using Lex.V3.Contracts;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Api;

/// <summary>
/// EU annexes as the answers disclose them. The corpus classifies each annex of an EU act against the publisher's PDF (Stage 3,
/// domain <c>europe_annex_body</c>) on the member the act's articles are read from, and the EU index copies those outcomes into
/// its member rows. An annex never becomes an article, so no annex text is searched, quoted or exported; dossier and
/// evidence_bundle list the annexes the corpus classified on the members they serve, each served as text not available with the
/// official source a reader can follow.
/// </summary>
internal sealed partial class V3CorpusMount
{
    /// <summary>How every annex listed under <c>annexes_not_served</c> is served: its text never is (the launch contract's word).</summary>
    internal const string EuropeAnnexServedAs = "text_not_available";

    internal const string EuropeAnnexesNotHeldReason =
        "no annex text is held: annexes are not indexed as articles, so no annex is searched, quoted or exported; annexes_not_served " +
        "lists the annexes the corpus classified against the publisher PDF on the members an expression was read from, with the " +
        "official source to read them, and an empty list says only that the corpus classified none there, not that the act has none";

    /// <summary>The fixed reason for each annex disposition of the corpus; anything else is not an annex disposition.</summary>
    internal static string EuropeAnnexReason(LexCorpus6Stage3Disposition disposition) => disposition switch
    {
        LexCorpus6Stage3Disposition.AnnexTextNotAvailable =>
            "every page of the publisher PDF the annex maps to is an image with no text layer: the annex is image-only, so there is no text of it to serve",
        LexCorpus6Stage3Disposition.AnnexMappingUnresolved =>
            "the annex could not be mapped to pages of the publisher PDF, so its body was not classified and its text is not served",
        LexCorpus6Stage3Disposition.AnnexBodyContainsText =>
            "a page of the publisher PDF the annex maps to has a text layer, so the annex is not image-only; annexes are not indexed as articles, so its text is not served here",
        LexCorpus6Stage3Disposition.AnnexBodyContainsNoImage =>
            "a page of the publisher PDF the annex maps to has neither a text layer nor an image, so the annex was not classified as image-only and its text is not served",
        LexCorpus6Stage3Disposition.AnnexMappedPageOutsideDocument =>
            "the annex maps to a page beyond the end of the publisher PDF, so its body was not classified and its text is not served",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, "not an EU annex disposition"),
    };

    /// <summary>
    /// The annexes the corpus classified on the given members, as <see cref="EuropeAnnexRows"/> lists them; a member the corpus
    /// does not hold contributes none.
    /// </summary>
    private object[] EuropeAnnexesNotServed(IEnumerable<string> memberObjectRefs, string officialIdentity, Func<string> officialSource) =>
        EuropeAnnexRows(
            memberObjectRefs.Distinct(StringComparer.Ordinal)
                .Select(EuropeMemberOf)
                .OfType<LexCorpus6Member>()
                .SelectMany(static member => member.Stage3Outcomes),
            officialIdentity,
            officialSource);

    /// <summary>
    /// One row per annex disposition among the outcomes, in the corpus's order: the number of annexes and their semantic
    /// identities, how they are served (never as text), the official identity and source a reader can follow, and the fixed
    /// reason. Outcomes of another domain are not annexes; the official source is read only when a row exists.
    /// </summary>
    internal static object[] EuropeAnnexRows(IEnumerable<LexCorpus6Stage3Outcome> outcomes, string officialIdentity, Func<string> officialSource)
    {
        var annexes = outcomes.Where(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody).ToArray();
        if (annexes.Length == 0)
        {
            return [];
        }

        var source = officialSource();
        return annexes
            .GroupBy(static outcome => outcome.Disposition)
            .OrderBy(static group => group.Key)
            .Select(group =>
            {
                var identities = group.Select(static outcome => outcome.SemanticIdentitySha256)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                return (object)new
                {
                    disposition = ContractWire.NameOf(group.Key),
                    annexes = identities.Length,
                    annex_identities_sha256 = identities,
                    served_as = EuropeAnnexServedAs,
                    official_identity = officialIdentity,
                    official_source = source,
                    reason = EuropeAnnexReason(group.Key),
                };
            })
            .ToArray();
    }

    /// <summary>
    /// The official source of an expression's wording: its articles' official source (the Publications Office address of the
    /// package they were read from), or the work when no article carries one, as the bundle's articles and refusals name it.
    /// </summary>
    private static string EuropeOfficialSourceOf(IEnumerable<EuropeIndexArticleText> articles, string workId) =>
        articles.Select(static article => article.OfficialSourceUri).FirstOrDefault(static uri => uri is not null) ?? workId;
}
