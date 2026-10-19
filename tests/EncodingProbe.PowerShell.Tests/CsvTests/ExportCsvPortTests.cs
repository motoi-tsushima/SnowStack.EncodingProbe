using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal.Csv;

namespace EncodingProbe.PowerShell.Tests.CsvTests;

/// <summary>
/// PowerShell から移植した CSV の書き出し（<see cref="ExportCsvHelper"/>）を検証するテスト。
/// </summary>
/// <remarks>
/// 期待値は実測報告 M4 の PowerShell 7.6.7 の <c>ConvertTo-Csv</c> の結果
/// （<c>tools/CsvBehaviorResults/csv-measure-Core-7.6.7.txt</c>）を、測定と同じ形式で写したもの。
/// 測定はカルチャー ja-JP で行っているので、日付と数値の文字列化もカルチャー ja-JP で行う。
/// <br/>
/// 移植した処理を ConvertTo-Csv と同じ順序で呼ぶ部分（<see cref="ConvertToCsv"/>）はテスト側にある。
/// コマンドレットの殻は第三次で Export-ProbedCsv として作る。
/// </remarks>
public class ExportCsvPortTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string SampleObjectScript =
        "[pscustomobject]@{ Name = \"$([char]0x65E5)$([char]0x672C)$([char]0x8A9E)\"; Quote = 'say \"hi\"'; "
        + "Multi = \"l1`nl2\"; Comma = 'a,b'; Flag = $true; When = (New-Object DateTime 2026, 10, 15, 14, 30, 0); "
        + "Num = 1234.5; Empty = $null }";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ExportCsvPortTests(ProbedCommandRunspaceFixture fixture)
        => _fixture = fixture;

    /// <summary>
    /// M4 の既定（-UseQuotes Always）・AsNeeded・Never・-QuoteFields・-NoHeader で、
    /// 7.6.7 の ConvertTo-Csv とバイト単位（文字単位）で同じ行になること。
    /// </summary>
    [Theory]
    [InlineData("Always", null, false, new[]
    {
        "STR: \"Name\",\"Quote\",\"Multi\",\"Comma\",\"Flag\",\"When\",\"Num\",\"Empty\"",
        "STR: \"\\u65E5\\u672C\\u8A9E\",\"say \"\"hi\"\"\",\"l1\\nl2\",\"a,b\",\"True\",\"2026/10/15 14:30:00\",\"1234.5\",",
    })]
    [InlineData("AsNeeded", null, false, new[]
    {
        "STR: Name,Quote,Multi,Comma,Flag,When,Num,Empty",
        "STR: \\u65E5\\u672C\\u8A9E,\"say \"\"hi\"\"\",\"l1\\nl2\",\"a,b\",True,2026/10/15 14:30:00,1234.5,",
    })]
    [InlineData("Never", null, false, new[]
    {
        "STR: Name,Quote,Multi,Comma,Flag,When,Num,Empty",
        "STR: \\u65E5\\u672C\\u8A9E,say \"hi\",l1\\nl2,a,b,True,2026/10/15 14:30:00,1234.5,",
    })]
    [InlineData("Always", "Name,Num", false, new[]
    {
        "STR: \"Name\",Quote,Multi,Comma,Flag,When,\"Num\",Empty",
        "STR: \"\\u65E5\\u672C\\u8A9E\",say \"hi\",l1\\nl2,a,b,True,2026/10/15 14:30:00,\"1234.5\",",
    })]
    [InlineData("Always", null, true, new[]
    {
        "STR: \"\\u65E5\\u672C\\u8A9E\",\"say \"\"hi\"\"\",\"l1\\nl2\",\"a,b\",\"True\",\"2026/10/15 14:30:00\",\"1234.5\",",
    })]
    public void Convert_M4_SampleObject_MatchesConvertToCsv767(
        string quoteKind, string? quoteFields, bool noHeader, string[] expected)
    {
        Collection<PSObject> input = RunScript(SampleObjectScript);

        string[] actual = WithCulture("ja-JP", () => ConvertToCsv(
            input, ',', Enum.Parse<CsvQuoteKind>(quoteKind), quoteFields?.Split(','), noHeader));

        Assert.Equal(expected, Format(actual));
    }

    /// <summary>
    /// M4 の入力の種類による違い（辞書・辞書に NoteProperty を足したもの・混在・文字列・区切り文字・-UseCulture）。
    /// 5.1 の標準とは違い、7.6.7 と同じ列になること。
    /// </summary>
    [Theory]
    [InlineData("[ordered]@{ A = 1; B = 'x' }", ',', new[] { "STR: \"A\",\"B\"", "STR: \"1\",\"x\"" })]
    [InlineData(
        "$h = @{ A = 1 }; Add-Member -InputObject $h -NotePropertyName Extra -NotePropertyValue 'e' -PassThru",
        ',',
        new[] { "STR: \"A\",\"Extra\"", "STR: \"1\",\"e\"" })]
    [InlineData(
        "[pscustomobject]@{ A = 1; B = 2 }; [pscustomobject]@{ B = 3; C = 4 }",
        ',',
        new[] { "STR: \"A\",\"B\"", "STR: \"1\",\"2\"", "STR: ,\"3\"" })]
    [InlineData("'abc'; 'de'", ',', new[] { "STR: \"Length\"", "STR: \"3\"", "STR: \"2\"" })]
    [InlineData("[pscustomobject]@{ A = 'x;y'; B = 'z' }", ';', new[] { "STR: \"A\";\"B\"", "STR: \"x;y\";\"z\"" })]
    public void Convert_M4_InputKinds_MatchConvertToCsv767(string script, char delimiter, string[] expected)
    {
        Collection<PSObject> input = RunScript(script);

        string[] actual = WithCulture("ja-JP", () => ConvertToCsv(input, delimiter, CsvQuoteKind.Always, null, false));

        Assert.Equal(expected, Format(actual));
    }

    /// <summary>
    /// M4 の -UseCulture（ja-JP のリスト区切り文字は ','）。区切り文字の決定は移植元の SetDelimiter による。
    /// </summary>
    [Fact]
    public void Convert_M4_UseCulture_UsesListSeparator()
    {
        Collection<PSObject> input = RunScript("[pscustomobject]@{ A = 1.5; B = 'z' }");

        string[] actual = WithCulture("ja-JP", () =>
        {
            char delimiter = ImportExportCSVHelper.SetDelimiter("CulturePath", '\0', useCulture: true);
            return ConvertToCsv(input, delimiter, CsvQuoteKind.Always, null, false);
        });

        Assert.Equal(new[] { "STR: \"A\",\"B\"", "STR: \"1.5\",\"z\"" }, Format(actual));

        // 区切り文字のカルチャーは CurrentCulture による（de-DE のリスト区切り文字は ';'）
        Assert.Equal(';', WithCulture("de-DE", () => ImportExportCSVHelper.SetDelimiter("CultureLiteralPath", '\0', true)));
        Assert.Equal(',', ImportExportCSVHelper.SetDelimiter("DelimiterPath", '\0', false));
        Assert.Equal('\t', ImportExportCSVHelper.SetDelimiter("DelimiterLiteralPath", '\t', false));
    }

    /// <summary>
    /// -IncludeTypeInformation の #TYPE 行。型名が CSV: で始まる場合は除く（移植元の GetTypeString）。
    /// </summary>
    [Fact]
    public void Convert_IncludeTypeInformation_WritesTypeLine()
    {
        Collection<PSObject> input = RunScript("[pscustomobject]@{ A = 1 }");

        string[] actual = ConvertToCsv(input, ',', CsvQuoteKind.Always, null, false, includeTypeInformation: true);

        Assert.Equal(new[] { "#TYPE System.Management.Automation.PSCustomObject", "\"A\"", "\"1\"" }, actual);

        PSObject imported = new PSObject();
        imported.Properties.Add(new PSNoteProperty("A", "1"));
        imported.TypeNames.Clear();
        imported.TypeNames.Add("CSV:Foo.Bar");

        Assert.Equal("#TYPE Foo.Bar", ExportCsvHelper.GetTypeString(imported));
    }

    /// <summary>
    /// AsNeeded は、区切り文字・LF・" のいずれかを含む値だけ囲む。CR だけを含む値は囲まない（移植元のとおり）。
    /// </summary>
    [Fact]
    public void Convert_AsNeeded_DoesNotQuoteCarriageReturnOnly()
    {
        Collection<PSObject> input = RunScript("[pscustomobject]@{ A = \"x`ry\"; B = \"x`ny\"; C = 'p q' }");

        string[] actual = ConvertToCsv(input, ',', CsvQuoteKind.AsNeeded, null, false);

        Assert.Equal(new[] { "A,B,C", "x\ry,\"x\ny\",p q" }, actual);
    }

    /// <summary>
    /// 列の照合（移植元の ReconcilePreexistingPropertyNames）: 追記先の列の順序と表記で書き、
    /// 足りない列は -Force が無ければエラー、-Force なら空にする。余分なプロパティは書かない（実測報告 M5 a〜c、i）。
    /// </summary>
    [Fact]
    public void Reconcile_UsesPreexistingOrderAndRejectsMissingColumns()
    {
        IList<string> preexisting = new List<string> { "a", "b" };

        // a) 順序が違う、i) 大文字小文字が違う
        Assert.Equal(preexisting, ImportExportCSVHelper.ReconcilePreexistingPropertyNames(
            new List<string> { "B", "A" }, preexisting, force: false, "x.csv"));

        // c) 余分なプロパティ C は書かない
        Assert.Equal(preexisting, ImportExportCSVHelper.ReconcilePreexistingPropertyNames(
            new List<string> { "A", "B", "C" }, preexisting, force: false, "x.csv"));

        // b) 列 B が無い
        var exception = Assert.Throws<CsvAppendMismatchException>(() =>
            ImportExportCSVHelper.ReconcilePreexistingPropertyNames(
                new List<string> { "A" }, preexisting, force: false, "x.csv"));
        Assert.Equal("b", exception.PropertyName);
        Assert.Contains("x.csv", exception.Message);

        // b2) -Force なら列 B を空にする
        Collection<PSObject> input = RunScript("[pscustomobject]@{ A = 'a' }");
        IList<string> columns = ImportExportCSVHelper.ReconcilePreexistingPropertyNames(
            ExportCsvHelper.BuildPropertyNames(input[0], null), preexisting, force: true, "x.csv");

        using var helper = new ExportCsvHelper(',', CsvQuoteKind.Always, null);
        Assert.Equal("\"a\",", helper.ConvertPSObjectToCSV(input[0], columns));
    }

    public static TheoryData<string, bool> HostObjectCases => new()
    {
        // スクリプト, 値の行まで比べるか（値が時刻やカルチャーで変わるものは列だけを比べる）
        { "[pscustomobject]@{ A = 1; B = 'x'; C = $null }", true },
        { "Get-Item $env:windir\\notepad.exe | Select-Object Name, Length, Extension", true },
        { "Get-Process -Id $PID", false },
        { "Get-Item $env:windir\\notepad.exe", false },
        { "Get-Item $env:windir", false },
        { "Get-Date", false },
        { "$t = New-Object System.Data.DataTable; [void]$t.Columns.Add('Col1'); [void]$t.Columns.Add('Col2'); "
          + "$r = $t.NewRow(); $r.Col1 = 'a'; $r.Col2 = 'b'; $t.Rows.Add($r); $t.Rows[0]", true },
        { "$t = New-Object System.Data.DataTable; [void]$t.Columns.Add('Col1'); $r = $t.NewRow(); $r.Col1 = 'a'; "
          + "$t.Rows.Add($r); $t.DefaultView[0]", true },
        { "([xml]'<r><item a=\"1\" b=\"2\"><c>x</c></item></r>').r.item", true },
        { "Get-CimInstance Win32_OperatingSystem", false },
        { "[ordered]@{ A = 1; B = 'x' }", true },
        { "42", true },
        { "[pscustomobject]@{ A = 1 } | Add-Member -MemberType ScriptProperty -Name S -Value { 2 } -PassThru "
          + "| Add-Member -MemberType AliasProperty -Name AA -Value A -PassThru", true },
    };

    /// <summary>
    /// 指示書 3.4: 内部 API（PSObject.GetPropertyCollection など）を公開 API で置き換えた列の決定が、
    /// 実行中のホスト（テストでは PowerShell 7.6 の SDK）の ConvertTo-Csv と同じ列になること。
    /// .NET オブジェクト、types.ps1xml で拡張メンバーを持つ型、Select-Object の結果、
    /// .NET 以外のアダプターを持つオブジェクト（DataRowView・XML・CIM）を含む。
    /// </summary>
    [Theory]
    [MemberData(nameof(HostObjectCases))]
    public void Convert_HostObjects_SameColumnsAsHostConvertToCsv(string script, bool compareValues)
    {
        Collection<PSObject> input = RunScript(script);

        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = _fixture.Runspace;
        shell.AddCommand("ConvertTo-Csv");
        string[] host = shell.Invoke(input).Select(o => (string)o.BaseObject).ToArray();

        string[] actual = ConvertToCsv(input, ',', CsvQuoteKind.Always, null, false);

        // 列の無いオブジェクト（数値など）は、ヘッダーも値の行も出力しない
        Assert.Equal(host.FirstOrDefault(), actual.FirstOrDefault());

        if (compareValues)
        {
            Assert.Equal(host, actual);
        }
    }

    /// <summary>
    /// 移植元の ConvertToCsvCommand.ProcessRecord と同じ順序で移植した処理を呼ぶ。
    /// </summary>
    private static string[] ConvertToCsv(
        IEnumerable<PSObject> input,
        char delimiter,
        CsvQuoteKind quoteKind,
        string[]? quoteFields,
        bool noHeader,
        bool includeTypeInformation = false)
    {
        var lines = new List<string>();
        IList<string>? propertyNames = null;

        using var helper = new ExportCsvHelper(delimiter, quoteKind, quoteFields);

        foreach (PSObject inputObject in input)
        {
            if (propertyNames == null)
            {
                propertyNames = ExportCsvHelper.BuildPropertyNames(inputObject, null);

                if (!noHeader)
                {
                    if (includeTypeInformation)
                    {
                        lines.Add(ExportCsvHelper.GetTypeString(inputObject));
                    }

                    string properties = helper.ConvertPropertyNamesCSV(propertyNames);

                    if (!properties.Equals(string.Empty))
                    {
                        lines.Add(properties);
                    }
                }
            }

            string csv = helper.ConvertPSObjectToCSV(inputObject, propertyNames);

            if (csv != string.Empty)
            {
                lines.Add(csv);
            }
        }

        return lines.ToArray();
    }

    private Collection<PSObject> RunScript(string script)
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = _fixture.Runspace;
        shell.AddScript(script);

        return shell.Invoke();
    }

    private static T WithCulture<T>(string name, Func<T> action)
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static string[] Format(IEnumerable<string> lines)
        => lines.Select(CsvTestFormat.FormatString).ToArray();
}
