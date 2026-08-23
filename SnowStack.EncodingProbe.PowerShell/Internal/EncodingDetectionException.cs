using System;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 対象ファイルに使う文字エンコーディングを決められなかったことを表す例外。
    /// </summary>
    /// <remarks>
    /// クラスライブラリ側の <see cref="SnowStack.EncodingProbe.EncodingDetectorException"/> とは別に、
    /// コマンドレットが ErrorRecord に変換するための例外として用いる。
    /// <br/>
    /// 「判定できなかった」場合と「判定できたが実行環境がそのコードページを提供していない」場合とでは
    /// 利用者が取るべき対処が違うため、<see cref="ErrorId"/> で区別できるようにしている。
    /// </remarks>
    internal sealed class EncodingDetectionException : Exception
    {
        /// <summary>文字エンコーディングを判定できなかった場合のエラーID</summary>
        public const string DetectionFailedId = "EncodingDetectionFailed";

        /// <summary>判定はできたが、実行環境がそのコードページを提供していない場合のエラーID</summary>
        public const string CodePageNotAvailableId = "CodePageNotAvailable";

        public EncodingDetectionException(string message, string path)
            : this(message, path, DetectionFailedId)
        {
        }

        public EncodingDetectionException(string message, string path, string errorId)
            : base(message)
        {
            this.Path = path;
            this.ErrorId = errorId;
        }

        /// <summary>対象ファイルのパス</summary>
        public string Path { get; }

        /// <summary>ErrorRecord に設定するエラーID</summary>
        public string ErrorId { get; }
    }
}
