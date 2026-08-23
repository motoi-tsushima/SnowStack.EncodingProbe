# SnowStack.EncodingProbe.PowerShell 1.1.0 作業引き継ぎメモ

- 最終更新: 2026-08-23（1.1.0 の作業は完了）
- 作業ブランチ: `feature/1.1.0-probed-content`（push 済み）
- **1.1.0 の作業はすべて完了しました。** 以降は、仕様書に無い挙動を足す前に
  `docs/EncodingProbe-1.1.0-仕様書.md` と本メモの「2. 確定した決定事項」を確認してください。

このメモは 1.1.0 の作業中に下した判断とその根拠を記録したものです。
**仕様書に書かれていない挙動の理由は、ほぼすべてここにあります。**
1.1.0 の挙動を変えようとする場合は、先に「2. 確定した決定事項」を読んでください。

---

## 1. 進捗

| 段階 | 内容 | 状態 |
|---|---|---|
| 第 1 段階 | 語彙解決の基盤（`EncodingSpec` / `EncodingVocabulary` / 引数変換属性 / 改行解決） | 完了 |
| （追加） | メッセージの 5 言語ローカライズ | 完了 |
| 第 2 段階 | `ConvertTo-DotNetEncoding` | 完了 |
| 第 3 段階 | `Get-ProbedContent` | 完了 |
| 第 4 段階 | `Set-ProbedContent` | 完了 |
| 第 5 段階 | `Add-ProbedContent` | 完了 |
| 仕上げ | MAML ヘルプ / `.psd1` 更新 / バージョン更新 / README / CHANGELOG | 完了 |

### Git の状態（2026-08-23 時点）

| 項目 | 状態 |
|---|---|
| 作業ブランチ | `feature/1.1.0-probed-content` … `origin/feature/1.1.0-probed-content` を追跡 |
| リモート | `origin` = `https://github.com/motoi-tsushima/SnowStack.EncodingProbe.git` |
| push | **2026-08-23 に push 済み。** GitHub 上にブランチがあり、ローカルと一致している |
| `master` | `2981455` = `origin/master`。**1.1.0 の作業はまだ 1 つも入っていない** |
| PR | **未作成。** GitHub の PR 作成 URL は `.../pull/new/feature/1.1.0-probed-content` |
| タグ | `v1.0.0` のみ。1.0.1 / 1.0.2 のタグは無く、`v1.1.0` も未作成 |

**1.1.0 は「ブランチを push しただけ」の状態。** master へのマージ、`v1.1.0` タグ、
NuGet / PowerShell Gallery への公開はいずれも未実施で、利用者の指示待ち。

#### コミット履歴（master からの差分。新しい順）

```
706dbc5 1.1.0 判定できないコードページが終了エラーになる不具合を修正
7a4721d 1.1.0 仕上げ: ヘルプ・バージョン・ドキュメントを整備
7b11068 1.1.0 第5段階: Add-ProbedContent を追加
5321be8 1.1.0 引き継ぎメモ: .cs の改行に関する記述を修正
f637e0f 1.1.0 第4段階: Set-ProbedContent を追加
1ebea01 1.1.0 作業引き継ぎメモを追加
7912447 1.1.0 第3段階の修正: 文字エンコーディングの判定をファイル全体で行う
c1fae6d 1.1.0 第3段階: Get-ProbedContent を追加
6e5cc46 1.1.0 第2段階: ConvertTo-DotNetEncoding を追加
6b3de19 1.1.0: メッセージを5言語にローカライズ
2255f3c 1.1.0 第1段階: 統一語彙の解決基盤を追加
```

このメモの更新自体もコミットしているため、履歴は上の一覧より先に進んでいることがある。
コミットハッシュは rebase / amend でも変わる。現在の状態は次で確認できる。

```bash
git branch -vv                                              # ブランチと追跡先
git status -sb                                              # 追跡状態と未コミットの変更
git log --oneline master..HEAD                              # master に入っていないコミット
git log --oneline origin/feature/1.1.0-probed-content..HEAD # 未 push のコミット（空なら一致）
```

#### 未コミットの変更

| 状態 | ファイル | 扱い |
|---|---|---|
| 削除（未ステージ） | `docs/debug_memo.txt` | **1.1.0 の作業開始前からこの状態**。意図的な削除か判断できないため触っていない |
| 未追跡 | `docs/EncodingProbe-1.1.0-仕様書.md` | 利用者の判断待ち |
| 未追跡 | `docs/EncodingProbe-1.1.0-ClaudeCode指示書.md` | 利用者の判断待ち |

本メモと `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` は追跡済み（コミット済み）。

`publish/` の DLL と MAML ヘルプは `.gitignore` で除外されているため Git の管理外。
ディスク上は 1.1.0 の Release ビルドに更新済みだが、**別の環境で clone しても入っていない**。
配布する際は Release ビルドの出力と `<culture>` フォルダーを手動で配置し直すこと。

### 現在のテスト結果

```
EncodingProbe.Tests (net10.0)            合格 64  / 失敗 0
EncodingProbe.PowerShell.Tests (net10.0) 合格 380 / 失敗 0
EncodingProbe.Tests (net48)              合格 74  / 失敗 0
PSCompat (PS 5.1 vs 7.x)                 195 シナリオ 完全一致
```

---

## 2. 確定した決定事項

### 2.1 仕様書 10 節「未決事項」の決定（利用者に確認済み）

| 項目 | 決定内容 |
|---|---|
| 混在改行の継承 | **CR-LF を含むなら CR-LF、含まないなら LF**。`LfAndCrLf` / `CrAndCrLf` / `LfAndCrAndCrLf` → CR-LF、`LfAndCr` → LF。OS に依存しない決定的な規則とする |
| 空ファイルからの継承 | **`Encoding.Default`（.NET ランタイム既定）と OS 標準の改行**。net48/PS5.1 → ANSI コードページ（BOM 無し）、net10.0/PS7.x → UTF-8（BOM 無し）。改行は `Environment.NewLine` |
| 同一パスの往復 | **同一パスを検出して Error 終了**。ただし `-Raw` は出力前にファイルを閉じるため対象外（後述） |
| `-LineBreak` の `Cr` | **追加する**（仕様書 5.1 からの変更）。`Resolve-Encoding` が `Cr` を返しうるため |

### 2.2 その他の決定・判断

| 項目 | 決定内容 | 根拠 |
|---|---|---|
| エラーメッセージの言語 | 英語・日本語・韓国語・繁体字中国語・簡体字中国語の 5 言語。`CurrentUICulture` で選択し、未対応は英語 | 利用者の指示 |
| `Resolve-Encoding` 側の既存メッセージ | 英語のまま据え置き | 指示書 1 節「変更しない」 |
| 数値コードページ経由の Unicode 系 | BOM 方針を「未指定」として扱う（書き込みでは拒否） | 利用者の承認済み |
| `ConvertTo-DotNetEncoding` の用途 | 読み取り用途として扱う（裸の `utf8` / `utf7` を受け付ける）。`Auto` のみエラー | 仕様書 7.4・7.5。**利用者への報告済み・異議なし** |
| 検出失敗・存在しないファイル | **非終了エラー**（`-ErrorAction Stop` で終了エラーにできる） | 標準 `Get-Content` の実測に合わせた。**利用者への報告済み・異議なし** |
| 文字エンコーディングの判定範囲 | **ファイル全体**。先頭の一定量に制限しない | 利用者の指示（1MiB 制限は誤判定を招くため撤廃） |

### 2.3 第 4 段階で決めたこと

| 項目 | 決定内容 | 根拠 |
|---|---|---|
| 終了エラーと非終了エラーの区別 | **パラメータの組み合わせの誤り**（`-Encoding` と `-EncodingFrom` の同時指定、`-EncodingFrom` の参照先なし）は終了エラー。**対象ファイルごとの失敗**（`-Encoding Auto` で書き込み先なし、判定失敗、読み取り専用、同一パス往復）は非終了エラー | 前者は 1 ファイルも処理できないため。後者は `-ErrorAction Stop` で終了エラーにできる |
| `-EncodingFrom` の参照先が 0 バイト | `-Encoding Auto` と同じくランタイム既定（2.1 参照） | 判定材料が無い点で同じ状況のため |
| `-EncodingFrom` のワイルドカード | **展開しない**（リテラルパスとして扱う） | 継承元は 1 つに定まる必要がある |
| `-Value` の文字列化 | `LanguagePrimitives.ConvertTo<string>` | 不変カルチャーで変換されるため PS 5.1 / 7.x で同じ結果になる |
| BOM の書き出し | `EncodingSpec.EmitBom` を見て**自分で書き出す**。StreamWriter には BOM を持たないインスタンスを渡す | `GetPreamble()` 任せにすると、解決経路によって出力が変わりうる |
| 書き込み先を開く順序 | 「同一パス検出 → エンコーディング決定 → `ShouldProcess` → ファイルを開く」 | 継承のための判定を、切り詰めの前に済ませる必要がある |
| 複数要素の書き込み | 最終要素の後ろにも改行を出力する（`-NoNewline` 指定時を除く） | 標準の `Set-Content` と同じ |

### 2.4 第 5 段階で決めたこと

| 項目 | 決定内容 | 根拠 |
|---|---|---|
| 整合性検査の単位 | **1 要素分（本文 + 改行）ごと**に比較する | 改行だけが一致しない場合（UTF-8 の LF は 1 バイト、UTF-16LE は 2 バイト）を取りこぼさないため |
| 拒否されたときの粒度 | **1 レコード分をまとめて検査してから書き込む**。途中の要素が拒否されたら、そのレコードは 1 要素も書かない | 手前の要素だけ書き込まれた中途半端な状態を残さないため |
| 拒否後の扱い | そのパスへは以後書き込まない（ライターを閉じる）。エラーは 1 回だけ報告する | パイプラインで要素が流れてくるたびに同じエラーを繰り返さないため |
| 新規作成時の BOM | **書かない**。`-Encoding utf8BOM` で新規作成しても BOM は付かない | 仕様書 6.3 が「追記では BOM 指定は無視される」と例外なく定めているため。ヘルプに明記する |
| 書き込み系での裸の `utf8` | 追記でも**束縛段階で拒否する** | 仕様書 8 節。追記では BOM 自体は無視されるが、語彙の意味は上書きと共通に保つ |
| `-AllowEncodingChange` 指定時の判定 | 既存ファイルの**判定自体を行わない**。判定できないファイルへも追記できる | 判定は突き合わせのためだけに行っている。ファイル全体を読むため、不要な読み込みも避ける |
| ISO-2022-JP への追記 | 追記部分の先頭にエスケープシーケンスが出て、末尾で ASCII に戻る。追記部分だけで完結する | `StreamWriter` が最初の書き込みでエスケープシーケンスを出力するため。テストで固定済み |

### 2.5 仕上げで決めたこと

| 項目 | 決定内容 | 根拠 |
|---|---|---|
| MAML ヘルプの言語 | **en-US と ja-JP の 2 言語**。原本は `SnowStack.EncodingProbe.PowerShell/<culture>/` | 利用者の指示。PowerShell は該当カルチャーが無ければ en-US にフォールバックする |
| ヘルプの配置 | `publish/` では `core\<culture>\` と `desktop\<culture>\` の 4 か所へコピーする | `Get-Help` はアセンブリと同じ場所のカルチャー別フォルダーを探すため。両ホストで実際に表示されることを確認済み |
| `publish/` のヘルプ | `.gitignore` で除外し、リポジトリでは管理しない | DLL と同じ扱い。原本はプロジェクト側にあり、二重管理を避ける |
| コアライブラリのバージョン | **1.1.0 に揃える**（コード変更は無い） | `CLAUDE.md` の「バージョン番号は 3 か所。上げるときはすべて揃える」に従った。配布物の DLL バージョンが食い違わないようにするため |
| `CHANGELOG.md` | 新規作成。1.1.0 を詳細に、1.0.2 / 1.0.0 はリポジトリに記録が残っている範囲で記載 | 1.0.1 の内容は記録が無いため、推測で書かず省いた |

### 2.6 リリース前に見つけた不具合の修正

利用者から「ISO-2022-JP のコードページが間違っている」という指摘を受けて調査した結果、
**50220 は正しい**ことを実測で確認した（`Encoding.GetEncoding("iso-2022-jp")` は 50220 を返し、
半角カタカナ `ESC ( I` を含むファイルも 50220 のデコーダで正しく復号できる）。
指摘は EUC-JP の 51932 / 20932 との取り違えだった。

ただし調査の過程で、1.1.0 側に実在の不具合が見つかったため修正した。

| 項目 | 内容 |
|---|---|
| 現象 | 判定処理が返したコードページを .NET が提供していない場合（ISO-2022-TW の 50229）、`Encoding.GetEncoding` の `NotSupportedException` が素通りし、対象ファイルごとの非終了エラーであるべきものが終了エラーになっていた |
| 修正 | `EncodingVocabulary.TryBuildEncoding` を追加し、`ProbedFileReader` / `EncodingInheritance` / `FromEncodingInformation` の 3 経路で戻り値として扱う |
| エラーID | `CodePageNotAvailable`（判定そのものの失敗は従来どおり `EncodingDetectionFailed`）。`EncodingDetectionException.ErrorId` で区別する |
| メッセージ | `DetectedCodePageNotAvailable` を 5 言語で追加。`-Encoding` での明示指定を案内する |
| 回帰テスト | `CmdletTests/UnavailableCodePageTests.cs`（7 件）と PSCompat の「実行環境が提供していないコードページ」の節 |

コア側に残る ISO-2022 の問題（TW が CN と誤判定される・SO/SI 形式を検出できない）は
`docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` に起票済み。**1.1.0 では対応しない。**

---

## 3. 実装済みの構成

### 3.1 製品コード（`SnowStack.EncodingProbe.PowerShell/`）

| ファイル | 役割 |
|---|---|
| `LineBreakOption.cs` | `-LineBreak` の公開列挙型（`Auto` / `CrLf` / `Lf` / `Cr`） |
| `ValidationMessages.cs` | メッセージの型付きアクセサ |
| `EncodingProbeModuleInitializer.cs` | `CodePagesProviderRegistration` を呼ぶだけに変更済み |
| `Internal/EncodingSpec.cs` | `{ Encoding, EmitBom, LineBreak }` の内部表現。`Auto` マーカーと `FromBoundParameter` を持つ |
| `Internal/EncodingUsage.cs` | `Read` / `Write` |
| `Internal/EncodingVocabulary.cs` | 語彙一覧・解決順序・BOM 方針（仕様書 3.3〜3.6） |
| `Internal/EncodingSpecTransformationAttribute.cs` | `ArgumentTransformationAttribute` 派生。束縛段階で変換・検証 |
| `Internal/LineBreakResolver.cs` | 改行の決定順序（仕様書 5.5）。混在改行の規則もここ |
| `Internal/MessageKey.cs` / `MessageCatalog.cs` | 5 言語のメッセージ表と言語判定 |
| `Internal/CodePagesProviderRegistration.cs` | `CodePagesEncodingProvider` 登録の共通化 |
| `Internal/ProbedFileReader.cs` | 判定 + BOM スキップ + ストリーミング復号 |
| `Internal/EncodingDetectionException.cs` | 判定失敗を表す内部例外 |
| `Internal/ActiveReadRegistry.cs` | 読み取り中パスの記録（同一パス往復の検出用） |
| `Internal/PathComparison.cs` | パスの比較方法（Windows では大文字小文字を区別しない） |
| `Internal/EncodingInheritance.cs` | 既存ファイルからの継承（`-EncodingFrom` / `-Encoding Auto`）。空ファイル時の既定もここ |
| `Internal/ProbedFileWriter.cs` | BOM 方針を明示した書き込み |
| `Cmdlets/ProbedContentCommandBase.cs` | パス解決の共通基底。`ResolveExistingFiles` を持つ |
| `Cmdlets/ConvertToDotNetEncodingCommand.cs` | 第 2 段階 |
| `Cmdlets/GetProbedContentCommand.cs` | 第 3 段階 |
| `Cmdlets/ProbedContentWriterCommandBase.cs` | 書き込み系に共通するパラメータ・継承・改行決定・後始末 |
| `Cmdlets/SetProbedContentCommand.cs` | 第 4 段階。上書き（差分は開き方だけ） |
| `Cmdlets/AddProbedContentCommand.cs` | 第 5 段階。追記（差分は開き方と整合性検査） |

### 3.2 テストコード（`tests/`）

| ファイル | 役割 |
|---|---|
| `EncodingProbe.PowerShell.Tests/Helpers/ByteExactFile.cs` | バイト列を明示した一時ファイル生成（仕様書 5.3 の要件） |
| `EncodingProbe.PowerShell.Tests/Helpers/ProbedCommandRunspaceFixture.cs` | コマンドレット登録済みの共有ランスペース。**新コマンドはここに登録を追加する** |
| `EncodingProbe.PowerShell.Tests/Helpers/InvocationResult.cs` | 出力と非終了エラーをまとめて受け取る |
| `EncodingProbe.PowerShell.Tests/EncodingTests/*` | 語彙解決・改行解決・ローカライズ |
| `EncodingProbe.PowerShell.Tests/CmdletTests/*` | コマンドレット単位 |
| `PSCompat/ProbedCompatScenarios.ps1` | PS 5.1 / 7.x 共通シナリオ。**新コマンドのシナリオはここに追加する** |
| `PSCompat/Invoke-ProbedCompatTests.ps1` | 両ホストで実行して突き合わせる |

---

## 4. 第 4 段階以降の作業

### 4.1 第 4 段階 — `Set-ProbedContent`（完了）

仕様書 5 節のすべてを実装済み。第 5 段階で下敷きにするため、要点だけ記す。

- パラメータ: `-Path` / `-LiteralPath` / `-Value` / `-Encoding` / `-EncodingFrom` /
  `-NoNewline` / `-LineBreak` / `-Force` / `SupportsShouldProcess`（`-NoClobber` は不採用）
- `-Encoding` には `[EncodingSpecTransformation(EncodingUsage.Write)]` を付けており、
  裸の `utf8` と `utf7` は束縛段階で拒否される（ファイルを開く前に失敗する）
- 書き込み先ごとに `ProbedFileWriter` を開き、`EndProcessing` / `StopProcessing` /
  `IDisposable.Dispose` で閉じる。パイプラインの要素が流れてくるたびに開き直さない
- 開けなかったパスも辞書に記録し、同じエラーを繰り返し報告しない

### 4.2 第 5 段階 — `Add-ProbedContent`（完了）

仕様書 6 節のすべてを実装済み。上書きとの差は次の 2 点だけで、
残りは `ProbedContentWriterCommandBase` に集約されている。

- ファイルの開き方 … `ProbedFileWriter.Append`（`FileMode.Append`、BOM は書かない）
- 書き込む内容の検査 … `RejectChunk` のオーバーライド（バイト列比較）

`-AllowEncodingChange` は `-Force` に相乗りさせていない。

### 4.3 仕上げ（完了）

- **MAML ヘルプ** … `SnowStack.EncodingProbe.PowerShell/en-US/` と `ja-JP/` に
  `SnowStack.EncodingProbe.PowerShell.dll-Help.xml` を作成した。6 コマンドすべてを収録している。
  csproj の `Content` で出力へコピーされる。指示書 6.1 の 7 項目はすべて記載済み。
  **2 言語の内容がずれないよう、片方だけ直さないこと**
- **`.psd1`** … `ModuleVersion` を 1.1.0 に、`CmdletsToExport` を 6 コマンドに、
  `ReleaseNotes` を更新した
- **バージョン** … 両 csproj の `Version` / `AssemblyVersion` / `FileVersion` を 1.1.0 に
- **`publish/`** … Release ビルドの出力とヘルプを配置した。両ホストで
  `Import-Module` → `Get-Help` → 実際の読み書きまで確認済み
- **README / CHANGELOG** … 1.1.0 の節を追加し、`CHANGELOG.md` を新規作成した

## 5. 検証コマンド

```bash
# ビルド
dotnet build SnowStack.EncodingProbe.slnx -c Debug

# 全テスト
dotnet test SnowStack.EncodingProbe.slnx

# 単一テストクラス
dotnet test tests/EncodingProbe.PowerShell.Tests/EncodingProbe.PowerShell.Tests.csproj \
  --filter "FullyQualifiedName~AddProbedContentTests"

# PowerShell 5.1 と 7.x の一致検証（最重要。指示書 5.2）
pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1
```

---

## 6. 作業上の注意点

これまでに踏んだ落とし穴です。再開時に同じ手戻りをしないよう記録します。

### 6.1 ソースファイルの文字コード

- `.cs` は **UTF-8 (BOM 無し) / 末尾改行なし**。
  改行は `.gitattributes` の `* text=auto` によりコミット時に LF へ正規化されるため、
  作業ツリー上の改行は気にしなくてよい（既存ファイルも LF になっている）
- `.ps1` は **UTF-8 (BOM 付き) / CRLF**。
  **PowerShell 5.1 は BOM 無しの `.ps1` を ANSI コードページとして読む**ため、
  BOM が無いと日本語を含む行がパースエラーになる（実際に発生した）

### 6.2 テストの書き方

- テストデータは `ByteExactFile` でバイト列を明示して作る。
  本モジュールの書き込みコードで作ると、バグが自己整合して検出できない（仕様書 5.3）
- メッセージのアサーションは部分一致ではなく `ValidationMessages.XXX(...)` との完全一致で行う。
  実行環境の言語に依存せず、かつメッセージキーと引数の埋め込みまで検証できる
- PowerShell スクリプトで検証する際、`(Get-ProbedContent $p)[0]` は
  出力が 1 行のとき**文字列の 1 文字目**を返す。`@(...)` で配列化すること（一度誤読した）
- **終了エラーは `shell.Streams.Error` に入らない**。`PowerShell.Invoke()` が
  `CmdletInvocationException` を投げるので、`Assert.Throws` で受けて
  `ErrorRecord` を検証する（非終了エラーとは受け取り方が違う）
- PSCompat のシナリオで `-Encoding ([System.Text.Encoding]::UTF8)` のような式を渡すときは
  **括弧が必須**。括弧が無いとパラメータの値が文字列リテラルとして解釈される
- ランタイム既定（`Encoding.Default`）はホストごとに異なる（net48 は ANSI、net10.0 は UTF-8）。
  PSCompat ではバイト列をそのまま記録せず、「ランタイム既定と一致すること」を記録して比較する

### 6.3 クラスライブラリ側の既知の挙動（変更しないこと）

- EUC-JP と Shift-JIS の両方に該当するバイト列は、改行コードで判定される。
  **CR-LF なら Shift-JIS、LF なら EUC-JP**。テストデータの改行に注意
- EUC-JP の判定結果は **コードページ 20932**（`.NET` の `GetEncoding("euc-jp")` が返す 51932 とは別）。
  波ダッシュや JIS X 0212 の文字で復号結果が分かれるが、常用的な日本語では一致する
- BOM 無しの UTF-16 / UTF-32 は、全バイトが 0x80 未満だと US-ASCII と判定される。
  テストデータには上位バイトが 0x80 以上になる文字（全角英字 U+FF21 など）を含める
- UTF-7 は .NET 5 以降 `Encoding.GetEncoding` から取得できない。
  `EncodingVocabulary` がコンストラクタ経由で生成している

### 6.4 その他

- `EncodingSpec` は internal のまま公開しない（指示書 9 節）
- internal な `ArgumentTransformationAttribute` は PS 5.1 / 7.x の両方で正しく機能することを確認済み
- 内部型をテストから参照するため、PowerShell 側 csproj に
  `InternalsVisibleTo("EncodingProbe.PowerShell.Tests")` を追加済み
- 公開クラスの `protected` メンバーに internal 型（`EncodingSpec` 等）は書けない。
  `private protected` にすれば書ける（派生クラスは同一アセンブリ内にあるため）
- PSCompat のシナリオで `[scriptblock]::Create` に文字列を組み立てて渡すと、
  内容によっては **AMSI にブロックされて実行されない**ことがある
  （実際に `([byte[]](0x41, 0x0A))` を含む生成コードで発生した）。
  ループ変数を束縛したいだけなら、通常のスクリプトブロックに `.GetNewClosure()` を使う