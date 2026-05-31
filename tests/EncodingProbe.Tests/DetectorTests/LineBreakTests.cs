using SnowStack.EncodingProbe;
using System.Text;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 改行コード判定テスト（EncodingInfomation.DetectLineBreak）
    /// </summary>
    /// <remarks>
    /// バイト列をインラインで組み立てているため、テストデータファイルは不要です。
    /// </remarks>
    public class LineBreakTests
    {
        private static byte[] Utf8Bytes(string text)
            => Encoding.UTF8.GetBytes(text);

        [Fact]
        public void DetectLineBreak_NullBuffer_ReturnsNone()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(null);
            Assert.Equal(LineBreakType.None, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_EmptyBuffer_ReturnsNone()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(Array.Empty<byte>());
            Assert.Equal(LineBreakType.None, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_OnlyCrLf_ReturnsCrLf()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(Utf8Bytes("line1\r\nline2\r\nline3"));
            Assert.Equal(LineBreakType.CrLf, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_OnlyLf_ReturnsLf()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(Utf8Bytes("line1\nline2\nline3"));
            Assert.Equal(LineBreakType.Lf, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_OnlyCr_ReturnsCr()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(Utf8Bytes("line1\rline2\rline3"));
            Assert.Equal(LineBreakType.Cr, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_MixedLfAndCrLf_ReturnsLfAndCrLf()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(Utf8Bytes("line1\nline2\r\nline3"));
            Assert.Equal(LineBreakType.LfAndCrLf, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_MixedCrAndCrLf_ReturnsCrAndCrLf()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(Utf8Bytes("line1\rline2\r\nline3"));
            Assert.Equal(LineBreakType.CrAndCrLf, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_MixedLfAndCr_ReturnsLfAndCr()
        {
            var info = new EncodingInfomation();
            info.DetectLineBreak(Utf8Bytes("line1\nline2\rline3"));
            Assert.Equal(LineBreakType.LfAndCr, info.LineBreak);
        }

        [Fact]
        public void DetectLineBreak_AllThreeMixed_ReturnsLfAndCrAndCrLf()
        {
            // LF×2, CR×2, CR-LF×2 を含む文字列（各1件では countLf==countCr==countCrLf になり CrLf に誤判定される）
            var info = new EncodingInfomation();
            info.DetectLineBreak(Utf8Bytes("line1\nline2\nline3\rline4\rline5\r\nline6\r\nline7"));
            Assert.Equal(LineBreakType.LfAndCrAndCrLf, info.LineBreak);
        }
    }
}
