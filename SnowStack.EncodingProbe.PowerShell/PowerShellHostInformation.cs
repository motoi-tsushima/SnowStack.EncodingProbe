using System;

namespace SnowStack.EncodingProbe.PowerShell;

/// <summary>
/// PowerShellホストに関する情報
/// </summary>
public sealed record PowerShellHostInformation
{
    /// <summary>PowerShellのバージョン 例: 5.1.26100.1591 / 7.5.1</summary>
    public Version PSVersion { get; internal set; } = new(0, 0);

    /// <summary>"Desktop"(Windows PowerShell) または "Core"(PowerShell 6以降)</summary>
    public string PSEdition { get; internal set; } = string.Empty;

    /// <summary>
    /// -Encoding パラメータに数値コードページ(例: 51932)を直接指定できるかどうか。
    /// PSVersionが6.2以降であればtrue。
    /// </summary>
    public bool SupportsNumericCodePageArgument { get; internal set; }

    /// <summary>
    /// -Encoding "ansi" (PS7.4で追加された、廃止されたDefaultの代替値)が使用可能かどうか。
    /// PSVersionが7.4以降であればtrue。
    /// </summary>
    public bool SupportsAnsiEncodingName { get; internal set; }
}
