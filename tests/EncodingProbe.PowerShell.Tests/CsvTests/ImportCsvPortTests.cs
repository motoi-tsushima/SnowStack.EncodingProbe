using System.IO;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell.Internal.Csv;

namespace EncodingProbe.PowerShell.Tests.CsvTests;

/// <summary>
/// PowerShell から移植した CSV の解析（<see cref="ImportCsvHelper"/>）を検証するテスト。
/// </summary>
/// <remarks>
/// 入力は実測報告 M1 の 15 ケース（<c>tools/Measure-CsvBehavior.ps1</c> の <c>$m1</c>）と同じ文字列である。
/// 期待値は PowerShell 7.6.7 の <c>Import-Csv</c> の結果（<c>tools/CsvBehaviorResults/csv-measure-Core-7.6.7.txt</c>）を、
/// 測定と同じ形式で写したもの（本モジュールの出力を期待値にしない）。
/// <br/>
/// Windows PowerShell 5.1 でも同じ結果になることは、コマンドレットを追加する第二次の PSCompat で確かめる
/// （このテストプロジェクトは net10.0 だけのため）。
/// </remarks>
public class ImportCsvPortTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string Jp1 = "日本語";
    private const string Jp2 = "追記";
    private const string PSCustomObjectType = "OBJ<System.Management.Automation.PSCustomObject>";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ImportCsvPortTests(ProbedCommandRunspaceFixture fixture)
        => _fixture = fixture;

    /// <summary>
    /// 実測報告 M1 の入力（c10 の列名の重複を除く 14 ケース）と、7.6.7 の Import-Csv の結果。
    /// </summary>
    public static TheoryData<string, string, string[]> M1Cases => new()
    {
        {
            "c01 quoted CRLF inside field",
            "Name,Memo\r\n\"a\",\"line1\r\nline2\"\r\n\"b\",\"x\"\r\n",
            new[]
            {
                PSCustomObjectType + " {Name=[a] | Memo=[line1\\r\\nline2]}",
                PSCustomObjectType + " {Name=[b] | Memo=[x]}",
            }
        },
        {
            "c02 quoted LF inside CRLF file",
            "Name,Memo\r\n\"a\",\"line1\nline2\"\r\n",
            new[] { PSCustomObjectType + " {Name=[a] | Memo=[line1\\nline2]}" }
        },
        {
            "c03 data row starting with #",
            "Name,Memo\r\n#1,abc\r\n2,def\r\n",
            new[]
            {
                PSCustomObjectType + " {Name=[#1] | Memo=[abc]}",
                PSCustomObjectType + " {Name=[2] | Memo=[def]}",
            }
        },
        {
            "c04 #TYPE line",
            "#TYPE Foo.Bar\r\nName,Memo\r\na,b\r\n",
            new[] { "OBJ<Foo.Bar> {Name=[a] | Memo=[b]}" }
        },
        {
            "c05 comment line before header",
            "# comment\r\nName,Memo\r\na,b\r\n",
            new[] { PSCustomObjectType + " {Name=[a] | Memo=[b]}" }
        },
        {
            "c06 W3C #Fields",
            "#Software: x\r\n#Fields: date time\r\n2026 10\r\n",
            new[] { PSCustomObjectType + " {date time=[2026 10]}" }
        },
        {
            "c07 blank lines",
            "Name,Memo\r\n\r\na,b\r\n\r\n",
            new[] { PSCustomObjectType + " {Name=[a] | Memo=[b]}" }
        },
        {
            "c08 spaces around values",
            "Name,Memo\r\n  a , \"b\"  \r\n",
            new[] { PSCustomObjectType + " {Name=[a ] | Memo=[b]}" }
        },
        {
            "c09 ragged rows, no final newline",
            "A,B\r\n1,2,3\r\n4\r\n5,6",
            new[]
            {
                PSCustomObjectType + " {A=[1] | B=[2]}",
                PSCustomObjectType + " {A=[4] | B=[<null>]}",
                PSCustomObjectType + " {A=[5] | B=[6]}",
            }
        },
        {
            "c11 empty header name",
            "A,,C\r\n1,2,3\r\n",
            new[] { PSCustomObjectType + " {A=[1] | H1=[2] | C=[3]}" }
        },
        {
            "c12 CR only line breaks",
            "A,B\r1,2\r3,4\r",
            new[]
            {
                PSCustomObjectType + " {A=[1] | B=[2]}",
                PSCustomObjectType + " {A=[3] | B=[4]}",
            }
        },
        {
            "c13 Japanese values (UTF-8)",
            "A,B\r\n" + Jp1 + "," + Jp2 + "\r\n",
            new[] { PSCustomObjectType + " {A=[\\u65E5\\u672C\\u8A9E] | B=[\\u8FFD\\u8A18]}" }
        },
        {
            "c14 trailing delimiter in header",
            "A,B,\r\n1,2,\r\n",
            new[] { PSCustomObjectType + " {A=[1] | B=[2]}" }
        },
        {
            "c15 escaped quotes",
            "A,B\r\n\"he said \"\"hi\"\"\",x\r\n",
            new[] { PSCustomObjectType + " {A=[he said \"hi\"] | B=[x]}" }
        },
    };

    /// <summary>
    /// M1 の各ケースで、7.6.7 の Import-Csv と同じオブジェクト（列・値・型名）になること。
    /// 入力は文字列からと、UTF-8 のバイト列のストリームから（ファイルと同じ読み方）の両方で確かめる。
    /// </summary>
    [Theory]
    [MemberData(nameof(M1Cases))]
    public void Import_M1_MatchesImportCsv767(string caseName, string input, string[] expected)
    {
        Assert.NotNull(caseName);

        Assert.Equal(expected, Format(Import(new StringReader(input), out _)));

        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(input));
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        Assert.Equal(expected, Format(Import(reader, out _)));
    }

    /// <summary>
    /// c04: 型名は <c>Foo.Bar</c>、<c>CSV:Foo.Bar</c> の順（7.x と同じ。5.1 とは逆）。
    /// </summary>
    [Fact]
    public void Import_M1_C04_TypeNamesOrderIsSameAsPowerShell7()
    {
        PSObject result = Assert.Single(Import(new StringReader("#TYPE Foo.Bar\r\nName,Memo\r\na,b\r\n"), out _));

        Assert.Equal(new[] { "Foo.Bar", "CSV:Foo.Bar" }, result.TypeNames);
    }

    /// <summary>
    /// c10: 列名の重複（大文字小文字を区別しない）は、1 件も出力する前に ExtendedTypeSystemException になること
    /// （コマンドがこれを ID <c>AlreadyPresentPSMemberInfoInternalCollectionAdd</c> のエラーにする）。
    /// </summary>
    [Theory]
    [InlineData("A,A\r\n1,2\r\n")]
    [InlineData("A,a\r\n1,2\r\n")]
    public void Import_M1_C10_DuplicateHeaderThrowsBeforeOutput(string input)
    {
        var output = new List<PSObject>();
        var helper = new ImportCsvHelper(output.Add, _ => { }, ',', null, null, new StringReader(input));
        bool warned = false;

        var exception = Assert.Throws<ExtendedTypeSystemException>(() => helper.Import(ref warned));

        Assert.Empty(output);
        Assert.Contains("\"A\"", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// c11: 列名が空のときの警告は、複数のファイル（ImportCsvHelper の複数のインスタンス）にまたがって 1 回だけ出ること。
    /// </summary>
    [Fact]
    public void Import_M1_C11_WarnsOnceAcrossFiles()
    {
        var warnings = new List<string>();
        var output = new List<PSObject>();
        bool alreadyWarned = false;

        foreach (string input in new[] { "A,,C\r\n1,2,3\r\n4,5,6\r\n", "X,\r\n1,2\r\n" })
        {
            var helper = new ImportCsvHelper(output.Add, warnings.Add, ',', null, null, new StringReader(input));
            helper.Import(ref alreadyWarned);
        }

        Assert.Single(warnings);
        Assert.True(alreadyWarned);
        Assert.Equal(3, output.Count);
    }

    /// <summary>
    /// -Header を指定した場合は、1 行目もデータとして読むこと。指定した列名にも重複の検査が効くこと。
    /// </summary>
    [Fact]
    public void Import_WithHeader_ReadsFirstLineAsData()
    {
        var output = new List<PSObject>();
        var helper = new ImportCsvHelper(output.Add, _ => { }, ',', new[] { "X", "Y" }, null, new StringReader("A,B\r\n1,2\r\n"));
        bool warned = false;

        helper.Import(ref warned);

        Assert.Equal(
            new[] { PSCustomObjectType + " {X=[A] | Y=[B]}", PSCustomObjectType + " {X=[1] | Y=[2]}" },
            Format(output));

        var duplicate = new ImportCsvHelper(output.Add, _ => { }, ',', new[] { "X", "x" }, null, new StringReader("1,2\r\n"));
        Assert.Throws<ExtendedTypeSystemException>(() => duplicate.Import(ref warned));
    }

    /// <summary>
    /// 区切り文字を変えても、移植元と同じ規則で解析すること（-Delimiter）。
    /// </summary>
    [Fact]
    public void Import_WithSemicolonDelimiter()
    {
        var output = new List<PSObject>();
        var helper = new ImportCsvHelper(output.Add, _ => { }, ';', null, null, new StringReader("A;B\r\n\"x;y\";z\r\n"));
        bool warned = false;

        helper.Import(ref warned);

        Assert.Equal(new[] { PSCustomObjectType + " {A=[x;y] | B=[z]}" }, Format(output));
    }

    /// <summary>
    /// ReadHeader だけを呼んだ場合（Export -Append の追記先のヘッダーの読み取り）に、列名が得られること。
    /// </summary>
    [Theory]
    [InlineData("\"A\",\"B\"\r\n\"1\",\"2\"\r\n", new[] { "A", "B" })]
    [InlineData("#TYPE Foo\r\n# comment\r\na,b\r\n", new[] { "a", "b" })]
    [InlineData("A,B,\r\n", new[] { "A", "B" })]
    public void ReadHeader_ReturnsColumnNames(string input, string[] expected)
    {
        var helper = new ImportCsvHelper(_ => { }, _ => { }, ',', null, null, new StringReader(input));

        helper.ReadHeader();

        Assert.Equal(expected, helper.Header);
    }

    /// <summary>
    /// 実行中のホスト（テストでは PowerShell 7.6 の SDK）の ConvertFrom-Csv に全体を 1 個の文字列で渡した結果と、
    /// M1 の全ケースで一致すること（実測報告 2.1: Import-Csv と同じ解析になる）。
    /// </summary>
    [Theory]
    [MemberData(nameof(M1Cases))]
    public void Import_M1_MatchesHostConvertFromCsv(string caseName, string input, string[] expected)
    {
        Assert.NotNull(caseName);
        Assert.NotNull(expected);

        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = _fixture.Runspace;
        shell.AddCommand("ConvertFrom-Csv").AddParameter("InputObject", input);

        var host = shell.Invoke();

        Assert.Equal(Format(host), Format(Import(new StringReader(input), out _)));
    }

    private static List<PSObject> Import(TextReader reader, out List<string> warnings)
    {
        var output = new List<PSObject>();
        var collected = new List<string>();
        var helper = new ImportCsvHelper(output.Add, collected.Add, ',', null, null, reader);
        bool warned = false;

        helper.Import(ref warned);

        warnings = collected;
        return output;
    }

    private static string[] Format(IEnumerable<PSObject> objects)
        => objects.Select(CsvTestFormat.FormatObject).ToArray();
}
