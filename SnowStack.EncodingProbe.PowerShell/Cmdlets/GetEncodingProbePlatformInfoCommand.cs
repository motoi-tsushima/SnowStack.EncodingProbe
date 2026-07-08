using System;
using System.Collections;
using System.Management.Automation;
using SnowStack.EncodingProbe;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 文字エンコーディング判定に関連する実行環境情報(OS/ .NETランタイム/ロケール/PowerShellホスト)を取得するコマンドレット
/// </summary>
[Cmdlet(VerbsCommon.Get, "EncodingProbePlatformInfo")]
[OutputType(typeof(EncodingProbePlatformInformation))]
public sealed class GetEncodingProbePlatformInfoCommand : PSCmdlet
{
    protected override void ProcessRecord()
    {
        var core = PlatformInfoResolver.Resolve();
        var host = ResolvePowerShellHost();

        WriteObject(EncodingProbePlatformInformation.FromCore(core, host));
    }

    private PowerShellHostInformation ResolvePowerShellHost()
    {
        var psVersionTable = (Hashtable)GetVariableValue("PSVersionTable")!;
        var psVersion = ParsePSVersion(psVersionTable["PSVersion"]!);
        var psEdition = (string)psVersionTable["PSEdition"]!;

        return new PowerShellHostInformation
        {
            PSVersion = psVersion,
            PSEdition = psEdition,
            SupportsNumericCodePageArgument = psVersion >= new Version(6, 2),
            SupportsAnsiEncodingName = psVersion >= new Version(7, 4),
        };
    }

    /// <summary>
    /// $PSVersionTable.PSVersion を System.Version に変換する。
    /// </summary>
    /// <remarks>
    /// PSVersion の実際の型は PowerShell のホストによって異なる。
    /// Windows PowerShell 5.1 では System.Version、PowerShell 6以降では
    /// System.Management.Automation.SemanticVersion が入っている。
    /// PowerShellStandard.Library には SemanticVersion 型が含まれず、
    /// コンパイル時に直接参照できないため、両者に共通する
    /// "Major.Minor.Patch[-prerelease]" 形式の文字列表現から数値部分のみを取り出す。
    /// </remarks>
    private static Version ParsePSVersion(object psVersionValue)
    {
        string raw = psVersionValue.ToString()!;
        string numericPart = raw.Split('-', '+')[0];
        return Version.Parse(numericPart);
    }
}
