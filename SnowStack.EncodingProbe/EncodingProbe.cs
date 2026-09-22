using System;
using System.Collections.Generic;
using System.Text;
using UtfUnknown;

namespace SnowStack.EncodingProbe
{
    /// <summary>
    /// 文字エンコーディング判定のためのクラス
    /// </summary>
    public static class EncodingProbe
    {
        public static readonly string License =
 @"MIT License

Copyright (c) 2026 motoi.tsushima

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE OTHER DEALINGS IN THE
SOFTWARE.

https://github.com/motoi-tsushima/SnowStack.EncodingProbe

SnowStack.EncodingProbe includes the following third-party component.


UTF.Unknown
-----------

Homepage:    https://github.com/CharsetDetector/UTF-unknown
Package:     https://www.nuget.org/packages/UTF.Unknown/
Version:     2.7.0 (net10.0 build) / 2.6.0 (net48 build)
Source code: https://github.com/CharsetDetector/UTF-unknown

UTF.Unknown is subject to the Mozilla Public License Version 1.1
(the ""License""). Alternatively, it may be used under the terms of
either the GNU General Public License Version 2 or later (the ""GPL""),
or the GNU Lesser General Public License Version 2.1 or later
(the ""LGPL"").

You may obtain a copy of the Mozilla Public License Version 1.1 at
https://www.mozilla.org/MPL/1.1/

SnowStack.EncodingProbe uses UTF.Unknown under the terms of the
Mozilla Public License Version 1.1. UTF.Unknown is referenced as an
unmodified binary NuGet package; no modifications have been made to
its source code. The complete source code of UTF.Unknown is publicly
available at the URL listed above.

Copyright notices contained in the UTF.Unknown source files are
retained by their respective holders and are not reproduced or
altered here.
";


        /// <summary>
        /// カルチャー名の妥当性を検証する
        /// </summary>
        /// <param name="culture">カルチャー名</param>
        /// <returns>検証済みのカルチャー名</returns>
        /// <exception cref="ArgumentException"></exception>
        private static string ValidateCulture(string culture)
        {
            if(string.IsNullOrEmpty(culture))
            {
                //空白設定ならば、何もしない。
                return string.Empty;
            }
            try
            {
                _ = new System.Globalization.CultureInfo(culture);
            }
            catch (System.Globalization.CultureNotFoundException ex)
            {
                throw new ArgumentException($"The specified culture '{culture}' is not supported.", ex);
            }
            return culture;
        }

        /// <summary>
        /// 文字エンコーディングを判定する
        /// </summary>
        /// <param name="buffer">テキストのバイト配列</param>
        /// <param name="options"><see cref="EncodingDetectorOptions"/> オブジェクト</param>
        /// <returns></returns>
        public static EncodingInformation Detect(byte[] buffer, EncodingDetectorOptions options = null)
        {
            EncodingInformation encInfo;
            if (options == null)
            {
                encInfo = NormalDetectEncoding(buffer);
                return encInfo;
            }
            
            if (!string.IsNullOrEmpty(options.Culture))
            {
                options.Culture = ValidateCulture(options.Culture);
            }

            switch (options.Strategy)
            {
                case DetectionStrategy.UtfUnknownOnly:
                    encInfo = DetectUtfUnknown(buffer, options.Culture);
                    break;
                case DetectionStrategy.NativeOnly:
                    encInfo = DetectEncoding(buffer, culture: options.Culture);
                    break;
                case DetectionStrategy.Combined:
                default:
                    encInfo = NormalDetectEncoding(buffer, options.Culture);
                    break;
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する
        /// </summary>
        /// <param name="stream">テキストのストリーム</param>
        /// <param name="options"><see cref="EncodingDetectorOptions"/> オブジェクト</param>
        /// <returns></returns>
        public static EncodingInformation Detect(Stream stream, EncodingDetectorOptions options = null)
        {
            EncodingInformation encInfo;
            if (options == null)
            {
                encInfo = NormalDetectEncoding(stream);
                return encInfo;
            }

            if (!string.IsNullOrEmpty(options.Culture))
            {
                options.Culture = ValidateCulture(options.Culture);
            }

            switch (options.Strategy)
            {
                case DetectionStrategy.UtfUnknownOnly:
                    encInfo = DetectUtfUnknown(stream, options.Culture);
                    break;
                case DetectionStrategy.NativeOnly:
                    encInfo = DetectEncoding(stream, culture: options.Culture);
                    break;
                case DetectionStrategy.Combined:
                default:
                    encInfo = NormalDetectEncoding(stream, options.Culture);
                    break;
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する
        /// </summary>
        /// <param name="filePath">テキストファイルのパス</param>
        /// <param name="options"><see cref="EncodingDetectorOptions"/> オブジェクト</param>
        /// <returns></returns>
        public static EncodingInformation Detect(string filePath, EncodingDetectorOptions options = null)
        {
            EncodingInformation encInfo;

            if (options == null)
            {
                encInfo = NormalDetectEncoding(filePath);
                return encInfo;
            }

            if (!string.IsNullOrEmpty(options.Culture))
            {
                options.Culture = ValidateCulture(options.Culture);
            }

            switch (options.Strategy)
            {
                case DetectionStrategy.UtfUnknownOnly:
                    encInfo = DetectUtfUnknown(filePath, options.Culture);
                    break;
                case DetectionStrategy.NativeOnly:
                    encInfo = DetectEncoding(filePath, culture: options.Culture);
                    break;
                case DetectionStrategy.Combined:
                default:
                    encInfo = NormalDetectEncoding(filePath, options.Culture);
                    break;
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（独自実装）
        /// </summary>
        /// <param name="buffer">テキストのバイト配列</param>
        /// <param name="detectionMode">検出モード</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation DetectEncoding(byte[] buffer, DetectionMode detectionMode = DetectionMode.Standard, string culture = null)
        {
            EncodingInformation encInfo;
            EncodingDetector encDetec = new EncodingDetector(buffer, detectionMode);
            encInfo = encDetec.Detection(culture);
            return encInfo;
        }
        
        /// <summary>
        /// 文字エンコーディングを判定する（独自実装）
        /// </summary>
        /// <param name="stream">テキストのストリーム</param>
        /// <param name="detectionMode">検出モード</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation DetectEncoding(Stream stream, DetectionMode detectionMode = DetectionMode.Standard, string culture = null)
        {
            EncodingInformation encInfo;
            EncodingDetector encDetec = new EncodingDetector(stream, detectionMode);
            encInfo = encDetec.Detection(culture);
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（独自実装）
        /// </summary>
        /// <param name="filePath">テキストファイルのパス</param>
        /// <param name="detectionMode">検出モード</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation DetectEncoding(string filePath, DetectionMode detectionMode = DetectionMode.Standard, string culture = null)
        {
            EncodingInformation encInfo;
            EncodingDetector encDetec = new EncodingDetector(filePath, detectionMode);
            encInfo = encDetec.Detection(culture);
            return encInfo;
        }

        /// <summary>
        /// UTF.Unknown の結果を「答えとして採用する」信頼度の下限
        /// </summary>
        /// <remarks>
        /// UtfUnknownOnly のとき、および独自判定が判定不能だったときの補完に使う。
        /// この値を超えていなければ判定不能（CodePage = -1）として扱う。
        /// </remarks>
        private const double UtfUnknownAdoptionThreshold = 0.5;

        /// <summary>
        /// 独自判定が出した旧マルチバイトの答えを「シングルバイトで上書きする」信頼度の下限
        /// </summary>
        /// <remarks>
        /// 採用の下限（<see cref="UtfUnknownAdoptionThreshold"/>）より高くしてある。
        /// 独自判定がすでに出した答えを覆すには、答えとして採用するより強い根拠を求める、という考え方である。
        ///
        /// UTF.Unknown は短い漢字列や HKSCS 入りの Big5 に対して 0.5 前後の信頼度でシングルバイトを返す。
        /// 実測では、旧マルチバイトのテキストに対してシングルバイトを返したときの最大が 0.5105
        /// （GBK 6 バイトの「这是一」→ tis-620）であり、
        /// 上書きが必要なシングルバイトのテキストの最小が 0.5695（ロシア語 cp1251 / koi8-r）であった。
        /// 値はこの間に取ってある。net48（UTF.Unknown 2.6.0）と net10.0（2.7.0）で測定値は一致する。
        /// </remarks>
        private const double SingleByteOverrideThreshold = 0.55;

        /// <summary>
        /// UTF.Unknown の判定結果を <see cref="EncodingInformation"/> に反映する
        /// </summary>
        /// <param name="encInfo">BOM と改行コードを判定済みの <see cref="EncodingInformation"/></param>
        /// <param name="result">UTF.Unknown の判定結果</param>
        /// <param name="confidence">UTF.Unknown が返した信頼度（判定できなかった場合は 0）</param>
        /// <returns>判定結果を反映した <paramref name="encInfo"/></returns>
        private static EncodingInformation ApplyUtfUnknownResult(EncodingInformation encInfo, DetectionResult result, out double confidence)
        {
            bool detected = (result != null && result.Detected != null);
            confidence = detected ? result.Detected.Confidence : 0.0;

            if (!detected || confidence <= UtfUnknownAdoptionThreshold)
            {
                encInfo.CodePage = -1;
                return encInfo;
            }

            try
            {
                encInfo.EncodingWebName = result.Detected.EncodingName;
                encInfo.CodePage = result.Detected.Encoding.CodePage;
                encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
            }
            catch (ArgumentException)
            {
                // UtfUnknownが検出したエンコーディングが.NETでサポートされていない場合
                // エンコーディング名だけを保存し、CodePageは-1にする
                encInfo.EncodingWebName = result.Detected.EncodingName;
                encInfo.CodePage = -1;
            }
            catch (NotSupportedException)
            {
                encInfo.EncodingWebName = result.Detected.EncodingName;
                encInfo.CodePage = -1;
            }

            return encInfo;
        }

        /// <summary>
        /// 独自判定が東アジアの旧マルチバイトとして返したコードページか判定する
        /// </summary>
        /// <remarks>
        /// これらの判定はバイト構造の妥当性しか見ていないため、
        /// 欧米のシングルバイトのテキストが偶然そのまま通ってしまうことがある。
        /// BOM・ISO-2022・ASCII・Unicode 系は根拠が確かなので対象に含めない。
        /// </remarks>
        /// <param name="codePage">独自判定が返したコードページ</param>
        /// <returns>true=バイト構造だけを根拠に決まったコードページである</returns>
        private static bool IsEastAsianLegacyMultiByteCodePage(int codePage)
        {
            switch (codePage)
            {
                case 932:    // Shift_JIS
                case 936:    // GBK
                case 949:    // CP949 (UHC)
                case 950:    // Big5
                case 20932:  // EUC-JP
                case 51936:  // EUC-CN (GB2312)
                case 51949:  // EUC-KR
                case 51950:  // EUC-TW
                case 54936:  // GB18030
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// UTF.Unknown の判定結果がシングルバイト文字エンコーディングか判定する
        /// </summary>
        /// <remarks>
        /// <c>Encoding.GetEncoding(codePage).IsSingleByte</c> は使わない。
        /// .NET Core では <c>CodePagesEncodingProvider</c> を登録していないと cp1251 などを解決できず、
        /// ホスト側の登録状況によって判定結果が変わってしまうためである。
        /// ここではマルチバイト側を表で除外し、残りをシングルバイトとして扱う。
        /// ASCII は独自判定が先に確定させるため、ここでは候補から外す。
        /// </remarks>
        /// <param name="codePage">UTF.Unknown が返したコードページ</param>
        /// <returns>true=シングルバイト文字エンコーディングである</returns>
        private static bool IsSingleByteCodePage(int codePage)
        {
            if (codePage < 0)
            {
                return false;
            }

            switch (codePage)
            {
                case 20127:  // us-ascii（独自判定が先に確定させる）
                case 1200:   // UTF-16LE
                case 1201:   // UTF-16BE
                case 12000:  // UTF-32LE
                case 12001:  // UTF-32BE
                case 65000:  // UTF-7
                case 65001:  // UTF-8
                case 932:    // Shift_JIS
                case 936:    // GBK
                case 949:    // CP949 (UHC)
                case 950:    // Big5
                case 20932:  // EUC-JP
                case 51932:  // EUC-JP（UTF.Unknown が返す方）
                case 51936:  // EUC-CN (GB2312)
                case 51949:  // EUC-KR
                case 51950:  // EUC-TW
                case 52936:  // HZ-GB-2312
                case 54936:  // GB18030
                case 50220:  // ISO-2022-JP
                case 50221:  // ISO-2022-JP (csISO2022JP)
                case 50222:  // ISO-2022-JP (SO/SI)
                case 50225:  // ISO-2022-KR
                case 50227:  // ISO-2022-CN
                case 50229:  // ISO-2022-TW
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 独自判定の結果を UTF.Unknown の結果で上書きすべきか判定する
        /// </summary>
        /// <remarks>
        /// 独自判定は、カルチャーが東アジア漢字文化圏のときだけ旧マルチバイトを判定する。
        /// その判定はバイト構造の妥当性しか見ていないため、たとえば日本語カルチャーの実行環境で
        /// ドイツ語の cp1252 のテキストを読むと、Shift_JIS の外字領域として構造が成立してしまい
        /// Shift_JIS と誤判定する。同じバイト列を UTF.Unknown がシングルバイト文字エンコーディングと
        /// 判定したのであれば、そちらのほうが確からしい。
        /// 逆に、本物の東アジアのテキストに対して UTF.Unknown が返すのはマルチバイトの符号化
        /// （932 / 51932 / 54936 など）か判定不能であり、この条件には該当しない。
        ///
        /// ただし、UTF.Unknown は短い漢字列や HKSCS 入りの Big5 に対しても 0.5 前後の信頼度で
        /// シングルバイトを返す。コードページの組み合わせだけで上書きを決めると、
        /// 0.5 をわずかに超えただけで正しい独自判定が覆ってしまうため、
        /// <see cref="SingleByteOverrideThreshold"/> を超える信頼度を要求する。
        /// </remarks>
        /// <param name="nativeInfo">独自判定の結果</param>
        /// <param name="utfUnknownInfo">UTF.Unknown の判定結果</param>
        /// <param name="confidence">UTF.Unknown が返した信頼度</param>
        /// <returns>true=UTF.Unknown の結果を採用する</returns>
        private static bool ShouldPreferUtfUnknown(EncodingInformation nativeInfo, EncodingInformation utfUnknownInfo, double confidence)
        {
            return IsEastAsianLegacyMultiByteCodePage(nativeInfo.CodePage)
                && IsSingleByteCodePage(utfUnknownInfo.CodePage)
                && confidence > SingleByteOverrideThreshold;
        }

        /// <summary>
        /// 独自判定が旧マルチバイトを返したときのクロスチェックを行い、採用する結果を返す
        /// </summary>
        /// <param name="nativeInfo">独自判定の結果</param>
        /// <param name="utfUnknownInfo">同じバイト列に対する UTF.Unknown の判定結果</param>
        /// <param name="confidence">UTF.Unknown が返した信頼度</param>
        /// <returns>採用する判定結果</returns>
        private static EncodingInformation ResolveEastAsianLegacyCrossCheck(
            EncodingInformation nativeInfo, EncodingInformation utfUnknownInfo, double confidence)
        {
            if (ShouldPreferUtfUnknown(nativeInfo, utfUnknownInfo, confidence))
            {
                return utfUnknownInfo;
            }

            return nativeInfo;
        }

        /// <summary>
        /// ストリームの現在位置から末尾までをバイト配列として読み出す
        /// </summary>
        /// <param name="stream">テキストのストリーム</param>
        /// <returns>読み出したバイト配列</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> が null の場合</exception>
        private static byte[] ReadAllBytes(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="buffer">テキストのバイト配列</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation DetectUtfUnknown(byte[] buffer, string culture = null)
        {
            double confidence;
            return DetectUtfUnknown(buffer, culture, out confidence);
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="buffer">テキストのバイト配列</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <param name="confidence">UTF.Unknown が返した信頼度（判定できなかった場合は 0）</param>
        /// <returns></returns>
        private static EncodingInformation DetectUtfUnknown(byte[] buffer, string culture, out double confidence)
        {
            EncodingInformation encInfo = DetectEncoding(buffer, DetectionMode.Skippable, culture);
            return ApplyUtfUnknownResult(encInfo, CharsetDetector.DetectFromBytes(buffer), out confidence);
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="stream">テキストのストリーム</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation DetectUtfUnknown(Stream stream, string culture = null)
        {
            // ストリームは一度しか読めないため、バイト配列に読み出してから両方の判定に渡す
            return DetectUtfUnknown(ReadAllBytes(stream), culture);
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="filePath">テキストファイルのパス</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation DetectUtfUnknown(string filePath, string culture = null)
        {
            double confidence;
            return DetectUtfUnknown(filePath, culture, out confidence);
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="filePath">テキストファイルのパス</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <param name="confidence">UTF.Unknown が返した信頼度（判定できなかった場合は 0）</param>
        /// <returns></returns>
        private static EncodingInformation DetectUtfUnknown(string filePath, string culture, out double confidence)
        {
            EncodingInformation encInfo = DetectEncoding(filePath, DetectionMode.Skippable, culture);
            return ApplyUtfUnknownResult(encInfo, CharsetDetector.DetectFromFile(filePath), out confidence);
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="buffer">テキストのバイト配列</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation NormalDetectEncoding(byte[] buffer, string culture = null)
        {
            EncodingInformation encInfo = DetectEncoding(buffer, culture: culture);

            if (encInfo.CodePage < 0)
            {
                return DetectUtfUnknown(buffer, culture);
            }

            if (IsEastAsianLegacyMultiByteCodePage(encInfo.CodePage))
            {
                double confidence;
                EncodingInformation utfUnknownInfo = DetectUtfUnknown(buffer, culture, out confidence);
                return ResolveEastAsianLegacyCrossCheck(encInfo, utfUnknownInfo, confidence);
            }

            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="stream">テキストのストリーム</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation NormalDetectEncoding(Stream stream, string culture = null)
        {
            // ストリームは一度しか読めないため、バイト配列に読み出してから両方の判定に渡す
            return NormalDetectEncoding(ReadAllBytes(stream), culture);
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="filePath">テキストファイルのパス</param>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns></returns>
        internal static EncodingInformation NormalDetectEncoding(string filePath, string culture = null)
        {
            EncodingInformation encInfo = DetectEncoding(filePath, culture: culture);

            if (encInfo.CodePage < 0)
            {
                return DetectUtfUnknown(filePath, culture);
            }

            if (IsEastAsianLegacyMultiByteCodePage(encInfo.CodePage))
            {
                double confidence;
                EncodingInformation utfUnknownInfo = DetectUtfUnknown(filePath, culture, out confidence);
                return ResolveEastAsianLegacyCrossCheck(encInfo, utfUnknownInfo, confidence);
            }

            return encInfo;
        }

    }
}
