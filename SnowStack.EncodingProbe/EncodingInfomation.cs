namespace SnowStack.EncodingProbe;

/// <summary>
/// 文字エンコーディング判定情報
/// </summary>
public sealed record EncodingInfomation
{
    /// <summary>コードページ</summary>
    public int CodePage { get; set; }

    /// <summary>エンコーディング名</summary>
    public string EncodingName { get; set; }

    /// <summary>PowerShell -Encoding用のエンコーディング名</summary>
    public string PSEncodingName { get; set; }

    /// <summary>BOMの有無</summary>
    public bool Bom { get; set; }

    /*--- 一時退避 begin ---
    /// <summary>エンコーディング</summary>
    public Encoding Encoding { get; set; }

    /// <summary>エンコーディングのバリアント（例: HKSCS）</summary>
    public string EncodingVariant { get; set; }
    --- 一時退避 end ---*/

    /// <summary>改行コードの種類</summary>
    public LineBreakType LineBreak { get; set; } = LineBreakType.None;

    /// <summary>実行中のOSがWindowsかどうか</summary>
    public bool IsWindowsOs { get; } = OperatingSystem.IsWindows();

    /// <summary>実行中のOSがmacOSかどうか</summary>
    public bool IsMacOs { get; } = OperatingSystem.IsMacOS();

    /// <summary>実行中のOSがLinuxかどうか</summary>
    public bool IsLinuxOs { get; } = OperatingSystem.IsLinux();
}
