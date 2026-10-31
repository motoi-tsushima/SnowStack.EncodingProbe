# Measure-CsvBehavior.ps1
# SnowStack.EncodingProbe.PowerShell 1.3.0 design: measure the standard CSV cmdlets
# on Windows PowerShell 5.1 and PowerShell 7.x, and write a report that can be diffed.
#
# Usage (run on both hosts):
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Measure-CsvBehavior.ps1
#   pwsh           -NoProfile -File tools\Measure-CsvBehavior.ps1
#
# Output: csv-measure-<Edition>-<Version>.txt in -OutputDirectory
#         (default: tools\CsvBehaviorResults next to this script).
# Work files are created in a temporary folder and removed at the end.
# The results committed in tools\CsvBehaviorResults were taken on 2026-10-15
# (Windows 11, ja-JP, Windows PowerShell 5.1.26100.9711 and PowerShell 7.6.7).
# The report is ASCII only (non-ASCII characters are written as \uXXXX) so that
# the two reports can be compared with fc / Compare-Object / any diff tool.
# This script itself is ASCII only, so it runs the same on 5.1 and 7.x.

param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Continue'
$WarningPreference     = 'Continue'

$edition = $PSVersionTable.PSEdition
if (-not $edition) { $edition = 'Desktop' }
$isCore  = ($edition -eq 'Core')
$version = $PSVersionTable.PSVersion.ToString()

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $here 'CsvBehaviorResults' }
if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
$work   = Join-Path ([System.IO.Path]::GetTempPath()) ("csv-measure-work-{0}-{1}" -f $edition, [System.Guid]::NewGuid().ToString('N'))
$report = Join-Path $OutputDirectory ("csv-measure-{0}-{1}.txt" -f $edition, $version)

if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

if ($isCore) {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$sjis      = [System.Text.Encoding]::GetEncoding(932)

# Japanese test strings built from code points (keeps this file ASCII only)
$JP1 = -join ([char]0x65E5, [char]0x672C, [char]0x8A9E)   # "nihongo"
$JP2 = -join ([char]0x8FFD, [char]0x8A18)                  # "tsuiki"

$lines = New-Object System.Collections.Generic.List[string]

function Esc([string]$s) {
    if ($null -eq $s) { return '<null>' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq "`r") { [void]$sb.Append('\r') }
        elseif ($ch -eq "`n") { [void]$sb.Append('\n') }
        elseif ($ch -eq "`t") { [void]$sb.Append('\t') }
        elseif ($code -lt 0x20 -or $code -gt 0x7E) { [void]$sb.Append(('\u{0:X4}' -f $code)) }
        else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

function Out-Line([string]$s) { $script:lines.Add($s) }

function Section([string]$title) {
    Out-Line ''
    Out-Line ('=' * 78)
    Out-Line $title
    Out-Line ('=' * 78)
}

function Case([string]$title) {
    Out-Line ''
    Out-Line ('--- ' + $title)
}

function Format-Item($item) {
    if ($null -eq $item) { return 'NULL' }
    if ($item -is [System.Management.Automation.WarningRecord]) { return 'WARNING: ' + (Esc $item.Message) }
    if ($item -is [System.Management.Automation.ErrorRecord]) {
        return 'ERROR: ' + $item.FullyQualifiedErrorId + ' :: ' + (Esc $item.Exception.Message)
    }
    if ($item -is [string]) { return 'STR: ' + (Esc $item) }
    $parts = @()
    foreach ($p in $item.PSObject.Properties) {
        $v = $p.Value
        if ($null -eq $v) { $s = '<null>' } else { $s = Esc ([string]$v) }
        $parts += ('{0}=[{1}]' -f (Esc $p.Name), $s)
    }
    $type = ''
    if ($item.PSObject.TypeNames.Count -gt 0) { $type = $item.PSObject.TypeNames[0] }
    return 'OBJ<' + (Esc $type) + '> {' + ($parts -join ' | ') + '}'
}

# Run a script block, capture output, warnings, non-terminating and terminating errors.
function Invoke-Capture([scriptblock]$sb) {
    $result = New-Object System.Collections.Generic.List[string]
    try {
        $items = @(& $sb 2>&1 3>&1)
        foreach ($i in $items) { $result.Add((Format-Item $i)) }
        if ($items.Count -eq 0) { $result.Add('(no output)') }
    }
    catch {
        $result.Add('TERMINATING: ' + $_.FullyQualifiedErrorId + ' :: ' + (Esc $_.Exception.Message))
    }
    return ,$result
}

function Write-Capture($captured) { foreach ($l in $captured) { Out-Line ('  ' + $l) } }

function Get-Hex([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '<no file>' }
    $b = [System.IO.File]::ReadAllBytes($path)
    if ($b.Length -eq 0) { return '<0 bytes>' }
    return (($b | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

function New-TextFile([string]$name, [string]$text, $encoding) {
    if ($null -eq $encoding) { $encoding = $script:utf8NoBom }
    $p = Join-Path $script:work $name
    [System.IO.File]::WriteAllBytes($p, $encoding.GetBytes($text))
    return $p
}

function New-ByteFile([string]$name, [byte[]]$bytes) {
    $p = Join-Path $script:work $name
    [System.IO.File]::WriteAllBytes($p, $bytes)
    return $p
}

# ---------------------------------------------------------------------------
Section 'M0  Environment'
Out-Line ('PSEdition      : ' + $edition)
Out-Line ('PSVersion      : ' + $version)
Out-Line ('OS             : ' + [System.Environment]::OSVersion.VersionString)
Out-Line ('CurrentCulture : ' + [System.Globalization.CultureInfo]::CurrentCulture.Name)
Out-Line ('ListSeparator  : ' + (Esc ([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.ListSeparator)))
Out-Line ('Encoding.Default CodePage/WebName : ' + [System.Text.Encoding]::Default.CodePage + ' / ' + [System.Text.Encoding]::Default.WebName)
Out-Line ('NewLine        : ' + (Esc ([System.Environment]::NewLine)))

# ---------------------------------------------------------------------------
Section 'M1  Import-Csv vs ConvertFrom-Csv (whole string) vs ConvertFrom-Csv (line by line)'
Out-Line 'Purpose: confirm that ConvertFrom-Csv given the WHOLE file as one string gives the'
Out-Line 'same result as Import-Csv, and that line-by-line input does not.'

$m1 = [ordered]@{
    'c01 quoted CRLF inside field'         = "Name,Memo`r`n`"a`",`"line1`r`nline2`"`r`n`"b`",`"x`"`r`n"
    'c02 quoted LF inside CRLF file'       = "Name,Memo`r`n`"a`",`"line1`nline2`"`r`n"
    'c03 data row starting with #'         = "Name,Memo`r`n#1,abc`r`n2,def`r`n"
    'c04 #TYPE line'                       = "#TYPE Foo.Bar`r`nName,Memo`r`na,b`r`n"
    'c05 comment line before header'       = "# comment`r`nName,Memo`r`na,b`r`n"
    'c06 W3C #Fields'                      = "#Software: x`r`n#Fields: date time`r`n2026 10`r`n"
    'c07 blank lines'                      = "Name,Memo`r`n`r`na,b`r`n`r`n"
    'c08 spaces around values'             = "Name,Memo`r`n  a , `"b`"  `r`n"
    'c09 ragged rows, no final newline'    = "A,B`r`n1,2,3`r`n4`r`n5,6"
    'c10 duplicate header'                 = "A,A`r`n1,2`r`n"
    'c11 empty header name'                = "A,,C`r`n1,2,3`r`n"
    'c12 CR only line breaks'              = "A,B`r1,2`r3,4`r"
    'c13 Japanese values (UTF-8)'          = "A,B`r`n$JP1,$JP2`r`n"
    'c14 trailing delimiter in header'     = "A,B,`r`n1,2,`r`n"
    'c15 escaped quotes'                   = "A,B`r`n`"he said `"`"hi`"`"`",x`r`n"
}

$n = 0
foreach ($key in $m1.Keys) {
    $n++
    $p = New-TextFile ("m1_{0:D2}.csv" -f $n) $m1[$key] $null
    Case $key
    Out-Line ('  bytes: ' + (Get-Hex $p))

    $a = Invoke-Capture { Import-Csv -LiteralPath $p -Encoding UTF8 }
    $b = Invoke-Capture { ConvertFrom-Csv -InputObject ([System.IO.File]::ReadAllText($p, $utf8NoBom)) }
    $c = Invoke-Capture { Get-Content -LiteralPath $p -Encoding UTF8 | ConvertFrom-Csv }

    $sameAB = (($a -join "`n") -ceq ($b -join "`n"))
    $sameAC = (($a -join "`n") -ceq ($c -join "`n"))
    Out-Line ('  Import == WholeString : ' + $sameAB)
    Out-Line ('  Import == LineByLine  : ' + $sameAC)
    Out-Line '  [Import-Csv]'
    Write-Capture $a
    if (-not $sameAB) { Out-Line '  [ConvertFrom-Csv whole string]'; Write-Capture $b }
    if (-not $sameAC) { Out-Line '  [ConvertFrom-Csv line by line]';  Write-Capture $c }
}

# ---------------------------------------------------------------------------
Section 'M2  Import-Csv -Path wildcard handling'

[void](New-TextFile 'w1.csv' "A`r`n1`r`n" $null)
[void](New-TextFile 'w2.csv' "A`r`n2`r`n" $null)
[void](New-TextFile '[x].csv' "A`r`n3`r`n" $null)

Case 'Path w*.csv (two matches)'
Write-Capture (Invoke-Capture { Import-Csv -Path (Join-Path $work 'w*.csv') })
Case 'Path zz*.csv (no match)'
Write-Capture (Invoke-Capture { Import-Csv -Path (Join-Path $work 'zz*.csv') })
Case 'Path [x].csv (brackets as wildcard)'
Write-Capture (Invoke-Capture { Import-Csv -Path (Join-Path $work '[x].csv') })
Case 'LiteralPath [x].csv'
Write-Capture (Invoke-Capture { Import-Csv -LiteralPath (Join-Path $work '[x].csv') })
Case 'Path array (w1.csv, missing.csv, w2.csv)'
Write-Capture (Invoke-Capture { Import-Csv -Path @((Join-Path $work 'w1.csv'), (Join-Path $work 'missing.csv'), (Join-Path $work 'w2.csv')) })
Case 'Pipeline: Get-ChildItem w*.csv | Import-Csv'
Write-Capture (Invoke-Capture { Get-ChildItem -Path $work -Filter 'w*.csv' | Sort-Object Name | Import-Csv })

# ---------------------------------------------------------------------------
Section 'M3  Import-Csv -Encoding (Shift_JIS, strict decoder)'

$pSj = New-TextFile 'm3_sjis.csv' "A,B`r`n$JP1,x`r`n" $sjis
Case 'Shift_JIS file, -Encoding Default'
Write-Capture (Invoke-Capture { Import-Csv -LiteralPath $pSj -Encoding Default })

if ($isCore) {
    Case 'Shift_JIS file, -Encoding <Encoding object 932>'
    Write-Capture (Invoke-Capture { Import-Csv -LiteralPath $pSj -Encoding $sjis })
    Case 'Shift_JIS file, -Encoding 932 (int)'
    Write-Capture (Invoke-Capture { Import-Csv -LiteralPath $pSj -Encoding 932 })
    Case 'BOM-less UTF-8 Encoding object (UTF8Encoding $false)'
    $pU = New-TextFile 'm3_utf8.csv' "A,B`r`n$JP1,x`r`n" $null
    Write-Capture (Invoke-Capture { Import-Csv -LiteralPath $pU -Encoding $utf8NoBom })

    # 3000 good rows, then one invalid UTF-8 byte (0xFF), then more rows
    $ms = New-Object System.IO.MemoryStream
    $hdr = $utf8NoBom.GetBytes("A,B`r`n"); $ms.Write($hdr, 0, $hdr.Length)
    for ($i = 1; $i -le 3000; $i++) { $row = $utf8NoBom.GetBytes("$i,ok`r`n"); $ms.Write($row, 0, $row.Length) }
    $bad = [byte[]](0x78, 0x2C, 0xFF, 0x0D, 0x0A); $ms.Write($bad, 0, $bad.Length)
    $tail = $utf8NoBom.GetBytes("9999,ok`r`n"); $ms.Write($tail, 0, $tail.Length)
    $pBad = New-ByteFile 'm3_bad.csv' $ms.ToArray()

    $strict = [System.Text.Encoding]::GetEncoding(65001,
        [System.Text.EncoderFallback]::ExceptionFallback,
        [System.Text.DecoderFallback]::ExceptionFallback)

    Case 'Invalid byte after 3000 rows, strict Encoding object: records emitted before error'
    $count = 0
    try {
        Import-Csv -LiteralPath $pBad -Encoding $strict -ErrorAction Stop | ForEach-Object { $count++ }
        Out-Line '  no error'
    }
    catch {
        Out-Line ('  TERMINATING: ' + $_.FullyQualifiedErrorId + ' :: ' + (Esc $_.Exception.Message))
        $inner = $_.Exception
        while ($null -ne $inner.InnerException) { $inner = $inner.InnerException }
        Out-Line ('  innermost exception type: ' + $inner.GetType().FullName)
    }
    Out-Line ('  records emitted before error: ' + $count)

    Case 'Same file, default (replacement) decoding: value of the bad row'
    Write-Capture (Invoke-Capture { Import-Csv -LiteralPath $pBad -Encoding UTF8 | Where-Object { $_.A -eq 'x' } })
}
else {
    Case 'Desktop: -Encoding is an enum; Encoding objects / code pages are not accepted'
    Write-Capture (Invoke-Capture { Import-Csv -LiteralPath $pSj -Encoding $sjis })
}

# ---------------------------------------------------------------------------
Section 'M4  Export-Csv / ConvertTo-Csv defaults and value formatting'

function New-SampleObject {
    [pscustomobject]@{
        Name  = $JP1
        Quote = 'say "hi"'
        Multi = "l1`nl2"
        Comma = 'a,b'
        Flag  = $true
        When  = (New-Object DateTime 2026, 10, 15, 14, 30, 0)
        Num   = 1234.5
        Empty = $null
    }
}

Case 'Export-Csv with no options (bytes)'
$p = Join-Path $work 'm4_default.csv'
Write-Capture (Invoke-Capture { New-SampleObject | Export-Csv -LiteralPath $p })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'Export-Csv -NoTypeInformation -Encoding UTF8 (bytes)'
$p = Join-Path $work 'm4_utf8.csv'
Write-Capture (Invoke-Capture { New-SampleObject | Export-Csv -LiteralPath $p -NoTypeInformation -Encoding UTF8 })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'ConvertTo-Csv (no options)'
Write-Capture (Invoke-Capture { New-SampleObject | ConvertTo-Csv })

Case 'ConvertTo-Csv -NoTypeInformation'
Write-Capture (Invoke-Capture { New-SampleObject | ConvertTo-Csv -NoTypeInformation })

if ($isCore) {
    Case 'ConvertTo-Csv -UseQuotes AsNeeded'
    Write-Capture (Invoke-Capture { New-SampleObject | ConvertTo-Csv -UseQuotes AsNeeded })
    Case 'ConvertTo-Csv -UseQuotes Never'
    Write-Capture (Invoke-Capture { New-SampleObject | ConvertTo-Csv -UseQuotes Never })
    Case 'ConvertTo-Csv -QuoteFields Name,Num'
    Write-Capture (Invoke-Capture { New-SampleObject | ConvertTo-Csv -QuoteFields Name, Num })
    Case 'ConvertTo-Csv -NoHeader'
    Write-Capture (Invoke-Capture { New-SampleObject | ConvertTo-Csv -NoHeader })
}

Case 'ConvertTo-Csv of an ordered dictionary'
Write-Capture (Invoke-Capture { [ordered]@{ A = 1; B = 'x' } | ConvertTo-Csv -NoTypeInformation })

Case 'ConvertTo-Csv of a hashtable with a NoteProperty added'
Write-Capture (Invoke-Capture {
    $h = @{ A = 1 }
    $h = Add-Member -InputObject $h -NotePropertyName Extra -NotePropertyValue 'e' -PassThru
    $h | ConvertTo-Csv -NoTypeInformation })

Case 'ConvertTo-Csv of mixed objects (second has different properties)'
Write-Capture (Invoke-Capture {
    @([pscustomobject]@{ A = 1; B = 2 }, [pscustomobject]@{ B = 3; C = 4 }) | ConvertTo-Csv -NoTypeInformation })

Case 'ConvertTo-Csv of strings'
Write-Capture (Invoke-Capture { 'abc', 'de' | ConvertTo-Csv -NoTypeInformation })

Case 'ConvertTo-Csv -Delimiter ";"'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x;y'; B = 'z' } | ConvertTo-Csv -NoTypeInformation -Delimiter ';' })

Case 'ConvertTo-Csv -UseCulture'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 1.5; B = 'z' } | ConvertTo-Csv -NoTypeInformation -UseCulture })

# ---------------------------------------------------------------------------
Section 'M5  Export-Csv -Append'

$baseText = "`"A`",`"B`"`r`n`"1`",`"2`"`r`n"

function New-Base([string]$name) { return (New-TextFile $name $script:baseText $null) }

Case 'a) same properties, different order'
$p = New-Base 'm5_a.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ B = 'b'; A = 'a' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'b) missing column B, no -Force'
$p = New-Base 'm5_b.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'a' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'b2) missing column B, -Force'
$p = New-Base 'm5_b2.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'a' } | Export-Csv -LiteralPath $p -Append -Force })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'c) extra property C'
$p = New-Base 'm5_c.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'a'; B = 'b'; C = 'c' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'd) append to a 0-byte file'
$p = New-ByteFile 'm5_d.csv' ([byte[]]@())
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'a'; B = 'b' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'e) append to a file that does not exist'
$p = Join-Path $work 'm5_e.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'a'; B = 'b' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'f) UTF-8 with BOM target, append Japanese, no -Encoding'
$bomBytes = [byte[]](0xEF, 0xBB, 0xBF) + $utf8NoBom.GetBytes("`"A`",`"B`"`r`n`"$JP1`",`"x`"`r`n")
$p = New-ByteFile 'm5_f.csv' $bomBytes
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = $JP2; B = 'y' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'g) Shift_JIS target, append Japanese, no -Encoding'
$p = New-TextFile 'm5_g.csv' "`"A`",`"B`"`r`n`"$JP1`",`"x`"`r`n" $sjis
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = $JP2; B = 'y' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'h) -Append -NoHeader'
$p = New-Base 'm5_h.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'a'; B = 'b' } | Export-Csv -LiteralPath $p -Append -NoHeader })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'i) target header uses different case (a,b)'
$p = New-TextFile 'm5_i.csv' "a,b`r`n1,2`r`n" $null
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'j) target ends without a final newline'
$p = New-TextFile 'm5_j.csv' "A,B`r`n1,2" $null
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'k) target is LF only'
$p = New-TextFile 'm5_k.csv' "A,B`n1,2`n" $null
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'l) target is read-only, no -Force'
$p = New-Base 'm5_l.csv'
Set-ItemProperty -LiteralPath $p -Name IsReadOnly -Value $true
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-Csv -LiteralPath $p -Append })
Out-Line ('  bytes: ' + (Get-Hex $p))
Out-Line ('  IsReadOnly after: ' + (Get-Item -LiteralPath $p).IsReadOnly)

Case 'l2) target is read-only, -Force'
$p = New-Base 'm5_l2.csv'
Set-ItemProperty -LiteralPath $p -Name IsReadOnly -Value $true
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-Csv -LiteralPath $p -Append -Force })
Out-Line ('  bytes: ' + (Get-Hex $p))
Out-Line ('  IsReadOnly after: ' + (Get-Item -LiteralPath $p).IsReadOnly)

# ---------------------------------------------------------------------------
Section 'M6  Same file in one pipeline / -NoClobber / -WhatIf'

Case 'Import-Csv r.csv | Export-Csv r.csv'
$p = New-Base 'm6_r.csv'
Write-Capture (Invoke-Capture { Import-Csv -LiteralPath $p | Export-Csv -LiteralPath $p -NoTypeInformation })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case '-NoClobber on an existing file'
$p = New-Base 'm6_nc.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-Csv -LiteralPath $p -NoClobber -NoTypeInformation })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case '-WhatIf on a new file (is the file created?)'
$p = Join-Path $work 'm6_whatif.csv'
Write-Capture (Invoke-Capture { [pscustomobject]@{ A = 'x'; B = 'y' } | Export-Csv -LiteralPath $p -NoTypeInformation -WhatIf })
Out-Line ('  bytes: ' + (Get-Hex $p))

Case 'No input objects (empty pipeline): is the file created?'
$p = Join-Path $work 'm6_empty.csv'
Write-Capture (Invoke-Capture { @() | Export-Csv -LiteralPath $p -NoTypeInformation })
Out-Line ('  bytes: ' + (Get-Hex $p))

# ---------------------------------------------------------------------------
[System.IO.File]::WriteAllLines($report, $lines.ToArray(), $utf8NoBom)
Get-ChildItem -LiteralPath $work -Recurse -File | ForEach-Object { $_.IsReadOnly = $false }
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host ('Report written: ' + $report)
