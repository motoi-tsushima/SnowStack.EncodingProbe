using System.Management.Automation;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 文字エンコーディング・BOM・改行コードを明示してテキストファイルへ書き込むコマンドレット
/// </summary>
/// <remarks>
/// -Encoding には統一語彙名を指定する。PowerShell 5.1 と 7.x のどちらで実行しても
/// 同じ名前が同じバイト列になる。BOM 方針の定まらない裸の utf8 は、
/// パラメータ束縛の段階で拒否される（ファイルを開く前に失敗するため、書きかけの破損ファイルが残らない）。
/// <br/>
/// -Encoding を省略した場合は書き込み先の既存ファイルから継承する。これは
/// 「上書きしても既存ファイルの性質を壊さない」ことを意図した既定であり、
/// パイプラインの読み取り元から継承したい場合は -EncodingFrom を使う。
/// </remarks>
[Cmdlet(
    VerbsCommon.Set,
    "ProbedContent",
    DefaultParameterSetName = PathParameterSet,
    SupportsShouldProcess = true)]
public sealed class SetProbedContentCommand : ProbedContentWriterCommandBase
{
    /// <inheritdoc/>
    protected override string OperationName => "Set-ProbedContent";

    /// <summary>
    /// 書き込み先を新規作成（既存なら切り詰め）して開く
    /// </summary>
    private protected override ProbedFileWriter CreateWriter(string file, EncodingSpec spec, bool force)
        => ProbedFileWriter.Create(file, spec, force);
}
