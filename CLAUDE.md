# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要

文字エンコーディング推測ライブラリと、それを使う PowerShell モジュールの 2 プロジェクト構成。
コード内コメント・XML ドキュメント・コミットメッセージはすべて日本語で書かれている。既存のスタイルに合わせること。

- `SnowStack.EncodingProbe` … NuGet パッケージ（クラスライブラリ）
- `SnowStack.EncodingProbe.PowerShell` … バイナリモジュール。`Resolve-Encoding` /
  `Get-EncodingProbePlatformInfo` / `Get-ProbedContent` / `Set-ProbedContent` /
  `Add-ProbedContent` / `ConvertTo-DotNetEncoding` / `Out-ProbedFile` / `Convert-ProbedContent` /
  `Import-ProbedCsv` / `Export-ProbedCsv` を提供する
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
- **書き込みは例外フォールバックで符号化し、表現できない文字は `UnrepresentableCharacter` にしてファイルを元に戻す（1.3.0 第五次）。**
  `ProbedFileWriter` はコードページから `BuildStrictEncoding` で組み立て直す（利用者が渡した Encoding のフォールバックは使わない）。
  上書きは `Internal/SafeFileOverwrite`（`Convert-ProbedContent` も共用）。ふつうは同じフォルダーの一時ファイル `.<名前>.<8 桁>.probedtmp` に書いて `File.Replace` で置き換える。
  シンボリックリンク・ジャンクションは実体（`GetFinalPathNameByHandle`）のフォルダーで置き換え、ハードリンクと一時ファイルを作れないフォルダーは、一時フォルダーに写してから直接書く（1.3.0 第五次の後半）。
  **書く内容と元のファイルがどちらも 1 MiB（`SafeFileOverwrite.DirectWriteThreshold`）以内なら、メモリに写してから直接書く（`MemoryThenDirect`。第五次の仕上げ、実装記録 10.16 節）。**
  書く内容は `SafeFileOverwrite.Write` でためる（`.Stream` に直接書かないこと）。既存のファイルは完了の時点まで開かず、まだ無いファイルは開いた時点で作る（完了まで `TemporaryFileRegistry` に載せる）。
  途中で 1 MiB を超えたら今までの書き方に切り替える。ためる量はプロセス全体で 64 MiB まで（`MemoryBudget`）。`File.Replace` の費用で小さなファイルが約 10 倍遅くなっていたため（10.15 節）。
  追記は開いたストリームの長さを覚えて切り詰めて戻す（リンクの `FileInfo.Length` を使わないこと。リンク自身の長さになる）。
  一時ファイルは `TemporaryFileRegistry` でワイルドカードの解決から除く。`-ErrorAction Stop` では `WriteError` が戻らないので、**元に戻してから** `WriteError` を呼ぶこと。
  表現できるかは状態を持たない `GetByteCount` で書く前に確かめる（Encoder の状態を進めない）。
  **規則は「エラーで止まったら、そのファイルには、その呼び出しで書いた分を残さない」**（`EncodingChangeOnAppend` も元に戻す。第五次の後半）。パイプラインの停止（Ctrl+C）は書いた内容を残す
- **同じファイルかどうかは、パスの文字列ではなく実体で比べる（`Internal/FileIdentity`。1.3.0 第五次の後半）。** Windows は `GetFileInformationByHandle` のボリュームのシリアル番号とファイル ID、
  Linux / macOS はリンクを解決したパス。まだ無いファイルは親フォルダーの実体＋名前で比べる。`ActiveReadRegistry`（`SamePathRoundTrip`）・書き込み系の書き込み先の表・`Convert-ProbedContent` の N8 / N9 が使う。
  `PathComparison`（文字列の比較）を、同じファイルの判定に使い直さないこと。追記は「切り詰めずに開く → 開いたハンドルで比べる → 書く」
- **パスは `Internal/FileSystemPath.TryGetFullPath` で絶対パスにする（`Path.GetFullPath` を直接呼ばない）。** `a.txt::$DATA` は `a.txt` とし、代替データストリームは `AlternateDataStreamNotSupported`。
  .NET Framework の `GetFullPath` はストリームの名前で `NotSupportedException` を投げる
- **`-LiteralPath` の別名は `PSPath`・`LP`・`FullName`、`-Path` に `FullName` を付けないこと**（BUG-0003。FileInfo の `FullName` がワイルドカードとして展開される）。
  `Resolve-Encoding -Path` は展開しないので `FullName` のまま。`Set-` / `Add-ProbedContent -Path` のワイルドカードが 0 件なら `ItemNotFound`（読み込みはエラーにしない）
- **読み込みは `Internal/ProbedFileReader` だけを通し、`StreamReader` には preamble を持たない `ReaderEncoding` を渡す（1.3.0 第六次。BUG-0021）。** BOM 付きの Encoding を渡すと、
  BOM の後ろへシークした後で `StreamReader` が 2 個目の BOM も読み飛ばす。取り除く BOM は先頭の 1 個だけ。net48 では同じ部品の `EndOfStreamSignal` / `FlushAtEndDecoder` が
  ストリームの終わりで Decoder を flush させる（BUG-0042。.NET Framework の `StreamReader` は flush せず、途中で切れた末尾を捨てていた）
- **ISO-2022 で書く追記は、追記先が ASCII 以外の状態のまま終わっていれば、最初に書く内容の前に戻すシーケンスを書く**（`Internal/Iso2022TailState`、`ProbedFileWriter` の `_pendingPrefix`。BUG-0018）。
  `-AllowEncodingChange` で別の文字エンコーディングを書くときは書かない（利用者の決定）
- **CSV の列名どうしの比較は `Internal/CsvNameComparer.Instance` を使う**（net10.0 は `OrdinalIgnoreCase`、net48 は .NET 10 から作った表 `CsvNameCaseTable`。BUG-0020）。
  列名でプロパティを引くときは `CsvPropertyViews.FindProperty`。表は `tools/New-CsvNameCaseTable.ps1` で生成する（手で直さない。.NET の版で結果が変わったら作り直し、`CsvNameComparerTests` を通す）
- **`ansi` / `oem` と `Locale` のコードページは、コアの `Internal/SystemCodePages` だけから求める**（Windows は `GetACP` / `GetOEMCP`、それ以外はカルチャーの値。BUG-0032）。`CurrentCulture.TextInfo` を直接使わないこと
- **継承した us-ascii は BOM 無しの UTF-8 とみなす（1.3.0 第五次）。** `EncodingVocabulary.TreatInheritedAsciiAsUtf8` を `EncodingInheritance.FromFile`（Auto・`-EncodingFrom`・追記の整合性検査の相手）と、
  書き込み用途で `EncodingInformation` を解決したときに通す。`-Encoding ascii` の明示と読み取り（`ConvertTo-DotNetEncoding` を含む）は置き換えない
- 文字エンコーディングの判定は**ファイル全体**を対象とする。先頭の一定量に制限すると、
  英数字が続いたあとにマルチバイト文字が現れるファイルを誤判定する。
  **例外は 1.3.0 の `-DetectionLineCount`**（`Resolve-Encoding` / `Import-ProbedCsv` / `Export-ProbedCsv`）で、
  先頭 N 行で判定する。`Import-` / `Export-ProbedCsv` は、使う前にファイル全体を厳密に検証して誤判定を止める
  （`Resolve-Encoding` は検証しない。「先頭 N 行が何に見えるか」を報告するコマンドのため）
- `Add-ProbedContent` の整合性検査は**実際に書き出されるバイト列の比較**で行う。
  名前の組み合わせ表では判定しない（仕様書 6.1）
- メッセージは `Internal/MessageCatalog` が英語・日本語・韓国語・繁体字中国語・簡体字中国語で持つ。
  サテライトアセンブリではなく単一アセンブリ内の表。`Resolve-Encoding` の既存メッセージは英語のまま
- 言語は `CurrentUICulture` の名前を `EncodingDetector.ResolveEastAsianLegacyRegion` に渡し、
  判定のカルチャー圏をそのまま言語に写して決める（`LanguageFromRegion`）。
  `TwoLetterISOLanguageName` や親カルチャーのチェーンは使わない（NLS と ICU で名前・親が異なる）。
  **香港（`ChineseHongKong`）は台湾と同じ繁体字の辞書を使う（B 案）。** 香港用の辞書は持たない。
  香港用の文面を持つ場合は、辞書と `Catalogs` の行を足して `LanguageFromRegion` の香港の行を変える。
  `MessageCatalogTests.HongKongMessages_AreIntentionallySameAsTaiwan_PlanB` が落ちるので、そこで B 案を見直す
- `-Culture` / `-Strategy` は共通の基底クラス `Cmdlets/ProbedContentCommandBase` にある。
  `Resolve-Encoding` と**同じ名前・同じ値**であり、判定方式の語彙表は
  `Cmdlets/ResolveEncodingOptions.TryParseStrategy` に集約している（表を二重に持たない）。
  基底クラスが `BeginProcessing` を持つため、派生側では**必ず `base.BeginProcessing()` を先に呼ぶ**
  （書き込み系は `-EncodingFrom` の判定を `BeginProcessing` で行うため）。
  判定オプションが届く先は `Internal/ProbedFileReader.Open` と
  `Internal/EncodingInheritance.FromFile` の 2 か所だけ。
  `ConvertTo-DotNetEncoding` は判定処理を呼ばないため対象外
- 実行環境依存の例外を利用者に見せない。不正なカルチャー名の検証では
  `CultureNotFoundException` を内部例外として持たせていない（1.3.0 第五次の後半からは `CultureInfo` を使わずに検証する）。
  この例外のメッセージは .NET Framework と .NET Core で文言が異なり、
  PS 5.1 と 7.x で見えるメッセージが変わってしまうため（PSCompat が検出した）

### 1.2.0 で追加したコマンド（Out-ProbedFile / Convert-ProbedContent）

仕様は `docs/EncodingProbe-1.2.0-仕様書.md`。仕様と異なった点・実装で決めた点は `docs/EncodingProbe-1.2.0-実装記録.md`。「標準の挙動」はすべて `docs/EncodingProbe-1.2.0-調査-Out-File挙動の実測.md` の実測に基づく。

- `Cmdlets/OutProbedFileCommand` は `ProbedContentWriterCommandBase` を継承しない（パラメータの形が違う。`-FilePath` は単一の string）。
  書き込み部品（`ProbedFileWriter`・`LineBreakResolver`・`EncodingInheritance`・`ActiveReadRegistry`・`AppendConsistency`）を共用する
- 整形は `Microsoft.PowerShell.Utility\Out-String -Stream` のステッパブルパイプラインで行う。
  **`Begin(this)` ではなく `Begin(expectInput: true)` を使うこと。** コマンドを渡すと Out-String の出力が
  本コマンドの出力ストリームへ直接流れ（プロキシコマンドの動作）、`Process` / `End` の戻り値が空になる
- ファイルを開くのは最初の行を書く直前（同一パスの往復を検出するため）。失敗はすべて終了エラー
- `-Encoding` の省略と明示的な `Auto` は意味が違う（省略は utf8NoBOM、`-Append` の省略は追記先から継承）。`BoundParameters` で区別している
- `Cmdlets/ConvertProbedContentCommand` の `-Encoding` は `Internal/ConvertEncodingTransformationAttribute` で
  元の値を保持したまま解決する。`-Bom` と組み合わせたとき、BOM 接尾辞付きの名前と裸名・WebName で扱いが違うため
  （`EncodingSpec` だけでは `unicode` と `unicodeBOM` を区別できない）。書き込み用途の検証は `BeginProcessing` で行う
- 変換元・変換先は `EncodingVocabulary.BuildStrictEncoding`（例外フォールバック）で復号・符号化する。
  **不正なバイト列の位置は `DecoderFallbackException.Index` を使わず自前で求める**（`FindInvalidBytes`）。
  `Index` の基準が .NET Framework と .NET Core で違い、PS 5.1 と 7.x で異なる位置を報告していた（PSCompat 相当の手動確認で検出）。
  表現できない文字の位置も `EncoderFallbackException.Index` を使わず、その文字の最初の出現位置から求める
- `-Force` で外した読み取り専用属性は `Internal/ReadOnlyAttributeScope` で元に戻す。
  書き込み系の 4 コマンドすべてがこの部品を通る（1.2.0 で Set-/Add-ProbedContent の「外したまま」を修正した）
- **ファイルを変更しない検査は `ShouldProcess` より前に置く**（1.2.0 手動確認後の方針）。`-WhatIf` でも、実行すれば失敗するエラーを
  `-WhatIf` なしと同じ ID で報告するため。`Convert-ProbedContent` の N8 は、書いた名前ではなく**検査を通った時点で書く予定の名前**を記録する
- `Convert-ProbedContent -PassThru` の往復は、Unicode 系は `SourceEncoding`、それ以外は `SourceCodePage`（`euc-jp` は 51932 に解決されるため）。
  `EncodingVocabulary.GetUnifiedName` は WebName を小文字にそろえる（.NET Framework は 20932 を `EUC-JP` と返す）
- PSCompat の `.GetNewClosure()` 付きのシナリオの中では `$script:` 変数が見えない（動的モジュールのスコープになる）。
  スクリプトの最上位で `$script:x = ...` と定義した変数は、クロージャの中では `$x` と書いて参照する

### 1.3.0 で追加したコマンド（Import-ProbedCsv / Export-ProbedCsv）と -DetectionLineCount

仕様は `docs/EncodingProbe-1.3.0-仕様書.md`、実装で決めた点は `docs/EncodingProbe-1.3.0-実装記録.md`。「標準の挙動」はすべて
`docs/EncodingProbe-1.3.0-調査-CSVコマンド挙動の実測.md`（測定は `tools/Measure-CsvBehavior.ps1`、生の結果は `tools/CsvBehaviorResults/`）に基づく。

- **CSV の解析・文字列化は PowerShell 7.x からの移植（`Internal/Csv/`）で行い、ホストの `Import-Csv` / `Export-Csv` / `ConvertTo-Csv` を呼ばない。**
  そのため 5.1 でも 7.x と同じ結果になる（書き出しはバイト単位）。規則は移植元のとおりとし、独自の改良を加えない。
  仕様で決めた変更（追記先の末尾の改行を補うなど）は移植したコードの外（コマンド側）で行う
- 移植元の内部 API の置き換え: `PSObject.GetPropertyCollection` / `PSMemberInfoIntegratingCollection` は `Internal/CsvPropertyViews`
  （`PSObject.Properties` を種類で選り分け、Property の種類は `psadapted` にあるものだけ残す）、`new PSObject(int)` は `new PSObject()`（5.1 に無い）。
  `AsSpan()` などの net48 に無い機能も置き換え、`System.Memory` への依存は持ち込んでいない。
  **FileInfo などホストによって形が違うオブジェクトの列は、両ホストで一致しなくてよい**（入力そのものが違うため。第一次の決定）。
  PSCompat ではホストによって形が変わらないオブジェクトを使う
- **`-Culture`（判定のカルチャー。基底クラス）と `-UseCulture`（区切り文字）は別のもの。** コード・コメント・ヘルプで取り違えないこと
- `Import-ProbedCsv` のパラメータセットは 4 つ（`DelimiterPath` / `DelimiterLiteralPath` / `CulturePath` / `CultureLiteralPath`）。
  `Export-ProbedCsv` は標準の `Export-Csv` と同じ 2 つ（`Delimiter` / `UseCulture`）で、`-Path` / `-LiteralPath` は必須にせず「ちょうど一方」を検査する。
  4 つにするとパイプライン入力と `-LiteralPath` の組み合わせで `AmbiguousParameterSet` になる（第三次で改めた）
- `Export-ProbedCsv` と `Out-ProbedFile` は、殻（パスの解決、`-NoClobber` と読み取り専用の検査、最初の行を書く直前に開く処理、追記の整合性検査、空の入力、後始末）を
  `Cmdlets/ProbedFileOutputCommandBase` で共用する。文字エンコーディングの決定は各コマンドにある。
  `Export-ProbedCsv` は、ヘッダーと末尾の改行を読むため、**1 バイト以上の追記先を常に判定する**（`Out-ProbedFile` とはここが違う）。
  BOM だけの追記先にはヘッダーを書く（BOM は重ねない）
- **`-DetectionLineCount` の判定は 1 か所に集約している。** 切り出しは `Internal/HeadLineCutter`、判定は `Internal/HeadLineDetection`
  （`Resolve-Encoding` はここまで）、読み替え（us-ascii を改行の型に応じて UTF-8 / UTF-16 / UTF-32 に）と全体の検証は `Internal/HeadLineEncodingResolver`
  （`Import-` / `Export-ProbedCsv`、`-EncodingFrom` も含む）。3 つのコマンドの判定結果（読み替え前）が一致することはこの共用で保証している
- **切り出しの型の決め方（`HeadLineCutter.DecideUnit`。1.3.0 第五次、仕様書 2.4）。** 先頭 64 KiB を単位に区切って N・A・B・Z・Abr・P を数え、
  共通の条件 A ≥ min(16, ⌈0.6N⌉)・Z = 0（UTF-16 は P ≤ 0.7N）で、UTF-32（B ≤ 0.2A）→ UTF-16 の改行の向き（Abr ≥ 4、逆 ≤ 0.25 Abr）→ UTF-16（B ≤ 0.2A）の順に決める。
  1 バイトの型で `00` があり 64 KiB 以内なら `None`（ファイル全体）。改行は決めた型の形だけで探す。値は利用者が確定したもの。根拠は `docs/EncodingProbe-1.3.0-調査-切り出しの境目.md`。
  `HeadLineCutterRuleTests` が調査の 1033 件（生成は `Helpers/CutThresholdSamples.cs`。中身を変えないこと）で、規則のモデルと一致することを確かめている。
  調査ブランチ `investigate/1.3.0-cut-threshold` は**どこにもマージしない**（文書だけをファイル単位で持ってきた。ブランチはリリース前に利用者が消す）
- 読み替えで UTF-16 / UTF-32 にしたときは `LineBreak` も読み替えた後の単位で数え直す。us-ascii を UTF-8 に読み替えたときは、全体の検証で ISO-2022 のエスケープシーケンス
  （ESC の後に `$` `(` `)`）も探して `DetectionVerificationFailed` にする（1.3.0 第五次）
- 全体の厳密な検証（`Internal/StrictDecodeVerifier`）は `DecoderFallbackException.Index` を使わない。失敗したときだけ先頭から読み直し、
  成功済みのバッファを同じ区切りで与えて `Decoder` の状態を再現し、失敗したバッファから 1 バイトずつ与えて位置を求める（ISO-2022 のような状態を持つ文字エンコーディングのため）
- 検証の失敗は `DetectionVerificationFailed`（`Import-ProbedCsv` は非終了、`Export-ProbedCsv` は `ShouldProcess` より前の終了エラー）。
  `Import-ProbedCsv` の列名の重複は標準と同じ ID の非終了エラー（24 章 U3）。ワイルドカードが 0 件の場合は `Get-ProbedContent` と同じくエラーにしない

### MAML ヘルプ

`SnowStack.EncodingProbe.PowerShell/` の下の `en-US/` `ja-JP/` `ko-KR/` `zh-TW/` `zh-CN/` `zh-HK/` `zh-MO/` に
`SnowStack.EncodingProbe.PowerShell.dll-Help.xml` を置いている（csproj で出力へコピーする）。
対応言語はメッセージ（`MessageCatalog`）と同じ 5 言語。
**`zh-HK/` と `zh-MO/` は `zh-TW/` のバイト単位の複製である（1.2.0、B 案）。**
Microsoft が zh-HK 言語パックの提供をやめ zh-TW を案内しているのに合わせた判断で、香港用の文面は作らない。
**zh-TW を直したら zh-HK / zh-MO にもコピーすること。**
`MamlHelpTests.HelpFile_HongKongAndMacau_AreIntentionallySameAsTaiwan_PlanB` がバイト列の一致を検査している。

`Get-Help` は「UI カルチャー名と同名のフォルダー → 親カルチャーのフォルダー → en-US」の順で探す（両ホストで同じ）。
フォルダー名は **Windows が報告する UI カルチャー名そのもの**にしてある。
`zh-Hant` / `zh-Hans` のような親カルチャー名は置かない（台湾版と香港版を分けられなくなる）。
`zh-MO` の親は `zh-Hant` で `zh-HK` フォルダーには届かないため、`zh-MO/` も置いている。
`zh-SG` `ko` `yue` 系などは en-US にフォールバックする。
PS 7（ICU）は `zh-Hant-TW` / `zh-Hant-HK` / `zh-Hant-MO` / `zh_HK` を正規化しないため、これらのヘルプは en-US になる
（PS 5.1 は `zh-TW` などに正規化するので繁体字）。既知の課題: `docs/EncodingProbe-課題-ヘルプの用字付きカルチャー名.md`

`publish/` へ配置する際は `core\` と `desktop\` の下に 7 フォルダーぶん、計 14 か所へコピーする
（`Copy-Item -Recurse` でビルド出力ごと配ればよい）。自動化は `docs/EncodingProbe-課題-ヘルプ配布の自動化.md`

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

## バージョン更新時に触る場所

バージョン番号は 3 か所に分散している。上げるときはすべて揃える：

1. `SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj`（`Version` / `AssemblyVersion` / `FileVersion`）
2. `SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.csproj`（同上）
3. `publish/SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.psd1`（`ModuleVersion`、`PrivateData.PSData.ReleaseNotes`）

`CHANGELOG.md` にも追記すること。

**PowerShell モジュールだけをリリースする場合は、2 と 3 だけを上げ、1（クラスライブラリ）は据え置く。**
クラスライブラリに変更が無いのにバージョンを上げると、NuGet パッケージの版と中身が食い違うためである。
モジュールは、据え置いたクラスライブラリの DLL を同梱する。
（1.3.0 は当初この扱いだったが、第四次でクラスライブラリの判定処理を直したため、結果として 3 か所すべてを 1.3.0 にして両方をリリースする）

新しいコマンドレットを追加したら、次も忘れずに行う:

- `.psd1` の `CmdletsToExport` に追加する
- `tests/EncodingProbe.PowerShell.Tests/CmdletTests/MamlHelpTests.cs` の `CmdletTypes` に追加する
- MAML ヘルプ 5 言語（`en-US` / `ja-JP` / `ko-KR` / `zh-TW` / `zh-CN`）すべてに項目を追加し、
  `zh-TW` を `zh-HK` / `zh-MO` へコピーする
  （`MamlHelpTests` が骨格の一致と複製の一致を要求するため、1 言語だけ足すとテストが落ちる）
- `tests/EncodingProbe.PowerShell.Tests/Helpers/ProbedCommandRunspaceFixture.cs` に登録する
  （登録しないとテストのランスペースから呼べない）
- `tests/PSCompat/ProbedCompatScenarios.ps1` にシナリオを追加する

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
- `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` … コアの判定エンジン側の未着手課題（3 件）

1.1.0 では次を変更していない（指示書 1 節の制約。今後も維持すること）:

- `Resolve-Encoding` / `Get-EncodingProbePlatformInfo` の既存のパラメータと戻り値を変更しない
  （1.3.0 で `Resolve-Encoding -DetectionLineCount` を追加した。利用者が明示的に決定した例外）
- `EncodingInformation` 型（`DotNetEncoding` プロパティは「追加しない」と決定済み）
- コアの NuGet パッケージの公開 API

## 1.2.0 の作業記録

**リリース済み。** クラスライブラリ（NuGet）と PowerShell モジュールの両方を公開し、
作業ブランチ `feature/1.2.0-world-language-detection` を master へマージした（2026-09-30）。

1.2.0 で行ったこと（詳細は CHANGELOG.md の 1.2.0 節）:

- 課題 1: 東アジア以外の言語への対応（カルチャーゲート、旧マルチバイトのクロスチェック、UTF-8 の厳密化）
- 第一次修正: クロスチェックの信頼度の下限（0.5 / 0.55）
- 第二次修正: 香港 Big5 段階 1（カルチャー名のサブタグ分解、繁簡の系統クロスチェック、Big5 後続バイトの厳密化）
- 第三次修正: 香港・マカオのメッセージとヘルプ（台湾と同じ内容。B 案）、メッセージの言語選択をカルチャーゲートと共通化
- 緊急修正: UTF.Unknown の `Encoding` が null（iso-8859-16 など）のときの `NullReferenceException`（1.1.0 から存在）
- 追加作業: 信頼度の測定の文書化、テストデータの追加
- PowerShell モジュールの新機能: `Out-ProbedFile` / `Convert-ProbedContent` の追加、
  `Set-` / `Add-ProbedContent` の `-Force` 後の読み取り専用属性の復元、`-LineBreak` の範囲の明文化
  （`docs/EncodingProbe-1.2.0-仕様書.md`、実装記録は `docs/EncodingProbe-1.2.0-実装記録.md`。完了した依頼文は削除した）

完了した依頼文はリポジトリから削除した（git の履歴に残っている）。判断の根拠は次の文書に移してある。

- `docs/EncodingProbe-1.1.0-仕様書.md` … 4.4 節（下限・系統クロスチェック・カルチャー名の解釈・.NET に無いエンコーディング・短文の限界）、
  8.1 節（メッセージとヘルプの言語、B 案）
- `docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md` … 下限の実測値と根拠、再測定の手順、0.55 / 0.8 の経緯
- `docs/EncodingProbe-1.2.0-課題_人間記述用.md` … 人間が確認して発見した課題。
  課題 1（東アジア以外の言語への対応）は対応済み。経緯と判断の理由は「Claude用記載欄」にある
- `docs/EncodingProbe-1.2.0-課題-香港Big5対応.md` … 香港 Big5 の構造解析と実測（第二次修正の背景）
- `docs/私用領域の扱い_方針草案.md` … 私用領域（外字・HKSCS 固有字）に介入しない方針

未着手・未決の課題:

- `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` … ISO-2022 系の未着手課題（3 件）
- `docs/EncodingProbe-課題-ヘルプの用字付きカルチャー名.md` … PS 7 で `zh-Hant-*` / `zh_HK` のヘルプが en-US になる。未着手
- `docs/EncodingProbe-課題-ヘルプ配布の自動化.md` … 配布物へのヘルプのコピーの自動化。未着手
- 短いスペイン語の行で UTF.Unknown が .NET に無い iso-8859-10 を返すと、上書き経路の対象にならず東アジアのまま残る。
  上書きして「判定不能（名前だけ）」を返すべきかは未決（測定文書 8.3 節）
- `docs/EncodingProbe-課題-香港Big5段階3_HKSCS復号.md` … HKSCS の復号・符号化。
  **本製品では対応しない（方針）。** 私用領域の内容に干渉しないのが基本方針で、香港固有字の解釈は利用者に任せる。
  対処するとしても別製品・別機能で扱う。HKSCS の対応表や写像を本製品に持ち込まないこと

2026-09-29 に手動確認後の修正（依頼 `docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md` は完了後に削除。コミット `4c5b1b3` までの git の履歴に残っている）を行った。
内容と判断は `docs/EncodingProbe-1.2.0-実装記録.md` 4 章。カルチャーによるシングルバイトの推定
（`docs/EncodingProbe-課題-カルチャーによるシングルバイトの推定.md`）と、変換元が 0 バイトのときの `SourceEncoding` の見直しは 1.3.0 以降で検討する（未着手）。

済んだ変更は CHANGELOG.md の「1.2.0」の節にまとめてある。

マージ前に、完了した実測依頼（`docs/EncodingProbe-1.2.0-実測依頼-Out-File挙動.md`）と、
手動確認後の修正の説明文書 2 件（`docs/EncodingProbe-1.2.0-説明-手動確認後の修正_ClaudeDesktop向け.md` / `_ブログ向け.md`）を
削除した（git の履歴に残っている）。

## 1.3.0 の作業記録

**PowerShell モジュールとクラスライブラリの両方を 1.3.0 としてリリースする**（当初はモジュールだけの予定だった。第四次で改めた）。作業ブランチは `feature/1.3.0-csv`。

**現在の状態（2026-10-26 時点）: 第六次 A（テスト担当の BUG-0018・0020・0021・0031・0032・0033・0037・0042。実装記録 11 章）を実装して feature/1.3.0-csv にコミットした（プッシュしていない）。第六次は A・B・C の 3 回に分けて依頼される。次は B の依頼を待っている。master へは未マージ・未リリース。**
第五次の仕上げ（実装記録 10.16 節）までは 2026-10-24 に開発用ブランチ（origin/feature/1.3.0-csv）へプッシュ済み。
第五次の後半（テスト担当の BUG-0003〜0008・0011・0015〜0017、H-17、9.9 節の 1〜4、報告の後の修正 10.14 節）は 2026-10-23 に origin/feature/1.3.0-csv へプッシュ済み。

- 両 TFM のテスト（コア net48 2139 / net10.0 2129 件、モジュール 1771 件）と PSCompat（1135 シナリオ）が通っている（第六次 A を含む）。
  第六次 A では、テスト担当のスクリプト 8 本を作業フォルダーに作った配布の構成で両ホストで確かめた（実装記録 11.11 節）。
  Linux の実測のため、WSL の Ubuntu-24.04 のホームに `~/.dotnet`（.NET 10.0.14）と `~/powershell-7.6.9` を入れてある（11.4 節）
  テスト担当のスクリプト 17 本も、作業フォルダーに作った配布の構成で両ホストで確かめた（実装記録 10.11 節・10.16 節）
  **性能:** 小さなファイルはメモリに写してから直接書くようにし、1000 個への `Set-ProbedContent` は修正の前の約 1/4〜1/5 になった。第四次よりまだ約 2 倍遅いのは、利用者が受け入れた（直さない）
  **著作権の表記:** すべて `Copyright (c) 2026 Motoi Tsushima` にそろえた（10.16 節の表。`Authors`・`Company` は `motoi.tsushima` のまま）
  **書き込みはファイルの ACL に従う（利用者の決定）。** 置き換えの前に `SafeFileOverwrite.EnsureWritable` で書き込み用に開けるかを確かめる（権限の拒否だけを失敗にし、共有違反は無視する）。10.16 節の末尾
- 第五次の前半の内容・決定・結果は実装記録 9 章、後半は 10 章（標準の実測、`::$DATA` とカルチャー名の案と決定、保たれるもの、決めきれなかったこと）、仕上げは 10.16 節
- テスト担当のフォルダー（`C:\_test\EncodingProbeBboxTest`）は読むだけにする。テスト担当にまだ伝えていない件（コアの改行の判定）を、テスト担当の報告やフォルダーに書かないこと
- 第五次の依頼文（`docs/EncodingProbe-1.3.0-依頼-第五次*.md` があれば）もコミットしない
- **配布フォルダー（`publish/SnowStack.EncodingProbe.PowerShell/`）の DLL は第三次（クラスライブラリ 1.2.0）のビルドのまま。** 第五次の後の Release ビルドで作り直す必要がある（リリース手順 2 章）
- リリースで残っている作業（`docs/EncodingProbe-1.3.0-リリース手順.md` 2 章・4 章）: 配布フォルダーの作り直し、README のリリース日の行、master へのマージ、
  配布フォルダーの公開、NuGet パッケージ 1.3.0 の公開、実験用ブランチの削除
- **利用者の判断待ち:** 配布フォルダーの `LICENSE.txt` / `THIRD-PARTY-NOTICES.txt` は Git の未追跡ファイルのまま（コミットしていない）。
  Git で管理するか、`.gitignore` に加えるかは未決
- 手動確認で課題が見つかったら、1.1.0 / 1.2.0 と同じく `docs/EncodingProbe-1.3.0-課題_人間記述用.md`（まだ無い）に利用者が書いて渡す。
  次に起動したときは、まずこのファイルの有無を確かめる
- 依頼文 4 件（`docs/EncodingProbe-1.3.0-依頼-第一次.md` / `第二次` / `第三次` / `第四次`）は手元に未追跡ファイルとして残っている。コミットしないこと（利用者の指示）
- **2026-10-20 追加:** 利用者が、BOM 無し UTF-16 / UTF-32 の英数字だけのファイルが us-ascii になる課題を、クラスライブラリとモジュールの **1.3.0 として同時に**直すと決めた
  （モジュールだけのリリースという方針を改める）。修正の前の実測を済ませた: 報告は `docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md`、
  スクリプトは `tools/Measure-NulInputDetection.ps1` / `tools/Invoke-NulInputMeasurement.ps1`、生の結果は `tools/NulInputDetectionResults/`。
  修正案は実験用ブランチ `investigate/1.3.0-nul-ascii`（ローカルのみ。**どこにもマージしない**）にある。
  第 2 回の実測（修正案 2：すべての単位が `<00 以外> 00` などの形に合うときだけ UTF-16 / UTF-32 とする厳密な検査）も済ませた。
  報告は同じ文書の 7 章、生の結果は `tools/NulInputDetectionResults/round2/`、修正案 2 は実験用ブランチ `investigate/1.3.0-nul-ascii-strict`（ローカルのみ。**どこにもマージしない**）。
  実測依頼の文書（`docs/EncodingProbe-1.3.0-実測依頼-NUL入りASCIIの判定.md`、`…-NUL入りの入力の判定-第2回.md`）と第四次の依頼文（`docs/EncodingProbe-1.3.0-依頼-第四次.md`）もコミットしない
- 利用者が修正案 2 の採用を決め、仕様を `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`（追補）にまとめた（仕様書なので第四次でコミットした）。
  第四次で本実装を `feature/1.3.0-csv` に改めて書いた（実験用ブランチはマージしていない）

1.3.0 で行ったこと（詳細は CHANGELOG.md の 1.3.0 節）:

- 第一次: 共通の部品（先頭 N 行の切り出し、全体の厳密な検証、PowerShell 7.x の CSV 処理の移植）と `Resolve-Encoding -DetectionLineCount`
- 第二次: `Import-ProbedCsv`
- 第三次: `Export-ProbedCsv`、`Out-ProbedFile` の殻の共通化、バージョン・文書の仕上げ
- 第四次: クラスライブラリの `0x00` を含む入力の判定の修正（追補）、クラスライブラリを 1.3.0 に。実装記録 8 章。
  修正後の判定は第 2 回の実測の「修正案 2」と 180 件すべて（両 TFM、判定 API とコマンド）で一致した
- 第五次（前半）: 書き込みで文字を失わない（表現できない文字のエラーと取り消し、継承した us-ascii を UTF-8 に）、切り出しの規則の置き換え、
  コアの改行の判定を単位で数える、読み替えたときの `LineBreak` の数え直し、ISO-2022 の検査、空の列名の ID。実装記録 9 章
- 第五次（後半）: パスの束縛（`FullName` を `-LiteralPath` に）、書き込みの 0 件のワイルドカード、同じファイルの判定を実体で、`EncodingChangeOnAppend` の取り消し、
- 第五次（仕上げ）: 小さなファイル（1 MiB 以内）はメモリに写してから直接書く（性能の修正）、著作権の表記を `Copyright (c) 2026 Motoi Tsushima` にそろえた。実装記録 10.15 節・10.16 節
- 第六次 A: ISO-2022 の追記先を ASCII に戻す、CSV の列名の比較を .NET 10 にそろえる、BOM は先頭の 1 個だけ取り除く、曖昧解消で厳密に復号できない候補を選ばない（コア）、
  `ansi` / `oem` をシステムのロケールに（コアの `Locale` も）、`-Value` の `$null` を飛ばす、UTF-7 のインスタンスを拒否、5.1 でも途中で切れた末尾を置き換えの文字で読む。実装記録 11 章、仕様書 18.4・25 章
  上書きのリンク・ハードリンク・権限の扱い、`::$DATA`、`Resolve-Encoding` の検証の部品、カルチャー名を BCP 47 の形で検証（コアも）、`Os.Description` の Trim、文書（BUG-0005・0008・0011）。実装記録 10 章

関連文書:

- `docs/EncodingProbe-1.3.0-仕様書.md` … 機能仕様。実装で改めた点（`Export-ProbedCsv` のパラメータセット、ワイルドカード 0 件）は該当節に経緯を書いてある
- `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md` … 第四次（クラスライブラリの判定処理の修正）の仕様。期待値の根拠は `docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md` 7 章
- `docs/EncodingProbe-1.3.0-調査-切り出しの境目.md` … 第五次の切り出しの規則の根拠（調査ブランチの文書をそのまま持ってきたもの）。結論は 1 章の囲み（P ≤ 0.7 × N を含む改訂版）で、値は利用者が確定した
- `docs/EncodingProbe-1.3.0-ClaudeCode指示書.md` … 実装時の制約（移植の方針、参照してはならないもの）
- `docs/EncodingProbe-1.3.0-調査-CSVコマンド挙動の実測.md` … 標準の CSV コマンドの実測（5.1 と 7.6.7）
- `docs/EncodingProbe-1.3.0-実装記録.md` … 決定事項とその根拠、仕様書と異なった点。**仕様書に書かれていない判断の理由はここにある**
- `docs/EncodingProbe-1.3.0-動作確認手順書.md` … 手元の PC で動作を確かめる手順と、リリース前のチェック
- `docs/EncodingProbe-1.3.0-リリース手順.md` … 配布フォルダー（`publish/`）の作り方と公開までの順序。
  配布フォルダーの DLL とヘルプは Release ビルドの出力を写したもの（1.3.0 は 2026-10-19 に配置済み）

依頼文（`docs/EncodingProbe-1.3.0-依頼-第一次.md` など）はリポジトリに入れていない（利用者の指示）。

未着手・未決の課題（1.3.0 で起票、または 1.4.0 で検討すると決めたもの）:

- 既存のコマンド（`Get-` / `Set-` / `Add-ProbedContent`、`Out-ProbedFile`、`Convert-ProbedContent`）への `-DetectionLineCount`、
  全体で判定した場合の検証、`Out-ProbedFile` / `Add-ProbedContent` の追記での末尾の改行の補完は 1.4.0 で検討する（仕様書 1.4）
- 改行の無い 2 GB 以上のファイルに `-DetectionLineCount` を指定すると、エラーになるまで 2 GB を 1 バイトずつ走査する（約 20 秒）。正しさを優先して最適化していない
