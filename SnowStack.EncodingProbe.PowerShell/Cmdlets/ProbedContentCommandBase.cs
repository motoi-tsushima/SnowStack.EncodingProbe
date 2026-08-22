using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// Probed 系コマンドレットに共通するパス解決を提供する基底クラス
/// </summary>
public abstract class ProbedContentCommandBase : PSCmdlet
{
    /// <summary>-Path のパラメータセット名</summary>
    protected const string PathParameterSet = "Path";

    /// <summary>-LiteralPath のパラメータセット名</summary>
    protected const string LiteralPathParameterSet = "LiteralPath";

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