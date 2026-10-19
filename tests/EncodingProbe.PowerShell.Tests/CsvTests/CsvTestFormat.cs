using System.Management.Automation;
using System.Text;

namespace EncodingProbe.PowerShell.Tests.CsvTests;

/// <summary>
/// CSV の結果を、実測（<c>tools/Measure-CsvBehavior.ps1</c>）の報告と同じ形式の文字列にする。
/// </summary>
/// <remarks>
/// 期待値は実測報告の生の結果（<c>tools/CsvBehaviorResults/csv-measure-Core-7.6.7.txt</c>）から写す。
/// 同じ形式にそろえることで、1 文字ずつ比較できる。
/// 測定スクリプトの <c>Esc</c> と <c>Format-Item</c> を C# に移したもの。
/// </remarks>
internal static class CsvTestFormat
{
    /// <summary>測定スクリプトの Esc（制御文字と非 ASCII を \uXXXX で表す）</summary>
    public static string Esc(string? value)
    {
        if (value == null)
        {
            return "<null>";
        }

        var builder = new StringBuilder();

        foreach (char ch in value)
        {
            int code = ch;

            if (ch == '\r')
            {
                builder.Append("\\r");
            }
            else if (ch == '\n')
            {
                builder.Append("\\n");
            }
            else if (ch == '\t')
            {
                builder.Append("\\t");
            }
            else if (code < 0x20 || code > 0x7E)
            {
                builder.Append("\\u").Append(code.ToString("X4"));
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    /// <summary>測定スクリプトの Format-Item（オブジェクト）</summary>
    public static string FormatObject(PSObject item)
    {
        var parts = new List<string>();

        foreach (PSPropertyInfo property in item.Properties)
        {
            object? value = property.Value;
            string text = value == null ? "<null>" : Esc(value.ToString());
            parts.Add(Esc(property.Name) + "=[" + text + "]");
        }

        string type = item.TypeNames.Count > 0 ? item.TypeNames[0] : string.Empty;

        return "OBJ<" + Esc(type) + "> {" + string.Join(" | ", parts) + "}";
    }

    /// <summary>測定スクリプトの Format-Item（文字列）</summary>
    public static string FormatString(string item) => "STR: " + Esc(item);
}
