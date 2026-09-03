namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 語彙解決の用途。読み取りと書き込みで許容される指定が異なる。
    /// </summary>
    internal enum EncodingUsage
    {
        /// <summary>
        /// 読み取り用途。BOM方針が未指定の指定（裸の utf8 等）と utf7 を許容する。
        /// </summary>
        Read,

        /// <summary>
        /// 書き込み用途。BOM方針が未指定の指定と utf7 を拒否する。
        /// </summary>
        Write,
    }
}