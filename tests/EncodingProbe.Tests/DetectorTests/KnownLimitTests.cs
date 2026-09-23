using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using UtfUnknown;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// UTF.Unknown の限界によって判定不能・誤判定になる入力の、現在の挙動を固定するテスト
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>このクラスが固定している挙動は、正しい仕様ではない。</b>
    /// 正しい答えは各テストのコメントにある文字エンコーディングであり、現在はそれを返せていない。
    /// 原因は本ライブラリの閾値ではなく、UTF.Unknown の信頼度・精度の限界である
    /// （docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md）。
    /// </para>
    /// <para>
    /// このテストが落ちたら、UTF.Unknown か判定処理の挙動が変わったということである。
    /// 改善であれば期待値を正しい答えに更新し、このクラスから通常のテストへ移すこと。
    /// 悪化であれば原因を調べること。いずれの場合も、測定（tools/Invoke-CrossCheckMeasurement.ps1）をやり直す。
    /// </para>
    /// <para>
    /// 各テストは UTF.Unknown の生の判定も確かめる。UTF.Unknown を更新したときに、
    /// 本ライブラリの結果より先にどこが変わったかが分かるようにするためである。
    /// </para>
    /// </remarks>
    public class KnownLimitTests
    {
        private static readonly string[] EastAsianCultures = { "ja-JP", "ko-KR", "zh-CN", "zh-TW", "zh-HK" };

        private static readonly string[] OtherCultures = { "de-DE", "en-US", "ru-RU", "kok-IN" };

        private static EncodingInformation DetectCombined(byte[] buffer, string culture)
            => SnowStack.EncodingProbe.EncodingProbe.Detect(
                buffer, new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined });

        private static DetectionDetail UtfUnknownRaw(byte[] buffer)
        {
            var detected = CharsetDetector.DetectFromBytes(buffer).Detected;
            Assert.NotNull(detected);
            return detected!;
        }

        public static TheoryData<string> AllCultures()
        {
            var data = new TheoryData<string>();
            foreach (var culture in EastAsianCultures) data.Add(culture);
            foreach (var culture in OtherCultures) data.Add(culture);
            return data;
        }

        #region 判定不能になる（ウクライナ語）

        /// <summary>
        /// ウクライナ語の cp1251 は、どのカルチャーでも判定不能になる。<b>正しくは 1251（windows-1251）。</b>
        /// </summary>
        /// <remarks>
        /// UTF.Unknown は windows-1251 と正しく答えるが、信頼度が 0.48 で採用の下限（0.5 超）に届かない。
        /// 0xFF（я）などを含むため東アジアの独自判定はすべて拒否し、上書き経路にも入らない。
        /// </remarks>
        [Theory]
        [MemberData(nameof(AllCultures))]
        public void Ukrainian_Cp1251_IsUndetermined_NotCorrectBehavior(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Ukrainian", "sample_cp1251.txt");

            var raw = UtfUnknownRaw(buffer);
            Assert.Equal("windows-1251", raw.EncodingName);
            Assert.True(raw.Confidence <= 0.5f, $"UTF.Unknown の信頼度が {raw.Confidence} になった。");

            Assert.Equal(-1, DetectCombined(buffer, culture).CodePage);
        }

        /// <summary>
        /// ウクライナ語の KOI8-U は、どのカルチャーでも判定不能になる。<b>正しくは 21866（koi8-u）。</b>
        /// </summary>
        /// <remarks>
        /// UTF.Unknown は KOI8-U を知らず koi8-r と答える（ウクライナ語固有の字が化けるので誤答）。
        /// 信頼度も 0.45 で採用の下限に届かないため、結果として誤った koi8-r は採用されていない。
        /// </remarks>
        [Theory]
        [MemberData(nameof(AllCultures))]
        public void Ukrainian_Koi8U_IsUndetermined_NotCorrectBehavior(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Ukrainian", "sample_koi8u.txt");

            var raw = UtfUnknownRaw(buffer);
            Assert.Equal("koi8-r", raw.EncodingName);
            Assert.True(raw.Confidence <= 0.5f, $"UTF.Unknown の信頼度が {raw.Confidence} になった。");

            Assert.Equal(-1, DetectCombined(buffer, culture).CodePage);
        }

        #endregion

        #region .NET に無いエンコーディング（ルーマニア語）

        /// <summary>
        /// ルーマニア語の ISO-8859-16 は、どのカルチャーでも名前だけを保存して CodePage = -1 になる。
        /// </summary>
        /// <remarks>
        /// UTF.Unknown は iso-8859-16 と正しく答えるが、.NET に iso-8859-16 が無いため読めない。
        /// 1.1.0 ではここで NullReferenceException が発生していた（緊急修正で直した経路の固定）。
        /// 名前だけを保存する扱いは仕様であり、これは不具合の固定ではない。
        /// </remarks>
        [Theory]
        [MemberData(nameof(AllCultures))]
        public void Romanian_Iso885916_ReturnsNameOnly(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Romanian", "sample_iso8859_16.txt");

            var result = DetectCombined(buffer, culture);

            Assert.Equal(-1, result.CodePage);
            Assert.Equal("iso-8859-16", result.EncodingWebName);
        }

        #endregion

        #region 短文の限界

        /// <summary>
        /// アイスランド語の短い行（28 バイト）は、東アジアのカルチャーで旧マルチバイトのまま残る。
        /// <b>正しくは 1252（windows-1252。28591 でも同じ文字列になる）。</b>
        /// </summary>
        /// <remarks>
        /// 独自判定は旧マルチバイトを返し（上書き経路に入る）、UTF.Unknown は ibm852 を信頼度 0.5366 で返す。
        /// 上書きの下限 0.55 に届かないため上書きされない。
        /// ただし下限を 0.5 に下げても、今度は誤った ibm852 で「救済」されるだけである（下のテスト）。
        /// 閾値では解決しない。UTF.Unknown の短文での精度の限界である。
        /// </remarks>
        [Theory]
        [InlineData("ja-JP", 932)]
        [InlineData("ko-KR", 949)]
        [InlineData("zh-CN", 54936)]
        [InlineData("zh-TW", 950)]
        [InlineData("zh-HK", 950)]
        public void IcelandicShortLine_EastAsianCulture_StaysEastAsian_NotCorrectBehavior(string culture, int currentCodePage)
        {
            var buffer = TestDataHelper.ReadBytes("Icelandic", "sample_short_cp1252.txt");

            var raw = UtfUnknownRaw(buffer);
            Assert.Equal("ibm852", raw.EncodingName);
            Assert.InRange(raw.Confidence, 0.5f, 0.55f);

            Assert.Equal(currentCodePage, DetectCombined(buffer, culture).CodePage);
        }

        /// <summary>
        /// アイスランド語の短い行は、東アジア以外のカルチャーで誤った ibm852 と判定される。
        /// <b>正しくは 1252。現在の 852 は誤りであり、正しい仕様と読み違えないこと。</b>
        /// </summary>
        /// <remarks>
        /// 東アジア以外のカルチャーでは独自判定が判定不能を返し、UTF.Unknown の答えが採用の下限（0.5 超）で採用される。
        /// UTF.Unknown の答え自体が誤っているため、誤った答えがそのまま結果になる。
        /// </remarks>
        [Theory]
        [InlineData("de-DE")]
        [InlineData("en-US")]
        [InlineData("kok-IN")]
        public void IcelandicShortLine_OtherCulture_IsWrongIbm852_NotCorrectBehavior(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Icelandic", "sample_short_cp1252.txt");

            Assert.Equal(852, DetectCombined(buffer, culture).CodePage);
        }

        /// <summary>
        /// ロシア語 KOI8-R・ウクライナ語 KOI8-U の短い行（47・54 バイト）は、日本語カルチャーで Shift_JIS のまま残る。
        /// <b>正しくは 20866（koi8-r）・21866（koi8-u）。</b>
        /// </summary>
        /// <remarks>
        /// 独自判定は Shift_JIS を返し（上書き経路に入る）、UTF.Unknown の信頼度は 0.38〜0.40 と
        /// 採用の下限 0.5 にも届かないため上書きされない。閾値を下げても解決しない。
        /// </remarks>
        [Theory]
        [InlineData("Russian",   "sample_short_koi8r.txt")]
        [InlineData("Ukrainian", "sample_short_koi8u.txt")]
        public void Koi8ShortLine_Japanese_StaysShiftJis_NotCorrectBehavior(string language, string fileName)
        {
            var buffer = TestDataHelper.ReadBytes(language, fileName);

            var raw = UtfUnknownRaw(buffer);
            Assert.True(raw.Confidence <= 0.5f, $"UTF.Unknown の信頼度が {raw.Confidence} になった。");

            Assert.Equal(932, DetectCombined(buffer, "ja-JP").CodePage);
        }

        /// <summary>
        /// KOI8 の短い行は、日本語以外のカルチャーでは判定不能になる。<b>正しくは 20866 / 21866。</b>
        /// </summary>
        [Theory]
        [InlineData("Russian",   "sample_short_koi8r.txt", "ko-KR")]
        [InlineData("Russian",   "sample_short_koi8r.txt", "zh-CN")]
        [InlineData("Russian",   "sample_short_koi8r.txt", "en-US")]
        [InlineData("Ukrainian", "sample_short_koi8u.txt", "zh-TW")]
        [InlineData("Ukrainian", "sample_short_koi8u.txt", "de-DE")]
        public void Koi8ShortLine_OtherCulture_IsUndetermined_NotCorrectBehavior(string language, string fileName, string culture)
        {
            var buffer = TestDataHelper.ReadBytes(language, fileName);

            Assert.Equal(-1, DetectCombined(buffer, culture).CodePage);
        }

        #endregion
    }
}
