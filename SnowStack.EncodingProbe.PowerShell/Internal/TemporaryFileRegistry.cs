using System;
using System.Collections.Generic;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 上書きのために書いている途中の一時ファイル（1.3.0 第五次）。
    /// </summary>
    /// <remarks>
    /// <see cref="ProbedFileWriter"/> は上書きを同じフォルダーの一時ファイルに書き、完了したときに置き換える。
    /// 書いている間に、同じプロセスの別のコマンド（またはパイプラインの次のレコード）がワイルドカードを解決すると、
    /// 一時ファイルに一致してしまう（'a','b' | Set-ProbedContent -Path * など）。パスの解決はここに載っているパスを除く。
    /// </remarks>
    internal static class TemporaryFileRegistry
    {
        private static readonly HashSet<string> Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Add(string path)
        {
            lock (Paths)
            {
                Paths.Add(path);
            }
        }

        public static void Remove(string path)
        {
            lock (Paths)
            {
                Paths.Remove(path);
            }
        }

        /// <summary>書いている途中の一時ファイルかどうか</summary>
        public static bool Contains(string path)
        {
            lock (Paths)
            {
                if (Paths.Count == 0)
                {
                    return false;
                }
            }

            // 主ストリームの名前（a.txt::$DATA）を含むパスでも例外にしない（.NET Framework の GetFullPath は投げる）
            if (!FileSystemPath.TryGetFullPath(path, out string fullPath))
            {
                return false;
            }

            lock (Paths)
            {
                return Paths.Contains(fullPath);
            }
        }
    }
}
