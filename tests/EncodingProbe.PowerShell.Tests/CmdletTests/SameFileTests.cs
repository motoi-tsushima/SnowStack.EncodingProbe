using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 同じファイルの判定をファイルの実体で行うことのテスト（1.3.0 第五次の後半。BUG-0004・0016・0017）。
/// </summary>
/// <remarks>
/// 第五次の前半まではパスの文字列で比べていたため、書き方を変えると SamePathRoundTrip をすり抜け、
/// 読み込み中のファイルが黙って切り詰められた（Add-ProbedContent は終わらずにファイルが増え続けた）。
/// </remarks>
public class SameFileTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    /// <summary>上流が読み終える前に下流が書き始める大きさ（2000 行）</summary>
    private static readonly byte[] Data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("line\r\n", 2000)));

    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public SameFileTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    public static IEnumerable<object[]> Spellings()
    {
        foreach (string spelling in new[] { "upper", "extended", "device", "unc", "symlink", "junction", "hardlink", "mainstream" })
        {
            foreach (string writer in new[] { "Set", "Add", "Out", "OutAppend" })
            {
                yield return new object[] { spelling, writer };
            }
        }
    }

    /// <summary>
    /// どの書き方でも SamePathRoundTrip で止まり、元のファイルは変わらない（Add-ProbedContent も終わる）
    /// </summary>
    [Theory]
    [MemberData(nameof(Spellings))]
    public void RoundTrip_AnySpelling_IsDetected(string spelling, string writer)
    {
        string path = this._directory.Write(@"sub\data.txt", Data);
        string? other = Spell(spelling, path);

        if (other == null)
        {
            return;  // その書き方を作れない環境（管理共有・シンボリックリンク）
        }

        string downstream = writer switch
        {
            "Set" => $"Set-ProbedContent -LiteralPath {Quote(other)} -Encoding utf8NoBOM",
            "Add" => $"Add-ProbedContent -LiteralPath {Quote(other)} -Encoding utf8NoBOM",
            "Out" => $"Out-ProbedFile -LiteralPath {Quote(other)} -Encoding utf8NoBOM",
            _ => $"Out-ProbedFile -LiteralPath {Quote(other)} -Encoding utf8NoBOM -Append",
        };

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ Get-ProbedContent -LiteralPath {Quote(path)} | {downstream} }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        string id = writer.StartsWith("Out", StringComparison.Ordinal)
            ? (string)Assert.Single(result.Output).BaseObject
            : Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0];

        Assert.Equal("SamePathRoundTrip", id);
        Assert.Equal(Data, File.ReadAllBytes(path));
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// Import-ProbedCsv | Export-ProbedCsv も実体で比べる
    /// </summary>
    [Theory]
    [InlineData("extended", false)]
    [InlineData("junction", false)]
    [InlineData("extended", true)]
    public void RoundTrip_Csv_IsDetected(string spelling, bool append)
    {
        byte[] csv = Encoding.ASCII.GetBytes("A,B\r\n" + string.Concat(Enumerable.Repeat("1,2\r\n", 2000)));
        string path = this._directory.Write(@"sub\data.csv", csv);
        string other = Spell(spelling, path)!;

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ Import-ProbedCsv -LiteralPath {Quote(path)} | Export-ProbedCsv -LiteralPath {Quote(other)}{(append ? " -Append" : string.Empty)} }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        Assert.Equal("SamePathRoundTrip", Assert.Single(result.Output).BaseObject);
        Assert.Equal(csv, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 1 回の Set-ProbedContent で同じ実体を 2 回指定しても、1 回書いたのと同じ結果になる（BUG-0016。メッセージは出さない）
    /// </summary>
    [Theory]
    [InlineData("-LiteralPath {0}, {0} -Value Z")]
    [InlineData("-LiteralPath {0}, {1} -Value Z")]
    [InlineData("-LiteralPath {0}, {2} -Value Z")]
    public void Set_SameFileTwiceInOneRecord_WritesOnce(string arguments)
    {
        string path = this._directory.Write("a.txt", Encoding.ASCII.GetBytes("old\r\n"));
        string hard = this._directory.Combine("hard.txt");
        Assert.True(FileLinks.TryCreateHardLink(hard, path));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            "Set-ProbedContent " + string.Format(arguments, Quote(path), Quote(path.ToUpperInvariant()), Quote(hard)) + " -Encoding utf8NoBOM -LineBreak CrLf");

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal("5A 0D 0A", Hex(File.ReadAllBytes(path)));
    }

    /// <summary>
    /// パスをパイプラインから受け取る場合も、同じ実体は 1 回書いたのと同じ結果（標準の Set-Content と同じ）。値のパイプラインは追記のまま
    /// </summary>
    [Fact]
    public void Set_SameFileFromPipelineTwice_WritesOnce_ButPipedValuesAccumulate()
    {
        string path = this._directory.Write("a.txt", Encoding.ASCII.GetBytes("old\r\n"));

        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"(Get-Item -LiteralPath {Quote(path)}), (Get-Item -LiteralPath {Quote(path)}) | Set-ProbedContent -Value Z -Encoding utf8NoBOM -LineBreak CrLf").Errors);
        Assert.Equal("5A 0D 0A", Hex(File.ReadAllBytes(path)));

        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"'p', 'q' | Set-ProbedContent -LiteralPath {Quote(path)} -Encoding utf8NoBOM -LineBreak CrLf").Errors);
        Assert.Equal("70 0D 0A 71 0D 0A", Hex(File.ReadAllBytes(path)));
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// Add-ProbedContent: 1 回の指定の中の重複は 1 回だけ追記する（7.x の標準の Add-Content と同じ）。
    /// パイプラインのレコードをまたぐ場合は、レコードごとに追記する（両ホストの標準と同じ）
    /// </summary>
    [Fact]
    public void Add_SameFileTwice_FollowsStandard()
    {
        string path = this._directory.Write("a.txt", Encoding.ASCII.GetBytes("old\r\n"));

        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"Add-ProbedContent -LiteralPath {Quote(path)}, {Quote(path.ToUpperInvariant())} -Value Z -Encoding utf8NoBOM -LineBreak CrLf").Errors);
        Assert.Equal("6F 6C 64 0D 0A 5A 0D 0A", Hex(File.ReadAllBytes(path)));

        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"(Get-Item -LiteralPath {Quote(path)}), (Get-Item -LiteralPath {Quote(path)}) | Add-ProbedContent -Value Y -Encoding utf8NoBOM -LineBreak CrLf").Errors);
        Assert.Equal("6F 6C 64 0D 0A 5A 0D 0A 59 0D 0A 59 0D 0A", Hex(File.ReadAllBytes(path)));
    }

    /// <summary>
    /// まだ無いファイルを 2 つの書き方で指定しても、1 つの書き込み先として扱う
    /// </summary>
    [Fact]
    public void Set_MissingFileTwoSpellings_IsOneTarget()
    {
        string path = this._directory.Combine("new.txt");

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath {Quote(path)}, {Quote(@"\\?\" + path)} -Value Z -Encoding utf8NoBOM -LineBreak CrLf");

        Assert.Empty(result.Errors);
        Assert.Equal("5A 0D 0A", Hex(File.ReadAllBytes(path)));
        AssertNoTemporaryFile();
    }

    public static IEnumerable<object[]> FolderSpellings()
    {
        foreach (string spelling in new[] { "upper", "extended", "device", "unc", "junction", "dirlink" })
        {
            yield return new object[] { spelling, false };
            yield return new object[] { spelling, true };
        }
    }

    /// <summary>
    /// Convert-ProbedContent -Destination に変換元のフォルダーを別の書き方で渡しても N9（DestinationIsSource）。-Force でも変換元を書き換えない（BUG-0017）
    /// </summary>
    [Theory]
    [MemberData(nameof(FolderSpellings))]
    public void Convert_DestinationIsSourceFolder_AnySpelling_IsN9(string spelling, bool force)
    {
        byte[] original = Encoding.ASCII.GetBytes("abc\r\n");
        string path = this._directory.Write(@"src\a.txt", original);
        string? folder = SpellFolder(spelling, Path.GetDirectoryName(path)!);

        if (folder == null)
        {
            return;
        }

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Convert-ProbedContent -LiteralPath {Quote(path)} -Encoding utf8BOM -Destination {Quote(folder)}{(force ? " -Force" : string.Empty)}");

        Assert.Equal("DestinationIsSource", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// N8: 同じ名前の 2 つの変換元を、別の書き方の同じフォルダーへ出力しようとしたら、2 つめは DestinationNameConflict
    /// </summary>
    [Fact]
    public void Convert_DestinationNameConflict_ComparesByEntity()
    {
        string first = this._directory.Write(@"one\a.txt", Encoding.ASCII.GetBytes("1\r\n"));
        string second = this._directory.Write(@"two\a.txt", Encoding.ASCII.GetBytes("2\r\n"));
        string destination = this._directory.Combine("out");
        Directory.CreateDirectory(destination);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Convert-ProbedContent -LiteralPath {Quote(first)} -Encoding utf8BOM -Destination {Quote(destination)}; "
            + $"Convert-ProbedContent -LiteralPath {Quote(first)}, {Quote(second)} -Encoding utf8BOM -Destination {Quote(@"\\?\" + destination)} -Force");

        Assert.Equal("DestinationNameConflict", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal("EF BB BF 31 0D 0A", Hex(File.ReadAllBytes(Path.Combine(destination, "a.txt"))));
    }

    /// <summary>
    /// 主ストリームの名前（::$DATA）は、そのファイルとして読み書きする。代替データストリームは両ホストで AlternateDataStreamNotSupported
    /// </summary>
    [Fact]
    public void MainStreamName_IsTheFile_AlternateStreamIsRejected()
    {
        string path = this._directory.Write("s.txt", Encoding.ASCII.GetBytes("abc\r\n"));

        InvocationResult read = this._fixture.InvokeScriptCapturingErrors($"Get-ProbedContent -LiteralPath {Quote(path + "::$DATA")}");
        Assert.Empty(read.Errors);
        Assert.Equal(new[] { "abc" }, read.AsStrings());

        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath {Quote(path + "::$DATA")} -Value q -Encoding utf8NoBOM -LineBreak CrLf").Errors);
        Assert.Equal("71 0D 0A", Hex(File.ReadAllBytes(path)));

        Assert.Equal(20127, (int)this._fixture.InvokeScript($"(Resolve-Encoding -Path {Quote(path + "::$DATA")}).CodePage")[0].BaseObject);

        foreach (string script in new[]
        {
            $"Get-ProbedContent -LiteralPath {Quote(path + ":alt")}",
            $"Set-ProbedContent -LiteralPath {Quote(path + ":alt")} -Value x -Encoding utf8NoBOM",
            $"Add-ProbedContent -LiteralPath {Quote(path + ":alt")} -Value x -Encoding utf8NoBOM",
            $"Import-ProbedCsv -LiteralPath {Quote(path + ":alt")}",
            $"Convert-ProbedContent -LiteralPath {Quote(path + ":alt")} -Encoding utf8BOM",
        })
        {
            Assert.Equal("AlternateDataStreamNotSupported", Assert.Single(this._fixture.InvokeScriptCapturingErrors(script).Errors).FullyQualifiedErrorId.Split(',')[0]);
        }

        foreach (string script in new[]
        {
            $"'x' | Out-ProbedFile -LiteralPath {Quote(path + ":alt")}",
            $"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath {Quote(path + ":alt")}",
            $"Resolve-Encoding -Path {Quote(path + ":alt")}",
        })
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors($"try {{ {script} }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");
            Assert.Equal("AlternateDataStreamNotSupported", Assert.Single(result.Output).BaseObject);
        }

        Assert.Equal("71 0D 0A", Hex(File.ReadAllBytes(path)));
    }

    /// <summary>テストの対象のパスを、別の書き方にする。作れない場合は null。</summary>
    private string? Spell(string spelling, string path)
    {
        string folder = Path.GetDirectoryName(path)!;
        string name = Path.GetFileName(path);

        switch (spelling)
        {
            case "upper":
                return path.ToUpperInvariant();
            case "extended":
                return @"\\?\" + path;
            case "device":
                return @"\\.\" + path;
            case "unc":
                return FileLinks.ToAdminShare(path);
            case "mainstream":
                return path + "::$DATA";
            case "symlink":
                string link = this._directory.Combine("link-" + name);
                return FileLinks.TryCreateFileSymbolicLink(link, path) ? link : null;
            case "hardlink":
                string hard = this._directory.Combine("hard-" + name);
                return FileLinks.TryCreateHardLink(hard, path) ? hard : null;
            case "junction":
                string? junctionFolder = SpellFolder("junction", folder);
                return junctionFolder == null ? null : Path.Combine(junctionFolder, name);
            default:
                throw new ArgumentOutOfRangeException(nameof(spelling));
        }
    }

    private string? SpellFolder(string spelling, string folder)
    {
        switch (spelling)
        {
            case "upper":
                return folder.ToUpperInvariant();
            case "extended":
                return @"\\?\" + folder;
            case "device":
                return @"\\.\" + folder;
            case "unc":
                return FileLinks.ToAdminShare(folder);
            case "junction":
                string junction = this._directory.Combine("junction-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                return FileLinks.TryCreateJunction(junction, folder) ? junction : null;
            case "dirlink":
                string link = this._directory.Combine("dirlink-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                return FileLinks.TryCreateDirectorySymbolicLink(link, folder) ? link : null;
            default:
                throw new ArgumentOutOfRangeException(nameof(spelling));
        }
    }

    private void AssertNoTemporaryFile()
        => Assert.Empty(Directory.GetFiles(this._directory.Path, "*.probedtmp", SearchOption.AllDirectories));

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
