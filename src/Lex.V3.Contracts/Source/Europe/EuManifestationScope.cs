using System.Globalization;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The manifestation formats the publisher offers for Union legal acts and summaries. Closed at nine.
/// </summary>
/// <remarks>
/// <para>
/// Every format named by the verified record for this scope is a member, including the ones we
/// never fetch. A format absent from the vocabulary would mean "not considered" rather than
/// "considered and refused", and those are different statements about our own coverage.
/// </para>
/// <para>
/// AKN4EU is deliberately not here. The record is explicit that no manifestation of any legal act
/// is disseminated by Cellar in that format; the AKN4EU types that do exist belong to schema
/// releases. Listing it would imply we chose not to fetch something the publisher offers.
/// </para>
/// <para>
/// The scope of this vocabulary is legal acts plus the summary class represented in
/// <see cref="EuContentClass"/>. Case-law manifestation formats are a different question and are
/// deliberately outside it: an ECLI is an identifier and a relation target, and admitting one does
/// not mean this profile ingests court text. Case-law formats belong to the later E6 source
/// profile, which covers case-law link metadata rather than case-law bodies.
///
/// So their absence here is a statement about this profile and never about the publisher. Cellar
/// does serve case-law manifestations, including xml, and nothing in this type should be read as
/// denying that. This is the one place where absence from the vocabulary means "another profile
/// answers this" rather than "considered and refused".
/// </para>
/// </remarks>
public enum EuManifestationFormat
{
    [JsonStringEnumMemberName("fmx4")]
    Formex4 = 1,

    [JsonStringEnumMemberName("xhtml")]
    Xhtml = 2,

    /// <summary>
    /// The format summaries of Union legislation are served in, beside Formex.
    /// </summary>
    /// <remarks>
    /// A member because <see cref="EuContentClass.Summary"/> is already represented in this
    /// contract. Keeping the content class while omitting the format it is actually served in is
    /// the same closed-set defect as omitting an act format: the scope would state a reuse basis
    /// for summaries whose manifestations it cannot name.
    /// </remarks>
    [JsonStringEnumMemberName("xhtml5")]
    Xhtml5 = 3,

    [JsonStringEnumMemberName("html")]
    Html = 4,

    [JsonStringEnumMemberName("pdf")]
    Pdf = 5,

    /// <summary>
    /// The archival PDF profile the 2010 to 2016 acts are served in.
    /// </summary>
    /// <remarks>
    /// A distinct member rather than a fold into <see cref="Pdf"/>, <see cref="PdfA1b"/> or
    /// <see cref="PdfA2a"/>. The record names it for 32010L0073, 32012R0648, 32013R0575 and
    /// 32016R0679, so collapsing it would report a format the publisher actually serves as one we
    /// never considered, across the whole middle of the corpus.
    /// </remarks>
    [JsonStringEnumMemberName("pdfa1a")]
    PdfA1a = 6,

    [JsonStringEnumMemberName("pdfa1b")]
    PdfA1b = 7,

    [JsonStringEnumMemberName("pdfa2a")]
    PdfA2a = 8,

    /// <summary>The print manifestation. Offered, never a body source for us.</summary>
    [JsonStringEnumMemberName("print")]
    Print = 9,
}

/// <summary>
/// Whether a format may serve as a body source for this corpus.
/// </summary>
public enum EuFormatBodyAdmission
{
    [JsonStringEnumMemberName("body_admitted")]
    BodyAdmitted = 1,

    /// <summary>Offered by the publisher, and never fetched as body text by us.</summary>
    [JsonStringEnumMemberName("body_not_admitted")]
    BodyNotAdmitted = 2,
}

/// <summary>
/// The classes of Union content whose reuse basis differs. Closed.
/// </summary>
/// <remarks>
/// Separate members rather than one licence for everything. The record states a classed basis:
/// metadata under CC0, consolidations and summaries under CC BY 4.0, and a wider reuse basis in
/// Commission Decision 2011/833/EU. No per-manifestation split has been measured on this
/// publisher, and that is evidence only that none is currently known. It is not authority to
/// collapse four content classes into one token, which is the same mistake as reading an
/// unmeasured relation family as an empty one, so an unmeasured basis is recorded as
/// <see cref="EuReuseBasis.Unknown"/> rather than as a blanket licence.
/// </remarks>
public enum EuContentClass
{
    [JsonStringEnumMemberName("metadata")]
    Metadata = 1,

    [JsonStringEnumMemberName("consolidation")]
    Consolidation = 2,

    [JsonStringEnumMemberName("summary")]
    Summary = 3,

    [JsonStringEnumMemberName("original_legal_text")]
    OriginalLegalText = 4,
}

/// <summary>
/// A reuse basis a Union content class may carry. Closed.
/// </summary>
public enum EuReuseBasis
{
    [JsonStringEnumMemberName("cc0")]
    Cc0 = 1,

    [JsonStringEnumMemberName("cc_by_4_0")]
    CcBy40 = 2,

    /// <summary>The general basis in Commission Decision 2011/833/EU and the legal notice.</summary>
    [JsonStringEnumMemberName("decision_2011_833_eu")]
    Decision2011833Eu = 3,

    /// <summary>
    /// No basis has been measured for this class at this granularity.
    /// </summary>
    /// <remarks>
    /// Required, and the reason is the whole ruling. Without this member every class must be given
    /// one of the three real bases, so a caller with no measurement is forced to assert one, and
    /// the type manufactures a blanket licence out of an absence of evidence. That is the same
    /// error as reading an unacquired relation family as an empty one, in a place where being
    /// wrong means republishing text under a licence nobody established.
    /// </remarks>
    [JsonStringEnumMemberName("unknown")]
    Unknown = 4,
}

/// <summary>
/// One content class and the reuse basis established for it, with the evidence.
/// </summary>
/// <remarks>
/// The mapping is supplied and evidenced rather than hard-coded here. This type enforces that
/// every class carries one basis and that the basis is evidenced; which basis belongs to which
/// class is a publisher fact, and a contract that decided it internally would be asserting the
/// answer rather than recording it.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuRightsDisposition
{
    [JsonConstructor]
    public EuRightsDisposition(
        EuContentClass contentClass,
        EuReuseBasis basis,
        SourceArtifactRef evidenceRef)
    {
        ContentClass = ContractValidation.RequireDefined(contentClass, nameof(contentClass));
        Basis = ContractValidation.RequireDefined(basis, nameof(basis));
        // Evidence is required for every basis including Unknown, where it is the observation
        // showing that no split is currently established. "We looked and found none" and "nobody
        // looked" are the same token with different standing.
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuContentClass ContentClass { get; }

    public EuReuseBasis Basis { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// That one language expression of one work carries one format. Positive only.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by work and language together, never by work alone, and this is the whole point of the
/// type. The record is explicit that absence of Formex must be asserted per language and never per
/// work, and gives the counterexample: <c>32004R0139</c> carries no Formex in its English or French
/// expressions and does carry it in Bulgarian, Croatian and Romanian from the 2007 and 2013
/// enlargement special editions. A per-work claim that the act has no Formex is therefore false for
/// three languages while being true for the one somebody checked.
/// </para>
/// <para>
/// So there is no work-level constructor and no convenience property that answers "does this work
/// have this format". Such a member could only be answered by quantifying over expressions this
/// type does not hold, and the answer would be a false absence the first time it met a special
/// edition.
/// </para>
/// <para>
/// It is also positive only, and carries no flag that could say a format is missing. A
/// content-bound observation reference proves which bytes were named. It does not prove that the
/// manifestation enumeration for that expression ran to completion, and only a complete bounded
/// observation can support an absence. That is the rule the relation families already follow, and
/// formats do not get a second, weaker completion mechanism. Minting a negative belongs to the
/// later source-completion validator, working from the shared delivery proof and its independent
/// witness, so this type defers it rather than inventing one of its own.
/// </para>
/// </remarks>
/// <summary>
/// That one Cellar expression carries one format. Positive only, and identity-bound.
/// </summary>
/// <remarks>
/// <para>
/// The expression is a source-object coordinate admitted by <see cref="EuWemiIdentityBoundary"/>,
/// not a coordinate this type checks for itself. An earlier version carried an independently
/// supplied CELEX and proved the expression belonged to it by comparing the parent key to that
/// CELEX, which set an attribute equal to an identity and proved only that somebody had written the
/// same string twice. The work is now reachable where it actually lives, as the expression's parent.
/// </para>
/// <para>
/// There is no work-level constructor and no convenience property answering a format question for a
/// work. <c>32004R0139</c> carries no Formex in its English or French expressions and does carry it
/// in Bulgarian, Croatian and Romanian from the 2007 and 2013 enlargement special editions, so a
/// per-work claim is false for three languages while being true for the one somebody checked.
/// </para>
/// <para>
/// It is positive only and carries no flag that could say a format is missing. A content-bound
/// observation reference proves which bytes were named; it does not prove the manifestation
/// enumeration for that expression ran to completion, and only a complete bounded observation can
/// support an absence. Minting a negative belongs to the later source-completion validator.
/// </para>
/// <para>
/// Deliberately not deserializable. A verified fact cannot be reconstituted from bytes alone,
/// because the boundary that admits its expression carries the registry and identity-profile
/// references of the scope, and a document cannot supply those without asserting them.
/// </para>
/// </remarks>
public sealed record EuExpressionFormatFact
{
    public EuExpressionFormatFact(
        EuWemiIdentityBoundary boundary,
        EuOfficialLanguage language,
        SourceObjectRef expressionRef,
        EuManifestationFormat format,
        SourceArtifactRef observationRef)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        Language = ContractValidation.RequireDefined(language, nameof(language));
        ExpressionRef = boundary.Require(expressionRef, EuWemiRole.Expression, nameof(expressionRef));
        Format = ContractValidation.RequireDefined(format, nameof(format));
        // Required even though the fact is positive: it names the bytes the observation read, and
        // a positive with no observation is an assertion rather than a reading.
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
    }

    public EuOfficialLanguage Language { get; }

    /// <summary>
    /// The Cellar expression this fact was read from, admitted by the shared identity boundary.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ObservationRef"/>, which identifies our own retained bytes rather
    /// than the publisher object. The work this expression belongs to is its parent key, proved by
    /// the boundary rather than restated here.
    /// </remarks>
    public SourceObjectRef ExpressionRef { get; }

    public EuManifestationFormat Format { get; }

    /// <summary>The enumeration this fact was read from, content-bound.</summary>
    public SourceArtifactRef ObservationRef { get; }
}

/// <summary>
/// The Union manifestation and rights scope: which formats exist, which may serve as bodies, when
/// Formex became available, and what reuse basis each content class carries.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuManifestationScope
{
    /// <summary>
    /// The date Formex V4 entered into force, as named by the verified record for this scope.
    /// </summary>
    /// <remarks>
    /// The canonical value only. The boundary an instance actually carries is
    /// <see cref="FormexAvailableFrom"/>, which is serialized, because a constant never reaches the
    /// retained bytes: an artifact carrying only the evidence reference would not state which date
    /// that evidence supports, and editing this constant later would reinterpret old bytes without
    /// changing them or their schema.
    /// </remarks>
    /// <remarks>
    /// Carried so it can be checked and used for partition planning, and deliberately not used to
    /// classify anything. Availability is decided by the per-expression enumeration, because the
    /// boundary tracks the publication event of each language expression rather than the act's
    /// adoption date: consolidations of pre-2004 acts do carry Formex, and enlargement special
    /// editions carry it for languages whose original publication predates it. A boundary used as
    /// a classifier would erase both.
    /// </remarks>
    public const string CanonicalFormexAvailableFrom = "2004-05-01";

    /// <summary>
    /// Formats that can never serve as a body source, whatever a caller supplies.
    /// </summary>
    /// <remarks>
    /// A closed production rule rather than a fixture convention. Print is a physical manifestation,
    /// so no digital body can be read from it under any configuration, and a scope admitting it
    /// would record an impossibility. Leaving this to the caller is the caller-minted-policy defect:
    /// the type would accept a document asserting that we take body text off paper.
    ///
    /// It holds print alone, on purpose. The PDF/A profiles are digital and parseable, so refusing
    /// them here would invent a publisher fact from nothing, which is the same error pointing the
    /// other way. Whether we choose to read a body from one of them is a per-scope judgement, and
    /// that is exactly what <see cref="EuFormatDisposition"/> exists to record.
    /// </remarks>
    public static readonly IReadOnlyList<EuManifestationFormat> FormatsThatCanNeverCarryABody =
        Array.AsReadOnly(new[] { EuManifestationFormat.Print });

    [JsonConstructor]
    public EuManifestationScope(
        IReadOnlyList<EuFormatDisposition> formats,
        IReadOnlyList<EuRightsDisposition> rights,
        string formexAvailableFrom,
        SourceArtifactRef formexBoundaryEvidenceRef)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(rights);
        FormexAvailableFrom = RequireCanonicalBoundary(formexAvailableFrom, nameof(formexAvailableFrom));
        FormexBoundaryEvidenceRef = formexBoundaryEvidenceRef
            ?? throw new ArgumentNullException(nameof(formexBoundaryEvidenceRef));

        Formats = CloseOver(
            formats,
            static disposition => disposition.Format,
            Enum.GetValues<EuManifestationFormat>(),
            nameof(formats));
        Rights = CloseOver(
            rights,
            static disposition => disposition.ContentClass,
            Enum.GetValues<EuContentClass>(),
            nameof(rights));

        foreach (var disposition in Formats)
        {
            if (FormatsThatCanNeverCarryABody.Contains(disposition.Format) &&
                disposition.Admission == EuFormatBodyAdmission.BodyAdmitted)
            {
                throw new ArgumentException(
                    $"{disposition.Format} is a physical manifestation and can never carry a body.",
                    nameof(formats));
            }
        }

        if (!Formats.Any(static disposition =>
                disposition.Admission == EuFormatBodyAdmission.BodyAdmitted))
        {
            throw new ArgumentException(
                "No format is admitted as a body source, so no text could ever be held.",
                nameof(formats));
        }
    }

    public IReadOnlyList<EuFormatDisposition> Formats { get; }

    public IReadOnlyList<EuRightsDisposition> Rights { get; }

    /// <summary>
    /// The expected-availability boundary this scope carries, bound to its own evidence.
    /// </summary>
    /// <remarks>
    /// On the wire, so the retained artifact states which date its evidence supports. It still
    /// classifies nothing. Availability is decided by the per-expression enumeration, because the
    /// boundary tracks the publication event of each language expression rather than the adoption
    /// date of the act. Consolidations of pre-2004 acts do carry Formex, and enlargement special
    /// editions carry it for languages whose original publication predates it, so a boundary used
    /// as a classifier would erase both.
    /// </remarks>
    public string FormexAvailableFrom { get; }

    /// <summary>The observation the Formex boundary was read from.</summary>
    public SourceArtifactRef FormexBoundaryEvidenceRef { get; }

    /// <summary>
    /// Require the one boundary the record supports, not merely a well-formed date.
    /// </summary>
    /// <remarks>
    /// The shape check alone would accept 2004-05-02 or 1999-01-01 beside the same evidence
    /// reference and serialize it. A syntactically valid false boundary is worse than a missing
    /// one, because it looks verified on the wire and nothing downstream can tell it apart from a
    /// checked value. The parameter still exists so the value travels in the retained bytes and
    /// round-trips; the equality guard is what makes those bytes worth reading.
    ///
    /// If the publisher record ever names a different date, that is a new profile and schema
    /// ruling, not arbitrary input to this one.
    /// </remarks>
    private static string RequireCanonicalBoundary(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DateOnly.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException(
                "A boundary date must be an exact yyyy-MM-dd calendar date.", parameterName);
        }

        if (!string.Equals(value, CanonicalFormexAvailableFrom, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The Formex boundary is {CanonicalFormexAvailableFrom}; a different but well-formed date looks verified on the wire and is not.",
                parameterName);
        }

        return value;
    }

    /// <summary>
    /// Materialise once, then require exactly one disposition per member of the closed set.
    /// </summary>
    /// <remarks>
    /// Shared by both axes because the rule is the same one twice: an omitted member is
    /// indistinguishable from a refused one, and a member with two dispositions has none. Written
    /// once rather than twice so the two cannot drift.
    ///
    /// The result is ordered by the closed key for the same reason both axes share this method:
    /// the retained profile must be deterministic, and caller order is not part of the content.
    /// </remarks>
    private static IReadOnlyList<T> CloseOver<T, TKey>(
        IReadOnlyList<T> supplied,
        Func<T, TKey> keyOf,
        TKey[] closedSet,
        string parameterName)
        where T : class
        where TKey : struct, Enum
    {
        var snapshot = supplied.ToArray();
        var seen = new HashSet<TKey>();
        foreach (var item in snapshot)
        {
            if (item is null)
            {
                throw new ArgumentException("A disposition cannot be null.", parameterName);
            }

            if (!seen.Add(keyOf(item)))
            {
                throw new ArgumentException(
                    $"{keyOf(item)} carries more than one disposition; a member with two answers has none.",
                    parameterName);
            }
        }

        foreach (var member in closedSet)
        {
            if (!seen.Contains(member))
            {
                throw new ArgumentException(
                    $"{member} carries no disposition; an unmentioned member is indistinguishable from a refused one.",
                    parameterName);
            }
        }

        // Sorted by the closed key, so the retained bytes are a property of the content rather
        // than of the order a caller happened to build the list in. These are set-like maps with no
        // semantic order, and ContractJson emits list order while the canonicaliser preserves
        // arrays, so without this two scopes with identical content digest differently.
        return Array.AsReadOnly(snapshot.OrderBy(keyOf).ToArray());
    }
}

/// <summary>
/// One offered format and whether it may serve as a body source.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuFormatDisposition
{
    [JsonConstructor]
    public EuFormatDisposition(
        EuManifestationFormat format,
        EuFormatBodyAdmission admission,
        string reasonCode,
        SourceArtifactRef evidenceRef)
    {
        Format = ContractValidation.RequireDefined(format, nameof(format));
        Admission = ContractValidation.RequireDefined(admission, nameof(admission));
        ReasonCode = ContractValidation.RequireIdentifier(reasonCode, nameof(reasonCode));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuManifestationFormat Format { get; }

    public EuFormatBodyAdmission Admission { get; }

    public string ReasonCode { get; }

    public SourceArtifactRef EvidenceRef { get; }
}
