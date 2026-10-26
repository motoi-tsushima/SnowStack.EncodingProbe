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
    /// 香港・マカオ・広東語は台湾と同じ繁体字の文面を使う（<see cref="ResolveLanguage"/> を参照）。
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

                [MessageKey.EncodingAndEncodingFromAreExclusive] =
                    "The -Encoding and -EncodingFrom parameters cannot be specified in the same command, "
                    + "because both of them determine the character encoding and the BOM.",

                [MessageKey.AutoEncodingRequiresExistingFile] =
                    "Cannot inherit the character encoding because the file '{0}' does not exist. "
                    + "Specify the encoding with -Encoding, or inherit it from another file with -EncodingFrom.",

                [MessageKey.NoNewlineIgnoresLineBreak] =
                    "-NoNewline is specified, so the value of -LineBreak is not used. "
                    + "-LineBreak selects which characters are written as a line break, "
                    + "while -NoNewline suppresses writing the line break itself.",

                [MessageKey.EncodingChangeOnAppend] =
                    "Cannot append to '{0}' with the character encoding '{1}'. The bytes produced by '{1}' "
                    + "differ from those produced by the current encoding of the file, '{2}', so appending "
                    + "would leave the file inconsistent. Specify -AllowEncodingChange if changing the "
                    + "character encoding of the file is intended.",

                [MessageKey.DetectedCodePageNotAvailable] =
                    "The character encoding was detected as code page {0} ('{1}'), but this .NET runtime "
                    + "does not provide that code page, so the file cannot be handled with it. "
                    + "Specify the character encoding explicitly with -Encoding.",

                [MessageKey.InvalidCulture] =
                    "'{0}' cannot be interpreted as a culture name. Specify a culture name such as "
                    + "'ja-JP' or 'ko-KR', or omit -Culture to use the culture of the current environment.",

                [MessageKey.InvalidStrategy] =
                    "'{0}' cannot be interpreted as a detection strategy. "
                    + "Specify 'Combined', 'NativeOnly' or 'UtfUnknownOnly'.",

                [MessageKey.FileIsReadOnly] =
                    "'{0}' is read-only. Specify -Force to write to it; the read-only attribute is restored "
                    + "after writing.",

                [MessageKey.NoClobberFileExists] =
                    "The file '{0}' already exists. -NoClobber prevents an existing file from being overwritten.",

                [MessageKey.WildcardMatchedNoFile] =
                    "No file matches '{0}'. A path containing wildcard characters must match exactly "
                    + "one existing file. To create a file whose name contains wildcard characters, use "
                    + "-LiteralPath.",

                [MessageKey.WildcardMatchedMultipleFiles] =
                    "'{0}' matches {1} files. A path containing wildcard characters must match exactly "
                    + "one file.",

                [MessageKey.ParentDirectoryNotFound] =
                    "Cannot write '{0}' because its parent directory does not exist.",

                [MessageKey.AllowEncodingChangeWithoutAppend] =
                    "-AllowEncodingChange has no effect without -Append, so it is ignored.",

                [MessageKey.NoConversionSpecified] =
                    "Nothing to convert. Specify at least one of -Encoding, -EncodingFrom, -Bom and -LineBreak.",

                [MessageKey.AutoNotAllowedForConvertContent] =
                    "Auto cannot be used as the -Encoding of Convert-ProbedContent. To keep the character "
                    + "encoding of the source file, omit -Encoding.",

                [MessageKey.BomConflictsWithEncoding] =
                    "The character encoding '{0}' contradicts -Bom {1}. Specify only one of them, or "
                    + "make them agree.",

                [MessageKey.BomNotSupportedByEncoding] =
                    "The character encoding '{0}' cannot have a BOM, so -Bom Add cannot be applied. A "
                    + "BOM exists only for the five Unicode families: utf8, unicode, bigendianunicode, "
                    + "utf32 and bigendianutf32.",

                [MessageKey.DestinationFolderNotFound] =
                    "The destination folder '{0}' does not exist. -Destination must be an existing folder; "
                    + "it is not created.",

                [MessageKey.InvalidSourceBytes] =
                    "'{0}' was not converted because it contains bytes that are invalid in the character "
                    + "encoding '{1}', at byte offset {2} ({3}). The file was left unchanged. If the character "
                    + "encoding was misdetected, specify it with -SourceEncoding.",

                [MessageKey.UnrepresentableCharacter] =
                    "'{0}' was not converted because the character {2} '{3}' at line {4}, column {5} "
                    + "cannot be represented in the character encoding '{1}'. The file was left unchanged.",

                [MessageKey.DestinationFileExists] =
                    "The file '{0}' already exists. Specify -Force to overwrite it.",

                [MessageKey.DestinationNameConflict] =
                    "'{1}' was not converted because '{0}' is already the destination of another file with the same name "
                    + "in this command. -Destination does not keep the folder structure.",

                [MessageKey.DestinationIsSource] =
                    "'{0}' was not converted because the destination is the source file itself. To convert "
                    + "a file in place, omit -Destination.",

                [MessageKey.SkippedDirectory] =
                    "Skipped the directory '{0}'.",

                [MessageKey.DetectionLinesTooLarge] =
                    "Cannot detect the character encoding of '{0}' from its first {1} lines, because they are larger "
                    + "than the detection limit (they must be smaller than 2 GB). A file without line breaks is read "
                    + "as a whole. Specify a smaller -DetectionLineCount. Cmdlets that have -Encoding also let you "
                    + "specify the character encoding explicitly.",
                [MessageKey.DetectionVerificationFailed] =
                    "'{0}' was not processed because it contains bytes that are invalid in the character encoding "
                    + "'{1}', which was detected from its first {2} lines, at byte offset {3} ({4}). "
                    + "Increase -DetectionLineCount, or specify the character encoding with -Encoding.",

                [MessageKey.CsvHeaderAlreadyPresent] =
                    "The column name \"{0}\" is specified more than once in the header (column names are not case-sensitive).",
                [MessageKey.CsvUnspecifiedHeader] =
                    "One or more headers were not specified. Default names starting with \"H\" have been used "
                    + "in place of any missing headers.",
                [MessageKey.CsvAppendMismatchedPropertyName] =
                    "Cannot append CSV content to the following file: {1}. The appended object does not have a property "
                    + "that corresponds to the following column: {0}. To continue with mismatched properties, "
                    + "add the -Force parameter, and then retry the command.",

                [MessageKey.FileTooLargeToDetectUseLineCount] =
                    "'{0}' is too large for character encoding detection, which reads the whole file "
                    + "(the file must be smaller than 2 GB). Specify -DetectionLineCount to detect the character encoding "
                    + "from the first lines, or specify it explicitly with -Encoding.",
                [MessageKey.InvalidBytesDuringImport] =
                    "Stopped reading '{0}' because bytes that are invalid in the character encoding '{1}' were found "
                    + "while reading, at byte offset {2} ({3}). The records already output cannot be withdrawn. "
                    + "The file may have been changed after it was verified.",

                [MessageKey.CannotSpecifyQuoteFieldsAndUseQuotes] =
                    "You must specify either the -UseQuotes or -QuoteFields parameters, but not both.",
                [MessageKey.CannotSpecifyPathAndLiteralPath] =
                    "You must specify either the -Path or -LiteralPath parameters, but not both.",

                // ---- 1.3.0 第五次 ----

                [MessageKey.UnrepresentableCharacterOnWrite] =
                    "'{0}' was not written because the character {2} '{3}' cannot be represented in the character encoding '{1}'. "
                    + "The file was left unchanged.",
                [MessageKey.DetectionVerificationIso2022Escape] =
                    "'{0}' was not processed because it contains an ISO-2022 escape sequence at byte offset {3} ({4}), "
                    + "although its first {2} lines contain only ASCII characters and were read as '{1}'. "
                    + "Remove -DetectionLineCount, or increase it so that the lines include the escape sequence.",
                [MessageKey.CsvAppendEmptyPropertyName] =
                    "Cannot append CSV content to the following file: {0}. The header of the file has an empty column name, "
                    + "which cannot be used as a property name.",

                // ---- 1.3.0 第五次の後半 ----

                [MessageKey.WildcardMatchedNoFileForWrite] =
                    "No file matches '{0}', so nothing was written. To create a file whose name contains wildcard characters, use "
                    + "-LiteralPath.",
                [MessageKey.AlternateDataStreamNotSupported] =
                    "'{0}' specifies an alternate data stream. Only the main stream of a file (the path itself, or the path followed by "
                    + "'::$DATA') can be read or written.",
                [MessageKey.RestoreFromBackupFailed] =
                    "'{0}' could not be restored and is left partially written. Its original content is kept in '{1}'; restore the file from this copy.",

                [MessageKey.LinkTargetNotFound] =
                    "'{0}' was not written because it is a symbolic link whose target does not exist. Like Set-Content, this command "
                    + "does not create the target of a broken link. Write to the target path, or use Out-ProbedFile.",

                [MessageKey.WriteAccessDenied] =
                    "'{0}' was not written because access to it was denied. Check the permissions of the file (for a new file, of the folder).",
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

                [MessageKey.EncodingAndEncodingFromAreExclusive] =
                    "-Encoding と -EncodingFrom は同時に指定できません。"
                    + "どちらも文字エンコーディングと BOM を決めるためです。",

                [MessageKey.AutoEncodingRequiresExistingFile] =
                    "ファイル '{0}' が存在しないため、文字エンコーディングを継承できません。"
                    + "-Encoding で明示的に指定するか、-EncodingFrom で別のファイルから継承してください。",

                [MessageKey.NoNewlineIgnoresLineBreak] =
                    "-NoNewline が指定されているため、-LineBreak の指定は使用されません。"
                    + "-LineBreak は改行として出力する文字を選ぶもので、"
                    + "-NoNewline は改行を出力すること自体を抑止します。",

                [MessageKey.EncodingChangeOnAppend] =
                    "'{0}' に文字エンコーディング '{1}' で追記できません。'{1}' で符号化したバイト列が、"
                    + "このファイルの現在の文字エンコーディング '{2}' で符号化したバイト列と異なるため、"
                    + "追記するとファイル全体の一貫性が失われます。"
                    + "文字エンコーディングを変えることが意図どおりであれば -AllowEncodingChange を指定してください。",

                [MessageKey.DetectedCodePageNotAvailable] =
                    "文字エンコーディングをコードページ {0}（'{1}'）と判定しましたが、"
                    + "この .NET ランタイムはそのコードページを提供していないため、このファイルを扱えません。"
                    + "-Encoding で文字エンコーディングを明示的に指定してください。",

                [MessageKey.InvalidCulture] =
                    "'{0}' はカルチャー名として解釈できません。'ja-JP' や 'ko-KR' のようなカルチャー名を"
                    + "指定するか、-Culture を省略して実行環境のカルチャーを使用してください。",

                [MessageKey.InvalidStrategy] =
                    "'{0}' は判定方式として解釈できません。"
                    + "'Combined'、'NativeOnly'、'UtfUnknownOnly' のいずれかを指定してください。",

                [MessageKey.FileIsReadOnly] =
                    "'{0}' は読み取り専用です。書き込むには -Force を指定してください。読み取り専用の属性は書き込み後に元に戻します。",

                [MessageKey.NoClobberFileExists] =
                    "ファイル '{0}' は既に存在します。-NoClobber が指定されているため上書きしません。",

                [MessageKey.WildcardMatchedNoFile] =
                    "'{0}' に一致するファイルがありません。ワイルドカード文字を含むパスは、既存のファイル 1 件にだけ一致する必要があります。名前にワイルドカード文字を含むファイルを作成するには "
                    + "-LiteralPath を使用してください。",

                [MessageKey.WildcardMatchedMultipleFiles] =
                    "'{0}' は {1} 件のファイルに一致しました。ワイルドカード文字を含むパスは、ファイル 1 件にだけ一致する必要があります。",

                [MessageKey.ParentDirectoryNotFound] =
                    "親ディレクトリが存在しないため、'{0}' に書き込めません。",

                [MessageKey.AllowEncodingChangeWithoutAppend] =
                    "-AllowEncodingChange は -Append と組み合わせたときだけ意味を持つため、無視します。",

                [MessageKey.NoConversionSpecified] =
                    "変換する内容が指定されていません。-Encoding、-EncodingFrom、-Bom、-LineBreak のいずれかを指定してください。",

                [MessageKey.AutoNotAllowedForConvertContent] =
                    "Convert-ProbedContent の -Encoding には Auto を指定できません。変換元の文字エンコーディングを保つ場合は、-Encoding "
                    + "を省略してください。",

                [MessageKey.BomConflictsWithEncoding] =
                    "文字エンコーディング '{0}' と -Bom {1} が食い違っています。どちらか一方だけを指定するか、両者を一致させてください。",

                [MessageKey.BomNotSupportedByEncoding] =
                    "文字エンコーディング '{0}' は BOM を持てないため、-Bom Add を適用できません。BOM があるのは utf8 / unicode / bigendianunicode "
                    + "/ utf32 / bigendianutf32 の 5 系統だけです。",

                [MessageKey.DestinationFolderNotFound] =
                    "出力先のフォルダー '{0}' が存在しません。-Destination には既存のフォルダーを指定してください（フォルダーは作成しません）。",

                [MessageKey.InvalidSourceBytes] =
                    "'{0}' には文字エンコーディング '{1}' として不正なバイト列がある（バイト位置 {2}: {3}）ため、変換しませんでした。ファイルは変更していません。"
                    + "文字エンコーディングの判定が誤っている場合は -SourceEncoding で指定してください。",

                [MessageKey.UnrepresentableCharacter] =
                    "'{0}' の {4} 行 {5} 桁目の文字 {2} '{3}' は文字エンコーディング '{1}' で表現できないため、変換しませんでした。ファイルは変更していません。",

                [MessageKey.DestinationFileExists] =
                    "ファイル '{0}' は既に存在します。上書きするには -Force を指定してください。",

                [MessageKey.DestinationNameConflict] =
                    "'{0}' は同じ名前の別のファイルの書き込み先として既に使われているため、'{1}' は変換しませんでした。-Destination はフォルダー構造を保ちません。",

                [MessageKey.DestinationIsSource] =
                    "出力先が変換元のファイル自身であるため、'{0}' は変換しませんでした。その場で変換する場合は -Destination を省略してください。",

                [MessageKey.SkippedDirectory] =
                    "ディレクトリ '{0}' は対象外のため飛ばしました。",

                [MessageKey.DetectionLinesTooLarge] =
                    "'{0}' の先頭 {1} 行は、文字エンコーディングを判定できる大きさ（2 GB 未満）を超えているため、判定できません。"
                    + "改行の無いファイルは全体を読みます。-DetectionLineCount を小さくしてください。"
                    + "-Encoding を持つコマンドでは、文字エンコーディングを明示することもできます。",
                [MessageKey.DetectionVerificationFailed] =
                    "'{0}' には、先頭 {2} 行から判定した文字エンコーディング '{1}' として不正なバイト列がある（バイト位置 {3}: {4}）ため、処理しませんでした。"
                    + "-DetectionLineCount を増やすか、-Encoding で文字エンコーディングを指定してください。",

                [MessageKey.CsvHeaderAlreadyPresent] =
                    "列名 \"{0}\" がヘッダーに複数回指定されています（列名の大文字と小文字は区別しません）。",
                [MessageKey.CsvUnspecifiedHeader] =
                    "指定されていない列名があります。不足している列名には、\"H\" で始まる既定の名前を使いました。",
                [MessageKey.CsvAppendMismatchedPropertyName] =
                    "次のファイルに CSV を追記できません: {1}。追記するオブジェクトに、次の列に当たるプロパティがありません: {0}。"
                    + "プロパティが一致しないまま続けるには、-Force を指定してもう一度実行してください。",

                [MessageKey.FileTooLargeToDetectUseLineCount] =
                    "'{0}' は大きすぎるため、文字エンコーディングを判定できません（判定はファイル全体を読むため、2 GB 未満である必要があります）。"
                    + "先頭の行で判定するには -DetectionLineCount を、文字エンコーディングを明示するには -Encoding を指定してください。",
                [MessageKey.InvalidBytesDuringImport] =
                    "'{0}' の読み込み中に、文字エンコーディング '{1}' として不正なバイト列が見つかった（バイト位置 {2}: {3}）ため、読み込みを中止しました。"
                    + "それまでに出力したレコードは取り消せません。検証の後にファイルが変更された可能性があります。",

                [MessageKey.CannotSpecifyQuoteFieldsAndUseQuotes] =
                    "-UseQuotes と -QuoteFields は同時に指定できません。どちらか一方を指定してください。",
                [MessageKey.CannotSpecifyPathAndLiteralPath] =
                    "-Path と -LiteralPath のどちらか一方だけを指定してください。",

                // ---- 1.3.0 第五次 ----

                [MessageKey.UnrepresentableCharacterOnWrite] =
                    "文字 {2} '{3}' は文字エンコーディング '{1}' で表現できないため、'{0}' に書き込みませんでした。ファイルは変更していません。",
                [MessageKey.DetectionVerificationIso2022Escape] =
                    "'{0}' は、先頭 {2} 行が英数字だけのため '{1}' として読みましたが、バイト位置 {3} に ISO-2022 のエスケープシーケンス（{4}）があるため、処理しませんでした。"
                    + "-DetectionLineCount を外すか、エスケープシーケンスのある行まで行数を増やしてください。",
                [MessageKey.CsvAppendEmptyPropertyName] =
                    "次のファイルに CSV を追記できません: {0}。ファイルのヘッダーに空の列名があり、プロパティ名として使えません。",

                // ---- 1.3.0 第五次の後半 ----

                [MessageKey.WildcardMatchedNoFileForWrite] =
                    "'{0}' に一致するファイルが無いため、書き込みませんでした。名前にワイルドカード文字を含むファイルを作成するには "
                    + "-LiteralPath を使用してください。",
                [MessageKey.AlternateDataStreamNotSupported] =
                    "'{0}' は代替データストリームを指定しています。読み書きできるのは、ファイルの主ストリーム（パスそのもの、またはパスの後に '::$DATA' を付けたもの）だけです。",
                [MessageKey.RestoreFromBackupFailed] =
                    "'{0}' を元に戻せなかったため、元のファイルは書きかけの状態です。元の内容は '{1}' に写してあります。この写しから元に戻してください。",

                [MessageKey.LinkTargetNotFound] =
                    "'{0}' はリンク先が存在しないシンボリックリンクのため、書き込みませんでした。標準の Set-Content と同じく、リンク切れのリンク先は作りません。"
                    + "リンク先のパスに書き込むか、Out-ProbedFile を使用してください。",

                [MessageKey.WriteAccessDenied] =
                    "'{0}' へのアクセスが拒否されたため、書き込みませんでした。ファイル（新しいファイルの場合はフォルダー）のアクセス権を確かめてください。",
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

                [MessageKey.EncodingAndEncodingFromAreExclusive] =
                    "-Encoding과 -EncodingFrom은 동시에 지정할 수 없습니다. "
                    + "둘 다 문자 인코딩과 BOM을 결정하기 때문입니다.",

                [MessageKey.AutoEncodingRequiresExistingFile] =
                    "파일 '{0}'이(가) 존재하지 않으므로 문자 인코딩을 상속할 수 없습니다. "
                    + "-Encoding으로 명시적으로 지정하거나 -EncodingFrom으로 다른 파일에서 상속하십시오.",

                [MessageKey.NoNewlineIgnoresLineBreak] =
                    "-NoNewline이 지정되어 있으므로 -LineBreak의 지정은 사용되지 않습니다. "
                    + "-LineBreak는 줄 바꿈으로 출력할 문자를 선택하며, "
                    + "-NoNewline은 줄 바꿈을 출력하는 것 자체를 억제합니다.",

                [MessageKey.EncodingChangeOnAppend] =
                    "'{0}'에 문자 인코딩 '{1}'(으)로 추가할 수 없습니다. '{1}'(으)로 인코딩한 바이트열이 "
                    + "이 파일의 현재 문자 인코딩 '{2}'(으)로 인코딩한 바이트열과 다르므로, "
                    + "추가하면 파일 전체의 일관성이 사라집니다. "
                    + "문자 인코딩을 바꾸는 것이 의도한 대로라면 -AllowEncodingChange를 지정하십시오.",

                [MessageKey.DetectedCodePageNotAvailable] =
                    "문자 인코딩을 코드 페이지 {0}('{1}')(으)로 판단했지만, "
                    + "이 .NET 런타임은 해당 코드 페이지를 제공하지 않으므로 이 파일을 처리할 수 없습니다. "
                    + "-Encoding으로 문자 인코딩을 명시적으로 지정하십시오.",

                [MessageKey.InvalidCulture] =
                    "'{0}'은(는) 문화권 이름으로 해석할 수 없습니다. 'ja-JP'나 'ko-KR' 같은 문화권 이름을 "
                    + "지정하거나, -Culture를 생략하여 현재 환경의 문화권을 사용하십시오.",

                [MessageKey.InvalidStrategy] =
                    "'{0}'은(는) 판정 방식으로 해석할 수 없습니다. "
                    + "'Combined', 'NativeOnly', 'UtfUnknownOnly' 중 하나를 지정하십시오.",

                [MessageKey.FileIsReadOnly] =
                    "'{0}'은(는) 읽기 전용입니다. 쓰려면 -Force를 지정하십시오. 읽기 전용 특성은 쓰기 후에 원래대로 되돌립니다.",

                [MessageKey.NoClobberFileExists] =
                    "파일 '{0}'이(가) 이미 있습니다. -NoClobber가 지정되어 있으므로 덮어쓰지 않습니다.",

                [MessageKey.WildcardMatchedNoFile] =
                    "'{0}'과(와) 일치하는 파일이 없습니다. 와일드카드 문자를 포함한 경로는 기존 파일 1개와만 일치해야 합니다. 이름에 와일드카드 문자가 포함된 "
                    + "파일을 만들려면 -LiteralPath를 사용하십시오.",

                [MessageKey.WildcardMatchedMultipleFiles] =
                    "'{0}'이(가) 파일 {1}개와 일치했습니다. 와일드카드 문자를 포함한 경로는 파일 1개와만 일치해야 합니다.",

                [MessageKey.ParentDirectoryNotFound] =
                    "상위 디렉터리가 없으므로 '{0}'에 쓸 수 없습니다.",

                [MessageKey.AllowEncodingChangeWithoutAppend] =
                    "-AllowEncodingChange는 -Append와 함께 지정할 때만 의미가 있으므로 무시합니다.",

                [MessageKey.NoConversionSpecified] =
                    "변환할 내용이 지정되지 않았습니다. -Encoding, -EncodingFrom, -Bom, -LineBreak 중 하나 이상을 지정하십시오.",

                [MessageKey.AutoNotAllowedForConvertContent] =
                    "Convert-ProbedContent의 -Encoding에는 Auto를 지정할 수 없습니다. 원본 파일의 문자 인코딩을 유지하려면 -Encoding을 "
                    + "생략하십시오.",

                [MessageKey.BomConflictsWithEncoding] =
                    "문자 인코딩 '{0}'과(와) -Bom {1}이(가) 서로 맞지 않습니다. 둘 중 하나만 지정하거나 둘을 일치시키십시오.",

                [MessageKey.BomNotSupportedByEncoding] =
                    "문자 인코딩 '{0}'은(는) BOM을 가질 수 없으므로 -Bom Add를 적용할 수 없습니다. BOM이 있는 것은 utf8 / unicode "
                    + "/ bigendianunicode / utf32 / bigendianutf32 의 5개 계열뿐입니다.",

                [MessageKey.DestinationFolderNotFound] =
                    "대상 폴더 '{0}'이(가) 없습니다. -Destination에는 기존 폴더를 지정하십시오(폴더는 만들지 않습니다).",

                [MessageKey.InvalidSourceBytes] =
                    "'{0}'에 문자 인코딩 '{1}'(으)로서 잘못된 바이트열이 있으므로(바이트 위치 {2}: {3}) 변환하지 않았습니다. 파일은 변경하지 않았습니다. "
                    + "문자 인코딩 판별이 잘못된 경우에는 -SourceEncoding으로 지정하십시오.",

                [MessageKey.UnrepresentableCharacter] =
                    "'{0}'의 {4}행 {5}열에 있는 문자 {2} '{3}'을(를) 문자 인코딩 '{1}'(으)로 표현할 수 없으므로 변환하지 않았습니다. 파일은 "
                    + "변경하지 않았습니다.",

                [MessageKey.DestinationFileExists] =
                    "파일 '{0}'이(가) 이미 있습니다. 덮어쓰려면 -Force를 지정하십시오.",

                [MessageKey.DestinationNameConflict] =
                    "'{0}'은(는) 이름이 같은 다른 파일의 출력 대상으로 이미 사용되었으므로 '{1}'은(는) 변환하지 않았습니다. -Destination은 폴더 구조를 "
                    + "유지하지 않습니다.",

                [MessageKey.DestinationIsSource] =
                    "대상이 원본 파일 자체이므로 '{0}'은(는) 변환하지 않았습니다. 그 자리에서 변환하려면 -Destination을 생략하십시오.",

                [MessageKey.SkippedDirectory] =
                    "디렉터리 '{0}'은(는) 대상이 아니므로 건너뛰었습니다.",

                [MessageKey.DetectionLinesTooLarge] =
                    "'{0}'의 처음 {1}줄은 문자 인코딩을 판별할 수 있는 크기(2GB 미만)를 초과하므로 판별할 수 없습니다. "
                    + "줄 바꿈이 없는 파일은 전체를 읽습니다. -DetectionLineCount를 줄이십시오. "
                    + "-Encoding이 있는 명령에서는 문자 인코딩을 명시할 수도 있습니다.",
                [MessageKey.DetectionVerificationFailed] =
                    "'{0}'에 처음 {2}줄에서 판별한 문자 인코딩 '{1}'(으)로서 잘못된 바이트열이 있으므로(바이트 위치 {3}: {4}) 처리하지 않았습니다. "
                    + "-DetectionLineCount를 늘리거나 -Encoding으로 문자 인코딩을 지정하십시오.",

                [MessageKey.CsvHeaderAlreadyPresent] =
                    "열 이름 \"{0}\"이(가) 헤더에 여러 번 지정되었습니다(열 이름은 대/소문자를 구분하지 않습니다).",
                [MessageKey.CsvUnspecifiedHeader] =
                    "지정되지 않은 열 이름이 있습니다. 누락된 열 이름 대신 \"H\"로 시작하는 기본 이름을 사용했습니다.",
                [MessageKey.CsvAppendMismatchedPropertyName] =
                    "다음 파일에 CSV를 추가할 수 없습니다: {1}. 추가하는 개체에 다음 열에 해당하는 속성이 없습니다: {0}. "
                    + "속성이 일치하지 않는 상태로 계속하려면 -Force를 지정하여 다시 실행하십시오.",

                [MessageKey.FileTooLargeToDetectUseLineCount] =
                    "'{0}'은(는) 너무 커서 문자 인코딩을 판별할 수 없습니다(판별은 파일 전체를 읽으므로 2GB 미만이어야 합니다). "
                    + "처음 줄로 판별하려면 -DetectionLineCount를, 문자 인코딩을 명시하려면 -Encoding을 지정하십시오.",
                [MessageKey.InvalidBytesDuringImport] =
                    "'{0}'을(를) 읽는 중에 문자 인코딩 '{1}'(으)로서 잘못된 바이트열이 발견되었으므로(바이트 위치 {2}: {3}) 읽기를 중지했습니다. "
                    + "이미 출력한 레코드는 취소할 수 없습니다. 검증 후에 파일이 변경되었을 수 있습니다.",

                [MessageKey.CannotSpecifyQuoteFieldsAndUseQuotes] =
                    "-UseQuotes와 -QuoteFields는 동시에 지정할 수 없습니다. 둘 중 하나만 지정하십시오.",
                [MessageKey.CannotSpecifyPathAndLiteralPath] =
                    "-Path와 -LiteralPath 중 하나만 지정하십시오.",

                // ---- 1.3.0 第五次 ----

                [MessageKey.UnrepresentableCharacterOnWrite] =
                    "문자 {2} '{3}'을(를) 문자 인코딩 '{1}'(으)로 표현할 수 없으므로 '{0}'에 쓰지 않았습니다. 파일은 변경하지 않았습니다.",
                [MessageKey.DetectionVerificationIso2022Escape] =
                    "'{0}'은(는) 처음 {2}줄이 ASCII 문자뿐이어서 '{1}'(으)로 읽었지만, 바이트 위치 {3}에 ISO-2022 이스케이프 시퀀스({4})가 있으므로 처리하지 않았습니다. "
                    + "-DetectionLineCount를 제거하거나, 이스케이프 시퀀스가 있는 줄까지 줄 수를 늘리십시오.",
                [MessageKey.CsvAppendEmptyPropertyName] =
                    "다음 파일에 CSV를 추가할 수 없습니다: {0}. 파일의 헤더에 빈 열 이름이 있어 속성 이름으로 사용할 수 없습니다.",

                // ---- 1.3.0 第五次の後半 ----

                [MessageKey.WildcardMatchedNoFileForWrite] =
                    "'{0}'과(와) 일치하는 파일이 없으므로 쓰지 않았습니다. 이름에 와일드카드 문자가 포함된 "
                    + "파일을 만들려면 -LiteralPath를 사용하십시오.",
                [MessageKey.AlternateDataStreamNotSupported] =
                    "'{0}'은(는) 대체 데이터 스트림을 지정합니다. 읽고 쓸 수 있는 것은 파일의 기본 스트림(경로 자체 또는 경로 뒤에 '::$DATA'를 붙인 것)뿐입니다.",
                [MessageKey.RestoreFromBackupFailed] =
                    "'{0}'을(를) 원래대로 되돌리지 못해 원래 파일은 쓰다 만 상태입니다. 원래 내용은 '{1}'에 복사해 두었습니다. 이 복사본에서 원래대로 되돌리십시오.",

                [MessageKey.LinkTargetNotFound] =
                    "'{0}'은(는) 대상이 존재하지 않는 기호 링크이므로 쓰지 않았습니다. 표준 Set-Content와 마찬가지로 끊어진 링크의 대상은 만들지 않습니다. "
                    + "대상 경로에 쓰거나 Out-ProbedFile을 사용하십시오.",

                [MessageKey.WriteAccessDenied] =
                    "'{0}'에 대한 액세스가 거부되어 쓰지 않았습니다. 파일(새 파일인 경우에는 폴더)의 액세스 권한을 확인하십시오.",
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

                [MessageKey.EncodingAndEncodingFromAreExclusive] =
                    "-Encoding 與 -EncodingFrom 不可同時指定，"
                    + "因為兩者都會決定字元編碼與 BOM。",

                [MessageKey.AutoEncodingRequiresExistingFile] =
                    "檔案 '{0}' 不存在，因此無法繼承字元編碼。"
                    + "請以 -Encoding 明確指定，或以 -EncodingFrom 從其他檔案繼承。",

                [MessageKey.NoNewlineIgnoresLineBreak] =
                    "已指定 -NoNewline，因此不會使用 -LineBreak 的指定。"
                    + "-LineBreak 用於選擇作為換行輸出的字元，"
                    + "而 -NoNewline 則抑制輸出換行本身。",

                [MessageKey.EncodingChangeOnAppend] =
                    "無法以字元編碼 '{1}' 附加至 '{0}'。以 '{1}' 編碼的位元組序列，"
                    + "與這個檔案目前的字元編碼 '{2}' 編碼的位元組序列不同，"
                    + "附加後整個檔案將失去一致性。"
                    + "若變更字元編碼確實是您的本意，請指定 -AllowEncodingChange。",

                [MessageKey.DetectedCodePageNotAvailable] =
                    "已將字元編碼判斷為字碼頁 {0}（'{1}'），但這個 .NET 執行階段並未提供該字碼頁，"
                    + "因此無法處理這個檔案。請以 -Encoding 明確指定字元編碼。",

                [MessageKey.InvalidCulture] =
                    "無法將 '{0}' 解譯為文化特性名稱。請指定像 'ja-JP' 或 'ko-KR' 這樣的文化特性名稱，"
                    + "或省略 -Culture 以使用目前環境的文化特性。",

                [MessageKey.InvalidStrategy] =
                    "無法將 '{0}' 解譯為判斷方式。"
                    + "請指定 'Combined'、'NativeOnly' 或 'UtfUnknownOnly'。",

                [MessageKey.FileIsReadOnly] =
                    "'{0}' 為唯讀。若要寫入，請指定 -Force。唯讀屬性會在寫入後還原。",

                [MessageKey.NoClobberFileExists] =
                    "檔案 '{0}' 已存在。由於指定了 -NoClobber，因此不會覆寫。",

                [MessageKey.WildcardMatchedNoFile] =
                    "沒有符合 '{0}' 的檔案。包含萬用字元的路徑必須恰好符合一個現有檔案。若要建立名稱包含萬用字元的檔案，請使用 -LiteralPath。",

                [MessageKey.WildcardMatchedMultipleFiles] =
                    "'{0}' 符合 {1} 個檔案。包含萬用字元的路徑必須恰好符合一個檔案。",

                [MessageKey.ParentDirectoryNotFound] =
                    "上層目錄不存在，因此無法寫入 '{0}'。",

                [MessageKey.AllowEncodingChangeWithoutAppend] =
                    "-AllowEncodingChange 僅在與 -Append 一起指定時才有意義，因此將予以忽略。",

                [MessageKey.NoConversionSpecified] =
                    "未指定要轉換的內容。請至少指定 -Encoding、-EncodingFrom、-Bom、-LineBreak 其中之一。",

                [MessageKey.AutoNotAllowedForConvertContent] =
                    "Convert-ProbedContent 的 -Encoding 不可指定 Auto。若要保留來源檔案的字元編碼，請省略 -Encoding。",

                [MessageKey.BomConflictsWithEncoding] =
                    "字元編碼 '{0}' 與 -Bom {1} 互相矛盾。請只指定其中之一，或使兩者一致。",

                [MessageKey.BomNotSupportedByEncoding] =
                    "字元編碼 '{0}' 無法擁有 BOM，因此無法套用 -Bom Add。擁有 BOM 的僅有 utf8 / unicode / bigendianunicode "
                    + "/ utf32 / bigendianutf32 這五個系列。",

                [MessageKey.DestinationFolderNotFound] =
                    "目的地資料夾 '{0}' 不存在。-Destination 必須是現有的資料夾（不會建立資料夾）。",

                [MessageKey.InvalidSourceBytes] =
                    "'{0}' 含有以字元編碼 '{1}' 而言無效的位元組序列（位元組位置 {2}：{3}），因此未轉換。檔案未變更。若字元編碼判斷有誤，請以 -SourceEncoding "
                    + "指定。",

                [MessageKey.UnrepresentableCharacter] =
                    "'{0}' 第 {4} 行第 {5} 欄的字元 {2} '{3}' 無法以字元編碼 '{1}' 表示，因此未轉換。檔案未變更。",

                [MessageKey.DestinationFileExists] =
                    "檔案 '{0}' 已存在。若要覆寫，請指定 -Force。",

                [MessageKey.DestinationNameConflict] =
                    "'{0}' 已作為另一個同名檔案的輸出目標，因此未轉換 '{1}'。-Destination 不會保留資料夾結構。",

                [MessageKey.DestinationIsSource] =
                    "目的地就是來源檔案本身，因此未轉換 '{0}'。若要就地轉換，請省略 -Destination。",

                [MessageKey.SkippedDirectory] =
                    "已略過目錄 '{0}'。",

                [MessageKey.DetectionLinesTooLarge] =
                    "'{0}' 的前 {1} 行超過可判斷字元編碼的大小（必須小於 2 GB），因此無法判斷。沒有換行的檔案會讀取整個檔案。"
                    + "請減少 -DetectionLineCount。在具有 -Encoding 的命令中，也可以明確指定字元編碼。",
                [MessageKey.DetectionVerificationFailed] =
                    "'{0}' 含有以前 {2} 行判斷的字元編碼 '{1}' 而言無效的位元組序列（位元組位置 {3}：{4}），因此未處理。"
                    + "請增加 -DetectionLineCount，或以 -Encoding 指定字元編碼。",

                [MessageKey.CsvHeaderAlreadyPresent] =
                    "欄位名稱 \"{0}\" 在標頭中指定了多次（欄位名稱不區分大小寫）。",
                [MessageKey.CsvUnspecifiedHeader] =
                    "有未指定的欄位名稱。已使用以 \"H\" 開頭的預設名稱取代缺少的欄位名稱。",
                [MessageKey.CsvAppendMismatchedPropertyName] =
                    "無法將 CSV 附加到下列檔案：{1}。附加的物件沒有對應下列欄位的屬性：{0}。"
                    + "若要在屬性不一致的情況下繼續，請指定 -Force 後再執行一次。",

                [MessageKey.FileTooLargeToDetectUseLineCount] =
                    "'{0}' 太大，無法判斷字元編碼（判斷會讀取整個檔案，因此檔案必須小於 2 GB）。"
                    + "若要以開頭的行判斷，請指定 -DetectionLineCount；若要明確指定字元編碼，請指定 -Encoding。",
                [MessageKey.InvalidBytesDuringImport] =
                    "讀取 '{0}' 時發現以字元編碼 '{1}' 而言無效的位元組序列（位元組位置 {2}：{3}），因此已中止讀取。"
                    + "已輸出的記錄無法取消。檔案可能在驗證之後被變更。",

                [MessageKey.CannotSpecifyQuoteFieldsAndUseQuotes] =
                    "-UseQuotes 與 -QuoteFields 不能同時指定。請只指定其中一個。",
                [MessageKey.CannotSpecifyPathAndLiteralPath] =
                    "請只指定 -Path 與 -LiteralPath 其中一個。",

                // ---- 1.3.0 第五次 ----

                [MessageKey.UnrepresentableCharacterOnWrite] =
                    "字元 {2} '{3}' 無法以字元編碼 '{1}' 表示，因此未寫入 '{0}'。檔案未變更。",
                [MessageKey.DetectionVerificationIso2022Escape] =
                    "'{0}' 的前 {2} 行只有 ASCII 字元，因此以 '{1}' 讀取，但在位元組位置 {3} 有 ISO-2022 跳脫序列（{4}），因此未處理。"
                    + "請移除 -DetectionLineCount，或將行數增加到包含跳脫序列的行。",
                [MessageKey.CsvAppendEmptyPropertyName] =
                    "無法將 CSV 附加到下列檔案：{0}。檔案的標頭有空的欄位名稱，無法作為屬性名稱使用。",

                // ---- 1.3.0 第五次の後半 ----

                [MessageKey.WildcardMatchedNoFileForWrite] =
                    "沒有符合 '{0}' 的檔案，因此未寫入。若要建立名稱包含萬用字元的檔案，請使用 -LiteralPath。",
                [MessageKey.AlternateDataStreamNotSupported] =
                    "'{0}' 指定了替代資料流。只能讀寫檔案的主要資料流（路徑本身，或在路徑後加上 '::$DATA'）。",
                [MessageKey.RestoreFromBackupFailed] =
                    "無法還原 '{0}'，原始檔案處於寫入到一半的狀態。原始內容已複製到 '{1}'，請從這個複本還原。",

                [MessageKey.LinkTargetNotFound] =
                    "'{0}' 是目標不存在的符號連結，因此未寫入。與標準的 Set-Content 相同，不會建立中斷連結的目標。請寫入目標路徑，或使用 Out-ProbedFile。",

                [MessageKey.WriteAccessDenied] =
                    "存取 '{0}' 遭到拒絕，因此未寫入。請確認檔案（若為新檔案，則為資料夾）的存取權限。",
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

                [MessageKey.EncodingAndEncodingFromAreExclusive] =
                    "-Encoding 与 -EncodingFrom 不能同时指定，"
                    + "因为两者都会决定字符编码与 BOM。",

                [MessageKey.AutoEncodingRequiresExistingFile] =
                    "文件 '{0}' 不存在，因此无法继承字符编码。"
                    + "请用 -Encoding 明确指定，或用 -EncodingFrom 从其他文件继承。",

                [MessageKey.NoNewlineIgnoresLineBreak] =
                    "已指定 -NoNewline，因此不会使用 -LineBreak 的指定。"
                    + "-LineBreak 用于选择作为换行输出的字符，"
                    + "而 -NoNewline 则抑制换行本身的输出。",

                [MessageKey.EncodingChangeOnAppend] =
                    "无法以字符编码 '{1}' 追加至 '{0}'。以 '{1}' 编码的字节序列，"
                    + "与该文件当前的字符编码 '{2}' 编码的字节序列不同，"
                    + "追加后整个文件将失去一致性。"
                    + "若变更字符编码确实是您的本意，请指定 -AllowEncodingChange。",

                [MessageKey.DetectedCodePageNotAvailable] =
                    "已将字符编码判断为代码页 {0}（'{1}'），但该 .NET 运行时并未提供该代码页，"
                    + "因此无法处理该文件。请用 -Encoding 明确指定字符编码。",

                [MessageKey.InvalidCulture] =
                    "无法将 '{0}' 解释为区域性名称。请指定像 'ja-JP' 或 'ko-KR' 这样的区域性名称，"
                    + "或省略 -Culture 以使用当前环境的区域性。",

                [MessageKey.InvalidStrategy] =
                    "无法将 '{0}' 解释为判定方式。"
                    + "请指定 'Combined'、'NativeOnly' 或 'UtfUnknownOnly'。",

                [MessageKey.FileIsReadOnly] =
                    "'{0}' 为只读。若要写入，请指定 -Force。只读属性会在写入后恢复。",

                [MessageKey.NoClobberFileExists] =
                    "文件 '{0}' 已存在。由于指定了 -NoClobber，因此不会覆盖。",

                [MessageKey.WildcardMatchedNoFile] =
                    "没有与 '{0}' 匹配的文件。包含通配符的路径必须恰好匹配一个现有文件。若要创建名称包含通配符的文件，请使用 -LiteralPath。",

                [MessageKey.WildcardMatchedMultipleFiles] =
                    "'{0}' 匹配了 {1} 个文件。包含通配符的路径必须恰好匹配一个文件。",

                [MessageKey.ParentDirectoryNotFound] =
                    "父目录不存在，因此无法写入 '{0}'。",

                [MessageKey.AllowEncodingChangeWithoutAppend] =
                    "-AllowEncodingChange 仅在与 -Append 一起指定时才有意义，因此将被忽略。",

                [MessageKey.NoConversionSpecified] =
                    "未指定要转换的内容。请至少指定 -Encoding、-EncodingFrom、-Bom、-LineBreak 中的一个。",

                [MessageKey.AutoNotAllowedForConvertContent] =
                    "Convert-ProbedContent 的 -Encoding 不能指定 Auto。若要保留源文件的字符编码，请省略 -Encoding。",

                [MessageKey.BomConflictsWithEncoding] =
                    "字符编码 '{0}' 与 -Bom {1} 相互矛盾。请只指定其中之一，或使两者一致。",

                [MessageKey.BomNotSupportedByEncoding] =
                    "字符编码 '{0}' 无法带有 BOM，因此无法应用 -Bom Add。带有 BOM 的只有 utf8 / unicode / bigendianunicode "
                    + "/ utf32 / bigendianutf32 这五个系列。",

                [MessageKey.DestinationFolderNotFound] =
                    "目标文件夹 '{0}' 不存在。-Destination 必须是现有的文件夹（不会创建文件夹）。",

                [MessageKey.InvalidSourceBytes] =
                    "'{0}' 含有以字符编码 '{1}' 而言无效的字节序列（字节位置 {2}：{3}），因此未转换。文件未更改。若字符编码判断有误，请用 -SourceEncoding "
                    + "指定。",

                [MessageKey.UnrepresentableCharacter] =
                    "'{0}' 第 {4} 行第 {5} 列的字符 {2} '{3}' 无法用字符编码 '{1}' 表示，因此未转换。文件未更改。",

                [MessageKey.DestinationFileExists] =
                    "文件 '{0}' 已存在。若要覆盖，请指定 -Force。",

                [MessageKey.DestinationNameConflict] =
                    "'{0}' 已作为另一个同名文件的输出目标，因此未转换 '{1}'。-Destination 不会保留文件夹结构。",

                [MessageKey.DestinationIsSource] =
                    "目标就是源文件本身，因此未转换 '{0}'。若要就地转换，请省略 -Destination。",

                [MessageKey.SkippedDirectory] =
                    "已跳过目录 '{0}'。",

                [MessageKey.DetectionLinesTooLarge] =
                    "'{0}' 的前 {1} 行超过可判断字符编码的大小（必须小于 2 GB），因此无法判断。没有换行的文件会读取整个文件。"
                    + "请减小 -DetectionLineCount。在具有 -Encoding 的命令中，也可以明确指定字符编码。",
                [MessageKey.DetectionVerificationFailed] =
                    "'{0}' 含有以前 {2} 行判断的字符编码 '{1}' 而言无效的字节序列（字节位置 {3}：{4}），因此未处理。"
                    + "请增大 -DetectionLineCount，或用 -Encoding 指定字符编码。",

                [MessageKey.CsvHeaderAlreadyPresent] =
                    "列名 \"{0}\" 在标头中指定了多次（列名不区分大小写）。",
                [MessageKey.CsvUnspecifiedHeader] =
                    "有未指定的列名。已使用以 \"H\" 开头的默认名称代替缺少的列名。",
                [MessageKey.CsvAppendMismatchedPropertyName] =
                    "无法将 CSV 追加到以下文件：{1}。追加的对象没有与以下列对应的属性：{0}。"
                    + "若要在属性不一致的情况下继续，请指定 -Force 后重新运行。",

                [MessageKey.FileTooLargeToDetectUseLineCount] =
                    "'{0}' 太大，无法判断字符编码（判断会读取整个文件，因此文件必须小于 2 GB）。"
                    + "若要根据开头的行判断，请指定 -DetectionLineCount；若要明确指定字符编码，请指定 -Encoding。",
                [MessageKey.InvalidBytesDuringImport] =
                    "读取 '{0}' 时发现以字符编码 '{1}' 而言无效的字节序列（字节位置 {2}：{3}），因此已中止读取。"
                    + "已输出的记录无法撤销。文件可能在验证之后被更改。",

                [MessageKey.CannotSpecifyQuoteFieldsAndUseQuotes] =
                    "-UseQuotes 与 -QuoteFields 不能同时指定。请只指定其中一个。",
                [MessageKey.CannotSpecifyPathAndLiteralPath] =
                    "请只指定 -Path 与 -LiteralPath 其中一个。",

                // ---- 1.3.0 第五次 ----

                [MessageKey.UnrepresentableCharacterOnWrite] =
                    "字符 {2} '{3}' 无法用字符编码 '{1}' 表示，因此未写入 '{0}'。文件未更改。",
                [MessageKey.DetectionVerificationIso2022Escape] =
                    "'{0}' 的前 {2} 行只有 ASCII 字符，因此按 '{1}' 读取，但在字节位置 {3} 有 ISO-2022 转义序列（{4}），因此未处理。"
                    + "请去掉 -DetectionLineCount，或将行数增加到包含转义序列的行。",
                [MessageKey.CsvAppendEmptyPropertyName] =
                    "无法将 CSV 追加到以下文件：{0}。文件的标题中有空的列名，无法用作属性名称。",

                // ---- 1.3.0 第五次の後半 ----

                [MessageKey.WildcardMatchedNoFileForWrite] =
                    "没有与 '{0}' 匹配的文件，因此未写入。若要创建名称包含通配符的文件，请使用 -LiteralPath。",
                [MessageKey.AlternateDataStreamNotSupported] =
                    "'{0}' 指定了备用数据流。只能读写文件的主数据流（路径本身，或在路径后加上 '::$DATA'）。",
                [MessageKey.RestoreFromBackupFailed] =
                    "无法还原 '{0}'，原始文件处于写入到一半的状态。原始内容已复制到 '{1}'，请从这个副本还原。",

                [MessageKey.LinkTargetNotFound] =
                    "'{0}' 是目标不存在的符号链接，因此未写入。与标准的 Set-Content 相同，不会创建断开链接的目标。请写入目标路径，或使用 Out-ProbedFile。",

                [MessageKey.WriteAccessDenied] =
                    "访问 '{0}' 被拒绝，因此未写入。请确认文件（若为新文件，则为文件夹）的访问权限。",
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
        /// <remarks>
        /// <para>
        /// 文字エンコーディング判定のカルチャーゲート
        /// （<see cref="EncodingDetector.ResolveEastAsianLegacyRegion(string)"/>）と同じ規則を使う。
        /// カルチャー名をサブタグに分解して判定し、<see cref="CultureInfo.TwoLetterISOLanguageName"/> や
        /// 親カルチャーのチェーンは使わない。これらは .NET Framework（NLS）と .NET Core（ICU）とで
        /// 名前や親が異なり、<c>zh_HK</c> が PowerShell 7 では簡体字、5.1 では繁体字になっていた。
        /// また <c>yue</c>（広東語）を判定側は香港として扱うのに、メッセージは英語になっていた。
        /// </para>
        /// <para>
        /// 香港・マカオ・広東語には台湾と同じ繁体字の文面を使う（B 案）。
        /// Microsoft は Windows の zh-HK 言語パックの提供をやめて zh-TW を案内しており、
        /// 香港の繁体字 UI で実際に表示されるのは台湾向けの文面であるため。
        /// 香港用の文面を持つ場合は、辞書と <see cref="Catalogs"/> の行を足し、
        /// <see cref="LanguageFromRegion"/> の香港の行を変えればよい。
        /// </para>
        /// </remarks>
        public static string ResolveLanguage(CultureInfo? culture)
        {
            if (culture == null)
            {
                return English;
            }

            return LanguageFromRegion(EncodingDetector.ResolveEastAsianLegacyRegion(culture.Name));
        }

        /// <summary>
        /// 判定のカルチャー圏を、メッセージの言語に写す
        /// </summary>
        private static string LanguageFromRegion(EncodingDetector.EastAsianLegacyRegion region)
        {
            switch (region)
            {
                case EncodingDetector.EastAsianLegacyRegion.Japanese:
                    return Japanese;
                case EncodingDetector.EastAsianLegacyRegion.Korean:
                    return Korean;
                case EncodingDetector.EastAsianLegacyRegion.ChineseSimplified:
                    return ChineseSimplified;
                case EncodingDetector.EastAsianLegacyRegion.ChineseTraditional:
                    return ChineseTraditional;
                case EncodingDetector.EastAsianLegacyRegion.ChineseHongKong:
                    // B 案: 香港用の辞書は持たず、台湾と同じ繁体字の文面を使う
                    return ChineseTraditional;
                default:
                    return English;
            }
        }
    }
}