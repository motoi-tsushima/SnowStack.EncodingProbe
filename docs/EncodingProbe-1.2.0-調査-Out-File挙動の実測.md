# EncodingProbe.PowerShell 1.2.0 — 調査：`Out-File` の挙動と `Set-ProbedContent` の改行処理の実測

- 作成日: 2026-09-24
- 依頼書: `docs/EncodingProbe-1.2.0-実測依頼-Out-File挙動.md`
- 作業の性質: 調査のみ。製品コードは変更していない。仕様の判断もしていない

---

## 0. 要約

依頼書の論点ごとに、主な結果を先にまとめる。詳細は各章を参照。

| 論点 | 主な結果 |
|---|---|
| 3（`-NoClobber` / `-Append` / `-Force`） | `-Append` があると `-NoClobber` は効かず、追記は成功する。`-Force` では `-NoClobber` は解除されない。`Out-File -Force` は**読み取り専用属性を書き込み後に元へ戻す**が、`Set-ProbedContent` / `Add-ProbedContent -Force` は**外したままにする**。エラーは `BeginProcessing` の時点の終了エラー。両ホストで同じ |
| 4（パス） | `-FilePath` にはワイルドカードの属性（`SupportsWildcards`）が無いが、実際には展開される。1 件に解決されれば書き込み、2 件以上なら終了エラー、0 件なら「ファイルが見つからない」終了エラー（新規作成されない）。相対パスは PowerShell の現在位置が基準。PS 7 だけ `-FilePath` に別名 `Path`、`-LiteralPath` に別名 `LP` がある |
| 5（整形の層） | `Out-File` の結果は、両ホストとも入力と `-Width` のすべての組み合わせで **「`Out-String -Stream` の各要素 + 改行」を連結したもの**と一致した。ステッパブルパイプライン（経路 C）も `Out-String -Stream` と一致した。ただし**表の整形そのものが PS 5.1 と 7.x で違う**（行末の空白、前後の空行の数、切り詰め記号、全角文字の幅の数え方、既定の幅） |
| 8（文字列の中の改行） | `Out-File` / `Set-Content` / `Out-String -Stream` はいずれも文字列の中の改行に手を加えない（`Out-String -Stream` は複数行の文字列を**分割しない**）。`Set-ProbedContent` / `Add-ProbedContent` の `-LineBreak` は**要素の後ろに付ける改行だけ**に効き、文字列の中の改行は置き換えない。両ホストで同じ |
| 6（参考：開くタイミング） | `Out-File` は `BeginProcessing` で既存ファイルを切り詰める（入力が 1 件も来なくても 0 バイトになる）。`Set-ProbedContent` は最初の入力が届くまでファイルに触れない |
| （参考）既定のエンコーディング | PS 5.1 の `Out-File` 既定は BOM 付き UTF-16LE、PS 7.x は BOM 無し UTF-8。PS 5.1 で BOM 無し UTF-8 のファイルに `-Append` すると、**UTF-16LE（BOM 無し）が混ざる**ことを確認した |

---

## 1. 測定の方法

### 1.1 成果物

| 成果物 | 場所 |
|---|---|
| 測定スクリプト | `tools/Measure-OutFileBehavior.ps1`（1 ホスト分を測って JSON に書く） |
| 両ホストの実行 | `tools/Invoke-OutFileMeasurement.ps1`（PS 5.1 と 7.x の子プロセスで上記を実行する） |
| 生の測定結果 | `tools/OutFileBehaviorResults/ps51.json`、`tools/OutFileBehaviorResults/ps7.json` |

再測定の手順:

```bash
dotnet build SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.csproj -c Debug
pwsh -NoProfile -File tools/Invoke-OutFileMeasurement.ps1
```

JSON の読み方:

- `Hex` はファイルのバイト列そのもの（`[IO.File]::ReadAllBytes` を自前で 16 進に整形したもの）。判断にはこれを使う
- `Text` は表示用で、BOM を見て復号し、CR / LF / ESC / TAB を `\r` `\n` `\e` `\t` に置き換えたもの。BOM が無いファイルは UTF-8 として復号しているため、UTF-16 が混ざったファイル（8 章）では文字化けして見える
- エラーは `ErrorKind`（`terminating` / `non-terminating` / `none`）と、`FullyQualifiedErrorId`・例外の型・`Category`・メッセージを記録している。
  メッセージには一時フォルダーのパスが含まれ、実行ごとに変わる

### 1.2 測定環境

| 項目 | PS 5.1 | PS 7.x |
|---|---|---|
| `PSVersion` | 5.1.26100.9444 | 7.6.6 |
| `PSEdition` | Desktop | Core |
| `OS` | Microsoft Windows NT 10.0.26200.0 | Microsoft Windows 10.0.26200 |
| `$Host.Name` | ConsoleHost | ConsoleHost |
| `[Console]::IsOutputRedirected` | True | True |
| `BufferSize` | 120x9001 | 120x9001 |
| `WindowSize` | 120x30 | 120x30 |
| `CurrentCulture` / `CurrentUICulture` | ja-JP / ja-JP | ja-JP / ja-JP |
| `$PSStyle.OutputRendering` の既定 | （`$PSStyle` なし） | Host |
| `[Environment]::NewLine` | 0D 0A | 0D 0A |
| 参照測定に使ったモジュール | `SnowStack.EncodingProbe.PowerShell\bin\Debug\net48\…dll` | `SnowStack.EncodingProbe.PowerShell\bin\Debug\net10.0\…dll` |

- OS: Windows 11 Pro 10.0.26200
- 参照測定のモジュールは、ブランチ `feature/1.2.0-world-language-detection` のコミット `e0fcd59` を Debug 構成でビルドしたもの（FileVersion 1.1.0.0。バージョン番号は未更新のため 1.1.0 のまま）。作業ツリーのソースに変更は無い
- 両ホストとも、Claude Code から起動した**非対話のホスト**（標準出力がリダイレクトされている）で測った
- エラーメッセージの言語: 測定時の UI カルチャーは ja-JP。PS 7.x では、PowerShell 自身のメッセージは日本語だが、.NET の例外メッセージ（`IOException` など）は英語で出た（.NET の日本語リソースが入っていないため）。PS 5.1 はどちらも日本語

### 1.3 測定しなかった環境

- **Linux / macOS の PowerShell 7.x は測定していない。** WSL の Ubuntu 24.04 はあるが pwsh が入っておらず、ソフトウェアの導入は依頼範囲外と判断した。改行の既定と幅の既定の確認は未実施
- **対話コンソールでの既定の幅**は測っていない（付録 A の手順で利用者が測る）

---

## 2. 論点 3 — `-NoClobber` / `-Append` / `-Force` の組み合わせ

既存ファイルの初期内容は BOM 付き UTF-16LE の `OLD` + CRLF（`FF FE 4F 00 4C 00 44 00 0D 00 0A 00`、12 バイト）。
書き込む値は `'NEW'`、`-Encoding unicode` を明示した。

### 2.1 観測した事実

#### 組み合わせ表（3.1）

PS 5.1 と PS 7.x で、結果・エラーの分類・ファイルの状態は**すべて一致した**（メッセージの文言だけが異なる）。

| # | 既存 | 読取専用 | パラメータ | PS 5.1 | PS 7.x | 実行後の内容 | 実行後の読取専用 |
|---|---|---|---|---|---|---|---|
| 3-1 | 無し | — | `-NoClobber` | 成功 | 成功 | `NEW` | — |
| 3-2 | 有り | 無し | `-NoClobber` | 終了エラー `NoClobber` | 同左 | `OLD`（変化なし） | 無し |
| 3-3 | 有り | 無し | `-Append -NoClobber` | **成功（追記）** | **成功（追記）** | `OLD` `NEW` | 無し |
| 3-4 | 無し | — | `-Append -NoClobber` | 成功（新規作成） | 同左 | `NEW` | — |
| 3-5 | 有り | 無し | `-Force -NoClobber` | **終了エラー `NoClobber`** | 同左 | `OLD`（変化なし） | 無し |
| 3-6 | 有り | 有り | （なし） | 終了エラー `FileOpenFailure` | 同左 | `OLD`（変化なし） | 有り |
| 3-7 | 有り | 有り | `-Force` | 成功 | 成功 | `NEW` | **有り（元に戻る）** |
| 3-8 | 有り | 有り | `-Force -NoClobber` | **終了エラー `NoClobber`** | 同左 | `OLD`（変化なし） | 有り |
| 3-9 | 有り | 有り | `-Append` | 終了エラー `FileOpenFailure` | 同左 | `OLD`（変化なし） | 有り |
| 3-10 | 有り | 有り | `-Append -Force` | 成功（追記） | 成功（追記） | `OLD` `NEW` | **有り（元に戻る）** |

エラーの詳細（`FullyQualifiedErrorId` の後半はいずれも `Microsoft.PowerShell.Commands.OutFileCommand`）:

| エラー | 例外の型 | `Category` | メッセージ（PS 5.1 / ja-JP） | メッセージ（PS 7.x / ja-JP） |
|---|---|---|---|---|
| `NoClobber`（3-2、3-5、3-8） | `System.IO.IOException` | ResourceExists | ファイル '…\target.txt' は既に存在します。 | The file '…\target.txt' already exists. |
| `FileOpenFailure`（3-6、3-9） | `System.UnauthorizedAccessException` | OpenError | パス '…\target.txt' へのアクセスが拒否されました。 | Access to the path '…\target.txt' is denied. |

依頼書で特に確認を求められた 3 点:

- **3-3:** `-Append` と `-NoClobber` を同時に指定すると、`-Append` が優先されて追記が成功する（両ホスト）
- **3-5 / 3-8:** `-Force` は `-NoClobber` を解除しない。`-NoClobber` が優先され終了エラーになる。3-8 では読み取り専用属性も触られずに残る（両ホスト）
- **3-7 / 3-10:** `-Force` で書き込んだ後、**読み取り専用属性は元に戻される**（両ホスト）

#### エラーが起きる時点（3.2）

上流のスクリプトブロックの `begin` / `process` / `end` に記録を仕込んだ。

| # | 記録された段階 | PS 5.1 | PS 7.x |
|---|---|---|---|
| 3-2（`-NoClobber`） | `upstream-begin` のみ | 終了エラー `NoClobber` | 同左 |
| 3-6（読み取り専用） | `upstream-begin` のみ | 終了エラー `FileOpenFailure` | 同左 |

上流の `process` / `end` が一度も呼ばれていないので、どちらも `Out-File` の **`BeginProcessing` で失敗している**。
エラーはパイプライン全体を止める終了エラーであり、上流の `end` ブロックは実行されない。

#### 参考測定：本モジュールと `Set-Content` / `Add-Content` の読み取り専用属性（3.3）

読み取り専用の既存ファイル（上と同じ初期内容）に対して実行した。両ホストで同じ結果。

| コマンド | エラー | 実行後の内容 | 実行後の読取専用 |
|---|---|---|---|
| `Set-ProbedContent -Encoding unicodeBOM -Force` | なし | `NEW` | **無し（外れたまま）** |
| `Set-ProbedContent -Encoding unicodeBOM`（`-Force` なし） | 非終了エラー `WriteAccessDenied`（`UnauthorizedAccessException` / PermissionDenied） | `OLD` | 有り |
| `Add-ProbedContent -Force` | なし | `OLD` `NEW` | **無し（外れたまま）** |
| `Add-ProbedContent`（`-Force` なし） | 非終了エラー `WriteAccessDenied` | `OLD` | 有り |
| `Set-Content -Encoding unicode -Force`（標準） | なし | `NEW` | **有り（元に戻る）** |
| `Add-Content -Encoding unicode -Force`（標準） | なし | `OLD` `NEW` | **有り（元に戻る）** |

- 本モジュールの実装は `Internal/ProbedFileWriter.ClearReadOnly` で属性を外し、戻す処理は無い（ソースとも一致）
- ヘルプ（MAML）の `-Force` の説明は「読み取り専用属性の付いたファイルへも書き込みます（属性を外してから書き込みます）」で、戻すかどうかは書いていない
- テスト `SetProbedContentTests.Write_ReadOnlyFile_WithForce_Succeeds` / `AddProbedContentTests.Append_ReadOnlyFile_WithForce_Succeeds` は書き込めたことだけを検査しており、書き込み後の属性は検査していない
- 標準コマンドとの違いはエラーの種類にもある。標準の `Out-File` は終了エラー、本モジュールは非終了エラー（仕様書の表「書き込み先が読み取り専用・書き込み権限なし → 非終了エラー」のとおり）

### 2.2 仕様への示唆

- `Out-ProbedFile` が `Out-File` と同じ組み合わせ規則を持つなら、「`-Append` があれば `-NoClobber` は無視」「`-Force` は `-NoClobber` を解除しない」の 2 点が標準の挙動である
- 読み取り専用属性の扱いについて、標準（`Out-File` / `Set-Content` / `Add-Content`）は 3 つとも「元に戻す」で一致しており、既存の `Set-ProbedContent` / `Add-ProbedContent` だけが「外したまま」である。
  `Out-ProbedFile` をどちらに合わせるかを決める際、既存の 2 コマンドとの不一致も同時に判断対象になる
- 標準の `Out-File` はファイルを開けない場合に `BeginProcessing` で終了エラーにする。`Out-ProbedFile` がファイルを開くのを遅らせる場合（7 章の論点 6）、エラーの時点と種類は標準と同じにはならない

---

## 3. 論点 4 — パスの扱い

### 3.1 観測した事実

#### パラメータのメタデータ（4.1）

`Out-File` の共通パラメータ以外のすべて。`SupportsWildcards` の属性は、**両ホストともどのパラメータにも付いていない**。

| パラメータ | 型（PS 5.1） | 型（PS 7.x） | 別名（PS 5.1） | 別名（PS 7.x） | 位置 | 必須 | パイプライン | 検証属性など | パラメータセット |
|---|---|---|---|---|---|---|---|---|---|
| `FilePath` | String | String | なし | **`Path`** | 0 | 必須 | — | — | ByPath |
| `LiteralPath` | String | String | `PSPath` | **`LP`**, `PSPath` | — | 必須 | ByPropertyName | — | ByLiteralPath |
| `Encoding` | **String** | **System.Text.Encoding** | — | — | 1 | — | — | 5.1: `ValidateNotNullOrEmpty`、`ValidateSet(unknown, string, unicode, bigendianunicode, utf8, utf7, utf32, ascii, default, oem)` / 7.x: `ValidateNotNullOrEmpty`、`ArgumentToEncodingTransformation`、`ArgumentEncodingCompletions` | 全セット |
| `Append` | Switch | Switch | — | — | — | — | — | — | 全セット |
| `Force` | Switch | Switch | — | — | — | — | — | — | 全セット |
| `NoClobber` | Switch | Switch | `NoOverwrite` | `NoOverwrite` | — | — | — | — | 全セット |
| `Width` | Int32 | Int32 | — | — | — | — | — | `ValidateRange(2, 2147483647)` | 全セット |
| `NoNewline` | Switch | Switch | — | — | — | — | — | — | 全セット |
| `InputObject` | PSObject | PSObject | — | — | — | — | ByValue | — | 全セット |

- 既定のパラメータセット: 両ホストとも `ByPath`（セットは `ByLiteralPath` / `ByPath`）
- `-FilePath` は `string`（配列ではない）で、`ValueFromPipelineByPropertyName` も無い。`-LiteralPath` だけがプロパティ名で受け取れる
- `-WhatIf` / `-Confirm` は両ホストに存在する（共通パラメータとして一覧から除いた）

`Set-Content`（参照用）:

| パラメータ | 型（PS 5.1） | 型（PS 7.x） | 別名（PS 5.1） | 別名（PS 7.x） | 位置 | 必須 | パイプライン | 属性 |
|---|---|---|---|---|---|---|---|---|
| `Path` | String[] | String[] | — | — | 0 | 必須（Path） | ByPropertyName | — |
| `LiteralPath` | String[] | String[] | `PSPath` | `LP`, `PSPath` | — | 必須（LiteralPath） | ByPropertyName | — |
| `Value` | Object[] | Object[] | — | — | 1 | 必須 | ByValue, ByPropertyName | `AllowNull`, `AllowEmptyCollection` |
| `Encoding` | `FileSystemCmdletProviderEncoding` | `System.Text.Encoding` | — | — | — | — | — | 7.x: `ValidateNotNullOrEmpty`、`ArgumentToEncodingTransformation`、`ArgumentEncodingCompletions` |
| `Credential` | PSCredential | PSCredential | — | — | — | — | ByPropertyName | `Credential` |
| `Exclude` / `Include` | String[] | String[] | — | — | — | — | — | — |
| `Filter` | String | String | — | — | — | — | — | — |
| `Force` / `NoNewline` / `PassThru` | Switch | Switch | — | — | — | — | — | — |
| `Stream` | String | String | — | — | — | — | — | （FileSystem プロバイダーの動的パラメータ） |
| `UseTransaction` | Switch | **なし** | `usetx` | — | — | — | — | — |
| `AsByteStream` | **なし** | Switch | — | — | — | — | — | — |

既定のパラメータセットは両ホストとも `Path`。`Set-Content` の `-Path` にも `SupportsWildcards` の属性は付いていない。

#### `-FilePath` のワイルドカード（4.2）

既存ファイルの内容は `OLD`、書き込む値は `NEW`。両ホストで結果は一致した。

| # | 状況 | 指定 | 結果（両ホスト） | 実行後のファイル |
|---|---|---|---|---|
| 4-1 | `a1.txt` がある | `-FilePath 'a?.txt'` | 成功 | `a1.txt` = `NEW`（ワイルドカードが展開された） |
| 4-2 | `a1.txt` `a2.txt` がある | `-FilePath 'a?.txt'` | 終了エラー `ReadWriteMultipleFilesNotSupported`（`PSInvalidOperationException` / InvalidArgument） | 両方とも `OLD` のまま |
| 4-3 | 該当なし | `-FilePath 'b*.txt'` | 終了エラー `FileOpenFailure`（`FileNotFoundException` / OpenError） | 何も作られない |
| 4-4 | `c[1].txt` が無い | `-FilePath 'c[1].txt'` | 終了エラー `FileOpenFailure`（`FileNotFoundException` / OpenError） | 何も作られない |
| 4-5 | `c[1].txt` が無い | `-LiteralPath 'c[1].txt'` | 成功 | `c[1].txt` が作られる |
| 4-6 | `c1.txt` がある | `-FilePath 'c[1].txt'` | 成功 | `c1.txt` = `NEW`（`[1]` が文字クラスとして解釈された） |

- 4-3 と 4-4 のメッセージは PS 5.1 が「指定されたファイルが見つかりません。」、PS 7.x が「Unable to find the specified file.」。どのパスかはメッセージに含まれない
- 4-2 のメッセージは両ホストとも「操作 "ReportMultipleFilesNotSupported" が無効なため、操作を実行できません。…」という、リソース名がそのまま出た不自然な文面だった（5 章の予想外の挙動を参照）

#### 相対パスの基準（4.3）

PowerShell の現在位置（`Push-Location`）とプロセスの現在ディレクトリ（`[Environment]::CurrentDirectory`）を別のフォルダーにして `rel.txt` を書いた。

| コマンド | PS 5.1 | PS 7.x |
|---|---|---|
| `Out-File -FilePath rel.txt` | PowerShell の現在位置に書いた | 同左 |
| `Set-Content -Path rel.txt`（参照） | PowerShell の現在位置 | 同左 |
| `Set-ProbedContent -Path rel.txt`（参照） | PowerShell の現在位置 | 同左 |

プロセスの現在ディレクトリには、どのコマンドも書かなかった。

#### 異常なパス（4.4）

| # | 状況 | 結果（両ホスト） | `FullyQualifiedErrorId` | 例外の型 / `Category` |
|---|---|---|---|---|
| 4-7 | 親ディレクトリが無い | 終了エラー。何も作られない | `FileOpenFailure` | `DirectoryNotFoundException` / OpenError |
| 4-8 | 既存のディレクトリを指す | 終了エラー | `FileOpenFailure` | `UnauthorizedAccessException` / OpenError |
| 4-9 | 空文字列 | 終了エラー（パラメータ束縛） | `ParameterArgumentValidationErrorEmptyStringNotAllowed` | `ParameterBindingValidationException` / InvalidData |
| （追加） | `$null` | 終了エラー（パラメータ束縛） | `ParameterArgumentValidationErrorNullNotAllowed` | `ParameterBindingValidationException` / InvalidData |

4-8 はメッセージが「アクセスが拒否されました」（`UnauthorizedAccessException`）であり、ディレクトリを指していることはメッセージから分からない。

### 3.2 仕様への示唆

- `Out-File -FilePath` は「メタデータ上はワイルドカード非対応だが、実際には展開し、ちょうど 1 件に解決されることを要求する」。
  `Set-ProbedContent -Path` は `string[]` でワイルドカードを展開し複数のファイルに書く設計なので、`-FilePath` の扱いを `Out-File` と `Set-ProbedContent` のどちらに寄せるかで、単一パスか複数パスかが変わる
- 解決先が 0 件のワイルドカード（4-3）と、存在しない `c[1].txt`（4-4）は、どちらも標準では**新規作成されずにエラー**になる。`-LiteralPath` でしか作れない名前がある
- 相対パスの基準は、標準・既存の本モジュールとも PowerShell の現在位置で揃っている
- PS 7 の別名 `-Path` / `-LP` を採用するかどうかは、PS 5.1 との一致を重視する本モジュールの方針と関わる（PS 5.1 の `Out-File` には `-Path` が無い）

---

## 4. 論点 5 — 整形の層

### 4.1 観測した事実

#### 経路 A・B・C の比較（5.2）

- 経路 A: `$input | Out-File -Encoding unicode [-Width N]` を読み戻した文字列
- 経路 B: `$input | Out-String -Stream [-Width N]` の各要素を `[Environment]::NewLine` で連結した文字列
- 経路 C: B と同じコマンドをステッパブルパイプラインで実行した結果

**入力 13 種 × `-Width`（省略 / 40 / 80 / 200）の全 52 通りで、両ホストとも次が成り立った。**

- **B と C は要素の数・内容とも完全に一致した**
- **A は「B の各要素の後ろに `[Environment]::NewLine` を付けて連結したもの」と一致した**（出力が 0 件のときは A も空）。
  言い換えると、`Out-File` は `Out-String -Stream` が返す 1 要素を 1 行として書き、**最後の行にも改行を付ける**。空行は空文字列の要素として返る
- 行の区切りは、整形によって作られたものはすべて CRLF（`[Environment]::NewLine`）。単独の LF / CR は出なかった

したがって A と B の差は「ファイル末尾の改行 1 個」だけで、これはどの入力でも同じだった。
一方で、**整形の結果そのものは PS 5.1 と 7.x で異なる**。次の表は `-Width` 省略時のファイル（経路 A）の形。

| # | 入力 | 行数 5.1 / 7.x | 先頭の空行 5.1 / 7.x | 末尾の空行 5.1 / 7.x | 最大行長 5.1 / 7.x | 両ホストで同一か |
|---|---|---|---|---|---|---|
| 5-1 | 文字列 3 個 | 3 / 3 | 0 / 0 | 0 / 0 | 5 / 5 | 同一 |
| 5-2 | 整数と小数 | 4 / 4 | 0 / 0 | 0 / 0 | 11 / 11 | 同一 |
| 5-3 | 3 プロパティ × 3（表） | 8 / 7 | 1 / 1 | **2 / 1** | 21 / 21 | **異なる** |
| 5-4 | 6 プロパティ × 1（一覧） | 11 / 8 | **2 / 1** | **3 / 1** | 10 / 10 | **異なる** |
| 5-5 | 200 文字超の値を持つ表 | 6 / 5 | 1 / 1 | **2 / 1** | **119 / 120** | **異なる** |
| 5-6 | 200 文字超の単一文字列 | 1 / 1 | 0 / 0 | 0 / 0 | 253 / 253 | 同一 |
| 5-7 | `$null` | 0 / 0 | — | — | — | 同一（BOM だけの 2 バイト） |
| 5-7 | 空文字列 | 1 / 1 | — | — | 0 / 0 | 同一（BOM + CRLF） |
| 5-7 | 空の配列 | 0 / 0 | — | — | — | 同一（**0 バイト**。BOM も無い） |
| 5-8 | 入れ子の配列 | 17 / 14 | 0 / 0 | **2 / 0** | 23 / 23 | **異なる** |
| 5-9 | 全角文字を含む表 | 8 / 7 | 1 / 1 | **2 / 1** | 12 / 17 | **異なる** |

表の具体例（5-3、`\r\n` は CRLF）:

```text
PS 5.1: \r\nName   Count Note    \r\n----   ----- ----    \r\nApple      1 red     \r\nBanana    22 yellow  \r\nCherry   333 dark red\r\n\r\n\r\n
PS 7.x: \r\nName   Count Note\r\n----   ----- ----\r\nApple      1 red\r\nBanana    22 yellow\r\nCherry   333 dark red\r\n\r\n
```

ホスト間で異なる点:

| 項目 | PS 5.1 | PS 7.x |
|---|---|---|
| 表の各行の末尾の空白 | 列幅まで空白で埋める | 埋めない（行末の空白なし） |
| 表の後ろの空行 | 2 行 | 1 行 |
| 一覧形式の前後の空行 | 前 2 行・後ろ 3 行 | 前 1 行・後ろ 1 行 |
| 切り詰めの記号（5-5） | `...`（ピリオド 3 個） | `…`（U+2026 の 1 文字） |
| 全角文字の幅（5-9） | **文字数**で数える（`名前` の下線が `--`、列がずれる） | **表示幅**で数える（`名前` の下線が `----`、列が揃う） |
| `-Width` 省略時の表の最大行長（非対話） | 119 | 120 |
| 一覧形式の長い値（1000 文字） | 幅 119 で折り返す | **折り返さない**（1007 文字の 1 行） |

5-9 の実例:

```text
PS 5.1: 名前    Code  / --    ----  / 日本語の値 AB    / ｶﾀｶﾅ  CDEFGH / x     全角全角全角
PS 7.x: 名前       Code / ----       ---- / 日本語の値 AB / ｶﾀｶﾅ       CDEFGH / x          全角全角全角
```

入力ごとの補足:

- 5-2（数値）: `42`、`3.14159`、`-0.5`、`1234567.891` と整形され、両ホストで一致した。カルチャーは ja-JP（小数点は `.`）。
  小数点が `.` 以外のカルチャーでは測っていないので、カルチャー依存かどうかは確認できていない（8 章）
- 5-4: プロパティが 5 個以上の `[pscustomobject]` は一覧形式（`P1 : one` の形）になった
- 5-6: 253 文字の文字列は、`-Width 40` でも切り詰め・折り返しされず 1 行のまま書かれた（両ホスト）
- 5-7: `$null` を 1 個流すと、ファイルは作られ BOM（`FF FE`）だけが書かれた。改行は書かれない。空の配列（何も流れない）では 0 バイトのファイルができた
- 5-8: `@(1, @(2, 3), @(@(4, 5), 6))` を流すと、1 階層目の要素 `@(2, 3)` は `2` `3` に展開され、`@(@(4, 5), 6)` は `@(4, 5)` が配列オブジェクトとして一覧形式（`Length : 2` など）で、`6` が値として出た。両ホストで同じ並び（空行の数だけ異なる）

#### `-Width`（5.4）

各入力の最大行長（経路 A）。`-Width` を指定したときは両ホストで一致した（表の空白埋めの有無はあるが最大行長は同じ）。

| 入力 | 省略 5.1 / 7.x | 40 | 80 | 200 |
|---|---|---|---|---|
| 5-5（長い値の表） | 119 / 120 | 40 | 80 | 200 |
| 5-6（長い文字列） | 253 / 253 | 253 | 253 | 253 |
| その他 | 幅に達しない | 同左 | 同左 | 同左 |

- 検証範囲: `0` `1` `-1` は両ホスト・`Out-File` / `Out-String` とも、パラメータ束縛の終了エラー
  `ParameterArgumentValidationError`（`ParameterBindingValidationException` / InvalidData、「最小許容範囲 2 を下回っています」）。
  `[int]::MaxValue` は両ホストとも受け付けられ、通常どおり出力された（時間・メモリの問題は起きなかった）
- `-Width` 省略時に使われた幅（非対話ホスト。`BufferSize` の幅は両ホストとも 120）:
  PS 5.1 は `Out-File` / `Out-String` とも表の最大行長 **119**、PS 7.x は **120**。**同じホストの中では `Out-File` と `Out-String` の既定の幅は一致した**
- PS 5.1 では明示した `-Width 40` / `80` / `200` で最大行長がちょうど 40 / 80 / 200 になるのに、省略時だけ 120 − 1 = 119 になった

#### `-NoNewline`（5.5）

`Out-File -NoNewline` は「`Out-String -Stream` の各要素を区切りなしで連結したもの」になった。空行（空文字列の要素）は何も残さない。

| 入力 | PS 5.1 | PS 7.x |
|---|---|---|
| 5-1 | `alphabetagamma` | 同左 |
| 5-3 | `Name   Count Note    ----   ----- ----    Apple      1 red     Banana    22 yellow  Cherry   333 dark red` | `Name   Count Note----   ----- ----Apple      1 redBanana    22 yellowCherry   333 dark red` |
| 5-4 | `P1 : oneP2 : twoP3 : threeP4 : fourP5 : fiveP6 : six` | 同左 |

5-3 は、PS 5.1 では行末の空白埋めが残るため行の境目に空白が入り、PS 7.x では行が直接つながる。

#### ANSI エスケープ（5.6）

経路 A・B・C の出力に ESC（0x1B）が含まれたかどうか。A・B・C は常に同じ結果だった。

| 入力 | PS 5.1 | PS 7.x `Host` | PS 7.x `PlainText` | PS 7.x `Ansi` |
|---|---|---|---|---|
| ESC を含む文字列 `"`e[31mred`e[0m"` | 含む（そのまま書かれる） | 含む | **含む** | 含む |
| 5-3 の表 | 含まない | 含まない | 含まない | **含む**（見出しと下線が `\e[32;1m` で装飾される） |

- `PlainText` でも、**文字列として渡したエスケープシーケンスは取り除かれずにファイルへ書かれた**。
  同じ設定で `Out-Host` に渡すと取り除かれることを別途確認した（ファイルへの書き込みでだけ残る）
- `Ansi` では、表の見出し行と下線行が `\e[32;1mName  \e[0m\e[32;1m Count\e[0m…` の形で書かれた。本体の行は装飾されない
- 既定の `Host` では、非対話ホストからのファイル出力に装飾は付かなかった

### 4.2 仕様への示唆

- 依頼書 5.1 で想定している「`Out-String -Stream` をステッパブルパイプラインで包み、得た各行に改行を付けて書く」やり方は、
  測った範囲のすべての入力・幅で、**同じホストの `Out-File` と同じ文字列**になった。末尾の改行は「各要素の後ろに改行」で再現できる
- ただし、その文字列はホストの整形系に依存するため、**PS 5.1 と 7.x で同じ入力から同じファイルにはならない**（表の空白埋め・空行の数・切り詰め記号・全角の幅・既定の幅）。
  これは `Out-File` 自体の性質で、`Out-String` を使う限り引き継ぐ。PS 5.1 と 7.x で同じ結果になることを重視するなら、整形を経由する入力は一致の対象にできない
- 文字列の入力（5-1、5-6、6 章）は整形の影響を受けず、両ホストで一致した
- 空の入力（何も流れない）と `$null` 1 個とで結果が違う（0 バイト / BOM だけ）点も `Out-File` の挙動として引き継がれる（7 章も参照）
- `-Width` の検証範囲は `ValidateRange(2, 2147483647)` で両ホスト同じ。省略時の幅はホスト（コンソールのバッファ幅）に依存し、非対話では 5.1 と 7.x で 1 ずれる
- PS 7.x では `$PSStyle.OutputRendering` が `Ansi` だと、整形された表に ESC が入る。`PlainText` でも文字列中の ESC は残る

---

## 5. 論点 8 — 文字列の中に含まれる改行

### 5.1 観測した事実（6.1 / 6.2）

すべて `-Encoding unicode`（本モジュールは `unicodeBOM`）で書いた。下の表の値は BOM を除いた内容で、`\r` `\n` は CR / LF。
**全項目で PS 5.1 と PS 7.x のバイト列は完全に一致した**ので、1 列にまとめる。

| # | 入力 | `Out-File` | `Out-String -Stream` の要素 | `Set-Content` |
|---|---|---|---|---|
| 8-1 | `a\nb` | `a\nb\r\n` | 1 個: `a\nb` | `a\nb\r\n` |
| 8-2 | `a\r\nb` | `a\r\nb\r\n` | 1 個: `a\r\nb` | `a\r\nb\r\n` |
| 8-3 | `a\rb` | `a\rb\r\n` | 1 個: `a\rb` | `a\rb\r\n` |
| 8-4 | `a\n` | `a\n\r\n` | 1 個: `a\n` | `a\n\r\n` |
| 8-5 | `a\r\n\r\nb` | `a\r\n\r\nb\r\n` | 1 個: `a\r\n\r\nb` | `a\r\n\r\nb\r\n` |
| 8-6 | `a\nb`, `c` | `a\nb\r\nc\r\n` | 2 個: `a\nb`, `c` | `a\nb\r\nc\r\n` |

- `Out-String -Stream` は、**複数行の文字列を行に分けなかった**（CR 単独でも CRLF でも 1 要素のまま）。両ホストで同じ
  （補足: 文字列ではなく、値に改行を含むプロパティを持つオブジェクトは整形されて複数の要素になる。これは別途確認した）
- `Out-File` と `Set-Content` は、文字列の中の改行をそのまま書き、要素の後ろに `[Environment]::NewLine`（CRLF）を 1 個付ける

本モジュール:

| # | 入力 | `-LineBreak` 省略 | `CrLf` | `Lf` | `Cr` | `-NoNewline` | `Add-ProbedContent -LineBreak Lf`（既存 `OLD\r\n` に追記） |
|---|---|---|---|---|---|---|---|
| 8-1 | `a\nb` | `a\nb\r\n` | `a\nb\r\n` | `a\nb\n` | `a\nb\r` | `a\nb` | `OLD\r\na\nb\n` |
| 8-2 | `a\r\nb` | `a\r\nb\r\n` | `a\r\nb\r\n` | `a\r\nb\n` | `a\r\nb\r` | `a\r\nb` | `OLD\r\na\r\nb\n` |
| 8-3 | `a\rb` | `a\rb\r\n` | `a\rb\r\n` | `a\rb\n` | `a\rb\r` | `a\rb` | `OLD\r\na\rb\n` |
| 8-4 | `a\n` | `a\n\r\n` | `a\n\r\n` | `a\n\n` | `a\n\r` | `a\n` | `OLD\r\na\n\n` |
| 8-5 | `a\r\n\r\nb` | `a\r\n\r\nb\r\n` | `a\r\n\r\nb\r\n` | `a\r\n\r\nb\n` | `a\r\n\r\nb\r` | `a\r\n\r\nb` | `OLD\r\na\r\n\r\nb\n` |
| 8-6 | `a\nb`, `c` | `a\nb\r\nc\r\n` | `a\nb\r\nc\r\n` | `a\nb\nc\n` | `a\nb\rc\r` | `a\nbc` | `OLD\r\na\nb\nc\n` |

- **`-LineBreak` は要素の後ろに付ける改行だけを決め、文字列の中の改行は置き換えない。** たとえば 8-2 に `-LineBreak Lf` を指定すると `a\r\nb\n` になり、1 つのファイルに CRLF と LF が混ざる
- `-LineBreak` 省略時（語彙名を指定したので参照情報なし）は OS 既定の CRLF で、`Out-File` / `Set-Content` と同じバイト列になった
- 8-4 のように末尾に改行を持つ文字列は、どの経路でも改行が 2 個並ぶ（空行が 1 つ増える）
- `Add-ProbedContent -LineBreak Lf` は既存の CRLF に関係なく LF を付けた（仕様書 6 節「改行の混在は許可」のとおり）

### 5.2 実装の確認（6.3）

| 項目 | 内容 |
|---|---|
| 改行を書き出している箇所 | `SnowStack.EncodingProbe.PowerShell/Cmdlets/ProbedContentWriterCommandBase.cs` の `ProbedContentWriterCommandBase.WriteValues`（395〜397 行）。1 要素ごとに `ToText(item) + target.LineBreak`（`-NoNewline` なら `ToText(item)` だけ）を作り、`Internal/ProbedFileWriter.Write` でそのまま `StreamWriter.Write` に渡す |
| 改行文字の決定 | `Internal/LineBreakResolver.Resolve`（`OpenTarget` の 303 行で呼ぶ）。`-LineBreak` の明示 → 参照情報の改行 → `Environment.NewLine` の順 |
| 文字列の中の改行に対する処理 | **無い。** `ToText` は `LanguagePrimitives.ConvertTo<string>` で文字列にするだけで、改行の置換・分割はしていない。`ProbedFileWriter` も加工しない |
| テスト | 文字列の中に改行を含む値を書くテストは**見つからなかった**。改行の関連テスト（`SetProbedContentTests.Write_ExplicitLineBreak_UsesSpecifiedCharacters`、`Write_MultipleValues_TerminatesEveryElementWithLineBreak`、`Write_NoNewline_WritesNoLineBreakAtAll`、`AddProbedContentTests.Append_DifferentLineBreak_IsAllowed` など）は、いずれも改行を含まない値を使っている |
| ヘルプ（MAML） | `-Value` は「-NoNewline を指定しない限り、最後の要素も含めて各要素の後ろに改行を出力します」、`-LineBreak` は「出力する改行コード」、`-NoNewline` は「要素間および末尾に改行を出力しません」。**文字列の中の改行をどう扱うかは書かれていない** |
| 仕様書 | `docs/EncodingProbe-1.1.0-仕様書.md` 5.6 節に「`-LineBreak`: 改行を出力するとき、どの文字を使うか」「`-NoNewline`: 末尾（および要素間）に改行を付けるか」とある。文字列の中の改行への言及は無い |

実装と測定結果は一致している（食い違いは無い）。

### 5.3 仕様への示唆

- 標準の `Out-File` / `Set-Content` / `Out-String -Stream` は、いずれも文字列の中の改行に介入しない。現在の `Set-ProbedContent` / `Add-ProbedContent` も同じ
- `-LineBreak` を持つのは本モジュールだけであり、「`-LineBreak Lf` を指定したのに CRLF が残る」という結果は、利用者から見て意図と異なる可能性がある
- `Get-ProbedContent -Raw` の内容を `-NoNewline` で書き戻す往復（仕様書 5.6 節）では `-LineBreak` が使われないため、この論点の影響を受けない。
  影響を受けるのは、`-Raw` で読んだ複数行の文字列や、改行を含む文字列を、`-NoNewline` なしで書く場合である
- `Out-ProbedFile` が `Out-String -Stream` を経由する場合、文字列の入力は分割されずに 1 要素で届く。したがって「行に分けてから改行を付ける」実装にしても、文字列の中の改行は自動的には `-LineBreak` に揃わない
- 現状の挙動（要素の後ろだけに効く）を仕様とする場合も変える場合も、ヘルプ・仕様書に明記されていない点と、テストが無い点は残る

---

## 6. （参考）論点 6 — ファイルを作成・切り詰めるタイミング

### 6.1 観測した事実

#### 切り詰めのタイミング（7.1）

上流の各段階で対象ファイルの長さを記録した（`absent` はファイルが無い）。上流は `end` で `'x'` と `'y'` を 1 個ずつ出力する。
両ホストで記録は完全に一致した。

| 段階 | `Out-File`（既存 `OLD`） | `Out-File`（ファイル無し） | `Set-ProbedContent`（既存 `OLD`） | `Set-ProbedContent`（ファイル無し） |
|---|---|---|---|---|
| 上流 `begin` | 12 | absent | 12 | absent |
| 上流 `process` | **0** | **0** | 12 | absent |
| 上流 `end`（出力前） | 0 | 0 | 12 | absent |
| 1 個目の出力の直後 | 8 | 8 | **0** | **0** |
| 2 個目の出力の直後 | 14 | 14 | 0 | 0 |
| 最終 | `x\r\ny\r\n`（BOM 付き、14 バイト） | 同左 | 同左 | 同左 |

- 上流の `begin` は `Out-File` の `BeginProcessing` より前に呼ばれ、上流の `process` は `BeginProcessing` の後に呼ばれる。
  したがって **`Out-File` は `BeginProcessing` の時点でファイルを作成・切り詰めている**（入力がまだ 1 件も無い段階）
- その時点では BOM もまだ書かれていない（0 バイト）。1 件目を書いた直後に BOM + `x` + CRLF の 8 バイトになった。`Out-File` は 1 件ごとにディスクへ書き出している
- `Set-ProbedContent` は、**最初の入力が届く（`ProcessRecord`）までファイルに触れない**。届いた時点で切り詰め、以後はバッファに溜めて閉じるときに書き出す（途中の長さは 0 のまま）

#### 空の入力（7.2）

| # | 状況 | 指定 | PS 5.1 | PS 7.x |
|---|---|---|---|---|
| 7-1 | ファイル無し | `@() \| Out-File`（`-Encoding` 省略） | 作成、0 バイト | 作成、0 バイト |
| 7-1 | ファイル無し | `@() \| Out-File -Encoding unicode` | 作成、0 バイト | 作成、0 バイト |
| 7-1 | ファイル無し | `@() \| Out-File -Encoding UTF8`（5.1）/ `utf8`（7.x） | 作成、0 バイト | 作成、0 バイト |
| 7-2 | ファイル無し | `Out-File -InputObject $null -Encoding unicode` | 作成、**`FF FE`（BOM だけ）** | 作成、**`FF FE`（BOM だけ）** |
| 7-2（追加） | ファイル無し | `Out-File -InputObject $null`（`-Encoding` 省略） | 作成、`FF FE` | 作成、0 バイト |
| 7-3 | 既存 `OLD` | `@() \| Out-File -Encoding unicode` | **0 バイトに切り詰め** | **0 バイトに切り詰め** |
| 7-3（追加） | 既存 `OLD` | `@() \| Out-File`（`-Encoding` 省略） | 0 バイト | 0 バイト |
| 7-4 | ファイル無し | `@() \| Out-File -Append -Encoding unicode` | 作成、0 バイト | 作成、0 バイト |
| 7-4（追加） | ファイル無し | `@() \| Out-File -Append`（`-Encoding` 省略） | 作成、0 バイト | 作成、0 バイト |

- 何も流れない場合、ファイルは必ず作成（既存なら切り詰め）され、**BOM は書かれない**（0 バイト）。どのエラーも出ない
- `$null` を 1 個渡すと、改行も書かずに BOM だけが書かれる。7-2 の `-Encoding` 省略で両ホストが違うのは既定のエンコーディングの違い（5.1 は UTF-16LE、7.x は BOM 無し UTF-8）による
- 5 章の 5-7 と同じ結果である

### 6.2 仕様への示唆

- `Out-File` の「`BeginProcessing` で作成・切り詰め」は、`Get-ProbedContent a.txt | Out-ProbedFile a.txt` のような同一パスの往復では、上流が読む前に内容を消す順序である
  （上流の `begin` は切り詰めより前、`process` は後に走る）。開くのを遅らせるかどうかの判断材料になる
- 一方、開くのを最初の入力まで遅らせると、7-1 / 7-3 / 7-4 の「入力 0 件でもファイルを作成・切り詰める」挙動は標準と一致しなくなる。既存の `Set-ProbedContent` は遅らせる側である（入力 0 件ならファイルを作らない、既存ファイルも触らない）
- 空の入力で BOM を書かない（0 バイト）という標準の挙動と、本モジュールの「BOM は `EncodingSpec.EmitBom` だけで決め、`ProbedFileWriter` が開いた時点で書く」方針とは、入力 0 件の場合に結果が異なりうる

---

## 7. （参考）既定のエンコーディング（8 章）

### 7.1 観測した事実

| 測定 | PS 5.1 | PS 7.x |
|---|---|---|
| `'aあ' \| Out-File`（`-Encoding` 省略）のバイト列 | `FF FE 61 00 42 30 0D 00 0A 00`（BOM 付き UTF-16LE） | `61 E3 81 82 0D 0A`（BOM 無し UTF-8） |
| BOM 無し UTF-8 の `日本語\r\n`（`E6 97 A5 E6 9C AC E8 AA 9E 0D 0A`）に `'追記' \| Out-File -Append` | `E6 97 A5 E6 9C AC E8 AA 9E 0D 0A` **`FD 8F 18 8A 0D 00 0A 00`** | `E6 97 A5 E6 9C AC E8 AA 9E 0D 0A E8 BF BD E8 A8 98 0D 0A` |

- PS 5.1 では、追記部分が **BOM 無しの UTF-16LE**（`追` = U+8FFD、`記` = U+8A18）で書かれ、UTF-8 と UTF-16LE が 1 つのファイルに混ざった。
  PS 5.1 の `Out-File -Append` は既存ファイルのエンコーディングを見ていない
- PS 7.x では既定の UTF-8 で追記され、結果として整合した（既存ファイルを見たからではなく、既定が同じだったため）

### 7.2 仕様への示唆

- 決定済みの論点 1・2（`-Encoding` 省略時は `utf8NoBOM`、`-Append` では追記先から継承）の根拠となる事実を確認した。
  PS 5.1 の標準の挙動は、追記で文字エンコーディングが混ざる実例になる

---

## 8. 予想外の挙動・測れなかった項目・測定方法の限界

### 8.1 予想外の挙動

1. **`Out-File -Force` / `Set-Content -Force` / `Add-Content -Force` は読み取り専用属性を元に戻すが、`Set-ProbedContent` / `Add-ProbedContent -Force` は外したままにする**（2 章）
2. **`-FilePath` に `SupportsWildcards` の属性が無いのにワイルドカードが展開される**（両ホスト）。`Set-Content -Path` にも属性は無い。メタデータからはワイルドカード対応かどうかを判断できない
3. 4-2（ワイルドカードが 2 件に解決）のエラーメッセージが、両ホストとも「操作 "ReportMultipleFilesNotSupported" が無効なため…」というリソース名が露出した文面だった。`FullyQualifiedErrorId` は `ReadWriteMultipleFilesNotSupported`
4. **`Out-String -Stream` は複数行の文字列を分割しない**（両ホスト）。整形されたオブジェクトの出力だけが行単位になる
5. PS 7.x の `$PSStyle.OutputRendering = 'PlainText'` でも、文字列に含まれる ESC は `Out-File` で除去されずに書かれた（`Out-Host` では除去される）
6. PS 5.1 の既定の幅は、明示指定と違って「バッファ幅 − 1」（119）だった。PS 7.x は 120
7. PS 5.1 の表整形は全角文字を 1 桁として数えるため、列がずれる。PS 7.x は表示幅で揃える
8. PS 7.x の一覧形式（`Format-List`）は、長い値を幅で折り返さなかった（1007 文字の行）。PS 5.1 は幅 119 で折り返した
9. `$null` 1 個を `Out-File` に渡すと、改行を含まず BOM だけのファイルになる。空文字列 1 個なら BOM + CRLF
10. `-ErrorVariable` には、終了エラーのとき `ErrorRecord` ではなく**例外オブジェクト**（`CmdletInvocationException`）が入った（両ホスト）。
    依頼書 2.4 の方法（`try/catch` と `-ErrorVariable` の併用）で終了エラーを判定する際、`$ev` の要素を `ErrorRecord` と決めつけると失敗する。
    スクリプトでは例外オブジェクトを別に数えて区別した（JSON の `ExceptionsInErrorVariable`）
11. PS 7.x では UI カルチャーが ja-JP でも .NET の例外メッセージが英語になり、PowerShell 自身のメッセージだけが日本語になった。PS 5.1 はどちらも日本語。
    同じエラーでも両ホストでメッセージの言語が異なる

### 8.2 測れなかった項目

| 項目 | 理由 |
|---|---|
| Linux / macOS の PowerShell 7.x（改行の既定・幅の既定） | 手元の WSL（Ubuntu 24.04）に pwsh が入っていない |
| 対話コンソールでの `-Width` 省略時の幅 | 非対話ホストしか起動できない。付録 A の手順で利用者が測る |
| 数値の整形のカルチャー依存（5-2） | 測定したカルチャーが ja-JP だけで、小数点が `.` のため区別できない |

### 8.3 測定方法の限界

- 既定の幅は、1000 文字の値を持つ表の最大行長から推定している。PS 5.1 の 119 が「幅 120 で右端 1 桁を空ける」のか「幅 119」なのかは、この方法では区別できない
- 7.1 の長さは `FileInfo.Length` で読んでいる。書き込み側のバッファに残ってディスクに出ていない分は数えない。
  `Out-File` の 0 バイトは「切り詰め済み、BOM は未書き出し」、`Set-ProbedContent` の 0 バイトは「切り詰め済み、内容はバッファ内」と解釈した
- 3.2 と 7.1 の段階の判定は、上流のスクリプトブロックの `begin` / `process` / `end` が下流の `BeginProcessing` / `ProcessRecord` とどう交互に呼ばれるかという PowerShell の実行順序を前提にしている
- 5 章の「A == B + 改行」の判定は文字列の完全一致で行った。BOM は `ReadAllText` で除いて比べた
- 測定はすべて `-Encoding unicode`（8 章を除く）で行ったため、BOM 無しの符号化や UTF-8 の場合の空入力の挙動は 7-1 の 3 通り以外は測っていない

---

## 付録 A — 利用者が対話コンソールで行う手順

Claude Code から起動したホストは非対話（出力がリダイレクトされている）ため、`-Width` 省略時の幅は対話コンソールと異なる可能性がある。
測定スクリプトには、既定の幅だけを測る `-InteractiveWidthOnly` スイッチを用意した。

### 手順

1. `dotnet build` は不要（このモードはモジュールを読み込まない）
2. Windows Terminal で PS 5.1 のタブを開き、ウィンドウを**狭く**（目安: 幅 80 桁前後）してから、リポジトリのルートで実行する

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-OutFileBehavior.ps1 -InteractiveWidthOnly
   ```

   子プロセスとして起動しても、標準出力をリダイレクトしなければ対話コンソールのまま測れる。
   すでに開いている PS 5.1 のプロンプトで `.\tools\Measure-OutFileBehavior.ps1 -InteractiveWidthOnly` を実行してもよい
3. ウィンドウを**広く**（目安: 幅 200 桁前後）して、同じコマンドをもう一度実行する
4. PS 7.x のタブで、`pwsh -NoProfile -File .\tools\Measure-OutFileBehavior.ps1 -InteractiveWidthOnly` を狭い・広いの 2 通りで実行する
5. 結果は `tools/OutFileBehaviorResults/interactive_<Edition>_<メジャー版>_w<ウィンドウ幅>.json` に書かれ、画面にも 3 つの値が表示される。下の表に書き写す

表示される値:

- `Out-File の表の最大行長` … `Out-File`（`-Width` 省略）で 1000 文字の値を持つ表を書いたときの最大行長
- `Out-String` … 同じ入力を `Out-String -Stream`（`-Width` 省略）に渡したときの最大行長
- `一覧形式` … 同じ入力を `Format-List | Out-File` で書いたときの最大行長

### 記入欄

| ホスト | ウィンドウ | `WindowSize`（JSON の `Environment`） | `BufferSize` | `Out-File` の表 | `Out-String` の表 | 一覧形式 |
|---|---|---|---|---|---|---|
| PS 5.1 | 狭い | | | | | |
| PS 5.1 | 広い | | | | | |
| PS 7.x | 狭い | | | | | |
| PS 7.x | 広い | | | | | |
| （参考）PS 5.1 非対話 | — | 120x30 | 120x9001 | 119 | 119 | 119 |
| （参考）PS 7.x 非対話 | — | 120x30 | 120x9001 | 120 | 120 | 1007 |
