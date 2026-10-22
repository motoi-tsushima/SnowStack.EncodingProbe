using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using EncodingProbe.PowerShell.Tests.Helpers.CutThreshold;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// UTF-16 / UTF-32 のファイルの改行を、その単位で数えることをコマンドで検証するテスト（1.3.0 第五次。BUG-0014 ほか）。
/// </summary>
/// <remarks>
/// 「上」U+4E0A（UTF-16LE で 0A 4E）・「《」U+300A（0A 30）・「不」U+4E0D（0D 4E）を含む本文で、
/// Resolve-Encoding の LineBreak、Convert-ProbedContent -PassThru の SourceLineBreak、-EncodingFrom による改行の継承、
/// Add-ProbedContent / Export-ProbedCsv -Append が書く改行が、実際の改行と一致することを確かめる。
/// 第四次までは、Resolve-Encoding がすべての組み合わせで混在（LfAndCrAndCrLf / LfAndCr）と報告し、
/// 改行が CR のファイルには LF を書いていた（Convert-ProbedContent の SourceLineBreak は復号した文字列から求めるので正しかった）。
/// </remarks>
public class UnitLineBreakCmdletTests : IClassFixture<ProbedCommandRunspaceFixture>, IDisposable
{
    private const string Header = "日本語の上《不》";

    private static readonly Lazy<List<Sample>> GeneratedSamples = new Lazy<List<Sample>>(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Generator.Own();
    });

    private readonly ProbedCommandRunspaceFixture _fixture;
    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public UnitLineBreakCmdletTests(ProbedCommandRunspaceFixture fixture)
    {
        this._fixture = fixture;
    }

    public void Dispose() => this._directory.Dispose();

    public static IEnumerable<object[]> Cases()
    {
        foreach (string form in new[] { "u16le", "u16be", "u32le", "u32be" })
        foreach (bool bom in new[] { false, true })
        foreach (string lineBreak in new[] { "CrLf", "Lf", "Cr" })
        {
            yield return new object[] { form, bom, lineBreak };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ResolveEncoding_ReportsActualLineBreak(string form, bool bom, string lineBreak)
    {
        string path = WriteCsv(form, bom, lineBreak);

        Assert.Equal(lineBreak, Scalar($"(Resolve-Encoding -Path {Quote(path)} -Culture ja-JP).LineBreak.ToString()"));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConvertProbedContent_SourceLineBreak(string form, bool bom, string lineBreak)
    {
        string path = WriteCsv(form, bom, lineBreak);

        Assert.Equal(lineBreak, Scalar(
            $"(Convert-ProbedContent -LiteralPath {Quote(path)} -Encoding utf8NoBOM -PassThru -Culture ja-JP).SourceLineBreak"));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodingFrom_InheritsActualLineBreak(string form, bool bom, string lineBreak)
    {
        string reference = WriteCsv(form, bom, lineBreak);
        string path = this._directory.Combine("from.txt");
        string ascii = this._directory.Write("convert.txt", Encoding.ASCII.GetBytes("a\r\nb\r\n"));

        Run($"Set-ProbedContent -LiteralPath {Quote(path)} -Value 'x' -EncodingFrom {Quote(reference)} -Culture ja-JP");
        Run($"Convert-ProbedContent -LiteralPath {Quote(ascii)} -EncodingFrom {Quote(reference)} -Culture ja-JP");

        Encoding encoding = GetEncoding(form, bom: false);
        Assert.Equal(lineBreak, LastLineBreak(File.ReadAllBytes(path), encoding));
        Assert.Equal(lineBreak, LastLineBreak(File.ReadAllBytes(ascii), encoding));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Append_WritesActualLineBreak(string form, bool bom, string lineBreak)
    {
        Encoding encoding = GetEncoding(form, bom: false);
        string newLine = NewLine(lineBreak);

        foreach (string command in new[]
        {
            "Add-ProbedContent -LiteralPath {0} -Value '9,8' -Culture ja-JP",
            $"[pscustomobject]@{{ '{Header}' = '9'; B = '8' }} | Export-ProbedCsv -LiteralPath {{0}} -Append -Culture ja-JP",
            $"[pscustomobject]@{{ '{Header}' = '9'; B = '8' }} | Export-ProbedCsv -LiteralPath {{0}} -Append -DetectionLineCount 1 -Culture ja-JP",
        })
        {
            string path = WriteCsv(form, bom, lineBreak);
            byte[] original = File.ReadAllBytes(path);

            Run(command.Replace("{0}", Quote(path)));

            string appended = command.StartsWith("Add", StringComparison.Ordinal) ? "9,8" : "\"9\",\"8\"";
            Assert.Equal(original.Concat(encoding.GetBytes(appended + newLine)).ToArray(), File.ReadAllBytes(path));
        }
    }

    /// <summary>
    /// BUG-0014 の再現の入力（BOM 無し、1 行目に「上」、英数字のほかは「上」だけ）。
    /// 全体の判定では us-ascii になる（追補 4.1 の既知の限界）が、-DetectionLineCount 1 では UTF-16 / UTF-32 と読み替え、
    /// 改行も読み替えた後の単位で数える（1.3.0 仕様書 3.2。第五次で改めた）
    /// </summary>
    [Theory]
    [InlineData("u16le", "Cr")]
    [InlineData("u16be", "Cr")]
    [InlineData("u32le", "Cr")]
    [InlineData("u32be", "Cr")]
    [InlineData("u16le", "CrLf")]
    [InlineData("u16be", "CrLf")]
    [InlineData("u32le", "CrLf")]
    [InlineData("u32be", "CrLf")]
    public void Bug0014_ExportAppendWithDetectionLineCount_WritesActualLineBreak(string form, string lineBreak)
    {
        Encoding encoding = GetEncoding(form, bom: false);
        string newLine = NewLine(lineBreak);
        byte[] original = encoding.GetBytes("A上,B" + newLine + "1,2" + newLine);
        string path = this._directory.Write("bug0014.csv", original);

        if (form == "u16le" && lineBreak == "Cr")
        {
            // 報告の手順 1 のバイト列そのもの
            Assert.Equal(
                new byte[] { 0x41, 0x00, 0x0A, 0x4E, 0x2C, 0x00, 0x42, 0x00, 0x0D, 0x00, 0x31, 0x00, 0x2C, 0x00, 0x32, 0x00, 0x0D, 0x00 },
                original);
        }

        Run($"[pscustomobject]@{{ 'A上' = '9'; B = '8' }} | Export-ProbedCsv -LiteralPath {Quote(path)} -Append -DetectionLineCount 1");

        Assert.Equal(original.Concat(encoding.GetBytes("\"9\",\"8\"" + newLine)).ToArray(), File.ReadAllBytes(path));
        Assert.Equal("A上=1", Scalar(
            $"$r = Import-ProbedCsv -LiteralPath {Quote(path)} -DetectionLineCount 1 | Select-Object -First 1; 'A上=' + $r.'A上'"));
    }

    /// <summary>
    /// 調査文書 11.4 節で「改行だけ違う」とした 2 件。CR-LF のファイルに CR-LF を書く
    /// </summary>
    [Theory]
    [InlineData("H Utf16Be tiny06")]
    [InlineData("H Utf16Be size256 alnum5")]
    public void Investigation_LineBreakOnlyDiffered_NowWritesCrLf(string id)
    {
        Sample sample = GeneratedSamples.Value.Single(s => s.Id == id);
        string path = this._directory.Write("sample.csv", sample.Bytes);
        var encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);

        Run($"$h = (Import-ProbedCsv -LiteralPath {Quote(path)} -Encoding bigendianunicodeNoBOM | Select-Object -First 1).PSObject.Properties.Name; "
            + $"$o = [ordered]@{{}}; foreach ($n in $h) {{ $o[$n] = 'z' }}; "
            + $"[pscustomobject]$o | Export-ProbedCsv -LiteralPath {Quote(path)} -Append -DetectionLineCount 1 -Culture ja-JP");

        byte[] written = File.ReadAllBytes(path);
        Assert.True(written.Length > sample.Bytes.Length);
        Assert.Equal(sample.Bytes, written.Take(sample.Bytes.Length).ToArray());
        string appended = encoding.GetString(written, sample.Bytes.Length, written.Length - sample.Bytes.Length);
        Assert.EndsWith("\"z\"\r\n", appended);
        Assert.DoesNotContain("\r\r", appended);
    }

    private string WriteCsv(string form, bool bom, string lineBreak)
    {
        Encoding encoding = GetEncoding(form, bom);
        string newLine = NewLine(lineBreak);
        string text = string.Join(newLine, Header + ",B", "1上,2《", "不3,4") + newLine;

        return this._directory.Write($"{form}-{lineBreak}-{bom}-{Guid.NewGuid():N}.csv", encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
    }

    private void Run(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);
        Assert.Empty(result.Errors);
    }

    private string Scalar(string script)
    {
        InvocationResult result = this._fixture.InvokeScriptCapturingErrors(script);
        Assert.Empty(result.Errors);
        return Assert.Single(result.Output).BaseObject.ToString()!;
    }

    private static string LastLineBreak(byte[] bytes, Encoding encoding)
    {
        string text = encoding.GetString(bytes).TrimStart('﻿');
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? "CrLf"
            : text.EndsWith("\n", StringComparison.Ordinal) ? "Lf"
            : text.EndsWith("\r", StringComparison.Ordinal) ? "Cr"
            : "None";
    }

    private static string NewLine(string lineBreak) => lineBreak == "CrLf" ? "\r\n" : lineBreak == "Lf" ? "\n" : "\r";

    private static Encoding GetEncoding(string form, bool bom)
    {
        switch (form)
        {
            case "u16le": return new UnicodeEncoding(false, bom);
            case "u16be": return new UnicodeEncoding(true, bom);
            case "u32le": return new UTF32Encoding(false, bom);
            default: return new UTF32Encoding(true, bom);
        }
    }

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
}
