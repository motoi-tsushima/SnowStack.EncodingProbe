using System;

namespace SnowStack.EncodingProbe;

/// <summary>
/// カルチャー名を言語・用字・地域のサブタグに分解した結果
/// </summary>
/// <remarks>
/// <see cref="System.Globalization.CultureInfo"/> の親チェーンは使わず、カルチャー名の文字列だけから分解する。
/// .NET Framework（NLS）と .NET Core（ICU）とでは、同じカルチャーでも名前や親が異なるためである
/// （たとえば ICU は <c>zh-Hant-HK</c> をその名前のまま保持する）。
///
/// 大文字小文字は区別しない。区切りの <c>_</c> は <c>-</c> と同じに扱う。
/// 旧形式の <c>zh-CHT</c> / <c>zh-CHS</c> は、用字サブタグ <c>Hant</c> / <c>Hans</c> として扱う。
///
/// 文字エンコーディング判定のカルチャーゲート（<see cref="EncodingDetector.ResolveEastAsianLegacyRegion(string)"/>）
/// のほか、PowerShell 層のメッセージの言語選択からも使う。
/// </remarks>
internal readonly struct CultureNameSubtags
{
    /// <summary>言語サブタグ（小文字。例: <c>zh</c>）。無い場合は空文字列</summary>
    public string Language { get; }

    /// <summary>用字サブタグ（先頭だけ大文字。例: <c>Hant</c>）。無い場合は null</summary>
    public string? Script { get; }

    /// <summary>地域サブタグ（大文字。例: <c>HK</c>）。無い場合は null</summary>
    public string? Region { get; }

    private CultureNameSubtags(string language, string? script, string? region)
    {
        Language = language;
        Script = script;
        Region = region;
    }

    /// <summary>
    /// カルチャー名をサブタグに分解する
    /// </summary>
    /// <remarks>
    /// 言語サブタグのあとに現れる 4 文字の英字を用字、2 文字の英字または 3 桁の数字を地域とみなす。
    /// それ以外のサブタグ（<c>zh-TW_radstr</c> の <c>radstr</c> のような並べ替え指定など）は読み飛ばす。
    /// 1 文字のサブタグ（拡張 <c>-u-</c> や私用 <c>-x-</c>）以降は解釈しない。
    /// </remarks>
    /// <param name="cultureName">カルチャー名（例: <c>zh-Hant-HK</c>）。null や空文字列も受け付ける</param>
    /// <returns>分解した結果</returns>
    public static CultureNameSubtags Parse(string? cultureName)
    {
        if (string.IsNullOrEmpty(cultureName))
        {
            return new CultureNameSubtags(string.Empty, null, null);
        }

        string[] parts = cultureName!.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return new CultureNameSubtags(string.Empty, null, null);
        }

        string language = parts[0].ToLowerInvariant();
        string? script = null;
        string? region = null;

        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i];

            // 拡張・私用のサブタグ以降は解釈しない
            if (part.Length == 1)
            {
                break;
            }

            // 旧形式の zh-CHT / zh-CHS
            if (i == 1 && language == "zh")
            {
                if (part.Equals("CHT", StringComparison.OrdinalIgnoreCase))
                {
                    script = "Hant";
                    continue;
                }
                if (part.Equals("CHS", StringComparison.OrdinalIgnoreCase))
                {
                    script = "Hans";
                    continue;
                }
            }

            // 用字サブタグ（地域より前にだけ現れる）
            if (script == null && region == null && part.Length == 4 && IsAsciiLetters(part))
            {
                script = char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
                continue;
            }

            // 地域サブタグ
            if (region == null &&
                ((part.Length == 2 && IsAsciiLetters(part)) || (part.Length == 3 && IsAsciiDigits(part))))
            {
                region = part.ToUpperInvariant();
                continue;
            }
        }

        return new CultureNameSubtags(language, script, region);
    }

    private static bool IsAsciiLetters(string s)
    {
        foreach (char c in s)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAsciiDigits(string s)
    {
        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }
        return true;
    }
}
