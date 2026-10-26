using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 書き込みのすべてのコマンドが、UTF-7（コードページ 65000）の Encoding のインスタンスを、
/// 名前の utf7 と同じエラーで拒否することを検証する（1.3.0 第六次。BUG-0037）。
/// </summary>
/// <remarks>
/// 第五次までは Convert-ProbedContent だけがインスタンスを受け付けて UTF-7 で書いていた。
/// ほかの 4 コマンドはパラメータ束縛の段階で拒否していたので、それも固定する。
/// どのコマンドでも、ファイルは変わらない（作られない）。
/// </remarks>
public class Utf7InstanceWriteTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string NoError = "NO-ERROR";

    private static readonly byte[] Original = Encoding.ASCII.GetBytes("A\r\n");

    private readonly ProbedCommandRunspaceFixture _fixture;

    public Utf7InstanceWriteTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>
    /// 名前とインスタンスで同じエラー ID になり、既存のファイルが変わらないこと。
    /// </summary>
    [Theory]
    [InlineData("Set-ProbedContent -LiteralPath '{0}' -Value 'x' -Encoding {1}", "ParameterArgumentTransformationError")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'x' -Encoding {1}", "ParameterArgumentTransformationError")]
    [InlineData("'x' | Out-ProbedFile -LiteralPath '{0}' -Encoding {1}", "ParameterArgumentTransformationError")]
    [InlineData("'x' | Out-ProbedFile -LiteralPath '{0}' -Append -Encoding {1}", "ParameterArgumentTransformationError")]
    [InlineData("[pscustomobject]@{{A='x'}} | Export-ProbedCsv -LiteralPath '{0}' -Encoding {1}", "ParameterArgumentTransformationError")]
    [InlineData("[pscustomobject]@{{A='x'}} | Export-ProbedCsv -LiteralPath '{0}' -Append -Encoding {1}", "ParameterArgumentTransformationError")]
    [InlineData("Convert-ProbedContent -LiteralPath '{0}' -SourceEncoding utf8NoBOM -Encoding {1}", "EncodingNotAllowedForWrite")]
    [InlineData("Convert-ProbedContent -LiteralPath '{0}' -SourceEncoding utf8NoBOM -Encoding {1} -Bom Remove", "EncodingNotAllowedForWrite")]
    public void Utf7_InstanceAndName_AreRejectedWithSameId(string template, string expected)
    {
        foreach (string encoding in new[] { "utf7", "(New-Object System.Text.UTF7Encoding)", "(New-Object System.Text.UTF7Encoding $true)" })
        {
            using var directory = new TemporaryDirectory();
            string path = directory.Write("a.txt", Original);

            Assert.Equal(expected, Run(string.Format(template, path, encoding)));
            Assert.Equal(Original, File.ReadAllBytes(path));
        }
    }

    /// <summary>
    /// 書き込み先が無い場合も、インスタンスを拒否してファイルを作らないこと。
    /// </summary>
    [Theory]
    [InlineData("Set-ProbedContent -LiteralPath '{0}' -Value 'x' -Encoding {1}")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value 'x' -Encoding {1}")]
    [InlineData("'x' | Out-ProbedFile -LiteralPath '{0}' -Encoding {1}")]
    [InlineData("[pscustomobject]@{{A='x'}} | Export-ProbedCsv -LiteralPath '{0}' -Encoding {1}")]
    public void Utf7_Instance_NewFile_IsNotCreated(string template)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.Combine("new.txt");

        Assert.Equal(
            "ParameterArgumentTransformationError",
            Run(string.Format(template, path, "(New-Object System.Text.UTF7Encoding)")));
        Assert.False(File.Exists(path));
    }

    private string Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ {script} ; '{NoError}' }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        return result.Output.Last().ToString();
    }
}
