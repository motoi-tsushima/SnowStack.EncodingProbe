# EncodingProbe 1.3.0 — 調査：`0x00` を含む入力の判定（BOM 無し UTF-16 / UTF-32 の英数字だけのファイル）

- 作成日: 2026-10-20
- 測定日: 2026-10-20
- 作業の性質: 調査のみ。`feature/1.3.0-csv` の製品コードは変更していない（修正案は実験用ブランチ `investigate/1.3.0-nul-ascii` にだけ入れた）
- 課題: `docs/EncodingProbe-課題-BOM無しUTF-16の英数字だけの判定.md`
- 本書は事実だけを記録する。望ましいかどうかの評価はしていない

---

## 0. 要約

| # | 問い | 結果 |
|---|---|---|
| 1 | C（回帰）で結果が変わった入力はあるか | **無い。** C1（TestData の 50 ファイル）・C2・C3・C4 の計 60 件 × 6 条件 × 2 TFM で、現状と修正案の結果は一致した。ただし **TestData に `0x00` を含むファイルは 1 つも無い**（C1 は修正案の分岐に入らない）。既存のテストは、コア（net10.0 1179 件 / net48 1189 件）は全件通過。**モジュール（net10.0 1079 件）は 12 件失敗**した。12 件とも、`-DetectionLineCount` で英数字だけの BOM 無し UTF-16 / UTF-32 に `20127` が返ることを固定しているテストである（3.2） |
| 2 | A で修正案が UTF-16 / UTF-32 と答えなかった入力はあるか | **無い。** A の 52 件すべてが、Combined / NativeOnly × ja-JP / en-US の 4 条件で、入力どおりの幅・エンディアン（1200 / 1201 / 12000 / 12001）になった。1 文字（2 バイト・4 バイト）の入力も含む。UtfUnknownOnly は修正案の対象外で、52 件すべてが `20127 / ascii` のままだった |
| 3 | 修正案の 4（ASCII に戻る）は守られているか | **守られている。** `0x00` を含み UTF-32 / UTF-16 が成立しなかった B の 19 件は、すべて `20127 / us-ascii` のままで、65001 などに変わったものは無い。ESC シーケンスのある C2 の 2 件は `50220` のまま（C2-mid は単独で呼べば UTF-16BE が成立するが、ISO-2022 の分岐なので試されない） |
| 4 | B で修正案が UTF-16 / UTF-32 と答えた入力はどれか | **B の 39 件中 20 件。** UTF-16LE が 18 件、UTF-16BE が 1 件（B5-1+13）、UTF-32LE が 1 件（B8-12）。内訳は B1 の 2 件、B3 の 14 件（16 件中）、B5 の 1 件、B7 の 1 件、B8 の 2 件。B6（すべて `00`）は 6 件とも ASCII のまま。成立の条件は 4 章 |
| 5 | 判定方式・カルチャー・TFM で結果が違った箇所はあるか | **TFM の差は無い**（両ビルドとも、判定 API 906 行とコマンド 45 行のすべてが net48 と net10.0 で一致）。**カルチャーの差も無い**（`0x00` を含む入力のすべてで、ja-JP と en-US の結果が一致）。**Combined と NativeOnly の差も無い**（`0x00` を含む全入力で一致。UTF-16 / UTF-32 はクロスチェックの対象にならない）。違いがあるのは UtfUnknownOnly だけで、UTF.Unknown は A / B のすべてに `20127 / ascii` を返し、修正案の変化は起きない |
| 6 | コマンドの結果は 1.3.0 の仕様書・ヘルプとどう食い違うか | `Resolve-Encoding -DetectionLineCount 1` は、英数字だけの BOM 無し UTF-16 / UTF-32 に `us-ascii` ではなく `1200` / `1201` / `12000` / `12001` を報告する。仕様書 3.2・6.3・24 章（U4、24.1）、MAML の `Resolve-Encoding -DetectionLineCount`（5 言語 + 複製 2）、動作確認手順書 1.2、実装記録 6.6、CHANGELOG の既知の課題の記述と食い違う。B 型の入力では `Get-ProbedContent` / `Import-ProbedCsv` の結果も変わる（5 章） |

---

## 1. 測定の方法

### 1.1 比べた 2 つのビルド

| ビルド | ブランチ / コミット | 内容 |
|---|---|---|
| 現状 | `feature/1.3.0-csv`（`eae172e`） | クラスライブラリは master と同じ |
| 修正案 | `investigate/1.3.0-nul-ascii`（`c58d23a`。`eae172e` から作成） | 依頼 2 章の修正案だけを `EncodingDetector.cs` に入れたもの。テストは追加も整理もしていない |

修正案の差分（`git diff feature/1.3.0-csv investigate/1.3.0-nul-ascii`）:

```diff
--- a/SnowStack.EncodingProbe/EncodingDetector.cs
+++ b/SnowStack.EncodingProbe/EncodingDetector.cs
@@ -491,6 +491,19 @@ namespace SnowStack.EncodingProbe
                 {
                     encInfo.CodePage = CodePageAscii;
                     encInfo.Bom = false;
+
+                    // 【実験】0x00 を含む場合だけ UTF-32 → UTF-16 を試す。成立しなければ ASCII のまま（UTF-8 には進まない）
+                    if (Array.IndexOf(this._buffer, (byte)0x00, 0, this.BufferSize) >= 0)
+                    {
+                        if (Utf32_Detection(out bool isLittleEndianNul32) == false)
+                        {
+                            encInfo.CodePage = isLittleEndianNul32 ? CodePageUtf32Le : CodePageUtf32Be;
+                        }
+                        else if (Utf16_Detection(out bool isLittleEndianNul16) == false)
+                        {
+                            encInfo.CodePage = isLittleEndianNul16 ? CodePageUtf16Le : CodePageUtf16Be;
+                        }
+                    }
                 }
 
                 encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
```

この変更は `ISO2022_Detection` が「ESC シーケンスの無い ASCII」と答えた分岐の中だけにある。ESC シーケンスがあった場合と、`0x00` の無い入力は、従来の経路をそのまま通る。
UTF-32 / UTF-16 のどちらも成立しなければ `encInfo.CodePage` は `CodePageAscii`（20127）のままで、UTF-8 の判定には進まない。

### 1.2 成果物

| 成果物 | 場所 |
|---|---|
| 測定スクリプト（1 ホスト × 1 ビルド） | `tools/Measure-NulInputDetection.ps1` |
| 測定スクリプト（2 ビルド × 2 ホストの実行と集計） | `tools/Invoke-NulInputMeasurement.ps1` |
| 判定 API の生の結果 | `tools/NulInputDetectionResults/detect-{current,proposal}-{net10.0,net48}.tsv`（入力 × カルチャー × 判定方式） |
| 診断 | `tools/NulInputDetectionResults/diag-*.tsv`（入力ごとの長さ・`00` の数・先頭 16 バイト・各判定メソッドを単独で呼んだ結果・NULL パターンの数） |
| コマンドの生の結果 | `tools/NulInputDetectionResults/commands-*.tsv` |
| 集計 | `tools/NulInputDetectionResults/summary.txt`、`table-net10.0-ja-JP.tsv`（全入力の対照表）、`table-commands-net10.0.tsv` |
| 修正案のビルドでの既存テストの結果 | `tools/NulInputDetectionResults/tests-proposal.txt` |

再測定の手順:

```powershell
# 修正案の worktree を作ってビルドする（パスが長いと PowerShell SDK のファイルのコピーが 260 文字の上限で失敗するので、短いパスに置く）
git worktree add ..\EP-nul investigate/1.3.0-nul-ascii
dotnet build SnowStack.EncodingProbe.slnx -c Debug
dotnet build ..\EP-nul\SnowStack.EncodingProbe.slnx -c Debug

pwsh -NoProfile -File tools/Invoke-NulInputMeasurement.ps1 -ProposalRoot ..\EP-nul
```

### 1.3 測定の条件

- 呼び方: `EncodingProbe.Detect(byte[], EncodingDetectorOptions)`。記録した値は `CodePage / EncodingWebName / Bom / LineBreak`
- 判定方式: Combined / NativeOnly / UtfUnknownOnly
- カルチャー: ja-JP / en-US
- TFM: net48（Windows PowerShell 5.1 で実行）と net10.0（PowerShell 7.x で実行）。DLL はモジュールのビルド出力（`SnowStack.EncodingProbe.PowerShell/bin/Debug/<TFM>`）から読み込んだ。
  同名のアセンブリを 1 つのプロセスに 2 つ読み込めないので、ビルドごとに別プロセスで測った
- 診断: `EncodingDetector` の private メソッド `ISO2022_Detection` / `Utf32_Detection` / `Utf16_Detection` を、リフレクションで**単独に**呼んだ結果（表の「単独 UTF-32 / UTF-16」の列。`no` = 不成立）。
  NULL パターンの数は、スクリプトの中で `Utf16_Detection` / `Utf32_Detection` の後半と同じ規則で数え直した値（補助）
- コマンド: A1 の 4 件と、B1-13+1・B3-rec8-nonl-x4・B3-rec16-crlf-x3・B6-zero2・B6-zero4 をファイルに書き出し、`-Culture ja-JP` で実行した
- 入力はすべて、スクリプトの中で符号位置からバイトを直接組み立てた（製品の書き込み処理も `System.Text.Encoding` も使っていない）。C1 だけは TestData のファイルをそのまま読んだ

### 1.4 入力の数

| 群 | 件数 | 内容 |
|---|---|---|
| A | 52 | A1 4、A2 12、A3 20、A4 8、A5 8 |
| B | 39 | B1 3、B2 4、B3 16、B4 2、B5 2、B6 6、B7 2、B8 4 |
| C | 60 | C1 50（TestData のすべてのファイル）、C2 2、C3 4、C4 4 |

判定 API は 151 件 × 2 カルチャー × 3 判定方式 = 906 行を、2 ビルド × 2 TFM で測った。

---

## 2. 結果：判定 API

表の値は net10.0 / ja-JP / NativeOnly のもの。1.3 の条件で確かめたとおり、`0x00` を含む入力では Combined と NativeOnly、ja-JP と en-US、net48 と net10.0 の結果がすべて一致する。
値の並びは `CodePage / EncodingWebName / Bom / LineBreak`。

### 2.1 A（修正案で UTF-16 / UTF-32 になってほしいもの）

| ID | 入力 | 長さ | 先頭のバイト列 | 単独 UTF-32 / UTF-16 | 現状 | 修正案 | 変化 |
|---|---|---|---|---|---|---|---|
| A1-crlf-u16le | A,B / 1,2 / abc,def CRLF (u16le) | 38 | `41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 32 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| A1-crlf-u16be | A,B / 1,2 / abc,def CRLF (u16be) | 38 | `00 41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 32 …` | no / BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | **変化** |
| A1-crlf-u32le | A,B / 1,2 / abc,def CRLF (u32le) | 76 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | LE / LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | **変化** |
| A1-crlf-u32be | A,B / 1,2 / abc,def CRLF (u32be) | 76 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 0D …` | BE / BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | **変化** |
| A2-lf-u16le | A,B / 1,2 / abc,def LF (u16le) | 32 | `41 00 2C 00 42 00 0A 00 31 00 2C 00 32 00 0A 00 …` | no / LE | 20127 / us-ascii / False / Lf | 1200 / utf-16 / False / Lf | **変化** |
| A2-lf-u16be | A,B / 1,2 / abc,def LF (u16be) | 32 | `00 41 00 2C 00 42 00 0A 00 31 00 2C 00 32 00 0A …` | no / BE | 20127 / us-ascii / False / Lf | 1201 / unicodeFFFE / False / Lf | **変化** |
| A2-lf-u32le | A,B / 1,2 / abc,def LF (u32le) | 64 | `41 00 00 00 2C 00 00 00 42 00 00 00 0A 00 00 00 …` | LE / LE | 20127 / us-ascii / False / Lf | 12000 / utf-32 / False / Lf | **変化** |
| A2-lf-u32be | A,B / 1,2 / abc,def LF (u32be) | 64 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 0A …` | BE / BE | 20127 / us-ascii / False / Lf | 12001 / utf-32BE / False / Lf | **変化** |
| A2-cr-u16le | A,B / 1,2 / abc,def CR (u16le) | 32 | `41 00 2C 00 42 00 0D 00 31 00 2C 00 32 00 0D 00 …` | no / LE | 20127 / us-ascii / False / Cr | 1200 / utf-16 / False / Cr | **変化** |
| A2-cr-u16be | A,B / 1,2 / abc,def CR (u16be) | 32 | `00 41 00 2C 00 42 00 0D 00 31 00 2C 00 32 00 0D …` | no / BE | 20127 / us-ascii / False / Cr | 1201 / unicodeFFFE / False / Cr | **変化** |
| A2-cr-u32le | A,B / 1,2 / abc,def CR (u32le) | 64 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | LE / LE | 20127 / us-ascii / False / Cr | 12000 / utf-32 / False / Cr | **変化** |
| A2-cr-u32be | A,B / 1,2 / abc,def CR (u32be) | 64 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 0D …` | BE / BE | 20127 / us-ascii / False / Cr | 12001 / utf-32BE / False / Cr | **変化** |
| A2-none-u16le | A,B;1,2;abc,def (改行なし) (u16le) | 30 | `41 00 2C 00 42 00 3B 00 31 00 2C 00 32 00 3B 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| A2-none-u16be | A,B;1,2;abc,def (改行なし) (u16be) | 30 | `00 41 00 2C 00 42 00 3B 00 31 00 2C 00 32 00 3B …` | no / BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | **変化** |
| A2-none-u32le | A,B;1,2;abc,def (改行なし) (u32le) | 60 | `41 00 00 00 2C 00 00 00 42 00 00 00 3B 00 00 00 …` | LE / LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | **変化** |
| A2-none-u32be | A,B;1,2;abc,def (改行なし) (u32be) | 60 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 3B …` | BE / BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | **変化** |
| A3-1ch-u16le | 1 文字 A (u16le) | 2 | `41 00` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| A3-1ch-u16be | 1 文字 A (u16be) | 2 | `00 41` | no / BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | **変化** |
| A3-1ch-u32le | 1 文字 A (u32le) | 4 | `41 00 00 00` | LE / LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | **変化** |
| A3-1ch-u32be | 1 文字 A (u32be) | 4 | `00 00 00 41` | BE / BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | **変化** |
| A3-2ch-u16le | 2 文字 AB (u16le) | 4 | `41 00 42 00` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| A3-2ch-u16be | 2 文字 AB (u16be) | 4 | `00 41 00 42` | no / BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | **変化** |
| A3-2ch-u32le | 2 文字 AB (u32le) | 8 | `41 00 00 00 42 00 00 00` | LE / LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | **変化** |
| A3-2ch-u32be | 2 文字 AB (u32be) | 8 | `00 00 00 41 00 00 00 42` | BE / BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | **変化** |
| A3-10ch-u16le | 10 文字 0123456789 (u16le) | 20 | `30 00 31 00 32 00 33 00 34 00 35 00 36 00 37 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| A3-10ch-u16be | 10 文字 0123456789 (u16be) | 20 | `00 30 00 31 00 32 00 33 00 34 00 35 00 36 00 37 …` | no / BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | **変化** |
| A3-10ch-u32le | 10 文字 0123456789 (u32le) | 40 | `30 00 00 00 31 00 00 00 32 00 00 00 33 00 00 00 …` | LE / LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | **変化** |
| A3-10ch-u32be | 10 文字 0123456789 (u32be) | 40 | `00 00 00 30 00 00 00 31 00 00 00 32 00 00 00 33 …` | BE / BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | **変化** |
| A3-1k-u16le | 1024 文字 (32 文字 CRLF × 32) (u16le) | 2048 | `54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| A3-1k-u16be | 1024 文字 (32 文字 CRLF × 32) (u16be) | 2048 | `00 54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 …` | no / BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | **変化** |
| A3-1k-u32le | 1024 文字 (32 文字 CRLF × 32) (u32le) | 4096 | `54 00 00 00 68 00 00 00 65 00 00 00 20 00 00 00 …` | LE / LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | **変化** |
| A3-1k-u32be | 1024 文字 (32 文字 CRLF × 32) (u32be) | 4096 | `00 00 00 54 00 00 00 68 00 00 00 65 00 00 00 20 …` | BE / BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | **変化** |
| A3-40k-u16le | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u16le) | 80000 | `54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| A3-40k-u16be | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u16be) | 80000 | `00 54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 …` | no / BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | **変化** |
| A3-40k-u32le | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u32le) | 160000 | `54 00 00 00 68 00 00 00 65 00 00 00 20 00 00 00 …` | LE / LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | **変化** |
| A3-40k-u32be | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u32be) | 160000 | `00 00 00 54 00 00 00 68 00 00 00 65 00 00 00 20 …` | BE / BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | **変化** |
| A4-printable-u16le | 0x20〜0x7E を一巡 (改行なし) (u16le) | 190 | `20 00 21 00 22 00 23 00 24 00 25 00 26 00 27 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| A4-printable-u16be | 0x20〜0x7E を一巡 (改行なし) (u16be) | 190 | `00 20 00 21 00 22 00 23 00 24 00 25 00 26 00 27 …` | no / BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | **変化** |
| A4-printable-u32le | 0x20〜0x7E を一巡 (改行なし) (u32le) | 380 | `20 00 00 00 21 00 00 00 22 00 00 00 23 00 00 00 …` | LE / LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | **変化** |
| A4-printable-u32be | 0x20〜0x7E を一巡 (改行なし) (u32be) | 380 | `00 00 00 20 00 00 00 21 00 00 00 22 00 00 00 23 …` | BE / BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | **変化** |
| A4-tab-u16le | 数字・記号・タブ・空白 CRLF (u16le) | 48 | `31 00 09 00 32 00 09 00 33 00 0D 00 0A 00 2D 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| A4-tab-u16be | 数字・記号・タブ・空白 CRLF (u16be) | 48 | `00 31 00 09 00 32 00 09 00 33 00 0D 00 0A 00 2D …` | no / BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | **変化** |
| A4-tab-u32le | 数字・記号・タブ・空白 CRLF (u32le) | 96 | `31 00 00 00 09 00 00 00 32 00 00 00 09 00 00 00 …` | LE / LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | **変化** |
| A4-tab-u32be | 数字・記号・タブ・空白 CRLF (u32be) | 96 | `00 00 00 31 00 00 00 09 00 00 00 32 00 00 00 09 …` | BE / BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | **変化** |
| A5-c03-u16le | CSV c03 (1.3.0 実測 M1) (u16le) | 52 | `4E 00 61 00 6D 00 65 00 2C 00 4D 00 65 00 6D 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| A5-c03-u32le | CSV c03 (1.3.0 実測 M1) (u32le) | 104 | `4E 00 00 00 61 00 00 00 6D 00 00 00 65 00 00 00 …` | LE / LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | **変化** |
| A5-c05-u16le | CSV c05 (1.3.0 実測 M1) (u16le) | 54 | `23 00 20 00 63 00 6F 00 6D 00 6D 00 65 00 6E 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| A5-c05-u32le | CSV c05 (1.3.0 実測 M1) (u32le) | 108 | `23 00 00 00 20 00 00 00 63 00 00 00 6F 00 00 00 …` | LE / LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | **変化** |
| A5-c09-u16le | CSV c09 (1.3.0 実測 M1) (u16le) | 36 | `41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 32 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| A5-c09-u32le | CSV c09 (1.3.0 実測 M1) (u32le) | 72 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | LE / LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | **変化** |
| A5-c12-u16le | CSV c12 (1.3.0 実測 M1) (u16le) | 24 | `41 00 2C 00 42 00 0D 00 31 00 2C 00 32 00 0D 00 …` | no / LE | 20127 / us-ascii / False / Cr | 1200 / utf-16 / False / Cr | **変化** |
| A5-c12-u32le | CSV c12 (1.3.0 実測 M1) (u32le) | 48 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | LE / LE | 20127 / us-ascii / False / Cr | 12000 / utf-32 / False / Cr | **変化** |

### 2.2 B（修正案でも ASCII のままであってほしいもの）

| ID | 入力 | 長さ | 先頭のバイト列 | 単独 UTF-32 / UTF-16 | 現状 | 修正案 | 変化 |
|---|---|---|---|---|---|---|---|
| B1-13+1 | Hello, World! + 00 (14 バイト) | 14 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21 00` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B1-12+1 | Hello World! + 00 (13 バイト) | 13 | `48 65 6C 6C 6F 20 57 6F 72 6C 64 21 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B1-15+1 | Hello, World!!! + 00 (16 バイト) | 16 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21 21 21 00` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B2-12+2 | Hello, World + 00 × 2 | 14 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B2-12+3 | Hello, World + 00 × 3 | 15 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B2-12+4 | Hello, World + 00 × 4 | 16 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B2-12+100 | Hello, World + 00 × 100 | 112 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00 00 00 …` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B3-rec8-nonl-x3 | レコード長 8、nonl、3 レコード (24 バイト) | 24 | `49 44 30 30 31 00 00 00 41 6C 69 63 65 00 00 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B3-rec8-nonl-x4 | レコード長 8、nonl、4 レコード (32 バイト) | 32 | `49 44 30 30 31 00 00 00 41 6C 69 63 65 00 00 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B3-rec8-crlf-x3 | レコード長 8、crlf、3 レコード (30 バイト) | 30 | `49 44 30 30 31 00 00 00 0D 0A 41 6C 69 63 65 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| B3-rec8-crlf-x4 | レコード長 8、crlf、4 レコード (40 バイト) | 40 | `49 44 30 30 31 00 00 00 0D 0A 41 6C 69 63 65 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| B3-rec16-nonl-x3 | レコード長 16、nonl、3 レコード (48 バイト) | 48 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B3-rec16-nonl-x4 | レコード長 16、nonl、4 レコード (64 バイト) | 64 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B3-rec16-crlf-x3 | レコード長 16、crlf、3 レコード (54 バイト) | 54 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| B3-rec16-crlf-x4 | レコード長 16、crlf、4 レコード (72 バイト) | 72 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| B3-rec17-nonl-x3 | レコード長 17、nonl、3 レコード (51 バイト) | 51 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B3-rec17-nonl-x4 | レコード長 17、nonl、4 レコード (68 バイト) | 68 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B3-rec17-crlf-x3 | レコード長 17、crlf、3 レコード (57 バイト) | 57 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / no | 20127 / us-ascii / False / CrLf | 20127 / us-ascii / False / CrLf | — |
| B3-rec17-crlf-x4 | レコード長 17、crlf、4 レコード (76 バイト) | 76 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| B3-rec32-nonl-x3 | レコード長 32、nonl、3 レコード (96 バイト) | 96 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B3-rec32-nonl-x4 | レコード長 32、nonl、4 レコード (128 バイト) | 128 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B3-rec32-crlf-x3 | レコード長 32、crlf、3 レコード (102 バイト) | 102 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| B3-rec32-crlf-x4 | レコード長 32、crlf、4 レコード (136 バイト) | 136 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | no / LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | **変化** |
| B4-abc | a 00 bb 00 ccc 00 | 9 | `61 00 62 62 00 63 63 63 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B4-print0 | dir1/file.txt 00 dir2/x 00 | 21 | `64 69 72 31 2F 66 69 6C 65 2E 74 78 74 00 64 69 …` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B5-1+13 | 00 + "Hello, World!" | 14 | `00 48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21` | no / BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | **変化** |
| B5-4+12 | 00 × 4 + "Hello, World" | 16 | `00 00 00 00 48 65 6C 6C 6F 2C 20 57 6F 72 6C 64` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B6-zero1 | すべて 00 (1 バイト) | 1 | `00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B6-zero2 | すべて 00 (2 バイト) | 2 | `00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B6-zero3 | すべて 00 (3 バイト) | 3 | `00 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B6-zero4 | すべて 00 (4 バイト) | 4 | `00 00 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B6-zero8 | すべて 00 (8 バイト) | 8 | `00 00 00 00 00 00 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B6-zero1024 | すべて 00 (1024 バイト) | 1024 | `00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 …` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B7-9 | A 00 B 00 C 00 D E 00 (9 バイト) | 9 | `41 00 42 00 43 00 44 45 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B7-10 | A 00 B 00 C 00 D E 00 00 (10 バイト) | 10 | `41 00 42 00 43 00 44 45 00 00` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |
| B8-13 | A 000000 B 000000 C D 000000 (13 バイト) | 13 | `41 00 00 00 42 00 00 00 43 44 00 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B8-11 | A 000000 B 0000 C 000000 (11 バイト) | 11 | `41 00 00 00 42 00 00 43 00 00 00` | no / no | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | — |
| B8-12 | A 000000 B 000000 C D 0000 (12 バイト) | 12 | `41 00 00 00 42 00 00 00 43 44 00 00` | LE / LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | **変化** |
| B8-16 | A 000000 B 000000 C 00 D 00000000 00 (16 バイト) | 16 | `41 00 00 00 42 00 00 00 43 00 44 00 00 00 00 00` | no / LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | **変化** |

B3 のレコードは `ID001` / `Alice` / `B` / `Charlie01` をレコード長まで `00` で埋めたもの（レコード長 8 では `Charlie01` を 8 文字に切った）。`crlf` は各レコードの後に `0D 0A` を置いたもの。

### 2.3 C（修正案で結果が変わらないはずのもの）

C1（TestData の 50 ファイル）は、すべての条件で現状と修正案の結果が一致した。50 ファイルのいずれにも `0x00` は含まれていない（`diag-*.tsv` の `zeros` 列がすべて 0）。
ファイルごとの値は `table-net10.0-ja-JP.tsv` にある。

| ID | 入力 | 長さ | 先頭のバイト列 | 単独 UTF-32 / UTF-16 | 現状 | 修正案 | 変化 |
|---|---|---|---|---|---|---|---|
| C2-mid | ESC$B 日本 ESC(B 00 A | 12 | `1B 24 42 46 7C 4B 5C 1B 28 42 00 41` | no / BE | 50220 / iso-2022-jp / False / None | 50220 / iso-2022-jp / False / None | — |
| C2-wrap | A 00 ESC$B 日本 ESC(B CRLF 00 | 15 | `41 00 1B 24 42 46 7C 4B 5C 1B 28 42 0D 0A 00` | no / no | 50220 / iso-2022-jp / False / CrLf | 50220 / iso-2022-jp / False / CrLf | — |
| C3-bom-u16le | A1 の内容 BOM 付き (u16le) | 40 | `FF FE 41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 …` | no / LE | 1200 / utf-16 / True / CrLf | 1200 / utf-16 / True / CrLf | — |
| C3-bom-u16be | A1 の内容 BOM 付き (u16be) | 40 | `FE FF 00 41 00 2C 00 42 00 0D 00 0A 00 31 00 2C …` | no / BE | 1201 / unicodeFFFE / True / CrLf | 1201 / unicodeFFFE / True / CrLf | — |
| C3-bom-u32le | A1 の内容 BOM 付き (u32le) | 80 | `FF FE 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 …` | LE / LE | 12000 / utf-32 / True / CrLf | 12000 / utf-32 / True / CrLf | — |
| C3-bom-u32be | A1 の内容 BOM 付き (u32be) | 80 | `00 00 FE FF 00 00 00 41 00 00 00 2C 00 00 00 42 …` | BE / BE | 12001 / utf-32BE / True / CrLf | 12001 / utf-32BE / True / CrLf | — |
| C4-ja-u16le | 日本語,ABC CRLF BOM 無し (u16le) | 18 | `E5 65 2C 67 9E 8A 2C 00 41 00 42 00 43 00 0D 00 …` | no / LE | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | — |
| C4-ja-u16be | 日本語,ABC CRLF BOM 無し (u16be) | 18 | `65 E5 67 2C 8A 9E 00 2C 00 41 00 42 00 43 00 0D …` | no / BE | 1201 / unicodeFFFE / False / CrLf | 1201 / unicodeFFFE / False / CrLf | — |
| C4-ja-u32le | 日本語,ABC CRLF BOM 無し (u32le) | 36 | `E5 65 00 00 2C 67 00 00 9E 8A 00 00 2C 00 00 00 …` | LE / LE | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | — |
| C4-ja-u32be | 日本語,ABC CRLF BOM 無し (u32be) | 36 | `00 00 65 E5 00 00 67 2C 00 00 8A 9E 00 00 00 2C …` | BE / BE | 12001 / utf-32BE / False / CrLf | 12001 / utf-32BE / False / CrLf | — |

C2-mid は単独で呼ぶと UTF-16BE が成立する（偶数の位置の `00` の直後に `41`）が、`ISO2022_Detection` が ISO-2022 と答える分岐なので、修正案でも試されない。

### 2.4 集計

| 項目 | net10.0 | net48 |
|---|---|---|
| 現状と修正案で結果が変わった行 | 288 行（入力 72 件 = A 52 件 + B 20 件） | 同じ |
| うち C 群 | 0 行 | 0 行 |
| 変化した条件 | 72 件すべてで Combined / NativeOnly × ja-JP / en-US の 4 条件ちょうど。UtfUnknownOnly では 1 件も変化していない | 同じ |
| 同じビルドの net48 と net10.0 の差 | 現状 0 行、修正案 0 行（コマンドも 0 行） | — |

---

## 3. 結果：既存のテスト（修正案のビルド）

### 3.1 件数

| テスト | 結果 |
|---|---|
| `EncodingProbe.Tests`（net10.0） | 1179 件すべて合格 |
| `EncodingProbe.Tests`（net48） | 1189 件すべて合格 |
| `EncodingProbe.PowerShell.Tests`（net10.0） | 1079 件中 **12 件失敗**、1067 件合格 |

PSCompat（`tests/PSCompat/Invoke-ProbedCompatTests.ps1`）は修正案のビルドでは実行していない。

### 3.2 失敗した 12 件

| テスト | 期待 | 実際 |
|---|---|---|
| `ResolveEncodingCmdletTests.DetectionLineCount_UnicodeWithoutBom_AsciiHead_ReportsUsAscii`（codePage = 1200 / 1201 / 12000 / 12001 の 4 件） | 20127 | 1200 / 1201 / 12000 / 12001（入力の文字エンコーディング） |
| `ImportProbedCsvTests.Import_DetectionLineCount_UnicodeWithoutBomAsciiHead_ReadsCorrectly`（utf16le / utf16be / utf32le / utf32be × japaneseTail = True / False の 8 件） | 20127 | 1200 / 1201 / 12000 / 12001（入力の文字エンコーディング） |

12 件とも、`Resolve-Encoding -DetectionLineCount 2` が `20127` を返すことを確かめる行（`ResolveEncodingCmdletTests.cs` 268 行、`ImportProbedCsvTests.cs` 388 行）で失敗した。
`ImportProbedCsvTests` の 8 件は、この行で止まったため、その後の `Import-ProbedCsv` の値の検査は実行されていない。

---

## 4. UTF-32 / UTF-16 が成立する条件（要約 2・4 の根拠）

修正案の分岐に入る入力は「すべてのバイトが `0x80` 未満で、ESC シーケンスが無く、`0x00` を含む」ものだけである。この前提のもとで、各メソッドの条件は次のようになる（行番号は `feature/1.3.0-csv` の `SnowStack.EncodingProbe/EncodingDetector.cs`）。

### 4.1 `Utf32_Detection`（1208〜1331 行）

1. 長さが 4 の倍数で、4 バイト以上（1214〜1223 行）
2. LE / BE のどちらかのスコアが負でない（1275 行）。4 バイトの単位を符号位置として読み、`IsValidUtf32CodePoint`（1338 行）が偽ならスコアを -1000 にする（1256・1270 行）。
   すべてのバイトが `0x80` 未満なら、サロゲートや非文字の範囲には入らないので、偽になるのは `0x10FFFF` を超える場合だけである。LE なら 4 バイト目が `00` で 3 バイト目が `0x10` 以下、BE なら 1 バイト目が `00` で 2 バイト目が `0x10` 以下のときだけ妥当になる
3. NULL パターンが 1 つ以上ある（1314 行）。LE は「4 の倍数の位置に ASCII の範囲のバイト（`0x20`〜`0x7E`・`09`・`0A`・`0D`。`IsAsciiRangeByte`、1371 行）、続く 3 バイトが `00`」、BE はその逆の並び
4. エンディアンは NULL パターンの数が多い方。同数なら LE（1321 行）

補足（コードから読み取れること。今回の入力では測っていない）: 2 の -1000 は代入なので、それより後に妥当な単位が続くと 1〜2 ずつ加算される。妥当でない単位の後に、妥当な単位が約 500 個以上続くとスコアは負でなくなる。`Utf16_Detection` の -1000（1064・1081 行）も同じ。

### 4.2 `Utf16_Detection`（1018〜1142 行）

1. 長さが偶数で、2 バイト以上（1024〜1033 行）
2. LE / BE のどちらかのスコアが負でない（1092 行）。すべてのバイトが `0x80` 未満なら、2 バイトの単位は `0x7F7F` 以下で、サロゲート（`D800`〜`DFFF`）にも非文字（`FDD0`〜`FDEF`、`FFFE`、`FFFF`）にもならない。**前提のもとでは、この条件は必ず満たされる**
3. NULL パターンが 1 つ以上ある（1125 行）。LE は「偶数の位置に ASCII の範囲のバイト、その直後が `00`」（1111 行）、BE は「偶数の位置に `00`、その直後（奇数の位置）が ASCII の範囲のバイト」（1117 行）
4. エンディアンは NULL パターンの数が多い方。同数なら LE（1132 行）

したがって、前提のもとでは **UTF-16 が成立する条件は「長さが偶数」かつ「偶数の位置から始まる `<ASCII の範囲のバイト> 00` か `00 <ASCII の範囲のバイト>` の組が 1 つ以上ある」ことだけ**になる。

### 4.3 A がすべて成立した理由

A の UTF-16 の入力は、長さが偶数で、どの単位も `<文字> 00`（LE）か `00 <文字>`（BE）になっている。UTF-32 の入力は、どの単位も妥当な符号位置で（4.1 の 2）、NULL パターンも単位の数だけある。
UTF-32 の入力は UTF-16 の条件も満たす（表の「単独」の列が `LE / LE` など）が、UTF-32 を先に試すので UTF-32 になる。UTF-16 の入力は、`41 00 2C 00` のような並びを 4 バイトの単位で読むと `0x002C0041` となり、`0x10FFFF` を超えるので UTF-32 は成立しない。
例外は 1 文字の UTF-16（2 バイト）で、長さが 4 の倍数でないため UTF-32 は試されない（4.1 の 1）。

### 4.4 B で成立した入力と、成立した理由

| 入力 | 結果 | 長さ | `00` の数 | 成立した検査 |
|---|---|---|---|---|
| B1-13+1 `Hello, World!` + `00` | 1200 | 14 | 1 | UTF-16 の NULL パターン（LE）1 個: 偶数の位置 12 の `!`（`21`）の直後が `00`。UTF-32 は長さが 4 の倍数でない |
| B1-15+1 `Hello, World!!!` + `00` | 1200 | 16 | 1 | UTF-16 の NULL パターン（LE）1 個: 位置 14 の `!` の直後が `00`。UTF-32 は `Hell` = `0x6C6C6548` などが範囲外で LE / BE とも負 |
| B3-rec8-nonl-x3 / x4 | 1200 | 24 / 32 | 13 | UTF-16 の NULL パターン（LE）3 個（`ID001` の `1`、`Alice` の `e`、`B` の直後の `00` が偶数の位置から始まる）。UTF-32 は NULL パターン（LE）3 個があるが、`ID00` = `0x30304449` などが範囲外で LE / BE とも負 |
| B3-rec8-crlf-x3 / x4 | 1200 | 30 / 40 | 13 | 同上（UTF-16 の NULL パターン（LE）3 個）。x3 は長さが 4 の倍数でない |
| B3-rec16-nonl-x3 / x4 | 1200 | 48 / 64 | 37 / 44 | UTF-16 の NULL パターン（LE）3 / 4 個。UTF-32 は範囲外の単位があり不成立 |
| B3-rec16-crlf-x3 / x4 | 1200 | 54 / 72 | 37 / 44 | UTF-16 の NULL パターン（LE）3 / 4 個。x3 は長さが 4 の倍数でない |
| B3-rec17-nonl-x4 | 1200 | 68 | 48 | UTF-16 の NULL パターンが LE 2 個・BE 2 個で同数なので LE。x3（51 バイト）は奇数で不成立 |
| B3-rec17-crlf-x4 | 1200 | 76 | 48 | 同上（LE 2 個・BE 2 個）。x3（57 バイト）は奇数で不成立 |
| B3-rec32-nonl-x3 / x4 | 1200 | 96 / 128 | 85 / 108 | UTF-16 の NULL パターン（LE）3 / 4 個 |
| B3-rec32-crlf-x3 / x4 | 1200 | 102 / 136 | 85 / 108 | UTF-16 の NULL パターン（LE）3 / 4 個 |
| B5-1+13 `00` + `Hello, World!` | **1201** | 14 | 1 | UTF-16 の NULL パターン（BE）1 個: 位置 0 の `00` の直後が `H` |
| B7-10 `A 00 B 00 C 00 D E 00 00` | 1200 | 10 | 5 | UTF-16 の NULL パターン（LE）3 個（`A 00` `B 00` `C 00`）。`D E` `00 00` の単位も妥当な UTF-16 の単位 |
| B8-12 `A 000000 B 000000 C D 0000` | **12000** | 12 | 8 | UTF-32 の NULL パターン（LE）2 個、3 単位とも LE で妥当な符号位置（`0x41`、`0x42`、`0x4443`） |
| B8-16 `A 000000 B 000000 C 00 D 00 000000` | 1200 | 16 | 12 | UTF-32 は 3 単位目 `43 00 44 00` が LE で `0x00440043`（範囲外）、BE で `0x43004400`（範囲外）なので不成立。UTF-16 の NULL パターン（LE）4 個 |

B で成立しなかった 19 件の理由:

| 入力 | 不成立の理由 |
|---|---|
| B1-12+1（13 バイト）、B2-12+3（15）、B3-rec17-nonl-x3（51）、B3-rec17-crlf-x3（57）、B4-abc（9）、B4-print0（21）、B6-zero1（1）、B6-zero3（3）、B7-9（9）、B8-13（13）、B8-11（11） | 長さが奇数（UTF-16 の 1）。UTF-32 も長さが 4 の倍数でない |
| B2-12+2（14）、B2-12+4（16）、B2-12+100（112） | NULL パターンが 0 個。`Hello, World` は 12 文字で、最後の `d` は奇数の位置 11 にあり、`00` は偶数の位置 12 から始まる。`00` の後に ASCII の範囲のバイトが続かない |
| B5-4+12（16） | NULL パターンが 0 個（`00` が 4 つ並んだ後、偶数の位置 4 から `Hello, World` が続く。`00` と ASCII の範囲のバイトが同じ単位に入らない）。UTF-32 は 2 単位目 `Hell` 以降が範囲外 |
| B6-zero2 / zero4 / zero8 / zero1024 | NULL パターンが 0 個（`00` は ASCII の範囲のバイトではない）。UTF-32 の符号位置としては妥当（すべて `0x0`）だが、NULL パターンが無いので不成立 |

---

## 5. 結果：コマンド

値は net10.0。net48 と完全に一致した。`Get-ProbedContent` と `Import-ProbedCsv` は、文字を 16 進のコード（`0x100` 未満は 2 桁、それ以上は 4 桁）で示す。
`Import-ProbedCsv` の「n 行」は出力したオブジェクトの数。

| 入力 | コマンド | 現状 | 修正案 | 変化 |
|---|---|---|---|---|
| A1-crlf-u16le | `Resolve-Encoding` | 20127 / us-ascii / CrLf | 1200 / utf-16 / CrLf | **変化** |
| | `Resolve-Encoding -DetectionLineCount 1` | 20127 / us-ascii / CrLf | 1200 / utf-16 / CrLf | **変化** |
| | `Get-ProbedContent` | 7 行; 1 行目 `41 00 2C 00 42 00` | 3 行; 1 行目 `41 2C 42` | **変化** |
| | `Import-ProbedCsv` | 6 行; 列名 `[41 00] [00 42 00]`; 1 行目 `[00] []` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | **変化** |
| | `Import-ProbedCsv -DetectionLineCount 1` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | 同左 | — |
| A1-crlf-u16be | `Resolve-Encoding` | 20127 / us-ascii / CrLf | 1201 / unicodeFFFE / CrLf | **変化** |
| | `Resolve-Encoding -DetectionLineCount 1` | 20127 / us-ascii / CrLf | 1201 / unicodeFFFE / CrLf | **変化** |
| | `Get-ProbedContent` | 6 行; 1 行目 `00 41 00 2C 00 42 00` | 3 行; 1 行目 `41 2C 42` | **変化** |
| | `Import-ProbedCsv` | 5 行; 列名 `[00 41 00] [00 42 00]`; 1 行目 `[00] []` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | **変化** |
| | `Import-ProbedCsv -DetectionLineCount 1` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | 同左 | — |
| A1-crlf-u32le | `Resolve-Encoding` | 20127 / us-ascii / CrLf | 12000 / utf-32 / CrLf | **変化** |
| | `Resolve-Encoding -DetectionLineCount 1` | 20127 / us-ascii / CrLf | 12000 / utf-32 / CrLf | **変化** |
| | `Get-ProbedContent` | 7 行; 1 行目 `41 00 00 00 2C 00 00 00 42 00 00 00` | 3 行; 1 行目 `41 2C 42` | **変化** |
| | `Import-ProbedCsv` | 6 行; 列名 `[41 00 00 00] [00 00 00 42 00 00 00]`; 1 行目 `[00 00 00] []` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | **変化** |
| | `Import-ProbedCsv -DetectionLineCount 1` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | 同左 | — |
| A1-crlf-u32be | `Resolve-Encoding` | 20127 / us-ascii / CrLf | 12001 / utf-32BE / CrLf | **変化** |
| | `Resolve-Encoding -DetectionLineCount 1` | 20127 / us-ascii / CrLf | 12001 / utf-32BE / CrLf | **変化** |
| | `Get-ProbedContent` | 6 行; 1 行目 `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00` | 3 行; 1 行目 `41 2C 42` | **変化** |
| | `Import-ProbedCsv` | 5 行; 列名 `[00 00 00 41 00 00 00] [00 00 00 42 00 00 00]`; 1 行目 `[00 00 00] []` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | **変化** |
| | `Import-ProbedCsv -DetectionLineCount 1` | 2 行; 列名 `[41] [42]`; 1 行目 `[31] [32]` | 同左 | — |
| B1-13+1 | `Resolve-Encoding` | 20127 / us-ascii / None | 1200 / utf-16 / None | **変化** |
| | `Resolve-Encoding -DetectionLineCount 1` | 20127 / us-ascii / None | 1200 / utf-16 / None | **変化** |
| | `Get-ProbedContent` | 1 行; `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21 00` | 1 行; `6548 6C6C 2C6F 5720 726F 646C 21` | **変化** |
| | `Import-ProbedCsv`（両方） | 0 行（ヘッダーだけ） | 0 行 | — |
| B3-rec8-nonl-x4 | `Resolve-Encoding`（両方） | 20127 / us-ascii / None | 1200 / utf-16 / None | **変化** |
| | `Get-ProbedContent` | 1 行; `49 44 30 30 31 00 00 00 41 6C 69 63 65 00 00 00 42 00 …` | 1 行; `4449 3030 31 00 6C41 6369 65 00 42 00 00 00 6843 …` | **変化** |
| | `Import-ProbedCsv`（両方） | 0 行 | 0 行 | — |
| B3-rec16-crlf-x3 | `Resolve-Encoding`（両方） | 20127 / us-ascii / CrLf | 1200 / utf-16 / CrLf | **変化** |
| | `Get-ProbedContent` | 3 行; 1 行目 `49 44 30 30 31 00 …`（`00` × 11） | 1 行; `4449 3030 31 00 00 00 00 00 0A0D 6C41 …`（`0D 0A` が U+0A0D になる） | **変化** |
| | `Import-ProbedCsv` | 2 行; 列名 `[49 44 30 30 31 00 …]`; 1 行目 `[41 6C 69 63 65 00 …]` | 0 行 | **変化** |
| | `Import-ProbedCsv -DetectionLineCount 1` | 同上 | 0 行 | **変化** |
| B6-zero2 / zero4 | すべて | 20127 / us-ascii / None、`Get-ProbedContent` は `00 00` / `00 00 00 00` の 1 行、`Import-ProbedCsv` は 0 行 | 同左 | — |

補足:

- 修正案の B3-rec16-crlf-x3 では、`LineBreak` が `CrLf` と報告される一方で、UTF-16LE として復号した文字列には改行が無い（`0D 0A` が奇数の位置から始まる単位 `0A0D` に入るため）。`LineBreak` は判定とは別の `DetectLineBreak`（1 バイト単位で数える）で決まる
- `Import-ProbedCsv` はどの入力でもエラーを出さなかった（`-DetectionLineCount` の検証の失敗も無い）
- A1 の `Import-ProbedCsv -DetectionLineCount 1` は、現状でも修正案でも同じ値になった。現状は 1.3.0 の読み替え（仕様書 3.2・U4）で UTF-16 / UTF-32 になる。修正案は判定の時点で UTF-16 / UTF-32 になっている（読み替えの表の「`20127 / us-ascii`」の行に該当しない）

---

## 6. 1.3.0 の文書・ヘルプ・テストとの食い違い（要約 6）

修正案のとおりに直した場合、次の記述が実際の挙動と合わなくなる（記述の位置を挙げるだけで、直し方は書かない）。

| 場所 | 現在の記述 | 修正案での挙動 |
|---|---|---|
| 仕様書 3.2 の末尾（171 行）・6.3（311 行） | `Resolve-Encoding` は、先頭 N 行が英数字だけの BOM 無し UTF-16 / UTF-32 に `us-ascii` を報告する | `1200` / `1201` / `12000` / `12001` を報告する（A1 の `Resolve-Encoding -DetectionLineCount 1`） |
| 仕様書 3.2（155〜167 行）の読み替えの表と「UTF-16 / UTF-32 の行が必要な理由」 | 判定処理は `0x00` を含んでいても ASCII と判定するので、UTF-16 / UTF-32 への読み替えが必要 | A の入力は判定の時点で UTF-16 / UTF-32 になり、読み替えの表の UTF-16 / UTF-32 の行に該当しない。読み替えの結果（読める値）は A1 では変わらない |
| 仕様書 24 章 U4（700 行）・24.1（704〜705 行） | 使わないと U+0000 混じりで読む／全体で判定しても us-ascii になると考えられる | 全体で判定しても UTF-16 / UTF-32 になる（A1 の `Resolve-Encoding`・`Get-ProbedContent`・`Import-ProbedCsv`） |
| MAML `Resolve-Encoding` の `-DetectionLineCount`（en-US・ja-JP など 3401 行。5 言語と zh-HK / zh-MO の複製） | "this also applies to UTF-16 and UTF-32 files without a BOM whose first N lines contain only alphanumeric characters" / 「BOM 無しの UTF-16 / UTF-32 で先頭 N 行が英数字だけの場合も us-ascii になります」 | us-ascii にならない |
| MAML `Import-ProbedCsv` / `Export-ProbedCsv` の `-DetectionLineCount`（ja-JP 2358 行・2889 行など） | 先頭 N 行が ASCII だけの場合は UTF-8、改行のバイト列から UTF-16 / UTF-32 と分かる場合はその文字エンコーディング | 記述そのものは挙動と矛盾しない。ただし A 型の入力では判定の時点で UTF-16 / UTF-32 になるので、「改行のバイト列から分かる場合」の経路を通らない |
| 動作確認手順書 1.2（77 行） | `Resolve-Encoding .\u16_ascii.csv -DetectionLineCount 1   # us-ascii（判定処理の既存の挙動。仕様書 3.2）` | `1200` になる |
| 実装記録 6.6（400 行） | 英数字だけの BOM 無しの UTF-32BE を全体で判定すると us-ascii と判定され、列名にも値にも U+0000 が混じる。PSCompat はこの現状の挙動も記録している | UTF-32BE と判定され、U+0000 は混じらない（A1-crlf-u32be） |
| CHANGELOG 1.3.0 の既知の課題（55 行） | BOM 無しの UTF-16 / UTF-32 で英数字だけのファイルを全体で判定すると、`us-ascii` と判定される | UTF-16 / UTF-32 と判定される |
| モジュールのテスト 12 件（3.2） | `-DetectionLineCount` で `20127` を期待 | 失敗する |
| PSCompat（`tests/PSCompat/ProbedCompatScenarios.ps1` 1632 行付近の UTF-16 / UTF-32 の `ascii-head`） | 実装記録 6.6 のとおり、現状の挙動を記録している | 修正案のビルドでは実行していない。PSCompat は 2 つのホストの結果を突き合わせるもので、本調査では net48 と net10.0 の差は 0 行だった |

B 型の入力（英数字の後に `00` を付けたもの、`00` で埋めた固定長レコードなど）では、1.3.0 の文書に記述は無いが、次のとおり結果が変わる（5 章）。

- `Resolve-Encoding` が `1200`（B5-1+13 は `1201`、B8-12 は `12000`）を報告する
- `Get-ProbedContent` は、2 バイトずつ組にした文字（`6548` = `He` など）として読む
- `Import-ProbedCsv` は、B3-rec16-crlf-x3 で、現状の 2 行（`00` が混じった値）が 0 行になる。エラーは出ない

---

## 7. 第 2 回の測定（修正案 2：厳密な構造の検査）

- 測定日: 2026-10-20
- 依頼: `docs/EncodingProbe-1.3.0-実測依頼-NUL入りの入力の判定-第2回.md`
- 0〜6 章（第 1 回）は書き換えていない。本章の「修正案 1」は第 1 回の「修正案」と同じもの
- 本章も事実だけを記録する。望ましいかどうかの評価はしていない

### 7.0 要約

| # | 問い | 結果 |
|---|---|---|
| 1 | C（回帰）で、現状と修正案 2 の結果が変わった入力はあるか | **無い。** C の 60 件 × 6 条件 × 2 TFM で一致した。修正案 1 も第 1 回と同じく変化は無い |
| 2 | A（第 1 回の 52 件）で、修正案 2 が UTF-16 / UTF-32 と答えなかった入力はあるか | **無い。** 52 件すべてが、Combined / NativeOnly × ja-JP / en-US の 4 条件で入力どおりの幅とエンディアン（1200 / 1201 / 12000 / 12001）になった。結果は修正案 1 と同じ。UtfUnknownOnly は、修正案 1 と同じく 52 件とも `20127 / ascii` のまま |
| 3 | B（第 1 回の 39 件）で、修正案 2 が UTF-16 / UTF-32 と答えた入力はあるか | **無い。** 39 件すべてが `20127 / us-ascii` のまま。修正案 1 で UTF-16 / UTF-32 になった 20 件（B1 2、B3 14、B5 1、B7 1、B8 2）も、修正案 2 では `20127` |
| 4 | A6 の結果 | **予想どおり、11 件すべてが `20127 / us-ascii` のまま。** 修正案 2 の条件はどれも成立しなかった（7.3）。修正案 1 では 11 件すべてが入力どおりの UTF-16 / UTF-32 になった（`00 00` の単位を含む A6-4 も 1200） |
| 5 | B9〜B12 の結果 | 修正案 2 が UTF-16 / UTF-32 と答えたのは **18 件中 9 件**: B9 の 4 件すべて、B10 のうち 4 件（`41 42 43 00` だけ 20127）、B11 のうち 6 バイトの 1 件。**B12 の 6 件はすべて 20127。** 修正案 1 は 18 件中 14 件（B10 の `41 42 43 00`、B12 の 4 件を含む）。一覧は 7.2.4 |
| 6 | 判定方式・カルチャー・TFM で結果が違った箇所はあるか | **TFM の差は無い**（3 ビルドとも、判定 API 1080 行とコマンド 55 行が net48 と net10.0 で一致）。**カルチャーの差も、Combined と NativeOnly の差も無い**（`0x00` を含む 130 件のすべて）。違いは UtfUnknownOnly だけで、`0x00` を含み ESC シーケンスも `0x80` 以上のバイトも無い入力には、3 ビルドとも `20127 / ascii` を返す |
| 7 | 修正案 2 のビルドでの既存のテスト・PSCompat の結果 | コアは全件通過（net10.0 1179 件 / net48 1189 件）。**モジュールは 12 件失敗**（net10.0 1079 件中）。第 1 回と同じ 12 件で、実際の値も同じ（7.6）。**PSCompat は 691 シナリオすべてで 5.1 と 7.x の結果が一致した**（不一致 0 件） |

補足:

- 修正案 1 と修正案 2 で結果が違ったのは **36 件**（B の 20 件、A6 の 11 件、B10 の 1 件、B12 の 4 件）。36 件とも、修正案 1 が UTF-16 / UTF-32 と答え、修正案 2 が `20127` と答えた。逆の向き（修正案 2 だけが UTF-16 / UTF-32）は 0 件
- 第 2 回で測り直した現状と修正案 1 の結果は、第 1 回と重なる 151 件で第 1 回の結果と完全に一致した（判定 API 906 行 × 4 通り、コマンド 45 行 × 4 通り。不一致 0 行）
- 修正案 2 の「ASCII に戻る」（依頼 2 章の表の 5）は守られている。`0x00` を含み修正案 2 の条件が成立しなかった入力は、ISO-2022 の C2 の 2 件（`50220`）を除き、すべて `20127 / us-ascii` で、65001 などに変わったものは無い

### 7.1 測定の方法

#### 7.1.1 比べた 3 つのビルド

| ビルド | ブランチ / コミット | 内容 |
|---|---|---|
| 現状 | `feature/1.3.0-csv`（`a39a702`） | クラスライブラリは master と同じ（第 1 回の `eae172e` からクラスライブラリの変更は無い） |
| 修正案 1 | `investigate/1.3.0-nul-ascii`（`c58d23a`） | 第 1 回のまま。変更していない |
| 修正案 2 | `investigate/1.3.0-nul-ascii-strict`（`82fdf6c`。`a39a702` から作成） | 依頼 2 章の厳密な検査だけを `EncodingDetector.cs` に入れたもの。テストは追加も整理もしていない。どこにもマージしない |

修正案 2 の差分（`git diff feature/1.3.0-csv investigate/1.3.0-nul-ascii-strict`）:

```diff
diff --git a/SnowStack.EncodingProbe/EncodingDetector.cs b/SnowStack.EncodingProbe/EncodingDetector.cs
index 4c991ec..22982ea 100644
--- a/SnowStack.EncodingProbe/EncodingDetector.cs
+++ b/SnowStack.EncodingProbe/EncodingDetector.cs
@@ -491,6 +491,17 @@ namespace SnowStack.EncodingProbe
                 {
                     encInfo.CodePage = CodePageAscii;
                     encInfo.Bom = false;
+
+                    // 【実験・修正案 2】0x00 を含む場合だけ、すべての単位が成り立つ UTF-32 / UTF-16 を試す。
+                    // 成立しなければ ASCII のまま（UTF-8 には進まない）
+                    if (Array.IndexOf(this._buffer, (byte)0x00, 0, this.BufferSize) >= 0)
+                    {
+                        int strictCodePage = StrictNulUnicode_Detection();
+                        if (strictCodePage > 0)
+                        {
+                            encInfo.CodePage = strictCodePage;
+                        }
+                    }
                 }
 
                 encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
@@ -1010,6 +1021,47 @@ namespace SnowStack.EncodingProbe
             return outOfSpecification;
         }
 
+        /// <summary>
+        /// 【実験・修正案 2】0x00 を含み、すべてのバイトが 0x80 未満の入力を、オフセット 0 から単位に区切り、
+        /// すべての単位が形に合うときだけ UTF-32 / UTF-16 と判定する
+        /// </summary>
+        /// <returns>成立したコードページ。成立しなければ 0</returns>
+        private int StrictNulUnicode_Detection()
+        {
+            int length = this.BufferSize;
+
+            if (length >= 4 && length % 4 == 0)
+            {
+                if (AllStrictUnits(4, 0)) return CodePageUtf32Le;
+                if (AllStrictUnits(4, 3)) return CodePageUtf32Be;
+            }
+
+            if (length >= 2 && length % 2 == 0)
+            {
+                if (AllStrictUnits(2, 0)) return CodePageUtf16Le;
+                if (AllStrictUnits(2, 1)) return CodePageUtf16Be;
+            }
+
+            return 0;
+        }
+
+        /// <summary>
+        /// 【実験・修正案 2】すべての単位で、位置 nonZeroOffset のバイトが 00 以外、残りのバイトが 00 であるか
+        /// </summary>
+        private bool AllStrictUnits(int unitSize, int nonZeroOffset)
+        {
+            for (int i = 0; i < this.BufferSize; i += unitSize)
+            {
+                for (int k = 0; k < unitSize; k++)
+                {
+                    bool isZero = this._buffer[i + k] == 0x00;
+                    if (isZero == (k == nonZeroOffset)) return false;
+                }
+            }
+
+            return true;
+        }
+
         /// <summary>
         /// UTF-16であるか判定する
         /// </summary>
```

この変更は、修正案 1 と同じく `ISO2022_Detection` が「ESC シーケンスの無い ASCII」と答えた分岐の中だけにある。既存の `Utf32_Detection` / `Utf16_Detection` は呼ばない。
単位はオフセット 0 から区切り、依頼 2 章の表の順（UTF-32LE → UTF-32BE → UTF-16LE → UTF-16BE）に、**すべての単位**が形に合うかを見る。
「`00` 以外」の側のバイトは絞り込んでいない（この分岐の前提により `0x01`〜`0x7F`）。どれも成立しなければ `encInfo.CodePage` は `CodePageAscii`（20127）のままで、UTF-8 の判定には進まない。

#### 7.1.2 追加した入力（29 件）

入力はすべて、第 1 回と同じくスクリプトの中で符号位置または 16 進のリテラルからバイトを直接組み立てた。

| # | ID | 内容 |
|---|---|---|
| A6-1 | `A6-ue-u16le` / `-u16be` | `A,上` CRLF。上 = U+4E0A（LE で `0A 4E`、BE で `4E 0A`） |
| A6-2 | `A6-ji-*` / `A6-a-*` / `A6-touten-*`（各 u16le / u16be） | `A,<文字>` CRLF。**字 U+5B57**（`57 5B`）、**あ U+3042**（`42 30`）、**、U+3001**（`01 30`。制御文字の範囲のバイト `01` を含む） |
| A6-3 | `A6-ue-u32le` / `-u32be` | A6-1 を UTF-32 にしたもの（LE で `0A 4E 00 00`、BE で `00 00 4E 0A`） |
| A6-4 | `A6-nul-u16le` | `A,B` U+0000 `C` CRLF の UTF-16LE（`00 00` の単位がある） |
| B9 | `B9-4100` / `B9-0041` / `B9-0A00` / `B9-000A` | 2 バイト |
| B10 | `B10-41000000` / `B10-00000041` / `B10-41004200` / `B10-00410042` / `B10-41424300` | 4 バイト |
| B11 | `B11-410042` / `B11-4100420043` / `B11-410042004300` | 3・5・6 バイト |
| B12-1 | `B12-1-pad4k-body64` / `-body65` | 英数字のログ 2 行（CRLF。64 バイト）、またはその末尾に `x` を足したもの（65 バイト）の後ろを `00` で 4096 バイトまで埋めたもの |
| B12-2 | `B12-2-lines2` / `-lines10` | `<英数字 3 文字> 00 0D 0A` を 2 行・10 行（12 バイト・60 バイト） |
| B12-3 | `B12-3-even` / `-odd` | `id\tname\tmemo` CRLF の後に、空のフィールドに `00` を入れた 2 行（30 バイト・31 バイト） |

入力は第 1 回の 151 件と合わせて **180 件**、うち `0x00` を含むものは **130 件**。

#### 7.1.3 測定の条件

第 1 回（1.3）と同じ。判定方式 3 つ（Combined / NativeOnly / UtfUnknownOnly）× カルチャー 2 つ（ja-JP / en-US）× TFM 2 つ（net48 を Windows PowerShell 5.1、net10.0 を PowerShell 7.x で実行）を、3 つのビルドそれぞれ別のプロセスで測った。

- 診断に 2 列を追加した。`strict` は修正案 2 の `StrictNulUnicode_Detection` をリフレクションで単独に呼んだ結果（そのメソッドが無い現状と修正案 1 では `-`）。`strictrule` は、スクリプトの中で同じ規則を数え直した値で、依頼 2 章の表の番号（`1:UTF-32LE` 〜 `4:UTF-16BE`、不成立は `no`）。両者は 180 件のすべてで一致した（成立 61 件、不成立 119 件）
- コマンド: 第 1 回の 9 件（A1 の 4 件、B1-13+1、B3-rec8-nonl-x4、B3-rec16-crlf-x3、B6-zero2、B6-zero4）に、A6-ue-u16le（A6-1 の UTF-16LE）と B9-4100（`41 00`）を加えた 11 件。`-Culture ja-JP`、両ホスト

#### 7.1.4 成果物と再測定の手順

| 成果物 | 場所 |
|---|---|
| 測定スクリプト | `tools/Measure-NulInputDetection.ps1`（A6・B9〜B12 と、診断の `strict` / `strictrule` 列を追加） |
| 実行と集計 | `tools/Invoke-NulInputMeasurement.ps1`（`-StrictRoot` を指定すると 3 つのビルドを比べる。指定しなければ第 1 回と同じ） |
| 生の結果 | `tools/NulInputDetectionResults/round2/`（`detect-` / `diag-` / `commands-{current,proposal,strict}-{net10.0,net48}.tsv`）。第 1 回の結果（`tools/NulInputDetectionResults/` の直下）は残してある |
| 集計 | `round2/summary.txt`、`round2/table-net10.0-ja-JP.tsv`（全入力の 3 列の対照表）、`round2/table-commands-net10.0.tsv` |
| 既存テスト・PSCompat | `round2/tests-strict.txt` |

```powershell
# 修正案 1 と修正案 2 の worktree を短いパスに作ってビルドする
git worktree add ..\EP-nul investigate/1.3.0-nul-ascii
git worktree add ..\EP-nul2 investigate/1.3.0-nul-ascii-strict
dotnet build SnowStack.EncodingProbe.slnx -c Debug
dotnet build ..\EP-nul\SnowStack.EncodingProbe.slnx -c Debug
dotnet build ..\EP-nul2\SnowStack.EncodingProbe.slnx -c Debug

pwsh -NoProfile -File tools/Invoke-NulInputMeasurement.ps1 -ProposalRoot ..\EP-nul -StrictRoot ..\EP-nul2
```

### 7.2 結果：判定 API

表の値は net10.0 / ja-JP / NativeOnly のもの。7.4 のとおり、`0x00` を含む入力では Combined と NativeOnly、ja-JP と en-US、net48 と net10.0 の結果がすべて一致する。
値の並びは `CodePage / EncodingWebName / Bom / LineBreak`。「修正案 2 の規則」は `strictrule` 列（`—` は不成立）。

「印」の列:

- `1 で変化` … 現状と修正案 1 で結果が違う
- `2 で変化` … 現状と修正案 2 で結果が違う
- **`1≠2`** … 修正案 1 と修正案 2 で結果が違う（36 件）

C1（TestData の 50 ファイル）は `0x00` を含まず、3 つのビルドの結果は 6 条件 × 2 TFM のすべてで一致した。表は省き、C2〜C4 だけを載せる。

#### 7.2.1 A1〜A5（第 1 回の A）

| ID | 入力 | 長さ | `00` | 先頭のバイト | 修正案 2 の規則 | 現状 | 修正案 1 | 修正案 2 | 印 |
|---|---|---|---|---|---|---|---|---|---|
| A1-crlf-u16le | A,B / 1,2 / abc,def CRLF (u16le) | 38 | 19 | `41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 32 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1 で変化 2 で変化 |
| A1-crlf-u16be | A,B / 1,2 / abc,def CRLF (u16be) | 38 | 19 | `00 41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 32 …` | 4:UTF-16BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1 で変化 2 で変化 |
| A1-crlf-u32le | A,B / 1,2 / abc,def CRLF (u32le) | 76 | 57 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 1 で変化 2 で変化 |
| A1-crlf-u32be | A,B / 1,2 / abc,def CRLF (u32be) | 76 | 57 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 0D …` | 2:UTF-32BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | 12001 / utf-32BE / False / CrLf | 1 で変化 2 で変化 |
| A2-lf-u16le | A,B / 1,2 / abc,def LF (u16le) | 32 | 16 | `41 00 2C 00 42 00 0A 00 31 00 2C 00 32 00 0A 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / Lf | 1200 / utf-16 / False / Lf | 1200 / utf-16 / False / Lf | 1 で変化 2 で変化 |
| A2-lf-u16be | A,B / 1,2 / abc,def LF (u16be) | 32 | 16 | `00 41 00 2C 00 42 00 0A 00 31 00 2C 00 32 00 0A …` | 4:UTF-16BE | 20127 / us-ascii / False / Lf | 1201 / unicodeFFFE / False / Lf | 1201 / unicodeFFFE / False / Lf | 1 で変化 2 で変化 |
| A2-lf-u32le | A,B / 1,2 / abc,def LF (u32le) | 64 | 48 | `41 00 00 00 2C 00 00 00 42 00 00 00 0A 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / Lf | 12000 / utf-32 / False / Lf | 12000 / utf-32 / False / Lf | 1 で変化 2 で変化 |
| A2-lf-u32be | A,B / 1,2 / abc,def LF (u32be) | 64 | 48 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 0A …` | 2:UTF-32BE | 20127 / us-ascii / False / Lf | 12001 / utf-32BE / False / Lf | 12001 / utf-32BE / False / Lf | 1 で変化 2 で変化 |
| A2-cr-u16le | A,B / 1,2 / abc,def CR (u16le) | 32 | 16 | `41 00 2C 00 42 00 0D 00 31 00 2C 00 32 00 0D 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / Cr | 1200 / utf-16 / False / Cr | 1200 / utf-16 / False / Cr | 1 で変化 2 で変化 |
| A2-cr-u16be | A,B / 1,2 / abc,def CR (u16be) | 32 | 16 | `00 41 00 2C 00 42 00 0D 00 31 00 2C 00 32 00 0D …` | 4:UTF-16BE | 20127 / us-ascii / False / Cr | 1201 / unicodeFFFE / False / Cr | 1201 / unicodeFFFE / False / Cr | 1 で変化 2 で変化 |
| A2-cr-u32le | A,B / 1,2 / abc,def CR (u32le) | 64 | 48 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / Cr | 12000 / utf-32 / False / Cr | 12000 / utf-32 / False / Cr | 1 で変化 2 で変化 |
| A2-cr-u32be | A,B / 1,2 / abc,def CR (u32be) | 64 | 48 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 0D …` | 2:UTF-32BE | 20127 / us-ascii / False / Cr | 12001 / utf-32BE / False / Cr | 12001 / utf-32BE / False / Cr | 1 で変化 2 で変化 |
| A2-none-u16le | A,B;1,2;abc,def (改行なし) (u16le) | 30 | 15 | `41 00 2C 00 42 00 3B 00 31 00 2C 00 32 00 3B 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| A2-none-u16be | A,B;1,2;abc,def (改行なし) (u16be) | 30 | 15 | `00 41 00 2C 00 42 00 3B 00 31 00 2C 00 32 00 3B …` | 4:UTF-16BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 1201 / unicodeFFFE / False / None | 1 で変化 2 で変化 |
| A2-none-u32le | A,B;1,2;abc,def (改行なし) (u32le) | 60 | 45 | `41 00 00 00 2C 00 00 00 42 00 00 00 3B 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | 12000 / utf-32 / False / None | 1 で変化 2 で変化 |
| A2-none-u32be | A,B;1,2;abc,def (改行なし) (u32be) | 60 | 45 | `00 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 3B …` | 2:UTF-32BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | 12001 / utf-32BE / False / None | 1 で変化 2 で変化 |
| A3-1ch-u16le | 1 文字 A (u16le) | 2 | 1 | `41 00` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| A3-1ch-u16be | 1 文字 A (u16be) | 2 | 1 | `00 41` | 4:UTF-16BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 1201 / unicodeFFFE / False / None | 1 で変化 2 で変化 |
| A3-1ch-u32le | 1 文字 A (u32le) | 4 | 3 | `41 00 00 00` | 1:UTF-32LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | 12000 / utf-32 / False / None | 1 で変化 2 で変化 |
| A3-1ch-u32be | 1 文字 A (u32be) | 4 | 3 | `00 00 00 41` | 2:UTF-32BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | 12001 / utf-32BE / False / None | 1 で変化 2 で変化 |
| A3-2ch-u16le | 2 文字 AB (u16le) | 4 | 2 | `41 00 42 00` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| A3-2ch-u16be | 2 文字 AB (u16be) | 4 | 2 | `00 41 00 42` | 4:UTF-16BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 1201 / unicodeFFFE / False / None | 1 で変化 2 で変化 |
| A3-2ch-u32le | 2 文字 AB (u32le) | 8 | 6 | `41 00 00 00 42 00 00 00` | 1:UTF-32LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | 12000 / utf-32 / False / None | 1 で変化 2 で変化 |
| A3-2ch-u32be | 2 文字 AB (u32be) | 8 | 6 | `00 00 00 41 00 00 00 42` | 2:UTF-32BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | 12001 / utf-32BE / False / None | 1 で変化 2 で変化 |
| A3-10ch-u16le | 10 文字 0123456789 (u16le) | 20 | 10 | `30 00 31 00 32 00 33 00 34 00 35 00 36 00 37 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| A3-10ch-u16be | 10 文字 0123456789 (u16be) | 20 | 10 | `00 30 00 31 00 32 00 33 00 34 00 35 00 36 00 37 …` | 4:UTF-16BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 1201 / unicodeFFFE / False / None | 1 で変化 2 で変化 |
| A3-10ch-u32le | 10 文字 0123456789 (u32le) | 40 | 30 | `30 00 00 00 31 00 00 00 32 00 00 00 33 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | 12000 / utf-32 / False / None | 1 で変化 2 で変化 |
| A3-10ch-u32be | 10 文字 0123456789 (u32be) | 40 | 30 | `00 00 00 30 00 00 00 31 00 00 00 32 00 00 00 33 …` | 2:UTF-32BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | 12001 / utf-32BE / False / None | 1 で変化 2 で変化 |
| A3-1k-u16le | 1024 文字 (32 文字 CRLF × 32) (u16le) | 2048 | 1024 | `54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1 で変化 2 で変化 |
| A3-1k-u16be | 1024 文字 (32 文字 CRLF × 32) (u16be) | 2048 | 1024 | `00 54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 …` | 4:UTF-16BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1 で変化 2 で変化 |
| A3-1k-u32le | 1024 文字 (32 文字 CRLF × 32) (u32le) | 4096 | 3072 | `54 00 00 00 68 00 00 00 65 00 00 00 20 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 1 で変化 2 で変化 |
| A3-1k-u32be | 1024 文字 (32 文字 CRLF × 32) (u32be) | 4096 | 3072 | `00 00 00 54 00 00 00 68 00 00 00 65 00 00 00 20 …` | 2:UTF-32BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | 12001 / utf-32BE / False / CrLf | 1 で変化 2 で変化 |
| A3-40k-u16le | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u16le) | 80000 | 40000 | `54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1 で変化 2 で変化 |
| A3-40k-u16be | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u16be) | 80000 | 40000 | `00 54 00 68 00 65 00 20 00 71 00 75 00 69 00 63 …` | 4:UTF-16BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1 で変化 2 で変化 |
| A3-40k-u32le | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u32le) | 160000 | 120000 | `54 00 00 00 68 00 00 00 65 00 00 00 20 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 1 で変化 2 で変化 |
| A3-40k-u32be | 40000 文字 (32 文字 CRLF × 1250、UTF-16 で 80000 バイト) (u32be) | 160000 | 120000 | `00 00 00 54 00 00 00 68 00 00 00 65 00 00 00 20 …` | 2:UTF-32BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | 12001 / utf-32BE / False / CrLf | 1 で変化 2 で変化 |
| A4-printable-u16le | 0x20〜0x7E を一巡 (改行なし) (u16le) | 190 | 95 | `20 00 21 00 22 00 23 00 24 00 25 00 26 00 27 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| A4-printable-u16be | 0x20〜0x7E を一巡 (改行なし) (u16be) | 190 | 95 | `00 20 00 21 00 22 00 23 00 24 00 25 00 26 00 27 …` | 4:UTF-16BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 1201 / unicodeFFFE / False / None | 1 で変化 2 で変化 |
| A4-printable-u32le | 0x20〜0x7E を一巡 (改行なし) (u32le) | 380 | 285 | `20 00 00 00 21 00 00 00 22 00 00 00 23 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | 12000 / utf-32 / False / None | 1 で変化 2 で変化 |
| A4-printable-u32be | 0x20〜0x7E を一巡 (改行なし) (u32be) | 380 | 285 | `00 00 00 20 00 00 00 21 00 00 00 22 00 00 00 23 …` | 2:UTF-32BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | 12001 / utf-32BE / False / None | 1 で変化 2 で変化 |
| A4-tab-u16le | 数字・記号・タブ・空白 CRLF (u16le) | 48 | 24 | `31 00 09 00 32 00 09 00 33 00 0D 00 0A 00 2D 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1 で変化 2 で変化 |
| A4-tab-u16be | 数字・記号・タブ・空白 CRLF (u16be) | 48 | 24 | `00 31 00 09 00 32 00 09 00 33 00 0D 00 0A 00 2D …` | 4:UTF-16BE | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1 で変化 2 で変化 |
| A4-tab-u32le | 数字・記号・タブ・空白 CRLF (u32le) | 96 | 72 | `31 00 00 00 09 00 00 00 32 00 00 00 09 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 1 で変化 2 で変化 |
| A4-tab-u32be | 数字・記号・タブ・空白 CRLF (u32be) | 96 | 72 | `00 00 00 31 00 00 00 09 00 00 00 32 00 00 00 09 …` | 2:UTF-32BE | 20127 / us-ascii / False / CrLf | 12001 / utf-32BE / False / CrLf | 12001 / utf-32BE / False / CrLf | 1 で変化 2 で変化 |
| A5-c03-u16le | CSV c03 (1.3.0 実測 M1) (u16le) | 52 | 26 | `4E 00 61 00 6D 00 65 00 2C 00 4D 00 65 00 6D 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1 で変化 2 で変化 |
| A5-c03-u32le | CSV c03 (1.3.0 実測 M1) (u32le) | 104 | 78 | `4E 00 00 00 61 00 00 00 6D 00 00 00 65 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 1 で変化 2 で変化 |
| A5-c05-u16le | CSV c05 (1.3.0 実測 M1) (u16le) | 54 | 27 | `23 00 20 00 63 00 6F 00 6D 00 6D 00 65 00 6E 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1 で変化 2 で変化 |
| A5-c05-u32le | CSV c05 (1.3.0 実測 M1) (u32le) | 108 | 81 | `23 00 00 00 20 00 00 00 63 00 00 00 6F 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 1 で変化 2 で変化 |
| A5-c09-u16le | CSV c09 (1.3.0 実測 M1) (u16le) | 36 | 18 | `41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 32 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1 で変化 2 で変化 |
| A5-c09-u32le | CSV c09 (1.3.0 実測 M1) (u32le) | 72 | 54 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 1 で変化 2 で変化 |
| A5-c12-u16le | CSV c12 (1.3.0 実測 M1) (u16le) | 24 | 12 | `41 00 2C 00 42 00 0D 00 31 00 2C 00 32 00 0D 00 …` | 3:UTF-16LE | 20127 / us-ascii / False / Cr | 1200 / utf-16 / False / Cr | 1200 / utf-16 / False / Cr | 1 で変化 2 で変化 |
| A5-c12-u32le | CSV c12 (1.3.0 実測 M1) (u32le) | 48 | 36 | `41 00 00 00 2C 00 00 00 42 00 00 00 0D 00 00 00 …` | 1:UTF-32LE | 20127 / us-ascii / False / Cr | 12000 / utf-32 / False / Cr | 12000 / utf-32 / False / Cr | 1 で変化 2 で変化 |

#### 7.2.2 A6（修正案 2 では直らないと予想しているもの）

| ID | 入力 | 長さ | `00` | 先頭のバイト | 修正案 2 の規則 | 現状 | 修正案 1 | 修正案 2 | 印 |
|---|---|---|---|---|---|---|---|---|---|
| A6-ue-u16le | A,上 U+4E0A CRLF (u16le) | 10 | 4 | `41 00 2C 00 0A 4E 0D 00 0A 00` | — | 20127 / us-ascii / False / LfAndCrLf | 1200 / utf-16 / False / LfAndCrLf | 20127 / us-ascii / False / LfAndCrLf | 1 で変化 **1≠2** |
| A6-ue-u16be | A,上 U+4E0A CRLF (u16be) | 10 | 4 | `00 41 00 2C 4E 0A 00 0D 00 0A` | — | 20127 / us-ascii / False / LfAndCrLf | 1201 / unicodeFFFE / False / LfAndCrLf | 20127 / us-ascii / False / LfAndCrLf | 1 で変化 **1≠2** |
| A6-ji-u16le | A,字 U+5B57 CRLF (u16le) | 10 | 4 | `41 00 2C 00 57 5B 0D 00 0A 00` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| A6-ji-u16be | A,字 U+5B57 CRLF (u16be) | 10 | 4 | `00 41 00 2C 5B 57 00 0D 00 0A` | — | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| A6-a-u16le | A,あ U+3042 CRLF (u16le) | 10 | 4 | `41 00 2C 00 42 30 0D 00 0A 00` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| A6-a-u16be | A,あ U+3042 CRLF (u16be) | 10 | 4 | `00 41 00 2C 30 42 00 0D 00 0A` | — | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| A6-touten-u16le | A,、 U+3001 CRLF (u16le) | 10 | 4 | `41 00 2C 00 01 30 0D 00 0A 00` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| A6-touten-u16be | A,、 U+3001 CRLF (u16be) | 10 | 4 | `00 41 00 2C 30 01 00 0D 00 0A` | — | 20127 / us-ascii / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| A6-ue-u32le | A,上 U+4E0A CRLF (u32le) | 20 | 14 | `41 00 00 00 2C 00 00 00 0A 4E 00 00 0D 00 00 00 …` | — | 20127 / us-ascii / False / LfAndCrLf | 12000 / utf-32 / False / LfAndCrLf | 20127 / us-ascii / False / LfAndCrLf | 1 で変化 **1≠2** |
| A6-ue-u32be | A,上 U+4E0A CRLF (u32be) | 20 | 14 | `00 00 00 41 00 00 00 2C 00 00 4E 0A 00 00 00 0D …` | — | 20127 / us-ascii / False / LfAndCrLf | 12001 / utf-32BE / False / LfAndCrLf | 20127 / us-ascii / False / LfAndCrLf | 1 で変化 **1≠2** |
| A6-nul-u16le | A,B U+0000 C CRLF (u16le) | 14 | 8 | `41 00 2C 00 42 00 00 00 43 00 0D 00 0A 00` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |

#### 7.2.3 B1〜B8（第 1 回の B）

| ID | 入力 | 長さ | `00` | 先頭のバイト | 修正案 2 の規則 | 現状 | 修正案 1 | 修正案 2 | 印 |
|---|---|---|---|---|---|---|---|---|---|
| B1-13+1 | Hello, World! + 00 (14 バイト) | 14 | 1 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21 00` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B1-12+1 | Hello World! + 00 (13 バイト) | 13 | 1 | `48 65 6C 6C 6F 20 57 6F 72 6C 64 21 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B1-15+1 | Hello, World!!! + 00 (16 バイト) | 16 | 1 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21 21 21 00` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B2-12+2 | Hello, World + 00 × 2 | 14 | 2 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B2-12+3 | Hello, World + 00 × 3 | 15 | 3 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B2-12+4 | Hello, World + 00 × 4 | 16 | 4 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B2-12+100 | Hello, World + 00 × 100 | 112 | 100 | `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B3-rec8-nonl-x3 | レコード長 8、nonl、3 レコード (24 バイト) | 24 | 13 | `49 44 30 30 31 00 00 00 41 6C 69 63 65 00 00 00 …` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B3-rec8-nonl-x4 | レコード長 8、nonl、4 レコード (32 バイト) | 32 | 13 | `49 44 30 30 31 00 00 00 41 6C 69 63 65 00 00 00 …` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B3-rec8-crlf-x3 | レコード長 8、crlf、3 レコード (30 バイト) | 30 | 13 | `49 44 30 30 31 00 00 00 0D 0A 41 6C 69 63 65 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B3-rec8-crlf-x4 | レコード長 8、crlf、4 レコード (40 バイト) | 40 | 13 | `49 44 30 30 31 00 00 00 0D 0A 41 6C 69 63 65 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B3-rec16-nonl-x3 | レコード長 16、nonl、3 レコード (48 バイト) | 48 | 37 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B3-rec16-nonl-x4 | レコード長 16、nonl、4 レコード (64 バイト) | 64 | 44 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B3-rec16-crlf-x3 | レコード長 16、crlf、3 レコード (54 バイト) | 54 | 37 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B3-rec16-crlf-x4 | レコード長 16、crlf、4 レコード (72 バイト) | 72 | 44 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B3-rec17-nonl-x3 | レコード長 17、nonl、3 レコード (51 バイト) | 51 | 40 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B3-rec17-nonl-x4 | レコード長 17、nonl、4 レコード (68 バイト) | 68 | 48 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B3-rec17-crlf-x3 | レコード長 17、crlf、3 レコード (57 バイト) | 57 | 40 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / CrLf | 20127 / us-ascii / False / CrLf | 20127 / us-ascii / False / CrLf |  |
| B3-rec17-crlf-x4 | レコード長 17、crlf、4 レコード (76 バイト) | 76 | 48 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B3-rec32-nonl-x3 | レコード長 32、nonl、3 レコード (96 バイト) | 96 | 85 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B3-rec32-nonl-x4 | レコード長 32、nonl、4 レコード (128 バイト) | 128 | 108 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B3-rec32-crlf-x3 | レコード長 32、crlf、3 レコード (102 バイト) | 102 | 85 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B3-rec32-crlf-x4 | レコード長 32、crlf、4 レコード (136 バイト) | 136 | 108 | `49 44 30 30 31 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B4-abc | a 00 bb 00 ccc 00 | 9 | 3 | `61 00 62 62 00 63 63 63 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B4-print0 | dir1/file.txt 00 dir2/x 00 | 21 | 2 | `64 69 72 31 2F 66 69 6C 65 2E 74 78 74 00 64 69 …` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B5-1+13 | 00 + "Hello, World!" | 14 | 1 | `00 48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21` | — | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B5-4+12 | 00 × 4 + "Hello, World" | 16 | 4 | `00 00 00 00 48 65 6C 6C 6F 2C 20 57 6F 72 6C 64` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B6-zero1 | すべて 00 (1 バイト) | 1 | 1 | `00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B6-zero2 | すべて 00 (2 バイト) | 2 | 2 | `00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B6-zero3 | すべて 00 (3 バイト) | 3 | 3 | `00 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B6-zero4 | すべて 00 (4 バイト) | 4 | 4 | `00 00 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B6-zero8 | すべて 00 (8 バイト) | 8 | 8 | `00 00 00 00 00 00 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B6-zero1024 | すべて 00 (1024 バイト) | 1024 | 1024 | `00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 …` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B7-9 | A 00 B 00 C 00 D E 00 (9 バイト) | 9 | 4 | `41 00 42 00 43 00 44 45 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B7-10 | A 00 B 00 C 00 D E 00 00 (10 バイト) | 10 | 5 | `41 00 42 00 43 00 44 45 00 00` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B8-13 | A 000000 B 000000 C D 000000 (13 バイト) | 13 | 9 | `41 00 00 00 42 00 00 00 43 44 00 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B8-11 | A 000000 B 0000 C 000000 (11 バイト) | 11 | 8 | `41 00 00 00 42 00 00 43 00 00 00` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B8-12 | A 000000 B 000000 C D 0000 (12 バイト) | 12 | 8 | `41 00 00 00 42 00 00 00 43 44 00 00` | — | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B8-16 | A 000000 B 000000 C 00 D 00000000 00 (16 バイト) | 16 | 12 | `41 00 00 00 42 00 00 00 43 00 44 00 00 00 00 00` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |

#### 7.2.4 B9〜B12（区切りにくい短い入力・実在しそうな入力）

| ID | 入力 | 長さ | `00` | 先頭のバイト | 修正案 2 の規則 | 現状 | 修正案 1 | 修正案 2 | 印 |
|---|---|---|---|---|---|---|---|---|---|
| B9-4100 | 41 00 | 2 | 1 | `41 00` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| B9-0041 | 00 41 | 2 | 1 | `00 41` | 4:UTF-16BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 1201 / unicodeFFFE / False / None | 1 で変化 2 で変化 |
| B9-0A00 | 0A 00 | 2 | 1 | `0A 00` | 3:UTF-16LE | 20127 / us-ascii / False / Lf | 1200 / utf-16 / False / Lf | 1200 / utf-16 / False / Lf | 1 で変化 2 で変化 |
| B9-000A | 00 0A | 2 | 1 | `00 0A` | 4:UTF-16BE | 20127 / us-ascii / False / Lf | 1201 / unicodeFFFE / False / Lf | 1201 / unicodeFFFE / False / Lf | 1 で変化 2 で変化 |
| B10-41000000 | 41 00 00 00 | 4 | 3 | `41 00 00 00` | 1:UTF-32LE | 20127 / us-ascii / False / None | 12000 / utf-32 / False / None | 12000 / utf-32 / False / None | 1 で変化 2 で変化 |
| B10-00000041 | 00 00 00 41 | 4 | 3 | `00 00 00 41` | 2:UTF-32BE | 20127 / us-ascii / False / None | 12001 / utf-32BE / False / None | 12001 / utf-32BE / False / None | 1 で変化 2 で変化 |
| B10-41004200 | 41 00 42 00 | 4 | 2 | `41 00 42 00` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| B10-00410042 | 00 41 00 42 | 4 | 2 | `00 41 00 42` | 4:UTF-16BE | 20127 / us-ascii / False / None | 1201 / unicodeFFFE / False / None | 1201 / unicodeFFFE / False / None | 1 で変化 2 で変化 |
| B10-41424300 | 41 42 43 00 | 4 | 1 | `41 42 43 00` | — | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 20127 / us-ascii / False / None | 1 で変化 **1≠2** |
| B11-410042 | 41 00 42 | 3 | 1 | `41 00 42` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B11-4100420043 | 41 00 42 00 43 | 5 | 2 | `41 00 42 00 43` | — | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None | 20127 / us-ascii / False / None |  |
| B11-410042004300 | 41 00 42 00 43 00 | 6 | 3 | `41 00 42 00 43 00` | 3:UTF-16LE | 20127 / us-ascii / False / None | 1200 / utf-16 / False / None | 1200 / utf-16 / False / None | 1 で変化 2 で変化 |
| B12-1-pad4k-body64 | ログ 64 バイト + 00 で 4096 バイトまで | 4096 | 4032 | `32 30 32 36 2D 31 30 2D 32 30 20 31 30 3A 30 30 …` | — | 20127 / us-ascii / False / CrLf | 20127 / us-ascii / False / CrLf | 20127 / us-ascii / False / CrLf |  |
| B12-1-pad4k-body65 | ログ 65 バイト + 00 で 4096 バイトまで | 4096 | 4031 | `32 30 32 36 2D 31 30 2D 32 30 20 31 30 3A 30 30 …` | — | 20127 / us-ascii / False / CrLf | 12000 / utf-32 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B12-2-lines2 | <3 文字> 00 CRLF × 2 (12 バイト) | 12 | 2 | `61 62 63 00 0D 0A 64 65 66 00 0D 0A` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B12-2-lines10 | <3 文字> 00 CRLF × 10 (60 バイト) | 60 | 10 | `61 62 63 00 0D 0A 64 65 66 00 0D 0A 67 68 69 00 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B12-3-even | タブ区切り、空のフィールドに 00 (30 バイト) | 30 | 2 | `69 64 09 6E 61 6D 65 09 6D 65 6D 6F 0D 0A 31 09 …` | — | 20127 / us-ascii / False / CrLf | 1200 / utf-16 / False / CrLf | 20127 / us-ascii / False / CrLf | 1 で変化 **1≠2** |
| B12-3-odd | タブ区切り、空のフィールドに 00 (31 バイト) | 31 | 2 | `69 64 09 6E 61 6D 65 09 6D 65 6D 6F 0D 0A 31 09 …` | — | 20127 / us-ascii / False / CrLf | 20127 / us-ascii / False / CrLf | 20127 / us-ascii / False / CrLf |  |

#### 7.2.5 C2〜C4（第 1 回の C のうち C1 以外）

| ID | 入力 | 長さ | `00` | 先頭のバイト | 修正案 2 の規則 | 現状 | 修正案 1 | 修正案 2 | 印 |
|---|---|---|---|---|---|---|---|---|---|
| C2-mid | ESC$B 日本 ESC(B 00 A | 12 | 1 | `1B 24 42 46 7C 4B 5C 1B 28 42 00 41` | — | 50220 / iso-2022-jp / False / None | 50220 / iso-2022-jp / False / None | 50220 / iso-2022-jp / False / None |  |
| C2-wrap | A 00 ESC$B 日本 ESC(B CRLF 00 | 15 | 2 | `41 00 1B 24 42 46 7C 4B 5C 1B 28 42 0D 0A 00` | — | 50220 / iso-2022-jp / False / CrLf | 50220 / iso-2022-jp / False / CrLf | 50220 / iso-2022-jp / False / CrLf |  |
| C3-bom-u16le | A1 の内容 BOM 付き (u16le) | 40 | 19 | `FF FE 41 00 2C 00 42 00 0D 00 0A 00 31 00 2C 00 …` | — | 1200 / utf-16 / True / CrLf | 1200 / utf-16 / True / CrLf | 1200 / utf-16 / True / CrLf |  |
| C3-bom-u16be | A1 の内容 BOM 付き (u16be) | 40 | 19 | `FE FF 00 41 00 2C 00 42 00 0D 00 0A 00 31 00 2C …` | — | 1201 / unicodeFFFE / True / CrLf | 1201 / unicodeFFFE / True / CrLf | 1201 / unicodeFFFE / True / CrLf |  |
| C3-bom-u32le | A1 の内容 BOM 付き (u32le) | 80 | 59 | `FF FE 00 00 41 00 00 00 2C 00 00 00 42 00 00 00 …` | — | 12000 / utf-32 / True / CrLf | 12000 / utf-32 / True / CrLf | 12000 / utf-32 / True / CrLf |  |
| C3-bom-u32be | A1 の内容 BOM 付き (u32be) | 80 | 59 | `00 00 FE FF 00 00 00 41 00 00 00 2C 00 00 00 42 …` | — | 12001 / utf-32BE / True / CrLf | 12001 / utf-32BE / True / CrLf | 12001 / utf-32BE / True / CrLf |  |
| C4-ja-u16le | 日本語,ABC CRLF BOM 無し (u16le) | 18 | 6 | `E5 65 2C 67 9E 8A 2C 00 41 00 42 00 43 00 0D 00 …` | — | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf | 1200 / utf-16 / False / CrLf |  |
| C4-ja-u16be | 日本語,ABC CRLF BOM 無し (u16be) | 18 | 6 | `65 E5 67 2C 8A 9E 00 2C 00 41 00 42 00 43 00 0D …` | — | 1201 / unicodeFFFE / False / CrLf | 1201 / unicodeFFFE / False / CrLf | 1201 / unicodeFFFE / False / CrLf |  |
| C4-ja-u32le | 日本語,ABC CRLF BOM 無し (u32le) | 36 | 24 | `E5 65 00 00 2C 67 00 00 9E 8A 00 00 2C 00 00 00 …` | — | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf | 12000 / utf-32 / False / CrLf |  |
| C4-ja-u32be | 日本語,ABC CRLF BOM 無し (u32be) | 36 | 24 | `00 00 65 E5 00 00 67 2C 00 00 8A 9E 00 00 00 2C …` | — | 12001 / utf-32BE / False / CrLf | 12001 / utf-32BE / False / CrLf | 12001 / utf-32BE / False / CrLf |  |

### 7.3 修正案 2 で UTF-16 / UTF-32 になった入力と、成立した規則

修正案 2 が UTF-16 / UTF-32 と答えたのは 61 件で、すべて ISO-2022 / ASCII の分岐（`0x00` を含み、ESC シーケンスも `0x80` 以上のバイトも無い）に入った入力である。規則の番号は依頼 2 章の表のもの。

| 規則 | 件数 | 入力 |
|---|---|---|
| 1（UTF-32LE、12000） | 16 | A の `*-u32le` 15 件（A1 1、A2 3、A3 5、A4 2、A5 4）、B10-41000000 |
| 2（UTF-32BE、12001） | 12 | A の `*-u32be` 11 件（A1 1、A2 3、A3 5、A4 2）、B10-00000041 |
| 3（UTF-16LE、1200） | 19 | A の `*-u16le` 15 件（A1 1、A2 3、A3 5、A4 2、A5 4）、B9-4100、B9-0A00、B10-41004200、B11-410042004300 |
| 4（UTF-16BE、1201） | 14 | A の `*-u16be` 11 件（A1 1、A2 3、A3 5、A4 2）、B9-0041、B9-000A、B10-00410042 |

成立しなかった入力の理由（規則ごとに、長さの条件で外れたか、形に合わない単位があったか）:

| 入力 | 長さ | 規則 1・2（4 の倍数） | 規則 3・4（偶数） |
|---|---|---|---|
| A6-1・A6-2（UTF-16LE / BE、8 件） | 10 | 長さで外れる | 漢字・記号の単位（例: `0A 4E`、`57 5B`、`01 30`）は 2 バイトとも `00` 以外 |
| A6-3（UTF-32LE / BE、2 件） | 20 | 入力と同じエンディアンの規則は、上の単位 `0A 4E 00 00` / `00 00 4E 0A`（`00` 以外が 2 バイト）で外れる。反対のエンディアンの規則は先頭の単位で外れる | `00 00` の単位（LE の `2C 00 00 00` の後半など）がある |
| A6-4（UTF-16LE、1 件） | 14 | 長さで外れる | U+0000 の単位 `00 00` がある |
| B10-41424300 | 4 | 4 バイトのうち `00` 以外が 3 バイト | 先頭の単位 `41 42` が 2 バイトとも `00` 以外 |
| B11-410042 / B11-4100420043 | 3 / 5 | 長さで外れる | 長さで外れる |
| B12-1（2 件） | 4096 | 先頭の単位 `32 30 32 36` | 先頭の単位 `32 30`。後ろの埋め草は `00 00` |
| B12-2（2 件） | 12 / 60 | 先頭の単位 `61 62 63 00` | 先頭の単位 `61 62` |
| B12-3-even | 30 | 長さで外れる | 先頭の単位 `69 64` |
| B12-3-odd | 31 | 長さで外れる | 長さで外れる |
| B（第 1 回の 39 件） | — | 偶数長・4 の倍数長のものも、すべて形に合わない単位を 1 つ以上含む（B6 のすべて `00` は、`00` 以外が必要な位置が `00`） | 同左 |

修正案 1 では、A6 の 11 件、B10-41424300、B12-1-pad4k-body65（UTF-32LE）、B12-2 の 2 件、B12-3-even が UTF-16 / UTF-32 になった。修正案 1 で成立する条件は 4.1・4.2 のとおりで、本章では数え直していない。

### 7.4 判定方式・カルチャー・TFM による違い

- **TFM:** 3 つのビルドのすべてで、判定 API の 1080 行（180 件 × 6 条件）とコマンドの 55 行（11 件 × 5 コマンド）が net48 と net10.0 で一致した
- **カルチャー:** `0x00` を含む 130 件のすべてで、3 つのビルドとも ja-JP と en-US の結果が一致した
- **Combined と NativeOnly:** `0x00` を含む 130 件のすべてで、3 つのビルドとも一致した。修正案 2 で結果が変わった 61 件は、Combined / NativeOnly × 2 カルチャーの 4 条件のすべてで変わり、UtfUnknownOnly では変わらなかった
- **UtfUnknownOnly:** ISO-2022 / ASCII の分岐に入る `0x00` 入りの 120 件（130 件から、ESC シーケンスのある C2 の 2 件、BOM 付きの C3 の 4 件、日本語を含む C4 の 4 件を除いたもの）に、3 つのビルドとも `20127 / ascii` を返した

### 7.5 結果：コマンド

値は net10.0。net48 と完全に一致した。表記は 5 章と同じ（文字は 16 進のコード。`Import-ProbedCsv` の「n 行」は出力したオブジェクトの数）。どの入力・どのビルドでもエラーは出なかった。
第 1 回と同じ 9 件の、現状と修正案 1 の値は第 1 回（5 章）と同じだった。

| 入力 | コマンド | 現状 | 修正案 1 | 修正案 2 | 印 |
|---|---|---|---|---|---|
| A1（4 件） | すべて | 5 章の「現状」と同じ | 5 章の「修正案」と同じ | 修正案 1 と同じ | 1 で変化、2 で変化（`Import-ProbedCsv -DetectionLineCount 1` は 3 つとも同じ） |
| B1-13+1 | `Resolve-Encoding`（両方） | 20127 / us-ascii / None | 1200 / utf-16 / None | 20127 / us-ascii / None | **1≠2** |
| | `Get-ProbedContent` | 1 行; `48 65 6C 6C 6F 2C 20 57 6F 72 6C 64 21 00` | 1 行; `6548 6C6C 2C6F 5720 726F 646C 21` | 現状と同じ | **1≠2** |
| | `Import-ProbedCsv`（両方） | 0 行 | 0 行 | 0 行 | — |
| B3-rec8-nonl-x4 | `Resolve-Encoding`（両方） | 20127 / us-ascii / None | 1200 / utf-16 / None | 20127 / us-ascii / None | **1≠2** |
| | `Get-ProbedContent` | 1 行; `49 44 30 30 31 00 00 00 41 6C 69 63 65 00 00 00 42 00 …` | 1 行; `4449 3030 31 00 6C41 6369 65 00 42 00 00 00 6843 …` | 現状と同じ | **1≠2** |
| | `Import-ProbedCsv`（両方） | 0 行 | 0 行 | 0 行 | — |
| B3-rec16-crlf-x3 | `Resolve-Encoding`（両方） | 20127 / us-ascii / CrLf | 1200 / utf-16 / CrLf | 20127 / us-ascii / CrLf | **1≠2** |
| | `Get-ProbedContent` | 3 行; 1 行目 `49 44 30 30 31 00 …`（`00` × 11） | 1 行; `4449 3030 31 00 00 00 00 00 0A0D 6C41 …` | 現状と同じ | **1≠2** |
| | `Import-ProbedCsv`（両方） | 2 行; 列名 `[49 44 30 30 31 00 …]`; 1 行目 `[41 6C 69 63 65 00 …]` | 0 行 | 現状と同じ | **1≠2** |
| B6-zero2 / zero4 | すべて | 20127 / us-ascii / None、`Get-ProbedContent` は `00 00` / `00 00 00 00` の 1 行、`Import-ProbedCsv` は 0 行 | 同左 | 同左 | — |
| A6-ue-u16le | `Resolve-Encoding`（両方） | 20127 / us-ascii / LfAndCrLf | 1200 / utf-16 / LfAndCrLf | 20127 / us-ascii / LfAndCrLf | **1≠2** |
| | `Get-ProbedContent` | 4 行; 1 行目 `41 00 2C 00` | 1 行; `41 2C 4E0A` | 現状と同じ | **1≠2** |
| | `Import-ProbedCsv` | 3 行; 列名 `[41 00] [00]`; 1 行目 `[4E] []` | 0 行 | 現状と同じ | **1≠2** |
| | `Import-ProbedCsv -DetectionLineCount 1` | 0 行 | 0 行 | 0 行 | — |
| B9-4100 | `Resolve-Encoding`（両方） | 20127 / us-ascii / None | 1200 / utf-16 / None | 1200 / utf-16 / None | 1 で変化、2 で変化 |
| | `Get-ProbedContent` | 1 行; `41 00` | 1 行; `41` | 1 行; `41` | 1 で変化、2 で変化 |
| | `Import-ProbedCsv`（両方） | 0 行 | 0 行 | 0 行 | — |

補足:

- A6-ue-u16le の `LineBreak` が `LfAndCrLf` になるのは、上（U+4E0A）の下位バイト `0A` を、`DetectLineBreak`（1 バイト単位で数える）が LF と数えるためである（3 つのビルドで同じ）。現状と修正案 2 の `Get-ProbedContent` が 4 行になるのも、このバイトで行が分かれるためである
- A6-ue-u16le の `Import-ProbedCsv -DetectionLineCount 1` は、3 つのビルドとも 0 行で、エラーも出なかった。理由は本調査では調べていない
- 修正案 1 の A6-ue-u16le は、`Get-ProbedContent` では `A,上` の 1 行として読み、`Import-ProbedCsv` ではヘッダーだけの 0 行になった

### 7.6 既存のテストと PSCompat（修正案 2 のビルド）

| 対象 | 結果 |
|---|---|
| `EncodingProbe.Tests`（net10.0） | 成功 1179 / 1179 |
| `EncodingProbe.Tests`（net48） | 成功 1189 / 1189 |
| `EncodingProbe.PowerShell.Tests`（net10.0） | **失敗 12**、成功 1067 / 1079 |
| PSCompat（`tests/PSCompat/Invoke-ProbedCompatTests.ps1` を修正案 2 の worktree で実行） | 691 シナリオすべてで 5.1 と 7.x の結果が一致（不一致 0 件） |

失敗した 12 件は、第 1 回（3.2）と同じテストで、実際の値も同じだった。

| テスト | 件数 | 期待 | 実際 |
|---|---|---|---|
| `ResolveEncodingCmdletTests.DetectionLineCount_UnicodeWithoutBom_AsciiHead_ReportsUsAscii`（`codePage` = 1200 / 1201 / 12000 / 12001） | 4 | 20127 | 入力の `codePage` と同じ値 |
| `ImportProbedCsvTests.Import_DetectionLineCount_UnicodeWithoutBomAsciiHead_ReadsCorrectly`（`label` = utf16le / utf16be / utf32le / utf32be × `japaneseTail` = True / False） | 8 | 20127 | utf16le 1200、utf16be 1201、utf32le 12000、utf32be 12001 |

PSCompat は 2 つのホストの結果を突き合わせるもので、現状のビルドとの比較はしていない（第 1 回の修正案 1 のビルドでは PSCompat を実行していない）。
