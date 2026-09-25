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
/// Out-ProbedFile の仕様（1.2.0 仕様書 第 1 部）を検証するテスト。
/// </summary>
/// <remarks>
/// 期待値はバイト列で表現する（SetProbedContentTests と同じ方針）。
/// 終了エラーはスクリプトの try / catch で FullyQualifiedErrorId を受け取って検査する。
/// -ErrorVariable には終了エラーのとき ErrorRecord ではなく例外オブジェクトが入るため（仕様書 5.2）、
/// それに頼らない。
/// </remarks>
public class OutProbedFileTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    /// <summary>エラーが起きなかったことを表す、スクリプトの戻り値</summary>
    private const string NoError = "NO-ERROR";

    private static readonly byte[] Utf8Bom = ByteExactFile.Utf8Bom;

    private readonly ProbedCommandRunspaceFixture _fixture;

    public OutProbedFileTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    #region 統一語彙（仕様書 2.4）

    /// <summary>
    /// BOM 接尾辞を持つ語彙が、全系統で接尾辞のとおりのバイト列を書き出すこと。
    /// </summary>
    [Theory]
    [InlineData("utf8NoBOM", new byte[] { 0x41, 0x0A })]
    [InlineData("utf8BOM", new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x0A })]
    [InlineData("unicodeNoBOM", new byte[] { 0x41, 0x00, 0x0A, 0x00 })]
    [InlineData("unicodeBOM", new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00 })]
    [InlineData("bigendianunicodeNoBOM", new byte[] { 0x00, 0x41, 0x00, 0x0A })]
    [InlineData("bigendianunicodeBOM", new byte[] { 0xFE, 0xFF, 0x00, 0x41, 0x00, 0x0A })]
    [InlineData("utf32NoBOM", new byte[] { 0x41, 0, 0, 0, 0x0A, 0, 0, 0 })]
    [InlineData("utf32BOM", new byte[] { 0xFF, 0xFE, 0, 0, 0x41, 0, 0, 0, 0x0A, 0, 0, 0 })]
    [InlineData("bigendianutf32NoBOM", new byte[] { 0, 0, 0, 0x41, 0, 0, 0, 0x0A })]
    [InlineData("bigendianutf32BOM", new byte[] { 0, 0, 0xFE, 0xFF, 0, 0, 0, 0x41, 0, 0, 0, 0x0A })]
    public void Vocabulary_BomSuffix_EmitsBomExactlyAsNamed(string encoding, byte[] expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -LiteralPath '{path}' -Encoding {encoding} -LineBreak Lf"));
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    /// <summary>
    /// WebName・数値コードページ・Encoding インスタンスのいずれでも指定できること。
    /// </summary>
    [Theory]
    [InlineData("shift_jis", new byte[] { 0x82, 0xA0, 0x0D, 0x0A })]
    [InlineData("932", new byte[] { 0x82, 0xA0, 0x0D, 0x0A })]
    [InlineData("([System.Text.Encoding]::UTF8)", new byte[] { 0xEF, 0xBB, 0xBF, 0xE3, 0x81, 0x82, 0x0D, 0x0A })]
    [InlineData("(New-Object System.Text.UTF8Encoding $false)", new byte[] { 0xE3, 0x81, 0x82, 0x0D, 0x0A })]
    public void Vocabulary_OtherNotations_AreAccepted(string encoding, byte[] expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"'あ' | Out-ProbedFile -LiteralPath '{path}' -Encoding {encoding} -LineBreak CrLf"));
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    /// <summary>
    /// EncodingInformation を渡すと、文字エンコーディング・BOM・改行を継承すること。
    /// </summary>
    [Fact]
    public void Vocabulary_EncodingInformation_InheritsLineBreak()
    {
        using var directory = new TemporaryDirectory();
        string reference = directory.Write("ref.txt", ByteExactFile.Concat(Utf8Bom, Encoding.ASCII.GetBytes("x\n")));
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run(
            $"$info = Resolve-Encoding -Path '{reference}'; 'A','B' | Out-ProbedFile -LiteralPath '{path}' -Encoding $info"));
        Assert.Equal(ByteExactFile.Concat(Utf8Bom, Encoding.ASCII.GetBytes("A\nB\n")), File.ReadAllBytes(path));
    }

    /// <summary>
    /// 裸の utf8・WebName の utf-8・utf7 はパラメータ束縛の段階で拒否され、ファイルに触れないこと。
    /// </summary>
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf-8")]
    [InlineData("65001")]
    [InlineData("unicode-1-1-utf-7")]
    [InlineData("utf7")]
    [InlineData("shift_jisBOM")]
    public void Vocabulary_Rejected_FailsAtParameterBinding(string encoding)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        string result = Run($"'A' | Out-ProbedFile -LiteralPath '{path}' -Encoding '{encoding}'");

        Assert.StartsWith("ParameterArgumentTransformationError", result);
        Assert.False(File.Exists(path));
    }

    #endregion

    #region -Encoding の決定（仕様書 2.5）

    /// <summary>
    /// -Encoding 省略の上書き・新規作成は utf8NoBOM で、既存ファイルを判定しないこと。
    /// </summary>
    /// <remarks>
    /// 既存ファイルを判定できない内容（NUL だけ）にしておき、判定が走ればエラーになるようにする。
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 })]
    public void Encoding_Omitted_Overwrite_IsUtf8NoBom(byte[]? existing)
    {
        using var directory = new TemporaryDirectory();
        string path = existing == null ? directory.Combine("out.txt") : directory.Write("out.txt", existing);

        Assert.Equal(NoError, Run($"'あ' | Out-ProbedFile -LiteralPath '{path}' -LineBreak Lf"));
        Assert.Equal(new byte[] { 0xE3, 0x81, 0x82, 0x0A }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Encoding 省略の -Append は追記先から継承し、追記先が無い・0 バイトなら utf8NoBOM になること。
    /// </summary>
    [Fact]
    public void Encoding_Omitted_Append_InheritsFromTarget()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00 });

        Assert.Equal(NoError, Run($"'B' | Out-ProbedFile -LiteralPath '{path}' -Append"));

        // 文字エンコーディング（UTF-16LE）と改行（LF）を継承し、BOM は書かない
        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 }, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encoding_Omitted_Append_MissingOrEmptyTarget_IsUtf8NoBom(bool createEmpty)
    {
        using var directory = new TemporaryDirectory();
        string path = createEmpty ? directory.Write("out.txt", Array.Empty<byte>()) : directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"'あ' | Out-ProbedFile -LiteralPath '{path}' -Append -LineBreak Lf"));
        Assert.Equal(new byte[] { 0xE3, 0x81, 0x82, 0x0A }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 明示的な Auto は出力先の既存ファイルから継承すること（上書き・追記とも）。
    /// </summary>
    [Theory]
    [InlineData(false, new byte[] { 0xFF, 0xFE, 0x42, 0x00, 0x0A, 0x00 })]
    [InlineData(true, new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 })]
    public void Encoding_ExplicitAuto_InheritsFromTarget(bool append, byte[] expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00 });

        Assert.Equal(NoError, Run($"'B' | Out-ProbedFile -LiteralPath '{path}' -Encoding Auto{(append ? " -Append" : "")}"));
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 明示的な Auto で出力先が無ければ、上書き・追記とも終了エラーになりファイルを作らないこと。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" -Append")]
    public void Encoding_ExplicitAuto_MissingTarget_IsTerminatingError(string append)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(
            "AutoEncodingRequiresExistingFile",
            Run($"'B' | Out-ProbedFile -LiteralPath '{path}' -Encoding Auto{append}"));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// 明示的な Auto で出力先が 0 バイトなら、EncodingInheritance の規則（ランタイム既定、BOM 無し）に従うこと。
    /// </summary>
    [Fact]
    public void Encoding_ExplicitAuto_EmptyTarget_FollowsEncodingInheritance()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", Array.Empty<byte>());

        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -LiteralPath '{path}' -Encoding Auto -LineBreak Lf"));
        Assert.Equal(new byte[] { 0x41, 0x0A }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Encoding と -EncodingFrom の同時指定は終了エラー。
    /// </summary>
    [Fact]
    public void Encoding_WithEncodingFrom_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        string reference = directory.Write("ref.txt", Encoding.ASCII.GetBytes("x\n"));
        string path = directory.Combine("out.txt");

        Assert.Equal(
            "EncodingAndEncodingFromAreExclusive",
            Run($"'A' | Out-ProbedFile -LiteralPath '{path}' -Encoding utf8NoBOM -EncodingFrom '{reference}'"));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// -EncodingFrom は文字エンコーディング・BOM・改行を継承すること。参照先が無ければ終了エラー。
    /// </summary>
    [Fact]
    public void EncodingFrom_InheritsEncodingBomAndLineBreak()
    {
        using var directory = new TemporaryDirectory();
        string reference = directory.Write("ref.txt", new byte[] { 0xFE, 0xFF, 0x00, 0x78, 0x00, 0x0D });
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -LiteralPath '{path}' -EncodingFrom '{reference}'"));
        Assert.Equal(new byte[] { 0xFE, 0xFF, 0x00, 0x41, 0x00, 0x0D }, File.ReadAllBytes(path));

        Assert.Equal(
            "EncodingFromNotFound",
            Run($"'A' | Out-ProbedFile -LiteralPath '{path}' -EncodingFrom '{directory.Combine("missing.txt")}'"));
    }

    #endregion

    #region 改行（仕様書 2.6 / 2.7）

    /// <summary>
    /// 参照情報が無い場合は OS 既定の改行になり、-LineBreak の明示が参照情報より優先されること。
    /// </summary>
    [Fact]
    public void LineBreak_PriorityOrder()
    {
        using var directory = new TemporaryDirectory();
        string reference = directory.Write("ref.txt", Encoding.ASCII.GetBytes("x\r"));
        string plain = directory.Combine("plain.txt");
        string explicitOverReference = directory.Combine("explicit.txt");
        string inherited = directory.Combine("inherited.txt");

        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -LiteralPath '{plain}' utf8NoBOM"));
        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -LiteralPath '{explicitOverReference}' -EncodingFrom '{reference}' -LineBreak Lf"));
        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -LiteralPath '{inherited}' -EncodingFrom '{reference}'"));

        Assert.Equal(Encoding.ASCII.GetBytes("A" + Environment.NewLine), File.ReadAllBytes(plain));
        Assert.Equal(Encoding.ASCII.GetBytes("A\n"), File.ReadAllBytes(explicitOverReference));
        Assert.Equal(Encoding.ASCII.GetBytes("A\r"), File.ReadAllBytes(inherited));
    }

    /// <summary>
    /// -LineBreak は各行の後ろの改行だけを決め、文字列の中の改行は置き換えないこと（実測報告 5 章）。
    /// </summary>
    [Theory]
    [InlineData("\"a`nb\"", "Lf", "a\nb\n")]
    [InlineData("\"a`r`nb\"", "Lf", "a\r\nb\n")]
    [InlineData("\"a`rb\"", "Cr", "a\rb\r")]
    [InlineData("\"a`n\"", "CrLf", "a\n\r\n")]
    [InlineData("\"a`nb\", 'c'", "Lf", "a\nb\nc\n")]
    public void LineBreak_DoesNotReplaceLineBreaksInsideStrings(string input, string lineBreak, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"{input} | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -LineBreak {lineBreak}"));
        Assert.Equal(Encoding.ASCII.GetBytes(expected), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -NoNewline は行を区切りなしで連結し、最後にも改行を付けないこと。-LineBreak と併用すると Warning。
    /// </summary>
    [Fact]
    public void NoNewline_ConcatenatesLines_AndWarnsWithLineBreak()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'a', '', 'b' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -NoNewline -LineBreak Lf");

        Assert.Empty(result.Errors);
        Assert.Equal(ValidationMessages.NoNewlineIgnoresLineBreak(), Assert.Single(result.Warnings).Message);
        Assert.Equal(Encoding.ASCII.GetBytes("ab"), File.ReadAllBytes(path));
    }

    #endregion

    #region -Append（仕様書 2.8）

    /// <summary>
    /// 追記先（1 バイト以上）には BOM を書かず、追記先が無ければ BOM を書くこと。
    /// </summary>
    [Fact]
    public void Append_WritesBomOnlyWhenTargetIsMissingOrEmpty()
    {
        using var directory = new TemporaryDirectory();
        string existing = directory.Write("existing.txt", ByteExactFile.Concat(Utf8Bom, Encoding.ASCII.GetBytes("A\n")));
        string empty = directory.Write("empty.txt", Array.Empty<byte>());
        string missing = directory.Combine("missing.txt");

        foreach (string path in new[] { existing, empty, missing })
        {
            Assert.Equal(NoError, Run($"'B' | Out-ProbedFile -LiteralPath '{path}' utf8BOM -Append -LineBreak Lf"));
        }

        Assert.Equal(ByteExactFile.Concat(Utf8Bom, Encoding.ASCII.GetBytes("A\nB\n")), File.ReadAllBytes(existing));
        Assert.Equal(ByteExactFile.Concat(Utf8Bom, Encoding.ASCII.GetBytes("B\n")), File.ReadAllBytes(empty));
        Assert.Equal(ByteExactFile.Concat(Utf8Bom, Encoding.ASCII.GetBytes("B\n")), File.ReadAllBytes(missing));
    }

    /// <summary>
    /// 整合性検査: 書き出すバイト列が同じなら、名前が違っても追記できること。
    /// </summary>
    [Fact]
    public void Append_SameBytes_IsAllowed()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", new byte[] { 0x82, 0xA0, 0x0A });

        Assert.Equal(NoError, Run($"'x' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -Append -Culture ja-JP -LineBreak Lf"));
        Assert.Equal(new byte[] { 0x82, 0xA0, 0x0A, 0x78, 0x0A }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 整合性検査: 不一致の行の手前で終了エラーになり、それまでの行は追記先のエンコーディングで残ること。
    /// </summary>
    [Fact]
    public void Append_Mismatch_StopsBeforeTheLineAndKeepsEarlierLines()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", new byte[] { 0x82, 0xA0, 0x0A });

        Assert.Equal(
            "EncodingChangeOnAppend",
            Run($"'x', 'い', 'y' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -Append -Culture ja-JP -LineBreak Lf"));
        Assert.Equal(new byte[] { 0x82, 0xA0, 0x0A, 0x78, 0x0A }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -AllowEncodingChange で整合性検査を飛ばすこと。-Append なしでは Warning を出して無視すること。
    /// </summary>
    [Fact]
    public void AllowEncodingChange_SkipsCheck_AndWarnsWithoutAppend()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", new byte[] { 0x82, 0xA0, 0x0A });

        Assert.Equal(NoError, Run(
            $"'い' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -Append -AllowEncodingChange -Culture ja-JP -LineBreak Lf"));
        Assert.Equal(new byte[] { 0x82, 0xA0, 0x0A, 0xE3, 0x81, 0x84, 0x0A }, File.ReadAllBytes(path));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'A' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -AllowEncodingChange -LineBreak Lf");

        Assert.Empty(result.Errors);
        Assert.Equal(ValidationMessages.AllowEncodingChangeWithoutAppend(), Assert.Single(result.Warnings).Message);
        Assert.Equal(new byte[] { 0x41, 0x0A }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 追記先の判定に失敗したら終了エラー（-Encoding を明示していても整合性検査のために判定する）。
    /// </summary>
    [Fact]
    public void Append_TargetDetectionFailure_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        byte[] undetectable = { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00 };
        string path = directory.Write("out.txt", undetectable);

        string result = Run($"'A' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -Append -Strategy NativeOnly");

        Assert.Contains(result, new[] { "EncodingDetectionFailed", "CodePageNotAvailable" });
        Assert.Equal(undetectable, File.ReadAllBytes(path));
    }

    #endregion

    #region -NoClobber / -Force / 読み取り専用（仕様書 2.9、実測報告 3-1〜3-10）

    /// <summary>
    /// 実測報告の 3-1〜3-10 と同じ組み合わせで、標準の Out-File と同じ結果になること。
    /// </summary>
    [Theory]
    [InlineData("3-1", false, false, "-NoClobber", NoError, "NEW", false)]
    [InlineData("3-2", true, false, "-NoClobber", "NoClobber", "OLD", false)]
    [InlineData("3-3", true, false, "-Append -NoClobber", NoError, "OLDNEW", false)]
    [InlineData("3-4", false, false, "-Append -NoClobber", NoError, "NEW", false)]
    [InlineData("3-5", true, false, "-Force -NoClobber", "NoClobber", "OLD", false)]
    [InlineData("3-6", true, true, "", "WriteAccessDenied", "OLD", true)]
    [InlineData("3-7", true, true, "-Force", NoError, "NEW", true)]
    [InlineData("3-8", true, true, "-Force -NoClobber", "NoClobber", "OLD", true)]
    [InlineData("3-9", true, true, "-Append", "WriteAccessDenied", "OLD", true)]
    [InlineData("3-10", true, true, "-Append -Force", NoError, "OLDNEW", true)]
    public void Combination_MatchesStandardOutFile(
        string id, bool exists, bool readOnly, string parameters, string expectedResult, string expectedLines, bool expectedReadOnly)
    {
        _ = id;
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("target.txt");

        if (exists)
        {
            // 実測と同じ初期内容: BOM 付き UTF-16LE の OLD + CRLF
            File.WriteAllBytes(path, ByteExactFile.Concat(ByteExactFile.Utf16LeBom, Encoding.Unicode.GetBytes("OLD\r\n")));
        }

        if (readOnly)
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        try
        {
            Assert.Equal(
                expectedResult,
                Run($"'NEW' | Out-ProbedFile -LiteralPath '{path}' unicodeBOM -LineBreak CrLf {parameters}"));

            string expectedText = expectedLines == "OLDNEW" ? "OLD\r\nNEW\r\n" : expectedLines + "\r\n";
            Assert.Equal(
                ByteExactFile.Concat(ByteExactFile.Utf16LeBom, Encoding.Unicode.GetBytes(expectedText)),
                File.ReadAllBytes(path));
            Assert.Equal(expectedReadOnly, ReadOnlyAttributeScope.IsReadOnly(path));
        }
        finally
        {
            ClearReadOnly(path);
        }
    }

    /// <summary>
    /// -Force で外した読み取り専用属性は、書き込みが途中で失敗しても元に戻ること。
    /// </summary>
    [Fact]
    public void Force_RestoresReadOnlyAttribute_EvenWhenWritingFails()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", new byte[] { 0x82, 0xA0, 0x0A });
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        try
        {
            Assert.Equal(
                "EncodingChangeOnAppend",
                Run($"'い' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -Append -Force -Culture ja-JP"));
            Assert.True(ReadOnlyAttributeScope.IsReadOnly(path));
            Assert.Equal(new byte[] { 0x82, 0xA0, 0x0A }, File.ReadAllBytes(path));
        }
        finally
        {
            ClearReadOnly(path);
        }
    }

    #endregion

    #region パス（仕様書 2.12、実測報告 4-1〜4-9）

    [Fact]
    public void Path_WildcardMatchingOneFile_WritesThatFile()
    {
        using var directory = new TemporaryDirectory();
        string a1 = directory.Write("a1.txt", Encoding.ASCII.GetBytes("OLD\n"));

        Assert.Equal(NoError, Run($"'NEW' | Out-ProbedFile -FilePath '{directory.Combine("a?.txt")}' utf8NoBOM -LineBreak Lf"));
        Assert.Equal(Encoding.ASCII.GetBytes("NEW\n"), File.ReadAllBytes(a1));
    }

    [Fact]
    public void Path_WildcardMatchingTwoFiles_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        string a1 = directory.Write("a1.txt", Encoding.ASCII.GetBytes("OLD\n"));
        string a2 = directory.Write("a2.txt", Encoding.ASCII.GetBytes("OLD\n"));

        Assert.Equal("MultipleFilesNotSupported", Run($"'NEW' | Out-ProbedFile '{directory.Combine("a?.txt")}' utf8NoBOM"));
        Assert.Equal(Encoding.ASCII.GetBytes("OLD\n"), File.ReadAllBytes(a1));
        Assert.Equal(Encoding.ASCII.GetBytes("OLD\n"), File.ReadAllBytes(a2));
    }

    [Theory]
    [InlineData("b*.txt")]
    [InlineData("c[1].txt")]
    public void Path_WildcardMatchingNothing_IsTerminatingErrorAndCreatesNothing(string name)
    {
        using var directory = new TemporaryDirectory();

        Assert.Equal("FileNotFound", Run($"'NEW' | Out-ProbedFile '{directory.Combine(name)}' utf8NoBOM"));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void Path_LiteralPath_CreatesNameWithWildcardCharacters()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("c[1].txt");

        Assert.Equal(NoError, Run($"'NEW' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -LineBreak Lf"));
        Assert.Equal(Encoding.ASCII.GetBytes("NEW\n"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Path_BracketMatchingExistingFile_IsInterpretedAsWildcard()
    {
        using var directory = new TemporaryDirectory();
        string c1 = directory.Write("c1.txt", Encoding.ASCII.GetBytes("OLD\n"));

        Assert.Equal(NoError, Run($"'NEW' | Out-ProbedFile '{directory.Combine("c[1].txt")}' utf8NoBOM -LineBreak Lf"));
        Assert.Equal(Encoding.ASCII.GetBytes("NEW\n"), File.ReadAllBytes(c1));
    }

    [Fact]
    public void Path_RelativePath_IsBasedOnPowerShellLocation()
    {
        using var directory = new TemporaryDirectory();

        Assert.Equal(NoError, Run(
            $"Push-Location '{directory.Path}'; try {{ 'A' | Out-ProbedFile rel.txt utf8NoBOM -LineBreak Lf }} finally {{ Pop-Location }}"));
        Assert.Equal(Encoding.ASCII.GetBytes("A\n"), File.ReadAllBytes(directory.Combine("rel.txt")));
    }

    [Fact]
    public void Path_MissingParentDirectory_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine(Path.Combine("missing", "out.txt"));

        Assert.Equal("ParentDirectoryNotFound", Run($"'A' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM"));
        Assert.False(Directory.Exists(directory.Combine("missing")));
    }

    [Fact]
    public void Path_Directory_IsTerminatingError()
    {
        using var directory = new TemporaryDirectory();

        Assert.Equal("PathIsNotFile", Run($"'A' | Out-ProbedFile -LiteralPath '{directory.Path}' utf8NoBOM"));
    }

    [Theory]
    [InlineData("''", "ParameterArgumentValidationErrorEmptyStringNotAllowed")]
    [InlineData("$null", "ParameterArgumentValidationErrorNullNotAllowed")]
    public void Path_EmptyOrNull_IsRejectedAtParameterBinding(string value, string expected)
    {
        Assert.Equal(expected, Run($"'A' | Out-ProbedFile -FilePath {value} utf8NoBOM"));
    }

    /// <summary>
    /// 別名 -Path / -LP / -PSPath が使えること（PS 5.1 でも提供する）。
    /// </summary>
    [Theory]
    [InlineData("Path")]
    [InlineData("LP")]
    [InlineData("PSPath")]
    public void Path_Aliases_AreAvailable(string alias)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -{alias} '{path}' utf8NoBOM -LineBreak Lf"));
        Assert.Equal(Encoding.ASCII.GetBytes("A\n"), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -LiteralPath はパイプラインから受け取らないこと（PSPath を持つオブジェクトは -InputObject に束縛される）。
    /// </summary>
    [Fact]
    public void Path_LiteralPath_IsNotBoundFromPipeline()
    {
        ParameterAttribute attribute = typeof(OutProbedFileCommand)
            .GetProperty(nameof(OutProbedFileCommand.LiteralPath))!
            .GetCustomAttributes(typeof(ParameterAttribute), false)
            .Cast<ParameterAttribute>()
            .Single();

        Assert.False(attribute.ValueFromPipeline);
        Assert.False(attribute.ValueFromPipelineByPropertyName);

        using var directory = new TemporaryDirectory();
        directory.Write("item.txt", Encoding.ASCII.GetBytes("x"));

        // -FilePath を与えずに Get-ChildItem を流すと、PSPath から出力先が決まることはなく、
        // 必須パラメータの不足で失敗する（非対話ホストでは入力を求められずにエラーになる）
        string result = Run($"Get-ChildItem -LiteralPath '{directory.Path}' | Out-ProbedFile -Encoding utf8NoBOM");

        Assert.NotEqual(NoError, result);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    #endregion

    #region 整形（仕様書 2.2 / 2.3）

    /// <summary>
    /// 文字列の入力は整形の影響を受けず、そのまま 1 行ずつ書かれること。
    /// </summary>
    [Fact]
    public void Format_Strings_AreWrittenAsIs()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");
        string longText = new string('x', 253);

        Assert.Equal(NoError, Run($"'alpha', 'beta', '{longText}' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -Width 40 -LineBreak Lf"));
        Assert.Equal(Encoding.ASCII.GetBytes("alpha\nbeta\n" + longText + "\n"), File.ReadAllBytes(path));
    }

    /// <summary>
    /// 表形式などは「同じホストの Out-String -Stream の各要素 + 改行」と一致すること。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" -Width 40")]
    [InlineData(" -Width 200")]
    public void Format_Objects_MatchOutStringStreamOfTheSameHost(string width)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");
        const string data =
            "$data = @([pscustomobject]@{ Name = 'Apple'; Count = 1; Note = 'red' }, "
            + "[pscustomobject]@{ Name = 'Banana'; Count = 22; Note = ('y' * 300) }, 42, 3.5)";

        Assert.Equal(NoError, Run($"{data}; $data | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM{width}"));

        string expected = (string)this._fixture.InvokeScript(
            $"{data}; (($data | Out-String -Stream{width}) | ForEach-Object {{ $_ + [Environment]::NewLine }}) -join ''")[0]
            .BaseObject;

        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Width は ValidateRange(2, int.MaxValue) で検証されること。
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Format_WidthBelowTwo_IsRejectedAtParameterBinding(string width)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal("ParameterArgumentValidationError", Run($"'A' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -Width {width}"));
        Assert.False(File.Exists(path));
    }

    #endregion

    #region 空の入力（仕様書 2.11）

    [Fact]
    public void Empty_NoInput_Overwrite_CreatesOrTruncatesToZeroBytes()
    {
        using var directory = new TemporaryDirectory();
        string missing = directory.Combine("missing.txt");
        string existing = directory.Write("existing.txt", Encoding.ASCII.GetBytes("OLD"));

        Assert.Equal(NoError, Run($"@() | Out-ProbedFile -LiteralPath '{missing}' unicodeBOM"));
        Assert.Equal(NoError, Run($"@() | Out-ProbedFile -LiteralPath '{existing}' unicodeBOM"));

        Assert.Empty(File.ReadAllBytes(missing));
        Assert.Empty(File.ReadAllBytes(existing));
    }

    [Fact]
    public void Empty_NoInput_Append_KeepsExistingOrCreatesZeroBytes()
    {
        using var directory = new TemporaryDirectory();
        string missing = directory.Combine("missing.txt");
        string existing = directory.Write("existing.txt", Encoding.ASCII.GetBytes("OLD"));

        Assert.Equal(NoError, Run($"@() | Out-ProbedFile -LiteralPath '{missing}' unicodeBOM -Append"));
        Assert.Equal(NoError, Run($"@() | Out-ProbedFile -LiteralPath '{existing}' utf8NoBOM -Append"));

        Assert.Empty(File.ReadAllBytes(missing));
        Assert.Equal(Encoding.ASCII.GetBytes("OLD"), File.ReadAllBytes(existing));
    }

    /// <summary>
    /// $null を 1 個渡すと 0 バイトになること（標準の Out-File は BOM だけを書く。既知の差異）。
    /// </summary>
    [Theory]
    [InlineData("$null | Out-ProbedFile")]
    [InlineData("Out-ProbedFile -InputObject $null")]
    public void Empty_Null_WritesZeroBytes(string command)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"{command} -LiteralPath '{path}' -Encoding unicodeBOM"));
        Assert.Empty(File.ReadAllBytes(path));
    }

    /// <summary>
    /// 空文字列を 1 個渡すと、BOM と改行 1 個を書くこと（標準と同じ）。
    /// </summary>
    [Fact]
    public void Empty_EmptyString_WritesBomAndOneLineBreak()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(NoError, Run($"'' | Out-ProbedFile -LiteralPath '{path}' unicodeBOM -LineBreak CrLf"));
        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x0D, 0x00, 0x0A, 0x00 }, File.ReadAllBytes(path));
    }

    #endregion

    #region ファイルを開くタイミング（仕様書 2.10 / 2.14）

    /// <summary>
    /// 同一パスの往復はエラーになり、元のファイルが壊れないこと。
    /// </summary>
    [Fact]
    public void Timing_SamePathRoundTrip_IsTerminatingErrorAndKeepsFile()
    {
        using var directory = new TemporaryDirectory();
        byte[] original = Encoding.ASCII.GetBytes("line1\nline2\n");
        string path = directory.Write("a.txt", original);

        Assert.Equal("SamePathRoundTrip", Run($"Get-ProbedContent -LiteralPath '{path}' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 上流が読み終えてから書く形（-Raw）なら同一パスでも書けること。
    /// </summary>
    [Fact]
    public void Timing_SamePathAfterReadingEverything_Succeeds()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", Encoding.ASCII.GetBytes("abc"));

        Assert.Equal(NoError, Run($"Get-ProbedContent -LiteralPath '{path}' -Raw | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -NoNewline"));
        Assert.Equal(Encoding.ASCII.GetBytes("abc"), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -WhatIf ではファイルを作らず、既存ファイルにも触れないこと。
    /// </summary>
    [Fact]
    public void Timing_WhatIf_DoesNotTouchFiles()
    {
        using var directory = new TemporaryDirectory();
        string missing = directory.Combine("missing.txt");
        string existing = directory.Write("existing.txt", Encoding.ASCII.GetBytes("OLD"));

        Assert.Equal(NoError, Run($"'A' | Out-ProbedFile -LiteralPath '{missing}' utf8NoBOM -WhatIf"));
        Assert.Equal(NoError, Run($"@() | Out-ProbedFile -LiteralPath '{existing}' utf8NoBOM -WhatIf"));

        Assert.False(File.Exists(missing));
        Assert.Equal(Encoding.ASCII.GetBytes("OLD"), File.ReadAllBytes(existing));
    }

    #endregion

    #region エラー（仕様書 5.1）

    [Theory]
    [InlineData("-Culture 'no-such-culture-xx'", "InvalidCulture")]
    [InlineData("-Strategy 'NoSuchStrategy'", "InvalidStrategy")]
    public void Error_InvalidDetectionOptions_AreTerminating(string parameters, string expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("out.txt");

        Assert.Equal(expected, Run($"'A' | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM {parameters}"));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// 失敗は終了エラーであり、上流のレコードごとに同じエラーが繰り返されないこと。
    /// </summary>
    [Fact]
    public void Error_IsReportedOnce_AsTerminatingError()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("out.txt", Encoding.ASCII.GetBytes("OLD"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"$count = 0; try {{ 1..5 | ForEach-Object {{ $count++; $_ }} | Out-ProbedFile -LiteralPath '{path}' utf8NoBOM -NoClobber }} "
            + "catch { $_.FullyQualifiedErrorId.Split(',')[0] }; $count");

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "NoClobber", "0" }, result.Output.Select(item => item.ToString()).ToArray());
    }

    #endregion

    /// <summary>
    /// スクリプトを実行し、終了エラーの FullyQualifiedErrorId（前半）か、エラーが無ければ <see cref="NoError"/> を返す
    /// </summary>
    private string Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ {script} ; '{NoError}' }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        return result.Output.Last().ToString();
    }

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }
}
