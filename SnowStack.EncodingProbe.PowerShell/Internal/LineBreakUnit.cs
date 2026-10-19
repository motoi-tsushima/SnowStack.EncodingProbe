namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 改行のバイト列の型（ファイルの符号化単位）。先頭 N 行の切り出しで決める。
    /// </summary>
    /// <remarks>
    /// 1.3.0 仕様書 2.3 の 5 つの型に、改行が見つからなかったことを表す <see cref="None"/> を加えたもの。
    /// 文字エンコーディングそのものではなく、改行を探すための単位である。
    /// </remarks>
    internal enum LineBreakUnit
    {
        /// <summary>改行が見つからず、型が決まらなかった</summary>
        None,

        /// <summary>1 バイト（ASCII 互換のすべての文字エンコーディング）。LF = 0A、CR = 0D</summary>
        SingleByte,

        /// <summary>UTF-16LE。LF = 0A 00、CR = 0D 00。2 の倍数の位置から始まる</summary>
        Utf16Le,

        /// <summary>UTF-16BE。LF = 00 0A、CR = 00 0D。2 の倍数の位置から始まる</summary>
        Utf16Be,

        /// <summary>UTF-32LE。LF = 0A 00 00 00、CR = 0D 00 00 00。4 の倍数の位置から始まる</summary>
        Utf32Le,

        /// <summary>UTF-32BE。LF = 00 00 00 0A、CR = 00 00 00 0D。4 の倍数の位置から始まる</summary>
        Utf32Be,
    }
}
