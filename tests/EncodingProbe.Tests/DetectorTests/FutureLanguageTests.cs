using System.Globalization;
using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 韓国語テキストのエンコーディング判定テスト
    /// </summary>
    /// <remarks>
    /// 現在は精度が不十分なため Skip しています。
    /// 対応が完了したら Skip 属性を外してください。
    /// テストデータは TestData/Korean/ に配置してください。
    /// </remarks>
    public class KoreanEncodingTests
    {
        private const string SkipReason = "未対応：韓国語エンコーディング判定は将来実装予定";

        [Theory(Skip = SkipReason)]
        [InlineData("sample_utf8.txt",  65001, "utf-8")]
        [InlineData("sample_euckr.txt", 51949, "euc-kr")]
        [InlineData("sample_cp949.txt", 949,   "cp949")]
        public void Detection_Korean_FromByteArray(string fileName, int expectedCodePage, string expectedEncodingName)
        {
            var buffer = TestDataHelper.ReadBytes("Korean", fileName);
            var result = new EncodingDetector(buffer).Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
        }
    }

    /// <summary>
    /// 繁体字中国語テキストのエンコーディング判定テスト（台湾・香港）
    /// </summary>
    /// <remarks>
    /// 現在は精度が不十分なため Skip しています。
    /// 対応が完了したら Skip 属性を外してください。
    /// テストデータは TestData/Chinese_Traditional/ に配置してください。
    /// </remarks>
    public class ChineseTraditionalEncodingTests
    {
        private const string SkipReason = "未対応：繁体字中国語エンコーディング判定は将来実装予定";

        [Theory(Skip = SkipReason)]
        [InlineData("sample_utf8.txt",  65001, "utf-8")]
        [InlineData("sample_big5.txt",  950,   "big5")]
        [InlineData("sample_euctw.txt", 51950, "euc-tw")]
        public void Detection_ChineseTraditional_FromByteArray(string fileName, int expectedCodePage, string expectedEncodingName)
        {
            var buffer = TestDataHelper.ReadBytes("Chinese_Traditional", fileName);
            var result = new EncodingDetector(buffer).Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
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
