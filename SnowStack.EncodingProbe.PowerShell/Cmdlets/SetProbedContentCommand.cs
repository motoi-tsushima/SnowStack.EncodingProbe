using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 文字エンコーディング・BOM・改行コードを明示してテキストファイルへ書き込むコマンドレット
/// </summary>
/// <remarks>
/// -Encoding には統一語彙名を指定する。PowerShell 5.1 と 7.x のどちらで実行しても
/// 同じ名前が同じバイト列になる。BOM 方針の定まらない裸の utf8 は、
/// パラメータ束縛の段階で拒否される（ファイルを開く前に失敗するため、書きかけの破損ファイルが残らない）。
/// <br/>
/// -Encoding を省略した場合は書き込み先の既存ファイルから継承する。これは
/// 「上書きしても既存ファイルの性質を壊さない」ことを意図した既定であり、
/// パイプラインの読み取り元から継承したい場合は -EncodingFrom を使う。
/// </remarks>
[Cmdlet(
    VerbsCommon.Set,
    "ProbedContent",
    DefaultParameterSetName = PathParameterSet,
    SupportsShouldProcess = true)]
public sealed class SetProbedContentCommand : ProbedContentCommandBase, IDisposable
{
    /// <summary>ShouldProcess に表示する操作名</summary>
    private const string OperationName = "Set-ProbedContent";

    /// <summary>
    /// 書き込み先ごとに開いたライター。値が null のものは、
    /// エラーまたは -WhatIf により書き込まないと決まったパスである。
    /// </summary>
    private readonly Dictionary<string, ProbedFileWriter?> _writers =
        new Dictionary<string, ProbedFileWriter?>(PathComparison.Comparer);

    /// <summary>-EncodingFrom から得た継承情報。指定されていない場合は null。</summary>
    private EncodingSpec? _inheritedSpec;

    private bool _disposed;

    /// <summary>
    /// 書き込み先のパス。ワイルドカードを使用できる。
    /// </summary>
    [Parameter(
        ParameterSetName = PathParameterSet,
        Mandatory = true,
        Position = 0,
        ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "FilePath")]
    public string[] Path { get; set; } = default!;

    /// <summary>
    /// 書き込み先のパス。ワイルドカードを展開せず、そのままのパスとして扱う。
    /// </summary>
    [Parameter(
        ParameterSetName = LiteralPathParameterSet,
        Mandatory = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = default!;

    /// <summary>
    /// 書き込む内容。要素ごとに改行を付けて出力する（-NoNewline 指定時を除く）。
    /// </summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromPipeline = true)]
    [AllowNull]
    [AllowEmptyCollection]
    public object[] Value { get; set; } = default!;

    /// <summary>
    /// 書き込みに使用する文字エンコーディング。
    /// 省略時は書き込み先の既存ファイルから継承する。
    /// </summary>
    [Parameter]
    [EncodingSpecTransformation(EncodingUsage.Write)]
    public object Encoding { get; set; } = EncodingSpec.Auto;

    /// <summary>
    /// 文字エンコーディング・BOM・改行コードの継承元とするファイル。
    /// ワイルドカードは展開せず、そのままのパスとして扱う。
    /// </summary>
    [Parameter]
    public string EncodingFrom { get; set; } = default!;

    /// <summary>
    /// 要素間・末尾に改行を出力しない
    /// </summary>
    [Parameter]
    public SwitchParameter NoNewline { get; set; }

    /// <summary>
    /// 改行として出力する文字。省略時は参照情報があればそれを継承し、無ければOS既定に従う。
    /// </summary>
    [Parameter]
    public LineBreakOption LineBreak { get; set; } = LineBreakOption.Auto;

    /// <summary>
    /// 読み取り専用属性の付いたファイルへも書き込む
    /// </summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// パラメータの組み合わせを検証し、-EncodingFrom の継承情報を求める
    /// </summary>
    protected override void BeginProcessing()
    {
        bool encodingBound = this.MyInvocation.BoundParameters.ContainsKey(nameof(this.Encoding));
        bool encodingFromBound = this.MyInvocation.BoundParameters.ContainsKey(nameof(this.EncodingFrom));

        if (encodingBound && encodingFromBound)
        {
            // どちらも文字エンコーディングとBOMを決めようとするため、優先順位を決めようがない
            ThrowTerminatingError(CreateError(
                new PSArgumentException(ValidationMessages.EncodingAndEncodingFromAreExclusive()),
                "EncodingAndEncodingFromAreExclusive",
                ErrorCategory.InvalidArgument,
                null));
        }

        if (this.NoNewline.IsPresent && this.MyInvocation.BoundParameters.ContainsKey(nameof(this.LineBreak)))
        {
            // 矛盾ではないが、指定した改行が使われないことは利用者の意図と異なる可能性がある
            WriteWarning(ValidationMessages.NoNewlineIgnoresLineBreak());
        }

        if (encodingFromBound)
        {
            this._inheritedSpec = ResolveInheritedSpec();
        }
    }

    /// <summary>
    /// 指定されたファイルへ内容を書き込む
    /// </summary>
    protected override void ProcessRecord()
    {
        bool literal = this.ParameterSetName == LiteralPathParameterSet;
        string[] inputPaths = literal ? this.LiteralPath : this.Path;

        foreach (string file in ResolveWritablePaths(inputPaths, literal))
        {
            ProbedFileWriter? writer = GetWriter(file);

            if (writer == null)
            {
                continue;
            }

            WriteValues(writer, file);
        }
    }

    /// <summary>
    /// 開いているファイルをすべて閉じる
    /// </summary>
    protected override void EndProcessing() => CloseWriters();

    /// <summary>
    /// パイプラインが中断された場合もファイルを閉じる
    /// </summary>
    protected override void StopProcessing() => CloseWriters();

    /// <summary>
    /// 終了エラーなどで通常の経路を通らなかった場合に備えて後始末する
    /// </summary>
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        CloseWriters();
    }

    /// <summary>
    /// -EncodingFrom で指定された参照ファイルから継承情報を求める
    /// </summary>
    private EncodingSpec ResolveInheritedSpec()
    {
        string reference = GetUnresolvedProviderPathFromPSPath(this.EncodingFrom);

        if (!File.Exists(reference))
        {
            ThrowTerminatingError(CreateError(
                new FileNotFoundException(ValidationMessages.FileNotFound(reference), reference),
                "EncodingFromNotFound",
                ErrorCategory.ObjectNotFound,
                reference));
        }

        try
        {
            return EncodingInheritance.FromFile(reference);
        }
        catch (EncodingDetectionException exception)
        {
            ThrowTerminatingError(CreateError(
                exception, "EncodingFromDetectionFailed", ErrorCategory.InvalidData, reference));

            throw;  // ThrowTerminatingError は戻らないが、コンパイラには分からない
        }
    }

    /// <summary>
    /// 書き込み先のライターを取得する。まだ開いていなければ開く。
    /// 書き込まないと決まっているパスに対しては null を返す。
    /// </summary>
    private ProbedFileWriter? GetWriter(string file)
    {
        if (this._writers.TryGetValue(file, out ProbedFileWriter? existing))
        {
            return existing;
        }

        ProbedFileWriter? writer = OpenWriter(file);

        // 開けなかった場合も記録しておく。パイプラインで要素が流れてくるたびに
        // 同じエラーを繰り返し報告しないため。
        this._writers[file] = writer;

        return writer;
    }

    /// <summary>
    /// 書き込み先を開く。開かない・開けない場合は非終了エラーを報告して null を返す。
    /// </summary>
    private ProbedFileWriter? OpenWriter(string file)
    {
        if (ActiveReadRegistry.IsBeingRead(file))
        {
            // 同じパイプラインで読み取り中のファイルを切り詰めると、残りを読めなくなる
            WriteError(CreateError(
                new PSInvalidOperationException(ValidationMessages.SamePathRoundTrip(file)),
                "SamePathRoundTrip",
                ErrorCategory.ResourceExists,
                file));

            return null;
        }

        EncodingSpec spec;

        try
        {
            // 継承する場合は、ファイルを切り詰める前に判定を済ませる
            spec = ResolveSpecFor(file);
        }
        catch (FileNotFoundException exception)
        {
            WriteError(CreateError(
                exception, "AutoEncodingRequiresExistingFile", ErrorCategory.ObjectNotFound, file));

            return null;
        }
        catch (EncodingDetectionException exception)
        {
            WriteError(CreateError(exception, "EncodingDetectionFailed", ErrorCategory.InvalidData, file));

            return null;
        }

        if (!ShouldProcess(file, OperationName))
        {
            return null;
        }

        string lineBreak = LineBreakResolver.Resolve(this.LineBreak, spec.LineBreak);

        try
        {
            return ProbedFileWriter.Create(file, spec, lineBreak, this.Force.IsPresent);
        }
        catch (IOException exception)
        {
            WriteError(CreateError(exception, "WriteFailed", ErrorCategory.WriteError, file));
        }
        catch (UnauthorizedAccessException exception)
        {
            WriteError(CreateError(exception, "WriteAccessDenied", ErrorCategory.PermissionDenied, file));
        }

        return null;
    }

    /// <summary>
    /// 書き込み先ごとに使用する文字エンコーディングを決める
    /// </summary>
    /// <exception cref="FileNotFoundException">継承元となる書き込み先が存在しない場合</exception>
    /// <exception cref="EncodingDetectionException">継承元の判定に失敗した場合</exception>
    private EncodingSpec ResolveSpecFor(string file)
    {
        if (this._inheritedSpec != null)
        {
            return this._inheritedSpec;
        }

        EncodingSpec spec = EncodingSpec.FromBoundParameter(this.Encoding);

        if (!spec.IsAuto)
        {
            return spec;
        }

        if (!File.Exists(file))
        {
            throw new FileNotFoundException(
                ValidationMessages.AutoEncodingRequiresExistingFile(file), file);
        }

        return EncodingInheritance.FromFile(file);
    }

    /// <summary>
    /// -Value の各要素を書き込む
    /// </summary>
    private void WriteValues(ProbedFileWriter writer, string file)
    {
        if (this.Value == null)
        {
            return;
        }

        try
        {
            foreach (object? item in this.Value)
            {
                writer.Write(ToText(item));

                if (!this.NoNewline.IsPresent)
                {
                    writer.WriteLineBreak();
                }
            }
        }
        catch (IOException exception)
        {
            WriteError(CreateError(exception, "WriteFailed", ErrorCategory.WriteError, file));
        }
    }

    /// <summary>
    /// 書き込む1要素を文字列に変換する
    /// </summary>
    /// <remarks>
    /// PowerShell の型変換を用いる。実行環境のカルチャーに依存しない不変カルチャーで
    /// 変換されるため、PowerShell 5.1 と 7.x で同じ文字列になる。
    /// </remarks>
    private static string ToText(object? value)
        => value == null ? string.Empty : (LanguagePrimitives.ConvertTo<string>(value) ?? string.Empty);

    /// <summary>
    /// 開いているライターをすべて閉じる
    /// </summary>
    private void CloseWriters()
    {
        foreach (ProbedFileWriter? writer in this._writers.Values)
        {
            writer?.Dispose();
        }

        this._writers.Clear();
    }
}