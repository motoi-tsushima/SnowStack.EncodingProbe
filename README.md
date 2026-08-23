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
```

`-Encoding` には、統一語彙名のほかに WebName（`shift_jis` など）、数値コードページ（`932` など）、`System.Text.Encoding` インスタンス、`Resolve-Encoding` の戻り値をそのまま渡せます。

使ううえで知っておく必要のある点がいくつかあります。

- 書き込みでは、BOM 方針の定まらない裸の `utf8` を受け付けません。`utf8NoBOM` または `utf8BOM` を指定してください
- `Add-ProbedContent` では BOM の指定が常に無視されます。また、追記できるかどうかは実際に書き出されるバイト列で判定します
- `-Encoding` の入力形式によって改行コードの決まり方が変わります

いずれも各コマンドの `Get-Help <コマンド名> -Full` に記載しています。ヘルプは英語と日本語を同梱しています。詳細は [CHANGELOG.md](CHANGELOG.md) を参照してください。

2026年7月14日に正式版 1.0.0 をリリースしました。

NuGet.org より SnowStack.EncodingProbe を公開しました。

以下の記事で使い方の解説を行っています。

[SnowStack.EncodingProbe NuGet Package 解説](https://snow-stack.net/encodingprobe_guide/)

また、PowerShell コマンドレットの解説は同ブログの以下の記事で解説しています。

[SnowStack.EncodingProbe.PowerShell 解説](https://snow-stack.net/encodingprobe_powershell_guide/)



解説記事は、現時点では、やや説明不足ですが、後日詳細な解説記事を書く予定です。

