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
 @"This software includes the following third-party components:

SnowStack.EncodingProbe
Copyright c 2026 motoi.tsushima
Licensed under MIT License
https://github.com/motoi-tsushima/SnowStack.EncodingProbe

UTF.Unknown
Copyright (c) 2018 Nikolay Pultsin
Licensed under MIT License
https://github.com/CharsetDetector/UTF-unknown
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
        /// <param name="buffer"></param>
        /// <param name="options"></param>
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
        /// <param name="stream"></param>
        /// <param name="options"></param>
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
        /// <param name="filePath"></param>
        /// <param name="options"></param>
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
        /// <param name="buffer"></param>
        /// <param name="detectionMode"></pa
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
        /// <param name="stream"></param>
        /// <param name="detectionMode"></param>
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
        /// <param name="filePath"></param>
        /// <param name="detectionMode"></param>
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
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        internal static EncodingInformation DetectUtfUnknown(byte[] buffer, string culture = null)
        {
            EncodingInformation encInfo = new EncodingInformation();

            encInfo = DetectEncoding(buffer, DetectionMode.Skippable, culture);

            var result = CharsetDetector.DetectFromBytes(buffer);
            if (result != null && result.Detected != null)
            {
                if (result.Detected.Confidence > 0.5)
                {
                    try
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = result.Detected.Encoding.CodePage;
                        encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                    }
                    catch (ArgumentException)
                    {
                        // UtfUnknownが検出したエンコーディングが.NETでサポートされていない場合
                        // エンコーディング名だけを保存し、CodePageは-1にする
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = -1;
                    }
                    catch (NotSupportedException)
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = -1;
                    }
                }
                else
                {
                    encInfo.CodePage = -1;
                }
            }
            else
            {
                encInfo.CodePage = -1;
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        internal static EncodingInformation DetectUtfUnknown(Stream stream, string culture = null)
        {
            EncodingInformation encInfo = new EncodingInformation();

            encInfo = DetectEncoding(stream, DetectionMode.Skippable, culture);

            var result = CharsetDetector.DetectFromStream(stream);
            if (result != null && result.Detected != null)
            {
                if (result.Detected.Confidence > 0.5)
                {
                    try
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = result.Detected.Encoding.CodePage;
                        encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                    }
                    catch (ArgumentException)
                    {
                        // UtfUnknownが検出したエンコーディングが.NETでサポートされていない場合
                        // エンコーディング名だけを保存し、CodePageは-1にする
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = -1;
                    }
                    catch (NotSupportedException)
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = -1;
                    }
                }
                else
                {
                    encInfo.CodePage = -1;
                }
            }
            else
            {
                encInfo.CodePage = -1;
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        internal static EncodingInformation DetectUtfUnknown(string filePath, string culture = null)
        {
            EncodingInformation encInfo = new EncodingInformation();

            encInfo = DetectEncoding(filePath, DetectionMode.Skippable, culture);

            var result = CharsetDetector.DetectFromFile(filePath);
            if (result != null && result.Detected != null)
            {
                if (result.Detected.Confidence > 0.5)
                {
                    try
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = result.Detected.Encoding.CodePage;
                        encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                    }
                    catch (ArgumentException)
                    {
                        // UtfUnknownが検出したエンコーディングが.NETでサポートされていない場合
                        // エンコーディング名だけを保存し、CodePageは-1にする
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = -1;
                    }
                    catch (NotSupportedException)
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = -1;
                    }
                }
                else
                {
                    encInfo.CodePage = -1;
                }
            }
            else
            {
                encInfo.CodePage = -1;
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        internal static EncodingInformation NormalDetectEncoding(byte[] buffer, string culture = null)
        {
            EncodingInformation encInfo;

            encInfo = DetectEncoding(buffer, culture: culture);
            if (encInfo.CodePage < 0)
            {
                encInfo = DetectUtfUnknown(buffer, culture);
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        internal static EncodingInformation NormalDetectEncoding(Stream stream, string culture = null)
        {
            EncodingInformation encInfo;

            encInfo = DetectEncoding(stream, culture: culture);
            if (encInfo.CodePage < 0)
            {
                encInfo = DetectUtfUnknown(stream, culture);
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        internal static EncodingInformation NormalDetectEncoding(string filePath, string culture = null)
        {
            EncodingInformation encInfo;

            encInfo = DetectEncoding(filePath, culture: culture);
            if (encInfo.CodePage < 0)
            {
                encInfo = DetectUtfUnknown(filePath, culture);
            }
            return encInfo;
        }

    }
}

