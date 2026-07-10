@{
    RootModule           = 'SnowStack.EncodingProbe.PowerShell.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = 'c807b12c-ca2a-4964-a3fe-b5f8823f78e8'
    Author               = 'motoi.tsushima'
    CompanyName          = 'motoi.tsushima'
    Copyright            = 'Copyright © 2026 motoi.tsushima'
    Description          = 'A PowerShell module for detecting the character encoding of text files. テキストファイルの文字エンコーディングを判定する PowerShell モジュールです。'

    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')

    CmdletsToExport      = @('Resolve-Encoding', 'Get-EncodingProbePlatformInfo')
    FunctionsToExport    = @()
    VariablesToExport    = @()
    AliasesToExport      = @()

    PrivateData = @{
        PSData = @{
            Prerelease   = 'preview6'
            Tags         = @('encoding', 'chardet', 'charset', 'text', 'shift-jis', 'euc-jp', 'japanese')
            LicenseUri   = 'https://github.com/motoi-tsushima/SnowStack.EncodingProbe/blob/master/LICENSE.txt'
            ProjectUri   = 'https://github.com/motoi-tsushima/SnowStack.EncodingProbe'
            ReleaseNotes = '初回プレビュー。Resolve-Encoding コマンドレットを提供。'
        }
    }
}
