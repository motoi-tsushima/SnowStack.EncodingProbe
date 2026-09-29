# EncodingProbe.PowerShell 1.2.0 — 実装記録

- 対象: `Out-ProbedFile` / `Convert-ProbedContent` の追加と既存コマンドの変更（`docs/EncodingProbe-1.2.0-仕様書.md`）
- 作成日: 2026-09-25
- 目的: 実装の過程で、仕様書・依頼（完了後に削除。git の履歴に残っている）・当初の想定と異なる結果になった点と、
  仕様書に書かれていなかったため実装側で決めた点を残す
- 追記: 2026-09-29 の手動確認後の修正（`docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md`）は 4 章にまとめた

---

## 1. 仕様書の記述と異なった点

### 1.1 ステッパブルパイプラインは `Begin(this)` ではなく `Begin(expectInput: true)` で開始する（仕様書 8 章）

仕様書 8 章は `BeginProcessing` で `Begin(this)` を呼ぶとしていた。そのとおりに実装したところ、
`'a','b' | Out-ProbedFile x.txt` で `a` `b` がコンソールに表示され、ファイルは 0 バイトになった。

`SteppablePipeline.Begin(InternalCommand)` はプロキシコマンド用の形で、パイプラインの出力を
**渡したコマンドの出力ストリームへ直接流す**。そのため `Process` / `End` の戻り値は空になる。
`Begin(bool expectInput)` なら出力が戻り値の配列として返り、行として書き込める。
PowerShell 5.1 / 7.x の両方で同じ挙動だった。仕様書 8 章は実装に合わせて改めた。

### 1.2 不正なバイト列の位置は `DecoderFallbackException.Index` から求めない（仕様書 19.3）

仕様書 19.3 は `DecoderFallbackException.Index` / `EncoderFallbackException.Index` で位置を得るとしていた。
UTF-8 の `61 E9 62 0A`（2 バイト目が不正）で、PowerShell 7.x（.NET 10）は 1、PowerShell 5.1（.NET Framework 4.8）は 0 を返した。
`Index` の基準が実行環境によって異なり、エラーメッセージが両ホストで一致しない。

- 不正なバイト列は、復号器に 1 バイトずつ与えて例外になった位置を求め、例外が報告したバイト列（`BytesUnknown`）の先頭まで逆にたどる
  （`ConvertProbedContentCommand.FindInvalidBytes`）。UTF-8（途中の不正・末尾で途切れた文字・BOM の後）、Shift_JIS、EUC-JP、UTF-16 で両ホストの一致を確認した
- 表現できない文字は、その文字の最初の出現位置から行・桁を求める。最初に失敗する文字は、その文字の最初の出現位置にあるため
  （それより前に同じ文字があれば、そこで先に失敗している）。`EncoderFallbackException.Index` は使わない

---

## 2. 仕様書に書かれていなかったため実装で決めた点

### 2.1 エラー ID（仕様書 5.1 / 18 章「新設するものは既存の命名規則に合わせる」）

`Out-ProbedFile`（すべて終了エラー）:

| 事象 | ID | 例外 / Category | 標準の `Out-File`（実測） |
|---|---|---|---|
| `-NoClobber` で出力先が存在する | `NoClobber` | `IOException` / ResourceExists | 同じ |
| 読み取り専用で `-Force` なし | `WriteAccessDenied` | `UnauthorizedAccessException` / PermissionDenied | `FileOpenFailure` / OpenError |
| ワイルドカードが 0 件 | `FileNotFound` | `FileNotFoundException` / ObjectNotFound | `FileOpenFailure` / OpenError |
| ワイルドカードが 2 件以上 | `MultipleFilesNotSupported` | `PSInvalidOperationException` / InvalidArgument | `ReadWriteMultipleFilesNotSupported` |
| 親ディレクトリが無い | `ParentDirectoryNotFound` | `DirectoryNotFoundException` / ObjectNotFound | `FileOpenFailure` / OpenError |
| ディレクトリを指している | `PathIsNotFile` | `IOException` / InvalidArgument | `FileOpenFailure` / OpenError |

読み取り専用の ID は、既存の `Set-ProbedContent` と同じ `WriteAccessDenied` にそろえた。
メッセージはすべて `MessageCatalog` の本モジュール独自の文面で、.NET の例外メッセージ（ホストによって言語が変わる）は使わない。

`Convert-ProbedContent`:

| 番号 | ID |
|---|---|
| E1 | `NoConversionSpecified` |
| E2 | `AutoNotAllowedForConvertContent` |
| E4 / E5 | `EncodingNotAllowedForWrite`（メッセージは既存の書き込み系と同じ） |
| E6 / E8 | `BomConflict` |
| E7 / N5 | `BomNotSupportedByEncoding` |
| E11 | `DestinationNotFound` |
| N3 | `InvalidSourceBytes` |
| N4 | `UnrepresentableCharacter` |
| N6 | `WriteAccessDenied` |
| N7 | `DestinationExists` |
| N8 | `DestinationNameConflict` |
| N9 | `DestinationIsSource` |

E3・E9・E10・N1・N2・N10 は既存の ID（`EncodingAndEncodingFromAreExclusive`、`EncodingFromNotFound` など）を流用した。

### 2.2 `Convert-ProbedContent -Encoding` の検証の時点

ほかの書き込み系は裸の `utf8` や `utf7` を**パラメータ束縛の段階**で拒否するが、`Convert-ProbedContent` は
`-Bom` があれば裸名を受け付けるため、束縛の段階では `-Bom` の有無が分からない。
そこで束縛の段階では読み取り用途として解決し（解決できない名前・接尾辞の誤用はここで拒否）、
E4 / E5 は `BeginProcessing` の終了エラーにした。どちらもファイルに触れる前であることは変わらない。

また、`unicode` と `unicodeBOM` は解決後の `EncodingSpec` では区別できない（どちらも BOM 有り）。
仕様書 12.3 の表では扱いが違うため、元の指定を保持する引数変換属性（`Internal/ConvertEncodingTransformationAttribute`）を設けた。

### 2.3 12.3 の表に無い組み合わせ

- `-EncodingFrom` の参照先が Unicode 系以外で `-Bom Add` → E7（`-Encoding` に Unicode 系以外を指定したのと同じ扱い）
- `EncodingInformation` が Unicode 系以外で `-Bom Add` → E7
- Unicode 系以外の `System.Text.Encoding` インスタンスと `-Bom Add` → E8（`GetPreamble()` が空で食い違うため）

### 2.4 変換元が 0 バイトのときの `SourceEncoding`

0 バイトからは判定できない。`-SourceEncoding` が無ければ、変換先の文字エンコーディング（無ければ `utf8NoBOM`）を変換元とみなす。
既存の `EncodingInheritance.ForEmptySource`（ランタイム既定）を使うと、`-PassThru` の値が PowerShell 5.1 と 7.x で変わってしまうため。
書き出す内容は 0 バイトのままで、どちらを選んでも変わらない。

### 2.5 `Convert-ProbedContent` で `ShouldProcess` を呼ぶ時点

ファイルごとに、変換結果を求めた後・書き込みの前に呼ぶ。変換結果が元と同じで書き直さないファイルでも呼ぶ。
仕様書 17 章の「`-WhatIf` のときは `-PassThru` を出力しない」を満たすためで、その代わり
`-WhatIf` / `-Confirm` のメッセージは書き直さないファイルにも出る。
不正なバイト列・表現できない文字（N3 / N4）は変換結果を求める段階で分かるため、`-WhatIf` でも報告される（事前の確認に使える）。

### 2.6 `Out-ProbedFile -WhatIf` と検査の順序

仕様書 2.10 の順序どおり、`ShouldProcess` は `-NoClobber` と読み取り専用の検査より前にある。
そのため `-WhatIf`（または `-Confirm` で拒否）のときは、`-NoClobber` や読み取り専用のエラーは報告されない。

**追記（2026-09-29）: 手動確認の結果、方針を改めた。** 「ファイルを変更しない検査で失敗するものは、`-WhatIf` のときにも報告する」。
`ShouldProcess` を `-NoClobber`・読み取り専用・`-EncodingFrom` の参照先・出力先の既存ファイルの判定の後ろに移した。
4 コマンドの調査と修正の内容は 4.2 にある。仕様書 2.10 / 2.14 も改めた。

### 2.7 ワイルドカードが 0 件に解決された `Convert-ProbedContent -Path`

共通の基底クラスのパス解決（`ProbedContentCommandBase.ResolveOne`）は、ワイルドカードが 0 件のとき何も返さない
（`Get-ProbedContent` は標準の `Get-Content` と同じく黙って何もしない）。
仕様書 18.2 の N1 は 0 件もエラーとするため、`Convert-ProbedContent` の側で N1 を報告するようにした。
`Get-ProbedContent` などの挙動は変えていない。

---

## 3. 作業中に気づいたこと（仕様には影響しない）

- PSCompat のシナリオで `.GetNewClosure()` を付けたスクリプトブロックの中では、`$script:` 付きの変数が見えない
  （クロージャは動的モジュールのスコープで動くため）。スクリプトの最上位で定義した変数は、クロージャの中では `$` だけで参照する
- ASCII だけのファイルの `-PassThru` の `SourceEncoding` は `us-ascii` になる（判定結果が ASCII のため。仕様書 17 章の「それ以外は WebName」のとおり）

---

## 4. 手動確認後の修正（2026-09-29）

依頼: `docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md`。判定ロジック（クラスライブラリの判定処理）は変えていない。
課題文書 `docs/EncodingProbe-課題-カルチャーによるシングルバイトの推定.md` の提案（1.3.0 以降）は実装していない。

### 4.1 `PSEncodingName` の `I do not know.`（依頼 1 章）

- **発生箇所:** `SnowStack.EncodingProbe/EncodingDetector.cs` の `EncodingDetector.EncodingName(int)`（private static）。
  独自判定のコードページとエンコーディング名の表で、表に無いコードページに既定値 `"I do not know."` を返していた。
  net10.0 ビルドの `EncodingDetector.PSEncodingName(int, bool)` が、フレンドリ名の無いコードページについてこの表から WebName を引くため、
  UTF.Unknown の結果（`EncodingProbe.ApplyUtfUnknownResult` で `PSEncodingName` を設定する経路）が
  1252 / 28591 / 1251 / 1250 / 874 など表に無いコードページだと、この文字列が `PSEncodingName` に入っていた
- **影響範囲:** net10.0 ビルド（PowerShell 7.x）だけ。net48 ビルド（PowerShell 5.1）は固定名の表に一致しなければ null を返すため起きない。
  独自判定のコードページ（932 / 20932 / 950 など）はすべて表にあり、影響しない
- **公開済みの版:** 1.0.0 から存在した（`v1.0.0` タグの時点で同じコード）。1.1.0 にも含まれる
- **修正:** 表の既定値を null にした（`EncodingName` の戻り値を `string?` にした）。`UsePSName` は従来どおり false。
  文字列 `I do not know.` はほかに無かった（ヘルプ・サンプル・テストの期待値を含めて検索した）
- **依頼と異なる判断:** 依頼 1.4 のテストは 20932・950 でも `PSEncodingName` が null であることを求めていたが、
  net10.0 ビルドはフレンドリ名が無いとき WebName を入れる設計（`EncodingInformation` の XML ドキュメント、CLAUDE.md、既存テスト）で、
  1.1.0 でも 932 → `shift_jis`、20932 → `euc-jp`、950 → `big5` を返している。
  利用者に確認し、**表に無い（従来 `I do not know.` が入っていた）場合だけ null にする**ことにした。
  20932・950 の net10.0 での値は従来どおり WebName で、テストもその値を期待している
- テスト: `tests/EncodingProbe.Tests/DetectorTests/PSEncodingNameUnknownCodePageTests.cs`（両 TFM）、
  PSCompat `World/PSEncodingName/*`（1252 / 28591 / 1251 が両ホストで null）

### 4.2 `-WhatIf` で、実行すれば失敗するエラーを報告する（依頼 2 章）

修正前の順序の調査結果:

| コマンド | 修正前 | 修正 |
|---|---|---|
| `Out-ProbedFile` | パス解決 → **`ShouldProcess`** → `-NoClobber` → 読み取り専用 → `-EncodingFrom` → 既存ファイルの判定 | `ShouldProcess` を既存ファイルの判定の後ろへ移した |
| `Set-ProbedContent` / `Add-ProbedContent` | 同一パスの往復 → 既存ファイルの判定・継承元の有無 → **`ShouldProcess`** → ファイルを開く（読み取り専用・親ディレクトリ無しはここで失敗） | 読み取り専用・親ディレクトリ無しの検査を `ShouldProcess` の前に足した |
| `Convert-ProbedContent` | 読み取り → 判定 → 復号・符号化（N3 / N4）→ N9 → N8 → N7 → N6 → **`ShouldProcess`** → 書き込み | N6〜N9 は既に前にあった。N8 だけ `-WhatIf` で検出できなかったので直した |

- **`Set-` / `Add-ProbedContent`:** 読み取り専用で `-Force` なしは `WriteAccessDenied`（`PermissionDenied`）、親ディレクトリが無い場合は `WriteFailed`（`WriteError`）。
  いずれも修正前にファイルを開いた時点で出ていたのと同じ ID・分類にした。例外の型も同じ（`UnauthorizedAccessException` / `DirectoryNotFoundException`）。
  メッセージは .NET の例外の文言（実行環境で変わりうる）から、本モジュールのメッセージ（`FileIsReadOnly` / `ParentDirectoryNotFound`、5 言語）に変わった。
  **挙動が変わったため CHANGELOG に記載した**
- **`Out-ProbedFile`:** 依頼は `-NoClobber`・読み取り専用の後ろへの移動だったが、`-EncodingFrom` の参照先の判定と出力先の既存ファイルの判定
  （明示的な `Auto`、`-Append` の継承と整合性検査の準備）も読み取りだけなので、同じ方針で `ShouldProcess` の前に置いた。
  `Set-` / `Add-ProbedContent` は 1.1.0 から判定を `ShouldProcess` の前で行っており、これと揃う。
  結果として、`-WhatIf` でも出力先の既存ファイルを判定する（ファイル全体を読む）ようになった
- **`Convert-ProbedContent` の N8:** 同じ実行で書いたファイル名の記録（`_writtenDestinations`）は、実際に書いた後にだけ追加していたため、
  `-WhatIf` では 2 件目が N8 にならなかった。検査（N6〜N9）を通った時点で**書く予定のファイル名**を記録するようにした（`_plannedDestinations`）。
  その結果、`-Confirm` で拒否したファイル・書き込みに失敗したファイルの名前も記録済みとして扱い、2 件目は N8 になる。
  同じ実行での名前の重なりは利用者の指定の誤りなので、書いたかどうかに関係なく報告するのが一貫していると判断した
- **N8 のメッセージ:** 「同じ名前の別のファイルから既に書き込んだ」は `-WhatIf` では事実と異なるため、
  「同じ名前の別のファイルの書き込み先として既に使われている」に改めた（5 言語）
- テスト: 各コマンドのテストクラスに `-WhatIf` の有無で同じ ID になることの検査を足した。PSCompat `WhatIf/{normal,WhatIf}/*`

### 4.3 `Convert-ProbedContent -PassThru` の `SourceCodePage`（依頼 3 章）

- `SourceEncoding` の直後に `SourceCodePage`（int）を追加した。変換元が 0 バイトの場合は、2.4 の「変換元とみなしたエンコーディング」のコードページ
- **PSCompat で見つかった食い違い:** EUC-JP の `SourceEncoding` が PowerShell 5.1 で `EUC-JP`、7.x で `euc-jp` だった。
  .NET Framework は 20932 の WebName を大文字で返す。1.2.0 の新機能（`Convert-ProbedContent -PassThru`）の不具合なので、
  `EncodingVocabulary.GetUnifiedName` で WebName を小文字にそろえた（語彙の照合は大文字小文字を区別しないため、`-Encoding` に渡す用途に影響しない）
- 1.1.0 から、エラーメッセージの中の WebName（`Add-ProbedContent` の `EncodingChangeOnAppend` など）にも同じ大文字小文字の差がありうる。
  本依頼の範囲外のため直していない
- ヘルプ（5 言語）の `-PassThru` の説明・OUTPUTS・例 6 に、往復には Unicode 系以外は `SourceCodePage` を使うことを書いた

### 4.4 シングルバイト系の判定の限界（依頼 4 章）

- 1.1.0 仕様書 4.4 節に「シングルバイト系の判定の限界（1.2.0）」を設け、「短文での既知の限界」の 170 バイトの記述が上書き経路だけの話であることを明記した
- ヘルプ（5 言語）の NOTES に、判定を行う 6 コマンドぶん追記した。明示指定に使うパラメータ名はコマンドごとに合わせた
  （`Convert-ProbedContent` は `-SourceEncoding`、`Resolve-Encoding` は読み書きのコマンドの `-Encoding` を案内）
- README.md（NuGet パッケージの説明にも使われる）に追記した。PowerShell モジュール専用の README は無い

### 4.5 文書（依頼 5 章）

- 仕様書 19.3（`Index` を使わない）、13.1（0 バイトの変換元）、15.1（書き直さないファイルにも `-WhatIf` / `-Confirm` のメッセージが出る、`-WhatIf` で報告するエラー）、
  16 章（N7〜N9 と `-WhatIf`）、17 章（`SourceCodePage` と往復の案内）、2.10 / 2.14（検査の順序）、4.4（`Set-` / `Add-ProbedContent` の変更）を改めた
- `Convert-ProbedContent` のヘルプの NOTES に、`-WhatIf` / `-Confirm` で報告するエラーと、書き直さないファイルにもメッセージが出ることを書いた。
  `Set-` / `Add-` / `Out-` / `Convert-` の `-WhatIf` の説明に、ファイルを変更しない検査は行うことを足した
- ヘルプは `zh-TW` を `zh-HK` / `zh-MO` へ複製し、手元の `publish/` の `core\` / `desktop\` の 7 フォルダーずつ（計 14 か所）にもコピーした
  （`publish/` のヘルプと DLL は git の管理外。DLL は更新していない）

### 4.6 テストの件数（2026-09-29 時点）

| 対象 | 件数 |
|---|---|
| コア net10.0 | 1179 |
| コア net48 | 1189 |
| PowerShell 層（net10.0） | 707 |
| PSCompat（PS 5.1 / 7.x） | 489 シナリオ、完全に一致 |
