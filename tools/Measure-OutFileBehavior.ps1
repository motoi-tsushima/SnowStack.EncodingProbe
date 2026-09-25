#Requires -Version 5.1
<#
.SYNOPSIS
    標準の Out-File の挙動と、Set-ProbedContent / Add-ProbedContent の改行処理を実測し、JSON に書き出す。

.DESCRIPTION
    docs/EncodingProbe-1.2.0-実測依頼-Out-File挙動.md の依頼に基づく測定スクリプト。
    Out-ProbedFile（1.2.0 で追加予定）の仕様を決めるための事実を集める。製品コードには触れない。

    1 回の実行で 1 ホスト分の結果を 1 つの JSON に書き出す。
    PS 5.1 と PS 7.x の両方で実行し、結果を突き合わせる（Invoke-OutFileMeasurement.ps1 が両方を実行する）。

    ホストによって結果が変わらないよう、次を守っている。
    - 並べ替えは序数比較（[StringComparer]::Ordinal）
    - 数値の文字列化はインバリアントカルチャー
    - バイト列の 16 進表示は Format-Hex を使わず自前で整形する
    - 入力データに実行ごとに変わる値を含めない

    一時ファイルは [IO.Path]::GetTempPath() の下の測定専用フォルダーだけを使い、終了時に削除する。

.PARAMETER OutFile
    結果の JSON の出力先。

.PARAMETER Configuration
    Set-ProbedContent / Add-ProbedContent の参照測定に使うビルド構成。既定は Debug。

.PARAMETER InteractiveWidthOnly
    対話コンソールで -Width 省略時の幅だけを測る（依頼書の付録 A）。
    OutFile を省略すると tools/OutFileBehaviorResults/ に、ホストとウィンドウ幅を名前に含むファイルを作る。

.EXAMPLE
    pwsh -NoProfile -File tools/Measure-OutFileBehavior.ps1 -OutFile out.json

.EXAMPLE
    # Windows Terminal 上の対話コンソールで
    .\tools\Measure-OutFileBehavior.ps1 -InteractiveWidthOnly
#>
[CmdletBinding()]
param(
    [string] $OutFile,
    [string] $Configuration = 'Debug',
    [switch] $InteractiveWidthOnly
)

$ErrorActionPreference = 'Stop'

$inv = [cultureinfo]::InvariantCulture
$isCore = ($PSVersionTable.PSEdition -eq 'Core')
$repoRoot = Split-Path -Parent $PSScriptRoot

#region 共通の補助関数

function ConvertTo-HexString([byte[]] $Bytes) {
    if ($null -eq $Bytes) { return $null }
    $parts = New-Object 'System.Collections.Generic.List[string]'
    foreach ($b in $Bytes) { $parts.Add($b.ToString('X2', $inv)) }
    return [string]::Join(' ', $parts.ToArray())
}

# 改行・制御文字を見える形にする（JSON を目で読むため）
function ConvertTo-Visible([string] $Text) {
    if ($null -eq $Text) { return $null }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        switch ([int]$ch) {
            13 { [void]$sb.Append('\r') }
            10 { [void]$sb.Append('\n') }
            27 { [void]$sb.Append('\e') }
            9  { [void]$sb.Append('\t') }
            default { [void]$sb.Append($ch) }
        }
    }
    return $sb.ToString()
}

function Get-FileState([string] $Path) {
    if ([IO.Directory]::Exists($Path)) {
        return [ordered]@{ Exists = $true; IsDirectory = $true }
    }
    if (-not [IO.File]::Exists($Path)) {
        return [ordered]@{ Exists = $false }
    }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $attributes = [IO.File]::GetAttributes($Path)
    return [ordered]@{
        Exists   = $true
        Length   = $bytes.Length
        Hex      = ConvertTo-HexString $bytes
        Text     = ConvertTo-Visible (Get-DecodedText $bytes)
        ReadOnly = (($attributes -band [IO.FileAttributes]::ReadOnly) -ne 0)
    }
}

# BOM を見て復号する（BOM が無ければ UTF-8 とみなす）。表示用であり判定には使わない
function Get-DecodedText([byte[]] $Bytes) {
    if ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) {
        return [Text.Encoding]::Unicode.GetString($Bytes, 2, $Bytes.Length - 2)
    }
    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) {
        return [Text.Encoding]::UTF8.GetString($Bytes, 3, $Bytes.Length - 3)
    }
    return [Text.Encoding]::UTF8.GetString($Bytes)
}

function Get-ErrorInfo($Record) {
    if ($null -eq $Record) { return $null }
    $exception = $Record.Exception
    $inner = $null
    if ($null -ne $exception -and $null -ne $exception.InnerException) {
        $inner = $exception.InnerException.GetType().FullName
    }
    return [ordered]@{
        FullyQualifiedErrorId = $Record.FullyQualifiedErrorId
        ExceptionType         = if ($null -ne $exception) { $exception.GetType().FullName } else { $null }
        InnerExceptionType    = $inner
        Category              = $Record.CategoryInfo.Category.ToString()
        Message               = if ($null -ne $exception) { ConvertTo-Visible $exception.Message } else { $null }
    }
}

# 依頼書 2.4 の方法で、終了エラーか非終了エラーかを判定する。
# 測定対象のコマンドには -ErrorAction Continue -ErrorVariable ev を付けること。
# アクションはこの関数のスコープでドットソースされるため、$ev はここに設定される。
function Invoke-Measured([scriptblock] $Action) {
    $ErrorActionPreference = 'Continue'
    $ev = $null
    $caught = $null
    $output = $null
    try {
        $output = . $Action 2>$null
    }
    catch {
        $caught = $_
    }
    $nonTerminating = New-Object 'System.Collections.Generic.List[object]'
    $exceptionsInEv = New-Object 'System.Collections.Generic.List[string]'
    if ($null -ne $ev) {
        foreach ($e in $ev) {
            # 終了エラーは $ev に ErrorRecord ではなく例外（CmdletInvocationException など）として入る
            if ($e -is [Exception]) { $exceptionsInEv.Add($e.GetType().FullName); continue }
            if ($null -ne $caught -and [object]::ReferenceEquals($e, $caught)) { continue }
            if ($null -ne $caught -and $e.FullyQualifiedErrorId -eq $caught.FullyQualifiedErrorId) { continue }
            $nonTerminating.Add((Get-ErrorInfo $e))
        }
    }
    $kind = 'none'
    if ($null -ne $caught) { $kind = 'terminating' }
    elseif ($nonTerminating.Count -gt 0) { $kind = 'non-terminating' }
    return [ordered]@{
        ErrorKind            = $kind
        Terminating          = Get-ErrorInfo $caught
        NonTerminating       = $nonTerminating.ToArray()
        ExceptionsInErrorVariable = $exceptionsInEv.ToArray()
        Output               = $output
    }
}

$script:sandboxRoot = Join-Path ([IO.Path]::GetTempPath()) ('OutFileBehavior_' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($script:sandboxRoot)
$script:caseCounter = 0

# 測定ごとに新しいフォルダーを作る
function New-CaseDirectory {
    $script:caseCounter++
    $dir = Join-Path $script:sandboxRoot ('c' + $script:caseCounter.ToString('000', $inv))
    [void][IO.Directory]::CreateDirectory($dir)
    return $dir
}

function Remove-Sandbox {
    if (-not [IO.Directory]::Exists($script:sandboxRoot)) { return }
    foreach ($f in [IO.Directory]::GetFiles($script:sandboxRoot, '*', [IO.SearchOption]::AllDirectories)) {
        [IO.File]::SetAttributes($f, [IO.FileAttributes]::Normal)
    }
    [IO.Directory]::Delete($script:sandboxRoot, $true)
}

$utf16Bom = New-Object System.Text.UnicodeEncoding($false, $true)

# 既存ファイルの初期内容: BOM 付き UTF-16LE の "OLD" + CRLF
function New-OldFile([string] $Path) {
    [IO.File]::WriteAllText($Path, "OLD`r`n", $utf16Bom)
}

function Set-ReadOnly([string] $Path) {
    [IO.File]::SetAttributes($Path, [IO.FileAttributes]::ReadOnly)
}

function Get-DirectoryListing([string] $Dir) {
    $sorted = [string[]]@([IO.Directory]::GetFileSystemEntries($Dir) | ForEach-Object { [IO.Path]::GetFileName($_) })
    [Array]::Sort([string[]]$sorted, [StringComparer]::Ordinal)
    $result = [ordered]@{}
    foreach ($n in $sorted) { $result[$n] = Get-FileState (Join-Path $Dir $n) }
    return $result
}

function Get-EnvironmentInfo {
    $bufferSize = $null; $windowSize = $null
    try { $b = $Host.UI.RawUI.BufferSize; $bufferSize = '{0}x{1}' -f $b.Width, $b.Height } catch { $bufferSize = 'exception: ' + $_.Exception.GetType().FullName }
    try { $w = $Host.UI.RawUI.WindowSize; $windowSize = '{0}x{1}' -f $w.Width, $w.Height } catch { $windowSize = 'exception: ' + $_.Exception.GetType().FullName }
    $os = if ($PSVersionTable.ContainsKey('OS')) { $PSVersionTable.OS } else { [Environment]::OSVersion.VersionString }
    $rendering = $null
    if ($isCore -and (Get-Variable -Name PSStyle -ErrorAction SilentlyContinue)) { $rendering = $PSStyle.OutputRendering.ToString() }
    return [ordered]@{
        PSVersion              = $PSVersionTable.PSVersion.ToString()
        PSEdition              = $PSVersionTable.PSEdition
        OS                     = $os
        HostName               = $Host.Name
        IsOutputRedirected     = [Console]::IsOutputRedirected
        BufferSize             = $bufferSize
        WindowSize             = $windowSize
        CurrentCulture         = [cultureinfo]::CurrentCulture.Name
        CurrentUICulture       = [cultureinfo]::CurrentUICulture.Name
        PSStyleOutputRendering = $rendering
        NewLineHex             = ConvertTo-HexString ([Text.Encoding]::ASCII.GetBytes([Environment]::NewLine))
    }
}

# 行に分けて各種の統計を取る（最後の改行の後ろは行として数えない）
function Get-TextShape([string] $Text) {
    if ($null -eq $Text) { return $null }
    $crlf = ([regex]::Matches($Text, "`r`n")).Count
    $lf = ([regex]::Matches($Text, "(?<!`r)`n")).Count
    $cr = ([regex]::Matches($Text, "`r(?!`n)")).Count
    $endsWithNewLine = $Text.EndsWith("`n") -or $Text.EndsWith("`r")
    $body = [regex]::Replace($Text, "(`r`n|`n|`r)$", '')
    $lines = @(if ($Text.Length -eq 0) { } else { [regex]::Split($body, "`r`n|`n|`r") })
    $leading = 0
    foreach ($l in $lines) { if ($l.Trim().Length -eq 0) { $leading++ } else { break } }
    $trailing = 0
    for ($i = $lines.Count - 1; $i -ge 0; $i--) { if ($lines[$i].Trim().Length -eq 0) { $trailing++ } else { break } }
    if ($leading -eq $lines.Count) { $trailing = $leading }
    $maxLength = 0
    foreach ($l in $lines) { if ($l.Length -gt $maxLength) { $maxLength = $l.Length } }
    return [ordered]@{
        LineCount       = $lines.Count
        LeadingBlank    = $leading
        TrailingBlank   = $trailing
        EndsWithNewLine = $endsWithNewLine
        MaxLineLength   = $maxLength
        CrLfCount       = $crlf
        LoneLfCount     = $lf
        LoneCrCount     = $cr
        ContainsEscape  = $Text.Contains([string][char]27)
    }
}

# 文字列キーで序数比較の並べ替えを行う（Sort-Object はカルチャー依存のため使わない）
function Get-OrdinalSorted([object[]] $Items, [scriptblock] $Key) {
    $keyed = New-Object 'System.Collections.Generic.List[string]'
    for ($i = 0; $i -lt $Items.Count; $i++) {
        $keyed.Add(([string](& $Key $Items[$i])) + [char]0 + $i.ToString($inv))
    }
    $keys = $keyed.ToArray()
    [Array]::Sort([string[]]$keys, [StringComparer]::Ordinal)
    $sorted = New-Object 'System.Collections.Generic.List[object]'
    foreach ($k in $keys) { $sorted.Add($Items[[int]::Parse($k.Substring($k.IndexOf([char]0) + 1), $inv)]) }
    return , $sorted.ToArray()
}

# 出力が無いときに要素 1 個（$null）の配列にならないよう、文字列の配列に直す
function ConvertTo-Lines($Output) {
    if ($null -eq $Output) { return , [string[]]@() }
    return , [string[]]@($Output)
}

#endregion

#region 整形の層（5 章）の補助

$formatInputs = [ordered]@{
    '5-1' = { @('alpha', 'beta', 'gamma') }
    '5-2' = { @(42, 3.14159, -0.5, 1234567.891) }
    '5-3' = { @(
            [pscustomobject][ordered]@{ Name = 'Apple';  Count = 1;  Note = 'red' }
            [pscustomobject][ordered]@{ Name = 'Banana'; Count = 22; Note = 'yellow' }
            [pscustomobject][ordered]@{ Name = 'Cherry'; Count = 333; Note = 'dark red' }
        ) }
    '5-4' = { @([pscustomobject][ordered]@{ P1 = 'one'; P2 = 'two'; P3 = 'three'; P4 = 'four'; P5 = 'five'; P6 = 'six' }) }
    '5-5' = { @([pscustomobject][ordered]@{ Id = 1; Long = (('0123456789' * 25) + 'END') }) }
    '5-6' = { @(('abcdefghij' * 25) + 'END') }
    '5-7-null'  = { , @($null) }
    '5-7-empty' = { @('') }
    '5-7-array' = { , @() }
    '5-8' = { , @(1, @(2, 3), @(@(4, 5), 6)) }
    '5-9' = { @(
            [pscustomobject][ordered]@{ 名前 = '日本語の値'; Code = 'AB' }
            [pscustomobject][ordered]@{ 名前 = 'ｶﾀｶﾅ';       Code = 'CDEFGH' }
            [pscustomobject][ordered]@{ 名前 = 'x';          Code = '全角全角全角' }
        ) }
}

function Get-FormatInput([string] $Key) {
    $items = & $formatInputs[$Key]
    # 5-7-null / 5-7-array / 5-8 は、配列そのものを 1 つの値として返している（パイプラインで展開させるため）
    return , @($items)
}

function Invoke-PathA([object[]] $Items, $Width, [switch] $NoNewline) {
    $dir = New-CaseDirectory
    $p = Join-Path $dir 'a.txt'
    $extra = @{}
    if ($null -ne $Width) { $extra['Width'] = $Width }
    if ($NoNewline) { $extra['NoNewline'] = $true }
    $r = Invoke-Measured { $Items | Out-File -FilePath $p -Encoding unicode @extra -ErrorAction Continue -ErrorVariable ev }
    $text = $null
    if ([IO.File]::Exists($p)) { $text = [IO.File]::ReadAllText($p, [Text.Encoding]::Unicode) }
    return [ordered]@{ ErrorKind = $r.ErrorKind; Error = $r.Terminating; Text = $text; State = Get-FileState $p }
}

function Invoke-PathB([object[]] $Items, $Width) {
    $extra = @{}
    if ($null -ne $Width) { $extra['Width'] = $Width }
    $r = Invoke-Measured { $Items | Out-String -Stream @extra -ErrorAction Continue -ErrorVariable ev }
    return [ordered]@{ ErrorKind = $r.ErrorKind; Error = $r.Terminating; Lines = ConvertTo-Lines $r.Output }
}

function Invoke-PathC([object[]] $Items, $Width) {
    $command = 'Microsoft.PowerShell.Utility\Out-String -Stream'
    if ($null -ne $Width) { $command += ' -Width ' + ([int]$Width).ToString($inv) }
    $collected = New-Object 'System.Collections.Generic.List[string]'
    $sb = [scriptblock]::Create($command)
    $sp = $sb.GetSteppablePipeline()
    try {
        $sp.Begin($true)
        foreach ($o in $Items) {
            foreach ($x in @($sp.Process($o))) { $collected.Add([string]$x) }
        }
        foreach ($x in @($sp.End())) { $collected.Add([string]$x) }
    }
    finally {
        $sp.Dispose()
    }
    return [ordered]@{ Lines = $collected.ToArray() }
}

function Compare-FormatPaths([object[]] $Items, $Width) {
    $a = Invoke-PathA $Items $Width
    $b = Invoke-PathB $Items $Width
    $c = Invoke-PathC $Items $Width
    $nl = [Environment]::NewLine
    $bJoined = [string]::Join($nl, [string[]]$b.Lines)
    $cJoined = [string]::Join($nl, [string[]]$c.Lines)
    $aText = $a.Text
    $relation = 'different'
    if ($null -ne $aText) {
        if ($aText -ceq $bJoined) { $relation = 'A == B' }
        elseif ($aText -ceq ($bJoined + $nl)) { $relation = 'A == B + NewLine' }
        elseif ($aText -ceq ($nl + $bJoined + $nl)) { $relation = 'A == NewLine + B + NewLine' }
    }
    return [ordered]@{
        A         = [ordered]@{ ErrorKind = $a.ErrorKind; Error = $a.Error; Text = ConvertTo-Visible $aText; Shape = Get-TextShape $aText; FileLength = $a.State.Length }
        B         = [ordered]@{ ErrorKind = $b.ErrorKind; Error = $b.Error; ElementCount = @($b.Lines).Count; Joined = ConvertTo-Visible $bJoined; Shape = Get-TextShape $bJoined }
        C         = [ordered]@{ ElementCount = @($c.Lines).Count; Joined = ConvertTo-Visible $cJoined; Shape = Get-TextShape $cJoined }
        AvsB      = $relation
        BEqualsC  = (($b.Lines -join "`n") -ceq ($c.Lines -join "`n")) -and (@($b.Lines).Count -eq @($c.Lines).Count)
    }
}

#endregion

$results = [ordered]@{}
$results['Environment'] = Get-EnvironmentInfo

try {

#region 付録 A: 対話コンソールでの既定の幅

function Measure-DefaultWidth {
    # 表の列を幅いっぱいに使わせるため、長い値を 1 つだけ持つ表と、長い単一文字列の一覧形式を使う
    $wide = [pscustomobject][ordered]@{ Id = 1; Long = ('x' * 1000) }
    $dir = New-CaseDirectory
    $p = Join-Path $dir 'w.txt'
    $wide | Out-File -FilePath $p -Encoding unicode
    $outFileText = [IO.File]::ReadAllText($p, [Text.Encoding]::Unicode)
    $outStringText = [string]::Join([Environment]::NewLine, [string[]]@($wide | Out-String -Stream))
    $p2 = Join-Path $dir 'l.txt'
    $wide | Format-List | Out-File -FilePath $p2 -Encoding unicode
    $listText = [IO.File]::ReadAllText($p2, [Text.Encoding]::Unicode)
    return [ordered]@{
        OutFileTableMaxLine   = (Get-TextShape $outFileText).MaxLineLength
        OutStringTableMaxLine = (Get-TextShape $outStringText).MaxLineLength
        OutFileListMaxLine    = (Get-TextShape $listText).MaxLineLength
        OutFileTable          = ConvertTo-Visible $outFileText
    }
}

if ($InteractiveWidthOnly) {
    $results['DefaultWidth'] = Measure-DefaultWidth
    if ([string]::IsNullOrEmpty($OutFile)) {
        $windowWidth = 'unknown'
        try { $windowWidth = $Host.UI.RawUI.WindowSize.Width.ToString($inv) } catch { }
        $folder = Join-Path $PSScriptRoot 'OutFileBehaviorResults'
        [void][IO.Directory]::CreateDirectory($folder)
        $OutFile = Join-Path $folder ('interactive_{0}_{1}_w{2}.json' -f $PSVersionTable.PSEdition, $PSVersionTable.PSVersion.Major, $windowWidth)
    }
    $json = $results | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($OutFile, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ('結果を書き出しました: {0}' -f $OutFile)
    Write-Host ('  Out-File の表の最大行長: {0} / Out-String: {1} / 一覧形式: {2}' -f
        $results.DefaultWidth.OutFileTableMaxLine, $results.DefaultWidth.OutStringTableMaxLine, $results.DefaultWidth.OutFileListMaxLine)
    return
}

#endregion

#region モジュールの読み込み（参照測定用）

$tfm = if ($isCore) { 'net10.0' } else { 'net48' }
$moduleDll = Join-Path $repoRoot ('SnowStack.EncodingProbe.PowerShell\bin\{0}\{1}\SnowStack.EncodingProbe.PowerShell.dll' -f $Configuration, $tfm)
Import-Module $moduleDll
$results['Environment']['ModuleDll'] = $moduleDll.Substring($repoRoot.Length + 1)
$results['Environment']['ModuleFileVersion'] = [Diagnostics.FileVersionInfo]::GetVersionInfo($moduleDll).FileVersion

#endregion

#region 3 章: -NoClobber / -Append / -Force

$combos = @(
    @{ Id = '3-1';  Exists = $false; ReadOnly = $false; Params = @{ NoClobber = $true } }
    @{ Id = '3-2';  Exists = $true;  ReadOnly = $false; Params = @{ NoClobber = $true } }
    @{ Id = '3-3';  Exists = $true;  ReadOnly = $false; Params = @{ Append = $true; NoClobber = $true } }
    @{ Id = '3-4';  Exists = $false; ReadOnly = $false; Params = @{ Append = $true; NoClobber = $true } }
    @{ Id = '3-5';  Exists = $true;  ReadOnly = $false; Params = @{ Force = $true; NoClobber = $true } }
    @{ Id = '3-6';  Exists = $true;  ReadOnly = $true;  Params = @{ } }
    @{ Id = '3-7';  Exists = $true;  ReadOnly = $true;  Params = @{ Force = $true } }
    @{ Id = '3-8';  Exists = $true;  ReadOnly = $true;  Params = @{ Force = $true; NoClobber = $true } }
    @{ Id = '3-9';  Exists = $true;  ReadOnly = $true;  Params = @{ Append = $true } }
    @{ Id = '3-10'; Exists = $true;  ReadOnly = $true;  Params = @{ Append = $true; Force = $true } }
)
$section3 = [ordered]@{}
foreach ($combo in $combos) {
    $dir = New-CaseDirectory
    $p = Join-Path $dir 'target.txt'
    if ($combo.Exists) { New-OldFile $p }
    if ($combo.ReadOnly) { Set-ReadOnly $p }
    $params = $combo.Params
    $r = Invoke-Measured { 'NEW' | Out-File -FilePath $p -Encoding unicode @params -ErrorAction Continue -ErrorVariable ev }
    $section3[$combo.Id] = [ordered]@{
        Parameters     = [string]::Join(' ', [string[]]@(Get-OrdinalSorted @($params.Keys) { $args[0] } | ForEach-Object { '-' + $_ }))
        ErrorKind      = $r.ErrorKind
        Terminating    = $r.Terminating
        NonTerminating = $r.NonTerminating
        After          = Get-FileState $p
    }
}
$results['Section3_Combinations'] = $section3

# 3.2 エラーが起きる時点
$section32 = [ordered]@{}
foreach ($case in @(@{ Id = '3-2'; ReadOnly = $false; Params = @{ NoClobber = $true } }, @{ Id = '3-6'; ReadOnly = $true; Params = @{ } })) {
    $dir = New-CaseDirectory
    $p = Join-Path $dir 'target.txt'
    New-OldFile $p
    if ($case.ReadOnly) { Set-ReadOnly $p }
    $log = New-Object 'System.Collections.Generic.List[string]'
    $params = $case.Params
    $r = Invoke-Measured {
        & {
            begin   { $log.Add('upstream-begin') }
            process { $log.Add('upstream-process') }
            end     { $log.Add('upstream-end'); 'x'; $log.Add('upstream-after-first-output'); 'y'; $log.Add('upstream-after-second-output') }
        } | Out-File -FilePath $p -Encoding unicode @params -ErrorAction Continue -ErrorVariable ev
    }
    $section32[$case.Id] = [ordered]@{
        Log            = $log.ToArray()
        ErrorKind      = $r.ErrorKind
        Terminating    = $r.Terminating
        NonTerminating = $r.NonTerminating
        After          = Get-FileState $p
    }
}
$results['Section3_2_ErrorTiming'] = $section32

# 3.3 参考: 読み取り専用ファイルへの -Force 書き込み後の属性（本モジュールと Set-Content / Add-Content）
$section33 = [ordered]@{}
$refCases = [ordered]@{
    'Set-ProbedContent -Force'           = { Set-ProbedContent -LiteralPath $p -Value 'NEW' -Encoding unicodeBOM -Force -ErrorAction Continue -ErrorVariable ev }
    'Set-ProbedContent (no -Force)'      = { Set-ProbedContent -LiteralPath $p -Value 'NEW' -Encoding unicodeBOM -ErrorAction Continue -ErrorVariable ev }
    'Add-ProbedContent -Force'           = { Add-ProbedContent -LiteralPath $p -Value 'NEW' -Force -ErrorAction Continue -ErrorVariable ev }
    'Add-ProbedContent (no -Force)'      = { Add-ProbedContent -LiteralPath $p -Value 'NEW' -ErrorAction Continue -ErrorVariable ev }
    'Set-Content -Force'                 = { Set-Content -LiteralPath $p -Value 'NEW' -Encoding unicode -Force -ErrorAction Continue -ErrorVariable ev }
    'Add-Content -Force'                 = { Add-Content -LiteralPath $p -Value 'NEW' -Encoding unicode -Force -ErrorAction Continue -ErrorVariable ev }
}
foreach ($name in $refCases.Keys) {
    $dir = New-CaseDirectory
    $p = Join-Path $dir 'target.txt'
    New-OldFile $p
    Set-ReadOnly $p
    $r = Invoke-Measured $refCases[$name]
    $section33[$name] = [ordered]@{
        ErrorKind      = $r.ErrorKind
        Terminating    = $r.Terminating
        NonTerminating = $r.NonTerminating
        After          = Get-FileState $p
    }
}
$results['Section3_3_ModuleReadOnly'] = $section33

#endregion

#region 4 章: パスの扱い

$commonParameterNames = @(
    'Verbose', 'Debug', 'ErrorAction', 'WarningAction', 'InformationAction', 'ProgressAction',
    'ErrorVariable', 'WarningVariable', 'InformationVariable', 'OutVariable', 'OutBuffer', 'PipelineVariable',
    'WhatIf', 'Confirm'
)

function Get-ParameterMetadata([string] $CommandName) {
    $command = Get-Command $CommandName -CommandType Cmdlet
    $names = [string[]]@($command.Parameters.Keys | Where-Object { $commonParameterNames -notcontains $_ })
    [Array]::Sort([string[]]$names, [StringComparer]::Ordinal)
    $parameters = [ordered]@{}
    foreach ($name in $names) {
        $meta = $command.Parameters[$name]
        $aliases = [string[]]@($meta.Aliases)
        [Array]::Sort([string[]]$aliases, [StringComparer]::Ordinal)
        $sets = New-Object 'System.Collections.Generic.List[object]'
        $validation = New-Object 'System.Collections.Generic.List[string]'
        $otherAttributes = New-Object 'System.Collections.Generic.List[string]'
        $wildcard = $false
        foreach ($attr in $meta.Attributes) {
            if ($attr -is [System.Management.Automation.ParameterAttribute]) {
                $position = if ($attr.Position -eq [int]::MinValue) { $null } else { $attr.Position }
                $sets.Add([ordered]@{
                        Set                             = $attr.ParameterSetName
                        Position                        = $position
                        Mandatory                       = $attr.Mandatory
                        ValueFromPipeline               = $attr.ValueFromPipeline
                        ValueFromPipelineByPropertyName = $attr.ValueFromPipelineByPropertyName
                        ValueFromRemainingArguments     = $attr.ValueFromRemainingArguments
                    })
            }
            elseif ($attr -is [System.Management.Automation.ValidateRangeAttribute]) {
                $validation.Add(('ValidateRange({0}, {1})' -f $attr.MinRange, $attr.MaxRange))
            }
            elseif ($attr -is [System.Management.Automation.ValidateSetAttribute]) {
                $validation.Add(('ValidateSet({0})' -f [string]::Join(', ', [string[]]$attr.ValidValues)))
            }
            elseif ($attr -is [System.Management.Automation.ValidateArgumentsAttribute]) {
                $validation.Add($attr.GetType().Name)
            }
            elseif ($attr.GetType().Name -eq 'SupportsWildcardsAttribute') {
                $wildcard = $true
            }
            elseif ($attr.GetType().Name -eq 'ArgumentCompletionsAttribute') {
                $otherAttributes.Add('ArgumentCompletions')
            }
            elseif ($attr -isnot [System.Management.Automation.AliasAttribute]) {
                $otherAttributes.Add($attr.GetType().Name)
            }
        }
        $sortedSets = Get-OrdinalSorted $sets.ToArray() { $args[0].Set }
        $validationArray = $validation.ToArray(); [Array]::Sort([string[]]$validationArray, [StringComparer]::Ordinal)
        $otherArray = $otherAttributes.ToArray(); [Array]::Sort([string[]]$otherArray, [StringComparer]::Ordinal)
        $parameters[$name] = [ordered]@{
            Type             = $meta.ParameterType.FullName
            Aliases          = $aliases
            SupportsWildcards = $wildcard
            Validation       = $validationArray
            OtherAttributes  = $otherArray
            ParameterSets    = $sortedSets
        }
    }
    $setNames = [string[]]@($command.ParameterSets | ForEach-Object { $_.Name })
    [Array]::Sort([string[]]$setNames, [StringComparer]::Ordinal)
    return [ordered]@{
        DefaultParameterSet = $command.DefaultParameterSet
        ParameterSetNames   = $setNames
        Parameters          = $parameters
    }
}

$results['Section4_1_Metadata'] = [ordered]@{
    'Out-File'    = Get-ParameterMetadata 'Out-File'
    'Set-Content' = Get-ParameterMetadata 'Set-Content'
}

# 4.2 ワイルドカード
$wildcardCases = @(
    @{ Id = '4-1'; Existing = @('a1.txt');           Param = 'FilePath';    Value = 'a?.txt' }
    @{ Id = '4-2'; Existing = @('a1.txt', 'a2.txt'); Param = 'FilePath';    Value = 'a?.txt' }
    @{ Id = '4-3'; Existing = @();                   Param = 'FilePath';    Value = 'b*.txt' }
    @{ Id = '4-4'; Existing = @();                   Param = 'FilePath';    Value = 'c[1].txt' }
    @{ Id = '4-5'; Existing = @();                   Param = 'LiteralPath'; Value = 'c[1].txt' }
    @{ Id = '4-6'; Existing = @('c1.txt');           Param = 'FilePath';    Value = 'c[1].txt' }
)
$section42 = [ordered]@{}
foreach ($case in $wildcardCases) {
    $dir = New-CaseDirectory
    foreach ($name in $case.Existing) { New-OldFile (Join-Path $dir $name) }
    $params = @{ $case.Param = (Join-Path $dir $case.Value) }
    $r = Invoke-Measured { 'NEW' | Out-File @params -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
    $section42[$case.Id] = [ordered]@{
        Existing       = $case.Existing
        Parameter      = ('-{0} ''{1}''' -f $case.Param, $case.Value)
        ErrorKind      = $r.ErrorKind
        Terminating    = $r.Terminating
        NonTerminating = $r.NonTerminating
        FilesAfter     = Get-DirectoryListing $dir
    }
}
$results['Section4_2_Wildcard'] = $section42

# 4.3 相対パスの基準
$section43 = [ordered]@{}
$relCommands = [ordered]@{
    'Out-File'          = { 'NEW' | Out-File -FilePath 'rel.txt' -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
    'Set-Content'       = { Set-Content -Path 'rel.txt' -Value 'NEW' -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
    'Set-ProbedContent' = { Set-ProbedContent -Path 'rel.txt' -Value 'NEW' -Encoding unicodeBOM -ErrorAction Continue -ErrorVariable ev }
}
foreach ($name in $relCommands.Keys) {
    $psLocation = New-CaseDirectory
    $processDir = New-CaseDirectory
    $savedProcessDir = [Environment]::CurrentDirectory
    Push-Location -LiteralPath $psLocation
    try {
        [Environment]::CurrentDirectory = $processDir
        $r = Invoke-Measured $relCommands[$name]
    }
    finally {
        [Environment]::CurrentDirectory = $savedProcessDir
        Pop-Location
    }
    $section43[$name] = [ordered]@{
        ErrorKind            = $r.ErrorKind
        Terminating          = $r.Terminating
        NonTerminating       = $r.NonTerminating
        WrittenToPSLocation  = [IO.File]::Exists((Join-Path $psLocation 'rel.txt'))
        WrittenToProcessDir  = [IO.File]::Exists((Join-Path $processDir 'rel.txt'))
    }
}
$results['Section4_3_RelativePath'] = $section43

# 4.4 異常なパス
$section44 = [ordered]@{}
$dir = New-CaseDirectory
$missingParent = Join-Path (Join-Path $dir 'nosuchdir') 'x.txt'
$r = Invoke-Measured { 'NEW' | Out-File -FilePath $missingParent -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
$section44['4-7'] = [ordered]@{ Case = 'parent directory does not exist'; ErrorKind = $r.ErrorKind; Terminating = $r.Terminating; NonTerminating = $r.NonTerminating; After = Get-FileState $missingParent }
$dir = New-CaseDirectory
$subDir = Join-Path $dir 'sub'
[void][IO.Directory]::CreateDirectory($subDir)
$r = Invoke-Measured { 'NEW' | Out-File -FilePath $subDir -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
$section44['4-8'] = [ordered]@{ Case = 'path is an existing directory'; ErrorKind = $r.ErrorKind; Terminating = $r.Terminating; NonTerminating = $r.NonTerminating; After = Get-FileState $subDir }
$r = Invoke-Measured { 'NEW' | Out-File -FilePath '' -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
$section44['4-9'] = [ordered]@{ Case = 'empty string'; ErrorKind = $r.ErrorKind; Terminating = $r.Terminating; NonTerminating = $r.NonTerminating }
$r = Invoke-Measured { 'NEW' | Out-File -FilePath $null -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
$section44['4-9-null'] = [ordered]@{ Case = '$null (added)'; ErrorKind = $r.ErrorKind; Terminating = $r.Terminating; NonTerminating = $r.NonTerminating }
$results['Section4_4_AbnormalPath'] = $section44

#endregion

#region 5 章: 整形の層

$section5 = [ordered]@{}
foreach ($key in $formatInputs.Keys) {
    $items = Get-FormatInput $key
    $perWidth = [ordered]@{}
    foreach ($width in @($null, 40, 80, 200)) {
        $label = if ($null -eq $width) { 'default' } else { 'w' + $width.ToString($inv) }
        $perWidth[$label] = Compare-FormatPaths $items $width
    }
    $section5[$key] = $perWidth
}
$results['Section5_2_FormatPaths'] = $section5

# 5.4 -Width の検証範囲
$section54 = [ordered]@{}
$tableItems = Get-FormatInput '5-3'
foreach ($w in @(0, 1, -1, [int]::MaxValue)) {
    $label = $w.ToString($inv)
    $dir = New-CaseDirectory
    $p = Join-Path $dir 'w.txt'
    $rA = Invoke-Measured { $tableItems | Out-File -FilePath $p -Encoding unicode -Width $w -ErrorAction Continue -ErrorVariable ev }
    $rB = Invoke-Measured { $tableItems | Out-String -Stream -Width $w -ErrorAction Continue -ErrorVariable ev }
    $aText = if ([IO.File]::Exists($p)) { [IO.File]::ReadAllText($p, [Text.Encoding]::Unicode) } else { $null }
    $bJoined = if ($null -ne $rB.Output) { [string]::Join([Environment]::NewLine, (ConvertTo-Lines $rB.Output)) } else { $null }
    $section54[$label] = [ordered]@{
        OutFile   = [ordered]@{ ErrorKind = $rA.ErrorKind; Terminating = $rA.Terminating; Text = ConvertTo-Visible $aText; Shape = Get-TextShape $aText }
        OutString = [ordered]@{ ErrorKind = $rB.ErrorKind; Terminating = $rB.Terminating; Joined = ConvertTo-Visible $bJoined; Shape = Get-TextShape $bJoined }
    }
}
$results['Section5_4_WidthValidation'] = $section54
$results['Section5_4_DefaultWidth'] = Measure-DefaultWidth

# 5.5 -NoNewline
$section55 = [ordered]@{}
foreach ($key in @('5-1', '5-3', '5-4')) {
    $a = Invoke-PathA (Get-FormatInput $key) $null -NoNewline
    $section55[$key] = [ordered]@{ ErrorKind = $a.ErrorKind; Error = $a.Error; Text = ConvertTo-Visible $a.Text; Hex = $a.State.Hex }
}
$results['Section5_5_NoNewline'] = $section55

# 5.6 ANSI エスケープ
$esc = [char]27
$ansiInputs = [ordered]@{
    'escape-string' = @("$esc[31mred$esc[0m")
    '5-3-table'     = Get-FormatInput '5-3'
}
$section56 = [ordered]@{}
$renderings = if ($isCore) { @('Host', 'PlainText', 'Ansi') } else { @('(no $PSStyle)') }
foreach ($rendering in $renderings) {
    $saved = $null
    if ($isCore) { $saved = $PSStyle.OutputRendering; $PSStyle.OutputRendering = $rendering }
    try {
        $perInput = [ordered]@{}
        foreach ($key in $ansiInputs.Keys) {
            $cmp = Compare-FormatPaths $ansiInputs[$key] $null
            $perInput[$key] = [ordered]@{
                A_ContainsEscape = $cmp.A.Shape.ContainsEscape
                B_ContainsEscape = $cmp.B.Shape.ContainsEscape
                C_ContainsEscape = $cmp.C.Shape.ContainsEscape
                A                = $cmp.A.Text
                B                = $cmp.B.Joined
                C                = $cmp.C.Joined
            }
        }
        $section56[$rendering] = $perInput
    }
    finally {
        if ($isCore) { $PSStyle.OutputRendering = $saved }
    }
}
$results['Section5_6_Ansi'] = $section56

#endregion

#region 6 章: 文字列の中に含まれる改行

$newlineInputs = [ordered]@{
    '8-1' = @("a`nb")
    '8-2' = @("a`r`nb")
    '8-3' = @("a`rb")
    '8-4' = @("a`n")
    '8-5' = @("a`r`n`r`nb")
    '8-6' = @("a`nb", 'c')
}
$section6 = [ordered]@{}
foreach ($key in $newlineInputs.Keys) {
    $items = $newlineInputs[$key]
    $entry = [ordered]@{ Input = [string[]]@($items | ForEach-Object { ConvertTo-Visible $_ }) }

    $dir = New-CaseDirectory
    $p = Join-Path $dir 'outfile.txt'
    $r = Invoke-Measured { $items | Out-File -FilePath $p -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
    $entry['Out-File'] = [ordered]@{ ErrorKind = $r.ErrorKind; After = Get-FileState $p }

    $r = Invoke-Measured { $items | Out-String -Stream -ErrorAction Continue -ErrorVariable ev }
    $elements = ConvertTo-Lines $r.Output
    $entry['Out-String -Stream'] = [ordered]@{ ElementCount = $elements.Count; Elements = [string[]]@($elements | ForEach-Object { ConvertTo-Visible $_ }) }

    $p = Join-Path $dir 'setcontent.txt'
    $r = Invoke-Measured { $items | Set-Content -Path $p -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
    $entry['Set-Content'] = [ordered]@{ ErrorKind = $r.ErrorKind; After = Get-FileState $p }

    foreach ($lb in @('(omitted)', 'CrLf', 'Lf', 'Cr')) {
        $p = Join-Path $dir ('probed_' + $lb.Trim('(', ')') + '.txt')
        $extra = @{}
        if ($lb -ne '(omitted)') { $extra['LineBreak'] = $lb }
        $r = Invoke-Measured { $items | Set-ProbedContent -LiteralPath $p -Encoding unicodeBOM @extra -ErrorAction Continue -ErrorVariable ev }
        $entry['Set-ProbedContent -LineBreak ' + $lb] = [ordered]@{ ErrorKind = $r.ErrorKind; NonTerminating = $r.NonTerminating; After = Get-FileState $p }
    }

    $p = Join-Path $dir 'probed_nonewline.txt'
    $r = Invoke-Measured { $items | Set-ProbedContent -LiteralPath $p -Encoding unicodeBOM -NoNewline -ErrorAction Continue -ErrorVariable ev }
    $entry['Set-ProbedContent -NoNewline'] = [ordered]@{ ErrorKind = $r.ErrorKind; After = Get-FileState $p }

    $p = Join-Path $dir 'probed_append.txt'
    New-OldFile $p
    $r = Invoke-Measured { $items | Add-ProbedContent -LiteralPath $p -LineBreak Lf -ErrorAction Continue -ErrorVariable ev }
    $entry['Add-ProbedContent -LineBreak Lf (existing CRLF)'] = [ordered]@{ ErrorKind = $r.ErrorKind; NonTerminating = $r.NonTerminating; After = Get-FileState $p }

    $section6[$key] = $entry
}
$results['Section6_EmbeddedNewline'] = $section6

#endregion

#region 7 章: ファイルを作成・切り詰めるタイミング

function Get-LengthLabel([string] $Path) {
    $info = New-Object System.IO.FileInfo($Path)
    if (-not $info.Exists) { return 'absent' }
    return $info.Length.ToString($inv)
}

$section71 = [ordered]@{}
$timingCommands = [ordered]@{
    'Out-File'          = { & $upstream | Out-File -FilePath $p -Encoding unicode -ErrorAction Continue -ErrorVariable ev }
    'Set-ProbedContent' = { & $upstream | Set-ProbedContent -LiteralPath $p -Encoding unicodeBOM -ErrorAction Continue -ErrorVariable ev }
}
# 上流の各段階で、対象ファイルの長さ（無ければ absent）を記録する。
# 上流の begin は下流の BeginProcessing より前に呼ばれる。
# 上流の end が出力した値は、その場で下流の ProcessRecord に渡される。
$upstream = {
    begin   { $log.Add('upstream-begin: ' + (Get-LengthLabel $p)) }
    process { $log.Add('upstream-process: ' + (Get-LengthLabel $p)) }
    end {
        $log.Add('upstream-end (before first output): ' + (Get-LengthLabel $p))
        'x'
        $log.Add('upstream-end (after first output): ' + (Get-LengthLabel $p))
        'y'
        $log.Add('upstream-end (after second output): ' + (Get-LengthLabel $p))
    }
}
foreach ($name in $timingCommands.Keys) {
    foreach ($existing in @($true, $false)) {
        $dir = New-CaseDirectory
        $p = Join-Path $dir 'target.txt'
        if ($existing) { New-OldFile $p }
        $log = New-Object 'System.Collections.Generic.List[string]'
        $r = Invoke-Measured $timingCommands[$name]
        $label = '{0} / {1}' -f $name, $(if ($existing) { 'existing OLD' } else { 'absent' })
        $section71[$label] = [ordered]@{ Log = $log.ToArray(); ErrorKind = $r.ErrorKind; After = Get-FileState $p }
    }
}
$results['Section7_1_TruncateTiming'] = $section71

$section72 = [ordered]@{}
$emptyCases = [ordered]@{
    '7-1 (-Encoding omitted)' = @{ Existing = $false; Action = { @() | Out-File -FilePath $p -ErrorAction Continue -ErrorVariable ev } }
    '7-1 (-Encoding unicode)' = @{ Existing = $false; Action = { @() | Out-File -FilePath $p -Encoding unicode -ErrorAction Continue -ErrorVariable ev } }
    '7-1 (-Encoding utf8)'    = @{ Existing = $false; Action = { @() | Out-File -FilePath $p -Encoding $(if ($isCore) { 'utf8' } else { 'UTF8' }) -ErrorAction Continue -ErrorVariable ev } }
    '7-2'                     = @{ Existing = $false; Action = { Out-File -FilePath $p -InputObject $null -Encoding unicode -ErrorAction Continue -ErrorVariable ev } }
    '7-2 (-Encoding omitted)' = @{ Existing = $false; Action = { Out-File -FilePath $p -InputObject $null -ErrorAction Continue -ErrorVariable ev } }
    '7-3'                     = @{ Existing = $true;  Action = { @() | Out-File -FilePath $p -Encoding unicode -ErrorAction Continue -ErrorVariable ev } }
    '7-3 (-Encoding omitted)' = @{ Existing = $true;  Action = { @() | Out-File -FilePath $p -ErrorAction Continue -ErrorVariable ev } }
    '7-4'                     = @{ Existing = $false; Action = { @() | Out-File -FilePath $p -Append -Encoding unicode -ErrorAction Continue -ErrorVariable ev } }
    '7-4 (-Encoding omitted)' = @{ Existing = $false; Action = { @() | Out-File -FilePath $p -Append -ErrorAction Continue -ErrorVariable ev } }
}
foreach ($name in $emptyCases.Keys) {
    $dir = New-CaseDirectory
    $p = Join-Path $dir 'target.txt'
    if ($emptyCases[$name].Existing) { New-OldFile $p }
    $r = Invoke-Measured $emptyCases[$name].Action
    $section72[$name] = [ordered]@{ ErrorKind = $r.ErrorKind; After = Get-FileState $p }
}
$results['Section7_2_EmptyInput'] = $section72

#endregion

#region 8 章: 既定のエンコーディング

$section8 = [ordered]@{}
$dir = New-CaseDirectory
$p = Join-Path $dir 'default.txt'
$r = Invoke-Measured { 'aあ' | Out-File -FilePath $p -ErrorAction Continue -ErrorVariable ev }
$section8['Out-File (-Encoding omitted)'] = [ordered]@{ ErrorKind = $r.ErrorKind; After = Get-FileState $p }

$p = Join-Path $dir 'append.txt'
[IO.File]::WriteAllBytes($p, (New-Object System.Text.UTF8Encoding($false)).GetBytes("日本語`r`n"))
$before = Get-FileState $p
$r = Invoke-Measured { '追記' | Out-File -FilePath $p -Append -ErrorAction Continue -ErrorVariable ev }
$section8['Out-File -Append onto UTF-8 (no BOM) Japanese'] = [ordered]@{ ErrorKind = $r.ErrorKind; Before = $before; After = Get-FileState $p }
$results['Section8_DefaultEncoding'] = $section8

#endregion

}
finally {
    Remove-Sandbox
}

if ([string]::IsNullOrEmpty($OutFile)) {
    $results | ConvertTo-Json -Depth 20
}
else {
    $json = $results | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($OutFile, $json, (New-Object System.Text.UTF8Encoding($false)))
}
