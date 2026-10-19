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
        /// <summary>BOM が無い場合に 0x00 を探す範囲（先頭からのバイト数）</summary>
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

            // 2. 先頭の探索範囲に 0x00 があれば、1 バイトの型を候補から外す。
            //    ASCII 互換の文字エンコーディングのテキストには 0x00 が現れないため
            bool hasZero = false;

            for (long i = 0; i < ZeroProbeLength; i++)
            {
                int value = window.Get(i);

                if (value < 0)
                {
                    break;
                }

                if (value == 0)
                {
                    hasZero = true;
                    break;
                }
            }

            window.Reset();

            // 3. 先頭から 0A / 0D を探し、候補の型のうちバイト列と位置の条件の両方に一致するものに決める
            for (long position = 0; ; position++)
            {
                int value = window.Get(position);

                if (value < 0)
                {
                    // 4. 末尾まで決まらなかった場合は、改行が無いとみなす
                    return LineBreakUnit.None;
                }

                if (position >= maxLength)
                {
                    // ここより後ろで型が決まっても、切り出す範囲は上限を超える
                    throw TooLarge(path, lineCount);
                }

                if (value != LineFeed && value != CarriageReturn)
                {
                    continue;
                }

                if (!hasZero)
                {
                    return LineBreakUnit.SingleByte;
                }

                // UTF-32 の改行は UTF-16 の改行の形も含むため、UTF-32 を先に調べる
                if (position % 4 == 0 && IsZero(window, position + 1, 3))
                {
                    return LineBreakUnit.Utf32Le;
                }

                if (position % 4 == 3 && IsZero(window, position - 3, 3))
                {
                    return LineBreakUnit.Utf32Be;
                }

                if (position % 2 == 0 && IsZero(window, position + 1, 1))
                {
                    return LineBreakUnit.Utf16Le;
                }

                if (position % 2 == 1 && IsZero(window, position - 1, 1))
                {
                    return LineBreakUnit.Utf16Be;
                }

                // どの型にも一致しない 0A / 0D は改行ではない（UTF-16LE の「上」U+4E0A = 0A 4E など）
            }
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
