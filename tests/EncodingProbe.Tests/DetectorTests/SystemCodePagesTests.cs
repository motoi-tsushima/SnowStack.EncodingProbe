using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests;

/// <summary>
/// Get-EncodingProbePlatformInfo の Locale の AnsiCodePage / OemCodePage が、スレッドのカルチャーではなく
/// システムのロケールの値（GetACP / GetOEMCP）になることを検証する（1.3.0 第六次。BUG-0032）。両 TFM で同じ。
/// </summary>
public class SystemCodePagesTests
{
    [Theory]
    [InlineData("ja-JP")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    [InlineData("tr-TR")]
    [InlineData("")]
    public void Locale_CodePages_AreSystemValues_RegardlessOfThreadCulture(string culture)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        CultureInfo old = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            LocaleInformation locale = PlatformInfoResolver.Resolve().Locale;

            Assert.Equal((int)GetACP(), locale.AnsiCodePage);
            Assert.Equal((int)GetOEMCP(), locale.OemCodePage);
            Assert.Equal(SystemCodePages.GetAnsiCodePage(), locale.AnsiCodePage);
            Assert.Equal(SystemCodePages.GetOemCodePage(), locale.OemCodePage);

            // カルチャー名はスレッドのカルチャーのまま
            Assert.Equal(culture, locale.CurrentCulture);
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
