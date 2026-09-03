using System.Net;
using System.Net.Http;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Step 5: <see cref="LuxembourgDeliveryEvidenceSet"/> materialized and compared with no executor
/// yet, driven by a hand-assembled two-pass run against <see
/// cref="RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore"/>. This is the step where
/// the anti-tautology claim (every artifact is read back out of custody, never carried from the
/// bind) becomes true or does not.
/// </summary>
[TestClass]
public sealed class LuxembourgDeliveryEvidenceSetTests
{
    [TestMethod]
    public async Task TheRebuiltRendererReproducesTheFrozenDigests()
    {
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
        var run = await RunTwoPassAsync(store).ConfigureAwait(false);

        var set = await LuxembourgDeliveryEvidenceSet.MaterializeAsync(
                run.Profile, run.ProfileRef, run.InvariantPlan, run.InvariantPlanResourceId, run.SetId,
                run.RendererSource, run.PassA, run.PassB, store, CancellationToken.None)
            .ConfigureAwait(false);

        var membership = MembershipFor(store, run);
        var receipt = set.TryCompareAndReceipt(membership.Session, membership.Executor, out var refusal);

        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, receipt.Delivery.Outcome);
        Assert.AreEqual(2, receipt.Delivery.DeliveredRowCountA);
        Assert.AreEqual(2, receipt.Delivery.DeliveredRowCountB);
        Assert.AreEqual(CustodyMembership.Floored, receipt.RetainedFloor);
    }

    [TestMethod]
    public async Task MaterializationAgainstAStoreHoldingNothingRefusesRatherThanServingMemory()
    {
        // Seeding the empty store to make this pass destroys the test: the whole point is that a
        // resolver holding only in-memory objects (never reopened) cannot be told apart from a
        // correct one unless something forces a real reopen, and a fresh, empty store is that force.
        var writingStore = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
        var run = await RunTwoPassAsync(writingStore).ConfigureAwait(false);
        var emptyStore = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();

        // The executor's own catch (step 6) treats CustodyIntegrityException and
        // CustodyRequiredException identically (both become custody_member_missing): which of the
        // two surfaces here is an artifact of RecordingCustodyStore's own "unknown digest" exception
        // shape (an AssertFailedException, which CustodyRestore's generic catch wraps as
        // CustodyRequiredException) rather than FileSystemCustodyStore's real
        // CustodyIntegrityException, so this asserts the shared base contract, not the CLR type.
        var thrown = await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
                LuxembourgDeliveryEvidenceSet.MaterializeAsync(
                    run.Profile, run.ProfileRef, run.InvariantPlan, run.InvariantPlanResourceId, run.SetId,
                    run.RendererSource, run.PassA, run.PassB, emptyStore, CancellationToken.None))
            .ConfigureAwait(false);
        Assert.IsInstanceOfType<AssertFailedException>(thrown.InnerException);
    }

    [TestMethod]
    public async Task ATamperedInvariantPlanFailsTheOfflineRerender()
    {
        // The mutation this drives: carrying the binder's own renderer instance rather than
        // rebuilding one from the reopened invariant plan. Here the plan CONTENT is genuine and
        // genuinely reopenable (same digest, same bytes, so custody itself is not what refuses),
        // but MaterializeAsync is handed a different resource id for it than the one the retained
        // MachineQueryPlan actually names as its RendererProfileRef. A rebuilt renderer's identity
        // is minted from what MaterializeAsync was given, so it disagrees with the retained plan
        // and Source/Core's ReproduceForEvidence refuses. A renderer carried from the original bind
        // would still hold the correct, original identity and would not notice.
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
        var run = await RunTwoPassAsync(store).ConfigureAwait(false);

        var wrongResourceId = $"urn:uuid:{Guid.NewGuid():D}";
        Assert.AreNotEqual(run.InvariantPlanResourceId, wrongResourceId);

        var set = await LuxembourgDeliveryEvidenceSet.MaterializeAsync(
                run.Profile, run.ProfileRef, run.InvariantPlan, wrongResourceId, run.SetId,
                run.RendererSource, run.PassA, run.PassB, store, CancellationToken.None)
            .ConfigureAwait(false);

        var membership = MembershipFor(store, run);
        var receipt = set.TryCompareAndReceipt(membership.Session, membership.Executor, out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.DeliveryComparisonRefused, refusal);
        Assert.IsNotNull(set.LastCoreRefusalMessage);
    }

    private static (
        Dictionary<string, CustodyMembership> Session,
        Dictionary<string, CustodyMembership> Executor) MembershipFor(
        RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore store,
        TwoPassRun run)
    {
        var session = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
        var executor = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
        foreach (var observation in run.PassA.Pages.Prepend(run.PassA.Count)
                     .Concat(run.PassB.Pages.Prepend(run.PassB.Count)))
        {
            foreach (var digest in observation.SessionRetainedDigests)
            {
                session[digest] = CustodyMembership.Floored;
            }

            executor[observation.HttpEvidenceRef.Sha256] = CustodyMembership.Floored;
        }

        return (session, executor);
    }

    private sealed record TwoPassRun(
        RepeatedEnumerationInterpretationProfile Profile,
        SourceArtifactRef ProfileRef,
        LuxembourgQueryPlan InvariantPlan,
        string InvariantPlanResourceId,
        string SetId,
        MachineQueryRendererSource RendererSource,
        LuxembourgDeliveryPass PassA,
        LuxembourgDeliveryPass PassB);

    private static async Task<TwoPassRun> RunTwoPassAsync(ICustodyStore store)
    {
        var (invariantPlan, invariantPlanResourceId, invariantPlanRef) =
            LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var rendererSource = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var partition = LuxembourgAcquisitionTestFixture.FullRange();
        var profile = invariantPlan.CreateDeliveryProfile(invariantPlanResourceId, LuxembourgAcquisitionTestFixture.SubjectsSetId);
        var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
            $"urn:uuid:{Guid.NewGuid():D}", profile);

        var bodies = new[]
        {
            LuxembourgAcquisitionTestFixture.CountJson(2),
            LuxembourgAcquisitionTestFixture.RowsJson("a", "b"),
            LuxembourgAcquisitionTestFixture.EmptyRowsJson(),
            LuxembourgAcquisitionTestFixture.CountJson(2),
            LuxembourgAcquisitionTestFixture.RowsJson("a", "b"),
            LuxembourgAcquisitionTestFixture.EmptyRowsJson(),
        };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, bodies[ordinal - 1]));

        // A throwaway count bind, used only as the session's non-rendering source witness.
        var witnessCount = invariantPlan.BindCount(
            invariantPlanResourceId, $"urn:uuid:{Guid.NewGuid():D}", $"urn:uuid:{Guid.NewGuid():D}",
            LuxembourgAcquisitionTestFixture.SubjectsSetId, LuxembourgQueryPass.Pass1, partition, rendererSource);

        using var session = await LuxembourgAcquisitionTestFixture.StartedSessionAsync(
                witnessCount.Request, handler, store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider())
            .ConfigureAwait(false);

        var passA = await LuxembourgAcquisitionTestFixture.RunPassAsync(
                session, store, invariantPlan, invariantPlanResourceId,
                LuxembourgAcquisitionTestFixture.SubjectsSetId, LuxembourgQueryPass.Pass1, partition,
                rendererSource, profile, selectedRowCount: 2,
                pageBodies: [bodies[1], bodies[2]])
            .ConfigureAwait(false);
        var passB = await LuxembourgAcquisitionTestFixture.RunPassAsync(
                session, store, invariantPlan, invariantPlanResourceId,
                LuxembourgAcquisitionTestFixture.SubjectsSetId, LuxembourgQueryPass.Pass2, partition,
                rendererSource, profile, selectedRowCount: 2,
                pageBodies: [bodies[4], bodies[5]])
            .ConfigureAwait(false);

        return new TwoPassRun(
            profile, profileRef, invariantPlan, invariantPlanResourceId,
            LuxembourgAcquisitionTestFixture.SubjectsSetId, rendererSource, passA, passB);
    }
}
