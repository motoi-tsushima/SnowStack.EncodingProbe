using System.Globalization;
using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// 統一語彙（EncodingVocabulary）の解決仕様を検証するテスト。
/// </summary>
/// <remarks>
/// 語彙解決は Get-ProbedContent / Set-ProbedContent / Add-ProbedContent /
/// ConvertTo-DotNetEncoding の全コマンドが共有する土台であるため、ここを厚く検証する。
/// </remarks>
public class EncodingVocabularyTests
{
    /// <summary>コードページ：UTF-8</summary>
    private const int Utf8 = 65001;

    /// <summary>コードページ：UTF-16 リトルエンディアン</summary>
    private const int Utf16Le = 1200;

    /// <summary>コードページ：UTF-16 ビッグエンディアン</summary>
    private const int Utf16Be = 1201;

    /// <summary>コードページ：UTF-32 リトルエンディアン</summary>
    private const int Utf32Le = 12000;

    /// <summary>コードページ：UTF-32 ビッグエンディアン</summary>
    private const int Utf32Be = 12001;

    /// <summary>コードページ：US-ASCII</summary>
    private const int Ascii = 20127;

    /// <summary>コードページ：UTF-7</summary>
    private const int Utf7 = 65000;

    /// <summary>コードページ：Shift-JIS</summary>
    private const int ShiftJis = 932;

    #region 語彙一覧（仕様書 3.3）

    /// <summary>
    /// 独自拡張名（*BOM / *NoBOM）が、正しいコードページとBOM方針に解決されること。
    /// </summary>
    [Theory]
    [InlineData("utf8BOM", Utf8, true)]
    [InlineData("utf8NoBOM", Utf8, false)]
    [InlineData("unicodeBOM", Utf16Le, true)]
    [InlineData("unicodeNoBOM", Utf16Le, false)]
    [InlineData("bigendianunicodeBOM", Utf16Be, true)]
    [InlineData("bigendianunicodeNoBOM", Utf16Be, false)]
    [InlineData("utf32BOM", Utf32Le, true)]
    [InlineData("utf32NoBOM", Utf32Le, false)]
    [InlineData("bigendianutf32BOM", Utf32Be, true)]
    [InlineData("bigendianutf32NoBOM", Utf32Be, false)]
    public void Resolve_ExtendedName_ResolvesToCodePageAndBomPolicy(
        string name, int expectedCodePage, bool expectedEmitBom)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.False(spec.IsAuto);
        Assert.Equal(expectedCodePage, spec.Encoding!.CodePage);
        Assert.Equal(expectedEmitBom, spec.EmitBom);
    }

    /// <summary>
    /// PowerShell 7.x のフレンドリ名（裸名）が解決されること。
    /// 裸名のBOM方針は「utf8 のみBOM無し（ただし未指定扱い）、他のUnicode系はBOM有り」という
    /// PowerShell 7.x の不規則な体系を受け継ぐ。
    /// </summary>
    [Theory]
    [InlineData("unicode", Utf16Le, true)]
    [InlineData("bigendianunicode", Utf16Be, true)]
    [InlineData("utf32", Utf32Le, true)]
    [InlineData("bigendianutf32", Utf32Be, true)]
    [InlineData("ascii", Ascii, false)]
    [InlineData("utf7", Utf7, false)]
    public void Resolve_BareName_ResolvesToCodePageAndBomPolicy(
        string name, int expectedCodePage, bool expectedEmitBom)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.Equal(expectedCodePage, spec.Encoding!.CodePage);
        Assert.Equal(expectedEmitBom, spec.EmitBom);
    }

    /// <summary>
    /// 裸の utf8 は、PowerShell 5.1 と 7.x で意味が衝突する唯一の語彙であるため、
    /// BOM方針を「未指定」として解決されること。
    /// </summary>
    [Fact]
    public void Resolve_BareUtf8_LeavesBomPolicyUnspecified()
    {
        var spec = EncodingVocabulary.Resolve("utf8", EncodingUsage.Read);

        Assert.Equal(Utf8, spec.Encoding!.CodePage);
        Assert.Null(spec.EmitBom);
        Assert.True(spec.IsBomPolicyUnspecified);
    }

    /// <summary>
    /// ansi / oem が実行環境のコードページに解決されること。
    /// </summary>
    [Fact]
    public void Resolve_AnsiAndOem_ResolveToEnvironmentCodePages()
    {
        var ansi = EncodingVocabulary.Resolve("ansi", EncodingUsage.Read);
        var oem = EncodingVocabulary.Resolve("oem", EncodingUsage.Read);

        Assert.Equal(CultureInfo.CurrentCulture.TextInfo.ANSICodePage, ansi.Encoding!.CodePage);
        Assert.Equal(CultureInfo.CurrentCulture.TextInfo.OEMCodePage, oem.Encoding!.CodePage);

        // 非Unicode系はBOM出力なしで固定される
        Assert.False(ansi.EmitBom);
        Assert.False(oem.EmitBom);
    }

    /// <summary>
    /// 語彙の解決は大文字小文字を区別しないこと。
    /// </summary>
    [Theory]
    [InlineData("UTF8NOBOM", Utf8)]
    [InlineData("Utf8NoBom", Utf8)]
    [InlineData("uTf8bOm", Utf8)]
    [InlineData("ASCII", Ascii)]
    [InlineData("Ansi", 0)]
    [InlineData("UNICODEnobom", Utf16Le)]
    [InlineData("SHIFT_JIS", ShiftJis)]
    public void Resolve_IsCaseInsensitive(string name, int expectedCodePage)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        int expected = expectedCodePage == 0
            ? CultureInfo.CurrentCulture.TextInfo.ANSICodePage
            : expectedCodePage;

        Assert.Equal(expected, spec.Encoding!.CodePage);
    }

    /// <summary>
    /// Auto が大文字小文字を問わず解決されること。
    /// </summary>
    [Theory]
    [InlineData("Auto")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void Resolve_Auto_ReturnsAutoMarker(string name)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.True(spec.IsAuto);
        Assert.Null(spec.Encoding);
    }

    #endregion

    #region BOM方針の決定規則（仕様書 3.5）

    /// <summary>
    /// Unicode系がコンストラクタで組み立てられ、GetPreamble() が語彙側のBOM方針と一致すること。
    /// Encoding.GetEncoding("utf-8") / Encoding.UTF8 はBOM付きインスタンスを返すため、
    /// この検証が BOM 方針の実効性を担保する。
    /// </summary>
    [Theory]
    [InlineData("utf8NoBOM", new byte[0])]
    [InlineData("utf8BOM", new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData("unicodeNoBOM", new byte[0])]
    [InlineData("unicodeBOM", new byte[] { 0xFF, 0xFE })]
    [InlineData("bigendianunicodeNoBOM", new byte[0])]
    [InlineData("bigendianunicodeBOM", new byte[] { 0xFE, 0xFF })]
    [InlineData("utf32NoBOM", new byte[0])]
    [InlineData("utf32BOM", new byte[] { 0xFF, 0xFE, 0x00, 0x00 })]
    [InlineData("bigendianutf32NoBOM", new byte[0])]
    [InlineData("bigendianutf32BOM", new byte[] { 0x00, 0x00, 0xFE, 0xFF })]
    public void Resolve_UnicodeVocabulary_PreambleMatchesBomPolicy(string name, byte[] expectedPreamble)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.Equal(expectedPreamble, spec.Encoding!.GetPreamble());
    }

    /// <summary>
    /// 裸名 unicode / utf32 系は BOM 有りとして解決されるため、preamble を持つこと。
    /// </summary>
    [Theory]
    [InlineData("unicode", new byte[] { 0xFF, 0xFE })]
    [InlineData("bigendianunicode", new byte[] { 0xFE, 0xFF })]
    [InlineData("utf32", new byte[] { 0xFF, 0xFE, 0x00, 0x00 })]
    [InlineData("bigendianutf32", new byte[] { 0x00, 0x00, 0xFE, 0xFF })]
    public void Resolve_BareUnicodeNames_EmitBomByDefault(string name, byte[] expectedPreamble)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.Equal(expectedPreamble, spec.Encoding!.GetPreamble());
    }

    /// <summary>
    /// WebName 経路で解決されたUnicode系は、BOM方針が未指定になり、
    /// かつ実体は BOM 無しで組み立てられること（GetPreamble() に依存しない）。
    /// </summary>
    [Theory]
    [InlineData("utf-8", Utf8)]
    [InlineData("utf-16", Utf16Le)]
    [InlineData("utf-16BE", Utf16Be)]
    [InlineData("utf-32", Utf32Le)]
    public void Resolve_WebNameUnicode_LeavesBomPolicyUnspecified(string name, int expectedCodePage)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.Equal(expectedCodePage, spec.Encoding!.CodePage);
        Assert.Null(spec.EmitBom);
        Assert.Empty(spec.Encoding.GetPreamble());
    }

    /// <summary>
    /// 数値コードページ経路で解決されたUnicode系も、BOM方針が未指定になること。
    /// </summary>
    [Theory]
    [InlineData(Utf8)]
    [InlineData(Utf16Le)]
    [InlineData(Utf16Be)]
    [InlineData(Utf32Le)]
    [InlineData(Utf32Be)]
    public void Resolve_NumericUnicodeCodePage_LeavesBomPolicyUnspecified(int codePage)
    {
        var spec = EncodingVocabulary.Resolve(codePage, EncodingUsage.Read);

        Assert.Equal(codePage, spec.Encoding!.CodePage);
        Assert.Null(spec.EmitBom);
        Assert.Empty(spec.Encoding.GetPreamble());
    }

    /// <summary>
    /// 非Unicode系はBOM出力なしで固定されること。
    /// </summary>
    [Theory]
    [InlineData("shift_jis")]
    [InlineData("euc-jp")]
    [InlineData("iso-2022-jp")]
    [InlineData("ascii")]
    public void Resolve_NonUnicode_NeverEmitsBom(string name)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.False(spec.EmitBom);
        Assert.Empty(spec.Encoding!.GetPreamble());
    }

    #endregion

    #region 解決順序（仕様書 3.4）

    /// <summary>
    /// .NET の変換表が持つ別名が、自前の別名表を持たずに解決されること（経路5）。
    /// </summary>
    [Theory]
    [InlineData("shift-jis", ShiftJis)]
    [InlineData("sjis", ShiftJis)]
    [InlineData("ms_kanji", ShiftJis)]
    [InlineData("shift_jis", ShiftJis)]
    [InlineData("euc-jp", 51932)]
    [InlineData("iso-2022-jp", 50220)]
    [InlineData("big5", 950)]
    [InlineData("gb2312", 936)]
    public void Resolve_DotNetAliases_AreDelegatedToGetEncoding(string name, int expectedCodePage)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.Equal(expectedCodePage, spec.Encoding!.CodePage);
    }

    /// <summary>
    /// 本モジュールが定義した語彙が .NET の別名より優先されること。
    /// "unicode" は .NET でも CP1200 に解決されるが、BOM方針は本モジュール側の規則
    /// （裸名 unicode は BOM 有り）が採用される。
    /// </summary>
    [Fact]
    public void Resolve_ModuleVocabulary_TakesPrecedenceOverDotNetAlias()
    {
        var moduleVocabulary = EncodingVocabulary.Resolve("unicode", EncodingUsage.Read);
        var webName = EncodingVocabulary.Resolve("utf-16", EncodingUsage.Read);

        // どちらも CP1200 だが、BOM方針の決まり方が異なる
        Assert.Equal(Utf16Le, moduleVocabulary.Encoding!.CodePage);
        Assert.Equal(Utf16Le, webName.Encoding!.CodePage);
        Assert.True(moduleVocabulary.EmitBom);
        Assert.Null(webName.EmitBom);
    }

    /// <summary>
    /// 数値は文字列として渡されても、GetEncoding(string) ではなく GetEncoding(int) に解決されること。
    /// 経路4を経路5より前に置くことで、"65001" が文字列として解決に失敗する問題を回避している。
    /// </summary>
    [Theory]
    [InlineData("65001", Utf8)]
    [InlineData("932", ShiftJis)]
    [InlineData("20127", Ascii)]
    [InlineData("51932", 51932)]
    public void Resolve_NumericString_IsResolvedAsCodePage(string text, int expectedCodePage)
    {
        var spec = EncodingVocabulary.Resolve(text, EncodingUsage.Read);

        Assert.Equal(expectedCodePage, spec.Encoding!.CodePage);
    }

    /// <summary>
    /// CLRの数値型がそのままコードページとして扱われること。
    /// </summary>
    [Fact]
    public void Resolve_NumericClrTypes_AreResolvedAsCodePage()
    {
        Assert.Equal(ShiftJis, EncodingVocabulary.Resolve(932, EncodingUsage.Read).Encoding!.CodePage);
        Assert.Equal(ShiftJis, EncodingVocabulary.Resolve(932L, EncodingUsage.Read).Encoding!.CodePage);
        Assert.Equal(ShiftJis, EncodingVocabulary.Resolve((short)932, EncodingUsage.Read).Encoding!.CodePage);
    }

    /// <summary>
    /// PSObject に包まれた値が解決されること。
    /// </summary>
    [Fact]
    public void Resolve_PSObject_IsUnwrapped()
    {
        var spec = EncodingVocabulary.Resolve(PSObject.AsPSObject("utf8NoBOM"), EncodingUsage.Read);

        Assert.Equal(Utf8, spec.Encoding!.CodePage);
        Assert.False(spec.EmitBom);
    }

    /// <summary>
    /// 解決済みの EncodingSpec は、そのまま通されること
    /// （パラメータ束縛が複数回走った場合に備える）。
    /// </summary>
    [Fact]
    public void Resolve_AlreadyResolvedSpec_IsPassedThrough()
    {
        var first = EncodingVocabulary.Resolve("utf8BOM", EncodingUsage.Read);
        var second = EncodingVocabulary.Resolve(first, EncodingUsage.Read);

        Assert.Same(first, second);
    }

    #endregion

    #region 型による分岐（仕様書 3.4 経路6 / 3.5 例外）

    /// <summary>
    /// System.Text.Encoding インスタンスを直接渡した場合のみ、
    /// そのインスタンスの GetPreamble() が尊重されること。
    /// </summary>
    [Fact]
    public void Resolve_EncodingInstance_RespectsItsPreamble()
    {
        var withBom = EncodingVocabulary.Resolve(new UTF8Encoding(true), EncodingUsage.Read);
        var withoutBom = EncodingVocabulary.Resolve(new UTF8Encoding(false), EncodingUsage.Read);

        Assert.True(withBom.EmitBom);
        Assert.False(withoutBom.EmitBom);
    }

    /// <summary>
    /// Encoding.GetEncoding(65001) を渡すとBOM付きとして扱われること。
    /// これは .NET の仕様に由来する挙動であり、ヘルプに注意事項として明記する対象である。
    /// </summary>
    [Fact]
    public void Resolve_GetEncodingInstance_IsTreatedAsBomEmitting()
    {
        var spec = EncodingVocabulary.Resolve(Encoding.GetEncoding(65001), EncodingUsage.Read);

        Assert.True(spec.EmitBom);

        // BOM方針が定まっているため、書き込み用途でも拒否されない
        var writeSpec = EncodingVocabulary.Resolve(Encoding.GetEncoding(65001), EncodingUsage.Write);
        Assert.True(writeSpec.EmitBom);
    }

    /// <summary>
    /// EncodingInformation を渡した場合、エンコーディング・BOM・改行の3点が継承されること。
    /// </summary>
    [Fact]
    public void Resolve_EncodingInformation_InheritsEncodingBomAndLineBreak()
    {
        // BOM付きUTF-8 / CR-LF のバイト列を明示して用意する
        byte[] bytes = { 0xEF, 0xBB, 0xBF, 0x61, 0x0D, 0x0A, 0x62, 0x0D, 0x0A };
        EncodingInformation information = SnowStack.EncodingProbe.EncodingProbe.Detect(bytes);

        var spec = EncodingVocabulary.Resolve(information, EncodingUsage.Read);

        Assert.Equal(Utf8, spec.Encoding!.CodePage);
        Assert.True(spec.EmitBom);
        Assert.Equal(LineBreakType.CrLf, spec.LineBreak);

        // BOM方針が反映されたインスタンスであること
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, spec.Encoding.GetPreamble());
    }

    /// <summary>
    /// BOM無しの判定結果からは、BOMを出力しないインスタンスが組み立てられること。
    /// </summary>
    [Fact]
    public void Resolve_EncodingInformationWithoutBom_DoesNotEmitBom()
    {
        // BOM無しUTF-16LE / LF のバイト列（"ＡＢＣ\nＤＥＦ\n"）。
        // ASCII範囲の文字だけで構成すると全バイトが 0x80 未満になり US-ASCII と判定されるため、
        // 上位バイトが 0x80 以上になる全角文字（U+FF21..）を使う。
        byte[] bytes =
        {
            0x21, 0xFF, 0x22, 0xFF, 0x23, 0xFF, 0x0A, 0x00,
            0x24, 0xFF, 0x25, 0xFF, 0x26, 0xFF, 0x0A, 0x00,
        };
        EncodingInformation information = SnowStack.EncodingProbe.EncodingProbe.Detect(bytes);

        var spec = EncodingVocabulary.Resolve(information, EncodingUsage.Read);

        Assert.Equal(Utf16Le, spec.Encoding!.CodePage);
        Assert.False(spec.EmitBom);
        Assert.Empty(spec.Encoding.GetPreamble());
        Assert.Equal(LineBreakType.Lf, spec.LineBreak);
    }

    /// <summary>
    /// 判定に失敗した EncodingInformation はエラーになること。
    /// </summary>
    [Fact]
    public void Resolve_UndetectedEncodingInformation_Throws()
    {
        byte[] undetectable = { 0x81, 0xFF, 0x00, 0xFE, 0x93, 0x40, 0xC0, 0x80, 0xED, 0xA0, 0x80 };
        EncodingInformation information = SnowStack.EncodingProbe.EncodingProbe.Detect(undetectable);

        Assert.True(information.CodePage < 0, "テスト前提: このバイト列は判定不能であること");

        Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve(information, EncodingUsage.Read));
    }

    #endregion

    #region BOM接尾辞の誤用（仕様書 3.3 / 8）

    /// <summary>
    /// BOM接尾辞を許さない語彙への接尾辞付き指定がエラーになること。
    /// </summary>
    [Theory]
    [InlineData("ansiBOM", "ansi")]
    [InlineData("ansiNoBOM", "ansi")]
    [InlineData("oemBOM", "oem")]
    [InlineData("oemNoBOM", "oem")]
    [InlineData("asciiBOM", "ascii")]
    [InlineData("asciiNoBOM", "ascii")]
    [InlineData("utf7BOM", "utf7")]
    [InlineData("utf7NoBOM", "utf7")]
    [InlineData("shift_jisBOM", "shift_jis")]
    [InlineData("shift_jisNoBOM", "shift_jis")]
    [InlineData("932BOM", "932")]
    [InlineData("932NoBOM", "932")]
    [InlineData("euc-jpBOM", "euc-jp")]
    [InlineData("utf-7BOM", "utf-7")]
    [InlineData("65000BOM", "65000")]
    public void Resolve_BomSuffixOnDisallowedVocabulary_Throws(string name, string expectedStem)
    {
        var exception = Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve(name, EncodingUsage.Read));

        // メッセージは実行環境の言語に応じて変わるため、カタログと照合して検証する。
        // これにより、正しいメッセージが選ばれていることと、語幹の切り出しが正しいことを
        // 同時に検証できる。
        Assert.Equal(ValidationMessages.BomSuffixNotAllowed(expectedStem), exception.Message);
    }

    /// <summary>
    /// 語幹が語彙として解決できない場合は、BOM接尾辞のエラーではなく
    /// 「解決できない」エラーになること。
    /// </summary>
    [Fact]
    public void Resolve_UnknownStemWithBomSuffix_ReportsUnknownEncoding()
    {
        var exception = Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve("nonexistentBOM", EncodingUsage.Read));

        Assert.Equal(ValidationMessages.UnknownEncoding("nonexistentBOM"), exception.Message);
    }

    #endregion

    #region 用途による制限（仕様書 3.6 / 8）

    /// <summary>
    /// 読み取り用途では、BOM方針が未指定の指定を許容すること（原則A）。
    /// </summary>
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf-8")]
    [InlineData("65001")]
    [InlineData("utf-16")]
    [InlineData("utf7")]
    [InlineData("utf-7")]
    public void Resolve_ForRead_AcceptsUnspecifiedBomPolicyAndUtf7(string name)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Read);

        Assert.NotNull(spec.Encoding);
    }

    /// <summary>
    /// 書き込み用途で裸の utf8 が拒否され、代替候補がメッセージに含まれること。
    /// </summary>
    [Fact]
    public void Resolve_ForWrite_RejectsBareUtf8WithSuggestions()
    {
        var exception = Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve("utf8", EncodingUsage.Write));

        Assert.Contains("utf8NoBOM", exception.Message);
        Assert.Contains("utf8BOM", exception.Message);
    }

    /// <summary>
    /// 書き込み用途で、WebName / 数値コードページ経由のUnicode系が拒否され、
    /// 代替候補がメッセージに含まれること。
    /// </summary>
    [Theory]
    [InlineData("utf-8", "utf8NoBOM", "utf8BOM")]
    [InlineData("65001", "utf8NoBOM", "utf8BOM")]
    [InlineData("utf-16", "unicodeNoBOM", "unicodeBOM")]
    [InlineData("1200", "unicodeNoBOM", "unicodeBOM")]
    [InlineData("utf-16BE", "bigendianunicodeNoBOM", "bigendianunicodeBOM")]
    [InlineData("utf-32", "utf32NoBOM", "utf32BOM")]
    [InlineData("12001", "bigendianutf32NoBOM", "bigendianutf32BOM")]
    public void Resolve_ForWrite_RejectsUnspecifiedBomPolicy(
        string name, string expectedNoBomSuggestion, string expectedBomSuggestion)
    {
        var exception = Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve(name, EncodingUsage.Write));

        Assert.Contains(expectedNoBomSuggestion, exception.Message);
        Assert.Contains(expectedBomSuggestion, exception.Message);
    }

    /// <summary>
    /// 書き込み用途で utf7 が拒否されること。
    /// </summary>
    [Theory]
    [InlineData("utf7")]
    [InlineData("utf-7")]
    [InlineData("65000")]
    public void Resolve_ForWrite_RejectsUtf7(string name)
    {
        var exception = Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve(name, EncodingUsage.Write));

        Assert.Contains("UTF-7", exception.Message);
    }

    /// <summary>
    /// 書き込み用途でも、BOM方針が明示された語彙は受け付けられること。
    /// </summary>
    [Theory]
    [InlineData("utf8NoBOM")]
    [InlineData("utf8BOM")]
    [InlineData("unicode")]
    [InlineData("unicodeNoBOM")]
    [InlineData("shift_jis")]
    [InlineData("932")]
    [InlineData("ascii")]
    [InlineData("ansi")]
    [InlineData("oem")]
    [InlineData("iso-2022-jp")]
    public void Resolve_ForWrite_AcceptsExplicitBomPolicy(string name)
    {
        var spec = EncodingVocabulary.Resolve(name, EncodingUsage.Write);

        Assert.NotNull(spec.Encoding);
        Assert.NotNull(spec.EmitBom);
    }

    /// <summary>
    /// Auto は書き込み用途でも有効であること（書き込み先から継承するため）。
    /// </summary>
    [Fact]
    public void Resolve_ForWrite_AcceptsAuto()
    {
        var spec = EncodingVocabulary.Resolve("Auto", EncodingUsage.Write);

        Assert.True(spec.IsAuto);
    }

    /// <summary>
    /// allowAuto が false の場合、Auto がエラーになり、
    /// メッセージが正規形（Resolve-Encoding からのパイプライン）へ誘導すること。
    /// </summary>
    [Fact]
    public void Resolve_AutoWhenNotAllowed_ThrowsWithGuidance()
    {
        var exception = Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve("Auto", EncodingUsage.Read, allowAuto: false));

        Assert.Contains("Resolve-Encoding", exception.Message);
        Assert.Contains("ConvertTo-DotNetEncoding", exception.Message);
    }

    #endregion

    #region 不正な入力

    /// <summary>
    /// 解決できない語彙がエラーになること。
    /// </summary>
    [Theory]
    [InlineData("nonexistent-encoding")]
    [InlineData("utf99")]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_UnknownVocabulary_Throws(string name)
    {
        Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve(name, EncodingUsage.Read));
    }

    /// <summary>
    /// null がエラーになること。
    /// </summary>
    [Fact]
    public void Resolve_Null_Throws()
    {
        Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve(null, EncodingUsage.Read));
    }

    /// <summary>
    /// 存在しないコードページ数値がエラーになること。
    /// </summary>
    [Fact]
    public void Resolve_UnknownCodePage_Throws()
    {
        Assert.Throws<ArgumentTransformationMetadataException>(
            () => EncodingVocabulary.Resolve(999999, EncodingUsage.Read));
    }

    #endregion
}