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
            strategy = strategy.Trim().ToLower();
            return strategy switch
            {
                "combined" => DetectionStrategy.Combined,
                "utfunknownonly" => DetectionStrategy.UtfUnknownOnly,
                "nativeonly" => DetectionStrategy.NativeOnly,
                "0" => DetectionStrategy.Combined,
                "3" => DetectionStrategy.UtfUnknownOnly,
                "1" => DetectionStrategy.NativeOnly,
                "default" => DetectionStrategy.Combined,
                "utfunknown" => DetectionStrategy.UtfUnknownOnly,
                "native" => DetectionStrategy.NativeOnly,
                _ => throw new ArgumentException($"Invalid strategy: {strategy}")
            };
        }
    }
}
