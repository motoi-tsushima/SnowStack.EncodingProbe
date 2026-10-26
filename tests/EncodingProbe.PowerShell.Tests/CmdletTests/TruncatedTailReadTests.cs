using System.IO;
using System.Linq;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 途中で切れたファイルの末尾を、置換フォールバックの文字として読むことを検証する（1.3.0 第六次。BUG-0042）。
/// </summary>
/// <remarks>
/// このテストプロジェクトは net10.0 だけで動くので、ここで固定するのは 7.x の挙動（.NET の StreamReader はストリームの終わりで Decoder を flush する）。
/// 5.1（.NET Framework）が同じ結果になることは PSCompat（Sixth/Truncated/*）が確かめる。
/// Convert-ProbedContent は不正なバイト列として変換しない（InvalidSourceBytes。今までどおり）。
/// </remarks>
public class TruncatedTailReadTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private readonly ProbedCommandRunspaceFixture _fixture;

    public TruncatedTailReadTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    [Theory]
    [InlineData("Auto", new byte[] { 0xFF, 0xFE, 0x41, 0, 0x0D, 0, 0x0A, 0, 0x31, 0, 0x32 }, "0041 000D 000A 0031 FFFD")]
    [InlineData("unicodeNoBOM", new byte[] { 0x41, 0, 0x0D, 0, 0x0A, 0, 0x31, 0, 0x32 }, "0041 000D 000A 0031 FFFD")]
    [InlineData("bigendianunicodeNoBOM", new byte[] { 0, 0x41, 0, 0x0D, 0, 0x0A, 0, 0x31, 0x32 }, "0041 000D 000A 0031 FFFD")]
    [InlineData("utf32NoBOM", new byte[] { 0x41, 0, 0, 0, 0x0A, 0, 0, 0, 0x31 }, "0041 000A FFFD")]
    [InlineData("utf32NoBOM", new byte[] { 0x41, 0, 0, 0, 0x0A, 0, 0, 0, 0x31, 0, 0 }, "0041 000A FFFD")]
    [InlineData("utf8NoBOM", new byte[] { 0x41, 0x0D, 0x0A, 0xE6, 0x97 }, "0041 000D 000A FFFD")]
    public void Raw_TruncatedTail_IsReplacementCharacter(string encoding, byte[] content, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", content);

        Assert.Equal(expected, Run($"(Get-ProbedContent -LiteralPath '{path}' -Encoding {encoding} -Raw).ToCharArray() | ForEach-Object {{ '{{0:X4}}' -f [int]$_ }}"));
    }

    [Fact]
    public void Lines_AndCsv_OddUtf16_EndWithReplacementCharacter()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.csv", new byte[] { 0xFF, 0xFE, 0x41, 0, 0x0D, 0, 0x0A, 0, 0x31, 0, 0x32 });

        Assert.Equal("1\uFFFD", Run($"(Get-ProbedContent -LiteralPath '{path}')[1]"));
        Assert.Equal("1\uFFFD", Run($"(Import-ProbedCsv -LiteralPath '{path}').A"));
    }

    [Fact]
    public void Convert_OddUtf16_IsStillInvalidSourceBytes()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = { 0xFF, 0xFE, 0x41, 0, 0x0D, 0, 0x0A, 0, 0x31, 0, 0x32 };
        string path = directory.Write("a.txt", original);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Convert-ProbedContent -LiteralPath '{path}' -Encoding utf8NoBOM");

        Assert.Equal("InvalidSourceBytes", result.Errors.Single().FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    private string Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);

        Assert.Empty(result.Errors);
        return string.Join(" ", result.Output.Select(o => o.ToString()));
    }
}
