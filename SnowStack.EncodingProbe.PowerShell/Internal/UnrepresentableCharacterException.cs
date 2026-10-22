using System;
using System.Globalization;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 書き込む文字列に、書き込み先の文字エンコーディングで表現できない文字があったことを表す例外（1.3.0 第五次）。
    /// </summary>
    /// <remarks>
    /// 書き込み系の 4 コマンド（Set-ProbedContent / Add-ProbedContent / Out-ProbedFile / Export-ProbedCsv）が、
    /// <see cref="ErrorId"/>（Convert-ProbedContent と同じ ID）のエラーとして報告する。ファイルは元に戻してある。
    /// </remarks>
    internal sealed class UnrepresentableCharacterException : Exception
    {
        /// <summary>ErrorRecord に設定するエラー ID（Convert-ProbedContent の N4 と同じ）</summary>
        public const string ErrorId = "UnrepresentableCharacter";

        private UnrepresentableCharacterException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 符号化の例外から、最初の表現できない文字の符号位置（U+XXXX）と文字を示す例外を作る。
        /// </summary>
        /// <remarks>
        /// <see cref="EncoderFallbackException.Index"/> は使わない（基準が実行環境によって異なりうる。Convert-ProbedContent と同じ）。
        /// 符号化の例外は内部例外に持たせない。.NET の文言は PowerShell 5.1 と 7.x で違い、利用者に見せないため（1.1.0 の方針）。
        /// </remarks>
        public static UnrepresentableCharacterException From(EncoderFallbackException exception, string path, string encodingName)
        {
            int codePoint;
            string character;

            if (exception.CharUnknownHigh != '\0' && exception.CharUnknownLow != '\0')
            {
                codePoint = char.ConvertToUtf32(exception.CharUnknownHigh, exception.CharUnknownLow);
                character = new string(new[] { exception.CharUnknownHigh, exception.CharUnknownLow });
            }
            else
            {
                codePoint = exception.CharUnknown;
                character = exception.CharUnknown.ToString();
            }

            return new UnrepresentableCharacterException(
                ValidationMessages.UnrepresentableCharacterOnWrite(
                    path,
                    encodingName,
                    "U+" + codePoint.ToString("X4", CultureInfo.InvariantCulture),
                    character));
        }
    }
}
