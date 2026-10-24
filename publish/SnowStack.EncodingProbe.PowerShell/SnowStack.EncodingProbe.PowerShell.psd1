@{
    RootModule           = 'SnowStack.EncodingProbe.PowerShell.psm1'
    ModuleVersion        = '1.3.0'
    GUID                 = 'c807b12c-ca2a-4964-a3fe-b5f8823f78e8'
    Author               = 'motoi.tsushima'
    CompanyName          = 'motoi.tsushima'
    Copyright            = 'Copyright (c) 2026 Motoi Tsushima'
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
        'ConvertTo-DotNetEncoding',
        'Import-ProbedCsv',
        'Export-ProbedCsv'
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
1.3.0
CSV の読み書きコマンドを 2 つ追加し、Resolve-Encoding で先頭の行だけを判定できるようにしました。
同梱のクラスライブラリも 1.3.0 です（判定処理の不具合を 2 件修正し、カルチャー名の検証を改めました）。

- Import-ProbedCsv : 文字エンコーディングを判定して CSV ファイルを読み込みます。解析は PowerShell 7.x の
  Import-Csv から移植した処理で行うため、PowerShell 5.1 でも 7.x と同じ結果になります（CR だけの改行の
  CSV も読め、W3C 拡張ログ形式にも対応します）。ワイルドカードで複数のファイルを読め、
  Get-ChildItem | Import-ProbedCsv も 5.1 で動きます。
- Export-ProbedCsv : 文字エンコーディング・BOM・改行を指定して CSV ファイルに書き出します。
  文字列化は PowerShell 7.x の Export-Csv から移植した処理で行うため、5.1 と 7.x でバイト単位まで同じ
  ファイルになります（日付・数値の書式は、カルチャーとホストの .NET によって違うことがあります）。-Encoding を省略すると 5.1 でも utf8NoBOM で書きます。-Append では追記先の
  文字エンコーディングと改行を継承し、最後の行に改行が無ければ補ってから追記します。
  -UseQuotes / -QuoteFields / -NoHeader は 5.1 でも使えます。
- -DetectionLineCount : Resolve-Encoding / Import-ProbedCsv / Export-ProbedCsv で、先頭の N 行だけで
  文字エンコーディングを判定します。Import-ProbedCsv / Export-ProbedCsv では、使う前にファイル全体を
  厳密に検証し、判定を誤っていれば処理せずに止めます。
- 不具合の修正（クラスライブラリ）: BOM 無しの UTF-16 / UTF-32 で英数字だけのファイルが us-ascii と
  判定され、U+0000 が混じって読まれていた問題を直しました。2 バイトとも 0x80 未満になる文字（「上」など）を
  英数字に混ぜたものは従来どおり us-ascii と判定されます（Import-ProbedCsv / Export-ProbedCsv では
  -DetectionLineCount を指定すると正しく読めます）。
- 不具合の修正（クラスライブラリ）: UTF-16 / UTF-32 のファイルの改行コード（LineBreak）を、1 文字分の単位で
  数えるようにしました。「上」などの文字のバイトを改行と数え、改行が混在していると報告していました。
- 挙動の変更: 書き込みの 4 コマンド（Set-ProbedContent / Add-ProbedContent / Out-ProbedFile /
  Export-ProbedCsv）は、書き込み先の文字エンコーディングで表現できない文字を '?' や似た文字に置き換えず、
  エラー UnrepresentableCharacter にしてファイルを元のままにします。
- 挙動の変更: 継承した文字エンコーディングが us-ascii の場合は、BOM 無しの UTF-8 とみなして書きます
  （英数字だけのログや CSV にも日本語を追記できます）。
- 挙動の変更: 別名 FullName を -Path から -LiteralPath に移しました。Get-ChildItem から渡したファイルの
  名前に [ ] があっても、そのファイルそのものを読み書きします。Set-ProbedContent / Add-ProbedContent -Path の
  ワイルドカードがどのファイルにも一致しないときは、エラー ItemNotFound にします。
- 挙動の変更: 読み取り中のファイルへの書き込み（SamePathRoundTrip）と Convert-ProbedContent -Destination の
  変換元との比較を、パスの文字列ではなくファイルの実体で行います（\\?\・UNC・subst・リンクの書き方でも検出）。
  1 回の Set-ProbedContent で同じファイルを 2 回指定しても、1 回書いたのと同じ結果になります。
- 挙動の変更: 追記の整合性検査（EncodingChangeOnAppend）で止まった場合も、ファイルを追記の前に戻します。
- 挙動の変更: シンボリックリンクに上書きするとリンクの先の実体を書き換え、リンクを残します。ハードリンクは
  切りません（Convert-ProbedContent を含む）。リンク切れへの Set-ProbedContent はエラー LinkTargetNotFound です。
- 挙動の変更: 上書きは同じフォルダーの一時ファイルに書いてから置き換えます。ただし書く内容と元のファイルが
  どちらも 1 MiB 以内なら、元のファイルの中身をメモリに写してから直接書き、失敗したら書き戻します
  （小さなファイルを速く書くため。書いている最中にプロセスが強制終了されると、書きかけで残ることがあります）。
- 挙動の変更: a.txt::$DATA は a.txt として扱い、代替データストリームはエラー AlternateDataStreamNotSupported
  にします。Resolve-Encoding の -Strategy / -Culture の誤りは、ほかのコマンドと同じ ID のエラーにします。
  -Culture に受け付ける名前は、名前の形（BCP 47）で決めます（C・root は両ホストで InvalidCulture）。

CSV の読み書き処理には、PowerShell（Copyright (c) Microsoft Corporation、MIT License）のコードの
一部を改変して含めています。詳細は THIRD-PARTY-NOTICES.txt を参照してください。

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
- Set-ProbedContent / Add-ProbedContent は、-WhatIf のときも読み取り専用などのエラーを報告するようになりました。

不具合の修正:
- PowerShell 7.x で、フレンドリ名の無い文字エンコーディング（windows-1252 など）の PSEncodingName に
  "I do not know." が入っていたのを null に直しました（1.0.0 から存在した不具合）。

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
