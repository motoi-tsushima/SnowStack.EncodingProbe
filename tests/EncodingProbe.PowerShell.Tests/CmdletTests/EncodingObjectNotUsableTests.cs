using System.IO;
using System.Linq;
using System.Management.Automation;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 逆シリアル化した判定結果などの PSCustomObject を文字エンコーディングに渡したときのメッセージ（1.3.0 第六次 C。BUG-0039）。
/// </summary>
/// <remarks>
/// 受け付けないのは今までどおり（ID は ParameterArgumentTransformationError のまま）。
/// メッセージを「'' を解決できない」から、型が違うことと渡し方（CodePage・統一語彙名）を示すものにした。
/// 判定結果と関係の無い PSCustomObject にも同じ文を出す（文は両方に合うように書いてある）。
/// </remarks>
public class EncodingObjectNotUsableTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private readonly ProbedCommandRunspaceFixture _fixture;

    public EncodingObjectNotUsableTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>値の作り方 → PowerShell の型名の先頭</summary>
    public static TheoryData<string, string> Shapes() => new()
    {
        { "$s = [System.Management.Automation.PSSerializer]::Serialize((Resolve-Encoding -Path $src)); $v = [System.Management.Automation.PSSerializer]::Deserialize($s)", "Deserialized.SnowStack.EncodingProbe.EncodingInformation" },
        { "Resolve-Encoding -Path $src | Export-Clixml -LiteralPath $xml; $v = Import-Clixml -LiteralPath $xml", "Deserialized.SnowStack.EncodingProbe.EncodingInformation" },
        { "$v = Resolve-Encoding -Path $src | Select-Object *", "Selected.SnowStack.EncodingProbe.EncodingInformation" },
        { "$v = Resolve-Encoding -Path $src | ConvertTo-Json | ConvertFrom-Json", "System.Management.Automation.PSCustomObject" },
        { "$v = [pscustomobject]@{ A = 1 }", "System.Management.Automation.PSCustomObject" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void PSCustomObject_IsRejected_WithTypeName(string make, string typeName)
    {
        using var directory = new TemporaryDirectory();
        string src = directory.Write("u16.txt", new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x0A, 0x00 });
        string xml = directory.Combine("info.xml");
        string output = directory.Combine("out.txt");
        string expected = ValidationMessages.EncodingObjectNotUsable(typeName);

        foreach (string command in new[]
        {
            "ConvertTo-DotNetEncoding -Encoding $v",
            "Get-ProbedContent -LiteralPath $src -Encoding $v",
            "Set-ProbedContent -LiteralPath $out -Value a -Encoding $v",
            "Convert-ProbedContent -LiteralPath $src -SourceEncoding $v -Encoding utf8BOM",
        })
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
                $"$src = '{src}'; $xml = '{xml}'; $out = '{output}'; {make}; try {{ {command} }} catch {{ $_ }}");
            var error = (ErrorRecord)result.Output.Select(o => o.BaseObject).OfType<ErrorRecord>().Single();

            Assert.Equal("ParameterArgumentTransformationError", error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(expected, error.Exception.InnerException!.Message);
            Assert.False(File.Exists(output));
        }
    }

    /// <summary>
    /// 元の EncodingInformation と、案内した渡し方（CodePage・統一語彙名）は受け付ける
    /// </summary>
    [Theory]
    [InlineData("-Encoding (Resolve-Encoding -Path $src)")]
    [InlineData("-Encoding $v.CodePage")]
    [InlineData("-Encoding unicodeBOM")]
    public void SuggestedForms_AreAccepted(string encoding)
    {
        using var directory = new TemporaryDirectory();
        string src = directory.Write("u16.txt", new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x0A, 0x00 });

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"$src = '{src}'; $v = Resolve-Encoding -Path $src | Select-Object *; Get-ProbedContent -LiteralPath $src {encoding}");

        Assert.Empty(result.Errors);
        Assert.Equal("a", Assert.Single(result.Output).ToString());
    }
}
