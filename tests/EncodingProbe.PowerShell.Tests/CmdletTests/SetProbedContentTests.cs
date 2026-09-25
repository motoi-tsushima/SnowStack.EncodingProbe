using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using System.Management.Automation;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Set-ProbedContent の仕様（仕様書 5 節）を検証するテスト。
/// </summary>
/// <remarks>
/// 期待値はすべてバイト列で表現する。書き込み結果を Get-ProbedContent で読み返すと、
/// 読み書き両方に同じ誤りがある場合に辻褄が合ってしまい検出できないためである。
/// </remarks>
public class SetProbedContentTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string CommandName = "Set-ProbedContent";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public SetProbedContentTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    #region 統一語彙と BOM（仕様書 3.3 / 3.5）

    /// <summary>
    /// BOM 接尾辞を持つ語彙が、接尾辞のとおりのバイト列を書き出すこと。
    /// </summary>
    /// <remarks>
    /// Encoding.GetEncoding("utf-8") や Encoding.UTF8 は BOM 付きインスタンスを返すため、
    /// 実装が語彙ではなくインスタンス既定に従っていると NoBOM 側が壊れる。
    /// この検証が BOM 方針に関する最も重要な回帰テストとなる。
    /// </remarks>
    [Fact]
    public void Write_UnicodeVocabulary_EmitsBomExactlyAsNamed()
    {
        // 本文は "A" + LF に固定し、BOM の有無だけが違う形にする
        var cases = new Dictionary<string, byte[]>
        {
            ["utf8NoBOM"] = new byte[] { 0x41, 0x0A },
            ["utf8BOM"] = ByteExactFile.Concat(ByteExactFile.Utf8Bom, new byte[] { 0x41, 0x0A }),

            ["unicodeNoBOM"] = new byte[] { 0x41, 0x00, 0x0A, 0x00 },
            ["unicodeBOM"] = ByteExactFile.Concat(
                ByteExactFile.Utf16LeBom, new byte[] { 0x41, 0x00, 0x0A, 0x00 }),

            ["bigendianunicodeNoBOM"] = new byte[] { 0x00, 0x41, 0x00, 0x0A },
            ["bigendianunicodeBOM"] = ByteExactFile.Concat(
                ByteExactFile.Utf16BeBom, new byte[] { 0x00, 0x41, 0x00, 0x0A }),

            ["utf32NoBOM"] = new byte[] { 0x41, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 },
            ["utf32BOM"] = ByteExactFile.Concat(
                ByteExactFile.Utf32LeBom, new byte[] { 0x41, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 }),

            ["bigendianutf32NoBOM"] = new byte[] { 0x00, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00, 0x0A },
            ["bigendianutf32BOM"] = ByteExactFile.Concat(
                ByteExactFile.Utf32BeBom, new byte[] { 0x00, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00, 0x0A }),
        };

        foreach (KeyValuePair<string, byte[]> testCase in cases)
        {
            using var file = ByteExactFile.CreateMissing();

            InvocationResult result = Invoke(
                file.Path, "A", new Dictionary<string, object?>
                {
                    ["Encoding"] = testCase.Key,
                    ["LineBreak"] = LineBreakOption.Lf,
                });

            Assert.Empty(result.Errors);
            Assert.Equal(testCase.Value, file.ReadBytes());
        }
    }

    /// <summary>
    /// 語彙・WebName・数値コードページのいずれで指定しても同じバイト列になること。
    /// </summary>
    [Theory]
    [InlineData("shift_jis")]
    [InlineData("932")]
    public void Write_LegacyCodePage_ProducesSameBytesForEveryNotation(object encoding)
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "あ", new Dictionary<string, object?>
            {
                ["Encoding"] = encoding,
                ["LineBreak"] = LineBreakOption.CrLf,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x82, 0xA0, 0x0D, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// BOM 方針の定まらない指定は、ファイルを開く前（パラメータ束縛の段階）で拒否されること。
    /// </summary>
    /// <remarks>
    /// 書き込み系コマンドでは、書きかけの破損ファイルを残さないことが重要になる。
    /// 束縛の段階で失敗していれば、ファイルには一切触れていないと保証できる。
    /// </remarks>
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf-8")]
    [InlineData("65001")]
    [InlineData("utf-16")]
    [InlineData("1200")]
    [InlineData("utf7")]
    [InlineData("utf-7")]
    public void Write_BomPolicyUnspecified_FailsAtParameterBindingWithoutTouchingFile(string encoding)
    {
        using var file = ByteExactFile.CreateMissing();

        var exception = Assert.ThrowsAny<ParameterBindingException>(
            () => Invoke(file.Path, "A", new Dictionary<string, object?> { ["Encoding"] = encoding }));

        Assert.IsType<ArgumentTransformationMetadataException>(exception.InnerException);
        Assert.False(file.Exists);
    }

    /// <summary>
    /// System.Text.Encoding インスタンスを渡した場合、その BOM 方針に従うこと（仕様書 3.4）。
    /// </summary>
    /// <remarks>
    /// Encoding.UTF8 は BOM 付きインスタンス、new UTF8Encoding($false) は BOM 無しインスタンスであり、
    /// 同じ UTF-8 でも書き出されるバイト列が変わる。
    /// </remarks>
    [Theory]
    [InlineData("([System.Text.Encoding]::UTF8)", new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x0A })]
    [InlineData("(New-Object System.Text.UTF8Encoding $false)", new byte[] { 0x41, 0x0A })]
    public void Write_EncodingInstance_FollowsItsOwnBomPolicy(string expression, byte[] expected)
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath '{file.Path}' -Value 'A' "
            + $"-Encoding {expression} -LineBreak Lf");

        Assert.Empty(result.Errors);
        Assert.Equal(expected, file.ReadBytes());
    }

    #endregion

    #region 改行コード（仕様書 5.5 / 5.6）

    /// <summary>
    /// -LineBreak の明示指定がそのまま使われること。
    /// </summary>
    [Theory]
    [InlineData(LineBreakOption.CrLf, new byte[] { 0x41, 0x0D, 0x0A })]
    [InlineData(LineBreakOption.Lf, new byte[] { 0x41, 0x0A })]
    [InlineData(LineBreakOption.Cr, new byte[] { 0x41, 0x0D })]
    public void Write_ExplicitLineBreak_UsesSpecifiedCharacters(LineBreakOption option, byte[] expected)
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "A", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = option,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(expected, file.ReadBytes());
    }

    /// <summary>
    /// 参照情報が無い場合は OS 既定の改行になること。
    /// </summary>
    [Fact]
    public void Write_WithoutReference_UsesOperatingSystemDefaultLineBreak()
    {
        using var file = ByteExactFile.CreateMissing();

        Invoke(file.Path, "A", new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" });

        Assert.Equal(Encoding.ASCII.GetBytes("A" + Environment.NewLine), file.ReadBytes());
    }

    /// <summary>
    /// 参照情報がある場合は、OS を問わず参照元の改行を継承すること。
    /// </summary>
    [Theory]
    [InlineData(0x0A, new byte[] { 0x58, 0x0A })]
    [InlineData(0x0D, new byte[] { 0x58, 0x0D })]
    public void Write_EncodingFrom_InheritsLineBreakRegardlessOfOperatingSystem(
        byte referenceLineBreak, byte[] expected)
    {
        using var reference = ByteExactFile.Create(new byte[] { 0x41, referenceLineBreak, 0x42 });
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "X", new Dictionary<string, object?> { ["EncodingFrom"] = reference.Path });

        Assert.Empty(result.Errors);
        Assert.Equal(expected, file.ReadBytes());
    }

    /// <summary>
    /// 混在改行の参照元からは、CR-LF を含むなら CR-LF、含まないなら LF を継承すること。
    /// </summary>
    /// <remarks>
    /// EncodingInformation は改行の出現回数を保持しないため多数決は取れない。
    /// OS に依存しない決定的な規則とすることで、PowerShell 5.1 と 7.x で同じ結果になる。
    /// </remarks>
    [Theory]
    // "A" LF "B" CRLF "C" … LF と CR-LF の混在
    [InlineData(new byte[] { 0x41, 0x0A, 0x42, 0x0D, 0x0A, 0x43 }, new byte[] { 0x58, 0x0D, 0x0A })]
    // "A" LF "B" CR "C" … LF と CR の混在（CR-LF を含まない）
    [InlineData(new byte[] { 0x41, 0x0A, 0x42, 0x0D, 0x43 }, new byte[] { 0x58, 0x0A })]
    public void Write_MixedLineBreakReference_FollowsDeterministicRule(byte[] referenceBytes, byte[] expected)
    {
        using var reference = ByteExactFile.Create(referenceBytes);
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "X", new Dictionary<string, object?> { ["EncodingFrom"] = reference.Path });

        Assert.Empty(result.Errors);
        Assert.Equal(expected, file.ReadBytes());
    }

    /// <summary>
    /// -LineBreak の明示指定は継承より優先されること（粒度の細かい方が勝つ）。
    /// </summary>
    [Fact]
    public void Write_ExplicitLineBreak_OverridesInheritance()
    {
        // 参照元は LF
        using var reference = ByteExactFile.Create(new byte[] { 0x41, 0x0A, 0x42 });
        using var file = ByteExactFile.CreateMissing();

        Invoke(
            file.Path, "X", new Dictionary<string, object?>
            {
                ["EncodingFrom"] = reference.Path,
                ["LineBreak"] = LineBreakOption.CrLf,
            });

        Assert.Equal(new byte[] { 0x58, 0x0D, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// EncodingInformation を -Encoding に渡した場合は改行も継承すること（仕様書 5.4）。
    /// </summary>
    [Fact]
    public void Write_EncodingInformation_InheritsLineBreakToo()
    {
        // BOM 無し UTF-8 / LF のファイルから EncodingInformation を得る
        using var reference = ByteExactFile.Create(
            ByteExactFile.Encode(new UTF8Encoding(false), "あ\nい\n"));

        EncodingInformation information =
            SnowStack.EncodingProbe.EncodingProbe.Detect(reference.ReadBytes());

        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "X", new Dictionary<string, object?> { ["Encoding"] = information });

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x58, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 語彙名で指定した場合は改行情報が無いため、OS 既定に落ちること（仕様書 5.4）。
    /// </summary>
    [Fact]
    public void Write_VocabularyName_HasNoLineBreakInformation()
    {
        using var file = ByteExactFile.CreateMissing();

        Invoke(file.Path, "X", new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" });

        Assert.Equal(Encoding.ASCII.GetBytes("X" + Environment.NewLine), file.ReadBytes());
    }

    /// <summary>
    /// -NoNewline は要素間・末尾のどちらにも改行を出力しないこと。
    /// </summary>
    [Fact]
    public void Write_NoNewline_WritesNoLineBreakAtAll()
    {
        using var file = ByteExactFile.CreateMissing();

        Invoke(
            file.Path, new object[] { "A", "B" }, new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["NoNewline"] = true,
            });

        Assert.Equal(new byte[] { 0x41, 0x42 }, file.ReadBytes());
    }

    /// <summary>
    /// -NoNewline と -LineBreak の同時指定は Warning であり、Error にはしないこと（仕様書 5.6）。
    /// </summary>
    [Fact]
    public void Write_NoNewlineWithLineBreak_WarnsButSucceeds()
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "A", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["NoNewline"] = true,
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(ValidationMessages.NoNewlineIgnoresLineBreak(), Assert.Single(result.Warnings).Message);
        Assert.Equal(new byte[] { 0x41 }, file.ReadBytes());
    }

    /// <summary>
    /// -NoNewline を指定しなければ警告は出ないこと。
    /// </summary>
    [Fact]
    public void Write_LineBreakWithoutNoNewline_DoesNotWarn()
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "A", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Warnings);
    }

    #endregion

    #region -Encoding Auto（仕様書 5.3）

    /// <summary>
    /// -Encoding 省略時は、書き込み先の既存ファイルからエンコーディング・BOM・改行を継承すること。
    /// </summary>
    [Fact]
    public void Write_AutoEncoding_InheritsEncodingBomAndLineBreakFromTarget()
    {
        // BOM 付き UTF-8 / LF
        using var file = ByteExactFile.CreateFrom(
            ByteExactFile.Utf8Bom, new byte[] { 0x41, 0x0A });

        InvocationResult result = Invoke(file.Path, "X");

        Assert.Empty(result.Errors);
        Assert.Equal(
            ByteExactFile.Concat(ByteExactFile.Utf8Bom, new byte[] { 0x58, 0x0A }),
            file.ReadBytes());
    }

    /// <summary>
    /// レガシーコードページの既存ファイルも継承できること。
    /// </summary>
    [Fact]
    public void Write_AutoEncoding_InheritsLegacyCodePage()
    {
        // Shift_JIS / CR-LF（EUC-JP との曖昧解消のため CR-LF を使う）
        using var file = ByteExactFile.Create(
            ByteExactFile.Encode(Encoding.GetEncoding(932), "日本語\r\n"));

        InvocationResult result = Invoke(file.Path, "あ");

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x82, 0xA0, 0x0D, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 書き込み先が存在しない場合はエラーとし、ファイルを作らないこと。
    /// </summary>
    [Fact]
    public void Write_AutoEncoding_MissingTarget_ReportsErrorAndCreatesNothing()
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(file.Path, "X");

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("AutoEncodingRequiresExistingFile", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(
            ValidationMessages.AutoEncodingRequiresExistingFile(file.Path),
            error.Exception.Message);
        Assert.False(file.Exists);
    }

    /// <summary>
    /// 書き込み先が 0 バイトの場合は、稼働している .NET ランタイムの既定
    /// （BOM 無し）と OS 既定の改行を使うこと。
    /// </summary>
    /// <remarks>
    /// 0 バイトのファイルからは何も判定できない。「存在しない」と同じエラー扱いにすると、
    /// New-Item で作った空ファイルへ書き込めなくなるため、既定値で書き込む。
    /// </remarks>
    [Fact]
    public void Write_AutoEncoding_EmptyTarget_UsesRuntimeDefaultEncoding()
    {
        using var file = ByteExactFile.Create();

        InvocationResult result = Invoke(file.Path, "A");

        Encoding expected = EncodingVocabulary.BuildEncoding(Encoding.Default.CodePage, emitBom: false);

        Assert.Empty(result.Errors);
        Assert.Equal(expected.GetBytes("A" + Environment.NewLine), file.ReadBytes());
    }

    #endregion

    #region -EncodingFrom（仕様書 5.2）

    /// <summary>
    /// -EncodingFrom は文字エンコーディング・BOM・改行の3点すべてを継承すること。
    /// </summary>
    [Fact]
    public void Write_EncodingFrom_InheritsEncodingBomAndLineBreak()
    {
        // BOM 付き UTF-16LE / LF
        using var reference = ByteExactFile.CreateFrom(
            ByteExactFile.Utf16LeBom,
            new byte[] { 0x21, 0xFF, 0x0A, 0x00 });  // U+FF21 + LF

        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "X", new Dictionary<string, object?> { ["EncodingFrom"] = reference.Path });

        Assert.Empty(result.Errors);
        Assert.Equal(
            ByteExactFile.Concat(ByteExactFile.Utf16LeBom, new byte[] { 0x58, 0x00, 0x0A, 0x00 }),
            file.ReadBytes());
    }

    /// <summary>
    /// -Encoding と -EncodingFrom の同時指定は終了エラーになること。
    /// </summary>
    /// <remarks>
    /// パラメータの組み合わせ自体が成立していないため、対象ファイルごとの
    /// 非終了エラーではなく、コマンド全体を終了させる。
    /// </remarks>
    [Fact]
    public void Write_EncodingAndEncodingFrom_IsTerminatingError()
    {
        using var reference = ByteExactFile.Create(new byte[] { 0x41, 0x0A });
        using var file = ByteExactFile.CreateMissing();

        var exception = Assert.Throws<CmdletInvocationException>(() => Invoke(
            file.Path, "X", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["EncodingFrom"] = reference.Path,
            }));

        Assert.Equal(
            "EncodingAndEncodingFromAreExclusive",
            exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(
            ValidationMessages.EncodingAndEncodingFromAreExclusive(),
            exception.ErrorRecord.Exception.Message);
        Assert.False(file.Exists);
    }

    /// <summary>
    /// 参照元が存在しない場合は終了エラーとし、書き込み先を作らないこと。
    /// </summary>
    [Fact]
    public void Write_EncodingFrom_MissingReference_IsTerminatingError()
    {
        using var reference = ByteExactFile.CreateMissing();
        using var file = ByteExactFile.CreateMissing();

        var exception = Assert.Throws<CmdletInvocationException>(() => Invoke(
            file.Path, "X", new Dictionary<string, object?> { ["EncodingFrom"] = reference.Path }));

        Assert.Equal(
            "EncodingFromNotFound", exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(
            ValidationMessages.FileNotFound(reference.Path), exception.ErrorRecord.Exception.Message);
        Assert.False(file.Exists);
    }

    /// <summary>
    /// 参照元が 0 バイトの場合も、判定材料が無いため既定値で書き込むこと。
    /// </summary>
    [Fact]
    public void Write_EncodingFrom_EmptyReference_UsesRuntimeDefaultEncoding()
    {
        using var reference = ByteExactFile.Create();
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "A", new Dictionary<string, object?> { ["EncodingFrom"] = reference.Path });

        Encoding expected = EncodingVocabulary.BuildEncoding(Encoding.Default.CodePage, emitBom: false);

        Assert.Empty(result.Errors);
        Assert.Equal(expected.GetBytes("A" + Environment.NewLine), file.ReadBytes());
    }

    #endregion

    #region 同一パスの往復（仕様書 9 節）

    /// <summary>
    /// 読み取り中のファイルへの書き込みはエラーとし、元のファイルを壊さないこと。
    /// </summary>
    /// <remarks>
    /// Get-ProbedContent は行単位でストリーミング出力するため、この検出が無いと
    /// 1行目を読んだ時点でファイルが切り詰められ、残りが失われる。
    /// </remarks>
    [Fact]
    public void Write_ToFileBeingRead_ReportsErrorAndKeepsOriginalBytes()
    {
        byte[] original = { 0x41, 0x0D, 0x0A, 0x42, 0x0D, 0x0A };

        using var file = ByteExactFile.Create(original);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Get-ProbedContent -LiteralPath '{file.Path}' | Set-ProbedContent -LiteralPath '{file.Path}'");

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("SamePathRoundTrip", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ValidationMessages.SamePathRoundTrip(file.Path), error.Exception.Message);
        Assert.Equal(original, file.ReadBytes());
    }

    /// <summary>
    /// -Raw で読んだ場合は、読み終えた時点でファイルを閉じているため書き戻せること。
    /// </summary>
    [Fact]
    public void Write_ToFileAlreadyReadWithRaw_Succeeds()
    {
        // BOM 無し UTF-8 / LF
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A, 0x42, 0x0A });

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Get-ProbedContent -LiteralPath '{file.Path}' -Raw "
            + $"| Set-ProbedContent -LiteralPath '{file.Path}' -NoNewline");

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x41, 0x0A, 0x42, 0x0A }, file.ReadBytes());
    }

    #endregion

    #region -Value とパイプライン（仕様書 5.1）

    /// <summary>
    /// 複数の要素は、それぞれの後ろに改行を付けて出力されること（末尾も含む）。
    /// </summary>
    [Fact]
    public void Write_MultipleValues_TerminatesEveryElementWithLineBreak()
    {
        using var file = ByteExactFile.CreateMissing();

        Invoke(
            file.Path, new object[] { "A", "B" }, new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Equal(new byte[] { 0x41, 0x0A, 0x42, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// パイプラインから流れてきた要素も、1回のコマンドで1つのファイルに書き込まれること。
    /// </summary>
    [Fact]
    public void Write_PipelineInput_WritesAllElementsToOneFile()
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'A','B','C' | Set-ProbedContent -LiteralPath '{file.Path}' -Encoding utf8NoBOM -LineBreak Lf");

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x41, 0x0A, 0x42, 0x0A, 0x43, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 空のコレクションを渡した場合は、内容が空のファイルになること。
    /// </summary>
    [Fact]
    public void Write_EmptyValue_TruncatesToEmptyFile()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x42, 0x43 });

        InvocationResult result = Invoke(
            file.Path, Array.Empty<object>(), new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" });

        Assert.Empty(result.Errors);
        Assert.Empty(file.ReadBytes());
    }

    /// <summary>
    /// BOM 付きの語彙で空のコレクションを渡した場合は、BOM だけのファイルになること。
    /// </summary>
    [Fact]
    public void Write_EmptyValueWithBomVocabulary_WritesBomOnly()
    {
        using var file = ByteExactFile.CreateMissing();

        Invoke(
            file.Path, Array.Empty<object>(), new Dictionary<string, object?> { ["Encoding"] = "utf8BOM" });

        Assert.Equal(ByteExactFile.Utf8Bom, file.ReadBytes());
    }

    /// <summary>
    /// null の要素は空行として出力されること。
    /// </summary>
    [Fact]
    public void Write_NullElement_WritesEmptyLine()
    {
        using var file = ByteExactFile.CreateMissing();

        Invoke(
            file.Path, new object?[] { "A", null, "B" }, new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Equal(new byte[] { 0x41, 0x0A, 0x0A, 0x42, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 文字列以外の値も PowerShell の型変換で文字列化されること。
    /// </summary>
    [Fact]
    public void Write_NonStringValue_IsConvertedToString()
    {
        using var file = ByteExactFile.CreateMissing();

        Invoke(
            file.Path, new object[] { 1.5, 42 }, new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Equal(Encoding.ASCII.GetBytes("1.5\n42\n"), file.ReadBytes());
    }

    #endregion

    #region パス指定・-Force・-WhatIf（仕様書 5.1）

    /// <summary>
    /// 複数のパスを指定した場合、すべてに同じ内容が書き込まれること。
    /// </summary>
    [Fact]
    public void Write_MultiplePaths_WritesSameContentToEach()
    {
        using var first = ByteExactFile.CreateMissing();
        using var second = ByteExactFile.CreateMissing();

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            CommandName,
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { first.Path, second.Path },
                ["Value"] = new object[] { "A" },
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x41, 0x0A }, first.ReadBytes());
        Assert.Equal(new byte[] { 0x41, 0x0A }, second.ReadBytes());
    }

    /// <summary>
    /// ディレクトリを指定した場合はエラーになること。
    /// </summary>
    [Fact]
    public void Write_Directory_ReportsError()
    {
        string directory = CreateWorkDirectory();

        try
        {
            InvocationResult result = Invoke(
                directory, "A", new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" });

            Assert.Equal("PathIsNotFile", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 読み取り専用ファイルへは、-Force 無しでは書き込まないこと。
    /// </summary>
    [Fact]
    public void Write_ReadOnlyFile_WithoutForce_ReportsErrorAndKeepsContent()
    {
        byte[] original = { 0x41, 0x0A };

        using var file = ByteExactFile.Create(original);

        SetReadOnly(file.Path, true);

        try
        {
            InvocationResult result = Invoke(
                file.Path, "X", new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" });

            Assert.Equal(
                "WriteAccessDenied", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(original, file.ReadBytes());
        }
        finally
        {
            SetReadOnly(file.Path, false);
        }
    }

    /// <summary>
    /// -Force を指定すると読み取り専用ファイルへも書き込め、書き込み後に読み取り専用属性が元に戻ること（1.2.0 仕様書 4.1）。
    /// </summary>
    /// <remarks>
    /// 1.1.0 は属性を外したままにしていた。標準の Set-Content / Add-Content / Out-File は元に戻す。
    /// </remarks>
    [Fact]
    public void Write_ReadOnlyFile_WithForce_Succeeds()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        SetReadOnly(file.Path, true);

        try
        {
            InvocationResult result = Invoke(
                file.Path, "X", new Dictionary<string, object?>
                {
                    ["Encoding"] = "utf8NoBOM",
                    ["LineBreak"] = LineBreakOption.Lf,
                    ["Force"] = true,
                });

            Assert.Empty(result.Errors);
            Assert.Equal(new byte[] { 0x58, 0x0A }, file.ReadBytes());
            Assert.True(IsReadOnly(file.Path));
        }
        finally
        {
            SetReadOnly(file.Path, false);
        }
    }

    /// <summary>
    /// -WhatIf ではファイルを作らないこと。
    /// </summary>
    [Fact]
    public void Write_WhatIf_DoesNotTouchFile()
    {
        byte[] original = { 0x41, 0x0A };

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, "X", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["WhatIf"] = true,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(original, file.ReadBytes());
    }

    #endregion

    #region 文字列の中の改行（1.2.0 仕様書 4.2）

    /// <summary>
    /// -LineBreak は要素の後ろに付ける改行だけを決め、文字列の中の改行は置き換えないこと。
    /// </summary>
    /// <remarks>
    /// 期待値は実測報告 5 章の 8-1〜8-6。-LineBreak 省略時は OS 既定の改行になるため、
    /// 省略の列は Environment.NewLine で組み立てる（Windows では実測の CRLF と一致する）。
    /// </remarks>
    [Theory]
    [MemberData(nameof(LineBreakInsideValueCases))]
    public void Write_LineBreak_DoesNotReplaceLineBreaksInsideValues(
        string[] values, LineBreakOption? option, bool noNewline, string expected)
    {
        using var file = ByteExactFile.CreateMissing();

        var parameters = new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" };

        if (option.HasValue)
        {
            parameters["LineBreak"] = option.Value;
        }

        if (noNewline)
        {
            parameters["NoNewline"] = true;
        }

        InvocationResult result = Invoke(file.Path, values, parameters);

        Assert.Empty(result.Errors);
        Assert.Equal(Encoding.ASCII.GetBytes(expected), file.ReadBytes());
    }

    public static IEnumerable<object?[]> LineBreakInsideValueCases()
    {
        string nl = Environment.NewLine;

        var inputs = new[]
        {
            new[] { "a\nb" },
            new[] { "a\r\nb" },
            new[] { "a\rb" },
            new[] { "a\n" },
            new[] { "a\r\n\r\nb" },
            new[] { "a\nb", "c" },
        };

        foreach (string[] values in inputs)
        {
            yield return new object?[] { values, null, false, string.Concat(values.Select(v => v + nl)) };
            yield return new object?[] { values, LineBreakOption.CrLf, false, string.Concat(values.Select(v => v + "\r\n")) };
            yield return new object?[] { values, LineBreakOption.Lf, false, string.Concat(values.Select(v => v + "\n")) };
            yield return new object?[] { values, LineBreakOption.Cr, false, string.Concat(values.Select(v => v + "\r")) };
            yield return new object?[] { values, null, true, string.Concat(values) };
        }
    }

    #endregion

    #region 無損失の往復

    /// <summary>
    /// -Raw で読んで -NoNewline で書き戻すと、バイト列が完全に一致すること。
    /// </summary>
    /// <remarks>
    /// 「エンコーディングを保って読み書きする」という本コマンド群の目的そのものを検証する。
    /// </remarks>
    [Theory]
    [InlineData(932, "\r\n", false)]
    [InlineData(65001, "\n", false)]
    [InlineData(65001, "\r\n", true)]
    [InlineData(1200, "\n", true)]
    public void RoundTrip_RawReadThenWriteBack_ReproducesOriginalBytes(
        int codePage, string lineBreak, bool bom)
    {
        Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: bom);

        byte[] original = ByteExactFile.Concat(
            encoding.GetPreamble(),
            encoding.GetBytes("日本語" + lineBreak + "ABC" + lineBreak));

        using var source = ByteExactFile.Create(original);
        using var destination = ByteExactFile.CreateMissing();

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"$text = Get-ProbedContent -LiteralPath '{source.Path}' -Raw; "
            + $"Set-ProbedContent -LiteralPath '{destination.Path}' -Value $text "
            + $"-EncodingFrom '{source.Path}' -NoNewline");

        Assert.Empty(result.Errors);
        Assert.Equal(original, destination.ReadBytes());
    }

    #endregion

    private InvocationResult Invoke(
        string path, object? value, IDictionary<string, object?>? parameters = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["LiteralPath"] = new[] { path },
            ["Value"] = value,
        };

        if (parameters != null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                arguments[parameter.Key] = parameter.Value;
            }
        }

        return this._fixture.InvokeCapturingErrors(CommandName, arguments);
    }

    private static bool IsReadOnly(string path)
        => (new FileInfo(path).Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;

    private static void SetReadOnly(string path, bool readOnly)
    {
        var info = new FileInfo(path);

        info.Attributes = readOnly
            ? info.Attributes | FileAttributes.ReadOnly
            : info.Attributes & ~FileAttributes.ReadOnly;
    }

    private static string CreateWorkDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EncodingProbeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}