using System;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// テスト担当の報告（BUG-0002・0009・0013）の再現の入力と、Export-ProbedCsv -Append の空の列名で、
/// 第五次の修正後の挙動を固定するテスト。
/// </summary>
public class DetectionLineCountRegressionTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public DetectionLineCountRegressionTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    /// <summary>
    /// BUG-0002: 0x00 を含む ASCII の CSV は、-DetectionLineCount の有無で読み込んだ値が変わらず、
    /// 追記は ASCII の行（UTF-8 とみなす）を書く（UTF-16 の改行を書かない）
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x41, 0x2C, 0x42, 0x00, 0x0A, 0x31, 0x2C, 0x32, 0x00, 0x0A }, "\n")]
    [InlineData(new byte[] { 0x41, 0x2C, 0x42, 0x00, 0x00, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x00, 0x00, 0x0D, 0x0A }, "\r\n")]
    [InlineData(new byte[] { 0x41, 0x2C, 0x42, 0x00, 0x00, 0x0D, 0x31, 0x2C, 0x32, 0x00, 0x00, 0x0D, 0x33, 0x2C, 0x34, 0x00, 0x00, 0x0D }, "\r")]
    [InlineData(new byte[] { 0x78, 0x41, 0x2C, 0x42, 0x0A, 0x31, 0x2C, 0x32, 0x0A, 0x33, 0x2C, 0x34, 0x00, 0x0A }, "\n")]
    [InlineData(new byte[] { 0x41, 0x2C, 0x42, 0x00, 0x0A, 0x31, 0x2C, 0x32, 0x00, 0x0A, 0x33, 0x2C, 0x34, 0x00, 0x0A }, "\n")]
    public void Bug0002_NulPaddedAsciiCsv(byte[] content, string newLine)
    {
        string path = this._directory.Write("bug0002.csv", content);

        string whole = Scalar($"Import-ProbedCsv -LiteralPath {Quote(path)} | ConvertTo-Json -Compress");
        string head = Scalar($"Import-ProbedCsv -LiteralPath {Quote(path)} -DetectionLineCount 1 | ConvertTo-Json -Compress");

        Assert.Equal(whole, head);

        string resolved = Scalar($"$r = Resolve-Encoding -Path {Quote(path)} -DetectionLineCount 1; '' + $r.CodePage + ' ' + $r.LineBreak");
        Assert.StartsWith("20127 ", resolved);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"$h = (Import-ProbedCsv -LiteralPath {Quote(path)} | Select-Object -First 1).PSObject.Properties.Name; "
            + "$o = [ordered]@{}; foreach ($n in $h) { $o[$n] = 'z' }; "
            + $"[pscustomobject]$o | Export-ProbedCsv -LiteralPath {Quote(path)} -Append -Force -DetectionLineCount 1");

        Assert.Empty(result.Errors);
        byte[] written = File.ReadAllBytes(path);
        Assert.Equal(content, written.Take(content.Length).ToArray());

        // 追記した部分は ASCII のバイトだけ（UTF-16 の 00 0A などを書かない）
        byte[] appended = written.Skip(content.Length).ToArray();
        Assert.NotEmpty(appended);
        Assert.DoesNotContain((byte)0x00, appended);
        Assert.EndsWith(newLine, Encoding.ASCII.GetString(appended));
    }

    /// <summary>
    /// BUG-0013: 最初の改行より前に U+0D00 / U+0A00 がある BOM 無し UTF-16 の CSV を、-DetectionLineCount 1 で正しく読む
    /// </summary>
    [Theory]
    [InlineData(true, "日ഀ")]
    [InlineData(false, "日ഀ")]
    [InlineData(false, "日਀")]
    public void Bug0013_Utf16WithLineBreakLikeCharacter(bool bigEndian, string name)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: false);
        byte[] content = encoding.GetBytes(name + ",B\r\n1,2\r\n");
        string path = this._directory.Write("bug0013.csv", content);
        string explicitName = bigEndian ? "bigendianunicodeNoBOM" : "unicodeNoBOM";

        string expected = Scalar($"Import-ProbedCsv -LiteralPath {Quote(path)} -Encoding {explicitName} | ConvertTo-Json -Compress");
        string actual = Scalar($"Import-ProbedCsv -LiteralPath {Quote(path)} -DetectionLineCount 1 | ConvertTo-Json -Compress");

        Assert.Equal(expected, actual);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"[pscustomobject]@{{ '{name}' = '9'; B = '8' }} | Export-ProbedCsv -LiteralPath {Quote(path)} -Append -Force -DetectionLineCount 1");

        Assert.Empty(result.Errors);
        Assert.Equal(content.Concat(encoding.GetBytes("\"9\",\"8\"\r\n")).ToArray(), File.ReadAllBytes(path));
    }

    /// <summary>
    /// BUG-0009: 先頭 N 行が ASCII だけの ISO-2022-JP / KR の CSV は、us-ascii を UTF-8 に読み替えた検証で
    /// エスケープシーケンスを見つけて DetectionVerificationFailed にする（読まない・書かない）
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x22, 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x4B, 0x5C, 0x1B, 0x28, 0x42, 0x22, 0x2C, 0x78, 0x0D, 0x0A }, "ja-JP", 6L)]
    [InlineData(new byte[] { 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x22, 0x1B, 0x24, 0x29, 0x43, 0x0E, 0x47, 0x51, 0x31, 0x39, 0x0F, 0x22, 0x2C, 0x78, 0x0D, 0x0A }, "ko-KR", 6L)]
    public void Bug0009_Iso2022WithAsciiHead_FailsVerification(byte[] content, string culture, long escapeOffset)
    {
        string path = this._directory.Write("bug0009.csv", content);

        InvocationResult import = this._fixture.InvokeScriptCapturingErrors(
            $"Import-ProbedCsv -LiteralPath {Quote(path)} -DetectionLineCount 1 -Culture {culture}");

        Assert.Empty(import.Output);
        System.Management.Automation.ErrorRecord error = Assert.Single(import.Errors);
        Assert.Equal("DetectionVerificationFailed", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Contains("-DetectionLineCount", error.Exception.Message);
        Assert.Contains(escapeOffset.ToString(), error.Exception.Message);

        InvocationResult export = this._fixture.InvokeScriptCapturingErrors(
            "try { [pscustomobject]@{ A = 'y'; B = 'y' } | Export-ProbedCsv -LiteralPath "
            + Quote(path) + $" -Append -DetectionLineCount 1 -Culture {culture} }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        Assert.Equal("DetectionVerificationFailed", (string)Assert.Single(export.Output).BaseObject);
        Assert.Equal(content, File.ReadAllBytes(path));

        // 日本語の行まで含めれば、ISO-2022 と判定して正しく読める（報告のとおり）
        Assert.Empty(this._fixture.InvokeScriptCapturingErrors(
            $"Import-ProbedCsv -LiteralPath {Quote(path)} -DetectionLineCount 2 -Culture {culture}").Errors);
    }

    /// <summary>
    /// UTF-8 に読み替えない場合（全体の判定）は、エスケープシーケンスを探さない
    /// </summary>
    [Fact]
    public void Iso2022_WholeFile_ReadsCorrectly()
    {
        byte[] content = { 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x22, 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x4B, 0x5C, 0x1B, 0x28, 0x42, 0x22, 0x2C, 0x78, 0x0D, 0x0A };
        string path = this._directory.Write("iso2022.csv", content);

        Assert.Equal("日本", Scalar($"(Import-ProbedCsv -LiteralPath {Quote(path)} -Culture ja-JP).A"));
    }

    /// <summary>
    /// 追記先のヘッダーに空の列名があるとき、-Force なしは本家と同じ CannotAppendCsvWithMismatchedPropertyNames、
    /// -Force ありは CannotAppendCsvWithEmptyPropertyName（本家は汎用の Argument）。どちらもファイルは変えない。
    /// 末尾の空の列名は本家と同じく無視して追記する
    /// </summary>
    [Theory]
    [InlineData("A,,B", false, "CannotAppendCsvWithMismatchedPropertyNames")]
    [InlineData("A,,B", true, "CannotAppendCsvWithEmptyPropertyName")]
    [InlineData(",A,B", true, "CannotAppendCsvWithEmptyPropertyName")]
    [InlineData("A,\"\",B", true, "CannotAppendCsvWithEmptyPropertyName")]
    [InlineData("A,B,", true, null)]
    [InlineData("A,B,", false, null)]
    public void EmptyColumnName_Append(string header, bool force, string? expectedId)
    {
        byte[] content = Encoding.ASCII.GetBytes(header + "\r\n1,2,3\r\n");
        string path = this._directory.Write("empty.csv", content);

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            "try { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath " + Quote(path)
            + " -Append" + (force ? " -Force" : string.Empty) + " ; 'ok' } catch { $_.FullyQualifiedErrorId.Split(',')[0] + '|' + $_.CategoryInfo.Category }");

        string outcome = (string)Assert.Single(result.Output).BaseObject;

        if (expectedId == null)
        {
            Assert.Equal("ok", outcome);
            Assert.Equal(content.Concat(Encoding.ASCII.GetBytes("\"x\",\"y\"\r\n")).ToArray(), File.ReadAllBytes(path));
            return;
        }

        Assert.Equal(expectedId + "|InvalidData", outcome);
        Assert.Equal(content, File.ReadAllBytes(path));
    }

    private string Scalar(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);
        Assert.Empty(result.Errors);
        return Assert.Single(result.Output).BaseObject.ToString()!;
    }

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
}
