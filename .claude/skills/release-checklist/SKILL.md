---
name: release-checklist
description: SnowStack.EncodingProbe のバージョン番号を上げるとき、または新しいコマンドレットを追加するときに使う。触るべきファイルの一覧（csproj・psd1・CHANGELOG・MAML ヘルプ 5 言語・テストの登録・PSCompat）
---

# バージョン更新時に触る場所

バージョン番号は 3 か所に分散している。上げるときはすべて揃える：

1. `SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj`（`Version` / `AssemblyVersion` / `FileVersion`）
2. `SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.csproj`（同上）
3. `publish/SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.psd1`（`ModuleVersion`、`PrivateData.PSData.ReleaseNotes`）

`CHANGELOG.md` にも追記すること。

**PowerShell モジュールだけをリリースする場合は、2 と 3 だけを上げ、1（クラスライブラリ）は据え置く。**
クラスライブラリに変更が無いのにバージョンを上げると、NuGet パッケージの版と中身が食い違うためである。
モジュールは、据え置いたクラスライブラリの DLL を同梱する。
（1.3.0 は当初この扱いだったが、第四次でクラスライブラリの判定処理を直したため、結果として 3 か所すべてを 1.3.0 にして両方をリリースする）

新しいコマンドレットを追加したら、次も忘れずに行う:

- `.psd1` の `CmdletsToExport` に追加する
- `tests/EncodingProbe.PowerShell.Tests/CmdletTests/MamlHelpTests.cs` の `CmdletTypes` に追加する
- MAML ヘルプ 5 言語（`en-US` / `ja-JP` / `ko-KR` / `zh-TW` / `zh-CN`）すべてに項目を追加し、
  `zh-TW` を `zh-HK` / `zh-MO` へコピーする
  （`MamlHelpTests` が骨格の一致と複製の一致を要求するため、1 言語だけ足すとテストが落ちる）
- `tests/EncodingProbe.PowerShell.Tests/Helpers/ProbedCommandRunspaceFixture.cs` に登録する
  （登録しないとテストのランスペースから呼べない）
- `tests/PSCompat/ProbedCompatScenarios.ps1` にシナリオを追加する
