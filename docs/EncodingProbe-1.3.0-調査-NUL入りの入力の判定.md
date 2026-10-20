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
