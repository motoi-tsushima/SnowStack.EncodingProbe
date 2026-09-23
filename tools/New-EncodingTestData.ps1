#Requires -Version 5.1
<#
.SYNOPSIS
    tests/EncodingProbe.Tests/TestData/ の言語別サンプルファイルを生成する。

.DESCRIPTION
    テストデータは「UTF-8 以外の文字エンコーディングであること」自体が試験の目的なので、
    エディターや整形ツールに触らせず、このスクリプトでバイト列を明示して生成する。
    .editorconfig の [*.txt] charset = utf-8-bom は TestData には適用してはならない
    （.gitattributes で TestData/** は -text にしてある）。

    生成対象は 1.2.0 で追加した「東アジア以外の言語」と香港（Chinese_HongKong）だけである。
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

    # ヒアドキュメントの改行はこのスクリプト自体の改行コードに従うため、CRLF に揃える
    $Text = $Text -replace "`r?`n", "`r`n"

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

#--------------------------------------------------------------------------
# 香港（繁体字）
#   本文は cp950 で表現できる共通漢字だけを使う。
#   sample_big5hkscs.txt は、これに HKSCS 固有領域のバイト列を明示して挟んだもの。
#   HKSCS 固有字は .NET の cp950 エンコーダーでは生成できない
#   （復号すると私用領域 U+E000–U+F8FF に写される）。
#--------------------------------------------------------------------------
$hongKongText = @"
香港是一個國際大都會，粵語是香港人的主要語言。
今天天氣很好，我們一起去飲茶吧。
維多利亞港的夜景非常美麗，每晚都有很多遊客。
中文資訊處理需要正確的文字編碼判斷方法。
"@

# HKSCS 固有領域の 4 文字（88 62 / 8B F8 / FA 5F / FE 52）
$hkscsBytes = [byte[]](0x88, 0x62, 0x8B, 0xF8, 0xFA, 0x5F, 0xFE, 0x52)

<#
.SYNOPSIS
    Big5（cp950）の本文の 1 行目の末尾に、HKSCS 固有領域のバイト列を挟んで書き出す。
#>
function Write-HkscsSampleFile {
    param(
        [Parameter(Mandatory)] [string] $Language,
        [Parameter(Mandatory)] [string] $FileName,
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [byte[]] $ExtensionBytes
    )

    $directory = Join-Path $TestDataRoot $Language
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    # ヒアドキュメントの改行はこのスクリプト自体の改行コードに従うため、CRLF に揃える
    $Text = $Text -replace "`r?`n", "`r`n"

    $encoding = [System.Text.Encoding]::GetEncoding(950)
    $body = $encoding.GetBytes($Text)
    if ($encoding.GetString($body) -ne $Text) {
        throw "$Language/$FileName : cp950 で表現できない文字が含まれている。"
    }

    # 1 行目の改行の直前（文字境界）に挟む
    $insertAt = [Array]::IndexOf($body, [byte]0x0A)
    if ($insertAt -gt 0 -and $body[$insertAt - 1] -eq 0x0D) { $insertAt-- }

    $bytes = New-Object byte[] ($body.Length + $ExtensionBytes.Length)
    [Array]::Copy($body, 0, $bytes, 0, $insertAt)
    [Array]::Copy($ExtensionBytes, 0, $bytes, $insertAt, $ExtensionBytes.Length)
    [Array]::Copy($body, $insertAt, $bytes, $insertAt + $ExtensionBytes.Length, $body.Length - $insertAt)

    # 私用領域を経由してもバイト列が往復することを確かめる（私用領域の扱い 4.1）
    $back = $encoding.GetBytes($encoding.GetString($bytes))
    if ([BitConverter]::ToString($back) -ne [BitConverter]::ToString($bytes)) {
        throw "$Language/$FileName : cp950 で往復しない。"
    }

    $path = Join-Path $directory $FileName
    [System.IO.File]::WriteAllBytes($path, $bytes)

    '{0,-22} {1,-24} cp{2,-6} {3,5} bytes' -f $Language, $FileName, 950, $bytes.Length
}

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

$results += Write-SampleFile -Language 'Chinese_HongKong' -FileName 'sample_big5.txt' -CodePage 950   -Text $hongKongText
$results += Write-SampleFile -Language 'Chinese_HongKong' -FileName 'sample_utf8.txt' -CodePage 65001 -Text $hongKongText
$results += Write-HkscsSampleFile -Language 'Chinese_HongKong' -FileName 'sample_big5hkscs.txt' -Text $hongKongText -ExtensionBytes $hkscsBytes

$results
