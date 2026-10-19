using System;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace
using SnowStack.EncodingProbe.PowerShell.Internal;
using SnowStack.EncodingProbe.PowerShell.Internal.Csv;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 文字エンコーディングを判定して CSV ファイルを読み込み、各レコードをオブジェクトとして出力するコマンドレット
/// </summary>
/// <remarks>
/// 解析は PowerShell 7.x の Import-Csv から移植した処理（<see cref="ImportCsvHelper"/>）で行い、ホストの Import-Csv は呼ばない。
/// そのため Windows PowerShell 5.1 でも 7.x の Import-Csv と同じ結果になる（1.3.0 仕様書 第 3 部）。
/// 5.1 の Import-Csv は CR だけの改行の CSV を黙って 0 件で返し、W3C 拡張ログ形式にも対応していない。
/// <br/>
/// -Culture（文字エンコーディングの判定に使うカルチャー。基底クラス）と -UseCulture（区切り文字に
/// カルチャーのリスト区切り文字を使う）は別のものである。パラメータセット名の Culture は -UseCulture の側を指す。
/// <br/>
/// 1 レコードずつ出力する。ファイル全体をメモリに読まない（全体で判定する場合の判定処理を除く）。
/// ファイルごとの失敗は非終了エラーとし、次のファイルに進む。
/// </remarks>
[Cmdlet(VerbsData.Import, "ProbedCsv", DefaultParameterSetName = DelimiterPathParameterSet)]
[OutputType(typeof(PSObject))]
public sealed class ImportProbedCsvCommand : ProbedContentCommandBase
{
    // パラメータセット名は、移植した ImportExportCSVHelper.SetDelimiter が見る名前と同じにする。
    // Delimiter* は -Delimiter（省略時は ','）、Culture* は -UseCulture で区切り文字を決める

    /// <summary>-Delimiter と -Path のパラメータセット名（既定）</summary>
    private const string DelimiterPathParameterSet = "DelimiterPath";

    /// <summary>-Delimiter と -LiteralPath のパラメータセット名</summary>
    private const string DelimiterLiteralPathParameterSet = "DelimiterLiteralPath";

    /// <summary>-UseCulture と -Path のパラメータセット名</summary>
    private const string CulturePathParameterSet = "CulturePath";

    /// <summary>-UseCulture と -LiteralPath のパラメータセット名</summary>
    private const string CultureLiteralPathParameterSet = "CultureLiteralPath";

    /// <summary>列名が重複した場合のエラー ID（標準の Import-Csv と同じ）</summary>
    private const string DuplicateHeaderErrorId = "AlreadyPresentPSMemberInfoInternalCollectionAdd";

    /// <summary>区切り文字（BeginProcessing で決める）</summary>
    private char _delimiter;

    /// <summary>
    /// 列名が空のときの警告を、実行全体（複数のファイル・パイプラインの入力）で 1 回だけにするためのフラグ
    /// </summary>
    private bool _alreadyWarnedUnspecifiedNames;

    /// <summary>
    /// 読み込むファイルのパス。ワイルドカードを使用でき、複数のファイルに一致した場合は順に読む。
    /// </summary>
    /// <remarks>
    /// パイプライン束縛の属性と別名は、両ホストで Get-ChildItem からの入力が動作確認済みの Get-ProbedContent に合わせる。
    /// -LiteralPath とは別のプロパティに持つ（標準の 5.1 は両者を 1 つの変数で兼ねるため、
    /// Get-ChildItem | Import-Csv が CannotSpecifyPathAndLiteralPath で失敗する）。
    /// </remarks>
    [Parameter(
        ParameterSetName = DelimiterPathParameterSet,
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true)]
    [Parameter(
        ParameterSetName = CulturePathParameterSet,
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "FilePath")]
    public string[] Path { get; set; } = default!;

    /// <summary>
    /// 読み込むファイルのパス。ワイルドカードを展開せず、そのままのパスとして扱う。
    /// </summary>
    [Parameter(
        ParameterSetName = DelimiterLiteralPathParameterSet,
        Mandatory = true,
        ValueFromPipelineByPropertyName = true)]
    [Parameter(
        ParameterSetName = CultureLiteralPathParameterSet,
        Mandatory = true,
        ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath", "LP")]
    public string[] LiteralPath { get; set; } = default!;

    /// <summary>
    /// 区切り文字。省略時は ','。
    /// </summary>
    [Parameter(Position = 1, ParameterSetName = DelimiterPathParameterSet)]
    [Parameter(Position = 1, ParameterSetName = DelimiterLiteralPathParameterSet)]
    [ValidateNotNull]
    public char Delimiter { get; set; }

    /// <summary>
    /// 区切り文字に、現在のカルチャーのリスト区切り文字を使う。
    /// </summary>
    /// <remarks>
    /// 文字エンコーディングの判定に使う -Culture とは別のものである。
    /// </remarks>
    [Parameter(ParameterSetName = CulturePathParameterSet, Mandatory = true)]
    [Parameter(ParameterSetName = CultureLiteralPathParameterSet, Mandatory = true)]
    [ValidateNotNull]
    public SwitchParameter UseCulture { get; set; }

    /// <summary>
    /// 列名。指定すると、ファイルの 1 行目もデータとして読む（標準と同じ）。すべてのファイルに同じ列名を使う。
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? Header { get; set; }

    /// <summary>
    /// 読み込みに使用する文字エンコーディング。
    /// 省略時（Auto）は対象ファイルから判定する。明示した場合は判定も検証も行わない。
    /// </summary>
    [Parameter]
    [EncodingSpecTransformation(EncodingUsage.Read)]
    public object Encoding { get; set; } = EncodingSpec.Auto;

    /// <summary>
    /// 判定に使う先頭の行数。省略時はファイル全体で判定する。
    /// </summary>
    /// <remarks>
    /// 行数は物理的な行数（CR-LF / LF / CR）であり、CSV のレコード数ではない。
    /// 指定した場合は、先頭 N 行で判定して読み替え（仕様書 3.2）、ファイル全体を厳密に検証してから読む（仕様書 4 章）。
    /// -Encoding を明示した場合は意味を持たない（警告もエラーも出さない）。
    /// </remarks>
    [Parameter]
    [ValidateRange(1L, long.MaxValue)]
    public long DetectionLineCount { get; set; }

    /// <summary>
    /// 区切り文字を決める
    /// </summary>
    protected override void BeginProcessing()
    {
        base.BeginProcessing();

        this._delimiter = ImportExportCSVHelper.SetDelimiter(this.ParameterSetName, this.Delimiter, this.UseCulture.IsPresent);
    }

    /// <summary>
    /// 指定されたファイルを順に読み込む
    /// </summary>
    protected override void ProcessRecord()
    {
        bool literal = this.ParameterSetName == DelimiterLiteralPathParameterSet
            || this.ParameterSetName == CultureLiteralPathParameterSet;
        string[] inputPaths = literal ? this.LiteralPath : this.Path;

        foreach (string file in ResolveExistingFiles(inputPaths, literal))
        {
            ImportFile(file);
        }
    }

    /// <summary>
    /// 1 ファイルを読み込んで、レコードをパイプラインに出力する
    /// </summary>
    private void ImportFile(string file)
    {
        EncodingSpec spec = EncodingSpec.FromBoundParameter(this.Encoding);
        int verifiedCodePage = 0;

        try
        {
            if (spec.IsAuto && this.MyInvocation.BoundParameters.ContainsKey(nameof(this.DetectionLineCount)))
            {
                // 先頭 N 行で判定し、読み替え、全体を検証してから、検証と同じ厳密な Encoding で読む（仕様書 10 章）。
                // 検証に失敗した場合は、そのファイルのレコードを 1 件も出力しない（仕様書 4.3）
                HeadLineEncoding resolved = HeadLineEncodingResolver.Resolve(file, this.DetectionLineCount, this.DetectorOptions);

                verifiedCodePage = resolved.CodePage;
                spec = EncodingSpec.Create(EncodingVocabulary.BuildStrictEncoding(resolved.CodePage), emitBom: false);
            }
            else if (spec.IsAuto && new FileInfo(file).Length > EncodingInheritance.MaxDetectableLength)
            {
                // 全体で判定できない大きさの場合は、-DetectionLineCount を案内する（仕様書 10 章）
                throw new EncodingDetectionException(ValidationMessages.FileTooLargeToDetectUseLineCount(file), file);
            }

            ReadRecords(file, spec, verifiedCodePage);
        }
        catch (EncodingDetectionException exception)
        {
            WriteError(CreateError(exception, exception.ErrorId, ErrorCategory.InvalidData, file));
        }
        catch (DetectionVerificationException exception)
        {
            WriteError(CreateError(exception, DetectionVerificationException.ErrorId, ErrorCategory.InvalidData, file));
        }
        catch (IOException exception)
        {
            WriteError(CreateError(exception, "ReadFailed", ErrorCategory.ReadError, file));
        }
        catch (UnauthorizedAccessException exception)
        {
            WriteError(CreateError(exception, "ReadAccessDenied", ErrorCategory.PermissionDenied, file));
        }
    }

    /// <summary>
    /// 移植した ImportCsvHelper でレコードを読み、1 件ずつ出力する
    /// </summary>
    /// <param name="verifiedCodePage">先頭 N 行の判定で検証したコードページ。全体の判定・明示の場合は 0。</param>
    private void ReadRecords(string file, EncodingSpec spec, int verifiedCodePage)
    {
        // 読み込み中の間は、下流の書き込み系コマンドが同じファイルに書こうとしたら検出できるようにしておく
        // （Import-ProbedCsv a.csv | Export-ProbedCsv a.csv）
        using (ActiveReadRegistry.Register(file))
        using (ProbedFileReader reader = ProbedFileReader.Open(file, spec, this.DetectorOptions))
        {
            var helper = new ImportCsvHelper(
                this.WriteObject,
                this.WriteWarning,
                this._delimiter,
                this.Header,
                null /* typeName */,
                reader.Reader);

            try
            {
                helper.Import(ref this._alreadyWarnedUnspecifiedNames);
            }
            catch (ExtendedTypeSystemException exception)
            {
                // 列名の重複。ヘッダーを読んだ時点で分かるので、レコードは 1 件も出力していない。
                // 標準は終了エラーだが、本モジュールではそのファイルだけを失敗にして次へ進む（24 章 U3）。
                // ID とカテゴリは標準と同じ。対象は、どのファイルかが分かるようにパスにする
                WriteError(CreateError(exception, DuplicateHeaderErrorId, ErrorCategory.NotSpecified, file));
            }
            catch (DecoderFallbackException)
            {
                // 検証を通った後の解析で復号に失敗した（検証の後にファイルが書き換えられた場合など）。
                // それまでに出力したレコードは取り消せない（仕様書 10 章）
                WriteError(CreateError(
                    new InvalidDataException(BuildInvalidBytesDuringImportMessage(file, verifiedCodePage, reader.HadBom)),
                    "InvalidBytesDuringImport",
                    ErrorCategory.InvalidData,
                    file));
            }
        }
    }

    /// <summary>
    /// 解析の途中で復号に失敗した場合のメッセージを組み立てる。位置は検証の部品でもう一度走査して求める。
    /// </summary>
    /// <remarks>
    /// <see cref="DecoderFallbackException.Index"/> は .NET Framework と .NET Core で基準が違うため使わない。
    /// 再走査でも見つからなかった場合（ファイルが再び書き換えられた場合など）は、位置を "?" とする。
    /// </remarks>
    private static string BuildInvalidBytesDuringImportMessage(string file, int codePage, bool hadBom)
    {
        StrictDecodeFailure? failure = null;

        try
        {
            failure = StrictDecodeVerifier.Verify(file, codePage);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValidationMessages.InvalidBytesDuringImport(
            file,
            EncodingVocabulary.GetUnifiedName(codePage, hadBom),
            failure == null ? "?" : failure.Offset.ToString(CultureInfo.InvariantCulture),
            failure == null ? string.Empty : ByteSequenceText.Format(failure.Bytes));
    }
}
