using System;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// ファイルパスの同一性判定を提供する。
    /// </summary>
    /// <remarks>
    /// Windows のファイルシステムは大文字小文字を区別しないため、
    /// 同じファイルを指すパスが表記だけ異なることがある。
    /// 「読み取り中のファイルへの書き込み」や「同一ファイルへの複数回の書き込み」を
    /// 取りこぼさないよう、比較方法をここに集約する。
    /// </remarks>
    internal static class PathComparison
    {
        /// <summary>
        /// パスの比較方法。Windows では大文字小文字を区別しない。
        /// </summary>
        public static readonly StringComparer Comparer =
            PlatformInfo.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}