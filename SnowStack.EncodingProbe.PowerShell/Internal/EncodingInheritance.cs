using System.IO;
using System.Text;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 既存ファイルから文字エンコーディング・BOM・改行コードを継承する。
    /// </summary>
    /// <remarks>
    /// -EncodingFrom による参照ファイルからの継承と、書き込み系コマンドの
    /// -Encoding Auto（書き込み先・追記先からの継承）の両方がここを通る。
    /// </remarks>
    internal static class EncodingInheritance
    {
        /// <summary>
        /// 判定できるファイルサイズの上限。
        /// </summary>
        /// <remarks>
        /// 判定はファイル全体を対象とする必要がある。先頭の一定量だけで判定すると、
        /// 「1MB 分の英数字のあとに日本語のコメントが続くソースファイル」のような内容を
        /// US-ASCII と誤判定するため。
        /// 上限は byte 配列の最大長に由来する。クラスライブラリの判定処理は
        /// バイト配列を前提としており、これを超えるファイルは判定できない。
        /// </remarks>
        public const long MaxDetectableLength = 0x7FFFFFC7;

        /// <summary>
        /// 判定材料が無いかどうか（存在しないか、0バイトか）を返す。
        /// </summary>
        public static bool IsEmpty(string path)
        {
            var info = new FileInfo(path);

            return !info.Exists || info.Length == 0;
        }

        /// <summary>
        /// 参照元のファイルから継承する情報を求める。
        /// </summary>
        /// <param name="path">参照元ファイルの絶対パス。存在することを前提とする。</param>
        /// <exception cref="EncodingDetectionException">判定に失敗した場合</exception>
        public static EncodingSpec FromFile(string path)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            var info = new FileInfo(path);

            if (info.Length == 0)
            {
                // 0バイトのファイルからは何も判定できない。
                // 「存在しない」と同じ扱いにはせず、既定値で書き込めるようにする。
                return ForEmptySource();
            }

            if (info.Length > MaxDetectableLength)
            {
                throw new EncodingDetectionException(ValidationMessages.FileTooLargeToDetect(path), path);
            }

            EncodingInformation information = EncodingProbe.Detect(File.ReadAllBytes(path));

            if (information.CodePage < 0)
            {
                throw new EncodingDetectionException(ValidationMessages.DetectionFailed(path), path);
            }

            return EncodingVocabulary.FromEncodingInformation(information);
        }

        /// <summary>
        /// 継承元が空で、判定材料が無い場合に用いる既定値を返す。
        /// </summary>
        /// <remarks>
        /// 稼働している .NET ランタイムの既定エンコーディング（BOM 無し）とする。
        /// net48 / PowerShell 5.1 では ANSI コードページ、net10.0 / PowerShell 7.x では UTF-8 になり、
        /// いずれもそのホストで新規ファイルを作るときの既定と一致する。
        /// 改行コードは持たないため、呼び出し側では OS 既定（<c>Environment.NewLine</c>）に落ちる。
        /// </remarks>
        public static EncodingSpec ForEmptySource()
        {
            CodePagesProviderRegistration.EnsureRegistered();

            // Encoding.Default をそのまま使わずコードページから組み立て直すことで、
            // BOM を出力しないインスタンスであることを確定させる。
            Encoding encoding = EncodingVocabulary.BuildEncoding(Encoding.Default.CodePage, emitBom: false);

            return EncodingSpec.Create(encoding, emitBom: false, lineBreak: null);
        }
    }
}