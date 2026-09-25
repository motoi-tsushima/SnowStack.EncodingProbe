@{
    RootModule           = 'SnowStack.EncodingProbe.PowerShell.psm1'
    ModuleVersion        = '1.2.0'
    GUID                 = 'c807b12c-ca2a-4964-a3fe-b5f8823f78e8'
    Author               = 'motoi.tsushima'
    CompanyName          = 'motoi.tsushima'
    Copyright            = 'Copyright © 2026 motoi.tsushima'
    Description          = 'A PowerShell module for detecting the character encoding of text files. テキストファイルの文字エンコーディングを判定する PowerShell モジュールです。'

    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')

    CmdletsToExport      = @(
        'Resolve-Encoding',
        'Get-EncodingProbePlatformInfo',
        'Get-ProbedContent',
        'Set-ProbedContent',
        'Add-ProbedContent',
        'Out-ProbedFile',
        'Convert-ProbedContent',
        'ConvertTo-DotNetEncoding'
    )
    FunctionsToExport    = @()
    VariablesToExport    = @()
    AliasesToExport      = @()

    PrivateData = @{
        PSData = @{
            # Prerelease   = 'preview6'
            Tags         = @('encoding', 'chardet', 'charset', 'text', 'shift-jis', 'euc-jp', 'japanese', 'bom', 'utf8')
            LicenseUri   = 'https://github.com/motoi-tsushima/SnowStack.EncodingProbe/blob/master/LICENSE.txt'
            ProjectUri   = 'https://github.com/motoi-tsushima/SnowStack.EncodingProbe'
            ReleaseNotes = @'
1.2.0
コマンドを 2 つ追加し、文字エンコーディング判定を東アジア以外の言語に対応させました。

- Out-ProbedFile : オブジェクトを整形してファイルへ出力します。標準の Out-File のパラメーターを
  すべて持ち、文字エンコーディング・BOM・改行を統一語彙で指定できます。-Encoding を省略すると
  PowerShell 5.1 と 7.x のどちらでも utf8NoBOM で書き、-Append では追記先の文字エンコーディングを継承して
  整合性を検査します。
- Convert-ProbedContent : 既存のテキストファイルの文字エンコーディング・BOM・改行を変換します。
  不正なバイト列や表現できない文字があるファイルは変換せず、文字を失わないことを保証します。

既存コマンドの変更:
- Set-ProbedContent / Add-ProbedContent の -Force は、書き込み後に読み取り専用の属性を元に戻すように
  なりました（標準の Set-Content / Add-Content と同じ）。
- -LineBreak が決めるのは要素の後ろに付ける改行だけで、文字列の中の改行は置き換えないことを明文化しました。

判定の変更:
- 東アジア以外のカルチャーでは、Shift_JIS などの東アジアの旧マルチバイトの判定を行わなくなりました。
- 東アジアのカルチャーでも、欧米のシングルバイトのテキストを旧マルチバイトと誤判定しにくくなりました。
- UTF-8 の判定を厳密にしました。
- 香港・マカオのカルチャーに対応し、メッセージとヘルプは台湾と同じ繁体字の内容で提供します。

1.1.0
テキストの読み書きコマンドを追加しました。既存のコマンドと公開 API に変更はありません。

- Get-ProbedContent : 文字エンコーディングを判定してテキストファイルを読み込みます。
- Set-ProbedContent : 文字エンコーディング・BOM・改行コードを明示して書き込みます。
- Add-ProbedContent : 文字エンコーディングを保ったまま追記します。
- ConvertTo-DotNetEncoding : 各種の指定を System.Text.Encoding に変換します。

これらの -Encoding は統一語彙を受け付けます。同じ名前が PowerShell 5.1 と 7.x で同じ結果になり、
PowerShell 5.1 でも utf8NoBOM（BOM 無しの UTF-8）や shift_jis を名前で指定できます。
書き込みでは BOM 方針の定まらない裸の utf8 を受け付けません。utf8NoBOM または utf8BOM を指定してください。

Get-ProbedContent / Set-ProbedContent / Add-ProbedContent は、Resolve-Encoding と同じ
-Culture と -Strategy を受け取ります。日本語環境で韓国語や中国語のファイルを読むときは
-Culture ko-KR のように対象言語のカルチャーを指定してください。
欧米のテキストは -Strategy UtfUnknownOnly で読めます。

エラーメッセージは英語・日本語・韓国語・繁体字中国語・簡体字中国語に対応しています。
ヘルプ（Get-Help）も同じ 5 言語を同梱しています。

1.0.2
ライセンスリリース。Resolve-Encoding, Get-EncodingProbePlatformInfo コマンドレットを提供。
'@
        }
    }
}
