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
    /// 書き込み先ごとに開いた状態。書き込み先はパスの文字列ではなくファイルの実体で区別する（1.3.0 第五次の後半。BUG-0016）。
    /// </summary>
    private readonly WriteTargetTable _targets = new WriteTargetTable();

    /// <summary>-EncodingFrom から得た継承情報。指定されていない場合は null。</summary>
    private EncodingSpec? _inheritedSpec;

    /// <summary>書き込み先のパスをパイプラインから受け取っているかどうか（-Path も -LiteralPath もコマンド行で指定されていない）</summary>
    private bool _pathsFromPipeline;

    private bool _disposed;

    /// <summary>
    /// 書き込み先のパス。ワイルドカードを使用できる。
    /// </summary>
    /// <remarks>
    /// 別名 FullName は -LiteralPath に移した（1.3.0 第五次の後半。BUG-0003）。
    /// FileInfo をパイプラインで渡すと FullName がここに束縛されてワイルドカードとして展開され、
    /// a[1].txt を流すと a1.txt に書いていた。
    /// </remarks>
    [Parameter(
        ParameterSetName = PathParameterSet,
        Mandatory = true,
        Position = 0,
        ValueFromPipelineByPropertyName = true)]
    [Alias("FilePath")]
    public string[] Path { get; set; } = default!;

    /// <summary>
    /// 書き込み先のパス。ワイルドカードを展開せず、そのままのパスとして扱う。
    /// Get-ChildItem の出力は PSPath で、New-Object System.IO.FileInfo の出力は FullName でここに束縛される。
    /// </summary>
    [Parameter(
        ParameterSetName = LiteralPathParameterSet,
        Mandatory = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath", "LP", "FullName")]
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
    /// <remarks>
    /// 決めるのは要素の後ろに付ける改行だけであり、要素の文字列の中に含まれる改行は置き換えない
    /// （標準の Set-Content / Out-File と同じ。1.2.0 で仕様として明文化した）。
    /// ファイル内の改行を統一する用途は Convert-ProbedContent が担う。
    /// </remarks>
    [Parameter]
    public LineBreakOption LineBreak { get; set; } = LineBreakOption.Auto;

    /// <summary>
    /// 読み取り専用属性の付いたファイルへも書き込む。属性は書き込み後に元へ戻す（1.2.0）。
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
    /// パスをパイプラインから受け取っていて、前のレコードと同じ実体が再び現れたときに、書き直すかどうか。
    /// </summary>
    /// <remarks>
    /// Set-ProbedContent は true（前のレコードで書いた内容を取り消して、新しく書く。標準の Set-Content がレコードごとに切り詰め直すのと同じ結果）。
    /// Add-ProbedContent は false（レコードごとに追記する。標準の Add-Content と同じ）。
    /// 1 つのレコードの中で同じ実体が重なった場合は、どちらも 1 回だけ書く。
    /// </remarks>
    protected virtual bool RewritesRepeatedPipelinePath => false;

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
            this._inheritedSpec = ResolveEncodingFrom(this.EncodingFrom);
        }

        // BeginProcessing の時点で束縛されていなければ、パスはパイプラインから来る（標準の Set-Content と同じ見分け方）
        this._pathsFromPipeline =
            !this.MyInvocation.BoundParameters.ContainsKey(nameof(this.Path))
            && !this.MyInvocation.BoundParameters.ContainsKey(nameof(this.LiteralPath));
    }

    /// <summary>
    /// 指定されたファイルへ内容を書き込む
    /// </summary>
    /// <remarks>
    /// 同じ実体を指すパス（a.txt と A.TXT、ハードリンク、\\?\ の書き方など）は、1 つの書き込み先として扱う（BUG-0016）。
    /// 1 つのレコードの中で重なったものは 1 回だけ書く（Add-ProbedContent は標準の 7.x の Add-Content と同じ。
    /// 5.1 の標準は 1 回書いた後に 2 件目で GetContentWriterIOError を出すが、エラーにはしない）。
    /// </remarks>
    protected override void ProcessRecord()
    {
        bool literal = this.ParameterSetName == LiteralPathParameterSet;
        string[] inputPaths = literal ? this.LiteralPath : this.Path;
        var writtenInThisRecord = new List<WriteTargetEntry>();

        foreach (string file in ResolveWritablePaths(inputPaths, literal))
        {
            var identity = FileIdentity.Of(file);
            WriteTargetEntry? entry = this._targets.Find(identity);

            if (entry != null && writtenInThisRecord.Contains(entry))
            {
                continue;
            }

            if (entry != null && entry.Target != null && this._pathsFromPipeline && this.RewritesRepeatedPipelinePath)
            {
                // 前のレコードで書いた内容を取り消して、書き直す（Set-ProbedContent）
                string? kept = RollbackTarget(entry);

                if (kept != null)
                {
                    // 写しから書き戻せなかった。書き直さず、写しのパスを知らせる
                    WriteError(CreateError(
                        new IOException(ValidationMessages.RestoreFromBackupFailed(file, kept)), "WriteFailed", ErrorCategory.WriteError, file));
                    continue;
                }

                this._targets.Remove(entry);
                entry = null;
            }

            if (entry == null)
            {
                entry = this._targets.Add(identity, file, OpenTarget(file));
            }

            writtenInThisRecord.Add(entry);

            if (entry.Target == null)
            {
                continue;
            }

            WriteValues(entry);
        }
    }

    /// <summary>
    /// 開いているファイルをすべて閉じる（書き込みを完了する）。完了に失敗したファイルは元に戻し、非終了エラーを報告する。
    /// </summary>
    protected override void EndProcessing()
    {
        foreach (WriteTargetEntry entry in this._targets.Entries)
        {
            if (entry.Target == null)
            {
                continue;
            }

            ProbedFileWriter writer = entry.Target.Writer;

            try
            {
                writer.Complete();
            }
            catch (UnrepresentableCharacterException exception)
            {
                WriteError(RollbackNotice.Attach(CreateError(
                    exception, UnrepresentableCharacterException.ErrorId, ErrorCategory.InvalidData, entry.Path), entry.Path, writer.KeptBackupPath));
            }
            catch (IOException exception)
            {
                WriteError(RollbackNotice.Attach(
                    CreateError(exception, "WriteFailed", ErrorCategory.WriteError, entry.Path), entry.Path, writer.KeptBackupPath));
            }
            catch (UnauthorizedAccessException exception)
            {
                WriteError(RollbackNotice.Attach(
                    CreateError(exception, "WriteAccessDenied", ErrorCategory.PermissionDenied, entry.Path), entry.Path, writer.KeptBackupPath));
            }
        }

        CloseTargets();
    }

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
    /// 書き込み先を開く。開かない・開けない場合は非終了エラーを報告して null を返す。
    /// </summary>
    /// <remarks>
    /// null を返した場合も、呼び出し側はそれを記録しておく。パイプラインで要素が流れてくるたびに
    /// 同じエラーを繰り返し報告しないため。
    /// </remarks>
    private WriteTarget? OpenTarget(string file)
    {
        if (ActiveReadRegistry.IsBeingRead(file))
        {
            // 同じパイプラインで読み取り中のファイルを書き換えると、残りを読めなくなる。
            // パスの文字列ではなく実体で比べる（\\?\・UNC・subst・リンクの書き方でもすり抜けない。BUG-0004）
            ReportSameFileRoundTrip(new SameFileRoundTripException(file), file);

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

        // ファイルを開かずに分かる失敗は、ShouldProcess より前に報告する。
        // -WhatIf でも、実行すれば失敗することが分かるようにするため（1.2.0 手動確認後の修正）
        if (!CheckWritable(file))
        {
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
        catch (SameFileRoundTripException exception)
        {
            // 追記で、開いたハンドルで比べて初めて分かった場合（何も書いていない）
            ReportSameFileRoundTrip(exception, file);
        }
        catch (LinkTargetNotFoundException exception)
        {
            // Set-ProbedContent で、リンク切れのシンボリックリンク（標準の Set-Content と同じく書かない）
            WriteError(CreateError(exception, LinkTargetNotFoundException.ErrorId, ErrorCategory.ObjectNotFound, file));
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
    /// ファイルを開かずに判定できる書き込みの失敗を検査する。失敗なら非終了エラーを報告して false を返す。
    /// </summary>
    /// <remarks>
    /// エラー ID は、検査しなかった場合にファイルを開いた時点で報告していたものと同じにする
    /// （読み取り専用は WriteAccessDenied、親ディレクトリが無い場合は WriteFailed）。
    /// 読み取り専用以外の理由で書き込めない場合（ACL など）は、実際に開くまで分からないため対象外。
    /// </remarks>
    private bool CheckWritable(string file)
    {
        if (!this.Force.IsPresent && ReadOnlyAttributeScope.IsReadOnly(file))
        {
            WriteError(CreateError(
                new UnauthorizedAccessException(ValidationMessages.FileIsReadOnly(file)),
                "WriteAccessDenied",
                ErrorCategory.PermissionDenied,
                file));

            return false;
        }

        string? parent = System.IO.Path.GetDirectoryName(file);

        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            WriteError(CreateError(
                new DirectoryNotFoundException(ValidationMessages.ParentDirectoryNotFound(file)),
                "WriteFailed",
                ErrorCategory.WriteError,
                file));

            return false;
        }

        return true;
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
    /// <br/>
    /// 表現できない文字・追記の整合性検査（EncodingChangeOnAppend）・書き込みの失敗のどれでも、そのファイルへの書き込みを取りやめ、
    /// ファイルを元に戻す（それまでのレコードで書いた内容も取り消す）。ほかのファイルへの書き込みは続ける。
    /// 「エラーで止まったら、そのファイルには、その呼び出しで書いた分を残さない」（1.3.0 第五次の後半で EncodingChangeOnAppend もそろえた）。
    /// 表現できない文字は、追記の整合性検査より先に調べる。
    /// </remarks>
    private void WriteValues(WriteTargetEntry entry)
    {
        WriteTarget target = entry.Target!;
        string file = entry.Path;

        if (this.Value == null)
        {
            return;
        }

        var texts = new List<string>(this.Value.Length);

        foreach (object? item in this.Value)
        {
            texts.Add(this.NoNewline.IsPresent
                ? ToText(item)
                : ToText(item) + target.LineBreak);
        }

        try
        {
            // 要素の境目で分かれたサロゲートペアも 1 文字として調べるため、レコードをまとめて調べる
            target.Writer.EnsureRepresentable(string.Concat(texts));
        }
        catch (UnrepresentableCharacterException exception)
        {
            ReportUnrepresentable(exception, entry);
            return;
        }

        var chunks = new List<string>(this.Value.Length);

        foreach (string chunk in texts)
        {
            string? rejection = RejectChunk(file, target.Spec, target.Baseline, chunk);

            if (rejection != null)
            {
                // -ErrorAction Stop では WriteError が戻らない（後始末が書き込みを完了してしまう）ので、先に元に戻す
                string? kept = RollbackTarget(entry);
                WriteError(RollbackNotice.Attach(CreateError(
                    new PSInvalidOperationException(rejection),
                    "EncodingChangeOnAppend",
                    ErrorCategory.InvalidData,
                    file), file, kept));

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
        catch (UnrepresentableCharacterException exception)
        {
            ReportUnrepresentable(exception, entry);
        }
        catch (IOException exception)
        {
            // 書きかけのファイルを残さない（第五次）。-ErrorAction Stop では WriteError が戻らないので、先に元に戻す
            string? kept = RollbackTarget(entry);
            WriteError(RollbackNotice.Attach(CreateError(exception, "WriteFailed", ErrorCategory.WriteError, file), file, kept));
        }
    }

    /// <summary>
    /// 表現できない文字を非終了エラーとして報告し、ファイルを元に戻す（ほかのファイルへの書き込みは続ける）
    /// </summary>
    private void ReportUnrepresentable(UnrepresentableCharacterException exception, WriteTargetEntry entry)
    {
        // -ErrorAction Stop では WriteError が戻らない（後始末が書き込みを完了してしまう）ので、先に元に戻す
        string? kept = RollbackTarget(entry);
        WriteError(RollbackNotice.Attach(
            CreateError(exception, UnrepresentableCharacterException.ErrorId, ErrorCategory.InvalidData, entry.Path), entry.Path, kept));
    }

    /// <summary>
    /// 書き込み先が読み取り中のファイルと同じ実体だったことを、非終了エラーとして報告する
    /// </summary>
    private void ReportSameFileRoundTrip(SameFileRoundTripException exception, string file)
        => WriteError(CreateError(
            new PSInvalidOperationException(exception.Message),
            SameFileRoundTripException.ErrorId,
            ErrorCategory.ResourceExists,
            file));

    /// <summary>
    /// 書き込み先を書き込みの前の状態に戻し、以後そのファイルへは書き込まないようにする（第五次）
    /// </summary>
    /// <returns>写しから書き戻せず、一時フォルダーに残した写しのパス（<see cref="ProbedFileWriter.KeptBackupPath"/>）。無ければ null</returns>
    private static string? RollbackTarget(WriteTargetEntry entry)
    {
        ProbedFileWriter? writer = entry.Target?.Writer;
        writer?.Rollback();
        entry.Target = null;

        return writer?.KeptBackupPath;
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
        foreach (WriteTargetEntry entry in this._targets.Entries)
        {
            entry.Target?.Dispose();
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

    /// <summary>
    /// 書き込み先 1 つ分の記録（実体と、最初に現れたときのパス）
    /// </summary>
    private sealed class WriteTargetEntry
    {
        public WriteTargetEntry(FileIdentity identity, string path, WriteTarget? target)
        {
            this.Identity = identity;
            this.Path = path;
            this.Target = target;
        }

        public FileIdentity Identity { get; }

        /// <summary>最初に現れたときのパス（エラーの対象・メッセージに使う）</summary>
        public string Path { get; }

        /// <summary>
        /// 開いている書き込み先。null は、エラー・-WhatIf・書き込みの中止により、以後書き込まないと決まったもの。
        /// </summary>
        public WriteTarget? Target { get; set; }
    }

    /// <summary>
    /// 書き込み先の表。実体の鍵と、親フォルダーと名前の鍵の両方で引く（<see cref="FileIdentity.Matches"/> と同じ規則）。
    /// </summary>
    private sealed class WriteTargetTable
    {
        private readonly List<WriteTargetEntry> _entries = new List<WriteTargetEntry>();
        private readonly Dictionary<string, WriteTargetEntry> _byEntity = new Dictionary<string, WriteTargetEntry>(PathComparison.Comparer);
        private readonly Dictionary<string, WriteTargetEntry> _byLocation = new Dictionary<string, WriteTargetEntry>(PathComparison.Comparer);

        /// <summary>記録した順の書き込み先</summary>
        public IEnumerable<WriteTargetEntry> Entries => this._entries;

        public WriteTargetEntry? Find(FileIdentity identity)
        {
            if (identity.EntityKey != null && this._byEntity.TryGetValue(identity.EntityKey, out WriteTargetEntry? byEntity))
            {
                return byEntity;
            }

            return this._byLocation.TryGetValue(identity.LocationKey, out WriteTargetEntry? byLocation) ? byLocation : null;
        }

        public WriteTargetEntry Add(FileIdentity identity, string path, WriteTarget? target)
        {
            var entry = new WriteTargetEntry(identity, path, target);
            this._entries.Add(entry);
            Index(identity, entry);

            if (identity.EntityKey == null && target != null)
            {
                // 追記で新しく作ったファイルは、開いた後に実体の鍵を持つ（リンクなど別の書き方で現れても引けるようにする）
                Index(FileIdentity.Of(path), entry);
            }

            return entry;
        }

        public void Remove(WriteTargetEntry entry)
        {
            this._entries.Remove(entry);
            RemoveFrom(this._byEntity, entry);
            RemoveFrom(this._byLocation, entry);
        }

        public void Clear()
        {
            this._entries.Clear();
            this._byEntity.Clear();
            this._byLocation.Clear();
        }

        private void Index(FileIdentity identity, WriteTargetEntry entry)
        {
            if (identity.EntityKey != null)
            {
                this._byEntity[identity.EntityKey] = entry;
            }

            this._byLocation[identity.LocationKey] = entry;
        }

        private static void RemoveFrom(Dictionary<string, WriteTargetEntry> index, WriteTargetEntry entry)
        {
            var keys = new List<string>();

            foreach (KeyValuePair<string, WriteTargetEntry> pair in index)
            {
                if (ReferenceEquals(pair.Value, entry))
                {
                    keys.Add(pair.Key);
                }
            }

            foreach (string key in keys)
            {
                index.Remove(key);
            }
        }
    }
}
