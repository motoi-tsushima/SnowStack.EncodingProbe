#requires -Version 5.1
<#
.SYNOPSIS
    上書きの 2 つの書き方（置き換え・メモリに写してから直接書く）の時間を、ファイルの大きさごとに測る（1.3.0 第五次の仕上げ。実装記録 10.16 節）。

.DESCRIPTION
    モジュールの内部の境目（SafeFileOverwrite.DirectWriteThreshold）をリフレクションで切り替え、
    同じ大きさの既存のファイルに Set-ProbedContent で同じ大きさの内容を書く時間を測る。
      Replace … 境目を 0 にする（常に今までの方法。一時ファイルに書いて File.Replace）
      Memory  … 境目を最大の大きさより大きくする（常にメモリに写してから直接書く）
    結果は 1 行 1 件の CSV で標準出力に出す。

.PARAMETER ModulePath
    読み込むモジュールの DLL（PS 7 は net10.0、PS 5.1 は net48 の出力）

.EXAMPLE
    pwsh -NoProfile -File tools/Measure-OverwriteThreshold.ps1 -ModulePath SnowStack.EncodingProbe.PowerShell/bin/Release/net10.0/SnowStack.EncodingProbe.PowerShell.dll
#>
param(
    [Parameter(Mandatory)]
    [string] $ModulePath,

    [int[]] $SizesKiB = @(1, 64, 256, 1024, 4096, 16384),

    [int] $Rounds = 5
)

$ErrorActionPreference = 'Stop'
Import-Module $ModulePath

$type = [SnowStack.EncodingProbe.PowerShell.Cmdlets.ResolveEncodingCmdlet].Assembly.GetType('SnowStack.EncodingProbe.PowerShell.Internal.SafeFileOverwrite')
$flags = [Reflection.BindingFlags]'NonPublic, Public, Static'
$threshold = $type.GetProperty('DirectWriteThreshold', $flags)
$budget = $type.GetProperty('MemoryBudget', $flags)
$budget.SetValue($null, [long]1GB)

$work = Join-Path ([IO.Path]::GetTempPath()) ('ProbedThreshold_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    'Host,SizeKiB,Method,Writes,MedianMs,MinMs'

    foreach ($sizeKiB in $SizesKiB) {
        $size = $sizeKiB * 1024
        # 1 回の測定で書く回数（小さいものは多く書いて、1 回あたりの時間にならす）
        $writes = [Math]::Max(3, [int](200 / [Math]::Sqrt($sizeKiB)))
        $value = 'x' * $size
        $files = 1..$writes | ForEach-Object { Join-Path $work ("f$sizeKiB-$_.txt") }

        foreach ($method in 'Replace', 'Memory') {
            $threshold.SetValue($null, $(if ($method -eq 'Replace') { [long]0 } else { [long]1GB }))
            $samples = foreach ($round in 1..($Rounds + 1)) {
                foreach ($file in $files) { [IO.File]::WriteAllText($file, $value) }
                $elapsed = (Measure-Command {
                    foreach ($file in $files) {
                        Set-ProbedContent -LiteralPath $file -Value $value -Encoding utf8NoBOM -NoNewline
                    }
                }).TotalMilliseconds / $writes

                # 1 回目は JIT などの準備を含むので捨てる
                if ($round -gt 1) { $elapsed }
            }

            $sorted = @($samples | Sort-Object)
            '{0},{1},{2},{3},{4:F3},{5:F3}' -f $PSVersionTable.PSEdition, $sizeKiB, $method, $writes, $sorted[[int][Math]::Floor($sorted.Count / 2)], $sorted[0]
        }

        Remove-Item -Path $files -Force
    }
}
finally {
    $threshold.SetValue($null, $type.GetField('DefaultDirectWriteThreshold', $flags).GetValue($null))
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
