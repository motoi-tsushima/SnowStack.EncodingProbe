namespace SnowStack.EncodingProbe;

/// <summary>
/// 文字エンコーディング判定情報
/// </summary>
public sealed record EncodingInformation
{
    /// <summary>コードページ</summary>
    public int CodePage { get; internal set; }

    /// <summary>エンコーディング名</summary>
    public string EncodingWebName { get; internal set; }

    /// <summary>PowerShell -Encoding用のエンコーディング名</summary>
    public string PSEncodingName { get; internal set; }

    /// <summary>
    /// PSEncodingName をそのまま PowerShell の -Encoding パラメータに渡してよいかどうか。
    /// PSEncodingName が PowerShell 6.2+ の登録済みフレンドリ名（"utf8BOM" 等）である場合は true。
    /// PSEncodingName が数値コードページの文字列（例: "932"）の場合は false となり、
    /// -Encoding に渡すには PowerShell 6.2 以降の数値コードページ対応が必要。
    /// </summary>
    public bool UsePSName { get; internal set; }

    /// <summary>BOMの有無</summary>
    public bool Bom { get; internal set; }

    /// <summary>改行コードの種類</summary>
    public LineBreakType LineBreak { get; internal set; } = LineBreakType.None;

    /// <summary>実行中のカルチャー名</summary>
    public string? Culture { get; internal set; } = null;
}

