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

    [TestMethod]
    public void ADeliveredFamilysRowsReopenVerifiedFromItsProof()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out var proofRefusal);
        Assert.IsNotNull(proof, "the fixture must mint an admitting proof or this test proves nothing");
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.None, proofRefusal);

        var pages = ResolvePages(fixture, delivery);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!,
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
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
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
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, NotJson);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
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
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, ShapeViolation);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
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
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
        Assert.IsNotNull(proof);
        Assert.AreEqual(2, proof!.DeliveredRowCount);

        var tampered = Tamper(fixture, delivery, OneRowBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof, fixture.ProfileForTest, delivery.InterpretationProfileRef,
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
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
        Assert.IsNotNull(proof);

        var tampered = Tamper(fixture, delivery, TwoDifferentRowsBody);

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, [tampered], out var refusal);

        Assert.IsNull(rows);
        Assert.AreEqual(RepeatedEnumerationRowsOpenRefusal.CanonicalKeyDigestMismatch, refusal);
    }

    [TestMethod]
    public void NullArgumentsAreRejectedAsCallerErrors()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
        Assert.IsNotNull(proof);
        var pages = ResolvePages(fixture, delivery);

        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            null!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, null!, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, fixture.ProfileForTest, null!,
            delivery.CountA.HttpEvidenceRef, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            null!, pages, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, null!, out _));
    }

    [TestMethod]
    public void AnEmptyPageListIsACallerError()
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture();
        var delivery = fixture.Create("a,b", "a,b");
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
        Assert.IsNotNull(proof);

        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, fixture.ProfileForTest, delivery.InterpretationProfileRef,
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
        var proof = AbsenceFamilyEnumerationProof.TryCreate("laws", delivery, out _);
        Assert.IsNotNull(proof);
        var pages = ResolvePages(fixture, delivery);

        var otherFixture = new RepeatedEnumerationDeliveryProofTests.Fixture(maximumDeliverableRows: 999);
        var otherProfile = otherFixture.ProfileForTest;
        var otherProfileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
            "urn:uuid:00000000-0000-4000-8000-000000000921", otherProfile);

        // A profile that does not even reproduce the reference handed alongside it.
        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, otherProfile, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, pages, out _));

        // A profile that reproduces its own reference, but is not the one the proof was read under.
        Assert.ThrowsExactly<ArgumentException>(() => VerifiedRepeatedEnumerationRows.TryOpen(
            proof!, otherProfile, otherProfileRef,
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
