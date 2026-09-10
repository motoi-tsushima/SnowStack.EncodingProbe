#Requires -Version 5.1
<#
.SYNOPSIS
    tests/EncodingProbe.Tests/TestData/ の言語別サンプルファイルを生成する。

.DESCRIPTION
    テストデータは「UTF-8 以外の文字エンコーディングであること」自体が試験の目的なので、
    エディターや整形ツールに触らせず、このスクリプトでバイト列を明示して生成する。
    .editorconfig の [*.txt] charset = utf-8-bom は TestData には適用してはならない
    （.gitattributes で TestData/** は -text にしてある）。

    生成対象は 1.2.0 で追加した「東アジア以外の言語」だけである。
    English / Japanese / Korean / Chinese_* の既存ファイルは上書きしない。

.PARAMETER TestDataRoot
    出力先の TestData フォルダー。既定はこのスクリプトから見た相対パス。

.EXAMPLE
    pwsh -NoProfile -File tools/New-EncodingTestData.ps1
#>
[CmdletBinding()]
param(
    [string] $TestDataRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($TestDataRoot)) {
    $TestDataRoot = Join-Path $PSScriptRoot '..\tests\EncodingProbe.Tests\TestData'
}
$TestDataRoot = [System.IO.Path]::GetFullPath($TestDataRoot)

# .NET Core では CP932 などのコードページがこれ無しでは取れない
[System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)

<#
.SYNOPSIS
    指定した文字エンコーディングでテキストを書き出す（BOM は付けない）。
#>
function Write-SampleFile {
    param(
        [Parameter(Mandatory)] [string] $Language,
        [Parameter(Mandatory)] [string] $FileName,
        [Parameter(Mandatory)] [int]    $CodePage,
        [Parameter(Mandatory)] [string] $Text
    )

    $directory = Join-Path $TestDataRoot $Language
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    if ($CodePage -eq 65001) {
        $encoding = New-Object System.Text.UTF8Encoding($false)
    }
    else {
        $encoding = [System.Text.Encoding]::GetEncoding($CodePage)
    }

    $bytes = $encoding.GetBytes($Text)
    $path  = Join-Path $directory $FileName
    [System.IO.File]::WriteAllBytes($path, $bytes)

    # 往復して、指定した文字エンコーディングで表現しきれない文字が無いことを確かめる
    if ($encoding.GetString($bytes) -ne $Text) {
        throw "$Language/$FileName : cp$CodePage で表現できない文字が含まれている。"
    }

    '{0,-22} {1,-24} cp{2,-6} {3,5} bytes' -f $Language, $FileName, $CodePage, $bytes.Length
}

#--------------------------------------------------------------------------
# ドイツ語
#   cp1252 と ISO-8859-1 でバイト列が一致する文字だけを使う。
#   このバイト列は Shift_JIS としても構造が成立する（例: FC DF = "üß" が
#   Shift_JIS の外字領域の 2 バイト文字になる）。日本語カルチャーの実行環境で
#   Shift_JIS と誤判定していた回帰を固定するためのデータである。
#--------------------------------------------------------------------------
$germanText = @"
Grüße aus München. Die Straße ist für Fußgänger gesperrt.
Äpfel, Öl und Übung. Schöne Grüße von Herrn Müller.
Über die Brücke geht es zum Bahnhof. Wir müssen früh los.
Der Fluß führt nach Süden. Die Tür war größer als gedacht.
"@ -replace "`r`n", "`r`n"

#--------------------------------------------------------------------------
# フランス語
#   cp1252 と ISO-8859-15 でバイト列が一致する文字だけを使う。
#--------------------------------------------------------------------------
$frenchText = @"
Français : portez ce vieux whisky au juge blond qui fume.
L'été dernier, à Genève, il a été très déçu par la crème brûlée.
Où sont les clés ? Près de la fenêtre, à côté du théâtre.
Ça va très bien, merci. Août est un mois agréable.
"@

#--------------------------------------------------------------------------
# ロシア語
#--------------------------------------------------------------------------
$russianText = @"
Русский язык. Съешь ещё этих мягких французских булок, да выпей чаю.
В чащах юга жил бы цитрус? Да, но фальшивый экземпляр!
Широкая электрификация южных губерний даст мощный толчок.
Эх, чужак, общий съём цен шляп (юфть) вдрызг!
"@

#--------------------------------------------------------------------------
# ポーランド語
#   cp1250 と ISO-8859-2 ではバイト列が異なる文字があるため、
#   それぞれの符号化で別々に書き出す（同じ本文から生成する）。
#--------------------------------------------------------------------------
$polishText = @"
Polski. Pchnąć w tę łódź jeża lub ośm skrzyń fig.
Zażółć gęślą jaźń. Mężny bądź, chroń pułk twój i sześć flag.
Śnieżna zima w Krakowie. Wąska ścieżka prowadzi do źródła.
Dość gróźb! Ćwierć litra żurku i już.
"@

#--------------------------------------------------------------------------
# タイ語
#--------------------------------------------------------------------------
$thaiText = @"
ภาษาไทย เป็นภาษาราชการของประเทศไทย
เป็นเด็กดีต้องขยันหมั่นเพียร อ่านหนังสือทุกวัน
กรุงเทพมหานคร เป็นเมืองหลวงของประเทศไทย
ฉันชอบกินข้าวผัดกับไข่ดาว อร่อยมาก
"@

$results = @()
$results += Write-SampleFile -Language 'German'  -FileName 'sample_cp1252.txt'     -CodePage 1252  -Text $germanText
$results += Write-SampleFile -Language 'German'  -FileName 'sample_iso8859_1.txt'  -CodePage 28591 -Text $germanText
$results += Write-SampleFile -Language 'German'  -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $germanText

$results += Write-SampleFile -Language 'French'  -FileName 'sample_cp1252.txt'     -CodePage 1252  -Text $frenchText
$results += Write-SampleFile -Language 'French'  -FileName 'sample_iso8859_15.txt' -CodePage 28605 -Text $frenchText
$results += Write-SampleFile -Language 'French'  -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $frenchText

$results += Write-SampleFile -Language 'Russian' -FileName 'sample_cp1251.txt'     -CodePage 1251  -Text $russianText
$results += Write-SampleFile -Language 'Russian' -FileName 'sample_koi8r.txt'      -CodePage 20866 -Text $russianText
$results += Write-SampleFile -Language 'Russian' -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $russianText

$results += Write-SampleFile -Language 'Polish'  -FileName 'sample_cp1250.txt'     -CodePage 1250  -Text $polishText
$results += Write-SampleFile -Language 'Polish'  -FileName 'sample_iso8859_2.txt'  -CodePage 28592 -Text $polishText
$results += Write-SampleFile -Language 'Polish'  -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $polishText

$results += Write-SampleFile -Language 'Thai'    -FileName 'sample_cp874.txt'      -CodePage 874   -Text $thaiText
$results += Write-SampleFile -Language 'Thai'    -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $thaiText

$results
