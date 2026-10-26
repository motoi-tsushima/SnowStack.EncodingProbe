using System;
using System.Globalization;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Threading;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// ansi / oem の語彙が、スレッドのカルチャーではなくシステムのロケールのコードページになることを検証する（1.3.0 第六次。BUG-0032）。
/// </summary>
/// <remarks>
/// 期待値は GetACP / GetOEMCP を直接呼んで求める（標準の 7.x の -Encoding ansi・5.1 の Default と同じ値）。
/// テストは Windows でだけ意味を持つ（Windows 以外は今までどおりカルチャーの値）。
/// </remarks>
public class SystemCodePageVocabularyTests
{
    [Theory]
    [InlineData("ja-JP")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    [InlineData("ko-KR")]
    [InlineData("ar-SA")]
    [InlineData("")]
    public void AnsiAndOem_DoNotFollowThreadCulture(string culture)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        int expectedAnsi = (int)GetACP();
        int expectedOem = (int)GetOEMCP();

        WithCulture(culture, () =>
        {
            Assert.Equal(expectedAnsi, Resolve("ansi").Encoding!.CodePage);
            Assert.Equal(expectedOem, Resolve("oem").Encoding!.CodePage);
        });
    }

    /// <summary>
    /// ansi / oem は BOM を持たない（システムの ANSI が 65001 でも BOM 無しの UTF-8 になるよう、BOM 方針を常に false にする）。
    /// </summary>
    [Fact]
    public void AnsiAndOem_HaveNoBom()
    {
        foreach (string name in new[] { "ansi", "oem" })
        {
            EncodingSpec spec = Resolve(name);

            Assert.False(spec.EmitBom);
            Assert.Empty(spec.Encoding!.GetPreamble());
        }
    }

    /// <summary>
    /// コードページ 0 は語彙に無いので、使えないコードページ（1・2・3 など）と同じエラーで拒否する。
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData(0)]
    [InlineData("1")]
    [InlineData(2)]
    public void CodePageZero_IsRejectedLikeUnavailableCodePages(object value)
    {
        foreach (EncodingUsage usage in new[] { EncodingUsage.Read, EncodingUsage.Write })
        {
            var exception = Assert.Throws<ArgumentTransformationMetadataException>(
                () => EncodingVocabulary.Resolve(value, usage));

            Assert.Equal(ValidationMessages.UnknownEncoding(value.ToString()!), exception.Message);
        }
    }

    private static EncodingSpec Resolve(string name) => EncodingVocabulary.Resolve(name, EncodingUsage.Read);

    private static void WithCulture(string name, Action action)
    {
        CultureInfo old = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(name);
            action();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();
}
