using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 出力先が常に 1 件の書き込み系コマンドレット（Out-ProbedFile / Export-ProbedCsv）に共通する殻
/// </summary>
/// <remarks>
/// 1.2.0 で Out-ProbedFile に実装した規則を、1.3.0 の Export-ProbedCsv と共用するために切り出した（1.3.0 指示書 4.5）。
/// 挙動は切り出す前の Out-ProbedFile と同じである。
/// <list type="bullet">
/// <item>出力先のパスはちょうど 1 件に解決されること（1.2.0 仕様書 2.12）</item>
/// <item>-NoClobber と読み取り専用の検査は、ファイルを変更しないので ShouldProcess より前に行う（2.9、2.10）</item>
/// <item>ファイルを開く（作成・切り詰める）のは最初の行を書く直前。同一パスの往復を検出するため（2.10）</item>
/// <item>BOM は 1 行でも書いたときにだけ書く。入力が 0 件なら、上書きは 0 バイトに切り詰め、追記は既存ファイルに触れない（2.11）</item>
/// <item>追記の整合性検査は、各行を書く前のバイト列の比較で行う（2.8）</item>
/// <item>失敗はすべて終了エラー。パイプラインの停止・破棄でもファイルを閉じ、読み取り専用の属性を戻す</item>
/// </list>
/// 文字エンコーディングの決定（どの経路で何を判定するか）はコマンドごとに違うので、派生クラスで行う。
/// </remarks>
public abstract class ProbedFileOutputCommandBase : ProbedContentCommandBase, IDisposable
{
    /// <summary>書き込み先の絶対パス。BeginProcessing で決まる。</summary>
    private protected string OutputPath = default!;

    /// <summary>書き込みに使う文字エンコーディングと BOM 方針</summary>
    private protected EncodingSpec OutputSpec = default!;

    /// <summary>
    /// 追記の整合性検査に使う、追記先の文字エンコーディング。
    /// 検査しない場合（上書き・追記先が無い・0 バイト・-AllowEncodingChange）は null。
    /// </summary>
    private protected Encoding? AppendBaseline;

    /// <summary>追記先が 1 バイト以上あるかどうか（BOM を書かない条件）</summary>
    private protected bool AppendToExistingContent;

    /// <summary>開いているライター。最初の行を書く直前に開く。</summary>
    private ProbedFileWriter? _writer;

    private bool _disposed;

    /// <summary>ファイルを開いたかどうか（1 行でも書いたかどうか）</summary>
    private protected bool WriterOpened => this._writer != null;

    /// <summary>ShouldProcess に表示する操作名</summary>
    private protected abstract string OperationName { get; }

    /// <summary>-Append が指定されたかどうか</summary>
    private protected abstract bool AppendRequested { get; }

    /// <summary>-Force が指定されたかどうか</summary>
    private protected abstract bool ForceRequested { get; }

    /// <summary>
    /// パイプラインが中断された場合もファイルを閉じ、読み取り専用属性を戻す
    /// </summary>
    protected override void StopProcessing() => Cleanup();

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
        Cleanup();
    }

    /// <summary>
    /// 出力先のパスを解決する。ちょうど 1 件に決まらなければ終了エラー。
    /// </summary>
    /// <param name="path">-Path（-FilePath）または -LiteralPath に与えられた値</param>
    /// <param name="literal">-LiteralPath として扱う場合は true（ワイルドカードを展開しない）</param>
    private protected string ResolveOutputPath(string path, bool literal)
    {
        string resolved;

        if (literal)
        {
            resolved = GetUnresolvedProviderPathFromPSPath(path);
        }
        else if (WildcardPattern.ContainsWildcardCharacters(path))
        {
            resolved = ResolveWildcardPath(path);
        }
        else
        {
            resolved = GetUnresolvedProviderPathFromPSPath(path);
        }

        // 主ストリームの名前（a.txt::$DATA）は a.txt として扱い、代替データストリームは終了エラーにする
        if (!FileSystemPath.TryGetFullPath(resolved, out string fullPath))
        {
            ThrowTerminatingError(CreateAlternateDataStreamError(resolved));
        }

        if (Directory.Exists(fullPath))
        {
            ThrowTerminatingError(CreateError(
                new IOException(ValidationMessages.PathIsNotFile(fullPath)),
                "PathIsNotFile",
                ErrorCategory.InvalidArgument,
                fullPath));
        }

        string? parent = System.IO.Path.GetDirectoryName(fullPath);

        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            ThrowTerminatingError(CreateError(
                new DirectoryNotFoundException(ValidationMessages.ParentDirectoryNotFound(fullPath)),
                "ParentDirectoryNotFound",
                ErrorCategory.ObjectNotFound,
                fullPath));
        }

        return fullPath;
    }

    /// <summary>
    /// -NoClobber と読み取り専用の検査（ファイルを変更しないので ShouldProcess より前に行う）
    /// </summary>
    /// <param name="exists">出力先が存在するかどうか</param>
    /// <param name="noClobber">-NoClobber が指定されたかどうか</param>
    private protected void CheckNoClobberAndReadOnly(bool exists, bool noClobber)
    {
        if (exists && noClobber && !this.AppendRequested)
        {
            // -Force は -NoClobber を解除しない（標準と同じ）
            ThrowTerminatingError(CreateError(
                new IOException(ValidationMessages.NoClobberFileExists(this.OutputPath)),
                "NoClobber",
                ErrorCategory.ResourceExists,
                this.OutputPath));
        }

        if (!this.ForceRequested && ReadOnlyAttributeScope.IsReadOnly(this.OutputPath))
        {
            ThrowTerminatingError(CreateError(
                new UnauthorizedAccessException(ValidationMessages.FileIsReadOnly(this.OutputPath)),
                "WriteAccessDenied",
                ErrorCategory.PermissionDenied,
                this.OutputPath));
        }
    }

    /// <summary>
    /// 出力先の既存ファイルを全体で判定する。失敗は終了エラー。
    /// </summary>
    private protected EncodingSpec DetectExisting()
    {
        try
        {
            return EncodingInheritance.FromFile(this.OutputPath, this.DetectorOptions);
        }
        catch (EncodingDetectionException exception)
        {
            ThrowTerminatingError(CreateError(exception, exception.ErrorId, ErrorCategory.InvalidData, this.OutputPath));

            throw;  // ThrowTerminatingError は戻らないが、コンパイラには分からない
        }
        catch (IOException exception)
        {
            // 出力先・追記先を読めない（1.3.0 第六次 B。BUG-0026）
            ThrowTerminatingError(CreateError(FileErrors.ReadFailed(this.OutputPath, exception), "ReadFailed", ErrorCategory.ReadError, this.OutputPath));

            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            ThrowTerminatingError(CreateError(FileErrors.ReadAccessDenied(this.OutputPath, exception), "ReadAccessDenied", ErrorCategory.PermissionDenied, this.OutputPath));

            throw;
        }
    }

    /// <summary>
    /// 1 行分（改行を含む）を書く。必要ならファイルを開き、追記の整合性を検査する。
    /// </summary>
    /// <remarks>
    /// 表現できない文字・追記の整合性検査（EncodingChangeOnAppend）・書き込みの失敗のどれでも、それまでに書いた行も取り消して
    /// ファイルを元に戻し（追記なら追記を始める前の長さに切り詰め、追記先が無かった場合は作ったファイルを消す）、終了エラーにする。
    /// 「エラーで止まったら、そのファイルには、その呼び出しで書いた分を残さない」（第五次。EncodingChangeOnAppend は後半でそろえた）。
    /// 表現できない文字は、追記の整合性検査より先に調べる。
    /// </remarks>
    private protected void WriteChunk(string chunk)
    {
        EnsureWriter();

        try
        {
            this._writer!.EnsureRepresentable(chunk);
        }
        catch (UnrepresentableCharacterException exception)
        {
            string? kept = RollbackOutput();
            ThrowTerminatingError(RollbackNotice.Attach(CreateError(
                exception, UnrepresentableCharacterException.ErrorId, ErrorCategory.InvalidData, this.OutputPath), this.OutputPath, kept));
        }

        if (this.AppendBaseline != null
            && !AppendConsistency.ProducesSameBytes(this.OutputSpec.Encoding!, this.AppendBaseline, chunk))
        {
            // -ErrorAction Stop の場合も含め、元に戻してからエラーにする
            string? kept = RollbackOutput();

            ThrowTerminatingError(RollbackNotice.Attach(CreateError(
                new PSInvalidOperationException(ValidationMessages.EncodingChangeOnAppend(
                    this.OutputPath, this.OutputSpec.Encoding!.WebName, this.AppendBaseline.WebName)),
                "EncodingChangeOnAppend",
                ErrorCategory.InvalidData,
                this.OutputPath), this.OutputPath, kept));
        }

        try
        {
            this._writer!.Write(chunk);
        }
        catch (UnrepresentableCharacterException exception)
        {
            string? kept = RollbackOutput();
            ThrowTerminatingError(RollbackNotice.Attach(CreateError(
                exception, UnrepresentableCharacterException.ErrorId, ErrorCategory.InvalidData, this.OutputPath), this.OutputPath, kept));
        }
        catch (IOException exception)
        {
            // 書きかけのファイルを残さない（第五次）
            string? kept = RollbackOutput();
            ThrowTerminatingError(RollbackNotice.Attach(
                CreateError(FileErrors.WriteFailed(this.OutputPath, exception), "WriteFailed", ErrorCategory.WriteError, this.OutputPath), this.OutputPath, kept));
        }
        catch (UnauthorizedAccessException exception)
        {
            // 書く内容が境目を超えて書き方を切り替えたときに、切り替えた先を開けなかった（第五次の仕上げ）
            string? kept = RollbackOutput();
            ThrowTerminatingError(RollbackNotice.Attach(
                CreateError(FileErrors.WriteAccessDenied(this.OutputPath, exception), "WriteAccessDenied", ErrorCategory.PermissionDenied, this.OutputPath), this.OutputPath, kept));
        }
    }

    /// <summary>
    /// 書き込みを完了する（上書きは一時ファイルで置き換える）。失敗した場合はファイルを元に戻して終了エラー。
    /// 派生クラスの EndProcessing が、最後の行を書いた後（入力が 0 件なら <see cref="CreateEmptyOutput"/> の後）に呼ぶ。
    /// </summary>
    private protected void CompleteOutput()
    {
        ProbedFileWriter? writer = this._writer;

        if (writer == null)
        {
            return;
        }

        try
        {
            writer.Complete();
        }
        catch (UnrepresentableCharacterException exception)
        {
            Cleanup();
            ThrowTerminatingError(RollbackNotice.Attach(CreateError(
                exception, UnrepresentableCharacterException.ErrorId, ErrorCategory.InvalidData, this.OutputPath), this.OutputPath, writer.KeptBackupPath));
        }
        catch (IOException exception)
        {
            Cleanup();
            ThrowTerminatingError(RollbackNotice.Attach(
                CreateError(FileErrors.WriteFailed(this.OutputPath, exception), "WriteFailed", ErrorCategory.WriteError, this.OutputPath), this.OutputPath, writer.KeptBackupPath));
        }
        catch (UnauthorizedAccessException exception)
        {
            Cleanup();
            ThrowTerminatingError(RollbackNotice.Attach(
                CreateError(FileErrors.WriteAccessDenied(this.OutputPath, exception), "WriteAccessDenied", ErrorCategory.PermissionDenied, this.OutputPath), this.OutputPath, writer.KeptBackupPath));
        }
    }

    /// <summary>
    /// ファイルを書き込みの前の状態に戻し、後始末をする
    /// </summary>
    /// <returns>写しから書き戻せず、一時フォルダーに残した写しのパス（<see cref="ProbedFileWriter.KeptBackupPath"/>）。無ければ null</returns>
    private string? RollbackOutput()
    {
        ProbedFileWriter? writer = this._writer;
        writer?.Rollback();
        Cleanup();

        return writer?.KeptBackupPath;
    }

    /// <summary>
    /// 入力が 0 件だったときに、BOM を書かずに出力先を作る（既存なら上書きは 0 バイトにし、追記は触れない）。
    /// 書き込みの完了は <see cref="CompleteOutput"/> で行う。
    /// </summary>
    private protected void CreateEmptyOutput()
    {
        if (this.AppendRequested && File.Exists(this.OutputPath))
        {
            return;
        }

        this._writer = OpenWriter(emitBom: false);
    }

    /// <summary>
    /// ファイルを閉じる（読み取り専用属性もここで戻る）。派生クラスは自分の後始末を足してから base を呼ぶ。
    /// </summary>
    private protected virtual void Cleanup()
    {
        ProbedFileWriter? writer = this._writer;
        this._writer = null;
        writer?.Dispose();
    }

    /// <summary>
    /// ワイルドカードを含むパスを、ちょうど 1 件の既存ファイルに解決する
    /// </summary>
    /// <remarks>
    /// 0 件のとき新規作成しないのは標準の Out-File と同じ。c[1].txt のような名前は -LiteralPath で作る。
    /// </remarks>
    private string ResolveWildcardPath(string path)
    {
        Collection<string> matches;

        try
        {
            // 上書きのために書いている途中の一時ファイルは、ワイルドカードに一致させない（第五次）
            matches = new Collection<string>(
                GetResolvedProviderPathFromPSPath(path, out _).Where(p => !TemporaryFileRegistry.Contains(p)).ToList());
        }
        catch (ItemNotFoundException)
        {
            matches = new Collection<string>();
        }

        if (matches.Count == 0)
        {
            ThrowTerminatingError(CreateError(
                new FileNotFoundException(ValidationMessages.WildcardMatchedNoFile(path), path),
                "FileNotFound",
                ErrorCategory.ObjectNotFound,
                path));
        }

        if (matches.Count > 1)
        {
            ThrowTerminatingError(CreateError(
                new PSInvalidOperationException(ValidationMessages.WildcardMatchedMultipleFiles(path, matches.Count)),
                "MultipleFilesNotSupported",
                ErrorCategory.InvalidArgument,
                path));
        }

        return matches[0];
    }

    /// <summary>
    /// 最初の行を書く直前にファイルを開く
    /// </summary>
    private void EnsureWriter()
    {
        if (this._writer != null)
        {
            return;
        }

        // BOM は、上書き・新規作成と、追記先が無い・0 バイトの追記のときだけ書く
        bool emitBom = this.OutputSpec.EmitBom == true && !this.AppendToExistingContent;

        this._writer = OpenWriter(emitBom);
    }

    /// <summary>
    /// ファイルを開く。同一パスの往復と、開けなかった場合は終了エラー。
    /// </summary>
    private ProbedFileWriter OpenWriter(bool emitBom)
    {
        if (ActiveReadRegistry.IsBeingRead(this.OutputPath))
        {
            // 同じパイプラインで読み取り中のファイルを書き換えると、残りを読めなくなる。
            // パスの文字列ではなく実体で比べる（\\?\・UNC・subst・リンクの書き方でもすり抜けない。BUG-0004）
            ThrowSameFileRoundTrip();
        }

        try
        {
            return ProbedFileWriter.Open(
                this.OutputPath, this.OutputSpec, this.ForceRequested, this.AppendRequested, emitBom);
        }
        catch (SameFileRoundTripException)
        {
            // 追記で、開いたハンドルで比べて初めて分かった場合（何も書いていない）
            ThrowSameFileRoundTrip();
        }
        catch (IOException exception)
        {
            ThrowTerminatingError(CreateError(FileErrors.WriteFailed(this.OutputPath, exception), "WriteFailed", ErrorCategory.WriteError, this.OutputPath));
        }
        catch (UnauthorizedAccessException exception)
        {
            ThrowTerminatingError(CreateError(FileErrors.WriteAccessDenied(this.OutputPath, exception), "WriteAccessDenied", ErrorCategory.PermissionDenied, this.OutputPath));
        }

        throw new InvalidOperationException();  // ThrowTerminatingError は戻らないが、コンパイラには分からない
    }

    private void ThrowSameFileRoundTrip()
        => ThrowTerminatingError(CreateError(
            new PSInvalidOperationException(ValidationMessages.SamePathRoundTrip(this.OutputPath)),
            SameFileRoundTripException.ErrorId,
            ErrorCategory.ResourceExists,
            this.OutputPath));
}
