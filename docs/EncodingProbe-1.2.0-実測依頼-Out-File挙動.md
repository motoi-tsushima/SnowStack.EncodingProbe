# EncodingProbe.PowerShell 1.2.0 — 実測依頼：`Out-File` の挙動と `Set-ProbedContent` の改行処理

- 作成日: 2026-09-24
- 依頼先: Claude Code
- 目的: `Out-ProbedFile`（1.2.0 で新規追加）の仕様を確定するための事実を集める
- 作業の性質: **調査のみ。製品コードは変更しない**

---

## 0. この依頼の位置づけ

1.2.0 のコマンドレット側では `Out-ProbedFile` と `Convert-ProbedContent` を新規に追加する。
`Out-ProbedFile` は標準の `Out-File` との対比で仕様を決める方針であり、
`Out-File` のパラメータはほぼすべて採用し、`-EncodingFrom` / `-LineBreak` / `-Culture` / `-Strategy` /
`-AllowEncodingChange` を加える。

仕様の検討で、**標準の挙動を実測しないと決められない論点**が残った。本書はその実測の依頼である。

### 0.1 すでに決まっていること（実測の前提として読むこと）

- `-Encoding` 省略時は、上書き・新規作成では `utf8NoBOM`、`-Append` では追記先から継承（追記先が無ければ `utf8NoBOM` で新規作成）
- 明示的な `-Encoding Auto` は `Set-ProbedContent` と同じ意味（既存ファイルから継承、無ければエラー）

### 0.2 この実測で決めたい論点

| 論点 | 内容 | 本書の章 |
|---|---|---|
| 論点 3 | `-NoClobber` と `-Append` / `-Force` の組み合わせ | 3 章 |
| 論点 4 | パスの扱い（単一パス、ワイルドカード、相対パスの基準） | 4 章 |
| 論点 5 | 整形の層（`-Width`、`Out-String -Stream` との一致、ANSI） | 5 章 |
| 論点 8 | 文字列の中に含まれる改行の扱い（標準と `Set-ProbedContent` の現状） | 6 章 |
| （参考）論点 6 | ファイルを作成・切り詰めるタイミング、空入力 | 7 章 |

7 章は依頼範囲の外だが、4 章と同じ仕組みで安価に測れるため含めた。

### 0.3 してはいけないこと

- **製品コード（`src/` 配下など）を変更しない。** 実装方針の提案も求めない。観測した事実と、そこから言える示唆を分けて書くこと
- **仕様を決めない。** 標準の挙動に合わせるかどうかは利用者が判断する
- コミットしない。成果物の扱いは確認後に指示する

---

## 1. 成果物

| 成果物 | 置き場所 |
|---|---|
| 測定スクリプト | `tools/Measure-OutFileBehavior.ps1`（名前は既存の命名規則に合わせて変えてよい） |
| 生の測定結果 | `tools/` 配下の出力フォルダー（ホストごとに 1 ファイル。JSON 推奨） |
| 報告書 | `docs/EncodingProbe-1.2.0-調査-Out-File挙動の実測.md` |

報告書の構成は次のとおりとする。

- 各測定項目について、**PS 5.1 と PS 7.x を列に並べた表**で結果を示す
- 両者が異なる箇所を明示する
- 「観測した事実」と「仕様への示唆」を別の節に分ける
- 予想外の挙動、測れなかった項目、測定方法の限界を独立した節にまとめる

---

## 2. 測定環境と共通の注意

### 2.1 対象ホスト

| 環境 | 必須/任意 |
|---|---|
| Windows 11 / Windows PowerShell 5.1 | 必須 |
| Windows 11 / PowerShell 7.x | 必須 |
| Ubuntu 24.04 または macOS / PowerShell 7.x | 任意（実行できる場合のみ。改行の既定と幅の既定の確認用） |

各ホストで次を記録すること。

- `$PSVersionTable`（`PSVersion` / `PSEdition` / `OS`）
- `$Host.Name`
- `[Console]::IsOutputRedirected`
- `$Host.UI.RawUI.BufferSize` と `WindowSize`（例外が出る場合はその旨）
- `[cultureinfo]::CurrentCulture.Name` と `CurrentUICulture.Name`
- PS 7.2 以降では `$PSStyle.OutputRendering` の既定値

**非対話実行と対話実行では、幅の既定が変わる可能性が高い。**
Claude Code から起動するのは非対話（出力がリダイレクトされた）ホストであるため、
その値を記録したうえで、対話コンソールでの確認は付録 A の手順を利用者が手動で行う。

### 2.2 スクリプトのホスト差に関する注意

既存の `tools/` の測定スクリプトでは、ホストによって結果が変わる問題が 2 件見つかっている
（ファイル名の並び順がカルチャー依存で変わる件、PS 7 で `Array.Sort` のオーバーロード解決が異なる件）。
本スクリプトでも次を守ること。

- 並べ替えは序数比較で行う（`[StringComparer]::Ordinal` を明示する）
- 数値・日付の文字列化はインバリアントカルチャーで行う
- バイト列の 16 進表示に `Format-Hex` を使わない（バージョンで出力形式が違う）。`[IO.File]::ReadAllBytes` の結果を `ToString('X2', [cultureinfo]::InvariantCulture)` で自前整形する
- `Array.Sort` などオーバーロードが曖昧になる .NET メソッドは、型を明示してから呼ぶ
- 測定の入力データ（テスト用オブジェクト）は、日付・時刻・プロセス情報など実行ごとに変わる値を含めない

### 2.3 一時ファイル

- `[IO.Path]::GetTempPath()` の下に測定ごとの新しいフォルダーを作り、そこだけを使う
- 終了時に削除する。読み取り専用属性を付けたファイルは、属性を外してから削除する
- リポジトリの作業ツリーに一時ファイルを残さない

### 2.4 エラーの分類方法

各エラーについて、次の方法で**終了エラーか非終了エラーか**を判定し、記録する。

```powershell
$caught = $null
$ev = $null
try {
    Out-File ... -ErrorAction Continue -ErrorVariable ev
} catch {
    $caught = $_
}
# $caught があれば終了エラー、$ev だけにあれば非終了エラー
```

記録する項目は次のとおり。

- 終了/非終了の別
- `FullyQualifiedErrorId`
- 例外の型名
- `CategoryInfo.Category`
- メッセージ（カルチャー依存なので、測定時の UI カルチャーと併記する）
- エラー後のファイルの状態（存在するか、バイト列、属性）

### 2.5 バイト列の比較方法

エンコーディングの既定値を測る項目以外では、`Out-File` に `-Encoding unicode` を明示すること。
`unicode` は PS 5.1 と 7.x のどちらでも「BOM 付き UTF-16LE」であり、両者のバイト列を直接比較できる。

---

## 3. 論点 3 — `-NoClobber` / `-Append` / `-Force` の組み合わせ

### 3.1 組み合わせ表

各行について、実行後のファイルのバイト列、エラーの有無と分類（2.4）、読み取り専用属性の状態を記録する。
既存ファイルの初期内容は、区別しやすい固定の文字列（例: `OLD`）を `-Encoding unicode` で書いたものとする。

| # | 既存ファイル | 読み取り専用 | パラメータ |
|---|---|---|---|
| 3-1 | 無し | — | `-NoClobber` |
| 3-2 | 有り | 無し | `-NoClobber` |
| 3-3 | 有り | 無し | `-Append -NoClobber` |
| 3-4 | 無し | — | `-Append -NoClobber` |
| 3-5 | 有り | 無し | `-Force -NoClobber` |
| 3-6 | 有り | 有り | （指定なし） |
| 3-7 | 有り | 有り | `-Force` |
| 3-8 | 有り | 有り | `-Force -NoClobber` |
| 3-9 | 有り | 有り | `-Append` |
| 3-10 | 有り | 有り | `-Append -Force` |

特に確認したいのは次の 3 点である。

- `-Append` と `-NoClobber` を同時に指定したとき、`-Append` が優先されて追記が成功するか（3-3）
- `-Force` が `-NoClobber` を解除するか（3-5、3-8）
- `-Force` で書き込んだ後、**読み取り専用属性が元に戻されるか、外れたままか**（3-7、3-10）

### 3.2 エラーが起きる時点

3-2 と 3-6 について、エラーが**パイプラインのどの時点で**起きるかを記録する。
上流に、実行の段階を記録するスクリプトブロックを置く。

```powershell
$log = [System.Collections.Generic.List[string]]::new()
& {
    begin   { $log.Add('upstream-begin') }
    process { }
    end     { $log.Add('upstream-end'); 'x' }
} | Out-File -FilePath $p -NoClobber -Encoding unicode
```

`upstream-end` が記録される前にエラーになれば、`Out-File` の `BeginProcessing` の時点で失敗している。

### 3.3 参考測定：`Set-ProbedContent` の読み取り専用属性

比較のため、現在の `Set-ProbedContent` / `Add-ProbedContent` についても、
読み取り専用ファイルへ `-Force` で書き込んだ後の属性の状態を記録する（3-7、3-10 に相当）。
モジュールは現在のブランチのビルドを使い、どのビルドを使ったかを報告書に書くこと。

---

## 4. 論点 4 — パスの扱い

### 4.1 パラメータのメタデータ

PS 5.1 と 7.x の両方で、`Out-File` の全パラメータについて次を一覧にする（`Get-Command Out-File` の `Parameters` と `ParameterSets` から取得する）。

- 型
- 別名
- 位置（Position）
- 必須かどうか
- `ValueFromPipeline` / `ValueFromPipelineByPropertyName`
- ワイルドカード対応の属性（`SupportsWildcards`）の有無
- 検証属性（`ValidateRange`、`ValidateSet` など）とその値
- 所属するパラメータセットと既定のパラメータセット

同じ一覧を `Set-Content` についても作る（参照用）。

### 4.2 `-FilePath` のワイルドカード

| # | 状況 | 指定 |
|---|---|---|
| 4-1 | `a1.txt` が存在する | `-FilePath 'a?.txt'`（1 件に解決される） |
| 4-2 | `a1.txt` と `a2.txt` が存在する | `-FilePath 'a?.txt'`（2 件に解決される） |
| 4-3 | 該当するファイルが無い | `-FilePath 'b*.txt'` |
| 4-4 | `c[1].txt` が存在しない | `-FilePath 'c[1].txt'` |
| 4-5 | `c[1].txt` が存在しない | `-LiteralPath 'c[1].txt'` |
| 4-6 | `c1.txt` が存在する | `-FilePath 'c[1].txt'`（`c1.txt` に解決されるか） |

それぞれ、どのファイルが作成・変更されたか、エラーの有無と分類を記録する。

### 4.3 相対パスの基準

PowerShell の現在位置（`Set-Location`）と、プロセスの現在ディレクトリ（`[Environment]::CurrentDirectory`）を
**意図的に異なる場所に設定した状態**で、`Out-File -FilePath 'rel.txt'` がどちらに書き込むかを記録する。

### 4.4 異常なパス

| # | 状況 |
|---|---|
| 4-7 | 親ディレクトリが存在しない |
| 4-8 | パスが既存のディレクトリを指している |
| 4-9 | `-FilePath` に空文字列を渡す |

---

## 5. 論点 5 — 整形の層

### 5.1 目的

`Out-ProbedFile` は、`Out-String -Stream`（必要に応じて `-Width` も渡す）をステッパブルパイプラインで包み、
得られた行を符号化して書く実装を想定している。
この実装で **`Out-File` と同じ文字列が得られるか**を確かめるのが目的である。

### 5.2 比較する 3 つの経路

同じ入力に対して、次の 3 つの経路で得た文字列を比較する。

| 経路 | 方法 |
|---|---|
| A | `$input | Out-File -FilePath $p -Encoding unicode [-Width N]` の結果を読み戻した文字列 |
| B | `$input | Out-String -Stream [-Width N]` の各行を `[Environment]::NewLine` で連結した文字列 |
| C | B と同じコマンドを**ステッパブルパイプライン**で実行した結果（下記） |

```powershell
$sb = [scriptblock]::Create('Microsoft.PowerShell.Utility\Out-String -Stream -Width 80')
$sp = $sb.GetSteppablePipeline()
$sp.Begin($true)
foreach ($o in $inputs) { $sp.Process($o) }   # 戻り値を集める
$sp.End()
$sp.Dispose()
```

A と B / C の差について、次を記録する。

- 本文の差
- **先頭と末尾の空行の数**（表形式の出力は前後に空行を出す）
- **ファイル末尾の改行の有無**
- 行の区切り文字

### 5.3 入力データ

実行ごとに変わる値を含めない固定のデータを使う。

| # | 入力 | 狙い |
|---|---|---|
| 5-1 | 短い文字列 3 個 | 最も単純な場合 |
| 5-2 | 整数と小数 | 数値の整形（カルチャーを記録すること） |
| 5-3 | プロパティ 3 個の `[pscustomobject]` を 3 個 | 表形式 |
| 5-4 | プロパティ 6 個の `[pscustomobject]` | 一覧形式（5 個以上で一覧になるか） |
| 5-5 | 値が 200 文字を超えるプロパティを持つ `[pscustomobject]` | 幅を超えたときの切り詰め・折り返し |
| 5-6 | 200 文字を超える単一の文字列 | 文字列は切り詰められるか |
| 5-7 | `$null`、空文字列、空の配列 | 境界の値 |
| 5-8 | 入れ子の配列 | 展開のされ方 |
| 5-9 | 全角文字を含む表 | 幅の計算が表示幅か文字数か |

### 5.4 `-Width`

- 各入力を `-Width` 省略、`40`、`80`、`200` で出力し、各行の最大文字数を記録する
- `-Width` の検証範囲を実測する（`0`、`1`、負の値、`[int]::MaxValue`）
- `-Width` 省略時に実際に使われた幅を、非対話ホストで記録する（対話コンソールは付録 A）
- `Out-File` と `Out-String` で、`-Width` 省略時の幅が同じかどうか

### 5.5 `-NoNewline`

5-1、5-3、5-4 について `Out-File -NoNewline` の出力を記録する。
表形式の各行や、先頭・末尾の空行がどう連結されるかを確認する。

### 5.6 ANSI エスケープ（PS 7.2 以降）

次の入力について、`$PSStyle.OutputRendering` を `Host` / `PlainText` / `Ansi` のそれぞれに設定して、
経路 A・B・C の出力にエスケープシーケンス（0x1B）が含まれるかを記録する。

- エスケープシーケンスを含む文字列（`"$([char]27)[31mred$([char]27)[0m"`）
- 5-3 の表（PS 7.2 以降では表の見出しに装飾が付く場合がある）

PS 5.1 では `$PSStyle` が存在しないため、エスケープシーケンスを含む文字列がそのまま書かれるかどうかだけを記録する。

---

## 6. 論点 8 — 文字列の中に含まれる改行

### 6.1 入力

| # | 入力文字列 |
|---|---|
| 8-1 | `"a`nb"`（LF） |
| 8-2 | `"a`r`nb"`（CRLF） |
| 8-3 | `"a`rb"`（CR のみ） |
| 8-4 | `"a`n"`（末尾に LF） |
| 8-5 | `"a`r`n`r`nb"`（空行を含む） |
| 8-6 | `"a`nb"` と `"c"` の 2 要素 |

### 6.2 測定するコマンド

各入力について、書き出されたバイト列を 16 進で記録する。

| 経路 | コマンド |
|---|---|
| 標準 | `Out-File -Encoding unicode` |
| 標準 | `Out-String -Stream` が返す要素の数と各要素（**複数行の文字列を行に分けるか、CR 単独でも分けるか**） |
| 標準 | `Set-Content -Encoding unicode`（参照用） |
| 本モジュール | `Set-ProbedContent -Encoding unicodeBOM`：`-LineBreak` 省略、`CrLf`、`Lf`、`Cr` のそれぞれ |
| 本モジュール | `Set-ProbedContent -NoNewline` |
| 本モジュール | `Add-ProbedContent`：`-LineBreak Lf` で、CRLF の既存ファイルに追記 |

特に確認したいのは、**`-LineBreak` の指定が、要素と要素の間の改行だけに効くのか、文字列の中に含まれる改行も置き換えるのか**である。

### 6.3 実装の確認

`Set-ProbedContent` / `Add-ProbedContent` のソースを読み、次を報告する。

- 改行を書き出している箇所（ファイル名、型名、メソッド名）
- 文字列の中の改行に対する処理の有無と、その方法
- この挙動をテストしているテストの有無（テスト名）
- この挙動がヘルプ（MAML）や仕様書に書かれているかどうか

実装と測定結果が食い違う場合は、両方を記録する。

---

## 7. （参考）論点 6 — ファイルを作成・切り詰めるタイミング

`Out-ProbedFile` は、同一パスの往復（`Get-ProbedContent a.txt | Out-ProbedFile a.txt`）を
`ActiveReadRegistry` で検出するため、ファイルを開くタイミングを遅らせる必要があると考えている。
その判断の前提として、標準の `Out-File` がいつファイルを作成・切り詰めるかを測る。

### 7.1 切り詰めのタイミング

既存ファイル（内容 `OLD`）に対して、上流のスクリプトブロックの `begin` / `process` / `end` の各段階で
対象ファイルの存在と長さを記録し、どの時点で切り詰められたかを特定する。

```powershell
& {
    begin   { $log.Add("begin: $((Get-Item -LiteralPath $p).Length)") }
    process { }
    end     { $log.Add("end: $((Get-Item -LiteralPath $p).Length)"); 'x' }
} | Out-File -FilePath $p -Encoding unicode
```

`begin` は `Out-File` の `BeginProcessing` より前に呼ばれる点に注意して、結果を解釈すること。

### 7.2 空の入力

| # | 状況 | 指定 |
|---|---|---|
| 7-1 | ファイルが無い | `@() \| Out-File -FilePath $p` |
| 7-2 | ファイルが無い | `Out-File -FilePath $p -InputObject $null` |
| 7-3 | 既存ファイル（内容 `OLD`） | `@() \| Out-File -FilePath $p` |
| 7-4 | ファイルが無い | `@() \| Out-File -FilePath $p -Append` |

それぞれ、ファイルが作成されるか、**作成された場合に BOM だけが書かれるか（0 バイトか）**を記録する。
7-1 は `-Encoding` 省略、`unicode`、`utf8`（PS 7.x）/`UTF8`（PS 5.1）の 3 通りで行う。

---

## 8. （参考）既定のエンコーディングの確認

決定済みの論点 1・2 の根拠を確認するための、短い測定である。

- `-Encoding` 省略時に `Out-File` が書くバイト列（PS 5.1 と 7.x）
- BOM 無し UTF-8 の既存ファイル（日本語を含む）に、`-Encoding` 省略の `Out-File -Append` で日本語を追記した結果のバイト列。PS 5.1 で UTF-16LE が混ざるかどうか

---

## 付録 A — 利用者が対話コンソールで行う手順

Claude Code の実行環境は非対話のホストであるため、`-Width` 省略時の幅は対話コンソールと異なる可能性がある。
スクリプトには、**対話コンソールで実行すると 5.4 の「省略時の幅」だけを測って結果ファイルに書く**モードを用意すること
（例: `-InteractiveWidthOnly` スイッチ）。

利用者は、Windows Terminal 上の PS 5.1 と PS 7.x で、ウィンドウの幅を 2 通り（狭い／広い）に変えて実行する。
報告書には、この手順と、結果を書き込む空欄の表を用意しておくこと。
