using System.IO;

namespace EncodingProbe.Tests.Helpers
{
    /// <summary>
    /// テストデータファイルへのパスを解決するヘルパークラス
    /// </summary>
    internal static class TestDataHelper
    {
        /// <summary>
        /// TestData フォルダーのルートパス
        /// </summary>
        private static readonly string TestDataRoot =
            Path.Combine(AppContext.BaseDirectory, "TestData");

        /// <summary>
        /// 言語カテゴリとファイル名からテストデータファイルの絶対パスを返す
        /// </summary>
        /// <param name="language">言語フォルダー名（例: "Japanese"）</param>
        /// <param name="fileName">ファイル名（例: "sample_utf8.txt"）</param>
        public static string GetPath(string language, string fileName)
            => Path.Combine(TestDataRoot, language, fileName);

        /// <summary>
        /// テストデータファイルの内容を byte[] として返す
        /// </summary>
        public static byte[] ReadBytes(string language, string fileName)
            => File.ReadAllBytes(GetPath(language, fileName));

        /// <summary>
        /// テストデータファイルを FileStream として返す（呼び出し元で using してください）
        /// </summary>
        public static FileStream OpenStream(string language, string fileName)
            => new FileStream(GetPath(language, fileName), FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
