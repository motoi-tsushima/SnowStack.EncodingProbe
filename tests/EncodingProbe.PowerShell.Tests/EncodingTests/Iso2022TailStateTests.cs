using System.IO;
using System.Linq;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// ISO-2022 の追記先の末尾の状態から、追記の前に書く「ASCII に戻すシーケンス」を求める部品のテスト（1.3.0 第六次。BUG-0018）。
/// </summary>
public class Iso2022TailStateTests
{
    private static readonly byte[] Si = { 0x0F };
    private static readonly byte[] ToAscii = { 0x1B, 0x28, 0x42 };
    private static readonly byte[] None = new byte[0];

    [Theory]
    [InlineData(new byte[] { 0x41, 0x0D, 0x0A }, "")]                                        // ESC が無い
    [InlineData(new byte[] { 0x31, 0x1B, 0x24, 0x42, 0x46, 0x7C }, "1B 28 42")]               // ESC $ B のまま
    [InlineData(new byte[] { 0x31, 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x0D, 0x0A }, "1B 28 42")]   // JIS のまま CRLF（改行は状態を戻さない）
    [InlineData(new byte[] { 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x1B, 0x28, 0x42 }, "")]           // ESC ( B で戻っている
    [InlineData(new byte[] { 0x1B, 0x24, 0x40, 0x46, 0x7C }, "1B 28 42")]                     // ESC $ @（旧 JIS）
    [InlineData(new byte[] { 0x1B, 0x24, 0x28, 0x44, 0x22, 0x2F }, "1B 28 42")]               // ESC $ ( D（JIS X 0212）
    [InlineData(new byte[] { 0x1B, 0x24, 0x28, 0x42, 0x46, 0x7C }, "1B 28 42")]               // ESC $ ( B は ASCII ではない
    [InlineData(new byte[] { 0x1B, 0x28, 0x4A, 0x5C }, "1B 28 42")]                           // ESC ( J（JIS X 0201 ローマ字）
    [InlineData(new byte[] { 0x1B, 0x28, 0x49, 0x31 }, "1B 28 42")]                           // ESC ( I（片仮名）
    [InlineData(new byte[] { 0x1B, 0x29, 0x49, 0x0E, 0x31 }, "0F")]                           // 50222 の SO（G1 の指示は数えない）
    [InlineData(new byte[] { 0x1B, 0x29, 0x49, 0x0E, 0x31, 0x0F, 0x41 }, "")]                 // SI で戻っている
    [InlineData(new byte[] { 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x1B, 0x29, 0x49, 0x0E, 0x31 }, "0F 1B 28 42")]  // SO と JIS の両方
    [InlineData(new byte[] { 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x1B, 0x28, 0x42, 0x1B, 0x24 }, "")]  // 末尾で途中で切れた ESC は数えない
    [InlineData(new byte[] { 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x1B }, "1B 28 42")]
    public void Jis(byte[] content, string expected)
    {
        foreach (int codePage in new[] { 50220, 50221, 50222 })
        {
            Assert.Equal(expected, Hex(Reset(content, codePage)));
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x1B, 0x24, 0x29, 0x43, 0x41, 0x0D, 0x0A, 0x0E, 0x30, 0x21 }, "0F")]
    [InlineData(new byte[] { 0x1B, 0x24, 0x29, 0x43, 0x0E, 0x30, 0x21, 0x0F, 0x0D, 0x0A }, "")]
    [InlineData(new byte[] { 0x1B, 0x24, 0x29, 0x43, 0x41 }, "")]
    public void Kr_OnlyShiftMatters(byte[] content, string expected)
    {
        Assert.Equal(expected, Hex(Reset(content, 50225)));
    }

    [Theory]
    [InlineData(932)]
    [InlineData(65001)]
    [InlineData(52936)]
    public void OtherCodePages_NeverReset(int codePage)
    {
        Assert.Equal(None, Reset(new byte[] { 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x0E }, codePage));
        Assert.False(Iso2022TailState.AppliesTo(codePage));
    }

    /// <summary>
    /// 逆向きに読むブロック（64 KiB）の境目にまたがるエスケープシーケンスと、末尾から 64 KiB より前にある指示。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EscapeAcrossBlockBoundary(int bytesBeforeBoundary)
    {
        const int block = 64 * 1024;
        const int escapeAt = 10;

        // ESC $ ( D の ESC を、末尾から block バイト前の境目の bytesBeforeBoundary バイト手前に置く。
        // bytesBeforeBoundary が 1〜3 なら、ESC の後ろのバイトは後ろのブロックにある
        int length = escapeAt + bytesBeforeBoundary + block;
        var content = new byte[length];

        for (int i = 0; i < length; i++)
        {
            content[i] = i < escapeAt ? (byte)0x41 : (byte)0x46;
        }

        content[escapeAt] = 0x1B;
        content[escapeAt + 1] = 0x24;
        content[escapeAt + 2] = 0x28;
        content[escapeAt + 3] = 0x44;

        Assert.Equal(0x1B, content[length - block - bytesBeforeBoundary]);
        Assert.Equal(ToAscii, Reset(content, 50220));

        // 同じ位置が ESC ( B なら戻さない
        content[escapeAt + 1] = 0x28;
        content[escapeAt + 2] = 0x42;
        content[escapeAt + 3] = 0x41;
        Assert.Equal(None, Reset(content, 50220));
    }

    [Fact]
    public void DesignationFarFromEnd_IsFound()
    {
        var content = new byte[] { 0x1B, 0x24, 0x42 }.Concat(Enumerable.Repeat((byte)0x30, 300_000)).ToArray();

        Assert.Equal(ToAscii, Reset(content, 50220));
        Assert.Equal(Si.Concat(ToAscii), Reset(new byte[] { 0x0E }.Concat(content).ToArray(), 50222));
    }

    /// <summary>
    /// 開いたときの長さより後ろは読まない。
    /// </summary>
    [Fact]
    public void ReadsOnlyUpToGivenLength()
    {
        byte[] content = { 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x1B, 0x28, 0x42 };

        using var stream = new MemoryStream(content);
        Assert.Equal(ToAscii, Iso2022TailState.GetResetSequence(stream, 5, 50220));
        Assert.Equal(None, Iso2022TailState.GetResetSequence(stream, 0, 50220));
    }

    private static byte[] Reset(byte[] content, int codePage)
    {
        using var stream = new MemoryStream(content);
        return Iso2022TailState.GetResetSequence(stream, content.Length, codePage);
    }

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
