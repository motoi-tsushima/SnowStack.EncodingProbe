<#
.SYNOPSIS
    PowerShell 5.1 と 7.x で同一の結果になることを確認するためのシナリオ集。

.DESCRIPTION
    本モジュールの存在意義は「PowerShell のバージョンによらず同一の結果を得られること」に
    あるため、同じシナリオを両ホストで実行し、出力を突き合わせて検証する。

    このスクリプトは片方のホストで実行され、結果を正規化したレポートとして出力する。
    両ホストの実行と突き合わせは Invoke-ProbedCompatTests.ps1 が行う。

    レポートにホスト固有の情報（バージョン番号、パス、PowerShell 自身のエラー文言）を
    含めてはならない。含めると、検証したい差分が埋もれる。

.PARAMETER Dll
    読み込む SnowStack.EncodingProbe.PowerShell.dll のパス。
    PowerShell 5.1 では net48 ビルド、PowerShell 7.x では net10.0 ビルドを指定する。

.PARAMETER OutFile
    レポートの出力先。UTF-8 (BOM 無し) で書き出す。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Dll,
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = 'Stop'

# メッセージの言語差ではなく挙動の差を見たいので、UI カルチャーを固定する。
# 言語ごとのメッセージ自体は xUnit 側 (MessageCatalogTests) で検証している。
[System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::GetCultureInfo('en-US')

# Import-Module は相対パスをモジュール検索パスとして解釈してしまうため、絶対パスに直す
Import-Module ((Resolve-Path -LiteralPath $Dll).Path)

$script:Report = New-Object System.Collections.Generic.List[string]
$script:WorkRoot = Join-Path ([IO.Path]::GetTempPath()) ("ProbedCompat_" + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $script:WorkRoot

<#
.SYNOPSIS
    例外の最も内側のメッセージを返す。
.DESCRIPTION
    PowerShell 自身が付ける枠の文言 (「パラメーターの引数変換を処理できません」等) は
    ホストの言語設定によって変わるため、本モジュールが投げたメッセージだけを取り出す。
#>
function Get-InnermostMessage {
    param([System.Exception]$Exception)

    $current = $Exception
    while ($null -ne $current.InnerException) { $current = $current.InnerException }
    return ($current.Message -replace "`r?`n", ' ')
}

<#
.SYNOPSIS
    シナリオを実行し、結果を1行のレポートとして記録する。
#>
function Add-Scenario {
    param([string]$Name, [scriptblock]$Body)

    try {
        $value = & $Body
        $script:Report.Add(("{0}`tOK`t{1}" -f $Name, (ConvertTo-StableText $value)))
    }
    catch {
        $script:Report.Add(("{0}`tERROR`t{1}" -f $Name, (ConvertTo-StableText (Get-InnermostMessage $_.Exception))))
    }
}

<#
.SYNOPSIS
    レポートに載せる文字列から、実行ごとに変わる情報を取り除く。
.DESCRIPTION
    作業ディレクトリは実行のたびに異なるため、そのまま記録すると
    両ホストの結果が必ず不一致になる。プレースホルダーに置き換える。
#>
function ConvertTo-StableText {
    param([object]$Value)

    $text = [string]$Value
    if ([string]::IsNullOrEmpty($text)) { return $text }
    return $text.Replace($script:WorkRoot, '<WORK>')
}

<#
.SYNOPSIS
    Encoding インスタンスを、コードページと BOM で表現した文字列にする。
#>
function Format-Encoding {
    param([System.Text.Encoding]$Encoding)

    $preamble = ($Encoding.GetPreamble() | ForEach-Object { $_.ToString('X2') }) -join ''
    return ("cp={0} web={1} preamble={2}" -f $Encoding.CodePage, $Encoding.WebName, $preamble)
}

<#
.SYNOPSIS
    ファイルの内容を16進文字列にする。バイト単位の一致を検証するために使う。
#>
function Format-FileBytes {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return '(ファイルなし)' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    return (($bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

<#
.SYNOPSIS
    バイト列を明示して作業用ファイルを作る。
#>
function New-ByteFile {
    param([string]$Name, [byte[]]$Bytes)

    $path = Join-Path $script:WorkRoot $Name
    [IO.File]::WriteAllBytes($path, $Bytes)
    return $path
}

# ---------------------------------------------------------------------------
# ConvertTo-DotNetEncoding (仕様書 7 節)
# ---------------------------------------------------------------------------

foreach ($name in @(
    'utf8NoBOM', 'utf8BOM', 'utf8',
    'unicode', 'unicodeBOM', 'unicodeNoBOM',
    'bigendianunicode', 'bigendianunicodeBOM', 'bigendianunicodeNoBOM',
    'utf32', 'utf32BOM', 'utf32NoBOM',
    'bigendianutf32', 'bigendianutf32BOM', 'bigendianutf32NoBOM',
    'ascii', 'ansi', 'oem', 'utf7',
    'shift_jis', 'shift-jis', 'sjis', 'ms_kanji', 'euc-jp', 'iso-2022-jp', 'big5', 'gb2312',
    'utf-8', 'utf-16', 'utf-16BE', 'utf-32', 'utf-7',
    '932', '65001', '51932', '20127', '65000')) {

    Add-Scenario "ConvertTo/$name" ([scriptblock]::Create(
        "Format-Encoding (ConvertTo-DotNetEncoding '$name')"))
}

Add-Scenario 'ConvertTo/number-932'   { Format-Encoding (ConvertTo-DotNetEncoding 932) }
Add-Scenario 'ConvertTo/number-65001' { Format-Encoding (ConvertTo-DotNetEncoding 65001) }

Add-Scenario 'ConvertTo/instance-GetEncoding65001' {
    Format-Encoding (ConvertTo-DotNetEncoding ([System.Text.Encoding]::GetEncoding(65001)))
}
Add-Scenario 'ConvertTo/instance-Utf8NoBom' {
    Format-Encoding (ConvertTo-DotNetEncoding (New-Object System.Text.UTF8Encoding($false)))
}

# エラー系 (仕様書 8 節)
foreach ($name in @('Auto', 'nonexistent-encoding', 'ansiBOM', 'asciiNoBOM', '932BOM', 'utf8BOMBOM')) {
    Add-Scenario "ConvertTo/error/$name" ([scriptblock]::Create(
        "Format-Encoding (ConvertTo-DotNetEncoding '$name')"))
}

# ---------------------------------------------------------------------------
# Resolve-Encoding からのパイプライン (仕様書 7.3)
# ---------------------------------------------------------------------------

$utf8BomFile = New-ByteFile 'utf8_bom.txt' ([byte[]](0xEF, 0xBB, 0xBF, 0x61, 0x62, 0x63, 0x0D, 0x0A))
$sjisFile = New-ByteFile 'sjis.txt' ([byte[]](0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA, 0x0D, 0x0A))
$utf16LeFile = New-ByteFile 'utf16le_nobom.txt' ([byte[]](0x21, 0xFF, 0x22, 0xFF, 0x0A, 0x00))

Add-Scenario 'Pipeline/utf8BOM'  { Format-Encoding (Resolve-Encoding $utf8BomFile | ConvertTo-DotNetEncoding) }
Add-Scenario 'Pipeline/shiftjis' { Format-Encoding (Resolve-Encoding $sjisFile | ConvertTo-DotNetEncoding) }
Add-Scenario 'Pipeline/utf16le'  { Format-Encoding (Resolve-Encoding $utf16LeFile | ConvertTo-DotNetEncoding) }

# ---------------------------------------------------------------------------
# Get-ProbedContent (仕様書 4 節)
# ---------------------------------------------------------------------------

<#
.SYNOPSIS
    文字列をコードポイント列として表現する。
.DESCRIPTION
    ホストのコンソール出力エンコーディングに影響されずに文字列を比較するため、
    表示可能な文字ではなくコードポイントで記録する。
    BOM (U+FEFF) の混入も、この形式なら確実に検出できる。
#>
function Format-Text {
    param([string]$Text)

    if ($null -eq $Text) { return '(null)' }
    if ($Text.Length -eq 0) { return '(空文字列)' }
    return ((([int[]][char[]]$Text) | ForEach-Object { 'U+{0:X4}' -f $_ }) -join ' ')
}

<#
.SYNOPSIS
    Get-ProbedContent の出力を、行数とコードポイント列として表現する。
#>
function Format-Lines {
    param([object]$Lines)

    # @($null) は要素数1の配列になるため、出力が無い場合を先に判定する
    if ($null -eq $Lines) { return '(出力なし)' }

    $items = @($Lines)
    if ($items.Count -eq 0) { return '(出力なし)' }
    return ("{0}行: {1}" -f $items.Count, (($items | ForEach-Object { '[' + (Format-Text $_) + ']' }) -join ' '))
}

# 本文はいずれも "AB" + CRLF。BOM が本文に混入すれば U+FEFF として現れる。
$bomFiles = @{
    'utf8'     = [byte[]](0xEF,0xBB,0xBF, 0x41,0x42, 0x0D,0x0A)
    'utf16le'  = [byte[]](0xFF,0xFE, 0x41,0x00,0x42,0x00, 0x0D,0x00,0x0A,0x00)
    'utf16be'  = [byte[]](0xFE,0xFF, 0x00,0x41,0x00,0x42, 0x00,0x0D,0x00,0x0A)
    'utf32le'  = [byte[]](0xFF,0xFE,0x00,0x00, 0x41,0x00,0x00,0x00, 0x42,0x00,0x00,0x00)
    'utf32be'  = [byte[]](0x00,0x00,0xFE,0xFF, 0x00,0x00,0x00,0x41, 0x00,0x00,0x00,0x42)
    'nobom'    = [byte[]](0x41,0x42, 0x0D,0x0A)
}

foreach ($key in ($bomFiles.Keys | Sort-Object)) {
    $path = New-ByteFile "bom_$key.txt" $bomFiles[$key]
    Add-Scenario "Get/bom/$key" ([scriptblock]::Create("Format-Lines (Get-ProbedContent '$path')")).GetNewClosure()
}

# 原則A: -Encoding を明示しても BOM は読み飛ばす / BOM 無しファイルでもエラーにしない
$u8bom = New-ByteFile 'p_u8bom.txt' ([byte[]](0xEF,0xBB,0xBF, 0x41,0x42))
$u8nobom = New-ByteFile 'p_u8nobom.txt' ([byte[]](0x41,0x42))
Add-Scenario 'Get/原則A/明示shift_jis' { Format-Lines (Get-ProbedContent $u8bom -Encoding shift_jis) }
Add-Scenario 'Get/原則A/utf8BOM指定でBOM無' { Format-Lines (Get-ProbedContent $u8nobom -Encoding utf8BOM) }

# 各エンコーディングの判定と復号
$jp = '日本語'
$encodingCases = @(
    @('shiftjis', 932,   "`r`n"),
    @('eucjp',    51932, "`n"),
    @('utf8',     65001, "`r`n"),
    @('iso2022jp',50220, "`r`n")
)
foreach ($case in $encodingCases) {
    $enc = [System.Text.Encoding]::GetEncoding($case[1])
    $path = New-ByteFile ("t_" + $case[0] + '.txt') ($enc.GetBytes($jp + $case[2]))
    Add-Scenario ("Get/判定/" + $case[0]) ([scriptblock]::Create("Format-Lines (Get-ProbedContent '$path')")).GetNewClosure()
    Add-Scenario ("Get/判定/" + $case[0] + "/Resolve") ([scriptblock]::Create("(Resolve-Encoding '$path').CodePage")).GetNewClosure()
}

# -Raw は改行を保持する（無損失な読み取り）
foreach ($case in @(@('crlf', "a`r`nb`r`n"), @('lf', "a`nb`n"), @('末尾改行なし', "a`r`nb"))) {
    $path = New-ByteFile ("raw_" + $case[0] + '.txt') ([System.Text.Encoding]::ASCII.GetBytes($case[1]))
    Add-Scenario ("Get/Raw/" + $case[0]) ([scriptblock]::Create("Format-Text (Get-ProbedContent '$path' -Raw)")).GetNewClosure()
}

# -TotalCount / 複数ファイル / 境界条件
$three = New-ByteFile 'three.txt' ([System.Text.Encoding]::ASCII.GetBytes("1`r`n2`r`n3`r`n"))
$two = New-ByteFile 'two.txt' ([System.Text.Encoding]::ASCII.GetBytes("x`r`ny`r`n"))
Add-Scenario 'Get/TotalCount/0' { Format-Lines (Get-ProbedContent $three -TotalCount 0) }
Add-Scenario 'Get/TotalCount/2' { Format-Lines (Get-ProbedContent $three -TotalCount 2) }
Add-Scenario 'Get/TotalCount/超過' { Format-Lines (Get-ProbedContent $three -TotalCount 99) }
Add-Scenario 'Get/複数ファイル連結' { Format-Lines (Get-ProbedContent $three, $two) }
Add-Scenario 'Get/複数ファイルTotalCount1' { Format-Lines (Get-ProbedContent $three, $two -TotalCount 1) }
Add-Scenario 'Get/複数ファイルRaw個数' { @(Get-ProbedContent $three, $two -Raw).Count }

Add-Scenario 'Get/空ファイル' { Format-Lines (Get-ProbedContent (New-ByteFile 'e.txt' ([byte[]]@()))) }
Add-Scenario 'Get/空ファイルRaw' { Format-Text (Get-ProbedContent (New-ByteFile 'e2.txt' ([byte[]]@())) -Raw) }
Add-Scenario 'Get/BOMのみ' { Format-Lines (Get-ProbedContent (New-ByteFile 'b.txt' ([byte[]](0xEF,0xBB,0xBF)))) }
Add-Scenario 'Get/1バイト' { Format-Lines (Get-ProbedContent (New-ByteFile 'o.txt' ([byte[]](0x41)))) }

# エラー系
$binary = New-ByteFile 'binary.bin' ([byte[]](0x81,0xFF,0x00,0xFE,0x93,0x40,0xC0,0x80,0xED,0xA0,0x80))
Add-Scenario 'Get/error/判定失敗' { Get-ProbedContent $binary -ErrorAction Stop }
Add-Scenario 'Get/error/Raw+TotalCount' { Get-ProbedContent $three -Raw -TotalCount 1 -ErrorAction Stop }
Add-Scenario 'Get/error/存在しないファイル' {
    Get-ProbedContent (Join-Path $script:WorkRoot 'missing_file.txt') -ErrorAction Stop
}

# ---------------------------------------------------------------------------
# レポート出力
# ---------------------------------------------------------------------------

try {
    [IO.File]::WriteAllLines($OutFile, $script:Report, (New-Object System.Text.UTF8Encoding($false)))
}
finally {
    Remove-Item -LiteralPath $script:WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
}