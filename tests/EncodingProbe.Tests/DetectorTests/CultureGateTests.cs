using SnowStack.EncodingProbe;
using Xunit;
using Region = SnowStack.EncodingProbe.EncodingDetector.EastAsianLegacyRegion;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// カルチャーゲート（<see cref="EncodingDetector.ResolveEastAsianLegacyRegion(string)"/>）の単体テスト
    /// </summary>
    /// <remarks>
    /// 1.2.0 で完全一致の引き当てをやめ、カルチャー名をサブタグに分解して判定するようにした。
    /// 完全一致だった頃は、.NET 10（ICU）が保持する <c>zh-Hant-HK</c> のような名前が判定不能になっていた。
    /// 期待値は「EncodingProbe 1.2.0 第二次修正（香港 Big5 段階 1）」1 章の表である。
    /// </remarks>
    public class CultureGateTests
    {
        [Theory]
        [InlineData("zh")]
        [InlineData("zh-CN")]
        [InlineData("zh-SG")]
        [InlineData("zh-Hans")]
        [InlineData("zh-Hans-CN")]
        [InlineData("zh-CHS")]
        [InlineData("zh-Hans-HK")]   // 用字サブタグが地域より優先する
        [InlineData("yue-Hans-CN")]
        [InlineData("yue-CN")]
        public void Resolve_ChineseSimplified(string culture)
        {
            Assert.Equal(Region.ChineseSimplified, EncodingDetector.ResolveEastAsianLegacyRegion(culture));
        }

        [Theory]
        [InlineData("zh-TW")]
        [InlineData("zh-Hant")]
        [InlineData("zh-Hant-TW")]
        [InlineData("zh-CHT")]
        public void Resolve_ChineseTraditional(string culture)
        {
            Assert.Equal(Region.ChineseTraditional, EncodingDetector.ResolveEastAsianLegacyRegion(culture));
        }

        [Theory]
        [InlineData("zh-HK")]
        [InlineData("zh-MO")]
        [InlineData("zh-Hant-HK")]
        [InlineData("zh-Hant-MO")]
        [InlineData("yue")]
        [InlineData("yue-HK")]
        [InlineData("yue-Hant")]
        [InlineData("yue-Hant-HK")]
        [InlineData("zh_HK")]        // 区切りの _ は - と同じに扱う
        [InlineData("ZH-hk")]        // 大文字小文字を区別しない
        public void Resolve_ChineseHongKong(string culture)
        {
            Assert.Equal(Region.ChineseHongKong, EncodingDetector.ResolveEastAsianLegacyRegion(culture));
        }

        /// <summary>
        /// 日本語・韓国語の判定結果は 1.2.0 より前と変わらないこと
        /// </summary>
        [Theory]
        [InlineData("ja")]
        [InlineData("ja-JP")]
        [InlineData("ja_JP")]
        [InlineData("JA-jp")]
        [InlineData("ja-JP_radstr")]   // Windows の並べ替え指定付きの名前
        public void Resolve_Japanese(string culture)
        {
            Assert.Equal(Region.Japanese, EncodingDetector.ResolveEastAsianLegacyRegion(culture));
        }

        [Theory]
        [InlineData("ko")]
        [InlineData("ko-KR")]
        [InlineData("ko-KP")]
        [InlineData("ko_KR")]
        [InlineData("KO-kr")]
        public void Resolve_Korean(string culture)
        {
            Assert.Equal(Region.Korean, EncodingDetector.ResolveEastAsianLegacyRegion(culture));
        }

        /// <summary>
        /// 言語コードが ja / ko で始まるだけの別の言語を、日本語・韓国語と判定しないこと
        /// </summary>
        /// <remarks>
        /// 1.2.0 より前は前方一致で判定していたため、kok（コンカニ語）を韓国語と判定し、
        /// EUC-KR / CP949 の判定にかけていた。kos（コスラエ語）は .NET の一覧には無いが、
        /// Windows は未登録の名前も受け付けるため、前方一致に戻っていないことの確認として置く。
        /// </remarks>
        [Theory]
        [InlineData("kok")]
        [InlineData("kok-IN")]
        [InlineData("kos")]
        [InlineData("jam")]
        public void Resolve_LanguagesStartingWithJaOrKo_AreNone(string culture)
        {
            Assert.Equal(Region.None, EncodingDetector.ResolveEastAsianLegacyRegion(culture));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("th-TH")]
        [InlineData("ru-RU")]
        public void Resolve_None(string? culture)
        {
            Assert.Equal(Region.None, EncodingDetector.ResolveEastAsianLegacyRegion(culture));
        }
    }

    /// <summary>
    /// カルチャー名のサブタグ分解（<see cref="CultureNameSubtags"/>）の単体テスト
    /// </summary>
    public class CultureNameSubtagsTests
    {
        [Theory]
        [InlineData("zh-Hant-HK",   "zh",  "Hant", "HK")]
        [InlineData("ZH_hant_hk",   "zh",  "Hant", "HK")]
        [InlineData("zh-TW",        "zh",  null,   "TW")]
        [InlineData("zh-TW_radstr", "zh",  null,   "TW")]   // Windows の並べ替え指定は読み飛ばす
        [InlineData("zh-CHT",       "zh",  "Hant", null)]
        [InlineData("zh-CHS",       "zh",  "Hans", null)]
        [InlineData("yue",          "yue", null,   null)]
        [InlineData("es-419",       "es",  null,   "419")]
        [InlineData("zh-Hant-HK-u-nu-hanidec", "zh", "Hant", "HK")]
        [InlineData("",             "",    null,   null)]
        public void Parse(string culture, string language, string? script, string? region)
        {
            var subtags = CultureNameSubtags.Parse(culture);

            Assert.Equal(language, subtags.Language);
            Assert.Equal(script, subtags.Script);
            Assert.Equal(region, subtags.Region);
        }
    }
}
