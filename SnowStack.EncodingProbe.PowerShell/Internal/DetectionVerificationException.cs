using System;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 先頭 N 行で判定した文字エンコーディングで、ファイル全体を厳密に復号できなかったことを表す例外（1.3.0 仕様書 4.3）。
    /// </summary>
    /// <remarks>
    /// Import-ProbedCsv は非終了エラー、Export-ProbedCsv は終了エラーとして報告する。ID はどちらも <see cref="ErrorId"/>。
    /// </remarks>
    internal sealed class DetectionVerificationException : Exception
    {
        /// <summary>ErrorRecord に設定するエラー ID</summary>
        public const string ErrorId = "DetectionVerificationFailed";

        public DetectionVerificationException(string message, string path)
            : base(message)
        {
            this.Path = path;
        }

        /// <summary>対象ファイルのパス</summary>
        public string Path { get; }
    }
}
