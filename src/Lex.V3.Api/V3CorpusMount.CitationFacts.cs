using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// What a citation of a quoted article needs beyond the states themselves (the plan's item 3.3; the specification of record's
/// R65): the one citation generator, <c>web/scripts/citation.mjs</c>, composes the three forms (conventional, with the dated
/// permalink, and the evidence form) for copy, export and the permalink page, and this platform serves the facts it composes
/// them from, verbatim, and composes no citation string itself (one source for each: the facts here, the forms there). The
/// <c>citation</c> operation is another thing: the references the publisher wrote in a state's text (lane R4), unchanged.
/// <list type="bullet">
/// <item><c>evidence_bundle</c> (Luxembourg) carries <c>citation_facts</c>: the publisher's titles of each listed state's
/// expression, as <c>dossier</c> serves them, and the act as published (<c>{work}/jo</c>) with the publisher's
/// <c>isPartOf</c> facts on it (the issue of the Journal officiel, Mémorial A, it was published in) and its
/// <c>publicationDate</c> facts, each verbatim with its evidence digest. An empty list says only that the index holds no
/// such fact.</item>
/// <item><c>verify</c> at an article's (or an EU provision's) permalink answers <c>article_text_sha256</c>: the SHA-256 of the
/// article's text as <c>evidence_bundle</c> serves it (R64's long citation adds it), so the evidence form is verified by both
/// of its hashes, the version's in the permalink and the article text's; null for an article held without text.</item>
/// </list>
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string CitationFactsRule =
        "what a citation of these states needs that the states do not carry, read verbatim from the index and never composed: titles are the publisher's " +
        "titles of each listed state's expression (its title and short title, each with the digest of the evidence it was read from), as dossier serves " +
        "them; journal_officiel_iri is the act as published, the publisher work IRI followed by /jo; memorial_issues are the publisher's isPartOf facts on it " +
        "(the issue of the Journal officiel, Mémorial A, the act was published in) and publication_dates its publicationDate facts, each verbatim with its " +
        "evidence digest; an empty list says only that the index holds no such fact, never that none exists; no citation is composed here: the site's " +
        "citation generator composes its forms from these facts and the states, and says what is not held";

    /// <summary>
    /// The titles the publisher stated for <paramref name="expressions"/> (one work's), grouped by language and expression,
    /// as <c>dossier</c> and <c>evidence_bundle</c> serve them; only <paramref name="language"/>'s when it is given.
    /// </summary>
    private object[] TitleGroups(IReadOnlyList<string> expressions, string? language) =>
        _reader!.ResolveWorkTitles(expressions)
            .Where(title => language is null || string.Equals(title.Language, language, StringComparison.Ordinal))
            .GroupBy(static title => (title.Language, title.ExpressionIri))
            .Select(static group => (object)new
            {
                language = group.Key.Language,
                expression_iri = group.Key.ExpressionIri,
                titles = group.Where(static title => title.TitleKind == "title")
                    .Select(static title => new { title = title.Title, evidence_sha256 = title.EvidenceSha256 })
                    .ToArray(),
                short_titles = group.Where(static title => title.TitleKind == "title_short")
                    .Select(static title => new { title = title.Title, evidence_sha256 = title.EvidenceSha256 })
                    .ToArray(),
            })
            .ToArray();

    /// <summary>
    /// <c>citation_facts</c> for the states an <c>evidence_bundle</c> answer lists (one work's): their expressions' titles and
    /// the act as published with its Mémorial issue and publication date facts.
    /// </summary>
    private object CitationFactsOf(IReadOnlyList<LuxembourgIndexResolvedState> listed)
    {
        var expressions = listed.Select(static state => state.ExpressionIri)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var journalOfficiel = listed[0].PublisherWorkIri + "/jo";
        var facts = _reader!.ResolveFacts([journalOfficiel]);
        return new
        {
            titles = TitleGroups(expressions, language: null),
            journal_officiel_iri = journalOfficiel,
            memorial_issues = FactsOf(facts, LuxembourgAssertionPredicate.IsPartOf, journalOfficiel),
            publication_dates = FactsOf(facts, LuxembourgAssertionPredicate.PublicationDate, journalOfficiel),
            rule = CitationFactsRule,
        };
    }

    /// <summary>
    /// The SHA-256 of the text of the article <paramref name="publisherId"/> of a Luxembourg state, as <c>evidence_bundle</c>
    /// serves it; null when the state holds that article without text.
    /// </summary>
    private static string? ArticleTextSha256(LuxembourgIndexReader holder, string stateSha256, string publisherId)
    {
        var article = holder.ResolveStateArticles(stateSha256)
            .FirstOrDefault(article => string.Equals(article.PublisherId, publisherId, StringComparison.Ordinal));
        return article is null || article.Text.Length == 0 ? null : TextSha256(article.Text);
    }

    /// <summary>The SHA-256 of the text of the EU provision <paramref name="provision"/> of an expression, as <c>evidence_bundle</c> serves it; null when held without text.</summary>
    private string? EuropeProvisionTextSha256(string expressionId, string provision)
    {
        var article = EuropeArticlesOf(expressionId)
            .FirstOrDefault(article => string.Equals(article.PublisherIdentifier, provision, StringComparison.Ordinal));
        return article is null || article.Text.Length == 0 ? null : TextSha256(article.Text);
    }

    private static string TextSha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
