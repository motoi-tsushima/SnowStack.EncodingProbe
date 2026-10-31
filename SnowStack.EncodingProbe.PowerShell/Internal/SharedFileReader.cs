using System;
using System.IO;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 判定・変換のためにファイル全体を読む（1.3.0 第六次 B。BUG-0026）。
    /// </summary>
    /// <remarks>
    /// Get-ProbedContent（<see cref="ProbedFileReader"/>）と同じ共有モード（<see cref="FileShare.ReadWrite"/>）で開く。
    /// <c>File.ReadAllBytes</c> とクラスライブラリの <c>EncodingProbe.Detect(string)</c> は <see cref="FileShare.Read"/> で開くので、
    /// ほかのプログラムが書き込み用に開いているファイル（書き込み中のログなど）を、Get-ProbedContent は読めるのに、
    /// Resolve-Encoding・追記先の判定・Convert-ProbedContent は読めなかった。
    /// 大きさは読む前に調べ、上限を超えていれば読まずに <paramref name="tooLarge"/> の例外を投げる（BUG-0030）。
    /// </remarks>
    internal static class SharedFileReader
    {
        /// <summary>読み込みの共有モード（Get-ProbedContent と同じ）</summary>
        public const FileShare Share = FileShare.ReadWrite;

        /// <summary>
        /// ファイル全体を読む。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス</param>
        /// <param name="maxLength">読める大きさの上限（バイト数）</param>
        /// <param name="tooLarge">上限を超えたときに投げる例外を作る</param>
        /// <exception cref="IOException">開けない・読めない場合</exception>
        /// <exception cref="UnauthorizedAccessException">開く権限が無い場合</exception>
        public static byte[] ReadAll(string path, long maxLength, Func<Exception> tooLarge)
        {
            // バッファを持たずに、長さちょうどを読む（File.ReadAllBytes と同じ）。先読みすると、ほかのハンドルが末尾より先をロックしているときに失敗する
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, Share, 1, FileOptions.SequentialScan))
            {
                long length = stream.Length;

                if (length > maxLength)
                {
                    throw tooLarge();
                }

                BeforeAllocateForTests?.Invoke(length);

                var content = new byte[length];
                int total = 0;

                while (total < content.Length)
                {
                    int read = stream.Read(content, total, content.Length - total);

                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                }

                if (total < content.Length)
                {
                    Array.Resize(ref content, total);
                }

                return content;
            }
        }

        /// <summary>
        /// テスト用: 全体を読むための配列を作る直前に、ファイルの大きさを渡して呼ぶ（メモリ不足を確実に起こす方法が無いため、テストがここで例外を投げる）
        /// </summary>
        internal static Action<long>? BeforeAllocateForTests { get; set; }
    }
}
