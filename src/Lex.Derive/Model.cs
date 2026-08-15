using System.Security.Cryptography;
using System.Text;

namespace Lex.Derive;

public sealed record Citation(string? Href, string Text);

public sealed record PublisherStructuralEmptyArticle(string Anchor, string WId);

public sealed record Provision(
    string Anchor,
    string? Eli,
    string Type,
    string? Num,
    string? Heading,
    IReadOnlyList<string> Path,
    string? ArticleValidFrom,
    string TextMd,
    string TextSha256,
    int MdStart,
    int MdEnd,
    IReadOnlyList<Citation> Citations);

public sealed record Extraction(
    IReadOnlyList<Provision> Provisions,
    string Markdown,
    IReadOnlyList<string> Notes,
    IReadOnlyList<PublisherStructuralEmptyArticle>? PublisherStructuralEmptyArticles = null);

internal static class MdUtil
{
    public static string CollapseWs(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWs = true;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWs) sb.Append(' ');
                lastWs = true;
            }
            else { sb.Append(ch); lastWs = false; }
        }
        return sb.ToString().TrimEnd();
    }

    public static string Slug(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        }
        var slug = sb.ToString();
        while (slug.Contains("__")) slug = slug.Replace("__", "_");
        return slug.Trim('_') is { Length: > 0 } t ? t : "item";
    }

    public static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    public static string Sha256Hex(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Convert .NET char-index spans to Unicode codepoint offsets in one pass.</summary>
    public static List<Provision> ToCodepointSpans(string markdown, List<Provision> provisions)
    {
        var map = new int[markdown.Length + 1];
        var cp = 0;
        for (var i = 0; i < markdown.Length; i++)
        {
            map[i] = cp;
            if (!char.IsLowSurrogate(markdown[i])) cp++;
        }
        map[markdown.Length] = cp;
        return provisions
            .Select(p => p with { MdStart = map[p.MdStart], MdEnd = map[p.MdEnd] })
            .ToList();
    }
}
