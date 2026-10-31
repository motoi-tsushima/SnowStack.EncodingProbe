using System.Linq;
using System.Text;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// バイト列だけでは区別できない組み合わせの曖昧解消で、標本を厳密に復号できない候補を選ばないことを検証する（1.3.0 第六次。BUG-0031）。
    /// </summary>
    /// <remarks>
    /// 第五次までは、短い EUC-JP の日本語を、改行が CRLF・改行なしのときに ja-JP で Shift_JIS（932）と判定していた
    /// （FC CB など、構造上は Shift_JIS の 2 バイト文字として成り立つが CP932 に割り当ての無いバイト列を含む）。
    /// 両方の候補を厳密に復号できる場合は、今までどおりカルチャーと改行で決める。両 TFM で同じ結果になる。
    /// </remarks>
    public class AmbiguityStrictDecodeTests
    {
        private const string Source = "日本語の文書です。今日は良い天気ですね。明日も晴れるでしょう。";

        /// <summary>
        /// テスト担当の再現手順の入力（「日本語の文書です」+ CRLF の 18 バイト）。改行によらず EUC-JP。
        /// </summary>
        [Theory]
        [InlineData(new byte[] { 0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC, 0xA4, 0xCE, 0xCA, 0xB8, 0xBD, 0xF1, 0xA4, 0xC7, 0xA4, 0xB9, 0x0D, 0x0A })]
        [InlineData(new byte[] { 0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC, 0xA4, 0xCE, 0xCA, 0xB8, 0xBD, 0xF1, 0xA4, 0xC7, 0xA4, 0xB9, 0x0A })]
        [InlineData(new byte[] { 0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC, 0xA4, 0xCE, 0xCA, 0xB8, 0xBD, 0xF1, 0xA4, 0xC7, 0xA4, 0xB9 })]
        public void ShortEucJp_NotDecodableAsCp932_IsEucJp(byte[] content)
        {
            foreach (DetectionStrategy strategy in new[] { DetectionStrategy.NativeOnly, DetectionStrategy.Combined })
            {
                Assert.Equal(20932, Detect(content, "ja-JP", strategy).CodePage);
            }
        }

        /// <summary>
        /// 長さを 1 文字ずつ変えても（4〜92 バイト）、改行によらず EUC-JP。
        /// </summary>
        [Theory]
        [InlineData("\r\n")]
        [InlineData("\n")]
        [InlineData("")]
        public void ShortEucJp_AllLengths_AreEucJp(string lineBreak)
        {
            Encoding euc = GetEncoding(20932);

            for (int n = 1; n <= Source.Length; n++)
            {
                byte[] content = euc.GetBytes(Source.Substring(0, n) + lineBreak);

                Assert.True(EncodingDetector.CanDecodeStrictly(content, content.Length, 20932) == true);
                Assert.Equal(20932, Detect(content, "ja-JP", DetectionStrategy.NativeOnly).CodePage);
            }
        }

        /// <summary>
        /// 逆向き（Shift_JIS の日本語を LF で終え、EUC-JP として成り立たないもの）は、今までどおり Shift_JIS。
        /// </summary>
        [Fact]
        public void ShortShiftJis_WithLf_IsStillShiftJis()
        {
            Encoding sjis = GetEncoding(932);

            for (int n = 1; n <= Source.Length; n++)
            {
                byte[] content = sjis.GetBytes(Source.Substring(0, n) + "\n");

                Assert.Equal(932, Detect(content, "ja-JP", DetectionStrategy.NativeOnly).CodePage);
            }
        }

        /// <summary>
        /// 両方の候補を厳密に復号できるバイト列は、今までどおり改行で決める（CRLF・改行なしの Windows は Shift_JIS、LF は EUC-JP）。
        /// B0 A1 は EUC-JP の「亜」、Shift_JIS の半角片仮名「ｰ｡」。
        /// </summary>
        [Theory]
        [InlineData(new byte[] { 0xB0, 0xA1, 0x0D, 0x0A }, 932)]
        [InlineData(new byte[] { 0xB0, 0xA1, 0x0A }, 20932)]
        public void BothDecodable_KeepsLineBreakRule(byte[] content, int expected)
        {
            Assert.True(EncodingDetector.CanDecodeStrictly(content, content.Length, 932) == true);
            Assert.True(EncodingDetector.CanDecodeStrictly(content, content.Length, 20932) == true);
            Assert.Equal(expected, Detect(content, "ja-JP", DetectionStrategy.NativeOnly).CodePage);
        }

        /// <summary>
        /// 候補を作れるかどうかは両 TFM で同じ。作れない候補（EUC-TW の 51950）は null（「復号できない」とみなさず、外さない）。
        /// </summary>
        [Theory]
        [InlineData(932, true)]
        [InlineData(20932, true)]
        [InlineData(949, true)]
        [InlineData(51949, true)]
        [InlineData(950, true)]
        [InlineData(51950, null)]
        public void CandidateAvailability_IsSameOnBothFrameworks(int codePage, bool? expected)
        {
            byte[] ascii = { 0x41, 0x42 };

            Assert.Equal(expected, EncodingDetector.CanDecodeStrictly(ascii, ascii.Length, codePage));
        }

        /// <summary>
        /// 復号を確かめるのは判定に使った標本の範囲だけ（長さの外のバイトは見ない）。
        /// </summary>
        [Fact]
        public void StrictDecode_LooksOnlyAtSample()
        {
            byte[] content = { 0xB0, 0xA1, 0xFC, 0xCB };

            Assert.True(EncodingDetector.CanDecodeStrictly(content, 2, 932) == true);
            Assert.True(EncodingDetector.CanDecodeStrictly(content, 4, 932) == false);
        }

        private static EncodingInformation Detect(byte[] content, string culture, DetectionStrategy strategy)
            => SnowStack.EncodingProbe.EncodingProbe.Detect(content, new EncodingDetectorOptions { Culture = culture, Strategy = strategy });

        private static Encoding GetEncoding(int codePage)
        {
#if NETFRAMEWORK
            return Encoding.GetEncoding(codePage);
#else
            return CodePagesEncodingProvider.Instance.GetEncoding(codePage)!;
#endif
        }
    }
}
