using System;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets
{
    /// <summary>
    /// エンコーディング判定のオプションを表すクラス
    /// </summary>
    internal class ResolveEncodingOptions
    {
        /// <summary>
        /// エンコーディング判定の戦略
        /// </summary>
        /// <param name="strategy">判定戦略を表す文字列</param>
        /// <returns>対応するDetectionStrategy列挙値</returns>
        /// <exception cref="ArgumentException">無効な戦略が指定された場合にスローされる</exception>
        internal static DetectionStrategy ParseStrategy(string strategy)
        {
            if (!TryParseStrategy(strategy, out DetectionStrategy parsed))
            {
                throw new ArgumentException($"Invalid strategy: {strategy}");
            }

            return parsed;
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
