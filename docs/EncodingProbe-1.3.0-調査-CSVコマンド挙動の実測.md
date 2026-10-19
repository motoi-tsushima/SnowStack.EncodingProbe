# EncodingProbe.PowerShell 1.3.0 — 調査：標準の CSV コマンドの挙動の実測

- 作成日: 2026-10-15
- 測定日: 2026-10-15
- 作業の性質: 調査のみ。製品コードは変更していない
- 仕様書: `docs/EncodingProbe-1.3.0-仕様書.md`（本書の結果を根拠にしている）

---

## 0. 要約

| 論点 | 主な結果 |
|---|---|
| 全体を 1 個の文字列にした `ConvertFrom-Csv` | 15 ケースすべてで、同じホストの `Import-Csv` と同じ結果。**5.1 でも 7.6.7 でも同じ** |
| 1 行ずつ渡した `ConvertFrom-Csv` | 引用符の中の改行でレコードが分断され、ヘッダーより後の `#` で始まる行が消え、W3C 形式は何も出力されない。両ホストで同じ |
| 5.1 の `Import-Csv` の欠陥 | **CR だけの改行の CSV を読むと 0 件（エラーも警告もない）**。W3C `#Fields:` に未対応。`Get-ChildItem \| Import-Csv` がエラー |
| ワイルドカード | 両ホストとも、`Import-Csv -Path` が複数のファイルに一致するとエラー（`ReadWriteMultipleFilesNotSupported`） |
| 厳密な復号 | 7.6.7 の `Import-Csv` に例外フォールバック付きの `Encoding` を渡すと、2966 件を出力した後にエラーになる |
| 5.1 の `Export-Csv` の既定 | `#TYPE` 行を書き、**ASCII で書くので日本語は `???`（`3F 3F 3F`）** |
| 辞書の書き出し | 5.1 は辞書オブジェクト自体のプロパティ（`Count`、`Keys`…）を書く。7.6.7 はキーを列にする |
| `-Append` の欠陥（両ホスト） | 文字エンコーディングの違うファイルに混ぜる。改行の違うファイルに混ぜる。**最後の行に改行が無いと行をつなげる** |
| 同じファイルの読み書き | `Import-Csv r.csv \| Export-Csv r.csv` で 0 バイトになる。両ホストで同じ |
| 入力が空 | `Export-Csv` は 0 バイトのファイルを作る。両ホストで同じ |
| `-Append -NoHeader` | 7.6.7 はエラーにならない（master のソースは拒否するコードになっている） |

---

## 1. 測定の方法

### 1.1 成果物

| 成果物 | 場所 |
|---|---|
| 測定スクリプト | `tools/Measure-CsvBehavior.ps1`（1 ホスト分を測り、テキストの報告を書く） |
| 生の測定結果 | `tools/CsvBehaviorResults/csv-measure-Desktop-5.1.26100.9711.txt`、`tools/CsvBehaviorResults/csv-measure-Core-7.6.7.txt` |

スクリプトと報告は ASCII 文字だけで書いている（非 ASCII 文字は `\uXXXX` で出力する）。5.1 で BOM の問題が起きず、2 つの報告をそのまま比較できる。
テストデータはバイト列を明示して作っている。

再測定の手順（両ホストで実行する）:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Measure-CsvBehavior.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Measure-CsvBehavior.ps1
```

### 1.2 環境

| 項目 | 値 |
|---|---|
| OS | Windows 11（Microsoft Windows NT 10.0.26300.0） |
| ホスト | Windows PowerShell 5.1.26100.9711、PowerShell 7.6.7（管理者権限で実行） |
| カルチャー | ja-JP（リスト区切り文字 `,`） |
| `Encoding.Default` | 5.1: 932 / shift_jis、7.6.7: 65001 / utf-8 |
| 改行 | CR-LF |

### 1.3 節の構成

| 節 | 内容 |
|---|---|
| M0 | 環境 |
| M1 | `Import-Csv` と `ConvertFrom-Csv`（全体を 1 個の文字列／1 行ずつ）の比較、15 ケース |
| M2 | `Import-Csv -Path` のワイルドカードと複数のパス |
| M3 | `Import-Csv -Encoding`（Shift_JIS、厳密な復号） |
| M4 | `Export-Csv` / `ConvertTo-Csv` の既定と値の書き方 |
| M5 | `Export-Csv -Append`（a〜l） |
| M6 | 同じファイルの読み書き、`-NoClobber`、`-WhatIf`、入力が空 |

---

## 2. M1 — `Import-Csv` と `ConvertFrom-Csv`

### 2.1 全体を 1 個の文字列にした場合

c10（列名の重複。どれもエラーで、コマンド名だけが違う）を除く 14 ケースで、`ConvertFrom-Csv` に全体を 1 個の文字列で渡した結果は、
同じホストの `Import-Csv` と完全に一致した。**5.1 でも 7.6.7 でも同じ。** 解析は同じ処理（7.x のソースでは `ImportCsvHelper`）で行われている。

### 2.2 1 行ずつ渡した場合

| ケース | 1 行ずつ渡した結果（両ホスト） |
|---|---|
| c01 引用符の中の CR-LF | `"line1` と `line2"` が別のレコードになる |
| c02 引用符の中の LF | 同上 |
| c03 ヘッダーより後の `#1,abc` | **行が消える**（エラーも警告もない） |
| c06 W3C `#Fields:` | 何も出力されない |
| c12 CR だけの改行（5.1） | 1 行ずつ渡すと読める（`Import-Csv` は 0 件） |

`ConvertFrom-Csv` は入力の文字列ごとに新しい解析を始め、先頭が `#` なら `#TYPE` 行かどうかを調べ、そうでなければ捨てるためである。

### 2.3 5.1 と 7.6.7 で `Import-Csv` の結果が違ったケース

| ケース | 入力（要点） | 5.1 | 7.6.7 |
|---|---|---|---|
| c04 | `#TYPE Foo.Bar` | 型名 `CSV:Foo.Bar` が先 | 型名 `Foo.Bar` が先 |
| c06 | `#Software: x` / `#Fields: date time` / `2026 10` | 列名 `#Fields: date time` | 列名 `date time`（W3C 形式として解釈） |
| c12 | `A,B` `1,2` `3,4`（CR だけの改行） | **0 件（エラーも警告もない）** | 2 件 |

それ以外のケース（c01〜c03、c05、c07〜c11、c13〜c15）は、両ホストで同じ結果だった。

### 2.4 7.6.7 の `Import-Csv` の結果（仕様書の期待値）

| ケース | 結果 |
|---|---|
| c01 | `{Name=a, Memo=line1\r\nline2}`、`{Name=b, Memo=x}` |
| c02 | `{Name=a, Memo=line1\nline2}` |
| c03 | `{Name=#1, Memo=abc}`、`{Name=2, Memo=def}` |
| c04 | 型名 `Foo.Bar`、`{Name=a, Memo=b}` |
| c05 | `{Name=a, Memo=b}`（`# comment` は読み飛ばす） |
| c06 | `{date time=2026 10}` |
| c07 | `{Name=a, Memo=b}`（空行は読み飛ばす） |
| c08 | `{Name=a␠, Memo=b}`（`  a , "b"  ` → 前の空白は除き、`a` の後ろの空白は残る） |
| c09 | `{A=1, B=2}`、`{A=4, B=$null}`、`{A=5, B=6}` |
| c10 | 終了エラー `AlreadyPresentPSMemberInfoInternalCollectionAdd` |
| c11 | 警告（列名を `H` で補う）、`{A=1, H1=2, C=3}` |
| c12 | `{A=1, B=2}`、`{A=3, B=4}` |
| c13 | `{A=日本語, B=追記}` |
| c14 | `{A=1, B=2}`（ヘッダーの末尾の空の列名は除く） |
| c15 | `{A=he said "hi", B=x}` |

---

## 3. M2 — パス

| ケース | 5.1 | 7.6.7 |
|---|---|---|
| `-Path w*.csv`（2 件に一致） | 終了エラー `ReadWriteMultipleFilesNotSupported` | 同左 |
| `-Path zz*.csv`（0 件） | 終了エラー `FileOpenFailure` | 同左 |
| `-Path [x].csv`（角かっこがワイルドカードとして解釈される） | 終了エラー `FileOpenFailure` | 同左 |
| `-LiteralPath [x].csv` | 読める | 読める |
| `-Path` に配列（w1.csv、存在しない missing.csv、w2.csv） | 終了エラー `FileOpenFailure`（残りも読まない） | 同左 |
| `Get-ChildItem w*.csv \| Import-Csv` | **終了エラー `CannotSpecifyPathAndLiteralPath`** | 2 件とも読める |

---

## 4. M3 — `Import-Csv -Encoding`

| ケース | 5.1 | 7.6.7 |
|---|---|---|
| Shift_JIS の CSV、`-Encoding Default` | 正しく読める（`Default` = CP932） | 文字化け（`Default` = UTF-8。`U+FFFD` になる） |
| Shift_JIS の CSV、`Encoding` オブジェクト（932） | パラメータの検証エラー（列挙型のため渡せない） | 正しく読める |
| Shift_JIS の CSV、`-Encoding 932`（数値） | — | 正しく読める |
| BOM 無し UTF-8 の `Encoding` オブジェクト | — | 正しく読める |
| 3000 行の後に不正なバイト（`FF`）、例外フォールバック付きの `Encoding` | — | **2966 件を出力した後に**終了エラー `System.Text.DecoderFallbackException` |
| 同じファイル、既定の復号 | — | 不正なバイトは `U+FFFD` になり、エラーにならない |

厳密な復号を `Import-Csv` に任せると、エラーになる前にレコードを出力してしまう。仕様書 4 章で、検証を本来の処理より前に独立させた根拠である。
（2966 件は、`StreamReader` のバッファの大きさによって決まる値であり、固定の意味はない）

---

## 5. M4 — `Export-Csv` / `ConvertTo-Csv`

### 5.1 既定の出力

入力は、`Name=日本語`、`Quote=say "hi"`、`Multi="l1`nl2"`、`Comma=a,b`、`Flag=$true`、`When=2026-10-15 14:30:00`、`Num=1234.5`、`Empty=$null` の 1 オブジェクト。

| ケース | 5.1 | 7.6.7 |
|---|---|---|
| `Export-Csv`（オプションなし） | 1 行目に `#TYPE System.Management.Automation.PSCustomObject`。**日本語が `22 3F 3F 3F 22`（`"???"`）** | `#TYPE` 行なし。BOM 無し UTF-8 |
| `-NoTypeInformation -Encoding UTF8` | BOM（`EF BB BF`）付き | BOM 無し |
| 改行 | CR-LF（値の中の LF はそのまま `0A`） | 同左 |

7.6.7 の `ConvertTo-Csv` の出力（仕様書の期待値）:

```text
"Name","Quote","Multi","Comma","Flag","When","Num","Empty"
"日本語","say ""hi""","l1
l2","a,b","True","2026/10/15 14:30:00","1234.5",
```

日付と数値の書式は、両ホストで同じだった（どちらもカルチャー ja-JP の書式）。値が `$null` の列は、引用符を付けずに何も書かない。

### 5.2 7.6.7 だけにあるオプション

| オプション | 2 行目の出力 |
|---|---|
| `-UseQuotes AsNeeded` | `日本語,"say ""hi""","l1\nl2","a,b",True,2026/10/15 14:30:00,1234.5,` |
| `-UseQuotes Never` | `日本語,say "hi",l1\nl2,a,b,True,2026/10/15 14:30:00,1234.5,`（区切り文字を含む値で CSV が壊れる） |
| `-QuoteFields Name,Num` | `"日本語",say "hi",l1\nl2,a,b,True,2026/10/15 14:30:00,"1234.5",`（ヘッダーも `"Name"` と `"Num"` だけ囲む） |
| `-NoHeader` | ヘッダー行を書かない |

### 5.3 入力の種類による違い

| 入力 | 5.1 | 7.6.7 |
|---|---|---|
| `[ordered]@{ A = 1; B = 'x' }` | 列 `Count, IsReadOnly, Keys, Values, IsFixedSize, SyncRoot, IsSynchronized` | 列 `A, B` |
| `@{ A = 1 }` に NoteProperty `Extra` を追加 | 列 `Extra, IsReadOnly, IsFixedSize, IsSynchronized, Keys, Values, SyncRoot, Count` | 列 `A, Extra` |
| `{A=1,B=2}` と `{B=3,C=4}` | 列 `A, B`。2 行目は `,"3"`（`C` は書かれない） | 同左 |
| 文字列 `'abc','de'` | 列 `Length`（`3`、`2`） | 同左 |
| `-Delimiter ';'` | `"x;y";"z"` | 同左 |
| `-UseCulture`（ja-JP） | 区切り文字 `,` | 同左 |

---

## 6. M5 — `Export-Csv -Append`

追記先は、特記の無い限り `"A","B"` / `"1","2"`（CR-LF、BOM 無し UTF-8）。

| # | ケース | 5.1 | 7.6.7 |
|---|---|---|---|
| a | プロパティの順序が違う（`B`, `A`） | 追記先の列順で `"a","b"` | 同左 |
| b | 列 `B` が無い、`-Force` なし | 終了エラー `CannotAppendCsvWithMismatchedPropertyNames`。ファイルは変わらない | 同左 |
| b2 | 列 `B` が無い、`-Force` | `"a",` を追記 | 同左 |
| c | 余分なプロパティ `C` | `"a","b"` を追記（`C` は**黙って捨てる**） | 同左 |
| d | 0 バイトのファイル | `#TYPE` 行・ヘッダー・行を書く | ヘッダー・行を書く |
| e | 存在しないファイル | 同上 | 同上 |
| f | BOM 付き UTF-8 の追記先に日本語を追記（`-Encoding` なし） | UTF-8 で追記（一貫している） | 同左 |
| g | Shift_JIS の追記先に日本語を追記（`-Encoding` なし） | **UTF-8（`E8 BF BD E8 A8 98`）で追記され、混ざる** | 同左 |
| h | `-Append -NoHeader` | パラメータが無い | エラーにならず `"a","b"` を追記 |
| i | 追記先のヘッダーが小文字（`a,b`） | 大文字小文字を区別せずに照合し、`"x","y"` を追記 | 同左 |
| j | 追記先の最後の行に改行が無い（`1,2`） | **`1,2"x","y"` とつながる** | 同左 |
| k | 追記先が LF だけ | CR-LF の行を追記し、**改行が混ざる** | 同左 |
| l | 読み取り専用、`-Force` なし | 終了エラー `FileOpenFailure`（アクセス拒否）。読み取り専用のまま | 同左 |
| l2 | 読み取り専用、`-Force` | 追記し、読み取り専用の属性を戻す | 同左 |

---

## 7. M6 — その他

| ケース | 5.1 | 7.6.7 |
|---|---|---|
| `Import-Csv r.csv \| Export-Csv r.csv` | **0 バイトになる** | 同左 |
| `-NoClobber` で既存のファイル | 終了エラー `NoClobber`。ファイルは変わらない | 同左 |
| `-WhatIf` で新しいファイル | ファイルを作らない | 同左 |
| 入力が空（`@() \| Export-Csv`） | 0 バイトのファイルを作る | 同左 |

---

## 8. 7.x のソースとの照合

PowerShell の master（コミット `db1d0a56`、2026-10-14）の `CsvCommands.cs` と照合した結果:

- `Import-Csv` と `ConvertFrom-Csv` は、同じ `ImportCsvHelper` で解析する。`ConvertFrom-Csv` は入力の文字列ごとに新しい `ImportCsvHelper` を作り、ヘッダーと型名だけを引き継ぐ（2.2 の結果の理由）
- `Export-Csv -Append` は、`ImportCsvHelper.ReadHeader` で追記先のヘッダーを読み、`ReconcilePreexistingPropertyNames` で列を照合する（M5 a〜c、i の結果と一致）
- master のソースには `-Append` と `-NoHeader` の同時指定を拒否するコードがあるが、リリース版の 7.6.7 では拒否されなかった（M5 h）。リリース後に入った変更と考えられる
- `-UseQuotes AsNeeded` で引用符を付ける条件は、値が区切り文字・LF・`"` のいずれかを含むこと（CR だけを含む値は対象外）

## 9. 試走（参考）

測定スクリプトは、作成時に Linux 上の PowerShell 7.5.3（root 権限、インバリアントカルチャー）でも試走した。
改行（LF）、カルチャー、読み取り専用の扱い（root では書き込める）が Windows と違うため、仕様の根拠には使っていない。
`-Append -NoHeader` は 7.5.3 でも拒否されなかった。
