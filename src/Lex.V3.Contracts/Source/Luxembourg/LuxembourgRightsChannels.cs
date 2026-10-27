using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public enum LuxembourgRightsChannelDisposition
{
    [JsonStringEnumMemberName("channel_enumeration_unproven")]
    ChannelEnumerationUnproven = 1,

    [JsonStringEnumMemberName("missing_value")]
    MissingValue = 2,

    [JsonStringEnumMemberName("stale")]
    Stale = 3,

    [JsonStringEnumMemberName("evidence_not_independent")]
    EvidenceNotIndependent = 4,

    [JsonStringEnumMemberName("multiple")]
    Multiple = 5,

    [JsonStringEnumMemberName("conflict")]
    Conflict = 6,

    [JsonStringEnumMemberName("agreed_same_run_cc_by")]
    AgreedSameRunCcBy = 7,

    [JsonStringEnumMemberName("non_admitting_licence_scl")]
    NonAdmittingLicenceScl = 8,

    [JsonStringEnumMemberName("typed_quarantine_unruled_licence")]
    TypedQuarantineUnruledLicence = 9,

    /// <summary>
    /// Channel one, the publisher's own SPARQL <c>jolux:license</c> declaration, resolved for this
    /// manifestation and names the admitting licence. Channel two, Decision 21's in-file
    /// declaration, has not run: it reads the licence out of the document itself and so cannot
    /// precede acquisition (RULING lex-event-20260904T201756388Z-897fb21258b14e088f0495121479c9f4).
    /// </summary>
    /// <remarks>
    /// This is a state, NEVER an empty channel dressed as a resolved one. Nothing fabricates a
    /// second evidence reference to reach agreement: <see cref="LuxembourgRightsChannels"/>'s own
    /// disjointness check exists precisely because two channels sharing evidence are one channel
    /// counted twice, and a synthesised second ref would defeat it silently rather than loudly.
    /// The in-file channel is D1-04f, queued first among the residue and needing no re-fetch.
    /// </remarks>
    [JsonStringEnumMemberName("second_channel_pending")]
    SecondChannelPending = 10,

    /// <summary>
    /// This channel read at least one <c>jolux:license</c> assertion for the manifestation whose
    /// object term it could not represent, and holds no licence IRI for it. The channel was
    /// therefore not observed empty: it was observed saying something this reader cannot carry.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="MissingValue"/> on purpose, and that distinction is the whole point
    /// of the member. <see cref="MissingValue"/> states that the publisher's channel was read and
    /// declared no licence, which downstream may treat as a settled negative fact. This states that
    /// the reading is incomplete, which is a gap and never a negative fact. Distinct from
    /// <see cref="TypedQuarantineUnruledLicence"/> too: there the publisher named a licence this
    /// profile has no rule for, and the IRI is on the record; here there is no IRI to record.
    /// </remarks>
    [JsonStringEnumMemberName("typed_quarantine_unrepresentable_licence_shape")]
    TypedQuarantineUnrepresentableLicenceShape = 11,

    /// <summary>
    /// The in-file channel read this manifestation's retained representation and rejected it for a
    /// reason its reader named, so the channel holds no row for it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ChannelEnumerationUnproven"/>, and that is the entire point.
    /// "Unproven" says this channel never established anything about the manifestation. This says
    /// it looked, and refused what it found. Collapsing the two — which is what happened while the
    /// fold discarded every reading whose status was not <c>Observed</c> — loses the typed evidence
    /// the reader had already produced, and reports a channel that ran as one that did not.
    /// The reader's specific status (unsupported representation, malformed XML, manifestation
    /// identity mismatch, invalid licence IRI) stays recoverable from the retained index the
    /// enumeration reference addresses, which preserves every reading including the refused ones.
    /// </remarks>
    [JsonStringEnumMemberName("typed_quarantine_in_file_reading_rejected")]
    TypedQuarantineInFileReadingRejected = 12,

    /// <summary>
    /// Both channels say exactly CC BY 4.0 for the fetched manifestation, the in-file one as in-file reading /2 reads the
    /// file's own block (the <c>scl:JOLUXManifestation</c> whose <c>uriThis</c> is the fetched manifestation) in a
    /// re-derivation from custody, and nothing either channel holds disagrees (resolution /2,
    /// <see cref="LuxembourgRightsChannels.ResolveTwo"/>). The same two-channel agreement as
    /// <see cref="AgreedSameRunCcBy"/>, read by a later rule; whether it admits is the admission policy's question
    /// (<see cref="LuxembourgRightsAdmissionPolicies"/>).
    /// </summary>
    [JsonStringEnumMemberName("agreed_cc_by_rederived")]
    AgreedCcByRederived = 13,

    /// <summary>Single channel: SPARQL says CC BY; the file's only licence statements sit in blocks naming other identities.</summary>
    [JsonStringEnumMemberName("sparql_cc_by_in_file_names_other_identity")]
    SparqlCcByInFileNamesOtherIdentity = 14,

    /// <summary>Single channel: SPARQL says CC BY; the file states no licence anywhere.</summary>
    [JsonStringEnumMemberName("sparql_cc_by_in_file_silent")]
    SparqlCcByInFileSilent = 15,

    /// <summary>Single channel: SPARQL says CC BY; the file's own block names the fetched manifestation and states no licence.</summary>
    [JsonStringEnumMemberName("sparql_cc_by_in_file_own_block_empty")]
    SparqlCcByInFileOwnBlockEmpty = 16,

    /// <summary>Single channel: SPARQL says CC BY; the in-file channel cannot read the representation (a PDF).</summary>
    [JsonStringEnumMemberName("sparql_cc_by_in_file_not_applicable")]
    SparqlCcByInFileNotApplicable = 17,

    /// <summary>The file binds no block to the fetched manifestation, and it names no identity, or one the catalogue does not know.</summary>
    [JsonStringEnumMemberName("typed_quarantine_in_file_identity_unknown")]
    TypedQuarantineInFileIdentityUnknown = 18,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgRightsChannelObservation
{
    [JsonConstructor]
    public LuxembourgRightsChannelObservation(
        string manifestationIri,
        SourceArtifactRef runIdentity,
        SourceArtifactRef evidenceRef,
        IReadOnlyList<string> licenceIris,
        int unrepresentableLicenceAssertions = 0)
    {
        ManifestationIri = LuxembourgSourceValidation.RequireExactResourceIri(
            manifestationIri,
            nameof(manifestationIri));
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
        LicenceIris = LuxembourgSourceValidation.CopyStrings(licenceIris, nameof(licenceIris));
        foreach (var licenceIri in LicenceIris)
        {
            LuxembourgSourceValidation.RequireExactAbsoluteIri(licenceIri, nameof(licenceIris));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(
            unrepresentableLicenceAssertions, nameof(unrepresentableLicenceAssertions));
        UnrepresentableLicenceAssertions = unrepresentableLicenceAssertions;
    }

    public string ManifestationIri { get; }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef EvidenceRef { get; }

    public IReadOnlyList<string> LicenceIris { get; }

    /// <summary>
    /// How many <c>jolux:license</c> assertions this channel read for this manifestation and could
    /// not represent, because the publisher's object term was not an IRI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// E0(b), #409, clauses S2-A05 then S2-A03. These assertions used to be skipped with no typed
    /// evidence and no accounting, and the manifestation then resolved through
    /// <see cref="LuxembourgRightsChannelDisposition.MissingValue"/> to
    /// <c>missing_rights_value</c> / <c>lu_rights_observed_empty_channel</c> — a positive claim
    /// that this channel was observed and carried nothing. "We could not represent what the
    /// publisher said" is not "the publisher said nothing", and publishing the second for the first
    /// is exactly the false absence the clauses forbid.
    /// </para>
    /// <para>
    /// The skip was never careless: the comment beside it reasons at length that a dropped row is
    /// unacceptable because "a dropped row means the IRI vanishes from the record entirely". That
    /// argument was made about an unruled IRI and the non-IRI object fell through the guard above
    /// it, so the one shape nobody had a rule for was the one silently discarded.
    /// </para>
    /// <para>
    /// A count rather than the terms themselves, deliberately. What the resolution needs to know is
    /// that this channel's reading is incomplete and must not be reported as an observed emptiness.
    /// Carrying unparsed lexical forms here would put publisher text of unknown shape onto a record
    /// whose every other field is a validated IRI; the exact terms stay recoverable from the
    /// retained assertion evidence this channel was built from.
    /// </para>
    /// <para>
    /// Zero is the ordinary case and the default, so a caller that never saw such an assertion says
    /// so by construction rather than by omission.
    /// </para>
    /// </remarks>
    public int UnrepresentableLicenceAssertions { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgSparqlRightsChannelObservations
{
    [JsonConstructor]
    public LuxembourgSparqlRightsChannelObservations(
        SourceArtifactRef runIdentity,
        SourceArtifactRef enumerationRef,
        IReadOnlyList<LuxembourgRightsChannelObservation> observations)
    {
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        EnumerationRef = enumerationRef
            ?? throw new ArgumentNullException(nameof(enumerationRef));
        Observations = LuxembourgRightsChannelCollection.CopyCanonical(
            RunIdentity,
            observations,
            nameof(observations));
    }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef EnumerationRef { get; }

    public IReadOnlyList<LuxembourgRightsChannelObservation> Observations { get; }

    internal LuxembourgRightsChannelObservation? Find(string manifestationIri) =>
        Observations.SingleOrDefault(row =>
            string.Equals(row.ManifestationIri, manifestationIri, StringComparison.Ordinal));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgInFileRightsChannelObservations
{
    [JsonConstructor]
    public LuxembourgInFileRightsChannelObservations(
        SourceArtifactRef runIdentity,
        SourceArtifactRef enumerationRef,
        IReadOnlyList<LuxembourgRightsChannelObservation> observations,
        bool acquisitionCompleted = false,
        IReadOnlyList<string>? rejectedManifestationIris = null)
    {
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        EnumerationRef = enumerationRef
            ?? throw new ArgumentNullException(nameof(enumerationRef));
        Observations = LuxembourgRightsChannelCollection.CopyCanonical(
            RunIdentity,
            observations,
            nameof(observations));
        AcquisitionCompleted = acquisitionCompleted;
        RejectedManifestationIris = LuxembourgSourceValidation.CopyStrings(
            rejectedManifestationIris ?? [], nameof(rejectedManifestationIris));
    }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef EnumerationRef { get; }

    public IReadOnlyList<LuxembourgRightsChannelObservation> Observations { get; }

    /// <summary>
    /// This run has finished its acquisition attempts. A missing declaration is then unproven,
    /// not a promise that channel two will run later. Failed readings remain in EnumerationRef.
    /// </summary>
    public bool AcquisitionCompleted { get; }

    /// <summary>
    /// Manifestations this channel read and refused, each for a reason its reader named.
    /// </summary>
    /// <remarks>
    /// The fold that builds this channel keeps only readings whose status is <c>Observed</c>, which
    /// is right — a refused reading has no licence to carry. What was wrong is that it discarded
    /// the refusal too, so a manifestation the reader had examined and rejected became
    /// indistinguishable from one the channel never reached. Naming them here lets the resolution
    /// say "read and refused" instead of "never enumerated", without inventing a row that would
    /// claim a licence reading it does not have.
    /// </remarks>
    public IReadOnlyList<string> RejectedManifestationIris { get; }

    internal LuxembourgRightsChannelObservation? Find(string manifestationIri) =>
        Observations.SingleOrDefault(row =>
            string.Equals(row.ManifestationIri, manifestationIri, StringComparison.Ordinal));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgRightsChannelResolution
{
    internal LuxembourgRightsChannelResolution(
        string selectedManifestationIri,
        SourceArtifactRef boundRunIdentity,
        LuxembourgSparqlRightsChannelObservations sparqlObservations,
        LuxembourgInFileRightsChannelObservations inFileObservations,
        LuxembourgRightsChannelObservation? sparqlObservation,
        LuxembourgRightsChannelObservation? inFileObservation,
        LuxembourgRightsChannelDisposition disposition)
    {
        SelectedManifestationIri = selectedManifestationIri;
        BoundRunIdentity = boundRunIdentity;
        SparqlObservations = sparqlObservations;
        InFileObservations = inFileObservations;
        SparqlObservation = sparqlObservation;
        InFileObservation = inFileObservation;
        Disposition = LuxembourgSourceValidation.RequireDefined(disposition, nameof(disposition));
    }

    public string SelectedManifestationIri { get; }

    public SourceArtifactRef BoundRunIdentity { get; }

    public LuxembourgSparqlRightsChannelObservations SparqlObservations { get; }

    public LuxembourgInFileRightsChannelObservations InFileObservations { get; }

    public LuxembourgRightsChannelObservation? SparqlObservation { get; }

    public LuxembourgRightsChannelObservation? InFileObservation { get; }

    public LuxembourgRightsChannelDisposition Disposition { get; }

    public bool ChannelsAgreeOnAdmittingLicence =>
        Disposition == LuxembourgRightsChannelDisposition.AgreedSameRunCcBy;

    /// <summary>
    /// Channel one alone admits, and channel two is a typed pending state rather than a refusal.
    /// This is what the pre-acquisition body selection reads; it is deliberately NOT
    /// <see cref="ChannelsAgreeOnAdmittingLicence"/>, because the two channels have not agreed and
    /// saying they had would be the fabrication the disjointness rule guards against.
    /// </summary>
    public bool SparqlChannelAdmitsWithSecondChannelPending =>
        Disposition == LuxembourgRightsChannelDisposition.SecondChannelPending;

    public string ReasonCode => ReasonCodeOf(Disposition);

    /// <summary>The reason code of <paramref name="disposition"/>: what a withheld member's gap names.</summary>
    public static string ReasonCodeOf(LuxembourgRightsChannelDisposition disposition) => disposition switch
    {
        LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven =>
            "rights_channel_enumeration_unproven",
        LuxembourgRightsChannelDisposition.MissingValue => "rights_missing_value",
        LuxembourgRightsChannelDisposition.Stale => "rights_stale_run",
        LuxembourgRightsChannelDisposition.EvidenceNotIndependent =>
            "rights_evidence_not_independent",
        LuxembourgRightsChannelDisposition.Multiple => "rights_multiple",
        LuxembourgRightsChannelDisposition.Conflict => "rights_conflict",
        LuxembourgRightsChannelDisposition.AgreedSameRunCcBy =>
            "rights_agreed_same_run_dual_channel_cc_by_4_0",
        LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl =>
            "rights_non_admitting_licence_scl",
        LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence =>
            "rights_typed_quarantine_unruled_licence",
        LuxembourgRightsChannelDisposition.SecondChannelPending =>
            "rights_sparql_channel_admits_second_channel_pending",
        LuxembourgRightsChannelDisposition.TypedQuarantineUnrepresentableLicenceShape =>
            "rights_typed_quarantine_unrepresentable_licence_shape",
        LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected =>
            "rights_typed_quarantine_in_file_reading_rejected",
        LuxembourgRightsChannelDisposition.AgreedCcByRederived =>
            "rights_agreed_dual_channel_cc_by_4_0_rederived",
        LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity =>
            "rights_sparql_cc_by_in_file_names_other_identity",
        LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent => "rights_sparql_cc_by_in_file_silent",
        LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty =>
            "rights_sparql_cc_by_in_file_own_block_empty",
        LuxembourgRightsChannelDisposition.SparqlCcByInFileNotApplicable =>
            "rights_sparql_cc_by_in_file_not_applicable",
        LuxembourgRightsChannelDisposition.TypedQuarantineInFileIdentityUnknown =>
            "rights_typed_quarantine_in_file_identity_unknown",
        _ => throw new InvalidOperationException("Unknown rights-channel disposition."),
    };

    // Resolution /2's own facts are private state read through methods, never properties: a method is invisible to the
    // serializer, so a resolution /1 serializes exactly as it did before reading /2 existed (the acquisition's Gazette and
    // query checkpoints hash body joins and Gazette dispositions that carry one), and no name opens on the closed wire
    // (WireIgnoredMemberTests).
    private LuxembourgInFileRightsReadingTwo? _inFileReadingTwo;
    private string? _resolutionRuleSha256;
    private LuxembourgSparqlLicencePage? _licencePage;

    /// <summary>
    /// The in-file reading /2 this resolution was made from (<see cref="LuxembourgRightsChannels.ResolveTwo"/>); null for a
    /// resolution /1, whose in-file channel is the acquisition's reading /1.
    /// </summary>
    public LuxembourgInFileRightsReadingTwo? InFileReadingTwo() => _inFileReadingTwo;

    /// <summary>
    /// The SHA-256 of the resolution rule this resolution was made under: null for resolution /1 (the acquisition's), and
    /// <see cref="LuxembourgRightsChannels.ResolutionTwoProfileSha256"/> for resolution /2.
    /// </summary>
    public string? ResolutionRuleSha256() => _resolutionRuleSha256;

    internal LuxembourgRightsChannelResolution AsResolutionTwo(LuxembourgInFileRightsReadingTwo reading) =>
        this with { _inFileReadingTwo = reading, _resolutionRuleSha256 = LuxembourgRightsChannels.ResolutionTwoProfileSha256 };

    /// <summary>
    /// The retained SPARQL page that holds the fetched manifestation's licence row, bound to a single-channel resolution /2
    /// (<see cref="WithLicencePage"/>): what its admission cites and discloses. Null otherwise.
    /// </summary>
    public LuxembourgSparqlLicencePage? LicencePage() => _licencePage;

    /// <summary>
    /// This single-channel resolution /2 bound to <paramref name="page"/>, the retained SPARQL page holding the licence row of
    /// the fetched manifestation, whose licences there are exactly the SPARQL channel's. Anything else throws: a two-channel
    /// or a refused disposition cites no page, and a page of another manifestation or other licences never stands for this one.
    /// </summary>
    public LuxembourgRightsChannelResolution WithLicencePage(LuxembourgSparqlLicencePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_resolutionRuleSha256 is null || !IsSingleChannel(Disposition) || _licencePage is not null)
        {
            throw new ArgumentException("Only a single-channel resolution /2 is bound to a licence page, once.", nameof(page));
        }

        if (!string.Equals(page.ManifestationIri, SelectedManifestationIri, StringComparison.Ordinal) ||
            SparqlObservation is not { } sparql || !page.LicenceIris.SequenceEqual(sparql.LicenceIris, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A licence page binds only the manifestation it holds a row for, with exactly the SPARQL channel's licences.",
                nameof(page));
        }

        return this with { _licencePage = page };
    }

    /// <summary>The four single-channel dispositions: the file does not speak for the fetched manifestation.</summary>
    public static bool IsSingleChannel(LuxembourgRightsChannelDisposition disposition) =>
        disposition is LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity
            or LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent
            or LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty
            or LuxembourgRightsChannelDisposition.SparqlCcByInFileNotApplicable;

    /// <summary>
    /// The disclosure code a single-channel admission carries (LU-DATA-DESIGN v2 §1.2, "Disclosure"): what the reader is told
    /// the text rests on. Null for every other disposition.
    /// </summary>
    public static string? DisclosureCodeOf(LuxembourgRightsChannelDisposition disposition) => disposition switch
    {
        LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity => "lu_rights_publisher_catalogue_only_file_names_other_identity",
        LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent => "lu_rights_publisher_catalogue_only_file_silent",
        LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty => "lu_rights_publisher_catalogue_only_own_block_empty",
        LuxembourgRightsChannelDisposition.SparqlCcByInFileNotApplicable => "lu_rights_publisher_catalogue_only_not_applicable",
        _ => null,
    };
}

/// <summary>
/// One retained SPARQL licence page for one manifestation (<c>lex-lu-sparql-licence-pages/1</c>): the page's response body
/// digest, the manifestation's licence values on it, when the acquisition observed it, and whether it observed it live
/// (on or after the resumption a resumed acquisition records; every page of an acquisition that did not resume).
/// </summary>
public sealed record LuxembourgSparqlLicencePage
{
    public LuxembourgSparqlLicencePage(
        string manifestationIri,
        SourceArtifactRef pageRef,
        IReadOnlyList<string> licenceIris,
        DateTimeOffset observedAt,
        bool observedLive)
    {
        ManifestationIri = LuxembourgSourceValidation.RequireExactResourceIri(manifestationIri, nameof(manifestationIri));
        PageRef = pageRef ?? throw new ArgumentNullException(nameof(pageRef));
        LicenceIris = LuxembourgInFileRightsReadingTwo.SortedLicences(licenceIris, nameof(licenceIris));
        if (observedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A licence page's observation time is UTC.", nameof(observedAt));
        }

        ObservedAt = observedAt;
        ObservedLive = observedLive;
    }

    public string ManifestationIri { get; }

    /// <summary>The retained page: its response body, by digest.</summary>
    public SourceArtifactRef PageRef { get; }

    public IReadOnlyList<string> LicenceIris { get; }

    public DateTimeOffset ObservedAt { get; }

    public bool ObservedLive { get; }
}

public static class LuxembourgRightsChannels
{
    /// <summary>The first line of resolution /2's canonical text: its name.</summary>
    public const string ResolutionTwoProfileName = "lex-lu-rights-resolution/2";

    /// <summary>
    /// Resolution /2's rules, as canonical text (LU-DATA-DESIGN v2 §1.1, "Resolution"): /1's order, then the outcomes reading
    /// /2 makes possible. Each line is one rule, in the order <see cref="ResolveTwo"/> applies them; the first that holds wins.
    /// </summary>
    private const string ResolutionTwoProfileText =
        ResolutionTwoProfileName + "\n" +
        "in-file-channel=lex-lu-in-file-rights-reading/2;run-identity=the-rights-bound-run-identity;evidence=the-retained-body-and-the-reading-2-evidence-index\n" +
        "catalogue=the-run's-typed-jolux:license-assertions-per-manifestation;an-assertion-whose-object-is-not-one-exact-iri-is-unrepresentable\n" +
        "1=stale:either-channel's-run-identity-is-not-the-bound-run-identity\n" +
        "2=channel_enumeration_unproven:no-sparql-row-for-the-fetched-manifestation\n" +
        "3=typed_quarantine_in_file_reading_rejected:reading-2-rejected-the-file(malformed_xml,root_rejected,ambiguous_binding,invalid_licence_iri)\n" +
        "4=typed_quarantine_unrepresentable_licence_shape:the-sparql-row-holds-an-assertion-it-cannot-represent\n" +
        "5=missing_value:the-sparql-row-holds-no-licence\n" +
        "6=evidence_not_independent:the-two-channels-share-an-evidence-reference-or-digest\n" +
        "7=multiple:the-sparql-row-holds-more-than-one-licence\n" +
        "8=conflict:any-licence-the-file-declares-in-any-block-differs-from-the-sparql-value,or-any-identity-a-block-names-is-one-the-catalogue-knows-and-licenses-otherwise-than-exactly-the-sparql-value\n" +
        "9=non_admitting_licence_scl:the-sparql-value-is-licencescl;otherwise-typed_quarantine_unruled_licence:the-sparql-value-is-not-cc-by-4.0\n" +
        "10=agreed_cc_by_rederived:the-own-block-states-exactly-the-sparql-value-cc-by-4.0\n" +
        "11=sparql_cc_by_in_file_own_block_empty:the-own-block-states-no-licence\n" +
        "12=typed_quarantine_in_file_identity_unknown:no-own-block,and-the-file-names-no-identity-or-an-identity-unknown-to-the-catalogue\n" +
        "13=sparql_cc_by_in_file_names_other_identity:no-own-block,every-identity-the-file-names-is-cc-by-4.0-in-the-catalogue,and-the-file's-declared-licences-are-exactly-cc-by-4.0\n" +
        "14=sparql_cc_by_in_file_silent:the-file-states-no-licence\n" +
        "15=sparql_cc_by_in_file_not_applicable:the-in-file-channel-cannot-read-the-representation\n" +
        "single-channel=11,13,14,15:the-file-does-not-speak-for-the-fetched-manifestation;only-a-policy-naming-them-admits-them\n";

    /// <summary>The SHA-256 of resolution /2's canonical text, lowercase hex.</summary>
    public static string ResolutionTwoProfileSha256 { get; } = Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ResolutionTwoProfileText)));

    /// <summary>Resolution /2's canonical text, as UTF-8 bytes (a copy).</summary>
    public static byte[] CopyResolutionTwoProfileBytes() => System.Text.Encoding.UTF8.GetBytes(ResolutionTwoProfileText);

    /// <summary>
    /// Resolution /2 (LU-DATA-DESIGN v2 §1.1): the SPARQL channel the acquisition observed for the fetched manifestation
    /// against in-file reading /2 of its retained body, with every identity the file names checked against the catalogue.
    /// The in-file channel binds the rights' run identity and names the reading's body and the reading /2 evidence index as
    /// its evidence. The disposition states the evidence; whether it admits is the admission policy's question.
    /// </summary>
    /// <remarks>
    /// The PDF twin's block (a sibling <c>scl:JOLUXManifestation</c> naming <c>…/pdf</c>) never supplies the licence of the
    /// held body: only the own block does (rule 10). A sibling block is still read: a licence it states that differs from the
    /// SPARQL value, or an identity it names that the catalogue licenses otherwise, is a conflict (rule 8), so a CC BY own
    /// block beside a licenceSCL sibling never admits.
    /// </remarks>
    public static LuxembourgRightsChannelResolution ResolveTwo(
        string selectedManifestationIri,
        SourceArtifactRef boundRunIdentity,
        LuxembourgSparqlRightsChannelObservations sparqlObservations,
        SourceArtifactRef inFileRunIdentity,
        SourceArtifactRef inFileEvidenceIndexRef,
        LuxembourgInFileRightsReadingTwo reading,
        LuxembourgRightsCatalogue catalogue)
    {
        selectedManifestationIri = LuxembourgSourceValidation.RequireExactResourceIri(
            selectedManifestationIri,
            nameof(selectedManifestationIri));
        ArgumentNullException.ThrowIfNull(boundRunIdentity);
        ArgumentNullException.ThrowIfNull(sparqlObservations);
        ArgumentNullException.ThrowIfNull(inFileRunIdentity);
        ArgumentNullException.ThrowIfNull(inFileEvidenceIndexRef);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(catalogue);
        if (!string.Equals(reading.ManifestationIri, selectedManifestationIri, StringComparison.Ordinal))
        {
            throw new ArgumentException("A reading /2 resolves only the manifestation it read.", nameof(reading));
        }

        // The in-file channel /2: a row for the fetched manifestation when the own block was bound (with or without a
        // licence), a named refusal when the reader rejected the file, and nothing otherwise.
        var ownBlock = reading.Status is LuxembourgInFileRightsReadingTwoStatus.Bound
            or LuxembourgInFileRightsReadingTwoStatus.OwnBlockEmpty;
        var inFileObservations = new LuxembourgInFileRightsChannelObservations(
            inFileRunIdentity,
            inFileEvidenceIndexRef,
            ownBlock
                ? [new LuxembourgRightsChannelObservation(
                    selectedManifestationIri, inFileRunIdentity, reading.BodyRef, reading.OwnBlockLicenceIris)]
                : [],
            acquisitionCompleted: true,
            rejectedManifestationIris: reading.IsRejected ? [selectedManifestationIri] : []);
        var sparql = sparqlObservations.Find(selectedManifestationIri);
        var inFile = inFileObservations.Find(selectedManifestationIri);
        var disposition = ResolveDispositionTwo(
            boundRunIdentity, sparqlObservations, inFileObservations, sparql, reading, catalogue);
        return new LuxembourgRightsChannelResolution(
            selectedManifestationIri,
            boundRunIdentity,
            sparqlObservations,
            inFileObservations,
            sparql,
            inFile,
            disposition).AsResolutionTwo(reading);
    }

    private static LuxembourgRightsChannelDisposition ResolveDispositionTwo(
        SourceArtifactRef boundRunIdentity,
        LuxembourgSparqlRightsChannelObservations sparqlObservations,
        LuxembourgInFileRightsChannelObservations inFileObservations,
        LuxembourgRightsChannelObservation? sparql,
        LuxembourgInFileRightsReadingTwo reading,
        LuxembourgRightsCatalogue catalogue)
    {
        // 1. Both channels carry the rights' bound run identity.
        if (sparqlObservations.RunIdentity != boundRunIdentity || inFileObservations.RunIdentity != boundRunIdentity)
        {
            return LuxembourgRightsChannelDisposition.Stale;
        }

        // 2. No SPARQL row.
        if (sparql is null)
        {
            return LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven;
        }

        // 3. A rejected reading outranks anything SPARQL carries, as in /1.
        if (reading.IsRejected)
        {
            return LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected;
        }

        // 4. A shape the SPARQL channel cannot represent (reading /2 refuses, rather than drops, what it cannot carry).
        if (sparql.UnrepresentableLicenceAssertions > 0)
        {
            return LuxembourgRightsChannelDisposition.TypedQuarantineUnrepresentableLicenceShape;
        }

        // 5. No SPARQL value.
        if (sparql.LicenceIris.Count == 0)
        {
            return LuxembourgRightsChannelDisposition.MissingValue;
        }

        // 6. Independence: the two channels share no evidence.
        var sparqlEvidence = new[] { sparql.EvidenceRef, sparqlObservations.EnumerationRef };
        var inFileEvidence = new[] { reading.BodyRef, inFileObservations.EnumerationRef };
        if (sparqlEvidence.Any(left => inFileEvidence.Any(right =>
                left == right || string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal))))
        {
            return LuxembourgRightsChannelDisposition.EvidenceNotIndependent;
        }

        // 7. More than one SPARQL value.
        if (sparql.LicenceIris.Count > 1)
        {
            return LuxembourgRightsChannelDisposition.Multiple;
        }

        // 8. Conflict: anything the file declares, in any block, that is not the SPARQL value; or an identity a block names
        // that the catalogue licenses otherwise (licenceSCL, a double licence, another IRI, or a shape it cannot represent).
        var licence = sparql.LicenceIris[0];
        if (reading.DeclaredLicenceIris.Any(declared => !string.Equals(declared, licence, StringComparison.Ordinal)) ||
            reading.NamedIdentities.Any(named => catalogue.DisagreesWith(named.Identity, licence)))
        {
            return LuxembourgRightsChannelDisposition.Conflict;
        }

        // 9. The SPARQL value itself.
        if (string.Equals(licence, VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl, StringComparison.Ordinal))
        {
            return LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl;
        }

        if (!string.Equals(licence, VerifiedLuxembourgSourceProfile.AdmittingLicence, StringComparison.Ordinal))
        {
            return LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence;
        }

        return reading.Status switch
        {
            // 10. The own block states exactly CC BY (rule 8 has ruled out any other value in it).
            LuxembourgInFileRightsReadingTwoStatus.Bound when reading.OwnBlockLicenceIris.Count == 1 &&
                string.Equals(reading.OwnBlockLicenceIris[0], licence, StringComparison.Ordinal) =>
                LuxembourgRightsChannelDisposition.AgreedCcByRederived,
            // 11. The own block states no licence, whatever its siblings say.
            LuxembourgInFileRightsReadingTwoStatus.OwnBlockEmpty => LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty,
            // 12. No own block, and something the file names cannot be checked: it names no identity, or one the catalogue
            // does not know. A file naming one CC BY identity and one unknown one never reaches rule 13 (design §5: every
            // identity the block names is itself CC BY in the same catalogue).
            LuxembourgInFileRightsReadingTwoStatus.Unbound when reading.NamedIdentities.Count == 0 ||
                reading.NamedIdentities.Any(named => !catalogue.Knows(named.Identity)) =>
                LuxembourgRightsChannelDisposition.TypedQuarantineInFileIdentityUnknown,
            // 13. No own block; every identity the file names is known and, by rule 8, licensed exactly CC BY; the file's
            // declared values are exactly [CC BY] (rule 8).
            LuxembourgInFileRightsReadingTwoStatus.Unbound => LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity,
            // 14. Nothing stated.
            LuxembourgInFileRightsReadingTwoStatus.Silent => LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent,
            // 15. A representation the channel cannot read.
            LuxembourgInFileRightsReadingTwoStatus.NotApplicable => LuxembourgRightsChannelDisposition.SparqlCcByInFileNotApplicable,
            _ => throw new InvalidOperationException($"Reading /2 status {reading.Status} has no resolution /2 rule."),
        };
    }

    public static LuxembourgRightsChannelResolution Resolve(
        string selectedManifestationIri,
        SourceArtifactRef boundRunIdentity,
        LuxembourgSparqlRightsChannelObservations sparqlObservations,
        LuxembourgInFileRightsChannelObservations inFileObservations)
    {
        selectedManifestationIri = LuxembourgSourceValidation.RequireExactResourceIri(
            selectedManifestationIri,
            nameof(selectedManifestationIri));
        ArgumentNullException.ThrowIfNull(boundRunIdentity);
        ArgumentNullException.ThrowIfNull(sparqlObservations);
        ArgumentNullException.ThrowIfNull(inFileObservations);

        var sparql = sparqlObservations.Find(selectedManifestationIri);
        var inFile = inFileObservations.Find(selectedManifestationIri);
        var disposition = ResolveDisposition(
            selectedManifestationIri,
            boundRunIdentity,
            sparqlObservations,
            inFileObservations,
            sparql,
            inFile);

        return new LuxembourgRightsChannelResolution(
            selectedManifestationIri,
            boundRunIdentity,
            sparqlObservations,
            inFileObservations,
            sparql,
            inFile,
            disposition);
    }

    /// <summary>
    /// Channel one read alone: the same value tail the two-channel path uses, so a missing value, a
    /// multiple, the non-admitting SCL licence and an unruled licence all keep their own named
    /// outcomes rather than collapsing into "pending". Only a single admitting licence reaches
    /// <see cref="LuxembourgRightsChannelDisposition.SecondChannelPending"/>.
    /// </summary>
    private static LuxembourgRightsChannelDisposition ClassifySingleChannel(
        LuxembourgRightsChannelObservation sparql)
    {
        if (sparql.LicenceIris.Count == 0)
        {
            // Only reachable once the dominant unrepresentable-shape gate in ResolveDisposition
            // has declined, so an empty set here is a real observed emptiness.
            return LuxembourgRightsChannelDisposition.MissingValue;
        }

        if (sparql.LicenceIris.Count > 1)
        {
            return LuxembourgRightsChannelDisposition.Multiple;
        }

        var licence = sparql.LicenceIris[0];
        if (string.Equals(
                licence, VerifiedLuxembourgSourceProfile.AdmittingLicence, StringComparison.Ordinal))
        {
            return LuxembourgRightsChannelDisposition.SecondChannelPending;
        }

        return string.Equals(
                licence, VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl, StringComparison.Ordinal)
            ? LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl
            : LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence;
    }

    private static LuxembourgRightsChannelDisposition ResolveDisposition(
        string manifestationIri,
        SourceArtifactRef boundRunIdentity,
        LuxembourgSparqlRightsChannelObservations sparqlObservations,
        LuxembourgInFileRightsChannelObservations inFileObservations,
        LuxembourgRightsChannelObservation? sparql,
        LuxembourgRightsChannelObservation? inFile)
    {
        if (sparqlObservations.RunIdentity != boundRunIdentity ||
            inFileObservations.RunIdentity != boundRunIdentity)
        {
            return LuxembourgRightsChannelDisposition.Stale;
        }

        if (sparql is null)
        {
            return LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven;
        }

        // The in-file channel read this manifestation and refused it. That is a finding, and it
        // outranks anything the SPARQL channel carries: a rights answer assembled while one
        // channel's reading was rejected is not a completed dual-channel answer. Before this, the
        // refusal produced no row and the resolution reported ChannelEnumerationUnproven -- a
        // channel that ran reported as one that never did.
        if (inFileObservations.RejectedManifestationIris.Contains(manifestationIri, StringComparer.Ordinal))
        {
            return LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected;
        }

        // S2-A05: DRIFT FAILS CLOSED BEFORE ANY READABLE VALUE IS CLASSIFIED, and this gate is
        // where both paths meet so the rule cannot hold on only one of them. A channel that could
        // not represent one of the publisher's licence assertions has an incomplete reading, and an
        // incomplete reading must not resolve as though the values it did carry were the whole
        // answer. That includes the mixed case: a readable CC BY sitting beside an assertion we
        // dropped is still an incomplete reading, and my first version let it resolve on the
        // readable value alone because the count was consulted only when the set was empty.
        //
        // Placed after the stale and enumeration gates deliberately. A stale run, or a channel that
        // never enumerated this manifestation, is a prior and more basic finding than what that
        // channel turned out to contain.
        if (sparql.UnrepresentableLicenceAssertions > 0 ||
            inFile?.UnrepresentableLicenceAssertions > 0)
        {
            return LuxembourgRightsChannelDisposition.TypedQuarantineUnrepresentableLicenceShape;
        }

        if (inFile is null)
        {
            var singleChannel = ClassifySingleChannel(sparql);
            return singleChannel == LuxembourgRightsChannelDisposition.SecondChannelPending &&
                inFileObservations.AcquisitionCompleted
                    ? LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven
                    : singleChannel;
        }

        if (sparql.LicenceIris.Count == 0 || inFile.LicenceIris.Count == 0)
        {
            // Reaching here means neither channel dropped anything: the dominant gate above
            // already returned for that case, on both routes, so an empty set here really is an
            // observed emptiness.
            return LuxembourgRightsChannelDisposition.MissingValue;
        }

        var sparqlEvidence = new[]
        {
            sparql.EvidenceRef,
            sparqlObservations.EnumerationRef,
        };
        var inFileEvidence = new[]
        {
            inFile.EvidenceRef,
            inFileObservations.EnumerationRef,
        };
        if (sparqlEvidence.Any(left => inFileEvidence.Any(right =>
                left == right ||
                string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal))))
        {
            return LuxembourgRightsChannelDisposition.EvidenceNotIndependent;
        }

        if (sparql.LicenceIris.Count > 1 || inFile.LicenceIris.Count > 1)
        {
            return LuxembourgRightsChannelDisposition.Multiple;
        }

        var sparqlLicence = sparql.LicenceIris[0];
        var inFileLicence = inFile.LicenceIris[0];
        if (!string.Equals(sparqlLicence, inFileLicence, StringComparison.Ordinal))
        {
            return LuxembourgRightsChannelDisposition.Conflict;
        }

        if (string.Equals(
                sparqlLicence,
                VerifiedLuxembourgSourceProfile.AdmittingLicence,
                StringComparison.Ordinal))
        {
            return LuxembourgRightsChannelDisposition.AgreedSameRunCcBy;
        }

        return string.Equals(
                sparqlLicence,
                VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl,
                StringComparison.Ordinal)
            ? LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl
            : LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence;
    }
}

internal static class LuxembourgRightsChannelCollection
{
    internal static IReadOnlyList<LuxembourgRightsChannelObservation> CopyCanonical(
        SourceArtifactRef runIdentity,
        IReadOnlyList<LuxembourgRightsChannelObservation> observations,
        string parameterName)
    {
        var copy = LuxembourgSourceValidation.Copy(observations, parameterName)
            .OrderBy(
                static row => row.ManifestationIri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
        if (copy.Any(row => row.RunIdentity != runIdentity))
        {
            throw new ArgumentException(
                "Every channel row must bind the collection's exact run identity.",
                parameterName);
        }

        for (var index = 1; index < copy.Length; index++)
        {
            if (string.Equals(
                    copy[index - 1].ManifestationIri,
                    copy[index].ManifestationIri,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A rights channel can contain only one observation per manifestation.",
                    parameterName);
            }
        }

        return Array.AsReadOnly(copy);
    }
}
