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

        /// <summary>
        /// 指定されたカルチャーを現在のスレッドのカルチャーに設定する
        /// </summary>
        /// <param name="culture">設定するカルチャーの名前</param>
        /// <returns>設定されたカルチャーの名前</returns>
        /// <exception cref="Exception">無効なカルチャーが指定された場合にスローされる</exception>
        internal static string ChangeCurrentCulture(string? culture)
        {
            if (!string.IsNullOrWhiteSpace(culture))
            {
                culture = culture.Trim();
                try
                {
                    var cultureInfo = new System.Globalization.CultureInfo(culture);
                    // Native AOT 対応: CurrentCulture と CurrentUICulture の両方を設定
                    System.Threading.Thread.CurrentThread.CurrentCulture = cultureInfo;
                    System.Threading.Thread.CurrentThread.CurrentUICulture = cultureInfo;
                    // アプリケーション全体のデフォルトカルチャーも設定
                    System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
                    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
                    return culture;
                }
                catch (System.Globalization.CultureNotFoundException ex)
                {
                    throw new Exception(string.Format(ValidationMessages.InvalidCultureInfo, culture) + "\n" + ex.Message, ex);
                }
                catch (ArgumentException ex)
                {
                    throw new Exception(string.Format(ValidationMessages.InvalidCultureInfo, culture) + "\n" + ex.Message, ex);
                }
            }
            return string.Empty;
        }
    }
}
