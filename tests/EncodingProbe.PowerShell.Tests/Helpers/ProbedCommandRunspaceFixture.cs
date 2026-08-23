using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// 1.1.0 で追加したコマンドレットを登録したランスペースを共有する xUnit の IClassFixture。
/// </summary>
/// <remarks>
/// Import-Module を経由しないため <see cref="SnowStack.EncodingProbe.PowerShell.EncodingProbeModuleInitializer"/>
/// は動作しないが、CodePagesEncodingProvider の登録は語彙解決側でも行われるため
/// レガシーコードページを扱うテストも動作する。
/// </remarks>
public sealed class ProbedCommandRunspaceFixture : IDisposable
{
    public ProbedCommandRunspaceFixture()
    {
        InitialSessionState sessionState = InitialSessionState.CreateDefault2();

        Register(sessionState, "Resolve-Encoding", typeof(ResolveEncodingCmdlet));
        Register(sessionState, "ConvertTo-DotNetEncoding", typeof(ConvertToDotNetEncodingCommand));
        Register(sessionState, "Get-ProbedContent", typeof(GetProbedContentCommand));
        Register(sessionState, "Set-ProbedContent", typeof(SetProbedContentCommand));

        this.Runspace = RunspaceFactory.CreateRunspace(sessionState);
        this.Runspace.Open();
    }

    /// <summary>共有するランスペース</summary>
    public Runspace Runspace { get; }

    /// <summary>
    /// コマンドをパラメータ付きで実行して結果を返す
    /// </summary>
    public Collection<PSObject> Invoke(string command, IDictionary<string, object?>? parameters = null)
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = this.Runspace;

        shell.AddCommand(command);

        if (parameters != null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                shell.AddParameter(parameter.Key, parameter.Value);
            }
        }

        return shell.Invoke();
    }

    /// <summary>
    /// コマンドを位置指定引数ひとつで実行して結果を返す
    /// </summary>
    public Collection<PSObject> InvokeWithArgument(string command, object? argument)
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = this.Runspace;
        shell.AddCommand(command).AddArgument(argument);

        return shell.Invoke();
    }

    /// <summary>
    /// スクリプトを実行して結果を返す（パイプラインの検証に使う）
    /// </summary>
    public Collection<PSObject> InvokeScript(string script)
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = this.Runspace;
        shell.AddScript(script);

        return shell.Invoke();
    }

    /// <summary>
    /// コマンドを実行し、出力と非終了エラーの両方を返す
    /// </summary>
    public InvocationResult InvokeCapturingErrors(string command, IDictionary<string, object?>? parameters = null)
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = this.Runspace;
        shell.AddCommand(command);

        if (parameters != null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                shell.AddParameter(parameter.Key, parameter.Value);
            }
        }

        Collection<PSObject> output = shell.Invoke();

        return new InvocationResult(
            output,
            new List<ErrorRecord>(shell.Streams.Error),
            new List<WarningRecord>(shell.Streams.Warning));
    }

    /// <summary>
    /// スクリプトを実行し、出力と非終了エラー・警告をまとめて返す
    /// </summary>
    public InvocationResult InvokeScriptCapturingErrors(string script)
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = this.Runspace;
        shell.AddScript(script);

        Collection<PSObject> output = shell.Invoke();

        return new InvocationResult(
            output,
            new List<ErrorRecord>(shell.Streams.Error),
            new List<WarningRecord>(shell.Streams.Warning));
    }

    public void Dispose() => this.Runspace.Dispose();

    private static void Register(InitialSessionState sessionState, string name, Type implementingType)
        => sessionState.Commands.Add(new SessionStateCmdletEntry(name, implementingType, helpFileName: null));
}