using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 英語テキストのエンコーディング判定テスト
    /// </summary>
    /// <remarks>
    /// テストデータは TestData/English/ に配置されたテキストファイルを使用します。
    /// より複雑なテストデータを追加する場合は、TestData/English/ にファイルを追加し、
    /// 対応する [Theory][InlineData] エントリを追記してください。
    /// </remarks>
    public class EnglishEncodingTests
    {
        // ─── byte[] コンストラクタ経由 ─────────────────────────────────────────

        [Theory]
        [InlineData("sample_ascii.txt",    20127, "us-ascii", "ascii")]
        [InlineData("sample_utf8.txt",     65001, "utf-8",    "utf8NoBOM")]
        [InlineData("sample_utf8_bom.txt", 65001, "utf-8",    "utf8BOM")]
        public void Detection_English_FromByteArray(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
        {
            var buffer = TestDataHelper.ReadBytes("English", fileName);
            var detector = new EncodingDetector(buffer);

            var result = detector.Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingName);
            Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
        }

        // ─── Stream コンストラクタ経由 ─────────────────────────────────────────

        [Theory]
        [InlineData("sample_ascii.txt",    20127, "us-ascii", "ascii")]
        [InlineData("sample_utf8.txt",     65001, "utf-8",    "utf8NoBOM")]
        [InlineData("sample_utf8_bom.txt", 65001, "utf-8",    "utf8BOM")]
        public void Detection_English_FromStream(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
        {
            using var stream = TestDataHelper.OpenStream("English", fileName);
            var detector = new EncodingDetector(stream);

            var result = detector.Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingName);
            Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
        }

        // ─── filePath コンストラクタ経由 ───────────────────────────────────────

        [Theory]
        [InlineData("sample_ascii.txt",    20127, "us-ascii", "ascii")]
        [InlineData("sample_utf8.txt",     65001, "utf-8",    "utf8NoBOM")]
        [InlineData("sample_utf8_bom.txt", 65001, "utf-8",    "utf8BOM")]
        public void Detection_English_FromFilePath(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
        {
            var path = TestDataHelper.GetPath("English", fileName);
            var detector = new EncodingDetector(path);

            var result = detector.Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingName);
            Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
        }

        // ─── BOM フラグ検証 ────────────────────────────────────────────────────

        [Fact]
        public void Detection_English_Utf8WithBom_BomFlagIsTrue()
        {
            var buffer = TestDataHelper.ReadBytes("English", "sample_utf8_bom.txt");
            var result = new EncodingDetector(buffer).Detection();
            Assert.True(result.Bom);
        }

        [Fact]
        public void Detection_English_Utf8WithoutBom_BomFlagIsFalse()
        {
            var buffer = TestDataHelper.ReadBytes("English", "sample_utf8.txt");
            var result = new EncodingDetector(buffer).Detection();
            Assert.False(result.Bom);
        }
    }
}
