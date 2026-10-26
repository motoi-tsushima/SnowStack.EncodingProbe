# SnowStack.EncodingProbe

このソリューションには以下の二つのプロジェクトが含まれています。

SnowStack.EncodingProbe : NuGetパッケージ

SnowStack.EncodingProbe.PowerShell : PowerShellコマンドレット



SnowStack.EncodingProbe パッケージは、文字エンコーディングの推測を行うクラスライブラリです。

SnowStack.EncodingProbe.PowerShellコマンドレットは、Resolve-Encoding というPowerShellコマンドを格納しています。Resolve-Encoding は EncodingProbeパッケージを使用して、文字エンコーディングの推測を行うコマンドです。

実行環境情報は、Get-EncodingProbePlatformInfo コマンドによって取得できます。

EncodingProbe の主な機能は以下の様になります。

- 文字エンコーディングの推測

- BOMの有無の確認

- 改行コードの種類の確認

- コマンドが認識しているカルチャー情報の表示

- 実行環境情報（OS種別 / .NETランタイム / ロケール / PowerShellホスト）の取得

  

SnowStack.EncodingProbe パッケージは、内部で UTF.Unknown を使用しています。

EncodingProbe の独自文字エンコーディング推測処理で、英語・日本語・韓国語・繁体字中国語・簡体字中国語の推測を行い、それらの推測でわからなかった場合は、 UTF.Unknown に文字エンコーディング推測を任せます。

UTF.Unknown は欧米などのシングルバイト文字エンコーディングの推測には優れていますが、東アジア漢字文化圏の旧マルチバイト文字エンコーディングの推測では、やや推測信頼性に劣る欠点があり、東アジア漢字文化圏の文字エンコーディングの推測処理だけを、EncodingProbe の独自文字エンコーディング推測処理で補っています。

両者の文字エンコーディングの推測処理を組み合わせることにより、世界中の文字エンコーディングの推測処理を可能にしています。

### シングルバイト系の判定の限界

シングルバイト系の文字エンコーディング（windows-1252、ISO-8859 など）は、バイト列の構造からは判別できず、文章の統計で推定しています。
そのため、判定の成否は長さよりも内容に左右されます。文字の一覧、記号・数字の多い行、その言語で通常使わない文字を含むテキストでは、
数百バイトあっても判定できない（`CodePage = -1`）ことがあります。

その場合は、コードページを明示してください。PowerShell モジュールでは、読み書きのコマンドの `-Encoding`
（`Convert-ProbedContent` では `-SourceEncoding`）で指定します。`Resolve-Encoding` には明示する手段がありません。



## version 1.3.0 の変更点

### BOM 無しの UTF-16 / UTF-32 の判定の修正（クラスライブラリ・PowerShell モジュール共通）

クラスライブラリ（NuGet パッケージ）を 1.3.0 とし、判定処理の不具合を 2 件（この節と次の節）直し、カルチャー名の検証を改めました（下の「カルチャー名の検証」）。公開 API は変えていません。

BOM 無しの UTF-16 / UTF-32 で英数字だけのテキストが `us-ascii` と判定され、読み込むと各文字の間に `U+0000` が混じっていました。
1.3.0 では、`0x00` を含み、すべてのバイトが `0x80` 未満の入力のうち、**すべての単位**が UTF-16 / UTF-32 の英数字の形
（UTF-16LE なら `00` 以外のバイトの後に `00`）になっているものを UTF-16 / UTF-32 と判定します。
`0x00` を含む ASCII のテキスト（末尾に `00` が付いたもの、`00` で埋めた固定長レコードなど）は、従来どおり `us-ascii` です。

既知の限界（直していないもの）:

- 英数字の中に、2 バイトとも `0x80` 未満になる文字（「上」U+4E0A = UTF-16LE で `0A 4E`、「字」U+5B57、「あ」U+3042、「、」U+3001 など）が混じる BOM 無しの UTF-16 / UTF-32 は、従来どおり `us-ascii` と判定されます。
  PowerShell モジュールの `Import-ProbedCsv` / `Export-ProbedCsv` では、`-DetectionLineCount` を指定すると正しく読み書きできます
- `41 00` のような数バイトの入力は、「BOM 無しの UTF-16LE の `A`」とも「ASCII の `A` と `00`」とも読め、区別できません。UTF-16 / UTF-32 の形に合えば UTF-16 / UTF-32 と判定します
- U+0000 の文字を含む BOM 無しの UTF-16 / UTF-32 は、`us-ascii` と判定されます

`0x80` 以上のバイトになる文字（ほとんどの日本語の文字など）を 1 文字でも含むファイルは、これらの限界に当たりません。

### 改行コードの判定の修正（クラスライブラリ・PowerShell モジュール共通）

UTF-16 / UTF-32 と判定したファイルの改行コード（`LineBreak`）を、1 文字分の単位（UTF-16 は 2 バイト、UTF-32 は 4 バイト）で数えるようにしました。
これまでは 1 バイトずつ数えていたため、「上」「《」「不」のように、文字の一部のバイトが改行のバイトと同じになる文字があると、
改行が混在している（`LfAndCrAndCrLf` など）と報告し、改行が CR のファイルに LF を追記していました。

### 書き込みで文字を失わないようにした（PowerShell モジュール。挙動の変更）

`Set-ProbedContent`・`Add-ProbedContent`・`Out-ProbedFile`・`Export-ProbedCsv` は、書き込み先の文字エンコーディングで表現できない文字を
`?` や似た文字（`é` → `e` など）に置き換えて書かず、**エラー（`UnrepresentableCharacter`）にしてファイルを元のままにします**。
途中まで書いていた場合も元に戻します。これまでは、たとえば Shift_JIS のファイルにハングルを追記すると、エラーも警告も無く `??` を書いていました。
表現できない文字を置き換えて書く方法はありません（文字列を先に編集してください）。

また、書き込み先・追記先・`-EncodingFrom` の参照先から**継承した文字エンコーディングが `us-ascii` の場合は、BOM 無しの UTF-8 とみなして書きます。**
英数字だけで始まるログや CSV（`us-ascii` と判定されます）に日本語を追記すると、これまでは `?` になっていましたが、UTF-8 で書きます。
英数字だけを書く場合のバイト列は変わりません。

### `-DetectionLineCount` の改善と既知の限界（PowerShell モジュール）

`-DetectionLineCount` で先頭 N 行を切り出すときの、BOM の無いファイルの改行の形（1 バイト・UTF-16・UTF-32）の決め方を改めました。
`00` を含む ASCII の CSV（`00` で埋めた固定長レコードなど）を UTF-16 と誤って読み、追記でファイルを壊すことがありましたが、そうならなくなりました。
また、先頭 N 行が英数字だけの ISO-2022-JP / KR / CN のファイルは、UTF-8 として読まずに `DetectionVerificationFailed` で止めます。

次のファイルは、`-DetectionLineCount` を指定すると、改行の形を誤るか決められないことがあります。`Import-ProbedCsv` / `Export-ProbedCsv` は、
多くの場合ファイル全体の検証で止まりますが、確実ではありません。心当たりがあれば、`-DetectionLineCount` を外すか、`-Encoding` で文字エンコーディングを指定してください。

- BOM 無しの UTF-16 で、U+0D00（マラヤーラム文字の記号）や U+0A00 が、文字の 1 割以上を占めるもの（逆のバイト順と誤ることがあります。ファイル全体で判定しても誤ります）
- BOM 無しの UTF-16 で、先頭 64 KiB に英数字も改行も無いもの（日本語だけの 1 行が長いもの。バイト順を誤ることがあります）
- BOM 無しの UTF-16 で、ひらがなだけでできているもの（1 バイトの文字エンコーディングと誤ります）
- 各行に `00` が 1 バイトずつあり、本文が Shift_JIS などの日本語の文字エンコーディングのファイル（UTF-16 と誤ることがあります）
- U+0000 の文字を含む BOM 無しの UTF-16 / UTF-32（1 バイトの文字エンコーディングと誤ります）

なお、`00` を含む 64 KiB 以内の 1 バイトの文字エンコーディングのファイルは、`-DetectionLineCount` を指定しても、ファイル全体で判定します。

### パスの束縛・同じファイルの判定・リンクの扱い（PowerShell モジュール。挙動の変更）

テスト担当のブラックボックステストで見つかった、パスとファイルの扱いの不具合を直しました。

- **`Get-ChildItem` などから渡したファイルの名前に `[ ]` があっても、そのファイルそのものを読み書きします。**
  別名 `FullName` を `-Path` から `-LiteralPath` に移しました（`Get-ProbedContent`・`Import-ProbedCsv`・`Set-ProbedContent`・`Add-ProbedContent`・`Convert-ProbedContent`）。
  これまでは `a[1].csv` を流すと `FullName` がワイルドカードとして展開され、`a1.csv` を読み書きしていました。文字列をパイプラインで渡した場合は、これまでどおりワイルドカードです
- **`Set-ProbedContent` / `Add-ProbedContent -Path` のワイルドカードがどのファイルにも一致しないときは、非終了エラー `ItemNotFound` にします**（標準の `Set-Content` / `Add-Content` と同じ）。
  これまでは何も書かずにエラーも出しませんでした
- **読み取り中のファイルへの書き込み（`SamePathRoundTrip`）を、パスの文字列ではなくファイルの実体で検出します。**
  `\\?\`・UNC の管理共有・`\\.\`・subst・シンボリックリンク・ジャンクション・ハードリンクで書き方を変えると検出をすり抜け、読み込み中のファイルが黙って切り詰められていました
  （`Add-ProbedContent` は終わらずにファイルが増え続けていました）。`Convert-ProbedContent -Destination` が変換元のフォルダーかどうかも、同じく実体で比べます
- **1 回の `Set-ProbedContent` で同じファイルを 2 回指定しても、1 回書いたのと同じ結果になります**（2 回目が追記になっていました）
- **追記の整合性検査（`EncodingChangeOnAppend`）で止まった場合も、その呼び出しで書いた分を取り消して、ファイルを追記の前に戻します**
- **シンボリックリンクに上書きすると、リンクの先の実体を書き換え、リンクはそのまま残します。ハードリンクは切りません**（`Convert-ProbedContent` を含む）。
  これまでは一時ファイルで置き換えていたため、リンクがただのファイルに置き換わり、ハードリンクが切れていました。
  ハードリンクと、フォルダーにファイルを作る権限が無い場合は、元のファイルを一時フォルダーに写してから直接書きます（大きなファイルでは写す時間がかかります）
- `a.txt::$DATA`（主ストリームの名前）は `a.txt` として扱います。Windows PowerShell 5.1 では生の例外で止まっていました。代替データストリーム（`a.txt:alt`）は、両ホストでエラー `AlternateDataStreamNotSupported` です
- `Resolve-Encoding` も、解釈できない `-Strategy` / `-Culture` を、ほかのコマンドと同じ ID（`InvalidStrategy` / `InvalidCulture`）の終了エラーにします

### カルチャー名の検証（クラスライブラリ・PowerShell モジュール共通。挙動の変更）

`-Culture`（クラスライブラリでは `EncodingDetectorOptions.Culture`）に受け付けるカルチャー名を、.NET の `CultureInfo` で作れるかどうかではなく、**名前の形（BCP 47）で決める**ようにしました。
.NET Framework（Windows PowerShell 5.1）と .NET（PowerShell 7.x）で受け付ける名前が違い、`-Culture C` が 5.1 ではエラー、7.x では判定に進んでいました。
`C`・`root`・`POSIX` は両方で受け付けません。`art-lojban`・`zh-yue`・`und-u-ca-gregory` のように片方だけが受け付けていた名前は、両方で受け付けます。
言語の直後の拡張言語は、RFC 5646 のとおりそれを言語として扱います（`zh-yue` は広東語として、香港の扱い）。
クラスライブラリでは、`ArgumentException` になる名前の集合が変わります（公開 API の型は変えていません）。

### 読み書きの細部の修正（クラスライブラリ・PowerShell モジュール。挙動の変更）

テスト担当のブラックボックステストで見つかった不具合を直しました（詳細は [CHANGELOG.md](CHANGELOG.md) と 1.3.0 仕様書 18.4・25 章）。

- **`ansi` / `oem` は、Windows ではシステムのロケール（「Unicode 対応ではないプログラムの言語」）の ANSI / OEM コードページです**（標準の 7.x の `-Encoding ansi` / `oem`、5.1 の `Default` / `Oem` と同じ）。
  これまでは「地域」の形式（スレッドのカルチャー）の値で、日本語の Windows で形式を「英語（米国）」にしていると `ansi` が 1252 になっていました。
  `Get-EncodingProbePlatformInfo` の `Locale` も同じ値です。システムの ANSI が 65001（UTF-8 のベータ機能）の場合は、BOM 無しの UTF-8 として扱います。`-Encoding 0` はエラーにします
- **BOM が 2 個続くファイルは、先頭の 1 個だけを取り除きます**（2 個目の U+FEFF は文字として残します。7.x の `Get-Content` / `Import-Csv` と同じ）。
  `-Encoding utf8BOM` などを指定すると、2 個とも取り除いていました（1.1.0 から）
- **Windows PowerShell 5.1 でも、途中で切れたファイルの末尾（長さが奇数の UTF-16 の最後の 1 バイトなど）を、7.x と同じく置き換えの文字（U+FFFD など）として読みます。** 5.1 では黙って捨てていました
- **JIS（ISO-2022-JP）のまま終わっているファイルに追記するときは、先に ASCII に戻すエスケープシーケンス（`ESC ( B`）を書きます。** 追記した行が 2 バイト文字として読まれて消えていました
- **CSV の列名の大文字小文字の比べ方を、5.1 でも 7.x と同じにしました**（`ǅ` / `ǆ`、語末のシグマ `ς` / `Σ` など）
- 短い EUC-JP のファイルを、日本語の環境で Shift_JIS と判定しなくなりました（Shift_JIS として読めないバイト列は、Shift_JIS を選びません）
- `Set-ProbedContent` / `Add-ProbedContent` の `-Value` の配列の `$null` は、標準と同じく飛ばします（空行を書いていました）
- `Convert-ProbedContent` は、`UTF7Encoding` のインスタンスでも UTF-7 で書きません（`utf7` と同じエラー）
- **同じパイプラインで、書き込み中のファイルを後から読もうとした場合も `SamePathRoundTrip` にし、書き込みを取り消します**（`Get-ProbedContent a.txt, z.txt | Out-ProbedFile z.txt` など。z.txt は元のままです）
- **ほかのプログラムが書き込み用に開いているファイル（書き込み中のログなど）も、判定・変換のために読めます。** 読めない・大きすぎる・メモリが足りない場合は ID の付いたエラーで、`Resolve-Encoding` は次のファイルに進みます（追記は、今までどおりほかのプログラムが書き込み中の間は書けません）
- 書き込んだファイルには Archive 属性が付きます。隠し・システム属性のファイルにも `-Force` なしで書け、代替データストリームを保ちます（標準の `Out-File` / `Export-Csv` は書けず、ストリームを消します）
- 置き換えに使う一時ファイルの名前は `.~probed<8 桁>.probedtmp` です（名前の長いファイルにも書けます。残った一時ファイルの名前からは、どのファイルのものかは分かりません）
- `Out-ProbedFile` は、`$PSDefaultParameterValues['Out-String:Width']` などの影響を受けません（標準の `Out-File` と同じ）

#### Linux / macOS での `ansi` / `oem`

Linux / macOS には ANSI / OEM コードページの考え方が無いため、**現在のカルチャー**（環境変数 `LC_ALL`・`LANG` などから決まる）について .NET が持つ値を使います（挙動は 1.2.0 までと同じです）。
Linux（Ubuntu 24.04、.NET 10.0.14、PowerShell 7.6.9）で実測した値は次のとおりです。

| `LANG`（`LC_ALL` なし） | `ansi` | `oem` |
|---|---|---|
| 未設定・`C`・`POSIX`・`C.UTF-8`（不変カルチャー） | 1252 | 437 |
| `en_US.UTF-8` | 1252 | 437 |
| `ja_JP.UTF-8` | 932 | 932 |
| `de_DE.UTF-8` | 1252 | 850 |
| `ko_KR.UTF-8` / `zh_CN.UTF-8` / `zh_TW.UTF-8` | 949 / 936 / 950 | 949 / 936 / 950 |

`LC_ALL` を設定すると、`LANG` より優先されます（空の文字列を設定した `LC_ALL` も優先され、不変カルチャーになります）。
標準の PowerShell 7.x の `-Encoding ansi` は同じ値ですが、**標準の `-Encoding oem` は UTF-8 で、本モジュールの `oem` とは違います。**
macOS は実測していません（.NET のソースからの推定では、Linux と同じく現在のカルチャーの値になります）。

### CSV の読み書きコマンド（PowerShell モジュール）

PowerShell モジュールに CSV の読み書きコマンドを追加しました。

| コマンド | 役割 |
|---|---|
| `Import-ProbedCsv` | 文字エンコーディングを判定して CSV ファイルを読み込む（`Import-Csv` の代わり） |
| `Export-ProbedCsv` | 文字エンコーディング・BOM・改行を指定して CSV ファイルに書き出す（`Export-Csv` の代わり） |

CSV の解析と文字列化は PowerShell 7.x の `Import-Csv` / `Export-Csv` から移植した処理で行うため、
**Windows PowerShell 5.1 でも PowerShell 7.x と同じ結果（書き出しはバイト単位まで同じファイル）になります。**
ただし、日付・数値の書式は現在のカルチャーとホストの .NET（5.1 は NLS、7.x は ICU）で決まるため、ko-KR・zh-TW・zh-HK・ar-SA などのカルチャーでは両ホストで違うことがあります（標準の `Export-Csv` / `ConvertTo-Csv` も同じです）。
5.1 の標準の `Import-Csv` は CR だけの改行の CSV を黙って 0 件で返し、`Export-Csv` は既定で日本語を `???` にしてしまいますが、
これらのコマンドではそうなりません。

```powershell
# 文字エンコーディングを判定して読み込む（Shift_JIS・EUC-JP・UTF-8・UTF-16 など）
Import-ProbedCsv .\data.csv

# フォルダー内の CSV をまとめて読む（PowerShell 5.1 でも動く）
Get-ChildItem *.csv | Import-ProbedCsv

# BOM 無しの UTF-8 で書き出す（PowerShell 5.1 でも同じバイト列になる）
Get-Process | Select-Object Name, Id | Export-ProbedCsv .\processes.csv

# Shift_JIS の CSV に、Shift_JIS のまま追記する（最後の行に改行が無くても行がつながらない）
$rows | Export-ProbedCsv .\sjis.csv -Append

# 巨大なファイルを先頭 1000 行で判定する（使う前にファイル全体を検証する）
Import-ProbedCsv .\huge.csv -DetectionLineCount 1000
```

`Resolve-Encoding` にも、先頭の N 行だけで判定する `-DetectionLineCount` を追加しました。

CSV の読み書き処理には、PowerShell（Copyright (c) Microsoft Corporation、MIT License）のコードの一部を改変して含めています。
詳細は [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) と [CHANGELOG.md](CHANGELOG.md) を参照してください。



## version 1.2.0 の変更点

### 東アジア以外の言語への対応（クラスライブラリ・PowerShell モジュール共通）

- **東アジア以外のカルチャーでは、旧マルチバイト（Shift_JIS / EUC / GB / Big5 など）の判定を行わず、UTF.Unknown に任せます。**
- **東アジアのカルチャーでも、欧米のシングルバイトのテキストを誤判定しなくなりました。**
  たとえば日本語環境で windows-1252 のドイツ語を読むと、従来は Shift_JIS と判定していました。
  既定の判定方式（`Combined`）では、独自判定が旧マルチバイトを返したときに UTF.Unknown の結果と突き合わせ、
  UTF.Unknown が十分な信頼度でシングルバイトと判定していればそちらを採用します
- **UTF-8 の判定を RFC 3629 に厳密化しました。** 欧米のシングルバイトを UTF-8 と誤判定しなくなりました
- **繁体字と簡体字の取り違えを直しました。** 台湾・香港の環境で GBK を Big5 と、大陸の環境で Big5 を GB18030 と判定することがありました
- **香港・マカオ・広東語のカルチャーに対応しました。** `zh-Hant-HK` のような用字付きの名前や `zh_HK` も解釈します。
  香港・マカオのメッセージとヘルプは、台湾と同じ繁体字中国語です
- ルーマニア語（ISO-8859-16）などを判定すると `NullReferenceException` が発生する不具合を直しました（1.1.0 から存在）
- PowerShell 7.x で `PSEncodingName` に `I do not know.` が入る不具合を直しました（null になります）

公開 API と、日本語・韓国語・中国語のテキストの判定結果は変わっていません。

### 追加したコマンド（PowerShell モジュール）

| コマンド | 役割 |
|---|---|
| `Out-ProbedFile` | オブジェクトを整形し、文字エンコーディング・BOM・改行を指定してファイルに書き出す（`Out-File` の代わり） |
| `Convert-ProbedContent` | 既存のテキストファイルの文字エンコーディング・BOM・改行を変換する |

```powershell
# BOM 無しの UTF-8 で書き出す（Out-File と同じ使い方。PowerShell 5.1 でも同じバイト列になる）
Get-Process | Out-ProbedFile .\processes.txt

# 追記先の文字エンコーディングを保ったまま追記する
Get-Date | Out-ProbedFile .\log.txt -Append

# 変換元の文字エンコーディングを判定し、BOM 無しの UTF-8、改行 LF に変換する
Convert-ProbedContent .\*.txt -Encoding utf8NoBOM -LineBreak Lf

# BOM だけを取り除く
Convert-ProbedContent .\a.txt -Bom Remove
```

`Convert-ProbedContent` は、変換元に不正なバイト列があるファイルや、変換先で表現できない文字を含むファイルを変換しません（文字を失いません）。
`-WhatIf` / `-Confirm` / `-PassThru` に対応しています。

このほか、`Set-ProbedContent` / `Add-ProbedContent` の `-Force` で外した読み取り専用属性を、書き込み後に元に戻すようにしました。
詳細は [CHANGELOG.md](CHANGELOG.md) を参照してください。



## version 1.1.0 で追加したコマンド（PowerShell モジュール）

PowerShell モジュールに、テキストファイルの読み書きコマンドを追加しました。既存のコマンドと公開 API に変更はありません。

| コマンド | 役割 |
|---|---|
| `Get-ProbedContent` | 文字エンコーディングを判定してテキストファイルを読み込む |
| `Set-ProbedContent` | 文字エンコーディング・BOM・改行コードを明示して書き込む |
| `Add-ProbedContent` | 文字エンコーディングを保ったまま追記する |
| `ConvertTo-DotNetEncoding` | 各種の指定を `System.Text.Encoding` に変換する |

これらの `-Encoding` は、PowerShell 5.1 と 7.x で共通の名前（統一語彙）を受け付けます。同じ名前が同じ結果になるため、**PowerShell 5.1 でも BOM 無しの UTF-8 や Shift_JIS を名前で指定できます**。どちらも 5.1 の標準コマンドではできないことです。

```powershell
# 判定して読み込む（BOM は常に読み飛ばす）
Get-ProbedContent .\shift-jis.txt

# BOM 無しの UTF-8 で書き込む。PowerShell 5.1 でも同じ結果になる
Set-ProbedContent .\out.txt -Value $lines -Encoding utf8NoBOM

# 元のファイルの文字エンコーディング・BOM・改行コードを保ったまま書き戻す
$text = Get-ProbedContent .\a.txt -Raw
$text -replace 'foo', 'bar' | Set-ProbedContent .\a.txt -EncodingFrom .\a.txt -NoNewline

# 文字エンコーディングを保ったまま追記する
Add-ProbedContent .\log.txt -Value $line

# 日本語環境から韓国語のファイルを読む（カルチャーを指定しないと EUC-JP と誤判定される）
Get-ProbedContent .\korean.txt -Culture ko-KR

# 欧米のテキストは UTF.Unknown の判定に任せる
Get-ProbedContent .\german.txt -Strategy UtfUnknownOnly
```

`-Encoding` には、統一語彙名のほかに WebName（`shift_jis` など）、数値コードページ（`932` など）、`System.Text.Encoding` インスタンス、`Resolve-Encoding` の戻り値をそのまま渡せます。

使ううえで知っておく必要のある点がいくつかあります。

- 書き込みでは、BOM 方針の定まらない裸の `utf8` を受け付けません。`utf8NoBOM` または `utf8BOM` を指定してください
- `Add-ProbedContent` では BOM の指定が常に無視されます。また、追記できるかどうかは実際に書き出されるバイト列で判定します
- `-Encoding` の入力形式によって改行コードの決まり方が変わります

`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` は、`Resolve-Encoding` と同じ `-Culture` と `-Strategy` を受け取ります。バイト列だけでは区別できない組み合わせ（EUC-KR と CP949、EUC-JP と Shift-JIS など）はカルチャーで曖昧解消するため、**日本語環境で韓国語や中国語のファイルを扱うときは `-Culture` に対象言語のカルチャーを指定してください**。`-EncodingFrom` の参照ファイルや、`-Encoding` を省略したときの継承にも効きます。

いずれも各コマンドの `Get-Help <コマンド名> -Full` に記載しています。ヘルプは英語・日本語・韓国語・繁体字中国語・簡体字中国語の 5 言語を同梱しています。詳細は [CHANGELOG.md](CHANGELOG.md) を参照してください。

2026年7月14日に正式版 1.0.0 をリリースしました。

NuGet.org より SnowStack.EncodingProbe を公開しました。

2026年9月30日に 1.2.0 をリリースしました（クラスライブラリ・PowerShell モジュールとも）。

以下の記事で使い方の解説を行っています。

[SnowStack.EncodingProbe NuGet Package 解説](https://snow-stack.net/encodingprobe_guide/)

また、PowerShell コマンドレットの解説は同ブログの以下の記事で解説しています。

[SnowStack.EncodingProbe.PowerShell 解説](https://snow-stack.net/encodingprobe_powershell_guide/)



解説記事は、現時点では、やや説明不足ですが、後日詳細な解説記事を書く予定です。

