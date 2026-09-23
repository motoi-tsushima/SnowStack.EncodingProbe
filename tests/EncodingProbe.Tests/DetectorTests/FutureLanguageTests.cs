using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 韓国語テキストのエンコーディング判定テスト
    /// </summary>
    /// <remarks>
    /// テストデータは TestData/Korean/ に配置されています。
    /// EUC-KR と CP949 の両方に該当する場合は CP949 と判定します。
    /// </remarks>
    public class KoreanEncodingTests
    {
        [Theory]
        [InlineData("sample_utf8.txt",  65001, "utf-8")]
        [InlineData("sample_euckr.txt", 949,   "cp949")]
        [InlineData("sample_cp949.txt", 949,   "cp949")]
        public void Detection_Korean_FromByteArray(string fileName, int expectedCodePage, string expectedEncodingName)
        {
            var buffer = TestDataHelper.ReadBytes("Korean", fileName);
            var result = new EncodingDetector(buffer).Detection("ko-KR");

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
        }
    }

    /// <summary>
    /// 繁体字中国語テキストのエンコーディング判定テスト（台湾・香港）
    /// </summary>
    /// <remarks>
    /// テストデータは TestData/Chinese_Traditional/ に配置されています。
    /// EUC-TW と CP950(Big5) の両方に該当する場合は CP950(Big5) と判定します。
    /// </remarks>
    public class ChineseTraditionalEncodingTests
    {
        [Theory]
        [InlineData("sample_utf8.txt",  65001, "utf-8")]
        [InlineData("sample_big5.txt",  950,   "big5")]
        [InlineData("sample_euctw.txt", 950,   "big5")]
        public void Detection_ChineseTraditional_FromByteArray(string fileName, int expectedCodePage, string expectedEncodingName)
        {
            var buffer = TestDataHelper.ReadBytes("Chinese_Traditional", fileName);
            var result = new EncodingDetector(buffer).Detection("zh-TW");

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
        }
    }

    /// <summary>
    /// 繁体字中国語テキストのエンコーディング判定テスト（香港・マカオ・広東語）
    /// </summary>
    /// <remarks>
    /// テストデータは TestData/Chinese_HongKong/ に配置されています（tools/New-EncodingTestData.ps1 が生成）。
    /// sample_big5hkscs.txt は HKSCS 固有領域（88 62 / 8B F8 / FA 5F / FE 52）を含みます。
    ///
    /// 台湾 Big5 と香港 Big5 はバイト列から区別しません。HKSCS 固有字を含んでいても 950 / big5 を返します。
    /// HKSCS 固有字は私用領域として復号されます（docs/私用領域の扱い_方針草案.md）。
    /// </remarks>
    public class ChineseHongKongEncodingTests
    {
        public static TheoryData<string, string> Samples()
        {
            var data = new TheoryData<string, string>();
            foreach (var culture in new[] { "zh-HK", "zh-MO", "zh-Hant-HK", "yue", "yue-Hant-HK" })
            {
                data.Add("sample_big5.txt", culture);
                data.Add("sample_big5hkscs.txt", culture);
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(Samples))]
        public void Detection_ChineseHongKong_NativeOnly_Returns950(string fileName, string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Chinese_HongKong", fileName);
            var result = new EncodingDetector(buffer).Detection(culture);

            Assert.Equal(950, result.CodePage);
            Assert.Equal("big5", result.EncodingWebName);
        }

        [Theory]
        [MemberData(nameof(Samples))]
        public void Detect_ChineseHongKong_Combined_Returns950(string fileName, string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Chinese_HongKong", fileName);
            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(
                buffer, new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined });

            Assert.Equal(950, result.CodePage);
            Assert.Equal("big5", result.EncodingWebName);
        }

        [Theory]
        [InlineData("zh-HK")]
        [InlineData("zh-Hant-HK")]
        public void Detection_ChineseHongKong_Utf8(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Chinese_HongKong", "sample_utf8.txt");
            var result = new EncodingDetector(buffer).Detection(culture);

            Assert.Equal(65001, result.CodePage);
        }

        /// <summary>
        /// 香港では EUC-TW を候補にしないが、EUC-TW のバイト列は Big5 としても成立するので結果は台湾と同じ
        /// </summary>
        [Fact]
        public void Detection_ChineseHongKong_EucTwSample_SameAsTaiwan()
        {
            var buffer = TestDataHelper.ReadBytes("Chinese_Traditional", "sample_euctw.txt");

            var hongKong = new EncodingDetector(buffer).Detection("zh-HK");
            var taiwan = new EncodingDetector(buffer).Detection("zh-TW");

            Assert.Equal(950, hongKong.CodePage);
            Assert.Equal(taiwan.CodePage, hongKong.CodePage);
        }

        /// <summary>
        /// HKSCS 固有字は例外を出さずに私用領域へ写され、同じバイト列へ戻ること（私用領域の扱い 4.1）
        /// </summary>
        [Fact]
        public void Big5Hkscs_DecodesToPrivateUseArea_AndRoundTrips()
        {
            RegisterCodePages();
            var buffer = TestDataHelper.ReadBytes("Chinese_HongKong", "sample_big5hkscs.txt");
            var cp950 = System.Text.Encoding.GetEncoding(950);

            var text = cp950.GetString(buffer);

            Assert.Contains(text, c => c >= '' && c <= '');
            Assert.Equal(buffer, cp950.GetBytes(text));
        }

        private static void RegisterCodePages()
        {
#if NETCOREAPP
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
#endif
        }
    }

    /// <summary>
    /// 簡体字中国語テキストのエンコーディング判定テスト
    /// </summary>
    /// <remarks>
    /// テストデータは TestData/Chinese_Simplified/ に配置してください。
    /// </remarks>
    public class ChineseSimplifiedEncodingTests
    {
        [Theory]
        [InlineData("sample_utf8.txt",    65001, "utf-8")]
        [InlineData("sample_gb2312.txt",  936,   "gbk")]
        [InlineData("sample_gbk.txt",     54936, "gb18030")]
        [InlineData("sample_gb18030.txt", 54936, "gb18030")]
        public void Detection_ChineseSimplified_FromByteArray(string fileName, int expectedCodePage, string expectedEncodingName)
        {
            var buffer = TestDataHelper.ReadBytes("Chinese_Simplified", fileName);
            var result = new EncodingDetector(buffer).Detection("zh-CN");

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
        }
    }
}
