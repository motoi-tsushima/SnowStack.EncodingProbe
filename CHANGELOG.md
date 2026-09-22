# 変更履歴

このファイルは SnowStack.EncodingProbe（NuGet パッケージ）と
SnowStack.EncodingProbe.PowerShell（PowerShell モジュール）の変更をまとめたものです。

## 1.2.0（開発中）

文字エンコーディング判定を、東アジア以外の言語に対応させました。
**リリース前の作業中の変更です。** バージョン番号はまだ上げていません。

### 判定を変えた点

- **東アジア以外のカルチャーでは、旧マルチバイトの判定を行わなくなりました。**
  独自判定が担当するのは Shift_JIS / EUC / GB / Big5 など東アジア漢字文化圏のマルチバイトだけです。
  カルチャーがそれ以外の言語圏のときは、これらの判定を実行せず UTF.Unknown に委ねます。
  カルチャー名と判定対象の対応表は `EncodingDetector.GetEastAsianLegacyRegion()` に集約しました
- **東アジアのカルチャーであっても、欧米のシングルバイトのテキストを誤判定しなくなりました。**
  旧マルチバイトの判定はバイト構造の妥当性しか見ていないため、たとえば日本語カルチャーの実行環境で
  windows-1252 のドイツ語を読むと、`FC DF`（`üß`）が Shift_JIS の外字領域の 2 バイト文字として
  成立してしまい Shift_JIS と誤判定していました。
  既定の判定方式（`Combined`）では、独自判定が旧マルチバイトを返したときに UTF.Unknown の結果と
  突き合わせ、UTF.Unknown がシングルバイト文字エンコーディングと判定していればそちらを採用します
- **UTF-8 の判定を RFC 3629 の整形式バイト列に厳密化しました。**
  後続バイトが足りないまま ASCII に戻る形（windows-1252 の `Français` の `E7 61 69` など）、
  ファイル終端で途切れた多バイト文字、冗長な符号化、サロゲート符号位置、
  U+10FFFF を超える符号位置を、いずれも UTF-8 ではないと判定します。
  従来はこれらを UTF-8 として受け入れていました

### 課題 1 の修正（クロスチェックの信頼度下限）

- **UTF.Unknown の信頼度が低いときに、正しい旧マルチバイトの判定が覆らなくなりました。**
  上記の突き合わせは、コードページの組み合わせ（独自判定＝東アジア旧マルチバイト、
  UTF.Unknown＝シングルバイト）だけで上書きを決めており、信頼度の条件を持っていませんでした。
  UTF.Unknown は短い漢字列や HKSCS 入りの Big5 に対して 0.5 前後の信頼度でシングルバイトを返すため、
  0.5 をわずかに超えただけで正しい判定が覆り、文字化けしていました。
  たとえば簡体字中国語のカルチャーで GBK の「这是一」（6 バイト）を読むと、
  UTF.Unknown が tis-620 を信頼度 0.5104… で返すため cp874（タイ語）と判定していました
- **信頼度の下限を役割で分けました。**
  UTF.Unknown の結果を答えとして採用する下限（0.5 超）は従来どおりです。
  独自判定がすでに出した答えをシングルバイトで覆すときは、これより高い下限（**0.55 超**）を要求します。
  独自判定の答えを覆すには、答えとして採用するより強い根拠を求める、という考え方です

### 変えていない点

- BOM・ISO-2022・ASCII・UTF-32・UTF-16・UTF-8 の判定は、**カルチャーに関わらず**実行します。
  UTF.Unknown は BOM 無しの UTF-16 / UTF-32 に対応していないため、
  Unicode 系の判定を落とすわけにはいきません
- `-Strategy NativeOnly` は独自判定だけを行います。上記の突き合わせも行いません
- 日本語・韓国語・繁体字中国語・簡体字中国語のカルチャーにおける、
  これらの言語のテキストの判定結果は変わりません
- 公開 API に変更はありません

### 内部の整理

- `EncodingProbe.DetectUtfUnknown` の 3 つのオーバーロードで重複していた処理を
  `ApplyUtfUnknownResult` にまとめました
- `EncodingProbe.Detect(Stream)` が、ストリームを一度だけ読んでバイト配列にしてから
  両方の判定に渡すようになりました。従来は独自判定が読み切ったストリームを
  そのまま UTF.Unknown にも渡していたため、UTF.Unknown 側が空のストリームを見ていました。
  PowerShell モジュールはバイト配列とファイルパスのオーバーロードしか使っていないため、
  影響を受けるのはクラスライブラリを直接使ってストリームを渡していた場合だけです

### テスト

- `tests/EncodingProbe.Tests/TestData/` に German / French / Russian / Polish / Thai を追加しました。
  生成は `tools/New-EncodingTestData.ps1` で行います
- `WorldLanguageTests` が、これらのファイルを 10 のカルチャーで判定して結果が変わらないことを検証します
- `Utf8StrictnessTests` が、UTF-8 判定と .NET の厳格なデコーダーの判断が一致することを検証します
- `CrossCheckConfidenceTests` が、短い簡体字・繁体字と HKSCS 固有字を含む Big5 が
  シングルバイトに覆されないことを検証します。
  テストデータは cp950 / cp936 を解決できない実行環境でも動くよう、
  また HKSCS 固有字は .NET のエンコーダーで作れないため、バイト列を明示して組み立てています
- `tests/PSCompat/ProbedCompatScenarios.ps1` に「世界言語の判定」の節を追加しました

## 1.1.0

PowerShell モジュールにテキストの読み書きコマンドを追加しました。
**既存のコマンドと公開 API に変更はありません。** 追加のみのリリースです。

### 追加したコマンド

| コマンド | 役割 |
|---|---|
| `Get-ProbedContent` | 文字エンコーディングを判定してテキストファイルを読み込む |
| `Set-ProbedContent` | 文字エンコーディング・BOM・改行コードを明示して書き込む |
| `Add-ProbedContent` | 文字エンコーディングを保ったまま追記する |
| `ConvertTo-DotNetEncoding` | 各種の指定を `System.Text.Encoding` に変換する |

### 統一語彙

追加した 4 コマンドの `-Encoding` は、PowerShell 5.1 と 7.x で共通の名前
（統一語彙）を受け付けます。同じ名前が同じ結果になります。

- `Auto` / `utf8NoBOM` / `utf8BOM` / `unicodeNoBOM` / `unicodeBOM` /
  `bigendianunicodeNoBOM` / `bigendianunicodeBOM` / `utf32NoBOM` / `utf32BOM` /
  `bigendianutf32NoBOM` / `bigendianutf32BOM` / `ascii` / `ansi` / `oem` / `utf7`（読み取り専用）
- `.NET` が知っている WebName（`shift_jis`、`euc-jp`、`iso-2022-jp`、`big5`、`gb18030` など）
- 数値コードページ（`932`、`65001` など）
- `System.Text.Encoding` インスタンス
- `Resolve-Encoding` が返す `EncodingInformation` オブジェクト

これにより、PowerShell 5.1 でも **BOM 無しの UTF-8** や **Shift_JIS** を名前で指定できます。
どちらも 5.1 の標準コマンドではできないことです。

### 主な仕様

- 読み取りでは、指定した語彙にかかわらずファイル先頭の BOM を常に読み飛ばします。
  `U+FEFF` が 1 行目に混入することはありません
- 文字エンコーディングの判定はファイル全体を対象とします。
  先頭が英数字だけで後方にだけ日本語があるソースファイルも正しく判定します
- 書き込みでは、BOM 方針の定まらない裸の `utf8`（および `utf-8`、`65001`）を受け付けません。
  `utf8NoBOM` または `utf8BOM` を指定してください。この失敗はパラメータ束縛の段階で起きるため、
  ファイルは一切変更されません
- `Set-ProbedContent` / `Add-ProbedContent` で `-Encoding` を省略すると、
  書き込み先・追記先の既存ファイルから文字エンコーディング・BOM・改行コードを継承します。
  読み取り元から継承したい場合は `-EncodingFrom` を使います
- `-Encoding` に `EncodingInformation` を渡した場合のみ、改行コードも継承されます。
  語彙名を渡した場合は改行の情報が無いため、`-LineBreak` 省略時は OS 既定の改行になります
- `Add-ProbedContent` では BOM の指定が常に無視されます（新規作成の場合も含む）。
  ファイルの途中に BOM を書き込むことは正しくないためです
- `Add-ProbedContent` は、追記できるかどうかを**実際に書き出されるバイト列**で判定します。
  UTF-8 のファイルへ `-Encoding ascii` で ASCII だけを追記することは許可され、
  同じファイルへ `-Encoding shift_jis` で日本語を追記することは拒否されます。
  意図的に変更する場合は `-AllowEncodingChange` を指定します（`-Force` では回避できません）
- 同一のファイルを 1 つのパイプラインで読み書きすると、読み終える前にファイルが
  切り詰められるため、検出してエラーにします。`-Raw` で読んだ場合は書き戻せます
- 判定はできたものの、実行環境がそのコードページを提供していない場合
  （ISO-2022-TW の 50229 など。`.NET` は net10.0 / net48 のどちらでも提供していません）は、
  エラーID `CodePageNotAvailable` の非終了エラーとして報告し、`-Encoding` による明示指定を案内します

### 判定オプション

`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` に、
`Resolve-Encoding` と同じ `-Culture` と `-Strategy` を追加しました。

- `-Culture` … 判定に用いるカルチャー名。バイト列だけでは区別できない組み合わせ
  （EUC-KR と CP949、EUC-JP と Shift-JIS など）をカルチャーで曖昧解消します。
  日本語環境で韓国語や中国語のファイルを読むときは、対象言語のカルチャーを指定してください
- `-Strategy` … `Combined`（既定）/ `NativeOnly` / `UtfUnknownOnly`。
  独自判定は東アジアのマルチバイト、UTF.Unknown は欧米のシングルバイトを担当するため、
  独自判定が誤る欧米のテキストは `UtfUnknownOnly` で読めます

`-EncodingFrom` の参照ファイルと、`-Encoding` 省略時の継承（書き込み先・追記先の判定）にも効きます。
解釈できない値を指定した場合は、ファイルを開く前にエラーになります。

`ConvertTo-DotNetEncoding` にはこれらのパラメーターはありません。
ファイルを引数に取らず、判定処理を呼び出さないためです。

### その他

- 追加したコマンドのエラーメッセージを、英語・日本語・韓国語・繁体字中国語・簡体字中国語に
  対応させました。`CurrentUICulture` で選択され、未対応の言語は英語になります
- `Get-Help` 用のヘルプ（MAML）を英語（en-US）・日本語（ja-JP）・韓国語（ko-KR）・
  繁体字中国語（zh-TW）・簡体字中国語（zh-CN）の 5 言語で同梱しました。
  未対応のカルチャー（`zh-HK` など）は英語になります
- `-LineBreak` には `Cr`（旧 Macintosh 形式）も指定できます。
  `Resolve-Encoding` が `Cr` を返しうるため、判定しうる状態はすべて書き戻せるようにしています

### 依存パッケージ

UTF.Unknown の参照バージョンを、**ターゲットフレームワークごとに分けました**。

| ターゲット | UTF.Unknown |
|---|---|
| net10.0（PowerShell 7.x） | 2.7.0 |
| net48（Windows PowerShell 5.1） | 2.6.0（据え置き） |

2.7.0 の `netstandard2.0` 向けアセットは `System.Memory` に依存します。
`System.Memory` は .NET Framework では参照アセンブリと実行時アセンブリのバージョンが
食い違い、通常は `app.config` のバインディングリダイレクトで解決しますが、
**バイナリモジュールを `Import-Module` する PowerShell 5.1 ホストには
`app.config` を差し込めません**。net48 側は 2.7.0 で追加された API を使っておらず、
判定結果も 2.6.0 と変わらないため、据え置いています。

判定結果に差が無いことは、両ターゲットのテストと
PowerShell 5.1 / 7.x の一致検証（223 シナリオ）で確認しています。

## 1.0.2

- ライセンスリリース。
- UTF.Unknown のライセンス表記を修正しました。MIT ライセンスと誤記していましたが、
  正しくは MPL 1.1（または GPL 2.0+ / LGPL 2.1+ とのトリプルライセンス）です

## 1.0.0

- 2026 年 7 月 14 日、正式版をリリースしました
- `Resolve-Encoding` / `Get-EncodingProbePlatformInfo` を提供します
