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
        $script:Report.Add(("{0}`tOK`t{1}" -f $Name, $value))
    }
    catch {
        $script:Report.Add(("{0}`tERROR`t{1}" -f $Name, (Get-InnermostMessage $_.Exception)))
    }
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
# レポート出力
# ---------------------------------------------------------------------------

try {
    [IO.File]::WriteAllLines($OutFile, $script:Report, (New-Object System.Text.UTF8Encoding($false)))
}
finally {
    Remove-Item -LiteralPath $script:WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
}