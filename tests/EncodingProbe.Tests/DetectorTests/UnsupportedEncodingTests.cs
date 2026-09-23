using System.Collections.Generic;
using System.IO;
using System.Linq;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// UTF.Unknown が .NET に無いエンコーディングを返したときに、例外を出さないことの回帰テスト
    /// </summary>
    /// <remarks>
    /// <para>
    /// ルーマニア語のテキストに対して UTF.Unknown は iso-8859-16 を信頼度 0.8 前後で返す。
    /// .NET には iso-8859-16 が無いため、UTF.Unknown の <c>Detected.Encoding</c> は null になる。
    /// 1.1.0 ではこれを読んで NullReferenceException が発生し、
    /// Resolve-Encoding や Get-ProbedContent の利用者まで例外が届いていた。
    /// </para>
    /// <para>
    /// 期待する結果は、.NET が非対応のエンコーディングの既存の扱いと同じである:
    /// 名前（EncodingWebName）だけを保存し、CodePage は -1 にする。
    /// </para>
    /// <para>
    /// テストデータはバイト列を明示して組み立てる（CodePagesEncodingProvider に依存しないため。
    /// そもそも .NET は iso-8859-16 の符号化器を持たない）。
    /// </para>
    /// </remarks>
    public class UnsupportedEncodingTests
    {
        /// <summary>ISO-8859-16 のルーマニア語の文字とバイトの対応（ASCII 以外）</summary>
        private static readonly Dictionary<char, byte> Iso885916 = new Dictionary<char, byte>
        {
            ['ă'] = 0xE3, ['â'] = 0xE2, ['î'] = 0xEE, ['ș'] = 0xBA, ['ț'] = 0xFE,
            ['Ă'] = 0xC3, ['Â'] = 0xC2, ['Î'] = 0xCE, ['Ș'] = 0xAA, ['Ț'] = 0xDE,
        };

        private const string RomanianText =
            "Limba română este o limbă romanică. Fiecare zi este o nouă șansă de a învăța.\r\n" +
            "București este capitala României și cel mai mare oraș din țară.\r\n" +
            "Copiii merg în fiecare dimineață la școală pe jos.\r\n" +
            "Mulțumesc frumos și o zi bună.\r\n";

        /// <summary>ルーマニア語の本文を ISO-8859-16 のバイト列にする</summary>
        internal static byte[] RomanianIso885916()
            => RomanianText.Select(c => c < 0x80 ? (byte)c : Iso885916[c]).ToArray();

        public static TheoryData<DetectionStrategy, string> StrategiesAndCultures()
        {
            var data = new TheoryData<DetectionStrategy, string>();
            foreach (var strategy in new[] { DetectionStrategy.Combined, DetectionStrategy.UtfUnknownOnly })
            {
                foreach (var culture in new[] { "ro-RO", "en-US", "de-DE" })
                {
                    data.Add(strategy, culture);
                }
            }
            return data;
        }

        /// <summary>
        /// 例外を出さず、名前だけを保存して CodePage を -1 にすること
        /// </summary>
        [Theory]
        [MemberData(nameof(StrategiesAndCultures))]
        public void Detect_Iso885916_DoesNotThrow_AndReturnsNameOnly(DetectionStrategy strategy, string culture)
        {
            var buffer = RomanianIso885916();
            var options = new EncodingDetectorOptions { Culture = culture, Strategy = strategy };

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options);

            Assert.Equal(-1, result.CodePage);
            Assert.Equal("iso-8859-16", result.EncodingWebName);
            Assert.False(result.Bom);
            Assert.Null(result.PSEncodingName);
            Assert.False(result.UsePSName);
        }

        /// <summary>
        /// 入力の種類（ストリーム・ファイルパス）でも同じ結果になること
        /// </summary>
        /// <remarks>
        /// ファイルパスの場合、UTF.Unknown は DetectFromFile で判定するため、別の経路を通る。
        /// </remarks>
        [Theory]
        [MemberData(nameof(StrategiesAndCultures))]
        public void Detect_Iso885916_StreamAndFilePath_DoNotThrow(DetectionStrategy strategy, string culture)
        {
            var buffer = RomanianIso885916();
            var path = Path.Combine(Path.GetTempPath(), "EncodingProbe_iso885916_" + System.Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllBytes(path, buffer);

            try
            {
                var fromPath = SnowStack.EncodingProbe.EncodingProbe.Detect(
                    path, new EncodingDetectorOptions { Culture = culture, Strategy = strategy });

                EncodingInformation fromStream;
                using (var stream = new MemoryStream(buffer))
                {
                    fromStream = SnowStack.EncodingProbe.EncodingProbe.Detect(
                        stream, new EncodingDetectorOptions { Culture = culture, Strategy = strategy });
                }

                Assert.Equal(-1, fromPath.CodePage);
                Assert.Equal("iso-8859-16", fromPath.EncodingWebName);
                Assert.Equal(-1, fromStream.CodePage);
                Assert.Equal("iso-8859-16", fromStream.EncodingWebName);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// 東アジアのカルチャーでも例外を出さないこと
        /// </summary>
        /// <remarks>
        /// 東アジアのカルチャーでは独自判定が先に動くため、結果のコードページはバイト構造による。
        /// ここでは例外が出ないことだけを検証する。
        /// </remarks>
        [Theory]
        [InlineData("ja-JP")]
        [InlineData("ko-KR")]
        [InlineData("zh-CN")]
        [InlineData("zh-TW")]
        [InlineData("zh-HK")]
        public void Detect_Iso885916_EastAsianCulture_DoesNotThrow(string culture)
        {
            var buffer = RomanianIso885916();

            var exception = Record.Exception(() => SnowStack.EncodingProbe.EncodingProbe.Detect(
                buffer, new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined }));

            Assert.Null(exception);
        }

        /// <summary>
        /// 独自判定だけの場合は UTF.Unknown を呼ばないので、判定不能になること（修正前後で変わらない）
        /// </summary>
        [Fact]
        public void Detect_Iso885916_NativeOnly_IsUndetermined()
        {
            var buffer = RomanianIso885916();

            var result = SnowStack.EncodingProbe.EncodingProbe.Detect(
                buffer, new EncodingDetectorOptions { Culture = "ro-RO", Strategy = DetectionStrategy.NativeOnly });

            Assert.Equal(-1, result.CodePage);
        }
    }
}
