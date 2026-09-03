using System;
using System.Management.Automation;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// -Encoding に与えられた多形の入力を <see cref="EncodingSpec"/> へ変換する引数変換属性。
    /// </summary>
    /// <remarks>
    /// 変換をパラメータ束縛の段階で行うことで、書き込み系コマンドにおける
    /// 裸の utf8 や utf7 の指定を「ファイルを開く前に」失敗させられる。
    /// 処理の途中で失敗すると書きかけの破損ファイルが残るため、この位置での検証が重要である。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    internal sealed class EncodingSpecTransformationAttribute : ArgumentTransformationAttribute
    {
        /// <summary>
        /// 用途を指定して属性を生成する
        /// </summary>
        /// <param name="usage">読み取り用途か書き込み用途か</param>
        public EncodingSpecTransformationAttribute(EncodingUsage usage)
        {
            this.Usage = usage;
        }

        /// <summary>読み取り用途か書き込み用途か</summary>
        public EncodingUsage Usage { get; }

        /// <summary>
        /// Auto を許容するかどうか。
        /// ファイルを引数に取らない ConvertTo-DotNetEncoding では false を指定する。
        /// </summary>
        public bool AllowAuto { get; set; } = true;

        /// <summary>
        /// 入力を <see cref="EncodingSpec"/> へ変換する
        /// </summary>
        public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
            => EncodingVocabulary.Resolve(inputData, this.Usage, this.AllowAuto);
    }
}