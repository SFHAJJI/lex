using Lex.V3.Contracts.Custody;

namespace Lex.V3.Tests.Custody;

/// <summary>
/// The one classification rule, exercised directly. <c>RoutedHttpAcquisitionSessionAuditTests</c>
/// and the Luxembourg executor tests exercise the same rule indirectly through real receipts; this
/// file pins the rule itself so the two callers cannot drift apart.
/// </summary>
[TestClass]
public sealed class CustodyMembershipClassifierTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The classifier's answer for every value of <see cref="CustodyProtection"/> there is.
    /// </summary>
    /// <remarks>
    /// This was called ClassifierAndSessionAgreeOnEveryProtection and it never called a session:
    /// <c>RoutedHttpAcquisitionSession</c> lives in Lex.V3.Ingest, which this assembly does not
    /// reference, so the agreement half of that name was unverifiable from here and read as
    /// verified. What holds the agreement is that the session's own <c>ClassifyMembership</c> is a
    /// one-line delegation to this classifier, which its own assembly's tests exercise; what this
    /// file holds is the rule itself.
    ///
    /// It also enumerated three cases by hand, so a fourth protection added to the enum would have
    /// gone unclassified and unnoticed. The expectations are still literals written beside the
    /// case, never derived from the code under test, but the SET of cases is now taken from the
    /// enum, so a new member fails here until somebody decides what it means.
    /// </remarks>
    [TestMethod]
    public void TheClassifierAnswersEveryDefinedProtection()
    {
        var expected = new Dictionary<CustodyProtection, CustodyMembership>
        {
            [CustodyProtection.NotEnforced] = CustodyMembership.RetainedUnenforced,
            [CustodyProtection.LockedTime] = CustodyMembership.Floored,
            [CustodyProtection.ActiveLegalHold] = CustodyMembership.Floored,
        };

        CollectionAssert.AreEquivalent(
            Enum.GetValues<CustodyProtection>(),
            expected.Keys.ToArray(),
            "a protection was added to the vocabulary and nothing here says what it classifies as");

        foreach (var (protection, membership) in expected)
        {
            Assert.AreEqual(
                membership,
                CustodyMembershipClassifier.Classify(ReceiptFor(protection)),
                $"{protection} classified wrongly");
        }
    }

    /// <summary>
    /// A receipt carrying exactly one protection, on the only verification profile and custody
    /// class whose policy-evidence constructor admits it.
    /// </summary>
    private static DurableBlobWriteReceipt ReceiptFor(CustodyProtection protection) => protection switch
    {
        CustodyProtection.NotEnforced => Receipt(
            CustodyVerificationProfile.FileSystemUnenforced1,
            CustodyProtection.NotEnforced,
            policyKey: null,
            protectedUntil: null),
        CustodyProtection.LockedTime => Receipt(
            CustodyVerificationProfile.ImmutableObject1,
            CustodyProtection.LockedTime,
            policyKey: Guid.Parse("00000000-0000-0000-0000-000000000050"),
            protectedUntil: ObservedAt.AddDays(91)),
        CustodyProtection.ActiveLegalHold => LegalHoldReceipt(),
        _ => throw new AssertFailedException($"No receipt shape is defined for {protection}."),
    };

    [TestMethod]
    public void ANullReceiptIsRefusedRatherThanMisclassified()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => CustodyMembershipClassifier.Classify(null!));
    }

    private static DurableBlobWriteReceipt Receipt(
        CustodyVerificationProfile profile,
        CustodyProtection protection,
        Guid? policyKey,
        DateTimeOffset? protectedUntil)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            new string('a', 64),
            4,
            CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            profile,
            policyKey,
            protection,
            ObservedAt,
            protectedUntil);
        return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
    }

    private static DurableBlobWriteReceipt LegalHoldReceipt()
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            new string('b', 64),
            4,
            CustodyClass.LegalHoldEvidence);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.ImmutableObject1,
            Guid.Parse("00000000-0000-0000-0000-000000000051"),
            CustodyProtection.ActiveLegalHold,
            ObservedAt,
            protectedUntil: null);
        return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
    }
}
