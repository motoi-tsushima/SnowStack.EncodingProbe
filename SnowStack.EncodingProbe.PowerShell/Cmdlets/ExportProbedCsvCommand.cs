using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using SnowStack.EncodingProbe.PowerShell.Internal;
using SnowStack.EncodingProbe.PowerShell.Internal.Csv;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// オブジェクトを CSV の行に変換し、文字エンコーディング・BOM・改行を指定してファイルに書き出すコマンドレット
/// </summary>
/// <remarks>
/// CSV の文字列化は PowerShell 7.x の Export-Csv から移植した処理（<see cref="ExportCsvHelper"/>）で行い、
/// ホストの Export-Csv / ConvertTo-Csv は呼ばない。そのため両ホストでバイト単位まで同じ CSV になる（1.3.0 仕様書 第 4 部）。
/// <br/>
/// 符号化の層（文字エンコーディング・BOM・改行・追記・ファイルを開く時点・読み取り専用・-WhatIf）は
/// Out-ProbedFile の規則に従い、殻は <see cref="ProbedFileOutputCommandBase"/> を共用する。
/// <br/>
/// -Culture（文字エンコーディングの判定に使うカルチャー。基底クラス）と -UseCulture（区切り文字に
/// カルチャーのリスト区切り文字を使う）は別のものである。パラメータセット名の Culture は -UseCulture の側を指す。
/// <br/>
/// 出力先は常に 1 件。失敗はすべて終了エラーとする（Out-ProbedFile と同じ）。
/// </remarks>
[Cmdlet(
    VerbsData.Export,
    "ProbedCsv",
    DefaultParameterSetName = DelimiterParameterSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(void))]
public sealed class ExportProbedCsvCommand : ProbedFileOutputCommandBase
{
    // パラメータセットは標準の Export-Csv と同じ 2 つ（Delimiter / UseCulture）。名前は移植した SetDelimiter が見る名前と同じ。
    // -Path / -LiteralPath はどちらのセットにも入れ、必須にせず、BeginProcessing で「ちょうど一方」を検査する（標準と同じ）。
    // 仕様書 15 章の当初の 4 セット（Import-ProbedCsv と同じ構成）では、パイプラインから入力を受けて -LiteralPath を指定すると
    // DelimiterLiteralPath と CultureLiteralPath のどちらか決まらず AmbiguousParameterSet になった（第三次の決定で改めた）

    /// <summary>-Delimiter のパラメータセット名（既定）</summary>
    private const string DelimiterParameterSet = "Delimiter";

    /// <summary>-UseCulture のパラメータセット名</summary>
    private const string UseCultureParameterSet = "UseCulture";

    /// <summary>区切り文字（BeginProcessing で決める）</summary>
    private char _delimiter;

    /// <summary>各行の後ろに付ける改行</summary>
    private string _lineBreak = default!;

    /// <summary>移植した書き出し処理</summary>
    private ExportCsvHelper? _helper;

    /// <summary>列（最初のオブジェクトで決まる）</summary>
    private IList<string>? _propertyNames;

    /// <summary>
    /// 追記先のヘッダーの列。追記先に内容が無い、またはヘッダーが読めなかった場合は null（列の照合をしない。移植元と同じ）
    /// </summary>
    private IList<string>? _preexistingPropertyNames;

    /// <summary>ヘッダー行（と #TYPE 行）を書くかどうか</summary>
    private bool _writeHeader;

    /// <summary>最初のレコードの前に改行を補うかどうか（追記先の最後の行に改行が無い場合。仕様書 18.3）</summary>
    private bool _needsLeadingLineBreak;

    /// <summary>ShouldProcess が拒否された（-WhatIf を含む）。入力を受け取って捨てる。</summary>
    private bool _discard;

    /// <summary>BeginProcessing を最後まで終えたかどうか</summary>
    private bool _prepared;

    /// <summary>
    /// 書き出すオブジェクト
    /// </summary>
    /// <remarks>
    /// ByPropertyName は持たない（Out-ProbedFile と同じ。意図しない束縛を避ける。仕様書 21 章）。
    /// </remarks>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public PSObject InputObject { get; set; } = default!;

    /// <summary>
    /// 出力先のパス。ワイルドカードは、ちょうど 1 件のファイルに解決されること。
    /// </summary>
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>
    /// 出力先のパス。ワイルドカードを展開せず、そのままのパスとして扱う。
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    [Alias("PSPath", "LP")]
    public string? LiteralPath { get; set; }

    /// <summary>
    /// 区切り文字。省略時は ','。
    /// </summary>
    [Parameter(Position = 1, ParameterSetName = DelimiterParameterSet)]
    [ValidateNotNull]
    public char Delimiter { get; set; }

    /// <summary>
    /// 区切り文字に、現在のカルチャーのリスト区切り文字を使う。文字エンコーディングの判定に使う -Culture とは別のものである。
    /// </summary>
    [Parameter(ParameterSetName = UseCultureParameterSet)]
    public SwitchParameter UseCulture { get; set; }

    /// <summary>
    /// 使用する文字エンコーディング。位置パラメータにしない（標準では位置 1 が -Delimiter のため）。
    /// 省略時は新規・上書きなら utf8NoBOM、-Append なら追記先から継承する。
    /// 明示的な Auto は出力先の既存ファイルから継承する（無ければ終了エラー）。
    /// </summary>
    [Parameter]
    [EncodingSpecTransformation(EncodingUsage.Write)]
    public object Encoding { get; set; } = EncodingSpec.Auto;

    /// <summary>
    /// 文字エンコーディング・BOM・改行コードの継承元とするファイル。
    /// </summary>
    [Parameter]
    public string EncodingFrom { get; set; } = default!;

    /// <summary>
    /// 各レコード（とヘッダー）の後ろに付ける改行。値の中の改行は置き換えない。
    /// </summary>
    [Parameter]
    public LineBreakOption LineBreak { get; set; } = LineBreakOption.Auto;

    /// <summary>
    /// 既存ファイルの末尾に追記する
    /// </summary>
    [Parameter]
    public SwitchParameter Append { get; set; }

    /// <summary>
    /// 追記の整合性検査を行わない（-Append のときだけ有効）
    /// </summary>
    [Parameter]
    public SwitchParameter AllowEncodingChange { get; set; }

    /// <summary>
    /// 読み取り専用属性の付いたファイルへも書き込む（属性は書き込み後に元へ戻す）。
    /// -Append で、追記先の列に当たるプロパティが無いオブジェクトも許容する（その列を空にして書く）。
    /// </summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// 既存ファイルを上書きしない（-Append と同時に指定した場合は -Append が優先される）
    /// </summary>
    [Parameter]
    [Alias("NoOverwrite")]
    public SwitchParameter NoClobber { get; set; }

    /// <summary>
    /// ヘッダー行を書かない
    /// </summary>
    [Parameter]
    public SwitchParameter NoHeader { get; set; }

    /// <summary>
    /// #TYPE 行を書く
    /// </summary>
    [Parameter]
    [Alias("ITI")]
    public SwitchParameter IncludeTypeInformation { get; set; }

    /// <summary>
    /// 何もしない（受け付けるだけ）。Windows PowerShell 5.1 向けに書かれたスクリプトがそのまま動くようにする。
    /// </summary>
    [Parameter]
    [Alias("NTI")]
    public SwitchParameter NoTypeInformation { get; set; }

    /// <summary>
    /// 引用符の付け方。既定は Always。Windows PowerShell 5.1 でも使える。
    /// </summary>
    [Parameter]
    [Alias("UQ")]
    public CsvQuoteKind UseQuotes { get; set; } = CsvQuoteKind.Always;

    /// <summary>
    /// 引用符で囲む列名（大文字小文字を区別しない）。Windows PowerShell 5.1 でも使える。
    /// </summary>
    [Parameter]
    [Alias("QF")]
    public string[]? QuoteFields { get; set; }

    /// <summary>
    /// 追記先・出力先・参照ファイルの判定に使う先頭の行数。指定すると、ファイルに書く前に全体を検証する。
    /// </summary>
    [Parameter]
    [ValidateRange(1L, long.MaxValue)]
    public long DetectionLineCount { get; set; }

    /// <inheritdoc />
    private protected override string OperationName => "Export-ProbedCsv";

    /// <inheritdoc />
    private protected override bool AppendRequested => this.Append.IsPresent;

    /// <inheritdoc />
    private protected override bool ForceRequested => this.Force.IsPresent;

    /// <summary>-DetectionLineCount が指定されていればその値、無ければ 0（全体で判定する）</summary>
    private long LineCountOrZero
        => this.MyInvocation.BoundParameters.ContainsKey(nameof(this.DetectionLineCount)) ? this.DetectionLineCount : 0;

    /// <summary>
    /// パラメータを検証し、出力先と文字エンコーディングを決め、追記先のヘッダーを読む。ファイルはまだ開かない。
    /// </summary>
    /// <remarks>
    /// 検査の順序は仕様書 19 章（1.2.0 仕様書 2.10 と同じ）。ファイルを変更しない検査（判定・検証・ヘッダーの読み取りを含む）は
    /// ShouldProcess より前に行い、-WhatIf でも同じ ID のエラーを報告する。
    /// </remarks>
    protected override void BeginProcessing()
    {
        // -Culture / -Strategy の検証を先に済ませる。後段の判定がその結果を使うためである
        base.BeginProcessing();

        if (this.MyInvocation.BoundParameters.ContainsKey(nameof(this.QuoteFields))
            && this.MyInvocation.BoundParameters.ContainsKey(nameof(this.UseQuotes)))
        {
            // 標準と同じ ID とカテゴリ
            ThrowTerminatingError(CreateError(
                new InvalidOperationException(ValidationMessages.CannotSpecifyQuoteFieldsAndUseQuotes()),
                "CannotSpecifyQuoteFieldsAndUseQuotes",
                ErrorCategory.InvalidData,
                null));
        }

        bool pathBound = this.MyInvocation.BoundParameters.ContainsKey(nameof(this.Path));
        bool literalPathBound = this.MyInvocation.BoundParameters.ContainsKey(nameof(this.LiteralPath));

        if (pathBound == literalPathBound)
        {
            // -Path と -LiteralPath のちょうど一方を求める（標準と同じ ID とカテゴリ）
            ThrowTerminatingError(CreateError(
                new InvalidOperationException(ValidationMessages.CannotSpecifyPathAndLiteralPath()),
                "CannotSpecifyPathAndLiteralPath",
                ErrorCategory.InvalidData,
                null));
        }

        bool encodingBound = this.MyInvocation.BoundParameters.ContainsKey(nameof(this.Encoding));
        bool encodingFromBound = this.MyInvocation.BoundParameters.ContainsKey(nameof(this.EncodingFrom));

        if (encodingBound && encodingFromBound)
        {
            ThrowTerminatingError(CreateError(
                new PSArgumentException(ValidationMessages.EncodingAndEncodingFromAreExclusive()),
                "EncodingAndEncodingFromAreExclusive",
                ErrorCategory.InvalidArgument,
                null));
        }

        if (this.AllowEncodingChange.IsPresent && !this.Append.IsPresent)
        {
            // エラーにしない。スクリプトで -Append を動的に切り替える書き方を壊さないため
            WriteWarning(ValidationMessages.AllowEncodingChangeWithoutAppend());
        }

        this._delimiter = ImportExportCSVHelper.SetDelimiter(this.ParameterSetName, this.Delimiter, this.UseCulture.IsPresent);

        this.OutputPath = ResolveOutputPath(literalPathBound ? this.LiteralPath! : this.Path!, literalPathBound);

        bool exists = File.Exists(this.OutputPath);

        CheckNoClobberAndReadOnly(exists, this.NoClobber.IsPresent);

        // -EncodingFrom の参照ファイルも、-DetectionLineCount 指定時は全体を検証する（24 章 U2）
        EncodingSpec? inherited = encodingFromBound ? ResolveEncodingFrom(this.EncodingFrom, this.LineCountOrZero) : null;

        EncodingSpec? existing = ResolveEncoding(inherited, encodingBound, exists);

        PrepareAppend(existing);

        if (!ShouldProcess(this.OutputPath, this.OperationName))
        {
            this._discard = true;
            return;
        }

        this._lineBreak = LineBreakResolver.Resolve(this.LineBreak, this.OutputSpec.LineBreak);
        this._helper = new ExportCsvHelper(this._delimiter, this.UseQuotes, this.QuoteFields);
        this._prepared = true;
    }

    /// <summary>
    /// 1 レコードを CSV の行にして書く。最初のレコードで列を決め、ヘッダーを書く。
    /// </summary>
    protected override void ProcessRecord()
    {
        if (this._discard || !this._prepared || this.InputObject == null)
        {
            return;
        }

        if (this._propertyNames == null)
        {
            // 列を決め、順序を固定する（移植元の ExportCsvCommand.ProcessRecord と同じ順序）
            this._propertyNames = ExportCsvHelper.BuildPropertyNames(this.InputObject, null);

            if (this._preexistingPropertyNames != null)
            {
                this._propertyNames = ReconcileWithExistingHeader(this._propertyNames, this._preexistingPropertyNames);
                RejectEmptyPropertyName(this._propertyNames);
            }

            if (this._writeHeader)
            {
                if (this.IncludeTypeInformation.IsPresent)
                {
                    WriteCsvLine(ExportCsvHelper.GetTypeString(this.InputObject));
                }

                WriteCsvLine(this._helper!.ConvertPropertyNamesCSV(this._propertyNames));
            }
        }

        WriteCsvLine(this._helper!.ConvertPSObjectToCSV(this.InputObject, this._propertyNames));
    }

    /// <summary>
    /// ファイルを閉じる。入力が 0 件なら 0 バイトのファイルを作る（追記は既存ファイルに触れない）。
    /// </summary>
    protected override void EndProcessing()
    {
        if (this._discard || !this._prepared)
        {
            return;
        }

        if (!this.WriterOpened)
        {
            CreateEmptyOutput();
        }

        CompleteOutput();
        Cleanup();
    }

    /// <summary>
    /// 書き込みに使う文字エンコーディングと、追記の整合性検査の相手を決める（仕様書 16 章）。
    /// </summary>
    /// <remarks>
    /// 規則は Out-ProbedFile（1.2.0 仕様書 2.5）と同じ。違いは次の 2 点。
    /// <list type="bullet">
    /// <item>1 バイト以上の追記先は常に判定する（ヘッダーの読み取りと、末尾の改行の判定に要るため。第三次の決定）。
    /// Out-ProbedFile は、-Encoding の明示と -AllowEncodingChange を同時に指定した場合に判定しない</item>
    /// <item>-DetectionLineCount を指定した場合は、先頭 N 行で判定し、読み替え、全体を検証する（仕様書 16.2）</item>
    /// </list>
    /// </remarks>
    /// <returns>判定した追記先・出力先の情報。判定しなかった場合は null。</returns>
    private EncodingSpec? ResolveEncoding(EncodingSpec? inherited, bool encodingBound, bool exists)
    {
        bool hasContent = exists && new FileInfo(this.OutputPath).Length > 0;
        bool append = this.Append.IsPresent;
        EncodingSpec specified = EncodingSpec.FromBoundParameter(this.Encoding);

        EncodingSpec? existing = null;

        if (hasContent && (append || (encodingBound && specified.IsAuto)))
        {
            existing = DetectTarget();
        }

        if (inherited != null)
        {
            this.OutputSpec = inherited;
        }
        else if (!encodingBound)
        {
            // 省略: 上書きは utf8NoBOM。追記は追記先から継承し、無い・0 バイトなら utf8NoBOM
            this.OutputSpec = append && existing != null ? existing : EncodingVocabulary.Utf8NoBom();
        }
        else if (specified.IsAuto)
        {
            // 明示的な Auto: 出力先が無ければ終了エラー。0 バイトは EncodingInheritance の規則に従う
            if (!exists)
            {
                ThrowTerminatingError(CreateError(
                    new FileNotFoundException(ValidationMessages.AutoEncodingRequiresExistingFile(this.OutputPath), this.OutputPath),
                    "AutoEncodingRequiresExistingFile",
                    ErrorCategory.ObjectNotFound,
                    this.OutputPath));
            }

            this.OutputSpec = existing ?? EncodingInheritance.ForEmptySource();
        }
        else
        {
            this.OutputSpec = specified;
        }

        this.AppendToExistingContent = append && hasContent;
        this.AppendBaseline = append && !this.AllowEncodingChange.IsPresent ? existing?.Encoding : null;

        return existing;
    }

    /// <summary>
    /// 出力先・追記先の既存ファイル（1 バイト以上）を判定する。失敗は終了エラー。
    /// </summary>
    private EncodingSpec DetectTarget()
    {
        long lineCount = this.LineCountOrZero;

        if (lineCount == 0)
        {
            return DetectExisting();
        }

        try
        {
            // 先頭 N 行で判定し、読み替え、ファイルに書く前に全体を検証する（仕様書 16.2）
            return HeadLineEncodingResolver.Resolve(this.OutputPath, lineCount, this.DetectorOptions).ToSpec();
        }
        catch (EncodingDetectionException exception)
        {
            ThrowTerminatingError(CreateError(exception, exception.ErrorId, ErrorCategory.InvalidData, this.OutputPath));
        }
        catch (DetectionVerificationException exception)
        {
            ThrowTerminatingError(CreateError(
                exception, DetectionVerificationException.ErrorId, ErrorCategory.InvalidData, this.OutputPath));
        }

        throw new InvalidOperationException();  // ThrowTerminatingError は戻らないが、コンパイラには分からない
    }

    /// <summary>
    /// 追記先のヘッダーを読み、ヘッダーを書くかどうかと、末尾に改行を補うかどうかを決める（仕様書 18 章）。
    /// </summary>
    /// <param name="existing">判定した追記先の情報。追記先に内容が無い場合は null。</param>
    private void PrepareAppend(EncodingSpec? existing)
    {
        // 新規作成・上書き・追記先が無い / 0 バイトの追記: ヘッダーを書く（-NoHeader なら書かない）
        this._writeHeader = !this.NoHeader.IsPresent;

        if (!this.AppendToExistingContent || existing == null)
        {
            return;
        }

        int bomLength = ReadBomLength();
        long bodyLength = new FileInfo(this.OutputPath).Length - bomLength;

        if (bodyLength == 0)
        {
            // BOM だけの追記先は、ヘッダーの扱いだけ新規と同じにする（第三次の決定）。
            // ヘッダーは書き、BOM はすでにあるので書かない（AppendToExistingContent のまま）。
            // 7.x の Export-Csv も、BOM の後ろが空の追記先には新規と同じくヘッダーを書く
            return;
        }

        // 内容のある追記先: ヘッダー行と #TYPE 行は書かない。列は追記先のヘッダーに合わせる
        this._writeHeader = false;
        this._preexistingPropertyNames = ReadExistingHeader(existing);
        this._needsLeadingLineBreak = !EndsWithLineBreak(existing.Encoding!.CodePage);
    }

    /// <summary>
    /// 追記先のヘッダーを、移植した ReadHeader で読む（判定した文字エンコーディングで、BOM の後ろから復号する）。
    /// ヘッダーが読めない（空行と # の行だけなど）場合は null（移植元と同じく、列の照合をしない）。
    /// </summary>
    private IList<string>? ReadExistingHeader(EncodingSpec existing)
    {
        try
        {
            using (ProbedFileReader reader = ProbedFileReader.Open(
                this.OutputPath, EncodingSpec.Create(existing.Encoding!, emitBom: false), this.DetectorOptions))
            {
                var helper = new ImportCsvHelper(_ => { }, _ => { }, this._delimiter, null, null, reader.Reader);
                helper.ReadHeader();
                return helper.Header;
            }
        }
        catch (ExtendedTypeSystemException exception)
        {
            // 追記先のヘッダーに同じ列名が複数ある。Import-ProbedCsv と同じ ID にそろえる
            ThrowTerminatingError(CreateError(
                exception, "AlreadyPresentPSMemberInfoInternalCollectionAdd", ErrorCategory.NotSpecified, this.OutputPath));
        }
        catch (IOException exception)
        {
            ThrowTerminatingError(CreateError(exception, "ReadFailed", ErrorCategory.ReadError, this.OutputPath));
        }
        catch (UnauthorizedAccessException exception)
        {
            ThrowTerminatingError(CreateError(exception, "ReadAccessDenied", ErrorCategory.PermissionDenied, this.OutputPath));
        }

        throw new InvalidOperationException();  // ThrowTerminatingError は戻らないが、コンパイラには分からない
    }

    /// <summary>
    /// 追記先の末尾が改行（LF または CR）で終わっているかどうか（仕様書 18.3）。
    /// 改行の単位は追記先の文字エンコーディングで決まる（UTF-16 は最後の 2 バイト、UTF-32 は最後の 4 バイト）。
    /// </summary>
    private bool EndsWithLineBreak(int codePage)
    {
        int unit;
        bool bigEndian;

        switch (codePage)
        {
            case EncodingVocabulary.CodePageUtf16Le:
                unit = 2;
                bigEndian = false;
                break;
            case EncodingVocabulary.CodePageUtf16Be:
                unit = 2;
                bigEndian = true;
                break;
            case EncodingVocabulary.CodePageUtf32Le:
                unit = 4;
                bigEndian = false;
                break;
            case EncodingVocabulary.CodePageUtf32Be:
                unit = 4;
                bigEndian = true;
                break;
            default:
                unit = 1;
                bigEndian = false;
                break;
        }

        using (var stream = new FileStream(this.OutputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (stream.Length < unit)
            {
                return false;
            }

            var last = new byte[unit];
            stream.Seek(-unit, SeekOrigin.End);

            int read = 0;

            while (read < unit)
            {
                int count = stream.Read(last, read, unit - read);

                if (count == 0)
                {
                    return false;
                }

                read += count;
            }

            long value = 0;

            for (int i = 0; i < unit; i++)
            {
                value = bigEndian ? (value << 8) | last[i] : value | ((long)last[i] << (8 * i));
            }

            return value == 0x0A || value == 0x0D;
        }
    }

    /// <summary>
    /// ファイル先頭の BOM の長さ
    /// </summary>
    private int ReadBomLength()
    {
        var head = new byte[4];
        int read;

        using (var stream = new FileStream(this.OutputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            read = stream.Read(head, 0, head.Length);
        }

        if (read < head.Length)
        {
            Array.Resize(ref head, read);
        }

        return ProbedFileReader.GetBomLength(head);
    }

    /// <summary>
    /// 追記するオブジェクトの列を、追記先のヘッダーの列に合わせる（移植した ReconcilePreexistingPropertyNames）。
    /// 追記先の列に当たるプロパティが無ければ終了エラー（-Force なら空にして書く）。
    /// </summary>
    private IList<string> ReconcileWithExistingHeader(IList<string> propertyNames, IList<string> preexisting)
    {
        try
        {
            return ImportExportCSVHelper.ReconcilePreexistingPropertyNames(
                propertyNames, preexisting, this.Force.IsPresent, this.OutputPath);
        }
        catch (CsvAppendMismatchException exception)
        {
            // 標準と同じ ID・カテゴリ・対象（列名）
            ThrowTerminatingError(CreateError(
                exception, "CannotAppendCsvWithMismatchedPropertyNames", ErrorCategory.InvalidData, exception.PropertyName));
        }

        throw new InvalidOperationException();  // ThrowTerminatingError は戻らないが、コンパイラには分からない
    }

    /// <summary>
    /// 追記先のヘッダーに空の列名があれば終了エラーにする（第五次）。
    /// </summary>
    /// <remarks>
    /// 移植元は、-Force で照合を通った後、空の名前でプロパティを引こうとして PSArgumentException（ID は汎用の Argument、
    /// 文言は .NET の「"name" の値が無効」）で止まる（7.6.7 と 5.1 の実測。実装記録 9 章）。止まる点は同じにし、
    /// ID とメッセージを本モジュールのものにする。-Force が無い場合は、照合が先に CannotAppendCsvWithMismatchedPropertyNames
    /// で止めるので（移植元と同じ）、ここには来ない。何も書く前なので、ファイルは変わらない。
    /// </remarks>
    private void RejectEmptyPropertyName(IList<string> propertyNames)
    {
        foreach (string name in propertyNames)
        {
            if (string.IsNullOrEmpty(name))
            {
                ThrowTerminatingError(CreateError(
                    new InvalidOperationException(ValidationMessages.CsvAppendEmptyPropertyName(this.OutputPath)),
                    "CannotAppendCsvWithEmptyPropertyName",
                    ErrorCategory.InvalidData,
                    this.OutputPath));
            }
        }
    }

    /// <summary>
    /// CSV の 1 行を、改行を付けて書く。追記先の最後の行に改行が無ければ、最初の行の前に改行を補う（仕様書 18.3）。
    /// 補う改行も整合性検査の対象である。
    /// </summary>
    private void WriteCsvLine(string line)
    {
        if (this._needsLeadingLineBreak)
        {
            this._needsLeadingLineBreak = false;
            WriteChunk(this._lineBreak);
        }

        WriteChunk(line + this._lineBreak);
    }

    /// <summary>
    /// ファイルを閉じ（読み取り専用属性もここで戻る）、書き出し処理を破棄する
    /// </summary>
    private protected override void Cleanup()
    {
        base.Cleanup();

        ExportCsvHelper? helper = this._helper;
        this._helper = null;
        helper?.Dispose();

        this._prepared = false;
    }
}
