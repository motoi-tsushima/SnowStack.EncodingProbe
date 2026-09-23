#Requires -Version 5.1
<#
.SYNOPSIS
    クロスチェックの信頼度の下限（0.5 / 0.55 / 0.8）の根拠となる値を、実行中のホストで測定する。

.DESCRIPTION
    EncodingProbe.cs の信頼度の下限は、UTF.Unknown の実測値に基づいて決めている
    （docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md）。
    余裕が上下とも 0.02 程度しかないため、UTF.Unknown を更新したときはこの測定をやり直すこと。

    このスクリプトは 1 つのホストで測定し、結果を TSV に書き出す。
    PowerShell 5.1（net48）と 7.x（net10.0）の両方で測定して集計するには
    Invoke-CrossCheckMeasurement.ps1 を使う。

    測定する入力は 3 種類（TSV の kind 列）。
      A … tests/EncodingProbe.Tests/TestData の東アジア以外の言語のファイル（シングルバイト系）
      B … 東アジア旧マルチバイトの入力（テストデータと、バイト列を明示して組み立てた Big5 / GBK / HKSCS）
      X … テストデータに無いシングルバイト系言語の文（このスクリプト内の本文。1 行ずつ・全文・全文 ×3）

    各入力について、UTF.Unknown の生の判定（名前・コードページ・信頼度）と、
    カルチャーごとの独自判定（NativeOnly）と既定の判定（Combined）の結果を記録する。

.PARAMETER Bin
    SnowStack.EncodingProbe.dll と UtfUnknown.dll があるフォルダー。
    既定は tests/EncodingProbe.Tests/bin/<Configuration>/<TFM>（TFM はホストから決める）。

.PARAMETER Configuration
    ビルド構成。既定は Debug。

.PARAMETER OutFile
    結果の TSV の出力先（UTF-8、BOM 無し）。

.EXAMPLE
    pwsh -NoProfile -File tools/Measure-CrossCheckConfidence.ps1 -OutFile measure_net10.tsv
#>
[CmdletBinding()]
param(
    [string] $Bin,
    [string] $Configuration = 'Debug',
    [Parameter(Mandatory = $true)] [string] $OutFile
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$isCore = ($PSVersionTable.PSEdition -eq 'Core')
if ([string]::IsNullOrEmpty($Bin)) {
    $tfm = if ($isCore) { 'net10.0' } else { 'net48' }
    $Bin = Join-Path $repositoryRoot "tests\EncodingProbe.Tests\bin\$Configuration\$tfm"
}
foreach ($dll in 'UtfUnknown.dll', 'SnowStack.EncodingProbe.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $Bin $dll))) {
        throw "$dll が見つかりません: $Bin`n先に dotnet build を実行してください。"
    }
}
Add-Type -Path (Join-Path $Bin 'UtfUnknown.dll')
Add-Type -Path (Join-Path $Bin 'SnowStack.EncodingProbe.dll')
if ($isCore) {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}

# WorldLanguageTests と同じカルチャー
$cultures = 'de-DE', 'fr-FR', 'ru-RU', 'pl-PL', 'th-TH', 'en-US', 'ja-JP', 'ko-KR', 'zh-CN', 'zh-TW', 'zh-HK', 'kok-IN'

$testDataRoot = Join-Path $repositoryRoot 'tests\EncodingProbe.Tests\TestData'
$eastAsianFolders = 'Japanese', 'Korean', 'Chinese_Simplified', 'Chinese_Traditional', 'Chinese_HongKong'

#--------------------------------------------------------------------------
# 1 件ぶんの測定
#--------------------------------------------------------------------------
function Measure-Input {
    param(
        [string] $Kind,
        [string] $Label,
        [string] $SourceCodePage,
        [string] $Variant,
        [byte[]] $Bytes,
        [string] $Text   # 元の本文（復号の正否を確かめる。分からない場合は空）
    )

    $detected = [UtfUnknown.CharsetDetector]::DetectFromBytes($Bytes).Detected
    $uuName = if ($detected) { $detected.EncodingName } else { '' }
    $uuCp = if ($detected -and $detected.Encoding) { $detected.Encoding.CodePage } else { -1 }
    $uuConf = if ($detected) { '{0:F5}' -f $detected.Confidence } else { '0.00000' }
    $uuDecode = Test-Decode $Bytes ([string]$uuCp) $Text

    $cells = foreach ($culture in $cultures) {
        $native = Invoke-Detect $Bytes $culture 'NativeOnly'
        $combined = Invoke-Detect $Bytes $culture 'Combined'
        $decode = Test-Decode $Bytes $combined $Text
        "$native`t$combined`t$decode"
    }

    $script:rows.Add(("{0}`t{1}`t{2}`t{3}`t{4}`t{5}`t{6}`t{7}`t{8}`t{9}" -f
        $Kind, $Label, $SourceCodePage, $Variant, $Bytes.Length, $uuName, $uuCp, $uuConf, $uuDecode, ($cells -join "`t")))
}

function Invoke-Detect {
    param([byte[]] $Bytes, [string] $Culture, [string] $Strategy)

    $options = New-Object SnowStack.EncodingProbe.EncodingDetectorOptions
    $options.Culture = $Culture
    $options.Strategy = [SnowStack.EncodingProbe.DetectionStrategy]$Strategy
    try {
        return [string][SnowStack.EncodingProbe.EncodingProbe]::Detect([byte[]]$Bytes, $options).CodePage
    }
    catch {
        $e = $_.Exception
        while ($null -ne $e.InnerException) { $e = $e.InnerException }
        return 'EXC:' + $e.GetType().Name
    }
}

# 判定結果のコードページで復号して元の本文に戻るか（same / DIFF / -）
function Test-Decode {
    param([byte[]] $Bytes, [string] $CodePage, [string] $Text)

    if ([string]::IsNullOrEmpty($Text)) { return '-' }
    $cp = 0
    if (-not [int]::TryParse($CodePage, [ref]$cp) -or $cp -le 0) { return '-' }
    try { $encoding = [System.Text.Encoding]::GetEncoding($cp) } catch { return '-' }
    $decoded = $encoding.GetString($Bytes).Normalize([Text.NormalizationForm]::FormC)
    if ($decoded -ceq $Text.Normalize([Text.NormalizationForm]::FormC)) { 'same' } else { 'DIFF' }
}

$script:rows = New-Object System.Collections.Generic.List[string]
$script:rows.Add("kind`tlabel`tsource_cp`tvariant`tbytes`tuu_name`tuu_cp`tuu_conf`tuu_decode`t" +
    (($cultures | ForEach-Object { "native_$_`tcombined_$_`tdecode_$_" }) -join "`t"))

# テストデータのファイル名（sample_cp1252.txt など）からコードページを求める。.NET に無いものは -1
function Get-CodePageFromFileName {
    param([string] $FileName)
    $map = [ordered]@{
        'iso8859_15' = 28605; 'iso8859_16' = -1; 'iso8859_1' = 28591; 'iso8859_2' = 28592
        'koi8r' = 20866; 'koi8u' = 21866
        'cp1250' = 1250; 'cp1251' = 1251; 'cp1252' = 1252; 'cp1257' = 1257; 'cp874' = 874
    }
    foreach ($key in $map.Keys) { if ($FileName -like "*$key*") { return $map[$key] } }
    return -1
}

# ファイル名の順序で並べる。Sort-Object はカルチャーに依存した比較を使い、
# .NET Framework（NLS）と .NET（ICU）で sample_gbk.txt と sample_gbk_long.txt の順序が入れ替わるため、序数比較で並べる
function Get-OrdinalSorted {
    param([object[]] $Items)
    # [Array]::Sort(keys, items, comparer) は PowerShell 7 で別のオーバーロードに束縛され、
    # items が並べ替えられないため、名前だけを並べてから引き当てる
    $array = @($Items)
    $names = [string[]]@($array | ForEach-Object { $_.Name })
    [Array]::Sort($names, [StringComparer]::Ordinal)
    foreach ($name in $names) { $array | Where-Object { $_.Name -ceq $name } }
}

#--------------------------------------------------------------------------
# A: テストデータのシングルバイト系言語
#--------------------------------------------------------------------------
foreach ($folder in (Get-OrdinalSorted @(Get-ChildItem -LiteralPath $testDataRoot -Directory))) {
    if ($eastAsianFolders -contains $folder.Name -or $folder.Name -eq 'English') { continue }
    foreach ($file in (Get-OrdinalSorted @(Get-ChildItem -LiteralPath $folder.FullName -Filter '*.txt'))) {
        if ($file.Name -like '*utf8*') { continue }
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        # ファイル名が示す文字エンコーディングで復号した本文を「正答」とする（.NET に無いものは空）
        $sourceCp = Get-CodePageFromFileName $file.Name
        $text = ''
        if ($sourceCp -gt 0) { $text = [System.Text.Encoding]::GetEncoding($sourceCp).GetString($bytes) }
        Measure-Input 'A' ("{0}/{1}" -f $folder.Name, $file.Name) ([string]$sourceCp) 'file' $bytes $text
    }
}

#--------------------------------------------------------------------------
# B: 東アジア旧マルチバイト
#--------------------------------------------------------------------------
foreach ($folderName in $eastAsianFolders) {
    $folder = Join-Path $testDataRoot $folderName
    if (-not (Test-Path -LiteralPath $folder)) { continue }
    foreach ($file in (Get-OrdinalSorted @(Get-ChildItem -LiteralPath $folder -Filter '*.txt'))) {
        # Unicode 系と ISO-2022-JP（エスケープシーケンスで確定する）は対象外
        if ($file.Name -like '*utf8*' -or $file.Name -eq 'sample_jis.txt') { continue }
        Measure-Input 'B' ("{0}/{1}" -f $folderName, $file.Name) '' 'file' ([IO.File]::ReadAllBytes($file.FullName)) ''
    }
}

# CrossCheckConfidenceTests と同じ文字（いずれも 2 バイト固定）
$big5Chars = 0xB36F, 0xAC4F, 0xA440, 0xADD3, 0xA5CE, 0xA9F3, 0xB4FA, 0xB8D5, 0xAABA, 0xC163, 0xC5E9, 0xA4A4,
             0xA4E5, 0xA579, 0xA46C, 0xA143, 0xA672, 0xBD73, 0xBD58, 0xA750, 0xA977, 0xBCCB, 0xA5BB
$gbkChars = 0xD5E2, 0xCAC7, 0xD2BB, 0xB8F6, 0xD3C3, 0xD3DA, 0xB2E2, 0xCAD4, 0xB5C4, 0xBCF2, 0xCCE5, 0xD6D0,
            0xCEC4, 0xBEE4, 0xD7D3, 0xA1A3, 0xD7D6, 0xB1E0, 0xC2EB, 0xC5D0, 0xB6A8, 0xD1F9, 0xB1BE
$hkscs = [byte[]](0x88, 0x62, 0xFA, 0x5F)

function New-Repeat {
    param([int[]] $Chars, [int] $Length, [int] $Offset)
    $buffer = New-Object byte[] $Length
    for ($i = 0; $i -lt $Length; $i += 2) {
        $c = $Chars[(($i / 2) + $Offset) % $Chars.Length]
        $buffer[$i] = [byte](($c -shr 8) -band 0xFF)
        $buffer[$i + 1] = [byte]($c -band 0xFF)
    }
    return , $buffer
}

Measure-Input 'B' 'GBK「这是一」' '936' '6' ([byte[]](0xD5, 0xE2, 0xCA, 0xC7, 0xD2, 0xBB)) ''
foreach ($length in 6, 8, 12, 16, 24, 32, 64, 128, 256, 512) {
    foreach ($offset in 0, 5, 10) {
        Measure-Input 'B' 'GBK 反復' '936' ("{0}/{1}" -f $length, $offset) (New-Repeat $gbkChars $length $offset) ''
        Measure-Input 'B' 'Big5 反復' '950' ("{0}/{1}" -f $length, $offset) (New-Repeat $big5Chars $length $offset) ''
        if ($length -ge 12) {
            $body = New-Repeat $big5Chars ($length - $hkscs.Length) $offset
            $insertAt = [int]([Math]::Floor($body.Length / 4) * 2)
            $bytes = [byte[]]($body[0..($insertAt - 1)] + $hkscs + $body[$insertAt..($body.Length - 1)])
            Measure-Input 'B' 'Big5+HKSCS' '950' ("{0}/{1}" -f $length, $offset) $bytes ''
        }
    }
}

#--------------------------------------------------------------------------
# X: テストデータに無いシングルバイト系言語
#   ベトナム語は .NET の cp1258 エンコーダーで往復しないため、バイト列自体が信用できない（参考値）
#--------------------------------------------------------------------------
$samples = @(
    @{ Lang = 'Turkish'; CodePages = @(1254, 28599); Text = @"
Türkçe bir metin. Pijamalı hasta yağız şoföre çabucak güvendi.
Öğretmen öğrencilere İstanbul'un tarihini anlattı.
Şu çiçekleri ağacın gölgesine dikmeliyiz, güneş çok güçlü.
Görüşmek üzere, iyi günler dilerim.
"@ }
    @{ Lang = 'Hebrew'; CodePages = @(1255, 28598); Text = @"
עברית היא שפה שמית. דג סקרן שט בים מאוכזב ולפתע מצא חברה.
הילדים הלכו לבית הספר בבוקר ולמדו מתמטיקה והיסטוריה.
ירושלים היא עיר עתיקה עם היסטוריה ארוכה ומרתקת.
תודה רבה ולהתראות בקרוב.
"@ }
    @{ Lang = 'Greek'; CodePages = @(1253, 28597); Text = @"
Ελληνικά. Ξεσκεπάζω την ψυχοφθόρα βδελυγμία.
Η Αθήνα είναι η πρωτεύουσα της Ελλάδας και έχει μεγάλη ιστορία.
Τα παιδιά πηγαίνουν στο σχολείο κάθε πρωί με τα πόδια.
Ευχαριστώ πολύ και καλή σας μέρα.
"@ }
    @{ Lang = 'Arabic'; CodePages = @(1256, 28596); Text = @"
اللغة العربية لغة جميلة. نص حكيم له سر قاطع وذو شأن عظيم مكتوب على ثوب أخضر.
ذهب الأطفال إلى المدرسة في الصباح وتعلموا الرياضيات والتاريخ.
القاهرة مدينة كبيرة ذات تاريخ طويل.
شكرا جزيلا وإلى اللقاء.
"@ }
    @{ Lang = 'Lithuanian'; CodePages = @(1257, 28603); Text = @"
Lietuvių kalba. Įlinkdama fechtuotojo špaga sublykčiojusi pragręžė apvalų arbūzą.
Vilnius yra Lietuvos sostinė ir didžiausias šalies miestas.
Vaikai kiekvieną rytą eina į mokyklą pėsčiomis.
Ačiū labai ir geros dienos.
"@ }
    @{ Lang = 'Latvian'; CodePages = @(1257, 28603); Text = @"
Latviešu valoda. Glāžšķūņa rūķīši dzērumā čiepj Baha koncertflīģeļu vākus.
Rīga ir Latvijas galvaspilsēta un lielākā pilsēta.
Bērni katru rītu iet uz skolu kājām.
Paldies un visu labu.
"@ }
    @{ Lang = 'Estonian'; CodePages = @(1257, 28605); Text = @"
Eesti keel. Põdur Zagrebi tšellomängija-följetonist Ciqo külmetas kehvas garaažis.
Tallinn on Eesti pealinn ja suurim linn.
Lapsed käivad igal hommikul jala koolis.
Aitäh ja head päeva.
"@ }
    @{ Lang = 'Vietnamese'; CodePages = @(1258); Text = @"
Tiếng Việt. Con cáo nâu nhanh nhẹn nhảy qua con chó lười.
Hà Nội là thủ đô của Việt Nam và có lịch sử lâu đời.
Trẻ em đi học mỗi sáng bằng xe đạp.
Cảm ơn rất nhiều và hẹn gặp lại.
"@ }
    @{ Lang = 'Czech'; CodePages = @(1250, 28592); Text = @"
Čeština. Příliš žluťoučký kůň úpěl ďábelské ódy.
Praha je hlavní město České republiky a má dlouhou historii.
Děti chodí každé ráno do školy pěšky.
Děkuji a hezký den.
"@ }
    @{ Lang = 'Hungarian'; CodePages = @(1250, 28592); Text = @"
Magyar nyelv. Árvíztűrő tükörfúrógép.
Budapest Magyarország fővárosa és legnagyobb városa.
A gyerekek minden reggel gyalog mennek az iskolába.
Köszönöm szépen és további szép napot.
"@ }
    @{ Lang = 'Romanian'; CodePages = @(1250, 28592); Text = @"
Limba română. Fiecare zi este o nouă şansă de a învăţa.
Bucureşti este capitala României şi cel mai mare oraş.
Copiii merg în fiecare dimineaţă la şcoală pe jos.
Mulţumesc frumos şi o zi bună.
"@ }
    @{ Lang = 'Ukrainian'; CodePages = @(1251, 21866); Text = @"
Українська мова. Чуєш їх, доцю, га? Кумедна ж ти, прощайся без ґольфів!
Київ є столицею України та найбільшим містом країни.
Діти щоранку ходять до школи пішки.
Дякую і гарного дня.
"@ }
    @{ Lang = 'Bulgarian'; CodePages = @(1251); Text = @"
Български език. Ах, чудна българска земьо, полюшвай цъфтящи жита.
София е столицата на България и най-големият град в страната.
Децата ходят на училище всяка сутрин пеша.
Благодаря и приятен ден.
"@ }
    @{ Lang = 'Russian'; CodePages = @(1251, 20866); Text = @"
Русский язык. Съешь ещё этих мягких французских булок, да выпей чаю.
В чащах юга жил бы цитрус? Да, но фальшивый экземпляр!
Широкая электрификация южных губерний даст мощный толчок.
Эх, чужак, общий съём цен шляп (юфть) вдрызг!
"@ }
    @{ Lang = 'Spanish'; CodePages = @(1252); Text = @"
Español. El pingüino Wenceslao hizo kilómetros bajo exhaustiva lluvia y frío.
Madrid es la capital de España y tiene una larga historia.
Los niños van a la escuela cada mañana a pie.
Muchas gracias y hasta pronto.
"@ }
    @{ Lang = 'Portuguese'; CodePages = @(1252); Text = @"
Português. À noite, vovô Kowalsky vê o ímã cair no pé do pinguim queixoso.
Lisboa é a capital de Portugal e tem uma longa história.
As crianças vão à escola todas as manhãs a pé.
Muito obrigado e até logo.
"@ }
    @{ Lang = 'Swedish'; CodePages = @(1252); Text = @"
Svenska. Flygande bäckasiner söka hwila på mjuka tuvor.
Stockholm är Sveriges huvudstad och största stad.
Barnen går till skolan varje morgon.
Tack så mycket och ha en trevlig dag.
"@ }
    @{ Lang = 'Icelandic'; CodePages = @(1252); Text = @"
Íslenska. Kæmi ný öxi hér, ykist þjófum nú bæði víl og ádrepa.
Reykjavík er höfuðborg Íslands og stærsta borg landsins.
Börnin ganga í skólann á hverjum morgni.
Takk kærlega og góðan dag.
"@ }
)

# .NET に符号化器が無い ISO-8859-16 は、バイトの対応を明示して組み立てる
$iso885916 = @{
    [char]0x0103 = 0xE3; [char]0x00E2 = 0xE2; [char]0x00EE = 0xEE; [char]0x0219 = 0xBA; [char]0x021B = 0xFE
    [char]0x0102 = 0xC3; [char]0x00C2 = 0xC2; [char]0x00CE = 0xCE; [char]0x0218 = 0xAA; [char]0x021A = 0xDE
}
$romanianIso885916Text = @"
Limba română este o limbă romanică. Fiecare zi este o nouă șansă de a învăța.
București este capitala României și cel mai mare oraș din țară.
Copiii merg în fiecare dimineață la școală pe jos.
Mulțumesc frumos și o zi bună.
"@

function Get-Variants {
    param([string] $Text)
    $normalized = ($Text -replace "`r?`n", "`r`n") + "`r`n"
    $lines = @($normalized -split "`r`n" | Where-Object { $_ -ne '' })
    $variants = [ordered]@{}
    for ($i = 0; $i -lt $lines.Count; $i++) { $variants['L' + ($i + 1)] = $lines[$i] + "`r`n" }
    $variants['full'] = $normalized
    $variants['x3'] = $normalized + $normalized + $normalized
    return $variants
}

foreach ($sample in $samples) {
    $variants = Get-Variants $sample.Text
    foreach ($cp in $sample.CodePages) {
        $encoding = [System.Text.Encoding]::GetEncoding($cp)
        foreach ($name in $variants.Keys) {
            Measure-Input 'X' $sample.Lang ([string]$cp) $name $encoding.GetBytes($variants[$name]) $variants[$name]
        }
    }
}

$variants = Get-Variants $romanianIso885916Text
foreach ($name in $variants.Keys) {
    $text = $variants[$name]
    $bytes = [byte[]]($text.ToCharArray() | ForEach-Object { if ([int]$_ -lt 0x80) { [byte][int]$_ } else { [byte]$iso885916[$_] } })
    Measure-Input 'X' 'Romanian' 'iso-8859-16' $name $bytes $text
}

$directory = Split-Path -Parent ([IO.Path]::GetFullPath($OutFile))
if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($OutFile), $script:rows, (New-Object System.Text.UTF8Encoding($false)))
'{0} 件を測定しました: {1}' -f ($script:rows.Count - 1), $OutFile
