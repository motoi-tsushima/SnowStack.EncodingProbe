using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// ISO-2022 の追記先が ASCII 以外の状態のまま終わっているとき、追記の前に ASCII に戻すシーケンスを書くことを検証する
/// （1.3.0 第六次。BUG-0018）。
/// </summary>
/// <remarks>
/// 書く順序は「戻すシーケンス → 補う改行（Export-ProbedCsv、仕様書 18.3）→ 追記する内容」。
/// エラーで止まったら、戻すシーケンスも残さない。何も書かない呼び出しでは書かない。
/// -AllowEncodingChange で別の文字エンコーディングを足す場合は書かない（利用者の決定）。
/// </remarks>
public class Iso2022AppendTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string NoError = "NO-ERROR";

    /// <summary>A,B CRLF 1, ESC $ B 「日」（ESC ( B も改行も無い）</summary>
    private static readonly byte[] JisTail = { 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x1B, 0x24, 0x42, 0x46, 0x7C };

    /// <summary>JIS のまま CRLF で終わる</summary>
    private static readonly byte[] JisTailCrLf = JisTail.Concat(new byte[] { 0x0D, 0x0A }).ToArray();

    private static readonly byte[] ToAscii = { 0x1B, 0x28, 0x42 };

    private readonly ProbedCommandRunspaceFixture _fixture;

    public Iso2022AppendTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'xy'")]
    [InlineData("'xy' | Out-ProbedFile -LiteralPath '{0}' -Append")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'xy' -Encoding iso-2022-jp")]
    [InlineData("'xy' | Out-ProbedFile -LiteralPath '{0}' -Append -Encoding iso-2022-jp")]
    public void Text_JisTail_ResetsBeforeContent(string template)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", JisTail);

        Assert.Equal(NoError, Run(string.Format(template, path)));
        Assert.Equal(JisTail.Concat(ToAscii).Concat(Encoding.ASCII.GetBytes("xy\r\n")), File.ReadAllBytes(path));
        Assert.Equal("1,日xy", Value($"(Get-ProbedContent -LiteralPath '{path}')[-1]"));
    }

    [Fact]
    public void Csv_JisTail_ResetThenLineBreakThenRecord()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.csv", JisTail);

        Assert.Equal(NoError, Run($"[pscustomobject]@{{A='x';B='y'}} | Export-ProbedCsv -LiteralPath '{path}' -Append"));
        Assert.Equal(
            JisTail.Concat(ToAscii).Concat(Encoding.ASCII.GetBytes("\r\n\"x\",\"y\"\r\n")),
            File.ReadAllBytes(path));
        Assert.Equal("2 y", Value($"$r = @(Import-ProbedCsv -LiteralPath '{path}'); '{{0}} {{1}}' -f $r.Count, $r[-1].B"));
    }

    /// <summary>
    /// JIS のまま CRLF で終わる形も戻す（改行は状態を戻さない）。改行はすでにあるので補わない。
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'xy'", "xy\r\n")]
    [InlineData("'xy' | Out-ProbedFile -LiteralPath '{0}' -Append", "xy\r\n")]
    [InlineData("[pscustomobject]@{{A='x';B='y'}} | Export-ProbedCsv -LiteralPath '{0}' -Append", "\"x\",\"y\"\r\n")]
    public void JisTailWithCrLf_ResetsWithoutExtraLineBreak(string template, string appended)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", JisTailCrLf);

        Assert.Equal(NoError, Run(string.Format(template, path)));
        Assert.Equal(JisTailCrLf.Concat(ToAscii).Concat(Encoding.ASCII.GetBytes(appended)), File.ReadAllBytes(path));
    }

    /// <summary>
    /// 日本語を含む行を追記した場合も、先頭の文字が直前の JIS の状態に飲まれない。
    /// </summary>
    [Fact]
    public void JapaneseLine_IsReadBack()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", JisTail);

        Assert.Equal(NoError, Run($"Add-ProbedContent -LiteralPath '{path}' -Value 'x本'"));
        // 追記先に改行が無いので、同じ行につながる（ASCII で終わる追記先と同じ）
        Assert.Equal("1,日x本", Value($"(Get-ProbedContent -LiteralPath '{path}' -Encoding iso-2022-jp)[-1]"));
    }

    /// <summary>
    /// ASCII に戻っている追記先には何も足さない（第五次までと同じバイト列）。
    /// </summary>
    [Fact]
    public void AsciiTail_IsUnchanged()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = JisTail.Concat(ToAscii).ToArray();
        string path = directory.Write("a.txt", original);

        Assert.Equal(NoError, Run($"Add-ProbedContent -LiteralPath '{path}' -Value 'xy'"));
        Assert.Equal(original.Concat(Encoding.ASCII.GetBytes("xy\r\n")), File.ReadAllBytes(path));
    }

    /// <summary>
    /// ISO-2022-KR は、SO の状態で終わっていれば SI を書く。
    /// </summary>
    [Fact]
    public void Kr_ShiftOutTail_WritesShiftIn()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = { 0x1B, 0x24, 0x29, 0x43, 0x41, 0x0D, 0x0A, 0x0E, 0x30, 0x21 };  // SO の後に 1 文字。SI が無い
        string path = directory.Write("a.txt", original);

        Assert.Equal(NoError, Run($"Add-ProbedContent -LiteralPath '{path}' -Value 'xy' -Encoding iso-2022-kr"));

        byte[] written = File.ReadAllBytes(path);
        Assert.Equal(original.Concat(new byte[] { 0x0F }), written.Take(original.Length + 1));
        Assert.EndsWith("xy", Value($"(Get-ProbedContent -LiteralPath '{path}' -Encoding iso-2022-kr)[-1]"));
    }

    /// <summary>
    /// 何も書かない呼び出しでは、戻すシーケンスも書かない。
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value @()")]
    [InlineData("@() | Out-ProbedFile -LiteralPath '{0}' -Append")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'x' -WhatIf")]
    public void NothingWritten_FileUnchanged(string template)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", JisTail);

        Assert.Equal(NoError, Run(string.Format(template, path)));
        Assert.Equal(JisTail, File.ReadAllBytes(path));
    }

    /// <summary>
    /// エラーで止まったら、戻すシーケンスも残さない（その呼び出しで書いた分を残さない規則）。
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'ok', '한'", "UnrepresentableCharacter")]
    [InlineData("'ok', '한' | Out-ProbedFile -LiteralPath '{0}' -Append", "UnrepresentableCharacter")]
    [InlineData("'ok', '한' | ForEach-Object {{ [pscustomobject]@{{A=$_;B='y'}} }} | Export-ProbedCsv -LiteralPath '{0}' -Append", "UnrepresentableCharacter")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'ok', 'é' -Encoding utf8NoBOM", "EncodingChangeOnAppend")]
    public void Failure_LeavesNoResetSequence(string template, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", JisTail);

        Assert.Equal(expected, Run(string.Format(template, path)));
        Assert.Equal(JisTail, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -AllowEncodingChange で別の文字エンコーディングを足す場合は、追記先の状態に手を入れない（利用者の決定）。
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'xy' -Encoding utf8NoBOM -AllowEncodingChange")]
    [InlineData("'xy' | Out-ProbedFile -LiteralPath '{0}' -Append -Encoding utf8NoBOM -AllowEncodingChange")]
    public void AllowEncodingChange_OtherEncoding_DoesNotReset(string template)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", JisTail);

        Assert.Equal(NoError, Run(string.Format(template, path)));
        Assert.Equal(JisTail.Concat(Encoding.ASCII.GetBytes("xy\r\n")), File.ReadAllBytes(path));
    }

    private string Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ $ErrorActionPreference = 'Stop'; {script} ; '{NoError}' }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        return result.Output.Last().ToString();
    }

    private string Value(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);

        Assert.Empty(result.Errors);
        return result.Output.Last().ToString();
    }
}
