using System;
using System.IO;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 読み取り専用属性を一時的に外し、破棄したときに元へ戻す（-Force の実体）。
    /// </summary>
    /// <remarks>
    /// 標準の Set-Content / Add-Content / Out-File は、-Force で読み取り専用のファイルへ書き込んだあと
    /// 属性を元に戻す（1.2.0 の実測）。1.1.0 の Set-ProbedContent / Add-ProbedContent は外したままにしており、
    /// 標準との食い違いになっていた。
    /// <br/>
    /// 書き込み系の 4 コマンド（Set-ProbedContent / Add-ProbedContent / Out-ProbedFile / Convert-ProbedContent）
    /// はすべてこの部品を通す。書き込みが途中で失敗した場合も、呼び出し側が破棄すれば属性は戻る。
    /// </remarks>
    internal sealed class ReadOnlyAttributeScope : IDisposable
    {
        private readonly string _path;
        private bool _restored;

        private ReadOnlyAttributeScope(string path)
        {
            this._path = path;
        }

        /// <summary>
        /// ファイルが読み取り専用であれば属性を外し、戻すためのスコープを返す。
        /// 読み取り専用でない（または存在しない）場合は null を返す。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス</param>
        public static ReadOnlyAttributeScope? ClearIfReadOnly(string path)
        {
            if (!IsReadOnly(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            info.Attributes &= ~FileAttributes.ReadOnly;

            return new ReadOnlyAttributeScope(path);
        }

        /// <summary>
        /// ファイルが存在し、読み取り専用属性を持つかどうか
        /// </summary>
        public static bool IsReadOnly(string path)
        {
            var info = new FileInfo(path);

            return info.Exists && (info.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
        }

        /// <summary>
        /// 読み取り専用属性を元に戻す
        /// </summary>
        /// <remarks>
        /// 書き込みの後始末から呼ばれるため、ここで例外を投げて元の例外を覆い隠さない。
        /// ファイルが消えている（置き換えに失敗した等）場合は何もしない。
        /// </remarks>
        public void Dispose()
        {
            if (this._restored)
            {
                return;
            }

            this._restored = true;

            try
            {
                var info = new FileInfo(this._path);

                if (info.Exists)
                {
                    info.Attributes |= FileAttributes.ReadOnly;
                }
            }
            catch (IOException)
            {
                // 後始末であり、元に戻せなくても書き込みの結果は変わらない
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }
    }
}
