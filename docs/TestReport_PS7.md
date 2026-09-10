# Resolve-Encoding 判定テスト レポート（PS7）

- 実行日時: 2026-09-10 11:47:53
- PowerShell: 7.6.6（Core）
- モジュール: SnowStack.EncodingProbe.PowerShell 1.1.0
- テストデータ: 83 ファイル / 20 フォルダー
- 判定実行回数: 498（カルチャー 2 通り × ストラテジー 3 通り）

## 実行環境

```
Os             : OsInformation { IsWindows = True, IsMacOs = False, IsLinux = False, Description = Microsoft Windows 10.0.26200 }
Runtime        : DotNetRuntimeInformation { FrameworkDescription = .NET 10.0.12, IsDotNetFramework = False, IsCodePagesEncodingProviderRegistered = True }
Locale         : LocaleInformation { CurrentCulture = ja-JP, AnsiCodePage = 932, OemCodePage = 932, IsAnsiOemSame = True }
PowerShellHost : PowerShellHostInformation { PSVersion = 7.6.6, PSEdition = Core, SupportsNumericCodePageArgument = True, SupportsAnsiEncodingName = True }
```

## 判定の見かた

`testinfo.txt` が申告する文字エンコーディング名を期待値とし、`Resolve-Encoding` が返す `CodePage` と突き合わせています。
ホストによって意味が変わる `PSEncodingName` ではなく `CodePage` を比較軸にしています。

| 判定 | 意味 |
|---|---|
| 一致 | 申告どおりのコードページを返した |
| 許容 | 申告とは違うが内容上それを責められない。次の 3 つのいずれか。(1) 他のテストファイルとバイト列が完全に同一、(2) 同一符号化の別コードページ番号、(3) 申告の符号化と返ってきた符号化で、このバイト列の復号結果が完全に一致する |
| 不一致 | 別の符号化と判定した |
| 判定不能 | `CodePage = -1` を返した |
| エラー | 例外が発生した |

## 総合結果

カルチャー指定の有無と判定ストラテジーの組み合わせごとの件数です（各 83 ファイル）。

| カルチャー | ストラテジー | 一致 | 許容 | 不一致 | 判定不能 | エラー | 一致+許容 |
|---|---|---:|---:|---:|---:|---:|---:|
| 既定 | Combined | 59 | 4 | 5 | 15 | 0 | 75.9% |
| 既定 | NativeOnly | 50 | 2 | 4 | 27 | 0 | 62.7% |
| 既定 | UtfUnknownOnly | 58 | 6 | 1 | 18 | 0 | 77.1% |
| 言語指定 | Combined | 62 | 6 | 1 | 14 | 0 | 81.9% |
| 言語指定 | NativeOnly | 54 | 4 | 0 | 25 | 0 | 69.9% |
| 言語指定 | UtfUnknownOnly | 58 | 6 | 1 | 18 | 0 | 77.1% |

「カルチャー = 既定」は `-Culture` を指定せず、セッションのカルチャーに任せた場合です。
「言語指定」は言語フォルダーに対応するカルチャー（`CJK_Korean` なら `ko-KR` など）を明示した場合です。

## 主結果の詳細（カルチャー = 言語指定 / ストラテジー = Combined）

一致 62 / 許容 6 / 不一致 1 / 判定不能 14 / エラー 0（全 83 ファイル）

| # | ファイル | 申告 | 判定結果 (CodePage / WebName) | BOM | 改行 | 判定 | 備考 |
|---:|---|---|---|:-:|:-:|---|---|
| 1 | English/english_ascii.txt | US-ASCII | 20127 / us-ascii | OK | OK | 一致 |  |
| 2 | English/english_ascii_crlf.txt | US-ASCII | 20127 / us-ascii | OK | OK | 一致 |  |
| 3 | English/english_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 4 | English/english_utf8_bom.txt | UTF-8 with BOM | 65001 / utf-8 | OK | OK | 一致 |  |
| 5 | English/english_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 6 | English/english_utf16be_bom.txt | UTF-16BE with BOM | 1201 / unicodeFFFE | OK | OK | 一致 |  |
| 7 | English/english_utf16le_nobom.txt | UTF-16LE without BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 8 | English/english_utf32le_bom.txt | UTF-32LE with BOM | 12000 / utf-32 | OK | OK | 一致 |  |
| 9 | English/english_windows1252.txt | Windows-1252 | 1250 / windows-1250 | OK | OK | 許容 | 復号結果が申告の符号化と同一（cp1252 と cp1250） |
| 10 | Spanish/spanish_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 11 | Spanish/spanish_utf8_bom.txt | UTF-8 with BOM | 65001 / utf-8 | OK | OK | 一致 |  |
| 12 | Spanish/spanish_windows1252.txt | Windows-1252 | 28591 / iso-8859-1 | OK | OK | 不一致 |  |
| 13 | Spanish/spanish_iso8859_1.txt | ISO-8859-1 | 28591 / iso-8859-1 | OK | OK | 一致 |  |
| 14 | French/french_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 15 | French/french_windows1252.txt | Windows-1252 | — | OK | OK | 判定不能 |  |
| 16 | French/french_iso8859_1.txt | ISO-8859-1 | — | OK | OK | 判定不能 |  |
| 17 | French/french_iso8859_15.txt | ISO-8859-15 | — | OK | OK | 判定不能 |  |
| 18 | German/german_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 19 | German/german_utf8_bom.txt | UTF-8 with BOM | 65001 / utf-8 | OK | OK | 一致 |  |
| 20 | German/german_windows1252.txt | Windows-1252 | 1252 / windows-1252 | OK | OK | 一致 |  |
| 21 | German/german_iso8859_1.txt | ISO-8859-1 | 28591 / iso-8859-1 | OK | OK | 一致 |  |
| 22 | Italian/italian_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 23 | Italian/italian_windows1252.txt | Windows-1252 | — | OK | OK | 判定不能 |  |
| 24 | Italian/italian_iso8859_1.txt | ISO-8859-1 | — | OK | OK | 判定不能 |  |
| 25 | Portuguese/portuguese_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 26 | Portuguese/portuguese_windows1252.txt | Windows-1252 | 1252 / windows-1252 | OK | OK | 一致 |  |
| 27 | Portuguese/portuguese_iso8859_1.txt | ISO-8859-1 | 28591 / iso-8859-1 | OK | OK | 一致 |  |
| 28 | Polish/polish_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 29 | Polish/polish_windows1250.txt | Windows-1250 | — | OK | OK | 判定不能 |  |
| 30 | Polish/polish_iso8859_2.txt | ISO-8859-2 | — | OK | OK | 判定不能 |  |
| 31 | Turkish/turkish_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 32 | Turkish/turkish_windows1254.txt | Windows-1254 | 28599 / iso-8859-9 | OK | OK | 許容 | バイト列が同一: turkish_iso8859_9.txt |
| 33 | Turkish/turkish_iso8859_9.txt | ISO-8859-9 | 28599 / iso-8859-9 | OK | OK | 一致 |  |
| 34 | Russian/russian_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 35 | Russian/russian_utf8_bom.txt | UTF-8 with BOM | 65001 / utf-8 | OK | OK | 一致 |  |
| 36 | Russian/russian_windows1251.txt | Windows-1251 | — | OK | OK | 判定不能 |  |
| 37 | Russian/russian_koi8r.txt | KOI8-R | — | OK | OK | 判定不能 |  |
| 38 | Russian/russian_iso8859_5.txt | ISO-8859-5 | — | OK | OK | 判定不能 |  |
| 39 | Russian/russian_cp866.txt | IBM866 | — | OK | OK | 判定不能 |  |
| 40 | Russian/russian_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 41 | Arabic/arabic_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 42 | Arabic/arabic_windows1256.txt | Windows-1256 | 1256 / windows-1256 | OK | OK | 一致 |  |
| 43 | Arabic/arabic_iso8859_6.txt | ISO-8859-6 | 28596 / iso-8859-6 | OK | OK | 一致 |  |
| 44 | Arabic/arabic_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 45 | Hindi/hindi_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 46 | Hindi/hindi_utf8_bom.txt | UTF-8 with BOM | 65001 / utf-8 | OK | OK | 一致 |  |
| 47 | Hindi/hindi_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 48 | Hindi/hindi_utf16be_bom.txt | UTF-16BE with BOM | 1201 / unicodeFFFE | OK | OK | 一致 |  |
| 49 | Bengali/bengali_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 50 | Bengali/bengali_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 51 | Vietnamese/vietnamese_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 52 | Vietnamese/vietnamese_utf8_nfd.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 53 | Vietnamese/vietnamese_windows1258.txt | Windows-1258 | — | OK | OK | 判定不能 |  |
| 54 | Vietnamese/vietnamese_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 55 | Thai/thai_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 56 | Thai/thai_windows874.txt | Windows-874 | — | OK | OK | 判定不能 |  |
| 57 | Thai/thai_iso8859_11.txt | ISO-8859-11 | — | OK | OK | 判定不能 |  |
| 58 | Indonesian_Malay/indonesian_malay_ascii.txt | US-ASCII | 20127 / us-ascii | OK | OK | 一致 |  |
| 59 | Indonesian_Malay/indonesian_malay_utf8.txt | UTF-8 | 20127 / us-ascii | OK | OK | 許容 | バイト列が同一: indonesian_malay_ascii.txt / indonesian_malay_windows1252.txt |
| 60 | Indonesian_Malay/indonesian_malay_windows1252.txt | Windows-1252 | 20127 / us-ascii | OK | OK | 許容 | バイト列が同一: indonesian_malay_ascii.txt / indonesian_malay_utf8.txt |
| 61 | CJK_Japanese/japanese_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 62 | CJK_Japanese/japanese_utf8_bom.txt | UTF-8 with BOM | 65001 / utf-8 | OK | OK | 一致 |  |
| 63 | CJK_Japanese/japanese_utf8_crlf.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 64 | CJK_Japanese/japanese_shiftjis.txt | Shift_JIS | 932 / shift_jis | OK | OK | 一致 |  |
| 65 | CJK_Japanese/japanese_shiftjis_hankaku.txt | Shift_JIS | 932 / shift_jis | OK | OK | 一致 |  |
| 66 | CJK_Japanese/japanese_eucjp.txt | EUC-JP | 20932 / euc-jp | OK | OK | 一致 |  |
| 67 | CJK_Japanese/japanese_iso2022jp.txt | ISO-2022-JP | 50220 / iso-2022-jp | OK | OK | 一致 |  |
| 68 | CJK_Japanese/japanese_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | NG | 一致 |  |
| 69 | CJK_Japanese/japanese_utf16be_bom.txt | UTF-16BE with BOM | 1201 / unicodeFFFE | OK | NG | 一致 |  |
| 70 | CJK_Korean/korean_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 71 | CJK_Korean/korean_euckr.txt | EUC-KR | 949 / cp949 | OK | OK | 許容 | バイト列が同一: korean_cp949.txt |
| 72 | CJK_Korean/korean_cp949.txt | UHC | 949 / cp949 | OK | OK | 一致 |  |
| 73 | CJK_Korean/korean_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 74 | CJK_Traditional_Chinese_Taiwan/traditional_chinese_tw_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 75 | CJK_Traditional_Chinese_Taiwan/traditional_chinese_tw_big5.txt | Big5 | 950 / big5 | OK | OK | 一致 |  |
| 76 | CJK_Traditional_Chinese_Taiwan/traditional_chinese_tw_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | NG | 一致 |  |
| 77 | CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 78 | CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gb18030.txt | GB18030 | 936 / gbk | OK | OK | 許容 | バイト列が同一: simplified_chinese_cn_gbk.txt |
| 79 | CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gbk.txt | GBK | 936 / gbk | OK | OK | 一致 |  |
| 80 | CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_utf16le_bom.txt | UTF-16LE with BOM | 1200 / utf-16 | OK | OK | 一致 |  |
| 81 | CJK_Traditional_Chinese_HongKong/traditional_chinese_hk_utf8.txt | UTF-8 | 65001 / utf-8 | OK | OK | 一致 |  |
| 82 | CJK_Traditional_Chinese_HongKong/traditional_chinese_hk_utf8_bom.txt | UTF-8 with BOM | 65001 / utf-8 | OK | OK | 一致 |  |
| 83 | CJK_Traditional_Chinese_HongKong/traditional_chinese_hk_big5hkscs.txt | Big5-HKSCS | 950 / big5 | OK | OK | 一致 |  |

## 期待どおりにならなかったファイル

主結果で「不一致」「判定不能」「エラー」となった 15 件です。先頭 16 バイトを添えます。

| ファイル | 申告 | 期待 CodePage | 実際 | 判定 | 先頭16バイト |
|---|---|---:|---|---|---|
| Spanish/spanish_windows1252.txt | Windows-1252 | 1252 | 28591 / iso-8859-1 | 不一致 | `45 73 70 61 F1 6F 6C 20 28 63 61 73 74 65 6C 6C` |
| French/french_windows1252.txt | Windows-1252 | 1252 | 判定不能 | 判定不能 | `46 72 61 6E E7 61 69 73 0A 50 6F 72 74 65 7A 20` |
| French/french_iso8859_1.txt | ISO-8859-1 | 28591 | 判定不能 | 判定不能 | `46 72 61 6E E7 61 69 73 0A 50 6F 72 74 65 7A 20` |
| French/french_iso8859_15.txt | ISO-8859-15 | 28605 | 判定不能 | 判定不能 | `46 72 61 6E E7 61 69 73 0A 50 6F 72 74 65 7A 20` |
| Italian/italian_windows1252.txt | Windows-1252 | 1252 | 判定不能 | 判定不能 | `49 74 61 6C 69 61 6E 6F 0A 50 72 61 6E 7A 6F 20` |
| Italian/italian_iso8859_1.txt | ISO-8859-1 | 28591 | 判定不能 | 判定不能 | `49 74 61 6C 69 61 6E 6F 0A 50 72 61 6E 7A 6F 20` |
| Polish/polish_windows1250.txt | Windows-1250 | 1250 | 判定不能 | 判定不能 | `50 6F 6C 73 6B 69 0A 50 63 68 6E B9 E6 20 77 20` |
| Polish/polish_iso8859_2.txt | ISO-8859-2 | 28592 | 判定不能 | 判定不能 | `50 6F 6C 73 6B 69 0A 50 63 68 6E B1 E6 20 77 20` |
| Russian/russian_windows1251.txt | Windows-1251 | 1251 | 判定不能 | 判定不能 | `D0 F3 F1 F1 EA E8 E9 20 FF E7 FB EA 0A D1 FA E5` |
| Russian/russian_koi8r.txt | KOI8-R | 20866 | 判定不能 | 判定不能 | `F2 D5 D3 D3 CB C9 CA 20 D1 DA D9 CB 0A F3 DF C5` |
| Russian/russian_iso8859_5.txt | ISO-8859-5 | 28595 | 判定不能 | 判定不能 | `C0 E3 E1 E1 DA D8 D9 20 EF D7 EB DA 0A C1 EA D5` |
| Russian/russian_cp866.txt | IBM866 | 866 | 判定不能 | 判定不能 | `90 E3 E1 E1 AA A8 A9 20 EF A7 EB AA 0A 91 EA A5` |
| Vietnamese/vietnamese_windows1258.txt | Windows-1258 | 1258 | 判定不能 | 判定不能 | `54 69 EA EC 6E 67 20 56 69 EA F2 74 0A 54 F4 69` |
| Thai/thai_windows874.txt | Windows-874 | 874 | 判定不能 | 判定不能 | `C0 D2 C9 D2 E4 B7 C2 0A E0 BB E7 B9 C1 B9 D8 C9` |
| Thai/thai_iso8859_11.txt | ISO-8859-11 | 28601 | 判定不能 | 判定不能 | `C0 D2 C9 D2 E4 B7 C2 0A E0 BB E7 B9 C1 B9 D8 C9` |

## -Culture 指定の効果

`-Culture` の指定有無で判定結果が変わったのは 5 ファイルです。

| ファイル | 申告 | 既定カルチャー | 言語カルチャーを指定 |
|---|---|---|---|
| CJK_Korean/korean_euckr.txt | EUC-KR | 20932 / euc-jp（不一致） | 949 / cp949（許容） |
| CJK_Korean/korean_cp949.txt | UHC | 20932 / euc-jp（不一致） | 949 / cp949（一致） |
| CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gb18030.txt | GB18030 | 20932 / euc-jp（不一致） | 936 / gbk（許容） |
| CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gbk.txt | GBK | 20932 / euc-jp（不一致） | 936 / gbk（一致） |
| CJK_Traditional_Chinese_HongKong/traditional_chinese_hk_big5hkscs.txt | Big5-HKSCS | 判定不能 | 950 / big5（一致） |

## -Strategy の効果

カルチャーを言語指定にしたうえで、ストラテジー間で判定結果が変わったファイルです。

差が出たのは 18 ファイルです（値は CodePage）。

| ファイル | 申告 | Combined | NativeOnly | UtfUnknownOnly |
|---|---|---|---|---|
| English/english_utf16le_nobom.txt | UTF-16LE without BOM | 1200 | 1200 | 判定不能 |
| English/english_windows1252.txt | Windows-1252 | 1250 | 判定不能 | 1250 |
| Spanish/spanish_windows1252.txt | Windows-1252 | 28591 | 判定不能 | 28591 |
| Spanish/spanish_iso8859_1.txt | ISO-8859-1 | 28591 | 判定不能 | 28591 |
| German/german_windows1252.txt | Windows-1252 | 1252 | 判定不能 | 1252 |
| German/german_iso8859_1.txt | ISO-8859-1 | 28591 | 判定不能 | 28591 |
| Portuguese/portuguese_windows1252.txt | Windows-1252 | 1252 | 判定不能 | 1252 |
| Portuguese/portuguese_iso8859_1.txt | ISO-8859-1 | 28591 | 判定不能 | 28591 |
| Turkish/turkish_windows1254.txt | Windows-1254 | 28599 | 判定不能 | 28599 |
| Turkish/turkish_iso8859_9.txt | ISO-8859-9 | 28599 | 判定不能 | 28599 |
| Arabic/arabic_windows1256.txt | Windows-1256 | 1256 | 判定不能 | 1256 |
| Arabic/arabic_iso8859_6.txt | ISO-8859-6 | 28596 | 判定不能 | 28596 |
| CJK_Japanese/japanese_eucjp.txt | EUC-JP | 20932 | 20932 | 51932 |
| CJK_Korean/korean_euckr.txt | EUC-KR | 949 | 949 | 判定不能 |
| CJK_Korean/korean_cp949.txt | UHC | 949 | 949 | 判定不能 |
| CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gb18030.txt | GB18030 | 936 | 936 | 54936 |
| CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gbk.txt | GBK | 936 | 936 | 54936 |
| CJK_Traditional_Chinese_HongKong/traditional_chinese_hk_big5hkscs.txt | Big5-HKSCS | 950 | 950 | 判定不能 |

## BOM と改行コードの判定

- BOM の有無: 不一致 0 件
- 改行コード: 不一致 3 件

### 改行コードが期待と違ったファイル

「復号して数えた改行」は、申告どおりの符号化でファイルを文字列に復号し、実際に現れる改行を数えた真値です。

| ファイル | 申告 | 期待 | 判定結果 | 復号して数えた改行 |
|---|---|---|---|---|
| CJK_Japanese/japanese_utf16le_bom.txt | UTF-16LE with BOM | Lf | LfAndCr | CRLF=0 / 単独CR=0 / 単独LF=5 |
| CJK_Japanese/japanese_utf16be_bom.txt | UTF-16BE with BOM | Lf | LfAndCr | CRLF=0 / 単独CR=0 / 単独LF=5 |
| CJK_Traditional_Chinese_Taiwan/traditional_chinese_tw_utf16le_bom.txt | UTF-16LE with BOM | Lf | LfAndCr | CRLF=0 / 単独CR=0 / 単独LF=5 |

判定結果が `LfAndCr` なのに単独 CR が 0 件であれば、ファイルに CR は存在しません。
UTF-16 では CJK 文字の片側のバイトが `0x0D` になることがあり（`U+300D` の「」」など）、
符号位置ではなく生バイトで改行を数えていると、そのバイトを CR と取り違えます。

## PSEncodingName の状況

このプロパティはホストによって意味が変わるため判定の正否には使っていません。参考として集計します。

- 値が入っていたファイル: 69 / 83
- `UsePSName` が真: 48 / 83

| PSEncodingName | 件数 |
|---|---:|
| ascii | 5 |
| big5 | 2 |
| bigendianunicode | 3 |
| cp949 | 2 |
| euc-jp | 1 |
| gbk | 2 |
| I do not know. | 11 |
| iso-2022-jp | 1 |
| shift_jis | 2 |
| unicode | 11 |
| utf32 | 1 |
| utf8BOM | 7 |
| utf8NoBOM | 21 |

## バイト列が完全に同一のファイル群

これらは内容だけでは区別できません。どれか一つの符号化を返すのが妥当な動作であり、申告と違っても「許容」としています。

| ファイル群 | それぞれの申告 |
|---|---|
| CJK_Korean/korean_euckr.txt<br>CJK_Korean/korean_cp949.txt | EUC-KR<br>UHC |
| CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gb18030.txt<br>CJK_Simplified_Chinese_Mainland/simplified_chinese_cn_gbk.txt | GB18030<br>GBK |
| Indonesian_Malay/indonesian_malay_ascii.txt<br>Indonesian_Malay/indonesian_malay_utf8.txt<br>Indonesian_Malay/indonesian_malay_windows1252.txt | US-ASCII<br>UTF-8<br>Windows-1252 |
| Thai/thai_windows874.txt<br>Thai/thai_iso8859_11.txt | Windows-874<br>ISO-8859-11 |
| Turkish/turkish_windows1254.txt<br>Turkish/turkish_iso8859_9.txt | Windows-1254<br>ISO-8859-9 |

---

生データは同フォルダーの `TestResult_PS7.csv` にあります（BOM 無し UTF-8）。
このレポートは `tools/Invoke-EncodingProbeTest.ps1` が生成しました。
