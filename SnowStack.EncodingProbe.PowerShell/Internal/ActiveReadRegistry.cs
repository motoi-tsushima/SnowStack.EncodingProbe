using System;
using System.Collections.Generic;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 現在読み取り中のファイルパスを記録し、同一ファイルの読み書きを検出する。
    /// </summary>
    /// <remarks>
    /// Get-ProbedContent は行単位でストリーミング出力するため、
    /// Get-ProbedContent a.txt | Set-ProbedContent a.txt と書くと、
    /// 1行目を読んだ時点で下流が同じファイルを切り詰め、残りが読めなくなる。
    /// 本コマンド群は「エンコーディングを保って読んで書き戻す」用途のために作られており、
    /// この書き方を最も自然な形として利用者が選びやすいため、明示的に検出してエラーにする。
    /// <br/>
    /// 記録はスレッド単位で保持する。PowerShell のパイプラインでは、上流の WriteObject から
    /// 下流の ProcessRecord が同じスレッド上で同期的に呼ばれるため、
    /// スレッド単位に閉じておけば、無関係なランスペースが同じファイルを扱っていても
    /// 誤検出しない。
    /// </remarks>
    internal static class ActiveReadRegistry
    {
        [ThreadStatic]
        private static Dictionary<string, int>? _activePaths;

        /// <summary>
        /// パスを読み取り中として登録する。戻り値を破棄すると登録が解除される。
        /// </summary>
        /// <param name="fullPath">正規化済みの絶対パス</param>
        public static IDisposable Register(string fullPath)
        {
            Dictionary<string, int> active = _activePaths ??= new Dictionary<string, int>(PathComparison.Comparer);

            // ワイルドカードで同じファイルが複数回解決される場合に備えて参照数で数える
            active[fullPath] = active.TryGetValue(fullPath, out int count) ? count + 1 : 1;

            return new Registration(fullPath);
        }

        /// <summary>
        /// 指定したパスが、同じスレッド上で読み取り中かどうか
        /// </summary>
        /// <param name="fullPath">正規化済みの絶対パス</param>
        public static bool IsBeingRead(string fullPath)
            => _activePaths != null && _activePaths.ContainsKey(fullPath);

        /// <summary>
        /// 登録を解除する
        /// </summary>
        private static void Release(string fullPath)
        {
            Dictionary<string, int>? active = _activePaths;

            if (active == null || !active.TryGetValue(fullPath, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                active.Remove(fullPath);
            }
            else
            {
                active[fullPath] = count - 1;
            }
        }

        /// <summary>
        /// 読み取り中である期間を表すスコープ
        /// </summary>
        private sealed class Registration : IDisposable
        {
            private readonly string _fullPath;
            private bool _released;

            public Registration(string fullPath)
            {
                this._fullPath = fullPath;
            }

            public void Dispose()
            {
                if (this._released)
                {
                    return;
                }

                this._released = true;
                Release(this._fullPath);
            }
        }
    }
}