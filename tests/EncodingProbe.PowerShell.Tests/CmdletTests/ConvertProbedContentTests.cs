using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Convert-ProbedContent の仕様（1.2.0 仕様書 第 2 部）を検証するテスト。
/// </summary>
/// <remarks>
/// 期待値はバイト列で表現する。終了エラーは try / catch で、非終了エラーはエラーストリームで検査する。
/// </remarks>
public class ConvertProbedContentTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string NoError = "NO-ERROR";

    /// <summary>「あ」「い」の Shift_JIS</summary>
    private static readonly byte[] SjisAI = { 0x82, 0xA0, 0x82, 0xA2 };

    /// <summary>「あ」「い」の UTF-8</summary>
    private static readonly byte[] Utf8AI = { 0xE3, 0x81, 0x82, 0xE3, 0x81, 0x84 };

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ConvertProbedContentTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    #region 省略時の規則（仕様書 12.1）

    /// <summary>
    /// 変換の指定が 1 つも無ければ終了エラー（E1）。-LineBreak Auto だけでも同じ。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("-LineBreak Auto")]
    [InlineData("-SourceEncoding utf8 -Culture ja-JP")]
    public void Omitted_NothingToConvert_IsTerminatingError(string parameters)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A\r\n"));

        Assert.Equal("NoConversionSpecified", Run($"Convert-ProbedContent -LiteralPath '{path}' {parameters}"));
    }

    /// <summary>
    /// -Encoding だけを指定すると、BOM の規則は語彙に従い、改行は保つこと。
    /// </summary>
    [Fact]
    public void Omitted_LineBreak_IsKept()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", ByteExactFile.Concat(new byte[] { 0x82, 0xA0, 0x0D, 0x0A, 0x82, 0xA2, 0x0A }));

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding utf8NoBOM -SourceEncoding shift_jis"));
        Assert.Equal(new byte[] { 0xE3, 0x81, 0x82, 0x0D, 0x0A, 0xE3, 0x81, 0x84, 0x0A }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -LineBreak だけを指定すると、文字エンコーディングと BOM は変換元のまま保つこと。
    /// </summary>
    [Fact]
    public void Omitted_EncodingAndBom_AreKept()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", ByteExactFile.Concat(ByteExactFile.Utf16LeBom, Encoding.Unicode.GetBytes("a\r\nb\n")));

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf"));
        Assert.Equal(ByteExactFile.Concat(ByteExactFile.Utf16LeBom, Encoding.Unicode.GetBytes("a\nb\n")), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Encoding Auto は受け付けない（E2）。
    /// </summary>
    [Fact]
    public void Encoding_Auto_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A"));

        Assert.Equal("AutoNotAllowedForConvertContent", Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding Auto"));
    }

    /// <summary>
    /// -Encoding と -EncodingFrom の同時指定は終了エラー（E3）。
    /// </summary>
    [Fact]
    public void Encoding_WithEncodingFrom_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A"));

        Assert.Equal(
            "EncodingAndEncodingFromAreExclusive",
            Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding utf8NoBOM -EncodingFrom '{path}'"));
    }

    #endregion

    #region -Bom と -Encoding（仕様書 12.2 / 12.3）

    /// <summary>
    /// Unicode 系 5 系統の Add / Remove（-Encoding 省略。変換元の文字エンコーディングを保つ）。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnicodeFamilies))]
    public void Bom_UnicodeFamilies_AddAndRemove(string noBomName, string bomName, byte[] preamble, Func<string, byte[]> encode)
    {
        _ = noBomName;
        _ = bomName;
        using var directory = new TemporaryDirectory();
        byte[] body = encode("A\n");
        string withoutBom = directory.Write("without.txt", body);
        string withBom = directory.Write("with.txt", ByteExactFile.Concat(preamble, body));
        string sourceEncoding = noBomName;

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{withoutBom}' -Bom Add -SourceEncoding {sourceEncoding}"));
        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{withBom}' -Bom Remove -SourceEncoding {sourceEncoding}"));

        Assert.Equal(ByteExactFile.Concat(preamble, body), File.ReadAllBytes(withoutBom));
        Assert.Equal(body, File.ReadAllBytes(withBom));
    }

    public static IEnumerable<object[]> UnicodeFamilies()
    {
        yield return new object[] { "utf8NoBOM", "utf8BOM", ByteExactFile.Utf8Bom, (Func<string, byte[]>)(s => new UTF8Encoding(false).GetBytes(s)) };
        yield return new object[] { "unicodeNoBOM", "unicodeBOM", ByteExactFile.Utf16LeBom, (Func<string, byte[]>)(s => new UnicodeEncoding(false, false).GetBytes(s)) };
        yield return new object[] { "bigendianunicodeNoBOM", "bigendianunicodeBOM", ByteExactFile.Utf16BeBom, (Func<string, byte[]>)(s => new UnicodeEncoding(true, false).GetBytes(s)) };
        yield return new object[] { "utf32NoBOM", "utf32BOM", ByteExactFile.Utf32LeBom, (Func<string, byte[]>)(s => new UTF32Encoding(false, false).GetBytes(s)) };
        yield return new object[] { "bigendianutf32NoBOM", "bigendianutf32BOM", ByteExactFile.Utf32BeBom, (Func<string, byte[]>)(s => new UTF32Encoding(true, false).GetBytes(s)) };
    }

    /// <summary>
    /// -Encoding 省略で変換元が Unicode 系以外: Add は非終了エラー（N5）、Remove は何もしない。
    /// </summary>
    [Fact]
    public void Bom_NonUnicodeSource_AddIsError_RemoveIsNoOp()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", SjisAI);

        InvocationResult add = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -Bom Add -SourceEncoding shift_jis");
        Assert.Equal("BomNotSupportedByEncoding", ErrorId(Assert.Single(add.Errors)));
        Assert.Equal(SjisAI, File.ReadAllBytes(path));

        InvocationResult remove = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -Bom Remove -SourceEncoding shift_jis -PassThru");
        Assert.Empty(remove.Errors);
        Assert.False((bool)Assert.Single(remove.Output).Properties["Changed"].Value);
        Assert.Equal(SjisAI, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 接尾辞付きの語彙は、-Bom と一致すれば許可、食い違えば終了エラー（E6）。
    /// </summary>
    [Theory]
    [InlineData("utf8BOM", "Add", NoError)]
    [InlineData("utf8NoBOM", "Remove", NoError)]
    [InlineData("utf8NoBOM", "Add", "BomConflict")]
    [InlineData("unicodeBOM", "Remove", "BomConflict")]
    public void Bom_SuffixedVocabulary_MustAgree(string encoding, string bom, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A"));

        Assert.Equal(expected, Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding {encoding} -Bom {bom}"));
    }

    /// <summary>
    /// Unicode 系の裸名・WebName・数値は、-Bom があれば系統名として扱い、BOM は -Bom で決めること。
    /// -Bom が無ければ 1.1.0 の規則（utf8・WebName・数値は拒否、ほかの裸名は BOM 有り）。
    /// </summary>
    [Theory]
    [InlineData("utf8", "-Bom Add", new byte[] { 0xEF, 0xBB, 0xBF, 0x41 })]
    [InlineData("utf8", "-Bom Remove", new byte[] { 0x41 })]
    [InlineData("utf-8", "-Bom Add", new byte[] { 0xEF, 0xBB, 0xBF, 0x41 })]
    [InlineData("65001", "-Bom Remove", new byte[] { 0x41 })]
    [InlineData("unicode", "-Bom Remove", new byte[] { 0x41, 0x00 })]
    [InlineData("utf-16", "-Bom Add", new byte[] { 0xFF, 0xFE, 0x41, 0x00 })]
    [InlineData("unicode", "", new byte[] { 0xFF, 0xFE, 0x41, 0x00 })]
    [InlineData("utf32", "", new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 })]
    public void Bom_BareUnicodeNames_AreFamilyNamesWithBom(string encoding, string bom, byte[] expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A"));

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding {encoding} {bom}"));
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Bom が無い裸の utf8・WebName・数値は拒否（E4）。utf7 は常に拒否（E5）。
    /// BOM 接尾辞を許さない語彙への接尾辞はパラメータ束縛で拒否（E5）。
    /// </summary>
    [Theory]
    [InlineData("utf8", "", "EncodingNotAllowedForWrite")]
    [InlineData("utf-8", "", "EncodingNotAllowedForWrite")]
    [InlineData("65001", "", "EncodingNotAllowedForWrite")]
    [InlineData("utf-16", "", "EncodingNotAllowedForWrite")]
    [InlineData("utf7", "", "EncodingNotAllowedForWrite")]
    [InlineData("utf7", "-Bom Remove", "EncodingNotAllowedForWrite")]
    [InlineData("shift_jisBOM", "", "ParameterArgumentTransformationError")]
    public void Bom_ForbiddenWriteEncodings_AreTerminatingErrors(string encoding, string bom, string expected)
    {
        using var directory = new TemporaryDirectory();
        byte[] original = Encoding.ASCII.GetBytes("A");
        string path = directory.Write("a.txt", original);

        Assert.Equal(expected, Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding {encoding} {bom}"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Unicode 系以外の -Encoding: Add は終了エラー（E7）、Remove は許可（BOM 無し）。
    /// </summary>
    [Theory]
    [InlineData("shift_jis", "Add", "BomNotSupportedByEncoding")]
    [InlineData("932", "Add", "BomNotSupportedByEncoding")]
    [InlineData("shift_jis", "Remove", NoError)]
    public void Bom_NonUnicodeEncoding(string encoding, string bom, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Utf8AI);

        Assert.Equal(expected, Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding {encoding} -Bom {bom} -SourceEncoding utf8"));
        Assert.Equal(expected == NoError ? SjisAI : Utf8AI, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Encoding インスタンス: GetPreamble() に従い、-Bom と食い違えば終了エラー（E8）。
    /// </summary>
    [Theory]
    [InlineData("([System.Text.Encoding]::UTF8)", "", NoError, new byte[] { 0xEF, 0xBB, 0xBF, 0x41 })]
    [InlineData("([System.Text.Encoding]::UTF8)", "-Bom Add", NoError, new byte[] { 0xEF, 0xBB, 0xBF, 0x41 })]
    [InlineData("([System.Text.Encoding]::UTF8)", "-Bom Remove", "BomConflict", new byte[] { 0x41 })]
    [InlineData("(New-Object System.Text.UTF8Encoding $false)", "-Bom Add", "BomConflict", new byte[] { 0x41 })]
    public void Bom_EncodingInstance_FollowsPreamble(string encoding, string bom, string expected, byte[] expectedBytes)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A"));

        Assert.Equal(expected, Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding {encoding} {bom}"));
        Assert.Equal(expectedBytes, File.ReadAllBytes(path));
    }

    /// <summary>
    /// EncodingInformation と -EncodingFrom は BOM と改行を継承し、-Bom で BOM を上書きできること。
    /// </summary>
    [Theory]
    [InlineData("-EncodingFrom '{0}'", "", new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 })]
    [InlineData("-EncodingFrom '{0}'", "-Bom Remove", new byte[] { 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 })]
    [InlineData("-Encoding (Resolve-Encoding -Path '{0}')", "", new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 })]
    [InlineData("-Encoding (Resolve-Encoding -Path '{0}')", "-Bom Remove", new byte[] { 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 })]
    [InlineData("-Encoding (Resolve-Encoding -Path '{0}')", "-LineBreak CrLf", new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0D, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0D, 0x00, 0x0A, 0x00 })]
    public void Bom_ReferenceInformation_IsInheritedAndOverridable(string reference, string extra, byte[] expected)
    {
        using var directory = new TemporaryDirectory();
        string referencePath = directory.Write("ref.txt", new byte[] { 0xFF, 0xFE, 0x78, 0x00, 0x0A, 0x00 });
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A\r\nB\r\n"));

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' {string.Format(reference, referencePath)} {extra}"));
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -EncodingFrom の参照先が無ければ終了エラー（E9）。変換元に BOM を付けられない参照先への -Bom Add は E7。
    /// </summary>
    [Fact]
    public void EncodingFrom_Errors()
    {
        using var directory = new TemporaryDirectory();
        string sjis = directory.Write("sjis.txt", SjisAI);
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("A"));

        Assert.Equal("EncodingFromNotFound", Run($"Convert-ProbedContent -LiteralPath '{path}' -EncodingFrom '{directory.Combine("missing.txt")}'"));
        Assert.Equal("BomNotSupportedByEncoding", Run($"Convert-ProbedContent -LiteralPath '{path}' -EncodingFrom '{sjis}' -Bom Add -Culture ja-JP"));
    }

    #endregion

    #region 文字を失わない保証（仕様書 13 章）

    /// <summary>
    /// 変換元に不正なバイト列があれば変換せず（N3）、メッセージにバイト位置を示すこと。
    /// </summary>
    [Fact]
    public void Lossless_InvalidSourceBytes_LeaveFileUntouched()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = { 0x61, 0xE9, 0x62, 0x0A };
        string path = directory.Write("a.txt", original);

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -SourceEncoding utf8 -Encoding utf8BOM");

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("InvalidSourceBytes", ErrorId(error));
        Assert.Equal(ValidationMessages.InvalidSourceBytes(path, "utf8NoBOM", 1, "E9"), error.Exception.Message);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    /// <summary>
    /// 不正なバイト列の位置は、復号器の報告ではなく自前で求めるため、どの実行環境でも同じになること。
    /// </summary>
    [Theory]
    [InlineData("utf8", new byte[] { 0x61, 0x62, 0xE3, 0x81 }, 2, "E3 81")]
    [InlineData("utf8", new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0xFF, 0x62 }, 4, "FF")]
    [InlineData("shift_jis", new byte[] { 0x61, 0x82, 0xA0, 0x82, 0x0A }, 3, "82 0A")]
    [InlineData("euc-jp", new byte[] { 0x61, 0xA4, 0xA2, 0xA4, 0x41 }, 3, "A4 41")]
    public void Lossless_InvalidByteOffset_IsExact(string encoding, byte[] content, int offset, string bytes)
    {
        System.Text.Encoding strict = EncodingVocabulary.BuildStrictEncoding(EncodingVocabulary.Resolve(encoding, EncodingUsage.Read).Encoding!.CodePage);
        int start = encoding == "utf8" && content[0] == 0xEF ? 3 : 0;

        int actual = ConvertProbedContentCommand.FindInvalidBytes(strict, content, start, out byte[] invalid);

        Assert.Equal(offset, actual);
        Assert.Equal(bytes, string.Join(" ", invalid.Select(b => b.ToString("X2"))));
    }

    /// <summary>
    /// 変換先で表現できない文字があれば変換せず（N4）、文字と行・桁を示すこと。
    /// </summary>
    [Fact]
    public void Lossless_UnrepresentableCharacter_LeavesFileUntouched()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = new UTF8Encoding(false).GetBytes("ab\r\nc\U0001F600d\n");
        string path = directory.Write("a.txt", original);

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -SourceEncoding utf8 -Encoding shift_jis");

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("UnrepresentableCharacter", ErrorId(error));
        Assert.Equal(
            ValidationMessages.UnrepresentableCharacter(path, "shift_jis", "U+1F600", "\U0001F600", 2, 2),
            error.Exception.Message);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("abc", 0, 1, 1)]
    [InlineData("ab\r\ncd", 5, 2, 2)]
    [InlineData("a\rb\nc", 4, 3, 1)]
    public void Lossless_LineAndColumn(string text, int index, int line, int column)
    {
        ConvertProbedContentCommand.GetLineAndColumn(text, index, out int actualLine, out int actualColumn);

        Assert.Equal(line, actualLine);
        Assert.Equal(column, actualColumn);
    }

    /// <summary>
    /// 私用領域（HKSCS 固有字）は符号位置のまま往復し、Big5 に戻すと元のバイト列に一致すること。
    /// </summary>
    [Fact]
    public void Lossless_PrivateUseArea_RoundTrips()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = File.ReadAllBytes(TestDataHelper.GetPath("Chinese_HongKong", "sample_big5hkscs.txt"));
        string path = directory.Write("hkscs.txt", original);

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' -SourceEncoding big5 -Encoding utf8NoBOM"));
        Assert.NotEqual(original, File.ReadAllBytes(path));

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' -SourceEncoding utf8 -Encoding big5"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    #endregion

    #region 改行（仕様書 14 章）

    /// <summary>
    /// 混在改行を揃え、末尾の改行の有無を保ち、U+2028 / U+0085 は対象外とすること。
    /// </summary>
    [Theory]
    [InlineData("a\r\nb\nc\rd", "Lf", "a\nb\nc\nd")]
    [InlineData("a\r\nb\nc\rd\n", "CrLf", "a\r\nb\r\nc\r\nd\r\n")]
    [InlineData("a\nb\n\n", "Cr", "a\rb\r\r")]
    [InlineData("a\u2028b\u0085c\r\n", "Lf", "a\u2028b\u0085c\n")]
    public void LineBreak_UnifiesAllLineBreaks(string source, string lineBreak, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", new UTF8Encoding(false).GetBytes(source));

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak {lineBreak} -SourceEncoding utf8"));
        Assert.Equal(new UTF8Encoding(false).GetBytes(expected), File.ReadAllBytes(path));
    }

    #endregion

    #region 上書きの安全性（仕様書 15 章）

    /// <summary>
    /// 変換結果が元と同じなら書き直さない（更新日時が変わらない）。
    /// </summary>
    [Fact]
    public void Safety_Unchanged_IsNotRewritten()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\nb\n"));
        var past = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, past);

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf -PassThru");

        Assert.Empty(result.Errors);
        Assert.False((bool)Assert.Single(result.Output).Properties["Changed"].Value);
        Assert.Equal(past, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>
    /// 読み取り専用で -Force なしは非終了エラー（N6）。-Force ありは書き込み後に属性を戻すこと。
    /// </summary>
    [Fact]
    public void Safety_ReadOnly()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\r\n"));
        File.SetAttributes(path, FileAttributes.ReadOnly);

        InvocationResult denied = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf");
        Assert.Equal("WriteAccessDenied", ErrorId(Assert.Single(denied.Errors)));
        Assert.Equal(Encoding.ASCII.GetBytes("a\r\n"), File.ReadAllBytes(path));

        InvocationResult forced = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf -Force");
        Assert.Empty(forced.Errors);
        Assert.Equal(Encoding.ASCII.GetBytes("a\n"), File.ReadAllBytes(path));
        Assert.True(ReadOnlyAttributeScope.IsReadOnly(path));

        // 一時ファイルが残っていないこと
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    /// <summary>
    /// 読み取り中のファイルは変換しない（N10）。
    /// </summary>
    [Fact]
    public void Safety_FileBeingRead_IsError()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\r\nb\r\n"));

        InvocationResult result = Invoke(
            $"Get-ProbedContent -LiteralPath '{path}' | ForEach-Object {{ Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf }}");

        Assert.All(result.Errors, error => Assert.Equal("SamePathRoundTrip", ErrorId(error)));
        Assert.NotEmpty(result.Errors);
        Assert.Equal(Encoding.ASCII.GetBytes("a\r\nb\r\n"), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -WhatIf ではファイルを変えず、-PassThru も出力しないこと。
    /// </summary>
    [Fact]
    public void Safety_WhatIf_DoesNothing()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\r\n"));

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf -PassThru -WhatIf");

        Assert.Empty(result.Errors);
        Assert.Empty(result.Output);
        Assert.Equal(Encoding.ASCII.GetBytes("a\r\n"), File.ReadAllBytes(path));
    }

    #endregion

    #region -Destination（仕様書 16 章）

    [Fact]
    public void Destination_WritesEvenWhenUnchanged_AndKeepsSource()
    {
        using var directory = new TemporaryDirectory();
        string source = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\n"));
        string output = directory.Combine("out");
        Directory.CreateDirectory(output);

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{source}' -LineBreak Lf -Destination '{output}' -PassThru");

        Assert.Empty(result.Errors);
        PSObject item = Assert.Single(result.Output);
        Assert.False((bool)item.Properties["Changed"].Value);
        Assert.Equal(Path.Combine(output, "a.txt"), item.Properties["Destination"].Value);
        Assert.Equal(Encoding.ASCII.GetBytes("a\n"), File.ReadAllBytes(Path.Combine(output, "a.txt")));
        Assert.Equal(Encoding.ASCII.GetBytes("a\n"), File.ReadAllBytes(source));
    }

    [Fact]
    public void Destination_MissingFolder_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        string source = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\n"));

        Assert.Equal("DestinationNotFound", Run($"Convert-ProbedContent -LiteralPath '{source}' -LineBreak Lf -Destination '{directory.Combine("missing")}'"));
        Assert.False(Directory.Exists(directory.Combine("missing")));

        Assert.Equal("DestinationNotFound", Run($"Convert-ProbedContent -LiteralPath '{source}' -LineBreak Lf -Destination '{source}'"));
    }

    [Fact]
    public void Destination_ExistingFile_RequiresForce()
    {
        using var directory = new TemporaryDirectory();
        string source = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\r\n"));
        string existing = directory.Write(Path.Combine("out", "a.txt"), Encoding.ASCII.GetBytes("OLD"));
        File.SetAttributes(existing, FileAttributes.ReadOnly);

        InvocationResult denied = Invoke($"Convert-ProbedContent -LiteralPath '{source}' -LineBreak Lf -Destination '{directory.Combine("out")}'");
        Assert.Equal("DestinationExists", ErrorId(Assert.Single(denied.Errors)));
        Assert.Equal(Encoding.ASCII.GetBytes("OLD"), File.ReadAllBytes(existing));

        InvocationResult forced = Invoke($"Convert-ProbedContent -LiteralPath '{source}' -LineBreak Lf -Destination '{directory.Combine("out")}' -Force");
        Assert.Empty(forced.Errors);
        Assert.Equal(Encoding.ASCII.GetBytes("a\n"), File.ReadAllBytes(existing));
        Assert.True(ReadOnlyAttributeScope.IsReadOnly(existing));
    }

    [Fact]
    public void Destination_NameConflictInSameRun_IsErrorEvenWithForce()
    {
        using var directory = new TemporaryDirectory();
        directory.Write(Path.Combine("x", "a.txt"), Encoding.ASCII.GetBytes("first\r\n"));
        directory.Write(Path.Combine("y", "a.txt"), Encoding.ASCII.GetBytes("second\r\n"));
        string output = directory.Combine("out");
        Directory.CreateDirectory(output);

        InvocationResult result = Invoke(
            $"Get-ChildItem -LiteralPath '{directory.Combine("x")}', '{directory.Combine("y")}' -File | "
            + $"Convert-ProbedContent -LineBreak Lf -Destination '{output}' -Force");

        Assert.Equal("DestinationNameConflict", ErrorId(Assert.Single(result.Errors)));
        Assert.Equal(Encoding.ASCII.GetBytes("first\n"), File.ReadAllBytes(Path.Combine(output, "a.txt")));
    }

    [Fact]
    public void Destination_SameAsSource_IsError()
    {
        using var directory = new TemporaryDirectory();
        string source = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\r\n"));

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{source}' -LineBreak Lf -Destination '{directory.Path}'");

        Assert.Equal("DestinationIsSource", ErrorId(Assert.Single(result.Errors)));
        Assert.Equal(Encoding.ASCII.GetBytes("a\r\n"), File.ReadAllBytes(source));
    }

    #endregion

    #region パイプラインとパス（仕様書 11 章）

    /// <summary>
    /// Get-ChildItem の出力が PSPath で -LiteralPath に束縛され、ディレクトリは飛ばされること。
    /// </summary>
    [Fact]
    public void Pipeline_GetChildItem_BindsAndSkipsDirectories()
    {
        using var directory = new TemporaryDirectory();
        string a = directory.Write("a[1].txt", Encoding.ASCII.GetBytes("a\r\n"));
        string b = directory.Write(Path.Combine("sub", "b.txt"), Encoding.ASCII.GetBytes("b\r\n"));

        InvocationResult result = Invoke(
            $"Get-ChildItem -LiteralPath '{directory.Path}' -Recurse | Convert-ProbedContent -LineBreak Lf -Verbose 4>&1");

        Assert.Empty(result.Errors);
        Assert.Contains(result.Output, item => item.BaseObject is VerboseRecord record
            && record.Message == ValidationMessages.SkippedDirectory(directory.Combine("sub")));
        Assert.Equal(Encoding.ASCII.GetBytes("a\n"), File.ReadAllBytes(a));
        Assert.Equal(Encoding.ASCII.GetBytes("b\n"), File.ReadAllBytes(b));
    }

    /// <summary>
    /// -Path はワイルドカードを展開し、存在しないパスは非終了エラー（N1）で他のファイルの処理は続けること。
    /// </summary>
    [Fact]
    public void Path_WildcardAndMissingFile()
    {
        using var directory = new TemporaryDirectory();
        string a = directory.Write("a1.txt", Encoding.ASCII.GetBytes("a\r\n"));
        string b = directory.Write("a2.txt", Encoding.ASCII.GetBytes("b\r\n"));

        InvocationResult result = Invoke(
            $"Convert-ProbedContent '{directory.Combine("missing.txt")}', '{directory.Combine("a?.txt")}', '{directory.Combine("z*.txt")}' -LineBreak Lf");

        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Equal("FileNotFound", ErrorId(error)));
        Assert.Equal(Encoding.ASCII.GetBytes("a\n"), File.ReadAllBytes(a));
        Assert.Equal(Encoding.ASCII.GetBytes("b\n"), File.ReadAllBytes(b));
    }

    /// <summary>
    /// 変換元の検出に失敗した場合は非終了エラー（N2）。-SourceEncoding で回避できること。
    /// </summary>
    [Fact]
    public void Source_DetectionFailure_IsNonTerminating()
    {
        using var directory = new TemporaryDirectory();
        byte[] undetectable = { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00 };
        string path = directory.Write("a.bin", undetectable);

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf -Strategy NativeOnly");

        Assert.Contains(ErrorId(Assert.Single(result.Errors)), new[] { "EncodingDetectionFailed", "CodePageNotAvailable" });
        Assert.Equal(undetectable, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 解釈できない -SourceEncoding / -Culture / -Strategy は終了エラー（E10）。
    /// </summary>
    [Theory]
    [InlineData("-SourceEncoding no-such-encoding", "ParameterArgumentTransformationError")]
    [InlineData("-Culture no-such-culture-xx", "InvalidCulture")]
    [InlineData("-Strategy NoSuchStrategy", "InvalidStrategy")]
    public void Source_InvalidOptions_AreTerminating(string parameters, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("a\r\n"));

        Assert.Equal(expected, Run($"Convert-ProbedContent -LiteralPath '{path}' -LineBreak Lf {parameters}"));
        Assert.Equal(Encoding.ASCII.GetBytes("a\r\n"), File.ReadAllBytes(path));
    }

    /// <summary>
    /// 0 バイトのファイルは 0 バイトのまま書き直さないこと（BOM も付けない）。
    /// </summary>
    [Fact]
    public void Source_Empty_StaysEmpty()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Array.Empty<byte>());

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{path}' -Encoding utf8BOM -PassThru");

        Assert.Empty(result.Errors);
        Assert.False((bool)Assert.Single(result.Output).Properties["Changed"].Value);
        Assert.Empty(File.ReadAllBytes(path));
    }

    #endregion

    #region -PassThru（仕様書 17 章）

    [Fact]
    public void PassThru_ReportsAllProperties()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", new byte[] { 0x82, 0xA0, 0x0D, 0x0A, 0x82, 0xA2, 0x0A });

        PSObject item = Assert.Single(Invoke(
            $"Convert-ProbedContent -LiteralPath '{path}' -Encoding unicodeBOM -LineBreak Lf -Culture ja-JP -PassThru").Output);

        Assert.Equal(ConvertProbedContentCommand.ResultTypeName, item.TypeNames[0]);
        Assert.Equal(path, item.Properties["Path"].Value);
        Assert.Equal(path, item.Properties["Destination"].Value);
        Assert.Equal("shift_jis", item.Properties["SourceEncoding"].Value);
        Assert.Equal("unicodeBOM", item.Properties["Encoding"].Value);
        Assert.Equal("LfAndCrLf", item.Properties["SourceLineBreak"].Value);
        Assert.Equal("Lf", item.Properties["LineBreak"].Value);
        Assert.Equal(true, item.Properties["Changed"].Value);
    }

    /// <summary>
    /// 出力された語彙名をそのまま -Encoding に渡せば、元のバイト列に戻ること（原則 B）。
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x0D, 0x0A })]
    [InlineData(new byte[] { 0x41, 0x00, 0x0A, 0x00 })]
    [InlineData(new byte[] { 0x82, 0xA0, 0x0D, 0x0A })]
    public void PassThru_SourceEncodingName_RestoresOriginal(byte[] original)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", original);
        string sourceEncoding = original[0] == 0x41 ? " -SourceEncoding unicodeNoBOM" : string.Empty;

        PSObject item = Assert.Single(Invoke(
            $"Convert-ProbedContent -LiteralPath '{path}' -Encoding utf32BOM -Culture ja-JP -PassThru{sourceEncoding}").Output);
        string name = (string)item.Properties["SourceEncoding"].Value;

        Assert.Equal(NoError, Run($"Convert-ProbedContent -LiteralPath '{path}' -Encoding {name} -SourceEncoding utf32"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 既定では何も出力しないこと。エラーになったファイルは -PassThru でも出力しないこと。
    /// </summary>
    [Fact]
    public void PassThru_NoOutputByDefaultOrOnError()
    {
        using var directory = new TemporaryDirectory();
        string good = directory.Write("good.txt", Encoding.ASCII.GetBytes("a\r\n"));
        string bad = directory.Write("bad.txt", new byte[] { 0x61, 0xE9 });

        Assert.Empty(Invoke($"Convert-ProbedContent -LiteralPath '{good}' -LineBreak Lf").Output);

        InvocationResult result = Invoke($"Convert-ProbedContent -LiteralPath '{bad}', '{good}' -SourceEncoding utf8 -LineBreak CrLf -PassThru");

        Assert.Single(result.Errors);
        Assert.Equal(good, Assert.Single(result.Output).Properties["Path"].Value);
    }

    #endregion

    /// <summary>
    /// スクリプトを実行し、終了エラーの FullyQualifiedErrorId（前半）か、エラーが無ければ <see cref="NoError"/> を返す。
    /// 非終了エラーはこの戻り値に現れない（<see cref="Invoke"/> を使う）。
    /// </summary>
    private string Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ {script} -ErrorAction Stop | Out-Null ; '{NoError}' }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        return result.Output.Last().ToString();
    }

    /// <summary>
    /// スクリプトを実行し、出力と非終了エラーを返す
    /// </summary>
    private InvocationResult Invoke(string script) => this._fixture.InvokeScriptCapturingErrors(script);

    private static string ErrorId(ErrorRecord error) => error.FullyQualifiedErrorId.Split(',')[0];
}
