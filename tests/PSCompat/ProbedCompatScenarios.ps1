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

# フレンドリ名の無いシングルバイト系の PSEncodingName は null (1.2.0 手動確認後の修正)。
# 1.2.0 より前は PS 7.x (net10.0 ビルド) でだけ "I do not know." が入っていた。
# shift_jis などの独自判定の結果は、PS 5.1 (null) と 7.x (WebName) で違うのが仕様なので対象にしない。
$script:psNameSamples = @(
    [PSCustomObject]@{ Name = 'it_cp1252'; Culture = 'it-IT'; Bytes = [System.Text.Encoding]::GetEncoding(1252).GetBytes("Italiano`nPranzo d'acqua fa volti sghembi.`nPerch$([char]0xE9) $([char]0xE8) cos$([char]0xEC)? Qual $([char]0xE8) la citt$([char]0xE0) pi$([char]0xF9) bella? Niente po' po' di meno!`nPrezzo: 1.234,56 $([char]0x20AC) $([char]0x2014) .`n") }
    [PSCustomObject]@{ Name = 'de_latin1'; Culture = 'de-DE'; Bytes = [System.Text.Encoding]::GetEncoding(28591).GetBytes('Grüße aus München. Die Straße ist für Fußgänger. Schöne Grüße, Herr Müller.' + "`n") }
    [PSCustomObject]@{ Name = 'ru_cp1251'; Culture = 'ja-JP'; Bytes = [System.Text.Encoding]::GetEncoding(1251).GetBytes('Русский язык. Съешь ещё этих мягких французских булок, да выпей чаю.' + "`n") }
)

foreach ($psNameSample in $script:psNameSamples) {
    Add-Scenario ("World/PSEncodingName/{0}" -f $psNameSample.Name) {
        $path = New-ByteFile ("psname_{0}.txt" -f $psNameSample.Name) $psNameSample.Bytes
        $info = Resolve-Encoding -Path $path -Culture $psNameSample.Culture
        $psName = if ($null -eq $info.PSEncodingName) { '(null)' } else { "'" + $info.PSEncodingName + "'" }
        'cp={0} PSEncodingName={1} UsePSName={2}' -f $info.CodePage, $psName, $info.UsePSName
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
# Out-ProbedFile (1.2.0 仕様書 第 1 部)
#
# 符号化の層（エンコーディング・BOM・改行）は両ホストで同じバイト列になることを確かめる。
# 整形の層（表・一覧）はホストによって異なるのが正しいため、値そのものではなく
# 「同じホストの Out-String -Stream の各要素 + 改行と一致するか」を記録する。
# 終了エラーは FullyQualifiedErrorId とメッセージで記録する（PowerShell 自身の文言は含めない）。
# ---------------------------------------------------------------------------

<#
.SYNOPSIS
    スクリプトブロックを実行し、終了エラーなら「ID: メッセージ」、エラーが無ければ NO-ERROR を返す。
#>
function Get-TerminatingError {
    param([scriptblock]$Body)

    try {
        & $Body
        return 'NO-ERROR'
    }
    catch {
        return ('{0}: {1}' -f $_.FullyQualifiedErrorId.Split(',')[0], (Get-InnermostMessage $_.Exception))
    }
}

<#
.SYNOPSIS
    スクリプトブロックを実行し、終了エラーなら ID だけを返す（PowerShell 自身のメッセージを含むもの用）。
#>
function Get-TerminatingErrorId {
    param([scriptblock]$Body)

    try {
        & $Body
        return 'NO-ERROR'
    }
    catch {
        return $_.FullyQualifiedErrorId.Split(',')[0]
    }
}

function Test-ReadOnly {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return 'absent' }
    return [bool]((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReadOnly)
}

function Clear-ReadOnly {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        $item.Attributes = $item.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly)
    }
}

$outDir = Join-Path $script:WorkRoot 'out'
$null = New-Item -ItemType Directory -Path $outDir

foreach ($outName in @(
    'utf8NoBOM', 'utf8BOM', 'unicodeNoBOM', 'unicodeBOM', 'bigendianunicodeNoBOM', 'bigendianunicodeBOM',
    'utf32NoBOM', 'utf32BOM', 'bigendianutf32NoBOM', 'bigendianutf32BOM', 'shift_jis', '932', 'euc-jp', 'ascii')) {

    Add-Scenario "OutProbedFile/vocabulary/$outName" {
        $path = Join-Path $outDir ("vocab_{0}.txt" -f $outName)
        'Aあ' | Out-ProbedFile -LiteralPath $path -Encoding $outName -LineBreak Lf -ErrorAction Stop
        Format-FileBytes $path
    }.GetNewClosure()
}

foreach ($outName in @('utf8', 'utf-8', '65001', 'utf7', 'utf-16', 'shift_jisBOM')) {
    Add-Scenario "OutProbedFile/vocabulary/rejected/$outName" {
        $path = Join-Path $outDir ("rejected_{0}.txt" -f $outName)
        'A' | Out-ProbedFile -LiteralPath $path -Encoding $outName
        Format-FileBytes $path
    }.GetNewClosure()
}

Add-Scenario 'OutProbedFile/vocabulary/EncodingInstance-UTF8' {
    $path = Join-Path $outDir 'instance.txt'
    'A' | Out-ProbedFile -LiteralPath $path -Encoding ([System.Text.Encoding]::UTF8) -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'OutProbedFile/vocabulary/EncodingInformation' {
    $reference = New-ByteFile 'out_ref_lf.txt' ([byte[]](0xFF, 0xFE, 0x78, 0x00, 0x0A, 0x00))
    $path = Join-Path $outDir 'information.txt'
    'A', 'B' | Out-ProbedFile -LiteralPath $path -Encoding (Resolve-Encoding -Path $reference)
    Format-FileBytes $path
}

# -Encoding の決定（仕様書 2.5 の表）
$script:outUtf16 = [byte[]](0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00)
foreach ($outCase in @(
    @{ Name = 'omitted/overwrite/missing'; Existing = $null;            Parameters = @{} },
    @{ Name = 'omitted/overwrite/empty';   Existing = [byte[]]@();      Parameters = @{} },
    @{ Name = 'omitted/overwrite/utf16';   Existing = $script:outUtf16; Parameters = @{} },
    @{ Name = 'omitted/append/missing';    Existing = $null;            Parameters = @{ Append = $true } },
    @{ Name = 'omitted/append/empty';      Existing = [byte[]]@();      Parameters = @{ Append = $true } },
    @{ Name = 'omitted/append/utf16';      Existing = $script:outUtf16; Parameters = @{ Append = $true } },
    @{ Name = 'auto/overwrite/missing';    Existing = $null;            Parameters = @{ Encoding = 'Auto' } },
    @{ Name = 'auto/overwrite/empty';      Existing = [byte[]]@();      Parameters = @{ Encoding = 'Auto'; LineBreak = 'Lf' } },
    @{ Name = 'auto/overwrite/utf16';      Existing = $script:outUtf16; Parameters = @{ Encoding = 'Auto' } },
    @{ Name = 'auto/append/missing';       Existing = $null;            Parameters = @{ Encoding = 'Auto'; Append = $true } },
    @{ Name = 'auto/append/empty';         Existing = [byte[]]@();      Parameters = @{ Encoding = 'Auto'; Append = $true; LineBreak = 'Lf' } },
    @{ Name = 'auto/append/utf16';         Existing = $script:outUtf16; Parameters = @{ Encoding = 'Auto'; Append = $true } })) {

    Add-Scenario ("OutProbedFile/encoding/{0}" -f $outCase.Name) {
        $path = Join-Path $outDir (($outCase.Name -replace '/', '_') + '.txt')
        if ($null -ne $outCase.Existing) { [IO.File]::WriteAllBytes($path, $outCase.Existing) }
        $parameters = $outCase.Parameters
        $result = Get-TerminatingError { 'B' | Out-ProbedFile -LiteralPath $path @parameters -ErrorAction Stop }
        '{0} | {1}' -f $result, (Format-FileBytes $path)
    }.GetNewClosure()
}

# 改行（仕様書 2.6。実測報告 5 章の 8-1〜8-6）
foreach ($outLb in @('CrLf', 'Lf', 'Cr')) {
    Add-Scenario "OutProbedFile/linebreak/inside-strings/$outLb" {
        $path = Join-Path $outDir ("inside_{0}.txt" -f $outLb)
        "a`nb", "a`r`nb", "a`rb", "a`n", "a`r`n`r`nb", 'c' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -LineBreak $outLb
        Format-FileBytes $path
    }.GetNewClosure()
}

Add-Scenario 'OutProbedFile/linebreak/NoNewline' {
    $path = Join-Path $outDir 'nonewline.txt'
    'a', '', 'b' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -NoNewline
    Format-FileBytes $path
}

Add-Scenario 'OutProbedFile/linebreak/EncodingFrom-Cr' {
    $reference = New-ByteFile 'out_ref_cr.txt' ([byte[]](0x78, 0x0D))
    $path = Join-Path $outDir 'from_cr.txt'
    'A', 'B' | Out-ProbedFile -LiteralPath $path -EncodingFrom $reference
    Format-FileBytes $path
}

# -Append の整合性検査（仕様書 2.8）
Add-Scenario 'OutProbedFile/append/mismatch-keeps-earlier-lines' {
    $path = New-ByteFile 'out_append_sjis.txt' ([byte[]](0x82, 0xA0, 0x0A))
    $result = Get-TerminatingError { 'x', 'い', 'y' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -Append -Culture ja-JP -LineBreak Lf -ErrorAction Stop }
    '{0} | {1}' -f $result, (Format-FileBytes $path)
}

Add-Scenario 'OutProbedFile/append/AllowEncodingChange' {
    $path = New-ByteFile 'out_append_allow.txt' ([byte[]](0x82, 0xA0, 0x0A))
    'い' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -Append -AllowEncodingChange -Culture ja-JP -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'OutProbedFile/append/AllowEncodingChange-without-Append-warns' {
    $path = Join-Path $outDir 'allow_warn.txt'
    'A' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -AllowEncodingChange -WarningVariable warnings -WarningAction SilentlyContinue
    '{0} | {1}' -f ($warnings -join ' / '), (Format-FileBytes $path)
}

# -NoClobber / -Force / 読み取り専用（実測報告 3-1〜3-10）
foreach ($outCombo in @(
    @{ Id = '3-1';  Exists = $false; ReadOnly = $false; Parameters = @{ NoClobber = $true } },
    @{ Id = '3-2';  Exists = $true;  ReadOnly = $false; Parameters = @{ NoClobber = $true } },
    @{ Id = '3-3';  Exists = $true;  ReadOnly = $false; Parameters = @{ Append = $true; NoClobber = $true } },
    @{ Id = '3-4';  Exists = $false; ReadOnly = $false; Parameters = @{ Append = $true; NoClobber = $true } },
    @{ Id = '3-5';  Exists = $true;  ReadOnly = $false; Parameters = @{ Force = $true; NoClobber = $true } },
    @{ Id = '3-6';  Exists = $true;  ReadOnly = $true;  Parameters = @{} },
    @{ Id = '3-7';  Exists = $true;  ReadOnly = $true;  Parameters = @{ Force = $true } },
    @{ Id = '3-8';  Exists = $true;  ReadOnly = $true;  Parameters = @{ Force = $true; NoClobber = $true } },
    @{ Id = '3-9';  Exists = $true;  ReadOnly = $true;  Parameters = @{ Append = $true } },
    @{ Id = '3-10'; Exists = $true;  ReadOnly = $true;  Parameters = @{ Append = $true; Force = $true } })) {

    Add-Scenario ("OutProbedFile/combination/{0}" -f $outCombo.Id) {
        $path = Join-Path $outDir ("combo_{0}.txt" -f $outCombo.Id)
        if ($outCombo.Exists) { [IO.File]::WriteAllBytes($path, [byte[]](0xFF, 0xFE, 0x4F, 0x00, 0x4C, 0x00, 0x44, 0x00, 0x0D, 0x00, 0x0A, 0x00)) }
        if ($outCombo.ReadOnly) { (Get-Item -LiteralPath $path).Attributes = 'ReadOnly' }
        $parameters = $outCombo.Parameters
        try {
            $result = Get-TerminatingError { 'NEW' | Out-ProbedFile -LiteralPath $path -Encoding unicodeBOM -LineBreak CrLf @parameters -ErrorAction Stop }
            '{0} | {1} | readonly={2}' -f $result, (Format-FileBytes $path), (Test-ReadOnly $path)
        }
        finally {
            Clear-ReadOnly $path
        }
    }.GetNewClosure()
}

# パス（実測報告 4-1〜4-9）
Add-Scenario 'OutProbedFile/path/4-1-wildcard-one' {
    $dir = Join-Path $script:WorkRoot 'out_p41'; $null = New-Item -ItemType Directory -Path $dir
    [IO.File]::WriteAllText((Join-Path $dir 'a1.txt'), 'OLD')
    'NEW' | Out-ProbedFile -FilePath (Join-Path $dir 'a?.txt') -Encoding utf8NoBOM -LineBreak Lf
    Format-FileBytes (Join-Path $dir 'a1.txt')
}

Add-Scenario 'OutProbedFile/path/4-2-wildcard-two' {
    $dir = Join-Path $script:WorkRoot 'out_p42'; $null = New-Item -ItemType Directory -Path $dir
    [IO.File]::WriteAllText((Join-Path $dir 'a1.txt'), 'OLD')
    [IO.File]::WriteAllText((Join-Path $dir 'a2.txt'), 'OLD')
    Get-TerminatingError { 'NEW' | Out-ProbedFile (Join-Path $dir 'a?.txt') -Encoding utf8NoBOM -ErrorAction Stop }
}

foreach ($outPattern in @('b*.txt', 'c[1].txt')) {
    Add-Scenario "OutProbedFile/path/wildcard-none/$outPattern" {
        $dir = Join-Path $WorkRoot ('out_none_' + ($outPattern -replace '[^a-z]', ''))
        $null = New-Item -ItemType Directory -Path $dir
        $result = Get-TerminatingError { 'NEW' | Out-ProbedFile (Join-Path $dir $outPattern) -Encoding utf8NoBOM -ErrorAction Stop }
        '{0} | files={1}' -f $result, @(Get-ChildItem -LiteralPath $dir).Count
    }.GetNewClosure()
}

Add-Scenario 'OutProbedFile/path/4-5-literal-brackets' {
    $path = Join-Path $outDir 'c[1].txt'
    'NEW' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'OutProbedFile/path/4-7-missing-parent' {
    Get-TerminatingError { 'NEW' | Out-ProbedFile -LiteralPath (Join-Path $outDir 'missing\x.txt') -Encoding utf8NoBOM -ErrorAction Stop }
}

Add-Scenario 'OutProbedFile/path/4-8-directory' {
    Get-TerminatingError { 'NEW' | Out-ProbedFile -LiteralPath $outDir -Encoding utf8NoBOM -ErrorAction Stop }
}

Add-Scenario 'OutProbedFile/path/4-9-empty-string' {
    Get-TerminatingErrorId { 'NEW' | Out-ProbedFile -FilePath '' -Encoding utf8NoBOM -ErrorAction Stop }
}

foreach ($outAlias in @('Path', 'LP', 'PSPath')) {
    Add-Scenario "OutProbedFile/path/alias/$outAlias" {
        $path = Join-Path $outDir ("alias_{0}.txt" -f $outAlias)
        $parameters = @{ $outAlias = $path }
        'A' | Out-ProbedFile @parameters -Encoding utf8NoBOM -LineBreak Lf
        Format-FileBytes $path
    }.GetNewClosure()
}

Add-Scenario 'OutProbedFile/path/parameter-metadata' {
    $command = Get-Command Out-ProbedFile
    $literal = $command.Parameters['LiteralPath']
    '{0} | {1} | {2} | {3}' -f `
        (($command.Parameters['FilePath'].Aliases | Sort-Object) -join ','),
        (($literal.Aliases | Sort-Object) -join ','),
        (@($literal.Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] -and ($_.ValueFromPipeline -or $_.ValueFromPipelineByPropertyName) }).Count),
        $command.DefaultParameterSet
}

# 整形（仕様書 2.2 / 2.3）。文字列は両ホストで同一、表は同じホストの Out-String -Stream と一致
Add-Scenario 'OutProbedFile/format/strings' {
    $path = Join-Path $outDir 'strings.txt'
    'alpha', 'beta', ('x' * 253), 42, 'あいう' | Out-ProbedFile -LiteralPath $path -Encoding utf8BOM -Width 40 -LineBreak Lf
    Format-FileBytes $path
}

foreach ($outWidth in @(0, 40, 200)) {
    Add-Scenario "OutProbedFile/format/table-matches-host-Out-String/$outWidth" {
        $path = Join-Path $outDir ("table_{0}.txt" -f $outWidth)
        $data = @(
            [pscustomobject]@{ Name = 'Apple'; Count = 1; Note = 'red' },
            [pscustomobject]@{ Name = 'Banana'; Count = 22; Note = ('y' * 300) })
        $widthParameter = if ($outWidth -gt 0) { @{ Width = $outWidth } } else { @{} }
        $data | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM @widthParameter
        $expected = (($data | Out-String -Stream @widthParameter) | ForEach-Object { $_ + [Environment]::NewLine }) -join ''
        if ([IO.File]::ReadAllText($path) -ceq $expected) { '一致' } else { '不一致' }
    }.GetNewClosure()
}

# 空の入力（仕様書 2.11）
foreach ($outEmpty in @(
    @{ Name = 'none/overwrite-existing'; Script = { param($p) @() | Out-ProbedFile -LiteralPath $p -Encoding unicodeBOM }; Existing = $true },
    @{ Name = 'none/append-existing';    Script = { param($p) @() | Out-ProbedFile -LiteralPath $p -Encoding unicodeBOM -Append }; Existing = $true },
    @{ Name = 'none/append-missing';     Script = { param($p) @() | Out-ProbedFile -LiteralPath $p -Encoding unicodeBOM -Append }; Existing = $false },
    @{ Name = 'null';                    Script = { param($p) $null | Out-ProbedFile -LiteralPath $p -Encoding unicodeBOM }; Existing = $false },
    @{ Name = 'null-InputObject';        Script = { param($p) Out-ProbedFile -InputObject $null -LiteralPath $p -Encoding unicodeBOM }; Existing = $false },
    @{ Name = 'empty-string';            Script = { param($p) '' | Out-ProbedFile -LiteralPath $p -Encoding unicodeBOM -LineBreak CrLf }; Existing = $false })) {

    Add-Scenario ("OutProbedFile/empty/{0}" -f $outEmpty.Name) {
        $path = Join-Path $outDir (($outEmpty.Name -replace '[/-]', '_') + '.txt')
        if ($outEmpty.Existing) { [IO.File]::WriteAllText($path, 'OLD') }
        & $outEmpty.Script $path
        Format-FileBytes $path
    }.GetNewClosure()
}

# タイミング（仕様書 2.10 / 2.14）
Add-Scenario 'OutProbedFile/timing/same-path-round-trip' {
    $path = New-ByteFile 'out_roundtrip.txt' ([byte[]](0x61, 0x0A, 0x62, 0x0A))
    $result = Get-TerminatingError { Get-ProbedContent -LiteralPath $path | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -ErrorAction Stop }
    '{0} | {1}' -f $result, (Format-FileBytes $path)
}

Add-Scenario 'OutProbedFile/timing/WhatIf' {
    $path = Join-Path $outDir 'whatif.txt'
    'A' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -WhatIf 6>$null
    Format-FileBytes $path
}

Add-Scenario 'OutProbedFile/error/Width' {
    Get-TerminatingErrorId { 'A' | Out-ProbedFile -LiteralPath (Join-Path $outDir 'w.txt') -Encoding utf8NoBOM -Width 1 -ErrorAction Stop }
}

Add-Scenario 'OutProbedFile/error/EncodingAndEncodingFrom' {
    Get-TerminatingError { 'A' | Out-ProbedFile -LiteralPath (Join-Path $outDir 'ef.txt') -Encoding utf8NoBOM -EncodingFrom $outDir -ErrorAction Stop }
}

# ---------------------------------------------------------------------------
# Convert-ProbedContent (1.2.0 仕様書 第 2 部)
# ---------------------------------------------------------------------------

<#
.SYNOPSIS
    非終了エラーを「ID: メッセージ」の形で連結して返す。
#>
function Format-ErrorRecords {
    param([object[]]$Records)

    if ($null -eq $Records -or $Records.Count -eq 0) { return 'no-errors' }
    return (($Records | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) {
            '{0}: {1}' -f $_.FullyQualifiedErrorId.Split(',')[0], (Get-InnermostMessage $_.Exception)
        }
        else {
            [string]$_
        }
    }) -join ' / ')
}

function Format-ConvertResult {
    param([object[]]$Results)

    return (($Results | ForEach-Object {
        '{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}' -f $_.PSObject.TypeNames[0], $_.SourceEncoding, $_.SourceCodePage, $_.Encoding, $_.SourceLineBreak, $_.LineBreak, $_.Changed, ($_.Destination -eq $_.Path)
    }) -join ' / ')
}

$convertDir = Join-Path $script:WorkRoot 'convert'
$null = New-Item -ItemType Directory -Path $convertDir

function New-ConvertFile {
    param([string]$Name, [byte[]]$Bytes)

    $path = Join-Path $convertDir $Name
    [IO.File]::WriteAllBytes($path, $Bytes)
    return $path
}

$script:convSjis = [byte[]](0x82, 0xA0, 0x0D, 0x0A, 0x82, 0xA2, 0x0A)

foreach ($convCase in @(
    @{ Name = 'sjis-to-utf8BOM-lf';     Bytes = $script:convSjis; Parameters = @{ Encoding = 'utf8BOM'; LineBreak = 'Lf'; Culture = 'ja-JP' } },
    @{ Name = 'sjis-to-unicodeBOM';     Bytes = $script:convSjis; Parameters = @{ Encoding = 'unicodeBOM'; Culture = 'ja-JP' } },
    @{ Name = 'sjis-to-eucjp-crlf';     Bytes = $script:convSjis; Parameters = @{ Encoding = 'euc-jp'; LineBreak = 'CrLf'; Culture = 'ja-JP' } },
    @{ Name = 'utf8bom-bom-remove';     Bytes = [byte[]](0xEF, 0xBB, 0xBF, 0x41, 0x0A); Parameters = @{ Bom = 'Remove' } },
    @{ Name = 'utf8-bom-add';           Bytes = [byte[]](0xE3, 0x81, 0x82, 0x0A); Parameters = @{ Bom = 'Add'; SourceEncoding = 'utf8' } },
    @{ Name = 'utf16-bom-remove';       Bytes = [byte[]](0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00); Parameters = @{ Bom = 'Remove' } },
    @{ Name = 'bare-utf8-with-bom';     Bytes = [byte[]](0x41, 0x0A); Parameters = @{ Encoding = 'utf8'; Bom = 'Add' } },
    @{ Name = 'webname-utf16-remove';   Bytes = [byte[]](0x41, 0x0A); Parameters = @{ Encoding = 'utf-16'; Bom = 'Remove' } },
    @{ Name = 'bare-unicode';           Bytes = [byte[]](0x41, 0x0A); Parameters = @{ Encoding = 'unicode' } },
    @{ Name = 'mixed-linebreaks-lf';    Bytes = [byte[]](0x61, 0x0D, 0x0A, 0x62, 0x0A, 0x63, 0x0D, 0x64); Parameters = @{ LineBreak = 'Lf' } },
    @{ Name = 'keep-trailing-none';     Bytes = [byte[]](0x61, 0x0A, 0x62); Parameters = @{ LineBreak = 'CrLf' } },
    @{ Name = 'u2028-not-a-linebreak';  Bytes = [byte[]](0x61, 0xE2, 0x80, 0xA8, 0x62, 0x0A); Parameters = @{ LineBreak = 'CrLf'; SourceEncoding = 'utf8' } },
    @{ Name = 'sjis-bom-remove-noop';   Bytes = [byte[]](0x82, 0xA0); Parameters = @{ Bom = 'Remove'; SourceEncoding = 'shift_jis' } },
    @{ Name = 'unchanged';              Bytes = [byte[]](0x61, 0x0A); Parameters = @{ LineBreak = 'Lf' } },
    @{ Name = 'empty-file';             Bytes = [byte[]]@(); Parameters = @{ Encoding = 'utf8BOM' } })) {

    Add-Scenario ("ConvertProbedContent/convert/{0}" -f $convCase.Name) {
        $path = New-ConvertFile ($convCase.Name + '.txt') $convCase.Bytes
        $parameters = $convCase.Parameters
        $results = @(Convert-ProbedContent -LiteralPath $path @parameters -PassThru -ErrorVariable errors -ErrorAction SilentlyContinue)
        '{0} | {1} | {2}' -f (Format-FileBytes $path), (Format-ConvertResult $results), (Format-ErrorRecords $errors)
    }.GetNewClosure()
}

# 終了エラー（仕様書 18.1）
foreach ($convError in @(
    @{ Id = 'E1';  Parameters = @{} },
    @{ Id = 'E1b'; Parameters = @{ LineBreak = 'Auto' } },
    @{ Id = 'E2';  Parameters = @{ Encoding = 'Auto' } },
    @{ Id = 'E4';  Parameters = @{ Encoding = 'utf8' } },
    @{ Id = 'E4b'; Parameters = @{ Encoding = 'utf-16' } },
    @{ Id = 'E5';  Parameters = @{ Encoding = 'utf7'; Bom = 'Remove' } },
    @{ Id = 'E6';  Parameters = @{ Encoding = 'utf8NoBOM'; Bom = 'Add' } },
    @{ Id = 'E7';  Parameters = @{ Encoding = 'shift_jis'; Bom = 'Add' } },
    @{ Id = 'E10'; Parameters = @{ LineBreak = 'Lf'; Culture = 'no-such-culture-xx' } })) {

    Add-Scenario ("ConvertProbedContent/error/{0}" -f $convError.Id) {
        $path = New-ConvertFile ('err_{0}.txt' -f $convError.Id) ([byte[]](0x41, 0x0D, 0x0A))
        $parameters = $convError.Parameters
        $result = Get-TerminatingError { Convert-ProbedContent -LiteralPath $path @parameters -ErrorAction Stop }
        '{0} | {1}' -f $result, (Format-FileBytes $path)
    }.GetNewClosure()
}

Add-Scenario 'ConvertProbedContent/error/E3' {
    $path = New-ConvertFile 'err_E3.txt' ([byte[]](0x41))
    Get-TerminatingError { Convert-ProbedContent -LiteralPath $path -Encoding utf8NoBOM -EncodingFrom $path -ErrorAction Stop }
}

Add-Scenario 'ConvertProbedContent/error/E8' {
    $path = New-ConvertFile 'err_E8.txt' ([byte[]](0x41))
    Get-TerminatingError { Convert-ProbedContent -LiteralPath $path -Encoding ([System.Text.Encoding]::UTF8) -Bom Remove -ErrorAction Stop }
}

Add-Scenario 'ConvertProbedContent/error/E9' {
    $path = New-ConvertFile 'err_E9.txt' ([byte[]](0x41))
    Get-TerminatingError { Convert-ProbedContent -LiteralPath $path -EncodingFrom (Join-Path $convertDir 'missing.txt') -ErrorAction Stop }
}

Add-Scenario 'ConvertProbedContent/error/E11' {
    $path = New-ConvertFile 'err_E11.txt' ([byte[]](0x41))
    Get-TerminatingError { Convert-ProbedContent -LiteralPath $path -LineBreak Lf -Destination (Join-Path $convertDir 'missing') -ErrorAction Stop }
}

# 非終了エラー（仕様書 18.2）。そのファイルは変換せず、元のまま残す
foreach ($convFailure in @(
    @{ Id = 'N3-invalid-utf8';      Bytes = [byte[]](0x61, 0xE9, 0x62, 0x0A); Parameters = @{ SourceEncoding = 'utf8'; Encoding = 'utf8BOM' } },
    @{ Id = 'N3-truncated-utf8';    Bytes = [byte[]](0x61, 0x62, 0xE3, 0x81); Parameters = @{ SourceEncoding = 'utf8'; Encoding = 'utf8BOM' } },
    @{ Id = 'N3-invalid-sjis';      Bytes = [byte[]](0x61, 0x82, 0xA0, 0x82, 0x0A); Parameters = @{ SourceEncoding = 'shift_jis'; LineBreak = 'Lf' } },
    @{ Id = 'N3-invalid-eucjp';     Bytes = [byte[]](0x61, 0xA4, 0xA2, 0xA4, 0x41); Parameters = @{ SourceEncoding = 'euc-jp'; LineBreak = 'Lf' } },
    @{ Id = 'N4-emoji-to-sjis';     Bytes = [byte[]](0x61, 0x62, 0x0D, 0x0A, 0x63, 0xF0, 0x9F, 0x98, 0x80, 0x64, 0x0A); Parameters = @{ SourceEncoding = 'utf8'; Encoding = 'shift_jis' } },
    @{ Id = 'N4-hangul-to-sjis';    Bytes = [byte[]](0xEA, 0xB0, 0x80, 0x0A); Parameters = @{ SourceEncoding = 'utf8'; Encoding = 'shift_jis' } },
    @{ Id = 'N5-bom-add-sjis';      Bytes = [byte[]](0x82, 0xA0); Parameters = @{ Bom = 'Add'; SourceEncoding = 'shift_jis' } })) {

    Add-Scenario ("ConvertProbedContent/failure/{0}" -f $convFailure.Id) {
        $path = New-ConvertFile ($convFailure.Id + '.txt') $convFailure.Bytes
        $parameters = $convFailure.Parameters
        Convert-ProbedContent -LiteralPath $path @parameters -ErrorVariable errors -ErrorAction SilentlyContinue
        '{0} | {1}' -f (Format-ErrorRecords $errors), (Format-FileBytes $path)
    }.GetNewClosure()
}

Add-Scenario 'ConvertProbedContent/failure/N1-missing-and-wildcard' {
    Convert-ProbedContent (Join-Path $convertDir 'missing.txt'), (Join-Path $convertDir 'zz*.txt') -LineBreak Lf -ErrorVariable errors -ErrorAction SilentlyContinue
    Format-ErrorRecords $errors
}

Add-Scenario 'ConvertProbedContent/failure/N6-readonly' {
    $path = New-ConvertFile 'n6.txt' ([byte[]](0x61, 0x0D, 0x0A))
    (Get-Item -LiteralPath $path).Attributes = 'ReadOnly'
    try {
        Convert-ProbedContent -LiteralPath $path -LineBreak Lf -ErrorVariable errors -ErrorAction SilentlyContinue
        $denied = '{0} | {1}' -f (Format-ErrorRecords $errors), (Format-FileBytes $path)
        Convert-ProbedContent -LiteralPath $path -LineBreak Lf -Force
        '{0} | forced: {1} readonly={2}' -f $denied, (Format-FileBytes $path), (Test-ReadOnly $path)
    }
    finally {
        Clear-ReadOnly $path
    }
}

Add-Scenario 'ConvertProbedContent/destination/N7-N8-N9' {
    $root = Join-Path $convertDir 'dest'
    foreach ($sub in 'x', 'y', 'out') { $null = New-Item -ItemType Directory -Path (Join-Path $root $sub) }
    [IO.File]::WriteAllBytes((Join-Path $root 'x\a.txt'), [byte[]](0x66, 0x0D, 0x0A))
    [IO.File]::WriteAllBytes((Join-Path $root 'y\a.txt'), [byte[]](0x73, 0x0D, 0x0A))
    [IO.File]::WriteAllBytes((Join-Path $root 'out\b.txt'), [byte[]](0x4F))
    [IO.File]::WriteAllBytes((Join-Path $root 'x\b.txt'), [byte[]](0x62, 0x0D, 0x0A))

    $results = @(Get-ChildItem -LiteralPath (Join-Path $root 'x'), (Join-Path $root 'y') -File |
        Convert-ProbedContent -LineBreak Lf -Destination (Join-Path $root 'out') -PassThru -ErrorVariable errors -ErrorAction SilentlyContinue)
    $same = @(Convert-ProbedContent -LiteralPath (Join-Path $root 'x\a.txt') -LineBreak Lf -Destination (Join-Path $root 'x') -ErrorVariable sameErrors -ErrorAction SilentlyContinue)
    '{0} | {1} | {2} | out\a={3} | out\b={4}' -f (Format-ConvertResult $results), (Format-ErrorRecords $errors), (Format-ErrorRecords $sameErrors),
        (Format-FileBytes (Join-Path $root 'out\a.txt')), (Format-FileBytes (Join-Path $root 'out\b.txt'))
}

Add-Scenario 'ConvertProbedContent/pipeline/Get-ChildItem-skips-directories' {
    $root = Join-Path $convertDir 'gci'
    $null = New-Item -ItemType Directory -Path (Join-Path $root 'sub')
    [IO.File]::WriteAllBytes((Join-Path $root 'a[1].txt'), [byte[]](0x61, 0x0D, 0x0A))
    [IO.File]::WriteAllBytes((Join-Path $root 'sub\b.txt'), [byte[]](0x62, 0x0D, 0x0A))
    $verbose = Get-ChildItem -LiteralPath $root -Recurse | Convert-ProbedContent -LineBreak Lf -Verbose 4>&1 |
        Where-Object { $_ -is [System.Management.Automation.VerboseRecord] -and $_.Message -notlike '*Convert-ProbedContent*' }
    '{0} | {1} | {2}' -f (Format-FileBytes (Join-Path $root 'a[1].txt')), (Format-FileBytes (Join-Path $root 'sub\b.txt')), (($verbose | ForEach-Object { ConvertTo-StableText $_.Message }) -join ' / ')
}

Add-Scenario 'ConvertProbedContent/passthru/WhatIf-outputs-nothing' {
    $path = New-ConvertFile 'whatif.txt' ([byte[]](0x61, 0x0D, 0x0A))
    $results = @(Convert-ProbedContent -LiteralPath $path -LineBreak Lf -PassThru -WhatIf 6>$null)
    '{0} | {1}' -f $results.Count, (Format-FileBytes $path)
}

# ---------------------------------------------------------------------------
# -WhatIf で、実行すれば失敗するエラーを報告する (1.2.0 手動確認後の修正)
#
# ファイルを変更しない検査は ShouldProcess より前に置いた。-WhatIf の有無で同じエラーになり、
# どちらの場合もファイルは変わらないことを確かめる。
# ---------------------------------------------------------------------------
foreach ($whatIfMode in @('normal', 'WhatIf')) {
    $whatIfSplat = if ($whatIfMode -eq 'WhatIf') { @{ WhatIf = $true } } else { @{} }

    Add-Scenario ("WhatIf/{0}/Set/読み取り専用" -f $whatIfMode) {
        $path = New-ByteFile ("wi_set_ro_{0}.txt" -f $whatIfMode) ([byte[]](0x41, 0x0A))
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
        try {
            Set-ProbedContent -LiteralPath $path -Value 'X' -Encoding utf8NoBOM @whatIfSplat -ErrorVariable errors -ErrorAction SilentlyContinue 6>$null
            '{0} | {1}' -f (Format-ErrorRecords $errors), (Format-FileBytes $path)
        }
        finally { Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false }
    }.GetNewClosure()

    Add-Scenario ("WhatIf/{0}/Add/読み取り専用" -f $whatIfMode) {
        $path = New-ByteFile ("wi_add_ro_{0}.txt" -f $whatIfMode) ([byte[]](0x41, 0x0A))
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
        try {
            Add-ProbedContent -LiteralPath $path -Value 'X' -Encoding utf8NoBOM @whatIfSplat -ErrorVariable errors -ErrorAction SilentlyContinue 6>$null
            '{0} | {1}' -f (Format-ErrorRecords $errors), (Format-FileBytes $path)
        }
        finally { Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false }
    }.GetNewClosure()

    Add-Scenario ("WhatIf/{0}/Set/親ディレクトリなし" -f $whatIfMode) {
        $path = Join-Path $WorkRoot ("wi_missing_{0}\a.txt" -f $whatIfMode)
        Set-ProbedContent -LiteralPath $path -Value 'X' -Encoding utf8NoBOM @whatIfSplat -ErrorVariable errors -ErrorAction SilentlyContinue 6>$null
        '{0} | {1}' -f (Format-ErrorRecords $errors), (Test-Path -LiteralPath (Split-Path -Parent $path))
    }.GetNewClosure()

    Add-Scenario ("WhatIf/{0}/Out/NoClobber" -f $whatIfMode) {
        $path = Join-Path $outDir ("wi_noclobber_{0}.txt" -f $whatIfMode)
        [IO.File]::WriteAllText($path, 'OLD')
        $result = Get-TerminatingError { 'A' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM -NoClobber @whatIfSplat -ErrorAction Stop 6>$null }
        '{0} | {1}' -f $result, (Format-FileBytes $path)
    }.GetNewClosure()

    Add-Scenario ("WhatIf/{0}/Out/読み取り専用" -f $whatIfMode) {
        $path = Join-Path $outDir ("wi_ro_{0}.txt" -f $whatIfMode)
        [IO.File]::WriteAllText($path, 'OLD')
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
        try {
            $result = Get-TerminatingError { 'A' | Out-ProbedFile -LiteralPath $path -Encoding utf8NoBOM @whatIfSplat -ErrorAction Stop 6>$null }
            '{0} | {1}' -f $result, (Format-FileBytes $path)
        }
        finally { Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false }
    }.GetNewClosure()

    Add-Scenario ("WhatIf/{0}/Convert/N6-N9" -f $whatIfMode) {
        $root = Join-Path $convertDir ("whatif_{0}" -f $whatIfMode)
        foreach ($sub in @('x', 'y', 'out7', 'out8')) { $null = New-Item -ItemType Directory -Path (Join-Path $root $sub) }
        $ro = Join-Path $root 'ro.txt'
        [IO.File]::WriteAllBytes($ro, [byte[]](0x61, 0x0D, 0x0A))
        [IO.File]::WriteAllBytes((Join-Path $root 'out7\ro.txt'), [byte[]](0x4F))
        [IO.File]::WriteAllBytes((Join-Path $root 'x\b.txt'), [byte[]](0x62, 0x0D, 0x0A))
        [IO.File]::WriteAllBytes((Join-Path $root 'y\b.txt'), [byte[]](0x63, 0x0D, 0x0A))
        Set-ItemProperty -LiteralPath $ro -Name IsReadOnly -Value $true
        try {
            Convert-ProbedContent -LiteralPath $ro -LineBreak Lf @whatIfSplat -ErrorVariable n6 -ErrorAction SilentlyContinue 6>$null
        }
        finally { Set-ItemProperty -LiteralPath $ro -Name IsReadOnly -Value $false }
        Convert-ProbedContent -LiteralPath $ro -LineBreak Lf -Destination (Join-Path $root 'out7') @whatIfSplat -ErrorVariable n7 -ErrorAction SilentlyContinue 6>$null
        Get-ChildItem -LiteralPath (Join-Path $root 'x'), (Join-Path $root 'y') -File |
            Convert-ProbedContent -LineBreak Lf -Destination (Join-Path $root 'out8') -Force @whatIfSplat -ErrorVariable n8 -ErrorAction SilentlyContinue 6>$null
        Convert-ProbedContent -LiteralPath $ro -LineBreak Lf -Destination $root @whatIfSplat -ErrorVariable n9 -ErrorAction SilentlyContinue 6>$null
        'N6={0} | N7={1} | N8={2} | N9={3} | ro={4} | out7={5}' -f (Format-ErrorRecords $n6), (Format-ErrorRecords $n7), (Format-ErrorRecords $n8), (Format-ErrorRecords $n9),
            (Format-FileBytes $ro), (Format-FileBytes (Join-Path $root 'out7\ro.txt'))
    }.GetNewClosure()
}

Add-Scenario 'ConvertProbedContent/passthru/name-restores-original' {
    $path = New-ConvertFile 'restore.txt' ([byte[]](0xEF, 0xBB, 0xBF, 0x41, 0x0D, 0x0A))
    $first = Convert-ProbedContent -LiteralPath $path -Encoding bigendianutf32NoBOM -PassThru
    $second = Convert-ProbedContent -LiteralPath $path -Encoding $first.SourceEncoding -SourceEncoding bigendianutf32NoBOM -PassThru
    '{0} | {1} | {2}' -f $first.SourceEncoding, $second.Changed, (Format-FileBytes $path)
}

# 元に戻すには、Unicode 系以外は SourceCodePage を渡す (1.2.0 手動確認後の修正)。
# EUC-JP の判定結果は 20932 だが、SourceEncoding の euc-jp を渡すと 51932 に解決される
foreach ($restoreSample in @(
    @{ Name = 'euc-jp'; Bytes = [System.Text.Encoding]::GetEncoding(20932).GetBytes("日本語のテキスト`n"); Culture = 'ja-JP' },
    @{ Name = 'shift_jis'; Bytes = [System.Text.Encoding]::GetEncoding(932).GetBytes("日本語のテキスト`r`n"); Culture = 'ja-JP' },
    @{ Name = 'big5hkscs'; Bytes = $script:hkHkscs; Culture = 'zh-HK' })) {
    Add-Scenario ("ConvertProbedContent/passthru/SourceCodePage-restores/{0}" -f $restoreSample.Name) {
        $path = New-ConvertFile ("restore_cp_{0}.txt" -f $restoreSample.Name) $restoreSample.Bytes
        $first = Convert-ProbedContent -LiteralPath $path -Encoding utf8NoBOM -Culture $restoreSample.Culture -PassThru
        Convert-ProbedContent -LiteralPath $path -Encoding $first.SourceCodePage -Culture $restoreSample.Culture
        $restored = if ((Format-FileBytes $path) -eq (($restoreSample.Bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')) { '元のバイト列に一致' } else { Format-FileBytes $path }
        '{0} | {1} | {2}' -f $first.SourceEncoding, $first.SourceCodePage, $restored
    }.GetNewClosure()
}

# 私用領域（HKSCS 固有字）は符号位置のまま往復する
Add-Scenario 'ConvertProbedContent/private-use-area/round-trip' {
    $path = New-ConvertFile 'hkscs.txt' $script:hkHkscs
    Convert-ProbedContent -LiteralPath $path -SourceEncoding big5 -Encoding utf8NoBOM
    $utf8 = [IO.File]::ReadAllBytes($path)
    Convert-ProbedContent -LiteralPath $path -SourceEncoding utf8 -Encoding big5
    '{0} | {1}' -f $utf8.Length, $(if ((Format-FileBytes $path) -eq (($script:hkHkscs | ForEach-Object { $_.ToString('X2') }) -join ' ')) { 'バイト列が一致' } else { Format-FileBytes $path })
}

# ---------------------------------------------------------------------------
# 既存コマンドの変更（1.2.0 仕様書 4 章）
# ---------------------------------------------------------------------------

foreach ($writer in @('Set-ProbedContent', 'Add-ProbedContent')) {
    Add-Scenario "$writer/Force-restores-read-only" {
        $path = New-ByteFile ("force_{0}.txt" -f $writer) ([byte[]](0x41, 0x0A))
        (Get-Item -LiteralPath $path).Attributes = 'ReadOnly'
        try {
            & $writer -LiteralPath $path -Value 'B' -Encoding utf8NoBOM -LineBreak Lf -Force
            '{0} | readonly={1}' -f (Format-FileBytes $path), (Test-ReadOnly $path)
        }
        finally {
            Clear-ReadOnly $path
        }
    }.GetNewClosure()
}

Add-Scenario 'Set-ProbedContent/LineBreak-does-not-replace-inside-values' {
    $path = Join-Path $script:WorkRoot 'set_inside.txt'
    Set-ProbedContent -LiteralPath $path -Value "a`nb", "a`r`nb", "a`rb", 'c' -Encoding utf8NoBOM -LineBreak Lf
    Format-FileBytes $path
}

Add-Scenario 'Add-ProbedContent/LineBreak-does-not-replace-inside-values' {
    $path = New-ByteFile 'add_inside.txt' ([byte[]](0x4F, 0x4C, 0x44, 0x0D, 0x0A))
    Add-ProbedContent -LiteralPath $path -Value "a`r`n`r`nb", "a`n" -Encoding utf8NoBOM -LineBreak Lf
    Format-FileBytes $path
}

# ---------------------------------------------------------------------------
# Resolve-Encoding -DetectionLineCount (1.3.0 仕様書 第 2 部)
#
# 先頭 N 行の切り出しと判定が、両ホストで同じ結果になることを確かめる。
# PSEncodingName / UsePSName は TFM ごとに意味が違う（仕様）ので記録しない。
# パラメータ束縛のエラーは PowerShell 自身の文言を含むため、FullyQualifiedErrorId だけを記録する。
# ---------------------------------------------------------------------------

function Format-DetectionResult {
    param($Information)

    return ("cp={0} web={1} bom={2} lb={3} culture={4}" -f
        $Information.CodePage, $Information.EncodingWebName, $Information.Bom, $Information.LineBreak, $Information.Culture)
}

$script:utf8Bytes = { param([string]$Text) (New-Object System.Text.UTF8Encoding($false)).GetBytes($Text) }
$script:asciiHead = "Name,Memo`r`na,b`r`n"
$script:jpTail = "$([char]0x65E5)$([char]0x672C),$([char]0x8A9E)`r`n"

# 先頭だけが ASCII の UTF-8 / Shift_JIS / EUC-JP（後半に「日本,語」）
$script:dlcFiles = [ordered]@{
    'utf8'     = New-ByteFile 'dlc_utf8.csv' ([byte[]]((& $script:utf8Bytes ($script:asciiHead + $script:jpTail))))
    'shiftjis' = New-ByteFile 'dlc_sjis.csv' ([byte[]]((& $script:utf8Bytes $script:asciiHead) + @(0x93, 0xFA, 0x96, 0x7B, 0x2C, 0x8C, 0xEA, 0x0D, 0x0A)))
    'eucjp'    = New-ByteFile 'dlc_eucjp.csv' ([byte[]]((& $script:utf8Bytes $script:asciiHead) + @(0xC6, 0xFC, 0xCB, 0xDC, 0x2C, 0xB8, 0xEC, 0x0A)))
    'utf8bom'  = New-ByteFile 'dlc_utf8bom.csv' ([byte[]](@(0xEF, 0xBB, 0xBF) + (& $script:utf8Bytes ($script:asciiHead + $script:jpTail))))
    'nobreak'  = New-ByteFile 'dlc_nobreak.csv' ([byte[]](& $script:utf8Bytes "A,B,$([char]0x65E5)"))
    'mixedlb'  = New-ByteFile 'dlc_mixedlb.csv' ([byte[]](& $script:utf8Bytes "a`r`nb`r`nc`nd`n"))
    'empty'    = New-ByteFile 'dlc_empty.csv' ([byte[]]@())
}

# UTF-16 / UTF-32（BOM 有無）。先頭が英数字のものと、先頭に日本語があるもの
foreach ($unicode in @(
    @('utf16le', (New-Object System.Text.UnicodeEncoding($false, $false)), [byte[]](0xFF, 0xFE)),
    @('utf16be', (New-Object System.Text.UnicodeEncoding($true, $false)), [byte[]](0xFE, 0xFF)),
    @('utf32le', (New-Object System.Text.UTF32Encoding($false, $false)), [byte[]](0xFF, 0xFE, 0x00, 0x00)),
    @('utf32be', (New-Object System.Text.UTF32Encoding($true, $false)), [byte[]](0x00, 0x00, 0xFE, 0xFF)))) {

    $label = $unicode[0]
    $encoding = $unicode[1]
    $bom = $unicode[2]
    $asciiFirst = $encoding.GetBytes($script:asciiHead + $script:jpTail)
    $jpFirst = $encoding.GetBytes($script:jpTail + $script:asciiHead)

    $script:dlcFiles["$label/ascii-head"] = New-ByteFile "dlc_${label}_a.csv" ([byte[]]$asciiFirst)
    $script:dlcFiles["$label/jp-head"] = New-ByteFile "dlc_${label}_j.csv" ([byte[]]$jpFirst)
    $script:dlcFiles["$label/bom"] = New-ByteFile "dlc_${label}_bom.csv" ([byte[]]($bom + $asciiFirst))
}

foreach ($entry in $script:dlcFiles.GetEnumerator()) {
    foreach ($lineCount in @(1, 2, 3, 1000)) {
        $path = $entry.Value
        Add-Scenario ("Resolve/DetectionLineCount/{0}/{1}" -f $entry.Key, $lineCount) {
            Format-DetectionResult (Resolve-Encoding -Path $path -DetectionLineCount $lineCount -Culture ja-JP)
        }.GetNewClosure()
    }

    $path = $entry.Value
    Add-Scenario ("Resolve/DetectionLineCount/{0}/whole" -f $entry.Key) {
        Format-DetectionResult (Resolve-Encoding -Path $path -Culture ja-JP)
    }.GetNewClosure()
}

# -Strategy も先頭 N 行の判定に効く
$path = $script:dlcFiles['shiftjis']
Add-Scenario 'Resolve/DetectionLineCount/shiftjis/3/UtfUnknownOnly' {
    Format-DetectionResult (Resolve-Encoding -Path $path -DetectionLineCount 3 -Culture ja-JP -Strategy UtfUnknownOnly)
}.GetNewClosure()

# 0 以下はパラメータ束縛の段階で拒否する
foreach ($lineCount in @(0, -1)) {
    $path = $script:dlcFiles['utf8']
    Add-Scenario ("Resolve/DetectionLineCount/error/{0}" -f $lineCount) {
        try {
            Resolve-Encoding -Path $path -DetectionLineCount $lineCount | Out-Null
            'no error'
        }
        catch {
            $_.FullyQualifiedErrorId
        }
    }.GetNewClosure()
}

# -License に PowerShell 由来のコードの表示が含まれる
Add-Scenario 'Resolve/License/PowerShell' {
    $text = Resolve-Encoding -License
    ($text -split "`r?`n" | Where-Object { $_ -match 'PowerShell/PowerShell|portions of PowerShell|MIT License\. See' }) -join ' / '
}

# ---------------------------------------------------------------------------
# Import-ProbedCsv (1.3.0 仕様書 第 3 部)
#
# 解析は PowerShell 7.x の Import-Csv から移植した処理で行う。5.1 でも 7.x と同じオブジェクトになることを確かめる。
# 両ホストの結果の一致と、xUnit（ImportProbedCsvTests）での 7.6.7 の Import-Csv との一致を合わせて、
# 「5.1 でも 7.6.7 と同じ」ことの確認とする（c04 の型名の順序、c06 の W3C、c12 の CR だけの改行は、5.1 の標準とは違う結果が正しい）。
# ---------------------------------------------------------------------------

function Format-CsvText {
    # [string] で受けると $null が空文字列になり、値の無い列と空文字列の列を区別できないので、型を付けない
    param($Text)

    if ($null -eq $Text) { return '<null>' }
    $escaped = ([string]$Text) -replace "`r", '\r' -replace "`n", '\n'

    # 制御文字（U+0000 など）はレポートを壊すので \uXXXX で表す
    return [regex]::Replace($escaped, '[\x00-\x1F]', { param($m) '\u{0:X4}' -f [int][char]$m.Value })
}

<#
.SYNOPSIS
    Import-ProbedCsv の出力・警告・非終了エラーを 1 行にする。
.DESCRIPTION
    オブジェクトは型名（すべて、順序どおり）と、列名・値の組で表す。値が $null の列は <null> とする。
    エラーは FullyQualifiedErrorId とメッセージ、警告は件数を記録する（UI カルチャーは en-US に固定している）。
#>
function Invoke-ImportProbedCsv {
    param([scriptblock]$Body)

    # スクリプト全体は ErrorActionPreference = Stop なので、非終了エラーを非終了のまま受け取るために戻す
    $ErrorActionPreference = 'Continue'
    $all = @(& $Body 3>&1 2>&1)
    $warnings = @($all | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })
    $errors = @($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] })
    $items = @($all | Where-Object { $_ -isnot [System.Management.Automation.WarningRecord] -and $_ -isnot [System.Management.Automation.ErrorRecord] })
    $parts = foreach ($item in $items) {
        $columns = foreach ($property in $item.PSObject.Properties) { '{0}=[{1}]' -f (Format-CsvText $property.Name), (Format-CsvText $property.Value) }
        'OBJ<{0}> {{{1}}}' -f ($item.PSObject.TypeNames -join ';'), ($columns -join ' | ')
    }
    $errorText = foreach ($e in @($errors)) { 'ERROR ' + $e.FullyQualifiedErrorId.Split(',')[0] + ' :: ' + $e.Exception.Message }

    return ('warnings={0} // {1} // {2}' -f @($warnings).Count, ($parts -join ' // '), ($errorText -join ' // '))
}

# 実測報告 M1 の 15 ケース（tools/Measure-CsvBehavior.ps1 の $m1 と同じ入力。UTF-8、BOM 無し）
$script:jp3 = "$([char]0x65E5)$([char]0x672C)$([char]0x8A9E)"
$script:jp2 = "$([char]0x8FFD)$([char]0x8A18)"
$script:m1 = [ordered]@{
    'c01' = "Name,Memo`r`n`"a`",`"line1`r`nline2`"`r`n`"b`",`"x`"`r`n"
    'c02' = "Name,Memo`r`n`"a`",`"line1`nline2`"`r`n"
    'c03' = "Name,Memo`r`n#1,abc`r`n2,def`r`n"
    'c04' = "#TYPE Foo.Bar`r`nName,Memo`r`na,b`r`n"
    'c05' = "# comment`r`nName,Memo`r`na,b`r`n"
    'c06' = "#Software: x`r`n#Fields: date time`r`n2026 10`r`n"
    'c07' = "Name,Memo`r`n`r`na,b`r`n`r`n"
    'c08' = "Name,Memo`r`n  a , `"b`"  `r`n"
    'c09' = "A,B`r`n1,2,3`r`n4`r`n5,6"
    'c10' = "A,A`r`n1,2`r`n"
    'c11' = "A,,C`r`n1,2,3`r`n"
    'c12' = "A,B`r1,2`r3,4`r"
    'c13' = "A,B`r`n$($script:jp3),$($script:jp2)`r`n"
    'c14' = "A,B,`r`n1,2,`r`n"
    'c15' = "A,B`r`n`"he said `"`"hi`"`"`",x`r`n"
}

foreach ($key in $script:m1.Keys) {
    $path = New-ByteFile ("import_m1_{0}.csv" -f $key) ([byte[]](& $script:utf8Bytes $script:m1[$key]))
    Add-Scenario ("Import/M1/{0}" -f $key) {
        Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path }
    }.GetNewClosure()
}

# パス（仕様書 9 章）。Get-ChildItem からの入力は、標準の 5.1 では CannotSpecifyPathAndLiteralPath で失敗する
$script:importDir = Join-Path $script:WorkRoot 'import_paths'
$null = New-Item -ItemType Directory -Path $script:importDir
[IO.File]::WriteAllBytes((Join-Path $script:importDir 'w1.csv'), [byte[]](& $script:utf8Bytes "A`r`n1`r`n"))
[IO.File]::WriteAllBytes((Join-Path $script:importDir 'w2.csv'), [byte[]](& $script:utf8Bytes "A`r`n2`r`n"))
[IO.File]::WriteAllBytes((Join-Path $script:importDir '[x].csv'), [byte[]](& $script:utf8Bytes "A`r`n3`r`n"))

$dir = $script:importDir
Add-Scenario 'Import/Path/Get-ChildItem' {
    Invoke-ImportProbedCsv { Get-ChildItem -LiteralPath $dir -Filter 'w*.csv' | Sort-Object Name | Import-ProbedCsv -ErrorAction Continue }
}.GetNewClosure()
Add-Scenario 'Import/Path/wildcard-multiple' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -Path (Join-Path $dir 'w*.csv') }
}.GetNewClosure()
Add-Scenario 'Import/Path/wildcard-none' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -Path (Join-Path $dir 'zz*.csv') }
}.GetNewClosure()
Add-Scenario 'Import/Path/array-with-missing' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -Path (Join-Path $dir 'w1.csv'), (Join-Path $dir 'missing.csv'), $dir, (Join-Path $dir 'w2.csv') }
}.GetNewClosure()
Add-Scenario 'Import/Path/LiteralPath-brackets' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LP (Join-Path $dir '[x].csv') }
}.GetNewClosure()

# 文字エンコーディングの決定（仕様書 10 章）。先頭 2 行が ASCII で、3 行目に「日本,語」がある
$script:importEncodingFiles = [ordered]@{
    'shiftjis'     = New-ByteFile 'import_sjis.csv' ([byte[]]((& $script:utf8Bytes $script:asciiHead) + @(0x93, 0xFA, 0x96, 0x7B, 0x2C, 0x8C, 0xEA, 0x0D, 0x0A)))
    'eucjp'        = New-ByteFile 'import_eucjp.csv' ([byte[]]((& $script:utf8Bytes $script:asciiHead) + @(0xC6, 0xFC, 0xCB, 0xDC, 0x2C, 0xB8, 0xEC, 0x0D, 0x0A)))
    'utf8'         = New-ByteFile 'import_utf8.csv' ([byte[]](& $script:utf8Bytes ($script:asciiHead + $script:jpTail)))
    'utf8bom'      = New-ByteFile 'import_utf8bom.csv' ([byte[]](@(0xEF, 0xBB, 0xBF) + (& $script:utf8Bytes ($script:asciiHead + $script:jpTail))))
    'utf16le'      = New-ByteFile 'import_utf16le.csv' ([byte[]](@(0xFF, 0xFE) + (New-Object System.Text.UnicodeEncoding($false, $false)).GetBytes($script:asciiHead + $script:jpTail)))
    'utf16le-nobom' = New-ByteFile 'import_utf16le_nobom.csv' ([byte[]](New-Object System.Text.UnicodeEncoding($false, $false)).GetBytes($script:asciiHead + $script:jpTail))
    'utf32be-nobom' = New-ByteFile 'import_utf32be_nobom.csv' ([byte[]](New-Object System.Text.UTF32Encoding($true, $false)).GetBytes($script:asciiHead + "c,d`r`n"))
}

foreach ($entry in $script:importEncodingFiles.GetEnumerator()) {
    $path = $entry.Value
    Add-Scenario ("Import/Encoding/{0}/whole" -f $entry.Key) {
        Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -Culture ja-JP }
    }.GetNewClosure()

    foreach ($lineCount in @(2, 3)) {
        Add-Scenario ("Import/Encoding/{0}/DetectionLineCount{1}" -f $entry.Key, $lineCount) {
            Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -DetectionLineCount $lineCount -Culture ja-JP }
        }.GetNewClosure()
    }
}

# -Encoding の明示は、判定も検証も行わない（-DetectionLineCount は無視する）
$path = $script:importEncodingFiles['shiftjis']
Add-Scenario 'Import/Encoding/shiftjis/explicit' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -Encoding shift_jis -DetectionLineCount 1 }
}.GetNewClosure()

# 検証の失敗。位置とバイト列が両ホストで同じであること（指示書 4.2、第一次で後回しにした正式な確認）。
# 64 KiB を超える位置（複数のバッファにまたがる場合）も確かめる
$script:largeSjis = New-ByteFile 'import_sjis_large.csv' ([byte[]]((& $script:utf8Bytes ("A,B`r`n" + ("1,2`r`n" * 20000))) + @(0x93, 0xFA, 0x96, 0x7B, 0x2C, 0x8C, 0xEA, 0x0D, 0x0A)))
$path = $script:largeSjis
Add-Scenario 'Import/Verification/shiftjis-beyond-64KiB' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -DetectionLineCount 100 }
}.GetNewClosure()

$path = New-ByteFile 'import_utf8bom_invalid.csv' ([byte[]](@(0xEF, 0xBB, 0xBF) + (& $script:utf8Bytes "A`r`n1`r`n") + @(0xE6, 0x97)))
Add-Scenario 'Import/Verification/utf8bom-truncated-at-end' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -DetectionLineCount 1 }
}.GetNewClosure()

# 区切り文字と -Header。-UseCulture は区切り文字、-Culture は判定のカルチャー（別のもの）
$path = New-ByteFile 'import_semicolon.csv' ([byte[]](& $script:utf8Bytes "A;B`r`n`"x;y`";z`r`n"))
Add-Scenario 'Import/Delimiter/semicolon' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue $path ';' }
}.GetNewClosure()
Add-Scenario 'Import/Header' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -Header X, Y -Delimiter ';' }
}.GetNewClosure()
Add-Scenario 'Import/UseCulture/de-DE' {
    $saved = [System.Threading.Thread]::CurrentThread.CurrentCulture
    try {
        [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('de-DE')
        Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -UseCulture -Culture ja-JP }
    }
    finally {
        [System.Threading.Thread]::CurrentThread.CurrentCulture = $saved
    }
}.GetNewClosure()

# 列名が空のときの警告は、複数のファイルにまたがって 1 回だけ
$first = New-ByteFile 'import_unnamed1.csv' ([byte[]](& $script:utf8Bytes "A,,C`r`n1,2,3`r`n"))
$second = New-ByteFile 'import_unnamed2.csv' ([byte[]](& $script:utf8Bytes ",B`r`n4,5`r`n"))
Add-Scenario 'Import/UnspecifiedHeader/warn-once' {
    Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $first, $second }
}.GetNewClosure()

# ---------------------------------------------------------------------------
# 0x00 を含む入力の判定（1.3.0 仕様書 追補。クラスライブラリ 1.3.0 の修正）
#
# alnum: 英数字だけの BOM 無し UTF-16 / UTF-32。全体の判定でも入力どおりの文字エンコーディングになる
# lowbyte: 2 バイトとも 0x80 未満になる文字（上 U+4E0A）を含むもの。全体の判定では ASCII のまま（既知の限界。追補 4.1）で、
#          Import-ProbedCsv -DetectionLineCount 1 では改行の型による読み替えで正しく読める
# ---------------------------------------------------------------------------

foreach ($unicode in @(
    @('utf16le', (New-Object System.Text.UnicodeEncoding($false, $false))),
    @('utf16be', (New-Object System.Text.UnicodeEncoding($true, $false))),
    @('utf32le', (New-Object System.Text.UTF32Encoding($false, $false))),
    @('utf32be', (New-Object System.Text.UTF32Encoding($true, $false))))) {

    $label = $unicode[0]
    $encoding = $unicode[1]

    $alnum = New-ByteFile "nul_${label}_alnum.csv" ([byte[]]$encoding.GetBytes("Name,Memo`r`na,b`r`nc,d`r`n"))
    $lowbyte = New-ByteFile "nul_${label}_lowbyte.csv" ([byte[]]$encoding.GetBytes(("A,{0}`r`n1,2`r`n" -f [char]0x4E0A)))

    foreach ($entry in @(@('alnum', $alnum), @('lowbyte', $lowbyte))) {
        $kind = $entry[0]
        $path = $entry[1]

        Add-Scenario ("NulInput/{0}/{1}/Resolve/whole" -f $label, $kind) {
            Format-DetectionResult (Resolve-Encoding -Path $path -Culture ja-JP)
        }.GetNewClosure()

        Add-Scenario ("NulInput/{0}/{1}/Get/whole" -f $label, $kind) {
            Format-Lines (Get-ProbedContent -LiteralPath $path -Culture ja-JP)
        }.GetNewClosure()

        Add-Scenario ("NulInput/{0}/{1}/Import/whole" -f $label, $kind) {
            Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -Culture ja-JP }
        }.GetNewClosure()
    }

    $path = $lowbyte
    Add-Scenario ("NulInput/{0}/lowbyte/Import/DetectionLineCount1" -f $label) {
        Invoke-ImportProbedCsv { Import-ProbedCsv -ErrorAction Continue -LiteralPath $path -DetectionLineCount 1 -Culture ja-JP }
    }.GetNewClosure()
}

# ---------------------------------------------------------------------------
# Export-ProbedCsv (1.3.0 仕様書 第 4 部)
#
# CSV の文字列化は PowerShell 7.x の Export-Csv から移植した処理で行う。出力が両ホストでバイト単位で同じになることを確かめる。
# 日付・数値はカルチャーに依存するので、値は文字列だけにする。ホストによって形が変わるオブジェクト（FileInfo など）は使わない
# （第一次の決定）。実測報告 M4 / M5 / M6 に対応するケースを含む。
# ---------------------------------------------------------------------------

<#
.SYNOPSIS
    Export-ProbedCsv を実行し、出力先のバイト列とエラーを 1 行にする。
.DESCRIPTION
    終了エラーは FullyQualifiedErrorId とメッセージを記録する（UI カルチャーは en-US に固定している）。
    警告は件数を記録する。
#>
function Invoke-ExportProbedCsv {
    param([string]$Path, [scriptblock]$Body)

    $ErrorActionPreference = 'Continue'
    $outcome = 'ok'
    $warnings = 0

    try {
        $all = @(& $Body 3>&1)
        $warnings = @($all | Where-Object { $_ -is [System.Management.Automation.WarningRecord] }).Count
    }
    catch {
        # PowerShell 自身が付ける枠の文言（パラメータの変換の失敗など）を含めないよう、最も内側のメッセージを記録する
        $outcome = 'ERROR ' + $_.FullyQualifiedErrorId.Split(',')[0] + ' :: ' + (Get-InnermostMessage $_.Exception)
    }

    return ('{0} // warnings={1} // {2}' -f $outcome, $warnings, (Format-FileBytes $Path))
}

$script:exportDir = Join-Path $script:WorkRoot 'export'
$null = New-Item -ItemType Directory -Path $script:exportDir
$script:exportCount = 0

<#
.SYNOPSIS
    Export-ProbedCsv のシナリオを追加する。追記先などの既存ファイルはバイト列を明示して作る。
#>
function Add-ExportScenario {
    param([string]$Name, [byte[]]$Existing, [scriptblock]$Body)

    $script:exportCount++
    $target = Join-Path $script:exportDir ("e{0:D3}.csv" -f $script:exportCount)

    if ($null -ne $Existing) {
        [IO.File]::WriteAllBytes($target, $Existing)
    }

    $run = $Body
    Add-Scenario ("Export/{0}" -f $Name) {
        Invoke-ExportProbedCsv -Path $target -Body { & $run $target }
    }.GetNewClosure()
}

$script:jpValue = "$([char]0x65E5)$([char]0x672C)$([char]0x8A9E)"
$script:tsuiki = "$([char]0x8FFD)$([char]0x8A18)"
$script:baseCsv = [byte[]](& $script:utf8Bytes "`"A`",`"B`"`r`n`"1`",`"2`"`r`n")
$script:sjisCsv = [byte[]]($script:baseCsv + @(0x93, 0xFA, 0x96, 0x7B, 0x2C, 0x8C, 0xEA, 0x0D, 0x0A))

# M4: 既定（utf8NoBOM、#TYPE 行なし、Always）と各オプション。値は文字列だけ
$sample = [pscustomobject]@{ Name = $script:jpValue; Quote = 'say "hi"'; Multi = "l1`nl2"; Comma = 'a,b'; Empty = $null }
foreach ($options in @('default', 'AsNeeded', 'Never', 'QuoteFields', 'NoHeader', 'IncludeTypeInformation', 'NoTypeInformation', 'Semicolon')) {
    $o = $options
    Add-ExportScenario "M4/$o" $null {
        param($p)
        switch ($o) {
            'default' { $sample | Export-ProbedCsv -LiteralPath $p }
            'AsNeeded' { $sample | Export-ProbedCsv -LiteralPath $p -UseQuotes AsNeeded }
            'Never' { $sample | Export-ProbedCsv -LiteralPath $p -UseQuotes Never }
            'QuoteFields' { $sample | Export-ProbedCsv -LiteralPath $p -QuoteFields Name, comma }
            'NoHeader' { $sample | Export-ProbedCsv -LiteralPath $p -NoHeader }
            'IncludeTypeInformation' { $sample | Export-ProbedCsv -LiteralPath $p -IncludeTypeInformation }
            'NoTypeInformation' { $sample | Export-ProbedCsv -LiteralPath $p -NoTypeInformation }
            'Semicolon' { $sample | Export-ProbedCsv $p ';' }
        }
    }.GetNewClosure()
}

# M4: 入力の種類（5.1 の標準は辞書オブジェクト自体のプロパティを書く）
Add-ExportScenario 'M4/ordered-dictionary' $null { param($p) [ordered]@{ A = '1'; B = 'x' } | Export-ProbedCsv -LiteralPath $p }
Add-ExportScenario 'M4/hashtable-with-note' $null {
    param($p)
    $h = @{ A = '1' }
    $h = Add-Member -InputObject $h -NotePropertyName Extra -NotePropertyValue 'e' -PassThru
    $h | Export-ProbedCsv -LiteralPath $p
}
Add-ExportScenario 'M4/mixed-objects' $null { param($p) [pscustomobject]@{ A = '1'; B = '2' }, [pscustomobject]@{ B = '3'; C = '4' } | Export-ProbedCsv -LiteralPath $p }
Add-ExportScenario 'M4/strings' $null { param($p) 'abc', 'de' | Export-ProbedCsv -LiteralPath $p }

# 文字エンコーディングの決定（仕様書 16.1）
foreach ($encoding in @('utf8BOM', 'shift_jis', 'euc-jp', 'unicodeBOM', 'bigendianunicodeNoBOM', 'utf32BOM')) {
    $enc = $encoding
    Add-ExportScenario "Encoding/$enc" $null { param($p) [pscustomobject]@{ A = $script:jpValue } | Export-ProbedCsv -LiteralPath $p -Encoding $enc }.GetNewClosure()
}
Add-ExportScenario 'Encoding/omitted-overwrites-sjis-as-utf8' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki } | Export-ProbedCsv -LiteralPath $p }
Add-ExportScenario 'Encoding/Auto-inherits-sjis' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki } | Export-ProbedCsv -LiteralPath $p -Encoding Auto -Culture ja-JP }
Add-ExportScenario 'Encoding/Auto-missing' $null { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -LiteralPath $p -Encoding Auto }
Add-ExportScenario 'Encoding/bare-utf8' $null { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -LiteralPath $p -Encoding utf8 }
$lfSjisReference = New-ByteFile 'export_ref_lf_sjis.csv' ([byte[]]((& $script:utf8Bytes "x`n") + @(0x93, 0xFA, 0x96, 0x7B, 0x0A)))
Add-ExportScenario 'Encoding/EncodingFrom-lf-sjis' $null { param($p) [pscustomobject]@{ A = $script:tsuiki } | Export-ProbedCsv -LiteralPath $p -EncodingFrom $lfSjisReference -Culture ja-JP }.GetNewClosure()

# -DetectionLineCount（仕様書 16.2）。先頭が ASCII の Shift_JIS は、どの箇所でも検証で止まり何も書かない（-WhatIf でも報告）
$sjisReference = New-ByteFile 'export_ref_sjis.csv' $script:sjisCsv
Add-ExportScenario 'DetectionLineCount/Append-verification-failed' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki; B = 'x' } | Export-ProbedCsv -LiteralPath $p -Append -DetectionLineCount 2 }
Add-ExportScenario 'DetectionLineCount/Append-WhatIf-verification-failed' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki; B = 'x' } | Export-ProbedCsv -LiteralPath $p -Append -DetectionLineCount 2 -WhatIf }
Add-ExportScenario 'DetectionLineCount/Auto-verification-failed' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki } | Export-ProbedCsv -LiteralPath $p -Encoding Auto -DetectionLineCount 2 }
Add-ExportScenario 'DetectionLineCount/EncodingFrom-verification-failed' $null { param($p) [pscustomobject]@{ A = $script:tsuiki } | Export-ProbedCsv -LiteralPath $p -EncodingFrom $sjisReference -DetectionLineCount 2 }.GetNewClosure()
Add-ExportScenario 'DetectionLineCount/Append-covering' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki; B = 'x' } | Export-ProbedCsv -LiteralPath $p -Append -DetectionLineCount 3 -Culture ja-JP }

# -Append（仕様書 18 章、実測報告 M5 a〜l）
Add-ExportScenario 'M5/a-different-order' $script:baseCsv { param($p) [pscustomobject]@{ B = 'b'; A = 'a' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/b-missing-column' $script:baseCsv { param($p) [pscustomobject]@{ A = 'a' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/b2-missing-column-Force' $script:baseCsv { param($p) [pscustomobject]@{ A = 'a' } | Export-ProbedCsv -LiteralPath $p -Append -Force }
Add-ExportScenario 'M5/c-extra-property' $script:baseCsv { param($p) [pscustomobject]@{ A = 'a'; B = 'b'; C = 'c' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/d-empty-file' ([byte[]]@()) { param($p) [pscustomobject]@{ A = 'a'; B = 'b' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/e-missing-file' $null { param($p) [pscustomobject]@{ A = 'a'; B = 'b' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/f-utf8bom-target' ([byte[]](@(0xEF, 0xBB, 0xBF) + $script:baseCsv)) { param($p) [pscustomobject]@{ A = $script:tsuiki; B = 'b' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/g-sjis-target' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki; B = 'b' } | Export-ProbedCsv -LiteralPath $p -Append -Culture ja-JP }
Add-ExportScenario 'M5/h-Append-NoHeader' $script:baseCsv { param($p) [pscustomobject]@{ A = 'a'; B = 'b' } | Export-ProbedCsv -LiteralPath $p -Append -NoHeader }
Add-ExportScenario 'M5/i-lowercase-header' ([byte[]](& $script:utf8Bytes "a,b`r`n1,2`r`n")) { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/j-no-trailing-newline' ([byte[]](& $script:utf8Bytes "`"A`",`"B`"`r`n1,2")) { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/j-utf16le-no-trailing-newline' ([byte[]](@(0xFF, 0xFE) + (New-Object System.Text.UnicodeEncoding($false, $false)).GetBytes("`"A`",`"B`"`r`n1,2"))) { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/k-lf-target' ([byte[]](& $script:utf8Bytes "`"A`",`"B`"`n`"1`",`"2`"`n")) { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/l-readonly' $script:baseCsv {
    param($p)
    Set-ItemProperty -LiteralPath $p -Name IsReadOnly -Value $true
    try { [pscustomobject]@{ A = 'a'; B = 'b' } | Export-ProbedCsv -LiteralPath $p -Append }
    finally { Set-ItemProperty -LiteralPath $p -Name IsReadOnly -Value $false }
}
Add-ExportScenario 'M5/l2-readonly-Force' $script:baseCsv {
    param($p)
    Set-ItemProperty -LiteralPath $p -Name IsReadOnly -Value $true
    try {
        [pscustomobject]@{ A = 'a'; B = 'b' } | Export-ProbedCsv -LiteralPath $p -Append -Force
        if (-not (Get-Item -LiteralPath $p).IsReadOnly) { throw 'read-only attribute was not restored' }
    }
    finally { Set-ItemProperty -LiteralPath $p -Name IsReadOnly -Value $false }
}
Add-ExportScenario 'M5/bom-only-target' ([byte[]]@(0xEF, 0xBB, 0xBF)) { param($p) [pscustomobject]@{ A = $script:tsuiki } | Export-ProbedCsv -LiteralPath $p -Append }
Add-ExportScenario 'M5/encoding-change' $script:sjisCsv { param($p) [pscustomobject]@{ A = $script:tsuiki; B = 'x' } | Export-ProbedCsv -LiteralPath $p -Append -Encoding utf8NoBOM -Culture ja-JP }

# M6 とその他（仕様書 19 章）
$roundTrip = New-ByteFile 'export_roundtrip.csv' $script:baseCsv
Add-Scenario 'Export/M6/same-path-round-trip' {
    Invoke-ExportProbedCsv -Path $roundTrip -Body { Import-ProbedCsv -LiteralPath $roundTrip | Export-ProbedCsv -LiteralPath $roundTrip }
}.GetNewClosure()
Add-ExportScenario 'M6/NoClobber' $script:baseCsv { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -LiteralPath $p -NoClobber }
Add-ExportScenario 'M6/NoClobber-WhatIf' $script:baseCsv { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -LiteralPath $p -NoClobber -WhatIf }
Add-ExportScenario 'M6/WhatIf-new-file' $null { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -LiteralPath $p -WhatIf }
Add-ExportScenario 'M6/empty-input' $script:baseCsv { param($p) @() | Export-ProbedCsv -LiteralPath $p }
Add-ExportScenario 'M6/empty-input-Append' ([byte[]](& $script:utf8Bytes "`"A`"`r`n1")) { param($p) @() | Export-ProbedCsv -LiteralPath $p -Append }

# エラー（仕様書 20 章）と警告
Add-ExportScenario 'Errors/QuoteFields-and-UseQuotes' $null { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -LiteralPath $p -QuoteFields A -UseQuotes Always }
Add-ExportScenario 'Errors/Path-and-LiteralPath' $null { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -Path $p -LiteralPath $p }
Add-ExportScenario 'Errors/AllowEncodingChange-without-Append' $null { param($p) [pscustomobject]@{ A = '1' } | Export-ProbedCsv -LiteralPath $p -AllowEncodingChange }

# 往復: Export-ProbedCsv で書いたものを Import-ProbedCsv で読むと元の値に戻る
Add-ExportScenario 'RoundTrip/shift_jis' $null {
    param($p)
    $src = [pscustomobject]@{ Q = 'say "hi"'; D = 'a,b'; N = "l1`r`nl2"; J = $script:jpValue }
    $src | Export-ProbedCsv -LiteralPath $p -Encoding shift_jis
    $back = Import-ProbedCsv -LiteralPath $p -Culture ja-JP
    if (-not (($src.Q -ceq $back.Q) -and ($src.D -ceq $back.D) -and ($src.N -ceq $back.N) -and ($src.J -ceq $back.J))) { throw 'round trip changed the values' }
}

# ---------------------------------------------------------------------------
# 1.3.0 第五次: 表現できない文字・継承した us-ascii・改行の単位・切り出し・ISO-2022・空の列名
#
# 書き込みの 4 コマンドは、表現できない文字を '?' やベストフィット（é → e。5.1 の .NET Framework で起きる）にせず、
# UnrepresentableCharacter で止めてファイルを元に戻す。結果は「ok / ERROR <ID> :: <メッセージ>」と書いた後のバイト列で比べる。
# ---------------------------------------------------------------------------

function Invoke-FifthWrite {
    param([string]$Path, [scriptblock]$Body)

    $outcome = 'ok'

    try {
        $null = & $Body
    }
    catch {
        $outcome = 'ERROR ' + $_.FullyQualifiedErrorId.Split(',')[0] + ' :: ' + (Get-InnermostMessage $_.Exception)
    }

    $leftovers = @(Get-ChildItem -LiteralPath (Split-Path -Parent $Path) -Filter '*.probedtmp' -Force).Count
    return ('{0} // tmp={1} // {2}' -f $outcome, $leftovers, (Format-FileBytes $Path))
}

$script:fifthDir = Join-Path $script:WorkRoot 'fifth'
$null = New-Item -ItemType Directory -Path $script:fifthDir
$script:fifthCount = 0

function Add-FifthScenario {
    param([string]$Name, [byte[]]$Existing, [scriptblock]$Body)

    $script:fifthCount++
    $target = Join-Path $script:fifthDir ("f{0:D3}.txt" -f $script:fifthCount)

    if ($null -ne $Existing) {
        [IO.File]::WriteAllBytes($target, $Existing)
    }

    $run = $Body
    Add-Scenario ("Fifth/{0}" -f $Name) {
        Invoke-FifthWrite -Path $target -Body { & $run $target }
    }.GetNewClosure()
}

$script:korean = "$([char]0xD55C)$([char]0xAD6D)"
$script:cafe = "caf$([char]0xE9)"
$script:euro = "$([char]0x20AC)"
$script:fifthOld = [byte[]](0x6F, 0x6C, 0x64, 0x31, 0x0D, 0x0A, 0x6F, 0x6C, 0x64, 0x32, 0x0D, 0x0A)
$script:fifthCsv = [byte[]](0x22, 0x56, 0x22, 0x0D, 0x0A, 0x22, 0x6F, 0x6C, 0x64, 0x22, 0x0D, 0x0A)

# BUG-0010: -Encoding で明示した狭い文字エンコーディングで表現できない文字（ベストフィットを含む）
foreach ($pair in @(@('shift_jis', 'korean'), @('shift_jis', 'cafe'), @('shift_jis', 'euro'), @('ascii', 'jp'), @('iso-2022-jp', 'cafe'), @('ks_c_5601-1987', 'cafe'), @('iso-8859-1', 'euro'))) {
    $enc = $pair[0]
    $value = switch ($pair[1]) { 'korean' { $script:korean } 'cafe' { $script:cafe } 'euro' { $script:euro } default { $script:jpValue } }
    $label = $pair[1]
    Add-FifthScenario "Unrepresentable/Set/$enc/$label" $script:fifthOld { param($p) Set-ProbedContent -LiteralPath $p -Value $value -Encoding $enc -ErrorAction Stop }.GetNewClosure()
    Add-FifthScenario "Unrepresentable/Add/$enc/$label" $script:fifthOld { param($p) Add-ProbedContent -LiteralPath $p -Value $value -Encoding $enc -AllowEncodingChange -ErrorAction Stop }.GetNewClosure()
    Add-FifthScenario "Unrepresentable/Out/$enc/$label" $script:fifthOld { param($p) $value | Out-ProbedFile -LiteralPath $p -Encoding $enc }.GetNewClosure()
    Add-FifthScenario "Unrepresentable/OutAppend/$enc/$label" $script:fifthOld { param($p) $value | Out-ProbedFile -LiteralPath $p -Encoding $enc -Append -AllowEncodingChange }.GetNewClosure()
    Add-FifthScenario "Unrepresentable/Export/$enc/$label" $script:fifthCsv { param($p) [pscustomobject]@{ V = $value } | Export-ProbedCsv -LiteralPath $p -Encoding $enc }.GetNewClosure()
    Add-FifthScenario "Unrepresentable/ExportAppend/$enc/$label" $script:fifthCsv { param($p) [pscustomobject]@{ V = $value } | Export-ProbedCsv -LiteralPath $p -Encoding $enc -Append -AllowEncodingChange }.GetNewClosure()
}

# 途中のレコードで止まったら、それまでに書いた内容も取り消す
Add-FifthScenario 'Rollback/Set-pipeline' $script:fifthOld { param($p) 'ok', $script:korean, 'after' | Set-ProbedContent -LiteralPath $p -Encoding shift_jis -ErrorAction Stop }
Add-FifthScenario 'Rollback/Add-pipeline' $script:fifthOld { param($p) 'ok', $script:korean, 'after' | Add-ProbedContent -LiteralPath $p -Encoding shift_jis -AllowEncodingChange -ErrorAction Stop }
Add-FifthScenario 'Rollback/Out-pipeline' $script:fifthOld { param($p) 'ok', $script:korean, 'after' | Out-ProbedFile -LiteralPath $p -Encoding shift_jis }
Add-FifthScenario 'Rollback/Export-pipeline' $script:fifthCsv { param($p) 'ok', $script:korean | ForEach-Object { [pscustomobject]@{ V = $_ } } | Export-ProbedCsv -LiteralPath $p -Encoding shift_jis }
Add-FifthScenario 'Rollback/Add-missing-file' $null { param($p) Add-ProbedContent -LiteralPath $p -Value $script:korean -Encoding shift_jis -ErrorAction Stop }

# BUG-0012: 例外フォールバック・置換フォールバックの Encoding を渡しても、組み立て直して UnrepresentableCharacter
Add-FifthScenario 'Instance/exception-fallback/Set' $script:fifthOld {
    param($p)
    $ef = [Text.Encoding]::GetEncoding(932, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
    Set-ProbedContent -LiteralPath $p -Value 'ok', $script:korean, 'after' -Encoding $ef -ErrorAction Stop
}
Add-FifthScenario 'Instance/exception-fallback/Add' $script:fifthOld {
    param($p)
    $ef = [Text.Encoding]::GetEncoding(932, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
    Add-ProbedContent -LiteralPath $p -Value 'ok', $script:korean, 'after' -Encoding $ef -ErrorAction Stop
}
Add-FifthScenario 'Instance/replacement-fallback/Out' $script:fifthOld { param($p) 'ok', $script:korean | Out-ProbedFile -LiteralPath $p -Encoding ([Text.Encoding]::GetEncoding(932)) }

# BUG-0001: 継承した us-ascii は BOM 無しの UTF-8 とみなす（ASCII だけならバイト列は変わらない）
$script:asciiText = [byte[]](0x61, 0x62, 0x63, 0x0D, 0x0A)
$script:asciiCsv = [byte[]](0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x0D, 0x0A)
Add-FifthScenario 'InheritedAscii/Add' $script:asciiText { param($p) Add-ProbedContent -LiteralPath $p -Value $script:jpValue -ErrorAction Stop }
Add-FifthScenario 'InheritedAscii/Add-ascii-only' $script:asciiText { param($p) Add-ProbedContent -LiteralPath $p -Value 'xyz' -ErrorAction Stop }
Add-FifthScenario 'InheritedAscii/Add-utf8NoBOM' $script:asciiText { param($p) Add-ProbedContent -LiteralPath $p -Value $script:jpValue -Encoding utf8NoBOM -ErrorAction Stop }
Add-FifthScenario 'InheritedAscii/Add-ascii-explicit' $script:asciiText { param($p) Add-ProbedContent -LiteralPath $p -Value $script:jpValue -Encoding ascii -ErrorAction Stop }
Add-FifthScenario 'InheritedAscii/Set' $script:asciiText { param($p) Set-ProbedContent -LiteralPath $p -Value $script:jpValue -ErrorAction Stop }
Add-FifthScenario 'InheritedAscii/OutAppend' $script:asciiText { param($p) $script:jpValue | Out-ProbedFile -LiteralPath $p -Append }
Add-FifthScenario 'InheritedAscii/ExportAppend' $script:asciiCsv { param($p) [pscustomobject]@{ A = $script:jpValue; B = 'x' } | Export-ProbedCsv -LiteralPath $p -Append }
$asciiReference = New-ByteFile 'fifth_ascii_ref.txt' ([byte[]](0x61, 0x62, 0x63, 0x0A))
Add-FifthScenario 'InheritedAscii/EncodingFrom' $null { param($p) Set-ProbedContent -LiteralPath $p -Value $jpValue -EncodingFrom $asciiReference -ErrorAction Stop }.GetNewClosure()
Add-FifthScenario 'InheritedAscii/EncodingInformation' $null { param($p) Set-ProbedContent -LiteralPath $p -Value $jpValue -Encoding (Resolve-Encoding -Path $asciiReference) -ErrorAction Stop }.GetNewClosure()

# 改行の単位（UTF-16 / UTF-32 の「上」「《」「不」を LF / CR と数えない）
$unitEncodings = [ordered]@{
    'u16le' = (New-Object System.Text.UnicodeEncoding($false, $false))
    'u16be' = (New-Object System.Text.UnicodeEncoding($true, $false))
    'u32le' = (New-Object System.Text.UTF32Encoding($false, $false))
    'u32be' = (New-Object System.Text.UTF32Encoding($true, $false))
}
$unitBreaks = [ordered]@{ 'CrLf' = "`r`n"; 'Lf' = "`n"; 'Cr' = "`r" }
$unitHeader = "$([char]0x65E5)$([char]0x672C)$([char]0x8A9E)$([char]0x4E0A)$([char]0x300A)$([char]0x4E0D)"
foreach ($formName in $unitEncodings.Keys) {
    foreach ($breakName in $unitBreaks.Keys) {
        $unitEncoding = $unitEncodings[$formName]
        $nl = $unitBreaks[$breakName]
        $text = ("$unitHeader,B", "1$([char]0x4E0A),2", "$([char]0x4E0D)3,4") -join $nl
        $bytes = [byte[]]$unitEncoding.GetBytes($text + $nl)
        $unitFile = New-ByteFile ("fifth_unit_{0}_{1}.csv" -f $formName, $breakName) $bytes
        Add-Scenario "Fifth/UnitLineBreak/$formName/$breakName/Resolve" { (Resolve-Encoding -Path $unitFile -Culture ja-JP).LineBreak }.GetNewClosure()
        Add-FifthScenario "UnitLineBreak/$formName/$breakName/Add" $bytes { param($p) Add-ProbedContent -LiteralPath $p -Value '9,8' -Culture ja-JP -ErrorAction Stop }.GetNewClosure()
        $h = $unitHeader
        Add-FifthScenario "UnitLineBreak/$formName/$breakName/ExportAppend-N1" $bytes { param($p) [pscustomobject]@{ "$h" = '9'; B = '8' } | Export-ProbedCsv -LiteralPath $p -Append -DetectionLineCount 1 -Culture ja-JP }.GetNewClosure()
    }
}

# BUG-0014: 英数字と「上」だけの BOM 無し UTF-16LE（改行は CR）。-DetectionLineCount で読み替えた後の単位で改行を数える
$bug0014 = [byte[]](0x41, 0x00, 0x0A, 0x4E, 0x2C, 0x00, 0x42, 0x00, 0x0D, 0x00, 0x31, 0x00, 0x2C, 0x00, 0x32, 0x00, 0x0D, 0x00)
Add-FifthScenario 'BUG-0014/ExportAppend-N1' $bug0014 { param($p) [pscustomobject]@{ "A$([char]0x4E0A)" = '9'; B = '8' } | Export-ProbedCsv -LiteralPath $p -Append -DetectionLineCount 1 }

# BUG-0002: 0x00 を含む ASCII の CSV（-DetectionLineCount の有無で同じ値、追記は ASCII の行）
foreach ($case in @(
    @('A-lf', [byte[]](0x41, 0x2C, 0x42, 0x00, 0x0A, 0x31, 0x2C, 0x32, 0x00, 0x0A)),
    @('B-crlf', [byte[]](0x41, 0x2C, 0x42, 0x00, 0x00, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x00, 0x00, 0x0D, 0x0A)))) {
    $caseName = $case[0]
    $caseBytes = $case[1]
    $caseFile = New-ByteFile ("fifth_bug0002_{0}.csv" -f $caseName) $caseBytes
    Add-Scenario "Fifth/BUG-0002/$caseName/Import-N1" { Import-ProbedCsv -LiteralPath $caseFile -DetectionLineCount 1 | ConvertTo-Json -Compress }.GetNewClosure()
    Add-Scenario "Fifth/BUG-0002/$caseName/Resolve-N1" { $r = Resolve-Encoding -Path $caseFile -DetectionLineCount 1; '{0} {1}' -f $r.CodePage, $r.LineBreak }.GetNewClosure()
    Add-FifthScenario "BUG-0002/$caseName/ExportAppend-N1" $caseBytes { param($p) [pscustomobject]@{ A = 'z'; "B$([char]0)" = 'z' } | Export-ProbedCsv -LiteralPath $p -Append -Force -DetectionLineCount 1 }
}

# BUG-0013: 最初の改行より前に U+0D00 がある BOM 無し UTF-16BE
$bug0013 = New-ByteFile 'fifth_bug0013.csv' ([byte[]](0x65, 0xE5, 0x0D, 0x00, 0x00, 0x2C, 0x00, 0x42, 0x00, 0x0D, 0x00, 0x0A, 0x00, 0x31, 0x00, 0x2C, 0x00, 0x32, 0x00, 0x0D, 0x00, 0x0A))
Add-Scenario 'Fifth/BUG-0013/Import-N1' { Import-ProbedCsv -LiteralPath $bug0013 -DetectionLineCount 1 | ConvertTo-Json -Compress }.GetNewClosure()

# BUG-0009: 先頭行が ASCII だけの ISO-2022-JP / KR
$iso2022jp = [byte[]](0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x22, 0x1B, 0x24, 0x42, 0x46, 0x7C, 0x4B, 0x5C, 0x1B, 0x28, 0x42, 0x22, 0x2C, 0x78, 0x0D, 0x0A)
$iso2022kr = [byte[]](0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x22, 0x1B, 0x24, 0x29, 0x43, 0x0E, 0x47, 0x51, 0x31, 0x39, 0x0F, 0x22, 0x2C, 0x78, 0x0D, 0x0A)
$iso2022jpFile = New-ByteFile 'fifth_iso2022jp.csv' $iso2022jp
Add-Scenario 'Fifth/BUG-0009/jp/Import-N1' { Import-ProbedCsv -LiteralPath $iso2022jpFile -DetectionLineCount 1 -Culture ja-JP -ErrorAction Stop }.GetNewClosure()
Add-FifthScenario 'BUG-0009/jp/ExportAppend-N1' $iso2022jp { param($p) [pscustomobject]@{ A = $script:jpValue; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append -DetectionLineCount 1 -Culture ja-JP }
Add-FifthScenario 'BUG-0009/kr/ExportAppend-N1' $iso2022kr { param($p) [pscustomobject]@{ A = $script:korean; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append -DetectionLineCount 1 -Culture ko-KR }

# Export-ProbedCsv -Append で、追記先のヘッダーに空の列名がある（-Force なしは本家と同じ ID、-Force ありは本モジュールの ID）
foreach ($header in @('A,,B', ',A,B', 'A,B,')) {
    $headerBytes = [byte[]]([Text.Encoding]::ASCII.GetBytes($header + "`r`n1,2,3`r`n"))
    $hd = $header
    Add-FifthScenario "EmptyColumn/$hd" $headerBytes { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append }.GetNewClosure()
    Add-FifthScenario "EmptyColumn/$hd/Force" $headerBytes { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append -Force }.GetNewClosure()
}

# ---------------------------------------------------------------------------
# 1.3.0 第五次の後半: パスの束縛・同じファイルの判定（実体で比べる）・リンクと権限・追記の取り消し・-Culture の検証
#
# リンクは cmd の mklink で作る（両ホストで同じ）。作れない環境では両ホストとも「(作れない)」を記録する。
# 同じファイルの往復は、エラーの ID と、元のファイルの長さが変わっていないか（unchanged / changed）で比べる。
# ---------------------------------------------------------------------------

$script:f2Dir = Join-Path $script:WorkRoot 'fifth2'
$null = New-Item -ItemType Directory -Path $script:f2Dir

function New-F2Folder {
    param([string]$Name)

    $folder = Join-Path $script:f2Dir $Name
    $null = New-Item -ItemType Directory -Path $folder
    return $folder
}

function New-F2Link {
    param([string]$Kind, [string]$Link, [string]$Target)

    $switch = switch ($Kind) { 'symlink' { '' } 'hardlink' { '/H' } 'junction' { '/J' } 'dirlink' { '/D' } }
    $null = cmd /c "mklink $switch `"$Link`" `"$Target`"" 2>&1
    return (Test-Path -LiteralPath $Link)
}

function Invoke-F2 {
    param([scriptblock]$Body)

    $outcome = @()

    try {
        $outcome = @(& $Body 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) { 'ERR ' + $_.FullyQualifiedErrorId.Split(',')[0] } else { [string]$_ }
        })
    }
    catch {
        $outcome += 'TERM ' + $_.FullyQualifiedErrorId.Split(',')[0]
    }

    return ($outcome -join ' | ')
}

# BUG-0003: [ ] を含む名前のファイルをパイプラインで渡す（a1.csv / b1.txt はワイルドカードとして一致してしまう別のファイル）
$bindDir = New-F2Folder 'bind'
$bindCsv = Join-Path $bindDir 'a[1].csv'
[IO.File]::WriteAllBytes($bindCsv, [byte[]](0x41, 0x0D, 0x0A, 0x78, 0x0D, 0x0A))
[IO.File]::WriteAllBytes((Join-Path $bindDir 'a1.csv'), [byte[]](0x41, 0x0D, 0x0A, 0x79, 0x0D, 0x0A))
$bindTxt = Join-Path $bindDir 'b[1].txt'
$bindOther = Join-Path $bindDir 'b1.txt'
Add-Scenario 'Fifth2/Bind/Get-Item|Get' { Invoke-F2 { Get-Item -LiteralPath $bindCsv | Get-ProbedContent } }.GetNewClosure()
Add-Scenario 'Fifth2/Bind/Get-Item|Import' { Invoke-F2 { Get-Item -LiteralPath $bindCsv | Import-ProbedCsv | ForEach-Object { $_.A } } }.GetNewClosure()
Add-Scenario 'Fifth2/Bind/FileInfo|Get' { Invoke-F2 { New-Object System.IO.FileInfo $bindCsv | Get-ProbedContent } }.GetNewClosure()
Add-Scenario 'Fifth2/Bind/gci|Import' { Invoke-F2 { Get-ChildItem -LiteralPath $bindDir -Filter '*.csv' | Import-ProbedCsv | ForEach-Object { $_.A } | Sort-Object } }.GetNewClosure()
foreach ($bindCommand in @('Set', 'Add')) {
    $bc = $bindCommand
    Add-Scenario "Fifth2/Bind/Get-Item|$bc" {
        [IO.File]::WriteAllBytes($bindTxt, [byte[]](0x78, 0x0D, 0x0A))
        [IO.File]::WriteAllBytes($bindOther, [byte[]](0x79, 0x0D, 0x0A))
        $r = Invoke-F2 { Get-Item -LiteralPath $bindTxt | & "$bc-ProbedContent" -Value z -Encoding utf8NoBOM -LineBreak CrLf }
        '{0} // b[1]={1} // b1={2}' -f $r, (Format-FileBytes $bindTxt), (Format-FileBytes $bindOther)
    }.GetNewClosure()
}
Add-Scenario 'Fifth2/Bind/string|Get (wildcard)' { Invoke-F2 { (Join-Path $bindDir 'a?.csv') | Get-ProbedContent } }.GetNewClosure()

# BUG-0015: 書き込み側のワイルドカードが 0 件（標準と同じ ItemNotFound の非終了エラー。読み込み側はエラーにしない）
$wildDir = New-F2Folder 'wild'
foreach ($wildCommand in @('Set', 'Add')) {
    foreach ($pattern in @('c[1].txt', 'x*.txt')) {
        $wc = $wildCommand
        $wp = Join-Path $wildDir $pattern
        Add-Scenario "Fifth2/WildcardNoMatch/$wc/$pattern" {
            $r = Invoke-F2 { & "$wc-ProbedContent" -Path $wp -Value x -Encoding utf8NoBOM }
            '{0} // files={1}' -f $r, @(Get-ChildItem -LiteralPath $wildDir -Force).Count
        }.GetNewClosure()
    }
}
Add-Scenario 'Fifth2/WildcardNoMatch/Get' { Invoke-F2 { Get-ProbedContent -Path (Join-Path $wildDir 'x*.txt') } }.GetNewClosure()

# BUG-0004: 読み込み中のファイルに、別の書き方で書く（どれも SamePathRoundTrip で止まり、元のファイルは変わらない）
$sameDir = New-F2Folder 'same'
$sameSub = Join-Path $sameDir 'sub'
$null = New-Item -ItemType Directory -Path $sameSub
$sameData = Join-Path $sameSub 'data.txt'
$sameBytes = [byte[]][Text.Encoding]::ASCII.GetBytes(('line' + "`r`n") * 2000)
$sameFull = (Resolve-Path -LiteralPath $sameSub).ProviderPath
$sameSpellings = [ordered]@{
    'extended' = '\\?\' + (Join-Path $sameFull 'data.txt')
    'device' = '\\.\' + (Join-Path $sameFull 'data.txt')
    'unc' = '\\localhost\' + $sameFull.Substring(0, 1) + '$' + $sameFull.Substring(2) + '\data.txt'
    'mainstream' = (Join-Path $sameFull 'data.txt') + '::$DATA'
    'symlink' = Join-Path $sameDir 'link.txt'
    'hardlink' = Join-Path $sameDir 'hard.txt'
    'junction' = Join-Path (Join-Path $sameDir 'junction') 'data.txt'
}
[IO.File]::WriteAllBytes($sameData, $sameBytes)
$sameAvailable = @{
    'extended' = $true; 'device' = $true; 'mainstream' = $true
    'unc' = (Test-Path -LiteralPath $sameSpellings['unc'])
    'symlink' = (New-F2Link 'symlink' $sameSpellings['symlink'] $sameData)
    'hardlink' = (New-F2Link 'hardlink' $sameSpellings['hardlink'] $sameData)
    'junction' = (New-F2Link 'junction' (Join-Path $sameDir 'junction') $sameSub)
}
foreach ($spelling in $sameSpellings.Keys) {
    foreach ($writer in @('Set', 'Add', 'Out', 'OutAppend')) {
        $other = $sameSpellings[$spelling]
        $available = $sameAvailable[$spelling]
        $w = $writer
        Add-Scenario "Fifth2/SameFile/$spelling/$w" {
            if (-not $available) { return '(作れない)' }
            [IO.File]::WriteAllBytes($sameData, $sameBytes)
            $r = Invoke-F2 {
                switch ($w) {
                    'Set' { Get-ProbedContent -LiteralPath $sameData | Set-ProbedContent -LiteralPath $other -Encoding utf8NoBOM }
                    'Add' { Get-ProbedContent -LiteralPath $sameData | Add-ProbedContent -LiteralPath $other -Encoding utf8NoBOM }
                    'Out' { Get-ProbedContent -LiteralPath $sameData | Out-ProbedFile -LiteralPath $other -Encoding utf8NoBOM }
                    'OutAppend' { Get-ProbedContent -LiteralPath $sameData | Out-ProbedFile -LiteralPath $other -Encoding utf8NoBOM -Append }
                }
            }
            $state = if ((Get-Item -LiteralPath $sameData).Length -eq $sameBytes.Length) { 'unchanged' } else { 'changed' }
            '{0} // {1}' -f $r, $state
        }.GetNewClosure()
    }
}
Add-Scenario 'Fifth2/SameFile/Csv/extended' {
    $csv = Join-Path $sameSub 'data.csv'
    [IO.File]::WriteAllBytes($csv, [byte[]][Text.Encoding]::ASCII.GetBytes("A,B`r`n" + ('1,2' + "`r`n") * 2000))
    $len = (Get-Item -LiteralPath $csv).Length
    $r = Invoke-F2 { Import-ProbedCsv -LiteralPath $csv | Export-ProbedCsv -LiteralPath ('\\?\' + (Join-Path $sameFull 'data.csv')) }
    '{0} // {1}' -f $r, ($(if ((Get-Item -LiteralPath $csv).Length -eq $len) { 'unchanged' } else { 'changed' }))
}.GetNewClosure()

# BUG-0016: 同じ実体を 2 回（Set は 1 回書いたのと同じ。Add は 1 回の指定の中では 1 回、レコードをまたげば 2 回）
$dupFile = Join-Path $script:f2Dir 'dup.txt'
foreach ($dup in @(
    @('Set/a,A', { Set-ProbedContent -LiteralPath $dupFile, $dupFile.ToUpperInvariant() -Value Z -Encoding utf8NoBOM -LineBreak CrLf }),
    @('Set/pipeline', { (Get-Item -LiteralPath $dupFile), (Get-Item -LiteralPath $dupFile) | Set-ProbedContent -Value Z -Encoding utf8NoBOM -LineBreak CrLf }),
    @('Set/values', { 'p', 'q' | Set-ProbedContent -LiteralPath $dupFile -Encoding utf8NoBOM -LineBreak CrLf }),
    @('Add/a,A', { Add-ProbedContent -LiteralPath $dupFile, $dupFile.ToUpperInvariant() -Value Z -Encoding utf8NoBOM -LineBreak CrLf }),
    @('Add/pipeline', { (Get-Item -LiteralPath $dupFile), (Get-Item -LiteralPath $dupFile) | Add-ProbedContent -Value Z -Encoding utf8NoBOM -LineBreak CrLf }))) {
    $dupName = $dup[0]
    $dupBody = $dup[1]
    Add-Scenario "Fifth2/Duplicate/$dupName" {
        [IO.File]::WriteAllBytes($dupFile, [byte[]](0x6F, 0x6C, 0x64, 0x0D, 0x0A))
        '{0} // {1}' -f (Invoke-F2 $dupBody), (Format-FileBytes $dupFile)
    }.GetNewClosure()
}

# BUG-0017: Convert-ProbedContent -Destination に変換元のフォルダーを別の書き方で渡す（-Force でも N9）
$convDir = New-F2Folder 'conv'
$convSrc = Join-Path $convDir 'src'
$null = New-Item -ItemType Directory -Path $convSrc
$convFile = Join-Path $convSrc 'a.txt'
$convFull = (Resolve-Path -LiteralPath $convSrc).ProviderPath
$convJunction = Join-Path $convDir 'junction'
$convJunctionOk = New-F2Link 'junction' $convJunction $convSrc
$convDestinations = [ordered]@{
    'upper' = $convFull.ToUpperInvariant()
    'extended' = '\\?\' + $convFull
    'device' = '\\.\' + $convFull
    'unc' = '\\localhost\' + $convFull.Substring(0, 1) + '$' + $convFull.Substring(2)
    'junction' = $convJunction
}
foreach ($dest in $convDestinations.GetEnumerator()) {
    foreach ($force in @($false, $true)) {
        $destName = $dest.Key
        $destPath = $dest.Value
        $f = $force
        Add-Scenario "Fifth2/ConvertDestination/$destName/Force=$f" {
            if ($destName -eq 'junction' -and -not $convJunctionOk) { return '(作れない)' }
            if (-not (Test-Path -LiteralPath $destPath)) { return '(作れない)' }
            [IO.File]::WriteAllBytes($convFile, [byte[]](0x61, 0x62, 0x63, 0x0D, 0x0A))
            $r = Invoke-F2 { Convert-ProbedContent -LiteralPath $convFile -Encoding utf8BOM -Destination $destPath -Force:$f }
            '{0} // {1}' -f $r, (Format-FileBytes $convFile)
        }.GetNewClosure()
    }
}

# 書き込みのリンク・権限（実装記録 9.9 節の 4、H-17）
$linkDir = New-F2Folder 'links'
$linkSub = Join-Path $linkDir 'sub'
$null = New-Item -ItemType Directory -Path $linkSub
$linkTarget = Join-Path $linkSub 't.txt'
$linkWriters = [ordered]@{
    'Set' = { param($p) Set-ProbedContent -LiteralPath $p -Value new -Encoding utf8NoBOM -LineBreak CrLf }
    'Add' = { param($p) Add-ProbedContent -LiteralPath $p -Value new -Encoding utf8NoBOM -LineBreak CrLf }
    'Out' = { param($p) 'new' | Out-ProbedFile -LiteralPath $p -Encoding utf8NoBOM -LineBreak CrLf }
    'Export' = { param($p) [pscustomobject]@{ A = 'new' } | Export-ProbedCsv -LiteralPath $p }
    'Convert' = { param($p) Convert-ProbedContent -LiteralPath $p -Encoding utf8BOM }
}
$linkIndex = 0
foreach ($kind in @('symlink-relative-chain', 'hardlink')) {
    foreach ($writerName in $linkWriters.Keys) {
        $linkIndex++
        $k = $kind
        $wb = $linkWriters[$writerName]
        $n = $linkIndex
        Add-Scenario "Fifth2/Link/$k/$writerName" {
            [IO.File]::WriteAllBytes($linkTarget, [byte[]](0x6F, 0x6C, 0x64, 0x0D, 0x0A))
            if ($k -eq 'hardlink') {
                $entry = Join-Path $linkDir ("hard{0}.txt" -f $n)
                if (-not (New-F2Link 'hardlink' $entry $linkTarget)) { return '(作れない)' }
            }
            else {
                $mid = Join-Path $linkDir ("mid{0}.txt" -f $n)
                $entry = Join-Path $linkDir ("link{0}.txt" -f $n)
                if (-not (New-F2Link 'symlink' $mid 'sub\t.txt') -or -not (New-F2Link 'symlink' $entry ("mid{0}.txt" -f $n))) { return '(作れない)' }
            }
            $r = Invoke-F2 { & $wb $entry }
            $linkType = (Get-Item -LiteralPath $entry -Force).LinkType
            '{0} // target={1} // entry={2} // linktype={3} // tmp={4}' -f $r, (Format-FileBytes $linkTarget), (Format-FileBytes $entry), $linkType, @(Get-ChildItem -LiteralPath $linkDir -Recurse -Force -Filter '*.probedtmp').Count
        }.GetNewClosure()
    }
}
foreach ($writerName in @('Set', 'Add', 'Out', 'Export')) {
    $linkIndex++
    $wb = $linkWriters[$writerName]
    $n = $linkIndex
    Add-Scenario "Fifth2/Link/dangling/$writerName" {
        $entry = Join-Path $linkDir ("dangling{0}.txt" -f $n)
        $missing = Join-Path $linkSub ("missing{0}.txt" -f $n)
        if (-not (New-F2Link 'symlink' $entry ("sub\missing{0}.txt" -f $n))) { return '(作れない)' }
        $r = Invoke-F2 { & $wb $entry }
        '{0} // target={1} // linktype={2}' -f $r, (Format-FileBytes $missing), (Get-Item -LiteralPath $entry -Force).LinkType
    }.GetNewClosure()
}
Add-Scenario 'Fifth2/Link/hardlink/Set-rollback' {
    [IO.File]::WriteAllBytes($linkTarget, [byte[]](0x6F, 0x6C, 0x64, 0x0D, 0x0A))
    $entry = Join-Path $linkDir 'hard-rollback.txt'
    if (-not (New-F2Link 'hardlink' $entry $linkTarget)) { return '(作れない)' }
    $r = Invoke-F2 { Set-ProbedContent -LiteralPath $entry -Value 'a', "$([char]0xD55C)" -Encoding shift_jis }
    '{0} // target={1} // entry={2}' -f $r, (Format-FileBytes $linkTarget), (Format-FileBytes $entry)
}.GetNewClosure()

# 2 章: EncodingChangeOnAppend でも、その呼び出しで書いた分を残さない
$appendDir = New-F2Folder 'append'
$appendSjis = [byte[]](0x82, 0xA0, 0x0A)
foreach ($appendCase in @(
    @('Add', { param($p) 'x', "$([char]0x3044)", 'y' | Add-ProbedContent -LiteralPath $p -Encoding utf8NoBOM -Culture ja-JP -LineBreak Lf }),
    @('Add-Stop', { param($p) 'x', "$([char]0x3044)" | Add-ProbedContent -LiteralPath $p -Encoding utf8NoBOM -Culture ja-JP -LineBreak Lf -ErrorAction Stop }),
    @('OutAppend', { param($p) 'x', "$([char]0x3044)" | Out-ProbedFile -LiteralPath $p -Encoding utf8NoBOM -Append -Culture ja-JP -LineBreak Lf }),
    @('ExportAppend', { param($p) [pscustomobject]@{ A = 'x' }, [pscustomobject]@{ A = "$([char]0x3044)" } | Export-ProbedCsv -LiteralPath $p -Encoding utf8NoBOM -Append -Culture ja-JP }))) {
    $an = $appendCase[0]
    $ab = $appendCase[1]
    $ap = Join-Path $appendDir ("{0}.txt" -f $an)
    $initial = if ($an -eq 'ExportAppend') { [byte[]](0x41, 0x0A, 0x82, 0xA0, 0x0A) } else { $appendSjis }
    Add-Scenario "Fifth2/AppendRollback/$an" {
        [IO.File]::WriteAllBytes($ap, $initial)
        '{0} // {1}' -f (Invoke-F2 { & $ab $ap }), (Format-FileBytes $ap)
    }.GetNewClosure()
}

# 代替データストリーム（主ストリームの名前はそのファイル。ほかのストリームは両ホストで AlternateDataStreamNotSupported）
$streamFile = Join-Path $script:f2Dir 'stream.txt'
[IO.File]::WriteAllBytes($streamFile, [byte[]](0x61, 0x62, 0x63, 0x0D, 0x0A))
Add-Scenario 'Fifth2/Stream/Get-main' { Invoke-F2 { Get-ProbedContent -LiteralPath ($streamFile + '::$DATA') } }.GetNewClosure()
Add-Scenario 'Fifth2/Stream/Resolve-main' { Invoke-F2 { (Resolve-Encoding -Path ($streamFile + '::$DATA')).CodePage } }.GetNewClosure()
Add-Scenario 'Fifth2/Stream/Get-alt' { Invoke-F2 { Get-ProbedContent -LiteralPath ($streamFile + ':alt') } }.GetNewClosure()
Add-Scenario 'Fifth2/Stream/Set-alt' { Invoke-F2 { Set-ProbedContent -LiteralPath ($streamFile + ':alt') -Value x -Encoding utf8NoBOM } }.GetNewClosure()
Add-Scenario 'Fifth2/Stream/Out-alt' { Invoke-F2 { 'x' | Out-ProbedFile -LiteralPath ($streamFile + ':alt') } }.GetNewClosure()

# BUG-0006・0007: -Culture / -Strategy の検証（Resolve-Encoding もほかのコマンドと同じ ID。受け付ける名前は形で決める）
$cultureFile = Join-Path $script:f2Dir 'culture.txt'
[IO.File]::WriteAllBytes($cultureFile, [byte[]](0xC7, 0xD1, 0xB1, 0xB9, 0xBE, 0xEE, 0x0D, 0x0A))
foreach ($cultureName in @('C', 'c', 'root', 'POSIX', 'xx-YY', 'ko-KR', 'ko_KR', 'art-lojban', 'zh-yue', 'zh-min-nan', 'und-u-ca-gregory', 'c-JP', 'ja-JP-x', 'i-klingon', 'x-test', 'en-u', 'not a culture!', 'cel-gaulish', 'zh-min', 'no-bok', 'zh-cmn', 'zh-guoyu', 'zh-yue-HK')) {
    $cn = $cultureName
    Add-Scenario "Fifth2/Culture/$cn/Resolve" { Invoke-F2 { $r = Resolve-Encoding -Path $cultureFile -Culture $cn; '{0} {1}' -f $r.CodePage, $r.Culture } }.GetNewClosure()
    Add-Scenario "Fifth2/Culture/$cn/Get" { Invoke-F2 { (Get-ProbedContent -LiteralPath $cultureFile -Culture $cn -Raw).Length } }.GetNewClosure()
    Add-Scenario "Fifth2/Culture/$cn/Import" { Invoke-F2 { @(Import-ProbedCsv -LiteralPath $cultureFile -Culture $cn).Count } }.GetNewClosure()
    Add-Scenario "Fifth2/Culture/$cn/Convert" { Invoke-F2 { (Convert-ProbedContent -LiteralPath $cultureFile -Culture $cn -Encoding utf8BOM -WhatIf -PassThru | Measure-Object).Count } }.GetNewClosure()
}
Add-Scenario 'Fifth2/Strategy/Resolve-invalid' {
    try { Resolve-Encoding -Path $cultureFile -Strategy Nonexistent; 'no error' }
    catch { '{0} [{1}] {2} :: {3}' -f $_.FullyQualifiedErrorId.Split(',')[0], $_.CategoryInfo.Category, $_.Exception.GetType().Name, (Get-InnermostMessage $_.Exception) }
}.GetNewClosure()
Add-Scenario 'Fifth2/Culture/Resolve-invalid-message' {
    try { Resolve-Encoding -Path $cultureFile -Culture 'not a culture!'; 'no error' }
    catch { '{0} [{1}] {2} :: {3}' -f $_.FullyQualifiedErrorId.Split(',')[0], $_.CategoryInfo.Category, $_.Exception.GetType().Name, (Get-InnermostMessage $_.Exception) }
}.GetNewClosure()

# BUG-0008 (5): OS の説明に前後の空白が無い
Add-Scenario 'Fifth2/PlatformInfo/OsDescriptionTrimmed' { $d = (Get-EncodingProbePlatformInfo).Os.Description; [string]($d -ceq $d.Trim()) }

# ---------------------------------------------------------------------------
# 1.3.0 第五次の仕上げ: 小さなファイルはメモリに写してから直接書く（実装記録 10.16 節）
#
# 境目（SafeFileOverwrite.DefaultDirectWriteThreshold）ちょうどと 1 バイト超えたところで、書き方が両ホストで同じに切り替わること、
# 失敗したときに元どおりになることを確かめる。書き方は、実体（ファイル ID）が変わったかどうか（置き換えなら変わる）で記録する。
# ファイル ID そのものはホストや実行ごとに違うので、変わったかどうかだけを記録する。
# ---------------------------------------------------------------------------
$f3Assembly = [SnowStack.EncodingProbe.PowerShell.Cmdlets.ResolveEncodingCmdlet].Assembly
$f3Threshold = [int]$f3Assembly.GetType('SnowStack.EncodingProbe.PowerShell.Internal.SafeFileOverwrite').GetField('DefaultDirectWriteThreshold').GetValue($null)
$f3IdentityOf = $f3Assembly.GetType('SnowStack.EncodingProbe.PowerShell.Internal.FileIdentity').GetMethod('Of', [Reflection.BindingFlags]'Public, NonPublic, Static')
$f3Dir = New-F2Folder 'directwrite'

function Get-F3FileId {
    param([string]$Path)

    # 実体の鍵（ボリュームのシリアル番号とファイル ID）。モジュールの内部の FileIdentity を使う（fsutil は環境によって使えない）
    return $f3IdentityOf.Invoke($null, @($Path)).EntityKey
}

function Format-F3File {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return '(ファイルなし)' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -le 16) { return (Format-FileBytes $Path) }
    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('X2') }) -join '').Substring(0, 16)
    return ('{0} bytes sha256={1}' -f $bytes.Length, $hash)
}

$f3Writers = [ordered]@{
    'Set' = { param($p, $v) Set-ProbedContent -LiteralPath $p -Value $v -Encoding utf8NoBOM -NoNewline }
    'Out' = { param($p, $v) $v | Out-ProbedFile -LiteralPath $p -Encoding utf8NoBOM -NoNewline }
    'Export' = { param($p, $v) [pscustomobject]@{ A = $v } | Export-ProbedCsv -LiteralPath $p -Encoding utf8NoBOM }
}
$f3Index = 0
# 書く内容の大きさと、元のファイルの大きさ（-1 はまだ無いファイル）。Export は見出しと引用符・改行の 9 バイトを引いて、ファイルの大きさをそろえる
foreach ($f3Case in @(
    @('content=T', $f3Threshold, 5),
    @('content=T+1', ($f3Threshold + 1), 5),
    @('original=T', 3, $f3Threshold),
    @('original=T+1', 3, ($f3Threshold + 1)),
    @('new-small', 3, -1),
    @('new-T+1', ($f3Threshold + 1), -1))) {
    foreach ($writerName in $f3Writers.Keys) {
        $f3Index++
        $cn = $f3Case[0]
        $contentLength = $f3Case[1]
        $originalLength = $f3Case[2]
        $wb = $f3Writers[$writerName]
        $valueLength = if ($writerName -eq 'Export' -and $contentLength -gt 9) { $contentLength - 9 } else { $contentLength }
        $p = Join-Path $f3Dir ("size{0}.txt" -f $f3Index)
        Add-Scenario "Finish/DirectWrite/Size/$cn/$writerName" {
            if ($originalLength -ge 0) { [IO.File]::WriteAllBytes($p, [byte[]]@(0x6F) * $originalLength) }
            $before = if ($originalLength -ge 0) { Get-F3FileId $p } else { $null }
            $r = Invoke-F2 { & $wb $p ('n' * $valueLength) }
            $entity = if ($null -eq $before) { 'new' } elseif ($before -eq (Get-F3FileId $p)) { 'same' } else { 'changed' }
            '{0} // {1} // entity={2} // tmp={3}' -f $r, (Format-F3File $p), $entity, @(Get-ChildItem -LiteralPath $f3Dir -Force -Filter '*.probedtmp').Count
        }.GetNewClosure()
    }
}

# Convert-ProbedContent（その場の変換と -Destination）も、小さなファイルには直接書く
$f3ConvertDest = Join-Path $f3Dir 'dest'
$null = New-Item -ItemType Directory -Path $f3ConvertDest
Add-Scenario 'Finish/DirectWrite/Convert/inplace' {
    $p = Join-Path $f3Dir 'convert.txt'
    [IO.File]::WriteAllBytes($p, [byte[]](0x6F, 0x6C, 0x64, 0x0D, 0x0A))
    $before = Get-F3FileId $p
    $r = Invoke-F2 { Convert-ProbedContent -LiteralPath $p -Encoding utf8BOM }
    '{0} // {1} // same={2}' -f $r, (Format-FileBytes $p), ($before -eq (Get-F3FileId $p))
}.GetNewClosure()
Add-Scenario 'Finish/DirectWrite/Convert/destination' {
    $p = Join-Path $f3Dir 'convert2.txt'
    $d = Join-Path $f3ConvertDest 'convert2.txt'
    [IO.File]::WriteAllBytes($p, [byte[]](0x6F, 0x6C, 0x64, 0x0D, 0x0A))
    [IO.File]::WriteAllBytes($d, [byte[]](0x70, 0x0D, 0x0A))
    $before = Get-F3FileId $d
    $r = Invoke-F2 { Convert-ProbedContent -LiteralPath $p -Destination $f3ConvertDest -Encoding utf8BOM -Force }
    '{0} // {1} // same={2}' -f $r, (Format-FileBytes $d), ($before -eq (Get-F3FileId $d))
}.GetNewClosure()

# 表現できない文字で失敗したら、元のファイルはバイト単位で元どおり（まだ無かったファイルは残らない）。-ErrorAction Stop も
$f3Original = [byte[]](0..255)
foreach ($f3Fail in @(
    @('Set', { param($p) Set-ProbedContent -LiteralPath $p -Value 'a', "$([char]0xD55C)" -Encoding shift_jis }),
    @('Set-Stop', { param($p) 'a', "$([char]0xD55C)" | Set-ProbedContent -LiteralPath $p -Encoding shift_jis -ErrorAction Stop }),
    @('Out', { param($p) 'a', "$([char]0xD55C)" | Out-ProbedFile -LiteralPath $p -Encoding shift_jis }),
    @('Export', { param($p) [pscustomobject]@{ A = 'a' }, [pscustomobject]@{ A = "$([char]0xD55C)" } | Export-ProbedCsv -LiteralPath $p -Encoding shift_jis }))) {
    foreach ($newFile in @($false, $true)) {
        $f3Index++
        $fn = $f3Fail[0]
        $fb = $f3Fail[1]
        $nf = $newFile
        $p = Join-Path $f3Dir ("fail{0}.txt" -f $f3Index)
        Add-Scenario "Finish/DirectWrite/Unrepresentable/$fn/new=$nf" {
            if (-not $nf) { [IO.File]::WriteAllBytes($p, $f3Original) }
            '{0} // {1}' -f (Invoke-F2 { & $fb $p }), (Format-F3File $p)
        }.GetNewClosure()
    }
}

# 直接書いている途中の IOException（ほかのハンドルが、元のファイルの末尾より先の範囲をロックしている）は WriteFailed で、元どおり
foreach ($writerName in $f3Writers.Keys) {
    $f3Index++
    $wb = $f3Writers[$writerName]
    $p = Join-Path $f3Dir ("lock{0}.txt" -f $f3Index)
    Add-Scenario "Finish/DirectWrite/LockedRange/$writerName" {
        [IO.File]::WriteAllBytes($p, [byte[]](0x6F, 0x6C, 0x64, 0x0D, 0x0A))
        $locker = New-Object IO.FileStream($p, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $locker.Lock(50, 10)
            $r = Invoke-F2 { & $wb $p ('n' * 100) }
            $locker.Unlock(50, 10)
        }
        finally { $locker.Dispose() }
        '{0} // {1}' -f $r, (Format-FileBytes $p)
    }.GetNewClosure()
}

# ほかのハンドルが開いているとき: 直接書く方法は相手が書き込みの共有を、置き換えは削除の共有を許していないと、完了の時点で失敗する
foreach ($f3Share in @('Read', 'ReadWrite', 'Read, Delete')) {
    foreach ($large in @($false, $true)) {
        $f3Index++
        $share = [IO.FileShare]$f3Share
        $lg = $large
        $p = Join-Path $f3Dir ("busy{0}.txt" -f $f3Index)
        Add-Scenario "Finish/DirectWrite/OpenedByAnother/$f3Share/large=$lg" {
            [IO.File]::WriteAllBytes($p, [byte[]]@(0x6F) * $(if ($lg) { $f3Threshold + 1 } else { 5 }))
            $other = New-Object IO.FileStream($p, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
            try { $r = Invoke-F2 { Set-ProbedContent -LiteralPath $p -Value new -Encoding utf8NoBOM -LineBreak CrLf } }
            finally { $other.Dispose() }
            '{0} // {1}' -f $r, (Format-F3File $p)
        }.GetNewClosure()
    }
}

# 1 回の Set-ProbedContent で複数のファイルに書く（パイプラインの各要素を、すべてのファイルに書く）
Add-Scenario 'Finish/DirectWrite/MultipleFiles' {
    $files = 1..5 | ForEach-Object { Join-Path $f3Dir ("multi{0}.txt" -f $_) }
    foreach ($f in $files[0..2]) { [IO.File]::WriteAllBytes($f, [byte[]](0x6F, 0x0D, 0x0A)) }
    $r = Invoke-F2 { 'a', 'b' | Set-ProbedContent -LiteralPath $files -Encoding utf8NoBOM -LineBreak CrLf }
    '{0} // {1}' -f $r, (($files | ForEach-Object { Format-FileBytes $_ }) -join ' / ')
}.GetNewClosure()

# ---------------------------------------------------------------------------
# 1.3.0 第六次 A（実装記録 11 章）
# ---------------------------------------------------------------------------
$f6Dir = Join-Path $script:WorkRoot 'sixth'
[void](New-Item -ItemType Directory -Path $f6Dir)
$f6Index = 0

# BUG-0037: UTF-7 の Encoding のインスタンスを、書き込みのすべてのコマンドが名前の utf7 と同じ ID で拒否する。ファイルは変わらない
$f6Utf7Writers = [ordered]@{
    'Set'     = { param($p, $e) Set-ProbedContent -LiteralPath $p -Value x -Encoding $e }
    'Add'     = { param($p, $e) Add-ProbedContent -LiteralPath $p -Value x -Encoding $e }
    'Out'     = { param($p, $e) 'x' | Out-ProbedFile -LiteralPath $p -Encoding $e }
    'Export'  = { param($p, $e) [pscustomobject]@{ A = 'x' } | Export-ProbedCsv -LiteralPath $p -Encoding $e }
    'Convert' = { param($p, $e) Convert-ProbedContent -LiteralPath $p -SourceEncoding utf8NoBOM -Encoding $e }
}
foreach ($f6Name in $f6Utf7Writers.Keys) {
    foreach ($f6Kind in @('name', 'instance')) {
        $f6Index++
        $wb = $f6Utf7Writers[$f6Name]
        $kind = $f6Kind
        $p = Join-Path $f6Dir ("utf7_{0}.txt" -f $f6Index)
        Add-Scenario "Sixth/Utf7/$f6Name/$kind" {
            [IO.File]::WriteAllBytes($p, [byte[]](0x41, 0x0D, 0x0A))
            $e = if ($kind -eq 'name') { 'utf7' } else { New-Object System.Text.UTF7Encoding }
            '{0} // {1}' -f (Invoke-F2 { & $wb $p $e }), (Format-FileBytes $p)
        }.GetNewClosure()
    }
}

# BUG-0033: -Value の配列の $null の要素は飛ばす（標準の Set-Content / Add-Content と同じ）。パイプラインで渡した場合と同じになる
foreach ($f6Cmd in @('Set-ProbedContent', 'Add-ProbedContent')) {
    foreach ($f6Shape in @('a,null,b', 'null,null', 'null')) {
        foreach ($f6Nn in @($false, $true)) {
            foreach ($f6Via in @('Value', 'Pipeline')) {
                $f6Index++
                $cmd = $f6Cmd; $nn = $f6Nn; $via = $f6Via
                $arr = switch ($f6Shape) { 'a,null,b' { @('a', $null, 'b') } 'null,null' { @($null, $null) } default { @($null) } }
                $p = Join-Path $f6Dir ("null_{0}.txt" -f $f6Index)
                Add-Scenario "Sixth/NullElement/$cmd/$f6Shape/NoNewline=$nn/$via" {
                    [IO.File]::WriteAllBytes($p, [byte[]](0x4F, 0x4C, 0x44, 0x0D, 0x0A))
                    $x = @{ LiteralPath = $p; Encoding = 'utf8NoBOM'; LineBreak = 'CrLf' }
                    if ($nn) { $x.NoNewline = $true; $x.Remove('LineBreak') }
                    if ($via -eq 'Value') { $r = Invoke-F2 { & $cmd -Value $arr @x } } else { $r = Invoke-F2 { $arr | & $cmd @x } }
                    '{0} // {1}' -f $r, (Format-FileBytes $p)
                }.GetNewClosure()
            }
        }
    }
}

# BUG-0032: ansi / oem と Locale のコードページは、スレッドのカルチャーではなくシステムのロケールの値。
# ホストによらず同じ値になることを、標準の 7.x の ansi / 5.1 の Default と一致するかで記録する（値そのものはホストの設定による）
foreach ($f6Culture in @('ja-JP', 'de-DE', 'en-US', 'ru-RU', 'ko-KR', '')) {
    $cu = $f6Culture
    Add-Scenario "Sixth/SystemCodePage/culture=$cu" {
        $old = [Threading.Thread]::CurrentThread.CurrentCulture
        try {
            [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($cu)
            $ansi = (ConvertTo-DotNetEncoding ansi).CodePage
            $oem = (ConvertTo-DotNetEncoding oem).CodePage
            $loc = (Get-EncodingProbePlatformInfo).Locale
            $system = if ($PSVersionTable.PSEdition -eq 'Core') { [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::InstalledUICulture.TextInfo.ANSICodePage).CodePage } else { [Text.Encoding]::Default.CodePage }
            'ansi=locale:{0} oem=locale:{1} sameAsInstalled:{2}' -f ($ansi -eq $loc.AnsiCodePage), ($oem -eq $loc.OemCodePage), ($ansi -eq $system)
        }
        finally { [Threading.Thread]::CurrentThread.CurrentCulture = $old }
    }.GetNewClosure()
}
foreach ($f6Zero in @('0', 0)) {
    $z = $f6Zero
    Add-Scenario "Sixth/CodePageZero/$($z.GetType().Name)" { Invoke-F2 { ConvertTo-DotNetEncoding $z } }.GetNewClosure()
}

# BUG-0021: BOM が 2 個続くファイルは、-Encoding の指定によらず先頭の 1 個だけを取り除く（2 個目の U+FEFF は文字として残る）
$f6DoubleBom = [ordered]@{
    'utf8'  = @{ Bytes = [byte[]](0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x0D, 0x0A); Names = @('Auto', 'utf8BOM', 'utf8NoBOM', 'utf-8') }
    'utf16' = @{ Bytes = [byte[]](0xFF, 0xFE, 0xFF, 0xFE, 0x41, 0, 0x2C, 0, 0x42, 0, 0x0D, 0, 0x0A, 0, 0x31, 0, 0x2C, 0, 0x32, 0, 0x0D, 0, 0x0A, 0); Names = @('Auto', 'unicodeBOM', 'unicodeNoBOM', 'unicode', 'utf-16') }
}
foreach ($f6Family in $f6DoubleBom.Keys) {
    foreach ($f6Name in $f6DoubleBom[$f6Family].Names) {
        $f6Index++
        $bytes = $f6DoubleBom[$f6Family].Bytes; $en = $f6Name
        $p = Join-Path $f6Dir ("dbom_{0}.csv" -f $f6Index)
        Add-Scenario "Sixth/DoubleBom/$f6Family/$en" {
            [IO.File]::WriteAllBytes($p, $bytes)
            $first = (Get-ProbedContent -LiteralPath $p -Encoding $en)[0]
            $col = (Import-ProbedCsv -LiteralPath $p -Encoding $en).PSObject.Properties.Name[0]
            $colHead = (Import-ProbedCsv -LiteralPath $p -Encoding $en -DetectionLineCount 1).PSObject.Properties.Name[0]
            $raw = Get-ProbedContent -LiteralPath $p -Encoding $en -Raw
            $q = $p + '.out'
            Set-ProbedContent -LiteralPath $q -Value $raw -NoNewline -EncodingFrom $p
            'line={0} col={1} head={2} roundtrip={3}' -f ([int][char]$first[0]), ([int][char]$col[0]), ([int][char]$colHead[0]), ((Format-FileBytes $q) -eq (Format-FileBytes $p))
        }.GetNewClosure()
    }
}
$f6Index++
$p = Join-Path $f6Dir ("dbom_{0}.csv" -f $f6Index)
Add-Scenario 'Sixth/DoubleBom/ExportAppend' {
    [IO.File]::WriteAllBytes($p, [byte[]](0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, 0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x0D, 0x0A))
    $r = Invoke-F2 { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append }
    '{0} // {1}' -f $r, (Format-FileBytes $p)
}.GetNewClosure()

# BUG-0042: 途中で切れたファイルの末尾は、5.1 でも 7.x と同じく置換フォールバックの文字として読む（以前の 5.1 は黙って捨てていた）。
# GB18030 の途中で切れた 4 バイトの列は、.NET Framework と .NET の復号器の違いで両ホストの結果が違う（直さない。実装記録 11 章）ので入れない。
# Convert-ProbedContent は今のまま（InvalidSourceBytes）
$f6Truncated = [ordered]@{
    'utf8-2of3'     = @('utf8NoBOM', [byte[]](0x41, 0x0D, 0x0A, 0xE6, 0x97))
    'utf8-3of4'     = @('utf8NoBOM', [byte[]](0x41, 0x0D, 0x0A, 0xF0, 0x9F, 0x98))
    'utf16le-odd'   = @('Auto', [byte[]](0xFF, 0xFE, 0x41, 0, 0x0D, 0, 0x0A, 0, 0x31, 0, 0x32))
    'utf16be-odd'   = @('bigendianunicodeNoBOM', [byte[]](0, 0x41, 0, 0x0D, 0, 0x0A, 0, 0x31, 0x32))
    'utf16le-hisur' = @('unicodeNoBOM', [byte[]](0x41, 0, 0x0D, 0, 0x0A, 0, 0x3D, 0xD8))
    'utf32le-1'     = @('utf32NoBOM', [byte[]](0x41, 0, 0, 0, 0x0A, 0, 0, 0, 0x31))
    'utf32le-3'     = @('utf32NoBOM', [byte[]](0x41, 0, 0, 0, 0x0A, 0, 0, 0, 0x31, 0, 0))
    'sjis-lead'     = @('shift_jis', [byte[]](0x41, 0x0D, 0x0A, 0x93, 0xFA, 0x93))
    'eucjp-lead'    = @('euc-jp', [byte[]](0x41, 0x0D, 0x0A, 0xC6))
    'cp949-lead'    = @('ks_c_5601-1987', [byte[]](0x41, 0x0D, 0x0A, 0xC7))
    'big5-lead'     = @('big5', [byte[]](0x41, 0x0D, 0x0A, 0xA4))
    'jis-half'      = @('iso-2022-jp', [byte[]](0x41, 0x0D, 0x0A, 0x1B, 0x24, 0x42, 0x46))
    'jis-esc'       = @('iso-2022-jp', [byte[]](0x41, 0x0D, 0x0A, 0x1B, 0x24))
    'kr-so-half'    = @('iso-2022-kr', [byte[]](0x1B, 0x24, 0x29, 0x43, 0x41, 0x0D, 0x0A, 0x0E, 0x30))
}
foreach ($f6Name in $f6Truncated.Keys) {
    $f6Index++
    $en = $f6Truncated[$f6Name][0]; $bytes = $f6Truncated[$f6Name][1]
    $p = Join-Path $f6Dir ("trunc_{0}.txt" -f $f6Index)
    Add-Scenario "Sixth/Truncated/$f6Name" {
        [IO.File]::WriteAllBytes($p, $bytes)
        $cp = { param($t) (($t.ToCharArray()) | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' ' }
        $lines = (Get-ProbedContent -LiteralPath $p -Encoding $en | ForEach-Object { & $cp $_ }) -join ' | '
        $raw = & $cp (Get-ProbedContent -LiteralPath $p -Encoding $en -Raw)
        $csv = (Import-ProbedCsv -LiteralPath $p -Encoding $en -Header H | ForEach-Object { & $cp $_.H }) -join ' | '
        $q = $p + '.c'; Copy-Item -LiteralPath $p -Destination $q
        $conv = Invoke-F2 { Convert-ProbedContent -LiteralPath $q -SourceEncoding $en -Encoding utf8NoBOM }
        'lines={0} raw={1} csv={2} convert={3} // {4}' -f $lines, $raw, $csv, $conv, (Format-FileBytes $q)
    }.GetNewClosure()
}

# BUG-0018: ISO-2022 の追記先が ASCII 以外の状態のまま終わっていれば、追記の前に戻すシーケンスを書く（戻す → 補う改行 → 内容）。
# エラーで止まったら戻すシーケンスも残さない。-AllowEncodingChange で別の文字エンコーディングを足すときは書かない
$f6JisTail = [byte[]](0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x1B, 0x24, 0x42, 0x46, 0x7C)
$f6Iso = [ordered]@{
    'Add'            = @($f6JisTail, { param($p) Add-ProbedContent -LiteralPath $p -Value 'xy' })
    'Out'            = @($f6JisTail, { param($p) 'xy' | Out-ProbedFile -LiteralPath $p -Append })
    'Export'         = @($f6JisTail, { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append })
    'Add/CrLf'       = @([byte[]]($f6JisTail + [byte[]](0x0D, 0x0A)), { param($p) Add-ProbedContent -LiteralPath $p -Value 'xy' })
    'Export/CrLf'    = @([byte[]]($f6JisTail + [byte[]](0x0D, 0x0A)), { param($p) [pscustomobject]@{ A = 'x'; B = 'y' } | Export-ProbedCsv -LiteralPath $p -Append })
    'Add/Japanese'   = @($f6JisTail, { param($p) Add-ProbedContent -LiteralPath $p -Value ('x' + [char]0x672C) })
    'Add/50222-SO'   = @([byte[]](0x41, 0x0D, 0x0A, 0x1B, 0x29, 0x49, 0x0E, 0x31), { param($p) Add-ProbedContent -LiteralPath $p -Value 'xy' -Encoding 50222 })
    'Add/KR-SO'      = @([byte[]](0x1B, 0x24, 0x29, 0x43, 0x41, 0x0D, 0x0A, 0x0E, 0x30, 0x21), { param($p) Add-ProbedContent -LiteralPath $p -Value 'xy' -Encoding iso-2022-kr })
    'Add/Rollback'   = @($f6JisTail, { param($p) Add-ProbedContent -LiteralPath $p -Value 'ok', ([string][char]0xD55C) })
    'Export/Rollback'= @($f6JisTail, { param($p) 'ok', ([string][char]0xD55C) | ForEach-Object { [pscustomobject]@{ A = $_; B = 'y' } } | Export-ProbedCsv -LiteralPath $p -Append })
    'Add/Empty'      = @($f6JisTail, { param($p) Add-ProbedContent -LiteralPath $p -Value @() })
    'Add/AllowChange'= @($f6JisTail, { param($p) Add-ProbedContent -LiteralPath $p -Value 'xy' -Encoding utf8NoBOM -AllowEncodingChange })
}
foreach ($f6Name in $f6Iso.Keys) {
    $f6Index++
    $bytes = $f6Iso[$f6Name][0]; $wb = $f6Iso[$f6Name][1]
    $p = Join-Path $f6Dir ("iso_{0}.txt" -f $f6Index)
    Add-Scenario "Sixth/Iso2022Append/$f6Name" {
        [IO.File]::WriteAllBytes($p, $bytes)
        '{0} // {1}' -f (Invoke-F2 { & $wb $p }), (Format-FileBytes $p)
    }.GetNewClosure()
}

# BUG-0020: CSV の列名の比較（Import の重複の検査・-Append の照合と値・-QuoteFields）は、5.1 でも .NET 10 の OrdinalIgnoreCase と同じ。
foreach ($f6Pair in @(@(0xC4, 0xE4), @(0x1C5, 0x1C6), @(0x1C5, 0x1C4), @(0x3C2, 0x3A3), @(0x10400, 0x10428), @(0xDF, 0x53), @(0x130, 0x69), @(0x212A, 0x6B), @(0xAD, 0x2D))) {
    $f6Index++
    $h = [char]::ConvertFromUtf32($f6Pair[0]); $pn = [char]::ConvertFromUtf32($f6Pair[1])
    $p = Join-Path $f6Dir ("name_{0}.csv" -f $f6Index)
    $q = Join-Path $f6Dir ("name_{0}q.csv" -f $f6Index)
    Add-Scenario ("Sixth/CsvName/{0:X4}-{1:X4}" -f $f6Pair[0], $f6Pair[1]) {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($p, "$h,$pn`r`n1,2`r`n", $utf8)
        $import = Invoke-F2 { @(Import-ProbedCsv -LiteralPath $p).Count }
        [IO.File]::WriteAllText($p, "$h,Z`r`n1,2`r`n", $utf8)
        $o = New-Object psobject; $o | Add-Member NoteProperty $pn 'x'; $o | Add-Member NoteProperty 'Z' 'y'
        $append = Invoke-F2 { $o | Export-ProbedCsv -LiteralPath $p -Append }
        $quote = Invoke-F2 { $o | Export-ProbedCsv -LiteralPath $q -QuoteFields $h }
        'import={0} append={1} // {2} quote={3} // {4}' -f $import, $append, (Format-FileBytes $p), $quote, (Format-FileBytes $q)
    }.GetNewClosure()
}

# 全符号位置: 列名の比較器が表の同値類どおりに比べること（7.x は実行環境の OrdinalIgnoreCase、5.1 は表）。
# ホストの .NET の大文字・小文字の組でも、比較器の結果が表と一致すること。結果は不一致の数だけを記録する（組の数はホストで違う）
Add-Type -TypeDefinition @'
using System; using System.Collections.Generic;
public static class ProbedCompatCsvNameProbe {
    static string S(int cp) { return (cp >= 0xD800 && cp <= 0xDFFF) ? ((char)cp).ToString() : char.ConvertFromUtf32(cp); }
    public static string Run(IEqualityComparer<string> comparer, int[] pairs) {
        var rep = new Dictionary<int, int>();
        for (int i = 0; i < pairs.Length; i += 2) { rep[pairs[i]] = pairs[i + 1]; }
        Func<int, int> r = c => rep.ContainsKey(c) ? rep[c] : c;
        int byTable = 0, byHostCase = 0, hostOnly = 0;
        for (int c = 0; c <= 0x10FFFF; c++) {
            string s = S(c);
            if (!comparer.Equals(s, S(r(c)))) { byTable++; }
            foreach (string d in new[] { s.ToUpperInvariant(), s.ToLowerInvariant() }) {
                if (d == s) { continue; }
                int dc = (d.Length == 2 && char.IsSurrogatePair(d[0], d[1])) ? char.ConvertToUtf32(d[0], d[1]) : (d.Length == 1 ? d[0] : -1);
                bool tableEqual = dc >= 0 && r(c) == r(dc);
                if (comparer.Equals(s, d) != tableEqual) { byHostCase++; }
                if (string.Equals(s, d, StringComparison.OrdinalIgnoreCase) && !tableEqual) { hostOnly++; }
            }
        }
        return string.Format("comparerVsTable={0} comparerVsTableOnHostCasePairs={1} hostEqualButTableNot={2}", byTable, byHostCase, hostOnly);
    }
}
'@
Add-Scenario 'Sixth/CsvName/AllCodePoints' {
    $asm = [SnowStack.EncodingProbe.PowerShell.Cmdlets.ImportProbedCsvCommand].Assembly
    $comparer = $asm.GetType('SnowStack.EncodingProbe.PowerShell.Internal.CsvNameComparer').GetField('Instance', [Reflection.BindingFlags]'Static,Public').GetValue($null)
    $pairs = $asm.GetType('SnowStack.EncodingProbe.PowerShell.Internal.CsvNameCaseTable').GetField('Pairs', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    [ProbedCompatCsvNameProbe]::Run($comparer, $pairs)
}

# BUG-0031: 短い EUC-JP（CP932 では割り当ての無い 2 バイトを含む）は、ja-JP でも改行によらず EUC-JP（20932）。-DetectionLineCount も同じ
$f6EucText = [byte[]](0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC, 0xA4, 0xCE, 0xCA, 0xB8, 0xBD, 0xF1, 0xA4, 0xC7, 0xA4, 0xB9)
foreach ($f6Lb in @('CRLF', 'LF', 'None')) {
    $f6Index++
    $bytes = [byte[]]($f6EucText + $(switch ($f6Lb) { 'CRLF' { [byte[]](0x0D, 0x0A) } 'LF' { [byte[]](0x0A) } default { [byte[]]@() } }))
    $p = Join-Path $f6Dir ("euc_{0}.txt" -f $f6Index)
    Add-Scenario "Sixth/ShortEucJp/$f6Lb" {
        [IO.File]::WriteAllBytes($p, $bytes)
        $cp = { param($t) (($t.ToCharArray()) | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' ' }
        'resolve={0} head={1} text={2}' -f (Resolve-Encoding -Path $p -Culture ja-JP).CodePage, (Resolve-Encoding -Path $p -Culture ja-JP -DetectionLineCount 1).CodePage, (& $cp (Get-ProbedContent -LiteralPath $p -Culture ja-JP -Raw))
    }.GetNewClosure()
}

# （第六次 A のシナリオはここより前に足す）

# ---------------------------------------------------------------------------
# 1.3.0 第六次 B（実装記録 12 章）
# ---------------------------------------------------------------------------
$f7Dir = Join-Path $script:WorkRoot 'sixthB'
[void](New-Item -ItemType Directory -Path $f7Dir)
$f7Index = 0
$f7Threshold = 1048576
function Format-F7Attributes { param([string]$Path) ([IO.File]::GetAttributes($Path) -band ([IO.FileAttributes]'Hidden, System, Archive, ReadOnly')).ToString() }

# BUG-0025: 隠し・システム属性のファイルに -Force なしで書け、代替データストリームを保ち、書いたら Archive 属性が付く。取り消しでは元の属性のまま
$f7Writers = [ordered]@{
    'Set'    = { param($p, $r) $r | Set-ProbedContent -LiteralPath $p -Encoding shift_jis }
    'Out'    = { param($p, $r) $r | Out-ProbedFile -LiteralPath $p -Encoding shift_jis }
    'Export' = { param($p, $r) $r | ForEach-Object { [pscustomobject]@{ A = $_ } } | Export-ProbedCsv -LiteralPath $p -Encoding shift_jis }
    'Add'    = { param($p, $r) $r | Add-ProbedContent -LiteralPath $p -Encoding shift_jis }
}
foreach ($f7Name in $f7Writers.Keys) {
    foreach ($f7Large in @($false, $true)) {
        foreach ($f7Fail in @($false, $true)) {
            $f7Index++
            $wb = $f7Writers[$f7Name]; $lg = $f7Large; $fl = $f7Fail
            $p = Join-Path $f7Dir ("attr_{0}.txt" -f $f7Index)
            Add-Scenario "SixthB/Archive/$f7Name/large=$lg/fail=$fl" {
                [IO.File]::WriteAllText($p, ('o' * $(if ($lg) { $f7Threshold + 10 } else { 3 })) + "`r`n")
                Set-Content -LiteralPath $p -Stream Zone.Identifier -Value '[ZoneTransfer]', 'ZoneId=3'
                [IO.File]::SetAttributes($p, 'Hidden, System')
                $first = if ($lg) { 'y' * ($f7Threshold + 10) } else { 'y' }
                $records = if ($fl) { @($first, [string][char]0xD55C) } else { @($first) }
                $r = Invoke-F2 { & $wb $p $records }
                $len = (New-Object IO.FileInfo $p).Length
                $stream = (Get-Content -LiteralPath $p -Stream Zone.Identifier) -join '|'
                $attr = Format-F7Attributes $p
                [IO.File]::SetAttributes($p, 'Normal')
                '{0} // len={1} attr={2} stream={3}' -f $r, $len, $attr, $stream
            }.GetNewClosure()
        }
    }
}

# BUG-0025: ACL で書き込みを拒否したファイルの WriteAccessDenied は、本モジュールのメッセージ（.NET の例外の文面ではない）
foreach ($f7Name in $f7Writers.Keys) {
    $f7Index++
    $wb = $f7Writers[$f7Name]
    $p = Join-Path $f7Dir ("acl_{0}.txt" -f $f7Index)
    Add-Scenario "SixthB/WriteAccessDenied/$f7Name" {
        [IO.File]::WriteAllText($p, "o`r`n")
        $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        & icacls.exe $p /deny "${user}:(WD,AD)" | Out-Null
        try {
            $message = $null
            $outcome = try { & $wb $p @('x') 2>&1 | ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $message = $_.Exception.Message; 'ERR ' + $_.FullyQualifiedErrorId.Split(',')[0] } } } catch { $message = $_.Exception.Message; 'TERM ' + $_.FullyQualifiedErrorId.Split(',')[0] }
        }
        finally { & icacls.exe $p /remove:d $user | Out-Null }
        '{0} // {1} // {2}' -f ($outcome -join ','), $message, (Format-FileBytes $p)
    }.GetNewClosure()
}

# BUG-0026: ほかのプログラムが開いているファイル。判定・変換の読み取りは Get-ProbedContent と同じ共有（ReadWrite）で開き、開けなければ ID の付いた 5 言語のメッセージ
$f7Readers = [ordered]@{
    'Resolve'      = { param($p, $d) Get-ChildItem -LiteralPath $d -Filter *.csv | Resolve-Encoding | ForEach-Object { $_.CodePage } }
    'Get'          = { param($p, $d) @(Get-ProbedContent -LiteralPath $p).Count }
    'Import'       = { param($p, $d) @(Import-ProbedCsv -LiteralPath $p).Count }
    'Convert'      = { param($p, $d) Convert-ProbedContent -LiteralPath $p -Destination (Join-Path $d 'out') -Encoding utf8BOM -PassThru | ForEach-Object { $_.Changed } }
    'EncodingFrom' = { param($p, $d) Set-ProbedContent -LiteralPath (Join-Path $d 'new.txt') -Value x -EncodingFrom $p }
    'Add'          = { param($p, $d) Add-ProbedContent -LiteralPath $p -Value '9,9' }
    'OutAppend'    = { param($p, $d) '9,9' | Out-ProbedFile -LiteralPath $p -Append }
    'ExportAppend' = { param($p, $d) [pscustomobject]@{ A = '9'; B = '9' } | Export-ProbedCsv -LiteralPath $p -Append }
}
foreach ($f7Share in @('ReadWrite', 'None')) {
    foreach ($f7Name in $f7Readers.Keys) {
        $f7Index++
        $rb = $f7Readers[$f7Name]; $share = $f7Share
        $d = Join-Path $f7Dir ("open_{0}" -f $f7Index)
        Add-Scenario "SixthB/OpenedByAnother/$f7Share/$f7Name" {
            [void](New-Item -ItemType Directory -Path $d)
            [void](New-Item -ItemType Directory -Path (Join-Path $d 'out'))
            foreach ($n in 'a', 'b', 'c') { [IO.File]::WriteAllBytes((Join-Path $d "$n.csv"), [byte[]](0x41, 0x2C, 0x42, 0x0D, 0x0A, 0x31, 0x2C, 0x32, 0x0D, 0x0A)) }
            $p = Join-Path $d 'b.csv'
            $other = New-Object IO.FileStream($p, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]$share)
            try {
                $messages = New-Object System.Collections.Generic.List[string]
                $outcome = @(try { & $rb $p $d 2>&1 | ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $messages.Add($_.Exception.Message); 'ERR ' + $_.FullyQualifiedErrorId.Split(',')[0] } else { [string]$_ } } } catch { $messages.Add($_.Exception.Message); 'TERM ' + $_.FullyQualifiedErrorId.Split(',')[0] })
            }
            finally { $other.Dispose() }
            '{0} // {1} // {2}' -f ($outcome -join ','), ($messages -join ' | '), (Format-FileBytes $p)
        }.GetNewClosure()
    }
}

# BUG-0027: 一時ファイルの名前は元の名前によらない固定長（.~probed<8 桁>.probedtmp）。1 MiB を超える書き込みで、
# 名前の長いファイル（パス全体は 260 文字未満）も書け、5.1 で一時ファイルのパスだけが上限を超える場合は写してから直接書く方法に切り替える
$f7Short = Join-Path ([IO.Path]::GetPathRoot($script:WorkRoot)) ('pe27_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[void](New-Item -ItemType Directory -Path $f7Short)
$f7Deep = $f7Short
while ($f7Deep.Length -lt 240) { $f7Deep = Join-Path $f7Deep ('d' * [Math]::Min(40, 239 - $f7Deep.Length)) }
[void](New-Item -ItemType Directory -Path $f7Deep -Force)
$f7LongCases = [ordered]@{
    'LongName240' = Join-Path $f7Short (('n' * 236) + '.txt')
    'DeepFolder'  = Join-Path $f7Deep 'a.txt'
}
$f7LongWriters = [ordered]@{
    'Set'     = { param($p, $v) Set-ProbedContent -LiteralPath $p -Value $v -Encoding utf8NoBOM -NoNewline }
    'Convert' = { param($p, $v) Convert-ProbedContent -LiteralPath $p -SourceEncoding utf8NoBOM -Encoding utf8BOM }
}
foreach ($f7Case in $f7LongCases.Keys) {
    foreach ($f7Name in $f7LongWriters.Keys) {
        $p = $f7LongCases[$f7Case]; $wb = $f7LongWriters[$f7Name]
        Add-Scenario "SixthB/LongPath/$f7Case/$f7Name" {
            [IO.File]::WriteAllText($p, 'o' * ($f7Threshold + 10))
            $r = Invoke-F2 { & $wb $p ('y' * ($f7Threshold + 20)) }
            $leftovers = @(Get-ChildItem -LiteralPath (Split-Path -Parent $p) -Force -Filter '*.probedtmp').Count
            $len = (New-Object IO.FileInfo $p).Length
            [IO.File]::Delete($p)
            '{0} // len={1} tmp={2} pathLength={3} temporaryPathOver259={4}' -f $r, $len, $leftovers, $p.Length, ((Split-Path -Parent $p).Length + 27 -gt 259)
        }.GetNewClosure()
    }
}
Add-Scenario 'SixthB/LongPath/Cleanup' { Remove-Item -LiteralPath $f7Short -Recurse -Force; Test-Path -LiteralPath $f7Short }.GetNewClosure()

# （第六次 B のシナリオはここより前に足す）

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