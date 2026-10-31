using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// Probed 系コマンドレットに共通するパス解決と判定オプションを提供する基底クラス
/// </summary>
public abstract class ProbedContentCommandBase : PSCmdlet
{
    /// <summary>-Path のパラメータセット名</summary>
    protected const string PathParameterSet = "Path";

    /// <summary>-LiteralPath のパラメータセット名</summary>
    protected const string LiteralPathParameterSet = "LiteralPath";

    /// <summary>-Culture / -Strategy から組み立てた判定オプション。どちらも未指定なら null。</summary>
    private EncodingDetectorOptions? _detectorOptions;

    /// <summary>
    /// 文字エンコーディングの判定に用いるカルチャー名（例: "ko-KR"）。
    /// 省略時は実行環境のカルチャーを使用する。
    /// </summary>
    /// <remarks>
    /// バイト列だけでは区別できない組み合わせ（EUC-KR と CP949 など）は
    /// カルチャーで曖昧解消するため、日本語環境で韓国語や中国語のファイルを扱う場合は
    /// このパラメータで対象言語のカルチャーを指定する。
    /// 名前と値は <c>Resolve-Encoding -Culture</c> と同一である。
    /// </remarks>
    [Parameter]
    public string? Culture { get; set; }

    /// <summary>
    /// 文字エンコーディングの判定方式。
    /// Combined（既定）/ NativeOnly / UtfUnknownOnly を指定する。
    /// </summary>
    /// <remarks>
    /// 名前と値は <c>Resolve-Encoding -Strategy</c> と同一である。
    /// </remarks>
    [Parameter]
    public string? Strategy { get; set; }

    /// <summary>
    /// 判定処理へ渡すオプション。-Culture も -Strategy も指定されていない場合は null。
    /// </summary>
    /// <remarks>
    /// null を渡した場合とすべて既定値のオプションを渡した場合とで判定結果は変わらないが、
    /// 未指定のときは null のままにして、既存の呼び出し経路と同一であることを明確にする。
    /// </remarks>
    private protected EncodingDetectorOptions? DetectorOptions => this._detectorOptions;

    /// <summary>
    /// -Culture / -Strategy を検証し、判定オプションを組み立てる。
    /// </summary>
    /// <remarks>
    /// ファイルを開く前に検証する。書き込み系コマンドで不正な値が与えられたときに、
    /// 書きかけの破損ファイルを残さないためである（-Encoding の検証と同じ方針）。
    /// 派生クラスで BeginProcessing を上書きする場合は必ず base を呼ぶこと。
    /// </remarks>
    protected override void BeginProcessing()
    {
        this._detectorOptions = BuildDetectorOptions();
    }

    /// <summary>
    /// -Culture / -Strategy から <see cref="EncodingDetectorOptions"/> を組み立てる。
    /// どちらも未指定の場合は null を返す。
    /// </summary>
    private EncodingDetectorOptions? BuildDetectorOptions()
    {
        bool cultureSpecified = !string.IsNullOrWhiteSpace(this.Culture);
        bool strategySpecified = !string.IsNullOrWhiteSpace(this.Strategy);

        if (!cultureSpecified && !strategySpecified)
        {
            return null;
        }

        var options = new EncodingDetectorOptions();

        if (cultureSpecified)
        {
            options.Culture = ValidateCulture(this.Culture!);
        }

        if (strategySpecified)
        {
            options.Strategy = ValidateStrategy(this.Strategy!);
        }

        return options;
    }

    /// <summary>
    /// カルチャー名を検証する。解釈できない場合は終了エラーとする（<see cref="ResolveEncodingOptions.ValidateCulture"/>）。
    /// </summary>
    private string ValidateCulture(string culture)
    {
        ErrorRecord? error = ResolveEncodingOptions.ValidateCulture(culture);

        if (error != null)
        {
            ThrowTerminatingError(error);
        }

        // 前後の空白を除いた名前を判定に渡す（BUG-0038）
        return ResolveEncodingOptions.TrimCulture(culture);
    }

    /// <summary>
    /// 判定方式を検証する。解釈できない場合は終了エラーとする（<see cref="ResolveEncodingOptions.ValidateStrategy"/>）。
    /// </summary>
    private DetectionStrategy ValidateStrategy(string strategy)
    {
        ErrorRecord? error = ResolveEncodingOptions.ValidateStrategy(strategy, out DetectionStrategy parsed);

        if (error != null)
        {
            ThrowTerminatingError(error);
        }

        return parsed;
    }

    /// <summary>
    /// -EncodingFrom で指定された参照ファイルから継承情報を求める
    /// </summary>
    /// <remarks>
    /// 書き込み系の Set-ProbedContent / Add-ProbedContent / Out-ProbedFile と Convert-ProbedContent が共用する。
    /// 参照先が無い・判定できない場合は終了エラーとする（1 ファイルも処理できないため）。
    /// <br/>
    /// <paramref name="detectionLineCount"/> が 1 以上の場合（1.3.0 の Export-ProbedCsv -DetectionLineCount）は、
    /// 参照ファイルを先頭 N 行で判定し、読み替え、全体を検証する（1.3.0 仕様書 16.2、24 章 U2）。
    /// 検証に失敗した場合は、ID DetectionVerificationFailed の終了エラーとする。
    /// </remarks>
    /// <param name="encodingFrom">-EncodingFrom に与えられた値</param>
    /// <param name="detectionLineCount">判定に使う先頭の行数。0 なら全体で判定する（既定）。</param>
    private protected EncodingSpec ResolveEncodingFrom(string encodingFrom, long detectionLineCount = 0)
    {
        string unresolved = GetUnresolvedProviderPathFromPSPath(encodingFrom);

        if (!FileSystemPath.TryGetFullPath(unresolved, out string reference))
        {
            ThrowTerminatingError(CreateAlternateDataStreamError(unresolved));
        }

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
            if (detectionLineCount > 0 && new FileInfo(reference).Length > 0)
            {
                return HeadLineEncodingResolver.Resolve(reference, detectionLineCount, this.DetectorOptions).ToSpec();
            }

            return EncodingInheritance.FromFile(reference, this.DetectorOptions);
        }
        catch (DetectionVerificationException exception)
        {
            ThrowTerminatingError(CreateError(
                exception, DetectionVerificationException.ErrorId, ErrorCategory.InvalidData, reference));

            throw;  // ThrowTerminatingError は戻らないが、コンパイラには分からない
        }
        catch (EncodingDetectionException exception)
        {
            string errorId = exception.ErrorId == EncodingDetectionException.CodePageNotAvailableId
                ? "EncodingFromCodePageNotAvailable"
                : "EncodingFromDetectionFailed";

            ThrowTerminatingError(CreateError(exception, errorId, ErrorCategory.InvalidData, reference));

            throw;  // ThrowTerminatingError は戻らないが、コンパイラには分からない
        }
        catch (IOException exception)
        {
            // 参照ファイルを読めない（1.3.0 第六次 B。BUG-0026）
            ThrowTerminatingError(CreateError(FileErrors.ReadFailed(reference, exception), "ReadFailed", ErrorCategory.ReadError, reference));

            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            ThrowTerminatingError(CreateError(FileErrors.ReadAccessDenied(reference, exception), "ReadAccessDenied", ErrorCategory.PermissionDenied, reference));

            throw;
        }
    }

    /// <summary>
    /// 入力されたパスを解決し、実在するファイルの絶対パスを列挙する。
    /// 解決できないものは非終了エラーとして報告し、残りの処理は続行する。
    /// </summary>
    /// <param name="paths">-Path または -LiteralPath に与えられた値</param>
    /// <param name="literal">-LiteralPath として扱う場合は true（ワイルドカードを展開しない）</param>
    protected IEnumerable<string> ResolveExistingFiles(IEnumerable<string>? paths, bool literal)
    {
        if (paths == null)
        {
            yield break;
        }

        foreach (string path in paths)
        {
            foreach (string resolved in ResolveOne(path, literal))
            {
                if (!TryGetFullPath(resolved, out string fullPath))
                {
                    continue;
                }

                if (Directory.Exists(fullPath))
                {
                    WriteError(CreateError(
                        new IOException(ValidationMessages.PathIsNotFile(fullPath)),
                        "PathIsNotFile",
                        ErrorCategory.InvalidArgument,
                        fullPath));
                    continue;
                }

                // 同じパイプラインで書き込み中のファイルは読まない（逆向きの往復。1.3.0 第六次 B。BUG-0028）。
                // まだ無いファイル（1 MiB を超える新しいファイルは、完了まで一時ファイルに書く）も、親フォルダーと名前で引き、
                // 当たれば「ファイルが無い」ではなく SamePathRoundTrip にする（利用者の決定）
                if (ReportIfBeingWritten(fullPath))
                {
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    WriteError(CreateError(
                        new FileNotFoundException(ValidationMessages.FileNotFound(fullPath), fullPath),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        fullPath));
                    continue;
                }

                yield return fullPath;
            }
        }
    }

    /// <summary>
    /// 同じパイプラインで書き込み中のファイルなら、その書き込みを元に戻して止めさせ、読み込み側として SamePathRoundTrip（非終了エラー）を報告して true を返す
    /// （1.3.0 第六次 B。BUG-0028）。書き込み側は、次の入力か完了の時点で、自分の SamePathRoundTrip を報告する。
    /// </summary>
    /// <remarks>まだファイルを開いていない時点で呼ぶ（新しいファイルを消して元に戻すため）。</remarks>
    private protected bool ReportIfBeingWritten(string fullPath)
    {
        if (!ActiveWriteRegistry.HasActiveWriters || !ActiveWriteRegistry.AbortWritersOf(FileIdentity.Of(fullPath)))
        {
            return false;
        }

        WriteError(CreateError(
            new PSInvalidOperationException(ValidationMessages.SamePathReadWhileWriting(fullPath)),
            SameFileRoundTripException.ErrorId,
            ErrorCategory.ResourceExists,
            fullPath));

        return true;
    }

    /// <summary>
    /// 入力されたパスを解決し、書き込み先として使える絶対パスを列挙する。
    /// 実在しないパスも「これから作るファイル」として列挙する。
    /// </summary>
    /// <remarks>
    /// ワイルドカードがどのファイルにも一致しなかった場合は、非終了エラー ItemNotFound にする（1.3.0 第五次の後半。BUG-0015）。
    /// ID とカテゴリは標準の Set-Content / Add-Content と同じ（両ホストで実測）。
    /// 読み込み（<see cref="ResolveExistingFiles"/>）は、これまでどおりエラーにしない。
    /// 書き込みでは「書いたはずのデータが書かれていない」ことになり、スクリプトが成功したように見えてしまうため。
    /// </remarks>
    /// <param name="paths">-Path または -LiteralPath に与えられた値</param>
    /// <param name="literal">-LiteralPath として扱う場合は true（ワイルドカードを展開しない）</param>
    protected IEnumerable<string> ResolveWritablePaths(IEnumerable<string>? paths, bool literal)
    {
        if (paths == null)
        {
            yield break;
        }

        foreach (string path in paths)
        {
            // ワイルドカードを含まない存在しないパスは、解決に失敗して「これから作るファイル」として 1 件返る。
            // 0 件になるのは、ワイルドカードがどのファイルにも一致しなかった場合だけ
            List<string> matches = ResolveOne(path, literal).ToList();

            if (matches.Count == 0)
            {
                WriteError(CreateError(
                    new ItemNotFoundException(ValidationMessages.WildcardMatchedNoFileForWrite(path)),
                    "ItemNotFound",
                    ErrorCategory.ObjectNotFound,
                    path));
                continue;
            }

            foreach (string resolved in matches)
            {
                if (!TryGetFullPath(resolved, out string fullPath))
                {
                    continue;
                }

                if (Directory.Exists(fullPath))
                {
                    WriteError(CreateError(
                        new IOException(ValidationMessages.PathIsNotFile(fullPath)),
                        "PathIsNotFile",
                        ErrorCategory.InvalidArgument,
                        fullPath));
                    continue;
                }

                yield return fullPath;
            }
        }
    }

    /// <summary>
    /// プロバイダーのパスを絶対パスにする。代替データストリームを指定されていれば非終了エラーを報告して false を返す。
    /// </summary>
    /// <remarks>
    /// 主ストリームの名前（<c>a.txt::$DATA</c>）は <c>a.txt</c> として扱う（<see cref="FileSystemPath"/>）。
    /// </remarks>
    private protected bool TryGetFullPath(string resolved, out string fullPath)
    {
        if (FileSystemPath.TryGetFullPath(resolved, out fullPath))
        {
            return true;
        }

        WriteError(CreateAlternateDataStreamError(resolved));
        return false;
    }

    /// <summary>
    /// 代替データストリームを指定された場合のエラー（終了エラーにするコマンドも同じものを使う）
    /// </summary>
    private protected static ErrorRecord CreateAlternateDataStreamError(string path)
        => CreateError(
            new PSArgumentException(ValidationMessages.AlternateDataStreamNotSupported(path)),
            FileSystemPath.AlternateDataStreamErrorId,
            ErrorCategory.InvalidArgument,
            path);

    /// <summary>
    /// 1つのパス指定を解決する。ワイルドカードは複数のパスに展開されうる。
    /// </summary>
    private protected IEnumerable<string> ResolveOne(string path, bool literal)
    {
        if (literal)
        {
            return new[] { GetUnresolvedProviderPathFromPSPath(path) };
        }

        try
        {
            // 上書きのために書いている途中の一時ファイルは、ワイルドカードに一致させない（第五次）
            return GetResolvedProviderPathFromPSPath(path, out _).Where(p => !TemporaryFileRegistry.Contains(p)).ToList();
        }
        catch (ItemNotFoundException)
        {
            // ワイルドカードに一致しなかった場合と、存在しないパスを指定された場合。
            // どちらも実在チェックで「見つからない」として報告させる。
            return new[] { GetUnresolvedProviderPathFromPSPath(path) };
        }
    }

    /// <summary>
    /// 非終了エラーとして報告する ErrorRecord を組み立てる
    /// </summary>
    protected static ErrorRecord CreateError(
        System.Exception exception, string errorId, ErrorCategory category, object? target)
        => new ErrorRecord(exception, errorId, category, target);
}