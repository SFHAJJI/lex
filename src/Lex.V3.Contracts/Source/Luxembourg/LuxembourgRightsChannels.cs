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
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgRightsChannelObservation
{
    [JsonConstructor]
    public LuxembourgRightsChannelObservation(
        string manifestationIri,
        SourceArtifactRef runIdentity,
        SourceArtifactRef evidenceRef,
        IReadOnlyList<string> licenceIris)
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
    }

    public string ManifestationIri { get; }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef EvidenceRef { get; }

    public IReadOnlyList<string> LicenceIris { get; }
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

        if (inFile is null)
        {
            // Channel one resolved and channel two has not run. Read channel one on its own terms,
            // with exactly the same tail the two-channel path applies, and stop at the typed
            // pending state instead of claiming an agreement one channel cannot make.
            return ClassifySingleChannel(sparql);
        }

        if (sparql.LicenceIris.Count == 0 || inFile.LicenceIris.Count == 0)
        {
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
