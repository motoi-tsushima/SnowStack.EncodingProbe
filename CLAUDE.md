# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要

文字エンコーディング推測ライブラリと、それを使う PowerShell モジュールの 2 プロジェクト構成。
コード内コメント・XML ドキュメント・コミットメッセージはすべて日本語で書かれている。既存のスタイルに合わせること。

- `SnowStack.EncodingProbe` … NuGet パッケージ（クラスライブラリ）
- `SnowStack.EncodingProbe.PowerShell` … バイナリモジュール（提供するコマンドは `.psd1` の `CmdletsToExport` を見ること）
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

1. 改行コード判定（`DetectLineBreak`）— 後段の曖昧解消にも使う。UTF-16 / UTF-32 に決まったら、その単位で数え直す（1.3.0 第五次。`RecountLineBreakByUnit`。
   バイト単位だと「上」U+4E0A = `0A 4E` などを改行と数える）。公開の `DetectLineBreak()` はバイト単位のまま変えないこと（公開 API）
2. BOM（`ByteOrderMarkDetection`）— 一致したら即確定
3. ISO-2022 / ASCII。ASCII と判定し、かつ `0x00` を含む入力だけは `NulPaddedUnicode_Detection` で、
   英数字だけの BOM 無し UTF-32 / UTF-16 であるかを厳密に調べる（1.3.0。後述）
4. UTF-32 → UTF-16 → UTF-8（UTF-32 を UTF-16 より先に判定する）
5. **カルチャーゲート**（`GetEastAsianLegacyRegion()` → `ResolveEastAsianLegacyRegion()`）。東アジア漢字文化圏でなければここで打ち切る
6. カルチャーで分岐。簡体字中国語なら GB2312 → GBK → GB18030 の 3 段階判定。それ以外は EUC 系 → CPxxx 系

**1〜4 はカルチャーに関わらず必ず実行する。** UTF.Unknown は BOM 無しの UTF-16 / UTF-32 に対応していないため、
Unicode 系の判定を独自判定側から外すことはできない。

`Utf8_Detection` は RFC 3629 の整形式バイト列の表どおりに検証する（途中で途切れた多バイト文字・冗長な符号化・
サロゲート符号位置・U+10FFFF 超えをすべて規格外とする）。ここを緩くすると欧米のシングルバイトを UTF-8 と誤判定する。
`.NET` の厳格な UTF-8 デコーダーと判断が一致することを `Utf8StrictnessTests` が検証している。

`DetectionMode.Skippable` を渡すと BOM が無い時点で打ち切る（BOM の有無だけ知りたい呼び出し向け）。

**`0x00` を含む ASCII の扱い（1.3.0、`docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`）。** 判定順序は変えていない。
3 で ASCII（ESC シーケンスが無く、すべてのバイトが `0x80` 未満）となった入力に `0x00` があれば、先頭から単位に区切り、
UTF-32LE → UTF-32BE → UTF-16LE → UTF-16BE の順に**すべての単位**が `<00 以外> 00`（UTF-16LE の場合）などの形に合うかを調べる。
成立すればそのコードページ（BOM 無し）、成立しなければ ASCII のまま（**UTF-8 の判定には進めない**）。

- 既存の `Utf16_Detection` / `Utf32_Detection` をこの分岐で使わないこと。すべてのバイトが `0x80` 未満という前提では、
  既存の UTF-16 の判定はほぼ常に成立し、`0x00` を含む ASCII（末尾の `00`、`00` で埋めた固定長レコード）を UTF-16 と判定する
  （調査 `docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md` 4.2 節。修正案 1 として測って採用しなかった）
- 既知の限界（追補 4 章）: 2 バイトとも `0x80` 未満になる文字（「上」U+4E0A = `0A 4E` など）や U+0000 を含む UTF-16 / UTF-32 は ASCII のまま、
  `41 00` のような数バイトの入力は形に合えば UTF-16 / UTF-32。`NulInputDetectionTests` の `KnownLimit_*` が固定している（正しい挙動ではない）。
  前者は `Import-` / `Export-ProbedCsv` の `-DetectionLineCount` の読み替え（1.3.0 仕様書 3.2）で救える。この読み替えを消さないこと

### カルチャー依存の曖昧解消

バイト列だけでは区別できない組み合わせがあり、カルチャーと改行コードで決める。ここが本ライブラリの中核ロジックなので、変更時は該当言語のテストデータを必ず追加すること。

- EUC-JP と Shift-JIS の両方に該当 → 改行が CRLF なら Shift-JIS、LF なら EUC-JP、改行なしなら OS（Windows → Shift-JIS）
- EUC-KR と CP949 の両方に該当 → CP949
- EUC-TW と CP950 の両方に該当 → CP950(Big5)（香港カルチャーは EUC-TW を候補にしない）
- **ただし、決めた候補で標本を厳密に（例外フォールバックで）復号できず、もう一方では復号できるなら、もう一方を選ぶ**（1.3.0 第六次。BUG-0031。`ExcludeUndecodableCandidate` / `CanDecodeStrictly`）。
  その TFM で作れない候補（51950）は外さない。net10.0 は `CodePagesEncodingProvider.Instance` から直接取り出す（グローバルに登録しないこと）

カルチャーはグローバル状態ではなくパラメータで渡す（`Detection(culture)` / `EncodingDetectorOptions.Culture`）。`CultureInfo.CurrentCulture` へのフォールバックは 1 か所だけ。

**カルチャー名と判定対象の対応表は `EncodingDetector.ResolveEastAsianLegacyRegion(string)`（internal static）に集約している。**
`EastAsianLegacyRegion`（`None` / `Japanese` / `Korean` / `ChineseSimplified` / `ChineseTraditional` / `ChineseHongKong`）を返し、
`GetEucCodePageFromCulture()` と `CPxxx_Detection()` はこの結果で分岐する。表を増やさないこと。

- **カルチャー名を受け付けるかどうかは、名前の形（BCP 47）で決める（`CultureNameSubtags.IsWellFormed`。1.3.0 第五次の後半）。** `CultureInfo` で作れるかどうかで決めないこと。
  NLS と ICU で受け付ける名前が両方向に違い（`C`・`root`・`art-lojban` は ICU だけ、`und-u-ca-gregory` は NLS だけ）、どちらかに合わせても一致しない。
  コアの `EncodingProbe.ValidateCulture` とモジュールの `ResolveEncodingOptions.ValidateCulture`（`Resolve-Encoding` を含む全コマンド）が同じ規則を使う。
  受け付けた名前はそのままコアに渡す（`EncodingInformation.Culture` に出るため、組み立て直さない）。受け付けた名前で `CultureInfo` を作らないこと（片方の .NET だけで例外になる）
- **言語の直後の拡張言語（英字 3 文字）は、それを言語として扱う**（`CultureNameSubtags.Parse`。RFC 5646 の `zh-yue` = `yue`）。`zh-CHT` / `zh-CHS` は用字として先に扱う。表は増やさない
- カルチャー名は `Internal/CultureNameSubtags` で言語・用字・地域のサブタグに分解してから引く。
  `CultureInfo` の親チェーンは使わない（net48 の NLS と net10.0 の ICU で名前・親が異なるため）。
  `MessageCatalog` の言語選択も同じ規則を使う（1.2.0 第三次修正。`MessageCatalog.ResolveLanguage`）
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
測定の条件・全表・短文での限界は `docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md` にある。

**UTF.Unknown の依存を上げたら、`dotnet build` のあと `pwsh -NoProfile -File tools/Invoke-CrossCheckMeasurement.ps1` で再測定し、
表 B の最大 < 0.55 < 表 A の最小 が保たれているかを確かめること。** 余裕は下に 0.04、上に 0.02 しかない。

- 多くの言語では 0.55 ではなく 0.5 が効く。東アジアの判定がすべて拒否するバイト（cp1251 の `я` = 0xFF など）を含む入力は
  独自判定が判定不能になり、上書き経路に入らないため
- 約 90 バイト以下の短文では、閾値に関係なく誤判定が残る（UTF.Unknown の限界）。
  `KnownLimitTests` が現在の挙動を固定しているが、**それは正しい挙動ではない**。落ちたら改善か悪化かを調べる
- UTF.Unknown が .NET に無いエンコーディング（iso-8859-10 など）を返した場合は `CodePage = -1` になり、上書き経路の対象にならない

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

- net10.0 ビルド … PS 6.2+ の登録済みフレンドリ名（`utf8BOM` 等）。無ければ WebName を入れ、`UsePSName = false`（`-Encoding` に直接渡せない）。
  WebName は独自判定の表（`EncodingDetector.EncodingName`）から引くため、表に無いコードページ（UTF.Unknown の 1252 / 28591 など）は **null**
  （1.2.0 より前はここに `I do not know.` が入っていた。20932 / 950 などを null にしないこと。利用者の判断済み）
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

### PowerShell モジュールのコマンドと MAML ヘルプ

1.1.0〜1.3.0 で追加したコマンド（読み書き・Out-ProbedFile / Convert-ProbedContent・CSV と -DetectionLineCount）の決定事項と、MAML ヘルプの規則は
`SnowStack.EncodingProbe.PowerShell/CLAUDE.md` にある（そのフォルダーのファイルを読むと読み込まれる）。
**モジュールのコード・テスト（`tests/EncodingProbe.PowerShell.Tests`）・PSCompat・ヘルプに手を入れる前に、必ずそのファイルを読むこと。**

### テストデータ

`tests/EncodingProbe.Tests/TestData/<言語>/` に言語別・エンコーディング別のサンプルファイルがある（English / Japanese / Korean / Chinese_Simplified / Chinese_Traditional / Chinese_HongKong / German / French / Russian / Polish / Thai / Spanish / Estonian / Ukrainian / Romanian / Icelandic）。PowerShell テストプロジェクトは `Link` でこれを共有している。

東アジア以外の言語（German / French / Russian / Polish / Thai / Spanish / Estonian / Ukrainian / Romanian / Icelandic）と
Chinese_HongKong、繁体字・簡体字の長めのサンプル（`*_long.txt`）は 1.2.0 で追加したもので、
`tools/New-EncodingTestData.ps1` が生成する。内容を変えるときはこのスクリプトを直して再生成すること。
スクリプトは改行を CRLF に正規化して書き出す（スクリプト自体の改行コードに結果が左右されないように）。
`sample_big5hkscs.txt` の HKSCS 固有字は .NET のエンコーダーで作れないのでバイト列を明示して挟んでいる。

各ファイルが何を固定しているかは `docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md` 11 章の表にある。
`*_short_*.txt` とウクライナ語は **UTF.Unknown の限界で誤判定・判定不能になる入力**で、`KnownLimitTests` が現在の挙動を固定している（正しい挙動ではない）。
東アジアの既存データ（4〜15 バイト）は短すぎて UTF.Unknown が系統を判定できないため、
繁簡の系統クロスチェックは `*_long.txt`（196 バイト）でだけ発動する。

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

## バージョン更新・コマンドレットの追加

手順はスキル `.claude/skills/release-checklist/SKILL.md` にある。バージョン番号を上げるとき・新しいコマンドレットを追加するときは、必ずそれに従うこと
（バージョンは 3 か所に分散している。モジュールだけのリリースではクラスライブラリの版を上げない）。

## ライセンス上の注意

UTF.Unknown は **MIT ではなく MPL 1.1**（または GPL 2.0+ / LGPL 2.1+ とのトリプルライセンス）。過去に MIT と誤記して修正したコミットがある。サードパーティ表記は `THIRD-PARTY-NOTICES.txt` と `EncodingProbe.cs` の `License` 定数（`Resolve-Encoding -License` が返す文字列）の 2 か所にあり、内容を揃えること。バージョンは TFM で分かれるため、両方に `2.7.0 (net10.0 build) / 2.6.0 (net48 build)` の形で書く（`LICENSE.txt` は本プロジェクト自身の MIT ライセンス）。

**PowerShell 由来のコード（1.3.0）。** `SnowStack.EncodingProbe.PowerShell/Internal/Csv/` の 4 ファイルは、
`PowerShell/PowerShell` の `CsvCommands.cs`（コミット `db1d0a56cbe41adf05d0bad90be5190a6655404f`、Copyright (c) Microsoft Corporation、MIT License）からの移植である。
MIT License の条件（著作権表示と許諾表示）を満たすため、次の 4 か所に表示している。どれかを直したら他も確かめること。

1. 移植したすべてのファイルの冒頭（1.3.0 仕様書 付録 A.1 の文面）
2. `THIRD-PARTY-NOTICES.txt` の PowerShell の節（ライセンス本文は移植元の `LICENSE.txt` の原文のまま。`*AS IS*` のアスタリスクも変えない）
3. `LicenseAndHelp.License`（`Resolve-Encoding -License` の出力）
4. 配布するモジュールのフォルダーに `LICENSE.txt` と `THIRD-PARTY-NOTICES.txt` を同梱する（リリース作業）

このコードはモジュールにだけ含まれ、クラスライブラリには含まれない。そのため `EncodingProbe.License` 定数（クラスライブラリ）には書かない。
`Internal/Csv/` には移植したコードだけを置く（内部 API の置き換えなど、本プロジェクトで書いたコードは `Internal/CsvPropertyViews.cs` のように外に置く）。

## 守ること（作業記録から）

各版の作業記録（現在の状態・リリースで残っている作業・未着手の課題・関連文書）は `docs/EncodingProbe-作業状況.md` にある。作業を始める前に読むこと。

- `Resolve-Encoding` / `Get-EncodingProbePlatformInfo` の既存のパラメータと戻り値、`EncodingInformation` 型（`DotNetEncoding` は追加しない）、コアの NuGet パッケージの公開 API を変えない（1.3.0 の `-DetectionLineCount` は利用者が決めた例外）
- HKSCS の対応表や写像を本製品に持ち込まない（私用領域に干渉しない方針）
- 依頼文・実測依頼の文書（`docs/EncodingProbe-1.3.0-依頼-*.md` など）はコミットしない（利用者の指示）
- テスト担当のフォルダー（`C:\_test\EncodingProbeBboxTest`）は読むだけにする。テスト担当にまだ伝えていない件を、テスト担当の報告やフォルダーに書かない
- 調査・実験用ブランチ（`investigate/*`）はどこにもマージしない
- 手動確認の課題は `docs/EncodingProbe-<版>-課題_人間記述用.md` に利用者が書く。起動したらまずその有無を確かめる
- 依頼を受けたら、まず調査して計画を示し、利用者の承認を受けてから実装する。終わったら止まって報告する
- プッシュとリリースはしない（利用者が行う）。publish\ で触れてよいのは .psd1 の ReleaseNotes の文面だけ
- Windows PowerShell 5.1 のバイナリは逆コンパイルしない
- 変えた挙動は CHANGELOG の「挙動の変更」に書く
