using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 書き込みの後の Archive 属性・元に戻したときの属性と、WriteAccessDenied のメッセージを検証する（1.3.0 第六次 B。BUG-0025）。
/// </summary>
/// <remarks>
/// 書き込みに成功したら、4 つの書き方（メモリに写してから直接・置き換え・写してから直接・追記）のどれでも Archive 属性が付く（標準の Set-Content と同じ）。
/// 元に戻した場合は、書き込みの前の属性のまま（書き戻しでファイルシステムが付ける Archive も外す）。
/// 隠し・システム属性のファイルに -Force なしで書け、代替データストリームが保たれることも固定する。
/// </remarks>
public class ArchiveAttributeAndAccessDeniedTests : IClassFixture<ProbedCommandRunspaceFixture>
{
    private const string NoError = "NO-ERROR";

    private readonly ProbedCommandRunspaceFixture _fixture;

    public ArchiveAttributeAndAccessDeniedTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    private static int Threshold => checked((int)SafeFileOverwrite.DirectWriteThreshold);

    /// <summary>書き方 × 成功・失敗（'한' は shift_jis で表現できないので、取り消しになる）</summary>
    public static TheoryData<string, bool, bool, bool> Cases()
    {
        var data = new TheoryData<string, bool, bool, bool>();

        foreach (string writer in new[] { "Set", "Out", "Export", "Convert", "Add", "OutAppend", "ExportAppend" })
        {
            foreach (bool large in new[] { false, true })
            {
                foreach (bool hardLink in new[] { false, true })
                {
                    if (hardLink && (writer.EndsWith("Append") || writer == "Add"))
                    {
                        continue;
                    }

                    data.Add(writer, large, hardLink, false);
                    data.Add(writer, large, hardLink, true);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Archive_IsSetOnSuccess_AndAttributesAreKeptOnRollback(string writer, bool large, bool hardLink, bool fail)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string original = "A,B\r\n" + new string('x', large ? Threshold + 10 : 10) + ",1\r\n";
        string path = directory.Write("a.csv", System.Text.Encoding.ASCII.GetBytes(original));

        if (hardLink)
        {
            Assert.True(FileLinks.TryCreateHardLink(directory.Combine("other.csv"), path));
        }

        File.SetAttributes(path, FileAttributes.Hidden);

        // 1 件目を書いた後、2 件目の '한'（shift_jis で表現できない）で止めて取り消しにする。大きい場合は 1 件目で境目を超える
        string first = large ? $"('y' * {Threshold + 10})" : "'y'";
        string records = fail ? $"@({first}, '한')" : $"@({first})";

        string script = writer switch
        {
            "Set" => $"{records} | Set-ProbedContent -LiteralPath '{path}' -Encoding shift_jis",
            "Out" => $"{records} | Out-ProbedFile -LiteralPath '{path}' -Encoding shift_jis",
            "Export" => $"{records} | ForEach-Object {{ [pscustomobject]@{{A=$_}} }} | Export-ProbedCsv -LiteralPath '{path}' -Encoding shift_jis",
            "Convert" => $"Convert-ProbedContent -LiteralPath '{path}' -SourceEncoding utf8NoBOM -Encoding {(fail ? "shift_jis" : "utf8BOM")}",
            "Add" => $"{records} | Add-ProbedContent -LiteralPath '{path}' -Encoding shift_jis",
            "OutAppend" => $"{records} | Out-ProbedFile -LiteralPath '{path}' -Append -Encoding shift_jis",
            _ => $"{records} | ForEach-Object {{ [pscustomobject]@{{A=$_;B='2'}} }} | Export-ProbedCsv -LiteralPath '{path}' -Append -Encoding shift_jis",
        };

        if (writer == "Convert" && fail)
        {
            // 変換は、変換先で表現できない文字を変換元に置いて止める
            File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllText(path, original + "\uD55C" + Environment.NewLine, new System.Text.UTF8Encoding(false));
            File.SetAttributes(path, FileAttributes.Hidden);
        }

        byte[] before = File.ReadAllBytes(path);
        string result = Run(script);

        if (fail)
        {
            Assert.NotEqual(NoError, result);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(FileAttributes.Hidden, File.GetAttributes(path));
        }
        else
        {
            Assert.Equal(NoError, result);
            Assert.NotEqual(before, File.ReadAllBytes(path));
            Assert.Equal(FileAttributes.Hidden | FileAttributes.Archive, File.GetAttributes(path));
        }

        File.SetAttributes(path, FileAttributes.Normal);
    }

    /// <summary>
    /// 追記で何も書かなかった場合は、属性を変えない。
    /// </summary>
    [Fact]
    public void Append_NothingWritten_AttributesUnchanged()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", new byte[] { 0x41, 0x0D, 0x0A });
        File.SetAttributes(path, FileAttributes.Normal);

        Assert.Equal(NoError, Run($"Add-ProbedContent -LiteralPath '{path}' -Value @()"));
        Assert.Equal(FileAttributes.Normal, File.GetAttributes(path));
    }

    /// <summary>
    /// 隠し・システム属性のファイルに -Force なしで書け、代替データストリームが保たれる（1 MiB 以内・超える）。
    /// </summary>
    [Theory]
    [InlineData("Set", false)]
    [InlineData("Set", true)]
    [InlineData("Out", false)]
    [InlineData("Out", true)]
    [InlineData("Export", false)]
    [InlineData("Export", true)]
    [InlineData("Convert", true)]
    public void HiddenSystemFile_IsWritten_AndStreamIsKept(string writer, bool large)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", System.Text.Encoding.ASCII.GetBytes(new string('o', large ? Threshold + 10 : 3) + "\r\n"));
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
        string value = large ? $"('y' * {Threshold + 10})" : "'y'";

        string script = writer switch
        {
            "Set" => $"Set-ProbedContent -LiteralPath '{path}' -Value {value} -Encoding utf8NoBOM",
            "Out" => $"{value} | Out-ProbedFile -LiteralPath '{path}' -Encoding utf8NoBOM",
            "Export" => $"[pscustomobject]@{{A={value}}} | Export-ProbedCsv -LiteralPath '{path}' -Encoding utf8NoBOM",
            _ => $"Convert-ProbedContent -LiteralPath '{path}' -SourceEncoding utf8NoBOM -Encoding utf8BOM",
        };

        Assert.Equal(NoError, Run(script));
        Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\n", File.ReadAllText(path + ":Zone.Identifier"));
        Assert.Equal(FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive, File.GetAttributes(path));
        File.SetAttributes(path, FileAttributes.Normal);
    }

    /// <summary>
    /// ACL で書き込みを拒否したファイルの WriteAccessDenied は、本モジュールのメッセージ（.NET の例外の文面ではない）。
    /// </summary>
    [Theory]
    [InlineData("Set-ProbedContent -LiteralPath '{0}' -Value x -Encoding utf8NoBOM")]
    [InlineData("Add-ProbedContent -LiteralPath '{0}' -Value x -Encoding utf8NoBOM")]
    [InlineData("'x' | Out-ProbedFile -LiteralPath '{0}' -Encoding utf8NoBOM")]
    [InlineData("'x' | Out-ProbedFile -LiteralPath '{0}' -Append -Encoding utf8NoBOM")]
    [InlineData("[pscustomobject]@{{A='x'}} | Export-ProbedCsv -LiteralPath '{0}' -Encoding utf8NoBOM")]
    [InlineData("Convert-ProbedContent -LiteralPath '{0}' -SourceEncoding utf8NoBOM -Encoding utf8BOM")]
    public void WriteAccessDenied_HasModuleMessage(string template)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string path = directory.Write("a.txt", new byte[] { 0x41, 0x0D, 0x0A });
        var info = new FileInfo(path);
        FileSecurity security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.WriteData | FileSystemRights.AppendData, AccessControlType.Deny);
        security.AddAccessRule(rule);
        info.SetAccessControl(security);

        try
        {
            InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
                $"try {{ {string.Format(template, path)} }} catch {{ $_ }}");
            var error = result.Errors.Concat(result.Output.Select(o => o.BaseObject).OfType<System.Management.Automation.ErrorRecord>()).Single();

            Assert.Equal("WriteAccessDenied", error.FullyQualifiedErrorId.Split(',')[0]);
            Assert.Equal(ValidationMessages.WriteAccessDenied(path), error.Exception.Message);
            Assert.Null(error.Exception.InnerException);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
    }

    private string Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ $ErrorActionPreference = 'Stop'; {script} ; '{NoError}' }} catch {{ $_.FullyQualifiedErrorId.Split(',')[0] }}");

        return result.Output.Last().ToString();
    }
}
