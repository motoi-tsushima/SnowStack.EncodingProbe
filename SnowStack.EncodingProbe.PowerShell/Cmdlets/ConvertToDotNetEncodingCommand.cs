using System.Management.Automation;
using SnowStack.EncodingProbe.PowerShell.Internal;

namespace SnowStack.EncodingProbe.PowerShell.Cmdlets;

/// <summary>
/// 統一語彙・WebName・コードページ数値・判定結果などを
/// System.Text.Encoding インスタンスに変換するコマンドレット
/// </summary>
/// <remarks>
/// [IO.File]::ReadAllLines / WriteAllText、StreamWriter、XmlWriter、
/// サードパーティ製ライブラリなど、.NET クラスライブラリを直接利用する場合のための
/// 特殊なコマンドである。通常のファイル操作には Probed 系コマンドを使用する。
/// <br/>
/// 返されるインスタンスは BOM 方針を反映しており、Unicode 系はコンストラクタで
/// 組み立てられている。ただし読み取り系の .NET API は GetPreamble() を参照せず、
/// BOM があれば自動で読み飛ばすため、BOM 方針が意味を持つのは
/// WriteAllText / StreamWriter などの書き込み側のみである。
/// </remarks>
[Cmdlet(VerbsData.ConvertTo, "DotNetEncoding")]
[OutputType(typeof(System.Text.Encoding))]
public sealed class ConvertToDotNetEncodingCommand : PSCmdlet
{
    /// <summary>
    /// 変換元。統一語彙名 / WebName / コードページ数値 / EncodingInformation /
    /// System.Text.Encoding インスタンスを受け付ける。
    /// </summary>
    /// <remarks>
    /// Auto はファイルからの検出を指す語彙であり、ファイルを引数に取らない
    /// 本コマンドでは解決できないためエラーとする。
    /// </remarks>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [EncodingSpecTransformation(EncodingUsage.Read, AllowAuto = false)]
    public object Encoding { get; set; } = default!;

    /// <summary>
    /// 変換結果をパイプラインに出力する
    /// </summary>
    protected override void ProcessRecord()
    {
        EncodingSpec spec = EncodingSpec.FromBoundParameter(this.Encoding);

        WriteObject(spec.Encoding);
    }
}