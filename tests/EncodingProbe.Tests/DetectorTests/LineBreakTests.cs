using SnowStack.EncodingProbe;
using System.Text;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 改行コード判定テスト（EncodingDetector.DetectLineBreak）
    /// </summary>
    /// <remarks>
    /// バイト列をインラインで組み立てているため、テストデータファイルは不要です。
    /// </remarks>
    public class LineBreakTests
    {
        private static byte[] Utf8Bytes(string text)
            => Encoding.UTF8.GetBytes(text);

        private static LineBreakType Detect(byte[] buffer)
            => new EncodingDetector(buffer).DetectLineBreak(buffer);

        [Fact]
        public void DetectLineBreak_NullBuffer_ReturnsNone()
        {
            var result = new EncodingDetector(Utf8Bytes("x")).DetectLineBreak(null);
            Assert.Equal(LineBreakType.None, result);
        }

        [Fact]
        public void DetectLineBreak_EmptyBuffer_ReturnsNone()
        {
            var result = new EncodingDetector(Utf8Bytes("x")).DetectLineBreak(Array.Empty<byte>());
            Assert.Equal(LineBreakType.None, result);
        }

        [Fact]
        public void DetectLineBreak_OnlyCrLf_ReturnsCrLf()
        {
            Assert.Equal(LineBreakType.CrLf, Detect(Utf8Bytes("line1\r\nline2\r\nline3")));
        }

        [Fact]
        public void DetectLineBreak_OnlyLf_ReturnsLf()
        {
            Assert.Equal(LineBreakType.Lf, Detect(Utf8Bytes("line1\nline2\nline3")));
        }

        [Fact]
        public void DetectLineBreak_OnlyCr_ReturnsCr()
        {
            Assert.Equal(LineBreakType.Cr, Detect(Utf8Bytes("line1\rline2\rline3")));
        }

        [Fact]
        public void DetectLineBreak_MixedLfAndCrLf_ReturnsLfAndCrLf()
        {
            Assert.Equal(LineBreakType.LfAndCrLf, Detect(Utf8Bytes("line1\nline2\r\nline3")));
        }

        [Fact]
        public void DetectLineBreak_MixedCrAndCrLf_ReturnsCrAndCrLf()
        {
            Assert.Equal(LineBreakType.CrAndCrLf, Detect(Utf8Bytes("line1\rline2\r\nline3")));
        }

        [Fact]
        public void DetectLineBreak_MixedLfAndCr_ReturnsLfAndCr()
        {
            Assert.Equal(LineBreakType.LfAndCr, Detect(Utf8Bytes("line1\nline2\rline3")));
        }

        [Fact]
        public void DetectLineBreak_AllThreeMixed_ReturnsLfAndCrAndCrLf()
        {
            // LF×2, CR×2, CR-LF×2 を含む文字列（各1件では countLf==countCr==countCrLf になり CrLf に誤判定される）
            Assert.Equal(LineBreakType.LfAndCrAndCrLf,
                Detect(Utf8Bytes("line1\nline2\nline3\rline4\rline5\r\nline6\r\nline7")));
        }

        [Fact]
        public void DetectLineBreak_Utf16LE_OnlyCrLf_ReturnsCrLf()
        {
            var bytes = Encoding.Unicode.GetBytes("line1\r\nline2\r\nline3");
            Assert.Equal(LineBreakType.CrLf, Detect(bytes));
        }

        [Fact]
        public void DetectLineBreak_Utf16BE_OnlyCrLf_ReturnsCrLf()
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes("line1\r\nline2\r\nline3");
            Assert.Equal(LineBreakType.CrLf, Detect(bytes));
        }

        [Fact]
        public void DetectLineBreak_Utf32LE_OnlyCrLf_ReturnsCrLf()
        {
            var bytes = Encoding.UTF32.GetBytes("line1\r\nline2\r\nline3");
            Assert.Equal(LineBreakType.CrLf, Detect(bytes));
        }

        [Fact]
        public void DetectLineBreak_Utf32BE_OnlyCrLf_ReturnsCrLf()
        {
            var bytes = new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes("line1\r\nline2\r\nline3");
            Assert.Equal(LineBreakType.CrLf, Detect(bytes));
        }
    }
}
