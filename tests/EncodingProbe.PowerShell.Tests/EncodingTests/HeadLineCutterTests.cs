using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// 先頭 N 行の切り出し（1.3.0 仕様書 2 章）を検証するテスト。
/// </summary>
/// <remarks>
/// 期待値は .NET の Encoding で組み立てたバイト列から求める（切り出しの実装を使って期待値を作らない）。
/// バッファの境目にかかる場合を確かめるため、同じ入力を複数のバッファの大きさで切り出す。
/// </remarks>
public class HeadLineCutterTests
{
    /// <summary>走査のバッファの大きさ。1〜5 は、改行や単位がバッファの境目にかかる場合を作る</summary>
    private static readonly int[] BufferSizes = { 1, 2, 3, 4, 5, 7, 64 * 1024 };

    private static readonly string[] Lines = { "A,B", "1,日本", "3,4" };

    public static IEnumerable<object[]> UnitCases()
    {
        string[] encodings = { "utf8", "utf16le", "utf16be", "utf32le", "utf32be" };
        string[] lineBreaks = { "\r\n", "\n", "\r" };

        foreach (string encoding in encodings)
        {
            foreach (bool bom in new[] { false, true })
            {
                foreach (string lineBreak in lineBreaks)
                {
                    foreach (long lineCount in new long[] { 1, 2, 3, 5 })
                    {
                        yield return new object[] { encoding, bom, lineBreak, lineCount };
                    }
                }
            }
        }
    }

    /// <summary>
    /// 5 つの改行の型 × BOM の有無 × CR-LF / LF / CR × N（1・途中・改行の数・改行の数より多い）で、
    /// N 個目の改行の末尾で切れ、型が正しく決まること。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnitCases))]
    public void Cut_CutsAfterNthLineBreak(string encodingName, bool bom, string lineBreak, long lineCount)
    {
        (Encoding encoding, byte[] preamble, LineBreakUnit unit) = GetEncoding(encodingName);
        byte[] bomBytes = bom ? preamble : Array.Empty<byte>();
        byte[] content = ByteExactFile.Concat(bomBytes, encoding.GetBytes(string.Concat(Lines.Select(l => l + lineBreak))));

        int taken = (int)Math.Min(lineCount, Lines.Length);
        byte[] expected = ByteExactFile.Concat(
            bomBytes, encoding.GetBytes(string.Concat(Lines.Take(taken).Select(l => l + lineBreak))));

        foreach (int bufferSize in BufferSizes)
        {
            HeadLineCut cut = Cut(content, lineCount, bufferSize);

            Assert.Equal(expected, cut.Bytes);
            Assert.Equal(unit, cut.Unit);
            Assert.Equal(lineCount >= Lines.Length, cut.IsWholeFile);
            Assert.Equal(0, cut.Bytes.Length % UnitSize(unit));
        }
    }

    /// <summary>
    /// CR-LF は 1 個の改行として数え、CR と LF の間で切らないこと。CR だけの改行は 1 個と数えること。
    /// </summary>
    [Theory]
    [InlineData("a\r\nb\rc\nd", 1, "a\r\n")]
    [InlineData("a\r\nb\rc\nd", 2, "a\r\nb\r")]
    [InlineData("a\r\nb\rc\nd", 3, "a\r\nb\rc\n")]
    [InlineData("a\r\nb\rc\nd", 4, "a\r\nb\rc\nd")]
    [InlineData("a\r\r\nb", 1, "a\r")]
    [InlineData("a\r\r\nb", 2, "a\r\r\n")]
    [InlineData("a\n\nb", 2, "a\n\n")]
    [InlineData("a\r", 1, "a\r")]
    public void Cut_CountsCrLfAsOneLineBreak(string text, long lineCount, string expected)
    {
        foreach (string encodingName in new[] { "utf8", "utf16le", "utf16be", "utf32le", "utf32be" })
        {
            (Encoding encoding, _, _) = GetEncoding(encodingName);

            foreach (int bufferSize in BufferSizes)
            {
                HeadLineCut cut = Cut(encoding.GetBytes(text), lineCount, bufferSize);

                Assert.Equal(encoding.GetBytes(expected), cut.Bytes);
            }
        }
    }

    /// <summary>
    /// UTF-16LE の「上」(U+4E0A = 0A 4E) のように、改行の形に一致しない 0A は改行と数えないこと。
    /// </summary>
    [Fact]
    public void Cut_Utf16Le_ByteOfKanjiIsNotLineBreak()
    {
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        byte[] content = encoding.GetBytes("上\n下\n");

        Assert.Equal(0x0A, content[0]);

        foreach (int bufferSize in BufferSizes)
        {
            HeadLineCut cut = Cut(content, 1, bufferSize);

            Assert.Equal(LineBreakUnit.Utf16Le, cut.Unit);
            Assert.Equal(encoding.GetBytes("上\n"), cut.Bytes);
        }
    }

    /// <summary>
    /// UTF-16BE の「上」(U+4E0A = 4E 0A) の 0A も、直前が 00 ではないので改行と数えないこと。
    /// </summary>
    [Fact]
    public void Cut_Utf16Be_ByteOfKanjiIsNotLineBreak()
    {
        var encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
        byte[] content = encoding.GetBytes("上上\n下\n");

        foreach (int bufferSize in BufferSizes)
        {
            HeadLineCut cut = Cut(content, 1, bufferSize);

            Assert.Equal(LineBreakUnit.Utf16Be, cut.Unit);
            Assert.Equal(encoding.GetBytes("上上\n"), cut.Bytes);
        }
    }

    /// <summary>
    /// UTF-32 の改行は UTF-16 の改行の形も含む。UTF-32 を優先すること。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cut_PrefersUtf32OverUtf16(bool bigEndian)
    {
        var encoding = new UTF32Encoding(bigEndian, byteOrderMark: false);
        byte[] content = encoding.GetBytes("a\nb\n");

        foreach (int bufferSize in BufferSizes)
        {
            HeadLineCut cut = Cut(content, 1, bufferSize);

            Assert.Equal(bigEndian ? LineBreakUnit.Utf32Be : LineBreakUnit.Utf32Le, cut.Unit);
            Assert.Equal(encoding.GetBytes("a\n"), cut.Bytes);
        }
    }

    /// <summary>
    /// 単位の境界に合わない位置の 0A は改行と数えないこと。
    /// UTF-16LE の U+0A0A（0A 0A）は、どちらの 0A も直後（または直前）が 00 ではないので改行ではない。
    /// </summary>
    [Fact]
    public void Cut_Utf16Le_MisalignedLineBreakPatternIsIgnored()
    {
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        // "a" U+0A0A "b" LF ... : 61 00 0A 0A 62 00 0A 00
        byte[] content = encoding.GetBytes("aਊb\nc\n");

        foreach (int bufferSize in BufferSizes)
        {
            HeadLineCut cut = Cut(content, 1, bufferSize);

            Assert.Equal(LineBreakUnit.Utf16Le, cut.Unit);
            Assert.Equal(encoding.GetBytes("aਊb\n"), cut.Bytes);
        }
    }

    /// <summary>
    /// BOM がある場合は BOM から型を決め、位置の条件はファイル先頭（BOM を含む）から数えること。
    /// UTF-32LE の BOM（FF FE 00 00）を UTF-16LE と取り違えないこと。
    /// </summary>
    [Fact]
    public void Cut_Utf32LeBom_IsNotTakenAsUtf16Le()
    {
        var encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: false);
        byte[] content = ByteExactFile.Concat(ByteExactFile.Utf32LeBom, encoding.GetBytes("x\ny\n"));

        HeadLineCut cut = Cut(content, 1, 64 * 1024);

        Assert.Equal(LineBreakUnit.Utf32Le, cut.Unit);
        Assert.Equal(ByteExactFile.Concat(ByteExactFile.Utf32LeBom, encoding.GetBytes("x\n")), cut.Bytes);
    }

    /// <summary>
    /// BOM 無しで 0x00 が無ければ 1 バイトの型とすること（Shift_JIS など ASCII 互換の文字エンコーディング）。
    /// </summary>
    [Fact]
    public void Cut_NoZeroByte_IsSingleByte()
    {
        // Shift_JIS の「日本」= 93 FA 96 7B
        byte[] content = { 0x93, 0xFA, 0x96, 0x7B, 0x0D, 0x0A, 0x41, 0x0D, 0x0A };

        foreach (int bufferSize in BufferSizes)
        {
            HeadLineCut cut = Cut(content, 1, bufferSize);

            Assert.Equal(LineBreakUnit.SingleByte, cut.Unit);
            Assert.Equal(content.Take(6).ToArray(), cut.Bytes);
        }
    }

    /// <summary>
    /// 0x00 の探索は先頭 64 KiB に限ること。64 KiB より後ろにだけ 0x00 があるファイルは 1 バイトの型になる。
    /// </summary>
    [Fact]
    public void Cut_ZeroByteBeyondProbeRange_IsSingleByte()
    {
        var content = new byte[HeadLineCutter.ZeroProbeLength + 8];

        for (int i = 0; i < content.Length; i++)
        {
            content[i] = 0x41;
        }

        content[HeadLineCutter.ZeroProbeLength + 1] = 0x00;
        content[HeadLineCutter.ZeroProbeLength + 2] = 0x0A;
        content[HeadLineCutter.ZeroProbeLength + 3] = 0x00;

        HeadLineCut cut = Cut(content, 1, 64 * 1024);

        Assert.Equal(LineBreakUnit.SingleByte, cut.Unit);
        Assert.Equal(HeadLineCutter.ZeroProbeLength + 3, cut.Bytes.Length);
    }

    /// <summary>
    /// 改行が無いファイルは、全体を切り出すこと。BOM が無ければ型は None、BOM があれば BOM の型。
    /// </summary>
    [Theory]
    [InlineData("utf8", false, "None")]
    [InlineData("utf8", true, "SingleByte")]
    [InlineData("utf16le", false, "None")]
    [InlineData("utf16le", true, "Utf16Le")]
    [InlineData("utf32be", false, "None")]
    [InlineData("utf32be", true, "Utf32Be")]
    public void Cut_NoLineBreak_CutsWholeFile(string encodingName, bool bom, string expectedUnitName)
    {
        (Encoding encoding, byte[] preamble, _) = GetEncoding(encodingName);
        byte[] content = ByteExactFile.Concat(bom ? preamble : Array.Empty<byte>(), encoding.GetBytes("abc,日本"));

        foreach (int bufferSize in BufferSizes)
        {
            HeadLineCut cut = Cut(content, 1, bufferSize);

            Assert.Equal(content, cut.Bytes);
            Assert.Equal(Enum.Parse<LineBreakUnit>(expectedUnitName), cut.Unit);
            Assert.True(cut.IsWholeFile);
        }
    }

    /// <summary>
    /// 0 バイトのファイルは、空のバイト列を全体として返すこと。
    /// </summary>
    [Fact]
    public void Cut_EmptyFile_ReturnsEmpty()
    {
        HeadLineCut cut = Cut(Array.Empty<byte>(), 1, 64 * 1024);

        Assert.Empty(cut.Bytes);
        Assert.Equal(LineBreakUnit.None, cut.Unit);
        Assert.True(cut.IsWholeFile);
    }

    /// <summary>
    /// 既知の限界（仕様書 2.4）: BOM 無しの UTF-16 で ASCII 文字も改行も無いファイルは、0x00 が無いので
    /// 1 バイトの型と決まり、漢字の 1 バイト目の 0A で切れる。挙動を固定する。
    /// </summary>
    [Fact]
    public void Cut_KnownLimit_Utf16WithoutAsciiOrLineBreak_IsTakenAsSingleByte()
    {
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        // 上 = 0A 4E、下 = 0B 4E
        byte[] content = encoding.GetBytes("上下上下");

        HeadLineCut cut = Cut(content, 1, 64 * 1024);

        Assert.Equal(LineBreakUnit.SingleByte, cut.Unit);
        Assert.Equal(new byte[] { 0x0A }, cut.Bytes);
    }

    /// <summary>
    /// 切り出す範囲が上限ちょうどなら切り出せ、上限を超えたら判定できないエラーにすること。
    /// </summary>
    [Fact]
    public void Cut_ExceedingLimit_Throws()
    {
        byte[] content = Encoding.ASCII.GetBytes("abcd\nefgh\n");

        // 1 行目（5 バイト）は上限 5 に収まる
        Assert.Equal(5, Cut(content, 1, 4, maxLength: 5).Bytes.Length);

        // 2 行目まで（10 バイト）は上限 9 を超える
        var exception = Assert.Throws<EncodingDetectionException>(() => Cut(content, 2, 4, maxLength: 9));
        Assert.Equal(EncodingDetectionException.DetectionFailedId, exception.ErrorId);

        // 改行が N 個未満で全体（10 バイト）が上限を超える
        Assert.Throws<EncodingDetectionException>(() => Cut(content, 3, 4, maxLength: 9));
    }

    /// <summary>
    /// 改行の無いファイルが上限を超える場合は、型を決める段階で打ち切ってエラーにすること。
    /// </summary>
    [Fact]
    public void Cut_NoLineBreakExceedingLimit_Throws()
    {
        byte[] content = Encoding.ASCII.GetBytes("abcdefghij");

        Assert.Throws<EncodingDetectionException>(() => Cut(content, 1, 4, maxLength: 9));
        Assert.Equal(10, Cut(content, 1, 4, maxLength: 10).Bytes.Length);
    }

    /// <summary>
    /// ファイルから切り出す入口が、ストリームの場合と同じ結果を返すこと。
    /// </summary>
    [Fact]
    public void Cut_FromFile_SameAsStream()
    {
        byte[] content = ByteExactFile.Concat(ByteExactFile.Utf16BeBom, Encoding.BigEndianUnicode.GetBytes("x\r\ny\r\nz"));

        using var file = ByteExactFile.Create(content);

        HeadLineCut cut = HeadLineCutter.Cut(file.Path, 2);

        Assert.Equal(LineBreakUnit.Utf16Be, cut.Unit);
        Assert.Equal(content.Take(2 + 12).ToArray(), cut.Bytes);
        Assert.False(cut.IsWholeFile);
    }

    private static HeadLineCut Cut(byte[] content, long lineCount, int bufferSize, long maxLength = 0x7FFFFFC7)
    {
        using var stream = new MemoryStream(content, writable: false);

        return HeadLineCutter.Cut(stream, "test.txt", lineCount, maxLength, bufferSize);
    }

    private static (Encoding Encoding, byte[] Preamble, LineBreakUnit Unit) GetEncoding(string name)
    {
        switch (name)
        {
            case "utf8":
                return (new UTF8Encoding(false), ByteExactFile.Utf8Bom, LineBreakUnit.SingleByte);
            case "utf16le":
                return (new UnicodeEncoding(false, false), ByteExactFile.Utf16LeBom, LineBreakUnit.Utf16Le);
            case "utf16be":
                return (new UnicodeEncoding(true, false), ByteExactFile.Utf16BeBom, LineBreakUnit.Utf16Be);
            case "utf32le":
                return (new UTF32Encoding(false, false), ByteExactFile.Utf32LeBom, LineBreakUnit.Utf32Le);
            case "utf32be":
                return (new UTF32Encoding(true, false), ByteExactFile.Utf32BeBom, LineBreakUnit.Utf32Be);
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }
    }

    private static int UnitSize(LineBreakUnit unit) => unit switch
    {
        LineBreakUnit.Utf16Le or LineBreakUnit.Utf16Be => 2,
        LineBreakUnit.Utf32Le or LineBreakUnit.Utf32Be => 4,
        _ => 1,
    };
}
