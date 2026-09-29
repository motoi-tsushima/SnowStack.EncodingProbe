using System.Collections.Generic;
using System.Linq;
using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// フレンドリ名の無いコードページで PSEncodingName に "I do not know." が入らないことの回帰テスト
    /// </summary>
    /// <remarks>
    /// <para>
    /// net10.0 ビルドの PSEncodingName は、フレンドリ名が無いとき独自判定のエンコーディング名の表
    /// （<c>EncodingDetector.EncodingName</c>）から WebName を引く。表に無いのは UTF.Unknown が返す
    /// windows-1252 / ISO-8859-x などで、1.2.0 より前（1.0.0 から）は表の既定値 "I do not know." が入っていた。
    /// 1.2.0 からは null にする（手動確認後の修正依頼 1 章）。
    /// </para>
    /// <para>
    /// 表にあるコードページ（shift_jis / euc-jp / big5 など）は net10.0 では従来どおり WebName を返す。
    /// net48 ビルドは PS 5.1 の固定名に一致しなければ null で、これも従来どおり。
    /// </para>
    /// </remarks>
    public class PSEncodingNameUnknownCodePageTests
    {
        /// <summary>windows-1252 のイタリア語の文字とバイトの対応（ASCII 以外）</summary>
        private static readonly Dictionary<char, byte> Cp1252 = new Dictionary<char, byte>
        {
            ['à'] = 0xE0, ['è'] = 0xE8, ['é'] = 0xE9, ['ì'] = 0xEC, ['ù'] = 0xF9,
            ['€'] = 0x80, ['—'] = 0x97,
        };

        /// <summary>
        /// UTF.Unknown が windows-1252 と判定するイタリア語の文（修正依頼 4.1 の表の 3 行目）
        /// </summary>
        private const string ItalianText =
            "Italiano\n" +
            "Pranzo d'acqua fa volti sghembi.\n" +
            "Perché è così? Qual è la città più bella? Niente po' po' di meno!\n" +
            "Prezzo: 1.234,56 € — .\n";

        private static byte[] ItalianCp1252Bytes()
            => ItalianText.Select(c => c < 0x80 ? (byte)c : Cp1252[c]).ToArray();

        private static EncodingInformation Detect(byte[] buffer, string culture)
            => SnowStack.EncodingProbe.EncodingProbe.Detect(
                buffer, new EncodingDetectorOptions { Culture = culture });

        [Theory]
        [InlineData("it-IT")]
        [InlineData("ja-JP")]
        public void Cp1252_PSEncodingNameIsNull(string culture)
        {
            var result = Detect(ItalianCp1252Bytes(), culture);

            Assert.Equal(1252, result.CodePage);
            Assert.Equal("windows-1252", result.EncodingWebName);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("ja-JP")]
        public void Iso8859_1_PSEncodingNameIsNull(string culture)
        {
            var result = Detect(TestDataHelper.ReadBytes("German", "sample_iso8859_1.txt"), culture);

            Assert.Equal(28591, result.CodePage);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        [Fact]
        public void UtfUnknownOnly_Cp1252_PSEncodingNameIsNull()
        {
            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(
                ItalianCp1252Bytes(),
                new EncodingDetectorOptions { Culture = "it-IT", Strategy = DetectionStrategy.UtfUnknownOnly });

            Assert.Equal(1252, result.CodePage);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        [Fact]
        public void SingleByte_PSEncodingNameIsNull()
        {
            var inputs = new[]
            {
                Detect(ItalianCp1252Bytes(), "it-IT"),
                Detect(TestDataHelper.ReadBytes("German", "sample_cp1252.txt"), "de-DE"),
                Detect(TestDataHelper.ReadBytes("French", "sample_iso8859_15.txt"), "fr-FR"),
                Detect(TestDataHelper.ReadBytes("Russian", "sample_cp1251.txt"), "ru-RU"),
                Detect(TestDataHelper.ReadBytes("Polish", "sample_cp1250.txt"), "pl-PL"),
                Detect(TestDataHelper.ReadBytes("Thai", "sample_cp874.txt"), "th-TH"),
            };

            foreach (var result in inputs)
            {
                Assert.True(result.CodePage > 0, $"cp{result.CodePage}");
                Assert.Null(result.PSEncodingName);
                Assert.False(result.UsePSName);
            }
        }

        // ─── 独自判定の表にあるコードページ: TFM ごとの従来の値 ─────────────────

#if NETFRAMEWORK
        private const string ExpectedEucJp = null;
        private const string ExpectedBig5 = null;
        private const string ExpectedShiftJis = null;
#else
        private const string ExpectedEucJp = "euc-jp";
        private const string ExpectedBig5 = "big5";
        private const string ExpectedShiftJis = "shift_jis";
#endif

        [Theory]
        [InlineData("Japanese", "sample_eucjp.txt", "ja-JP", 20932, ExpectedEucJp)]
        [InlineData("Japanese", "sample_shiftjis.txt", "ja-JP", 932, ExpectedShiftJis)]
        [InlineData("Chinese_HongKong", "sample_big5hkscs.txt", "zh-HK", 950, ExpectedBig5)]
        public void LegacyMultiByte_PSEncodingNameIsUnchanged(
            string language, string fileName, string culture, int expectedCodePage, string expectedPSName)
        {
            var result = Detect(TestDataHelper.ReadBytes(language, fileName), culture);

            Assert.Equal(expectedCodePage, result.CodePage);
            Assert.Equal(expectedPSName, result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        // ─── フレンドリ名のあるもの: 従来どおり ──────────────────────────────────

#if NETFRAMEWORK
        private const string ExpectedUtf8Bom = "UTF8";
        private const string ExpectedUtf16LeBom = "Unicode";
#else
        private const string ExpectedUtf8Bom = "utf8BOM";
        private const string ExpectedUtf16LeBom = "unicode";
#endif

        [Fact]
        public void Unicode_PSEncodingNameIsUnchanged()
        {
            var utf8 = Detect(TestDataHelper.ReadBytes("Japanese", "sample_utf8_bom.txt"), "ja-JP");
            Assert.Equal(ExpectedUtf8Bom, utf8.PSEncodingName);
            Assert.True(utf8.UsePSName);

            var utf16 = Detect(
                new byte[] { 0xFF, 0xFE }.Concat(System.Text.Encoding.Unicode.GetBytes("Grüße")).ToArray(), "de-DE");
            Assert.Equal(1200, utf16.CodePage);
            Assert.Equal(ExpectedUtf16LeBom, utf16.PSEncodingName);
            Assert.True(utf16.UsePSName);
        }
    }
}
