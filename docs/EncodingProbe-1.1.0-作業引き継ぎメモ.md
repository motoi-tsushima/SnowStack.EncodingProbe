# SnowStack.EncodingProbe.PowerShell 1.1.0 作業引き継ぎメモ

- 最終更新: 2026-08-23
- 作業ブランチ: `feature/1.1.0-probed-content`
- **次回の再開地点: 第 5 段階（`Add-ProbedContent`）から**

このメモは作業を中断した時点の状態を記録したものです。再開時は、まず
`docs/EncodingProbe-1.1.0-仕様書.md` と `docs/EncodingProbe-1.1.0-ClaudeCode指示書.md`
を通読したうえで、本メモの「4. 第 4 段階以降の作業」に進んでください。

---

## 1. 進捗

| 段階 | 内容 | 状態 |
|---|---|---|
| 第 1 段階 | 語彙解決の基盤（`EncodingSpec` / `EncodingVocabulary` / 引数変換属性 / 改行解決） | 完了 |
| （追加） | メッセージの 5 言語ローカライズ | 完了 |
| 第 2 段階 | `ConvertTo-DotNetEncoding` | 完了 |
| 第 3 段階 | `Get-ProbedContent` | 完了 |
| 第 4 段階 | `Set-ProbedContent` | 完了 |
| **第 5 段階** | **`Add-ProbedContent`** | **未着手（ここから再開）** |
| 仕上げ | MAML ヘルプ / `.psd1` 更新 / バージョン更新 / README / CHANGELOG | 未着手 |

### コミット履歴（master からの差分）

```
（最新） 1.1.0 第4段階: Set-ProbedContent を追加
7912447 1.1.0 第3段階の修正: 文字エンコーディングの判定をファイル全体で行う
c1fae6d 1.1.0 第3段階: Get-ProbedContent を追加
6e5cc46 1.1.0 第2段階: ConvertTo-DotNetEncoding を追加
6b3de19 1.1.0: メッセージを5言語にローカライズ
2255f3c 1.1.0 第1段階: 統一語彙の解決基盤を追加
```

作業ツリーに残っている未コミットの変更（いずれも今回の作業とは無関係）:

- `docs/debug_memo.txt` の削除、`publish/.../deps.json` の変更 … 作業開始前から存在
- `CLAUDE.md` … `/init` で生成したもの。コミットするかは未定
- `docs/EncodingProbe-1.1.0-*.md` … 仕様書・指示書。未追跡のまま

### 現在のテスト結果

```
EncodingProbe.Tests (net10.0)            合格 64  / 失敗 0
EncodingProbe.PowerShell.Tests (net10.0) 合格 335 / 失敗 0
EncodingProbe.Tests (net48)              合格 74  / 失敗 0
PSCompat (PS 5.1 vs 7.x)                 145 シナリオ 完全一致
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
| `Cmdlets/SetProbedContentCommand.cs` | 第 4 段階。**第 5 段階はこれを下敷きにする** |

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

### 4.2 第 5 段階 — `Add-ProbedContent`（仕様書 6 節）

`SetProbedContentCommand` の派生として作る。差分は次のとおり。

1. `-AllowEncodingChange` を追加する。**`-Force` に相乗りさせない**（仕様書 6.2）
2. 整合性検査は**バイト列比較**で行う（仕様書 6.1）。
   「指定されたエンコーディング X で符号化した結果 == 既存のエンコーディング Y で符号化した結果」
   が成立すれば許可、しなければ Error。名前の組み合わせ表では判定しない
   - `-Encoding` を明示した場合でも、この比較のために**既存ファイルの判定は必ず走る**（仕様書 6.3）
   - `-AllowEncodingChange` でのみ回避できる
3. BOM 指定は**常に無視**する。追記でファイル途中に BOM を書くことは正しくない。
   警告も出さない（`-EncodingFrom` で BOM 付きから継承した場合に毎回鳴るため）
4. 改行の不一致は**許可**する（混在改行になるだけで読めなくなることはない）
5. `-Encoding Auto`（省略時）は追記先から継承する。追記先が無ければ Error
6. `ProbedFileWriter` に追記用のファクトリを追加する（`FileMode.Append`、BOM は書かない）
7. ISO-2022-JP への追記でエスケープシーケンスが正しく出ることを検証する。
   .NET のエンコーダは `GetBytes` ごとに状態をリセットするため壊れない

### 4.3 仕上げ

1. **MAML ヘルプ** — バイナリモジュールのためコメントベースヘルプは使えない。
   `en-US/SnowStack.EncodingProbe.PowerShell.dll-Help.xml` を新規作成し、
   csproj に出力コピー設定を追加する。現在ヘルプファイルは 1 つも存在しない。
   指示書 6.1 が「ヘルプに明記が必要」としている 7 項目を必ず書く。
   第 4 段階で確定した次の 2 点も明記が必要（仕様書 5.4 / 6.3）:
   「`-Encoding` の入力形式によって改行の決まり方が変わる」
   「追記では BOM 指定は無視される」
2. **`.psd1`** — `publish/SnowStack.EncodingProbe.PowerShell/SnowStack.EncodingProbe.PowerShell.psd1` の
   `ModuleVersion` を 1.1.0 に、`CmdletsToExport` に 4 コマンドを追加、`ReleaseNotes` を更新
3. **バージョン** — 両 csproj の `Version` / `AssemblyVersion` / `FileVersion` を 1.1.0 に
4. **README / CHANGELOG** — 指示書 6.2

---

## 5. 検証コマンド

```bash
# ビルド
dotnet build SnowStack.EncodingProbe.slnx -c Debug

# 全テスト
dotnet test SnowStack.EncodingProbe.slnx

# 単一テストクラス
dotnet test tests/EncodingProbe.PowerShell.Tests/EncodingProbe.PowerShell.Tests.csproj \
  --filter "FullyQualifiedName~SetProbedContentTests"

# PowerShell 5.1 と 7.x の一致検証（最重要。指示書 5.2）
pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1
```

---

## 6. 作業上の注意点

これまでに踏んだ落とし穴です。再開時に同じ手戻りをしないよう記録します。

### 6.1 ソースファイルの文字コード

- `.cs` は **UTF-8 (BOM 無し) / CRLF / 末尾改行なし**（`.editorconfig` と既存ファイルに合わせる）
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