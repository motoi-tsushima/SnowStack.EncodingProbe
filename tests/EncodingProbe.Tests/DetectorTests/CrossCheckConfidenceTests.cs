using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 統合層のクロスチェック（独自判定の旧マルチバイトを UTF.Unknown のシングルバイトで上書きする規則）
    /// が、信頼度の低い判定で正しい結果を覆さないことのテスト
    /// </summary>
    /// <remarks>
    /// 1.2.0 の課題 1 で入れたクロスチェックは、コードページの組み合わせだけで上書きを決めていた。
    /// UTF.Unknown は短い漢字列や HKSCS 入りの Big5 に対して 0.5 前後の信頼度でシングルバイトを返すため、
    /// 0.5 をわずかに超えただけで正しい旧マルチバイトの判定が覆り、文字化けしていた。
    ///
    /// テストデータはバイト列を明示して組み立てる。
    /// HKSCS 固有字は .NET のエンコーダーでは作れず、
    /// Big5 / GBK の漢字も cp950 / cp936 を解決できる実行環境が要るためである。
    /// </remarks>
    public class CrossCheckConfidenceTests
    {
        /// <summary>
        /// Big5（cp950）の繁体字。いずれも 2 バイト固定で、
        /// 「這是一個用於測試的繁體中文句子。文字編碼判定的樣本。」の各文字にあたる。
        /// </summary>
        private static readonly byte[][] Big5Characters =
        {
            new byte[] { 0xB3, 0x6F }, new byte[] { 0xAC, 0x4F }, new byte[] { 0xA4, 0x40 },
            new byte[] { 0xAD, 0xD3 }, new byte[] { 0xA5, 0xCE }, new byte[] { 0xA9, 0xF3 },
            new byte[] { 0xB4, 0xFA }, new byte[] { 0xB8, 0xD5 }, new byte[] { 0xAA, 0xBA },
            new byte[] { 0xC1, 0x63 }, new byte[] { 0xC5, 0xE9 }, new byte[] { 0xA4, 0xA4 },
            new byte[] { 0xA4, 0xE5 }, new byte[] { 0xA5, 0x79 }, new byte[] { 0xA4, 0x6C },
            new byte[] { 0xA1, 0x43 }, new byte[] { 0xA6, 0x72 }, new byte[] { 0xBD, 0x73 },
            new byte[] { 0xBD, 0x58 }, new byte[] { 0xA7, 0x50 }, new byte[] { 0xA9, 0x77 },
            new byte[] { 0xBC, 0xCB }, new byte[] { 0xA5, 0xBB },
        };

        /// <summary>
        /// GBK（cp936）の簡体字。いずれも 2 バイト固定で、
        /// 「这是一个用于测试的简体中文句子。文字编码判定的样本。」の各文字にあたる。
        /// </summary>
        private static readonly byte[][] GbkCharacters =
        {
            new byte[] { 0xD5, 0xE2 }, new byte[] { 0xCA, 0xC7 }, new byte[] { 0xD2, 0xBB },
            new byte[] { 0xB8, 0xF6 }, new byte[] { 0xD3, 0xC3 }, new byte[] { 0xD3, 0xDA },
            new byte[] { 0xB2, 0xE2 }, new byte[] { 0xCA, 0xD4 }, new byte[] { 0xB5, 0xC4 },
            new byte[] { 0xBC, 0xF2 }, new byte[] { 0xCC, 0xE5 }, new byte[] { 0xD6, 0xD0 },
            new byte[] { 0xCE, 0xC4 }, new byte[] { 0xBE, 0xE4 }, new byte[] { 0xD7, 0xD3 },
            new byte[] { 0xA1, 0xA3 }, new byte[] { 0xD7, 0xD6 }, new byte[] { 0xB1, 0xE0 },
            new byte[] { 0xC2, 0xEB }, new byte[] { 0xC5, 0xD0 }, new byte[] { 0xB6, 0xA8 },
            new byte[] { 0xD1, 0xF9 }, new byte[] { 0xB1, 0xBE },
        };

        /// <summary>
        /// HKSCS 固有字のバイト列。Big5 の未割り当て領域を使うため、
        /// .NET の cp950 エンコーダーでは生成できない。
        /// </summary>
        private static readonly byte[] HkscsCharacters = { 0x88, 0x62, 0xFA, 0x5F };

        /// <summary>「这是一」（GBK、6 バイト）</summary>
        private static readonly byte[] GbkThreeCharacters = { 0xD5, 0xE2, 0xCA, 0xC7, 0xD2, 0xBB };

        /// <summary>GB 系のコードページ</summary>
        private static readonly int[] GbCodePages = { 936, 51936, 54936 };

        /// <summary>
        /// <paramref name="characters"/> を <paramref name="offset"/> 文字目から繰り返して
        /// <paramref name="length"/> バイトのバイト列を作る
        /// </summary>
        /// <remarks><paramref name="length"/> は偶数であること（1 文字 2 バイトのため）。</remarks>
        private static byte[] Repeat(byte[][] characters, int length, int offset)
        {
            var buffer = new byte[length];
            for (int i = 0; i < length; i += 2)
            {
                var character = characters[((i / 2) + offset) % characters.Length];
                buffer[i] = character[0];
                buffer[i + 1] = character[1];
            }
            return buffer;
        }

        /// <summary>
        /// HKSCS 固有字 2 文字を中ほどに挟んだ Big5 のバイト列を作る
        /// </summary>
        /// <param name="length">全体の長さ（偶数、12 以上）</param>
        /// <param name="offset">本文に使う文字の開始位置</param>
        private static byte[] Big5WithHkscs(int length, int offset)
        {
            var body = Repeat(Big5Characters, length - HkscsCharacters.Length, offset);
            int insertAt = (body.Length / 4) * 2;   // 文字境界に合わせた中ほどの位置

            var buffer = new byte[length];
            System.Array.Copy(body, 0, buffer, 0, insertAt);
            System.Array.Copy(HkscsCharacters, 0, buffer, insertAt, HkscsCharacters.Length);
            System.Array.Copy(body, insertAt, buffer, insertAt + HkscsCharacters.Length, body.Length - insertAt);
            return buffer;
        }

        /// <summary>
        /// 既定の判定方式（Combined）で判定する
        /// </summary>
        private static EncodingInformation DetectCombined(byte[] buffer, string culture)
            => SnowStack.EncodingProbe.EncodingProbe.Detect(
                buffer, new EncodingDetectorOptions { Culture = culture, Strategy = DetectionStrategy.Combined });

        /// <summary>
        /// 検査する長さと標本の組み合わせ。
        /// HKSCS 固有字 2 文字ぶん（4 バイト）を入れられる 12 バイト以上を対象とする。
        /// </summary>
        public static TheoryData<int, int> HkscsLengths()
        {
            var data = new TheoryData<int, int>();
            foreach (var length in new[] { 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512 })
            {
                for (int sample = 0; sample < 3; sample++)
                {
                    data.Add(length, sample * 5);
                }
            }
            return data;
        }

        /// <summary>
        /// 6 バイトの簡体字「这是一」が GBK のまま維持されること
        /// </summary>
        /// <remarks>
        /// UTF.Unknown はこのバイト列に対して tis-620 を信頼度 0.5104… で返す。
        /// 上書きの下限を持たなかった頃は、これが cp874（タイ語）に化けていた。
        /// </remarks>
        [Fact]
        public void Detect_Combined_ZhCn_ThreeSimplifiedCharacters_IsNotOverwrittenBySingleByte()
        {
            var result = DetectCombined(GbkThreeCharacters, "zh-CN");

            Assert.Equal(936, result.CodePage);
        }

        /// <summary>
        /// 6〜8 バイトの簡体字が GB 系のまま維持されること
        /// </summary>
        [Theory]
        [InlineData(6, 0)]
        [InlineData(6, 5)]
        [InlineData(6, 10)]
        [InlineData(8, 0)]
        [InlineData(8, 5)]
        [InlineData(8, 10)]
        public void Detect_Combined_ZhCn_ShortSimplifiedText_StaysEastAsianLegacy(int length, int offset)
        {
            var buffer = Repeat(GbkCharacters, length, offset);

            var result = DetectCombined(buffer, "zh-CN");

            Assert.Contains(result.CodePage, GbCodePages);
        }

        /// <summary>
        /// HKSCS 固有字を含む Big5 の文書が、どの長さでも Big5 と判定されること
        /// </summary>
        /// <remarks>
        /// UTF.Unknown は HKSCS 固有字が 2 字入るだけで windows-1252 を返す、または判定不能になる。
        /// 信頼度は 0.5 ちょうどで、採用の下限（0.5 以下なら捨てる）に境界ちょうどで救われているだけなので、
        /// ここで結果を固定しておく。
        /// </remarks>
        [Theory]
        [MemberData(nameof(HkscsLengths))]
        public void Detect_Combined_ZhTw_Big5WithHkscs_Returns950(int length, int offset)
        {
            var buffer = Big5WithHkscs(length, offset);

            var result = DetectCombined(buffer, "zh-TW");

            Assert.Equal(950, result.CodePage);
        }

        /// <summary>
        /// 香港のカルチャーでも同じ結果になること
        /// </summary>
        [Theory]
        [MemberData(nameof(HkscsLengths))]
        public void Detect_Combined_ZhHk_Big5WithHkscs_Returns950(int length, int offset)
        {
            var buffer = Big5WithHkscs(length, offset);

            var result = DetectCombined(buffer, "zh-HK");

            Assert.Equal(950, result.CodePage);
        }

        /// <summary>
        /// 6〜8 バイトの繁体字が Big5 のまま維持されること
        /// </summary>
        [Theory]
        [InlineData(6, 0)]
        [InlineData(6, 5)]
        [InlineData(6, 10)]
        [InlineData(8, 0)]
        [InlineData(8, 5)]
        [InlineData(8, 10)]
        public void Detect_Combined_ZhTw_ShortTraditionalText_Returns950(int length, int offset)
        {
            var buffer = Repeat(Big5Characters, length, offset);

            var result = DetectCombined(buffer, "zh-TW");

            Assert.Equal(950, result.CodePage);
        }
    }
}
