using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// パスの束縛（BUG-0003）と、書き込み側のワイルドカードが 0 件の場合（BUG-0015）のテスト（1.3.0 第五次の後半）。
/// </summary>
/// <remarks>
/// 別名 FullName を -Path から -LiteralPath に移した。FileInfo をパイプラインで渡すと、-LiteralPath に PSPath（Get-ChildItem）か
/// FullName（New-Object System.IO.FileInfo）で束縛され、[ ] を含む名前もワイルドカードとして展開しない。
/// 文字列をパイプラインで渡した場合は、これまでどおり -Path に入り、ワイルドカードとして扱う。
/// </remarks>
public class PathBindingTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public PathBindingTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    /// <summary>
    /// 6 つのコマンドの束縛の形がそろっていること（-Path に FullName が無く、-LiteralPath が PSPath・LP・FullName を持つ）
    /// </summary>
    [Theory]
    [InlineData(typeof(GetProbedContentCommand))]
    [InlineData(typeof(ImportProbedCsvCommand))]
    [InlineData(typeof(SetProbedContentCommand))]
    [InlineData(typeof(AddProbedContentCommand))]
    [InlineData(typeof(ConvertProbedContentCommand))]
    public void Metadata_FullNameAliasIsOnLiteralPath(Type cmdletType)
    {
        var metadata = new CommandMetadata(cmdletType);

        Assert.Equal(new[] { "FilePath" }, metadata.Parameters["Path"].Aliases);
        Assert.Equal(new[] { "PSPath", "LP", "FullName" }, metadata.Parameters["LiteralPath"].Aliases);
        Assert.All(metadata.Parameters["LiteralPath"].ParameterSets.Values, set => Assert.True(set.ValueFromPipelineByPropertyName));
    }

    /// <summary>
    /// Resolve-Encoding の -Path は、もともとワイルドカードを展開しないので、FullName の別名のまま（既存のパラメータを変えない）
    /// </summary>
    [Fact]
    public void Metadata_ResolveEncoding_KeepsFullNameOnLiteralOnlyPath()
    {
        var metadata = new CommandMetadata(typeof(ResolveEncodingCmdlet));

        Assert.Equal(new[] { "FullName", "FilePath" }, metadata.Parameters["Path"].Aliases);
        Assert.False(metadata.Parameters.ContainsKey("LiteralPath"));
    }

    /// <summary>
    /// [ ] を含む名前のファイルと、その名前がワイルドカードとして一致する別のファイルを並べ、
    /// Get-ChildItem / Get-Item / New-Object FileInfo から渡したときに、流したファイルそのものを読むこと
    /// </summary>
    [Theory]
    [InlineData("Get-Item -LiteralPath {0}")]
    [InlineData("New-Object System.IO.FileInfo {0}")]
    public void Read_FileInfoFromPipeline_ReadsThatFile(string source)
    {
        string target = this._directory.Write("a[1].csv", Encoding.ASCII.GetBytes("A\r\nx\r\n"));
        this._directory.Write("a1.csv", Encoding.ASCII.GetBytes("A\r\ny\r\n"));
        string producer = string.Format(source, Quote(target));

        Assert.Equal(new[] { "A", "x" }, Strings($"{producer} | Get-ProbedContent"));
        Assert.Equal(new[] { "x" }, Strings($"{producer} | Import-ProbedCsv | ForEach-Object {{ $_.A }}"));
    }

    [Fact]
    public void Read_GetChildItem_ReadsEachFileOnce()
    {
        this._directory.Write("a[1].csv", Encoding.ASCII.GetBytes("A\r\nx\r\n"));
        this._directory.Write("a1.csv", Encoding.ASCII.GetBytes("A\r\ny\r\n"));
        this._directory.Write("b[12].csv", Encoding.ASCII.GetBytes("A\r\nz\r\n"));
        this._directory.Write("b1.csv", Encoding.ASCII.GetBytes("A\r\nw\r\n"));
        this._directory.Write("b2.csv", Encoding.ASCII.GetBytes("A\r\nv\r\n"));

        string[] values = Strings($"Get-ChildItem -LiteralPath {Quote(this._directory.Path)} | Import-ProbedCsv | ForEach-Object {{ $_.A }}");

        Assert.Equal(new[] { "v", "w", "x", "y", "z" }, values.OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// 書き込みの 2 コマンドも、流したファイルそのものに書き、ワイルドカードとして一致する別のファイルには触れないこと
    /// </summary>
    [Theory]
    [InlineData("Set", "Get-Item -LiteralPath {0}", "7A 0D 0A")]
    [InlineData("Add", "Get-Item -LiteralPath {0}", "78 0D 0A 7A 0D 0A")]
    [InlineData("Set", "New-Object System.IO.FileInfo {0}", "7A 0D 0A")]
    [InlineData("Add", "New-Object System.IO.FileInfo {0}", "78 0D 0A 7A 0D 0A")]
    public void Write_FileInfoFromPipeline_WritesThatFile(string command, string source, string expected)
    {
        string target = this._directory.Write("b[1].txt", Encoding.ASCII.GetBytes("x\r\n"));
        string other = this._directory.Write("b1.txt", Encoding.ASCII.GetBytes("y\r\n"));
        string producer = string.Format(source, Quote(target));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"{producer} | {command}-ProbedContent -Value z -Encoding utf8NoBOM -LineBreak CrLf");

        Assert.Empty(result.Errors);
        Assert.Equal(expected, Hex(File.ReadAllBytes(target)));
        Assert.Equal("79 0D 0A", Hex(File.ReadAllBytes(other)));
    }

    /// <summary>
    /// Convert-ProbedContent も New-Object FileInfo（PSPath を持たない）から受け取れること。文字列のパイプラインは -Path でワイルドカードとして扱う
    /// </summary>
    [Fact]
    public void Convert_AcceptsFileInfoWithoutPSPath_AndPipedWildcardStrings()
    {
        string target = this._directory.Write("c[1].txt", Encoding.ASCII.GetBytes("x\r\n"));
        string other = this._directory.Write("c1.txt", Encoding.ASCII.GetBytes("y\r\n"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"New-Object System.IO.FileInfo {Quote(target)} | Convert-ProbedContent -Encoding utf8BOM");

        Assert.Empty(result.Errors);
        Assert.Equal("EF BB BF 78 0D 0A", Hex(File.ReadAllBytes(target)));
        Assert.Equal("79 0D 0A", Hex(File.ReadAllBytes(other)));

        result = this._fixture.InvokeScriptCapturingErrors(
            $"{Quote(this._directory.Combine("c*.txt"))} | Convert-ProbedContent -Encoding utf8BOM");

        Assert.Empty(result.Errors);
        Assert.Equal("EF BB BF 79 0D 0A", Hex(File.ReadAllBytes(other)));
    }

    /// <summary>
    /// 文字列をパイプラインで渡した場合は、これまでどおりワイルドカードとして扱う
    /// </summary>
    [Fact]
    public void Read_PipedString_IsStillWildcard()
    {
        this._directory.Write("d1.txt", Encoding.ASCII.GetBytes("one\r\n"));
        this._directory.Write("d2.txt", Encoding.ASCII.GetBytes("two\r\n"));

        Assert.Equal(new[] { "one", "two" }, Strings($"{Quote(this._directory.Combine("d?.txt"))} | Get-ProbedContent"));
    }

    /// <summary>
    /// 書き込み側で、ワイルドカードが 0 件に一致したら、非終了エラー ItemNotFound（ObjectNotFound）にする（標準の Set-Content / Add-Content と同じ）
    /// </summary>
    [Theory]
    [InlineData("Set", "c[1].txt")]
    [InlineData("Add", "c[1].txt")]
    [InlineData("Set", "x*.txt")]
    [InlineData("Add", "x*.txt")]
    public void Write_WildcardMatchesNothing_IsItemNotFound(string command, string pattern)
    {
        string path = this._directory.Combine(pattern);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"{command}-ProbedContent -Path {Quote(path)} -Value x -Encoding utf8NoBOM; 'after'");

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("ItemNotFound", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ErrorCategory.ObjectNotFound, error.CategoryInfo.Category);
        Assert.Equal(ValidationMessages.WildcardMatchedNoFileForWrite(path), error.Exception.Message);
        Assert.Equal(new[] { "after" }, result.AsStrings());
        Assert.Empty(Directory.GetFiles(this._directory.Path));
    }

    /// <summary>
    /// -LiteralPath なら [ ] を含む名前のファイルを作れる。読み込み側の 0 件はエラーにしない（これまでどおり）
    /// </summary>
    [Fact]
    public void Write_LiteralPath_CreatesBracketName_AndReadZeroMatchIsNotError()
    {
        string path = this._directory.Combine("c[1].txt");

        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath {Quote(path)} -Value x -Encoding utf8NoBOM").Errors);
        Assert.True(File.Exists(path));

        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"Get-ProbedContent -Path {Quote(this._directory.Combine("zz*.txt"))}").Errors);
        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"Import-ProbedCsv -Path {Quote(this._directory.Combine("zz*.csv"))}").Errors);
    }

    /// <summary>
    /// 書き込み先のワイルドカードの 0 件は、ほかのパスの書き込みを止めない（非終了エラー）
    /// </summary>
    [Fact]
    public void Write_WildcardMatchesNothing_OtherPathsAreWritten()
    {
        string existing = this._directory.Write("e.txt", Encoding.ASCII.GetBytes("old\r\n"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -Path {Quote(this._directory.Combine("none*.txt"))}, {Quote(existing)} -Value new -Encoding utf8NoBOM -LineBreak CrLf");

        Assert.Equal("ItemNotFound", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal("6E 65 77 0D 0A", Hex(File.ReadAllBytes(existing)));
    }

    private string[] Strings(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);

        Assert.Empty(result.Errors);
        return result.AsStrings();
    }

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
