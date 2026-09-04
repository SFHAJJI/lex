using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest;

/// <summary>
/// Writes bytes into custody and proves the store really holds them, for the acquisition paths of
/// both publishers. The one place that decides what "held" means.
/// </summary>
/// <remarks>
/// RULING lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8, interpreting Decision 71.
/// The durability floor conflated RETENTION with IMMUTABILITY, the same shape in which the rights
/// axis conflated holding with serving. A store that wrote the bytes, verified the digest and
/// honestly declares <see cref="CustodyProtection.NotEnforced"/> did not fail; it held the bytes
/// under a weaker guarantee and said so. Refusing it as a custody failure meant no body could be
/// held anywhere outside Azure, which stopped both acceptance canaries at a wall that had nothing
/// to do with the publisher or the route.
/// <para>
/// So the membership class is no longer a gate here. It is RECORDED: the receipt travels onto
/// <c>CorpusBodyRecord.Held</c>, which derives its own <c>Floor</c> from that receipt's policy
/// evidence and serialises it, so every held record already says under which guarantee it is held.
/// Both <see cref="CustodyMembership.Floored"/> and
/// <see cref="CustodyMembership.RetainedUnenforced"/> are Held. What the floor now gates is what
/// actually depends on immutability, the cut release and any answer served as checkable evidence,
/// not whether a body may be stored at all.
/// </para>
/// <para>
/// THE DANGEROUS DIRECTION, and the reason this is a method rather than a deleted line. "We stored
/// it under a weaker guarantee" and "we failed to store it" must stay different facts. A write that
/// errored, or bytes that do not reopen at their own digest, is a REAL custody failure and stays a
/// typed refusal; it must never quietly reappear as a weaker class. That is why this method proves
/// the hold by reopening the receipt's own digest through the checked reader rather than trusting
/// the write call, and why every failure path returns no receipt at all instead of a receipt with a
/// softer label.
/// </para>
/// <para>
/// EXACTLY WHAT THE REOPEN PROVES, stated narrowly because it was nearly removed as a duplicate of
/// the write's own readback. RULING lex-event-20260904T223559409Z-940e6f5dd5f540598920f6bf7849da47. It
/// proves the object is DISCOVERABLE BY CONTENT ADDRESS ALONE, through
/// <see cref="CustodyRestore.ReadByDigestCheckedAsync"/>, which is the reader every downstream
/// consumer uses. <see cref="ICustodyStore.CreateAsync"/>'s own obligation is a different one: its
/// readback goes through the reference the write just produced, one class-qualified path. A store
/// can satisfy that and still fail to answer for the object by digest, and a held record for a body
/// no consumer can find is the user finding no text. The two are not duplicates, and this one is
/// the property acquisition actually depends on.
/// </para>
/// <para>
/// WHAT IT DOES NOT PROVE. Both readbacks are WRITE TIME facts. Under
/// <see cref="CustodyMembership.RetainedUnenforced"/> the bytes can change afterwards and neither
/// this method nor the store's own obligation proves or claims otherwise. Durability on an
/// unenforced store is exactly what the retention floor gates at the release, in
/// <c>AbsenceCut.TryCreateComplete</c>, and nothing here should be read as standing in for it.
/// </para>
/// <para>
/// ITS COST, recorded rather than hidden: this reopen reads the whole object a second time, 5.5 MB
/// for the Code civil. That is an ACQUISITION cost, paid once per held object on a path that has
/// already spent a publisher round trip on the same bytes, and it buys the one property above.
/// </para>
/// </remarks>
internal static class CustodyHold
{
    /// <summary>
    /// The receipt for bytes this store demonstrably holds, or a typed failure describing why the
    /// hold could not be proven. Never both, never neither.
    /// </summary>
    internal static async Task<(DurableBlobWriteReceipt? Receipt, string? Failure)> TryHoldAsync(
        ICustodyStore custodyStore,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);

        DurableBlobWriteReceipt receipt;
        try
        {
            receipt = await custodyStore
                .CreateAsync(bytes, CustodyClass.NightlyFloor90d, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is CustodyRequiredException or CustodyIntegrityException or
                CustodyPolicyException or IOException or UnauthorizedAccessException)
        {
            return (null, $"the custody write failed: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            // The proof, not a formality. ReadByDigestCheckedAsync re-reads by the receipt's own
            // content address and refuses unless the returned bytes hash to it, so a store that
            // accepted a write and cannot produce those exact bytes again fails HERE rather than
            // being recorded as held. A comparison of the reopened bytes against the originals
            // afterwards would only re-derive what that digest check already established.
            _ = await CustodyRestore
                .ReadByDigestCheckedAsync(custodyStore, receipt.Reference.ContentSha256, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is CustodyRequiredException or CustodyIntegrityException or
                CustodyPolicyException or IOException or UnauthorizedAccessException or
                KeyNotFoundException)
        {
            return (
                null,
                "the custody write returned a receipt but the store could not reproduce those exact "
                + $"bytes at their own digest: {exception.GetType().Name}: {exception.Message}");
        }

        return (receipt, null);
    }
}
