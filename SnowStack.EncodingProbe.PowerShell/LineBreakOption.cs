namespace SnowStack.EncodingProbe.PowerShell
{
    /// <summary>
    /// 書き込み系コマンドの -LineBreak パラメータで指定できる改行コード
    /// </summary>
    /// <remarks>
    /// Cr は旧Macintosh形式の改行である。Resolve-Encoding が
    /// <see cref="SnowStack.EncodingProbe.LineBreakType.Cr"/> を返しうるため、
    /// 「検出しうる状態はすべて語彙で表現できる」という原則に合わせて指定可能にしている。
    /// </remarks>
    public enum LineBreakOption
    {
        /// <summary>参照情報があればそれを継承し、無ければOS既定に従う</summary>
        Auto,

        /// <summary>CR-LF (Windows)</summary>
        CrLf,

        /// <summary>LF (Unix/macOS)</summary>
        Lf,

        /// <summary>CR (旧Macintosh)</summary>
        Cr,
    }
}