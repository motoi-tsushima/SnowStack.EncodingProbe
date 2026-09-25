#Requires -Version 5.1
<#
.SYNOPSIS
    Measure-OutFileBehavior.ps1 を PowerShell 5.1 と 7.x の両方で実行し、ホストごとの JSON を書き出す。

.DESCRIPTION
    docs/EncodingProbe-1.2.0-実測依頼-Out-File挙動.md の測定を両ホストで行う。
    結果の読み方は docs/EncodingProbe-1.2.0-調査-Out-File挙動の実測.md を参照。

    Set-ProbedContent / Add-ProbedContent の参照測定にビルド済みの DLL を使うため、
    先に dotnet build を実行しておくこと。

    子プロセスの標準出力はリダイレクトされる（非対話のホストになる）。
    対話コンソールでの既定の幅は、Measure-OutFileBehavior.ps1 -InteractiveWidthOnly を手動で実行して測る。

.PARAMETER Configuration
    ビルド構成。既定は Debug。

.PARAMETER OutDirectory
    JSON の出力先。既定は tools/OutFileBehaviorResults。

.EXAMPLE
    pwsh -NoProfile -File tools/Invoke-OutFileMeasurement.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $OutDirectory
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($OutDirectory)) {
    $OutDirectory = Join-Path $PSScriptRoot 'OutFileBehaviorResults'
}
if (-not (Test-Path -LiteralPath $OutDirectory)) { New-Item -ItemType Directory -Path $OutDirectory | Out-Null }

$measureScript = Join-Path $PSScriptRoot 'Measure-OutFileBehavior.ps1'
$hosts = @(
    [PSCustomObject]@{ Name = 'PowerShell 5.1'; Executable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'; File = 'ps51.json' }
    [PSCustomObject]@{ Name = 'PowerShell 7.x'; Executable = 'pwsh'; File = 'ps7.json' }
)

foreach ($targetHost in $hosts) {
    $outFile = Join-Path $OutDirectory $targetHost.File
    if (Test-Path -LiteralPath $outFile) { Remove-Item -LiteralPath $outFile }
    Write-Host ('測定中: {0}' -f $targetHost.Name)
    & $targetHost.Executable -NoProfile -ExecutionPolicy Bypass -File $measureScript -Configuration $Configuration -OutFile $outFile | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outFile)) {
        throw ('{0} で測定に失敗しました。' -f $targetHost.Name)
    }
    Write-Host ('  → {0}' -f $outFile)
}
