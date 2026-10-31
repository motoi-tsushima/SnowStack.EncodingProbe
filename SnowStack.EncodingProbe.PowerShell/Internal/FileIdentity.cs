using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// パスが指すファイルの実体（1.3.0 第五次の後半。BUG-0004・0016・0017）。
    /// </summary>
    /// <remarks>
    /// 同じファイルの往復（SamePathRoundTrip）、同じ書き込み先の重複、Convert-ProbedContent の N8・N9 を、
    /// パスの文字列ではなく実体で比べるために使う。第五次の前半まではパスの文字列で比べていたため、
    /// <c>\\?\C:\…</c>・UNC の管理共有・<c>\\.\</c>・subst・シンボリックリンク・ジャンクション・ハードリンクで書き方を変えるとすり抜けた。
    /// <list type="bullet">
    /// <item>Windows: ファイルを開いて <c>GetFileInformationByHandle</c> で、ボリュームのシリアル番号とファイル ID を求める（net48・net10.0 共通）。
    /// リンクは最後までたどる。ファイル ID は 64 ビットなので、ReFS では別のファイルを同じと判定しうる（既知の限界）</item>
    /// <item>Linux / macOS: リンクを解決したフルパスで比べる。ハードリンクは別のファイルとみなす（既知の限界）</item>
    /// </list>
    /// まだ存在しないファイル（これから作る書き込み先）は実体を持たないので、「親フォルダーの実体＋名前」（<see cref="LocationKey"/>）で比べる。
    /// 存在するファイルも <see cref="LocationKey"/> を持ち、どちらかが一致すれば同じファイルとする（<see cref="Matches"/>）。
    /// </remarks>
    internal sealed class FileIdentity
    {
        private const uint FileReadAttributes = 0x80;
        private const uint FileShareAll = 0x1 | 0x2 | 0x4;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const int ErrorFileNotFound = 2;
        private const int ErrorPathNotFound = 3;

        /// <summary>名前の比べ方。Windows では大文字小文字を区別しない。</summary>
        private static readonly StringComparer KeyComparer = PathComparison.Comparer;

        private FileIdentity(string path, string? entityKey, string locationKey, int linkCount, string? finalPath, bool isDanglingLink, bool isDirectory)
        {
            this.Path = path;
            this.EntityKey = entityKey;
            this.LocationKey = locationKey;
            this.LinkCount = linkCount;
            this.FinalPath = finalPath;
            this.IsDanglingLink = isDanglingLink;
            this.IsDirectory = isDirectory;
        }

        /// <summary>求めたときのパス</summary>
        public string Path { get; }

        /// <summary>
        /// 実体の鍵（Windows はボリュームのシリアル番号とファイル ID、Linux / macOS はリンクを解決したフルパス）。
        /// リンクの先が存在しない・開けない場合は null。
        /// </summary>
        public string? EntityKey { get; }

        /// <summary>親フォルダーの実体と名前の鍵。存在しないファイルにもある。</summary>
        public string LocationKey { get; }

        /// <summary>ハードリンクの数（Windows のみ。ほかは 1）。存在しない場合は 0。</summary>
        public int LinkCount { get; }

        /// <summary>リンクを最後までたどった実体のパス。存在しない場合は null（Linux / macOS はリンク切れの先のパス）。</summary>
        public string? FinalPath { get; }

        /// <summary>シンボリックリンクで、リンクの先が存在しない場合は true</summary>
        public bool IsDanglingLink { get; }

        /// <summary>実体がフォルダーの場合は true</summary>
        public bool IsDirectory { get; }

        /// <summary>実体が存在するかどうか</summary>
        public bool Exists => this.EntityKey != null;

        /// <summary>
        /// 同じファイルを指すかどうか（実体の鍵か、親フォルダーと名前の鍵のどちらかが一致すれば同じ）
        /// </summary>
        public bool Matches(FileIdentity other)
        {
            if (this.EntityKey != null && other.EntityKey != null && KeyComparer.Equals(this.EntityKey, other.EntityKey))
            {
                return true;
            }

            return KeyComparer.Equals(this.LocationKey, other.LocationKey);
        }

        /// <summary>
        /// パスが指すファイルの実体を求める。開けない場合も例外は投げない（パスの文字列から作った鍵になる）。
        /// </summary>
        /// <param name="fullPath">正規化済みの絶対パス（<see cref="FileSystemPath.TryGetFullPath"/> の結果）</param>
        public static FileIdentity Of(string fullPath)
        {
            if (!PlatformInfo.IsWindows)
            {
                return OfUnix(fullPath);
            }

            string locationKey = BuildLocationKey(fullPath);

            using (SafeFileHandle handle = OpenForQuery(fullPath, followLink: true, out int error))
            {
                if (!handle.IsInvalid && TryGetInformation(handle, out ByHandleFileInformation info))
                {
                    return new FileIdentity(
                        fullPath,
                        BuildEntityKey(info),
                        locationKey,
                        (int)info.NumberOfLinks,
                        GetFinalPath(handle),
                        isDanglingLink: false,
                        isDirectory: (info.FileAttributes & (uint)FileAttributes.Directory) != 0);
                }

                bool dangling = (error == ErrorFileNotFound || error == ErrorPathNotFound) && IsLinkEntry(fullPath);

                return new FileIdentity(fullPath, null, locationKey, 0, null, dangling, isDirectory: false);
            }
        }

        /// <summary>
        /// 開いているファイルの実体を求める（追記で「切り詰めずに開く → 比べる」ため）。
        /// </summary>
        public static FileIdentity FromHandle(SafeFileHandle handle, string fullPath)
        {
            if (!PlatformInfo.IsWindows || !TryGetInformation(handle, out ByHandleFileInformation info))
            {
                return Of(fullPath);
            }

            return new FileIdentity(
                fullPath,
                BuildEntityKey(info),
                BuildLocationKey(fullPath),
                (int)info.NumberOfLinks,
                GetFinalPath(handle),
                isDanglingLink: false,
                isDirectory: false);
        }

        /// <summary>
        /// 開いているファイルの、リンクを最後までたどった実体のパス（Windows）。求められなければ null。
        /// </summary>
        public static string? GetFinalPath(SafeFileHandle handle)
        {
            if (!PlatformInfo.IsWindows)
            {
                return null;
            }

            var buffer = new StringBuilder(512);
            uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);

            if (length > buffer.Capacity)
            {
                buffer = new StringBuilder((int)length + 1);
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            }

            if (length == 0 || length > buffer.Capacity)
            {
                return null;
            }

            return FromExtendedPath(buffer.ToString(0, (int)length));
        }

        /// <summary>
        /// パスが指す実体のパス（リンクを最後までたどる）。実体が無い・求められなければ元のパスを返す。
        /// </summary>
        /// <remarks>
        /// 読み取り専用の属性は、リンクではなくリンクの先の実体に対して調べ、外し、戻す（<see cref="ReadOnlyAttributeScope"/>）。
        /// </remarks>
        public static string ResolveFinalPath(string fullPath)
        {
            FileIdentity identity = Of(fullPath);

            return identity.Exists && identity.FinalPath != null ? identity.FinalPath : fullPath;
        }

        // ---- Windows ----

        private static SafeFileHandle OpenForQuery(string path, bool followLink, out int error)
        {
            uint flags = FileFlagBackupSemantics | (followLink ? 0 : FileFlagOpenReparsePoint);
            SafeFileHandle handle = CreateFileW(ToExtendedPath(path), FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
            error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;

            return handle;
        }

        private static bool TryGetInformation(SafeFileHandle handle, out ByHandleFileInformation info)
        {
            try
            {
                return GetFileInformationByHandle(handle, out info);
            }
            catch (ObjectDisposedException)
            {
                info = default;
                return false;
            }
        }

        /// <summary>リンク自体（リンクをたどらずに開いた項目）が存在し、リパースポイントであるかどうか</summary>
        private static bool IsLinkEntry(string path)
        {
            using (SafeFileHandle handle = OpenForQuery(path, followLink: false, out _))
            {
                return !handle.IsInvalid
                    && TryGetInformation(handle, out ByHandleFileInformation info)
                    && (info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0;
            }
        }

        private static string BuildEntityKey(ByHandleFileInformation info)
            => string.Format(
                CultureInfo.InvariantCulture,
                "{0:X8}:{1:X8}{2:X8}",
                info.VolumeSerialNumber,
                info.FileIndexHigh,
                info.FileIndexLow);

        /// <summary>親フォルダーの実体（開けなければパスの文字列）と名前から鍵を作る</summary>
        private static string BuildLocationKey(string fullPath)
        {
            string name = System.IO.Path.GetFileName(fullPath);
            string? parent = System.IO.Path.GetDirectoryName(fullPath);
            string parentKey;

            if (string.IsNullOrEmpty(parent))
            {
                parentKey = "path:";
            }
            else
            {
                using (SafeFileHandle handle = OpenForQuery(parent!, followLink: true, out _))
                {
                    parentKey = !handle.IsInvalid && TryGetInformation(handle, out ByHandleFileInformation info)
                        ? BuildEntityKey(info)
                        : "path:" + parent;
                }
            }

            return parentKey + "|" + name;
        }

        /// <summary>
        /// Win32 の API に渡すパス。長いパスは <c>\\?\</c> の形にする（PowerShell 5.1 のホストは長いパスに対応していないことがあるため）。
        /// </summary>
        private static string ToExtendedPath(string path)
        {
            if (path.Length < 240 || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return path;
            }

            return path.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + path.Substring(2)
                : @"\\?\" + path;
        }

        /// <summary>
        /// <c>GetFinalPathNameByHandle</c> の結果（<c>\\?\C:\…</c> / <c>\\?\UNC\…</c>）を、ふつうのパスに戻す（長いパスはそのまま）
        /// </summary>
        private static string FromExtendedPath(string path)
        {
            string plain;

            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                plain = @"\\" + path.Substring(8);
            }
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                plain = path.Substring(4);
            }
            else
            {
                return path;
            }

            return plain.Length < 260 ? plain : path;
        }

        // ---- Linux / macOS ----

        private static FileIdentity OfUnix(string fullPath)
        {
            string resolved = ResolveUnixPath(fullPath);
            string? parent = System.IO.Path.GetDirectoryName(fullPath);
            string locationKey = (string.IsNullOrEmpty(parent) ? string.Empty : ResolveUnixPath(parent!))
                + "/" + System.IO.Path.GetFileName(fullPath);

            bool isDirectory = Directory.Exists(resolved);
            bool exists = isDirectory || File.Exists(resolved);
            bool dangling = !exists && IsUnixLink(fullPath);

            return new FileIdentity(
                fullPath,
                exists ? resolved : null,
                locationKey,
                exists ? 1 : 0,
                exists || dangling ? resolved : null,
                dangling,
                isDirectory);
        }

        private static bool IsUnixLink(string path)
        {
#if NETFRAMEWORK
            return false;
#else
            try
            {
                return new FileInfo(path).LinkTarget != null;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
#endif
        }

        /// <summary>
        /// リンクを 1 要素ずつ解決したフルパス（realpath に相当。最後の要素が存在しなくてもよい）。
        /// 解決できない（ループしている）場合は元のパスを返す。
        /// </summary>
        private static string ResolveUnixPath(string fullPath)
        {
#if NETFRAMEWORK
            return fullPath;
#else
            var pending = new List<string>(fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
            pending.Reverse();
            var resolved = new List<string>();
            int hops = 0;

            while (pending.Count > 0)
            {
                string part = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);

                if (part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (resolved.Count > 0)
                    {
                        resolved.RemoveAt(resolved.Count - 1);
                    }

                    continue;
                }

                string candidate = "/" + string.Join("/", resolved) + (resolved.Count > 0 ? "/" : string.Empty) + part;
                string? target = null;

                try
                {
                    target = new FileInfo(candidate).LinkTarget;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (target == null)
                {
                    resolved.Add(part);
                    continue;
                }

                if (++hops > 40)
                {
                    return fullPath;
                }

                if (target.StartsWith("/", StringComparison.Ordinal))
                {
                    resolved.Clear();
                }

                string[] targetParts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);

                for (int i = targetParts.Length - 1; i >= 0; i--)
                {
                    pending.Add(targetParts[i]);
                }
            }

            return "/" + string.Join("/", resolved);
#endif
        }

        // ---- P/Invoke ----

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);
    }
}
