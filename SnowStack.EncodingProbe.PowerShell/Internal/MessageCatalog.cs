using System;
using System.Collections.Generic;
using System.Globalization;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 1.1.0 で追加したコマンドが返すメッセージのローカライズを担う。
    /// </summary>
    /// <remarks>
    /// 対応言語は、EncodingProbe の独自判定処理が対象としている言語圏に合わせて
    /// 英語・日本語・韓国語・繁体字中国語・簡体字中国語の5言語とする。
    /// いずれにも該当しない場合は英語にフォールバックする。
    /// <br/>
    /// 言語の決定には <see cref="CultureInfo.CurrentUICulture"/> を用いる。
    /// メッセージの表示言語と、数値や日付の書式に用いるカルチャー
    /// (<see cref="CultureInfo.CurrentCulture"/>) は別の軸であり、
    /// .NET では前者が UI 文字列の言語を決めるためである。
    /// 通常の実行環境では両者は一致する。
    /// <br/>
    /// サテライトアセンブリではなく単一アセンブリ内の表として持つ。
    /// 本モジュールの配布物 (publish フォルダー) は手作業で構成しており、
    /// 言語別サブフォルダーの配置漏れが「黙って英語に戻る」形で表面化するのを避けるためである。
    /// </remarks>
    internal static class MessageCatalog
    {
        /// <summary>英語（フォールバック先）</summary>
        public const string English = "en";

        /// <summary>日本語</summary>
        public const string Japanese = "ja";

        /// <summary>韓国語</summary>
        public const string Korean = "ko";

        /// <summary>繁体字中国語</summary>
        public const string ChineseTraditional = "zh-Hant";

        /// <summary>簡体字中国語</summary>
        public const string ChineseSimplified = "zh-Hans";

        /// <summary>中国語の言語コード</summary>
        private const string ChineseLanguageCode = "zh";

        /// <summary>繁体字を使用する地域のサブタグ（台湾・香港・マカオ）</summary>
        private static readonly string[] TraditionalChineseRegions = { "-TW", "-HK", "-MO" };

        private static readonly Dictionary<MessageKey, string> EnglishMessages =
            new Dictionary<MessageKey, string>
            {
                [MessageKey.UnknownEncoding] =
                    "Cannot resolve the character encoding '{0}'. Specify a unified vocabulary name "
                    + "(such as utf8NoBOM), a WebName (such as shift_jis), or a numeric code page (such as 932).",

                [MessageKey.NullEncoding] =
                    "The character encoding cannot be null.",

                [MessageKey.BareUtf8NotAllowedForWrite] =
                    "'{0}' cannot be used for writing. UTF-8 is interpreted differently with respect to the BOM "
                    + "between PowerShell 5.1 and 7.x, so specify either 'utf8NoBOM' or 'utf8BOM'.",

                [MessageKey.BomPolicyUnspecifiedForWrite] =
                    "'{0}' cannot be used for writing because it does not determine whether a BOM is emitted. "
                    + "Specify a name that states the BOM explicitly, such as '{1}' or '{2}'.",

                [MessageKey.Utf7NotAllowedForWrite] =
                    "UTF-7 cannot be used for writing. UTF-7 is supported for reading only.",

                [MessageKey.BomSuffixNotAllowed] =
                    "A BOM suffix cannot be applied to '{0}'. BOM suffixes are allowed only for the five Unicode "
                    + "families: utf8, unicode, bigendianunicode, utf32 and bigendianutf32.",

                [MessageKey.AutoNotAllowedForConvert] =
                    "Auto denotes detection from a file, so it cannot be used with ConvertTo-DotNetEncoding. "
                    + "To resolve from a file, use Resolve-Encoding <path> | ConvertTo-DotNetEncoding.",

                [MessageKey.CodePageNotAvailable] =
                    "The code page for '{0}' is not available in this environment. "
                    + "Specify a numeric code page or a WebName explicitly.",

                [MessageKey.UndetectedEncodingInformation] =
                    "The supplied EncodingInformation does not carry a successful detection result (CodePage = {0}).",
                [MessageKey.DetectionFailed] =
                    "Cannot detect the character encoding of '{0}'. Specify it explicitly with -Encoding.",
                [MessageKey.FileTooLargeToDetect] =
                    "'{0}' is too large for character encoding detection, which reads the whole file "
                    + "(the file must be smaller than 2 GB). Specify the encoding explicitly with -Encoding.",

                [MessageKey.RawAndTotalCountAreExclusive] =
                    "The -Raw and -TotalCount parameters cannot be specified in the same command.",

                [MessageKey.FileNotFound] =
                    "Cannot find the file '{0}'.",

                [MessageKey.PathIsNotFile] =
                    "'{0}' is not a file.",

                [MessageKey.SamePathRoundTrip] =
                    "'{0}' is currently being read, so it cannot be used as the write target. "
                    + "Reading and writing the same file within one pipeline truncates it before it has been read. "
                    + "Receive the content into a variable first, for example: "
                    + "$text = Get-ProbedContent <path> -Raw",
            };

        private static readonly Dictionary<MessageKey, string> JapaneseMessages =
            new Dictionary<MessageKey, string>
            {
                [MessageKey.UnknownEncoding] =
                    "文字エンコーディング '{0}' を解決できませんでした。統一語彙名（utf8NoBOM 等）、"
                    + "WebName（shift_jis 等）、コードページ数値（932 等）のいずれかを指定してください。",

                [MessageKey.NullEncoding] =
                    "文字エンコーディングに null は指定できません。",

                [MessageKey.BareUtf8NotAllowedForWrite] =
                    "書き込みでは '{0}' を指定できません。UTF-8 は PowerShell 5.1 と 7.x で BOM の解釈が異なるため、"
                    + "'utf8NoBOM' または 'utf8BOM' のいずれかを明示してください。",

                [MessageKey.BomPolicyUnspecifiedForWrite] =
                    "'{0}' は BOM の有無が定まらないため、書き込みでは指定できません。"
                    + "'{1}' または '{2}' のように BOM の有無を明示した名前を指定してください。",

                [MessageKey.Utf7NotAllowedForWrite] =
                    "書き込みでは UTF-7 を指定できません。UTF-7 は読み取りのみ対応しています。",

                [MessageKey.BomSuffixNotAllowed] =
                    "'{0}' に BOM 接尾辞は指定できません。BOM 接尾辞を指定できるのは "
                    + "utf8 / unicode / bigendianunicode / utf32 / bigendianutf32 の 5 系統のみです。",

                [MessageKey.AutoNotAllowedForConvert] =
                    "Auto はファイルからの検出を指す語彙のため、ConvertTo-DotNetEncoding では指定できません。"
                    + "ファイルから解決するには Resolve-Encoding <path> | ConvertTo-DotNetEncoding を使用してください。",

                [MessageKey.CodePageNotAvailable] =
                    "この実行環境では '{0}' に対応するコードページを取得できません。"
                    + "コードページ数値または WebName で明示的に指定してください。",

                [MessageKey.UndetectedEncodingInformation] =
                    "渡された EncodingInformation は文字エンコーディングの判定に失敗しています（CodePage = {0}）。",
                [MessageKey.DetectionFailed] =
                    "'{0}' の文字エンコーディングを判定できませんでした。-Encoding で明示的に指定してください。",
                [MessageKey.FileTooLargeToDetect] =
                    "'{0}' は、文字エンコーディングの判定に対して大きすぎます。"
                    + "判定はファイル全体を読み込むため、2GB 未満である必要があります。"
                    + "-Encoding で明示的に指定してください。",

                [MessageKey.RawAndTotalCountAreExclusive] =
                    "-Raw と -TotalCount は同時に指定できません。",

                [MessageKey.FileNotFound] =
                    "ファイル '{0}' が見つかりません。",

                [MessageKey.PathIsNotFile] =
                    "'{0}' はファイルではありません。",

                [MessageKey.SamePathRoundTrip] =
                    "'{0}' は読み取り中のため、書き込み先に指定できません。"
                    + "同一のファイルを1つのパイプラインで読み書きすると、読み終える前にファイルが切り詰められます。"
                    + "$text = Get-ProbedContent <path> -Raw のように、いったん変数に受けてください。",
            };

        private static readonly Dictionary<MessageKey, string> KoreanMessages =
            new Dictionary<MessageKey, string>
            {
                [MessageKey.UnknownEncoding] =
                    "문자 인코딩 '{0}'을(를) 확인할 수 없습니다. 통합 어휘 이름(utf8NoBOM 등), "
                    + "WebName(shift_jis 등), 코드 페이지 번호(932 등) 중 하나를 지정하십시오.",

                [MessageKey.NullEncoding] =
                    "문자 인코딩에는 null을 지정할 수 없습니다.",

                [MessageKey.BareUtf8NotAllowedForWrite] =
                    "쓰기에는 '{0}'을(를) 지정할 수 없습니다. UTF-8은 PowerShell 5.1과 7.x에서 BOM 해석이 다르므로 "
                    + "'utf8NoBOM' 또는 'utf8BOM'을 명시하십시오.",

                [MessageKey.BomPolicyUnspecifiedForWrite] =
                    "'{0}'은(는) BOM 출력 여부가 정해지지 않으므로 쓰기에 지정할 수 없습니다. "
                    + "'{1}' 또는 '{2}'처럼 BOM 유무를 명시한 이름을 지정하십시오.",

                [MessageKey.Utf7NotAllowedForWrite] =
                    "쓰기에는 UTF-7을 지정할 수 없습니다. UTF-7은 읽기만 지원합니다.",

                [MessageKey.BomSuffixNotAllowed] =
                    "'{0}'에는 BOM 접미사를 지정할 수 없습니다. BOM 접미사를 지정할 수 있는 것은 "
                    + "utf8 / unicode / bigendianunicode / utf32 / bigendianutf32 의 5개 계열뿐입니다.",

                [MessageKey.AutoNotAllowedForConvert] =
                    "Auto는 파일에서 검색하는 것을 가리키는 어휘이므로 ConvertTo-DotNetEncoding에서는 지정할 수 없습니다. "
                    + "파일에서 확인하려면 Resolve-Encoding <path> | ConvertTo-DotNetEncoding 을 사용하십시오.",

                [MessageKey.CodePageNotAvailable] =
                    "이 실행 환경에서는 '{0}'에 해당하는 코드 페이지를 가져올 수 없습니다. "
                    + "코드 페이지 번호 또는 WebName으로 명시적으로 지정하십시오.",

                [MessageKey.UndetectedEncodingInformation] =
                    "전달된 EncodingInformation은 문자 인코딩 판별에 실패했습니다(CodePage = {0}).",
                [MessageKey.DetectionFailed] =
                    "'{0}'의 문자 인코딩을 판별할 수 없습니다. -Encoding으로 명시적으로 지정하십시오.",
                [MessageKey.FileTooLargeToDetect] =
                    "'{0}'은(는) 문자 인코딩 판별에 비해 너무 큽니다. "
                    + "판별은 파일 전체를 읽으므로 2GB 미만이어야 합니다. "
                    + "-Encoding으로 명시적으로 지정하십시오.",

                [MessageKey.RawAndTotalCountAreExclusive] =
                    "-Raw와 -TotalCount는 동시에 지정할 수 없습니다.",

                [MessageKey.FileNotFound] =
                    "파일 '{0}'을(를) 찾을 수 없습니다.",

                [MessageKey.PathIsNotFile] =
                    "'{0}'은(는) 파일이 아닙니다.",

                [MessageKey.SamePathRoundTrip] =
                    "'{0}'은(는) 읽는 중이므로 쓰기 대상으로 지정할 수 없습니다. "
                    + "같은 파일을 하나의 파이프라인에서 읽고 쓰면 다 읽기 전에 파일이 잘립니다. "
                    + "$text = Get-ProbedContent <path> -Raw 처럼 먼저 변수에 받으십시오.",
            };

        private static readonly Dictionary<MessageKey, string> ChineseTraditionalMessages =
            new Dictionary<MessageKey, string>
            {
                [MessageKey.UnknownEncoding] =
                    "無法解析字元編碼 '{0}'。請指定統一詞彙名稱（例如 utf8NoBOM）、"
                    + "WebName（例如 shift_jis）或數值字碼頁（例如 932）。",

                [MessageKey.NullEncoding] =
                    "字元編碼不可指定為 null。",

                [MessageKey.BareUtf8NotAllowedForWrite] =
                    "寫入時無法指定 '{0}'。UTF-8 在 PowerShell 5.1 與 7.x 中對 BOM 的解釋不同，"
                    + "請明確指定 'utf8NoBOM' 或 'utf8BOM'。",

                [MessageKey.BomPolicyUnspecifiedForWrite] =
                    "'{0}' 未確定是否輸出 BOM，因此無法用於寫入。"
                    + "請指定明確標示 BOM 有無的名稱，例如 '{1}' 或 '{2}'。",

                [MessageKey.Utf7NotAllowedForWrite] =
                    "寫入時無法指定 UTF-7。UTF-7 僅支援讀取。",

                [MessageKey.BomSuffixNotAllowed] =
                    "'{0}' 不可加上 BOM 後置詞。可加上 BOM 後置詞的僅有 "
                    + "utf8 / unicode / bigendianunicode / utf32 / bigendianutf32 這五個系列。",

                [MessageKey.AutoNotAllowedForConvert] =
                    "Auto 是指從檔案偵測的詞彙，因此無法用於 ConvertTo-DotNetEncoding。"
                    + "若要從檔案解析，請使用 Resolve-Encoding <path> | ConvertTo-DotNetEncoding。",

                [MessageKey.CodePageNotAvailable] =
                    "此執行環境無法取得 '{0}' 對應的字碼頁。請以數值字碼頁或 WebName 明確指定。",

                [MessageKey.UndetectedEncodingInformation] =
                    "傳入的 EncodingInformation 並未成功判斷字元編碼（CodePage = {0}）。",
                [MessageKey.DetectionFailed] =
                    "無法判斷 '{0}' 的字元編碼。請以 -Encoding 明確指定。",
                [MessageKey.FileTooLargeToDetect] =
                    "'{0}' 對於字元編碼判斷而言太大。"
                    + "判斷會讀取整個檔案，因此檔案必須小於 2 GB。"
                    + "請以 -Encoding 明確指定。",

                [MessageKey.RawAndTotalCountAreExclusive] =
                    "-Raw 與 -TotalCount 不可同時指定。",

                [MessageKey.FileNotFound] =
                    "找不到檔案 '{0}'。",

                [MessageKey.PathIsNotFile] =
                    "'{0}' 不是檔案。",

                [MessageKey.SamePathRoundTrip] =
                    "'{0}' 正在讀取中，因此無法指定為寫入目標。"
                    + "在同一個管線中讀寫同一個檔案，會在讀取完成前就將檔案截斷。"
                    + "請先接收到變數中，例如：$text = Get-ProbedContent <path> -Raw",
            };

        private static readonly Dictionary<MessageKey, string> ChineseSimplifiedMessages =
            new Dictionary<MessageKey, string>
            {
                [MessageKey.UnknownEncoding] =
                    "无法解析字符编码 '{0}'。请指定统一词汇名称（例如 utf8NoBOM）、"
                    + "WebName（例如 shift_jis）或数值代码页（例如 932）。",

                [MessageKey.NullEncoding] =
                    "字符编码不能指定为 null。",

                [MessageKey.BareUtf8NotAllowedForWrite] =
                    "写入时无法指定 '{0}'。UTF-8 在 PowerShell 5.1 与 7.x 中对 BOM 的解释不同，"
                    + "请明确指定 'utf8NoBOM' 或 'utf8BOM'。",

                [MessageKey.BomPolicyUnspecifiedForWrite] =
                    "'{0}' 未确定是否输出 BOM，因此无法用于写入。"
                    + "请指定明确标示 BOM 有无的名称，例如 '{1}' 或 '{2}'。",

                [MessageKey.Utf7NotAllowedForWrite] =
                    "写入时无法指定 UTF-7。UTF-7 仅支持读取。",

                [MessageKey.BomSuffixNotAllowed] =
                    "'{0}' 不能添加 BOM 后缀。可以添加 BOM 后缀的只有 "
                    + "utf8 / unicode / bigendianunicode / utf32 / bigendianutf32 这五个系列。",

                [MessageKey.AutoNotAllowedForConvert] =
                    "Auto 是指从文件检测的词汇，因此无法用于 ConvertTo-DotNetEncoding。"
                    + "若要从文件解析，请使用 Resolve-Encoding <path> | ConvertTo-DotNetEncoding。",

                [MessageKey.CodePageNotAvailable] =
                    "此运行环境无法获取 '{0}' 对应的代码页。请使用数值代码页或 WebName 明确指定。",

                [MessageKey.UndetectedEncodingInformation] =
                    "传入的 EncodingInformation 未能成功判断字符编码（CodePage = {0}）。",
                [MessageKey.DetectionFailed] =
                    "无法判断 '{0}' 的字符编码。请使用 -Encoding 明确指定。",
                [MessageKey.FileTooLargeToDetect] =
                    "'{0}' 对于字符编码判断而言太大。"
                    + "判断会读取整个文件，因此文件必须小于 2 GB。"
                    + "请使用 -Encoding 明确指定。",

                [MessageKey.RawAndTotalCountAreExclusive] =
                    "-Raw 与 -TotalCount 不能同时指定。",

                [MessageKey.FileNotFound] =
                    "找不到文件 '{0}'。",

                [MessageKey.PathIsNotFile] =
                    "'{0}' 不是文件。",

                [MessageKey.SamePathRoundTrip] =
                    "'{0}' 正在读取中，因此无法指定为写入目标。"
                    + "在同一个管道中读写同一个文件，会在读取完成前就将文件截断。"
                    + "请先接收到变量中，例如：$text = Get-ProbedContent <path> -Raw",
            };

        private static readonly Dictionary<string, Dictionary<MessageKey, string>> Catalogs =
            new Dictionary<string, Dictionary<MessageKey, string>>(StringComparer.OrdinalIgnoreCase)
            {
                [English] = EnglishMessages,
                [Japanese] = JapaneseMessages,
                [Korean] = KoreanMessages,
                [ChineseTraditional] = ChineseTraditionalMessages,
                [ChineseSimplified] = ChineseSimplifiedMessages,
            };

        /// <summary>
        /// 対応している言語の一覧
        /// </summary>
        public static IEnumerable<string> SupportedLanguages => Catalogs.Keys;

        /// <summary>
        /// 実行環境の UI カルチャーに応じたメッセージを取得する
        /// </summary>
        public static string Get(MessageKey key)
            => Get(key, ResolveLanguage(CultureInfo.CurrentUICulture));

        /// <summary>
        /// 言語を指定してメッセージを取得する。未対応の言語は英語にフォールバックする。
        /// </summary>
        public static string Get(MessageKey key, string language)
        {
            if (!Catalogs.TryGetValue(language, out Dictionary<MessageKey, string>? catalog))
            {
                catalog = EnglishMessages;
            }

            return catalog.TryGetValue(key, out string? message) ? message : EnglishMessages[key];
        }

        /// <summary>
        /// 実行環境の UI カルチャーに応じたメッセージを取得し、書式を適用する
        /// </summary>
        public static string Format(MessageKey key, params object?[] arguments)
            => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

        /// <summary>
        /// カルチャーから、対応言語のいずれかを決定する。
        /// 対応しない言語は英語にフォールバックする。
        /// </summary>
        public static string ResolveLanguage(CultureInfo? culture)
        {
            if (culture == null)
            {
                return English;
            }

            string language = culture.TwoLetterISOLanguageName;

            if (string.Equals(language, ChineseLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                return ResolveChineseScript(culture);
            }

            if (string.Equals(language, Japanese, StringComparison.OrdinalIgnoreCase))
            {
                return Japanese;
            }

            if (string.Equals(language, Korean, StringComparison.OrdinalIgnoreCase))
            {
                return Korean;
            }

            return English;
        }

        /// <summary>
        /// 中国語のカルチャーが繁体字と簡体字のどちらであるかを判定する。
        /// </summary>
        /// <remarks>
        /// zh-TW / zh-CN などの具体的なカルチャーは、親をたどると zh-Hant / zh-Hans に到達する。
        /// .NET Framework 4.8 では途中に zh-CHT / zh-CHS が挟まるが、いずれも最終的には
        /// zh-Hant / zh-Hans を経由するため、親チェーンの走査で両ランタイムに対応できる。
        /// スクリプトを特定できない中立カルチャー(zh)は簡体字として扱う。
        /// </remarks>
        private static string ResolveChineseScript(CultureInfo culture)
        {
            for (CultureInfo? current = culture;
                 current != null && !string.IsNullOrEmpty(current.Name);
                 current = current.Parent)
            {
                if (string.Equals(current.Name, ChineseTraditional, StringComparison.OrdinalIgnoreCase))
                {
                    return ChineseTraditional;
                }

                if (string.Equals(current.Name, ChineseSimplified, StringComparison.OrdinalIgnoreCase))
                {
                    return ChineseSimplified;
                }
            }

            // 親チェーンからスクリプトを特定できない場合は地域で判定する
            foreach (string region in TraditionalChineseRegions)
            {
                if (culture.Name.EndsWith(region, StringComparison.OrdinalIgnoreCase))
                {
                    return ChineseTraditional;
                }
            }

            return ChineseSimplified;
        }
    }
}