using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// Queue item 17: the delivered-rows reader door. Before this door existed, no production path
/// anywhere decoded a family's delivered SPARQL-results-JSON rows into typed data - an adapter had
/// no way back from an <see cref="AbsenceFamilyEnumerationProof"/> and retained bytes to the actual
/// rows behind it. <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> closes that gap by reusing
/// <see cref="EnumerationDeliveryComparison"/>'s own strict parser and page-chain verification
/// (<c>VerifyPages</c>, <c>Digest</c>, both promoted from private to internal, never
/// InternalsVisibleTo) and independently re-deriving the two claims the proof carries: the delivered
/// row count and the canonical-key digest.
/// </summary>
/// <remarks>
/// Every delivery in this file is a real one, built by <see cref="RepeatedEnumerationDeliveryProofTests.Fixture"/>
/// from the full retained evidence tuple Source/Core verifies - the same fixture the rest of this
/// directory's tests already trust for that construction, rather than a second copy of it. The
/// exhaustive ValueKind and shape guards on the shared parser (a non-object binding, a non-string
/// type or datatype, an extra or unknown member, and so on) are this door's own hostile-shape
/// coverage too, because <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> calls the identical
/// <c>VerifyPages</c>/<c>ParseRows</c> code <see cref="RepeatedEnumerationDeliveryProofTests"/>
/// already exercises against <see cref="EnumerationDeliveryComparison.Create"/>; this file adds only
/// the shapes specific to reopening rows from an already-minted proof rather than minting one.
/// </remarks>
[TestClass]
public sealed class VerifiedRepeatedEnumerationRowsTests
{
    private static readonly string OneRowBody =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + "{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:a\"},\"cursor\":{\"type\":\"literal\",\"value\":\"a\"}}]}}";

    private static readonly string TwoDifferentRowsBody =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + "{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:x\"},\"cursor\":{\"type\":\"literal\",\"value\":\"x\"}},"
        + "{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:y\"},\"cursor\":{\"type\":\"literal\",\"value\":\"y\"}}]}}";

    private static readonly string NotJson = "this is not json at all";

    private static readonly string ShapeViolation =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + "{\"id\":{\"type\":\"uri\"}}]}}"; // a term with no "value" member: the shape Term() refuses.

    /// <summary>
    /// The same ids and cursors as the fixture's own "a,b" baseline ("urn:row:a"/"a", "urn:row:b"/"b"),
    /// but with a "value" term now bound where the baseline left it unbound: same canonical key, same
    /// cursor, different non-key content.
    /// </summary>
    private static readonly string NonKeyTermSubstitutedBody =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + "{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:a\"},\"cursor\":{\"type\":\"literal\",\"value\":\"a\"},\"value\":{\"type\":\"literal\",\"value\":\"x\"}},"
        + "{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:b\"},\"cursor\":{\"type\":\"literal\",\"value\":\"b\"},\"value\":{\"type\":\"literal\",\"value\":\"z\"}}]}}";

    /// <summary>
    /// The same ids as the baseline, but the second row's cursor moved from "b" to "z": still
    /// strictly increasing after "a", so <c>VerifyPages</c>' own continuity check never fires.
    /// </summary>
    private static readonly string CursorSubstitutedBody =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + "{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:a\"},\"cursor\":{\"type\":\"literal\",\"value\":\"a\"}},"
        + "{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:b\"},\"cursor\":{\"type\":\"literal\",\"value\":\"z\"}}]}}";

    /// <summary>Fold-in two's hostile shape: a non-string element inside <c>head.vars</c>.</summary>
    private static readonly string NonStringHeadVarsBody =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",5]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[]}}";

    /// <summary>Fold-in one's hostile shape: a <c>bindings</c> array element that is not an object.</summary>
    private static readonly string NonObjectBindingBody =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[5]}}";

    [TestMethod]
    public void ADeliveredFamilysRowsReopenVerifiedFromItsProof()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out var proofRefusal);
        Assert.IsNotNull(proof, "the fixture must mint an admitting proof or this test proves nothing");
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.None, proofRefusal);

        var pages = ResolvePages(fixture, delivery);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!,
            delivery,
            fixture.ProfileForTest,
            delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef,
            pages,
            out var refusal);

        Assert.IsNotNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.None, refusal);
        Assert.AreEqual(2, rows!.Count);
        Assert.AreEqual("urn:row:a", rows[0].Terms[0].Value);
        Assert.AreEqual("urn:row:b", rows[1].Terms[0].Value);
    }

    /// <summary>
    /// End to end against a real, fresh <see cref="FileSystemCustodyStore"/>: the page body this
    /// door parses is written to disk under one store instance and reopened by digest through a
    /// second, independent instance, exactly as a caller would after a process restart. This is the
    /// caller's own reopen-through-custody step the door itself never performs.
    /// </summary>
    [TestMethod]
    public async Task ADeliveredFamilysRowsReopenFromARealFreshCustodyStore()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var page = fixture.Resolve(delivery.PagesA.Pages[0].Evidence);

        var root = Directory.CreateTempSubdirectory("lex-item17-row-reader-");
        try
        {
            var writer = new FileSystemCustodyStore(root.FullName);
            var receipt = await writer.CreateAsync(
                page.RetainedPayloadBytes.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);

            // A second, independent store instance over the same directory: nothing here is the
            // in-memory bytes the fixture built, only what a fresh process would read off disk.
            var reader = new FileSystemCustodyStore(root.FullName);
            var reopenedBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                reader, receipt.Reference.ContentSha256, CancellationToken.None);
            CollectionAssert.AreEqual(page.RetainedPayloadBytes.ToArray(), reopenedBytes.ToArray());

            var reopenedPage = page with { RetainedPayloadBytes = reopenedBytes };

            var rows = VerifiedRepeatedEnumerationRows.TryOpen(
                proof!,
                delivery,
                fixture.ProfileForTest,
                delivery.InterpretationProfileRef,
                delivery.CountA.HttpEvidenceRef,
                [reopenedPage],
                out var refusal);

            Assert.IsNotNull(rows);
            Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.None, refusal);
            Assert.AreEqual(2, rows!.Count);
            Assert.AreEqual("urn:row:a", rows[0].Terms[0].Value);
            Assert.AreEqual("urn:row:b", rows[1].Terms[0].Value);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ANonJsonPageBodyRefusesRatherThanThrows()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, NotJson);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.PageChainInvalid, refusal);
    }

    /// <summary>
    /// A term missing its required "value" member is the exact ValueKind/shape guard
    /// <c>EnumerationDeliveryComparison</c>'s own <c>Term</c> method refuses
    /// ("The SPARQL term shape is invalid."). Reused, not re-implemented: this proves the guard
    /// still fires through this door.
    /// </summary>
    [TestMethod]
    public void AHostileTermShapeRefusesRatherThanThrows()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, ShapeViolation);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.PageChainInvalid, refusal);
    }

    /// <summary>
    /// A page that is individually a perfectly well-formed, terminal single-page chain, but delivers
    /// fewer rows than the proof's own <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>.
    /// <c>VerifyPages</c> never sums delivered rows against the count it is handed - it only checks
    /// each page's own declared cardinality and the terminal-page policy - so this mismatch is this
    /// door's own re-derivation catching what page-chain verification alone would let through.
    /// </summary>
    [TestMethod]
    public void FewerDeliveredRowsThanTheProofClaimsIsRefused()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);
        Assert.AreEqual(2, proof!.DeliveredRowCount);

        var tampered = Tamper(fixture, delivery, OneRowBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.DeliveredRowCountMismatch, refusal);
    }

    /// <summary>
    /// Same row count as the proof claims, different row content: the chain and the count both
    /// verify, and only the independently re-derived canonical-key digest disagrees with the proof's
    /// claim. This is the one check queue item 17 exists to add.
    /// </summary>
    [TestMethod]
    public void DifferentRowContentAtTheSameCountFailsTheKeyDigest()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, TwoDifferentRowsBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.CanonicalKeyDigestMismatch, refusal);
    }

    /// <summary>
    /// Same canonical key, same cursor, different non-key term: <c>VerifyPages</c> and the count and
    /// key-digest checks all verify, because none of them ever look past the canonical key. Before
    /// the door bound <see cref="EnumerationDeliveryComparison.CanonicalRowDigestA"/>, this exact
    /// shape opened as <see cref="RepeatedEnumerationRowsOpenRefusal.None"/> with the substituted
    /// "value" term - the gap queue item 17's fold-in closes.
    /// </summary>
    [TestMethod]
    public void ANonKeyTermSubstitutedAtTheSameKeysAndCursorFailsTheRowContentDigest()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, NonKeyTermSubstitutedBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.CanonicalRowDigestMismatch, refusal);
    }

    /// <summary>
    /// Same canonical keys, one row's cursor substituted: still strictly increasing, so
    /// <c>VerifyPages</c>' own continuity check never fires, and the canonical-key digest still
    /// agrees because it never looks at the cursor column. Checked ahead of the row-content digest
    /// in <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> for exactly this reason: a cursor
    /// substitution also changes <see cref="RepeatedEnumerationRow.Terms"/>, so without that
    /// ordering this shape would always be reported as the wider row-content mismatch instead.
    /// </summary>
    [TestMethod]
    public void ACursorSubstitutedAtTheSameKeysFailsTheCursorDigest()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, CursorSubstitutedBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.CursorDigestMismatch, refusal);
    }

    /// <summary>
    /// A comparison that is internally valid but did not mint this proof (a different partition
    /// key) is a caller contract violation, exactly like pairing a proof with the wrong profile:
    /// nothing about the comparison's own digests can be trusted as this proof's anchor unless the
    /// comparison is first shown to be the one that actually produced it.
    /// </summary>
    [TestMethod]
    public void AComparisonThatDidNotMintThisProofIsACallerError()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);
        var pages = ResolvePages(fixture, delivery);

        var otherDelivery = new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "other")
            .Create("a,b", "a,b");

        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, otherDelivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
    }

    /// <summary>
    /// The same binding check, varied on a different one of the six compared fields: a comparison
    /// with the same partition key but a different acquisition run is just as much an unrelated
    /// comparison as one with a different partition key, and the fixture's own
    /// <c>runIdentitySeed</c> exists (per its own remarks) exactly to build two otherwise-identical
    /// comparisons that differ in only that one respect.
    /// </summary>
    [TestMethod]
    public void AComparisonWithADifferentRunDidNotMintThisProofIsACallerError()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);
        var pages = ResolvePages(fixture, delivery);

        var otherDelivery = new RepeatedEnumerationDeliveryProofTests.Fixture(runIdentitySeed: 931)
            .Create("a,b", "a,b");

        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, otherDelivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
    }

    /// <summary>
    /// Another of the six compared fields: a comparison with the same partition key and run, but a
    /// different delivered row count (one row instead of two), is still an unrelated comparison
    /// this proof was not minted from.
    /// </summary>
    [TestMethod]
    public void AComparisonWithADifferentDeliveredRowCountDidNotMintThisProofIsACallerError()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);
        var pages = ResolvePages(fixture, delivery);

        var otherDelivery = new RepeatedEnumerationDeliveryProofTests.Fixture(expectedCount: 1)
            .Create("a", "a");

        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, otherDelivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
    }

    /// <summary>
    /// Fold-in two: a <c>head.vars</c> array element that is not a JSON string throws
    /// <see cref="InvalidOperationException"/> out of <see cref="System.Text.Json.JsonElement.GetString"/>
    /// past this door's own catch filter, so the door threw where it promises to refuse. The strict
    /// parser now checks the element kind before calling <c>GetString</c>, and this test drives that
    /// exact body.
    /// </summary>
    [TestMethod]
    public void ANonStringHeadVarsElementRefusesRatherThanThrows()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, NonStringHeadVarsBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.PageChainInvalid, refusal);
    }

    /// <summary>
    /// Fold-in one: a <c>bindings</c> array element that is not a JSON object threw
    /// <see cref="InvalidOperationException"/> out of <see cref="System.Text.Json.JsonElement.EnumerateObject"/>
    /// past this door's own catch filter, because that call sat inside the argument expression
    /// evaluated before the shared <c>Object</c> helper's own element-kind guard ever ran. The
    /// strict parser now checks the element kind before calling <c>EnumerateObject</c> on the
    /// binding, and this test drives that exact body.
    /// </summary>
    [TestMethod]
    public void ANonObjectBindingElementRefusesRatherThanThrows()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, NonObjectBindingBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.PageChainInvalid, refusal);
    }

    [TestMethod]
    public void NullArgumentsAreRejectedAsCallerErrors()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);
        var pages = ResolvePages(fixture, delivery);

        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            null!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, null!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, null!, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, null!,
            delivery.CountA.HttpEvidenceRef, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            null!, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, null!, out _));
    }

    [TestMethod]
    public void AnEmptyPageListIsACallerError()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [], out _));
    }

    /// <summary>
    /// A profile that is not the one this proof was read under is a caller contract violation, not a
    /// reviewable data disagreement: pairing one family's proof with a different profile would let a
    /// caller pick which dialect or projection reparses the bytes, so it throws rather than refusing.
    /// </summary>
    [TestMethod]
    public void AProfileThatIsNotTheOneTheProofWasReadUnderIsACallerError()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);
        var pages = ResolvePages(fixture, delivery);

        var otherFixture = new RepeatedEnumerationDeliveryProofTests.Fixture(maximumDeliverableRows: 999);
        var otherProfile = otherFixture.ProfileForTest;
        var otherProfileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
            "urn:uuid:00000000-0000-4000-8000-000000000921", otherProfile);

        // A profile that does not even reproduce the reference handed alongside it.
        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, otherProfile, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));

        // A profile that reproduces its own reference, but is not the one the proof was read under.
        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, delivery, otherProfile, otherProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
    }

    private static IReadOnlyList<RepeatedEnumerationResolvedEvidence> ResolvePages(
        RepeatedEnumerationDeliveryProofTests.Fixture fixture, EnumerationDeliveryComparison delivery) =>
        delivery.PagesA.Pages.Select(page => fixture.Resolve(page.Evidence)).ToArray();

    /// <summary>
    /// Side A's single page, resolved through the fixture and then given a different retained body -
    /// exactly the shape of "the caller reopened bytes by digest through custody" that this door
    /// takes as input, standing in for a page whose reopened bytes disagree with what the proof was
    /// originally minted from.
    /// </summary>
    private static RepeatedEnumerationResolvedEvidence Tamper(
        RepeatedEnumerationDeliveryProofTests.Fixture fixture,
        EnumerationDeliveryComparison delivery,
        string body)
    {
        var page = fixture.Resolve(delivery.PagesA.Pages[0].Evidence);
        return page with { RetainedPayloadBytes = Encoding.UTF8.GetBytes(body) };
    }
}
