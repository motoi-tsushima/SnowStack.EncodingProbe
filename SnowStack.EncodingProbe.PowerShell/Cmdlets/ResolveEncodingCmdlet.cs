using System.Management.Automation;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// ファイルのエンコーディングを判定するPowerShellコマンドレット
/// </summary>
[Cmdlet(VerbsDiagnostic.Resolve, "Encoding", DefaultParameterSetName = "Default")]
[OutputType(typeof(EncodingInformation))]
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

    /// <summary>
    /// 判定に使う先頭の行数（1.3.0）。省略時はファイル全体で判定する（従来どおり）。
    /// </summary>
    /// <remarks>
    /// 行数は物理的な行数（CR-LF / LF / CR で区切られた単位）であり、CSV のレコード数ではない。
    /// 結果は読み替えも検証もせずにそのまま出力する（仕様書 3.1）。
    /// 名前を -TotalCount にしないのは、Get-Content / Get-ProbedContent の -TotalCount（出力する行数）と意味が違うためである。
    /// </remarks>
    [Parameter(ParameterSetName = "Default", Mandatory = false)]
    [ValidateRange(1L, long.MaxValue)]
    public long DetectionLineCount { get; set; }

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

        EncodingDetectorOptions detectorOptions = null;

        // カルチャーを設定。検証はほかのコマンドと同じ部品で行い、同じ ID の終了エラーにする（1.3.0 第五次の後半。BUG-0006）
        if (!string.IsNullOrWhiteSpace(Culture))
        {
            ErrorRecord? cultureError = ResolveEncodingOptions.ValidateCulture(Culture!);
            if (cultureError != null)
            {
                ThrowTerminatingError(cultureError);
            }

            if(detectorOptions == null)
            {
                detectorOptions = new EncodingDetectorOptions();
            }
            // 前後の空白を除いた名前を判定に渡す（1.3.0 第六次 C。BUG-0038）
            detectorOptions.Culture = ResolveEncodingOptions.TrimCulture(Culture!);
        }

        //エンコーディング判定の戦略を設定
        if (!string.IsNullOrWhiteSpace(Strategy))
        {
            ErrorRecord? strategyError = ResolveEncodingOptions.ValidateStrategy(Strategy!, out DetectionStrategy parsedStrategy);
            if (strategyError != null)
            {
                ThrowTerminatingError(strategyError);
            }

            if(detectorOptions == null)
            {
                detectorOptions = new EncodingDetectorOptions();
            }
            detectorOptions.Strategy = parsedStrategy;
        }

        // PowerShellのパス（相対パス、~等）を解決する。主ストリームの名前（a.txt::$DATA）は a.txt として扱う
        string unresolvedPath = GetUnresolvedProviderPathFromPSPath(Path);

        if (!FileSystemPath.TryGetFullPath(unresolvedPath, out string resolvedPath))
        {
            ThrowTerminatingError(new ErrorRecord(
                new PSArgumentException(ValidationMessages.AlternateDataStreamNotSupported(unresolvedPath)),
                FileSystemPath.AlternateDataStreamErrorId,
                ErrorCategory.InvalidArgument,
                unresolvedPath));
            return;
        }

        // ファイルの存在を確認
        if (!System.IO.File.Exists(resolvedPath))
        {
            ThrowTerminatingError(new ErrorRecord(
                new System.IO.FileNotFoundException($"File not found: '{resolvedPath}'"),
                "FileNotFound",
                ErrorCategory.ObjectNotFound,
                resolvedPath));
            return;
        }

        // エンコーディングを判定
        EncodingInformation encodingInfomation;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(DetectionLineCount)))
        {
            // 先頭 N 行だけで判定する。切り出しが判定の上限を超えた場合は、
            // 「ファイルが無い」と同じく終了エラーにする（仕様書 6.2）
            try
            {
                encodingInfomation = HeadLineDetection.Detect(resolvedPath, DetectionLineCount, detectorOptions).Information;
            }
            catch (EncodingDetectionException exception) when (exception.ErrorId == EncodingDetectionException.NotEnoughMemoryId)
            {
                // メモリ不足は新しく足したエラーなので、非終了エラー（1.3.0 第六次 B。BUG-0030。利用者の決定）
                WriteError(new ErrorRecord(exception, exception.ErrorId, ErrorCategory.InvalidData, resolvedPath));
                return;
            }
            catch (EncodingDetectionException exception)
            {
                ThrowTerminatingError(new ErrorRecord(
                    exception,
                    exception.ErrorId,
                    ErrorCategory.InvalidData,
                    resolvedPath));
                return;
            }
            catch (IOException exception)
            {
                ReportReadFailure(exception, resolvedPath);
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                ReportReadFailure(exception, resolvedPath);
                return;
            }
        }
        else
        {
            // ファイル全体を、Get-ProbedContent と同じ共有モードで読んでから判定する（1.3.0 第六次 B。BUG-0026）。
            // 読む前に大きさを調べ、上限を超えていれば -DetectionLineCount を案内する（BUG-0030）。
            // 読めない・大きすぎるは、ファイルごとの失敗として非終了エラーにして次のファイルへ進む
            // （既存の「ファイルが無い」と、-DetectionLineCount の上限を超える場合は、終了エラーのまま。利用者の決定）
            try
            {
                encodingInfomation = EncodingDetectionException.GuardMemory(resolvedPath, () =>
                {
                    byte[] content = SharedFileReader.ReadAll(
                        resolvedPath,
                        EncodingInheritance.MaxDetectableLength,
                        () => new EncodingDetectionException(ValidationMessages.FileTooLargeToDetectUseLineCount(resolvedPath), resolvedPath));
                    return EncodingProbe.Detect(content, detectorOptions);
                });
            }
            catch (EncodingDetectionException exception)
            {
                WriteError(new ErrorRecord(exception, exception.ErrorId, ErrorCategory.InvalidData, resolvedPath));
                return;
            }
            catch (IOException exception)
            {
                ReportReadFailure(exception, resolvedPath);
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                ReportReadFailure(exception, resolvedPath);
                return;
            }
        }

        // 結果をパイプラインに出力
        WriteObject(encodingInfomation);
    }

    /// <summary>
    /// 読めなかったファイルを、5 言語のメッセージの非終了エラーとして報告する（1.3.0 第六次 B。BUG-0026）
    /// </summary>
    private void ReportReadFailure(Exception exception, string path)
    {
        if (exception is UnauthorizedAccessException denied)
        {
            WriteError(new ErrorRecord(FileErrors.ReadAccessDenied(path, denied), "ReadAccessDenied", ErrorCategory.PermissionDenied, path));
            return;
        }

        WriteError(new ErrorRecord(FileErrors.ReadFailed(path, (IOException)exception), "ReadFailed", ErrorCategory.ReadError, path));
    }
}
