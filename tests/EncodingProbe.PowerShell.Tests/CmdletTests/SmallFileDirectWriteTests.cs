using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 小さなファイルを、メモリに写してから直接書くことのテスト（1.3.0 第五次の仕上げ。実装記録 10.16 節）。
/// </summary>
/// <remarks>
/// どちらの方法で書いたかは、ファイルの実体（ボリュームのシリアル番号とファイル ID）が変わったかどうかで見分ける。
/// 置き換え（File.Replace）は一時ファイルの実体が残るので変わり、直接書けば変わらない。
/// </remarks>
[Collection(OverwriteStateCollection.Name)]
public class SmallFileDirectWriteTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private static readonly byte[] Old = Encoding.ASCII.GetBytes("old\r\n");

    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public SmallFileDirectWriteTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    private static int Threshold => checked((int)SafeFileOverwrite.DirectWriteThreshold);

    /// <summary>書く内容が境目ちょうどなら直接書き（実体が変わらない）、1 バイト超えたら置き換える（実体が変わる）</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Set_ContentAtThreshold(int extra, bool direct)
    {
        string path = this._directory.Write("a.txt", Old);
        string? before = Entity(path);

        // ASCII だけなので、utf8NoBOM で文字数とバイト数が同じ
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath {Quote(path)} -Value ('n' * {Threshold + extra}) -Encoding utf8NoBOM -NoNewline");

        Assert.Empty(result.Errors);
        Assert.Equal(Threshold + extra, new FileInfo(path).Length);
        Assert.Equal(direct, before == Entity(path));
        AssertNoTemporaryFile();
    }

    /// <summary>元のファイルが境目ちょうどなら直接書き、1 バイト超えたら置き換える（書く内容は小さい）</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Set_OriginalAtThreshold(int extra, bool direct)
    {
        string path = this._directory.Write("a.txt", Filled(Threshold + extra, (byte)'o'));
        string? before = Entity(path);

        Assert.Empty(Run(Build("Set", path)).Errors);

        Assert.Equal("6E 65 77 0D 0A", Hex(File.ReadAllBytes(path)));
        Assert.Equal(direct, before == Entity(path));
    }

    /// <summary>上書きを使う 4 つのコマンドのどれも、小さなファイルには直接書く</summary>
    [Theory]
    [InlineData("Set", "6E 65 77 0D 0A")]
    [InlineData("Out", "6E 65 77 0D 0A")]
    [InlineData("Export", "22 41 22 0D 0A 22 6E 65 77 22 0D 0A")]
    [InlineData("Convert", "EF BB BF 6F 6C 64 0D 0A")]
    [InlineData("ConvertDestination", "EF BB BF 6F 6C 64 0D 0A")]
    public void Writers_SmallFile_WriteDirectly(string writer, string expected)
    {
        string path = this._directory.Write("a.txt", Old);
        string target = writer == "ConvertDestination" ? this._directory.Write(@"dest\a.txt", Encoding.ASCII.GetBytes("previous\r\n")) : path;
        string? before = Entity(target);

        Assert.Empty(Run(Build(writer, path)).Errors);

        Assert.Equal(expected, Hex(File.ReadAllBytes(target)));
        Assert.Equal(before, Entity(target));
        AssertNoTemporaryFile();
    }

    /// <summary>まだ無いファイルにも書ける（一時ファイルを残さない）</summary>
    [Theory]
    [InlineData("Set", "6E 65 77 0D 0A")]
    [InlineData("Out", "6E 65 77 0D 0A")]
    [InlineData("Export", "22 41 22 0D 0A 22 6E 65 77 22 0D 0A")]
    [InlineData("ConvertDestination", "EF BB BF 6F 6C 64 0D 0A")]
    public void Writers_NewFile(string writer, string expected)
    {
        string source = this._directory.Write("a.txt", Old);
        Directory.CreateDirectory(this._directory.Combine("dest"));
        string target = writer == "ConvertDestination" ? this._directory.Combine(@"dest\a.txt") : this._directory.Combine("new.txt");

        Assert.Empty(Run(Build(writer, writer == "ConvertDestination" ? source : target)).Errors);

        Assert.Equal(expected, Hex(File.ReadAllBytes(target)));
        Assert.False(TemporaryFileRegistry.Contains(target));
        AssertNoTemporaryFile();
    }

    /// <summary>表現できない文字で失敗したら、元のファイルはバイト単位で元どおり（実体も変わらない）。まだ無かったファイルは残らない</summary>
    [Theory]
    [InlineData("Set", false)]
    [InlineData("Out", false)]
    [InlineData("Export", false)]
    [InlineData("Set", true)]
    [InlineData("Out", true)]
    [InlineData("Export", true)]
    public void Unrepresentable_IsRestoredExactly(string writer, bool newFile)
    {
        byte[] original = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        string path = newFile ? this._directory.Combine("new.txt") : this._directory.Write("a.txt", original);
        string? before = newFile ? null : Entity(path);
        string literal = Quote(path);

        string script = writer switch
        {
            "Set" => $"Set-ProbedContent -LiteralPath {literal} -Value 'a', '한' -Encoding shift_jis",
            "Out" => $"'a', '한' | Out-ProbedFile -LiteralPath {literal} -Encoding shift_jis",
            _ => $"[pscustomobject]@{{ A = 'a' }}, [pscustomobject]@{{ A = '한' }} | Export-ProbedCsv -LiteralPath {literal} -Encoding shift_jis",
        };

        Assert.Equal("UnrepresentableCharacter", RunExpectingError(script));

        if (newFile)
        {
            Assert.False(File.Exists(path));
        }
        else
        {
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(before, Entity(path));
        }

        AssertNoTemporaryFile();
    }

    /// <summary>
    /// 直接書いている途中で IOException（ほかのハンドルが、元のファイルの末尾より先の範囲をロックしている）になったら、
    /// WriteFailed を報告し、元のファイルはバイト単位で元どおりになる
    /// </summary>
    [Theory]
    [InlineData("Set")]
    [InlineData("Out")]
    [InlineData("Export")]
    [InlineData("Convert")]
    public void WriteIOException_IsRestoredExactly(string writer)
    {
        string path = this._directory.Write("a.txt", Old);
        string literal = Quote(path);

        string script = writer switch
        {
            "Set" => $"Set-ProbedContent -LiteralPath {literal} -Value ('n' * 100) -Encoding utf8NoBOM",
            "Out" => $"('n' * 100) | Out-ProbedFile -LiteralPath {literal} -Encoding utf8NoBOM",
            "Export" => $"[pscustomobject]@{{ A = ('n' * 100) }} | Export-ProbedCsv -LiteralPath {literal}",
            _ => $"Convert-ProbedContent -LiteralPath {literal} -Encoding utf8BOM",
        };

        using (var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            // Convert は 8 バイト（BOM＋old＋改行）を書くので、元の 5 バイトの直後をロックする
            locker.Lock(writer == "Convert" ? Old.Length + 1 : 50, 10);

            try
            {
                Assert.Equal("WriteFailed", RunExpectingError(script));
            }
            finally
            {
                locker.Unlock(writer == "Convert" ? Old.Length + 1 : 50, 10);
            }
        }

        Assert.Equal(Old, File.ReadAllBytes(path));
    }

    /// <summary>
    /// ほかのハンドルが開いているときは、どちらの書き方も完了の時点で失敗する（WriteFailed、元のファイルは変わらない）。
    /// 失敗する開き方は書き方によって違う: 直接書く方法は相手が書き込みの共有を許していないと、置き換えは削除の共有を許していないと失敗する
    /// </summary>
    [Theory]
    [InlineData(FileShare.Read, false, false)]
    [InlineData(FileShare.Read, true, false)]
    [InlineData(FileShare.ReadWrite, false, true)]
    [InlineData(FileShare.ReadWrite, true, false)]
    [InlineData(FileShare.Read | FileShare.Delete, false, false)]
    [InlineData(FileShare.Read | FileShare.Delete, true, true)]
    [InlineData(FileShare.ReadWrite | FileShare.Delete, false, true)]
    [InlineData(FileShare.ReadWrite | FileShare.Delete, true, true)]
    public void OpenedByAnotherHandle(FileShare share, bool large, bool succeeds)
    {
        byte[] original = large ? Filled(Threshold + 1, (byte)'o') : Old;
        string path = this._directory.Write("busy.txt", original);
        InvocationResult result;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, share))
        {
            result = Run(Build("Set", path));
        }

        if (succeeds)
        {
            Assert.Empty(result.Errors);
            Assert.Equal("6E 65 77 0D 0A", Hex(File.ReadAllBytes(path)));
        }
        else
        {
            Assert.Equal("WriteFailed", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(original, File.ReadAllBytes(path));
        }

        AssertNoTemporaryFile();
    }

    /// <summary>
    /// ファイルに WriteData の拒否の ACL があると、大きさによらず WriteAccessDenied で、元のファイルは変わらない（ファイルの ACL に従う。標準の Set-Content と同じ）。
    /// 境目を超えるファイルも、置き換え（フォルダーの権限で行われる）の前に書き込み用に開けるかを確かめるので、書けてしまわない（第五次の仕上げ）
    /// </summary>
    [Theory]
    [InlineData("Set", false)]
    [InlineData("Set", true)]
    [InlineData("Add", false)]
    [InlineData("Add", true)]
    [InlineData("Out", false)]
    [InlineData("Out", true)]
    [InlineData("Export", false)]
    [InlineData("Export", true)]
    [InlineData("Convert", false)]
    [InlineData("Convert", true)]
    public void WriteDataDenied_IsWriteAccessDenied_RegardlessOfSize(string writer, bool large)
    {
        byte[] original = large ? Filled(Threshold + 1, (byte)'o') : Old;
        string path = this._directory.Write("denied.txt", original);
        string? before = Entity(path);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            user, System.Security.AccessControl.FileSystemRights.WriteData, System.Security.AccessControl.AccessControlType.Deny);
        var info = new FileInfo(path);
        System.Security.AccessControl.FileSecurity security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        string script = writer == "Add"
            ? $"Add-ProbedContent -LiteralPath {Quote(path)} -Value new -Encoding utf8NoBOM -LineBreak CrLf"
            : Build(writer, path);

        try
        {
            Assert.Equal("WriteAccessDenied", RunExpectingError(script));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(before, Entity(path));
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// -ErrorAction Stop でも、元に戻してからエラーにする（後始末が、ためていた分を書いてしまわない）。
    /// パイプラインの 1 件目でためた分も残さない（既存のファイルは元どおり、まだ無かったファイルは残らない）
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorActionStop_LeavesNothing(bool newFile)
    {
        string path = newFile ? this._directory.Combine("new.txt") : this._directory.Write("a.txt", Old);

        Assert.Equal("UnrepresentableCharacter", RunExpectingError($"'n', '한' | Set-ProbedContent -LiteralPath {Quote(path)} -Encoding shift_jis"));

        if (newFile)
        {
            Assert.False(File.Exists(path));
        }
        else
        {
            Assert.Equal(Old, File.ReadAllBytes(path));
        }
    }

    /// <summary>
    /// パイプラインの停止（Ctrl+C。StopProcessing）では、それまでに受け取った内容を書く（メモリにためていた分も書き出す）
    /// </summary>
    [Theory]
    [InlineData("Set", false)]
    [InlineData("Set", true)]
    [InlineData("Out", false)]
    [InlineData("Out", true)]
    public void StopProcessing_WritesReceivedContent(string writer, bool newFile)
    {
        string path = newFile ? this._directory.Combine("new.txt") : this._directory.Write("a.txt", Old);
        string marker = this._directory.Combine("marker");
        string upstream = $"& {{ 'a'; [IO.File]::WriteAllText({Quote(marker)}, ''); Start-Sleep -Seconds 60; 'b' }}";
        string script = writer == "Set"
            ? $"{upstream} | Set-ProbedContent -LiteralPath {Quote(path)} -Encoding utf8NoBOM -LineBreak CrLf"
            : $"{upstream} | Out-ProbedFile -LiteralPath {Quote(path)} -Encoding utf8NoBOM -LineBreak CrLf";

        using (System.Management.Automation.PowerShell shell = System.Management.Automation.PowerShell.Create())
        {
            shell.Runspace = this._fixture.Runspace;
            shell.AddScript(script);
            IAsyncResult pending = shell.BeginInvoke();

            SpinWait.SpinUntil(() => File.Exists(marker), TimeSpan.FromSeconds(30));
            Assert.True(File.Exists(marker));

            shell.Stop();

            try
            {
                shell.EndInvoke(pending);
            }
            catch (PipelineStoppedException)
            {
            }
        }

        Assert.Equal("61 0D 0A", Hex(File.ReadAllBytes(path)));
        Assert.False(TemporaryFileRegistry.Contains(path));
        AssertNoTemporaryFile();
    }

    private string Build(string writer, string path)
    {
        string literal = Quote(path);

        return writer switch
        {
            "Set" => $"Set-ProbedContent -LiteralPath {literal} -Value new -Encoding utf8NoBOM -LineBreak CrLf",
            "Out" => $"'new' | Out-ProbedFile -LiteralPath {literal} -Encoding utf8NoBOM -LineBreak CrLf",
            "Export" => $"[pscustomobject]@{{ A = 'new' }} | Export-ProbedCsv -LiteralPath {literal}",
            "Convert" => $"Convert-ProbedContent -LiteralPath {literal} -Encoding utf8BOM",
            "ConvertDestination" => $"Convert-ProbedContent -LiteralPath {literal} -Destination {Quote(this._directory.Combine("dest"))} -Encoding utf8BOM -Force",
            _ => throw new ArgumentOutOfRangeException(nameof(writer)),
        };
    }

    private InvocationResult Run(string script) => this._fixture.InvokeScriptCapturingErrors(script);

    private string RunExpectingError(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            "try { " + script + " -ErrorAction Stop | Out-Null } catch { $_.FullyQualifiedErrorId.Split(',')[0] }");

        Assert.Empty(result.Errors);
        return (string)Assert.Single(result.Output).BaseObject;
    }

    private static string? Entity(string path) => FileIdentity.Of(path).EntityKey;

    private static byte[] Filled(int length, byte value) => Enumerable.Repeat(value, length).ToArray();

    private void AssertNoTemporaryFile()
        => Assert.Empty(Directory.GetFiles(this._directory.Path, "*" + SafeFileOverwrite.TemporarySuffix, SearchOption.AllDirectories));

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
