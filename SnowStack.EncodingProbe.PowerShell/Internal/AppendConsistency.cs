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
        /// <remarks>
        /// どちらもコードページから例外フォールバックで組み立て直して符号化する（第五次）。
        /// 利用者が渡した Encoding のフォールバックの設定は使わない（例外フォールバックのインスタンスが生の例外を投げていた。BUG-0012）。
        /// 既存の文字エンコーディングで表現できない文字は、バイト列が一致しないものとして扱う
        /// （置換フォールバックでは '?' 同士が一致してしまっていた。BUG-0001）。
        /// 指定された文字エンコーディングで表現できるかどうかは、呼び出し側が先に確かめている。
        /// </remarks>
        public static bool ProducesSameBytes(Encoding specified, Encoding baseline, string chunk)
        {
            if (specified.CodePage == baseline.CodePage)
            {
                return true;
            }

            try
            {
                return BytesAreEqual(
                    EncodingVocabulary.BuildStrictEncoding(specified.CodePage).GetBytes(chunk),
                    EncodingVocabulary.BuildStrictEncoding(baseline.CodePage).GetBytes(chunk));
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        /// <summary>
        /// 追記の整合性検査のエラー（EncodingChangeOnAppend）に出す文字エンコーディングの名前（1.3.0 第六次 C。BUG-0045 (b)）
        /// </summary>
        /// <remarks>
        /// 統一語彙の名前（utf8NoBOM、unicodeBOM、shift_jis など）で示す。WebName（utf-8、utf-16）で示すと、
        /// ほかのメッセージ（DetectionVerificationFailed など）と表記がそろわず、utf-8 は書き込みでは拒否される名前でもあった。
        /// BOM の方針が決まっていない値（unicode などの裸の名前）は、BOM を付けない名前ではなく WebName で示す。
        /// </remarks>
        public static string DescribeForMessage(EncodingSpec spec)
        {
            int codePage = spec.Encoding!.CodePage;

            if (!spec.EmitBom.HasValue && EncodingVocabulary.IsUnicodeCodePage(codePage))
            {
                return spec.Encoding.WebName.ToLowerInvariant();
            }

            return EncodingVocabulary.GetUnifiedName(codePage, spec.EmitBom == true);
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
