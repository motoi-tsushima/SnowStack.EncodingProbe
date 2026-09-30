using System;
using System.Management.Automation;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// Convert-ProbedContent の -Encoding に与えられた値。解決結果と、利用者が指定した元の値を持つ。
    /// </summary>
    /// <remarks>
    /// Convert-ProbedContent では、-Bom と組み合わせたときの扱いが「どう書かれたか」で変わる
    /// （1.2.0 仕様書 12.3）。BOM 接尾辞付きの名前（utf8BOM）は -Bom と食い違えばエラーになり、
    /// 裸名・WebName（utf8、utf-8）は系統名として扱って BOM を -Bom で決める。
    /// 解決後の <see cref="EncodingSpec"/> だけでは unicode と unicodeBOM を区別できないため、元の値も保持する。
    /// </remarks>
    internal sealed class ConvertEncodingArgument
    {
        public ConvertEncodingArgument(EncodingSpec spec, object? original)
        {
            this.Spec = spec;
            this.Original = original;
        }

        /// <summary>読み取り用途として解決した結果（書き込みの検証は -Bom が分かってから行う）</summary>
        public EncodingSpec Spec { get; }

        /// <summary>利用者が指定した元の値（PSObject は外してある）</summary>
        public object? Original { get; }

        /// <summary>
        /// パラメータに束縛された値から取り出す
        /// </summary>
        public static ConvertEncodingArgument FromBoundParameter(object? value)
        {
            while (value is PSObject psObject && !ReferenceEquals(psObject.BaseObject, value))
            {
                value = psObject.BaseObject;
            }

            if (value is ConvertEncodingArgument argument)
            {
                return argument;
            }

            // 引数変換属性が適用されていれば到達しない。付け忘れを早期に検出するための保険。
            throw new InvalidOperationException(
                $"パラメータが {nameof(ConvertEncodingArgument)} に変換されていません。"
                + $"{nameof(ConvertEncodingTransformationAttribute)} の指定漏れの可能性があります。");
        }
    }

    /// <summary>
    /// Convert-ProbedContent の -Encoding を <see cref="ConvertEncodingArgument"/> へ変換する引数変換属性
    /// </summary>
    /// <remarks>
    /// 解決できない名前・BOM 接尾辞の誤用は、ほかのコマンドと同じくパラメータ束縛の段階で拒否する。
    /// 裸の utf8 や utf7 の拒否（書き込み用途の検証）は、-Bom の有無が分かる BeginProcessing で行う。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    internal sealed class ConvertEncodingTransformationAttribute : ArgumentTransformationAttribute
    {
        public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
        {
            object? value = inputData;

            while (value is PSObject psObject && !ReferenceEquals(psObject.BaseObject, value))
            {
                value = psObject.BaseObject;
            }

            if (value is ConvertEncodingArgument alreadyResolved)
            {
                // パラメータ束縛が複数回走った場合に備えて、解決済みの値はそのまま通す
                return alreadyResolved;
            }

            return new ConvertEncodingArgument(EncodingVocabulary.Resolve(value, EncodingUsage.Read), value);
        }
    }
}
