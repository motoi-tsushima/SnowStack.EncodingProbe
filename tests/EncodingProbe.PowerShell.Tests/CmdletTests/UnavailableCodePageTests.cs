using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 判定はできたが、実行環境がそのコードページを提供していない場合の挙動を検証するテスト。
/// </summary>
/// <remarks>
/// 判定処理は .NET が提供していないコードページを返すことがある。ISO-2022-TW の 50229 がそれで、
/// <c>Encoding.GetEncoding(50229)</c> は net10.0 / net48 のどちらでも NotSupportedException を投げる。
/// これを素通りさせると、対象ファイルごとの非終了エラーであるべきものが
/// 生の例外による終了エラーになってしまう。
/// </remarks>
public class UnavailableCodePageTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    /// <summary>
    /// ISO-2022-TW と判定されるバイト列。
    /// </summary>
    /// <remarks>
    /// <c>ESC $ + I</c>（CNS 11643 Plane 3）は ISO-2022-TW にしか現れないエスケープシーケンスであり、
    /// ISO-2022-CN との同点にならないため確実に 50229 と判定される。
    /// </remarks>
    private static readonly byte[] Iso2022TwBytes =
    {
        0x1B, 0x24, 0x2B, 0x49,  // ESC $ + I
        0x1B, 0x4E, 0x21, 0x21,  // ESC N + 2バイト
        0x1B, 0x28, 0x42,        // ESC ( B （ASCIIへ復帰）
        0x41, 0x0D, 0x0A,
    };

    private readonly ProbedCommandRunspaceFixture _fixture;

    public UnavailableCodePageTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>
    /// 前提の確認。判定結果が「.NET が提供していないコードページ」であること。
    /// </summary>
    /// <remarks>
    /// この前提が崩れると以降のテストが意味を失うため、明示的に確認する。
    /// </remarks>
    [Fact]
    public void Precondition_DetectedCodePageIsNotAvailableInThisRuntime()
    {
        EncodingInformation information = Detect();

        Assert.Equal(50229, information.CodePage);
        Assert.Throws<NotSupportedException>(() => Encoding.GetEncoding(information.CodePage));
    }

    /// <summary>
    /// 読み込みは、生の例外ではなく非終了エラーとして報告されること。
    /// </summary>
    [Fact]
    public void Read_UnavailableCodePage_ReportsNonTerminatingError()
    {
        using var file = ByteExactFile.Create(Iso2022TwBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Get-ProbedContent", new Dictionary<string, object?> { ["LiteralPath"] = new[] { file.Path } });

        ErrorRecord error = Assert.Single(result.Errors);

        Assert.Equal("CodePageNotAvailable", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ExpectedMessage(), error.Exception.Message);
        Assert.Empty(result.Output);
    }

    /// <summary>
    /// -Encoding を明示すれば読み込めること（エラーメッセージが案内する回避手段）。
    /// </summary>
    [Fact]
    public void Read_UnavailableCodePage_WithExplicitEncoding_Succeeds()
    {
        using var file = ByteExactFile.Create(Iso2022TwBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Get-ProbedContent",
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { file.Path },
                ["Encoding"] = "ascii",
            });

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Output);
    }

    /// <summary>
    /// 上書きで継承しようとした場合も非終了エラーとし、ファイルを壊さないこと。
    /// </summary>
    [Fact]
    public void Write_InheritFromUnavailableCodePage_ReportsErrorAndKeepsFile()
    {
        using var file = ByteExactFile.Create(Iso2022TwBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Set-ProbedContent",
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { file.Path },
                ["Value"] = "A",
            });

        ErrorRecord error = Assert.Single(result.Errors);

        Assert.Equal("CodePageNotAvailable", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ExpectedMessage(), error.Exception.Message);
        Assert.Equal(Iso2022TwBytes, file.ReadBytes());
    }

    /// <summary>
    /// 追記で整合性検査のために判定した場合も非終了エラーとし、ファイルを壊さないこと。
    /// </summary>
    [Fact]
    public void Append_ToUnavailableCodePage_ReportsErrorAndKeepsFile()
    {
        using var file = ByteExactFile.Create(Iso2022TwBytes);

        InvocationResult result = this._fixture.InvokeCapturingErrors(
            "Add-ProbedContent",
            new Dictionary<string, object?>
            {
                ["LiteralPath"] = new[] { file.Path },
                ["Value"] = "A",
                ["Encoding"] = "ascii",
            });

        ErrorRecord error = Assert.Single(result.Errors);

        Assert.Equal("CodePageNotAvailable", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(Iso2022TwBytes, file.ReadBytes());
    }

    /// <summary>
    /// -EncodingFrom の参照先が扱えないコードページだった場合は終了エラーになること。
    /// </summary>
    /// <remarks>
    /// パラメータの指定そのものが成立していないため、対象ファイルごとの非終了エラーとは扱わない。
    /// </remarks>
    [Fact]
    public void Write_EncodingFromUnavailableCodePage_IsTerminatingError()
    {
        using var reference = ByteExactFile.Create(Iso2022TwBytes);
        using var file = ByteExactFile.CreateMissing();

        var exception = Assert.Throws<CmdletInvocationException>(
            () => this._fixture.InvokeCapturingErrors(
                "Set-ProbedContent",
                new Dictionary<string, object?>
                {
                    ["LiteralPath"] = new[] { file.Path },
                    ["Value"] = "A",
                    ["EncodingFrom"] = reference.Path,
                }));

        Assert.Equal(
            "EncodingFromCodePageNotAvailable",
            exception.ErrorRecord.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ExpectedMessage(), exception.ErrorRecord.Exception.Message);
        Assert.False(file.Exists);
    }

    /// <summary>
    /// ConvertTo-DotNetEncoding も、生の .NET 例外ではなく本モジュールのメッセージで失敗すること。
    /// </summary>
    [Fact]
    public void Convert_UnavailableCodePage_FailsAtParameterBindingWithOwnMessage()
    {
        EncodingInformation information = Detect();

        var exception = Assert.ThrowsAny<ParameterBindingException>(
            () => this._fixture.InvokeWithArgument("ConvertTo-DotNetEncoding", information));

        Assert.IsType<ArgumentTransformationMetadataException>(exception.InnerException);
        Assert.Contains(ExpectedMessage(), exception.Message);
    }

    private static EncodingInformation Detect()
        => SnowStack.EncodingProbe.EncodingProbe.Detect(Iso2022TwBytes);

    private static string ExpectedMessage()
    {
        EncodingInformation information = Detect();

        return ValidationMessages.DetectedCodePageNotAvailable(
            information.CodePage, information.EncodingWebName);
    }
}
