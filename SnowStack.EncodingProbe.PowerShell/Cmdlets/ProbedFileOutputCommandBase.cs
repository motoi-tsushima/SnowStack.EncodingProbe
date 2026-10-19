using System;
using System.Collections.ObjectModel;
using System.IO;
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

        string fullPath = System.IO.Path.GetFullPath(resolved);

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
    }

    /// <summary>
    /// 1 行分（改行を含む）を書く。必要ならファイルを開き、追記の整合性を検査する。
    /// </summary>
    private protected void WriteChunk(string chunk)
    {
        EnsureWriter();

        if (this.AppendBaseline != null
            && !AppendConsistency.ProducesSameBytes(this.OutputSpec.Encoding!, this.AppendBaseline, chunk))
        {
            // それまでに書いた行は比較が成立した行だけなので、ファイルは追記先のエンコーディングのまま一貫している
            Cleanup();

            ThrowTerminatingError(CreateError(
                new PSInvalidOperationException(ValidationMessages.EncodingChangeOnAppend(
                    this.OutputPath, this.OutputSpec.Encoding!.WebName, this.AppendBaseline.WebName)),
                "EncodingChangeOnAppend",
                ErrorCategory.InvalidData,
                this.OutputPath));
        }

        try
        {
            this._writer!.Write(chunk);
        }
        catch (IOException exception)
        {
            Cleanup();
            ThrowTerminatingError(CreateError(exception, "WriteFailed", ErrorCategory.WriteError, this.OutputPath));
        }
    }

    /// <summary>
    /// 入力が 0 件だったときに、BOM を書かずに出力先を作る（既存なら上書きは切り詰め、追記は触れない）
    /// </summary>
    private protected void CreateEmptyOutput()
    {
        if (this.AppendRequested && File.Exists(this.OutputPath))
        {
            return;
        }

        OpenWriter(emitBom: false).Dispose();
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
            matches = GetResolvedProviderPathFromPSPath(path, out _);
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
            // 同じパイプラインで読み取り中のファイルを書き換えると、残りを読めなくなる
            ThrowTerminatingError(CreateError(
                new PSInvalidOperationException(ValidationMessages.SamePathRoundTrip(this.OutputPath)),
                "SamePathRoundTrip",
                ErrorCategory.ResourceExists,
                this.OutputPath));
        }

        try
        {
            return ProbedFileWriter.Open(
                this.OutputPath, this.OutputSpec, this.ForceRequested, this.AppendRequested, emitBom);
        }
        catch (IOException exception)
        {
            ThrowTerminatingError(CreateError(exception, "WriteFailed", ErrorCategory.WriteError, this.OutputPath));
        }
        catch (UnauthorizedAccessException exception)
        {
            ThrowTerminatingError(CreateError(exception, "WriteAccessDenied", ErrorCategory.PermissionDenied, this.OutputPath));
        }

        throw new InvalidOperationException();  // ThrowTerminatingError は戻らないが、コンパイラには分からない
    }
}
