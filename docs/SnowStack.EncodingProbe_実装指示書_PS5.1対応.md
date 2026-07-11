# SnowStack.EncodingProbe 実装指示書 — PSEncodingName の PS5.1 対応（差分）

対象リポジトリ: `motoi-tsushima/SnowStack.EncodingProbe`（コミット `5c189b7` 時点）

この指示書は **差分のみ** を扱います。以下はすでに実装済みで、変更不要です。
- `EncodingProbeModuleInitializer`（CodePagesEncodingProvider登録）
- `EncodingDetector.PSEncodingName()` のフレンドリ名 / WebNameフォールバック（PS6.2+向け、現状のまま）
- `SetUsePSName()` の `KnownPSFriendlyNames` によるフレンドリ名判定（PS6.2+向け、現状のまま）
- `DotNetEncoding` 等の生Encodingインスタンス返却プロパティ（追加しない）

## 目的

`EncodingDetector.PSEncodingName(int codePage, bool bom)` は現在、ビルドターゲット（net10.0 / net48）にかかわらず同じロジック（PS6.2+向けフレンドリ名 or WebNameフォールバック）を返している。これを **ビルドターゲットで分岐** させ、net48（＝Windows PowerShell 5.1想定）では、PS5.1の固定`-Encoding`列挙値に一致する場合のみ値を返し、一致しない場合は`null`を返すようにする。

## 変更1: `SnowStack.EncodingProbe/EncodingDetector.cs`

対象: `PSEncodingName(int, bool)` メソッドと `SetUsePSName(string)` メソッド（293〜353行目付近）、および `KnownPSFriendlyNames` フィールド宣言。

### 1-1. `KnownPSFriendlyNames` フィールドを `#if !NETFRAMEWORK` で囲む

net48ビルドでは未使用になるため、コンパイラ警告を避けるためプリプロセッサディレクティブで囲む。

### 1-2. `PSEncodingName` と `SetUsePSName` を `#if NETFRAMEWORK` / `#else` で分岐

**net48ブランチ（新規追加）**: PS5.1の固定`-Encoding`列挙値に一致する場合のみ値を返す。

マッピング仕様:

| 検出コードページ | 条件 | 返す値 |
|---|---|---|
| 20127 (ASCII) | 常に | `"Ascii"` |
| 1200 (UTF-16LE) | `bom == true` のみ | `"Unicode"` |
| 1201 (UTF-16BE) | `bom == true` のみ | `"BigEndianUnicode"` |
| 12000 (UTF-32LE) | `bom == true` のみ | `"UTF32"` |
| 12001 (UTF-32BE) | `bom == true` のみ | `"BigEndianUTF32"` |
| 65001 (UTF-8) | `bom == true` のみ | `"UTF8"` |
| 上記以外すべて（Shift-JIS, EUC-JP, GB2312等のレガシーコードページ、およびBOM条件を満たさないUnicode系） | — | `null` |

理由（コード中にコメントとして残すこと）:
- `Default`/`Oem`はロケール依存のため対象外
- `String`/`Unknown`は`Unicode`のエイリアスのため独立枝を持たない
- `UTF7`は検出未実装のため対象外
- Unicode系（Unicode/BigEndianUnicode/UTF32/BigEndianUTF32/UTF8）はPS5.1では書き込み時に必ずBOMを生成するため、`bom == true`の場合のみ一致とみなす（BOMなし検出結果をこれらの名前で返すとラウンドトリップ時にBOMが不正に付与されるため）

**net10.0ブランチ**: 既存の実装をそのまま`#else`節に移すだけ（ロジック変更なし）。

`SetUsePSName`も同様に分岐:
- net48ブランチ: `PSEncodingName`が非null（＝固定名一覧との一致が保証されている）なら`true`
- net10.0ブランチ: 既存の`KnownPSFriendlyNames.Contains`判定のまま

### 1-3. XMLドキュメントコメントの更新

- `PSEncodingName`メソッドのコメントに「net48ビルド（PS5.1向け）」「net10.0ビルド（PS6.2+向け）」の説明をそれぞれの`#if`節に追加する
- `SnowStack.EncodingProbe/EncodingInformation.cs` の `PSEncodingName`/`UsePSName`プロパティのXMLコメントを更新する。特に`UsePSName`の既存コメントは「数値コードページの文字列（例: "932"）の場合はfalse」という**現状と食い違う古い記述**が残っているため、「WebNameの場合はfalse」に修正し、あわせて「net48ビルドでは、固定名一覧に一致しない場合PSEncodingNameはnullになる」旨を追記する

## 変更2: テストプロジェクトのマルチターゲット化

対象: `tests/EncodingProbe.Tests/EncodingProbe.Tests.csproj`

```xml
<!-- 変更前 -->
<TargetFramework>net10.0</TargetFramework>

<!-- 変更後 -->
<TargetFrameworks>net10.0;net48</TargetFrameworks>
```

（要素名が単数形`TargetFramework`から複数形`TargetFrameworks`になる点に注意）

## 変更3: 既存テストのTFM対応（重要・必須）

上記の変更2により、`JapaneseEncodingTests.cs`と`EnglishEncodingTests.cs`にある`[Theory][InlineData]`ベースの既存テストが、net48ビルドでも**そのまま同じ期待値で**実行されるようになる。しかし以下のケースでnet48ビルド時に既存の期待値と実際の値が食い違い、**テストが失敗する**：

- `EnglishEncodingTests.cs`: `"ascii"`期待 → net48実際は`"Ascii"`（大文字違い）。`"utf8NoBOM"`期待 → net48実際は`null`。`"utf8BOM"`期待 → net48実際は`"UTF8"`
- `JapaneseEncodingTests.cs`: Shift-JIS等のレガシーコードページ期待値（WebName文字列）→ net48実際は`null`

対応方針: 各`InlineData`の`expectedPSEncodingName`をハードコードされた単一文字列のまま両TFMで共用せず、**TFM条件付き定数**に置き換える。例:

```csharp
#if NETFRAMEWORK
    private const string ExpectedAsciiPSName = "Ascii";
    private const string ExpectedUtf8NoBomPSName = null;
    private const string ExpectedUtf8BomPSName = "UTF8";
#else
    private const string ExpectedAsciiPSName = "ascii";
    private const string ExpectedUtf8NoBomPSName = "utf8NoBOM";
    private const string ExpectedUtf8BomPSName = "utf8BOM";
#endif
```

のような定数を各テストクラスに用意し、`InlineData`の代わりにテストメソッド内で参照する（`InlineData`属性の引数はコンパイル時定数である必要があるため、`Theory`のデータ列自体を分けるか、`MemberData`に切り替えるなど、既存のテスト構造に合わせて適切な形に調整すること）。Shift-JIS等のレガシーコードページ系テストケースも同様に、net48では期待値を`null`にする。

## 変更4: net48向けの新規テスト追加

`tests/EncodingProbe.Tests/DetectorTests/`配下に、PS5.1マッピング仕様（本指示書の1-2節の表）を直接検証するテスト（`#if NETFRAMEWORK`ブロック内、または別ファイルとして）を追加する。最低限、以下をカバーすること:

- BOMなしUTF-16LE検出時に`PSEncodingName == null`（BOM条件チェック）
- BOMなしUTF-8検出時に`PSEncodingName == null`
- BOM付き各Unicode系（Unicode/BigEndianUnicode/UTF32/BigEndianUTF32/UTF8）で正しい固定名が返ること
- Shift-JIS等レガシーコードページ検出時に`PSEncodingName == null`
- 上記すべてのケースで`UsePSName`が`PSEncodingName != null`と一致すること

## 変更不要（念のため明記）

- `EncodingProbe.PowerShell.Tests`プロジェクトのTFM変更は不要（今回のスコープ外）
- `EncodingProbeModuleInitializer.cs`は変更不要
- `PowerShellHostInformation`/`Get-EncodingProbePlatformInfo`関連ファイルは変更不要
