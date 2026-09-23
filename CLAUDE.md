# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要

文字エンコーディング推測ライブラリと、それを使う PowerShell モジュールの 2 プロジェクト構成。
コード内コメント・XML ドキュメント・コミットメッセージはすべて日本語で書かれている。既存のスタイルに合わせること。

- `SnowStack.EncodingProbe` … NuGet パッケージ（クラスライブラリ）
- `SnowStack.EncodingProbe.PowerShell` … バイナリモジュール。`Resolve-Encoding` /
  `Get-EncodingProbePlatformInfo` / `Get-ProbedContent` / `Set-ProbedContent` /
  `Add-ProbedContent` / `ConvertTo-DotNetEncoding` を提供する
- `tests/EncodingProbe.Tests` … コアライブラリの xUnit テスト（net10.0 / net48 の両方で動く）
- `tests/EncodingProbe.PowerShell.Tests` … コマンドレットの xUnit テスト（net10.0 のみ。`Microsoft.PowerShell.SDK` を参照）

ソリューションファイルは `.slnx` 形式（`SnowStack.EncodingProbe.slnx`）。

## ビルドとテスト

```bash
dotnet build SnowStack.EncodingProbe.slnx -c Debug

# 全テスト（両 TFM）
dotnet test SnowStack.EncodingProbe.slnx

# TFM を指定して実行（両方通すこと。詳細は「マルチターゲット」を参照）
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net48
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net10.0

# 単一テストの実行
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net10.0 \
  --filter "FullyQualifiedName~JapaneseEncodingTests"
dotnet test tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj -f net10.0 \
  --filter "FullyQualifiedName=EncodingProbe.Tests.DetectorTests.PS51EncodingNameTests.NoBom_Utf8_PSEncodingNameIsNull"

# NuGet パッケージ生成（GeneratePackageOnBuild は false なので明示的に pack する）
dotnet pack SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj -c Release
```

### 実 PowerShell ホストでの手動確認

ビルド済み DLL を直接 Import-Module する。PS 5.1（Desktop）は net48 出力、PS 7.x（Core）は net10.0 出力を読ませる。

```bash
pwsh -NoProfile -Command "Import-Module './SnowStack.EncodingProbe.PowerShell/bin/Debug/net10.0/SnowStack.EncodingProbe.PowerShell.dll'; Resolve-Encoding -Path <file> | Format-List *"
powershell.exe -NoProfile -Command "Import-Module './SnowStack.EncodingProbe.PowerShell/bin/Debug/net48/SnowStack.EncodingProbe.PowerShell.dll'; Resolve-Encoding -Path <file> | Format-List *"
```

Visual Studio 用には `Properties/launchSettings.json` に「.NET 4.8 テスト」「.NET 10 テスト」の 2 プロファイルがある。
`Configurations` に `PS5.1_Debug` / `PS7.x_Debug` が定義されているが、これらは VS のデバッグ対象切り替え用で、ビルド内容は Debug と変わらない。

## アーキテクチャ

### 判定パイプライン

`EncodingProbe`（静的ファサード）→ `EncodingDetector`（独自判定エンジン、約 2200 行）→ UTF.Unknown（フォールバック）という 3 層。

`EncodingProbe.Detect(byte[] | Stream | string)` は `EncodingDetectorOptions.Strategy` で振る舞いが決まる：

- `Combined`（既定）… まず独自判定。`CodePage < 0`（＝判定不能）なら UTF.Unknown に委譲する。
  独自判定が**旧マルチバイト**のコードページを返したときも UTF.Unknown に問い合わせ、突き合わせる（後述）
- `NativeOnly` … 独自判定のみ。突き合わせも行わない
- `UtfUnknownOnly` … UTF.Unknown のみ

分業の理由は README のとおり。独自判定は東アジア漢字文化圏のマルチバイト（Shift-JIS / EUC-JP / EUC-KR / CP949 / GB 系 / Big5 / EUC-TW / ISO-2022-*）を担当し、欧米のシングルバイトは UTF.Unknown が担当する。

`EncodingDetector.Detection(culture)` 内の判定順序は意味を持つ。順序を入れ替えると誤判定する：

1. 改行コード判定（`DetectLineBreak`）— 後段の曖昧解消にも使う
2. BOM（`ByteOrderMarkDetection`）— 一致したら即確定
3. ISO-2022 / ASCII
4. UTF-32 → UTF-16 → UTF-8（UTF-32 を UTF-16 より先に判定する）
5. **カルチャーゲート**（`GetEastAsianLegacyRegion()` → `ResolveEastAsianLegacyRegion()`）。東アジア漢字文化圏でなければここで打ち切る
6. カルチャーで分岐。簡体字中国語なら GB2312 → GBK → GB18030 の 3 段階判定。それ以外は EUC 系 → CPxxx 系

**1〜4 はカルチャーに関わらず必ず実行する。** UTF.Unknown は BOM 無しの UTF-16 / UTF-32 に対応していないため、
Unicode 系の判定を独自判定側から外すことはできない。

`Utf8_Detection` は RFC 3629 の整形式バイト列の表どおりに検証する（途中で途切れた多バイト文字・冗長な符号化・
サロゲート符号位置・U+10FFFF 超えをすべて規格外とする）。ここを緩くすると欧米のシングルバイトを UTF-8 と誤判定する。
`.NET` の厳格な UTF-8 デコーダーと判断が一致することを `Utf8StrictnessTests` が検証している。

`DetectionMode.Skippable` を渡すと BOM が無い時点で打ち切る（BOM の有無だけ知りたい呼び出し向け）。

### カルチャー依存の曖昧解消

バイト列だけでは区別できない組み合わせがあり、カルチャーと改行コードで決める。ここが本ライブラリの中核ロジックなので、変更時は該当言語のテストデータを必ず追加すること。

- EUC-JP と Shift-JIS の両方に該当 → 改行が CRLF なら Shift-JIS、LF なら EUC-JP、改行なしなら OS（Windows → Shift-JIS）
- EUC-KR と CP949 の両方に該当 → CP949
- EUC-TW と CP950 の両方に該当 → CP950(Big5)（香港カルチャーは EUC-TW を候補にしない）

カルチャーはグローバル状態ではなくパラメータで渡す（`Detection(culture)` / `EncodingDetectorOptions.Culture`）。`CultureInfo.CurrentCulture` へのフォールバックは 1 か所だけ。

**カルチャー名と判定対象の対応表は `EncodingDetector.ResolveEastAsianLegacyRegion(string)`（internal static）に集約している。**
`EastAsianLegacyRegion`（`None` / `Japanese` / `Korean` / `ChineseSimplified` / `ChineseTraditional` / `ChineseHongKong`）を返し、
`GetEucCodePageFromCulture()` と `CPxxx_Detection()` はこの結果で分岐する。表を増やさないこと。

- カルチャー名は `Internal/CultureNameSubtags` で言語・用字・地域のサブタグに分解してから引く。
  `CultureInfo` の親チェーンは使わない（net48 の NLS と net10.0 の ICU で名前・親が異なるため）。
  第三次修正で `MessageCatalog` の言語選択にも同じ規則を使う予定
- `zh` / `yue` は 用字サブタグ → 地域サブタグ → 言語の既定（`zh` は簡体字、`yue` は香港）の順で決める。
  繁体字のうち地域 `HK` / `MO` が `ChineseHongKong`
- 日本語・韓国語も言語サブタグの完全一致（`ja` / `ko`）で判定する。前方一致に戻さないこと。
  1.2.0 より前は前方一致だったため `kok`（コンカニ語）を Korean と判定していた。
  .NET の一覧で `ja` / `ko` で始まる別言語は `kok` だけだが、Windows は未登録の名前（`kos` 等）も受け付ける
- `ChineseHongKong` と `ChineseTraditional` の挙動差は EUC-TW を候補にしないことだけ。
  台湾 Big5 と香港 Big5 はバイト列から区別せず、どちらも `950 / big5` を返す（段階 2 は見送り）。
  HKSCS 固有字は私用領域に復号され、本ライブラリは介入しない（`docs/私用領域の扱い_方針草案.md`）

### 旧マルチバイト判定の横取りとクロスチェック（1.2.0）

`SJIS_Detection` / `CP949_Detection` / `CP936_Detection` / `CP950_Detection` / `EUCxx_Detection` は
**バイト構造の妥当性しか見ていない**。そのため欧米のシングルバイトのテキストがそのまま通ってしまう。
たとえば日本語カルチャーの実行環境でドイツ語の windows-1252 を読むと、
`FC DF`（`üß`）が Shift_JIS の外字領域の 2 バイト文字として成立し、Shift_JIS と誤判定する。

`EncodingProbe.NormalDetectEncoding`（＝`Combined`）は、独自判定が旧マルチバイトのコードページ
（`IsEastAsianLegacyMultiByteCodePage`）を返したときに UTF.Unknown にも判定させ、
UTF.Unknown がシングルバイト文字エンコーディングを返していればそちらを採用する。
本物の東アジアのテキストに対して UTF.Unknown が返すのはマルチバイトの符号化か判定不能なので、
東アジアの判定結果は変わらない。

**UTF.Unknown の信頼度に対する下限は、役割ごとに 2 つあり値が違う。**

- `UtfUnknownAdoptionThreshold`（0.5）… UTF.Unknown の結果を**答えとして採用する**下限。
  `ApplyUtfUnknownResult` が使う。`UtfUnknownOnly` と、独自判定が判定不能だったときの補完に効く
- `SingleByteOverrideThreshold`（0.55）… 独自判定が出した旧マルチバイトの答えを
  **シングルバイトで上書きする**下限。`ShouldPreferUtfUnknown` が使う

上書きの下限を高くしてあるのは、独自判定がすでに出した答えを覆すには、
答えとして採用するより強い根拠を求めるためである。
UTF.Unknown は短い漢字列や HKSCS 入りの Big5 に対して 0.5 前後でシングルバイトを返すので、
コードページの組み合わせだけで上書きを決めると正しい判定が覆る
（GBK の「这是一」6 バイトが tis-620 / 0.5104… で cp874 に化けていた）。
値は実測に基づく: 旧マルチバイトに対してシングルバイトを返したときの最大が 0.5105、
上書きが必要なシングルバイトのテキストの最小が 0.5695（ロシア語）。
**測定値は net48（UTF.Unknown 2.6.0）と net10.0（2.7.0）で一致する。**
`CrossCheckConfidenceTests` がこの境界を固定している。

**繁簡の系統クロスチェック（1.2.0 第二次修正）。** 独自判定が Big5 系（950）または GB 系（936 / 54936 / 20936）を返し、
UTF.Unknown が**反対の系統**を `ChineseFamilyOverrideThreshold`（**0.8 以上**、上の 2 つとは別の定数）で返したら、
`EncodingDetector.RedetectChineseLegacy` で反対の系統の独自判定を**カルチャーゲートを通らずに**やり直す。
UTF.Unknown のコードページはそのまま使わない（GB 系の番号は `GB_Detection` の規則で決める）。
成立しなければ元の結果を返す。系統表（`GetChineseLegacyFamily`）に日本語・韓国語のコードページを入れないこと
（EUC-JP の 20932 と UTF.Unknown の 51932 のような同一系統の食い違いを差し替えてしまう）。
短い入力（実測では 20 バイト程度まで、標本による）と HKSCS 入りの Big5 では UTF.Unknown が系統を言えないので差し替えは起きない。
zh-CN の Big5+HKSCS が 54936 のまま残るのは既知の限界。

`CP950_Detection` の後続バイトは `0x40–0x7E` / `0xA1–0xFE`（1.2.0 で `0x80–0xA0` を除外した）。

`IsSingleByteCodePage` は `Encoding.GetEncoding(cp).IsSingleByte` を**使わない**。
.NET Core では `CodePagesEncodingProvider` を登録していないと cp1251 などを解決できず、
ホスト側の登録状況で判定が変わってしまうため。マルチバイト側を表で除外している。

### マルチターゲット（net10.0 / net48）と PSEncodingName

両プロジェクトとも `net10.0;net48` をターゲットにする。net48 = Windows PowerShell 5.1（Desktop）向け、net10.0 = PowerShell 7.x（Core）向け。

`EncodingInformation.PSEncodingName` / `UsePSName` は **TFM ごとに意味が違う**。`EncodingDetector.cs` の `#if NETFRAMEWORK` 分岐で実装が分かれている：

- net10.0 ビルド … PS 6.2+ の登録済みフレンドリ名（`utf8BOM` 等）。無ければ WebName を入れ、`UsePSName = false`（`-Encoding` に直接渡せない）
- net48 ビルド … PS 5.1 の固定 `-Encoding` 列挙値（`Ascii` / `Unicode` / `UTF32` 等）に一致する場合のみその値。一致しなければ `null` かつ `UsePSName = false`

この差異は仕様であってバグではない。`PS51EncodingNameTests` が両 TFM でこのマッピングを検証している。**片方の TFM だけでテストを通しても意味がない。**

net48 では `PolySharp` により新しい言語機能（record 等）を使えるようにしている。

**UTF.Unknown の参照バージョンも TFM で分かれている**（net10.0 = 2.7.0、net48 = 2.6.0）。
2.7.0 の netstandard2.0 アセットが `System.Memory` に依存し、`app.config` を差し込めない
PowerShell 5.1 ホストで解決できないためで、意図した非対称である。**揃えないこと。**
理由は `docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md` 2.8 節にある。

### PowerShell モジュール層

- `EncodingProbeModuleInitializer`（`IModuleAssemblyInitializer`）が Import-Module 時に `CodePagesEncodingProvider` を登録する。.NET Core では CP932 等がこれ無しでは取れない。ホスト側の登録に依存しないこと
- コア側の `SnowStack.EncodingProbe.csproj` に `InternalsVisibleTo("SnowStack.EncodingProbe.PowerShell")` があり、`Internal/PlatformInfoResolver` 等を PowerShell 層だけが参照できる。公開 API を増やさずに内部実装を共有するための仕組み
- `EncodingProbePlatformInformation` はコアの `PlatformInformation`（OS / ランタイム / ロケール）に PowerShell ホスト情報を足したもの
- 配布用の `publish/SnowStack.EncodingProbe.PowerShell/` のうち、リポジトリで管理しているのは `.psd1` / `.psm1` / `deps.json` だけ。DLL と MAML ヘルプは `.gitignore` で除外されている（ビルド出力を手動でここへ配置する運用）。`.psm1` が `$PSEdition` を見て `core\` か `desktop\` の DLL を読み分ける

### 1.1.0 で追加した読み書きコマンド

`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `ConvertTo-DotNetEncoding` は
**統一語彙**（PS 5.1 と 7.x で同じ名前が同じ結果になるエンコーディング名の体系）の上に作られている。
仕様は `docs/EncodingProbe-1.1.0-仕様書.md`。

- `Internal/EncodingVocabulary` が語彙の解決を一手に担う。解決順序は意味を持つ
- `Internal/EncodingSpecTransformationAttribute`（`ArgumentTransformationAttribute` 派生）が
  **パラメータ束縛の段階**で `EncodingSpec` に変換・検証する。ファイルを開く前に失敗させることで、
  書きかけの破損ファイルを残さない。`EncodingSpec` は internal のまま公開しない
- 書き込み系は `Cmdlets/ProbedContentWriterCommandBase` を共有する。
  `Set-` と `Add-` の差は「ファイルの開き方」と「書き込む内容の検査」の 2 点だけ
- BOM を書き出すかどうかは `EncodingSpec.EmitBom` だけで決め、`ProbedFileWriter` が自分で書き出す。
  `Encoding.GetPreamble()` 任せにしない（解決経路によって出力が変わってしまうため）
- 文字エンコーディングの判定は**ファイル全体**を対象とする。先頭の一定量に制限すると、
  英数字が続いたあとにマルチバイト文字が現れるファイルを誤判定する
- `Add-ProbedContent` の整合性検査は**実際に書き出されるバイト列の比較**で行う。
  名前の組み合わせ表では判定しない（仕様書 6.1）
- メッセージは `Internal/MessageCatalog` が英語・日本語・韓国語・繁体字中国語・簡体字中国語で持つ。
  サテライトアセンブリではなく単一アセンブリ内の表。`Resolve-Encoding` の既存メッセージは英語のまま
- `-Culture` / `-Strategy` は共通の基底クラス `Cmdlets/ProbedContentCommandBase` にある。
  `Resolve-Encoding` と**同じ名前・同じ値**であり、判定方式の語彙表は
  `Cmdlets/ResolveEncodingOptions.TryParseStrategy` に集約している（表を二重に持たない）。
  基底クラスが `BeginProcessing` を持つため、派生側では**必ず `base.BeginProcessing()` を先に呼ぶ**
  （書き込み系は `-EncodingFrom` の判定を `BeginProcessing` で行うため）。
  判定オプションが届く先は `Internal/ProbedFileReader.Open` と
  `Internal/EncodingInheritance.FromFile` の 2 か所だけ。
  `ConvertTo-DotNetEncoding` は判定処理を呼ばないため対象外
- 実行環境依存の例外を利用者に見せない。不正なカルチャー名の検証では
  `CultureNotFoundException` を内部例外として持たせていない。
  この例外のメッセージは .NET Framework と .NET Core で文言が異なり、
  PS 5.1 と 7.x で見えるメッセージが変わってしまうため（PSCompat が検出した）

### MAML ヘルプ

`SnowStack.EncodingProbe.PowerShell/` の下の `en-US/` `ja-JP/` `ko-KR/` `zh-TW/` `zh-CN/` に
`SnowStack.EncodingProbe.PowerShell.dll-Help.xml` を置いている（csproj で出力へコピーする）。
対応言語はメッセージ（`MessageCatalog`）と同じ 5 言語。

`Get-Help` はアセンブリと同じ場所のカルチャー別フォルダーを、UI カルチャーの親を
たどりながら探す。フォルダー名は **Windows が報告する UI カルチャー名そのもの**にしてある。
`zh-Hant` / `zh-Hans` のような親カルチャー名を置くと `zh-HK`（香港）まで拾ってしまい、
「香港は後のバージョンで対応する」という方針に反するため、置いていない。
`zh-HK` `zh-SG` `ko` などは en-US にフォールバックする（これが期待どおりの挙動）。

`publish/` へ配置する際は `core\` と `desktop\` の下に 5 言語ぶん、計 10 か所へコピーする
（`Copy-Item -Recurse` でビルド出力ごと配ればよい）。

**5 言語の内容がずれないよう、1 つだけ直さないこと。**
`tests/EncodingProbe.PowerShell.Tests/CmdletTests/MamlHelpTests.cs` が、
本文（`maml:para` / `maml:title`）を伏せた骨格が 5 言語で完全に一致することを検証している。
要素の構成・属性・出現順・コード例まで一致を要求するため、
1 言語にだけパラメーターを足すとテストが落ちる。
5 言語ぶんを生成し直す場合は en-US を雛形にして本文だけ差し替えるとよい。

他言語のヘルプを手元で確認するときは、**`Import-Module` の前に** UI カルチャーを変える。
読み込んだ後に変えても切り替わらない。

```powershell
[System.Threading.Thread]::CurrentThread.CurrentUICulture =
    [System.Globalization.CultureInfo]::GetCultureInfo('ko-KR')
Import-Module <dll>
```

### テストデータ

`tests/EncodingProbe.Tests/TestData/<言語>/` に言語別・エンコーディング別のサンプルファイルがある（English / Japanese / Korean / Chinese_Simplified / Chinese_Traditional / Chinese_HongKong / German / French / Russian / Polish / Thai）。PowerShell テストプロジェクトは `Link` でこれを共有している。

東アジア以外の 5 言語（German / French / Russian / Polish / Thai）と Chinese_HongKong は 1.2.0 で追加したもので、
`tools/New-EncodingTestData.ps1` が生成する。内容を変えるときはこのスクリプトを直して再生成すること。
スクリプトは改行を CRLF に正規化して書き出す（スクリプト自体の改行コードに結果が左右されないように）。
`sample_big5hkscs.txt` の HKSCS 固有字は .NET のエンコーダーで作れないのでバイト列を明示して挟んでいる。

`PrivateUseAreaTests/PrivateUseAreaRoundTripTests` は私用領域方針の付録 A の往復検査で、件数まで固定している。
テストプロジェクトはコアの internal（カルチャーゲート等）を `InternalsVisibleTo` で参照できる。

**注意:** `.editorconfig` は `[*.txt]` に `charset = utf-8-bom` を指定している。TestData の .txt はまさにそれ以外のエンコーディングであることが試験の目的なので、エディタや整形ツールがこれらを書き換えないようにすること（`.gitattributes` で `tests/EncodingProbe.Tests/TestData/** -text` にしてある）。テストデータを新規作成するときはバイト列を明示して生成する。

### PowerShell 5.1 / 7.x の一致検証

`tests/PSCompat/Invoke-ProbedCompatTests.ps1` が同一のシナリオ集を両ホストで実行し、結果を突き合わせる。
本モジュールの存在意義そのものを検証しているため、読み書き系に手を入れたら必ず実行すること。

```bash
pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1
```

シナリオは `tests/PSCompat/ProbedCompatScenarios.ps1` に追加する。注意点:

- `.ps1` は **UTF-8 (BOM 付き)** で保存する。PS 5.1 は BOM 無しの `.ps1` を ANSI として読むため、
  日本語を含む行がパースエラーになる
- レポートにホスト固有の情報（バージョン、パス、PowerShell 自身のエラー文言）を含めない
- ホストによって差が出るのが正しい値（`Encoding.Default` など）は、値そのものではなく
  「ランタイム既定と一致するか」を記録して比較する
- `[scriptblock]::Create` に組み立てた文字列を渡すと AMSI にブロックされることがある。
  ループ変数を束縛したいだけなら `.GetNewClosure()` を使う

## バージョン更新時に触る場所

バージョン番号は 3 か所に分散している。上げるときはすべて揃える：

1. `SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj`（`Version` / `AssemblyVersion` / `FileVersion`）
2. `SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.csproj`（同上）
3. `publish/SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.psd1`（`ModuleVersion`、`PrivateData.PSData.ReleaseNotes`）

`CHANGELOG.md` にも追記すること。

新しいコマンドレットを追加したら、次も忘れずに行う:

- `.psd1` の `CmdletsToExport` に追加する
- MAML ヘルプ 5 言語（`en-US` / `ja-JP` / `ko-KR` / `zh-TW` / `zh-CN`）すべてに項目を追加する
  （`MamlHelpTests` が骨格の一致を要求するため、1 言語だけ足すとテストが落ちる）
- `tests/EncodingProbe.PowerShell.Tests/Helpers/ProbedCommandRunspaceFixture.cs` に登録する
  （登録しないとテストのランスペースから呼べない）
- `tests/PSCompat/ProbedCompatScenarios.ps1` にシナリオを追加する

## ライセンス上の注意

UTF.Unknown は **MIT ではなく MPL 1.1**（または GPL 2.0+ / LGPL 2.1+ とのトリプルライセンス）。過去に MIT と誤記して修正したコミットがある。サードパーティ表記は `THIRD-PARTY-NOTICES.txt` と `EncodingProbe.cs` の `License` 定数（`Resolve-Encoding -License` が返す文字列）の 2 か所にあり、内容を揃えること。バージョンは TFM で分かれるため、両方に `2.7.0 (net10.0 build) / 2.6.0 (net48 build)` の形で書く（`LICENSE.txt` は本プロジェクト自身の MIT ライセンス）。

## 1.1.0 の作業記録

1.1.0（`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` / `ConvertTo-DotNetEncoding` の追加）
は完了している。関連文書:

- `docs/EncodingProbe-1.1.0-仕様書.md` … 機能仕様。挙動を確認するときはまずここを見る
- `docs/EncodingProbe-1.1.0-ClaudeCode指示書.md` … 実装時の制約
- `docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md` … 決定事項とその根拠、踏んだ落とし穴。
  **仕様書に書かれていない判断の理由はここにある**
- `docs/EncodingProbe-1.1.0-課題_人間記述用.md`… Claude Code 実装後に、人間が確認して発見した課題を記述している。Claude Code 再起動時はこの課題を解消すること。
  起票済みの 3 件（`-Culture` / `-Strategy` の追加、ヘルプの 5 言語化）はすべて対応済み
- `docs/EncodingProbe-1.1.0-動作確認手順書.md` … 手元の PC で動作を確認する手順
- `docs/EncodingProbe-1.1.0-ブログ記事用資料.md` … 個人ブログの解説記事を書くための素材集。
  **原稿ではない。** 実機で採取した実行結果を載せてあるので、挙動を変えたら採り直すこと
- `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` … コアの判定エンジン側の未着手課題（3 件）

1.1.0 では次を変更していない（指示書 1 節の制約。今後も維持すること）:

- `Resolve-Encoding` / `Get-EncodingProbePlatformInfo` のパラメータと戻り値
- `EncodingInformation` 型（`DotNetEncoding` プロパティは「追加しない」と決定済み）
- コアの NuGet パッケージの公開 API

## 1.2.0 の作業記録

開発中。**まだリリースしていないのでバージョン番号は上げていない。**
作業は `feature/1.2.0-world-language-detection` ブランチで行う。

- `docs/EncodingProbe-1.2.0-課題_人間記述用.md` … 人間が確認して発見した課題。
  課題 1（東アジア以外の言語への対応）は対応済み。経緯と判断の理由は「Claude用記載欄」にある
- `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` … ISO-2022 系の未着手課題（3 件）
- `docs/EncodingProbe-1.2.0-依頼-第一次修正_クロスチェック信頼度.md` … 完了
- `docs/EncodingProbe-1.2.0-依頼-第二次修正_香港Big5段階1.md` … 完了。
  背景と実測は `docs/EncodingProbe-1.2.0-課題-香港Big5対応.md`、私用領域の扱いは `docs/私用領域の扱い_方針草案.md`
- 第三次修正（予定）… ヘルプ（MAML）と `MessageCatalog` の香港対応（zh-HK 版、zh-MO は同じ内容）。
  第二次修正では `MessageCatalog` と MAML ヘルプ、`MamlHelpTests` の期待値に触れていない
- `docs/EncodingProbe-課題-香港Big5段階3_HKSCS復号.md` … HKSCS の復号・符号化。
  **本製品では対応しない（方針）。** 私用領域の内容に干渉しないのが基本方針で、香港固有字の解釈は利用者に任せる。
  対処するとしても別製品・別機能で扱う。HKSCS の対応表や写像を本製品に持ち込まないこと
- `docs/TestReport_PS7.md` / `docs/TestReport_PS51.md` … 世界言語 83 ファイルの判定テスト結果。
  1.1.0 時点の測定値であり、課題 1 の対応は反映されていない。
  このレポートを生成する `tools/Invoke-EncodingProbeTest.ps1` とそのテストデータはリポジトリに入っていない

済んだ変更は CHANGELOG.md の「1.2.0（開発中）」の節にまとめてある。
