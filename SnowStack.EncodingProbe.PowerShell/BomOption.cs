namespace SnowStack.EncodingProbe.PowerShell
{
    /// <summary>
    /// Convert-ProbedContent の -Bom パラメータで指定できる BOM の操作
    /// </summary>
    /// <remarks>
    /// BOM を持てるのは Unicode 系 5 系統（utf8 / unicode / bigendianunicode / utf32 / bigendianutf32）だけである。
    /// それ以外の文字エンコーディングに Add を指定するとエラーになり、Remove は何もしない
    /// （外す BOM が無く、要求は満たされているため）。
    /// </remarks>
    public enum BomOption
    {
        /// <summary>BOM を付ける</summary>
        Add,

        /// <summary>BOM を外す</summary>
        Remove,
    }
}
