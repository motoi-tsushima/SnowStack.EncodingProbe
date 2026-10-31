using System;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 書き込み先が、同じパイプラインで読み取り中のファイルと同じ実体だったことを表す例外（1.3.0 第五次の後半）。
    /// </summary>
    /// <remarks>
    /// 追記は「切り詰めずに開く → 開いたハンドルで実体を比べる → 書く」の順に行う（<see cref="ProbedFileWriter.Open"/>）。
    /// そこで一致したときに投げる。書き込み系のコマンドは、開く前の検査と同じ ID（<see cref="ErrorId"/>）で報告する。
    /// ファイルには何も書いていない。
    /// </remarks>
    internal sealed class SameFileRoundTripException : InvalidOperationException
    {
        /// <summary>ErrorRecord に設定するエラー ID（開く前の検査と同じ）</summary>
        public const string ErrorId = "SamePathRoundTrip";

        public SameFileRoundTripException(string path)
            : base(ValidationMessages.SamePathRoundTrip(path))
        {
        }
    }
}
