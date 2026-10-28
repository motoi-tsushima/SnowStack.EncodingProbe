# EncodingProbe.PowerShell 1.3.0 — 実装記録

- 対象: `Resolve-Encoding -DetectionLineCount`、`Import-ProbedCsv`、`Export-ProbedCsv` と共通の部品（`docs/EncodingProbe-1.3.0-仕様書.md`）
- 作成日: 2026-10-19
- 目的: 実装の過程で、仕様書・依頼・当初の想定と異なる結果になった点と、
  仕様書に書かれていなかったため実装側で決めた点を残す（1.2.0 の実装記録と同じ形式）
- 章の構成: 第一次（共通の部品と `Resolve-Encoding -DetectionLineCount`）は 1〜5 章、第二次（`Import-ProbedCsv`）は 6 章、第三次（`Export-ProbedCsv` と仕上げ）は 7 章、
  第四次（クラスライブラリの `0x00` を含む入力の判定の修正。`docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`）は 8 章、
  第五次の前半（書き込みで文字を失う件・切り出し・改行の判定・ISO-2022・CSV の追記。テスト担当のブラックボックステストの不具合）は 9 章、
  第五次の後半（パスの束縛・同じファイルの判定・リンクと権限・追記の取り消し・検証の部品・文書）は 10 章

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

## 10. 第五次（後半） — パスの束縛・同じファイルの判定・リンクと権限・追記の取り消し・検証の部品・文書（2026-10-23）

テスト担当のブラックボックステストの残り（BUG-0003〜0008・0011・0015〜0017）と H-17（`Convert-ProbedContent` がハードリンクを切る件）、
前半の報告で判断を求めた 9.9 節の 1〜4 を片付けた。

### 10.1 利用者の決定

| 事項 | 決定 |
|---|---|
| 別名 `FullName` を `-Path` から `-LiteralPath` に移す・BUG-0006 / 0007 のエラー ID を変える | 認める（まだ広く使われていないため。依頼 0.4） |
| BUG-0011 | 挙動を変えず、ヘルプを直す |
| `Resolve-Encoding -DetectionLineCount` の `LineBreak`（9.9 節の 1） | 今のまま。`us-ascii` と報告する以上、改行も `us-ascii` として数える。ヘルプに一言書く |
| `EncodingChangeOnAppend`（9.9 節の 2） | ファイルを元に戻す（10.5 節） |
| パイプラインの停止 Ctrl+C（9.9 節の 3） | 今のまま、それまでに書いた内容を残す |
| 一時ファイルで置き換える方法（9.9 節の 4） | 残し、リンクと権限の場合だけ書き方を切り替える（10.6 節） |
| `::$DATA`（依頼 4 章 6.。計画時の質問） | **主ストリームとして扱う**（`a.txt::$DATA` は `a.txt`。ほかのストリームは両ホストで `AlternateDataStreamNotSupported`） |
| `-Culture` の範囲（依頼 5 章 3.。計画時の質問） | **独自の書式で決める**（BCP 47 の形。`C`・`root` は両ホストで `InvalidCulture`） |
| リンク切れのシンボリックリンク（計画時の質問） | **各コマンドを標準に合わせる**（`Set-ProbedContent` は書かずに `LinkTargetNotFound`、`Out-ProbedFile`・`Export-ProbedCsv`・追記はリンクの先に作る） |
| BUG-0008 (5) `Os.Description` の末尾の空白（計画時の質問） | **コアで Trim する** |

### 10.2 標準の実測（依頼 1 章 2.・3 章 1.・4 章 5.）

PowerShell 7.6.7 と Windows PowerShell 5.1.26100.9711、Windows 11 Pro 10.0.26300、ja-JP。逆コンパイルはしていない。使い捨てのスクリプトで測った（リポジトリには入れていない）。

**ワイルドカードが 0 件（依頼 1 章 2.）。** 両ホストで同じ。

| 操作 | 結果 |
|---|---|
| `Set-Content -Path 'c[1].txt'`・`'x*.txt'` | 非終了エラー `ItemNotFound`（ObjectNotFound）。ファイルは作らない |
| `Add-Content -Path 'c[1].txt'`・`'x*.txt'` | 同上 |
| 参考: ワイルドカード文字を含まない存在しないパス | 作る |

→ `Set-` / `Add-ProbedContent` も同じ ID・分類の非終了エラーにした。メッセージは新しいキー `WildcardMatchedNoFileForWrite`（「一致するファイルが無いため、書き込みませんでした。…-LiteralPath を使って」）。
`Out-ProbedFile` / `Export-ProbedCsv` は終了エラー `FileNotFound` のまま（1.2.0 仕様書 2.12。出力先がちょうど 1 件の規則）。メッセージは「一致するファイルがありません」で始まり、
`Out-ProbedFile` 側は「ちょうど 1 件に一致する必要がある」、`Set-` / `Add-` 側は「書き込みませんでした」と、それぞれの規則を書いていて、食い違いはない。

**同じパスを 2 回（依頼 4 章 5.）。**

| 操作 | 7.6.7 | 5.1 |
|---|---|---|
| `Add-Content -LiteralPath a.txt, a.txt -Value Z` | 1 回だけ追記、エラーなし | 1 回追記し、2 件目で `GetContentWriterIOError` |
| `-LiteralPath a.txt, A.TXT` | 同上 | 同上 |
| `-Path a.txt, a*` | 同上 | 同上 |
| `'p','q' \| Add-Content -LiteralPath a.txt, a.txt` | `p`・`q` を 1 回ずつ | 同左、2 件目で `GetContentWriterIOError` |
| `(gi a.txt), (gi a.txt) \| Add-Content -Value Z` | **2 回追記**（レコードごと） | 同左 |
| `Set-Content -LiteralPath a.txt, a.txt -Value Z` | `Z` 1 回 | `Z` 1 回、2 件目で `GetContentWriterIOError` |

→ `Add-ProbedContent` は、1 回の指定（1 レコード）の中の重複は 1 回だけ追記し、エラーにしない（7.x に合わせた。5.1 のエラーは標準の内部の事情で、利用者に意味が無い）。
パイプラインのレコードをまたぐ場合は、レコードごとに追記する（両ホストの標準と同じ）。
`Set-ProbedContent` は依頼どおり「1 回書いたのと同じ」。パスをパイプラインから受け取る場合（`BeginProcessing` の時点で `-Path` も `-LiteralPath` も束縛されていない）は、
前のレコードで書いた内容を取り消して書き直す（標準の `Set-Content` がレコードごとに切り詰め直すのと同じ結果）。値をパイプラインから受け取る場合（`'p','q' | Set-ProbedContent a.txt`）は、これまでどおり続けて書く。

**リンク切れのシンボリックリンク（依頼 3 章 1.）。** `mklink` で作った（5.1 の `New-Item` はリンク切れのリンクを作れない）。両ホストで同じ。

| 操作 | 結果 |
|---|---|
| `Set-Content` にリンク切れ（絶対パス・相対パス・2 段） | 生の `FileNotFoundException` の終了エラー。何も作らない |
| 同上でリンク先のフォルダーも無い | 生の `DirectoryNotFoundException` |
| `Add-Content` / `Out-File` / `Export-Csv` にリンク切れ（相対パス） | リンクの先にファイルを作って書く。リンクは残る |
| `Add-Content` でリンク先のフォルダーが無い | 非終了エラー `GetContentWriterDirectoryNotFoundError` |
| `Out-File` でリンク先のフォルダーが無い | `FileOpenFailure` |
| 参考: リンクが 2 段で、実体が存在する `Set-Content` | 実体に書く。リンクは残る |

→ 利用者の決定で、各コマンドを対応する標準に合わせた。`Set-ProbedContent` は生の例外ではなく、非終了エラー `LinkTargetNotFound`（ObjectNotFound、新しいキー `LinkTargetNotFound`）。
リンク先のフォルダーが無い場合は、既存の `WriteFailed`（Set / Add）・終了エラー `WriteFailed`（Out / Export）のまま。

**`::$DATA`（依頼 4 章 6.）。** 標準の `Get-Content` / `Set-Content -LiteralPath a.txt::$DATA` は、両ホストで主ストリームを読み書きする。
.NET の `Path.GetFullPath` は、7.x では受け付け、5.1（.NET Framework）では `NotSupportedException` を投げる（本モジュールの 5.1 の生の例外の原因）。
`Convert-Path` は両ホストで受け付ける。

**カルチャー名（依頼 5 章 3.）。** `new CultureInfo(name)` の可否を 100 個ほどの名前で比べた。

| 名前 | net48（NLS） | net10.0（ICU） |
|---|---|---|
| `C`・`c`・`root`・`ROOT` | 拒否 | 受付（Name は空、LCID 127 / 4096） |
| `art-lojban`・`zh-guoyu`・`zh-hakka`・`zh-min-nan`・`zh-xiang`・`zh-yue` | 拒否 | 受付（`jbo`・`zh`・`hak` などに正規化） |
| `und-u-ca-gregory` | 受付 | 拒否 |
| ほかの名前（`xx-YY`・`ko_KR`・`i-klingon`・`x-test`・`qps-ploc`・`POSIX`・`en-u` など） | 両ホストで同じ | 同左 |

差が両方向にあるので、どちらの .NET に合わせても一致は保証できない。コアの `EncodingProbe.ValidateCulture` も `CultureInfo` に頼っていた。

**FileInfo の束縛（依頼 1 章 1.）。** 高度な関数で `-Path`（ByValue・ByPropertyName、別名 `FilePath`）と `-LiteralPath`（ByPropertyName、別名 `PSPath`・`LP`・`FullName`）を作って両ホストで確かめた（後で製品の DLL でも確かめた）。
`Get-Item` / `Get-ChildItem` の FileInfo は `-LiteralPath` に `PSPath` で、`New-Object System.IO.FileInfo` は `FullName` で束縛される。文字列は `-Path` に入る。`DirectoryInfo` も `-LiteralPath`。
両ホストで同じ。1 つのパラメーターが `PSPath` と `FullName` の両方の別名を持っても問題ない。

### 10.3 パスの束縛（BUG-0003・0015）

- `Get-ProbedContent`・`Import-ProbedCsv`・`Set-ProbedContent`・`Add-ProbedContent`: `-Path` の別名を `FilePath` だけにし、`-LiteralPath` の別名を `PSPath`・`LP`・`FullName` にした
- `Convert-ProbedContent`: `-LiteralPath` に `FullName` を足した（`New-Object FileInfo` は `PSPath` を持たず `InputObjectNotBound` だった。標準の `Get-Content` も同じだが、ほかの 4 コマンドとそろえた）。
  `-Path` に `ValueFromPipeline` / `ValueFromPipelineByPropertyName` と別名 `FilePath` を足した（文字列をパイプラインから受け取る。ほかのコマンドとそろえた）
- `Resolve-Encoding`: 変えていない。`-Path` はもともとワイルドカードを展開しない（`GetUnresolvedProviderPathFromPSPath`）ので、`FullName` で束縛されても正しいファイルを判定する。
  `-LiteralPath` を足すのは既存のパラメーターの形を変えるので、依頼の範囲外とした
- MAML の `aliases` 属性・`pipelineInput` 属性も 5 言語でそろえた
- 書き込み側の 0 件のワイルドカード: `ProbedContentCommandBase.ResolveWritablePaths` で、解決した結果が 0 件なら `ItemNotFound`。
  ワイルドカード文字を含まない存在しないパスは、解決に失敗して「これから作るファイル」として 1 件返るので、0 件になるのはワイルドカードが一致しなかった場合だけ

### 10.4 同じファイルの判定（BUG-0004・0016・0017）

**実体の求め方（`Internal/FileIdentity`）。**

- Windows: `CreateFileW`（`FILE_READ_ATTRIBUTES`、共有はすべて、`FILE_FLAG_BACKUP_SEMANTICS`）で開き、`GetFileInformationByHandle` のボリュームのシリアル番号とファイル ID（64 ビット）を鍵にする。
  リンク数（`nNumberOfLinks`）もここで求める。リンクを最後までたどった実体のパスは `GetFinalPathNameByHandle`。net48 と net10.0 で同じ P/Invoke。
  属性の読み取りだけで開くので、ほかのプロセスが共有なしで開いているファイルでも求められる。長いパスは `\\?\` を付けて渡す（5.1 のホストは長いパスに対応していないことがある）
- Linux / macOS（net10.0 だけ）: パスを 1 要素ずつ `FileSystemInfo.LinkTarget` で解決したフルパスを鍵にする（realpath 相当。最後の要素は存在しなくてよい）。ハードリンクは別のファイルとみなす（既知の限界）
- まだ無いファイル: 「親フォルダーの実体＋名前」（`LocationKey`）で比べる。存在するファイルも `LocationKey` を持ち、実体の鍵か `LocationKey` のどちらかが一致すれば同じとする（`Matches`）
- BUG-0004 の書き方（`\\?\`・`\\?\UNC\`・`\\localhost\C$`・`\\.\`・subst・シンボリックリンク・ジャンクション・ハードリンク・大文字小文字・`::$DATA`）をすべて同じ実体と判定することを、単体テスト（subst を除く）・PSCompat・テスト担当のスクリプト（subst を含む）で確かめた
- 既知の限界: ReFS の 128 ビットのファイル ID は扱わない（`GetFileInformationByHandle` の 64 ビットの値で比べる。依頼の指定どおり）

**比べる時点。**

| 書き方 | 時点 |
|---|---|
| 上書き（置き換え・写してから直接） | 一時ファイルに書き始める前（コマンドの検査と、`ProbedFileWriter.Open` の中の 2 か所。どちらもファイルに触れる前） |
| 追記 | 切り詰めずに開く → 開いたハンドルで実体を求めて比べる → 書く（`ProbedFileWriter.Open`）。一致したら何も書かずに閉じて `SamePathRoundTrip` |
| `Convert-ProbedContent` | 変換元を読む前（N10）と、`-Destination` の N8・N9 の検査（`ShouldProcess` より前） |

`ActiveReadRegistry` は、読み込むファイルの実体（`FileIdentity`）を登録し、実体で比べる。読み取り中のファイルが無いときは実体を求めない（余分にファイルを開かない）。

**書き込み先の表（`ProbedContentWriterCommandBase`）。** パスの文字列の辞書をやめ、実体の鍵と `LocationKey` の 2 つの索引を持つ表にした。追記で新しく作ったファイルは、開いた後に実体の鍵でも引けるようにする。

**BUG-0004 の `Add-ProbedContent` が終わらない件。** 開く前の検査で `SamePathRoundTrip` になり、追記を始めないので止まる（テスト担当のスクリプトで、`\\?\`・シンボリックリンクとも `finished error=SamePathRoundTrip`、長さは変わらない）。

**BUG-0017。** `Convert-ProbedContent -Destination` の出力先（フォルダー＋名前）と変換元を `FileIdentity.Matches` で比べる。出力先が変換元そのものなら、実体の鍵が一致する。
N8（同じ実行で書く予定のファイル）も実体の表で比べる。`-Force` の有無によらず N9 で止まり、変換元は変わらない。

### 10.5 `EncodingChangeOnAppend` でもファイルを元に戻す（依頼 2 章）

`ProbedContentWriterCommandBase.WriteValues`（`Add-ProbedContent`）と `ProbedFileOutputCommandBase.WriteChunk`（`Out-ProbedFile -Append`・`Export-ProbedCsv -Append`）で、
`EncodingChangeOnAppend` のときに、それまでの「閉じる（書いた行を残す）」をやめ、表現できない文字と同じ `Rollback` にした。どちらも**元に戻してから**エラーを報告する（`-ErrorAction Stop` の対策）。

- `Add-ProbedContent` は前のレコードで書いた分も取り消し、そのファイルには以後書かない。ほかのファイルは続ける（非終了エラー）
- 追記先が無かった場合（`-AllowEncodingChange` なしでは整合性検査の相手が無いので、実際には起きない）は、作ったファイルを消す
- 1.1.0 仕様書 6.4（新設）・8 章、1.2.0 仕様書 2.8、1.3.0 仕様書 16.4 を改めた。既存のテスト `OutProbedFileTests.Append_Mismatch_StopsBeforeTheLineAndKeepsEarlierLines` は、期待値を「元に戻す」に改めて名前を変えた

### 10.6 書き込みのリンク・権限の扱い（依頼 3 章、H-17）

上書きの部品を `Internal/SafeFileOverwrite` に切り出し、`ProbedFileWriter`（`Set-ProbedContent`・`Out-ProbedFile`・`Export-ProbedCsv`）と `Convert-ProbedContent` の両方が使う。

| 書き込み先 | 書き方 |
|---|---|
| まだ無い | 置き換え（同じフォルダーの一時ファイルに書き、完了したときに移動）。一時ファイルを作れなければ（権限）直接作る |
| ふつうのファイル | 置き換え（`File.Replace`）。置き換える前の属性を付け直す（読み取り専用は `ReadOnlyAttributeScope` が戻す） |
| シンボリックリンク・ジャンクション経由（何段でも、相対パスでも） | `GetFinalPathNameByHandle` で求めた実体のフォルダーに一時ファイルを作り、実体を置き換える。リンクはそのまま |
| ハードリンク（リンク数 2 以上） | 写してから直接書く: 元のファイルを `Path.GetTempPath()` の `SnowStack.EncodingProbe.<8 桁>.probedbak` に写し、元のファイルを開いて長さ 0 にしてから書く。成功したら写しを消し、失敗したら写しから書き戻す |
| 一時ファイルを作るときに `UnauthorizedAccessException` | 写してから直接書く（同上） |
| リンク切れのシンボリックリンク | `Set-ProbedContent` は `LinkTargetNotFound`。ほかは `FileMode.Create` で開いてリンクの先に作る（失敗したら、ハンドルから求めた実体のパスを消す） |

- 追記（`Add-ProbedContent`・`-Append`）は、もともと追記先を直接開く（`FileMode.Append` はリンクをたどる）。開く前の長さは、リンクの長さではなく、開いたストリームの長さにした
  （`FileInfo` をリンクに対して使うとリンク自身の長さになり、元に戻すときに実体を切り詰めてしまうため）。元に戻すときに消すのも、リンクではなく作った実体
- 読み取り専用の検査・`-Force` で外す属性は、リンクの先の実体に対して行う（`ReadOnlyAttributeScope` が `FileIdentity.ResolveFinalPath` を通す）
- Linux / macOS: 置き換える前に `File.GetUnixFileMode` / `SetUnixFileMode` でアクセス権を一時ファイルに写す。所有者は写せない。ハードリンクは切れる（既知の限界）。**この環境では実行していない**
- `Convert-ProbedContent` の一時ファイルの名前は `.<名前>.<GUID>.tmp` から `.<名前>.<8 桁>.probedtmp` になり、`TemporaryFileRegistry` に載る（ワイルドカードの解決から除かれる）。CHANGELOG に書いた

**保たれるもの（依頼 3 章 6.）。** 隠し＋読み取り専用の属性、作成日時（2001-02-03）、代替データストリーム（`:meta`）、ACL（Everyone に ReadData を足したもの）を付けたファイルに、`-Force` で書いて確かめた。
Windows 11（NTFS）、両ホストで同じ結果。

| 書き方 | 確かめたコマンド | 属性 | 作成日時 | 代替データストリーム | ACL |
|---|---|---|---|---|---|
| 置き換え | `Set-ProbedContent`・`Out-ProbedFile`・`Convert-ProbedContent` | 保たれる | 保たれる | 保たれる | 保たれる |
| 写してから直接 | `Set-ProbedContent`・`Convert-ProbedContent`（ハードリンク）、失敗して書き戻した場合 | 保たれる | 保たれる | 保たれる | 保たれる |
| 追記 | `Add-ProbedContent`、失敗して切り詰めた場合 | 保たれる | 保たれる | 保たれる | 保たれる |

（置き換えは `ReplaceFile` が作成日時・代替データストリーム・ACL を引き継ぐ。写してから直接と追記は、元のファイルそのものに書くので変わらない。）
既知の限界として書くもの: Linux / macOS の所有者とハードリンク。ReFS など NTFS 以外のファイルシステムは確かめていない。

**ほかのプロセスが開いているとき（依頼 3 章 7.）。** 置き換えは、失敗するのが開く時点ではなく完了の時点になる（`Set-` / `Add-` は `EndProcessing` で非終了エラー `WriteFailed`、`Out-` / `Export-` は終了エラー）。
元のファイルは変わらず、一時ファイルも残らない（テスト `OpenedByAnotherHandle_FailsAtCompletion`）。ヘルプ（注意事項）と 1.3.0 仕様書 16.5 に書いた。
写してから直接書く方法の、大きなファイルで写す時間がかかる点（依頼 3 章 8.）も、ヘルプと仕様書に書いた。

**フォルダーにファイルを作れない場合（依頼 3 章 3.）。** フォルダーに「ファイルの作成（WD）」の拒否を付けたフォルダーで、両ホストで、`Set-ProbedContent`・`Out-ProbedFile`・`Convert-ProbedContent` が書けること、
表現できない文字で失敗したときに元に戻ること、新しいファイルは作れずに `WriteAccessDenied` になること、写しが残らないことを確かめた。

### 10.7 `::$DATA` と代替データストリーム（依頼 4 章 6.）

**案と決定。** 案 1（推奨・採用）: 主ストリームの名前は両ホストでそのファイルとして扱い、ほかのストリームは両ホストで ID の付いたエラーにする。案 2: ストリームの指定はすべて拒否する（7.x で今動いているものが動かなくなる）。

- `Internal/FileSystemPath.TryGetFullPath` が、パスの最後の要素の `::$DATA`（大文字小文字は問わない）を取り除いてから `GetFullPath` を呼ぶ。最後の要素にそれ以外の `:` があれば代替データストリームとして false を返す（Windows のみ。Linux / macOS では `:` は名前の一部）
- `GetFullPath` を直接呼んでいた箇所（`ProbedContentCommandBase` のパスの解決・`-EncodingFrom`、`ProbedFileOutputCommandBase` の出力先、`Convert-ProbedContent` の変換元と `-Destination`、`Resolve-Encoding`、`TemporaryFileRegistry`）をすべて通した
- エラー ID は `AlternateDataStreamNotSupported`（InvalidArgument、新しいキー）。ファイルごとの指定は非終了エラー、出力先が 1 件のコマンド・`Resolve-Encoding`・`-EncodingFrom`・`-Destination` は終了エラー
- 同じファイルの判定では、`::$DATA` を取り除いたパスで実体を求めるので、`a.txt::$DATA` への書き込みも `SamePathRoundTrip` になる

### 10.8 `-Culture` の検証（BUG-0006・0007。依頼 5 章 2.・3.）

**案と決定。** 案 1（推奨・採用）: `CultureInfo` に頼らず、名前の形（BCP 47）で受け付けるかを決める。案 2: 今の検査に加えて `C` と `root` だけを拒否する（`art-lojban`・`und-u-ca-gregory` などの差が残る）。

**規則（`CultureNameSubtags.IsWellFormed`。クラスライブラリ）。** 区切りは `-` と `_`、各要素は英数字 1〜8 文字。言語は英字 2〜3 文字（拡張言語 3 文字を 3 つまで）、用字 4 文字、地域 2 文字か数字 3 文字、
変種 5〜8 文字か数字で始まる 4 文字、拡張（`x` 以外の 1 文字＋2〜8 文字）、私用（`x`＋1〜8 文字）。`x-…`（全体が私用）と `i-…`、RFC 5646 の irregular な grandfathered タグ 17 個も受け付ける。

**計画と違えた点（報告で確認を求める）。** 計画では「コアには、分解したサブタグから組み立て直した名前を渡す」としたが、コアの判定結果 `EncodingInformation.Culture` には渡した名前がそのまま入り、
`Resolve-Encoding` の出力に見える（`-Culture ko_KR` が `ko-KR` になってしまう。既存の戻り値を変えない制約に触れる）。そのため、**コアの `EncodingProbe.ValidateCulture` 自体を同じ規則に改めた**。
クラスライブラリの公開 API（`Detect` の引数と戻り値）は変えていないが、受け付ける名前の集合が変わる（net48 と net10.0 で同じになる）。CHANGELOG のクラスライブラリの節に書いた。
モジュールは先に同じ規則で検証し、本モジュールの ID とメッセージにする（`ResolveEncodingOptions.ValidateCulture`。`Resolve-Encoding` とほかのコマンドの共通の部品）。

| 名前 | 1.2.0 まで（5.1 / 7.x） | 第五次の後半 |
|---|---|---|
| `C`・`c`・`root` | 拒否 / 判定に進む | 両ホストで `InvalidCulture` |
| `art-lojban`・`zh-min-nan`・`zh-yue`・`zh-guoyu` | 拒否 / 受付 | 両ホストで受付 |
| `und-u-ca-gregory` | 受付 / 拒否 | 両ホストで受付 |
| `c-JP`・`ja-JP-x`・`ja-JP-JP`・`ja-JP_x` | 受付 / 受付 | **両ホストで拒否**（BCP 47 の形でない。利用者の決定の範囲で、両ホストとも変わる） |
| `cel-gaulish`・`no-bok`・`zh-min` | 拒否 / 拒否 | **両ホストで受付**（BCP 47 の形をしている） |
| `xx-YY`・`ko_KR`・`i-klingon`・`x-test`・`POSIX`（拒否）・`en-u`（拒否）など | 同じ | 変わらない |

- 受け付けた名前の判定は従来どおりサブタグで行う（`ResolveEastAsianLegacyRegion`）。受け付けた名前で `CultureInfo` を作る処理は、コアにもモジュールにも無い（10.14 節で確かめた）
- **拡張言語（言語サブタグの直後の英字 3 文字）は、それを言語として扱う**（報告の後の修正。10.14 節）。RFC 5646 では `zh-yue` = `yue`、`zh-cmn` = `cmn`。
  `zh-yue` は `yue`（広東語。繁体字・香港）になる。表は増やしていないので、`cmn`・`min` などは表に無い言語（`None`）。旧形式の `zh-CHT` / `zh-CHS` は用字として先に扱う
- `Resolve-Encoding` の `-Strategy` / `-Culture` は、`ResolveEncodingOptions.ValidateStrategy` / `ValidateCulture` で検証し、ほかのコマンドと同じ ID・分類（InvalidArgument）・例外の型（`PSArgumentException`）・5 言語のメッセージの終了エラーにした（BUG-0006）。
  使われなくなった `ResolveEncodingOptions.ParseStrategy`（生の `ArgumentException` を投げていた）は消した
- `PlatformInfoResolver` で `OSDescription` を Trim した（BUG-0008 (5)。両ホストで `Microsoft Windows 10.0.26300`）

### 10.9 文書（依頼 5 章 1.・4.〜6.）

| BUG | 直したもの |
|---|---|
| 0005 | 「両ホストでバイト単位で同じ」に、日付・数値の書式はカルチャーとホストの .NET（NLS / ICU）で違うことがある、という条件を足した: README、CHANGELOG（`Export-ProbedCsv` の節）、1.3.0 仕様書 1.5・14 章、`Export-ProbedCsv` のヘルプの説明（5 言語）、`.psd1` のリリースノート |
| 0008 | (1) CHANGELOG の `>>>>` の行を消した。(3) `ConvertTo-DotNetEncoding` の例 2 を `[System.IO.File]::ReadAllText((Convert-Path -LiteralPath .\a.txt), $enc)` にした（5 言語）。(4) README の「推測処理ょ」の誤字。(5) `Os.Description` の Trim（10.8 節）。(2) はブログの記述で、リポジトリには無いので直していない |
| 0011 | `Add-ProbedContent` の `-AllowEncodingChange` の「判定自体を行わない」を「`-Encoding` を指定した場合は」に限り、省略した場合は継承のために判定する（判定できなければ `EncodingDetectionFailed`）ことを書いた。`Out-ProbedFile` / `Export-ProbedCsv` の `-AllowEncodingChange` にも同じことを足した（5 言語）。1.1.0 仕様書 6.2 に注記 |
| — | `Resolve-Encoding -DetectionLineCount` のヘルプに、`us-ascii` と報告する場合は `LineBreak` も `us-ascii` として数えた値であることを書いた（5 言語）。1.3.0 仕様書 3.1 |

ヘルプにはほかに、`-LiteralPath` の FileInfo の束縛、`-Path` の 0 件のワイルドカード（Set / Add）、`-Culture` の受け付ける名前（8 コマンド）、`Resolve-Encoding -Strategy` のエラー、
注意事項として上書きの書き方（リンク・ハードリンク・権限・保たれるもの・Linux）、追記の書き方と取り消し、ほかのプロセスが開いているとき、リンク切れ、同じファイルの判定、同じファイルの重複（Set / Add）、
代替データストリームを足した（5 言語、`zh-TW` を `zh-HK` / `zh-MO` に複製。`MamlHelpTests` の骨格の一致を通した）。

### 10.10 テスト

| テスト | 内容 |
|---|---|
| コア `DetectorTests/CultureNameValidationTests`（59） | 受け付けない名前・受け付ける名前（10.2 節の表の旧 net48 / 旧 net10.0 の差を含む）、例外に内部例外が無いこと、`Os.Description` の Trim。net48 と net10.0 の両方で通す |
| `FileSystemTests/FileIdentityTests`（18） | 大文字小文字・`\\?\`・`\\.\`・管理共有・`\\?\UNC\`・相対パスと 2 段のシンボリックリンク・リンク切れ・ジャンクション・フォルダーのシンボリックリンク・ハードリンクとリンク数・まだ無いファイル・別のファイル・`::$DATA` の正規化・代替データストリームの拒否・`ActiveReadRegistry` |
| `CmdletTests/PathBindingTests`（21） | 5 コマンドの別名、`Resolve-Encoding` の別名、`Get-Item` / `New-Object FileInfo` / `Get-ChildItem` からの読み書き、`Convert-ProbedContent` のパイプライン、文字列はワイルドカード、0 件のワイルドカード（Set / Add は `ItemNotFound`、読み込みはエラーにしない、ほかのパスは書く） |
| `CmdletTests/SameFileTests`（55） | 8 つの書き方 × 4 つの書き込み（`SamePathRoundTrip`、ファイルは変わらない）、CSV、Set / Add の重複、まだ無いファイルの 2 つの書き方、`Convert-ProbedContent -Destination` の 6 つの書き方 × `-Force` の有無（N9）、N8、`::$DATA` と代替データストリーム（8 コマンド） |
| `CmdletTests/LinkAndPermissionWriteTests`（22） | 5 つの書き込み × シンボリックリンク（相対・2 段）とハードリンク、ハードリンクの書き戻し、リンク切れ（4 コマンド）、リンクの先の読み取り専用、フォルダーにファイルを作れない場合、3 つの書き方で保たれるもの、ほかのハンドルが開いているとき |
| `CmdletTests/AppendRollbackTests`（4） | `EncodingChangeOnAppend` の取り消し（Add の複数ファイル、`-ErrorAction Stop` の 3 コマンド） |
| `CmdletTests/CultureValidationTests`（19） | 7 つの受け付けない名前 × 5 コマンド（`Resolve-Encoding` を含む）、11 の受け付ける名前（`Culture` にそのままの名前が出る）、`Resolve-Encoding -Strategy` |
| 改めた既存のテスト | `ImportProbedCsvTests.Metadata_ParameterSetsAndBinding`（別名）、`ResolveEncodingCmdletTests.Invoke_WithInvalidStrategy_ThrowsCmdletInvocationException`（ID）、`OutProbedFileTests.Append_Mismatch_StopsAndRestoresTheFile`（10.5 節） |
| PSCompat | `Fifth2/` の 117 シナリオ（束縛・0 件のワイルドカード・同じファイルの 7 つの書き方 × 4・CSV・重複・`-Destination` の 5 つの書き方 × `-Force`・リンク・リンク切れ・ハードリンクの書き戻し・追記の取り消し・ストリーム・カルチャー 17 個 × 2・`-Strategy`・`Os.Description`）。リンクは `mklink` で作る |

リンクを作るテストは、シンボリックリンクを作れない環境（開発者モードでも管理者でもない）では何も確かめずに終わる（xUnit 2 に実行時のスキップが無いため。`Helpers/FileLinks`）。この環境では作れる（開発者モード）。

### 10.11 テストと確認の結果（2026-10-23）

| 確認 | 結果 |
|---|---|
| コアのテスト | net48 **2090 件**、net10.0 **2080 件**、すべて成功（前半から各 59 件増） |
| モジュールのテスト | net10.0 **1431 件**、すべて成功（前半の 1292 件から 139 件増） |
| PSCompat | **948 シナリオ**で 5.1 と 7.x の結果が完全に一致（前半の 831 から 117 件増）。`Fifth2/` の値も 5.1 のレポートで期待どおりなことを確かめた |
| テスト担当のスクリプト | 作業フォルダー（scratchpad）に Release ビルドで配布フォルダーと同じ構成を作り、`scripts\` の写しを 7.6.7 と 5.1 で実行した（テスト担当のフォルダーには何も置いていない。CHANGELOG・README は写しを `docs\repo\` に置いた）。下の表 |

| BUG | 結果（7.6.7・5.1。両ホストで同じ） |
|---|---|
| 0003 | 4 コマンドとも流したファイルそのもの（`A,x`、`b[1].txt` に追記・上書き、`b1.txt` は変わらない）。`a1.csv` / `b1.txt` が無くても同じ。`Get-ChildItem` の 2 ファイルは各 1 回（並び順だけ 5.1 が違う。`Get-ChildItem` の順序） |
| 0004 | 7 つの書き方（大文字・`\\?\`・管理共有・シンボリックリンク・ジャンクション・subst）× Set / Out / Export がすべて `SamePathRoundTrip`、長さは 12003 バイトのまま。`Add-ProbedContent` は 10 秒以内に終わり `SamePathRoundTrip` |
| 0005 | 書式の差は残る（文書で条件を書いた。挙動は変えていない） |
| 0006 | `Resolve-Encoding` も `InvalidStrategy` / `InvalidCulture`、InvalidArgument、`PSArgumentException`、本モジュールのメッセージ |
| 0007 | `C`・`root`・`POSIX` は両ホストで終了エラー `InvalidCulture`。`xx-YY` は両ホストで判定不能、`ko-KR` は 949 |
| 0008 | (1) `>>>>` 無し、(3) `[Microsoft Windows 10.0.26300]`（5.1 も末尾の空白が無い）、(5) 誤字無し。(4) はスクリプトが相対パスの危うさそのものを示す（ヘルプの例は直した）、(2) はブログ |
| 0011 | 挙動は変えていない（ヘルプを直した） |
| 0015 | Set / Add とも `ItemNotFound`、ファイルを作らない（標準と同じ） |
| 0016 | 3 通りとも `5A 0D 0A`（標準と同じ） |
| 0017 | 5 つの書き方 × `-Force` の有無がすべて `DestinationIsSource`、`a.txt` は変わらない |
| 0001・0002・0009・0010・0012・0013・0014（前半） | 前半と同じ結果（後退なし）。0009 は 5.1 と 7.x のエラーの表示の形式（ホストの表示）だけが違う |

### 10.12 決めきれなかったこと・仕様と食い違ったこと・新たに気づいたこと

1. **コアのカルチャー名の検証を変えた**（10.8 節）。計画では「組み立て直した名前をコアに渡す」としていたが、`EncodingInformation.Culture` に出る名前が変わってしまうため、コアの検証を同じ規則にした。
   NuGet パッケージの利用者にとっては、`ArgumentException` になる名前の集合が変わる（net48 と net10.0 でそろう）。確認を求める
2. **両ホストで変わる名前がある**（10.8 節の表）。`c-JP`・`ja-JP-x`・`ja-JP-JP` は 1.2.0 まで両ホストで受け付けていたが、拒否する。`cel-gaulish` などは受け付ける。BCP 47 の形に合わせた結果
3. ~~`zh-yue`（広東語の拡張言語の形）は簡体字中国語として扱う~~ → **報告の後に改めた（10.14 節）:** 拡張言語を言語として扱う規則を足し、`zh-yue` は `yue`（香港）になった。表は増やしていない
4. **`Convert-ProbedContent -Path` が文字列をパイプラインから受け取るようになった**（そろえるために足した機能）。`-LiteralPath` の `FullName` も足した
5. **`Add-ProbedContent` の 1 レコードの中の重複は、5.1 でもエラーにしない**（5.1 の標準は 2 件目で `GetContentWriterIOError`）。7.x に合わせた
6. **`Set-ProbedContent` でパスをパイプラインから受け取り、同じ実体が後のレコードで現れた場合は、前のレコードの分を取り消して書き直す**（最後のレコードの内容が残る。標準と同じ結果）
7. **写してから直接書く方法で、写しから書き戻せなかった場合は、写しを一時フォルダーに残す。** ~~メッセージは出さない~~ → 報告の後に、エラーのメッセージに写しのパスを示すようにした（10.14 節）
8. **Linux / macOS の経路（リンクの解決・アクセス権の複写）は、この環境では実行していない**。コンパイルと Windows での動作だけを確かめた
9. **`Convert-ProbedContent -Destination` の出力先がリンク切れのシンボリックリンクの場合は、リンクの先に作る**（`-Force` が必要。N7 で `File.Exists` がリンクに対して真になるため）。対応する標準のコマンドが無いので、`Out-File` に合わせた
10. `FileIdentity` はパスごとにファイルを開くので、`Get-ChildItem -Recurse | Set-ProbedContent` のように多数のファイルを書くと、ファイルごとに数回の余分な `CreateFile` が増える。測っていない
11. 新たに気づいた製品の不具合は無い。測定の途中で、Bash の heredoc が `\\` を 1 つに縮めること、5.1 が BOM 無しのスクリプトの日本語のコメントで次の行を飲み込むこと、pwsh から起動した 5.1 では `PSModulePath` を引き継いで `Get-Acl` が読めないことに当たった（どれも測定の道具の問題で、製品とは関係ない）

### 10.13 直した文書

| 文書 | 内容 |
|---|---|
| `docs/EncodingProbe-1.1.0-仕様書.md` | 6.2 に `-AllowEncodingChange` の注記（BUG-0011）、6.4 を新設（拒否されたときのファイル・重複）、8 章の表に 4 行・1 行を改め、9 章の同一ファイルの往復に注記 |
| `docs/EncodingProbe-1.2.0-仕様書.md` | 2.8（不一致で元に戻す）、2.10（実体で比べる）、15.1（一時ファイルの名前と書き方の切り替え） |
| `docs/EncodingProbe-1.3.0-仕様書.md` | 1.5・14 章（書式の条件）、3.1（`LineBreak`）、9.1（FileInfo の束縛）、16.4（`EncodingChangeOnAppend`）、16.5 を新設（上書きの書き方・同じファイルの判定）、20 章の表 |
| MAML（5 言語、`zh-TW` を `zh-HK` / `zh-MO` に複製） | 10.9 節 |
| `README.md`・`CHANGELOG.md` | 1.3.0 の節（パス・同じファイル・リンクの挙動の変更、カルチャー名の検証、書式の条件、`>>>>`・誤字） |
| `SnowStack.EncodingProbe.csproj` の `PackageReleaseNotes`・`.psd1` の `ReleaseNotes` | 第五次の後半の内容を追加 |
| `CLAUDE.md` | 第五次の後半の作業記録、実体の判定・上書きの部品・カルチャー名の規則 |

### 10.14 報告の後の修正（2026-10-23）

利用者は、コアのカルチャー名の検証を BCP 47 の形で決めるように変えたこと（10.8 節）を認め、コミットの前に次を直すよう依頼した。

**1. クラスライブラリの変更の記述をそろえた。** 1.3.0 仕様書 1.1〜1.3 は「クラスライブラリの変更は追補 2 章の点だけ」としていたが、実際には、改行の数え直し（第五次の前半）・カルチャー名の検証・`Os.Description` の Trim も変わっている。
仕様書 1.1 の表、1.2（4 点を列挙し、公開 API（型・メソッド・プロパティ、引数と戻り値の型）は変えないこと、`ArgumentException` になる名前の集合が変わることを書いた）、1.3、
CHANGELOG（「挙動の変更（クラスライブラリ）」の節を新設し、カルチャー名の検証と `Os.Description` を「不具合の修正」から移した）、`SnowStack.EncodingProbe.csproj` の `PackageReleaseNotes` を改めた。
1.3 の「変更しないもの」に並んでいた既存のコマンドには、第五次で挙動を変えた点があることを注記した。

**2. 受け付けるようになった名前で `CultureInfo` を作っていないかを確かめた。** コアとモジュールのソースで、`CultureInfo` の作成（`new CultureInfo(...)`・`GetCultureInfo`・`CreateSpecificCulture`）を探した。
渡された名前から作る箇所は無い（第五次の後半で検証の 2 か所を消した後は、`CultureInfo.CurrentCulture` / `CurrentUICulture` / `InvariantCulture` だけ）。
`EncodingDetectorOptions.Culture` と `EncodingInformation.Culture` はただの文字列で、判定は `CultureNameSubtags.Parse` によるサブタグの分解だけを使う。テストを足した。

- コア `CultureNameValidationTests.NewlyAcceptedNames_DetectToTheEnd`（9 件）: `art-lojban`・`zh-min-nan`・`zh-guoyu`・`cel-gaulish`・`no-bok`・`zh-min`・`und-u-ca-gregory`（CP949 のファイル）と `zh-yue`・`zh-cmn`（香港の Big5）で、
  3 つの判定方式 × バイト列・ストリーム・ファイルの判定を最後まで通す。**net48 と net10.0 の両方で通した**（これが「両ホスト」の確認。モジュールの経路は下の PSCompat）
- モジュール `CultureValidationTests.Accepted_ReachesDetection`: 名前を 4 つ足し、`Get-ProbedContent`・`Import-ProbedCsv`・`Resolve-Encoding -DetectionLineCount`・`Set-ProbedContent -EncodingFrom`・`Convert-ProbedContent` を通す
- PSCompat `Fifth2/Culture`: 名前を 6 つ足し（`cel-gaulish`・`zh-min`・`no-bok`・`zh-cmn`・`zh-guoyu`・`zh-yue-HK`）、`Import-ProbedCsv`・`Convert-ProbedContent` のシナリオを足した。CP949 のファイルを両ホストで判定する

**3. 拡張言語を解釈する。** `CultureNameSubtags.Parse` で、言語サブタグの直後の英字 3 文字を拡張言語とみなし、それを言語として扱うようにした（RFC 5646: `zh-yue` = `yue`、`zh-cmn` = `cmn`）。
旧形式の `zh-CHT` / `zh-CHS` は、従来どおり先に用字として扱う。表は増やしていない。

| 名前 | 改める前 | 改めた後 |
|---|---|---|
| `zh-yue`・`zh-yue-HK` | `zh`（簡体字） | `yue`（香港） |
| `zh-yue-Hans-CN` | 簡体字 | 簡体字（`yue` でも用字が優先する。既存の規則） |
| `zh-cmn`・`zh-cmn-Hans-CN`・`zh-min-nan` | `zh`（簡体字） | 表に無い言語（`None`。東アジアの旧マルチバイトの判定を行わない） |
| `zh-CHT`・`zh-CHS` | 繁体字・簡体字 | 変わらない |

テスト: `CultureNameSubtagsTests.Parse` に 5 件、`Resolve_ExtendedLanguage`（8 件）、コアの `ExtendedLanguage_IsUsedForDetection`（香港の Big5 を `zh-yue` と `zh-HK` で同じに、`zh-cmn` と `en-US` で同じに判定する）。
メッセージの言語の選択（`MessageCatalog.ResolveLanguage`）も同じ分解を使うので、UI カルチャーが `zh-yue` なら繁体字の文面になる。
ヘルプの `-Culture` の説明（8 コマンド × 5 言語、複製を含む）に、拡張言語の扱いを足した。10.8 節・10.12 節の 3 を改めた。

**4. ハードリンクの書き戻しに失敗したとき。** `SafeFileOverwrite.Rollback` が写しから書き戻せなかった場合に、残した写しのパスを `KeptBackupPath` に記録する（写しは今までどおり残す）。
エラーを報告するすべての箇所（`Set-` / `Add-ProbedContent` の表現できない文字・整合性検査・書き込みの失敗・完了の失敗、`Out-ProbedFile` / `Export-ProbedCsv` の同じ 4 か所、`Convert-ProbedContent` の書き込みの失敗）で、
`Internal/RollbackNotice.Attach` が、エラーの `ErrorDetails` に元のメッセージと新しいキー `RestoreFromBackupFailed`（5 言語）の文を入れる。
「'<パス>' を元に戻せなかったため、元のファイルは書きかけの状態です。元の内容は '<写しのパス>' に写してあります。この写しから元に戻してください。」
ID・分類・例外（`Exception.Message`）は変えない。パイプラインからパスを受け取る `Set-ProbedContent` が、前のレコードの分を取り消すときに書き戻せなかった場合は、書き直さずに `WriteFailed` でこの文を報告する。

テストで起こせた（`LinkAndPermissionWriteTests.HardLink_RestoreFails_MessageShowsTheCopy`、Set と Out）: ハードリンクに、上流のスクリプトが 1 件目を流した後で実体を読み取り専用にし、2 件目の表現できない文字で元に戻させる。
書き戻しは読み取り専用で失敗し、エラーの ID は `UnrepresentableCharacter` のまま、`ErrorDetails.Message` に写しのパスが入り、写しの中身は元の内容、元のファイルは書きかけになることを確かめる。

**5. LICENSE.txt。**

- **本文の比較:** GitHub のライセンスのひな形（`github/choosealicense.com` の `_licenses/mit.txt`、2026-10-23 に取得）と 1 文字ずつ比べた。著作権の行（年と名前）を除いて違うのは 1 か所だけで、
  20 行目の「OR **THE USE OR** OTHER DEALINGS」の「THE USE OR」が抜けて「OR THE OTHER DEALINGS」になっていた。
  依頼の文面は「OR OTHER DEALINGS」に直すことだったが、それでは原文と一致しないので、**依頼の趣旨（MIT License の原文どおり）に合わせて「OR THE USE OR OTHER DEALINGS」に直した**。
  直した後、`LICENSE.txt` と、同じ文を持つ `EncodingProbe.License` 定数（`Resolve-Encoding -License` が表示する）が、ひな形とバイト単位で一致することを確かめた（年と名前を置き換えて比べた）
- **「Copyright c」:** `LICENSE.txt` には無かった（最初から `Copyright (c)`）。`SnowStack.EncodingProbe.PowerShell/LicenseAndHelp.cs`（`Resolve-Encoding -License` の先頭）にあったので、`Copyright (c)` に直した
- **履歴:**
  - `LICENSE.txt` の「THE USE OR」は、コミット `3c368dc`（2026-08-10「UTF.Unknown のライセンスを MITライセンスと誤認してしたので、正しいライセンス条項に書き換えました。」）で抜けた。
    その前の `9981260`（2026-05-31。ひな形のまま `[year] [fullname]`、ASCII）と `3086af6`（2026-05-31。UTF-8 の BOM 付きになり、著作権の行が `SnowStack.EncodingProbe Copyright (c) 2026 motoi.tsushima`）には「THE USE OR」がある。
    同じコミットで `EncodingProbe.License` 定数にも、抜けた文が入った
  - 「Copyright c」は、コミット `2b2e93c`（2026-06-06「ライセンスとバージョンの表示機能を追加した。」）で、`LicenseAndHelp.cs` と `EncodingProbe.cs` に入った。`EncodingProbe.cs` の分は `3c368dc` で `Copyright (c)` に直り、`LicenseAndHelp.cs` の分が残っていた
  - 「©」（U+00A9）が「c」に変わった形跡は、Git の履歴には無い。`LicenseAndHelp.cs` は最初の版から ASCII のファイルで「c」だった（同じコミットのほかのファイルは BOM 無しの UTF-8 で、文字化けは無い）。
    変わったとすれば、コミットする前（エディターでの入力など）である。なお `©` は、クラスライブラリの csproj の `<Copyright>` と `.psd1` の `Copyright` で、別の表記として使われている
- **著作権の年の現状（直していない）:**

| 箇所 | 現状 |
|---|---|
| `LICENSE.txt`（3 行目） | `Copyright (c) 2025 Motoi Tsushima` |
| `publish/SnowStack.EncodingProbe.PowerShell/LICENSE.txt`（未追跡の写し） | 同上（本文の修正は、リリースの手順で写し直すときに入る。このファイルは変えていない） |
| `SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj` の `<Copyright>` | `Copyright © 2026 motoi.tsushima` |
| `SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.csproj` | `<Copyright>` は無い |
| `publish/.../SnowStack.EncodingProbe.PowerShell.psd1` の `Copyright` | `Copyright © 2026 motoi.tsushima` |
| `SnowStack.EncodingProbe/EncodingProbe.cs` の `License` 定数 | `Copyright (c) 2026 motoi.tsushima` |
| `SnowStack.EncodingProbe.PowerShell/LicenseAndHelp.cs` の `License` | `Copyright (c) 2026 motoi.tsushima`（`c` から直した） |
| `README.md`・`THIRD-PARTY-NOTICES.txt`・`CHANGELOG.md` | 本プロジェクトの著作権の年は書かれていない（`THIRD-PARTY-NOTICES.txt` は第三者の表示だけ） |

年は 2025 と 2026、名前は `Motoi Tsushima` と `motoi.tsushima`、記号は `(c)` と `©` が混在している。利用者が表記を決めるまで直さない。

**確認の結果（10.14 節の修正の後）。** コアのテスト net48 **2117 件**・net10.0 **2107 件**（各 27 件増）、モジュールのテスト **1437 件**（6 件増）、すべて成功。PSCompat は **1006 シナリオ**で 5.1 と 7.x が完全に一致（58 件増）。新しく受け付ける名前（`cel-gaulish`・`zh-min`・`no-bok`・`zh-cmn` など）は、両ホストで CP949 のファイルを判定し、表に無い言語は判定不能（`EncodingDetectionFailed`）、`zh-yue` / `zh-yue-HK` は香港の扱い（950）で、どちらも例外にはならなかった。

### 10.15 性能の確認（依頼 6 章）

`Set-ProbedContent` で、小さなファイル（5 バイト）1000 個に書く時間を測った。Release ビルド、Windows 11 Pro（NTFS、Microsoft Defender は既定のまま）、1 つの書き方につき 5 回（517bb86 は 3 回）の中央値。
第四次（`85318cb`）と今（第五次の後半）は、両ホストとも 2 回ずつ測り、どちらの回も同じ傾向だった（下は 2 回目）。切り分けのため、第五次の前半（`517bb86`）も測った。

| ビルド | ホスト | `-LiteralPath` に 1000 個 | `Get-ChildItem` から | `-Encoding` 省略（継承） |
|---|---|---:|---:|---:|
| 第四次 `85318cb` | 7.6.7 | 1,058 ms | 1,478 ms | 1,766 ms |
| 第四次 `85318cb` | 5.1 | 456 ms | 1,596 ms | 1,721 ms |
| 前半 `517bb86` | 7.6.7 | 10,618 ms | 11,597 ms | 11,302 ms |
| 前半 `517bb86` | 5.1 | 10,119 ms | 10,925 ms | 11,162 ms |
| 今 | 7.6.7 | 11,330 ms | 11,163 ms | 11,432 ms |
| 今 | 5.1 | 10,574 ms | 11,330 ms | 11,756 ms |

**明らかに遅くなっている（約 10 倍、1 ファイルあたり約 1〜1.7 ms → 約 11 ms）。** ただし、遅くなったのは第五次の前半からで、後半の `FileIdentity`（ファイルごとの `CreateFile` の追加）による差は、計測のぶれの範囲だった。
.NET だけで 1000 個に書いて確かめた（7.6.7 で 2 回、5.1 で 1 回）:

| 書き方 | 時間 |
|---|---:|
| 直接書く（`File.WriteAllBytes`） | 約 280 ms |
| 一時ファイルに書いて移す（`File.Move`。比較のため移した先を消す） | 約 1,400 ms |
| 一時ファイルに書いて `File.Replace` で置き換える | 約 8,800〜10,300 ms |

原因は、前半で入れた「同じフォルダーの一時ファイルに書いて `File.Replace`（Windows の `ReplaceFile`）で置き換える」方法の、`ReplaceFile` の費用（1 回あたり約 9 ms）である。
`ReplaceFile` は属性・作成日時・ACL・代替データストリームを引き継ぐ処理をし、ウイルス対策の走査も受けるためと考えられる（内訳は測っていない）。
直すかどうかは利用者が決める（直していない）。考えられる案: 書き込み先が小さい・新しい場合は直接書く、`File.Move`（上書き）で置き換えてから属性などを付け直す、など。どれも 10.6 節の「保たれるもの」を確かめ直す必要がある。

**修正の後の測り直し（2026-10-24。10.16 節）。** 同じ条件（Release ビルド、Windows 11 Pro、5 バイトのファイル 1000 個、1 つの書き方につき準備の 1 回を除いた 5 回の中央値）で、
第四次（`85318cb`）・修正の前（`a980c3d`）・修正の後の 3 つを、両ホストで 2 回ずつ測った（`tools/Measure-OverwritePerformance.ps1`。下は 2 回目。1 回目も同じ傾向）。

| ビルド | ホスト | `-LiteralPath` に 1000 個 | `Get-ChildItem` から | `-Encoding` 省略（継承） |
|---|---|---:|---:|---:|
| 第四次 `85318cb` | 7.6.8 | 1,067 ms | 1,489 ms | 1,695 ms |
| 第四次 `85318cb` | 5.1 | 1,051 ms | 1,595 ms | 1,652 ms |
| 修正の前 `a980c3d` | 7.6.8 | 11,369 ms | 11,275 ms | 11,694 ms |
| 修正の前 `a980c3d` | 5.1 | 11,255 ms | 11,615 ms | 11,621 ms |
| **修正の後** | 7.6.8 | **2,347 ms** | **2,322 ms** | **2,951 ms** |
| **修正の後** | 5.1 | **2,582 ms** | **2,500 ms** | **3,155 ms** |

修正の前の約 1/4〜1/5 になったが、**第四次よりはまだ約 1.5〜2.5 倍遅い**（1 ファイルあたり約 1〜1.7 ms → 約 2.3〜3.2 ms）。
第四次は `FileMode.Create` で開いて書くだけだったのに対し、修正の後は、開く前の実体の判定（`FileIdentity`、第五次の後半）、大きさの確認、完了の時点で元のファイルを読み書き両用で開き直して中身を読む処理がある。内訳は測っていない。

大きなファイル（16 MiB のファイルに 16 MiB を書く、10 個）は、境目を超えるので書き方は修正の前と同じ（置き換え）。遅くなっていないことを確かめた（1 回ずつ）:

| ビルド | ホスト | `-LiteralPath` | `Get-ChildItem` から | `-Encoding` 省略（継承） |
|---|---|---:|---:|---:|
| 修正の前 `a980c3d` | 7.6.8 | 740 ms | 792 ms | 11,973 ms |
| 修正の前 `a980c3d` | 5.1 | 1,195 ms | 1,219 ms | 24,828 ms |
| 修正の後 | 7.6.8 | 785 ms | 784 ms | 11,970 ms |
| 修正の後 | 5.1 | 1,220 ms | 1,238 ms | 21,171 ms |

（差は計測のぶれの範囲。継承が遅いのは書き込み先 16 MiB の判定の時間で、書き方とは関係しない。）

### 10.16 小さなファイルはメモリに写してから直接書く（第五次の仕上げ、2026-10-24）

**利用者の決定。** 性能は直す。小さなファイルは「メモリに写してから直接書く」方法にする。著作権の表記は、すべての箇所を `Copyright (c) 2026 Motoi Tsushima` にそろえる。
`LICENSE.txt` の「OR THE USE OR OTHER DEALINGS」は、10.14 節の 5 のとおりでよい。計画の段階で次の 3 つの判断を示し、認められた。

- **判断 A（まだ無いファイル）:** 開いた時点で書き込み先を直接作る（`FileMode.CreateNew`）。開く時点のエラー（フォルダーの権限など）を、今までと同じ時点で報告するため。
  完了するまでは空のファイルとして見える。同じプロセスのワイルドカードの解決からは、完了するまで除く（`TemporaryFileRegistry` に載せる。今までは完了するまで存在しなかったため）
- **判断 B（既存のファイル）:** 完了の時点まで開かない。ほかのハンドルが開いていて失敗するのが完了の時点になる点を、置き換えと同じにするため。失敗する開き方の条件は変わる（下の表）
- **判断 C（テスト用の差し込み口）:** 書き戻しにも失敗する場合を確実に起こす方法が無いので、`SafeFileOverwrite.BeforeRestoreForTests`（`internal static`）を 1 つだけ足した。テストはここで、別のハンドルからファイルの範囲をロックする

**実装（`Internal/SafeFileOverwrite`）。** 書き方 `OverwriteMethod.MemoryThenDirect` を足し、`SafeFileOverwrite.Write` で書く内容を受け取る（`ProbedFileWriter` と `Convert-ProbedContent` は `.Stream` に書かず、これを呼ぶ）。

| 開いた時点の書き込み先 | 開いた時点でしていること | 書き終えた時点で T 以内 | 途中で T を超えた |
|---|---|---|---|
| まだ無い | 書き込み先を直接作る（空のまま） | 作ったファイルに書く（`DirectCreate`）。失敗したら消す | 作った空のファイルを消してから、まだ無いファイルとして置き換えを始める（一時ファイルを作れなければ直接作る。第五次の後半と同じ） |
| 既にあり、T 以内（ハードリンクを含む） | 何も開かない | 完了の時点で、実体を `FileMode.OpenOrCreate`・読み書き両用・`FileShare.Read`・バッファ無しで開き、元の中身を読み、先頭から書いて長さを合わせる。失敗したらメモリの写しから書き戻す | その時点で、第五次の後半と同じ書き方（10.6 節の表）を始める |
| 既にあり、T を超える | 第五次の後半と同じ | 第五次の後半と同じ | 第五次の後半と同じ |
| リンク切れ | 第五次の後半と同じ（メモリにためない） | 同左 | 同左 |

- 完了の時点で元のファイルが T を超えていたら（開いた後に大きくなった）、何も書かずに閉じて切り替える。実体が無くなっていたら作る（失敗したら消す）
- 直接書くストリームはバッファを持たない（`bufferSize = 1`）。失敗した後に、書き出していない分が後から書かれないようにするため
- 書き戻しは、まず開いたハンドルで、できなければ開き直して行う。どちらもできなければ、メモリの写しを一時フォルダーの `SnowStack.EncodingProbe.<8 桁>.probedbak` に書き出し、
  `KeptBackupPath` に入れる。エラーのメッセージには、10.14 節の 4 と同じく `RollbackNotice` がそのパスを示す
- 切り替えで書き込み先を開くのは書いている途中になるので、`Set-` / `Add-ProbedContent` の `WriteValues` と出力系の `WriteChunk` で、`UnauthorizedAccessException` を捕まえて、元に戻してから `WriteAccessDenied` を報告するようにした
- **変えていないこと:** 同じファイルの判定（`SamePathRoundTrip` など）は、今までどおり開く前（`ProbedFileWriter.Open` の冒頭・`OpenTarget`）に行う。読み取り専用の検査と `-Force`、リンクの先の実体への書き込み、リンク切れの扱いも同じ。
  「エラーで止まったら、そのファイルには、その呼び出しで書いた分を残さない」「Ctrl+C では、それまでに受け取った内容を書く」（`Dispose` → `Complete` が、ためていた分を書き出す）、`-ErrorAction Stop` で元に戻してからエラーを出す順序も同じ

**境目 T を 1 MiB にした（依頼 1.3）。** 同じ大きさの既存のファイルに同じ大きさを `Set-ProbedContent` で書く時間（1 回あたり。`tools/Measure-OverwriteThreshold.ps1`。Release ビルド、境目をリフレクションで 0（常に置き換え）と最大（常に直接）に切り替えた。中央値）:

| 大きさ | 置き換え 7.6.8 | 直接 7.6.8 | 置き換え 5.1 | 直接 5.1 |
|---:|---:|---:|---:|---:|
| 1 KiB | 12.96 ms | 2.69 ms | 14.32 ms | 2.83 ms |
| 64 KiB | 30.61 ms | 3.20 ms | 31.55 ms | 5.01 ms |
| 256 KiB | 30.26 ms | 4.80 ms | 34.86 ms | 7.70 ms |
| 1 MiB | 32.76 ms | 5.59 ms | 45.46 ms | 17.45 ms |
| 4 MiB | 47.88 ms | 17.87 ms | 64.39 ms | 38.16 ms |
| 16 MiB | 81.18 ms | 61.02 ms | 127.65 ms | 99.90 ms |

直接書く方法はどの大きさでも速く、縮む時間は 1 回あたり約 20〜30 ms でほぼ一定（置き換えの固定の費用）である。大きさに対する割合では、1 MiB までは 2.6〜10 倍だが、4 MiB で 1.7〜2.7 倍、16 MiB で 1.3 倍に縮む。
一方、メモリの使用量は T に比例する（書く内容は `MemoryStream` にためるので容量の倍々で最大約 2T、完了の時点で元のファイルの写しが最大 T。1 ファイルあたり最大約 3T）。
強制終了で書きかけが残りうる大きさも T まで広がる。テキストファイルの多くは 1 MiB 以内で、そこまでで割合の効果のほとんどが得られるので、**T = 1 MiB** とした（256 KiB〜8 MiB の範囲の中）。

**複数のファイルに書く場合のメモリ（依頼 1.2）。** `Set-ProbedContent` は、パイプラインの終わりまで書き込み先をすべて開いておくので、ファイルの数だけメモリにためる。
**プロセス全体で、ためる量の合計に 64 MiB の上限を設けた**（`SafeFileOverwrite.MemoryBudget`。`Interlocked` で数える。ランスペースが複数あっても合計する）。
超えそうになったファイルは、その時点で T を超えたものとして今までの方法に切り替える。書き終えた・元に戻した・切り替えたファイルの分は上限から戻る。
1 MiB のファイルなら 64 個まで同時にためられる（`MemoryStream` の容量の分、実際の使用量は最大で約 2 倍）。完了の時点の元のファイルの写しは、1 ファイルずつ順に作って捨てるので上限に数えない。

**ほかのハンドルが開いているとき（判断 B の結果）。** どちらも完了の時点で `WriteFailed`、元のファイルは変わらない（テスト `SmallFileDirectWriteTests.OpenedByAnotherHandle`、PSCompat `Finish/DirectWrite/OpenedByAnother/*`）。

| 相手の共有モード | T 以内（直接） | T 超え（置き換え） |
|---|---|---|
| `Read` | 失敗 | 失敗 |
| `ReadWrite` | **書ける** | 失敗 |
| `Read, Delete` | **失敗** | 書ける |
| `ReadWrite, Delete` | 書ける | 書ける |

**保たれるもの（依頼 1.4。10.6 節の表をやり直した）。** 隠し＋読み取り専用の属性、作成日時（2001-02-03）、代替データストリーム（`:meta`）、ACL（Everyone に ReadData を足したもの）を付けたファイルに、`-Force` で書いて確かめた
（`LinkAndPermissionWriteTests.Metadata_IsPreserved`。書けた場合と、2 件目の表現できない文字で失敗して元に戻した場合の両方。Windows 11（NTFS）。xUnit は 7.x のランスペースで、同じ 18 件を Release ビルドで 7.6.8 と 5.1 のプロセスでも確かめ、すべて同じ結果だった）。

| 書き方 | 経路 | 属性 | 作成日時 | 代替データストリーム | ACL | 実体 |
|---|---|---|---|---|---|---|
| メモリに写してから直接（5 バイト） | そのまま・ハードリンク・シンボリックリンク | 保たれる | 保たれる | 保たれる | 保たれる | 変わらない |
| 置き換え（T + 1 バイト） | そのまま・シンボリックリンク | 保たれる | 保たれる | 保たれる | 保たれる | 書けたときは変わる |
| 写してから直接（T + 1 バイト） | ハードリンク | 保たれる | 保たれる | 保たれる | 保たれる | 変わらない |
| 追記 | そのまま・ハードリンク・シンボリックリンク | 保たれる | 保たれる | 保たれる | 保たれる | 変わらない |

**テスト（依頼 1.4）。** モジュールのテストに 69 件を足した。

- `FileSystemTests/SafeFileOverwriteTests`: 書く内容・元のファイル・まだ無いファイルの、T ちょうどと T + 1 で選ばれる書き方（`Method`）、少しずつ書いて T を超えた場合、ハードリンク、メモリの上限、
  まだ無いファイルを開いた時点で作り完了するまでワイルドカードから除くこと、完了の前に元に戻すと元のファイルに触れない（実体・更新日時も同じ）こと、
  書いている途中の `IOException`（元のファイルの末尾より先の範囲を別のハンドルがロック）でバイト単位で元どおりになること、書き戻しにも失敗した場合（差し込み口で書き戻す範囲もロック）に写しが一時フォルダーに残ること
- `CmdletTests/SmallFileDirectWriteTests`: 4 つのコマンドの T ちょうど・T + 1（実体が変わったかどうかで書き方を見分ける）、まだ無いファイル、表現できない文字と書き込みの `IOException` でバイト単位で元どおり、
  ほかのハンドル（上の表）、`-ErrorAction Stop`、パイプラインの停止（`PowerShell.Stop()`。それまでに受け取った内容が書かれる）
- `LinkAndPermissionWriteTests`: 保たれるものを 4 つの書き方 × 経路 × 成否の 18 件にし、ハードリンクの失敗を T 以内と T 超えの両方に、フォルダーにファイルを作れない場合の T 超えを足した。
  ハードリンクの書き戻しの失敗（10.14 節の 4）は、T 以内では 1 件目をメモリにためるだけで元のファイルに触れないので、元のファイルを T + 1 バイトにして写してから直接書く方法を通すようにした
- `ResolveEncodingCmdletTests.Invoke_License_IncludesPowerShellNotice`: `-License` の出力に `Copyright (c) 2026 Motoi Tsushima` が 2 回（モジュールとクラスライブラリ）出て、`motoi.tsushima` が無いこと
- 静的な設定（上限・差し込み口）を変えるテストと書き方を確かめるテストは、並行に走らないコレクション（`Helpers/OverwriteStateCollection`）に入れた
- PSCompat に `Finish/DirectWrite/*`（38 シナリオ）を足した。書き方は実体の鍵（モジュールの内部の `FileIdentity` をリフレクションで呼ぶ）が変わったかどうかで記録する（`fsutil` はこの実行環境で使えなかった）

**著作権の表記（依頼 2 章）。** 次を `Copyright (c) 2026 Motoi Tsushima` にした。ほかに本プロジェクトの著作権の表記は無かった（AssemblyInfo・nuspec は無い。README・`THIRD-PARTY-NOTICES.txt` にあるのは Microsoft の表記）。
`publish/` の `LICENSE.txt`（未追跡の写し）は変えていない。

| 箇所 | 前 |
|---|---|
| `LICENSE.txt` | `Copyright (c) 2025 Motoi Tsushima` |
| `SnowStack.EncodingProbe.csproj` の `<Copyright>` | `Copyright © 2026 motoi.tsushima` |
| `SnowStack.EncodingProbe.PowerShell.csproj` の `<Copyright>` | 無かった（足した。アセンブリの著作権の属性になる） |
| `publish/.../SnowStack.EncodingProbe.PowerShell.psd1` の `Copyright` | `Copyright © 2026 motoi.tsushima` |
| `EncodingProbe.cs` の `License` | `Copyright (c) 2026 motoi.tsushima` |
| `LicenseAndHelp.cs` の `License` | `Copyright (c) 2026 motoi.tsushima` |

`Authors`・`Company`（csproj）と `.psd1` の `Author`・`CompanyName` は `motoi.tsushima` のまま（著作権の表記ではないので変えていない）。

**確認の結果（依頼 3 章）。**

| 確認 | 結果 |
|---|---|
| コアのテスト | net48 **2117 件**、net10.0 **2107 件**、すべて成功（増減なし） |
| モジュールのテスト | net10.0 **1506 件**、すべて成功（69 件増） |
| PSCompat | **1044 シナリオ**で 5.1 と 7.x が完全に一致（38 件増）。`Finish/` の値も両ホストのレポートで期待どおり（T ちょうどは実体が同じ、T + 1 は変わる） |
| テスト担当のスクリプト 17 本 | 作業フォルダー（scratchpad）に、修正の後と修正の前（`a980c3d`）の Release ビルドで、それぞれ配布フォルダーと同じ構成を作り、`scripts\` の写しを `Invoke-BothHosts.ps1` で 7.6.8 と 5.1 で実行した（テスト担当のフォルダーには何も置いていない）。**両方のビルドの標準出力が一致した**（違いは作業フォルダーの名前と、それによる 5.1 のコンソールの折り返しの位置だけ）。ホストの間の差は 10.11 節と同じもの（0003 の `Get-ChildItem` の順序、0005 の書式、0009 のエラーの表示）と、作業フォルダーの名前（`-Core` / `-Desktop`）だけ |

**決めきれなかったこと・新たに気づいたこと。**

1. **ファイルに WriteData の拒否の ACL が付いていると、T 以内のファイルは書けなくなった。** 修正の前は `ReplaceFile`（フォルダーの権限で置き換える）で書けていた（両ビルドで確かめた。T を超えるファイルは今も書ける）。
   修正の後は `WriteAccessDenied` で、元のファイルは変わらない。標準の `Set-Content` と同じく、ファイルの ACL に従う挙動である。完了の時点で開けなかったら置き換えに切り替える案もあるが、入れていない（利用者の判断を求める）
2. **第四次よりは、まだ約 2 倍遅い**（10.15 節の表）。内訳は測っていない
3. **まだ無いファイルは、完了するまで空のファイルとして見える**（判断 A）。ほかのプロセスからは、書いている最中に 0 バイトのファイルが見える。強制終了されると 0 バイトのファイルが残る
4. 失敗する開き方の条件が変わった（上の表）。`ReadWrite` で開いているほかのプロセス（ログを読み続けるツールなど）がいても、T 以内なら書ける。`Read, Delete` だけで開いているものがいると、T 以内は書けない
5. メモリの上限は、`SafeFileOverwrite` を完了も元に戻しもせずに捨てると戻らない（コマンドは必ずどちらかを呼ぶので、ふつうは起きない）。戻らなければ以後のファイルが今までの書き方になるだけで、結果は変わらない
6. Linux / macOS では、T 以内のファイルは直接書くので、所有者とハードリンクが保たれるようになるはずだが、この環境では実行していない
7. 測定の道具の問題として、Bash から `-File` で配列（`1,64`）を渡すと 1 つの数になること、`fsutil` がこの実行環境では使えないことに当たった（製品とは関係ない）

**報告の後の利用者の決定（2026-10-24）と対応。**

1. **WriteData の拒否が付いたファイルに書けないのは正しい挙動とする**（ファイルの ACL に従う。1.2.0 仕様書 2.9 の「`-Force` はセキュリティ上の制限を越えない」と同じ。標準の `Set-Content` とも同じ）。
   ただし T を超えるファイルは置き換えで書けてしまい、大きさで結果が変わっていたので、そろえた。
   `SafeFileOverwrite.BeginStreaming` が置き換えを始める前に `EnsureWritable` で元のファイルを `FileMode.Open`・`FileAccess.Write`・`FileShare.ReadWrite | Delete` で開き、すぐに閉じる。
   `UnauthorizedAccessException` ならそのまま投げ（`WriteAccessDenied`）、共有違反などの `IOException` は無視する（依頼の「ほかのハンドルが開いているときの失敗の条件を今より厳しくしない」を守るため。
   相手が書き込みの共有を許していないだけなら、置き換えは今までどおり試みられ、上の表の結果は変わらない）。写してから直接書く方法は、もともと元のファイルを書き込み用に開くので ACL に従っていた。
   権限が無いと分かる時点は、T 以内は完了の時点（判断 B）、T を超えるものは開く時点で、どちらも同じ ID・元のファイルは変わらない。
   テスト `SmallFileDirectWriteTests.WriteDataDenied_IsWriteAccessDenied_RegardlessOfSize`（`Set`・`Add`・`Out`・`Export`・`Convert` × 5 バイトと T + 1 バイトの 10 件）。
   両ホストのプロセスでも確かめた（拒否する権利 × 大きさの 6 通り。WriteData を拒否したものは両方の大きさで `WriteAccessDenied`、Delete だけを拒否したものは書ける）
2. 第四次より約 2 倍遅いのは受け入れる（直さない）
3. ほかのハンドルが開いているときに失敗する条件が変わった点は受け入れる。ヘルプ（5 言語と複製）の注意事項には、10.16 節の修正で既に書いてある（置き換えは削除の共有、直接の書き込みは書き込みの共有）
4. まだ無いファイルが完了まで 0 バイトに見える点は受け入れる。強制終了で 0 バイトのファイルが残りうることを、仕様書 16.5 の既知の限界に足した
5. `Authors`・`Company`・`Author`・`CompanyName` は `motoi.tsushima` のままにする

**対応の後の確認（2026-10-24）。** コアのテスト net48 **2117 件**・net10.0 **2107 件**、モジュールのテスト **1516 件**（10 件増）、すべて成功。PSCompat は **1044 シナリオ**で 5.1 と 7.x が完全に一致。
テスト担当のスクリプト 17 本を、対応の後の Release ビルドで作り直した作業フォルダーで両ホストで流し、修正の前（`a980c3d`）の出力と一致した（作業フォルダーの名前とコンソールの折り返しを除く）

## 11. 第六次 A — テスト担当の報告への対応（BUG-0018・0020・0021・0031・0032・0033・0037・0042。2026-10-26）

第六次は 3 回（A・B・C）に分けて依頼される。本章は A。テスト担当の報告（`C:\_test\EncodingProbeBboxTest\reports\bugs\BUG-<番号>.md`）は第五次より前のモジュールで実行したもので、
報告の「実際の結果」は古い挙動である。仕様は 1.3.0 仕様書 18.4・25 章、変えた挙動は CHANGELOG の「挙動の変更（PowerShell モジュール。第六次 A）」と「挙動の変更（クラスライブラリ）」にある。

### 11.1 利用者の決定（計画の確認のとき）

1. **Linux の `ansi` / `oem` は、WSL に .NET を入れて実測する**（Ubuntu-24.04 のホームフォルダーにだけ入れる。sudo・apt は使わない）。macOS は「.NET のソースからの推定で、実測していない」と書く
2. **BUG-0042 は、すべての文字エンコーディングに当てはめる。** ただし系統ごとに途中で切れたファイルを両ホストで読み、一致しないものは直さずに一覧にする。`Convert-ProbedContent` が変わらないことを確かめる
3. **BUG-0018 で、`-AllowEncodingChange` で別の文字エンコーディングを追記する場合は、戻すシーケンスを書かない。** 仕様書 18.3（18.4 に書いた）とヘルプの `-AllowEncodingChange`（5 言語）に書く
4. 計画への指摘: 0037 は書き込みの全コマンドに単体テストを入れる。0033 は標準を両ホストで実測して合わせ、パイプラインも確かめる。
   0031 は作れない候補を外さず、復号を確かめるのは判定の標本に限り、両 TFM で同じ結果になることをテストで確かめる（既存のテストで結果が変わったら止まる）。
   0018 は末尾から探す方法を検討し、選んだ方法と理由を書く。0021 は 1.1.0 からの挙動であることを CHANGELOG に書く

### 11.2 BUG-0037（UTF-7 の `Encoding` のインスタンス）

`Convert-ProbedContent` の `ResolveTargetFromEncoding` は、`Encoding` のインスタンスの分岐（`GetPreamble()` に従う）を、UTF-7 の検査より先に通していた。
65000 の検査を、インスタンスの分岐より前に移した（ID は名前と同じ `EncodingNotAllowedForWrite`）。
ほかの 4 コマンドは、束縛の段階の `ValidateForWrite` がコードページで調べるので、もともとインスタンスも `ParameterArgumentTransformationError` で拒否していた。
`Utf7InstanceWriteTests` が、5 コマンド（`Out-` と `Export-` は `-Append` の有無も）× 名前・インスタンス 2 種で、ID とファイルが変わらないことを固定する。

### 11.3 BUG-0033（`-Value` の配列の `$null`）

標準の `Set-Content` / `Add-Content` を両ホストで実測した（`-Value` の 5 通り × パイプラインの 4 通り × `-NoNewline` の有無 × 既存のファイルの有無。5.1 は `-Encoding Ascii`、7.x は `utf8NoBOM`）。
**両ホストの標準は同じ結果**で、モジュールとの違いは `$null` の要素だけだった。

| 入力 | 標準（両ホスト）| 第五次までのモジュール |
|---|---|---|
| `-Value @('a',$null,'b')` | `61 0D 0A 62 0D 0A` | `61 0D 0A 0D 0A 62 0D 0A` |
| `-Value @($null,$null)`（Set、新規・既存） | 0 バイト | `0D 0A 0D 0A` |
| `-Value @($null)`（Set） | 0 バイト | `0D 0A` |
| `-Value @($null,$null)`（Add、既存 `OLD`） | `OLD` のまま | `OLD` + `0D 0A 0D 0A` |
| `-Value $null`・`-Value @()`・パイプラインの `$null` | 標準と同じ（0 バイト。`P @()` だけは標準もモジュールもファイルを作らない） | 同じ |
| `-NoNewline` | 標準と同じ | 同じ |

`WriteValues` で `$null` の要素を飛ばした。すべて `$null` なら、`-Value $null` と同じく何も書かない（Set- は空のファイル）。
以前の挙動を固定していた `SetProbedContentTests.Write_NullElement_WritesEmptyLine` を `Write_NullElement_IsSkipped` に改めた（依頼による挙動の変更）。

### 11.4 BUG-0032（`ansi` / `oem`）

**直し方。** クラスライブラリに `Internal/SystemCodePages`（internal）を置いた。Windows では `GetACP` / `GetOEMCP`（kernel32 の P/Invoke。net48・net10.0 とも）、それ以外は今までどおり `CultureInfo.CurrentCulture.TextInfo` の値。
`PlatformInfoResolver.ResolveLocale`（`Get-EncodingProbePlatformInfo` の `Locale`）とモジュールの `EncodingVocabulary`（`ansi` / `oem`）の両方がこれを使う。
`LocaleInformation` の型・プロパティは変えていない（XML ドキュメントは、もともと「システムの ANSI コードページ」と書いていた。Windows 以外の値の説明を直した）。

- 65001 は `BuildEncoding(65001, emitBom: false)` で BOM 無しの UTF-8 にする（`Encoding.GetEncoding(65001)` は BOM 付き）
- `-Encoding 0`（数値・文字列）は `FromCodePage` で `UnknownEncoding` にする（`1`・`2`・`3` と同じメッセージ・同じ ID）
- 5.1 の 0 バイトのファイルの既定（`EncodingInheritance.ForEmptySource` の `Encoding.Default`）は、もともとシステムの ANSI なので変えていない

**確かめた結果（Windows。システムのロケールは ja-JP で ANSI・OEM とも 932）。** スレッドのカルチャーを ja-JP・de-DE・en-US・ru-RU・ko-KR・ar-SA・不変にしても、
`ansi` / `oem` と `Locale` は 932 / 932 で、標準の 7.x の `-Encoding ansi`・5.1 の `Default` と同じ（テスト担当の BUG-0032.ps1 も両ホストで 932）。

> **訂正（リリース前の補足。15.6 節）:** 「標準の 7.x の `-Encoding ansi` と同じ」は誤りだった。標準の 7.x の `ansi` は、プロセスの中で最初に使った時点の現在のカルチャーの ANSI コードページで、その後は変わらない。
> 上の確認は、1 つのセッションの途中でスレッドのカルチャーを変えて測ったため、標準の側が最初の ja-JP（932）のままになり、一致して見えた。
> 「形式」をシステムのロケールと違う値にしている環境（日本語の Windows で形式を英語（米国））では、標準は 1252、本モジュールは 932 で違う。
> 本モジュールの値（システムのロケール）は正しく、5.1 の `Default` / `Oem`、7.x の `oem` とは同じ。ソース（`SystemCodePages.cs` のコメント）は今回は直していない

**65001 の環境は作らなかった。** 「ベータ: ワールドワイド言語サポートで Unicode UTF-8 を使用」は OS の設定の変更と再起動が要り、利用者の PC の状態を変えるため。
**標準のコマンドとは比べていない。** 実装は 65001 を BOM 無しの UTF-8 にする（単体テストは「`ansi` / `oem` は BOM を持たない」ことを固定する）。

**Linux の実測。** WSL の Ubuntu-24.04 のホームフォルダーにだけ、次を入れた（sudo・apt は使っていない）。

- `~/.dotnet`: .NET ランタイム **10.0.14**（`dotnet-install.sh --channel 10.0 --runtime dotnet`。結果的に測定には使わず、下の PowerShell に同梱のランタイムで動かした）
- `~/powershell-7.6.9`: **PowerShell 7.6.9** の linux-x64 の tar.gz（.NET 10.0.14 を同梱。依頼では .NET だけを入れる話だったが、標準の `-Encoding ansi` / `oem` と比べるために足した。消すときはフォルダーを消す）

作業フォルダーに作った配布の構成（net10.0 の Debug ビルド）を `/mnt/c/...` から読み込み、`env -u LC_ALL -u LC_CTYPE LANG=<値> pwsh` で測った。
試した `LANG`: `C`・`POSIX`・`C.UTF-8`・`en_US.UTF-8`・`ja_JP.UTF-8`・`ja_JP.eucJP`・`de_DE.UTF-8`・`ru_RU.UTF-8`・`ko_KR.UTF-8`・`zh_CN.UTF-8`・`zh_TW.UTF-8`・`tr_TR.UTF-8`・`ar_SA.UTF-8`、
と `LANG=ja_JP.UTF-8 LC_ALL=de_DE.UTF-8`。結果は仕様書 25.5 の表（モジュールの `ansi` / `oem` と `Locale` は現在のカルチャーの `TextInfo` の値で一致）。

- 標準の 7.6.9 の `Set-Content -Encoding ansi` は、モジュールの `ansi` と同じコードページで書いた（ja_JP なら `é` → `65`、ru_RU なら `65`、zh_CN なら `A8 A6`）。**標準の `-Encoding oem` は、どの LANG でも UTF-8（`C3 A9`）** だった。モジュールの `oem` とは違う（今の挙動を変えない、という依頼のとおり直していない）
- 最初は `LC_ALL=`（空の値）を付けて測り、すべてが不変カルチャーになった。.NET（ICU）は空の `LC_ALL` も「設定されている」とみなす。この結果も仕様書に書いた
- システムの言語のパッケージ（`locale -a`）は `C`・`C.utf8`・`POSIX` だけだったが、.NET は環境変数の名前からカルチャーを作るので、測定には影響しない

macOS は実測していない。.NET のソースからの推定（現在のカルチャーの `TextInfo` の値）を、仕様書と README に「推定」と書いた。

### 11.5 BUG-0021（BOM が 2 個続くファイル）

**原因。** `ProbedFileReader` は、ファイル先頭の BOM の後ろへシークしてから `StreamReader` を作る。`StreamReader` は `detectEncodingFromByteOrderMarks: false` でも、
渡された Encoding の preamble と一致する先頭を読み飛ばす（.NET Framework・.NET とも）。BOM 付きの Encoding（`utf8BOM`・`unicodeBOM`・裸の `unicode`・`utf32` など、
判定した「BOM あり」の結果から組み立てたもの）を渡すと、2 個目の BOM も消えた。**1.1.0 の最初の版（`c1fae6d`）から同じコード**で、1.1.0 からの挙動である。

**直し方。** `StreamReader` には preamble を持たない包み（`Internal/ReaderEncoding`）を渡す。符号化・復号・フォールバックは包んだ Encoding にそのまま任せる。
読み込みの経路（`Get-ProbedContent`・`-Raw`、`Import-ProbedCsv`、`Export-ProbedCsv -Append` のヘッダー、`-DetectionLineCount` の検証の後の読み込み）はすべて `ProbedFileReader` を通るので、1 か所で直る。
`-DetectionLineCount` の全体の検証（`StrictDecodeVerifier`）は `StreamReader` を使わないので、もともと 2 個目を文字として扱っていた。

`DoubleBomReadTests`（111 件）は、5 系統 × 語彙（`Auto`・BOM 付き・`NoBOM`・裸名・WebName・数値）× BOM 2 個・3 個で、`Get-ProbedContent`・`-Raw`・`Import-ProbedCsv`（`-DetectionLineCount` の有無）と、
`Export-ProbedCsv -Append` の照合、`-Raw` → `-EncodingFrom -NoNewline` の往復を固定する。直す前のコードでは 42 件が落ちることを確かめた。

### 11.6 BUG-0042（途中で切れたファイルの末尾）

**原因。** .NET の `StreamReader` はストリームの終わりで Decoder を flush し、.NET Framework は flush しない。

**直し方（net48 だけ）。** `StreamReader` の中は変えられないので、ストリームと Decoder の両方を包む（`EndOfStreamSignal`・`FlushAtEndDecoder`。`ReaderEncoding.cs`）。

1. `EndOfStreamSignal` は、包んだ `FileStream` が要求より少なく返し、かつ位置が長さに達したとき（終わり）、読めたバイトの後ろに 1 バイトの目印を 1 回だけ足す
2. `FlushAtEndDecoder` は、目印が足された回の `GetChars` で、目印を除いたバイトを `flush: true` で包んだ Decoder に渡す

目印は最後のバイトと同じ回に足す。.NET Framework の `StreamReader.Peek` は、短い読み込みの後（`_isBlocked`）には読みに来ないので、別の回に足すと `Peek` を使う経路（CSV の解析）で flush した文字が届かない。
.NET Framework 4.8 の `StreamReader` は `Decoder.GetChars(byte[], int, int, char[], int)` だけを呼ぶ（4.8 は更新されない）。net10.0 は何も挟まない。

**系統ごとの確認（依頼 11.1 の 2）。** 次の入力を両ホストで `Get-ProbedContent`・`-Raw`・`Import-ProbedCsv -Header H`・`Convert-ProbedContent` で読んだ（各入力は `A` と改行の後ろに、途中で切れた文字を置いた）。
表の値は `-Raw` の 2 行目以降の符号位置。`Get-ProbedContent`・`Import-ProbedCsv` も同じ文字を返した。

| 入力 | 7.x | 5.1（修正の前） | 5.1（修正の後） | 一致 | `Convert-ProbedContent`（両ホスト・前後で同じ） |
|---|---|---|---|---|---|
| UTF-8 の 3 バイト文字の 2 バイト（`E6 97`） | FFFD | （なし） | FFFD | ○ | InvalidSourceBytes |
| UTF-8 の 4 バイト文字の 1 バイト・3 バイト | FFFD | （なし） | FFFD | ○ | InvalidSourceBytes |
| BOM 付き UTF-8 の `E6 97` | FFFD | （なし） | FFFD | ○ | InvalidSourceBytes |
| UTF-16LE（BOM の有無）・BE の奇数の長さ（`31 00 32`） | 0031 FFFD | 0031 | 0031 FFFD | ○ | InvalidSourceBytes |
| UTF-16LE の対になっていない上位サロゲート（`3D D8`） | FFFD | （なし） | FFFD | ○ | InvalidSourceBytes |
| 同上 + 1 バイト | FFFD FFFD | （なし） | FFFD FFFD | ○ | InvalidSourceBytes |
| UTF-32LE の 1・2・3 バイト、UTF-32BE の 3 バイト | FFFD | （なし） | FFFD | ○ | InvalidSourceBytes |
| Shift_JIS の先行バイト（`93`）・「日」+ 先行バイト | 30FB / 65E5 30FB | （なし） / 65E5 | 30FB / 65E5 30FB | ○ | InvalidSourceBytes |
| EUC-JP の先行バイト（`C6`）・SS3 の 2 バイト（`8F B0`） | 30FB / 30FB 30FB | （なし） / 30FB | 30FB / 30FB 30FB | ○ | InvalidSourceBytes |
| CP949・GB2312（936）・Big5 の先行バイト | 003F | （なし） | 003F | ○ | InvalidSourceBytes |
| **GB18030 の 4 バイトの 2 バイト（`81 30`）** | 003F 0030 | （なし） | 003F | **×** | InvalidSourceBytes |
| **GB18030 の 4 バイトの 3 バイト（`81 30 81`）** | 003F 0030 003F | （なし） | 003F 003F | **×** | InvalidSourceBytes |
| ISO-2022-JP の途中で切れた ESC（`1B 24`・`1B`） | 001B 0024 / 001B | （なし） | 001B 0024 / 001B | ○ | 変換する（`1B 24` / `1B` をそのまま UTF-8 に） |
| ISO-2022-JP の 2 バイト文字の 1 バイト（`1B 24 42 46`） | 30FB | （なし） | 30FB | ○ | InvalidSourceBytes |
| ISO-2022-JP の JIS のまま終わる（`… 46 7C`） | 65E5 | 65E5 | 65E5 | ○ | 変換する |
| ISO-2022-KR の SO の後の 1 バイト | 003F | （なし） | 003F | ○ | InvalidSourceBytes |
| HZ の `~{` の後の 1 バイト・末尾の `~` | 003F | （なし） | 003F | ○ | InvalidSourceBytes |

- **一致しないのは GB18030 の途中で切れた 4 バイトの列の 2 件だけ**（依頼どおり直さない）。.NET は不正な先頭の 1 バイトだけを `?` にして、続く `30` を ASCII として読み直すが、
  .NET Framework は 2 バイトをまとめて 1 つの `?` にする（復号器の実装の違い。flush の有無ではない）
- `Convert-ProbedContent` の結果は、修正の前後・両ホストで同じ（変えていない）。途中で切れた ISO-2022 の ESC を `Convert-ProbedContent` が不正なバイト列としないのは、.NET の ISO-2022 の復号器が ESC を文字として返すためで、前からの挙動（本件の範囲外。報告だけ）
- 性能: net48 で 1 回の読み込みごとに包みを 1 段通るだけで、`StreamReader` の速い経路は変わらない（測っていない）

PSCompat（`Sixth/Truncated/*`、GB18030 の 2 件を除く 14 件）が両ホストの一致を、`TruncatedTailReadTests` が 7.x の挙動を固定する（単体テストは net10.0 だけで動く）。

### 11.7 BUG-0018（ASCII 以外の状態のまま終わる ISO-2022 の追記先）

**直し方。** 追記は 3 コマンドとも `ProbedFileWriter.Open(append: true)` を通るので、ここで行う。書くコードページが 50220・50221・50222・50225 で、追記先が 1 バイト以上なら、
`Internal/Iso2022TailState` で末尾の状態から戻すシーケンスを求め、**最初に 1 バイト以上書くとき**（`WriteBytes`）に先に書く。
補う改行（`Export-ProbedCsv` の 18.3）も追記する内容も `Write` で書くので、順序は「戻すシーケンス → 補う改行 → 内容」になる。取り消しは元の長さへの切り詰めなので、戻すシーケンスも一緒に消える。
何も書かない呼び出し（入力が 0 件、`-WhatIf`、整合性検査で最初の行から止まる）では書かない。

**末尾の状態の求め方（依頼 11.1 の指摘）。** 末尾から逆向きに 64 KiB ずつ読み、最後の G0 の指示と最後の SO / SI が**両方見つかった時点で止める**方法にした。理由:

- ISO-2022 では `1B`・`0E`・`0F` は制御の意味でしか現れない（2 バイト文字は `21`〜`7E`）ので、逆向きに探しても誤らない。ブロックの境目にまたがるエスケープシーケンスは、後ろのブロックの先頭 3 バイトを持ち越して読む
- 先頭から読む方法は、大きな追記先で毎回ファイル全体を読む。逆向きなら、日本語を含む ISO-2022-JP（`ESC ( B` で ASCII に戻る行が続く）は、ふつう末尾の 1 ブロックで G0 の指示が見つかる
- ただし SO / SI は「無い」ことを確かめないと止まれないので、SO / SI を使わないファイル（.NET の 50220 で書いたものなど）は先頭まで読む。
  ISO-2022-KR は G0 を見ないので、SO / SI が見つかれば止まる。継承する場合は、その前の判定でファイル全体を読んでいるので、費用は同じ程度である
- 「SO の前の G0 の指示」は、SO / SI の状態に関係しない（G0 の指示は SO の状態を戻さない）ので、2 つを別々に探してよい

`ESC $ ( B` は ASCII ではない（G0 に 2 バイトの文字集合）ので戻す。最初の実装はこれを ASCII と取り違えていたので、テストを書く前に直した。

**`-AllowEncodingChange`（利用者の決定）。** 書くコードページが ISO-2022 でないときは調べない。`-AllowEncodingChange -Encoding utf8NoBOM` で JIS のまま終わるファイルに追記すると、今までどおり `ESC ( B` を書かない。

**HZ の経路（依頼 1 章。報告だけ）。** 判定エンジンは HZ（52936）を返さない（`~{` `~}` は ASCII のバイトなので、ISO-2022 / ASCII の判定で `us-ascii` になり、UTF.Unknown には進まない）。
`~{` のまま終わる HZ のファイルに `Add-ProbedContent -Value xy` すると、両ホストで次のとおり（`41 0D 0A 7E 7B 56 50` に追記）。

| 指定 | 書いたバイト | HZ として読み戻すと |
|---|---|---|
| `-Encoding` 省略（継承） | `us-ascii` → BOM 無しの UTF-8 として、`78 79 0D 0A` を足す | `A`・CRLF・`中`・`?`・CRLF（`xy` が消える） |
| `-Encoding hz-gb-2312` | `78 79 0D 0A` を足す（`~}` を書かない） | 同上 |
| 対照（`~}` で閉じた追記先） | 同じ | `中xy` と読める |

HZ も ISO-2022 と同じ問題を持つが、継承されないので明示した場合だけに起きる。依頼どおり直していない（仕様書 18.4 に既知の限界として書いた）。

**テスト。** `Iso2022TailStateTests`（部品。G0 の指示の種類、SO / SI、G1 の指示、途中で切れた ESC、ISO-2022-KR、ブロックの境目にまたがる ESC、末尾から 64 KiB より前の指示、開いたときの長さより後ろを読まないこと）、
`Iso2022AppendTests`（3 コマンド × 継承・明示、CRLF で終わる形、日本語を含む行、ASCII で終わる追記先は変えない、ISO-2022-KR、何も書かない呼び出し、取り消し、`-AllowEncodingChange`）、
PSCompat（`Sixth/Iso2022Append/*`）。

### 11.8 BUG-0020（CSV の列名の比較）

**列名を比べている箇所（依頼 1 章の一覧）。**

| 箇所 | ファイル | 比べ方 |
|---|---|---|
| 列名（`-Header` を含む）の重複の検査 | `Internal/Csv/ImportCsvHelper.ValidatePropertyNames` | **改めた**（`CsvNameComparer`） |
| `Export-ProbedCsv -Append` の追記先の列とプロパティの照合 | `Internal/Csv/ImportExportCSVHelper.ReconcilePreexistingPropertyNames` | **改めた** |
| 照合した列の値の取り出し（列名でプロパティを引く） | `Internal/Csv/ExportCsvHelper.ConvertPSObjectToCSV`（2 か所） | **改めた**（`CsvPropertyViews.FindProperty`） |
| `-QuoteFields` の名前とプロパティ名の照合 | `Internal/Csv/ExportCsvHelper` のコンストラクター（`_quoteFields`） | **改めた** |
| PSObject のメンバー名との突き合わせ（拡張・アダプテッドの見え方） | `Internal/CsvPropertyViews.GetAdaptedPropertyNames` | 変えない（ホストの PSObject の規則と同じでなければならない） |
| 入力が `IDictionary` のときのキーの照合 | `ExportCsvHelper.ConvertPSObjectToCSV` の `dictionary.Contains` | 変えない（入力の辞書の比較器） |
| `#Type` / `CSV:` の接頭辞 | `ImportCsvHelper`・`ExportCsvHelper` | 列名ではない（ASCII だけなので結果はホストで変わらない） |

照合が通った列の値の取り出しは、テスト担当の報告に無かったが、直す途中で見つけた。照合を `CsvNameComparer` にすると、5.1 では追記先の `ǅ` とプロパティの `ǆ` が照合を通るのに、
値は `mshObject.Properties["ǅ"]`（ホストの規則）で引くので見つからず、空で書いていた（7.x は `"x","y"`、5.1 は `,"y"`）。`FindProperty` は、まずホストの規則で引き、見つからなければ `CsvNameComparer` で一覧から探す。

**比較器（`Internal/CsvNameComparer`）。** net10.0 は `StringComparer.OrdinalIgnoreCase`。net48 は、`tools/New-CsvNameCaseTable.ps1` が .NET 10.0.14 で生成した表（`Internal/CsvNameCaseTable.cs`。同値類 1445 個・組 1470 個）で比べる。
表は、全符号位置（U+0000〜U+10FFFF。単独のサロゲートも 1 文字として含む）を 1 文字の文字列にして `OrdinalIgnoreCase` で並べ、隣り合って等しいものを同値類にし、代表を最小の符号位置にしたもの。
同値類の中の UTF-16 の長さが同じであることを生成のときに確かめている（面をまたぐ大文字小文字は無い）。

**確かめたこと。**

- net10.0（`CsvNameComparerTests`）: 全符号位置を実行環境の `OrdinalIgnoreCase` で並べた同値類と、表の同値類が完全に一致する（同じ同値類は同じ代表、別の同値類は別の代表）。
  複数文字の文字列 20 万組（同値類の符号位置・サロゲート・ASCII を混ぜる）で、表の比較器と実行環境が一致し、等しいものはハッシュも等しい
- 両ホスト（PSCompat の `Sixth/CsvName/AllCodePoints`。`Add-Type` の補助で全符号位置を回す）: モジュールの比較器が表どおりに比べる（不一致 0）。
  ホストの .NET の `ToUpperInvariant` / `ToLowerInvariant` の組でも比較器と表が一致する（不一致 0）
- **5.1 のホストの `OrdinalIgnoreCase` で等しいのに、表では別になる組は無かった**（上の大文字・小文字の組で 0 件）。.NET Framework の同値類は .NET 10 の同値類に含まれる。
  そのため、5.1 で PSObject がメンバー名の重複として拒否するのに、列名の検査は通す、という組は（調べた範囲では）無い
- テスト担当の 20 組のうち、ソフトハイフン・ZWJ・合成文字・全角・かな・`ß`/`SS`・`İ`/`i`・`K`(U+212A)/`k` などは、もともと両ホストで「別の列」だった（変わらない）。
  `CsvColumnNameComparisonTests`（同じ列 5 組・別の列 4 組）と PSCompat（9 組）が、報告の 4 組と対照の組を固定する

`Internal/Csv/` の移植したコードは、比較器と `FindProperty` を呼ぶところだけを変え、移植元の表記をコメントに残した（規則は移植元と同じで、5.1 での結果を 7.x にそろえるための変更）。

### 11.9 BUG-0031（短い EUC-JP を Shift_JIS と判定する）

**直し方。** `EncodingDetector.Detection` の曖昧な 3 つの分岐（EUC-JP / Shift_JIS、EUC-KR / CP949、EUC-TW / CP950）で、カルチャーと改行で決めた候補を `ExcludeUndecodableCandidate(決めた候補, もう一方)` に通す。
決めた候補で標本を厳密に復号できず（`false`）、もう一方が `false` でなければ、もう一方を返す。`CanDecodeStrictly` は例外フォールバックの Encoding の `GetCharCount` を標本（`_buffer` の先頭 `BufferSize` バイト）に対して呼ぶ。

- net10.0 では `CodePagesEncodingProvider.Instance` から直接取り出す（クラスライブラリはグローバルに登録しない。CLAUDE.md の `IsSingleByteCodePage` と同じ理由）。net48 は `Encoding.GetEncoding`
- 作れない候補は `null`（外さない）。EUC-TW の 51950 は両 TFM とも作れない（テストで固定）ので、台湾の分岐の結果は変わらない。EUC-KR は CP949 に含まれるので、韓国の分岐も実際には変わらない
- `-DetectionLineCount` は切り出した先頭の行を同じ判定に渡すので、同じ規則になる（`ShortEucJpDetectionTests`）

**既存の判定は変わらなかった（依頼 1 章の「止まる」条件には当たらない）。** コアの既存のテスト（net48 2123 件・net10.0 2113 件、本件のテストを足す前）はすべて通った。
念のため、`tests/EncodingProbe.Tests/TestData` の全 50 ファイルを 6 カルチャー（ja-JP・ko-KR・zh-TW・zh-HK・zh-CN・en-US）× 2 方式（Combined・NativeOnly）で、修正の前後に両 TFM で判定し、600 件すべてが同じだった。

**テスト担当の入力。** 「日本語の文書です。今日は…」の先頭 1〜30 文字（4〜92 バイト）× CRLF・LF・改行なしは、ja-JP で両方式・両 TFM とも 20932（修正の前は CRLF・改行なしの多くが 932）。
逆向き（Shift_JIS の日本語 + LF）はすべて 932 のまま。両方の候補で厳密に復号できる `B0 A1` は、今までどおり改行で決まる（CRLF は 932、LF は 20932）。

### 11.10 テスト

| 件数 | コア net48 | コア net10.0 | モジュール | PSCompat |
|---|---|---|---|---|
| 第五次の仕上げの後 | 2117 | 2107 | 1516 | 1044 |
| 第六次 A の後 | **2139** | **2129** | **1771** | **1135** |
| 増えた数 | 22 | 22 | 255 | 91 |

足したテスト: コアは `SystemCodePagesTests`（6）・`AmbiguityStrictDecodeTests`（16）。モジュールは `Utf7InstanceWriteTests`・`NullValueElementTests`・`SystemCodePageVocabularyTests`・
`DoubleBomReadTests`・`TruncatedTailReadTests`・`Iso2022TailStateTests`・`Iso2022AppendTests`・`CsvNameComparerTests`・`CsvColumnNameComparisonTests`・`ShortEucJpDetectionTests`。
PSCompat は `Sixth/*`（`ProbedCompatScenarios.ps1` の「1.3.0 第六次 A」の節）。改めたテストは `SetProbedContentTests` の 1 件（11.3）。

### 11.11 テスト担当のスクリプトでの確認（2026-10-26）

`scripts\BUG-0018・0020・0021・0031・0032・0033・0037・0042.ps1` と `Common.ps1`・`Invoke-BothHosts.ps1` を作業フォルダー（scratchpad）に写し、
開発中の Debug ビルドで配布フォルダーと同じ構成（`.psd1`・`.psm1`・`core\`・`desktop\`）を作って、`Invoke-BothHosts.ps1` で 7.6.9 と 5.1 で実行した（テスト担当のフォルダーには何も置いていない）。
8 本とも、期待する結果（報告の「期待する結果」）になった。両ホストの標準出力の違いは、ホストの .NET の参考の行（0020 の `String.Equals(…, 'OrdinalIgnoreCase')`）、
7.x だけで試す標準のコマンドの行（0021 の `Export-Csv -Append`）、エラーの表示の形（0032）だけで、モジュールの結果は一致した。

### 11.12 決めきれなかったこと・残したこと

1. **65001 の環境は作っていない**（11.4）。標準のコマンドとの比較が残っている（OS の設定を変えて再起動すれば確かめられる）
2. **Linux の `oem` は標準と違う**（モジュールはカルチャーの OEM コードページ、標準の 7.x は UTF-8）。依頼どおり変えていない
3. **GB18030 の途中で切れた 4 バイトの列は両ホストで違う**（11.6）
4. **HZ の追記先は ASCII に戻さない**（11.7。報告だけ）
5. WSL の Ubuntu-24.04 に入れた `~/.dotnet` と `~/powershell-7.6.9`（と `~/dotnet-install.sh`）は残してある。要らなければ消す
6. 判定の部品の中で、`CsvPropertyViews.GetAdaptedPropertyNames` はホストの規則のまま（11.8）。5.1 で `ǅ` と `ǆ` の両方のプロパティを持つオブジェクト（7.x では作れない）を `Export-ProbedCsv` に渡すと、ホストの規則で先に見つかったほうを使う

### 11.13 直した文書

- 1.3.0 仕様書: 11 章の表・18.1〜18.3 に注記、18.4（ISO-2022 の追記先）と 25 章（第六次で改めた挙動）を追加
- 1.1.0 仕様書 3.3: `ansi` / `oem` の定義に注記
- ヘルプ（5 言語と zh-HK / zh-MO の複製）: `-AllowEncodingChange`（Add・Out・Export）、`-Value`（Set・Add）、読み込みの `-Encoding`（Get・Import。BOM と途中で切れた末尾）、`Get-EncodingProbePlatformInfo` の説明
- README: 「読み書きの細部の修正」と Linux / macOS の `ansi` / `oem` の表
- CHANGELOG: 1.3.0 の「不具合の修正（クラスライブラリ）」「挙動の変更（クラスライブラリ）」「挙動の変更（PowerShell モジュール。第六次 A）」
- CLAUDE.md: 1.3.0 の作業記録の現在の状態

## 12. 第六次 B — ファイルシステムとエラーの扱い（BUG-0025・0026・0027・0028・0030・0035、ACL の時点、文書。2026-10-26）

仕様は 1.3.0 仕様書 26 章、変えた挙動は CHANGELOG の「挙動の変更（PowerShell モジュール。第六次 B）」。
テストに使った版: PowerShell **7.6.9**（.NET 10.0.14）、Windows PowerShell **5.1.26100.9711**（64 ビット。BUG-0030 だけ 32 ビット版も）、Windows 11 Pro 10.0.26300、ja-JP。

### 12.1 利用者の決定（計画の確認のとき）

1. **BUG-0028 は案 A**（読み込み側・書き込み側の両方がエラーを出し、書き込みを元に戻す）。読み込み側でファイルが見つからなかった場合も LocationKey で書き込み中の表を引き、当たれば SamePathRoundTrip にする。比べる箇所・比べない箇所を理由付きで一覧にする
2. **`Resolve-Encoding` のエラーの種類:** 新しく足すエラー（読めない・全体の判定の上限を超える・メモリ不足）は非終了、既存のもの（ファイルが無い・`-DetectionLineCount` の上限を超える）は終了エラーのまま。混ざることをヘルプと仕様書に書く
3. クラスライブラリの `EncodingDetector(string path)` の共有モードは変えない
4. `.psd1` の `ReleaseNotes` は文面だけ直してよい（`publish\` の中で触れるのはこれだけ。`deps.json`・`LICENSE.txt` などには触れない）
5. 計画への指摘: BUG-0025 の Archive 属性は直す（4 つの書き方すべてで付け、元に戻したら元の属性のまま）。BUG-0027 の一時ファイルの名前から書き込み先が分からなくなることを CHANGELOG とヘルプに書く。
   BUG-0035 で `Out-ProbedFile -Width` への既定値が効くことをテストで確かめる

### 12.2 今のビルドの挙動（直す前に確かめたこと）

テスト担当の報告は第五次より前のモジュールで実行したものなので、第五次の後のビルドでスクリプトを両ホストで流した。

| BUG | 第五次の後のビルド |
|---|---|
| 0025 | 隠し・システム属性のファイルに書け、代替データストリームも保つ（1 MiB 以内・超える、4 コマンドと Convert、両ホスト）。**1 MiB を超える上書きで Archive 属性が付かない**（置き換えが元の属性を写す）。`WriteAccessDenied` は .NET の文面 |
| 0026 | 変わらず（`Resolve-Encoding`・追記の 3 コマンドが生の `IOException` の終了エラー） |
| 0027 | 1 MiB 以内は一時ファイルを使わないので、240 文字の名前でも変換できる。1 MiB を超えると今も失敗する |
| 0028 | 1 MiB 以内の上書き・1 MiB を超える置き換えは元のファイルに触れないので、読み込み側は元の内容を読み、z は `a1 z1` になる（エラーにはならない）。写してから直接書く方法と追記は、書きかけを読む |
| 0035 | 変わらず |

**標準の Out-File / Export-Csv（BUG-0025。両ホストで実測、`tools` ではなく作業フォルダーのスクリプト）。** 隠し・システム属性のファイルは、`-Force` の有無によらず `FileOpenFailure` で書けない。代替データストリームを消す（ふつうのファイル）。
`Set-Content` は書けて、ストリームを保ち、Archive を付ける。両ホストで同じ（違いは 5.1 の `Out-File` の既定が UTF-16 で長さが違うことだけ）。

### 12.3 BUG-0025（Archive 属性・WriteAccessDenied のメッセージ）

- `Internal/FileErrors.WriteAccessDenied` で、`WriteAccessDenied` を出す 6 か所（Set / Add の開く時点・書く時点・完了の時点、Out / Export の書く時点・完了の時点・開く時点、Convert）の例外を、5 言語のメッセージ（`MessageKey.WriteAccessDenied`）の `LocalizedAccessDeniedException` に包み直した。
  内部例外は持たず、HRESULT は引き継ぐ。読み取り専用の検査の 3 か所は、もともと 5 言語のメッセージ（`FileIsReadOnly`）
- `Internal/FileAttributeKeeper`: 書き込みに成功したら Archive 属性を付ける（`SafeFileOverwrite.Commit`・追記の `ProbedFileWriter.Complete`。追記は 1 バイト以上書いた場合だけ）。
  元に戻したら、書き込みの前の属性に戻す（メモリの写し・写しからの書き戻し・追記の切り詰めの後。書き戻しでファイルシステムが Archive を付けるため）。読み取り専用は `ReadOnlyAttributeScope` が戻すので触れない。Windows 以外では何もしない
- テスト `ArchiveAttributeAndAccessDeniedTests`（7 コマンド × 大きさ × ハードリンク × 成功・失敗、隠し・システム＋ストリーム、ACL の拒否のメッセージ）。PSCompat `SixthB/Archive/*`・`SixthB/WriteAccessDenied/*`
- **テストの干渉（直した）:** このテストの写してから直接書く場合は一時フォルダーに写しを作るので、写しが残っていないかを一時フォルダー全体で調べる `LinkAndPermissionWriteTests` と並行に動くと、時々そちらが落ちた（全体を流したときだけ）。
  並行に動かさないコレクション（`OverwriteStateCollection`）に入れた（BUG-0027 のコミット）

### 12.4 BUG-0026（ほかのプログラムが開いているファイル）

**ファイルを開いている箇所の一覧（依頼の報告の項目）。**

| 箇所 | 用途 | 共有モード（第六次 B） | 開けないとき |
|---|---|---|---|
| `ProbedFileReader.Open` | Get-ProbedContent・Import-ProbedCsv・Export -Append のヘッダー | ReadWrite（変えない） | ReadFailed / ReadAccessDenied（Get・Import は非終了、Export は終了） |
| `HeadLineCutter.Cut`・`StrictDecodeVerifier.Verify` | -DetectionLineCount の切り出し・全体の検証 | ReadWrite（変えない） | 各コマンドの ReadFailed（Resolve は非終了、Import は非終了、Export・-EncodingFrom は終了） |
| `SharedFileReader.ReadAll`（新） | Resolve-Encoding・`EncodingInheritance.FromFile`（書き込み先・追記先・-EncodingFrom の判定）・Convert の変換元 | **ReadWrite（Read から変えた）** | ReadFailed / ReadAccessDenied（Resolve・Set・Add・Convert は非終了、Out・Export・-EncodingFrom は終了） |
| `HeadLineDetection`（0 バイト） | 0 バイトのファイル | クラスライブラリの Detect(path)（Read）をやめ、切り出した 0 バイトを渡す | — |
| Export の `ReadBomLength`・`EndsWithLineBreak` | 追記先の BOM・末尾 | ReadWrite（変えない） | ReadFailed（終了） |
| `Iso2022TailState` | ISO-2022 の追記先の末尾の状態 | ReadWrite\|Delete（変えない） | WriteFailed（開く時点） |
| `FileIdentity`（CreateFileW、属性の読み取り） | 同じファイルの判定 | すべて許す（属性だけを読むハンドルは共有の検査の対象外） | 開けなければパスの文字列から作った鍵 |
| `ProbedFileWriter`（追記の FileStream） | 追記 | Read（変えない） | WriteFailed（共有違反は 5 言語のメッセージ） |
| `SafeFileOverwrite`（一時ファイル・直接書く・写しの `File.Copy`・`EnsureWritable`） | 上書き | Read / EnsureWritable は ReadWrite\|Delete（変えない） | WriteFailed / WriteAccessDenied |
| クラスライブラリの `EncodingDetector(string)` | 公開 API | Read（変えない。利用者の決定） | — |

- `Resolve-Encoding` は、`Detect(path)` ではなく、モジュールで読んだバイト列を `Detect(bytes)` に渡す。テストデータ 50 ファイル × 8 カルチャー × 3 方式の 1200 件で、両 TFM とも結果が同じであることを確かめた（UTF.Unknown にファイルではなくバイト列を渡すことになるため）
- `SharedFileReader` は、バッファを持たずに長さちょうどを読む。最初は 4 KiB のバッファで読み、ほかのハンドルが末尾より先をロックしている場合に失敗した（`SmallFileDirectWriteTests` が検出。`File.ReadAllBytes` は長さちょうどを読む）
- 読み込みの失敗は `FileErrors.ReadFailed`（共有違反・ロック違反は `FileInUseForRead`、それ以外は `FileReadFailed` でエラー コードを示す）・`ReadAccessDenied` の 5 言語のメッセージ。書き込みの `WriteFailed` は、共有違反・ロック違反だけを `FileInUseForWrite` に包み直す（それ以外は .NET の文面のまま。残した点）
- テスト担当のスクリプト: `Get-ChildItem *.csv | Resolve-Encoding` は 3 件とも結果が出る。Convert -Destination は変換できる。追記の 3 コマンドは `WriteFailed`（5 言語のメッセージ）。7.x の標準の `Add-Content` は書く、5.1 の標準は `GetContentWriterIOError`

### 12.5 BUG-0027（一時ファイルの名前）

- `SafeFileOverwrite.CreateTemporaryPath` の名前を `.~probed<8 桁>.probedtmp`（26 文字）にした（`TemporaryPrefix`）。`CreateNew` で作れなかったときに同じ名前のファイルがあれば、5 回まで作り直す
- 一時ファイルを作るときに `PathTooLongException`・`ERROR_FILENAME_EXCED_RANGE`・`ERROR_INVALID_NAME`・（フォルダーが実在するのに）`DirectoryNotFoundException` になったら、既存のファイルは写してから直接書く方法、まだ無いファイルは直接作る方法に切り替える
- 確かめたこと: 7.x では長いパスを .NET が扱うので、名前 218〜250 文字の 1 MiB を超える上書きが置き換えで書ける（`TemporaryFileNameTests`）。
  5.1 では、240 文字のフォルダーの `a.txt`（パス 246 文字、一時ファイル 267 文字）が書けることを PSCompat で、**切り替えを一時的に外したビルドでは `WriteFailed` になることも確かめた**
- `TemporaryFileRegistry` はパスで登録しているので変更は要らない。テスト・PSCompat の `*.probedtmp` の検索もそのまま使える

### 12.6 BUG-0028（逆向きの往復。案 A）

**設計（承認した案 A）。** 1.3.0 仕様書 26.4。`Internal/ActiveWriteRegistry`（スレッド単位）に、`ProbedFileWriter` が開いた時点で書き込み先の実体（追記は開いたハンドル、上書きはパスの実体。まだ無ければ親フォルダーと名前）と「元に戻して止める」処理を登録し、完了・元に戻す・閉じるで外す。

- 読み込み側（`ProbedContentCommandBase.ReportIfBeingWritten`）: `Get-ProbedContent`・`Import-ProbedCsv` は `ResolveExistingFiles` で、ファイルがある場合は読む直前に、無い場合は「ファイルが無い」の代わりに比べる。
  `Convert-ProbedContent` は変換を始める時点で比べる。当たれば書き込み側を元に戻させ（読み込み側はまだ開いていないので、新しいファイルも消せる）、非終了エラー `SamePathRoundTrip`（`SamePathReadWhileWriting`）
- 書き込み側: `ProbedFileWriter.AbortedByRoundTrip`。Set / Add は次の入力か EndProcessing で非終了エラー、Out / Export は次の行か完了の時点で終了エラー（`SamePathWriteCancelled`）。書き込み側のエラーは、読み込み側の呼び出しの中では書けない（WriteError は自分の処理の中でしか呼べない）ので後になるが、ファイルはその時点で戻っている
- 比べる箇所・比べない箇所と理由は仕様書 26.4 の表（Get・Import・Convert は比べる。-EncodingFrom・書き込み側の自分の書き込み先の読み取り・Resolve-Encoding は比べない）

**標準の挙動（両ホストで実測。各ケースを別のジョブで実行し、10 秒で打ち切り）。**

| パイプライン | 7.x | 5.1 |
|---|---|---|
| `Get-Content a,z \| Out-File z` | 止まらずに増え続ける（10 秒で 1.2 MB） | 同じ（0.9 MB） |
| `… \| Set-Content z`・`-Raw \| Set-Content z` | `z1` だけ（a1 が消える） | 同じ |
| `… \| Add-Content z` | `z1 a1 z1` | `GetContentWriterIOError`、`z1` のまま |
| `Import-Csv a,z \| Export-Csv z` | `a1` だけ | 止まらずに増え続ける |
| `… \| Export-Csv z -Append` | `z1 a1 z1` | 止まらずに増え続ける |
| `gci *.txt \| Get-Content \| Out-File z` | 増え続ける | 増え続ける |
| 対照: `Get-Content z,a \| Out-File z` | `a1` | 同じ |

**結果。** `ReverseRoundTripTests`（6 つの書き込み × 4 つの読み込み × 大きさ × 既存・ハードリンク・まだ無い、130 件）で、両方が SamePathRoundTrip、ファイルは元のまま（まだ無いファイルは残らない）、一時ファイルも残らず、書き込み中の表が空になることを確かめた。
1 MiB を超えるまだ無いファイルは、読み込み側で「ファイルが無い」の代わりに LocationKey で当たる。PSCompat `SixthB/ReverseRoundTrip/*`（24 件）。テスト担当のスクリプトの 4 つのパイプラインは、両ホストとも SamePathRoundTrip が 2 件、z は元のまま

### 12.7 BUG-0030（大きなファイル・メモリ不足）

- `EncodingInheritance.MaxDetectableLength` を、テストのために内部で変えられる静的なプロパティにした（既定 `DefaultMaxDetectableLength` = 0x7FFFFFC7）
- `Resolve-Encoding` は `SharedFileReader.ReadAll` で読む前に大きさを調べ、上限を超えれば `FileTooLargeToDetectUseLineCount` の非終了エラー（`EncodingDetectionFailed`）
- `EncodingDetectionException.GuardMemory`: 全体を読む判定（`ProbedFileReader`・`EncodingInheritance.FromFile`・`Resolve-Encoding`・`HeadLineDetection`）の `OutOfMemoryException` を `NotEnoughMemoryToDetect` にする。
  `Resolve-Encoding -DetectionLineCount` でも、メモリ不足は非終了（新しいエラーのため）
- `Convert-ProbedContent`: 上限を超えれば読む前に `FileTooLargeToConvert`、読み込み・変換のメモリ不足は `NotEnoughMemoryToConvert`（LimitsExceeded の非終了）
- テスト用の差し込み口 `SharedFileReader.BeforeAllocateForTests`（全体を読む配列を作る直前に、大きさを渡して呼ぶ）で OutOfMemoryException を起こして確かめた（`LargeFileAndMemoryTests`）。PSCompat `SixthB/TooLarge`（リフレクションで上限を 5 バイトにする）
- **32 ビット版の 5.1 でのテスト担当のスクリプト**（600 MB と 2 GB + 1 バイトのファイル。空きメモリ約 3.9 GB を確かめてから最後に実行した）

  | ファイル | コマンド | 第五次の前（報告） | 第六次 B |
  |---|---|---|---|
  | 600 MB | `Resolve-Encoding` | 生の OutOfMemoryException | **932 で成功**（258 秒） |
  | 600 MB | `Convert-ProbedContent -Destination` | 生の OutOfMemoryException | `NotEnoughMemoryToConvert` |
  | 600 MB | `Resolve-Encoding -DetectionLineCount 10` | 20127 | 20127（37 ミリ秒） |
  | 2 GB + 1 | `Get-ProbedContent -TotalCount 1` | `EncodingDetectionFailed` | 同じ |
  | 2 GB + 1 | `Resolve-Encoding` | 生の OutOfMemoryException | `EncodingDetectionFailed`（上限の案内。26 ミリ秒） |

  600 MB の `Resolve-Encoding` が成功するようになったのは、クラスライブラリの `Detect(path)`（ファイルを `MemoryStream` に写してから配列にする）を使わなくなり、ファイルの大きさの配列を 1 つだけ作るようになったため。
  約 3.6 倍のメモリを使う点（1.4.0 で扱う）は直していない。PowerShell 7.x と 64 ビット版の 5.1 では、大きなファイルを作る測定はしていない（上限を小さくした単体テストと PSCompat で確かめた）

### 12.8 BUG-0035（内部で呼ぶコマンド）

**内部で呼ぶコマンドの一覧（依頼の報告の項目）。** 本モジュールが内部で呼ぶ PowerShell のコマンドは `Out-ProbedFile` の `Microsoft.PowerShell.Utility\Out-String -Stream` だけ（`ScriptBlock.Create`・`InvokeCommand`・`InvokeScript` をほかに使っていない）。

| 利用者の状態 | 影響 | 扱い |
|---|---|---|
| `$PSDefaultParameterValues['Out-String:…']`（`Out-String:*` を含む） | Out-ProbedFile の結果が変わっていた | **直した**（新しいスコープで空のローカル変数にしてから組み立てる） |
| `$PSDefaultParameterValues['Out-ProbedFile:Width']`・`'*:Width'` | 本コマンドの -Width に入る | 今までどおり効く（`'*:Width'` は Out-File にも効く）。テストで固定 |
| 同名の関数・別名 `Out-String` | 影響しない | モジュール修飾名で呼ぶ（1.2.0 から） |
| `$FormatEnumerationLimit`・書式データ・型データ | 整形が変わる | Out-File も同じ影響を受けるので、そのまま |
| PSDrive・現在の場所（パスの解決） | 解決されるパス | 標準と同じ（意図どおり） |
| `-Value` の文字列化（型データの ToString） | 書く文字列 | 標準の Set-Content と同じ（そのまま） |

- 幅を計算して `-Width` を渡す方法は採らなかった。5.1 のコンソールでは `-Width` 省略時の幅が `BufferSize.Width - 1`（バッファー 120 で 119）、7.x は 120、ホストの UI の無いランスペースは両方 120 で、計算で再現すると今の Out-File との一致を崩すおそれがある
- スクリプトブロックの引数（`param($PSDefaultParameterValues)`）で隠す方法は効かなかった（ステッパブルパイプラインはスクリプトブロックのスコープを作らない）。`& { begin { … } process { … } end { … } }` の中で内側のステッパブルパイプラインを組み立てると効く（両ホストで確かめた）
- テスト `OutStringDefaultParameterTests`（5 件）。PSCompat `SixthB/DefaultParameterValues/*`

### 12.9 ACL で拒否されたファイルの時点（前回の引き継ぎ）

`SafeFileOverwrite.Begin` で、1 MiB 以内の既存のファイルにも `EnsureWritable` を呼ぶ（1 MiB を超える場合と同じく、書き込み用に切り詰めずに、すべての共有を許して開き、すぐ閉じる。共有違反は無視）。
テスト `SmallFileDirectWriteTests.WriteDataDenied_IsReportedAtOpen_RegardlessOfSize`（パイプラインの 2 件目を処理する前に止まる）は、この変更を外すと 1 MiB 以内の 2 件が落ちることを確かめた。
ほかのハンドルが開いているときに失敗する条件（実装記録 10.16 節の表）が変わっていないことは、PSCompat の既存のシナリオ（`Finish/DirectWrite/OpenedByAnother/*`）の値が変更の前後で同じことで確かめた。

### 12.10 文書の食い違い（第六次 A の分）

- 1.3.0 仕様書 1.1 の表に BUG-0031・0032 の 2 行、1.2 を「次の 6 点」（5・6 を追加）、1.3 を「1.2 の 6 点だけ」にした
- `PackageReleaseNotes`（クラスライブラリ）と `.psd1` の `ReleaseNotes` には、第六次 A の内容が入っていなかった。どちらも足した（`.psd1` は B の内容も。`publish\` の中で直したのは `.psd1` の文面だけ）

### 12.11 テストと確認の結果

| 件数 | コア net48 | コア net10.0 | モジュール | PSCompat |
|---|---|---|---|---|
| 第六次 A の後 | 2139 | 2129 | 1771 | 1135 |
| 第六次 B の後 | **2139** | **2129** | **2002** | **1205** |
| 増えた数 | 0 | 0 | 231 | 70 |

足したテスト: `ArchiveAttributeAndAccessDeniedTests`・`OpenedByAnotherProgramTests`・`TemporaryFileNameTests`・`LargeFileAndMemoryTests`・`OutStringDefaultParameterTests`・`ReverseRoundTripTests`・`SmallFileDirectWriteTests` に 4 件。
PSCompat は「1.3.0 第六次 B」の節。クラスライブラリは変えていない。

テスト担当のスクリプト（BUG-0025・0026・0027・0028・0035 を両ホスト、BUG-0030 を 32 ビット版の 5.1）を、作業フォルダーに作った配布の構成で実行し、期待どおりになった。
両ホストの標準出力の違いは、0026 の標準の Add-Content の行と、0035 のコンソールの空行だけ。

**時々起きた PSCompat の失敗（直した）。** 5.1 の `SixthB/Archive/*` の 1 MiB を超えるシナリオが、2 回（別のシナリオで）「Stream was not readable.」になった。
どちらも、モジュールのコマンドの外側（シナリオの中の標準の `Set-Content -Stream` / `Get-Content -Stream`）で起きていた（モジュールのエラーは `Invoke-F2` が ID に変える）。
シナリオは `Zone.Identifier`（ZoneId=3。インターネットから来た印）を付けた 1 MiB を超えるファイルを作っており、ウイルス対策の検査と重なったときに 5.1 の標準の `-Stream` が失敗したと考えられる
（同じ操作を 5.1 で 75 回繰り返しても再現しなかった）。シナリオのストリームの名前を `probedmeta` に変えた後、PSCompat の全体を 3 回流して一度も起きていない。
net10.0 の単体テストは、BUG-0025 の主題なので `Zone.Identifier` のままにした（12.12 の 4）。

### 12.12 決めきれなかったこと・残したこと

1. **追記は、ほかのプログラムが書き込み用に開いている間は書けない**（今までどおり。7.x の標準の Add-Content は書く）。交互に書いて壊さないためと、取り消し（切り詰め）がほかのプログラムの書いた分を消すおそれがあるため、変えていない
2. `WriteFailed` は、共有違反・ロック違反以外（ディスクの空き不足など）は .NET の文面のまま
3. 書き込みのコマンドどうしの衝突（同じパイプラインの 2 つの書き込みのコマンドが同じファイルに書く）は、SamePathRoundTrip の対象にしていない
4. 12.11 の PSCompat の失敗の原因は見立てで、確かめていない（ウイルス対策の検査との重なり。シナリオのストリームの名前を変えて避けた）
5. `Resolve-Encoding` のエラーの種類が混ざる（利用者の決定。ヘルプと仕様書に書いた）
6. 一時ファイルの名前から書き込み先が分からない（既知の限界。CHANGELOG とヘルプに書いた）
7. 全体の判定がファイルの約 3.6 倍のメモリを使う（1.4.0）
8. 標準の Out-File / Export-Csv は隠しファイルに書けず、ストリームを消すが、Out-ProbedFile / Export-ProbedCsv は書けて保つ（標準と違う。受け入れた）

### 12.13 直した文書

- 1.3.0 仕様書: 1.1〜1.3（12.10）、16.4・16.5 の注記（一時ファイルの名前、ACL の時点、逆向きの往復）、26 章（第六次 B で改めた挙動）
- ヘルプ（5 言語と zh-HK / zh-MO の複製）: Set・Out・Convert・Export の注意事項（一時ファイルの名前と、名前から書き込み先が分からないこと、長いパスの切り替え、Archive 属性）、Resolve-Encoding の注意事項（エラーの種類が混ざること・ほかのプログラムが開いているファイル）
- README（読み書きの細部の修正に追記）、CHANGELOG（挙動の変更。第六次 B）、`PackageReleaseNotes`、`.psd1` の `ReleaseNotes`、CLAUDE.md

## 13. 第六次 C — パラメーター・メッセージ・文書（BUG-0038・0039・0041・0045、WriteFailed、文書の BUG、文書のそろえ直し。2026-10-26）

仕様は 1.3.0 仕様書 27 章、変えた挙動は CHANGELOG の「挙動の変更（PowerShell モジュール。第六次 C）」。第六次の最後の回。
テストに使った版: PowerShell **7.6.9**（.NET 10.0.14）、Windows PowerShell **5.1.26100.9711**（64 ビット）、Windows 11 Pro 10.0.26300、ja-JP。

### 13.1 利用者の決定（計画の確認のとき）

1. BUG-0045 (c) の中国語は、案のとおり語順だけを直す（「以前 N 行」が「以前は」と読めるのを直す）。「以…而言」はそのままにし、「以→對（对）」の見直しは残したことに入れる
2. BUG-0045 (d) の英語は、行数による出し分けではなく言い換えにする。ただし先頭だけを見たことが分かる言い方を残し、3 つのメッセージで表現をそろえる（「the beginning of the file (with -DetectionLineCount N)」）
3. BUG-0045 (a) で `-Header` の値が重複した場合は、「'…' のヘッダー」とは書かず、`-Header` の値が重複していると分かる文にする（5 言語）。どちらの場合も、ID（`AlreadyPresentPSMemberInfoInternalCollectionAdd`）・分類・対象が今までと変わらないことをテストで確かめる
4. BUG-0039 は PSCustomObject 全体を対象にする。判定結果と関係の無い PSCustomObject（`[pscustomobject]@{ A = 1 }`）でも不自然にならない文にする

### 13.2 BUG-0038（`-Culture` の前後の空白）

- `ResolveEncodingOptions.ValidateCulture` が `TrimCulture`（`string.Trim()`）の後の名前で形を調べ、`ProbedContentCommandBase`・`ResolveEncodingCmdlet` は除いた名前を判定に渡す
- メッセージと対象（`TargetObject`）には渡された元の値を出す（`' ko KR '` → `InvalidCulture`、対象は `' ko KR '`）。空白だけの値は今までどおり省略と同じ（`IsNullOrWhiteSpace`）
- 列挙型（`-Bom`・`-LineBreak`・`-UseQuotes`）は、PowerShell の型変換が前後の空白を除く（両ホストで確かめた: `[IO.FileAttributes]' Hidden '`）。前後の空白を除くパラメーターの一覧は仕様書 27.1
- テスト: `CultureValidationTests`（`SurroundingWhitespace_IsTrimmed`・`InnerWhitespace_IsStillRejected_WithOriginalValue`）、PSCompat `SixthC/CultureTrim/*`

### 13.3 BUG-0039（逆シリアル化した判定結果）

- `EncodingVocabulary.Resolve` で、`PSObject` を外した値が `PSCustomObject` なら `EncodingObjectNotUsable`（型名は `PSObject.TypeNames[0]`）を投げる。ID は今までどおり `ParameterArgumentTransformationError`
- 逆シリアル化した `EncodingInformation`（`Import-Clixml`・`Receive-Job`）は、両ホストで `PSCustomObject` を元にした `PSObject` だった（5.1 でも同じ。ジョブで確かめた）
- 文は「文字エンコーディングとして渡された値は '<型名>' のオブジェクトで、文字エンコーディングとして使えません。EncodingInformation オブジェクト（Resolve-Encoding の結果）はそのまま渡せますが、…を通した判定結果は、EncodingInformation オブジェクトではなくなります。その場合は、…」。
  判定結果と関係の無い PSCustomObject でも、前半（型名と使えないこと）が当てはまり、後半は「その場合は」で判定結果の場合に限った案内になる
- 案内する渡し方: `CodePage` の値（読み込み）と、BOM を明示した統一語彙名（書き込み。数値の Unicode のコードページは BOM が決まらないので書き込みで拒否される）
- `-Encoding` の解決はパラメーター名を知らないので、メッセージでは「文字エンコーディングとして渡された値」と書いた（`-SourceEncoding` にも同じ文が出る）
- テスト: `EncodingObjectNotUsableTests`（5 つの形 × 4 つのコマンド、案内した渡し方が通ること）、PSCompat `SixthC/EncodingObject/*`

### 13.4 BUG-0041（`-TotalCount` の別名）

- `[Alias("Head", "First")]`。ヘルプの `aliases` 属性を 5 言語で `Head, First` にし、既定値（`dev:defaultValue`。5 言語で同じ値）を「None (all lines)」にした
- テスト: `GetProbedContentTests.Read_TotalCountAliases`、PSCompat `SixthC/TotalCount/*`

### 13.5 BUG-0045（メッセージの細部）

- **(a)** 移植したコード（`Internal/Csv/ImportCsvHelper.ValidatePropertyNames`）は変えず、新しい `Internal/CsvDuplicateColumnName` で、コマンドが受け止めた `ExtendedTypeSystemException` のメッセージだけを差し替える。
  重複した列名は `helper.Header` から同じ比較器（`CsvNameComparer`）で求め直す（移植元と同じく空の列名は数えない）。`-Header` を指定したかどうか（`this.Header != null`）で文を選ぶ。
  例外の型は同じなので、ID・分類（NotSpecified）・対象（パス）は変わらない。`Export-ProbedCsv -Append` の追記先のヘッダーも同じ部品でパスを示す
- **(b)** 追記の整合性検査の相手を `Encoding` から `EncodingSpec`（BOM の有無を持つ）にし、`AppendConsistency.DescribeForMessage` で `GetUnifiedName(コードページ, BOM)` にする。
  BOM の方針が決まっていない Unicode 系（`Add-ProbedContent` に裸の `unicode` など）は WebName（`utf-16`）で示す（BOM を付けない名前にすると、指定と違って見えるため）
- **(c)** 繁体字: 「根據前 {2} 行判斷 '{0}' 的字元編碼為 '{1}'，但檔案含有以字元編碼 '{1}' 而言無效的位元組序列（位元組位置 {3}：{4}），因此未處理。…」。簡体字も同じ形
- **(d)** 英語のメッセージ 65 件をすべて調べた。数を入れて名詞の単数・複数が変わるのは、`-DetectionLineCount` の 3 件（`first {n} lines`）だけだった（`matches {1} files` は 2 以上のときだけ出る）
- テスト: `MessageDetailTests`、既存の `AddProbedContentTests.Append_EncodingChange_ReportsBothEncodings` の期待を `utf-8` から `utf8NoBOM` に改めた。PSCompat `SixthC/DuplicateColumn/*`・`SixthC/EncodingChangeOnAppend/*`・`SixthC/DetectionVerificationFailed/OneLine`

**(d) で直した英語の文:**

| キー | 前 | 後 |
|---|---|---|
| `DetectionLinesTooLarge` | from its first {1} lines, because they are larger than the detection limit | from the beginning of the file (with -DetectionLineCount {1}), because the lines to read are larger than the detection limit |
| `DetectionVerificationFailed` | which was detected from its first {2} lines | which was detected from the beginning of the file (with -DetectionLineCount {2}) |
| `DetectionVerificationIso2022Escape` | although its first {2} lines contain only ASCII characters and were read as '{1}' | although the beginning of the file (with -DetectionLineCount {2}) contains only ASCII characters and was read as '{1}' |

### 13.6 WriteFailed のメッセージ

- `FileErrors.WriteFailed` を `ReadFailed` と同じ形にし、共有違反・ロック違反以外は `FileWriteFailed`（「'{0}' に書き込めませんでした（エラー コード {1}）。」）。内部例外は持たせず、HRESULT を引き継ぐ
- ディスクの空き不足はテストで起こせないので、HRESULT を指定した `IOException` を直接渡して確かめた（`FileErrorsTests`）。PSCompat のシナリオは作っていない

### 13.7 文書だけの BUG（挙動は変えていない）

**BUG-0022 の標準の挙動（両ホストで実測）:** 列名が重複した追記先への `Export-Csv -Append` は、7.6.9 と 5.1 のどちらも `RuntimeException`（NotSpecified）。

**BUG-0036 の `0.1 + 0.2`（依頼と違った点）:** 依頼は「0.1+0.2 が 5.1 で 0.3・7.x で 0.30000000000000004」を `Set-` / `Add-ProbedContent` の既知の限界とするものだったが、
実測では `Set-ProbedContent -Value (0.1 + 0.2)` は**両ホストで `0.3`**（PowerShell の型変換）だった。違うのは `Export-ProbedCsv`（値の `ToString()`）で、5.1 が `0.3`、7.x が `0.30000000000000004`。
そのため、`0.1 + 0.2` は 1.3.0 仕様書 17 章（`Export-ProbedCsv`）の既知の限界に書き、1.1.0 仕様書 5.8 には「`Set-` / `Add-ProbedContent` では両ホストで `0.3`、`Export-ProbedCsv` では違う」と書いた。
`-0.0` は両方のコマンドで 7.x が `-0`、5.1 が `0`。

**BUG ごとの書いた場所:**

| BUG | 書いた場所 |
|---|---|
| 0022 | 1.3.0 仕様書 20 章の表（事象・時点・ID）、21 章の差異、27 章の冒頭、CHANGELOG |
| 0023 | 1.3.0 仕様書 18.3（既知の限界。1.4.0 で検討）、`Export-ProbedCsv -Append` のヘルプ（5 言語と複製）、動作確認手順書 3.3、CHANGELOG |
| 0029 | 1.3.0 仕様書 1.5・14・17・21 章、`Import-ProbedCsv` / `Export-ProbedCsv` の `-UseCulture` のヘルプ、README（1.3.0 の CSV の節）、`.psd1`、動作確認手順書 3.6・4.8、CHANGELOG |
| 0034 | 1.1.0 仕様書 3.6、書き込みのヘルプ（`Set-` / `Add-ProbedContent` の注意事項、`Out-ProbedFile` / `Export-ProbedCsv` の `-Encoding`）、README（知っておく必要のある点）、`.psd1`、動作確認手順書 4.8、CHANGELOG |
| 0036 | 1.1.0 仕様書 5.8（標準との差異・既知の限界）、1.3.0 仕様書 17 章（`Export-ProbedCsv` の数値の表記）、`Set-` / `Add-ProbedContent` の `-Value` のヘルプ、CHANGELOG |
| 0040 | 1.1.0 仕様書 4.6（標準との差異）、`Get-ProbedContent -Raw` のヘルプ、動作確認手順書 4.8、CHANGELOG |
| 0043 | 1.2.0 仕様書 13.1、`Convert-ProbedContent` の注意事項（`-Encoding` と `-Bom`）のヘルプ、動作確認手順書 4.8、CHANGELOG |
| 0044 | 1.1.0 仕様書 4.4（7 ビットの文字エンコーディング。`ascii` / `us-ascii` の名前の不統一も）、`-Strategy` のヘルプ（判定する 8 コマンド）、動作確認手順書 4.8、CHANGELOG |
| 0008 の追記 | README の 1.2.0 の節（`Out-ProbedFile` の例の注釈）、1.2.0 仕様書 2.3、CHANGELOG |
| 0038 | `-Culture` のヘルプ（25 か所）、1.3.0 仕様書 27.1、README、`.psd1`、動作確認手順書 4.6、CHANGELOG |
| 0039 | `-Encoding` のヘルプ（25 か所）、1.3.0 仕様書 27.2、README、`.psd1`、動作確認手順書 4.6、CHANGELOG |
| 0041 | `-TotalCount` のヘルプ（別名・既定値・説明）、1.1.0 仕様書 4.1・4.5・4.6、1.3.0 仕様書 27.3、README、`.psd1`、動作確認手順書 4.6、CHANGELOG |
| 0045 | 1.3.0 仕様書 12 章・20 章・27.4、README、`.psd1`、動作確認手順書 4.7、CHANGELOG |
| WriteFailed | 1.3.0 仕様書 26.2・27.5、README、`.psd1`、CHANGELOG |

### 13.8 文書のそろえ直し（第六次 A・B・C）

テスト担当の `regression-plan.md` の「文書で解決とする条件（見る所）」を、第六次 A・B・C の BUG について確かめた（ブログは対象外）。
挙動で直した BUG は、見る所に書くことは必須ではないが、文書が挙動と食い違っていないかを確かめた。足りなかった所を足した:

| BUG | 足りなかった所 | 書いたこと |
|---|---|---|
| 0028 | ヘルプ、1.3.0 仕様書 19 章 | 逆向きの往復も `SamePathRoundTrip`（書き込みを取り消す）（書き込みの 4 コマンドと Convert の同じファイルの判定の注意事項、19 章の表） |
| 0030 | ヘルプ（32 ビット版のメモリの目安） | 全体の判定は約 3.6 倍のメモリを使い、32 ビット版の 5.1 では数百 MB で `NotEnoughMemoryToDetect` になりうる（`Resolve-Encoding`）。`Convert-ProbedContent` の上限と `NotEnoughMemoryToConvert` |
| 0035 | 1.2.0 仕様書 2.2 | 内部の `Out-String` に利用者の既定値が効かないこと |
| 0036・0040 | 1.1.0 仕様書の標準との差異 | 1.1.0 仕様書には標準との差異の一覧が無かったので、4.6（`Get-Content`）と 5.8（`Set-Content` / `Add-Content`）を作った |

ほかの BUG の見る所（A・B の分）:

| BUG | 書かれている所 |
|---|---|
| 0018 | 1.3.0 仕様書 18.1・18.3・18.4、追記の 3 コマンドのヘルプ、README、CHANGELOG |
| 0020 | 1.3.0 仕様書 18.2・25.3、README、CHANGELOG（両ホストで同じにしたので「ホストに従う」とは書かない） |
| 0021 | 1.3.0 仕様書 18.2・25.1、`-Encoding` のヘルプ（先頭の 1 個だけ取り除く）、README、CHANGELOG |
| 0025 | 1.3.0 仕様書 26.1、書き込みのヘルプの注意事項（Archive 属性）、README、CHANGELOG（隠しファイルに書け、ストリームを保つので 1.1.0 仕様書の差異には書かない） |
| 0026 | 1.3.0 仕様書 26.2、`Resolve-Encoding` のヘルプ、README、CHANGELOG |
| 0027 | 1.3.0 仕様書 26.3、書き込みのヘルプの注意事項（一時ファイルの名前・長いパス）、README、CHANGELOG |
| 0031 | 1.3.0 仕様書 25.7、README、CHANGELOG（直したので `-Culture` のヘルプの既知の限界には書かない） |
| 0032 | 1.1.0 仕様書 3.3、1.3.0 仕様書 25.5、ヘルプ、README（Linux の実測値も）、CHANGELOG |
| 0033 | 1.3.0 仕様書 25.4、`-Value` のヘルプ、1.1.0 仕様書 5.8 の表 |
| 0037 | 1.3.0 仕様書 25.6、1.1.0 仕様書 3.3 の注記、CHANGELOG |
| 0042 | 1.3.0 仕様書 25.2、`-Encoding` のヘルプ（途中で切れた末尾）、README、CHANGELOG |

### 13.9 動作確認手順書

`docs/EncodingProbe-1.3.0-動作確認手順書.md` に 4 章「第六次の後 — 改めた挙動（A・B・C）」を足し、もとの 4・5 章を 5・6 章にした。
`ansi` / `oem`（地域の形式を変える手順も）、BOM が 2 個続くファイル・途中で切れたファイル、JIS の追記、逆向きの往復、属性・一時ファイル・ほかのプログラムが開いているファイル、
第六次 C のパラメーター・メッセージ・文書に書いた点。期待値は両ホストで確かめた（コンソールの文字化けを除いて期待どおり）。
3.3 に引用符が閉じていない追記先の既知の限界、3.6 にホストで違いうる書式と区切り文字を足した。

### 13.10 テストと確認の結果

| 件数 | コア net48 | コア net10.0 | モジュール | PSCompat |
|---|---|---|---|---|
| 第六次 B の後 | 2139 | 2129 | 2002 | 1205 |
| 第六次 C の後 | **2139** | **2129** | **2038** | **1234** |
| 増えた数 | 0 | 0 | 36 | 29 |

足したテスト: `EncodingObjectNotUsableTests`・`MessageDetailTests`・`FileErrorsTests`、`CultureValidationTests` に 2 件、`GetProbedContentTests` に 1 件。PSCompat は「第六次 C」の節。クラスライブラリは変えていない。

テスト担当のスクリプト 13 本（BUG-0008・0022・0023・0029・0034・0036・0038・0039・0040・0041・0043・0044・0045）を、作業フォルダーに作った配布の構成で両ホストで実行した。
BUG-0029 は 2 回流した（2 回目で他方のホストのファイルを読む）。BUG-0008 は作業フォルダーの `docs\repo\` に今の README・CHANGELOG を写して流した。

| BUG | 結果 |
|---|---|
| 0038 | 両ホストで `' ko-KR'`・`'ko-KR '` も受け付ける |
| 0039 | 両ホストで `Import-Clixml`・`Receive-Job`・`Select-Object *` が新しいメッセージ（型名と渡し方） |
| 0041 | `-Head 2`・`-First 2` が `a,b`、`-TotalCount -1` は両ホストでエラー、ヘルプの既定値は `None (all lines)` |
| 0045 | 4 言語 × (a)(b)(c)(d) が直った文。両ホストで同じ文面 |
| 0008 | (1)(4)(5) の文書は直っている（(4) の行はスクリプト自身が .NET の相対パスを試すもの）、5.1 の `Os.Description` に末尾の空白が無い |
| 0022・0023・0029・0034・0036・0040・0043・0044 | 挙動は報告のとおり（文書だけで扱った）。0029 は 5.1 が `;`、7.x が `,`、他方のファイルを読むと列が分かれない |

### 13.11 決めきれなかったこと・残したこと

1. **PackageReleaseNotes（クラスライブラリ）は変えていない。** 依頼は「PackageReleaseNotes と `.psd1` の ReleaseNotes に第六次 C を足す」だったが、PackageReleaseNotes はクラスライブラリ（NuGet パッケージ）だけの文で、第六次 C はクラスライブラリを変えていないため。`.psd1` には足した
2. 中国語の「以…而言」を「對…而言」（对…而言）にそろえるかは見直していない（`InvalidSourceBytes`・`InvalidBytesDuringImport`・`DetectionVerificationFailed`。中国語の話者の確認が要る）
3. BUG-0036 の `0.1 + 0.2` は依頼と実測が違ったので、実測に合わせて書いた（13.7）
4. BUG-0023（引用符が閉じていない追記先）、BUG-0043（BOM だけのファイル）、BUG-0044（HZ を条件付きで採るか）は 1.4.0 で検討する
5. BUG-0044 の `EncodingWebName` の `ascii` / `us-ascii` の不統一は、文書に書いただけで直していない
6. ディスクの空き不足の `WriteFailed` は、実際の書き込みでは確かめていない（部品のテストだけ）
7. BUG-0039 のメッセージは、`-Encoding` の解決がパラメーター名を知らないので「文字エンコーディングとして渡された値」と書いた（PowerShell が「パラメーター 'Encoding' の引数変換を処理できません。」を前に付ける）

### 13.12 直した文書

- 1.3.0 仕様書: 1.3・1.5・12・14・17・18.3・19・20・21 章、26.2、27 章（第六次 C で改めた挙動）
- 1.1.0 仕様書: 3.6、4.1、4.4（7 ビットの文字エンコーディング）、4.5、4.6・5.8（標準との差異。新しい節）
- 1.2.0 仕様書: 2.2、2.3、13.1
- ヘルプ（5 言語と zh-HK / zh-MO の複製）: `-Culture`・`-Encoding`・`-TotalCount`・`-UseCulture`・`-Append`（Export）・`-Raw`・`-Strategy`・`-Value`・書き込みの注意事項（裸の utf8 と既定値、逆向きの往復）・`Resolve-Encoding` と `Convert-ProbedContent` のメモリ
- README、CHANGELOG（挙動の変更。第六次 C）、`.psd1` の `ReleaseNotes`、動作確認手順書、CLAUDE.md

## 14. リリース前の補足 — BUG-0030 の残り（文書だけ。2026-10-27）

テスト担当の第 2 段階（回帰テスト）が終わり、BUG-0001〜0045 に「直っていない」ものと新しい不具合は無かった。**1.3.0 は 993a341 のモジュールでリリースする**（利用者の決定）。
補足の確認で BUG-0030 の残りが 1 つ見つかり、利用者が 1.4.0 に送ると決めた。**ソース・テスト・`publish\`（`.psd1` の ReleaseNotes を含む）・ヘルプ（MAML）は変えていない。** 既知の制限として文書（CHANGELOG・README・1.3.0 仕様書 26.5）に書いた。
実測は行っていない（利用者の指示）。以下はソースからの判断である。

### 14.1 報告の内容

- 32 ビット版の Windows PowerShell 5.1 で、600 MB のファイル（ASCII の行が続き、末尾に Shift_JIS の行）に `Get-ProbedContent -Raw` を実行すると、生の `System.OutOfMemoryException` になる（113 秒、ピーク 1,276 MB）
- 同じファイルで、`-Encoding` 省略と `-TotalCount 1` は成功する（ピーク 693 MB）
- 仕様書 26.5 の `NotEnoughMemoryToDetect` は全体を読む判定が対象で、`-Raw` で文字列を作る段階は対象になっていない

### 14.2 生の例外が出る箇所と理由

- 箇所は `GetProbedContentCommand.WriteRawContent` の `reader.ReadToEnd()`（`ProbedFileReader.ReadToEnd` → `StreamReader.ReadToEnd`）
- `EncodingDetectionException.GuardMemory` が包むのは `ProbedFileReader.Open` の中の判定（`ReadAll` と `Detect`）だけで、`ReadToEnd` は包んでいない。`ReadFile` の catch は `EncodingDetectionException`・`IOException`・`UnauthorizedAccessException` だけなので、
  `OutOfMemoryException` は `ProcessRecord` の外へ出て**終了エラー**になる。`-Path` に複数のファイルを渡した場合、残りのファイルは処理されない
- `StreamReader.ReadToEnd` は `StringBuilder`（8000 文字ずつのチャンクの連結リスト）にためてから `ToString()` で 1 つの文字列を作る。文字数を n とすると、ためる途中で約 2n バイト、最後に連続した約 2n バイトで、終盤のピークは約 4n バイト。
  判定に使ったバイト配列は `Open` を抜けた時点で参照が切れているので、同時には残らない
- 600 MB の ASCII では n は約 6 億で、ためるのに約 1.2 GB、最後の文字列にさらに連続した約 1.2 GB が要る。32 ビットのプロセスのアドレス空間ではまず確保できない（報告のピーク 1,276 MB と合う）
- **`-Encoding` を明示しても避けられない。** 判定を飛ばしても、文字列を作る段階の量は変わらない
- 行で読む場合と `-TotalCount 1` は 1 行ずつ文字列を作るので、ピークは判定の分（693 MB）だけになる

### 14.3 64 ビット版（pwsh 7・5.1）でも起きるか

起きる（メモリが十分あっても）。

- .NET の文字列の長さの上限は約 2^30 文字（.NET 10 は `0x3FFFFFDF` = 1,073,741,791 文字、.NET Framework もほぼ同じ）。`gcAllowVeryLargeObjects` は文字列に効かない
- `StringBuilder` の `MaxCapacity` は既定で `int.MaxValue` なのでためる段階は通り、最後の `ToString()` で文字列を確保するときに `OutOfMemoryException` になる。pwsh 7 と 5.1 の 64 ビット版で同じ
- 条件はバイト数ではなく、**復号した後の文字数**（UTF-16 の単位。補助面の文字は 2）が約 10.7 億を超えること

  | ファイルの中身 | 失敗するおおよその大きさ |
  |---|---|
  | 英数字だけ（1 バイト = 1 文字。UTF-8・Shift_JIS など） | 約 1 GiB を超える |
  | 日本語が主の Shift_JIS / EUC（約 2 バイト = 1 文字） | 判定の上限（2 GB）の手前まで届かないことが多い |
  | UTF-16 | 2 GB の上限ぎりぎりが境目 |
  | `-Encoding` を明示（判定の上限が掛からない） | 文字数が上限を超えれば、2 GB を超えるファイルでも同じ |

- 上限の手前で成功させるのに要るメモリは、判定の約 3.6 倍と、その後の `ReadToEnd` の約 4n バイト（1 GB に近い英数字のファイルでは合わせて数 GB）
- 避け方は条件で分かれる。32 ビット版の 5.1 で数百 MB の場合は、64 ビット版を使うか、`-Raw` を使わずに行で読む。文字数が上限を超える場合は、64 ビット版でも避けられないので、`-Raw` を使わずに行で読むか、ファイルを分ける

### 14.4 標準の `Get-Content -Raw`

標準も 1 つの文字列を作るため、同じ上限がある。32 ビット版の 5.1 でも、同じ量の連続した領域が要る（判定をしないので、ピークは判定の分だけ小さい）。
エラーの出方（生の終了エラーか、プロバイダーのエラーに包まれた非終了エラーか）は確かめていないので、文書に書いていない。

### 14.5 1 つの巨大な文字列を作るほかの経路（1.4.0 の検討の材料。直していない）

| コマンド・経路 | 巨大な文字列 | メモリ不足の扱い |
|---|---|---|
| `Get-ProbedContent`（行で読む）で、改行の無い巨大な 1 行 | `ReadLine` が 1 行を 1 つの文字列にする | 生の OOM（終了エラー） |
| `Import-ProbedCsv` で、巨大なフィールド（改行の無いファイル、引用符で囲んだ複数行の値など）・巨大なヘッダー行 | `ImportCsvHelper.ParseNextRecord` / `ReadTillNextDelimiter` の `StringBuilder.ToString()` | 生の OOM |
| `Export-ProbedCsv -Append` で、改行の無い巨大な追記先 | 追記先のヘッダーを読む `ReadHeader`（`ExportProbedCsvCommand`） | 生の OOM |
| `Convert-ProbedContent` | `GetString` でファイル全体を 1 つの文字列にし、改行をそろえる `LineBreakResolver.Normalize` でもう 1 つ作る（LF → CRLF では長くなる） | `NotEnoughMemoryToConvert`（扱い済み。文字列の上限の超過も OOM なので同じく捕まる） |
| `Export-ProbedCsv`（1 行の文字列化）、`Out-ProbedFile`（要素の `ToString()`）、`Set-` / `Add-ProbedContent` | 入力の値の大きさ程度 | 入力がすでにメモリにあるので対象外とみなした |
| `Resolve-Encoding`、`-DetectionLineCount` の切り出し、全体の厳密な検証 | 文字列を作らない（バイト列とバッファだけ） | 該当しない |

### 14.6 1.4.0 の候補

1. **`Get-ProbedContent -Raw` のメモリ不足を ID の付いたエラーにする（BUG-0030 の残り）。** 14.5 の生の OOM の経路（巨大な 1 行、CSV の巨大なフィールド・ヘッダー、`Export-ProbedCsv -Append` のヘッダー）も合わせて検討する。
   ID（`NotEnoughMemoryToDetect` を広げるか、新しい ID にするか）と、非終了エラーにするかは未決
2. **`$PSDefaultParameterValues['*:Encoding'] = 'utf8'` のとき、`Convert-ProbedContent` だけがエラーになり、書き込みの 4 コマンドは警告して省略扱いになる。この違いをヘルプに書く（BUG-0034 の残り）。** 第六次 C では書き込みの 4 コマンドの側だけを書いた

### 14.7 直した文書

- CHANGELOG（1.3.0 の「挙動の変更（PowerShell モジュール。第六次 B）」の BUG-0030 の項の下。既知の制限をまとめた節は 1.3.0 に無い）
- README（1.3.0 の「読み書きの細部の修正」）
- 1.3.0 仕様書 26.5（対象外であることと条件・避け方）
- 本章、CLAUDE.md

---

## 15. リリース前の補足 — Linux（WSL）での単体テストの仕分けと既知の制限 D1〜D4（文書だけ。2026-10-28）

リリースの準備で、利用者が WSL の Ubuntu 24.04 で単体テスト（net10.0）を流し、多くの失敗が出た。仕分けと調査を行い、利用者が次のとおり決めた。

- **1.3.0 は今のモジュール（993a341）でリリースする。** 製品の問題 D1〜D4 は既知の制限として文書に書き、1.4.0 で直す
- **ソース・テスト・MAML ヘルプは変えていない。** `publish\` で変えたのは `.psd1` の ReleaseNotes の文面（`ansi` の書き方の訂正）だけ
- 文書: CHANGELOG（1.3.0 の「Linux / macOS の既知の制限」、BUG-0004・0017 の項と H-17 のリンク切れの項に Linux の例外、`ansi`）、README（同じく）、1.3.0 仕様書（16.5・21 章・25.5・28 章）、動作確認手順書 4.1、本章

### 15.1 実行した環境

- WSL の Ubuntu-24.04。リポジトリを ext4 のホーム（`~/ep`）に clone し、`feature/1.3.0-csv` の f2ed172（ソースは 993a341 と同じ）で実行
- dotnet は `/usr/lib/dotnet`（SDK 10.0.113、ランタイム 10.0.13）。製品の挙動の実測には `~/powershell-7.6.9`（11.4 節で入れたもの）を使い、ビルドした net10.0 の DLL を直接読み込んだ
- 利用者の実行では `LANG` が未設定で、現在のカルチャーは不変カルチャー（`Get-EncodingProbePlatformInfo` の `Locale.CurrentCulture` が空）
- 利用者の結果: コア（EncodingProbe.Tests）2129 件中 7 件、モジュール（EncodingProbe.PowerShell.Tests）2038 件中 210 件が失敗

### 15.2 仕分けの結果

不変カルチャー（`LANG` を外したもの）と `LANG=ja_JP.UTF-8` で流し直した。

| カルチャー | コアの失敗 | モジュールの失敗 |
|---|---|---|
| 不変カルチャー | 7 | 210（利用者のログと同じ件数） |
| ja-JP（`LANG=ja_JP.UTF-8`） | 1 | 201 |

ja-JP でだけ失敗するテストは無かった。不変カルチャーの 217 件の分類:

| 分類 | コア | モジュール | 内訳 |
|---|---|---|---|
| (a) Windows が前提 | 1 | 155 | 下の表 |
| (b) CRLF が前提 | 0 | 35 | CsvColumnNameComparison 5、ExportProbedCsv 15、Iso2022Append 4、InheritedAscii 3、DoubleBomRead 2、UnrepresentableCharacter 2、各テストの Export の行 4 |
| (c) カルチャー（ja-JP で消える） | 6 | 9 | JapaneseEncodingTests 6、Add 3、Set 2、Resolve-Encoding 3、Get 1 |
| (d) 製品の問題 | 0 | 11 | D3 の 1 件（`DanglingLink_FollowsStandard` の Add）、D4 の 10 件（`OpenedByAnotherProgramTests.ShareNone_IsIdentifiedError` など） |

(a) の内訳:

| 原因 | 件数 |
|---|---|
| ハードリンクを作るテストの部品 `FileLinks.TryCreateHardLink` が Windows だけ（Assert で落ちる） | 約 50 件。ReverseRoundTrip の 32 件はすべて target=hardlink |
| `\\?\`・`\\.\`・大文字にした書き方・管理共有・ジャンクション・`C:\`・`::$DATA` | FileIdentity 14、SameFile の大半 |
| パスに `\` を含む（`sub\t.txt`・`dest\a.txt`）。Linux ではファイル名の一部になる | SameFile の symlink の書き方、ConvertDestination |
| `WindowsPrincipal` を使う ACL の検査 | Metadata・WriteDataDenied・FolderWithoutCreatePermission |
| `FileStream.Lock` と共有のロック（Linux では強制されない） | 約 15 件 |
| CIM、`SparseFile` の kernel32 | 6 件 |
| 実体の比較 `FileIdentity.EntityKey`（Linux ではパスの文字列） | 1 MiB の境目の 2 件（15.5 節） |

コアの (a) の 1 件は `FilePathConstructorTests.Constructor_WithNonExistentPath_ThrowsIOException`（`C:\nonexistent\…` が Linux では相対のファイル名になり、`DirectoryNotFoundException` ではなく `FileNotFoundException`）。

**(b) の注意。** `Append_HeaderNotReadable_SameAsHostExportCsv` だけは逆向きに落ちる。モジュールは仕様書 16.3 のとおり追記先の CRLF を引き継ぎ、標準の `Export-Csv` は Linux では LF を書く。
Windows では両方が CRLF なので一致していただけで、Linux では仕様の上で標準と違う（仕様書 21 章の表に足した）。テスト名の「標準と同じ」は Windows でしか成り立たない。

### 15.3 製品の問題 D1〜D4

| # | 内容 | データを失うか | 標準のコマンド（Linux） | 直し方の案と規模 |
|---|---|---|---|---|
| D1 | 1 MiB を超えるハードリンクを上書きすると、リンクが切れる（ほかの名前は古い内容のまま） | 失わない（名前ごとに内容が分かれる） | `Set-Content` はリンクを保つが、同じファイルの往復では 4097 バイトに切り詰める | statx（下）。中 |
| D2 | 読み込みと追記を別のハードリンク名で行うと、終わらずに増え続ける | 元の内容は失わない。ディスクを使い切りうる | `Add-Content` は同じ名前でも増え続ける | statx（下）。中 |
| D3 | リンク切れのシンボリックリンクへの追記がエラーになる。Windows ではリンクの先に作る | 失わない | `Add-Content`・`Out-File -Append` は作る。`Export-Csv -Append` もエラー | 小（2〜3 か所、数十行とテスト） |
| D4 | 共有を許さずに開かれたファイルのメッセージが「error code 0x0000000B」。ID は正しい | 失わない | 「being used by another process」 | 小（1 行とテスト）。直すとテストの 10 件が Linux でも通るはず |

**D1 の原因。** 上書きの書き方（仕様書 16.5）は、T（1 MiB）以内ならメモリに写してから直接書くのでリンクを保つ。T を超えると、Windows ではリンク数が 2 以上なら「写してから直接書く」方法を選ぶ。
Linux では `FileIdentity`（`OfUnix`）がリンク数を取れず常に 1 なので、一時ファイルで置き換え、書いた名前だけが新しい inode になる。`Set`・`Out`・`Export`・`Convert` が該当する（`Add` は追記なので保つ）。

**D2 の原因。** Linux / macOS の同じファイルの判定（`FileIdentity.EntityKey`）はリンクを解決したフルパスの文字列で、ハードリンクを別のファイルとみなす（仕様書 16.5 の既知の限界）。
上書きでは名前が違っても内容は失わない（T 以内は読み終えてから書くので結果は正しい。T を超えると D1 のとおりリンクが切れるだけ）が、追記では読み込み中のファイルの末尾に書き続けるので終わらない。

**D1・D2 の直し方の案。** Linux で `statx` を P/Invoke し、デバイス番号・inode・リンク数を取る。それを `FileIdentity` の実体の鍵とリンク数に使えば、Windows と同じく写してから直接書く方法と、名前が違う場合の検出が効き、D1 と D2 が両方直る。
`FileIdentity.OfUnix` と `FromHandle` に、テストを足して 100〜150 行ほど（中）。macOS は `stat` の構造体が違うので別に扱う（15.7 節）。

**D3 の原因。** Linux の .NET では、リンク切れのリンクについて `FileInfo.Exists` / `File.Exists` が true、`Length` がリンク自身の長さ（実測で 15）を返す。そのため、追記先を「中身のある既存のファイル」と扱い、
文字エンコーディングを継承しようとして読みに行き、失敗する。`Add-ProbedContent` は `AutoEncodingRequiresExistingFile`（.NET の生のメッセージ「Could not find file」）、
`Out-ProbedFile -Append`・`Export-ProbedCsv -Append` は `ReadFailed`（error code 0x80070002。終了エラー）。`Add-ProbedContent -Encoding utf8NoBOM -AllowEncodingChange` は継承しないので、リンクの先に作った。
該当箇所: `EncodingInheritance.IsEmpty`、`OutProbedFileCommand.cs` の `File.Exists` と `hasContent`（191・259 行）、`ExportProbedCsvCommand.cs` の同じ箇所（269・359 行）。
直し方: これらで「リンクの先が無ければ、ファイルは無い」と扱う（`FileIdentity.IsDanglingLink` を使える）。

**D4 の原因。** Linux の .NET は、`FileShare.None` を `flock` の勧告ロックで表し、ロックの失敗の `IOException.HResult` に errno（EAGAIN = 11 = `0x0000000B`）を入れる。
`FileErrors.IsInUse` は Windows の `0x80070020` / `0x80070021` しか見ていないので、使用中のメッセージ（`FileInUseForRead` / `FileInUseForWrite`）ではなく、エラー コードのメッセージ（`FileReadFailed` / `FileWriteFailed`）になる。
.NET 自身のメッセージは「The process cannot access the file … because it is being used by another process.」で、標準の `Get-Content` もこれを出す。直し方: Windows 以外では 11 も「使用中」とみなす。

### 15.4 不変カルチャー（`LANG` が未設定）

製品の問題ではなく、カルチャーゲート（1.2.0）の設計どおり。不変カルチャーは東アジアのカルチャーではないので、旧マルチバイトの独自判定を打ち切る。
Windows の pwsh 7.6.9 で、スレッドのカルチャーを `''`（不変）と `en-US` にして確かめた（WSL から Windows 側を実行。ビルドした DLL を一時フォルダーに写して読み込み、終わったら消した）。

| カルチャー | `sample_shiftjis.txt`（`Resolve-Encoding` / コアの `Detection()`） | `sample_eucjp.txt` |
|---|---|---|
| ja-JP | 932 `shift_jis` / 932 | 20932 `euc-jp` / 20932 |
| 不変 | 932 `shift-jis`（UTF.Unknown） / -1 | 51932 `euc-jp`（UTF.Unknown） / -1 |
| en-US | 932 `shift-jis`（UTF.Unknown） / -1 | 51932 `euc-jp`（UTF.Unknown） / -1 |

Linux と同じ結果だった。5.1（net48）では確かめていない。文書（README・CHANGELOG・仕様書 28 章）で、`-Culture ja-JP` を指定するか `LANG` を設定するよう案内した。

空のファイルで `Culture` が食い違うテスト（`DetectionLineCount_EmptyFile_SameAsWholeFile`）は、先頭 N 行で判定する側にだけ `-Culture ja-JP` を渡しているテストの側の問題。
Windows でも、不変カルチャーでは全体の判定が `''`、`-DetectionLineCount 1 -Culture ja-JP` が `'ja-JP'` になった。

### 15.5 1 MiB の境目のテスト

LF による大きさの違いでは説明できない（テストは `-NoNewline` と `-LineBreak CrLf` を使う）。テストは「実体（`EntityKey`）が変わったか」で書き方を判定しているが、Linux では `EntityKey` がパスの文字列なので、置き換えても変わらない。
inode で確かめると、製品は正しく動いていた。書く内容がちょうど 1,048,576 バイトなら同じ inode のまま書き、1 バイト超えると inode が変わった。元のファイルの大きさの境目でも同じ。

### 15.6 `ansi` の書き方の訂正（11.4 節の測り方の誤り）

利用者の実機の結果（「形式」を英語（米国）にした日本語の Windows で、標準の 7.x の `-Encoding ansi` が `日` を `3F` に書いた）を、Windows で再現した。

| 測り方（Windows の pwsh 7.6.9。システムのロケールは ja-JP、ACP 932） | 標準の `-Encoding ansi` | 本モジュールの `ansi` |
|---|---|---|
| 1 つのプロセスで ja-JP → en-US → ko-KR の順にスレッドのカルチャーを変える（11.4 節と同じ測り方） | すべて `93 FA` | すべて `93 FA`（932） |
| 新しいプロセスで、最初にカルチャーを en-US にしてから使う | `3F` | `93 FA` |
| 新しいプロセスで、最初に en-US で使い、次に ja-JP に変えて使う | `3F`・`3F`（変わらない） | — |
| 新しいプロセスで ja-JP | `93 FA` | `93 FA` |
| 新しいプロセスで en-US の `-Encoding oem` | `93 FA` | — |
| 5.1 の `-Encoding Default`（en-US） | `93 FA` | — |

- 標準の 7.x の `ansi` は、プロセスの中で最初に使った時点の `CurrentCulture.TextInfo.ANSICodePage` を使い、その後は変わらない。`oem` と 5.1 の `Default` はシステムのロケールの値
- 11.4 節では 1 つのセッションの途中でカルチャーを変えて測ったので、標準の側が最初の ja-JP（932）のまま固定され、一致して見えた。**本モジュールの実装（システムのロケール）は正しく、変えていない。** 標準と同じとした書き方だけを直した
- 直した箇所: CHANGELOG（第六次 A の BUG-0032 の項）、README（読み書きの細部の修正）、1.3.0 仕様書 25.5、`.psd1` の ReleaseNotes、本書 11.4 節（訂正の注記）。動作確認手順書 4.1 に、新しいウィンドウで標準の `Set-Content -Encoding ansi` と比べる手順を足した
- 直していない箇所: `SnowStack.EncodingProbe/Internal/SystemCodePages.cs` の XML コメント（「標準の 7.x の `-Encoding ansi` / `oem` … と同じ値」）。ソースなので今回は変えない（利用者の指示）。1.4.0 で直す
- MAML ヘルプ（5 言語）には標準と同じとする記述は無かった（`Get-EncodingProbePlatformInfo` の説明はシステムのロケールとだけ書いてあり、正しい）
- Linux では、標準の `ansi` とモジュールの `ansi` は同じコードページだった（`LANG=en_US.UTF-8` で `日` は標準が `3F`、モジュールは 1252 で表せないので `UnrepresentableCharacter`。`LANG=ko_KR.UTF-8` では両方 `EC ED`）。README の Linux の表は正しい

### 15.7 根拠として残す測定の結果（WSL の pwsh 7.6.9）

測定のスクリプトと生の結果は、前のセッションの scratchpad（一時フォルダー。消える）にあった。根拠として要るものを要約する。

**ハードリンク、1 MiB 以内（`ln t.txt hard.txt` を作り、`hard.txt` に書く）。** `Set`・`Out`・`Export`・`Convert`・`Add`、標準の `Set-Content`・`Add-Content`・`Export-Csv` のすべてで、2 つの名前が同じ inode のまま（リンク数 2）で、同じ内容になった。

**ハードリンク、1 MiB を超える（D1）。**

| 書き方 | t.txt の大きさ | hard.txt の大きさ | リンク数 | 同じ内容 |
|---|---|---|---|---|
| `Set-ProbedContent hard.txt`（4 バイト） | 1,100,001（元のまま） | 4 | 1 | いいえ |
| `Set-ProbedContent hard.txt`（書く内容が 1,100,001 バイト、元は小さい） | 4 | 1,100,001 | 1 | いいえ |
| `Out-ProbedFile` | 1,100,001 | 4 | 1 | いいえ |
| `Export-ProbedCsv` | 1,100,001 | 10 | 1 | いいえ |
| `Convert-ProbedContent` | 1,100,001 | 1,100,004 | 1 | いいえ |
| `Add-ProbedContent` | 1,100,005 | 1,100,005 | 2 | はい |
| 標準の `Set-Content`（小さい内容・大きい内容） | 書いた内容 | 書いた内容 | 2 | はい |

**往復（`t.txt` と、そのハードリンク `hard.txt`）。** 32 バイトと 1,200,002 バイトで測った。

- 読む名前と書く名前が同じ（`t.txt`）なら、順方向（`Get | Set`・`Get | Out`・`Get | Add`・`Import | Export`）と逆向き（`Get a.txt, t.txt | …`）のすべてで `SamePathRoundTrip` になり、内容は変わらなかった
- 名前が違う（`hard.txt` に書く）と検出しない。32 バイトでは、上書きは正しい結果（読み終えてから書く）、`Add` は 1 回分の追記（32 → 64 バイト）で止まった。1,200,002 バイトでは、上書き（`Set`・`Out`・`Export`）はリンクを切った（リンク数 1。t.txt は元のまま）
- **D2:** 1,200,002 バイトの `Get-ProbedContent t.txt | Add-ProbedContent hard.txt` は終わらず、約 60 秒で 32,702,338 バイトになったところで止めた（2 つの名前は同じ inode のまま）。逆向き（`Get-ProbedContent a.txt, t.txt | Add-ProbedContent hard.txt`）も同じく 32,345,986 バイト
- 標準（1,200,002 バイト）: `Get-Content t.txt | Set-Content hard.txt` は 4,097 バイトに切り詰めた（内容を失う。同じ名前でも同じ）。`Import-Csv | Export-Csv` は 0 バイト、`Get-Content | Out-File` も 0 バイト。`Get-Content | Add-Content` は増え続けた（53,399,426 バイトで止めた）

**1 MiB の境目（15.5 節）。** 書く内容が 1,048,576 バイトなら同じ inode、1,048,577 バイトなら inode が変わった。

**リンク切れのシンボリックリンク（D3）。** `ln -s sub/missing.txt dangling.txt` に書いた。

| コマンド | 結果 |
|---|---|
| `Set-ProbedContent` | `LinkTargetNotFound`（仕様どおり。リンクの先を作らない） |
| `Out-ProbedFile`・`Export-ProbedCsv`（`-Append` なし） | リンクの先に作った（仕様どおり） |
| `Add-ProbedContent` | `AutoEncodingRequiresExistingFile`（「Could not find file …」）。作らない |
| `Add-ProbedContent -Encoding utf8NoBOM -AllowEncodingChange` | 作った |
| `Out-ProbedFile -Append`（`-Encoding` の有無によらず）・`Export-ProbedCsv -Append` | 終了エラー `ReadFailed`（error code 0x80070002）。作らない |
| 標準の `Add-Content`・`Out-File`・`Out-File -Append` | 作った |
| 標準の `Set-Content` | 終了エラー（`FileNotFoundException`）。作らない |
| 標準の `Export-Csv -Append` | 終了エラー `FileOpenFailure`。作らない |
| `[IO.FileInfo]` | `Exists = True`、`Length = 15`、`File.Exists = True` |

**共有を許さずに開いたファイル（D4）。** 同じプロセスで `[IO.FileStream]::new($p, 'Open', 'ReadWrite', 'None')` で開いたまま読み書きした。
.NET の `ReadAllBytes` は HResult `0x0000000B`、メッセージ「… being used by another process.」。`Get-ProbedContent` は `ReadFailed` で「could not be read (error code 0x0000000B).」、
`Add-ProbedContent` は `WriteFailed` で「could not be written (error code 0x0000000B).」。標準の `Get-Content` は「being used by another process」。

**フォルダーにファイルを作る権限が無い場合（`chmod 555`）。** 10 バイトと 1,100,000 バイトのどちらでも、`Set`・`Out`・`Export`・`Convert` は書けて、一時ファイルを残さなかった（直接書く方法に切り替わる。Linux でも仕様どおり）。

**シンボリックリンク・フォルダーへのリンク・`..`・`//`。** Linux でも同じファイルと検出した（テストの失敗は、パスに `\` を使った書き方のため）。

### 15.8 1.4.0 の候補

1. **D1・D2: Linux の実体の鍵とリンク数を statx で取る。** `FileIdentity.OfUnix` / `FromHandle` で、デバイス番号と inode を実体の鍵に、リンク数を `LinkCount` に使う。中（100〜150 行とテスト）。
   直したら、Linux のハードリンクを `KnownLimit_` ではなく Windows と同じ期待値でテストする
2. **D3: リンク切れのシンボリックリンクへの追記で、リンクの先にファイルを作る。** `EncodingInheritance.IsEmpty`・`OutProbedFileCommand`・`ExportProbedCsvCommand` で、リンクの先が無ければファイルは無いと扱う。小
3. **D4: Windows 以外では errno 11（EAGAIN）も「使用中」とみなす**（`FileErrors.IsInUse`）。小
4. **テストを Linux・macOS でも動くようにする**
   - xUnit 2.9 には実行時にテストを飛ばす機能が無いので、`[WindowsFact]` / `[WindowsTheory]`（Windows 以外ならコンストラクターで `Skip` を設定する属性）を作り、
     `\\?\`・`\\.\`・大文字の書き方・管理共有・ジャンクション・`::$DATA` と代替データストリーム・ACL・`FileStream.Lock`・共有のロック・CIM のテストを Windows 専用にする。
     代わりに Linux 用のテスト（シンボリックリンク・フォルダーへのリンク・`..`・`//` の検出、`chmod 555` のフォルダー）を足す
   - パスの区切り: `TemporaryDirectory.Write` / `Combine` で `\` を区切り文字に置き換える（一度に多くのテストが直る）。`C:\nonexistent` は一時フォルダーの下の存在しないパスにする
   - ハードリンクの作り方: Linux では libc の `link()` で作る（`FileLinks.TryCreateHardLink`）。`SparseFile` は Linux では `SetLength` だけにする（ext4 では疎なファイルになる）
   - 1 MiB の境目のテストは、実体の比較の代わりに `SafeFileOverwrite.Method` などで書き方を確かめる（statx を入れれば実体の比較のままでもよい）
   - 改行: 改行を確かめること自体が目的でないテストは、コマンドに `-LineBreak CrLf` を足すか、期待値を `Environment.NewLine` で組み立てる。`Export_M4_*` は期待値の改行を `Environment.NewLine` にする。
     `Append_HeaderNotReadable_SameAsHostExportCsv` は Windows 専用にするか、Linux では仕様（追記先の改行を引き継ぐ）の期待値にする
   - カルチャーの明示: コアは `Detection(ja-JP)`、モジュールは `-Culture ja-JP`（空のファイルのテストは両方の呼び出しに渡す）。今のテストは日本語の Windows で動かすことを暗に前提にしている
5. **macOS の実体の鍵。** macOS は `stat` の構造体と関数が Linux と違う（`statx` が無い）ので、別に P/Invoke する。手元に macOS が無いので、実測の手段から決める
6. `SystemCodePages.cs` の XML コメントの `ansi` の書き方を直す（15.6 節）

### 15.9 直した文書

- CHANGELOG（1.3.0 に「Linux / macOS の既知の制限（PowerShell モジュール）」の節を足した。BUG-0004・0017 の項に「Linux のハードリンクは除く」、H-17 のリンク切れの項に Linux の例外、第六次 A の BUG-0032 の項の `ansi`）
- README（1.3.0 の「パスの束縛・同じファイルの判定・リンクの扱い」に Linux の例外、「読み書きの細部の修正」の `ansi`、「Linux / macOS の既知の制限」の小見出し、1.1.0 の `-Culture` の説明に Linux の例）
- 1.3.0 仕様書（16.5 に Linux の例外と 28 章への参照、21 章の表に Linux の `-Append` の改行、25.5 の `ansi`、28 章を新設）
- `publish/SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.psd1` の ReleaseNotes（`ansi` の書き方だけ）
- 動作確認手順書 4.1（標準の `Set-Content -Encoding ansi` と比べる手順）
- 本書 11.4 節（訂正の注記）と本章
