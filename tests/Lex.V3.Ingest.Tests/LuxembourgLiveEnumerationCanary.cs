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
    [TestMethod]
    public async Task CodeCivilFamiliesAreEnumeratedTwiceThroughThePublisherRoute()
    {
        if (Environment.GetEnvironmentVariable("LEX_LU_ENUMERATION_CANARY") != "1")
        {
            Assert.Inconclusive("Set LEX_LU_ENUMERATION_CANARY=1 for the live bounded enumeration proof.");
        }

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
            var outcome = await executor.RunPartitionAsync(request, witness.Request, CancellationToken.None);
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
