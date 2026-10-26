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
    /// </remarks>
    internal sealed class ReaderEncoding : Encoding
    {
        private readonly Encoding _inner;

        public ReaderEncoding(Encoding inner)
            : base(inner.CodePage, inner.EncoderFallback, inner.DecoderFallback)
        {
            this._inner = inner;
        }

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

        public override Decoder GetDecoder() => this._inner.GetDecoder();
    }
}
