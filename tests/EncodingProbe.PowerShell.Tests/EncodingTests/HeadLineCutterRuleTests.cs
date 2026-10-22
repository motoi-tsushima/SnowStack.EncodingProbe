using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EncodingProbe.PowerShell.Tests.Helpers.CutThreshold;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// 切り出しで改行の型を決める規則（1.3.0 仕様書 2.4。第五次）を、調査の 1033 件で検証するテスト。
/// </summary>
/// <remarks>
/// 入力は docs/EncodingProbe-1.3.0-調査-切り出しの境目.md の 1033 件（R 群を含む）を、調査と同じ生成の処理（<see cref="Generator"/>）で作る。
/// 期待値は、調査文書 1 章の囲みの規則をそのまま書いたモデル（<see cref="Model"/>。浮動小数点で比べる）で求め、
/// 製品の <see cref="HeadLineCutter.DecideUnit"/>（整数で比べる）と全件で一致することを確かめる。
/// 1 バイトの型になり、0x00 があり、ファイル全体が 64 KiB 以内のものは、手当てのとおり None を期待値にする。
/// </remarks>
public class HeadLineCutterRuleTests
{
    private const int Window = 64 * 1024;

    /// <summary>調査の manifest.csv の sha256 列（1033 行）を、改行（LF）でつないだ文字列の SHA-256</summary>
    private const string ManifestDigest = "2d7a18549abd6b206ad671225bfe54048181a69e2848d5c0dff00c703519e978";

    private static readonly Lazy<List<Sample>> Samples = new Lazy<List<Sample>>(CreateSamples);

    /// <summary>
    /// 生成した入力が、調査で使った入力とバイト単位で同じであること
    /// </summary>
    [Fact]
    public void Samples_AreSameAsInvestigation()
    {
        List<Sample> samples = Samples.Value;

        Assert.Equal(1033, samples.Count);

        using (SHA256 sha = SHA256.Create())
        {
            string joined = string.Join("\n", samples.Select(s => ToHex(sha.ComputeHash(s.Bytes))));

            Assert.Equal(ManifestDigest, ToHex(sha.ComputeHash(Encoding.ASCII.GetBytes(joined))));
        }
    }

    /// <summary>
    /// 製品が決める型が、1033 件すべてで調査の規則のモデル（と手当て）に一致すること
    /// </summary>
    [Fact]
    public void DecideUnit_MatchesRuleModel_AllSamples()
    {
        var mismatches = new List<string>();

        foreach (Sample sample in Samples.Value)
        {
            int length = Math.Min(sample.Bytes.Length, Window);
            bool isWholeFile = sample.Bytes.Length <= Window;

            LineBreakUnit actual = HeadLineCutter.DecideUnit(sample.Bytes, length, isWholeFile);
            LineBreakUnit expected = Model.Decide(sample.Bytes, applyWholeFileTreatment: true);

            if (actual != expected)
            {
                mismatches.Add($"{sample.Group} {sample.Id}: expected {expected}, actual {actual}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// 手当ての前の規則のモデルが、調査文書の数（改めた規則で 1033 件中 71 件を誤る。11.2 節）を再現すること。
    /// モデルの書き写しの誤りを見つけるための検査である
    /// </summary>
    [Fact]
    public void Model_ReproducesInvestigationErrorCount()
    {
        int errors = Samples.Value.Count(s => Model.Decide(s.Bytes, applyWholeFileTreatment: false).ToString() != s.Truth);

        Assert.Equal(71, errors);
    }

    /// <summary>
    /// 手当てで None になるのは、1 バイトの型・0x00 あり・64 KiB 以内のものだけ。
    /// 調査文書 11.4 節で黙って化けた 2 件（H Utf16Le tiny06 / tiny12）は None（ファイル全体）になる
    /// </summary>
    [Theory]
    [InlineData("H Utf16Le tiny06")]
    [InlineData("H Utf16Le tiny12")]
    public void DecideUnit_SilentlyCorruptedShortUtf16_BecomesWholeFile(string id)
    {
        Sample sample = Samples.Value.Single(s => s.Id == id);

        Assert.Equal(LineBreakUnit.None, HeadLineCutter.DecideUnit(sample.Bytes, sample.Bytes.Length, isWholeFile: true));
    }

    /// <summary>
    /// 64 KiB を超えるファイルには手当てを使わない（行で切る）
    /// </summary>
    [Fact]
    public void DecideUnit_LongSingleByteWithZero_IsSingleByte()
    {
        var bytes = new byte[Window + 16];

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 50 == 49 ? 0x0A : 'a' + (i % 26));
        }

        bytes[10] = 0x00;

        Assert.Equal(LineBreakUnit.SingleByte, HeadLineCutter.DecideUnit(bytes, Window, isWholeFile: false));
        Assert.Equal(LineBreakUnit.None, HeadLineCutter.DecideUnit(bytes, Window, isWholeFile: true));
    }

    private static List<Sample> CreateSamples()
    {
        CodePagesProviderRegistration.EnsureRegistered();

        var samples = new List<Sample>();
        samples.AddRange(Generator.TesterUtf());
        samples.AddRange(Generator.TesterNul());
        samples.AddRange(Generator.Own());
        samples.AddRange(Generator.OwnPerLineNul());
        return samples;
    }

    private static string ToHex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

    /// <summary>
    /// 調査文書 1 章の囲みの規則を、調査の評価（CutThreshold.cs の Counts / Rule）と同じく浮動小数点で書いたもの
    /// </summary>
    private static class Model
    {
        private static readonly LineBreakUnit[] Units =
        {
            LineBreakUnit.Utf16Le, LineBreakUnit.Utf16Be, LineBreakUnit.Utf32Le, LineBreakUnit.Utf32Be,
        };

        public static LineBreakUnit Decide(byte[] bytes, bool applyWholeFileTreatment)
        {
            int w = Math.Min(bytes.Length, Window);
            var n = new int[4];
            var a = new int[4];
            var b = new int[4];
            var z = new int[4];
            var abr = new int[4];
            int printable = 0;
            bool hasZero = false;

            for (int i = 0; i < w; i++)
            {
                hasZero |= bytes[i] == 0;
            }

            for (int i = 0; i + 2 <= w; i += 2)
            {
                byte b0 = bytes[i], b1 = bytes[i + 1];
                n[0]++;
                n[1]++;
                if (b0 >= 0x20 && b0 <= 0x7E && b1 >= 0x20 && b1 <= 0x7E) printable++;
                if (b0 == 0 && b1 == 0) { z[0]++; z[1]++; }
                else if (b1 == 0) { a[0]++; b[1]++; if (b0 == 0x0A || b0 == 0x0D) abr[0]++; }
                else if (b0 == 0) { b[0]++; a[1]++; if (b1 == 0x0A || b1 == 0x0D) abr[1]++; }
            }

            for (int i = 0; i + 4 <= w; i += 4)
            {
                bool low0 = bytes[i] == 0 && bytes[i + 1] == 0;
                bool high0 = bytes[i + 2] == 0 && bytes[i + 3] == 0;
                n[2]++;
                n[3]++;
                if (low0 && high0) { z[2]++; z[3]++; continue; }
                if (high0) { a[2]++; b[3]++; }
                if (low0) { b[2]++; a[3]++; }
            }

            bool Common(int t)
            {
                if (n[t] == 0) return false;
                int minA = Math.Min(16, Math.Max(1, (int)Math.Ceiling(0.6 * n[t])));
                if (a[t] < minA || z[t] != 0) return false;
                return t >= 2 || printable <= 0.7 * n[t];
            }

            bool Ratio(int t) => b[t] <= 0.2 * a[t];
            bool Direction(int t) => abr[t] >= 4 && abr[t ^ 1] <= 0.25 * abr[t];

            foreach (int t in new[] { 2, 3 })
            {
                if (Common(t) && Ratio(t)) return Units[t];
            }

            foreach (int t in new[] { 0, 1 })
            {
                if (Common(t) && Direction(t)) return Units[t];
            }

            foreach (int t in new[] { 0, 1 })
            {
                if (Common(t) && Ratio(t)) return Units[t];
            }

            if (applyWholeFileTreatment && hasZero && bytes.Length <= Window)
            {
                return LineBreakUnit.None;
            }

            return LineBreakUnit.SingleByte;
        }
    }
}
