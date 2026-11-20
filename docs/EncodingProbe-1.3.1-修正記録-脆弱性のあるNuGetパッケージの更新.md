# EncodingProbe 1.3.1 修正記録 — 脆弱性のある NuGet パッケージの更新

作業日: 2026-11-20
状態: **実装済み・未リリース**（利用者の指示により、今回はリリースしない。コミット・プッシュもしていない）

## 1. 概要

ビルド時に `NU1902` / `NU1903`（既知の脆弱性があるパッケージ）の警告が出ていた 3 つのパッケージを、修正版に上げた。

| パッケージ | 変更前 | 変更後 | 参照の仕方 | 参照しているプロジェクト |
|---|---|---|---|---|
| Microsoft.Build.Tasks.Git | 10.0.301 | 10.0.404 | 間接（`Microsoft.SourceLink.GitHub` の依存） | `SnowStack.EncodingProbe`（クラスライブラリ） |
| System.Security.Cryptography.Xml | 10.0.10 | 10.0.15 | 直接（`Microsoft.PowerShell.SDK` の依存でもある） | `tests/EncodingProbe.PowerShell.Tests` |
| System.Security.Cryptography.Pkcs | 10.0.10 | 10.0.15 | 間接（`Microsoft.PowerShell.SDK` などの依存） | `tests/EncodingProbe.PowerShell.Tests` |

あわせて、クラスライブラリ（NuGet パッケージ `SnowStack.EncodingProbe`）の版を 1.3.0 から 1.3.1 に上げた。
PowerShell モジュールの版は 1.3.0 のまま据え置いた（3 章）。

## 2. 調査の結果

### 2.1 どこで何に使っているか

- **Microsoft.Build.Tasks.Git** … `Microsoft.SourceLink.GitHub` が引く。ビルド・`dotnet pack` のときに MSBuild の中で動き、
  Git のコミット ID とリモート URL を読んで、PDB の SourceLink 情報と nuspec の `repository` 要素に書き込む。
  `PrivateAssets="All"` なので、NuGet パッケージの依存には現れない。DLL の実行時の動作には関わらない
- **System.Security.Cryptography.Xml / Pkcs** … テストプロジェクトが参照する `Microsoft.PowerShell.SDK`（テスト内で PowerShell をホストするため）の依存。
  Xml の直接参照は c95562b（2026-06-06）で 8.0.3 として足したもの（コミットメッセージに理由の記載は無い。間接参照の版を引き上げる目的と推測）
- ソース（`.cs` / `.ps1` / `.psm1`）に `SignedXml`・`SignedCms`・`EnvelopedCms` などの呼び出しは無い
- 配布物への影響の確認:
  - クラスライブラリの `.nupkg` の中身は `lib/net10.0`・`lib/net48` の DLL と README だけ。nuspec の依存は UTF.Unknown だけ
  - モジュールのビルド出力と `publish/.../core/SnowStack.EncodingProbe.PowerShell.deps.json` に Cryptography 系は無い

### 2.2 脆弱性（GitHub Advisory Database）

| パッケージ | アドバイザリ | 重大度 | 内容 | 修正版 |
|---|---|---|---|---|
| Microsoft.Build.Tasks.Git | GHSA-23fw-v26w-5fgq / CVE-2026-62900 | 中 5.9 | 情報漏えい（CWE-212: 機密情報を取り除かずに保存・送信する）。詳細は非公開 | 10.0.303（10.0.3xx 帯） |
| System.Security.Cryptography.Xml | GHSA-7hqp-7c5m-4vq6 / CVE-2026-96960 | 高 7.5 | 署名付き XML の処理での DoS | 10.0.13 |
| 〃 | GHSA-c399-9853-6jmj / CVE-2026-96959 | 高 7.5 | 同上（無制限なリソース確保） | 10.0.13 |
| 〃 | GHSA-r955-8q5j-3h83 / CVE-2026-96958 | 高 7.5 | 同上（計算量） | 10.0.13 |
| System.Security.Cryptography.Pkcs | GHSA-846f-f764-76mx / CVE-2026-84008 | 高 7.5 | `SignedCms.CheckSignature` の CPU 枯渇 | 10.0.13 |
| 〃 | GHSA-2fmv-rv2c-59x7 / CVE-2026-70162 | 中 5.9 | Windows の `EnvelopedCms.Decode` の CPU 枯渇 | 10.0.13 |

影響の評価:

- Xml / Pkcs はテストプロジェクトだけが使い、署名付きのデータを処理しないので、実害はほぼ無い
- Build.Tasks.Git はクラスライブラリの**ビルドの出力**（`.snupkg` の PDB、nuspec の `repository`）に関わる。DLL の実行時の動作には関わらない

### 2.3 既存の `.snupkg` の検査

`SnowStack.EncodingProbe/bin/Release/` にある 1.0.0-preview.1〜1.3.0 の 12 版・24 個の PDB と nuspec を検査した
（Portable PDB の SourceLink・コンパイルオプション・文書の一覧・埋め込みソースを読み、生のバイト列を GitHub のトークン・
`x-access-token`・`AUTHORIZATION`・`bearer`・`https://…@`・メールアドレスなどで検索した）。

- **認証情報やトークンは入っていなかった。** SourceLink の URL は `https://raw.githubusercontent.com/motoi-tsushima/SnowStack.EncodingProbe/<コミット ID>/*`、
  nuspec の `repository` は `https://github.com/motoi-tsushima/SnowStack.EncodingProbe` とコミット ID だけ
- 埋め込みソースは `obj\` の自動生成ファイル（AssemblyInfo・GlobalUsings・PolySharp のポリフィル）だけ。PDB の文書はすべてリポジトリ内
- 脆弱性とは別に、手元でビルドしたため `ContinuousIntegrationBuild` が無効で、PDB にローカルのフォルダーのパス
  （`C:\Users\<ユーザー名>\source\github.repos\SnowStack.EncodingProbe\`）がそのまま入っている。秘密の情報ではない。
  CI（`publish-nuget.yml`）でビルドすれば `/_/` に置き換わる
- 検査したのは手元のファイルで、nuget.org に公開されているファイルとの同一性は確かめていない

## 3. 決定事項（利用者の判断）

- **版を上げるのはクラスライブラリだけ（1.3.1）。** 脆弱性が関わるのはクラスライブラリのビルドの SourceLink だけで、
  モジュールの DLL は影響を受けないため。`SnowStack.EncodingProbe.PowerShell.csproj` と `publish/.../SnowStack.EncodingProbe.PowerShell.psd1` は 1.3.0 のまま
- **Xml / Pkcs は `Microsoft.PowerShell.SDK` を 7.6.4 → 7.6.10 に上げて解消する。** 7.6.10 は Xml / Pkcs 10.0.15 に依存する。
  Xml の直接参照の行は残し、版を 10.0.15 にそろえた
- SourceLink は 10.0 系の最新安定版 10.0.404 にした（アドバイザリの修正版 10.0.303 以降）
- 今回はリリースしない

## 4. 変更したファイル

| ファイル | 変更 |
|---|---|
| `SnowStack.EncodingProbe/SnowStack.EncodingProbe.csproj` | `Version` / `AssemblyVersion` / `FileVersion` を 1.3.1 に。`PackageReleaseNotes` の先頭に 1.3.1 の文面。`Microsoft.SourceLink.GitHub` を 10.0.404 に |
| `tests/EncodingProbe.PowerShell.Tests/EncodingProbe.PowerShell.Tests.csproj` | `Microsoft.PowerShell.SDK` を 7.6.10 に、`System.Security.Cryptography.Xml` を 10.0.15 に |
| `CHANGELOG.md` | 1.3.1 の節（未リリース）を追加 |
| `docs/EncodingProbe-作業状況.md` | 1.3.1 の節を追加 |
| `docs/EncodingProbe-1.3.1-修正記録-脆弱性のあるNuGetパッケージの更新.md` | 本文書 |

ソースコード・テスト・MAML ヘルプ・`publish\`・README は変えていない。

## 5. 確認の結果

- `dotnet list SnowStack.EncodingProbe.slnx package --vulnerable --include-transitive`: 4 プロジェクトとも脆弱なパッケージ無し
- `dotnet build -c Debug --no-incremental`: エラー 0。警告は変更前から `NU1903`（16 件）と `NU1902`（6 件）が消えただけで、ほかのコード解析の警告（CA1416・CS86xx・xUnit など）の件数は同じ
- テスト:
  - コア net48: 2139 件合格
  - コア net10.0: 2129 件合格
  - モジュール（net10.0）: 2038 件合格
- `dotnet pack -c Release`（出力は一時フォルダー。`bin/Release` の既存のパッケージには触れていない）:
  `SnowStack.EncodingProbe.1.3.1.nupkg` / `.snupkg` を作り、2.3 節と同じ検査で SourceLink・nuspec の `repository` に余計なものが無いことを確かめた。
  nuspec の依存は UTF.Unknown（2.7.0 / 2.6.0）だけで、1.3.0 と同じ
- PSCompat（`tests/PSCompat/Invoke-ProbedCompatTests.ps1`）は実行していない。モジュールのコードと、モジュールが実行時に読む依存は変えていないため

## 6. リリースするときに残っている作業

- `CHANGELOG.md` の 1.3.1 の節にリリース日を書く
- README に 1.3.1 の記載を足すかを決める
- CI（`publish-nuget.yml`）で pack すれば、PDB のローカルのパスも `/_/` になる
- **注意:** モジュールをソースからビルドし直すと、同梱するクラスライブラリの DLL は 1.3.1（`AssemblyVersion` 1.3.1.0）になる。
  モジュールを次に出すときは、release-checklist に従って版をそろえること（モジュール 1.3.0 の配布物は今回変えていない）
