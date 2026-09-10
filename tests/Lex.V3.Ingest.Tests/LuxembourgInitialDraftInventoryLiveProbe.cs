using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Does Legilux answer the InitialDraft inventory at all? Everything E8 does next depends on it.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS A MEASUREMENT, NOT AN ACCEPTANCE PROOF, and the distinction is the point. The staged
/// class sweep rests on a first stage that the publisher has never been asked. Legilux refused the
/// five-predicate draft-graph query at its pass-one count with
/// <c>Virtuoso SR319: Max row length is exceeded</c>, twice — at seven grouped columns and at
/// three — so whether it will answer a one-column grouping over the same class is open, and
/// building the batching stage on the assumption that it does would be building on a guess.
/// </para>
/// <para>
/// The reviewer's own disposition says what to do with a refusal: stop with it retained, infer no
/// population from it, and introduce no arbitrary lexical root bounds. So this asserts almost
/// nothing about the answer. It records what came back, and it fails only on the two things that
/// would make the record untrustworthy — a request sent somewhere this family may not send one, and
/// a run that reports neither a delivery nor a refusal.
/// </para>
/// <para>
/// SKIPPED BY DEFAULT under <see cref="EnableVariable"/>. One sequential run through the governed
/// routed client, official metadata only, under the owner's standing authorization for E6 and E8
/// live acceptance work.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgInitialDraftInventoryLiveProbe
{
    private const string EnableVariable = "LEX_E8_INVENTORY_LIVE";
    private const string LegiluxEndpoint = "https://data.legilux.public.lu/sparqlendpoint";

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TheInventoryIsAskedOnceAndWhateverCameBackIsRecorded()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 to ask Legilux whether it answers the InitialDraft "
                + "inventory. Skipped by default so the suite sends no unasked traffic.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "e8-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new FileSystemCustodyStore(root);
        var producer = new LuxembourgInitialDraftInventoryProducer(store, TimeProvider.System);
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();

        var result = await producer.RunAsync(
            new LuxembourgInitialDraftInventoryRunRequest(plan, NewUrn(), RendererSource(checkout)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        // The one thing that fails whatever the publisher said: a request this family may not send.
        // It holds on a refused run as much as a delivered one, because a request sent in error is a
        // finding even when the answer was no.
        var offending = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            foreach (var target in RequestTargetsIn(text))
            {
                if (!string.Equals(target, LegiluxEndpoint, StringComparison.Ordinal))
                {
                    offending.Add(Path.GetFileName(file)[..12] + " -> " + target);
                }
            }
        }

        Assert.IsEmpty(
            offending,
            "this probe may contact the Legilux SPARQL endpoint and nothing else: "
            + string.Join("; ", offending.Take(10)));

        var summary = new StringBuilder()
            .AppendLine("e8-inventory-live-probe/1")
            .AppendLine("endpoint=" + LegiluxEndpoint)
            .AppendLine("delivered=" + result.Delivered)
            .AppendLine("refusal=" + result.Refusal)
            .AppendLine("detail=" + (result.Detail ?? string.Empty))
            .AppendLine("product_requests=" + result.ProductRequestCount)
            .AppendLine("subjects=" + (result.Subjects?.Count.ToString() ?? "none"))
            .AppendLine("non_addressable_observed=" + result.ObservedNonAddressable.Count)
            .AppendLine("hypothesis_initial_drafts=8164")
            .ToString();
        await File.WriteAllTextAsync(Path.Combine(root, "probe-summary.txt"), summary);
        TestContext?.WriteLine(summary);

        // A run must come back as one thing or the other. This is the only claim made about the
        // answer itself, and it is about the run rather than the publisher.
        Assert.IsTrue(
            result.Delivered || result.Refusal != LuxembourgInitialDraftInventoryRefusal.None,
            "a run reports a delivery or a typed refusal, never neither.");

        if (!result.Delivered)
        {
            Assert.Inconclusive(
                "Legilux did not answer the inventory, and per the reviewer's disposition that "
                + "refusal is the finding rather than a failure of this probe. Retained under "
                + root + ". Refusal: " + result.Refusal + " " + result.Detail);
        }
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (witnessPlan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9401);
        return witnessPlan.BindCount(
            planResourceId,
            NewUrn(),
            NewUrn(),
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9401)).Request;
    }

    /// <summary>Every target one retained artifact records as a REQUEST.</summary>
    private static IEnumerable<string> RequestTargetsIn(string artifact)
    {
        const string Marker = "\"request_uri\"";
        var index = artifact.IndexOf(Marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var open = artifact.IndexOf('"', index + Marker.Length);
            if (open < 0)
            {
                yield break;
            }

            var close = artifact.IndexOf('"', open + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return artifact[(open + 1)..close];
            index = artifact.IndexOf(Marker, close, StringComparison.Ordinal);
        }
    }

    private static MachineQueryRendererSource RendererSource(string checkout)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            checkout,
            "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgInitialDraftInventoryDiscoveryPlan.cs"));
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                NewUrn(),
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))),
            bytes);
    }

    private static string NewUrn() => "urn:uuid:" + Guid.NewGuid().ToString("D");

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
    }
}
