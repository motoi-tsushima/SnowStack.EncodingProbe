# Claude Code 実装指示書 — SnowStack.EncodingProbe.PowerShell 1.1.0

本書は実装作業の指示書である。機能仕様は別紙「SnowStack.EncodingProbe.PowerShell 1.1.0 新規追加機能仕様書」を参照すること。**仕様書に書かれていない挙動を推測で実装しないこと。** 判断が必要な箇所に遭遇したら、実装を進める前に確認を求めること。

---

## 0. 最初に行うこと

1. 仕様書を通読する
2. 既存リポジトリの構成を確認する（プロジェクト構成、ターゲットフレームワーク、既存コマンドレットの実装パターン、テストの配置、CI の構成）
3. **既存の実装スタイルに合わせる**。本指示書のコード例は説明のためのものであり、命名規則やファイル配置は既存コードに従うこと
4. 着手前に、実装計画（作成・変更するファイルの一覧と作業順序）を提示すること

---

## 1. 絶対に変更してはならないもの

以下は 1.1.0 で意図的に「変更しない」と決定されている。リファクタリングや改善の提案も不要である。

- `Resolve-Encoding` — パラメータ、戻り値、挙動のいずれも変更しない
- `Get-EncodingProbePlatformInfo` — 変更しない
- `EncodingInformation` 型 — **プロパティを追加しない**。特に `DotNetEncoding` プロパティは、検討の上で「追加しない」と決定されている
- コア側の NuGet パッケージ（SnowStack.EncodingProbe）の公開 API

既存の公開 API に手を入れる必要が生じた場合は、実装を止めて報告すること。

---

## 2. 追加するもの

| 種別 | 名称 | 備考 |
|---|---|---|
| コマンドレット | `Get-ProbedContent` | |
| コマンドレット | `Set-ProbedContent` | `SupportsShouldProcess` |
| コマンドレット | `Add-ProbedContent` | `SupportsShouldProcess` |
| コマンドレット | `ConvertTo-DotNetEncoding` | |
| 内部型 | `EncodingSpec` | **公開しない**（`internal`） |
| 内部属性 | `ArgumentTransformationAttribute` 派生 | 語彙解決を全コマンドで共有 |

---

## 3. 推奨する実装順序

依存関係の順に進めること。各段階でテストを通してから次に進む。

### 第 1 段階 — 語彙解決の基盤

1. `EncodingSpec`（internal）の定義: `{ Encoding, EmitBom, LineBreak }`
2. 語彙解決ロジック（仕様書 3.3 / 3.4 / 3.5）
3. `ArgumentTransformationAttribute` 派生クラス。読み取り用と書き込み用の挙動差（裸 `utf8` の可否、`utf7` の可否）はフラグで切り替える
4. この段階のユニットテストを厚く書く。**ここが全コマンドの土台であり、バグが最も高くつく**

### 第 2 段階 — `ConvertTo-DotNetEncoding`

第 1 段階の成果物をほぼそのまま公開するだけなので、基盤の検証を兼ねられる。

### 第 3 段階 — `Get-ProbedContent`

検出とストリーミング読み込み。

### 第 4 段階 — `Set-ProbedContent`

書き込み、`-EncodingFrom`、`-LineBreak`、`ShouldProcess`。

### 第 5 段階 — `Add-ProbedContent`

第 4 段階の派生。加えて整合性検査（仕様書 6 節）。

---

## 4. 実装時に必ず守る技術的注意点

以下はいずれも、過去に踏み抜くことが分かっている落とし穴である。

### 4.1 BOM 付きインスタンスの罠

```csharp
// NG — BOM 付きインスタンスが返る
Encoding.GetEncoding("utf-8")
Encoding.UTF8

// OK — BOM 方針を明示して構築する
new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
new UTF32Encoding(bigEndian: false, byteOrderMark: false)
```

Unicode 系は**必ずコンストラクタで組み立てる**こと。

### 4.2 読み取り時の BOM 混入

`StreamReader` に `detectEncodingFromByteOrderMarks: false` を渡すと、BOM が U+FEFF として 1 行目の先頭に混入する。検出結果のエンコーディングを強制しつつ、BOM を確実に読み飛ばす実装にすること。この挙動は必ずテストで検証する。

### 4.3 ファイルの二重読み込みを避ける

1 本の `FileStream` で先頭バッファを検出に使い、`Seek(0)` してから復号すること。検出用と読み込み用で 2 回開かない。

### 4.4 CodePagesEncodingProvider

.NET Core / .NET 5+ 上で CP932 等を扱うには `CodePagesEncodingProvider` の登録が必要である。ホスト側の登録に依存せず、**モジュール初期化時に登録**すること。`ConvertTo-DotNetEncoding` が返したインスタンスを利用者が後で使う場合にも必要になる。

### 4.5 ストリーミング

`Get-ProbedContent` は、標準の `Get-Content` と同様のメモリ挙動を保つため、行単位で `WriteObject` すること（`-Raw` を除く）。`ReadAllLines` で全読みしてから返す実装にしないこと。

### 4.6 パラメータ束縛段階でのエラー

裸の `utf8` の拒否など、仕様書 8 節で「パラメータ束縛段階」と指定されているものは、`ArgumentTransformationMetadataException` を用いてファイルを開く前に失敗させること。処理途中で失敗すると、書きかけの破損ファイルが残る。

---

## 5. テスト要件

### 5.1 実行環境

**PowerShell 5.1 と PowerShell 7.x の両方でテストを実行すること。** 本モジュールの存在意義は「バージョンによらず同一の結果を返すこと」にあるため、片方だけの検証では意味がない。可能なら CI にも両方を組み込む。

### 5.2 必須のテストケース

**語彙解決**

- 統一語彙の全エントリが正しいエンコーディングと BOM 方針に解決されること
- 大文字小文字を区別しないこと
- 別名（`shift-jis` / `sjis` / `ms_kanji` 等）が .NET 経由で解決されること
- 解決順序が仕様どおりであること（特に数値の解釈）
- BOM 接尾辞を許さない語彙への接尾辞付き指定がエラーになること

**バージョン間の一致（最重要）**

- 同一の入力・同一の語彙指定に対し、PS 5.1 と PS 7.x が**バイト単位で同一のファイル**を出力すること
- 対象は少なくとも: `utf8NoBOM` / `utf8BOM` / `shift_jis` / `euc-jp` / `unicode` / `unicodeNoBOM`

**ラウンドトリップ**

- 各エンコーディング・各 BOM 有無・各改行コードの組み合わせについて、`Get-ProbedContent -Raw` → `Set-ProbedContent -NoNewline -EncodingFrom <元>` で**元ファイルとバイト単位で一致**すること
- BOM 無し UTF-16LE / UTF-32 も対象に含めること

**エラー系**

- 仕様書 8 節の表にある全事象について、期待どおり Error / Warning になること
- エラーメッセージに代替候補や誘導が含まれること（裸 `utf8`、`ConvertTo-DotNetEncoding` の `Auto`）

**`Add-ProbedContent` の整合性検査**

- 仕様書 6.1 の判定表の全行
- `-AllowEncodingChange` で回避できること
- `-Force` では回避**できない**こと
- ISO-2022-JP への追記でエスケープシーケンスが正しく出力されること

**その他**

- `Get-ChildItem | Get-ProbedContent` が動作すること
- ワイルドカード指定時に内容が連結されること
- `-TotalCount` が指定行数で打ち切ること
- 空ファイル、1 バイトファイル、BOM のみのファイルなどの境界条件

### 5.3 テストデータ

各エンコーディング × BOM 有無 × 改行コードの組み合わせで、**バイト列を明示的に指定して**テストファイルを生成すること。既存の書き込みコードでテストファイルを作ると、バグが自己整合してしまい検出できない。

---

## 6. ドキュメント

### 6.1 コメントベースヘルプ / MAML

各コマンドに用意すること。次の点は**ヘルプに明記が必要**と仕様で定められている。

- PS 5.1 上では、本モジュールの統一語彙と PS 5.1 標準の語彙が異なること。また `Resolve-Encoding` が返す `PSEncodingName` とも一致しない場合があること
- 書き込みでは裸の `utf8` が使えないこと（`utf8NoBOM` / `utf8BOM` を使う）
- `Add-ProbedContent` では BOM 指定が無視されること
- `-Encoding` の入力形式によって改行の決まり方が変わること（`EncodingInformation` を渡した場合のみ改行も継承される）
- BOM 無し UTF-16 / UTF-32 は相互運用性が低く非推奨であること
- `ConvertTo-DotNetEncoding` は .NET クラスライブラリを直接利用する場合のための特殊なコマンドであること
- `System.Text.Encoding` インスタンスを直接渡した場合のみ `GetPreamble()` が尊重されること（`Encoding.GetEncoding(65001)` を渡すと BOM 付きになる）

### 6.2 README / CHANGELOG

追加機能の概要と、上記の注意点への導線を記載すること。

---

## 7. 未決事項 — 実装前に確認すること

仕様書 10 節の項目は未決である。該当箇所に到達したら、**独自に決めずに確認を求めること。**

1. **混在改行の継承** — `-EncodingFrom` の参照元が CRLF / LF 混在ファイルの場合。まず `Resolve-Encoding` の `LineBreak` がそのケースで何を返すかを調査し、結果を報告すること
2. **空ファイルからの継承** — `-Encoding Auto` で対象が 0 バイトの場合
3. **同一パスの往復** — `Get-ProbedContent a.txt | Set-ProbedContent a.txt` をエラー化するか、標準どおりの挙動とするか

---

## 8. 作業の進め方

- 第 1 段階から順に、段階ごとにコミットを分けること
- 各段階の完了時に、実装内容とテスト結果を報告すること
- 仕様書に判断材料が無い箇所に遭遇したら、**推測で実装せず質問すること**。本仕様は細部まで議論の上で決定されており、推測による補完は意図と食い違う可能性が高い
- パフォーマンス最適化は、正しさが確認できるまで行わないこと
