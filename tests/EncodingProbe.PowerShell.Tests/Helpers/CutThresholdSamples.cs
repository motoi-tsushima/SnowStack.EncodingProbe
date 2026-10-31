// docs/EncodingProbe-1.3.0-調査-切り出しの境目.md の 1033 件の入力を生成する（第五次で調査ブランチの tools/CutThreshold.cs から移した）。
// 生成の部分（Sample・Rng・Generator）だけを移し、数え上げと規則の評価は移していない（規則のモデルは HeadLineCutterRuleTests にある）。
// バイト列を調査と同じにするため、生成の処理と擬似乱数（xorshift32）・種は変えていない。
// 調査の manifest.csv の SHA-256 との一致を HeadLineCutterRuleTests が確かめている。中身を変えるとそのテストが落ちる。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

#nullable disable

namespace EncodingProbe.PowerShell.Tests.Helpers.CutThreshold
{
    /// <summary>生成した入力 1 件</summary>
    internal sealed class Sample
    {
        public string Id;
        public string Group;
        /// <summary>正しい型（Utf16Le / Utf16Be / Utf32Le / Utf32Be / SingleByte）</summary>
        public string Truth;
        public string Description;
        public byte[] Bytes;
    }

    /// <summary>決定的な擬似乱数（xorshift32）。.NET の版による System.Random の違いを避けるため自前で持つ</summary>
    internal sealed class Rng
    {
        private uint state;

        public Rng(uint seed)
        {
            this.state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint Next()
        {
            uint x = this.state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            this.state = x;
            return x;
        }

        /// <summary>0 以上 n 未満</summary>
        public int Next(int n)
        {
            return (int)(this.Next() % (uint)n);
        }

        public double NextDouble()
        {
            return this.Next() / 4294967296.0;
        }
    }

    /// <summary>入力の生成</summary>
    internal static class Generator
    {
        public const int Window = 64 * 1024;

        /// <summary>自作の長いファイルの目安の大きさ（先頭 64 KiB が必ず埋まるように 64 KiB より大きくする）</summary>
        public const int LongSize = 72 * 1024;

        // 常用漢字から選んだもの。「一」（U+4E00）と「上」（U+4E0A）も実際の文章と同じく含める
        private const string Kanji =
            "日本語人年大中出上下国一十二三四五六七八九百千万円会社者時分事自生行学手見作業関方前後間新地子話高長" +
            "気内同場合部力名発文立理物問題実成定性体法開通明東西南北京都市区町村電車駅道路店品価格表示設計画面" +
            "情報処理技術開発確認変更追加削除保存読書込出力入件数値番号送信受取引用説明記録資料調査結果報告予定";
        private const string Punct = "、。「」・ー　";
        private const string Alnum = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ,.-";

        private static readonly string[] EnglishWords =
        {
            "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "data", "file", "record", "value",
            "name", "address", "city", "total", "amount", "order", "item", "price", "count", "status", "report",
            "system", "user", "account", "date", "time", "number", "text", "line", "field", "column", "row"
        };

        public static Encoding GetEncoding(string type)
        {
            switch (type)
            {
                case "Utf16Le": return new UnicodeEncoding(false, false);
                case "Utf16Be": return new UnicodeEncoding(true, false);
                case "Utf32Le": return new UTF32Encoding(false, false);
                case "Utf32Be": return new UTF32Encoding(true, false);
                default: throw new ArgumentException(type);
            }
        }

        public static readonly string[] UnicodeTypes = { "Utf16Le", "Utf16Be", "Utf32Le", "Utf32Be" };

        private static readonly string[] BreakNames = { "CRLF", "LF", "CR" };

        private static string BreakText(string name)
        {
            switch (name)
            {
                case "CRLF": return "\r\n";
                case "LF": return "\n";
                case "CR": return "\r";
                default: throw new ArgumentException(name);
            }
        }

        /// <summary>日本語の文字を 1 つ選ぶ（漢字 40%、平仮名 45%、片仮名 10%、句読点など 5%）</summary>
        private static char JapaneseChar(Rng rng)
        {
            int r = rng.Next(100);
            if (r < 40) return Kanji[rng.Next(Kanji.Length)];
            if (r < 85) return (char)(0x3041 + rng.Next(0x3093 - 0x3041 + 1));
            if (r < 95) return (char)(0x30A1 + rng.Next(0x30F3 - 0x30A1 + 1));
            return Punct[rng.Next(Punct.Length)];
        }

        /// <summary>
        /// 日本語の文章を作る。1 行 lineLength 文字、英数字の割合 alnumPercent（%）。
        /// special が null でなければ、各文字を specialPercent（%）の確率でその文字に置き換える。
        /// </summary>
        public static string JapaneseText(Rng rng, int totalChars, int lineLength, int alnumPercent, string br,
            string special, int specialPercent)
        {
            var sb = new StringBuilder(totalChars + totalChars / lineLength * 2 + 4);
            int col = 0;
            int written = 0;
            while (written < totalChars)
            {
                if (special != null && rng.Next(100) < specialPercent)
                {
                    sb.Append(special);
                }
                else if (rng.Next(100) < alnumPercent)
                {
                    sb.Append(Alnum[rng.Next(Alnum.Length)]);
                }
                else
                {
                    sb.Append(JapaneseChar(rng));
                }

                written++;
                col++;
                if (col == lineLength)
                {
                    sb.Append(br);
                    col = 0;
                }
            }

            return sb.ToString();
        }

        /// <summary>英語の CSV 風の文章（us-ascii）</summary>
        public static string EnglishText(Rng rng, int totalBytes, string br)
        {
            var sb = new StringBuilder(totalBytes + 64);
            while (sb.Length < totalBytes)
            {
                int words = 4 + rng.Next(8);
                for (int i = 0; i < words; i++)
                {
                    if (i > 0) sb.Append(rng.Next(4) == 0 ? "," : " ");
                    sb.Append(EnglishWords[rng.Next(EnglishWords.Length)]);
                }

                sb.Append(',').Append(rng.Next(100000).ToString(CultureInfo.InvariantCulture)).Append(br);
            }

            return sb.ToString();
        }

        /// <summary>
        /// バイト列に 00 を挿入する。runs 個の場所に、それぞれ runLength バイトの 00 を入れる（位置はばらばら）。
        /// </summary>
        public static byte[] InsertZeros(Rng rng, byte[] source, int runs, int runLength, int firstOffset)
        {
            var positions = new List<int>(runs);
            for (int i = 0; i < runs; i++)
            {
                positions.Add(firstOffset + rng.Next(source.Length - firstOffset));
            }

            positions.Sort();
            var result = new List<byte>(source.Length + runs * runLength);
            int p = 0;
            for (int i = 0; i < source.Length; i++)
            {
                while (p < positions.Count && positions[p] == i)
                {
                    for (int k = 0; k < runLength; k++) result.Add(0);
                    p++;
                }

                result.Add(source[i]);
            }

            return result.ToArray();
        }

        /// <summary>LongSize バイトを少し超えるまで文字を足した日本語の文章を符号化する</summary>
        private static byte[] EncodeJapaneseToSize(Encoding enc, Rng rng, int size, int lineLength, int alnumPercent,
            string br, string special, int specialPercent)
        {
            // 1 文字の平均のバイト数から文字数を見積もる（UTF-16 は 2、UTF-32 は 4、UTF-8 は約 3、Shift_JIS は約 2）
            int perChar = enc.GetMaxByteCount(1) >= 8 ? 4 : 2;
            if (enc.CodePage == 65001) perChar = 3;
            int chars = size / perChar + 16;
            return enc.GetBytes(JapaneseText(rng, chars, lineLength, alnumPercent, br, special, specialPercent));
        }

        private static Sample Make(string group, string id, string truth, string description, byte[] bytes)
        {
            var s = new Sample();
            s.Group = group;
            s.Id = id;
            s.Truth = truth;
            s.Description = description;
            s.Bytes = bytes;
            return s;
        }

        // ---------------------------------------------------------------------
        // テスト担当のデータ（C:\_test\EncodingProbeBboxTest\work\c03\ の c03utf.ps1 / c03nul.ps1 と同じ規則で作り直す）
        // ---------------------------------------------------------------------

        /// <summary>reports\data\c03-utf16-32.txt の 72 件（4 型 × 6 内容 × 3 改行）</summary>
        public static List<Sample> TesterUtf()
        {
            var list = new List<Sample>();
            string up = "\u4E0A";
            string ajp = "\u3042\u5B57";
            string jp = "\u65E5\u672C";
            var contents = new List<KeyValuePair<string, string[]>>
            {
                new KeyValuePair<string, string[]>("alnum", new[] { "A,B", "1,2", "3,4" }),
                new KeyValuePair<string, string[]>("up-h", new[] { "A" + up + ",B", "1,2", "3,4" }),
                new KeyValuePair<string, string[]>("up-d", new[] { "A,B", "1" + up + ",2", "3,4" }),
                new KeyValuePair<string, string[]>("a-h", new[] { ajp + ",B", "1,2", "3,4" }),
                new KeyValuePair<string, string[]>("jp-h", new[] { jp + ",B", "1,2", "3,4" }),
                new KeyValuePair<string, string[]>("jp-d", new[] { "A,B", jp + ",2", "3,4" }),
            };
            string[] testerNames = { "utf16le", "utf16be", "utf32le", "utf32be" };
            string[] testerBreaks = { "LF", "CRLF", "CR" };
            for (int e = 0; e < 4; e++)
            {
                Encoding enc = GetEncoding(UnicodeTypes[e]);
                foreach (var c in contents)
                {
                    foreach (string bk in testerBreaks)
                    {
                        string nl = BreakText(bk);
                        string text = string.Join(nl, c.Value) + nl;
                        string id = testerNames[e] + " " + c.Key + " " + bk;
                        list.Add(Make("T-utf", id, UnicodeTypes[e], "c03-utf16-32: " + id, enc.GetBytes(text)));
                    }
                }
            }

            return list;
        }

        /// <summary>reports\data\c03-nul-ascii.txt の 264 件（11 型 × 4 × 3 改行 × 末尾の改行の有無）</summary>
        public static List<Sample> TesterNul()
        {
            var list = new List<Sample>();
            string[] patterns = { "T1", "T2", "T3", "T4", "L1", "L2", "M1", "M2", "E1", "E2", "F8" };
            string[] testerBreaks = { "LF", "CRLF", "CR" };
            foreach (string pat in patterns)
            {
                char kind = pat[0];
                int k = int.Parse(pat.Substring(1), CultureInfo.InvariantCulture);
                for (int s = 0; s <= 3; s++)
                {
                    foreach (string bk in testerBreaks)
                    {
                        byte[] br = Encoding.ASCII.GetBytes(BreakText(bk));
                        foreach (int tail in new[] { 1, 0 })
                        {
                            string[] lines = { new string('x', s) + "A,B", "1,2", "3,4" };
                            var output = new List<byte>();
                            for (int i = 0; i < 3; i++)
                            {
                                byte[] a = Encoding.ASCII.GetBytes(lines[i]);
                                switch (kind)
                                {
                                    case 'T':
                                        output.AddRange(a); output.AddRange(new byte[k]);
                                        break;
                                    case 'L':
                                        output.AddRange(new byte[k]); output.AddRange(a);
                                        break;
                                    case 'M':
                                        if (i > 0)
                                        {
                                            output.AddRange(Encoding.ASCII.GetBytes(lines[i].Substring(0, 2)));
                                            output.AddRange(new byte[k]);
                                            output.AddRange(Encoding.ASCII.GetBytes(lines[i].Substring(2)));
                                        }
                                        else
                                        {
                                            output.AddRange(a);
                                        }
                                        break;
                                    case 'E':
                                        output.AddRange(a);
                                        if (i == 2) output.AddRange(new byte[k]);
                                        break;
                                    case 'F':
                                        output.AddRange(a); output.AddRange(new byte[8 - lines[i].Length]);
                                        break;
                                }

                                if (i < 2 || tail == 1) output.AddRange(br);
                            }

                            byte[] bytes = output.ToArray();
                            string id = string.Format(CultureInfo.InvariantCulture, "{0} s{1} {2} {3} len={4}",
                                pat, s, bk, tail == 1 ? "tail" : "notail", bytes.Length);
                            list.Add(Make("T-nul", id, "SingleByte", "c03-nul-ascii: " + id, bytes));
                        }
                    }
                }
            }

            return list;
        }

        // ---------------------------------------------------------------------
        // 自作のデータ
        // ---------------------------------------------------------------------

        public static List<Sample> Own()
        {
            var list = new List<Sample>();
            uint seed = 1;

            // J: 日本語の文章。英数字の割合 × 1 行の長さ × 改行 × 4 型（英数字 100% も加える）
            foreach (string type in UnicodeTypes)
            {
                Encoding enc = GetEncoding(type);
                foreach (int alnum in new[] { 0, 5, 50, 100 })
                {
                    foreach (int lineLength in new[] { 10, 40, 200 })
                    {
                        foreach (string bk in BreakNames)
                        {
                            var rng = new Rng(seed++);
                            string id = string.Format(CultureInfo.InvariantCulture, "J {0} alnum{1} len{2} {3}",
                                type, alnum, lineLength, bk);
                            list.Add(Make("J", id, type, "日本語の文章（約 72 KiB）",
                                EncodeJapaneseToSize(enc, rng, LongSize, lineLength, alnum, BreakText(bk), null, 0)));
                        }
                    }
                }
            }

            // S: 特定の文字を多く含むもの。日本語の文章（英数字 5%）と英数字の文章（100%）に混ぜる
            var specials = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("U+4E0A", "\u4E0A"),
                new KeyValuePair<string, string>("U+300A", "\u300A"),
                new KeyValuePair<string, string>("U+0D00", "\u0D00"),
                new KeyValuePair<string, string>("U+0A00", "\u0A00"),
                new KeyValuePair<string, string>("U+4E00", "\u4E00"),
                new KeyValuePair<string, string>("U+3000", "\u3000"),
                new KeyValuePair<string, string>("U+20BB7", "\uD842\uDFB7"),
            };
            foreach (string type in UnicodeTypes)
            {
                Encoding enc = GetEncoding(type);
                foreach (var sp in specials)
                {
                    foreach (int alnum in new[] { 5, 100 })
                    {
                        foreach (int percent in new[] { 1, 10, 50, 100 })
                        {
                            var rng = new Rng(seed++);
                            string id = string.Format(CultureInfo.InvariantCulture, "S {0} {1} {2}% base-alnum{3}",
                                type, sp.Key, percent, alnum);
                            list.Add(Make("S", id, type, sp.Key + " を " + percent + "% 含む（1 行 40 文字、CRLF）",
                                EncodeJapaneseToSize(enc, rng, LongSize, 40, alnum, "\r\n", sp.Value, percent)));
                        }
                    }
                }
            }

            // B13: BUG-0013 の再現の入力そのもの（と、英数字だけのヘッダーの版）
            list.Add(Make("S", "S BUG-0013 utf16be 日<U+0D00>,B", "Utf16Be", "BUG-0013 の手順 1",
                GetEncoding("Utf16Be").GetBytes("\u65E5\u0D00,B\r\n1,2\r\n")));
            list.Add(Make("S", "S BUG-0013 utf16le 日<U+0D00>,B", "Utf16Le", "BUG-0013 の手順 4",
                GetEncoding("Utf16Le").GetBytes("\u65E5\u0D00,B\r\n1,2\r\n")));
            list.Add(Make("S", "S BUG-0013 utf16le 日<U+0A00>,B", "Utf16Le", "BUG-0013 の表の 3 行目",
                GetEncoding("Utf16Le").GetBytes("\u65E5\u0A00,B\r\n1,2\r\n")));
            list.Add(Make("S", "S BUG-0013 utf16be A<U+0D00>,B", "Utf16Be", "BUG-0013 の英数字だけのヘッダー",
                GetEncoding("Utf16Be").GetBytes("A\u0D00,B\r\n1,2\r\n")));
            list.Add(Make("S", "S BUG-0013 utf16le A<U+0D00>,B", "Utf16Le", "BUG-0013 の英数字だけのヘッダー",
                GetEncoding("Utf16Le").GetBytes("A\u0D00,B\r\n1,2\r\n")));

            // L: 改行の少ないもの（1 行が 64 KiB を超える）
            foreach (string type in UnicodeTypes)
            {
                Encoding enc = GetEncoding(type);
                foreach (int alnum in new[] { 0, 5, 50, 100 })
                {
                    var rng = new Rng(seed++);
                    string id = string.Format(CultureInfo.InvariantCulture, "L {0} alnum{1} oneline", type, alnum);
                    list.Add(Make("L", id, type, "1 行が 64 KiB を超える（改行は末尾の 1 つだけ）",
                        EncodeJapaneseToSize(enc, rng, LongSize, int.MaxValue, alnum, "\r\n", null, 0)
                            .Concat(enc.GetBytes("\r\n")).ToArray()));
                }
            }

            // H: 64 KiB より短いもの（数十バイトから 8 KiB）
            string[] tinyTexts =
            {
                "A", "AB", "A,B", "A,B\r\n", "A,B\n1,2\n", "\u65E5\u672C", "\u65E5\u672C\u8A9E,\u30C6\u30B9\u30C8\r\n1,2\r\n",
                "\u4E0A", "\u4E00", "\u3000", "abc\u65E5\u672C", "\u3042\u3044\u3046\u3048\u304A",
                "\u4E00\u4E8C\u4E09\r\n", "ID,\u540D\u524D\r\n1,\u5C71\u7530\r\n",
            };
            foreach (string type in UnicodeTypes)
            {
                Encoding enc = GetEncoding(type);
                for (int i = 0; i < tinyTexts.Length; i++)
                {
                    string id = string.Format(CultureInfo.InvariantCulture, "H {0} tiny{1:00}", type, i);
                    list.Add(Make("H", id, type, "数バイトから数十バイト: " + Escape(tinyTexts[i]), enc.GetBytes(tinyTexts[i])));
                }

                foreach (int size in new[] { 64, 256, 1024, 8192 })
                {
                    foreach (int alnum in new[] { 0, 5, 50 })
                    {
                        var rng = new Rng(seed++);
                        string id = string.Format(CultureInfo.InvariantCulture, "H {0} size{1} alnum{2}", type, size, alnum);
                        byte[] b = EncodeJapaneseToSize(enc, rng, size, 40, alnum, "\r\n", null, 0);
                        int unit = type.StartsWith("Utf32", StringComparison.Ordinal) ? 4 : 2;
                        int cut = Math.Min(b.Length, size) / unit * unit;
                        list.Add(Make("H", id, type, "約 " + size + " バイト（1 行 40 文字、CRLF）", b.Take(cut).ToArray()));
                    }
                }
            }

            // N: us-ascii・UTF-8・Shift_JIS の文章に 00 を混ぜたもの
            var bases = new List<KeyValuePair<string, Func<Rng, string, byte[]>>>
            {
                new KeyValuePair<string, Func<Rng, string, byte[]>>("ascii",
                    (r, br) => Encoding.ASCII.GetBytes(EnglishText(r, LongSize, br))),
                new KeyValuePair<string, Func<Rng, string, byte[]>>("utf8",
                    (r, br) => EncodeJapaneseToSize(new UTF8Encoding(false), r, LongSize, 40, 5, br, null, 0)),
                new KeyValuePair<string, Func<Rng, string, byte[]>>("sjis",
                    (r, br) => EncodeJapaneseToSize(Encoding.GetEncoding(932), r, LongSize, 40, 5, br, null, 0)),
            };
            foreach (var b in bases)
            {
                foreach (string bk in new[] { "CRLF", "LF" })
                {
                    foreach (int runLength in new[] { 1, 2, 3, 4, 8 })
                    {
                        foreach (int perWindow in new[] { 1, 10, 100, 1000 })
                        {
                            var rng = new Rng(seed++);
                            byte[] source = b.Value(rng, BreakText(bk));
                            int runs = (int)Math.Round(perWindow * (double)source.Length / Window);
                            if (runs < 1) runs = 1;
                            string id = string.Format(CultureInfo.InvariantCulture, "N {0} {1} run{2} x{3}",
                                b.Key, bk, runLength, perWindow);
                            list.Add(Make("N", id, "SingleByte",
                                b.Key + " に " + runLength + " バイトの 00 を 64 KiB あたり " + perWindow + " か所",
                                InsertZeros(rng, source, runs, runLength, 0)));
                        }
                    }
                }
            }

            // P: 00 で埋めた固定長レコード（改行あり・なし）
            foreach (string enc in new[] { "ascii", "sjis" })
            {
                Encoding e = enc == "ascii" ? Encoding.ASCII : Encoding.GetEncoding(932);
                foreach (int recordLength in new[] { 16, 32, 64 })
                {
                    foreach (bool withBreak in new[] { false, true })
                    {
                        var rng = new Rng(seed++);
                        var output = new List<byte>();
                        int n = 0;
                        while (output.Count < LongSize)
                        {
                            string payload = n.ToString("D5", CultureInfo.InvariantCulture) + "," +
                                (enc == "ascii"
                                    ? EnglishWords[rng.Next(EnglishWords.Length)]
                                    : JapaneseText(rng, 1 + rng.Next(recordLength / 4), int.MaxValue, 0, "", null, 0));
                            byte[] p = e.GetBytes(payload);
                            int bodyLength = recordLength - (withBreak ? 2 : 0);
                            if (p.Length > bodyLength) p = p.Take(bodyLength).ToArray();
                            output.AddRange(p);
                            output.AddRange(new byte[bodyLength - p.Length]);
                            if (withBreak) { output.Add(0x0D); output.Add(0x0A); }
                            n++;
                        }

                        string id = string.Format(CultureInfo.InvariantCulture, "P {0} rec{1} {2}",
                            enc, recordLength, withBreak ? "CRLF" : "nobreak");
                        list.Add(Make("P", id, "SingleByte", "00 で埋めた " + recordLength + " バイトの固定長レコード",
                            output.ToArray()));
                    }
                }
            }

            // F: 64 KiB より後に初めて 00 が現れるもの
            foreach (var b in bases)
            {
                foreach (int first in new[] { Window, Window + 4000 })
                {
                    var rng = new Rng(seed++);
                    byte[] source = b.Value(rng, "\r\n");
                    string id = string.Format(CultureInfo.InvariantCulture, "F {0} first00@{1}", b.Key, first);
                    list.Add(Make("F", id, "SingleByte", "オフセット " + first + " 以降にだけ 00 がある",
                        InsertZeros(rng, source, 20, 1, first)));
                }
            }

            return list;
        }

        // ---------------------------------------------------------------------
        // 追加の調査（調査文書 10 章）: 各行の同じ位置に 00 が 1 つある、長い 1 バイトのファイル
        // 既存の 967 件のバイト列を変えないよう、別の種で別のメソッドにしてある
        // ---------------------------------------------------------------------

        /// <summary>行の本文を targetLength バイトちょうどで作る（先頭は "nnnnn," の 6 バイト。Shift_JIS は文字の途中で切らない）</summary>
        private static byte[] LineBody(Rng rng, bool sjis, int number, int targetLength)
        {
            var bytes = new List<byte>(Encoding.ASCII.GetBytes(number.ToString("D5", CultureInfo.InvariantCulture) + ","));
            Encoding enc = sjis ? Encoding.GetEncoding(932) : Encoding.ASCII;
            while (bytes.Count < targetLength)
            {
                string piece;
                if (sjis)
                {
                    piece = rng.Next(100) < 5 ? Alnum[rng.Next(Alnum.Length)].ToString() : JapaneseChar(rng).ToString();
                }
                else
                {
                    piece = rng.Next(6) == 0 ? (rng.Next(3) == 0 ? "," : " ") : EnglishWords[rng.Next(EnglishWords.Length)].Substring(0, 1 + rng.Next(3));
                }

                byte[] b = enc.GetBytes(piece);
                if (bytes.Count + b.Length > targetLength)
                {
                    // 2 バイトの文字が入りきらないときは英字で埋める
                    while (bytes.Count < targetLength) bytes.Add((byte)'x');
                    break;
                }

                bytes.AddRange(b);
            }

            return bytes.ToArray();
        }

        public static List<Sample> OwnPerLineNul()
        {
            var list = new List<Sample>();
            uint seed = 10001;
            string[] patterns = { "T", "L", "M" };
            string[] parities = { "even", "odd", "mixed" };
            foreach (string pat in patterns)
            foreach (string parity in parities)
            foreach (string bk in BreakNames)
            foreach (bool sjis in new[] { false, true })
            {
                var rng = new Rng(seed++);
                byte[] br = Encoding.ASCII.GetBytes(BreakText(bk));
                var output = new List<byte>(LongSize + 128);
                int number = 1;
                while (output.Count < LongSize)
                {
                    // 行の長さ（00 と改行を含む）を 20〜81 バイトから選び、偶奇をそろえる
                    int lineLength = 20 + rng.Next(62);
                    if (parity == "even" && lineLength % 2 != 0) lineLength++;
                    if (parity == "odd" && lineLength % 2 == 0) lineLength++;
                    byte[] body = LineBody(rng, sjis, number++, lineLength - 1 - br.Length);
                    switch (pat)
                    {
                        case "T":
                            output.AddRange(body); output.Add(0);
                            break;
                        case "L":
                            output.Add(0); output.AddRange(body);
                            break;
                        default:
                            // 行の途中（"nnnnn" の直後、5 バイト目）
                            output.AddRange(body.Take(5)); output.Add(0); output.AddRange(body.Skip(5));
                            break;
                    }

                    output.AddRange(br);
                }

                string id = string.Format(CultureInfo.InvariantCulture, "R {0} {1} {2} {3}", pat, parity, bk, sjis ? "sjis" : "ascii");
                list.Add(Make("R", id, "SingleByte",
                    (sjis ? "Shift_JIS" : "us-ascii") + " の各行の" + (pat == "T" ? "末尾" : pat == "L" ? "先頭" : "途中") + "に 00 を 1 バイト。行の長さは " + parity,
                    output.ToArray()));
            }

            // 1 バイトの 00 で終わる固定長レコード
            foreach (bool sjis in new[] { false, true })
            foreach (int recordLength in new[] { 16, 32, 17 })
            foreach (bool withBreak in new[] { false, true })
            {
                var rng = new Rng(seed++);
                var output = new List<byte>(LongSize + 64);
                int number = 1;
                while (output.Count < LongSize)
                {
                    output.AddRange(LineBody(rng, sjis, number++, recordLength - 1 - (withBreak ? 2 : 0)));
                    output.Add(0);
                    if (withBreak) { output.Add(0x0D); output.Add(0x0A); }
                }

                string id = string.Format(CultureInfo.InvariantCulture, "R record {0} rec{1} {2}", sjis ? "sjis" : "ascii",
                    recordLength, withBreak ? "CRLF" : "nobreak");
                list.Add(Make("R", id, "SingleByte", "1 バイトの 00 で終わる " + recordLength + " バイトの固定長レコード", output.ToArray()));
            }

            return list;
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }

            return sb.ToString();
        }
    }

}
