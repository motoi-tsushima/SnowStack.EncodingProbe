using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 既存のテキストファイルの文字エンコーディング・BOM・改行を変換して書き直すコマンドレット
/// </summary>
/// <remarks>
/// 文字列の置換など、内容の加工は行わない（1.2.0 仕様書 第 2 部）。
/// <br/>
/// 変換で文字を失わないことを保証する。変換元に不正なバイト列がある、または変換先で表現できない文字がある
/// ファイルは変換せず、元のまま残す。置き換えて変換する手段は設けない。
/// <br/>
/// 変換の順序は「復号 → 改行の変換 → 符号化（BOM 付加）→ 元のバイト列との比較 → 一時ファイルに書き込み → 置き換え」。
/// 同じフォルダーの一時ファイルに書いてから置き換えるため、途中で失敗しても元のファイルは壊れない。
/// <br/>
/// 対象の探索（再帰・絞り込み）は持たない。Get-ChildItem に任せる。
/// </remarks>
[Cmdlet(
    VerbsData.Convert,
    "ProbedContent",
    DefaultParameterSetName = PathParameterSet,
    SupportsShouldProcess = true,
    ConfirmImpact = ConfirmImpact.Medium)]
[OutputType(typeof(PSObject))]
public sealed class ConvertProbedContentCommand : ProbedContentCommandBase
{
    /// <summary>-PassThru の出力の型名。公開型にはせず、PSTypeNames の先頭に入れる。</summary>
    internal const string ResultTypeName = "SnowStack.EncodingProbe.ConvertResult";

    /// <summary>ShouldProcess に表示する操作名</summary>
    private const string OperationName = "Convert-ProbedContent";

    /// <summary>コードページ：UTF-8（変換元が空で、変換元の文字エンコーディングを決めようがない場合に使う）</summary>
    private const int CodePageUtf8 = 65001;

    /// <summary>-Destination への出力で、この実行の中で書いたファイル（ファイル名の重なりの検出に使う）</summary>
    private readonly HashSet<string> _writtenDestinations = new HashSet<string>(PathComparison.Comparer);

    /// <summary>変換先の文字エンコーディング。変換元を保つ場合は null。</summary>
    private EncodingSpec? _targetSpec;

    /// <summary>変換先の BOM。null は「変換先の文字エンコーディングの規則（または変換元）に従う」。</summary>
    private bool? _targetBom;

    /// <summary>変換先の改行。改行を保つ場合は null。</summary>
    private string? _targetLineBreak;

    /// <summary>-Destination の絶対パス。その場で変換する場合は null。</summary>
    private string? _destination;

    /// <summary>
    /// 変換するファイルのパス。ワイルドカードを使用できる。
    /// </summary>
    [Parameter(ParameterSetName = PathParameterSet, Mandatory = true, Position = 0)]
    public string[] Path { get; set; } = default!;

    /// <summary>
    /// 変換するファイルのパス。ワイルドカードを展開せず、そのままのパスとして扱う。
    /// Get-ChildItem の出力は PSPath でここに束縛される。
    /// </summary>
    [Parameter(ParameterSetName = LiteralPathParameterSet, Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath", "LP")]
    public string[] LiteralPath { get; set; } = default!;

    /// <summary>
    /// 変換先の文字エンコーディング（と BOM）。省略時は変換元の文字エンコーディングを保つ。
    /// </summary>
    [Parameter(Position = 1)]
    [ConvertEncodingTransformation]
    public object Encoding { get; set; } = default!;

    /// <summary>
    /// 変換先の文字エンコーディング・BOM・改行を、参照ファイルから継承する。
    /// </summary>
    [Parameter]
    public string EncodingFrom { get; set; } = default!;

    /// <summary>
    /// 変換先の BOM（Add / Remove）。省略時は BOM を変えない。
    /// </summary>
    [Parameter]
    public BomOption Bom { get; set; }

    /// <summary>
    /// 変換先の改行。ファイル内のすべての改行をこれに揃える。省略時（Auto）は改行を保つ。
    /// </summary>
    [Parameter]
    public LineBreakOption LineBreak { get; set; } = LineBreakOption.Auto;

    /// <summary>
    /// 変換元の文字エンコーディング。省略時（Auto）は検出する。
    /// </summary>
    [Parameter]
    [EncodingSpecTransformation(EncodingUsage.Read)]
    public object SourceEncoding { get; set; } = EncodingSpec.Auto;

    /// <summary>
    /// 出力先のフォルダー。省略時はその場で上書きする。
    /// </summary>
    [Parameter]
    public string Destination { get; set; } = default!;

    /// <summary>
    /// 読み取り専用のファイルも変換する（属性は書き込み後に元へ戻す）。
    /// -Destination では同名のファイルを上書きする。
    /// </summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// 変換したファイルごとに結果を出力する
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// パラメータを検証し、変換先の規則を決める。問題があれば 1 ファイルも処理せず終了エラーとする。
    /// </summary>
    protected override void BeginProcessing()
    {
        base.BeginProcessing();

        bool encodingBound = IsBound(nameof(this.Encoding));
        bool encodingFromBound = IsBound(nameof(this.EncodingFrom));
        bool bomBound = IsBound(nameof(this.Bom));
        bool lineBreakSpecified = IsBound(nameof(this.LineBreak)) && this.LineBreak != LineBreakOption.Auto;

        if (!encodingBound && !encodingFromBound && !bomBound && !lineBreakSpecified)
        {
            // E1: 何も変換しない
            ThrowArgumentError(ValidationMessages.NoConversionSpecified(), "NoConversionSpecified", null);
        }

        if (encodingBound && encodingFromBound)
        {
            // E3
            ThrowArgumentError(
                ValidationMessages.EncodingAndEncodingFromAreExclusive(), "EncodingAndEncodingFromAreExclusive", null);
        }

        if (encodingBound)
        {
            ResolveTargetFromEncoding(ConvertEncodingArgument.FromBoundParameter(this.Encoding), bomBound);
        }
        else if (encodingFromBound)
        {
            // E9 は ResolveEncodingFrom が終了エラーにする
            this._targetSpec = ResolveEncodingFrom(this.EncodingFrom);
            this._targetBom = bomBound ? ResolveBomForFixedTarget(this._targetSpec) : this._targetSpec.EmitBom;
        }
        else if (bomBound)
        {
            // 変換元の文字エンコーディングを保ち、BOM だけ変える。
            // 変換元が Unicode 系以外なら Add は非終了エラー（N5）、Remove は何もしない
            this._targetBom = this.Bom == BomOption.Add;
        }

        this._targetLineBreak = ResolveTargetLineBreak();

        if (IsBound(nameof(this.Destination)))
        {
            this._destination = ResolveDestination();
        }
    }

    /// <summary>
    /// 指定されたファイルを順に変換する
    /// </summary>
    protected override void ProcessRecord()
    {
        bool literal = this.ParameterSetName == LiteralPathParameterSet;
        string[] inputPaths = literal ? this.LiteralPath : this.Path;

        if (inputPaths == null)
        {
            return;
        }

        foreach (string inputPath in inputPaths)
        {
            List<string> matches = ResolveOne(inputPath, literal).ToList();

            if (matches.Count == 0)
            {
                // N1: ワイルドカードが 0 件に解決された場合も、変換元が無いものとして報告する
                WriteError(CreateError(
                    new FileNotFoundException(ValidationMessages.FileNotFound(inputPath), inputPath),
                    "FileNotFound",
                    ErrorCategory.ObjectNotFound,
                    inputPath));
                continue;
            }

            foreach (string resolved in matches)
            {
                if (Directory.Exists(resolved))
                {
                    // Get-ChildItem -Recurse の出力にはディレクトリが混ざる。エラーにせず飛ばす
                    WriteVerbose(ValidationMessages.SkippedDirectory(resolved));
                    continue;
                }

                if (!File.Exists(resolved))
                {
                    // N1
                    WriteError(CreateError(
                        new FileNotFoundException(ValidationMessages.FileNotFound(resolved), resolved),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolved));
                    continue;
                }

                ConvertFile(System.IO.Path.GetFullPath(resolved));
            }
        }
    }

    /// <summary>
    /// -Encoding の指定から変換先の文字エンコーディングと BOM を決める（仕様書 12.3 の表）
    /// </summary>
    private void ResolveTargetFromEncoding(ConvertEncodingArgument argument, bool bomBound)
    {
        EncodingSpec spec = argument.Spec;

        if (spec.IsAuto)
        {
            // E2: 「変換元を保つ」は省略で表す
            ThrowArgumentError(
                ValidationMessages.AutoNotAllowedForConvertContent(), "AutoNotAllowedForConvertContent", argument.Original);
        }

        int codePage = spec.Encoding!.CodePage;
        bool unicode = EncodingVocabulary.IsUnicodeCodePage(codePage);
        string display = DescribeEncoding(argument.Original, spec);

        if (argument.Original is System.Text.Encoding)
        {
            // GetPreamble() に従う。-Bom と食い違えば E8
            if (bomBound && (this.Bom == BomOption.Add) != (spec.EmitBom == true))
            {
                ThrowArgumentError(
                    ValidationMessages.BomConflictsWithEncoding(display, this.Bom.ToString()), "BomConflict", argument.Original);
            }

            this._targetSpec = spec;
            this._targetBom = spec.EmitBom == true;
            return;
        }

        if (codePage == 65000 || (!bomBound && unicode && argument.Original is not EncodingInformation))
        {
            // E4 / E5: -Bom が無ければ 1.1.0 の書き込みの規則（裸の utf8・WebName・数値は拒否、utf7 は常に拒否）
            EnsureWritable(spec, argument.Original);
        }

        if (argument.Original is EncodingInformation)
        {
            // 参照元の BOM を継承し、-Bom で上書きできる
            this._targetSpec = spec;
            this._targetBom = bomBound ? ResolveBomForFixedTarget(spec) : spec.EmitBom;
            return;
        }

        if (EncodingVocabulary.IsBomSuffixedName(argument.Original))
        {
            // 接尾辞付きの語彙は BOM を明示している。-Bom と食い違えば E6
            if (bomBound && (this.Bom == BomOption.Add) != (spec.EmitBom == true))
            {
                ThrowArgumentError(
                    ValidationMessages.BomConflictsWithEncoding(display, this.Bom.ToString()), "BomConflict", argument.Original);
            }

            this._targetSpec = spec;
            this._targetBom = spec.EmitBom;
            return;
        }

        // 裸名・WebName・数値。Unicode 系は -Bom があれば系統名として扱い、BOM は -Bom で決める
        this._targetSpec = spec;
        this._targetBom = bomBound ? ResolveBomForFixedTarget(spec) : spec.EmitBom;
    }

    /// <summary>
    /// 変換先の文字エンコーディングが決まっている場合に、-Bom から BOM を決める。
    /// Unicode 系以外に Add は E7、Remove は何もしない（BOM 無し）。
    /// </summary>
    private bool ResolveBomForFixedTarget(EncodingSpec spec)
    {
        bool unicode = EncodingVocabulary.IsUnicodeCodePage(spec.Encoding!.CodePage);

        if (this.Bom == BomOption.Add && !unicode)
        {
            ThrowArgumentError(
                ValidationMessages.BomNotSupportedByEncoding(spec.Encoding.WebName), "BomNotSupportedByEncoding", null);
        }

        return this.Bom == BomOption.Add;
    }

    /// <summary>
    /// 書き込み用途として許されるかを検査する。許されなければ終了エラー。
    /// </summary>
    private void EnsureWritable(EncodingSpec spec, object? original)
    {
        try
        {
            EncodingVocabulary.EnsureWritable(spec, original);
        }
        catch (ArgumentTransformationMetadataException exception)
        {
            ThrowArgumentError(exception.Message, "EncodingNotAllowedForWrite", original);
        }
    }

    /// <summary>
    /// 変換先の改行を決める。-LineBreak の明示 → 参照情報の改行 → 改行を保つ（null）の順。
    /// </summary>
    private string? ResolveTargetLineBreak()
    {
        if (this.LineBreak != LineBreakOption.Auto)
        {
            return LineBreakResolver.FromOption(this.LineBreak);
        }

        // -EncodingFrom と -Encoding <EncodingInformation> は改行も継承する（混在改行は LineBreakResolver の規則）
        LineBreakType? reference = this._targetSpec?.LineBreak;

        return reference.HasValue ? LineBreakResolver.FromDetected(reference.Value) : null;
    }

    /// <summary>
    /// -Destination を解決する。フォルダーとして存在しなければ終了エラー（E11）。フォルダーは作らない。
    /// </summary>
    private string ResolveDestination()
    {
        string destination = System.IO.Path.GetFullPath(GetUnresolvedProviderPathFromPSPath(this.Destination));

        if (!Directory.Exists(destination))
        {
            ThrowTerminatingError(CreateError(
                new DirectoryNotFoundException(ValidationMessages.DestinationFolderNotFound(destination)),
                "DestinationNotFound",
                ErrorCategory.ObjectNotFound,
                destination));
        }

        return destination;
    }

    /// <summary>
    /// 1 ファイルを変換する。失敗はそのファイルだけの非終了エラーとし、元のファイルは無傷で残す。
    /// </summary>
    private void ConvertFile(string file)
    {
        if (ActiveReadRegistry.IsBeingRead(file))
        {
            // N10: 他のコマンド（Get-ProbedContent の遅延読み込みなど）が読んでいる最中
            WriteError(CreateError(
                new PSInvalidOperationException(ValidationMessages.SamePathRoundTrip(file)),
                "SamePathRoundTrip",
                ErrorCategory.ResourceExists,
                file));
            return;
        }

        byte[] original;

        try
        {
            original = File.ReadAllBytes(file);
        }
        catch (IOException exception)
        {
            WriteError(CreateError(exception, "ReadFailed", ErrorCategory.ReadError, file));
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            WriteError(CreateError(exception, "ReadAccessDenied", ErrorCategory.PermissionDenied, file));
            return;
        }

        ConversionResult? result = Transcode(file, original);

        if (result == null)
        {
            return;
        }

        string target = this._destination == null
            ? file
            : System.IO.Path.Combine(this._destination, System.IO.Path.GetFileName(file));

        if (!CanWrite(file, target, result))
        {
            return;
        }

        if (!ShouldProcess(target, OperationName))
        {
            return;
        }

        // その場での変換は、変換結果が元と同じなら書き直さない（更新日時を変えない）。
        // -Destination は同一でも書く
        if ((this._destination != null || result.Changed) && !WriteAtomically(target, result.Bytes))
        {
            return;
        }

        if (this._destination != null)
        {
            this._writtenDestinations.Add(target);
        }

        if (this.PassThru.IsPresent)
        {
            WriteObject(CreatePassThruObject(file, target, result));
        }
    }

    /// <summary>
    /// 復号 → 改行の変換 → 符号化（BOM 付加）→ 元のバイト列との比較を行う。
    /// 変換できない場合は非終了エラーを報告して null を返す。
    /// </summary>
    private ConversionResult? Transcode(string file, byte[] original)
    {
        if (!TryResolveSource(file, original, out int sourceCodePage))
        {
            return null;
        }

        int bomLength = GetBomLength(original, sourceCodePage);
        bool sourceBom = bomLength > 0;

        if (!TryDecode(file, original, bomLength, sourceCodePage, out string text))
        {
            return null;
        }

        int targetCodePage = this._targetSpec?.Encoding!.CodePage ?? sourceCodePage;
        bool targetUnicode = EncodingVocabulary.IsUnicodeCodePage(targetCodePage);
        bool targetBom;

        if (this._targetBom.HasValue)
        {
            if (this._targetSpec == null && this._targetBom.Value && !targetUnicode && original.Length > 0)
            {
                // N5: -Encoding を省略し、変換元が Unicode 系以外なのに -Bom Add
                WriteError(CreateError(
                    new PSInvalidOperationException(ValidationMessages.BomNotSupportedByEncoding(
                        EncodingVocabulary.GetUnifiedName(sourceCodePage, false))),
                    "BomNotSupportedByEncoding",
                    ErrorCategory.InvalidArgument,
                    file));
                return null;
            }

            targetBom = this._targetBom.Value && targetUnicode;
        }
        else
        {
            targetBom = sourceBom && targetUnicode;
        }

        string converted = this._targetLineBreak == null ? text : LineBreakResolver.Normalize(text, this._targetLineBreak);

        if (!TryEncode(file, converted, targetCodePage, targetBom, out byte[] bytes))
        {
            return null;
        }

        return new ConversionResult(
            bytes,
            !BytesAreEqual(original, bytes),
            EncodingVocabulary.GetUnifiedName(sourceCodePage, sourceBom),
            EncodingVocabulary.GetUnifiedName(targetCodePage, targetBom),
            LineBreakResolver.Classify(text),
            LineBreakResolver.Classify(converted));
    }

    /// <summary>
    /// 変換元の文字エンコーディングを決める。-SourceEncoding の明示が無ければ検出する（N2）。
    /// </summary>
    private bool TryResolveSource(string file, byte[] original, out int codePage)
    {
        EncodingSpec source = EncodingSpec.FromBoundParameter(this.SourceEncoding);

        if (!source.IsAuto)
        {
            codePage = source.Encoding!.CodePage;
            return true;
        }

        if (original.Length == 0)
        {
            // 判定材料が無い。結果は 0 バイトのままなので、何を選んでも書き出す内容は変わらない。
            // ホストによって値が変わらないよう、ランタイム既定ではなく変換先（無ければ UTF-8）とする
            codePage = this._targetSpec?.Encoding!.CodePage ?? CodePageUtf8;
            return true;
        }

        EncodingInformation information = EncodingProbe.Detect(original, this.DetectorOptions);

        if (information.CodePage < 0)
        {
            WriteError(CreateError(
                new EncodingDetectionException(ValidationMessages.DetectionFailed(file), file),
                EncodingDetectionException.DetectionFailedId,
                ErrorCategory.InvalidData,
                file));
            codePage = -1;
            return false;
        }

        if (!EncodingVocabulary.TryBuildEncoding(information.CodePage, emitBom: false, out _))
        {
            WriteError(CreateError(
                new EncodingDetectionException(
                    ValidationMessages.DetectedCodePageNotAvailable(information.CodePage, information.EncodingWebName),
                    file,
                    EncodingDetectionException.CodePageNotAvailableId),
                EncodingDetectionException.CodePageNotAvailableId,
                ErrorCategory.InvalidData,
                file));
            codePage = -1;
            return false;
        }

        codePage = information.CodePage;
        return true;
    }

    /// <summary>
    /// ファイル先頭が、変換元の文字エンコーディングの BOM と一致すればその長さを返す（原則 A）。
    /// </summary>
    private static int GetBomLength(byte[] content, int codePage)
    {
        if (!EncodingVocabulary.IsUnicodeCodePage(codePage))
        {
            return 0;
        }

        byte[] preamble = EncodingVocabulary.BuildEncoding(codePage, emitBom: true).GetPreamble();

        if (content.Length < preamble.Length)
        {
            return 0;
        }

        for (int i = 0; i < preamble.Length; i++)
        {
            if (content[i] != preamble[i])
            {
                return 0;
            }
        }

        return preamble.Length;
    }

    /// <summary>
    /// 例外フォールバックで復号する。不正なバイト列があれば、その位置を示して非終了エラー（N3）。
    /// </summary>
    private bool TryDecode(string file, byte[] content, int bomLength, int codePage, out string text)
    {
        System.Text.Encoding strict = EncodingVocabulary.BuildStrictEncoding(codePage);

        try
        {
            text = strict.GetString(content, bomLength, content.Length - bomLength);
            return true;
        }
        catch (DecoderFallbackException)
        {
            // DecoderFallbackException.Index は .NET Framework と .NET Core で基準が異なり、
            // PowerShell 5.1 と 7.x で違う位置を示してしまう。位置は自分で求め直す
            int offset = FindInvalidBytes(strict, content, bomLength, out byte[] invalidBytes);

            WriteError(CreateError(
                new InvalidDataException(ValidationMessages.InvalidSourceBytes(
                    file,
                    EncodingVocabulary.GetUnifiedName(codePage, bomLength > 0),
                    offset,
                    FormatBytes(invalidBytes))),
                "InvalidSourceBytes",
                ErrorCategory.InvalidData,
                file));

            text = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// 例外フォールバックで符号化し、必要なら BOM を付ける。
    /// 表現できない文字があれば、その文字と行・桁を示して非終了エラー（N4）。
    /// </summary>
    /// <remarks>
    /// BOM は 1 文字でも書くときにだけ書く（Out-ProbedFile と同じ規則）。空のファイルは空のまま。
    /// </remarks>
    private bool TryEncode(string file, string text, int codePage, bool emitBom, out byte[] bytes)
    {
        System.Text.Encoding strict = EncodingVocabulary.BuildStrictEncoding(codePage);
        byte[] body;

        try
        {
            body = strict.GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            ReportUnrepresentable(file, text, codePage, emitBom, exception);
            bytes = Array.Empty<byte>();
            return false;
        }

        if (!emitBom || text.Length == 0)
        {
            bytes = body;
            return true;
        }

        byte[] preamble = EncodingVocabulary.BuildEncoding(codePage, emitBom: true).GetPreamble();
        bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return true;
    }

    /// <summary>
    /// 表現できなかった最初の文字を、U+XXXX・文字そのもの・行・桁で報告する
    /// </summary>
    private void ReportUnrepresentable(
        string file, string text, int codePage, bool emitBom, EncoderFallbackException exception)
    {
        int codePoint;
        string character;

        if (exception.CharUnknownHigh != '\0' && exception.CharUnknownLow != '\0')
        {
            codePoint = char.ConvertToUtf32(exception.CharUnknownHigh, exception.CharUnknownLow);
            character = new string(new[] { exception.CharUnknownHigh, exception.CharUnknownLow });
        }
        else
        {
            codePoint = exception.CharUnknown;
            character = exception.CharUnknown.ToString();
        }

        // EncoderFallbackException.Index の基準も実行環境によって異なりうるため使わない。
        // 最初に表現できなかった文字は、その文字の最初の出現位置にある
        // （それより前に同じ文字があれば、そこで先に失敗しているはずである）
        int index = text.IndexOf(character, StringComparison.Ordinal);

        GetLineAndColumn(text, Math.Max(0, index), out int line, out int column);

        WriteError(CreateError(
            new InvalidDataException(ValidationMessages.UnrepresentableCharacter(
                file,
                EncodingVocabulary.GetUnifiedName(codePage, emitBom),
                "U+" + codePoint.ToString("X4", CultureInfo.InvariantCulture),
                character,
                line,
                column)),
            "UnrepresentableCharacter",
            ErrorCategory.InvalidData,
            file));
    }

    /// <summary>
    /// 最初の不正なバイト列の位置（ファイル先頭からのバイトオフセット）と、そのバイト列を求める。
    /// </summary>
    /// <remarks>
    /// 復号器に 1 バイトずつ与え、例外になったバイトの位置から、例外が報告した不正なバイト列の先頭を逆にたどる。
    /// 不正と分かるのは、不正なバイト列そのものの最後のバイトか、その直後のバイトが届いたときである。
    /// 復号に失敗したときだけ呼ばれるため、1 バイトずつの処理の遅さは問題にならない。
    /// </remarks>
    internal static int FindInvalidBytes(System.Text.Encoding strict, byte[] content, int start, out byte[] invalidBytes)
    {
        Decoder decoder = strict.GetDecoder();
        var chars = new char[strict.GetMaxCharCount(1) + 2];
        int detectedAt = content.Length;

        invalidBytes = Array.Empty<byte>();

        try
        {
            for (int i = start; i < content.Length; i++)
            {
                detectedAt = i;
                decoder.GetChars(content, i, 1, chars, 0, flush: false);
            }

            // 末尾で途切れた多バイト文字は、最後に flush したときに分かる
            detectedAt = content.Length;
            decoder.GetChars(content, content.Length, 0, chars, 0, flush: true);

            return start;
        }
        catch (DecoderFallbackException exception)
        {
            invalidBytes = exception.BytesUnknown ?? Array.Empty<byte>();
        }

        int length = invalidBytes.Length;

        for (int candidate = Math.Min(detectedAt, content.Length - length); candidate >= start; candidate--)
        {
            if (candidate + length <= detectedAt + 1 && StartsWith(content, candidate, invalidBytes))
            {
                return candidate;
            }
        }

        return detectedAt;
    }

    private static bool StartsWith(byte[] content, int offset, byte[] value)
    {
        if (offset < 0 || offset + value.Length > content.Length)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (content[offset + i] != value[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 文字列中の位置を、1 始まりの行・桁に換算する（CR-LF / LF / CR をそれぞれ 1 個の改行として数える）
    /// </summary>
    internal static void GetLineAndColumn(string text, int index, out int line, out int column)
    {
        line = 1;
        int lineStart = 0;
        int limit = Math.Min(index, text.Length);

        for (int i = 0; i < limit; i++)
        {
            char c = text[i];

            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            if (c == '\r' || c == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        column = index - lineStart + 1;
    }

    /// <summary>
    /// 書き込み先に書いてよいかを検査する（N6〜N9）。書けない場合は非終了エラーを報告して false。
    /// </summary>
    private bool CanWrite(string file, string target, ConversionResult result)
    {
        if (this._destination != null)
        {
            if (PathComparison.Comparer.Equals(file, target))
            {
                // N9
                ReportWriteRejection(new IOException(ValidationMessages.DestinationIsSource(file)),
                    "DestinationIsSource", ErrorCategory.InvalidArgument, file);
                return false;
            }

            if (this._writtenDestinations.Contains(target))
            {
                // N8: 同じ実行で書いたファイルは -Force があっても消さない
                ReportWriteRejection(new IOException(ValidationMessages.DestinationNameConflict(target, file)),
                    "DestinationNameConflict", ErrorCategory.ResourceExists, file);
                return false;
            }

            if (File.Exists(target) && !this.Force.IsPresent)
            {
                // N7
                ReportWriteRejection(new IOException(ValidationMessages.DestinationFileExists(target)),
                    "DestinationExists", ErrorCategory.ResourceExists, target);
                return false;
            }
        }
        else if (!result.Changed)
        {
            // 書き直さないため、読み取り専用でも問題にならない
            return true;
        }

        if (!this.Force.IsPresent && ReadOnlyAttributeScope.IsReadOnly(target))
        {
            // N6
            ReportWriteRejection(new UnauthorizedAccessException(ValidationMessages.FileIsReadOnly(target)),
                "WriteAccessDenied", ErrorCategory.PermissionDenied, target);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 同じフォルダーの一時ファイルに書いてから置き換える。失敗したら一時ファイルを消し、非終了エラーを報告する。
    /// </summary>
    private bool WriteAtomically(string target, byte[] content)
    {
        string directory = System.IO.Path.GetDirectoryName(target)!;
        string temporary = System.IO.Path.Combine(
            directory, "." + System.IO.Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        ReadOnlyAttributeScope? readOnlyScope = null;

        try
        {
            File.WriteAllBytes(temporary, content);

            if (File.Exists(target))
            {
                if (this.Force.IsPresent)
                {
                    readOnlyScope = ReadOnlyAttributeScope.ClearIfReadOnly(target);
                }

                File.Replace(temporary, target, null);
            }
            else
            {
                File.Move(temporary, target);
            }

            return true;
        }
        catch (IOException exception)
        {
            WriteError(CreateError(exception, "WriteFailed", ErrorCategory.WriteError, target));
        }
        catch (UnauthorizedAccessException exception)
        {
            WriteError(CreateError(exception, "WriteAccessDenied", ErrorCategory.PermissionDenied, target));
        }
        finally
        {
            readOnlyScope?.Dispose();
            DeleteQuietly(temporary);
        }

        return false;
    }

    /// <summary>
    /// 一時ファイルを消す。置き換えが済んでいれば既に無い。
    /// </summary>
    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 後始末であり、消せなくても変換の結果は変わらない
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    /// <summary>
    /// -PassThru の出力を組み立てる
    /// </summary>
    private static PSObject CreatePassThruObject(string file, string target, ConversionResult result)
    {
        var output = new PSObject();

        output.Properties.Add(new PSNoteProperty("Path", file));
        output.Properties.Add(new PSNoteProperty("Destination", target));
        output.Properties.Add(new PSNoteProperty("SourceEncoding", result.SourceEncoding));
        output.Properties.Add(new PSNoteProperty("Encoding", result.Encoding));
        output.Properties.Add(new PSNoteProperty("SourceLineBreak", result.SourceLineBreak.ToString()));
        output.Properties.Add(new PSNoteProperty("LineBreak", result.LineBreak.ToString()));
        output.Properties.Add(new PSNoteProperty("Changed", result.Changed));

        output.TypeNames.Insert(0, ResultTypeName);

        return output;
    }

    private void ReportWriteRejection(Exception exception, string errorId, ErrorCategory category, string target)
        => WriteError(CreateError(exception, errorId, category, target));

    private void ThrowArgumentError(string message, string errorId, object? target)
        => ThrowTerminatingError(CreateError(new PSArgumentException(message), errorId, ErrorCategory.InvalidArgument, target));

    private bool IsBound(string parameterName) => this.MyInvocation.BoundParameters.ContainsKey(parameterName);

    /// <summary>
    /// エラーメッセージに埋め込む、-Encoding の表示用の名前
    /// </summary>
    private static string DescribeEncoding(object? original, EncodingSpec spec)
    {
        switch (original)
        {
            case System.Text.Encoding encoding:
                return encoding.WebName;
            case EncodingInformation:
                return EncodingVocabulary.GetUnifiedName(spec.Encoding!.CodePage, spec.EmitBom == true);
            case null:
                return string.Empty;
            default:
                return original.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// バイト列を 16 進で表す（エラーメッセージ用）
    /// </summary>
    private static string FormatBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 3);

        foreach (byte value in bytes)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool BytesAreEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 1 ファイルの変換結果
    /// </summary>
    private sealed class ConversionResult
    {
        public ConversionResult(
            byte[] bytes,
            bool changed,
            string sourceEncoding,
            string encoding,
            LineBreakType sourceLineBreak,
            LineBreakType lineBreak)
        {
            this.Bytes = bytes;
            this.Changed = changed;
            this.SourceEncoding = sourceEncoding;
            this.Encoding = encoding;
            this.SourceLineBreak = sourceLineBreak;
            this.LineBreak = lineBreak;
        }

        /// <summary>書き込む内容（BOM を含む）</summary>
        public byte[] Bytes { get; }

        /// <summary>変換結果が変換元と異なるか</summary>
        public bool Changed { get; }

        /// <summary>変換元の統一語彙名</summary>
        public string SourceEncoding { get; }

        /// <summary>変換先の統一語彙名</summary>
        public string Encoding { get; }

        /// <summary>変換元の改行</summary>
        public LineBreakType SourceLineBreak { get; }

        /// <summary>変換先の改行</summary>
        public LineBreakType LineBreak { get; }
    }
}
