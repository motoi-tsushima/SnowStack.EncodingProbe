# EncodingProbe 作業状況

ルートの `CLAUDE.md` から移した、各版の作業記録（内容は変えていない）。

## 1.1.0 の作業記録

1.1.0（`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `ConvertTo-DotNetEncoding` の追加）
は完了している。関連文書:

- `docs/EncodingProbe-1.1.0-仕様書.md` … 機能仕様。挙動を確認するときはまずここを見る
- `docs/EncodingProbe-1.1.0-ClaudeCode指示書.md` … 実装時の制約
- `docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md` … 決定事項とその根拠、踏んだ落とし穴。
  **仕様書に書かれていない判断の理由はここにある**
- `docs/EncodingProbe-1.1.0-課題_人間記述用.md`… Claude Code 実装後に、人間が確認して発見した課題を記述している。Claude Code 再起動時はこの課題を解消すること。
  起票済みの 3 件（`-Culture` / `-Strategy` の追加、ヘルプの 5 言語化）はすべて対応済み
- `docs/EncodingProbe-1.1.0-動作確認手順書.md` … 手元の PC で動作を確認する手順
- `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` … コアの判定エンジン側の未着手課題（3 件）

1.1.0 では次を変更していない（指示書 1 節の制約。今後も維持すること）:

- `Resolve-Encoding` / `Get-EncodingProbePlatformInfo` の既存のパラメータと戻り値を変更しない
  （1.3.0 で `Resolve-Encoding -DetectionLineCount` を追加した。利用者が明示的に決定した例外）
- `EncodingInformation` 型（`DotNetEncoding` プロパティは「追加しない」と決定済み）
- コアの NuGet パッケージの公開 API

## 1.2.0 の作業記録

**リリース済み。** クラスライブラリ（NuGet）と PowerShell モジュールの両方を公開し、
作業ブランチ `feature/1.2.0-world-language-detection` を master へマージした（2026-09-30）。

1.2.0 で行ったこと（詳細は CHANGELOG.md の 1.2.0 節）:

- 課題 1: 東アジア以外の言語への対応（カルチャーゲート、旧マルチバイトのクロスチェック、UTF-8 の厳密化）
- 第一次修正: クロスチェックの信頼度の下限（0.5 / 0.55）
- 第二次修正: 香港 Big5 段階 1（カルチャー名のサブタグ分解、繁簡の系統クロスチェック、Big5 後続バイトの厳密化）
- 第三次修正: 香港・マカオのメッセージとヘルプ（台湾と同じ内容。B 案）、メッセージの言語選択をカルチャーゲートと共通化
- 緊急修正: UTF.Unknown の `Encoding` が null（iso-8859-16 など）のときの `NullReferenceException`（1.1.0 から存在）
- 追加作業: 信頼度の測定の文書化、テストデータの追加
- PowerShell モジュールの新機能: `Out-ProbedFile` / `Convert-ProbedContent` の追加、
  `Set-` / `Add-ProbedContent` の `-Force` 後の読み取り専用属性の復元、`-LineBreak` の範囲の明文化
  （`docs/EncodingProbe-1.2.0-仕様書.md`、実装記録は `docs/EncodingProbe-1.2.0-実装記録.md`。完了した依頼文は削除した）

完了した依頼文はリポジトリから削除した（git の履歴に残っている）。判断の根拠は次の文書に移してある。

- `docs/EncodingProbe-1.1.0-仕様書.md` … 4.4 節（下限・系統クロスチェック・カルチャー名の解釈・.NET に無いエンコーディング・短文の限界）、
  8.1 節（メッセージとヘルプの言語、B 案）
- `docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md` … 下限の実測値と根拠、再測定の手順、0.55 / 0.8 の経緯
- `docs/EncodingProbe-1.2.0-課題_人間記述用.md` … 人間が確認して発見した課題。
  課題 1（東アジア以外の言語への対応）は対応済み。経緯と判断の理由は「Claude用記載欄」にある
- `docs/EncodingProbe-1.2.0-課題-香港Big5対応.md` … 香港 Big5 の構造解析と実測（第二次修正の背景）
- `docs/私用領域の扱い_方針草案.md` … 私用領域（外字・HKSCS 固有字）に介入しない方針

未着手・未決の課題:

- `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` … ISO-2022 系の未着手課題（3 件）
- `docs/EncodingProbe-課題-ヘルプの用字付きカルチャー名.md` … PS 7 で `zh-Hant-*` / `zh_HK` のヘルプが en-US になる。未着手
- `docs/EncodingProbe-課題-ヘルプ配布の自動化.md` … 配布物へのヘルプのコピーの自動化。未着手
- 短いスペイン語の行で UTF.Unknown が .NET に無い iso-8859-10 を返すと、上書き経路の対象にならず東アジアのまま残る。
  上書きして「判定不能（名前だけ）」を返すべきかは未決（測定文書 8.3 節）
- `docs/EncodingProbe-課題-香港Big5段階3_HKSCS復号.md` … HKSCS の復号・符号化。
  **本製品では対応しない（方針）。** 私用領域の内容に干渉しないのが基本方針で、香港固有字の解釈は利用者に任せる。
  対処するとしても別製品・別機能で扱う。HKSCS の対応表や写像を本製品に持ち込まないこと

2026-09-29 に手動確認後の修正（依頼 `docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md` は完了後に削除。コミット `4c5b1b3` までの git の履歴に残っている）を行った。
内容と判断は `docs/EncodingProbe-1.2.0-実装記録.md` 4 章。カルチャーによるシングルバイトの推定
（`docs/EncodingProbe-課題-カルチャーによるシングルバイトの推定.md`）と、変換元が 0 バイトのときの `SourceEncoding` の見直しは 1.3.0 以降で検討する（未着手）。

済んだ変更は CHANGELOG.md の「1.2.0」の節にまとめてある。

マージ前に、完了した実測依頼（`docs/EncodingProbe-1.2.0-実測依頼-Out-File挙動.md`）と、
手動確認後の修正の説明文書 2 件（`docs/EncodingProbe-1.2.0-説明-手動確認後の修正_ClaudeDesktop向け.md` / `_ブログ向け.md`）を
削除した（git の履歴に残っている）。

## 1.3.0 の作業記録

**PowerShell モジュールとクラスライブラリの両方を 1.3.0 としてリリースする**（当初はモジュールだけの予定だった。第四次で改めた）。作業ブランチは `feature/1.3.0-csv`。

**現在の状態（2026-10-27 時点）: 第六次 A（実装記録 11 章）・B（12 章）・C（13 章）まで実装した。テスト担当の第 2 段階（回帰テスト）が終わり、1.3.0 は 993a341 のモジュールでリリースすると利用者が決めた。その後、BUG-0030 の残り（`Get-ProbedContent -Raw` の生の OutOfMemoryException）を既知の制限として文書だけに書いた（実装記録 14 章。ソース・テスト・`publish\`・ヘルプは 993a341 から変えていない。プッシュしていない）。master へは未マージ・未リリース。**
第五次の仕上げ（実装記録 10.16 節）までは 2026-10-24 に開発用ブランチ（origin/feature/1.3.0-csv）へプッシュ済み。
第五次の後半（テスト担当の BUG-0003〜0008・0011・0015〜0017、H-17、9.9 節の 1〜4、報告の後の修正 10.14 節）は 2026-10-23 に origin/feature/1.3.0-csv へプッシュ済み。

- 両 TFM のテスト（コア net48 2139 / net10.0 2129 件、モジュール 2038 件）と PSCompat（1234 シナリオ）が通っている（第六次 C を含む。pwsh 7.6.9）。
  第六次 C では、テスト担当のスクリプト 13 本を両ホストで確かめた（実装記録 13.10 節）。
  第六次 B では、テスト担当のスクリプト 6 本を両ホスト（BUG-0030 は 32 ビット版の 5.1）で確かめた（実装記録 12.11 節）。
  第六次 A では、テスト担当のスクリプト 8 本を作業フォルダーに作った配布の構成で両ホストで確かめた（実装記録 11.11 節）。
  Linux の実測のため、WSL の Ubuntu-24.04 のホームに `~/.dotnet`（.NET 10.0.14）と `~/powershell-7.6.9` を入れてある（11.4 節）
  テスト担当のスクリプト 17 本も、作業フォルダーに作った配布の構成で両ホストで確かめた（実装記録 10.11 節・10.16 節）
  **性能:** 小さなファイルはメモリに写してから直接書くようにし、1000 個への `Set-ProbedContent` は修正の前の約 1/4〜1/5 になった。第四次よりまだ約 2 倍遅いのは、利用者が受け入れた（直さない）
  **著作権の表記:** すべて `Copyright (c) 2026 Motoi Tsushima` にそろえた（10.16 節の表。`Authors`・`Company` は `motoi.tsushima` のまま）
  **書き込みはファイルの ACL に従う（利用者の決定）。** 置き換えの前に `SafeFileOverwrite.EnsureWritable` で書き込み用に開けるかを確かめる（権限の拒否だけを失敗にし、共有違反は無視する）。10.16 節の末尾
- 第五次の前半の内容・決定・結果は実装記録 9 章、後半は 10 章（標準の実測、`::$DATA` とカルチャー名の案と決定、保たれるもの、決めきれなかったこと）、仕上げは 10.16 節
- テスト担当のフォルダー（`C:\_test\EncodingProbeBboxTest`）は読むだけにする。テスト担当にまだ伝えていない件（コアの改行の判定）を、テスト担当の報告やフォルダーに書かないこと
- 第五次の依頼文（`docs/EncodingProbe-1.3.0-依頼-第五次*.md` があれば）もコミットしない
- **配布フォルダー（`publish/SnowStack.EncodingProbe.PowerShell/`）の DLL は第三次（クラスライブラリ 1.2.0）のビルドのまま。** 第五次の後の Release ビルドで作り直す必要がある（リリース手順 2 章）
- リリースで残っている作業（`docs/EncodingProbe-1.3.0-リリース手順.md` 2 章・4 章）: 配布フォルダーの作り直し、README のリリース日の行、master へのマージ、
  配布フォルダーの公開、NuGet パッケージ 1.3.0 の公開、実験用ブランチの削除
- **利用者の判断待ち:** 配布フォルダーの `LICENSE.txt` / `THIRD-PARTY-NOTICES.txt` は Git の未追跡ファイルのまま（コミットしていない）。
  Git で管理するか、`.gitignore` に加えるかは未決
- 手動確認で課題が見つかったら、1.1.0 / 1.2.0 と同じく `docs/EncodingProbe-1.3.0-課題_人間記述用.md`（まだ無い）に利用者が書いて渡す。
  次に起動したときは、まずこのファイルの有無を確かめる
- 依頼文 4 件（`docs/EncodingProbe-1.3.0-依頼-第一次.md` / `第二次` / `第三次` / `第四次`）は手元に未追跡ファイルとして残っている。コミットしないこと（利用者の指示）
- **2026-10-20 追加:** 利用者が、BOM 無し UTF-16 / UTF-32 の英数字だけのファイルが us-ascii になる課題を、クラスライブラリとモジュールの **1.3.0 として同時に**直すと決めた
  （モジュールだけのリリースという方針を改める）。修正の前の実測を済ませた: 報告は `docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md`、
  スクリプトは `tools/Measure-NulInputDetection.ps1` / `tools/Invoke-NulInputMeasurement.ps1`、生の結果は `tools/NulInputDetectionResults/`。
  修正案は実験用ブランチ `investigate/1.3.0-nul-ascii`（ローカルのみ。**どこにもマージしない**）にある。
  第 2 回の実測（修正案 2：すべての単位が `<00 以外> 00` などの形に合うときだけ UTF-16 / UTF-32 とする厳密な検査）も済ませた。
  報告は同じ文書の 7 章、生の結果は `tools/NulInputDetectionResults/round2/`、修正案 2 は実験用ブランチ `investigate/1.3.0-nul-ascii-strict`（ローカルのみ。**どこにもマージしない**）。
  実測依頼の文書（`docs/EncodingProbe-1.3.0-実測依頼-NUL入りASCIIの判定.md`、`…-NUL入りの入力の判定-第2回.md`）と第四次の依頼文（`docs/EncodingProbe-1.3.0-依頼-第四次.md`）もコミットしない
- 利用者が修正案 2 の採用を決め、仕様を `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`（追補）にまとめた（仕様書なので第四次でコミットした）。
  第四次で本実装を `feature/1.3.0-csv` に改めて書いた（実験用ブランチはマージしていない）

1.3.0 で行ったこと（詳細は CHANGELOG.md の 1.3.0 節）:

- 第一次: 共通の部品（先頭 N 行の切り出し、全体の厳密な検証、PowerShell 7.x の CSV 処理の移植）と `Resolve-Encoding -DetectionLineCount`
- 第二次: `Import-ProbedCsv`
- 第三次: `Export-ProbedCsv`、`Out-ProbedFile` の殻の共通化、バージョン・文書の仕上げ
- 第四次: クラスライブラリの `0x00` を含む入力の判定の修正（追補）、クラスライブラリを 1.3.0 に。実装記録 8 章。
  修正後の判定は第 2 回の実測の「修正案 2」と 180 件すべて（両 TFM、判定 API とコマンド）で一致した
- 第五次（前半）: 書き込みで文字を失わない（表現できない文字のエラーと取り消し、継承した us-ascii を UTF-8 に）、切り出しの規則の置き換え、
  コアの改行の判定を単位で数える、読み替えたときの `LineBreak` の数え直し、ISO-2022 の検査、空の列名の ID。実装記録 9 章
- 第五次（後半）: パスの束縛（`FullName` を `-LiteralPath` に）、書き込みの 0 件のワイルドカード、同じファイルの判定を実体で、`EncodingChangeOnAppend` の取り消し、
- 第五次（仕上げ）: 小さなファイル（1 MiB 以内）はメモリに写してから直接書く（性能の修正）、著作権の表記を `Copyright (c) 2026 Motoi Tsushima` にそろえた。実装記録 10.15 節・10.16 節
- 第六次 C: `-Culture` の前後の空白を除く、逆シリアル化した判定結果のメッセージ、`-TotalCount` の別名 `-Head`・`-First`、列名の重複のメッセージにパス、`EncodingChangeOnAppend` を統一語彙名で、中国語・英語のメッセージ、`WriteFailed` をエラー コードの 5 言語のメッセージに、文書だけの BUG（0022・0023・0029・0034・0036・0040・0043・0044）、文書のそろえ直し。実装記録 13 章、仕様書 27 章
- 第六次 B: 逆向きの往復も SamePathRoundTrip（書き込みを元に戻す）、判定・変換の読み取りを ReadWrite の共有に、読み書きの失敗を 5 言語のメッセージに、一時ファイルの名前を固定長に、大きなファイルとメモリ不足を ID の付いたエラーに、Archive 属性、内部の Out-String を $PSDefaultParameterValues から守る、ACL の拒否を開く時点に。実装記録 12 章、仕様書 26 章
- 第六次 A: ISO-2022 の追記先を ASCII に戻す、CSV の列名の比較を .NET 10 にそろえる、BOM は先頭の 1 個だけ取り除く、曖昧解消で厳密に復号できない候補を選ばない（コア）、
  `ansi` / `oem` をシステムのロケールに（コアの `Locale` も）、`-Value` の `$null` を飛ばす、UTF-7 のインスタンスを拒否、5.1 でも途中で切れた末尾を置き換えの文字で読む。実装記録 11 章、仕様書 18.4・25 章
  上書きのリンク・ハードリンク・権限の扱い、`::$DATA`、`Resolve-Encoding` の検証の部品、カルチャー名を BCP 47 の形で検証（コアも）、`Os.Description` の Trim、文書（BUG-0005・0008・0011）。実装記録 10 章

関連文書:

- `docs/EncodingProbe-1.3.0-仕様書.md` … 機能仕様。実装で改めた点（`Export-ProbedCsv` のパラメータセット、ワイルドカード 0 件）は該当節に経緯を書いてある
- `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md` … 第四次（クラスライブラリの判定処理の修正）の仕様。期待値の根拠は `docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md` 7 章
- `docs/EncodingProbe-1.3.0-調査-切り出しの境目.md` … 第五次の切り出しの規則の根拠（調査ブランチの文書をそのまま持ってきたもの）。結論は 1 章の囲み（P ≤ 0.7 × N を含む改訂版）で、値は利用者が確定した
- `docs/EncodingProbe-1.3.0-ClaudeCode指示書.md` … 実装時の制約（移植の方針、参照してはならないもの）
- `docs/EncodingProbe-1.3.0-調査-CSVコマンド挙動の実測.md` … 標準の CSV コマンドの実測（5.1 と 7.6.7）
- `docs/EncodingProbe-1.3.0-実装記録.md` … 決定事項とその根拠、仕様書と異なった点。**仕様書に書かれていない判断の理由はここにある**
- `docs/EncodingProbe-1.3.0-動作確認手順書.md` … 手元の PC で動作を確かめる手順と、リリース前のチェック
- `docs/EncodingProbe-1.3.0-リリース手順.md` … 配布フォルダー（`publish/`）の作り方と公開までの順序。
  配布フォルダーの DLL とヘルプは Release ビルドの出力を写したもの（1.3.0 は 2026-10-19 に配置済み）

依頼文（`docs/EncodingProbe-1.3.0-依頼-第一次.md` など）はリポジトリに入れていない（利用者の指示）。

未着手・未決の課題（1.3.0 で起票、または 1.4.0 で検討すると決めたもの）:

- 既存のコマンド（`Get-` / `Set-` / `Add-ProbedContent`、`Out-ProbedFile`、`Convert-ProbedContent`）への `-DetectionLineCount`、
  全体で判定した場合の検証、`Out-ProbedFile` / `Add-ProbedContent` の追記での末尾の改行の補完は 1.4.0 で検討する（仕様書 1.4）
- `Get-ProbedContent -Raw` のメモリ不足を ID の付いたエラーにする（BUG-0030 の残り。巨大な 1 行・CSV の巨大なフィールドなど、生の OutOfMemoryException になるほかの経路も。実装記録 14.5・14.6 節、1.3.0 仕様書 26.5）
- `$PSDefaultParameterValues['*:Encoding']='utf8'` のとき `Convert-ProbedContent` だけがエラーになり、書き込みの 4 コマンドは警告して省略扱いになる違いをヘルプに書く（BUG-0034 の残り。実装記録 14.6 節）
- 改行の無い 2 GB 以上のファイルに `-DetectionLineCount` を指定すると、エラーになるまで 2 GB を 1 バイトずつ走査する（約 20 秒）。正しさを優先して最適化していない
