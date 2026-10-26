using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// CSV の列名の比較器（<see cref="CsvNameComparer"/>）が、.NET 10 の OrdinalIgnoreCase と同じ結果になることを検証する（1.3.0 第六次。BUG-0020）。
/// </summary>
/// <remarks>
/// このテストプロジェクトは net10.0 で動く。5.1（net48）では表（<see cref="CsvNameComparer.Table"/>）で比べるので、
/// ここで「表 = .NET 10 の OrdinalIgnoreCase」を全符号位置で確かめれば、両ホストの結果が一致する。
/// 5.1 で実際に表が使われていることは PSCompat（Sixth/CsvName/*）が確かめる。
/// </remarks>
public class CsvNameComparerTests
{
    /// <summary>
    /// すべての符号位置（U+0000〜U+10FFFF。単独のサロゲートも含む）を 1 文字の文字列にして実行環境の OrdinalIgnoreCase で並べ、
    /// その同値類が、表の同値類（代表の符号位置）と完全に一致すること。
    /// </summary>
    [Fact]
    public void AllCodePoints_TableClassesEqualRuntimeOrdinalIgnoreCase()
    {
        var all = new string[0x110000];

        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            all[cp] = cp >= 0xD800 && cp <= 0xDFFF ? ((char)cp).ToString() : char.ConvertFromUtf32(cp);
        }

        string[] sorted = (string[])all.Clone();
        Array.Sort(sorted, StringComparer.OrdinalIgnoreCase);

        var seenRepresentatives = new HashSet<int>();
        int classes = 0;
        int i = 0;

        while (i < sorted.Length)
        {
            int j = i + 1;

            while (j < sorted.Length && string.Equals(sorted[i], sorted[j], StringComparison.OrdinalIgnoreCase))
            {
                j++;
            }

            // 実行環境で等しい符号位置は、表でも同じ代表を持つ
            int representative = CsvNameComparer.ToRepresentative(CodePoint(sorted[i]));

            for (int k = i + 1; k < j; k++)
            {
                Assert.Equal(representative, CsvNameComparer.ToRepresentative(CodePoint(sorted[k])));
            }

            // 実行環境で別の同値類なら、表でも別の代表を持つ
            Assert.True(seenRepresentatives.Add(representative), $"U+{representative:X4} が 2 つの同値類の代表になっている");

            if (j - i > 1)
            {
                classes++;
            }

            i = j;
        }

        Assert.Equal(CsvNameCaseTable.ClassCount, classes);
    }

    /// <summary>
    /// 代表は同値類の最小の符号位置で、表の組は代表と違う符号位置だけを含む。
    /// </summary>
    [Fact]
    public void Table_PairsAreOrderedAndPointToSmallerRepresentative()
    {
        int[] pairs = CsvNameCaseTable.Pairs;

        Assert.Equal(0, pairs.Length % 2);

        for (int i = 0; i < pairs.Length; i += 2)
        {
            Assert.True(pairs[i + 1] < pairs[i]);
            Assert.Equal(pairs[i + 1], CsvNameComparer.ToRepresentative(pairs[i + 1]));

            if (i > 0)
            {
                Assert.True(pairs[i - 2] < pairs[i]);
            }
        }
    }

    [Theory]
    [InlineData("Ä", "ä", true)]
    [InlineData("ǅ", "ǆ", true)]
    [InlineData("ǅ", "Ǆ", true)]
    [InlineData("ς", "Σ", true)]
    [InlineData("ς", "σ", true)]
    [InlineData("𐐀", "𐐨", true)]
    [InlineData("Name", "NAME", true)]
    [InlineData("ß", "SS", false)]
    [InlineData("İ", "i", false)]
    [InlineData("K", "k", false)]
    [InlineData("a­b", "ab", false)]
    [InlineData("ｱ", "ア", false)]
    [InlineData("\uD801", "\uD801", true)]
    [InlineData("\uD801x", "\uD801X", true)]
    [InlineData("𐐀", "𐐀", true)]
    [InlineData("𐐀", "𐐨", true)]
    [InlineData("𐐀", "𐐁", false)]
    public void KnownPairs_TableAndRuntimeAgree(string x, string y, bool expected)
    {
        Assert.Equal(expected, string.Equals(x, y, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, CsvNameComparer.Table.Equals(x, y));
        Assert.Equal(expected, CsvNameComparer.Instance.Equals(x, y));

        if (expected)
        {
            Assert.Equal(CsvNameComparer.Table.GetHashCode(x), CsvNameComparer.Table.GetHashCode(y));
        }
    }

    /// <summary>
    /// 同値類の符号位置・サロゲート（対になったもの・単独のもの）・ASCII を混ぜた複数文字の文字列で、表と実行環境が一致すること。
    /// </summary>
    [Fact]
    public void RandomStrings_TableAndRuntimeAgree()
    {
        int[] pairs = CsvNameCaseTable.Pairs;
        var pool = new List<string> { "a", "A", "\uD801", "\uDC00", "𐐀", "𐐨", "ß", "s", "S", "K", "k" };

        for (int i = 0; i < pairs.Length; i += 2)
        {
            pool.Add(char.ConvertFromUtf32(pairs[i]));
            pool.Add(char.ConvertFromUtf32(pairs[i + 1]));
        }

        var random = new Random(20261026);

        for (int n = 0; n < 200_000; n++)
        {
            int length = random.Next(1, 5);
            var left = new StringBuilder();
            var right = new StringBuilder();

            for (int k = 0; k < length; k++)
            {
                string piece = pool[random.Next(pool.Count)];
                left.Append(piece);

                // 半分は同じ同値類の符号位置、半分は無関係な符号位置を置く
                right.Append(random.Next(2) == 0 ? Partner(piece, pairs) : pool[random.Next(pool.Count)]);
            }

            string x = left.ToString();
            string y = right.ToString();
            bool runtime = string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            Assert.True(runtime == CsvNameComparer.Table.Equals(x, y), $"{Escape(x)} / {Escape(y)}");

            if (runtime)
            {
                Assert.Equal(CsvNameComparer.Table.GetHashCode(x), CsvNameComparer.Table.GetHashCode(y));
            }
        }
    }

    private static string Partner(string piece, int[] pairs)
    {
        int cp = CodePoint(piece);

        for (int i = 0; i < pairs.Length; i += 2)
        {
            if (pairs[i] == cp)
            {
                return char.ConvertFromUtf32(pairs[i + 1]);
            }

            if (pairs[i + 1] == cp)
            {
                return char.ConvertFromUtf32(pairs[i]);
            }
        }

        return piece;
    }

    private static int CodePoint(string s)
        => s.Length == 2 && char.IsSurrogatePair(s[0], s[1]) ? char.ConvertToUtf32(s[0], s[1]) : s[0];

    private static string Escape(string s) => string.Join(" ", s.Select(c => ((int)c).ToString("X4")));
}
