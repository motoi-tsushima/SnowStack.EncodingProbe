# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要

文字エンコーディング推測ライブラリと、それを使う PowerShell モジュールの 2 プロジェクト構成。
コード内コメント・XML ドキュメント・コミットメッセージはすべて日本語で書かれている。既存のスタイルに合わせること。

- `SnowStack.EncodingProbe` … NuGet パッケージ（クラスライブラリ）
- `SnowStack.EncodingProbe.PowerShell` … バイナリモジュール。`Resolve-Encoding` /
  `Get-EncodingProbePlatformInfo` / `Get-ProbedContent` / `Set-ProbedContent` /
  `Add-ProbedContent` / `ConvertTo-DotNetEncoding` を提供する
- `tests/EncodingProbe.Tests` … コアライブラリの xUnit テスト（net10.0 / net48 の両方で動く）
- `tests/EncodingProbe.PowerShell.Tests` … コマンドレットの xUnit テスト（net10.0 のみ。`Microsoft.PowerShell.SDK` を参照）

ソリューションファイルは `.slnx` 形式（`SnowStack.EncodingProbe.slnx`）。

## ビルドとテスト

```bash
dotnet build SnowStack.EncodingProbe.slnx -c Debug

# 全テスト（両 TFM）
dotnet test SnowStack.EncodingProbe.slnx

# TFM を指定して実行（両方通すこと。詳細は「マルチターゲット」を参照）
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net48
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net10.0

# 単一テストの実行
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net10.0 \
  --filter "FullyQualifiedName~JapaneseEncodingTests"
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net10.0 \
  --filter "FullyQualifiedName=EncodingProbe.Tests.DetectorTests.PS51EncodingNameTests.NoBom_Utf8_PSEncodingNameIsNull"

# NuGet パッケージ生成（GeneratePackageOnBuild は false なので明示的に pack する）
dotnet pack SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj -c Release
```

### 実 PowerShell ホストでの手動確認

ビルド済み DLL を直接 Import-Module する。PS 5.1（Desktop）は net48 出力、PS 7.x（Core）は net10.0 出力を読ませる。

```bash
pwsh -NoProfile -Command "Import-Module './SnowStack.EncodingProbe.PowerShell/bin/Debug/net10.0/SnowStack.EncodingProbe.PowerShell.dll'; Resolve-Encoding -Path <file> | Format-List *"
powershell.exe -NoProfile -Command "Import-Module './SnowStack.EncodingProbe.PowerShell/bin/Debug/net48/SnowStack.EncodingProbe.PowerShell.dll'; Resolve-Encoding -Path <file> | Format-List *"
```

Visual Studio 用には `Properties/launchSettings.json` に「.NET 4.8 テスト」「.NET 10 テスト」の 2 プロファイルがある。
`Configurations` に `PS5.1_Debug` / `PS7.x_Debug` が定義されているが、これらは VS のデバッグ対象切り替え用で、ビルド内容は Debug と変わらない。

## アーキテクチャ

### 判定パイプライン

`EncodingProbe`（静的ファサード）→ `EncodingDetector`（独自判定エンジン、約 2200 行）→ UTF.Unknown（フォールバック）という 3 層。

`EncodingProbe.Detect(byte[] | Stream | string)` は `EncodingDetectorOptions.Strategy` で振る舞いが決まる：

- `Combined`（既定）… まず独自判定。`CodePage < 0`（＝判定不能）のときだけ UTF.Unknown に委譲する
- `NativeOnly` … 独自判定のみ
- `UtfUnknownOnly` … UTF.Unknown のみ

分業の理由は README のとおり。独自判定は東アジア漢字文化圏のマルチバイト（Shift-JIS / EUC-JP / EUC-KR / CP949 / GB 系 / Big5 / EUC-TW / ISO-2022-*）を担当し、欧米のシングルバイトは UTF.Unknown が担当する。

`EncodingDetector.Detection(culture)` 内の判定順序は意味を持つ。順序を入れ替えると誤判定する：

1. 改行コード判定（`DetectLineBreak`）— 後段の曖昧解消にも使う
2. BOM（`ByteOrderMarkDetection`）— 一致したら即確定
3. ISO-2022 / ASCII
4. UTF-32 → UTF-16 → UTF-8（UTF-32 を UTF-16 より先に判定する）
5. カルチャーで分岐。簡体字中国語なら GB2312 → GBK → GB18030 の 3 段階判定。それ以外は EUC 系 → CPxxx 系

`DetectionMode.Skippable` を渡すと BOM が無い時点で打ち切る（BOM の有無だけ知りたい呼び出し向け）。

### カルチャー依存の曖昧解消

バイト列だけでは区別できない組み合わせがあり、カルチャーと改行コードで決める。ここが本ライブラリの中核ロジックなので、変更時は該当言語のテストデータを必ず追加すること。

- EUC-JP と Shift-JIS の両方に該当 → 改行が CRLF なら Shift-JIS、LF なら EUC-JP、改行なしなら OS（Windows → Shift-JIS）
- EUC-KR と CP949 の両方に該当 → CP949
- EUC-TW と CP950 の両方に該当 → CP950(Big5)

カルチャーはグローバル状態ではなくパラメータで渡す（`Detection(culture)` / `EncodingDetectorOptions.Culture`）。`CultureInfo.CurrentCulture` へのフォールバックは 1 か所だけ。

### マルチターゲット（net10.0 / net48）と PSEncodingName

両プロジェクトとも `net10.0;net48` をターゲットにする。net48 = Windows PowerShell 5.1（Desktop）向け、net10.0 = PowerShell 7.x（Core）向け。

`EncodingInformation.PSEncodingName` / `UsePSName` は **TFM ごとに意味が違う**。`EncodingDetector.cs` の `#if NETFRAMEWORK` 分岐で実装が分かれている：

- net10.0 ビルド … PS 6.2+ の登録済みフレンドリ名（`utf8BOM` 等）。無ければ WebName を入れ、`UsePSName = false`（`-Encoding` に直接渡せない）
- net48 ビルド … PS 5.1 の固定 `-Encoding` 列挙値（`Ascii` / `Unicode` / `UTF32` 等）に一致する場合のみその値。一致しなければ `null` かつ `UsePSName = false`

この差異は仕様であってバグではない。`PS51EncodingNameTests` が両 TFM でこのマッピングを検証している。**片方の TFM だけでテストを通しても意味がない。**

net48 では `PolySharp` により新しい言語機能（record 等）を使えるようにしている。

### PowerShell モジュール層

- `EncodingProbeModuleInitializer`（`IModuleAssemblyInitializer`）が Import-Module 時に `CodePagesEncodingProvider` を登録する。.NET Core では CP932 等がこれ無しでは取れない。ホスト側の登録に依存しないこと
- コア側の `SnowStack.EncodingProbe.csproj` に `InternalsVisibleTo("SnowStack.EncodingProbe.PowerShell")` があり、`Internal/PlatformInfoResolver` 等を PowerShell 層だけが参照できる。公開 API を増やさずに内部実装を共有するための仕組み
- `EncodingProbePlatformInformation` はコアの `PlatformInformation`（OS / ランタイム / ロケール）に PowerShell ホスト情報を足したもの
- 配布用の `publish/SnowStack.EncodingProbe.PowerShell/` のうち、リポジトリで管理しているのは `.psd1` / `.psm1` / `deps.json` だけ。DLL と MAML ヘルプは `.gitignore` で除外されている（ビルド出力を手動でここへ配置する運用）。`.psm1` が `$PSEdition` を見て `core\` か `desktop\` の DLL を読み分ける

### 1.1.0 で追加した読み書きコマンド

`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `ConvertTo-DotNetEncoding` は
**統一語彙**（PS 5.1 と 7.x で同じ名前が同じ結果になるエンコーディング名の体系）の上に作られている。
仕様は `docs/EncodingProbe-1.1.0-仕様書.md`。

- `Internal/EncodingVocabulary` が語彙の解決を一手に担う。解決順序は意味を持つ
- `Internal/EncodingSpecTransformationAttribute`（`ArgumentTransformationAttribute` 派生）が
  **パラメータ束縛の段階**で `EncodingSpec` に変換・検証する。ファイルを開く前に失敗させることで、
  書きかけの破損ファイルを残さない。`EncodingSpec` は internal のまま公開しない
- 書き込み系は `Cmdlets/ProbedContentWriterCommandBase` を共有する。
  `Set-` と `Add-` の差は「ファイルの開き方」と「書き込む内容の検査」の 2 点だけ
- BOM を書き出すかどうかは `EncodingSpec.EmitBom` だけで決め、`ProbedFileWriter` が自分で書き出す。
  `Encoding.GetPreamble()` 任せにしない（解決経路によって出力が変わってしまうため）
- 文字エンコーディングの判定は**ファイル全体**を対象とする。先頭の一定量に制限すると、
  英数字が続いたあとにマルチバイト文字が現れるファイルを誤判定する
- `Add-ProbedContent` の整合性検査は**実際に書き出されるバイト列の比較**で行う。
  名前の組み合わせ表では判定しない（仕様書 6.1）
- メッセージは `Internal/MessageCatalog` が英語・日本語・韓国語・繁体字中国語・簡体字中国語で持つ。
  サテライトアセンブリではなく単一アセンブリ内の表。`Resolve-Encoding` の既存メッセージは英語のまま

### MAML ヘルプ

`SnowStack.EncodingProbe.PowerShell/en-US/` と `ja-JP/` に
`SnowStack.EncodingProbe.PowerShell.dll-Help.xml` を置いている（csproj で出力へコピーする）。
`Get-Help` はアセンブリと同じ場所のカルチャー別フォルダーを探すため、`publish/` へ配置する際は
`core\en-US\` `core\ja-JP\` `desktop\en-US\` `desktop\ja-JP\` の 4 か所へコピーする。
**2 言語の内容がずれないよう、片方だけ直さないこと。**

### テストデータ

`tests/EncodingProbe.Tests/TestData/<言語>/` に言語別・エンコーディング別のサンプルファイルがある（English / Japanese / Korean / Chinese_Simplified / Chinese_Traditional）。PowerShell テストプロジェクトは `Link` でこれを共有している。

**注意:** `.editorconfig` は `[*.txt]` に `charset = utf-8-bom` を指定している。TestData の .txt はまさにそれ以外のエンコーディングであることが試験の目的なので、エディタや整形ツールがこれらを書き換えないようにすること。テストデータを新規作成するときはバイト列を明示して生成する。

### PowerShell 5.1 / 7.x の一致検証

`tests/PSCompat/Invoke-ProbedCompatTests.ps1` が同一のシナリオ集を両ホストで実行し、結果を突き合わせる。
本モジュールの存在意義そのものを検証しているため、読み書き系に手を入れたら必ず実行すること。

```bash
pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1
```

シナリオは `tests/PSCompat/ProbedCompatScenarios.ps1` に追加する。注意点:

- `.ps1` は **UTF-8 (BOM 付き)** で保存する。PS 5.1 は BOM 無しの `.ps1` を ANSI として読むため、
  日本語を含む行がパースエラーになる
- レポートにホスト固有の情報（バージョン、パス、PowerShell 自身のエラー文言）を含めない
- ホストによって差が出るのが正しい値（`Encoding.Default` など）は、値そのものではなく
  「ランタイム既定と一致するか」を記録して比較する
- `[scriptblock]::Create` に組み立てた文字列を渡すと AMSI にブロックされることがある。
  ループ変数を束縛したいだけなら `.GetNewClosure()` を使う

## バージョン更新時に触る場所

バージョン番号は 3 か所に分散している。上げるときはすべて揃える：

1. `SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj`（`Version` / `AssemblyVersion` / `FileVersion`）
2. `SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.csproj`（同上）
3. `publish/SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.psd1`（`ModuleVersion`、`PrivateData.PSData.ReleaseNotes`）

`CHANGELOG.md` にも追記すること。

新しいコマンドレットを追加したら、次も忘れずに行う:

- `.psd1` の `CmdletsToExport` に追加する
- `en-US` と `ja-JP` の MAML ヘルプに項目を追加する
- `tests/EncodingProbe.PowerShell.Tests/Helpers/ProbedCommandRunspaceFixture.cs` に登録する
  （登録しないとテストのランスペースから呼べない）
- `tests/PSCompat/ProbedCompatScenarios.ps1` にシナリオを追加する

## ライセンス上の注意

UTF.Unknown は **MIT ではなく MPL 1.1**（または GPL 2.0+ / LGPL 2.1+ とのトリプルライセンス）。過去に MIT と誤記して修正したコミットがある。サードパーティ表記は `THIRD-PARTY-NOTICES.txt` と `EncodingProbe.cs` の `License` 定数（`Resolve-Encoding -License` が返す文字列）の 2 か所にあり、内容を揃えること（`LICENSE.txt` は本プロジェクト自身の MIT ライセンス）。

## 1.1.0 の作業記録

1.1.0（`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `ConvertTo-DotNetEncoding` の追加）
は完了している。関連文書:

- `docs/EncodingProbe-1.1.0-仕様書.md` … 機能仕様。挙動を確認するときはまずここを見る
- `docs/EncodingProbe-1.1.0-ClaudeCode指示書.md` … 実装時の制約
- `docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md` … 決定事項とその根拠、踏んだ落とし穴。
  **仕様書に書かれていない判断の理由はここにある**

1.1.0 では次を変更していない（指示書 1 節の制約。今後も維持すること）:

- `Resolve-Encoding` / `Get-EncodingProbePlatformInfo` のパラメータと戻り値
- `EncodingInformation` 型（`DotNetEncoding` プロパティは「追加しない」と決定済み）
- コアの NuGet パッケージの公開 API
