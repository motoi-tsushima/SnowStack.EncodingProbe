# SnowStack.EncodingProbe

このソリューションには以下の二つのプロジェクトが含まれています。

SnowStack.EncodingProbe : NuGetパッケージ

SnowStack.EncodingProbe.PowerShell : PowerShellコマンドレット



SnowStack.EncodingProbe パッケージは、文字エンコーディングの推測を行うクラスライブラリです。

SnowStack.EncodingProbe.PowerShellコマンドレットは、Resolve-Encoding というPowerShellコマンドを格納しています。Resolve-Encoding は EncodingProbeパッケージを使用して、文字エンコーディングの推測を行うコマンドです。

実行環境情報は、Get-EncodingProbePlatformInfo コマンドによって取得できます。

EncodingProbe の主な機能は以下の様になります。

- 文字エンコーディングの推測

- BOMの有無の確認

- 改行コードの種類の確認

- コマンドが認識しているカルチャー情報の表示

- 実行環境情報（OS種別 / .NETランタイム / ロケール / PowerShellホスト）の取得

  

SnowStack.EncodingProbe パッケージは、内部で UTF.Unknown を使用しています。

EncodingProbe の独自文字エンコーディング推測処理で、英語・日本語・韓国語・繁体字中国語・簡体字中国語の推測を行い、それらの推測でわからなかった場合は、 UTF.Unknown に文字エンコーディング推測を任せます。

UTF.Unknown は欧米などのシングルバイト文字エンコーディングの推測には優れていますが、東アジア漢字文化圏の旧マルチバイト文字エンコーディングの推測では、やや推測信頼性に劣る欠点があり、東アジア漢字文化圏の文字エンコーディングの推測処理だけを、EncodingProbe の独自文字エンコーディング推測処理で補っています。

両者の文字エンコーディングの推測処理を組み合わせることにより、世界中の文字エンコーディングの推測処理ょを可能にしています。

### シングルバイト系の判定の限界

シングルバイト系の文字エンコーディング（windows-1252、ISO-8859 など）は、バイト列の構造からは判別できず、文章の統計で推定しています。
そのため、判定の成否は長さよりも内容に左右されます。文字の一覧、記号・数字の多い行、その言語で通常使わない文字を含むテキストでは、
数百バイトあっても判定できない（`CodePage = -1`）ことがあります。

その場合は、コードページを明示してください。PowerShell モジュールでは、読み書きのコマンドの `-Encoding`
（`Convert-ProbedContent` では `-SourceEncoding`）で指定します。`Resolve-Encoding` には明示する手段がありません。



## version 1.2.0 の変更点

### 東アジア以外の言語への対応（クラスライブラリ・PowerShell モジュール共通）

- **東アジア以外のカルチャーでは、旧マルチバイト（Shift_JIS / EUC / GB / Big5 など）の判定を行わず、UTF.Unknown に任せます。**
- **東アジアのカルチャーでも、欧米のシングルバイトのテキストを誤判定しなくなりました。**
  たとえば日本語環境で windows-1252 のドイツ語を読むと、従来は Shift_JIS と判定していました。
  既定の判定方式（`Combined`）では、独自判定が旧マルチバイトを返したときに UTF.Unknown の結果と突き合わせ、
  UTF.Unknown が十分な信頼度でシングルバイトと判定していればそちらを採用します
- **UTF-8 の判定を RFC 3629 に厳密化しました。** 欧米のシングルバイトを UTF-8 と誤判定しなくなりました
- **繁体字と簡体字の取り違えを直しました。** 台湾・香港の環境で GBK を Big5 と、大陸の環境で Big5 を GB18030 と判定することがありました
- **香港・マカオ・広東語のカルチャーに対応しました。** `zh-Hant-HK` のような用字付きの名前や `zh_HK` も解釈します。
  香港・マカオのメッセージとヘルプは、台湾と同じ繁体字中国語です
- ルーマニア語（ISO-8859-16）などを判定すると `NullReferenceException` が発生する不具合を直しました（1.1.0 から存在）
- PowerShell 7.x で `PSEncodingName` に `I do not know.` が入る不具合を直しました（null になります）

公開 API と、日本語・韓国語・中国語のテキストの判定結果は変わっていません。

### 追加したコマンド（PowerShell モジュール）

| コマンド | 役割 |
|---|---|
| `Out-ProbedFile` | オブジェクトを整形し、文字エンコーディング・BOM・改行を指定してファイルに書き出す（`Out-File` の代わり） |
| `Convert-ProbedContent` | 既存のテキストファイルの文字エンコーディング・BOM・改行を変換する |

```powershell
# BOM 無しの UTF-8 で書き出す（Out-File と同じ使い方。PowerShell 5.1 でも同じバイト列になる）
Get-Process | Out-ProbedFile .\processes.txt

# 追記先の文字エンコーディングを保ったまま追記する
Get-Date | Out-ProbedFile .\log.txt -Append

# 変換元の文字エンコーディングを判定し、BOM 無しの UTF-8、改行 LF に変換する
Convert-ProbedContent .\*.txt -Encoding utf8NoBOM -LineBreak Lf

# BOM だけを取り除く
Convert-ProbedContent .\a.txt -Bom Remove
```

`Convert-ProbedContent` は、変換元に不正なバイト列があるファイルや、変換先で表現できない文字を含むファイルを変換しません（文字を失いません）。
`-WhatIf` / `-Confirm` / `-PassThru` に対応しています。

このほか、`Set-ProbedContent` / `Add-ProbedContent` の `-Force` で外した読み取り専用属性を、書き込み後に元に戻すようにしました。
詳細は [CHANGELOG.md](CHANGELOG.md) を参照してください。



## version 1.1.0 で追加したコマンド（PowerShell モジュール）

PowerShell モジュールに、テキストファイルの読み書きコマンドを追加しました。既存のコマンドと公開 API に変更はありません。

| コマンド | 役割 |
|---|---|
| `Get-ProbedContent` | 文字エンコーディングを判定してテキストファイルを読み込む |
| `Set-ProbedContent` | 文字エンコーディング・BOM・改行コードを明示して書き込む |
| `Add-ProbedContent` | 文字エンコーディングを保ったまま追記する |
| `ConvertTo-DotNetEncoding` | 各種の指定を `System.Text.Encoding` に変換する |

これらの `-Encoding` は、PowerShell 5.1 と 7.x で共通の名前（統一語彙）を受け付けます。同じ名前が同じ結果になるため、**PowerShell 5.1 でも BOM 無しの UTF-8 や Shift_JIS を名前で指定できます**。どちらも 5.1 の標準コマンドではできないことです。

```powershell
# 判定して読み込む（BOM は常に読み飛ばす）
Get-ProbedContent .\shift-jis.txt

# BOM 無しの UTF-8 で書き込む。PowerShell 5.1 でも同じ結果になる
Set-ProbedContent .\out.txt -Value $lines -Encoding utf8NoBOM

# 元のファイルの文字エンコーディング・BOM・改行コードを保ったまま書き戻す
$text = Get-ProbedContent .\a.txt -Raw
$text -replace 'foo', 'bar' | Set-ProbedContent .\a.txt -EncodingFrom .\a.txt -NoNewline

# 文字エンコーディングを保ったまま追記する
Add-ProbedContent .\log.txt -Value $line

# 日本語環境から韓国語のファイルを読む（カルチャーを指定しないと EUC-JP と誤判定される）
Get-ProbedContent .\korean.txt -Culture ko-KR

# 欧米のテキストは UTF.Unknown の判定に任せる
Get-ProbedContent .\german.txt -Strategy UtfUnknownOnly
```

`-Encoding` には、統一語彙名のほかに WebName（`shift_jis` など）、数値コードページ（`932` など）、`System.Text.Encoding` インスタンス、`Resolve-Encoding` の戻り値をそのまま渡せます。

使ううえで知っておく必要のある点がいくつかあります。

- 書き込みでは、BOM 方針の定まらない裸の `utf8` を受け付けません。`utf8NoBOM` または `utf8BOM` を指定してください
- `Add-ProbedContent` では BOM の指定が常に無視されます。また、追記できるかどうかは実際に書き出されるバイト列で判定します
- `-Encoding` の入力形式によって改行コードの決まり方が変わります

`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` は、`Resolve-Encoding` と同じ `-Culture` と `-Strategy` を受け取ります。バイト列だけでは区別できない組み合わせ（EUC-KR と CP949、EUC-JP と Shift-JIS など）はカルチャーで曖昧解消するため、**日本語環境で韓国語や中国語のファイルを扱うときは `-Culture` に対象言語のカルチャーを指定してください**。`-EncodingFrom` の参照ファイルや、`-Encoding` を省略したときの継承にも効きます。

いずれも各コマンドの `Get-Help <コマンド名> -Full` に記載しています。ヘルプは英語・日本語・韓国語・繁体字中国語・簡体字中国語の 5 言語を同梱しています。詳細は [CHANGELOG.md](CHANGELOG.md) を参照してください。

2026年7月14日に正式版 1.0.0 をリリースしました。

NuGet.org より SnowStack.EncodingProbe を公開しました。

2026年9月30日に 1.2.0 をリリースしました（クラスライブラリ・PowerShell モジュールとも）。

以下の記事で使い方の解説を行っています。

[SnowStack.EncodingProbe NuGet Package 解説](https://snow-stack.net/encodingprobe_guide/)

また、PowerShell コマンドレットの解説は同ブログの以下の記事で解説しています。

[SnowStack.EncodingProbe.PowerShell 解説](https://snow-stack.net/encodingprobe_powershell_guide/)



解説記事は、現時点では、やや説明不足ですが、後日詳細な解説記事を書く予定です。

