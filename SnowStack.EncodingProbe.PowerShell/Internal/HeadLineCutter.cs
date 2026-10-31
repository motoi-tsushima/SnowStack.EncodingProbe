using System;
using System.IO;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 先頭 N 行の切り出しの結果。
    /// </summary>
    internal sealed class HeadLineCut
    {
        public HeadLineCut(byte[] bytes, LineBreakUnit unit, bool isWholeFile)
        {
            this.Bytes = bytes;
            this.Unit = unit;
            this.IsWholeFile = isWholeFile;
        }

        /// <summary>ファイルの先頭（BOM を含む）から、N 個目の改行の末尾までのバイト列</summary>
        public byte[] Bytes { get; }

        /// <summary>切り出しで決めた改行の型。改行が見つからず BOM も無い場合は <see cref="LineBreakUnit.None"/></summary>
        public LineBreakUnit Unit { get; }

        /// <summary>ファイル全体を切り出したかどうか（改行が N 個未満、または改行が無い場合）</summary>
        public bool IsWholeFile { get; }
    }

    /// <summary>
    /// 判定に使う先頭 N 行を、ファイルから切り出す（1.3.0 仕様書 2 章）。
    /// </summary>
    /// <remarks>
    /// バイト数ではなく行数で切るのは、切った末尾が必ず改行になり、多バイト文字の途中で切れないためである。
    /// 判定処理（クラスライブラリ）を変更せずに、巨大なファイルを先頭だけで判定できる。
    /// <br/>
    /// 処理は 2 段階に分ける。まずストリームを走査して切る位置だけを求め（メモリは一定）、
    /// 次にその長さだけを読み込む。走査しながらバイト列を貯めると、
    /// 上限を超えたと分かる時点で上限分（約 2 GB）をメモリに持ってしまうためである。
    /// <br/>
    /// Resolve-Encoding / Import-ProbedCsv / Export-ProbedCsv の -DetectionLineCount はすべてここを通る。
    /// 同じファイルと同じ N に対して、判定結果が一致することをこの共用で保証する（仕様書 3.3）。
    /// </remarks>
    internal static class HeadLineCutter
    {
        /// <summary>BOM が無い場合に、改行の型を決めるために単位を数える範囲（先頭からのバイト数）</summary>
        public const int ZeroProbeLength = 64 * 1024;

        /// <summary>走査に使うバッファの大きさ</summary>
        private const int DefaultBufferSize = 64 * 1024;

        private const byte LineFeed = 0x0A;
        private const byte CarriageReturn = 0x0D;

        /// <summary>
        /// ファイルを開き、先頭 N 行を切り出す。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス。存在することを前提とする。</param>
        /// <param name="lineCount">切り出す行数（1 以上）</param>
        /// <exception cref="EncodingDetectionException">切り出す範囲が判定できる大きさを超える場合</exception>
        public static HeadLineCut Cut(string path, long lineCount)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                4096,
                FileOptions.SequentialScan))
            {
                return Cut(stream, path, lineCount, EncodingInheritance.MaxDetectableLength, DefaultBufferSize);
            }
        }

        /// <summary>
        /// ストリームの先頭から N 行を切り出す。上限とバッファの大きさを指定できる（テスト用）。
        /// </summary>
        /// <param name="stream">シーク可能なストリーム。先頭から読む。</param>
        /// <param name="path">エラーメッセージに示すパス</param>
        /// <param name="lineCount">切り出す行数（1 以上）</param>
        /// <param name="maxLength">切り出せるバイト数の上限</param>
        /// <param name="bufferSize">走査に使うバッファの大きさ</param>
        /// <exception cref="EncodingDetectionException">切り出す範囲が上限を超える場合</exception>
        internal static HeadLineCut Cut(Stream stream, string path, long lineCount, long maxLength, int bufferSize)
        {
            if (lineCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(lineCount));
            }

            var window = new ByteWindow(stream, bufferSize);

            LineBreakUnit unit = DetermineUnit(window, path, lineCount, maxLength);

            window.Reset();

            long cutLength = FindCutLength(window, unit, path, lineCount, maxLength);
            long totalLength = stream.Length;

            return new HeadLineCut(ReadHead(stream, cutLength), unit, cutLength == totalLength);
        }

        /// <summary>
        /// 改行の型を決める（仕様書 2.4）。
        /// </summary>
        private static LineBreakUnit DetermineUnit(ByteWindow window, string path, long lineCount, long maxLength)
        {
            // 1. BOM がある場合は BOM から決める。
            //    UTF-32LE の BOM（FF FE 00 00）は UTF-16LE の BOM（FF FE）を前方に含むため、
            //    規則を二重に持たずにクラスライブラリの判定を使う
            var head = new byte[4];
            int headLength = 0;

            while (headLength < head.Length && window.Get(headLength) >= 0)
            {
                head[headLength] = (byte)window.Get(headLength);
                headLength++;
            }

            if (headLength < head.Length)
            {
                Array.Resize(ref head, headLength);
            }

            var bom = new ByteOrderMarkDetection();

            if (bom.IsBOM(head))
            {
                switch (bom.CodePage)
                {
                    case 1200:
                        return LineBreakUnit.Utf16Le;
                    case 1201:
                        return LineBreakUnit.Utf16Be;
                    case 12000:
                        return LineBreakUnit.Utf32Le;
                    case 12001:
                        return LineBreakUnit.Utf32Be;
                    default:
                        return LineBreakUnit.SingleByte;
                }
            }

            // 2. 先頭の探索範囲（短ければ全体）を単位に区切って数え、規則で型を決める（第五次。仕様書 2.4）
            var probe = new byte[ZeroProbeLength];
            int probeLength = 0;

            while (probeLength < probe.Length)
            {
                int value = window.Get(probeLength);

                if (value < 0)
                {
                    break;
                }

                probe[probeLength++] = (byte)value;
            }

            bool isWholeFile = probeLength < ZeroProbeLength || window.Get(ZeroProbeLength) < 0;
            LineBreakUnit decided = DecideUnit(probe, probeLength, isWholeFile);

            window.Reset();

            if (decided == LineBreakUnit.None)
            {
                // 1 バイトの型だが 0x00 があり、ファイル全体が探索範囲に収まる。行で切らずにファイル全体を切り出す
                return LineBreakUnit.None;
            }

            // 3. 決めた型の改行だけを先頭から探す。ほかの型の改行の形は見ない
            //    （第四次までは最初に一致した型に決めていた。BUG-0013 の原因）
            for (long position = 0; ; position++)
            {
                int value = window.Get(position);

                if (value < 0)
                {
                    // 4. 末尾まで見つからなかった場合は、改行が無いとみなす
                    return LineBreakUnit.None;
                }

                if (position >= maxLength)
                {
                    // ここより後ろで改行が見つかっても、切り出す範囲は上限を超える
                    throw TooLarge(path, lineCount);
                }

                if (value != LineFeed && value != CarriageReturn)
                {
                    continue;
                }

                // 単位の境界とバイト列の両方が合う位置だけを改行とする（UTF-16LE の「上」U+4E0A = 0A 4E は改行ではない）
                switch (decided)
                {
                    case LineBreakUnit.Utf32Le:
                        if (position % 4 == 0 && IsZero(window, position + 1, 3))
                        {
                            return decided;
                        }

                        break;
                    case LineBreakUnit.Utf32Be:
                        if (position % 4 == 3 && IsZero(window, position - 3, 3))
                        {
                            return decided;
                        }

                        break;
                    case LineBreakUnit.Utf16Le:
                        if (position % 2 == 0 && IsZero(window, position + 1, 1))
                        {
                            return decided;
                        }

                        break;
                    case LineBreakUnit.Utf16Be:
                        if (position % 2 == 1 && IsZero(window, position - 1, 1))
                        {
                            return decided;
                        }

                        break;
                    default:
                        return decided;
                }
            }
        }

        /// <summary>
        /// BOM の無いファイルの先頭（探索範囲）を単位に区切って数え、改行の型を決める（仕様書 2.4。第五次）。
        /// </summary>
        /// <remarks>
        /// 規則と値の根拠は docs/EncodingProbe-1.3.0-調査-切り出しの境目.md の 1 章・5 章・10 章。
        /// 次の順に調べ、最初に成り立った型にする。どれも成り立たなければ 1 バイトの型。
        /// <list type="number">
        /// <item>UTF-32LE → UTF-32BE: 共通の条件 かつ B ≤ 0.2 × A</item>
        /// <item>UTF-16LE → UTF-16BE: 共通の条件 かつ 改行の向きが合う（Abr ≥ 4 かつ 逆の形の改行 ≤ 0.25 × Abr）</item>
        /// <item>UTF-16LE → UTF-16BE: 共通の条件 かつ B ≤ 0.2 × A</item>
        /// </list>
        /// 共通の条件は A ≥ min(16, ⌈0.6 × N⌉) かつ Z = 0。UTF-16 の型ではさらに P ≤ 0.7 × N。
        /// <br/>
        /// 1 バイトの型になり、探索範囲に 0x00 があり、しかもファイル全体が探索範囲に収まる場合は <see cref="LineBreakUnit.None"/>
        /// を返す（行で切らずにファイル全体を切り出す）。短い日本語だけの UTF-16 を 1 バイトの型として切ると、
        /// 文字の途中で切れた先頭を Shift_JIS などと判定して黙って化けるため（調査文書 11.4 節の 2 件）。
        /// <br/>
        /// 比は整数で比べる（⌈0.6 × N⌉ = ⌊(3N + 4) / 5⌋、B ≤ 0.2 × A は 5B ≤ A など）。
        /// 調査の規則のモデルと 1033 件で決まる型が一致することを、テスト（HeadLineCutterRuleTests）が確かめている。
        /// </remarks>
        /// <param name="head">探索範囲のバイト列</param>
        /// <param name="length">探索範囲のバイト数（ファイルが短ければファイル全体の長さ）</param>
        /// <param name="isWholeFile">探索範囲がファイル全体であるかどうか</param>
        internal static LineBreakUnit DecideUnit(byte[] head, int length, bool isWholeFile)
        {
            UnitCounts counts = UnitCounts.Measure(head, length);

            foreach (int t in UnitCounts.Utf32Order)
            {
                if (counts.BaseConditionHolds(t) && counts.ReverseIsFew(t))
                {
                    return UnitCounts.Units[t];
                }
            }

            // UTF-16 は改行の向きで先に決める。全角空白や「一」の多い文章で、
            // B の条件が逆のバイト順でたまたま成り立つのを止めるため（調査文書 5.1 節）
            foreach (int t in UnitCounts.Utf16Order)
            {
                if (counts.BaseConditionHolds(t) && counts.LineBreakDirectionMatches(t))
                {
                    return UnitCounts.Units[t];
                }
            }

            foreach (int t in UnitCounts.Utf16Order)
            {
                if (counts.BaseConditionHolds(t) && counts.ReverseIsFew(t))
                {
                    return UnitCounts.Units[t];
                }
            }

            if (isWholeFile && counts.HasZero)
            {
                return LineBreakUnit.None;
            }

            return LineBreakUnit.SingleByte;
        }

        /// <summary>
        /// 決めた型の単位で改行を数え、N 個目の改行の末尾の位置を返す（仕様書 2.5・2.6）。
        /// 改行が N 個未満の場合は、ファイル全体の長さを返す。
        /// </summary>
        private static long FindCutLength(
            ByteWindow window, LineBreakUnit unit, string path, long lineCount, long maxLength)
        {
            if (unit != LineBreakUnit.None)
            {
                int unitSize = GetUnitSize(unit);
                long count = 0;
                long position = 0;

                while (true)
                {
                    long value = ReadUnit(window, position, unit);

                    if (value < 0)
                    {
                        break;
                    }

                    if (position + unitSize > maxLength)
                    {
                        // この単位は切り出す範囲に含まれるので、範囲は上限を超える
                        throw TooLarge(path, lineCount);
                    }

                    position += unitSize;

                    if (value == CarriageReturn)
                    {
                        // CR の直後に LF が続く場合は、合わせて 1 個の改行とする。CR と LF の間では切らない。
                        // ByteWindow がバッファの境目をまたいで次の単位を読む
                        if (ReadUnit(window, position, unit) == LineFeed)
                        {
                            if (position + unitSize > maxLength)
                            {
                                throw TooLarge(path, lineCount);
                            }

                            position += unitSize;
                        }
                    }
                    else if (value != LineFeed)
                    {
                        continue;
                    }

                    count++;

                    if (count == lineCount)
                    {
                        return position;
                    }
                }
            }

            // 改行が N 個未満、または改行が無い場合は、ファイル全体を切り出す
            long totalLength = window.ReadToEnd();

            if (totalLength > maxLength)
            {
                throw TooLarge(path, lineCount);
            }

            return totalLength;
        }

        /// <summary>
        /// 指定位置から始まる 1 単位の値を返す。単位の途中でデータが尽きた場合は -1。
        /// </summary>
        private static long ReadUnit(ByteWindow window, long position, LineBreakUnit unit)
        {
            int size = GetUnitSize(unit);
            long value = 0;

            for (int i = 0; i < size; i++)
            {
                int b = window.Get(position + i);

                if (b < 0)
                {
                    return -1;
                }

                switch (unit)
                {
                    case LineBreakUnit.Utf16Le:
                    case LineBreakUnit.Utf32Le:
                        value |= (long)b << (8 * i);
                        break;
                    default:
                        value = (value << 8) | (long)b;
                        break;
                }
            }

            return value;
        }

        private static int GetUnitSize(LineBreakUnit unit)
        {
            switch (unit)
            {
                case LineBreakUnit.Utf16Le:
                case LineBreakUnit.Utf16Be:
                    return 2;
                case LineBreakUnit.Utf32Le:
                case LineBreakUnit.Utf32Be:
                    return 4;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// 指定位置から count バイトがすべて 0x00 かどうか。範囲がファイルの外にかかる場合は false。
        /// </summary>
        private static bool IsZero(ByteWindow window, long position, int count)
        {
            if (position < 0)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (window.Get(position + i) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// ストリームの先頭から指定バイト数を読み込む
        /// </summary>
        private static byte[] ReadHead(Stream stream, long length)
        {
            stream.Seek(0, SeekOrigin.Begin);

            var buffer = new byte[length];
            int read = 0;

            while (read < buffer.Length)
            {
                int count = stream.Read(buffer, read, buffer.Length - read);

                if (count == 0)
                {
                    // 走査の後にファイルが短くなった場合は、読めた分だけを返す
                    Array.Resize(ref buffer, read);
                    break;
                }

                read += count;
            }

            return buffer;
        }

        private static EncodingDetectionException TooLarge(string path, long lineCount)
            => new EncodingDetectionException(ValidationMessages.DetectionLinesTooLarge(path, lineCount), path);

        /// <summary>
        /// 探索範囲を 4 つの型の単位に区切って数えた値（調査文書 2 章の N・A・B・Z・Abr・P）
        /// </summary>
        private sealed class UnitCounts
        {
            public const int Utf16Le = 0;
            public const int Utf16Be = 1;
            public const int Utf32Le = 2;
            public const int Utf32Be = 3;

            /// <summary>添字に対応する型</summary>
            public static readonly LineBreakUnit[] Units =
            {
                LineBreakUnit.Utf16Le, LineBreakUnit.Utf16Be, LineBreakUnit.Utf32Le, LineBreakUnit.Utf32Be,
            };

            public static readonly int[] Utf32Order = { Utf32Le, Utf32Be };

            public static readonly int[] Utf16Order = { Utf16Le, Utf16Be };

            /// <summary>A の最小の上限（長い入力ではこの数だけあればよい）</summary>
            private const int MinMatchCap = 16;

            /// <summary>改行の向きで決めるときに要る、その型の形の改行の数</summary>
            private const int MinLineBreaks = 4;

            /// <summary>N: 全単位数</summary>
            private readonly int[] _n = new int[4];

            /// <summary>A: その型の英数字の形の単位（UTF-16LE は &lt;00 以外&gt; 00、UTF-32LE は b2・b3 が 00 で b0・b1 が両方 00 ではない）</summary>
            private readonly int[] _a = new int[4];

            /// <summary>B: 逆のバイト順の英数字の形の単位</summary>
            private readonly int[] _b = new int[4];

            /// <summary>Z: すべてのバイトが 00 の単位</summary>
            private readonly int[] _z = new int[4];

            /// <summary>Abr: A のうち改行（00 以外のバイトが 0A / 0D）のもの。UTF-16 だけ数える</summary>
            private readonly int[] _abr = new int[4];

            /// <summary>P: 2 バイトの単位のうち、両方のバイトが 0x20〜0x7E のもの</summary>
            private int _printable;

            /// <summary>探索範囲に 0x00 があるかどうか</summary>
            public bool HasZero { get; private set; }

            public static UnitCounts Measure(byte[] head, int length)
            {
                var c = new UnitCounts();

                c.HasZero = Array.IndexOf(head, (byte)0, 0, length) >= 0;

                for (int i = 0; i + 2 <= length; i += 2)
                {
                    byte b0 = head[i];
                    byte b1 = head[i + 1];

                    c._n[Utf16Le]++;
                    c._n[Utf16Be]++;

                    if (IsPrintable(b0) && IsPrintable(b1))
                    {
                        c._printable++;
                    }

                    if (b0 == 0 && b1 == 0)
                    {
                        c._z[Utf16Le]++;
                        c._z[Utf16Be]++;
                    }
                    else if (b1 == 0)
                    {
                        c._a[Utf16Le]++;
                        c._b[Utf16Be]++;

                        if (IsLineBreakByte(b0))
                        {
                            c._abr[Utf16Le]++;
                        }
                    }
                    else if (b0 == 0)
                    {
                        c._b[Utf16Le]++;
                        c._a[Utf16Be]++;

                        if (IsLineBreakByte(b1))
                        {
                            c._abr[Utf16Be]++;
                        }
                    }
                }

                for (int i = 0; i + 4 <= length; i += 4)
                {
                    bool low0 = head[i] == 0 && head[i + 1] == 0;
                    bool high0 = head[i + 2] == 0 && head[i + 3] == 0;

                    c._n[Utf32Le]++;
                    c._n[Utf32Be]++;

                    if (low0 && high0)
                    {
                        c._z[Utf32Le]++;
                        c._z[Utf32Be]++;
                    }
                    else if (high0)
                    {
                        c._a[Utf32Le]++;
                        c._b[Utf32Be]++;
                    }
                    else if (low0)
                    {
                        c._b[Utf32Le]++;
                        c._a[Utf32Be]++;
                    }
                }

                return c;
            }

            /// <summary>
            /// 共通の条件: A ≥ min(16, ⌈0.6 × N⌉) かつ Z = 0。UTF-16 の型ではさらに P ≤ 0.7 × N
            /// </summary>
            public bool BaseConditionHolds(int t)
            {
                int n = this._n[t];

                if (n == 0 || this._z[t] != 0)
                {
                    return false;
                }

                // ⌈0.6 × N⌉ = ⌊(3N + 4) / 5⌋（N ≥ 1 なので 1 以上）
                long minMatch = Math.Min(MinMatchCap, (3L * n + 4) / 5);

                if (this._a[t] < minMatch)
                {
                    return false;
                }

                // 各行に 00 が 1 つある us-ascii のファイルを UTF-16 とみなさないための条件（調査文書 10 章）
                return t >= Utf32Le || 10L * this._printable <= 7L * n;
            }

            /// <summary>B ≤ 0.2 × A</summary>
            public bool ReverseIsFew(int t) => 5L * this._b[t] <= this._a[t];

            /// <summary>改行の向きが合う: Abr ≥ 4 かつ 逆の形の改行 ≤ 0.25 × Abr（UTF-16 の型だけで使う）</summary>
            public bool LineBreakDirectionMatches(int t)
            {
                int abr = this._abr[t];

                return abr >= MinLineBreaks && 4L * this._abr[t ^ 1] <= abr;
            }

            private static bool IsPrintable(byte value) => value >= 0x20 && value <= 0x7E;

            private static bool IsLineBreakByte(byte value) => value == LineFeed || value == CarriageReturn;
        }

        /// <summary>
        /// ストリームを固定サイズのバッファで先頭から順に読み、ファイル先頭からの位置でバイトを引けるようにする。
        /// </summary>
        /// <remarks>
        /// 改行の照合では、見つかった位置の前後 3 バイトまでを見る。
        /// バッファを読み足すときに末尾の数バイトを残すことで、バッファの境目にかかった改行も照合できる。
        /// 位置は単調に進むことを前提とし、残した範囲より前には戻れない（戻るときは <see cref="Reset"/> を使う）。
        /// </remarks>
        private sealed class ByteWindow
        {
            /// <summary>読み足すときに残すバイト数（後ろを 3 バイトまで見るため）</summary>
            private const int Keep = 8;

            private readonly Stream _stream;
            private readonly byte[] _buffer;
            private long _bufferStart;
            private int _bufferLength;
            private bool _endOfStream;

            public ByteWindow(Stream stream, int bufferSize)
            {
                this._stream = stream;
                this._buffer = new byte[bufferSize + Keep];
            }

            /// <summary>
            /// 指定位置のバイトを返す。ファイルの末尾より後ろなら -1。
            /// </summary>
            public int Get(long position)
            {
                while (position >= this._bufferStart + this._bufferLength)
                {
                    if (!this.Fill())
                    {
                        return -1;
                    }
                }

                if (position < this._bufferStart)
                {
                    throw new InvalidOperationException("The position is before the retained range of the buffer.");
                }

                return this._buffer[position - this._bufferStart];
            }

            /// <summary>
            /// 末尾まで読み進め、ファイル全体の長さを返す
            /// </summary>
            public long ReadToEnd()
            {
                while (this.Fill())
                {
                }

                return this._bufferStart + this._bufferLength;
            }

            /// <summary>
            /// ストリームの先頭に戻る
            /// </summary>
            public void Reset()
            {
                this._stream.Seek(0, SeekOrigin.Begin);
                this._bufferStart = 0;
                this._bufferLength = 0;
                this._endOfStream = false;
            }

            /// <summary>
            /// 末尾の数バイトを残して、バッファを読み足す。読めなかった場合は false。
            /// </summary>
            private bool Fill()
            {
                if (this._endOfStream)
                {
                    return false;
                }

                int keep = Math.Min(Keep, this._bufferLength);

                if (keep > 0)
                {
                    Buffer.BlockCopy(this._buffer, this._bufferLength - keep, this._buffer, 0, keep);
                }

                this._bufferStart += this._bufferLength - keep;
                this._bufferLength = keep;

                int count = this._stream.Read(this._buffer, keep, this._buffer.Length - keep);

                if (count == 0)
                {
                    this._endOfStream = true;
                    return false;
                }

                this._bufferLength += count;
                return true;
            }
        }
    }
}
