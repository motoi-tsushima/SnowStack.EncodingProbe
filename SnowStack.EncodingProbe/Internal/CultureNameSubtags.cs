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
    /// <br/>
    /// 言語サブタグの直後の英字 3 文字は拡張言語（extlang）とみなし、それを言語として扱う（1.3.0 第五次の後半）。
    /// RFC 5646 では、拡張言語を持つタグは拡張言語を主言語にしたタグと同じ意味である（<c>zh-yue</c> = <c>yue</c>、<c>zh-cmn</c> = <c>cmn</c>）。
    /// 旧形式の <c>zh-CHT</c> / <c>zh-CHS</c> は用字として先に扱う。
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

            // 拡張言語（言語サブタグの直後の英字 3 文字）は、それを言語として扱う（zh-yue = yue）
            if (i == 1 && part.Length == 3 && IsAsciiLetters(part))
            {
                language = part.ToLowerInvariant();
                continue;
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

    /// <summary>
    /// BCP 47 の形をしているが、言語・用字・地域・変種の並びの規則に合わない古い名前（RFC 5646 の irregular な grandfathered タグ）。
    /// </summary>
    private static readonly string[] IrregularGrandfatheredTags =
    {
        "en-GB-oed", "i-ami", "i-bnn", "i-default", "i-enochian", "i-hak", "i-klingon", "i-lux", "i-mingo",
        "i-navajo", "i-pwn", "i-tao", "i-tay", "i-tsu", "sgn-BE-FR", "sgn-BE-NL", "sgn-CH-DE",
    };

    /// <summary>
    /// カルチャー名として受け付ける形かどうか（1.3.0）
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="System.Globalization.CultureInfo"/> で作れるかどうかでは決めない。.NET Framework（NLS）と .NET Core（ICU）で受け付ける名前が違い、
    /// PowerShell 5.1 と 7.x で <c>-Culture C</c> がエラーになるかどうかが違っていた（BUG-0007。ICU だけが <c>C</c>・<c>root</c>・<c>art-lojban</c> などを、
    /// NLS だけが <c>und-u-ca-gregory</c> を受け付ける）。どちらかの .NET に合わせても一致は保証できないため、名前の形だけで決める。
    /// </para>
    /// <para>
    /// 規則は BCP 47（RFC 5646）の形に従う。区切りは <c>-</c> と <c>_</c>（Windows の並べ替えの名前 <c>de-DE_phoneb</c> など）。大文字小文字は問わない。
    /// <list type="bullet">
    /// <item>言語: 英字 2〜3 文字。続けて拡張言語（英字 3 文字）を 3 つまで。英字 4 文字以上（<c>root</c>・<c>POSIX</c>）と 1 文字（<c>C</c>）は受け付けない</item>
    /// <item>用字: 英字 4 文字（省略できる）。地域: 英字 2 文字か数字 3 文字（省略できる）</item>
    /// <item>変種: 英数字 5〜8 文字か、数字で始まる 4 文字（いくつでも）</item>
    /// <item>拡張: <c>x</c> 以外の 1 文字の後に、英数字 2〜8 文字を 1 つ以上</item>
    /// <item>私用: <c>x</c> の後に、英数字 1〜8 文字を 1 つ以上（<c>x-test</c> のように名前全体が私用でもよい）</item>
    /// <item><c>i-</c> で始まる古い名前（<c>i-klingon</c> など）と、RFC 5646 の irregular な grandfathered タグ（<c>sgn-BE-FR</c> など）</item>
    /// </list>
    /// 形が正しければ、実在しない名前（<c>xx-YY</c>）も受け付ける（判定ではどの東アジアの言語圏にも当たらない）。
    /// </para>
    /// </remarks>
    /// <param name="cultureName">カルチャー名</param>
    /// <returns>受け付ける形なら true</returns>
    public static bool IsWellFormed(string? cultureName)
    {
        if (string.IsNullOrEmpty(cultureName))
        {
            return false;
        }

        string[] parts = cultureName!.Split('-', '_');

        foreach (string part in parts)
        {
            if (part.Length < 1 || part.Length > 8 || !IsAsciiLettersOrDigits(part))
            {
                return false;
            }
        }

        string normalized = string.Join("-", parts);

        foreach (string tag in IrregularGrandfatheredTags)
        {
            if (string.Equals(tag, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string first = parts[0];

        if (first.Length == 1)
        {
            // 名前全体が私用（x-…）か、i- で始まる古い名前
            if (IsSingleton(first, 'x'))
            {
                return IsPrivateUse(parts, 1);
            }

            if (IsSingleton(first, 'i'))
            {
                return parts.Length >= 2 && AllAtLeast(parts, 1, 2);
            }

            return false;
        }

        if (first.Length > 3 || !IsAsciiLetters(first))
        {
            return false;
        }

        int index = 1;

        // 拡張言語（zh-yue、zh-min-nan など）
        for (int extlang = 0; extlang < 3 && index < parts.Length && parts[index].Length == 3 && IsAsciiLetters(parts[index]); extlang++)
        {
            index++;
        }

        // 用字
        if (index < parts.Length && parts[index].Length == 4 && IsAsciiLetters(parts[index]))
        {
            index++;
        }

        // 地域
        if (index < parts.Length
            && ((parts[index].Length == 2 && IsAsciiLetters(parts[index])) || (parts[index].Length == 3 && IsAsciiDigits(parts[index]))))
        {
            index++;
        }

        // 変種
        while (index < parts.Length
            && (parts[index].Length >= 5 || (parts[index].Length == 4 && parts[index][0] >= '0' && parts[index][0] <= '9')))
        {
            index++;
        }

        // 拡張（x 以外の 1 文字の後に 2〜8 文字が 1 つ以上）
        while (index < parts.Length && parts[index].Length == 1 && !IsSingleton(parts[index], 'x'))
        {
            int start = ++index;

            while (index < parts.Length && parts[index].Length >= 2)
            {
                index++;
            }

            if (index == start)
            {
                return false;
            }
        }

        // 私用
        if (index < parts.Length && IsSingleton(parts[index], 'x'))
        {
            return IsPrivateUse(parts, index + 1);
        }

        return index == parts.Length;
    }

    private static bool IsSingleton(string part, char letter)
        => part.Length == 1 && char.ToLowerInvariant(part[0]) == letter;

    /// <summary>私用（x の後）の部分。1 つ以上あればよい（各要素の長さは呼び出し前に 1〜8 文字と確かめてある）</summary>
    private static bool IsPrivateUse(string[] parts, int start) => start < parts.Length;

    private static bool AllAtLeast(string[] parts, int start, int length)
    {
        for (int i = start; i < parts.Length; i++)
        {
            if (parts[i].Length < length)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAsciiLettersOrDigits(string s)
    {
        foreach (char c in s)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
            {
                return false;
            }
        }
        return true;
    }
}
