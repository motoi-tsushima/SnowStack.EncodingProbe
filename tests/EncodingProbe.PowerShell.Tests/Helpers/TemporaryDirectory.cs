using System;
using System.IO;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// テストごとの一時フォルダー。破棄すると中身ごと削除する。
/// </summary>
/// <remarks>
/// ワイルドカードの解決や -Destination のように、フォルダー内のファイルの顔ぶれが結果を左右する
/// テストで使う。<see cref="ByteExactFile"/> と同じく、ファイルの内容はバイト列を明示して作る。
/// </remarks>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        this.Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "EncodingProbeTests_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(this.Path);
    }

    /// <summary>フォルダーの絶対パス</summary>
    public string Path { get; }

    /// <summary>フォルダー内のパスを組み立てる（ファイルは作らない）</summary>
    public string Combine(string name) => System.IO.Path.Combine(this.Path, name);

    /// <summary>バイト列を指定してファイルを作り、その絶対パスを返す</summary>
    public string Write(string name, byte[] bytes)
    {
        string path = Combine(name);
        string? parent = System.IO.Path.GetDirectoryName(path);

        if (parent != null)
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>フォルダーを中身ごと削除する（読み取り専用のファイルも外して消す）</summary>
    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.GetFiles(this.Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(this.Path, recursive: true);
        }
        catch (IOException)
        {
            // テストの後始末であり、削除できなくてもテスト結果には影響しない
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }
}
