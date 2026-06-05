using System.Management.Automation;
using System.Management.Automation.Runspaces;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// xUnit の IClassFixture 用 PowerShell ランスペース共有クラス。
/// テストクラスごとに 1 つのランスペースを生成・破棄する。
/// </summary>
public sealed class RunspaceFixture : IDisposable
{
    public Runspace Runspace { get; }

    public RunspaceFixture()
    {
        var iss = InitialSessionState.CreateDefault2();
        iss.Commands.Add(new SessionStateCmdletEntry(
            "Resolve-Encoding",
            typeof(ResolveEncodingCmdlet),
            helpFileName: null));

        Runspace = RunspaceFactory.CreateRunspace(iss);
        Runspace.Open();
    }

    /// <summary>
    /// ランスペース上でコマンドレットを実行して結果を返す
    /// </summary>
    /// <param name="path">-Path に渡すファイルパス</param>
    public System.Collections.ObjectModel.Collection<PSObject> Invoke(string path)
    {
        using var ps = System.Management.Automation.PowerShell.Create();
        ps.Runspace = Runspace;
        ps.AddCommand("Resolve-Encoding").AddParameter("Path", path);
        return ps.Invoke();
    }

    public void Dispose() => Runspace.Dispose();
}
