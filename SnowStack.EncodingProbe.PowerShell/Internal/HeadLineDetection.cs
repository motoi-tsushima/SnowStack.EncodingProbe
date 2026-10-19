using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 先頭 N 行で判定した結果。
    /// </summary>
    internal sealed class HeadLineDetectionResult
    {
        public HeadLineDetectionResult(EncodingInformation information, LineBreakUnit unit)
        {
            this.Information = information;
            this.Unit = unit;
        }

        /// <summary>判定結果（読み替えの前）</summary>
        public EncodingInformation Information { get; }

        /// <summary>切り出しで決めた改行の型（Import-ProbedCsv / Export-ProbedCsv の読み替えに使う。仕様書 3.2）</summary>
        public LineBreakUnit Unit { get; }
    }

    /// <summary>
    /// ファイルの先頭 N 行を切り出して判定する（1.3.0 仕様書 2.7）。
    /// </summary>
    /// <remarks>
    /// Resolve-Encoding / Import-ProbedCsv / Export-ProbedCsv の -DetectionLineCount はすべてここを通る。
    /// 同じファイル・同じ N・同じ判定オプションに対して、3 つのコマンドの判定結果（読み替えの前）が
    /// すべてのプロパティで一致することを、この共用で保証する（仕様書 3.3）。
    /// </remarks>
    internal static class HeadLineDetection
    {
        /// <summary>
        /// 先頭 N 行で判定する。判定に失敗した場合（CodePage &lt; 0）も、そのまま結果を返す。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス。存在することを前提とする。</param>
        /// <param name="lineCount">判定に使う行数（1 以上）</param>
        /// <param name="detectorOptions">-Culture / -Strategy から組み立てた判定オプション。未指定の場合は null。</param>
        /// <exception cref="EncodingDetectionException">切り出す範囲が判定できる大きさを超える場合</exception>
        public static HeadLineDetectionResult Detect(
            string path, long lineCount, EncodingDetectorOptions? detectorOptions)
        {
            HeadLineCut cut = HeadLineCutter.Cut(path, lineCount);

            // 0 バイトのファイルは、既存の規則（全体の判定）と同じに扱う（仕様書 2.6）
            EncodingInformation information = cut.Bytes.Length == 0
                ? EncodingProbe.Detect(path, detectorOptions)
                : EncodingProbe.Detect(cut.Bytes, detectorOptions);

            return new HeadLineDetectionResult(information, cut.Unit);
        }
    }
}
