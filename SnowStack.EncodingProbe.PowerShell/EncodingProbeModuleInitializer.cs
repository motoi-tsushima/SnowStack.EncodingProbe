using System;
using System.Management.Automation;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell;

/// <summary>
/// モジュールロード時（Import-Module）に自動実行される初期化処理。
/// PowerShell (Core) 環境で Shift-JIS 等のレガシーコードページを
/// 利用できるよう CodePagesEncodingProvider を登録する。
/// </summary>
public sealed class EncodingProbeModuleInitializer : IModuleAssemblyInitializer
{
    /// <summary>
    /// Import-Module 時に PowerShell エンジンから呼び出される。
    /// </summary>
    public void OnImport()
    {
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
    }
}
