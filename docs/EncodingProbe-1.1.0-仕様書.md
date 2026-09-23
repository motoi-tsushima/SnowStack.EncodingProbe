# SnowStack.EncodingProbe.PowerShell 1.1.0 新規追加機能仕様書

- 対象バージョン: 1.1.0（現行 1.0.2 からの機能追加）
- 位置づけ: 追加のみ。既存機能に対する破壊的変更は行わない
- 作成日: 2026-08-22
- 最終更新: 2026-08-31
- 状態: **実装完了。本書の内容と実装が一致していることを確認済み**

10 節「未決事項」は 3 件とも決定済みで、決定内容を本文に反映してある。
決定に至った理由は `docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md` の
「2. 確定した決定事項」にある。

---

## 1. 背景と目的

PowerShell 5.1 の標準コマンドは、`-Encoding` パラメータが `FileSystemCmdletProviderEncoding` 列挙型であるため、次の制約を持つ。

- 「BOM 無し UTF-8」をフレンドリ名で指定できない（`UTF8` は書き込み時に必ず BOM を付与する）
- Shift_JIS / EUC-JP などをフレンドリ名で指定できない（`Default` / `Oem` は「日本語環境ならたまたま CP932」に過ぎない）
- `System.Text.Encoding` インスタンスを受け付けない（PS 6.2 以降は可能）

このため `Get-Content -Encoding (Resolve-Encoding $f).PSEncodingName $f` というイディオムは、PS 5.1 において `UsePSName = False` となる場合に成立しない。

1.1.0 では、標準コマンドに橋を架けるのではなく、**PowerShell のバージョンと検出結果によらず同一の結果を得られる独自コマンド群**を提供することでこの問題を解決する。

なお読み取りに限れば、PS 5.1 でも `Get-Content -Encoding UTF8` は BOM の有無にかかわらず UTF-8 を正しくデコードする。5.1 の「UTF8 = BOM 付き」という制約が実害を持つのは書き込み側である。本仕様における読み取り側の主眼は、BOM 対応ではなく**自動判定と、フレンドリ名で表現できないエンコーディングの指定手段**にある。

---

## 2. スコープ

### 2.1 1.1.0 で追加するコマンド

| コマンド | 役割 |
|---|---|
| `Get-ProbedContent` | エンコーディングを自動判定してテキストを読み込む |
| `Set-ProbedContent` | 統一語彙でエンコーディング・BOM・改行を指定して書き込む |
| `Add-ProbedContent` | 同上（追記） |
| `ConvertTo-DotNetEncoding` | 各種入力を `System.Text.Encoding` に変換する |

### 2.2 変更しないもの

- `Resolve-Encoding` — パラメータ・戻り値ともに変更しない
- `Get-EncodingProbePlatformInfo` — 変更しない
- `EncodingInformation` 型 — プロパティを追加しない（`DotNetEncoding` は追加しない）

### 2.3 1.2.0 以降に送るもの

- `Out-ProbedFile`（整形出力）— 当面は `| Out-String | Set-ProbedContent` で代替可能
- `Convert-ProbedContent`（一括変換）— mfsr との役割分担を整理してから検討

---

## 3. 統一フレンドリ名（統一語彙）

### 3.1 方針

- 原則として PowerShell 7.x のフレンドリ名に合わせる
- 該当する名前が PS 7.x に存在しない場合のみ、独自名を定義する
- **この語彙は PS 5.1 上でも PS 7.x 上でも同一である**
- 結果として、PS 5.1 上では「本モジュールの語彙」と「PS 5.1 標準の語彙」が異なる。また `Resolve-Encoding` が返す `PSEncodingName` は実行中の PowerShell の標準語彙であるため、PS 5.1 では本モジュールの語彙と一致しない場合がある

### 3.2 設計原則

**原則 A — BOM の有無は書き込み時のみ意味を持つ**

読み取り時は、指定された語彙にかかわらず、先頭に BOM があれば常に読み飛ばす。`-Encoding utf8BOM` で BOM 無しファイルを読んでもエラーにしない。

**原則 B — 検出しうる全状態が語彙で表現できること**

`Resolve-Encoding` が返しうる状態は、すべて語彙で指定できなければならない。これが破れると `-EncodingFrom` によるラウンドトリップと、利用者が検出結果を見て手で書き写す操作が破綻する。`Resolve-Encoding` は BOM 無しの UTF-16LE / UTF-32 を検出するため、対応する語彙が必要となる。

### 3.3 語彙一覧

BOM 接尾辞を付けられるのは Unicode 系 5 系統のみとする。

| 基底名 | BOM 有 | BOM 無 | 裸名の意味 |
|---|---|---|---|
| `utf8` | `utf8BOM` | `utf8NoBOM` | BOM 無（※書き込みでは裸名を拒否） |
| `unicode` | `unicodeBOM` ★ | `unicodeNoBOM` ★ | BOM 有 |
| `bigendianunicode` | `bigendianunicodeBOM` ★ | `bigendianunicodeNoBOM` ★ | BOM 有 |
| `utf32` | `utf32BOM` ★ | `utf32NoBOM` ★ | BOM 有 |
| `bigendianutf32` | `bigendianutf32BOM` ★ | `bigendianutf32NoBOM` ★ | BOM 有 |

★ = 本モジュールの独自拡張。

PS 7.x の裸名は「UTF-8 だけ BOM 無、他は BOM 有」という不規則な体系である。互換のためこの不規則さは受け継ぐが、**全系統に明示形（`*BOM` / `*NoBOM`）を用意する**ことで、利用者が裸名を覚えずに済むようにする。ドキュメントでは明示形の使用を推奨する。

その他の語彙は次のとおり。

| 語彙 | 意味 | BOM 接尾辞 |
|---|---|---|
| `ascii` | US-ASCII | 不可 |
| `ansi` | 実行環境の ANSI コードページ | 不可 |
| `oem` | 実行環境の OEM コードページ | 不可 |
| `utf7` | UTF-7（**読み取りのみ許容・書き込み不可**） | 不可 |
| WebName（`shift_jis` `euc-jp` `iso-2022-jp` 等） | `Encoding.GetEncoding(string)` に委譲 | 不可 |
| 数値コードページ（`932` `65001` 等） | `Encoding.GetEncoding(int)` に委譲 | 不可 |
| `Auto` | 検出に委ねる（コマンドごとに意味が定まる） | 不可 |

BOM 接尾辞を付けられない語彙に接尾辞を付けた指定（`ansiBOM` 等）はエラーとする。

PS 5.1 の `Byte` / `String` / `Unknown` は `Get-Content` の特殊値であり、本モジュールの対象外とする。

### 3.4 解決順序

`-Encoding` に渡された値は、次の順序で解決する。

1. `Auto`
2. 独自拡張名（`*BOM` / `*NoBOM`）
3. PS 7.x フレンドリ名（裸名）
4. 数値 → `Encoding.GetEncoding(int)`
5. 文字列 → `Encoding.GetEncoding(string)`（WebName および .NET が持つ別名）
6. `System.Text.Encoding` インスタンス、`EncodingInformation` は型で分岐

5 を最後に置くことが重要である。`shift-jis` `sjis` `ms_kanji` といった別名は .NET の変換表が既に持っているため、**自前で別名表を持たず委譲する**。本モジュールが定義した語彙が常に優先されるため、`unicode` のように .NET 側にも存在する名前でも解釈がぶれない。

大文字小文字は区別しない（`OrdinalIgnoreCase`）。

### 3.5 BOM 方針の決定規則

`Encoding.GetEncoding("utf-8")` および `Encoding.UTF8` は BOM 付きインスタンスを返す。この挙動により、語彙で決めた BOM 方針が `GetPreamble()` 経由で上書きされることを防ぐため、次の規則を定める。

- BOM 出力の可否は**語彙側で決定し、`GetPreamble()` に依存しない**
- Unicode 系は必ずコンストラクタ（`UTF8Encoding(bool)` / `UnicodeEncoding(bool, bool)` / `UTF32Encoding(bool, bool)`）で組み立てる
- 経路 5（WebName）で解決された Unicode 系は、BOM 方針が「未指定」となる。したがって**裸の `utf8` と同じ扱い**（読み取り可・書き込み拒否）とする
- 非 Unicode 系は BOM 出力なしで固定
- **例外**: 経路 6 で `System.Text.Encoding` インスタンスを直接渡された場合のみ、そのインスタンスの `GetPreamble()` を尊重する。利用者が自ら構築した意図を尊重するため。ただし `Encoding.GetEncoding(65001)` を渡すと BOM 付きになる点は、注意事項として文書化する

### 3.6 裸の `utf8` の扱い

PS 5.1 標準では `UTF8` が BOM 付きを意味し、PS 7.x では `utf8` が BOM 無しを意味する。**標準語彙間で意味が衝突するのは `utf8` ただ一つ**である（`unicode` / `utf32` / `bigendianunicode` はどちらも BOM 有で一致する）。

| 場面 | `-Encoding utf8` の扱い |
|---|---|
| `Get-ProbedContent` | **許容**。UTF-8 として解釈し、BOM があれば読み飛ばす |
| `Set-ProbedContent` / `Add-ProbedContent` | **拒否**。`utf8NoBOM` か `utf8BOM` の明示を求める |
| 将来の `Out-` / `Convert-` 系 | 書き込み側なので同様に拒否 |

拒否は**パラメータ束縛の段階**で行う。ファイルを開く前に失敗するため、書きかけの破損ファイルが残らない。エラーメッセージには代替候補（`utf8NoBOM` / `utf8BOM`）を必ず含める。

WebName 経路で解決された Unicode 系（`"utf-8"` など）も同じ扱いとする。

### 3.7 BOM 無し UTF-16 / UTF-32 について

原則 B により語彙として定義するが、BOM 無し UTF-16 は他ツールから確実に判別できないため相互運用性が低い。**ドキュメントで非推奨と明記**する。ただし警告は出さない（`-EncodingFrom` によるラウンドトリップのたびに鳴るため）。

---

## 4. `Get-ProbedContent`

### 4.1 パラメータ

| パラメータ | 型 | 説明 |
|---|---|---|
| `-Path` | `string[]` | ワイルドカード対応。パイプライン入力対応（値・プロパティ名の両方） |
| `-LiteralPath` | `string[]` | ワイルドカード非対応 |
| `-Encoding` | 多形 | 省略時は `Auto`（対象ファイルを検出） |
| `-Raw` | switch | 行分割せず、ファイル全体を 1 個の文字列として返す |
| `-TotalCount` | `long` | 先頭 N 行のみ読み込む |
| `-Culture` | `string` | 判定に用いるカルチャー名。省略時は実行環境のカルチャー |
| `-Strategy` | `string` | 判定方式。`Combined`（既定）/ `NativeOnly` / `UtfUnknownOnly` |

`-Path` がパイプライン入力に対応することで、次が成立する。

```powershell
Get-ChildItem *.txt | Get-ProbedContent
```

### 4.2 出力

- 既定: `string`（1 行 1 オブジェクト）
- `-Raw` 指定時: `string` 1 個

### 4.3 挙動

- `-Encoding` 省略時は対象ファイルを検出する。**検出に失敗した場合は非終了エラー**とする（8 節）
- `-Encoding` を明示した場合は検出を行わない。検出の誤判定を回避する手段として機能する
- BOM は、指定された語彙にかかわらず常に読み飛ばす（原則 A）
- 複数ファイルを指定した場合、標準の `Get-Content` と同様に内容が連結される。各ファイルのエンコーディングが異なっていても、文字列に変換された時点で元のエンコーディングは意味を失うため、連結結果は正しい

### 4.4 `-Culture` / `-Strategy`

`Resolve-Encoding` と**同じ名前・同じ値**を取る。両者で語彙表を分けると、
片方にだけ別名が増えて挙動がずれるため、判定方式の解決は
`Cmdlets/ResolveEncodingOptions` に集約している。

- `-Culture` … バイト列だけでは区別できない組み合わせ（EUC-KR と CP949、
  EUC-JP と Shift-JIS など）をカルチャーで曖昧解消する。日本語環境で韓国語や
  中国語のファイルを読む場合は、対象言語のカルチャーを指定する
- `-Strategy` … `Combined`（既定）/ `NativeOnly` / `UtfUnknownOnly`。
  独自判定は東アジアのマルチバイト、UTF.Unknown は欧米のシングルバイトを担当するため、
  独自判定が誤る欧米のテキストは `UtfUnknownOnly` で読む

#### UTF.Unknown の信頼度に対する 2 つの下限（1.2.0）

`Combined` は独自判定と UTF.Unknown の両方を使う。UTF.Unknown の信頼度に対する下限は、
**役割ごとに 2 つ**あり、値が異なる。`EncodingProbe.cs` に定数として持つ。

| 下限 | 役割 | 値 |
|---|---|---|
| 採用の下限（`UtfUnknownAdoptionThreshold`） | UTF.Unknown の結果を答えとして使う。`UtfUnknownOnly` のとき、および独自判定が判定不能だったときの補完 | **0.5 超** |
| 上書きの下限（`SingleByteOverrideThreshold`） | 独自判定が返した東アジア旧マルチバイトの答えを、UTF.Unknown のシングルバイトの結果で覆す | **0.55 超** |

上書きの下限を高くしてあるのは、**独自判定がすでに出した答えを覆すには、
答えとして採用するより強い根拠を求める**という考え方による。
UTF.Unknown は短い漢字列や HKSCS 固有字を含む Big5 に対して 0.5 前後の信頼度で
シングルバイトを返すため、採用の下限だけで上書きを決めると、
0.5 をわずかに超えただけで正しい判定が覆ってしまう。

`-Strategy NativeOnly` はどちらの下限も使わない（UTF.Unknown を呼ばないため）。

`-Encoding` を明示した場合は判定を行わないため、どちらも影響しない。

#### 繁簡の系統クロスチェック（1.2.0）

独自判定の GB 系・Big5 系の判定はバイト構造の妥当性しか見ていない。
GBK の簡体字は Big5 としても、Big5 の繁体字は GB18030 としても構造が成立するため、
カルチャーだけで決めると、台湾・香港カルチャーで簡体字を読むと Big5、
大陸カルチャーで繁体字を読むと GB18030 と誤判定する。

`Combined` では、次のすべてを満たすとき、UTF.Unknown に**系統だけを投票させ**、
反対の系統の独自判定を**カルチャーに関係なく**実行し直す。成立すればその結果を返し、
成立しなければ元の独自判定の結果を返す。UTF.Unknown のコードページはそのまま使わない
（GB 系なら既存の GB2312 / GBK / GB18030 の判別規則で 936 等を決める）。

1. 独自判定の結果が Big5 系（950）または GB 系（936 / 54936 / 20936）
2. UTF.Unknown の結果が、独自判定と反対の系統
3. UTF.Unknown の信頼度が **0.8 以上**（系統上書きの下限 `ChineseFamilyOverrideThreshold`。上の 2 つとは別の定数）

日本語・韓国語のコードページは系統表に含めない。EUC-JP の 20932 / 51932 のような
同一系統の別番号の食い違いは差し替えの対象にならず、日本語・韓国語の判定結果は変わらない。

| 入力 | カルチャー | 1.1.0 | 1.2.0 |
|---|---|---|---|
| GBK 簡体字 | zh-TW / zh-HK | 950 | 936 |
| Big5 繁体字 | zh-CN | 54936 | 950 |
| Big5 + HKSCS 固有字 | zh-CN | 54936 | 54936（救済されない。下記） |
| Big5 + HKSCS 固有字 | zh-TW / zh-HK | 950 | 950 |

**既知の限界:** 大陸カルチャー（zh-CN 等）で HKSCS 固有字を含む Big5 を読むと、
GB18030（54936）のまま残る。UTF.Unknown は HKSCS 固有字が入ると系統を言えなくなり
（シングルバイトの低い信頼度か判定不能を返す）、条件 2 を満たさないためである。
香港のファイルは香港・台湾のカルチャーで読むこと。

同様に、UTF.Unknown が系統を言えない短い入力（実測では 20 バイト程度まで。標本によって異なる）も、
差し替えは起きずカルチャーによる判定のまま残る。反対の系統を返した例は無いため、
差し替えによって誤る方向の変化は起きない。

#### カルチャー名の解釈と香港 Big5（1.2.0）

カルチャー名は完全一致ではなく、言語・用字・地域のサブタグに分解して解釈する
（大文字小文字を区別せず、`_` は `-` と同じに扱う）。1.1.0 では `zh-Hant-HK` や `zh-Hans-CN` が
判定不能になっていた。中国語（`zh`）と広東語（`yue`）は、用字サブタグ → 地域サブタグ →
言語の既定（`zh` は簡体字、`yue` は繁体字）の順で繁簡を決め、繁体字のうち地域が `HK` / `MO`
（地域が無い `yue` を含む）を香港として扱う。香港では EUC-TW（台湾の規格）を候補にしない。

- **台湾 Big5 と香港 Big5 はバイト列から区別しない。** HKSCS は Big5 の上位互換であり、
  共通漢字だけで書かれた文書には産地を示す情報が無い。拡張字が無ければどちらで復号しても同じ文字列になる。
  香港カルチャーでも、HKSCS 固有字を含んでいても `950 / big5` を返す
- **HKSCS 固有字は私用領域（U+E000–U+F8FF）として復号される。**
  .NET には HKSCS のデコーダーが無く、本モジュールは私用領域に写されるバイト列に介入しない
  （`docs/私用領域の扱い_方針草案.md`）。加工せずに同じ文字エンコーディングで書き戻せばバイト列は保存される
- **往復を保証するのは `CodePage` であり、`EncodingWebName` ではない。**
  たとえば `euc-jp` という名前で `Encoding.GetEncoding` を呼ぶと、判定結果の 20932 ではなく 51932 が返る。
  `big5-hkscs` という名前も .NET 上は 950 の別名にすぎない

解釈できない値は**ファイルを開く前**に Error 終了とする。`-Encoding` の検証と同じ方針で、
書きかけの破損ファイルを残さないためである（書き込み系で特に重要）。

`ConvertTo-DotNetEncoding` には追加しない。ファイルを引数に取らず、
判定処理そのものを呼び出さないためである。

### 4.5 採用しないパラメータと理由

| パラメータ | 理由 |
|---|---|
| `-Delimiter` | `-Raw` で読んでから `-split` すれば同じことができ、正規表現が使える分そちらが強力 |
| `-Tail` | 末尾からの逆方向読み取りとエンコーディング判定の組み合わせは実装が重い。全行読んでから末尾 N 件を返すなら `\| Select-Object -Last N` と変わらない |
| `-Wait` / `-Stream` / `-ReadCount` | 完全互換を目指さない方針のため |
| FileSystem 以外のプロバイダー対応 | 同上 |
| `-NoNewline` | `Get-Content` には存在せず、読み取り側で意味を持たない |

`-Raw` は利便性ではなく**情報保全**のために必須とする。行分割すると、行末が CRLF か LF か、末尾に改行があったかが失われ、後から復元できない。読み込んだ内容を加工して書き戻す用途では、`-Raw` が唯一の無損失な読み取り手段となる。

---

## 5. `Set-ProbedContent` / `Add-ProbedContent`

### 5.1 パラメータ

| パラメータ | 型 | 説明 |
|---|---|---|
| `-Path` | `string[]` | ワイルドカード対応。パイプライン入力対応 |
| `-LiteralPath` | `string[]` | ワイルドカード非対応 |
| `-Value` | `object[]` | 書き込む内容。パイプライン入力対応 |
| `-Encoding` | 多形 | 省略時は `Auto` |
| `-EncodingFrom` | `string` | 参照ファイルから継承 |
| `-NoNewline` | switch | 要素間・末尾に改行を出力しない |
| `-LineBreak` | `Auto` / `CrLf` / `Lf` / `Cr` | 省略可。`Cr` は 10 節の決定で追加した |
| `-Force` | switch | 読み取り専用ファイルへも書き込む |
| `-AllowEncodingChange` | switch | **`Add-ProbedContent` のみ。** 整合性検査（6 節）を意図的に飛ばす |
| `-Culture` | `string` | 判定に用いるカルチャー名。省略時は実行環境のカルチャー |
| `-Strategy` | `string` | 判定方式。`Combined`（既定）/ `NativeOnly` / `UtfUnknownOnly` |
| `-WhatIf` / `-Confirm` | — | `SupportsShouldProcess` を有効にする |

`-NoClobber` は採用しない。標準の `Set-Content` / `Add-Content` にも存在しないため（`Out-File` / `Export-Csv` のパラメータである）。

### 5.2 `-EncodingFrom`

参照ファイルを検出し、次の 3 点をすべて継承する。

- 文字エンコーディング
- BOM の有無
- 改行コード

```powershell
Get-ProbedContent a.txt | Set-ProbedContent b.txt -EncodingFrom a.txt
```

個別の明示指定は継承より優先する（粒度の細かい方が勝つ）。

```powershell
Set-ProbedContent b.txt -EncodingFrom a.txt -LineBreak Lf
```

`-Encoding` と `-EncodingFrom` の同時指定は、いずれもエンコーディングと BOM を決めようとするため**競合として Error** とする。

### 5.3 `-Encoding Auto` の意味

| コマンド | 意味 | 対象が存在しない場合 |
|---|---|---|
| `Set-ProbedContent` | 書き込み先の既存ファイルから継承 | **Error 終了** |
| `Add-ProbedContent` | 追記先から継承 | **Error 終了** |

`Set-ProbedContent` では、この既定は「上書き時に既存ファイルの性質を壊さない」という限定的な用途にのみ適合する。パイプラインで繋いだ場合に読み取り元の性質が引き継がれない点に注意が必要であり、そのために `-EncodingFrom` を用意している。

`Add-ProbedContent` では、この既定がそのまま自然な挙動となる（追記先の性質を保って追記する）。

### 5.4 `-Encoding` に `EncodingInformation` を渡した場合

`EncodingInformation` は `LineBreak` を保持しているため、エンコーディング・BOM に加えて**改行も継承する**。

```powershell
$enc = Resolve-Encoding a.txt
Get-ProbedContent a.txt -Encoding $enc | Set-ProbedContent b.txt -Encoding $enc
```

語彙名（`utf8NoBOM` 等）を渡した場合は改行情報が存在しないため、`-LineBreak` 省略時は OS 既定に落ちる。**`-Encoding` の入力形式によって改行の決まり方が変わる**点は、ヘルプに明記する。

### 5.5 `-LineBreak` 省略時の決定順序

| 優先 | 条件 | 結果 |
|---|---|---|
| 1 | 参照情報がある（`-EncodingFrom`、`-Encoding Auto`、`-Encoding <EncodingInformation>`） | 参照元の改行を継承（OS を問わない） |
| 2 | 参照情報が無い | OS 既定 |

OS 既定は次のとおり（`Environment.NewLine` と一致する）。

| OS | 既定 |
|---|---|
| Windows | CRLF |
| Linux | LF |
| macOS | LF |

PS 7.x の標準 `Set-Content` も同じ挙動であるため、標準コマンドとの一貫性も保たれる。

### 5.6 `-NoNewline` との関係

`-LineBreak` と `-NoNewline` は別のことを制御しており、矛盾しない。

- `-LineBreak`: 改行を出力するとき、**どの文字を使うか**
- `-NoNewline`: **末尾（および要素間）に改行を付けるか**

`-NoNewline` 指定時は `-LineBreak` の指定が単に使われないだけである。ただし利用者が意図と異なる結果を得る可能性があるため、**同時指定時は Warning** を出す（Error にはしない）。

`-NoNewline` は、`-Raw` で読んだ内容をそのまま書き戻す往復に不可欠である。

```powershell
$raw = Get-ProbedContent a.txt -Raw
$raw -replace 'foo','bar' | Set-ProbedContent b.txt -NoNewline
```

### 5.7 `-Culture` / `-Strategy`

意味と値は 4.4 と同じ。書き込み系で判定が走るのは次の 2 つの経路であり、
**どちらにも効く**。

| 経路 | 判定する対象 |
|---|---|
| `-EncodingFrom` | 参照ファイル |
| `-Encoding` 省略（`Auto`） | 書き込み先・追記先の既存ファイル |

`-EncodingFrom` の判定は `BeginProcessing` で行うため、`-Culture` / `-Strategy` の
検証はその前に済ませる。

---

## 6. `Add-ProbedContent` の整合性検査

既存ファイルと異なる値を指定された場合の扱いを、3 つの軸ごとに定める。

| 軸 | 不一致時 | 理由 |
|---|---|---|
| 文字エンコーディング | **条件付きで Error** | ファイルが解析不能・参照不能になる |
| BOM | **常に無視** | 追記では意味を持たない |
| 改行コード | **許可** | 混在しても実害がない |

### 6.1 文字エンコーディング — バイト列で判定する

安全性を決めているのはエンコーディング名の一致ではなく、**実際に書き出されるバイト列**である。「US-ASCII のファイルに UTF-8 で追記する」も「UTF-8 のファイルに ASCII 範囲だけの文字列を Shift_JIS で追記する」も安全であり、名前の組み合わせ表では判定できない。

`Add-ProbedContent` は追記する文字列を手元に持っているため、実際に符号化して比較できる。判定規則は次の一つで足りる。

```
指定されたエンコーディング X で符号化したバイト列
  == 既存ファイルのエンコーディング Y で符号化したバイト列
```

成立すれば、X で追記した結果は Y で追記したのとバイト単位で同一であり、ファイルは Y のまま一貫する。成立しなければ Error とする。

この規則から導かれる結果は次のとおり。

| 既存 | 追記指定 | 内容 | 判定 |
|---|---|---|---|
| US-ASCII | UTF-8 | 日本語含む | Error（※） |
| UTF-8 | US-ASCII | ASCII のみ | 許可 |
| UTF-8 | Shift_JIS | 日本語含む | Error |
| UTF-8 | Shift_JIS | ASCII のみ | 許可 |
| Shift_JIS | UTF-8 | 日本語含む | Error |
| UTF-8 | UTF-16LE | 何でも | Error |

※ 日本語を含む文字列は US-ASCII で符号化できず `?` に置換されるため、バイト列は一致せず Error となる。これは正しい挙動である。既存ファイルが ASCII だからといって UTF-8 の日本語を追記すれば、**ファイル全体のエンコーディングが UTF-8 に変わる**。それを黙って行うのは `Add-` の役割を超えている。

### 6.2 `-AllowEncodingChange`

「ASCII のファイルを UTF-8 に格上げしながら追記する」は正当な需要であるため、専用スイッチで許可する。

```powershell
Add-ProbedContent log.txt -Value $line -Encoding utf8NoBOM -AllowEncodingChange
```

**`-Force` に相乗りさせない**。`-Force` は「読み取り専用ファイルへ書き込む」という別の意味を既に持っており、両者を分けないと「読み取り専用属性を外したいだけなのにエンコーディング検査まで無効化された」という事故が起きる。

### 6.3 補足

- `-Encoding` を明示指定した場合でも、この判定に既存側のエンコーディングが必要なため、**既存ファイルの検出は必ず走る**。検出失敗は既定どおり Error
- BOM: 追記でファイル途中に BOM を書き込むことは、いかなる場合も正しくない。`-Encoding utf8BOM` と `utf8NoBOM` は `Add-ProbedContent` では同一の挙動となる。警告は出さない（`-EncodingFrom` で BOM 付きファイルから継承した場合、正当な使い方なのに毎回鳴るため）。ヘルプに「追記では BOM 指定は無視される」と明記する
- 改行: CRLF のファイルに LF を追記しても混在改行になるだけで、読めなくなることはない。`-LineBreak Lf` の明示指定は「今後は LF に統一したい」という正当な意図であり得るため、Error にしない
- 既存ファイルの末尾に改行が無い場合、追記内容は最終行に連結される。標準の `Add-Content` と同じ挙動とする（1.1.0 では警告もスイッチも設けない）
- ISO-2022-JP のような状態を持つエンコーディングも、.NET のエンコーダは `GetBytes` 呼び出しごとに状態をリセットしてエスケープシーケンスを先頭に出力するため、追記しても壊れない。バイト列比較の判定もそのまま機能する

---

## 7. `ConvertTo-DotNetEncoding`

### 7.1 目的

`System.Text.Encoding` インスタンスを得るための専用コマンド。`[IO.File]::ReadAllLines` / `WriteAllText`、`StreamWriter`、`XmlWriter`、サードパーティ製ライブラリなど、**.NET クラスライブラリを直接利用する場合にのみ使用する**。標準的なコマンド操作（Probed 系コマンド）とは用途が異なるため、コマンド単位で分離する。

```powershell
$enc    = Resolve-Encoding file.txt
$encobj = ConvertTo-DotNetEncoding $enc
$content = [IO.File]::ReadAllLines('file.txt', $encobj)
```

### 7.2 入力（多形）

Probed 系コマンドの `-Encoding` と同じ変換ロジックを共有し、次を受け付ける。

- `EncodingInformation`（`Resolve-Encoding` の戻り値）
- 統一語彙名
- WebName
- コードページ数値
- `System.Text.Encoding` インスタンス

パラメータで入力種別を分けない。`-Name` と `-WebName` の境界が利用者から見て無意味であること、パイプライン入力を受けるなら型による自動判別が必要になることが理由である。

数値の解釈が曖昧になる問題（`65001` が文字列として `GetEncoding(string)` に渡ると失敗する）は、3.4 の解決順序で回避する。

### 7.3 パイプライン入力

対応する。`Resolve-Encoding` はワイルドカード非対応で一度に複数ファイルを扱えないため、単一入力が保証される。

```powershell
$encobj = Resolve-Encoding file.txt | ConvertTo-DotNetEncoding
```

### 7.4 出力

`System.Text.Encoding`。BOM 方針を反映するため、Unicode 系は必ずコンストラクタで組み立てる（3.5 参照）。

`ReadAllLines` などの読み取り系 API は `GetPreamble()` を参照せず、BOM があれば自動で読み飛ばす。したがって BOM 方針が意味を持つのは `WriteAllText` / `StreamWriter` などの書き込み側のみである。これは原則 A と同じ構造であり、ヘルプでも同じ説明が使える。

### 7.5 `Auto` は Error

`Auto` は「ファイルからの検出」を指す語彙であり、ファイルを引数に取らない本コマンドでは解決できない。実行環境の既定エンコーディングを返す案は採らない。理由は次のとおり。

1. PS 5.1 では既定エンコーディングがコマンドごとに異なる（`Get-Content` は ANSI、`Set-Content` は ASCII、`Out-File` は UTF-16LE）。さらに `$OutputEncoding` や `[Console]::OutputEncoding` という別軸もあり、「その PowerShell の既定」を一意に書き切れない
2. 同じスクリプトが PS 5.1 と PS 7.x で異なるバイト列を出力することになり、本モジュールの存在意義と逆行する
3. Probed 系での `Auto`（対象を検出）と意味が二重化する

エラーメッセージは正規形に誘導する内容とする。

> `Auto` はファイルからの検出を指す語彙のため、`ConvertTo-DotNetEncoding` では指定できません。ファイルから解決するには `Resolve-Encoding <path> | ConvertTo-DotNetEncoding` を使用してください。

環境依存の値が必要な場合は `ansi` / `oem` を使用する。これらは環境依存であることが名前から明らかであり、利用者を誤解させない。

---

## 8. エラー方針まとめ

終了エラーと非終了エラーを次の基準で分けている。

- **終了エラー** … その指定では 1 ファイルも処理できないもの（パラメータの組み合わせの誤り、
  パラメータ束縛段階での失敗）。ファイルを開く前に失敗させ、書きかけの破損ファイルを残さない
- **非終了エラー** … 対象ファイルごとの失敗。他のファイルの処理は続行する。
  `-ErrorAction Stop` を付ければ終了エラーにできる

| 事象 | 扱い |
|---|---|
| 書き込み系での裸の `utf8` 指定 | 終了エラー（パラメータ束縛段階） |
| 書き込み系での `utf7` 指定 | 終了エラー（パラメータ束縛段階） |
| BOM 接尾辞を許さない語彙への接尾辞付き指定 | 終了エラー（パラメータ束縛段階） |
| `-Encoding` と `-EncodingFrom` の同時指定 | 終了エラー |
| `-EncodingFrom` の参照先が存在しない | 終了エラー |
| 解釈できない `-Culture` の指定 | 終了エラー（ファイルを開く前） |
| 解釈できない `-Strategy` の指定 | 終了エラー（ファイルを開く前） |
| `ConvertTo-DotNetEncoding` への `Auto` 指定 | 終了エラー（誘導メッセージ付き） |
| 読み取り対象のファイルが存在しない | 非終了エラー |
| エンコーディング検出の失敗 | 非終了エラー |
| 判定できても実行環境がそのコードページを提供していない | 非終了エラー（`CodePageNotAvailable`） |
| `Set-ProbedContent -Encoding Auto` で書き込み先が存在しない | 非終了エラー |
| `Add-ProbedContent -Encoding Auto` で追記先が存在しない | 非終了エラー |
| `Add-ProbedContent` でのバイト列不一致 | 非終了エラー（`-AllowEncodingChange` で回避可） |
| 同一パスの読み書きを 1 つのパイプラインで行った | 非終了エラー |
| 書き込み先が読み取り専用・書き込み権限なし | 非終了エラー |
| `-LineBreak` と `-NoNewline` の同時指定 | Warning |

読み取り側の「存在しない」「検出失敗」を非終了エラーにしたのは、
標準の `Get-Content` の実測に合わせたためである。

---

## 9. 実装上の留意点

- **BOM の混入**: `StreamReader` に `detectEncodingFromByteOrderMarks: false` を渡すと、BOM が U+FEFF として 1 行目の先頭に混入する。検出結果を強制しつつ BOM を確実に落とす処理が必要
- **二重読み込みの回避**: 1 本の `FileStream` で先頭バッファを検出に使い、`Seek(0)` してから復号する
- **BOM 付きインスタンスの罠**: `Encoding.GetEncoding("utf-8")` および `Encoding.UTF8` は BOM 付きインスタンスを返す。Unicode 系は必ずコンストラクタで組み立てる
- **CodePagesEncodingProvider**: PS 7.x / .NET で CP932 等を扱うには登録が必要。ホスト側の登録に依存せず、モジュール内で登録する
- **大容量ファイル**: 標準の `Get-Content` と同様のメモリ挙動を保つため、行単位でストリーミング出力する（`-Raw` を除く）
- **`-TotalCount` と検出**: 先頭 N 行のみ読む場合でも、検出のためにファイル先頭の一定量を読む必要がある
- **同一ファイルの往復**: `Get-ProbedContent a.txt | Set-ProbedContent a.txt` は、遅延読み込みの場合に書き込み側が先にファイルを切り詰めて破壊する。標準の `Get-Content` も同様だが、本コマンド群は「エンコーディングを保って書き戻す」用途でこの罠を踏みやすい。**同一パスを検出して非終了エラーとした**（`Internal/ActiveReadRegistry`）。ただし `-Raw` は出力前にファイルを閉じるため対象外で、読んで加工して書き戻せる
- **`EncodingSpec`（内部型）**: 入力を `{ Encoding, EmitBom, LineBreak }` の 3 項に正規化する内部表現。`ArgumentTransformationAttribute` で全コマンド共通に解決する。**公開型としない**

---

## 10. 未決事項（すべて決定済み）

起票時の 3 件は、いずれも利用者の確認を経て決定し、実装済みである。
本節は経緯として残す。決定の根拠は
`docs/EncodingProbe-1.1.0-作業引き継ぎメモ.md`「2.1 仕様書 10 節『未決事項』の決定」にある。

| 項目 | 決定内容 | 実装箇所 |
|---|---|---|
| 混在改行の継承 | **CR-LF を含むなら CR-LF、含まないなら LF**。`LfAndCrLf` / `CrAndCrLf` / `LfAndCrAndCrLf` → CR-LF、`LfAndCr` → LF。OS に依存しない決定的な規則とする | `Internal/LineBreakResolver` |
| 空ファイルからの継承 | Error にはしない。**`Encoding.Default`（.NET ランタイム既定・BOM 無し）と `Environment.NewLine`** を使う。net48/PS 5.1 なら ANSI コードページ、net10.0/PS 7.x なら UTF-8 | `Internal/EncodingInheritance` |
| 同一パスの往復 | **同一パスを検出して非終了エラー**にする。ただし `-Raw` は出力前にファイルを閉じるため対象外 | `Internal/ActiveReadRegistry` |

あわせて、本書 5.1 の `-LineBreak` から変更した点が 1 つある。

| 項目 | 変更内容 | 理由 |
|---|---|---|
| `-LineBreak` の `Cr` | **追加した**（起票時は `Auto` / `CrLf` / `Lf` の 3 値だった） | `Resolve-Encoding` が `Cr`（旧 Macintosh 形式）を返しうるため。原則 B「検出しうる全状態が語彙で表現できること」に従う |

### 1.2.0 以降に持ち越した課題

1.1.0 の作業中に見つかった、コアの判定エンジン側の課題は
`docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` にまとめてある（3 件、いずれも未着手）。
このうち「判定できても .NET が扱えないコードページがある」については、
1.1.0 側で非終了エラー（`CodePageNotAvailable`）として報告する対応を済ませている。

---

## 11. 用語

| 用語 | 意味 |
|---|---|
| 統一語彙 | 本モジュールが定義する、PS 5.1 / 7.x で共通のエンコーディング名の体系 |
| 標準語彙 | 実行中の PowerShell が `-Encoding` で受け付けるフレンドリ名 |
| 参照情報 | `-EncodingFrom`、`-Encoding Auto`、`-Encoding <EncodingInformation>` によって得られる、既存ファイル由来のエンコーディング・BOM・改行の情報 |
| 原則 A | BOM の有無は書き込み時のみ意味を持つ |
| 原則 B | 検出しうる全状態が語彙で表現できること |
