# SnowStack.EncodingProbe.PowerShell 1.2.0 — 手動確認後の修正点（ブログ説明書向け）

- 対象バージョン: 1.2.0（未リリース。リリース前の最終修正）
- 作成日: 2026-09-29
- 位置づけ: ブログの解説記事（[SnowStack.EncodingProbe.PowerShell 解説](https://snow-stack.net/encodingprobe_powershell_guide/)）に
  1.2.0 の内容を書き足すための素材。利用者から見て変わった点だけを、使い方の例とともにまとめる
- 詳しい根拠: `docs/EncodingProbe-1.2.0-実装記録.md` 4 章、`CHANGELOG.md` の「手動確認後の修正（2026-09-29）」

---

## 1. `PSEncodingName` に `I do not know.` と表示されなくなりました

PowerShell 7.x で、windows-1252 や iso-8859-1 のように PowerShell のフレンドリ名が無い文字エンコーディングを判定すると、
`PSEncodingName` に `I do not know.` という文字列が入っていました。1.0.0 から存在した不具合です。

```text
PS> Resolve-Encoding .\italian.txt

CodePage        : 1252
EncodingWebName : windows-1252
PSEncodingName  : I do not know.     ← 1.1.0 まで
UsePSName       : False
```

1.2.0 では、この場合の `PSEncodingName` は **空（null）** になります。`UsePSName` は従来どおり `False` です。

- PowerShell 5.1 では、もともと null でした（変化なし）
- Shift_JIS や EUC-JP など、本ライブラリが独自に判定する文字エンコーディングの値（PowerShell 7.x の `shift_jis`、`euc-jp` など）は変わりません
- `UsePSName` が `False` のときは、`PSEncodingName` ではなく `CodePage` を使って文字エンコーディングを指定してください

```powershell
$info = Resolve-Encoding .\italian.txt
Get-ProbedContent .\italian.txt -Encoding $info.CodePage
```

---

## 2. `-WhatIf` で「実行すれば失敗すること」が分かるようになりました

`-WhatIf` は「実行したら何が起きるか」を確かめるためのものです。
これまでは、読み取り専用のファイルなど、実行すれば失敗するファイルでも `-WhatIf` では「書き込みます」という表示だけが出ていました。

1.2.0 では、**ファイルを変更せずに確かめられる失敗は、`-WhatIf` のときにも報告します。**
報告されるエラーは、`-WhatIf` を付けずに実行したときと同じです。

```text
PS> Set-ProbedContent .\readonly.txt -Value 'X' -Encoding utf8NoBOM -WhatIf
Set-ProbedContent : 'C:\work\readonly.txt' は読み取り専用です。書き込むには -Force を指定してください。
読み取り専用の属性は書き込み後に元に戻します。
```

| コマンド | `-WhatIf` でも報告するようになったもの |
|---|---|
| `Set-ProbedContent` / `Add-ProbedContent` | 読み取り専用のファイル（`-Force` なし）、書き込み先のフォルダーが無い |
| `Out-ProbedFile` | `-NoClobber` で出力先が既にある、読み取り専用のファイル、`-EncodingFrom` の参照先の問題 |
| `Convert-ProbedContent` | `-Destination` で同じ名前のファイルが 2 つ以上ある（2 件目以降） |

`Convert-ProbedContent` は、読み取り専用・出力先に同名のファイルがある・出力先が変換元と同じ・不正なバイト列・表現できない文字を、
もともと `-WhatIf` でも報告していました。

**補足（仕様どおりの動作）:** `Convert-ProbedContent` の `-WhatIf` / `-Confirm` のメッセージは、変換しても内容が変わらず
実際には書き直さないファイルにも表示されます。`-Confirm` を指定したときに確認が省かれないようにするための動作です。

---

## 3. `Convert-ProbedContent` で変換したファイルを確実に元に戻せるようになりました

`-PassThru` の結果に、変換元のコードページ番号 `SourceCodePage` を追加しました。

```text
PS> Convert-ProbedContent .\legacy.txt -Encoding utf8NoBOM -PassThru

Path            : C:\work\legacy.txt
Destination     : C:\work\legacy.txt
SourceEncoding  : euc-jp
SourceCodePage  : 20932      ← 追加
Encoding        : utf8NoBOM
SourceLineBreak : Lf
LineBreak       : Lf
Changed         : True
```

元に戻すときは、次のように使い分けてください。

| 変換元 | `-Encoding` に渡すもの |
|---|---|
| Unicode 系（UTF-8、UTF-16 など） | `SourceEncoding`（`utf8BOM` のように BOM の有無まで表す名前） |
| それ以外（Shift_JIS、EUC-JP、Big5、windows-1252 など） | `SourceCodePage` |

```powershell
$r = Convert-ProbedContent .\legacy.txt -Encoding utf8NoBOM -PassThru
Convert-ProbedContent .\legacy.txt -Encoding $r.SourceCodePage    # 元の EUC-JP に戻る
```

**なぜ名前ではだめなのか:** EUC-JP には .NET 上で 20932 と 51932 の 2 つのコードページがあり、
`euc-jp` という名前を渡すと 51932 として扱われます。判定結果が 20932 のファイルは、名前で戻すと一部の文字が元のバイト列に戻りません。
番号（`SourceCodePage`）なら確実に元どおりになります。

あわせて、PowerShell 5.1 では `SourceEncoding` が `EUC-JP`（大文字）、7.x では `euc-jp` になっていた違いをなくし、どちらも小文字にしました。

---

## 4. 欧米のシングルバイト文字エンコーディングの判定の限界

windows-1252（西欧語）や ISO-8859 系のようなシングルバイトの文字エンコーディングは、バイト列の形からは区別できません。
同じバイトが、どのコードページとしても「正しい文字」として読めてしまうためです（例: `0xE9` は windows-1252 では `é`、windows-1251 では `й`）。
そのため、これらは文章の統計（よく出てくる文字の並び）で推定しています。

**判定できるかどうかは、ファイルの長さよりも内容で決まります。**
文字の一覧、記号や数字の多い行、その言語でふだん使わない文字が多いテキストは、数百バイトあっても判定できない（`CodePage` が `-1`）ことがあります。

実際に、イタリア語の windows-1252 のファイル（約 200 バイト）で次の結果になりました。

| 内容 | 判定結果 |
|---|---|
| アクセント付き文字の一覧の行と、記号・数字の行を含む（約 200 バイト） | 判定できない（-1） |
| その 2 行を除いた普通の文章（約 110 バイト） | iso-8859-1（正しい） |
| さらに `Prezzo: 1.234,56 € — .` の行を足したもの | windows-1252（正しい） |

判定できなかった場合は、コードページを明示して読み書きしてください。

```powershell
Get-ProbedContent .\italian.txt -Encoding 1252
Convert-ProbedContent .\italian.txt -SourceEncoding 1252 -Encoding utf8NoBOM
```

判定できないときに、あてずっぽうで特定の文字エンコーディングを返すことはしません。
外れたときに、エラーにならないまま文字化けしたファイルができてしまうためです。
（実行環境の言語から推定する方法は、1.3.0 以降で検討しています）

この内容は、各コマンドのヘルプ（`Get-Help <コマンド名> -Full` の NOTES）にも 5 言語で記載しています。

---

## 記事に書くときのまとめ（箇条書き）

- PowerShell 7.x の `PSEncodingName` に `I do not know.` が入る不具合を修正（null に）
- `-WhatIf` で、読み取り専用などの「実行すれば失敗すること」が分かるようになった
- `Convert-ProbedContent -PassThru` に `SourceCodePage` を追加。Unicode 系以外は番号で戻すのが確実
- 欧米のシングルバイト文字エンコーディングは内容しだいで判定できないことがある。そのときは `-Encoding` / `-SourceEncoding` で指定する
