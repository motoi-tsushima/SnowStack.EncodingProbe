using System.IO;
using System.Linq;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 利用者の $PSDefaultParameterValues が、Out-ProbedFile の内部の Out-String に効かないことを検証する（1.3.0 第六次 B。BUG-0035）。
/// </summary>
/// <remarks>
/// 標準の Out-File は Out-String を呼ばないので、'Out-String:Width' の影響を受けない。Out-ProbedFile も同じ結果にする。
/// Out-ProbedFile 自身の -Width への既定値（'Out-ProbedFile:Width'・'*:Width'）は、今までどおり効く（'*:Width' は Out-File にも効く）。
/// 既定値はランスペースで共有されるので、スクリプトの中で設定し、終わったら消す。
/// </remarks>
public class OutStringDefaultParameterTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string Objects =
        "[pscustomobject]@{ Name = ('a' * 20); Value = ('b' * 40); N = 1 }, [pscustomobject]@{ Name = 'c'; Value = 'd'; N = 2 }";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public OutStringDefaultParameterTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    [Theory]
    [InlineData("Out-String:Width", "30", false)]
    [InlineData("Out-String:Stream", "$false", false)]
    [InlineData("Out-String:*", "30", false)]
    [InlineData("Out-ProbedFile:Width", "30", true)]
    [InlineData("*:Width", "30", true)]
    public void DefaultParameterValues_AffectOnlyOutProbedFileItself(string key, string value, bool narrowed)
    {
        using var directory = new TemporaryDirectory();
        string probed = directory.Combine("p.txt");
        string plain = directory.Combine("plain.txt");
        string standard = directory.Combine("s.txt");

        // 既定値なしで書いたものと比べる
        this._fixture.InvokeScript($"{Objects} | Out-ProbedFile -LiteralPath '{plain}' -Encoding utf8NoBOM");

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            "$PSDefaultParameterValues['" + key + "'] = " + value + "; try { "
            + $"{Objects} | Out-ProbedFile -LiteralPath '{probed}' -Encoding utf8NoBOM; "
            + $"{Objects} | Out-File -LiteralPath '{standard}' -Encoding utf8NoBOM "
            + "} finally { $PSDefaultParameterValues.Remove('" + key + "') }");

        Assert.Empty(result.Errors);
        string[] lines = File.ReadAllLines(probed);

        if (narrowed)
        {
            // 本コマンドの -Width に効く（30 桁に切り詰め、N の列が消える）
            Assert.True(lines.Where(l => l.Length > 0).All(l => l.Length <= 30));
            Assert.DoesNotContain(lines, l => l.TrimEnd().EndsWith(" N"));
        }
        else
        {
            Assert.Equal(File.ReadAllLines(plain), lines);
        }

        if (key != "Out-ProbedFile:Width")
        {
            // 標準の Out-File と同じ行（Out-File は 'Out-String:*' の影響を受けず、'*:Width' の影響は受ける）
            Assert.Equal(File.ReadAllLines(standard), lines);
        }
    }
}
