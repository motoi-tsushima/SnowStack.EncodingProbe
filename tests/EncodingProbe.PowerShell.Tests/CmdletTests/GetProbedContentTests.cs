using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Get-ProbedContent の仕様（仕様書 4 節）を検証するテスト。
/// </summary>
public class GetProbedContentTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string CommandName = "Get-ProbedContent";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public GetProbedContentTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    #region BOM の読み飛ばし（仕様書 3.1 原則A / 実装上の留意点）

    /// <summary>
    /// BOM が本文に混入しないこと。
    /// </summary>
    /// <remarks>
    /// StreamReader に detectEncodingFromByteOrderMarks: false を渡すと、
    /// BOM が U+FEFF として1行目の先頭に混入する。この検証が最も重要な回帰テストとなる。
    /// </remarks>
    [Fact]
    public void Read_FileWithBom_DoesNotLeakByteOrderMarkIntoContent()
    {
        // "AB" + CRLF を各BOM付きで用意する
        var cases = new Dictionary<string, byte[]>
        {
            ["UTF-8"] = ByteExactFile.Concat(
                ByteExactFile.Utf8Bom, new byte[] { 0x41, 0x42, 0x0D, 0x0A }),
            ["UTF-16LE"] = ByteExactFile.Concat(
                ByteExactFile.Utf16LeBom, new byte[] { 0x41, 0x00, 0x42, 0x00, 0x0D, 0x00, 0x0A, 0x00 }),
            ["UTF-16BE"] = ByteExactFile.Concat(
                ByteExactFile.Utf16BeBom, new byte[] { 0x00, 0x41, 0x00, 0x42, 0x00, 0x0D, 0x00, 0x0A }),
            ["UTF-32LE"] = ByteExactFile.Concat(
                ByteExactFile.Utf32LeBom, new byte[] { 0x41, 0x00, 0x00, 0x00, 0x42, 0x00, 0x00, 0x00 }),
            ["UTF-32BE"] = ByteExactFile.Concat(
                ByteExactFile.Utf32BeBom, new byte[] { 0x00, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00, 0x42 }),
        };

        foreach (KeyValuePair<string, byte[]> testCase in cases)
        {
            using var file = ByteExactFile.Create(testCase.Value);

            string[] lines = ReadLines(file.Path);

            Assert.Equal("AB", lines[0]);
            Assert.DoesNotContain('﻿', lines[0]);
        }
    }

    /// <summary>
    /// -Encoding を明示していても、ファイル先頭の BOM は読み飛ばされること（原則A）。
    /// </summary>
    [Fact]
    public void Read_WithExplicitEncoding_StillSkipsBom()
    {
        using var file = ByteExactFile.CreateFrom(ByteExactFile.Utf8Bom, new byte[] { 0x41, 0x42 });

        string[] lines = ReadLines(file.Path, new Dictionary<string, object?> { ["Encoding"] = "shift_jis" });

        Assert.Equal("AB", Assert.Single(lines));
    }

    /// <summary>
    /// -Encoding utf8BOM で BOM 無しのファイルを読んでもエラーにならないこと（原則A）。
    /// </summary>
    [Fact]
    public void Read_BomVocabularyOnFileWithoutBom_DoesNotFail()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41, 0x42 });

        InvocationResult result = Invoke(
            file.Path, new Dictionary<string, object?> { ["Encoding"] = "utf8BOM" });

        Assert.Empty(result.Errors);
        Assert.Equal("AB", Assert.Single(result.AsStrings()));
    }

    /// <summary>
    /// BOM のみのファイルは、内容が空として扱われること。
    /// </summary>
    [Fact]
    public void Read_BomOnlyFile_ProducesNoLines()
    {
        using var file = ByteExactFile.Create(ByteExactFile.Utf8Bom);

        InvocationResult result = Invoke(file.Path);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Output);
    }

    #endregion

    #region 文字エンコーディングの判定と明示指定（仕様書 4.3）

    /// <summary>
    /// -Encoding 省略時は対象ファイルを判定して正しく復号すること。
    /// </summary>
    /// <remarks>
    /// EUC-JP は改行コードに LF を使う。EUC-JP と Shift-JIS の両方に該当するバイト列の場合、
    /// クラスライブラリ側は改行コードで判定し、CR-LF なら Shift-JIS と解釈するためである。
    /// これはクラスライブラリの仕様であり、本コマンドはその判定結果に従う。
    /// </remarks>
    [Theory]
    [InlineData(932, "\r\n")]
    [InlineData(51932, "\n")]
    [InlineData(65001, "\r\n")]
    [InlineData(65001, "\n")]
    public void Read_WithoutEncoding_DetectsAndDecodes(int codePage, string lineBreak)
    {
        Encoding encoding = Encoding.GetEncoding(codePage);
        const string Text = "日本語のテキストです";

        using var file = ByteExactFile.Create(encoding.GetBytes(Text + lineBreak));

        Assert.Equal(Text, Assert.Single(ReadLines(file.Path)));
    }

    /// <summary>
    /// -Encoding を明示した場合は判定を行わないこと。
    /// 判定に委ねると Shift-JIS と解釈されうるバイト列を EUC-JP として読ませて確認する。
    /// </summary>
    [Fact]
    public void Read_WithExplicitEncoding_DoesNotDetect()
    {
        Encoding eucJp = Encoding.GetEncoding(51932);
        const string Text = "日本語";

        // CR-LF を付けると、クラスライブラリの判定では Shift-JIS 側に倒れるバイト列。
        // -Encoding を明示すればその判定は行われないことを確認する。
        using var file = ByteExactFile.Create(eucJp.GetBytes(Text + "\r\n"));

        // 明示指定した EUC-JP で復号されるため元のテキストに戻る
        Assert.Equal(
            Text,
            Assert.Single(ReadLines(file.Path, new Dictionary<string, object?> { ["Encoding"] = "euc-jp" })));

        // 別のエンコーディングを明示すれば、判定に委ねずその指定で復号される（＝判定していない）
        string asShiftJis = Assert.Single(
            ReadLines(file.Path, new Dictionary<string, object?> { ["Encoding"] = "shift_jis" }));

        Assert.NotEqual(Text, asShiftJis);
    }

    /// <summary>
    /// 先頭が英数字だけで、後方にマルチバイト文字が現れるファイルを正しく判定すること。
    /// </summary>
    /// <remarks>
    /// 判定をファイル先頭の一定量に限ると、このようなファイル
    /// （例: 大きなソースファイルの末尾にだけ日本語のコメントがある）を US-ASCII と誤判定し、
    /// 後続のマルチバイト文字をすべて壊して復号してしまう。
    /// 判定はファイル全体を対象としなければならない。
    /// </remarks>
    [Theory]
    [InlineData(932)]
    [InlineData(65001)]
    public void Read_MultiByteOnlyNearEndOfLargeFile_IsStillDetectedCorrectly(int codePage)
    {
        Encoding encoding = Encoding.GetEncoding(codePage);
        const string JapaneseLine = "日本語のコメントです";

        // 先頭に十分な量の英数字を置き、最終行にだけマルチバイト文字を置く
        var builder = new StringBuilder();

        for (int i = 0; i < 60000; i++)
        {
            builder.Append("// ASCII only source line for padding\r\n");
        }

        builder.Append(JapaneseLine).Append("\r\n");

        using var file = ByteExactFile.Create(encoding.GetBytes(builder.ToString()));

        Assert.True(new FileInfo(file.Path).Length > 2 * 1024 * 1024, "テスト前提: 十分に大きいこと");

        InvocationResult result = Invoke(file.Path);
        string[] lines = result.AsStrings();

        Assert.Empty(result.Errors);
        Assert.Equal(JapaneseLine, lines[lines.Length - 1]);
    }

    /// <summary>
    /// 判定に失敗した場合はエラーになり、他のファイルの処理は継続されること。
    /// </summary>
    /// <remarks>
    /// 標準の Get-Content が、存在しないファイルを非終了エラーとして報告し
    /// 残りのファイルを処理し続けるのに合わせている。
    /// </remarks>
    [Fact]
    public void Read_UndetectableFile_ReportsErrorAndContinues()
    {
        using var undetectable = ByteExactFile.Create(
            new byte[] { 0x81, 0xFF, 0x00, 0xFE, 0x93, 0x40, 0xC0, 0x80, 0xED, 0xA0, 0x80 });
        using var readable = ByteExactFile.Create(Encoding.ASCII.GetBytes("ok\r\n"));

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            CommandName,
            new Dictionary<string, object?> { ["Path"] = new[] { undetectable.Path, readable.Path } });

        Assert.Equal("EncodingDetectionFailed", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal("ok", Assert.Single(result.AsStrings()));
    }

    /// <summary>
    /// 存在しないファイルは非終了エラーとして報告され、処理は継続されること。
    /// </summary>
    [Fact]
    public void Read_MissingFile_ReportsErrorAndContinues()
    {
        using var missing = ByteExactFile.CreateMissing();
        using var readable = ByteExactFile.Create(Encoding.ASCII.GetBytes("ok\r\n"));

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            CommandName,
            new Dictionary<string, object?> { ["Path"] = new[] { missing.Path, readable.Path } });

        Assert.Single(result.Errors);
        Assert.Equal("ok", Assert.Single(result.AsStrings()));
    }

    #endregion

    #region -Raw（仕様書 4.2 / 4.4）

    /// <summary>
    /// -Raw はファイル全体を1個の文字列として返し、改行を保持すること。
    /// 行分割では失われる「行末が CRLF か LF か」「末尾に改行があったか」を保全する。
    /// </summary>
    [Theory]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\nb\n")]
    [InlineData("a\r\nb")]
    [InlineData("a")]
    [InlineData("")]
    public void Read_Raw_PreservesContentExactly(string text)
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes(text));

        InvocationResult result = Invoke(file.Path, new Dictionary<string, object?> { ["Raw"] = true });

        Assert.Equal(text, Assert.Single(result.AsStrings()));
    }

    /// <summary>
    /// 複数ファイルに -Raw を指定した場合、ファイルごとに1個の文字列が出力されること。
    /// </summary>
    [Fact]
    public void Read_RawWithMultipleFiles_ReturnsOneStringPerFile()
    {
        using var first = ByteExactFile.Create(Encoding.ASCII.GetBytes("a\r\n"));
        using var second = ByteExactFile.Create(Encoding.ASCII.GetBytes("b\r\n"));

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            CommandName,
            new Dictionary<string, object?>
            {
                ["Path"] = new[] { first.Path, second.Path },
                ["Raw"] = true,
            });

        Assert.Equal(new[] { "a\r\n", "b\r\n" }, result.AsStrings());
    }

    /// <summary>
    /// -Raw と -TotalCount は同時に指定できないこと（標準の Get-Content と同じ）。
    /// </summary>
    [Fact]
    public void Read_RawWithTotalCount_Throws()
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("a\r\n"));

        Assert.ThrowsAny<Exception>(() => Invoke(
            file.Path,
            new Dictionary<string, object?> { ["Raw"] = true, ["TotalCount"] = 1L }));
    }

    #endregion

    #region -TotalCount（仕様書 4.1）

    /// <summary>
    /// -TotalCount が指定行数で打ち切ること。
    /// </summary>
    [Theory]
    [InlineData(0, new string[0])]
    [InlineData(1, new[] { "1" })]
    [InlineData(2, new[] { "1", "2" })]
    [InlineData(5, new[] { "1", "2", "3" })]
    public void Read_TotalCount_LimitsLines(long totalCount, string[] expected)
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("1\r\n2\r\n3\r\n"));

        Assert.Equal(
            expected,
            ReadLines(file.Path, new Dictionary<string, object?> { ["TotalCount"] = totalCount }));
    }

    /// <summary>
    /// -TotalCount は各ファイルに適用されること（標準の Get-Content と同じ）。
    /// </summary>
    [Fact]
    public void Read_TotalCountWithMultipleFiles_AppliesPerFile()
    {
        using var first = ByteExactFile.Create(Encoding.ASCII.GetBytes("a1\r\na2\r\n"));
        using var second = ByteExactFile.Create(Encoding.ASCII.GetBytes("b1\r\nb2\r\n"));

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            CommandName,
            new Dictionary<string, object?>
            {
                ["Path"] = new[] { first.Path, second.Path },
                ["TotalCount"] = 1L,
            });

        Assert.Equal(new[] { "a1", "b1" }, result.AsStrings());
    }

    #endregion

    #region パス指定とパイプライン（仕様書 4.1 / 4.3）

    /// <summary>
    /// 複数ファイルを指定した場合、内容が連結されること。
    /// </summary>
    [Fact]
    public void Read_MultipleFiles_ConcatenatesContent()
    {
        using var first = ByteExactFile.Create(Encoding.ASCII.GetBytes("a1\r\na2\r\n"));
        using var second = ByteExactFile.Create(Encoding.ASCII.GetBytes("b1\r\n"));

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            CommandName,
            new Dictionary<string, object?> { ["Path"] = new[] { first.Path, second.Path } });

        Assert.Equal(new[] { "a1", "a2", "b1" }, result.AsStrings());
    }

    /// <summary>
    /// エンコーディングが異なるファイルを連結しても、それぞれが正しく復号されること。
    /// 文字列に変換された時点で元のエンコーディングは意味を失うため、連結結果は正しい。
    /// </summary>
    [Fact]
    public void Read_FilesWithDifferentEncodings_AreEachDecodedCorrectly()
    {
        using var shiftJis = ByteExactFile.Create(Encoding.GetEncoding(932).GetBytes("日本語\r\n"));
        using var utf8 = ByteExactFile.CreateFrom(
            ByteExactFile.Utf8Bom, new UTF8Encoding(false).GetBytes("한국어\r\n"));

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            CommandName,
            new Dictionary<string, object?> { ["Path"] = new[] { shiftJis.Path, utf8.Path } });

        Assert.Equal(new[] { "日本語", "한국어" }, result.AsStrings());
    }

    /// <summary>
    /// ワイルドカードで複数ファイルに展開されること。
    /// </summary>
    [Fact]
    public void Read_Wildcard_ExpandsToMultipleFiles()
    {
        string directory = CreateWorkDirectory();

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "w1.txt"), Encoding.ASCII.GetBytes("w1\r\n"));
            File.WriteAllBytes(Path.Combine(directory, "w2.txt"), Encoding.ASCII.GetBytes("w2\r\n"));

            string[] lines = ReadLines(Path.Combine(directory, "w?.txt"));

            Assert.Equal(new[] { "w1", "w2" }, lines);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Get-ChildItem からのパイプライン入力に対応すること。
    /// </summary>
    [Fact]
    public void Read_AcceptsPipelineInputFromGetChildItem()
    {
        string directory = CreateWorkDirectory();

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "p1.txt"), Encoding.ASCII.GetBytes("p1\r\n"));
            File.WriteAllBytes(Path.Combine(directory, "p2.txt"), Encoding.ASCII.GetBytes("p2\r\n"));

            var results = this._fixture.InvokeScript(
                $"Get-ChildItem -LiteralPath '{directory}' -Filter *.txt | Get-ProbedContent");

            Assert.Equal(new[] { "p1", "p2" }, ToStrings(results));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// -LiteralPath はワイルドカードを展開しないこと。
    /// </summary>
    [Fact]
    public void Read_LiteralPath_DoesNotExpandWildcards()
    {
        string directory = CreateWorkDirectory();

        try
        {
            // ワイルドカード文字を名前に含むファイルを、そのままのパスとして読めること
            string literalName = Path.Combine(directory, "a[1].txt");
            File.WriteAllBytes(literalName, Encoding.ASCII.GetBytes("literal\r\n"));

            InvocationResult result = this._fixture.InvokeCapturingErrors(
                CommandName, new Dictionary<string, object?> { ["LiteralPath"] = new[] { literalName } });

            Assert.Empty(result.Errors);
            Assert.Equal("literal", Assert.Single(result.AsStrings()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    #endregion

    #region 境界条件

    /// <summary>
    /// 空ファイルは、エラーにならず出力も無いこと。
    /// </summary>
    [Fact]
    public void Read_EmptyFile_ProducesNoOutput()
    {
        using var file = ByteExactFile.Create(Array.Empty<byte>());

        InvocationResult result = Invoke(file.Path);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Output);
    }

    /// <summary>
    /// 空ファイルを -Raw で読むと、空文字列が1個返ること。
    /// </summary>
    [Fact]
    public void Read_EmptyFileWithRaw_ReturnsEmptyString()
    {
        using var file = ByteExactFile.Create(Array.Empty<byte>());

        InvocationResult result = Invoke(file.Path, new Dictionary<string, object?> { ["Raw"] = true });

        Assert.Equal(string.Empty, Assert.Single(result.AsStrings()));
    }

    /// <summary>
    /// 1バイトのファイルを読めること。
    /// </summary>
    [Fact]
    public void Read_SingleByteFile_ReturnsSingleLine()
    {
        using var file = ByteExactFile.Create(new byte[] { 0x41 });

        Assert.Equal("A", Assert.Single(ReadLines(file.Path)));
    }

    /// <summary>
    /// 末尾に改行が無いファイルでも、最終行が失われないこと。
    /// </summary>
    [Fact]
    public void Read_FileWithoutTrailingNewLine_KeepsLastLine()
    {
        using var file = ByteExactFile.Create(Encoding.ASCII.GetBytes("a\r\nb"));

        Assert.Equal(new[] { "a", "b" }, ReadLines(file.Path));
    }

    /// <summary>
    /// ディレクトリを指定した場合はエラーになること。
    /// </summary>
    [Fact]
    public void Read_Directory_ReportsError()
    {
        string directory = CreateWorkDirectory();

        try
        {
            InvocationResult result = Invoke(directory);

            Assert.Empty(result.Output);
            Assert.Equal("PathIsNotFile", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    #endregion

    private InvocationResult Invoke(string path, IDictionary<string, object?>? parameters = null)
    {
        var arguments = new Dictionary<string, object?> { ["Path"] = new[] { path } };

        if (parameters != null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                arguments[parameter.Key] = parameter.Value;
            }
        }

        return this._fixture.InvokeCapturingErrors(CommandName, arguments);
    }

    private string[] ReadLines(string path, IDictionary<string, object?>? parameters = null)
        => Invoke(path, parameters).AsStrings();

    private static string[] ToStrings(System.Collections.ObjectModel.Collection<System.Management.Automation.PSObject> results)
    {
        var values = new string[results.Count];

        for (int i = 0; i < results.Count; i++)
        {
            values[i] = (string)results[i].BaseObject;
        }

        return values;
    }

    private static string CreateWorkDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EncodingProbeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}