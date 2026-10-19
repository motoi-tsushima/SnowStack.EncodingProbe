# EncodingProbe.PowerShell 1.3.0 動作確認手順書

- 作成日: 2026-10-15
- 対象: 利用者（人間）が、Claude Code の各段階の作業の後に、手元の PC で動作を確かめるための手順
- 仕様書: `docs/EncodingProbe-1.3.0-仕様書.md`

Claude Code の自動テストと PSCompat が通っていることを前提に、**実際に使う形で**確かめる項目だけを挙げる。
すべて Windows PowerShell 5.1 と PowerShell 7.x の両方で行う。

---

## 0. 準備

### 0.1 ビルドしたモジュールの読み込み

```powershell
# PowerShell 7.x
Import-Module .\SnowStack.EncodingProbe.PowerShell\bin\Debug\net10.0\SnowStack.EncodingProbe.PowerShell.dll

# Windows PowerShell 5.1
Import-Module .\SnowStack.EncodingProbe.PowerShell\bin\Debug\net48\SnowStack.EncodingProbe.PowerShell.dll
```

### 0.2 バイト列を表示する関数

```powershell
function Show-Bytes([string]$Path) {
    $b = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path))
    if ($b.Length -eq 0) { return '<0 bytes>' }
    ($b | ForEach-Object { $_.ToString('X2') }) -join ' '
}
```

### 0.3 作業フォルダー

```powershell
$t = Join-Path $env:TEMP 'ep130'
New-Item -ItemType Directory -Path $t -Force | Out-Null
Set-Location $t
```

---

## 1. 第一次の後 — `Resolve-Encoding -DetectionLineCount`

### 1.1 先頭が ASCII で、後半に日本語がある Shift_JIS のファイル

```powershell
$sj = [System.Text.Encoding]::GetEncoding(932)
$text = (1..100 | ForEach-Object { "line$_,abc" }) -join "`r`n"
$text += "`r`n日本語,あいう`r`n"
[System.IO.File]::WriteAllBytes("$t\head_ascii_sjis.csv", $sj.GetBytes($text))

Resolve-Encoding .\head_ascii_sjis.csv                          # 全体: 932 / shift_jis
Resolve-Encoding .\head_ascii_sjis.csv -DetectionLineCount 10   # 先頭 10 行: 20127 / us-ascii
Resolve-Encoding .\head_ascii_sjis.csv -DetectionLineCount 200  # 改行の数以上: 全体と同じ
```

| 確認 | 期待 |
|---|---|
| 全体の判定 | `CodePage : 932` |
| 先頭 10 行 | `CodePage : 20127`、`EncodingWebName : us-ascii` |
| 200 行 | 全体と同じ |

### 1.2 BOM 無しの UTF-16LE

```powershell
$u16 = New-Object System.Text.UnicodeEncoding($false, $false)
[System.IO.File]::WriteAllBytes("$t\u16.txt", $u16.GetBytes("日本語上`r`nabc`r`n"))
Resolve-Encoding .\u16.txt -DetectionLineCount 1
```

期待: `CodePage : 1200`。「上」（U+4E0A、バイト列 `0A 4E`）の `0A` を改行と取り違えて奇数の長さで切ると、UTF-16 と判定されなくなる。

```powershell
[System.IO.File]::WriteAllBytes("$t\u16_ascii.csv", $u16.GetBytes("A,B`r`n1,2`r`n3,4`r`n"))
Resolve-Encoding .\u16_ascii.csv -DetectionLineCount 1   # us-ascii（判定処理の既存の挙動。仕様書 3.2）
```

第二次の後に、`Import-ProbedCsv .\u16_ascii.csv -DetectionLineCount 1` が `A=1, B=2` / `A=3, B=4` を正しく読むことも確かめる（UTF-16LE と読み替える）。

### 1.3 エラーと表示

```powershell
Resolve-Encoding .\u16.txt -DetectionLineCount 0     # パラメータの検証エラー
Resolve-Encoding -License                            # 末尾に PowerShell（Microsoft）の表示がある
Get-Help Resolve-Encoding -Parameter DetectionLineCount
```

`Get-Help` は、UI カルチャーを ja-JP 以外（en-US など）に変えて Import-Module し直した状態でも確かめる（CLAUDE.md の手順）。

---

## 2. 第二次の後 — `Import-ProbedCsv`

### 2.1 文字エンコーディングの判定

```powershell
$csv = "名前,メモ`r`n佐藤,テスト`r`n"
[System.IO.File]::WriteAllBytes("$t\sjis.csv",  [System.Text.Encoding]::GetEncoding(932).GetBytes($csv))
[System.IO.File]::WriteAllBytes("$t\eucjp.csv", [System.Text.Encoding]::GetEncoding(51932).GetBytes($csv))
[System.IO.File]::WriteAllBytes("$t\utf8.csv",  (New-Object System.Text.UTF8Encoding($false)).GetBytes($csv))

Import-ProbedCsv .\sjis.csv
Import-ProbedCsv .\eucjp.csv
Import-ProbedCsv .\utf8.csv
Import-ProbedCsv .\*.csv          # ワイルドカード: 一致したファイルを順に読む
Get-ChildItem *.csv | Import-ProbedCsv   # 5.1 でも読める（標準の Import-Csv はエラー）
```

期待: どれも `名前 : 佐藤`、`メモ : テスト` が出る（ワイルドカードでは件数分）。

### 2.2 `-DetectionLineCount` と検証

```powershell
Import-ProbedCsv .\head_ascii_sjis.csv -DetectionLineCount 10
Import-ProbedCsv .\head_ascii_sjis.csv -DetectionLineCount 10 -Encoding shift_jis
```

| 確認 | 期待 |
|---|---|
| 1 行目のコマンド | **1 件も出力されずに** `DetectionVerificationFailed` の非終了エラー。メッセージにバイト位置と、`-DetectionLineCount` を増やすか `-Encoding` を指定する案内がある |
| 2 行目のコマンド | 100 件が出力される（1 行目の `line1,abc` はヘッダー） |

### 2.3 標準の 5.1 と違う点（7.x に合わせた点）

```powershell
[System.IO.File]::WriteAllBytes("$t\cr.csv", [byte[]](0x41,0x2C,0x42,0x0D,0x31,0x2C,0x32,0x0D,0x33,0x2C,0x34,0x0D))
Import-Csv .\cr.csv -Encoding UTF8      # 5.1 の標準: 何も出力されない
Import-ProbedCsv .\cr.csv               # 2 件出力される（両ホスト）
```

### 2.4 `-Culture` と `-UseCulture`

```powershell
Get-Help Import-ProbedCsv -Parameter Culture
Get-Help Import-ProbedCsv -Parameter UseCulture
```

期待: 2 つの説明が、それぞれ「判定のカルチャー」「区切り文字」であることと、違いが書かれている。

---

## 3. 第三次の後 — `Export-ProbedCsv`

### 3.1 既定の書き出し（5.1 で特に確認）

```powershell
[pscustomobject]@{ 名前 = '佐藤'; メモ = 'テスト' } | Export-ProbedCsv .\out.csv
Show-Bytes .\out.csv
```

期待: 両ホストで同じバイト列。BOM 無しの UTF-8、`#TYPE` 行なし（標準の 5.1 は `#TYPE` 行を書き、日本語が `???` になる）。

### 3.2 Shift_JIS への追記

```powershell
Copy-Item .\sjis.csv .\append_sjis.csv
[pscustomobject]@{ 名前 = '鈴木'; メモ = '追記' } | Export-ProbedCsv .\append_sjis.csv -Append
Show-Bytes .\append_sjis.csv
Import-ProbedCsv .\append_sjis.csv
```

期待: 追記した行も Shift_JIS（「鈴木」は `97 E9 96 D8`）。引用符は `Always` なので `"鈴木","追記"`。読み直すと 2 件。

### 3.3 最後の行に改行が無いファイルへの追記

```powershell
[System.IO.File]::WriteAllBytes("$t\nofinal.csv", [byte[]](0x41,0x2C,0x42,0x0D,0x0A,0x31,0x2C,0x32))
[pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv .\nofinal.csv -Append
Show-Bytes .\nofinal.csv
```

期待: `41 2C 42 0D 0A 31 2C 32 0D 0A 22 78 22 2C 22 79 22 0D 0A`（改行を補ってから追記。標準は `1,2"x","y"` とつながる）。

### 3.4 同じファイルの読み書き

```powershell
Copy-Item .\utf8.csv .\r.csv
Import-ProbedCsv .\r.csv | Export-ProbedCsv .\r.csv
Show-Bytes .\r.csv
```

期待: `SamePathRoundTrip` の終了エラー。r.csv は元のまま（標準は 0 バイトになる）。

### 3.5 `-DetectionLineCount` での追記

```powershell
Copy-Item .\head_ascii_sjis.csv .\append_head.csv
$before = (Get-FileHash .\append_head.csv).Hash
[pscustomobject]@{ line1 = '日本'; abc = 'x' } | Export-ProbedCsv .\append_head.csv -Append -DetectionLineCount 10
(Get-FileHash .\append_head.csv).Hash -eq $before   # True（ファイルが変わっていない）
```

期待: `DetectionVerificationFailed` の終了エラー。**何も追記されない**（UTF-8 の行が Shift_JIS のファイルに混ざらない）。
`-WhatIf` を付けても同じエラーになること。

### 3.6 5.1 と 7.x でバイト単位で同じになること

```powershell
$o = @(
    [pscustomobject]@{ A = 'a,b'; B = 'say "hi"'; C = "l1`nl2"; D = $null }
    [pscustomobject]@{ A = 'x';   B = 'y';        C = 'z';      D = '' }
)
$o | Export-ProbedCsv .\same.csv
$o | Export-ProbedCsv .\same_asneeded.csv -UseQuotes AsNeeded   # 5.1 でも使える
Show-Bytes .\same.csv
Show-Bytes .\same_asneeded.csv
```

期待: 両ホストで完全に同じバイト列。

---

## 4. リリース前のチェック

| # | 項目 |
|---|---|
| 1 | モジュールのバージョンが 1.3.0（csproj、`.psd1` の `ModuleVersion`、`ReleaseNotes`） |
| 2 | **クラスライブラリは 1.2.0 のまま**（NuGet パッケージは公開しない） |
| 3 | `.psd1` の `CmdletsToExport` に `Import-ProbedCsv` / `Export-ProbedCsv` がある |
| 4 | **配布フォルダー（`publish/SnowStack.EncodingProbe.PowerShell/`）に `LICENSE.txt` と `THIRD-PARTY-NOTICES.txt` を置いた**（MIT License の表示の条件。仕様書 5.3） |
| 5 | `THIRD-PARTY-NOTICES.txt` に PowerShell の節があり、ライセンス本文が原文のまま |
| 6 | MAML ヘルプを `core\` と `desktop\` の下の 7 フォルダー（計 14 か所）に配置した |
| 7 | クリーンな環境（Windows Sandbox など）で、PSGallery 形式のモジュールを 5.1 と 7.x にインストールし、1〜3 章の主要な項目を再確認した |
| 8 | CHANGELOG.md、README.md に 1.3.0 の節がある |

---

## 5. 確認の記録

確認で見つけた課題は、1.1.0 / 1.2.0 と同じく `docs/EncodingProbe-1.3.0-課題_人間記述用.md` に書き、Claude Code に渡す。
