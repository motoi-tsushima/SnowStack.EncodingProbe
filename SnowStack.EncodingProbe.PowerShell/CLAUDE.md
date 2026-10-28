# CLAUDE.md（PowerShell モジュール）

ルートの `CLAUDE.md` を補う、モジュールのコマンドと MAML ヘルプの決定事項（ルートから移した。内容は変えていない）。
テスト（`tests/EncodingProbe.PowerShell.Tests`）・PSCompat を触るときも、この内容に従うこと。

## 1.1.0 で追加した読み書きコマンド

`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `ConvertTo-DotNetEncoding` は
**統一語彙**（PS 5.1 と 7.x で同じ名前が同じ結果になるエンコーディング名の体系）の上に作られている。
仕様は `docs/EncodingProbe-1.1.0-仕様書.md`。

- `Internal/EncodingVocabulary` が語彙の解決を一手に担う。解決順序は意味を持つ
- `Internal/EncodingSpecTransformationAttribute`（`ArgumentTransformationAttribute` 派生）が
  **パラメータ束縛の段階**で `EncodingSpec` に変換・検証する。ファイルを開く前に失敗させることで、
  書きかけの破損ファイルを残さない。`EncodingSpec` は internal のまま公開しない
- 書き込み系は `Cmdlets/ProbedContentWriterCommandBase` を共有する。
  `Set-` と `Add-` の差は「ファイルの開き方」と「書き込む内容の検査」の 2 点だけ
- BOM を書き出すかどうかは `EncodingSpec.EmitBom` だけで決め、`ProbedFileWriter` が自分で書き出す。
  `Encoding.GetPreamble()` 任せにしない（解決経路によって出力が変わってしまうため）
- **書き込みは例外フォールバックで符号化し、表現できない文字は `UnrepresentableCharacter` にしてファイルを元に戻す（1.3.0 第五次）。**
  `ProbedFileWriter` はコードページから `BuildStrictEncoding` で組み立て直す（利用者が渡した Encoding のフォールバックは使わない）。
  上書きは `Internal/SafeFileOverwrite`（`Convert-ProbedContent` も共用）。ふつうは同じフォルダーの一時ファイル `.~probed<8 桁>.probedtmp`（元の名前を含めない固定長。第六次 B。BUG-0027）に書いて `File.Replace` で置き換える。一時ファイルのパスが長さの上限を超えたら、写してから直接書く方法に切り替える。
  シンボリックリンク・ジャンクションは実体（`GetFinalPathNameByHandle`）のフォルダーで置き換え、ハードリンクと一時ファイルを作れないフォルダーは、一時フォルダーに写してから直接書く（1.3.0 第五次の後半）。
  **書く内容と元のファイルがどちらも 1 MiB（`SafeFileOverwrite.DirectWriteThreshold`）以内なら、メモリに写してから直接書く（`MemoryThenDirect`。第五次の仕上げ、実装記録 10.16 節）。**
  書く内容は `SafeFileOverwrite.Write` でためる（`.Stream` に直接書かないこと）。既存のファイルは完了の時点まで開かず、まだ無いファイルは開いた時点で作る（完了まで `TemporaryFileRegistry` に載せる）。
  途中で 1 MiB を超えたら今までの書き方に切り替える。ためる量はプロセス全体で 64 MiB まで（`MemoryBudget`）。`File.Replace` の費用で小さなファイルが約 10 倍遅くなっていたため（10.15 節）。
  追記は開いたストリームの長さを覚えて切り詰めて戻す（リンクの `FileInfo.Length` を使わないこと。リンク自身の長さになる）。
  一時ファイルは `TemporaryFileRegistry` でワイルドカードの解決から除く。`-ErrorAction Stop` では `WriteError` が戻らないので、**元に戻してから** `WriteError` を呼ぶこと。
  表現できるかは状態を持たない `GetByteCount` で書く前に確かめる（Encoder の状態を進めない）。
  **規則は「エラーで止まったら、そのファイルには、その呼び出しで書いた分を残さない」**（`EncodingChangeOnAppend` も元に戻す。第五次の後半）。パイプラインの停止（Ctrl+C）は書いた内容を残す
- **同じファイルかどうかは、パスの文字列ではなく実体で比べる（`Internal/FileIdentity`。1.3.0 第五次の後半）。** Windows は `GetFileInformationByHandle` のボリュームのシリアル番号とファイル ID、
  Linux / macOS はリンクを解決したパス。まだ無いファイルは親フォルダーの実体＋名前で比べる。`ActiveReadRegistry`（`SamePathRoundTrip`）・書き込み系の書き込み先の表・`Convert-ProbedContent` の N8 / N9 が使う。
  `PathComparison`（文字列の比較）を、同じファイルの判定に使い直さないこと。追記は「切り詰めずに開く → 開いたハンドルで比べる → 書く」
- **パスは `Internal/FileSystemPath.TryGetFullPath` で絶対パスにする（`Path.GetFullPath` を直接呼ばない）。** `a.txt::$DATA` は `a.txt` とし、代替データストリームは `AlternateDataStreamNotSupported`。
  .NET Framework の `GetFullPath` はストリームの名前で `NotSupportedException` を投げる
- **`-LiteralPath` の別名は `PSPath`・`LP`・`FullName`、`-Path` に `FullName` を付けないこと**（BUG-0003。FileInfo の `FullName` がワイルドカードとして展開される）。
  `Resolve-Encoding -Path` は展開しないので `FullName` のまま。`Set-` / `Add-ProbedContent -Path` のワイルドカードが 0 件なら `ItemNotFound`（読み込みはエラーにしない）
- **読み込みは `Internal/ProbedFileReader` だけを通し、`StreamReader` には preamble を持たない `ReaderEncoding` を渡す（1.3.0 第六次。BUG-0021）。** BOM 付きの Encoding を渡すと、
  BOM の後ろへシークした後で `StreamReader` が 2 個目の BOM も読み飛ばす。取り除く BOM は先頭の 1 個だけ。net48 では同じ部品の `EndOfStreamSignal` / `FlushAtEndDecoder` が
  ストリームの終わりで Decoder を flush させる（BUG-0042。.NET Framework の `StreamReader` は flush せず、途中で切れた末尾を捨てていた）
- **ISO-2022 で書く追記は、追記先が ASCII 以外の状態のまま終わっていれば、最初に書く内容の前に戻すシーケンスを書く**（`Internal/Iso2022TailState`、`ProbedFileWriter` の `_pendingPrefix`。BUG-0018）。
  `-AllowEncodingChange` で別の文字エンコーディングを書くときは書かない（利用者の決定）
- **CSV の列名どうしの比較は `Internal/CsvNameComparer.Instance` を使う**（net10.0 は `OrdinalIgnoreCase`、net48 は .NET 10 から作った表 `CsvNameCaseTable`。BUG-0020）。
  列名でプロパティを引くときは `CsvPropertyViews.FindProperty`。表は `tools/New-CsvNameCaseTable.ps1` で生成する（手で直さない。.NET の版で結果が変わったら作り直し、`CsvNameComparerTests` を通す）
- **`ansi` / `oem` と `Locale` のコードページは、コアの `Internal/SystemCodePages` だけから求める**（Windows は `GetACP` / `GetOEMCP`、それ以外はカルチャーの値。BUG-0032）。`CurrentCulture.TextInfo` を直接使わないこと
- **判定・変換のためにファイル全体を読むときは `Internal/SharedFileReader.ReadAll`（`FileShare.ReadWrite`、長さちょうど）を使う（第六次 B。BUG-0026）。** `File.ReadAllBytes` とクラスライブラリの `Detect(path)` は `FileShare.Read` なので使わない（ほかのプログラムが書き込み中のファイルを読めない）。読み書きの失敗は `Internal/FileErrors` で 5 言語のメッセージに包み直す（内部例外は持たない）。全体を読む判定のメモリ不足は `EncodingDetectionException.GuardMemory`（BUG-0030）
- **逆向きの往復（書き込み中のファイルを、同じパイプラインの読み込み側が後から開く）は `Internal/ActiveWriteRegistry` で検出する（第六次 B。BUG-0028。案 A）。** `ProbedFileWriter` が開いた時点で登録し、読み込み側（`ProbedContentCommandBase.ReportIfBeingWritten`。Get・Import・Convert）が当たれば、書き込み側を元に戻させてから両方が `SamePathRoundTrip` を出す。読み込み側で見つからないファイルも LocationKey で引く
- **書き込みに成功したら Archive 属性を付け、元に戻したら書き込みの前の属性に戻す（`Internal/FileAttributeKeeper`。第六次 B。BUG-0025）**
- **前後の空白は、`-Encoding`・`-SourceEncoding`・`-Strategy`・`-Culture`（`ResolveEncodingOptions.TrimCulture`。第六次 C。BUG-0038）で除く。** パス・列名・`-Delimiter` は除かない。クラスライブラリのカルチャー名の検証は変えない
- **列名の重複のメッセージは、移植したコードを変えずに `Internal/CsvDuplicateColumnName` で差し替える（パスと、ファイルのヘッダーか `-Header` か。第六次 C。BUG-0045）。** 例外の型・ID・対象は変えない。`EncodingChangeOnAppend` の名前は `AppendConsistency.DescribeForMessage`（統一語彙名）。追記の整合性検査の相手は BOM の有無を持つ `EncodingSpec`
- **継承した us-ascii は BOM 無しの UTF-8 とみなす（1.3.0 第五次）。** `EncodingVocabulary.TreatInheritedAsciiAsUtf8` を `EncodingInheritance.FromFile`（Auto・`-EncodingFrom`・追記の整合性検査の相手）と、
  書き込み用途で `EncodingInformation` を解決したときに通す。`-Encoding ascii` の明示と読み取り（`ConvertTo-DotNetEncoding` を含む）は置き換えない
- 文字エンコーディングの判定は**ファイル全体**を対象とする。先頭の一定量に制限すると、
  英数字が続いたあとにマルチバイト文字が現れるファイルを誤判定する。
  **例外は 1.3.0 の `-DetectionLineCount`**（`Resolve-Encoding` / `Import-ProbedCsv` / `Export-ProbedCsv`）で、
  先頭 N 行で判定する。`Import-` / `Export-ProbedCsv` は、使う前にファイル全体を厳密に検証して誤判定を止める
  （`Resolve-Encoding` は検証しない。「先頭 N 行が何に見えるか」を報告するコマンドのため）
- `Add-ProbedContent` の整合性検査は**実際に書き出されるバイト列の比較**で行う。
  名前の組み合わせ表では判定しない（仕様書 6.1）
- メッセージは `Internal/MessageCatalog` が英語・日本語・韓国語・繁体字中国語・簡体字中国語で持つ。
  サテライトアセンブリではなく単一アセンブリ内の表。`Resolve-Encoding` の既存メッセージは英語のまま
- 言語は `CurrentUICulture` の名前を `EncodingDetector.ResolveEastAsianLegacyRegion` に渡し、
  判定のカルチャー圏をそのまま言語に写して決める（`LanguageFromRegion`）。
  `TwoLetterISOLanguageName` や親カルチャーのチェーンは使わない（NLS と ICU で名前・親が異なる）。
  **香港（`ChineseHongKong`）は台湾と同じ繁体字の辞書を使う（B 案）。** 香港用の辞書は持たない。
  香港用の文面を持つ場合は、辞書と `Catalogs` の行を足して `LanguageFromRegion` の香港の行を変える。
  `MessageCatalogTests.HongKongMessages_AreIntentionallySameAsTaiwan_PlanB` が落ちるので、そこで B 案を見直す
- `-Culture` / `-Strategy` は共通の基底クラス `Cmdlets/ProbedContentCommandBase` にある。
  `Resolve-Encoding` と**同じ名前・同じ値**であり、判定方式の語彙表は
  `Cmdlets/ResolveEncodingOptions.TryParseStrategy` に集約している（表を二重に持たない）。
  基底クラスが `BeginProcessing` を持つため、派生側では**必ず `base.BeginProcessing()` を先に呼ぶ**
  （書き込み系は `-EncodingFrom` の判定を `BeginProcessing` で行うため）。
  判定オプションが届く先は `Internal/ProbedFileReader.Open` と
  `Internal/EncodingInheritance.FromFile` の 2 か所だけ。
  `ConvertTo-DotNetEncoding` は判定処理を呼ばないため対象外
- 実行環境依存の例外を利用者に見せない。不正なカルチャー名の検証では
  `CultureNotFoundException` を内部例外として持たせていない（1.3.0 第五次の後半からは `CultureInfo` を使わずに検証する）。
  この例外のメッセージは .NET Framework と .NET Core で文言が異なり、
  PS 5.1 と 7.x で見えるメッセージが変わってしまうため（PSCompat が検出した）

## 1.2.0 で追加したコマンド（Out-ProbedFile / Convert-ProbedContent）

仕様は `docs/EncodingProbe-1.2.0-仕様書.md`。仕様と異なった点・実装で決めた点は `docs/EncodingProbe-1.2.0-実装記録.md`。「標準の挙動」はすべて `docs/EncodingProbe-1.2.0-調査-Out-File挙動の実測.md` の実測に基づく。

- `Cmdlets/OutProbedFileCommand` は `ProbedContentWriterCommandBase` を継承しない（パラメータの形が違う。`-FilePath` は単一の string）。
  書き込み部品（`ProbedFileWriter`・`LineBreakResolver`・`EncodingInheritance`・`ActiveReadRegistry`・`AppendConsistency`）を共用する
- 整形は `Microsoft.PowerShell.Utility\Out-String -Stream` のステッパブルパイプラインで行う。
  **`Begin(this)` ではなく `Begin(expectInput: true)` を使うこと。** コマンドを渡すと Out-String の出力が
  本コマンドの出力ストリームへ直接流れ（プロキシコマンドの動作）、`Process` / `End` の戻り値が空になる
- ファイルを開くのは最初の行を書く直前（同一パスの往復を検出するため）。失敗はすべて終了エラー
- `-Encoding` の省略と明示的な `Auto` は意味が違う（省略は utf8NoBOM、`-Append` の省略は追記先から継承）。`BoundParameters` で区別している
- `Cmdlets/ConvertProbedContentCommand` の `-Encoding` は `Internal/ConvertEncodingTransformationAttribute` で
  元の値を保持したまま解決する。`-Bom` と組み合わせたとき、BOM 接尾辞付きの名前と裸名・WebName で扱いが違うため
  （`EncodingSpec` だけでは `unicode` と `unicodeBOM` を区別できない）。書き込み用途の検証は `BeginProcessing` で行う
- 変換元・変換先は `EncodingVocabulary.BuildStrictEncoding`（例外フォールバック）で復号・符号化する。
  **不正なバイト列の位置は `DecoderFallbackException.Index` を使わず自前で求める**（`FindInvalidBytes`）。
  `Index` の基準が .NET Framework と .NET Core で違い、PS 5.1 と 7.x で異なる位置を報告していた（PSCompat 相当の手動確認で検出）。
  表現できない文字の位置も `EncoderFallbackException.Index` を使わず、その文字の最初の出現位置から求める
- `-Force` で外した読み取り専用属性は `Internal/ReadOnlyAttributeScope` で元に戻す。
  書き込み系の 4 コマンドすべてがこの部品を通る（1.2.0 で Set-/Add-ProbedContent の「外したまま」を修正した）
- **ファイルを変更しない検査は `ShouldProcess` より前に置く**（1.2.0 手動確認後の方針）。`-WhatIf` でも、実行すれば失敗するエラーを
  `-WhatIf` なしと同じ ID で報告するため。`Convert-ProbedContent` の N8 は、書いた名前ではなく**検査を通った時点で書く予定の名前**を記録する
- `Convert-ProbedContent -PassThru` の往復は、Unicode 系は `SourceEncoding`、それ以外は `SourceCodePage`（`euc-jp` は 51932 に解決されるため）。
  `EncodingVocabulary.GetUnifiedName` は WebName を小文字にそろえる（.NET Framework は 20932 を `EUC-JP` と返す）
- PSCompat の `.GetNewClosure()` 付きのシナリオの中では `$script:` 変数が見えない（動的モジュールのスコープになる）。
  スクリプトの最上位で `$script:x = ...` と定義した変数は、クロージャの中では `$x` と書いて参照する

## 1.3.0 で追加したコマンド（Import-ProbedCsv / Export-ProbedCsv）と -DetectionLineCount

仕様は `docs/EncodingProbe-1.3.0-仕様書.md`、実装で決めた点は `docs/EncodingProbe-1.3.0-実装記録.md`。「標準の挙動」はすべて
`docs/EncodingProbe-1.3.0-調査-CSVコマンド挙動の実測.md`（測定は `tools/Measure-CsvBehavior.ps1`、生の結果は `tools/CsvBehaviorResults/`）に基づく。

- **CSV の解析・文字列化は PowerShell 7.x からの移植（`Internal/Csv/`）で行い、ホストの `Import-Csv` / `Export-Csv` / `ConvertTo-Csv` を呼ばない。**
  そのため 5.1 でも 7.x と同じ結果になる（書き出しはバイト単位）。規則は移植元のとおりとし、独自の改良を加えない。
  仕様で決めた変更（追記先の末尾の改行を補うなど）は移植したコードの外（コマンド側）で行う
- 移植元の内部 API の置き換え: `PSObject.GetPropertyCollection` / `PSMemberInfoIntegratingCollection` は `Internal/CsvPropertyViews`
  （`PSObject.Properties` を種類で選り分け、Property の種類は `psadapted` にあるものだけ残す）、`new PSObject(int)` は `new PSObject()`（5.1 に無い）。
  `AsSpan()` などの net48 に無い機能も置き換え、`System.Memory` への依存は持ち込んでいない。
  **FileInfo などホストによって形が違うオブジェクトの列は、両ホストで一致しなくてよい**（入力そのものが違うため。第一次の決定）。
  PSCompat ではホストによって形が変わらないオブジェクトを使う
- **`-Culture`（判定のカルチャー。基底クラス）と `-UseCulture`（区切り文字）は別のもの。** コード・コメント・ヘルプで取り違えないこと
- `Import-ProbedCsv` のパラメータセットは 4 つ（`DelimiterPath` / `DelimiterLiteralPath` / `CulturePath` / `CultureLiteralPath`）。
  `Export-ProbedCsv` は標準の `Export-Csv` と同じ 2 つ（`Delimiter` / `UseCulture`）で、`-Path` / `-LiteralPath` は必須にせず「ちょうど一方」を検査する。
  4 つにするとパイプライン入力と `-LiteralPath` の組み合わせで `AmbiguousParameterSet` になる（第三次で改めた）
- `Export-ProbedCsv` と `Out-ProbedFile` は、殻（パスの解決、`-NoClobber` と読み取り専用の検査、最初の行を書く直前に開く処理、追記の整合性検査、空の入力、後始末）を
  `Cmdlets/ProbedFileOutputCommandBase` で共用する。文字エンコーディングの決定は各コマンドにある。
  `Export-ProbedCsv` は、ヘッダーと末尾の改行を読むため、**1 バイト以上の追記先を常に判定する**（`Out-ProbedFile` とはここが違う）。
  BOM だけの追記先にはヘッダーを書く（BOM は重ねない）
- **`-DetectionLineCount` の判定は 1 か所に集約している。** 切り出しは `Internal/HeadLineCutter`、判定は `Internal/HeadLineDetection`
  （`Resolve-Encoding` はここまで）、読み替え（us-ascii を改行の型に応じて UTF-8 / UTF-16 / UTF-32 に）と全体の検証は `Internal/HeadLineEncodingResolver`
  （`Import-` / `Export-ProbedCsv`、`-EncodingFrom` も含む）。3 つのコマンドの判定結果（読み替え前）が一致することはこの共用で保証している
- **切り出しの型の決め方（`HeadLineCutter.DecideUnit`。1.3.0 第五次、仕様書 2.4）。** 先頭 64 KiB を単位に区切って N・A・B・Z・Abr・P を数え、
  共通の条件 A ≥ min(16, ⌈0.6N⌉)・Z = 0（UTF-16 は P ≤ 0.7N）で、UTF-32（B ≤ 0.2A）→ UTF-16 の改行の向き（Abr ≥ 4、逆 ≤ 0.25 Abr）→ UTF-16（B ≤ 0.2A）の順に決める。
  1 バイトの型で `00` があり 64 KiB 以内なら `None`（ファイル全体）。改行は決めた型の形だけで探す。値は利用者が確定したもの。根拠は `docs/EncodingProbe-1.3.0-調査-切り出しの境目.md`。
  `HeadLineCutterRuleTests` が調査の 1033 件（生成は `Helpers/CutThresholdSamples.cs`。中身を変えないこと）で、規則のモデルと一致することを確かめている。
  調査ブランチ `investigate/1.3.0-cut-threshold` は**どこにもマージしない**（文書だけをファイル単位で持ってきた。ブランチはリリース前に利用者が消す）
- 読み替えで UTF-16 / UTF-32 にしたときは `LineBreak` も読み替えた後の単位で数え直す。us-ascii を UTF-8 に読み替えたときは、全体の検証で ISO-2022 のエスケープシーケンス
  （ESC の後に `$` `(` `)`）も探して `DetectionVerificationFailed` にする（1.3.0 第五次）
- 全体の厳密な検証（`Internal/StrictDecodeVerifier`）は `DecoderFallbackException.Index` を使わない。失敗したときだけ先頭から読み直し、
  成功済みのバッファを同じ区切りで与えて `Decoder` の状態を再現し、失敗したバッファから 1 バイトずつ与えて位置を求める（ISO-2022 のような状態を持つ文字エンコーディングのため）
- 検証の失敗は `DetectionVerificationFailed`（`Import-ProbedCsv` は非終了、`Export-ProbedCsv` は `ShouldProcess` より前の終了エラー）。
  `Import-ProbedCsv` の列名の重複は標準と同じ ID の非終了エラー（24 章 U3）。ワイルドカードが 0 件の場合は `Get-ProbedContent` と同じくエラーにしない

## MAML ヘルプ

`SnowStack.EncodingProbe.PowerShell/` の下の `en-US/` `ja-JP/` `ko-KR/` `zh-TW/` `zh-CN/` `zh-HK/` `zh-MO/` に
`SnowStack.EncodingProbe.PowerShell.dll-Help.xml` を置いている（csproj で出力へコピーする）。
対応言語はメッセージ（`MessageCatalog`）と同じ 5 言語。
**`zh-HK/` と `zh-MO/` は `zh-TW/` のバイト単位の複製である（1.2.0、B 案）。**
Microsoft が zh-HK 言語パックの提供をやめ zh-TW を案内しているのに合わせた判断で、香港用の文面は作らない。
**zh-TW を直したら zh-HK / zh-MO にもコピーすること。**
`MamlHelpTests.HelpFile_HongKongAndMacau_AreIntentionallySameAsTaiwan_PlanB` がバイト列の一致を検査している。

`Get-Help` は「UI カルチャー名と同名のフォルダー → 親カルチャーのフォルダー → en-US」の順で探す（両ホストで同じ）。
フォルダー名は **Windows が報告する UI カルチャー名そのもの**にしてある。
`zh-Hant` / `zh-Hans` のような親カルチャー名は置かない（台湾版と香港版を分けられなくなる）。
`zh-MO` の親は `zh-Hant` で `zh-HK` フォルダーには届かないため、`zh-MO/` も置いている。
`zh-SG` `ko` `yue` 系などは en-US にフォールバックする。
PS 7（ICU）は `zh-Hant-TW` / `zh-Hant-HK` / `zh-Hant-MO` / `zh_HK` を正規化しないため、これらのヘルプは en-US になる
（PS 5.1 は `zh-TW` などに正規化するので繁体字）。既知の課題: `docs/EncodingProbe-課題-ヘルプの用字付きカルチャー名.md`

`publish/` へ配置する際は `core\` と `desktop\` の下に 7 フォルダーぶん、計 14 か所へコピーする
（`Copy-Item -Recurse` でビルド出力ごと配ればよい）。自動化は `docs/EncodingProbe-課題-ヘルプ配布の自動化.md`

**5 言語の内容がずれないよう、1 つだけ直さないこと。**
`tests/EncodingProbe.PowerShell.Tests/CmdletTests/MamlHelpTests.cs` が、
本文（`maml:para` / `maml:title`）を伏せた骨格が 5 言語で完全に一致することを検証している。
要素の構成・属性・出現順・コード例まで一致を要求するため、
1 言語にだけパラメーターを足すとテストが落ちる。
5 言語ぶんを生成し直す場合は en-US を雛形にして本文だけ差し替えるとよい。

他言語のヘルプを手元で確認するときは、**`Import-Module` の前に** UI カルチャーを変える。
読み込んだ後に変えても切り替わらない。

```powershell
[System.Threading.Thread]::CurrentThread.CurrentUICulture =
    [System.Globalization.CultureInfo]::GetCultureInfo('ko-KR')
Import-Module <dll>
```
