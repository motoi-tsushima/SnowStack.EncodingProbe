# EncodingProbe.PowerShell 1.3.0 動作確認手順書

- 作成日: 2026-10-15（第六次の後の挙動を 4 章に足した: 2026-10-26）
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
Resolve-Encoding .\u16_ascii.csv -DetectionLineCount 1   # 1200（第四次でクラスライブラリを直した。追補 2 章）
Resolve-Encoding .\u16_ascii.csv                         # 全体でも 1200
```

期待: どちらも `CodePage : 1200`、`Bom : False`。第四次より前は `20127 / us-ascii` だった（仕様書 追補）。

第二次の後に、`Import-ProbedCsv .\u16_ascii.csv -DetectionLineCount 1` と `Import-ProbedCsv .\u16_ascii.csv`（全体）が、
`A=1, B=2` / `A=3, B=4` を正しく読むことも確かめる（値に U+0000 が混じらない）。

2 バイトとも `0x80` 未満になる文字（「上」U+4E0A）を英数字に混ぜた BOM 無しの UTF-16LE（追補 4.1 の既知の限界）:

```powershell
[System.IO.File]::WriteAllBytes("$t\u16_ue.csv", $u16.GetBytes("A,上`r`n1,2`r`n"))
Resolve-Encoding .\u16_ue.csv                            # 20127 / us-ascii（既知の限界）
Import-ProbedCsv .\u16_ue.csv -DetectionLineCount 1      # A=1, 上=2（第二次の後。改行の型による読み替え）
```

| 確認 | 期待 |
|---|---|
| 全体の判定 | `CodePage : 20127`、`EncodingWebName : us-ascii`（直らない。追補 4.1） |
| `Import-ProbedCsv -DetectionLineCount 1` | 1 件。列名は `A` と `上`、値は `1` と `2`（U+0000 が混じらない） |

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

最後の値の引用符が閉じていない追記先（`41 2C 42 0D 0A 31 2C 22 32`。`1,"2` で終わる）では、補った改行と追記したレコードが引用符の中に入り、読み直すと 1 件（B が `2` CRLF `x"`）になる。
警告もエラーも出ないのは既知の限界（仕様書 18.3。第六次 C で文書にした。1.4.0 で検討）。

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

期待: 両ホストで完全に同じバイト列。日付・数値の書式（ko-KR・ar-SA など）と、`-UseCulture` の区切り文字（ms-ID など。4.8）は、ホストの .NET のカルチャーのデータで違うことがある（仕様書 1.5・17 章）。

## 4. 第六次の後 — 改めた挙動（A・B・C）

テスト担当のブラックボックステスト（第 2 段階）への対応で変えた挙動を確かめる。根拠は仕様書 25〜27 章、実装記録 11〜13 章。

### 4.1 `ansi` / `oem`（第六次 A。BUG-0032）

```powershell
(ConvertTo-DotNetEncoding ansi).CodePage
(ConvertTo-DotNetEncoding oem).CodePage
(Get-EncodingProbePlatformInfo).Locale | Format-List *
[System.Text.Encoding]::Default.CodePage        # 5.1 だけ
ConvertTo-DotNetEncoding 0                      # エラー
```

期待: 日本語の Windows では `ansi` が 932、`oem` が 932（`Locale` の `AnsiCodePage` / `OemCodePage` も同じ。5.1 の `Default` も 932）。`0` はエラー。
可能なら、設定 → 時刻と言語 → 言語と地域 の「地域設定」の「形式」を「英語（米国）」にして新しいウィンドウで同じことを行い、`ansi` / `oem` が 932 のままであることを確かめる（以前は 1252 / 437 になっていた）。確かめたら「形式」を元に戻す。

### 4.2 BOM が 2 個続くファイル・途中で切れたファイル（第六次 A。BUG-0021・0042）

```powershell
[System.IO.File]::WriteAllBytes("$t\bom2.txt", [byte[]](0xEF,0xBB,0xBF,0xEF,0xBB,0xBF,0x41))
(Get-ProbedContent .\bom2.txt -Encoding utf8BOM -Raw).Length   # 2（2 個目の U+FEFF が残る）
[System.IO.File]::WriteAllBytes("$t\odd16.txt", [byte[]](0xFF,0xFE,0x41,0x00,0x42))
[int[]][char[]](Get-ProbedContent .\odd16.txt -Raw)            # 65, 65533（5.1 でも U+FFFD がある）
```

### 4.3 JIS のまま終わっているファイルへの追記（第六次 A。BUG-0018）

```powershell
$jis = [System.Text.Encoding]::GetEncoding(50220)
[System.IO.File]::WriteAllBytes("$t\jis.txt", [byte[]](0x1B,0x24,0x42,0x46,0x7C,0x4B,0x5C))   # 「日本」（ESC ( B で戻していない）
Add-ProbedContent .\jis.txt -Value 'abc'
Show-Bytes .\jis.txt
Get-ProbedContent .\jis.txt
```

期待: 追記の前に `1B 28 42`（`ESC ( B`）が書かれ、読み直すと「日本abc」。

### 4.4 逆向きの往復（第六次 B。BUG-0028）

```powershell
Set-Content .\a.txt -Value 'a1'; Set-Content .\z.txt -Value 'z1'
Get-ProbedContent .\a.txt, .\z.txt | Out-ProbedFile .\z.txt
Get-Content .\z.txt
```

期待: `SamePathRoundTrip` のエラー（`Out-ProbedFile` は書き込みを取り消して終了エラー）。z.txt は `z1` のまま（1.2.0 までは z.txt の元の内容が消えていた）。

### 4.5 属性・一時ファイル・ほかのプログラムが開いているファイル（第六次 B。BUG-0025・0026・0027）

```powershell
Set-Content .\attr.txt -Value 'x'
attrib -A +H .\attr.txt
Set-ProbedContent .\attr.txt -Value 'y' -Encoding utf8NoBOM    # -Force なしで隠しファイルに書ける
(Get-Item .\attr.txt -Force).Attributes                         # Hidden, Archive
attrib -H .\attr.txt

$fs = [System.IO.File]::Open("$t\attr.txt", 'Open', 'ReadWrite', 'ReadWrite')   # 書き込み用に開いたまま
Resolve-Encoding .\attr.txt                                     # 読める
$fs.Dispose()
```

1 MiB を超える上書きの一時ファイルは `.~probed<8 桁の 16 進>.probedtmp`（元の名前を含めない）で、完了すると残らない。
書き込み中にプロセスを強制終了したときに残った場合は、名前からはどのファイルのものか分からない（同じフォルダーにある）。

### 4.6 パラメーター（第六次 C。BUG-0038・0041・0039）

```powershell
Resolve-Encoding .\sjis.csv -Culture ' ko-KR' | Select-Object CodePage, Culture   # Culture は ko-KR（エラーにならない）
Get-ProbedContent .\sjis.csv -Head 1
Get-ProbedContent .\sjis.csv -First 1
Get-ProbedContent .\sjis.csv -TotalCount -1                   # 今までどおりパラメーターの検証エラー
Get-Help Get-ProbedContent -Parameter TotalCount               # 別名 Head, First。既定値 None (all lines)

Resolve-Encoding .\sjis.csv | Export-Clixml .\info.xml
Get-ProbedContent .\sjis.csv -Encoding (Import-Clixml .\info.xml)
```

期待: 最後のコマンドは、型（`Deserialized.SnowStack.EncodingProbe.EncodingInformation`）が違うことと、`CodePage` の値か BOM を明示した統一語彙名を渡す案内のエラー（以前は「文字エンコーディング '' を解決できませんでした」）。

### 4.7 メッセージ（第六次 C。BUG-0045、WriteFailed）

```powershell
[System.IO.File]::WriteAllBytes("$t\dup.csv", [byte[]](0x41,0x2C,0x61,0x0D,0x0A,0x31,0x2C,0x32,0x0D,0x0A))
Import-ProbedCsv .\utf8.csv, .\dup.csv                       # dup.csv のパスと列名 "a" を示すエラー。utf8.csv は読める
Import-ProbedCsv .\utf8.csv -Header 'N', 'n'                 # -Header の値が重複していると分かるエラー

[System.IO.File]::WriteAllBytes("$t\u16bom.txt", [byte[]](0xFF,0xFE,0x41,0x00,0x0D,0x00,0x0A,0x00))
Add-ProbedContent .\u16bom.txt -Value 'あ' -Encoding utf8NoBOM   # 'utf8NoBOM' と 'unicodeBOM' で示す（以前は 'utf-8' と 'utf-16'）
```

UI カルチャーを en-US・zh-TW・zh-CN に変えて Import-Module し直し（CLAUDE.md の手順）、`Import-ProbedCsv .\head_ascii_sjis.csv -DetectionLineCount 1` の `DetectionVerificationFailed` の文を読む。
英語は「the beginning of the file (with -DetectionLineCount 1)」（「first 1 lines」ではない）、中国語は「根據前 1 行判斷…」「根据前 1 行判断…」で始まる。
ディスクの空き不足の書き込みの失敗（`WriteFailed`）は手元では起こしにくいので、自動テストで確かめている（エラー コードを示す 5 言語のメッセージ）。

### 4.8 文書に書いた点（第六次 C。挙動は変えていない）

```powershell
# BUG-0034: 既定値の裸の utf8 は書き込みでは無視される（警告だけ）
$PSDefaultParameterValues['*:Encoding'] = 'utf8'
Set-ProbedContent .\u16bom.txt -Value 'B'          # 警告の後、UTF-16LE（追記先から継承）で書く
$PSDefaultParameterValues.Remove('*:Encoding')

# BUG-0040: 空のファイルの -Raw は空の文字列
New-Item .\empty.txt -ItemType File -Force | Out-Null
$null -eq (Get-ProbedContent .\empty.txt -Raw)       # False（標準の Get-Content -Raw は True）

# BUG-0043: BOM だけのファイルは -Bom Add でも 0 バイトになる
[System.IO.File]::WriteAllBytes("$t\bomonly.txt", [byte[]](0xEF,0xBB,0xBF))
Convert-ProbedContent .\bomonly.txt -Bom Add -PassThru | Select-Object Changed
Show-Bytes .\bomonly.txt                               # <0 bytes>

# BUG-0044: HZ は既定の判定では us-ascii
[System.IO.File]::WriteAllBytes("$t\hz.txt", [System.Text.Encoding]::ASCII.GetBytes('~{<:Ky2;S{#,NR2;PhR*~}'))
Resolve-Encoding .\hz.txt -Culture zh-CN                         # 20127
Resolve-Encoding .\hz.txt -Culture zh-CN -Strategy UtfUnknownOnly  # 52936
Get-ProbedContent .\hz.txt -Encoding 52936

# BUG-0029: -UseCulture の区切り文字はホストで違うことがある（両ホストで実行して比べる）
$c = [System.Globalization.CultureInfo]::CurrentCulture
[System.Threading.Thread]::CurrentThread.CurrentCulture = 'ms-ID'
[pscustomobject]@{ Name = 'x'; Price = '1,5' } | Export-ProbedCsv .\ms.csv -UseCulture
[System.Threading.Thread]::CurrentThread.CurrentCulture = $c
Show-Bytes .\ms.csv                                    # 5.1 は ; （3B）、7.x は , （2C）
```

各ヘルプ（`Get-Help <コマンド> -Full`）に、上の点が書かれていることも確かめる（`-Encoding`・`-UseCulture`・`-Append`（`Export-ProbedCsv`。引用符が閉じていない追記先の既知の限界）・`-Raw`・`-Strategy`・`Convert-ProbedContent` の注意事項）。

---

## 5. リリース前のチェック

| # | 項目 |
|---|---|
| 1 | モジュールのバージョンが 1.3.0（csproj、`.psd1` の `ModuleVersion`、`ReleaseNotes`） |
| 2 | **クラスライブラリも 1.3.0**（csproj の `Version` / `AssemblyVersion` / `FileVersion`、`PackageReleaseNotes`）。NuGet パッケージも公開する（第四次で方針を改めた。仕様書 追補 6 章） |
| 3 | `.psd1` の `CmdletsToExport` に `Import-ProbedCsv` / `Export-ProbedCsv` がある |
| 4 | **配布フォルダー（`publish/SnowStack.EncodingProbe.PowerShell/`）に `LICENSE.txt` と `THIRD-PARTY-NOTICES.txt` を置いた**（MIT License の表示の条件。仕様書 5.3） |
| 5 | `THIRD-PARTY-NOTICES.txt` に PowerShell の節があり、ライセンス本文が原文のまま |
| 6 | MAML ヘルプを `core\` と `desktop\` の下の 7 フォルダー（計 14 か所）に配置した |
| 7 | クリーンな環境（Windows Sandbox など）で、PSGallery 形式のモジュールを 5.1 と 7.x にインストールし、1〜3 章の主要な項目を再確認した |
| 8 | CHANGELOG.md、README.md に 1.3.0 の節がある |

---

## 6. 確認の記録

確認で見つけた課題は、1.1.0 / 1.2.0 と同じく `docs/EncodingProbe-1.3.0-課題_人間記述用.md` に書き、Claude Code に渡す。
