using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// CSV の列を決めるために、オブジェクトのプロパティを「拡張」「アダプテッド」の見え方で取り出す。
    /// </summary>
    /// <remarks>
    /// PowerShell の Export-Csv / ConvertTo-Csv（移植元の <c>ExportCsvHelper.BuildPropertyNames</c>）は、
    /// internal の <c>PSObject.GetPropertyCollection(PSMemberViewTypes)</c> と
    /// <c>PSMemberInfoIntegratingCollection&lt;T&gt;</c> でプロパティの見え方を絞っている。
    /// 本モジュールからは使えないため、公開 API で同じ結果を求める（1.3.0 指示書 3.4）。
    /// 移植したコードではないので、<c>Internal/Csv/</c> の外に置く。
    /// <br/>
    /// <see cref="PSObject.Properties"/> は「インスタンスのメンバー → 型テーブル → アダプター → .NET（Base）」の順に、
    /// 同じ名前を先勝ちで統合した一覧である（<c>PSMemberViewTypes.All</c>）。
    /// <list type="bullet">
    /// <item>拡張（Extended）= インスタンスのメンバーと型テーブルのメンバー。種類は NoteProperty / ScriptProperty /
    /// AliasProperty / CodeProperty のいずれかで、アダプターや .NET の見え方からは現れない</item>
    /// <item>Base の見え方が加わるのは、XML・WMI・DataRowView など .NET 以外のアダプターを持つオブジェクトだけで、
    /// 加わるメンバーの種類は Property である。そこで Property の種類のメンバーは、
    /// アダプテッドのメンバーセット（psadapted）にあるものだけを残す</item>
    /// </list>
    /// 一覧の順序は <see cref="PSObject.Properties"/> の順序をそのまま使う。移植元の統合の順序と同じである。
    /// </remarks>
    internal static class CsvPropertyViews
    {
        /// <summary>アダプテッドのメンバーセットの名前（<c>PSObject.AdaptedMemberSetName</c>）</summary>
        private const string AdaptedMemberSetName = "psadapted";

        /// <summary>
        /// 拡張の見え方のプロパティ（移植元の <c>PSMemberViewTypes.Extended</c>）
        /// </summary>
        public static List<PSPropertyInfo> GetExtendedProperties(PSObject source)
        {
            var result = new List<PSPropertyInfo>();

            foreach (PSPropertyInfo property in source.Properties)
            {
                if (IsExtended(property))
                {
                    result.Add(property);
                }
            }

            return result;
        }

        /// <summary>
        /// 拡張とアダプテッドの見え方のプロパティ（移植元の <c>PSMemberViewTypes.Extended | PSMemberViewTypes.Adapted</c>）
        /// </summary>
        public static List<PSPropertyInfo> GetExtendedAndAdaptedProperties(PSObject source)
        {
            var result = new List<PSPropertyInfo>();
            HashSet<string>? adaptedNames = null;

            foreach (PSPropertyInfo property in source.Properties)
            {
                if (IsExtended(property))
                {
                    result.Add(property);
                    continue;
                }

                adaptedNames ??= GetAdaptedPropertyNames(source);

                if (adaptedNames.Contains(property.Name))
                {
                    result.Add(property);
                }
            }

            return result;
        }

        /// <summary>
        /// CSV の列名でプロパティを引く（移植元の <c>mshObject.Properties[propertyName]</c>）。
        /// </summary>
        /// <remarks>
        /// 列名の照合（Export-ProbedCsv -Append）は <see cref="CsvNameComparer"/> で行うので、値を取り出すときも同じ規則で引く
        /// （1.3.0 第六次。BUG-0020）。PSObject のメンバー名の引き方はホストの .NET の OrdinalIgnoreCase なので、
        /// 5.1 では照合が通った列（追記先の ǅ とプロパティの ǆ など）の値を引けず、空で書いていた。
        /// まずホストの規則で引き、見つからなければ <see cref="CsvNameComparer"/> で一覧から探す（7.x では両者が同じなので、結果は移植元と同じ）。
        /// 5.1 でホストの規則で等しい名前は、すべて <see cref="CsvNameComparer"/> でも等しい（実装記録 11 章の測定）。
        /// </remarks>
        public static PSPropertyInfo? FindProperty(PSObject source, string name)
        {
            if (source.Properties[name] is PSPropertyInfo property)
            {
                return property;
            }

            foreach (PSPropertyInfo candidate in source.Properties)
            {
                if (CsvNameComparer.Instance.Equals(candidate.Name, name))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsExtended(PSPropertyInfo property)
        {
            switch (property.MemberType)
            {
                case PSMemberTypes.NoteProperty:
                case PSMemberTypes.ScriptProperty:
                case PSMemberTypes.AliasProperty:
                case PSMemberTypes.CodeProperty:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// アダプテッドの見え方にあるプロパティの名前（メンバー名は大文字小文字を区別しない）
        /// </summary>
        private static HashSet<string> GetAdaptedPropertyNames(PSObject source)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (source.Members[AdaptedMemberSetName] is PSMemberSet adapted)
            {
                foreach (PSPropertyInfo property in adapted.Properties)
                {
                    names.Add(property.Name);
                }
            }

            return names;
        }
    }
}
