using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// Set-ProbedContent / Add-ProbedContent に共通する書き込み処理を提供する基底クラス
/// </summary>
/// <remarks>
/// 上書きと追記の違いは、実際にファイルを開く方法（<see cref="CreateWriter"/>）と、
/// 書き込む内容に制約があるかどうか（<see cref="RejectChunk"/>）の2点に集約されている。
/// パラメータ・パス解決・継承・改行の決定・後始末は両者で同一である。
/// </remarks>
public abstract class ProbedContentWriterCommandBase : ProbedContentCommandBase, IDisposable
{
    /// <summary>
    /// 書き込み先ごとに開いた状態。値が null のものは、エラー・-WhatIf・
    /// 書き込みの中止により、以後書き込まないと決まったパスである。
    /// </summary>
    private readonly Dictionary<string, WriteTarget?> _targets =
        new Dictionary<string, WriteTarget?>(PathComparison.Comparer);

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
    /// 使用する文字エンコーディング。省略時は対象ファイルから継承する。
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

    /// <summary>ShouldProcess に表示する操作名</summary>
    protected abstract string OperationName { get; }

    /// <summary>
    /// 書き込みの前に、既存ファイルの文字エンコーディングを必要とするかどうか。
    /// </summary>
    /// <remarks>
    /// 継承のためではなく、書き込む内容の検査のために必要かどうかを表す。
    /// true を返すと、-Encoding を明示していても既存ファイルの判定が走る。
    /// </remarks>
    protected virtual bool RequiresExistingEncoding => false;

    /// <summary>
    /// 書き込み先を開く
    /// </summary>
    private protected abstract ProbedFileWriter CreateWriter(string file, EncodingSpec spec, bool force);

    /// <summary>
    /// 書き込もうとしている文字列を受け付けられない場合に、その理由を返す。
    /// 受け付けられる場合は null を返す。
    /// </summary>
    /// <param name="file">書き込み先の絶対パス</param>
    /// <param name="spec">使用する文字エンコーディングと BOM 方針</param>
    /// <param name="baseline">
    /// 書き込みの前にファイルが持っていた文字エンコーディング。
    /// 新規作成・空ファイル・検査が不要な場合は null。
    /// </param>
    /// <param name="chunk">1要素分の書き込み内容（改行を含む）</param>
    private protected virtual string? RejectChunk(
        string file, EncodingSpec spec, Encoding? baseline, string chunk) => null;

    /// <summary>
    /// パラメータの組み合わせを検証し、-EncodingFrom の継承情報を求める
    /// </summary>
    protected override void BeginProcessing()
    {
        // -Culture / -Strategy の検証を先に済ませる。
        // 直後の -EncodingFrom の判定がその結果を使うためである。
        base.BeginProcessing();

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
            WriteTarget? target = GetTarget(file);

            if (target == null)
            {
                continue;
            }

            WriteValues(target, file);
        }
    }

    /// <summary>
    /// 開いているファイルをすべて閉じる
    /// </summary>
    protected override void EndProcessing() => CloseTargets();

    /// <summary>
    /// パイプラインが中断された場合もファイルを閉じる
    /// </summary>
    protected override void StopProcessing() => CloseTargets();

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
        CloseTargets();
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
            return EncodingInheritance.FromFile(reference, this.DetectorOptions);
        }
        catch (EncodingDetectionException exception)
        {
            string errorId = exception.ErrorId == EncodingDetectionException.CodePageNotAvailableId
                ? "EncodingFromCodePageNotAvailable"
                : "EncodingFromDetectionFailed";

            ThrowTerminatingError(CreateError(exception, errorId, ErrorCategory.InvalidData, reference));

            throw;  // ThrowTerminatingError は戻らないが、コンパイラには分からない
        }
    }

    /// <summary>
    /// 書き込み先の状態を取得する。まだ開いていなければ開く。
    /// 書き込まないと決まっているパスに対しては null を返す。
    /// </summary>
    private WriteTarget? GetTarget(string file)
    {
        if (this._targets.TryGetValue(file, out WriteTarget? existing))
        {
            return existing;
        }

        WriteTarget? target = OpenTarget(file);

        // 開けなかった場合も記録しておく。パイプラインで要素が流れてくるたびに
        // 同じエラーを繰り返し報告しないため。
        this._targets[file] = target;

        return target;
    }

    /// <summary>
    /// 書き込み先を開く。開かない・開けない場合は非終了エラーを報告して null を返す。
    /// </summary>
    private WriteTarget? OpenTarget(string file)
    {
        if (ActiveReadRegistry.IsBeingRead(file))
        {
            // 同じパイプラインで読み取り中のファイルを書き換えると、残りを読めなくなる
            WriteError(CreateError(
                new PSInvalidOperationException(ValidationMessages.SamePathRoundTrip(file)),
                "SamePathRoundTrip",
                ErrorCategory.ResourceExists,
                file));

            return null;
        }

        EncodingSpec spec;
        Encoding? baseline;

        try
        {
            // 既存ファイルの判定は、ファイルを書き換える前に済ませる。
            // 上書きの場合、開いた時点で内容が失われるためここでしか読めない。
            EncodingSpec? existing = ResolveExistingSpec(file);

            spec = ResolveSpec(file, existing);
            baseline = this.RequiresExistingEncoding ? existing?.Encoding : null;
        }
        catch (FileNotFoundException exception)
        {
            WriteError(CreateError(
                exception, "AutoEncodingRequiresExistingFile", ErrorCategory.ObjectNotFound, file));

            return null;
        }
        catch (EncodingDetectionException exception)
        {
            WriteError(CreateError(exception, exception.ErrorId, ErrorCategory.InvalidData, file));

            return null;
        }

        if (!ShouldProcess(file, this.OperationName))
        {
            return null;
        }

        string lineBreak = LineBreakResolver.Resolve(this.LineBreak, spec.LineBreak);

        try
        {
            ProbedFileWriter writer = CreateWriter(file, spec, this.Force.IsPresent);

            return new WriteTarget(writer, spec, lineBreak, baseline);
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
    /// 既存ファイルの状態を求める。読む必要が無い場合や、読んでも情報が無い場合は null を返す。
    /// </summary>
    /// <remarks>
    /// 判定はファイル全体を読むため、必要なときだけ行う。
    /// -Encoding を明示していて内容の検査も不要なら、既存ファイルには触れない。
    /// </remarks>
    /// <exception cref="EncodingDetectionException">判定に失敗した場合</exception>
    private EncodingSpec? ResolveExistingSpec(string file)
    {
        bool inheritsFromTarget =
            this._inheritedSpec == null && EncodingSpec.FromBoundParameter(this.Encoding).IsAuto;

        if (!inheritsFromTarget && !this.RequiresExistingEncoding)
        {
            return null;
        }

        if (EncodingInheritance.IsEmpty(file))
        {
            return null;
        }

        return EncodingInheritance.FromFile(file, this.DetectorOptions);
    }

    /// <summary>
    /// 書き込みに使用する文字エンコーディングを決める
    /// </summary>
    /// <exception cref="FileNotFoundException">継承元となる対象ファイルが存在しない場合</exception>
    private EncodingSpec ResolveSpec(string file, EncodingSpec? existing)
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

        // 0バイトのファイルからは判定できないため、ランタイム既定に落とす
        return existing ?? EncodingInheritance.ForEmptySource();
    }

    /// <summary>
    /// -Value の各要素を書き込む
    /// </summary>
    /// <remarks>
    /// 1レコード分をまとめて検査してから書き込む。途中の要素で拒否された場合に
    /// 手前の要素だけが書き込まれた状態にならないようにするためである。
    /// </remarks>
    private void WriteValues(WriteTarget target, string file)
    {
        if (this.Value == null)
        {
            return;
        }

        var chunks = new List<string>(this.Value.Length);

        foreach (object? item in this.Value)
        {
            string chunk = this.NoNewline.IsPresent
                ? ToText(item)
                : ToText(item) + target.LineBreak;

            string? rejection = RejectChunk(file, target.Spec, target.Baseline, chunk);

            if (rejection != null)
            {
                WriteError(CreateError(
                    new PSInvalidOperationException(rejection),
                    "EncodingChangeOnAppend",
                    ErrorCategory.InvalidData,
                    file));

                AbandonTarget(file);
                return;
            }

            chunks.Add(chunk);
        }

        try
        {
            foreach (string chunk in chunks)
            {
                target.Writer.Write(chunk);
            }
        }
        catch (IOException exception)
        {
            WriteError(CreateError(exception, "WriteFailed", ErrorCategory.WriteError, file));
            AbandonTarget(file);
        }
    }

    /// <summary>
    /// 書き込み先を閉じ、以後そのパスへは書き込まないようにする
    /// </summary>
    private void AbandonTarget(string file)
    {
        if (this._targets.TryGetValue(file, out WriteTarget? target))
        {
            target?.Dispose();
        }

        this._targets[file] = null;
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
    /// 開いている書き込み先をすべて閉じる
    /// </summary>
    private void CloseTargets()
    {
        foreach (WriteTarget? target in this._targets.Values)
        {
            target?.Dispose();
        }

        this._targets.Clear();
    }

    /// <summary>
    /// 開いている書き込み先ひとつ分の状態
    /// </summary>
    private sealed class WriteTarget : IDisposable
    {
        public WriteTarget(ProbedFileWriter writer, EncodingSpec spec, string lineBreak, Encoding? baseline)
        {
            this.Writer = writer;
            this.Spec = spec;
            this.LineBreak = lineBreak;
            this.Baseline = baseline;
        }

        /// <summary>開いているライター</summary>
        public ProbedFileWriter Writer { get; }

        /// <summary>使用する文字エンコーディングと BOM 方針</summary>
        public EncodingSpec Spec { get; }

        /// <summary>改行として出力する文字列</summary>
        public string LineBreak { get; }

        /// <summary>書き込みの前にファイルが持っていた文字エンコーディング</summary>
        public Encoding? Baseline { get; }

        public void Dispose() => this.Writer.Dispose();
    }
}
