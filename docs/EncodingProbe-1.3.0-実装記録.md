# EncodingProbe.PowerShell 1.3.0 — 実装記録

- 対象: `Resolve-Encoding -DetectionLineCount`、`Import-ProbedCsv`、`Export-ProbedCsv` と共通の部品（`docs/EncodingProbe-1.3.0-仕様書.md`）
- 作成日: 2026-10-19
- 目的: 実装の過程で、仕様書・依頼・当初の想定と異なる結果になった点と、
  仕様書に書かれていなかったため実装側で決めた点を残す（1.2.0 の実装記録と同じ形式）
- 章の構成: 第一次（共通の部品と `Resolve-Encoding -DetectionLineCount`）は 1〜5 章、第二次（`Import-ProbedCsv`）は 6 章、第三次（`Export-ProbedCsv` と仕上げ）は 7 章、
  第四次（クラスライブラリの `0x00` を含む入力の判定の修正。`docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`）は 8 章、
  第五次の前半（書き込みで文字を失う件・切り出し・改行の判定・ISO-2022・CSV の追記。テスト担当のブラックボックステストの不具合）は 9 章

---

## 1. 利用者の回答（仕様書 24 章）

第一次の計画の承認時（2026-10-19）に、24 章の 4 件すべてに回答を得た。

| # | 回答 | 適用する段階 |
|---|---|---|
| U1 | 標準どおり（余分なプロパティは黙って書かない。警告しない） | 第三次 |
| U2 | 検証する（`-EncodingFrom` の参照ファイルも、`-DetectionLineCount` 指定時に全体を検証する） | 第三次 |
| U3 | 非終了エラー（列名の重複は、そのファイルだけを失敗にして次へ進む） | 第二次 |
| U4 | 使う（`us-ascii` の読み替えに、切り出しで決めた改行の型を使う） | 第一次で部品を用意（`HeadLineDetectionResult.Unit`）、第二次・第三次で適用 |

計画の時点で確認した 5 件の判断（2.1〜2.5 に記す）も、すべて提案どおりに承認された。

---

## 2. 仕様書に書かれていなかったため実装で決めた点

### 2.1 `Resolve-Encoding` の上限超過のエラー（仕様書 2.6・6.2）

仕様書 6.2 は「ID は全体の判定の上限超過と同じもの」としていた。ところが、`Resolve-Encoding` の全体の判定（`EncodingProbe.Detect(path)`）には上限の検査が無く、対応する ID も無かった。

- ID は、Probed 系の全体の判定の上限超過と同じ `EncodingDetectionFailed`（`EncodingDetectionException.DetectionFailedId`）、カテゴリは `InvalidData` とした
- メッセージは新しいキー `DetectionLinesTooLarge` として `MessageCatalog` に 5 言語で持つ。
  既存の `FileTooLargeToDetect` の文面は「判定はファイル全体を読む」「`-Encoding` で指定する」で、この場合には合わないため
  （`Resolve-Encoding` には `-Encoding` が無い）。文面は「`-DetectionLineCount` を小さくする」と「`-Encoding` を持つコマンドでは明示もできる」を案内する
- CLAUDE.md の「`Resolve-Encoding` の既存メッセージは英語のまま」は既存の文面についての規則と解釈した。
  このメッセージは `Import-ProbedCsv` / `Export-ProbedCsv` と共用する切り出しの部品が出すので、多言語にした
- `Resolve-Encoding` の全体の判定（`-DetectionLineCount` なし）には、上限の検査を足していない（既存の挙動を変えない）

### 2.2 上限超過の cmdlet テストの作り方

`ResolveEncodingCmdletTests.DetectionLineCount_ExceedingLimit_ThrowsTerminatingError` は、
論理サイズが `MaxDetectableLength + 1` で中身がすべて `0x00` の NTFS スパースファイル（`Helpers/SparseFile`）を使う。
`0x00` があるので 1 バイトの型が候補から外れ、改行が見つからないまま上限で打ち切られる。ディスクはほとんど使わない。

- 走査に約 19 秒かかる（2 GB を 1 バイトずつ調べるため）。正しさを優先し、走査の最適化は行っていない（指示書 9 節）
- 実際の利用でも、改行の無い 2 GB 以上のファイルに `-DetectionLineCount` を指定すると、エラーになるまで同じくらいかかる
- 切り出しの部品の単体テストは、上限を引数で小さくして確かめている（`HeadLineCutter.Cut(Stream, path, n, maxLength, bufferSize)`）

### 2.3 検証の位置の 5.1 での確認（第一次）

第一次では検証を呼ぶコマンドがまだ無いので、PSCompat では確かめられない。
作業用の使い捨てスクリプト（コミットしていない）で、リフレクションを使って net48 版と net10.0 版の部品を直接呼び、結果を比べた（5.3）。
正式な確認は、`Import-ProbedCsv` を追加する第二次の PSCompat で行う。

### 2.4 移植したコードが出すメッセージを第一次で追加した

依頼文の第一次にあるメッセージは `DetectionVerificationFailed` だけだった。しかし、移植した処理（`ImportCsvHelper` など）は、
列名の重複（`CsvHeaderAlreadyPresent`）、空の列名の警告（`CsvUnspecifiedHeader`）、追記の列の不一致（`CsvAppendMismatchedPropertyName`）のメッセージを使う。
移植部分をビルドしてテストするにはこれらが要るので、第一次で 5 言語分を追加した。

- 列名の重複の文面は、移植元の `ExtendedTypeSystem.MemberAlreadyPresent`（`The member "{0}" is already present.`）ではなく、
  CSV のヘッダーの列名であることが分かる独自の文面にした（例外の型は移植元と同じ `ExtendedTypeSystemException`。エラー ID はコマンド側で付ける）
- 空の列名の警告と追記の列の不一致は、移植元の英文をそのまま英語版にし、4 言語に訳した
- 移植元の内部向けのメッセージ（`EOFIsReached`、`BuildPropertyNamesMethodShouldBeCalledOnlyOncePerCmdletInstance` など。プログラムの誤りでしか出ない）は、
  移植元の英文をそのまま例外のメッセージにした。`MessageCatalog` には入れていない

### 2.5 `-UseQuotes` の列挙型

移植元の `BaseCsvWritingCommand.QuoteKind` に当たる列挙型を、公開の `SnowStack.EncodingProbe.PowerShell.CsvQuoteKind`（`Never` / `Always` / `AsNeeded`、値の順序も移植元と同じ）として `Internal/Csv/CsvQuoteKind.cs` に置いた。
既存の `BomOption` / `LineBreakOption` と同じ名前空間である。

### 2.6 3 つのコマンドの判定を 1 か所にまとめた

仕様書 3.3（同じファイル・同じ N で、3 つのコマンドの判定結果が一致する）を構造で保証するため、
「切り出して判定する」処理を `Internal/HeadLineDetection` にまとめ、`Resolve-Encoding` はこれを呼ぶ。
第二次・第三次の `Import-ProbedCsv` / `Export-ProbedCsv` もこれを呼ぶ。
結果は `EncodingInformation`（読み替えの前）と、切り出しで決めた改行の型（`LineBreakUnit`、3.2 の読み替えに使う）を持つ。

- 0 バイトのファイルは、仕様書 2.6 のとおり全体の判定（`EncodingProbe.Detect(path)`）に回す。結果は `20127 / us-ascii`、`LineBreak = None`（既存と同じ）

### 2.7 改行の型 `None`

仕様書 2.3 の 5 つの型に加えて、「改行が見つからず BOM も無い」を表す `LineBreakUnit.None` を設けた（仕様書 3.2 の「改行が無い」の行に当たる）。
BOM がある場合は、改行が無くても BOM の型を返す。

### 2.8 Convert-ProbedContent のバイト列の 16 進表記を共用にした

`InvalidSourceBytes` と `DetectionVerificationFailed` で不正なバイト列を同じ形式（`82 A0` のような大文字の 16 進を空白区切り）で示すため、
`ConvertProbedContentCommand.FormatBytes` を `Internal/ByteSequenceText.Format` に移した。挙動は変えていない（既存のテストと PSCompat が通る）。
あわせて、`ProbedFileReader.GetBomLength` を private から internal にした（検証で BOM の長さを同じ規則で求めるため）。

---

## 3. 先頭 N 行の切り出しと全体の厳密な検証

### 3.1 切り出し（`Internal/HeadLineCutter`、仕様書 2 章）

- 2 段階で処理する。まずストリームを 64 KiB ずつ走査して切る位置だけを求め（メモリは一定）、上限以内ならその長さだけを読み込む。
  走査しながらバイト列を貯めると、上限を超えたと分かる時点で上限分（約 2 GB）をメモリに持ってしまうためである
- 型を決める段階で、型が決まらないまま位置が上限に達したら打ち切ってエラーにする（指示書 4.1。改行の無い巨大なファイルを最後まで読まない）
- 改行の照合では、見つかった位置の前後 3 バイトまでを見る。バッファを読み足すときに末尾の 8 バイトを残し、バッファの境目にかかった改行も照合できるようにした
- 型が決まった後は、型の単位（1 / 2 / 4 バイト）でファイル先頭（BOM を含む）から数える。CR の直後の単位が LF なら合わせて 1 個とし、CR と LF の間では切らない
- 単体テストは、バッファの大きさを 1・2・3・4・5・7・64 KiB に変えて同じ入力を切り出し、境目の位置によらず同じ結果になることを確かめている

### 3.2 検証（`Internal/StrictDecodeVerifier`、仕様書 4 章）

- `EncodingVocabulary.BuildStrictEncoding` の `Decoder` に、64 KiB ずつ `flush: false` で与え、最後に `flush: true` で呼ぶ。メモリは一定
- BOM の長さは `ProbedFileReader.GetBomLength` で求め、BOM の後ろから復号する
- 失敗したときの戻り値は `StrictDecodeFailure`（位置とバイト列）。メッセージ（`DetectionVerificationFailed`）を組み立てるのはコマンド側（第二次・第三次）

### 3.3 不正なバイト列の位置の求め方（指示書 4.2）

`DecoderFallbackException.Index` は使わない（.NET Framework と .NET Core で基準が違う。1.2.0 で判明）。

1. 通常の検証で例外になったら、そのバッファのファイル上の先頭の位置を覚える
2. 新しい `Decoder` で、ファイルの先頭（BOM の後ろ）から読み直す。失敗したバッファの直前までは、検証と同じ区切りのバッファで与える。
   この区間は復号に成功しているので、`Decoder` の状態がそのまま再現される
3. 失敗したバッファからは 1 バイトずつ与え、例外になったバイトの位置から、例外が報告した不正なバイト列（`BytesUnknown`）の先頭を逆にたどる
   （`ConvertProbedContentCommand.FindInvalidBytes` と同じ考え方）。逆にたどるため、直前に与えた 64 バイトを覚えておく

`Decoder` の状態は複製できない。途中のバッファから読み直すと、ISO-2022 のようにエスケープシーケンスで切り替えた状態がいくらでも長く続く文字エンコーディングで、位置を誤る。
そのため、失敗したときだけ先頭から読み直す。成功する検証の速さには影響しない。
読み直しで失敗しなかった場合（検証の後にファイルが書き換えられた場合など）は、失敗したバッファの先頭の位置を報告する。

---

## 4. CSV の読み書き処理の移植（仕様書 5 章、指示書 3 節）

### 4.1 移植元と配置

- 移植元: `PowerShell/PowerShell` の `src/Microsoft.PowerShell.Commands.Utility/commands/utility/CsvCommands.cs`、
  コミット `db1d0a56cbe41adf05d0bad90be5190a6655404f`（raw.githubusercontent.com からこのコミットのファイルを取得した）
- 配置: `SnowStack.EncodingProbe.PowerShell/Internal/Csv/` の 4 ファイル。すべての冒頭に付録 A.1 の表示を付けた

| ファイル | 移植元 |
|---|---|
| `ImportCsvHelper.cs` | `ImportCsvHelper`（全体） |
| `ExportCsvHelper.cs` | `ExportCsvHelper`（全体） |
| `ImportExportCSVHelper.cs` | `ImportExportCSVHelper.SetDelimiter`、`ExportCsvCommand.ReconcilePreexistingPropertyNames` の論理 |
| `CsvQuoteKind.cs` | `BaseCsvWritingCommand.QuoteKind` |

- 内部 API の置き換え部分（`Internal/CsvPropertyViews.cs`）は、移植したコードではないので `Internal/Csv/` の外に置いた
- ライセンス本文は、移植元のコミットの `LICENSE.txt` と仕様書 付録 A.2 の本文を機械的に比較し、1 文字ずつ一致することを確かめた（`*AS IS*` を含む）。
  `THIRD-PARTY-NOTICES.txt` の PowerShell の節には、取得した `LICENSE.txt` をそのまま入れた
- コマンドレットの殻（`ImportCsvCommand` など）と `PathUtils` は移植していない（指示書 3.3）

### 4.2 内部 API の置き換え（指示書 3.4）

| 移植元が使っているもの | 置き換え | 5.1 での公開状況 |
|---|---|---|
| `PSMemberInfoIntegratingCollection<PSPropertyInfo>` + `PSObject.GetPropertyCollection(PSMemberViewTypes.Extended)`（辞書の拡張メンバー） | `PSObject.Properties` から、種類が NoteProperty / ScriptProperty / AliasProperty / CodeProperty のものを取り出す（`CsvPropertyViews.GetExtendedProperties`） | 公開 API だけを使う |
| 同上 `(Extended \| Adapted)`（辞書以外） | `PSObject.Properties` から、上の拡張の種類のものと、アダプテッドのメンバーセット（`psadapted`）にある Property を取り出す（`CsvPropertyViews.GetExtendedAndAdaptedProperties`） | 公開 API だけを使う |
| `PSMemberInfoCollection<T>.Add(T, bool preValidated)` | そのまま使う | **公開されている**（PowerShellStandard.Library 5.1.1 の net452 / netstandard2.0、実際の 5.1 の `System.Management.Automation` の両方で確認） |
| `new PSObject(int)` | `new PSObject()` | **公開されていない**（PowerShellStandard.Library 5.1.1 にも実際の 5.1 にも無い）。容量の指定は結果に影響しない |
| `CsvCommandStrings`、`ExtendedTypeSystem.MemberAlreadyPresent` | `MessageCatalog`（2.4） | — |
| `PSTraceSource`、`Diagnostics.Assert` | 通常の例外、`Debug.Assert` | — |
| `LanguagePrimitives.ConvertTo<string>` | そのまま使う | 公開されている |
| `ExtendedTypeSystemException(string)` | そのまま使う | 公開されている |

`PSObject.Properties` の考え方: `Properties` は「インスタンスのメンバー → 型テーブル → アダプター → .NET（Base）」の順に、同じ名前を先勝ちで統合した一覧である（`PSMemberViewTypes.All`）。

- 拡張の見え方（インスタンスのメンバーと型テーブル）の種類は、NoteProperty / ScriptProperty / AliasProperty / CodeProperty のいずれかで、アダプターと .NET の見え方からは現れない
- Base の見え方が加わるのは、XML・WMI・DataRowView など .NET 以外のアダプターを持つオブジェクトだけで、加わるメンバーの種類は Property である。
  そこで Property の種類のメンバーは、`psadapted` にあるものだけを残す
- 並び順は `Properties` の順をそのまま使う。移植元の統合の順と同じである
- （API の調べものとして、同じコミットの `MshObject.cs` / `MshMemberInfo.cs` を読んだ。移植はしていない）

**確認の結果:**

- 7.x（テストの PowerShell 7.6 SDK）で、次のすべてについて、移植した処理の列が実際の `ConvertTo-Csv` と一致した（`ExportCsvPortTests.Convert_HostObjects_SameColumnsAsHostConvertToCsv`）。
  - `[pscustomobject]`
  - `Select-Object` の結果
  - `Get-Process`
  - FileInfo / DirectoryInfo（types.ps1xml の拡張メンバー付き）
  - `Get-Date`
  - DataRow / DataRowView
  - XmlElement
  - CimInstance
  - `[ordered]`
  - 整数
  - ScriptProperty / AliasProperty を足した pscustomobject
- 実測報告 M4 の 4 ケース（ordered dictionary、hashtable に NoteProperty を追加、mixed objects、strings）は、5.1 でも 7.x でも 7.6.7 と同じ列になった（5.3）
- **5.1 と 7.x で差が出たのは FileInfo / DirectoryInfo だけ**（5.3）。差は移植した処理ではなく、オブジェクトそのものにある。
  5.1 の列は、5.1 で `(Get-Item notepad.exe).psobject.Properties` を列挙した結果と一致した。
  .NET Framework の FileInfo には `LinkTarget` / `UnixFileMode` が無く、7.x の types.ps1xml にだけ `ModeWithoutHardLink` / `ResolvedTarget` がある。
  指示書 3.4 の「差が出たら止めて報告する」に当たるので、第一次の報告で判断を仰いだ
- **利用者の決定（2026-10-19）:** 移植の誤りではないので、「ホストによって入力のオブジェクトそのものが違う場合は、両ホストの一致の対象外」とする。
  第三次の PSCompat（`Export-ProbedCsv` のバイト単位の一致）では、ホストによって形が変わらないオブジェクト
  （`[pscustomobject]`、`[ordered]` など。日付・数値はカルチャーを固定するか使わない）を使う

### 4.3 net48 向けの置き換え（指示書 3.5）

| 移植元 | 置き換え |
|---|---|
| `value.AsSpan().IndexOfAny(_delimiter, '\n', '"')` | `string.IndexOfAny(char[])`（引用符を付ける判定の文字の配列はコンストラクターで作る）。`System.Memory` への依存は増えていない（net48 の出力の参照アセンブリを確認した） |
| `ArgumentNullException.ThrowIfNull` | `if (x == null) throw new ArgumentNullException(...)` |
| `string.Create(CultureInfo.InvariantCulture, $"#TYPE {temp}")` | `string.Format(CultureInfo.InvariantCulture, "#TYPE {0}", temp)` |
| `values[0].StartsWith('#')` | `StartsWith("#", StringComparison.Ordinal)` |
| `values[0].StartsWith("#Fields: ")`（カルチャー依存） | `StartsWith("#Fields: ", StringComparison.Ordinal)`。比べる文字列は ASCII だけなので結果は変わらない（指示書 3.3） |
| ターゲット型の `new()` | そのまま使った（言語機能であり、net48 でもコンパイルできる） |

`HashSet<T>.TryGetValue` は .NET Framework 4.7.2 以降にあるので、そのまま使った。
移植したファイルには `#nullable disable` を付け、null 許容の注釈を加えていない。

### 4.4 移植元から形を変えた点（解析・書き出しの規則は変えていない）

- `ImportCsvHelper` の入力を `StreamReader` から `TextReader` に広げた（テストで `StringReader` を渡すため）。
  末尾の判定は `StreamReader.EndOfStream` の代わりに `Peek() == -1` で行う。`StreamReader` では両者は同じ条件である。
  M1 の全ケースを `StringReader` と `StreamReader` の両方で読み、同じ結果になることを確かめた
- `ImportCsvHelper` はコマンドレット（`PSCmdlet`）の代わりに、オブジェクトと警告の出力先（`Action<PSObject>` / `Action<string>`）を受け取る
- `SetDelimiter` は使われていなかった `PSCmdlet` 引数を受け取らない
- `ReconcilePreexistingPropertyNames` はインスタンスメソッドから静的メソッドにし、コマンドの状態を引数で受け取って結果の列を返す。
  不一致は `CsvAppendMismatchException`（列名を持つ `InvalidOperationException`）で知らせ、ErrorRecord（ID `CannotAppendCsvWithMismatchedPropertyNames`、対象は列名）はコマンド側で作る。
  移植元の「追記中でなければ例外」の内部の検査は、呼び出し側の責任とした

### 4.5 移植した処理の単体テストの結果（net10.0）

期待値は、実測の生の結果（`tools/CsvBehaviorResults/csv-measure-Core-7.6.7.txt`）を、測定スクリプトと同じ形式（`OBJ<型> {名前=[値] | ...}`、`STR: ...`）で写したもの。

- 読み込み（`CsvTests/ImportCsvPortTests`）: M1 の 15 ケースすべてで、7.6.7 の `Import-Csv` と一致した。
  - c10（列名の重複）は、1 件も出力する前に `ExtendedTypeSystemException` になる
  - c11 の警告は、複数のファイルにまたがって 1 回だけ出る
  - c04 の型名は `Foo.Bar`、`CSV:Foo.Bar` の順になる
  - 実行中のホストの `ConvertFrom-Csv` とも全ケースで一致した
- 書き出し（`CsvTests/ExportCsvPortTests`）: M4 の既定（`Always`）・`AsNeeded`・`Never`・`-QuoteFields Name,Num`・`-NoHeader` で、7.6.7 の `ConvertTo-Csv` と文字単位で一致した（カルチャー ja-JP）。
  次も 7.6.7 と一致した。
  - 辞書・NoteProperty を足した辞書・混在・文字列・`-Delimiter ';'`・`-UseCulture`
  - `#TYPE` 行（`CSV:` の除去）
  - `AsNeeded` で CR だけを含む値を囲まないこと
  - 列の照合（M5 a〜c、i）

---

## 5. テストと確認の結果（第一次）

### 5.1 自動テスト（2026-10-19）

| テスト | net10.0 | net48 |
|---|---|---|
| `EncodingProbe.Tests`（コア） | 1179 件成功 | 1189 件成功 |
| `EncodingProbe.PowerShell.Tests` | 981 件成功 | — |

追加したテスト:

- `EncodingTests/HeadLineCutterTests`（147 件）
- `EncodingTests/StrictDecodeVerifierTests`（29 件）
- `CsvTests/ImportCsvPortTests`・`ExportCsvPortTests`（64 件）
- `CmdletTests/ResolveEncodingCmdletTests`（-DetectionLineCount と -License の 37 件）

### 5.2 PSCompat

588 シナリオで 5.1 と 7.x が完全に一致した。追加したのは `Resolve/DetectionLineCount/*` の 98 件と `Resolve/License/PowerShell` で、すべて `OK` だった。

- 入力: UTF-8 / Shift_JIS / EUC-JP / UTF-8 BOM、改行なし、改行の混在、0 バイト、UTF-16LE・BE / UTF-32LE・BE（先頭が英数字・先頭が日本語・BOM 付き）
- N は 1・2・3・1000 と全体
- その他: `-Strategy UtfUnknownOnly`、0 と -1 の拒否

上限超過は 2 GB のファイルを両ホストで作ることになるため、PSCompat には入れていない（xUnit でだけ確かめている）。

### 5.3 内部の部品の 5.1 / 7.x の比較（使い捨てのスクリプト、2.3）

net48 版を Windows PowerShell 5.1.26100.9711 で、net10.0 版を PowerShell 7.6.7 で、リフレクションを使って直接呼んだ。

- 検証（`StrictDecodeVerifier`）: 次の 14 ケース × バッファの大きさ 4 通りで、報告する位置とバイト列が両ホストで一致した。
  - UTF-8（先頭・BOM の後・途中・境目をまたぐ多バイト文字・末尾で途切れた文字・64 KiB を超える位置）
  - Shift_JIS（途中・末尾）
  - EUC-JP
  - GBK
  - UTF-16LE・BE
  - UTF-32LE
  - UTF-16LE の BOM の後
- 解析（`ImportCsvHelper`）: M1 の 15 ケースで、出力（列・値・型名）・警告の件数・c10 の例外が両ホストで一致し、7.6.7 の `Import-Csv` と同じだった。
  **5.1 でも c12（CR だけの改行）を 2 件読め、c06 は W3C 形式として解釈し、c04 の型名は 7.x の順になる**
- 列（`ExportCsvHelper.BuildPropertyNames`）: pscustomobject・ordered・NoteProperty を足した hashtable・文字列・Select-Object・DataRowView・XmlElement・ScriptProperty は両ホストで一致した。
  FileInfo / DirectoryInfo は異なった（4.2）

### 5.4 実ホストでの手動確認

ビルド済みの DLL を `Import-Module` して実行した（Windows PowerShell 5.1.26100.9711 と PowerShell 7.6.7）。
入力は、先頭 2 行が ASCII で 3 行目に Shift_JIS の日本語がある CSV である。

| 指定 | 5.1 | 7.6.7 |
|---|---|---|
| `-DetectionLineCount 1` / `2` | `20127 / us-ascii`、Bom False、LineBreak CrLf | 同左 |
| `-DetectionLineCount 3` | `932 / shift_jis` | 同左 |
| 省略（全体） | `932 / shift_jis` | 同左 |
| `-DetectionLineCount 0` | `ParameterArgumentValidationError` | 同左 |
| `Get-Help Resolve-Encoding -Parameter DetectionLineCount` | 日本語のヘルプ | 同左 |

`PSEncodingName` / `UsePSName` は TFM ごとに意味が違う既存の仕様どおりだった（5.1 は `Ascii` / null、7.x は `ascii` / `shift_jis`）。

### 5.5 既存の判定の挙動の確認（依頼 3.6、仕様書 3.2・24.1）

英数字だけの BOM 無しの UTF-16LE / UTF-16BE / UTF-32LE / UTF-32BE を全体で判定すると、両ホストとも結果は次のとおりだった。
仕様書の推定どおりなので、課題 `docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md` を起票した。クラスライブラリは変更していない。
（第四次で直した。8 章）

- `Resolve-Encoding` は `20127 / us-ascii` を返した
- `Get-ProbedContent` は `U+0000` の混じった行を返した

### 5.6 クラスライブラリ

`git diff master -- SnowStack.EncodingProbe/` は空である（クラスライブラリは変更していない）。
（第一次の時点の記録。第四次でクラスライブラリを変更した。8 章）

---

## 6. 第二次 — `Import-ProbedCsv`（2026-10-19）

### 6.1 利用者の回答と決定

| 事項 | 決定 |
|---|---|
| U3（列名の重複） | 非終了エラーとし、次のファイルへ進む（依頼文の回答。1 章） |
| 解析の途中で復号に失敗した場合（仕様書 10 章の最後の項目） | 新しい ID `InvalidBytesDuringImport`（カテゴリ `InvalidData`）。メッセージに、ファイル・文字エンコーディング・不正な位置とバイト列・「それまでに出力したレコードは取り消せない」・「検証の後にファイルが変更された可能性」を書く。位置は検証の部品（`StrictDecodeVerifier`）でもう一度走査して求め、見つからなければ `?` とする |
| 列名の重複のエラーの対象とカテゴリ | ID（`AlreadyPresentPSMemberInfoInternalCollectionAdd`）とカテゴリ（`NotSpecified`）は標準と同じにし、対象（TargetObject）は標準の null ではなくファイルのパスにした（複数のファイルを読むときにどのファイルか分かるように） |
| 検証の失敗のメッセージに示す文字エンコーディング名 | 読み替えた後の名前（実際に検証したもの。例: us-ascii を読み替えた `utf8NoBOM`） |
| ワイルドカードが 0 件（6.2） | `Get-ProbedContent` に合わせ、エラーにしない |

### 6.2 仕様書と異なった点: ワイルドカードが 0 件

仕様書 9.1・12 章は、ワイルドカードが 0 件に一致した場合を「`Get-ProbedContent` と同じ非終了エラー（同じ ID）」としていた。
実装時に確かめたところ、既存の `Get-ProbedContent` は 5.1 でも 7.x でも、0 件のワイルドカードに対して**何も出力せず、エラーも出さなかった**
（標準の `Get-Content` はエラーを出す）。パスの解決の部品（`ProbedContentCommandBase.ResolveOne`）が、
`GetResolvedProviderPathFromPSPath` の空の結果をそのまま返すためである。

既存のコマンドの挙動は変えない（指示書 1 節）ので、部品は変えられない。利用者に確認し、`Import-ProbedCsv` も `Get-ProbedContent` に合わせてエラーにしないと決めた。
仕様書 9.1・12 章・13 章の表を改め、経緯を 9.1 に記した。

### 6.3 実装の要点

- **基底クラス:** `ProbedContentCommandBase` を継承する。`-Culture` / `-Strategy`（判定）とパスの解決（`ResolveExistingFiles`）を使う。
  `BeginProcessing` では `base.BeginProcessing()` を先に呼び、そのあと移植した `SetDelimiter` で区切り文字を決める
- **パラメータセット:** `DelimiterPath`（既定）/ `DelimiterLiteralPath` / `CulturePath` / `CultureLiteralPath`。
  名前は移植した `SetDelimiter` が見る名前と同じにした。基底クラスの定数（`Path` / `LiteralPath`）は使っていない（`ResolveExistingFiles` はセット名に依存しない）。
  `-Culture`（判定のカルチャー）と `-UseCulture`（区切り文字）を取り違えないよう、コメントとヘルプで区別した
- **-Path / -LiteralPath:** 別々のプロパティに持つ。束縛の属性と別名は `Get-ProbedContent` と同じで、`-LiteralPath` に別名 `LP` を加えた。
  `Get-ChildItem | Import-ProbedCsv` は両ホストで動いた（PSCompat）
- **文字エンコーディングの決定:**
  - 明示した場合は、その値で読む（判定も検証もしない。`-DetectionLineCount` は黙って無視する）
  - Auto で `-DetectionLineCount` が無い場合は、既存の `ProbedFileReader` で全体を判定する
  - Auto で `-DetectionLineCount` がある場合は、`HeadLineDetection` で判定し、`GetReinterpretedCodePage` で読み替える（3.2）。
    `StrictDecodeVerifier` で全体を検証してから、`BuildStrictEncoding` の厳密な `Encoding` で読む
- **全体の判定の上限超過:** ファイルを開く前に大きさで検査し、`-DetectionLineCount` を案内する新しいメッセージ `FileTooLargeToDetectUseLineCount` で報告する
  （ID は既存と同じ `EncodingDetectionFailed`）。既存の `FileTooLargeToDetect`（`Get-ProbedContent` などが使う）は変えていない
- **読み替え:** `HeadLineDetectionResult.GetReinterpretedCodePage` にまとめた（第三次の `Export-ProbedCsv` と共用する）。
  このため `EncodingVocabulary` のコードページの定数のうち 6 個を private から internal にした（値・挙動は変えていない）
- **解析:** `ProbedFileReader` に、BOM の後ろから復号する `TextReader` を返す internal の `Reader` を加え、移植した `ImportCsvHelper` に渡す。
  `ActiveReadRegistry` に登録してから読む。列名が空のときの警告のフラグは cmdlet のフィールドに持ち、複数のファイルとパイプラインの入力にまたがって 1 回だけにした
- **列名の重複:** ヘッダーを読んだ時点で分かるので、レコードは 1 件も出力していない。移植した処理が投げる `ExtendedTypeSystemException` を非終了エラーにする

### 6.4 テスト

- `CmdletTests/ImportProbedCsvTests`（59 件）
  - M1 の 15 ケース（7.6.7 の期待値）、c10 の非終了エラーと続行、警告が 1 回だけ、型名・W3C・CR だけの改行
  - `-Header`・ファイルごとの列、`-Delimiter`・`-UseCulture`（`-Culture` との併用）
  - パス: ワイルドカードで複数、存在しないファイル・フォルダーでの続行、`Get-ChildItem` からの入力、`-LP`
  - 判定: 10 章の表の全行
    - 全体で判定: Shift_JIS / EUC-JP / UTF-8（BOM 有無）/ UTF-16LE・BE（BOM 有無）
    - 明示: 語彙名・数値・`EncodingInformation`・`Encoding` オブジェクト
    - 先頭 N 行で判定
  - 読み替え
    - 先頭が ASCII の UTF-8 は読める。Shift_JIS / EUC-JP は検証で止まり、メッセージに位置とバイト列が入る
    - BOM 無しの UTF-16 / UTF-32 で先頭が英数字のもの（英数字だけ・後半に日本語）が正しく読める
    - BOM 付きも検証する
  - 一致（3.3）: `Resolve-Encoding -DetectionLineCount` の結果と、`Import-ProbedCsv` が使う `HeadLineDetection.Detect` の結果が全プロパティで一致する
  - エラー: 12 章の各行（不正な `-Culture` / `-Strategy` / `-Encoding`、0 以下、判定の失敗とコードページが無い場合の続行、全体の判定の上限超過）
  - コマンドの形（パラメータセット・別名・束縛の属性）
- 自動テストにしていないもの
  - 読み取り権限が無いファイル（`ReadAccessDenied`）。ACL の操作が要るため。実装は `Get-ProbedContent` と同じ
  - 解析の途中で復号に失敗する場合（`InvalidBytesDuringImport`）。検証と解析の間にファイルを書き換える必要があるため

### 6.5 テストと確認の結果（2026-10-19）

| テスト | 結果 |
|---|---|
| `EncodingProbe.Tests`（コア） | net10.0 1179 件 / net48 1189 件 成功 |
| `EncodingProbe.PowerShell.Tests` | 1040 件成功（`ImportProbedCsvTests` 59 件、`MamlHelpTests` の追加分を含む） |
| PSCompat | 636 シナリオで 5.1 と 7.x が完全に一致（`Import/*` の 48 件を追加） |

PSCompat の `Import/M1/*` の結果は、両ホストで同じだった。xUnit で確かめた 7.6.7 の `Import-Csv` の結果とも同じなので、
**M1 の 15 ケースは 5.1 でも 7.6.7 と同じ結果になる**。5.1 の標準とは次の点が違い、こちらが正しい。

- c04 の型名は `Foo.Bar;CSV:Foo.Bar` の順
- c06 は W3C 形式として `date time` の列になる
- c12 は CR だけの改行で 2 件読める

検証の失敗の位置とバイト列も、両ホストで同じだった（64 KiB を超える位置 100005 / `93`、BOM の後で末尾が途切れた文字 9 / `E6 97`）。

実ホスト（Windows PowerShell 5.1.26100.9711、PowerShell 7.6.7）での手動確認:

| 入力（先頭 2 行が ASCII、3 行目が「日本,語」） | 5.1 | 7.6.7 |
|---|---|---|
| Shift_JIS / EUC-JP / UTF-8 / UTF-8 BOM / UTF-16LE（BOM 付き）を全体で判定 | 2 件。3 行目は U+65E5 U+672C U+8A9E | 同左 |
| Shift_JIS、`-DetectionLineCount 2` | 0 件、`DetectionVerificationFailed`（バイト位置 16: 93、`utf8NoBOM`） | 同左 |
| Shift_JIS、`-DetectionLineCount 3` | 2 件（正しく読める） | 同左 |
| `Get-Help Import-ProbedCsv` | 日本語のヘルプ | 同左 |

### 6.6 気づいたこと

- 英数字だけの BOM 無しの UTF-32BE を全体で判定すると、既知の課題（`docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md`）のとおり us-ascii と判定され、
  列名にも値にも U+0000 が混じる。`-DetectionLineCount` を指定すると、読み替えで正しく読める。PSCompat はこの現状の挙動も記録している
  - **第四次で直した（8 章）。** 英数字だけの BOM 無しの UTF-32BE は全体の判定でも UTF-32BE（12001）と判定され、U+0000 は混じらない。
    PSCompat の該当シナリオは、両ホストとも修正後の値を記録する（ホストの結果を突き合わせるものなので、シナリオは変えていない）
- `-DetectionLineCount` の検証で報告するバイト列は、UTF-8 では不正と分かった 1 バイト（例: Shift_JIS の「日」`93 FA` の `93`）になる。
  `Convert-ProbedContent` の `InvalidSourceBytes` と同じ規則である

---

## 7. 第三次 — `Export-ProbedCsv` と仕上げ（2026-10-19）

### 7.1 利用者の回答と決定

| 事項 | 決定 |
|---|---|
| U1（余分なプロパティ） | 標準どおり、黙って書かない（警告しない）。ヘルプの NOTES に書いた |
| U2（`-EncodingFrom` の参照ファイル） | `-DetectionLineCount` 指定時は全体を検証する |
| `-Append` で追記先を判定する条件 | **1 バイト以上の追記先は常に判定する。** ヘッダーの読み取り（18.2）と末尾の改行の判定（18.3）に、追記先の文字エンコーディングが要るため。`Out-ProbedFile` は `-Encoding` の明示と `-AllowEncodingChange` の組み合わせで判定しないが、`Export-ProbedCsv` は判定する（失敗すれば終了エラー） |
| BOM だけの追記先 | ヘッダーの扱いだけ新規と同じにする。ヘッダーは書き、BOM はすでにあるので書かず、追記先の文字エンコーディングを継承する。7.x の `Export-Csv` も BOM の後ろが空なら新規と同じくヘッダーを書く |
| `Out-ProbedFile` との共通化 | 殻を基底クラス `ProbedFileOutputCommandBase` に切り出し、両コマンドが継承する（7.3） |
| パラメータセット（7.2） | 標準の `Export-Csv` と同じ 2 つにする |

### 7.2 仕様書と異なった点: `Export-ProbedCsv` のパラメータセット

仕様書 15 章は、`Import-ProbedCsv` と同じ 4 つのセット（`DelimiterPath` / `DelimiterLiteralPath` / `CulturePath` / `CultureLiteralPath`）としていた。
そのとおりに実装したところ、`$x | Export-ProbedCsv -LiteralPath a.csv` が `AmbiguousParameterSet` で失敗した。
`-LiteralPath` を指定すると `DelimiterLiteralPath` と `CultureLiteralPath` が候補に残る。`-UseCulture` は必須でも、パイプラインの束縛が終わるまで候補から外れない。
既定のセット（`DelimiterPath`）は候補に無いため、どちらにも決まらない。
`Import-ProbedCsv` はパスをパイプラインから受けるので、この問題が無い。

標準の `Export-Csv` を確かめたところ、両ホストとも `Delimiter`（既定）/ `UseCulture` の 2 セットだった。`-Path` / `-LiteralPath` はどちらのセットにもあって必須ではなく、
「どちらか一方だけ」は実行時に検査している。利用者の決定で、これに合わせた。

- `-Path`（位置 0）と `-LiteralPath` は必須にせず、`BeginProcessing` で「ちょうど一方」を検査する。
  満たさなければ終了エラー（ID `CannotSpecifyPathAndLiteralPath`、カテゴリ `InvalidData`。標準と同じ。メッセージは 5 言語）
- `-UseCulture` は `UseCulture` セットのスイッチで、必須ではない（標準と同じ）
- 仕様書 15 章と 20 章を改め、経緯を 15 章に記した

### 7.3 `Out-ProbedFile` との共通化の範囲

`Cmdlets/ProbedFileOutputCommandBase`（`ProbedContentCommandBase` を継承）に、次を移した。挙動は変えていない（既存のテスト 1040 件と PSCompat 636 シナリオが通ることを、切り出した直後に確かめた）。

- 出力先のパスの解決（ちょうど 1 件。ワイルドカードの 0 件・複数、親ディレクトリが無い、ディレクトリを指す）
- `-NoClobber` と読み取り専用の検査
- 出力先の既存ファイルの全体の判定（`DetectExisting`）
- 最初の行を書く直前にファイルを開く処理（`SamePathRoundTrip` の検出、BOM の判断）と、追記の整合性検査を含む 1 行の書き込み（`WriteChunk`）
- 入力が 0 件の場合の出力（`CreateEmptyOutput`）と後始末（`Cleanup`、`StopProcessing`、`Dispose`）

文字エンコーディングの決定（`ResolveEncoding`）は各コマンドに残した。判定する条件が違う（7.1）ためである。
整形のパイプライン（`Out-ProbedFile`）と CSV の書き出し処理（`Export-ProbedCsv`）の後始末は、各コマンドが `Cleanup` を上書きして足す。

あわせて、先頭 N 行の判定・読み替え・検証を `Internal/HeadLineEncodingResolver` にまとめ、`Import-ProbedCsv` もこれを使うようにした（第二次の実装を置き換えた。挙動は同じ）。
検証の失敗は `DetectionVerificationException`（ID `DetectionVerificationFailed`）で知らせる。
`-EncodingFrom` の参照ファイルの判定（`ProbedContentCommandBase.ResolveEncodingFrom`）には、行数を受け取る省略可能な引数を足した（省略時の挙動は従来どおり）。

### 7.4 仕様書に書かれていなかったため実装で決めた点

- **追記先のヘッダーに同じ列名が複数ある場合:** 移植した `ReadHeader` が `ExtendedTypeSystemException` を投げる。
  `Import-ProbedCsv` と同じ ID `AlreadyPresentPSMemberInfoInternalCollectionAdd`（カテゴリ `NotSpecified`、対象は追記先のパス）の終了エラーにした
- **追記先のヘッダーを読めなかった場合（仕様書 18.2 の最後の項目）:** 移植元のとおり、列の照合をせず、ヘッダー行も書かずに、オブジェクトの列で値の行だけを追記する。
  空行だけの追記先は、移植元の `ReadHeader` が空の列名 1 個のヘッダーとして読むため、列の照合で `CannotAppendCsvWithMismatchedPropertyNames` になる。
  実行中のホスト（PowerShell 7.6）の `Export-Csv -Append` と、次のとおり同じ結果になることを確かめ、テストで固定した（`ExportProbedCsvTests.Append_HeaderNotReadable_*` / `Append_BlankLinesOnly_*`）

  | 追記先 | 結果（`Export-ProbedCsv` と 7.6 の `Export-Csv` で同じ） |
  |---|---|
  | `# comment only` | `# comment only\r\n"…","…"\r\n`（ヘッダーなしで値の行を追記） |
  | `#TYPE Foo` / `# comment` | 同上 |
  | 空行だけ | `CannotAppendCsvWithMismatchedPropertyNames`、ファイルは変わらない |

- **列の照合のエラーのメッセージに示すパス:** 移植元は利用者が指定したパスを示すが、解決した絶対パスを示した（他のエラーと同じ）
- **`-InputObject` に `$null` が来た場合:** 移植元と同じく、その入力を飛ばす

### 7.5 テスト

- `CmdletTests/ExportProbedCsvTests`（39 件）
  - M4: 既定の出力を 7.6.7 の `Export-Csv` のバイト列と比べた。`AsNeeded`・`Never`・`-QuoteFields`・`-NoHeader`・`-NoTypeInformation`、`-IncludeTypeInformation`（`CSV:` の除去）も確かめた
  - 入力の種類と区切り文字: 辞書、混在、`-Delimiter`、`-UseCulture`（`-Culture` と併用）
  - `Import-ProbedCsv` との往復（3 つの文字エンコーディング）
  - 16.1 の表の全セル、`-EncodingFrom`、16.2 の 3 か所での検証の失敗（`-WhatIf` を含む）と成功
  - M5 の a〜l: UTF-16LE の追記先の末尾の改行、`-LineBreak` の明示、読み取り専用の属性の復元を含む
  - BOM だけの追記先、ヘッダーが読めない追記先（7.6 の `Export-Csv` と比較）
  - M6: `SamePathRoundTrip`、`-NoClobber`（`-WhatIf` を含む）、`-WhatIf`、空の入力
  - パスの解決、20 章のエラー、コマンドの形
- PSCompat: `Export/*` の 55 件。出力先のバイト列と、エラーの ID と本モジュールのメッセージを比べる。
  値は文字列だけにし、ホストによって形が変わるオブジェクトは使わない（第一次の決定）

### 7.6 テストと確認の結果（2026-10-19）

| テスト | 結果 |
|---|---|
| `EncodingProbe.Tests`（コア） | net10.0 1179 件 / net48 1189 件 成功 |
| `EncodingProbe.PowerShell.Tests` | 1079 件成功 |
| PSCompat | 691 シナリオで 5.1 と 7.x が完全に一致 |
| クラスライブラリ | `git diff master -- SnowStack.EncodingProbe/` は空 |

実ホスト（Windows PowerShell 5.1.26100.9711、PowerShell 7.6.7）での手動確認（両ホストで同じ結果）:

| 確認したこと | 結果 |
|---|---|
| Shift_JIS の CSV に日本語を `-Append`（M5 g） | Shift_JIS（`92 C7 8B 4C`）のまま追記された（標準は UTF-8 が混ざる） |
| 最後の行に改行が無い CSV に追記（M5 j） | `1,2` の後ろに CR-LF を補ってから `"x","y"` を追記した（標準は `1,2"x","y"` とつながる） |
| `Import-ProbedCsv a.csv \| Export-ProbedCsv a.csv`（M6） | `SamePathRoundTrip` で止まり、ファイルは元のまま（標準は 0 バイトになる） |
| 5.1 で `-Encoding` を省略して日本語を書き出す（M4） | BOM 無しの UTF-8（`E6 97 A5 E6 9C AC E8 AA 9E`）。同じ操作で 5.1 の標準の `Export-Csv` は `#TYPE` 行と `"???"`（`22 3F 3F 3F 22`） |
| `Get-Help Export-ProbedCsv` | 日本語のヘルプ |

### 7.7 文書とバージョン

- バージョン: モジュールの csproj（`Version` / `AssemblyVersion` / `FileVersion`）と `.psd1`（`ModuleVersion`、`ReleaseNotes`）を 1.3.0 にした。
  クラスライブラリの csproj は 1.2.0 のまま（指示書 4.9）。**第四次で 1.3.0 に上げた（8 章）**
- `CHANGELOG.md` / `README.md` に 1.3.0 の節を書いた。README のリリース日の行は、リリース時に利用者が書く
- `CLAUDE.md` を指示書 7 節のとおり更新した（コマンドの一覧、1.3.0 で追加したコマンドの節、判定はファイル全体を対象とするの例外、
  `Resolve-Encoding` の制約の文言、ライセンス上の注意、モジュールだけをリリースする場合の扱い、1.3.0 の作業記録）
- `docs/EncodingProbe-1.1.0-仕様書.md` に注記を 2 か所入れた（2.2 の `Resolve-Encoding` を変更しない、9 章の `-TotalCount` と判定の範囲）
- 仕様書は 9.1（ワイルドカード 0 件。第二次）、15 章・20 章（`Export-ProbedCsv` のパラメータセット。第三次）を改めた

---

## 8. 第四次 — `0x00` を含む入力の判定の修正（クラスライブラリ、2026-10-20）

仕様は `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`（以下「追補」）、期待値の根拠は `docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md`（以下「調査報告」）の 7 章。
課題 `docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md` を、調査報告の修正案 2（厳密な構造の検査）で直した。

### 8.1 利用者の回答と決定

| 事項 | 決定 |
|---|---|
| 計画 | 提示した計画のとおりに進める |
| (a) NuGet の `PackageReleaseNotes` | 追記ではなく、1.3.0 の内容に**差し替える**（それまでは「初回リリース。…」のまま古くなっていた） |
| (b) 追補 4.1 の入力のモジュールのテスト | 依頼の UTF-16LE だけでなく、UTF-16LE / BE・UTF-32LE / BE の **4 つの形**で確かめる |

### 8.2 判定処理の変更（`SnowStack.EncodingProbe/EncodingDetector.cs`）

`Detection` の ISO-2022 / ASCII の分岐で ASCII（20127）とした直後に、次を加えた。判定順序とそれ以外の判定メソッドは変えていない。

```csharp
encInfo.CodePage = CodePageAscii;
encInfo.Bom = false;

// 0x00 を含む場合だけ、英数字だけの BOM 無し UTF-32 / UTF-16 であるかを厳密に調べる（1.3.0）。
// 成立しなければ ASCII のまま（UTF-8 の判定には進まない）
if (Array.IndexOf(this._buffer, (byte)0x00, 0, this.BufferSize) >= 0)
{
    int nulPaddedCodePage;
    if (NulPaddedUnicode_Detection(out nulPaddedCodePage) == false)
    {
        encInfo.CodePage = nulPaddedCodePage;
    }
}
```

- 新しいメソッドは `NulPaddedUnicode_Detection(out int codePage)` と `AllUnitsMatch(int unitSize, int nonZeroIndex)`。`ISO2022_Detection` と `Utf16_Detection` の間に置いた
- 戻り値は既存の判定メソッドと同じく「`true` = 規格外」とし、コードページは出力引数で返す（調査報告 7.1.1 の手本は「成立したコードページ、不成立は 0」を返していたが、既存の `ISO2022_Detection(out ..., out int codePage)` の形に合わせた）
- 順序は追補 2.2 の表のとおり UTF-32LE → UTF-32BE → UTF-16LE → UTF-16BE。長さの条件（4 の倍数で 4 以上・偶数で 2 以上）もメソッドの中で見る
- 既存の `Utf16_Detection` / `Utf32_Detection` を使わない理由（調査報告 4.2）と、既知の限界（追補 4 章）を XML ドキュメントの `remarks` に書いた
- `EncodingWebName` / `PSEncodingName` / `UsePSName` は、分岐の後の既存の処理（`EncodingName` / `PSEncodingName` / `SetUsePSName`）でコードページから決まる。BOM 無しの UTF-16 / UTF-32 を判定した既存の経路と同じ値になる（`utf-16` / `unicodeFFFE` / `utf-32` / `utf-32BE`）
- `LineBreak` と `Culture` は変えていない（追補 2.3）

### 8.3 バージョン

- クラスライブラリの csproj の `Version` / `AssemblyVersion` / `FileVersion` を 1.3.0 にし、`PackageReleaseNotes` を 1.3.0 の内容に差し替えた（8.1 (a)）
- `.psd1` の `ReleaseNotes` を、同梱のクラスライブラリも 1.3.0 であることと、この修正の内容に改めた
- UTF.Unknown の参照バージョンと `EncodingProbe.License` 定数は変えていない（追補 6 章）
- これで CLAUDE.md の「バージョン更新時に触る場所」の 3 か所がすべて 1.3.0 になった

### 8.4 テスト

**コア（`tests/EncodingProbe.Tests/DetectorTests/NulInputDetectionTests.cs`、新規）**

- 入力は測定スクリプト（`tools/Measure-NulInputDetection.ps1`）の組み立てを C# に移したもので、符号位置または 16 進のリテラルからバイト列を直接作る。テストデータのファイルは増やしていない
- 期待値は調査報告 7.2 の「修正案 2」の列（Combined / NativeOnly）と、第 2 回の測定の `detect-strict-*.tsv` の UtfUnknownOnly の値。`CodePage` / `EncodingWebName` / `Bom` / `LineBreak` を確かめる
- 1 行のテストは「入力 1 件 × 判定方式 3 × カルチャー 2（ja-JP / en-US）」の 1 通り。メソッドは群ごとに分けた

| メソッド | 群 | 入力 | 行 |
|---|---|---|---|
| `AlphanumericUnicodeWithoutBom_IsDetectedAsInput` | A | 52 | 312 |
| `AsciiWithNul_StaysAscii` | B・B12 | 45 | 270 |
| `OutsideOfAsciiBranch_IsUnchanged` | C2〜C4 | 10 | 60 |
| `KnownLimit_UnicodeWithLowBytesOnlyCharacter_IsAscii_NotCorrectBehavior` | A6（既知の限界。追補 4.1・4.3） | 11 | 66 |
| `KnownLimit_ShortInput_IsDecidedByUnitShape` | B9〜B11（既知の限界。追補 4.2） | 12 | 72 |
| `InputCounts_MatchTheReport` | 群ごとの件数が調査報告と同じこと | — | 1 |

計 781 行。修正前の `EncodingDetector.cs` で実行すると 244 行が落ちることを確かめた（A の 52 件 × Combined / NativeOnly × 2 カルチャー = 208、B9〜B11 のうち UTF-16 / UTF-32 になる 9 件 × 4 = 36）。

- 入力の一覧は静的フィールドの初期化順に左右されないよう、`Lazy` で最初に使うときに組み立てる

**モジュール（`tests/EncodingProbe.PowerShell.Tests`）**

| テスト | 変更 | 行 |
|---|---|---|
| `ResolveEncodingCmdletTests.DetectionLineCount_UnicodeWithoutBom_AsciiHead_ReportsUsAscii` | `…_ReportsInputEncoding` に改名し、期待値を入力のコードページにした（調査報告 7.6） | 4（既存） |
| `ImportProbedCsvTests.Import_DetectionLineCount_UnicodeWithoutBomAsciiHead_ReadsCorrectly` | `Resolve-Encoding -DetectionLineCount 2` の期待値を入力のコードページにした（読み込みの期待値は変わらない） | 8（既存） |
| `ResolveEncodingCmdletTests.WholeFile_AlphanumericUnicodeWithoutBom_ReportsInputEncoding` | 追加。全体の判定 | 4 |
| `ResolveEncodingCmdletTests.WholeFile_UnicodeWithLowBytesOnlyCharacter_ReportsUsAscii_KnownLimit` | 追加。追補 4.1 の入力（`A,上` / `1,2`）は全体では us-ascii | 4 |
| `GetProbedContentTests.Read_AlphanumericUnicodeWithoutBom_DetectsAndDecodes` | 追加。U+0000 の混じらない行で読む | 4 |
| `ImportProbedCsvTests.Import_WholeFile_AlphanumericUnicodeWithoutBom_ReadsCorrectly` | 追加。全体の判定で正しく読む | 4 |
| `ImportProbedCsvTests.Import_UnicodeWithLowBytesOnlyCharacter_DetectionLineCountReadsCorrectly` | 追加。追補 4.1 の入力が、全体では us-ascii、`-DetectionLineCount 1` では列名 `A` / `上`、値 `1` / `2` と読めること（8.1 (b) で 4 つの形） | 4 |

追補 4.1 の入力は、ヘッダーとデータの 2 行にした。調査報告 7.5 の A6-ue-u16le（`A,上` の 1 行だけ）が `-DetectionLineCount 1` で 0 行になったのは、ヘッダーしか無いためである（エラーではない）。

**PSCompat（`tests/PSCompat/ProbedCompatScenarios.ps1`）**

`NulInput/<形>/<種類>/...` の 28 シナリオを追加した。4 つの形 × 2 種類（`alnum`: 英数字だけ、`lowbyte`: 追補 4.1 の入力）×
`Resolve-Encoding` / `Get-ProbedContent` / `Import-ProbedCsv` の全体の判定（24）と、`lowbyte` の `Import-ProbedCsv -DetectionLineCount 1`（4）。
既存の `Resolve/DetectionLineCount/<形>/ascii-head/...` のシナリオは、両ホストで同じように値が変わるので、そのままにした。

### 8.5 テストと確認の結果（2026-10-20）

| 対象 | 結果 |
|---|---|
| `dotnet test SnowStack.EncodingProbe.slnx` | コア net10.0 1960 / 1960、net48 1970 / 1970（それぞれ 781 件の追加）。モジュール net10.0 1099 / 1099（20 件の追加） |
| PSCompat | 719 シナリオ（既存 691 ＋ 追加 28）すべてで 5.1 と 7.x の結果が一致 |

**再測定（依頼 4 章）**

`tools/Invoke-NulInputMeasurement.ps1` を、修正後の `feature/1.3.0-csv` を現状（`current`）として実行した。
スクリプトは修正案のビルドを必須の引数（`-ProposalRoot`）に取るが、実験用の worktree はすでに無いため、修正後のリポジトリ自身を渡した。
出力先は一時フォルダーにし、生の結果はコミットしていない。

```powershell
dotnet build SnowStack.EncodingProbe.slnx -c Debug
pwsh -NoProfile -File tools/Invoke-NulInputMeasurement.ps1 -ProposalRoot . -OutDirectory <一時フォルダー>
```

結果の `detect-current-{net10.0,net48}.tsv` と `commands-current-{net10.0,net48}.tsv` は、第 2 回の
`tools/NulInputDetectionResults/round2/detect-strict-*.tsv` / `commands-strict-*.tsv` と**バイト単位で同一**だった。

| 比較 | 行 | 結果 |
|---|---|---|
| 判定 API（net10.0） | 1080（180 件 × 判定方式 3 × カルチャー 2） | 同一 |
| 判定 API（net48） | 1080 | 同一 |
| コマンド（net10.0） | 55（11 件 × 5 コマンド） | 同一 |
| コマンド（net48） | 55 | 同一 |

診断の `diag-*.tsv` は比べていない。`strict` 列は実験用ブランチのメソッド名（`StrictNulUnicode_Detection`）をリフレクションで呼ぶため、
名前を変えた本実装では `-` になる。

**実ホストでの手動確認（依頼 4 章）**

ビルド済みの DLL を直接 `Import-Module` し、Windows PowerShell 5.1.26100 と PowerShell 7.6.7 で同じスクリプトを実行した。両ホストで結果は同じだった。

| 入力 | 確認 | 結果 |
|---|---|---|
| 英数字だけの BOM 無し UTF-16LE の CSV（`Name,Memo` / `a,b` / `c,d`） | `Resolve-Encoding` | `1200 / utf-16 / Bom=False / CrLf` |
| 同上 | `Get-ProbedContent` | 3 行。文字のコードに U+0000 が無い |
| 同上 | `Import-ProbedCsv` | `Name=[a] Memo=[b]`、`Name=[c] Memo=[d]` |
| `Hello, World!` ＋ `00` | `Resolve-Encoding` | `20127 / us-ascii / Bom=False / None` |
| 追補 4.1 の入力（`A,上` / `1,2` の BOM 無し UTF-16LE） | `Resolve-Encoding`（全体） | `20127 / us-ascii` |
| 同上 | `Resolve-Encoding -DetectionLineCount 1` | `20127 / us-ascii`（`Resolve-Encoding` は読み替えない） |
| 同上 | `Import-ProbedCsv -DetectionLineCount 1` | 1 件。列名 `A`（0041）と `上`（4E0A）、値 `1` と `2` |

### 8.6 追補と異なった点・追補に無く決めた点

- 追補と異なった点は無い
- 新しいメソッドの名前と戻り値の形（8.2）。追補 2.2 は「名前・コメント・置き場所は既存のコードのスタイルに合わせて書き直す」としている
- テストの行の単位（8.4）と、UtfUnknownOnly の期待値を第 2 回の測定の TSV から取ったこと（調査報告 7.2 の表は NativeOnly の値だけを載せている）
- 再測定で `-ProposalRoot` に修正後のリポジトリ自身を渡したこと（8.5）
- 追補 7 章の表に無い `docs/EncodingProbe-1.3.0-リリース手順.md` も直したこと（「クラスライブラリは 1.2.0 のまま公開しない」が修正後の状態と合わなくなったため。8.7）
- 未追跡だった追補（仕様書）をコミットしたこと。依頼文・実測依頼の文書はコミットしていない
- 配布フォルダーの DLL は作り直していない（第三次のビルドのまま）。リリース手順 2 章に、作り直す必要があることを注記した

### 8.7 直した文書

| 文書 | 内容 |
|---|---|
| `docs/EncodingProbe-1.3.0-仕様書.md` | 冒頭（対象バージョン・前提文書）、1.1（表に追補の行）、1.2（同時リリース。経緯）、1.3、3.2（UTF-16 / UTF-32 の行が必要な理由を追補 4.1 の入力に改め、経緯を注記）、5.3 の License 定数の理由、6.3、24 章 U4、24.1（1.3.0 で対応した）。1.4 は確かめた（この課題を「扱わないもの」に含めていない） |
| MAML（5 言語、`zh-TW` を `zh-HK` / `zh-MO` に複製） | `Resolve-Encoding -DetectionLineCount` の記述を改め、NOTES に既知の限界 3 点の項目を追加。`Import-ProbedCsv` / `Export-ProbedCsv` の `-DetectionLineCount` に、追補 4.1 の入力を読める旨の案内を追加 |
| `docs/EncodingProbe-1.3.0-動作確認手順書.md` | 1.2 の `u16_ascii.csv` の期待値を 1200 にし、追補 4.1 の入力の手順を追加。4 章のチェックの 2 をクラスライブラリも 1.3.0 に改めた |
| `docs/EncodingProbe-1.3.0-実装記録.md` | 本章。冒頭の章の構成、5.5・5.6・6.6・7.7 に注記（元の記述は残した） |
| `docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md` | 状態を「対応済み（1.3.0）」にし、5 章（採用した直し方・結果・残った限界・参照）を追加 |
| `docs/EncodingProbe-1.3.0-ClaudeCode指示書.md` | 1 節に追補の例外を注記。4.9 に第四次で改めた旨を注記 |
| `CHANGELOG.md` | 1.3.0 の見出しから「PowerShell モジュールのみ」を外し、クラスライブラリの不具合の修正の節を追加。既知の課題の節を削除 |
| `README.md` | 1.3.0 の節に、クラスライブラリの修正と既知の限界を追加 |
| `CLAUDE.md` | 判定パイプラインの 3、バージョン更新時に触る場所の注記、1.3.0 の作業記録 |
| `publish/SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.psd1` | `ReleaseNotes`（8.3） |
| `docs/EncodingProbe-1.3.0-リリース手順.md` | 冒頭（クラスライブラリも 1.3.0 として公開する）、1 章の確認、2 章に配布フォルダーを作り直す注記、4 章に NuGet パッケージの公開と実験用ブランチの削除を追加 |

---

## 9. 第五次（前半） — 書き込みで文字を失う件・切り出し・改行の判定・ISO-2022・CSV の追記（2026-10-22）

テスト担当のブラックボックステスト（`C:\_test\EncodingProbeBboxTest\reports\bugs\`）で見つかった不具合のうち、BUG-0001・0002・0009・0010・0012・0013・0014 と、
前のセッションの調査で見つかった `Export-ProbedCsv -Append` の空の列名の件を直した。後半（パスの束縛・同じファイルの判定・文書・検証の部品）は別の依頼で扱う。
切り出しの規則の根拠は `docs/EncodingProbe-1.3.0-調査-切り出しの境目.md`（以下「調査文書」。調査ブランチ `investigate/1.3.0-cut-threshold` から、この文書だけをファイル単位で持ってきた）。

### 9.1 利用者の決定

| 事項 | 決定 |
|---|---|
| 表現できない文字の書き込み | エラーにする。1.1.0 からの挙動の変更として認める。逃げ道のスイッチは作らない（依頼 0.4） |
| 継承した us-ascii | BOM 無しの UTF-8 とみなす（依頼 0.4） |
| 細部の仕様の変更 | 認める。変えたものは CHANGELOG に「挙動の変更」として書く（依頼 0.4） |
| 切り出しの規則の値 | 16・0.6・Z = 0・0.2・Abr ≥ 4・0.25・P ≤ 0.7 × N で確定（依頼 2.1） |
| us-ascii のファイルに `-Encoding utf8NoBOM` / `utf8BOM` を明示して日本語を追記 | **許可する**（計画時の質問。整合性検査でも追記先の us-ascii を UTF-8 とみなす。省略したときと結果をそろえる）。1.1.0 仕様書 6.1 の表の 1 行目を改めた |
| us-ascii の `EncodingInformation` を `-Encoding` に渡した書き込み | **UTF-8 とみなす**（計画時の質問。継承と同じに扱う）。`ConvertTo-DotNetEncoding` は読み取りと同じ用途なので us-ascii のまま |
| `Export-ProbedCsv -Append` の空の列名 | **`-Force` の場合だけ新しい ID `CannotAppendCsvWithEmptyPropertyName`**（計画時の質問。`-Force` なしは標準と同じ ID のまま） |

### 9.2 書き込みで文字を失う件（BUG-0001・0010・0012）

対象は依頼のとおり 4 コマンド（`Set-ProbedContent`・`Add-ProbedContent`・`Out-ProbedFile`・`Export-ProbedCsv`）。ほかに書き込むコマンドは `Convert-ProbedContent` だけで、もともと例外フォールバックで符号化している。

**符号化（`Internal/ProbedFileWriter`）。** StreamWriter をやめ、`EncodingVocabulary.BuildStrictEncoding(コードページ)` の Encoder で 1 回の書き込みごとに符号化してから、そのバイト列を FileStream に書く。

- コードページから組み立て直すので、利用者が渡した Encoding のフォールバック（置換・例外）の設定は使わない（BUG-0012）。net48 では `Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)` になり、ベストフィット（`é` → `e`）も起きない（PSCompat の 5.1 で確かめた）
- Encoder は書き込みをまたいで状態を持ち越す。ISO-2022 のエスケープシーケンスの出方は StreamWriter を使っていたときと同じ（完了するときに `flush: true` で ASCII に戻す）
- 表現できるかどうかは、書く前に状態を持たない `Encoding.GetByteCount(string)` で確かめる（`EnsureRepresentable`）。Encoder の状態を進めると、書かなかった塊の状態が完了時の flush に出てしまうため。
  文字を表現できるかどうかは ISO-2022 の切り替えの状態によらない。`Set-` / `Add-ProbedContent` は、要素の境目で分かれたサロゲートペアも 1 文字として調べるため、1 レコード分をまとめて調べる
- 対になっていないサロゲートも、表現できない文字として止まる（第四次までは U+FFFD や `?` で書いていた）

**エラー。** ID は `UnrepresentableCharacter`（`Convert-ProbedContent` と同じ）、分類は InvalidData。メッセージは新しいキー `UnrepresentableCharacterOnWrite`（5 言語）で、
符号位置（`U+XXXX`。補助文字は 1 文字として）・文字・文字エンコーディングの統一語彙名・パスを示す。`Convert-ProbedContent` のキーは「変換しませんでした」で、書き込みには合わないので分けた。
例外（`Internal/UnrepresentableCharacterException`）には .NET の `EncoderFallbackException` を内部例外として持たせない。持たせると、PSCompat の最も内側のメッセージが .NET の文言（5.1 と 7.x で違う）になった（1.1.0 の方針と同じ）。

**元に戻す方法（依頼 1.2 で開発側に任された点）。**

| 書き方 | 方法 | 元に戻すとき |
|---|---|---|
| 上書き・新規作成（`Set-ProbedContent`、`Out-ProbedFile` / `Export-ProbedCsv` の `-Append` なし） | 同じフォルダーの一時ファイル `.<名前>.<8 桁の 16 進>.probedtmp` に書き、完了したときに `File.Replace`（書き込み先が無ければ `File.Move`）で置き換える | 一時ファイルを消すだけ。元のファイルには触れない |
| 追記（`Add-ProbedContent`、`-Append`） | 追記先を直接開き、開く前の長さを覚えておく | 元の長さに切り詰める。追記先が無かった場合は、作ったファイルを消す |

- 元に戻すのは、表現できない文字と、書き込みの IOException（ディスクがいっぱいなど）のとき。`EncodingChangeOnAppend` は第四次までと同じく、それまでに書いた行（検査を通った行）を残す。
  パイプラインの停止（Ctrl+C）や、ほかのコマンドの終了エラーで後始末だけが走った場合も、それまでに書いた内容を残す（第四次までと同じ。上書きなら、その時点の一時ファイルで置き換える）
- `-ErrorAction Stop` では `WriteError` が戻らず、後始末（`Dispose`）が書き込みを完了してしまう。そのため、**元に戻してから** `WriteError` を呼ぶ（テストで見つけた）
- 置き換えの後、置き換える前のファイルの属性（隠しファイルなど）を付け直す。読み取り専用は、これまでどおり `ReadOnlyAttributeScope` が戻す（テストで確かめた）
- 書いている間の一時ファイルは `Internal/TemporaryFileRegistry` に載せ、ワイルドカードの解決（`ProbedContentCommandBase.ResolveOne`、`ProbedFileOutputCommandBase` の 1 件の解決）から除く。
  `'a','b' | Set-ProbedContent -Path *` で、2 件目のレコードが 1 件目の一時ファイルに一致してしまうため
- 置き換えの完了に失敗した場合（ほかのプロセスが書き込み先を共有の削除なしで開いているなど）は、`Set-` / `Add-ProbedContent` は `EndProcessing` で非終了エラー `WriteFailed` / `WriteAccessDenied`、
  `Out-ProbedFile` / `Export-ProbedCsv` は終了エラーにし、一時ファイルを消す
- 一時ファイルで置き換える方法の限界（第四次までの直接書き込みとの違い）:
  - 書き込み先がほかのプロセスに開かれていると、失敗するのが開く時点ではなく完了の時点になる。開いている間、ほかのプロセスは元の内容を読める
  - フォルダーにファイルを作る権限が無く、書き込み先のファイルだけに書き込める場合は、書けなくなる
  - シンボリックリンクに書くと、リンクの先ではなくリンク自体が通常のファイルに置き換わる。Linux では、元のファイルのアクセス権（mode）を引き継がない（Windows の `ReplaceFile` は ACL・作成日時・代替データストリームを引き継ぐ）
  - プロセスが強制終了されると、一時ファイルが残ることがある（元のファイルは変わらない）
  - 上書きする前に元のファイルを一時フォルダーに写しておき、失敗したら書き戻す方法も考えたが、成功したときも毎回写す費用がかかるので採らなかった

**継承した us-ascii。** `EncodingVocabulary.TreatInheritedAsciiAsUtf8` で、us-ascii を BOM 無しの UTF-8（改行の情報はそのまま）に置き換える。呼ぶ場所は 2 か所だけ。

- `EncodingInheritance.FromFile`: 書き込み先・追記先からの継承（省略・`Auto`）、`-EncodingFrom`、追記の整合性検査の相手（`Add-ProbedContent` の baseline、`Out-ProbedFile` / `Export-ProbedCsv` の `AppendBaseline`）がすべてここを通る。
  `Convert-ProbedContent -EncodingFrom` も通るので、us-ascii の参照ファイルからの変換先は UTF-8 になる（CHANGELOG に書いた）
- `EncodingVocabulary.Resolve`: 書き込み用途（`EncodingUsage.Write`）で `EncodingInformation` を渡された場合
- `-DetectionLineCount` の経路（`HeadLineEncodingResolver`）は、もともと us-ascii を読み替えている（1.3.0 仕様書 3.2）

**追記の整合性検査（`AppendConsistency`）。** 指定・既存の両方を、コードページから例外フォールバックで組み立て直して符号化する。既存の側で表現できない文字は「一致しない」（`EncodingChangeOnAppend`）とする。
指定の側で表現できない文字は、その前に `UnrepresentableCharacter` で止まっている。第四次までは置換フォールバックで `?` 同士が一致していた。

### 9.3 切り出し（BUG-0002・0013）

`HeadLineCutter.DetermineUnit` の手順 2・3 を、調査文書 1 章の囲みの規則（P ≤ 0.7 × N を含む改訂版）に置き換えた（1.3.0 仕様書 2.4）。

- 先頭 64 KiB を 1 回読み、`DecideUnit(byte[], int, bool)`（internal static）が 4 型の N・A・B・Z・Abr と P を数えて型を決める。調査の仮実装（調査ブランチ）は使わず書き直した。比は整数で比べる（⌈0.6N⌉ = ⌊(3N + 4) / 5⌋、5B ≤ A、10P ≤ 7N、4 × 逆の Abr ≤ Abr）
- UTF-32 の型では改行の向きを見ない（調査文書 1 章の囲みのとおり。調査の評価のモデル `Rule.Predict` は UTF-32 にも改行の向きの救済を許していたが、1033 件の結果は同じ）
- 手当て（依頼 2.2）: 1 バイトの型になり、先頭 64 KiB に `00` があり、ファイル全体が 64 KiB 以内なら `None`（ファイル全体）
- 型が決まったら、その型の形の改行だけを探す（依頼 2.3）。見つからなければ従来どおり `None`

**回帰テスト（`EncodingTests/HeadLineCutterRuleTests`）。** 調査の `CutThreshold.cs` の生成の部分（`Sample`・`Rng`・`Generator`）を、生成の処理と擬似乱数・種を変えずに `Helpers/CutThresholdSamples.cs` に移した。

- 1033 件の SHA-256 を改行でつないだ値の SHA-256 が、調査の `manifest.csv` から求めた値（`2d7a1854…`）と一致する（バイト列が調査と同じ）
- 調査文書 1 章の囲みを浮動小数点でそのまま書いたモデルと、製品の `DecideUnit` の結果が **1033 件すべてで一致**した（手当てで `None` になるものは `None` を期待値にした）
- モデルが、調査文書の数（改めた規則で 1033 件中 71 件を誤る）を再現することも確かめた（書き写しの誤りの検出のため）
- `HeadLineCutterTests` の 2 件（`Cut_Utf16Le_ByteOfKanjiIsNotLineBreak`・`Cut_Utf16Be_ByteOfKanjiIsNotLineBreak`）は、8〜10 バイトで A ≥ ⌈0.6N⌉ を満たさず 1 バイトの型になり、
  手当てでファイル全体を切り出す。期待値を「`None`・ファイル全体（漢字の `0A` で切らない）」に改めた。UTF-16 と決まる長さの入力で漢字の `0A` で切らないことを確かめるテストを 1 つ足した

**最終的な結果（依頼 2.4。調査文書 11 章の `Invoke-CutOutcome.ps1` と同じ観点）。** 調査の入力のうち、BUG-0002 の系統（T-nul の 264 件。テスト担当の C-03 の入力）、BUG-0013 の再現の入力（5 件）、
調査の規則で誤った 97 件（R 群の us-ascii、後退する 11 件などを含む）の計 366 件を、第四次（コミット `85318cb` のビルド）と第五次で比べた。
`Invoke-CutOutcome.ps1` を作業フォルダーに写し、対象の一覧と、切り出しで決まった型（`unit`）の列を足して実行した（リポジトリには入れていない）。PowerShell 7.6.7、ja-JP。
`Import` は `-Encoding` で正しい文字エンコーディングを明示した結果との比較、`-Append` は `-DetectionLineCount` なしで追記したバイト列との比較（調査文書 11.1 節と同じ分類）。

| 入力（件数） | 切り出しの型（第四次 → 第五次） | `Import-ProbedCsv -DetectionLineCount 1`（第四次 → 第五次） | `Export-ProbedCsv -Append -DetectionLineCount 1`（第四次 → 第五次） |
|---|---|---|---|
| BUG-0002 の系統（T-nul、264） | None 128・UTF-16BE 82・UTF-16LE 36・UTF-32BE 18 → **None 264**（手当て） | correct 128・DVF 80・**wrong 56** → **correct 264** | 同じ 128・DVF 80・**壊れる 56** → **同じ 264** |
| BUG-0013 の再現（5） | 逆のバイト順 5 → **正しいバイト順 5** | **wrong 5** → **correct 5** | **壊れる 5** → 同じ 3・比較できない 2（※1） |
| R 群 us-ascii（26） | None 11・UTF-16 15 → 1 バイト 24・None 2 | correct 9・**wrong 9**・DVF 6・列名の重複 2 → **correct 24**・列名の重複 2 | 同じ 7・**壊れる 8**・DVF 6・`Argument` 2・列なし 2・全体でも失敗 1 → **同じ 20**・`CannotAppendCsvWithEmptyPropertyName` 4・列なし 2 |
| 後退する 11 件（調査文書 11.4 節） | UTF-16 10・None 1 → **None 11**（手当て） | correct 8・DVF 2・EDF 1 → correct 9・DVF 1・EDF 1（※2） | 同じ 5・DVF 1・列なし 5 → 同じ 6・列なし 5 |
| R 群 Shift_JIS（26。調査文書 10.4 節） | 変わらない（None 11・UTF-16 15） | correct 12・DVF 14（変わらない） | 同じ 10・DVF 14・列なし 2（変わらない） |
| S 群 U+0D00 / U+0A00 が 10% 以上（20。既知の限界） | UTF-16 20 → UTF-16 16・1 バイト 4 | wrong 12・DVF 8（変わらない。全体で判定しても誤る） | 壊れる 2・DVF 8・`Argument` 10 → 壊れる 2・DVF 8・`CannotAppendCsvWithEmptyPropertyName` 10 |
| H 群 短い UTF-16（14。既知の限界） | 変わらない | correct 11・EDF 2・wrong 1（変わらない。wrong は「上」だけのファイル） | 列なし 14 |

（DVF = `DetectionVerificationFailed`、EDF = `EncodingDetectionFailed`）

- ※1 `A<U+0D00>,B`（英数字だけのヘッダー）の 2 件は、全体の判定が 20127（追補 4.1 の既知の限界）で、`-DetectionLineCount` なしの追記が比較の基準にならない。
  `-DetectionLineCount 1` の追記を直接確かめると、正しい UTF-16 で書いている（`"9","8"` と CR-LF。BUG-0013 の報告の手順 3）
- ※2 DVF 1・EDF 1 は L 群の 2 件（先頭 64 KiB に英数字も改行も無い UTF-16。全体で判定しても誤る）。調査の仮実装で後退した 9 件（黙って化けた `H Utf16Le tiny06` / `tiny12` を含む）は、
  手当てでファイル全体を判定するため、すべて第四次と同じ（正しい）結果になった。`H Utf16Be size256 alnum0` は、第四次の DVF が correct に変わった
- `-DetectionLineCount 10` も、BUG-0013 の系統（第四次も 3 件は正しかった）を除いて 1 と同じ傾向（第五次では T-nul・BUG-0013・R 群 us-ascii がすべて correct）
- R 群 us-ascii・S 群の `Argument` は、追記先のヘッダーに空の列名がある入力（9.6 節）。ID が変わっただけで、止まる点は同じ
- 第五次の結果は、Windows PowerShell 5.1 でも同じだった（9.8 節）

### 9.4 改行の判定（BUG-0014 と、コアの DetectLineBreak）

**疑いの確認（依頼 3.1）。** 疑いは本物だった。「上」「《」「不」を各行に含む本文（`日本語の上《不》,B` / `1上,2《` / `不3,4`）を、BOM 付き・BOM 無しの UTF-16LE / BE・UTF-32LE / BE × CR-LF / LF / CR の 24 通りで作り、第四次のビルドで確かめた（7.x、ja-JP）。

| 確かめたもの | 第四次で誤った数（24 通り中） | 誤り方 |
|---|---:|---|
| `Resolve-Encoding` の `LineBreak` | 24 | CR-LF → `LfAndCrAndCrLf`、LF・CR → `LfAndCr` |
| `Convert-ProbedContent -PassThru` の `SourceLineBreak` | 0 | 復号した文字列から求めているので正しかった（依頼の疑いのうち、ここは当たらなかった） |
| `-EncodingFrom` による改行の継承（`Set-ProbedContent`・`Convert-ProbedContent`） | 8 | 改行が CR のファイルから LF を継承（`LfAndCr` の混在の規則で LF になる） |
| `Add-ProbedContent` の追記の改行 | 8 | 同上 |
| `Export-ProbedCsv -Append` の追記の改行（`-DetectionLineCount` なし・1） | 8・8 | 同上 |

CR-LF と LF のファイルは、混在の規則でたまたま正しい改行を選んでいた。第五次のビルドでは、24 通りのすべての項目が実際の改行と一致した（7.x と 5.1）。

**直し方。** `EncodingDetector` に `internal static LineBreakType DetectLineBreak(byte[] buffer, int length, int codePage)` を足した。UTF-16 は 2 バイト、UTF-32 は 4 バイトの単位にオフセット 0 から区切り、
単位の値が U+000D / U+000A のものだけを数える（BOM はちょうど 1 単位なので区切りは変わらない。端数のバイトは数えない）。`Detection` は、BOM・`0x00` を含む ASCII の分岐（`NulPaddedUnicode_Detection`）・
UTF-32・UTF-16 で UTF-16 / UTF-32 に決まったときに、この単位で数え直す（`RecountLineBreakByUnit`）。EUC-JP と Shift_JIS の曖昧解消に使う改行は、従来どおりバイト単位（判定の途中で使うため）。

- 公開の `DetectLineBreak()` は署名も挙動も変えていない（文字エンコーディングを知らずに呼ばれる。公開 API を変えない制約）。CHANGELOG に書いた
- `UtfUnknownOnly` で UTF.Unknown が BOM 付きの UTF-16 を返す場合も、BOM の分岐で数え直した値が使われる。UTF.Unknown は BOM 無しの UTF-16 / UTF-32 を返さないので、それ以外の経路は扱っていない

**`-DetectionLineCount` の読み替え（依頼 3.3）。** `HeadLineEncodingResolver` は、us-ascii を UTF-16 / UTF-32 に読み替えた場合に、切り出したバイト列（`HeadLineDetectionResult.Bytes` を足した）を読み替えた後の単位で数え直す。
1.3.0 仕様書 3.2 の「LineBreak は読み替えない」を改めた。

- `Resolve-Encoding -DetectionLineCount` は読み替えない（仕様書 3.1）ので、BUG-0014 の入力（英数字と「上」だけの UTF-16）には、第五次でも `20127 / LfAndCr` を報告する。
  us-ascii として読めば `0A` は LF であり、報告している CodePage とは食い違っていない。テスト担当の期待（`Cr`）とは違う。決めきれなかったこととして 9.9 節に挙げた

**テスト（依頼 3.4）。** コアの `DetectorTests/UnitLineBreakTests`（24 通り × `EncodingProbe.Detect` と `Detection`、`0x00` を含む ASCII の分岐、単位の処理そのもの、公開の `DetectLineBreak()` が変わっていないこと）と、
モジュールの `CmdletTests/UnitLineBreakCmdletTests`（24 通り × `Resolve-Encoding`・`Convert-ProbedContent -PassThru`・`-EncodingFrom`（Set と Convert）・`Add-ProbedContent`・`Export-ProbedCsv -Append`（`-DetectionLineCount` なし・1）、
BUG-0014 の再現の入力 8 通り、調査文書 11.4 節で「改行だけ違う」とした 2 件（`H Utf16Be tiny06`・`H Utf16Be size256 alnum5`））。

### 9.5 ISO-2022（BUG-0009）

`StrictDecodeVerifier.Verify` に `rejectIso2022Escape` を足した。`HeadLineEncodingResolver` が us-ascii を UTF-8 に読み替えたときだけ指定する。
復号の検証と同じ走査の中で、ESC（`0x1B`）の直後が `$`・`(`・`)` のバイト列を探す（ファイルを読む回数は増えない）。ESC と次のバイトがバッファの境目にかかる場合も見つける（バッファの大きさ 1〜64 KiB で確かめた）。

- 見つかれば `DetectionVerificationFailed`（同じ ID）。メッセージは新しいキー `DetectionVerificationIso2022Escape`（5 言語）で、ESC の位置とその 2 バイト、先頭 N 行が英数字だけなので UTF-8 として読んだこと、
  `-DetectionLineCount` を外すか、エスケープシーケンスのある行まで増やすように書いた
- 同じバッファの中で、エスケープシーケンスより前に不正な UTF-8 のバイト列がある場合は、不正なバイト列のほうを報告する（そのバッファの復号を先に確かめるため）
- 端末の制御文字を記録したログ（`ESC ( B`。`tput sgr0` の出力など）も止まる。判定を誤ったまま処理するより安全な側に倒した。`ESC [`（色など）は止めない
- 全体で判定した場合（`-DetectionLineCount` なし）と、`-Encoding` で明示した場合は探さない。全体の判定は ISO-2022 を正しく判定する
- BUG-0009 の再現の入力（ISO-2022-JP / KR）で、`Import-ProbedCsv -DetectionLineCount 1` は 1 件も出力せずに DVF、`Export-ProbedCsv -Append -DetectionLineCount 1` は何も書かずに DVF になった。`-DetectionLineCount 2` では従来どおり正しく読める

### 9.6 `Export-ProbedCsv -Append` で、追記先のヘッダーに空の列名がある件

**実測（依頼 5.1）。** 標準の `Export-Csv -Append`（PowerShell 7.6.7 と Windows PowerShell 5.1.26100、ja-JP）に、ヘッダーの違う追記先へ `[pscustomobject]@{A='x';B='y'}` を追記した（逆コンパイルはしていない）。
2 行目は `1,2,3`。両ホストで同じ結果だった。

| 追記先のヘッダー | `-Force` なし | `-Force` あり |
|---|---|---|
| `A,,B`（途中が空） | `CannotAppendCsvWithMismatchedPropertyNames`（InvalidData）。何も書かない | `Argument`（`PSArgumentException`、InvalidArgument、「引数 "name" の値が無効なため…」）。何も書かない |
| `,A,B`（先頭が空） | 同上 | 同上 |
| `A,"",B`（引用符で囲んだ空） | 同上 | 同上 |
| 空の行（ヘッダーが空）・`""` | 同上 | 同上 |
| `A,B,`（末尾が空） | 追記する（`"x","y"`。末尾の空の列は無視される） | 同左 |
| `A,,`（末尾の 2 つが空） | 追記する（`"x"`。ヘッダーは `A` だけと読まれる） | 同左 |

第四次の本モジュールも、ID・分類・書いたバイト列まで標準と同じだった（移植したコードのとおり）。`-Force` ありの例外は、移植した `ExportCsvHelper` が空の名前で `PSObject.Properties[""]` を引いたときに出る。

**決めたこと（依頼 5.2）。** 止まる点（最初のオブジェクト・何も書く前）と `-Force` なしの ID は標準に合わせたまま、`-Force` ありだけを、照合を通った直後に検査して
ID `CannotAppendCsvWithEmptyPropertyName`（InvalidData、対象は出力先のパス）と本モジュールのメッセージ（キー `CsvAppendEmptyPropertyName`、5 言語）にした（利用者の決定）。
移植したコード（`Internal/Csv/`）は変えていない。検査は `ExportProbedCsvCommand.RejectEmptyPropertyName`。

### 9.7 テスト

| テスト | 内容 |
|---|---|
| コア `DetectorTests/UnitLineBreakTests`（61） | 9.4 節 |
| `EncodingTests/HeadLineCutterRuleTests`（5） | 9.3 節（1033 件の一致・バイト列の同一性・モデルの誤りの数・手当て） |
| `EncodingTests/HeadLineCutterTests` | 漢字のバイトの 2 件の期待値を改め、1 件（2 通り）を足した |
| `EncodingTests/StrictDecodeVerifierTests` | ISO-2022 のエスケープシーケンス（3 通り × バッファの大きさ 8 通り）、ほかの ESC を止めないこと |
| `CmdletTests/UnrepresentableCharacterTests`（39） | 4 コマンド × 明示・継承・`-EncodingFrom`・Encoding のインスタンス（例外・置換フォールバック）、メッセージ、途中のレコードでの取り消し、複数のファイル、検査の順序、属性、一時ファイルとワイルドカード |
| `CmdletTests/InheritedAsciiTests`（18） | BUG-0001 の再現の入力、明示の utf8NoBOM / utf8BOM、ASCII だけの場合のバイト列、`-EncodingFrom`、`EncodingInformation`、`-Encoding ascii`、`ConvertTo-DotNetEncoding` |
| `CmdletTests/UnitLineBreakCmdletTests`（106） | 9.4 節 |
| `CmdletTests/DetectionLineCountRegressionTests`（17） | BUG-0002・0009・0013 の再現の入力、空の列名 |
| 改めた既存のテスト | `AddProbedContentTests.Append_FollowsByteSequenceRule`（us-ascii に utf8NoBOM で日本語 → 許可。us-ascii に shift_jis で日本語 → Error を足した）、`DetectionOptionsTests.Write_EncodingFromWithJapaneseCulture_DoesNotProduceKoreanBytes`（EUC-JP で表せないハングル → `UnrepresentableCharacter`、ファイルを作らない） |
| PSCompat | `Fifth/` のシナリオ（表現できない文字 7 通り × 6 つの書き方、取り消し、Encoding のインスタンス、継承した us-ascii、改行の単位 4 型 × 3 改行 × 3、BUG-0002・0009・0013・0014、空の列名） |

### 9.8 テストと確認の結果（2026-10-22）

| 確認 | 結果 |
|---|---|
| コアのテスト | net48 **2031 件**、net10.0 **2021 件**、すべて成功（第四次から各 61 件増） |
| モジュールのテスト | net10.0 **1292 件**、すべて成功（第四次の 1099 件から 193 件増） |
| PSCompat | **831 シナリオ**で 5.1 と 7.x の結果が完全に一致（第四次の 719 から 112 件増）。`Fifth/` の結果も期待どおり（エラーの場合はすべてファイルが変わらず、一時ファイルも残っていない）ことを、5.1 のレポートで確かめた |
| 9.4 節の 24 通りの確認 | 第五次のビルドで、7.x・5.1 とも全項目が一致 |
| 9.3 節の 366 件 | 5.1 の結果は 7.x と全列で一致（`-Append` の分類だけ 4 件違うが、書いたバイト列は同じ。分類のスクリプトが置換フォールバックで復号して比べており、不正なバイト列の復号が .NET Framework と .NET Core で違うため） |
| テスト担当のスクリプト | 作業フォルダー（scratchpad）に Release ビルドで配布フォルダーと同じ構成を作り、`scripts\BUG-0001・0002・0009・0010・0012・0013・0014.ps1` の写しを 7.6.7 と 5.1 で実行した（テスト担当のフォルダーには何も置いていない）。結果は下の表。両ホストで同じ（メッセージの言語だけ違う場合があった。下の注） |

| BUG | 結果（7.6.7・5.1） |
|---|---|
| 0001 | 4 コマンドとも、日本語を UTF-8（`E6 97 A5 E6 9C AC`）で書いた。`3F` は無い。参考の `-Encoding utf8NoBOM` も書けた（第四次は `EncodingChangeOnAppend`） |
| 0002 | A・B とも、`Import-ProbedCsv` の結果は `-DetectionLineCount` の有無で同じ。`-Append -DetectionLineCount 1 -Force` は ASCII の行（`22 78 22 2C 0A` など）を書いた。`Resolve-Encoding -DetectionLineCount 1` の `LineBreak` は B で `CrLf`（第四次は `Cr`） |
| 0009 | ISO-2022-JP / KR とも、`Import-ProbedCsv -DetectionLineCount 1` と `Export-ProbedCsv -Append -DetectionLineCount 1` は `DetectionVerificationFailed`、ファイルは変わらない（スクリプトの表示の「appended: 0A」は、ファイルが変わらないときの添字の範囲の計算による見かけ。「=> ACTUAL: …」の行は固定の文字列） |
| 0010 | 6 つの手順すべてが `UnrepresentableCharacter`。新規のファイルは作らず、既存のファイルは変わらない |
| 0012 | 5 つのコマンドすべてが `UnrepresentableCharacter`（InvalidData）、ファイルは変わらない。生の `EncoderFallbackException` は出ない |
| 0013 | 3 つの入力とも、`-DetectionLineCount 1` の判定は全体の判定と同じ（1201 / 1200）、`Import-ProbedCsv` は `-Encoding` で明示した結果と同じ。`-Append` は正しいバイト順の単位で書いた |
| 0014 | 6 つの入力すべてで、`-Append -DetectionLineCount 1` が期待どおりのバイト列（CR / CR-LF）を書いた（「=> ok」）。`Resolve-Encoding -DetectionLineCount 1` は `20127 / LfAndCr` のまま（9.9 節の 1） |

注: このセッションの道具（PowerShell の呼び出し）から 5.1 を起動したときだけ、5.1 の本モジュールのメッセージが英語になった。Git Bash から起動すると日本語（UI カルチャーは ja-JP）。
起動の環境によるもので、本モジュールの不具合ではない（同じ DLL・同じスクリプトで確かめた）。

### 9.9 決めきれなかったこと・仕様と食い違ったこと・新たに気づいたこと

1. **`Resolve-Encoding -DetectionLineCount` は、読み替えないので BUG-0014 の入力に `LfAndCr` を報告する。** 依頼 3.3 は読み替えた場合（`Import-` / `Export-ProbedCsv`）の数え直しで、
   `Resolve-Encoding` は仕様書 3.1 のとおり先頭 N 行が us-ascii に見えることを報告する。テスト担当の期待（`Cr`）とは違う。切り出しの型（UTF-16）で数え直した値を報告するか、
   us-ascii と報告する以上は今のままにするかは、利用者の判断を求める
2. **`EncodingChangeOnAppend` は、それまでに書いた行を残す（元に戻さない）。** 表現できない文字は元に戻すようにしたので、`Out-ProbedFile -Append` / `Export-ProbedCsv -Append` と
   `Add-ProbedContent` の複数のレコードで、失敗の種類によってファイルが元に戻るかどうかが違う。依頼の範囲（表現できない文字）を超えるので変えていない。そろえるかは利用者の判断を求める
3. **パイプラインの停止（Ctrl+C）での上書き。** 一時ファイルに書いた途中までの内容で置き換える（第四次までの「途中までを書いた状態」と同じ結果）。元のファイルを残すほうが安全とも考えられるが、変えていない
4. **一時ファイルで置き換える方法の限界**（9.2 節の表の下）。シンボリックリンク・Linux のアクセス権・フォルダーの書き込み権限・失敗する時点の違い
5. **依頼の 4 コマンドの外への影響。** 継承した us-ascii を UTF-8 とみなす処理を `EncodingInheritance.FromFile` に置いたので、`Convert-ProbedContent -EncodingFrom` の参照ファイルが us-ascii の場合も変換先が UTF-8 になる（第四次は us-ascii で、日本語は `UnrepresentableCharacter`）。CHANGELOG に書いた
6. **対になっていないサロゲート**も `UnrepresentableCharacter` になる（第四次は `?` や U+FFFD のバイト列で書いていた）。CHANGELOG の「表現できない文字」に含まれるものとして、個別には書いていない
7. **追補 4.1 の限界の入力への追記。** 英数字と「上」だけの BOM 無し UTF-16 など（全体の判定が us-ascii）に、`-DetectionLineCount` なしで追記すると、
   継承した us-ascii を UTF-8 とみなすので、日本語は UTF-8 で書く（第四次は `?`）。どちらも UTF-16 のファイルを壊す既存の限界で、`-DetectionLineCount` を指定すれば正しく書く
8. 新たに気づいた製品の不具合は無い。テスト担当のスクリプト（BUG-0009）の表示の見かけは 9.8 節の表に書いた
9. リポジトリの配布フォルダー（`publish/`）は作り直していない（依頼のとおりリリースしない）。`publish/.../core/SnowStack.EncodingProbe.PowerShell.deps.json` の変更（モジュールが同梱するクラスライブラリを 1.3.0 にしたもの）は、前のセッションから未コミットのまま残っている

### 9.10 直した文書

| 文書 | 内容 |
|---|---|
| `docs/EncodingProbe-1.3.0-仕様書.md` | 2.4 を新しい規則に書き直し、経緯と既知の限界の表を追加。3.2 の「LineBreak は読み替えない」を改め、ISO-2022 の検査を追加。4.3 に ISO-2022 の失敗を追加。16.1 に継承した us-ascii、16.4（表現できない文字）を新設。18.2 に空の列名、20 章の表に 2 行 |
| `docs/EncodingProbe-1.1.0-仕様書.md` | 6.1 の表と注（us-ascii に UTF-8 → 許可、表現できない文字、経緯）、8 章の表に 1 行 |
| MAML（5 言語、`zh-TW` を `zh-HK` / `zh-MO` に複製） | 書き込みの 4 コマンドの注意事項に 2 項目（表現できない文字・継承した us-ascii）、`Import-` / `Export-ProbedCsv` / `Resolve-Encoding` の `-DetectionLineCount`、`Export-ProbedCsv` の `-Force` |
| `README.md`・`CHANGELOG.md` | 1.3.0 の節（改行の判定の修正、挙動の変更、`-DetectionLineCount` の既知の限界） |
| `SnowStack.EncodingProbe.csproj` の `PackageReleaseNotes`・`.psd1` の `ReleaseNotes` | 第五次の内容を追加 |
| `docs/EncodingProbe-1.3.0-調査-切り出しの境目.md` | 調査ブランチからそのまま持ってきた（変えていない） |
| `CLAUDE.md` | 切り出しの調査の結論、調査ブランチをマージしないこと、第五次の作業記録 |
