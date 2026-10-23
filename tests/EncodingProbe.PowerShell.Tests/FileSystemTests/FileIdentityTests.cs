using System;
using System.IO;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.FileSystemTests;

/// <summary>
/// ファイルの実体の判定（<see cref="FileIdentity"/>）とパスの正規化（<see cref="FileSystemPath"/>）のテスト（1.3.0 第五次の後半。BUG-0004・0016・0017）。
/// </summary>
/// <remarks>
/// BUG-0004 の書き方（\\?\、UNC の管理共有、\\.\、シンボリックリンク・ジャンクション・ハードリンク、大文字小文字、8.3 名）を、すべて同じ実体と判定することを確かめる。
/// subst のドライブはテストの中で作らない（ログオンのセッション全体に見えるため）。テスト担当のスクリプト（BUG-0004）で確かめる。
/// </remarks>
public class FileIdentityTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public void Dispose() => this._directory.Dispose();

    [Fact]
    public void SamePath_DifferentCase_IsSameFile()
    {
        string path = this._directory.Write("a.txt", new byte[] { 0x41 });

        Assert.True(FileIdentity.Of(path).Matches(FileIdentity.Of(path.ToUpperInvariant())));
    }

    [Fact]
    public void ExtendedAndDevicePaths_AreSameFile()
    {
        string path = this._directory.Write("a.txt", new byte[] { 0x41 });
        var identity = FileIdentity.Of(path);

        Assert.True(identity.Exists);
        Assert.True(identity.Matches(FileIdentity.Of(@"\\?\" + path)));
        Assert.True(identity.Matches(FileIdentity.Of(@"\\.\" + path)));
    }

    [Fact]
    public void AdminShare_IsSameFile()
    {
        string path = this._directory.Write("a.txt", new byte[] { 0x41 });
        string? unc = FileLinks.ToAdminShare(path);

        if (unc == null)
        {
            return;  // 管理共有を使えない環境
        }

        Assert.True(FileIdentity.Of(path).Matches(FileIdentity.Of(unc)));
        Assert.True(FileIdentity.Of(path).Matches(FileIdentity.Of(@"\\?\UNC\" + unc.Substring(2))));
    }

    [Fact]
    public void SymbolicLink_RelativeAndChained_IsSameFileAndResolvesToTarget()
    {
        string target = this._directory.Write(@"sub\t.txt", new byte[] { 0x41 });
        string link2 = this._directory.Combine("link2.txt");
        string link1 = this._directory.Combine("link1.txt");

        if (!FileLinks.TryCreateFileSymbolicLink(link2, @"sub\t.txt") || !FileLinks.TryCreateFileSymbolicLink(link1, "link2.txt"))
        {
            return;  // シンボリックリンクを作れない環境
        }

        var identity = FileIdentity.Of(link1);

        Assert.True(identity.Matches(FileIdentity.Of(target)));
        Assert.Equal(target, identity.FinalPath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(target, FileIdentity.ResolveFinalPath(link1), StringComparer.OrdinalIgnoreCase);
        Assert.False(identity.IsDanglingLink);
    }

    [Fact]
    public void DanglingLink_IsDetected()
    {
        string link = this._directory.Combine("dangling.txt");
        Directory.CreateDirectory(this._directory.Combine("sub"));

        if (!FileLinks.TryCreateFileSymbolicLink(link, @"sub\missing.txt"))
        {
            return;
        }

        var identity = FileIdentity.Of(link);

        Assert.False(identity.Exists);
        Assert.True(identity.IsDanglingLink);
    }

    [Fact]
    public void JunctionAndDirectoryLink_AreSameFile()
    {
        string target = this._directory.Write(@"sub\t.txt", new byte[] { 0x41 });
        string junction = this._directory.Combine("junction");
        string directoryLink = this._directory.Combine("dirlink");

        Assert.True(FileLinks.TryCreateJunction(junction, this._directory.Combine("sub")));
        Assert.True(FileIdentity.Of(target).Matches(FileIdentity.Of(Path.Combine(junction, "t.txt"))));

        if (FileLinks.TryCreateDirectorySymbolicLink(directoryLink, this._directory.Combine("sub")))
        {
            Assert.True(FileIdentity.Of(target).Matches(FileIdentity.Of(Path.Combine(directoryLink, "t.txt"))));
        }
    }

    [Fact]
    public void HardLink_IsSameFileAndCountsLinks()
    {
        string target = this._directory.Write("t.txt", new byte[] { 0x41 });
        string hard = this._directory.Combine("hard.txt");

        Assert.Equal(1, FileIdentity.Of(target).LinkCount);
        Assert.True(FileLinks.TryCreateHardLink(hard, target));

        Assert.True(FileIdentity.Of(target).Matches(FileIdentity.Of(hard)));
        Assert.Equal(2, FileIdentity.Of(target).LinkCount);
    }

    [Fact]
    public void MissingFile_ComparesByFolderAndName()
    {
        string sub = this._directory.Combine("sub");
        Directory.CreateDirectory(sub);
        string junction = this._directory.Combine("junction");
        Assert.True(FileLinks.TryCreateJunction(junction, sub));

        var missing = FileIdentity.Of(Path.Combine(sub, "new.txt"));

        Assert.False(missing.Exists);
        Assert.True(missing.Matches(FileIdentity.Of(Path.Combine(junction, "NEW.TXT"))));
        Assert.True(missing.Matches(FileIdentity.Of(@"\\?\" + Path.Combine(sub, "new.txt"))));
        Assert.False(missing.Matches(FileIdentity.Of(Path.Combine(sub, "other.txt"))));
    }

    [Fact]
    public void DifferentFiles_AreNotSame()
    {
        string a = this._directory.Write("a.txt", new byte[] { 0x41 });
        string b = this._directory.Write("b.txt", new byte[] { 0x41 });

        Assert.False(FileIdentity.Of(a).Matches(FileIdentity.Of(b)));
    }

    [Theory]
    [InlineData(@"C:\dir\a.txt::$DATA", @"C:\dir\a.txt")]
    [InlineData(@"C:\dir\a.txt::$data", @"C:\dir\a.txt")]
    [InlineData(@"C:\dir\a.txt", @"C:\dir\a.txt")]
    [InlineData(@"\\?\C:\dir\a.txt::$DATA", @"\\?\C:\dir\a.txt")]
    public void FileSystemPath_MainStream_IsTheFile(string input, string expected)
    {
        Assert.True(FileSystemPath.TryGetFullPath(input, out string full));
        Assert.Equal(expected, full);
    }

    [Theory]
    [InlineData(@"C:\dir\a.txt:alt")]
    [InlineData(@"C:\dir\a.txt:alt:$DATA")]
    [InlineData(@"C:\dir\a.txt:")]
    [InlineData(@"C:\dir\a.txt:$DATA")]
    public void FileSystemPath_AlternateStream_IsRejected(string input)
    {
        Assert.False(FileSystemPath.TryGetFullPath(input, out _));
    }

    [Fact]
    public void ActiveReadRegistry_DetectsOtherSpellings()
    {
        string path = this._directory.Write("a.txt", new byte[] { 0x41 });

        using (ActiveReadRegistry.Register(path))
        {
            Assert.True(ActiveReadRegistry.IsBeingRead(@"\\?\" + path));
            Assert.True(ActiveReadRegistry.IsBeingRead(path.ToUpperInvariant()));
            Assert.False(ActiveReadRegistry.IsBeingRead(this._directory.Combine("b.txt")));
        }

        Assert.False(ActiveReadRegistry.IsBeingRead(path));
    }
}
