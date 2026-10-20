using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.CsvTests;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Import-ProbedCsv（1.3.0 仕様書 第 3 部）を PowerShell ランスペース経由で検証するテスト。
/// </summary>
/// <remarks>
/// 解析の期待値は、実測報告 M1 の 7.6.7 の Import-Csv の結果（<see cref="ImportCsvPortTests.M1Cases"/>）を使う。
/// テストデータはバイト列を明示して作る。
/// </remarks>
public class ImportProbedCsvTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string CommandName = "Import-ProbedCsv";
    private const string PSCustomObjectType = "OBJ<System.Management.Automation.PSCustomObject>";

    /// <summary>「日本,語」の Shift_JIS</summary>
    private static readonly byte[] JapaneseRowShiftJis = { 0x93, 0xFA, 0x96, 0x7B, 0x2C, 0x8C, 0xEA, 0x0D, 0x0A };

    /// <summary>「日本,語」の EUC-JP</summary>
    private static readonly byte[] JapaneseRowEucJp = { 0xC6, 0xFC, 0xCB, 0xDC, 0x2C, 0xB8, 0xEC, 0x0D, 0x0A };

    private const string AsciiHead = "Name,Memo\r\na,b\r\n";
    private const string JapaneseRow = "日本,語\r\n";

    private static readonly string[] AsciiHeadThenJapanese =
    {
        PSCustomObjectType + " {Name=[a] | Memo=[b]}",
        PSCustomObjectType + " {Name=[\\u65E5\\u672C] | Memo=[\\u8A9E]}",
    };

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ImportProbedCsvTests(ProbedCommandRunspaceFixture fixture)
        => this._fixture = fixture;

    // ─── 解析（移植。仕様書 11 章） ─────────────────────────────────────────

    /// <summary>
    /// 実測報告 M1 のケースを UTF-8 のファイルにして読むと、7.6.7 の Import-Csv と同じ結果になること。
    /// </summary>
    [Theory]
    [MemberData(nameof(ImportCsvPortTests.M1Cases), MemberType = typeof(ImportCsvPortTests))]
    public void Import_M1_MatchesImportCsv767(string caseName, string input, string[] expected)
    {
        Assert.NotNull(caseName);
        using var file = ByteExactFile.Create(new UTF8Encoding(false).GetBytes(input));

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { file.Path } });

        Assert.Empty(result.Errors);
        Assert.Equal(expected, Format(result.Output));
    }

    /// <summary>
    /// c10: 列名の重複は、そのファイルについて非終了エラー（ID は標準と同じ）とし、1 件も出力せずに次のファイルへ進むこと（24 章 U3）。
    /// </summary>
    [Fact]
    public void Import_M1_C10_DuplicateHeader_NonTerminatingAndContinues()
    {
        using var duplicate = ByteExactFile.Create(Encoding.ASCII.GetBytes("A,a\r\n1,2\r\n"));
        using var good = ByteExactFile.Create(Encoding.ASCII.GetBytes("X\r\n9\r\n"));

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { duplicate.Path, good.Path } });

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("AlreadyPresentPSMemberInfoInternalCollectionAdd", ErrorId(error));
        Assert.Equal(ErrorCategory.NotSpecified, error.CategoryInfo.Category);
        Assert.Equal(duplicate.Path, error.TargetObject);
        Assert.IsType<ExtendedTypeSystemException>(error.Exception);
        Assert.Equal(new[] { PSCustomObjectType + " {X=[9]}" }, Format(result.Output));
    }

    /// <summary>
    /// c11: 列名が空のときの警告は、複数のファイルにまたがって実行全体で 1 回だけ出ること（仕様書 9.2）。
    /// パイプラインから 1 件ずつ渡した場合（ProcessRecord が複数回呼ばれる）も 1 回だけであること。
    /// </summary>
    [Fact]
    public void Import_UnspecifiedHeaderWarning_OnceForWholeRun()
    {
        using var first = ByteExactFile.Create(Encoding.ASCII.GetBytes("A,,C\r\n1,2,3\r\n"));
        using var second = ByteExactFile.Create(Encoding.ASCII.GetBytes(",B\r\n4,5\r\n"));

        InvocationResult byArray = Invoke(new() { ["LiteralPath"] = new[] { first.Path, second.Path } });
        Assert.Single(byArray.Warnings);
        Assert.Equal(2, byArray.Output.Count);

        InvocationResult byPipeline = this._fixture.InvokeScriptCapturingErrors(
            $"'{first.Path}', '{second.Path}' | Import-ProbedCsv");
        Assert.Single(byPipeline.Warnings);
        Assert.Equal(
            new[] { PSCustomObjectType + " {A=[1] | H1=[2] | C=[3]}", PSCustomObjectType + " {H1=[4] | B=[5]}" },
            Format(byPipeline.Output));
    }

    /// <summary>
    /// c04 / c06 / c12: 型名の順序（7.x）、W3C 形式、CR だけの改行。5.1 の標準とは違う結果が正しい。
    /// </summary>
    [Fact]
    public void Import_TypeNamesW3cAndCarriageReturnOnly_FollowPowerShell7()
    {
        using var typed = ByteExactFile.Create(Encoding.ASCII.GetBytes("#TYPE Foo.Bar\r\nName,Memo\r\na,b\r\n"));
        using var crOnly = ByteExactFile.Create(Encoding.ASCII.GetBytes("A,B\r1,2\r3,4\r"));

        PSObject result = Assert.Single(Invoke(new() { ["LiteralPath"] = new[] { typed.Path } }).Output);
        Assert.Equal(new[] { "Foo.Bar", "CSV:Foo.Bar" }, result.TypeNames);

        Assert.Equal(2, Invoke(new() { ["LiteralPath"] = new[] { crOnly.Path } }).Output.Count);
    }

    /// <summary>
    /// -Header を指定すると 1 行目もデータとして読み、すべてのファイルに同じ列名を使うこと。
    /// ファイルごとに列が違ってもよいこと（-Header なし）。
    /// </summary>
    [Fact]
    public void Import_HeaderAndPerFileColumns()
    {
        using var first = ByteExactFile.Create(Encoding.ASCII.GetBytes("A,B\r\n1,2\r\n"));
        using var second = ByteExactFile.Create(Encoding.ASCII.GetBytes("C\r\n3\r\n"));

        Assert.Equal(
            new[]
            {
                PSCustomObjectType + " {X=[A] | Y=[B]}",
                PSCustomObjectType + " {X=[1] | Y=[2]}",
                PSCustomObjectType + " {X=[C] | Y=[<null>]}",
                PSCustomObjectType + " {X=[3] | Y=[<null>]}",
            },
            Format(Invoke(new() { ["LiteralPath"] = new[] { first.Path, second.Path }, ["Header"] = new[] { "X", "Y" } }).Output));

        Assert.Equal(
            new[] { PSCustomObjectType + " {A=[1] | B=[2]}", PSCustomObjectType + " {C=[3]}" },
            Format(Invoke(new() { ["LiteralPath"] = new[] { first.Path, second.Path } }).Output));
    }

    /// <summary>
    /// -Delimiter と -UseCulture（区切り文字。判定の -Culture とは別）。
    /// </summary>
    [Fact]
    public void Import_DelimiterAndUseCulture()
    {
        using var semicolon = ByteExactFile.Create(Encoding.ASCII.GetBytes("A;B\r\n\"x;y\";z\r\n"));
        using var comma = ByteExactFile.Create(Encoding.ASCII.GetBytes("A,B\r\n1,2\r\n"));

        Assert.Equal(
            new[] { PSCustomObjectType + " {A=[x;y] | B=[z]}" },
            Format(this._fixture.InvokeScriptCapturingErrors($"Import-ProbedCsv '{semicolon.Path}' ';'").Output));

        // -UseCulture は区切り文字に CurrentCulture のリスト区切り文字を使う。-Culture（判定のカルチャー）と同時に指定できる
        string script =
            "$c = [System.Threading.Thread]::CurrentThread.CurrentCulture; "
            + "try { [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('de-DE'); "
            + $"Import-ProbedCsv -LiteralPath '{semicolon.Path}' -UseCulture -Culture ja-JP }} "
            + "finally { [System.Threading.Thread]::CurrentThread.CurrentCulture = $c }";
        Assert.Equal(new[] { PSCustomObjectType + " {A=[x;y] | B=[z]}" }, Format(this._fixture.InvokeScriptCapturingErrors(script).Output));

        Assert.Equal(new[] { PSCustomObjectType + " {A=[1] | B=[2]}" }, Format(Invoke(new() { ["LiteralPath"] = new[] { comma.Path } }).Output));
    }

    // ─── パス（仕様書 9 章） ────────────────────────────────────────────────

    /// <summary>
    /// ワイルドカードが複数のファイルに一致した場合は、一致したファイルを順に読むこと（標準はエラー）。
    /// </summary>
    [Fact]
    public void Import_WildcardMatchingMultipleFiles_ReadsAll()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("w1.csv", Encoding.ASCII.GetBytes("A\r\n1\r\n"));
        directory.Write("w2.csv", Encoding.ASCII.GetBytes("A\r\n2\r\n"));

        InvocationResult result = Invoke(new() { ["Path"] = new[] { directory.Combine("w*.csv") } });

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { PSCustomObjectType + " {A=[1]}", PSCustomObjectType + " {A=[2]}" }, Format(result.Output));
    }

    /// <summary>
    /// 配列のパスに存在しないファイルがあっても、非終了エラーを報告して残りを読むこと（標準は終了エラー）。
    /// フォルダーの指定も Get-ProbedContent と同じ非終了エラーにすること。ワイルドカードが 0 件の場合は、
    /// Get-ProbedContent と同じくエラーにしない（仕様書 9.1。第二次で改めた）。
    /// </summary>
    [Fact]
    public void Import_MissingFilesAndDirectories_NonTerminatingAndContinues()
    {
        using var directory = new TemporaryDirectory();
        string w1 = directory.Write("w1.csv", Encoding.ASCII.GetBytes("A\r\n1\r\n"));
        string w2 = directory.Write("w2.csv", Encoding.ASCII.GetBytes("A\r\n2\r\n"));

        InvocationResult result = Invoke(new()
        {
            ["Path"] = new[] { w1, directory.Combine("missing.csv"), directory.Combine("zz*.csv"), directory.Path, w2 },
        });

        Assert.Equal(new[] { "FileNotFound", "PathIsNotFile" }, result.Errors.Select(ErrorId).ToArray());
        Assert.Equal(new[] { PSCustomObjectType + " {A=[1]}", PSCustomObjectType + " {A=[2]}" }, Format(result.Output));
    }

    /// <summary>
    /// Get-ChildItem からの入力を読めること（標準の 5.1 は CannotSpecifyPathAndLiteralPath で失敗する）。
    /// -LiteralPath は角かっこをワイルドカードとして解釈しないこと。
    /// </summary>
    [Fact]
    public void Import_FromGetChildItemAndLiteralPath()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("w1.csv", Encoding.ASCII.GetBytes("A\r\n1\r\n"));
        directory.Write("w2.csv", Encoding.ASCII.GetBytes("A\r\n2\r\n"));
        string bracket = directory.Write("[x].csv", Encoding.ASCII.GetBytes("A\r\n3\r\n"));

        InvocationResult piped = this._fixture.InvokeScriptCapturingErrors(
            $"Get-ChildItem -LiteralPath '{directory.Path}' -Filter 'w*.csv' | Sort-Object Name | Import-ProbedCsv");
        Assert.Empty(piped.Errors);
        Assert.Equal(new[] { PSCustomObjectType + " {A=[1]}", PSCustomObjectType + " {A=[2]}" }, Format(piped.Output));

        InvocationResult literal = this._fixture.InvokeScriptCapturingErrors($"Import-ProbedCsv -LP '{bracket}'");
        Assert.Equal(new[] { PSCustomObjectType + " {A=[3]}" }, Format(literal.Output));
    }

    // ─── 文字エンコーディングの決定（仕様書 10 章） ─────────────────────────

    public static TheoryData<string, byte[]> WholeFileCases => new()
    {
        { "shift_jis", ByteExactFile.Concat(Encoding.ASCII.GetBytes(AsciiHead), JapaneseRowShiftJis) },
        { "euc-jp", ByteExactFile.Concat(Encoding.ASCII.GetBytes(AsciiHead.Replace("\r\n", "\n")), new byte[] { 0xC6, 0xFC, 0xCB, 0xDC, 0x2C, 0xB8, 0xEC, 0x0A }) },
        { "utf8NoBOM", new UTF8Encoding(false).GetBytes(AsciiHead + JapaneseRow) },
        { "utf8BOM", ByteExactFile.Concat(ByteExactFile.Utf8Bom, new UTF8Encoding(false).GetBytes(AsciiHead + JapaneseRow)) },
        { "unicodeBOM", ByteExactFile.Concat(ByteExactFile.Utf16LeBom, new UnicodeEncoding(false, false).GetBytes(AsciiHead + JapaneseRow)) },
        { "unicodeNoBOM", new UnicodeEncoding(false, false).GetBytes(AsciiHead + JapaneseRow) },
        { "bigendianunicodeBOM", ByteExactFile.Concat(ByteExactFile.Utf16BeBom, new UnicodeEncoding(true, false).GetBytes(AsciiHead + JapaneseRow)) },
        { "bigendianunicodeNoBOM", new UnicodeEncoding(true, false).GetBytes(AsciiHead + JapaneseRow) },
    };

    /// <summary>
    /// -Encoding 省略（Auto）・-DetectionLineCount なしは、ファイル全体で判定して読むこと（10 章の表の 2 行目）。
    /// BOM は常に読み飛ばすこと。
    /// </summary>
    [Theory]
    [MemberData(nameof(WholeFileCases))]
    public void Import_AutoWholeFile_DetectsAndReads(string label, byte[] content)
    {
        Assert.NotNull(label);
        using var file = ByteExactFile.Create(content);

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["Culture"] = "ja-JP" });

        Assert.Empty(result.Errors);
        Assert.Equal(AsciiHeadThenJapanese, Format(result.Output));
    }

    /// <summary>
    /// -Encoding を明示した場合は、その値で読み、判定も検証も行わないこと（10 章の表の 1 行目）。
    /// -DetectionLineCount を同時に指定しても無視すること（警告もエラーも出さない）。
    /// </summary>
    [Theory]
    [InlineData("shift_jis")]
    [InlineData("932")]
    public void Import_ExplicitEncoding_NoDetectionNoVerification(string encoding)
    {
        using var file = ByteExactFile.Create(ByteExactFile.Concat(Encoding.ASCII.GetBytes(AsciiHead), JapaneseRowShiftJis));

        InvocationResult result = Invoke(new()
        {
            ["LiteralPath"] = new[] { file.Path },
            ["Encoding"] = encoding,
            ["DetectionLineCount"] = 1L,
        });

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(AsciiHeadThenJapanese, Format(result.Output));
    }

    /// <summary>
    /// -Encoding に EncodingInformation（Resolve-Encoding の結果）と System.Text.Encoding を渡せること。
    /// </summary>
    [Fact]
    public void Import_EncodingInformationAndEncodingObject()
    {
        using var file = ByteExactFile.Create(ByteExactFile.Concat(Encoding.ASCII.GetBytes(AsciiHead), JapaneseRowShiftJis));

        InvocationResult byInformation = this._fixture.InvokeScriptCapturingErrors(
            $"Import-ProbedCsv -LiteralPath '{file.Path}' -Encoding (Resolve-Encoding '{file.Path}' -Culture ja-JP)");
        Assert.Equal(AsciiHeadThenJapanese, Format(byInformation.Output));

        InvocationResult byEncoding = this._fixture.InvokeScriptCapturingErrors(
            $"Import-ProbedCsv -LiteralPath '{file.Path}' -Encoding (ConvertTo-DotNetEncoding shift_jis)");
        Assert.Equal(AsciiHeadThenJapanese, Format(byEncoding.Output));
    }

    /// <summary>
    /// -DetectionLineCount 指定で先頭 N 行が ASCII の UTF-8 は、UTF-8 と読み替えて読めること（仕様書 3.2）。
    /// N が日本語の行まで含む場合も読めること。BOM 付きも読めること。
    /// </summary>
    [Theory]
    [InlineData(false, 1L)]
    [InlineData(false, 2L)]
    [InlineData(false, 3L)]
    [InlineData(true, 1L)]
    public void Import_DetectionLineCount_AsciiHeadUtf8_Reads(bool bom, long lineCount)
    {
        using var file = ByteExactFile.Create(ByteExactFile.Concat(
            bom ? ByteExactFile.Utf8Bom : Array.Empty<byte>(), new UTF8Encoding(false).GetBytes(AsciiHead + JapaneseRow)));

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["DetectionLineCount"] = lineCount });

        Assert.Empty(result.Errors);
        Assert.Equal(AsciiHeadThenJapanese, Format(result.Output));
    }

    /// <summary>
    /// 先頭 N 行が ASCII で後半に日本語がある Shift_JIS / EUC-JP は、UTF-8 と読み替えた結果が検証で止まり、
    /// 1 件も出力しないこと（仕様書 4.3）。メッセージに、パス・読み替えた文字エンコーディング・N・位置・バイト列を含むこと。
    /// 次のファイルには進むこと。
    /// </summary>
    [Theory]
    [InlineData("shift_jis", 16, "93 FA")]
    [InlineData("euc-jp", 16, "C6 FC")]
    public void Import_DetectionLineCount_AsciiHeadLegacy_StopsAtVerification(string label, long offset, string bytes)
    {
        byte[] tail = label == "shift_jis" ? JapaneseRowShiftJis : JapaneseRowEucJp;
        using var file = ByteExactFile.Create(ByteExactFile.Concat(Encoding.ASCII.GetBytes(AsciiHead), tail));
        using var next = ByteExactFile.Create(Encoding.ASCII.GetBytes("A\r\n1\r\n"));

        InvocationResult result = Invoke(new()
        {
            ["LiteralPath"] = new[] { file.Path, next.Path },
            ["DetectionLineCount"] = 2L,
        });

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("DetectionVerificationFailed", ErrorId(error));
        Assert.Equal(ErrorCategory.InvalidData, error.CategoryInfo.Category);
        Assert.Equal(
            ValidationMessages.DetectionVerificationFailed(file.Path, "utf8NoBOM", 2, offset, bytes.Split(' ')[0]),
            error.Exception.Message);
        Assert.Equal(new[] { PSCustomObjectType + " {A=[1]}" }, Format(result.Output));

        // N が日本語の行まで含めば、その文字エンコーディングと判定して読める
        InvocationResult covering = Invoke(new()
        {
            ["LiteralPath"] = new[] { file.Path },
            ["DetectionLineCount"] = 3L,
            ["Culture"] = "ja-JP",
        });
        Assert.Empty(covering.Errors);
        Assert.Equal(AsciiHeadThenJapanese, Format(covering.Output));
    }

    public static TheoryData<string, bool> UnicodeAsciiHeadCases => new()
    {
        { "utf16le", false }, { "utf16le", true },
        { "utf16be", false }, { "utf16be", true },
        { "utf32le", false }, { "utf32le", true },
        { "utf32be", false }, { "utf32be", true },
    };

    /// <summary>
    /// BOM 無しの UTF-16 / UTF-32 で、先頭 N 行が英数字だけのファイル（英数字だけのもの・後半に日本語があるもの）が、
    /// 切り出しで決めた改行の型に従って読み替えられ、U+0000 の混じらない値で読めること（仕様書 3.2、24 章 U4）。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnicodeAsciiHeadCases))]
    public void Import_DetectionLineCount_UnicodeWithoutBomAsciiHead_ReadsCorrectly(string label, bool japaneseTail)
    {
        Encoding encoding = label switch
        {
            "utf16le" => new UnicodeEncoding(false, false),
            "utf16be" => new UnicodeEncoding(true, false),
            "utf32le" => new UTF32Encoding(false, false),
            _ => new UTF32Encoding(true, false),
        };

        using var file = ByteExactFile.Create(encoding.GetBytes(AsciiHead + (japaneseTail ? JapaneseRow : "c,d\r\n")));

        // 先頭 N 行の判定の時点で UTF-16 / UTF-32 になる（1.3.0 仕様書 追補 5 章。追補より前は us-ascii で、読み替えで救っていた）
        var resolved = (EncodingInformation)this._fixture.Invoke(
            "Resolve-Encoding", new Dictionary<string, object?> { ["Path"] = file.Path, ["DetectionLineCount"] = 2L })[0].BaseObject;
        Assert.Equal(encoding.CodePage, resolved.CodePage);

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["DetectionLineCount"] = 2L });

        Assert.Empty(result.Errors);
        Assert.Equal(
            new[]
            {
                PSCustomObjectType + " {Name=[a] | Memo=[b]}",
                japaneseTail
                    ? PSCustomObjectType + " {Name=[\\u65E5\\u672C] | Memo=[\\u8A9E]}"
                    : PSCustomObjectType + " {Name=[c] | Memo=[d]}",
            },
            Format(result.Output));
    }

    public static TheoryData<string> UnicodeWithoutBomLabels => new() { "utf16le", "utf16be", "utf32le", "utf32be" };

    private static Encoding UnicodeWithoutBom(string label) => label switch
    {
        "utf16le" => new UnicodeEncoding(false, false),
        "utf16be" => new UnicodeEncoding(true, false),
        "utf32le" => new UTF32Encoding(false, false),
        _ => new UTF32Encoding(true, false),
    };

    /// <summary>
    /// 英数字だけの BOM 無し UTF-16 / UTF-32 を、全体の判定（-DetectionLineCount なし）でも U+0000 の混じらない値で読めること
    /// （1.3.0 仕様書 追補 5 章）。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnicodeWithoutBomLabels))]
    public void Import_WholeFile_AlphanumericUnicodeWithoutBom_ReadsCorrectly(string label)
    {
        using var file = ByteExactFile.Create(UnicodeWithoutBom(label).GetBytes(AsciiHead + "c,d\r\n"));

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { file.Path } });

        Assert.Empty(result.Errors);
        Assert.Equal(
            new[]
            {
                PSCustomObjectType + " {Name=[a] | Memo=[b]}",
                PSCustomObjectType + " {Name=[c] | Memo=[d]}",
            },
            Format(result.Output));
    }

    /// <summary>
    /// 2 バイトとも 0x80 未満になる文字（上 U+4E0A = UTF-16LE で 0A 4E）を含む BOM 無しの UTF-16 / UTF-32 は、
    /// 全体の判定では ASCII のままだが（<b>既知の限界。1.3.0 仕様書 追補 4.1</b>）、
    /// -DetectionLineCount を指定すると、切り出しで決めた改行の型による読み替え（仕様書 3.2・U4）で正しく読めること。
    /// 切り出しは「上」の 0A を改行とみなさない。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnicodeWithoutBomLabels))]
    public void Import_UnicodeWithLowBytesOnlyCharacter_DetectionLineCountReadsCorrectly(string label)
    {
        Encoding encoding = UnicodeWithoutBom(label);
        using var file = ByteExactFile.Create(encoding.GetBytes("A,上\r\n1,2\r\n"));

        // 全体の判定では ASCII（既知の限界）
        var resolved = (EncodingInformation)this._fixture.Invoke(
            "Resolve-Encoding", new Dictionary<string, object?> { ["Path"] = file.Path })[0].BaseObject;
        Assert.Equal(20127, resolved.CodePage);

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["DetectionLineCount"] = 1L });

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { PSCustomObjectType + " {A=[1] | \\u4E0A=[2]}" }, Format(result.Output));
    }

    /// <summary>
    /// BOM で文字エンコーディングが決まった場合も検証すること（例外を設けない。仕様書 4.1）。
    /// </summary>
    [Fact]
    public void Import_DetectionLineCount_BomFileIsAlsoVerified()
    {
        // UTF-8 の BOM の後ろに、UTF-8 として不正な FF がある
        using var file = ByteExactFile.Create(ByteExactFile.Concat(
            ByteExactFile.Utf8Bom, Encoding.ASCII.GetBytes("A\r\n1\r\n"), new byte[] { 0xFF, 0x0D, 0x0A }));

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["DetectionLineCount"] = 1L });

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("DetectionVerificationFailed", ErrorId(error));
        Assert.Equal(
            ValidationMessages.DetectionVerificationFailed(file.Path, "utf8BOM", 1, 9, "FF"),
            error.Exception.Message);
        Assert.Empty(result.Output);

        // -DetectionLineCount なし（全体の判定）は検証しない（既存のコマンドと同じ。仕様書 1.4）
        Assert.Empty(Invoke(new() { ["LiteralPath"] = new[] { file.Path } }).Errors);
    }

    /// <summary>
    /// 一致（仕様書 3.3）: Resolve-Encoding -DetectionLineCount の結果と、Import-ProbedCsv が使う判定（読み替えの前）が
    /// すべてのプロパティで一致すること。Import-ProbedCsv は HeadLineDetection を通して判定する。
    /// </summary>
    [Theory]
    [InlineData("shift_jis", 2L)]
    [InlineData("shift_jis", 3L)]
    [InlineData("utf16be", 2L)]
    [InlineData("utf8bom", 1L)]
    public void HeadLineDetection_SameAsResolveEncoding(string label, long lineCount)
    {
        byte[] content = label switch
        {
            "shift_jis" => ByteExactFile.Concat(Encoding.ASCII.GetBytes(AsciiHead), JapaneseRowShiftJis),
            "utf16be" => new UnicodeEncoding(true, false).GetBytes(AsciiHead + JapaneseRow),
            _ => ByteExactFile.Concat(ByteExactFile.Utf8Bom, new UTF8Encoding(false).GetBytes(AsciiHead + JapaneseRow)),
        };
        using var file = ByteExactFile.Create(content);

        var resolved = (EncodingInformation)this._fixture.Invoke(
            "Resolve-Encoding",
            new Dictionary<string, object?> { ["Path"] = file.Path, ["DetectionLineCount"] = lineCount, ["Culture"] = "ja-JP" })[0].BaseObject;

        EncodingInformation used = HeadLineDetection.Detect(
            file.Path, lineCount, new EncodingDetectorOptions { Culture = "ja-JP" }).Information;

        Assert.Equal(resolved.CodePage, used.CodePage);
        Assert.Equal(resolved.EncodingWebName, used.EncodingWebName);
        Assert.Equal(resolved.PSEncodingName, used.PSEncodingName);
        Assert.Equal(resolved.UsePSName, used.UsePSName);
        Assert.Equal(resolved.Bom, used.Bom);
        Assert.Equal(resolved.LineBreak, used.LineBreak);
        Assert.Equal(resolved.Culture, used.Culture);
    }

    // ─── エラー（仕様書 12 章） ──────────────────────────────────────────────

    /// <summary>
    /// 解釈できない -Culture / -Strategy、不正な -Encoding は終了エラーにすること。
    /// </summary>
    [Fact]
    public void Import_InvalidOptions_Terminating()
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("A\r\n1\r\n"));

        var culture = Assert.Throws<CmdletInvocationException>(
            () => Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["Culture"] = "xx-INVALID-yy" }));
        Assert.Equal("InvalidCulture", ErrorId(culture.ErrorRecord));

        var strategy = Assert.Throws<CmdletInvocationException>(
            () => Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["Strategy"] = "Nope" }));
        Assert.Equal("InvalidStrategy", ErrorId(strategy.ErrorRecord));

        Assert.ThrowsAny<ParameterBindingException>(
            () => Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["Encoding"] = "no-such-encoding" }));
    }

    /// <summary>
    /// -DetectionLineCount が 0 以下はパラメータ束縛の段階で拒否すること。
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Import_DetectionLineCountZeroOrNegative_Rejected(long lineCount)
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("A\r\n1\r\n"));

        var exception = Assert.ThrowsAny<ParameterBindingException>(
            () => Invoke(new() { ["LiteralPath"] = new[] { file.Path }, ["DetectionLineCount"] = lineCount }));

        Assert.Equal("ParameterArgumentValidationError", ErrorId(exception.ErrorRecord));
    }

    /// <summary>
    /// 判定の失敗・コードページが無い場合は、Get-ProbedContent と同じ非終了エラーにし、次のファイルへ進むこと。
    /// 全体の判定でも先頭 N 行の判定でも同じ。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_DetectionFailures_NonTerminating(bool useLineCount)
    {
        using var undetectable = ByteExactFile.Create(
            new byte[] { 0x81, 0xFF, 0x00, 0xFE, 0x93, 0x40, 0xC0, 0x80, 0xED, 0xA0, 0x80 });
        using var unavailable = ByteExactFile.Create(new byte[]
        {
            0x1B, 0x24, 0x2B, 0x49, 0x1B, 0x4E, 0x21, 0x21, 0x1B, 0x28, 0x42, 0x41, 0x0D, 0x0A,
        });
        using var good = ByteExactFile.Create(Encoding.ASCII.GetBytes("A\r\n1\r\n"));

        var parameters = new Dictionary<string, object?>
        {
            ["LiteralPath"] = new[] { undetectable.Path, unavailable.Path, good.Path },
        };

        if (useLineCount)
        {
            parameters["DetectionLineCount"] = 5L;
        }

        InvocationResult result = Invoke(parameters);

        Assert.Equal(new[] { "EncodingDetectionFailed", "CodePageNotAvailable" }, result.Errors.Select(ErrorId).ToArray());
        Assert.Equal(new[] { PSCustomObjectType + " {A=[1]}" }, Format(result.Output));
    }

    /// <summary>
    /// 全体で判定する場合に上限（2 GB）を超えるファイルは、-DetectionLineCount を案内する非終了エラーにすること（仕様書 10 章）。
    /// 大きさだけで判断するので、ファイルを読まない。
    /// </summary>
    [Fact]
    public void Import_WholeFileTooLarge_SuggestsDetectionLineCount()
    {
        using var huge = SparseFile.Create(EncodingInheritance.MaxDetectableLength + 1);

        InvocationResult result = Invoke(new() { ["LiteralPath"] = new[] { huge.Path } });

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("EncodingDetectionFailed", ErrorId(error));
        Assert.Equal(ValidationMessages.FileTooLargeToDetectUseLineCount(huge.Path), error.Exception.Message);
    }

    // ─── コマンドの形 ───────────────────────────────────────────────────────

    /// <summary>
    /// パラメータセットが 4 つで、既定が DelimiterPath であること。-Path / -LiteralPath の束縛の属性が Get-ProbedContent と同じであること。
    /// </summary>
    [Fact]
    public void Metadata_ParameterSetsAndBinding()
    {
        var metadata = new CommandMetadata(typeof(ImportProbedCsvCommand));

        Assert.Equal("DelimiterPath", metadata.DefaultParameterSetName);
        Assert.Equal(
            new[] { "CultureLiteralPath", "CulturePath", "DelimiterLiteralPath", "DelimiterPath" },
            metadata.Parameters["Path"].ParameterSets.Keys
                .Concat(metadata.Parameters["LiteralPath"].ParameterSets.Keys).OrderBy(k => k, StringComparer.Ordinal).ToArray());

        Assert.Equal(new[] { "FullName", "FilePath" }, metadata.Parameters["Path"].Aliases);
        Assert.Equal(new[] { "PSPath", "LP" }, metadata.Parameters["LiteralPath"].Aliases);
        Assert.True(metadata.Parameters["Path"].ParameterSets["DelimiterPath"].ValueFromPipeline);
        Assert.True(metadata.Parameters["Path"].ParameterSets["DelimiterPath"].ValueFromPipelineByPropertyName);
        Assert.False(metadata.Parameters["LiteralPath"].ParameterSets["DelimiterLiteralPath"].ValueFromPipeline);
        Assert.True(metadata.Parameters["LiteralPath"].ParameterSets["DelimiterLiteralPath"].ValueFromPipelineByPropertyName);

        Assert.Equal(1, metadata.Parameters["Delimiter"].ParameterSets["DelimiterPath"].Position);
        Assert.True(metadata.Parameters["UseCulture"].ParameterSets["CulturePath"].IsMandatory);
        Assert.Equal(typeof(long), metadata.Parameters["DetectionLineCount"].ParameterType);
        Assert.True(metadata.Parameters.ContainsKey("Culture"));
        Assert.True(metadata.Parameters.ContainsKey("Strategy"));
    }

    private InvocationResult Invoke(Dictionary<string, object?> parameters)
        => this._fixture.InvokeCapturingErrors(CommandName, parameters);

    private static string ErrorId(ErrorRecord error) => error.FullyQualifiedErrorId.Split(',')[0];

    private static string[] Format(IEnumerable<PSObject> objects)
        => objects.Select(CsvTestFormat.FormatObject).ToArray();
}
