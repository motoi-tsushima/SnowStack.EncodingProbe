# 課題 — BOM 無しの UTF-16 / UTF-32 で英数字だけのファイルが us-ascii と判定される

- 起票日: 2026-10-19
- 起票の経緯: 1.3.0 第一次（`docs/EncodingProbe-1.3.0-依頼-第一次.md` 3.6）で、仕様書 3.2・24.1 の推定を実測で確かめた
- 状態: **未着手**（1.3.0 ではクラスライブラリを変更しないため、範囲外とした）
- 対象: クラスライブラリの判定処理（`SnowStack.EncodingProbe/EncodingDetector.cs`）と、それを使うすべてのコマンド

---

## 1. 現象

BOM 無しの UTF-16LE / UTF-16BE / UTF-32LE / UTF-32BE で、内容が英数字（U+0000〜U+007F）だけのファイルを全体で判定すると、
`20127 / us-ascii` と判定される。そのため、読み込むと `U+0000` が混じった文字列になる。

実測（2026-10-19、1.2.0 のビルド、Windows 11、内容は `A,B` `1,2` `abc,def` の 3 行、CR-LF）:

| ファイル | `Resolve-Encoding` | `Get-ProbedContent` の 1 行目（文字のコード） |
|---|---|---|
| UTF-16LE（BOM 無し） | `20127 / us-ascii`、LineBreak `CrLf` | `41 00 2C 00 42 00` |
| UTF-16BE（BOM 無し） | 同上 | `00 41 00 2C 00 42 00` |
| UTF-32LE（BOM 無し） | 同上 | `41 00 00 00 2C 00 00 00 42 00 00 00` |
| UTF-32BE（BOM 無し） | 同上 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00` |

Windows PowerShell 5.1（net48 ビルド）と PowerShell 7.x（net10.0 ビルド）で同じ結果だった。
`Get-ProbedContent` は us-ascii で復号するので、`00` が U+0000 として行に残る
（行の分割は `0D` / `0A` の位置で起きるため、UTF-16BE などでは行頭に U+0000 が付く）。

非 ASCII 文字（日本語など）を 1 文字でも含むファイルは、0x80 以上のバイトが現れるのでこの経路に入らず、UTF-16 / UTF-32 と判定される。

## 2. 原因

`EncodingDetector.Detection` の判定順序で、ISO-2022 / ASCII の判定（`ISO2022_Detection`）が UTF-32 / UTF-16 の判定より前にある。
`ISO2022_Detection` は、すべてのバイトが 0x80 未満なら（ESC シーケンスが無ければ）ASCII と判定し、`0x00` を含むかどうかを見ていない。
英数字だけの UTF-16 / UTF-32 はすべてのバイトが 0x80 未満なので、ここで `20127` に確定し、後段の `Utf32_Detection` / `Utf16_Detection` に届かない。

ASCII 互換の文字エンコーディングのテキストには `0x00` が現れないので、`0x00` を含む入力を ASCII と判定するのは誤りである。

## 3. 影響するコマンド

全体で判定するすべての経路が対象になる。

- `Resolve-Encoding`（`-DetectionLineCount` の有無にかかわらず。先頭 N 行が英数字だけの場合も `us-ascii` になる。1.3.0 仕様書 3.1・6.3 で仕様として明記した）
- `Get-ProbedContent`（`-Encoding Auto`、既定）
- `Set-ProbedContent` / `Add-ProbedContent` / `Out-ProbedFile` の `-Encoding Auto`・`-EncodingFrom`・追記先からの継承
- `Convert-ProbedContent` の変換元の判定
- クラスライブラリの `EncodingProbe.Detect`

1.3.0 の `Import-ProbedCsv` / `Export-ProbedCsv` は、`-DetectionLineCount` を指定した場合だけ、切り出しで決めた改行の型を使って読み替える（1.3.0 仕様書 3.2、24 章 U4）。
全体で判定した場合は既存の規則どおりで、この課題の影響を受ける。

## 4. 対処の方向（未決）

- `ISO2022_Detection` で、`0x00` を含む入力を ASCII としない（UTF-32 / UTF-16 の判定に進める）案が考えられる。
  判定順序は CLAUDE.md で「意味を持つ」とされており、変更する場合は全言語のテストデータでの回帰を確かめる必要がある
- クラスライブラリの判定処理の変更なので、NuGet パッケージのバージョンを上げることになる
- 1.4.0 で検討する「全体で判定した場合の検証」（1.3.0 仕様書 1.4）と合わせて検討するとよい
