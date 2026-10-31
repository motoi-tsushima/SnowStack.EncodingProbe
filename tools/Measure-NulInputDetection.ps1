#Requires -Version 5.1
<#
.SYNOPSIS
    0x00 を含む入力（BOM 無しの UTF-16 / UTF-32 で英数字だけのファイルなど）の判定結果を、実行中のホストで測定する。

.DESCRIPTION
    docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md の測定に使う。
    ISO-2022 / ASCII の判定（ISO2022_Detection）が 0x00 を含む入力を ASCII と判定する課題
    （docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md）について、現状と修正案のビルドの結果を比べるためのもの。

    このスクリプトは 1 つのホストで 1 つのビルドを測定し、TSV を書き出す。
    2 つのビルドを PowerShell 5.1（net48）と 7.x（net10.0）の両方で測定して比べるには
    Invoke-NulInputMeasurement.ps1 を使う。

    入力はすべてこのスクリプトの中でバイト列として組み立てる（製品の書き込み処理を使わない）。
    群（group 列）の意味は調査文書の 2 章を参照。
      A … 修正案で UTF-16 / UTF-32 になってほしいもの
      B … 修正案でも ASCII のままであってほしいもの（リスクの確認）
      C … 修正案で結果が変わらないはずのもの（回帰の確認）。C1 は TestData のすべてのファイル
    第 2 回（修正案 2）で追加した群（調査文書の 7 章）:
      A6 … 修正案 2 では直らないと予想しているもの（2 バイトとも 0x01〜0x7F になる文字、U+0000 を含むもの）
      B9〜B11 … 区切りにくい短い入力（2・3・4・5・6 バイト）
      B12 … 実在しそうな 0x00 入りの ASCII

    出力するファイル（TFM は net10.0 または net48）:
      detect-<BuildName>-<TFM>.tsv   … 入力 × カルチャー × 判定方式ごとの EncodingProbe.Detect の結果
      diag-<BuildName>-<TFM>.tsv     … 入力ごとの診断（ISO2022_Detection / Utf32_Detection / Utf16_Detection を
                                       リフレクションで単独に呼んだ結果と、NULL パターンの数）。
                                       strict 列は修正案 2 の StrictNulUnicode_Detection を単独で呼んだ結果
                                       （そのメソッドが無いビルドでは '-'）。strictrule 列は修正案 2 の規則を
                                       このスクリプトで数え直した値（補助。どのビルドでも同じ）
      commands-<BuildName>-<TFM>.tsv … 代表の入力に対するコマンドの結果（-ModuleDll を指定した場合だけ）

.PARAMETER Bin
    SnowStack.EncodingProbe.dll と UtfUnknown.dll があるフォルダー。
    既定は <RepositoryRoot>/SnowStack.EncodingProbe.PowerShell/bin/<Configuration>/<TFM>（モジュールの出力。
    モジュールを読み込むときに同名のアセンブリが別の場所から読み込まれないよう、モジュールと同じフォルダーを使う）。

.PARAMETER RepositoryRoot
    測定するビルドのリポジトリ（または worktree）のルート。既定はこのスクリプトのリポジトリ。
    TestData（C1）は常にこのスクリプトのリポジトリのものを使う。

.PARAMETER ModuleDll
    SnowStack.EncodingProbe.PowerShell.dll のパス。指定しなかった場合は <Bin> のものを使う。
    -SkipCommands を指定するとコマンドを測らない。

.PARAMETER BuildName
    出力ファイル名に入れるビルドの名前（例: current / proposal / strict）。

.PARAMETER OutDirectory
    TSV の出力先（UTF-8、BOM 無し）。

.EXAMPLE
    pwsh -NoProfile -File tools/Measure-NulInputDetection.ps1 -BuildName current -OutDirectory tools/NulInputDetectionResults
#>
[CmdletBinding()]
param(
    [string] $Bin,
    [string] $RepositoryRoot,
    [string] $Configuration = 'Debug',
    [string] $ModuleDll,
    [switch] $SkipCommands,
    [Parameter(Mandatory = $true)] [string] $BuildName,
    [Parameter(Mandatory = $true)] [string] $OutDirectory
)

$ErrorActionPreference = 'Stop'

$scriptRepositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($RepositoryRoot)) { $RepositoryRoot = $scriptRepositoryRoot }
$isCore = ($PSVersionTable.PSEdition -eq 'Core')
$tfm = if ($isCore) { 'net10.0' } else { 'net48' }
if ([string]::IsNullOrEmpty($Bin)) {
    $Bin = Join-Path $RepositoryRoot "SnowStack.EncodingProbe.PowerShell\bin\$Configuration\$tfm"
}
if ([string]::IsNullOrEmpty($ModuleDll)) {
    $ModuleDll = Join-Path $Bin 'SnowStack.EncodingProbe.PowerShell.dll'
}
foreach ($dll in 'UtfUnknown.dll', 'SnowStack.EncodingProbe.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $Bin $dll))) {
        throw "$dll が見つかりません: $Bin`n先に dotnet build を実行してください。"
    }
}
Add-Type -Path (Join-Path $Bin 'UtfUnknown.dll')
Add-Type -Path (Join-Path $Bin 'SnowStack.EncodingProbe.dll')
if ($isCore) {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}
if (-not (Test-Path -LiteralPath $OutDirectory)) { New-Item -ItemType Directory -Path $OutDirectory | Out-Null }

$cultures = 'ja-JP', 'en-US'
$strategies = 'Combined', 'NativeOnly', 'UtfUnknownOnly'

#--------------------------------------------------------------------------
# バイト列の組み立て
#--------------------------------------------------------------------------
# 文字列の各文字（BMP の範囲）を、指定した幅（1 / 2 / 4 バイト）とエンディアンで並べる。
# 製品のコードも System.Text.Encoding も使わず、符号位置からバイトを直接作る。
function ConvertTo-UnitBytes {
    param([string] $Text, [int] $Width, [switch] $BigEndian)

    $list = New-Object System.Collections.Generic.List[byte]
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($Width -eq 1 -and $code -ge 0x80) { throw "1 バイトの幅で 0x80 以上の文字は作れません: $code" }
        $unit = New-Object byte[] $Width
        for ($k = 0; $k -lt $Width; $k++) {
            $value = [byte](($code -shr (8 * $k)) -band 0xFF)
            if ($BigEndian) { $unit[$Width - 1 - $k] = $value } else { $unit[$k] = $value }
        }
        $list.AddRange($unit)
    }
    return , $list.ToArray()
}

function Join-Bytes {
    param([object[]] $Parts)
    $list = New-Object System.Collections.Generic.List[byte]
    foreach ($part in $Parts) { $list.AddRange([byte[]]$part) }
    return , $list.ToArray()
}

function New-Zeros([int] $Count) { return , (New-Object byte[] $Count) }

# 16 進のリテラル（"41 00 2C"）からバイト列を作る
function ConvertFrom-Hex([string] $Hex) {
    $tokens = $Hex.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    $bytes = New-Object byte[] $tokens.Count
    for ($k = 0; $k -lt $tokens.Count; $k++) { $bytes[$k] = [Convert]::ToByte($tokens[$k], 16) }
    return , $bytes
}

$script:inputs = New-Object System.Collections.Generic.List[object]
function Add-Input {
    param([string] $Group, [string] $Id, [string] $Label, [byte[]] $Bytes, [switch] $Command)
    $script:inputs.Add([PSCustomObject]@{ Group = $Group; Id = $Id; Label = $Label; Bytes = $Bytes; Command = [bool]$Command })
}

# UTF-16LE / UTF-16BE / UTF-32LE / UTF-32BE の 4 種を追加する
$unicodeForms = @(
    @{ Name = 'u16le'; Width = 2; BigEndian = $false }
    @{ Name = 'u16be'; Width = 2; BigEndian = $true }
    @{ Name = 'u32le'; Width = 4; BigEndian = $false }
    @{ Name = 'u32be'; Width = 4; BigEndian = $true }
)
function Add-UnicodeForms {
    param([string] $Group, [string] $Id, [string] $Label, [string] $Text, [string[]] $Forms = @('u16le', 'u16be', 'u32le', 'u32be'), [switch] $Command)
    foreach ($form in $unicodeForms) {
        if ($Forms -notcontains $form.Name) { continue }
        $bytes = ConvertTo-UnitBytes -Text $Text -Width $form.Width -BigEndian:($form.BigEndian)
        Add-Input -Group $Group -Id ('{0}-{1}' -f $Id, $form.Name) -Label ('{0} ({1})' -f $Label, $form.Name) -Bytes $bytes -Command:$Command
    }
}

#--------------------------------------------------------------------------
# A. 修正案で UTF-16 / UTF-32 になってほしいもの
#--------------------------------------------------------------------------
$a1Text = "A,B`r`n1,2`r`nabc,def`r`n"
Add-UnicodeForms -Group 'A1' -Id 'A1-crlf' -Label 'A,B / 1,2 / abc,def CRLF' -Text $a1Text -Command
Add-UnicodeForms -Group 'A2' -Id 'A2-lf' -Label 'A,B / 1,2 / abc,def LF' -Text "A,B`n1,2`nabc,def`n"
Add-UnicodeForms -Group 'A2' -Id 'A2-cr' -Label 'A,B / 1,2 / abc,def CR' -Text "A,B`r1,2`rabc,def`r"
Add-UnicodeForms -Group 'A2' -Id 'A2-none' -Label 'A,B;1,2;abc,def (改行なし)' -Text 'A,B;1,2;abc,def'

$line32 = "The quick brown fox 0123456789`r`n"   # 32 文字
Add-UnicodeForms -Group 'A3' -Id 'A3-1ch' -Label '1 文字 A' -Text 'A'
Add-UnicodeForms -Group 'A3' -Id 'A3-2ch' -Label '2 文字 AB' -Text 'AB'
Add-UnicodeForms -Group 'A3' -Id 'A3-10ch' -Label '10 文字 0123456789' -Text '0123456789'
Add-UnicodeForms -Group 'A3' -Id 'A3-1k' -Label '1024 文字 (32 文字 CRLF × 32)' -Text ($line32 * 32)
Add-UnicodeForms -Group 'A3' -Id 'A3-40k' -Label '40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト)' -Text ($line32 * 1250)

$printable = -join ((0x20..0x7E) | ForEach-Object { [char]$_ })
Add-UnicodeForms -Group 'A4' -Id 'A4-printable' -Label '0x20〜0x7E を一巡 (改行なし)' -Text $printable
Add-UnicodeForms -Group 'A4' -Id 'A4-tab' -Label '数字・記号・タブ・空白 CRLF' -Text "1`t2`t3`r`n-`t+ *`r`n 9.5`t(0)`r`n"

$csvCases = [ordered]@{
    'c03' = "Name,Memo`r`n#1,abc`r`n2,def`r`n"
    'c05' = "# comment`r`nName,Memo`r`na,b`r`n"
    'c09' = "A,B`r`n1,2,3`r`n4`r`n5,6"
    'c12' = "A,B`r1,2`r3,4`r"
}
foreach ($key in $csvCases.Keys) {
    Add-UnicodeForms -Group 'A5' -Id ('A5-{0}' -f $key) -Label ('CSV {0} (1.3.0 実測 M1)' -f $key) -Text $csvCases[$key] -Forms 'u16le', 'u32le'
}

#--------------------------------------------------------------------------
# B. 修正案でも ASCII のままであってほしいもの
#--------------------------------------------------------------------------
function Ascii([string] $Text) { return , (ConvertTo-UnitBytes -Text $Text -Width 1) }

Add-Input -Group 'B1' -Id 'B1-13+1' -Label '"Hello, World!" + 00 (14 バイト)' -Bytes (Join-Bytes @((Ascii 'Hello, World!'), (New-Zeros 1))) -Command
Add-Input -Group 'B1' -Id 'B1-12+1' -Label '"Hello World!" + 00 (13 バイト)' -Bytes (Join-Bytes @((Ascii 'Hello World!'), (New-Zeros 1)))
Add-Input -Group 'B1' -Id 'B1-15+1' -Label '"Hello, World!!!" + 00 (16 バイト)' -Bytes (Join-Bytes @((Ascii 'Hello, World!!!'), (New-Zeros 1)))

foreach ($n in 2, 3, 4, 100) {
    Add-Input -Group 'B2' -Id ('B2-12+{0}' -f $n) -Label ('"Hello, World" + 00 × {0}' -f $n) -Bytes (Join-Bytes @((Ascii 'Hello, World'), (New-Zeros $n)))
}

# 固定長レコード: 各レコードの文字数を変え、残りを 00 で埋める
$recordWords = 'ID001', 'Alice', 'B', 'Charlie01'
foreach ($recordLength in 8, 16, 17, 32) {
    foreach ($newline in $false, $true) {
        foreach ($count in 3, 4) {
            $parts = @()
            for ($r = 0; $r -lt $count; $r++) {
                $word = $recordWords[$r]
                if ($word.Length -gt $recordLength) { $word = $word.Substring(0, $recordLength) }
                $parts += , (Ascii $word)
                $parts += , (New-Zeros ($recordLength - $word.Length))
                if ($newline) { $parts += , (Ascii "`r`n") }
            }
            $bytes = Join-Bytes $parts
            $nl = if ($newline) { 'crlf' } else { 'nonl' }
            $isCommand = (($recordLength -eq 8 -and -not $newline -and $count -eq 4) -or ($recordLength -eq 16 -and $newline -and $count -eq 3))
            Add-Input -Group 'B3' -Id ('B3-rec{0}-{1}-x{2}' -f $recordLength, $nl, $count) -Label ('レコード長 {0}、{1}、{2} レコード ({3} バイト)' -f $recordLength, $nl, $count, $bytes.Length) -Bytes $bytes -Command:$isCommand
        }
    }
}

Add-Input -Group 'B4' -Id 'B4-abc' -Label 'a 00 bb 00 ccc 00' -Bytes (Join-Bytes @((Ascii 'a'), (New-Zeros 1), (Ascii 'bb'), (New-Zeros 1), (Ascii 'ccc'), (New-Zeros 1)))
Add-Input -Group 'B4' -Id 'B4-print0' -Label 'dir1/file.txt 00 dir2/x 00' -Bytes (Join-Bytes @((Ascii 'dir1/file.txt'), (New-Zeros 1), (Ascii 'dir2/x'), (New-Zeros 1)))

Add-Input -Group 'B5' -Id 'B5-1+13' -Label '00 + "Hello, World!"' -Bytes (Join-Bytes @((New-Zeros 1), (Ascii 'Hello, World!')))
Add-Input -Group 'B5' -Id 'B5-4+12' -Label '00 × 4 + "Hello, World"' -Bytes (Join-Bytes @((New-Zeros 4), (Ascii 'Hello, World')))

foreach ($n in 1, 2, 3, 4, 8, 1024) {
    Add-Input -Group 'B6' -Id ('B6-zero{0}' -f $n) -Label ('すべて 00 ({0} バイト)' -f $n) -Bytes (New-Zeros $n) -Command:($n -eq 2 -or $n -eq 4)
}

Add-Input -Group 'B7' -Id 'B7-9' -Label 'A 00 B 00 C 00 D E 00 (9 バイト)' -Bytes (ConvertFrom-Hex '41 00 42 00 43 00 44 45 00')
Add-Input -Group 'B7' -Id 'B7-10' -Label 'A 00 B 00 C 00 D E 00 00 (10 バイト)' -Bytes (ConvertFrom-Hex '41 00 42 00 43 00 44 45 00 00')

Add-Input -Group 'B8' -Id 'B8-13' -Label 'A 000000 B 000000 C D 000000 (13 バイト)' -Bytes (ConvertFrom-Hex '41 00 00 00 42 00 00 00 43 44 00 00 00')
Add-Input -Group 'B8' -Id 'B8-11' -Label 'A 000000 B 0000 C 000000 (11 バイト)' -Bytes (ConvertFrom-Hex '41 00 00 00 42 00 00 43 00 00 00')
Add-Input -Group 'B8' -Id 'B8-12' -Label 'A 000000 B 000000 C D 0000 (12 バイト)' -Bytes (ConvertFrom-Hex '41 00 00 00 42 00 00 00 43 44 00 00')
Add-Input -Group 'B8' -Id 'B8-16' -Label 'A 000000 B 000000 C 00 D 00000000 00 (16 バイト)' -Bytes (ConvertFrom-Hex '41 00 00 00 42 00 00 00 43 00 44 00 00 00 00 00')

#--------------------------------------------------------------------------
# 第 2 回で追加した入力（A6・B9〜B12）
#--------------------------------------------------------------------------
# A6. 2 バイトとも 0x01〜0x7F になる文字（すべてのバイトが 0x80 未満になる）
#   上 U+4E0A（0A 4E）、字 U+5B57（57 5B）、あ U+3042（42 30）、、 U+3001（01 30。制御文字の範囲のバイトを含む）
$a6Chars = [ordered]@{ 'ue' = 0x4E0A; 'ji' = 0x5B57; 'a' = 0x3042; 'touten' = 0x3001 }
$a6Names = @{ 'ue' = '上 U+4E0A'; 'ji' = '字 U+5B57'; 'a' = 'あ U+3042'; 'touten' = '、 U+3001' }
foreach ($key in $a6Chars.Keys) {
    $text = ('A,{0}' -f [char]$a6Chars[$key]) + "`r`n"
    $group = if ($key -eq 'ue') { 'A6-1' } else { 'A6-2' }
    Add-UnicodeForms -Group $group -Id ('A6-{0}' -f $key) -Label ('A,{0} CRLF' -f $a6Names[$key]) -Text $text -Forms 'u16le' -Command:($key -eq 'ue')
    Add-UnicodeForms -Group $group -Id ('A6-{0}' -f $key) -Label ('A,{0} CRLF' -f $a6Names[$key]) -Text $text -Forms 'u16be'
}
Add-UnicodeForms -Group 'A6-3' -Id 'A6-ue' -Label ('A,{0} CRLF' -f $a6Names['ue']) -Text (('A,{0}' -f [char]0x4E0A) + "`r`n") -Forms 'u32le', 'u32be'
Add-UnicodeForms -Group 'A6-4' -Id 'A6-nul' -Label 'A,B U+0000 C CRLF' -Text ("A,B{0}C`r`n" -f [char]0) -Forms 'u16le'

# B9〜B11. 区切りにくい短い入力
foreach ($hex in '41 00', '00 41', '0A 00', '00 0A') {
    Add-Input -Group 'B9' -Id ('B9-' + $hex.Replace(' ', '')) -Label $hex -Bytes (ConvertFrom-Hex $hex) -Command:($hex -eq '41 00')
}
foreach ($hex in '41 00 00 00', '00 00 00 41', '41 00 42 00', '00 41 00 42', '41 42 43 00') {
    Add-Input -Group 'B10' -Id ('B10-' + $hex.Replace(' ', '')) -Label $hex -Bytes (ConvertFrom-Hex $hex)
}
foreach ($hex in '41 00 42', '41 00 42 00 43', '41 00 42 00 43 00') {
    Add-Input -Group 'B11' -Id ('B11-' + $hex.Replace(' ', '')) -Label $hex -Bytes (ConvertFrom-Hex $hex)
}

# B12-1. 英数字のログの後ろを 00 で 4096 バイトまで埋めたもの（本文の長さを偶数・奇数で変える）
$logText = "2026-10-20 10:00:00 INFO start`r`n2026-10-20 10:00:01 INFO ready`r`n"
foreach ($body in $logText, ($logText + 'x')) {
    $bodyBytes = Ascii $body
    Add-Input -Group 'B12-1' -Id ('B12-1-pad4k-body{0}' -f $bodyBytes.Length) -Label ('ログ {0} バイト + 00 で 4096 バイトまで' -f $bodyBytes.Length) -Bytes (Join-Bytes @($bodyBytes, (New-Zeros (4096 - $bodyBytes.Length))))
}

# B12-2. 改行（CR-LF）ごとに 00 が 1 つ入るテキスト
$b12Words = 'abc', 'def', 'ghi', 'jkl', 'mno', 'pqr', 'stu', 'vwx', 'yz0', '123'
foreach ($lineCount in 2, 10) {
    $parts = @()
    for ($r = 0; $r -lt $lineCount; $r++) { $parts += , (Ascii $b12Words[$r]); $parts += , (New-Zeros 1); $parts += , (Ascii "`r`n") }
    $bytes = Join-Bytes $parts
    Add-Input -Group 'B12-2' -Id ('B12-2-lines{0}' -f $lineCount) -Label ('<3 文字> 00 CRLF × {0} ({1} バイト)' -f $lineCount, $bytes.Length) -Bytes $bytes
}

# B12-3. タブ区切りで、空のフィールドに 00 が入っているもの（偶数長と奇数長）
$nul = [string][char]0
$tsvCases = [ordered]@{
    'even' = "id`tname`tmemo`r`n1`t$nul`tx`r`n2`tbob`t$nul`r`n"
    'odd'  = "id`tname`tmemo`r`n1`t$nul`tx`r`n22`tbob`t$nul`r`n"
}
foreach ($key in $tsvCases.Keys) {
    $bytes = Ascii $tsvCases[$key]
    Add-Input -Group 'B12-3' -Id ('B12-3-{0}' -f $key) -Label ('タブ区切り、空のフィールドに 00 ({0} バイト)' -f $bytes.Length) -Bytes $bytes
}

#--------------------------------------------------------------------------
# C. 修正案で結果が変わらないはずのもの
#--------------------------------------------------------------------------
$testDataRoot = Join-Path $scriptRepositoryRoot 'tests\EncodingProbe.Tests\TestData'
# Sort-Object はホストによって並び順が変わる（カルチャー依存）ので、序数で並べる
$testDataFiles = [string[]]@(Get-ChildItem -LiteralPath $testDataRoot -Recurse -File | ForEach-Object { $_.FullName })
[Array]::Sort($testDataFiles, [StringComparer]::Ordinal)
foreach ($file in $testDataFiles) {
    $relative = $file.Substring($testDataRoot.Length + 1).Replace('\', '/')
    Add-Input -Group 'C1' -Id ('C1-' + $relative) -Label $relative -Bytes ([IO.File]::ReadAllBytes($file))
}

# ISO-2022-JP「日本」= ESC $ B 46 7C 4B 5C ESC ( B
Add-Input -Group 'C2' -Id 'C2-mid' -Label 'ESC$B 日本 ESC(B 00 A' -Bytes (ConvertFrom-Hex '1B 24 42 46 7C 4B 5C 1B 28 42 00 41')
Add-Input -Group 'C2' -Id 'C2-wrap' -Label 'A 00 ESC$B 日本 ESC(B CRLF 00' -Bytes (ConvertFrom-Hex '41 00 1B 24 42 46 7C 4B 5C 1B 28 42 0D 0A 00')

$boms = @{ u16le = 'FF FE'; u16be = 'FE FF'; u32le = 'FF FE 00 00'; u32be = '00 00 FE FF' }
foreach ($form in $unicodeForms) {
    $body = ConvertTo-UnitBytes -Text $a1Text -Width $form.Width -BigEndian:($form.BigEndian)
    Add-Input -Group 'C3' -Id ('C3-bom-{0}' -f $form.Name) -Label ('A1 の内容 BOM 付き ({0})' -f $form.Name) -Bytes (Join-Bytes @((ConvertFrom-Hex $boms[$form.Name]), $body))
}

# 日本語,ABC CRLF（日 U+65E5 本 U+672C 語 U+8A9E）
Add-UnicodeForms -Group 'C4' -Id 'C4-ja' -Label '日本語,ABC CRLF BOM 無し' -Text ("{0}{1}{2},ABC`r`n" -f [char]0x65E5, [char]0x672C, [char]0x8A9E)

#--------------------------------------------------------------------------
# 測定
#--------------------------------------------------------------------------
function Format-Head([byte[]] $Bytes) {
    $n = [Math]::Min(16, $Bytes.Length)
    $hex = for ($k = 0; $k -lt $n; $k++) { $Bytes[$k].ToString('X2') }
    $text = ($hex -join ' ')
    if ($Bytes.Length -gt 16) { $text += ' …' }
    return $text
}

function Get-ZeroCount([byte[]] $Bytes) {
    $count = 0
    foreach ($b in $Bytes) { if ($b -eq 0) { $count++ } }
    return $count
}

function Test-AsciiRangeByte([byte] $b) {
    # EncodingDetector.IsAsciiRangeByte と同じ規則
    return (($b -ge 0x20 -and $b -le 0x7E) -or $b -eq 0x09 -or $b -eq 0x0A -or $b -eq 0x0D)
}

# EncodingDetector の NULL パターンの数え方（Utf16_Detection / Utf32_Detection の後半）を再計算する（診断の補助）
function Get-NullPatternCounts([byte[]] $Bytes) {
    $le16 = 0; $be16 = 0; $le32 = 0; $be32 = 0
    $n = $Bytes.Length
    for ($i = 0; $i -lt $n; $i++) {
        if (-not (Test-AsciiRangeByte $Bytes[$i])) { continue }
        if ($i % 2 -eq 0 -and $i + 1 -lt $n -and $Bytes[$i + 1] -eq 0) { $le16++ }
        if ($i % 2 -eq 1 -and $Bytes[$i - 1] -eq 0) { $be16++ }
        if ($i % 4 -eq 0 -and $i + 3 -lt $n -and $Bytes[$i + 1] -eq 0 -and $Bytes[$i + 2] -eq 0 -and $Bytes[$i + 3] -eq 0) { $le32++ }
        if ($i % 4 -eq 3 -and $Bytes[$i - 3] -eq 0 -and $Bytes[$i - 2] -eq 0 -and $Bytes[$i - 1] -eq 0) { $be32++ }
    }
    return @($le16, $be16, $le32, $be32)
}

# 修正案 2 の規則（StrictNulUnicode_Detection）を数え直す（診断の補助）。
# 修正案 2 の分岐に入るのは、すべてのバイトが 0x80 未満で 0x00 を含む入力だけだが、ここでは形だけを見る
function Test-StrictUnits([byte[]] $Bytes, [int] $Unit, [int] $NonZero) {
    for ($i = 0; $i -lt $Bytes.Length; $i += $Unit) {
        for ($k = 0; $k -lt $Unit; $k++) {
            if (($Bytes[$i + $k] -eq 0) -eq ($k -eq $NonZero)) { return $false }
        }
    }
    return $true
}

function Get-StrictRule([byte[]] $Bytes) {
    $n = $Bytes.Length
    if ($n -ge 4 -and $n % 4 -eq 0) {
        if (Test-StrictUnits $Bytes 4 0) { return '1:UTF-32LE' }
        if (Test-StrictUnits $Bytes 4 3) { return '2:UTF-32BE' }
    }
    if ($n -ge 2 -and $n % 2 -eq 0) {
        if (Test-StrictUnits $Bytes 2 0) { return '3:UTF-16LE' }
        if (Test-StrictUnits $Bytes 2 1) { return '4:UTF-16BE' }
    }
    return 'no'
}

$bindingFlags = [Reflection.BindingFlags]'NonPublic, Instance'
$detectorType = [SnowStack.EncodingProbe.EncodingDetector]
$isoMethod = $detectorType.GetMethod('ISO2022_Detection', $bindingFlags)
$u32Method = $detectorType.GetMethod('Utf32_Detection', $bindingFlags)
$u16Method = $detectorType.GetMethod('Utf16_Detection', $bindingFlags)
$strictMethod = $detectorType.GetMethod('StrictNulUnicode_Detection', $bindingFlags)   # 修正案 2 のビルドにだけある
$byteConstructor = $detectorType.GetConstructor([Type[]]@([byte[]], [SnowStack.EncodingProbe.DetectionMode]))

# 各判定メソッドを単独で呼ぶ。戻り値は「規格外」なので、成立 = false
function Invoke-Diagnostics([byte[]] $Bytes) {
    # string と byte[] のコンストラクタを取り違えないよう、型を指定して呼ぶ
    $detector = $byteConstructor.Invoke([object[]]@([byte[]]$Bytes, [SnowStack.EncodingProbe.DetectionMode]::Standard))

    $isoArgs = [object[]]@($false, 0)
    $isoOut = [bool]$isoMethod.Invoke($detector, $isoArgs)
    $iso = if ($isoOut) { 'out' } elseif ([bool]$isoArgs[0]) { 'iso:' + $isoArgs[1] } else { 'ascii' }

    $u32Args = [object[]]@($true)
    $u32 = if ([bool]$u32Method.Invoke($detector, $u32Args)) { 'no' } elseif ([bool]$u32Args[0]) { 'LE' } else { 'BE' }

    $u16Args = [object[]]@($true)
    $u16 = if ([bool]$u16Method.Invoke($detector, $u16Args)) { 'no' } elseif ([bool]$u16Args[0]) { 'LE' } else { 'BE' }

    $strict = '-'
    if ($null -ne $strictMethod) {
        $strictCodePage = [int]$strictMethod.Invoke($detector, [object[]]@())
        $strict = if ($strictCodePage -eq 0) { 'no' } else { [string]$strictCodePage }
    }

    return @($iso, $u32, $u16, $strict)
}

function Invoke-Detect([byte[]] $Bytes, [string] $Culture, [string] $Strategy) {
    $options = New-Object SnowStack.EncodingProbe.EncodingDetectorOptions
    $options.Culture = $Culture
    $options.Strategy = [SnowStack.EncodingProbe.DetectionStrategy]$Strategy
    try {
        $info = [SnowStack.EncodingProbe.EncodingProbe]::Detect([byte[]]$Bytes, $options)
        return ("{0}`t{1}`t{2}`t{3}" -f $info.CodePage, $info.EncodingWebName, $info.Bom, $info.LineBreak)
    }
    catch {
        $e = $_.Exception
        while ($null -ne $e.InnerException) { $e = $e.InnerException }
        return ("EXC:{0}`t`t`t" -f $e.GetType().Name)
    }
}

function Write-Tsv([string] $Path, [System.Collections.Generic.List[string]] $Lines) {
    [IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

$detectRows = New-Object System.Collections.Generic.List[string]
$detectRows.Add("group`tid`tculture`tstrategy`tCodePage`tEncodingWebName`tBom`tLineBreak")
$diagRows = New-Object System.Collections.Generic.List[string]
$diagRows.Add("group`tid`tlabel`tlength`tmod2`tmod4`tzeros`thead`tiso2022`tutf32`tutf16`tnull16le`tnull16be`tnull32le`tnull32be`tstrict`tstrictrule")

foreach ($item in $script:inputs) {
    $bytes = [byte[]]$item.Bytes
    $zeros = Get-ZeroCount $bytes
    $diag = Invoke-Diagnostics $bytes
    $nulls = if ($zeros -gt 0) { Get-NullPatternCounts $bytes } else { @(0, 0, 0, 0) }
    $diagRows.Add(("{0}`t{1}`t{2}`t{3}`t{4}`t{5}`t{6}`t{7}`t{8}`t{9}`t{10}`t{11}`t{12}`t{13}`t{14}`t{15}`t{16}" -f
        $item.Group, $item.Id, $item.Label, $bytes.Length, ($bytes.Length % 2), ($bytes.Length % 4), $zeros, (Format-Head $bytes),
        $diag[0], $diag[1], $diag[2], $nulls[0], $nulls[1], $nulls[2], $nulls[3], $diag[3], (Get-StrictRule $bytes)))

    foreach ($culture in $cultures) {
        foreach ($strategy in $strategies) {
            $detectRows.Add(("{0}`t{1}`t{2}`t{3}`t{4}" -f $item.Group, $item.Id, $culture, $strategy, (Invoke-Detect $bytes $culture $strategy)))
        }
    }
}

Write-Tsv (Join-Path $OutDirectory ('detect-{0}-{1}.tsv' -f $BuildName, $tfm)) $detectRows
Write-Tsv (Join-Path $OutDirectory ('diag-{0}-{1}.tsv' -f $BuildName, $tfm)) $diagRows

#--------------------------------------------------------------------------
# コマンド（A1 と B1・B3・B6 の代表。第 2 回で A6-1 の UTF-16LE と B9 の 41 00 を追加）
#--------------------------------------------------------------------------
if ($SkipCommands) { return }

Import-Module $ModuleDll

# 文字列を文字のコードの並びにする（0x100 未満は 2 桁、それ以上は 4 桁）
function Format-Codes([string] $Text) {
    if ($null -eq $Text) { return '(null)' }
    $codes = foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($code -lt 0x100) { $code.ToString('X2') } else { $code.ToString('X4') }
    }
    return ($codes -join ' ')
}

function Format-Errors($Errors) {
    $ids = foreach ($err in $Errors) { $err.FullyQualifiedErrorId.Split(',')[0] }
    return ('ERR:' + (($ids | Select-Object -Unique) -join '|'))
}

function Invoke-ResolveEncoding([string] $Path, [long] $LineCount) {
    $ev = $null
    $params = @{ Path = $Path; Culture = 'ja-JP'; ErrorAction = 'SilentlyContinue'; ErrorVariable = 'ev' }
    if ($LineCount -gt 0) { $params['DetectionLineCount'] = $LineCount }
    try { $info = Resolve-Encoding @params } catch { return Format-Errors @($_) }
    if ($ev) { return Format-Errors $ev }
    return ('{0} / {1} / {2}' -f $info.CodePage, $info.EncodingWebName, $info.LineBreak)
}

function Invoke-GetProbedContent([string] $Path) {
    $ev = $null
    try { $lines = @(Get-ProbedContent -LiteralPath $Path -Culture 'ja-JP' -ErrorAction SilentlyContinue -ErrorVariable ev) } catch { return Format-Errors @($_) }
    if ($ev) { return Format-Errors $ev }
    if ($lines.Count -eq 0) { return '(行なし)' }
    return ('{0} 行; 1 行目: {1}' -f $lines.Count, (Format-Codes ([string]$lines[0])))
}

function Invoke-ImportProbedCsv([string] $Path, [long] $LineCount) {
    $ev = $null
    $params = @{ LiteralPath = $Path; Culture = 'ja-JP'; ErrorAction = 'SilentlyContinue'; ErrorVariable = 'ev' }
    if ($LineCount -gt 0) { $params['DetectionLineCount'] = $LineCount }
    try { $rows = @(Import-ProbedCsv @params) } catch { return Format-Errors @($_) }
    $errorText = if ($ev) { ' ' + (Format-Errors $ev) } else { '' }
    if ($rows.Count -eq 0) { return '(行なし)' + $errorText }
    $first = $rows[0]
    $names = foreach ($p in $first.PSObject.Properties) { '[' + (Format-Codes $p.Name) + ']' }
    $values = foreach ($p in $first.PSObject.Properties) { '[' + (Format-Codes ([string]$p.Value)) + ']' }
    return ('{0} 行; 列名: {1}; 1 行目: {2}{3}' -f $rows.Count, ($names -join ' '), ($values -join ' '), $errorText)
}

$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('NulInputDetection_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDirectory | Out-Null
try {
    $commandRows = New-Object System.Collections.Generic.List[string]
    $commandRows.Add("group`tid`tcommand`tresult")
    foreach ($item in ($script:inputs | Where-Object { $_.Command })) {
        $path = Join-Path $workDirectory ($item.Id + '.csv')
        [IO.File]::WriteAllBytes($path, [byte[]]$item.Bytes)
        $results = [ordered]@{
            'Resolve-Encoding'                       = Invoke-ResolveEncoding $path 0
            'Resolve-Encoding -DetectionLineCount 1' = Invoke-ResolveEncoding $path 1
            'Get-ProbedContent'                      = Invoke-GetProbedContent $path
            'Import-ProbedCsv'                       = Invoke-ImportProbedCsv $path 0
            'Import-ProbedCsv -DetectionLineCount 1' = Invoke-ImportProbedCsv $path 1
        }
        foreach ($key in $results.Keys) {
            $commandRows.Add(("{0}`t{1}`t{2}`t{3}" -f $item.Group, $item.Id, $key, $results[$key]))
        }
    }
    Write-Tsv (Join-Path $OutDirectory ('commands-{0}-{1}.tsv' -f $BuildName, $tfm)) $commandRows
}
finally {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force
}
