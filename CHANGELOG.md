# 変更履歴

このファイルは SnowStack.EncodingProbe（NuGet パッケージ）と
SnowStack.EncodingProbe.PowerShell（PowerShell モジュール）の変更をまとめたものです。

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

## 1.0.2

- ライセンスリリース。
- UTF.Unknown のライセンス表記を修正しました。MIT ライセンスと誤記していましたが、
  正しくは MPL 1.1（または GPL 2.0+ / LGPL 2.1+ とのトリプルライセンス）です

## 1.0.0

- 2026 年 7 月 14 日、正式版をリリースしました
- `Resolve-Encoding` / `Get-EncodingProbePlatformInfo` を提供します
