using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Probed 系コマンドレットの -Culture / -Strategy を検証するテスト。
/// </summary>
/// <remarks>
/// この2つのパラメータは Resolve-Encoding と同じ名前・同じ値を取る。
/// カルチャーを渡せないと、日本語環境で韓国語や中国語のファイルを読んだときに
/// 別のエンコーディングと判定されて文字化けする。これが導入の動機であるため、
/// 「渡せること」ではなく「判定結果が実際に変わること」を検証する。
/// <br/>
/// 実行環境のカルチャーに依存しないよう、比較する2通りはどちらも値を明示する。
/// </remarks>
public class DetectionOptionsTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    /// <summary>EUC-KR（韓国語）</summary>
    private const int EucKrCodePage = 51949;

    /// <summary>CP949（統合完成形。EUC-KR を包含する）</summary>
    private const int Cp949CodePage = 949;

    /// <summary>Big5（繁体字中国語）</summary>
    private const int Big5CodePage = 950;

    /// <summary>Shift-JIS（日本語）</summary>
    private const int ShiftJisCodePage = 932;

    /// <summary>windows-1252（西欧）</summary>
    private const int Windows1252CodePage = 1252;

    /// <summary>ISO-8859-1（西欧。UTF.Unknown が返す判定結果）</summary>
    private const int Latin1CodePage = 28591;

    /// <summary>韓国語のサンプル文字列</summary>
    private const string KoreanText = "안녕하세요. 한국어 텍스트입니다.";

    /// <summary>繁体字中国語のサンプル文字列</summary>
    private const string ChineseTraditionalText = "你好。這是繁體中文的文字檔案。";

    /// <summary>
    /// 西欧のサンプル文字列。UTF.Unknown が信頼度 0.5 を超える判定を返すだけの分量を持たせる。
    /// </summary>
    private const string GermanText =
        "Rundfunk und Fernsehen. Grüße aus München und Köln. Straße, Fuß, Maß, schön.";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public DetectionOptionsTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;

        // テストデータの組み立てにもレガシーコードページが必要となる
        CodePagesProviderRegistration.EnsureRegistered();
    }

    #region -Culture（課題1）

    /// <summary>
    /// -Culture ko-KR を指定すると、EUC-KR のファイルを韓国語として復号できること。
    /// </summary>
    /// <remarks>
    /// EUC-KR のバイト列は EUC-JP としても成立してしまう。日本語環境で -Culture を
    /// 指定せずに読むと EUC-JP と判定され、全文が文字化けする。
    /// </remarks>
    [Fact]
    public void Read_WithKoreanCulture_DecodesEucKrText()
    {
        using var file = ByteExactFile.Create(Encode(EucKrCodePage, KoreanText + "\n"));

        string[] lines = ReadLines(file.Path, culture: "ko-KR");

        Assert.Equal(KoreanText, Assert.Single(lines));
    }

    /// <summary>
    /// 同じバイト列でも -Culture が違えば復号結果が変わること。
    /// </summary>
    /// <remarks>
    /// -Culture が判定処理へ届いていることの証明である。
    /// ja-JP を指定した側が何になるかは判定処理の仕様であり、ここでは問わない。
    /// </remarks>
    [Fact]
    public void Read_SameBytes_DifferentCulture_ProducesDifferentText()
    {
        using var file = ByteExactFile.Create(Encode(EucKrCodePage, KoreanText + "\n"));

        string korean = Assert.Single(ReadLines(file.Path, culture: "ko-KR"));
        string japanese = Assert.Single(ReadLines(file.Path, culture: "ja-JP"));

        Assert.Equal(KoreanText, korean);
        Assert.NotEqual(korean, japanese);
    }

    /// <summary>
    /// -Culture zh-TW を指定すると、Big5 のファイルを繁体字中国語として復号できること。
    /// </summary>
    [Fact]
    public void Read_WithTraditionalChineseCulture_DecodesBig5Text()
    {
        using var file = ByteExactFile.Create(Encode(Big5CodePage, ChineseTraditionalText + "\n"));

        string[] lines = ReadLines(file.Path, culture: "zh-TW");

        Assert.Equal(ChineseTraditionalText, Assert.Single(lines));
    }

    /// <summary>
    /// -Culture ja-JP を指定すると、Shift-JIS のファイルを日本語として復号できること。
    /// </summary>
    [Fact]
    public void Read_WithJapaneseCulture_DecodesShiftJisText()
    {
        const string Text = "日本語のテキストです";

        using var file = ByteExactFile.Create(Encode(ShiftJisCodePage, Text + "\r\n"));

        Assert.Equal(Text, Assert.Single(ReadLines(file.Path, culture: "ja-JP")));
    }

    #endregion

    #region -Strategy（課題2）

    /// <summary>
    /// -Strategy UtfUnknownOnly を指定すると、独自判定ではなく UTF.Unknown の判定に従うこと。
    /// </summary>
    /// <remarks>
    /// 独自判定は東アジアのマルチバイトを担当し、欧米のシングルバイトは UTF.Unknown が担当する。
    /// windows-1252 のテキストを独自判定に任せるとカルチャー既定のマルチバイトと解釈されるが、
    /// UTF.Unknown は ISO-8859-1 と判定する。
    /// </remarks>
    [Fact]
    public void Read_WithUtfUnknownOnly_UsesUtfUnknownResult()
    {
        byte[] content = Encode(Windows1252CodePage, GermanText);

        using var file = ByteExactFile.CreateFrom(content, new byte[] { 0x0A });

        string[] lines = ReadLines(file.Path, strategy: "UtfUnknownOnly");

        // この文面に現れる文字については ISO-8859-1 と windows-1252 のバイト値は同じ
        Assert.Equal(Decode(Latin1CodePage, content), Assert.Single(lines));
    }

    /// <summary>
    /// -Strategy NativeOnly を指定すると、UTF.Unknown へは委譲しないこと。
    /// </summary>
    /// <remarks>
    /// UtfUnknownOnly と同じバイト列で結果が変わることをもって、-Strategy が
    /// 判定処理へ届いていることを示す。
    /// </remarks>
    [Fact]
    public void Read_WithNativeOnly_DoesNotDelegateToUtfUnknown()
    {
        byte[] content = Encode(Windows1252CodePage, GermanText);

        using var file = ByteExactFile.CreateFrom(content, new byte[] { 0x0A });

        string utfUnknown = Assert.Single(ReadLines(file.Path, strategy: "UtfUnknownOnly"));
        string native = Assert.Single(ReadLines(file.Path, culture: "ja-JP", strategy: "NativeOnly"));

        Assert.NotEqual(utfUnknown, native);
    }

    /// <summary>
    /// -Strategy の語彙が Resolve-Encoding と同一であること。
    /// </summary>
    /// <remarks>
    /// 大文字小文字の違い・別名・数値のいずれも、Resolve-Encoding と同じ表で解決される。
    /// </remarks>
    [Theory]
    [InlineData("Combined")]
    [InlineData("combined")]
    [InlineData("default")]
    [InlineData("0")]
    [InlineData("NativeOnly")]
    [InlineData("native")]
    [InlineData("1")]
    [InlineData("UtfUnknownOnly")]
    [InlineData("utfunknown")]
    [InlineData("3")]
    public void Read_StrategyVocabulary_IsAccepted(string strategy)
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x42, 0x0A });

        InvocationResult result = Invoke(file.Path, strategy: strategy);

        Assert.Empty(result.Errors);
        Assert.Equal("AB", Assert.Single(result.AsStrings()));
    }

    #endregion

    #region 不正な値の検証

    /// <summary>
    /// 解釈できないカルチャー名は終了エラーになること。
    /// </summary>
    [Fact]
    public void Read_InvalidCulture_IsTerminatingError()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        var exception = Assert.Throws<CmdletInvocationException>(
            () => Invoke(file.Path, culture: "not a culture!"));

        Assert.Equal("InvalidCulture", exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(
            ValidationMessages.InvalidCulture("not a culture!"),
            exception.ErrorRecord.Exception.Message);
    }

    /// <summary>
    /// 解釈できない判定方式は終了エラーになること。
    /// </summary>
    [Fact]
    public void Read_InvalidStrategy_IsTerminatingError()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        var exception = Assert.Throws<CmdletInvocationException>(
            () => Invoke(file.Path, strategy: "Nonexistent"));

        Assert.Equal("InvalidStrategy", exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(
            ValidationMessages.InvalidStrategy("Nonexistent"),
            exception.ErrorRecord.Exception.Message);
    }

    /// <summary>
    /// 書き込み系コマンドでは、不正な値の検証をファイルを開く前に行うこと。
    /// </summary>
    /// <remarks>
    /// -Encoding の検証と同じ方針である。書きかけの破損ファイルを残さない。
    /// </remarks>
    [Theory]
    [InlineData("Set-ProbedContent", "Strategy", "Nonexistent", "InvalidStrategy")]
    [InlineData("Add-ProbedContent", "Strategy", "Nonexistent", "InvalidStrategy")]
    [InlineData("Set-ProbedContent", "Culture", "not a culture!", "InvalidCulture")]
    [InlineData("Add-ProbedContent", "Culture", "not a culture!", "InvalidCulture")]
    public void Write_InvalidDetectionOption_DoesNotTouchTargetFile(
        string command, string parameter, string value, string expectedErrorId)
    {
        using var file = ByteExactFile.CreateMissing();

        var exception = Assert.Throws<CmdletInvocationException>(
            () => this._fixture.InvokeCapturingErrors(
                command,
                new Dictionary<string, object?>
                {
                    ["LiteralPath"] = new[] { file.Path },
                    ["Value"] = new object[] { "X" },
                    ["Encoding"] = "utf8NoBOM",
                    [parameter] = value,
                }));

        Assert.Equal(expectedErrorId, exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.False(file.Exists);
    }

    #endregion

    #region 書き込み系コマンドへの反映（課題1・課題2）

    /// <summary>
    /// -EncodingFrom の参照ファイルの判定にも -Culture が効くこと。
    /// </summary>
    /// <remarks>
    /// 課題文書が「-EncodingFrom も対象に含む」と明示している経路である。
    /// </remarks>
    [Fact]
    public void Write_EncodingFromWithKoreanCulture_InheritsKoreanEncoding()
    {
        using var reference = ByteExactFile.Create(Encode(EucKrCodePage, KoreanText + "\n"));
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = InvokeWriter("Set-ProbedContent", file.Path, "ko-KR", reference.Path);

        Assert.Empty(result.Errors);

        // EUC-KR は CP949 に包含されるため、判定結果は CP949 になる
        Assert.Equal(Encode(Cp949CodePage, KoreanText + "\n"), file.ReadBytes());
    }

    /// <summary>
    /// -Culture が違えば、同じ参照ファイルから継承する結果も変わること。
    /// </summary>
    /// <remarks>
    /// 課題1 の現象そのものを固定する。日本語カルチャーでは EUC-JP と判定されるため、
    /// 韓国語の文字を EUC-JP で符号化することになり、書き出したバイト列が一致しない。
    /// </remarks>
    [Fact]
    public void Write_EncodingFromWithJapaneseCulture_DoesNotProduceKoreanBytes()
    {
        using var reference = ByteExactFile.Create(Encode(EucKrCodePage, KoreanText + "\n"));
        using var file = ByteExactFile.CreateMissing();

        InvokeWriter("Set-ProbedContent", file.Path, "ja-JP", reference.Path);

        Assert.NotEqual(Encode(Cp949CodePage, KoreanText + "\n"), file.ReadBytes());
    }

    /// <summary>
    /// 追記先からの継承（-Encoding 省略）にも -Culture が効くこと。
    /// </summary>
    [Fact]
    public void Append_InheritsWithCulture_KeepsKoreanEncoding()
    {
        byte[] original = Encode(EucKrCodePage, KoreanText + "\n");

        using var file = ByteExactFile.Create(original);

        InvocationResult result = InvokeWriter("Add-ProbedContent", file.Path, "ko-KR", encodingFrom: null);

        Assert.Empty(result.Errors);
        Assert.Equal(
            ByteExactFile.Concat(original, Encode(Cp949CodePage, KoreanText + "\n")),
            file.ReadBytes());
    }

    #endregion

    #region パラメータの定義（3コマンド共通）

    /// <summary>
    /// 判定処理を内部で呼び出すコマンドレットはすべて -Culture / -Strategy を持つこと。
    /// </summary>
    /// <remarks>
    /// ConvertTo-DotNetEncoding はファイルを引数に取らず判定処理を呼び出さないため対象外。
    /// </remarks>
    [Theory]
    [InlineData(typeof(GetProbedContentCommand))]
    [InlineData(typeof(SetProbedContentCommand))]
    [InlineData(typeof(AddProbedContentCommand))]
    public void Cmdlet_HasCultureAndStrategyParameters(Type cmdletType)
    {
        foreach (string name in new[] { "Culture", "Strategy" })
        {
            System.Reflection.PropertyInfo? property = cmdletType.GetProperty(name);

            Assert.NotNull(property);
            Assert.Equal(typeof(string), property!.PropertyType);
            Assert.NotEmpty(property.GetCustomAttributes(typeof(ParameterAttribute), inherit: false));
        }
    }

    #endregion

    private static byte[] Encode(int codePage, string text)
        => Encoding.GetEncoding(codePage).GetBytes(text);

    private static string Decode(int codePage, byte[] bytes)
        => Encoding.GetEncoding(codePage).GetString(bytes);

    private InvocationResult Invoke(string path, string? culture = null, string? strategy = null)
    {
        var arguments = new Dictionary<string, object?> { ["LiteralPath"] = new[] { path } };

        if (culture != null)
        {
            arguments["Culture"] = culture;
        }

        if (strategy != null)
        {
            arguments["Strategy"] = strategy;
        }

        return this._fixture.InvokeCapturingErrors("Get-ProbedContent", arguments);
    }

    private string[] ReadLines(string path, string? culture = null, string? strategy = null)
    {
        InvocationResult result = Invoke(path, culture, strategy);

        Assert.Empty(result.Errors);

        return result.AsStrings();
    }

    private InvocationResult InvokeWriter(
        string command, string path, string culture, string? encodingFrom)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["LiteralPath"] = new[] { path },
            ["Value"] = new object[] { KoreanText },
            ["Culture"] = culture,
            ["LineBreak"] = LineBreakOption.Lf,
        };

        if (encodingFrom != null)
        {
            arguments["EncodingFrom"] = encodingFrom;
        }

        return this._fixture.InvokeCapturingErrors(command, arguments);
    }
}
