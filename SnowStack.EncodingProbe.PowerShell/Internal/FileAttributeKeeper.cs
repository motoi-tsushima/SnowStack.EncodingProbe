using System;
using System.IO;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 書き込みの後の Archive 属性と、元に戻したときの属性を扱う（1.3.0 第六次 B）。
    /// </summary>
    /// <remarks>
    /// ファイルを書き換えたら Archive 属性を付ける（Windows のふつうの挙動。標準の Set-Content も付ける）。
    /// 置き換え（ReplaceFile）は元のファイルの属性を写すので、元の属性に Archive が無いと消えたままになり、
    /// バックアップのツールが変更を見落とすおそれがある。
    /// 元に戻した場合は、書き戻すとファイルシステムが Archive 属性を付けるので、書き込みの前の属性に戻す。
    /// 属性を扱えない（権限が無い・ファイルが消えたなど）場合は、書き込みの結果を変えないよう、黙って続ける。
    /// Windows 以外では何もしない（Archive 属性が無い）。
    /// </remarks>
    internal static class FileAttributeKeeper
    {
        /// <summary>今の属性。求められなければ null。</summary>
        public static FileAttributes? TryGet(string path)
        {
            if (!PlatformInfo.IsWindows)
            {
                return null;
            }

            try
            {
                return File.GetAttributes(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Archive 属性を付ける（すでにあれば何もしない）</summary>
        public static void MarkArchive(string path)
        {
            FileAttributes? attributes = TryGet(path);

            if (attributes.HasValue && (attributes.Value & FileAttributes.Archive) == 0)
            {
                Set(path, attributes.Value | FileAttributes.Archive);
            }
        }

        /// <summary>
        /// 書き込みの前の属性に戻す（読み取り専用属性は <see cref="ReadOnlyAttributeScope"/> が戻すので、ここでは今の状態を保つ）
        /// </summary>
        public static void Restore(string path, FileAttributes? before)
        {
            if (!before.HasValue)
            {
                return;
            }

            FileAttributes? now = TryGet(path);

            if (!now.HasValue)
            {
                return;
            }

            FileAttributes target = (before.Value & ~FileAttributes.ReadOnly) | (now.Value & FileAttributes.ReadOnly);

            if (target != now.Value)
            {
                Set(path, target);
            }
        }

        private static void Set(string path, FileAttributes attributes)
        {
            try
            {
                File.SetAttributes(path, attributes);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
