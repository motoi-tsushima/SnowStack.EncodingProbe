using System.Globalization;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// バイト列をエラーメッセージ用の文字列にする。
    /// </summary>
    /// <remarks>
    /// Convert-ProbedContent の InvalidSourceBytes と、-DetectionLineCount の検証の失敗（DetectionVerificationFailed）とで、
    /// 不正なバイト列を同じ形式で示すために共用する。
    /// </remarks>
    internal static class ByteSequenceText
    {
        /// <summary>
        /// バイト列を 16 進で表す（例: "82 A0"）
        /// </summary>
        public static string Format(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(bytes.Length * 3);

            foreach (byte value in bytes)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
