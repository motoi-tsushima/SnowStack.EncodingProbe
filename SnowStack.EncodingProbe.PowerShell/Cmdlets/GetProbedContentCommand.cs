using System;
using System.IO;
using System.Management.Automation;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 文字エンコーディングを判定してテキストファイルを読み込むコマンドレット
/// </summary>
/// <remarks>
/// BOM は、指定された語彙にかかわらず常に読み飛ばす（原則A）。
/// -Encoding utf8BOM で BOM 無しのファイルを読んでもエラーにはならない。
/// <br/>
/// 既定では行単位でストリーミング出力するため、大容量ファイルでも
/// 標準の Get-Content と同様のメモリ挙動になる。
/// -Raw はファイル全体を1個の文字列として返す。行分割すると行末が CRLF か LF か、
/// 末尾に改行があったかが失われるため、読み込んだ内容を加工して書き戻す用途では
/// -Raw が唯一の無損失な読み取り手段となる。
/// </remarks>
[Cmdlet(VerbsCommon.Get, "ProbedContent", DefaultParameterSetName = PathParameterSet)]
[OutputType(typeof(string))]
public sealed class GetProbedContentCommand : ProbedContentCommandBase
{
    /// <summary>-TotalCount が未指定であることを表す値</summary>
    private const long TotalCountUnlimited = -1;

    /// <summary>
    /// 読み込むファイルのパス。ワイルドカードを使用できる。
    /// </summary>
    [Parameter(
        ParameterSetName = PathParameterSet,
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "FilePath")]
    public string[] Path { get; set; } = default!;

    /// <summary>
    /// 読み込むファイルのパス。ワイルドカードを展開せず、そのままのパスとして扱う。
    /// </summary>
    [Parameter(
        ParameterSetName = LiteralPathParameterSet,
        Mandatory = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = default!;

    /// <summary>
    /// 読み込みに使用する文字エンコーディング。
    /// 省略時は対象ファイルから判定する。明示した場合は判定を行わない。
    /// </summary>
    [Parameter]
    [EncodingSpecTransformation(EncodingUsage.Read)]
    public object Encoding { get; set; } = EncodingSpec.Auto;

    /// <summary>
    /// 行分割せず、ファイル全体を1個の文字列として返す
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <summary>
    /// 各ファイルの先頭から読み込む行数
    /// </summary>
    [Parameter]
    [ValidateRange(0, long.MaxValue)]
    public long TotalCount { get; set; } = TotalCountUnlimited;

    /// <summary>
    /// パラメータの組み合わせを検証する
    /// </summary>
    protected override void BeginProcessing()
    {
        if (this.Raw.IsPresent && this.MyInvocation.BoundParameters.ContainsKey(nameof(this.TotalCount)))
        {
            // -Raw はファイル全体を1個の文字列として返すため、行数の指定と両立しない。
            // 標準の Get-Content も同じ組み合わせを拒否する。
            ThrowTerminatingError(CreateError(
                new PSArgumentException(ValidationMessages.RawAndTotalCountAreExclusive()),
                "RawAndTotalCountAreExclusive",
                ErrorCategory.InvalidArgument,
                null));
        }
    }

    /// <summary>
    /// 指定されたファイルを順に読み込む
    /// </summary>
    protected override void ProcessRecord()
    {
        bool literal = this.ParameterSetName == LiteralPathParameterSet;
        string[] inputPaths = literal ? this.LiteralPath : this.Path;

        foreach (string file in ResolveExistingFiles(inputPaths, literal))
        {
            ReadFile(file);
        }
    }

    /// <summary>
    /// 1ファイルを読み込んでパイプラインに出力する
    /// </summary>
    private void ReadFile(string file)
    {
        EncodingSpec spec = EncodingSpec.FromBoundParameter(this.Encoding);

        try
        {
            if (this.Raw.IsPresent)
            {
                WriteRawContent(file, spec);
                return;
            }

            WriteLines(file, spec);
        }
        catch (EncodingDetectionException exception)
        {
            WriteError(CreateError(exception, exception.ErrorId, ErrorCategory.InvalidData, file));
        }
        catch (IOException exception)
        {
            WriteError(CreateError(exception, "ReadFailed", ErrorCategory.ReadError, file));
        }
        catch (UnauthorizedAccessException exception)
        {
            WriteError(CreateError(exception, "ReadAccessDenied", ErrorCategory.PermissionDenied, file));
        }
    }

    /// <summary>
    /// ファイル全体を1個の文字列として出力する
    /// </summary>
    /// <remarks>
    /// 出力する前にファイルを閉じる。読み終えたあとであれば同じファイルへの書き戻しは安全であり、
    /// Get-ProbedContent a.txt -Raw | Set-ProbedContent a.txt を成立させるためである。
    /// </remarks>
    private void WriteRawContent(string file, EncodingSpec spec)
    {
        string content;

        using (ActiveReadRegistry.Register(file))
        using (ProbedFileReader reader = ProbedFileReader.Open(file, spec))
        {
            content = reader.ReadToEnd();
        }

        WriteObject(content);
    }

    /// <summary>
    /// 行単位でストリーミング出力する
    /// </summary>
    private void WriteLines(string file, EncodingSpec spec)
    {
        long limit = this.MyInvocation.BoundParameters.ContainsKey(nameof(this.TotalCount))
            ? this.TotalCount
            : TotalCountUnlimited;

        // 読み取り中である間は、同じファイルへの書き込みを検出できるようにしておく。
        // 行単位で出力する経路では、下流が書き込みを始めると読み終える前に切り詰められる。
        using (ActiveReadRegistry.Register(file))
        using (ProbedFileReader reader = ProbedFileReader.Open(file, spec))
        {
            long written = 0;

            while (limit == TotalCountUnlimited || written < limit)
            {
                string? line = reader.ReadLine();

                if (line == null)
                {
                    break;
                }

                WriteObject(line);
                written++;
            }
        }
    }
}