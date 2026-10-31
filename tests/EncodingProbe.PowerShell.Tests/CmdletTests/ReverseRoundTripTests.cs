using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 書き込み側が先に開いたファイルを、同じパイプラインの読み込み側が後から開く場合（逆向きの往復）も SamePathRoundTrip にし、
/// 書き込み側を元に戻すことを検証する（1.3.0 第六次 B。BUG-0028。案 A）。
/// </summary>
/// <remarks>
/// <c>Get-ProbedContent a.txt, z.txt | Out-ProbedFile z.txt</c> では、a.txt の 1 行目で書き込み側が z.txt を開き、その後で読み込み側が z.txt を開く。
/// 読み込み側は読まずに非終了エラー SamePathRoundTrip を出して次へ進み、書き込み側は元に戻して、次の入力か完了の時点で SamePathRoundTrip を出す
/// （Set / Add は非終了、Out / Export は終了エラー）。ファイルは元のまま（まだ無かったファイルは残らない）。
/// 4 つの書き方（メモリに写してから直接・置き換え・写してから直接・追記）と、まだ無いファイル（1 MiB 以内は直接作る、超えると一時ファイル）のすべてで確かめる。
/// </remarks>
[Collection(OverwriteStateCollection.Name)]
public class ReverseRoundTripTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private readonly ProbedCommandRunspaceFixture _fixture;

    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public ReverseRoundTripTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    private static int Threshold => checked((int)SafeFileOverwrite.DirectWriteThreshold);

    public static TheoryData<string, string, bool, string> Cases()
    {
        var data = new TheoryData<string, string, bool, string>();

        foreach (string writer in new[] { "Set", "Add", "Out", "OutAppend", "Export", "ExportAppend" })
        {
            foreach (string reader in new[] { "Get", "GetRaw", "Import", "Convert" })
            {
                foreach (bool large in new[] { false, true })
                {
                    foreach (string target in new[] { "existing", "hardlink", "new" })
                    {
                        // 追記は書き方が 1 つ（ハードリンクでも同じ）。Import は CSV を書くコマンドと組み合わせる
                        if (target == "hardlink" && writer.EndsWith("Append"))
                        {
                            continue;
                        }

                        data.Add(writer, reader, large, target);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WriterOpenedFirst_IsSamePathRoundTrip_AndFileUnchanged(string writer, string reader, bool large, string target)
    {
        string a = this._directory.Write("a.csv", System.Text.Encoding.ASCII.GetBytes("A\r\n" + new string('a', large ? Threshold + 10 : 2) + "\r\n"));
        string z = this._directory.Combine("z.csv");
        byte[] original = System.Text.Encoding.ASCII.GetBytes("A\r\nz1\r\n");

        if (target != "new")
        {
            File.WriteAllBytes(z, original);
        }

        if (target == "hardlink")
        {
            Assert.True(FileLinks.TryCreateHardLink(this._directory.Combine("z-other.csv"), z));
        }

        string output = this._directory.Combine("out");
        Directory.CreateDirectory(output);

        string upstream = reader switch
        {
            "Get" => $"Get-ProbedContent -LiteralPath '{a}', '{z}' -Encoding utf8NoBOM",
            "GetRaw" => $"Get-ProbedContent -LiteralPath '{a}', '{z}' -Encoding utf8NoBOM -Raw",
            "Import" => $"Import-ProbedCsv -LiteralPath '{a}', '{z}' -Encoding utf8NoBOM",
            _ => $"Convert-ProbedContent -LiteralPath '{a}', '{z}' -SourceEncoding utf8NoBOM -Encoding utf8BOM -Destination '{output}' -PassThru | ForEach-Object {{ $_.Path }}",
        };

        string downstream = writer switch
        {
            "Set" => $"Set-ProbedContent -LiteralPath '{z}' -Encoding utf8NoBOM",
            "Add" => $"Add-ProbedContent -LiteralPath '{z}' -Encoding utf8NoBOM",
            "Out" => $"Out-ProbedFile -LiteralPath '{z}' -Encoding utf8NoBOM",
            "OutAppend" => $"Out-ProbedFile -LiteralPath '{z}' -Encoding utf8NoBOM -Append",
            "Export" => $"ForEach-Object {{ [pscustomobject]@{{ A = [string]$_ }} }} | Export-ProbedCsv -LiteralPath '{z}' -Encoding utf8NoBOM",
            _ => $"ForEach-Object {{ [pscustomobject]@{{ A = [string]$_ }} }} | Export-ProbedCsv -LiteralPath '{z}' -Encoding utf8NoBOM -Append",
        };

        if (reader == "Import")
        {
            // Import の出力はオブジェクトなので、文字列にしてから書く
            upstream += " | ForEach-Object { $_.A }";
        }

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ {upstream} | {downstream} }} catch {{ $_ }}");
        ErrorRecord[] errors = result.Errors.Concat(result.Output.Select(o => o.BaseObject).OfType<ErrorRecord>()).ToArray();

        Assert.Equal(2, errors.Length);
        Assert.All(errors, e => Assert.Equal("SamePathRoundTrip", e.FullyQualifiedErrorId.Split(',')[0]));
        Assert.Contains(errors, e => e.Exception.Message == ValidationMessages.SamePathReadWhileWriting(z));
        Assert.Contains(errors, e => e.Exception.Message == ValidationMessages.SamePathWriteCancelled(z));

        if (target == "new")
        {
            Assert.False(File.Exists(z));
        }
        else
        {
            Assert.Equal(original, File.ReadAllBytes(z));
        }

        Assert.Empty(Directory.GetFiles(this._directory.Path, "*" + SafeFileOverwrite.TemporarySuffix));
        Assert.False(ActiveWriteRegistry.HasActiveWriters);
    }

    /// <summary>
    /// 書き込み側が開く前に読み込み側が開いた場合（順方向）は、今までどおり書き込み側だけが SamePathRoundTrip
    /// </summary>
    [Fact]
    public void ReaderOpenedFirst_IsUnchanged()
    {
        string a = this._directory.Write("a.txt", System.Text.Encoding.ASCII.GetBytes("a1\r\n"));
        string z = this._directory.Write("z.txt", System.Text.Encoding.ASCII.GetBytes("z1\r\n"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"try {{ Get-ProbedContent -LiteralPath '{z}', '{a}' | Out-ProbedFile -LiteralPath '{z}' }} catch {{ $_ }}");
        ErrorRecord error = result.Errors.Concat(result.Output.Select(o => o.BaseObject).OfType<ErrorRecord>()).Single();

        Assert.Equal("SamePathRoundTrip", error.FullyQualifiedErrorId.Split(',')[0]);
        Assert.Equal(System.Text.Encoding.ASCII.GetBytes("z1\r\n"), File.ReadAllBytes(z));
    }

    /// <summary>
    /// 別のファイルへの書き込みには影響しない（Set-ProbedContent の 2 つの書き込み先のうち、読まれた方だけを取り消す）
    /// </summary>
    [Fact]
    public void OnlyTheReadTarget_IsCancelled()
    {
        string a = this._directory.Write("a.txt", System.Text.Encoding.ASCII.GetBytes("a1\r\n"));
        string y = this._directory.Write("y.txt", System.Text.Encoding.ASCII.GetBytes("y1\r\n"));
        string z = this._directory.Write("z.txt", System.Text.Encoding.ASCII.GetBytes("z1\r\n"));

        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(
            $"Get-ProbedContent -LiteralPath '{a}', '{z}' | Set-ProbedContent -LiteralPath '{y}', '{z}' -Encoding utf8NoBOM -LineBreak CrLf");

        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(System.Text.Encoding.ASCII.GetBytes("z1\r\n"), File.ReadAllBytes(z));
        Assert.Equal(System.Text.Encoding.ASCII.GetBytes("a1\r\n"), File.ReadAllBytes(y));
    }
}
