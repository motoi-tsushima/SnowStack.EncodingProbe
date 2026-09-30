#Requires -Version 5.1
<#
.SYNOPSIS
    クロスチェックの信頼度を PowerShell 5.1（net48）と 7.x（net10.0）の両方で測定し、集計を表示する。

.DESCRIPTION
    Measure-CrossCheckConfidence.ps1 を両ホストで実行し、
    1. 両ホストの結果が一致すること（UTF.Unknown は net48 = 2.6.0、net10.0 = 2.7.0 で版が異なる）
    2. 信頼度の下限（0.5 / 0.55）の根拠となる値
    を表示する。結果の読み方は docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md を参照。

    UTF.Unknown の依存を更新したときは、先に dotnet build を実行してからこのスクリプトを実行し、
    表示された値が文書の値から動いていないかを確かめること。
    特に「表 B の最大」が 0.55 に、「表 A の最小」「上書き経路の最小」が 0.55 に近づいていないかを見る。

.PARAMETER Configuration
    ビルド構成。既定は Debug。

.PARAMETER OutDirectory
    TSV の出力先。既定は一時フォルダー。

.EXAMPLE
    pwsh -NoProfile -File tools/Invoke-CrossCheckMeasurement.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $OutDirectory
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($OutDirectory)) {
    $OutDirectory = Join-Path ([IO.Path]::GetTempPath()) ('CrossCheckMeasurement_' + [Guid]::NewGuid().ToString('N'))
}
if (-not (Test-Path -LiteralPath $OutDirectory)) { New-Item -ItemType Directory -Path $OutDirectory | Out-Null }

$measureScript = Join-Path $PSScriptRoot 'Measure-CrossCheckConfidence.ps1'
$hosts = @(
    [PSCustomObject]@{ Name = 'PowerShell 5.1 (net48 / UTF.Unknown 2.6.0)'; Executable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'; File = 'measure_net48.tsv' }
    [PSCustomObject]@{ Name = 'PowerShell 7.x (net10.0 / UTF.Unknown 2.7.0)'; Executable = 'pwsh'; File = 'measure_net10.tsv' }
)

foreach ($targetHost in $hosts) {
    $outFile = Join-Path $OutDirectory $targetHost.File
    Write-Host ('測定中: {0}' -f $targetHost.Name)
    & $targetHost.Executable -NoProfile -ExecutionPolicy Bypass -File $measureScript -Configuration $Configuration -OutFile $outFile | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outFile)) {
        throw ('{0} で測定に失敗しました。' -f $targetHost.Name)
    }
}

$net48 = [IO.File]::ReadAllLines((Join-Path $OutDirectory $hosts[0].File))
$net10 = [IO.File]::ReadAllLines((Join-Path $OutDirectory $hosts[1].File))
# 信頼度は float の文字列化の差で 5 桁目が食い違うことがある（例: 0.34780 と 0.34779）。
# 信頼度の列だけ 0.0001 未満の差を許し、それ以外の列は完全一致を求める。
$differences = New-Object System.Collections.Generic.List[string]
if ($net48.Count -ne $net10.Count) {
    $differences.Add(('行数が異なる: net48 = {0} / net10.0 = {1}' -f $net48.Count, $net10.Count))
}
else {
    $header = $net10[0].Split("`t")
    $confIndex = [Array]::IndexOf($header, 'uu_conf')
    for ($i = 1; $i -lt $net10.Count; $i++) {
        $a = $net48[$i].Split("`t"); $b = $net10[$i].Split("`t")
        for ($j = 0; $j -lt $header.Count; $j++) {
            if ($a[$j] -ceq $b[$j]) { continue }
            if ($j -eq $confIndex -and [Math]::Abs([double]$a[$j] - [double]$b[$j]) -lt 0.0001) { continue }
            $differences.Add(('{0} {1} {2} {3}: {4} = {5} / {6}' -f $a[0], $a[1], $a[2], $a[3], $header[$j], $a[$j], $b[$j]))
        }
    }
}
$identical = ($differences.Count -eq 0)

$script:summary = New-Object System.Collections.Generic.List[string]
function Out-Line([string] $Text = '') { $script:summary.Add($Text); Write-Host $Text }

$rows = Import-Csv -LiteralPath (Join-Path $OutDirectory $hosts[1].File) -Delimiter "`t" -Encoding UTF8

$eastAsianCultures = 'ja-JP', 'ko-KR', 'zh-CN', 'zh-TW', 'zh-HK'
$legacy = 932, 936, 949, 950, 20932, 51932, 51936, 51949, 51950, 54936
# シングルバイトではないコードページ（EncodingProbe.IsSingleByteCodePage の除外表と同じ）
$notSingleByte = @(20127, 1200, 1201, 12000, 12001, 65000, 65001) + $legacy + @(52936, 50220, 50221, 50222, 50225, 50227, 50229)

function Test-Legacy([string] $value) { $n = 0; [int]::TryParse($value, [ref]$n) -and ($legacy -contains $n) }
function Get-PathCultures($row) { @($eastAsianCultures | Where-Object { Test-Legacy $row."native_$_" }) }

Out-Line
Out-Line '## 両ホストの一致'
Out-Line
Out-Line ('{0} 件。{1}' -f ($net10.Count - 1), $(if ($identical) { 'net48 と net10.0 の結果は一致した（信頼度は小数第 4 位まで）。' } else { '**一致しない値がある。**' }))
foreach ($d in $differences) { Out-Line ('- ' + $d) }

$exceptions = @($rows | Where-Object { ($_.PSObject.Properties | Where-Object { $_.Name -like 'combined_*' -and $_.Value -like 'EXC:*' }).Count -gt 0 })
Out-Line ('例外が出た入力: {0} 件' -f $exceptions.Count)

#--------------------------------------------------------------------------
Out-Line
Out-Line '## 表 A: テストデータのシングルバイト系言語'
Out-Line
Out-Line '| テストデータ | UTF.Unknown | 信頼度 | 正答か | 上書き経路に入るカルチャー | Combined の結果（東アジア 5 カルチャー） |'
Out-Line '|---|---|---|---|---|---|'
$tableA = @($rows | Where-Object { $_.kind -eq 'A' })
foreach ($r in $tableA) {
    $path = Get-PathCultures $r
    $combined = @($eastAsianCultures | ForEach-Object { $r."combined_$_" } | Select-Object -Unique) -join ' / '
    $correct = switch ($r.uu_decode) { 'same' { '正答' } 'DIFF' { '誤答' } default { '—' } }
    Out-Line ('| {0} | {1} | {2} | {3} | {4} | {5} |' -f $r.label, $r.uu_name, $r.uu_conf, $correct, $(if ($path.Count) { $path -join ', ' } else { '入らない' }), $combined)
}
# 下限の根拠に使うのは、正答した通常の長さのファイルのうち、採用の下限（0.5）を超えるものだけ。
# 0.5 以下の答えは上書き経路に入るかどうかに関わらず捨てられる（判定不能になる）ので、0.55 の根拠にならない。
# 短文の限界を固定する *_short_* も除く
$tableACorrectAll = @($tableA | Where-Object { $_.uu_decode -eq 'same' -and $_.label -notlike '*_short_*' })
$tableACorrect = @($tableACorrectAll | Where-Object { [double]$_.uu_conf -gt 0.5 })
$tableABelowAdoption = @($tableACorrectAll | Where-Object { [double]$_.uu_conf -le 0.5 })
$minA = ($tableACorrect | ForEach-Object { [double]$_.uu_conf } | Measure-Object -Minimum).Minimum
$minAOnPath = ($tableACorrect | Where-Object { (Get-PathCultures $_).Count -gt 0 } | ForEach-Object { [double]$_.uu_conf } | Measure-Object -Minimum).Minimum

#--------------------------------------------------------------------------
Out-Line
Out-Line '## 表 B: 東アジア旧マルチバイトに対して UTF.Unknown がシングルバイトを返した信頼度'
Out-Line
Out-Line '| 入力 | UTF.Unknown | 件数 | 最大信頼度 |'
Out-Line '|---|---|---|---|'
$tableB = @($rows | Where-Object { $_.kind -eq 'B' -and $_.uu_name -ne '' })
$singleByteB = @($tableB | Where-Object { [int]$_.uu_cp -gt 0 -and $notSingleByte -notcontains [int]$_.uu_cp })
foreach ($g in $singleByteB | Group-Object label, uu_name) {
    $max = ($g.Group | ForEach-Object { [double]$_.uu_conf } | Measure-Object -Maximum).Maximum
    Out-Line ('| {0} | {1} | {2} | {3:F5} |' -f $g.Group[0].label, $g.Group[0].uu_name, $g.Count, $max)
}
foreach ($r in $tableB | Where-Object { [int]$_.uu_cp -le 0 }) {
    Out-Line ('| {0} {1} | {2}（.NET で解決できず対象外） | 1 | {3} |' -f $r.label, $r.variant, $r.uu_name, $r.uu_conf)
}
$maxB = ($singleByteB | ForEach-Object { [double]$_.uu_conf } | Measure-Object -Maximum).Maximum

#--------------------------------------------------------------------------
Out-Line
Out-Line '## テストデータに無いシングルバイト系言語（東アジア 5 カルチャーでの結果、ベトナム語を除く）'
Out-Line
Out-Line '| 長さ | 入力×カルチャー | 経路に入った | 正しく救済 | 誤って救済 | 救済されず | 経路外: 正答 | 経路外: 誤答 | 経路外: 判定不能 | 経路上の最低信頼度 |'
Out-Line '|---|---|---|---|---|---|---|---|---|---|'
$extra = @($rows | Where-Object { $_.kind -eq 'X' -and $_.label -ne 'Vietnamese' })
$minOnPathLong = $null
foreach ($class in @(
        [PSCustomObject]@{ Name = '1 行（L1〜L4）'; Filter = { $_.variant -like 'L*' } }
        [PSCustomObject]@{ Name = '全文・全文 ×3'; Filter = { $_.variant -eq 'full' -or $_.variant -eq 'x3' } })) {
    $subset = @($extra | Where-Object $class.Filter)
    $c = [ordered]@{ Cases = 0; Path = 0; Right = 0; Wrong = 0; Stay = 0; OutRight = 0; OutWrong = 0; OutNone = 0 }
    $confs = New-Object System.Collections.Generic.List[double]
    foreach ($r in $subset) {
        foreach ($culture in $eastAsianCultures) {
            $c.Cases++
            $combined = $r."combined_$culture"
            if (Test-Legacy $r."native_$culture") {
                $c.Path++
                $confs.Add([double]$r.uu_conf)
                if (Test-Legacy $combined) { $c.Stay++ }
                elseif ($r."decode_$culture" -eq 'same') { $c.Right++ }
                else { $c.Wrong++ }
            }
            elseif ($combined -eq '-1') { $c.OutNone++ }
            elseif ($r."decode_$culture" -eq 'same') { $c.OutRight++ }
            else { $c.OutWrong++ }
        }
    }
    $minConf = if ($confs.Count) { '{0:F5}' -f ($confs | Measure-Object -Minimum).Minimum } else { '—' }
    if ($class.Name -like '全文*' -and $confs.Count) { $minOnPathLong = ($confs | Measure-Object -Minimum).Minimum }
    Out-Line ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |' -f $class.Name, $c.Cases, $c.Path, $c.Right, $c.Wrong, $c.Stay, $c.OutRight, $c.OutWrong, $c.OutNone, $minConf)
}

Out-Line
Out-Line '### 救済されずに東アジアのまま残った入力'
Out-Line
foreach ($r in $extra) {
    foreach ($culture in $eastAsianCultures) {
        if ((Test-Legacy $r."native_$culture") -and (Test-Legacy $r."combined_$culture")) {
            Out-Line ('- {0} cp{1} {2}（{3} バイト）{4}: {5} のまま。UTF.Unknown {6} {7}' -f $r.label, $r.source_cp, $r.variant, $r.bytes, $culture, $r."combined_$culture", $r.uu_name, $r.uu_conf)
        }
    }
}

#--------------------------------------------------------------------------
Out-Line
Out-Line '## 下限の余裕'
Out-Line
Out-Line ('- 表 B の最大（シングルバイト）: {0:F5}  … 上書きの下限 0.55 はこれを上回る必要がある' -f $maxB)
Out-Line ('- 表 A の最小（正答・通常の長さ・採用の下限 0.5 超）: {0:F5}  … 上書きの下限 0.55 はこれを下回る必要がある' -f $minA)
foreach ($r in $tableABelowAdoption) {
    Out-Line ('- 参考: {0} は正答だが信頼度 {1} で採用の下限 0.5 以下（経路に関わらず判定不能）' -f $r.label, $r.uu_conf)
}
if ($null -ne $minAOnPath) { Out-Line ('- 表 A のうち上書き経路に入るものの最小: {0:F5}' -f $minAOnPath) }
if ($null -ne $minOnPathLong) { Out-Line ('- 追加言語（全文・全文 ×3）で上書き経路に入ったものの最小: {0:F5}' -f $minOnPathLong) }
Out-Line
Out-Line ('TSV: {0}' -f $OutDirectory)

$summaryPath = Join-Path $OutDirectory 'summary.md'
[IO.File]::WriteAllLines($summaryPath, $script:summary, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('集計: {0}' -f $summaryPath)
if (-not $identical) { exit 1 }
