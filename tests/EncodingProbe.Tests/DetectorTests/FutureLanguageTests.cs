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
