using System;
using System.IO;
using System.Linq;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// 全体の厳密な検証（1.3.0 仕様書 4 章）を検証するテスト。
/// </summary>
/// <remarks>
/// 位置の期待値は、テストデータを組み立てたときに分かっている位置を使う。
/// UTF-8 以外の文字エンコーディングでは、Convert-ProbedContent の位置の求め方（<c>FindInvalidBytes</c>、
/// 1.2.0 で両ホストの一致を確かめたもの）と一致することも確かめる。
/// </remarks>
public class StrictDecodeVerifierTests
{
    /// <summary>バッファの境目にかかる場合を作るためのバッファの大きさ</summary>
    private static readonly int[] BufferSizes = { 1, 2, 3, 4, 5, 7, 16, 64 * 1024 };

    private const int Utf8 = 65001;

    static StrictDecodeVerifierTests()
    {
        CodePagesProviderRegistration.EnsureRegistered();
    }

    /// <summary>
    /// 正しく復号できるファイルは null を返すこと（BOM の有無、多バイト文字がバッファの境目にかかる場合を含む）。
    /// </summary>
    [Theory]
    [InlineData(65001, false)]
    [InlineData(65001, true)]
    [InlineData(932, false)]
    [InlineData(51932, false)]
    [InlineData(1200, false)]
    [InlineData(1200, true)]
    [InlineData(1201, true)]
    [InlineData(12000, true)]
    [InlineData(12001, false)]
    public void Verify_ValidContent_ReturnsNull(int codePage, bool bom)
    {
        Encoding encoding = EncodingVocabulary.BuildEncoding(codePage, emitBom: bom);
        byte[] content = ByteExactFile.Concat(encoding.GetPreamble(), encoding.GetBytes("A,日本語\r\n𠮷,ｶﾅ\r\n"));

        foreach (int bufferSize in BufferSizes)
        {
            Assert.Null(Verify(content, codePage, bufferSize));
        }
    }

    /// <summary>
    /// 先頭に不正なバイト列がある場合。BOM がある場合は、BOM を含めたファイル先頭からの位置を報告すること。
    /// </summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public void Verify_InvalidAtStart_ReportsOffset(bool bom, long expectedOffset)
    {
        // Shift_JIS の「日」(93 FA) を UTF-8 として検証する
        byte[] content = ByteExactFile.Concat(
            bom ? ByteExactFile.Utf8Bom : Array.Empty<byte>(), new byte[] { 0x93, 0xFA, 0x41, 0x0D, 0x0A });

        foreach (int bufferSize in BufferSizes)
        {
            StrictDecodeFailure? failure = Verify(content, Utf8, bufferSize);

            Assert.NotNull(failure);
            Assert.Equal(expectedOffset, failure!.Offset);
            Assert.Equal(new byte[] { 0x93 }, failure.Bytes);
        }
    }

    /// <summary>
    /// 不正なバイト列が、バッファの境目の前後のどこにあっても同じ位置を報告すること。
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    public void Verify_InvalidAroundBufferBoundary_ReportsSameOffset(int offset)
    {
        // 前半は多バイト文字（3 バイト）を含む UTF-8、offset の位置に単独の FF を置く
        byte[] prefix = Encoding.UTF8.GetBytes("日本語abcdefghijklmnopqrstuvwxyz0123456789").Take(offset).ToArray();

        // 多バイト文字の途中で切れないように、切った位置で文字が完結しているものだけを使う
        if (!IsCompleteUtf8(prefix))
        {
            prefix = Encoding.UTF8.GetBytes(new string('a', offset));
        }

        byte[] content = ByteExactFile.Concat(prefix, new byte[] { 0xFF }, Encoding.UTF8.GetBytes("日本\r\n"));

        foreach (int bufferSize in BufferSizes)
        {
            StrictDecodeFailure? failure = Verify(content, Utf8, bufferSize);

            Assert.NotNull(failure);
            Assert.Equal(offset, failure!.Offset);
            Assert.Equal(new byte[] { 0xFF }, failure.Bytes);
        }
    }

    /// <summary>
    /// 多バイト文字がバッファの境目にかかり、その文字の途中に不正なバイトがある場合も、文字の先頭の位置を報告すること。
    /// </summary>
    [Fact]
    public void Verify_InvalidMultiByteSpanningBoundary_ReportsStartOfSequence()
    {
        // E6 97 の後ろに、継続バイトではない 41 が来る（E6 97 が不正）
        byte[] content = ByteExactFile.Concat(Encoding.ASCII.GetBytes("abc"), new byte[] { 0xE6, 0x97, 0x41, 0x42 });

        foreach (int bufferSize in BufferSizes)
        {
            StrictDecodeFailure? failure = Verify(content, Utf8, bufferSize);

            Assert.NotNull(failure);
            Assert.Equal(3, failure!.Offset);
            Assert.Equal(new byte[] { 0xE6, 0x97 }, failure.Bytes);
        }
    }

    /// <summary>
    /// 末尾で途切れた多バイト文字を、最後の flush で検出すること。
    /// </summary>
    [Fact]
    public void Verify_TruncatedAtEnd_ReportsStartOfSequence()
    {
        byte[] content = ByteExactFile.Concat(Encoding.ASCII.GetBytes("abc"), new byte[] { 0xE6, 0x97 });

        foreach (int bufferSize in BufferSizes)
        {
            StrictDecodeFailure? failure = Verify(content, Utf8, bufferSize);

            Assert.NotNull(failure);
            Assert.Equal(3, failure!.Offset);
            Assert.Equal(new byte[] { 0xE6, 0x97 }, failure.Bytes);
        }
    }

    /// <summary>
    /// 64 KiB を超えるファイルで、64 KiB より後ろの不正なバイト列を検出し、位置を報告すること。
    /// </summary>
    [Fact]
    public void Verify_LargerThanBuffer_ReportsOffsetBeyondFirstBuffer()
    {
        // 3 バイトの「日」を並べ、64 KiB の境目を多バイト文字がまたぐようにする
        byte[] body = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("日", 30000)));
        byte[] content = ByteExactFile.Concat(body, new byte[] { 0xC3 }, Encoding.ASCII.GetBytes("\r\nend"));

        StrictDecodeFailure? failure = Verify(content, Utf8, 64 * 1024);

        Assert.NotNull(failure);
        Assert.Equal(body.Length, failure!.Offset);

        using var file = ByteExactFile.Create(content);
        StrictDecodeFailure? fromFile = StrictDecodeVerifier.Verify(file.Path, Utf8);

        Assert.NotNull(fromFile);
        Assert.Equal(body.Length, fromFile!.Offset);
        Assert.Equal(failure.Bytes, fromFile.Bytes);

        // 正しいファイルは検証を通る
        using var valid = ByteExactFile.Create(ByteExactFile.Concat(body, Encoding.ASCII.GetBytes("\r\nend")));
        Assert.Null(StrictDecodeVerifier.Verify(valid.Path, Utf8));
    }

    /// <summary>
    /// UTF-8 以外の文字エンコーディングでも、Convert-ProbedContent と同じ位置・同じバイト列を報告すること。
    /// </summary>
    [Theory]
    [InlineData(932, new byte[] { 0x41, 0x93, 0xFA, 0x0D, 0x0A, 0x82, 0xA0, 0x82, 0x20, 0x41 })]
    [InlineData(932, new byte[] { 0x41, 0x42, 0x82 })]
    [InlineData(51932, new byte[] { 0x41, 0xC6, 0xFC, 0x0A, 0x8E, 0x20, 0x41 })]
    [InlineData(936, new byte[] { 0xC4, 0xE3, 0x41, 0x81, 0x20, 0x41 })]
    [InlineData(1200, new byte[] { 0x41, 0x00, 0x42, 0x00, 0x00, 0xDC, 0x43, 0x00 })]
    [InlineData(1201, new byte[] { 0x00, 0x41, 0xD8, 0x00, 0x00, 0x42 })]
    [InlineData(12000, new byte[] { 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x00 })]
    public void Verify_OtherEncodings_MatchesConvertProbedContent(int codePage, byte[] content)
    {
        Encoding strict = EncodingVocabulary.BuildStrictEncoding(codePage);
        int expectedOffset = ConvertProbedContentCommand.FindInvalidBytes(strict, content, 0, out byte[] expectedBytes);

        foreach (int bufferSize in BufferSizes)
        {
            StrictDecodeFailure? failure = Verify(content, codePage, bufferSize);

            Assert.NotNull(failure);
            Assert.Equal(expectedOffset, failure!.Offset);
            Assert.Equal(expectedBytes, failure.Bytes);
        }
    }

    /// <summary>
    /// BOM で決まった文字エンコーディングでも、BOM の後ろを検証すること（例外を設けない）。
    /// </summary>
    [Fact]
    public void Verify_WithUtf16Bom_SkipsBomAndVerifiesRest()
    {
        byte[] content = ByteExactFile.Concat(ByteExactFile.Utf16LeBom, new byte[] { 0x41, 0x00, 0x00, 0xDC });

        StrictDecodeFailure? failure = Verify(content, 1200, 64 * 1024);

        Assert.NotNull(failure);
        Assert.Equal(4, failure!.Offset);
    }

    /// <summary>
    /// 0 バイトのファイルは検証を通ること。
    /// </summary>
    [Fact]
    public void Verify_Empty_ReturnsNull()
    {
        Assert.Null(Verify(Array.Empty<byte>(), Utf8, 64 * 1024));
    }

    /// <summary>
    /// us-ascii を UTF-8 に読み替えた検証では、ESC の直後が '$'・'('・')' のバイト列を見つけたら失敗とすること（第五次。BUG-0009）。
    /// ESC と次のバイトがバッファの境目にかかる場合も見つける。指定しなければ探さない
    /// </summary>
    [Theory]
    [InlineData(0x24)]
    [InlineData(0x28)]
    [InlineData(0x29)]
    public void Verify_RejectIso2022Escape_FindsEscapeAtOffset(byte intermediate)
    {
        byte[] content = Encoding.ASCII.GetBytes("A,B\r\n\"x").Concat(new byte[] { 0x1B, intermediate, 0x42 }).Concat(Encoding.ASCII.GetBytes("\"\r\n")).ToArray();

        foreach (int bufferSize in BufferSizes)
        {
            using (var stream = new MemoryStream(content, writable: false))
            {
                StrictDecodeFailure failure = Assert.IsType<StrictDecodeFailure>(
                    StrictDecodeVerifier.Verify(stream, Utf8, bufferSize, rejectIso2022Escape: true));

                Assert.True(failure.IsIso2022Escape);
                Assert.Equal(7, failure.Offset);
                Assert.Equal(new byte[] { 0x1B, intermediate }, failure.Bytes);
            }

            Assert.Null(Verify(content, Utf8, bufferSize));
        }
    }

    /// <summary>
    /// ESC の後ろが ISO-2022 の中間バイトでなければ（端末の色の ESC [ など）失敗としないこと
    /// </summary>
    [Fact]
    public void Verify_RejectIso2022Escape_IgnoresOtherEscapes()
    {
        byte[] content = Encoding.ASCII.GetBytes("\u001B[31mred\u001B[0m\r\n\u001B\r\n");

        foreach (int bufferSize in BufferSizes)
        {
            using var stream = new MemoryStream(content, writable: false);

            Assert.Null(StrictDecodeVerifier.Verify(stream, Utf8, bufferSize, rejectIso2022Escape: true));
        }
    }

    private static StrictDecodeFailure? Verify(byte[] content, int codePage, int bufferSize)
    {
        using var stream = new MemoryStream(content, writable: false);

        return StrictDecodeVerifier.Verify(stream, codePage, bufferSize);
    }

    private static bool IsCompleteUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
