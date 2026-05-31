using System;

namespace SnowStack.EncodingProbe
{
    /// <summary>
    /// EncodingDetectorアプリケーション固有の例外クラス
    /// </summary>
    public class EncodingDetectorException : Exception
    {
        public EncodingDetectorException()
        {
        }

        public EncodingDetectorException(string message)
            : base(message)
        {
        }

        public EncodingDetectorException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
