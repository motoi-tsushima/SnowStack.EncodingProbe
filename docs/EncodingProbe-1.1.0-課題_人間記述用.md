# 1.1.0 の課題 - 人間が記述する文書

- 起票日: 2026-08-26
- 対象: `SnowStack.EncodingProbe.PowerShell`（コマンドレット側）
- 状態: **対応済み（2026-08-29）**。1.1.0 で対応した

## 課題 1:カルチャー情報をオプション指定できない

### 現象

日本語WIndows上から、韓国語や繁体字中国語・簡体字中国語のテキストを `Get-ProbedContent` で表示しようとすると、文字エンコーディングの解析処理に失敗して文字化けする。

### 原因

`Resolve-Encoding` では、`-Culture`オプションでカルチャーを指定することで、外国語の文字エンコーディング解析の精度を向上させている。

しかし、`Get-ProbedContent` など、他のコマンドレットには`-Culture`オプションが存在しないため、手元のWindowsのカルチャー情報による文字エンコーディング解析処理だけしか走らない。

よって、外国語のテキストの文字エンコーディング解析処理の精度が著しく低下する。

### 対策

内部で、`SnowStack.EncodingProbe`クラスライブラリの `Detect`メソッド（文字エンコーディング解析処理）を呼び出しているコマンドレット全てに対して、`Resolve-Encoding` と同じ`-Culture`オプションを装備する必要がある。

（`Set-ProbedContent` の `-EncoidngFrom`も対象に含む。全ての機能を再確認する）

### Claude用記載欄

**対応済み（2026-08-29）**

`Get-ProbedContent` / `Set-ProbedContent` / `Add-ProbedContent` の 3 つに
`-Culture` を追加した。名前・型・受け付ける値は `Resolve-Encoding -Culture` と同一。

- 共通の基底クラス `Cmdlets/ProbedContentCommandBase` にパラメータを置いた。
  3 コマンドは元からこのクラスを継承しており、実装は 1 か所で済む
- 判定を行う経路はコマンドレット側の 2 か所しかなく、両方へ渡るようにした
  - `Internal/ProbedFileReader.Open` … `Get-ProbedContent` の判定
  - `Internal/EncodingInheritance.FromFile` … `-EncodingFrom` の参照ファイル判定と、
    書き込み系の `-Encoding` 省略時（上書き先・追記先からの継承）の判定
- **`-EncodingFrom` も対象に含まれている**（課題文書の指示どおり）。
  `-EncodingFrom` の参照ファイルは `BeginProcessing` で判定するため、
  `-Culture` の検証をその前に済ませる順序にしてある
- `ConvertTo-DotNetEncoding` にはあえて追加していない。
  このコマンドはファイルを引数に取らず、判定処理（`Detect`）を呼び出さないため、
  カルチャーを渡す先が無い

不正なカルチャー名は**ファイルを開く前に**終了エラーにする（`-Encoding` の検証と同じ方針）。
書きかけの破損ファイルを残さないためであり、`Write_InvalidDetectionOption_DoesNotTouchTargetFile`
で書き込み先が作られないことを検証している。

エラーメッセージは `MessageKey.InvalidCulture` として 5 言語ぶん追加した。

**確認した動作**（日本語 Windows 上、EUC-KR のファイル）

| 指定 | 復号結果 |
|---|---|
| `-Culture` 無し | EUC-JP と判定され文字化け |
| `-Culture ko-KR` | CP949 と判定され正しく復号 |

`-EncodingFrom` でも同様で、`-Culture ko-KR` なら CP949 のバイト列で書き出され、
`-Culture ja-JP` では韓国語を EUC-JP で符号化できず `3F`（`?`）の並びになる。

## 課題 2:判定方式をオプション指定できない

### 現象

`Get-ProbedContent`などProbed系コマンドレットで、文字エンコーディングの判定方式が選択できない。

 例えば、テキストファイルを独自実装の解析処理ではなく、UTF.Unknownだけで解析したいときには、Resolve-Encoding なら`-Strategy UtfUnknownOnly`で指定できるが、Probed系コマンドではできない。

### 原因

課題1と同根の問題で、`Resolve-Encoding`では判定方式を`-Strategy`オプションで選択できるが、 `Get-ProbedContent`などProbed系コマンドでは同様のオプションが用意されていないのが原因。

### 対策

内部で、`SnowStack.EncodingProbe`クラスライブラリの `Detect`メソッド（文字エンコーディング解析処理）を呼び出しているコマンドレット全てに対して、`Resolve-Encoding` と同じ`-Strategy`オプションを装備する必要がある。

（`Set-ProbedContent` の `-EncoidngFrom`も対象に含む。全ての機能を再確認する）

### Claude用記載欄

**対応済み（2026-08-29）**

課題 1 と同じ 3 コマンドへ `-Strategy` を追加した。通る経路も課題 1 とまったく同じである。

- 判定方式の語彙表は `Cmdlets/ResolveEncodingOptions` に集約した。
  `Resolve-Encoding` と Probed 系で表を二重に持つと、片方にだけ別名が増えて
  挙動がずれるため。`TryParseStrategy` を追加し、`ParseStrategy` はそれを呼ぶだけにした
- 語彙は `Resolve-Encoding` と完全に同一
  （`Combined` / `default` / `0`、`NativeOnly` / `native` / `1`、
  `UtfUnknownOnly` / `utfunknown` / `3`。大文字小文字は問わない）
- 小文字化を `ToLower()` から `ToLowerInvariant()` に直した。
  トルコ語環境では `'I'` が `'ı'` になり、`Combined` や `NativeOnly` を解析できなくなるため
- 不正な判定方式も、カルチャーと同じくファイルを開く前に終了エラーにする

エラーメッセージは `MessageKey.InvalidStrategy` として 5 言語ぶん追加した。

**確認した動作**（windows-1252 のドイツ語テキスト）

| 指定 | 判定結果 |
|---|---|
| `-Strategy UtfUnknownOnly` | ISO-8859-1。正しく復号 |
| `-Strategy NativeOnly -Culture ja-JP` | Shift-JIS。文字化け |

独自判定は東アジアのマルチバイトを、UTF.Unknown は欧米のシングルバイトを担当するという
分業がそのまま現れている。欧米のテキストは `UtfUnknownOnly` で読めるようになった。

## 課題 3:ヘルプに英語と日本語しか対応していない

### 現象

`Get-Help`で表示される解説文が英語と日本語しか表示されない。

文字エンコーディングの解析処理の独自実装が対応している言語（英語・日本語・韓国語・繁体字中国語（台湾華語）・簡体字中国語（大陸北京語））には対応しておくべき。 

### 原因

これは初期段階の開発方針で、第一段階では英語と日本語だけで実装した。実装確認後、現在の実装方式で他の言語へも対応する方針。

### 対策

英語・日本語・韓国語・繁体字中国語（台湾華語）・簡体字中国語（大陸北京語）にだけ、ヘルプもメッセージも対応する。

香港（繁体字広東語）だけは、後のバージョンで対応する予定なので、今回は未対応にする。

他のカルチャーの場合は、全て英語にする。

### Claude用記載欄

**対応済み（2026-08-29）**

MAML ヘルプを英語・日本語・韓国語・繁体字中国語・簡体字中国語の 5 言語にした。
`SnowStack.EncodingProbe.PowerShell/` の下に `ko-KR/` `zh-TW/` `zh-CN/` を追加し、
csproj の `Content` にも登録して出力へコピーされるようにしてある。

**フォルダー名を `ko-KR` / `zh-TW` / `zh-CN` にした理由**

`Get-Help` は UI カルチャー名のフォルダーを、親カルチャーへさかのぼりながら探す。
Windows が報告する UI カルチャー名そのもの（地域まで含む名前）を置けば、
最初に見つかるため確実である。`zh-Hant` / `zh-Hans` のような親カルチャー名を置くと、
`zh-HK`（香港）まで繁体字のヘルプを拾ってしまい、「香港は後のバージョンで対応する」
という方針に反する。そのため親カルチャー名のフォルダーは作っていない。

実機で確認した結果（PowerShell 5.1 / 7.x の両方で同じ）:

| UI カルチャー | 表示されるヘルプ |
|---|---|
| `en-US` | 英語 |
| `ja-JP` | 日本語 |
| `ko-KR` | 韓国語 |
| `zh-TW` | 繁体字中国語 |
| `zh-CN` | 簡体字中国語 |
| `zh-HK` | **英語**（今回未対応。方針どおり） |
| `zh-SG` / `ko` / `fr-FR` など | 英語 |

なお `Get-Help` の言語は、`Import-Module` の時点の `CurrentUICulture` で決まる。
読み込んだ後にカルチャーを変えても切り替わらない。

**5 言語がずれないようにした仕組み**

5 言語ぶんの XML を手作業で保守すると、片方だけにパラメータを足したり
訳を入れ忘れたりしても、その言語の環境で `Get-Help` を実行するまで気づけない。
そこで `tests/EncodingProbe.PowerShell.Tests/CmdletTests/MamlHelpTests.cs` を追加し、
次を機械的に固定した。

- 本文（`maml:para` / `maml:title`）を伏せた**骨格が 5 言語で完全に一致する**こと。
  要素の構成・属性・出現順・コード例まで一致を要求する
- 英語の原文がそのまま残っていないこと（コピーしただけのファイルを検出する）
- 公開しているコマンドレットとそのパラメータが、5 言語すべてに記載されていること

実際に ko-KR から `-Culture` の記述を 1 つ削って、テストが落ちることを確認済み。

なお、エラーメッセージ（`Internal/MessageCatalog`）は 1.1.0 の時点で既に 5 言語対応済みで、
今回追加した 2 つのメッセージも 5 言語ぶん入れてある。
`Resolve-Encoding` の既存メッセージが英語のままである点は変えていない（指示書 1 節の制約）。

---

## 3 件の対応後の状態（2026-08-31 追記）

課題 1〜3 はいずれも 1.1.0 のブランチに入っており、次で確認できる。

| 確認したいこと | 方法 |
|---|---|
| `-Culture` / `-Strategy` が付いたか | `Get-Help Get-ProbedContent -Full` の構文、または `docs/EncodingProbe-1.1.0-動作確認手順書.md` 5.10 節 |
| ヘルプが 5 言語になったか | 同手順書 5.8 節（`Import-Module` の**前に** UI カルチャーを変える） |
| 回帰が無いか | `dotnet test SnowStack.EncodingProbe.slnx` と `pwsh -NoProfile -File tests/PSCompat/Invoke-ProbedCompatTests.ps1` |

新たに見つかった課題は、この文書に節を足して記述してください。
コアの判定エンジン側の課題は `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` に分けてあります。
