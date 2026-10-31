using System;
using System.Text;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// カルチャー名を受け付けるかどうかを、名前の形（BCP 47）で決めることのテスト（1.3.0。BUG-0007）
    /// </summary>
    /// <remarks>
    /// 1.2.0 までは <see cref="System.Globalization.CultureInfo"/> で作れるかどうかで決めていたため、net48（NLS）と net10.0（ICU）で受け付ける名前が違った。
    /// 下の表の「旧 net48」「旧 net10.0」の列は、1.3.0 第五次の後半の実測（Windows 11、ja-JP）である。どちらの TFM でも同じ結果になることを、両方でテストを通して確かめる。
    /// </remarks>
    public class CultureNameValidationTests
    {
        [Theory]
        // 旧 net48: 拒否 / 旧 net10.0: 受付 → 両方で拒否
        [InlineData("C")]
        [InlineData("c")]
        [InlineData("root")]
        [InlineData("ROOT")]
        // 旧 両方で拒否 → 拒否のまま
        [InlineData("POSIX")]
        [InlineData("C.UTF-8")]
        [InlineData("en_US.UTF-8")]
        [InlineData("not a culture!")]
        [InlineData("*")]
        [InlineData("-")]
        [InlineData("ja--JP")]
        [InlineData("a")]
        [InlineData("abcd")]
        [InlineData("abcdefghi")]
        [InlineData("en-")]
        [InlineData("-en")]
        [InlineData("en-u")]
        [InlineData("en-x-")]
        [InlineData("x")]
        [InlineData("i")]
        [InlineData("root-JP")]
        [InlineData("no-such-culture-xx")]
        [InlineData("xx-INVALID-yy")]
        [InlineData(" ja-JP")]
        // 旧 両方で受付だが、BCP 47 の形でない → 両方で拒否（1.3.0 で変わる）
        [InlineData("c-JP")]
        [InlineData("ja-JP-x")]
        [InlineData("ja-JP-JP")]
        public void Rejected(string name)
        {
            Assert.False(CultureNameSubtags.IsWellFormed(name));
            Assert.Throws<ArgumentException>(() => SnowStack.EncodingProbe.EncodingProbe.Detect(
                Encoding.ASCII.GetBytes("abc"), new EncodingDetectorOptions { Culture = name }));
        }

        [Theory]
        // 旧 両方で受付 → 受付のまま
        [InlineData("ja-JP")]
        [InlineData("JA-jp")]
        [InlineData("ja_JP")]
        [InlineData("ko_KR")]
        [InlineData("iv")]
        [InlineData("und")]
        [InlineData("und-JP")]
        [InlineData("xx-YY")]
        [InlineData("x-test")]
        [InlineData("i-klingon")]
        [InlineData("i-xx")]
        [InlineData("en-US-POSIX")]
        [InlineData("ja-JP-u-ca-japanese")]
        [InlineData("qps-ploc")]
        [InlineData("sr-Latn")]
        [InlineData("zh-Hant-HK")]
        [InlineData("zh-Hant-HK-x-y")]
        [InlineData("zh-CHT")]
        [InlineData("zh-TW_radstr")]
        [InlineData("de-DE_phoneb")]
        [InlineData("de-DE-1996")]
        [InlineData("es-419")]
        [InlineData("en-GB-oed")]
        [InlineData("sgn-BE-FR")]
        [InlineData("yue")]
        // 旧 net48: 拒否 / 旧 net10.0: 受付 → 両方で受付
        [InlineData("art-lojban")]
        [InlineData("zh-min-nan")]
        [InlineData("zh-yue")]
        [InlineData("zh-guoyu")]
        // 旧 net48: 受付 / 旧 net10.0: 拒否 → 両方で受付
        [InlineData("und-u-ca-gregory")]
        // 旧 両方で拒否 → 両方で受付（BCP 47 の形をしている）
        [InlineData("cel-gaulish")]
        [InlineData("no-bok")]
        [InlineData("zh-min")]
        [InlineData("zh-cmn")]
        public void Accepted(string name)
        {
            Assert.True(CultureNameSubtags.IsWellFormed(name));

            EncodingInformation info = SnowStack.EncodingProbe.EncodingProbe.Detect(
                Encoding.ASCII.GetBytes("abc"), new EncodingDetectorOptions { Culture = name });

            Assert.Equal(name, info.Culture);
        }

        /// <summary>
        /// 片方の .NET が CultureInfo を作れない名前でも、判定の最後まで通ること（判定の後で CultureInfo を作る処理が無いこと。1.3.0 第五次の後半）。
        /// 東アジアの旧マルチバイト（CP949・Big5）のファイルを、すべての判定方式で判定する。net48 と net10.0 の両方で通す
        /// </summary>
        [Theory]
        [InlineData("art-lojban", "Korean", "sample_cp949.txt")]
        [InlineData("zh-min-nan", "Korean", "sample_cp949.txt")]
        [InlineData("zh-guoyu", "Korean", "sample_cp949.txt")]
        [InlineData("cel-gaulish", "Korean", "sample_cp949.txt")]
        [InlineData("no-bok", "Korean", "sample_cp949.txt")]
        [InlineData("zh-min", "Korean", "sample_cp949.txt")]
        [InlineData("und-u-ca-gregory", "Korean", "sample_cp949.txt")]
        [InlineData("zh-yue", "Chinese_HongKong", "sample_big5.txt")]
        [InlineData("zh-cmn", "Chinese_HongKong", "sample_big5.txt")]
        public void NewlyAcceptedNames_DetectToTheEnd(string name, string language, string file)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(EncodingProbe.Tests.Helpers.TestDataHelper.GetPath(language, file));

            foreach (DetectionStrategy strategy in new[] { DetectionStrategy.Combined, DetectionStrategy.NativeOnly, DetectionStrategy.UtfUnknownOnly })
            {
                var options = new EncodingDetectorOptions { Culture = name, Strategy = strategy };

                EncodingInformation fromBytes = SnowStack.EncodingProbe.EncodingProbe.Detect(bytes, options);
                EncodingInformation fromStream = SnowStack.EncodingProbe.EncodingProbe.Detect(new System.IO.MemoryStream(bytes), options);
                EncodingInformation fromFile = SnowStack.EncodingProbe.EncodingProbe.Detect(
                    EncodingProbe.Tests.Helpers.TestDataHelper.GetPath(language, file), options);

                Assert.Equal(name, fromBytes.Culture);
                Assert.Equal(fromBytes.CodePage, fromStream.CodePage);
                Assert.Equal(fromBytes.CodePage, fromFile.CodePage);
            }
        }

        /// <summary>
        /// zh-yue は yue（香港）として、香港の Big5 を 950 と判定する。zh-cmn は表に無い言語として、東アジアの判定を行わない（1.3.0 第五次の後半）
        /// </summary>
        [Fact]
        public void ExtendedLanguage_IsUsedForDetection()
        {
            byte[] big5 = System.IO.File.ReadAllBytes(EncodingProbe.Tests.Helpers.TestDataHelper.GetPath("Chinese_HongKong", "sample_big5.txt"));
            var nativeOnly = DetectionStrategy.NativeOnly;

            Assert.Equal(
                SnowStack.EncodingProbe.EncodingProbe.Detect(big5, new EncodingDetectorOptions { Culture = "zh-HK", Strategy = nativeOnly }).CodePage,
                SnowStack.EncodingProbe.EncodingProbe.Detect(big5, new EncodingDetectorOptions { Culture = "zh-yue", Strategy = nativeOnly }).CodePage);
            Assert.Equal(
                SnowStack.EncodingProbe.EncodingProbe.Detect(big5, new EncodingDetectorOptions { Culture = "en-US", Strategy = nativeOnly }).CodePage,
                SnowStack.EncodingProbe.EncodingProbe.Detect(big5, new EncodingDetectorOptions { Culture = "zh-cmn", Strategy = nativeOnly }).CodePage);
        }

        /// <summary>
        /// 例外には .NET の CultureNotFoundException を内部例外として持たせない（net48 と net10.0 で文言が違うため）
        /// </summary>
        [Fact]
        public void Rejected_HasNoInnerException()
        {
            var exception = Assert.Throws<ArgumentException>(() => SnowStack.EncodingProbe.EncodingProbe.Detect(
                Encoding.ASCII.GetBytes("abc"), new EncodingDetectorOptions { Culture = "C" }));

            Assert.Null(exception.InnerException);
            Assert.Equal("The specified culture 'C' is not supported.", exception.Message);
        }

        /// <summary>
        /// OS の説明は前後の空白を除く（.NET Framework の OSDescription は末尾に空白が付く。BUG-0008）
        /// </summary>
        [Fact]
        public void OsDescription_IsTrimmed()
        {
            string description = SnowStack.EncodingProbe.PlatformInfoResolver.Resolve().Os.Description;

            Assert.Equal(description.Trim(), description);
        }
    }
}
