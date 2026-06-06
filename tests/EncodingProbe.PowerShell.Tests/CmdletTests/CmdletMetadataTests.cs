using System.Management.Automation;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// ResolveEncodingCmdlet のメタデータ（属性・パラメーター定義）をリフレクションで検証するテスト。
/// PowerShell ランスペースを必要とせず高速に実行できる。
/// </summary>
public class CmdletMetadataTests
{
    private static readonly Type CmdletType = typeof(ResolveEncodingCmdlet);

    // ─── CmdletAttribute ─────────────────────────────────────────────────────

    [Fact]
    public void Cmdlet_HasCmdletAttribute()
    {
        Assert.NotNull(CmdletType.GetCustomAttributes(typeof(CmdletAttribute), inherit: false)
                                 .FirstOrDefault());
    }

    [Fact]
    public void Cmdlet_VerbIsDiagnosticResolve()
    {
        var attr = (CmdletAttribute)CmdletType
            .GetCustomAttributes(typeof(CmdletAttribute), inherit: false)
            .Single();

        Assert.Equal(VerbsDiagnostic.Resolve, attr.VerbName);
        Assert.Equal("Encoding", attr.NounName);
    }

    // ─── OutputType ──────────────────────────────────────────────────────────

    [Fact]
    public void Cmdlet_OutputTypeIsEncodingInfomation()
    {
        var attr = (OutputTypeAttribute)CmdletType
            .GetCustomAttributes(typeof(OutputTypeAttribute), inherit: false)
            .Single();

        Assert.Contains(attr.Type, t => t.Type == typeof(EncodingInfomation));
    }

    // ─── Path パラメーター ───────────────────────────────────────────────────

    [Fact]
    public void Parameter_Path_Exists()
    {
        Assert.NotNull(CmdletType.GetProperty("Path"));
    }

    [Fact]
    public void Parameter_Path_IsMandatory()
    {
        var attr = (ParameterAttribute)CmdletType
            .GetProperty("Path")!
            .GetCustomAttributes(typeof(ParameterAttribute), inherit: false)
            .Single();

        Assert.True(attr.Mandatory);
    }

    [Fact]
    public void Parameter_Path_PositionIsZero()
    {
        var attr = (ParameterAttribute)CmdletType
            .GetProperty("Path")!
            .GetCustomAttributes(typeof(ParameterAttribute), inherit: false)
            .Single();

        Assert.Equal(0, attr.Position);
    }

    [Fact]
    public void Parameter_Path_AcceptsValueFromPipeline()
    {
        var attr = (ParameterAttribute)CmdletType
            .GetProperty("Path")!
            .GetCustomAttributes(typeof(ParameterAttribute), inherit: false)
            .Single();

        Assert.True(attr.ValueFromPipeline);
    }

    [Fact]
    public void Parameter_Path_HasAliasFullName()
    {
        var aliases = ((AliasAttribute)CmdletType
            .GetProperty("Path")!
            .GetCustomAttributes(typeof(AliasAttribute), inherit: false)
            .Single()).AliasNames;

        Assert.Contains("FullName", aliases);
        Assert.Contains("FilePath", aliases);
    }

    // ─── Culture パラメーター ────────────────────────────────────────────────

    [Fact]
    public void Parameter_Culture_Exists()
    {
        Assert.NotNull(CmdletType.GetProperty("Culture"));
    }

    [Fact]
    public void Parameter_Culture_IsNotMandatory()
    {
        var attr = (ParameterAttribute)CmdletType
            .GetProperty("Culture")!
            .GetCustomAttributes(typeof(ParameterAttribute), inherit: false)
            .Single();

        Assert.False(attr.Mandatory);
    }

    // ─── Strategy パラメーター ───────────────────────────────────────────────

    [Fact]
    public void Parameter_Strategy_Exists()
    {
        Assert.NotNull(CmdletType.GetProperty("Strategy"));
    }

    [Fact]
    public void Parameter_Strategy_IsNotMandatory()
    {
        var attr = (ParameterAttribute)CmdletType
            .GetProperty("Strategy")!
            .GetCustomAttributes(typeof(ParameterAttribute), inherit: false)
            .Single();

        Assert.False(attr.Mandatory);
    }
}
