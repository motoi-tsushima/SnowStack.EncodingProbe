using System.Collections.Generic;
using System.Management.Automation;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 列名の重複のエラーを、どこで重複したか（ファイルのヘッダーか -Header か）とファイルのパスを示すメッセージにする（1.3.0 第六次 C。BUG-0045 (a)）。
    /// </summary>
    /// <remarks>
    /// 移植したコード（<c>Internal/Csv/ImportCsvHelper</c>）は標準と同じく列名だけのメッセージで例外を投げる。移植したコードは変えず、
    /// コマンド側で受け止めてから、重複した列名を同じ比較器（<see cref="CsvNameComparer"/>）で求め直してメッセージだけを差し替える。
    /// 例外の型（<see cref="ExtendedTypeSystemException"/>）は変えないので、エラーの ID・分類・対象は今までと同じ。
    /// </remarks>
    internal static class CsvDuplicateColumnName
    {
        /// <summary>
        /// ファイルのヘッダーで列名が重複した場合の例外
        /// </summary>
        public static ExtendedTypeSystemException InFile(string path, IList<string>? names, ExtendedTypeSystemException original)
        {
            string? name = Find(names);

            return name == null ? original : new ExtendedTypeSystemException(ValidationMessages.CsvHeaderAlreadyPresentInFile(path, name));
        }

        /// <summary>
        /// -Header の値で列名が重複した場合の例外
        /// </summary>
        public static ExtendedTypeSystemException InParameter(string path, IList<string>? names, ExtendedTypeSystemException original)
        {
            string? name = Find(names);

            return name == null ? original : new ExtendedTypeSystemException(ValidationMessages.CsvHeaderAlreadyPresentInParameter(path, name));
        }

        /// <summary>
        /// 最初に重複した列名を返す（移植元の ValidatePropertyNames と同じく、空の列名は数えない）。重複が無ければ null。
        /// </summary>
        private static string? Find(IList<string>? names)
        {
            if (names == null)
            {
                return null;
            }

            var seen = new HashSet<string>(CsvNameComparer.Instance);

            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(name) && !seen.Add(name))
                {
                    return name;
                }
            }

            return null;
        }
    }
}
