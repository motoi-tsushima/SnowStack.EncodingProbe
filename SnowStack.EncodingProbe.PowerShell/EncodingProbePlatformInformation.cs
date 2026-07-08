namespace SnowStack.EncodingProbe.PowerShell;

/// <summary>
/// Get-EncodingProbePlatformInfo が返す統合的な実行環境情報
/// </summary>
public sealed record EncodingProbePlatformInformation
{
    /// <summary>OS種別に関する情報</summary>
    public OsInformation Os { get; internal set; } = new();

    /// <summary>.NETランタイムに関する情報</summary>
    public DotNetRuntimeInformation Runtime { get; internal set; } = new();

    /// <summary>ロケール・コードページに関する情報</summary>
    public LocaleInformation Locale { get; internal set; } = new();

    /// <summary>PowerShellホストに関する情報</summary>
    public PowerShellHostInformation PowerShellHost { get; internal set; } = new();

    internal static EncodingProbePlatformInformation FromCore(
        PlatformInformation core,
        PowerShellHostInformation host)
    {
        return new EncodingProbePlatformInformation
        {
            Os = core.Os,
            Runtime = core.Runtime,
            Locale = core.Locale,
            PowerShellHost = host,
        };
    }
}
