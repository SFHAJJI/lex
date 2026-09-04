using Lex.V3.Contracts.Source.Absence;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>
/// Why <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> refused to hand back rows. Closed.
/// </summary>
/// <remarks>
/// No member here carries a <c>JsonStringEnumMemberName</c> wire token: this refusal is not yet
/// serialised anywhere (queue item 17 has no adapter caller today), and no sibling enum in this
/// same file (<see cref="RepeatedEnumerationThresholdAssessment"/>,
/// <see cref="EnumerationDeliveryOutcome"/>, and the rest) carries one either. The Absence
/// namespace's closed vocabularies are pinned member-by-member because they cross a real wire
/// boundary today (<c>AbsenceKeyTests.EveryClosedAbsenceVocabularyIsPinnedMemberByMember</c>); this
/// enum is outside that sweep by namespace, on purpose, and stays that way until something actually
/// serialises it. What is pinned instead, in
/// <see cref="VerifiedRepeatedEnumerationRowsConstructionSurfaceTests"/>, is the exact member set
/// and the one place that can hand one out.
/// </remarks>
public enum RepeatedEnumerationRowsOpenRefusal
{
    /// <summary>No refusal: the rows were admitted.</summary>
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
    PageChainInvalid = 1,

    /// <summary>
    /// The freshly reparsed row count does not equal
    /// <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>. The proof already established
    /// this count once, from evidence resolved and verified at mint time; this route only ever
    /// re-derives it from bytes the caller reopened just now, independently.
    /// </summary>
    DeliveredRowCountMismatch = 2,

    /// <summary>
    /// The freshly reparsed rows' canonical-key digest does not equal
    /// <see cref="AbsenceFamilyEnumerationProof.CanonicalKeyDigest"/>. This is the one check queue
    /// item 17 exists to add: before this door, no production path ever recomputed this digest from
    /// rows it parsed itself rather than trusting a caller's restatement of it.
    /// </summary>
    CanonicalKeyDigestMismatch = 3,

    /// <summary>
    /// The freshly reparsed rows' cursor digest does not equal the caller-supplied comparison's own
    /// <see cref="EnumerationDeliveryComparison.CursorDigestA"/>, checked ahead of
    /// <see cref="CanonicalRowDigestMismatch"/> because a cursor is one of the projected columns a
    /// row's content digest also covers: checking the narrower claim first keeps this refusal
    /// reachable on its own rather than always being shadowed by the wider one. The digest the proof
    /// itself carries binds only <see cref="RepeatedEnumerationRow.CanonicalKey"/>; this and
    /// <see cref="CanonicalRowDigestMismatch"/> are the fold-in that closes the rest of the row: two
    /// page sets sharing the same keys could otherwise carry different cursors or different non-key
    /// terms and still open as <see cref="None"/>.
    /// </summary>
    CursorDigestMismatch = 4,

    /// <summary>
    /// The freshly reparsed rows' full row-content digest (over every
    /// <see cref="RepeatedEnumerationRow.Terms"/>, not only the canonical key) does not equal the
    /// caller-supplied comparison's own <see cref="EnumerationDeliveryComparison.CanonicalRowDigestA"/>.
    /// Before this member existed, a page set with the same keys and the same cursors as the one the
    /// proof was minted from, but different non-key term values, opened as <see cref="None"/> with
    /// the substituted terms - exactly the shape D1-04b decodes into domain observations.
    /// </summary>
    CanonicalRowDigestMismatch = 5,
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
/// by the same code, not by a second copy that could silently drift from it. Four more checks live
/// only here, because they are this door's own reason to exist rather than something the original
/// two-pass comparison ever needed: the freshly reparsed total row count must equal the proof's
/// <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>; the freshly reparsed rows'
/// canonical-key digest must equal the proof's <see cref="AbsenceFamilyEnumerationProof.CanonicalKeyDigest"/>;
/// and the freshly reparsed rows' cursor digest and full row-content digest must equal the
/// caller-supplied <see cref="EnumerationDeliveryComparison"/>'s own
/// <see cref="EnumerationDeliveryComparison.CursorDigestA"/> and
/// <see cref="EnumerationDeliveryComparison.CanonicalRowDigestA"/>. All four are independent
/// re-derivations compared against a claim made before this call, never a value compared against a
/// copy of itself.
/// </para>
/// <para>
/// The proof's own wire form carries only <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>
/// and <see cref="AbsenceFamilyEnumerationProof.CanonicalKeyDigest"/>: it has no digest over
/// <see cref="RepeatedEnumerationRow.Terms"/> or <see cref="RepeatedEnumerationRow.Cursor"/>, so
/// before the two checks above existed, a page set sharing the proof's keys and count but carrying
/// different non-key terms or a different cursor opened as
/// <see cref="RepeatedEnumerationRowsOpenRefusal.None"/> with the substituted data - exactly the
/// shape D1-04b decodes into domain observations. Widening the proof's own wire form to carry these
/// two extra digests is a schema change ruled out separately; instead the caller passes the
/// <see cref="EnumerationDeliveryComparison"/> that minted the proof, and this door checks that the
/// comparison actually corresponds to <paramref name="proof"/> (same family, run, profile and
/// already-proven claims) before trusting its <c>CursorDigestA</c>/<c>CanonicalRowDigestA</c> as the
/// anchor for the two new checks. A caller cannot forge a matching comparison for substituted bytes:
/// <see cref="EnumerationDeliveryComparison"/>'s only constructor is private, so every instance in
/// existence came from <see cref="EnumerationDeliveryComparison.Create"/> actually replaying two
/// independently agreeing, custody-verified passes - unlike a bare digest string, which a caller
/// could simply recompute from whatever bytes it wants to substitute.
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
    /// <param name="comparison">
    /// The <see cref="EnumerationDeliveryComparison"/> that minted <paramref name="proof"/>. Checked
    /// against <paramref name="proof"/>'s own family key, run identity, interpretation profile,
    /// source profile, delivered row count and canonical-key digest before anything is parsed, so a
    /// caller cannot pair this proof with an unrelated comparison. Once bound, its own
    /// <see cref="EnumerationDeliveryComparison.CursorDigestA"/> and
    /// <see cref="EnumerationDeliveryComparison.CanonicalRowDigestA"/> are the anchor the freshly
    /// reparsed rows' cursor and full row-content digests are checked against - the two claims
    /// <paramref name="proof"/>'s own wire form does not carry.
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
    /// empty page list, a profile that is not the one <paramref name="proof"/> was read under, or a
    /// comparison that is not the one <paramref name="proof"/> was minted from.
    /// </exception>
    public static IReadOnlyList<RepeatedEnumerationRow>? TryOpen(
        AbsenceFamilyEnumerationProof proof,
        EnumerationDeliveryComparison comparison,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef profileRef,
        SourceArtifactRef countHttpEvidenceRef,
        IReadOnlyList<RepeatedEnumerationResolvedEvidence> pagesInOrder,
        out RepeatedEnumerationRowsOpenRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(comparison);
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

        // The comparison a caller hands in must be the one that actually minted this proof, not some
        // other comparison that merely resembles it: every field a proof retains from its minting
        // comparison must agree exactly. Without this, a caller could pair a genuine proof with an
        // unrelated (but validly constructed) comparison and have that comparison's digests approve
        // rows the real minting comparison never saw.
        if (comparison.PartitionKey != proof.FamilyKey ||
            comparison.RunIdentity != proof.AcquisitionRunRef ||
            comparison.InterpretationProfileRef != proof.InterpretationProfileRef ||
            comparison.SourceProfileRef != proof.SourceProfileRef ||
            comparison.DeliveredRowCountA != proof.DeliveredRowCount ||
            !string.Equals(comparison.CanonicalKeyDigestA, proof.CanonicalKeyDigest, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The supplied comparison is not the one this proof was minted from.",
                nameof(comparison));
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
            EnumerationDeliveryComparison.CanonicalKeySetSchema, rows.Select(static row => row.CanonicalKey));
        if (!string.Equals(keyDigest, proof.CanonicalKeyDigest, StringComparison.Ordinal))
        {
            refusal = RepeatedEnumerationRowsOpenRefusal.CanonicalKeyDigestMismatch;
            return null;
        }

        // The proof's own wire form binds only the canonical-key digest above; it carries no digest
        // over the cursor or the full row content, so a page set sharing the proof's keys and count
        // but substituting either would otherwise open as None. comparison.CursorDigestA and
        // comparison.CanonicalRowDigestA are the anchor for that missing binding, checked against a
        // caller-supplied EnumerationDeliveryComparison rather than a caller-supplied bare digest
        // string, because only EnumerationDeliveryComparison.Create can mint one, and only by
        // actually replaying two independently agreeing, custody-verified passes. The cursor check
        // runs first: a cursor is one of the columns the row-content digest also covers, so checking
        // the narrower claim first keeps CursorDigestMismatch reachable on its own rather than always
        // being shadowed by CanonicalRowDigestMismatch.
        var cursorDigest = EnumerationDeliveryComparison.Digest(
            EnumerationDeliveryComparison.CursorSetSchema, rows.Select(static row => row.Cursor));
        if (!string.Equals(cursorDigest, comparison.CursorDigestA, StringComparison.Ordinal))
        {
            refusal = RepeatedEnumerationRowsOpenRefusal.CursorDigestMismatch;
            return null;
        }

        var rowDigest = EnumerationDeliveryComparison.Digest(
            EnumerationDeliveryComparison.CanonicalRowSetSchema, rows.Select(static row => row.Terms));
        if (!string.Equals(rowDigest, comparison.CanonicalRowDigestA, StringComparison.Ordinal))
        {
            refusal = RepeatedEnumerationRowsOpenRefusal.CanonicalRowDigestMismatch;
            return null;
        }

        refusal = RepeatedEnumerationRowsOpenRefusal.None;
        return rows;
    }
}
