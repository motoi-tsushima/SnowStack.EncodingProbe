using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 日本語テキストのエンコーディング判定テスト
    /// </summary>
    /// <remarks>
    /// テストデータは TestData/Japanese/ に配置されたテキストファイルを使用します。
    /// より複雑なテストデータを追加する場合は、TestData/Japanese/ にファイルを追加し、
    /// 対応する [Theory][InlineData] エントリを追記してください。
    /// </remarks>
    public class JapaneseEncodingTests
    {
        // ─── byte[] コンストラクタ経由 ─────────────────────────────────────────

        [Theory]
        [InlineData("sample_utf8.txt",     65001, "utf-8",       "utf8NoBOM")]
        [InlineData("sample_utf8_bom.txt", 65001, "utf-8",       "utf8BOM")]
        [InlineData("sample_shiftjis.txt", 932,   "shift_jis",   "932")]
        [InlineData("sample_eucjp.txt",    20932, "euc-jp",      "20932")]
        [InlineData("sample_jis.txt",      50220, "iso-2022-jp", "50220")]
        public void Detection_Japanese_FromByteArray(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", fileName);
            var detector = new EncodingDetector(buffer);

            var result = detector.Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
            Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
        }

        // ─── Stream コンストラクタ経由 ─────────────────────────────────────────

        [Theory]
        [InlineData("sample_utf8.txt",     65001, "utf-8",       "utf8NoBOM")]
        [InlineData("sample_utf8_bom.txt", 65001, "utf-8",       "utf8BOM")]
        [InlineData("sample_shiftjis.txt", 932,   "shift_jis",   "932")]
        [InlineData("sample_eucjp.txt",    20932, "euc-jp",      "20932")]
        [InlineData("sample_jis.txt",      50220, "iso-2022-jp", "50220")]
        public void Detection_Japanese_FromStream(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
        {
            using var stream = TestDataHelper.OpenStream("Japanese", fileName);
            var detector = new EncodingDetector(stream);

            var result = detector.Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
            Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
        }

        // ─── filePath コンストラクタ経由 ───────────────────────────────────────

        [Theory]
        [InlineData("sample_utf8.txt",     65001, "utf-8",       "utf8NoBOM")]
        [InlineData("sample_utf8_bom.txt", 65001, "utf-8",       "utf8BOM")]
        [InlineData("sample_shiftjis.txt", 932,   "shift_jis",   "932")]
        [InlineData("sample_eucjp.txt",    20932, "euc-jp",      "20932")]
        [InlineData("sample_jis.txt",      50220, "iso-2022-jp", "50220")]
        public void Detection_Japanese_FromFilePath(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
        {
            var path = TestDataHelper.GetPath("Japanese", fileName);
            var detector = new EncodingDetector(path);

            var result = detector.Detection();

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedEncodingName, result.EncodingWebName);
            Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
        }

        // ─── BOM フラグ検証 ────────────────────────────────────────────────────

        [Fact]
        public void Detection_Japanese_Utf8WithBom_BomFlagIsTrue()
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", "sample_utf8_bom.txt");
            var result = new EncodingDetector(buffer).Detection();
            Assert.True(result.Bom);
        }

        [Fact]
        public void Detection_Japanese_Utf8WithoutBom_BomFlagIsFalse()
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", "sample_utf8.txt");
            var result = new EncodingDetector(buffer).Detection();
            Assert.False(result.Bom);
        }
    }
}
