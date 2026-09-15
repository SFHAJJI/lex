using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why a run's observed object identity set could not be retained.</summary>
public enum LuxembourgObservedObjectIdentitySetWriteRefusalKind
{
    /// <summary>
    /// Custody would not hold the set's bytes. The run derived its premise and could not keep it,
    /// which is a retention failure and is stated as one rather than passed over.
    /// </summary>
    [JsonStringEnumMemberName("identity_set_not_retained")]
    IdentitySetNotRetained = 1,
}

public sealed record LuxembourgObservedObjectIdentitySetWriteRefusal(
    LuxembourgObservedObjectIdentitySetWriteRefusalKind Kind, string Detail);

/// <summary>Retained, or refused. Never both, never neither.</summary>
public sealed class LuxembourgObservedObjectIdentitySetWriteResult
{
    private LuxembourgObservedObjectIdentitySetWriteResult(
        SourceArtifactRef? setRef,
        DurableBlobWriteReceipt? retainedSetReceipt,
        VerifiedLuxembourgObservedObjectIdentitySet? verifiedSet,
        LuxembourgObservedObjectIdentitySetWriteRefusal? refusal)
    {
        SetRef = setRef;
        RetainedSetReceipt = retainedSetReceipt;
        VerifiedSet = verifiedSet;
        Refusal = refusal;
    }

    /// <summary>The set's own domain-separated reference, for a retained result only.</summary>
    public SourceArtifactRef? SetRef { get; }

    /// <summary>
    /// The custody write receipt for the set's own bytes. Custody addresses blobs by
    /// <see cref="CustodyDigest.Of"/>, the plain SHA-256 of the bytes, while
    /// <see cref="SetRef"/> is the domain-separated digest -- two different values for the same
    /// bytes -- so the reference alone cannot locate them and the receipt has to be carried.
    /// </summary>
    public DurableBlobWriteReceipt? RetainedSetReceipt { get; }

    /// <summary>The set as it came back out of custody, checked against the bytes.</summary>
    public VerifiedLuxembourgObservedObjectIdentitySet? VerifiedSet { get; }

    public LuxembourgObservedObjectIdentitySetWriteRefusal? Refusal { get; }

    // Internal, not public: only the writer and the reader in this file produce these results, and
    // the factories check nulls, not agreement. A public door would let any caller pair verified set
    // A with reference B and receipt C and hand out something whose public shape says retained while
    // its three evidence legs describe different bytes. No external production caller exists, so the
    // door is closed rather than defended.
    internal static LuxembourgObservedObjectIdentitySetWriteResult Retained(
        SourceArtifactRef setRef,
        DurableBlobWriteReceipt retainedSetReceipt,
        VerifiedLuxembourgObservedObjectIdentitySet verifiedSet)
    {
        ArgumentNullException.ThrowIfNull(setRef);
        ArgumentNullException.ThrowIfNull(retainedSetReceipt);
        ArgumentNullException.ThrowIfNull(verifiedSet);
        return new LuxembourgObservedObjectIdentitySetWriteResult(
            setRef, retainedSetReceipt, verifiedSet, null);
    }

    internal static LuxembourgObservedObjectIdentitySetWriteResult Refused(
        LuxembourgObservedObjectIdentitySetWriteRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new LuxembourgObservedObjectIdentitySetWriteResult(null, null, null, refusal);
    }
}

/// <summary>
/// Writes a run's observed object identity set to custody and reopens it in the same call, so a
/// retained result is one whose bytes the store has already been made to produce back.
/// </summary>
public sealed class LuxembourgObservedObjectIdentitySetWriter
{
    private readonly ICustodyStore _custodyStore;

    public LuxembourgObservedObjectIdentitySetWriter(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<LuxembourgObservedObjectIdentitySetWriteResult> WriteAsync(
        SourceArtifactRef runIdentity,
        IReadOnlyList<LuxembourgResourceObservation> observations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runIdentity);
        ArgumentNullException.ThrowIfNull(observations);

        var set = LuxembourgObservedObjectIdentitySet.FromObservations(runIdentity, observations);
        using var buffer = new MemoryStream();
        var setCanonicalSha256 = LuxembourgObservedObjectIdentitySetCanonicalWriter.Write(buffer, set);
        var setBytes = buffer.ToArray();

        var (writeReceipt, holdFailure) = await CustodyHold
            .TryHoldAsync(_custodyStore, setBytes, cancellationToken)
            .ConfigureAwait(false);
        if (writeReceipt is null)
        {
            return LuxembourgObservedObjectIdentitySetWriteResult.Refused(
                new LuxembourgObservedObjectIdentitySetWriteRefusal(
                    LuxembourgObservedObjectIdentitySetWriteRefusalKind.IdentitySetNotRetained,
                    // Never null here: CustodyHold.TryHoldAsync documents its pair as never both and
                    // never neither, so no receipt means a failure string.
                    holdFailure!));
        }

        // Read for the BYTES the verification below parses. The hold already proved the store can
        // reproduce them at this digest; this call is how they are obtained, not a second proof.
        var reopenedBytes = await CustodyRestore
            .ReadByDigestCheckedAsync(_custodyStore, writeReceipt.Reference.ContentSha256, cancellationToken)
            .ConfigureAwait(false);

        var setArtifactRef = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", setCanonicalSha256);
        var verifiedSet = VerifiedLuxembourgObservedObjectIdentitySet
            .ParseAndVerify(setArtifactRef, reopenedBytes.Span);

        return LuxembourgObservedObjectIdentitySetWriteResult.Retained(
            setArtifactRef, writeReceipt, verifiedSet);
    }
}

/// <summary>Why a retained observed object identity set could not be reopened from custody.</summary>
public enum LuxembourgObservedObjectIdentitySetReadRefusalKind
{
    /// <summary>
    /// Custody holds nothing at the receipt's own digest, or hands back bytes that are not the ones
    /// that digest names. Either way the run's premise is not there to reopen.
    /// </summary>
    [JsonStringEnumMemberName("custody_bytes_not_retained")]
    CustodyBytesNotRetained = 1,

    /// <summary>
    /// The store itself could not serve the read. Distinct from
    /// <see cref="CustodyBytesNotRetained"/> on purpose: "the evidence is gone" and "the shelf could
    /// not be opened right now" are different facts, and collapsing them would let a transient
    /// outage read as a retention failure.
    /// </summary>
    [JsonStringEnumMemberName("custody_unavailable")]
    CustodyUnavailable = 2,

    /// <summary>
    /// The bytes were obtained, and they are not this set: they do not carry
    /// <c>setRef</c>'s own domain-separated digest, or they are not the exact canonical form of the
    /// set they parse into. This is the refusal a receipt paired with another run's reference earns.
    /// </summary>
    [JsonStringEnumMemberName("retained_bytes_are_not_this_set")]
    RetainedBytesAreNotThisSet = 3,

    /// <summary>
    /// The bytes are a valid identity set and they are another run's. Separate from
    /// <see cref="RetainedBytesAreNotThisSet"/> because the two are different facts: those bytes
    /// are not this artifact, these bytes are this artifact but not this run's premise. Without
    /// this refusal a caller could reopen any run's set and present it as the basis of theirs,
    /// which the artifact's own RunIdentity field was there to prevent and nothing enforced.
    /// </summary>
    [JsonStringEnumMemberName("retained_set_is_for_another_run")]
    RetainedSetIsForAnotherRun = 4,
}

public sealed record LuxembourgObservedObjectIdentitySetReadRefusal(
    LuxembourgObservedObjectIdentitySetReadRefusalKind Kind, string Detail);

/// <summary>Reopened from custody, or refused. Never both, never neither.</summary>
public sealed class LuxembourgObservedObjectIdentitySetReadResult
{
    private LuxembourgObservedObjectIdentitySetReadResult(
        VerifiedLuxembourgObservedObjectIdentitySet? verifiedSet,
        LuxembourgObservedObjectIdentitySetReadRefusal? refusal)
    {
        VerifiedSet = verifiedSet;
        Refusal = refusal;
    }

    public VerifiedLuxembourgObservedObjectIdentitySet? VerifiedSet { get; }

    public LuxembourgObservedObjectIdentitySetReadRefusal? Refusal { get; }

    internal static LuxembourgObservedObjectIdentitySetReadResult Reopened(
        VerifiedLuxembourgObservedObjectIdentitySet verifiedSet)
    {
        ArgumentNullException.ThrowIfNull(verifiedSet);
        return new LuxembourgObservedObjectIdentitySetReadResult(verifiedSet, null);
    }

    internal static LuxembourgObservedObjectIdentitySetReadResult Refused(
        LuxembourgObservedObjectIdentitySetReadRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new LuxembourgObservedObjectIdentitySetReadResult(null, refusal);
    }
}

/// <summary>
/// The post-run door onto a run's observed object identity set: the premise every admission on
/// <see cref="LuxembourgProductionScopeReductionEvidenceResolver"/> rested on, obtained again from
/// custody by a later party.
/// </summary>
/// <remarks>
/// BOTH INPUTS ARE CHECKED AGAINST THE BYTES, NEVER AGAINST EACH OTHER. The receipt decides which
/// bytes are fetched and <see cref="CustodyRestore.ReadByDigestCheckedAsync"/> proves they carry its
/// digest; the reference then has to be the digest those same bytes actually produce, and the parsed
/// set has to re-serialize to them exactly. A caller who pairs one run's receipt with another run's
/// reference gets <see cref="LuxembourgObservedObjectIdentitySetReadRefusalKind.RetainedBytesAreNotThisSet"/>,
/// not a set. There is no parameter here through which a caller could supply the identities the
/// bytes are not required to prove.
/// </remarks>
public sealed class LuxembourgObservedObjectIdentitySetReader
{
    private readonly ICustodyStore _custodyStore;

    public LuxembourgObservedObjectIdentitySetReader(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    /// <param name="expectedRunIdentity">
    /// The run whose premise the caller is asking for. Required, not optional: a reader that let a
    /// caller omit it would hand back whatever set the bytes happened to be, and the artifact's
    /// RunIdentity would be decoration.
    /// </param>
    public async Task<LuxembourgObservedObjectIdentitySetReadResult> ReadAsync(
        DurableBlobWriteReceipt retainedSetReceipt,
        SourceArtifactRef setRef,
        SourceArtifactRef expectedRunIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retainedSetReceipt);
        ArgumentNullException.ThrowIfNull(setRef);
        ArgumentNullException.ThrowIfNull(expectedRunIdentity);

        ReadOnlyMemory<byte> retained;
        try
        {
            retained = await CustodyRestore
                .ReadByDigestCheckedAsync(
                    _custodyStore, retainedSetReceipt.Reference.ContentSha256, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CustodyIntegrityException exception)
        {
            return LuxembourgObservedObjectIdentitySetReadResult.Refused(
                new LuxembourgObservedObjectIdentitySetReadRefusal(
                    LuxembourgObservedObjectIdentitySetReadRefusalKind.CustodyBytesNotRetained,
                    exception.Message));
        }
        catch (Exception exception)
            when (exception is CustodyRequiredException or CustodyPolicyException)
        {
            return LuxembourgObservedObjectIdentitySetReadResult.Refused(
                new LuxembourgObservedObjectIdentitySetReadRefusal(
                    LuxembourgObservedObjectIdentitySetReadRefusalKind.CustodyUnavailable,
                    exception.Message));
        }

        VerifiedLuxembourgObservedObjectIdentitySet verified;
        try
        {
            verified = VerifiedLuxembourgObservedObjectIdentitySet.ParseAndVerify(setRef, retained.Span);
        }
        catch (ArgumentException exception)
        {
            // ParseAndVerify states every one of its own rejections as an ArgumentException naming
            // the bytes. Catching that one type is catching exactly its verdict, not a net thrown
            // over unrelated failures -- a null argument cannot reach here, both are checked above.
            return LuxembourgObservedObjectIdentitySetReadResult.Refused(
                new LuxembourgObservedObjectIdentitySetReadRefusal(
                    LuxembourgObservedObjectIdentitySetReadRefusalKind.RetainedBytesAreNotThisSet,
                    exception.Message));
        }

        // Checked against the bytes, like everything else here: the run identity being compared is
        // the one the retained bytes carry, not one the caller also supplied alongside them.
        if (!string.Equals(
                verified.Set.RunIdentity.Sha256, expectedRunIdentity.Sha256, StringComparison.Ordinal)
            || !string.Equals(
                verified.Set.RunIdentity.ResourceId,
                expectedRunIdentity.ResourceId,
                StringComparison.Ordinal))
        {
            return LuxembourgObservedObjectIdentitySetReadResult.Refused(
                new LuxembourgObservedObjectIdentitySetReadRefusal(
                    LuxembourgObservedObjectIdentitySetReadRefusalKind.RetainedSetIsForAnotherRun,
                    $"expected run {expectedRunIdentity.Sha256}; the retained set names "
                    + verified.Set.RunIdentity.Sha256));
        }

        return LuxembourgObservedObjectIdentitySetReadResult.Reopened(verified);
    }
}
