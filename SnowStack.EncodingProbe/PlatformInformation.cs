namespace SnowStack.EncodingProbe;

/// <summary>
/// 文字エンコーディング判定に関連する実行環境情報
/// </summary>
public sealed record PlatformInformation
{
    /// <summary>OS種別に関する情報</summary>
    public OsInformation Os { get; internal set; } = new();

    /// <summary>.NETランタイムに関する情報</summary>
    public DotNetRuntimeInformation Runtime { get; internal set; } = new();

    /// <summary>ロケール・コードページに関する情報</summary>
    public LocaleInformation Locale { get; internal set; } = new();
}
