namespace SnowStack.EncodingProbe;

/// <summary>
/// 実行中のOS種別に関する情報
/// </summary>
public sealed record OsInformation
{
    /// <summary>実行中のOSがWindowsかどうか</summary>
    public bool IsWindows { get; internal set; }

    /// <summary>実行中のOSがmacOSかどうか</summary>
    public bool IsMacOs { get; internal set; }

    /// <summary>実行中のOSがLinuxかどうか</summary>
    public bool IsLinux { get; internal set; }

    /// <summary>
    /// OSの詳細な説明文字列
    /// (System.Runtime.InteropServices.RuntimeInformation.OSDescription 相当)
    /// </summary>
    public string Description { get; internal set; } = string.Empty;
}
