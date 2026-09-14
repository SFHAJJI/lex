using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>Why accepted Stage 3 producer outputs could not form the shared evidence boundary.</summary>
public enum Stage3EvidenceEnvelopeRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("europe_not_complete")]
    EuropeNotComplete = 1,

    [JsonStringEnumMemberName("luxembourg_not_complete")]
    LuxembourgNotComplete = 2,

    [JsonStringEnumMemberName("annex_production_refused")]
    AnnexProductionRefused = 3,

    [JsonStringEnumMemberName("annex_is_not_text_unavailable")]
    AnnexIsNotTextUnavailable = 4,

    [JsonStringEnumMemberName("duplicate_annex")]
    DuplicateAnnex = 5,
}

/// <summary>
/// The construction-only meeting point for the accepted EU, Luxembourg and image-only-annex
/// producer outputs. It neither serializes nor upgrades them to a release artifact.
/// </summary>
public sealed class Stage3EvidenceEnvelope
{
    private Stage3EvidenceEnvelope(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        IReadOnlyList<EuAnnexBodyDisposition> imageOnlyEuAnnexes)
    {
        Europe = europe;
        Luxembourg = luxembourg;
        ImageOnlyEuAnnexes = imageOnlyEuAnnexes;
    }

    public EuQueryExecutionResult Europe { get; }

    public LuxembourgQueryExecutionResult Luxembourg { get; }

    /// <summary>
    /// Successfully produced image-only annex gaps, ordered by their evidence-bound identity.
    /// A producer refusal cannot enter this collection as a disposition. This collection records
    /// the supplied producer outcomes; it does not certify that a global annex population is complete.
    /// </summary>
    public IReadOnlyList<EuAnnexBodyDisposition> ImageOnlyEuAnnexes { get; }

    public static Stage3EvidenceEnvelope? TryCreate(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        IEnumerable<EuImageOnlyAnnexProductionResult> imageOnlyEuAnnexProductions,
        out Stage3EvidenceEnvelopeRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(europe);
        ArgumentNullException.ThrowIfNull(luxembourg);
        ArgumentNullException.ThrowIfNull(imageOnlyEuAnnexProductions);

        refusal = Stage3EvidenceEnvelopeRefusal.None;
        detail = null;
        if (!EuropeIsComplete(europe))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.EuropeNotComplete;
            detail = europe.Refusal?.Code.ToString() ?? europe.Completion?.ToString() ?? "completion absent";
            return null;
        }

        if (!LuxembourgIsComplete(luxembourg))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.LuxembourgNotComplete;
            detail = luxembourg.Refusal?.Code.ToString()
                ?? luxembourg.Completion?.ToString()
                ?? "completion absent";
            return null;
        }

        var annexes = new List<EuAnnexBodyDisposition>();
        foreach (var production in imageOnlyEuAnnexProductions)
        {
            if (production is null)
            {
                throw new ArgumentException(
                    "An annex production result cannot be null.",
                    nameof(imageOnlyEuAnnexProductions));
            }

            if (!production.Produced || production.Disposition is null)
            {
                refusal = Stage3EvidenceEnvelopeRefusal.AnnexProductionRefused;
                detail = production.Refusal + ": " + production.Detail;
                return null;
            }

            var annex = production.Disposition;
            if (annex.Outcome != EuAnnexBodyDispositionOutcome.TextNotAvailable)
            {
                refusal = Stage3EvidenceEnvelopeRefusal.AnnexIsNotTextUnavailable;
                detail = annex.IdentitySha256;
                return null;
            }

            annexes.Add(annex);
        }

        var duplicate = annexes
            .GroupBy(AnnexKey, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            refusal = Stage3EvidenceEnvelopeRefusal.DuplicateAnnex;
            detail = duplicate.Key;
            return null;
        }

        annexes.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.IdentitySha256, right.IdentitySha256));
        return new Stage3EvidenceEnvelope(
            europe,
            luxembourg,
            new ReadOnlyCollection<EuAnnexBodyDisposition>(annexes));
    }

    private static bool EuropeIsComplete(EuQueryExecutionResult result) =>
        result.Refusal is null &&
        result.Completion == EuQueryExecutionCompletion.AllFamiliesProven &&
        result.FamilyOutcomes.All(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven) &&
        result.WatermarkWitnessPlan is not null &&
        result.RootBinding is not null &&
        result.WitnessReconciliation is not null &&
        result.WitnessTerminations is not null &&
        result.ScopeManifestReceipt is not null &&
        result.ScopeManifestCanonicalSha256 is not null &&
        result.DocumentAcquisitionOutcomesByOrdinal is not null &&
        result.DocumentLadderResultsByOrdinal is not null &&
        result.ObservedManifestationTypesByCelex is not null &&
        result.ObservedExpressionsByCelex is not null &&
        result.MintedRowsByOrdinal is not null &&
        result.LocatedAmendmentProduction is not null &&
        result.CorrigendumTripwires is not null &&
        result.CorpusRecordSetRef is not null &&
        result.CorpusRecordSet is not null;

    private static bool LuxembourgIsComplete(LuxembourgQueryExecutionResult result) =>
        result.Refusal is null &&
        result.Completion == LuxembourgQueryExecutionCompletion.AllFamiliesProven &&
        result.FamilyOutcomes.All(static outcome => outcome.Kind is
            LuxembourgFamilyEnumerationOutcomeKind.Proven or
            LuxembourgFamilyEnumerationOutcomeKind.CoverProven) &&
        result.ScopeManifestReceipt is not null &&
        result.ScopeManifestCanonicalSha256 is not null &&
        result.DocumentAcquisitionOutcomesByOrdinal is not null &&
        result.CorpusRecordSetRef is not null &&
        result.CorpusRecordSet is not null &&
        result.GazetteBodySetsByOrdinal is not null &&
        result.GazetteListingFetchRefusalsByOrdinal is not null &&
        result.GazetteListingsWithContradictoryLegalValueByOrdinal is not null;

    private static string AnnexKey(EuAnnexBodyDisposition annex) => string.Join('\n',
        annex.SourceObject.CanonicalKeySha256,
        annex.AnnexLocation.RawValue);
}
