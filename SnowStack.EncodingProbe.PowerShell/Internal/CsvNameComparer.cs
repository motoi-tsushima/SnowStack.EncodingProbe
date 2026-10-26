using System;
using System.Collections.Generic;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// CSV の列名の比較（大文字小文字を区別しない）。PowerShell 5.1 でも 7.x（.NET 10）の
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> と同じ結果にする（1.3.0 第六次。BUG-0020）。
    /// </summary>
    /// <remarks>
    /// 移植元（PowerShell 7.x の CsvCommands.cs）は <see cref="StringComparer.OrdinalIgnoreCase"/> で列名を比べる。
    /// そのまま 5.1 で動かすと .NET Framework の規則になり、ǅ / ǆ、語末のシグマ ς / Σ、補助面の 𐐀 / 𐐨 などで、
    /// 列名の重複の検査（Import-ProbedCsv）と追記の列の照合（Export-ProbedCsv -Append）の結果がホストで違っていた。
    /// <list type="bullet">
    /// <item>net10.0（7.x）: 実行環境の <see cref="StringComparer.OrdinalIgnoreCase"/> をそのまま使う</item>
    /// <item>net48（5.1）: .NET 10 の同値類から作った表（<see cref="CsvNameCaseTable"/>。tools/New-CsvNameCaseTable.ps1 が生成）で比べる</item>
    /// </list>
    /// 比べ方は .NET 10 と同じく、UTF-16 の長さが同じ文字列を、符号位置ごと（正しいサロゲートペアは 1 つの符号位置、
    /// 対になっていないサロゲートはそのコード単位）に同値類の代表へ写して比べる。
    /// 表が .NET 10 の OrdinalIgnoreCase と全符号位置で一致することは、CsvNameComparerTests が net10.0 で確かめている。
    /// <br/>
    /// 使うのは列名どうしの比較だけ（Internal/Csv の移植したコードの 3 か所）。PSObject のメンバー名との突き合わせ
    /// （<see cref="CsvPropertyViews"/>）は、ホストの PSObject と同じ規則でなければならないので使わない。
    /// </remarks>
    internal sealed class CsvNameComparer : IEqualityComparer<string>
    {
        /// <summary>表で比べる比較器（net48 の <see cref="Instance"/>。net10.0 ではテストが表を確かめるために使う）</summary>
        internal static readonly CsvNameComparer Table = new CsvNameComparer();

        /// <summary>列名の比較に使う比較器</summary>
#if NETFRAMEWORK
        public static readonly IEqualityComparer<string> Instance = Table;
#else
        public static readonly IEqualityComparer<string> Instance = StringComparer.OrdinalIgnoreCase;
#endif

        private static readonly Dictionary<int, int> Representatives = BuildRepresentatives();

        private CsvNameComparer()
        {
        }

        /// <summary>
        /// 符号位置を、.NET 10 の OrdinalIgnoreCase の同値類の代表（最小の符号位置）に写す。
        /// </summary>
        internal static int ToRepresentative(int codePoint)
            => Representatives.TryGetValue(codePoint, out int representative) ? representative : codePoint;

        public bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null || x.Length != y.Length)
            {
                return false;
            }

            int index = 0;

            while (index < x.Length)
            {
                int left = ReadCodePoint(x, index, out int leftWidth);
                int right = ReadCodePoint(y, index, out int rightWidth);

                if (leftWidth != rightWidth || ToRepresentative(left) != ToRepresentative(right))
                {
                    return false;
                }

                index += leftWidth;
            }

            return true;
        }

        public int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            unchecked
            {
                int hash = (int)2166136261;
                int index = 0;

                while (index < obj.Length)
                {
                    int codePoint = ReadCodePoint(obj, index, out int width);
                    hash = (hash ^ ToRepresentative(codePoint)) * 16777619;
                    index += width;
                }

                return hash;
            }
        }

        /// <summary>
        /// index の位置の符号位置を読む。正しいサロゲートペアは 1 つの符号位置（幅 2）、それ以外はコード単位そのもの（幅 1）。
        /// </summary>
        private static int ReadCodePoint(string text, int index, out int width)
        {
            char c = text[index];

            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                width = 2;
                return char.ConvertToUtf32(c, text[index + 1]);
            }

            width = 1;
            return c;
        }

        private static Dictionary<int, int> BuildRepresentatives()
        {
            int[] pairs = CsvNameCaseTable.Pairs;
            var map = new Dictionary<int, int>(pairs.Length / 2);

            for (int i = 0; i < pairs.Length; i += 2)
            {
                map.Add(pairs[i], pairs[i + 1]);
            }

            return map;
        }
    }
}
