using System.Management.Automation;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

[Cmdlet(VerbsDiagnostic.Resolve, "Encoding")]
[OutputType(typeof(EncodingInfomation))]
public sealed class ResolveEncodingCmdlet : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "FilePath")]
    public string Path { get; set; } = default!;

    protected override void ProcessRecord()
    {
        // PowerShellのパス（相対パス、~等）を解決
        var resolvedPath = GetUnresolvedProviderPathFromPSPath(Path);

        // クラスライブラリ側の判定処理を呼ぶ
        var detector = new EncodingDetector(resolvedPath);
        var result = detector.Detection();

        // 結果をパイプラインに出力
        WriteObject(result);
    }
}
