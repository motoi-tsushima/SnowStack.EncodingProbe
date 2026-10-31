using System.Linq;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 短い EUC-JP を ja-JP で Shift_JIS と判定しないことを、コマンドの経路（全体の判定と -DetectionLineCount）で検証する（1.3.0 第六次。BUG-0031）。
/// </summary>
public class ShortEucJpDetectionTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    /// <summary>「日本語の文書です」の EUC-JP（CP932 では FC CB などが割り当てられていない）</summary>
    private static readonly byte[] Text = { 0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC, 0xA4, 0xCE, 0xCA, 0xB8, 0xBD, 0xF1, 0xA4, 0xC7, 0xA4, 0xB9 };

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ShortEucJpDetectionTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    [Theory]
    [InlineData(new byte[] { 0x0D, 0x0A })]
    [InlineData(new byte[] { 0x0A })]
    [InlineData(new byte[0])]
    public void WholeFile_IsEucJpAndReadable(byte[] lineBreak)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Text.Concat(lineBreak).ToArray());

        Assert.Equal("20932", Value($"(Resolve-Encoding -Path '{path}' -Culture ja-JP).CodePage"));
        Assert.Equal("日本語の文書です", Value($"@(Get-ProbedContent -LiteralPath '{path}' -Culture ja-JP)[0]"));
    }

    /// <summary>
    /// 先頭 N 行で判定する場合も同じ規則（標本は切り出した先頭の行）。
    /// </summary>
    [Fact]
    public void DetectionLineCount_IsEucJp()
    {
        using var directory = new TemporaryDirectory();

        // 1 行目だけが短い EUC-JP、2 行目以降は英数字
        byte[] content = new byte[] { 0x48, 0x0D, 0x0A }.Concat(Text).Concat(new byte[] { 0x0D, 0x0A, 0x41, 0x0D, 0x0A }).ToArray();
        string path = directory.Write("a.csv", content);

        Assert.Equal("20932", Value($"(Resolve-Encoding -Path '{path}' -Culture ja-JP -DetectionLineCount 2).CodePage"));
        Assert.Equal("日本語の文書です", Value($"@(Import-ProbedCsv -LiteralPath '{path}' -Culture ja-JP -DetectionLineCount 2)[0].H"));
    }

    private string Value(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);

        Assert.Empty(result.Errors);
        return result.Output.Last().ToString();
    }
}
