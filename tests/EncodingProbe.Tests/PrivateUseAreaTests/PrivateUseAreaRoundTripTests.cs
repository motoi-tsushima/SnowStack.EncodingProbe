using System.Text;
using Xunit;

namespace EncodingProbe.Tests.PrivateUseAreaTests
{
    /// <summary>
    /// 私用領域（U+E000–U+F8FF）へ写されるバイト列が往復することの回帰テスト
    /// </summary>
    /// <remarks>
    /// docs/私用領域の扱い_方針草案.md の付録 A の検証コードを移したもの。
    /// 本ライブラリは私用領域に写されるバイト列に介入せず、<see cref="Encoding"/> の既定の動作をそのまま使う。
    /// 外字・造字・HKSCS 固有字を含むファイルを読み込み、加工せずに同じ文字エンコーディングで書き戻したときに
    /// バイト列が保存されるのは、ここで固定している性質に依拠している。
    ///
    /// 件数は Windows 11（PowerShell 5.1 / 7.6.6）・Ubuntu 24.04・macOS 15.7 で一致することを確認済み。
    /// 件数が変わった場合は .NET の対応表が変わったことを意味するので、方針文書の 4.1 節と付録 A を見直すこと。
    /// </remarks>
    public class PrivateUseAreaRoundTripTests
    {
        static PrivateUseAreaRoundTripTests()
        {
#if NETCOREAPP
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
        }

        private static bool IsPrivateUse(char c) => c >= '' && c <= '';

        /// <summary>
        /// A.1 バイト列側からの往復：2 バイト列 → 私用領域の符号位置 → 同じ 2 バイト列
        /// </summary>
        [Theory]
        [InlineData(932, 1880)]
        [InlineData(936, 2149)]
        [InlineData(949, 188)]
        [InlineData(950, 6217)]
        public void TwoByteSequences_MappedToPrivateUse_RoundTrip(int codePage, int expectedCount)
        {
            var encoding = Encoding.GetEncoding(codePage);
            int total = 0;
            int failures = 0;

            for (int lead = 0x81; lead <= 0xFE; lead++)
            {
                for (int trail = 0x40; trail <= 0xFE; trail++)
                {
                    var bytes = new[] { (byte)lead, (byte)trail };
                    var text = encoding.GetString(bytes);
                    if (text.Length != 1 || !IsPrivateUse(text[0]))
                    {
                        continue;
                    }

                    total++;
                    if (!encoding.GetBytes(text).SequenceEqual(bytes))
                    {
                        failures++;
                    }
                }
            }

            Assert.Equal(expectedCount, total);
            Assert.Equal(0, failures);
        }

        /// <summary>
        /// A.2 符号位置側からの往復：私用領域の符号位置 → バイト列 → 同じ符号位置
        /// </summary>
        /// <remarks>
        /// 符号化できない符号位置（<c>?</c> 1 バイトになるもの）は数えない。
        /// 51932 と 51949 の件数が少ないのは、ユーザー定義領域を私用領域へ写さず、
        /// 未定義の単バイトだけが私用領域に対応しているためである。
        /// </remarks>
        [Theory]
        [InlineData(932, 1884)]
        [InlineData(936, 2150)]
        [InlineData(949, 189)]
        [InlineData(950, 6218)]
        [InlineData(20932, 1882)]
        [InlineData(51932, 2)]
        [InlineData(51936, 2150)]
        [InlineData(51949, 6)]
        [InlineData(54936, 6400)]
        public void PrivateUseCodePoints_RoundTrip(int codePage, int expectedCount)
        {
            var encoding = Encoding.GetEncoding(codePage);
            int total = 0;
            int failures = 0;

            for (int u = 0xE000; u <= 0xF8FF; u++)
            {
                var text = ((char)u).ToString();
                var bytes = encoding.GetBytes(text);
                if (bytes.Length == 1 && bytes[0] == 0x3F)
                {
                    continue;
                }

                total++;
                if (encoding.GetString(bytes) != text)
                {
                    failures++;
                }
            }

            Assert.Equal(expectedCount, total);
            Assert.Equal(0, failures);
        }

        /// <summary>
        /// 51950（EUC-TW）は .NET の実行環境が提供していないこと
        /// </summary>
        /// <remarks>
        /// 判定結果として 51950 を返した場合、読み書き系のコマンドは CodePageNotAvailable として報告する。
        /// </remarks>
        [Fact]
        public void EucTw_IsNotProvidedByRuntime()
        {
            Assert.ThrowsAny<Exception>(() => Encoding.GetEncoding(51950));
        }
    }
}
