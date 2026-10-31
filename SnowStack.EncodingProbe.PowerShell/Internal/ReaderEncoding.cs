using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 読み込み（<see cref="ProbedFileReader"/>）で <see cref="System.IO.StreamReader"/> に渡す、preamble を持たない Encoding の包み。
    /// </summary>
    /// <remarks>
    /// <see cref="System.IO.StreamReader"/> は、<c>detectEncodingFromByteOrderMarks: false</c> でも、
    /// 渡された Encoding の preamble（BOM）と一致する先頭のバイト列を読み飛ばす。
    /// <see cref="ProbedFileReader"/> はファイル先頭の BOM の後ろへ進めてから <see cref="System.IO.StreamReader"/> を作るので、
    /// BOM 付きの Encoding（utf8BOM・unicodeBOM・裸の unicode、判定した「BOM あり」の結果など）を渡すと、
    /// BOM が 2 個続くファイルで 2 個目まで取り除いていた（1.1.0 から。BUG-0021）。
    /// 取り除く BOM は、ファイル先頭の 1 個だけとする（標準の 7.x の Get-Content・Import-Csv と同じ）。2 個目の U+FEFF は文字として残る。
    /// <br/>
    /// 符号化・復号は包んだ Encoding にそのまま任せる（フォールバックの設定も包んだ Encoding のもの）。
    /// <br/>
    /// .NET Framework では、ストリームの終わりで Decoder を flush させる役目も持つ（<see cref="EndOfStreamSignal"/>。1.3.0 第六次。BUG-0042）。
    /// </remarks>
    internal sealed class ReaderEncoding : Encoding
    {
        private readonly Encoding _inner;

#if NETFRAMEWORK
        private readonly EndOfStreamSignal? _signal;

        public ReaderEncoding(Encoding inner, EndOfStreamSignal? signal = null)
            : base(inner.CodePage, inner.EncoderFallback, inner.DecoderFallback)
        {
            this._inner = inner;
            this._signal = signal;
        }
#else
        public ReaderEncoding(Encoding inner)
            : base(inner.CodePage, inner.EncoderFallback, inner.DecoderFallback)
        {
            this._inner = inner;
        }
#endif

        public override string WebName => this._inner.WebName;

        public override string EncodingName => this._inner.EncodingName;

        public override bool IsSingleByte => this._inner.IsSingleByte;

        public override byte[] GetPreamble() => new byte[0];

#if NET
        public override System.ReadOnlySpan<byte> Preamble => default;
#endif

        public override int GetByteCount(char[] chars, int index, int count)
            => this._inner.GetByteCount(chars, index, count);

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
            => this._inner.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

        public override int GetCharCount(byte[] bytes, int index, int count)
            => this._inner.GetCharCount(bytes, index, count);

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
            => this._inner.GetChars(bytes, byteIndex, byteCount, chars, charIndex);

        public override int GetMaxByteCount(int charCount) => this._inner.GetMaxByteCount(charCount);

        public override int GetMaxCharCount(int byteCount) => this._inner.GetMaxCharCount(byteCount);

        public override Encoder GetEncoder() => this._inner.GetEncoder();

#if NETFRAMEWORK
        public override Decoder GetDecoder()
            => this._signal == null ? this._inner.GetDecoder() : new FlushAtEndDecoder(this._inner.GetDecoder(), this._signal);
#else
        public override Decoder GetDecoder() => this._inner.GetDecoder();
#endif
    }

#if NETFRAMEWORK
    /// <summary>
    /// .NET Framework の StreamReader に、ストリームの終わりで Decoder を flush させるためのストリームの包み（1.3.0 第六次。BUG-0042）。
    /// </summary>
    /// <remarks>
    /// .NET（PowerShell 7.x）の StreamReader は、ストリームの終わりで Decoder を flush する。途中で切れた文字（長さが奇数の UTF-16 の末尾の 1 バイト、
    /// UTF-32 の 1〜3 バイト、途中で切れた UTF-8・2 バイトのコードページの先行バイトなど）は、置換フォールバックで U+FFFD などとして読まれる。
    /// .NET Framework（PowerShell 5.1）の StreamReader は flush しないので、同じバイトを黙って捨てていた。
    /// <br/>
    /// StreamReader の内部を変えることはできないので、次のように知らせる。
    /// <list type="number">
    /// <item>このストリームは、包んだ FileStream の終わりに達したとき、読めたバイトの後ろに 1 バイトの目印（内容は使わない）を 1 回だけ足して返す</item>
    /// <item><see cref="FlushAtEndDecoder"/> は、目印が足された回の復号で、目印を除いたバイトを flush: true で復号する</item>
    /// </list>
    /// 目印は最後のバイトと同じ回に足すので、StreamReader が短い読み込みの後に読みに来ない場合（Peek）も、flush した文字が届く。
    /// .NET Framework の StreamReader は Decoder.GetChars(byte[], int, int, char[], int) だけを呼ぶ（4.8 は更新されない）。
    /// </remarks>
    internal sealed class EndOfStreamSignal : System.IO.Stream
    {
        private readonly System.IO.FileStream _inner;
        private bool _sent;

        public EndOfStreamSignal(System.IO.FileStream inner)
        {
            this._inner = inner;
        }

        /// <summary>目印を足した読み込みを、まだ復号していないかどうか</summary>
        internal bool Pending { get; set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new System.NotSupportedException();

        public override long Position
        {
            get => throw new System.NotSupportedException();
            set => throw new System.NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = this._inner.Read(buffer, offset, count);

            // FileStream が要求より少なく返すのは終わりに達したとき。念のため位置でも確かめる
            if (!this._sent && read < count && this._inner.Position >= this._inner.Length)
            {
                this._sent = true;
                this.Pending = true;
                buffer[offset + read] = 0;
                read++;
            }

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new System.NotSupportedException();

        public override void SetLength(long value) => throw new System.NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();
    }

    /// <summary>
    /// <see cref="EndOfStreamSignal"/> の目印を見て、ストリームの終わりで包んだ Decoder を flush する Decoder。
    /// </summary>
    internal sealed class FlushAtEndDecoder : Decoder
    {
        private readonly Decoder _inner;
        private readonly EndOfStreamSignal _signal;

        public FlushAtEndDecoder(Decoder inner, EndOfStreamSignal signal)
        {
            this._inner = inner;
            this._signal = signal;
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
            => this._inner.GetCharCount(bytes, index, count);

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
            => GetChars(bytes, byteIndex, byteCount, chars, charIndex, flush: false);

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, bool flush)
        {
            if (this._signal.Pending)
            {
                // 最後のバイトは目印。除いたバイトを flush して復号する
                this._signal.Pending = false;
                return this._inner.GetChars(bytes, byteIndex, byteCount - 1, chars, charIndex, flush: true);
            }

            return this._inner.GetChars(bytes, byteIndex, byteCount, chars, charIndex, flush);
        }

        public override void Reset() => this._inner.Reset();
    }
#endif
}
