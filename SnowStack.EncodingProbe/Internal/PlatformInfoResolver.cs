using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SnowStack.EncodingProbe;

/// <summary>
/// PlatformInformation を収集する内部ロジック
/// </summary>
/// <remarks>
/// ファイル1件ごとの判定処理（<see cref="EncodingDetector"/>）が使う軽量なOS判定は
/// 従来どおり <see cref="PlatformInfo"/> を直接参照する。このクラスは
/// Get-EncodingProbePlatformInfo 用に実行環境情報一式を組み立てるためだけに使う。
/// </remarks>
internal static class PlatformInfoResolver
{
    public static PlatformInformation Resolve()
    {
        return new PlatformInformation
        {
            Os = ResolveOs(),
            Runtime = ResolveRuntime(),
            Locale = ResolveLocale(),
        };
    }

    private static OsInformation ResolveOs() => new()
    {
        IsWindows = PlatformInfo.IsWindows,
        IsMacOs = PlatformInfo.IsMacOs,
        IsLinux = PlatformInfo.IsLinux,
        // .NET Framework の OSDescription は末尾に空白が付く（"Microsoft Windows 10.0.26300 "）。
        // .NET Core と同じ文字列にするため、前後の空白を除く（1.3.0。BUG-0008）
        Description = RuntimeInformation.OSDescription.Trim(),
    };

    private static DotNetRuntimeInformation ResolveRuntime()
    {
        string description = RuntimeInformation.FrameworkDescription;

        return new DotNetRuntimeInformation
        {
            FrameworkDescription = description,
            IsDotNetFramework = description.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase),
            IsCodePagesEncodingProviderRegistered = IsCodePagesProviderRegistered(),
        };
    }

    private static bool IsCodePagesProviderRegistered()
    {
        // .NET Framework は追加コードページを標準サポートしているため常にtrue
        if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // .NET (Core/5+) では、Shift-JIS(932)取得可否で登録状態を判定する
        try
        {
            _ = System.Text.Encoding.GetEncoding(932);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static LocaleInformation ResolveLocale()
    {
        var culture = CultureInfo.CurrentCulture;
        int ansi = -1;
        int oem = -1;

        try
        {
            ansi = culture.TextInfo.ANSICodePage;
            oem = culture.TextInfo.OEMCodePage;
        }
        catch (NotSupportedException)
        {
            // 非Windows環境等、取得できない場合は-1のまま
        }

        return new LocaleInformation
        {
            CurrentCulture = culture.Name,
            AnsiCodePage = ansi,
            OemCodePage = oem,
        };
    }
}
