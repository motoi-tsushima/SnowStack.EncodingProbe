using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// UTF-8 判定が RFC 3629 の整形式バイト列だけを受け入れることのテスト
    /// </summary>
    /// <remarks>
    /// 1.2.0 より前の判定は、多バイト文字の後続バイトが足りないまま ASCII に戻った場合に
    /// それを見逃していた。そのため cp1252 のフランス語のテキスト（E7 61 69 …）を
    /// UTF-8 と誤判定していた。Unicode の判定はどのカルチャーでも実行するため、
    /// ここが緩いと全言語に影響する。
    /// </remarks>
    public class Utf8StrictnessTests
    {
        private const int CodePageUtf8 = 65001;

        /// <summary>
        /// 独自判定だけを使い、UTF.Unknown のフォールバックを挟まずに検証する。
        /// カルチャーは東アジア以外にして、旧マルチバイトの判定が働かないようにする。
        /// </summary>
        private static int DetectNative(byte[] buffer)
        {
            var options = new EncodingDetectorOptions { Culture = "de-DE", Strategy = DetectionStrategy.NativeOnly };
            return SnowStack.EncodingProbe.EncodingProbe.Detect(buffer, options).CodePage;
        }

        public static TheoryData<string, byte[]> ValidUtf8 => new TheoryData<string, byte[]>
        {
            // "A" + U+00E9 (C3 A9) + "B"
            { "2バイト文字", new byte[] { 0x41, 0xC3, 0xA9, 0x42, 0x0A } },
            // "A" + U+3042 (E3 81 82) + "B"
            { "3バイト文字", new byte[] { 0x41, 0xE3, 0x81, 0x82, 0x42, 0x0A } },
            // "A" + U+1F600 (F0 9F 98 80) + "B"
            { "4バイト文字", new byte[] { 0x41, 0xF0, 0x9F, 0x98, 0x80, 0x42, 0x0A } },
            // U+0800 (E0 A0 80) … 3バイト文字の下限
            { "3バイト文字の下限", new byte[] { 0xE0, 0xA0, 0x80, 0x0A } },
            // U+FFFD (EF BF BD)
            { "3バイト文字の上限付近", new byte[] { 0xEF, 0xBF, 0xBD, 0x0A } },
            // U+10000 (F0 90 80 80) … 4バイト文字の下限
            { "4バイト文字の下限", new byte[] { 0xF0, 0x90, 0x80, 0x80, 0x0A } },
            // U+10FFFF (F4 8F BF BF) … 4バイト文字の上限
            { "4バイト文字の上限", new byte[] { 0xF4, 0x8F, 0xBF, 0xBF, 0x0A } },
        };

        public static TheoryData<string, byte[]> InvalidUtf8 => new TheoryData<string, byte[]>
        {
            // cp1252 の "Français" … E7 のあとに後続バイトが来ず ASCII に戻る（1.2.0 で直した誤判定）
            { "後続バイトが足りないまま ASCII に戻る",
                new byte[] { 0x46, 0x72, 0x61, 0x6E, 0xE7, 0x61, 0x69, 0x73, 0x0A } },
            // バッファの終端で文字が途切れる
            { "終端で文字が途切れる", new byte[] { 0x41, 0xE3, 0x81 } },
            // 後続バイトから文字が始まる
            { "後続バイトから始まる", new byte[] { 0x41, 0xA9, 0x42, 0x0A } },
            // 冗長な2バイト文字 (C0 80 は U+0000 の冗長表現)
            { "冗長な2バイト文字", new byte[] { 0xC0, 0x80, 0x0A } },
            { "冗長な2バイト文字(C1)", new byte[] { 0xC1, 0xBF, 0x0A } },
            // 冗長な3バイト文字 (E0 80 80)
            { "冗長な3バイト文字", new byte[] { 0xE0, 0x80, 0x80, 0x0A } },
            // 冗長な4バイト文字 (F0 80 80 80)
            { "冗長な4バイト文字", new byte[] { 0xF0, 0x80, 0x80, 0x80, 0x0A } },
            // サロゲート符号位置 U+D800 (ED A0 80)
            { "サロゲート符号位置", new byte[] { 0xED, 0xA0, 0x80, 0x0A } },
            // U+110000 (F4 90 80 80) … U+10FFFF を超える
            { "U+10FFFF を超える", new byte[] { 0xF4, 0x90, 0x80, 0x80, 0x0A } },
            // 5バイト文字の先頭バイト
            { "5バイト文字の先頭バイト", new byte[] { 0xF8, 0x88, 0x80, 0x80, 0x80, 0x0A } },
            { "0xFF", new byte[] { 0x41, 0xFF, 0x42, 0x0A } },
        };

        [Theory]
        [MemberData(nameof(ValidUtf8))]
        public void Detect_ValidUtf8_IsUtf8(string description, byte[] buffer)
        {
            _ = description;
            Assert.Equal(CodePageUtf8, DetectNative(buffer));
        }

        [Theory]
        [MemberData(nameof(InvalidUtf8))]
        public void Detect_InvalidUtf8_IsNotUtf8(string description, byte[] buffer)
        {
            _ = description;
            Assert.NotEqual(CodePageUtf8, DetectNative(buffer));
        }

        /// <summary>
        /// .NET の厳格な UTF-8 デコーダーと判定結果が一致すること
        /// </summary>
        [Theory]
        [MemberData(nameof(ValidUtf8))]
        [MemberData(nameof(InvalidUtf8))]
        public void Detect_AgreesWithStrictDecoder(string description, byte[] buffer)
        {
            _ = description;

            bool decodable;
            try
            {
                var strict = new System.Text.UTF8Encoding(false, true);
                _ = strict.GetString(buffer);
                decodable = true;
            }
            catch (System.Text.DecoderFallbackException)
            {
                decodable = false;
            }

            // 純粋な ASCII は UTF-8 より先に ASCII として確定するため、ここでは対象にしない
            bool isAscii = true;
            foreach (var b in buffer)
            {
                if (b > 0x7F) { isAscii = false; break; }
            }
            if (isAscii) return;

            Assert.Equal(decodable, DetectNative(buffer) == CodePageUtf8);
        }
    }
}
