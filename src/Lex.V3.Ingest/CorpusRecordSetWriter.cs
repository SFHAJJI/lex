using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest;

/// <summary>
/// D1-06b's own acquisition door: the one supplyable outcome a caller may hand this writer for an
/// object whose manifest body axis is <see cref="ScopeDisposition.AcceptedSelected"/>, in place of
/// the default <see cref="CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired"/> D1-06b itself
/// always produces. Exactly two shapes, never a boolean flag standing in for either: a real held
/// receipt (<see cref="Held"/>), or a real refusal cause (<see cref="Refused"/>) from the closed
/// <see cref="CorpusAcquisitionRefusalReason"/> vocabulary. D1-06b's own writer never constructs one
/// of these (it has no fetch to report); this type exists so a future caller (D1-06c, once a real
/// document-body fetch exists) can hand this writer a real outcome per object without this writer's
/// own code needing to change -- <see cref="CorpusRecordBuilder.BuildRecords"/> only ever reads this
/// type's own public surface, never decides for itself whether or how to fetch.
/// </summary>
public sealed record CorpusAcquisitionOutcome
{
    private CorpusAcquisitionOutcome(
        DurableBlobWriteReceipt? receipt,
        CorpusAcquisitionRefusalReason? refusal)
    {
        Receipt = receipt;
        Refusal = refusal;
    }

    /// <summary>The real receipt, for <see cref="Held"/> only.</summary>
    public DurableBlobWriteReceipt? Receipt { get; }

    /// <summary>The real refusal cause, for <see cref="Refused"/> only.</summary>
    public CorpusAcquisitionRefusalReason? Refusal { get; }

    /// <summary>
    /// A real acquired body. Decision 80's own proof surface: the only way this writer's public API
    /// can ever produce <see cref="CorpusBodyRecordKind.Held"/> is a caller passing an actual,
    /// non-null <see cref="DurableBlobWriteReceipt"/> here -- there is no boolean or unchecked path
    /// onto that outcome anywhere in this type or in <see cref="CorpusRecordBuilder"/>.
    /// </summary>
    public static CorpusAcquisitionOutcome Held(DurableBlobWriteReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new CorpusAcquisitionOutcome(receipt, null);
    }

    /// <summary>A real, named acquisition refusal.</summary>
    public static CorpusAcquisitionOutcome Refused(CorpusAcquisitionRefusalReason refusal)
    {
        if (!Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new CorpusAcquisitionOutcome(null, refusal);
    }
}

/// <summary>
/// Which of the three shapes one manifest row ended up as in a built corpus record: mirrors
/// <see cref="CorpusBodyRecordKind"/> exactly, reused here under this writer's own name rather than
/// forcing a completion reader to import <c>Lex.V3.Contracts.Source.Corpus</c>'s own enum just to
/// read a summary.
/// </summary>
public enum CorpusRecordOutcomeKind
{
    Held = 1,
    NotHeld = 2,
    PendingAcquisition = 3,
}

/// <summary>
/// One manifest row's own outcome in a built <see cref="CorpusRecordSet"/>: which object, which
/// ordinal, which of the three body shapes it ended up as, and (for the two non-held shapes) the
/// exact typed reason -- named directly rather than requiring a reader to re-derive it from the
/// underlying <see cref="CorpusRecord.Body"/>. For <see cref="CorpusRecordOutcomeKind.PendingAcquisition"/>
/// whose own <see cref="PendingAcquisitionReasonKind"/> is
/// <see cref="CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused"/>, <see cref="RefusalCause"/>
/// additionally names the exact one of the fourteen <see cref="CorpusAcquisitionRefusalReason"/>
/// values that caused it -- named directly here too, rather than requiring a reader to reach back
/// into <see cref="CorpusRecord.Body"/>'s own <see cref="CorpusBodyPendingAcquisitionReason.Refusal"/>
/// for the one fact this entry otherwise only reports the shape of. <see langword="null"/> for every
/// other combination of <see cref="Kind"/> and <see cref="PendingAcquisitionReasonKind"/>.
/// </summary>
public sealed record CorpusRecordOutcomeEntry(
    SourceObjectRef ObjectRef,
    int ObjectOrdinal,
    CorpusRecordOutcomeKind Kind,
    ScopeDisposition? NotHeldReason,
    CorpusBodyPendingAcquisitionReasonKind? PendingAcquisitionReasonKind,
    CorpusAcquisitionRefusalReason? RefusalCause);

/// <summary>
/// Whether a built record set covers its whole manifest. Closed at two.
/// </summary>
public enum CorpusRecordSetCompletionState
{
    Complete = 1,
    Partial = 2,
}

/// <summary>
/// D1-06b's own typed completion: "complete" means every manifest row produced exactly one corpus
/// record, whatever its kind -- never "every object's body was fetched", which is not this slice's
/// job and, since D1-06b has no fetch capability, cannot be true for any accepted-body row today.
/// </summary>
/// <remarks>
/// <see cref="CorpusRecordBuilder.BuildRecords"/> is total over its manifest's own observed-object
/// count: it either returns exactly one record per ordinal or throws (a malformed manifest, or an
/// acquisition outcome naming an ordinal it cannot apply to). So <see cref="State"/> is
/// <see cref="CorpusRecordSetCompletionState.Complete"/> for every completion this writer's own
/// <see cref="CorpusRecordSetWriter.WriteAsync"/> can actually produce today; <see cref="Partial"/>
/// is modelled honestly rather than omitted, reserved for a future resumable or batched builder this
/// slice does not add.
/// </remarks>
public sealed record CorpusRecordSetCompletion(
    CorpusRecordSetCompletionState State,
    int ExpectedObjectCount,
    IReadOnlyList<CorpusRecordOutcomeEntry> Entries);

/// <summary>
/// The pure per-manifest record builder: item 1 of D1-06b. Consumes one
/// <see cref="ScopeManifest"/> and, for every observed object, produces exactly one
/// <see cref="CorpusRecord"/> -- <see cref="CorpusBodyRecord.NotHeld"/> for the manifest's three
/// non-accepted body dispositions, or, for an accepted body,
/// <see cref="CorpusBodyRecord.PendingAcquisition"/> with
/// <see cref="CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired"/> unless the caller's own
/// <see cref="CorpusAcquisitionOutcome"/> door supplies a real outcome for that ordinal. Never
/// fetches anything, never calls any HTTP machinery: there is none to call (the scope ruling this
/// writer implements). No custody, no canonical set write, no floor check -- see
/// <see cref="CorpusRecordSetWriter"/> for those.
/// </summary>
public static class CorpusRecordBuilder
{
    public static IReadOnlyList<CorpusRecord> BuildRecords(
        ScopeManifest manifest,
        SourceArtifactRef manifestRef,
        SourceArtifactRef runIdentity,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? acquisitionOutcomesByOrdinal = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifestRef);
        ArgumentNullException.ThrowIfNull(runIdentity);

        var objectCount = manifest.ObservedObjects.Count;
        var recordDispositions = DispositionsByOrdinal(manifest, ScopeAxis.Record, objectCount);
        var bodyDispositions = DispositionsByOrdinal(manifest, ScopeAxis.Body, objectCount);
        var relationDispositions = DispositionsByOrdinal(manifest, ScopeAxis.Relation, objectCount);
        var supportingDocumentDispositions = DispositionsByOrdinal(
            manifest, ScopeAxis.SupportingDocument, objectCount);

        if (acquisitionOutcomesByOrdinal is not null)
        {
            foreach (var ordinal in acquisitionOutcomesByOrdinal.Keys)
            {
                if (ordinal < 0 || ordinal >= objectCount)
                {
                    throw new ArgumentException(
                        "An acquisition outcome names an object ordinal outside the manifest.",
                        nameof(acquisitionOutcomesByOrdinal));
                }

                if (bodyDispositions[ordinal] != ScopeDisposition.AcceptedSelected)
                {
                    throw new ArgumentException(
                        "An acquisition outcome may only be supplied for an accepted-selected " +
                        "body.",
                        nameof(acquisitionOutcomesByOrdinal));
                }
            }
        }

        var records = new CorpusRecord[objectCount];
        for (var ordinal = 0; ordinal < objectCount; ordinal++)
        {
            var objectRef = manifest.ObservedObjects[ordinal].ObjectRef;
            var bodyDisposition = bodyDispositions[ordinal];
            CorpusBodyRecord body;
            if (bodyDisposition == ScopeDisposition.AcceptedSelected)
            {
                CorpusAcquisitionOutcome? outcome = null;
                acquisitionOutcomesByOrdinal?.TryGetValue(ordinal, out outcome);
                body = outcome switch
                {
                    { Receipt: { } receipt } => CorpusBodyRecord.Held(receipt),
                    { Refusal: { } refusal } => CorpusBodyRecord.PendingAcquisition(
                        CorpusBodyPendingAcquisitionReason.AcquisitionRefused(refusal)),
                    _ => CorpusBodyRecord.PendingAcquisition(
                        CorpusBodyPendingAcquisitionReason.NotYetAcquired()),
                };
            }
            else
            {
                body = CorpusBodyRecord.NotHeld(bodyDisposition);
            }

            records[ordinal] = new CorpusRecord(
                CorpusRecordSchemaIds.Record,
                objectRef,
                ordinal,
                recordDispositions[ordinal],
                bodyDisposition,
                relationDispositions[ordinal],
                supportingDocumentDispositions[ordinal],
                body,
                manifestRef,
                runIdentity);
        }

        return Array.AsReadOnly(records);
    }

    /// <summary>
    /// Inverts <paramref name="manifest"/>'s own <c>Accounting</c> partitions (the sixteen
    /// axis/disposition sets <c>ScopeManifestCanonicalWriter</c> already proves exactly cover every
    /// ordinal, per axis, exactly once) into a per-ordinal lookup for one axis, so this builder never
    /// needs internal access to <c>ScopeReducer</c> or <c>ScopeValidation</c> to recover what a row's
    /// own four axis outcomes were -- everything read here is <see cref="ScopeManifest"/>'s already
    /// public surface.
    /// </summary>
    private static ScopeDisposition[] DispositionsByOrdinal(
        ScopeManifest manifest, ScopeAxis axis, int objectCount)
    {
        var found = new ScopeDisposition?[objectCount];
        foreach (var set in manifest.Accounting)
        {
            if (set.Axis != axis)
            {
                continue;
            }

            foreach (var ordinal in set.ObjectOrdinals)
            {
                if (ordinal < 0 || ordinal >= objectCount)
                {
                    throw new ArgumentException(
                        $"The manifest's {axis} accounting names an ordinal outside its observed " +
                        "objects.",
                        nameof(manifest));
                }

                if (found[ordinal] is not null)
                {
                    throw new ArgumentException(
                        $"The manifest's {axis} accounting names ordinal {ordinal} in more than " +
                        "one disposition.",
                        nameof(manifest));
                }

                found[ordinal] = set.Disposition;
            }
        }

        var resolved = new ScopeDisposition[objectCount];
        for (var ordinal = 0; ordinal < objectCount; ordinal++)
        {
            resolved[ordinal] = found[ordinal] ?? throw new ArgumentException(
                $"The manifest's {axis} accounting does not cover ordinal {ordinal}.",
                nameof(manifest));
        }

        return resolved;
    }
}

/// <summary>Why <see cref="CorpusRecordSetWriter.WriteAsync"/> refused to complete a run. Closed at one.</summary>
public enum CorpusRecordSetWriteRefusalKind
{
    /// <summary>The set was written, but the store enforced no retention floor on it.</summary>
    RecordSetNotHeld = 1,
}

public sealed record CorpusRecordSetWriteRefusal(CorpusRecordSetWriteRefusalKind Kind, string Detail);

/// <summary>Written under the run's own floor and reopened, or refused. Never both, never neither.</summary>
public sealed class CorpusRecordSetWriteResult
{
    private CorpusRecordSetWriteResult(
        SourceArtifactRef? setRef,
        VerifiedCorpusRecordSet? verifiedSet,
        CorpusRecordSetCompletion? completion,
        CorpusRecordSetWriteRefusal? refusal)
    {
        SetRef = setRef;
        VerifiedSet = verifiedSet;
        Completion = completion;
        Refusal = refusal;
    }

    /// <summary>The reopened set's own artifact reference, for a written result only.</summary>
    public SourceArtifactRef? SetRef { get; }

    /// <summary>
    /// The reopened, checked set -- reopened through <see cref="VerifiedCorpusRecordSet.ParseAndVerify"/>
    /// against the exact bytes the custody store returned, never the in-memory set this writer built,
    /// for a written result only.
    /// </summary>
    public VerifiedCorpusRecordSet? VerifiedSet { get; }

    public CorpusRecordSetCompletion? Completion { get; }

    public CorpusRecordSetWriteRefusal? Refusal { get; }

    public static CorpusRecordSetWriteResult Written(
        SourceArtifactRef setRef,
        VerifiedCorpusRecordSet verifiedSet,
        CorpusRecordSetCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(setRef);
        ArgumentNullException.ThrowIfNull(verifiedSet);
        ArgumentNullException.ThrowIfNull(completion);
        return new CorpusRecordSetWriteResult(setRef, verifiedSet, completion, null);
    }

    public static CorpusRecordSetWriteResult Refused(CorpusRecordSetWriteRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new CorpusRecordSetWriteResult(null, null, null, refusal);
    }
}

/// <summary>
/// D1-06b items 4-6: builds one run's whole <see cref="CorpusRecordSet"/> from a
/// <see cref="ScopeManifest"/> via <see cref="CorpusRecordBuilder"/>, canonically writes it under
/// this run's own required floor (<see cref="CustodyClass.NightlyFloor90d"/>, exactly the constant
/// and floor-check <c>EuQueryExecutionAdapter</c> and <c>LuxembourgQueryExecutionAdapter</c> already
/// require for a scope manifest's own custody write, reused rather than reinvented), then reopens it
/// through <see cref="VerifiedCorpusRecordSet.ParseAndVerify"/> the same way those two adapters
/// reopen their own manifest after writing it.
/// </summary>
public sealed class CorpusRecordSetWriter
{
    private readonly ICustodyStore _custodyStore;

    public CorpusRecordSetWriter(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<CorpusRecordSetWriteResult> WriteAsync(
        ScopeManifest manifest,
        SourceArtifactRef manifestRef,
        SourceArtifactRef runIdentity,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? acquisitionOutcomesByOrdinal,
        CancellationToken cancellationToken)
    {
        var records = CorpusRecordBuilder.BuildRecords(
            manifest, manifestRef, runIdentity, acquisitionOutcomesByOrdinal);
        var completion = BuildCompletion(manifest, records);

        var set = new CorpusRecordSet(CorpusRecordSetSchemaIds.Set, manifestRef, runIdentity, records);
        using var buffer = new MemoryStream();
        var setCanonicalSha256 = CorpusRecordSetCanonicalWriter.Write(buffer, set);
        var setBytes = buffer.ToArray();

        var writeReceipt = await _custodyStore.CreateAsync(
                setBytes, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);
        if (CustodyMembershipClassifier.Classify(writeReceipt) != CustodyMembership.Floored)
        {
            return CorpusRecordSetWriteResult.Refused(
                new CorpusRecordSetWriteRefusal(
                    CorpusRecordSetWriteRefusalKind.RecordSetNotHeld,
                    "The corpus record set was written but the store enforced no retention floor " +
                    "on it."));
        }

        var reopenedBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, writeReceipt.Reference.ContentSha256, cancellationToken)
            .ConfigureAwait(false);
        var setArtifactRef = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", setCanonicalSha256);
        var verifiedSet = VerifiedCorpusRecordSet.ParseAndVerify(setArtifactRef, reopenedBytes.Span);

        return CorpusRecordSetWriteResult.Written(setArtifactRef, verifiedSet, completion);
    }

    private static CorpusRecordSetCompletion BuildCompletion(
        ScopeManifest manifest, IReadOnlyList<CorpusRecord> records)
    {
        var expected = manifest.ObservedObjects.Count;
        var entries = new CorpusRecordOutcomeEntry[records.Count];
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var kind = record.Body.Kind switch
            {
                CorpusBodyRecordKind.Held => CorpusRecordOutcomeKind.Held,
                CorpusBodyRecordKind.NotHeld => CorpusRecordOutcomeKind.NotHeld,
                CorpusBodyRecordKind.PendingAcquisition => CorpusRecordOutcomeKind.PendingAcquisition,
                _ => throw new InvalidOperationException("Unknown corpus body record kind."),
            };
            entries[index] = new CorpusRecordOutcomeEntry(
                record.ObjectRef,
                record.ObjectOrdinal,
                kind,
                record.Body.NotHeldReason,
                record.Body.PendingAcquisitionReason?.Kind,
                record.Body.PendingAcquisitionReason?.Refusal);
        }

        var state = records.Count == expected
            ? CorpusRecordSetCompletionState.Complete
            : CorpusRecordSetCompletionState.Partial;
        return new CorpusRecordSetCompletion(state, expected, Array.AsReadOnly(entries));
    }
}
