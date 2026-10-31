using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// CSV の列名を比べるすべての箇所が、.NET 10 の OrdinalIgnoreCase と同じ規則で比べることを検証する（1.3.0 第六次。BUG-0020）。
/// </summary>
/// <remarks>
/// 列名を比べる箇所: Import-ProbedCsv の列名（-Header を含む）の重複の検査、Export-ProbedCsv -Append の列の照合と
/// 照合した列の値の取り出し、Export-ProbedCsv / ConvertTo の -QuoteFields。
/// このテストは net10.0 で動く（7.x の挙動）。5.1 で同じ結果になることは PSCompat（Sixth/CsvName/*）が確かめる。
/// </remarks>
public class CsvColumnNameComparisonTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string NoError = "NO-ERROR";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public CsvColumnNameComparisonTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>ホストによって結果が違っていた組（BUG-0020）と、対照の組</summary>
    public static TheoryData<int, int> SameColumnPairs() => new()
    {
        { 0x00C4, 0x00E4 },
        { 0x01C5, 0x01C6 },
        { 0x01C5, 0x01C4 },
        { 0x03C2, 0x03A3 },
        { 0x10400, 0x10428 },
    };

    /// <summary>標準とモジュールのすべてが「別の列」とした組（テスト担当の 20 組の一部）</summary>
    public static TheoryData<int, int> DifferentColumnPairs() => new()
    {
        { 0x00DF, 0x0053 },
        { 0x0130, 0x0069 },
        { 0x212A, 0x006B },
        { 0xFF21, 0x0041 },
    };

    [Theory]
    [MemberData(nameof(SameColumnPairs))]
    public void Import_SameColumns_AreDuplicate(int first, int second)
    {
        using var directory = new TemporaryDirectory();
        string path = WriteCsv(directory, $"{S(first)},{S(second)}\r\n1,2\r\n");

        Assert.Equal("AlreadyPresentPSMemberInfoInternalCollectionAdd", Run($"Import-ProbedCsv -LiteralPath '{path}' -ErrorAction Stop"));
        Assert.Equal(
            "AlreadyPresentPSMemberInfoInternalCollectionAdd",
            Run($"Import-ProbedCsv -LiteralPath '{path}' -Header '{S(first)}', '{S(second)}' -ErrorAction Stop"));
    }

    [Theory]
    [MemberData(nameof(DifferentColumnPairs))]
    public void Import_DifferentColumns_AreNotDuplicate(int first, int second)
    {
        using var directory = new TemporaryDirectory();
        string path = WriteCsv(directory, $"{S(first)},{S(second)}\r\n1,2\r\n");

        Assert.Equal("1 2", Value($"$o = Import-ProbedCsv -LiteralPath '{path}'; '{{0}} {{1}}' -f $o.PSObject.Properties.Value[0], $o.PSObject.Properties.Value[1]"));
    }

    /// <summary>
    /// 追記先の列名とプロパティ名が同じ列なら照合が通り、その列の値を書く（5.1 では照合が通った後も値を空で書いていた）。
    /// </summary>
    [Theory]
    [MemberData(nameof(SameColumnPairs))]
    public void ExportAppend_SameColumn_MatchesAndWritesValue(int header, int property)
    {
        using var directory = new TemporaryDirectory();
        string path = WriteCsv(directory, $"{S(header)},Z\r\n1,2\r\n");

        Assert.Equal(NoError, Run($"$o = New-Object psobject; $o | Add-Member NoteProperty '{S(property)}' 'x'; $o | Add-Member NoteProperty Z 'y'; $o | Export-ProbedCsv -LiteralPath '{path}' -Append"));
        Assert.Equal($"{S(header)},Z\r\n1,2\r\n\"x\",\"y\"\r\n", File.ReadAllText(path));
    }

    [Theory]
    [MemberData(nameof(DifferentColumnPairs))]
    public void ExportAppend_DifferentColumn_IsMismatch(int header, int property)
    {
        using var directory = new TemporaryDirectory();
        string original = $"{S(header)},Z\r\n1,2\r\n";
        string path = WriteCsv(directory, original);

        Assert.Equal(
            "CannotAppendCsvWithMismatchedPropertyNames",
            Run($"$o = New-Object psobject; $o | Add-Member NoteProperty '{S(property)}' 'x'; $o | Add-Member NoteProperty Z 'y'; $o | Export-ProbedCsv -LiteralPath '{path}' -Append"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    /// <summary>
    /// -QuoteFields の名前もプロパティ名と同じ規則で比べる。
    /// </summary>
    [Theory]
    [MemberData(nameof(SameColumnPairs))]
    public void QuoteFields_SameColumn_IsQuoted(int quoteField, int property)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("q.csv");

        Assert.Equal(NoError, Run($"$o = New-Object psobject; $o | Add-Member NoteProperty '{S(property)}' 'x'; $o | Add-Member NoteProperty Z 'y'; $o | Export-ProbedCsv -LiteralPath '{path}' -QuoteFields '{S(quoteField)}'"));
        Assert.Equal($"\"{S(property)}\",Z\r\n\"x\",y\r\n", File.ReadAllText(path));
    }

    private static string S(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string WriteCsv(TemporaryDirectory directory, string text)
        => directory.Write("a.csv", new UTF8Encoding(false).GetBytes(text));

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
