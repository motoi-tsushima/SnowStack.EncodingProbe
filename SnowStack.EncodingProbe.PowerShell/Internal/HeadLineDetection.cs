using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 先頭 N 行で判定した結果。
    /// </summary>
    internal sealed class HeadLineDetectionResult
    {
        public HeadLineDetectionResult(EncodingInformation information, LineBreakUnit unit, byte[] bytes)
        {
            this.Information = information;
            this.Unit = unit;
            this.Bytes = bytes;
        }

        /// <summary>判定に使った先頭 N 行のバイト列（読み替えた後の単位で改行を数え直すために使う）</summary>
        public byte[] Bytes { get; }

        /// <summary>判定結果（読み替えの前）</summary>
        public EncodingInformation Information { get; }

        /// <summary>切り出しで決めた改行の型（Import-ProbedCsv / Export-ProbedCsv の読み替えに使う。仕様書 3.2）</summary>
        public LineBreakUnit Unit { get; }

        /// <summary>
        /// 仕様書 3.2 の読み替えを行ったコードページを返す（Import-ProbedCsv / Export-ProbedCsv 用）。
        /// </summary>
        /// <remarks>
        /// 判定結果が us-ascii（先頭 N 行のバイトがすべて 0x80 未満）の場合は、切り出しで決めた改行の型に従い、
        /// 1 バイトの型・改行が無い場合は BOM 無しの UTF-8、UTF-16 / UTF-32 の型ならその文字エンコーディングとする。
        /// ASCII は UTF-8 の部分集合なので、ファイルが本当に UTF-8 なら必ず正しく読める。
        /// UTF-8 でなければ、全体の検証で止まる（黙って文字化けしない）。
        /// UTF-16 / UTF-32 の行が必要なのは、判定処理が 0x00 を含んでいても 0x80 未満のバイトだけなら ASCII と判定するためである
        /// （24 章 U4、課題 docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md）。
        /// <br/>
        /// それ以外の判定結果はそのまま返す。Resolve-Encoding は読み替えない（仕様書 3.1）。
        /// </remarks>
        public int GetReinterpretedCodePage()
        {
            if (this.Information.CodePage != EncodingVocabulary.CodePageAscii)
            {
                return this.Information.CodePage;
            }

            switch (this.Unit)
            {
                case LineBreakUnit.Utf16Le:
                    return EncodingVocabulary.CodePageUtf16Le;
                case LineBreakUnit.Utf16Be:
                    return EncodingVocabulary.CodePageUtf16Be;
                case LineBreakUnit.Utf32Le:
                    return EncodingVocabulary.CodePageUtf32Le;
                case LineBreakUnit.Utf32Be:
                    return EncodingVocabulary.CodePageUtf32Be;
                default:
                    return EncodingVocabulary.CodePageUtf8;
            }
        }
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
            // 0 バイトのファイルは、全体の判定と同じ（切り出した 0 バイトがファイル全体）。
            // クラスライブラリの Detect(path) は FileShare.Read で開くので使わない（1.3.0 第六次 B。BUG-0026）
            EncodingInformation information = EncodingProbe.Detect(cut.Bytes, detectorOptions);

            return new HeadLineDetectionResult(information, cut.Unit, cut.Bytes);
        }
    }
}
