using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

/// <summary>
/// The EXACT <c>jolux:userFormat</c> token a selected Legilux manifestation is held under, never a
/// normalised category. RULING lex-event-20260904T174533266Z-bcf05c64ac1b43a3a4f8acf75196a6d5
/// item 4: "the corpus record names the exact userFormat token held, so xml-akomantoso, xml, pdfa
/// and pdf are distinguishable downstream".
/// </summary>
/// <remarks>
/// Four members, not two, because the store uses four distinct authority IRIs under
/// <c>.../resource/authority/user-format/</c> and an earlier reading of "xml first, pdf second"
/// collapsed each pair. Counted over one bounded probe of 1,000 expressions
/// (PROBE_RESULT lex-event-20260904T174227089Z-8f2c03f33d1c4e95b397323c992bbfce, an unordered
/// LIMIT and explicitly not a census): xml-akomantoso 250, xml 131, pdf 586, pdfa 414.
/// <para>
/// Both XML tokens are Akoma Ntoso, which was probed rather than assumed: the plain-xml Civil Code
/// manifestation was fetched and read (PROBE_RESULT
/// lex-event-20260904T180020924Z-ca9982dc058b4d539f2e4a61662af959, 5,531,380 bytes, SHA-256
/// 71695f377e7cef4ab1f0a39361a5992f1772d2fd059d8012801b65d962adf40f) and carries an
/// <c>akomaNtoso</c> root in namespace
/// <c>http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13</c>, the same family as the retained
/// xml-akomantoso instance 9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d. The
/// ruling's conditional -- record plain xml after pdf if it turned out to be another schema -- does
/// not trigger.
/// </para>
/// <para>
/// <c>html</c>, <c>doc</c>, <c>docx</c> and <c>svg</c> are deliberately absent. They are real store
/// tokens and are not wording candidates for this route; <c>docx</c> and <c>svg</c> are in addition
/// disallowed outright by the www host's own robots.txt (<c>Disallow: /*.docx</c>,
/// <c>Disallow: /*.svg</c>).
/// </para>
/// </remarks>
public enum LuxembourgUserFormatToken
{
    [JsonStringEnumMemberName("xml-akomantoso")]
    XmlAkomaNtoso = 1,

    [JsonStringEnumMemberName("xml")]
    Xml = 2,

    [JsonStringEnumMemberName("pdfa")]
    PdfA = 3,

    [JsonStringEnumMemberName("pdf")]
    Pdf = 4,
}

/// <summary>
/// The publisher's own <c>jolux:legalValue</c> marker, and the typed state for its absence.
/// Decision 58(a)'s "if such a marker exists" resolves to yes: the store carries exactly two
/// values, <c>statut-version/officiel</c> and <c>statut-version/definitif</c>. Most manifestations
/// carry neither.
/// </summary>
/// <remarks>
/// <see cref="Unstated"/> exists because the first census of this property was wrong, and the
/// correction matters more than the number. That census joined on <c>jolux:legalValue</c>, which
/// silently drops every manifestation without one, and was reported as a population: "pdf officiel
/// in all 117,960 cases, xml in all 165, xml-akomantoso in all 36,798". Re-measured with an
/// OPTIONAL so absent rows survive (CORRECTION
/// lex-event-20260904T193852758Z-ffc719a2a5b04b16840900a2d7daf771, read as
/// with-legalValue of total): doc 0 of 4, docx 0 of 98,440, html 4,136 of 98,431, pdf 117,960 of
/// 153,648, pdfa 107,482 of 107,485, xml 165 of 34,569, xml-akomantoso 36,798 of 63,862. So 99.5
/// percent of plain xml and 42 percent of xml-akomantoso carry NO marker: absence is the common
/// case for exactly the wording formats D49 prefers, not an edge.
/// <para>
/// <see cref="Unstated"/> is NEVER read as "not official" (RULING
/// lex-event-20260904T194018108Z-62079c93ce9d405ca1fb326cfea41bd9 item two). It is the absence of
/// a publisher statement, and Decision 58(a)'s disclosure at the answer layer is where that
/// absence is surfaced to a reader, not here.
/// </para>
/// </remarks>
public enum LuxembourgLegalValue
{
    [JsonStringEnumMemberName("officiel")]
    Officiel = 1,

    [JsonStringEnumMemberName("definitif")]
    Definitif = 2,

    /// <summary>The publisher states no legal value for this manifestation.</summary>
    [JsonStringEnumMemberName("unstated")]
    Unstated = 3,
}

public enum LuxembourgManifestationSelectionOutcome
{
    [JsonStringEnumMemberName("selected")]
    Selected = 1,

    /// <summary>
    /// Reachable ONLY when the store lists no admitted token for the expression. It used to be
    /// reachable for a second, wrong reason: a manifestation without a legalValue was dropped from
    /// candidacy, so an expression whose files were all unmarked reported an absence that was not
    /// one. An unmarked manifestation is now a candidate carrying
    /// <see cref="LuxembourgLegalValue.Unstated"/>, so this outcome means what its name says.
    /// </summary>
    [JsonStringEnumMemberName("no_manifestation_available")]
    NoManifestationAvailable = 2,
}

/// <summary>
/// One candidate manifestation offered for selection: its exact userFormat token, its publisher
/// legal-value marker, and its already-validated store file URI. Taking the validated
/// <see cref="LuxembourgFileUri"/> rather than a raw string means a caller cannot route an
/// unvalidated candidate into selection: validation happens once, at
/// <see cref="LuxembourgFileUri.RequireValid"/>, before a candidate can be constructed.
/// </summary>
public sealed record LuxembourgManifestationCandidate
{
    public LuxembourgManifestationCandidate(
        LuxembourgUserFormatToken token,
        LuxembourgLegalValue legalValue,
        LuxembourgFileUri fileUri)
    {
        if (!Enum.IsDefined(token))
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        if (!Enum.IsDefined(legalValue))
        {
            throw new ArgumentOutOfRangeException(nameof(legalValue));
        }

        Token = token;
        LegalValue = legalValue;
        FileUri = fileUri ?? throw new ArgumentNullException(nameof(fileUri));
    }

    public LuxembourgUserFormatToken Token { get; }

    public LuxembourgLegalValue LegalValue { get; }

    public LuxembourgFileUri FileUri { get; }
}

/// <summary>
/// D1-06c-LU-2 item 2: which single manifestation this route fetches, among the manifestations the
/// publisher's own store lists. Pure decision logic over already-validated candidates.
/// </summary>
/// <remarks>
/// FORMAT ORDER IS PRIMARY and legal value never outranks it (RULING
/// lex-event-20260904T194018108Z-62079c93ce9d405ca1fb326cfea41bd9 item one, amending
/// lex-event-20260904T174533266Z-bcf05c64ac1b43a3a4f8acf75196a6d5):
/// <list type="number">
/// <item>
/// xml-akomantoso, then xml, then pdfa, then pdf. Both XML tokens are wording candidates in that
/// order; pdfa precedes pdf.
/// </item>
/// <item>
/// <c>jolux:legalValue</c> orders only WITHIN one token: an officiel pdf over an unmarked pdf,
/// never an officiel pdf over an unmarked Akoma Ntoso XML.
/// </item>
/// <item>
/// A deterministic tie-break on the store file URI's own ordinal order, so one candidate set has
/// exactly one answer whatever order a caller enumerates it in.
/// </item>
/// </list>
/// <para>
/// THIS FILE PREVIOUSLY RANKED LEGAL VALUE FIRST, and that was a real defect rather than a
/// preference: a legal-value-first ladder selects an officiel PDF over an unmarked Akoma Ntoso XML,
/// which inverts D49 and Decision 58(a) rather than refining them. It was ruled on a census that
/// dropped every manifestation without the property (see
/// <see cref="LuxembourgLegalValue.Unstated"/>). The named act that decides it is loi
/// 2021/09/09/a676/jo/fr, retained at digest
/// f36772f7377f0d30f827a74594165219b42374e4a420fc9f39cd890d059d5efe: its pdfa is marked
/// <c>definitif</c> and its xml-akomantoso, html and docx carry no marker at all, so the old ladder
/// held a 2021 Luxembourg law as a definitif PDF/A instead of its Akoma Ntoso XML. Format-first
/// answers the XML, carried as <see cref="LuxembourgLegalValue.Unstated"/>.
/// </para>
/// <para>
/// ONE ARM OF THIS FUNCTION HAS NO OBSERVED INSTANCE and its test is labelled a shape test: a
/// candidate set holding both <c>pdfa</c> and <c>pdf</c>. A direct query for an expression
/// offering both, admin paths excluded, returned ZERO rows, and a separate 1,000-expression sample
/// contained no such format set either. The pdfa-before-pdf step is kept because the ruling states
/// it and a total order needs it, not because anyone has seen a case it decides. No synthetic
/// response is called observed anywhere.
/// </para>
/// <para>
/// The typed absence is a different matter and is NOT an unobservable edge: see
/// <see cref="LuxembourgManifestationSelectionOutcome.NoManifestationAvailable"/>. It is reachable
/// exactly when the store lists no admitted token for the expression, which is what it now means.
/// </para>
/// </remarks>
public sealed record LuxembourgManifestationSelection
{
    private LuxembourgManifestationSelection(
        LuxembourgManifestationSelectionOutcome outcome,
        LuxembourgManifestationCandidate? selected)
    {
        Outcome = outcome;
        Selected = selected;
    }

    public LuxembourgManifestationSelectionOutcome Outcome { get; }

    /// <summary>
    /// The winning candidate, or null exactly when <see cref="Outcome"/> is the typed absence. Per
    /// the ruling's own words the record names the format it holds, so this carries the whole
    /// candidate -- exact token, legal value and file URI -- never a reduced category.
    /// </summary>
    public LuxembourgManifestationCandidate? Selected { get; }

    /// <summary>The selected exact userFormat token, or null exactly when absent.</summary>
    public LuxembourgUserFormatToken? Token => Selected?.Token;

    /// <summary>The selected manifestation's store file URI, or null exactly when absent.</summary>
    public string? FileUri => Selected?.FileUri.Value.AbsoluteUri;

    /// <summary>
    /// Selects one manifestation among those the publisher's own store lists for one expression.
    /// An empty candidate list is the typed absence, never an exception and never a silent null.
    /// </summary>
    public static LuxembourgManifestationSelection Select(
        IReadOnlyList<LuxembourgManifestationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        LuxembourgManifestationCandidate? best = null;
        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (best is null || Precedes(candidate, best))
            {
                best = candidate;
            }
        }

        return best is null
            ? new LuxembourgManifestationSelection(
                LuxembourgManifestationSelectionOutcome.NoManifestationAvailable,
                null)
            : new LuxembourgManifestationSelection(
                LuxembourgManifestationSelectionOutcome.Selected,
                best);
    }

    /// <summary>
    /// Total order over candidates: token, THEN legal value, then the store file URI. Total rather
    /// than merely "better than", so <see cref="Select"/> never depends on enumeration order.
    /// </summary>
    private static bool Precedes(
        LuxembourgManifestationCandidate left,
        LuxembourgManifestationCandidate right)
    {
        if (left.Token != right.Token)
        {
            return TokenRank(left.Token) < TokenRank(right.Token);
        }

        if (left.LegalValue != right.LegalValue)
        {
            return LegalValueRank(left.LegalValue) < LegalValueRank(right.LegalValue);
        }

        return string.CompareOrdinal(
            left.FileUri.Value.AbsoluteUri,
            right.FileUri.Value.AbsoluteUri) < 0;
    }

    /// <summary>
    /// Ranks legal value WITHIN one token only. <see cref="LuxembourgLegalValue.Officiel"/> is the
    /// publisher's own positive marker and leads.
    /// </summary>
    /// <remarks>
    /// The step from <see cref="LuxembourgLegalValue.Definitif"/> to
    /// <see cref="LuxembourgLegalValue.Unstated"/> is a JUDGEMENT NO RULING SETTLED and no
    /// observation grounds: it prefers a manifestation the publisher characterised over one it did
    /// not, which is a statement about evidence rather than about officialness. It cannot amount to
    /// reading absence as "not official", because it only ever compares two files of the SAME
    /// format, and the ruling's own worked case (an unmarked xml-akomantoso beating a definitif
    /// pdfa) is decided by token order before this method is reached. Recorded rather than buried,
    /// because this is the third judgement about this property today and the first two were wrong.
    /// </remarks>
    private static int LegalValueRank(LuxembourgLegalValue value) => value switch
    {
        LuxembourgLegalValue.Officiel => 0,
        LuxembourgLegalValue.Definitif => 1,
        LuxembourgLegalValue.Unstated => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static int TokenRank(LuxembourgUserFormatToken token) => token switch
    {
        LuxembourgUserFormatToken.XmlAkomaNtoso => 0,
        LuxembourgUserFormatToken.Xml => 1,
        LuxembourgUserFormatToken.PdfA => 2,
        LuxembourgUserFormatToken.Pdf => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };
}
