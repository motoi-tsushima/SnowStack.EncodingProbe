# Claude Code 実装指示書 — SnowStack.EncodingProbe.PowerShell 1.3.0

- 作成日: 2026-10-15
- 機能仕様: `docs/EncodingProbe-1.3.0-仕様書.md`（以下「仕様書」）
- 標準コマンドの挙動の根拠: `docs/EncodingProbe-1.3.0-調査-CSVコマンド挙動の実測.md`（以下「実測報告」）
- 作業の段階ごとの依頼: `docs/EncodingProbe-1.3.0-依頼-第一次.md` / `第二次.md` / `第三次.md`

本書は実装作業の制約と注意点を定める。**仕様書に書かれていない挙動を推測で実装しないこと。**
判断が必要な箇所に遭遇したら、実装を進める前に確認を求めること。
本仕様は利用者との議論で細部まで決定されており、推測による補完は意図と食い違う可能性が高い。

---

## 0. 最初に行うこと

1. CLAUDE.md、仕様書、実測報告、本書を通読する。1.2.0 仕様書の 2 章（`Out-ProbedFile`）も読む。`Export-ProbedCsv` の符号化の層はそれに従う
2. 既存の実装を確認する。特に次のもの
   - `Cmdlets/ProbedContentCommandBase`（`-Culture` / `-Strategy`、パスの解決）、`Cmdlets/GetProbedContentCommand`
   - `Cmdlets/OutProbedFileCommand`（`Export-ProbedCsv` の手本。ファイルを開く時点、`BeginProcessing` の検査の順序、空の入力）
   - `Internal/ProbedFileReader`、`EncodingInheritance`、`LineBreakResolver`、`AppendConsistency`、`ActiveReadRegistry`、`ReadOnlyAttributeScope`、`EncodingVocabulary.BuildStrictEncoding`
   - `Cmdlets/ConvertProbedContentCommand.FindInvalidBytes`（不正なバイト列の位置の求め方）
3. **既存の実装スタイルに合わせる。** 本書の名前（クラス名など）は説明のための仮のものであり、命名とファイル配置は既存のコードに従うこと
4. 依頼文の段階に入る前に、その段階の実装計画（作成・変更するファイルの一覧と作業順序）を提示すること

---

## 1. 変更してはならないもの

- **クラスライブラリ（`SnowStack.EncodingProbe` プロジェクト）のすべて。** 1.3.0 は PowerShell モジュールだけのリリースである。
  クラスライブラリに手を入れる必要が生じたら、実装を止めて報告すること
- クラスライブラリのバージョン（csproj の `Version` / `AssemblyVersion` / `FileVersion`）。1.2.0 のまま

> **例外（第四次、2026-10-20）:** `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md` の変更（判定処理の追補 2 章の点と、
> クラスライブラリのバージョンを 1.3.0 に上げること）だけは、この制約を解除する。利用者の決定による（追補 1 章）。
> それ以外のクラスライブラリの変更（判定順序・他の判定メソッド・公開 API）は、引き続き禁止する。
- `Resolve-Encoding` の**既存の**パラメータと戻り値。`-DetectionLineCount` の追加だけを行う（仕様書 1.3 の例外）
- `Get-EncodingProbePlatformInfo`、`EncodingInformation` 型
- 既存の Probed 系コマンド（`Get-` / `Set-` / `Add-ProbedContent`、`Out-ProbedFile`、`Convert-ProbedContent`、`ConvertTo-DotNetEncoding`）の挙動。
  共通部品の抽出などで既存のコードを整理するのはよいが、挙動を変えないこと（既存のテストと PSCompat がすべて通ること）

---

## 2. 追加するもの

| 種別 | 名称 | 仕様書 |
|---|---|---|
| 内部部品 | 先頭 N 行の切り出し（仮称 `HeadLineCutter`） | 2 章 |
| 内部部品 | 全体の厳密な検証（仮称 `StrictDecodeVerifier`） | 4 章 |
| 内部部品 | CSV の読み書き（PowerShell からの移植。仮称 `Internal/Csv/`） | 5 章 |
| パラメータ | `Resolve-Encoding -DetectionLineCount` | 6 章 |
| コマンドレット | `Import-ProbedCsv` | 第 3 部 |
| コマンドレット | `Export-ProbedCsv`（`SupportsShouldProcess`） | 第 4 部 |
| 文書・表示 | `THIRD-PARTY-NOTICES.txt`、`LicenseAndHelp.License`、移植ファイルの冒頭 | 5.3、付録 A |

---

## 3. 移植についての制約（最重要）

### 3.1 ライセンス

- 移植元は `PowerShell/PowerShell` の `CsvCommands.cs`、コミット `db1d0a56cbe41adf05d0bad90be5190a6655404f`（仕様書 5.1）。
  **このコミットのファイルを取得して移植すること。** master の最新版ではない
- 移植したコードは 1 か所にまとめ、すべてのファイルの冒頭に仕様書 付録 A.1 の表示を付ける
- `THIRD-PARTY-NOTICES.txt` に付録 A.2 の文面を入れる。ライセンス本文は、移植元のコミットの `LICENSE.txt` と 1 文字ずつ照合すること（`*AS IS*` のアスタリスクを含む）
- `LicenseAndHelp.License` に付録 A.3 の趣旨を追記する。クラスライブラリの `EncodingProbe.License` は変更しない（1 節）
- CLAUDE.md の「ライセンス上の注意」に、PowerShell 由来のコードの表示箇所（上の 3 か所と、配布物への同梱）を追記する

### 3.2 参照してはならないもの

- **Windows PowerShell 5.1 のバイナリを逆コンパイルして参照しない**（仕様書 5.4）。5.1 の挙動は実測報告だけを根拠とする
- PowerShell のリポジトリのうち、移植元のファイル以外のコードは移植しない。API の調べものとして読むのはよい

### 3.3 移植の方針

- 解析・書き出しの規則は**移植元のとおり**とする。「より良い」と思える変更でも加えない。仕様書で明示した変更は、移植したコードの外（コマンド側）で行う
- 移植元のうち、コマンドレットの殻（`ImportCsvCommand` / `ExportCsvCommand` / `ConvertFromCsvCommand` / `ConvertToCsvCommand`）とパスの処理（`PathUtils`）は移植しない。
  本モジュールの既存の部品を使う
- 移植元の `string.StartsWith(string)`（カルチャー依存の比較）は `StringComparison.Ordinal` にしてよい（比較する文字列はすべて ASCII なので結果は変わらない）。変えた場合は実装記録に書く

### 3.4 内部 API の置き換え

移植元は PowerShell 本体の一部なので、`System.Management.Automation` の `internal` の API を使っている。本モジュールからは使えない。
**公開 API で同じ結果になるように置き換え、結果が同じであることをテストで確かめること。**

| 移植元が使っているもの | 状況（コミット `db1d0a56` で確認） | 対応 |
|---|---|---|
| `PSMemberInfoIntegratingCollection<T>` | `internal class`（`MshMemberInfo.cs`） | 公開 API（`PSObject.Properties` など）で置き換える |
| `PSObject.GetPropertyCollection(PSMemberViewTypes)` | `internal static`（`MshObject.cs`） | 同上。`BuildPropertyNames` の「拡張とアダプテッドのプロパティ」「辞書の拡張メンバー」を公開 API で求める |
| `PSMemberViewTypes` | `public enum` | 使える |
| `PSMemberInfoCollection<T>.Add(T, bool preValidated)` | 7.x では `public abstract` | **Windows PowerShell 5.1 の参照アセンブリ（net48 ビルド）でも公開されているか確かめる。** 無ければ `Add(T)` を使う |
| `new PSObject(int)` | 7.x では `public` | 同上。5.1 に無ければ `new PSObject()` を使う |
| `CsvCommandStrings`、`ExtendedTypeSystem.MemberAlreadyPresent` | リソース（使えない） | `MessageCatalog` に 5 言語で持つ |
| `PSTraceSource`、`Diagnostics.Assert` | 内部 | 通常の例外や `Debug.Assert` に置き換える |
| `LanguagePrimitives.ConvertTo<string>` | `public` | 使える |

`BuildPropertyNames` の置き換えは、実測報告 M4 の「ordered dictionary」「hashtable に NoteProperty を追加」「mixed objects」「strings」の 4 ケースで、
7.6.7 の `ConvertTo-Csv` と同じ列になることを、**両ホストで**確かめること。`[pscustomobject]`、`Get-Process` などの .NET オブジェクト、
`Select-Object` の結果、`types.ps1xml` で拡張メンバーを持つ型（`System.IO.FileInfo` など）でも、7.x の `ConvertTo-Csv` と比べること。
差が出たら、実装を止めて報告すること。

### 3.5 net48 でのビルド

モジュールは net48（Windows PowerShell 5.1 用）でもビルドされる。移植元には net48 に無い機能が使われている。

| 移植元の記述 | 置き換え |
|---|---|
| `value.AsSpan().IndexOfAny(_delimiter, '\n', '"')` | `string.IndexOfAny(char[])` など。**`System.Memory` への依存を持ち込まないこと**（PowerShell 5.1 で読み込めなくなる。CLAUDE.md の UTF.Unknown の参照バージョンの節と同じ理由） |
| `ArgumentNullException.ThrowIfNull` | 従来の `if (x == null) throw new ArgumentNullException(...)` |
| `string.Create(CultureInfo.InvariantCulture, $"...")` | `string.Format(CultureInfo.InvariantCulture, ...)` など |
| `string.StartsWith(char)` | `StartsWith("#", StringComparison.Ordinal)` など |
| ターゲット型の `new(...)` | 既存のコードのスタイルに合わせる（`PolySharp` で使えるものは使ってよい） |

---

## 4. 実装上の注意点

### 4.1 先頭 N 行の切り出し（仕様書 2 章）

- バッファの境目で CR-LF が分かれる場合、UTF-16 / UTF-32 の改行の単位が分かれる場合を必ずテストする
- 型の決定（2.4）で、UTF-32 の改行は UTF-16 の改行の形も含む（`0A 00 00 00` は `0A 00` で始まる）。優先順位を守ること
- 位置の条件は「ファイル先頭からのオフセット（BOM を含む）」で判定する。BOM の後ろからではない
- 切り出しの上限（`MaxDetectableLength`）を超えそうなときは、上限まで読んでからではなく、超えた時点で打ち切ってエラーにする（巨大なファイルを無駄に読まない）

### 4.2 全体の厳密な検証（仕様書 4 章）

- `EncodingVocabulary.BuildStrictEncoding` を使う。独自に `Encoding` を組み立てない
- **不正なバイト列の位置に `DecoderFallbackException.Index` を使わない。** .NET Framework と .NET Core で基準が違い、5.1 と 7.x で異なる位置を報告する（1.2.0 で判明済み。CLAUDE.md）。
  失敗したときだけ、`FindInvalidBytes` の考え方で再走査してよい。`Decoder` の状態は複製できないので、バッファの境目にかかった多バイト文字の扱いに注意すること。
  正しい位置を求める方法に迷ったら、方針を報告してから実装すること
- 5.1 と 7.x で同じ位置・同じバイト列を報告することを、PSCompat で確かめる

### 4.3 パラメータセットと名前

- `Import-ProbedCsv` / `Export-ProbedCsv` は、パラメータセットが 4 つ（Delimiter / Culture × Path / LiteralPath）になる。
  `ProbedContentCommandBase` のパラメータセット名の定数（`Path` / `LiteralPath`）と組み合わせ方を検討し、既存のコマンドの束縛を壊さないこと
- **`-Culture`（判定のカルチャー）と `-UseCulture`（区切り文字）が同じコマンドに並ぶ。** 名前が紛らわしいので、パラメータセット名に `Culture` を使う場合も、
  コード上の名前・コメント・ヘルプで取り違えないこと
- 基底クラスの `BeginProcessing` を持つ場合は、派生側で**必ず `base.BeginProcessing()` を先に呼ぶ**（CLAUDE.md）

### 4.4 `Import-ProbedCsv`

- `Get-ChildItem | Import-ProbedCsv` が**両ホストで**動くこと。パイプライン束縛の属性は `Get-ProbedContent` に合わせる（仕様書 9.1）
- 読み込み中は、ファイルを `ActiveReadRegistry` に登録する。1 レコードずつ出力するので、下流の `Export-ProbedCsv` が同じファイルに書こうとしたら検出できる
- 列名の重複や検証の失敗は、**1 件も出力する前に**分かるようにする（ヘッダーの読み取りと検証を、最初のレコードの出力より前に行う）
- 列名が空のときの警告を、実行全体で 1 回だけにする（移植元の `_alreadyWarnedUnspecifiedNames` の扱い。複数のファイルにまたがる）

### 4.5 `Export-ProbedCsv`

- **`Out-ProbedFile` の構造（ファイルを開く時点、`BeginProcessing` の検査の順序、`EndProcessing` の空の入力、`StopProcessing`、`Dispose`）をそのまま踏襲する。**
  1.2.0 で手動確認の後に直した点（`-WhatIf` でも検査のエラーを報告する、など）を落とさないこと
- 標準の `Export-Csv` / `ConvertTo-Csv` を呼ばない（両ホストで同じバイト列にするため）
- 追記先のヘッダーの読み取りと、末尾が改行で終わっているかの判定（仕様書 18.3）は、判定した文字エンコーディングに基づいて行う。
  UTF-16 / UTF-32 の追記先でもテストする
- `-Append` の整合性検査は、補う改行（18.3）にも適用する

### 4.6 ホストの CSV コマンドを実装に使わない

`Import-Csv` / `ConvertFrom-Csv` / `Export-Csv` / `ConvertTo-Csv` を実装の中で呼ばないこと。
テストでは、PowerShell 7.x 上で標準の結果と比較するために呼んでよい（5.1 上の標準とは比較しない。5.1 の標準とは違う結果が正しい）。

### 4.7 エラーの分類

仕様書 12 章（`Import-ProbedCsv`、ファイルごとの失敗は非終了）と 20 章（`Export-ProbedCsv`、すべて終了）に従う。
終了エラーのとき、`-ErrorVariable` には `ErrorRecord` ではなく例外オブジェクトが入る（1.2.0 仕様書 5.2）。テストではこの点を前提にすること。

### 4.8 モジュールへの登録（CLAUDE.md「バージョン更新時に触る場所」）

- `.psd1` の `CmdletsToExport` に `Import-ProbedCsv` / `Export-ProbedCsv` を追加する
- MAML ヘルプ 5 言語に追加し、`zh-TW` を `zh-HK` / `zh-MO` へ複製する（`MamlHelpTests` が骨格と複製の一致を検査する）
- `tests/EncodingProbe.PowerShell.Tests/Helpers/ProbedCommandRunspaceFixture.cs` に登録する
- `tests/PSCompat/ProbedCompatScenarios.ps1` にシナリオを追加する（`.ps1` は UTF-8 BOM 付きで保存）

### 4.9 バージョン

- 上げるのは**モジュールだけ**: `SnowStack.EncodingProbe.PowerShell.csproj`（`Version` / `AssemblyVersion` / `FileVersion`）と `.psd1`（`ModuleVersion`、`ReleaseNotes`）を 1.3.0 にする
- クラスライブラリの csproj は 1.2.0 のまま（CLAUDE.md は 3 か所を揃えるよう書いているが、1.3.0 は例外。CLAUDE.md の該当箇所にもその旨を追記する）
  （第四次で改めた: クラスライブラリも 1.3.0 に上げた。1 節の例外を参照）
- バージョンを上げるのは第三次の最後でよい

---

## 5. テスト要件

### 5.1 実行環境

- 両 TFM でテストを通すこと（`-f net48` と `-f net10.0`）
- **PSCompat（`tests/PSCompat/Invoke-ProbedCompatTests.ps1`）で 5.1 と 7.x の結果が一致すること。** 本モジュールの存在意義そのものである
- 実ホストでの確認（CLAUDE.md の「実 PowerShell ホストでの手動確認」）も行い、結果を報告する

### 5.2 必須のテスト

仕様書 23 章の表の全行。加えて次を守る。

- **期待値は実測報告の 7.6.7 の結果を使う。** 本モジュールの出力を期待値として固定しない（バグが自己整合する）
- テストデータはバイト列を明示して生成する（既存の書き込みコードで作らない）
- 実測報告の M1（15 ケース）・M4・M5 は、**5.1 でも 7.6.7 と同じ結果になること**をテストで固定する
- `Export-ProbedCsv` の出力が、両ホストでバイト単位で同じになることを PSCompat で確かめる（日付・数値を含む値はカルチャーに依存するので、PSCompat のシナリオではカルチャーを固定するか、カルチャーに依存しない値を使う）

### 5.3 既存のテスト

既存のテストと PSCompat のシナリオがすべて通ること。既存のテストの期待値を変える必要が生じたら、変える前に報告すること。

---

## 6. ドキュメント

| 文書 | 内容 | 段階 |
|---|---|---|
| MAML ヘルプ（5 言語 + 複製） | 仕様書 6.3、22 章 | 各段階 |
| `docs/EncodingProbe-1.3.0-実装記録.md`（新規） | 仕様書・依頼・当初の想定と異なる結果になった点と、仕様書に書かれていなかった判断。1.2.0 の実装記録と同じ形式 | 各段階で追記 |
| `CHANGELOG.md` | 1.3.0 の節（追加・変更・標準との差異の要点） | 第三次 |
| `README.md` | 1.3.0 の変更点の節（1.2.0 の節と同じ形式） | 第三次 |
| `CLAUDE.md` | 7 節の項目 | 第三次 |
| `docs/EncodingProbe-1.1.0-仕様書.md` | 1.3.0 で既存の記述と食い違う箇所があれば、注記を入れる（「判定はファイル全体を対象とする」など） | 第三次 |

---

## 7. CLAUDE.md の更新（第三次）

- 概要のコマンドの一覧に `Import-ProbedCsv` / `Export-ProbedCsv` を加える
- 「1.3.0 で追加したコマンド」の節を設け、本書 3〜4 節の要点（移植の位置、内部 API の置き換え、`-Culture` と `-UseCulture`、`Out-ProbedFile` の踏襲）を書く
- 「文字エンコーディングの判定は**ファイル全体**を対象とする」の記述に、`-DetectionLineCount` の例外（先頭 N 行で判定し、全体を検証する）を書き加える
- 「1.1.0 では次を変更していない（今後も維持すること）」の `Resolve-Encoding` の項を、「既存のパラメータと戻り値を変更しない（1.3.0 で `-DetectionLineCount` を追加した）」に改める
- 「ライセンス上の注意」に PowerShell 由来のコードを追記する（3.1）
- 「バージョン更新時に触る場所」に、モジュールだけをリリースする場合の扱いを追記する（4.9）
- 「1.3.0 の作業記録」の節を設ける（1.2.0 の節と同じ形式）

---

## 8. 未決事項

仕様書 24 章の U1〜U4 は、利用者の回答を依頼文に記載する（U4 は第一次、U3 は第二次、U1・U2 は第三次）。依頼文に回答が無い項目に到達したら、**独自に決めずに確認を求めること。**

---

## 9. 作業の進め方

- 作業ブランチは `feature/1.3.0-csv`（master から作る）
- 依頼文の段階ごとに作業する。段階の中でも、意味のある単位でコミットを分ける。コミットメッセージは日本語
- 各段階の完了時に、実装内容・テスト結果（両 TFM、PSCompat）・実装記録に書いたことを報告する
- 仕様書に判断材料が無い箇所に遭遇したら、**推測で実装せず質問すること**
- パフォーマンスの最適化は、正しさが確認できるまで行わないこと
- 完了した依頼文は、利用者の指示があってから削除する（1.2.0 と同じ運用。判断の根拠は実装記録・仕様書に移す）
