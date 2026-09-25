using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Add-ProbedContent の仕様（仕様書 6 節）を検証するテスト。
/// </summary>
/// <remarks>
/// 期待値はすべてバイト列で表現する。追記の可否を決めているのはエンコーディング名ではなく
/// 実際に書き出されるバイト列であり、読み返して比較したのでは検証にならないためである。
/// </remarks>
public class AddProbedContentTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string CommandName = "Add-ProbedContent";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public AddProbedContentTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    #region 整合性検査 — バイト列で判定する（仕様書 6.1）

    /// <summary>
    /// 仕様書 6.1 の判定表がそのまま成立すること。
    /// </summary>
    /// <remarks>
    /// 「指定されたエンコーディング X で符号化したバイト列 == 既存のエンコーディング Y で
    /// 符号化したバイト列」が成立すれば許可、しなければ Error という一つの規則から導かれる。
    /// 名前の組み合わせでは判定していないことを、同じ組み合わせで内容だけを変えた
    /// UTF-8 / Shift_JIS の 2 件（日本語ありは Error、ASCII のみは許可）が示している。
    /// </remarks>
    [Theory]
    // US-ASCII のファイルに UTF-8 で日本語を追記 → ファイル全体が UTF-8 に変わってしまう
    [InlineData(20127, "ABC\r\n", "utf8NoBOM", "日本語", false)]
    // UTF-8 のファイルに ascii で ASCII のみを追記 → バイト列が一致する
    [InlineData(65001, "日本語\r\n", "ascii", "ABC", true)]
    [InlineData(65001, "日本語\r\n", "shift_jis", "日本語", false)]
    [InlineData(65001, "日本語\r\n", "shift_jis", "ABC", true)]
    [InlineData(932, "日本語\r\n", "utf8NoBOM", "日本語", false)]
    // UTF-16LE は ASCII のみでも一致しない（1文字が2バイトになるため）
    [InlineData(65001, "日本語\r\n", "unicodeNoBOM", "ABC", false)]
    public void Append_FollowsByteSequenceRule(
        int existingCodePage, string existingText, string encoding, string value, bool allowed)
    {
        Encoding existing = Encoding.GetEncoding(existingCodePage);
        byte[] original = existing.GetBytes(existingText);

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, value, new Dictionary<string, object?>
            {
                ["Encoding"] = encoding,
                ["LineBreak"] = LineBreakOption.Lf,
            });

        if (!allowed)
        {
            Assert.Equal(
                "EncodingChangeOnAppend", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(original, file.ReadBytes());
            return;
        }

        Assert.Empty(result.Errors);

        // 許可された場合、結果は既存のエンコーディングで追記したのとバイト単位で同一になる
        Assert.Equal(
            ByteExactFile.Concat(original, existing.GetBytes(value + "\n")),
            file.ReadBytes());
    }

    /// <summary>
    /// エラーメッセージが、指定された側と既存側の両方の文字エンコーディングを示すこと。
    /// </summary>
    [Fact]
    public void Append_EncodingChange_ReportsBothEncodings()
    {
        using var file = ByteExactFile.Create(Encoding.UTF8.GetBytes("日本語\r\n"));

        InvocationResult result = Invoke(
            file.Path, "あ", new Dictionary<string, object?> { ["Encoding"] = "shift_jis" });

        ErrorRecord error = Assert.Single(result.Errors);

        Assert.Equal(
            ValidationMessages.EncodingChangeOnAppend(file.Path, "shift_jis", "utf-8"),
            error.Exception.Message);
    }

    /// <summary>
    /// -Encoding を明示していても既存ファイルの判定は走り、判定に失敗すればエラーになること（仕様書 6.3）。
    /// </summary>
    [Fact]
    public void Append_WithExplicitEncoding_StillDetectsExistingFile()
    {
        // どの文字エンコーディングとしても解釈できないバイト列
        using var file = ByteExactFile.Create(
            new byte[] { 0x81, 0xFF, 0x00, 0xFE, 0x93, 0x40, 0xC0, 0x80, 0xED, 0xA0, 0x80 });

        InvocationResult result = Invoke(
            file.Path, "A", new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" });

        Assert.Equal(
            "EncodingDetectionFailed", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
    }

    /// <summary>
    /// 1レコード内の途中の要素が拒否された場合、手前の要素も書き込まれないこと。
    /// </summary>
    [Fact]
    public void Append_RejectedElement_LeavesEarlierElementsUnwritten()
    {
        byte[] original = Encoding.UTF8.GetBytes("日\n");

        using var file = ByteExactFile.Create(original);

        // "OK" だけなら許可されるが、"あ" が続くためレコード全体を書き込まない
        InvocationResult result = Invoke(
            file.Path, new object[] { "OK", "あ" }, new Dictionary<string, object?>
            {
                ["Encoding"] = "shift_jis",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Equal(
            "EncodingChangeOnAppend", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(original, file.ReadBytes());
    }

    /// <summary>
    /// 改行だけが一致しない場合も検出されること。
    /// </summary>
    /// <remarks>
    /// 本文が空でも、改行の符号化が違えばファイルは壊れる
    /// （UTF-8 の LF は 1 バイト、UTF-16LE の LF は 2 バイト）。
    /// 判定は本文だけでなく、実際に書き出す改行まで含めて行う必要がある。
    /// </remarks>
    [Fact]
    public void Append_LineBreakAloneDiffers_IsRejected()
    {
        byte[] original = Encoding.UTF8.GetBytes("日\n");

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, string.Empty, new Dictionary<string, object?>
            {
                ["Encoding"] = "unicodeNoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Equal(
            "EncodingChangeOnAppend", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(original, file.ReadBytes());
    }

    #endregion

    #region -AllowEncodingChange（仕様書 6.2）

    /// <summary>
    /// -AllowEncodingChange を指定すると、バイト列が一致しない追記も許可されること。
    /// </summary>
    [Fact]
    public void Append_AllowEncodingChange_PermitsDifferentBytes()
    {
        byte[] original = Encoding.UTF8.GetBytes("日\n");

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, "あ", new Dictionary<string, object?>
            {
                ["Encoding"] = "shift_jis",
                ["LineBreak"] = LineBreakOption.Lf,
                ["AllowEncodingChange"] = true,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(
            ByteExactFile.Concat(original, Encoding.GetEncoding(932).GetBytes("あ\n")),
            file.ReadBytes());
    }

    /// <summary>
    /// -Force では整合性検査を回避できないこと。
    /// </summary>
    /// <remarks>
    /// -Force は「読み取り専用ファイルへ書き込む」という別の意味を既に持っている。
    /// 相乗りさせると「読み取り専用属性を外したいだけなのにエンコーディングの検査まで
    /// 無効になった」という事故が起きる。
    /// </remarks>
    [Fact]
    public void Append_Force_DoesNotBypassEncodingCheck()
    {
        byte[] original = Encoding.UTF8.GetBytes("日\n");

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, "あ", new Dictionary<string, object?>
            {
                ["Encoding"] = "shift_jis",
                ["Force"] = true,
            });

        Assert.Equal(
            "EncodingChangeOnAppend", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(original, file.ReadBytes());
    }

    /// <summary>
    /// -AllowEncodingChange を指定した場合は、既存ファイルの判定自体を行わないこと。
    /// </summary>
    /// <remarks>
    /// 既存ファイルの判定は、追記内容と突き合わせるためだけに行っている。
    /// 突き合わせないと決まっているなら、判定できないファイルへも追記できる。
    /// 判定はファイル全体を読むため、不要な読み込みを避ける意味もある。
    /// </remarks>
    [Fact]
    public void Append_AllowEncodingChange_SkipsDetectionOfExistingFile()
    {
        byte[] original = { 0x81, 0xFF, 0x00, 0xFE, 0x93, 0x40, 0xC0, 0x80, 0xED, 0xA0, 0x80 };

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, "A", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
                ["AllowEncodingChange"] = true,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(ByteExactFile.Concat(original, new byte[] { 0x41, 0x0A }), file.ReadBytes());
    }

    #endregion

    #region BOM は常に無視する（仕様書 6.3）

    /// <summary>
    /// BOM 付きの語彙を指定しても、ファイルの途中に BOM を書き込まないこと。
    /// </summary>
    [Fact]
    public void Append_BomVocabulary_DoesNotWriteBomIntoTheMiddle()
    {
        byte[] original = { 0x41, 0x0A };

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, "B", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8BOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x41, 0x0A, 0x42, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 既に BOM のあるファイルへ追記しても、BOM が増えないこと。
    /// </summary>
    [Fact]
    public void Append_ToFileWithBom_DoesNotDuplicateBom()
    {
        using var file = ByteExactFile.CreateFrom(ByteExactFile.Utf8Bom, new byte[] { 0x41, 0x0A });

        InvocationResult result = Invoke(file.Path, "B");

        Assert.Empty(result.Errors);
        Assert.Equal(
            ByteExactFile.Concat(ByteExactFile.Utf8Bom, new byte[] { 0x41, 0x0A, 0x42, 0x0A }),
            file.ReadBytes());
    }

    /// <summary>
    /// utf8BOM と utf8NoBOM が同じ結果になること（仕様書 6.3）。
    /// </summary>
    [Fact]
    public void Append_BomAndNoBomVocabulary_ProduceIdenticalResults()
    {
        using var withBom = ByteExactFile.Create(new byte[] { 0x41, 0x0A });
        using var withoutBom = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        Invoke(withBom.Path, "B", new Dictionary<string, object?>
        {
            ["Encoding"] = "utf8BOM",
            ["LineBreak"] = LineBreakOption.Lf,
        });

        Invoke(withoutBom.Path, "B", new Dictionary<string, object?>
        {
            ["Encoding"] = "utf8NoBOM",
            ["LineBreak"] = LineBreakOption.Lf,
        });

        Assert.Equal(withoutBom.ReadBytes(), withBom.ReadBytes());
    }

    /// <summary>
    /// BOM の指定が無視されても警告は出さないこと（仕様書 6.3）。
    /// </summary>
    /// <remarks>
    /// -EncodingFrom で BOM 付きのファイルから継承した場合、正当な使い方なのに毎回鳴るため。
    /// </remarks>
    [Fact]
    public void Append_BomVocabulary_DoesNotWarn()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        InvocationResult result = Invoke(
            file.Path, "B", new Dictionary<string, object?> { ["Encoding"] = "utf8BOM" });

        Assert.Empty(result.Warnings);
    }

    #endregion

    #region 改行コード（仕様書 6.3）

    /// <summary>
    /// 既存ファイルと異なる改行を指定しても許可されること。
    /// </summary>
    /// <remarks>
    /// 混在改行になるだけで読めなくなることはない。
    /// 「今後は LF に統一したい」という正当な意図があり得るため Error にしない。
    /// </remarks>
    [Fact]
    public void Append_DifferentLineBreak_IsAllowed()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0D, 0x0A });

        InvocationResult result = Invoke(
            file.Path, "B", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x0A, 0x42, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 末尾に改行が無いファイルへの追記は、最終行に連結されること。
    /// </summary>
    /// <remarks>
    /// 標準の Add-Content と同じ挙動とする。1.1.0 では警告もスイッチも設けない。
    /// </remarks>
    [Fact]
    public void Append_ToFileWithoutTrailingLineBreak_ConcatenatesToLastLine()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41 });

        InvocationResult result = Invoke(
            file.Path, "B", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// -Encoding 省略時は、追記先の改行コードを継承すること。
    /// </summary>
    [Theory]
    [InlineData(0x0D, new byte[] { 0x41, 0x0D, 0x42, 0x0D })]
    [InlineData(0x0A, new byte[] { 0x41, 0x0A, 0x42, 0x0A })]
    public void Append_AutoEncoding_InheritsLineBreakFromTarget(byte lineBreak, byte[] expected)
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, lineBreak });

        InvocationResult result = Invoke(file.Path, "B");

        Assert.Empty(result.Errors);
        Assert.Equal(expected, file.ReadBytes());
    }

    #endregion

    #region ISO-2022-JP（仕様書 6.3）

    /// <summary>
    /// 状態を持つ文字エンコーディングへ追記しても壊れないこと。
    /// </summary>
    /// <remarks>
    /// ISO-2022-JP はエスケープシーケンスで文字集合を切り替える。
    /// .NET のエンコーダは書き込みの先頭でエスケープシーケンスを出力し、
    /// 最後に ASCII へ戻すため、追記した部分だけで完結した状態になる。
    /// </remarks>
    [Fact]
    public void Append_ToIso2022Jp_EmitsItsOwnEscapeSequences()
    {
        Encoding iso2022Jp = Encoding.GetEncoding(50220);

        byte[] original = iso2022Jp.GetBytes("日\r\n");

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(file.Path, "本");

        Assert.Empty(result.Errors);

        byte[] written = file.ReadBytes();

        // 追記部分が、単独で符号化したバイト列と一致すること
        Assert.Equal(ByteExactFile.Concat(original, iso2022Jp.GetBytes("本\r\n")), written);

        // 追記部分の先頭にエスケープシーケンス（ESC $ B）があること
        var appendedHead = new byte[3];
        Array.Copy(written, original.Length, appendedHead, 0, appendedHead.Length);

        Assert.Equal(new byte[] { 0x1B, 0x24, 0x42 }, appendedHead);
    }

    #endregion

    #region -Encoding Auto と -EncodingFrom（仕様書 5.2 / 5.3）

    /// <summary>
    /// -Encoding 省略時は、追記先の文字エンコーディングを継承すること。
    /// </summary>
    [Fact]
    public void Append_AutoEncoding_InheritsEncodingFromTarget()
    {
        Encoding shiftJis = Encoding.GetEncoding(932);

        byte[] original = shiftJis.GetBytes("日本語\r\n");

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(file.Path, "あ");

        Assert.Empty(result.Errors);
        Assert.Equal(
            ByteExactFile.Concat(original, shiftJis.GetBytes("あ\r\n")),
            file.ReadBytes());
    }

    /// <summary>
    /// 追記先が存在しない場合はエラーとし、ファイルを作らないこと（仕様書 5.3）。
    /// </summary>
    [Fact]
    public void Append_AutoEncoding_MissingTarget_ReportsErrorAndCreatesNothing()
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(file.Path, "A");

        ErrorRecord error = Assert.Single(result.Errors);

        Assert.Equal("AutoEncodingRequiresExistingFile", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.False(file.Exists);
    }

    /// <summary>
    /// -Encoding を明示した場合は、存在しないファイルを新規作成すること。
    /// </summary>
    /// <remarks>
    /// 新規作成であっても BOM は書き出さない（仕様書 6.3）。
    /// </remarks>
    [Fact]
    public void Append_ExplicitEncoding_CreatesMissingFileWithoutBom()
    {
        using var file = ByteExactFile.CreateMissing();

        InvocationResult result = Invoke(
            file.Path, "A", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8BOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x41, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 追記先が 0 バイトの場合は、ランタイム既定の文字エンコーディングを使うこと。
    /// </summary>
    [Fact]
    public void Append_AutoEncoding_EmptyTarget_UsesRuntimeDefaultEncoding()
    {
        using var file = ByteExactFile.Create();

        InvocationResult result = Invoke(file.Path, "A");

        Encoding expected = EncodingVocabulary.BuildEncoding(Encoding.Default.CodePage, emitBom: false);

        Assert.Empty(result.Errors);
        Assert.Equal(expected.GetBytes("A" + Environment.NewLine), file.ReadBytes());
    }

    /// <summary>
    /// -EncodingFrom で別のファイルから継承できること。
    /// </summary>
    [Fact]
    public void Append_EncodingFrom_InheritsFromReference()
    {
        Encoding shiftJis = Encoding.GetEncoding(932);

        // 参照元は Shift_JIS / CR-LF、追記先は同じ Shift_JIS だが LF
        using var reference = ByteExactFile.Create(shiftJis.GetBytes("日本語\r\n"));

        byte[] original = shiftJis.GetBytes("あ\n");

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, "い", new Dictionary<string, object?> { ["EncodingFrom"] = reference.Path });

        Assert.Empty(result.Errors);
        Assert.Equal(
            ByteExactFile.Concat(original, shiftJis.GetBytes("い\r\n")),
            file.ReadBytes());
    }

    /// <summary>
    /// -Encoding と -EncodingFrom の同時指定は終了エラーになること。
    /// </summary>
    [Fact]
    public void Append_EncodingAndEncodingFrom_IsTerminatingError()
    {
        using var reference = ByteExactFile.Create(new byte[] { 0x41, 0x0A });
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        var exception = Assert.Throws<CmdletInvocationException>(() => Invoke(
            file.Path, "B", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["EncodingFrom"] = reference.Path,
            }));

        Assert.Equal(
            "EncodingAndEncodingFromAreExclusive",
            exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
    }

    #endregion

    #region 書き込み系に共通する挙動

    /// <summary>
    /// BOM 方針の定まらない指定は、ファイルを開く前（パラメータ束縛の段階）で拒否されること。
    /// </summary>
    /// <remarks>
    /// 追記では BOM 自体は無視されるが、語彙の意味は上書きと共通に保つ。
    /// </remarks>
    [Theory]
    [InlineData("utf8")]
    [InlineData("65001")]
    [InlineData("utf7")]
    public void Append_BomPolicyUnspecified_FailsAtParameterBinding(string encoding)
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        var exception = Assert.ThrowsAny<ParameterBindingException>(
            () => Invoke(file.Path, "B", new Dictionary<string, object?> { ["Encoding"] = encoding }));

        Assert.IsType<ArgumentTransformationMetadataException>(exception.InnerException);
        Assert.Equal(new byte[] { 0x41, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// 複数の要素は、それぞれの後ろに改行を付けて追記されること。
    /// </summary>
    [Fact]
    public void Append_MultipleValues_TerminatesEveryElementWithLineBreak()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        Invoke(
            file.Path, new object[] { "B", "C" }, new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Equal(new byte[] { 0x41, 0x0A, 0x42, 0x0A, 0x43, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// パイプラインから流れてきた要素が、1つのファイルに順に追記されること。
    /// </summary>
    [Fact]
    public void Append_PipelineInput_AppendsAllElementsInOrder()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'B','C' | Add-ProbedContent -LiteralPath '{file.Path}' -Encoding utf8NoBOM -LineBreak Lf");

        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0x41, 0x0A, 0x42, 0x0A, 0x43, 0x0A }, file.ReadBytes());
    }

    /// <summary>
    /// -NoNewline を指定すると、追記内容の末尾に改行を付けないこと。
    /// </summary>
    [Fact]
    public void Append_NoNewline_WritesNoLineBreak()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        Invoke(
            file.Path, "B", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["NoNewline"] = true,
            });

        Assert.Equal(new byte[] { 0x41, 0x0A, 0x42 }, file.ReadBytes());
    }

    /// <summary>
    /// 読み取り中のファイルへの追記もエラーになること。
    /// </summary>
    [Fact]
    public void Append_ToFileBeingRead_ReportsErrorAndKeepsOriginalBytes()
    {
        byte[] original = { 0x41, 0x0A, 0x42, 0x0A };

        using var file = ByteExactFile.Create(original);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Get-ProbedContent -LiteralPath '{file.Path}' | Add-ProbedContent -LiteralPath '{file.Path}'");

        Assert.Equal(
            "SamePathRoundTrip", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(original, file.ReadBytes());
    }

    /// <summary>
    /// -WhatIf ではファイルに触れないこと。
    /// </summary>
    [Fact]
    public void Append_WhatIf_DoesNotTouchFile()
    {
        byte[] original = { 0x41, 0x0A };

        using var file = ByteExactFile.Create(original);

        InvocationResult result = Invoke(
            file.Path, "B", new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["WhatIf"] = true,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(original, file.ReadBytes());
    }

    /// <summary>
    /// 読み取り専用ファイルへは、-Force 無しでは追記しないこと。
    /// </summary>
    [Fact]
    public void Append_ReadOnlyFile_WithoutForce_ReportsError()
    {
        byte[] original = { 0x41, 0x0A };

        using var file = ByteExactFile.Create(original);

        SetReadOnly(file.Path, true);

        try
        {
            InvocationResult result = Invoke(
                file.Path, "B", new Dictionary<string, object?> { ["Encoding"] = "utf8NoBOM" });

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
    /// -Force を指定すると読み取り専用ファイルへも追記でき、書き込み後に読み取り専用属性が元に戻ること（1.2.0 仕様書 4.1）。
    /// </summary>
    [Fact]
    public void Append_ReadOnlyFile_WithForce_Succeeds()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x0A });

        SetReadOnly(file.Path, true);

        try
        {
            InvocationResult result = Invoke(
                file.Path, "B", new Dictionary<string, object?>
                {
                    ["Encoding"] = "utf8NoBOM",
                    ["LineBreak"] = LineBreakOption.Lf,
                    ["Force"] = true,
                });

            Assert.Empty(result.Errors);
            Assert.Equal(new byte[] { 0x41, 0x0A, 0x42, 0x0A }, file.ReadBytes());
            Assert.True(IsReadOnly(file.Path));
        }
        finally
        {
            SetReadOnly(file.Path, false);
        }
    }

    #endregion

    #region 文字列の中の改行（1.2.0 仕様書 4.2）

    /// <summary>
    /// -LineBreak は要素の後ろに付ける改行だけを決め、文字列の中の改行は置き換えないこと。
    /// </summary>
    /// <remarks>
    /// 期待値は実測報告 5 章の「Add-ProbedContent -LineBreak Lf（既存 OLD + CRLF に追記）」の列。
    /// </remarks>
    [Theory]
    [InlineData(new[] { "a\nb" }, "OLD\r\na\nb\n")]
    [InlineData(new[] { "a\r\nb" }, "OLD\r\na\r\nb\n")]
    [InlineData(new[] { "a\rb" }, "OLD\r\na\rb\n")]
    [InlineData(new[] { "a\n" }, "OLD\r\na\n\n")]
    [InlineData(new[] { "a\r\n\r\nb" }, "OLD\r\na\r\n\r\nb\n")]
    [InlineData(new[] { "a\nb", "c" }, "OLD\r\na\nb\nc\n")]
    public void Append_LineBreak_DoesNotReplaceLineBreaksInsideValues(string[] values, string expected)
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("OLD\r\n"));

        InvocationResult result = Invoke(
            file.Path, values, new Dictionary<string, object?>
            {
                ["Encoding"] = "utf8NoBOM",
                ["LineBreak"] = LineBreakOption.Lf,
            });

        Assert.Empty(result.Errors);
        Assert.Equal(Encoding.ASCII.GetBytes(expected), file.ReadBytes());
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
}
