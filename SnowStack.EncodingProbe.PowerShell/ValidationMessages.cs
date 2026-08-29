using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell
{
    /// <summary>
    /// 入力の検証に関連するメッセージを定義するクラス
    /// </summary>
    /// <remarks>
    /// 1.1.0 で追加したコマンドのメッセージは <see cref="MessageCatalog"/> によって
    /// 実行環境の UI カルチャーに応じた言語で返される。
    /// </remarks>
    internal static class ValidationMessages
    {
        /// <summary>
        /// 無効なカルチャー情報が指定された場合のエラーメッセージ
        /// </summary>
        public const string InvalidCultureInfo = "Invalid culture info: {0}";

        /// <summary>
        /// 語彙として解決できない文字エンコーディングが指定された場合のエラーメッセージ
        /// </summary>
        public static string UnknownEncoding(string name)
            => MessageCatalog.Format(MessageKey.UnknownEncoding, name);

        /// <summary>
        /// 文字エンコーディングに null が指定された場合のエラーメッセージ
        /// </summary>
        public static string NullEncoding()
            => MessageCatalog.Get(MessageKey.NullEncoding);

        /// <summary>
        /// 書き込み系コマンドで、BOM方針が定まらないUTF-8が指定された場合のエラーメッセージ。
        /// 裸の utf8 のほか、WebName の "utf-8" や数値コードページの 65001 もこれに該当する。
        /// </summary>
        public static string BareUtf8NotAllowedForWrite(string specified)
            => MessageCatalog.Format(MessageKey.BareUtf8NotAllowedForWrite, specified);

        /// <summary>
        /// 書き込み系コマンドで、BOM方針が定まらないUnicode系が指定された場合のエラーメッセージ
        /// </summary>
        public static string BomPolicyUnspecifiedForWrite(string specified, string noBomName, string bomName)
            => MessageCatalog.Format(MessageKey.BomPolicyUnspecifiedForWrite, specified, noBomName, bomName);

        /// <summary>
        /// 書き込み系コマンドで utf7 が指定された場合のエラーメッセージ
        /// </summary>
        public static string Utf7NotAllowedForWrite()
            => MessageCatalog.Get(MessageKey.Utf7NotAllowedForWrite);

        /// <summary>
        /// BOM接尾辞を許さない語彙に接尾辞が付けられた場合のエラーメッセージ
        /// </summary>
        public static string BomSuffixNotAllowed(string vocabulary)
            => MessageCatalog.Format(MessageKey.BomSuffixNotAllowed, vocabulary);

        /// <summary>
        /// ConvertTo-DotNetEncoding に Auto が指定された場合のエラーメッセージ
        /// </summary>
        public static string AutoNotAllowedForConvert()
            => MessageCatalog.Get(MessageKey.AutoNotAllowedForConvert);

        /// <summary>
        /// ANSI / OEM コードページを取得できない環境で ansi / oem が指定された場合のエラーメッセージ
        /// </summary>
        public static string CodePageNotAvailable(string vocabulary)
            => MessageCatalog.Format(MessageKey.CodePageNotAvailable, vocabulary);

        /// <summary>
        /// 判定に失敗した EncodingInformation が渡された場合のエラーメッセージ
        /// </summary>
        public static string UndetectedEncodingInformation(int codePage)
            => MessageCatalog.Format(MessageKey.UndetectedEncodingInformation, codePage);

        /// <summary>
        /// 対象ファイルの文字エンコーディングを判定できなかった場合のエラーメッセージ
        /// </summary>
        public static string DetectionFailed(string path)
            => MessageCatalog.Format(MessageKey.DetectionFailed, path);

        /// <summary>
        /// 判定できるサイズを超えるファイルが指定された場合のエラーメッセージ
        /// </summary>
        public static string FileTooLargeToDetect(string path)
            => MessageCatalog.Format(MessageKey.FileTooLargeToDetect, path);

        /// <summary>
        /// -Raw と -TotalCount が同時に指定された場合のエラーメッセージ
        /// </summary>
        public static string RawAndTotalCountAreExclusive()
            => MessageCatalog.Get(MessageKey.RawAndTotalCountAreExclusive);

        /// <summary>
        /// 指定されたファイルが存在しない場合のエラーメッセージ
        /// </summary>
        public static string FileNotFound(string path)
            => MessageCatalog.Format(MessageKey.FileNotFound, path);

        /// <summary>
        /// 指定されたパスがファイルではない場合のエラーメッセージ
        /// </summary>
        public static string PathIsNotFile(string path)
            => MessageCatalog.Format(MessageKey.PathIsNotFile, path);

        /// <summary>
        /// 読み取り中のファイルが書き込み先に指定された場合のエラーメッセージ
        /// </summary>
        public static string SamePathRoundTrip(string path)
            => MessageCatalog.Format(MessageKey.SamePathRoundTrip, path);

        /// <summary>
        /// -Encoding と -EncodingFrom が同時に指定された場合のエラーメッセージ
        /// </summary>
        public static string EncodingAndEncodingFromAreExclusive()
            => MessageCatalog.Get(MessageKey.EncodingAndEncodingFromAreExclusive);

        /// <summary>
        /// -Encoding Auto の継承元となるファイルが存在しない場合のエラーメッセージ
        /// </summary>
        public static string AutoEncodingRequiresExistingFile(string path)
            => MessageCatalog.Format(MessageKey.AutoEncodingRequiresExistingFile, path);

        /// <summary>
        /// -NoNewline と -LineBreak が同時に指定された場合の警告メッセージ
        /// </summary>
        public static string NoNewlineIgnoresLineBreak()
            => MessageCatalog.Get(MessageKey.NoNewlineIgnoresLineBreak);

        /// <summary>
        /// 追記により既存ファイルの文字エンコーディングが変わってしまう場合のエラーメッセージ
        /// </summary>
        public static string EncodingChangeOnAppend(string path, string specified, string existing)
            => MessageCatalog.Format(MessageKey.EncodingChangeOnAppend, path, specified, existing);

        /// <summary>
        /// 判定はできたが、実行環境がそのコードページを提供していない場合のエラーメッセージ
        /// </summary>
        public static string DetectedCodePageNotAvailable(int codePage, string webName)
            => MessageCatalog.Format(MessageKey.DetectedCodePageNotAvailable, codePage, webName);

        /// <summary>
        /// -Culture に解釈できないカルチャー名が指定された場合のエラーメッセージ
        /// </summary>
        public static string InvalidCulture(string culture)
            => MessageCatalog.Format(MessageKey.InvalidCulture, culture);

        /// <summary>
        /// -Strategy に解釈できない判定方式が指定された場合のエラーメッセージ
        /// </summary>
        public static string InvalidStrategy(string strategy)
            => MessageCatalog.Format(MessageKey.InvalidStrategy, strategy);
    }
}