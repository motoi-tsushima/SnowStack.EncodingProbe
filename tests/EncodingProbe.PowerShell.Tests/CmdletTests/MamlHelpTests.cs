using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SnowStack.EncodingProbe.PowerShell.Cmdlets;
using Xunit;

namespace EncodingProbe.PowerShell.Tests.CmdletTests;

/// <summary>
/// 言語別の MAML ヘルプが互いにずれていないことを検証するテスト。
/// </summary>
/// <remarks>
/// ヘルプは 5 言語ぶん（と、その複製の zh-HK / zh-MO）の XML を手作業で保守している。片方の言語にだけ
/// パラメーターを足す・訳を入れ忘れるといった食い違いは、実際に
/// その言語の環境で Get-Help を実行するまで表面化しない。
/// そこで「本文以外の構造は全言語で完全に一致する」ことを機械的に固定する。
/// <br/>
/// 判定処理が対象としている言語圏に合わせた 5 言語を持ち、
/// それ以外のカルチャーは en-US にフォールバックさせる。
/// 香港（zh-HK）とマカオ（zh-MO）には、zh-TW と同じ内容を置いている（1.2.0、B 案）。
/// </remarks>
public class MamlHelpTests
{
    /// <summary>MAML ヘルプのファイル名</summary>
    private const string HelpFileName = "SnowStack.EncodingProbe.PowerShell.dll-Help.xml";

    /// <summary>
    /// 用意しているカルチャー別フォルダー。
    /// </summary>
    /// <remarks>
    /// Windows が報告する UI カルチャー名そのものを使う。Get-Help はカルチャーの
    /// 親をたどって探すため、より限定的な名前を置くほうが確実に見つかる。
    /// zh-HK と zh-MO は zh-TW の複製である（B 案。<see cref="HongKongAndMacauFolders"/>）。
    /// zh-MO の親は zh-Hant であり zh-HK フォルダーには届かないため、zh-MO にも置いている。
    /// </remarks>
    public static readonly string[] Cultures = { "en-US", "ja-JP", "ko-KR", "zh-TW", "zh-CN", "zh-HK", "zh-MO" };

    /// <summary>
    /// zh-TW と同じ内容を置いているフォルダー
    /// </summary>
    public static readonly string[] HongKongAndMacauFolders = { "zh-HK", "zh-MO" };

    /// <summary>MAML の名前空間</summary>
    private static readonly XNamespace Maml =
        "http://schemas.microsoft.com/maml/2004/10";

    /// <summary>公開しているコマンドレット</summary>
    private static readonly Type[] CmdletTypes =
    {
        typeof(GetProbedContentCommand),
        typeof(SetProbedContentCommand),
        typeof(AddProbedContentCommand),
        typeof(OutProbedFileCommand),
        typeof(ConvertProbedContentCommand),
        typeof(ConvertToDotNetEncodingCommand),
        typeof(ResolveEncodingCmdlet),
        typeof(GetEncodingProbePlatformInfoCommand),
    };

    /// <summary>
    /// すべての言語フォルダーのヘルプが存在すること。
    /// </summary>
    [Theory]
    [MemberData(nameof(CultureNames))]
    public void HelpFile_Exists(string culture)
    {
        Assert.True(File.Exists(HelpPath(culture)), HelpPath(culture));
    }

    /// <summary>
    /// 本文を伏せた骨格が全言語で完全に一致すること。
    /// </summary>
    /// <remarks>
    /// 要素の構成・属性・出現順・コード例（dev:code）まで一致を要求する。
    /// 訳し分けてよいのは maml:para と maml:title の中身だけである。
    /// </remarks>
    [Theory]
    [MemberData(nameof(CultureNames))]
    public void HelpFile_SkeletonMatchesEnglish(string culture)
    {
        string expected = Skeleton(File.ReadAllText(HelpPath("en-US")));
        string actual = Skeleton(File.ReadAllText(HelpPath(culture)));

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 本文が翻訳されていること（英語の原文がそのまま残っていないこと）。
    /// </summary>
    /// <remarks>
    /// 骨格の一致だけでは、英語をコピーしただけのファイルを見逃す。
    /// コマンドレット名や語彙名は原文のまま残るため、完全一致するもののみを数える。
    /// </remarks>
    [Theory]
    [MemberData(nameof(TranslatedCultureNames))]
    public void HelpFile_ParagraphsAreTranslated(string culture)
    {
        string[] english = Paragraphs(HelpPath("en-US"));
        string[] translated = Paragraphs(HelpPath(culture));

        Assert.Equal(english.Length, translated.Length);

        string[] untranslated = english
            .Where((text, index) => text == translated[index])
            .ToArray();

        Assert.Empty(untranslated);
    }

    /// <summary>
    /// 香港・マカオのヘルプが、台湾（zh-TW）と同じ内容であること（B 案による意図的な同一）。
    /// </summary>
    /// <remarks>
    /// 1.2.0 では香港用の文面を作らず、zh-TW をそのまま複製すると決めた（B 案）。
    /// Microsoft は Windows の zh-HK 言語パックの提供をやめて zh-TW を案内しており、
    /// 香港の繁体字 UI で実際に表示されるのは台湾向けの文面であるため。
    /// <see cref="HelpFile_ParagraphsAreTranslated"/> は英語との比較だけなので、複製であることは検出できない。
    /// ここでバイト列の一致を固定しておく。zh-TW を直して複製を忘れた場合もこのテストが落ちる。
    /// 将来、香港用の文面を持つとこのテストは落ちる。そのときは B 案を見直す判断の機会として扱うこと。
    /// </remarks>
    [Theory]
    [MemberData(nameof(HongKongAndMacauFolderNames))]
    public void HelpFile_HongKongAndMacau_AreIntentionallySameAsTaiwan_PlanB(string culture)
    {
        byte[] taiwan = File.ReadAllBytes(HelpPath("zh-TW"));
        byte[] copy = File.ReadAllBytes(HelpPath(culture));

        Assert.Equal(taiwan, copy);
    }

    public static IEnumerable<object[]> HongKongAndMacauFolderNames()
        => HongKongAndMacauFolders.Select(culture => new object[] { culture });

    /// <summary>
    /// すべてのコマンドレットが、全言語のヘルプに記載されていること。
    /// </summary>
    [Theory]
    [MemberData(nameof(CultureNames))]
    public void HelpFile_DocumentsEveryCmdlet(string culture)
    {
        string[] documented = CommandNames(HelpPath(culture));

        foreach (Type type in CmdletTypes)
        {
            Assert.Contains(CmdletName(type), documented);
        }

        Assert.Equal(CmdletTypes.Length, documented.Length);
    }

    /// <summary>
    /// コマンドレットが公開しているパラメーターが、全言語のヘルプに記載されていること。
    /// </summary>
    /// <remarks>
    /// -Culture / -Strategy を追加したときのような、実装だけ直してヘルプを
    /// 忘れる食い違いを検出する。共通パラメーターと ShouldProcess のものは
    /// PowerShell が用意するため、コマンドレット側の宣言には現れない。
    /// </remarks>
    [Theory]
    [MemberData(nameof(CultureNames))]
    public void HelpFile_DocumentsEveryParameter(string culture)
    {
        Dictionary<string, string[]> documented = DocumentedParameters(HelpPath(culture));

        foreach (Type type in CmdletTypes)
        {
            string[] declared = type
                .GetProperties()
                .Where(property => property
                    .GetCustomAttributes(typeof(System.Management.Automation.ParameterAttribute), true)
                    .Any())
                .Select(property => property.Name)
                .ToArray();

            foreach (string name in declared)
            {
                Assert.Contains(name, documented[CmdletName(type)]);
            }
        }
    }

    public static IEnumerable<object[]> CultureNames()
        => Cultures.Select(culture => new object[] { culture });

    public static IEnumerable<object[]> TranslatedCultureNames()
        => Cultures.Where(culture => culture != "en-US").Select(culture => new object[] { culture });

    private static string HelpPath(string culture)
        => Path.Combine(AppContext.BaseDirectory, culture, HelpFileName);

    /// <summary>
    /// コマンドレットの動詞・名詞から PowerShell 上のコマンド名を組み立てる
    /// </summary>
    private static string CmdletName(Type type)
    {
        var attribute = (System.Management.Automation.CmdletAttribute)type
            .GetCustomAttributes(typeof(System.Management.Automation.CmdletAttribute), false)
            .Single();

        return attribute.VerbName + "-" + attribute.NounName;
    }

    /// <summary>
    /// 本文を伏せた骨格を求める
    /// </summary>
    private static string Skeleton(string xml)
    {
        xml = Regex.Replace(xml, @"<maml:para>.*?</maml:para>", "<maml:para/>", RegexOptions.Singleline);

        return Regex.Replace(xml, @"<maml:title>.*?</maml:title>", "<maml:title/>", RegexOptions.Singleline);
    }

    /// <summary>
    /// 本文（maml:para）を出現順に取り出す
    /// </summary>
    private static string[] Paragraphs(string path)
        => XDocument.Load(path)
            .Descendants(Maml + "para")
            .Select(element => element.Value)
            .ToArray();

    /// <summary>
    /// ヘルプに記載されているコマンド名を取り出す
    /// </summary>
    private static string[] CommandNames(string path)
        => CommandElements(path)
            .Select(command => command
                .Element(Command + "details")!
                .Element(Command + "name")!
                .Value)
            .ToArray();

    /// <summary>
    /// コマンドごとに、詳細ブロックへ記載されているパラメーター名を取り出す
    /// </summary>
    private static Dictionary<string, string[]> DocumentedParameters(string path)
        => CommandElements(path).ToDictionary(
            command => command.Element(Command + "details")!.Element(Command + "name")!.Value,
            command => command
                .Element(Command + "parameters")
                ?.Elements(Command + "parameter")
                .Select(parameter => parameter.Element(Maml + "name")!.Value)
                .ToArray() ?? Array.Empty<string>());

    private static IEnumerable<XElement> CommandElements(string path)
        => XDocument.Load(path).Root!.Elements(Command + "command");

    /// <summary>command 名前空間</summary>
    private static readonly XNamespace Command =
        "http://schemas.microsoft.com/maml/dev/command/2004/10";
}
