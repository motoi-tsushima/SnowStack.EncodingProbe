#Requires -Version 5.1
<#
.SYNOPSIS
    tests/EncodingProbe.Tests/TestData/ の言語別サンプルファイルを生成する。

.DESCRIPTION
    テストデータは「UTF-8 以外の文字エンコーディングであること」自体が試験の目的なので、
    エディターや整形ツールに触らせず、このスクリプトでバイト列を明示して生成する。
    .editorconfig の [*.txt] charset = utf-8-bom は TestData には適用してはならない
    （.gitattributes で TestData/** は -text にしてある）。

    生成対象は 1.2.0 で追加した「東アジア以外の言語」、香港（Chinese_HongKong）、
    繁体字・簡体字の長めのサンプル（*_long.txt）だけである。
    English / Japanese / Korean / Chinese_* の既存ファイルは上書きしない。

    言語の選び方と、各ファイルで何を固定しているかは
    docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md を参照。

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

#--------------------------------------------------------------------------
# スペイン語・エストニア語
#   東アジアのカルチャーで独自判定が旧マルチバイトを返し（上書き経路に入り）、
#   UTF.Unknown のシングルバイトで正しく救済される。上書きの回帰を検出するためのデータ
#--------------------------------------------------------------------------
$spanishText = @"
Español. El pingüino Wenceslao hizo kilómetros bajo exhaustiva lluvia y frío.
Madrid es la capital de España y tiene una larga historia.
Los niños van a la escuela cada mañana a pie.
Muchas gracias y hasta pronto.
"@

$estonianText = @"
Eesti keel. Põdur Zagrebi tšellomängija-följetonist Ciqo külmetas kehvas garaažis.
Tallinn on Eesti pealinn ja suurim linn.
Lapsed käivad igal hommikul jala koolis.
Aitäh ja head päeva.
"@

#--------------------------------------------------------------------------
# ウクライナ語
#   UTF.Unknown の信頼度が採用の下限（0.5）に届かず、判定不能になる（cp1251 / KOI8-U とも）。
#   KOI8-U は UTF.Unknown が知らず、koi8-r と答える
#--------------------------------------------------------------------------
$ukrainianText = @"
Українська мова. Чуєш їх, доцю, га? Кумедна ж ти, прощайся без ґольфів!
Київ є столицею України та найбільшим містом країни.
Діти щоранку ходять до школи пішки.
Дякую і гарного дня.
"@

#--------------------------------------------------------------------------
# ルーマニア語（ISO-8859-16）
#   UTF.Unknown は iso-8859-16 と答えるが、.NET には iso-8859-16 が無い。
#   1.1.0 では NullReferenceException になっていた（緊急修正で直した経路の固定）。
#   .NET に符号化器が無いため、バイトの対応を明示して書き出す
#--------------------------------------------------------------------------
$romanianText = @"
Limba română este o limbă romanică. Fiecare zi este o nouă șansă de a învăța.
București este capitala României și cel mai mare oraș din țară.
Copiii merg în fiecare dimineață la școală pe jos.
Mulțumesc frumos și o zi bună.
"@

$iso885916Bytes = @{
    [char]0x0103 = 0xE3; [char]0x00E2 = 0xE2; [char]0x00EE = 0xEE; [char]0x0219 = 0xBA; [char]0x021B = 0xFE
    [char]0x0102 = 0xC3; [char]0x00C2 = 0xC2; [char]0x00CE = 0xCE; [char]0x0218 = 0xAA; [char]0x021A = 0xDE
}

#--------------------------------------------------------------------------
# 短文の既知の限界（*_short_*.txt）
#   UTF.Unknown の短文での信頼度・精度の限界により、東アジアのカルチャーで誤判定が残る入力。
#   現在の挙動が正しいわけではない。テストは UTF.Unknown の改善に気づくために置いている。
#   測定した行と同じバイト列にするため、末尾に改行（CRLF）を付ける
#--------------------------------------------------------------------------
$icelandicShortText = "Takk kærlega og góðan dag.`r`n"
$russianShortText = "Эх, чужак, общий съём цен шляп (юфть) вдрызг!`r`n"
$ukrainianShortText = "Київ є столицею України та найбільшим містом країни.`r`n"

#--------------------------------------------------------------------------
# 繁体字・簡体字の長めのサンプル（200 バイト程度）
#   既存の東アジアのテストデータは 4〜15 バイトと短く、UTF.Unknown が系統（Big5 / GB）を判定できない。
#   繁簡の系統クロスチェック（1.2.0 第二次修正）が通常のテストデータでも発動するよう、長めの文を置く。
#   簡体字は GB2312 の範囲の字だけを使う（GB 系の判別規則で 936 になる）
#--------------------------------------------------------------------------
$traditionalLongText = @"
繁體中文是臺灣、香港與澳門使用的書寫系統。這份測試資料用來確認文字編碼的判斷結果是否正確。
電腦在讀取檔案時，必須先知道檔案使用哪一種編碼，才能正確顯示文字內容。如果判斷錯誤，畫面上就會出現亂碼。
"@

$simplifiedLongText = @"
简体中文是中国大陆和新加坡使用的书写系统。这份测试数据用来确认文字编码的判断结果是否正确。
电脑在读取文件时，必须先知道文件使用哪一种编码，才能正确显示文字内容。如果判断错误，屏幕上就会出现乱码。
"@

<#
.SYNOPSIS
    .NET に符号化器の無い文字エンコーディングで、バイトの対応を明示して書き出す（BOM は付けない）。
#>
function Write-ExplicitBytesFile {
    param(
        [Parameter(Mandatory)] [string]    $Language,
        [Parameter(Mandatory)] [string]    $FileName,
        [Parameter(Mandatory)] [string]    $EncodingName,
        [Parameter(Mandatory)] [hashtable] $Map,
        [Parameter(Mandatory)] [string]    $Text
    )

    $directory = Join-Path $TestDataRoot $Language
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    $Text = $Text -replace "`r?`n", "`r`n"
    $bytes = New-Object byte[] $Text.Length
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ([int]$c -lt 0x80) { $bytes[$i] = [byte][int]$c }
        elseif ($Map.ContainsKey($c)) { $bytes[$i] = [byte]$Map[$c] }
        else { throw "$Language/$FileName : $EncodingName に対応を定義していない文字 '$c' が含まれている。" }
    }

    $path = Join-Path $directory $FileName
    [System.IO.File]::WriteAllBytes($path, $bytes)

    '{0,-22} {1,-24} {2,-8} {3,5} bytes' -f $Language, $FileName, $EncodingName, $bytes.Length
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

$results += Write-SampleFile -Language 'Spanish'   -FileName 'sample_cp1252.txt'     -CodePage 1252  -Text $spanishText
$results += Write-SampleFile -Language 'Spanish'   -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $spanishText

$results += Write-SampleFile -Language 'Estonian'  -FileName 'sample_cp1257.txt'     -CodePage 1257  -Text $estonianText
$results += Write-SampleFile -Language 'Estonian'  -FileName 'sample_iso8859_15.txt' -CodePage 28605 -Text $estonianText
$results += Write-SampleFile -Language 'Estonian'  -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $estonianText

$results += Write-SampleFile -Language 'Ukrainian' -FileName 'sample_cp1251.txt'     -CodePage 1251  -Text $ukrainianText
$results += Write-SampleFile -Language 'Ukrainian' -FileName 'sample_koi8u.txt'      -CodePage 21866 -Text $ukrainianText
$results += Write-SampleFile -Language 'Ukrainian' -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $ukrainianText

$results += Write-ExplicitBytesFile -Language 'Romanian' -FileName 'sample_iso8859_16.txt' -EncodingName 'iso-8859-16' -Map $iso885916Bytes -Text $romanianText
$results += Write-SampleFile -Language 'Romanian'  -FileName 'sample_utf8.txt'       -CodePage 65001 -Text $romanianText

$results += Write-SampleFile -Language 'Icelandic' -FileName 'sample_short_cp1252.txt' -CodePage 1252  -Text $icelandicShortText
$results += Write-SampleFile -Language 'Russian'   -FileName 'sample_short_koi8r.txt'  -CodePage 20866 -Text $russianShortText
$results += Write-SampleFile -Language 'Ukrainian' -FileName 'sample_short_koi8u.txt'  -CodePage 21866 -Text $ukrainianShortText

$results += Write-SampleFile -Language 'Chinese_Traditional' -FileName 'sample_big5_long.txt' -CodePage 950 -Text $traditionalLongText
$results += Write-SampleFile -Language 'Chinese_Simplified'  -FileName 'sample_gbk_long.txt'  -CodePage 936 -Text $simplifiedLongText

$results
