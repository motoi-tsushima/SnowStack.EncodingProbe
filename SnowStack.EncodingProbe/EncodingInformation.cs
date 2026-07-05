namespace SnowStack.EncodingProbe;

/// <summary>
/// 文字エンコーディング判定情報
/// </summary>
public sealed record EncodingInformation
{
    /// <summary>コードページ</summary>
    public int CodePage { get; internal set; }

    /// <summary>エンコーディング名</summary>
    public string EncodingName { get; internal set; }

    /// <summary>PowerShell -Encoding用のエンコーディング名</summary>
    public string PSEncodingName { get; internal set; }

    /// <summary>
    /// PSEncodingName が有効ならば true、無効ならば false
    /// </summary>
    public bool UsePSName { get; internal set; }

    /// <summary>BOMの有無</summary>
    public bool Bom { get; internal set; }

    /// <summary>改行コードの種類</summary>
    public LineBreakType LineBreak { get; internal set; } = LineBreakType.None;

    /// <summary>実行中のOSがWindowsかどうか</summary>
    public bool IsWindowsOs { get; } = PlatformInfo.IsWindows;

    /// <summary>実行中のOSがmacOSかどうか</summary>
    public bool IsMacOs { get; } = PlatformInfo.IsMacOs;

    /// <summary>実行中のOSがLinuxかどうか</summary>
    public bool IsLinuxOs { get; } = PlatformInfo.IsLinux;

    /// <summary>実行中のカルチャー名</summary>
    public string Culture { get; internal set; } = null;
}

