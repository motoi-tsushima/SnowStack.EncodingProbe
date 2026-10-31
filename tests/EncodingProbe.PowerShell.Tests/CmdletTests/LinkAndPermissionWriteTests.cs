using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 書き込みのリンク・権限の扱いのテスト（1.3.0 第五次の後半。実装記録 9.9 節の 4、H-17）。
/// </summary>
/// <remarks>
/// 上書きは一時ファイルで置き換えるが、シンボリックリンクはたどって実体を置き換え（リンクは残す）、
/// ハードリンクとフォルダーにファイルを作れない場合は、元のファイルを一時フォルダーに写してから直接書く。
/// どの書き方でも、属性（読み取り専用・隠し）・作成日時・代替データストリーム・ACL が保たれること（3 章 6.）も確かめる。
/// </remarks>
public class LinkAndPermissionWriteTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private static readonly byte[] Old = Encoding.ASCII.GetBytes("old\r\n");
    private static readonly byte[] NewLine = Encoding.ASCII.GetBytes("new\r\n");

    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public LinkAndPermissionWriteTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    /// <summary>境目（<see cref="SnowStack.EncodingProbe.PowerShell.Internal.SafeFileOverwrite.DirectWriteThreshold"/>）より 1 バイト大きい、元のファイルの内容</summary>
    private static byte[] LargeOriginal()
    {
        var content = new byte[SnowStack.EncodingProbe.PowerShell.Internal.SafeFileOverwrite.DefaultDirectWriteThreshold + 1];
        content.AsSpan().Fill((byte)'x');
        return content;
    }

    public static IEnumerable<object[]> Writers()
    {
        yield return new object[] { "Set", "6E 65 77 0D 0A" };
        yield return new object[] { "Out", "6E 65 77 0D 0A" };
        yield return new object[] { "Export", "22 41 22 0D 0A 22 6E 65 77 22 0D 0A" };
        yield return new object[] { "Convert", "EF BB BF 6F 6C 64 0D 0A" };
        yield return new object[] { "Add", "6F 6C 64 0D 0A 6E 65 77 0D 0A" };
    }

    /// <summary>
    /// シンボリックリンク（相対パス・2 段）に書くと、リンクを最後までたどった実体が書き換わり、リンクはそのまま残る
    /// </summary>
    [Theory]
    [MemberData(nameof(Writers))]
    public void SymbolicLink_TargetIsWritten_LinkRemains(string writer, string expected)
    {
        string target = this._directory.Write(@"sub\t.txt", Old);
        string link2 = this._directory.Combine("link2.txt");
        string link1 = this._directory.Combine("link1.txt");

        if (!FileLinks.TryCreateFileSymbolicLink(link2, @"sub\t.txt") || !FileLinks.TryCreateFileSymbolicLink(link1, "link2.txt"))
        {
            return;
        }

        Assert.Empty(Run(Build(writer, link1)).Errors);

        Assert.Equal(expected, Hex(File.ReadAllBytes(target)));
        Assert.NotNull(new FileInfo(link1).LinkTarget);
        Assert.NotNull(new FileInfo(link2).LinkTarget);
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// ハードリンクは切らない（ほかの名前からも新しい内容が見える）
    /// </summary>
    [Theory]
    [MemberData(nameof(Writers))]
    public void HardLink_IsKept(string writer, string expected)
    {
        string target = this._directory.Write("t.txt", Old);
        string hard = this._directory.Combine("hard.txt");
        Assert.True(FileLinks.TryCreateHardLink(hard, target));

        Assert.Empty(Run(Build(writer, hard)).Errors);

        Assert.Equal(expected, Hex(File.ReadAllBytes(target)));
        Assert.Equal(expected, Hex(File.ReadAllBytes(hard)));
        AssertNoTemporaryFile();
        AssertNoBackupLeft();
    }

    /// <summary>
    /// ハードリンクに書いている途中で失敗したら、元に戻す（どちらの名前からも元の内容）。
    /// 境目以内ならメモリに写してから直接書く方法で、境目を超えるなら写してから直接書く方法で書く（第五次の仕上げ）
    /// </summary>
    [Theory]
    [InlineData("Set", false)]
    [InlineData("Out", false)]
    [InlineData("Set", true)]
    [InlineData("Out", true)]
    public void HardLink_Failure_IsRestoredFromCopy(string writer, bool large)
    {
        byte[] original = large ? LargeOriginal() : Old;
        string target = this._directory.Write("t.txt", original);
        string hard = this._directory.Combine("hard.txt");
        Assert.True(FileLinks.TryCreateHardLink(hard, target));

        string script = writer == "Set"
            ? $"Set-ProbedContent -LiteralPath {Quote(hard)} -Value 'a', '한' -Encoding shift_jis"
            : $"'a', '한' | Out-ProbedFile -LiteralPath {Quote(hard)} -Encoding shift_jis";

        Assert.Equal("UnrepresentableCharacter", RunExpectingError(script));
        Assert.Equal(original, File.ReadAllBytes(target));
        Assert.Equal(original, File.ReadAllBytes(hard));
        AssertNoBackupLeft();
    }

    /// <summary>
    /// ハードリンクに書いている途中で失敗し、さらに写しから書き戻せなかった場合（書いている間に読み取り専用にされた）は、
    /// 写しを一時フォルダーに残し、エラーのメッセージに写しのパスと「この写しから元に戻す」ことを示す。ID は変えない（1.3.0 第五次の後半）。
    /// 写してから直接書く方法を通すため、元のファイルを境目より大きくする（境目以内では 1 件目をメモリにためるだけなので、元のファイルに触れない）
    /// </summary>
    [Theory]
    [InlineData("Set")]
    [InlineData("Out")]
    public void HardLink_RestoreFails_MessageShowsTheCopy(string writer)
    {
        byte[] original = LargeOriginal();
        string target = this._directory.Write("t.txt", original);
        string hard = this._directory.Combine("hard.txt");
        Assert.True(FileLinks.TryCreateHardLink(hard, target));

        // 1 件目を書いた後で読み取り専用にし、2 件目の表現できない文字で元に戻させる
        string upstream = $"& {{ 'a'; [IO.File]::SetAttributes({Quote(target)}, 'ReadOnly'); '한' }}";
        string script = writer == "Set"
            ? $"{upstream} | Set-ProbedContent -LiteralPath {Quote(hard)} -Encoding shift_jis -LineBreak CrLf"
            : $"{upstream} | Out-ProbedFile -LiteralPath {Quote(hard)} -Encoding shift_jis -LineBreak CrLf";

        System.Management.Automation.ErrorRecord? error = null;

        try
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors("try { " + script + " -ErrorAction Stop } catch { $_ }");
            error = (System.Management.Automation.ErrorRecord)Assert.Single(result.Output).BaseObject;
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }

        Assert.Equal("UnrepresentableCharacter", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.NotNull(error.ErrorDetails);

        string kept = Directory.GetFiles(Path.GetTempPath(), "SnowStack.EncodingProbe.*.probedbak")
            .Where(p => File.ReadAllBytes(p).SequenceEqual(original) && error.ErrorDetails.Message.Contains(p))
            .Single();

        try
        {
            Assert.Equal(
                error.Exception.Message + " " + SnowStack.EncodingProbe.PowerShell.ValidationMessages.RestoreFromBackupFailed(hard, kept),
                error.ErrorDetails.Message);
            Assert.NotEqual(original, File.ReadAllBytes(target));  // 元のファイルは書きかけ
        }
        finally
        {
            File.Delete(kept);
        }
    }

    /// <summary>
    /// リンク切れ: Set-ProbedContent は書かずに LinkTargetNotFound（標準の Set-Content と同じく作らない）。
    /// Out-ProbedFile・Export-ProbedCsv・Add-ProbedContent はリンクの先に作る（標準の Out-File・Export-Csv・Add-Content と同じ）
    /// </summary>
    [Theory]
    [InlineData("Set", null)]
    [InlineData("Out", "6E 65 77 0D 0A")]
    [InlineData("Export", "22 41 22 0D 0A 22 6E 65 77 22 0D 0A")]
    [InlineData("Add", "6E 65 77 0D 0A")]
    public void DanglingLink_FollowsStandard(string writer, string? expected)
    {
        Directory.CreateDirectory(this._directory.Combine("sub"));
        string link = this._directory.Combine("dangling.txt");
        string target = this._directory.Combine(@"sub\missing.txt");

        if (!FileLinks.TryCreateFileSymbolicLink(link, @"sub\missing.txt"))
        {
            return;
        }

        InvocationResult result = Run(Build(writer, link));

        if (expected == null)
        {
            Assert.Equal("LinkTargetNotFound", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
            Assert.False(File.Exists(target));
        }
        else
        {
            Assert.Empty(result.Errors);
            Assert.Equal(expected, Hex(File.ReadAllBytes(target)));
        }

        Assert.NotNull(new FileInfo(link).LinkTarget);
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// 読み取り専用はリンクの先の実体で調べる。-Force なしは WriteAccessDenied、-Force は書いて実体の属性を戻す
    /// </summary>
    [Fact]
    public void SymbolicLink_ReadOnlyTarget_IsCheckedOnTarget()
    {
        string target = this._directory.Write(@"sub\t.txt", Old);
        string link = this._directory.Combine("link.txt");

        if (!FileLinks.TryCreateFileSymbolicLink(link, @"sub\t.txt"))
        {
            return;
        }

        File.SetAttributes(target, FileAttributes.ReadOnly);

        Assert.Equal("WriteAccessDenied", Assert.Single(Run(Build("Set", link)).Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(Old, File.ReadAllBytes(target));

        Assert.Empty(Run(Build("Set", link) + " -Force").Errors);
        Assert.Equal("6E 65 77 0D 0A", Hex(File.ReadAllBytes(target)));
        Assert.True((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0);
    }

    /// <summary>
    /// フォルダーにファイルを作る権限が無く、ファイルにだけ書ける場合は、写してから直接書く（失敗したら書き戻す）
    /// </summary>
    [Fact]
    public void FolderWithoutCreatePermission_WritesDirectly()
    {
        string folder = this._directory.Combine("locked");
        Directory.CreateDirectory(folder);
        string path = this._directory.Write(@"locked\f.txt", Old);
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var info = new DirectoryInfo(folder);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllBytes(Path.Combine(folder, "probe.txt"), Old));

            Assert.Empty(Run(Build("Set", path)).Errors);
            Assert.Equal("6E 65 77 0D 0A", Hex(File.ReadAllBytes(path)));

            Assert.Equal("UnrepresentableCharacter", RunExpectingError($"Set-ProbedContent -LiteralPath {Quote(path)} -Value '한' -Encoding shift_jis"));
            Assert.Equal("6E 65 77 0D 0A", Hex(File.ReadAllBytes(path)));

            Assert.Empty(Run(Build("Convert", path)).Errors);
            Assert.Equal("EF BB BF 6E 65 77 0D 0A", Hex(File.ReadAllBytes(path)));

            // 作れないフォルダーに新しいファイルは作れない（WriteAccessDenied）
            Assert.Equal("WriteAccessDenied", Assert.Single(Run(Build("Set", Path.Combine(folder, "new.txt"))).Errors).FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(new[] { "f.txt" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
            AssertNoBackupLeft();
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
    }

    /// <summary>
    /// フォルダーにファイルを作る権限が無く、境目を超えるファイルの場合は、写してから直接書く（失敗したら写しから書き戻す）
    /// </summary>
    [Fact]
    public void FolderWithoutCreatePermission_LargeFile_CopiesThenWritesDirectly()
    {
        string folder = this._directory.Combine("locked");
        Directory.CreateDirectory(folder);
        byte[] original = LargeOriginal();
        string path = this._directory.Write(@"locked\f.txt", original);
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var info = new DirectoryInfo(folder);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);

        try
        {
            Assert.Equal("UnrepresentableCharacter", RunExpectingError($"Set-ProbedContent -LiteralPath {Quote(path)} -Value 'a', '한' -Encoding shift_jis"));
            Assert.Equal(original, File.ReadAllBytes(path));

            Assert.Empty(Run(Build("Set", path)).Errors);
            Assert.Equal("6E 65 77 0D 0A", Hex(File.ReadAllBytes(path)));
            Assert.Equal(new[] { "f.txt" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
            AssertNoBackupLeft();
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
    }

    public static IEnumerable<object[]> PreservationCases()
    {
        foreach (bool fail in new[] { false, true })
        {
            // 置き換えはハードリンクに使わない（ハードリンクで境目を超えるものは、写してから直接書く）
            yield return new object[] { "MemoryThenDirect", "Path", fail };
            yield return new object[] { "MemoryThenDirect", "HardLink", fail };
            yield return new object[] { "MemoryThenDirect", "SymbolicLink", fail };
            yield return new object[] { "Replace", "Path", fail };
            yield return new object[] { "Replace", "SymbolicLink", fail };
            yield return new object[] { "CopyThenDirect", "HardLink", fail };
            yield return new object[] { "Append", "Path", fail };
            yield return new object[] { "Append", "HardLink", fail };
            yield return new object[] { "Append", "SymbolicLink", fail };
        }
    }

    /// <summary>
    /// 4 つの書き方（メモリに写してから直接・置き換え・写してから直接・追記）のどれでも、そのまま・ハードリンク・シンボリックリンクのどの経路でも、
    /// 書けた場合も失敗して元に戻した場合も、属性（隠し・読み取り専用）・作成日時・代替データストリーム・ACL が保たれる（第五次の仕上げで書き方を 4 つにした）
    /// </summary>
    [Theory]
    [MemberData(nameof(PreservationCases))]
    public void Metadata_IsPreserved(string method, string via, bool fail)
    {
        // メモリに写してから直接書くのは境目以内、置き換えと写してから直接書くのは境目を超えるファイル
        byte[] original = method == "Replace" || method == "CopyThenDirect" ? LargeOriginal() : Old;
        string path = this._directory.Write("p.txt", original);
        string written = path;

        if (via == "HardLink")
        {
            written = this._directory.Combine("p-hard.txt");
            Assert.True(FileLinks.TryCreateHardLink(written, path));
        }
        else if (via == "SymbolicLink")
        {
            written = this._directory.Combine("p-link.txt");

            if (!FileLinks.TryCreateFileSymbolicLink(written, "p.txt"))
            {
                return;
            }
        }

        File.WriteAllText(path + ":meta", "adsdata");
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var info = new FileInfo(path);
        FileSecurity security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadData, AccessControlType.Allow));
        info.SetAccessControl(security);

        var created = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Local);
        File.SetCreationTime(path, created);
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.ReadOnly);
        string? entity = SnowStack.EncodingProbe.PowerShell.Internal.FileIdentity.Of(path).EntityKey;

        // 失敗させる場合は、1 件目を書いた後の表現できない文字で元に戻させる
        string value = fail ? "'new', '한' -Encoding shift_jis" : "new -Encoding utf8NoBOM";
        string script = method == "Append"
            ? $"Add-ProbedContent -LiteralPath {Quote(written)} -Value {value} -LineBreak CrLf -Force"
            : $"Set-ProbedContent -LiteralPath {Quote(written)} -Value {value} -LineBreak CrLf -Force";

        InvocationResult result = Run(script);

        byte[] expected = fail
            ? original
            : method == "Append" ? original.Concat(NewLine).ToArray() : NewLine;

        if (fail)
        {
            Assert.Equal("UnrepresentableCharacter", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        }
        else
        {
            Assert.Empty(result.Errors);
        }

        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Equal(expected, File.ReadAllBytes(written));
        Assert.Equal(FileAttributes.Hidden | FileAttributes.ReadOnly, File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.ReadOnly));
        Assert.Equal(created, File.GetCreationTime(path));
        Assert.Equal("adsdata", File.ReadAllText(path + ":meta"));
        Assert.Contains(
            new FileInfo(path).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            r => r.IdentityReference.Equals(everyone) && (r.FileSystemRights & FileSystemRights.ReadData) != 0);

        // 置き換えて書けた場合だけ実体が変わる（ほかの書き方は元のファイルそのものに書く）
        Assert.Equal(method == "Replace" && !fail, entity != SnowStack.EncodingProbe.PowerShell.Internal.FileIdentity.Of(path).EntityKey);
        AssertNoTemporaryFile();
        AssertNoBackupLeft();
    }

    /// <summary>
    /// ほかのプロセス（ここでは同じプロセスの別のハンドル）が書き込み先を共有の削除なしで開いていると、
    /// 失敗するのは完了の時点になる（WriteFailed）。元のファイルは変わらず、一時ファイルも残らない（既知の限界。ヘルプと仕様書に書いた）
    /// </summary>
    [Fact]
    public void OpenedByAnotherHandle_FailsAtCompletion()
    {
        string path = this._directory.Write("busy.txt", Old);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            InvocationResult result = Run(Build("Set", path));

            Assert.Equal("WriteFailed", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        }

        Assert.Equal(Old, File.ReadAllBytes(path));
        AssertNoTemporaryFile();
    }

    private string Build(string writer, string path)
    {
        string literal = Quote(path);

        return writer switch
        {
            "Set" => $"Set-ProbedContent -LiteralPath {literal} -Value new -Encoding utf8NoBOM -LineBreak CrLf",
            "Add" => $"Add-ProbedContent -LiteralPath {literal} -Value new -Encoding utf8NoBOM -LineBreak CrLf",
            "Out" => $"'new' | Out-ProbedFile -LiteralPath {literal} -Encoding utf8NoBOM -LineBreak CrLf",
            "Export" => $"[pscustomobject]@{{ A = 'new' }} | Export-ProbedCsv -LiteralPath {literal}",
            "Convert" => $"Convert-ProbedContent -LiteralPath {literal} -Encoding utf8BOM",
            _ => throw new ArgumentOutOfRangeException(nameof(writer)),
        };
    }

    private InvocationResult Run(string script) => this._fixture.InvokeScriptCapturingErrors(script);

    private string RunExpectingError(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            "try { " + script + " -ErrorAction Stop } catch { $_.FullyQualifiedErrorId.Split(',')[0] }");

        Assert.Empty(result.Errors);
        return (string)Assert.Single(result.Output).BaseObject;
    }

    private void AssertNoTemporaryFile()
        => Assert.Empty(Directory.GetFiles(this._directory.Path, "*.probedtmp", SearchOption.AllDirectories));

    private static void AssertNoBackupLeft()
        => Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "SnowStack.EncodingProbe.*.probedbak")
            .Where(p => File.GetLastWriteTimeUtc(p) > DateTime.UtcNow.AddMinutes(-5)));

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
