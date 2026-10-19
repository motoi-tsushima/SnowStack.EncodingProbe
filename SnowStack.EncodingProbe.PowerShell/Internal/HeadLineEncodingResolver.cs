using System.Text;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 先頭 N 行で判定し、読み替え、ファイル全体を検証した結果。
    /// </summary>
    internal sealed class HeadLineEncoding
    {
        public HeadLineEncoding(int codePage, bool bom, LineBreakType lineBreak)
        {
            this.CodePage = codePage;
            this.Bom = bom;
            this.LineBreak = lineBreak;
        }

        /// <summary>読み替えた後の（検証を通った）コードページ</summary>
        public int CodePage { get; }

        /// <summary>ファイル先頭に BOM があったかどうか</summary>
        public bool Bom { get; }

        /// <summary>先頭 N 行の範囲で判定した改行（読み替えない。仕様書 3.2）</summary>
        public LineBreakType LineBreak { get; }

        /// <summary>
        /// 書き込み系（Export-ProbedCsv）で継承する情報として表す（文字エンコーディング・BOM・改行）
        /// </summary>
        public EncodingSpec ToSpec()
            => EncodingSpec.Create(EncodingVocabulary.BuildEncoding(this.CodePage, this.Bom), this.Bom, this.LineBreak);
    }

    /// <summary>
    /// -DetectionLineCount を指定した場合の文字エンコーディングの決定（1.3.0 仕様書 2〜4 章）。
    /// </summary>
    /// <remarks>
    /// 先頭 N 行で判定し（<see cref="HeadLineDetection"/>。Resolve-Encoding と同じ判定）、仕様書 3.2 の読み替えを行い、
    /// ファイル全体を厳密に検証する（<see cref="StrictDecodeVerifier"/>）。
    /// Import-ProbedCsv（読み込むファイル）と Export-ProbedCsv（追記先・出力先・-EncodingFrom の参照ファイル）が共用する。
    /// 「先頭 N 行で判定した結果は、全体で確かめてから使う」という規則に例外を作らないため、1 か所にまとめる。
    /// </remarks>
    internal static class HeadLineEncodingResolver
    {
        /// <summary>
        /// 先頭 N 行で判定し、読み替え、全体を検証する。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス。存在することを前提とする。</param>
        /// <param name="lineCount">判定に使う行数（1 以上）</param>
        /// <param name="detectorOptions">-Culture / -Strategy から組み立てた判定オプション。未指定の場合は null。</param>
        /// <exception cref="EncodingDetectionException">
        /// 判定に失敗した場合、判定したコードページを実行環境が提供していない場合、切り出しが上限を超えた場合
        /// </exception>
        /// <exception cref="DetectionVerificationException">ファイル全体を厳密に復号できなかった場合</exception>
        public static HeadLineEncoding Resolve(string path, long lineCount, EncodingDetectorOptions? detectorOptions)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            HeadLineDetectionResult detection = HeadLineDetection.Detect(path, lineCount, detectorOptions);
            EncodingInformation information = detection.Information;

            if (information.CodePage < 0)
            {
                throw new EncodingDetectionException(ValidationMessages.DetectionFailed(path), path);
            }

            int codePage = detection.GetReinterpretedCodePage();

            // 判定処理は .NET が提供していないコードページを返すことがある（全体の判定と同じ扱い）
            if (!EncodingVocabulary.TryBuildEncoding(codePage, emitBom: false, out Encoding? _))
            {
                throw new EncodingDetectionException(
                    ValidationMessages.DetectedCodePageNotAvailable(information.CodePage, information.EncodingWebName),
                    path,
                    EncodingDetectionException.CodePageNotAvailableId);
            }

            StrictDecodeFailure? failure = StrictDecodeVerifier.Verify(path, codePage);

            if (failure != null)
            {
                // 文字エンコーディング名は、読み替えた後の（実際に検証した）ものを示す（第二次の決定）
                throw new DetectionVerificationException(
                    ValidationMessages.DetectionVerificationFailed(
                        path,
                        EncodingVocabulary.GetUnifiedName(codePage, information.Bom),
                        lineCount,
                        failure.Offset,
                        ByteSequenceText.Format(failure.Bytes)),
                    path);
            }

            return new HeadLineEncoding(codePage, information.Bom, information.LineBreak);
        }
    }
}
