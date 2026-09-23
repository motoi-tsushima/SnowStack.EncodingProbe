using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// UTF.Unknown が .NET に無いエンコーディング（iso-8859-16）を返す入力で、
/// コマンドレットが例外を利用者に飛ばさないことを検証するテスト。
/// </summary>
/// <remarks>
/// <para>
/// ルーマニア語のテキストに対して UTF.Unknown は iso-8859-16 を返すが、.NET には iso-8859-16 が無く、
/// UTF.Unknown の <c>Detected.Encoding</c> は null になる。1.1.0 ではこれを読んで NullReferenceException が発生し、
/// Resolve-Encoding と Get-ProbedContent の利用者まで例外が届いていた。
/// </para>
/// <para>
/// 修正後の判定結果は CodePage = -1（名前だけ保存）であり、1.1.0 から決まっている
/// 「判定できなかった」の扱いになる。読み書き系のコマンドは <c>EncodingDetectionFailed</c> の非終了エラーを報告する。
/// （コードページ番号が付いたうえで .NET に無い場合の <c>CodePageNotAvailable</c> とは別の経路である。
/// UTF.Unknown が .NET に無いエンコーディングを返した場合、コードページ番号は得られないため）
/// </para>
/// </remarks>
public class UnsupportedUtfUnknownEncodingTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    /// <summary>ISO-8859-16 のルーマニア語の文字とバイトの対応（ASCII 以外）</summary>
    private static readonly Dictionary<char, byte> Iso885916 = new Dictionary<char, byte>
    {
        ['ă'] = 0xE3, ['â'] = 0xE2, ['î'] = 0xEE, ['ș'] = 0xBA, ['ț'] = 0xFE,
        ['Ă'] = 0xC3, ['Â'] = 0xC2, ['Î'] = 0xCE, ['Ș'] = 0xAA, ['Ț'] = 0xDE,
    };

    private const string RomanianText =
        "Limba română este o limbă romanică. Fiecare zi este o nouă șansă de a învăța.\r\n" +
        "București este capitala României și cel mai mare oraș din țară.\r\n" +
        "Copiii merg în fiecare dimineață la școală pe jos.\r\n" +
        "Mulțumesc frumos și o zi bună.\r\n";

    /// <summary>ルーマニア語の本文を ISO-8859-16 のバイト列にしたもの（バイト列を明示して組み立てる）</summary>
    private static readonly byte[] RomanianBytes =
        RomanianText.Select(c => c < 0x80 ? (byte)c : Iso885916[c]).ToArray();

    private readonly ProbedCommandRunspaceFixture _fixture;

    public UnsupportedUtfUnknownEncodingTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>
    /// Resolve-Encoding は例外を投げず、名前だけを保存した判定結果（CodePage = -1）を返すこと。
    /// </summary>
    [Theory]
    [InlineData("Combined", "ro-RO")]
    [InlineData("Combined", "en-US")]
    [InlineData("UtfUnknownOnly", "ro-RO")]
    [InlineData("UtfUnknownOnly", "en-US")]
    public void Resolve_Iso885916_ReturnsNameOnlyWithoutThrowing(string strategy, string culture)
    {
        using var file = ByteExactFile.Create(RomanianBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Resolve-Encoding",
            new Dictionary<string, object?>
            {
                ["Path"] = file.Path,
                ["Culture"] = culture,
                ["Strategy"] = strategy,
            });

        Assert.Empty(result.Errors);
        var information = (EncodingInformation)Assert.Single(result.Output).BaseObject;
        Assert.Equal(-1, information.CodePage);
        Assert.Equal("iso-8859-16", information.EncodingWebName);
    }

    /// <summary>
    /// Get-ProbedContent は例外を投げず、判定失敗の非終了エラーを報告すること。
    /// </summary>
    [Theory]
    [InlineData("Combined", "ro-RO")]
    [InlineData("Combined", "en-US")]
    [InlineData("UtfUnknownOnly", "ro-RO")]
    [InlineData("UtfUnknownOnly", "en-US")]
    public void Read_Iso885916_ReportsDetectionFailed(string strategy, string culture)
    {
        using var file = ByteExactFile.Create(RomanianBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Get-ProbedContent",
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { file.Path },
                ["Culture"] = culture,
                ["Strategy"] = strategy,
            });

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("EncodingDetectionFailed", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ValidationMessages.DetectionFailed(file.Path), error.Exception.Message);
        Assert.Empty(result.Output);
    }

    /// <summary>
    /// -Encoding を明示すれば読み込めること（エラーメッセージが案内する回避手段）。
    /// </summary>
    [Fact]
    public void Read_Iso885916_WithExplicitEncoding_Succeeds()
    {
        using var file = ByteExactFile.Create(RomanianBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Get-ProbedContent",
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { file.Path },
                ["Encoding"] = "ascii",
                ["Raw"] = true,
            });

        Assert.Empty(result.Errors);
        Assert.Single(result.Output);
    }

    /// <summary>
    /// 上書きで継承しようとした場合も判定失敗の非終了エラーとし、ファイルを壊さないこと。
    /// </summary>
    [Fact]
    public void Write_InheritFromIso885916_ReportsDetectionFailedAndKeepsFile()
    {
        using var file = ByteExactFile.Create(RomanianBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Set-ProbedContent",
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { file.Path },
                ["Value"] = "A",
                ["Culture"] = "ro-RO",
            });

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("EncodingDetectionFailed", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(RomanianBytes, file.ReadBytes());
    }

    /// <summary>
    /// 追記の整合性検査で判定した場合も判定失敗の非終了エラーとし、ファイルを壊さないこと。
    /// </summary>
    [Fact]
    public void Append_ToIso885916_ReportsDetectionFailedAndKeepsFile()
    {
        using var file = ByteExactFile.Create(RomanianBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Add-ProbedContent",
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { file.Path },
                ["Value"] = "A",
                ["Encoding"] = "ascii",
                ["Culture"] = "ro-RO",
            });

        ErrorRecord error = Assert.Single(result.Errors);
        Assert.Equal("EncodingDetectionFailed", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(RomanianBytes, file.ReadBytes());
    }

    /// <summary>
    /// -EncodingFrom の参照先で判定に失敗した場合は、既存どおり終了エラーになること（NullReferenceException ではない）。
    /// </summary>
    [Fact]
    public void Write_EncodingFromIso885916_IsTerminatingDetectionFailed()
    {
        using var reference = ByteExactFile.Create(RomanianBytes);
        using var file = ByteExactFile.CreateMissing();

        var exception = Assert.Throws<CmdletInvocationException>(
            () => this._fixture.InvokeCapturingErrors(
                "Set-ProbedContent",
                new Dictionary<string, object?>
                {
                    ["LiteralPath"] = new[] { file.Path },
                    ["Value"] = "A",
                    ["EncodingFrom"] = reference.Path,
                    ["Culture"] = "ro-RO",
                }));

        Assert.Equal(
            "EncodingFromDetectionFailed",
            exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.False(file.Exists);
    }
}
