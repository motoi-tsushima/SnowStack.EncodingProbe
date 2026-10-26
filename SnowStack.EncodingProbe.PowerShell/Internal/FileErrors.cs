using System;
using System.Globalization;
using System.IO;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// ファイルの読み書きの失敗を、本モジュールの 5 言語のメッセージを持つ例外に包み直す（1.3.0 第六次 B）。
    /// </summary>
    /// <remarks>
    /// .NET の例外のメッセージは実行環境で文言が違い（PowerShell 7.x では英語、5.1 では OS の言語）、
    /// 利用者に見せるメッセージが両ホストで変わってしまう（BUG-0025・0026）。
    /// 包み直した例外は元の例外を内部例外に持たない（CLAUDE.md「実行環境依存の例外を利用者に見せない」。
    /// PSCompat はいちばん内側のメッセージを比べる）。HRESULT は引き継ぐ。
    /// すでに包み直した例外（読み取り専用の検査で作ったものなど）は、そのまま返す。
    /// </remarks>
    internal static class FileErrors
    {
        /// <summary>ERROR_SHARING_VIOLATION</summary>
        private const int SharingViolation = unchecked((int)0x80070020);

        /// <summary>ERROR_LOCK_VIOLATION</summary>
        private const int LockViolation = unchecked((int)0x80070021);

        /// <summary>
        /// 書き込む権限が無い場合の例外（エラー ID WriteAccessDenied に使う）
        /// </summary>
        public static UnauthorizedAccessException WriteAccessDenied(string path, UnauthorizedAccessException original)
            => original is LocalizedAccessDeniedException ? original : new LocalizedAccessDeniedException(ValidationMessages.WriteAccessDenied(path), original.HResult);

        /// <summary>
        /// 読み込む権限が無い場合の例外（エラー ID ReadAccessDenied に使う）
        /// </summary>
        public static UnauthorizedAccessException ReadAccessDenied(string path, UnauthorizedAccessException original)
            => original is LocalizedAccessDeniedException ? original : new LocalizedAccessDeniedException(ValidationMessages.ReadAccessDenied(path), original.HResult);

        /// <summary>
        /// 読み込めなかった場合の例外（エラー ID ReadFailed に使う）。共有違反・ロック違反は「ほかのプログラムが使用中」、それ以外はエラー コードを示す。
        /// </summary>
        public static IOException ReadFailed(string path, IOException original)
        {
            if (original is LocalizedIOException)
            {
                return original;
            }

            string message = IsInUse(original)
                ? ValidationMessages.FileInUseForRead(path)
                : ValidationMessages.FileReadFailed(path, "0x" + original.HResult.ToString("X8", CultureInfo.InvariantCulture));

            return new LocalizedIOException(message, original.HResult);
        }

        /// <summary>
        /// 書き込めなかった場合の例外（エラー ID WriteFailed に使う）。共有違反・ロック違反だけを「ほかのプログラムが使用中」に包み直し、
        /// それ以外（ディスクの空き不足など）は元の例外のまま返す。
        /// </summary>
        public static IOException WriteFailed(string path, IOException original)
            => original is LocalizedIOException || !IsInUse(original)
                ? original
                : new LocalizedIOException(ValidationMessages.FileInUseForWrite(path), original.HResult);

        private static bool IsInUse(IOException exception)
            => exception.HResult == SharingViolation || exception.HResult == LockViolation;
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

        public LocalizedAccessDeniedException(string message, int hresult)
            : base(message)
        {
            this.HResult = hresult;
        }
    }

    /// <summary>
    /// 本モジュールのメッセージを持つ、入出力の失敗の例外
    /// </summary>
    internal sealed class LocalizedIOException : IOException
    {
        public LocalizedIOException(string message, int hresult)
            : base(message, hresult)
        {
        }
    }
}
