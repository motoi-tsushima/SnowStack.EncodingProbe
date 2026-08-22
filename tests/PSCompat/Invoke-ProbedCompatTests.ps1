<#
.SYNOPSIS
    同じシナリオを PowerShell 5.1 と 7.x の両方で実行し、結果が一致することを検証する。

.DESCRIPTION
    本モジュールの存在意義は「PowerShell のバージョンによらず同一の結果を得られること」に
    あるため、片方のホストだけの検証では意味がない。

    xUnit のテストプロジェクト (EncodingProbe.PowerShell.Tests) は
    Microsoft.PowerShell.SDK を参照する都合上 net10.0 単独ターゲットであり、
    PowerShell 5.1 上では実行できない。そのため、バージョン間の一致検証は
    このスクリプトが担う。

    PowerShell 5.1 では net48 ビルド、PowerShell 7.x では net10.0 ビルドを読み込ませ、
    シナリオの出力を1行ずつ突き合わせる。差分があれば内容を表示して終了コード 1 を返す。

.PARAMETER Configuration
    使用するビルド構成。既定は Debug。

.EXAMPLE
    pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scenarioScript = Join-Path $PSScriptRoot 'ProbedCompatScenarios.ps1'
$binRoot = Join-Path $repositoryRoot "SnowStack.EncodingProbe.PowerShell\bin\$Configuration"

$hosts = @(
    [PSCustomObject]@{
        Name       = 'PowerShell 5.1 (Desktop / net48)'
        Executable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        Dll        = Join-Path $binRoot 'net48\SnowStack.EncodingProbe.PowerShell.dll'
    },
    [PSCustomObject]@{
        Name       = 'PowerShell 7.x (Core / net10.0)'
        Executable = 'pwsh'
        Dll        = Join-Path $binRoot 'net10.0\SnowStack.EncodingProbe.PowerShell.dll'
    }
)

$reports = @{}

foreach ($targetHost in $hosts) {
    if (-not (Test-Path -LiteralPath $targetHost.Dll)) {
        Write-Error ("ビルド結果が見つかりません: {0}`n先に dotnet build を実行してください。" -f $targetHost.Dll)
        exit 2
    }

    $reportPath = Join-Path ([IO.Path]::GetTempPath()) ("ProbedCompat_" + [Guid]::NewGuid().ToString('N') + '.txt')

    Write-Host ("実行中: {0}" -f $targetHost.Name)
    & $targetHost.Executable -NoProfile -ExecutionPolicy Bypass -File $scenarioScript -Dll $targetHost.Dll -OutFile $reportPath

    if ($LASTEXITCODE -ne 0) {
        Write-Error ("{0} でシナリオの実行に失敗しました (終了コード {1})" -f $targetHost.Name, $LASTEXITCODE)
        exit 2
    }

    $reports[$targetHost.Name] = [IO.File]::ReadAllLines($reportPath)
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
}

$desktop = $reports[$hosts[0].Name]
$core = $reports[$hosts[1].Name]

$differences = New-Object System.Collections.Generic.List[string]

if ($desktop.Count -ne $core.Count) {
    $differences.Add(("シナリオ数が異なります: {0}={1} / {2}={3}" -f
        $hosts[0].Name, $desktop.Count, $hosts[1].Name, $core.Count))
}

for ($i = 0; $i -lt [Math]::Min($desktop.Count, $core.Count); $i++) {
    if ($desktop[$i] -cne $core[$i]) {
        $differences.Add("")
        $differences.Add(("  5.1 : {0}" -f $desktop[$i]))
        $differences.Add(("  7.x : {0}" -f $core[$i]))
    }
}

Write-Host ""

if ($differences.Count -gt 0) {
    Write-Host ("不一致が {0} 件ありました。" -f ($differences | Where-Object { $_ -like '  5.1 *' }).Count) -ForegroundColor Red
    $differences | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

Write-Host ("PowerShell 5.1 と 7.x の結果は完全に一致しました ({0} シナリオ)。" -f $desktop.Count) -ForegroundColor Green
exit 0