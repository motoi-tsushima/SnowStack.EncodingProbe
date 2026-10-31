using System.IO;
using System.Linq;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Set-ProbedContent / Add-ProbedContent の -Value の配列の $null の要素を、
/// 標準の Set-Content / Add-Content と同じく飛ばすことを検証する（1.3.0 第六次。BUG-0033）。
/// </summary>
/// <remarks>
/// 期待値は、標準の Set-Content / Add-Content を 5.1 と 7.x で実測したバイト列（両ホストで同じ。実装記録 11 章）。
/// すべて $null の場合、Set- は空のファイル（既存なら空にする）、Add- は何も足さない（無ければ空のファイルを作る）。
/// パイプラインで渡した場合は、もともと標準と同じだった。
/// </remarks>
public class NullValueElementTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private static readonly byte[] Old = { 0x4F, 0x4C, 0x44, 0x0D, 0x0A };

    private readonly ProbedCommandRunspaceFixture _fixture;

    public NullValueElementTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    [Theory]
    [InlineData("-Value @('a', $null, 'b')", "", false, new byte[] { 0x61, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    [InlineData("-Value @('a', $null, 'b')", "", true, new byte[] { 0x61, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    [InlineData("-Value @($null, 'b')", "", false, new byte[] { 0x62, 0x0D, 0x0A })]
    [InlineData("-Value @('a', $null)", "", false, new byte[] { 0x61, 0x0D, 0x0A })]
    [InlineData("-Value @('a', $null, 'b')", "-NoNewline", false, new byte[] { 0x61, 0x62 })]
    [InlineData("-Value @($null, $null)", "", false, new byte[0])]
    [InlineData("-Value @($null, $null)", "", true, new byte[0])]
    [InlineData("-Value @($null)", "", true, new byte[0])]
    [InlineData("-Value @($null, $null)", "-NoNewline", true, new byte[0])]
    [InlineData("-Value @('a', '', 'b')", "", false, new byte[] { 0x61, 0x0D, 0x0A, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    public void Set_NullElements_AreSkipped(string value, string extra, bool existing, byte[] expected)
    {
        using var directory = new TemporaryDirectory();
        string path = existing ? directory.Write("a.txt", Old) : directory.Combine("a.txt");

        Invoke($"Set-ProbedContent -LiteralPath '{path}' {value} -Encoding utf8NoBOM -LineBreak CrLf {extra}");

        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("-Value @('a', $null, 'b')", "", true, new byte[] { 0x4F, 0x4C, 0x44, 0x0D, 0x0A, 0x61, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    [InlineData("-Value @('a', $null, 'b')", "-NoNewline", true, new byte[] { 0x4F, 0x4C, 0x44, 0x0D, 0x0A, 0x61, 0x62 })]
    [InlineData("-Value @($null, $null)", "", true, new byte[] { 0x4F, 0x4C, 0x44, 0x0D, 0x0A })]
    [InlineData("-Value @($null)", "", true, new byte[] { 0x4F, 0x4C, 0x44, 0x0D, 0x0A })]
    [InlineData("-Value @($null, $null)", "", false, new byte[0])]
    [InlineData("-Value @('a', $null, 'b')", "", false, new byte[] { 0x61, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    public void Add_NullElements_AreSkipped(string value, string extra, bool existing, byte[] expected)
    {
        using var directory = new TemporaryDirectory();
        string path = existing ? directory.Write("a.txt", Old) : directory.Combine("a.txt");

        Invoke($"Add-ProbedContent -LiteralPath '{path}' {value} -Encoding utf8NoBOM -LineBreak CrLf {extra}");

        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 同じ配列を -Value で渡してもパイプラインで渡しても、同じバイト列になること。
    /// </summary>
    [Theory]
    [InlineData("Set-ProbedContent", "@('a', $null, 'b')")]
    [InlineData("Set-ProbedContent", "@($null, $null)")]
    [InlineData("Add-ProbedContent", "@('a', $null, 'b')")]
    [InlineData("Add-ProbedContent", "@($null, $null)")]
    public void ValueAndPipeline_AreSame(string command, string array)
    {
        using var directory = new TemporaryDirectory();
        string byValue = directory.Write("v.txt", Old);
        string byPipeline = directory.Write("p.txt", Old);

        Invoke($"{command} -LiteralPath '{byValue}' -Value {array} -Encoding utf8NoBOM -LineBreak CrLf");
        Invoke($"{array} | {command} -LiteralPath '{byPipeline}' -Encoding utf8NoBOM -LineBreak CrLf");

        Assert.Equal(File.ReadAllBytes(byPipeline), File.ReadAllBytes(byValue));
    }

    private void Invoke(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);

        Assert.Empty(result.Errors);
    }
}
