# SnowStack.EncodingProbe 実装指示書

このドキュメントは、`SnowStack.EncodingProbe` / `SnowStack.EncodingProbe.PowerShell` の設計検討の結果をまとめた実装指示書です。Claude（claude.ai）との設計会話の結果を、Claude Code での実装作業に引き継ぐことを目的としています。

対象リポジトリ: https://github.com/motoi-tsushima/SnowStack.EncodingProbe

---

## 1. 全体方針

### 1.1 リリース戦略

- **1.0.0.0**: `Resolve-Encoding` による検出機能のみを正式リリース対象とする
- **1.1.0.0以降**: 読み込みヘルパー（`Get-PSEncodingArgument` 相当）、書き込みヘルパー（`Set-ContentNoBom` 相当）を追加

### 1.2 PS5.1 対応方針（1.0.0.0時点）

- コード側での自動判定・自動分岐ロジックは実装しない（**案A採用**）
- PS5.1特有の制約（固定enumしか使えない、BOM無し出力ができない等）は **README / ブログ記事側でドキュメント化** して案内する
- `UsePSName` プロパティにより「PS6.2以降ならそのまま `-Encoding` に渡せるか」だけを判定できるようにする

### 1.3 設計原則

- `SnowStack.EncodingProbe`（コア）は「検出結果を返す」という単一責務に徹する
- 実行環境情報（OS種別、.NETランタイム、ロケール、PowerShellバージョン等）は検出結果とは別のAPI（`Get-EncodingProbePlatformInfo`）に分離する
- 型の一貫性を優先する（同じコマンドの戻り値の型が条件によって変化する設計は避ける）

---

## 2. `EncodingInformation`（コア: 検出結果）

### 2.1 現状の実装（preview5時点）

```csharp
namespace SnowStack.EncodingProbe;

public sealed record EncodingInformation
{
    public int CodePage { get; internal set; }
    public string EncodingName { get; internal set; }
    public string PSEncodingName { get; internal set; }
    public bool UsePSName { get; internal set; }
    public bool Bom { get; internal set; }
    public LineBreakType LineBreak { get; internal set; } = LineBreakType.None;
    public bool IsWindowsOs { get; } = PlatformInfo.IsWindows;
    public bool IsMacOs { get; } = PlatformInfo.IsMacOs;
    public bool IsLinuxOs { get; } = PlatformInfo.IsLinux;
    public string Culture { get; internal set; } = null;
}
```

### 2.2 変更方針

| 項目 | 変更内容 | 理由 |
|---|---|---|
| `EncodingName` | `EncodingWebName` にリネーム検討 | 実際の値が `.NET Encoding.WebName` 相当（例: `"utf-8"`）であり、`.NET Encoding.EncodingName`（表示名、例: `"Unicode (UTF-8)"`）とは異なるため、命名を値の実体に合わせる |
| `PSEncodingName` | **変更なし** | 用途（PowerShell向け整形値）を示す命名軸であり、`EncodingWebName`（値の種類を示す命名軸）とは別軸のため整合性は保たれる |
| `UsePSName` | 変更なし | シンプルな二値判定のままで良い。ただし判定基準（何を根拠にtrue/falseとするか）をXMLコメントに明記すること |
| `IsWindowsOs` / `IsMacOs` / `IsLinuxOs` | **削除**し、`Get-EncodingProbePlatformInfo`（後述）に分離 | 個々のファイル検出結果とは無関係な静的情報が同居しており、単一責任の観点で不適切なため |
| `Culture` | `string?` に変更（Nullable Reference Types対応） | 現状 `= null` を代入しているにも関わらず型が `string` のままで、NRT有効化時に警告が出るため |

### 2.3 変更後のイメージ

```csharp
namespace SnowStack.EncodingProbe;

/// <summary>
/// 文字エンコーディング判定情報
/// </summary>
public sealed record EncodingInformation
{
    /// <summary>コードページ</summary>
    public int CodePage { get; internal set; }

    /// <summary>
    /// エンコーディングのWebName相当の識別子
    /// (.NET Encoding.WebName に対応する値。例: "utf-8")
    /// </summary>
    public string EncodingWebName { get; internal set; } = string.Empty;

    /// <summary>PowerShell -Encoding パラメータ用の名称</summary>
    public string PSEncodingName { get; internal set; } = string.Empty;

    /// <summary>
    /// PSEncodingName をそのまま -Encoding に渡してよいか。
    /// PowerShell 6.2以降で登録済みの名称と一致する場合に true。
    /// </summary>
    public bool UsePSName { get; internal set; }

    /// <summary>BOMの有無</summary>
    public bool Bom { get; internal set; }

    /// <summary>改行コードの種類</summary>
    public LineBreakType LineBreak { get; internal set; } = LineBreakType.None;

    /// <summary>実行中のカルチャー名</summary>
    public string? Culture { get; internal set; }
}
```

**注意**: `EncodingName` → `EncodingWebName` のリネームは破壊的変更のため、1.0.0.0リリース前（preview段階）のうちに確定させること。

---

## 3. `PlatformInformation`（コア: 実行環境情報 / 新設）

コア層（`SnowStack.EncodingProbe`）に配置。PowerShell固有の情報（`PSVersion`等）は含めない。

### 3.1 `OsInformation.cs`

```csharp
namespace SnowStack.EncodingProbe;

/// <summary>
/// 実行中のOS種別に関する情報
/// </summary>
public sealed record OsInformation
{
    /// <summary>実行中のOSがWindowsかどうか</summary>
    public bool IsWindows { get; internal set; }

    /// <summary>実行中のOSがmacOSかどうか</summary>
    public bool IsMacOs { get; internal set; }

    /// <summary>実行中のOSがLinuxかどうか</summary>
    public bool IsLinux { get; internal set; }

    /// <summary>
    /// OSの詳細な説明文字列
    /// (System.Runtime.InteropServices.RuntimeInformation.OSDescription 相当)
    /// </summary>
    public string Description { get; internal set; } = string.Empty;
}
```

### 3.2 `DotNetRuntimeInformation.cs`

**命名注意**: `System.Runtime.InteropServices.RuntimeInformation` との名前衝突を避けるため `DotNetRuntimeInformation` とする。

```csharp
namespace SnowStack.EncodingProbe;

/// <summary>
/// 実行中の.NETランタイムに関する情報
/// </summary>
public sealed record DotNetRuntimeInformation
{
    /// <summary>
    /// フレームワークの説明文字列
    /// 例: ".NET 10.0.1"、".NET Framework 4.8.9037.0"
    /// </summary>
    public string FrameworkDescription { get; internal set; } = string.Empty;

    /// <summary>.NET Framework(4.8等)上で動作しているかどうか</summary>
    public bool IsDotNetFramework { get; internal set; }

    /// <summary>
    /// System.Text.Encoding.CodePages の追加コードページプロバイダーが
    /// 登録済み(利用可能)かどうか。
    /// trueの場合、Shift-JIS(932)やEUC-JP(51932)等の
    /// 追加コードページを Encoding.GetEncoding(int) で取得できる。
    /// </summary>
    public bool IsCodePagesEncodingProviderRegistered { get; internal set; }
}
```

### 3.3 `LocaleInformation.cs`

```csharp
namespace SnowStack.EncodingProbe;

/// <summary>
/// 実行中のロケール・コードページに関する情報
/// </summary>
public sealed record LocaleInformation
{
    /// <summary>現在のカルチャー名 例: "ja-JP"</summary>
    public string CurrentCulture { get; internal set; } = string.Empty;

    /// <summary>
    /// システムのANSIコードページ(Windows PowerShellの-Encoding "Default"相当)。
    /// 非Windows環境や取得不可の場合は -1。
    /// </summary>
    public int AnsiCodePage { get; internal set; } = -1;

    /// <summary>
    /// システムのOEMコードページ(Windows PowerShellの-Encoding "Oem"相当)。
    /// 非Windows環境や取得不可の場合は -1。
    /// </summary>
    public int OemCodePage { get; internal set; } = -1;

    /// <summary>
    /// ANSIとOEMが同一コードページかどうか。
    /// CJK圏(日本語/韓国語/中国語)ではtrueになりやすく、
    /// 西欧圏(英/独/仏)では通常falseになる。
    /// </summary>
    public bool IsAnsiOemSame => AnsiCodePage != -1 && AnsiCodePage == OemCodePage;
}
```

### 3.4 `PlatformInformation.cs`

```csharp
namespace SnowStack.EncodingProbe;

/// <summary>
/// 文字エンコーディング判定に関連する実行環境情報
/// </summary>
public sealed record PlatformInformation
{
    /// <summary>OS種別に関する情報</summary>
    public OsInformation Os { get; internal set; } = new();

    /// <summary>.NETランタイムに関する情報</summary>
    public DotNetRuntimeInformation Runtime { get; internal set; } = new();

    /// <summary>ロケール・コードページに関する情報</summary>
    public LocaleInformation Locale { get; internal set; } = new();
}
```

### 3.5 `PlatformInfoResolver.cs`（内部実装）

既存の `PlatformInfo` 静的クラス（`IsWindows` 等の単純な静的プロパティのみ）を、`PlatformInformation` を組み立てる内部リゾルバとして拡張する。

```csharp
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SnowStack.EncodingProbe;

/// <summary>
/// PlatformInformation を収集する内部ロジック
/// </summary>
internal static class PlatformInfoResolver
{
    public static PlatformInformation Resolve()
    {
        return new PlatformInformation
        {
            Os = ResolveOs(),
            Runtime = ResolveRuntime(),
            Locale = ResolveLocale(),
        };
    }

    private static OsInformation ResolveOs() => new()
    {
        IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
        IsMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
        IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
        Description = RuntimeInformation.OSDescription,
    };

    private static DotNetRuntimeInformation ResolveRuntime()
    {
        string description = RuntimeInformation.FrameworkDescription;

        return new DotNetRuntimeInformation
        {
            FrameworkDescription = description,
            IsDotNetFramework = description.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase),
            IsCodePagesEncodingProviderRegistered = IsCodePagesProviderRegistered(),
        };
    }

    private static bool IsCodePagesProviderRegistered()
    {
        // .NET Framework は追加コードページを標準サポートしているため常にtrue
        if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // .NET (Core/5+) では、Shift-JIS(932)取得可否で登録状態を判定する
        // ※ TODO: 既存コードで Encoding.RegisterProvider(...) を呼んでいる箇所があれば、
        //   その初期化済みフラグを直接参照する方式に差し替えることを検討する
        try
        {
            _ = System.Text.Encoding.GetEncoding(932);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static LocaleInformation ResolveLocale()
    {
        var culture = CultureInfo.CurrentCulture;
        int ansi = -1;
        int oem = -1;

        try
        {
            ansi = culture.TextInfo.ANSICodePage;
            oem = culture.TextInfo.OEMCodePage;
        }
        catch (NotSupportedException)
        {
            // 非Windows環境等、取得できない場合は-1のまま
        }

        return new LocaleInformation
        {
            CurrentCulture = culture.Name,
            AnsiCodePage = ansi,
            OemCodePage = oem,
        };
    }
}
```

**保留事項（TODO）**: `IsCodePagesProviderRegistered()` の判定方法は、実際にShift-JISのエンコーディング取得を試みて例外の有無で判定する暫定実装。既存コード内に `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` を呼んでいる初期化処理があれば、そのフラグを直接参照する方式に変更した方が正確かつ高速。**実装時に既存コードを確認して判断すること。**

---

## 4. PowerShell層（`SnowStack.EncodingProbe.PowerShell`）

### 4.1 `PowerShellHostInformation.cs`

PowerShell固有の情報のため、コア層ではなくPowerShell層に配置する。

```csharp
using System;

namespace SnowStack.EncodingProbe.PowerShell;

/// <summary>
/// PowerShellホストに関する情報
/// </summary>
public sealed record PowerShellHostInformation
{
    /// <summary>PowerShellのバージョン 例: 5.1.26100.1591 / 7.5.1</summary>
    public Version PSVersion { get; internal set; } = new(0, 0);

    /// <summary>"Desktop"(Windows PowerShell) または "Core"(PowerShell 6以降)</summary>
    public string PSEdition { get; internal set; } = string.Empty;

    /// <summary>
    /// -Encoding パラメータに数値コードページ(例: 51932)を直接指定できるかどうか。
    /// PSVersionが6.2以降であればtrue。
    /// </summary>
    public bool SupportsNumericCodePageArgument { get; internal set; }

    /// <summary>
    /// -Encoding "ansi" (PS7.4で追加された、廃止されたDefaultの代替値)が使用可能かどうか。
    /// PSVersionが7.4以降であればtrue。
    /// </summary>
    public bool SupportsAnsiEncodingName { get; internal set; }
}
```

### 4.2 `EncodingProbePlatformInformation.cs`

`Get-EncodingProbePlatformInfo` の戻り値型。**フラット展開方式を採用**（`Platform` プロパティにネストせず、`Os`/`Runtime`/`Locale`/`PowerShellHost` を並列に配置）。

```csharp
namespace SnowStack.EncodingProbe.PowerShell;

/// <summary>
/// Get-EncodingProbePlatformInfo が返す統合的な実行環境情報
/// </summary>
public sealed record EncodingProbePlatformInformation
{
    /// <summary>OS種別に関する情報</summary>
    public OsInformation Os { get; internal set; } = new();

    /// <summary>.NETランタイムに関する情報</summary>
    public DotNetRuntimeInformation Runtime { get; internal set; } = new();

    /// <summary>ロケール・コードページに関する情報</summary>
    public LocaleInformation Locale { get; internal set; } = new();

    /// <summary>PowerShellホストに関する情報</summary>
    public PowerShellHostInformation PowerShellHost { get; internal set; } = new();

    internal static EncodingProbePlatformInformation FromCore(
        PlatformInformation core,
        PowerShellHostInformation host)
    {
        return new EncodingProbePlatformInformation
        {
            Os = core.Os,
            Runtime = core.Runtime,
            Locale = core.Locale,
            PowerShellHost = host,
        };
    }
}
```

### 4.3 `GetEncodingProbePlatformInfoCommand.cs`（cmdlet本体）

```csharp
using System.Management.Automation;

namespace SnowStack.EncodingProbe.PowerShell;

[Cmdlet(VerbsCommon.Get, "EncodingProbePlatformInfo")]
[OutputType(typeof(EncodingProbePlatformInformation))]
public sealed class GetEncodingProbePlatformInfoCommand : PSCmdlet
{
    protected override void ProcessRecord()
    {
        var core = PlatformInfoResolver.Resolve();
        var host = ResolvePowerShellHost();

        WriteObject(EncodingProbePlatformInformation.FromCore(core, host));
    }

    private PowerShellHostInformation ResolvePowerShellHost()
    {
        // TODO: $PSVersionTable の正確な取得方法（SessionState経由 or $Host経由）を
        //       実装時に確定させること。以下は概略コード。
        var psVersionTable = (System.Collections.Hashtable)GetVariableValue("PSVersionTable")!;
        var psVersion = (System.Version)psVersionTable["PSVersion"]!;
        var psEdition = (string)psVersionTable["PSEdition"]!;

        return new PowerShellHostInformation
        {
            PSVersion = psVersion,
            PSEdition = psEdition,
            SupportsNumericCodePageArgument = psVersion >= new System.Version(6, 2),
            SupportsAnsiEncodingName = psVersion >= new System.Version(7, 4),
        };
    }
}
```

**注意**: `-IncludePlatformInfo` のようなスイッチで `Resolve-Encoding` の戻り値型を可変にする案は**不採用**。型の一貫性（`Resolve-Encoding` は常に `EncodingInformation` 型を返す）を優先し、環境情報は完全に別コマンド `Get-EncodingProbePlatformInfo` に分離する。

---

## 5. 未確定・要確認事項一覧

実装時に確認・判断が必要な項目をまとめる。

| # | 項目 | 内容 | 優先度 |
|---|---|---|---|
| 1 | `EncodingName` → `EncodingWebName` リネーム | 破壊的変更のため、1.0.0.0リリース前に確定させる必要あり | 高 |
| 2 | `Culture` の `string?` 化 | プロジェクト全体でNullable Reference Typesを有効化するかどうかの方針も合わせて確認 | 中 |
| 3 | `IsCodePagesEncodingProviderRegistered` の判定方法 | 既存コードに `Encoding.RegisterProvider(...)` の呼び出し箇所があるか確認し、あれば直接参照方式に変更 | 中 |
| 4 | `GetEncodingProbePlatformInfoCommand` の `$PSVersionTable` 取得方法 | `SessionState` 経由か `GetVariableValue` か、実装時に確定 | 中 |
| 5 | `UsePSName` の判定基準の明文化 | 何を根拠に true/false とするか（PS6.2以降の登録済みリストとの照合方法）をXMLコメント・READMEに明記 | 中 |
| 6 | 既存の `PlatformInfo` 静的クラスの扱い | `PlatformInfoResolver` への統合・改名を行うか、後方互換のため残すか | 低 |

---

## 6. 1.0.0.0 リリース時のドキュメント記載事項（案A対応）

READMEまたはブログ記事に、以下の内容を明記すること。

1. PS5.1では `PSEncodingName` をそのまま `-Encoding` に渡せない旨と、その理由（固定enum: `Unicode, BigEndianUnicode, UTF8, UTF7, UTF32, Ascii, Default, Oem, Byte, String, Unknown` のみ受理）
2. `UsePSName=false` の場合の代替手段の概要
   - `Default`/`Oem` はロケール依存（CJK圏では一致しやすいが西欧圏では別物）
   - BOM無しのUTF-8/UTF-16出力は標準cmdletでは不可能（`.NET`の`UTF8Encoding`/`UnicodeEncoding`を直接使う必要がある）
3. 「詳細な対応ヘルパー関数は将来バージョン(1.1.0.0)で提供予定」という導線

---

## 7. 将来（1.1.0.0）に向けた設計メモ

本メモは1.0.0.0のスコープ外だが、設計時に決めた前提として記録しておく。

- `Get-PSEncodingArgument`: `EncodingInformation` + `PowerShellHostInformation`（またはPSVersion）を受け取り、`-Encoding`に渡せる値をSplat可能なHashtable、または「表現不可能」を示すオブジェクトとして返す関数
- `Set-ContentNoBom`（仮称）: BOM無しUTF-8/UTF-16出力を標準cmdletの代替として提供する関数。パイプライン入力・`-Append`対応を検討
- PS7.x(6.2+)では、`EUC-JP(51932)`, `ISO-2022-JP(50220/50221/50222)`, `EUC-KR(51949)`, `GB18030(54936)`, `Windows-1251` 等、多数のレガシー/国際規格エンコーディングが数値コードページ指定で解決可能（`LocaleInformation`/`PowerShellHostInformation`の値を使って自動判定できる）
