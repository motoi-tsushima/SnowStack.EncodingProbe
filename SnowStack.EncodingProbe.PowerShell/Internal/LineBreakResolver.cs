using System;
using System.Text;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 書き込み時に使用する改行コードを決定する。
    /// </summary>
    /// <remarks>
    /// 決定順序は次のとおり。
    /// 1. -LineBreak が明示指定されている（Auto 以外）→ その指定に従う
    /// 2. 参照情報がある（-EncodingFrom / -Encoding Auto / -Encoding &lt;EncodingInformation&gt;）
    ///    → 参照元の改行を継承する（OSを問わない）
    /// 3. 参照情報が無い → OS既定（Environment.NewLine）
    /// </remarks>
    internal static class LineBreakResolver
    {
        /// <summary>CR-LF (Windows)</summary>
        public const string CrLf = "\r\n";

        /// <summary>LF (Unix/macOS)</summary>
        public const string Lf = "\n";

        /// <summary>CR (旧Macintosh)</summary>
        public const string Cr = "\r";

        /// <summary>
        /// 使用する改行文字列を決定する
        /// </summary>
        /// <param name="option">-LineBreak の指定値</param>
        /// <param name="reference">参照情報から得られた改行コード。無い場合は null。</param>
        public static string Resolve(LineBreakOption option, LineBreakType? reference)
        {
            if (option != LineBreakOption.Auto)
            {
                return FromOption(option);
            }

            if (reference.HasValue)
            {
                string? inherited = FromDetected(reference.Value);

                if (inherited != null)
                {
                    return inherited;
                }
            }

            return Environment.NewLine;
        }

        /// <summary>
        /// -LineBreak の明示指定を改行文字列に変換する
        /// </summary>
        public static string FromOption(LineBreakOption option)
        {
            switch (option)
            {
                case LineBreakOption.CrLf:
                    return CrLf;
                case LineBreakOption.Lf:
                    return Lf;
                case LineBreakOption.Cr:
                    return Cr;
                default:
                    return Environment.NewLine;
            }
        }

        /// <summary>
        /// 文字列の中のすべての改行（CR-LF / LF / CR）を、指定の改行に揃える（Convert-ProbedContent 用）。
        /// </summary>
        /// <remarks>
        /// CR-LF は 1 個の改行として扱う。U+2028 / U+2029 / U+0085 などは改行として扱わない。
        /// 最後の行の末尾に改行があるかないかは変えない（改行を足したり削ったりしない）。
        /// 書き込み系の -LineBreak（要素の後ろに付ける改行だけを決める）とは役割が違う。
        /// </remarks>
        public static string Normalize(string text, string lineBreak)
        {
            var builder = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    builder.Append(lineBreak);
                }
                else if (c == '\n')
                {
                    builder.Append(lineBreak);
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 文字列に含まれる改行の種類を <see cref="LineBreakType"/> で表す（Convert-ProbedContent -PassThru 用）。
        /// </summary>
        /// <remarks>
        /// 値は Resolve-Encoding の LineBreak と同じ体系にそろえる。
        /// </remarks>
        public static LineBreakType Classify(string text)
        {
            bool crLf = false;
            bool lf = false;
            bool cr = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crLf = true;
                        i++;
                    }
                    else
                    {
                        cr = true;
                    }
                }
                else if (c == '\n')
                {
                    lf = true;
                }
            }

            if (lf && cr && crLf)
            {
                return LineBreakType.LfAndCrAndCrLf;
            }

            if (lf && crLf)
            {
                return LineBreakType.LfAndCrLf;
            }

            if (cr && crLf)
            {
                return LineBreakType.CrAndCrLf;
            }

            if (lf && cr)
            {
                return LineBreakType.LfAndCr;
            }

            if (crLf)
            {
                return LineBreakType.CrLf;
            }

            if (lf)
            {
                return LineBreakType.Lf;
            }

            return cr ? LineBreakType.Cr : LineBreakType.None;
        }

        /// <summary>
        /// 判定結果の改行コードを、書き込みに使う改行文字列へ変換する。
        /// 継承できない場合（改行が存在しない場合）は null を返す。
        /// </summary>
        /// <remarks>
        /// 混在改行の場合、書き込みに使える改行はひとつしか選べない。
        /// <see cref="EncodingInformation"/> は改行の出現回数を保持しないため多数決は取れず、
        /// 「CR-LF を含むなら CR-LF、含まないなら LF」という規則で決定する。
        /// OSに依存しないため、PowerShell 5.1 と 7.x で同一の結果になる。
        /// </remarks>
        public static string? FromDetected(LineBreakType lineBreak)
        {
            switch (lineBreak)
            {
                case LineBreakType.CrLf:
                    return CrLf;
                case LineBreakType.Lf:
                    return Lf;
                case LineBreakType.Cr:
                    return Cr;

                // 混在：CR-LF を含む
                case LineBreakType.LfAndCrLf:
                case LineBreakType.CrAndCrLf:
                case LineBreakType.LfAndCrAndCrLf:
                    return CrLf;

                // 混在：CR-LF を含まない
                case LineBreakType.LfAndCr:
                    return Lf;

                // 改行が存在しないため継承できない
                case LineBreakType.None:
                default:
                    return null;
            }
        }
    }
}