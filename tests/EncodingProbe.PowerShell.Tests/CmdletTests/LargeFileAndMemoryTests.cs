using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 全体の判定の上限を超えるファイルと、メモリ不足を、ID の付いたエラーにすることを検証する（1.3.0 第六次 B。BUG-0030）。
/// </summary>
/// <remarks>
/// 大きなファイルを作らずに確かめるため、上限（<see cref="EncodingInheritance.MaxDetectableLength"/>）を小さくし、
/// 全体を読む配列を作る直前の差し込み口（<see cref="SharedFileReader.BeforeAllocateForTests"/>）で OutOfMemoryException を投げる。
/// 静的な設定を変えるので、ほかのテストと並行に走らせない。
/// </remarks>
[Collection(OverwriteStateCollection.Name)]
public class LargeFileAndMemoryTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private static readonly byte[] Csv = { 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x0D, 0x0A };

    private readonly ProbedCommandRunspaceFixture _fixture;

    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public LargeFileAndMemoryTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose()
    {
        EncodingInheritance.MaxDetectableLength = EncodingInheritance.DefaultMaxDetectableLength;
        SharedFileReader.BeforeAllocateForTests = null;
        this._directory.Dispose();
    }

    /// <summary>
    /// Resolve-Encoding は、読む前に大きさを調べ、上限を超えるファイルを -DetectionLineCount を案内する非終了エラーにして、次のファイルへ進む
    /// （5.1 では生の OutOfMemoryException / IOException になっていた）
    /// </summary>
    [Fact]
    public void ResolveEncoding_TooLarge_IsNonTerminating_WithLineCountHint()
    {
        string small = this._directory.Write("a.csv", Csv.Take(4).ToArray());
        string large = this._directory.Write("b.csv", Csv);
        EncodingInheritance.MaxDetectableLength = 5;

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'{large}', '{small}' | ForEach-Object {{ Resolve-Encoding -Path $_ }} | ForEach-Object CodePage");

        Assert.Equal(new[] { "20127" }, result.Output.Select(o => o.ToString()));
        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("EncodingDetectionFailed", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ValidationMessages.FileTooLargeToDetectUseLineCount(large), error.Exception.Message);

        // -DetectionLineCount なら、先頭の行だけを読むので判定できる
        InvocationResult head = this._fixture.InvokeScriptCapturingErrors($"(Resolve-Encoding -Path '{large}' -DetectionLineCount 1).CodePage");
        Assert.Empty(head.Errors);
        Assert.Equal("20127", head.Output.Single().ToString());
    }

    /// <summary>
    /// 全体を読む判定でメモリが足りなければ、どのコマンドも NotEnoughMemoryToDetect（ファイルは変わらない）
    /// </summary>
    [Theory]
    [InlineData("Resolve-Encoding -Path '{0}'", false)]
    [InlineData("Get-ProbedContent -LiteralPath '{0}'", false)]
    [InlineData("Import-ProbedCsv -LiteralPath '{0}'", false)]
    [InlineData("Set-ProbedContent -LiteralPath '{0}' -Value x", false)]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value x", false)]
    [InlineData("'x' | Out-ProbedFile -LiteralPath '{0}' -Append", true)]
    [InlineData("[pscustomobject]@{{A='x';B='y'}} | Export-ProbedCsv -LiteralPath '{0}' -Append", true)]
    public void Detection_OutOfMemory_IsIdentifiedError(string template, bool terminating)
    {
        string path = this._directory.Write("a.csv", Csv);
        SharedFileReader.BeforeAllocateForTests = _ => throw new OutOfMemoryException();

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ {string.Format(template, path)} }} catch {{ 'TERM'; $_ }}");
        bool caught = result.Output.Any(o => o.ToString() == "TERM");
        ErrorRecord error = result.Errors.Concat(result.Output.Select(o => o.BaseObject).OfType<ErrorRecord>()).First();

        Assert.Equal(terminating, caught);
        Assert.Equal("NotEnoughMemoryToDetect", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ValidationMessages.NotEnoughMemoryToDetect(path), error.Exception.Message);
        Assert.Equal(Csv, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Convert-ProbedContent: 上限を超えるファイルは読む前に FileTooLargeToConvert、メモリが足りなければ NotEnoughMemoryToConvert（ファイルは変わらない）
    /// </summary>
    [Theory]
    [InlineData(true, "FileTooLargeToConvert")]
    [InlineData(false, "NotEnoughMemoryToConvert")]
    public void Convert_TooLargeOrOutOfMemory_IsNonTerminating(bool tooLarge, string expectedId)
    {
        string path = this._directory.Write("a.csv", Csv);
        string other = this._directory.Write("b.csv", Csv.Take(4).ToArray());

        if (tooLarge)
        {
            EncodingInheritance.MaxDetectableLength = 5;
        }
        else
        {
            SharedFileReader.BeforeAllocateForTests = length => { if (length > 5) { throw new OutOfMemoryException(); } };
        }

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Convert-ProbedContent -LiteralPath '{path}', '{other}' -SourceEncoding utf8NoBOM -Encoding utf8BOM -PassThru | ForEach-Object Changed");

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal(expectedId, error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ErrorCategory.LimitsExceeded, error.CategoryInfo.Category);
        Assert.Equal(
            tooLarge ? ValidationMessages.FileTooLargeToConvert(path) : ValidationMessages.NotEnoughMemoryToConvert(path),
            error.Exception.Message);
        Assert.Equal(Csv, File.ReadAllBytes(path));
        Assert.Equal(new[] { "True" }, result.Output.Select(o => o.ToString()));
    }
}
