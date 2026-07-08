using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// Resolve-Encoding コマンドレットを PowerShell ランスペース経由で実行する機能テスト。
/// </summary>
public class ResolveEncodingCmdletTests : IClassFixture<RunspaceFixture>
{
    private readonly RunspaceFixture _fixture;

    public ResolveEncodingCmdletTests(RunspaceFixture fixture)
        => _fixture = fixture;

    // ─── 戻り値の型 ──────────────────────────────────────────────────────────

    [Fact]
    public void Invoke_ReturnsEncodingInfomation()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var results = _fixture.Invoke(path);

        Assert.Single(results);
        Assert.IsType<EncodingInformation>(results[0].BaseObject);
    }

    // ─── 英語ファイル ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sample_ascii.txt",    20127, "us-ascii",  "ascii")]
    [InlineData("sample_utf8.txt",     65001, "utf-8",     "utf8NoBOM")]
    [InlineData("sample_utf8_bom.txt", 65001, "utf-8",     "utf8BOM")]
    public void Invoke_English(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
    {
        var path = TestDataHelper.GetPath("English", fileName);
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.Equal(expectedCodePage,       result.CodePage);
        Assert.Equal(expectedEncodingName,   result.EncodingWebName);
        Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
    }

    // ─── 日本語ファイル ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("sample_utf8.txt",     65001, "utf-8",       "utf8NoBOM")]
    [InlineData("sample_utf8_bom.txt", 65001, "utf-8",       "utf8BOM")]
    [InlineData("sample_shiftjis.txt", 932,   "shift_jis",   "932")]
    [InlineData("sample_eucjp.txt",    20932, "euc-jp",      "20932")]
    [InlineData("sample_jis.txt",      50220, "iso-2022-jp", "50220")]
    public void Invoke_Japanese(string fileName, int expectedCodePage, string expectedEncodingName, string expectedPSEncodingName)
    {
        var path = TestDataHelper.GetPath("Japanese", fileName);
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.Equal(expectedCodePage,       result.CodePage);
        Assert.Equal(expectedEncodingName,   result.EncodingWebName);
        Assert.Equal(expectedPSEncodingName, result.PSEncodingName);
    }

    // ─── BOM フラグ検証 ──────────────────────────────────────────────────────

    [Fact]
    public void Invoke_Utf8WithBom_BomFlagIsTrue()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8_bom.txt");
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.True(result.Bom);
    }

    [Fact]
    public void Invoke_Utf8WithoutBom_BomFlagIsFalse()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var result = (EncodingInformation)_fixture.Invoke(path)[0].BaseObject;

        Assert.False(result.Bom);
    }

    // ─── エラー処理 ──────────────────────────────────────────────────────────

    [Fact]
    public void Invoke_NonExistentFile_ThrowsFileNotFoundException()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          Guid.NewGuid().ToString() + ".txt");

        // PowerShell エンジンはコマンドレット内の例外を CmdletInvocationException でラップする。
        // 本来の FileNotFoundException は InnerException に格納される。
        var ex = Assert.Throws<System.Management.Automation.CmdletInvocationException>(
            () => _fixture.Invoke(path));
        Assert.IsType<System.IO.FileNotFoundException>(ex.InnerException);
    }

    // ─── Culture パラメーター ────────────────────────────────────────────────

    [Fact]
    public void Invoke_WithCulture_ReturnsEncodingInfomation()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var result = _fixture.Invoke(path, new() { ["Culture"] = "ja-JP" });

        Assert.Single(result);
        Assert.IsType<EncodingInformation>(result[0].BaseObject);
    }

    // ─── Strategy パラメーター ───────────────────────────────────────────────

    [Theory]
    [InlineData("Combined")]
    [InlineData("UtfUnknownOnly")]
    [InlineData("NativeOnly")]
    public void Invoke_WithStrategy_ReturnsEncodingInfomation(string strategy)
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
        var result = _fixture.Invoke(path, new() { ["Strategy"] = strategy });

        Assert.Single(result);
        Assert.IsType<EncodingInformation>(result[0].BaseObject);
    }

    [Fact]
    public void Invoke_WithInvalidStrategy_ThrowsCmdletInvocationException()
    {
        var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");

        var ex = Assert.Throws<System.Management.Automation.CmdletInvocationException>(
            () => _fixture.Invoke(path, new() { ["Strategy"] = "InvalidStrategy" }));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }
}
