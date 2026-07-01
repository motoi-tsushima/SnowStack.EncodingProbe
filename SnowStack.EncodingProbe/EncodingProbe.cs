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
        /// カルチャーを設定する
        /// </summary>
        /// <param name="culture">カルチャー名</param>
        /// <returns>設定されたカルチャー名</returns>
        /// <exception cref="ArgumentException"></exception>
        private static string SettingCulture(string culture)
        {
            if(string.IsNullOrEmpty(culture))
            {
                //空白設定ならば、何もしない。
                return string.Empty;
            }
            else if(System.Threading.Thread.CurrentThread.CurrentCulture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase) &&
                    System.Threading.Thread.CurrentThread.CurrentUICulture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase))
            {
                // 既に設定されているカルチャーと同じ場合は何もしない
                return culture;
            }
            else
            {
                try
                {
#if NET
                    // Native AOT 対応: CurrentCulture と CurrentUICulture の両方を設定
                    System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
                    System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(culture);
                    // アプリケーション全体のデフォルトカルチャーも設定
                    System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo(culture);
                    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo(culture);
#else
                    // .NET Framework 4.8: 現在のスレッドのカルチャーを設定
                    System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
                    System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(culture);
#endif
                }
                catch (System.Globalization.CultureNotFoundException ex)
                {
                    throw new ArgumentException($"The specified culture '{culture}' is not supported.", ex);
                }
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
                options.Culture = SettingCulture(options.Culture);
            }

            switch (options.Strategy)
            {
                case DetectionStrategy.UtfUnknownOnly:
                    encInfo = DetectUtfUnknown(buffer);
                    break;
                case DetectionStrategy.NativeOnly:
                    encInfo = DetectEncoding(buffer);
                    break;
                case DetectionStrategy.Combined:
                default:
                    encInfo = NormalDetectEncoding(buffer);
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
                options.Culture = SettingCulture(options.Culture);
            }

            switch (options.Strategy)
            {
                case DetectionStrategy.UtfUnknownOnly:
                    encInfo = DetectUtfUnknown(stream);
                    break;
                case DetectionStrategy.NativeOnly:
                    encInfo = DetectEncoding(stream);
                    break;
                case DetectionStrategy.Combined:
                default:
                    encInfo = NormalDetectEncoding(stream);
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
                options.Culture = SettingCulture(options.Culture);
            }

            switch (options.Strategy)
            {
                case DetectionStrategy.UtfUnknownOnly:
                    encInfo = DetectUtfUnknown(filePath);
                    break;
                case DetectionStrategy.NativeOnly:
                    encInfo = DetectEncoding(filePath);
                    break;
                case DetectionStrategy.Combined:
                default:
                    encInfo = NormalDetectEncoding(filePath);
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
        public static EncodingInformation DetectEncoding(byte[] buffer, DetectionMode detectionMode = DetectionMode.Standard)
        {
            EncodingInformation encInfo;
            EncodingDetector encDetec = new EncodingDetector(buffer, detectionMode);
            encInfo = encDetec.Detection();
            return encInfo;
        }
        
        /// <summary>
        /// 文字エンコーディングを判定する（独自実装）
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static EncodingInformation DetectEncoding(Stream stream, DetectionMode detectionMode = DetectionMode.Standard)
        {
            EncodingInformation encInfo;
            EncodingDetector encDetec = new EncodingDetector(stream, detectionMode);
            encInfo = encDetec.Detection();
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（独自実装）
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="detectionMode"></param>
        /// <returns></returns>
        public static EncodingInformation DetectEncoding(string filePath, DetectionMode detectionMode = DetectionMode.Standard)
        {
            EncodingInformation encInfo;
            EncodingDetector encDetec = new EncodingDetector(filePath, detectionMode);
            encInfo = encDetec.Detection();
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public static EncodingInformation DetectUtfUnknown(byte[] buffer)
        {
            EncodingInformation encInfo = new EncodingInformation();

            encInfo = DetectEncoding(buffer, DetectionMode.Skippable);

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
        public static EncodingInformation DetectUtfUnknown(Stream stream)
        {
            EncodingInformation encInfo = new EncodingInformation();

            encInfo = DetectEncoding(stream, DetectionMode.Skippable);

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
        public static EncodingInformation DetectUtfUnknown(string filePath)
        {
            EncodingInformation encInfo = new EncodingInformation();

            encInfo = DetectEncoding(filePath, DetectionMode.Skippable);

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
        public static EncodingInformation NormalDetectEncoding(byte[] buffer)
        {
            EncodingInformation encInfo;

            encInfo = DetectEncoding(buffer);
            if (encInfo.CodePage < 0)
            {
                encInfo = DetectUtfUnknown(buffer);
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static EncodingInformation NormalDetectEncoding(Stream stream)
        {
            EncodingInformation encInfo;

            encInfo = DetectEncoding(stream);
            if (encInfo.CodePage < 0)
            {
                encInfo = DetectUtfUnknown(stream);
            }
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（両方を統合）
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static EncodingInformation NormalDetectEncoding(string filePath)
        {
            EncodingInformation encInfo;

            encInfo = DetectEncoding(filePath);
            if (encInfo.CodePage < 0)
            {
                encInfo = DetectUtfUnknown(filePath);
            }
            return encInfo;
        }

    }
}
