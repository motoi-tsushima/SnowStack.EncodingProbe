using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// カルチャー名を検証する。解釈できない場合は終了エラーとする。
    /// </summary>
    /// <remarks>
    /// 捕捉した <see cref="CultureNotFoundException"/> を内部例外として持たせない。
    /// この例外のメッセージは .NET Framework と .NET Core で文言が異なり、
    /// そのまま持たせると PowerShell 5.1 と 7.x で見えるメッセージが変わってしまう。
    /// 本モジュールの存在意義は両ホストで同じ結果になることであり、
    /// 利用者に見せる文言は <see cref="ValidationMessages.InvalidCulture"/> だけに絞る。
    /// </remarks>
    private string ValidateCulture(string culture)
    {
        try
        {
            _ = new CultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            ThrowTerminatingError(CreateError(
                new PSArgumentException(ValidationMessages.InvalidCulture(culture)),
                "InvalidCulture",
                ErrorCategory.InvalidArgument,
                culture));
        }

        return culture;
    }

    /// <summary>
    /// 判定方式を検証する。解釈できない場合は終了エラーとする。
    /// </summary>
    private DetectionStrategy ValidateStrategy(string strategy)
    {
        if (ResolveEncodingOptions.TryParseStrategy(strategy, out DetectionStrategy parsed))
        {
            return parsed;
        }

        ThrowTerminatingError(CreateError(
            new PSArgumentException(ValidationMessages.InvalidStrategy(strategy)),
            "InvalidStrategy",
            ErrorCategory.InvalidArgument,
            strategy));

        return default;  // ThrowTerminatingError は戻らないが、コンパイラには分からない
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
                if (Directory.Exists(resolved))
                {
                    WriteError(CreateError(
                        new IOException(ValidationMessages.PathIsNotFile(resolved)),
                        "PathIsNotFile",
                        ErrorCategory.InvalidArgument,
                        resolved));
                    continue;
                }

                if (!File.Exists(resolved))
                {
                    WriteError(CreateError(
                        new FileNotFoundException(ValidationMessages.FileNotFound(resolved), resolved),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolved));
                    continue;
                }

                yield return System.IO.Path.GetFullPath(resolved);
            }
        }
    }

    /// <summary>
    /// 入力されたパスを解決し、書き込み先として使える絶対パスを列挙する。
    /// 実在しないパスも「これから作るファイル」として列挙する。
    /// </summary>
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
            foreach (string resolved in ResolveOne(path, literal))
            {
                if (Directory.Exists(resolved))
                {
                    WriteError(CreateError(
                        new IOException(ValidationMessages.PathIsNotFile(resolved)),
                        "PathIsNotFile",
                        ErrorCategory.InvalidArgument,
                        resolved));
                    continue;
                }

                yield return System.IO.Path.GetFullPath(resolved);
            }
        }
    }

    /// <summary>
    /// 1つのパス指定を解決する。ワイルドカードは複数のパスに展開されうる。
    /// </summary>
    private IEnumerable<string> ResolveOne(string path, bool literal)
    {
        if (literal)
        {
            return new[] { GetUnresolvedProviderPathFromPSPath(path) };
        }

        try
        {
            return GetResolvedProviderPathFromPSPath(path, out _);
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