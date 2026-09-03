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

    [TestMethod]
    public void ClassifierAndSessionAgreeOnEveryProtection()
    {
        Assert.AreEqual(
            CustodyMembership.RetainedUnenforced,
            CustodyMembershipClassifier.Classify(Receipt(
                CustodyVerificationProfile.FileSystemUnenforced1,
                CustodyProtection.NotEnforced,
                policyKey: null,
                protectedUntil: null)));

        Assert.AreEqual(
            CustodyMembership.Floored,
            CustodyMembershipClassifier.Classify(Receipt(
                CustodyVerificationProfile.ImmutableObject1,
                CustodyProtection.LockedTime,
                policyKey: Guid.Parse("00000000-0000-0000-0000-000000000050"),
                protectedUntil: ObservedAt.AddDays(91))));

        Assert.AreEqual(
            CustodyMembership.Floored,
            CustodyMembershipClassifier.Classify(LegalHoldReceipt()));
    }

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
