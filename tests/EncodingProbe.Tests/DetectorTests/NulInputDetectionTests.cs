using System;
using System.Collections.Generic;
using System.Linq;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// 0x00 を含む入力の判定のテスト（1.3.0。BOM 無しの UTF-16 / UTF-32 で英数字だけの入力が us-ascii になる課題の修正）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 仕様は docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md（以下「追補」）。
    /// 入力と期待値は docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md 7.2 節の「修正案 2」の列と同じで、
    /// 入力の組み立ては測定スクリプト（tools/Measure-NulInputDetection.ps1）と同じ内容にしてある。
    /// 製品のコードも System.Text.Encoding も使わず、符号位置または 16 進のリテラルからバイト列を直接組み立てる。
    /// </para>
    /// <para>
    /// 判定方式（Combined / NativeOnly / UtfUnknownOnly）とカルチャー（ja-JP / en-US）のすべての組み合わせで確かめる。
    /// UtfUnknownOnly は独自判定を通らないため、修正の影響を受けない（追補 2.4）。
    /// </para>
    /// </remarks>
    public class NulInputDetectionTests
    {
        private const int Ascii = 20127;
        private const int Utf16Le = 1200;
        private const int Utf16Be = 1201;
        private const int Utf32Le = 12000;
        private const int Utf32Be = 12001;

        private static readonly string[] Cultures = { "ja-JP", "en-US" };

        private static readonly DetectionStrategy[] Strategies =
        {
            DetectionStrategy.Combined, DetectionStrategy.NativeOnly, DetectionStrategy.UtfUnknownOnly,
        };

        /// <summary>
        /// 入力 1 件と、その期待値
        /// </summary>
        /// <param name="Group">調査報告の群（A1、B3、C4 など）</param>
        /// <param name="Id">調査報告の ID</param>
        /// <param name="Bytes">入力のバイト列</param>
        /// <param name="NativeCodePage">Combined / NativeOnly の CodePage</param>
        /// <param name="NativeWebName">Combined / NativeOnly の EncodingWebName</param>
        /// <param name="UtfUnknownCodePage">UtfUnknownOnly の CodePage</param>
        /// <param name="UtfUnknownWebName">UtfUnknownOnly の EncodingWebName</param>
        /// <param name="Bom">Bom（判定方式によらない）</param>
        /// <param name="LineBreak">LineBreak（判定方式によらない）</param>
        private sealed record NulInput(
            string Group,
            string Id,
            byte[] Bytes,
            int NativeCodePage,
            string? NativeWebName,
            int UtfUnknownCodePage,
            string? UtfUnknownWebName,
            bool Bom,
            LineBreakType LineBreak);

        // 静的フィールドの初期化順（UnicodeForms などより前に宣言している）に左右されないよう、最初に使うときに組み立てる
        private static readonly Lazy<Dictionary<string, NulInput>> LazyInputs =
            new Lazy<Dictionary<string, NulInput>>(() => BuildInputs().ToDictionary(input => input.Id));

        private static Dictionary<string, NulInput> Inputs => LazyInputs.Value;

        #region テスト

        /// <summary>
        /// A（英数字だけの BOM 無し UTF-16 / UTF-32、52 件）は、入力どおりの幅とエンディアンと判定すること（追補 3 章）
        /// </summary>
        [Theory]
        [MemberData(nameof(GroupA))]
        public void AlphanumericUnicodeWithoutBom_IsDetectedAsInput(string id, string culture, DetectionStrategy strategy)
            => AssertDetection(id, culture, strategy);

        /// <summary>
        /// B（0x00 を含む ASCII、39 件）と B12（実在しそうな 0x00 入りの ASCII、6 件）は、ASCII のままであること（追補 3 章）
        /// </summary>
        [Theory]
        [MemberData(nameof(GroupB))]
        public void AsciiWithNul_StaysAscii(string id, string culture, DetectionStrategy strategy)
            => AssertDetection(id, culture, strategy);

        /// <summary>
        /// C2〜C4（ESC シーケンスのあるもの・BOM 付き・0x80 以上のバイトを含む BOM 無しの UTF-16 / UTF-32、10 件）は、
        /// 修正前と同じ結果であること（追補 2.1 の「従来の経路をそのまま通る」場合）
        /// </summary>
        [Theory]
        [MemberData(nameof(GroupC))]
        public void OutsideOfAsciiBranch_IsUnchanged(string id, string culture, DetectionStrategy strategy)
            => AssertDetection(id, culture, strategy);

        /// <summary>
        /// <b>既知の限界（追補 4.1・4.3）。正しくは入力どおりの UTF-16 / UTF-32 であり、この結果は正しい挙動ではない。</b>
        /// A6（11 件）は、2 バイトとも 0x80 未満になる文字（上 U+4E0A など）や U+0000 を含むため、ASCII と判定される
        /// </summary>
        /// <remarks>
        /// 修正前も ASCII と判定されていた（悪くはなっていない）。
        /// Import-ProbedCsv / Export-ProbedCsv の -DetectionLineCount を指定すると、改行の型による読み替えで正しく読める。
        /// このテストが落ちたら、判定の規則が変わったということである。改善であれば通常のテストへ移すこと。
        /// </remarks>
        [Theory]
        [MemberData(nameof(GroupA6))]
        public void KnownLimit_UnicodeWithLowBytesOnlyCharacter_IsAscii_NotCorrectBehavior(string id, string culture, DetectionStrategy strategy)
            => AssertDetection(id, culture, strategy);

        /// <summary>
        /// <b>既知の限界（追補 4.2）。</b>
        /// B9〜B11（数バイトの入力、12 件）は、BOM 無しの UTF-16 / UTF-32 とも、ASCII と 00 とも読めてバイト列から区別できない。
        /// 追補 2.2 の形に合うものは UTF-16 / UTF-32 と判定する（9 件）。合わないものは ASCII（3 件）
        /// </summary>
        /// <remarks>
        /// このテストが落ちたら、判定の規則が変わったということである。原因を調べること。
        /// </remarks>
        [Theory]
        [MemberData(nameof(GroupB9To11))]
        public void KnownLimit_ShortInput_IsDecidedByUnitShape(string id, string culture, DetectionStrategy strategy)
            => AssertDetection(id, culture, strategy);

        /// <summary>
        /// 各群の件数が調査報告と同じであること（入力の取りこぼしを防ぐ）
        /// </summary>
        [Fact]
        public void InputCounts_MatchTheReport()
        {
            Assert.Equal(52, Inputs.Values.Count(input => IsGroupA(input.Group)));
            Assert.Equal(39, Inputs.Values.Count(input => IsGroupB(input.Group) && !input.Group.StartsWith("B12", StringComparison.Ordinal)));
            Assert.Equal(6, Inputs.Values.Count(input => input.Group.StartsWith("B12", StringComparison.Ordinal)));
            Assert.Equal(11, Inputs.Values.Count(input => input.Group.StartsWith("A6", StringComparison.Ordinal)));
            Assert.Equal(12, Inputs.Values.Count(input => IsGroupB9To11(input.Group)));
            Assert.Equal(10, Inputs.Values.Count(input => input.Group.StartsWith("C", StringComparison.Ordinal)));
        }

        private static void AssertDetection(string id, string culture, DetectionStrategy strategy)
        {
            NulInput input = Inputs[id];

            EncodingInformation result = SnowStack.EncodingProbe.EncodingProbe.Detect(
                input.Bytes, new EncodingDetectorOptions { Culture = culture, Strategy = strategy });

            bool native = strategy != DetectionStrategy.UtfUnknownOnly;

            Assert.Equal(native ? input.NativeCodePage : input.UtfUnknownCodePage, result.CodePage);
            Assert.Equal(native ? input.NativeWebName : input.UtfUnknownWebName, result.EncodingWebName);
            Assert.Equal(input.Bom, result.Bom);
            Assert.Equal(input.LineBreak, result.LineBreak);
        }

        #endregion

        #region テストデータ（MemberData）

        private static bool IsGroupA(string group)
            => group.StartsWith("A", StringComparison.Ordinal) && !group.StartsWith("A6", StringComparison.Ordinal);

        private static bool IsGroupB9To11(string group)
            => group == "B9" || group == "B10" || group == "B11";

        private static bool IsGroupB(string group)
            => group.StartsWith("B", StringComparison.Ordinal) && !IsGroupB9To11(group);

        private static TheoryData<string, string, DetectionStrategy> Cases(Func<string, bool> groupFilter)
        {
            var data = new TheoryData<string, string, DetectionStrategy>();

            foreach (NulInput input in BuildInputs().Where(input => groupFilter(input.Group)))
            {
                foreach (string culture in Cultures)
                {
                    foreach (DetectionStrategy strategy in Strategies)
                    {
                        data.Add(input.Id, culture, strategy);
                    }
                }
            }

            return data;
        }

        public static TheoryData<string, string, DetectionStrategy> GroupA() => Cases(IsGroupA);

        public static TheoryData<string, string, DetectionStrategy> GroupB() => Cases(IsGroupB);

        public static TheoryData<string, string, DetectionStrategy> GroupC()
            => Cases(group => group.StartsWith("C", StringComparison.Ordinal));

        public static TheoryData<string, string, DetectionStrategy> GroupA6()
            => Cases(group => group.StartsWith("A6", StringComparison.Ordinal));

        public static TheoryData<string, string, DetectionStrategy> GroupB9To11() => Cases(IsGroupB9To11);

        #endregion

        #region 入力の組み立て（tools/Measure-NulInputDetection.ps1 と同じ内容）

        /// <summary>
        /// 文字列の各文字（BMP の範囲）を、指定した幅（1 / 2 / 4 バイト）とエンディアンで並べる
        /// </summary>
        private static byte[] Units(string text, int width, bool bigEndian)
        {
            var list = new List<byte>();

            foreach (char ch in text)
            {
                int code = ch;
                if (width == 1 && code >= 0x80)
                {
                    throw new ArgumentException($"1 バイトの幅で 0x80 以上の文字は作れません: {code}");
                }

                var unit = new byte[width];
                for (int k = 0; k < width; k++)
                {
                    byte value = (byte)((code >> (8 * k)) & 0xFF);
                    unit[bigEndian ? width - 1 - k : k] = value;
                }

                list.AddRange(unit);
            }

            return list.ToArray();
        }

        private static byte[] AsciiBytes(string text) => Units(text, 1, false);

        private static byte[] Zeros(int count) => new byte[count];

        private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

        /// <summary>
        /// 16 進のリテラル（"41 00 2C"）からバイト列を作る
        /// </summary>
        private static byte[] Hex(string hex)
            => hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(token => Convert.ToByte(token, 16)).ToArray();

        /// <summary>
        /// UTF-16 / UTF-32 の形の名前と、幅・エンディアン・独自判定の期待値
        /// </summary>
        private static readonly (string Name, int Width, bool BigEndian, int CodePage, string WebName)[] UnicodeForms =
        {
            ("u16le", 2, false, Utf16Le, "utf-16"),
            ("u16be", 2, true, Utf16Be, "unicodeFFFE"),
            ("u32le", 4, false, Utf32Le, "utf-32"),
            ("u32be", 4, true, Utf32Be, "utf-32BE"),
        };

        private static readonly string[] AllForms = { "u16le", "u16be", "u32le", "u32be" };

        /// <summary>
        /// 0x00 を含み ISO-2022 / ASCII の分岐に入る入力の、UtfUnknownOnly の結果（UTF.Unknown は ascii と答える）
        /// </summary>
        private static NulInput AsciiBranch(string group, string id, byte[] bytes, int nativeCodePage, LineBreakType lineBreak)
            => new NulInput(group, id, bytes, nativeCodePage, NativeWebNameOf(nativeCodePage), Ascii, "ascii", false, lineBreak);

        private static string NativeWebNameOf(int codePage) => codePage switch
        {
            Ascii => "us-ascii",
            Utf16Le => "utf-16",
            Utf16Be => "unicodeFFFE",
            Utf32Le => "utf-32",
            Utf32Be => "utf-32BE",
            _ => throw new ArgumentOutOfRangeException(nameof(codePage)),
        };

        /// <summary>
        /// 文字列を UTF-16 / UTF-32 の各形で並べた入力を作る。
        /// expectAsInput が true なら独自判定の期待値は入力の形のコードページ、false なら ASCII
        /// </summary>
        private static IEnumerable<NulInput> UnicodeFormsOf(
            string group, string id, string text, LineBreakType lineBreak, bool expectAsInput, params string[] forms)
        {
            foreach (var form in UnicodeForms.Where(form => forms.Contains(form.Name)))
            {
                byte[] bytes = Units(text, form.Width, form.BigEndian);
                yield return AsciiBranch(group, $"{id}-{form.Name}", bytes, expectAsInput ? form.CodePage : Ascii, lineBreak);
            }
        }

        private static IEnumerable<NulInput> BuildInputs()
        {
            var inputs = new List<NulInput>();
            const LineBreakType CrLf = LineBreakType.CrLf;
            const LineBreakType None = LineBreakType.None;

            // A. 英数字だけの BOM 無し UTF-16 / UTF-32（修正後は入力どおり）
            string a1Text = "A,B\r\n1,2\r\nabc,def\r\n";
            inputs.AddRange(UnicodeFormsOf("A1", "A1-crlf", a1Text, CrLf, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A2", "A2-lf", "A,B\n1,2\nabc,def\n", LineBreakType.Lf, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A2", "A2-cr", "A,B\r1,2\rabc,def\r", LineBreakType.Cr, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A2", "A2-none", "A,B;1,2;abc,def", None, true, AllForms));

            string line32 = "The quick brown fox 0123456789\r\n";   // 32 文字
            inputs.AddRange(UnicodeFormsOf("A3", "A3-1ch", "A", None, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A3", "A3-2ch", "AB", None, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A3", "A3-10ch", "0123456789", None, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A3", "A3-1k", string.Concat(Enumerable.Repeat(line32, 32)), CrLf, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A3", "A3-40k", string.Concat(Enumerable.Repeat(line32, 1250)), CrLf, true, AllForms));

            string printable = new string(Enumerable.Range(0x20, 0x7E - 0x20 + 1).Select(code => (char)code).ToArray());
            inputs.AddRange(UnicodeFormsOf("A4", "A4-printable", printable, None, true, AllForms));
            inputs.AddRange(UnicodeFormsOf("A4", "A4-tab", "1\t2\t3\r\n-\t+ *\r\n 9.5\t(0)\r\n", CrLf, true, AllForms));

            // 1.3.0 の CSV コマンドの実測（M1）の入力
            inputs.AddRange(UnicodeFormsOf("A5", "A5-c03", "Name,Memo\r\n#1,abc\r\n2,def\r\n", CrLf, true, "u16le", "u32le"));
            inputs.AddRange(UnicodeFormsOf("A5", "A5-c05", "# comment\r\nName,Memo\r\na,b\r\n", CrLf, true, "u16le", "u32le"));
            inputs.AddRange(UnicodeFormsOf("A5", "A5-c09", "A,B\r\n1,2,3\r\n4\r\n5,6", CrLf, true, "u16le", "u32le"));
            inputs.AddRange(UnicodeFormsOf("A5", "A5-c12", "A,B\r1,2\r3,4\r", LineBreakType.Cr, true, "u16le", "u32le"));

            // B. 0x00 を含む ASCII（修正後も ASCII）
            inputs.Add(AsciiBranch("B1", "B1-13+1", Join(AsciiBytes("Hello, World!"), Zeros(1)), Ascii, None));
            inputs.Add(AsciiBranch("B1", "B1-12+1", Join(AsciiBytes("Hello World!"), Zeros(1)), Ascii, None));
            inputs.Add(AsciiBranch("B1", "B1-15+1", Join(AsciiBytes("Hello, World!!!"), Zeros(1)), Ascii, None));

            foreach (int n in new[] { 2, 3, 4, 100 })
            {
                inputs.Add(AsciiBranch("B2", $"B2-12+{n}", Join(AsciiBytes("Hello, World"), Zeros(n)), Ascii, None));
            }

            // 固定長レコード: 各レコードの文字数を変え、残りを 00 で埋める
            string[] recordWords = { "ID001", "Alice", "B", "Charlie01" };
            foreach (int recordLength in new[] { 8, 16, 17, 32 })
            {
                foreach (bool newline in new[] { false, true })
                {
                    foreach (int count in new[] { 3, 4 })
                    {
                        var parts = new List<byte[]>();
                        for (int r = 0; r < count; r++)
                        {
                            string word = recordWords[r];
                            if (word.Length > recordLength) word = word.Substring(0, recordLength);
                            parts.Add(AsciiBytes(word));
                            parts.Add(Zeros(recordLength - word.Length));
                            if (newline) parts.Add(AsciiBytes("\r\n"));
                        }

                        string nl = newline ? "crlf" : "nonl";
                        inputs.Add(AsciiBranch("B3", $"B3-rec{recordLength}-{nl}-x{count}", Join(parts.ToArray()), Ascii, newline ? CrLf : None));
                    }
                }
            }

            inputs.Add(AsciiBranch("B4", "B4-abc", Join(AsciiBytes("a"), Zeros(1), AsciiBytes("bb"), Zeros(1), AsciiBytes("ccc"), Zeros(1)), Ascii, None));
            inputs.Add(AsciiBranch("B4", "B4-print0", Join(AsciiBytes("dir1/file.txt"), Zeros(1), AsciiBytes("dir2/x"), Zeros(1)), Ascii, None));

            inputs.Add(AsciiBranch("B5", "B5-1+13", Join(Zeros(1), AsciiBytes("Hello, World!")), Ascii, None));
            inputs.Add(AsciiBranch("B5", "B5-4+12", Join(Zeros(4), AsciiBytes("Hello, World")), Ascii, None));

            foreach (int n in new[] { 1, 2, 3, 4, 8, 1024 })
            {
                inputs.Add(AsciiBranch("B6", $"B6-zero{n}", Zeros(n), Ascii, None));
            }

            inputs.Add(AsciiBranch("B7", "B7-9", Hex("41 00 42 00 43 00 44 45 00"), Ascii, None));
            inputs.Add(AsciiBranch("B7", "B7-10", Hex("41 00 42 00 43 00 44 45 00 00"), Ascii, None));

            inputs.Add(AsciiBranch("B8", "B8-13", Hex("41 00 00 00 42 00 00 00 43 44 00 00 00"), Ascii, None));
            inputs.Add(AsciiBranch("B8", "B8-11", Hex("41 00 00 00 42 00 00 43 00 00 00"), Ascii, None));
            inputs.Add(AsciiBranch("B8", "B8-12", Hex("41 00 00 00 42 00 00 00 43 44 00 00"), Ascii, None));
            inputs.Add(AsciiBranch("B8", "B8-16", Hex("41 00 00 00 42 00 00 00 43 00 44 00 00 00 00 00"), Ascii, None));

            // A6. 2 バイトとも 0x01〜0x7F になる文字（既知の限界。追補 4.1）
            //   上 U+4E0A（0A 4E）、字 U+5B57（57 5B）、あ U+3042（42 30）、、 U+3001（01 30。制御文字の範囲のバイトを含む）
            //   上の下位バイト 0A を DetectLineBreak が LF と数えるため、上を含む入力の LineBreak は LfAndCrLf になる
            foreach (var (key, code) in new[] { ("ue", 0x4E0A), ("ji", 0x5B57), ("a", 0x3042), ("touten", 0x3001) })
            {
                string text = "A," + (char)code + "\r\n";
                string group = key == "ue" ? "A6-1" : "A6-2";
                LineBreakType lineBreak = key == "ue" ? LineBreakType.LfAndCrLf : CrLf;
                inputs.AddRange(UnicodeFormsOf(group, $"A6-{key}", text, lineBreak, false, "u16le", "u16be"));
            }

            inputs.AddRange(UnicodeFormsOf("A6-3", "A6-ue", "A,上\r\n", LineBreakType.LfAndCrLf, false, "u32le", "u32be"));
            // U+0000 の文字（00 00 の単位）を含むもの（既知の限界。追補 4.3）
            inputs.AddRange(UnicodeFormsOf("A6-4", "A6-nul", "A,B\0C\r\n", CrLf, false, "u16le"));

            // B9〜B11. 区切りにくい短い入力（既知の限界。追補 4.2）
            inputs.Add(AsciiBranch("B9", "B9-4100", Hex("41 00"), Utf16Le, None));
            inputs.Add(AsciiBranch("B9", "B9-0041", Hex("00 41"), Utf16Be, None));
            inputs.Add(AsciiBranch("B9", "B9-0A00", Hex("0A 00"), Utf16Le, LineBreakType.Lf));
            inputs.Add(AsciiBranch("B9", "B9-000A", Hex("00 0A"), Utf16Be, LineBreakType.Lf));
            inputs.Add(AsciiBranch("B10", "B10-41000000", Hex("41 00 00 00"), Utf32Le, None));
            inputs.Add(AsciiBranch("B10", "B10-00000041", Hex("00 00 00 41"), Utf32Be, None));
            inputs.Add(AsciiBranch("B10", "B10-41004200", Hex("41 00 42 00"), Utf16Le, None));
            inputs.Add(AsciiBranch("B10", "B10-00410042", Hex("00 41 00 42"), Utf16Be, None));
            inputs.Add(AsciiBranch("B10", "B10-41424300", Hex("41 42 43 00"), Ascii, None));
            inputs.Add(AsciiBranch("B11", "B11-410042", Hex("41 00 42"), Ascii, None));
            inputs.Add(AsciiBranch("B11", "B11-4100420043", Hex("41 00 42 00 43"), Ascii, None));
            inputs.Add(AsciiBranch("B11", "B11-410042004300", Hex("41 00 42 00 43 00"), Utf16Le, None));

            // B12-1. 英数字のログの後ろを 00 で 4096 バイトまで埋めたもの（本文の長さを偶数・奇数で変える）
            string logText = "2026-10-20 10:00:00 INFO start\r\n2026-10-20 10:00:01 INFO ready\r\n";
            foreach (string body in new[] { logText, logText + "x" })
            {
                byte[] bodyBytes = AsciiBytes(body);
                inputs.Add(AsciiBranch("B12-1", $"B12-1-pad4k-body{bodyBytes.Length}", Join(bodyBytes, Zeros(4096 - bodyBytes.Length)), Ascii, CrLf));
            }

            // B12-2. 改行（CR-LF）ごとに 00 が 1 つ入るテキスト
            string[] b12Words = { "abc", "def", "ghi", "jkl", "mno", "pqr", "stu", "vwx", "yz0", "123" };
            foreach (int lineCount in new[] { 2, 10 })
            {
                var parts = new List<byte[]>();
                for (int r = 0; r < lineCount; r++)
                {
                    parts.Add(AsciiBytes(b12Words[r]));
                    parts.Add(Zeros(1));
                    parts.Add(AsciiBytes("\r\n"));
                }

                inputs.Add(AsciiBranch("B12-2", $"B12-2-lines{lineCount}", Join(parts.ToArray()), Ascii, CrLf));
            }

            // B12-3. タブ区切りで、空のフィールドに 00 が入っているもの（偶数長と奇数長）
            inputs.Add(AsciiBranch("B12-3", "B12-3-even", AsciiBytes("id\tname\tmemo\r\n1\t\0\tx\r\n2\tbob\t\0\r\n"), Ascii, CrLf));
            inputs.Add(AsciiBranch("B12-3", "B12-3-odd", AsciiBytes("id\tname\tmemo\r\n1\t\0\tx\r\n22\tbob\t\0\r\n"), Ascii, CrLf));

            // C2. ESC シーケンスがある（ISO-2022 の分岐。0x00 があっても修正の対象外）
            //   ISO-2022-JP「日本」= ESC $ B 46 7C 4B 5C ESC ( B
            inputs.Add(new NulInput("C2", "C2-mid", Hex("1B 24 42 46 7C 4B 5C 1B 28 42 00 41"),
                50220, "iso-2022-jp", 50220, "iso-2022-jp", false, None));
            inputs.Add(new NulInput("C2", "C2-wrap", Hex("41 00 1B 24 42 46 7C 4B 5C 1B 28 42 0D 0A 00"),
                50220, "iso-2022-jp", Ascii, "ascii", false, CrLf));

            // C3. BOM 付き（BOM で確定する）
            var boms = new Dictionary<string, (string Bom, string UtfUnknownWebName)>
            {
                ["u16le"] = ("FF FE", "utf-16le"),
                ["u16be"] = ("FE FF", "utf-16be"),
                ["u32le"] = ("FF FE 00 00", "utf-32le"),
                ["u32be"] = ("00 00 FE FF", "utf-32be"),
            };
            foreach (var form in UnicodeForms)
            {
                byte[] body = Units(a1Text, form.Width, form.BigEndian);
                inputs.Add(new NulInput("C3", $"C3-bom-{form.Name}", Join(Hex(boms[form.Name].Bom), body),
                    form.CodePage, form.WebName, form.CodePage, boms[form.Name].UtfUnknownWebName, true, CrLf));
            }

            // C4. 0x80 以上のバイトを含む BOM 無しの UTF-16 / UTF-32（既存の UTF-16 / UTF-32 の判定で決まる）
            //   日本語,ABC CRLF（日 U+65E5 本 U+672C 語 U+8A9E）
            foreach (var form in UnicodeForms)
            {
                byte[] bytes = Units("日本語,ABC\r\n", form.Width, form.BigEndian);
                inputs.Add(new NulInput("C4", $"C4-ja-{form.Name}", bytes, form.CodePage, form.WebName, -1, null, false, CrLf));
            }

            return inputs;
        }

        #endregion
    }
}
