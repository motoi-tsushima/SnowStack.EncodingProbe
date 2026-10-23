using System;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 追記の整合性検査（EncodingChangeOnAppend）で止まったら、そのファイルにその呼び出しで書いた分を残さないことのテスト（1.3.0 第五次の後半。実装記録 9.9 節の 2）。
/// </summary>
/// <remarks>
/// 第五次の前半までは、それまでに書いた行（検査を通った行）を残していた。表現できない文字は元に戻していたので、
/// 失敗の種類によってファイルが元に戻るかどうかが違っていた。
/// </remarks>
public class AppendRollbackTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    /// <summary>Shift_JIS の「あ」と LF（-Culture ja-JP で Shift_JIS と判定される）</summary>
    private static readonly byte[] ShiftJis = { 0x82, 0xA0, 0x0A };

    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public AppendRollbackTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    /// <summary>
    /// Add-ProbedContent: 前のレコードで書いた内容も取り消し、ほかのファイルへの追記は続ける（非終了エラー）
    /// </summary>
    [Fact]
    public void Add_Mismatch_RestoresFile_AndContinuesWithOtherFiles()
    {
        string sjis = this._directory.Write("sjis.txt", ShiftJis);
        string utf8 = this._directory.Write("utf8.txt", Encoding.UTF8.GetBytes("あ\n"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"'x', 'い', 'y' | Add-ProbedContent -LiteralPath {Quote(sjis)}, {Quote(utf8)} -Encoding utf8NoBOM -Culture ja-JP -LineBreak Lf");

        Assert.Equal("EncodingChangeOnAppend", Assert.Single(result.Errors).FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(ShiftJis, File.ReadAllBytes(sjis));
        Assert.Equal(Encoding.UTF8.GetBytes("あ\nx\nい\ny\n"), File.ReadAllBytes(utf8));
    }

    /// <summary>
    /// -ErrorAction Stop でも、元に戻してからエラーにする（後始末が書き込みを完了してしまわない）
    /// </summary>
    [Theory]
    [InlineData("Add")]
    [InlineData("Out")]
    [InlineData("Export")]
    public void Mismatch_WithErrorActionStop_RestoresFile(string command)
    {
        byte[] original = command == "Export" ? new byte[] { 0x41, 0x0A, 0x82, 0xA0, 0x0A } : ShiftJis;
        string path = this._directory.Write("f.txt", original);

        string script = command switch
        {
            "Add" => $"'x', 'い' | Add-ProbedContent -LiteralPath {Quote(path)} -Encoding utf8NoBOM -Culture ja-JP -LineBreak Lf",
            "Out" => $"'x', 'い' | Out-ProbedFile -LiteralPath {Quote(path)} -Encoding utf8NoBOM -Append -Culture ja-JP -LineBreak Lf",
            _ => $"[pscustomobject]@{{ A = 'x' }}, [pscustomobject]@{{ A = 'い' }} | Export-ProbedCsv -LiteralPath {Quote(path)} -Encoding utf8NoBOM -Append -Culture ja-JP",
        };

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            "try { " + script + " -ErrorAction Stop } catch { $_.FullyQualifiedErrorId.Split(',')[0] }");

        Assert.Equal("EncodingChangeOnAppend", Assert.Single(result.Output).BaseObject);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
}
