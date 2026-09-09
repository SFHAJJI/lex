using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>The opt-in official-source acceptance run for the final E5 bridge population.</summary>
[TestClass]
[DoNotParallelize]
public sealed class EuTranspositionBridgeLivePopulation
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [TestMethod]
    public async Task TheCompletedLegiluxAndCellarFamiliesReconcileIntoOneEvidenceBoundPopulation()
    {
        if (Environment.GetEnvironmentVariable("LEX_E5_LIVE_POPULATION") != "1")
        {
            Assert.Inconclusive(
                "Set LEX_E5_LIVE_POPULATION=1 to run the complete official-source E5 population.");
        }

        var checkout = CheckoutRoot();
        var configuredRoot = Environment.GetEnvironmentVariable("LEX_E5_EVIDENCE_ROOT");
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(checkout, "artifacts", "e5-live-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(configuredRoot);
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);
        var startedUtc = DateTimeOffset.UtcNow;

        var nimPlan = EuNationalImplementingMeasureDiscoveryPlan.Create();
        var nimRenderer = await RendererAsync(
            store, checkout,
            "src/Lex.V3.Contracts/Source/Europe/EuNationalImplementingMeasureDiscoveryPlan.cs");
        var nim = await new EuNationalImplementingMeasureProducer(store, TimeProvider.System).RunAsync(
            new EuNationalImplementingMeasureRunRequest(
                nimPlan,
                NewUrn(),
                nimRenderer),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);
        Assert.IsTrue(nim.Delivered, $"NIM refused: {nim.Refusal}: {nim.Detail}");
        Assert.IsNotNull(nim.Relations);
        Assert.IsNotNull(nim.CompletionEvidenceRef);

        var identityPlan = LuxembourgTranspositionIdentityDiscoveryPlan.Create();
        var directiveElis = nim.Relations
            .Where(static relation => relation.WorkKindAssertion.Kind == EuWorkKind.Directive)
            .Select(static relation => relation.WorkKindAssertion.Work.Value(FactsIdentifierFamily.Eli))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var contradictoryNimIdentity = nim.Relations.FirstOrDefault(relation =>
            !EuTranspositionBridgeReconciliationProducer.WorkKindMatchesEli(
                relation.WorkKindAssertion.Kind,
                relation.WorkKindAssertion.Work.Value(FactsIdentifierFamily.Eli)!));
        Assert.IsNull(
            contradictoryNimIdentity,
            contradictoryNimIdentity is null
                ? string.Empty
                : $"Cellar work {contradictoryNimIdentity.EuWorkUri} has publisher kind " +
                  $"{contradictoryNimIdentity.WorkKindAssertion.Kind} but ELI " +
                  contradictoryNimIdentity.WorkKindAssertion.Work.Value(FactsIdentifierFamily.Eli) + ".");
        Assert.IsGreaterThan(0, directiveElis.Length, "The completed NIM family contains no directive ELI selection.");
        var identityRenderer = await RendererAsync(
            store, checkout,
            "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgTranspositionIdentityDiscoveryPlan.cs");
        var identityProducer = new LuxembourgTranspositionIdentityProducer(store, TimeProvider.System);
        var completedBatches = new List<LuxembourgTranspositionIdentityCompletedBatch>();
        foreach (var selection in directiveElis.Chunk(
                     LuxembourgTranspositionIdentityDiscoveryPlan.BatchCapacity))
        {
            var batch = await identityProducer.RunAsync(
                new LuxembourgTranspositionIdentityRunRequest(
                    identityPlan,
                    selection,
                    NewUrn(),
                    identityRenderer),
                LuxembourgSourceWitness(),
                CancellationToken.None);
            Assert.IsTrue(
                batch.Delivered,
                $"Legilux identity batch refused: {batch.Refusal}: {batch.Detail}");
            completedBatches.Add(new LuxembourgTranspositionIdentityCompletedBatch(selection, batch));
        }
        var identities = await new LuxembourgTranspositionIdentityPopulationProducer(store).ProduceAsync(
            completedBatches,
            NewUrn(),
            CancellationToken.None);
        Assert.IsTrue(
            identities.Delivered,
            $"Legilux identity family refused: {identities.Refusal}: {identities.Detail}");
        Assert.IsNotNull(identities.Relations);
        Assert.IsNotNull(identities.CompletionEvidenceRef);

        var legilux = LuxembourgTranspositionProducer.Produce(identities);
        Assert.IsTrue(legilux.Delivered, $"Legilux projection refused: {legilux.Refusal}: {legilux.Detail}");

        var reconciliation = await new EuTranspositionBridgeReconciliationProducer(store).ProduceAsync(
            legilux,
            identities,
            nim,
            CancellationToken.None);
        Assert.IsTrue(
            reconciliation.Delivered,
            $"E5 reconciliation refused: {reconciliation.Refusal}: {reconciliation.Detail}");
        Assert.IsNotNull(reconciliation.Population?.Rows);
        Assert.IsNotNull(reconciliation.Reconciliations);
        Assert.IsTrue(nim.Relations.Count > 0, "A zero-row publisher run is complete but proves no live population.");
        Assert.IsTrue(identities.Relations.Count > 0, "A zero-row publisher run is complete but proves no live population.");

        var rows = reconciliation.Population.Rows;
        var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "lex-e5-live-two-source-population/1",
            startedUtc,
            completedUtc = DateTimeOffset.UtcNow,
            head = Git(checkout, "rev-parse", "HEAD"),
            dirtyPaths = Git(checkout, "status", "--porcelain"),
            nim = new
            {
                nim.ProductRequestCount,
                relationCount = nim.Relations.Count,
                completion = nim.CompletionEvidenceRef,
            },
            legilux = new
            {
                identities.ProductRequestCount,
                batchCount = completedBatches.Count,
                identityCount = identities.Relations.Count,
                relationCount = legilux.Relations!.Count,
                completion = identities.CompletionEvidenceRef,
            },
            population = new
            {
                workCount = rows.Count,
                legiluxAssertionCount = rows.Sum(static row => row.Bridge.Legilux.Sides.Count),
                nimAssertionCount = rows.Sum(static row => row.Bridge.Nim.Sides.Count),
                normalisedJoinCount = rows.Sum(static row => row.Bridge.NormalisedEliJoins.Count),
                heldJoinEvidenceCount = rows.Sum(static row => row.NormalisedEliJoinEvidenceReceipts.Count),
                reconciliationCount = reconciliation.Reconciliations.Count,
            },
            limitations = new[]
            {
                "This is an opt-in official-source acceptance run, not production promotion.",
                "FileSystemCustodyStore reports unenforced local retention; publisher bytes remain content-addressed.",
                "Counts are derived from the delivered producer results and are not acceptance criteria by themselves.",
            },
        }, JsonOptions);
        var receipt = await store.CreateAsync(
            receiptBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await CustodyRestore.ReadByDigestCheckedAsync(
            store, receipt.Reference.ContentSha256, CancellationToken.None);
        var indexPath = Path.Combine(root, "e5-live-population-receipt.json");
        await File.WriteAllBytesAsync(indexPath, receiptBytes);
        Console.WriteLine(
            $"E5 live population receipt: {indexPath}; " +
            $"sha256={Convert.ToHexStringLower(SHA256.HashData(receiptBytes))}; " +
            $"bytes={receiptBytes.Length}; works={rows.Count}; " +
            $"legilux={legilux.Relations!.Count}; nim={nim.Relations.Count}; " +
            $"joins={rows.Sum(static row => row.Bridge.NormalisedEliJoins.Count)}");
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(4144);
        return plan.BindCount(
            planResourceId,
            NewUrn(),
            NewUrn(),
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(4144)).Request;
    }

    private static async Task<MachineQueryRendererSource> RendererAsync(
        ICustodyStore store,
        string checkout,
        string relativePath)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(checkout, relativePath));
        var held = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await CustodyRestore.ReadByDigestCheckedAsync(
            store, held.Reference.ContentSha256, CancellationToken.None);
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), held.Reference.ContentSha256), bytes);
    }

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
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = checkout,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("git failed.");
        }
        return output;
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
