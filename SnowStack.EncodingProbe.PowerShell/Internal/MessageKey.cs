namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// ローカライズ対象メッセージの識別子
    /// </summary>
    internal enum MessageKey
    {
        /// <summary>語彙として解決できない文字エンコーディングが指定された</summary>
        UnknownEncoding,

        /// <summary>文字エンコーディングに null が指定された</summary>
        NullEncoding,

        /// <summary>書き込みで、BOM方針が定まらないUTF-8が指定された</summary>
        BareUtf8NotAllowedForWrite,

        /// <summary>書き込みで、BOM方針が定まらないUnicode系が指定された</summary>
        BomPolicyUnspecifiedForWrite,

        /// <summary>書き込みで UTF-7 が指定された</summary>
        Utf7NotAllowedForWrite,

        /// <summary>BOM接尾辞を許さない語彙に接尾辞が付けられた</summary>
        BomSuffixNotAllowed,

        /// <summary>ConvertTo-DotNetEncoding に Auto が指定された</summary>
        AutoNotAllowedForConvert,

        /// <summary>ansi / oem に対応するコードページを取得できない</summary>
        CodePageNotAvailable,

        /// <summary>判定に失敗した EncodingInformation が渡された</summary>
        UndetectedEncodingInformation,

        /// <summary>対象ファイルの文字エンコーディングを判定できなかった</summary>
        DetectionFailed,

        /// <summary>-Raw と -TotalCount が同時に指定された</summary>
        RawAndTotalCountAreExclusive,

        /// <summary>指定されたファイルが存在しない</summary>
        FileNotFound,

        /// <summary>指定されたパスがファイルではない</summary>
        PathIsNotFile,

        /// <summary>読み取り中のファイルが書き込み先に指定された</summary>
        SamePathRoundTrip,
    }
}