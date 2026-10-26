using System.IO;
using System.Linq;
using System.Management.Automation;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// ほかのプログラムが開いているファイルの読み込み・判定を検証する（1.3.0 第六次 B。BUG-0026）。
/// </summary>
/// <remarks>
/// 判定と変換の読み取りは、Get-ProbedContent と同じ共有モード（ReadWrite）で開く。書き込み用に開かれていても読める。
/// それでも開けない場合（相手が共有を許していない）は、ID の付いた 5 言語のメッセージのエラー（ReadFailed）。Resolve-Encoding は非終了エラーで次のファイルへ進む。
/// 追記は、相手が書き込みの共有を許していないと開けない（今までどおり。WriteFailed の 5 言語のメッセージ）。ファイルは変わらない。
/// </remarks>
public class OpenedByAnotherProgramTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private static readonly byte[] Csv = { 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x0D, 0x0A };

    private readonly ProbedCommandRunspaceFixture _fixture;

    public OpenedByAnotherProgramTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>
    /// 書き込み中のログのように、ReadWrite の共有で開かれているファイル: 読み込み・判定・変換はできる
    /// </summary>
    [Theory]
    [InlineData("Resolve-Encoding -Path '{0}' | ForEach-Object CodePage", "20127")]
    [InlineData("Resolve-Encoding -Path '{0}' -DetectionLineCount 1 | ForEach-Object CodePage", "20127")]
    [InlineData("(Get-ProbedContent -LiteralPath '{0}')[0]", "A,B")]
    [InlineData("(Import-ProbedCsv -LiteralPath '{0}').B", "2")]
    [InlineData("Convert-ProbedContent -LiteralPath '{0}' -Destination '{1}' -Encoding utf8BOM -PassThru | ForEach-Object Changed", "True")]
    [InlineData("Set-ProbedContent -LiteralPath '{1}\\new.txt' -Value x -EncodingFrom '{0}'; 'written'", "written")]
    public void OpenForWriteWithShareReadWrite_CanBeRead(string template, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("b.csv", Csv);
        string output = directory.Combine("out");
        Directory.CreateDirectory(output);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors(string.Format(template, path, output));

            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Last().ToString());
        }
    }

    /// <summary>
    /// Resolve-Encoding は、開けないファイルを非終了エラー ReadFailed にして、残りのファイルを処理する
    /// </summary>
    [Fact]
    public void ResolveEncoding_LockedFile_IsNonTerminating_AndContinues()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("a.csv", Csv);
        string locked = directory.Write("b.csv", Csv);
        directory.Write("c.csv", Csv);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
                $"Get-ChildItem -LiteralPath '{directory.Path}' -Filter *.csv | Resolve-Encoding | ForEach-Object CodePage");

            Assert.Equal(new[] { "20127", "20127" }, result.Output.Select(o => o.ToString()));
            ErrorRecord error = Assert.Single(result.Errors);
            Assert.Equal("ReadFailed", error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(ErrorCategory.ReadError, error.CategoryInfo.Category);
            Assert.Equal(ValidationMessages.FileInUseForRead(locked), error.Exception.Message);
        }
    }

    /// <summary>
    /// 共有を許さずに開かれているファイル: どのコマンドも ID の付いたエラーで、メッセージは本モジュールのもの。ファイルは変わらない
    /// </summary>
    [Theory]
    [InlineData("Get-ProbedContent -LiteralPath '{0}'", "ReadFailed")]
    [InlineData("Import-ProbedCsv -LiteralPath '{0}'", "ReadFailed")]
    [InlineData("Convert-ProbedContent -LiteralPath '{0}' -Encoding utf8BOM", "ReadFailed")]
    [InlineData("Set-ProbedContent -LiteralPath '{0}' -Value x", "ReadFailed")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value x", "ReadFailed")]
    [InlineData("'x' | Out-ProbedFile -LiteralPath '{0}' -Append", "ReadFailed")]
    [InlineData("[pscustomobject]@{{A='x';B='y'}} | Export-ProbedCsv -LiteralPath '{0}' -Append", "ReadFailed")]
    [InlineData("Set-ProbedContent -LiteralPath '{1}\\new.txt' -Value x -EncodingFrom '{0}'", "ReadFailed")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value x -Encoding utf8NoBOM -AllowEncodingChange", "WriteFailed")]
    public void ShareNone_IsIdentifiedError(string template, string expectedId)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("b.csv", Csv);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
                $"try {{ {string.Format(template, path, directory.Path)} }} catch {{ $_ }}");
            ErrorRecord error = result.Errors.Concat(result.Output.Select(o => o.BaseObject).OfType<ErrorRecord>()).First();

            Assert.Equal(expectedId, error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(
                expectedId == "ReadFailed" ? ValidationMessages.FileInUseForRead(path) : ValidationMessages.FileInUseForWrite(path),
                error.Exception.Message);
            Assert.Null(error.Exception.InnerException);
        }

        Assert.Equal(Csv, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 追記は、相手が書き込みの共有を許していても、書き込み用に開いている間は開けない（今までどおり。追記を交互に書いて壊さないため）
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 9,9")]
    [InlineData("'9,9' | Out-ProbedFile -LiteralPath '{0}' -Append")]
    [InlineData("[pscustomobject]@{{A='9';B='9'}} | Export-ProbedCsv -LiteralPath '{0}' -Append")]
    public void Append_WhileOpenForWrite_IsWriteFailed(string template)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("b.csv", Csv);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
                $"try {{ {string.Format(template, path)} }} catch {{ $_ }}");
            ErrorRecord error = result.Errors.Concat(result.Output.Select(o => o.BaseObject).OfType<ErrorRecord>()).First();

            Assert.Equal("WriteFailed", error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(ValidationMessages.FileInUseForWrite(path), error.Exception.Message);
        }

        Assert.Equal(Csv, File.ReadAllBytes(path));
    }
}
