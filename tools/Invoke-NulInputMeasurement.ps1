#Requires -Version 5.1
<#
.SYNOPSIS
    0x00 を含む入力の判定を、現状と修正案の 2 つのビルド × PowerShell 5.1（net48）/ 7.x（net10.0）で測定し、比べる。

.DESCRIPTION
    Measure-NulInputDetection.ps1 を 4 通り（ビルド 2 × ホスト 2）の別プロセスで実行し、
    結果の TSV を OutDirectory に集め、比較の集計を summary.txt に書き出す。
    同名のアセンブリ（SnowStack.EncodingProbe.dll）を 1 つのプロセスに 2 つ読み込めないため、プロセスを分けている。

    集計する内容（docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md の要約 1〜5 に対応する）:
      1. C 群で、現状と修正案の結果が変わった行
      2. A 群で、修正案が UTF-16 / UTF-32 と答えなかった入力
      3. 0x00 を含み、修正案が UTF-16 / UTF-32 と答えなかった入力の結果（20127 のままか）
      4. B 群で、修正案が UTF-16 / UTF-32 と答えた入力
      5. 判定方式・カルチャー・TFM による結果の違い
    加えて、すべての入力の現状と修正案の対照表（ja-JP / NativeOnly）とコマンドの対照表を書き出す。

    両方のビルドは事前に dotnet build -c <Configuration> しておくこと。

.PARAMETER CurrentRoot
    現状のビルドのリポジトリのルート。既定はこのスクリプトのリポジトリ。

.PARAMETER ProposalRoot
    修正案のビルドのリポジトリ（worktree）のルート。

.PARAMETER OutDirectory
    結果の出力先。既定は tools/NulInputDetectionResults。

.EXAMPLE
    pwsh -NoProfile -File tools/Invoke-NulInputMeasurement.ps1 -ProposalRoot ..\EP-nul
#>
[CmdletBinding()]
param(
    [string] $CurrentRoot,
    [Parameter(Mandatory = $true)] [string] $ProposalRoot,
    [string] $Configuration = 'Debug',
    [string] $OutDirectory,
    # 測定をせず、OutDirectory にある TSV から集計だけをやり直す
    [switch] $SkipMeasurement
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($CurrentRoot)) { $CurrentRoot = $repositoryRoot }
if ([string]::IsNullOrEmpty($OutDirectory)) { $OutDirectory = Join-Path $PSScriptRoot 'NulInputDetectionResults' }
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
function Read-Tsv([string] $Name) {
    return @(Import-Csv -LiteralPath (Join-Path $OutDirectory $Name) -Delimiter "`t" -Encoding UTF8)
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
$diag = Read-Tsv 'diag-proposal-net10.0.tsv'
$diagById = @{}
foreach ($d in $diag) { $diagById[$d.id] = $d }

function Format-Result($Row) { return ('{0} / {1} / {2} / {3}' -f $Row.CodePage, $Row.EncodingWebName, $Row.Bom, $Row.LineBreak) }
function Get-Key($Row) { return ('{0}|{1}|{2}' -f $Row.id, $Row.culture, $Row.strategy) }
function Test-Unicode($Row) { return @('1200', '1201', '12000', '12001') -contains $Row.CodePage }

$script:summary = New-Object System.Collections.Generic.List[string]
function Out-Line([string] $Text = '') { $script:summary.Add($Text); Write-Host $Text }

# 行の照合: 4 通りの TSV は同じ行数で、キー（入力 × カルチャー × 判定方式）の並びが一致すること
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

foreach ($tfm in 'net10.0', 'net48') {
    $cur = $detect[('current-{0}' -f $tfm)]
    $pro = $detect[('proposal-{0}' -f $tfm)]

    Out-Line ('# {0}' -f $tfm)
    Out-Line ''

    # 変化した行
    $changed = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $cur.Count; $i++) {
        if ((Format-Result $cur[$i]) -cne (Format-Result $pro[$i])) { $changed.Add([PSCustomObject]@{ Cur = $cur[$i]; Pro = $pro[$i] }) }
    }
    Out-Line ('## 現状と修正案で結果が変わった行: {0} 行（入力 {1} 件）' -f $changed.Count, @($changed | ForEach-Object { $_.Cur.id } | Select-Object -Unique).Count)
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

    # 2. A 群で修正案が UTF-16 / UTF-32 でない
    Out-Line '## 2. A 群で修正案が UTF-16 / UTF-32 と答えなかった行'
    # UtfUnknownOnly は独自判定を通らない（修正案の対象外）ので、件数だけ示す
    $aMissUu = @($pro | Where-Object { $_.group -like 'A*' -and $_.strategy -eq 'UtfUnknownOnly' -and -not (Test-Unicode $_) })
    $aTotalUu = @($pro | Where-Object { $_.group -like 'A*' -and $_.strategy -eq 'UtfUnknownOnly' })
    Out-Line ('  （UtfUnknownOnly: {0} 行中 {1} 行が UTF-16 / UTF-32 以外。内訳: {2}）' -f $aTotalUu.Count, $aMissUu.Count, ((($aMissUu | Group-Object { $_.CodePage + ' / ' + $_.EncodingWebName }) | ForEach-Object { '{0} = {1} 行' -f $_.Name, $_.Count }) -join ', '))
    $aMiss = @($pro | Where-Object { $_.group -like 'A*' -and $_.strategy -ne 'UtfUnknownOnly' -and -not (Test-Unicode $_) })
    foreach ($r in $aMiss) {
        $d = $diagById[$r.id]
        Out-Line ('  {0}: {1}  [len={2} mod4={3} utf32={4} utf16={5} null16={6}/{7} null32={8}/{9}]' -f (Get-Key $r), (Format-Result $r), $d.length, $d.mod4, $d.utf32, $d.utf16, $d.null16le, $d.null16be, $d.null32le, $d.null32be)
    }
    Out-Line ('  計 {0} 行（Combined / NativeOnly）' -f $aMiss.Count)
    # A 群で修正案のエンディアン・幅が入力と違うもの（id の末尾の形と照合）
    $expected = @{ u16le = '1200'; u16be = '1201'; u32le = '12000'; u32be = '12001' }
    $aWrong = @($pro | Where-Object { $_.group -like 'A*' -and (Test-Unicode $_) -and $expected[$_.id.Substring($_.id.LastIndexOf('-') + 1)] -ne $_.CodePage })
    Out-Line ('  A 群で UTF-16 / UTF-32 だが入力の形（幅・エンディアン）と違う行: {0} 行' -f $aWrong.Count)
    foreach ($r in $aWrong) { Out-Line ('    {0}: {1}' -f (Get-Key $r), (Format-Result $r)) }
    Out-Line ''

    # 3. 0x00 を含み UTF-16 / UTF-32 でない入力
    Out-Line '## 3. 0x00 を含み、修正案（NativeOnly）が UTF-16 / UTF-32 と答えなかった入力'
    $zeroIds = @{}
    foreach ($d in $diag) { if ([int]$d.zeros -gt 0) { $zeroIds[$d.id] = $d } }
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
        Out-Line ('  {0}: {1}  [len={2} mod2={3} mod4={4} zeros={5} utf32={6} utf16={7} null16={8}/{9} null32={10}/{11}] {12}' -f $r.id, (Format-Result $r), $d.length, $d.mod2, $d.mod4, $d.zeros, $d.utf32, $d.utf16, $d.null16le, $d.null16be, $d.null32le, $d.null32be, $d.label)
    }
    Out-Line ('  計 {0} 件' -f $bHit.Count)
    Out-Line ''

    # 5. 判定方式・カルチャーによる違い（修正案）
    Out-Line '## 5. 判定方式・カルチャーによる違い（入力ごとに 6 条件の結果が一致しないもの）'
    foreach ($buildName in 'current', 'proposal') {
        $rows = $detect[('{0}-{1}' -f $buildName, $tfm)]
        $diffCount = 0
        foreach ($g in ($rows | Group-Object id)) {
            # UtfUnknownOnly は同じ 20127 でも EncodingWebName が ascii になるので、CodePage と Bom だけで比べる
            $distinct = @($g.Group | ForEach-Object { '{0}/{1}' -f $_.CodePage, $_.Bom } | Select-Object -Unique)
            if ($distinct.Count -gt 1) {
                $diffCount++
                # C1 の東アジア以外などは UTF.Unknown の有無で結果が違うのが当然なので、0x00 を含む入力だけ詳しく出す
                if ($zeroIds.ContainsKey($g.Name)) {
                    $cells = $g.Group | ForEach-Object { '{0}/{1}={2}' -f $_.culture, $_.strategy, $_.CodePage }
                    Out-Line ('  {0} {1}: {2}' -f $buildName, $g.Name, ($cells -join ', '))
                }
            }
        }
        Out-Line ('  {0}: 6 条件で結果が一致しない入力 {1} 件（0x00 を含む入力だけ上に列挙）' -f $buildName, $diffCount)
    }
    # Combined / NativeOnly で変化の有無が違うもの
    $byKey = @{}
    foreach ($c in $changed) { $byKey[(Get-Key $c.Cur)] = $true }
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
}

#--------------------------------------------------------------------------
# 対照表（net10.0、ja-JP / NativeOnly と Combined）
#--------------------------------------------------------------------------
$tableRows = New-Object System.Collections.Generic.List[string]
$tableRows.Add("group`tid`tlabel`tlength`tzeros`thead`tutf32`tutf16`tcurrent_native`tproposal_native`tchanged_native`tcurrent_combined`tproposal_combined`tchanged_combined`tcurrent_utfunknown")
$cur = $detect['current-net10.0']; $pro = $detect['proposal-net10.0']
foreach ($d in $diag) {
    $pick = {
        param($rows, $strategy)
        $rows | Where-Object { $_.id -eq $d.id -and $_.culture -eq 'ja-JP' -and $_.strategy -eq $strategy } | Select-Object -First 1
    }
    $cn = Format-Result (& $pick $cur 'NativeOnly'); $pn = Format-Result (& $pick $pro 'NativeOnly')
    $cc = Format-Result (& $pick $cur 'Combined'); $pc = Format-Result (& $pick $pro 'Combined')
    $cu = Format-Result (& $pick $cur 'UtfUnknownOnly')
    $tableRows.Add(("{0}`t{1}`t{2}`t{3}`t{4}`t{5}`t{6}`t{7}`t{8}`t{9}`t{10}`t{11}`t{12}`t{13}`t{14}" -f
        $d.group, $d.id, $d.label, $d.length, $d.zeros, $d.head, $d.utf32, $d.utf16, $cn, $pn, ($(if ($cn -ceq $pn) { '' } else { '変化' })), $cc, $pc, ($(if ($cc -ceq $pc) { '' } else { '変化' })), $cu))
}
[IO.File]::WriteAllText((Join-Path $OutDirectory 'table-net10.0-ja-JP.tsv'), (($tableRows -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

$commandTable = New-Object System.Collections.Generic.List[string]
$commandTable.Add("id`tcommand`tcurrent`tproposal`tchanged")
$cc = $commands['current-net10.0']; $pc = $commands['proposal-net10.0']
for ($i = 0; $i -lt $cc.Count; $i++) {
    $commandTable.Add(("{0}`t{1}`t{2}`t{3}`t{4}" -f $cc[$i].id, $cc[$i].command, $cc[$i].result, $pc[$i].result, ($(if ($cc[$i].result -ceq $pc[$i].result) { '' } else { '変化' }))))
}
[IO.File]::WriteAllText((Join-Path $OutDirectory 'table-commands-net10.0.tsv'), (($commandTable -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

[IO.File]::WriteAllText((Join-Path $OutDirectory 'summary.txt'), (($script:summary -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('結果: {0}' -f $OutDirectory)
