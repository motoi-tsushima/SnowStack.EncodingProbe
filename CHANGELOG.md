# 変更履歴

このファイルは SnowStack.EncodingProbe（NuGet パッケージ）と
SnowStack.EncodingProbe.PowerShell（PowerShell モジュール）の変更をまとめたものです。

## 1.3.0

PowerShell モジュールに CSV の読み書きコマンド `Import-ProbedCsv` / `Export-ProbedCsv` を追加し、
`Resolve-Encoding` に `-DetectionLineCount` を追加しました。

クラスライブラリ（NuGet パッケージ `SnowStack.EncodingProbe`）は、判定処理の不具合を 3 件直し、カルチャー名の検証・OS の説明・ANSI / OEM コードページの挙動を改めて、1.3.0 としました（公開 API の変更はありません）。
モジュール 1.3.0 はクラスライブラリ 1.3.0 を同梱します。

仕様は `docs/EncodingProbe-1.3.0-仕様書.md` と `docs/EncodingProbe-1.3.0-仕様書-追補-NUL入りの入力の判定.md`（クラスライブラリの修正）、
根拠となった標準の CSV コマンドの実測は `docs/EncodingProbe-1.3.0-調査-CSVコマンド挙動の実測.md`、
`0x00` を含む入力の判定の実測は `docs/EncodingProbe-1.3.0-調査-NUL入りの入力の判定.md`、
実装の過程で決めたことは `docs/EncodingProbe-1.3.0-実装記録.md` にあります。

### 不具合の修正（クラスライブラリ）

- **BOM 無しの UTF-16 / UTF-32 で英数字だけのテキストが、`us-ascii` と判定されていた問題を直しました**（1.0.0 から存在）。
  読み込むと各文字の間に `U+0000` が混じっていました。`Resolve-Encoding`・`Get-ProbedContent`・`Import-ProbedCsv` など、判定を使うすべてのコマンドに効きます
  - `0x00` を含み、すべてのバイトが `0x80` 未満の入力を、**すべての単位**が UTF-16 / UTF-32 の英数字の形（UTF-16LE なら `<00 以外> 00`）に合うときだけ UTF-16 / UTF-32 と判定します。
    合わなければ、従来どおり `us-ascii` です。`0x00` を含む ASCII のテキスト（末尾の `00`、`00` で埋めた固定長レコードなど）は `us-ascii` のままです
  - 判定順序と、それ以外の入力に対する結果は変わりません（`0x00` を含まない入力、BOM のある入力、`0x80` 以上のバイトを含む入力、ESC シーケンスのある入力）
  - 既知の限界: 2 バイトとも `0x80` 未満になる文字（「上」U+4E0A など）や U+0000 を英数字に混ぜた BOM 無しの UTF-16 / UTF-32 は、従来どおり `us-ascii` です
    （`Import-ProbedCsv` / `Export-ProbedCsv` で `-DetectionLineCount` を指定すれば、正しく読み書きできます）。
    `41 00` のような数バイトの入力は、形に合えば UTF-16 / UTF-32 と判定します
- **UTF-16 / UTF-32 と判定したときの改行コード（`LineBreak`）を、その単位（2 バイト / 4 バイト）で数えるようにしました**（1.0.0 から存在）。
  1 バイトずつ `0A` / `0D` を数えていたため、「上」U+4E0A（UTF-16LE で `0A 4E`）・「《」U+300A・「不」U+4E0D のような文字があると、
  CR-LF のファイルを `LfAndCrAndCrLf`、LF・CR のファイルを `LfAndCr` と報告していました。BOM の有無によりません
  - PowerShell モジュールでは、`Resolve-Encoding` の `LineBreak` のほか、改行を継承する書き込み（`-EncodingFrom`、`-Encoding` 省略の追記、
    `Convert-ProbedContent -EncodingFrom`）が、改行が CR のファイルから LF を継承していました。`Convert-ProbedContent -PassThru` の `SourceLineBreak` は、
    復号した文字列から求めているので、もともと正しい値でした
  - 公開の `EncodingDetector.DetectLineBreak()` は、文字エンコーディングを知らずに呼ばれるメソッドなので、従来どおりバイト単位で数えます
- **短い EUC-JP の日本語を、日本語のカルチャーで Shift_JIS と判定していた問題を直しました**（1.0.0 から存在。BUG-0031）。
  バイト列だけでは区別できない組み合わせ（EUC-JP と Shift_JIS、EUC-KR と CP949、EUC-TW と CP950）をカルチャーと改行で決める処理で、
  判定に使った標本を、決めた候補では厳密に（例外フォールバックで）復号できず、もう一方では復号できる場合は、もう一方を選びます。
  CP932 に割り当ての無い 2 バイト（`FC CB` など）を含む約 40 バイト以下の EUC-JP を、改行が CRLF・改行なしのときに 932 と判定し、読み込みが `・` や私用領域の文字に黙って化けていました
  - どちらの候補も厳密に復号できない場合と、その TFM で作れない候補（EUC-TW の 51950）は、今までどおりカルチャーと改行で決めます
  - 既存のテストとテストデータ全件（6 カルチャー × 2 方式、両 TFM）の判定結果は変わりません

### 挙動の変更（クラスライブラリ）

公開 API（型・メソッド・プロパティ、引数と戻り値の型）は変えていません。次の 5 点は、1.2.0 までと結果が変わります。

- **曖昧な組み合わせで、標本を厳密に復号できない候補を選びません**（上の「不具合の修正」の 3 件目。BUG-0031）
- **`PlatformInformation.Locale` の `AnsiCodePage` / `OemCodePage` を、Windows ではシステムのロケールの値（`GetACP` / `GetOEMCP`）にしました**（BUG-0032）。
  1.2.0 までは、スレッドのカルチャー（`CultureInfo.CurrentCulture`）の `TextInfo` の値でした。Windows の「地域」の形式をシステムのロケールと違う値にしている環境
  （日本語の Windows で形式を「英語（米国）」にしているなど）で、1252 / 437 などを返していました。Windows 以外は今までどおり現在のカルチャーの値です。
  `CurrentCulture` はスレッドのカルチャーの名前のままです

- **UTF-16 / UTF-32 と判定したときの `LineBreak` を、その単位で数えます**（上の「不具合の修正」の 2 件目。BUG-0014）。1.2.0 までと違う `LineBreak` を返すことがあります
- **カルチャー名（`EncodingDetectorOptions.Culture`）を受け付けるかどうかを、名前の形（BCP 47）で決めるようにしました**（BUG-0007）。
  1.2.0 までは `CultureInfo` で作れるかどうかで決めていたため、net48（NLS）と net10.0（ICU）のビルドで結果が違いました（`C`・`root` は net10.0 だけが受け付け、`und-u-ca-gregory` は net48 だけが受け付けていた）。
  **`ArgumentException` になる名前の集合が変わります**（net48 と net10.0 でそろいます）。受け付けない名前は従来どおり `ArgumentException`（.NET の `CultureNotFoundException` を内部例外に持たない）です。規則は `CultureNameSubtags.IsWellFormed` にあります
  - `C`・`root`・`POSIX`・`c-JP`・`ja-JP-x`・`ja-JP-JP` などは、両方のビルドで受け付けません（`c-JP` などは 1.2.0 まで両方で受け付けていました）
  - `art-lojban`・`zh-min-nan`・`zh-yue`・`zh-guoyu`・`und-u-ca-gregory`・`cel-gaulish` などは、両方のビルドで受け付けます。受け付けた名前で `CultureInfo` を作ることはありません（判定はカルチャー名のサブタグだけで行います）
  - **言語の直後の拡張言語（extlang）は、それを言語として扱います**（RFC 5646 のとおり、`zh-yue` = `yue`、`zh-cmn` = `cmn`）。`zh-yue` は広東語（繁体字・香港）として判定します。
    カルチャー圏の表は増やしていないので、表に無い言語（`cmn`・`min` など）は、ほかの表に無い言語と同じく東アジアの旧マルチバイトの判定を行いません（これまで `zh-cmn` などは `zh` と同じ簡体字でした）
- `PlatformInformation.Os.Description` の前後の空白を除きました（BUG-0008。.NET Framework の `RuntimeInformation.OSDescription` は末尾に空白が付く）

### 挙動の変更（PowerShell モジュール）

最初の 2 件は 1.2.0 までと結果が変わります。残りは 1.3.0 で追加した機能（`-DetectionLineCount`・`Export-ProbedCsv`）の、リリース前の不具合の修正です。


- **書き込みの 4 コマンド（`Set-ProbedContent` / `Add-ProbedContent` / `Out-ProbedFile` / `Export-ProbedCsv`）は、表現できない文字をエラーにします**（BUG-0001・0010・0012）。
  1.1.0 から、書き込み先の文字エンコーディングで表現できない文字を `?`（`3F`）に、Windows PowerShell 5.1 ではベストフィット（`é` → `e`）にして、黙って書いていました
  - エラー ID は `UnrepresentableCharacter`（`Convert-ProbedContent` と同じ）で、メッセージに最初の文字の符号位置（`U+XXXX`）と文字エンコーディングの名前を示します
  - ファイルは書き込みの前の状態に戻します。途中まで書いていた場合も、上書きは同じフォルダーの一時ファイルに書いてから置き換えるので元のファイルは変わらず、追記は元の長さに戻します
  - 文字エンコーディングを継承した場合・`-Encoding` で明示した場合・`-EncodingFrom` の場合のすべてで同じです。`-Encoding` に `System.Text.Encoding` のインスタンスを渡した場合も、
    そのフォールバックの設定（置換・例外）は使いません（例外フォールバックのインスタンスでは、生の `EncoderFallbackException` で止まっていました）
  - `Set-` / `Add-ProbedContent` は非終了エラーで、ほかのファイルへの書き込みは続けます。`Out-ProbedFile` / `Export-ProbedCsv` は終了エラーです
  - `Add-ProbedContent` の整合性検査（`EncodingChangeOnAppend`）より先に調べます。置き換えて書く逃げ道のスイッチはありません
- **継承した `us-ascii` は、BOM 無しの UTF-8 とみなします**（BUG-0001）。対象は、書き込み先・追記先からの継承（`-Encoding` 省略・`Auto`）、`-EncodingFrom`、
  `-Encoding` に `us-ascii` の判定結果（`EncodingInformation`）を渡した場合、追記の整合性検査の相手（追記先の文字エンコーディング）です（`Convert-ProbedContent -EncodingFrom` も含む）
  - 英数字だけのログや CSV に日本語を追記すると、これまでは `?` になっていましたが、UTF-8 で書きます。英数字だけを書く場合のバイト列は変わりません
  - `us-ascii` のファイルに `-Encoding utf8NoBOM` / `utf8BOM` を明示して日本語を追記すると、これまでは `EncodingChangeOnAppend` でしたが、許可します（1.1.0 仕様書 6.1 の表を改めました）
  - `-Encoding ascii` の明示は置き換えません（日本語を書くと `UnrepresentableCharacter`）
- **`-DetectionLineCount` の切り出しで、BOM の無いファイルの改行の形（1 バイト・UTF-16・UTF-32）の決め方を改めました**（BUG-0002・0013）。
  先頭 64 KiB を単位に区切って数えた値で決め、改行はその形だけで探します（1.3.0 仕様書 2.4）。
  `0x00` を含む 64 KiB 以内の 1 バイトのファイルは、行で切らずにファイル全体で判定します
  - これまでは、`0x00` があれば UTF-16 / UTF-32 の形の最初の改行で決めていたため、`0x00` を含む ASCII の CSV を UTF-16 として読み（レコードが化ける・消える）、
    `-Append` で UTF-16 の改行を書いてファイルを壊していました。最初の改行より前に U+0D00 / U+0A00 がある BOM 無しの UTF-16 は、逆のバイト順として読んでいました
  - 型を誤る既知の限界は 1.3.0 仕様書 2.4 の表にあります（U+0D00 / U+0A00 が文字の 1 割以上の UTF-16、先頭 64 KiB に英数字も改行も無い UTF-16、平仮名だけの UTF-16、
    各行に `00` が 1 つある Shift_JIS などのファイル、U+0000 を含む UTF-16 / UTF-32）
- **`-DetectionLineCount` で UTF-16 / UTF-32 に読み替えたときの `LineBreak` を、読み替えた後の単位で数えます**（BUG-0014）。
  1.3.0 仕様書 3.2 の「改行の型は読み替えない」は誤りだったので改めました。英数字と「上」だけの BOM 無しの UTF-16 / UTF-32 の CR のファイルに、LF を追記していました
- **`-DetectionLineCount` で `us-ascii` を UTF-8 に読み替えたときは、全体の検証で ISO-2022 のエスケープシーケンス（`ESC` の後に `$`・`(`・`)`）も探し、
  見つかれば `DetectionVerificationFailed` で止めます**（BUG-0009）。先頭 N 行が英数字だけの ISO-2022-JP / KR の CSV を、エスケープシーケンスのまま読み、UTF-8 を追記していました
- **`Export-ProbedCsv -Append -Force` で追記先のヘッダーに空の列名がある場合は、`CannotAppendCsvWithEmptyPropertyName` で止めます。**
  標準の `Export-Csv` と同じ点で止まりますが、標準の ID（汎用の `Argument`）と .NET の文言（「"name" の値が無効」）を本モジュールのものに替えました。`-Force` が無い場合は、標準と同じ `CannotAppendCsvWithMismatchedPropertyNames` です

### 挙動の変更（PowerShell モジュール。パス・同じファイル・リンク）

テスト担当のブラックボックステスト（BUG-0003〜0008・0011・0015〜0017、H-17）への対応です。実装記録 10 章に、実測と決定の根拠があります。

- **別名 `FullName` を `-Path` から `-LiteralPath` に移しました**（BUG-0003）。対象は `Get-ProbedContent`・`Import-ProbedCsv`・`Set-ProbedContent`・`Add-ProbedContent`。
  `Get-ChildItem` / `Get-Item` / `New-Object System.IO.FileInfo` から渡したファイルは、`-LiteralPath` に `PSPath` か `FullName` で束縛され、名前に `[ ]` があってもそのファイルそのものを読み書きします
  （これまでは `a[1].csv` を流すと `a1.csv` を読み書きし、一致するファイルが無ければ黙って何もしませんでした）。文字列をパイプラインで渡した場合は、これまでどおり `-Path`（ワイルドカード）です
  - 6 つのコマンドの束縛の形をそろえました。`-LiteralPath` の別名は `PSPath`・`LP`・`FullName`。`Convert-ProbedContent -Path` は文字列をパイプラインから受け取るようになり、`-LiteralPath` は `FullName` でも受け取ります。
    `Resolve-Encoding -Path` は、もともとワイルドカードを展開しないので変えていません
- **`Set-ProbedContent` / `Add-ProbedContent -Path` のワイルドカードがどのファイルにも一致しないときは、非終了エラー `ItemNotFound`（ObjectNotFound）にします**（BUG-0015）。
  ID と分類は、両ホストの標準の `Set-Content` / `Add-Content` の実測と同じです。読み込み（`Get-ProbedContent`・`Import-ProbedCsv`）は、これまでどおりエラーにしません
- **同じファイルかどうかを、パスの文字列ではなくファイルの実体で比べます**（BUG-0004・0017）。Windows ではボリュームのシリアル番号とファイル ID、Linux / macOS ではリンクを解決したパスで比べます。
  - `\\?\`・`\\?\UNC\`・UNC の管理共有・`\\.\`・subst・シンボリックリンク・ジャンクション・ハードリンク・`::$DATA` の書き方でも `SamePathRoundTrip` で止まり、読み込み中のファイルを切り詰めません。
    `Get-ProbedContent | Add-ProbedContent` が終わらずにファイルが増え続けることもなくなりました
  - `Convert-ProbedContent -Destination` に変換元のフォルダーを別の書き方で渡しても、`DestinationIsSource`（N9）にします。`-Force` でも変換元を書き換えません（これまでは `DestinationExists` か、`-Force` で書き換えていました）。N8 も実体で比べます
- **1 回の呼び出しで同じ実体を 2 回指定したときの扱いを改めました**（BUG-0016）。`Set-ProbedContent` は 1 回書いたのと同じ結果にします（2 回目が追記になっていました。パスをパイプラインから受け取る場合も同じ）。
  `Add-ProbedContent` は、1 回の指定の中の重複は 1 回だけ追記し（7.x の標準の `Add-Content` と同じ）、パイプラインのレコードをまたぐ場合はレコードごとに追記します（両ホストの標準と同じ）
- **追記の整合性検査（`EncodingChangeOnAppend`）で止まった場合も、ファイルを元に戻します。** `Add-ProbedContent`・`Out-ProbedFile -Append`・`Export-ProbedCsv -Append` で、
  それまでに書いた行（前のレコードの分も）を取り消し、追記を始める前の長さに戻します（追記先が無かった場合は、作ったファイルを消します）。
  これで「エラーで止まったら、そのファイルには、その呼び出しで書いた分を残さない」にそろいました（パイプラインの停止 Ctrl+C では、これまでどおり書いた内容を残します）
- **上書きのリンク・権限の扱いを改めました**（H-17）。一時ファイルで置き換える方法は残し、次の場合だけ書き方を切り替えます（`Set-ProbedContent`・`Out-ProbedFile`・`Export-ProbedCsv`・`Convert-ProbedContent`）
  - シンボリックリンク・ジャンクションは最後までたどり、実体のフォルダーに一時ファイルを作って実体を置き換えます。リンクはそのまま残ります（これまではリンクがただのファイルに置き換わっていました）
  - ハードリンク（リンク数が 2 以上）と、フォルダーにファイルを作る権限が無い場合は、元のファイルを一時フォルダーに写してから元のファイルに直接書き、失敗したら写しから書き戻します（ハードリンクが切れていました・書けませんでした）
  - 書き込み先がリンク切れのシンボリックリンクの場合、`Set-ProbedContent` は書かずに非終了エラー `LinkTargetNotFound`（新しい ID）を報告します（標準の `Set-Content` も書きません）。
    `Out-ProbedFile`・`Export-ProbedCsv`・追記は、標準の `Out-File`・`Export-Csv`・`Add-Content` と同じく、リンクの先にファイルを作ります
  - 読み取り専用の検査と `-Force` は、リンクではなくリンクの先の実体に対して行います
  - Linux / macOS では、置き換える前に元のファイルのアクセス権（UnixFileMode）を一時ファイルに写します（所有者は写せません。ハードリンクは切れます。既知の限界）
  - `Convert-ProbedContent` の一時ファイルの名前は、ほかのコマンドと同じ `.<名前>.<8 桁の 16 進>.probedtmp` になりました（第六次 B で `.~probed<8 桁>.probedtmp` に改めました）（これまでは `.<名前>.<GUID>.tmp`）
- **書く内容と元のファイルがどちらも 1 MiB 以内なら、一時ファイルを使わず、元のファイルの中身をメモリに写してから直接書きます**（第五次の仕上げ。`Set-ProbedContent`・`Out-ProbedFile`・`Export-ProbedCsv`（`-Append` なし）・`Convert-ProbedContent`）。
  上の一時ファイルによる置き換え（`File.Replace`）で、小さなファイルへの書き込みが第四次の約 10 倍遅くなっていたためです。失敗したらメモリの写しから書き戻します（まだ無いファイルは直接作り、失敗したら消します）。
  書く内容が途中で 1 MiB を超えたら、その時点で一時ファイルによる置き換えなどに切り替えます。1 MiB 以内のハードリンクも切らずに直接書きます（写しを作りません）
  - 書いている最中にプロセスが強制終了されると、1 MiB 以内のファイルが書きかけで残ることがあります（既知の限界）
  - ほかのプロセスが開いているときに失敗する条件が変わります（置き換えは削除の共有、直接の書き込みは書き込みの共有を相手が許していないと失敗します。どちらも完了の時点）
  - まだ無いファイルは、開いた時点で（空のファイルとして）作ります。メモリにためる量は、プロセス全体で 64 MiB までです
- **`a.txt::$DATA`（主ストリームの名前）は `a.txt` として扱います**（BUG-0004）。Windows PowerShell 5.1 では生の `NotSupportedException` で止まり、7.x では同じファイルの検出をすり抜けていました。
  代替データストリーム（`a.txt:alt` など）は、両ホストでエラー `AlternateDataStreamNotSupported`（新しい ID、InvalidArgument）にします
- **`Resolve-Encoding` の `-Strategy` / `-Culture` の検証を、ほかのコマンドと同じ部品にしました**（BUG-0006）。解釈できない値は、生の `ArgumentException`（分類 NotSpecified、英語の文言）ではなく、
  `InvalidStrategy` / `InvalidCulture`（InvalidArgument、5 言語のメッセージ）の終了エラーです
- **`-Culture` に受け付ける名前を、名前の形（BCP 47）で決めます**（BUG-0007。クラスライブラリと同じ規則）。`-Culture C` / `root` は両ホストで `InvalidCulture` です（7.x では判定に進んでいました）。
  `-Culture zh-yue` は広東語（香港）として判定します（拡張言語を言語として扱う）
- `Resolve-Encoding -License` の表示の誤りを直しました（`Copyright c` → `Copyright (c)`。クラスライブラリの `EncodingProbe.License` と `LICENSE.txt` の MIT License の本文に抜けていた `THE USE OR` を、原文どおりに戻しました）。
  著作権の表記を、すべての箇所（`LICENSE.txt`・`EncodingProbe.License`・`-License`・NuGet パッケージとモジュールのアセンブリの Copyright・`.psd1`）で `Copyright (c) 2026 Motoi Tsushima` にそろえました（年が 2025 と 2026、名前が `Motoi Tsushima` と `motoi.tsushima`、記号が `(c)` と `©` で混在していました）
- ヘルプ: `-AllowEncodingChange` で `-Encoding` を省略すると判定する（追記先から継承する）ことを書きました（BUG-0011。挙動は変えていません）。
  `Resolve-Encoding -DetectionLineCount` が `us-ascii` と報告する場合は `LineBreak` も `us-ascii` として数えた値であること、`ConvertTo-DotNetEncoding` の例 2 を `Convert-Path` を使う形にしたこと（BUG-0008）、
  上書きの書き方・リンク・ほかのプロセスが開いているときに失敗する時点（完了の時点）・同じファイルの判定・代替データストリームを、注意事項に書きました

### 挙動の変更（PowerShell モジュール。第六次 A）

テスト担当のブラックボックステスト（BUG-0018・0020・0021・0031・0032・0033・0037・0042）への対応です。実装記録 11 章に、実測と決定の根拠があります。

- **`ansi` / `oem` を、スレッドのカルチャーではなく、システムのロケールの ANSI / OEM コードページにしました**（BUG-0032。1.1.0 から）。
  Windows では `GetACP` / `GetOEMCP` で、標準の 7.x の `-Encoding ansi` / `oem`、5.1 の `Default` / `Oem` と同じです。対象はすべての `-Encoding` と `ConvertTo-DotNetEncoding`、`Get-EncodingProbePlatformInfo` の `Locale`
  - システムの ANSI / OEM が 65001（「ベータ: ワールドワイド言語サポートで Unicode UTF-8 を使用」）の場合は、BOM 無しの UTF-8 として扱います
  - Linux / macOS では今までどおり、現在のカルチャー（環境変数 `LC_ALL` / `LANG` などから決まる）の値です（README と 1.3.0 仕様書 25.5 に、実測した値を書きました）
  - **`-Encoding 0` は拒否します**（使えないコードページの `1`・`2`・`3` などと同じエラー）。これまでは `Encoding.GetEncoding(0)`（実行環境の既定）になっていました
- **BOM が 2 個以上続くファイルを読むとき、取り除く BOM は先頭の 1 個だけにしました**（BUG-0021。1.1.0 から）。2 個目の U+FEFF は文字として残ります（7.x の標準の `Get-Content`・`Import-Csv` と同じ）。
  BOM 付きの語彙（`utf8BOM`・`unicodeBOM`・裸の `unicode` など）を指定した場合と、追記先から「BOM あり」と判定した場合（`Export-ProbedCsv -Append` のヘッダーの読み取り）に、2 個目の BOM まで取り除いていました。
  `-Encoding` の指定（`Auto`・BOM 付きの語彙・`NoBOM`・WebName）によらず同じ結果になり、`Get-ProbedContent -Raw` で読んで `Set-ProbedContent -EncodingFrom -NoNewline` で書き戻すと元のバイト列と一致します
  - BOM が 2 個続く CSV への `Export-ProbedCsv -Append` は、最初の列名を U+FEFF 付きで読むので、7.x の標準の `Export-Csv -Append` と同じく `CannotAppendCsvWithMismatchedPropertyNames` になります（これまでは追記していました）
- **Windows PowerShell 5.1 でも、途中で切れたファイルの末尾を、7.x と同じく置き換えの文字として読みます**（BUG-0042）。長さが奇数の UTF-16 の末尾の 1 バイト、UTF-32 の 1〜3 バイト、
  途中で切れた UTF-8 や 2 バイトのコードページの先行バイト、ISO-2022 の途中で切れたエスケープシーケンスなどを、5.1 では黙って捨てていました。
  置き換えの文字は文字エンコーディングによって違い（U+FFFD・`・`・`?` など）、両ホストで同じです（`Get-ProbedContent`・`-Raw`・`Import-ProbedCsv`・`Export-ProbedCsv -Append` の追記先の読み取り）
  - 既知の差: GB18030 の途中で切れた 4 バイトの列（`81 30` など）は、.NET Framework と .NET の復号器の違いで、置き換えの文字の数が両ホストで違います（直していません）
  - `Convert-ProbedContent` は今までどおり、不正なバイト列として変換しません（`InvalidSourceBytes`）
- **ISO-2022 の追記先が ASCII 以外の状態のまま終わっている場合は、追記の前に ASCII に戻すシーケンスを書きます**（BUG-0018）。対象は `Add-ProbedContent`・`Out-ProbedFile -Append`・`Export-ProbedCsv -Append` で、
  書く文字エンコーディングが ISO-2022-JP（50220・50221・50222）・ISO-2022-KR（50225）の場合です（追記先から継承した場合と、`-Encoding` で明示した場合）
  - ISO-2022-JP は、SO の後に SI が無ければ SI、最後の G0 の指示が `ESC ( B` 以外なら `ESC ( B` を書きます。ISO-2022-KR は SI だけです。JIS のまま CRLF で終わっている形（改行は状態を戻さない）も同じです
  - 書く順序は「戻すシーケンス → 補う改行（`Export-ProbedCsv`）→ 追記する内容」です。これまでは追記した行・レコードが 2 バイト文字として読まれて失われていました
  - 何も書かない呼び出しでは書きません。エラーで止まったら、戻すシーケンスも残しません
  - `-AllowEncodingChange` で別の文字エンコーディング（UTF-8 など）を追記する場合は、追記先の状態に手を入れません
- **CSV の列名の比較を、5.1 でも 7.x（.NET 10）の `OrdinalIgnoreCase` と同じにしました**（BUG-0020）。`ǅ` / `ǆ`、語末のシグマ `ς` / `Σ`、補助面の `𐐀` / `𐐨` などで、5.1 だけが別の列として扱っていました
  - 対象: `Import-ProbedCsv` の列名（`-Header` を含む）の重複の検査、`Export-ProbedCsv -Append` の列の照合と、照合した列の値の取り出し、`-QuoteFields` の照合
  - 5.1 では照合が通った列の値を空で書いていました（照合は大文字小文字を区別しない規則で通るのに、値は PSObject の規則で引いていたため）
- **`Set-ProbedContent` / `Add-ProbedContent` の `-Value` の配列の `$null` の要素を、標準の `Set-Content` / `Add-Content` と同じく飛ばします**（BUG-0033）。
  これまでは `$null` ごとに改行だけを書いていました。すべて `$null` なら `-Value $null` と同じく何も書きません（`Set-` は空のファイル）。パイプラインで渡した場合は、もともと標準と同じでした
- **`Convert-ProbedContent` は、UTF-7 の `System.Text.Encoding` のインスタンス（`UTF7Encoding` など、コードページが 65000 のもの）を、名前の `utf7` と同じ `EncodingNotAllowedForWrite` で拒否します**（BUG-0037）。
  これまでは受け付けて UTF-7 で書いていました。ほかの書き込みのコマンドは、もともとインスタンスも拒否していました（`ParameterArgumentTransformationError`）

### 挙動の変更（PowerShell モジュール。第六次 B）

テスト担当のブラックボックステスト（BUG-0025・0026・0027・0028・0030・0035）と、前回の引き継ぎ（ACL の時点）への対応です。実装記録 12 章、1.3.0 仕様書 26 章に、実測と決定の根拠があります。

- **同じパイプラインで、書き込み側が先に開いたファイルを読み込み側が後から開いた場合（逆向きの往復）も `SamePathRoundTrip` にします**（BUG-0028）。
  `Get-ProbedContent a.txt, z.txt | Out-ProbedFile z.txt`、`Import-ProbedCsv a.csv, z.csv | Export-ProbedCsv z.csv`、`Get-ChildItem *.txt | Get-ProbedContent | Out-ProbedFile z.txt` などです
  - 読み込み側（`Get-ProbedContent`・`Import-ProbedCsv`・`Convert-ProbedContent`）は読まずに非終了エラーを出して次へ進み、書き込み側（`Set-`・`Add-ProbedContent`・`Out-ProbedFile`・`Export-ProbedCsv`、`-Append` を含む）は書き込みを元に戻して `SamePathRoundTrip` を出します（Set / Add は非終了、Out / Export は終了エラー）。ファイルは元のままです（まだ無かったファイルは残りません）
  - **1 MiB 以内の上書きと、1 MiB を超える置き換えもエラーになります。** 第五次（1.3.0 の開発中）までは元のファイルに触れていなかったのでエラーにならず、z.txt は「a.txt の内容 + z.txt の元の内容」になっていました（1.2.0 までは z.txt の元の内容が消えていました）
  - 1 MiB を超える新しいファイルは、完了まで書き込み先が無いので、読み込み側では「ファイルが無い」ではなく `SamePathRoundTrip` にします
- **ほかのプログラムが書き込み用に開いているファイル（書き込み中のログなど）も、判定・変換のために読めます**（BUG-0026）。
  `Resolve-Encoding`・追記先や書き込み先の判定・`-EncodingFrom` の参照ファイル・`Convert-ProbedContent` の変換元を、`Get-ProbedContent` と同じ共有モードで開きます
  - それでも開けない場合は、ID の付いたエラー（`ReadFailed`・`ReadAccessDenied`。5 言語のメッセージ）です。**`Resolve-Encoding` は非終了エラーにして、残りのファイルを処理します**（これまでは生の `IOException` の終了エラーで、残りのファイルを処理しませんでした）。追記の 3 コマンドも、生の `IOException` の終了エラーではなくなりました
  - 追記（`Add-ProbedContent`・`-Append`）は、ほかのプログラムが書き込み用に開いている間は、今までどおり書けません（`WriteFailed`。5 言語のメッセージ）。7.x の標準の `Add-Content` は書きます
- **大きなファイルとメモリ不足を、ID の付いたエラーにしました**（BUG-0030）。`Resolve-Encoding` は読む前に大きさを調べ、上限（2 GB）を超えれば `-DetectionLineCount` を案内する非終了エラーにします。
  全体を読む判定のメモリ不足は `NotEnoughMemoryToDetect`、`Convert-ProbedContent` は `FileTooLargeToConvert` / `NotEnoughMemoryToConvert` です（Windows PowerShell 5.1 では生の `OutOfMemoryException` / `IOException` でした）
  - **`Resolve-Encoding` のエラーの種類が混ざります。** ファイルが無い場合と、`-DetectionLineCount` で切り出す範囲が上限を超える場合は、今までどおり終了エラーです
- **書き込みに成功したファイルに、Archive 属性を付けます**（BUG-0025）。1 MiB を超える上書き（置き換え）で、元のファイルに無かった Archive 属性が付いていませんでした（標準の `Set-Content` は付けます）。元に戻した場合は、書き込みの前の属性のままです
- **`WriteAccessDenied` のメッセージを、本モジュールの 5 言語のメッセージにしました**（BUG-0025）。.NET の例外の文面（PowerShell 7.x では英語）でした
- **隠し・システム属性のファイルへの書き込みと代替データストリーム**（BUG-0025。第五次で直っていたことを確かめました）: 書き込みの 4 コマンドと `Convert-ProbedContent` は、`-Force` なしで書け、代替データストリーム（`Zone.Identifier` など）を保ちます。
  **標準の `Out-File` / `Export-Csv` は、隠し・システム属性のファイルに書けず（`-Force` でも同じ）、代替データストリームを消すので、`Out-ProbedFile` / `Export-ProbedCsv` はこれと違います**（両ホストで実測）。標準の `Set-Content` とは同じです
- **置き換えに使う一時ファイルの名前を、元の名前によらない `.~probed<8 桁の 16 進>.probedtmp` にしました**（BUG-0027）。名前の長いファイル（7.x では名前が 218 文字以上、5.1 ではパスが 222 文字以上）に 1 MiB を超えて書けませんでした（`Convert-ProbedContent` も同じ）。
  一時ファイルのパスが長さの上限を超える場合は、元のファイルを一時フォルダーに写してから直接書きます
  - **書いている最中にプロセスが強制終了されて一時ファイルが残った場合、名前からはどのファイルのものかが分かりません**（書き込み先と同じフォルダーにあります）
- **`Out-ProbedFile` の結果が、利用者の `$PSDefaultParameterValues['Out-String:Width']` などの影響を受けないようにしました**（BUG-0035。標準の `Out-File` と同じ）。`Out-ProbedFile -Width` への既定値（`'Out-ProbedFile:Width'`・`'*:Width'`）は、今までどおり効きます
- **ACL で書き込みを拒否したファイルの `WriteAccessDenied` を、大きさによらず開く時点で出します**（第五次の仕上げでは、1 MiB 以内は完了の時点でした）。ほかのハンドルが開いているときに失敗する条件は変わりません

### 追加したコマンド

- **`Import-ProbedCsv`** … 文字エンコーディングを判定して CSV ファイルを読み込み、レコードごとにオブジェクトを出力します（標準の `Import-Csv` に対応）
  - 解析は PowerShell 7.x の `Import-Csv` から移植した処理で行い、ホストの `Import-Csv` は呼びません。
    **Windows PowerShell 5.1 でも 7.x と同じ結果になります**
  - 標準の `Import-Csv` との差異:
    - 文字エンコーディングを判定します（`-Encoding` で統一語彙・WebName・コードページを指定することもできます）
    - 5.1 でも、CR だけの改行の CSV を読みます（5.1 の標準は、エラーも警告も出さずに 0 件を返します）
    - 5.1 でも、W3C 拡張ログ形式（`#Fields:`）に対応し、`#TYPE` の型名を 7.x の順序にし、`Get-ChildItem | Import-ProbedCsv` が動きます
    - ワイルドカードが複数のファイルに一致した場合は、順に読みます（標準はエラー）
    - 存在しないファイルや列名の重複は非終了エラーとし、残りのファイルを読み続けます（標準は終了エラー）
    - ワイルドカードが 0 件の場合はエラーにしません（`Get-ProbedContent` と同じ）
- **`Export-ProbedCsv`** … オブジェクトを CSV の行に変換し、文字エンコーディング・BOM・改行を指定してファイルに書き出します（標準の `Export-Csv` に対応）
  - 文字列化は PowerShell 7.x の `Export-Csv` から移植した処理で行い、ホストの `Export-Csv` / `ConvertTo-Csv` は呼びません。
    **5.1 と 7.x でバイト単位まで同じファイルになります**（日付・数値の書式は、カルチャーとホストの .NET によって違うことがあります。ko-KR・zh-TW・zh-HK・ar-SA など。BUG-0005）
  - 文字エンコーディング・BOM・改行・追記・ファイルを開く時点・読み取り専用・`-WhatIf` は `Out-ProbedFile` の規則に従います
  - 標準の `Export-Csv` との差異:
    - 5.1 でも、既定の文字エンコーディングは `utf8NoBOM` です（5.1 の標準は ASCII で書くため、日本語が `???` になります）
    - 既定で `#TYPE` 行を書きません。`-NoTypeInformation` は受け付けるだけで何もしません
    - `-UseQuotes` / `-QuoteFields` / `-NoHeader` を 5.1 でも使えます。辞書はキーを列として書きます
    - `-Append` は追記先の文字エンコーディングと改行を継承し、整合性を検査します（標準は文字エンコーディングや改行が混ざります）
    - **追記先の最後の行に改行が無い場合は、改行を補ってから追記します**（標準は行をつなげてしまいます）
    - `Import-ProbedCsv a.csv | Export-ProbedCsv a.csv` は `SamePathRoundTrip` で止まり、ファイルを壊しません（標準は 0 バイトになります）
    - ファイルを切り詰めるのは最初の行を書く直前です（標準は開始時）
  - 2 件目以降のオブジェクトの、列に無いプロパティは黙って書きません（標準と同じ）
- 両コマンドとも、`-Culture`（文字エンコーディングの判定のカルチャー）と `-UseCulture`（区切り文字）は別のものです

### `-DetectionLineCount`

- `Resolve-Encoding` / `Import-ProbedCsv` / `Export-ProbedCsv` に、判定に使う先頭の行数を指定する `-DetectionLineCount` を追加しました。
  巨大なファイルを先頭の N 行だけで判定できます。行数は物理的な行数（CR-LF / LF / CR）で、CSV のレコード数ではありません
- `Resolve-Encoding -DetectionLineCount` は、先頭 N 行が何に見えるかをそのまま報告します。`Resolve-Encoding` の既存のパラメーターと戻り値は変えていません
- `Import-ProbedCsv` / `Export-ProbedCsv` では、先頭 N 行が ASCII だけなら BOM 無しの UTF-8 とみなし（改行のバイト列から UTF-16 / UTF-32 と分かればそちら）、
  **使う前にファイル全体を厳密に復号して検証します。** 判定を誤っていれば（先頭が英数字で後半に日本語がある Shift_JIS など）、
  何も読み書きせずにエラー `DetectionVerificationFailed` で止まります。検証のためにファイルを 2 回読みます。`-Encoding` を明示すれば、判定も検証も行いません

### ライセンス

- CSV の読み書き処理に、PowerShell（`PowerShell/PowerShell` の `CsvCommands.cs`、コミット `db1d0a56`。Copyright (c) Microsoft Corporation、MIT License）のコードの一部を改変して含めました。
  `THIRD-PARTY-NOTICES.txt` に MIT License の全文を載せ、`Resolve-Encoding -License` の出力にも追記しました。このコードは PowerShell モジュールにだけ含まれ、NuGet パッケージには含まれません

### 内部の変更

- `Out-ProbedFile` の殻（パスの解決、事前の検査、ファイルを開く時点、空の入力、後始末）を `Export-ProbedCsv` と共用するため、基底クラスに切り出しました。挙動は変えていません

## 1.2.0

文字エンコーディング判定を東アジア以外の言語に対応させ、PowerShell モジュールに
`Out-ProbedFile` と `Convert-ProbedContent` を追加しました。

### 追加したコマンド（PowerShell モジュール）

仕様は `docs/EncodingProbe-1.2.0-仕様書.md`、根拠となった標準コマンドの実測は
`docs/EncodingProbe-1.2.0-調査-Out-File挙動の実測.md` にあります。

- **`Out-ProbedFile`** … オブジェクトを整形し、統一語彙で指定した文字エンコーディング・BOM・改行でファイルに書き出します。
  標準の `Out-File` のパラメーターをすべて持ち、`-EncodingFrom` / `-LineBreak` / `-AllowEncodingChange` / `-Culture` / `-Strategy` を加えています
  - 符号化の層（エンコーディング・BOM・改行）は PowerShell 5.1 と 7.x で同じバイト列になります。
    整形の層は実行中のホストの `Out-String -Stream` に任せるため、表や一覧の形はホストによって異なります（標準の `Out-File` と同じ）
  - `-Encoding` を省略すると、上書き・新規作成は `utf8NoBOM`、`-Append` は追記先から継承します。
    明示的な `Auto` は出力先の既存ファイルから継承します（`Set-ProbedContent` の省略時とは規則が違います）
  - `-Append` は `Add-ProbedContent` と同じバイト列比較で整合性を検査し、不一致の行の手前で終了エラーにします
  - ファイルを開く（作成・切り詰める）のは最初の行を書く直前です。
    `Get-ProbedContent a.txt | Out-ProbedFile a.txt` は、標準の `Out-File` のように a.txt を読む前に消すことはなく、エラーになります
  - 標準の `Out-File` との差異: 既定のエンコーディング、`-Append` の整合性検査、切り詰める時点、
    `$null` 1 個で 0 バイト（`Out-File` は BOM だけ）、別名 `-Path` / `-LP` を PowerShell 5.1 でも提供、
    `-LiteralPath` をパイプラインから受け取らない
- **`Convert-ProbedContent`** … 既存のテキストファイルの文字エンコーディング・BOM・改行を変換して書き直します
  - 変換元に不正なバイト列がある、または変換先で表現できない文字があるファイルは変換しません（文字を失わない保証）。
    エラーメッセージに、不正なバイト列のバイト位置、または表現できない文字とその行・桁を示します
  - `-Bom Add` / `-Bom Remove` で BOM だけを変えられます。`-Bom` と組み合わせた場合、裸の `utf8` や `utf-8` を系統名として受け付けます
  - `-LineBreak` は、ファイル内のすべての改行（CR-LF / LF / CR）を揃えます（書き込み系の `-LineBreak` とは役割が違います）
  - 同じフォルダーの一時ファイルに書いてから置き換えます。変換結果が元と同じなら書き直しません（更新日時が変わりません）
  - `-Destination`（フォルダー構造は保たない）、`-SourceEncoding`、`-PassThru`、`-WhatIf` / `-Confirm` に対応します
- 両コマンドのメッセージを 5 言語で、ヘルプ（MAML）を 5 言語（と香港・マカオ向けの複製）で追加しました

### 既存コマンドの変更（PowerShell モジュール）

- **不具合の修正（標準コマンドとの食い違い）: `Set-ProbedContent` / `Add-ProbedContent` の `-Force` が、
  書き込み後に読み取り専用の属性を元に戻すようになりました。** 1.1.0 は属性を外したままにしていました。
  標準の `Set-Content` / `Add-Content` / `Out-File` はいずれも元に戻します。書き込みが途中で失敗した場合も戻します。
  読み取り専用で `-Force` なしの場合が非終了エラーであることは変えていません
- **`-LineBreak` の範囲を明文化しました（挙動は変えていません）。** `-LineBreak` が決めるのは要素の後ろに付ける改行だけで、
  値の文字列の中に含まれる改行は置き換えません。標準の `Set-Content` / `Out-File` と同じです。ヘルプ（5 言語）に明記しました
- `docs/EncodingProbe-1.1.0-仕様書.md` の 5.3 節（`-Encoding Auto` で対象が無い場合を「非終了エラー」に訂正）と
  5.6 節（`-LineBreak` の範囲）を改めました

### 判定を変えた点

- **東アジア以外のカルチャーでは、旧マルチバイトの判定を行わなくなりました。**
  独自判定が担当するのは Shift_JIS / EUC / GB / Big5 など東アジア漢字文化圏のマルチバイトだけです。
  カルチャーがそれ以外の言語圏のときは、これらの判定を実行せず UTF.Unknown に委ねます。
  カルチャー名と判定対象の対応表は `EncodingDetector.ResolveEastAsianLegacyRegion()` に集約しました
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
- **既知の限界（短文）:** 概ね 90 バイト以下の短いシングルバイト系テキストを東アジアのカルチャーで判定すると、
  誤ったエンコーディングになる、または東アジアの文字エンコーディングのまま残る場合があります。
  UTF.Unknown の短文での信頼度と精度の限界によるもので、下限の値では解決しません。
  通常の長さ（約 170 バイト以上）の文では起きないことを、テストデータに無い 17 言語で確認しています
  （`docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md`）

### 香港対応 段階 1（第二次修正）

- **カルチャー名をサブタグに分解して解釈するようになりました。**
  従来は完全一致で表を引いていたため、.NET 10（ICU）が保持する `zh-Hant-HK` や
  `zh-Hans-CN`、`zh-Hant-TW` などが判定不能になっていました。
  大文字小文字を区別せず、`_` は `-` と同じに扱います（`zh_HK` も香港）。
  中国語（`zh`）と広東語（`yue`）は、用字サブタグ → 地域サブタグ → 言語の既定の順で繁簡を決めます。
  `zh` だけのカルチャーは簡体字、`yue` は香港の繁体字として扱います
- **`kok`（コンカニ語）を韓国語と判定しなくなりました。**
  日本語・韓国語はカルチャー名の前方一致（`ja*` / `ko*`）で判定していたため、
  `kok` / `kok-IN` を韓国語と扱い、EUC-KR / CP949 の判定にかけていました。
  日本語・韓国語も言語サブタグの完全一致で判定するようにしました。
  `ja` / `ja-JP` / `ko` / `ko-KR` / `ko-KP` の結果は変わりません。
  `ja_JP` や `ja-JP_radstr` のような表記も日本語として扱います
- **香港・マカオ・広東語のカルチャーを、台湾と分けて扱うようになりました。**
  香港では EUC-TW（CNS 11643、台湾の規格）を候補にしません。
  EUC-TW のバイト列は Big5 としても成立し、両方成立時は Big5 を優先するため、判定結果は変わりません
- **繁体字と簡体字の取り違えを直しました。**
  台湾・香港カルチャーで GBK の簡体字を読むと `950 / big5`、
  大陸カルチャーで Big5 の繁体字を読むと `54936 / gb18030` と判定していました。
  `Combined` では、UTF.Unknown が独自判定と反対の系統（Big5 系 ⇔ GB 系）を
  信頼度 **0.8 以上**で返したとき、その系統の独自判定をカルチャーに関係なくやり直します
  （簡体字 → `936 / gbk`、繁体字 → `950 / big5`）。
  日本語・韓国語のコードページは対象外です
- **Big5 の後続バイトを厳密化しました。**
  `0x80–0xA0` を後続バイトとして受け入れなくなりました（Big5 / HKSCS とも存在しない範囲です）
- 台湾 Big5 と香港 Big5 はバイト列から区別しません。HKSCS 固有字を含んでいても `950 / big5` を返します。
  HKSCS 固有字は私用領域（U+E000–U+F8FF）として復号され、加工せずに書き戻せば元のバイト列に戻ります
  （`docs/私用領域の扱い_方針草案.md`）
- **既知の限界:** 大陸カルチャーで HKSCS 固有字を含む Big5 を読むと、`54936 / gb18030` のままです。
  UTF.Unknown が HKSCS 固有字の入った文書の系統を判定できないためです
- 返す値（`950 / big5`）と公開 API は変えていません

### 香港・マカオのメッセージとヘルプ（第三次修正）

- **香港・マカオのカルチャーでも、ヘルプ（`Get-Help`）が繁体字中国語で表示されるようになりました。**
  従来は `zh-HK` / `zh-MO` のフォルダーが無く、英語のヘルプになっていました（メッセージは繁体字だったため食い違っていました）。
  `zh-HK` と `zh-MO` のフォルダーに、台湾（`zh-TW`）と同じ内容を置いています
- **香港・マカオの文面は台湾版と同じ内容です。** Microsoft は Windows の zh-HK 言語パックの提供をやめ、zh-TW を案内しています。
  香港の Windows で繁体字の UI を使う場合に実際に表示されるのは台湾向けの文面であるため、それに合わせました
- **メッセージの言語を、文字エンコーディング判定と同じ規則で選ぶようになりました。**
  カルチャー名をサブタグに分解して判定し、`TwoLetterISOLanguageName` や親カルチャーのチェーンを使わなくなりました。
  - 広東語（`yue` / `yue-HK` / `yue-Hant-HK`）のメッセージが、英語から繁体字中国語になりました。`yue-CN` などは簡体字中国語です
  - `zh_HK` のメッセージが、PowerShell 7 でも 5.1 と同じ繁体字中国語になりました（従来は 7 だけ簡体字でした）
- **既知の制限:**
  - 広東語のカルチャーでは、ヘルプは英語のままです
  - PowerShell 7 では、`zh-Hant-TW` / `zh-Hant-HK` / `zh-Hant-MO` / `zh_HK` のヘルプが英語になります
    （PowerShell 5.1 はこれらの名前を `zh-TW` などに読み替えますが、7 は読み替えないためです）。
    別の課題として扱います（`docs/EncodingProbe-課題-ヘルプの用字付きカルチャー名.md`）
- 配布物（`publish/`）へのヘルプのコピー先は、`core\` と `desktop\` の下に 7 言語ぶん、計 14 か所になりました

### 1.1.0 から存在した不具合の修正（緊急修正）

- **ルーマニア語などのテキストを判定すると `NullReferenceException` が発生していたのを直しました。**
  UTF.Unknown は、.NET が提供していないエンコーディング（ルーマニア語に対する `iso-8859-16` など）を返すことがあり、
  その場合 UTF.Unknown の `Encoding` は null になります。これを読んで例外が発生し、
  `Resolve-Encoding` や `Get-ProbedContent` の利用者までそのまま届いていました。
  判定方式 `Combined` と `UtfUnknownOnly`、すべてのカルチャー、PowerShell 5.1 / 7.x のすべてで発生していました。
  **リリース済みの 1.1.0 から存在した不具合です。**
- 修正後は、.NET が非対応のエンコーディングの既存の扱いと同じく、名前（`EncodingWebName`）だけを保存し、
  `CodePage` を -1 にします。読み書き系のコマンドは判定失敗（`EncodingDetectionFailed`）の非終了エラーを報告し、
  `-Encoding` による明示指定を案内します
- 同じ扱いになる名前は、ほかに `iso-8859-10` / `viscii` / `euc-tw` / `X-ISO-10646-UCS-4-3412` / `X-ISO-10646-UCS-4-2143`、
  および .NET 10 ビルドの `utf-7` です（UTF.Unknown がこれらを返した場合）

### 手動確認後の修正（2026-09-29）

依頼（`docs/EncodingProbe-1.2.0-修正依頼-手動確認後.md`。完了後に削除。コミット `4c5b1b3` までの git の履歴に残っている）の内容と、実装の記録は `docs/EncodingProbe-1.2.0-実装記録.md` 4 章にあります。

- **不具合の修正: PowerShell 7.x の `Resolve-Encoding` などが返す `PSEncodingName` に、`I do not know.` という文字列が入っていたのを直しました。**
  独自判定の対象外のコードページ（UTF.Unknown が判定した windows-1252 / iso-8859-1 / windows-1251 など）で起きていました。
  これらはフレンドリ名が無いため、`PSEncodingName` は **null**、`UsePSName` は false になります。
  **1.0.0 から存在した不具合です**（net10.0 ビルドだけ。PowerShell 5.1 向けの net48 ビルドは従来から null）。
  独自判定のコードページ（`shift_jis` / `euc-jp` / `big5` など）の値は変わりません
- **`-WhatIf` のときも、実行すれば失敗するエラーを報告するようになりました。** ファイルを変更しない検査を `ShouldProcess` より前に置きました。
  エラーの分類（終了 / 非終了）と `FullyQualifiedErrorId` は `-WhatIf` の有無で同じです
  - `Set-ProbedContent` / `Add-ProbedContent`（**1.1.0 からの挙動の変更**）: 読み取り専用で `-Force` なし（`WriteAccessDenied`）、
    書き込み先の親ディレクトリが無い（`WriteFailed`）。エラー ID は従来と同じですが、メッセージは .NET の文言から本モジュールの文言（5 言語）になりました
  - `Out-ProbedFile`: `-NoClobber`、読み取り専用、`-EncodingFrom` の参照先の判定、出力先の既存ファイルの判定
  - `Convert-ProbedContent`: 同じ実行の中でのファイル名の重なり（N8）が `-WhatIf` でも報告されるようになりました
    （読み取り専用・出力先の同名ファイル・出力先が変換元と同じ、は従来から報告していました）
- **`Convert-ProbedContent -PassThru` に `SourceCodePage`（変換元のコードページ）を追加しました。**
  元に戻すには、Unicode 系は `SourceEncoding`、それ以外は `SourceCodePage` を `-Encoding` に渡します。
  `SourceEncoding` の WebName は別のコードページに解決される場合があり（判定結果 20932 の `euc-jp` は 51932 になる）、元に戻らないことがありました
- `Convert-ProbedContent -PassThru` の `SourceEncoding` / `Encoding` の WebName を小文字にそろえました。
  PowerShell 5.1 では EUC-JP が `EUC-JP` になり、7.x の `euc-jp` と食い違っていました
- シングルバイト系（windows-1252、ISO-8859 など）の判定の限界を、ヘルプ（判定を行う 6 コマンド、5 言語）・README・
  1.1.0 仕様書 4.4 節に明記しました。判定の成否は長さよりも内容に左右され、数百バイトあっても判定できない（`CodePage = -1`）ことがあります。
  その場合は `-Encoding` / `-SourceEncoding` で明示してください
- `Convert-ProbedContent` のヘルプに、`-WhatIf` / `-Confirm` のメッセージが変換結果が元と同じで書き直さないファイルにも出ることを明記しました（挙動は変えていません）

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
- `WorldLanguageTests` が、これらのファイルを 12 のカルチャーで判定して結果が変わらないことを検証します
- `Utf8StrictnessTests` が、UTF-8 判定と .NET の厳格なデコーダーの判断が一致することを検証します
- `CrossCheckConfidenceTests` が、短い簡体字・繁体字と HKSCS 固有字を含む Big5 が
  シングルバイトに覆されないことを検証します。
  テストデータは cp950 / cp936 を解決できない実行環境でも動くよう、
  また HKSCS 固有字は .NET のエンコーダーで作れないため、バイト列を明示して組み立てています
- `tests/PSCompat/ProbedCompatScenarios.ps1` に「世界言語の判定」の節を追加しました
- `TestData/Chinese_HongKong/` を追加しました（Big5、HKSCS 固有字入りの Big5、UTF-8）。
  HKSCS 固有字は .NET のエンコーダーで作れないため、`tools/New-EncodingTestData.ps1` がバイト列を明示して生成します。
  同スクリプトは、ヒアドキュメントの改行コードをスクリプト自体の改行コードに依存させず CRLF に揃えるようにしました
- `CultureGateTests` が、カルチャー名とカルチャー圏の対応表を検証します
- `ChineseHongKongEncodingTests`（`FutureLanguageTests.cs`）が香港のテストデータを検証します
- `CrossCheckConfidenceTests` に繁簡の系統クロスチェックのテストを追加しました
- `WorldLanguageTests` と PSCompat の世界言語の節に `zh-HK` と `kok-IN` を加えました。PSCompat には香港の節と、
  `kok-IN` で韓国語の判定を行わないことのシナリオも追加しました
- `PrivateUseAreaRoundTripTests` が、私用領域に写されるバイト列の往復（私用領域方針の付録 A）を検証します
- コアの csproj に `InternalsVisibleTo("EncodingProbe.Tests")` を追加しました（カルチャーゲートの単体テスト用）
- `MessageCatalogTests` に、言語の選択が判定のカルチャーゲートと同じ規則に従うことのテストと、
  香港・マカオ・広東語のメッセージが台湾と同じ文面であることのテスト（B 案による意図的な同一）を追加しました
- `MamlHelpTests` の対象に `zh-HK` / `zh-MO` を加え、両者が `zh-TW` とバイト単位で一致することのテストを追加しました
- PSCompat に、UI カルチャーを `zh-HK` / `zh-MO` / `yue-HK` / `zh_HK` にしたときの
  メッセージとヘルプの言語が PowerShell 5.1 / 7.x で一致することのシナリオを追加しました
  （`zh_HK` のヘルプは上記の既知の制限により比較の対象外です）
- テストデータに Spanish / Estonian / Ukrainian / Romanian / Icelandic を追加しました。
  スペイン語・エストニア語は上書き経路に入って正しく救済されること、ウクライナ語は判定不能になること、
  ルーマニア語（ISO-8859-16）は名前だけが保存されることを固定します。
  `*_short_*.txt`（アイスランド語・KOI8-R・KOI8-U の短い行）は短文での既知の限界を固定するもので、
  `KnownLimitTests` の期待値は**正しい挙動ではありません**（UTF.Unknown の改善に気づくために置いています）
- 繁体字・簡体字の長めのサンプル（`sample_big5_long.txt` / `sample_gbk_long.txt`、196 バイト）を追加しました。
  既存の東アジアのテストデータは短く、繁簡の系統クロスチェックが一度も発動していませんでした。
  `ChineseFamilyCrossCheckTestDataTests` が、zh-CN で繁体字が 950、zh-TW / zh-HK で簡体字が 936 になることを検証します
- 信頼度の下限の根拠となる測定を `docs/EncodingProbe-1.2.0-調査-クロスチェック信頼度の測定.md` に記録し、
  測定スクリプト（`tools/Measure-CrossCheckConfidence.ps1` / `tools/Invoke-CrossCheckMeasurement.ps1`）を追加しました。
  UTF.Unknown を更新したときは再測定してください
- `UnsupportedEncodingTests`（コア）と `UnsupportedUtfUnknownEncodingTests`（PowerShell 層）が、
  `iso-8859-16` のルーマニア語バイト列で例外が出ないことを検証します。
  修正前のコードでは、前者は 18 件中 17 件、後者は 12 件中 11 件が失敗します
- `OutProbedFileTests` / `ConvertProbedContentTests` を追加しました。
  `Out-ProbedFile` の組み合わせ（`-NoClobber` / `-Append` / `-Force` / 読み取り専用）とパスの扱いは、
  標準の `Out-File` の実測（実測報告の 3-1〜3-10、4-1〜4-9）と同じ結果になることを検証します
- `SetProbedContentTests` / `AddProbedContentTests` に、`-Force` 後の読み取り専用属性の検査と、
  文字列の中に改行を含む値のテスト（実測報告の 8-1〜8-6）を追加しました
- PSCompat に `Out-ProbedFile` / `Convert-ProbedContent` と既存コマンドの変更の節を追加しました

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
