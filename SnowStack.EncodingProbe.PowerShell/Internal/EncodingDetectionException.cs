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

        /// <summary>ファイル全体を読んで判定するためのメモリが足りなかった場合のエラーID（1.3.0 第六次 B。BUG-0030）</summary>
        public const string NotEnoughMemoryId = "NotEnoughMemoryToDetect";

        /// <summary>
        /// ファイル全体を読んで判定する処理を行い、メモリ不足（<see cref="OutOfMemoryException"/>）を ID の付いた判定の失敗にする（1.3.0 第六次 B。BUG-0030）。
        /// </summary>
        /// <remarks>
        /// 全体の判定はファイルの大きさの数倍のメモリを使う（約 3.6 倍。1.4.0 で扱う）。Windows PowerShell 5.1 の 32 ビット版などで、
        /// 上限（2 GB）より小さなファイルでも生の OutOfMemoryException になっていた。
        /// </remarks>
        public static T GuardMemory<T>(string path, Func<T> body)
        {
            try
            {
                return body();
            }
            catch (OutOfMemoryException)
            {
                throw new EncodingDetectionException(ValidationMessages.NotEnoughMemoryToDetect(path), path, NotEnoughMemoryId);
            }
        }

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
