using System;
using System.Collections.Generic;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 現在書き込み中のファイルを記録し、同じパイプラインで後から読み込み側がそのファイルを開くこと（逆向きの往復）を検出する（1.3.0 第六次 B。BUG-0028）。
    /// </summary>
    /// <remarks>
    /// <see cref="ActiveReadRegistry"/> と対になる。<see cref="ActiveReadRegistry"/> は「書き込み側が開くとき、そのファイルを読み取り中か」を調べ、
    /// こちらは「読み込み側が開くとき、そのファイルに書き込み中か」を調べる。
    /// <c>Get-ProbedContent a.txt, z.txt | Out-ProbedFile z.txt</c> では、a.txt の 1 行目で Out-ProbedFile が z.txt を開き、
    /// その後で Get-ProbedContent が z.txt を読む。書き方によっては書きかけの内容を読み（写してから直接書く方法・追記）、
    /// そうでなくても書いた結果から z.txt の元の内容が抜ける。
    /// <br/>
    /// 書き込み側（<see cref="ProbedFileWriter"/>）は、開いた時点で書き込み先の実体（<see cref="FileIdentity"/>）と「元に戻して止める」処理を登録し、
    /// 完了・元に戻す・閉じるで外す。読み込み側は、開く前に実体で比べ、当たれば読まずに、書き込み側を元に戻させてから SamePathRoundTrip を報告する。
    /// 書き込み側は、次の入力か完了の時点で SamePathRoundTrip を報告する（読み込み側の呼び出しの中では、書き込み側のコマンドはエラーを書けないため）。
    /// <br/>
    /// まだ無いファイル（1 MiB を超える新しいファイルは、完了まで一時ファイルに書く）は、親フォルダーの実体と名前（LocationKey）で比べる。
    /// 読み込み側でファイルが見つからなかった場合もこれで引き、当たれば「ファイルが無い」ではなく SamePathRoundTrip にする（利用者の決定）。
    /// <br/>
    /// 記録はスレッド単位で保持する（<see cref="ActiveReadRegistry"/> と同じ理由）。
    /// </remarks>
    internal static class ActiveWriteRegistry
    {
        [ThreadStatic]
        private static List<Entry>? _active;

        /// <summary>
        /// 書き込み先を登録する。戻り値を破棄すると登録が解除される。
        /// </summary>
        /// <param name="identity">書き込み先の実体</param>
        /// <param name="abort">書き込みを元に戻して止める処理（読み込み側が当たったときに、読み込み側のスレッドで呼ばれる）</param>
        public static IDisposable Register(FileIdentity identity, Action abort)
        {
            List<Entry> active = _active ??= new List<Entry>();
            var entry = new Entry(identity, abort);
            active.Add(entry);

            return new Registration(entry);
        }

        /// <summary>
        /// 指定した実体のファイルに書き込み中なら、その書き込みをすべて元に戻して止め、true を返す。
        /// </summary>
        /// <remarks>呼び出し側（読み込み側）は、このファイルのハンドルを閉じてから呼ぶ（新しいファイルを消して元に戻すため）。</remarks>
        public static bool AbortWritersOf(FileIdentity identity)
        {
            List<Entry>? active = _active;

            if (active == null || active.Count == 0)
            {
                return false;
            }

            var matched = new List<Entry>();

            foreach (Entry entry in active)
            {
                if (entry.Identity.Matches(identity))
                {
                    matched.Add(entry);
                }
            }

            foreach (Entry entry in matched)
            {
                entry.Abort();
            }

            return matched.Count > 0;
        }

        /// <summary>同じスレッド上で、何かに書き込み中かどうか（比べる必要があるかの判断に使う）</summary>
        public static bool HasActiveWriters => _active != null && _active.Count > 0;

        private static void Release(Entry entry) => _active?.Remove(entry);

        private sealed class Entry
        {
            private readonly Action _abort;

            public Entry(FileIdentity identity, Action abort)
            {
                this.Identity = identity;
                this._abort = abort;
            }

            public FileIdentity Identity { get; }

            public void Abort() => this._abort();
        }

        private sealed class Registration : IDisposable
        {
            private readonly Entry _entry;
            private bool _released;

            public Registration(Entry entry)
            {
                this._entry = entry;
            }

            public void Dispose()
            {
                if (this._released)
                {
                    return;
                }

                this._released = true;
                Release(this._entry);
            }
        }
    }
}
