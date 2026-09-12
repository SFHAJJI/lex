using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The full OpinionRequest sweep: one inventory and every batch its citation issues, one ceiling.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT DISCHARGES that the canary could not. A single batch cannot show that the batches
/// together ARE the class; only a complete sweep can be measured by
/// <see cref="LuxembourgOpinionRequestBatchCover"/>, whose expected keys come from the inventory
/// rather than from whatever came back.
/// </para>
/// <para>
/// SAME GATE AS THE CANARY, WIDENED ONLY IN COUNT.
/// <see cref="LuxembourgOpinionRequestCanaryPlan.EveryBatchAfterInventory"/> delegates to the
/// one-batch gate, so every refusal, exhaustion, empty-class and missing-citation stop is inherited
/// rather than restated. This runner still has no decision of its own: it acquires exactly the
/// ordinals it is handed.
/// </para>
/// <para>
/// IT STOPS AT THE CEILING RATHER THAN THROUGH IT. The shared budget is checked before each batch,
/// and a sweep that runs out returns what it has. A partial sweep is retained and reported as a
/// dated magnitude finding - <c>SweepStoppedBeforeEveryBatch</c> - and never as a cover, because a
/// cover over a truncated set would agree with itself about a class it never finished reading.
/// </para>
/// <para>
/// The cost is measured, not assumed: batch 0 of the 2026-09-12 canary delivered 145 rows in one
/// page per pass, so the sweep is expected near 805 wire requests. The ceiling is the enforcement;
/// that figure is only the reason the ceiling is set where it is.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgOpinionRequestFullSweep
{
    private const string EnableVariable = "LEX_E8_OPINION_REQUEST_FULL_SWEEP";

    /// <summary>
    /// The ceiling proposed for the full sweep: 1.5x the measured 805, covering every batch needing
    /// two pages per pass, and far below the 4,005 structural worst so that it actually binds.
    /// </summary>
    private const int SweepCeiling = 1_200;

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task EveryInventoryIssuedBatchIsAcquiredUnderOneCeilingAndCovered()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 to run the full OpinionRequest sweep against Legilux under "
                + $"a shared ceiling of {SweepCeiling} wire requests. Skipped by default so the "
                + "suite sends no unasked traffic.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(
            checkout, "artifacts", "e8-opinion-request-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);

        var scopeBytes = Encoding.UTF8.GetBytes(
            "E8 OpinionRequest full sweep\n"
            + "question=do the inventory-issued batches together cover the class exactly once\n"
            + $"ceiling={SweepCeiling} wire requests, ONE shared budget across the inventory and "
            + "every batch\n"
            + "stop=inventory refusal, incompleteness or exhaustion prevents batch acquisition; "
            + "exhaustion mid-sweep returns a partial finding, never a cover\n");
        var scopeReceipt = await store.CreateAsync(
            scopeBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var scopeRef = new SourceArtifactRef(NewUrn(), scopeReceipt.Reference.ContentSha256);

        var renderer = await RendererOfAsync(checkout);
        var witness = SourceWitness(scopeRef, renderer);
        var budget = WireRequestBudget.OfWireRequests(SweepCeiling);

        var inventory = await new LuxembourgOpinionRequestInventoryProducer(store, TimeProvider.System)
            .RunAsync(
                new LuxembourgOpinionRequestInventoryRunRequest(
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), renderer, budget),
                witness,
                CancellationToken.None);
        TestContext?.WriteLine(
            $"inventory: delivered={inventory.Delivered} refusal={inventory.Refusal} "
            + $"members={(inventory.Delivered ? inventory.AddressableInOrder().Count : 0)} "
            + $"spent={inventory.WireBudget.Spent}/{inventory.WireBudget.Limit}");

        var decision = LuxembourgOpinionRequestCanaryPlan.EveryBatchAfterInventory(inventory);
        TestContext?.WriteLine($"gate: {decision.Verdict} - {decision.Reason}");

        var producer = new LuxembourgOpinionRequestGraphProducer(store, TimeProvider.System);
        var acquired = new List<LuxembourgOpinionRequestGraphResult>();
        var coverages = new List<LuxembourgOpinionRequestCoverage>();
        var stoppedAt = -1;

        foreach (var ordinal in decision.BatchOrdinalsToAcquire)
        {
            // CHECKED BEFORE THE BATCH, NOT AFTER IT. A batch opened on an exhausted budget would
            // refuse on its own robots reservation and read as a batch failure rather than as the
            // ceiling doing its job.
            if (budget.Exhausted)
            {
                stoppedAt = ordinal;
                TestContext?.WriteLine($"ceiling reached before batch {ordinal}; stopping.");
                break;
            }

            var batch = await producer.RunAsync(
                LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                    LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
                    inventory.AddressableInOrder(),
                    inventory.Citation!,
                    ordinal,
                    NewUrn(),
                    renderer,
                    budget),
                witness,
                CancellationToken.None);
            acquired.Add(batch);

            if (batch.Coverage is { } coverage)
            {
                coverages.Add(coverage);
            }
            else
            {
                stoppedAt = ordinal;
                TestContext?.WriteLine(
                    $"batch {ordinal} refused: {batch.Refusal} {batch.Detail}; stopping.");
                break;
            }

            if (ordinal % 25 == 0 || ordinal == decision.BatchOrdinalsToAcquire[^1])
            {
                TestContext?.WriteLine(
                    $"batch {ordinal}: spent={batch.WireBudget.Spent}/{batch.WireBudget.Limit}");
            }
        }

        // THE COVER COMES FROM THE INVENTORY, and is only attempted when the sweep finished what it
        // was cleared for. Building one over a truncated set is the error the cover exists to catch.
        LuxembourgOpinionRequestBatchCover? cover = null;
        var coverRefusal = LuxembourgOpinionRequestBatchCoverRefusal.None;
        string? coverDetail = null;
        if (inventory.Delivered && coverages.Count == decision.BatchOrdinalsToAcquire.Count
            && decision.BatchOrdinalsToAcquire.Count > 0)
        {
            cover = LuxembourgOpinionRequestBatchCover.TryCreate(
                inventory.AddressableInOrder(), inventory.Citation!, coverages,
                out coverRefusal, out coverDetail);
        }

        var reconciliation = LuxembourgOpinionRequestCanaryPlan.Reconcile(inventory, acquired);
        var verdict = LuxembourgOpinionRequestCanaryPlan.Conclude(decision, inventory, acquired);
        TestContext?.WriteLine(
            $"verdict={verdict} cover={(cover is null ? coverRefusal.ToString() : "created")} "
            + $"spent={reconciliation.FinalBudgetSpent} reconciles={reconciliation.Reconciles}");

        var index = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                purpose = "E8 full OpinionRequest sweep: acquire every inventory-issued batch under "
                    + "one enforced ceiling and measure the class-level cover. Measurement and "
                    + "coverage only; not E8 acceptance and not Stage 2 closure.",
                observedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                head = Git(checkout, "rev-parse", "HEAD"),
                dirtyPaths = Git(checkout, "status", "--porcelain"),
                ceiling = SweepCeiling,
                verdict,
                gate = new { decision.Verdict, decision.Reason, batchesCleared = decision.BatchOrdinalsToAcquire.Count },
                inventoryRun = new
                {
                    inventory.Delivered,
                    refusal = inventory.Refusal.ToString(),
                    inventory.Detail,
                    inventory.ProductRequestCount,
                    addressableMembers = inventory.Delivered ? inventory.AddressableInOrder().Count : 0,
                    budgetSpent = inventory.WireBudget.Spent,
                    inventory.WireBudget.Exhausted,
                },
                sweep = new
                {
                    batchesAcquired = acquired.Count,
                    batchesCovered = coverages.Count,
                    stoppedBeforeOrdinal = stoppedAt < 0 ? null : (int?)stoppedAt,
                    budgetSpent = reconciliation.FinalBudgetSpent,
                    budgetLimit = SweepCeiling,
                },
                classCover = cover is null
                    ? new { created = false, refusal = coverRefusal.ToString(), detail = coverDetail }
                    : new { created = true, refusal = "None", detail = (string?)cover.Describe() },
                coverTotals = cover is null
                    ? null
                    : new
                    {
                        cover.SubjectCount,
                        cover.CoveredPairCount,
                        cover.PresentPairCount,
                        cover.DerivedAbsenceCount,
                        cover.UnresolvedGapCount,
                        cover.UnconfirmedRoleCount,
                        cover.RetainedRowCount,
                    },
                reconciliation,
                root,
            },
            new JsonSerializerOptions { WriteIndented = true });

        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var indexPath = Path.Combine(root, "evidence-index.json");
        await File.WriteAllBytesAsync(indexPath, index);
        TestContext?.WriteLine($"evidence: {indexPath}");
        Console.WriteLine(Encoding.UTF8.GetString(index));

        // As with the canary: every outcome is retained and reported, and the only thing that fails
        // this method is the ceiling being breached.
        Assert.IsLessThanOrEqualTo(
            SweepCeiling,
            reconciliation.FinalBudgetSpent,
            "the shared ceiling was exceeded, which is the one outcome this sweep must never have.");
    }

    private static async Task<MachineQueryRendererSource> RendererOfAsync(string checkout)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            checkout,
            "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgOpinionRequestInventoryDiscoveryPlan.cs"));
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static BoundMachineRequest SourceWitness(
        SourceArtifactRef scopeRef,
        MachineQueryRendererSource renderer)
    {
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(
            OfficialMachineQuerySourceProfiles.Resolve(
                OfficialMachineQuerySourceProfileId.LuxembourgSparql).ArtifactRef,
            scopeRef);
        return plan.BindCount(
            NewUrn(),
            NewUrn(),
            NewUrn(),
            "R",
            LuxembourgQueryPass.Pass1,
            new LuxembourgQueryPartitionRange(
                "opinion-request-sweep",
                new LuxembourgQueryCursor("", "", "", "", "", ""),
                new LuxembourgQueryCursor("￿", "", "", "", "", "")),
            renderer).Request;
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
    }

    private static string Git(string checkout, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = checkout, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var result = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? result : throw new InvalidOperationException("git failed.");
    }
}
