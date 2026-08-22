using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 統一語彙（PowerShell 5.1 / 7.x で共通のエンコーディング名の体系）の解決を担う。
    /// </summary>
    /// <remarks>
    /// 解決順序は次のとおり。
    /// 1. Auto
    /// 2. 独自拡張名（*BOM / *NoBOM）
    /// 3. PowerShell 7.x フレンドリ名（裸名）
    /// 4. 数値 → Encoding.GetEncoding(int)
    /// 5. 文字列 → Encoding.GetEncoding(string)（WebName および .NET が持つ別名）
    /// 6. System.Text.Encoding / EncodingInformation は型で分岐
    ///
    /// 5 を最後に置くことで、shift-jis / sjis / ms_kanji といった別名表を自前で持たずに
    /// .NET へ委譲しつつ、本モジュールが定義した語彙を常に優先できる。
    /// </remarks>
    internal static class EncodingVocabulary
    {
        /// <summary>コードページ：UTF-7</summary>
        private const int CodePageUtf7 = 65000;

        /// <summary>コードページ：UTF-8</summary>
        private const int CodePageUtf8 = 65001;

        /// <summary>コードページ：UTF-16 リトルエンディアン</summary>
        private const int CodePageUtf16Le = 1200;

        /// <summary>コードページ：UTF-16 ビッグエンディアン</summary>
        private const int CodePageUtf16Be = 1201;

        /// <summary>コードページ：UTF-32 リトルエンディアン</summary>
        private const int CodePageUtf32Le = 12000;

        /// <summary>コードページ：UTF-32 ビッグエンディアン</summary>
        private const int CodePageUtf32Be = 12001;

        /// <summary>コードページ：US-ASCII</summary>
        private const int CodePageAscii = 20127;

        /// <summary>「対象ファイルからの検出に委ねる」ことを表す語彙</summary>
        private const string AutoName = "Auto";

        /// <summary>実行環境のANSIコードページを表す語彙</summary>
        private const string AnsiName = "ansi";

        /// <summary>実行環境のOEMコードページを表す語彙</summary>
        private const string OemName = "oem";

        /// <summary>BOM有りを表す接尾辞</summary>
        private const string BomSuffix = "BOM";

        /// <summary>BOM無しを表す接尾辞</summary>
        private const string NoBomSuffix = "NoBOM";

        /// <summary>
        /// 独自拡張名（BOM接尾辞付き）。BOM接尾辞を付けられるのはUnicode系5系統のみとする。
        /// </summary>
        private static readonly Dictionary<string, VocabularyEntry> ExtendedNames =
            new Dictionary<string, VocabularyEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["utf8BOM"] = new VocabularyEntry(CodePageUtf8, true),
                ["utf8NoBOM"] = new VocabularyEntry(CodePageUtf8, false),
                ["unicodeBOM"] = new VocabularyEntry(CodePageUtf16Le, true),
                ["unicodeNoBOM"] = new VocabularyEntry(CodePageUtf16Le, false),
                ["bigendianunicodeBOM"] = new VocabularyEntry(CodePageUtf16Be, true),
                ["bigendianunicodeNoBOM"] = new VocabularyEntry(CodePageUtf16Be, false),
                ["utf32BOM"] = new VocabularyEntry(CodePageUtf32Le, true),
                ["utf32NoBOM"] = new VocabularyEntry(CodePageUtf32Le, false),
                ["bigendianutf32BOM"] = new VocabularyEntry(CodePageUtf32Be, true),
                ["bigendianutf32NoBOM"] = new VocabularyEntry(CodePageUtf32Be, false),
            };

        /// <summary>
        /// PowerShell 7.x のフレンドリ名（裸名）。
        /// 裸名のBOM方針は PowerShell 7.x に合わせる（utf8 のみBOM無し、他のUnicode系はBOM有り）。
        /// utf8 だけは PowerShell 5.1 の標準語彙と意味が衝突するため、BOM方針を「未指定」として
        /// 書き込み時に拒否できるようにしている。
        /// </summary>
        private static readonly Dictionary<string, VocabularyEntry> BareNames =
            new Dictionary<string, VocabularyEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["utf8"] = new VocabularyEntry(CodePageUtf8, null),
                ["unicode"] = new VocabularyEntry(CodePageUtf16Le, true),
                ["bigendianunicode"] = new VocabularyEntry(CodePageUtf16Be, true),
                ["utf32"] = new VocabularyEntry(CodePageUtf32Le, true),
                ["bigendianutf32"] = new VocabularyEntry(CodePageUtf32Be, true),
                ["ascii"] = new VocabularyEntry(CodePageAscii, false),
                ["utf7"] = new VocabularyEntry(CodePageUtf7, false),
            };

        /// <summary>
        /// BOM方針が未指定のまま解決されたUnicode系に提示する代替候補（BOM無し名, BOM有り名）
        /// </summary>
        private static readonly Dictionary<int, KeyValuePair<string, string>> BomSuffixSuggestions =
            new Dictionary<int, KeyValuePair<string, string>>
            {
                [CodePageUtf8] = new KeyValuePair<string, string>("utf8NoBOM", "utf8BOM"),
                [CodePageUtf16Le] = new KeyValuePair<string, string>("unicodeNoBOM", "unicodeBOM"),
                [CodePageUtf16Be] = new KeyValuePair<string, string>("bigendianunicodeNoBOM", "bigendianunicodeBOM"),
                [CodePageUtf32Le] = new KeyValuePair<string, string>("utf32NoBOM", "utf32BOM"),
                [CodePageUtf32Be] = new KeyValuePair<string, string>("bigendianutf32NoBOM", "bigendianutf32BOM"),
            };

        /// <summary>
        /// -Encoding に与えられた入力を <see cref="EncodingSpec"/> へ解決する。
        /// </summary>
        /// <param name="input">パラメータに与えられた値</param>
        /// <param name="usage">読み取り用途か書き込み用途か</param>
        /// <param name="allowAuto">Auto を許容するかどうか（ConvertTo-DotNetEncoding では false）</param>
        /// <exception cref="ArgumentTransformationMetadataException">
        /// 解決できない、または用途に対して許されない指定の場合
        /// </exception>
        public static EncodingSpec Resolve(object? input, EncodingUsage usage, bool allowAuto = true)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            object? value = UnwrapPSObject(input);
            EncodingSpec spec = ResolveCore(value, allowAuto);

            if (usage == EncodingUsage.Write)
            {
                ValidateForWrite(spec, Describe(value));
            }

            return spec;
        }

        /// <summary>
        /// 判定結果（<see cref="EncodingInformation"/>）から <see cref="EncodingSpec"/> を組み立てる。
        /// エンコーディング・BOMの有無・改行コードの3点をすべて引き継ぐ。
        /// </summary>
        /// <exception cref="ArgumentTransformationMetadataException">判定に失敗した情報が渡された場合</exception>
        public static EncodingSpec FromEncodingInformation(EncodingInformation information)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            if (information.CodePage < 0)
            {
                throw Error(ValidationMessages.UndetectedEncodingInformation(information.CodePage));
            }

            Encoding encoding = BuildEncoding(information.CodePage, information.Bom);
            return EncodingSpec.Create(encoding, information.Bom, information.LineBreak);
        }

        /// <summary>
        /// コードページとBOM方針から <see cref="Encoding"/> を組み立てる。
        /// </summary>
        /// <remarks>
        /// Encoding.GetEncoding("utf-8") および Encoding.UTF8 はBOM付きインスタンスを返すため、
        /// Unicode系は必ずコンストラクタで組み立ててBOM方針を確定させる。
        /// UTF-7 も .NET 5 以降では GetEncoding から取得できないためコンストラクタで組み立てる。
        /// </remarks>
        public static Encoding BuildEncoding(int codePage, bool emitBom)
        {
            switch (codePage)
            {
                case CodePageUtf7:
                    return CreateUtf7();
                case CodePageUtf8:
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: emitBom);
                case CodePageUtf16Le:
                    return new UnicodeEncoding(bigEndian: false, byteOrderMark: emitBom);
                case CodePageUtf16Be:
                    return new UnicodeEncoding(bigEndian: true, byteOrderMark: emitBom);
                case CodePageUtf32Le:
                    return new UTF32Encoding(bigEndian: false, byteOrderMark: emitBom);
                case CodePageUtf32Be:
                    return new UTF32Encoding(bigEndian: true, byteOrderMark: emitBom);
                default:
                    return Encoding.GetEncoding(codePage);
            }
        }

        /// <summary>
        /// UTF-7 のインスタンスを生成する。
        /// </summary>
        /// <remarks>
        /// .NET 5 以降では Encoding.GetEncoding(65000) / GetEncoding("utf-7") が
        /// NotSupportedException を投げるため、コンストラクタで直接組み立てる必要がある。
        /// UTF7Encoding 自体は廃止予定だが、仕様上 UTF-7 は読み取り専用の語彙として
        /// 提供する必要があり、PowerShell 5.1 と 7.x で同一の結果を返すためにも
        /// 実行環境によらずこの経路で生成する。
        /// </remarks>
#pragma warning disable SYSLIB0001 // UTF-7 は廃止予定だが、読み取り専用の語彙として提供する必要がある
        private static Encoding CreateUtf7() => new UTF7Encoding();
#pragma warning restore SYSLIB0001

        /// <summary>
        /// BOM方針をコンストラクタで表現すべきUnicode系コードページかどうか
        /// </summary>
        public static bool IsUnicodeCodePage(int codePage)
            => codePage == CodePageUtf8
            || codePage == CodePageUtf16Le
            || codePage == CodePageUtf16Be
            || codePage == CodePageUtf32Le
            || codePage == CodePageUtf32Be;

        /// <summary>
        /// 解決順序に従って入力を解決する（用途による検証は行わない）
        /// </summary>
        private static EncodingSpec ResolveCore(object? value, bool allowAuto)
        {
            if (value == null)
            {
                throw Error(ValidationMessages.NullEncoding());
            }

            // 経路6: 型で分岐する
            if (value is EncodingSpec alreadyResolved)
            {
                // パラメータ束縛が複数回走った場合に備えて、解決済みの値はそのまま通す
                return alreadyResolved;
            }

            if (value is EncodingInformation information)
            {
                return FromEncodingInformation(information);
            }

            if (value is Encoding encodingInstance)
            {
                // 利用者が自ら構築したインスタンスは、そのGetPreamble()の意図を尊重する
                return EncodingSpec.Create(encodingInstance, encodingInstance.GetPreamble().Length > 0);
            }

            // 経路4: CLRの数値型はそのままコードページとして扱う
            if (TryGetCodePageFromNumber(value, out int numericCodePage))
            {
                return FromCodePage(numericCodePage, Describe(value));
            }

            string text = (value as string ?? value.ToString() ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                throw Error(ValidationMessages.UnknownEncoding(Describe(value)));
            }

            // 経路1: Auto
            if (string.Equals(text, AutoName, StringComparison.OrdinalIgnoreCase))
            {
                if (!allowAuto)
                {
                    throw Error(ValidationMessages.AutoNotAllowedForConvert());
                }

                return EncodingSpec.Auto;
            }

            // 経路2: 独自拡張名
            if (ExtendedNames.TryGetValue(text, out VocabularyEntry extended))
            {
                return FromEntry(extended);
            }

            // 経路3: PowerShell 7.x フレンドリ名（裸名）
            if (TryResolveBareName(text, out EncodingSpec? bare))
            {
                return bare!;
            }

            // BOM接尾辞を許さない語彙に接尾辞が付けられていないかを確認する。
            // 経路4・5で語幹だけが解決されてしまう前に判定する必要がある（例: 932BOM の 932 部分）。
            if (TryGetBomSuffixStem(text, out string stem) && CanResolveWithoutSuffix(stem))
            {
                throw Error(ValidationMessages.BomSuffixNotAllowed(stem));
            }

            // 経路4: 数値文字列
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCodePage))
            {
                return FromCodePage(parsedCodePage, text);
            }

            // 経路5: WebName および .NET が持つ別名
            return FromWebName(text);
        }

        /// <summary>
        /// 裸名を解決する。ansi / oem は実行環境から解決するため個別に扱う。
        /// </summary>
        private static bool TryResolveBareName(string text, out EncodingSpec? spec)
        {
            if (string.Equals(text, AnsiName, StringComparison.OrdinalIgnoreCase))
            {
                spec = FromSystemCodePage(GetAnsiCodePage(), AnsiName);
                return true;
            }

            if (string.Equals(text, OemName, StringComparison.OrdinalIgnoreCase))
            {
                spec = FromSystemCodePage(GetOemCodePage(), OemName);
                return true;
            }

            if (BareNames.TryGetValue(text, out VocabularyEntry entry))
            {
                spec = FromEntry(entry);
                return true;
            }

            spec = null;
            return false;
        }

        /// <summary>
        /// 語彙表のエントリから <see cref="EncodingSpec"/> を組み立てる
        /// </summary>
        private static EncodingSpec FromEntry(VocabularyEntry entry)
        {
            // BOM方針が未指定の場合、実体としてはBOM無しで組み立てる。
            // 未指定であることは EncodingSpec.EmitBom が null であることで保持される。
            Encoding encoding = BuildEncoding(entry.CodePage, entry.EmitBom ?? false);
            return EncodingSpec.Create(encoding, entry.EmitBom);
        }

        /// <summary>
        /// コードページ数値から解決する（経路4）
        /// </summary>
        private static EncodingSpec FromCodePage(int codePage, string original)
        {
            if (IsUnicodeCodePage(codePage))
            {
                // 数値指定にはBOM方針が含まれないため、未指定として扱う
                return EncodingSpec.Create(BuildEncoding(codePage, false), null);
            }

            if (codePage == CodePageUtf7)
            {
                // .NET 5 以降では GetEncoding(65000) が使えないため、コンストラクタ経由で組み立てる
                return EncodingSpec.Create(CreateUtf7(), false);
            }

            try
            {
                return EncodingSpec.Create(Encoding.GetEncoding(codePage), false);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException)
            {
                throw Error(ValidationMessages.UnknownEncoding(original));
            }
        }

        /// <summary>
        /// WebName および .NET が持つ別名から解決する（経路5）
        /// </summary>
        private static EncodingSpec FromWebName(string text)
        {
            Encoding resolved;

            try
            {
                resolved = Encoding.GetEncoding(text);
            }
            catch (NotSupportedException)
            {
                // .NET 5 以降で GetEncoding(string) から無効化されているのは UTF-7 だけである。
                // 名前表は UTF-7 の別名を認識したうえでインスタンス生成を拒否しているため、
                // ここに到達するのは UTF-7 を指す名前が指定された場合に限られる。
                // .NET Framework 4.8 では例外にならないため、この分岐は PowerShell 7.x 側でのみ働き、
                // 結果として両バージョンで同じ解決結果になる。
                return EncodingSpec.Create(CreateUtf7(), false);
            }
            catch (ArgumentException)
            {
                throw Error(ValidationMessages.UnknownEncoding(text));
            }

            if (IsUnicodeCodePage(resolved.CodePage))
            {
                // GetEncoding が返すUnicode系はBOM付きインスタンスであり、
                // 名前側にBOM方針が含まれないため、未指定として組み立て直す
                return EncodingSpec.Create(BuildEncoding(resolved.CodePage, false), null);
            }

            return EncodingSpec.Create(resolved, false);
        }

        /// <summary>
        /// 実行環境から取得したコードページ（ansi / oem）を解決する
        /// </summary>
        private static EncodingSpec FromSystemCodePage(int codePage, string vocabulary)
        {
            if (codePage <= 0)
            {
                throw Error(ValidationMessages.CodePageNotAvailable(vocabulary));
            }

            try
            {
                return EncodingSpec.Create(Encoding.GetEncoding(codePage), false);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException)
            {
                throw Error(ValidationMessages.CodePageNotAvailable(vocabulary));
            }
        }

        /// <summary>システムのANSIコードページを取得する（取得できない場合は -1）</summary>
        private static int GetAnsiCodePage()
        {
            try
            {
                return CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            }
            catch (NotSupportedException)
            {
                return -1;
            }
        }

        /// <summary>システムのOEMコードページを取得する（取得できない場合は -1）</summary>
        private static int GetOemCodePage()
        {
            try
            {
                return CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            }
            catch (NotSupportedException)
            {
                return -1;
            }
        }

        /// <summary>
        /// 書き込み用途で許されない指定を弾く。
        /// ファイルを開く前に失敗させるため、パラメータ束縛の段階で呼び出される。
        /// </summary>
        private static void ValidateForWrite(EncodingSpec spec, string original)
        {
            if (spec.IsAuto)
            {
                // 書き込み先から継承するため、書き込み用途でも Auto は有効
                return;
            }

            if (spec.Encoding!.CodePage == CodePageUtf7)
            {
                throw Error(ValidationMessages.Utf7NotAllowedForWrite());
            }

            if (!spec.IsBomPolicyUnspecified)
            {
                return;
            }

            if (spec.Encoding.CodePage == CodePageUtf8)
            {
                throw Error(ValidationMessages.BareUtf8NotAllowedForWrite(original));
            }

            KeyValuePair<string, string> suggestion = BomSuffixSuggestions[spec.Encoding.CodePage];
            throw Error(ValidationMessages.BomPolicyUnspecifiedForWrite(
                original, suggestion.Key, suggestion.Value));
        }

        /// <summary>
        /// 文字列の末尾がBOM接尾辞かどうかを判定し、接尾辞を除いた語幹を返す
        /// </summary>
        private static bool TryGetBomSuffixStem(string text, out string stem)
        {
            // "NoBOM" は "BOM" で終わるため、先に判定する
            if (text.Length > NoBomSuffix.Length
                && text.EndsWith(NoBomSuffix, StringComparison.OrdinalIgnoreCase))
            {
                stem = text.Substring(0, text.Length - NoBomSuffix.Length);
                return true;
            }

            if (text.Length > BomSuffix.Length
                && text.EndsWith(BomSuffix, StringComparison.OrdinalIgnoreCase))
            {
                stem = text.Substring(0, text.Length - BomSuffix.Length);
                return true;
            }

            stem = string.Empty;
            return false;
        }

        /// <summary>
        /// 語幹が語彙として解決できるかどうか（BOM接尾辞の誤用を検出するために使う）
        /// </summary>
        private static bool CanResolveWithoutSuffix(string stem)
        {
            // 解決経路そのものを使うことで、語幹の判定が本体の解決規則と食い違わないようにする。
            // 語幹は元の文字列より必ず短くなるため、再帰は有限で終わる。
            try
            {
                _ = ResolveCore(stem, allowAuto: false);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException
                                           || exception is NotSupportedException
                                           || exception is ArgumentTransformationMetadataException)
            {
                return false;
            }
        }

        /// <summary>
        /// CLRの数値型からコードページを取り出す
        /// </summary>
        private static bool TryGetCodePageFromNumber(object value, out int codePage)
        {
            switch (value)
            {
                case int intValue:
                    codePage = intValue;
                    return true;
                case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                    codePage = (int)longValue;
                    return true;
                case short shortValue:
                    codePage = shortValue;
                    return true;
                case ushort ushortValue:
                    codePage = ushortValue;
                    return true;
                case byte byteValue:
                    codePage = byteValue;
                    return true;
                case sbyte sbyteValue:
                    codePage = sbyteValue;
                    return true;
                case uint uintValue when uintValue <= int.MaxValue:
                    codePage = (int)uintValue;
                    return true;
                default:
                    codePage = 0;
                    return false;
            }
        }

        /// <summary>
        /// PSObject に包まれた値を取り出す
        /// </summary>
        private static object? UnwrapPSObject(object? value)
        {
            while (value is PSObject psObject)
            {
                object baseObject = psObject.BaseObject;

                if (ReferenceEquals(baseObject, value))
                {
                    break;
                }

                value = baseObject;
            }

            return value;
        }

        /// <summary>
        /// エラーメッセージに埋め込むための、入力値の表示用文字列を返す
        /// </summary>
        private static string Describe(object? value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is Encoding encoding)
            {
                return encoding.WebName;
            }

            return value.ToString() ?? value.GetType().Name;
        }

        /// <summary>
        /// パラメータ束縛の段階で失敗させるための例外を生成する
        /// </summary>
        private static ArgumentTransformationMetadataException Error(string message)
            => new ArgumentTransformationMetadataException(message);

        /// <summary>
        /// 語彙表の1エントリ（コードページとBOM方針）
        /// </summary>
        private readonly struct VocabularyEntry
        {
            public VocabularyEntry(int codePage, bool? emitBom)
            {
                this.CodePage = codePage;
                this.EmitBom = emitBom;
            }

            /// <summary>コードページ</summary>
            public int CodePage { get; }

            /// <summary>BOM方針。null は未指定を表す。</summary>
            public bool? EmitBom { get; }
        }
    }
}