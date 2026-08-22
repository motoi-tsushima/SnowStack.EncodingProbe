using System.Text;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// -Encoding に与えられた多形の入力を正規化した内部表現。
    /// 文字エンコーディング・BOM方針・改行コードの3項を保持する。
    /// </summary>
    /// <remarks>
    /// この型は公開しない。コマンドレットのパラメータは object として受け取り、
    /// <see cref="EncodingSpecTransformationAttribute"/> がこの型へ変換する。
    /// </remarks>
    internal sealed class EncodingSpec
    {
        /// <summary>
        /// 「対象ファイルからの検出に委ねる」ことを表すマーカー。
        /// 意味はコマンドごとに定まる（読み取りなら対象を検出、書き込みなら書き込み先から継承）。
        /// </summary>
        public static readonly EncodingSpec Auto = new EncodingSpec(null, null, null, isAuto: true);

        private EncodingSpec(Encoding? encoding, bool? emitBom, LineBreakType? lineBreak, bool isAuto)
        {
            this.Encoding = encoding;
            this.EmitBom = emitBom;
            this.LineBreak = lineBreak;
            this.IsAuto = isAuto;
        }

        /// <summary>
        /// 解決された文字エンコーディング。<see cref="IsAuto"/> が true の場合は null。
        /// Unicode系はBOM方針を反映したコンストラクタで組み立てられている。
        /// </summary>
        public Encoding? Encoding { get; }

        /// <summary>
        /// BOMを出力するかどうか。null は「BOM方針が未指定」であることを表す。
        /// 裸の utf8 と、WebName・数値コードページ経由で解決されたUnicode系がこれに該当し、
        /// 書き込み系コマンドではパラメータ束縛の段階で拒否される。
        /// </summary>
        public bool? EmitBom { get; }

        /// <summary>
        /// 継承すべき改行コード。null は改行情報を持たないことを表す。
        /// EncodingInformation を渡された場合にのみ値が入る。
        /// </summary>
        public LineBreakType? LineBreak { get; }

        /// <summary>対象ファイルからの検出に委ねるかどうか</summary>
        public bool IsAuto { get; }

        /// <summary>
        /// 文字エンコーディングとBOM方針から生成する
        /// </summary>
        public static EncodingSpec Create(Encoding encoding, bool? emitBom, LineBreakType? lineBreak = null)
            => new EncodingSpec(encoding, emitBom, lineBreak, isAuto: false);

        /// <summary>
        /// BOM方針が未指定のまま解決されたかどうか。
        /// 書き込み系コマンドはこれが true の指定を受け付けない。
        /// </summary>
        public bool IsBomPolicyUnspecified => !this.IsAuto && !this.EmitBom.HasValue;

        /// <summary>
        /// 改行情報を差し替えた複製を返す（-EncodingFrom による継承の合成に使う）
        /// </summary>
        public EncodingSpec WithLineBreak(LineBreakType? lineBreak)
            => new EncodingSpec(this.Encoding, this.EmitBom, lineBreak, this.IsAuto);
    }
}