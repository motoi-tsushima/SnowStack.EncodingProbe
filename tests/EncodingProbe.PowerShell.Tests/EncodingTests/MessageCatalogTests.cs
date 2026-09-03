using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using SnowStack.EncodingProbe.PowerShell.Internal;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.EncodingTests;

/// <summary>
/// メッセージのローカライズ（MessageCatalog）を検証するテスト。
/// </summary>
/// <remarks>
/// 対応言語は EncodingProbe の独自判定処理が対象とする言語圏に合わせた5言語であり、
/// 未対応の言語は英語にフォールバックする。
/// </remarks>
public class MessageCatalogTests
{
    /// <summary>すべてのメッセージ識別子</summary>
    private static readonly MessageKey[] AllKeys =
        (MessageKey[])Enum.GetValues(typeof(MessageKey));

    /// <summary>書式指定子（{0} 等）を抽出する正規表現</summary>
    private static readonly Regex PlaceholderPattern = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

    #region 言語の決定（カルチャーからのマッピング）

    /// <summary>
    /// カルチャーが正しい言語に解決されること。
    /// 中国語は簡体字と繁体字を地域・スクリプトから区別する。
    /// </summary>
    [Theory]
    [InlineData("ja-JP", MessageCatalog.Japanese)]
    [InlineData("ja", MessageCatalog.Japanese)]
    [InlineData("ko-KR", MessageCatalog.Korean)]
    [InlineData("ko", MessageCatalog.Korean)]
    [InlineData("zh-TW", MessageCatalog.ChineseTraditional)]
    [InlineData("zh-HK", MessageCatalog.ChineseTraditional)]
    [InlineData("zh-MO", MessageCatalog.ChineseTraditional)]
    [InlineData("zh-Hant", MessageCatalog.ChineseTraditional)]
    [InlineData("zh-CN", MessageCatalog.ChineseSimplified)]
    [InlineData("zh-SG", MessageCatalog.ChineseSimplified)]
    [InlineData("zh-Hans", MessageCatalog.ChineseSimplified)]
    [InlineData("zh", MessageCatalog.ChineseSimplified)]
    [InlineData("en-US", MessageCatalog.English)]
    [InlineData("en-GB", MessageCatalog.English)]
    public void ResolveLanguage_MapsCultureToSupportedLanguage(string cultureName, string expected)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(expected, MessageCatalog.ResolveLanguage(culture));
    }

    /// <summary>
    /// 対応していない言語は英語にフォールバックすること。
    /// </summary>
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("ru-RU")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void ResolveLanguage_UnsupportedLanguage_FallsBackToEnglish(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(MessageCatalog.English, MessageCatalog.ResolveLanguage(culture));
    }

    /// <summary>
    /// インバリアントカルチャーおよび null は英語にフォールバックすること。
    /// </summary>
    [Fact]
    public void ResolveLanguage_InvariantOrNull_FallsBackToEnglish()
    {
        Assert.Equal(MessageCatalog.English, MessageCatalog.ResolveLanguage(CultureInfo.InvariantCulture));
        Assert.Equal(MessageCatalog.English, MessageCatalog.ResolveLanguage(null));
    }

    #endregion

    #region カタログの網羅性と整合性

    /// <summary>
    /// 対応言語が5言語そろっていること。
    /// </summary>
    [Fact]
    public void SupportedLanguages_ContainsFiveLanguages()
    {
        var languages = MessageCatalog.SupportedLanguages.ToList();

        Assert.Equal(5, languages.Count);
        Assert.Contains(MessageCatalog.English, languages);
        Assert.Contains(MessageCatalog.Japanese, languages);
        Assert.Contains(MessageCatalog.Korean, languages);
        Assert.Contains(MessageCatalog.ChineseTraditional, languages);
        Assert.Contains(MessageCatalog.ChineseSimplified, languages);
    }

    /// <summary>
    /// すべてのメッセージが、すべての対応言語で定義されていること。
    /// 未翻訳のまま英語にフォールバックしている状態を検出する。
    /// </summary>
    [Fact]
    public void AllMessages_AreDefinedInEveryLanguage()
    {
        var missing = new List<string>();

        foreach (string language in MessageCatalog.SupportedLanguages)
        {
            foreach (MessageKey key in AllKeys)
            {
                string message = MessageCatalog.Get(key, language);

                if (string.IsNullOrWhiteSpace(message))
                {
                    missing.Add($"{language}/{key}: 空です");
                    continue;
                }

                // 英語以外で英語と同一の文言は、翻訳漏れの可能性が高い
                if (language != MessageCatalog.English
                    && message == MessageCatalog.Get(key, MessageCatalog.English))
                {
                    missing.Add($"{language}/{key}: 英語と同一です");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// すべての言語で書式指定子の集合が英語版と一致すること。
    /// 翻訳時に {0} 等を落とすと、実行時に引数が欠落したメッセージになるため検証する。
    /// </summary>
    [Fact]
    public void AllMessages_HaveConsistentPlaceholdersAcrossLanguages()
    {
        var mismatches = new List<string>();

        foreach (MessageKey key in AllKeys)
        {
            HashSet<string> expected = ExtractPlaceholders(MessageCatalog.Get(key, MessageCatalog.English));

            foreach (string language in MessageCatalog.SupportedLanguages)
            {
                HashSet<string> actual = ExtractPlaceholders(MessageCatalog.Get(key, language));

                if (!expected.SetEquals(actual))
                {
                    mismatches.Add(
                        $"{language}/{key}: 期待 [{string.Join(",", expected.OrderBy(x => x))}] " +
                        $"実際 [{string.Join(",", actual.OrderBy(x => x))}]");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// 未対応の言語コードを直接指定した場合、英語にフォールバックすること。
    /// </summary>
    [Fact]
    public void Get_UnknownLanguage_FallsBackToEnglish()
    {
        foreach (MessageKey key in AllKeys)
        {
            Assert.Equal(MessageCatalog.Get(key, MessageCatalog.English), MessageCatalog.Get(key, "fr"));
        }
    }

    #endregion

    #region 仕様が要求する誘導内容（全言語で保たれること）

    /// <summary>
    /// 裸の utf8 を拒否するメッセージが、全言語で代替候補を含むこと（仕様書 3.6）。
    /// </summary>
    [Fact]
    public void BareUtf8Message_ContainsSuggestionsInEveryLanguage()
    {
        foreach (string language in MessageCatalog.SupportedLanguages)
        {
            string message = MessageCatalog.Get(MessageKey.BareUtf8NotAllowedForWrite, language);

            Assert.Contains("utf8NoBOM", message);
            Assert.Contains("utf8BOM", message);
        }
    }

    /// <summary>
    /// ConvertTo-DotNetEncoding の Auto 拒否メッセージが、全言語で正規形に誘導すること（仕様書 7.5）。
    /// </summary>
    [Fact]
    public void AutoRejectionMessage_ContainsGuidanceInEveryLanguage()
    {
        foreach (string language in MessageCatalog.SupportedLanguages)
        {
            string message = MessageCatalog.Get(MessageKey.AutoNotAllowedForConvert, language);

            Assert.Contains("Resolve-Encoding", message);
            Assert.Contains("ConvertTo-DotNetEncoding", message);
        }
    }

    /// <summary>
    /// BOM接尾辞のエラーメッセージが、全言語で許容される5系統を列挙していること。
    /// </summary>
    [Fact]
    public void BomSuffixMessage_ListsAllowedFamiliesInEveryLanguage()
    {
        foreach (string language in MessageCatalog.SupportedLanguages)
        {
            string message = MessageCatalog.Get(MessageKey.BomSuffixNotAllowed, language);

            Assert.Contains("utf8", message);
            Assert.Contains("unicode", message);
            Assert.Contains("bigendianunicode", message);
            Assert.Contains("utf32", message);
            Assert.Contains("bigendianutf32", message);
        }
    }

    #endregion

    #region 各言語の文字が正しく保持されていること

    /// <summary>
    /// 各言語のメッセージが、その言語固有の文字を保持していること。
    /// ソースファイルの文字エンコーディングが崩れて文字化けした状態を検出する。
    /// </summary>
    [Theory]
    [InlineData(MessageCatalog.Japanese, "文字エンコーディング")]
    [InlineData(MessageCatalog.Korean, "문자 인코딩")]
    [InlineData(MessageCatalog.ChineseTraditional, "無法解析字元編碼")]
    [InlineData(MessageCatalog.ChineseSimplified, "无法解析字符编码")]
    public void Messages_PreserveLanguageSpecificCharacters(string language, string expectedFragment)
    {
        string message = MessageCatalog.Get(MessageKey.UnknownEncoding, language);

        Assert.Contains(expectedFragment, message);
    }

    /// <summary>
    /// 繁体字と簡体字が別の文言として定義されていること。
    /// </summary>
    [Fact]
    public void ChineseVariants_AreDistinct()
    {
        foreach (MessageKey key in AllKeys)
        {
            Assert.NotEqual(
                MessageCatalog.Get(key, MessageCatalog.ChineseTraditional),
                MessageCatalog.Get(key, MessageCatalog.ChineseSimplified));
        }
    }

    #endregion

    #region 実行環境の言語に追随すること

    /// <summary>
    /// 実際にスローされるエラーメッセージが、実行環境の UI カルチャーに追随すること。
    /// </summary>
    [Theory]
    [InlineData("ja-JP", MessageCatalog.Japanese)]
    [InlineData("ko-KR", MessageCatalog.Korean)]
    [InlineData("zh-TW", MessageCatalog.ChineseTraditional)]
    [InlineData("zh-CN", MessageCatalog.ChineseSimplified)]
    [InlineData("en-US", MessageCatalog.English)]
    [InlineData("fr-FR", MessageCatalog.English)]
    public void ThrownMessage_FollowsCurrentUICulture(string cultureName, string expectedLanguage)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            var exception = Assert.Throws<ArgumentTransformationMetadataException>(
                () => EncodingVocabulary.Resolve("utf8", EncodingUsage.Write));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    MessageCatalog.Get(MessageKey.BareUtf8NotAllowedForWrite, expectedLanguage),
                    "utf8"),
                exception.Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    #endregion

    /// <summary>
    /// 書式文字列から書式指定子（{0} 等）の集合を取り出す
    /// </summary>
    private static HashSet<string> ExtractPlaceholders(string format)
    {
        var placeholders = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in PlaceholderPattern.Matches(format))
        {
            placeholders.Add(match.Groups[1].Value);
        }

        return placeholders;
    }
}