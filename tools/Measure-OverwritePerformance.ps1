#requires -Version 5.1
<#
.SYNOPSIS
    Set-ProbedContent で多くのファイルに上書きする時間を測る（1.3.0 第五次の性能の確認。実装記録 10.15 節・10.16 節）。

.DESCRIPTION
    既存のファイルを作り直してから、次の 3 つの書き方で上書きする時間を測り、中央値を CSV で標準出力に出す。
      LiteralPath … -LiteralPath にすべてのファイルを渡す
      Pipeline    … Get-ChildItem の出力をパイプラインで渡す
      Inherit     … -LiteralPath に渡し、-Encoding を省略する（書き込み先から継承する。既存のファイルを判定する）
    -Large を付けると、小さなファイルの代わりに大きなファイル（既定は 16 MiB を 10 個）で、書く内容も同じ大きさにして測る。
    比べたいビルドの DLL を -ModulePath に渡して、ビルドごとに実行する。

.EXAMPLE
    pwsh -NoProfile -File tools/Measure-OverwritePerformance.ps1 -ModulePath <net10.0 の DLL の絶対パス> -Label 修正後
#>
param(
    [Parameter(Mandatory)]
    [string] $ModulePath,

    [string] $Label = '',

    [int] $Count = 1000,

    [int] $Rounds = 5,

    [switch] $Large,

    [int] $LargeCount = 10,

    [int] $LargeSizeMiB = 16
)

$ErrorActionPreference = 'Stop'
Import-Module $ModulePath

$work = Join-Path ([IO.Path]::GetTempPath()) ('ProbedPerf_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null

if ($Large) {
    $files = 1..$LargeCount | ForEach-Object { Join-Path $work ('f{0:D4}.txt' -f $_) }
    $original = [Text.Encoding]::ASCII.GetBytes('o' * ($LargeSizeMiB * 1MB))
    $value = 'n' * ($LargeSizeMiB * 1MB)
    $kind = '{0}x{1}MiB' -f $LargeCount, $LargeSizeMiB
}
else {
    $files = 1..$Count | ForEach-Object { Join-Path $work ('f{0:D4}.txt' -f $_) }
    $original = [Text.Encoding]::ASCII.GetBytes("old`r`n")
    $value = 'new'
    $kind = '{0}x{1}B' -f $Count, $original.Length
}

$ways = [ordered]@{
    'LiteralPath' = { Set-ProbedContent -LiteralPath $files -Value $value -Encoding utf8NoBOM -NoNewline:$Large }
    'Pipeline' = { Get-ChildItem -LiteralPath $work -File | Set-ProbedContent -Value $value -Encoding utf8NoBOM -NoNewline:$Large }
    'Inherit' = { Set-ProbedContent -LiteralPath $files -Value $value -NoNewline:$Large }
}

try {
    'Label,Host,Files,Way,MedianMs,MinMs,MaxMs'

    foreach ($way in $ways.Keys) {
        $samples = foreach ($round in 1..($Rounds + 1)) {
            foreach ($file in $files) { [IO.File]::WriteAllBytes($file, $original) }
            [GC]::Collect()
            $elapsed = (Measure-Command $ways[$way]).TotalMilliseconds

            # 1 回目は JIT などの準備を含むので捨てる
            if ($round -gt 1) { $elapsed }
        }

        $sorted = @($samples | Sort-Object)
        '{0},{1},{2},{3},{4:F0},{5:F0},{6:F0}' -f $Label, $PSVersionTable.PSEdition, $kind, $way, $sorted[[int][Math]::Floor($sorted.Count / 2)], $sorted[0], $sorted[-1]
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
