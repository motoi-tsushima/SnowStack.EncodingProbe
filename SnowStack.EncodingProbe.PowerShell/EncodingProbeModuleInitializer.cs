using System;
using System.Management.Automation;
using SnowStack.EncodingProbe.PowerShell.Internal;

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
        CodePagesProviderRegistration.EnsureRegistered();
    }
}