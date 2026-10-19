# EncodingProbe.PowerShell 1.3.0 — リリース手順

- 作成日: 2026-10-19
- 対象: PowerShell モジュール `SnowStack.EncodingProbe.PowerShell` 1.3.0
- **クラスライブラリ（NuGet パッケージ `SnowStack.EncodingProbe`）は 1.2.0 のまま公開しない。** モジュールはクラスライブラリ 1.2.0 の DLL を同梱する
- リリース前のチェック項目は `docs/EncodingProbe-1.3.0-動作確認手順書.md` 4 章にもある。本書は作業の順序をまとめたもの

---

## 1. 事前の確認

リポジトリの直下で実行する。

```powershell
dotnet build SnowStack.EncodingProbe.slnx -c Debug
dotnet test SnowStack.EncodingProbe.slnx                              # 両 TFM
pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1    # 5.1 と 7.x の一致
git diff master -- SnowStack.EncodingProbe/                          # 空であること（クラスライブラリは変更しない）
```

| 確認すること | 場所 |
|---|---|
| モジュールのバージョンが 1.3.0 | `SnowStack.EncodingProbe.PowerShell.csproj`（`Version` / `AssemblyVersion` / `FileVersion`）、`.psd1`（`ModuleVersion`、`ReleaseNotes`） |
| クラスライブラリは 1.2.0 のまま | `SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj` |
| `CmdletsToExport` に `Import-ProbedCsv` / `Export-ProbedCsv` がある | `.psd1` |

## 2. 配布フォルダーの作成

配布フォルダーは `publish/SnowStack.EncodingProbe.PowerShell/`。PowerShell 7.x 用（net10.0）を `core\` に、Windows PowerShell 5.1 用（net48）を `desktop\` に置く。
`.psm1` が `$PSEdition` を見て読み分ける。

```powershell
dotnet build SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.csproj -c Release

$out = 'SnowStack.EncodingProbe.PowerShell\bin\Release'
$dst = 'publish\SnowStack.EncodingProbe.PowerShell'
Copy-Item "$out\net10.0\*" "$dst\core\"    -Recurse -Force
Copy-Item "$out\net48\*"   "$dst\desktop\" -Recurse -Force

# MIT License（PowerShell 由来のコード）と MPL 1.1（UTF.Unknown）の表示の条件として同梱する
Copy-Item LICENSE.txt, THIRD-PARTY-NOTICES.txt $dst -Force
```

- ビルド出力をフォルダーごと写すと、DLL・PDB と MAML ヘルプ 7 フォルダー（`en-US` / `ja-JP` / `ko-KR` / `zh-TW` / `zh-CN` / `zh-HK` / `zh-MO`）が、
  `core\` と `desktop\` の下に入る（ヘルプは計 14 か所）
- `core\SnowStack.EncodingProbe.PowerShell.deps.json` は Git で管理している。バージョンを上げると中身（モジュールのバージョン）が変わるので、写した後にコミットする
- DLL・PDB・ヘルプは `.gitignore` で除外している。`LICENSE.txt` / `THIRD-PARTY-NOTICES.txt` も配布フォルダーでは Git で管理していない（リポジトリ直下のものが原本）

配布フォルダーの中身（1.3.0）:

| 場所 | ファイル |
|---|---|
| 直下 | `SnowStack.EncodingProbe.PowerShell.psd1`、`.psm1`、`LICENSE.txt`、`THIRD-PARTY-NOTICES.txt` |
| `core\` | `SnowStack.EncodingProbe.PowerShell.dll` / `.pdb` / `.deps.json`、`SnowStack.EncodingProbe.dll` / `.pdb`、`UtfUnknown.dll`（2.7.0）、`System.Management.Automation.dll`、ヘルプ 7 フォルダー |
| `desktop\` | `SnowStack.EncodingProbe.PowerShell.dll` / `.pdb`、`SnowStack.EncodingProbe.dll` / `.pdb`、`UtfUnknown.dll`（2.6.0）、`System.Management.Automation.dll`、`System.Runtime.CompilerServices.Unsafe.dll`、`System.Text.Encoding.CodePages.dll`、ヘルプ 7 フォルダー |

## 3. 配布フォルダーの確認

両ホストで、`.psd1` から読み込めることを確かめる。

```powershell
# PowerShell 7.x と Windows PowerShell 5.1 の両方で実行する
$m = Import-Module .\publish\SnowStack.EncodingProbe.PowerShell\SnowStack.EncodingProbe.PowerShell.psd1 -PassThru
$m.Version                                    # 1.3.0
(Get-Command Import-ProbedCsv).DLL            # 7.x は core\、5.1 は desktop\ の DLL
Resolve-Encoding -Version                     # SnowStack.EncodingProbe.PowerShell 1.3.0
Get-Command -Module SnowStack.EncodingProbe.PowerShell   # 10 個のコマンド
Get-Help Export-ProbedCsv                     # UI カルチャーの言語のヘルプ
Resolve-Encoding -License                     # PowerShell の MIT License の表示を含む
```

可能なら、クリーンな環境（Windows Sandbox など）にインストールして、動作確認手順書の 1〜3 章の主要な項目を確かめる。

## 4. 公開

1. `README.md` の末尾の告知に、1.3.0 のリリース日の行を足す（例: 「2026年○月○日に PowerShell モジュール 1.3.0 をリリースしました。」）
2. 作業ブランチ `feature/1.3.0-csv` を master にマージする
3. 配布フォルダーを、従来と同じ方法で公開する（PowerShell Gallery など）。NuGet パッケージ（クラスライブラリ）は公開しない
4. 完了した依頼文（`docs/EncodingProbe-1.3.0-依頼-第一次.md` など）は、リポジトリに入れていない。不要になったら手元から削除する
