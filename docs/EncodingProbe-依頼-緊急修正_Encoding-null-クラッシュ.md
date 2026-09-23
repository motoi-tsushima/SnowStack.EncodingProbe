# EncodingProbe — 緊急修正：`Encoding` が null のときの `NullReferenceException`

- 作成日: 2026-09-23
- 位置づけ: **リリース済みの 1.1.0 から存在する不具合**の修正。1.2.0 に含める
- 発見の経緯: 1.2.0 第一次修正の追加調査（シングルバイト系言語の信頼度測定）
- **本依頼では、この不具合の修正だけを行う。** テストデータの追加・仕様書の整理・測定結果の文書化は別依頼とする

---

## 1. 不具合

ルーマニア語のテキストを判定すると `NullReferenceException` が発生し、利用者に例外がそのまま飛ぶ。

- UTF.Unknown は `iso-8859-16` を信頼度 0.81〜0.82 で返す
- .NET には `iso-8859-16` が無いため、`result.Detected.Encoding` が **null** になる
- `EncodingProbe.ApplyUtfUnknownResult` は `result.Detected.Encoding.CodePage` を読むため、ここで落ちる
- `catch` は `ArgumentException` と `NotSupportedException` しか捕まえておらず、null 参照は素通りする

### 影響範囲

- 検出モード Combined と UtfUnknownOnly の両方
- すべてのカルチャー（`ro-RO` や `en-US` を含む）
- PS 5.1 / PS 7 の両方
- `Resolve-Encoding` と `Get-ProbedContent` で例外が利用者に到達する
- **同じコードが master のリリース済み 1.1.0 にある**

---

## 2. 修正

`ApplyUtfUnknownResult` に null チェックを足し、既存の「.NET が非対応」の分岐と同じ結果にする。

- `EncodingWebName` … UTF.Unknown が返した名前を保存する
- `CodePage` … -1
- `PSEncodingName` … 既存の非対応時と同じ扱い

既存の 2 つの `catch`（`ArgumentException` / `NotSupportedException`）と結果が一致することを確認すること。
判定できても .NET で読めない場合の扱いは 1.1.0 で決めた仕様であり、そこは変えない。

`Get-ProbedContent -Encoding Auto` では、この後 `DetectedCodePageNotAvailable` の経路に入るはずである。
実際にそうなるか確認し、ならない場合は報告すること。

---

## 3. テスト

- `iso-8859-16` のルーマニア語バイト列を入力とし、**例外が出ないこと**と `CodePage` が -1 になることを検証する回帰テストを追加する
  - Combined と UtfUnknownOnly の両方
  - カルチャーは少なくとも `ro-RO` と `en-US`
  - テストデータはバイト列を明示して組み立てる（`CodePagesEncodingProvider` に依存しないため）
- `Resolve-Encoding` と `Get-ProbedContent` が例外を投げずに、決められた結果またはエラーを返すことを確認する
- net48 / net10.0 の両方で通ること
- 既存のテストがすべて通ること

---

## 4. 記録

- `CHANGELOG.md` の 1.2.0 節に、**1.1.0 から存在した不具合の修正**であることを明記して記載する
- 仕様書に、UTF.Unknown が .NET に無いエンコーディングを返す場合の扱い（名前だけ保存、`CodePage` は -1）が
  すでに書かれているか確認し、無ければ追記する。`iso-8859-16` を実例として挙げる

---

## 5. 確認したいこと

- `iso-8859-16` 以外に、UTF.Unknown が返しうるが .NET に無いエンコーディングがあるか。
  分かる範囲で列挙すること（網羅は不要。同種の入力で落ちる経路が他に無いかの確認が目的）
