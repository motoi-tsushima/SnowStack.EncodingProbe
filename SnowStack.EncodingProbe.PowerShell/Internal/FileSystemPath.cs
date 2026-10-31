using System;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// プロバイダーのパスを、ファイルの読み書きに使う絶対パスにする（1.3.0 第五次の後半）。
    /// </summary>
    /// <remarks>
    /// <c>System.IO.Path.GetFullPath</c> を直接呼ばず、ここを通す。
    /// <list type="bullet">
    /// <item>主ストリームの名前 <c>a.txt::$DATA</c>（大文字小文字は問わない）は、<c>a.txt</c> と同じファイルとして扱う（標準の Set-Content / Get-Content と同じ）。
    /// .NET Framework の GetFullPath はストリームの名前を含むパスに NotSupportedException を投げ、PowerShell 5.1 だけが生の例外で止まっていた（BUG-0004）</item>
    /// <item>代替データストリーム（<c>a.txt:alt</c> など）は、両ホストで <see cref="AlternateDataStreamErrorId"/> のエラーにする</item>
    /// </list>
    /// ストリームの名前は Windows だけのもので、Linux / macOS では <c>:</c> は名前の一部として扱う。
    /// </remarks>
    internal static class FileSystemPath
    {
        /// <summary>代替データストリームを指定された場合のエラー ID</summary>
        public const string AlternateDataStreamErrorId = "AlternateDataStreamNotSupported";

        /// <summary>主ストリームの名前</summary>
        private const string MainStreamSuffix = "::$DATA";

        /// <summary>
        /// 絶対パスにする。代替データストリームを指定されていれば false を返す。
        /// </summary>
        /// <param name="providerPath">プロバイダーが解決したパス</param>
        /// <param name="fullPath">絶対パス（主ストリームの名前は取り除く）</param>
        public static bool TryGetFullPath(string providerPath, out string fullPath)
        {
            string path = providerPath;

            if (PlatformInfo.IsWindows)
            {
                int separator = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                string name = separator < 0 ? path : path.Substring(separator + 1);

                // ドライブだけ（C:）の要素は、区切りの前にしか現れない
                int colon = separator < 0 && path.Length == 2 ? -1 : name.IndexOf(':');

                if (colon >= 0)
                {
                    if (!name.EndsWith(MainStreamSuffix, StringComparison.OrdinalIgnoreCase)
                        || colon != name.Length - MainStreamSuffix.Length)
                    {
                        fullPath = providerPath;
                        return false;
                    }

                    path = path.Substring(0, path.Length - MainStreamSuffix.Length);
                }
            }

            fullPath = System.IO.Path.GetFullPath(path);
            return true;
        }
    }
}
