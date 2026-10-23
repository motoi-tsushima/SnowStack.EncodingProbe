using System;
using System.Globalization;
using System.IO;
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

        /// <summary>新しいファイルを直接作って書く。失敗したら消す（リンク切れのリンクの先・一時ファイルを作れないフォルダー）</summary>
        DirectCreate,
    }

    /// <summary>
    /// ファイルを上書きし、失敗したら元に戻す（1.3.0 第五次。後半でリンクと権限の扱いを加えた）。
    /// </summary>
    /// <remarks>
    /// Set-ProbedContent・Out-ProbedFile・Export-ProbedCsv の上書き（<see cref="ProbedFileWriter"/>）と、Convert-ProbedContent が共用する。
    /// 書き方は次のように切り替える（実装記録 10 章）。
    /// <list type="bullet">
    /// <item>ふつうのファイル・まだ無いファイル: <see cref="OverwriteMethod.Replace"/>。書き終えるまで元のファイルに触れない。
    /// シンボリックリンク・ジャンクションは最後までたどり、実体のフォルダーに一時ファイルを作って実体を置き換える（リンクはそのまま残る）。
    /// Linux / macOS では、元のファイルのアクセス権（UnixFileMode）を一時ファイルに写してから置き換える（所有者は写せない。既知の限界）</item>
    /// <item>ハードリンク（Windows で、リンク数が 2 以上）と、フォルダーに一時ファイルを作る権限が無い場合: <see cref="OverwriteMethod.CopyThenDirect"/>。
    /// 置き換えるとほかの名前が古い内容のまま残る・書けないため。大きなファイルでは写す時間がかかる</item>
    /// <item>リンク切れのシンボリックリンク: <see cref="DanglingLinkPolicy"/> に従う</item>
    /// </list>
    /// 一時ファイルの名前は <c>.&lt;名前&gt;.&lt;8 桁の 16 進&gt;.probedtmp</c>、写しは一時フォルダーの <c>SnowStack.EncodingProbe.&lt;8 桁&gt;.probedbak</c>。
    /// 書いている間の一時ファイルは <see cref="TemporaryFileRegistry"/> に載せ、ワイルドカードの解決から除く。
    /// </remarks>
    internal sealed class SafeFileOverwrite
    {
        /// <summary>一時ファイルの名前の接尾辞</summary>
        public const string TemporarySuffix = ".probedtmp";

        /// <summary>写しの名前の接尾辞</summary>
        public const string BackupSuffix = ".probedbak";

        /// <summary>書き込み先のパス（求めたときのパス。リンクならリンクのパス）</summary>
        private readonly string _path;

        /// <summary>一時ファイル（<see cref="OverwriteMethod.Replace"/> のとき）</summary>
        private readonly string? _temporaryPath;

        /// <summary>元のファイルの写し（<see cref="OverwriteMethod.CopyThenDirect"/> のとき）</summary>
        private readonly string? _backupPath;

        /// <summary>置き換える前のファイルの属性（<see cref="OverwriteMethod.Replace"/> で、書き込み先が存在した場合）</summary>
        private readonly FileAttributes? _originalAttributes;

        private bool _finished;

        private SafeFileOverwrite(
            FileStream stream, OverwriteMethod method, string path, string targetPath,
            string? temporaryPath, string? backupPath, FileAttributes? originalAttributes)
        {
            this.Stream = stream;
            this.Method = method;
            this._path = path;
            this.TargetPath = targetPath;
            this._temporaryPath = temporaryPath;
            this._backupPath = backupPath;
            this._originalAttributes = originalAttributes;
        }

        /// <summary>書き込む先のストリーム（一時ファイル、または実体そのもの）</summary>
        public FileStream Stream { get; }

        /// <summary>選んだ書き方</summary>
        public OverwriteMethod Method { get; }

        /// <summary>最終的に内容が置かれる実体のパス（リンクを最後までたどったもの）</summary>
        public string TargetPath { get; }

        /// <summary>
        /// 元に戻すときに写しから書き戻せず、一時フォルダーに残した写しのパス。書き戻せた（または写しを使わなかった）場合は null。
        /// </summary>
        /// <remarks>
        /// このとき元のファイルは書きかけの状態で、無事な内容はこの写しにしか無い。報告するエラーのメッセージに、このパスを添える
        /// （<see cref="ValidationMessages.RestoreFromBackupFailed"/>。1.3.0 第五次の後半）。
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

            if (identity.IsDanglingLink)
            {
                if (policy == DanglingLinkPolicy.Reject)
                {
                    throw new LinkTargetNotFoundException(ValidationMessages.LinkTargetNotFound(path));
                }

                // リンクをたどって先にファイルを作る。リンク自体は置き換えない
                return BeginDirectCreate(path, FileMode.Create, bufferSize);
            }

            if (!identity.Exists)
            {
                try
                {
                    return BeginReplace(path, path, null, bufferSize);
                }
                catch (UnauthorizedAccessException)
                {
                    // フォルダーに一時ファイルを作れない（書き込み先の名前でなら作れる場合に備える）
                    return BeginDirectCreate(path, FileMode.CreateNew, bufferSize);
                }
            }

            string target = identity.FinalPath ?? path;

            if (identity.LinkCount >= 2)
            {
                return BeginCopyThenDirect(path, target, bufferSize);
            }

            try
            {
                return BeginReplace(path, target, File.GetAttributes(target), bufferSize);
            }
            catch (UnauthorizedAccessException)
            {
                return BeginCopyThenDirect(path, target, bufferSize);
            }
        }

        /// <summary>
        /// 書き込みを完了する。ストリームを閉じ、置き換え・写しの削除を行う。失敗した場合は元に戻して例外を投げる。
        /// </summary>
        /// <remarks>
        /// 呼び出し側は、書き残しをストリームに書き終えてから呼ぶ。
        /// ほかのプロセスが書き込み先を共有の削除なしで開いていると、<see cref="OverwriteMethod.Replace"/> はここで失敗する（開く時点ではない）。
        /// </remarks>
        public void Commit()
        {
            if (this._finished)
            {
                return;
            }

            try
            {
                this.Stream.Flush();
                this.Stream.Dispose();

                switch (this.Method)
                {
                    case OverwriteMethod.Replace:
                        Replace();
                        break;

                    case OverwriteMethod.CopyThenDirect:
                        DeleteQuietly(this._backupPath!);
                        break;
                }

                this._finished = true;
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        /// <summary>
        /// 書き込みの前の状態に戻す（一時ファイルを消す・写しから書き戻す・作ったファイルを消す）。例外は投げない。
        /// </summary>
        public void Rollback()
        {
            if (this._finished)
            {
                return;
            }

            this._finished = true;
            DisposeQuietly(this.Stream);

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
                    }
                    else
                    {
                        // 書き戻せなかった場合は、写しを一時フォルダーに残し、エラーのメッセージで知らせる
                        this.KeptBackupPath = this._backupPath;
                    }

                    break;

                case OverwriteMethod.DirectCreate:
                    DeleteQuietly(this.TargetPath);
                    break;
            }
        }

        private static SafeFileOverwrite BeginReplace(string path, string target, FileAttributes? originalAttributes, int bufferSize)
        {
            string temporaryPath = CreateTemporaryPath(target);
            var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            TemporaryFileRegistry.Add(temporaryPath);

            return new SafeFileOverwrite(stream, OverwriteMethod.Replace, path, target, temporaryPath, null, originalAttributes);
        }

        private static SafeFileOverwrite BeginCopyThenDirect(string path, string target, int bufferSize)
        {
            string backupPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SnowStack.EncodingProbe." + NewSuffix() + BackupSuffix);

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

                return new SafeFileOverwrite(stream, OverwriteMethod.CopyThenDirect, path, target, null, backupPath, null);
            }
            catch
            {
                DeleteQuietly(backupPath);
                throw;
            }
        }

        private static SafeFileOverwrite BeginDirectCreate(string path, FileMode mode, int bufferSize)
        {
            var stream = new FileStream(path, mode, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.SequentialScan);

            // 元に戻すときに消すのは、リンクではなく作った実体
            string target = FileIdentity.GetFinalPath(stream.SafeFileHandle) ?? FileIdentity.Of(path).FinalPath ?? path;

            return new SafeFileOverwrite(stream, OverwriteMethod.DirectCreate, path, target, null, null, null);
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
            string name = "." + System.IO.Path.GetFileName(target) + "." + NewSuffix() + TemporarySuffix;

            return System.IO.Path.Combine(directory, name);
        }

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
