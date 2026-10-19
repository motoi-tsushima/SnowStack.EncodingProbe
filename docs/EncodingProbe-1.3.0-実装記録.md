# EncodingProbe.PowerShell 1.3.0 — 実装記録

- 対象: `Resolve-Encoding -DetectionLineCount`、`Import-ProbedCsv`、`Export-ProbedCsv` と共通の部品（`docs/EncodingProbe-1.3.0-仕様書.md`）
- 作成日: 2026-10-19
- 目的: 実装の過程で、仕様書・依頼・当初の想定と異なる結果になった点と、
  仕様書に書かれていなかったため実装側で決めた点を残す（1.2.0 の実装記録と同じ形式）
- 章の構成: 第一次（共通の部品と `Resolve-Encoding -DetectionLineCount`）は 1〜5 章。第二次・第三次は後ろに追記する

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

- `Resolve-Encoding` は `20127 / us-ascii` を返した
- `Get-ProbedContent` は `U+0000` の混じった行を返した

### 5.6 クラスライブラリ

`git diff master -- SnowStack.EncodingProbe/` は空である（クラスライブラリは変更していない）。
