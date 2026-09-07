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

    public string ReasonCode => Disposition switch
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
        _ => throw new InvalidOperationException("Unknown rights-channel disposition."),
    };
}

public static class LuxembourgRightsChannels
{
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
