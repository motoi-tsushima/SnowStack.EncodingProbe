using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 書き込み系の 4 コマンドが、表現できない文字を黙って失わないことを検証するテスト（1.3.0 第五次。BUG-0001・0010・0012）。
/// </summary>
/// <remarks>
/// 第四次までは、書き込み先の文字エンコーディングで表現できない文字を '?'（3F）やベストフィット（é → e）に置き換えて書いていた。
/// 第五次から、Convert-ProbedContent と同じく例外のフォールバックで符号化し、ID UnrepresentableCharacter のエラーにする。
/// ファイルは書き込みの前の状態に戻す（上書きは一時ファイルを捨て、追記は元の長さに切り詰める）。
/// 失敗はスクリプトの中の try / catch で ID に変えて受け取る（Out-ProbedFile / Export-ProbedCsv は終了エラーのため）。
/// </remarks>
public class UnrepresentableCharacterTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private const string Korean = "한국";  // 한국

    private static readonly Encoding ShiftJis = GetShiftJis();

    /// <summary>Shift_JIS と判定される内容（-Culture ja-JP、CR-LF）</summary>
    private static readonly byte[] ShiftJisContent = ShiftJis.GetBytes("日本語のテキストです。\r\n二行目です。\r\n");

    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public UnrepresentableCharacterTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    /// <summary>コマンドと、そのコマンドで 1 件を書くスクリプトの組み立て方</summary>
    public static IEnumerable<object[]> Commands()
    {
        yield return new object[] { "Set" };
        yield return new object[] { "Add" };
        yield return new object[] { "Out" };
        yield return new object[] { "OutAppend" };
        yield return new object[] { "Export" };
        yield return new object[] { "ExportAppend" };
    }

    /// <summary>
    /// -Encoding で明示した文字エンコーディングで表現できない文字は、エラーにしてファイルを変えない
    /// </summary>
    [Theory]
    [MemberData(nameof(Commands))]
    public void ExplicitEncoding_Unrepresentable_FailsAndKeepsFile(string command)
    {
        byte[] original = Original(command);
        string path = this._directory.Write("explicit.txt", original);

        string id = RunExpectingError(Build(command, path, Korean, "-Encoding shift_jis"));

        Assert.Equal("UnrepresentableCharacter", id);
        Assert.Equal(original, File.ReadAllBytes(path));
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// 書き込み先が無い場合は、ファイルを作らない
    /// </summary>
    [Theory]
    [MemberData(nameof(Commands))]
    public void ExplicitEncoding_Unrepresentable_DoesNotCreateFile(string command)
    {
        string path = this._directory.Combine("missing.txt");

        string id = RunExpectingError(Build(command, path, Korean, "-Encoding shift_jis"));

        Assert.Equal("UnrepresentableCharacter", id);
        Assert.False(File.Exists(path));
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// 書き込み先・追記先から継承した文字エンコーディングで表現できない文字も、エラーにしてファイルを変えない（BUG-0001）
    /// </summary>
    [Theory]
    [InlineData("Set", "")]
    [InlineData("Add", "")]
    [InlineData("OutAppend", "")]
    [InlineData("Out", "-Encoding Auto")]
    public void InheritedEncoding_Unrepresentable_FailsAndKeepsFile(string command, string extra)
    {
        string path = this._directory.Write("inherited.txt", ShiftJisContent);

        string id = RunExpectingError(Build(command, path, Korean, extra + " -Culture ja-JP"));

        Assert.Equal("UnrepresentableCharacter", id);
        Assert.Equal(ShiftJisContent, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -EncodingFrom で継承した文字エンコーディングで表現できない文字も、エラーにする（BUG-0010）
    /// </summary>
    [Theory]
    [InlineData("Set")]
    [InlineData("Out")]
    [InlineData("Export")]
    public void EncodingFrom_Unrepresentable_FailsAndDoesNotCreateFile(string command)
    {
        string reference = this._directory.Write("reference.txt", ShiftJisContent);
        string path = this._directory.Combine("from.txt");

        string id = RunExpectingError(Build(command, path, "café", $"-EncodingFrom {Quote(reference)} -Culture ja-JP"));

        Assert.Equal("UnrepresentableCharacter", id);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// 例外フォールバックの Encoding を渡しても、生の EncoderFallbackException ではなく ID の付いたエラーになる。
    /// 置換フォールバックの Encoding を渡しても、'?' で書かない（どちらもコードページから組み立て直す。BUG-0012）
    /// </summary>
    [Theory]
    [InlineData("Set", true)]
    [InlineData("Add", true)]
    [InlineData("OutAppend", true)]
    [InlineData("Export", true)]
    [InlineData("Set", false)]
    [InlineData("Out", false)]
    [InlineData("ExportAppend", false)]
    public void EncodingInstance_FallbackIsIgnored(string command, bool exceptionFallback)
    {
        byte[] original = Original(command);
        string path = this._directory.Write("instance.txt", original);
        string encoding = exceptionFallback
            ? "([Text.Encoding]::GetEncoding(932, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback))"
            : "([Text.Encoding]::GetEncoding(932))";

        string id = RunExpectingError(Build(command, path, Korean, "-Encoding " + encoding));

        Assert.Equal("UnrepresentableCharacter", id);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// メッセージに、最初の表現できない文字の符号位置と文字エンコーディングの名前が入る
    /// </summary>
    [Fact]
    public void Message_ContainsCodePointAndEncodingName()
    {
        string path = this._directory.Combine("message.txt");

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath {Quote(path)} -Value 'ab{Korean}' -Encoding shift_jis");

        string message = Assert.Single(result.Errors).Exception.Message;

        Assert.Contains("U+D55C", message);
        Assert.Contains("shift_jis", message);
        Assert.Contains(path, message);
    }

    /// <summary>
    /// 補助文字（サロゲートペア）は 1 文字として、その符号位置を示す
    /// </summary>
    [Fact]
    public void Message_SupplementaryCharacter_ShowsCodePoint()
    {
        string path = this._directory.Combine("emoji.txt");

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath {Quote(path)} -Value ([char]::ConvertFromUtf32(0x1F600)) -Encoding shift_jis");

        Assert.Contains("U+1F600", Assert.Single(result.Errors).Exception.Message);
    }

    /// <summary>
    /// 途中のレコードで表現できない文字に当たったら、それまでのレコードで書いた内容も取り消す（上書き）
    /// </summary>
    [Fact]
    public void Set_LaterRecordUnrepresentable_RestoresOriginal()
    {
        byte[] original = Encoding.ASCII.GetBytes("old1\r\nold2\r\n");
        string path = this._directory.Write("set.txt", original);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'ok', '{Korean}', 'after' | Set-ProbedContent -LiteralPath {Quote(path)} -Encoding shift_jis");

        Assert.Equal("UnrepresentableCharacter", Id(Assert.Single(result.Errors)));
        Assert.Equal(original, File.ReadAllBytes(path));
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// 途中のレコードで表現できない文字に当たったら、追記先を元の長さに戻す（追記）
    /// </summary>
    [Fact]
    public void Add_LaterRecordUnrepresentable_TruncatesToOriginal()
    {
        string path = this._directory.Write("add.txt", ShiftJisContent);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'ok', '{Korean}', 'after' | Add-ProbedContent -LiteralPath {Quote(path)} -Culture ja-JP");

        Assert.Equal("UnrepresentableCharacter", Id(Assert.Single(result.Errors)));
        Assert.Equal(ShiftJisContent, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Out-ProbedFile / Export-ProbedCsv も、それまでに書いた行を取り消す
    /// </summary>
    [Theory]
    [InlineData("Out", false)]
    [InlineData("Out", true)]
    [InlineData("Export", false)]
    [InlineData("Export", true)]
    public void Output_LaterLineUnrepresentable_RestoresOriginal(string command, bool append)
    {
        byte[] original = command == "Out" ? Encoding.ASCII.GetBytes("old1\r\nold2\r\n") : Encoding.ASCII.GetBytes("\"V\"\r\n\"old\"\r\n");
        string path = this._directory.Write("output.txt", original);
        string appendSwitch = append ? " -Append" : string.Empty;
        string script = command == "Out"
            ? $"'ok', '{Korean}', 'after' | Out-ProbedFile -LiteralPath {Quote(path)} -Encoding shift_jis{appendSwitch} -AllowEncodingChange"
            : $"'ok', '{Korean}', 'after' | ForEach-Object {{ [pscustomobject]@{{ V = $_ }} }} | "
                + $"Export-ProbedCsv -LiteralPath {Quote(path)} -Encoding shift_jis{appendSwitch} -AllowEncodingChange";

        Assert.Equal("UnrepresentableCharacter", RunExpectingError(script));
        Assert.Equal(original, File.ReadAllBytes(path));
        AssertNoTemporaryFile();
    }

    /// <summary>
    /// 複数のファイルを扱う呼び出しでは、表現できない文字に当たったファイルだけを失敗にし、ほかのファイルは続ける
    /// </summary>
    [Theory]
    [InlineData("Set-ProbedContent")]
    [InlineData("Add-ProbedContent")]
    public void MultipleFiles_OtherFilesContinue(string command)
    {
        string sjis = this._directory.Write("a-sjis.txt", ShiftJisContent);
        byte[] utf8Content = new UTF8Encoding(false).GetBytes("日本語のテキストです。\r\n");
        string utf8 = this._directory.Write("b-utf8.txt", utf8Content);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"{command} -LiteralPath {Quote(sjis)}, {Quote(utf8)} -Value '{Korean}' -Culture ja-JP");

        ErrorRecordAssert(result, "UnrepresentableCharacter", sjis);
        Assert.Equal(ShiftJisContent, File.ReadAllBytes(sjis));

        byte[] written = new UTF8Encoding(false).GetBytes(Korean + "\r\n");
        byte[] expected = command == "Set-ProbedContent" ? written : utf8Content.Concat(written).ToArray();
        Assert.Equal(expected, File.ReadAllBytes(utf8));
    }

    /// <summary>
    /// 表現できない文字の検査は、追記の整合性検査より先に行う（-Encoding ascii で日本語を追記すると、EncodingChangeOnAppend ではなく UnrepresentableCharacter）
    /// </summary>
    [Fact]
    public void Add_UnrepresentableIsReportedBeforeConsistency()
    {
        byte[] original = new UTF8Encoding(false).GetBytes("日本語\r\n");
        string path = this._directory.Write("order.txt", original);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Add-ProbedContent -LiteralPath {Quote(path)} -Value '日本' -Encoding ascii");

        Assert.Equal("UnrepresentableCharacter", Id(Assert.Single(result.Errors)));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 表現できる文字だけなら、上書きの結果は第四次までと同じバイト列。一時ファイルは残らず、元のファイルの属性を保つ
    /// </summary>
    [Fact]
    public void Set_Success_ReplacesContentAndKeepsAttributes()
    {
        string path = this._directory.Write("attributes.txt", Encoding.ASCII.GetBytes("old\r\n"));
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.ReadOnly);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Set-ProbedContent -LiteralPath {Quote(path)} -Value '日本' -Encoding shift_jis -Force");

        Assert.Empty(result.Errors);
        Assert.Equal(ShiftJis.GetBytes("日本\r\n"), File.ReadAllBytes(path));
        FileAttributes attributes = File.GetAttributes(path);
        Assert.True((attributes & FileAttributes.Hidden) == FileAttributes.Hidden);
        Assert.True((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly);
        AssertNoTemporaryFile();

        File.SetAttributes(path, FileAttributes.Normal);
    }

    /// <summary>
    /// 書いている途中の一時ファイルは、ワイルドカードに一致しない（パイプラインの次のレコードが一時ファイルに書かない）
    /// </summary>
    [Fact]
    public void Set_Wildcard_DoesNotMatchTemporaryFile()
    {
        string path = this._directory.Write("only.txt", Encoding.ASCII.GetBytes("old\r\n"));
        string pattern = this._directory.Combine("*");

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'a', 'b' | Set-ProbedContent -Path {Quote(pattern)} -Encoding utf8NoBOM");

        Assert.Empty(result.Errors);
        Assert.Equal(Encoding.ASCII.GetBytes("a\r\nb\r\n"), File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(this._directory.Path));
    }

    private void ErrorRecordAssert(InvocationResult result, string id, string target)
    {
        System.Management.Automation.ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal(id, Id(error));
        Assert.Equal(target, error.TargetObject);
    }

    private void AssertNoTemporaryFile()
        => Assert.Empty(Directory.GetFiles(this._directory.Path, "*.probedtmp"));

    /// <summary>失敗を期待してスクリプトを実行し、最初のエラーの ID を返す（終了エラーも非終了エラーも）</summary>
    private string RunExpectingError(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            "try { " + script + " -ErrorAction Stop } catch { $_.FullyQualifiedErrorId.Split(',')[0] }");

        Assert.Empty(result.Errors);
        return (string)Assert.Single(result.Output).BaseObject;
    }

    /// <summary>1 件を書くスクリプトを組み立てる</summary>
    private static string Build(string command, string path, string value, string extra)
    {
        string literal = Quote(path);

        switch (command)
        {
            case "Set":
                return $"Set-ProbedContent -LiteralPath {literal} -Value '{value}' {extra}";
            case "Add":
                return $"Add-ProbedContent -LiteralPath {literal} -Value '{value}' {extra}";
            case "Out":
                return $"'{value}' | Out-ProbedFile -LiteralPath {literal} {extra}";
            case "OutAppend":
                return $"'{value}' | Out-ProbedFile -LiteralPath {literal} -Append {extra}";
            case "Export":
                return $"[pscustomobject]@{{ V = '{value}' }} | Export-ProbedCsv -LiteralPath {literal} {extra}";
            default:
                return $"[pscustomobject]@{{ V = '{value}' }} | Export-ProbedCsv -LiteralPath {literal} -Append {extra}";
        }
    }

    /// <summary>コマンドごとの既存の内容（CSV の追記ではヘッダーが V のもの）</summary>
    private static byte[] Original(string command)
        => command.StartsWith("Export", StringComparison.Ordinal)
            ? Encoding.ASCII.GetBytes("\"V\"\r\n\"old\"\r\n")
            : Encoding.ASCII.GetBytes("old1\r\nold2\r\n");

    private static string Id(System.Management.Automation.ErrorRecord error) => error.FullyQualifiedErrorId.Split(',')[0];

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    private static Encoding GetShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}
