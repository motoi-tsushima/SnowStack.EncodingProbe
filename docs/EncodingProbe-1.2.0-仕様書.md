# SnowStack.EncodingProbe.PowerShell 1.2.0 新規追加機能仕様書

- 対象バージョン: 1.2.0（現行 1.1.0 からの機能追加）
- 位置づけ: 追加が中心。既存コマンドへの変更は 4 章に挙げるものだけとする
- 作成日: 2026-09-25
- 状態: **確定。第 1 部（`Out-ProbedFile`）・第 2 部（`Convert-ProbedContent`）とも利用者の承認済み**
- 前提文書:
  - `docs/EncodingProbe-1.1.0-仕様書.md`（統一語彙、原則 A / B、エラー方針など。本書はこれを土台とし、差分だけを書く）
  - `docs/EncodingProbe-1.2.0-調査-Out-File挙動の実測.md`（本書の「標準の挙動」はすべてこの実測に基づく。以下「実測報告」）

> **Claude Code へ:** 作業の進め方（段階の分け方、コミットの単位など）は別途の依頼文で指示する。本書は仕様だけを定める。

---

## 1. スコープ

### 1.1 1.2.0 で追加するコマンド

| コマンド | 役割 | 本書 |
|---|---|---|
| `Out-ProbedFile` | オブジェクトを整形し、統一語彙でエンコーディング・BOM・改行を指定してファイルに書き出す | 第 1 部 |
| `Convert-ProbedContent` | 既存のテキストファイルのエンコーディング・BOM・改行を変換して書き直す | 第 2 部 |

### 1.2 既存コマンドの変更

4 章に挙げる。いずれも破壊的変更ではなく、標準コマンドとの食い違いの修正と、未記載だった挙動の明文化である。

### 1.3 変更しないもの

- 統一語彙（1.1.0 仕様書 3 章）、解決順序、BOM 方針の決定規則
- `Resolve-Encoding` / `ConvertTo-DotNetEncoding` / `Get-EncodingProbePlatformInfo`
- `EncodingInformation` 型

---

# 第 1 部 `Out-ProbedFile`

## 2. 基本方針と挙動

### 2.1 標準の `Out-File` との関係

`Out-ProbedFile` は、標準の `Out-File` のパラメータをすべて持ち、`Set-ProbedContent` / `Add-ProbedContent` 由来のパラメータを加えたコマンドである。

- **符号化の層（エンコーディング・BOM・改行）は、PS 5.1 と 7.x で同一の結果を保証する。** これが本コマンドの存在理由である
- **整形の層（オブジェクトを文字列にする処理）は、実行中のホストの整形系に委ね、同一性を保証しない**（2.3）
- 標準と挙動を変える箇所は、本書で明示したものに限る（一覧は 9 章）

### 2.2 整形の実装

整形は `Microsoft.PowerShell.Utility\Out-String -Stream` をステッパブルパイプラインで呼び出して行う。自前の整形処理は持たない。

- `-Width` が指定されたときだけ `-Width` を渡す。省略時は `Out-String` の既定（ホスト依存）に任せる
- `Out-String -Stream` が返す各要素を **1 行** として扱い、各行の後ろに改行（2.6）を付けて書く
- この方法は、同じホストの `Out-File` と全入力・全幅で一致した（実測報告 4 章）。ステッパブルパイプライン経由でも一致した
- ANSI エスケープの扱い（PS 7.2 以降の `$PSStyle.OutputRendering`）も `Out-String` に任せる。本コマンドは除去も追加もしない

### 2.3 整形の層がホストによって異なる点（保証外）

実測で確認した差異は次のとおり。ヘルプの NOTES に記載する。

- 表の各行の末尾の空白（PS 5.1 は列幅まで埋める、PS 7.x は埋めない）
- 表・一覧の前後の空行の数
- 切り詰めの記号（PS 5.1 は `...`、PS 7.x は `…`）
- 全角文字の幅の数え方（PS 5.1 は文字数、PS 7.x は表示幅）
- `-Width` 省略時の幅（非対話ホストで PS 5.1 は 119、PS 7.x は 120）

文字列の入力は整形の影響を受けないため、両ホストで同じ結果になる。

### 2.4 統一語彙と書き込み系の共通規則

1.1.0 仕様書の書き込み系の規則をすべて適用する。

- 裸の `utf8` と `utf7` は、パラメータ束縛の段階で拒否する（1.1.0 仕様書 3.6 節）
- BOM 接尾辞を許さない語彙への接尾辞付き指定は拒否する
- BOM 出力の可否は語彙側で決め、`GetPreamble()` に依存しない（3.5 節の例外も同じ）
- 内部表現は `EncodingSpec` を共用し、`ArgumentTransformationAttribute` で解決する

### 2.5 `-Encoding` の決定

`-Encoding` の**省略**と**明示的な `Auto`** は意味が異なる。`Set-ProbedContent`（省略 = `Auto`）とは規則が違う点をヘルプに明記する。

| 指定 | `-Append` なし（上書き・新規作成） | `-Append` あり |
|---|---|---|
| 省略 | `utf8NoBOM`。既存ファイルの判定は行わない | 追記先から継承。追記先が無い、または 0 バイトなら `utf8NoBOM` |
| `Auto` | 出力先の既存ファイルから継承。無ければ**終了エラー** | 追記先から継承。無ければ**終了エラー** |
| 語彙名・WebName・数値 | その値 | その値（整合性検査あり。2.8） |
| `EncodingInformation` | その値（改行も継承） | 同左（整合性検査あり） |
| `System.Text.Encoding` | その値（`GetPreamble()` を尊重） | 同左（整合性検査あり） |

- 省略時に上書きで判定を行わないのは、捨てる内容のために判定を走らせ、判定失敗で止まるのを避けるためである
- 明示的な `Auto` で出力先が 0 バイトの場合は、既存の `Internal/EncodingInheritance` の規則（空ファイルからの継承）に従う。
  省略時の `-Append` だけは 0 バイトを「無い」と同じに扱い `utf8NoBOM` とする（`Encoding.Default` が PS 5.1 と 7.x で異なるため）
- `-Encoding` と `-EncodingFrom` の同時指定は終了エラー（1.1.0 と同じ）

### 2.6 改行の決定

`-LineBreak` は `Auto` / `CrLf` / `Lf` / `Cr`（1.1.0 と同じ値）。決定順序は次のとおり。

| 優先 | 条件 | 結果 |
|---|---|---|
| 1 | `-LineBreak` の明示（`Auto` 以外） | その値 |
| 2 | 参照情報がある（`-EncodingFrom`、明示的な `-Encoding Auto`、`-Encoding <EncodingInformation>`、`-Encoding` 省略の `-Append` で追記先から継承した場合） | 参照元の改行（混在改行は `Internal/LineBreakResolver` の規則） |
| 3 | それ以外 | `Environment.NewLine` |

**`-LineBreak` が決めるのは、各行の後ろに付ける改行だけである。** 入力の文字列の中に含まれる改行（`"a`r`nb"` など）は置き換えない。
`Out-String -Stream` は複数行の文字列を分割しないため、そのような文字列は 1 行として扱われ、中の改行はそのまま書かれる。
これは標準の `Out-File` / `Set-Content` と同じ挙動であり、`Set-ProbedContent` / `Add-ProbedContent` の現行の挙動とも一致する（4.2）。
ファイル内の改行を統一する用途は `Convert-ProbedContent` が担う。

### 2.7 `-NoNewline`

- 各行を区切りなしで連結して書く。最後の行の後ろにも改行を付けない（標準と同じ）
- 空行（空文字列の要素）は何も残さない（標準と同じ）
- `-LineBreak` と同時に指定されたら Warning（1.1.0 と同じ。Error にしない）

### 2.8 `-Append`

- 追記先の既存ファイル（1 バイト以上）には **BOM を書かない**（1.1.0 仕様書 6.3 と同じ）
- 追記先が無い、または 0 バイトなら、新規作成と同じ扱いとし、BOM は 2.11 の規則に従う
- **整合性検査**は `Add-ProbedContent` の規則（1.1.0 仕様書 6.1 節のバイト列比較）をそのまま適用する
  - `-Encoding` が明示された場合も、追記先（1 バイト以上）の判定は必ず行う
  - 検査は各行を書き込む前に行い、不一致を検出した時点で終了エラーとする。それまでに書いた行は比較が成立した行だけなので、ファイルは追記先のエンコーディングのまま一貫している
  - 追記先が 0 バイトまたは存在しない場合は検査しない
- `-AllowEncodingChange` で整合性検査を飛ばす。`-Append` なしで指定された場合は Warning を出し、無視する（スクリプトで `-Append` を動的に切り替える書き方を壊さないため）
- 改行の不一致は許可する（1.1.0 と同じ）

### 2.9 `-NoClobber` / `-Force` / 読み取り専用

| 組み合わせ | 挙動（標準と同じ） |
|---|---|
| `-NoClobber` で出力先が存在する | 終了エラー。ファイルに触れない |
| `-Append -NoClobber` | `-Append` を優先し、追記する（`-NoClobber` は効かない） |
| `-Force -NoClobber` で出力先が存在する | 終了エラー（`-Force` は `-NoClobber` を解除しない） |
| 読み取り専用で `-Force` なし | 終了エラー |
| 読み取り専用で `-Force` あり | 属性を外して書き込み、**書き込み後に読み取り専用属性を元に戻す** |

- 属性の復元は、書き込みが途中で失敗した場合も行う（`finally`）
- `-Force` はセキュリティ上の制限（権限不足）を越えない

### 2.10 ファイルを開くタイミング

同一パスの往復（`Get-ProbedContent a.txt | Out-ProbedFile a.txt`）を `Internal/ActiveReadRegistry` で検出するため、
**ファイルを開く（作成・切り詰める）のは最初の行を書く直前**とする。
標準の `Out-File` は `BeginProcessing` で切り詰めるため、上流が読む前に内容を消してしまう（実測報告 6 章）。

`BeginProcessing` では、ファイルを開かずに判定できる次の検査を行う。いずれも失敗は終了エラー。

1. パラメータの検証（`-Culture` / `-Strategy`、`-Encoding` と `-EncodingFrom` の競合）
2. パスの解決（2.12）
3. `ShouldProcess`（2.14）
4. `-NoClobber`（`-Append` なし）で出力先が存在するか
5. 読み取り専用で `-Force` が無いか
6. `-EncodingFrom` の参照先の判定

出力先の既存ファイルの判定（明示的な `Auto`、`-Append` での継承と整合性検査の準備）も、この時点で行ってよい。判定は読み取りだけで、ファイルを変更しないためである。

### 2.11 空の入力と BOM

- **BOM は、1 行でも書いたときにだけ書く。** これを唯一の規則とする
- 入力が 0 件で終わった場合（または `Out-String -Stream` が 1 要素も返さなかった場合）は、`EndProcessing` で次のとおりにする
  - `-Append` なし: 出力先を **0 バイト** で作成する。既存なら 0 バイトに切り詰める（標準と同じ）
  - `-Append` あり: 既存ファイルには触れない。無ければ 0 バイトで作成する（標準と同じ）
- この規則により、`$null` を 1 個渡した場合は 0 バイトになる。標準の `Out-File` は BOM だけを書く。**既知の差異**として 9 章とヘルプに記載する
- 空文字列を 1 個渡した場合は、BOM（語彙が BOM 有りなら）と改行 1 個を書く（標準と同じ）
- `-WhatIf` の場合、および `ShouldProcess` が拒否された場合は、`EndProcessing` でもファイルを作らない

### 2.12 パスの解決

- `-FilePath`（位置 0、別名 `-Path`）は単一の `string`。ワイルドカードを展開し、**ちょうど 1 件**に解決されることを求める
  - 解決先が既存ファイル 1 件 → そのファイル
  - ワイルドカード文字を含まず、ファイルが存在しない → そのパスに新規作成
  - ワイルドカード文字を含み、0 件 → 終了エラー（新規作成しない。標準と同じ。`c[1].txt` のような名前は `-LiteralPath` で指定する）
  - 2 件以上 → 終了エラー
- `-LiteralPath`（別名 `-PSPath` / `-LP`）はワイルドカードを解釈しない
- **別名 `-Path` / `-LP` は PS 5.1 でも提供する**（標準では PS 7 だけにある）
- **`-LiteralPath` はパイプラインから受け取らない。** 標準は `ValueFromPipelineByPropertyName` を持つが、`Get-ChildItem | Out-File` で `PSPath` が意図せず束縛される紛らわしさを避けるため。パイプラインから受け取るのは `-InputObject` だけ
- 相対パスは PowerShell の現在位置を基準とする（標準・既存の本モジュールと同じ）
- 親ディレクトリが無い、ディレクトリを指している場合は終了エラー。空文字列と `$null` はパラメータ束縛で拒否する

### 2.13 `-Culture` / `-Strategy`

1.1.0 仕様書 4.4 と同じ名前・同じ値。`Cmdlets/ResolveEncodingOptions` に集約した解決を使う。判定が走るのは次の経路で、どれにも効く。

| 経路 | 判定する対象 |
|---|---|
| `-EncodingFrom` | 参照ファイル |
| 明示的な `-Encoding Auto` | 出力先の既存ファイル |
| `-Append`（`-Encoding` 省略の継承、および整合性検査） | 追記先の既存ファイル |

### 2.14 `-WhatIf` / `-Confirm`

`SupportsShouldProcess` を有効にし、`BeginProcessing` で出力先 1 件につき 1 回だけ `ShouldProcess` を呼ぶ。
拒否された場合は、入力を受け取って捨て、ファイルには一切触れない。

---

## 3. パラメータ一覧

| パラメータ | 型 | 位置 | パイプライン | 別名 | 説明 |
|---|---|---|---|---|---|
| `-FilePath` | `string` | 0 | — | `Path` | 出力先。ワイルドカードは 1 件に解決されること。パラメータセット `ByPath`（既定、必須） |
| `-LiteralPath` | `string` | — | — | `PSPath`, `LP` | 出力先。ワイルドカード非対応。パラメータセット `ByLiteralPath`（必須） |
| `-InputObject` | `PSObject` | — | ByValue | — | 書き出すオブジェクト |
| `-Encoding` | 多形 | 1 | — | — | 統一語彙ほか。2.5 |
| `-EncodingFrom` | `string` | — | — | — | 参照ファイルから継承（1.1.0 と同じ） |
| `-LineBreak` | `Auto` / `CrLf` / `Lf` / `Cr` | — | — | — | 2.6 |
| `-Append` | switch | — | — | — | 2.8 |
| `-AllowEncodingChange` | switch | — | — | — | 2.8。`-Append` なしでは Warning |
| `-NoClobber` | switch | — | — | `NoOverwrite` | 2.9 |
| `-Force` | switch | — | — | — | 2.9 |
| `-Width` | `int` | — | — | — | `ValidateRange(2, 2147483647)`（標準と同じ）。省略時は `Out-String` の既定 |
| `-NoNewline` | switch | — | — | — | 2.7 |
| `-Culture` | `string` | — | — | — | 2.13 |
| `-Strategy` | `string` | — | — | — | 2.13 |
| `-WhatIf` / `-Confirm` | — | — | — | — | 2.14 |

`-Encoding` の位置 1 は標準に合わせた（`Out-ProbedFile out.txt utf8NoBOM` と書ける）。

---

## 4. 既存コマンドの変更

### 4.1 `-Force` 後の読み取り専用属性を元に戻す

対象: `Set-ProbedContent` / `Add-ProbedContent`

- 現状は `Internal/ProbedFileWriter.ClearReadOnly` で属性を外し、戻していない。標準の `Set-Content` / `Add-Content` / `Out-File` はいずれも戻す（実測報告 2 章）
- 書き込み後（失敗時も）に元の属性へ戻すよう修正する。`Out-ProbedFile` と同じ実装を共用する
- ヘルプ（MAML）の `-Force` の説明を「読み取り専用の属性を一時的に外して書き込み、書き込み後に元に戻します」の趣旨に改める（5 言語）
- テスト `Write_ReadOnlyFile_WithForce_Succeeds` / `Append_ReadOnlyFile_WithForce_Succeeds` に、書き込み後の属性の検査を加える
- `CHANGELOG.md` には「不具合の修正（標準コマンドとの食い違い）」として記載する
- エラーの分類（読み取り専用で `-Force` なしは非終了エラー）は変更しない

### 4.2 `-LineBreak` の範囲を明文化する

対象: `Set-ProbedContent` / `Add-ProbedContent`（挙動は変更しない）

- 現行の挙動「`-LineBreak` は要素の後ろに付ける改行だけを決め、文字列の中の改行は置き換えない」を正式な仕様とする
- ヘルプの `-LineBreak` の説明に明記する（5 言語）
- 1.1.0 仕様書 5.6 節に同じ内容を追記する
- 文字列の中に改行を含む値のテストを追加する。期待値は実測報告 5 章の 8-1〜8-6（`-LineBreak` 省略 / `CrLf` / `Lf` / `Cr`、`-NoNewline`、`Add-ProbedContent -LineBreak Lf`）

### 4.3 1.1.0 仕様書の記述の修正

1.1.0 仕様書 5.3 節の表で、`-Encoding Auto` で対象が存在しない場合が「Error 終了」となっているが、8 章の表と実装は「非終了エラー」である。5.3 節の表現を 8 章に合わせる。

---

## 5. エラーと警告

### 5.1 分類

出力先は常に 1 件であり、出力先に関する失敗は「1 ファイルも処理できない」（1.1.0 仕様書 8 章の終了エラーの基準）に当たる。
また非終了エラーにすると、上流からレコードが届くたびに同じエラーが繰り返される。
したがって **`Out-ProbedFile` の失敗はすべて終了エラー**とする。標準の `Out-File` も同じである（実測報告 2 章）。

| 事象 | 扱い | 起きる時点 |
|---|---|---|
| 裸の `utf8` / `utf7` / 接尾辞を許さない語彙への接尾辞 | 終了エラー | パラメータ束縛 |
| `-Width` が 2 未満 | 終了エラー | パラメータ束縛 |
| `-FilePath` / `-LiteralPath` が空文字列・`$null` | 終了エラー | パラメータ束縛 |
| `-Encoding` と `-EncodingFrom` の同時指定 | 終了エラー | `BeginProcessing` |
| 解釈できない `-Culture` / `-Strategy` | 終了エラー | `BeginProcessing` |
| パスの解決の失敗（ワイルドカード 0 件・2 件以上、親ディレクトリ無し、ディレクトリ） | 終了エラー | `BeginProcessing` |
| `-NoClobber` で出力先が存在する | 終了エラー | `BeginProcessing` |
| 読み取り専用で `-Force` なし | 終了エラー | `BeginProcessing` |
| `-EncodingFrom` の参照先が無い・判定失敗 | 終了エラー | `BeginProcessing` |
| 明示的な `-Encoding Auto` で出力先が無い | 終了エラー | `BeginProcessing` |
| 出力先・追記先の判定失敗（`EncodingDetectionFailed`、`CodePageNotAvailable` を含む） | 終了エラー | `BeginProcessing` |
| 同一パスの読み書き（`ActiveReadRegistry`） | 終了エラー | 最初の行を書く直前 |
| `-Append` でのバイト列不一致 | 終了エラー | 不一致の行を書く直前 |
| 書き込み権限なし（読み取り専用以外の理由） | 終了エラー | ファイルを開く時点 |
| `-LineBreak` と `-NoNewline` の同時指定 | Warning | `BeginProcessing` |
| `-Append` なしの `-AllowEncodingChange` | Warning | `BeginProcessing` |

- `FullyQualifiedErrorId` は既存の ID（`WriteAccessDenied`、`EncodingDetectionFailed`、`EncodingFromDetectionFailed`、`CodePageNotAvailable` など）を流用する。
  新設が必要なもの（`-NoClobber`、パスの解決失敗など）は既存の命名規則に合わせる。`-NoClobber` は標準と同じ `NoClobber` とする
- 同じ事象でも `Set-ProbedContent` では非終了エラーのもの（読み取り専用など）がある。基準は同じで、出力先が 1 件か複数かの違いによる。ヘルプに記載する
- メッセージは `MessageCatalog` に追加する（6 章）

### 5.2 テストでの注意

終了エラーのとき、`-ErrorVariable` には `ErrorRecord` ではなく例外オブジェクトが入る（実測報告 8.1 の 10）。
テストで終了エラーを検査する場合は、この点を前提にすること。

---

## 6. ローカライズ

- `MessageCatalog` に `Out-ProbedFile` 用のメッセージと Warning を追加する（英語・日本語・韓国語・繁体字中国語・簡体字中国語）
- 香港・マカオは台湾版と同じ文面（B 案）。同一性テストが通ること
- MAML ヘルプに `Out-ProbedFile` を追加し、4.1・4.2 の修正を反映する。配布物のコピー 14 か所すべてに反映する
- `Out-ProbedFile` のヘルプの NOTES に次を記載する
  - 整形の層はホストによって異なる（2.3）
  - `-Encoding` の省略と `Auto` の違い、`Set-ProbedContent` との規則の違い（2.5）
  - `-LineBreak` は文字列の中の改行を置き換えない（2.6）
  - 標準の `Out-File` との差異（9 章）

---

## 7. テスト

PowerShell 層のテストとして追加し、PSCompat で PS 5.1 と 7.x の両方で結果が一致することを確認する。
期待値は実測報告の値を使う。

| 区分 | 内容 |
|---|---|
| 語彙 | 裸の `utf8` / `utf7` の拒否、`*BOM` / `*NoBOM` の全系統、WebName、数値、`EncodingInformation`、`Encoding` インスタンス |
| `-Encoding` の決定 | 2.5 の表の全セル（省略 / `Auto` × `-Append` の有無 × 出力先の有無・0 バイト） |
| 改行 | 2.6 の優先順位、参照情報からの継承、文字列の中の改行が置き換わらないこと |
| `-Append` | 既存ファイルに BOM を書かない、整合性検査の成立・不成立、不一致の手前までの行が残ること、`-AllowEncodingChange`、`-Append` なしの Warning |
| 組み合わせ | 2.9 の表の全行（実測報告の 3-1〜3-10 と同じ組み合わせ）、書き込み後の読み取り専用属性の復元（失敗時を含む） |
| パス | 2.12 の各場合（実測報告の 4-1〜4-9 と同じ）、別名 `-Path` / `-LP` が PS 5.1 でも使えること、`-LiteralPath` がパイプラインから束縛されないこと |
| 整形 | 文字列の入力が両ホストで同一のバイト列になること。表形式などは「同じホストの `Out-String -Stream` の各要素 + 改行」と一致すること（両ホストの結果同士は比較しない） |
| 空の入力 | 0 件（上書き・`-Append`、出力先の有無）、`$null` 1 個で 0 バイト、空文字列 1 個 |
| タイミング | 同一パスの往復がエラーになり元のファイルが壊れないこと、`-WhatIf` でファイルが作られないこと |
| エラー | 5.1 の表の全行が終了エラー（または Warning）であること |
| 既存コマンド | 4.1・4.2 のテスト |

---

## 8. 実装上の留意点

- **ステッパブルパイプライン**: `ScriptBlock.Create("Microsoft.PowerShell.Utility\\Out-String -Stream")` に、指定時だけ ` -Width N` を付けて作る。
  `BeginProcessing` で `Begin(this)`、`ProcessRecord` で `Process(InputObject)`、`EndProcessing` で `End()`、最後に `Dispose()`。
  `Process` / `End` が返す要素をその場で行として書く。モジュール修飾名で呼び、利用者が同名の関数を定義していても影響を受けないようにする
- **書き込み部品の共用**: `Internal/ProbedFileWriter`、`LineBreakResolver`、`EncodingInheritance`、`ActiveReadRegistry` を共用する。
  読み取り専用属性の復元（4.1）は共用部品に入れ、3 コマンドで同じ実装を使う
- **ファイルを開く時点**: 最初の行を書く直前。入力 0 件なら `EndProcessing`（2.10、2.11）
- **BOM**: 最初の行を書くときに、語彙の `EmitBom` に従って書く。`-Append` で追記先が 1 バイト以上なら書かない
- **パイプライン停止時**: 下流の `Select-Object -First` などでパイプラインが止まった場合（`StopProcessing`）や例外時も、ファイルを閉じ、読み取り専用属性を戻すこと

---

## 9. 標準の `Out-File` との差異（一覧）

| 項目 | 標準の `Out-File` | `Out-ProbedFile` | 理由 |
|---|---|---|---|
| `-Encoding` の既定 | PS 5.1 は BOM 付き UTF-16LE、PS 7.x は BOM 無し UTF-8 | 上書き・新規作成は `utf8NoBOM`、`-Append` は追記先から継承 | PS バージョンによらず同一の結果にするため。追記でエンコーディングが混ざる事故を防ぐため |
| `-Append` の整合性 | 追記先を見ない（PS 5.1 で UTF-8 に UTF-16LE が混ざる） | バイト列比較で検査 | 同上 |
| `-LineBreak` / `-EncodingFrom` / `-Culture` / `-Strategy` / `-AllowEncodingChange` | 無い | ある | 本モジュールの追加機能 |
| ファイルを切り詰める時点 | `BeginProcessing` | 最初の行を書く直前（入力 0 件なら `EndProcessing`） | 同一パスの往復でファイルを壊さないため |
| `$null` を 1 個渡した場合 | BOM だけのファイル | 0 バイトのファイル | BOM は 1 行でも書いたときだけ書く、という規則に統一するため |
| 別名 `-Path` / `-LP` | PS 7 だけ | PS 5.1 でも使える | PS 5.1 と 7.x で同じ語彙にするため |
| `-LiteralPath` のパイプライン入力 | プロパティ名で受け取る | 受け取らない | `PSPath` が意図せず束縛されるのを避けるため |

整形の層の差異（2.3）は、標準の `Out-File` 自体がホストによって異なるものであり、本コマンドはそれをそのまま引き継ぐ。

---

# 第 2 部 `Convert-ProbedContent`

## 10. 基本方針

### 10.1 役割

既存のテキストファイルの**エンコーディング・BOM・改行を変換して書き直す**。文字列の置換など、内容の加工は行わない。

```powershell
# その場で変換（上書き）
Convert-ProbedContent *.txt -Encoding utf8NoBOM -LineBreak Lf

# エンコーディングは知らなくてよい。BOM だけ外す
Convert-ProbedContent *.txt -Bom Remove

# Get-ChildItem から受ける
Get-ChildItem -Recurse -Filter *.cs | Convert-ProbedContent -Encoding utf8BOM

# 別のフォルダーへ出力
Convert-ProbedContent *.txt -Encoding utf8NoBOM -Destination .\out
```

- 対象の探索（再帰、絞り込み）は持たない。`Get-ChildItem` に任せる（`-Recurse` / `-Include` / `-Exclude` は設けない）
- **変換で文字を失わないことを保証する。** 表現できない文字があるファイルは変換しない（13 章）
- mfsr / mfprobe とは独立したコマンドであり、機能や仕様の一致は考慮しない

### 10.2 統一語彙と共通規則

1.1.0 仕様書の統一語彙と、書き込み系の共通規則（2.4 節）を適用する。ただし `-Bom` を併用した場合の裸名・WebName の扱いは 12 章で拡張する。

---

## 11. パラメータ一覧

| パラメータ | 型 | 位置 | パイプライン | 別名 | 説明 |
|---|---|---|---|---|---|
| `-Path` | `string[]` | 0 | — | — | 変換元。ワイルドカード対応。パラメータセット `Path`（既定、必須） |
| `-LiteralPath` | `string[]` | — | ByPropertyName | `PSPath`, `LP` | 変換元。ワイルドカード非対応。パラメータセット `LiteralPath`（必須）。`Get-ChildItem` の出力は `PSPath` でここに束縛される |
| `-Encoding` | 多形 | 1 | — | — | 変換**先**のエンコーディング（と BOM）。12 章 |
| `-EncodingFrom` | `string` | — | — | — | 変換先を参照ファイルから継承。12 章 |
| `-Bom` | `Add` / `Remove` | — | — | — | 変換先の BOM。12 章 |
| `-LineBreak` | `Auto` / `CrLf` / `Lf` / `Cr` | — | — | — | 変換先の改行。14 章 |
| `-SourceEncoding` | 多形 | — | — | — | 変換**元**のエンコーディングの明示。省略時は検出。13.1 |
| `-Culture` | `string` | — | — | — | 変換元の検出に使う（1.1.0 仕様書 4.4 と同じ） |
| `-Strategy` | `string` | — | — | — | 同上 |
| `-Destination` | `string` | — | — | — | 出力先フォルダー。省略時はその場で上書き。16 章 |
| `-Force` | switch | — | — | — | 読み取り専用も変換する。`-Destination` では同名ファイルを上書きする |
| `-PassThru` | switch | — | — | — | 結果を出力する。17 章 |
| `-WhatIf` / `-Confirm` | — | — | — | — | `ConfirmImpact = Medium`。ファイルごとに効く |

- `-Culture` / `-Strategy` は検出を行うときだけ使う。`-SourceEncoding` を明示した場合は使われない（エラーにはしない。`Get-ProbedContent` と同じ）
- `-LineBreak Auto` は省略と同じ意味（改行を保つ）とする
- 変換元にディレクトリが渡された場合（`Get-ChildItem -Recurse` の出力など）は、エラーにせず飛ばし、Verbose に記録する

---

## 12. 変換先のエンコーディングと BOM

### 12.1 省略時の規則

| パラメータ | 省略時 |
|---|---|
| `-Encoding` / `-EncodingFrom` | 変換元のエンコーディングを保つ |
| `-Bom` | BOM を変えない（`-Encoding` の語彙が BOM を決めていればそれに従い、決めていなければ変換元のまま） |
| `-LineBreak` | 改行を保つ |

- `-Encoding`・`-EncodingFrom`・`-Bom`・`-LineBreak`（`Auto` を除く）の**どれも指定しない場合は終了エラー**（何も変換しないため）
- `-Encoding Auto` は受け付けない（終了エラー）。「変換元を保つ」は省略で表す

### 12.2 `-Bom`

| 値 | 意味 |
|---|---|
| `Add` | BOM を付ける |
| `Remove` | BOM を外す |

BOM を持てるのは Unicode 系 5 系統だけである。変換先が Unicode 系以外の場合は次のとおりとする。

| 指定 | 変換先が Unicode 系 | 変換先が Unicode 系以外 |
|---|---|---|
| `-Bom Add` | BOM を付ける | エラー（そのファイルは変換しない。12.3・18 章） |
| `-Bom Remove` | BOM を外す | 何もしない（外す BOM が無く、要求は満たされている） |

- `Remove` を「何もしない」にするのは、種類の混ざったファイル群に `Get-ChildItem -Recurse | Convert-ProbedContent -Bom Remove` と掛けたとき、エラーが大量に出ないようにするためである
- UTF-16 / UTF-32 から `Remove` すると BOM 無しの UTF-16 / UTF-32 になる。原則 B により許可し、警告は出さない（1.1.0 仕様書 3.7 と同じ）

### 12.3 `-Encoding` と `-Bom` の関係

- `-Bom` は BOM だけを決め、`-Encoding` はエンコーディングを決める
- BOM を明示する指定どうしが食い違ったら終了エラーとする
- 参照情報から継承した BOM は `-Bom` で上書きできる（1.1.0 仕様書 5.2「個別の明示指定は継承より優先」と同じ）

| `-Encoding` の指定 | `-Bom` なし | `-Bom` あり |
|---|---|---|
| 省略 | 変換元のエンコーディングと BOM を保つ | 変換元のエンコーディングを保ち、BOM だけ変える（変換元が Unicode 系以外で `Add` なら非終了エラー N5） |
| 接尾辞付き（`utf8BOM`、`unicodeNoBOM` など） | その語彙のとおり | 語彙の BOM と一致すれば許可、食い違えば終了エラー（E6） |
| Unicode 系の裸名（`utf8`、`unicode` など）・Unicode 系の WebName（`utf-8` など） | 1.1.0 の規則どおり（`utf8` と WebName は拒否、それ以外の裸名は BOM 有） | **系統名として扱い、BOM は `-Bom` で決める** |
| Unicode 系以外（`shift_jis`、`932` など） | そのエンコーディング（BOM 無し） | `Add` は終了エラー（E7）、`Remove` は許可 |
| `EncodingInformation` | 参照元の BOM を継承 | `-Bom` で上書き |
| `System.Text.Encoding` インスタンス | `GetPreamble()` に従う | `GetPreamble()` と食い違えば終了エラー（E8） |
| `-EncodingFrom`（`-Encoding` の代わり） | 参照ファイルの BOM を継承 | `-Bom` で上書き |

裸の `utf8` を書き込みで拒否する規則（1.1.0 仕様書 3.6）は、BOM が曖昧になるのを防ぐためのものである。`-Bom` があれば BOM は曖昧でないため、裸名を受け付ける。

### 12.4 参照情報からの改行の継承

`-EncodingFrom` と `-Encoding <EncodingInformation>` は、1.1.0 と同じく改行も継承する（混在改行は `Internal/LineBreakResolver` の規則）。
`-LineBreak` を明示した場合はそちらを優先する。

---

## 13. 変換元の読み取りと、文字を失わない保証

### 13.1 変換元のエンコーディング

- `-SourceEncoding` を省略した場合（`Auto` も同じ）は、変換元を検出する。`-Culture` / `-Strategy` はここで使う
- `-SourceEncoding` を明示した場合は検出しない。検出の誤判定を回避する手段である。受け付ける値は `Get-ProbedContent -Encoding` と同じ（裸の `utf8` も可）
- 変換元の BOM の有無は、ファイル先頭のバイト列が変換元のエンコーディングの BOM と一致するかで決める。一致すれば読み飛ばす（原則 A）
- 変換元が 0 バイトの場合は、変換結果も 0 バイトとし、書き直さない（`Changed = false`）。BOM は 1 文字でも書くときにだけ書く（`Out-ProbedFile` 2.11 と同じ規則）

### 13.2 変換元の不正なバイト列

変換元は、ファイル全体をメモリに読み、**例外フォールバック**（`DecoderFallback.ExceptionFallback`）で復号する。
不正なバイト列があれば、黙って U+FFFD に置き換えず、そのファイルは変換しない（非終了エラー N3。元のファイルは無傷）。
エラーメッセージには、最初の不正なバイト列の位置（バイトオフセット）を示す。

### 13.3 変換先で表現できない文字

変換結果は、書き込む前に全体を**例外フォールバック**（`EncoderFallback.ExceptionFallback`）で符号化する。
1 文字でも表現できなければ、そのファイルは変換しない（非終了エラー N4。元のファイルは無傷）。
エラーメッセージには、最初に表現できなかった文字（U+XXXX と文字そのもの）と、その位置（行・桁）を示す。

**表現できない文字を置き換えて変換するスイッチは設けない。** 置き換えてよい場合は、利用者が事前に文字列を加工すればよい。
「変換で文字が失われない」ことを本コマンドの保証とする。

### 13.4 私用領域

私用領域の符号位置（HKSCS 固有字など）は、既存の方針どおり符号位置のまま扱い、警告は出さない（`docs/私用領域の扱い_方針草案.md`）。
例えば Big5 から UTF-8 に変換すると私用領域の符号位置がそのまま UTF-8 で書かれ、Big5 に戻せば元のバイト列に戻る。

---

## 14. 改行の変換

- `-LineBreak`（`CrLf` / `Lf` / `Cr`）を指定した場合、ファイル内の**すべての改行**（CRLF / LF / CR）を指定の改行に揃える
  - 書き込み系の `-LineBreak`（要素の後ろに付ける改行だけを決める。4.2）とは意味が違う。ファイル内の改行を統一するのが本コマンドの役割であり、ヘルプに違いを明記する
- 最後の行の末尾に改行があるかないかは保つ（改行を足したり削ったりしない）
- `U+2028`（LINE SEPARATOR）、`U+2029`（PARAGRAPH SEPARATOR）、`U+0085`（NEL）などは対象外とする
- `-LineBreak` を省略した場合（参照情報からの継承も無い場合）は、混在改行も含めてそのまま保つ

---

## 15. 書き込みと上書きの安全性

### 15.1 その場での変換

- **同じフォルダーに一時ファイルを書いてから置き換える。** 途中で失敗しても元のファイルは壊れない
  - 一時ファイルの名前は変換元と衝突しないもの（例: `.<元の名前>.<GUID>.tmp`）とし、失敗時は削除する
  - 置き換えは `File.Replace`（変換元が存在するため）で行う
- 変換結果が**元のバイト列と完全に同じなら書き直さない**（更新日時を変えない。`Changed = false`）
- 更新日時は保たない（内容の変更であるため新しい日時になる）。作成日時・属性・ACL は `File.Replace` の挙動に従う
- 読み取り専用で `-Force` なしは非終了エラー。`-Force` ありは属性を外して書き込み、書き込み後に元に戻す（4.1 と同じ共用部品）
- `-WhatIf` / `-Confirm` はファイルごとに効く。`ConfirmImpact` は `Medium`（既定では確認を出さない）

### 15.2 同じファイルの読み書き

本コマンドは変換元を全体読み込んでから閉じ、その後で書き込む。同一ファイルの読み書きによる破損は起きない。
ただし、他のコマンド（`Get-ProbedContent` の遅延読み込みなど）が同じファイルを読んでいる最中の場合は、`Internal/ActiveReadRegistry` で検出して非終了エラーとする（1.1.0 と同じ）。

---

## 16. `-Destination`

- 出力先はフォルダーとし、ファイル名は変換元のものを保つ
- 出力先のフォルダーが存在しない、またはフォルダーでない場合は終了エラー（E11）。フォルダーは作成しない
- `Get-ChildItem -Recurse` から受け取った場合も、**フォルダー構造は保たない**。すべて `-Destination` の直下に置く
- 出力先に同名のファイルが既にある場合は非終了エラー（N7）。`-Force` を付ければ上書きする（読み取り専用なら外して書き、元に戻す）
- 同じ実行の中でファイル名が重なった場合、2 件目以降は非終了エラー（N8）。`-Force` があっても上書きしない（同じ実行で書いたファイルを消さないため）
- 変換元と出力先が同じファイルになった場合は非終了エラー（N9）。その場で変換したい場合は `-Destination` を付けない
- `-Destination` の場合は、変換結果が変換元と同一でも**出力先には書く**（`Changed` は「変換結果が変換元と異なるか」を表す）
- 書き込みは出力先のフォルダーに一時ファイルを作ってから移す（上書き時は `File.Replace`）

---

## 17. `-PassThru`

既定では何も出力しない。`-PassThru` を指定したときは、変換したファイル（同一で書き直さなかったものを含む）1 件につき 1 個の `PSCustomObject` を出力する。
`-WhatIf` のとき、およびエラーになったファイルは出力しない。

| プロパティ | 型 | 内容 |
|---|---|---|
| `Path` | `string` | 変換元の絶対パス |
| `Destination` | `string` | 書き込み先の絶対パス（その場での変換は `Path` と同じ） |
| `SourceEncoding` | `string` | 変換元の統一語彙名 |
| `Encoding` | `string` | 変換先の統一語彙名 |
| `SourceLineBreak` | `string` | 変換元の改行（`Resolve-Encoding` の `LineBreak` と同じ値。混在なら `LfAndCrLf` など） |
| `LineBreak` | `string` | 変換先の改行（改行を保った場合は `SourceLineBreak` と同じ） |
| `Changed` | `bool` | 変換結果が変換元と異なるか |

- 統一語彙名は、Unicode 系なら明示形（`utf8BOM`、`unicodeNoBOM` など）、それ以外は WebName（`shift_jis`、`euc-jp` など）とする。出力された名前をそのまま `-Encoding` に渡せば元に戻せる（原則 B）
- 公開型にはしない。`PSTypeNames` の先頭に `SnowStack.EncodingProbe.ConvertResult` を入れる（将来のフォーマットファイルや公開型への昇格に備える）

---

## 18. エラー

1.1.0 仕様書 8 章の基準どおり、パラメータの誤りは終了エラー、ファイルごとの失敗は非終了エラーとする。
非終了エラーになったファイルは変換せず、元のまま残す。他のファイルの処理は続ける。

### 18.1 終了エラー（1 ファイルも処理しない）

| # | 事象 |
|---|---|
| E1 | `-Encoding`・`-EncodingFrom`・`-Bom`・`-LineBreak`（`Auto` を除く）のどれも指定していない |
| E2 | `-Encoding Auto` |
| E3 | `-Encoding` と `-EncodingFrom` の同時指定 |
| E4 | `-Bom` なしで裸の `utf8`・Unicode 系の WebName（1.1.0 と同じ） |
| E5 | `utf7` の指定、BOM 接尾辞を許さない語彙への接尾辞（1.1.0 と同じ） |
| E6 | 接尾辞付きの語彙と `-Bom` の食い違い（`utf8NoBOM` と `-Bom Add` など） |
| E7 | Unicode 系以外の `-Encoding` と `-Bom Add` |
| E8 | `Encoding` インスタンスの `GetPreamble()` と `-Bom` の食い違い |
| E9 | `-EncodingFrom` の参照先が無い・判定失敗 |
| E10 | 解釈できない `-Culture` / `-Strategy` / `-SourceEncoding` |
| E11 | `-Destination` がフォルダーとして存在しない |

### 18.2 非終了エラー（そのファイルだけ変換しない）

| # | 事象 |
|---|---|
| N1 | 変換元が存在しない（ワイルドカードが 0 件に解決された場合を含む） |
| N2 | 変換元の検出に失敗した（`CodePageNotAvailable` を含む） |
| N3 | 変換元に、検出（または `-SourceEncoding`）したエンコーディングとして不正なバイト列がある |
| N4 | 変換先で表現できない文字がある（最初の文字と行・桁を示す） |
| N5 | `-Encoding` を省略し、変換元が Unicode 系以外なのに `-Bom Add` を指定した |
| N6 | 読み取り専用で `-Force` なし、または書き込み権限がない |
| N7 | `-Destination` に同名のファイルがあり、`-Force` なし |
| N8 | `-Destination` への出力で、同じ実行の中でファイル名が重なった（2 件目以降） |
| N9 | 変換元と `-Destination` の出力先が同じファイルになった |
| N10 | 他のコマンドが同じファイルを読んでいる最中（`ActiveReadRegistry`） |

- N5 は E7 と同じ食い違いだが、変換元を読むまで分からないため非終了エラーになる
- `FullyQualifiedErrorId` は既存の ID を流用し、新設するものは既存の命名規則に合わせる
- メッセージは `MessageCatalog` に 5 言語で追加する（香港・マカオは台湾と同じ文面）

---

## 19. ヘルプ・テスト・実装上の留意点

### 19.1 ヘルプ

MAML に `Convert-ProbedContent` を追加し、配布物のコピー 14 か所すべてに反映する。NOTES に次を記載する。

- 表現できない文字・不正なバイト列があるファイルは変換しない（置き換えて変換する手段は無い）
- `-LineBreak` は、書き込み系と違いファイル内のすべての改行を揃える
- `-Bom` と `-Encoding` の関係（12.3 の表）
- 変換元はファイル全体をメモリに読む（大きなファイルの制限）
- `-Destination` はフォルダー構造を保たない

### 19.2 テスト

PSCompat で PS 5.1 と 7.x の結果が一致することを確認する。

| 区分 | 内容 |
|---|---|
| 省略時の規則 | 12.1 の各場合、E1 |
| `-Bom` | 12.2・12.3 の表の全セル（Unicode 系 5 系統 × Add / Remove、Unicode 系以外、裸名・WebName との組み合わせ） |
| 文字を失わない保証 | 不正なバイト列（N3）、表現できない文字（N4）でファイルが無傷であること、メッセージに位置が含まれること |
| 私用領域 | `Chinese_HongKong/sample_big5hkscs.txt` を UTF-8 に変換し、Big5 に戻して元のバイト列に一致すること |
| 改行 | 混在改行の統一、末尾の改行の有無が保たれること、`U+2028` が対象外であること |
| 上書きの安全性 | 同一なら書き直さない（更新日時が変わらない）、失敗時に一時ファイルが残らない、読み取り専用の復元 |
| `-Destination` | 16 章の各場合（N7〜N9、E11、同一でも書くこと） |
| パイプライン | `Get-ChildItem` からの束縛、ディレクトリを飛ばすこと |
| `-PassThru` | 各プロパティ、語彙名で元に戻せること、`-WhatIf` で出力しないこと |
| エラー | 18 章の全行 |

テストデータは既存の `tests/EncodingProbe.Tests/TestData` を使う。

### 19.3 実装上の留意点

- 変換元はファイル全体を読み、例外フォールバック付きの `Encoding` で復号・符号化する（`Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)`。Unicode 系はコンストラクタの `throwOnInvalidBytes` を使う）
- 例外から位置を得るには `DecoderFallbackException.Index` / `EncoderFallbackException.Index` を使い、行・桁に換算する
- 書き込み部品（読み取り専用の復元、BOM の出力）は `Out-ProbedFile` と共用する
- 変換の順序は「復号 → 改行の変換 → 符号化（BOM 付加）→ 元のバイト列との比較 → 一時ファイルに書き込み → 置き換え」とする
