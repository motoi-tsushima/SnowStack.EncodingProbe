# 実行中の PowerShell エディションに応じて正しいバイナリを読み込む
if ($PSEdition -eq 'Core') {
    Import-Module (Join-Path $PSScriptRoot 'core\SnowStack.EncodingProbe.PowerShell.dll')
} else {
    Import-Module (Join-Path $PSScriptRoot 'desktop\SnowStack.EncodingProbe.PowerShell.dll')
}
