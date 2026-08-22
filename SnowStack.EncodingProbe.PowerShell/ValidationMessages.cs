namespace SnowStack.EncodingProbe.PowerShell
{
    /// <summary>
    /// 入力の検証に関連する定数メッセージを定義するクラス
    /// </summary>
    internal static class ValidationMessages
    {
        /// <summary>
        /// 無効なカルチャー情報が指定された場合のエラーメッセージ
        /// </summary>
        public const string InvalidCultureInfo = "Invalid culture info: {0}";

        /// <summary>
        /// 語彙として解決できない文字エンコーディングが指定された場合のエラーメッセージ
        /// </summary>
        public const string UnknownEncoding =
            "文字エンコーディング '{0}' を解決できませんでした。統一語彙名（utf8NoBOM 等）、"
            + "WebName（shift_jis 等）、コードページ数値（932 等）のいずれかを指定してください。";

        /// <summary>
        /// -Encoding に null が指定された場合のエラーメッセージ
        /// </summary>
        public const string NullEncoding = "文字エンコーディングに null は指定できません。";

        /// <summary>
        /// 書き込み系コマンドで、BOM方針が定まらないUTF-8が指定された場合のエラーメッセージ。
        /// 裸の utf8 のほか、WebName の "utf-8" や数値コードページの 65001 もこれに該当する。
        /// </summary>
        public const string BareUtf8NotAllowedForWrite =
            "書き込みでは '{0}' を指定できません。UTF-8 は PowerShell 5.1 と 7.x で BOM の解釈が異なるため、"
            + "'utf8NoBOM' または 'utf8BOM' のいずれかを明示してください。";

        /// <summary>
        /// 書き込み系コマンドで、BOM方針が定まらない指定がされた場合のエラーメッセージ
        /// </summary>
        public const string BomPolicyUnspecifiedForWrite =
            "'{0}' は BOM の有無が定まらないため、書き込みでは指定できません。"
            + "'{1}' または '{2}' のように BOM の有無を明示した名前を指定してください。";

        /// <summary>
        /// 書き込み系コマンドで utf7 が指定された場合のエラーメッセージ
        /// </summary>
        public const string Utf7NotAllowedForWrite =
            "書き込みでは UTF-7 を指定できません。UTF-7 は読み取りのみ対応しています。";

        /// <summary>
        /// BOM接尾辞を許さない語彙に接尾辞が付けられた場合のエラーメッセージ
        /// </summary>
        public const string BomSuffixNotAllowed =
            "'{0}' に BOM 接尾辞は指定できません。BOM 接尾辞を指定できるのは "
            + "utf8 / unicode / bigendianunicode / utf32 / bigendianutf32 の 5 系統のみです。";

        /// <summary>
        /// ConvertTo-DotNetEncoding に Auto が指定された場合のエラーメッセージ
        /// </summary>
        public const string AutoNotAllowedForConvert =
            "Auto はファイルからの検出を指す語彙のため、ConvertTo-DotNetEncoding では指定できません。"
            + "ファイルから解決するには Resolve-Encoding <path> | ConvertTo-DotNetEncoding を使用してください。";

        /// <summary>
        /// ANSI / OEM コードページを取得できない環境で ansi / oem が指定された場合のエラーメッセージ
        /// </summary>
        public const string CodePageNotAvailable =
            "この実行環境では '{0}' に対応するコードページを取得できません。"
            + "コードページ数値または WebName で明示的に指定してください。";

        /// <summary>
        /// 判定に失敗した EncodingInformation が渡された場合のエラーメッセージ
        /// </summary>
        public const string UndetectedEncodingInformation =
            "渡された EncodingInformation は文字エンコーディングの判定に失敗しています（CodePage = {0}）。";
    }
}