using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// テストでシンボリックリンク・ジャンクション・ハードリンクを作る（1.3.0 第五次の後半）。
/// </summary>
/// <remarks>
/// シンボリックリンクは、開発者モードか管理者の権限が無いと作れない。作れない環境では false を返し、
/// 呼び出し側のテストはその場合に何も確かめずに終わる（xUnit 2 に実行時のスキップが無いため）。
/// ジャンクションとハードリンクは権限なしで作れる。
/// </remarks>
internal static class FileLinks
{
    /// <summary>ファイルのシンボリックリンクを作る。<paramref name="target"/> は相対パスでもよい（リンクのフォルダーが基準）。</summary>
    public static bool TryCreateFileSymbolicLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>フォルダーのシンボリックリンクを作る</summary>
    public static bool TryCreateDirectorySymbolicLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>ジャンクションを作る（Windows のみ）</summary>
    public static bool TryCreateJunction(string link, string target)
        => RunCmd($"mklink /J \"{link}\" \"{target}\"") && Directory.Exists(link);

    /// <summary>ハードリンクを作る（Windows のみ）</summary>
    public static bool TryCreateHardLink(string link, string existing)
        => OperatingSystem.IsWindows() && CreateHardLinkW(link, existing, IntPtr.Zero);

    /// <summary>UNC の管理共有（\\localhost\C$\…）の書き方。使えなければ null。</summary>
    public static string? ToAdminShare(string fullPath)
    {
        if (!OperatingSystem.IsWindows() || fullPath.Length < 3 || fullPath[1] != ':')
        {
            return null;
        }

        string unc = @"\\localhost\" + fullPath[0] + "$" + fullPath.Substring(2);

        try
        {
            return File.Exists(unc) || Directory.Exists(unc) ? unc : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool RunCmd(string arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var info = new ProcessStartInfo("cmd.exe", "/c " + arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using Process? process = Process.Start(info);

        if (process == null)
        {
            return false;
        }

        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
