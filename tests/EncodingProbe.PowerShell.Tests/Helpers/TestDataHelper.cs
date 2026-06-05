using System.IO;

namespace EncodingProbe.PowerShell.Tests.Helpers;

/// <summary>
/// テストデータファイルへのパスを解決するヘルパークラス
/// </summary>
internal static class TestDataHelper
{
    private static readonly string TestDataRoot =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    /// <summary>
    /// 言語カテゴリとファイル名からテストデータファイルの絶対パスを返す
    /// </summary>
    public static string GetPath(string language, string fileName)
        => Path.Combine(TestDataRoot, language, fileName);
}
