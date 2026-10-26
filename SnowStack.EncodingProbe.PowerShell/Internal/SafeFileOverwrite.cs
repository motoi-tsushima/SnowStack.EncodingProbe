using System;
using System.Globalization;
using System.IO;
using System.Threading;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// リンク切れのシンボリックリンクに上書きしようとしたときの扱い
    /// </summary>
    internal enum DanglingLinkPolicy
    {
        /// <summary>書かずに <see cref="LinkTargetNotFoundException"/> を投げる（標準の Set-Content と同じく、書かない）</summary>
        Reject,

        /// <summary>リンクの先にファイルを作って書く（標準の Out-File / Export-Csv と同じ）。リンクはそのまま残る</summary>
        CreateTarget,
    }

    /// <summary>
    /// 上書きの書き方（<see cref="SafeFileOverwrite"/>）
    /// </summary>
    internal enum OverwriteMethod
    {
        /// <summary>実体と同じフォルダーの一時ファイルに書き、完了したときに置き換える（書き込み先が無ければ移動する）</summary>
        Replace,

        /// <summary>元のファイルを一時フォルダーに写してから、元のファイルに直接書く。失敗したら写しから書き戻す</summary>
        CopyThenDirect,

        /// <summary>
        /// 新しいファイルを直接作って書く。失敗したら消す（まだ無いファイルで、書く内容が境目以内のとき・リンク切れのリンクの先・一時ファイルを作れないフォルダー）
        /// </summary>
        DirectCreate,

        /// <summary>
        /// 書く内容と元のファイルがどちらも境目以内のとき: 完了したときに、元のファイルの中身をメモリに読み、元のファイルを切り詰めずに開いて書き、長さを合わせる。
        /// 失敗したらメモリの写しから書き戻す（1.3.0 第五次の仕上げ）
        /// </summary>
        MemoryThenDirect,
    }

    /// <summary>
    /// ファイルを上書きし、失敗したら元に戻す（1.3.0 第五次。後半でリンクと権限の扱いを、仕上げで小さなファイルの扱いを加えた）。
    /// </summary>
    /// <remarks>
    /// Set-ProbedContent・Out-ProbedFile・Export-ProbedCsv の上書き（<see cref="ProbedFileWriter"/>）と、Convert-ProbedContent が共用する。
    /// 書く内容（符号化した後のバイト列）は、まず <see cref="DirectWriteThreshold"/> までメモリにためる。書き方は次のように切り替える（実装記録 10.6 節・10.16 節）。
    /// <list type="bullet">
    /// <item>まだ無いファイル: 開いた時点で直接作る（完了するまで <see cref="TemporaryFileRegistry"/> に載せる）。
    /// 書き終えた時点で境目以内なら、そこへ書く（<see cref="OverwriteMethod.DirectCreate"/>）。境目を超えたら、その時点で一時ファイルに切り替え、作った空のファイルを消す</item>
    /// <item>境目以内のふつうのファイル・ハードリンク: 完了するまで開かない。完了したときに <see cref="OverwriteMethod.MemoryThenDirect"/> で書く。
    /// 書く内容が境目を超えたとき・完了したときに元のファイルが境目を超えていたときは、その時点で下の方法に切り替える</item>
    /// <item>境目を超えるふつうのファイル: <see cref="OverwriteMethod.Replace"/>。書き終えるまで元のファイルに触れない。
    /// シンボリックリンク・ジャンクションは最後までたどり、実体のフォルダーに一時ファイルを作って実体を置き換える（リンクはそのまま残る）。
    /// Linux / macOS では、元のファイルのアクセス権（UnixFileMode）を一時ファイルに写してから置き換える（所有者は写せない。既知の限界）</item>
    /// <item>境目を超えるハードリンク（Windows で、リンク数が 2 以上）と、フォルダーに一時ファイルを作る権限が無い場合: <see cref="OverwriteMethod.CopyThenDirect"/>。
    /// 置き換えるとほかの名前が古い内容のまま残る・書けないため。大きなファイルでは写す時間がかかる</item>
    /// <item>リンク切れのシンボリックリンク: <see cref="DanglingLinkPolicy"/> に従う（メモリにためずに書く）</item>
    /// </list>
    /// メモリにためる量は、プロセス全体で <see cref="MemoryBudget"/> までとする（Set-ProbedContent は完了まで複数のファイルを開いておくため）。
    /// 超えそうになったファイルは、その時点で境目を超えたものとして扱う。
    /// 一時ファイルの名前は、元の名前によらない固定長の <c>.~probed&lt;8 桁の 16 進&gt;.probedtmp</c>（26 文字。1.3.0 第六次 B。BUG-0027）、
    /// 写しは一時フォルダーの <c>SnowStack.EncodingProbe.&lt;8 桁&gt;.probedbak</c>。
    /// 一時ファイルのパスが長さの上限を超える場合（.NET Framework の 260 文字など）は、写してから直接書く方法に切り替える。
    /// 書いている間の一時ファイルは <see cref="TemporaryFileRegistry"/> に載せ、ワイルドカードの解決から除く。
    /// </remarks>
    internal sealed class SafeFileOverwrite
    {
        /// <summary>一時ファイルの名前の接頭辞</summary>
        public const string TemporaryPrefix = ".~probed";

        /// <summary>一時ファイルの名前の接尾辞</summary>
        public const string TemporarySuffix = ".probedtmp";

        /// <summary>写しの名前の接尾辞</summary>
        public const string BackupSuffix = ".probedbak";

        /// <summary>メモリに写してから直接書く境目（書く内容・元のファイルの大きさ）の既定値</summary>
        public const long DefaultDirectWriteThreshold = 1024 * 1024;

        /// <summary>メモリにためる量の、プロセス全体での上限の既定値</summary>
        public const long DefaultMemoryBudget = 64L * 1024 * 1024;

        /// <summary>メモリにためている量の合計（プロセス全体）</summary>
        private static long s_reservedTotal;

        /// <summary>書き込み先のパス（求めたときのパス。リンクならリンクのパス）</summary>
        private readonly string _path;

        private readonly DanglingLinkPolicy _policy;

        private readonly int _bufferSize;

        /// <summary>書き込む先のストリーム（一時ファイル・実体そのもの・作った新しいファイル）。<see cref="OverwriteMethod.MemoryThenDirect"/> では完了するまで null。</summary>
        private FileStream? _stream;

        /// <summary>一時ファイル（<see cref="OverwriteMethod.Replace"/> のとき）</summary>
        private string? _temporaryPath;

        /// <summary>元のファイルの写し（<see cref="OverwriteMethod.CopyThenDirect"/> のとき）</summary>
        private string? _backupPath;

        /// <summary>置き換える前のファイルの属性（<see cref="OverwriteMethod.Replace"/> で、書き込み先が存在した場合）</summary>
        private FileAttributes? _originalAttributes;

        /// <summary>メモリにためている書く内容。境目を超えて書き方を切り替えた後・書き終えた後は null。</summary>
        private MemoryStream? _pending;

        /// <summary><see cref="_pending"/> のために <see cref="s_reservedTotal"/> に数えた量</summary>
        private long _reserved;

        /// <summary>開いた時点で作った新しいファイルで、境目を超えたら一時ファイルに切り替えるなら true</summary>
        private bool _createdAtBegin;

        /// <summary><see cref="OverwriteMethod.MemoryThenDirect"/> で、完了したときに開いた実体</summary>
        private FileStream? _directStream;

        /// <summary><see cref="OverwriteMethod.MemoryThenDirect"/> で、書き始める前に読んだ元のファイルの中身</summary>
        private byte[]? _original;

        /// <summary><see cref="OverwriteMethod.MemoryThenDirect"/> で、完了したときに実体が無くなっていたので作った場合は true</summary>
        private bool _createdAtCommit;

        /// <summary>
        /// 書き込みの前の、既存の実体の属性。元に戻したときにこの属性に戻す（書き戻すとファイルシステムが Archive 属性を付けるため。1.3.0 第六次 B）。
        /// まだ無いファイル・求められなかった場合は null。
        /// </summary>
        private FileAttributes? _attributesBefore;

        private bool _finished;

        private SafeFileOverwrite(string path, DanglingLinkPolicy policy, int bufferSize, OverwriteMethod method, string targetPath)
        {
            this._path = path;
            this._policy = policy;
            this._bufferSize = bufferSize;
            this.Method = method;
            this.TargetPath = targetPath;
        }

        /// <summary>
        /// メモリに写してから直接書く境目（バイト数）。書く内容と元のファイルがどちらもこれ以内なら <see cref="OverwriteMethod.MemoryThenDirect"/>
        /// （まだ無いファイルなら <see cref="OverwriteMethod.DirectCreate"/>）で書く。
        /// </summary>
        /// <remarks>値は実装記録 10.16 節の測定で決めた。テストと測定のために変えられるようにしてある。</remarks>
        internal static long DirectWriteThreshold { get; set; } = DefaultDirectWriteThreshold;

        /// <summary>メモリにためる量の、プロセス全体での上限（バイト数）。テストのために変えられるようにしてある。</summary>
        internal static long MemoryBudget { get; set; } = DefaultMemoryBudget;

        /// <summary>今メモリにためている量の合計（プロセス全体。テスト用）</summary>
        internal static long ReservedTotal => Interlocked.Read(ref s_reservedTotal);

        /// <summary>
        /// テスト用: <see cref="OverwriteMethod.MemoryThenDirect"/> で書き込みに失敗し、メモリの写しから書き戻す直前に、実体のパスを渡して呼ぶ。
        /// </summary>
        /// <remarks>書き戻しに失敗する場合（実装記録 10.16 節）を確実に起こす方法が無いため、テストがここでファイルの範囲をロックする。</remarks>
        internal static Action<string>? BeforeRestoreForTests { get; set; }

        /// <summary>選んだ書き方（境目を超えて切り替えた場合は、切り替えた後の書き方）</summary>
        public OverwriteMethod Method { get; private set; }

        /// <summary>最終的に内容が置かれる実体のパス（リンクを最後までたどったもの）</summary>
        public string TargetPath { get; private set; }

        /// <summary>
        /// 元に戻すときに写しから書き戻せず、一時フォルダーに残した写しのパス。書き戻せた（または写しを使わなかった）場合は null。
        /// </summary>
        /// <remarks>
        /// このとき元のファイルは書きかけの状態で、無事な内容はこの写しにしか無い。報告するエラーのメッセージに、このパスを添える
        /// （<see cref="ValidationMessages.RestoreFromBackupFailed"/>。1.3.0 第五次の後半）。
        /// <see cref="OverwriteMethod.MemoryThenDirect"/> では、メモリの写しを一時フォルダーに書き出して残す（第五次の仕上げ）。
        /// </remarks>
        public string? KeptBackupPath { get; private set; }

        /// <summary>
        /// 上書きを始める。
        /// </summary>
        /// <param name="path">書き込み先の絶対パス</param>
        /// <param name="policy">リンク切れのシンボリックリンクの扱い</param>
        /// <param name="bufferSize">FileStream のバッファサイズ</param>
        /// <exception cref="LinkTargetNotFoundException">リンク切れで、<paramref name="policy"/> が <see cref="DanglingLinkPolicy.Reject"/> の場合</exception>
        /// <exception cref="IOException">開けない場合</exception>
        /// <exception cref="UnauthorizedAccessException">開けない場合</exception>
        public static SafeFileOverwrite Begin(string path, DanglingLinkPolicy policy, int bufferSize)
        {
            FileIdentity identity = FileIdentity.Of(path);

            if (!identity.IsDanglingLink)
            {
                if (!identity.Exists)
                {
                    return BeginNewFile(path, policy, bufferSize);
                }

                string target = identity.FinalPath ?? path;

                if (GetLength(target) <= DirectWriteThreshold)
                {
                    // 書き込み用に開く権限があるかは、境目を超える場合と同じく開く時点で確かめる（ファイルの ACL に従う。1.3.0 第六次 B）。
                    // 共有違反は失敗としないので、ほかのハンドルとの衝突は、今までどおり完了の時点で分かる。それ以外は完了するまで元のファイルに触れない
                    EnsureWritable(target);

                    var memory = new SafeFileOverwrite(path, policy, bufferSize, OverwriteMethod.MemoryThenDirect, target);
                    memory._pending = new MemoryStream();
                    memory._attributesBefore = FileAttributeKeeper.TryGet(target);

                    return memory;
                }
            }

            return BeginStreaming(path, policy, bufferSize);
        }

        /// <summary>
        /// バイト列を書く。境目までメモリにため、超えたらその時点で書き方を切り替えて、ためていた分から書き始める。
        /// </summary>
        /// <exception cref="IOException">書けない・切り替えた先を開けない場合</exception>
        /// <exception cref="UnauthorizedAccessException">切り替えた先を開けない場合</exception>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (this._pending != null)
            {
                if (this._pending.Length + count <= DirectWriteThreshold && TryReserve(count))
                {
                    this._pending.Write(buffer, offset, count);
                    return;
                }

                SwitchToStreaming();
            }

            this._stream!.Write(buffer, offset, count);
        }

        /// <summary>
        /// 書き込みを完了する。ストリームを閉じ、置き換え・写しの削除を行う。失敗した場合は元に戻して例外を投げる。
        /// </summary>
        /// <remarks>
        /// 呼び出し側は、書き残しを <see cref="Write"/> で書き終えてから呼ぶ。
        /// ほかのプロセスが書き込み先を開いていると、<see cref="OverwriteMethod.Replace"/>（共有の削除なし）と
        /// <see cref="OverwriteMethod.MemoryThenDirect"/>（共有の書き込みなし）はここで失敗する（開く時点ではない）。
        /// </remarks>
        public void Commit()
        {
            if (this._finished)
            {
                return;
            }

            try
            {
                if (this.Method == OverwriteMethod.MemoryThenDirect)
                {
                    // 元のファイルが境目を超えていたら、ここで切り替える（下で一時ファイルなどに書き出す）
                    WriteDirectFromMemory();
                }

                if (this.Method != OverwriteMethod.MemoryThenDirect)
                {
                    if (this._pending != null)
                    {
                        // 作った新しいファイルに、ためていた分を書く
                        this._pending.WriteTo(this._stream!);
                        ReleasePending();
                    }

                    this._stream!.Flush();
                    this._stream.Dispose();

                    switch (this.Method)
                    {
                        case OverwriteMethod.Replace:
                            Replace();
                            break;

                        case OverwriteMethod.CopyThenDirect:
                            DeleteQuietly(this._backupPath!);
                            break;

                        case OverwriteMethod.DirectCreate:
                            UnregisterCreatedFile();
                            break;
                    }
                }

                // 書き換えたファイルには Archive 属性を付ける（Windows のふつうの挙動。標準の Set-Content も付ける。1.3.0 第六次 B）。
                // 置き換えは元の属性を写すので、付けないと消えたままになり、バックアップのツールが変更を見落とす
                FileAttributeKeeper.MarkArchive(this.TargetPath);

                this._finished = true;
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        /// <summary>
        /// 書き込みの前の状態に戻す（ためていた分を捨てる・一時ファイルを消す・写しから書き戻す・作ったファイルを消す）。例外は投げない。
        /// </summary>
        public void Rollback()
        {
            if (this._finished)
            {
                return;
            }

            this._finished = true;
            ReleasePending();

            if (this._stream != null)
            {
                DisposeQuietly(this._stream);
            }

            switch (this.Method)
            {
                case OverwriteMethod.Replace:
                    DeleteQuietly(this._temporaryPath!);
                    TemporaryFileRegistry.Remove(this._temporaryPath!);
                    break;

                case OverwriteMethod.CopyThenDirect:
                    if (RestoreFromBackup())
                    {
                        DeleteQuietly(this._backupPath!);
                        FileAttributeKeeper.Restore(this.TargetPath, this._attributesBefore);
                    }
                    else
                    {
                        // 書き戻せなかった場合は、写しを一時フォルダーに残し、エラーのメッセージで知らせる
                        this.KeptBackupPath = this._backupPath;
                    }

                    break;

                case OverwriteMethod.DirectCreate:
                    DeleteQuietly(this.TargetPath);
                    UnregisterCreatedFile();
                    break;

                case OverwriteMethod.MemoryThenDirect:
                    RollbackDirect();
                    break;
            }
        }

        /// <summary>
        /// メモリにためずに書き始める（第五次の後半までの書き方。境目を超えるファイル・リンク切れ・切り替えた後）
        /// </summary>
        private static SafeFileOverwrite BeginStreaming(string path, DanglingLinkPolicy policy, int bufferSize)
        {
            FileIdentity identity = FileIdentity.Of(path);

            if (identity.IsDanglingLink)
            {
                if (policy == DanglingLinkPolicy.Reject)
                {
                    throw new LinkTargetNotFoundException(ValidationMessages.LinkTargetNotFound(path));
                }

                // リンクをたどって先にファイルを作る。リンク自体は置き換えない
                return BeginDirectCreate(path, policy, bufferSize, FileMode.Create);
            }

            if (!identity.Exists)
            {
                try
                {
                    return BeginReplace(path, path, policy, bufferSize, null);
                }
                catch (UnauthorizedAccessException)
                {
                    // フォルダーに一時ファイルを作れない（書き込み先の名前でなら作れる場合に備える）
                    return BeginDirectCreate(path, policy, bufferSize, FileMode.CreateNew);
                }
                catch (IOException exception) when (IsPathTooLong(exception, path))
                {
                    // 一時ファイルのパスが長さの上限を超える（書き込み先の名前でなら作れる。BUG-0027）
                    return BeginDirectCreate(path, policy, bufferSize, FileMode.CreateNew);
                }
            }

            string target = identity.FinalPath ?? path;

            if (identity.LinkCount >= 2)
            {
                return BeginCopyThenDirect(path, target, policy, bufferSize);
            }

            EnsureWritable(target);

            try
            {
                return BeginReplace(path, target, policy, bufferSize, File.GetAttributes(target));
            }
            catch (UnauthorizedAccessException)
            {
                return BeginCopyThenDirect(path, target, policy, bufferSize);
            }
            catch (IOException exception) when (IsPathTooLong(exception, target))
            {
                // 一時ファイルのパスが長さの上限を超える（元のファイルの名前が一時ファイルの名前より短く、パスが上限の近くにある場合。BUG-0027）
                return BeginCopyThenDirect(path, target, policy, bufferSize);
            }
        }

        /// <summary>
        /// 一時ファイルを作れなかった原因が、パスの長さの上限かどうか（1.3.0 第六次 B。BUG-0027）
        /// </summary>
        /// <remarks>
        /// .NET Framework は 260 文字を超えるパスを <see cref="PathTooLongException"/>、またはパスの一部が見つからない
        /// （<see cref="DirectoryNotFoundException"/>）として報告する。.NET は 1 つの名前が 255 文字を超えると
        /// ERROR_FILENAME_EXCED_RANGE・ERROR_INVALID_NAME になる。フォルダーが実在するのに見つからないと言われた場合も長さとみなす。
        /// </remarks>
        private static bool IsPathTooLong(IOException exception, string target)
        {
            if (exception is PathTooLongException)
            {
                return true;
            }

            const int FilenameExceedsRange = unchecked((int)0x800700CE);
            const int InvalidName = unchecked((int)0x8007007B);

            if (exception.HResult == FilenameExceedsRange || exception.HResult == InvalidName)
            {
                return true;
            }

            string? directory = System.IO.Path.GetDirectoryName(target);

            return exception is DirectoryNotFoundException && !string.IsNullOrEmpty(directory) && Directory.Exists(directory);
        }

        /// <summary>
        /// 置き換える前に、元のファイルを書き込み用に（切り詰めずに）開けるかを確かめ、すぐに閉じる（1.3.0 第五次の仕上げ。実装記録 10.16 節）
        /// </summary>
        /// <remarks>
        /// 置き換え（ReplaceFile）はフォルダーの権限で行われるので、ファイルに WriteData の拒否の ACL があっても書けてしまう。
        /// 直接書く方法（境目以内・写してから直接）はファイルの ACL に従って失敗するので、大きさで結果が変わらないようにそろえる
        /// （標準の Set-Content と同じ。1.2.0 仕様書 2.9「-Force はセキュリティ上の制限を越えない」）。
        /// 権限の拒否（<see cref="UnauthorizedAccessException"/>）だけを失敗とし、共有違反などの <see cref="IOException"/> は無視する
        /// （ほかのハンドルが開いているときに失敗する条件を、置き換えの条件より厳しくしないため。その場合は今までどおり完了の時点で分かる）。
        /// 確かめるハンドルは、ほかのハンドルを妨げないよう、すべての共有を許して開く。
        /// </remarks>
        /// <exception cref="UnauthorizedAccessException">書き込み用に開く権限が無い場合</exception>
        private static void EnsureWritable(string target)
        {
            try
            {
                using (new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None))
                {
                }
            }
            catch (IOException)
            {
                // 共有違反など。権限の問題ではないので、置き換えを試みる
            }
        }

        /// <summary>
        /// まだ無いファイル: 開いた時点で直接作り、書く内容はメモリにためる
        /// </summary>
        /// <remarks>
        /// 開く時点のエラー（フォルダーの権限など）を、第五次の後半までと同じ時点で報告するため、ここで作る（実装記録 10.16 節の判断 A）。
        /// 完了するまでは空のファイルとして見える。同じプロセスのワイルドカードの解決からは除く（第五次の後半までは、完了するまで存在しなかったため）。
        /// </remarks>
        private static SafeFileOverwrite BeginNewFile(string path, DanglingLinkPolicy policy, int bufferSize)
        {
            SafeFileOverwrite overwrite = BeginDirectCreate(path, policy, bufferSize, FileMode.CreateNew);
            overwrite._createdAtBegin = true;
            overwrite._pending = new MemoryStream();
            TemporaryFileRegistry.Add(path);

            return overwrite;
        }

        private static SafeFileOverwrite BeginReplace(
            string path, string target, DanglingLinkPolicy policy, int bufferSize, FileAttributes? originalAttributes)
        {
            string temporaryPath;
            FileStream stream;
            int attempt = 0;

            while (true)
            {
                temporaryPath = CreateTemporaryPath(target);

                try
                {
                    stream = new FileStream(
                        temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.SequentialScan);
                    break;
                }
                catch (IOException exception) when (++attempt < 5 && File.Exists(temporaryPath))
                {
                    // 名前が元の名前によらないので、同じフォルダーで別の書き込みの一時ファイルとぶつかることがある。名前を作り直す
                    _ = exception;
                }
            }

            TemporaryFileRegistry.Add(temporaryPath);

            return new SafeFileOverwrite(path, policy, bufferSize, OverwriteMethod.Replace, target)
            {
                _stream = stream,
                _temporaryPath = temporaryPath,
                _originalAttributes = originalAttributes,
            };
        }

        private static SafeFileOverwrite BeginCopyThenDirect(string path, string target, DanglingLinkPolicy policy, int bufferSize)
        {
            string backupPath = CreateBackupPath();
            FileAttributes? attributesBefore = FileAttributeKeeper.TryGet(target);

            File.Copy(target, backupPath);

            try
            {
                // 写しを消せるように、属性（読み取り専用・隠し）は写さない
                File.SetAttributes(backupPath, FileAttributes.Normal);

                var stream = new FileStream(
                    target, FileMode.Open, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.SequentialScan);

                try
                {
                    stream.SetLength(0);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }

                return new SafeFileOverwrite(path, policy, bufferSize, OverwriteMethod.CopyThenDirect, target)
                {
                    _stream = stream,
                    _backupPath = backupPath,
                    _attributesBefore = attributesBefore,
                };
            }
            catch
            {
                DeleteQuietly(backupPath);
                throw;
            }
        }

        private static SafeFileOverwrite BeginDirectCreate(string path, DanglingLinkPolicy policy, int bufferSize, FileMode mode)
        {
            var stream = new FileStream(path, mode, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.SequentialScan);

            // 元に戻すときに消すのは、リンクではなく作った実体
            string target = FileIdentity.GetFinalPath(stream.SafeFileHandle) ?? FileIdentity.Of(path).FinalPath ?? path;

            return new SafeFileOverwrite(path, policy, bufferSize, OverwriteMethod.DirectCreate, target)
            {
                _stream = stream,
            };
        }

        /// <summary>
        /// 境目を超えたので、メモリにためずに書く方法へ切り替え、ためていた分を書く
        /// </summary>
        /// <remarks>
        /// 切り替えに失敗したときは、ファイルに触れていない状態（作った新しいファイルは消した状態）で例外を投げる。呼び出し側は <see cref="Rollback"/> を呼ぶ。
        /// </remarks>
        private void SwitchToStreaming()
        {
            MemoryStream pending = this._pending!;

            if (this._createdAtBegin)
            {
                // 作った空のファイルを消してから、まだ無いファイルとして始め直す（一時ファイルに書き、完了したときに移動する）
                DisposeQuietly(this._stream!);
                this._stream = null;
                DeleteQuietly(this.TargetPath);
                UnregisterCreatedFile();
            }

            SafeFileOverwrite streaming = BeginStreaming(this._path, this._policy, this._bufferSize);

            this.Method = streaming.Method;
            this.TargetPath = streaming.TargetPath;
            this._stream = streaming._stream;
            this._temporaryPath = streaming._temporaryPath;
            this._backupPath = streaming._backupPath;
            this._originalAttributes = streaming._originalAttributes;
            this._attributesBefore = streaming._attributesBefore;

            pending.WriteTo(this._stream!);
            ReleasePending();
        }

        /// <summary>
        /// <see cref="OverwriteMethod.MemoryThenDirect"/> の完了: 元のファイルをメモリに読み、切り詰めずに開いたまま書いて長さを合わせる
        /// </summary>
        private void WriteDirectFromMemory()
        {
            bool existed = File.Exists(this.TargetPath);

            // バッファを持たない（失敗したときに、書き出していない分が後から書かれないようにする）
            this._directStream = new FileStream(this.TargetPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.None);
            this._createdAtCommit = !existed;

            if (this._directStream.Length > DirectWriteThreshold)
            {
                // 開いた後に元のファイルが大きくなっていた。何も書かずに閉じて、今までの書き方にする
                this._directStream.Dispose();
                this._directStream = null;
                SwitchToStreaming();

                return;
            }

            this._original = ReadAll(this._directStream);
            this._directStream.Position = 0;
            this._pending!.WriteTo(this._directStream);
            this._directStream.SetLength(this._pending.Length);
            this._directStream.Flush();
            this._directStream.Dispose();
            this._directStream = null;
            this._original = null;
            ReleasePending();
        }

        /// <summary>
        /// <see cref="OverwriteMethod.MemoryThenDirect"/> を元に戻す
        /// </summary>
        private void RollbackDirect()
        {
            if (this._createdAtCommit)
            {
                if (this._directStream != null)
                {
                    DisposeQuietly(this._directStream);
                    DeleteQuietly(this.TargetPath);
                }

                return;
            }

            if (this._original == null)
            {
                // 書き始める前（ためていた分を捨てるだけ）
                if (this._directStream != null)
                {
                    DisposeQuietly(this._directStream);
                }

                return;
            }

            BeforeRestoreForTests?.Invoke(this.TargetPath);

            bool restored = TryRestore(this._directStream!);
            DisposeQuietly(this._directStream!);

            if (restored)
            {
                FileAttributeKeeper.Restore(this.TargetPath, this._attributesBefore);
            }

            if (!restored)
            {
                // 開いたハンドルで書き戻せなかった（閉じてしまっていた場合など）。開き直して書き戻す
                try
                {
                    using (var stream = new FileStream(this.TargetPath, FileMode.Open, FileAccess.Write, FileShare.Read, 1, FileOptions.None))
                    {
                        restored = TryRestore(stream);
                    }

                    if (restored)
                    {
                        FileAttributeKeeper.Restore(this.TargetPath, this._attributesBefore);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (!restored)
            {
                // 書き戻せなかった場合は、メモリの写しを一時フォルダーに書き出して残し、エラーのメッセージで知らせる
                this.KeptBackupPath = SaveBackup(this._original);
            }

            this._original = null;
        }

        private bool TryRestore(FileStream stream)
        {
            try
            {
                stream.Position = 0;
                stream.Write(this._original!, 0, this._original!.Length);
                stream.SetLength(this._original.Length);
                stream.Flush();

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>
        /// 一時ファイルで実体を置き換える（実体が無ければ移動する）
        /// </summary>
        private void Replace()
        {
            if (File.Exists(this.TargetPath))
            {
                CopyUnixFileMode(this.TargetPath, this._temporaryPath!);
                File.Replace(this._temporaryPath!, this.TargetPath, null);

                if (this._originalAttributes.HasValue)
                {
                    // 読み取り専用属性は ReadOnlyAttributeScope が戻す
                    File.SetAttributes(this.TargetPath, this._originalAttributes.Value & ~FileAttributes.ReadOnly);
                }
            }
            else
            {
                File.Move(this._temporaryPath!, this._path);
            }

            TemporaryFileRegistry.Remove(this._temporaryPath!);
        }

        /// <summary>写しから実体へ書き戻す（実体のファイルそのものに書くので、ハードリンクも保たれる）</summary>
        private bool RestoreFromBackup()
        {
            try
            {
                using (var source = new FileStream(this._backupPath!, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destination = new FileStream(this.TargetPath, FileMode.Open, FileAccess.Write, FileShare.Read))
                {
                    destination.SetLength(0);
                    source.CopyTo(destination);
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void UnregisterCreatedFile()
        {
            if (this._createdAtBegin)
            {
                TemporaryFileRegistry.Remove(this._path);
                this._createdAtBegin = false;
            }
        }

        private bool TryReserve(long count)
        {
            if (Interlocked.Add(ref s_reservedTotal, count) > MemoryBudget)
            {
                Interlocked.Add(ref s_reservedTotal, -count);
                return false;
            }

            this._reserved += count;
            return true;
        }

        private void ReleasePending()
        {
            this._pending = null;

            if (this._reserved != 0)
            {
                Interlocked.Add(ref s_reservedTotal, -this._reserved);
                this._reserved = 0;
            }
        }

        /// <summary>ファイルの大きさ。求められなければ（フォルダーなど）long.MaxValue（今までの書き方にする）</summary>
        private static long GetLength(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (IOException)
            {
                return long.MaxValue;
            }
            catch (UnauthorizedAccessException)
            {
                return long.MaxValue;
            }
        }

        private static byte[] ReadAll(FileStream stream)
        {
            var content = new byte[stream.Length];
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

        /// <summary>メモリの写しを一時フォルダーに書き出す。書き出せなければ null</summary>
        private static string? SaveBackup(byte[] content)
        {
            string backupPath = CreateBackupPath();

            try
            {
                File.WriteAllBytes(backupPath, content);
                return backupPath;
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

        /// <summary>Linux / macOS で、元のファイルのアクセス権を一時ファイルに写す</summary>
        private static void CopyUnixFileMode(string source, string destination)
        {
#if !NETFRAMEWORK
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
            }
#endif
        }

        /// <summary>
        /// 実体と同じフォルダーに、一時ファイルのパスを作る（同じボリュームで置き換えるため）
        /// </summary>
        private static string CreateTemporaryPath(string target)
        {
            string directory = System.IO.Path.GetDirectoryName(target) ?? string.Empty;

            // 元の名前を含めない（名前の長い書き込み先で、一時ファイルの名前が 255 文字・パスが 260 文字を超えていた。BUG-0027）
            string name = TemporaryPrefix + NewSuffix() + TemporarySuffix;

            return System.IO.Path.Combine(directory, name);
        }

        private static string CreateBackupPath()
            => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SnowStack.EncodingProbe." + NewSuffix() + BackupSuffix);

        private static string NewSuffix()
            => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).Substring(0, 8);

        private static void DisposeQuietly(FileStream stream)
        {
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
                // 書き残しを書き出せなかった。元に戻すので問題にならない
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Set-ProbedContent で、書き込み先がリンク切れのシンボリックリンクだったことを表す例外（1.3.0 第五次の後半）
    /// </summary>
    internal sealed class LinkTargetNotFoundException : IOException
    {
        /// <summary>ErrorRecord に設定するエラー ID</summary>
        public const string ErrorId = "LinkTargetNotFound";

        public LinkTargetNotFoundException(string message)
            : base(message)
        {
        }
    }
}
