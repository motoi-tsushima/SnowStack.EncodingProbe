namespace SnowStack.EncodingProbe;

internal static class PlatformInfo
{
#if NET5_0_OR_GREATER
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMacOs => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();
#else
    // .NET Framework 4.8 は Windows 専用ランタイムのため固定
    public static bool IsWindows => true;
    public static bool IsMacOs   => false;
    public static bool IsLinux   => false;
#endif
}
