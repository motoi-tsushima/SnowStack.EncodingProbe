using System.Management.Automation;
using System.Text;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 文字エンコーディングを保ったままテキストファイルへ追記するコマンドレット
/// </summary>
/// <remarks>
/// -Encoding を省略した場合は追記先の既存ファイルから継承する。追記では
/// これがそのまま自然な挙動になる（追記先の性質を保って追記する）。
/// <br/>
/// BOM の指定は常に無視する。追記でファイルの途中に BOM を書き込むことは、
/// いかなる場合も正しくないためである。-Encoding utf8BOM と utf8NoBOM は同じ結果になる。
/// <br/>
/// 既存ファイルと異なる文字エンコーディングを指定した場合、実際に書き出されるバイト列が
/// 一致するかどうかで可否を決める。名前が違っても、書き出されるバイト列が同じであれば
/// ファイルは一貫したままなので許可する（例: UTF-8 のファイルへ ascii で ASCII 文字だけを追記する）。
/// </remarks>
[Cmdlet(
    VerbsCommon.Add,
    "ProbedContent",
    DefaultParameterSetName = PathParameterSet,
    SupportsShouldProcess = true)]
public sealed class AddProbedContentCommand : ProbedContentWriterCommandBase
{
    /// <summary>
    /// 既存ファイルと異なるバイト列になる追記を許可する。
    /// </summary>
    /// <remarks>
    /// -Force に相乗りさせていない。-Force は「読み取り専用ファイルへ書き込む」という
    /// 別の意味を既に持っており、両者を分けないと
    /// 「読み取り専用属性を外したいだけなのにエンコーディングの検査まで無効になる」事故が起きる。
    /// </remarks>
    [Parameter]
    public SwitchParameter AllowEncodingChange { get; set; }

    /// <inheritdoc/>
    protected override string OperationName => "Add-ProbedContent";

    /// <summary>
    /// 追記内容の検査のために、既存ファイルの文字エンコーディングを必要とする。
    /// </summary>
    /// <remarks>
    /// -Encoding を明示した場合でも、比較の相手として既存側の文字エンコーディングが要る。
    /// -AllowEncodingChange が指定されている場合は比較しないため不要。
    /// </remarks>
    protected override bool RequiresExistingEncoding => !this.AllowEncodingChange.IsPresent;

    /// <summary>
    /// 追記先を開く
    /// </summary>
    private protected override ProbedFileWriter CreateWriter(string file, EncodingSpec spec, bool force)
        => ProbedFileWriter.Append(file, spec, force);

    /// <summary>
    /// 追記しても既存ファイルの一貫性が保たれるかどうかを、実際のバイト列で判定する。
    /// </summary>
    /// <remarks>
    /// 安全性を決めているのはエンコーディング名の一致ではなく、書き出されるバイト列である。
    /// 追記する文字列は手元にあるため、両方の文字エンコーディングで符号化して比較できる。
    /// 一致すれば、指定された文字エンコーディングで追記した結果は
    /// 既存の文字エンコーディングで追記したのとバイト単位で同一になる。
    /// </remarks>
    private protected override string? RejectChunk(
        string file, EncodingSpec spec, Encoding? baseline, string chunk)
    {
        if (baseline == null)
        {
            // 新規作成・空ファイル・-AllowEncodingChange のいずれか。比較する相手がいない。
            return null;
        }

        Encoding specified = spec.Encoding!;

        if (specified.CodePage == baseline.CodePage)
        {
            return null;
        }

        if (BytesAreEqual(specified.GetBytes(chunk), baseline.GetBytes(chunk)))
        {
            return null;
        }

        return ValidationMessages.EncodingChangeOnAppend(file, specified.WebName, baseline.WebName);
    }

    /// <summary>
    /// バイト列が完全に一致するかどうか
    /// </summary>
    private static bool BytesAreEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
