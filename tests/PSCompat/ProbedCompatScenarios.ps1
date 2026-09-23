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

# 判定はファイル全体を対象とする。
# 先頭が英数字だけで、後方にだけマルチバイト文字が現れるファイルを誤判定しないこと。
foreach ($cp in @(932, 65001)) {
    $enc = [System.Text.Encoding]::GetEncoding($cp)
    $builder = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt 60000; $i++) { $null = $builder.Append("// ASCII only source line for padding`r`n") }
    $null = $builder.Append("日本語のコメントです`r`n")
    $path = New-ByteFile ("tail_$cp.txt") ($enc.GetBytes($builder.ToString()))
    Add-Scenario "Get/末尾のみ多バイト/$cp" ([scriptblock]::Create(
        "Format-Text (@(Get-ProbedContent '$path')[-1])")).GetNewClosure()
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
# Set-ProbedContent (仕様書 5 節)
# ---------------------------------------------------------------------------

<#
.SYNOPSIS
    書き込み先を用意し、Set-ProbedContent を実行して結果のバイト列を返す。
.DESCRIPTION
    書き込み結果は Get-ProbedContent で読み返さずバイト列で記録する。
    読み書き両方に同じ誤りがある場合、読み返すと辻褄が合って検出できないためである。
#>
function Invoke-Set {
    param([string]$Name, [hashtable]$Parameters, [object]$Value = 'A', [byte[]]$Initial = $null)

    $path = Join-Path $script:WorkRoot $Name
    if ($null -ne $Initial) { [IO.File]::WriteAllBytes($path, $Initial) }

    Set-ProbedContent -LiteralPath $path -Value $Value @Parameters -ErrorAction Stop
    return (Format-FileBytes $path)
}

# 語彙ごとの書き出しバイト列。BOM 接尾辞のとおりになること
$script:setVocabularies = @(
    'utf8NoBOM', 'utf8BOM',
    'unicodeNoBOM', 'unicodeBOM',
    'bigendianunicodeNoBOM', 'bigendianunicodeBOM',
    'utf32NoBOM', 'utf32BOM',
    'bigendianutf32NoBOM', 'bigendianutf32BOM',
    'ascii', 'shift_jis', '932', 'euc-jp', 'iso-2022-jp', 'big5')

$script:setVocabularyIndex = 0
foreach ($name in $script:setVocabularies) {
    $script:setVocabularyIndex++
    Add-Scenario "Set/語彙/$name" ([scriptblock]::Create(
        "Invoke-Set 'w$($script:setVocabularyIndex).txt' @{ Encoding = '$name'; LineBreak = 'Lf' } '日'"))
}

# System.Text.Encoding インスタンスは、そのインスタンスの BOM 方針に従う
Add-Scenario 'Set/インスタンス/UTF8既定' {
    Invoke-Set 'inst1.txt' @{ Encoding = [System.Text.Encoding]::UTF8; LineBreak = 'Lf' }
}
Add-Scenario 'Set/インスタンス/UTF8BOM無し' {
    Invoke-Set 'inst2.txt' @{ Encoding = (New-Object System.Text.UTF8Encoding($false)); LineBreak = 'Lf' }
}

# 改行コード
Add-Scenario 'Set/改行/CrLf' { Invoke-Set 'lb1.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'CrLf' } }
Add-Scenario 'Set/改行/Lf' { Invoke-Set 'lb2.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Lf' } }
Add-Scenario 'Set/改行/Cr' { Invoke-Set 'lb3.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Cr' } }
Add-Scenario 'Set/改行/省略' { Invoke-Set 'lb4.txt' @{ Encoding = 'utf8NoBOM' } }
Add-Scenario 'Set/改行/NoNewline' {
    Invoke-Set 'lb5.txt' @{ Encoding = 'utf8NoBOM'; NoNewline = $true } @('A', 'B')
}

# -NoNewline と -LineBreak の同時指定は Warning（Error にはしない）
Add-Scenario 'Set/警告/NoNewlineとLineBreak' {
    $path = Join-Path $script:WorkRoot 'warn.txt'
    Set-ProbedContent -LiteralPath $path -Value 'A' -Encoding utf8NoBOM -NoNewline -LineBreak Lf -WarningVariable wv -WarningAction SilentlyContinue
    return ("{0} / {1}" -f (Format-FileBytes $path), (ConvertTo-StableText $wv[0].Message))
}

# -Encoding Auto（省略時）は書き込み先から継承する
$script:autoUtf8Bom = [byte[]](0xEF, 0xBB, 0xBF, 0x41, 0x0A)
$script:autoUtf8NoBom = [byte[]](0x41, 0x0D, 0x0A)
$script:autoUtf16Le = [byte[]](0xFF, 0xFE, 0x21, 0xFF, 0x0A, 0x00)
$script:autoSjis = [System.Text.Encoding]::GetEncoding(932).GetBytes("日本語`r`n")

Add-Scenario 'Set/Auto継承/utf8BOM_LF' { Invoke-Set 'auto1.txt' @{} '日' $script:autoUtf8Bom }
Add-Scenario 'Set/Auto継承/utf8NoBOM_CRLF' { Invoke-Set 'auto2.txt' @{} '日' $script:autoUtf8NoBom }
Add-Scenario 'Set/Auto継承/utf16LeBOM_LF' { Invoke-Set 'auto3.txt' @{} '日' $script:autoUtf16Le }
Add-Scenario 'Set/Auto継承/sjis_CRLF' { Invoke-Set 'auto4.txt' @{} '日' $script:autoSjis }

# 継承元が無い場合は Error。ファイルも作らない
Add-Scenario 'Set/error/Auto継承元なし' {
    $path = Join-Path $script:WorkRoot 'auto_missing.txt'
    try { Set-ProbedContent -LiteralPath $path -Value 'A' -ErrorAction Stop }
    finally { $script:Report.Add(("Set/Auto継承元なし/ファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}

# 0 バイトのファイルからは判定できないため、ランタイム既定（BOM 無し）を使う。
# 既定は net48 なら ANSI、net10.0 なら UTF-8 とホストごとに異なるため、
# バイト列そのものではなく「ランタイム既定と一致すること」を記録して比較する。
Add-Scenario 'Set/Auto継承/空ファイル' {
    $path = Join-Path $script:WorkRoot 'auto_empty.txt'
    [IO.File]::WriteAllBytes($path, [byte[]]@())
    Set-ProbedContent -LiteralPath $path -Value 'A' -ErrorAction Stop
    $expected = [System.Text.Encoding]::Default.GetBytes('A' + [Environment]::NewLine)
    $actual = [IO.File]::ReadAllBytes($path)
    return ("ランタイム既定と一致: {0}" -f (@(Compare-Object $expected $actual -SyncWindow 0).Count -eq 0))
}

# -EncodingFrom はエンコーディング・BOM・改行の3点を継承する
$script:refUtf16 = New-ByteFile 'ref_utf16le_bom_lf.txt' ([byte[]](0xFF, 0xFE, 0x21, 0xFF, 0x0A, 0x00))
$script:refSjis = New-ByteFile 'ref_sjis_crlf.txt' ([System.Text.Encoding]::GetEncoding(932).GetBytes("日本語`r`n"))
$script:refMixedCrLf = New-ByteFile 'ref_mixed1.txt' ([byte[]](0x41, 0x0A, 0x42, 0x0D, 0x0A, 0x43))
$script:refMixedNoCrLf = New-ByteFile 'ref_mixed2.txt' ([byte[]](0x41, 0x0A, 0x42, 0x0D, 0x43))

Add-Scenario 'Set/EncodingFrom/utf16leBOM' { Invoke-Set 'from1.txt' @{ EncodingFrom = $script:refUtf16 } }
Add-Scenario 'Set/EncodingFrom/sjis' { Invoke-Set 'from2.txt' @{ EncodingFrom = $script:refSjis } '日' }
Add-Scenario 'Set/EncodingFrom/改行上書き' {
    Invoke-Set 'from3.txt' @{ EncodingFrom = $script:refUtf16; LineBreak = 'CrLf' }
}
Add-Scenario 'Set/EncodingFrom/混在改行CrLfあり' { Invoke-Set 'from4.txt' @{ EncodingFrom = $script:refMixedCrLf } }
Add-Scenario 'Set/EncodingFrom/混在改行CrLfなし' { Invoke-Set 'from5.txt' @{ EncodingFrom = $script:refMixedNoCrLf } }

# EncodingInformation を渡した場合は改行も継承する（仕様書 5.4）
Add-Scenario 'Set/EncodingInformation/改行も継承' {
    $info = Resolve-Encoding $script:refUtf16
    Invoke-Set 'info1.txt' @{ Encoding = $info }
}

# 同一パスの往復
Add-Scenario 'Set/往復/ストリーミングは拒否' {
    $path = New-ByteFile 'rt1.txt' ([byte[]](0x41, 0x0D, 0x0A, 0x42, 0x0D, 0x0A))
    try { Get-ProbedContent -LiteralPath $path | Set-ProbedContent -LiteralPath $path -ErrorAction Stop }
    finally { $script:Report.Add(("Set/往復/拒否後のファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}
Add-Scenario 'Set/往復/Rawなら書き戻せる' {
    $path = New-ByteFile 'rt2.txt' ([byte[]](0x41, 0x0A, 0x42, 0x0A))
    Get-ProbedContent -LiteralPath $path -Raw | Set-ProbedContent -LiteralPath $path -NoNewline -ErrorAction Stop
    return (Format-FileBytes $path)
}
Add-Scenario 'Set/往復/変数経由の無損失' {
    $source = New-ByteFile 'rt3.txt' ([System.Text.Encoding]::GetEncoding(932).GetBytes("日本語`r`nABC`r`n"))
    $destination = Join-Path $script:WorkRoot 'rt3_out.txt'
    $text = Get-ProbedContent -LiteralPath $source -Raw
    Set-ProbedContent -LiteralPath $destination -Value $text -EncodingFrom $source -NoNewline -ErrorAction Stop
    return (Format-FileBytes $destination)
}

# -Value とパイプライン
Add-Scenario 'Set/Value/複数要素' {
    Invoke-Set 'v1.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Lf' } @('A', 'B')
}
Add-Scenario 'Set/Value/null要素' {
    Invoke-Set 'v2.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Lf' } @('A', $null, 'B')
}
Add-Scenario 'Set/Value/文字列以外' {
    Invoke-Set 'v3.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Lf' } @(1.5, 42)
}
Add-Scenario 'Set/Value/空コレクション' {
    Invoke-Set 'v4.txt' @{ Encoding = 'utf8NoBOM' } @()
}
Add-Scenario 'Set/Value/空コレクションBOM付き' {
    Invoke-Set 'v5.txt' @{ Encoding = 'utf8BOM' } @()
}
Add-Scenario 'Set/パイプライン入力' {
    $path = Join-Path $script:WorkRoot 'v6.txt'
    'A', 'B', 'C' | Set-ProbedContent -LiteralPath $path -Encoding utf8NoBOM -LineBreak Lf -ErrorAction Stop
    return (Format-FileBytes $path)
}

# -WhatIf ではファイルに触れない
Add-Scenario 'Set/WhatIf/ファイルを作らない' {
    $path = Join-Path $script:WorkRoot 'whatif.txt'
    Set-ProbedContent -LiteralPath $path -Value 'A' -Encoding utf8NoBOM -WhatIf
    return (Format-FileBytes $path)
}

# -Force と読み取り専用属性
Add-Scenario 'Set/error/読み取り専用' {
    $path = New-ByteFile 'ro1.txt' ([byte[]](0x41, 0x0A))
    Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
    try { Set-ProbedContent -LiteralPath $path -Value 'X' -Encoding utf8NoBOM -ErrorAction Stop }
    finally {
        $script:Report.Add(("Set/読み取り専用/内容`tOK`t{0}" -f (Format-FileBytes $path)))
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false
    }
}
Add-Scenario 'Set/Force/読み取り専用へ書き込む' {
    $path = New-ByteFile 'ro2.txt' ([byte[]](0x41, 0x0A))
    Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
    try {
        Set-ProbedContent -LiteralPath $path -Value 'X' -Encoding utf8NoBOM -LineBreak Lf -Force -ErrorAction Stop
        return (Format-FileBytes $path)
    }
    finally { Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false }
}

# エラー系 (仕様書 8 節)
$script:setErrorIndex = 0
foreach ($name in @('utf8', 'utf-8', '65001', 'utf-16', 'utf7', 'utf-7', 'ansiBOM')) {
    $script:setErrorIndex++
    Add-Scenario "Set/error/語彙/$name" ([scriptblock]::Create(
        "Invoke-Set 'err$($script:setErrorIndex).txt' @{ Encoding = '$name' }"))
}
Add-Scenario 'Set/error/EncodingとEncodingFrom' {
    Invoke-Set 'err_both.txt' @{ Encoding = 'utf8NoBOM'; EncodingFrom = $script:refUtf16 }
}
Add-Scenario 'Set/error/EncodingFrom参照先なし' {
    Invoke-Set 'err_from.txt' @{ EncodingFrom = (Join-Path $script:WorkRoot 'no_such_reference.txt') }
}
Add-Scenario 'Set/error/ディレクトリ' {
    Set-ProbedContent -LiteralPath $script:WorkRoot -Value 'A' -Encoding utf8NoBOM -ErrorAction Stop
}

# ---------------------------------------------------------------------------
# Add-ProbedContent (仕様書 6 節)
# ---------------------------------------------------------------------------

<#
.SYNOPSIS
    追記先を用意し、Add-ProbedContent を実行して結果のバイト列を返す。
#>
function Invoke-Add {
    param([string]$Name, [hashtable]$Parameters, [object]$Value = 'A', [byte[]]$Initial = $null)

    $path = Join-Path $script:WorkRoot $Name
    if ($null -ne $Initial) { [IO.File]::WriteAllBytes($path, $Initial) }

    Add-ProbedContent -LiteralPath $path -Value $Value @Parameters -ErrorAction Stop
    return (Format-FileBytes $path)
}

$script:sjis = [System.Text.Encoding]::GetEncoding(932)
$script:utf8 = New-Object System.Text.UTF8Encoding($false)
$script:ascii = [System.Text.Encoding]::ASCII
$script:iso2022 = [System.Text.Encoding]::GetEncoding(50220)

# 整合性検査（仕様書 6.1 の判定表）
$script:ruleCases = @(
    @('ascii_utf8_日本語', $script:ascii.GetBytes("ABC`r`n"), 'utf8NoBOM', '日本語'),
    @('utf8_ascii_ASCII', $script:utf8.GetBytes("日本語`r`n"), 'ascii', 'ABC'),
    @('utf8_sjis_日本語', $script:utf8.GetBytes("日本語`r`n"), 'shift_jis', '日本語'),
    @('utf8_sjis_ASCII', $script:utf8.GetBytes("日本語`r`n"), 'shift_jis', 'ABC'),
    @('sjis_utf8_日本語', $script:sjis.GetBytes("日本語`r`n"), 'utf8NoBOM', '日本語'),
    @('utf8_utf16_ASCII', $script:utf8.GetBytes("日本語`r`n"), 'unicodeNoBOM', 'ABC')
)

$script:ruleIndex = 0
foreach ($case in $script:ruleCases) {
    $script:ruleIndex++

    # GetNewClosure でループ変数を束縛する。
    # [scriptblock]::Create で組み立てると、生成した文字列が AMSI に引っかかることがある。
    $fileName = 'rule' + $script:ruleIndex + '.txt'
    $initialBytes = $case[1]
    $encodingName = $case[2]
    $appendValue = $case[3]

    Add-Scenario ("Add/判定表/" + $case[0]) {
        Invoke-Add $fileName @{ Encoding = $encodingName; LineBreak = 'Lf' } $appendValue $initialBytes
    }.GetNewClosure()
}

# -AllowEncodingChange で許可される。-Force では回避できない
Add-Scenario 'Add/AllowEncodingChange/許可' {
    Invoke-Add 'ace1.txt' @{ Encoding = 'shift_jis'; LineBreak = 'Lf'; AllowEncodingChange = $true } 'あ' $script:utf8.GetBytes("日`n")
}
Add-Scenario 'Add/error/Forceでは回避できない' {
    Invoke-Add 'ace2.txt' @{ Encoding = 'shift_jis'; LineBreak = 'Lf'; Force = $true } 'あ' $script:utf8.GetBytes("日`n")
}

# 1レコード内で拒否された場合、手前の要素も書き込まれないこと
Add-Scenario 'Add/error/レコード単位で中止' {
    $path = Join-Path $script:WorkRoot 'atomic.txt'
    [IO.File]::WriteAllBytes($path, $script:utf8.GetBytes("日`n"))
    try { Add-ProbedContent -LiteralPath $path -Value @('OK', 'あ') -Encoding shift_jis -LineBreak Lf -ErrorAction Stop }
    finally { $script:Report.Add(("Add/中止後のファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}

# BOM は常に無視する（仕様書 6.3）
Add-Scenario 'Add/BOM/途中に書かない' {
    Invoke-Add 'bom1.txt' @{ Encoding = 'utf8BOM'; LineBreak = 'Lf' } 'B' ([byte[]](0x41, 0x0A))
}
Add-Scenario 'Add/BOM/BOM付きへ追記しても増えない' {
    Invoke-Add 'bom2.txt' @{ LineBreak = 'Lf' } 'B' ([byte[]](0xEF, 0xBB, 0xBF, 0x41, 0x0A))
}
Add-Scenario 'Add/BOM/新規作成でも書かない' {
    Invoke-Add 'bom3.txt' @{ Encoding = 'utf8BOM'; LineBreak = 'Lf' }
}
Add-Scenario 'Add/BOM/警告は出さない' {
    $path = Join-Path $script:WorkRoot 'bom4.txt'
    [IO.File]::WriteAllBytes($path, [byte[]](0x41, 0x0A))
    Add-ProbedContent -LiteralPath $path -Value 'B' -Encoding utf8BOM -LineBreak Lf `
        -WarningVariable wv -WarningAction SilentlyContinue -ErrorAction Stop
    return ("警告数: {0}" -f @($wv).Count)
}

# 改行（仕様書 6.3）
Add-Scenario 'Add/改行/不一致は許可' {
    Invoke-Add 'lb1.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Lf' } 'B' ([byte[]](0x41, 0x0D, 0x0A))
}
Add-Scenario 'Add/改行/末尾改行なしへの追記' {
    Invoke-Add 'lb2.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Lf' } 'B' ([byte[]](0x41))
}
Add-Scenario 'Add/改行/追記先から継承CR' {
    Invoke-Add 'lb3.txt' @{} 'B' ([byte[]](0x41, 0x0D))
}
Add-Scenario 'Add/改行/NoNewline' {
    Invoke-Add 'lb4.txt' @{ Encoding = 'utf8NoBOM'; NoNewline = $true } 'B' ([byte[]](0x41, 0x0A))
}

# ISO-2022-JP への追記（状態を持つエンコーディング）
Add-Scenario 'Add/ISO2022JP/エスケープシーケンス' {
    Invoke-Add 'iso.txt' @{} '本' $script:iso2022.GetBytes("日`r`n")
}
Add-Scenario 'Add/ISO2022JP/読み返し' {
    $path = Join-Path $script:WorkRoot 'iso2.txt'
    [IO.File]::WriteAllBytes($path, $script:iso2022.GetBytes("日`r`n"))
    Add-ProbedContent -LiteralPath $path -Value '本' -ErrorAction Stop
    return (Format-Lines (Get-ProbedContent -LiteralPath $path))
}

# -Encoding Auto と -EncodingFrom
Add-Scenario 'Add/Auto継承/sjis' { Invoke-Add 'auto1.txt' @{} 'あ' $script:sjis.GetBytes("日本語`r`n") }
Add-Scenario 'Add/Auto継承/utf8BOM' {
    Invoke-Add 'auto2.txt' @{} '日' ([byte[]](0xEF, 0xBB, 0xBF, 0x41, 0x0A))
}
Add-Scenario 'Add/error/Auto継承元なし' {
    $path = Join-Path $script:WorkRoot 'auto_missing_add.txt'
    try { Add-ProbedContent -LiteralPath $path -Value 'A' -ErrorAction Stop }
    finally { $script:Report.Add(("Add/Auto継承元なし/ファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}
Add-Scenario 'Add/新規作成/明示指定なら作る' {
    Invoke-Add 'new1.txt' @{ Encoding = 'shift_jis'; LineBreak = 'CrLf' } '日'
}
Add-Scenario 'Add/Auto継承/空ファイル' {
    $path = Join-Path $script:WorkRoot 'add_empty.txt'
    [IO.File]::WriteAllBytes($path, [byte[]]@())
    Add-ProbedContent -LiteralPath $path -Value 'A' -ErrorAction Stop
    $expected = [System.Text.Encoding]::Default.GetBytes('A' + [Environment]::NewLine)
    $actual = [IO.File]::ReadAllBytes($path)
    return ("ランタイム既定と一致: {0}" -f (@(Compare-Object $expected $actual -SyncWindow 0).Count -eq 0))
}
Add-Scenario 'Add/EncodingFrom/参照元から継承' {
    $reference = New-ByteFile 'add_ref.txt' ($script:sjis.GetBytes("日本語`r`n"))
    Invoke-Add 'from_add.txt' @{ EncodingFrom = $reference } 'い' ($script:sjis.GetBytes("あ`n"))
}

# パイプラインと複数要素
Add-Scenario 'Add/Value/複数要素' {
    Invoke-Add 'av1.txt' @{ Encoding = 'utf8NoBOM'; LineBreak = 'Lf' } @('B', 'C') ([byte[]](0x41, 0x0A))
}
Add-Scenario 'Add/パイプライン入力' {
    $path = Join-Path $script:WorkRoot 'av2.txt'
    [IO.File]::WriteAllBytes($path, [byte[]](0x41, 0x0A))
    'B', 'C' | Add-ProbedContent -LiteralPath $path -Encoding utf8NoBOM -LineBreak Lf -ErrorAction Stop
    return (Format-FileBytes $path)
}

# 往復・-WhatIf・読み取り専用
Add-Scenario 'Add/error/読み取り中への追記' {
    $path = New-ByteFile 'add_rt.txt' ([byte[]](0x41, 0x0A, 0x42, 0x0A))
    try { Get-ProbedContent -LiteralPath $path | Add-ProbedContent -LiteralPath $path -ErrorAction Stop }
    finally { $script:Report.Add(("Add/往復拒否後のファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}
Add-Scenario 'Add/WhatIf/ファイルに触れない' {
    $path = New-ByteFile 'add_whatif.txt' ([byte[]](0x41, 0x0A))
    Add-ProbedContent -LiteralPath $path -Value 'B' -Encoding utf8NoBOM -WhatIf
    return (Format-FileBytes $path)
}
Add-Scenario 'Add/error/読み取り専用' {
    $path = New-ByteFile 'add_ro.txt' ([byte[]](0x41, 0x0A))
    Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
    try { Add-ProbedContent -LiteralPath $path -Value 'B' -Encoding utf8NoBOM -ErrorAction Stop }
    finally {
        $script:Report.Add(("Add/読み取り専用/内容`tOK`t{0}" -f (Format-FileBytes $path)))
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false
    }
}

# エラー系（仕様書 8 節）
$script:addErrorIndex = 0
foreach ($name in @('utf8', '65001', 'utf7')) {
    $script:addErrorIndex++

    $fileName = 'adderr' + $script:addErrorIndex + '.txt'
    $encodingName = $name

    Add-Scenario "Add/error/語彙/$name" {
        Invoke-Add $fileName @{ Encoding = $encodingName } 'B' ([byte[]](0x41, 0x0A))
    }.GetNewClosure()
}
Add-Scenario 'Add/error/判定失敗' {
    Invoke-Add 'add_binary.txt' @{ Encoding = 'utf8NoBOM' } 'B' ([byte[]](0x81, 0xFF, 0x00, 0xFE, 0x93, 0x40, 0xC0, 0x80, 0xED, 0xA0, 0x80))
}
Add-Scenario 'Add/error/EncodingとEncodingFrom' {
    Invoke-Add 'add_both.txt' @{ Encoding = 'utf8NoBOM'; EncodingFrom = $script:refUtf16 } 'B' ([byte[]](0x41, 0x0A))
}

# ---------------------------------------------------------------------------
# 実行環境が提供していないコードページ
# ---------------------------------------------------------------------------

# 判定処理は .NET が提供していないコードページを返すことがある。
# ESC $ + I (CNS 11643 Plane 3) は ISO-2022-TW にしか現れないため、確実に 50229 と判定される。
# Encoding.GetEncoding(50229) は net10.0 / net48 のどちらでも NotSupportedException を投げる。
$script:twBytes = [byte[]](0x1B, 0x24, 0x2B, 0x49, 0x1B, 0x4E, 0x21, 0x21, 0x1B, 0x28, 0x42, 0x41, 0x0D, 0x0A)

Add-Scenario '扱えないCP/判定結果' {
    $path = New-ByteFile 'cp_tw.txt' $script:twBytes
    $info = Resolve-Encoding $path
    return ('cp={0} web={1}' -f $info.CodePage, $info.EncodingWebName)
}

Add-Scenario '扱えないCP/.NETから取得できるか' {
    try { $null = [System.Text.Encoding]::GetEncoding(50229); return '取得できた' }
    catch { return ('取得できない: ' + $_.Exception.InnerException.GetType().FullName) }
}

Add-Scenario '扱えないCP/error/読み取り' {
    $path = New-ByteFile 'cp_read.txt' $script:twBytes
    Get-ProbedContent -LiteralPath $path -ErrorAction Stop
}

Add-Scenario '扱えないCP/明示指定なら読める' {
    $path = New-ByteFile 'cp_read2.txt' $script:twBytes
    return (Format-Lines (Get-ProbedContent -LiteralPath $path -Encoding ascii -ErrorAction Stop))
}

Add-Scenario '扱えないCP/error/上書きで継承' {
    $path = New-ByteFile 'cp_set.txt' $script:twBytes
    try { Set-ProbedContent -LiteralPath $path -Value 'A' -ErrorAction Stop }
    finally { $script:Report.Add(("扱えないCP/上書き後のファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}

Add-Scenario '扱えないCP/error/追記' {
    $path = New-ByteFile 'cp_add.txt' $script:twBytes
    try { Add-ProbedContent -LiteralPath $path -Value 'A' -Encoding ascii -ErrorAction Stop }
    finally { $script:Report.Add(("扱えないCP/追記後のファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}

Add-Scenario '扱えないCP/error/EncodingFrom' {
    $reference = New-ByteFile 'cp_ref.txt' $script:twBytes
    $path = Join-Path $script:WorkRoot 'cp_from.txt'
    try { Set-ProbedContent -LiteralPath $path -Value 'A' -EncodingFrom $reference -ErrorAction Stop }
    finally { $script:Report.Add(("扱えないCP/EncodingFrom後のファイル`tOK`t{0}" -f (Format-FileBytes $path))) }
}

Add-Scenario '扱えないCP/error/ConvertTo' {
    $path = New-ByteFile 'cp_conv.txt' $script:twBytes
    Format-Encoding (Resolve-Encoding $path | ConvertTo-DotNetEncoding)
}

# ---------------------------------------------------------------------------
# -Culture / -Strategy (課題1・課題2)
# ---------------------------------------------------------------------------
#
# 判定を伴う経路はすべて -Culture / -Strategy を受け取る。
# ホストのカルチャーに依存しないことを示すため、比較する2通りはどちらも値を明示する。

# EUC-KR のバイト列は EUC-JP としても成立する。カルチャーだけが両者を分ける。
$script:koBytes = [System.Text.Encoding]::GetEncoding(51949).GetBytes("안녕하세요" + "`n")
$script:koCp949 = [System.Text.Encoding]::GetEncoding(949).GetBytes("안녕하세요" + "`n")
$script:twBig5  = [System.Text.Encoding]::GetEncoding(950).GetBytes("你好世界" + "`n")

Add-Scenario 'Culture/読み取り/ko-KR' {
    $path = New-ByteFile 'cul_ko.txt' $script:koBytes
    Format-Lines (Get-ProbedContent -LiteralPath $path -Culture ko-KR)
}

Add-Scenario 'Culture/読み取り/ja-JP' {
    $path = New-ByteFile 'cul_ja.txt' $script:koBytes
    Format-Lines (Get-ProbedContent -LiteralPath $path -Culture ja-JP)
}

Add-Scenario 'Culture/読み取り/zh-TW' {
    $path = New-ByteFile 'cul_tw.txt' $script:twBig5
    Format-Lines (Get-ProbedContent -LiteralPath $path -Culture zh-TW)
}

Add-Scenario 'Culture/Raw/ko-KR' {
    $path = New-ByteFile 'cul_ko_raw.txt' $script:koBytes
    Format-Text (Get-ProbedContent -LiteralPath $path -Culture ko-KR -Raw)
}

# windows-1252 のテキスト。独自判定は東アジアのマルチバイトを、
# UTF.Unknown は欧米のシングルバイトを担当するため、判定方式で結果が分かれる。
$script:latin1Bytes = [System.Text.Encoding]::GetEncoding(1252).GetBytes(
    'Rundfunk und Fernsehen. Grüße aus München und Köln. Straße, Fuß, Maß, schön.' + "`n")

Add-Scenario 'Strategy/UtfUnknownOnly' {
    $path = New-ByteFile 'str_uu.txt' $script:latin1Bytes
    Format-Lines (Get-ProbedContent -LiteralPath $path -Strategy UtfUnknownOnly)
}

Add-Scenario 'Strategy/NativeOnly' {
    $path = New-ByteFile 'str_na.txt' $script:latin1Bytes
    Format-Lines (Get-ProbedContent -LiteralPath $path -Culture ja-JP -Strategy NativeOnly)
}

Add-Scenario 'Strategy/Combined' {
    $path = New-ByteFile 'str_co.txt' $script:latin1Bytes
    Format-Lines (Get-ProbedContent -LiteralPath $path -Culture ja-JP -Strategy Combined)
}

# 判定方式の語彙は Resolve-Encoding と共通である
foreach ($alias in @('Combined', 'combined', 'default', '0',
                     'NativeOnly', 'native', '1',
                     'UtfUnknownOnly', 'utfunknown', '3')) {
    $aliasPath = New-ByteFile ("str_alias_" + $alias + '.txt') ([byte[]](0x41,0x42,0x0A))
    Add-Scenario "Strategy/語彙/$alias" {
        Format-Lines (Get-ProbedContent -LiteralPath $aliasPath -Strategy $alias)
    }.GetNewClosure()
}

# 不正な値はファイルを開く前に弾く。書き込み先が作られていないことも記録する。
Add-Scenario 'Culture/error/不正なカルチャー' {
    $path = New-ByteFile 'cul_bad.txt' ([byte[]](0x41,0x0A))
    Get-ProbedContent -LiteralPath $path -Culture 'not a culture!' -ErrorAction Stop
}

Add-Scenario 'Strategy/error/不正な判定方式' {
    $path = New-ByteFile 'str_bad.txt' ([byte[]](0x41,0x0A))
    Get-ProbedContent -LiteralPath $path -Strategy 'Nonexistent' -ErrorAction Stop
}

Add-Scenario 'Culture/error/書き込み前に失敗' {
    $path = Join-Path $script:WorkRoot 'cul_write_bad.txt'
    try { Set-ProbedContent -LiteralPath $path -Value 'X' -Encoding utf8NoBOM -Culture 'not a culture!' -ErrorAction Stop }
    finally { $script:Report.Add(("Culture/error/書き込み先の状態`tOK`t{0}" -f (Format-FileBytes $path))) }
}

Add-Scenario 'Strategy/error/書き込み前に失敗' {
    $path = Join-Path $script:WorkRoot 'str_write_bad.txt'
    try { Set-ProbedContent -LiteralPath $path -Value 'X' -Encoding utf8NoBOM -Strategy 'Nonexistent' -ErrorAction Stop }
    finally { $script:Report.Add(("Strategy/error/書き込み先の状態`tOK`t{0}" -f (Format-FileBytes $path))) }
}

# 書き込み系: -EncodingFrom と、書き込み先・追記先からの継承にも -Culture が効く
Add-Scenario 'Culture/EncodingFrom/ko-KR' {
    $reference = New-ByteFile 'cul_ref_ko.txt' $script:koBytes
    $path = Join-Path $script:WorkRoot 'cul_from_ko.txt'
    Set-ProbedContent -LiteralPath $path -Value '안녕하세요' -EncodingFrom $reference -Culture ko-KR -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'Culture/EncodingFrom/ja-JP' {
    $reference = New-ByteFile 'cul_ref_ja.txt' $script:koBytes
    $path = Join-Path $script:WorkRoot 'cul_from_ja.txt'
    Set-ProbedContent -LiteralPath $path -Value '안녕하세요' -EncodingFrom $reference -Culture ja-JP -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'Culture/上書き継承/ko-KR' {
    $path = New-ByteFile 'cul_set_ko.txt' $script:koBytes
    Set-ProbedContent -LiteralPath $path -Value '안녕하세요' -Culture ko-KR -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'Culture/追記継承/ko-KR' {
    $path = New-ByteFile 'cul_add_ko.txt' $script:koBytes
    Add-ProbedContent -LiteralPath $path -Value '안녕하세요' -Culture ko-KR -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'Culture/CP949期待値' { ($script:koCp949 | ForEach-Object { $_.ToString('X2') }) -join ' ' }


# ---------------------------------------------------------------------------
# 世界言語の判定 (1.2.0)
#
# 独自判定が担当するのは東アジア漢字文化圏の旧マルチバイトだけである。
# それ以外の言語のシングルバイトのテキストは UTF.Unknown が判定するため、
# 実行環境のカルチャーが何であっても同じ結果にならなければならない。
#
# とくに windows-1252 のドイツ語は Shift_JIS としても構造が成立してしまう
# (FC DF = "üß" が Shift_JIS の外字領域の 2 バイト文字になる) ため、
# 日本語カルチャーで Shift_JIS と誤判定していた。ここで回帰を固定する。
# ---------------------------------------------------------------------------
$script:worldSamples = @(
    [PSCustomObject]@{ Name = 'de_cp1252'; CodePage = 1252; Text = 'Grüße aus München. Die Straße ist für Fußgänger. Schöne Grüße, Herr Müller.' }
    [PSCustomObject]@{ Name = 'ru_cp1251'; CodePage = 1251; Text = 'Русский язык. Съешь ещё этих мягких французских булок, да выпей чаю.' }
    [PSCustomObject]@{ Name = 'pl_cp1250'; CodePage = 1250; Text = 'Zażółć gęślą jaźń. Pchnąć w tę łódź jeża lub ośm skrzyń fig.' }
    [PSCustomObject]@{ Name = 'th_cp874';  CodePage = 874;  Text = 'ภาษาไทย เป็นภาษาราชการของประเทศไทย และเป็นภาษาประจำชาติ' }
)

foreach ($worldSample in $script:worldSamples) {
    foreach ($worldCulture in @('de-DE', 'ru-RU', 'ja-JP', 'ko-KR', 'zh-CN', 'zh-TW', 'zh-HK', 'kok-IN')) {

        # 判定したコードページ。カルチャーが変わっても同じ値でなければならない。
        Add-Scenario ("World/{0}/{1}/CodePage" -f $worldSample.Name, $worldCulture) {
            $bytes = [System.Text.Encoding]::GetEncoding($worldSample.CodePage).GetBytes($worldSample.Text + "`n")
            $path = New-ByteFile ("world_{0}_{1}.txt" -f $worldSample.Name, $worldCulture) $bytes
            (Resolve-Encoding -Path $path -Culture $worldCulture).CodePage
        }.GetNewClosure()

        # 復号した本文。誤判定していれば文字化けするので、元の文字列と一致しなくなる。
        Add-Scenario ("World/{0}/{1}/復号" -f $worldSample.Name, $worldCulture) {
            $bytes = [System.Text.Encoding]::GetEncoding($worldSample.CodePage).GetBytes($worldSample.Text + "`n")
            $path = New-ByteFile ("world_dec_{0}_{1}.txt" -f $worldSample.Name, $worldCulture) $bytes
            $decoded = Get-ProbedContent -LiteralPath $path -Culture $worldCulture -Raw
            if ($decoded.TrimEnd("`r", "`n") -ceq $worldSample.Text) { '元の本文と一致' } else { Format-Text $decoded }
        }.GetNewClosure()
    }
}

# Unicode の判定はカルチャーに関わらず実行する。
# UTF.Unknown は BOM 無しの UTF-16 / UTF-32 に対応していないため、
# 「東アジア以外では独自判定を一切動かさない」という作りにはできない。
foreach ($unicodeCulture in @('de-DE', 'ru-RU', 'th-TH', 'ja-JP')) {
    Add-Scenario ("World/BOM無しUTF-16LE/{0}" -f $unicodeCulture) {
        $bytes = [System.Text.Encoding]::Unicode.GetBytes('Grüße aus München.' + "`n")
        $path = New-ByteFile ("world_u16_{0}.txt" -f $unicodeCulture) $bytes
        (Resolve-Encoding -Path $path -Culture $unicodeCulture).CodePage
    }.GetNewClosure()

    Add-Scenario ("World/BOM無しUTF-16BE/{0}" -f $unicodeCulture) {
        $bytes = [System.Text.Encoding]::BigEndianUnicode.GetBytes('Grüße aus München.' + "`n")
        $path = New-ByteFile ("world_u16be_{0}.txt" -f $unicodeCulture) $bytes
        (Resolve-Encoding -Path $path -Culture $unicodeCulture).CodePage
    }.GetNewClosure()
}

# UTF-8 判定は RFC 3629 の整形式バイト列だけを受け入れる。
# 後続バイトが足りないまま ASCII に戻る形 (cp1252 の "Français" など) を
# UTF-8 と誤判定していたのを 1.2.0 で直した。
$script:utf8Probes = @(
    [PSCustomObject]@{ Name = '正しい3バイト文字';       Bytes = [byte[]](0x41, 0xE3, 0x81, 0x82, 0x42, 0x0A) }
    [PSCustomObject]@{ Name = '後続バイト不足';           Bytes = [byte[]](0x46, 0x72, 0x61, 0x6E, 0xE7, 0x61, 0x69, 0x73, 0x0A) }
    [PSCustomObject]@{ Name = '終端で途切れる';           Bytes = [byte[]](0x41, 0xE3, 0x81) }
    [PSCustomObject]@{ Name = '冗長な2バイト文字';        Bytes = [byte[]](0xC0, 0x80, 0x0A) }
    [PSCustomObject]@{ Name = 'サロゲート符号位置';       Bytes = [byte[]](0xED, 0xA0, 0x80, 0x0A) }
    [PSCustomObject]@{ Name = 'U+10FFFF を超える';        Bytes = [byte[]](0xF4, 0x90, 0x80, 0x80, 0x0A) }
)

foreach ($utf8Probe in $script:utf8Probes) {
    Add-Scenario ("World/UTF-8厳密判定/{0}" -f $utf8Probe.Name) {
        $path = New-ByteFile ("world_u8_{0}.txt" -f ($utf8Probe.Name -replace '[^0-9A-Za-z]', '_')) $utf8Probe.Bytes
        (Resolve-Encoding -Path $path -Culture de-DE -Strategy NativeOnly).CodePage
    }.GetNewClosure()
}

# ---------------------------------------------------------------------------
# 香港 Big5 (1.2.0 第二次修正)
#
# - カルチャー名はサブタグに分解して判定する。zh-Hant-HK は PS 5.1 では
#   CultureInfo.Name が zh-HK に正規化されるが、判定には渡された名前がそのまま届く
# - 台湾 Big5 と香港 Big5 はバイト列から区別しない。HKSCS 固有字が入っても 950 / big5
# - 繁簡の系統が食い違ったら、UTF.Unknown の系統の投票で判定し直す
#   (香港・台湾カルチャーの簡体字 → 936、大陸カルチャーの繁体字 → 950)
# - 大陸カルチャーの HKSCS 入り Big5 は救済されない (既知の限界、54936 のまま)
# - HKSCS 固有字は私用領域として復号され、書き戻すと元のバイト列に戻る
# ---------------------------------------------------------------------------
$script:hkHant = '香港是一個國際大都會，粵語是香港人的主要語言。今天天氣很好，我們一起去飲茶吧。中文資訊處理需要正確的文字編碼判斷方法。'
$script:hkHans = '香港是一个国际大都会，粤语是香港人的主要语言。今天天气很好，我们一起去饮茶吧。中文信息处理需要正确的文字编码判断方法。'
$script:hkBig5 = [System.Text.Encoding]::GetEncoding(950).GetBytes($script:hkHant + "`r`n")
$script:hkGbk = [System.Text.Encoding]::GetEncoding(936).GetBytes($script:hkHans + "`r`n")
# HKSCS 固有領域の 4 文字 (88 62 / 8B F8 / FA 5F / FE 52) を改行の直前に挟む
$script:hkHkscs = [byte[]]($script:hkBig5[0..($script:hkBig5.Length - 3)] + [byte[]](0x88, 0x62, 0x8B, 0xF8, 0xFA, 0x5F, 0xFE, 0x52) + [byte[]](0x0D, 0x0A))

$script:hkSamples = @(
    [PSCustomObject]@{ Name = 'big5';  Bytes = $script:hkBig5 }
    [PSCustomObject]@{ Name = 'hkscs'; Bytes = $script:hkHkscs }
    [PSCustomObject]@{ Name = 'gbk';   Bytes = $script:hkGbk }
)

foreach ($hkSample in $script:hkSamples) {
    foreach ($hkCulture in @('zh-TW', 'zh-HK', 'zh-Hant-HK', 'zh-MO', 'yue-HK', 'zh-CN')) {
        Add-Scenario ("HongKong/{0}/{1}/Combined" -f $hkSample.Name, $hkCulture) {
            $path = New-ByteFile ("hk_{0}_{1}.txt" -f $hkSample.Name, $hkCulture) $hkSample.Bytes
            $info = Resolve-Encoding -Path $path -Culture $hkCulture
            '{0} / {1}' -f $info.CodePage, $info.EncodingWebName
        }.GetNewClosure()

        Add-Scenario ("HongKong/{0}/{1}/NativeOnly" -f $hkSample.Name, $hkCulture) {
            $path = New-ByteFile ("hk_n_{0}_{1}.txt" -f $hkSample.Name, $hkCulture) $hkSample.Bytes
            (Resolve-Encoding -Path $path -Culture $hkCulture -Strategy NativeOnly).CodePage
        }.GetNewClosure()
    }
}

# kok (コンカニ語) を韓国語と判定しない。1.2.0 より前は前方一致のため cp949 と判定していた
Add-Scenario 'Culture/kok-IN/韓国語の判定を行わない' {
    $path = New-ByteFile 'kok_cp949.txt' ([System.Text.Encoding]::GetEncoding(949).GetBytes('안녕하세요. 한국어 문장입니다.' + "`n"))
    (Resolve-Encoding -Path $path -Culture kok-IN -Strategy NativeOnly).CodePage
}

# HKSCS 固有字は例外も置換文字も出さずに私用領域へ写される
Add-Scenario 'HongKong/hkscs/私用領域の符号位置' {
    $path = New-ByteFile 'hk_pua.txt' $script:hkHkscs
    $text = Get-ProbedContent -LiteralPath $path -Culture zh-HK -Raw
    ($text.ToCharArray() | Where-Object { [int]$_ -ge 0xE000 -and [int]$_ -le 0xF8FF } |
        ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
}

# 加工せずに書き戻すと、私用領域を経由してもバイト列が保存される
Add-Scenario 'HongKong/hkscs/往復' {
    $source = New-ByteFile 'hk_rt_src.txt' $script:hkHkscs
    $destination = Join-Path $script:WorkRoot 'hk_rt_dst.txt'
    $text = Get-ProbedContent -LiteralPath $source -Culture zh-HK -Raw
    Set-ProbedContent -LiteralPath $destination -Value $text -EncodingFrom $source -Culture zh-HK -NoNewline -ErrorAction Stop
    if ((Format-FileBytes $destination) -eq (Format-FileBytes $source)) { 'バイト列が一致' } else { Format-FileBytes $destination }
}

# ---------------------------------------------------------------------------
# 香港ロケールのメッセージとヘルプ (1.2.0 第三次修正)
#
# UI カルチャーごとに、同じホストの子プロセスでモジュールを読み込み直して確かめる。
# ヘルプの言語は Import-Module の前に UI カルチャーを変えないと切り替わらないため。
# 子プロセスの結果は UTF-8 のファイルで受け取る (標準出力はコンソールのコードページに左右される)。
#
# - メッセージは判定のカルチャーゲートと同じ規則で言語を選ぶ。香港・マカオ・広東語は台湾と同じ繁体字 (B 案)
# - ヘルプは zh-HK / zh-MO フォルダーに zh-TW の複製を置いた。yue 系は en-US (既知の制限)
# - zh_HK のヘルプは両ホストで異なる (PS 7 は en-US)。ICU がカルチャー名を正規化しないためで、
#   zh-Hant-HK などと同じ別課題として扱う。ここではメッセージだけを比べる
# ---------------------------------------------------------------------------
$script:uiCultureChild = Join-Path $script:WorkRoot 'uiculture_child.ps1'
[IO.File]::WriteAllText($script:uiCultureChild, @'
param([string]$Dll, [string]$Culture, [string]$OutFile)
$ErrorActionPreference = 'Stop'
[System.Threading.Thread]::CurrentThread.CurrentUICulture = New-Object System.Globalization.CultureInfo($Culture)
Import-Module $Dll
try { ConvertTo-DotNetEncoding 'nonexistent-encoding' | Out-Null; $message = '(no error)' }
catch {
    $e = $_.Exception
    while ($null -ne $e.InnerException) { $e = $e.InnerException }
    $message = $e.Message
}
$synopsis = ((Get-Help Get-ProbedContent).Synopsis | Out-String).Trim()
[IO.File]::WriteAllLines($OutFile, [string[]]@($message, $synopsis), (New-Object System.Text.UTF8Encoding($false)))
'@, (New-Object System.Text.UTF8Encoding($true)))

$script:hostExecutable = (Get-Process -Id $PID).Path
$script:dllFullPath = (Resolve-Path -LiteralPath $Dll).Path

# 注意: 変数名は大文字小文字を区別しない。$outFile のような名前はスクリプトの -OutFile を上書きしてしまう
foreach ($uiCulture in @('zh-HK', 'zh-MO', 'yue-HK', 'zh_HK')) {
    $uiCultureResult = Join-Path $script:WorkRoot ("uiculture_{0}.txt" -f $uiCulture)
    & $script:hostExecutable -NoProfile -ExecutionPolicy Bypass -File $script:uiCultureChild `
        -Dll $script:dllFullPath -Culture $uiCulture -OutFile $uiCultureResult
    $lines = if (Test-Path -LiteralPath $uiCultureResult) { [IO.File]::ReadAllLines($uiCultureResult) } else { @('(結果なし)', '(結果なし)') }

    $script:Report.Add(("UICulture/{0}/メッセージ`tOK`t{1}" -f $uiCulture, $lines[0]))
    if ($uiCulture -ne 'zh_HK') {
        $script:Report.Add(("UICulture/{0}/Get-Help`tOK`t{1}" -f $uiCulture, $lines[1]))
    }
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