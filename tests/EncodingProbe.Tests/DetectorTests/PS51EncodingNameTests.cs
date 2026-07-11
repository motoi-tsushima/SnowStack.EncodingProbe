#if NETFRAMEWORK
using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using System.Linq;
using System.Text;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// net48ビルド（Windows PowerShell 5.1向け）における
    /// EncodingDetector.PSEncodingName / UsePSName のマッピング仕様を検証するテスト。
    /// </summary>
    /// <remarks>
    /// PS5.1 の -Encoding は固定列挙値のみを受け付けるため、その一覧に一致する場合のみ
    /// 値を返し、一致しない場合は null を返す仕様になっている。
    /// </remarks>
    public class PS51EncodingNameTests
    {
        private static readonly byte[] BomUtf8 = { 0xEF, 0xBB, 0xBF };
        private static readonly byte[] BomUtf16Little = { 0xFF, 0xFE };
        private static readonly byte[] BomUtf16Big = { 0xFE, 0xFF };
        private static readonly byte[] BomUtf32Little = { 0xFF, 0xFE, 0x00, 0x00 };
        private static readonly byte[] BomUtf32Big = { 0x00, 0x00, 0xFE, 0xFF };

        private static EncodingInformation Detect(byte[] buffer)
            => new EncodingDetector(buffer).Detection();

        // ─── BOMなし Unicode系: null が返ること ────────────────────────────────

        // 7bitのみの文字列はASCIIと区別できないため、非ASCII文字を含む文字列を使用する

        [Fact]
        public void NoBom_Utf16LE_PSEncodingNameIsNull()
        {
            var bytes = Encoding.Unicode.GetBytes("こんにちは、PowerShell 5.1!");
            var result = Detect(bytes);

            Assert.Equal(1200, result.CodePage);
            Assert.False(result.Bom);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        [Fact]
        public void NoBom_Utf8_PSEncodingNameIsNull()
        {
            var bytes = Encoding.UTF8.GetBytes("こんにちは、PowerShell 5.1!");
            var result = Detect(bytes);

            Assert.Equal(65001, result.CodePage);
            Assert.False(result.Bom);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        // ─── BOM付き Unicode系: PS5.1の固定名が返ること ─────────────────────────

        [Fact]
        public void Bom_Utf16LE_ReturnsUnicode()
        {
            var bytes = BomUtf16Little
                .Concat(Encoding.Unicode.GetBytes("Hello"))
                .ToArray();
            var result = Detect(bytes);

            Assert.Equal(1200, result.CodePage);
            Assert.True(result.Bom);
            Assert.Equal("Unicode", result.PSEncodingName);
            Assert.True(result.UsePSName);
        }

        [Fact]
        public void Bom_Utf16BE_ReturnsBigEndianUnicode()
        {
            var bytes = BomUtf16Big
                .Concat(Encoding.BigEndianUnicode.GetBytes("Hello"))
                .ToArray();
            var result = Detect(bytes);

            Assert.Equal(1201, result.CodePage);
            Assert.True(result.Bom);
            Assert.Equal("BigEndianUnicode", result.PSEncodingName);
            Assert.True(result.UsePSName);
        }

        [Fact]
        public void Bom_Utf32LE_ReturnsUTF32()
        {
            var bytes = BomUtf32Little
                .Concat(new UTF32Encoding(bigEndian: false, byteOrderMark: false).GetBytes("Hello"))
                .ToArray();
            var result = Detect(bytes);

            Assert.Equal(12000, result.CodePage);
            Assert.True(result.Bom);
            Assert.Equal("UTF32", result.PSEncodingName);
            Assert.True(result.UsePSName);
        }

        [Fact]
        public void Bom_Utf32BE_ReturnsBigEndianUTF32()
        {
            var bytes = BomUtf32Big
                .Concat(new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes("Hello"))
                .ToArray();
            var result = Detect(bytes);

            Assert.Equal(12001, result.CodePage);
            Assert.True(result.Bom);
            Assert.Equal("BigEndianUTF32", result.PSEncodingName);
            Assert.True(result.UsePSName);
        }

        [Fact]
        public void Bom_Utf8_ReturnsUTF8()
        {
            var bytes = BomUtf8
                .Concat(Encoding.UTF8.GetBytes("Hello"))
                .ToArray();
            var result = Detect(bytes);

            Assert.Equal(65001, result.CodePage);
            Assert.True(result.Bom);
            Assert.Equal("UTF8", result.PSEncodingName);
            Assert.True(result.UsePSName);
        }

        // ─── ASCII: 常に "Ascii" が返ること ────────────────────────────────────

        [Fact]
        public void Ascii_ReturnsAscii()
        {
            var bytes = Encoding.ASCII.GetBytes("Hello, PowerShell 5.1!");
            var result = Detect(bytes);

            Assert.Equal(20127, result.CodePage);
            Assert.Equal("Ascii", result.PSEncodingName);
            Assert.True(result.UsePSName);
        }

        // ─── レガシーコードページ（Shift-JIS等）: null が返ること ───────────────

        [Fact]
        public void Legacy_ShiftJis_PSEncodingNameIsNull()
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", "sample_shiftjis.txt");
            var result = Detect(buffer);

            Assert.Equal(932, result.CodePage);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        [Fact]
        public void Legacy_EucJp_PSEncodingNameIsNull()
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", "sample_eucjp.txt");
            var result = Detect(buffer);

            Assert.Equal(20932, result.CodePage);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }
    }
}
#endif
