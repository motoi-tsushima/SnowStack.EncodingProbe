using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.FileSystemTests;

/// <summary>
/// 置き換えに使う一時ファイルの名前が、元の名前によらない固定長であることを検証する（1.3.0 第六次 B。BUG-0027）。
/// </summary>
/// <remarks>
/// 第五次までは <c>.&lt;元の名前&gt;.&lt;8 桁&gt;.probedtmp</c> で、名前の長い書き込み先（218 文字以上）では 1 つの名前の上限（255 文字）を超えて変換できなかった。
/// 一時ファイルを使うのは 1 MiB を超える上書き・新規作成だけ（1 MiB 以内はメモリに写してから直接書く）。
/// </remarks>
[Collection(OverwriteStateCollection.Name)]
public class TemporaryFileNameTests : IDisposable
{
    private static readonly Regex TemporaryName = new Regex(@"^\.~probed[0-9a-f]{8}\.probedtmp$");

    private readonly TemporaryDirectory _directory = new TemporaryDirectory();

    public void Dispose() => this._directory.Dispose();

    private static int Threshold => checked((int)SafeFileOverwrite.DirectWriteThreshold);

    [Theory]
    [InlineData("report.txt")]
    [InlineData("monthly-sales.csv")]
    public void TemporaryFile_HasFixedLengthName_WithoutOriginalName(string name)
    {
        string path = this._directory.Write(name, new byte[] { 0x6F, 0x0D, 0x0A });
        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);

        try
        {
            byte[] content = Enumerable.Repeat((byte)'n', Threshold + 1).ToArray();
            overwrite.Write(content, 0, content.Length);

            Assert.Equal(OverwriteMethod.Replace, overwrite.Method);
            string temporary = Assert.Single(Directory.GetFiles(this._directory.Path, "*" + SafeFileOverwrite.TemporarySuffix));
            string temporaryName = Path.GetFileName(temporary);

            Assert.Matches(TemporaryName, temporaryName);
            Assert.Equal(26, temporaryName.Length);
            Assert.DoesNotContain(Path.GetFileNameWithoutExtension(name), temporaryName);
        }
        finally
        {
            overwrite.Rollback();
        }

        Assert.Empty(Directory.GetFiles(this._directory.Path, "*" + SafeFileOverwrite.TemporarySuffix));
    }

    /// <summary>
    /// 名前が 240 文字のファイルも、1 MiB を超える上書き・変換ができ、一時ファイルが残らない（7.x で第五次まで WriteFailed だった）
    /// </summary>
    [Theory]
    [InlineData(218)]
    [InlineData(240)]
    [InlineData(250)]
    public void LongName_LargeOverwrite_Succeeds(int nameLength)
    {
        string name = new string('n', nameLength - 4) + ".txt";
        string path = this._directory.Write(name, Enumerable.Repeat((byte)'o', Threshold + 10).ToArray());
        byte[] content = Enumerable.Repeat((byte)'y', Threshold + 20).ToArray();

        SafeFileOverwrite overwrite = SafeFileOverwrite.Begin(path, DanglingLinkPolicy.Reject, 4096);
        overwrite.Write(content, 0, content.Length);
        overwrite.Commit();

        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(this._directory.Path, "*" + SafeFileOverwrite.TemporarySuffix));
    }
}
