# EncodingProbe 1.2.0 手動確認後の修正 — Claude Desktop への共有メモ

- 作成日: 2026-09-29
- 作成: Claude Code（実装担当）
- 目的: 仕様の検討・文書作成を担当する Claude Desktop に、手動確認後の修正で何をしたか、何を決めたか、何が残っているかを伝える
- リポジトリ: `motoi-tsushima/SnowStack.EncodingProbe`、ブランチ `feature/1.2.0-world-language-detection`
- コミット: `4c5b1b3`（push 済み）。**リリースと master へのマージは未実施**

---

## 1. 前提

依頼は `docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md`（1〜5 章。完了後に削除。コミット `4c5b1b3` までの git の履歴に残っている）。1.3.0 向けの内容と、クラスライブラリの判定ロジックには手を入れていない。
課題文書 `docs/EncodingProbe-課題-カルチャーによるシングルバイトの推定.md` は追加のみで、未実装（1.3.0 以降で検討）。

実装の詳細・判断の理由は `docs/EncodingProbe-1.2.0-実装記録.md` **4 章**にすべて書いてある。以降の検討ではそちらを正とすること。

---

## 2. 行ったこと

| 依頼 | 内容 | 主な変更箇所 |
|---|---|---|
| 1 章 | net10.0 ビルドの `PSEncodingName` に入っていた `I do not know.` を null にした | `EncodingDetector.EncodingName`（戻り値を `string?`、既定値 null） |
| 2 章 | ファイルを変更しない検査を `ShouldProcess` より前に置いた | `OutProbedFileCommand`、`ProbedContentWriterCommandBase.CheckWritable`、`ConvertProbedContentCommand`（N8） |
| 3 章 | `Convert-ProbedContent -PassThru` に `SourceCodePage`（int）を `SourceEncoding` の直後に追加 | `ConvertProbedContentCommand` |
| 4 章 | シングルバイト系の判定の限界を文書化 | ヘルプ 6 コマンド × 5 言語、README、1.1.0 仕様書 4.4 節 |
| 5 章 | 仕様書・実装記録の整合 | 1.2.0 仕様書 2.10 / 2.14 / 4.4 / 13.1 / 15.1 / 16 / 17 / 19.3、実装記録 2.6 追記・4 章 |

そのほか CHANGELOG（「手動確認後の修正（2026-09-29）」節）、psd1 の ReleaseNotes、CLAUDE.md を更新した。

---

## 3. 依頼と異なる判断（利用者の承認済み、または実装記録に理由を記載）

1. **`PSEncodingName` を null にする範囲（利用者に確認済み）**
   依頼 1.4 のテストは 20932・950 でも null を求めていたが、net10.0 ビルドは「フレンドリ名が無ければ WebName」という設計で、
   1.1.0 でも `shift_jis` / `euc-jp` / `big5` を返している。利用者の判断で、**独自判定の表に無いコードページ（従来 `I do not know.` だったもの）だけを null** にした。
   今後の文書で「フレンドリ名が無ければ null」と一般化して書かないこと
2. **`Out-ProbedFile` の検査順序**
   依頼は `-NoClobber`・読み取り専用の後ろへの移動だったが、`-EncodingFrom` の参照先と出力先の既存ファイルの判定も読み取りだけなので、
   同じく `ShouldProcess` の前に置いた（`Set-` / `Add-ProbedContent` は 1.1.0 からこの順）
3. **`Convert-ProbedContent` の N8**
   検査を通った時点で「書く予定の名前」を記録する。その結果、`-Confirm` で拒否したファイル・書き込みに失敗したファイルの名前も記録済みになる
4. **N8 のメッセージ（5 言語）**
   「既に書き込んだ」は `-WhatIf` では事実と異なるため、「書き込み先として既に使われている」に改めた
5. **依頼外の修正: WebName の小文字化**
   PSCompat で、`SourceEncoding` が PS 5.1 では `EUC-JP`、7.x では `euc-jp` になる食い違いが見つかった（.NET Framework が 20932 の WebName を大文字で返す）。
   `EncodingVocabulary.GetUnifiedName` で小文字にそろえた
6. **`Set-` / `Add-ProbedContent` の挙動変更（CHANGELOG に記載）**
   読み取り専用（`WriteAccessDenied`）と親ディレクトリ無し（`WriteFailed`）を `-WhatIf` でも報告する。
   エラー ID・分類は従来と同じだが、メッセージが .NET の文言から本モジュールの 5 言語の文言に変わった

---

## 4. 検証結果

| 対象 | 結果 |
|---|---|
| コア net10.0 | 1179 件すべて成功 |
| コア net48 | 1189 件すべて成功 |
| PowerShell 層（net10.0） | 707 件すべて成功 |
| PSCompat（PS 5.1 / 7.x） | 489 シナリオ、完全に一致 |

追加したテスト:

- `tests/EncodingProbe.Tests/DetectorTests/PSEncodingNameUnknownCodePageTests.cs`（1 章、両 TFM）
- 各コマンドのテストクラスに `-WhatIf` の有無で同じエラー ID になることの検査（2 章）
- `ConvertProbedContentTests` に `SourceCodePage` の往復（EUC-JP / Shift_JIS / Big5+HKSCS / windows-1252 / UTF-8 BOM / UTF-16LE）と 0 バイトの場合（3 章）
- PSCompat: `World/PSEncodingName/*`、`WhatIf/{normal,WhatIf}/*`、`ConvertProbedContent/passthru/SourceCodePage-restores/*`

ヘルプは `zh-TW` を `zh-HK` / `zh-MO` に複製し、手元の `publish/` の 14 か所にもコピーした（`publish/` のヘルプと DLL は git 管理外。DLL は更新していない）。

---

## 5. 残っていること・決めてほしいこと

- 完了した依頼文（`docs/request.md` と修正依頼）は、これまでの運用どおり削除した
- **既存メッセージ中の WebName の大文字小文字**: `Add-ProbedContent` の `EncodingChangeOnAppend` など、1.1.0 からあるエラーメッセージにも
  `EUC-JP` / `euc-jp` の差が出うる。依頼範囲外のため未対応（実装記録 4.3）。1.2.0 で直すか、1.3.0 に回すかの判断が必要
- **1.3.0 以降の検討事項（保留中）**
  - カルチャーによるシングルバイトの推定（課題文書）
  - 変換元が 0 バイトのときの `SourceEncoding` / `SourceCodePage`（現状は変換先、無ければ utf8NoBOM とみなす。1.2.0 仕様書 13.1）
- **リリース作業**: 1.2.0 のリリースと master へのマージは未実施。`publish/` への DLL の配置は手作業

---

## 6. 参照先

| 知りたいこと | 文書 |
|---|---|
| 依頼の原文 | `docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md`（削除済み。`git show 4c5b1b3:"docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md"` で読める） |
| 実装の判断と調査結果 | `docs/EncodingProbe-1.2.0-実装記録.md` 4 章 |
| 改めた仕様 | `docs/EncodingProbe-1.2.0-仕様書.md`（各節に「改めた経緯（2026-09-29）」） |
| シングルバイト系の判定の限界 | `docs/EncodingProbe-1.1.0-仕様書.md` 4.4 節「シングルバイト系の判定の限界（1.2.0）」 |
| 利用者向けの説明 | `docs/EncodingProbe-1.2.0-説明-手動確認後の修正_ブログ向け.md` |
| 変更の一覧 | `CHANGELOG.md` の 1.2.0 節 |
