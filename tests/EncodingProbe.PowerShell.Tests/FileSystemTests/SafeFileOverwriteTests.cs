using System;
using System.IO;
using System.Linq;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.FileSystemTests;

/// <summary>
/// 上書きの部品（<see cref="SafeFileOverwrite"/>）の、書き方の切り替えのテスト（1.3.0 第五次の仕上げ。実装記録 10.16 節）。
/// </summary>
/// <remarks>
/// 書く内容・元のファイルの大きさが境目（<see cref="SafeFileOverwrite.DirectWriteThreshold"/>）ちょうどと、1 バイト超えた場合に、
/// どの書き方（<see cref="SafeFileOverwrite.Method"/>）になるかを確かめる。メモリにためる量の上限と、メモリの写しから書き戻せなかった場合も確かめる。
/// </remarks>
[Collection(OverwriteStateCollection.Name)]
public class SafeFileOverwriteTests : IDisposable
{
    private static readonly byte[] Old = { 0x6F, 0x6C, 0x64, 0x0D, 0x0A };

    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public void Dispose()
    {
        SafeFileOverwrite.MemoryBudget = SafeFileOverwrite.DefaultMemoryBudget;
        SafeFileOverwrite.BeforeRestoreForTests = null;
        this._directory.Dispose();
    }

    private static int Threshold => checked((int)SafeFileOverwrite.DirectWriteThreshold);

    /// <summary>書く内容が境目ちょうどならメモリに写してから直接書き、1 バイト超えたら置き換える</summary>
    [Theory]
    [InlineData(0, nameof(OverwriteMethod.MemoryThenDirect))]
    [InlineData(1, nameof(OverwriteMethod.Replace))]
    public void ExistingFile_ContentSize_SelectsMethod(int extra, string expected)
    {
        string path = this._directory.Write("a.txt", Old);
        byte[] content = Filled(Threshold + extra, (byte)'n');

        Assert.Equal(expected, Name(WriteAll(path, content)));
        Assert.Equal(content, File.ReadAllBytes(path));
        AssertNoTemporaryFile();
    }

    /// <summary>元のファイルが境目ちょうどならメモリに写してから直接書き、1 バイト超えたら置き換える（書く内容は小さい）</summary>
    [Theory]
    [InlineData(0, nameof(OverwriteMethod.MemoryThenDirect))]
    [InlineData(1, nameof(OverwriteMethod.Replace))]
    public void ExistingFile_OriginalSize_SelectsMethod(int extra, string expected)
    {
        string path = this._directory.Write("a.txt", Filled(Threshold + extra, (byte)'o'));

        Assert.Equal(expected, Name(WriteAll(path, Old)));
        Assert.Equal(Old, File.ReadAllBytes(path));
        AssertNoTemporaryFile();
    }

    /// <summary>まだ無いファイルは、境目ちょうどまでなら直接作って書き、1 バイト超えたら一時ファイルから移す</summary>
    [Theory]
    [InlineData(0, nameof(OverwriteMethod.DirectCreate))]
    [InlineData(1, nameof(OverwriteMethod.Replace))]
    public void NewFile_ContentSize_SelectsMethod(int extra, string expected)
    {
        string path = this._directory.Combine("new.txt");
        byte[] content = Filled(Threshold + extra, (byte)'n');

        Assert.Equal(expected, Name(WriteAll(path, content)));
        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.False(TemporaryFileRegistry.Contains(path));
        AssertNoTemporaryFile();
    }

    /// <summary>少しずつ書いて境目を超えた場合も、ためていた分から書き始める</summary>
    [Fact]
    public void CrossingThresholdInChunks_KeepsAllContent()
    {
        string path = this._directory.Write("a.txt", Old);
        byte[] content = Enumerable.Range(0, Threshold + 2).Select(i => (byte)('a' + (i % 26))).ToArray();
        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);

        overwrite.Write(content, 0, Threshold - 1);
        Assert.Equal(OverwriteMethod.MemoryThenDirect, overwrite.Method);
        overwrite.Write(content, Threshold - 1, 3);
        Assert.Equal(OverwriteMethod.Replace, overwrite.Method);
        overwrite.Commit();

        Assert.Equal(content, File.ReadAllBytes(path));
    }

    /// <summary>ハードリンクは、境目以内ならメモリに写してから直接書き、超えたら写してから直接書く（どちらもリンクを切らない）</summary>
    [Theory]
    [InlineData(0, nameof(OverwriteMethod.MemoryThenDirect))]
    [InlineData(1, nameof(OverwriteMethod.CopyThenDirect))]
    public void HardLink_SelectsMethod_AndKeepsLink(int extra, string expected)
    {
        string path = this._directory.Write("a.txt", Filled(Threshold + extra, (byte)'o'));
        string hard = this._directory.Combine("hard.txt");
        Assert.True(FileLinks.TryCreateHardLink(hard, path));

        Assert.Equal(expected, Name(WriteAll(hard, Old)));
        Assert.Equal(Old, File.ReadAllBytes(path));
        Assert.Equal(Old, File.ReadAllBytes(hard));
    }

    /// <summary>
    /// メモリにためる量はプロセス全体で上限まで。超えそうになったファイルは今までの書き方に切り替わり、書き終えたファイルの分は戻る
    /// </summary>
    [Fact]
    public void MemoryBudget_IsSharedAcrossWriters()
    {
        // ほかのテストが残した分があっても、ここから 10 バイトだけためられるようにする
        SafeFileOverwrite.MemoryBudget = SafeFileOverwrite.ReservedTotal + 10;
        string a = this._directory.Write("a.txt", Old);
        string b = this._directory.Write("b.txt", Old);
        byte[] six = Filled(6, (byte)'n');

        SafeFileOverwrite first = SafeFileOverwrite.Begin(a, DanglingLinkPolicy.Reject, 4096);
        SafeFileOverwrite second = SafeFileOverwrite.Begin(b, DanglingLinkPolicy.Reject, 4096);
        first.Write(six, 0, six.Length);
        second.Write(six, 0, six.Length);

        Assert.Equal(OverwriteMethod.MemoryThenDirect, first.Method);
        Assert.Equal(OverwriteMethod.Replace, second.Method);

        first.Commit();
        second.Commit();
        Assert.Equal(six, File.ReadAllBytes(a));
        Assert.Equal(six, File.ReadAllBytes(b));

        // 書き終えた分は上限から戻る
        Assert.Equal(OverwriteMethod.MemoryThenDirect, WriteAll(a, six));
    }

    /// <summary>まだ無いファイルは開いた時点で作り、完了するまでワイルドカードの解決から除く。元に戻したら消す</summary>
    [Fact]
    public void NewFile_IsCreatedAtBegin_HiddenUntilCommit_RemovedOnRollback()
    {
        string path = this._directory.Combine("new.txt");
        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);

        Assert.True(File.Exists(path));
        Assert.True(TemporaryFileRegistry.Contains(path));

        overwrite.Write(Old, 0, Old.Length);
        overwrite.Rollback();

        Assert.False(File.Exists(path));
        Assert.False(TemporaryFileRegistry.Contains(path));
    }

    /// <summary>完了する前に元に戻すと、既存のファイルには触れていない（同じ実体のまま、同じ内容）</summary>
    [Fact]
    public void ExistingFile_RollbackBeforeCommit_DoesNotTouchFile()
    {
        string path = this._directory.Write("a.txt", Old);
        string? before = FileIdentity.Of(path).EntityKey;
        DateTime written = File.GetLastWriteTimeUtc(path);
        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);

        overwrite.Write(Filled(100, (byte)'n'), 0, 100);
        overwrite.Rollback();

        Assert.Equal(Old, File.ReadAllBytes(path));
        Assert.Equal(before, FileIdentity.Of(path).EntityKey);
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>
    /// 直接書いている途中で IOException（ほかのハンドルが元のファイルの末尾より先の範囲をロックしている）になったら、
    /// メモリの写しから書き戻し、元の長さに戻す（バイト単位で元どおり）
    /// </summary>
    [Fact]
    public void WriteFailure_IsRestoredFromMemory()
    {
        byte[] original = Enumerable.Range(0, 10).Select(i => (byte)('0' + i)).ToArray();
        string path = this._directory.Write("a.txt", original);
        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);
        overwrite.Write(Filled(200, (byte)'n'), 0, 200);

        using (var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            locker.Lock(100, 10);
            Assert.ThrowsAny<IOException>(() => overwrite.Commit());
            locker.Unlock(100, 10);
        }

        Assert.Equal(OverwriteMethod.MemoryThenDirect, overwrite.Method);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Null(overwrite.KeptBackupPath);
    }

    /// <summary>
    /// メモリの写しから書き戻せなかった場合は、写しを一時フォルダーに書き出して残し、そのパスを <see cref="SafeFileOverwrite.KeptBackupPath"/> に入れる
    /// </summary>
    [Fact]
    public void RestoreFailure_KeepsMemoryCopyInTemporaryFolder()
    {
        byte[] original = Enumerable.Range(0, 10).Select(i => (byte)('0' + i)).ToArray();
        string path = this._directory.Write("a.txt", original);
        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);
        overwrite.Write(Filled(200, (byte)'n'), 0, 200);

        using (var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            locker.Lock(100, 10);

            // 書き戻す直前に、書き戻す範囲もロックする
            SafeFileOverwrite.BeforeRestoreForTests = _ => locker.Lock(0, 100);

            Assert.ThrowsAny<IOException>(() => overwrite.Commit());
            locker.Unlock(0, 100);
            locker.Unlock(100, 10);
        }

        string kept = Assert.IsType<string>(overwrite.KeptBackupPath);

        try
        {
            Assert.StartsWith(Path.Combine(Path.GetTempPath(), "SnowStack.EncodingProbe."), kept);
            Assert.EndsWith(SafeFileOverwrite.BackupSuffix, kept);
            Assert.Equal(original, File.ReadAllBytes(kept));
        }
        finally
        {
            File.Delete(kept);
        }
    }

    private static OverwriteMethod WriteAll(string path, byte[] content)
    {
        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);
        overwrite.Write(content, 0, content.Length);
        overwrite.Commit();

        return overwrite.Method;
    }

    private static string Name(OverwriteMethod method) => method.ToString();

    private static byte[] Filled(int length, byte value) => Enumerable.Repeat(value, length).ToArray();

    private void AssertNoTemporaryFile()
        => Assert.Empty(Directory.GetFiles(this._directory.Path, "*" + SafeFileOverwrite.TemporarySuffix, SearchOption.AllDirectories));
}
