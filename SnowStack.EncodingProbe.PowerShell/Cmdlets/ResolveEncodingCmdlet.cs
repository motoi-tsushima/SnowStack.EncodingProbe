using System.Management.Automation;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// ファイルのエンコーディングを判定するPowerShellコマンドレット
/// </summary>
[Cmdlet(VerbsDiagnostic.Resolve, "Encoding", DefaultParameterSetName = "Default")]
[OutputType(typeof(EncodingInfomation))]
public sealed class ResolveEncodingCmdlet : PSCmdlet
{
    [Parameter(ParameterSetName = "License", Mandatory = true)]
    public SwitchParameter License { get; set; }

    [Parameter(ParameterSetName = "Version", Mandatory = true)]
    public SwitchParameter Version { get; set; }


    [Parameter(
        ParameterSetName = "Default",
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "FilePath")]
    public string Path { get; set; } = default!;

    [Parameter(ParameterSetName = "Default", Mandatory = false)]
    public string? Culture { get; set; }

    [Parameter(ParameterSetName = "Default", Mandatory = false)]
    public string? Strategy { get; set; }

    protected override void ProcessRecord()
    {
        // ライセンス情報を表示する
        if (License.IsPresent)
        {
            string licenseText = LicenseAndHelp.License + EncodingProbe.License;
            WriteObject(licenseText);
            return;
        }

        // バージョン情報を表示する
        if (Version.IsPresent)
        {
            var attr = (System.Reflection.AssemblyInformationalVersionAttribute?)
                Attribute.GetCustomAttribute(
                    System.Reflection.Assembly.GetExecutingAssembly(),
                    typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            var version = attr?.InformationalVersion?.Split('+')[0] ?? "unknown";
            WriteObject($"SnowStack.EncodingProbe.PowerShell {version}");
            return;
        }

        EncodingProbe.EncodingDetectorOptions detectorOptions = null;

        // カルチャーを設定
        if (!string.IsNullOrWhiteSpace(Culture))
        {
            ResolveEncodingOptions.ChangeCurrentCulture(Culture);   
            if(detectorOptions == null)
            {
                detectorOptions = new EncodingProbe.EncodingDetectorOptions();
            }
            detectorOptions.Culture = Culture;
        }

        //エンコーディング判定の戦略を設定
        if (!string.IsNullOrWhiteSpace(Strategy))
        {
            var parsedStrategy = ResolveEncodingOptions.ParseStrategy(Strategy);
            if(detectorOptions == null)
            {
                detectorOptions = new EncodingProbe.EncodingDetectorOptions();
            }
            detectorOptions.Strategy = parsedStrategy;
        }

        // PowerShellのパス（相対パス、~等）を解決
        var resolvedPath = GetUnresolvedProviderPathFromPSPath(Path);

        // エンコーディングを判定
        var encodingInfomation = EncodingProbe.Detect(resolvedPath, detectorOptions);

        // 結果をパイプラインに出力
        WriteObject(encodingInfomation);
    }
}
