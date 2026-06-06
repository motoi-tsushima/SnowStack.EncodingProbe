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
        /// <summary>
        /// 文字エンコーディング判定のオプション
        /// </summary>
        public sealed class EncodingDetectorOptions
        {
            public DetectionStrategy Strategy { get; set; } = DetectionStrategy.Combined;
            public string? Culture { get; set; } = null;
        }

        /// <summary>
        /// 文字エンコーディング判定の戦略
        /// </summary>
        public enum DetectionStrategy
        {
            Combined,       // デフォルト：両方を統合
            UtfUnknownOnly, // UTF.Unknownのみ
            NativeOnly      // 独自実装のみ
        }

        /// <summary>
        /// 文字エンコーディングを判定する
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static EncodingInfomation Detect(byte[] buffer, EncodingDetectorOptions options)
        {
            EncodingInfomation encInfo;
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
        public static EncodingInfomation Detect(Stream stream, EncodingDetectorOptions options)
        {
            EncodingInfomation encInfo;
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
        public static EncodingInfomation Detect(string filePath, EncodingDetectorOptions options = null)
        {
            EncodingInfomation encInfo;

            if (options == null)
            {
                encInfo = NormalDetectEncoding(filePath);
                return encInfo;
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
        /// <returns></returns>
        public static EncodingInfomation DetectEncoding(byte[] buffer)
        {
            EncodingInfomation encInfo;
            EncodingDetector encDetec = new EncodingDetector(buffer);
            encInfo = encDetec.Detection();
            return encInfo;
        }
        
        /// <summary>
        /// 文字エンコーディングを判定する（独自実装）
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static EncodingInfomation DetectEncoding(Stream stream)
        {
            EncodingInfomation encInfo;
            EncodingDetector encDetec = new EncodingDetector(stream);
            encInfo = encDetec.Detection();
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（独自実装）
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static EncodingInfomation DetectEncoding(string filePath)
        {
            EncodingInfomation encInfo;
            EncodingDetector encDetec = new EncodingDetector(filePath);
            encInfo = encDetec.Detection();
            return encInfo;
        }

        /// <summary>
        /// 文字エンコーディングを判定する（UTF.Unknown）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public static EncodingInfomation DetectUtfUnknown(byte[] buffer)
        {
            EncodingInfomation encInfo = new EncodingInfomation();

            var result = CharsetDetector.DetectFromBytes(buffer);
            if (result != null && result.Detected != null)
            {
                if (result.Detected.Confidence > 0.5)
                {
                    try
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = result.Detected.Encoding.CodePage;
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
        public static EncodingInfomation DetectUtfUnknown(Stream stream)
        {
            EncodingInfomation encInfo = new EncodingInfomation();

            var result = CharsetDetector.DetectFromStream(stream);
            if (result != null && result.Detected != null)
            {
                if (result.Detected.Confidence > 0.5)
                {
                    try
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = result.Detected.Encoding.CodePage;
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
        public static EncodingInfomation DetectUtfUnknown(string filePath)
        {
            EncodingInfomation encInfo = new EncodingInfomation();

            var result = CharsetDetector.DetectFromFile(filePath);
            if (result != null && result.Detected != null)
            {
                if (result.Detected.Confidence > 0.5)
                {
                    try
                    {
                        encInfo.EncodingName = result.Detected.EncodingName;
                        encInfo.CodePage = result.Detected.Encoding.CodePage;
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
        public static EncodingInfomation NormalDetectEncoding(byte[] buffer)
        {
            EncodingInfomation encInfo;

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
        public static EncodingInfomation NormalDetectEncoding(Stream stream)
        {
            EncodingInfomation encInfo;

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
        public static EncodingInfomation NormalDetectEncoding(string filePath)
        {
            EncodingInfomation encInfo;

            encInfo = DetectEncoding(filePath);
            if (encInfo.CodePage < 0)
            {
                encInfo = DetectUtfUnknown(filePath);
            }
            return encInfo;
        }

    }
}
