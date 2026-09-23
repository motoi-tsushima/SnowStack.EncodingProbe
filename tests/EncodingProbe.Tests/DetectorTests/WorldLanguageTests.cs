using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 東アジア以外の言語のテキストに対する判定テスト
    /// </summary>
    /// <remarks>
    /// テストデータは TestData/German・French・Russian・Polish・Thai/ に配置されています。
    /// 生成には tools/New-EncodingTestData.ps1 を使います。
    ///
    /// 独自判定が担当するのは東アジア漢字文化圏のマルチバイトだけなので、
    /// これらの言語のシングルバイトのテキストは UTF.Unknown が判定します。
    /// このクラスは「どのカルチャーの実行環境でも同じ結果になること」を固定します。
    /// </remarks>
    public class WorldLanguageEncodingTests
    {
        /// <summary>
        /// 判定に使うカルチャー。東アジアのカルチャーを含めているのが要点で、
        /// 日本語や韓国語の実行環境でドイツ語のファイルを読んでも結果が変わってはいけない。
        /// </summary>
        private static readonly string[] CultureNames =
        {
            "de-DE", "fr-FR", "ru-RU", "pl-PL", "th-TH", "en-US",
            "ja-JP", "ko-KR", "zh-CN", "zh-TW", "zh-HK", "kok-IN",
        };

        /// <summary>
        /// 言語フォルダー・ファイル名・期待するコードページの組み合わせ
        /// </summary>
        /// <remarks>
        /// ドイツ語の cp1252 と ISO-8859-1、フランス語の cp1252 と ISO-8859-15 は、
        /// このテストデータの文字だけを使う限りバイト列が完全に一致する。
        /// バイト列が同じである以上どちらか一方しか返しようがないため、
        /// ISO-8859-1 (28591) を期待値としている。スペイン語の cp1252 も同じ理由で 28591 である。
        /// <br/>
        /// スペイン語とエストニア語（1.2.0 で追加）は、東アジアのカルチャーで独自判定が旧マルチバイトを返し、
        /// UTF.Unknown のシングルバイトで上書きされる（上書き経路に入って正しく救済される）ことを固定する。
        /// 判定不能になるもの・短文の限界は KnownLimitTests にある。
        /// </remarks>
        private static readonly object[][] SampleFiles =
        {
            new object[] { "Spanish",   "sample_cp1252.txt",     28591 },
            new object[] { "Spanish",   "sample_utf8.txt",       65001 },
            new object[] { "Estonian",  "sample_cp1257.txt",      1257 },
            new object[] { "Estonian",  "sample_iso8859_15.txt", 28605 },
            new object[] { "Estonian",  "sample_utf8.txt",       65001 },
            new object[] { "Ukrainian", "sample_utf8.txt",       65001 },
            new object[] { "Romanian",  "sample_utf8.txt",       65001 },
            new object[] { "German",  "sample_cp1252.txt",     28591 },
            new object[] { "German",  "sample_iso8859_1.txt",  28591 },
            new object[] { "German",  "sample_utf8.txt",       65001 },
            new object[] { "French",  "sample_cp1252.txt",     28591 },
            new object[] { "French",  "sample_iso8859_15.txt", 28591 },
            new object[] { "French",  "sample_utf8.txt",       65001 },
            new object[] { "Russian", "sample_cp1251.txt",      1251 },
            new object[] { "Russian", "sample_koi8r.txt",      20866 },
            new object[] { "Russian", "sample_utf8.txt",       65001 },
            new object[] { "Polish",  "sample_cp1250.txt",      1250 },
            new object[] { "Polish",  "sample_iso8859_2.txt",  28592 },
            new object[] { "Polish",  "sample_utf8.txt",       65001 },
            new object[] { "Thai",    "sample_cp874.txt",        874 },
            new object[] { "Thai",    "sample_utf8.txt",       65001 },
        };

        /// <summary>
        /// <see cref="CultureNames"/> をテストデータとして返す
        /// </summary>
        public static TheoryData<string> Cultures()
        {
            var data = new TheoryData<string>();
            foreach (var culture in CultureNames)
            {
                data.Add(culture);
            }
            return data;
        }

        /// <summary>
        /// 東アジアの旧マルチバイト文字エンコーディングのコードページ
        /// </summary>
        private static readonly int[] EastAsianLegacyCodePages =
            { 932, 936, 949, 950, 20932, 51932, 51936, 51949, 51950, 54936 };

        /// <summary>
        /// カルチャーを問わず、既定の判定方式で同じコードページを返すこと
        /// </summary>
        [Theory]
        [MemberData(nameof(SamplesForAllCultures))]
        public void Detect_Combined_IsSameForEveryCulture(string language, string fileName, int expectedCodePage, string culture)
        {
            var buffer = TestDataHelper.ReadBytes(language, fileName);
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(expectedCodePage, result.CodePage);
        }

        /// <summary>
        /// 東アジアの旧マルチバイト文字エンコーディングと判定しないこと
        /// </summary>
        /// <remarks>
        /// ドイツ語の cp1252 のバイト列は Shift_JIS としても構造が成立してしまう
        /// （FC DF = "üß" が Shift_JIS の外字領域の 2 バイト文字になる）。
        /// 日本語カルチャーの実行環境でこれを Shift_JIS と誤判定していたのが 1.2.0 で直した回帰である。
        /// </remarks>
        [Theory]
        [MemberData(nameof(SamplesForAllCultures))]
        public void Detect_Combined_IsNotEastAsianLegacyEncoding(string language, string fileName, int expectedCodePage, string culture)
        {
            _ = expectedCodePage;

            var buffer = TestDataHelper.ReadBytes(language, fileName);
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.DoesNotContain(result.CodePage, EastAsianLegacyCodePages);
        }

        /// <summary>
        /// 独自判定だけでは、東アジア以外のカルチャーでシングルバイトを判定できないこと
        /// </summary>
        /// <remarks>
        /// 独自判定は東アジアの旧マルチバイトしか担当しない。
        /// UTF-8 のファイルは Unicode の判定がどのカルチャーでも動くので判定できる。
        /// </remarks>
        [Theory]
        [InlineData("German",  "sample_cp1252.txt")]
        [InlineData("Russian", "sample_cp1251.txt")]
        [InlineData("Polish",  "sample_iso8859_2.txt")]
        [InlineData("Thai",    "sample_cp874.txt")]
        public void Detect_NativeOnly_NonEastAsianCulture_ReturnsUndetermined(string language, string fileName)
        {
            var buffer = TestDataHelper.ReadBytes(language, fileName);
            var options = new EncodingDetectorOptions { Culture = "de-DE", Strategy = DetectionStrategy.NativeOnly };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.True(result.CodePage < 0, $"{language}/{fileName} が独自判定で cp{result.CodePage} と判定された。");
        }

        /// <summary>
        /// コンカニ語（kok）のカルチャーでは、韓国語の旧マルチバイトの判定を行わないこと
        /// </summary>
        /// <remarks>
        /// 1.2.0 より前はカルチャー名の前方一致で韓国語を判定していたため、
        /// kok-IN を韓国語と扱い、EUC-KR / CP949 のバイト列を cp949 と判定していた。
        /// </remarks>
        [Theory]
        [InlineData("kok")]
        [InlineData("kok-IN")]
        public void Detect_NativeOnly_Konkani_DoesNotDetectKorean(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Korean", "sample_cp949.txt");
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.NativeOnly };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.True(result.CodePage < 0, $"{culture} で cp{result.CodePage} と判定された。");
        }

        /// <summary>
        /// スペイン語・エストニア語は、東アジアのカルチャーで上書き経路に入ること（前提の確認）
        /// </summary>
        /// <remarks>
        /// 独自判定だけでは旧マルチバイトと誤判定し、Combined では UTF.Unknown のシングルバイトで上書きされて正しくなる。
        /// この前提が崩れると、SampleFiles の期待値が上書きの回帰を検出しなくなるため、明示的に確認する。
        /// </remarks>
        [Theory]
        [InlineData("Spanish",  "sample_cp1252.txt",     "ja-JP", 932)]
        [InlineData("Spanish",  "sample_cp1252.txt",     "ko-KR", 949)]
        [InlineData("Spanish",  "sample_cp1252.txt",     "zh-CN", 54936)]
        [InlineData("Spanish",  "sample_cp1252.txt",     "zh-TW", 950)]
        [InlineData("Spanish",  "sample_cp1252.txt",     "zh-HK", 950)]
        [InlineData("Estonian", "sample_iso8859_15.txt", "ja-JP", 932)]
        [InlineData("Estonian", "sample_iso8859_15.txt", "ko-KR", 949)]
        [InlineData("Estonian", "sample_iso8859_15.txt", "zh-CN", 54936)]
        [InlineData("Estonian", "sample_cp1257.txt",     "zh-TW", 950)]
        [InlineData("Estonian", "sample_cp1257.txt",     "zh-HK", 950)]
        public void Detect_NativeOnly_EastAsianCulture_EntersOverridePath(string language, string fileName, string culture, int nativeCodePage)
        {
            var buffer = TestDataHelper.ReadBytes(language, fileName);
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.NativeOnly };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(nativeCodePage, result.CodePage);
        }

        /// <summary>
        /// UTF-8 は独自判定だけでもどのカルチャーで判定できること
        /// </summary>
        [Theory]
        [MemberData(nameof(Cultures))]
        public void Detect_NativeOnly_Utf8_IsDetectedForEveryCulture(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("German", "sample_utf8.txt");
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.NativeOnly };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(65001, result.CodePage);
        }

        /// <summary>
        /// <see cref="SampleFiles"/> と <see cref="CultureNames"/> の直積
        /// </summary>
        public static TheoryData<string, string, int, string> SamplesForAllCultures()
        {
            var data = new TheoryData<string, string, int, string>();

            foreach (var sample in SampleFiles)
            {
                var language = (string)sample[0];
                var fileName = (string)sample[1];
                var codePage = (int)sample[2];

                foreach (var culture in CultureNames)
                {
                    data.Add(language, fileName, codePage, culture);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// 入力の種類（バイト配列 / ストリーム / ファイルパス）で結果が変わらないことのテスト
    /// </summary>
    /// <remarks>
    /// ストリームは一度しか読めない。独自判定が読み切ったストリームをそのまま
    /// UTF.Unknown にも渡すと、UTF.Unknown 側が空のストリームを見ることになる。
    /// </remarks>
    public class DetectInputKindTests
    {
        [Theory]
        [InlineData("German",  "sample_cp1252.txt", "ja-JP")]
        [InlineData("German",  "sample_cp1252.txt", "de-DE")]
        [InlineData("Russian", "sample_cp1251.txt", "ja-JP")]
        [InlineData("Thai",    "sample_cp874.txt",  "ja-JP")]
        [InlineData("Japanese", "sample_shiftjis.txt", "ja-JP")]
        public void Detect_StreamAndFilePath_MatchByteArray(string language, string fileName, string culture)
        {
            var buffer = TestDataHelper.ReadBytes(language, fileName);
            var path = TestDataHelper.GetPath(language, fileName);

            var fromBytes = SnowStack.EncodingProbe.EncodingProbe.Detect(
                buffer, new EncodingDetectorOptions { Culture = culture });
            var fromPath = SnowStack.EncodingProbe.EncodingProbe.Detect(
                path, new EncodingDetectorOptions { Culture = culture });

            EncodingInformation fromStream;
            using (var stream = TestDataHelper.OpenStream(language, fileName))
            {
                fromStream = SnowStack.EncodingProbe.EncodingProbe.Detect(
                    stream, new EncodingDetectorOptions { Culture = culture });
            }

            Assert.Equal(fromBytes.CodePage, fromStream.CodePage);
            Assert.Equal(fromBytes.CodePage, fromPath.CodePage);
        }

        [Theory]
        [InlineData("German",  "sample_cp1252.txt")]
        [InlineData("Russian", "sample_cp1251.txt")]
        public void DetectUtfUnknownOnly_Stream_MatchesByteArray(string language, string fileName)
        {
            var buffer = TestDataHelper.ReadBytes(language, fileName);
            var options = new EncodingDetectorOptions { Culture = "de-DE", Strategy = DetectionStrategy.UtfUnknownOnly };

            var fromBytes = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            EncodingInformation fromStream;
            using (var stream = TestDataHelper.OpenStream(language, fileName))
            {
                fromStream = SnowStack.EncodingProbe.EncodingProbe.Detect(
                    stream, new EncodingDetectorOptions { Culture = "de-DE", Strategy = DetectionStrategy.UtfUnknownOnly });
            }

            Assert.Equal(fromBytes.CodePage, fromStream.CodePage);
        }
    }

    /// <summary>
    /// カルチャーに関わらず Unicode の判定が動くことのテスト
    /// </summary>
    /// <remarks>
    /// UTF.Unknown は BOM 無しの UTF-16 / UTF-32 に対応していない。
    /// 独自判定を「東アジア以外ではまったく動かさない」ようにしてしまうとこれらを取りこぼすため、
    /// Unicode 系の判定だけはどのカルチャーでも実行する、というのが 1.2.0 の設計である。
    /// </remarks>
    public class UnicodeDetectionForEveryCultureTests
    {
        private const string SampleText = "Grüße aus München. Encoding probe.\n";

        [Theory]
        [InlineData("de-DE")]
        [InlineData("ru-RU")]
        [InlineData("th-TH")]
        [InlineData("ja-JP")]
        public void Detect_Utf16WithoutBom_IsDetectedForEveryCulture(string culture)
        {
            var buffer = System.Text.Encoding.Unicode.GetBytes(SampleText);
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(1200, result.CodePage);
            Assert.False(result.Bom);
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("ru-RU")]
        [InlineData("th-TH")]
        [InlineData("ja-JP")]
        public void Detect_Utf16BeWithoutBom_IsDetectedForEveryCulture(string culture)
        {
            var buffer = System.Text.Encoding.BigEndianUnicode.GetBytes(SampleText);
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(1201, result.CodePage);
            Assert.False(result.Bom);
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("ru-RU")]
        [InlineData("th-TH")]
        [InlineData("ja-JP")]
        public void Detect_Utf32WithoutBom_IsDetectedForEveryCulture(string culture)
        {
            var buffer = new System.Text.UTF32Encoding(false, false).GetBytes(SampleText);
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(12000, result.CodePage);
            Assert.False(result.Bom);
        }

        /// <summary>
        /// ISO-2022-JP はエスケープシーケンスが根拠なので、どのカルチャーでも判定できること
        /// </summary>
        [Theory]
        [InlineData("de-DE")]
        [InlineData("ru-RU")]
        [InlineData("ja-JP")]
        public void Detect_Iso2022Jp_IsDetectedForEveryCulture(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", "sample_jis.txt");
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(50220, result.CodePage);
        }

        /// <summary>
        /// ASCII はどのカルチャーでも判定できること
        /// </summary>
        [Theory]
        [InlineData("de-DE")]
        [InlineData("ru-RU")]
        [InlineData("ja-JP")]
        public void Detect_Ascii_IsDetectedForEveryCulture(string culture)
        {
            var buffer = TestDataHelper.ReadBytes("English", "sample_ascii.txt");
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.NativeOnly };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(20127, result.CodePage);
        }
    }
}
