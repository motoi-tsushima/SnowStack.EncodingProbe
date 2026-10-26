using System;
using System.IO;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// ファイルの読み書きの失敗を、本モジュールの 5 言語のメッセージを持つ例外に包み直す（1.3.0 第六次 B）。
    /// </summary>
    /// <remarks>
    /// .NET の例外のメッセージは実行環境で文言が違い（PowerShell 7.x では英語、5.1 では OS の言語）、
    /// 利用者に見せるメッセージが両ホストで変わってしまう（BUG-0025）。
    /// 包み直した例外は元の例外を内部例外に持たない（CLAUDE.md「実行環境依存の例外を利用者に見せない」。
    /// PSCompat はいちばん内側のメッセージを比べる）。
    /// すでに包み直した例外（読み取り専用の検査で作ったものなど）は、そのまま返す。
    /// </remarks>
    internal static class FileErrors
    {
        /// <summary>
        /// 書き込む権限が無い場合の例外（エラー ID WriteAccessDenied に使う）
        /// </summary>
        public static UnauthorizedAccessException WriteAccessDenied(string path, UnauthorizedAccessException original)
            => original is LocalizedAccessDeniedException ? original : new LocalizedAccessDeniedException(ValidationMessages.WriteAccessDenied(path));
    }

    /// <summary>
    /// 本モジュールのメッセージを持つ、アクセスの拒否の例外
    /// </summary>
    internal sealed class LocalizedAccessDeniedException : UnauthorizedAccessException
    {
        public LocalizedAccessDeniedException(string message)
            : base(message)
        {
        }
    }
}
