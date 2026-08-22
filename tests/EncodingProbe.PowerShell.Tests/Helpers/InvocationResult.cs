using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// コマンドレット実行の結果（出力と非終了エラー）
/// </summary>
public sealed class InvocationResult
{
    public InvocationResult(Collection<PSObject> output, IReadOnlyList<ErrorRecord> errors)
    {
        this.Output = output;
        this.Errors = errors;
    }

    /// <summary>パイプラインへの出力</summary>
    public Collection<PSObject> Output { get; }

    /// <summary>報告された非終了エラー</summary>
    public IReadOnlyList<ErrorRecord> Errors { get; }

    /// <summary>出力を文字列の配列として取り出す</summary>
    public string[] AsStrings()
    {
        var values = new string[this.Output.Count];

        for (int i = 0; i < this.Output.Count; i++)
        {
            values[i] = (string)this.Output[i].BaseObject;
        }

        return values;
    }
}