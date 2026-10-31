using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// メッセージの細部（1.3.0 第六次 C。BUG-0045）。
/// </summary>
/// <remarks>
/// (a) 列名の重複のメッセージにパスと、重複したのがファイルのヘッダーか -Header の値かを書く（ID・分類・対象は今までと同じ）。
/// (b) EncodingChangeOnAppend は文字エンコーディングを統一語彙名で示す。
/// (c) 中国語の DetectionVerificationFailed の語順。(d) 英語の「first 1 lines」を、行数によらない言い方にする。
/// </remarks>
public class MessageDetailTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string DuplicateId = "AlreadyPresentPSMemberInfoInternalCollectionAdd";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public MessageDetailTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>
    /// (a) ファイルのヘッダーの重複は、そのファイルのパスと列名を示す。2 件目以降の重複でなく最初の重複を示す
    /// </summary>
    [Fact]
    public void Import_DuplicateInFile_NamesPathAndColumn()
    {
        using var directory = new TemporaryDirectory();
        string good = directory.Write("good.csv", Encoding.ASCII.GetBytes("X\r\n9\r\n"));
        string dup = directory.Write("dup.csv", Encoding.ASCII.GetBytes("A,b,a,B\r\n1,2,3,4\r\n"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors($"Import-ProbedCsv -LiteralPath '{good}', '{dup}'");

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal(DuplicateId, error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ErrorCategory.NotSpecified, error.CategoryInfo.Category);
        Assert.Equal(dup, error.TargetObject);
        Assert.IsType<ExtendedTypeSystemException>(error.Exception);
        Assert.Equal(ValidationMessages.CsvHeaderAlreadyPresentInFile(dup, "a"), error.Exception.Message);
        Assert.Single(result.Output);
    }

    /// <summary>
    /// (a) -Header の値の重複は、ファイルのヘッダーではなく -Header だと示す（ファイルごとに 1 件。対象はそれぞれのパス）
    /// </summary>
    [Fact]
    public void Import_DuplicateInHeaderParameter_SaysHeaderParameter()
    {
        using var directory = new TemporaryDirectory();
        string first = directory.Write("1.csv", Encoding.ASCII.GetBytes("1,2\r\n"));
        string second = directory.Write("2.csv", Encoding.ASCII.GetBytes("3,4\r\n"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Import-ProbedCsv -LiteralPath '{first}', '{second}' -Header 'Name', 'NAME'");

        Assert.Empty(result.Output);
        Assert.Equal(2, result.Errors.Count);

        foreach ((ErrorRecord error, string path) in result.Errors.Zip(new[] { first, second }))
        {
            Assert.Equal(DuplicateId, error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(ErrorCategory.NotSpecified, error.CategoryInfo.Category);
            Assert.Equal(path, error.TargetObject);
            Assert.IsType<ExtendedTypeSystemException>(error.Exception);
            Assert.Equal(ValidationMessages.CsvHeaderAlreadyPresentInParameter(path, "NAME"), error.Exception.Message);
        }
    }

    /// <summary>
    /// (a) Export-ProbedCsv -Append の追記先のヘッダーの重複も、パスを示す（終了エラー。ID・分類・対象は今までと同じ。ファイルは変えない）
    /// </summary>
    [Fact]
    public void ExportAppend_DuplicateInTarget_NamesPath()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = Encoding.ASCII.GetBytes("A,a\r\n1,2\r\n");
        string path = directory.Write("t.csv", original);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ [pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{path}' -Append }} catch {{ $_ }}");
        var error = (ErrorRecord)Assert.Single(result.Output).BaseObject;

        Assert.Equal(DuplicateId, error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ErrorCategory.NotSpecified, error.CategoryInfo.Category);
        Assert.Equal(path, error.TargetObject);
        Assert.Equal(ValidationMessages.CsvHeaderAlreadyPresentInFile(path, "a"), error.Exception.Message);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// (b) EncodingChangeOnAppend は、指定した文字エンコーディングと追記先の文字エンコーディングを統一語彙名で示す（3 つの追記のコマンド）
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'あ' -Encoding {1}")]
    [InlineData("'あ' | Out-ProbedFile -LiteralPath '{0}' -Append -Encoding {1}")]
    [InlineData("[pscustomobject]@{{ A = 'あ' }} | Export-ProbedCsv -LiteralPath '{0}' -Append -Encoding {1}")]
    public void EncodingChangeOnAppend_UsesUnifiedNames(string template)
    {
        foreach ((byte[] content, string specified, string existing) in new[]
        {
            (new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0D, 0x00, 0x0A, 0x00 }, "utf8NoBOM", "unicodeBOM"),
            (new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x0D, 0x0A }, "shift_jis", "utf8BOM"),
            (new byte[] { 0x41, 0x0D, 0x0A, 0x82, 0xA0, 0x0D, 0x0A }, "utf8BOM", "shift_jis"),
        })
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("t.csv", content);

            InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
                "try { " + string.Format(CultureInfo.InvariantCulture, template, path, specified) + " -Culture ja-JP -ErrorAction Stop } catch { $_ }");
            var error = result.Output.Select(o => o.BaseObject).OfType<ErrorRecord>().Single();

            Assert.Equal("EncodingChangeOnAppend", error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(ValidationMessages.EncodingChangeOnAppend(path, specified, existing), error.Exception.Message);
            Assert.Equal(content, File.ReadAllBytes(path));
        }
    }

    /// <summary>
    /// (b) BOM の方針が決まっていない名前（追記の BOM は使わないので、裸の名前も指定できる）は WebName で示す
    /// </summary>
    [Fact]
    public void DescribeForMessage_BomUnspecifiedUnicode_IsWebName()
    {
        Assert.Equal("utf-16", AppendConsistency.DescribeForMessage(EncodingSpec.Create(new UnicodeEncoding(false, false), emitBom: null)));
        Assert.Equal("unicodeNoBOM", AppendConsistency.DescribeForMessage(EncodingSpec.Create(new UnicodeEncoding(false, false), emitBom: false)));
        Assert.Equal("shift_jis", AppendConsistency.DescribeForMessage(EncodingSpec.Create(Encoding.GetEncoding(932), emitBom: null)));
    }

    /// <summary>
    /// (d) 英語のメッセージは、行数が 1 でも単数・複数が崩れない（行数によらない言い方にした）。先頭だけで判定したことは残す
    /// </summary>
    [Fact]
    public void English_DetectionLineCountMessages_DoNotDependOnPlural()
    {
        CultureInfo saved = Thread.CurrentThread.CurrentUICulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        try
        {
            foreach (string message in new[]
            {
                ValidationMessages.DetectionLinesTooLarge("a.csv", 1),
                ValidationMessages.DetectionVerificationFailed("a.csv", "utf8NoBOM", 1, 10, "0x82"),
                ValidationMessages.DetectionVerificationIso2022Escape("a.csv", "utf8NoBOM", 1, 10, "ESC $ B"),
            })
            {
                Assert.DoesNotContain("1 lines", message);
                Assert.Contains("the beginning of the file (with -DetectionLineCount 1)", message);
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = saved;
        }
    }
}
