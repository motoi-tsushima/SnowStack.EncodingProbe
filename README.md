# SnowStack.EncodingProbe

このソリューションには以下の二つのプロジェクトが含まれています。

SnowStack.EncodingProbe : NuGetパッケージ

SnowStack.EncodingProbe.PowerShell : PowerShellコマンドレット



SnowStack.EncodingProbe パッケージは、文字エンコーディングの推測を行うクラスライブラリです。

SnowStack.EncodingProbe.PowerShellコマンドレットは、Resolve-Encoding というPowerShellコマンドレットを格納しています。Resolve-Encoding は EncodingProbeパッケージを使用して、文字エンコーディングの推測を行うコマンドレットです。

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



2026年7月8日に preview5 をリリースしました。

まだ、テスト中のため正式版はリリースしていません。今しばらくお待ちください。

### preview5 での変更点

- `Get-EncodingProbePlatformInfo` コマンドレットを新規追加しました。OS種別、.NETランタイム情報、ロケール・コードページ情報、PowerShellホスト情報（バージョン、Edition、`-Encoding` パラメータの対応状況）をまとめて取得できます。

- `EncodingInformation.EncodingName` を `EncodingInformation.EncodingWebName` にリネームしました（破壊的変更）。値自体は `.NET` の `Encoding.WebName` 相当（例: `"utf-8"`）であり、`.NET` の `Encoding.EncodingName`（表示名、例: `"Unicode (UTF-8)"`）とは異なるため、実体に合わせて命名を改めています。

- `EncodingInformation` から `IsWindowsOs` / `IsMacOs` / `IsLinuxOs` を削除しました。個々のファイルの判定結果とは無関係な実行環境情報のため、`Get-EncodingProbePlatformInfo` 側に統合しています。

- `EncodingInformation.Culture` を `string?`（Nullable Reference Types対応）に変更しました。

- `EncodingInformation.UsePSName` の判定基準（何を根拠に true/false とするか）をXMLドキュメントコメントに明記しました。

NuGet.org より SnowStack.EncodingProbe を公開しました。

以下の記事で使い方の解説を行っています。

[SnowStack.EncodingProbe NuGet Package 解説](https://snow-stack.net/encodingprobe_guide/)

また、PowerShell コマンドレットの解説は同ブログの以下の記事で解説しています。

[SnowStack.EncodingProbe.PowerShell 解説](https://snow-stack.net/encodingprobe_powershell_guide/)



