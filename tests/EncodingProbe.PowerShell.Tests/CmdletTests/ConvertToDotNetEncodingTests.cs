using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// ConvertTo-DotNetEncoding の仕様（仕様書 7 節）を検証するテスト。
/// </summary>
public class ConvertToDotNetEncodingTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string CommandName = "ConvertTo-DotNetEncoding";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ConvertToDotNetEncodingTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    #region 入力の多形性（仕様書 7.2）

    /// <summary>
    /// 統一語彙名から、BOM方針を反映した Encoding が得られること。
    /// </summary>
    [Theory]
    [InlineData("utf8NoBOM", 65001, new byte[0])]
    [InlineData("utf8BOM", 65001, new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData("unicode", 1200, new byte[] { 0xFF, 0xFE })]
    [InlineData("unicodeNoBOM", 1200, new byte[0])]
    [InlineData("bigendianunicodeBOM", 1201, new byte[] { 0xFE, 0xFF })]
    [InlineData("utf32NoBOM", 12000, new byte[0])]
    [InlineData("bigendianutf32BOM", 12001, new byte[] { 0x00, 0x00, 0xFE, 0xFF })]
    [InlineData("ascii", 20127, new byte[0])]
    public void Convert_UnifiedVocabulary_ReturnsEncodingWithExpectedPreamble(
        string name, int expectedCodePage, byte[] expectedPreamble)
    {
        Encoding encoding = InvokeSingle(name);

        Assert.Equal(expectedCodePage, encoding.CodePage);
        Assert.Equal(expectedPreamble, encoding.GetPreamble());
    }

    /// <summary>
    /// WebName および .NET の別名から解決できること。
    /// </summary>
    [Theory]
    [InlineData("shift_jis", 932)]
    [InlineData("shift-jis", 932)]
    [InlineData("sjis", 932)]
    [InlineData("ms_kanji", 932)]
    [InlineData("euc-jp", 51932)]
    [InlineData("iso-2022-jp", 50220)]
    [InlineData("big5", 950)]
    public void Convert_WebName_ResolvesToCodePage(string name, int expectedCodePage)
    {
        Assert.Equal(expectedCodePage, InvokeSingle(name).CodePage);
    }

    /// <summary>
    /// コードページ数値から解決できること。数値は文字列で渡しても解決される。
    /// </summary>
    [Theory]
    [InlineData(932)]
    [InlineData(65001)]
    [InlineData(51932)]
    public void Convert_NumericCodePage_ResolvesToCodePage(int codePage)
    {
        Assert.Equal(codePage, InvokeSingle(codePage).CodePage);
        Assert.Equal(codePage, InvokeSingle(codePage.ToString()).CodePage);
    }

    /// <summary>
    /// System.Text.Encoding インスタンスを渡した場合、その GetPreamble() が尊重されること。
    /// Encoding.GetEncoding(65001) はBOM付きインスタンスであるため、BOM付きのまま返る。
    /// これは .NET の仕様に由来する挙動であり、ヘルプに注意事項として明記する対象である。
    /// </summary>
    [Fact]
    public void Convert_EncodingInstance_RespectsItsPreamble()
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, InvokeSingle(Encoding.GetEncoding(65001)).GetPreamble());
        Assert.Empty(InvokeSingle(new UTF8Encoding(false)).GetPreamble());
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, InvokeSingle(new UTF8Encoding(true)).GetPreamble());
    }

    /// <summary>
    /// UTF-7 は読み取り用途として解決できること。
    /// .NET 5 以降は Encoding.GetEncoding から取得できないため、
    /// PowerShell 5.1 と 7.x で同じ結果になることの確認を兼ねる。
    /// </summary>
    [Theory]
    [InlineData("utf7")]
    [InlineData("utf-7")]
    [InlineData("65000")]
    public void Convert_Utf7_IsResolvable(string name)
    {
        Assert.Equal(65000, InvokeSingle(name).CodePage);
    }

    #endregion

    #region 判定結果からの変換とパイプライン（仕様書 7.2 / 7.3）

    /// <summary>
    /// Resolve-Encoding の戻り値から、BOMの有無を反映した Encoding が得られること。
    /// </summary>
    [Fact]
    public void Convert_EncodingInformation_ReflectsDetectedBom()
    {
        using var withBom = ByteExactFile.CreateFrom(
            ByteExactFile.Utf8Bom,
            Encoding.ASCII.GetBytes("abc\r\n"));

        Collection<PSObject> detected = this._fixture.Invoke(
            "Resolve-Encoding", new Dictionary<string, object?> { ["Path"] = withBom.Path });

        Encoding encoding = InvokeSingle(detected[0].BaseObject);

        Assert.Equal(65001, encoding.CodePage);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, encoding.GetPreamble());
    }

    /// <summary>
    /// BOM無しと判定されたファイルからは、BOMを出力しない Encoding が得られること。
    /// </summary>
    [Fact]
    public void Convert_EncodingInformationWithoutBom_DoesNotEmitBom()
    {
        using var withoutBom = ByteExactFile.Create(
            ByteExactFile.Encode(new UTF8Encoding(false), "日本語テキスト\r\n"));

        Collection<PSObject> detected = this._fixture.Invoke(
            "Resolve-Encoding", new Dictionary<string, object?> { ["Path"] = withoutBom.Path });

        Encoding encoding = InvokeSingle(detected[0].BaseObject);

        Assert.Equal(65001, encoding.CodePage);
        Assert.Empty(encoding.GetPreamble());
    }

    /// <summary>
    /// Resolve-Encoding からのパイプライン入力に対応すること（仕様書 7.3）。
    /// </summary>
    [Fact]
    public void Convert_AcceptsPipelineInputFromResolveEncoding()
    {
        using var file = ByteExactFile.CreateFrom(
            ByteExactFile.Utf8Bom,
            Encoding.ASCII.GetBytes("abc\r\n"));

        Collection<PSObject> results = this._fixture.InvokeScript(
            $"Resolve-Encoding -Path '{file.Path}' | ConvertTo-DotNetEncoding");

        var encoding = Assert.IsAssignableFrom<Encoding>(Assert.Single(results).BaseObject);

        Assert.Equal(65001, encoding.CodePage);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, encoding.GetPreamble());
    }

    /// <summary>
    /// 語彙名のパイプライン入力にも対応すること。
    /// </summary>
    [Fact]
    public void Convert_AcceptsPipelineInputFromVocabularyName()
    {
        Collection<PSObject> results = this._fixture.InvokeScript(
            "'utf8BOM','shift_jis' | ConvertTo-DotNetEncoding");

        Assert.Equal(2, results.Count);
        Assert.Equal(65001, ((Encoding)results[0].BaseObject).CodePage);
        Assert.Equal(932, ((Encoding)results[1].BaseObject).CodePage);
    }

    #endregion

    #region エラー方針（仕様書 7.5 / 8）

    /// <summary>
    /// Auto はエラーになり、正規形（Resolve-Encoding からのパイプライン）へ誘導すること。
    /// </summary>
    [Fact]
    public void Convert_Auto_ThrowsWithGuidance()
    {
        var exception = Assert.ThrowsAny<ParameterBindingException>(() => InvokeSingle("Auto"));

        // 引数変換の段階で失敗していること
        Assert.IsType<ArgumentTransformationMetadataException>(exception.InnerException);
        Assert.Contains(ValidationMessages.AutoNotAllowedForConvert(), exception.Message);
    }

    /// <summary>
    /// エラーはパラメータ束縛の段階で発生すること。
    /// </summary>
    [Theory]
    [InlineData("nonexistent-encoding")]
    [InlineData("ansiBOM")]
    [InlineData("asciiNoBOM")]
    [InlineData("932BOM")]
    public void Convert_InvalidVocabulary_FailsAtParameterBinding(string name)
    {
        var exception = Assert.ThrowsAny<ParameterBindingException>(() => InvokeSingle(name));

        // ファイルを開く前（引数変換の段階）で失敗していること
        Assert.IsType<ArgumentTransformationMetadataException>(exception.InnerException);
    }

    /// <summary>
    /// 書き込み用途ではないため、BOM方針が未指定の指定も受け付けること（原則A）。
    /// </summary>
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public void Convert_UnspecifiedBomPolicy_IsAccepted(string name)
    {
        Encoding encoding = InvokeSingle(name);

        // BOM方針が未指定の場合、実体はBOM無しで組み立てられる
        Assert.Empty(encoding.GetPreamble());
    }

    #endregion

    /// <summary>
    /// コマンドを位置指定引数で実行し、単一の Encoding を取り出す
    /// </summary>
    private Encoding InvokeSingle(object? argument)
    {
        Collection<PSObject> results = this._fixture.InvokeWithArgument(CommandName, argument);

        return Assert.IsAssignableFrom<Encoding>(Assert.Single(results).BaseObject);
    }
}