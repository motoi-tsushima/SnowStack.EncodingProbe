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

- 稼働しているOSの種類

- コマンドが認識しているカルチャー情報の表示

  

SnowStack.EncodingProbe パッケージは、内部で UTF.Unknown を使用しています。

EncodingProbe の独自文字エンコーディング推測処理で、英語・日本語・韓国語・繁体字中国語・簡体字中国語の推測を行い、それらの推測でわからなかった場合は、 UTF.Unknown に文字エンコーディング推測を任せます。

UTF.Unknown は欧米などのシングルバイト文字エンコーディングの推測には優れていますが、東アジア漢字文化圏の旧マルチバイト文字エンコーディングの推測では、やや推測信頼性に劣る欠点があり、東アジア漢字文化圏の文字エンコーディングの推測処理だけを、EncodingProbe の独自文字エンコーディング推測処理で補っています。

両者の文字エンコーディングの推測処理を組み合わせることにより、世界中の文字エンコーディングの推測処理ょを可能にしています。



両者はそれぞれ NuGet.org で、公開する予定です。2026年6月6日現在では、まだリリースしていません。
しばらくお待ちください。

使い方の解説なども公開後に執筆する予定です。

