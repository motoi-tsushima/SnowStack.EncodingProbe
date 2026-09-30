using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 追記しても既存ファイルの一貫性が保たれるかどうかを、実際に書き出されるバイト列で判定する。
    /// </summary>
    /// <remarks>
    /// 安全性を決めているのはエンコーディング名の一致ではなく、書き出されるバイト列である（1.1.0 仕様書 6.1）。
    /// 追記する文字列は手元にあるため、両方の文字エンコーディングで符号化して比較できる。
    /// 一致すれば、指定された文字エンコーディングで追記した結果は
    /// 既存の文字エンコーディングで追記したのとバイト単位で同一になる。
    /// <br/>
    /// Add-ProbedContent と Out-ProbedFile -Append が共用する。
    /// </remarks>
    internal static class AppendConsistency
    {
        /// <summary>
        /// 指定された文字エンコーディングで書いた結果が、既存の文字エンコーディングで書いた結果と同じバイト列になるか
        /// </summary>
        /// <param name="specified">書き込みに使う文字エンコーディング</param>
        /// <param name="baseline">追記先の既存の文字エンコーディング</param>
        /// <param name="chunk">書き込む内容（改行を含む）</param>
        public static bool ProducesSameBytes(Encoding specified, Encoding baseline, string chunk)
        {
            if (specified.CodePage == baseline.CodePage)
            {
                return true;
            }

            return BytesAreEqual(specified.GetBytes(chunk), baseline.GetBytes(chunk));
        }

        /// <summary>
        /// バイト列が完全に一致するかどうか
        /// </summary>
        private static bool BytesAreEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
