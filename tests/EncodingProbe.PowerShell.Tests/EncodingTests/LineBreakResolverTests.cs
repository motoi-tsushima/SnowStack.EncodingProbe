using System;
using SnowStack.EncodingProbe;
using SnowStack.EncodingProbe.PowerShell;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// 改行コードの決定規則（仕様書 5.5）を検証するテスト。
/// </summary>
public class LineBreakResolverTests
{
    /// <summary>
    /// -LineBreak の明示指定が、参照情報より優先されること（粒度の細かい方が勝つ）。
    /// </summary>
    [Theory]
    [InlineData(LineBreakOption.CrLf, "\r\n")]
    [InlineData(LineBreakOption.Lf, "\n")]
    [InlineData(LineBreakOption.Cr, "\r")]
    public void Resolve_ExplicitOption_OverridesReference(LineBreakOption option, string expected)
    {
        // 参照情報が別の改行を持っていても、明示指定が優先される
        string actual = LineBreakResolver.Resolve(option, LineBreakType.Lf);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// -LineBreak 省略時（Auto）は、参照情報の改行を継承すること。OSを問わない。
    /// </summary>
    [Theory]
    [InlineData(LineBreakType.CrLf, "\r\n")]
    [InlineData(LineBreakType.Lf, "\n")]
    [InlineData(LineBreakType.Cr, "\r")]
    public void Resolve_Auto_InheritsSingleLineBreakFromReference(LineBreakType reference, string expected)
    {
        string actual = LineBreakResolver.Resolve(LineBreakOption.Auto, reference);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 混在改行は「CR-LF を含むなら CR-LF、含まないなら LF」という規則で決定されること。
    /// EncodingInformation は改行の出現回数を保持しないため多数決は取れず、
    /// OS に依存しない決定的な規則を採用している。
    /// </summary>
    [Theory]
    [InlineData(LineBreakType.LfAndCrLf, "\r\n")]
    [InlineData(LineBreakType.CrAndCrLf, "\r\n")]
    [InlineData(LineBreakType.LfAndCrAndCrLf, "\r\n")]
    [InlineData(LineBreakType.LfAndCr, "\n")]
    public void Resolve_Auto_MixedLineBreakFollowsCrLfPreferenceRule(LineBreakType reference, string expected)
    {
        string actual = LineBreakResolver.Resolve(LineBreakOption.Auto, reference);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 参照情報が改行を持たない場合（None）はOS既定に落ちること。
    /// </summary>
    [Fact]
    public void Resolve_Auto_WithoutLineBreakInReference_FallsBackToOsDefault()
    {
        string actual = LineBreakResolver.Resolve(LineBreakOption.Auto, LineBreakType.None);

        Assert.Equal(Environment.NewLine, actual);
    }

    /// <summary>
    /// 参照情報そのものが無い場合はOS既定に落ちること。
    /// </summary>
    [Fact]
    public void Resolve_Auto_WithoutReference_FallsBackToOsDefault()
    {
        string actual = LineBreakResolver.Resolve(LineBreakOption.Auto, null);

        Assert.Equal(Environment.NewLine, actual);
    }

    /// <summary>
    /// 判定結果から改行文字列への変換が、全ての LineBreakType を網羅していること。
    /// None のみ「継承できない」を表す null を返す。
    /// </summary>
    [Theory]
    [InlineData(LineBreakType.None, null)]
    [InlineData(LineBreakType.CrLf, "\r\n")]
    [InlineData(LineBreakType.Lf, "\n")]
    [InlineData(LineBreakType.Cr, "\r")]
    [InlineData(LineBreakType.LfAndCrLf, "\r\n")]
    [InlineData(LineBreakType.CrAndCrLf, "\r\n")]
    [InlineData(LineBreakType.LfAndCr, "\n")]
    [InlineData(LineBreakType.LfAndCrAndCrLf, "\r\n")]
    public void FromDetected_CoversAllLineBreakTypes(LineBreakType lineBreak, string? expected)
    {
        Assert.Equal(expected, LineBreakResolver.FromDetected(lineBreak));
    }
}