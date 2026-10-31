using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// 中身がすべて 0x00 の、大きな一時ファイルを NTFS のスパースファイルとして作る。
/// </summary>
/// <remarks>
/// 判定の上限（2 GB）を超えるファイルの試験に使う。スパースファイルにするとディスクをほとんど使わない。
/// スパースにできないファイルシステムでは、通常のファイルとして長さを伸ばす（ディスクを消費する）。
/// </remarks>
internal sealed class SparseFile : IDisposable
{
    private const uint FsctlSetSparse = 0x000900C4;

    private SparseFile(string path)
    {
        this.Path = path;
    }

    /// <summary>生成されたファイルの絶対パス</summary>
    public string Path { get; }

    /// <summary>
    /// 指定した長さのスパースファイルを作る
    /// </summary>
    public static SparseFile Create(long length)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "EncodingProbeTests_" + Guid.NewGuid().ToString("N") + ".bin");

        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
        {
            DeviceIoControl(stream.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            stream.SetLength(length);
        }

        return new SparseFile(path);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(this.Path);
        }
        catch (IOException)
        {
            // テストの後始末であり、削除できなくてもテスト結果には影響しない
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
