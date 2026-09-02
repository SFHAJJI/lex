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
/// <para>
/// Separate members rather than one licence for everything. The reviewed EUR-Lex legal notice
/// states a classed basis: metadata under CC0, editorial content, summaries and consolidations
/// under CC BY 4.0, and a default permission for everything else that the notice asserts on its
/// own account while citing Commission Decision 2011/833/EU. Which class carries which is read
/// from that notice by <see cref="EuRightsDisposition.BasisFor"/> and is not a caller's choice.
/// </para>
/// <para>
/// Editorial content is its own member although it shares an answer with summaries and
/// consolidations, on the same principle that keeps two wholesale sectors apart: a shared answer
/// is not a shared identity, and a disposition has to say which of the three it was about.
/// </para>
/// <para>
/// Every member here is a whole-object class. The exception channels are deliberately not members:
/// each is a condition that can hold of part of a document while a class answer holds of the rest,
/// so putting any of them here would force a choice between two facts that are true at once. They
/// live on <see cref="EuRightsExceptionChannel"/> instead.
/// </para>
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

    /// <summary>Union-owned editorial content.</summary>
    [JsonStringEnumMemberName("editorial_content")]
    EditorialContent = 5,
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

    /// <summary>
    /// The default reuse permission the EUR-Lex legal notice asserts, which cites Commission
    /// Decision 2011/833/EU as its basis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named for the notice rather than for the Decision, because the Decision is not the operative
    /// grant here. It was called <c>Decision2011833Eu</c> with the wire token
    /// <c>decision_2011_833_eu</c>, which stated that a Commission instrument grants reuse of
    /// documents the Commission did not adopt. Decision 2011/833/EU Article 1 determines conditions
    /// for reuse of documents held by the Commission, Article 2(1) scopes it to documents produced
    /// by the Commission or on its behalf, and recital 14 excludes documents received from the other
    /// Institutions. An act of the Parliament and the Council is such a document.
    /// </para>
    /// <para>
    /// What this member records is therefore what the notice says, and only that. The notice speaks
    /// to us and cites the Decision; we record the notice's assertion and its citation, and we do
    /// not restate the citation as a grant of our own. Whether original legal text may be served on
    /// this footing is a separate question this type does not answer, because this scope is
    /// evidence and is never publication authority.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("eur_lex_legal_notice_permission")]
    EurLexLegalNoticePermission = 3,

}

/// <summary>
/// One content class and the reuse basis established for it, with the evidence.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is fixed here, read from the reviewed EUR-Lex legal notice by
/// <see cref="EuRightsDisposition.BasisFor"/>, and a record claiming another is refused. It was a
/// supplied argument once, on the reasoning that a publisher fact should be recorded rather than
/// decided internally. That was the wrong shape for this fact: nothing checked the pairing, so
/// metadata could be recorded as CC BY 4.0 and original legal text as CC0, which states a public
/// domain dedication over published law. The notice is the evidence, and reading it in one place
/// is what stops each record restating it differently.
/// </para>
/// <para>
/// What stays per record is the reference to the class-level source. The evidence says which
/// notice this class answer came from; it never establishes anything about an individual object.
/// </para>
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
        var accepted = BasisFor(ContentClass);
        if (Basis != accepted)
        {
            throw new ArgumentException(
                $"{ContentClass} is {accepted} in the reviewed notice, not {Basis}; the reuse " +
                "basis is read from the publisher's notice rather than chosen by whoever writes " +
                "a record.",
                nameof(basis));
        }

        // Required for every class. It names the class-level source the answer was read from,
        // and a class answer with no reference to the notice behind it is an assertion rather
        // than a reading.
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    /// <summary>The reviewed reuse basis for one content class. Total over the closed set.</summary>
    /// <remarks>
    /// <para>
    /// Read from the EUR-Lex legal notice, which cites Commission Decision 2011/833/EU, rather
    /// than chosen by whoever writes a record. Before this existed the basis was a constructor argument
    /// nothing checked, so metadata could be recorded as CC BY 4.0 and original legal text as CC0.
    /// The second is the dangerous direction: it states a public domain dedication over published
    /// law whose actual basis reserves an exception, which is a permission this project would be
    /// inventing on the class where inventing one is least defensible.
    /// </para>
    /// <para>
    /// Every member is written out and no arm returns a plausible default. A switch expression over
    /// an enum cannot be exhaustive to the compiler, because the variable can hold a value no member
    /// names, so an undecided class throws and the closed set is pinned by test.
    /// </para>
    /// </remarks>
    public static EuReuseBasis BasisFor(EuContentClass contentClass) =>
        ContractValidation.RequireDefined(contentClass, nameof(contentClass)) switch
        {
            EuContentClass.Metadata => EuReuseBasis.Cc0,
            EuContentClass.EditorialContent => EuReuseBasis.CcBy40,
            EuContentClass.Summary => EuReuseBasis.CcBy40,
            EuContentClass.Consolidation => EuReuseBasis.CcBy40,
            EuContentClass.OriginalLegalText => EuReuseBasis.EurLexLegalNoticePermission,
            _ => throw new ArgumentOutOfRangeException(
                nameof(contentClass),
                contentClass,
                "This content class has no reviewed reuse basis. A class with no decision behind " +
                "it does not inherit one from a class it resembles."),
        };

    public EuContentClass ContentClass { get; }

    public EuReuseBasis Basis { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// A rights condition that can override or block a class default, per document or per element.
/// </summary>
/// <remarks>
/// <para>
/// Closed, and separate from <see cref="EuContentClass"/> on purpose. One official legal document
/// is Union material and can at the same time carry a third party's work, and the notice reserves
/// special terms for particular documents, International Accounting Standards among them, stated
/// in that document or its Official Journal issue. Both facts hold of parts of an object while the
/// class answer holds of the rest, so a mutually exclusive content class would force a choice
/// between two true things and erase whichever lost.
/// </para>
/// <para>
/// No channel may be inferred from a class. A document does not carry special terms, industrial
/// property or an identifiable individual because of what kind of document it is; it carries them
/// because that document says so.
/// </para>
/// </remarks>
public enum EuRightsExceptionChannel
{
    /// <summary>A third party's material carried inside a Union document.</summary>
    [JsonStringEnumMemberName("third_party_material")]
    ThirdPartyMaterial = 1,

    /// <summary>Terms stated for one particular document or its Official Journal issue.</summary>
    [JsonStringEnumMemberName("document_specific_terms")]
    DocumentSpecificTerms = 2,

    /// <summary>
    /// Material covered by industrial property rights, excluded from the reuse policy and not
    /// licensed.
    /// </summary>
    /// <remarks>
    /// Its own channel rather than a kind of third-party material, and the distinction is the point.
    /// Patents, trademarks, registered designs, logos and names in a Union document are frequently
    /// the Union's own: the emblem, an institutional logo, an agency mark. Recording those as a
    /// third party's material would state that the Union's marks belong to somebody else, which is
    /// false in the same direction as calling a Commission decision the grant for an act the
    /// Commission did not adopt. Official Journal acts carry the emblem and named signatories, and
    /// annexes carry figures and marks, so this is an ordinary condition rather than a rare one.
    /// </remarks>
    [JsonStringEnumMemberName("industrial_property_rights")]
    IndustrialPropertyRights = 3,

    /// <summary>
    /// Content depicting identifiable private individuals, for which additional rights may need
    /// clearing.
    /// </summary>
    /// <remarks>
    /// Not a copyright channel at all. This is a personal-data clearance condition, and filing it
    /// under third-party material would label a data-protection obligation as a licensing one, so a
    /// reader clearing rights would look in the wrong place and a reader reading the record would be
    /// told the wrong kind of thing.
    /// </remarks>
    [JsonStringEnumMemberName("identifiable_private_individuals")]
    IdentifiablePrivateIndividuals = 4,
}

/// <summary>
/// One exception channel, and the class-level evidence that it exists at all.
/// </summary>
/// <remarks>
/// <para>
/// What this records is that the channel exists and requires per-item resolution. What it
/// deliberately cannot record is whether any given document or element is subject to it.
/// </para>
/// <para>
/// That absence is the design. Resolving any channel needs an observation binding a source
/// object, an exact term and value, and the run that saw it, and no acquisition path in this
/// project can produce one yet. A member for "present", "absent" or "resolved" would therefore
/// hold a caller's opinion under a word that promises evidence, which is the same defect as a
/// delivery subject nobody established. On this subject a wrong opinion republishes somebody
/// else's work, so the type refuses to hold one. The declared surface is pinned by test, so a
/// resolution member cannot arrive quietly ahead of the capability that would justify it.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuRightsExceptionDisposition
{
    [JsonConstructor]
    public EuRightsExceptionDisposition(
        EuRightsExceptionChannel channel,
        SourceArtifactRef evidenceRef)
    {
        Channel = ContractValidation.RequireDefined(channel, nameof(channel));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuRightsExceptionChannel Channel { get; }

    /// <summary>The class-level notice evidence that this channel exists.</summary>
    /// <remarks>
    /// Class-level, and that is all it is. It establishes that the exception can occur, never that
    /// it does or does not occur in any particular document.
    /// </remarks>
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
/// <remarks>
/// <para>
/// What this inventory is, and the four things it is not. It is a closed class-policy inventory
/// and a constraint on what later rights evidence must resolve. It is not itself a
/// reuse-conditions artifact, publication authority, clearance result, or notice.
/// </para>
/// <para>
/// It may guide which condition must be sought, but it cannot generate, satisfy, or substitute for
/// <c>CORE-06</c>, <c>OPS-EU-AUTHORITY</c>, <c>PUB-06</c>, or <c>OBS-07</c>. Later artifacts may
/// bind the same official notice evidence, but their authority is independently acquired,
/// reviewed, signed, unexpired, use-specific, and generation-bound.
/// </para>
/// <para>
/// Public projection remains <c>not_assessed_by_lex-license-policy/1</c> unless that separate
/// authority chain passes. A scope value is never authority.
/// </para>
/// </remarks>
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
        IReadOnlyList<EuRightsExceptionDisposition> exceptions,
        string formexAvailableFrom,
        SourceArtifactRef formexBoundaryEvidenceRef)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(rights);
        ArgumentNullException.ThrowIfNull(exceptions);
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
        // Closed the same way the other members are. A scope that could omit these would be
        // calling itself the Union rights scope while saying nothing about the conditions that can
        // override a class answer, which is the omission a reader is least able to notice.
        Exceptions = CloseOver(
            exceptions,
            static disposition => disposition.Channel,
            Enum.GetValues<EuRightsExceptionChannel>(),
            nameof(exceptions));

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
    /// The conditions that can override a class answer, each with the class-level evidence
    /// that the condition exists.
    /// </summary>
    /// <remarks>
    /// Complete and closed, and deliberately silent about any individual object. Holding every
    /// channel is what makes the class answers readable without being read as whole-object
    /// permissions: the scope states its policy and states, in the same breath, each way that
    /// policy does not reach. None carries a resolution, because nothing can derive one yet.
    /// </remarks>
    public IReadOnlyList<EuRightsExceptionDisposition> Exceptions { get; }

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
