using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Resolve-Encoding コマンドレットを PowerShell ランスペース経由で実行する機能テスト。
/// </summary>
public class ResolveEncodingCmdletTests : IClassFixture<RunspaceFixture>
{
    private readonly RunspaceFixture _fixture;

    public ResolveEncodingCmdletTests(RunspaceFixture fixture)
        => _fixture = fixture;

    // ─── 戻り値の型 ──────────────────────────────────────────────────────────

    [Fact]
    public void Invoke_ReturnsEncodingInfomation()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var results = _fixture.Invoke(path);

        Assert.Single(results);
        Assert.IsType<EncodingInformation>(results[0].BaseObject);
    }

    // ─── 英語ファイル ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sample_ascii.txt",    20127, "us-ascii",  "ascii")]
    [InlineData("sample_utf8.txt",     65001, "utf-8",     "utf8NoBOM")]
    [InlineData("sample_utf8_bom.txt", 65001, "utf-8",     "utf8BOM")]
    public void Invoke_English(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
    {
        var path = TestDataHelper.GetPath("English", fileName);
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.Equal(expectedCodePage,       result.CodePage);
        Assert.Equal(expectedEncodingName,   result.EncodingWebName);
        Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
    }

    // ─── 日本語ファイル ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("sample_utf8.txt",     65001, "utf-8",       "utf8NoBOM")]
    [InlineData("sample_utf8_bom.txt", 65001, "utf-8",       "utf8BOM")]
    [InlineData("sample_shiftjis.txt", 932,   "shift_jis",   "shift_jis")]
    [InlineData("sample_eucjp.txt",    20932, "euc-jp",      "euc-jp")]
    [InlineData("sample_jis.txt",      50220, "iso-2022-jp", "iso-2022-jp")]
    public void Invoke_Japanese(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
    {
        var path = TestDataHelper.GetPath("Japanese", fileName);
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.Equal(expectedCodePage,       result.CodePage);
        Assert.Equal(expectedEncodingName,   result.EncodingWebName);
        Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
    }

    // ─── BOM フラグ検証 ──────────────────────────────────────────────────────

    [Fact]
    public void Invoke_Utf8WithBom_BomFlagIsTrue()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8_bom.txt");
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.True(result.Bom);
    }

    [Fact]
    public void Invoke_Utf8WithoutBom_BomFlagIsFalse()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.False(result.Bom);
    }

    // ─── エラー処理 ──────────────────────────────────────────────────────────

    [Fact]
    public void Invoke_NonExistentFile_ThrowsFileNotFoundException()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          Guid.NewGuid().ToString() + ".txt");

        // PowerShell エンジンはコマンドレット内の例外を CmdletInvocationException でラップする。
        // 本来の FileNotFoundException は InnerException に格納される。
        var ex = Assert.Throws<System.Management.Automation.CmdletInvocationException>(
            () => _fixture.Invoke(path));
        Assert.IsType<System.IO.FileNotFoundException>(ex.InnerException);
    }

    // ─── Culture パラメーター ────────────────────────────────────────────────

    [Fact]
    public void Invoke_WithCulture_ReturnsEncodingInfomation()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var result = _fixture.Invoke(path, new() { ["Culture"] = "ja-JP" });

        Assert.Single(result);
        Assert.IsType<EncodingInformation>(result[0].BaseObject);
    }

    // ─── Strategy パラメーター ───────────────────────────────────────────────

    [Theory]
    [InlineData("Combined")]
    [InlineData("UtfUnknownOnly")]
    [InlineData("NativeOnly")]
    public void Invoke_WithStrategy_ReturnsEncodingInfomation(string strategy)
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var result = _fixture.Invoke(path, new() { ["Strategy"] = strategy });

        Assert.Single(result);
        Assert.IsType<EncodingInformation>(result[0].BaseObject);
    }

    [Fact]
    public void Invoke_WithInvalidStrategy_ThrowsCmdletInvocationException()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");

        // 1.3.0 第五次の後半（BUG-0006）から、ほかのコマンドと同じ ID の終了エラー（生の ArgumentException ではない）
        var ex = Assert.Throws<System.Management.Automation.CmdletInvocationException>(
            () => _fixture.Invoke(path, new() { ["Strategy"] = "InvalidStrategy" }));
        Assert.Equal("InvalidStrategy", ex.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(System.Management.Automation.ErrorCategory.InvalidArgument, ex.ErrorRecord.CategoryInfo.Category);
        Assert.IsType<System.Management.Automation.PSArgumentException>(ex.ErrorRecord.Exception);
    }

    // ─── License ─────────────────────────────────────────────────────────────

    /// <summary>
    /// -License の出力に、移植した PowerShell のコードの表示が含まれること（1.3.0 仕様書 5.3、付録 A.3）。
    /// </summary>
    [Fact]
    public void Invoke_License_IncludesPowerShellNotice()
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = _fixture.Runspace;
        shell.AddCommand("Resolve-Encoding").AddParameter("License");

        string text = (string)Assert.Single(shell.Invoke()).BaseObject;

        Assert.Contains("This module includes portions of PowerShell", text);
        Assert.Contains("https://github.com/PowerShell/PowerShell", text);
        Assert.Contains("Copyright (c) Microsoft Corporation", text);
        Assert.Contains("MIT License", text);
        Assert.Contains("THIRD-PARTY-NOTICES.txt", text);

        // 既存の表示（本モジュールとクラスライブラリ・UTF.Unknown）は残っていること
        Assert.Contains("SnowStack.EncodingProbe.PowerShell is licensed under MIT License.", text);
        Assert.Contains("UTF.Unknown", text);
    }

    // ─── DetectionLineCount パラメーター（1.3.0） ────────────────────────────

    /// <summary>
    /// N が改行の数以上なら、全体で判定した場合とすべてのプロパティが一致すること（仕様書 2.6）。
    /// </summary>
    [Theory]
    [InlineData("Japanese", "sample_utf8.txt")]
    [InlineData("Japanese", "sample_utf8_bom.txt")]
    [InlineData("Japanese", "sample_shiftjis.txt")]
    [InlineData("Japanese", "sample_eucjp.txt")]
    [InlineData("Japanese", "sample_jis.txt")]
    [InlineData("Korean", "sample_cp949.txt")]
    [InlineData("Korean", "sample_euckr.txt")]
    [InlineData("Chinese_Traditional", "sample_big5_long.txt")]
    [InlineData("Russian", "sample_cp1251.txt")]
    [InlineData("English", "sample_ascii.txt")]
    public void DetectionLineCount_CoveringAllLines_SameAsWholeFile(string language, string fileName)
    {
        var path = TestDataHelper.GetPath(language, fileName);

        var whole = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;
        var head = (EncodingInformation)_fixture.Invoke(path, new() { ["DetectionLineCount"] = 1_000_000L })[0].BaseObject;

        AssertSameInformation(whole, head);
    }

    /// <summary>
    /// 先頭 N 行だけが ASCII の場合は us-ascii を報告し、読み替えないこと（仕様書 3.1）。
    /// 後半の日本語まで含む N なら、全体と同じ判定になること。
    /// </summary>
    [Theory]
    [InlineData(65001)]
    [InlineData(932)]
    [InlineData(51932)]
    public void DetectionLineCount_AsciiHead_ReportsUsAscii(int codePage)
    {
        System.Text.Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: false);
        using var file = ByteExactFile.Create(encoding.GetBytes("Name,Memo\r\na,b\r\n日本,語\r\n"));

        var head = Detect(file.Path, 2);
        Assert.Equal(20127, head.CodePage);
        Assert.Equal("us-ascii", head.EncodingWebName);
        Assert.False(head.Bom);

        var covering = Detect(file.Path, 3);
        var whole = (EncodingInformation)_fixture.Invoke(file.Path, new() { ["Culture"] = "ja-JP" })[0].BaseObject;
        Assert.Equal(whole.CodePage, covering.CodePage);
    }

    /// <summary>
    /// UTF-16 / UTF-32 の BOM 付きのファイルは、先頭 N 行でも BOM から判定されること。
    /// </summary>
    [Theory]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void DetectionLineCount_UnicodeWithBom_DetectedFromBom(int codePage)
    {
        System.Text.Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: true);
        using var file = ByteExactFile.CreateFrom(encoding.GetPreamble(), encoding.GetBytes("A,B\r\n1,2\r\n日本,語\r\n"));

        var head = Detect(file.Path, 1);

        Assert.Equal(codePage, head.CodePage);
        Assert.True(head.Bom);
        Assert.Equal(LineBreakType.CrLf, head.LineBreak);
    }

    /// <summary>
    /// BOM 無しの UTF-16 / UTF-32 で、先頭 N 行に日本語がある場合は、その文字エンコーディングと判定されること。
    /// 切り出した長さが単位の倍数になるので、判定処理の UTF-16 / UTF-32 の判定が働く（仕様書 2.5）。
    /// </summary>
    [Theory]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void DetectionLineCount_UnicodeWithoutBom_NonAsciiHead_Detected(int codePage)
    {
        System.Text.Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: false);
        using var file = ByteExactFile.Create(encoding.GetBytes("日本,語\r\n1,2\r\nA,B\r\n"));

        var head = Detect(file.Path, 1);
        var whole = (EncodingInformation)_fixture.Invoke(file.Path)[0].BaseObject;

        Assert.Equal(codePage, head.CodePage);
        Assert.False(head.Bom);
        Assert.Equal(whole.CodePage, head.CodePage);
    }

    /// <summary>
    /// BOM 無しの UTF-16 / UTF-32 で、先頭 N 行が英数字だけの場合も、その文字エンコーディングと報告すること
    /// （1.3.0 仕様書 追補 2 章。追補より前は us-ascii と報告していた）。
    /// </summary>
    [Theory]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void DetectionLineCount_UnicodeWithoutBom_AsciiHead_ReportsInputEncoding(int codePage)
    {
        System.Text.Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: false);
        using var file = ByteExactFile.Create(encoding.GetBytes("A,B\r\n1,2\r\n日本,語\r\n"));

        var head = Detect(file.Path, 2);

        Assert.Equal(codePage, head.CodePage);
        Assert.False(head.Bom);
    }

    /// <summary>
    /// 英数字だけの BOM 無し UTF-16 / UTF-32 を、全体の判定でもその文字エンコーディングと報告すること（1.3.0 仕様書 追補 5 章）。
    /// </summary>
    [Theory]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void WholeFile_AlphanumericUnicodeWithoutBom_ReportsInputEncoding(int codePage)
    {
        System.Text.Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: false);
        using var file = ByteExactFile.Create(encoding.GetBytes("Name,Memo\r\na,b\r\n"));

        var whole = (EncodingInformation)_fixture.Invoke(file.Path)[0].BaseObject;

        Assert.Equal(codePage, whole.CodePage);
        Assert.False(whole.Bom);
        Assert.Equal(LineBreakType.CrLf, whole.LineBreak);
    }

    /// <summary>
    /// <b>既知の限界（1.3.0 仕様書 追補 4.1）。</b>2 バイトとも 0x80 未満になる文字（上 U+4E0A）を含む BOM 無しの UTF-16 / UTF-32 は、
    /// 全体の判定では us-ascii と報告すること（正しい挙動ではない。-DetectionLineCount を指定した Import-ProbedCsv では正しく読める）。
    /// </summary>
    [Theory]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void WholeFile_UnicodeWithLowBytesOnlyCharacter_ReportsUsAscii_KnownLimit(int codePage)
    {
        System.Text.Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: false);
        using var file = ByteExactFile.Create(encoding.GetBytes("A,上\r\n1,2\r\n"));

        var whole = (EncodingInformation)_fixture.Invoke(file.Path)[0].BaseObject;

        Assert.Equal(20127, whole.CodePage);
        Assert.Equal("us-ascii", whole.EncodingWebName);
    }

    /// <summary>
    /// LineBreak も先頭 N 行だけで判定した結果になること（後半だけの改行の混在は見逃す。仕様書 6.2）。
    /// </summary>
    [Fact]
    public void DetectionLineCount_LineBreakIsFromHeadOnly()
    {
        using var file = ByteExactFile.Create(System.Text.Encoding.ASCII.GetBytes("a\r\nb\r\nc\nd\n"));

        Assert.Equal(LineBreakType.CrLf, Detect(file.Path, 2).LineBreak);
        Assert.Equal(LineBreakType.LfAndCrLf, Detect(file.Path, 4).LineBreak);
    }

    /// <summary>
    /// 0 バイトのファイルは、全体の判定と同じ結果になること（仕様書 2.6）。
    /// </summary>
    [Fact]
    public void DetectionLineCount_EmptyFile_SameAsWholeFile()
    {
        using var file = ByteExactFile.Create();

        var whole = (EncodingInformation)_fixture.Invoke(file.Path)[0].BaseObject;

        AssertSameInformation(whole, Detect(file.Path, 1));
    }

    /// <summary>
    /// -Culture / -Strategy は、先頭 N 行の判定にも効くこと。
    /// 判定結果の Culture に、指定したカルチャーが入る。
    /// </summary>
    [Fact]
    public void DetectionLineCount_CultureAndStrategyApply()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");

        var result = (EncodingInformation)_fixture.Invoke(path, new()
        {
            ["DetectionLineCount"] = 1L,
            ["Culture"] = "ko-KR",
            ["Strategy"] = "NativeOnly",
        })[0].BaseObject;

        Assert.Equal("ko-KR", result.Culture);
    }

    /// <summary>
    /// 0 と負の値は、パラメータ束縛の段階で拒否すること（仕様書 6.1）。
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void DetectionLineCount_ZeroOrNegative_RejectedAtBinding(long lineCount)
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");

        var exception = Assert.ThrowsAny<System.Management.Automation.ParameterBindingException>(
            () => _fixture.Invoke(path, new() { ["DetectionLineCount"] = lineCount }));

        Assert.Equal("ParameterArgumentValidationError", exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
    }

    /// <summary>
    /// -DetectionLineCount は位置パラメータではなく、型は Int64 であること（仕様書 6.1）。
    /// </summary>
    [Fact]
    public void DetectionLineCount_IsNamedInt64Parameter()
    {
        var property = typeof(ResolveEncodingCmdlet).GetProperty(nameof(ResolveEncodingCmdlet.DetectionLineCount))!;
        var parameter = (System.Management.Automation.ParameterAttribute)Attribute.GetCustomAttribute(
            property, typeof(System.Management.Automation.ParameterAttribute))!;

        Assert.Equal(typeof(long), property.PropertyType);
        Assert.Equal(int.MinValue, parameter.Position);
        Assert.False(parameter.ValueFromPipeline);
        Assert.False(parameter.ValueFromPipelineByPropertyName);
        Assert.Equal("Default", parameter.ParameterSetName);
    }

    /// <summary>
    /// 切り出しが判定の上限（2 GB）を超える場合は、終了エラー（ID EncodingDetectionFailed）にすること（仕様書 2.6・6.2）。
    /// 改行の無い巨大なファイルとして、中身がすべて 0x00 のスパースファイルを使う。
    /// </summary>
    [Fact]
    public void DetectionLineCount_ExceedingLimit_ThrowsTerminatingError()
    {
        using var file = SparseFile.Create(EncodingInheritance.MaxDetectableLength + 1);

        var exception = Assert.Throws<System.Management.Automation.CmdletInvocationException>(
            () => _fixture.Invoke(file.Path, new() { ["DetectionLineCount"] = 1L }));

        Assert.Equal("EncodingDetectionFailed", exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(System.Management.Automation.ErrorCategory.InvalidData, exception.ErrorRecord.CategoryInfo.Category);
        Assert.IsType<EncodingDetectionException>(exception.InnerException);
    }

    private EncodingInformation Detect(string path, long lineCount)
        => (EncodingInformation)_fixture.Invoke(path, new()
        {
            ["DetectionLineCount"] = lineCount,
            ["Culture"] = "ja-JP",
        })[0].BaseObject;

    private static void AssertSameInformation(EncodingInformation expected, EncodingInformation actual)
    {
        Assert.Equal(expected.CodePage, actual.CodePage);
        Assert.Equal(expected.EncodingWebName, actual.EncodingWebName);
        Assert.Equal(expected.PSEncodingName, actual.PSEncodingName);
        Assert.Equal(expected.UsePSName, actual.UsePSName);
        Assert.Equal(expected.Bom, actual.Bom);
        Assert.Equal(expected.LineBreak, actual.LineBreak);
        Assert.Equal(expected.Culture, actual.Culture);
    }
}
