# SnowStack.EncodingProbe.PowerShell 1.1.0 動作確認手順書

- 対象: 1.1.0（`feature/1.1.0-probed-content` ブランチ）
- 作成日: 2026-08-23
- 目的: 手元の PC で 1.1.0 の追加機能が期待どおり動くことを確認する

この手順書のコマンドは、Windows 11 / .NET SDK 10.0.400 / PowerShell 7.6.5 / Windows PowerShell 5.1.26100
の環境で実際に実行し、記載の結果が出ることを確認しています。

---

## 0. 前提

| 必要なもの | 確認コマンド | 備考 |
|---|---|---|
| .NET SDK 10 以降 | `dotnet --version` | ビルドに必要 |
| PowerShell 7.x | `pwsh -NoProfile -c '$PSVersionTable.PSVersion'` | net10.0 側の確認に使う |
| Windows PowerShell 5.1 | `powershell.exe -NoProfile -c '$PSVersionTable.PSVersion'` | net48 側の確認に使う。**1.1.0 の要点はここ** |

**両方のホストで確認してください。** 「PowerShell のバージョンによらず同じ結果になること」が
このモジュールの存在意義であり、片方だけでは検証になりません。

### クローン先は浅いパスに置くこと

テストプロジェクトが参照する `Microsoft.PowerShell.SDK` は、深い階層のファイルを
ビルド出力へコピーします。クローン先が深いと Windows のパス長上限（260 文字）に当たり、
**テストプロジェクトのビルドだけが `MSB3021` で失敗します**。

```
error MSB3021: ... は OS のパスの上限を越えています。
完全修飾のファイル名は 260 文字以下にする必要があります。
```

`C:\src\SnowStack.EncodingProbe` のような浅いパスに置けば起きません。
製品側のプロジェクトは深いパスでもビルドできるため、この症状が出るのはテスト実行時だけです。

---

## 1. 取得

すでに手元にクローンがある場合は `git pull` で構いません。

```powershell
git clone -b feature/1.1.0-probed-content https://github.com/motoi-tsushima/SnowStack.EncodingProbe.git
cd SnowStack.EncodingProbe
```

`publish/` フォルダーには `.psd1` / `.psm1` / `deps.json` しか入っていません。
DLL と ヘルプは `.gitignore` で除外されているため、**次のビルド手順が必須です。**

---

## 2. ビルド

```powershell
dotnet build SnowStack.EncodingProbe.slnx -c Release
```

`0 エラー` になれば成功です。警告はテストプロジェクトのものが 90 件ほど出ますが、
1.1.0 で追加したコードの警告ではありません。

---

## 3. 自動テスト

```powershell
dotnet test SnowStack.EncodingProbe.slnx
```

期待する結果は次のとおりです。

```
成功!  - 失敗: 0、合格:  64 ... EncodingProbe.Tests.dll (net10.0)
成功!  - 失敗: 0、合格: 380 ... EncodingProbe.PowerShell.Tests.dll (net10.0)
成功!  - 失敗: 0、合格:  74 ... EncodingProbe.Tests.dll (net48)
```

続いて、PowerShell 5.1 と 7.x の結果が一致することを検証します。**これが最も重要な確認です。**

```powershell
pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1
```

```
PowerShell 5.1 と 7.x の結果は完全に一致しました (195 シナリオ)。
```

`What if:` の行が 2 行ほど流れますが、これは `-WhatIf` のシナリオが出しているもので異常ではありません。

---

## 4. モジュールの読み込み

用途に応じて 2 通りあります。

### 方法 A: ビルド出力を直接読む（手軽）

ホストごとに TFM が違う点に注意してください。

```powershell
# PowerShell 7.x
Import-Module (Resolve-Path .\SnowStack.EncodingProbe.PowerShell\bin\Release\net10.0\SnowStack.EncodingProbe.PowerShell.dll)

# Windows PowerShell 5.1
Import-Module (Resolve-Path .\SnowStack.EncodingProbe.PowerShell\bin\Release\net48\SnowStack.EncodingProbe.PowerShell.dll)
```

### 方法 B: 配布物の形で読む（推奨）

`.psd1` / `.psm1` / エディション自動切り替え / ヘルプまで含めて確認できます。
まず `publish/` へ配置します。

```powershell
New-Item -ItemType Directory -Force -Path `
    .\publish\SnowStack.EncodingProbe.PowerShell\core, `
    .\publish\SnowStack.EncodingProbe.PowerShell\desktop | Out-Null

Copy-Item .\SnowStack.EncodingProbe.PowerShell\bin\Release\net10.0\* `
          .\publish\SnowStack.EncodingProbe.PowerShell\core\    -Recurse -Force
Copy-Item .\SnowStack.EncodingProbe.PowerShell\bin\Release\net48\* `
          .\publish\SnowStack.EncodingProbe.PowerShell\desktop\ -Recurse -Force
```

以後は、**どちらのホストでも同じパス**を読み込めます（`.psm1` が `$PSEdition` を見て
`core\` と `desktop\` を切り替えます）。

```powershell
Import-Module (Resolve-Path .\publish\SnowStack.EncodingProbe.PowerShell\SnowStack.EncodingProbe.PowerShell.psd1)
```

読み込めたことを確認します。

```powershell
Get-Module SnowStack.EncodingProbe.PowerShell | Select-Object Version
Get-Command -Module SnowStack.EncodingProbe.PowerShell | Sort-Object Name
```

| 期待 | |
|---|---|
| Version | `1.1.0` |
| コマンド | `Add-ProbedContent` / `ConvertTo-DotNetEncoding` / `Get-EncodingProbePlatformInfo` / `Get-ProbedContent` / `Resolve-Encoding` / `Set-ProbedContent` の 6 個 |

---

## 5. 手動での動作確認

以降は **PowerShell 7.x と Windows PowerShell 5.1 の両方**で実施し、
結果が一致することを確認してください。

### 準備

作業用のフォルダーとバイト列表示のヘルパーを用意します。

```powershell
$work = Join-Path $env:TEMP ('probe_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
Set-Location $work

function Show-Bytes {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '(ファイルなし)' }
    # [IO.File] は PowerShell のカレント位置を見ないため、Convert-Path で絶対パスにする
    (([IO.File]::ReadAllBytes((Convert-Path -LiteralPath $Path))) |
        ForEach-Object { $_.ToString('X2') }) -join ' '
}
```

> **文字エンコーディングの確認は、必ずバイト列で行ってください。**
> 画面に表示された文字が正しく見えても、BOM の有無や改行コードは分かりません。

---

### 5.1 BOM 無しの UTF-8 を書ける（1.1.0 の最重要ポイント）

```powershell
Set-ProbedContent .\a.txt -Value 'あ' -Encoding utf8NoBOM -LineBreak Lf
Show-Bytes .\a.txt

Set-Content .\std.txt -Value 'あ' -Encoding UTF8     # 比較用: 標準コマンド
Show-Bytes .\std.txt
```

| | PowerShell 7.x | Windows PowerShell 5.1 |
|---|---|---|
| `Set-ProbedContent -Encoding utf8NoBOM` | `E3 81 82 0A` | `E3 81 82 0A` |
| 標準 `Set-Content -Encoding UTF8` | `E3 81 82 0D 0A` | `EF BB BF E3 81 82 0D 0A` |

**確認点:** 本モジュールは両ホストで同じバイト列になります。
標準コマンドは 5.1 で BOM（`EF BB BF`）が付き、7.x では付きません。この差を解消するのが 1.1.0 の目的です。

---

### 5.2 判定して読める / BOM が本文に混入しない

```powershell
[IO.File]::WriteAllBytes("$work\sjis.txt",
    [System.Text.Encoding]::GetEncoding(932).GetBytes("日本語`r`n"))

Resolve-Encoding .\sjis.txt | Format-List CodePage, EncodingWebName, Bom, LineBreak
Get-ProbedContent .\sjis.txt
```

| 期待 | |
|---|---|
| `CodePage` | `932` |
| `Get-ProbedContent` | `日本語` |

BOM が本文に混入しないことを、文字コード単位で確認します。

```powershell
[IO.File]::WriteAllBytes("$work\bom.txt", [byte[]](0xEF,0xBB,0xBF,0x41,0x42,0x0D,0x0A))
$line = @(Get-ProbedContent .\bom.txt)[0]
(([int[]][char[]]$line) | ForEach-Object { 'U+{0:X4}' -f $_ }) -join ' '
```

| 期待 | `U+0041 U+0042` |
|---|---|

**確認点:** 先頭に `U+FEFF` が付いていないこと。付いていたら BOM が混入しています。

> `@( )` で囲んでいるのは、出力が 1 行のときに `[0]` が
> **文字列の 1 文字目**を返してしまうためです。

---

### 5.3 無損失の往復（読んで加工して書き戻す）

```powershell
$src = "$work\rt.txt"
[IO.File]::WriteAllBytes($src,
    [System.Text.Encoding]::GetEncoding(932).GetBytes("日本語`r`nABC`r`n"))

$text = Get-ProbedContent $src -Raw
Set-ProbedContent .\rt_out.txt -Value $text -EncodingFrom $src -NoNewline

Show-Bytes $src
Show-Bytes .\rt_out.txt
```

| 期待 | 両方とも `93 FA 96 7B 8C EA 0D 0A 41 42 43 0D 0A` |
|---|---|

**確認点:** Shift_JIS のまま、CR-LF のまま、末尾の改行の有無まで含めて一致すること。
`-Raw` と `-NoNewline` の組み合わせが、無損失な往復に必要です。

---

### 5.4 追記は文字エンコーディングを壊さない

```powershell
[IO.File]::WriteAllBytes("$work\log.txt",
    (New-Object System.Text.UTF8Encoding($false)).GetBytes("日本語`n"))

# ASCII だけなら ascii で追記してもバイト列は UTF-8 と同じ → 許可される
Add-ProbedContent .\log.txt -Value 'ABC' -Encoding ascii -LineBreak Lf
Show-Bytes .\log.txt

# 日本語を Shift_JIS で追記するとバイト列が変わる → 拒否される
Add-ProbedContent .\log.txt -Value 'あ' -Encoding shift_jis

# -Force では回避できない（回避するのは -AllowEncodingChange）
Add-ProbedContent .\log.txt -Value 'あ' -Encoding shift_jis -Force
```

| 期待 | |
|---|---|
| ASCII 追記後 | `E6 97 A5 E6 9C AC E8 AA 9E 0A 41 42 43 0A` |
| 日本語 + `shift_jis` | エラー `EncodingChangeOnAppend` |
| `-Force` 付き | 同じくエラー（**回避できないことが正しい**） |

**確認点:** 許可・拒否がエンコーディング名ではなく、実際のバイト列で決まっていること。

---

### 5.5 書き込みで裸の `utf8` は拒否される

```powershell
Set-ProbedContent .\z.txt -Value 'x' -Encoding utf8
Test-Path .\z.txt
```

| 期待 | |
|---|---|
| エラー | `ParameterArgumentTransformationError`。`utf8NoBOM` か `utf8BOM` を使うよう案内される |
| `Test-Path` | `False` |

**確認点:** **ファイルが作られていないこと。** パラメータ束縛の段階で失敗しているため、
書きかけの壊れたファイルが残りません。

---

### 5.6 同じファイルの読み書きは拒否される

```powershell
Get-ProbedContent .\sjis.txt | Set-ProbedContent .\sjis.txt
Show-Bytes .\sjis.txt
```

| 期待 | |
|---|---|
| エラー | `SamePathRoundTrip` |
| ファイル | `93 FA 96 7B 8C EA 0D 0A`（**無傷**） |

**確認点:** 読み終える前に切り詰められて内容が消えていないこと。
変数にいったん受ける書き方（5.3）を案内するメッセージが出ます。

---

### 5.7 判定はファイル全体を対象とする

先頭が英数字だけで、後方にだけ日本語があるファイルでも誤判定しないことを確認します。

```powershell
$sb = New-Object System.Text.StringBuilder
1..60000 | ForEach-Object { $null = $sb.Append("// ASCII only source line`r`n") }
$null = $sb.Append("日本語のコメント`r`n")
[IO.File]::WriteAllBytes("$work\tail.txt",
    [System.Text.Encoding]::GetEncoding(932).GetBytes($sb.ToString()))

(Resolve-Encoding .\tail.txt).CodePage
@(Get-ProbedContent .\tail.txt)[-1]
```

| 期待 | |
|---|---|
| ファイルサイズ | 約 1.6 MB |
| `CodePage` | `932` |
| 最終行 | `日本語のコメント` |

**確認点:** 先頭 1.6 MB のうち最後の 1 行以外がすべて ASCII でも、Shift_JIS と判定されること。
文字化けしていたら、判定がファイルの先頭だけを見ています。

---

### 5.8 ヘルプ

```powershell
Get-Help Set-ProbedContent
Get-Help Add-ProbedContent -Full
Get-Help Get-ProbedContent -Examples
```

**確認点:**

- 日本語環境なら日本語、それ以外なら英語のヘルプが出ること
- `Add-ProbedContent` の NOTES に「BOM 指定は無視される」「`-Force` では整合性検査を回避できない」が載っていること
- 構文にスイッチが `[-Force]` の形（`[-Force <SwitchParameter>]` ではない）で出ること

英語のヘルプを見たい場合は、新しいセッションで次を実行してから読み込んでください。

```powershell
[System.Threading.Thread]::CurrentThread.CurrentUICulture =
    [System.Globalization.CultureInfo]::GetCultureInfo('en-US')
```

---

### 5.9 エラーメッセージの言語

対応言語は英語・日本語・韓国語・繁体字中国語・簡体字中国語です。
UI カルチャーを変えて、同じ操作のメッセージが切り替わることを確認できます。

```powershell
foreach ($culture in 'en-US','ja-JP','ko-KR','zh-TW','zh-CN') {
    [System.Threading.Thread]::CurrentThread.CurrentUICulture =
        [System.Globalization.CultureInfo]::GetCultureInfo($culture)
    "--- $culture ---"
    try { Set-ProbedContent .\z.txt -Value 'x' -Encoding utf8 -ErrorAction Stop }
    catch { $_.Exception.InnerException.Message }
}
```

**確認点:** 5 言語すべてでメッセージが変わること。未対応の言語（`fr-FR` など）は英語になります。

> 日本語環境のコンソールでは、韓国語が `????` のように表示されることがあります。
> これはコンソールのコードページにその文字が無いための**表示上の問題**で、
> メッセージ自体は正しく切り替わっています。
> 文字列として確認したい場合は
> `$_.Exception.InnerException.Message | Set-ProbedContent .\msg.txt -Encoding utf8BOM`
> のようにファイルへ書き出し、エディターで開いてください。

---

### 後片付け

```powershell
Set-Location $env:TEMP
Remove-Item -Recurse -Force $work
```

---

## 6. うまくいかないときの確認ポイント

| 症状 | 原因と対処 |
|---|---|
| テストのビルドが `MSB3021` で失敗する | クローン先のパスが深すぎます。`C:\src\...` のような浅いパスに置き直してください（0 節） |
| `Import-Module` で「モジュールが見つかりません」 | 相対パスは PowerShell がモジュール検索パスとして解釈します。`Resolve-Path` で絶対パスにしてください |
| `publish\...` の `Import-Module` が DLL 不明で失敗する | `publish/` の DLL は Git 管理外です。4 節の方法 B のコピーを実行してください |
| `Get-Help` に説明が出ない | ヘルプは DLL と同じ場所の `en-US\` `ja-JP\` を見ます。`bin\<TFM>\` からコピーされているか確認してください |
| 5.1 で日本語が化ける | ファイルの中身ではなくコンソールの表示の問題である場合があります。`Show-Bytes` で**バイト列を確認**してください |
| `sample_eucjp.txt` 関連のテストが落ちる | TestData の改行が CRLF に変換されています。`.gitattributes` の `tests/EncodingProbe.Tests/TestData/** -text` が効いているか（`git check-attr text -- <ファイル>` が `unset`）を確認し、`git checkout` し直してください |

---

## 7. 確認できたら

この手順書で確認しているのは 1.1.0 のブランチの内容です。
1.1.0 のリリースには、このあと次の作業が残っています（いずれも未実施）。

1. `master` へのマージ（または Pull Request の作成）
2. `v1.1.0` タグの作成
3. NuGet / PowerShell Gallery への公開

現在の Git の状態は `docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md` の
「Git の状態」の節に記録しています。
