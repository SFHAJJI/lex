using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public enum LuxembourgBodyCandidateDisposition
{
    [JsonStringEnumMemberName("withheld")]
    Withheld = 1,

    [JsonStringEnumMemberName("accepted_candidate")]
    AcceptedCandidate = 2,
}

public enum LuxembourgBodyBlockerCode
{
    [JsonStringEnumMemberName("assertion_enumeration_unproven")]
    AssertionEnumerationUnproven = 1,

    [JsonStringEnumMemberName("rights_channel_enumeration_unproven")]
    RightsChannelEnumerationUnproven = 2,

    [JsonStringEnumMemberName("text_public_not_cleared")]
    TextPublicNotCleared = 3,

    [JsonStringEnumMemberName("licence_contract_result_missing")]
    LicenceContractResultMissing = 4,

    [JsonStringEnumMemberName("robots_evidence_unbound")]
    RobotsEvidenceUnbound = 5,

    [JsonStringEnumMemberName("http_observation_unbound")]
    HttpObservationUnbound = 6,

    [JsonStringEnumMemberName("derivation_unverified")]
    DerivationUnverified = 7,

    [JsonStringEnumMemberName("integrity_unverified")]
    IntegrityUnverified = 8,

    [JsonStringEnumMemberName("wemi_tuple_typed_quarantine")]
    WemiTupleTypedQuarantine = 9,

    [JsonStringEnumMemberName("rights_channels_not_agreed")]
    RightsChannelsNotAgreed = 10,

    [JsonStringEnumMemberName("wemi_root_mismatch")]
    WemiRootMismatch = 11,

    [JsonStringEnumMemberName("wemi_observation_run_mismatch")]
    WemiObservationRunMismatch = 12,
}

public enum LuxembourgBodyRootBlockerCode
{
    [JsonStringEnumMemberName("publisher_realization_path_unproven")]
    PublisherRealizationPathUnproven = 1,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgBodyCandidateResolution
{
    internal LuxembourgBodyCandidateResolution(
        LuxembourgWemiCandidate wemiCandidate,
        LuxembourgRightsChannelResolution rightsResolution,
        LuxembourgBodyCandidateDisposition disposition,
        IReadOnlyList<LuxembourgBodyBlockerCode> blockerCodes)
    {
        WemiCandidate = wemiCandidate ?? throw new ArgumentNullException(nameof(wemiCandidate));
        RightsResolution = rightsResolution
            ?? throw new ArgumentNullException(nameof(rightsResolution));
        Disposition = LuxembourgSourceValidation.RequireDefined(disposition, nameof(disposition));
        var blockers = LuxembourgSourceValidation.Copy(blockerCodes, nameof(blockerCodes))
            .Select(code => LuxembourgSourceValidation.RequireDefined(code, nameof(blockerCodes)))
            .Distinct()
            .Order()
            .ToArray();
        if ((blockers.Length == 0) !=
            (Disposition == LuxembourgBodyCandidateDisposition.AcceptedCandidate))
        {
            throw new ArgumentException(
                "Exactly a blocker-free body candidate may be accepted.",
                nameof(disposition));
        }

        BlockerCodes = Array.AsReadOnly(blockers);
    }

    public LuxembourgWemiCandidate WemiCandidate { get; }

    public LuxembourgRightsChannelResolution RightsResolution { get; }

    public LuxembourgBodyCandidateDisposition Disposition { get; }

    public IReadOnlyList<LuxembourgBodyBlockerCode> BlockerCodes { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgBodyJoinResolution
{
    internal LuxembourgBodyJoinResolution(
        string rootIri,
        SourceArtifactRef observationRunRef,
        IReadOnlyList<LuxembourgBodyCandidateResolution> candidates,
        IReadOnlyList<LuxembourgBodyRootBlockerCode> rootBlockerCodes,
        IReadOnlyList<LuxembourgWemiBlocker> wemiBlockers)
    {
        RootIri = LuxembourgSourceValidation.RequireExactResourceIri(rootIri, nameof(rootIri));
        ObservationRunRef = observationRunRef
            ?? throw new ArgumentNullException(nameof(observationRunRef));
        Candidates = LuxembourgSourceValidation.Copy(candidates, nameof(candidates));
        RootBlockerCodes = Array.AsReadOnly(
            LuxembourgSourceValidation.Copy(rootBlockerCodes, nameof(rootBlockerCodes))
                .Select(code => LuxembourgSourceValidation.RequireDefined(
                    code,
                    nameof(rootBlockerCodes)))
                .Distinct()
                .Order()
                .ToArray());
        WemiBlockers = LuxembourgSourceValidation.Copy(wemiBlockers, nameof(wemiBlockers));
    }

    public string RootIri { get; }

    public SourceArtifactRef ObservationRunRef { get; }

    public IReadOnlyList<LuxembourgBodyCandidateResolution> Candidates { get; }

    public IReadOnlyList<LuxembourgBodyRootBlockerCode> RootBlockerCodes { get; }

    public IReadOnlyList<LuxembourgWemiBlocker> WemiBlockers { get; }
}

public static class LuxembourgBodyJoin
{
    private static readonly IReadOnlyList<LuxembourgBodyBlockerCode>
        CurrentMilestoneBlockers = Array.AsReadOnly(new[]
        {
            LuxembourgBodyBlockerCode.AssertionEnumerationUnproven,
            LuxembourgBodyBlockerCode.RightsChannelEnumerationUnproven,
            LuxembourgBodyBlockerCode.TextPublicNotCleared,
            LuxembourgBodyBlockerCode.LicenceContractResultMissing,
            LuxembourgBodyBlockerCode.RobotsEvidenceUnbound,
            LuxembourgBodyBlockerCode.HttpObservationUnbound,
            LuxembourgBodyBlockerCode.DerivationUnverified,
            LuxembourgBodyBlockerCode.IntegrityUnverified,
        });

    public static LuxembourgBodyJoinResolution Resolve(
        string rootIri,
        SourceArtifactRef observationRunRef,
        LuxembourgWemiTopologyResolution topology,
        LuxembourgSparqlRightsChannelObservations sparqlRights,
        LuxembourgInFileRightsChannelObservations inFileRights)
    {
        rootIri = LuxembourgSourceValidation.RequireExactResourceIri(rootIri, nameof(rootIri));
        ArgumentNullException.ThrowIfNull(observationRunRef);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(sparqlRights);
        ArgumentNullException.ThrowIfNull(inFileRights);

        var candidates = topology.Candidates
            .OrderBy(static row => row.LanguageIri, ScalarComparer)
            .ThenBy(static row => row.FormatIri, ScalarComparer)
            .ThenBy(static row => row.ExpressionIri, ScalarComparer)
            .ThenBy(static row => row.ManifestationIri, ScalarComparer)
            .ThenBy(static row => row.ItemIri, ScalarComparer)
            .Select(wemi => ResolveCandidate(
                rootIri,
                observationRunRef,
                wemi,
                sparqlRights,
                inFileRights))
            .ToArray();
        var rootBlockers = candidates.Length == 0
            ? new[] { LuxembourgBodyRootBlockerCode.PublisherRealizationPathUnproven }
            : [];

        return new LuxembourgBodyJoinResolution(
            rootIri,
            observationRunRef,
            candidates,
            rootBlockers,
            topology.Blockers);
    }

    private static LuxembourgBodyCandidateResolution ResolveCandidate(
        string rootIri,
        SourceArtifactRef observationRunRef,
        LuxembourgWemiCandidate wemi,
        LuxembourgSparqlRightsChannelObservations sparqlRights,
        LuxembourgInFileRightsChannelObservations inFileRights)
    {
        var rights = LuxembourgRightsChannels.Resolve(
            wemi.ManifestationIri,
            observationRunRef,
            sparqlRights,
            inFileRights);
        var blockers = CurrentMilestoneBlockers.ToList();
        if (wemi.Disposition != LuxembourgWemiCandidateDisposition.StructurallyConsistent)
        {
            blockers.Add(LuxembourgBodyBlockerCode.WemiTupleTypedQuarantine);
        }

        if (!rights.ChannelsAgreeOnAdmittingLicence)
        {
            blockers.Add(LuxembourgBodyBlockerCode.RightsChannelsNotAgreed);
        }

        if (!string.Equals(wemi.RootIri, rootIri, StringComparison.Ordinal))
        {
            blockers.Add(LuxembourgBodyBlockerCode.WemiRootMismatch);
        }

        if (wemi.ObservationRef != observationRunRef)
        {
            blockers.Add(LuxembourgBodyBlockerCode.WemiObservationRunMismatch);
        }

        return new LuxembourgBodyCandidateResolution(
            wemi,
            rights,
            LuxembourgBodyCandidateDisposition.Withheld,
            blockers);
    }

    private static IComparer<string> ScalarComparer =>
        LuxembourgSourceValidation.UnicodeScalarComparer;
}
