#Requires -Version 5.1
<#
.SYNOPSIS
    0x00 を含む入力の判定を、現状と修正案のビルド × PowerShell 5.1（net48）/ 7.x（net10.0）で測定し、比べる。

.DESCRIPTION
    Measure-NulInputDetection.ps1 を「ビルドの数 × ホスト 2」通りの別プロセスで実行し、
    結果の TSV を OutDirectory に集め、比較の集計を summary.txt に書き出す。
    同名のアセンブリ（SnowStack.EncodingProbe.dll）を 1 つのプロセスに 2 つ読み込めないため、プロセスを分けている。

    第 1 回（-StrictRoot を指定しない）は、現状（current）と修正案 1（proposal）の 2 つのビルドを比べる。
    第 2 回（-StrictRoot を指定する）は、修正案 2（strict）を加えた 3 つのビルドを比べる。
    以下の 1〜5 の「修正案」は、第 1 回では修正案 1、第 2 回では修正案 2 を指す。

    集計する内容（docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md の要約に対応する）:
      1. C 群で、現状と修正案の結果が変わった行
      2. A 群で、修正案が UTF-16 / UTF-32 と答えなかった入力
      3. 0x00 を含み、修正案が UTF-16 / UTF-32 と答えなかった入力の結果（20127 のままか）
      4. B 群で、修正案が UTF-16 / UTF-32 と答えた入力
      5. 判定方式・カルチャー・TFM による結果の違い
    第 2 回では、さらに次を集計する。
      6. 第 1 回の結果（OutDirectory の親の TSV）と、第 2 回で測り直した現状・修正案 1 の結果の一致
      7. 修正案 1 と修正案 2 で結果が違う行
      8. 修正案 2 が UTF-16 / UTF-32 と答えた入力と、成立した規則（依頼 2 章の表の 1〜4）
      9. 第 2 回で追加した入力（A6・B9〜B12）の 3 つのビルドの結果
    加えて、すべての入力の対照表（ja-JP / NativeOnly と Combined）とコマンドの対照表を書き出す。

    すべてのビルドは事前に dotnet build -c <Configuration> しておくこと。

.PARAMETER CurrentRoot
    現状のビルドのリポジトリのルート。既定はこのスクリプトのリポジトリ。

.PARAMETER ProposalRoot
    修正案 1 のビルドのリポジトリ（worktree）のルート。

.PARAMETER StrictRoot
    修正案 2 のビルドのリポジトリ（worktree）のルート。指定すると第 2 回の測定になる。

.PARAMETER OutDirectory
    結果の出力先。既定は tools/NulInputDetectionResults（第 2 回は tools/NulInputDetectionResults/round2）。

.EXAMPLE
    # 第 1 回
    pwsh -NoProfile -File tools/Invoke-NulInputMeasurement.ps1 -ProposalRoot ..\EP-nul

.EXAMPLE
    # 第 2 回
    pwsh -NoProfile -File tools/Invoke-NulInputMeasurement.ps1 -ProposalRoot ..\EP-nul -StrictRoot ..\EP-nul2
#>
[CmdletBinding()]
param(
    [string] $CurrentRoot,
    [Parameter(Mandatory = $true)] [string] $ProposalRoot,
    [string] $StrictRoot,
    [string] $Configuration = 'Debug',
    [string] $OutDirectory,
    # 測定をせず、OutDirectory にある TSV から集計だけをやり直す
    [switch] $SkipMeasurement
)

$ErrorActionPreference = 'Stop'

$isRound2 = -not [string]::IsNullOrEmpty($StrictRoot)
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$round1Directory = Join-Path $PSScriptRoot 'NulInputDetectionResults'
if ([string]::IsNullOrEmpty($CurrentRoot)) { $CurrentRoot = $repositoryRoot }
if ([string]::IsNullOrEmpty($OutDirectory)) {
    $OutDirectory = if ($isRound2) { Join-Path $round1Directory 'round2' } else { $round1Directory }
}
if (-not (Test-Path -LiteralPath $OutDirectory)) { New-Item -ItemType Directory -Path $OutDirectory | Out-Null }
$CurrentRoot = (Resolve-Path -LiteralPath $CurrentRoot).Path
$ProposalRoot = (Resolve-Path -LiteralPath $ProposalRoot).Path
$OutDirectory = (Resolve-Path -LiteralPath $OutDirectory).Path

$measureScript = Join-Path $PSScriptRoot 'Measure-NulInputDetection.ps1'
$hosts = @(
    [PSCustomObject]@{ Tfm = 'net48'; Executable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe' }
    [PSCustomObject]@{ Tfm = 'net10.0'; Executable = 'pwsh' }
)
$builds = @(
    [PSCustomObject]@{ Name = 'current'; Root = $CurrentRoot }
    [PSCustomObject]@{ Name = 'proposal'; Root = $ProposalRoot }
)
if ($isRound2) {
    $builds += [PSCustomObject]@{ Name = 'strict'; Root = (Resolve-Path -LiteralPath $StrictRoot).Path }
}
# 1〜5 で「修正案」として現状と比べるビルド
$target = if ($isRound2) { 'strict' } else { 'proposal' }

foreach ($build in $builds) {
    if ($SkipMeasurement) { break }
    foreach ($targetHost in $hosts) {
        Write-Host ('測定中: {0} / {1}' -f $build.Name, $targetHost.Tfm)
        & $targetHost.Executable -NoProfile -ExecutionPolicy Bypass -File $measureScript `
            -RepositoryRoot $build.Root -Configuration $Configuration -BuildName $build.Name -OutDirectory $OutDirectory
        if ($LASTEXITCODE -ne 0) { throw ('{0} / {1} で測定に失敗しました。' -f $build.Name, $targetHost.Tfm) }
    }
}

#--------------------------------------------------------------------------
# 集計
#--------------------------------------------------------------------------
function Read-Tsv([string] $Name, [string] $Directory = $OutDirectory) {
    return @(Import-Csv -LiteralPath (Join-Path $Directory $Name) -Delimiter "`t" -Encoding UTF8)
}

$detect = @{}
$commands = @{}
foreach ($build in $builds) {
    foreach ($targetHost in $hosts) {
        $key = '{0}-{1}' -f $build.Name, $targetHost.Tfm
        $detect[$key] = Read-Tsv ('detect-{0}.tsv' -f $key)
        $commands[$key] = Read-Tsv ('commands-{0}.tsv' -f $key)
    }
}
$diag = Read-Tsv ('diag-{0}-net10.0.tsv' -f $target)
$diagById = @{}
foreach ($d in $diag) { $diagById[$d.id] = $d }

function Format-Result($Row) { return ('{0} / {1} / {2} / {3}' -f $Row.CodePage, $Row.EncodingWebName, $Row.Bom, $Row.LineBreak) }
function Get-Key($Row) { return ('{0}|{1}|{2}' -f $Row.id, $Row.culture, $Row.strategy) }
function Test-Unicode($Row) { return @('1200', '1201', '12000', '12001') -contains $Row.CodePage }

$script:summary = New-Object System.Collections.Generic.List[string]
function Out-Line([string] $Text = '') { $script:summary.Add($Text); Write-Host $Text }

# 行の照合: すべての TSV は同じ行数で、キー（入力 × カルチャー × 判定方式）の並びが一致すること
$baseRows = $detect['current-net10.0']
foreach ($key in $detect.Keys) {
    if ($detect[$key].Count -ne $baseRows.Count) { throw ('行数が異なる: {0}' -f $key) }
    for ($i = 0; $i -lt $baseRows.Count; $i++) {
        if ((Get-Key $detect[$key][$i]) -cne (Get-Key $baseRows[$i])) { throw ('行の並びが異なる: {0} の {1} 行目' -f $key, ($i + 2)) }
    }
}
foreach ($key in $commands.Keys) {
    $baseCommands = $commands['current-net10.0']
    for ($i = 0; $i -lt $baseCommands.Count; $i++) {
        if ($commands[$key][$i].id -cne $baseCommands[$i].id -or $commands[$key][$i].command -cne $baseCommands[$i].command) { throw ('コマンドの行の並びが異なる: {0}' -f $key) }
    }
}

Out-Line ('ビルド: {0}（1〜5 の「修正案」= {1}）' -f (($builds | ForEach-Object { $_.Name }) -join ', '), $target)
Out-Line ('入力の数: {0}（判定 API の行数: {1} = 入力 × カルチャー 2 × 判定方式 3）' -f $diag.Count, $baseRows.Count)
Out-Line ''

# 5（TFM）: 同じビルドの net48 と net10.0 の違い
Out-Line '## TFM による違い（同じビルドの net48 と net10.0）'
foreach ($build in $builds) {
    $a = $detect[('{0}-net48' -f $build.Name)]; $b = $detect[('{0}-net10.0' -f $build.Name)]
    $count = 0
    for ($i = 0; $i -lt $a.Count; $i++) {
        if ((Format-Result $a[$i]) -cne (Format-Result $b[$i])) {
            $count++
            Out-Line ('  {0} {1}: net48 = {2} / net10.0 = {3}' -f $build.Name, (Get-Key $a[$i]), (Format-Result $a[$i]), (Format-Result $b[$i]))
        }
    }
    Out-Line ('  {0}: {1} 行で違い' -f $build.Name, $count)
    $ca = $commands[('{0}-net48' -f $build.Name)]; $cb = $commands[('{0}-net10.0' -f $build.Name)]
    $ccount = 0
    for ($i = 0; $i -lt $ca.Count; $i++) {
        if ($ca[$i].result -cne $cb[$i].result) {
            $ccount++
            Out-Line ('  {0} コマンド {1} {2}: net48 = {3} / net10.0 = {4}' -f $build.Name, $ca[$i].id, $ca[$i].command, $ca[$i].result, $cb[$i].result)
        }
    }
    Out-Line ('  {0}: コマンドは {1} 行で違い' -f $build.Name, $ccount)
}
Out-Line ''

$zeroIds = @{}
foreach ($d in $diag) { if ([int]$d.zeros -gt 0) { $zeroIds[$d.id] = $d } }

foreach ($tfm in 'net10.0', 'net48') {
    $cur = $detect[('current-{0}' -f $tfm)]
    $pro = $detect[('{0}-{1}' -f $target, $tfm)]

    Out-Line ('# {0}' -f $tfm)
    Out-Line ''

    # 変化した行
    $changed = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $cur.Count; $i++) {
        if ((Format-Result $cur[$i]) -cne (Format-Result $pro[$i])) { $changed.Add([PSCustomObject]@{ Cur = $cur[$i]; Pro = $pro[$i] }) }
    }
    Out-Line ('## 現状と修正案（{0}）で結果が変わった行: {1} 行（入力 {2} 件）' -f $target, $changed.Count, @($changed | ForEach-Object { $_.Cur.id } | Select-Object -Unique).Count)
    foreach ($group in ($changed | Group-Object { $_.Cur.group } | Sort-Object Name)) {
        Out-Line ('  {0}: {1} 行' -f $group.Name, $group.Count)
    }
    Out-Line ''

    # 1. C 群の変化
    Out-Line '## 1. C 群で変わった行'
    $cChanged = @($changed | Where-Object { $_.Cur.group -like 'C*' })
    foreach ($c in $cChanged) { Out-Line ('  {0}: {1} → {2}' -f (Get-Key $c.Cur), (Format-Result $c.Cur), (Format-Result $c.Pro)) }
    Out-Line ('  計 {0} 行' -f $cChanged.Count)
    Out-Line ''

    # 2. A 群で修正案が UTF-16 / UTF-32 でない（第 1 回の A1〜A5。A6 は 9 で扱う）
    Out-Line '## 2. A 群（A1〜A5）で修正案が UTF-16 / UTF-32 と答えなかった行'
    $isA = { param($Row) $Row.group -like 'A*' -and $Row.group -notlike 'A6*' }
    # UtfUnknownOnly は独自判定を通らない（修正案の対象外）ので、件数だけ示す
    $aMissUu = @($pro | Where-Object { (& $isA $_) -and $_.strategy -eq 'UtfUnknownOnly' -and -not (Test-Unicode $_) })
    $aTotalUu = @($pro | Where-Object { (& $isA $_) -and $_.strategy -eq 'UtfUnknownOnly' })
    Out-Line ('  （UtfUnknownOnly: {0} 行中 {1} 行が UTF-16 / UTF-32 以外。内訳: {2}）' -f $aTotalUu.Count, $aMissUu.Count, ((($aMissUu | Group-Object { $_.CodePage + ' / ' + $_.EncodingWebName }) | ForEach-Object { '{0} = {1} 行' -f $_.Name, $_.Count }) -join ', '))
    $aMiss = @($pro | Where-Object { (& $isA $_) -and $_.strategy -ne 'UtfUnknownOnly' -and -not (Test-Unicode $_) })
    foreach ($r in $aMiss) {
        $d = $diagById[$r.id]
        Out-Line ('  {0}: {1}  [len={2} mod4={3} utf32={4} utf16={5} null16={6}/{7} null32={8}/{9} strictrule={10}]' -f (Get-Key $r), (Format-Result $r), $d.length, $d.mod4, $d.utf32, $d.utf16, $d.null16le, $d.null16be, $d.null32le, $d.null32be, $d.strictrule)
    }
    Out-Line ('  計 {0} 行（Combined / NativeOnly）' -f $aMiss.Count)
    # A 群で修正案のエンディアン・幅が入力と違うもの（id の末尾の形と照合）
    $expected = @{ u16le = '1200'; u16be = '1201'; u32le = '12000'; u32be = '12001' }
    $aWrong = @($pro | Where-Object { $_.group -like 'A*' -and (Test-Unicode $_) -and $expected[$_.id.Substring($_.id.LastIndexOf('-') + 1)] -ne $_.CodePage })
    Out-Line ('  A 群（A6 を含む）で UTF-16 / UTF-32 だが入力の形（幅・エンディアン）と違う行: {0} 行' -f $aWrong.Count)
    foreach ($r in $aWrong) { Out-Line ('    {0}: {1}' -f (Get-Key $r), (Format-Result $r)) }
    Out-Line ''

    # 3. 0x00 を含み UTF-16 / UTF-32 でない入力
    Out-Line '## 3. 0x00 を含み、修正案（NativeOnly）が UTF-16 / UTF-32 と答えなかった入力'
    $three = @($pro | Where-Object { $zeroIds.ContainsKey($_.id) -and $_.strategy -eq 'NativeOnly' -and -not (Test-Unicode $_) })
    foreach ($group in ($three | Group-Object { Format-Result $_ })) {
        Out-Line ('  {0}: {1} 行' -f $group.Name, $group.Count)
        foreach ($r in $group.Group) {
            $c = $cur | Where-Object { $_.id -eq $r.id -and $_.culture -eq $r.culture -and $_.strategy -eq $r.strategy } | Select-Object -First 1
            Out-Line ('    {0} (現状 {1})' -f (Get-Key $r), (Format-Result $c))
        }
    }
    Out-Line ''

    # 4. B 群で UTF-16 / UTF-32
    Out-Line '## 4. B 群で修正案が UTF-16 / UTF-32 と答えた入力（ja-JP / NativeOnly）'
    $bHit = @($pro | Where-Object { $_.group -like 'B*' -and $_.culture -eq 'ja-JP' -and $_.strategy -eq 'NativeOnly' -and (Test-Unicode $_) })
    foreach ($r in $bHit) {
        $d = $diagById[$r.id]
        Out-Line ('  {0}: {1}  [len={2} mod2={3} mod4={4} zeros={5} utf32={6} utf16={7} null16={8}/{9} null32={10}/{11} strictrule={12}] {13}' -f $r.id, (Format-Result $r), $d.length, $d.mod2, $d.mod4, $d.zeros, $d.utf32, $d.utf16, $d.null16le, $d.null16be, $d.null32le, $d.null32be, $d.strictrule, $d.label)
    }
    Out-Line ('  計 {0} 件' -f $bHit.Count)
    Out-Line ''

    # 5. 判定方式・カルチャーによる違い
    Out-Line '## 5. 判定方式・カルチャーによる違い（入力ごとに 6 条件の結果が一致しないもの）'
    foreach ($build in $builds) {
        $rows = $detect[('{0}-{1}' -f $build.Name, $tfm)]
        $diffCount = 0
        foreach ($g in ($rows | Group-Object id)) {
            # UtfUnknownOnly は同じ 20127 でも EncodingWebName が ascii になるので、CodePage と Bom だけで比べる
            $distinct = @($g.Group | ForEach-Object { '{0}/{1}' -f $_.CodePage, $_.Bom } | Select-Object -Unique)
            if ($distinct.Count -gt 1) {
                $diffCount++
                # C1 の東アジア以外などは UTF.Unknown の有無で結果が違うのが当然なので、0x00 を含む入力だけ詳しく出す
                if ($zeroIds.ContainsKey($g.Name)) {
                    $cells = $g.Group | ForEach-Object { '{0}/{1}={2}' -f $_.culture, $_.strategy, $_.CodePage }
                    Out-Line ('  {0} {1}: {2}' -f $build.Name, $g.Name, ($cells -join ', '))
                }
            }
        }
        Out-Line ('  {0}: 6 条件で結果が一致しない入力 {1} 件（0x00 を含む入力だけ上に列挙）' -f $build.Name, $diffCount)
    }
    $inconsistent = New-Object System.Collections.Generic.List[string]
    foreach ($g in ($changed | Group-Object { $_.Cur.id })) {
        # 修正案は独自判定の変更なので、変化は Combined / NativeOnly の 4 条件すべてで起き、UtfUnknownOnly では起きないはず
        $conds = @($g.Group | ForEach-Object { '{0}/{1}' -f $_.Cur.culture, $_.Cur.strategy })
        $uu = @($g.Group | Where-Object { $_.Cur.strategy -eq 'UtfUnknownOnly' })
        if ($conds.Count -ne 4 -or $uu.Count -ne 0) {
            $inconsistent.Add(('  {0}: 変化した条件 {1} 個（{2}）' -f $g.Name, $conds.Count, ($conds -join ', ')))
        }
    }
    Out-Line ('  変化が Combined / NativeOnly × 2 カルチャーの 4 条件ちょうどでなかった入力: {0} 件' -f $inconsistent.Count)
    foreach ($line in $inconsistent) { Out-Line $line }
    Out-Line ''

    if (-not $isRound2) { continue }

    # 7. 修正案 1 と修正案 2 の違い
    $p1 = $detect[('proposal-{0}' -f $tfm)]
    Out-Line '## 7. 修正案 1 と修正案 2 で結果が違う行'
    $diff12 = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $p1.Count; $i++) {
        if ((Format-Result $p1[$i]) -cne (Format-Result $pro[$i])) { $diff12.Add([PSCustomObject]@{ Cur = $cur[$i]; P1 = $p1[$i]; P2 = $pro[$i] }) }
    }
    Out-Line ('  {0} 行（入力 {1} 件）' -f $diff12.Count, @($diff12 | ForEach-Object { $_.P1.id } | Select-Object -Unique).Count)
    foreach ($r in ($diff12 | Where-Object { $_.P1.culture -eq 'ja-JP' -and $_.P1.strategy -eq 'NativeOnly' })) {
        $marks = @()
        if ((Format-Result $r.Cur) -cne (Format-Result $r.P1)) { $marks += '修正案 1 で変化' }
        if ((Format-Result $r.Cur) -cne (Format-Result $r.P2)) { $marks += '修正案 2 で変化' }
        Out-Line ('  {0}: 現状 {1} / 修正案 1 {2} / 修正案 2 {3}  [{4}]' -f $r.P1.id, (Format-Result $r.Cur), (Format-Result $r.P1), (Format-Result $r.P2), ($marks -join '、'))
    }
    Out-Line ''

    # 8. 修正案 2 が UTF-16 / UTF-32 と答えた入力と規則
    Out-Line '## 8. 修正案 2（NativeOnly / ja-JP）が UTF-16 / UTF-32 と答えた入力と、成立した規則'
    $hits = @($pro | Where-Object { $_.culture -eq 'ja-JP' -and $_.strategy -eq 'NativeOnly' -and (Test-Unicode $_) -and $zeroIds.ContainsKey($_.id) -and $diagById[$_.id].iso2022 -eq 'ascii' })
    foreach ($group in ($hits | Group-Object { $diagById[$_.id].strictrule } | Sort-Object Name)) {
        Out-Line ('  規則 {0}: {1} 件' -f $group.Name, $group.Count)
        foreach ($r in $group.Group) { Out-Line ('    {0}: {1} (単独呼び出し strict={2})' -f $r.id, (Format-Result $r), $diagById[$r.id].strict) }
    }
    Out-Line ''
}

if ($isRound2) {
    # 6. 第 1 回の結果と、第 2 回で測り直した現状・修正案 1 の一致
    Out-Line '## 6. 第 1 回の結果との一致（第 1 回と重なる入力）'
    foreach ($name in 'current', 'proposal') {
        foreach ($tfm in 'net10.0', 'net48') {
            $key = '{0}-{1}' -f $name, $tfm
            $old = Read-Tsv ('detect-{0}.tsv' -f $key) $round1Directory
            $newByKey = @{}
            foreach ($r in $detect[$key]) { $newByKey[(Get-Key $r)] = $r }
            $mismatch = 0; $missing = 0
            foreach ($r in $old) {
                $n = $newByKey[(Get-Key $r)]
                if ($null -eq $n) { $missing++; continue }
                if ((Format-Result $n) -cne (Format-Result $r)) {
                    $mismatch++
                    Out-Line ('  {0} {1}: 第 1 回 {2} / 第 2 回 {3}' -f $key, (Get-Key $r), (Format-Result $r), (Format-Result $n))
                }
            }
            Out-Line ('  {0}: 第 1 回の {1} 行のうち、不一致 {2} 行、第 2 回に無い行 {3} 行' -f $key, $old.Count, $mismatch, $missing)

            $oldCommands = Read-Tsv ('commands-{0}.tsv' -f $key) $round1Directory
            $newCommands = @{}
            foreach ($r in $commands[$key]) { $newCommands[('{0}|{1}' -f $r.id, $r.command)] = $r.result }
            $cmismatch = 0
            foreach ($r in $oldCommands) {
                if ($newCommands[('{0}|{1}' -f $r.id, $r.command)] -cne $r.result) {
                    $cmismatch++
                    Out-Line ('  {0} コマンド {1} {2}: 第 1 回 {3} / 第 2 回 {4}' -f $key, $r.id, $r.command, $r.result, $newCommands[('{0}|{1}' -f $r.id, $r.command)])
                }
            }
            Out-Line ('  {0}: コマンドは第 1 回の {1} 行のうち、不一致 {2} 行' -f $key, $oldCommands.Count, $cmismatch)
        }
    }
    Out-Line ''

    # 9. 追加した入力（net10.0、ja-JP / NativeOnly）
    Out-Line '## 9. 第 2 回で追加した入力（net10.0、ja-JP / NativeOnly）'
    foreach ($d in ($diag | Where-Object { $_.group -like 'A6*' -or $_.group -match '^B(9|1[0-2])' })) {
        $results = foreach ($build in $builds) {
            $r = $detect[('{0}-net10.0' -f $build.Name)] | Where-Object { $_.id -eq $d.id -and $_.culture -eq 'ja-JP' -and $_.strategy -eq 'NativeOnly' } | Select-Object -First 1
            '{0}={1}' -f $build.Name, (Format-Result $r)
        }
        Out-Line ('  {0} [{1}] len={2} strictrule={3}: {4}' -f $d.id, $d.head, $d.length, $d.strictrule, ($results -join ' | '))
    }
    Out-Line ''
}

#--------------------------------------------------------------------------
# 対照表（net10.0、ja-JP / NativeOnly と Combined）
#--------------------------------------------------------------------------
$buildNames = @($builds | ForEach-Object { $_.Name })
$header = @('group', 'id', 'label', 'length', 'zeros', 'head', 'utf32', 'utf16')
if ($isRound2) { $header += 'strictrule' }
foreach ($strategy in 'native', 'combined') {
    foreach ($name in $buildNames) { $header += ('{0}_{1}' -f $name, $strategy) }
    $header += ('changed_{0}' -f $strategy)
}
$header += 'current_utfunknown'
$tableRows = New-Object System.Collections.Generic.List[string]
$tableRows.Add($header -join "`t")

# 変化の印: 第 1 回は「変化」、第 2 回は現状から変わったビルドと、修正案 1 と 2 の違いを示す
function Get-ChangeMark([string[]] $Results) {
    if (-not $isRound2) { if ($Results[0] -ceq $Results[1]) { return '' } else { return '変化' } }
    $marks = @()
    if ($Results[0] -cne $Results[1]) { $marks += '1:変化' }
    if ($Results[0] -cne $Results[2]) { $marks += '2:変化' }
    if ($Results[1] -cne $Results[2]) { $marks += '1≠2' }
    return ($marks -join ' ')
}

$rowsByBuild = @{}
foreach ($name in $buildNames) {
    $index = @{}
    foreach ($r in $detect[('{0}-net10.0' -f $name)]) { if ($r.culture -eq 'ja-JP') { $index[('{0}|{1}' -f $r.id, $r.strategy)] = $r } }
    $rowsByBuild[$name] = $index
}
foreach ($d in $diag) {
    $cells = @($d.group, $d.id, $d.label, $d.length, $d.zeros, $d.head, $d.utf32, $d.utf16)
    if ($isRound2) { $cells += $d.strictrule }
    foreach ($strategy in 'NativeOnly', 'Combined') {
        $results = [string[]]@(foreach ($name in $buildNames) { Format-Result $rowsByBuild[$name][('{0}|{1}' -f $d.id, $strategy)] })
        $cells += $results
        $cells += (Get-ChangeMark $results)
    }
    $cells += (Format-Result $rowsByBuild['current'][('{0}|UtfUnknownOnly' -f $d.id)])
    $tableRows.Add($cells -join "`t")
}
[IO.File]::WriteAllText((Join-Path $OutDirectory 'table-net10.0-ja-JP.tsv'), (($tableRows -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

$commandTable = New-Object System.Collections.Generic.List[string]
$commandTable.Add((@('id', 'command') + $buildNames + 'changed') -join "`t")
$commandRows = @(foreach ($name in $buildNames) { , $commands[('{0}-net10.0' -f $name)] })
for ($i = 0; $i -lt $commandRows[0].Count; $i++) {
    $results = [string[]]@(foreach ($rows in $commandRows) { $rows[$i].result })
    $commandTable.Add((@($commandRows[0][$i].id, $commandRows[0][$i].command) + $results + (Get-ChangeMark $results)) -join "`t")
}
[IO.File]::WriteAllText((Join-Path $OutDirectory 'table-commands-net10.0.tsv'), (($commandTable -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

[IO.File]::WriteAllText((Join-Path $OutDirectory 'summary.txt'), (($script:summary -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('結果: {0}' -f $OutDirectory)
