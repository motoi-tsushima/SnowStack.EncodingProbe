# EncodingProbe 1.2.0 — 第二次修正：香港 Big5 対応 段階 1

- 作成日: 2026-09-22
- 状態: 確定（MAML ヘルプとメッセージは第三次修正へ移管。5 章）
- 前提: 第一次修正（クロスチェックの信頼度下限）が完了していること
- 参照: `EncodingProbe-1.2.0-課題-香港Big5対応.md`（以下「報告書」）、`私用領域の扱い_方針草案.md`

---

## 0. 範囲

報告書の段階 1 だけを実装する。返す値は `950 / big5` のまま変えない。公開 API の変更なし。

実装しないもの：

- 段階 2（`EncodingWebName` を `big5-hkscs` にする）：見送り。Windows では香港も `950 / big5` が本来の姿であり、.NET 上は 950 の別名にすぎないため
- `CP950_Detection()` の使用領域の持ち帰り（`out Big5Extension`）：段階 2 の一部なので不要
- 段階 3（HKSCS デコーダー）：既定では持たない。私用領域に写されるバイト列には介入しない（私用領域方針）

---

## 1. (a) カルチャーゲートの作り直し — `GetEastAsianLegacyRegion()`

- private enum `EastAsianLegacyRegion` に `ChineseHongKong` を追加する
- 完全一致での引き当てをやめ、**カルチャー名の文字列をサブタグに分解して**判定する。
  `CultureInfo` の親チェーンは使わない（net48 の NLS と net10.0 の ICU で名前・親が異なるため）
- 大文字小文字を区別しない。区切りの `_` は `-` と同じに扱う
- 表は 1 か所のまま維持する
- サブタグ分解の処理は、`EncodingDetector` の外（internal な静的ヘルパー等）からも呼べる形にする。
  第三次修正で `MessageCatalog` の言語選択にも同じ規則を使うため

### 判定規則（中国語 `zh` と広東語 `yue`）

1. **用字サブタグ**があればそれで繁簡を決める（`Hans` → 簡体字、`Hant` → 繁体字）
2. 用字サブタグが無ければ、**地域サブタグ**から用字を決める（`TW` / `HK` / `MO` → 繁体字、`CN` / `SG` → 簡体字）
3. どちらも無ければ**言語の既定**（`zh` → 簡体字、`yue` → 繁体字・香港）
4. 繁体字の中では、地域が `HK` / `MO` なら `ChineseHongKong`、それ以外は `ChineseTraditional`。
   地域が無い場合は `zh-Hant` → 台湾、`yue-Hant` → 香港
5. 旧形式の `zh-CHT` → `ChineseTraditional`、`zh-CHS` → `ChineseSimplified`

### 期待値の例

| カルチャー | 結果 |
|---|---|
| `zh`, `zh-CN`, `zh-SG`, `zh-Hans`, `zh-Hans-CN`, `zh-CHS` | ChineseSimplified |
| `zh-Hans-HK` | ChineseSimplified（用字優先） |
| `zh-TW`, `zh-Hant`, `zh-Hant-TW`, `zh-CHT` | ChineseTraditional |
| `zh-HK`, `zh-MO`, `zh-Hant-HK`, `zh-Hant-MO` | ChineseHongKong |
| `yue`, `yue-HK`, `yue-Hant`, `yue-Hant-HK` | ChineseHongKong |
| `yue-Hans-CN`, `yue-CN` | ChineseSimplified |
| `zh_HK`, `ZH-hk` | ChineseHongKong |

日本語・韓国語の判定（`ja`、`ko` 系）も同じ分解の仕組みに乗せてよいが、結果は変えないこと。

---

## 2. (b) 香港では EUC-TW を候補から外す — `GetEucCodePageFromCulture()`

`ChineseHongKong` では -1 を返す。

EUC-TW のバイト列は構造上つねに Big5 としても成立し、両方成立時は CP950 を優先する既存規則があるため、
**判定結果は変わらない**（挙動中立の整理）。1.2.0 時点で `ChineseHongKong` と `ChineseTraditional` の挙動差はこれだけであり、
enum の追加は将来の HKSCS オプトインの適用条件、テストの意図の明確化のためである。

---

## 3. (c) 繁簡の系統クロスチェック — `EncodingProbe`

第一次修正で入れた判定の場所に、次の規則を足す。

### 系統表

| 系統 | コードページ |
|---|---|
| Big5 系 | 950 |
| GB 系 | 936, 54936, 20936 |

日本語・韓国語のコードページは含めない。したがって 20932 / 51932 を含め、日本語・韓国語の判定結果は変わらない。

### 差し替えの条件（すべて満たすとき）

1. 検出モードが Combined（NativeOnly は突き合わせない）
2. 独自判定の結果が Big5 系または GB 系
3. UTF.Unknown の結果が、独自判定と**反対の系統**
4. UTF.Unknown の信頼度が **0.8 以上**（系統上書きの下限。第一次修正のシングルバイト上書きの下限とは別の定数）

### 差し替え後に返す値

UTF.Unknown のコードページをそのまま使わない。UTF.Unknown には系統だけを投票させ、
**反対の系統の独自判定をカルチャーに関係なく実行し直し**、成立すればその結果を返す
（GB 系なら既存の GB2312 / GBK / GB18030 の判別規則により 936 等）。成立しなければ元の独自判定の結果を返す。

GB 系・Big5 系の判定関数がカルチャーゲートを通らずに呼べるかを確認し、呼べない場合は最小限の分離を行うこと。

### 閾値の根拠（実測、PS 5.1 / 7.x 同一）

- 12 バイト以上の Big5 / GBK で、UTF.Unknown は正しい系統を常に 0.99 で返した
- 反対の系統を返した例は、6〜512 バイトのどの長さでも無かった
- 短い入力と HKSCS 入りの入力ではシングルバイトを返すため、条件 3 を満たさず差し替えは起きない

### 期待される結果の変化

| 入力 | カルチャー | 現状 | 修正後 |
|---|---|---|---|
| GBK 簡体字 | zh-TW / zh-HK | 950 | 936 |
| Big5 繁体字 | zh-CN | 54936 | 950 |
| Big5+HKSCS | zh-CN | 54936 | 54936（UTF.Unknown が系統を言えないため救済されない。既知の限界として仕様に明記） |
| Big5+HKSCS | zh-TW / zh-HK | 950 | 950 |

---

## 4. (d) Big5 後続バイトの厳密化 — `CP950_Detection()`

後続バイトを `0x40–0x7E` と `0xA1–0xFE` に締める（現状は `0x80–0xFE` も許している）。
Big5 / HKSCS とも後続バイトに `0x80–0xA0` は存在しない。

---

## 5. MAML ヘルプとメッセージ（第三次修正で扱う）

本依頼では `MessageCatalog` と MAML ヘルプに**触れない**。`MamlHelpTests` の期待値も変更しない。

決定事項（参考）：香港用語で訳した zh-HK 版を `MessageCatalog` と MAML ヘルプの両方に用意し、zh-MO には zh-HK と同じ内容を置く。1.2.0 に含めるが、検出ロジックの修正とは別の依頼（第三次修正）として実施する。

---

## 6. テスト

- `FutureLanguageTests.cs`：香港のテストクラスを追加（HKSCS 拡張あり・なしの両方）
- `WorldLanguageTests.cs`：`CultureNames` に `zh-HK` を追加し、全言語×全カルチャーの直積で回帰を検出する
- カルチャーゲートの単体テスト：1 章の期待値の表をすべて検証する
- 3 章の「期待される結果の変化」の各行を検証する。既存テストの期待値（zh-CN の Big5 → 54936 等）が変わる場合は、変更箇所を報告すること
- `tests/PSCompat/ProbedCompatScenarios.ps1`：香港シナリオを追加し、PS 5.1 / 7.x で同一結果になることを確認（`.ps1` は UTF-8 BOM 付き）
- 私用領域方針の付録 A の検証コードを回帰テストとして `tests/` に移す

---

## 7. 記録

- `CHANGELOG.md` の 1.2.0 節に香港対応として追記する
- 仕様書に次を明記する
  - 台湾 Big5 と香港 Big5 はバイト列から区別しない（拡張字が無ければどちらで復号しても同じ）
  - HKSCS 固有字は私用領域として復号される（私用領域方針への参照）
  - 往復を保証するのは `CodePage` であり、`EncodingWebName` ではない
  - zh-CN カルチャーの Big5+HKSCS は救済されない既知の限界
- 段階 3（HKSCS の復号・符号化、オプトイン）を新しい課題文書として起票する
