using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why the exact held Luxembourg body population could not be made derivation-ready.</summary>
public enum LuxembourgHeldBodyDerivationPopulationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("held_record_has_no_held_outcome")]
    HeldRecordHasNoHeldOutcome = 1,

    [JsonStringEnumMemberName("held_outcome_has_no_record")]
    HeldOutcomeHasNoRecord = 2,

    [JsonStringEnumMemberName("held_outcome_record_is_not_held")]
    HeldOutcomeRecordIsNotHeld = 3,

    [JsonStringEnumMemberName("held_receipt_mismatch")]
    HeldReceiptMismatch = 4,

    [JsonStringEnumMemberName("held_record_has_no_selected_address")]
    HeldRecordHasNoSelectedAddress = 5,

    [JsonStringEnumMemberName("held_record_selected_identity_mismatch")]
    HeldRecordSelectedIdentityMismatch = 6,

    [JsonStringEnumMemberName("held_record_has_no_final_rights_resolution")]
    HeldRecordHasNoFinalRightsResolution = 7,

    [JsonStringEnumMemberName("held_record_final_rights_resolution_ambiguous")]
    HeldRecordFinalRightsResolutionAmbiguous = 8,
}

internal sealed class LuxembourgSelectedDocumentFetch
{
    internal LuxembourgSelectedDocumentFetch(
        LuxembourgDocumentFetchAddress address,
        LuxembourgWemiCandidate wemiCandidate)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        WemiCandidate = wemiCandidate ?? throw new ArgumentNullException(nameof(wemiCandidate));
        if (WemiCandidate.Disposition != LuxembourgWemiCandidateDisposition.StructurallyConsistent ||
            !string.Equals(
                LuxembourgFileUri.RequireValid(WemiCandidate.ItemIri).Value.AbsoluteUri,
                Address.StoreFileUri.Value.AbsoluteUri,
                StringComparison.Ordinal) ||
            LuxembourgAuthorityIri.TryParseUserFormat(WemiCandidate.FormatIri) != Address.UserFormatToken)
        {
            throw new ArgumentException(
                "The selected WEMI candidate must be the exact candidate that minted the address.",
                nameof(wemiCandidate));
        }
    }

    internal LuxembourgDocumentFetchAddress Address { get; }

    internal LuxembourgWemiCandidate WemiCandidate { get; }
}

/// <summary>
/// One held Luxembourg corpus body beside the exact publisher-selected address that determines
/// which derivation profile may read its retained bytes.
/// </summary>
public sealed class LuxembourgHeldBodyDerivationInput
{
    internal LuxembourgHeldBodyDerivationInput(
        CorpusRecord corpusRecord,
        LuxembourgSelectedDocumentFetch selectedFetch,
        DurableBlobWriteReceipt receipt,
        LuxembourgRightsChannelResolution? rightsResolution = null)
    {
        CorpusRecord = corpusRecord;
        Address = selectedFetch.Address;
        SelectedWemiCandidate = selectedFetch.WemiCandidate;
        Receipt = receipt;
        RightsResolution = rightsResolution;
    }

    public int ObjectOrdinal => CorpusRecord.ObjectOrdinal;

    public CorpusRecord CorpusRecord { get; }

    public LuxembourgDocumentFetchAddress Address { get; }

    public LuxembourgWemiCandidate SelectedWemiCandidate { get; }

    public DurableBlobWriteReceipt Receipt { get; }

    /// <summary>
    /// The final post-acquisition resolution of both publisher rights channels for this exact
    /// selected manifestation. Null only on lower-layer synthetic populations constructed through
    /// the legacy internal test door; terminal builders must refuse such a population.
    /// </summary>
    public LuxembourgRightsChannelResolution? RightsResolution { get; }
}

/// <summary>
/// The complete held-body subset of one verified Luxembourg corpus record set, with each member
/// bound to the address and exact format selected by that same acquisition run.
/// </summary>
public sealed class LuxembourgHeldBodyDerivationPopulation
{
    private LuxembourgHeldBodyDerivationPopulation(
        VerifiedCorpusRecordSet corpusRecordSet,
        IReadOnlyList<LuxembourgHeldBodyDerivationInput> inputs)
    {
        CorpusRecordSet = corpusRecordSet;
        Inputs = Array.AsReadOnly(inputs.ToArray());
    }

    public VerifiedCorpusRecordSet CorpusRecordSet { get; }

    public IReadOnlyList<LuxembourgHeldBodyDerivationInput> Inputs { get; }

    internal static LuxembourgHeldBodyDerivationPopulation? TryCreate(
        VerifiedCorpusRecordSet corpusRecordSet,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> outcomesByOrdinal,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgSelectedDocumentFetch> selectedFetchesByObject,
        out LuxembourgHeldBodyDerivationPopulationRefusal refusal,
        out string? detail) => TryCreateCore(
            corpusRecordSet,
            outcomesByOrdinal,
            selectedFetchesByObject,
            finalResolution: null,
            requireFinalRights: false,
            out refusal,
            out detail);

    /// <summary>
    /// Adapter-only production door. It derives the rights binding from the final resolved
    /// observations; no caller supplies a manifestation-to-rights map.
    /// </summary>
    internal static LuxembourgHeldBodyDerivationPopulation? TryCreateWithFinalRights(
        VerifiedCorpusRecordSet corpusRecordSet,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> outcomesByOrdinal,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgSelectedDocumentFetch> selectedFetchesByObject,
        LuxembourgProfileResolution.Resolved finalResolution,
        out LuxembourgHeldBodyDerivationPopulationRefusal refusal,
        out string? detail) => TryCreateCore(
            corpusRecordSet,
            outcomesByOrdinal,
            selectedFetchesByObject,
            finalResolution,
            requireFinalRights: true,
            out refusal,
            out detail);

    private static LuxembourgHeldBodyDerivationPopulation? TryCreateCore(
        VerifiedCorpusRecordSet corpusRecordSet,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> outcomesByOrdinal,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgSelectedDocumentFetch> selectedFetchesByObject,
        LuxembourgProfileResolution.Resolved? finalResolution,
        bool requireFinalRights,
        out LuxembourgHeldBodyDerivationPopulationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(corpusRecordSet);
        ArgumentNullException.ThrowIfNull(outcomesByOrdinal);
        ArgumentNullException.ThrowIfNull(selectedFetchesByObject);
        if (requireFinalRights)
        {
            ArgumentNullException.ThrowIfNull(finalResolution);
        }
        refusal = LuxembourgHeldBodyDerivationPopulationRefusal.None;
        detail = null;

        var recordsByOrdinal = corpusRecordSet.Set.Records.ToDictionary(static record => record.ObjectOrdinal);
        foreach (var pair in outcomesByOrdinal.OrderBy(static pair => pair.Key))
        {
            if (pair.Value.Receipt is null)
            {
                continue;
            }

            if (!recordsByOrdinal.TryGetValue(pair.Key, out var record))
            {
                refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldOutcomeHasNoRecord;
                detail = pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return null;
            }

            if (record.Body.Kind != CorpusBodyRecordKind.Held)
            {
                refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldOutcomeRecordIsNotHeld;
                detail = pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return null;
            }
        }

        var inputs = new List<LuxembourgHeldBodyDerivationInput>();
        foreach (var record in corpusRecordSet.Set.Records.Where(
                     static record => record.Body.Kind == CorpusBodyRecordKind.Held))
        {
            if (!outcomesByOrdinal.TryGetValue(record.ObjectOrdinal, out var outcome) ||
                outcome.Receipt is not { } receipt)
            {
                refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldRecordHasNoHeldOutcome;
                detail = record.ObjectOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return null;
            }

            if (record.Body.Receipt != receipt)
            {
                refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldReceiptMismatch;
                detail = record.ObjectOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return null;
            }

            if (!selectedFetchesByObject.TryGetValue(record.ObjectRef, out var selectedFetch))
            {
                refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldRecordHasNoSelectedAddress;
                detail = record.ObjectRef.PublisherUri;
                return null;
            }

            if (!string.Equals(
                    record.ObjectRef.PublisherUri,
                    selectedFetch.WemiCandidate.RootIri,
                    StringComparison.Ordinal))
            {
                refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldRecordSelectedIdentityMismatch;
                detail = record.ObjectRef.PublisherUri;
                return null;
            }

            LuxembourgRightsChannelResolution? rightsResolution = null;
            if (requireFinalRights)
            {
                var resources = finalResolution!.Resources
                    .Where(resource => resource.ObjectRef == record.ObjectRef)
                    .ToArray();
                var matches = resources
                    .SelectMany(static resource => resource.BodyJoin.Candidates)
                    .Where(candidate => candidate.WemiCandidate == selectedFetch.WemiCandidate)
                    .ToArray();
                if (matches.Length == 0)
                {
                    refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldRecordHasNoFinalRightsResolution;
                    detail = record.ObjectRef.PublisherUri;
                    return null;
                }

                if (resources.Length != 1 || matches.Length != 1)
                {
                    refusal = LuxembourgHeldBodyDerivationPopulationRefusal.HeldRecordFinalRightsResolutionAmbiguous;
                    detail = record.ObjectRef.PublisherUri;
                    return null;
                }

                rightsResolution = matches[0].RightsResolution;
            }

            inputs.Add(new LuxembourgHeldBodyDerivationInput(
                record,
                selectedFetch,
                record.Body.Receipt!,
                rightsResolution));
        }

        return new LuxembourgHeldBodyDerivationPopulation(corpusRecordSet, inputs);
    }
}
