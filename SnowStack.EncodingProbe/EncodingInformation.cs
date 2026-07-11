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

    /// <summary>
    /// PowerShell -Encoding用のエンコーディング名。
    /// net10.0ビルド（PS6.2+向け）では登録済みフレンドリ名（"utf8BOM"等）または
    /// フレンドリ名が存在しない場合はWebName。
    /// net48ビルド（PS5.1向け）では、PS5.1の固定-Encoding列挙値一覧に一致する場合のみ
    /// その値（"Ascii"等）を返し、一致しない場合はnullになる。
    /// </summary>
    public string PSEncodingName { get; internal set; }

    /// <summary>
    /// PSEncodingName をそのまま PowerShell の -Encoding パラメータに渡してよいかどうか。
    /// net10.0ビルド（PS6.2+向け）では、PSEncodingName が PowerShell 6.2+ の登録済み
    /// フレンドリ名（"utf8BOM" 等）である場合は true。WebName（.NETのEncodingWebNameと
    /// 同じ値）の場合は false となり、-Encoding に渡すには PowerShell 6.2 以降の
    /// 数値コードページ対応が必要。
    /// net48ビルド（PS5.1向け）では、PSEncodingName が非null（＝固定名一覧に一致）の場合のみ true。
    /// </summary>
    public bool UsePSName { get; internal set; }

    /// <summary>BOMの有無</summary>
    public bool Bom { get; internal set; }

    /// <summary>改行コードの種類</summary>
    public LineBreakType LineBreak { get; internal set; } = LineBreakType.None;

    /// <summary>実行中のカルチャー名</summary>
    public string? Culture { get; internal set; } = null;
}

