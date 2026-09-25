# EncodingProbe.PowerShell 1.2.0 — 実装記録

- 対象: `Out-ProbedFile` / `Convert-ProbedContent` の追加と既存コマンドの変更（`docs/EncodingProbe-1.2.0-仕様書.md`）
- 作成日: 2026-09-25
- 目的: 実装の過程で、仕様書・依頼（`docs/request.md`）・当初の想定と異なる結果になった点と、
  仕様書に書かれていなかったため実装側で決めた点を残す

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
