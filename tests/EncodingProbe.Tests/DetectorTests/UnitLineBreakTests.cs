using SnowStack.EncodingProbe;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// UTF-16 / UTF-32 と判定したときの改行コードを、その単位で数えることのテスト（1.3.0 第五次）
    /// </summary>
    /// <remarks>
    /// 1.3.0 第四次まではバイト単位で 0x0A / 0x0D を数えていたため、
    /// 「上」U+4E0A（UTF-16LE で 0A 4E）・「《」U+300A（0A 30）・「不」U+4E0D（0D 4E）を含む UTF-16 / UTF-32 では、
    /// CR-LF のファイルが LfAndCrAndCrLf、LF・CR のファイルが LfAndCr と報告されていた。
    /// </remarks>
    public class UnitLineBreakTests
    {
        /// <summary>「上」「《」「不」を、各行の先頭・途中・末尾に含む本文。日本語を含むので BOM 無しでも UTF-16 / UTF-32 と判定される</summary>
        private static readonly string[] Lines = { "日本語の上《不》,B", "1上,2《", "不3,4上" };

        public static IEnumerable<object[]> Cases()
        {
            foreach (string form in new[] { "u16le", "u16be", "u32le", "u32be" })
            foreach (bool bom in new[] { false, true })
            foreach (LineBreakType lineBreak in new[] { LineBreakType.CrLf, LineBreakType.Lf, LineBreakType.Cr })
            {
                yield return new object[] { form, bom, lineBreak };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Detect_ReportsActualLineBreak(string form, bool bom, LineBreakType lineBreak)
        {
            Encoding encoding = GetEncoding(form, bom);
            byte[] bytes = Build(encoding, lineBreak);

            EncodingInformation result = SnowStack.EncodingProbe.EncodingProbe.Detect(
                bytes, new EncodingDetectorOptions { Culture = "ja-JP" });

            Assert.Equal(encoding.CodePage, result.CodePage);
            Assert.Equal(bom, result.Bom);
            Assert.Equal(lineBreak, result.LineBreak);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Detection_ReportsActualLineBreak(string form, bool bom, LineBreakType lineBreak)
        {
            Encoding encoding = GetEncoding(form, bom);
            byte[] bytes = Build(encoding, lineBreak);

            EncodingInformation result = new EncodingDetector(bytes).Detection("ja-JP");

            Assert.Equal(encoding.CodePage, result.CodePage);
            Assert.Equal(lineBreak, result.LineBreak);
        }

        /// <summary>
        /// 英数字だけの BOM 無し UTF-16 / UTF-32（0x00 を含む ASCII の分岐で判定されるもの）も、その単位で数える
        /// </summary>
        [Theory]
        [InlineData("u16le")]
        [InlineData("u16be")]
        [InlineData("u32le")]
        [InlineData("u32be")]
        public void Detection_NulPaddedUnicode_CountsByUnit(string form)
        {
            Encoding encoding = GetEncoding(form, bom: false);
            byte[] bytes = encoding.GetBytes("A,B\r1,2\r3,4\r");

            EncodingInformation result = new EncodingDetector(bytes).Detection("ja-JP");

            Assert.Equal(encoding.CodePage, result.CodePage);
            Assert.Equal(LineBreakType.Cr, result.LineBreak);
        }

        /// <summary>
        /// 単位で数える処理そのもの: 文字の一部のバイトを改行と数えない。CR と LF の単位が続けば CR-LF
        /// </summary>
        [Theory]
        [InlineData(1200, "上\r不\r", LineBreakType.Cr)]
        [InlineData(1201, "਍ഊ\n", LineBreakType.Lf)]
        [InlineData(12000, "上\r\n《\r\n", LineBreakType.CrLf)]
        [InlineData(12001, "਀\u000D", LineBreakType.Cr)]
        [InlineData(1200, "上不《", LineBreakType.None)]
        [InlineData(1200, "a\nb\r\nc\r", LineBreakType.LfAndCrAndCrLf)]
        public void DetectLineBreak_ByUnit(int codePage, string text, LineBreakType expected)
        {
            byte[] bytes = GetEncodingByCodePage(codePage).GetBytes(text);

            Assert.Equal(expected, EncodingDetector.DetectLineBreak(bytes, bytes.Length, codePage));
        }

        /// <summary>
        /// 末尾の端数のバイトは数えない（UTF-16LE の LF の後ろに 0A だけが残る場合）
        /// </summary>
        [Fact]
        public void DetectLineBreak_ByUnit_IgnoresTrailingPartialUnit()
        {
            byte[] bytes = { 0x41, 0x00, 0x0D, 0x00, 0x0A };

            Assert.Equal(LineBreakType.Cr, EncodingDetector.DetectLineBreak(bytes, bytes.Length, 1200));
        }

        /// <summary>
        /// 1 バイトの文字エンコーディングでは、公開の DetectLineBreak() と同じくバイト単位で数える
        /// </summary>
        [Fact]
        public void DetectLineBreak_SingleByte_SameAsPublicMethod()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("a\nb\r\nc\r");

            Assert.Equal(
                new EncodingDetector(bytes).DetectLineBreak(),
                EncodingDetector.DetectLineBreak(bytes, bytes.Length, 65001));
        }

        /// <summary>
        /// 公開の DetectLineBreak() はバイト単位のまま変えていない（公開 API の挙動を保つ）
        /// </summary>
        [Fact]
        public void PublicDetectLineBreak_StillCountsBytes()
        {
            byte[] bytes = new UnicodeEncoding(false, false).GetBytes("上\r");

            Assert.Equal(LineBreakType.LfAndCr, new EncodingDetector(bytes).DetectLineBreak());
        }

        private static byte[] Build(Encoding encoding, LineBreakType lineBreak)
        {
            string newLine = lineBreak == LineBreakType.CrLf ? "\r\n" : lineBreak == LineBreakType.Lf ? "\n" : "\r";
            string text = string.Join(newLine, Lines) + newLine;

            return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        }

        private static Encoding GetEncoding(string form, bool bom)
        {
            switch (form)
            {
                case "u16le": return new UnicodeEncoding(false, bom);
                case "u16be": return new UnicodeEncoding(true, bom);
                case "u32le": return new UTF32Encoding(false, bom);
                default: return new UTF32Encoding(true, bom);
            }
        }

        private static Encoding GetEncodingByCodePage(int codePage)
        {
            switch (codePage)
            {
                case 1200: return new UnicodeEncoding(false, false);
                case 1201: return new UnicodeEncoding(true, false);
                case 12000: return new UTF32Encoding(false, false);
                default: return new UTF32Encoding(true, false);
            }
        }
    }
}
