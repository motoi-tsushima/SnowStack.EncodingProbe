namespace SnowStack.EncodingProbe;

/// <summary>
/// 実行中のロケール・コードページに関する情報
/// </summary>
public sealed record LocaleInformation
{
    /// <summary>現在のカルチャー名 例: "ja-JP"</summary>
    public string CurrentCulture { get; internal set; } = string.Empty;

    /// <summary>
    /// システムのANSIコードページ(Windows PowerShellの-Encoding "Default"相当)。
    /// 非Windows環境や取得不可の場合は -1。
    /// </summary>
    public int AnsiCodePage { get; internal set; } = -1;

    /// <summary>
    /// システムのOEMコードページ(Windows PowerShellの-Encoding "Oem"相当)。
    /// 非Windows環境や取得不可の場合は -1。
    /// </summary>
    public int OemCodePage { get; internal set; } = -1;

    /// <summary>
    /// ANSIとOEMが同一コードページかどうか。
    /// CJK圏(日本語/韓国語/中国語)ではtrueになりやすく、
    /// 西欧圏(英/独/仏)では通常falseになる。
    /// </summary>
    public bool IsAnsiOemSame => AnsiCodePage != -1 && AnsiCodePage == OemCodePage;
}
