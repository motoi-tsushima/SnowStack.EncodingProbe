using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// BOM が 2 個以上続くファイルを読むとき、-Encoding の指定によらず、取り除く BOM は先頭の 1 個だけであることを検証する
/// （1.3.0 第六次。BUG-0021）。2 個目の U+FEFF は文字として残る（標準の 7.x の Get-Content・Import-Csv と同じ）。
/// </summary>
/// <remarks>
/// 1.1.0 から、BOM 付きの語彙（utf8BOM・unicodeBOM・裸の unicode など）を指定すると、StreamReader が 2 個目の BOM も読み飛ばしていた。
/// 追記先から判定した「BOM あり」の結果で読む経路（Export-ProbedCsv -Append のヘッダー）も同じだった。
/// </remarks>
public class DoubleBomReadTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string NoError = "NO-ERROR";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public DoubleBomReadTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>系統ごとの BOM と、その系統を読む -Encoding の指定</summary>
    public static IEnumerable<object[]> Cases()
    {
        var families = new (Encoding Encoding, string[] Names)[]
        {
            (new UTF8Encoding(true), new[] { "Auto", "utf8BOM", "utf8NoBOM", "utf-8", "65001" }),
            (new UnicodeEncoding(false, true), new[] { "Auto", "unicodeBOM", "unicodeNoBOM", "unicode", "utf-16", "1200" }),
            (new UnicodeEncoding(true, true), new[] { "Auto", "bigendianunicodeBOM", "bigendianunicodeNoBOM", "bigendianunicode", "utf-16BE" }),
            (new UTF32Encoding(false, true), new[] { "Auto", "utf32BOM", "utf32NoBOM", "utf32", "utf-32" }),
            (new UTF32Encoding(true, true), new[] { "Auto", "bigendianutf32BOM", "bigendianutf32NoBOM", "bigendianutf32", "utf-32BE" }),
        };

        foreach (var family in families)
        {
            foreach (string name in family.Names)
            {
                foreach (int boms in new[] { 2, 3 })
                {
                    yield return new object[] { family.Encoding.CodePage, name, boms };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GetProbedContent_RemovesOnlyFirstBom(int codePage, string encoding, int boms)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Build(codePage, boms, "A,B\r\n1,2\r\n"));
        string extra = new string('﻿', boms - 1);

        Assert.Equal(extra + "A,B|1,2", Run($"(Get-ProbedContent -LiteralPath '{path}' -Encoding {encoding}) -join '|'"));
        Assert.Equal(extra + "A,B\r\n1,2\r\n", Run($"Get-ProbedContent -LiteralPath '{path}' -Encoding {encoding} -Raw"));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ImportProbedCsv_ColumnNameKeepsSecondBom(int codePage, string encoding, int boms)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.csv", Build(codePage, boms, "A,B\r\n1,2\r\n"));
        string extra = new string('﻿', boms - 1);

        Assert.Equal(
            extra + "A",
            Run($"(Import-ProbedCsv -LiteralPath '{path}' -Encoding {encoding}).PSObject.Properties.Name[0]"));
        Assert.Equal(
            extra + "A",
            Run($"(Import-ProbedCsv -LiteralPath '{path}' -Encoding {encoding} -DetectionLineCount 1).PSObject.Properties.Name[0]"));
    }

    /// <summary>
    /// Export-ProbedCsv -Append は、追記先のヘッダーの最初の列名を U+FEFF 付きで読むので、
    /// プロパティ A とは照合が合わない（7.x の Export-Csv -Append と同じ）。-Force なら、追記先の列名で書く。ファイルは変わらない。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("utf8BOM")]
    [InlineData("utf8NoBOM")]
    public void ExportProbedCsvAppend_HeaderKeepsSecondBom(string? encoding)
    {
        using var directory = new TemporaryDirectory();
        byte[] original = Build(65001, 2, "A,B\r\n1,2\r\n");
        string path = directory.Write("a.csv", original);
        string option = encoding == null ? string.Empty : $"-Encoding {encoding}";

        Assert.Equal(
            "CannotAppendCsvWithMismatchedPropertyNames",
            Run($"[pscustomobject]@{{A='x';B='y'}} | Export-ProbedCsv -LiteralPath '{path}' -Append {option}; '{NoError}'"));
        Assert.Equal(original, File.ReadAllBytes(path));

        Assert.Equal(
            NoError,
            Run($"[pscustomobject]@{{([string][char]0xFEFF + 'A')='x';B='y'}} | Export-ProbedCsv -LiteralPath '{path}' -Append {option}; '{NoError}'"));
        Assert.Equal(original.Concat(Encoding.ASCII.GetBytes("\"x\",\"y\"\r\n")), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Raw で読んで -EncodingFrom -NoNewline で書き戻すと、元のバイト列と一致すること。
    /// </summary>
    [Theory]
    [InlineData(65001, "utf8BOM")]
    [InlineData(65001, "Auto")]
    [InlineData(1200, "unicodeBOM")]
    [InlineData(12001, "bigendianutf32BOM")]
    public void RawRoundTrip_IsByteExact(int codePage, string encoding)
    {
        using var directory = new TemporaryDirectory();
        byte[] original = Build(codePage, 2, "A\r\n");
        string path = directory.Write("a.txt", original);
        string output = directory.Combine("b.txt");

        Assert.Equal(
            NoError,
            Run($"$t = Get-ProbedContent -LiteralPath '{path}' -Raw -Encoding {encoding}; Set-ProbedContent -LiteralPath '{output}' -Value $t -NoNewline -EncodingFrom '{path}'; '{NoError}'"));
        Assert.Equal(original, File.ReadAllBytes(output));
    }

    private static byte[] Build(int codePage, int boms, string text)
    {
        Encoding encoding = codePage switch
        {
            65001 => new UTF8Encoding(true),
            1200 => new UnicodeEncoding(false, true),
            1201 => new UnicodeEncoding(true, true),
            12000 => new UTF32Encoding(false, true),
            _ => new UTF32Encoding(true, true),
        };

        byte[] preamble = encoding.GetPreamble();
        return Enumerable.Repeat(preamble, boms).SelectMany(b => b).Concat(encoding.GetBytes(text)).ToArray();
    }

    private string Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ {script} }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        Assert.Empty(result.Errors);
        return result.Output.Last().ToString();
    }
}
