using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Absence;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>
/// Why <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> refused to hand back rows. Closed.
/// </summary>
public enum RepeatedEnumerationRowsOpenRefusal
{
    /// <summary>No refusal: the rows were admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The supplied page chain did not verify: a page body was not valid SPARQL Results JSON under
    /// the profile's dialect and projection, or the chain violated one of the invariants
    /// <see cref="EnumerationDeliveryComparison.VerifyPages"/> itself enforces (a page's cardinality
    /// does not bind the count it claims, a page exceeds its own row limit, a continuation cursor is
    /// missing or does not advance, or the final page does not satisfy the interpretation profile's
    /// terminal-page policy). This is the same strict parser and the same chain invariants that
    /// verified this family's delivery the first time; nothing here is a second, looser copy of
    /// either.
    /// </summary>
    [JsonStringEnumMemberName("page_chain_invalid")]
    PageChainInvalid = 1,

    /// <summary>
    /// The freshly reparsed row count does not equal
    /// <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>. The proof already established
    /// this count once, from evidence resolved and verified at mint time; this route only ever
    /// re-derives it from bytes the caller reopened just now, independently.
    /// </summary>
    [JsonStringEnumMemberName("delivered_row_count_mismatch")]
    DeliveredRowCountMismatch = 2,

    /// <summary>
    /// The freshly reparsed rows' canonical-key digest does not equal
    /// <see cref="AbsenceFamilyEnumerationProof.CanonicalKeyDigest"/>. This is the one check queue
    /// item 17 exists to add: before this door, no production path ever recomputed this digest from
    /// rows it parsed itself rather than trusting a caller's restatement of it.
    /// </summary>
    [JsonStringEnumMemberName("canonical_key_digest_mismatch")]
    CanonicalKeyDigestMismatch = 3,
}

/// <summary>
/// The one public door from a family's already-minted <see cref="AbsenceFamilyEnumerationProof"/>
/// and its own already-reopened page evidence back to typed, independently re-verified
/// <see cref="RepeatedEnumerationRow"/> data.
/// </summary>
/// <remarks>
/// <para>
/// Precedent: <c>VerifiedScopeManifest.ParseAndVerify</c> (Source/Scope/ScopeManifest.cs) - take
/// retained bytes plus a reference, re-derive and re-verify everything from scratch, refuse on any
/// disagreement, expose only the verified result. This door follows the same shape for a repeated
/// enumeration family instead of a scope manifest.
/// </para>
/// <para>
/// Before this door existed, <see cref="EnumerationDeliveryComparison.Create"/> was the only
/// production path that ever turned a family's delivered SPARQL-results-JSON bytes into typed rows,
/// and it deliberately never returned them: it parses both independent passes only long enough to
/// compute their counts and canonical digests, then discards the rows, because its job is proving
/// the two passes agree with each other, not handing data to a caller. <see
/// cref="AbsenceFamilyEnumerationProof"/> then carries forward only the reconciled
/// <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/> and
/// <see cref="AbsenceFamilyEnumerationProof.CanonicalKeyDigest"/> from that comparison, never the
/// rows themselves. So no production path anywhere could turn a family's proof plus its retained
/// bytes back into rows an adapter could build real domain observations from; an adapter reaching
/// for real data had to accept caller-supplied rows on faith, or duplicate the parser under a new
/// name, the "second parser" Decision 80 forbids.
/// </para>
/// <para>
/// This door closes that gap without either bad option. It takes bytes, never a store: the caller
/// reopens every page's query plan, query input and retained response body out of custody itself,
/// exactly as <see cref="Lex.V3.Contracts.Custody.CustodyRestore.ReadByDigestCheckedAsync"/> plus
/// each artifact's own <c>Identity.Validate</c> or <c>ParseAndVerify</c> door already require (the
/// same reopen <c>LuxembourgDeliveryEvidenceSet.ResolveOneAsync</c> performs today), and hands the
/// ordered result as <see cref="RepeatedEnumerationResolvedEvidence"/> per page to this door
/// alongside the family's own <see cref="AbsenceFamilyEnumerationProof"/>. Nothing in this file calls
/// <c>ICustodyStore</c>.
/// </para>
/// <para>
/// It reuses <see cref="EnumerationDeliveryComparison"/>'s own page-chain verification
/// (<c>VerifyPages</c>, which itself calls the same strict per-page parser <c>ParseRows</c>) and its
/// own digest computation (<c>Digest</c>), both promoted from private to internal so this
/// same-assembly, same-namespace door can call them directly - no <c>InternalsVisibleTo</c>, because
/// both types already live in Lex.V3.Contracts. A hostile shape that parser refuses today (a
/// non-object binding, a non-string type or datatype, an extra or missing member, a row outside its
/// page's own row limit, a continuation cursor that does not advance) is refused here the same way,
/// by the same code, not by a second copy that could silently drift from it. Two more checks live
/// only here, because they are this door's own reason to exist rather than something the original
/// two-pass comparison ever needed: the freshly reparsed total row count must equal the proof's
/// <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>, and the freshly reparsed rows'
/// canonical-key digest must equal the proof's <see cref="AbsenceFamilyEnumerationProof.CanonicalKeyDigest"/>.
/// Both are independent re-derivations compared against the proof's own claim, never a value compared
/// against a copy of itself.
/// </para>
/// <para>
/// Publisher-neutral by construction: nothing in this file's signature or body names Luxembourg or
/// Europe. <see cref="RepeatedEnumerationInterpretationProfile"/> already carries the one dialect
/// distinction the shared parser needs (<see cref="RepeatedEnumerationSparqlJsonDialect"/>), so the
/// same door serves every publisher's adapter.
/// </para>
/// </remarks>
public static class VerifiedRepeatedEnumerationRows
{
    /// <summary>
    /// Reopens the rows behind an already-minted <paramref name="proof"/> from page evidence the
    /// caller has already reopened and verified out of custody, in page order.
    /// </summary>
    /// <param name="proof">
    /// The family's own enumeration proof. Its <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>
    /// and <see cref="AbsenceFamilyEnumerationProof.CanonicalKeyDigest"/> are what every freshly
    /// reparsed row is checked against; nothing else about the proof's content is trusted without
    /// that check.
    /// </param>
    /// <param name="profile">
    /// The interpretation profile the proof was read under: its dialect, projection and canonical-key
    /// variables drive the reparse. Checked against <paramref name="profileRef"/> and against
    /// <paramref name="proof"/>'s own <see cref="AbsenceFamilyEnumerationProof.InterpretationProfileRef"/>
    /// before anything is parsed, so a caller cannot pair one family's proof with another's profile.
    /// </param>
    /// <param name="profileRef">The reference naming <paramref name="profile"/>'s own canonical bytes.</param>
    /// <param name="countHttpEvidenceRef">
    /// The reference identifying which count observation every page's cardinality must bind, exactly
    /// as <see cref="EnumerationDeliveryComparison.VerifyPages"/> already requires. The caller already
    /// holds this: it is the same <c>RepeatedEnumerationEvidenceRefs.HttpEvidenceRef</c> the original
    /// acquisition bound every page against.
    /// </param>
    /// <param name="pagesInOrder">
    /// One already-reopened, already-verified <see cref="RepeatedEnumerationResolvedEvidence"/> per
    /// page, in page order. Each is exactly what a caller already builds to resolve a
    /// <see cref="RepeatedEnumerationEvidenceRefs"/> today (query plan and query input reopened and
    /// validated against their own references, the response body reopened by the digest its retained
    /// HTTP evidence names); this door parses no plan or input itself and trusts none of it beyond
    /// what <see cref="EnumerationDeliveryComparison.VerifyPages"/> re-checks.
    /// </param>
    /// <param name="refusal">Why no rows were returned, when none were.</param>
    /// <exception cref="ArgumentException">
    /// A caller contract violation rather than a reviewable data disagreement: a null argument, an
    /// empty page list, or a profile that is not the one <paramref name="proof"/> was read under.
    /// </exception>
    public static IReadOnlyList<RepeatedEnumerationRow>? TryOpen(
        AbsenceFamilyEnumerationProof proof,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef profileRef,
        SourceArtifactRef countHttpEvidenceRef,
        IReadOnlyList<RepeatedEnumerationResolvedEvidence> pagesInOrder,
        out RepeatedEnumerationRowsOpenRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profileRef);
        ArgumentNullException.ThrowIfNull(countHttpEvidenceRef);
        ArgumentNullException.ThrowIfNull(pagesInOrder);

        // Evidence of construction, before anything is parsed: the profile a caller hands in must
        // reproduce its own reference, and it must be the exact profile this proof was read under.
        // Pairing one family's proof with another profile would let a caller pick which dialect or
        // projection reparses the bytes, which is exactly the substitution this check exists to
        // refuse.
        RepeatedEnumerationInterpretationProfileIdentity.Validate(profileRef, profile);
        if (proof.InterpretationProfileRef != profileRef)
        {
            throw new ArgumentException(
                "The supplied profile is not the one this proof was read under.",
                nameof(profileRef));
        }

        if (pagesInOrder.Count == 0)
        {
            throw new ArgumentException("At least one page is required.", nameof(pagesInOrder));
        }

        IReadOnlyList<RepeatedEnumerationRow> rows;
        try
        {
            rows = EnumerationDeliveryComparison.VerifyPages(
                pagesInOrder, countHttpEvidenceRef, proof.DeliveredRowCount, profile);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            // Both exception shapes land on the same refusal. VerifyPages calls the strict per-page
            // parser inline and does not distinguish "not JSON at all" (JsonException) from "JSON,
            // but not the SPARQL Results shape or chain this profile requires" (ArgumentException) in
            // its own throw sites; re-classifying that boundary here would be a second opinion about
            // where Source/Core's own parser draws it; refusal is over exceptions from the SAME
            // Source/Core code every other consumer of this data already trusts, not a copy of it.
            refusal = RepeatedEnumerationRowsOpenRefusal.PageChainInvalid;
            return null;
        }

        // VerifyPages binds each page's declared cardinality to countHttpEvidenceRef and to
        // proof.DeliveredRowCount, but never sums the rows it actually delivered against that count:
        // the two-pass comparison that first established this count derived it from parsing the
        // count query's own response, not from summing page rows. This is that missing sum, and it
        // is this door's own re-derivation, not a repeat of a check VerifyPages already made.
        if (rows.Count != proof.DeliveredRowCount)
        {
            refusal = RepeatedEnumerationRowsOpenRefusal.DeliveredRowCountMismatch;
            return null;
        }

        var keyDigest = EnumerationDeliveryComparison.Digest(
            "repeated_enumeration_keys/1", rows.Select(static row => row.CanonicalKey));
        if (!string.Equals(keyDigest, proof.CanonicalKeyDigest, StringComparison.Ordinal))
        {
            refusal = RepeatedEnumerationRowsOpenRefusal.CanonicalKeyDigestMismatch;
            return null;
        }

        refusal = RepeatedEnumerationRowsOpenRefusal.None;
        return rows;
    }
}
