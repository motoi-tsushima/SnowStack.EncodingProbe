# SnowStack.EncodingProbe.PowerShell 1.3.0 新規追加機能仕様書

- 対象バージョン: PowerShell モジュール 1.3.0（現行 1.2.0 からの機能追加）。クラスライブラリも 1.3.0 として同時にリリースする（不具合修正だけ。追補を参照）
- 位置づけ: 追加が中心。既存コマンドへの変更は `Resolve-Encoding` へのパラメータ追加だけとする
- 作成日: 2026-10-15
- 状態: **確定（24 章の未決事項を除く）。利用者との議論で決定した内容を記載している**
- 前提文書:
  - `docs/EncodingProbe-1.1.0-仕様書.md`（統一語彙、原則 A / B、エラー方針。本書はこれを土台とし、差分だけを書く）
  - `docs/EncodingProbe-1.2.0-仕様書.md`（`Out-ProbedFile` の規則。`Export-ProbedCsv` は多くをこれに従う。以下「1.2.0 仕様書」）
  - `docs/EncodingProbe-1.3.0-調査-CSVコマンド挙動の実測.md`（本書の「標準の挙動」はすべてこの実測に基づく。以下「実測報告」）
  - `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`（クラスライブラリの判定処理の修正。2026-10-20 に追加。以下「追補」）

> **Claude Code へ:** 作業の進め方（段階の分け方、コミットの単位など）は依頼文で指示する。
> 実装上の制約と落とし穴は `docs/EncodingProbe-1.3.0-ClaudeCode指示書.md` にある。本書は仕様だけを定める。

---

## 1. スコープ

### 1.1 1.3.0 で追加・変更するもの

| 種別 | 名称 | 本書 |
|---|---|---|
| 既存コマンドへのパラメータ追加 | `Resolve-Encoding -DetectionLineCount` | 第 2 部 |
| コマンドの追加 | `Import-ProbedCsv`（標準の `Import-Csv` に対応） | 第 3 部 |
| コマンドの追加 | `Export-ProbedCsv`（標準の `Export-Csv` に対応） | 第 4 部 |
| 内部の共通部品 | 先頭 N 行の切り出し、全体の厳密な検証、CSV の読み書き処理（PowerShell からの移植） | 第 1 部 |
| クラスライブラリの不具合修正 | 英数字だけの BOM 無し UTF-16 / UTF-32 が `us-ascii` と判定される課題（24.1） | 追補 |

### 1.2 リリースの単位

- **PowerShell モジュールとクラスライブラリ（NuGet パッケージ `SnowStack.EncodingProbe`）を、どちらも 1.3.0 として同時にリリースする**
- クラスライブラリの変更は、判定処理の追補 2 章の点（`0x00` を含む ASCII の扱い）だけである。公開 API は変えない
- モジュール 1.3.0 は、クラスライブラリ 1.3.0 の DLL を同梱する

> **経緯:** 当初は「PowerShell モジュールだけをリリースし、クラスライブラリは 1.2.0 のまま上げない」としていた。
> 2026-10-20 に利用者が、24.1 の課題をクラスライブラリとモジュールの 1.3.0 として同時に直すと決めた（追補 1 章）。

### 1.3 変更しないもの

- クラスライブラリの公開 API と `EncodingInformation` 型。判定処理は追補 2 章の点だけを変え、それ以外は変えない
  （当初は「クラスライブラリのすべて」としていた。1.2 の経緯を参照）
- `Resolve-Encoding` の**既存の**パラメータと戻り値。`Get-EncodingProbePlatformInfo`
- 統一語彙（1.1.0 仕様書 3 章）、解決順序、BOM 方針の決定規則
- `Get-` / `Set-` / `Add-ProbedContent`、`Out-ProbedFile`、`Convert-ProbedContent`、`ConvertTo-DotNetEncoding`

> **恒久的な制約との関係:** CLAUDE.md と 1.1.0 指示書 1 節は「`Resolve-Encoding` のパラメータと戻り値を変更しない」を今後も維持する制約としている。
> 1.3.0 の `-DetectionLineCount` の追加は、**利用者が明示的に決定した例外**である。既存のパラメータと戻り値は変えない。
> 制約の文言は、1.3.0 の完了時に「既存のパラメータと戻り値を変更しない（1.3.0 で `-DetectionLineCount` を追加した）」に改める。

### 1.4 1.3.0 で扱わないもの

| 項目 | 理由・予定 |
|---|---|
| JSON 用の機能 | JSON は RFC 8259 で UTF-8（BOM 無し）と定められており、判定の対象ではない。特別な対応はしない |
| XML 用の機能 | XML は BOM と XML 宣言の `encoding` 属性で文字エンコーディングを示す。仕様に即した専用の機能として、後のバージョンで検討する |
| YAML など | 既存のサードパーティ製品に任せる |
| 既存コマンドへの `-DetectionLineCount` | `Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `Out-ProbedFile` / `Convert-ProbedContent`。1.4.0 で検討する |
| 全体で判定した場合の検証 | 本書では、全体で判定した場合は検証しない（既存のコマンドと同じ）。検証の統一は 1.4.0 で検討する |
| 既存コマンドの追記での末尾の改行の補完 | `Export-ProbedCsv` だけで行う（18.3）。`Out-ProbedFile` / `Add-ProbedContent` への適用は 1.4.0 で検討する |

### 1.5 基本方針

1. **CSV の読み書きの規則は、Windows PowerShell 5.1 でも PowerShell 7.x の動作に合わせる。** 両ホストで、同じ入力から同じ結果（読み込みは同じオブジェクト、書き出しは同じバイト列）を得る
2. **標準コマンドの明らかな欠陥は直す。** 本モジュールは、標準コマンドの欠陥を補うためのものである。直した点は、標準との差異として一覧にする（13 章、21 章）
3. **文字を黙って失わない。** 判定を誤った可能性がある場合は、処理する前に止める（4 章）
4. RFC 4180 と PowerShell の標準の挙動が食い違う場合は、PowerShell の標準に合わせる（例: 改行は CR-LF 固定ではなく OS の既定）

---

# 第 1 部 共通の部品

## 2. 先頭 N 行の切り出し

### 2.1 目的

巨大なファイルの判定を、先頭の N 行だけで行う。バイト数ではなく行数で切るのは、切った末尾が必ず改行になり、
多バイト文字の途中で切れることがないためである。判定処理（クラスライブラリ）を変更せずに済む。

`Resolve-Encoding` / `Import-ProbedCsv` / `Export-ProbedCsv` の `-DetectionLineCount` は、**すべてこの部品を使う**。
同じファイルと同じ N に対して、3 つのコマンドの判定結果は常に一致する（3.3）。

### 2.2 N の意味

- N は**物理的な行数**である。改行（CR-LF / LF / CR）で区切られた単位を 1 行と数える
- CR-LF は 1 個の改行として数える
- CSV の引用符の中にある改行も 1 行と数える。**N は CSV のレコード数ではない**（ヘルプに明記する）
- U+2028 / U+2029 / U+0085 は改行として数えない（`LineBreakResolver.Normalize` と同じ）

### 2.3 改行の型

ファイルの符号化単位によって、改行のバイト列が異なる。次の 5 つの型を区別する。

| 型 | 単位 | LF | CR | 位置の条件（ファイル先頭からのオフセット。BOM を含む） |
|---|---|---|---|---|
| 1 バイト | 1 | `0A` | `0D` | なし |
| UTF-16LE | 2 | `0A 00` | `0D 00` | 先頭のバイトが偶数の位置 |
| UTF-16BE | 2 | `00 0A` | `00 0D` | 先頭のバイトが偶数の位置 |
| UTF-32LE | 4 | `0A 00 00 00` | `0D 00 00 00` | 先頭のバイトが 4 の倍数の位置 |
| UTF-32BE | 4 | `00 00 00 0A` | `00 00 00 0D` | 先頭のバイトが 4 の倍数の位置 |

「1 バイト」の型は、ASCII 互換のすべての文字エンコーディング（UTF-8、Shift_JIS、EUC 系、GBK / GB18030、Big5、UHC、
ISO-2022 系、シングルバイト系）に当てはまる。これらは多バイト文字の 2 バイト目以降に `0A` / `0D` が現れない。

### 2.4 型の決定

次の順に決める。

1. **BOM がある場合は、BOM から型を決める。** UTF-8 の BOM は「1 バイト」、UTF-16 / UTF-32 の BOM はそれぞれの型とする。
   BOM の判定は `ByteOrderMarkDetection` と同じ規則による（UTF-32LE の BOM `FF FE 00 00` を UTF-16LE と取り違えないこと）
2. **BOM が無い場合は、先頭の探索範囲に `0x00` があるかを調べる。** 探索範囲は先頭の 64 KiB（ファイルがそれより短ければ全体）とする
   - `0x00` がある場合は、「1 バイト」の型を候補から外す。ASCII 互換の文字エンコーディングのテキストには `0x00` が現れないためである
   - `0x00` が無い場合は、「1 バイト」の型だけを候補とする
3. 先頭から `0A` または `0D` を探す。見つかった位置ごとに、候補の型のうち、**バイト列と位置の条件の両方**（2.3 の表）に一致するものを探す。
   照合には、見つかった位置の前後のバイトを使う（UTF-16BE / UTF-32BE では、`0A` の前に `00` が来る）
   - 一致する型があれば、その型に決める。候補が複数一致する場合は、UTF-32 → UTF-16 の順に優先する（UTF-32 の改行は UTF-16 の改行の形も含むため）
   - 一致する型が無ければ、その位置は改行ではないとみなし、次の `0A` / `0D` へ進む
     （例: UTF-16LE の「上」U+4E0A は `0A 4E` であり、`0A` の直後が `00` ではないので改行ではない）
4. ファイルの末尾まで型が決まらなかった場合は、改行が無いとみなし、2.6 に従う

> **既知の限界:** BOM 無しの UTF-16 / UTF-32 で、先頭 64 KiB に ASCII 文字も改行も 1 つも無い（すべて漢字などの）ファイルは、
> `0x00` が見つからず「1 バイト」の型と誤って決まることがある。この場合も 4 章の検証で止まる可能性が高いが、保証はしない。
> ヘルプには記載しない（実在の可能性が極めて低いため）。テストでは境界の例として扱い、挙動を固定する。

### 2.5 行の数え方と切る位置

型が決まったら、**ファイルの先頭から**その型の単位で改行を数え、N 個目の改行の直後で切る。

- 単位の境界（2.3 の位置の条件）に合わない `0A` / `0D` は改行と数えない
- CR の直後に LF が続く場合は、合わせて 1 個の改行とする。**CR と LF の間で切らない。** CR がバッファの末尾に来た場合は、次の単位を読んでから決める
- 切り出したバイト列は、BOM を含むファイルの先頭から、N 個目の改行（CR-LF なら LF まで）の末尾までとする
- 切り出したバイト列の長さは、UTF-16 なら 2 の倍数、UTF-32 なら 4 の倍数になる（`Utf16_Detection` / `Utf32_Detection` は、長さが単位の倍数でないと対象外とするため、これが必要である）

### 2.6 N 行に満たない場合・改行が無い場合・上限

- ファイルの改行が N 個未満の場合は、ファイル全体を切り出す。結果は、全体で判定した場合と同じになる
- 改行が見つからない場合（2.4 の 4）も、ファイル全体を切り出す
- 切り出すバイト列の大きさが `EncodingInheritance.MaxDetectableLength` を超える場合は、判定できないエラーとする（全体の判定と同じ扱い・同じ ID）。
  改行の無い巨大なファイル（1 行に詰めた JSON など）では、`-DetectionLineCount` を指定しても判定できない。ヘルプに記載する
- 0 バイトのファイルは、既存の規則（全体の判定）と同じに扱う

### 2.7 判定への受け渡し

切り出したバイト列を `EncodingProbe.Detect(byte[], EncodingDetectorOptions)` に渡す。
`-Culture` / `-Strategy` は全体の判定と同じく `EncodingDetectorOptions` に入れる。

---

## 3. 先頭 N 行の判定結果の扱い

### 3.1 `Resolve-Encoding`

判定結果（`EncodingInformation`）を**そのまま**出力する。読み替え（3.2）も検証（4 章）も行わない。
`Resolve-Encoding -DetectionLineCount` は「先頭 N 行が何に見えるか」を報告するコマンドである。

### 3.2 `Import-ProbedCsv` / `Export-ProbedCsv`

判定結果を使う前に、次の読み替えを行う。読み替えには、切り出しで決めた改行の型（2.4）を使う。

| 判定結果 | 切り出しで決めた改行の型 | 読み替え後 |
|---|---|---|
| `20127 / us-ascii`（先頭 N 行のバイトがすべて `0x80` 未満だった） | 1 バイト、または改行が無い | **BOM 無しの UTF-8（65001）** |
| 同上 | UTF-16LE / UTF-16BE | BOM 無しの UTF-16LE（1200）/ UTF-16BE（1201） |
| 同上 | UTF-32LE / UTF-32BE | BOM 無しの UTF-32LE（12000）/ UTF-32BE（12001） |
| それ以外 | — | そのまま |

ASCII は UTF-8 の部分集合であり、ファイルが本当に UTF-8 なら必ず正しく読める。
UTF-8 でなければ、4 章の検証で止まる（黙って文字化けすることはない）。

UTF-16 / UTF-32 の行が必要な理由: 英数字だけの BOM 無し UTF-16 / UTF-32 は、判定処理が UTF-16 / UTF-32 と判定する（追補 2 章）。
しかし、2 バイトとも `0x80` 未満になる文字（「上」U+4E0A = UTF-16LE で `0A 4E`、「字」U+5B57、「あ」U+3042 など）が英数字に混じった
BOM 無しの UTF-16 / UTF-32 は、すべてのバイトが `0x80` 未満で、かつ追補 2.2 の形にも合わないため、`us-ascii` と判定される（追補 4.1。既知の限界）。
これを UTF-8 と読み替えると、UTF-8 の検証を通ってしまい（`0x00` も ASCII も UTF-8 として正しい）、`U+0000` が混じった文字列として読まれる。
切り出しの段階で改行の型が分かっているので、それに従って読み替える（24 章 U4）。

> **経緯:** 当初は、判定処理が `0x00` を含む ASCII をすべて ASCII と判定するため、英数字だけの BOM 無し UTF-16 / UTF-32 を救う目的でこの行を設けた。
> 追補で判定処理を直した後は、英数字だけのものは判定の時点で UTF-16 / UTF-32 になり、この行を通らない。追補 4.1 の入力のために残す（追補 1 章）。

- この読み替えは `-DetectionLineCount` を指定した場合だけ行う。全体で判定した場合は、既存の規則どおりそのまま使う
- 改行の型（`LineBreak`）は読み替えない
- `Resolve-Encoding` は読み替えない（3.1）。先頭 N 行が追補 4.1 の入力（2 バイトとも `0x80` 未満の文字を含む BOM 無しの UTF-16 / UTF-32）であれば `us-ascii` を報告する。
  英数字だけの BOM 無し UTF-16 / UTF-32 には、その文字エンコーディングを報告する（追補 5 章）

### 3.3 一致の保証

同じファイル・同じ N・同じ `-Culture` / `-Strategy` に対して、
`Resolve-Encoding -DetectionLineCount N` が返す `EncodingInformation` と、
`Import-ProbedCsv` / `Export-ProbedCsv` が 3.2 の読み替えの**前**に得る `EncodingInformation` は、すべてのプロパティで一致する。
自動テストでこれを確認する。

---

## 4. 全体の厳密な検証

### 4.1 適用範囲

`-DetectionLineCount` を指定して判定した結果を使う場合は、**使う前に必ず、そのファイルの全体を厳密に復号して検証する。**

| コマンド | 検証するファイル |
|---|---|
| `Import-ProbedCsv` | 読み込むファイル |
| `Export-ProbedCsv` | `-Append` の追記先、明示的な `-Encoding Auto` の出力先、`-EncodingFrom` の参照ファイル（24 章 U2） |
| `Resolve-Encoding` | 検証しない（3.1） |

- 全体で判定した場合は検証しない（1.4）
- `-Encoding` で文字エンコーディングを明示した場合は、判定も検証も行わない
- BOM で文字エンコーディングが決まった場合も検証する（例外を設けない）

### 4.2 方法

- `EncodingVocabulary.BuildStrictEncoding(codePage)`（例外フォールバック）で `Decoder` を作る
- ファイルを固定サイズのバッファ（64 KiB）で先頭から順に読み、`Decoder.GetChars(..., flush: false)` に与える。復号した文字は使わずに捨てる
- 最後に `flush: true` で呼び、末尾で途切れた多バイト文字も検出する
- BOM はスキップしてから復号する（BOM の長さは `ProbedFileReader` と同じ規則で求める）
- ファイルは `FileShare.ReadWrite`、`FileOptions.SequentialScan` で開く（`ProbedFileReader` と同じ）

**メモリの使用量は、ファイルの大きさに関係なく一定である**（バッファ・文字バッファ・`Decoder` 1 個）。
`Decoder` は、バッファの境目で分かれた多バイト文字を内部の状態で正しくつなぐ。

### 4.3 失敗した場合

- `Import-ProbedCsv`: そのファイルのレコードを**1 件も出力せずに**、非終了エラーとする。次のファイルに進む
- `Export-ProbedCsv`: ファイルに**何も書かずに**、終了エラーとする
- エラー ID は `DetectionVerificationFailed` とする（新設）
- メッセージには、次を含める
  - ファイルのパス
  - 先頭 N 行の判定で決めた文字エンコーディング（統一語彙名）と N
  - 最初の不正なバイト列の位置（ファイル先頭からのバイトオフセット）とそのバイト列（`Convert-ProbedContent` の `InvalidByteSequence` と同じ形式）
  - 対処の案内: `-DetectionLineCount` を増やすか、`-Encoding` で文字エンコーディングを指定する
- 不正な位置は `DecoderFallbackException.Index` を使わずに求める（.NET Framework と .NET Core で基準が違い、5.1 と 7.x で異なる値を報告するため。1.2.0 で判明済み）。
  `ConvertProbedContentCommand.FindInvalidBytes` と同じ考え方で、失敗したときだけ、失敗したバッファの付近を 1 バイトずつ再走査してよい。
  **5.1 と 7.x で同じ位置を報告すること**

### 4.4 時間とファイルの読み込み回数

`-DetectionLineCount` を指定すると、ファイル全体を 2 回読む（検証と、本来の処理）。巨大なファイルほど時間がかかる。
これは「判定を誤ったまま処理しない」保証の代償である。速さが必要な場合は `-Encoding` で明示すれば判定も検証も行わない。ヘルプに記載する。

`-DetectionLineCount` を指定した場合の処理の順序:

| 段階 | 読む範囲 | メモリ |
|---|---|---|
| ① 判定 | 先頭 N 行（2 章） | N 行分 |
| ② 検証 | 全体（ストリーム） | 一定 |
| ③ 本来の処理（インポート、追記先のヘッダーの読み取り） | 全体または先頭（ストリーム） | 1 レコード分 |

---

## 5. CSV の読み書き処理（PowerShell からの移植）

### 5.1 移植元

| 項目 | 内容 |
|---|---|
| リポジトリ | https://github.com/PowerShell/PowerShell |
| ファイル | `src/Microsoft.PowerShell.Commands.Utility/commands/utility/CsvCommands.cs` |
| コミット | `db1d0a56cbe41adf05d0bad90be5190a6655404f`（master、2026-10-14） |
| ライセンス | MIT License、`Copyright (c) Microsoft Corporation.` |

### 5.2 移植する範囲

| 移植元 | 用途 |
|---|---|
| `ImportCsvHelper`（`ReadHeader` / `Import` / `ParseNextRecord` / `IsNewLine` / `ReadTillNextDelimiter` / `ReadTypeInformation` / `ValidatePropertyNames` / `BuildMshobject`） | `Import-ProbedCsv` の解析、`Export-ProbedCsv -Append` の追記先のヘッダーの読み取り |
| `ExportCsvHelper`（`BuildPropertyNames` / `ConvertPropertyNamesCSV` / `ConvertPSObjectToCSV` / `GetToStringValueForProperty` / `GetTypeString` / `AppendStringWithEscapeAlways`） | `Export-ProbedCsv` の書き出し |
| `ExportCsvCommand.ReconcilePreexistingPropertyNames` の論理 | `-Append` の列の照合 |
| `ImportExportCSVHelper.SetDelimiter`、`BaseCsvWritingCommand.QuoteKind` | 区切り文字の決定、`-UseQuotes` の値 |

- 移植したコードは、**1 か所（1 ファイル、または 1 フォルダー）にまとめる**。どこが Microsoft のコードに由来するかを明確にするためである
- 解析・書き出しの規則（空白の扱い、引用符の扱い、`#` で始まる行の扱いなど）は**移植元のとおり**とし、独自の改良を加えない。
  本書で明示した変更（18.3 の末尾の改行の補完など）は、移植したコードの外（コマンド側）で行う
- PowerShell の内部 API（`internal` の型・メソッド）は使えない。公開 API で同じ結果になるように置き換える（指示書 4.2）
- 移植元のリソース文字列（`CsvCommandStrings`、`ExtendedTypeSystem`）は使わない。メッセージは `MessageCatalog` に 5 言語で持つ（22 章）

### 5.3 ライセンス表示

MIT License の条件（著作権表示と許諾表示を、複製または重要な部分に含めること）を満たすため、次の 4 か所に表示する。

1. **移植したソースファイルの冒頭**（付録 A.1 の文面）。元の著作権表示を残し、移植元と改変したことを書く
2. **`THIRD-PARTY-NOTICES.txt`**（付録 A.2 の文面）。MIT License の全文を、PowerShell の LICENSE.txt の**原文のまま**載せる（`*AS IS*` のアスタリスクも変えない）。
   あわせて、冒頭の「SnowStack.EncodingProbe includes the following third-party component.」を、パッケージごとに含まれるものが分かる書き方に改める（付録 A.2）
3. **`LicenseAndHelp.License`**（`Resolve-Encoding -License` の出力）に、モジュールに Microsoft の MIT コードが含まれることを追記する（付録 A.3）
4. **配布するモジュールのフォルダーに `LICENSE.txt` と `THIRD-PARTY-NOTICES.txt` を同梱する**（リリース作業。動作確認手順書のリリース前チェックに含める）

- クラスライブラリ側の `EncodingProbe.License` 定数は変更しない（PowerShell 由来のコードはモジュールにだけ含まれるため。クラスライブラリは追補で 1.3.0 に上げたが、この判断は変わらない。追補 6 章）
- CLAUDE.md の「ライセンス上の注意」に、PowerShell 由来のコードの表示箇所を追記する

### 5.4 参照してはならないもの

**Windows PowerShell 5.1 のバイナリ（`Microsoft.PowerShell.Commands.Utility.dll` など）を逆コンパイルして参照しない。** 5.1 はオープンソースではない。
5.1 の挙動は、実行して観察した結果（実測報告）だけを使う。

---

# 第 2 部 `Resolve-Encoding -DetectionLineCount`

## 6. 仕様

### 6.1 パラメータ

| パラメータ | 型 | パラメータセット | 検証 | 説明 |
|---|---|---|---|---|
| `-DetectionLineCount` | `long`（`Int64`） | `Default` | `ValidateRange(1, long.MaxValue)` | 判定に使う先頭の行数。省略時はファイル全体で判定する（従来どおり） |

- 位置パラメータにしない。パイプラインから受け取らない
- 0 と負の値はパラメータ束縛の段階で拒否する（0 行の判定には意味がない。`Get-ProbedContent -TotalCount` の 0 とは扱いが違う）
- 名前は `-TotalCount` にしない。`Get-Content` / `Get-ProbedContent` の `-TotalCount` は「出力する行数」であり、意味が違うため。
  1.4.0 で `Get-ProbedContent` に判定の行数を加えるときに、同じコマンドに `-TotalCount` と `-DetectionLineCount` が並ぶことを想定している。別名も設けない

### 6.2 挙動

- `-DetectionLineCount` を指定した場合は、2 章の切り出しを行い、切り出したバイト列を判定して、結果をそのまま出力する（3.1）
- 出力する `EncodingInformation` の `LineBreak` も、切り出した範囲だけで判定した結果になる（後半だけ改行が混在するファイルは見逃す）
- 切り出しが判定の上限を超えた場合（2.6）は、終了エラーとする。既存の「ファイルが無い」と同じく終了エラーで、ID は全体の判定の上限超過と同じものを使う

### 6.3 ヘルプ

5 言語の MAML に `-DetectionLineCount` を追加し、次を記載する。

- 行数は物理的な行数であり、改行（CR-LF / LF / CR）で数える。CSV のレコード数ではない
- 結果は先頭 N 行だけを見たものであり、後半にだけ現れる文字（ASCII の後の日本語など）は反映されない。
  先頭 N 行が ASCII だけなら `us-ascii` と報告する（BOM 無しの UTF-16 / UTF-32 で、先頭 N 行に 2 バイトとも `0x80` 未満になる文字（「上」など）が英数字に混じる場合も `us-ascii` になる。追補 4.1。
  英数字だけの BOM 無し UTF-16 / UTF-32 は、その文字エンコーディングと報告する）
- NOTES に、追補 4 章の既知の限界（3 点）を記載する（追補で追加）
- `LineBreak` も先頭 N 行だけで判定した結果である
- 改行の無いファイルでは、ファイル全体を読む（大きすぎる場合は判定できない）

---

# 第 3 部 `Import-ProbedCsv`

## 7. 基本方針

`Import-ProbedCsv` は、文字エンコーディングを判定して CSV ファイルを読み込み、各レコードをオブジェクトとして出力する。

- **解析は、PowerShell 7.x の `Import-Csv` から移植した処理で行う**（5 章）。ホストの `Import-Csv` は呼ばない。
  そのため、Windows PowerShell 5.1 でも 7.x の `Import-Csv` と同じ結果になる
- 1 レコードずつ出力する（ストリーミング）。ファイル全体をメモリに読まない（全体で判定する場合の判定処理を除く）
- 標準の `Import-Csv` との差異は 13 章に一覧する

> **ホストの `Import-Csv` に任せない理由（実測報告 3 章）:** Windows PowerShell 5.1 の `Import-Csv` は、CR だけの改行の CSV を読むと
> エラーも警告も出さずに 0 件を返す（データが黙って失われる）。W3C 拡張ログ形式にも対応していない。
> また 5.1 の `-Encoding` は列挙型で、EUC-JP などを渡せない。

## 8. パラメータ一覧

パラメータセットは 4 つ。`DelimiterPath`（既定）、`DelimiterLiteralPath`、`CulturePath`、`CultureLiteralPath`（標準の `Import-Csv` と同じ構成）。

| パラメータ | 型 | 位置 | パイプライン | 別名 | 説明 |
|---|---|---|---|---|---|
| `-Path` | `string[]` | 0 | ByValue, ByPropertyName | 9.1 | 読み込むファイル。ワイルドカード対応。`*Path` セットで必須 |
| `-LiteralPath` | `string[]` | — | ByPropertyName | `PSPath`, `LP` | ワイルドカード非対応。`*LiteralPath` セットで必須 |
| `-Delimiter` | `char` | 1 | — | — | 区切り文字。`Delimiter*` セット。省略時は `,` |
| `-UseCulture` | switch | — | — | — | 区切り文字に、現在のカルチャーのリスト区切り文字を使う。`Culture*` セットで必須 |
| `-Header` | `string[]` | — | — | — | 列名。指定すると、ファイルの 1 行目もデータとして読む（標準と同じ） |
| `-Encoding` | 多形（読み取り用） | — | — | — | 統一語彙ほか。既定は `Auto`（判定する）。`Get-ProbedContent -Encoding` と同じ語彙と規則 |
| `-DetectionLineCount` | `long` | — | — | — | 判定に使う先頭の行数。`ValidateRange(1, long.MaxValue)`。省略時は全体で判定する |
| `-Culture` | `string` | — | — | — | 判定に用いるカルチャー名（`Resolve-Encoding` と同じ） |
| `-Strategy` | `string` | — | — | — | 判定方式（`Resolve-Encoding` と同じ） |

- **`-Culture` と `-UseCulture` は別のものである。** `-Culture` は文字エンコーディングの判定に使うカルチャー、`-UseCulture` は区切り文字の決め方である。
  両方を同時に指定できる。ヘルプの両パラメータの説明に、この違いを明記する
- `-Encoding` を明示した場合（`Auto` 以外）、`-DetectionLineCount` / `-Culture` / `-Strategy` は判定を行わないため意味を持たない。
  既存の Probed 系コマンドが `-Encoding` 明示時の `-Culture` / `-Strategy` を扱うのと同じ扱いにする（警告もエラーも出さない）

## 9. パスの解決と複数のファイル

### 9.1 パスの解決

- `Get-ProbedContent` と同じ部品（`ProbedContentCommandBase` のパスの解決）と同じ規則を使う
- `-Path` のワイルドカードが**複数のファイルに一致した場合は、一致したファイルを順に読む**（標準の `Import-Csv` はエラーになる。実測報告 M2）
- `-Path` / `-LiteralPath` に配列を渡した場合は、順に読む
- `Get-ChildItem | Import-ProbedCsv` は **両ホストで**読めること（標準の 5.1 は `CannotSpecifyPathAndLiteralPath` で失敗する。実測報告 M2）。
  パイプライン束縛の属性（別名を含む）は、両ホストで同じ形が動作確認済みの `Get-ProbedContent` に合わせる
- 存在しないファイル、フォルダーを指定した場合は、`Get-ProbedContent` と同じ非終了エラー（同じ ID）とし、次のパスに進む
- ワイルドカードが 0 件に一致した場合は、`Get-ProbedContent` と同じく、エラーを出さずに何も読まない（標準の `Import-Csv` は終了エラー）

> **改めた経緯（2026-10-19、第二次）:** 当初は「ワイルドカードが 0 件」も非終了エラーとしていた。実装時に確かめたところ、
> 既存の `Get-ProbedContent` は両ホストとも 0 件のワイルドカードでエラーを出さなかった（パスの解決の部品を共用している）。
> 既存のコマンドの挙動は変えないため、利用者の決定で `Get-ProbedContent` に合わせた（実装記録 6 章）。

### 9.2 複数のファイルを読む場合の規則

- ファイルごとに、文字エンコーディングを判定し（10 章）、ヘッダーを読み直す。ファイルごとに列が違ってもよい（出力されるオブジェクトの形が混ざる）
- `-Header` を指定した場合は、すべてのファイルに同じ列名を使う
- 1 ファイルの失敗（判定の失敗、検証の失敗、ヘッダーの重複、読み取りの失敗）は非終了エラーとし、次のファイルに進む
- 列名が空のときの警告（`H1` などで補う警告）は、標準と同じく、**実行全体で 1 回だけ**出す
- 読み込み中のファイルは `ActiveReadRegistry` に登録する（`Import-ProbedCsv a.csv | Export-ProbedCsv a.csv` を検出するため）

## 10. 文字エンコーディングの決定

| `-Encoding` | `-DetectionLineCount` | 決定方法 | 検証 |
|---|---|---|---|
| 明示（語彙名・WebName・数値・`EncodingInformation`・`Encoding`） | （無視） | その値 | しない |
| `Auto`（既定） | 省略 | ファイル全体で判定する（`ProbedFileReader` と同じ） | しない |
| `Auto`（既定） | 指定 | 先頭 N 行で判定し（2 章）、3.2 の読み替えを行う | **する**（4 章） |

- BOM は、指定にかかわらず常に読み飛ばす（1.1.0 の原則 A。`ProbedFileReader` と同じ）
- 判定に失敗した場合、判定したコードページを実行環境が提供していない場合は、`Get-ProbedContent` と同じ非終了エラー（同じ ID）
- 全体で判定する場合のファイルの大きさの上限は、既存の規則（`MaxDetectableLength`）のとおり。上限を超えた場合のメッセージで、`-DetectionLineCount` を案内する
- 検証を通った後の解析では、検証と同じ厳密な `Encoding` で復号する。解析の途中で復号に失敗した場合（処理中にファイルが書き換えられた場合など）は、
  そこで止めて非終了エラーとする（それまでに出力したレコードは取り消せない）

## 11. 解析

移植した `ImportCsvHelper` のとおりに解析する。主な規則（すべて 7.x の `Import-Csv` と同じ。実測報告 M1 の 7.6.7 の結果と一致すること）:

| 項目 | 規則 |
|---|---|
| 改行 | CR-LF / LF / CR のいずれもレコードの区切りとする（**CR だけの改行も読む**） |
| 引用符の中の改行 | フィールドの一部として、元の改行のまま残す |
| ヘッダーより前の `#` で始まる行 | 読み飛ばす。ただし `#Type`（大文字小文字を区別しない）で始まる最初の行は型名とする |
| ヘッダーより後の `#` で始まる行 | データとして読む |
| `#Fields: ` で始まる行 | W3C 拡張ログ形式のヘッダーとして扱う |
| 型名 | `#TYPE Foo.Bar` なら、出力の `TypeNames` は `Foo.Bar`、`CSV:Foo.Bar` の順（7.x と同じ。5.1 とは順序が逆） |
| 空行 | 読み飛ばす |
| 値の前後の空白 | 移植元のとおり（値の前の空白は除く。引用符の外の後ろの空白は条件により残る） |
| 列より値が多い / 少ない | 多い値は捨てる。足りない列は `$null` |
| ヘッダーの末尾の区切り文字 | 末尾の空の列名は除く |
| 列名が空 | `H1`、`H2`… で補い、警告を出す（実行全体で 1 回） |
| 列名の重複（大文字小文字を区別しない） | そのファイルについてエラー（12 章。24 章 U3） |

## 12. エラーと警告

| 事象 | 扱い | ID |
|---|---|---|
| 解釈できない `-Culture` / `-Strategy`、不正な `-Encoding` | 終了エラー（パラメータ束縛または `BeginProcessing`） | 既存と同じ |
| `-DetectionLineCount` が 0 以下 | 終了エラー（パラメータ束縛） | 標準の検証エラー |
| ファイルが無い・フォルダー | 非終了エラー | `Get-ProbedContent` と同じ |
| ワイルドカード 0 件 | エラーにしない（何も読まない。`Get-ProbedContent` と同じ。9.1） | — |
| 判定の失敗・コードページが無い・全体の判定の上限超過 | 非終了エラー | 既存と同じ |
| 検証の失敗 | 非終了エラー（そのファイルは 1 件も出力しない） | `DetectionVerificationFailed` |
| 列名の重複 | 非終了エラー（そのファイルは 1 件も出力しない）（24 章 U3） | `AlreadyPresentPSMemberInfoInternalCollectionAdd`（標準と同じ ID） |
| 読み取りの失敗（権限など） | 非終了エラー | `Get-ProbedContent` と同じ（`ReadFailed` / `ReadAccessDenied`） |
| 列名が空 | Warning（実行全体で 1 回） | — |

## 13. 標準の `Import-Csv` との差異

| 項目 | 標準 5.1 | 標準 7.x | `Import-ProbedCsv` | 理由 |
|---|---|---|---|---|
| 文字エンコーディング | `-Encoding` の既定は ANSI（日本語環境では CP932）。列挙型で、EUC-JP などは指定できない | 既定は UTF-8 | 判定する（既定 `Auto`）。統一語彙で指定できる | 本モジュールの目的 |
| CR だけの改行 | **0 件（エラーも警告もない）** | 読める | 読める | データを黙って失わないため（7.x に合わせる） |
| W3C `#Fields:` | 未対応 | 対応 | 対応 | 7.x に合わせる |
| `#TYPE` の型名の順序 | `CSV:Foo.Bar` が先 | `Foo.Bar` が先 | `Foo.Bar` が先 | 7.x に合わせる |
| `Get-ChildItem \| ...` | エラー | 読める | 読める | 7.x に合わせる |
| ワイルドカードが複数に一致 | エラー | エラー | 順に読む | 利便性（Probed 系のコマンドに合わせる） |
| 存在しないファイル | 終了エラー（配列の残りも読まない） | 同左 | 非終了エラー（次のファイルへ進む） | 本モジュールのエラー方針（ファイルごとの失敗は非終了） |
| ワイルドカードが 0 件 | 終了エラー | 同左 | エラーにしない（`Get-ProbedContent` と同じ） | 既存の Probed 系コマンドに合わせる（9.1） |
| 列名の重複 | 終了エラー | 終了エラー | 非終了エラー（24 章 U3） | 同上 |
| `-DetectionLineCount` / `-Culture` / `-Strategy` | 無い | 無い | ある | 本モジュールの追加機能 |

---

# 第 4 部 `Export-ProbedCsv`

## 14. 基本方針

`Export-ProbedCsv` は、オブジェクトを CSV の行に変換し、文字エンコーディング・BOM・改行を指定してファイルに書き出す。

- **CSV の文字列化は、PowerShell 7.x の `Export-Csv` から移植した処理で行う**（5 章）。ホストの `Export-Csv` / `ConvertTo-Csv` は呼ばない。
  そのため、**両ホストでバイト単位まで同じ CSV になる**（1.2.0 の `Out-ProbedFile` は整形の層をホストに任せたが、`Export-ProbedCsv` は任せない）
- **符号化の層（文字エンコーディング・BOM・改行・追記・ファイルを開く時点・読み取り専用・`-WhatIf`）は `Out-ProbedFile` の規則に従う**（1.2.0 仕様書 2 章）。
  本書に書いていない点は、1.2.0 仕様書 2 章の該当節のとおりとする
- 書き込み部品（`ProbedFileWriter`・`LineBreakResolver`・`EncodingInheritance`・`ActiveReadRegistry`・`AppendConsistency`・`ReadOnlyAttributeScope`）を共用する
- 出力先は常に 1 件。**失敗はすべて終了エラー**（1.2.0 仕様書 5.1 と同じ基準）
- 標準の `Export-Csv` との差異は 21 章に一覧する

## 15. パラメータ一覧

パラメータセットは 2 つ。`Delimiter`（既定）、`UseCulture`（標準の `Export-Csv` と同じ構成）。
`-Path` / `-LiteralPath` は両方のセットにあり、必須ではない。`BeginProcessing` の最初に「ちょうど一方」を検査し、満たさなければ終了エラー（ID `CannotSpecifyPathAndLiteralPath`。標準と同じ）。

> **改めた経緯（2026-10-19、第三次）:** 当初は `Import-ProbedCsv` と同じ 4 つ（`DelimiterPath` / `DelimiterLiteralPath` / `CulturePath` / `CultureLiteralPath`）としていた。
> 実装したところ、パイプラインから入力を受けて `-LiteralPath` を指定すると、`DelimiterLiteralPath` と `CultureLiteralPath` のどちらか決まらず
> `AmbiguousParameterSet` で失敗した（`Import-ProbedCsv` はパスをパイプラインから受けるのでこの問題が無い）。利用者の決定で、標準の `Export-Csv` と同じ構成に改めた（実装記録 7 章）。

| パラメータ | 型 | 位置 | パイプライン | 別名 | 説明 |
|---|---|---|---|---|---|
| `-InputObject` | `PSObject` | — | ByValue | — | 書き出すオブジェクト。必須 |
| `-Path` | `string` | 0 | — | — | 出力先。ワイルドカードは 1 件に解決されること（1.2.0 仕様書 2.12）。`-LiteralPath` とちょうど一方を指定する |
| `-LiteralPath` | `string` | — | — | `PSPath`, `LP` | 出力先。ワイルドカード非対応。`-Path` とちょうど一方を指定する |
| `-Delimiter` | `char` | 1 | — | — | 区切り文字。`Delimiter` セット。省略時は `,` |
| `-UseCulture` | switch | — | — | — | 区切り文字に、現在のカルチャーのリスト区切り文字を使う。`UseCulture` セット（必須ではない。標準と同じ） |
| `-Encoding` | 多形（書き込み用） | — | — | — | 統一語彙ほか。**位置パラメータにしない**（標準では位置 1 が `-Delimiter` のため）。16 章 |
| `-EncodingFrom` | `string` | — | — | — | 参照ファイルから継承（1.1.0 と同じ） |
| `-LineBreak` | `Auto` / `CrLf` / `Lf` / `Cr` | — | — | — | 各行の後ろに付ける改行 |
| `-Append` | switch | — | — | — | 18 章 |
| `-AllowEncodingChange` | switch | — | — | — | 追記の整合性検査を行わない。`-Append` なしでは Warning |
| `-Force` | switch | — | — | — | 読み取り専用にも書き込む。**`-Append` で列が足りないオブジェクトも許容する**（標準と同じ 2 つの意味） |
| `-NoClobber` | switch | — | — | `NoOverwrite` | 出力先が存在すればエラー |
| `-NoHeader` | switch | — | — | — | ヘッダー行を書かない |
| `-IncludeTypeInformation` | switch | — | — | `ITI` | `#TYPE` 行を書く |
| `-NoTypeInformation` | switch | — | — | `NTI` | **何もしない**（受け付けるだけ）。5.1 向けに書かれたスクリプトがそのまま動くようにする |
| `-UseQuotes` | `Never` / `Always` / `AsNeeded` | — | — | `UQ` | 引用符の付け方。既定は `Always`。**5.1 でも使える** |
| `-QuoteFields` | `string[]` | — | — | `QF` | 引用符で囲む列名。**5.1 でも使える** |
| `-DetectionLineCount` | `long` | — | — | — | 追記先・出力先・参照ファイルの判定に使う先頭の行数。`ValidateRange(1, long.MaxValue)` |
| `-Culture` | `string` | — | — | — | 判定に用いるカルチャー名 |
| `-Strategy` | `string` | — | — | — | 判定方式 |
| `-WhatIf` / `-Confirm` | — | — | — | — | `SupportsShouldProcess` |

- `-Culture` と `-UseCulture` は別のものである（8 章と同じ注意）
- `-NoTypeInformation` と `-IncludeTypeInformation` を同時に指定した場合は、`-IncludeTypeInformation` に従う（`-NoTypeInformation` は何もしないため）
- `-QuoteFields` と `-UseQuotes` の同時指定は終了エラー（標準と同じ。ID `CannotSpecifyQuoteFieldsAndUseQuotes`）

## 16. 文字エンコーディングと改行の決定

### 16.1 文字エンコーディング

1.2.0 仕様書 2.5 の表（`Out-ProbedFile`）と同じ規則とする。要点:

| 指定 | `-Append` なし | `-Append` あり |
|---|---|---|
| 省略 | `utf8NoBOM` | 追記先から継承。追記先が無い、または 0 バイトなら `utf8NoBOM` |
| `Auto` | 出力先の既存ファイルから継承。無ければ終了エラー | 追記先から継承。無ければ終了エラー |
| 明示 | その値 | その値（整合性検査あり） |

`-Encoding` と `-EncodingFrom` の同時指定は終了エラー（1.1.0 と同じ）。

### 16.2 `-DetectionLineCount` が効く箇所

既存ファイルを判定するすべての箇所に効く。

| 箇所 | 判定する対象 |
|---|---|
| `-EncodingFrom` | 参照ファイル |
| 明示的な `-Encoding Auto` | 出力先の既存ファイル |
| `-Append`（`-Encoding` 省略時の継承、`-Encoding` 明示時の整合性検査の基準） | 追記先の既存ファイル |

`-DetectionLineCount` を指定した場合は、どの箇所でも、先頭 N 行で判定し（2 章）、3.2 の読み替えを行い、**ファイルに書く前に**全体を検証する（4 章）。
検証が失敗したら、何も書かずに終了エラーとする。

> **この規則が必要な理由:** 先頭が英数字だけで後半に日本語がある Shift_JIS の CSV に追記する場合、先頭 N 行の判定では us-ascii となり、
> 3.2 の読み替えで UTF-8 とみなす。検証が無ければ、UTF-8 の行が Shift_JIS のファイルに追記され、二つの文字エンコーディングが混ざる。
> 追記の整合性検査（バイト列の比較）では防げない。比較の基準である追記先の文字エンコーディングそのものを取り違えているからである。

検証は読み取りだけで、ファイルを変更しない。1.2.0 の方針（ファイルを変更しない検査は `ShouldProcess` より前に置く）に従い、
**`ShouldProcess` より前**に行う。`-WhatIf` でも、検証の失敗を同じ ID で報告する。

### 16.3 改行

1.2.0 仕様書 2.6 と同じ。

- `-LineBreak` の明示 → その値
- 参照情報がある（`-EncodingFrom`、明示的な `-Encoding Auto`、`-Encoding <EncodingInformation>`、`-Append` で追記先から継承した場合）→ 参照元の改行（混在は `LineBreakResolver.FromDetected` の規則）
- それ以外 → `Environment.NewLine`（標準の `Export-Csv` と同じく OS の既定。RFC 4180 の CR-LF 固定にはしない）

`-DetectionLineCount` を指定した場合、参照元の改行は先頭 N 行の範囲で判定したものになる。

**`-LineBreak` が決めるのは、各レコード（とヘッダー）の後ろに付ける改行だけである。** 値の中の改行（`"l1`nl2"` など）は置き換えない（標準と同じ）。

## 17. CSV の書き出し

移植した `ExportCsvHelper` のとおりに書き出す。主な規則（すべて 7.x の `Export-Csv` と同じ。実測報告 M4 の 7.6.7 の結果と一致すること）:

| 項目 | 規則 |
|---|---|
| 列 | 最初のオブジェクトのプロパティ（拡張・アダプテッド）で決め、順序を固定する。2 件目以降で足りない列は空、余分なプロパティは書かない（24 章 U1） |
| 辞書（`IDictionary`） | キーを列とし、ETS で追加された拡張メンバーも列に加える（5.1 の標準は辞書オブジェクト自体のプロパティ `Count` / `Keys`… を書く） |
| 値の文字列化 | プロパティの値の `ToString()`。`$null` と取得時の例外は値なし（区切り文字だけ）。日付・数値は現在のカルチャーの書式になる |
| 引用符（`Always`） | すべての値を `"` で囲み、値の中の `"` は `""` にする。値が無い（`$null`）列は何も書かない |
| 引用符（`AsNeeded`） | 区切り文字、LF、`"` のいずれかを含む値だけ囲む（**CR だけを含む値は囲まない。移植元のとおり**） |
| 引用符（`Never`） | 囲まない（値に区切り文字があると CSV が壊れる。標準と同じ） |
| `-QuoteFields` | 指定した列（大文字小文字を区別しない）だけ囲む |
| ヘッダー行 | 列名を、値と同じ引用符の規則で書く |
| `#TYPE` 行 | `-IncludeTypeInformation` を指定したときだけ、ヘッダーの前に書く。型名が `CSV:` で始まる場合は除く |
| 区切り文字 | `-Delimiter`、`-UseCulture`（`CurrentCulture.TextInfo.ListSeparator` の 1 文字目）、既定は `,` |

## 18. `-Append`

### 18.1 共通の規則

1.2.0 仕様書 2.8 と同じ。

- 追記先の既存ファイル（1 バイト以上）には BOM を書かない
- 追記先が無い、または 0 バイトなら、新規作成と同じ扱い（ヘッダーを書く。`-NoHeader` なら書かない）
- 整合性検査（バイト列比較）を行う。`-AllowEncodingChange` で行わない

### 18.2 列の照合

追記先（1 バイト以上）のヘッダーを、移植した `ReadHeader` で読む（判定した文字エンコーディングで、BOM の後ろから復号する）。

- 追記先のヘッダーの列の**順序と表記**で書く（列名の照合は大文字小文字を区別しない。実測報告 M5 i）
- 追記するオブジェクトに、追記先の列に当たるプロパティが無い場合は終了エラー（ID `CannotAppendCsvWithMismatchedPropertyNames`）。
  `-Force` を指定した場合は、その列を空にして書く
- 追記するオブジェクトの、追記先の列に無いプロパティは書かない（標準と同じ。24 章 U1）
- ヘッダー行と `#TYPE` 行は書かない
- `-NoHeader` は受け付ける（PowerShell 7.6.7 と同じ。master のソースは拒否するが、リリース版に合わせる）。列の照合は `-NoHeader` が無い場合と同じに行う
- 追記先が 1 バイト以上で、ヘッダーが読めない（空行と `#` の行だけなど）場合は、移植元の挙動に合わせる。挙動を実測報告と同じ形で確かめ、実装記録に残す

### 18.3 追記先の最後の行に改行が無い場合

**標準の `Export-Csv` は、追記先の最後の行に改行が無いと、新しい行をつなげてしまう**（実測報告 M5 j: `1,2"x","y"`。両ホストで同じ）。CSV が壊れる。
`Export-ProbedCsv` は、これを直す。

- 追記先の末尾が改行（判定した改行の型での LF または CR）で終わっていない場合は、**最初のレコードを書く前に、16.3 で決めた改行を 1 個書く**
- 末尾の判定は、2.3 の改行の型（追記先の文字エンコーディングから決まる単位）で行う。UTF-16 / UTF-32 では、最後の 2 / 4 バイトを見る
- この改行も整合性検査の対象とする（改行は ASCII なので、通常は検査を通る）
- 入力が 0 件の場合は、追記先に触れない（改行も書かない。1.2.0 仕様書 2.11）
- 追記先が 0 バイトの場合は、新規作成と同じ扱いなので、改行を書かない

## 19. ファイルを開く時点・空の入力・その他

すべて `Out-ProbedFile` と同じ（1.2.0 仕様書の該当節）。

| 項目 | 規則 | 1.2.0 仕様書 |
|---|---|---|
| ファイルを開く時点 | 最初の行を書く直前。`Import-ProbedCsv a.csv \| Export-ProbedCsv a.csv` は `SamePathRoundTrip` で止まり、元のファイルは壊れない | 2.10 |
| `BeginProcessing` の検査の順序 | パラメータ → パス → `-NoClobber` → 読み取り専用 → `-EncodingFrom` の判定（と検証）→ 出力先・追記先の判定（と検証）・追記先のヘッダーの読み取り → `ShouldProcess` | 2.10 |
| 空の入力 | `-Append` なし: 0 バイトで作成（既存は 0 バイトに切り詰める）。`-Append` あり: 既存に触れない、無ければ 0 バイトで作成（標準の `Export-Csv` と同じ。実測報告 M6） | 2.11 |
| BOM | 1 行でも書いたときにだけ書く | 2.11 |
| パスの解決 | ちょうど 1 件に解決されること | 2.12 |
| `-NoClobber` / `-Force` / 読み取り専用 | 同じ組み合わせの表。読み取り専用は、書き込み後に属性を戻す | 2.9 |
| `-WhatIf` / `-Confirm` | 出力先 1 件につき 1 回 | 2.14 |
| パイプラインの停止 | ファイルを閉じ、読み取り専用の属性を戻す | 8 章 |

追記先のヘッダーの読み取り（18.2）は読み取りだけなので、`ShouldProcess` より前に行う。
列の照合のエラー（`CannotAppendCsvWithMismatchedPropertyNames`）は、最初のオブジェクトが届くまで判定できないため、`ShouldProcess` の後になる。

## 20. エラーと警告

すべて終了エラー（Warning を除く）。

| 事象 | 起きる時点 | ID |
|---|---|---|
| 裸の `utf8` / `utf7` / 接尾辞を許さない語彙への接尾辞 | パラメータ束縛 | 既存と同じ |
| `-DetectionLineCount` が 0 以下 | パラメータ束縛 | 標準の検証エラー |
| `-Encoding` と `-EncodingFrom` の同時指定 | `BeginProcessing` | 既存と同じ |
| `-QuoteFields` と `-UseQuotes` の同時指定 | `BeginProcessing` | `CannotSpecifyQuoteFieldsAndUseQuotes` |
| `-Path` と `-LiteralPath` の両方の指定・どちらも指定しない（15 章） | `BeginProcessing` | `CannotSpecifyPathAndLiteralPath` |
| 解釈できない `-Culture` / `-Strategy` | `BeginProcessing` | 既存と同じ |
| パスの解決の失敗 | `BeginProcessing` | `Out-ProbedFile` と同じ |
| `-NoClobber` で出力先が存在する | `BeginProcessing` | `NoClobber` |
| 読み取り専用で `-Force` なし | `BeginProcessing` | `WriteAccessDenied` |
| `-EncodingFrom` の参照先が無い・判定の失敗 | `BeginProcessing` | 既存と同じ |
| 明示的な `-Encoding Auto` で出力先が無い | `BeginProcessing` | 既存と同じ |
| 出力先・追記先の判定の失敗 | `BeginProcessing` | 既存と同じ |
| 検証の失敗（`-DetectionLineCount` 指定時） | `BeginProcessing` | `DetectionVerificationFailed` |
| 追記先の列に当たるプロパティが無い（`-Force` なし） | 最初のオブジェクト | `CannotAppendCsvWithMismatchedPropertyNames` |
| 同一パスの読み書き | 最初の行を書く直前 | `SamePathRoundTrip` |
| `-Append` でのバイト列不一致 | 不一致の行を書く直前 | 既存と同じ |
| 書き込み権限なし（読み取り専用以外） | ファイルを開く時点 | 既存と同じ |
| `-Append` なしの `-AllowEncodingChange` | `BeginProcessing` | Warning |

`Export-ProbedCsv` は `-NoNewline` を持たない（標準の `Export-Csv` にも無い）。

## 21. 標準の `Export-Csv` との差異

| 項目 | 標準 5.1 | 標準 7.x | `Export-ProbedCsv` | 理由 |
|---|---|---|---|---|
| `-Encoding` の既定 | **ASCII（日本語は `?` になる）** | BOM 無し UTF-8 | 新規は `utf8NoBOM`、追記は追記先から継承 | ホストによらず同じ結果。文字を失わない |
| `-Encoding UTF8` | BOM 付き | BOM 無し | 統一語彙（`utf8BOM` / `utf8NoBOM`）で明示。裸の `utf8` は拒否 | 1.1.0 の統一語彙 |
| `#TYPE` 行の既定 | 書く | 書かない | 書かない | 7.x に合わせる |
| 0 バイト・存在しないファイルへの `-Append` | `#TYPE` 行を書く | 書かない | 書かない | 同上 |
| `-UseQuotes` / `-QuoteFields` / `-NoHeader` | 無い | ある | 両ホストである | 同上 |
| 辞書の書き出し | 辞書オブジェクト自体のプロパティ | キーを列にする | キーを列にする | 同上 |
| 文字エンコーディングの違うファイルへの `-Append` | 混ざる（実測報告 M5 g） | 混ざる | 追記先から継承し、整合性を検査する | 混在を防ぐ |
| 改行の違うファイルへの `-Append` | OS の既定の改行で書く（混ざる。M5 k） | 同左 | 追記先の改行を継承する | 混在を防ぐ |
| 追記先の最後の行に改行が無い | 行がつながる（M5 j） | 同左 | 改行を補ってから追記する | 標準の欠陥の修正 |
| 同じファイルの読み書き | 0 バイトになる（M6） | 同左 | `SamePathRoundTrip` で止める | 1.2.0 と同じ |
| ファイルを切り詰める時点 | 開始時 | 開始時 | 最初の行を書く直前 | 同上 |
| `-Append -NoHeader` | （`-NoHeader` が無い） | 受け付ける（7.6.7） | 受け付ける | 7.x のリリース版に合わせる |
| `-LineBreak` / `-EncodingFrom` / `-AllowEncodingChange` / `-DetectionLineCount` / `-Culture` / `-Strategy` | 無い | 無い | ある | 本モジュールの追加機能 |
| `-InputObject` の `ByPropertyName` | ある | ある | 無い（`ByValue` だけ） | `Out-ProbedFile` と同じ。意図しない束縛を避ける |

---

# 第 5 部 共通事項

## 22. ローカライズとヘルプ

- `MessageCatalog` に、新しいメッセージ（`DetectionVerificationFailed`、列の照合、列名の重複、`-QuoteFields` と `-UseQuotes` の同時指定など）と Warning を、
  英語・日本語・韓国語・繁体字中国語・簡体字中国語で追加する。香港・マカオは台湾版と同じ文面（B 案）
- MAML ヘルプ（5 言語）に `Import-ProbedCsv` / `Export-ProbedCsv` を追加し、`Resolve-Encoding` に `-DetectionLineCount` を追加する。`zh-TW` を `zh-HK` / `zh-MO` へ複製する
- `Import-ProbedCsv` / `Export-ProbedCsv` のヘルプの NOTES に次を記載する
  - CSV の規則は両ホストで PowerShell 7.x に合わせていること。標準との差異（13 章 / 21 章）
  - `-Culture`（判定のカルチャー）と `-UseCulture`（区切り文字）の違い
  - `-DetectionLineCount`: 行数は物理的な行数でレコード数ではないこと、全体を検証するので 2 回読むこと、`-Encoding` の明示で判定も検証も省けること
  - シングルバイト系の判定の限界（1.2.0 で記載済みの文面を流用する）。検証はシングルバイト系の誤判定を見つけられないことがある
  - `Export-ProbedCsv`: `-Encoding` の省略と `Auto` の違い（`Out-ProbedFile` と同じ）。Excel で開く CSV には `utf8BOM` か `shift_jis` を使うとよいこと
  - `Export-ProbedCsv`: 2 件目以降のオブジェクトの余分なプロパティは書かれないこと（24 章 U1 の決定に従って文面を決める）
- `Resolve-Encoding -License` の出力を更新する（5.3）

## 23. テスト

PowerShell 層のテストとして追加し、PSCompat で 5.1 と 7.x の結果が一致することを確認する。期待値は実測報告の 7.6.7 の値を使う。

| 区分 | 内容 |
|---|---|
| 切り出し（2 章） | 5 つの改行の型 × BOM の有無 × CR-LF / LF / CR。N が 1・途中・改行の数以上。CR-LF が単位の境目・バッファの境目にかかる場合。UTF-16LE の `0A 4E`（「上」）のような改行でない `0A`。改行の無いファイル。0 バイト。切り出した長さが単位の倍数であること。2.4 の既知の限界の例（挙動の固定） |
| 一致（3.3） | 同じファイル・同じ N で、`Resolve-Encoding` と `Import-ProbedCsv` / `Export-ProbedCsv` の判定結果（読み替え前）が一致すること |
| 読み替え（3.2） | 先頭が ASCII の UTF-8 / Shift_JIS / EUC-JP。UTF-8 は読めること、それ以外は検証で止まること。先頭 N 行が英数字だけの BOM 無し UTF-16LE / BE・UTF-32LE / BE（英数字だけのファイル、後半に日本語があるファイルの両方）が、その文字エンコーディングで正しく読めること |
| 検証（4 章） | 不正なバイト列が先頭・バッファの境目・末尾（途切れた多バイト文字）にある場合。報告する位置が両ホストで同じこと。64 KiB を超えるファイル |
| 移植（11 章 / 17 章） | 実測報告 M1 の 15 ケースすべてで、7.6.7 の `Import-Csv` と同じ結果になること（**5.1 でも**）。M4 の書き出しが 7.6.7 とバイト単位で同じになること（**5.1 でも**）。辞書・混在オブジェクト・文字列・区切り文字・`-UseCulture`・`-UseQuotes` の 3 値・`-QuoteFields` |
| 往復 | `Export-ProbedCsv` で書いたものを `Import-ProbedCsv` で読むと元の値に戻ること（引用符・区切り文字・改行・日本語を含む値） |
| `Import-ProbedCsv` のパス | ワイルドカードで複数、配列、`Get-ChildItem` からの入力（両ホスト）、存在しないファイルの非終了エラーと続行 |
| `Import-ProbedCsv` の判定 | 10 章の表の全行。Shift_JIS / EUC-JP / UTF-8（BOM 有無）/ UTF-16LE / BE |
| `Export-ProbedCsv` の決定 | 16.1 の表の全セル。16.2 の 3 つの箇所すべてで検証が効くこと（失敗時に何も書かれないこと、`-WhatIf` でも報告されること） |
| `-Append` | 18 章の全項目。実測報告 M5 の a〜l に対応するケースで、21 章の差異のとおりになること |
| その他 | 19 章の表の全行（同じファイルの往復、空の入力、`-NoClobber`、読み取り専用と属性の復元、`-WhatIf`） |
| エラー | 12 章・20 章の表の全行 |
| ライセンス | `Resolve-Encoding -License` に PowerShell の表示が含まれること |
| 既存 | 既存のテスト・PSCompat がすべて通ること |

テストデータは、バイト列を明示して生成する（既存の書き込みコードで作ると、バグが自己整合して検出できない）。

## 24. 未決事項（実装前に利用者へ確認する）

| # | 事項 | 推奨 |
|---|---|---|
| U1 | `Export-ProbedCsv` が、2 件目以降のオブジェクトや追記するオブジェクトの**余分なプロパティを黙って書かない**ことを、そのままにするか | **標準どおり（警告しない）。** 列が最初のオブジェクトで決まるのは CSV の性質で、`Export-Csv` の文書化された挙動である。異なる型を流す正当な使い方で警告が多発する。ヘルプの NOTES に書く |
| U2 | `Export-ProbedCsv -EncodingFrom` の参照ファイルも、`-DetectionLineCount` 指定時に全体を検証するか | **検証する。**「先頭 N 行で判定した結果は、全体で確かめてから使う」という規則に例外を作らないため。参照ファイルは書き込まないが、誤った文字エンコーディングで新しいファイルを作るのを防げる |
| U3 | `Import-ProbedCsv` で列名が重複するファイルを、非終了エラー（次のファイルへ進む）にするか。標準は終了エラー | **非終了エラー。** 本モジュールのエラー方針（ファイルごとの失敗は非終了）に合わせる。複数のファイルを読むときに、1 ファイルの問題で全体が止まらない |
| U4 | 3.2 の読み替えで、先頭 N 行が `us-ascii` のときに、切り出しで決めた改行の型（UTF-16 / UTF-32）を使うか | **使う。** 改行の型は `0x00` とバイトの位置から決まっており、UTF-8 と読み替えるより確かである。使わないと、2 バイトとも `0x80` 未満になる文字を含む BOM 無しの UTF-16 / UTF-32 の CSV（追補 4.1）を `U+0000` 混じりで読んでしまう（当初の理由は「英数字だけの BOM 無し UTF-16 の CSV」だった。追補で判定処理を直したため改めた） |

### 24.1 既存の課題（1.3.0 で対応した）

全体で判定する場合も、BOM 無しの UTF-16 / UTF-32 で英数字だけのファイルは `us-ascii` と判定されると考えられる（3.2 の理由と同じ。`Get-ProbedContent` などに影響する）。
クラスライブラリの判定処理の課題であり、1.3.0 では扱わない。第一次で挙動を確かめ、確かであれば課題の文書（`docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md`）を起票する。

> **1.3.0 で対応した（追補）。** 第一次で挙動を確かめて課題を起票し、2026-10-20 に利用者が 1.3.0 で直すと決めた。
> クラスライブラリの判定処理を直し（追補 2 章）、英数字だけの BOM 無し UTF-16 / UTF-32 は全体の判定でもその文字エンコーディングになる。
> 2 バイトとも `0x80` 未満になる文字や U+0000 を含むもの、数バイトの入力は既知の限界として残る（追補 4 章）。

---

## 付録 A ライセンス表示の文面

### A.1 移植したソースファイルの冒頭

```csharp
// Portions of this file are derived from PowerShell
// (https://github.com/PowerShell/PowerShell),
// src/Microsoft.PowerShell.Commands.Utility/commands/utility/CsvCommands.cs
// at commit db1d0a56cbe41adf05d0bad90be5190a6655404f.
// Copyright (c) Microsoft Corporation. Licensed under the MIT License.
// Modified by Motoi Tsushima for SnowStack.EncodingProbe.PowerShell.
// See THIRD-PARTY-NOTICES.txt for the full license text.
```

移植したコードを複数のファイルに分ける場合は、すべてのファイルに付ける。

### A.2 `THIRD-PARTY-NOTICES.txt`

冒頭を次のように改め、既存の UTF.Unknown の節はそのまま残し、その後ろに PowerShell の節を加える。

```text
This file lists the third-party components included in the packages
built from this repository.

  SnowStack.EncodingProbe (NuGet package)
    - UTF.Unknown

  SnowStack.EncodingProbe.PowerShell (PowerShell module)
    - UTF.Unknown (through SnowStack.EncodingProbe)
    - PowerShell (portions of the CSV import/export implementation)


UTF.Unknown
-----------
（既存の節をそのまま残す）


PowerShell
----------

Homepage:    https://github.com/PowerShell/PowerShell
Source file: src/Microsoft.PowerShell.Commands.Utility/commands/utility/CsvCommands.cs
Commit:      db1d0a56cbe41adf05d0bad90be5190a6655404f

Portions of the CSV import/export implementation in
SnowStack.EncodingProbe.PowerShell (Import-ProbedCsv / Export-ProbedCsv)
are derived from the source file above and have been modified.
This code is included only in the PowerShell module, not in the
SnowStack.EncodingProbe NuGet package.

The PowerShell license is reproduced below.

Copyright (c) Microsoft Corporation.

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

ライセンス本文は、実装時に移植元のコミットの `LICENSE.txt` と**1 文字ずつ照合**すること（上の本文は 2026-10-15 に取得したもの）。

### A.3 `LicenseAndHelp.License` への追記

既存の文面の後ろに、次の趣旨の英文を加える（`Resolve-Encoding -License` の出力）。

```text
This module includes portions of PowerShell
(https://github.com/PowerShell/PowerShell), Copyright (c) Microsoft Corporation,
licensed under the MIT License. See THIRD-PARTY-NOTICES.txt for details.
```
