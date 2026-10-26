using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SnowStack.EncodingProbe;

/// <summary>
/// 実行環境の ANSI / OEM コードページを求める（1.3.0 第六次。BUG-0032）。
/// </summary>
/// <remarks>
/// Windows では、システムのロケール（「Unicode 対応ではないプログラムの言語」）のコードページを
/// <c>GetACP</c> / <c>GetOEMCP</c> で求める。標準の 7.x の <c>-Encoding ansi</c> / <c>oem</c>、
/// 5.1 の <c>Default</c> / <c>Oem</c> と同じ値であり、スレッドのカルチャー（<see cref="CultureInfo.CurrentCulture"/>）には従わない。
/// 第五次まではスレッドのカルチャーの <see cref="TextInfo.ANSICodePage"/> を使っていたため、
/// 「地域」の形式をシステムのロケールと違う値にしている環境で、標準と違うコードページになっていた。
/// <br/>
/// Windows 以外では今までどおり、現在のカルチャーの <see cref="TextInfo"/> の値を返す（.NET が持つカルチャーの表の値。
/// 現在のカルチャーは環境変数 LANG などから決まる）。取得できない場合は -1。
/// <br/>
/// Get-EncodingProbePlatformInfo の Locale と、PowerShell モジュールの ansi / oem の語彙が、この同じ値を使う。
/// </remarks>
internal static class SystemCodePages
{
    /// <summary>ANSI コードページ。取得できない場合は -1。</summary>
    public static int GetAnsiCodePage()
    {
        if (PlatformInfo.IsWindows)
        {
            return (int)NativeMethods.GetACP();
        }

        return FromCurrentCulture(static textInfo => textInfo.ANSICodePage);
    }

    /// <summary>OEM コードページ。取得できない場合は -1。</summary>
    public static int GetOemCodePage()
    {
        if (PlatformInfo.IsWindows)
        {
            return (int)NativeMethods.GetOEMCP();
        }

        return FromCurrentCulture(static textInfo => textInfo.OEMCodePage);
    }

    private static int FromCurrentCulture(Func<TextInfo, int> select)
    {
        try
        {
            return select(CultureInfo.CurrentCulture.TextInfo);
        }
        catch (NotSupportedException)
        {
            return -1;
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern uint GetACP();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern uint GetOEMCP();
    }
}
