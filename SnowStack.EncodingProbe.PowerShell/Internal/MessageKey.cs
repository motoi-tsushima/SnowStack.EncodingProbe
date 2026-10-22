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

        // ---- 1.2.0: Out-ProbedFile ----

        /// <summary>書き込み先が読み取り専用で、-Force が指定されていない</summary>
        FileIsReadOnly,

        /// <summary>-NoClobber が指定され、書き込み先が既に存在する</summary>
        NoClobberFileExists,

        /// <summary>ワイルドカードを含むパスに一致するファイルが無い</summary>
        WildcardMatchedNoFile,

        /// <summary>ワイルドカードを含むパスが複数のファイルに一致した</summary>
        WildcardMatchedMultipleFiles,

        /// <summary>書き込み先の親ディレクトリが存在しない</summary>
        ParentDirectoryNotFound,

        /// <summary>-Append なしで -AllowEncodingChange が指定された（警告）</summary>
        AllowEncodingChangeWithoutAppend,

        // ---- 1.2.0: Convert-ProbedContent ----

        /// <summary>変換の指定（-Encoding / -EncodingFrom / -Bom / -LineBreak）が1つも無い</summary>
        NoConversionSpecified,

        /// <summary>Convert-ProbedContent の -Encoding に Auto が指定された</summary>
        AutoNotAllowedForConvertContent,

        /// <summary>BOM を明示する指定と -Bom が食い違う</summary>
        BomConflictsWithEncoding,

        /// <summary>BOM を持てない文字エンコーディングに -Bom Add が指定された</summary>
        BomNotSupportedByEncoding,

        /// <summary>-Destination のフォルダーが存在しない</summary>
        DestinationFolderNotFound,

        /// <summary>変換元に、その文字エンコーディングとして不正なバイト列がある</summary>
        InvalidSourceBytes,

        /// <summary>変換先の文字エンコーディングで表現できない文字がある</summary>
        UnrepresentableCharacter,

        /// <summary>-Destination に同名のファイルがあり、-Force が指定されていない</summary>
        DestinationFileExists,

        /// <summary>同じ実行の中で -Destination への出力ファイル名が重なった</summary>
        DestinationNameConflict,

        /// <summary>変換元と -Destination の出力先が同じファイルになった</summary>
        DestinationIsSource,

        /// <summary>ディレクトリを変換対象から除いた（詳細メッセージ）</summary>
        SkippedDirectory,

        // ---- 1.3.0: -DetectionLineCount と CSV ----

        /// <summary>先頭 N 行の切り出しが、判定できる大きさを超えた</summary>
        DetectionLinesTooLarge,

        /// <summary>先頭 N 行で判定した文字エンコーディングで、ファイル全体を復号できなかった</summary>
        DetectionVerificationFailed,

        /// <summary>CSV のヘッダーに同じ列名が複数ある</summary>
        CsvHeaderAlreadyPresent,

        /// <summary>CSV のヘッダーに空の列名があり、既定の名前で補った（警告）</summary>
        CsvUnspecifiedHeader,

        /// <summary>追記するオブジェクトに、追記先の列に当たるプロパティが無い</summary>
        CsvAppendMismatchedPropertyName,

        /// <summary>全体の判定の上限を超えるファイル（-DetectionLineCount を案内する。Import-ProbedCsv / Export-ProbedCsv）</summary>
        FileTooLargeToDetectUseLineCount,

        /// <summary>Import-ProbedCsv の解析の途中で、不正なバイト列が見つかった</summary>
        InvalidBytesDuringImport,

        /// <summary>Export-ProbedCsv で -QuoteFields と -UseQuotes が同時に指定された</summary>
        CannotSpecifyQuoteFieldsAndUseQuotes,

        /// <summary>Export-ProbedCsv で -Path と -LiteralPath が両方指定された、またはどちらも指定されなかった</summary>
        CannotSpecifyPathAndLiteralPath,

        // ---- 1.3.0 第五次 ----

        /// <summary>書き込み系のコマンドで、表現できない文字があったため書き込まなかった（ファイルは変更していない）</summary>
        UnrepresentableCharacterOnWrite,

        /// <summary>us-ascii を UTF-8 に読み替えて全体を検証したら、ISO-2022 のエスケープシーケンスがあった</summary>
        DetectionVerificationIso2022Escape,

        /// <summary>Export-ProbedCsv -Append -Force で、追記先のヘッダーに空の列名がある</summary>
        CsvAppendEmptyPropertyName,
    }
}