using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// What the reviewed scope does with a class it has accounted for but does not hold.
/// </summary>
/// <remarks>
/// Four answers, closed. The distinction between the first two is not severity: POINT means the
/// class is real and reachable and we direct the reader to the publisher, while NEVER-INGEST means
/// it does not enter this corpus at all. A reader who cannot tell those apart cannot tell a gap we
/// will fill from one we have decided against.
/// </remarks>
public enum EuSelectionPolicy
{
    /// <summary>Accounted for, not held, and the reader is directed to the official publisher.</summary>
    [JsonStringEnumMemberName("point")]
    Point = 1,

    /// <summary>The whole object never enters this corpus.</summary>
    [JsonStringEnumMemberName("never_ingest")]
    NeverIngest = 2,

    /// <summary>
    /// The body never enters this corpus while its metadata and official locator remain accepted.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="NeverIngest"/> because collapsing them would record a partial
    /// exclusion as a whole-object one, and a reader would conclude we hold nothing about a
    /// judgment when we hold its metadata and its link.
    /// </remarks>
    [JsonStringEnumMemberName("never_ingest_body")]
    NeverIngestBody = 3,

    /// <summary>
    /// The relation exists and is retained; it is never walked outward to pull further Works in.
    /// </summary>
    /// <remarks>
    /// A relation-axis answer rather than an object one. The six treaty identities remain directly
    /// selected body candidates, and only the inbound expansion is refused.
    /// </remarks>
    [JsonStringEnumMemberName("never_expand")]
    NeverExpand = 4,
}

/// <summary>
/// Every class the reviewed scope accounts for without holding it, closed.
/// </summary>
/// <remarks>
/// One member per selector identity rather than one per table row. The accepted inventory's last
/// row names three separate things, and a single token for it would lose which of the three a
/// disposition was about, so each is its own identity under one shared policy answer.
/// </remarks>
public enum EuExcludedSelector
{
    /// <summary>Non-LUX sector-7 national implementing measures for the selected directives.</summary>
    [JsonStringEnumMemberName("non_lux_national_implementing")]
    NonLuxNationalImplementing = 1,

    /// <summary>Sector-3 Works outside the reviewed pack and its frozen one-hop closure.</summary>
    [JsonStringEnumMemberName("sector3_outside_reviewed_closure")]
    Sector3OutsideReviewedClosure = 2,

    /// <summary>Other sector-1 treaty versions, and sectors 4, 8, 9, C and E.</summary>
    [JsonStringEnumMemberName("unreviewed_sector_or_treaty_version")]
    UnreviewedSectorOrTreatyVersion = 3,

    /// <summary>Dossier-contained sector-5 bodies, and journal or signature objects.</summary>
    [JsonStringEnumMemberName("dossier_contained_sector5_body")]
    DossierContainedSector5Body = 4,

    /// <summary>Sector 2, wholesale.</summary>
    [JsonStringEnumMemberName("wholesale_sector2")]
    WholesaleSector2 = 5,

    /// <summary>Sector 5, wholesale.</summary>
    [JsonStringEnumMemberName("wholesale_sector5")]
    WholesaleSector5 = 6,

    /// <summary>EU judgment text. Its metadata and official locator remain accepted.</summary>
    [JsonStringEnumMemberName("eu_judgment_text")]
    EuJudgmentText = 7,

    /// <summary>
    /// Internal Cellar Works carrying the exact term <c>cdm:do_not_index "1"^^xsd:boolean</c>.
    /// </summary>
    [JsonStringEnumMemberName("cellar_do_not_index")]
    CellarDoNotIndex = 8,

    /// <summary>Synthetic consolidations.</summary>
    [JsonStringEnumMemberName("synthetic_consolidation")]
    SyntheticConsolidation = 9,

    /// <summary>AKN4EU legal bodies.</summary>
    [JsonStringEnumMemberName("akn4eu_legal_body")]
    Akn4EuLegalBody = 10,

    /// <summary>EUR-Lex portal fallback.</summary>
    [JsonStringEnumMemberName("eurlex_portal_fallback")]
    EurLexPortalFallback = 11,

    /// <summary>Inbound treaty <c>based_on</c> closure expansion.</summary>
    [JsonStringEnumMemberName("inbound_treaty_based_on_expansion")]
    InboundTreatyBasedOnExpansion = 12,
}

/// <summary>
/// One accounted class, its policy, and the rule and evidence behind it.
/// </summary>
/// <remarks>
/// <para>
/// A scope row is legitimately declarative where a delivery subject was not: this records a
/// decision we made, not a fact derived about the publisher. It only stays honest while the
/// admissible pairings live in the type rather than in a caller's argument, which is why the
/// policy for each selector is fixed here and a disposition claiming a different one is refused.
/// </para>
/// <para>
/// The alternative, letting a caller pass any selector with any policy, would make this record a
/// place to write a scope decision rather than a place to read the reviewed one.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuSelectionDisposition
{
    [JsonConstructor]
    public EuSelectionDisposition(
        EuExcludedSelector selector,
        EuSelectionPolicy policy,
        string reasonCode,
        string ruleId,
        SourceArtifactRef evidenceRef)
    {
        Selector = ContractValidation.RequireDefined(selector, nameof(selector));
        ContractValidation.RequireDefined(policy, nameof(policy));

        var accepted = PolicyFor(selector);
        if (policy != accepted)
        {
            throw new ArgumentException(
                $"{selector} is {accepted} in the reviewed inventory, not {policy}; the policy is " +
                "read from the accepted scope rather than chosen here.",
                nameof(policy));
        }

        Policy = policy;
        ReasonCode = ContractValidation.RequireIdentifier(reasonCode, nameof(reasonCode));
        RuleId = ContractValidation.RequireIdentifier(ruleId, nameof(ruleId));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    /// <summary>The reviewed policy for one selector. Total over the closed set.</summary>
    /// <remarks>
    /// A switch with no default arm, so adding a selector without deciding its policy does not
    /// compile rather than defaulting to something plausible.
    /// </remarks>
    public static EuSelectionPolicy PolicyFor(EuExcludedSelector selector) =>
        ContractValidation.RequireDefined(selector, nameof(selector)) switch
        {
            EuExcludedSelector.NonLuxNationalImplementing => EuSelectionPolicy.Point,
            EuExcludedSelector.Sector3OutsideReviewedClosure => EuSelectionPolicy.Point,
            EuExcludedSelector.UnreviewedSectorOrTreatyVersion => EuSelectionPolicy.Point,
            EuExcludedSelector.DossierContainedSector5Body => EuSelectionPolicy.Point,
            EuExcludedSelector.WholesaleSector2 => EuSelectionPolicy.NeverIngest,
            EuExcludedSelector.WholesaleSector5 => EuSelectionPolicy.NeverIngest,
            EuExcludedSelector.EuJudgmentText => EuSelectionPolicy.NeverIngestBody,
            EuExcludedSelector.CellarDoNotIndex => EuSelectionPolicy.NeverIngest,
            EuExcludedSelector.SyntheticConsolidation => EuSelectionPolicy.NeverIngest,
            EuExcludedSelector.Akn4EuLegalBody => EuSelectionPolicy.NeverIngest,
            EuExcludedSelector.EurLexPortalFallback => EuSelectionPolicy.NeverIngest,
            _ => EuSelectionPolicy.NeverExpand,
        };

    public EuExcludedSelector Selector { get; }

    public EuSelectionPolicy Policy { get; }

    public string ReasonCode { get; }

    public string RuleId { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// The exact RDF term that marks a Cellar Work as not to be indexed.
/// </summary>
/// <remarks>
/// The accepted inventory names one term and only one: <c>"1"^^xsd:boolean</c>. Anything else in
/// that position, including an untyped <c>1</c>, a <c>true</c> spelling, or more than one value,
/// is drift in the publisher's own vocabulary rather than an ordinary exclusion, and the two must
/// not share an answer: an exclusion means we decided, and drift means we no longer recognise what
/// we are reading.
/// </remarks>
public static class EuDoNotIndexTerm
{
    /// <summary>The exact datatype IRI the accepted term carries.</summary>
    public const string DatatypeIri = "http://www.w3.org/2001/XMLSchema#boolean";

    /// <summary>The exact lexical form the accepted term carries.</summary>
    public const string Lexical = "1";

    /// <summary>
    /// Whether one observed value is the exact accepted term.
    /// </summary>
    /// <remarks>
    /// Lexical and datatype both compared ordinally. <c>true</c> is a valid xsd:boolean lexical
    /// form and is deliberately not accepted here, because the inventory names the exact term and
    /// a second spelling admitted quietly is a second way to say one thing.
    /// </remarks>
    public static bool IsExactTerm(string lexical, string datatypeIri) =>
        string.Equals(lexical, Lexical, StringComparison.Ordinal) &&
        string.Equals(datatypeIri, DatatypeIri, StringComparison.Ordinal);
}

/// <summary>
/// The acquisition period rule, which is that there is no inclusion bound.
/// </summary>
/// <remarks>
/// <para>
/// There is no document-date floor. Every publisher historical, current and future-dated state is
/// retained, and corrigenda, relations, national implementing measures, summaries, dossiers and
/// events carry no cutoff of their own.
/// </para>
/// <para>
/// Observation history begins with the fresh V3 run. This type deliberately offers no way to
/// express a retroactive observation, because the one thing a period rule can be twisted into is
/// a claim that we watched something before we did.
/// </para>
/// </remarks>
public static class EuAcquisitionPeriod
{
    /// <summary>The open sentinel, which is not a date on which anything happens.</summary>
    public const string OpenSentinel = "9999-12-31";

    /// <summary>Whether a stated end is the open sentinel rather than a real end.</summary>
    public static bool IsOpenEnded(string validTo) =>
        string.Equals(validTo, OpenSentinel, StringComparison.Ordinal);
}

/// <summary>
/// A year window used to partition execution. Never an inclusion filter.
/// </summary>
/// <remarks>
/// Its own type rather than a pair of dates, because the accepted rule is that partition years are
/// execution mechanics and never inclusion filters, and the way that rule is broken is by passing
/// an execution bound where a selection bound is expected. A separate type makes that a compile
/// error instead of a policy question, which is the only form of the rule that survives a reader
/// in a hurry.
/// </remarks>
public sealed record EuPartitionWindow
{
    public EuPartitionWindow(int firstYear, int lastYear)
    {
        if (firstYear < 1 || lastYear < firstYear)
        {
            throw new ArgumentException(
                $"A partition window runs from a year to a later or equal one, not {firstYear} " +
                $"to {lastYear}.",
                nameof(lastYear));
        }

        FirstYear = firstYear;
        LastYear = lastYear;
    }

    public int FirstYear { get; }

    public int LastYear { get; }
}
