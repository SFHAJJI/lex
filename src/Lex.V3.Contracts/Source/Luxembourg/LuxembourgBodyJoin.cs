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

/// <summary>
/// Why one manifestation is not selected for acquisition. Closed, and DELIBERATELY SHORT.
/// </summary>
/// <remarks>
/// This vocabulary had twelve members and every candidate carried eight of them unconditionally, so
/// no Luxembourg body was ever selected and the accepted fraction of every real manifest was zero.
/// The owner principle (RULING lex-event-20260904T205636383Z-e92b888b62c24df29fe3f8c1be5016f0) is
/// that a law that can legitimately be ingested is ingested, and that there are exactly four
/// legitimate reasons not to hold a body: the publisher's own rules (robots), the publisher's own
/// answer (404, 410, an unservable listing), the publisher marking the object not reusable, and a
/// custody failure on our side. UNKNOWN IS NEVER A REASON; it is recorded and the object proceeds.
/// <para>
/// Of the twelve, three survive here because they are pre-acquisition facts about the publisher's
/// own listing, plus one structural guard. The rest were removed rather than left unproduced:
/// robots, HTTP evidence and custody verification are acquisition-time facts that gate Held on the
/// corpus record and already live there; the in-file licence and its derivation belong to D1-04f;
/// the assertion family's proof is a run-level precondition carried by the
/// <see cref="LuxembourgProvenResourceObservations"/> door rather than re-checked here; and the
/// rights blockers that fired for an unproven, missing, multiple or unruled licence are gone
/// because every one of those is an UNKNOWN, which the owner ruling forbids as a reason. The rights
/// axis gates SERVING and derivation under Decision 58(a), never holding.
/// </para>
/// </remarks>
public enum LuxembourgBodyBlockerCode
{
    /// <summary>
    /// The publisher's own listing does not resolve to a usable manifestation tuple: the WEMI walk
    /// through isRealizedBy, isEmbodiedBy and isExemplifiedBy did not close, or its coordinates
    /// disagree. This is the publisher's unservable listing, one of the four legitimate reasons.
    /// </summary>
    [JsonStringEnumMemberName("wemi_tuple_typed_quarantine")]
    WemiTupleTypedQuarantine = 1,

    /// <summary>
    /// A structural guard rather than an ingestion refusal: this candidate was reached from another
    /// root and so is not this root's body. Without it one act could select another act's file.
    /// </summary>
    [JsonStringEnumMemberName("wemi_root_mismatch")]
    WemiRootMismatch = 2,

    /// <summary>
    /// The manifestation's own userFormat is not a wording candidate for this route (html, doc,
    /// docx, svg). The publisher listed something, and what it listed is not a body we can compare
    /// text from, which is again the unservable-listing reason rather than an unknown.
    /// </summary>
    [JsonStringEnumMemberName("format_not_a_wording_candidate")]
    FormatNotAWordingCandidate = 3,

    /// <summary>
    /// The publisher's own licence declaration marks this object not reusable (the non-admitting
    /// SCL licence). This is the ONLY rights state that withholds a body: it is the publisher
    /// marking the object not reusable, the third legitimate reason. An unruled, missing, multiple
    /// or as-yet-unread licence is an unknown, is recorded on the rights resolution, and does not
    /// withhold anything.
    /// </summary>
    [JsonStringEnumMemberName("publisher_marked_not_reusable")]
    PublisherMarkedNotReusable = 4,
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

/// <summary>
/// The pre-acquisition body selection: which of a root's manifestations this run may fetch, decided
/// on evidence held before any request. RULING
/// lex-event-20260904T201756388Z-897fb21258b14e088f0495121479c9f4.
/// </summary>
/// <remarks>
/// EVERY CANDIDATE USED TO CARRY EIGHT UNCONDITIONAL BLOCKERS, so nothing was ever selected and the
/// accepted fraction of every real LU manifest was zero. Those eight were not wrong, they were in
/// the wrong place: four of them name facts that only exist AFTER a fetch, so they cannot gate the
/// decision to fetch without making a cycle. They are re-homed, not weakened.
/// <list type="bullet">
/// <item>
/// Gating THIS selection, and checked here: the manifestation join with an admitted token
/// (<see cref="LuxembourgBodyBlockerCode.WemiTupleTypedQuarantine"/> and
/// <see cref="LuxembourgBodyBlockerCode.WemiRootMismatch"/>), and the SPARQL rights channel
/// (<see cref="LuxembourgBodyBlockerCode.RightsChannelEnumerationUnproven"/> and
/// <see cref="LuxembourgBodyBlockerCode.RightsChannelsNotAgreed"/>). The format is the third
/// condition and is decided by the caller's own format dimension, not here.
/// </item>
/// <item>
/// The fourth condition, that the assertion family was proven and re-verified, IS NOT A GUARD AT
/// ALL. It is a run-level precondition established before any observation exists, and it is carried
/// by a door: this join reads observations that can only be built through
/// <see cref="LuxembourgProvenResourceObservations"/>, whose construction requires the run's own
/// <see cref="Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProof"/>. There was a check
/// here that compared a ref against itself and could never fail, which is worse than no check
/// because it reads downstream as a guarantee; it is deleted, and the real per-assertion binding
/// check stays where it can genuinely fail, in
/// <see cref="LuxembourgWemiTopology"/> (ObservationMismatch).
/// </item>
/// <item>
/// Gating Held on the CORPUS RECORD after the fetch, never this axis: robots evidence, HTTP
/// evidence, the in-file licence declaration and custody verification. They already live there as
/// the per-object robots refusal, the retained hops and the custody floor check.
/// </item>
/// </list>
/// </remarks>
public static class LuxembourgBodyJoin
{
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
        var blockers = new List<LuxembourgBodyBlockerCode>();

        // The manifestation join: the WEMI walk reached an item in the publisher's own current
        // filestore family through isRealizedBy, isEmbodiedBy and isExemplifiedBy, and every
        // coordinate agreed. A quarantined tuple is an unservable listing.
        if (wemi.Disposition != LuxembourgWemiCandidateDisposition.StructurallyConsistent)
        {
            blockers.Add(LuxembourgBodyBlockerCode.WemiTupleTypedQuarantine);
        }

        // Structural guard: a candidate reached from another root is not this root's body.
        if (!string.Equals(wemi.RootIri, rootIri, StringComparison.Ordinal))
        {
            blockers.Add(LuxembourgBodyBlockerCode.WemiRootMismatch);
        }

        // An admitted wording token. html, doc, docx and svg are real listings this route cannot
        // compare text from.
        if (Http.LuxembourgAuthorityIri.TryParseUserFormat(wemi.FormatIri) is null)
        {
            blockers.Add(LuxembourgBodyBlockerCode.FormatNotAWordingCandidate);
        }

        // Rights. The ONLY state that withholds a body is the publisher marking the object not
        // reusable. Every other rights state, including an unruled licence, a missing one, several,
        // and the in-file channel not having run, is an UNKNOWN: it is recorded on
        // RightsResolution for the answer layer's Decision 58(a) disclosure and withholds nothing.
        // The rights axis gates serving and derivation, never holding.
        if (rights.Disposition == LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl)
        {
            blockers.Add(LuxembourgBodyBlockerCode.PublisherMarkedNotReusable);
        }

        return new LuxembourgBodyCandidateResolution(
            wemi,
            rights,
            blockers.Count == 0
                ? LuxembourgBodyCandidateDisposition.AcceptedCandidate
                : LuxembourgBodyCandidateDisposition.Withheld,
            blockers);
    }

    private static IComparer<string> ScalarComparer =>
        LuxembourgSourceValidation.UnicodeScalarComparer;
}
