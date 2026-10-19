using System;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// オブジェクトを整形し、文字エンコーディング・BOM・改行を統一語彙で指定してファイルへ書き出すコマンドレット
/// </summary>
/// <remarks>
/// 標準の Out-File のパラメータをすべて持ち、Set-ProbedContent / Add-ProbedContent 由来のパラメータを加えたもの
/// （1.2.0 仕様書 第 1 部）。
/// <br/>
/// 符号化の層（エンコーディング・BOM・改行）は PowerShell 5.1 と 7.x で同一の結果を保証する。
/// 整形の層（オブジェクトを文字列にする処理）は実行中のホストの Out-String に委ね、同一性を保証しない。
/// 自前の整形処理は持たない。
/// <br/>
/// ファイルを開く（作成・切り詰める）のは最初の行を書く直前である。標準の Out-File は BeginProcessing で
/// 切り詰めるため、Get-ProbedContent a.txt | Out-File a.txt は上流が読む前に内容を消してしまう。
/// 開くのを遅らせることで、この往復を <see cref="ActiveReadRegistry"/> で検出してエラーにできる。
/// <br/>
/// 出力先は常に 1 件であり、失敗はすべて終了エラーとする（標準の Out-File と同じ）。
/// 非終了エラーにすると、上流からレコードが届くたびに同じエラーが繰り返されるためである。
/// <br/>
/// パスの解決・事前の検査・ファイルを開く時点・空の入力・後始末は、1.3.0 で Export-ProbedCsv と共用するために
/// <see cref="ProbedFileOutputCommandBase"/> へ移した（挙動は変えていない）。
/// </remarks>
[Cmdlet(
    VerbsData.Out,
    "ProbedFile",
    DefaultParameterSetName = ByPathParameterSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(void))]
public sealed class OutProbedFileCommand : ProbedFileOutputCommandBase
{
    /// <summary>-FilePath のパラメータセット名（標準の Out-File と同じ）</summary>
    private const string ByPathParameterSet = "ByPath";

    /// <summary>-LiteralPath のパラメータセット名（標準の Out-File と同じ）</summary>
    private const string ByLiteralPathParameterSet = "ByLiteralPath";

    /// <summary>各行の後ろに付ける改行</summary>
    private string _lineBreak = default!;

    /// <summary>整形に使う Out-String -Stream のステッパブルパイプライン</summary>
    private SteppablePipeline? _formatter;

    /// <summary>ShouldProcess が拒否された（-WhatIf を含む）。入力を受け取って捨てる。</summary>
    private bool _discard;

    /// <summary>BeginProcessing を最後まで終えたかどうか</summary>
    private bool _prepared;

    /// <summary>
    /// 書き込み先のパス。ワイルドカードは、ちょうど 1 件のファイルに解決されること。
    /// </summary>
    /// <remarks>
    /// 別名 -Path は標準では PowerShell 7 にしか無いが、本コマンドは 5.1 でも提供する。
    /// </remarks>
    [Parameter(ParameterSetName = ByPathParameterSet, Mandatory = true, Position = 0)]
    [Alias("Path")]
    public string FilePath { get; set; } = default!;

    /// <summary>
    /// 書き込み先のパス。ワイルドカードを展開せず、そのままのパスとして扱う。
    /// </summary>
    /// <remarks>
    /// 標準の Out-File は ValueFromPipelineByPropertyName を持つが、本コマンドは持たない。
    /// Get-ChildItem | Out-ProbedFile で PSPath が意図せず束縛される紛らわしさを避けるためである。
    /// 別名 -LP は標準では PowerShell 7 にしか無いが、本コマンドは 5.1 でも提供する。
    /// </remarks>
    [Parameter(ParameterSetName = ByLiteralPathParameterSet, Mandatory = true)]
    [Alias("PSPath", "LP")]
    public string LiteralPath { get; set; } = default!;

    /// <summary>
    /// 書き出すオブジェクト
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public PSObject? InputObject { get; set; }

    /// <summary>
    /// 使用する文字エンコーディング。
    /// 省略時は上書き・新規作成なら utf8NoBOM、-Append なら追記先から継承する。
    /// 明示的な Auto は出力先の既存ファイルから継承する（無ければ終了エラー）。
    /// </summary>
    [Parameter(Position = 1)]
    [EncodingSpecTransformation(EncodingUsage.Write)]
    public object Encoding { get; set; } = EncodingSpec.Auto;

    /// <summary>
    /// 文字エンコーディング・BOM・改行コードの継承元とするファイル。
    /// ワイルドカードは展開せず、そのままのパスとして扱う。
    /// </summary>
    [Parameter]
    public string EncodingFrom { get; set; } = default!;

    /// <summary>
    /// 各行の後ろに付ける改行。省略時は参照情報があればそれを継承し、無ければ OS 既定に従う。
    /// </summary>
    [Parameter]
    public LineBreakOption LineBreak { get; set; } = LineBreakOption.Auto;

    /// <summary>
    /// 既存ファイルの末尾に追記する
    /// </summary>
    [Parameter]
    public SwitchParameter Append { get; set; }

    /// <summary>
    /// 追記先と異なるバイト列になる追記を許可する（-Append のときだけ有効）
    /// </summary>
    [Parameter]
    public SwitchParameter AllowEncodingChange { get; set; }

    /// <summary>
    /// 既存ファイルを上書きしない（-Append と同時に指定した場合は -Append が優先される）
    /// </summary>
    [Parameter]
    [Alias("NoOverwrite")]
    public SwitchParameter NoClobber { get; set; }

    /// <summary>
    /// 読み取り専用属性の付いたファイルへも書き込む。属性は書き込み後に元へ戻す。
    /// </summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// 1 行の文字数。省略時は Out-String の既定（ホスト依存）に任せる。
    /// </summary>
    [Parameter]
    [ValidateRange(2, int.MaxValue)]
    public int Width { get; set; }

    /// <summary>
    /// 各行を区切りなしで連結して書く。最後の行の後ろにも改行を付けない。
    /// </summary>
    [Parameter]
    public SwitchParameter NoNewline { get; set; }


    /// <inheritdoc />
    private protected override string OperationName => "Out-ProbedFile";

    /// <inheritdoc />
    private protected override bool AppendRequested => this.Append.IsPresent;

    /// <inheritdoc />
    private protected override bool ForceRequested => this.Force.IsPresent;

    /// <summary>
    /// パラメータを検証し、出力先を決め、整形のパイプラインを開始する。ファイルはまだ開かない。
    /// </summary>
    protected override void BeginProcessing()
    {
        // -Culture / -Strategy の検証を先に済ませる。後段の判定がその結果を使うためである。
        base.BeginProcessing();

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

        if (this.NoNewline.IsPresent && this.MyInvocation.BoundParameters.ContainsKey(nameof(this.LineBreak)))
        {
            WriteWarning(ValidationMessages.NoNewlineIgnoresLineBreak());
        }

        if (this.AllowEncodingChange.IsPresent && !this.Append.IsPresent)
        {
            // エラーにしない。スクリプトで -Append を動的に切り替える書き方を壊さないため
            WriteWarning(ValidationMessages.AllowEncodingChangeWithoutAppend());
        }

        bool literal = this.ParameterSetName == ByLiteralPathParameterSet;
        this.OutputPath = ResolveOutputPath(literal ? this.LiteralPath : this.FilePath, literal);

        // ファイルを変更しない検査（-NoClobber・読み取り専用・文字エンコーディングの決定）は、
        // ShouldProcess より前に行う。-WhatIf でも、実行すれば失敗することを報告するため（1.2.0 手動確認後の修正）
        bool exists = File.Exists(this.OutputPath);

        CheckNoClobberAndReadOnly(exists, this.NoClobber.IsPresent);

        EncodingSpec? inherited = encodingFromBound ? ResolveEncodingFrom(this.EncodingFrom) : null;

        ResolveEncoding(inherited, encodingBound, exists);

        if (!ShouldProcess(this.OutputPath, this.OperationName))
        {
            this._discard = true;
            return;
        }

        this._lineBreak = LineBreakResolver.Resolve(this.LineBreak, this.OutputSpec.LineBreak);
        this._formatter = CreateFormatter();

        // Begin(this) は使わない。コマンドを渡すと Out-String の出力が本コマンドの出力ストリームへ
        // 直接流れ（プロキシコマンドの動作）、Process / End の戻り値が空になるため。
        // Begin(true) なら出力が Process / End の戻り値として返り、行として書き込める。
        this._formatter.Begin(expectInput: true);
        this._prepared = true;
    }

    /// <summary>
    /// 1 レコードを整形し、得られた行を書く
    /// </summary>
    protected override void ProcessRecord()
    {
        if (this._discard || !this._prepared)
        {
            return;
        }

        WriteLines(this._formatter!.Process(this.InputObject));
    }

    /// <summary>
    /// 整形の残りを書き出してファイルを閉じる。入力が 0 件なら 0 バイトのファイルを作る。
    /// </summary>
    protected override void EndProcessing()
    {
        if (this._discard || !this._prepared)
        {
            return;
        }

        WriteLines(this._formatter!.End());

        if (!this.WriterOpened)
        {
            // BOM は 1 行でも書いたときにだけ書く。入力が 0 件なら、
            // 上書きは 0 バイトに切り詰め、追記は既存ファイルに触れない（無ければ 0 バイトで作る）
            CreateEmptyOutput();
        }

        Cleanup();
    }

    /// <summary>
    /// 書き込みに使う文字エンコーディングと、追記の整合性検査の相手を決める（仕様書 2.5 / 2.8）
    /// </summary>
    /// <param name="inherited">-EncodingFrom から得た継承情報。指定されていなければ null。</param>
    /// <param name="encodingBound">-Encoding が指定されたかどうか</param>
    /// <param name="exists">出力先が存在するかどうか</param>
    private void ResolveEncoding(EncodingSpec? inherited, bool encodingBound, bool exists)
    {
        bool hasContent = exists && new FileInfo(this.OutputPath).Length > 0;
        bool append = this.Append.IsPresent;
        EncodingSpec specified = EncodingSpec.FromBoundParameter(this.Encoding);

        // 追記先の判定。追記先から継承する場合と、整合性検査の相手が要る場合にだけ行う。
        // -Encoding を明示した場合も、検査のために判定する（Add-ProbedContent と同じ）
        EncodingSpec? existing = null;
        bool inheritsFromTarget = inherited == null && (!encodingBound || specified.IsAuto);
        bool needsBaseline = append && !this.AllowEncodingChange.IsPresent;

        if (hasContent && ((append && inheritsFromTarget) || needsBaseline || (encodingBound && specified.IsAuto)))
        {
            existing = DetectExisting();
        }

        if (inherited != null)
        {
            this.OutputSpec = inherited;
        }
        else if (!encodingBound)
        {
            // 省略: 上書きは utf8NoBOM（捨てる内容のために判定を走らせない）。
            // 追記は追記先から継承し、無い・0 バイトなら utf8NoBOM とする。
            // 0 バイトを Encoding.Default に落とさないのは、それが PowerShell 5.1 と 7.x で異なるため
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
        this.AppendBaseline = needsBaseline ? existing?.Encoding : null;
    }

    /// <summary>
    /// Out-String -Stream のステッパブルパイプラインを作る
    /// </summary>
    /// <remarks>
    /// モジュール修飾名で呼び、利用者が同名の関数を定義していても影響を受けないようにする。
    /// -Width は指定されたときだけ渡し、省略時は Out-String の既定（ホスト依存）に任せる。
    /// </remarks>
    private SteppablePipeline CreateFormatter()
    {
        string command = "Microsoft.PowerShell.Utility\\Out-String -Stream";

        if (this.MyInvocation.BoundParameters.ContainsKey(nameof(this.Width)))
        {
            command += " -Width " + this.Width.ToString(CultureInfo.InvariantCulture);
        }

        return ScriptBlock.Create(command).GetSteppablePipeline(CommandOrigin.Internal);
    }

    /// <summary>
    /// 整形で得られた要素を、1 要素 1 行として書く
    /// </summary>
    private void WriteLines(Array? lines)
    {
        if (lines == null)
        {
            return;
        }

        foreach (object? element in lines)
        {
            string line = element == null
                ? string.Empty
                : (element is PSObject psObject ? psObject.BaseObject : element).ToString() ?? string.Empty;

            WriteChunk(this.NoNewline.IsPresent ? line : line + this._lineBreak);
        }
    }

    /// <summary>
    /// ファイルを閉じ（読み取り専用属性もここで戻る）、整形のパイプラインを破棄する
    /// </summary>
    private protected override void Cleanup()
    {
        base.Cleanup();

        SteppablePipeline? formatter = this._formatter;
        this._formatter = null;
        formatter?.Dispose();

        this._prepared = false;
    }
}
