using System;
using System.Management.Automation;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets
{
    /// <summary>
    /// エンコーディング判定のオプションを表すクラス
    /// </summary>
    /// <remarks>
    /// -Culture / -Strategy の検証は、Resolve-Encoding と Probed 系コマンドでここを共用する（1.3.0 第五次の後半。BUG-0006）。
    /// 1.2.0 までの Resolve-Encoding は、解釈できない値をクラスライブラリの生の ArgumentException（英語の文言・分類 NotSpecified）で止めていた。
    /// </remarks>
    internal class ResolveEncodingOptions
    {
        /// <summary>
        /// カルチャー名を検証する。受け付けない場合は、終了エラーとして報告する ErrorRecord を返す（受け付ける場合は null）。
        /// </summary>
        /// <remarks>
        /// 受け付けるかどうかは、名前の形だけで決める（<see cref="CultureNameSubtags.IsWellFormed(string)"/>。1.3.0 第五次の後半。BUG-0007）。
        /// <see cref="System.Globalization.CultureInfo"/> で作れるかどうかで決めると、.NET Framework（NLS）と .NET Core（ICU）で受け付ける名前が違い、
        /// <c>-Culture C</c> が PowerShell 5.1 では InvalidCulture、7.x では判定に進んでいた。
        /// <br/>
        /// 例外には .NET の例外を内部例外として持たせない。.NET の文言は 5.1 と 7.x で違い、利用者に見せる文言は
        /// <see cref="ValidationMessages.InvalidCulture"/> だけに絞る（1.1.0 の方針）。
        /// </remarks>
        internal static ErrorRecord? ValidateCulture(string culture)
        {
            if (CultureNameSubtags.IsWellFormed(culture))
            {
                return null;
            }

            return new ErrorRecord(
                new PSArgumentException(ValidationMessages.InvalidCulture(culture)),
                "InvalidCulture",
                ErrorCategory.InvalidArgument,
                culture);
        }

        /// <summary>
        /// 判定方式を検証する。解釈できない場合は、終了エラーとして報告する ErrorRecord を返す（解釈できる場合は null）。
        /// </summary>
        internal static ErrorRecord? ValidateStrategy(string strategy, out DetectionStrategy parsed)
        {
            if (TryParseStrategy(strategy, out parsed))
            {
                return null;
            }

            return new ErrorRecord(
                new PSArgumentException(ValidationMessages.InvalidStrategy(strategy)),
                "InvalidStrategy",
                ErrorCategory.InvalidArgument,
                strategy);
        }

        /// <summary>
        /// エンコーディング判定の戦略を解析する。解析できない場合は false を返す。
        /// </summary>
        /// <remarks>
        /// 判定方式の語彙は Resolve-Encoding と Probed 系コマンドで共通である。
        /// 表を二重に持つと、片方だけに別名が増えて挙動がずれるため、ここに集約する。
        /// <br/>
        /// 小文字化には <see cref="string.ToLowerInvariant"/> を用いる。
        /// カルチャー依存の小文字化では、トルコ語環境で 'I' が 'ı' になり
        /// 'Combined' や 'NativeOnly' を解析できなくなるためである。
        /// </remarks>
        /// <param name="strategy">判定戦略を表す文字列</param>
        /// <param name="parsed">解析できた場合の DetectionStrategy 列挙値</param>
        /// <returns>解析できた場合は true</returns>
        internal static bool TryParseStrategy(string strategy, out DetectionStrategy parsed)
        {
            if (strategy == null)
            {
                parsed = default;
                return false;
            }

            switch (strategy.Trim().ToLowerInvariant())
            {
                case "combined":
                case "default":
                case "0":
                    parsed = DetectionStrategy.Combined;
                    return true;

                case "nativeonly":
                case "native":
                case "1":
                    parsed = DetectionStrategy.NativeOnly;
                    return true;

                case "utfunknownonly":
                case "utfunknown":
                case "3":
                    parsed = DetectionStrategy.UtfUnknownOnly;
                    return true;

                default:
                    parsed = default;
                    return false;
            }
        }
    }
}
