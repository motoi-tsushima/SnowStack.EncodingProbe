using System;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 継承した us-ascii を BOM 無しの UTF-8 とみなすことを検証するテスト（1.3.0 第五次。BUG-0001）。
/// </summary>
/// <remarks>
/// 対象は、書き込み先・追記先からの継承（-Encoding 省略・Auto）、-EncodingFrom、-Encoding に判定結果（EncodingInformation）を渡した場合と、
/// 追記の整合性検査の相手。-Encoding ascii の明示は置き換えない。
/// ASCII の文字だけを書く場合のバイト列は、第四次まで（us-ascii で書いていたとき）と変わらない。
/// </remarks>
public class InheritedAsciiTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private static readonly byte[] AsciiText = Encoding.ASCII.GetBytes("abc\r\n");
    private static readonly byte[] AsciiCsv = Encoding.ASCII.GetBytes("A,B\r\n1,2\r\n");
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public InheritedAsciiTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    /// <summary>
    /// BUG-0001 の再現の入力: us-ascii の既存ファイルに -Encoding を省略して日本語を書くと、UTF-8 で書く（'?' にしない）
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath {0} -Value '日本'", true)]
    [InlineData("'日本' | Out-ProbedFile -LiteralPath {0} -Append", true)]
    [InlineData("Set-ProbedContent -LiteralPath {0} -Value '日本'", false)]
    [InlineData("Add-ProbedContent -LiteralPath {0} -Value '日本' -AllowEncodingChange", true)]
    [InlineData("Add-ProbedContent -LiteralPath {0} -Value '日本' -Encoding utf8NoBOM", true)]
    [InlineData("Add-ProbedContent -LiteralPath {0} -Value '日本' -Encoding utf8BOM", true)]
    [InlineData("'日本' | Out-ProbedFile -LiteralPath {0} -Append -Encoding utf8NoBOM", true)]
    [InlineData("'日本' | Out-ProbedFile -LiteralPath {0} -Encoding Auto", false)]
    public void Text_InheritedAscii_WritesUtf8(string command, bool append)
    {
        string path = this._directory.Write("ascii.txt", AsciiText);

        InvocationResult result = Run(command, path);

        Assert.Empty(result.Errors);
        byte[] written = Utf8.GetBytes("日本\r\n");
        Assert.Equal(append ? AsciiText.Concat(written).ToArray() : written, File.ReadAllBytes(path));
    }

    /// <summary>
    /// CSV の追記も UTF-8 で書く（-DetectionLineCount の読み替えと同じ結果）
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" -DetectionLineCount 1")]
    public void Csv_InheritedAscii_WritesUtf8(string extra)
    {
        string path = this._directory.Write("ascii.csv", AsciiCsv);

        InvocationResult result = Run("[pscustomobject]@{{A='日本';B='x'}} | Export-ProbedCsv -LiteralPath {0} -Append" + extra, path);

        Assert.Empty(result.Errors);
        Assert.Equal(AsciiCsv.Concat(Utf8.GetBytes("\"日本\",\"x\"\r\n")).ToArray(), File.ReadAllBytes(path));
    }

    /// <summary>
    /// ASCII の文字だけを書く場合のバイト列は、us-ascii で書いていた第四次までと同じ
    /// </summary>
    [Theory]
    [InlineData("Add-ProbedContent -LiteralPath {0} -Value 'xyz'", true)]
    [InlineData("'xyz' | Out-ProbedFile -LiteralPath {0} -Append", true)]
    [InlineData("Set-ProbedContent -LiteralPath {0} -Value 'xyz'", false)]
    public void Text_InheritedAscii_AsciiOnly_SameBytesAsBefore(string command, bool append)
    {
        string path = this._directory.Write("ascii-only.txt", AsciiText);

        InvocationResult result = Run(command, path);

        Assert.Empty(result.Errors);
        byte[] written = Encoding.ASCII.GetBytes("xyz\r\n");
        Assert.Equal(append ? AsciiText.Concat(written).ToArray() : written, File.ReadAllBytes(path));
    }

    /// <summary>
    /// -EncodingFrom の参照ファイルが us-ascii なら、BOM 無しの UTF-8 で書く
    /// </summary>
    [Theory]
    [InlineData("Set-ProbedContent -LiteralPath {0} -Value '日本' -EncodingFrom {1}")]
    [InlineData("'日本' | Out-ProbedFile -LiteralPath {0} -EncodingFrom {1}")]
    public void EncodingFrom_Ascii_WritesUtf8(string command, string _ = "")
    {
        string reference = this._directory.Write("reference.txt", Encoding.ASCII.GetBytes("abc\n"));
        string path = this._directory.Combine("from.txt");

        InvocationResult result = Run(command, path, reference);

        Assert.Empty(result.Errors);

        // 改行も参照ファイルから継承する（LF）
        Assert.Equal(Utf8.GetBytes("日本\n"), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Encoding に us-ascii の判定結果（EncodingInformation）を渡した場合も、BOM 無しの UTF-8 で書く
    /// </summary>
    [Fact]
    public void EncodingInformation_Ascii_WritesUtf8()
    {
        string reference = this._directory.Write("reference.txt", Encoding.ASCII.GetBytes("abc\n"));
        string path = this._directory.Combine("info.txt");

        InvocationResult result = Run("Set-ProbedContent -LiteralPath {0} -Value '日本' -Encoding (Resolve-Encoding -Path {1})", path, reference);

        Assert.Empty(result.Errors);
        Assert.Equal(Utf8.GetBytes("日本\n"), File.ReadAllBytes(path));
    }

    /// <summary>
    /// -Encoding ascii の明示は置き換えない（日本語は表現できないのでエラー）
    /// </summary>
    [Fact]
    public void ExplicitAscii_IsNotReplaced()
    {
        string path = this._directory.Write("explicit.txt", AsciiText);

        InvocationResult result = Run("Add-ProbedContent -LiteralPath {0} -Value '日本' -Encoding ascii", path);

        Assert.Equal("UnrepresentableCharacter", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(AsciiText, File.ReadAllBytes(path));
    }

    /// <summary>
    /// ConvertTo-DotNetEncoding（読み取りと同じ用途）は us-ascii の判定結果を us-ascii のまま返す
    /// </summary>
    [Fact]
    public void ConvertToDotNetEncoding_KeepsAscii()
    {
        string reference = this._directory.Write("reference.txt", Encoding.ASCII.GetBytes("abc\n"));

        InvocationResult result = Run("(Resolve-Encoding -Path {0} | ConvertTo-DotNetEncoding).CodePage", reference);

        Assert.Equal(20127, (int)Assert.Single(result.Output).BaseObject);
    }

    private InvocationResult Run(string format, params string[] paths)
        => this._fixture.InvokeScriptCapturingErrors(
            string.Format(format, paths.Select(p => (object)("'" + p.Replace("'", "''") + "'")).ToArray()));
}
