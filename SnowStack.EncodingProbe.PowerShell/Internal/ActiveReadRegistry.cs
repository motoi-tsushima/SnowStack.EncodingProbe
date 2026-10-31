using System;
using System.Collections.Generic;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 現在読み取り中のファイルを記録し、同一ファイルの読み書きを検出する。
    /// </summary>
    /// <remarks>
    /// Get-ProbedContent は行単位でストリーミング出力するため、
    /// Get-ProbedContent a.txt | Set-ProbedContent a.txt と書くと、
    /// 1行目を読んだ時点で下流が同じファイルを切り詰め、残りが読めなくなる。
    /// 本コマンド群は「エンコーディングを保って読んで書き戻す」用途のために作られており、
    /// この書き方を最も自然な形として利用者が選びやすいため、明示的に検出してエラーにする。
    /// <br/>
    /// 読み取り中のファイルは、パスの文字列ではなくファイルの実体（<see cref="FileIdentity"/>）で記録し、比べる（1.3.0 第五次の後半。BUG-0004）。
    /// パスの文字列で比べていたため、<c>\\?\</c>・UNC の管理共有・subst・リンクなどで書き方を変えるとすり抜け、
    /// 読み込み中のファイルが黙って切り詰められ、Add-ProbedContent は終わらずにファイルが増え続けた。
    /// <br/>
    /// 記録はスレッド単位で保持する。PowerShell のパイプラインでは、上流の WriteObject から
    /// 下流の ProcessRecord が同じスレッド上で同期的に呼ばれるため、
    /// スレッド単位に閉じておけば、無関係なランスペースが同じファイルを扱っていても
    /// 誤検出しない。
    /// </remarks>
    internal static class ActiveReadRegistry
    {
        [ThreadStatic]
        private static List<FileIdentity>? _active;

        /// <summary>
        /// パスを読み取り中として登録する。戻り値を破棄すると登録が解除される。
        /// </summary>
        /// <param name="fullPath">正規化済みの絶対パス</param>
        public static IDisposable Register(string fullPath)
        {
            List<FileIdentity> active = _active ??= new List<FileIdentity>();
            var identity = FileIdentity.Of(fullPath);

            // ワイルドカードで同じファイルが複数回解決される場合に備えて、登録ごとに 1 件ずつ持つ
            active.Add(identity);

            return new Registration(identity);
        }

        /// <summary>
        /// 指定したパスのファイルが、同じスレッド上で読み取り中かどうか
        /// </summary>
        /// <param name="fullPath">正規化済みの絶対パス</param>
        public static bool IsBeingRead(string fullPath)
            => _active != null && _active.Count > 0 && IsBeingRead(FileIdentity.Of(fullPath));

        /// <summary>
        /// 指定した実体のファイルが、同じスレッド上で読み取り中かどうか
        /// </summary>
        public static bool IsBeingRead(FileIdentity identity)
        {
            List<FileIdentity>? active = _active;

            if (active == null)
            {
                return false;
            }

            foreach (FileIdentity reading in active)
            {
                if (reading.Matches(identity))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 登録を解除する
        /// </summary>
        private static void Release(FileIdentity identity) => _active?.Remove(identity);

        /// <summary>
        /// 読み取り中である期間を表すスコープ
        /// </summary>
        private sealed class Registration : IDisposable
        {
            private readonly FileIdentity _identity;
            private bool _released;

            public Registration(FileIdentity identity)
            {
                this._identity = identity;
            }

            public void Dispose()
            {
                if (this._released)
                {
                    return;
                }

                this._released = true;
                Release(this._identity);
            }
        }
    }
}
