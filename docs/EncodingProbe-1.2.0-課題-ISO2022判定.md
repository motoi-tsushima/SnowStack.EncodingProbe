# 1.2.0 の課題 — ISO-2022 系の判定

- 起票日: 2026-08-23
- 対象: `SnowStack.EncodingProbe`（クラスライブラリ側の `EncodingDetector`）
- 状態: 未着手。**1.1.0 では対応しない**
- 再確認: 2026-08-31。UTF.Unknown を net10.0 で 2.7.0 に上げたあと、
  下の「再現手順」を 3 件とも実行し、**判定結果に変化がない**ことを確認した
  （課題 1 → `50227`、課題 2 → `50229`、課題 3 → `20127`）。
  3 件はいずれも独自判定側（`EncodingDetector`）の課題であり、
  UTF.Unknown のバージョンでは変わらない

1.1.0 の作業中に見つかった、コアの判定処理の問題を記録する。
1.1.0 は「追加のみ・コアは変更しない」というリリースであり、
判定エンジン（約 2200 行、判定順序に依存する）に手を入れると全言語に回帰リスクが及ぶため、
1.2.0 以降で扱う。

なお、**ISO-2022-JP のコードページ 50220 は正しい**。
`Encoding.GetEncoding("iso-2022-jp")` が返すのは 50220 であり、WebName も一致する。
半角カタカナ（`ESC ( I`）を含むファイルも 50220 のデコーダで正しく復号できることを確認済み。
50221（`csiso2022jp`）や 50222 に変更する必要はない。

---

## 課題 1: ISO-2022-TW が ISO-2022-CN と誤判定される

### 現象

ISO-2022-TW のバイト列が、コードページ 50227（`x-cp50227` = ISO-2022-CN）と判定される。

```
入力: 1B 24 29 47 1B 4E 21 21 1B 28 42 41   (ESC $ ) G …)
判定: cp50227 / x-cp50227     ← 期待は cp50229 / iso-2022-tw
```

### 原因

`EncodingDetector.cs` の `ISO2022_Detection` で、次の 2 つのエスケープシーケンスが
`cnESC` と `twESC` の**両方**の表に入っている。

```
ESC $ ) G   CNS 11643-1992 Plane 1
ESC $ * H   CNS 11643-1992 Plane 2
```

このため両方のスコアが同点になり、バリアントの選択が

```csharp
if      (maxScore == jpScore) { codePage = CodePageJis; }
else if (maxScore == krScore) { codePage = CodePageIso2022Kr; }
else if (maxScore == cnScore) { codePage = CodePageIso2022Cn; }   // ← 同点だとここで確定
else if (maxScore == twScore) { codePage = CodePageIso2022Tw; }
```

の順で評価されるため、常に CN が勝つ。
TW と判定されるのは、TW にしか現れない `ESC $ + I` 〜 `ESC $ + M`（Plane 3〜7）を含む場合だけ。

### 検討すべきこと

CNS 11643 は本来 TW の文字集合であり、ISO-2022-CN が Plane 1/2 を使うのは
GB2312 との併用時である。単純に順序を入れ替えるのではなく、
`ESC $ ) A`（GB2312）の有無で CN と TW を切り分けるのが妥当と思われる。

---

## 課題 2: 判定できても .NET が扱えないコードページがある

### 現象

`Encoding.GetEncoding(50229)` は net10.0 / net48 のどちらでも `NotSupportedException` を投げる。
つまり ISO-2022-TW は、**正しく 50229 と判定できたとしても .NET では復号できない**。

### 1.1.0 側の対応（対応済み）

1.1.0 のコマンドは、この状況を対象ファイルごとの非終了エラーとして報告する。
エラーID は `CodePageNotAvailable`、メッセージは 5 言語対応で、
`-Encoding` による明示指定を案内する。
生の `NotSupportedException` が終了エラーとして素通りする問題は修正済み
（`tests/.../CmdletTests/UnavailableCodePageTests.cs` と PSCompat で回帰を固定している）。

### 検討すべきこと

課題 1 を直して TW を正しく 50229 と判定できるようにすると、
**現在 50227 として（誤って、しかし .NET が扱えるコードページで）返っていたものが、
扱えないコードページになる**。判定の正しさと実用性が逆方向に働くため、
課題 1 の修正だけでは完結しない。次のいずれかを決める必要がある。

1. 50229 を返さず、判定結果としては「判定不能」とする
2. 50229 を返し、扱えないことは呼び出し側に委ねる（1.1.0 の現状の扱い）
3. ライブラリ側で CNS 11643 のデコーダを持つ（大きい）

---

## 課題 3: SO/SI 形式の 1 バイトカナ（cp50222）を検出できない

### 現象

`0x0E`（SO）/ `0x0F`（SI）で JIS X 0201 カタカナに切り替える形式
（`Encoding.GetEncoding(50222)` が出力する形式）は、
エスケープシーケンスを含まないため ISO-2022 として検出されず、us-ascii（20127）と判定される。
復号すると SO/SI が制御文字 U+000E / U+000F としてそのまま現れる。

```
入力: 0E 31 32 33 0F 0D 0A      (SO "123" SI CRLF、本来は ｱｲｳ)
判定: cp20127 / us-ascii
復号: U+000E U+0031 U+0032 U+0033 U+000F   ← 期待は U+FF71 U+FF72 U+FF73
```

### 検討すべきこと

`ISO2022_Detection` は `hasShiftFunction`（SO/SI の存在）を見ているが、
エスケープシーケンスが 1 つも無い場合は `escExist` が false のままとなり
ISO-2022 と判定されない。

ただしこの形式は実際にはほとんど使われておらず、
SO/SI だけを根拠に ISO-2022 と判定すると、バイナリや他の形式を誤判定する危険がある。
優先度は低い。

---

## 再現手順

いずれも `tests/PSCompat/ProbedCompatScenarios.ps1` の
「実行環境が提供していないコードページ」の節と同じ要領で再現できる。

以下は 2026-08-31 に UTF.Unknown 2.7.0 で実行し、コメントの結果になることを確認済み。

```powershell
Import-Module .\publish\SnowStack.EncodingProbe.PowerShell\SnowStack.EncodingProbe.PowerShell.psd1

# 課題 1
$tw = [byte[]](0x1B,0x24,0x29,0x47, 0x1B,0x4E,0x21,0x21, 0x1B,0x28,0x42, 0x41)
[IO.File]::WriteAllBytes('tw.txt', $tw)
Resolve-Encoding .\tw.txt | Format-List CodePage, EncodingWebName   # cp50227 になる

# 課題 2
$tw3 = [byte[]](0x1B,0x24,0x2B,0x49, 0x1B,0x4E,0x21,0x21, 0x1B,0x28,0x42, 0x41)
[IO.File]::WriteAllBytes('tw3.txt', $tw3)
Resolve-Encoding .\tw3.txt | Format-List CodePage, EncodingWebName  # cp50229
Get-ProbedContent .\tw3.txt                                          # CodePageNotAvailable

# 課題 3
$sosi = [System.Text.Encoding]::GetEncoding(50222).GetBytes([char]0xFF71)
[IO.File]::WriteAllBytes('sosi.txt', $sosi)
Resolve-Encoding .\sosi.txt | Format-List CodePage, EncodingWebName # cp20127 になる
```
