using System.IO;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.FileSystemTests;

/// <summary>
/// 読み書きの失敗を 5 言語のメッセージに包み直す部品（1.3.0 第六次 B・C）。
/// </summary>
/// <remarks>
/// ディスクの空き不足などはテストで起こせないので、HRESULT を指定した IOException を直接渡して確かめる。
/// 包み直した例外は内部例外を持たず、HRESULT を引き継ぐ。
/// </remarks>
public class FileErrorsTests
{
    private const string Path = @"C:\work\a.txt";

    /// <summary>
    /// 共有違反・ロック違反以外の書き込みの失敗は、エラー コードを示す（第六次 C。第六次 B までは .NET の例外のままだった）
    /// </summary>
    [Theory]
    [InlineData(unchecked((int)0x80070070), "0x80070070")]   // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027), "0x80070027")]   // ERROR_HANDLE_DISK_FULL
    [InlineData(unchecked((int)0x8007045D), "0x8007045D")]   // ERROR_IO_DEVICE
    public void WriteFailed_OtherThanInUse_ShowsErrorCode(int hresult, string code)
    {
        IOException wrapped = FileErrors.WriteFailed(Path, new IOException("There is not enough space on the disk.", hresult));

        Assert.Equal(ValidationMessages.FileWriteFailed(Path, code), wrapped.Message);
        Assert.Equal(hresult, wrapped.HResult);
        Assert.Null(wrapped.InnerException);
    }

    /// <summary>
    /// 共有違反・ロック違反は、今までどおり「ほかのプログラムが使用中」
    /// </summary>
    [Theory]
    [InlineData(unchecked((int)0x80070020))]
    [InlineData(unchecked((int)0x80070021))]
    public void WriteFailed_InUse_IsFileInUseForWrite(int hresult)
    {
        IOException wrapped = FileErrors.WriteFailed(Path, new IOException("in use", hresult));

        Assert.Equal(ValidationMessages.FileInUseForWrite(Path), wrapped.Message);
        Assert.Equal(hresult, wrapped.HResult);
        Assert.Null(wrapped.InnerException);
    }

    /// <summary>
    /// すでに包み直した例外（取り消しの書き戻しに失敗した場合など）は、そのまま返す
    /// </summary>
    [Fact]
    public void WriteFailed_AlreadyLocalized_IsReturnedAsIs()
    {
        IOException first = FileErrors.WriteFailed(Path, new IOException("x", unchecked((int)0x80070070)));

        Assert.Same(first, FileErrors.WriteFailed(Path, first));
    }

    /// <summary>
    /// 読み込みの失敗も同じ形（第六次 B）
    /// </summary>
    [Fact]
    public void ReadFailed_OtherThanInUse_ShowsErrorCode()
    {
        IOException wrapped = FileErrors.ReadFailed(Path, new IOException("x", unchecked((int)0x8007045D)));

        Assert.Equal(ValidationMessages.FileReadFailed(Path, "0x8007045D"), wrapped.Message);
        Assert.Null(wrapped.InnerException);
    }
}
