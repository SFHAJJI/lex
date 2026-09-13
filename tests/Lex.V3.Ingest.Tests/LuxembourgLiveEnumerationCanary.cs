using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LuxembourgLiveEnumerationCanary
{
    /// <summary>The whole-canary charged-request ceiling. Null until the owner dispositions one.</summary>
    /// <remarks>
    /// THE SHAPE IS DERIVABLE, THE NUMBER IS NOT. This canary runs three families, each its own
    /// session: one robots fetch, then two passes of one count and however many pages that count
    /// implies. Under <c>ShortPageTerminal</c> the page count is a function of the rows the
    /// publisher returns for a two-day Code Civil range, and the profile allows four attempts per
    /// request. So the floor is 3 x (1 + 2 x 2) = 15 requests if every page is the only page and
    /// nothing is retried, and the ceiling is that times the retry allowance — 60 — if everything
    /// is. Both are projections of a row count nobody here has measured, which is why a number is
    /// asked for rather than computed. 60 is the arithmetic, not a recommendation.
    /// </para>
    /// <para>
    /// EVERY SEND IS CHARGED, REDIRECT HOPS INCLUDED, since #579's repair: the session reserves each
    /// hop at its own gate before sending it. The Luxembourg profile declares its robots route as a
    /// single step and the SPARQL channel admits no redirect, so every session here is one robots
    /// send and the arithmetic above still holds send for send. If the declared route ever gains a
    /// step, each session charges that hop too and the run stops one product attempt earlier - the
    /// fail-closed direction - and the number below is the owner's to re-derive, not this file's
    /// to grow.
    /// </para>
    /// <para>
    /// DISPOSITIONED BY THE OWNER ON 2026-09-13 AT 60, and the wording of that disposition is the
    /// reason it is safe to write a number here: 60 was accepted as the fail-closed wire ceiling
    /// "because the recorded plan derives it as the exact structural retry maximum", not because it
    /// looked roomy. Setting it bounds a run; it does not authorize one. This harness still refuses
    /// to move without its own enable variable, and the governed traffic gate is unchanged.
    /// </para>
    /// <para>
    /// A CEILING IS NOT A FORECAST. If a real Code Civil two-day range needs more than one page per
    /// count, this run stops at 60 with a WireBudgetExhausted refusal rather than quietly spending
    /// what the arithmetic above did not predict. That is the intended failure: an honest stop that
    /// reports the measurement, against which the next disposition can be made.
    /// </remarks>
    private static readonly int? SharedWireCeiling = 60;

    [TestMethod]
    public async Task CodeCivilFamiliesAreEnumeratedTwiceThroughThePublisherRoute()
    {
        if (Environment.GetEnvironmentVariable("LEX_LU_ENUMERATION_CANARY") != "1")
        {
            Assert.Inconclusive("Set LEX_LU_ENUMERATION_CANARY=1 for the live bounded enumeration proof.");
        }

        // FAIL CLOSED ON A MISSING CEILING, before a root, a store or an executor is built.
        if (SharedWireCeiling is not { } ceiling)
        {
            Assert.Inconclusive(
                "The whole-canary wire ceiling has not been dispositioned. This harness will not "
                + "choose one: set SharedWireCeiling before running it.");
            return;
        }

        // ONE INSTANCE FOR ALL THREE FAMILIES. Three sessions share it, so three robots fetches are
        // charged as three; a budget per family would bound each and leave the canary unbounded.
        var budget = WireRequestBudget.OfWireRequests(ceiling);

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "lu-enumeration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, TimeProvider.System);
        // This is a bounded prerequisite, not the full Luxembourg scope census.
        const string start = "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251226";
        const string end = "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251227";
        var scopeBytes = Encoding.UTF8.GetBytes($"Code Civil lexical partition\nstart={start}\nend={end}\n");
        var scopeReceipt = await store.CreateAsync(scopeBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var scopeRef = new SourceArtifactRef(NewUrn(), scopeReceipt.Reference.ContentSha256);
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(
            OfficialMachineQuerySourceProfiles.Resolve(OfficialMachineQuerySourceProfileId.LuxembourgSparql).ArtifactRef,
            scopeRef);
        var planId = NewUrn();
        var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(checkout,
            "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgQueryPlan.cs"));
        var renderer = MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(sourceBytes))), sourceBytes);
        var partition = new LuxembourgQueryPartitionRange("code-civil",
            new LuxembourgQueryCursor(start, "", "", "", "", ""),
            new LuxembourgQueryCursor(end, "", "", "", "", ""));
        var measured = new List<object>();
        LuxembourgEnumerationRunResult? failed = null;
        foreach (var family in new[] { "S", "A", "G" })
        {
            var request = new LuxembourgPartitionRunRequest(plan, planId, family, partition, renderer);
            var witness = plan.BindCount(planId, NewUrn(), NewUrn(), family,
                LuxembourgQueryPass.Pass1, partition, renderer);
            var outcome = await executor.RunPartitionAsync(
                request, witness.Request, budget, CancellationToken.None);
            measured.Add(new
            {
                family,
                outcome.ProductRequestCount,
                refusal = outcome.Refusal,
                selectedA = outcome.Receipt?.Delivery.SelectedRowCountA,
                selectedB = outcome.Receipt?.Delivery.SelectedRowCountB,
                deliveredA = outcome.Receipt?.Delivery.DeliveredRowCountA,
                deliveredB = outcome.Receipt?.Delivery.DeliveredRowCountB,
                delivery = outcome.Receipt?.Delivery,
                retainedFloor = outcome.Receipt?.RetainedFloor.ToString(),
            });
            if (outcome.Refusal is not null)
            {
                failed = outcome;
                break;
            }
        }

        var members = new List<object>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "nightly-floor-90d")).Order())
        {
            var digest = Path.GetFileName(file);
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, digest, CancellationToken.None);
            members.Add(new { sha256 = digest, byteLength = bytes.Length });
        }
        var index = JsonSerializer.SerializeToUtf8Bytes(new
        {
            purpose = "Bounded live LU repeated enumeration; not complete source or production retention acceptance",
            head = Git(checkout, "rev-parse", "HEAD"),
            dirtyPaths = Git(checkout, "status", "--porcelain"),
            root,
            start,
            end,
            measured,
            members,
        }, new JsonSerializerOptions { WriteIndented = true });
        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var indexPath = Path.Combine(root, "evidence-index.json");
        await File.WriteAllBytesAsync(indexPath, index);
        Console.WriteLine($"LU enumeration evidence: {indexPath}");
        Console.WriteLine(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(measured)));
        Assert.IsNull(failed, $"Live enumeration refused: {failed?.Refusal?.Code}; {failed?.Refusal?.CoreRefusalDetail}. Evidence: {indexPath}");
        Assert.HasCount(3, measured);
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
        var info = new ProcessStartInfo("git") { WorkingDirectory = checkout, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var result = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git failed.");
        return result;
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
