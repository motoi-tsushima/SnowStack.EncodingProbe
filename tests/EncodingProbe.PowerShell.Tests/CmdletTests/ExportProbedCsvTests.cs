using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.CsvTests;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Export-ProbedCsv（1.3.0 仕様書 第 4 部）を PowerShell ランスペース経由で検証するテスト。
/// </summary>
/// <remarks>
/// 期待値は実測報告 M4 / M5 / M6 の PowerShell 7.6.7 の結果（バイト列）と、仕様書 21 章の差異を使う。
/// 追記先などのテストデータはバイト列を明示して作る（本モジュールの書き込みコードで作らない）。
/// <br/>
/// 終了エラーは、スクリプトの try / catch で FullyQualifiedErrorId を取り出して確かめる。
/// </remarks>
public class ExportProbedCsvTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private static readonly byte[] Crlf = { 0x0D, 0x0A };

    /// <summary>実測報告 M5 の追記先（"A","B" / "1","2"、CR-LF、BOM 無し UTF-8）</summary>
    private static readonly byte[] BaseCsv = Encoding.ASCII.GetBytes("\"A\",\"B\"\r\n\"1\",\"2\"\r\n");

    /// <summary>「追記」の Shift_JIS（92 C7 8B 4C）と UTF-8（E8 BF BD E8 A8 98）</summary>
    private const string Tsuiki = "追記";

    private const string SampleObjectScript =
        "[pscustomobject]@{ Name = \"$([char]0x65E5)$([char]0x672C)$([char]0x8A9E)\"; Quote = 'say \"hi\"'; "
        + "Multi = \"l1`nl2\"; Comma = 'a,b'; Flag = $true; When = (New-Object DateTime 2026, 10, 15, 14, 30, 0); "
        + "Num = 1234.5; Empty = $null }";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ExportProbedCsvTests(ProbedCommandRunspaceFixture fixture)
        => this._fixture = fixture;

    // ─── 書き出し（移植。仕様書 17 章、実測報告 M4） ─────────────────────────

    /// <summary>
    /// M4: -Encoding 省略で、7.6.7 の Export-Csv（オプションなし）とバイト単位で同じファイルになること。
    /// #TYPE 行を書かず、BOM 無しの UTF-8（5.1 の標準は #TYPE 行を書き、日本語が ??? になる）。
    /// </summary>
    [Fact]
    public void Export_M4_Default_SameBytesAsExportCsv767()
    {
        using var file = ByteExactFile.CreateMissing();

        RunWithCulture("ja-JP", $"{SampleObjectScript} | Export-ProbedCsv -LiteralPath '{file.Path}'");

        byte[] expected = Hex(
            "22 4E 61 6D 65 22 2C 22 51 75 6F 74 65 22 2C 22 4D 75 6C 74 69 22 2C 22 43 6F 6D 6D 61 22 2C 22 46 6C 61 67 22 2C 22 57 68 65 6E 22 2C 22 4E 75 6D 22 2C 22 45 6D 70 74 79 22 0D 0A "
            + "22 E6 97 A5 E6 9C AC E8 AA 9E 22 2C 22 73 61 79 20 22 22 68 69 22 22 22 2C 22 6C 31 0A 6C 32 22 2C 22 61 2C 62 22 2C 22 54 72 75 65 22 2C 22 32 30 32 36 2F 31 30 2F 31 35 20 31 34 3A 33 30 3A 30 30 22 2C 22 31 32 33 34 2E 35 22 2C 0D 0A");

        Assert.Equal(expected, file.ReadBytes());
    }

    /// <summary>
    /// M4: -UseQuotes AsNeeded / Never、-QuoteFields、-NoHeader が 7.6.7 の ConvertTo-Csv と同じ行になること（5.1 でも使える）。
    /// </summary>
    [Theory]
    [InlineData("-UseQuotes AsNeeded", "Name,Quote,Multi,Comma,Flag,When,Num,Empty\r\n日本語,\"say \"\"hi\"\"\",\"l1\nl2\",\"a,b\",True,2026/10/15 14:30:00,1234.5,\r\n")]
    [InlineData("-UseQuotes Never", "Name,Quote,Multi,Comma,Flag,When,Num,Empty\r\n日本語,say \"hi\",l1\nl2,a,b,True,2026/10/15 14:30:00,1234.5,\r\n")]
    [InlineData("-QuoteFields Name,Num", "\"Name\",Quote,Multi,Comma,Flag,When,\"Num\",Empty\r\n\"日本語\",say \"hi\",l1\nl2,a,b,True,2026/10/15 14:30:00,\"1234.5\",\r\n")]
    [InlineData("-NoHeader", "\"日本語\",\"say \"\"hi\"\"\",\"l1\nl2\",\"a,b\",\"True\",\"2026/10/15 14:30:00\",\"1234.5\",\r\n")]
    [InlineData("-NoTypeInformation", "\"Name\",\"Quote\",\"Multi\",\"Comma\",\"Flag\",\"When\",\"Num\",\"Empty\"\r\n\"日本語\",\"say \"\"hi\"\"\",\"l1\nl2\",\"a,b\",\"True\",\"2026/10/15 14:30:00\",\"1234.5\",\r\n")]
    public void Export_M4_Options_SameAsConvertToCsv767(string options, string expected)
    {
        using var file = ByteExactFile.CreateMissing();

        RunWithCulture("ja-JP", $"{SampleObjectScript} | Export-ProbedCsv -LiteralPath '{file.Path}' {options}");

        Assert.Equal(new UTF8Encoding(false).GetBytes(expected), file.ReadBytes());
    }

    /// <summary>
    /// -IncludeTypeInformation は #TYPE 行を書く。型名が CSV: で始まる場合は除く（Import-ProbedCsv の結果を書き出す場合）。
    /// -NoTypeInformation と同時に指定した場合は -IncludeTypeInformation に従う。
    /// </summary>
    [Fact]
    public void Export_IncludeTypeInformation()
    {
        using var file = ByteExactFile.CreateMissing();
        using var source = ByteExactFile.Create(Encoding.ASCII.GetBytes("#TYPE Foo.Bar\r\nA\r\n1\r\n"));

        Run($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -IncludeTypeInformation -NoTypeInformation");
        Assert.Equal("#TYPE System.Management.Automation.PSCustomObject\r\n\"A\"\r\n\"1\"\r\n", Ascii(file));

        Run($"Import-ProbedCsv -LiteralPath '{source.Path}' | Export-ProbedCsv -LiteralPath '{file.Path}' -ITI");
        Assert.Equal("#TYPE Foo.Bar\r\n\"A\"\r\n\"1\"\r\n", Ascii(file));
    }

    /// <summary>
    /// 辞書はキーを列にし、2 件目以降で足りない列は空、余分なプロパティは書かない（24 章 U1。警告もしない）。
    /// -Delimiter と -UseCulture（区切り文字。判定の -Culture とは別）。
    /// </summary>
    [Fact]
    public void Export_InputKindsAndDelimiters()
    {
        using var file = ByteExactFile.CreateMissing();

        Run($"[ordered]@{{ A = 1; B = 'x' }} | Export-ProbedCsv -LiteralPath '{file.Path}'");
        Assert.Equal("\"A\",\"B\"\r\n\"1\",\"x\"\r\n", Ascii(file));

        InvocationResult mixed = Run(
            $"[pscustomobject]@{{ A = 1; B = 2 }}, [pscustomobject]@{{ B = 3; C = 4 }} | Export-ProbedCsv -LiteralPath '{file.Path}'");
        Assert.Empty(mixed.Warnings);
        Assert.Equal("\"A\",\"B\"\r\n\"1\",\"2\"\r\n,\"3\"\r\n", Ascii(file));

        Run($"[pscustomobject]@{{ A = 'x;y'; B = 'z' }} | Export-ProbedCsv '{file.Path}' ';'");
        Assert.Equal("\"A\";\"B\"\r\n\"x;y\";\"z\"\r\n", Ascii(file));

        RunWithCulture("de-DE", $"[pscustomobject]@{{ A = 'x'; B = 'z' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -UseCulture -Culture ja-JP");
        Assert.Equal("\"A\";\"B\"\r\n\"x\";\"z\"\r\n", Ascii(file));
    }

    /// <summary>
    /// 往復: Export-ProbedCsv で書いたものを Import-ProbedCsv で読むと元の値に戻ること（引用符・区切り文字・改行・日本語）。
    /// </summary>
    [Theory]
    [InlineData("utf8NoBOM")]
    [InlineData("shift_jis")]
    [InlineData("unicodeBOM")]
    public void Export_RoundTripWithImport(string encoding)
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Run(
            "$src = [pscustomobject]@{ Q = 'say \"hi\"'; D = 'a,b'; N = \"l1`r`nl2\"; J = \"$([char]0x65E5)$([char]0x672C)\"; E = '' }; "
            + $"$src | Export-ProbedCsv -LiteralPath '{file.Path}' -Encoding {encoding}; "
            + $"$back = Import-ProbedCsv -LiteralPath '{file.Path}' -Culture ja-JP; "
            + "($src.Q -ceq $back.Q) -and ($src.D -ceq $back.D) -and ($src.N -ceq $back.N) -and ($src.J -ceq $back.J) -and ($back.E -ceq '')");

        Assert.Empty(result.Errors);
        Assert.True((bool)Assert.Single(result.Output).BaseObject);
    }

    // ─── 文字エンコーディングの決定（仕様書 16.1） ──────────────────────────

    /// <summary>
    /// -Encoding 省略・-Append なし: utf8NoBOM。既存のファイルは判定しない（Shift_JIS の既存ファイルも UTF-8 で上書き）。
    /// </summary>
    [Fact]
    public void Encoding_OmittedWithoutAppend_Utf8NoBom()
    {
        using var file = ByteExactFile.Create(ShiftJisCsv());

        Run($"[pscustomobject]@{{ A = '{Tsuiki}' }} | Export-ProbedCsv -LiteralPath '{file.Path}'");

        Assert.Equal(ByteExactFile.Concat(Encoding.ASCII.GetBytes("\"A\"\r\n\""), Hex("E8 BF BD E8 A8 98"), Encoding.ASCII.GetBytes("\"\r\n")), file.ReadBytes());
    }

    /// <summary>
    /// -Encoding 省略・-Append: 追記先から継承する（M5 g: 標準は UTF-8 が混ざる）。追記先が無い・0 バイトなら utf8NoBOM でヘッダーから書く（M5 d / e）。
    /// </summary>
    [Fact]
    public void Encoding_OmittedWithAppend_InheritsOrUtf8NoBom()
    {
        using var sjis = ByteExactFile.Create(ShiftJisCsv());
        Run($"[pscustomobject]@{{ A = '{Tsuiki}'; B = 'x' }} | Export-ProbedCsv -LiteralPath '{sjis.Path}' -Append -Culture ja-JP");
        Assert.Equal(ByteExactFile.Concat(ShiftJisCsv(), Encoding.ASCII.GetBytes("\""), Hex("92 C7 8B 4C"), Encoding.ASCII.GetBytes("\",\"x\"\r\n")), sjis.ReadBytes());

        using var empty = ByteExactFile.Create();
        Run($"[pscustomobject]@{{ A = '{Tsuiki}' }} | Export-ProbedCsv -LiteralPath '{empty.Path}' -Append");
        Assert.Equal(ByteExactFile.Concat(Encoding.ASCII.GetBytes("\"A\"\r\n\""), Hex("E8 BF BD E8 A8 98"), Encoding.ASCII.GetBytes("\"\r\n")), empty.ReadBytes());

        using var missing = ByteExactFile.CreateMissing();
        Run($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{missing.Path}' -Append");
        Assert.Equal("\"A\"\r\n\"1\"\r\n", Ascii(missing));
    }

    /// <summary>
    /// 明示的な Auto: 出力先の既存ファイルから継承する。-Append なしで出力先が無ければ終了エラー。
    /// </summary>
    [Fact]
    public void Encoding_ExplicitAuto()
    {
        using var sjis = ByteExactFile.Create(ShiftJisCsv());
        Run($"[pscustomobject]@{{ A = '{Tsuiki}' }} | Export-ProbedCsv -LiteralPath '{sjis.Path}' -Encoding Auto -Culture ja-JP");
        Assert.Equal(ByteExactFile.Concat(Encoding.ASCII.GetBytes("\"A\"\r\n\""), Hex("92 C7 8B 4C"), Encoding.ASCII.GetBytes("\"\r\n")), sjis.ReadBytes());

        using var missing = ByteExactFile.CreateMissing();
        Assert.Equal("AutoEncodingRequiresExistingFile", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{missing.Path}' -Encoding Auto"));
        Assert.Equal("AutoEncodingRequiresExistingFile", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{missing.Path}' -Encoding Auto -Append"));
        Assert.False(missing.Exists);
    }

    /// <summary>
    /// 明示: その値で書く（BOM 付きの語彙なら BOM を書く）。裸の utf8 は拒否する。
    /// -Append で文字エンコーディングが違えば整合性検査で止まり、-AllowEncodingChange なら書く。
    /// </summary>
    [Fact]
    public void Encoding_Explicit()
    {
        using var file = ByteExactFile.CreateMissing();
        Run($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Encoding utf8BOM");
        Assert.Equal(ByteExactFile.Concat(ByteExactFile.Utf8Bom, Encoding.ASCII.GetBytes("\"A\"\r\n\"1\"\r\n")), file.ReadBytes());

        Assert.ThrowsAny<ParameterBindingException>(() => this._fixture.Invoke(
            "Export-ProbedCsv", new Dictionary<string, object?> { ["LiteralPath"] = file.Path, ["InputObject"] = 1, ["Encoding"] = "utf8" }));

        using var sjis = ByteExactFile.Create(ShiftJisCsv());
        Assert.Equal(
            "EncodingChangeOnAppend",
            ErrorIdOf($"[pscustomobject]@{{ A = '{Tsuiki}'; B = 'x' }} | Export-ProbedCsv -LiteralPath '{sjis.Path}' -Append -Encoding utf8NoBOM -Culture ja-JP"));
        Assert.Equal(ShiftJisCsv(), sjis.ReadBytes());

        Run($"[pscustomobject]@{{ A = '{Tsuiki}'; B = 'x' }} | Export-ProbedCsv -LiteralPath '{sjis.Path}' -Append -Encoding utf8NoBOM -AllowEncodingChange -Culture ja-JP");
        Assert.Equal(ByteExactFile.Concat(ShiftJisCsv(), Encoding.ASCII.GetBytes("\""), Hex("E8 BF BD E8 A8 98"), Encoding.ASCII.GetBytes("\",\"x\"\r\n")), sjis.ReadBytes());
    }

    /// <summary>
    /// -EncodingFrom は参照ファイルの文字エンコーディング・BOM・改行を継承する。-Encoding との同時指定は終了エラー。
    /// </summary>
    [Fact]
    public void Encoding_EncodingFrom()
    {
        using var reference = ByteExactFile.Create(ByteExactFile.Concat(Encoding.ASCII.GetBytes("x\n"), Hex("93 FA 96 7B 0A")));
        using var file = ByteExactFile.CreateMissing();

        Run($"[pscustomobject]@{{ A = '{Tsuiki}' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -EncodingFrom '{reference.Path}' -Culture ja-JP");
        Assert.Equal(ByteExactFile.Concat(Encoding.ASCII.GetBytes("\"A\"\n\""), Hex("92 C7 8B 4C"), Encoding.ASCII.GetBytes("\"\n")), file.ReadBytes());

        Assert.Equal(
            "EncodingAndEncodingFromAreExclusive",
            ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -EncodingFrom '{reference.Path}' -Encoding utf8NoBOM"));
    }

    // ─── -DetectionLineCount（仕様書 16.2） ────────────────────────────────

    /// <summary>
    /// 3 つの箇所（-Append の追記先、明示的な Auto の出力先、-EncodingFrom の参照ファイル）すべてで、
    /// 先頭が ASCII の Shift_JIS は検証で止まり、ファイルに何も書かないこと。-WhatIf でも同じ ID で報告すること。
    /// </summary>
    [Theory]
    [InlineData("-Append", false)]
    [InlineData("-Encoding Auto", false)]
    [InlineData("-EncodingFrom", true)]
    [InlineData("-Append -WhatIf", false)]
    [InlineData("-EncodingFrom -WhatIf", true)]
    public void DetectionLineCount_VerificationFailure_WritesNothing(string options, bool useReference)
    {
        using var target = ByteExactFile.Create(ShiftJisCsv());
        using var reference = ByteExactFile.Create(ShiftJisCsv());
        using var output = ByteExactFile.CreateMissing();

        string path = useReference ? output.Path : target.Path;
        string extra = useReference ? options.Replace("-EncodingFrom", $"-EncodingFrom '{reference.Path}'") : options;

        Assert.Equal(
            "DetectionVerificationFailed",
            ErrorIdOf($"[pscustomobject]@{{ A = '{Tsuiki}'; B = 'x' }} | Export-ProbedCsv -LiteralPath '{path}' {extra} -DetectionLineCount 2"));

        Assert.Equal(ShiftJisCsv(), target.ReadBytes());
        Assert.False(output.Exists);
    }

    /// <summary>
    /// 日本語の行まで含む N なら、Shift_JIS と判定して追記できること。
    /// 先頭が ASCII の UTF-8 の追記先は、UTF-8 と読み替えて追記できること。
    /// </summary>
    [Fact]
    public void DetectionLineCount_Success()
    {
        using var sjis = ByteExactFile.Create(ShiftJisCsv());
        Run($"[pscustomobject]@{{ A = '{Tsuiki}'; B = 'x' }} | Export-ProbedCsv -LiteralPath '{sjis.Path}' -Append -DetectionLineCount 3 -Culture ja-JP");
        Assert.Equal(ByteExactFile.Concat(ShiftJisCsv(), Encoding.ASCII.GetBytes("\""), Hex("92 C7 8B 4C"), Encoding.ASCII.GetBytes("\",\"x\"\r\n")), sjis.ReadBytes());

        byte[] utf8 = ByteExactFile.Concat(BaseCsv, new UTF8Encoding(false).GetBytes("\"日本\",\"語\"\r\n"));
        using var utf8File = ByteExactFile.Create(utf8);
        Run($"[pscustomobject]@{{ A = '{Tsuiki}'; B = 'x' }} | Export-ProbedCsv -LiteralPath '{utf8File.Path}' -Append -DetectionLineCount 1");
        Assert.Equal(ByteExactFile.Concat(utf8, Encoding.ASCII.GetBytes("\""), Hex("E8 BF BD E8 A8 98"), Encoding.ASCII.GetBytes("\",\"x\"\r\n")), utf8File.ReadBytes());
    }

    // ─── -Append（仕様書 18 章、実測報告 M5） ───────────────────────────────

    /// <summary>
    /// M5 a / c / i: 追記先の列の順序と表記で書き、余分なプロパティは書かない。列名の照合は大文字小文字を区別しない。
    /// </summary>
    [Theory]
    [InlineData("[pscustomobject]@{ B = 'b'; A = 'a' }", "\"A\",\"B\"\r\n\"1\",\"2\"\r\n", "\"a\",\"b\"\r\n")]
    [InlineData("[pscustomobject]@{ A = 'a'; B = 'b'; C = 'c' }", "\"A\",\"B\"\r\n\"1\",\"2\"\r\n", "\"a\",\"b\"\r\n")]
    [InlineData("[pscustomobject]@{ A = 'x'; B = 'y' }", "a,b\r\n1,2\r\n", "\"x\",\"y\"\r\n")]
    public void Append_M5_ColumnsFollowExistingHeader(string input, string existing, string appended)
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes(existing));

        Run($"{input} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append");

        Assert.Equal(existing + appended, Ascii(file));
    }

    /// <summary>
    /// M5 b / b2: 追記先の列に当たるプロパティが無ければ終了エラーでファイルは変わらない。-Force なら空にして書く。
    /// </summary>
    [Fact]
    public void Append_M5_MissingColumn()
    {
        using var file = ByteExactFile.Create(BaseCsv);

        Assert.Equal("CannotAppendCsvWithMismatchedPropertyNames", ErrorIdOf($"[pscustomobject]@{{ A = 'a' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append"));
        Assert.Equal(BaseCsv, file.ReadBytes());

        Run($"[pscustomobject]@{{ A = 'a' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append -Force");
        Assert.Equal(ByteExactFile.Concat(BaseCsv, Encoding.ASCII.GetBytes("\"a\",\r\n")), file.ReadBytes());
    }

    /// <summary>
    /// M5 f: BOM 付きの追記先には BOM を書かず、UTF-8 で追記する。M5 h: -Append -NoHeader は受け付ける（7.6.7 と同じ）。
    /// </summary>
    [Fact]
    public void Append_M5_BomTargetAndNoHeader()
    {
        byte[] existing = ByteExactFile.Concat(ByteExactFile.Utf8Bom, BaseCsv);
        using var file = ByteExactFile.Create(existing);

        Run($"[pscustomobject]@{{ A = '{Tsuiki}'; B = 'b' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append -NoHeader");

        Assert.Equal(
            ByteExactFile.Concat(existing, Encoding.ASCII.GetBytes("\""), Hex("E8 BF BD E8 A8 98"), Encoding.ASCII.GetBytes("\",\"b\"\r\n")),
            file.ReadBytes());
    }

    /// <summary>
    /// M5 j: 追記先の最後の行に改行が無い場合は、改行を補ってから追記する（標準は行がつながる）。
    /// UTF-16LE の追記先では、改行の単位（2 バイト）で判定し、UTF-16LE の改行を補う。
    /// </summary>
    [Fact]
    public void Append_M5_NoTrailingLineBreak_InsertsLineBreak()
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("\"A\",\"B\"\r\n1,2"));
        Run($"[pscustomobject]@{{ A = 'x'; B = 'y' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append");
        Assert.Equal("\"A\",\"B\"\r\n1,2\r\n\"x\",\"y\"\r\n", Ascii(file));

        var utf16 = new UnicodeEncoding(false, false);
        byte[] existing = ByteExactFile.Concat(ByteExactFile.Utf16LeBom, utf16.GetBytes("\"A\",\"B\"\r\n1,2"));
        using var unicode = ByteExactFile.Create(existing);
        Run($"[pscustomobject]@{{ A = 'x'; B = 'y' }} | Export-ProbedCsv -LiteralPath '{unicode.Path}' -Append");
        Assert.Equal(ByteExactFile.Concat(existing, utf16.GetBytes("\r\n\"x\",\"y\"\r\n")), unicode.ReadBytes());

        // 末尾が改行の UTF-16LE には補わない（0A 00 を改行と判定する）
        byte[] terminated = ByteExactFile.Concat(ByteExactFile.Utf16LeBom, utf16.GetBytes("\"A\",\"B\"\r\n1,2\r\n"));
        using var terminatedFile = ByteExactFile.Create(terminated);
        Run($"[pscustomobject]@{{ A = 'x'; B = 'y' }} | Export-ProbedCsv -LiteralPath '{terminatedFile.Path}' -Append");
        Assert.Equal(ByteExactFile.Concat(terminated, utf16.GetBytes("\"x\",\"y\"\r\n")), terminatedFile.ReadBytes());
    }

    /// <summary>
    /// M5 k: 追記先が LF だけなら、追記先の改行を継承する（標準は CR-LF が混ざる）。-LineBreak の明示はそれに優先する。
    /// </summary>
    [Fact]
    public void Append_M5_LfTarget_InheritsLineBreak()
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("\"A\",\"B\"\n\"1\",\"2\"\n"));
        Run($"[pscustomobject]@{{ A = 'x'; B = 'y' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append");
        Assert.Equal("\"A\",\"B\"\n\"1\",\"2\"\n\"x\",\"y\"\n", Ascii(file));

        Run($"[pscustomobject]@{{ A = 'p'; B = 'q' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append -LineBreak CrLf");
        Assert.Equal("\"A\",\"B\"\n\"1\",\"2\"\n\"x\",\"y\"\n\"p\",\"q\"\r\n", Ascii(file));
    }

    /// <summary>
    /// M5 l / l2: 読み取り専用は -Force なしで終了エラー（読み取り専用のまま）。-Force なら追記して属性を戻す。
    /// </summary>
    [Fact]
    public void Append_M5_ReadOnly()
    {
        using var file = ByteExactFile.Create(BaseCsv);
        File.SetAttributes(file.Path, FileAttributes.ReadOnly);

        try
        {
            Assert.Equal("WriteAccessDenied", ErrorIdOf($"[pscustomobject]@{{ A = 'a'; B = 'b' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append"));
            Assert.Equal(BaseCsv, file.ReadBytes());

            Run($"[pscustomobject]@{{ A = 'a'; B = 'b' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append -Force");
            Assert.Equal(ByteExactFile.Concat(BaseCsv, Encoding.ASCII.GetBytes("\"a\",\"b\"\r\n")), file.ReadBytes());
            Assert.True(File.GetAttributes(file.Path).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(file.Path, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// BOM だけの追記先は、ヘッダーの扱いだけ新規と同じにする（ヘッダーを書く。BOM は重ねて書かない。第三次の決定）。
    /// </summary>
    [Fact]
    public void Append_BomOnlyTarget_WritesHeaderWithoutSecondBom()
    {
        using var file = ByteExactFile.Create(ByteExactFile.Utf8Bom);

        Run($"[pscustomobject]@{{ A = '{Tsuiki}' }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Append");

        Assert.Equal(
            ByteExactFile.Concat(ByteExactFile.Utf8Bom, Encoding.ASCII.GetBytes("\"A\"\r\n\""), Hex("E8 BF BD E8 A8 98"), Encoding.ASCII.GetBytes("\"\r\n")),
            file.ReadBytes());
    }

    /// <summary>
    /// 仕様書 18.2 の最後の項目: 追記先が 1 バイト以上でヘッダーが読めない（# の行だけ・空行だけ）場合は、移植元の挙動に合わせる。
    /// 実行中のホスト（テストでは PowerShell 7.6 の SDK）の Export-Csv -Append と同じバイト列になることを確かめる。
    /// </summary>
    [Theory]
    [InlineData("# comment only\r\n")]
    [InlineData("#TYPE Foo\r\n# comment\r\n")]
    public void Append_HeaderNotReadable_SameAsHostExportCsv(string existing)
    {
        using var ours = ByteExactFile.Create(Encoding.ASCII.GetBytes(existing));
        using var standard = ByteExactFile.Create(Encoding.ASCII.GetBytes(existing));

        Run($"[pscustomobject]@{{ A = 'a'; B = 'b' }} | Export-ProbedCsv -LiteralPath '{ours.Path}' -Append");
        Run($"[pscustomobject]@{{ A = 'a'; B = 'b' }} | Export-Csv -LiteralPath '{standard.Path}' -Append");

        Assert.Equal(standard.ReadBytes(), ours.ReadBytes());
    }

    /// <summary>
    /// 仕様書 18.2 の最後の項目: 追記先が空行だけの場合（ヘッダーが空の列名 1 個として読まれる）も、ホストの Export-Csv -Append と同じ結果になること。
    /// </summary>
    [Fact]
    public void Append_BlankLinesOnly_SameAsHostExportCsv()
    {
        using var ours = ByteExactFile.Create(Encoding.ASCII.GetBytes("\r\n"));
        using var standard = ByteExactFile.Create(Encoding.ASCII.GetBytes("\r\n"));

        string ourError = ErrorIdOf($"[pscustomobject]@{{ A = 'a' }} | Export-ProbedCsv -LiteralPath '{ours.Path}' -Append");
        string standardError = ErrorIdOf($"[pscustomobject]@{{ A = 'a' }} | Export-Csv -LiteralPath '{standard.Path}' -Append");

        Assert.Equal(standardError, ourError);
        Assert.Equal(standard.ReadBytes(), ours.ReadBytes());
    }

    // ─── その他（仕様書 19 章、実測報告 M6） ─────────────────────────────────

    /// <summary>
    /// M6: Import-ProbedCsv a.csv | Export-ProbedCsv a.csv は SamePathRoundTrip で止まり、元のファイルは壊れない（標準は 0 バイトになる）。
    /// </summary>
    [Fact]
    public void Other_M6_SamePathRoundTrip()
    {
        using var file = ByteExactFile.Create(BaseCsv);

        Assert.Equal("SamePathRoundTrip", ErrorIdOf($"Import-ProbedCsv -LiteralPath '{file.Path}' | Export-ProbedCsv -LiteralPath '{file.Path}'"));
        Assert.Equal(BaseCsv, file.ReadBytes());
    }

    /// <summary>
    /// M6: -NoClobber で既存ファイルがあれば終了エラー。-WhatIf ではファイルを作らない。入力が空なら 0 バイトのファイルを作る。
    /// 入力が空の -Append は、既存ファイルに触れない（改行も書かない）。
    /// </summary>
    [Fact]
    public void Other_M6_NoClobberWhatIfAndEmptyInput()
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("\"A\",\"B\"\r\n1,2"));

        Assert.Equal("NoClobber", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -NoClobber"));
        Assert.Equal("NoClobber", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -NoClobber -WhatIf"));

        Run($"@() | Export-ProbedCsv -LiteralPath '{file.Path}' -Append");
        Assert.Equal("\"A\",\"B\"\r\n1,2", Ascii(file));

        using var missing = ByteExactFile.CreateMissing();
        Run($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{missing.Path}' -WhatIf");
        Assert.False(missing.Exists);

        Run($"@() | Export-ProbedCsv -LiteralPath '{missing.Path}'");
        Assert.Empty(missing.ReadBytes());

        Run($"@() | Export-ProbedCsv -LiteralPath '{file.Path}'");
        Assert.Empty(file.ReadBytes());
    }

    /// <summary>
    /// パスはちょうど 1 件に解決されること。ワイルドカードが複数・0 件、親ディレクトリが無い場合は終了エラー。
    /// </summary>
    [Fact]
    public void Other_PathResolution()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("w1.csv", BaseCsv);
        directory.Write("w2.csv", BaseCsv);

        Assert.Equal("MultipleFilesNotSupported", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -Path '{directory.Combine("w*.csv")}'"));
        Assert.Equal("FileNotFound", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -Path '{directory.Combine("zz*.csv")}'"));
        Assert.Equal("ParentDirectoryNotFound", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{directory.Combine("no\\such\\x.csv")}'"));
        Assert.Equal("PathIsNotFile", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{directory.Path}'"));
    }

    // ─── エラー（仕様書 20 章） ──────────────────────────────────────────────

    /// <summary>
    /// パラメータの検査の終了エラー。-Append なしの -AllowEncodingChange は警告。
    /// </summary>
    [Fact]
    public void Errors_ParameterChecks()
    {
        using var file = ByteExactFile.CreateMissing();

        Assert.Equal("CannotSpecifyQuoteFieldsAndUseQuotes", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -QuoteFields A -UseQuotes Always"));
        Assert.Equal("CannotSpecifyPathAndLiteralPath", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -Path '{file.Path}' -LiteralPath '{file.Path}'"));
        Assert.Equal("CannotSpecifyPathAndLiteralPath", ErrorIdOf("[pscustomobject]@{ A = 1 } | Export-ProbedCsv"));
        Assert.Equal("InvalidCulture", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Culture xx-INVALID-yy"));
        Assert.Equal("InvalidStrategy", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -Strategy Nope"));
        Assert.Equal("ParameterArgumentValidationError", ErrorIdOf($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -DetectionLineCount 0"));
        Assert.False(file.Exists);

        InvocationResult warned = Run($"[pscustomobject]@{{ A = 1 }} | Export-ProbedCsv -LiteralPath '{file.Path}' -AllowEncodingChange");
        Assert.Single(warned.Warnings);
        Assert.Equal("\"A\"\r\n\"1\"\r\n", Ascii(file));
    }

    // ─── コマンドの形 ───────────────────────────────────────────────────────

    /// <summary>
    /// パラメータセットは標準の Export-Csv と同じ 2 つ（Delimiter が既定 / UseCulture。第三次で改めた）。
    /// -Path / -LiteralPath はどちらのセットにもあり必須ではない。-Encoding は位置パラメータではない。
    /// -InputObject は ByValue だけで ByPropertyName を持たない。別名は仕様書 15 章のとおり。
    /// </summary>
    [Fact]
    public void Metadata_Parameters()
    {
        var metadata = new CommandMetadata(typeof(ExportProbedCsvCommand));

        Assert.Equal("Delimiter", metadata.DefaultParameterSetName);
        Assert.True(metadata.SupportsShouldProcess);
        Assert.Equal(0, metadata.Parameters["Path"].ParameterSets["__AllParameterSets"].Position);
        Assert.False(metadata.Parameters["Path"].ParameterSets["__AllParameterSets"].IsMandatory);
        Assert.False(metadata.Parameters["LiteralPath"].ParameterSets["__AllParameterSets"].IsMandatory);
        Assert.Equal(1, metadata.Parameters["Delimiter"].ParameterSets["Delimiter"].Position);
        Assert.False(metadata.Parameters["UseCulture"].ParameterSets["UseCulture"].IsMandatory);
        Assert.Equal(int.MinValue, metadata.Parameters["Encoding"].ParameterSets["__AllParameterSets"].Position);
        Assert.True(metadata.Parameters["InputObject"].ParameterSets["__AllParameterSets"].ValueFromPipeline);
        Assert.False(metadata.Parameters["InputObject"].ParameterSets["__AllParameterSets"].ValueFromPipelineByPropertyName);
        Assert.Equal(new[] { "PSPath", "LP" }, metadata.Parameters["LiteralPath"].Aliases);
        Assert.Equal(new[] { "NoOverwrite" }, metadata.Parameters["NoClobber"].Aliases);
        Assert.Equal(new[] { "ITI" }, metadata.Parameters["IncludeTypeInformation"].Aliases);
        Assert.Equal(new[] { "NTI" }, metadata.Parameters["NoTypeInformation"].Aliases);
        Assert.Equal(new[] { "UQ" }, metadata.Parameters["UseQuotes"].Aliases);
        Assert.Equal(new[] { "QF" }, metadata.Parameters["QuoteFields"].Aliases);
        Assert.Equal(typeof(CsvQuoteKind), metadata.Parameters["UseQuotes"].ParameterType);
    }

    // ─── 補助 ───────────────────────────────────────────────────────────────

    /// <summary>先頭 2 行が ASCII で、3 行目が「日本,語」の Shift_JIS の CSV（ヘッダー A,B）</summary>
    private static byte[] ShiftJisCsv()
        => ByteExactFile.Concat(BaseCsv, Hex("93 FA 96 7B 2C 8C EA 0D 0A"));

    private InvocationResult Run(string script) => this._fixture.InvokeScriptCapturingErrors(script);

    /// <summary>
    /// カルチャーを切り替えてスクリプトを実行する（日付・数値・リスト区切り文字の書式のため）
    /// </summary>
    private InvocationResult RunWithCulture(string culture, string script)
        => Run(
            "$saved = [System.Threading.Thread]::CurrentThread.CurrentCulture; "
            + $"try {{ [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('{culture}'); {script} }} "
            + "finally { [System.Threading.Thread]::CurrentThread.CurrentCulture = $saved }");

    /// <summary>
    /// スクリプトを実行し、最初のエラーの ID を返す（終了エラーも非終了エラーも。エラーが無ければ空文字列）
    /// </summary>
    private string ErrorIdOf(string script)
    {
        InvocationResult result = Run(
            $"try {{ {script} -ErrorAction Stop; '' }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        return result.Output.Count == 0 ? string.Empty : (string)result.Output[result.Output.Count - 1].BaseObject;
    }

    private static string Ascii(ByteExactFile file) => Encoding.ASCII.GetString(file.ReadBytes());

    private static byte[] Hex(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b, 16)).ToArray();
}
