using System;
using System.Collections.Generic;
using System.Text;
using UtfUnknown;

namespace SnowStack.EncodingProbe
{
    public static class EncodingDetectorControl
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


        public static EncodingInfomation DetectEncoding(byte[] buffer)
        {
            EncodingInfomation encInfo;
            EncodingDetector encDetec = new EncodingDetector(buffer);
            encInfo = encDetec.Detection();
            return encInfo;
        }

        public static EncodingInfomation DetectEncoding(Stream stream)
        {
            EncodingInfomation encInfo;
            EncodingDetector encDetec = new EncodingDetector(stream);
            encInfo = encDetec.Detection();
            return encInfo;
        }

        public static EncodingInfomation DetectEncoding(string filePath)
        {
            EncodingInfomation encInfo;
            EncodingDetector encDetec = new EncodingDetector(filePath);
            encInfo = encDetec.Detection();
            return encInfo;
        }


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
