# SnowStack.EncodingProbe.PowerShell 1.1.0 ブログ記事用資料

- 作成日: 2026-08-31
- 対象: 1.1.0（`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `ConvertTo-DotNetEncoding`）
- 位置づけ: **個人ブログの解説記事を書くための素材集。これ自体は原稿ではない**

掲載予定先は次の 2 記事の続きにあたる。

- [SnowStack.EncodingProbe NuGet Package 解説](https://snow-stack.net/encodingprobe_guide/)
- [SnowStack.EncodingProbe.PowerShell 解説](https://snow-stack.net/encodingprobe_powershell_guide/)

本書に載せた実行結果は、**すべて 2026-08-31 に実機で実行して採取したもの**である
（Windows 11 / PowerShell 7.6.5 / Windows PowerShell 5.1.26100 / Release ビルド）。
記事に転記する際は、そのまま使ってよい。採取に使ったコードも併記してある。

仕様の正確な記述が必要になったら `docs/EncodingProbe-1.1.0-仕様書.md` を、
「なぜそう決めたか」が必要になったら `docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md` を見ること。

---

## 1. 記事の中心に置きたい主張

この 1 文が言えれば記事は成立する。

> **同じスクリプトが、Windows PowerShell 5.1 でも PowerShell 7.x でも、同じバイト列を書く。**

1.1.0 は「新しい便利コマンドが 4 つ増えました」という話ではない。
**PowerShell のバージョン差という長年の地雷を、モジュールの側で踏み抜けなくした**話である。
記事の構成も、コマンド紹介から始めるのではなく、この問題から入ったほうが刺さる。

### 掴みに使える 1 つの実験

同じ 1 行を両方のホストで実行し、できたファイルのバイト列を並べる。

```powershell
Set-Content .\std.txt -Value 'abc' -Encoding UTF8
```

| ホスト | できたバイト列 |
|---|---|
| Windows PowerShell 5.1 | `EF BB BF 61 62 63 0D 0A` ← **BOM が付く** |
| PowerShell 7.x | `61 62 63 0D 0A` ← **BOM が付かない** |

同じコマンド、同じパラメータ、違う結果。`UTF8` という名前の意味が、ホストによって違うためである。
これが 5.1 と 7.x を両方使う現場で起きていることの縮図になる。

1.1.0 のコマンドなら、両方のホストで同じになる。

```powershell
Set-ProbedContent .\probed.txt -Value 'abc' -Encoding utf8NoBOM -LineBreak Lf
```

| ホスト | できたバイト列 |
|---|---|
| Windows PowerShell 5.1 | `61 62 63 0A` |
| PowerShell 7.x | `61 62 63 0A` |

**BOM 無しの UTF-8 を名前で指定する。これは PowerShell 5.1 の標準コマンドにはできない。**

---

## 2. 1.0.x で残っていた問題（記事の「前フリ」）

1.0.x の `Resolve-Encoding` は判定まではできた。しかし、その結果を
標準コマンドへ渡す段になると詰む。次のイディオムが成立しないためである。

```powershell
Get-Content -Encoding (Resolve-Encoding $f).PSEncodingName $f
```

実際に Shift_JIS のファイルで `Resolve-Encoding` を実行した結果。

| ホスト | `CodePage` | `PSEncodingName` | `UsePSName` |
|---|---|---|---|
| Windows PowerShell 5.1 | 932 | **（空）** | `False` |
| PowerShell 7.x | 932 | `shift_jis` | `False` |

- 5.1 では `-Encoding` が固定の列挙型（`Ascii` / `UTF8` / `Unicode` …）で、Shift_JIS を表す値が**存在しない**。
  だから `PSEncodingName` は `null` になる
- 7.x では名前は返せるが、`UsePSName` は `False`。
  標準コマンドの `-Encoding` にそのまま渡せる保証がないという意味である

つまり **「判定はできる。しかし判定結果でファイルを開けない」** という状態だった。
1.1.0 は、標準コマンドに橋を架けるのをやめ、
**自前の読み書きコマンドを持つ**ことでこれを解いている。

> 記事で `PSEncodingName` / `UsePSName` に触れるなら、
> 「TFM ごとに意味が違うのは仕様であってバグではない」ことを一言入れておくとよい。
> net48 ビルドは PS 5.1 の `-Encoding` 列挙値に一致するときだけ値を返す、
> net10.0 ビルドは PS 6.2+ の登録済みフレンドリ名を返す、という設計である。

---

## 3. 4 つのコマンド（一覧）

| コマンド | 役割 | 標準の対応物 |
|---|---|---|
| `Get-ProbedContent` | 判定して読む | `Get-Content` |
| `Set-ProbedContent` | 明示して書く | `Set-Content` |
| `Add-ProbedContent` | 文字エンコーディングを保って追記する | `Add-Content` |
| `ConvertTo-DotNetEncoding` | 各種の指定を `System.Text.Encoding` に変換する | （なし） |

パラメータの実際（`Get-Command` から採取）。

```
Get-ProbedContent        : Path, LiteralPath, Encoding, Raw, TotalCount, Culture, Strategy
Set-ProbedContent        : Path, LiteralPath, Value, Encoding, EncodingFrom, NoNewline,
                           LineBreak, Force, Culture, Strategy, WhatIf, Confirm
Add-ProbedContent        : （Set- と同じ）＋ AllowEncodingChange
ConvertTo-DotNetEncoding : Encoding
```

`ConvertTo-DotNetEncoding` だけ `-Culture` / `-Strategy` が無い。
**ファイルを引数に取らず、判定処理を呼ばないため**である。記事でも一言添えると親切。

---

## 4. 統一語彙 — 記事の技術的な山場

### 4.1 何が問題だったか

PowerShell の `-Encoding` に渡す名前の体系は、5.1 と 7.x で別物である。
そのうえ 7.x の体系自体も不規則で、

- `utf8` … **BOM 無し**
- `unicode` / `utf32` / `bigendianunicode` … **BOM 有り**

と、UTF-8 だけ裸名の意味が反転している。覚えられる体系ではない。

### 4.2 1.1.0 の答え

**全系統に明示形を用意して、裸名を覚えなくて済むようにした。**

| 基底名 | BOM 有り | BOM 無し |
|---|---|---|
| `utf8` | `utf8BOM` | `utf8NoBOM` |
| `unicode`（UTF-16LE） | `unicodeBOM` | `unicodeNoBOM` |
| `bigendianunicode`（UTF-16BE） | `bigendianunicodeBOM` | `bigendianunicodeNoBOM` |
| `utf32`（UTF-32LE） | `utf32BOM` | `utf32NoBOM` |
| `bigendianutf32`（UTF-32BE） | `bigendianutf32BOM` | `bigendianutf32NoBOM` |

`*BOM` / `*NoBOM` はこのモジュールの独自拡張。**記事では明示形の使用を勧めること。**

これに加えて次を受け付ける。

| 種類 | 例 |
|---|---|
| その他の語彙 | `ascii` / `ansi` / `oem` / `utf7`（読み取り専用）/ `Auto` |
| WebName | `shift_jis` / `euc-jp` / `iso-2022-jp` / `big5` / `gb18030` … |
| 数値コードページ | `932` / `65001` … |
| `System.Text.Encoding` インスタンス | `([System.Text.Encoding]::UTF8)` |
| `Resolve-Encoding` の戻り値 | `(Resolve-Encoding .\a.txt)` |

WebName と別名（`shift-jis` / `sjis` / `ms_kanji` …）は **.NET の変換表に委譲**していて、
モジュール側で別名表を持っていない。ここは「持たない設計にした」という書き方ができる箇所。

### 4.3 裸の `utf8` を書き込みで拒否する

**5.1 と 7.x で意味が衝突する名前は `utf8` ただ 1 つ**である
（`unicode` / `utf32` / `bigendianunicode` はどちらも BOM 有りで一致する）。
そこで、書き込み系では裸の `utf8` を受け付けない。

実際のメッセージ（両ホストで同一）。

```
書き込みでは 'utf8' が指定できません。UTF-8 は PowerShell 5.1 と 7.x で
BOM の解釈が異なるため、'utf8NoBOM' または 'utf8BOM' のいずれかを明示してください。
```

**記事で強調する価値があるのは、この失敗が起きるタイミング**である。

```powershell
Set-ProbedContent .\z.txt -Value 'x' -Encoding utf8
# → エラー。そして .\z.txt は作られない（Test-Path が False）
```

パラメータ束縛の段階で弾いているため、**ファイルを開く前に失敗する**。
書きかけの破損ファイルが残らない。読み取り側（`Get-ProbedContent`）では
BOM の有無が問題にならないので、裸の `utf8` を許容する。この非対称は意図的である。

---

## 5. 実演に使える題材（すべて実行結果を採取済み）

記事に載せるコード例は、この 4 つで足りると思われる。

### 5.1 Shift_JIS を名前で書く

```powershell
Set-ProbedContent .\sjis.txt -Value '日本語' -Encoding shift_jis -LineBreak Lf
```

| ホスト | バイト列 |
|---|---|
| Windows PowerShell 5.1 | `93 FA 96 7B 8C EA 0A` |
| PowerShell 7.x | `93 FA 96 7B 8C EA 0A` |

5.1 の標準コマンドには Shift_JIS を指す `-Encoding` の値が無い。
`Default` は「日本語環境ならたまたま CP932」に過ぎず、環境が変われば別物になる。

### 5.2 無損失のラウンドトリップ（実務でいちばん効く例）

「読んで、加工して、元の文字エンコーディング・BOM・改行のまま書き戻す」。
これが 3 行で書ける。

```powershell
$text = Get-ProbedContent .\sjis.txt -Raw
$text.Replace('日本語', '日本國語') | Set-ProbedContent .\sjis.txt -EncodingFrom .\sjis.txt -NoNewline
```

| | バイト列 |
|---|---|
| 加工前 | `93 FA 96 7B 8C EA 0A` |
| 加工後 | `93 FA 96 7B 9A A0 8C EA 0A` |

Shift_JIS のまま、改行 `0A`（LF）のまま、BOM 無しのまま、`國`（`9A A0`）だけが増えている。
**両ホストで同じ結果になる。**

記事で押さえるべき点が 2 つある。

- `-Raw` は利便性ではなく**情報保全**のために要る。行分割すると、行末が CRLF か LF か、
  末尾に改行があったかが失われ、後から復元できない
- 同じファイルを `-Raw` 無しで読み書きすると、読み終える前にファイルが切り詰められる。
  モジュールはこれを検出してエラーにする（`-Raw` は出力前に閉じるので対象外）

### 5.3 `-Culture` — 日本語環境から韓国語のファイルを読む

**これは「ハマった人にしか刺さらないが、刺さる人には決定的」な例。**

`C7 D1 B1 B9 BE EE`（韓国語で「한국어」）というバイト列は、**EUC-JP としても成立してしまう**。
だからバイト列だけでは決められず、カルチャーで曖昧解消する。

```powershell
Get-ProbedContent .\korean.txt              # カルチャー指定なし
Get-ProbedContent .\korean.txt -Culture ko-KR
```

| 指定 | 判定 | 読めた文字 |
|---|---|---|
| `-Culture` なし（日本語環境） | `20932` / `euc-jp` | `U+5EC3 U+53A9 U+5B22`（廃厩嬢）← **文字化け** |
| `-Culture ko-KR` | `949` / `cp949` | `U+D55C U+AD6D U+C5B4`（한국어）← **正しい** |

両ホストで同じ結果。`-Culture` は `Resolve-Encoding` と**同じ名前・同じ値**で、
`-EncodingFrom` の参照ファイルや、`-Encoding` 省略時の継承にも効く。

同種の曖昧さは他にもある。記事の補足に使える。

| 曖昧な組み合わせ | 決め方 |
|---|---|
| EUC-JP と Shift-JIS | 改行が CRLF なら Shift-JIS、LF なら EUC-JP、改行なしなら OS 既定 |
| EUC-KR と CP949 | CP949 |
| EUC-TW と CP950 | CP950（Big5） |

### 5.4 `-Strategy` — 欧米のテキストは UTF.Unknown に任せる

windows-1252 のドイツ語テキスト（`Größe Straße für Maßnahmen.` など）を、
日本語カルチャーの環境で 3 通りの判定方式にかけた結果。

```powershell
Get-ProbedContent .\german.txt -Strategy UtfUnknownOnly
```

| 指定 | 判定 | 復号結果 |
|---|---|---|
| `-Strategy Combined`（既定） | `932` / `shift_jis` | **文字化け** |
| `-Strategy NativeOnly` | `932` / `shift_jis` | **文字化け** |
| `-Strategy UtfUnknownOnly` | `28591` / `iso-8859-1` | **正しく復号** |

両ホストで同じ結果。

**ここが記事にすると面白いところ。** 既定の `Combined` でも救われない。
`Combined` は「独自判定を先に走らせ、**判定できなかったときだけ** UTF.Unknown へ委ねる」
という設計なので、独自判定が Shift-JIS だと**自信を持って誤答した**場合は
UTF.Unknown の出番が来ない。だから `-Strategy` という逃げ道が要る。

この結果は分業の設計そのものを説明している。
独自判定は東アジア漢字文化圏のマルチバイト、UTF.Unknown は欧米のシングルバイトを担当する。
**欧米のシングルバイトを日本語カルチャーで読むときは `UtfUnknownOnly` を指定する** —
これが記事に書くべき実用的な結論になる。

---

## 6. `Add-ProbedContent` の整合性検査（記事の「へえ」ポイント）

追記は、上書きと違って**既存のバイト列の続きに書く**。
文字エンコーディングが変わると、ファイルの途中から化ける。

そこで `Add-ProbedContent` は、**実際に書き出されるバイト列を比較して**追記の可否を決めている。
名前の組み合わせ表では判定しない。結果として、直感的にはややこしいが正しい挙動になる。

| 状況 | 結果 |
|---|---|
| UTF-8 のファイルへ `-Encoding ascii` で **ASCII だけ**を追記 | **許可**（バイト列が同じになるため） |
| UTF-8 のファイルへ `-Encoding shift_jis` で **日本語**を追記 | **拒否**（バイト列が変わるため） |

意図的に変えたい場合は `-AllowEncodingChange` を指定する。
**`-Force` では回避できない。** `-Force` は読み取り専用ファイルへの書き込みを許す
パラメータであって、文字エンコーディングを壊す許可ではないためである。
この 2 つを相乗りさせなかったのは意識的な設計判断。

もう 1 点、`Add-ProbedContent` では **BOM の指定が常に無視される**（新規作成の場合も含む）。
ファイルの途中に BOM を書き込むことは正しくないため。

---

## 7. 5 言語対応（記事のもう 1 つの売り）

判定処理が対象としている言語圏に、ヘルプとメッセージを揃えてある。

| 対応 | 言語 |
|---|---|
| `Get-Help`（MAML） | 英語 / 日本語 / 韓国語 / 繁体字中国語 / 簡体字中国語 |
| エラーメッセージ | 同上 |

未対応のカルチャー（`zh-HK` など）は英語になる。
香港（繁体字広東語）は後のバージョンで対応する予定。

記事に書くと親切な注意点が 1 つある。

> `Get-Help` の言語は **`Import-Module` した時点の `CurrentUICulture`** で決まる。
> 読み込んだ後にカルチャーを変えても切り替わらない。

```powershell
[System.Threading.Thread]::CurrentThread.CurrentUICulture =
    [System.Globalization.CultureInfo]::GetCultureInfo('ko-KR')
Import-Module <モジュール>     # ← この順序で
```

内部的には、5 言語の MAML がずれないよう、
**本文を伏せた骨格が 5 言語で完全に一致すること**をテストで機械的に固定してある。
「翻訳を人手で保守すると必ずずれる」問題への対処として、記事のネタになるかもしれない。

---

## 8. 記事に必ず入れたい注意事項

書かないと読者がハマる。

| 注意 | 内容 |
|---|---|
| 書き込みで裸の `utf8` は使えない | `utf8NoBOM` / `utf8BOM` を明示する（4.3 節） |
| `-Encoding` の入力形式で改行の決まり方が変わる | `EncodingInformation` を渡したときだけ改行も継承される。語彙名には改行の情報が無いため、`-LineBreak` 省略時は OS 既定になる |
| BOM 無し UTF-16 / UTF-32 は非推奨 | 語彙としては用意しているが、他ツールから確実に判別できない。ラウンドトリップのために用意しているもの |
| `Encoding.GetEncoding(65001)` を渡すと BOM が付く | `System.Text.Encoding` インスタンスを直接渡した場合だけ、そのインスタンスの `GetPreamble()` を尊重するため |
| `.ps1` は UTF-8 (BOM 付き) で保存する | **PowerShell 5.1 は BOM 無しの `.ps1` を ANSI として読む**。日本語を含む行がパースエラーになる。本資料の採取中にも実際に踏んだ |
| 判定できても .NET が扱えない文字エンコーディングがある | ISO-2022-TW（50229）など。`CodePageNotAvailable` の非終了エラーで報告し、`-Encoding` での明示指定を案内する |

---

## 9. 内部の話（記事に厚みを出したいとき用）

読者層によっては不要。書くなら「なぜそうしたか」まで書けると価値が出る。

| 話題 | 内容 |
|---|---|
| 判定はファイル全体で行う | 先頭の一定量に制限すると、**英数字が続いたあとにマルチバイト文字が現れるソースファイル**を誤判定する。実装途中で 1 MiB 制限を入れていたが撤廃した |
| BOM は `GetPreamble()` に頼らない | `Encoding.GetEncoding("utf-8")` も `Encoding.UTF8` も **BOM 付きインスタンス**を返す。解決経路によって出力が変わってしまうため、BOM を出すかどうかは語彙側だけで決め、書き込み処理が自分で書き出している |
| 読み取り時の BOM 混入 | `StreamReader` に `detectEncodingFromByteOrderMarks: false` を渡すと、BOM が `U+FEFF` として **1 行目の先頭に混入する**。検出結果を強制しつつ BOM を確実に落とす処理を自前で持っている |
| `CodePagesEncodingProvider` | .NET Core では CP932 等がこれ無しでは取れない。ホスト側の登録に依存せず、`Import-Module` 時にモジュール自身が登録する |
| 検証のしかた | 同一のシナリオ集（223 件）を 5.1 と 7.x の両ホストで実行し、結果を突き合わせている。**「バージョンによらず同じ」がこのモジュールの存在意義そのもの**なので、そこをテストで固定している |
| 依存の非対称 | UTF.Unknown は net10.0 で 2.7.0、net48 で 2.6.0。2.7.0 の netstandard2.0 アセットが `System.Memory` に依存し、**`app.config` を差し込めない PS 5.1 ホスト**で解決できないため（詳細は引き継ぎメモ 2.8 節） |

---

## 10. ライセンスの記載（記事に書くなら正確に）

**UTF.Unknown は MIT ではない。** 過去に MIT と誤記して修正した経緯がある。

| 対象 | ライセンス |
|---|---|
| SnowStack.EncodingProbe 本体 | MIT |
| UTF.Unknown（依存） | **MPL 1.1**（または GPL 2.0+ / LGPL 2.1+ とのトリプルライセンス） |

正確な表記は `THIRD-PARTY-NOTICES.txt` にある。
コマンドからも `Resolve-Encoding -License` で同じ文面が取得できる。

---

## 11. 記事の構成案

素材を並べ替えただけの叩き台。

1. **掴み** — 同じ `Set-Content -Encoding UTF8` が 5.1 と 7.x で違うバイト列を書く（1 節の実験）
2. **なぜ 1.0.x では解けなかったか** — `PSEncodingName` / `UsePSName` の表（2 節）
3. **1.1.0 の答え** — 4 つのコマンドと統一語彙（3・4 節）
4. **実演** — Shift_JIS を名前で書く / 無損失ラウンドトリップ（5.1・5.2 節）
5. **外国語のファイルを扱う** — `-Culture` と `-Strategy`（5.3・5.4 節）
6. **追記の話** — バイト列で判定する整合性検査（6 節）
7. **注意事項** — 8 節の表をそのまま
8. **設計の話**（任意）— 9 節から拾う
9. **入手方法とライセンス** — 10 節

### 図表にすると効く箇所

- 1 節の「同じコマンド・違うバイト列」の対比表 … **記事の要**。目立たせる
- 4.2 の語彙表 … 5 系統 × BOM 有無のマトリクス
- 5.2 のラウンドトリップ … 加工前後のバイト列を色分けして `9A A0` の増加を示せると強い
- 5.3 の韓国語 … 「同じバイト列が 2 通りに読める」ことを図にできると、
  カルチャーが要る理由が一発で伝わる

### 書かないほうがよいこと

- コミットハッシュ、ブランチ名 … 記事の寿命より短い
- テストの件数 … 更新のたびにずれる。「両ホストで同一シナリオを突き合わせている」で足りる
- `publish/` の配置手順 … 開発者向けであって記事の読者向けではない

---

## 12. 素材の再採取

本書の実行結果を採り直す場合は、Release ビルドの DLL を両ホストで読み込んで実行する。

```powershell
dotnet build SnowStack.EncodingProbe.slnx -c Release

# PowerShell 7.x
pwsh -NoProfile -Command "Import-Module '.\SnowStack.EncodingProbe.PowerShell\bin\Release\net10.0\SnowStack.EncodingProbe.PowerShell.dll'; ..."

# Windows PowerShell 5.1
powershell.exe -NoProfile -Command "Import-Module '.\SnowStack.EncodingProbe.PowerShell\bin\Release\net48\SnowStack.EncodingProbe.PowerShell.dll'; ..."
```

バイト列の確認にはこれを使う。

```powershell
function Bytes($p) { (([IO.File]::ReadAllBytes($p)) | ForEach-Object { '{0:X2}' -f $_ }) -join ' ' }
```

**採取用のスクリプトを `.ps1` にする場合は UTF-8 (BOM 付き) で保存すること**（8 節）。
手元での確認手順の全体は `docs/EncodingProbe-1.1.0-動作確認手順書.md` にある。
