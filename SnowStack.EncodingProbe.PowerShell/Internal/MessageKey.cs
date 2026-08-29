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

        /// <summary>判定できるサイズを超えるファイルが指定された</summary>
        FileTooLargeToDetect,

        /// <summary>-Raw と -TotalCount が同時に指定された</summary>
        RawAndTotalCountAreExclusive,

        /// <summary>指定されたファイルが存在しない</summary>
        FileNotFound,

        /// <summary>指定されたパスがファイルではない</summary>
        PathIsNotFile,

        /// <summary>読み取り中のファイルが書き込み先に指定された</summary>
        SamePathRoundTrip,

        /// <summary>-Encoding と -EncodingFrom が同時に指定された</summary>
        EncodingAndEncodingFromAreExclusive,

        /// <summary>-Encoding Auto の継承元となるファイルが存在しない</summary>
        AutoEncodingRequiresExistingFile,

        /// <summary>-NoNewline と -LineBreak が同時に指定された（警告）</summary>
        NoNewlineIgnoresLineBreak,

        /// <summary>追記により既存ファイルの文字エンコーディングが変わってしまう</summary>
        EncodingChangeOnAppend,

        /// <summary>判定はできたが、実行環境がそのコードページを提供していない</summary>
        DetectedCodePageNotAvailable,

        /// <summary>-Culture に解釈できないカルチャー名が指定された</summary>
        InvalidCulture,

        /// <summary>-Strategy に解釈できない判定方式が指定された</summary>
        InvalidStrategy,
    }
}