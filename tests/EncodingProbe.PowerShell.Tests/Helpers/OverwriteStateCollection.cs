using Xunit;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// 上書きの部品（<c>SafeFileOverwrite</c>）の静的な設定（メモリの上限・テスト用の差し込み口）を変えるテスト、
/// どの書き方で書いたかを確かめるテストを、ほかのテストと並行に走らせないためのコレクション（1.3.0 第五次の仕上げ）
/// </summary>
/// <remarks>
/// メモリの上限はプロセス全体で数えるため、ほかのテストが同時にメモリにためていると、書き方が切り替わってしまう。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OverwriteStateCollection
{
    public const string Name = "OverwriteState";
}
