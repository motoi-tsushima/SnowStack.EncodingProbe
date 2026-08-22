using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// バイト列を明示して一時ファイルを生成するテスト用ヘルパー。
/// </summary>
/// <remarks>
/// テストファイルを本モジュールの書き込みコードで作ると、書き込み側にバグがあっても
/// 読み取り側と辻褄が合ってしまい検出できない。そのため、テストデータは
/// バイト列を直接指定するか、.NET の Encoding で組み立てる。
/// </remarks>
internal sealed class ByteExactFile : IDisposable
{
    /// <summary>UTF-8 の BOM</summary>
    public static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>UTF-16 リトルエンディアンの BOM</summary>
    public static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };

    /// <summary>UTF-16 ビッグエンディアンの BOM</summary>
    public static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };

    /// <summary>UTF-32 リトルエンディアンの BOM</summary>
    public static readonly byte[] Utf32LeBom = { 0xFF, 0xFE, 0x00, 0x00 };

    /// <summary>UTF-32 ビッグエンディアンの BOM</summary>
    public static readonly byte[] Utf32BeBom = { 0x00, 0x00, 0xFE, 0xFF };

    private ByteExactFile(string path)
    {
        this.Path = path;
    }

    /// <summary>生成されたファイルの絶対パス</summary>
    public string Path { get; }

    /// <summary>
    /// バイト列を指定してファイルを生成する
    /// </summary>
    public static ByteExactFile Create(params byte[] bytes)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "EncodingProbeTests_" + Guid.NewGuid().ToString("N") + ".txt");

        File.WriteAllBytes(path, bytes);
        return new ByteExactFile(path);
    }

    /// <summary>
    /// 複数のバイト列を連結してファイルを生成する（BOM と本文の組み立てに使う）
    /// </summary>
    public static ByteExactFile CreateFrom(params byte[][] segments) => Create(Concat(segments));

    /// <summary>
    /// 内容を持たない（まだ存在しない）一時ファイルのパスを確保する
    /// </summary>
    public static ByteExactFile CreateMissing()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "EncodingProbeTests_" + Guid.NewGuid().ToString("N") + ".txt");

        return new ByteExactFile(path);
    }

    /// <summary>
    /// 複数のバイト列を連結する
    /// </summary>
    public static byte[] Concat(params byte[][] segments)
    {
        var result = new List<byte>();

        foreach (byte[] segment in segments)
        {
            result.AddRange(segment);
        }

        return result.ToArray();
    }

    /// <summary>
    /// .NET の Encoding で本文を符号化する。BOM は付与しない。
    /// </summary>
    public static byte[] Encode(Encoding encoding, string text) => encoding.GetBytes(text);

    /// <summary>ファイルの現在の内容をバイト列として読み出す</summary>
    public byte[] ReadBytes() => File.ReadAllBytes(this.Path);

    /// <summary>ファイルが存在するかどうか</summary>
    public bool Exists => File.Exists(this.Path);

    /// <summary>一時ファイルを削除する</summary>
    public void Dispose()
    {
        try
        {
            if (File.Exists(this.Path))
            {
                File.Delete(this.Path);
            }
        }
        catch (IOException)
        {
            // テストの後始末であり、削除できなくてもテスト結果には影響しない
        }
    }
}