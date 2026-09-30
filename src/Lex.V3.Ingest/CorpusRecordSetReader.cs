using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;

namespace Lex.V3.Ingest;

/// <summary>Why a retained corpus/6 record set could not be reopened from custody.</summary>
public enum CorpusRecordSetReadRefusalKind
{
    /// <summary>
    /// Custody holds nothing at the receipt's own digest, or hands back bytes that are not the ones
    /// that digest names. Either way the run's record set is not there to reopen, and this door says
    /// so rather than presenting a set it could not obtain.
    /// </summary>
    [JsonStringEnumMemberName("custody_bytes_not_retained")]
    CustodyBytesNotRetained = 1,

    /// <summary>
    /// The store itself could not serve the read. Distinct from
    /// <see cref="CustodyBytesNotRetained"/> on purpose: "the evidence is gone" and "the shelf could
    /// not be opened right now" are different facts about a corpus, and collapsing them would let a
    /// transient outage read as a retention failure.
    /// </summary>
    [JsonStringEnumMemberName("custody_unavailable")]
    CustodyUnavailable = 2,

    /// <summary>
    /// The bytes were obtained, and they are not this set: they do not carry
    /// <c>setRef</c>'s own domain-separated digest, or they are not the exact canonical typed
    /// representation of the set they parse into. This is the refusal a receipt paired with another
    /// run's reference earns.
    /// </summary>
    [JsonStringEnumMemberName("retained_bytes_are_not_this_set")]
    RetainedBytesAreNotThisSet = 3,
}

public sealed record CorpusRecordSetReadRefusal(CorpusRecordSetReadRefusalKind Kind, string Detail);

/// <summary>Reopened from custody, or refused. Never both, never neither.</summary>
public sealed class CorpusRecordSetReadResult
{
    private CorpusRecordSetReadResult(
        VerifiedCorpusRecordSet? verifiedSet,
        CorpusRecordSetReadRefusal? refusal)
    {
        VerifiedSet = verifiedSet;
        Refusal = refusal;
    }

    /// <summary>
    /// The reopened set, verified against the reference it was asked for and against its own exact
    /// canonical form, for a reopened result only. Holding one is the evidence that
    /// <see cref="VerifiedCorpusRecordSet.ParseAndVerifyStream"/> ran to completion over bytes custody
    /// actually returned -- never over an in-memory set this process still happened to be holding.
    /// </summary>
    public VerifiedCorpusRecordSet? VerifiedSet { get; }

    public CorpusRecordSetReadRefusal? Refusal { get; }

    public static CorpusRecordSetReadResult Reopened(VerifiedCorpusRecordSet verifiedSet)
    {
        ArgumentNullException.ThrowIfNull(verifiedSet);
        return new CorpusRecordSetReadResult(verifiedSet, null);
    }

    public static CorpusRecordSetReadResult Refused(CorpusRecordSetReadRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new CorpusRecordSetReadResult(null, refusal);
    }
}

/// <summary>
/// The post-run door onto a corpus/6 record set that some earlier run wrote:
/// <see cref="CorpusRecordSetWriter"/> reopens the bytes it has just written, inside the same call,
/// and until this type existed that was the only reopen anywhere in the line. A second, later,
/// independent party -- the other execution S3-A04 compares against, a reviewer, a rebuild -- had no
/// way to get the set back.
/// </summary>
/// <remarks>
/// <para>
/// The set reference carries the domain-separated digest of the complete canonical set. The
/// receipt supplies its storage address: inline canonical bytes for small or legacy sets, or a
/// root naming an ordered chunk closure for large sets. The reader verifies both storage and
/// canonical identity. A root receipt describes that root's protection, not an aggregate floor
/// for every object in the closure; this read result makes no aggregate retention claim.
/// </para>
/// <para>
/// BOTH INPUTS ARE CHECKED AGAINST THE BYTES, NEVER AGAINST EACH OTHER. The receipt decides which
/// storage object is fetched and <see cref="CustodyRestore.ReadByDigestCheckedAsync"/> proves it carries its
/// digest. Chunk roots also require a complete checked closure. The reference must match the
/// resulting canonical bytes, and the parsed
/// set has to re-serialize to them exactly. A caller who pairs one run's receipt with another run's
/// reference therefore gets <see cref="CorpusRecordSetReadRefusalKind.RetainedBytesAreNotThisSet"/>,
/// not a set. There is no parameter on this type through which a caller could supply a population,
/// a record, or a digest the bytes are not required to prove.
/// </para>
/// </remarks>
public sealed class CorpusRecordSetReader
{
    private readonly ICustodyStore _custodyStore;

    public CorpusRecordSetReader(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<CorpusRecordSetReadResult> ReadAsync(
        DurableBlobWriteReceipt retainedSetReceipt,
        SourceArtifactRef setRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retainedSetReceipt);
        ArgumentNullException.ThrowIfNull(setRef);

        try
        {
            var retained = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, retainedSetReceipt.Reference.ContentSha256, cancellationToken).ConfigureAwait(false);
            if (ChunkedDerivedArtifact.IsRoot(retained.Span))
            {
                var artifact = await ChunkedDerivedArtifact.OpenAsync(_custodyStore,
                    retainedSetReceipt.Reference.ContentSha256, CorpusRecordSetWriter.ChunkedKind,
                    cancellationToken).ConfigureAwait(false);
                if (artifact.CanonicalSha256 != setRef.Sha256)
                    return CorpusRecordSetReadResult.Refused(new CorpusRecordSetReadRefusal(
                        CorpusRecordSetReadRefusalKind.RetainedBytesAreNotThisSet,
                        "The retained chunk root names a different canonical corpus set."));
                using var chunks = artifact.OpenRead();
                return CorpusRecordSetReadResult.Reopened(
                    VerifiedCorpusRecordSet.ParseAndVerifyStream(setRef, chunks));
            }
            using var readback = MemoryMarshal.TryGetArray(retained, out var buffer)
                ? new MemoryStream(buffer.Array!, buffer.Offset, buffer.Count, writable: false)
                : new MemoryStream(retained.ToArray(), writable: false);
            return CorpusRecordSetReadResult.Reopened(
                VerifiedCorpusRecordSet.ParseAndVerifyStream(setRef, readback));
        }
        catch (CustodyIntegrityException exception)
        {
            return CorpusRecordSetReadResult.Refused(new CorpusRecordSetReadRefusal(
                CorpusRecordSetReadRefusalKind.CustodyBytesNotRetained, exception.Message));
        }
        catch (Exception exception)
            when (exception is CustodyRequiredException or CustodyPolicyException)
        {
            return CorpusRecordSetReadResult.Refused(new CorpusRecordSetReadRefusal(
                CorpusRecordSetReadRefusalKind.CustodyUnavailable, exception.Message));
        }
        catch (ArgumentException exception)
        {
            // ParseAndVerifyStream states every one of its own rejections as an ArgumentException naming
            // the bytes: the reference's digest, strict UTF-8, typed deserialization, and the exact
            // canonical round trip. Catching that one type is catching exactly its verdict, not a
            // net thrown over unrelated failures -- a null argument cannot reach here, both are
            // checked above.
            return CorpusRecordSetReadResult.Refused(new CorpusRecordSetReadRefusal(
                CorpusRecordSetReadRefusalKind.RetainedBytesAreNotThisSet, exception.Message));
        }
    }
}
