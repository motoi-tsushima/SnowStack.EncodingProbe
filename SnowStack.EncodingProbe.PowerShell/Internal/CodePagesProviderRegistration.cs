using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// CodePagesEncodingProvider の登録を担う。
    /// </summary>
    /// <remarks>
    /// .NET (Core/5+) では Shift-JIS (CP932) 等のレガシーコードページを扱うために
    /// プロバイダーの登録が必要である。ホスト側の登録には依存せず、
    /// モジュールのロード時（<see cref="EncodingProbeModuleInitializer"/>）に登録する。
    /// Import-Module を経由せずアセンブリを直接読み込む経路（テスト等）でも
    /// 語彙解決が機能するよう、<see cref="EncodingVocabulary"/> からも呼び出す。
    /// 二重登録は無害だが、判定を挟んで不要な登録を避けている。
    /// </remarks>
    internal static class CodePagesProviderRegistration
    {
        private static readonly object SyncRoot = new object();
        private static bool _registered;

        /// <summary>
        /// CodePagesEncodingProvider が未登録であれば登録する。何度呼び出してもよい。
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_registered)
                {
                    return;
                }

                try
                {
                    // Shift-JIS (CP932) が取得できるか確認する
                    _ = Encoding.GetEncoding(932);
                }
                catch (NotSupportedException)
                {
                    // 未登録の場合のみ登録する
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                }
                catch (ArgumentException)
                {
                    // 同上（ランタイムによっては ArgumentException になる）
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                }

                _registered = true;
            }
        }
    }
}