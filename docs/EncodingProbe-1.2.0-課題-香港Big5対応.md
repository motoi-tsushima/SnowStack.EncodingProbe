# 1.2.0 の課題 — 香港（繁体字広東語）Big5 系の判定

- 起票日: 2026-09-11
- 対象: `SnowStack.EncodingProbe`（コアの `EncodingDetector` / `EncodingProbe`）＋ PowerShell 層の一部
- 状態: **未着手。方針検討中**（この文書は着手前の構造解析と提案）
- 調査環境: Windows 11 / PowerShell 7（net10.0 ビルド）と Windows PowerShell 5.1（net48 ビルド）
- 調査日: 2026-09-11。本文中の「実測」はすべてこの日に手元のビルド出力で採取した値

現在の判定対象は英語・日本語・韓国語・繁体字中国語（台湾）・簡体字中国語（大陸）の 5 圏である。
ここに香港（繁体字広東語、Big5-HKSCS）を追加するにあたり、
何を変える必要があるか、どこまでが原理的に可能かを整理する。

---

## 結論（先に 3 点）

1. **台湾 Big5 と香港 Big5 はバイト構造では区別できない。**
   HKSCS は Big5 の上位互換で、許容バイト範囲が完全な包含関係にある。
   「規格外が出たら違う」という本ライブラリの方式は*除外*しかできないため、
   包含される側（Big5）を排除する材料が原理的に存在しない。
   区別できるのは一方向だけ ——「CP950 が未定義とする領域を使っている」＝素の Big5 を超えている、という検出。

2. **.NET には HKSCS のデコーダーが無い。**
   `Encoding.GetEncoding(951)` は PS 5.1 / 7.x とも失敗し、
   `Encoding.GetEncoding("big5-hkscs")` は CP950（950）を返す。
   HKSCS 固有の字は例外を出さずに私用領域（PUA）へ化ける。判定できても正しくは読めない。

3. **着手すべきなのは香港そのものより、その前段にあるカルチャー分岐の欠陥。**
   `zh-Hant-HK` は現状「判定不能」を返し、香港カルチャーで簡体字 GBK 文書が `950 / big5` と誤判定される。
   どちらも香港対応の土台に当たるため、先に直さないと HKSCS を載せられない。

---

## 1. 現状の構造（香港に関係する経路）

`EncodingProbe.Detect` → `EncodingDetector.Detection(culture)` → UTF.Unknown の 3 層のうち、
繁体字に到達する経路は次のとおり。番号は `Detection()` 内の実行順であり、順序そのものが仕様である。

| 順 | 処理 | 香港対応での位置づけ |
|---|---|---|
| 1–4 | 改行 → BOM → ISO-2022/ASCII → UTF-32/16/8 | カルチャー非依存。香港でも UTF-8 / UTF-16 はここで確定する。手を入れない |
| 5 | `GetEastAsianLegacyRegion()` のカルチャーゲート | **改修の中心。** 現在 `zh-TW` / `zh-Hant` / `zh-HK` / `zh-MO` を同じ `ChineseTraditional` に畳んでいる |
| 6a | `EUCxx_Detection()` → 繁体字なら EUC-TW | EUC-TW は CNS 11643（台湾）。香港では使われないので候補から外せる |
| 6b | EUC-TW と CP950 の両方成立 → CP950 を優先 | 香港でも同じ扱いでよい |
| 6c | `CPxxx_Detection()` → `CP950_Detection()` | **改修点。** いまは「Big5 の器として妥当か」だけを見て 950 を返す |
| 7 | `EncodingProbe.NormalDetectEncoding` のクロスチェック | 独自判定が旧マルチバイトを返し、UTF.Unknown が**シングルバイト**を返したときだけ差し替える |

重要なのは次の 2 点である。香港対応はこの 2 か所の性格を変える作業になる。

- **6c が返す値は常に 950 固定**である
- **7 のクロスチェックは「UTF.Unknown がシングルバイトのとき」しか働かない**

---

## 2. 実測 — いまのビルドが何を返すか

入力は次の 3 種類。

- Big5 繁体字文（118 バイト、共通漢字のみ）
- それに HKSCS 固有領域の 4 文字（`88 62` / `8B F8` / `FA 5F` / `FE 52`）を足したもの
- cp936 の簡体字文（118 バイト）

| 入力 | カルチャー | Combined（既定） | NativeOnly | UtfUnknownOnly |
|---|---|---|---|---|
| Big5 繁体字 | zh-TW | 950 / big5 | 950 / big5 | 950 / big5（0.99） |
| Big5 繁体字 | zh-HK | 950 / big5 | 950 / big5 | 950 / big5（0.99） |
| Big5 繁体字 | zh-CN | **54936 / gb18030** | 54936 / gb18030 | 950 / big5（0.99） |
| Big5 + HKSCS 拡張 | zh-TW | 950 / big5 | 950 / big5 | **判定不能（0.33）** |
| Big5 + HKSCS 拡張 | zh-HK | 950 / big5 | 950 / big5 | **判定不能（0.33）** |
| GBK 簡体字 | zh-TW | **950 / big5** | 950 / big5 | 54936 / gb18030（0.99） |
| GBK 簡体字 | zh-HK | **950 / big5** | 950 / big5 | 54936 / gb18030（0.99） |
| GBK 簡体字 | zh-CN | 936 / gbk | 936 / gbk | 54936 / gb18030（0.99） |
| Big5 + HKSCS 拡張 | zh-Hant-HK | **-1（判定不能）** | -1 | 判定不能 |

読み取れることが 4 つある。

- **香港カルチャーは現状ただの台湾扱い。** `zh-HK` と `zh-TW` の結果が完全に一致する。
  HKSCS 拡張が入っていても `950 / big5` としか言わない。
- **簡体字混在で壊れる。** 香港カルチャーで GBK 文書を読むと `950 / big5` になる。
  Big5 の器として構造が成立してしまい、クロスチェックは「UTF.Unknown がシングルバイトのときだけ」なので発動しない。
- **ただし UTF.Unknown は繁簡を 0.99 で見分けている。** 統計モデルを持っているため、
  実用サイズの文書なら Big5 と GB 系を高い信頼度で区別できる。混在対策の材料はすでに手元にある。
- **HKSCS が混ざると UTF.Unknown が黙る。** 信頼度 0.33 まで落ちて判定不能になる。
  HKSCS 入りのファイルは独自判定だけが頼りで、フォールバックが効かない。

### 2.1 `zh-Hant-HK` が判定不能になる理由

`Detection(culture)` は受け取った文字列をそのまま `_cultureName` に入れ、
`GetEastAsianLegacyRegion()` が `Equals("zh-HK")` のような**完全一致**で表を引いている。
カルチャー名の正規化を一切していない。

.NET 10 は ICU 由来の `zh-Hant-HK` をその名前のまま保持するため、PowerShell 7 側でこの形が渡る経路が現実にある
（`ValidateCulture` は妥当性を見るだけで正規化しない）。
`zh-Hant-TW` / `zh-Hans-CN` も同様に落ちるので、香港固有の問題ではなく**カルチャーゲート全体の欠陥**である。

---

## 3. 判定の原理的な限界 — Big5 と HKSCS は包含関係にある

「区別がつきにくい」は、正確には**「片方向にしか区別できない」**である。
先頭バイト 0x81–0xFE を CP950 の定義状況で切ると次のようになる。

| 領域 | CP950 での扱い | HKSCS での扱い | 判定材料としての価値 |
|---|---|---|---|
| `81–86 xx` | ユーザー定義（PUA） | 未使用 | 造字・UAO の可能性。HKSCS の根拠にはならない |
| `87–A0 xx` | ユーザー定義（PUA） | **HKSCS 本体** | 最も強い材料。素の Big5 には存在しない |
| `A1–F9 xx` | 記号・常用・次常用漢字 | 同一（互換） | 価値なし。台湾・香港どちらの文書でもここだけで書ける |
| `C6A1–C8FE` | 一部 PUA / 倚天拡張 | HKSCS 追加字 | 弱い。倚天（ETen）拡張と衝突する |
| `F9D6–F9FE` | 倚天拡張として定義済み | 同一 | 価値なし |
| `FA–FE xx` | ユーザー定義（PUA） | **HKSCS 本体** | 強い材料。ただし Big5-UAO とも衝突する |

後続バイトは Big5 / HKSCS とも `0x40–0x7E` と `0xA1–0xFE` で完全に同一である。したがって：

- **HKSCS 固有領域が出た** → 素の Big5 の範囲を超えている（何らかの拡張が使われている）と言い切れる
- **出なかった** → 何も言えない。台湾の文書か香港の文書かはバイト列に情報が無い。
  ただしこの場合、**どちらで復号しても同じ文字列**になるので実害が無い。
  判定を間違えても壊れない、という性質は押さえておきたい
- **固有領域が出ても HKSCS と断定はできない。** 同じ領域を Big5-UAO（台湾 PTT 系）・倚天拡張・各社の造字が使う。
  「領域の出現」＝「拡張あり」までしか言えず、それが HKSCS かどうかを決めるのは**カルチャー**である。
  これは EUC-JP と Shift-JIS をカルチャーと改行で決めている既存の設計思想と一致する

つまり `CP950_Detection()` を作り替える必要はない。
必要なのは「規格外か」に加えて「**どの領域を使ったか**」を持ち帰る経路だけで、既存の bool を壊さずに `out` で足せる。

### 3.1 ついでに直すべき点 — Big5 の後続バイトが緩い

現行の `CP950_Detection()` は後続バイトに `(b >= 0x80 && b <= 0xFE)` を許しているが、
Big5 / HKSCS とも後続バイトに `0x80–0xA0` は存在しない。
`0xA1–0xFE` に締めると Big5 の精度が上がり、欧文シングルバイト
（`0xA0` = NBSP が頻出する windows-1252 など）の誤吸収も減る。

---

## 4. .NET 側の制約 — 判定できても読めない

| 試したこと | PS 5.1 (net48) | PS 7 (net10.0 + CodePagesEncodingProvider) |
|---|---|---|
| `GetEncoding(951)` | 例外 | 例外 |
| `GetEncoding("big5-hkscs")` | 成功 → **CodePage 950** | 成功 → **CodePage 950** |
| `GetEncoding(950)` で `88 62` を復号 | U+F325（私用領域） | U+F325（私用領域） |
| 同 `FA 5F` | U+E01F（私用領域） | U+E01F（私用領域） |
| 同 `C6 A1` | U+F6B1（私用領域） | U+F6B1（私用領域） |
| 同 `A4 40`（共通漢字） | U+4E00「一」 | U+4E00「一」 |

ここから導かれる設計上の制約が 3 つある。

- **コードページ 951 を返してはいけない。**
  `EncodingInformation.CodePage` は最終的に `Internal/EncodingVocabulary.FromEncodingInformation`
  → `Encoding.GetEncoding(codePage)` に渡る。951 を返した瞬間、いま読めている香港のファイルが
  `Get-ProbedContent -Encoding Auto` でエラー（`DetectedCodePageNotAvailable`）になる。明確な機能後退である
- **`big5-hkscs` という名前は .NET 上では 950 の別名にすぎない。**
  名前として返す分には往復が壊れない（`GetEncoding("big5-hkscs")` が通る）利点がある一方、
  「HKSCS として読める」と誤解させる名前でもある
- **HKSCS 固有字は例外も出さずに PUA へ落ちる。**
  つまり現状、香港のファイルを読ませると*静かに*壊れた文字列が返る。
  これを本当に直すには HKSCS ↔ Unicode のマッピング表（約 5,000 字規模）を同梱して
  `Encoding` 派生クラスを自作するしかない。表の出所と再配布条件の確認が要る、独立した規模の作業になる

---

## 5. 改修方針（3 段階）

### 段階 1（推奨・今回）カルチャーの分離と誤判定の修正

返す値は `950 / big5` のまま変えない。公開 API の変更なし。

**(a) カルチャーゲートの作り直し** — `GetEastAsianLegacyRegion()`（表はここ 1 か所のまま維持）

```
None / Japanese / Korean / ChineseSimplified
ChineseTraditional   ← 台湾  : zh-TW, zh-Hant, zh-Hant-TW, zh-CHT
ChineseHongKong      ← 香港  : zh-HK, zh-MO, zh-Hant-HK, zh-Hant-MO,
                               yue, yue-HK, yue-Hant-HK
```

完全一致での引き当てをやめ、*言語サブタグ＋地域サブタグ*を取り出して判定する形に変える
（`zh-Hant-HK` が落ちる現行バグの修正を兼ねる）。
`yue`（広東語）は .NET 上では既定 ANSI が 1252 になるが、
文書の符号化としては香港 Big5 圏なのでここに含めるべきである。

**(b) 香港では EUC-TW を候補から外す** — `GetEucCodePageFromCulture()`

EUC-TW は CNS 11643、台湾専用。香港で使われる現実がないうえ、
候補に残すと偶然 EUC-TW 構造に合致するバイト列を拾って精度を落とす。`ChineseHongKong` では -1 を返す。

**(c) 繁簡混在の解決** — `EncodingProbe.ShouldPreferUtfUnknown()`

いまは「独自判定＝旧マルチバイト かつ UTF.Unknown＝シングルバイト」のときだけ差し替えている。
これを「**両者が東アジア旧マルチバイトで、かつ系統（Big5 系 / GB 系）が食い違い、
UTF.Unknown の信頼度が十分高い**」場合まで広げ、UTF.Unknown の統計判定を採る。
実測どおり 0.99 で繁簡を見分けるので、香港カルチャーで GBK 文書を `950` と言う現象が消える。

ただし**この拡張は日本語・韓国語にも波及する**。
EUC-JP は独自判定が 20932、UTF.Unknown が 51932 を返すという既知の差があり、
素朴に「食い違ったら UTF.Unknown」にすると日本語の結果が変わってしまう。
同一系統の別番号は系統表で除外して守ること。`NativeOnly` は従来どおり突き合わせない。

**(d) Big5 後続バイトの厳密化** — `CP950_Detection()`

後続バイトを `0x40–0x7E` と `0xA1–0xFE` に締める（3.1 節）。

### 段階 2（推奨・今回）HKSCS 拡張領域の検出と表示

`CP950_Detection()` に `out Big5Extension` を足し、
`None` / `HkscsArea`（0x87–0xA0・0xFA–0xFE を使用）/ `AmbiguousArea`（0xC6–0xC8 のみ）を返す。
`CPxxx_Detection()` はカルチャーと突き合わせて次のように決める。

| カルチャー | 拡張領域なし | HKSCS 領域あり |
|---|---|---|
| `ChineseHongKong` | 950 / big5 | 950 / **big5-hkscs** |
| `ChineseTraditional`（台湾） | 950 / big5 | 950 / big5（UAO・倚天・造字の可能性があるため名前は変えない） |

`CodePage` は 950 のままとする（4 節の制約）。

注意点が 1 つある。net10.0 ビルドの `PSEncodingName(codePage, bom)` は
**コードページからしか名前を作らない**ため、このままだと
`EncodingWebName = "big5-hkscs"` なのに `PSEncodingName = "big5"` という不整合が出る。
internal メソッドなので、決まった WebName を渡す形に直すのが素直
（`UsePSName` は両 TFM とも false のままで変化なし）。

### 段階 3（別課題として起票）HKSCS の復号・符号化

マッピング表を同梱し `Encoding` 派生クラスを実装する。
ここまでやって初めて `Get-ProbedContent` が香港の拡張字を正しく読める。
表のライセンス確認、統一語彙（`Internal/EncodingVocabulary`）への追加、
両 TFM・両ホストでの一致検証まで含む。コアの公開 API が増える。

---

## 6. 問題点と落としどころ

### 6.1 【原理的】台湾 Big5 と香港 Big5 は判別できない

共通漢字だけで書かれた文書に、産地を示す情報は入っていない。
頻度統計を持ち込めば「繁体字の語彙の傾向」で推測はできるが、
本ライブラリは統計モデルを持たない方針で作られており、持ち込むべきでもない。

**落としどころ：区別しない。** どちらで復号しても同じ文字列になるので実害が無い、と仕様に明記する。
区別する必要があるのは「拡張字が入っているか」だけであり、それは検出できる。

### 6.2 【要判断】拡張領域の出現＝HKSCS とは限らない

同じ領域を Big5-UAO・倚天拡張・各社の造字が使う。バイトだけで流派は決まらない。

**落としどころ：カルチャーで決める。** 香港カルチャーのときだけ HKSCS と名乗り、
台湾カルチャーでは `big5` のままにする。EUC-JP と Shift-JIS をカルチャーで決めている既存方針と同じ形になる。

### 6.3 【制約】判定できても正しく読めない

段階 2 まででは「HKSCS が混じっている」と名前で伝えるだけで、`Get-ProbedContent` の出力は改善しない。

**落としどころ：段階 3 として別課題に切る。**
段階 2 のリリース文には「判定のみ・復号は CP950 の範囲」と明記しないと、名前だけ見て誤解される。

### 6.4 【副作用】クロスチェック拡張は全東アジアに波及する

繁簡混在を UTF.Unknown に委ねる改修は、日本語・韓国語カルチャーの判定経路にも入る。

**落としどころ：系統表で守る。** 「Big5 系 ⇄ GB 系」の食い違いだけを対象にし、
`WorldLanguageTests` の全カルチャー×全言語の直積テストで回帰を押さえる。

### 6.5 【副作用】HKSCS 入りファイルはフォールバックが効かない

UTF.Unknown は HKSCS 拡張が入ると信頼度 0.33 まで落ちて判定不能になる（実測）。
クロスチェックを広げるときに「UTF.Unknown 優先」を強くしすぎると、
いま `950` と言えているファイルが判定不能に転落しうる。

**落としどころ：信頼度の下限（0.8 程度）を条件に入れる。**

### 6.6 【整合】ヘルプとメッセージで香港の扱いが食い違っている

`Internal/MessageCatalog` はすでに `zh-HK` / `zh-MO` を繁体字メッセージに割り当てている。
一方 MAML ヘルプは「香港は後のバージョン」という方針で `zh-HK` フォルダーを置かず英語に落としている。

香港を正式対応にするなら、**ヘルプを 6 言語（zh-HK 追加）にするか、方針どおり英語のまま据え置くか**を決める必要がある。
追加する場合は `MamlHelpTests` の「5 言語の骨格一致」と
「`zh-HK` は en-US にフォールバックする」という期待値の両方を更新することになる。

---

## 7. 触るファイル

| 場所 | 内容 |
|---|---|
| `EncodingDetector.cs` | `EastAsianLegacyRegion` に `ChineseHongKong` を追加。`GetEastAsianLegacyRegion()` をサブタグ解析に作り替え（表は 1 か所のまま維持） |
| `EncodingDetector.cs` | `GetEucCodePageFromCulture()`：香港は EUC-TW を候補にしない |
| `EncodingDetector.cs` | `CP950_Detection()`：後続バイトの厳密化＋使用領域の持ち帰り。`CPxxx_Detection()` で香港分岐 |
| `EncodingDetector.cs` | `EncodingName()` / `PSEncodingName()`：`big5-hkscs` を返す経路と両者の整合 |
| `EncodingProbe.cs` | `ShouldPreferUtfUnknown()` に繁簡食い違いの条件を追加。系統表と信頼度下限 |
| `tests/EncodingProbe.Tests/TestData/Chinese_Traditional_HongKong/` | HKSCS 拡張入りのサンプルを**バイト列を明示して**新規作成（.NET のエンコーダーでは作れない）。`tools/New-EncodingTestData.ps1` に生成処理を足す |
| `DetectorTests/FutureLanguageTests.cs` | 香港のテストクラスを追加（拡張あり／なしの両方） |
| `DetectorTests/WorldLanguageTests.cs` | `CultureNames` に `zh-HK` を追加。全言語×全カルチャーの直積で回帰を検出する |
| `tests/PSCompat/ProbedCompatScenarios.ps1` | 香港シナリオを追加し、PS 5.1 / 7.x で同一結果になることを確認（`.ps1` は UTF-8 BOM 付きで保存） |
| MAML ヘルプ 5 言語 + `MamlHelpTests.cs` | `zh-HK` を追加するか据え置くかを決定。追加するならテストの期待値も更新 |
| `docs/` / `CHANGELOG.md` | 1.2.0 の節に追記。段階 3（HKSCS 復号）を新しい課題文書として起票 |

---

## 8. 決めてほしいこと

1. **段階 2 をやるか。** `EncodingWebName` に `big5-hkscs` を出す＝戻り値の文字列が変わる。
   「読めないのに名前だけ HKSCS」を嫌うなら段階 1 で止め、`950 / big5` のままにする選択もある
2. **クロスチェック拡張の適用範囲。** 繁簡（Big5 系 ⇄ GB 系）に限定するか、東アジア全体に広げるか。
   前者を推す。後者は日本語の EUC-JP 判定に影響が出る
3. **MAML ヘルプの香港対応。** 6 言語目にするか、メッセージだけ繁体字・ヘルプは英語のまま据え置くか

---

## 9. 再現手順

2 節・4 節の実測は次の手順で再現できる。ビルド済みの出力を直接読み込む
（`SnowStack.EncodingProbe.dll` より先に UTF.Unknown を読み込むこと）。

```powershell
# PS 7（net10.0 ビルド）の場合
Add-Type -Path '.\tests\EncodingProbe.Tests\bin\Debug\net10.0\UtfUnknown.dll'
Add-Type -Path '.\SnowStack.EncodingProbe\bin\Debug\net10.0\SnowStack.EncodingProbe.dll'
[System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)

$hant = "香港是一個國際大都會，粵語是香港人的主要語言。今天天氣很好，我們一起去飲茶吧。中文資訊處理需要正確的文字編碼判斷方法。"
$hans = "香港是一个国际大都会，粤语是香港人的主要语言。今天天气很好，我们一起去饮茶吧。中文信息处理需要正确的文字编码判断方法。"
$big5  = [System.Text.Encoding]::GetEncoding(950).GetBytes($hant)
$gbk   = [System.Text.Encoding]::GetEncoding(936).GetBytes($hans)
$hkscs = $big5 + [byte[]](0x88,0x62,0x8B,0xF8,0xFA,0x5F,0xFE,0x52)   # HKSCS 固有領域

foreach ($c in 'zh-TW','zh-HK','zh-Hant-HK','zh-CN') {
    $o = New-Object SnowStack.EncodingProbe.EncodingDetectorOptions
    $o.Culture  = $c
    $o.Strategy = [SnowStack.EncodingProbe.DetectionStrategy]::Combined
    foreach ($pair in @(@{N='big5';B=$big5}, @{N='hkscs';B=$hkscs}, @{N='gbk';B=$gbk})) {
        $r = [SnowStack.EncodingProbe.EncodingProbe]::Detect([byte[]]$pair.B, $o)
        '{0,-10} {1,-12} -> {2} / {3}' -f $pair.N, $c, $r.CodePage, $r.EncodingWebName
    }
}

# UTF.Unknown 単独の信頼度を見る
([UtfUnknown.CharsetDetector]::DetectFromBytes([byte[]]$gbk)).Details |
    Select-Object -First 3 EncodingName, Confidence

# .NET が HKSCS を扱えないことの確認
try { [System.Text.Encoding]::GetEncoding(951) } catch { 'cp951: ' + $_.Exception.GetType().Name }
[System.Text.Encoding]::GetEncoding('big5-hkscs').CodePage        # 950 が返る
'{0:X4}' -f [int][char]([System.Text.Encoding]::GetEncoding(950).GetString([byte[]](0x88,0x62)))  # F325（PUA）
```

`[byte[]]` のキャストは省略しないこと。省略すると PowerShell が
`Detect(string filePath)` のオーバーロードを選び、バイト列がファイルパスとして扱われる。

`powershell.exe`（PS 5.1）で確認する場合は、パスを `net48` のビルド出力に置き換える。
`SnowStack.EncodingProbe\bin\Debug\net48\` には UTF.Unknown とその依存 DLL が並んでいる。

---

## 参照

- `docs/EncodingProbe-1.2.0-課題_人間記述用.md` … 課題 1（東アジア以外の言語への対応、対応済み）。
  カルチャーゲートとクロスチェックを導入した経緯はここにある
- `docs/EncodingProbe-1.2.0-課題-ISO2022判定.md` … 課題 2 が本文書 4 節と同じ構図
  （判定できても .NET が扱えないコードページをどう返すか）
- `docs/TestReport_PS7.md` / `docs/TestReport_PS51.md` … 1.1.0 時点の測定値。
  83 番の `traditional_chinese_hk_big5hkscs.txt` が香港の HKSCS ファイルで、`950 / big5` と判定されている
