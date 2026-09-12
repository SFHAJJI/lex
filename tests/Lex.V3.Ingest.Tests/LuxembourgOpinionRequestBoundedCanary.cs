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
/// The bounded canary: one whole-class inventory and at most one batch, under one shared ceiling.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT MEASURES IS ROWS PER BATCH, and that is the only unknown left. Everything else about the
/// acceptance arithmetic follows from the publisher's own count once that number exists; until it
/// does, every projection of full acceptance rests on an assumption, which is how a "120 ceiling"
/// came to be published and withdrawn.
/// </para>
/// <para>
/// THE SMALLEST HONEST CANARY IS INVENTORY PLUS ONE BATCH, not one batch. A batch cannot be issued
/// without a proven whole-class inventory: <c>ForBatch</c> takes an ordinal into the assignment a
/// citation issues, and the inventory plan is a whole-class sweep carrying no selection, so there is
/// no partial inventory to shortcut through.
/// </para>
/// <para>
/// ONE SHARED CEILING OF 250, NOT TWO. A budget per run would be honoured twice and bound nothing
/// over the pair. The inventory spends first and the batch receives the remainder; reaching the
/// ceiling stops the traffic where it stands.
/// </para>
/// <para>
/// THE DECISION TO ACQUIRE A BATCH IS NOT MADE HERE. It comes back from
/// <see cref="LuxembourgOpinionRequestCanaryPlan.AfterInventory"/> as a list of ordinals, empty
/// unless the inventory is a clean whole-class proof with budget left, and this method acquires
/// exactly what that list holds. Forcing the loop to run still sends nothing, and
/// <see cref="LuxembourgOpinionRequestCanaryPlanTests"/> pins that emptiness for every refusal shape
/// without touching a publisher.
/// </para>
/// <para>
/// EXHAUSTION IS A FINDING, NOT A CANARY. A run that reached its ceiling measured a magnitude and
/// did not demonstrate the path. The retained report says which of those happened, and reconciles
/// the budget it claims to have spent against the requests the producers actually recorded.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgOpinionRequestBoundedCanary
{
    private const string EnableVariable = "LEX_E8_OPINION_REQUEST_CANARY";

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TheOpinionRequestClassIsInventoriedAndOneBatchIsAcquiredUnderOneCeiling()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 to run the bounded OpinionRequest canary against Legilux "
                + $"under a shared ceiling of {LuxembourgOpinionRequestCanaryPlan.WireCeiling} wire "
                + "requests. Skipped by default so the suite sends no unasked traffic.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(
            checkout, "artifacts", "e8-opinion-request-canary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);

        var scopeBytes = Encoding.UTF8.GetBytes(
            "E8 OpinionRequest bounded canary\n"
            + "question=how many graph rows does one batch of 50 requests deliver\n"
            + $"ceiling={LuxembourgOpinionRequestCanaryPlan.WireCeiling} wire requests, ONE shared "
            + "budget across the inventory run and at most one inventory-issued batch\n"
            + "stop=inventory refusal, incompleteness or exhaustion prevents batch acquisition\n");
        var scopeReceipt = await store.CreateAsync(
            scopeBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var scopeRef = new SourceArtifactRef(NewUrn(), scopeReceipt.Reference.ContentSha256);

        var renderer = await RendererOfAsync(checkout);
        var witness = SourceWitness(scopeRef, renderer);

        // ONE INSTANCE, PASSED TO BOTH RUNS. This is the object the whole ceiling argument rests on.
        var budget = WireRequestBudget.OfWireRequests(LuxembourgOpinionRequestCanaryPlan.WireCeiling);

        var inventory = await new LuxembourgOpinionRequestInventoryProducer(store, TimeProvider.System)
            .RunAsync(
                new LuxembourgOpinionRequestInventoryRunRequest(
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), renderer, budget),
                witness,
                CancellationToken.None);
        TestContext?.WriteLine(
            $"inventory: delivered={inventory.Delivered} refusal={inventory.Refusal} "
            + $"spent={inventory.WireBudget.Spent}/{inventory.WireBudget.Limit}");

        var decision = LuxembourgOpinionRequestCanaryPlan.AfterInventory(inventory);
        TestContext?.WriteLine($"gate: {decision.Verdict} - {decision.Reason}");

        LuxembourgOpinionRequestGraphResult? batch = null;
        foreach (var ordinal in decision.BatchOrdinalsToAcquire)
        {
            batch = await new LuxembourgOpinionRequestGraphProducer(store, TimeProvider.System)
                .RunAsync(
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
            TestContext?.WriteLine(
                $"batch {ordinal}: delivered={batch.Delivered} refusal={batch.Refusal} "
                + $"spent={batch.WireBudget.Spent}/{batch.WireBudget.Limit}");
        }

        // N = 1 OR 0. The canary acquires at most one batch, and passes exactly what it acquired
        // to the same accounting the full sweep uses.
        LuxembourgOpinionRequestGraphResult[] acquired = batch is null ? [] : [batch];
        var reconciliation = LuxembourgOpinionRequestCanaryPlan.Reconcile(inventory, acquired);
        var verdict = LuxembourgOpinionRequestCanaryPlan.Conclude(decision, inventory, acquired);
        TestContext?.WriteLine(
            $"reconciliation: spent={reconciliation.FinalBudgetSpent} "
            + $"expected={reconciliation.ExpectedIfEverySessionCompleted} "
            + $"reconciles={reconciliation.Reconciles}");

        var index = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                purpose = "E8 bounded canary: measure how many graph rows one batch of 50 "
                    + "OpinionRequest instances delivers, under one shared enforced wire ceiling. "
                    + "Measurement only. Not E8 acceptance, not a closure claim, and not a "
                    + "population guarantee.",
                observedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                head = Git(checkout, "rev-parse", "HEAD"),
                dirtyPaths = Git(checkout, "status", "--porcelain"),
                ceiling = LuxembourgOpinionRequestCanaryPlan.WireCeiling,
                verdict,
                gate = new { decision.Verdict, decision.Reason, decision.BatchOrdinalsToAcquire },
                inventoryRun = new
                {
                    inventory.Delivered,
                    refusal = inventory.Refusal.ToString(),
                    inventory.Detail,
                    inventory.ProductRequestCount,
                    addressableMembers = inventory.Delivered ? inventory.AddressableInOrder().Count : 0,
                    budgetSpent = inventory.WireBudget.Spent,
                    budgetLimit = inventory.WireBudget.Limit,
                    inventory.WireBudget.Exhausted,
                },
                batchRun = batch is null
                    ? null
                    : new
                    {
                        batch.Delivered,
                        refusal = batch.Refusal.ToString(),
                        batch.Detail,
                        batch.ProductRequestCount,

                        // THE MEASUREMENT THE CANARY EXISTS FOR.
                        coveredPairs = batch.Coverage?.CoveredPairCount,
                        presentPairs = batch.Coverage?.PresentPairCount,
                        retainedRows = batch.Coverage?.RetainedRows.Count,
                        derivedAbsences = batch.Coverage?.DerivedAbsences.Count,
                        unresolvedGaps = batch.Coverage?.UnresolvedGaps.Count,
                        budgetSpent = batch.WireBudget.Spent,
                        budgetLimit = batch.WireBudget.Limit,
                        batch.WireBudget.Exhausted,
                    },

                // RECONCILED, AND THE COMPARISON IS COMPUTED RATHER THAN LEFT AS ARITHMETIC FOR A
                // READER. Spent counts reservations taken immediately before send; the recorded
                // count is incremented after each attempt by different code for a different reason.
                // Their agreement is evidence exactly because two mechanisms produced it, and a
                // mismatch is a finding about the accounting itself - so it is stated, not implied.
                reconciliation,
                root,
            },
            new JsonSerializerOptions { WriteIndented = true });

        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var indexPath = Path.Combine(root, "evidence-index.json");
        await File.WriteAllBytesAsync(indexPath, index);
        TestContext?.WriteLine($"evidence: {indexPath}");
        Console.WriteLine(Encoding.UTF8.GetString(index));

        // THE RUN IS NOT ASSERTED INTO SUCCESS. Every outcome above is retained and reported; the
        // only thing that fails this method is the ceiling being breached, because that would mean
        // the one guarantee the owner was given did not hold.
        var finalSpent = batch?.WireBudget.Spent ?? inventory.WireBudget.Spent;
        Assert.IsLessThanOrEqualTo(
            LuxembourgOpinionRequestCanaryPlan.WireCeiling,
            finalSpent,
            "the shared ceiling was exceeded, which is the one outcome this canary must never have.");
    }

    /// <summary>The plan source itself, so the rendered query is attributable to a reviewed file.</summary>
    private static async Task<MachineQueryRendererSource> RendererOfAsync(string checkout)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            checkout,
            "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgOpinionRequestInventoryDiscoveryPlan.cs"));
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    /// <summary>
    /// A bound request against the real Luxembourg profile, used only to resolve the source the
    /// session routes to.
    /// </summary>
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
                "opinion-request-canary",
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
