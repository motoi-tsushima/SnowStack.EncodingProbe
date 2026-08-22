using System;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 対象ファイルの文字エンコーディングを判定できなかったことを表す例外。
    /// </summary>
    /// <remarks>
    /// クラスライブラリ側の <see cref="SnowStack.EncodingProbe.EncodingDetectorException"/> とは別に、
    /// コマンドレットが ErrorRecord に変換するための例外として用いる。
    /// </remarks>
    internal sealed class EncodingDetectionException : Exception
    {
        public EncodingDetectionException(string message, string path)
            : base(message)
        {
            this.Path = path;
        }

        /// <summary>判定に失敗したファイルのパス</summary>
        public string Path { get; }
    }
}