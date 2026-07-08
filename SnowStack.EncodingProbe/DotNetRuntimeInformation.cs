namespace SnowStack.EncodingProbe;

/// <summary>
/// 実行中の.NETランタイムに関する情報
/// </summary>
/// <remarks>
/// 型名は <see cref="System.Runtime.InteropServices.RuntimeInformation"/> との名前衝突を避けるため
/// <c>DotNetRuntimeInformation</c> としている。
/// </remarks>
public sealed record DotNetRuntimeInformation
{
    /// <summary>
    /// フレームワークの説明文字列
    /// 例: ".NET 10.0.1"、".NET Framework 4.8.9037.0"
    /// </summary>
    public string FrameworkDescription { get; internal set; } = string.Empty;

    /// <summary>.NET Framework(4.8等)上で動作しているかどうか</summary>
    public bool IsDotNetFramework { get; internal set; }

    /// <summary>
    /// System.Text.Encoding.CodePages の追加コードページプロバイダーが
    /// 登録済み(利用可能)かどうか。
    /// trueの場合、Shift-JIS(932)やEUC-JP(51932)等の
    /// 追加コードページを Encoding.GetEncoding(int) で取得できる。
    /// </summary>
    public bool IsCodePagesEncodingProviderRegistered { get; internal set; }
}
