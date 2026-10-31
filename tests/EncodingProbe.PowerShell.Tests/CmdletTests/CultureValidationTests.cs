using System;
using System.IO;
using System.Management.Automation;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// -Culture / -Strategy の検証が、Resolve-Encoding を含むすべてのコマンドで同じ部品・同じ ID になることのテスト（1.3.0 第五次の後半。BUG-0006・0007）。
/// </summary>
/// <remarks>
/// 受け付けるカルチャー名は、.NET の CultureInfo ではなく名前の形（BCP 47）で決める。
/// 両ホストで同じ結果になることは PSCompat（Fifth/Culture）で確かめる。ここでは 7.x のランスペースで、期待する分類になることを確かめる。
/// </remarks>
public class CultureValidationTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly ByteExactFile _file = ByteExactFile.Create(new byte[] { 0x41, 0x0D, 0x0A });

    public CultureValidationTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._file.Dispose();

    /// <summary>
    /// 受け付けない名前（C・root は 7.x の ICU が受け付けていた）は、どのコマンドでも終了エラー InvalidCulture
    /// </summary>
    [Theory]
    [InlineData("C")]
    [InlineData("root")]
    [InlineData("POSIX")]
    [InlineData("not a culture!")]
    [InlineData("ja--JP")]
    [InlineData("en-u")]
    [InlineData("no-such-culture-xx")]
    public void Rejected_IsInvalidCultureForEveryCommand(string culture)
    {
        foreach (string command in new[]
        {
            $"Resolve-Encoding -Path {Quote(this._file.Path)}",
            $"Get-ProbedContent -LiteralPath {Quote(this._file.Path)}",
            $"Import-ProbedCsv -LiteralPath {Quote(this._file.Path)}",
            $"Set-ProbedContent -LiteralPath {Quote(this._file.Path)} -Value x",
            $"Convert-ProbedContent -LiteralPath {Quote(this._file.Path)}",
        })
        {
            ErrorRecord error = RunExpectingTerminatingError($"{command} -Culture '{culture.Replace("'", "''")}'");

            Assert.Equal("InvalidCulture", error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(ErrorCategory.InvalidArgument, error.CategoryInfo.Category);
            Assert.Equal(ValidationMessages.InvalidCulture(culture), error.Exception.Message);
            Assert.Null(error.Exception.InnerException);
        }

        Assert.Equal(new byte[] { 0x41, 0x0D, 0x0A }, File.ReadAllBytes(this._file.Path));
    }

    /// <summary>
    /// 形の正しい名前は受け付ける（実在しない名前・NLS だけ／ICU だけが受け付けていた名前も）。Resolve-Encoding の Culture にはそのままの名前が出る
    /// </summary>
    [Theory]
    [InlineData("ja-JP")]
    [InlineData("ko_KR")]
    [InlineData("xx-YY")]
    [InlineData("art-lojban")]
    [InlineData("zh-yue")]
    [InlineData("und-u-ca-gregory")]
    [InlineData("i-klingon")]
    [InlineData("x-test")]
    [InlineData("sgn-BE-FR")]
    [InlineData("de-DE_phoneb")]
    [InlineData("zh-Hant-HK-x-y")]
    [InlineData("cel-gaulish")]
    [InlineData("zh-min")]
    [InlineData("no-bok")]
    [InlineData("zh-cmn")]
    public void Accepted_ReachesDetection(string culture)
    {
        var info = (SnowStack.EncodingProbe.EncodingInformation)this._fixture.InvokeScript(
            $"Resolve-Encoding -Path {Quote(this._file.Path)} -Culture '{culture}'")[0].BaseObject;

        Assert.Equal(20127, info.CodePage);
        Assert.Equal(culture, info.Culture);
        // 判定を使うほかの経路（CSV の読み込み・-EncodingFrom・変換・先頭 N 行の判定）も最後まで通る（判定の後で CultureInfo を作らない）
        foreach (string script in new[]
        {
            $"Get-ProbedContent -LiteralPath {Quote(this._file.Path)} -Culture '{culture}'",
            $"Import-ProbedCsv -LiteralPath {Quote(this._file.Path)} -Culture '{culture}' | Out-Null",
            $"Resolve-Encoding -Path {Quote(this._file.Path)} -Culture '{culture}' -DetectionLineCount 1 | Out-Null",
            $"Set-ProbedContent -LiteralPath {Quote(this._file.Path + ".copy")} -Value x -EncodingFrom {Quote(this._file.Path)} -Culture '{culture}'",
            $"Convert-ProbedContent -LiteralPath {Quote(this._file.Path)} -Culture '{culture}' -Encoding utf8BOM -WhatIf",
        })
        {
            Assert.Empty(this._fixture.InvokeScriptCapturingErrors(script).Errors);
        }

        File.Delete(this._file.Path + ".copy");
    }

    /// <summary>
    /// 名前の前後の空白は除いて受け付け、除いた名前を判定に渡す（-Encoding・-Strategy と同じ。1.3.0 第六次 C。BUG-0038）
    /// </summary>
    [Theory]
    [InlineData(" ko-KR")]
    [InlineData("ko-KR ")]
    [InlineData("\tko-KR\t")]
    [InlineData("  ko-KR\r\n")]
    public void SurroundingWhitespace_IsTrimmed(string culture)
    {
        string literal = "\"" + culture.Replace("\t", "`t").Replace("\r", "`r").Replace("\n", "`n") + "\"";
        var info = (SnowStack.EncodingProbe.EncodingInformation)this._fixture.InvokeScript(
            $"Resolve-Encoding -Path {Quote(this._file.Path)} -Culture {literal}")[0].BaseObject;

        Assert.Equal("ko-KR", info.Culture);

        foreach (string script in new[]
        {
            $"Get-ProbedContent -LiteralPath {Quote(this._file.Path)} -Culture {literal}",
            $"Import-ProbedCsv -LiteralPath {Quote(this._file.Path)} -Culture {literal} | Out-Null",
            $"Set-ProbedContent -LiteralPath {Quote(this._file.Path + ".copy")} -Value x -EncodingFrom {Quote(this._file.Path)} -Culture {literal}",
            $"Convert-ProbedContent -LiteralPath {Quote(this._file.Path)} -Culture {literal} -Encoding utf8BOM -WhatIf",
        })
        {
            Assert.Empty(this._fixture.InvokeScriptCapturingErrors(script).Errors);
        }

        File.Delete(this._file.Path + ".copy");
    }

    /// <summary>
    /// 空白を除いても形の正しくない名前は、渡された元の値のまま InvalidCulture にする（BUG-0038）
    /// </summary>
    [Fact]
    public void InnerWhitespace_IsStillRejected_WithOriginalValue()
    {
        ErrorRecord error = RunExpectingTerminatingError($"Get-ProbedContent -LiteralPath {Quote(this._file.Path)} -Culture ' ko KR '");

        Assert.Equal("InvalidCulture", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ValidationMessages.InvalidCulture(" ko KR "), error.Exception.Message);
        Assert.Equal(" ko KR ", error.TargetObject);
    }

    /// <summary>
    /// Resolve-Encoding も、解釈できない -Strategy をほかのコマンドと同じ ID・分類・メッセージの終了エラーにする（BUG-0006）
    /// </summary>
    [Fact]
    public void ResolveEncoding_InvalidStrategy_IsSameAsOtherCommands()
    {
        ErrorRecord resolve = RunExpectingTerminatingError($"Resolve-Encoding -Path {Quote(this._file.Path)} -Strategy Nonexistent");
        ErrorRecord get = RunExpectingTerminatingError($"Get-ProbedContent -LiteralPath {Quote(this._file.Path)} -Strategy Nonexistent");

        Assert.Equal("InvalidStrategy", resolve.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(get.CategoryInfo.Category, resolve.CategoryInfo.Category);
        Assert.Equal(get.Exception.GetType(), resolve.Exception.GetType());
        Assert.Equal(ValidationMessages.InvalidStrategy("Nonexistent"), resolve.Exception.Message);
    }

    private ErrorRecord RunExpectingTerminatingError(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors("try { " + script + " } catch { $_ }");

        Assert.Empty(result.Errors);
        return (ErrorRecord)Assert.Single(result.Output).BaseObject;
    }

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
}
