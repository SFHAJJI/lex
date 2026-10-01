using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class V3FirstMountBuildTests
{
    [TestMethod]
    [DataRow(false, false, false, false)]
    [DataRow(true, true, false, false)]
    [DataRow(true, false, true, false)]
    [DataRow(true, false, true, true)]
    public async Task OfflineCommandRebuildsEveryMountFileInTwoSeparateProcesses(bool compressed, bool chained, bool consolidated, bool missingStateCelex)
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-offline-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var custody = Path.Combine(root, "custody");
            var store = compressed ? FileSystemCustodyStore.WithBrotliCompression(custody) : new FileSystemCustodyStore(custody);
            var (europe, luxembourg) = await AcquireAsync(store, CheckoutRoot());
            if (consolidated) europe = await EuFirstMountAcquisitionTests.AcquireConsolidatedAsync(store, missingStateCelex);
            var seed = consolidated ? EuFirstMountAcquisitionTests.ConsolidatedSeed : EuAxiomWiringHarness.Seed(null).Celex;
            Assert.IsTrue(europe.Delivered, europe.Detail);
            Assert.IsTrue(luxembourg.Delivered, luxembourg.Detail);
            var time = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1);
            V3GenerationSource? generations = null;
            LuxembourgIndexPredecessor? predecessor = null;
            if (chained)
            {
                var previous = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(time)).RunAsync(europe, luxembourg, CancellationToken.None);
                Assert.IsTrue(previous.Delivered, previous.Detail);
                var previousPath = Path.Combine(root, "previous");
                await V3CorpusMountWriter.WriteAsync(previous, previousPath, null, CancellationToken.None, time);
                var extra = Path.Combine(previousPath, "unexpected-input.txt");
                await File.WriteAllTextAsync(extra, "not a mount file");
                await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.ValidatePredecessorAsync(previousPath, CancellationToken.None));
                File.Delete(extra);
                predecessor = V3FirstMountBuild.ReadPredecessor(previousPath, out var refusal, out var detail);
                Assert.IsNotNull(predecessor, $"{refusal}: {detail}");
                generations = new(previousPath, new HashSet<string>(StringComparer.Ordinal) { predecessor.IndexSha256 });
                time = time.AddDays(1);
            }
            var checkpoint = await V3OfflineMount.CaptureAsync(store, europe, luxembourg,
                new[] { seed }, LuxembourgFirstMountAcquisitionTests.ActRange, time, generations, CancellationToken.None);
            var referencePath = Path.Combine(root, "inputs.json");
            await File.WriteAllTextAsync(referencePath, ContractJson.Serialize(checkpoint));
            var baseline = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(time)).RunAsync(europe, luxembourg, predecessor, CancellationToken.None);
            Assert.IsTrue(baseline.Delivered, baseline.Detail);
            var expected = Path.Combine(root, "baseline");
            await V3CorpusMountWriter.WriteAsync(baseline, expected, generations, CancellationToken.None, time);
            // Once captured, even the predecessor directory is no longer an input to the command.
            if (generations is not null) Directory.Delete(generations.PredecessorDirectory, recursive: true);
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var attemptedNetwork = listener.AcceptTcpClientAsync();
                var proxy = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                await RunOfflineCommandAsync(custody, referencePath, first, compressed, proxy);
                await RunOfflineCommandAsync(custody, referencePath, second, compressed, proxy);
                Assert.IsFalse(attemptedNetwork.IsCompleted, "The offline processes must not attempt publisher traffic.");
            }
            finally { listener.Stop(); }
            CollectionAssert.AreEqual(MountDigests(expected), MountDigests(first), "Live derivation and first independent replay\nExpected:\n" + string.Join("\n", MountDigests(expected)) + "\nActual:\n" + string.Join("\n", MountDigests(first)));
            CollectionAssert.AreEqual(MountDigests(first), MountDigests(second), "Every file, including report and generations\nFirst:\n" + string.Join("\n", MountDigests(first)) + "\nSecond:\n" + string.Join("\n", MountDigests(second)));
            Assert.IsTrue((await V3CorpusMountWriter.VerifyAsync(second, CancellationToken.None)).Verified);
            if (consolidated)
            {
                using var stateReader = await Lex.V3.Ingest.Europe.EuropeIndexReader.OpenAndVerifyFileAsync(
                    Path.Combine(second, "europe-index.sqlite3"),
                    await File.ReadAllBytesAsync(Path.Combine(second, "europe-capability-manifest.json")),
                    baseline.Corpus!.ArtifactRef, CancellationToken.None);
                Assert.IsTrue(stateReader.HasStates);
                var states = stateReader.ReadStates(seed);
                Assert.HasCount(2, states);
                var expressions = stateReader.ReadStateExpressions(seed);
                Assert.HasCount(4, expressions);
                var consolidatedExpressions = expressions.Where(expression =>
                    expression.PublisherWorkIri == EuFirstMountAcquisitionTests.ConsolidatedWork).ToArray();
                Assert.HasCount(2, consolidatedExpressions);
                CollectionAssert.AreEquivalent(new[] { "eng", "fra" },
                    consolidatedExpressions.Select(expression => expression.Language).ToArray());
                Assert.IsTrue(consolidatedExpressions.All(expression =>
                    expression.PublisherWorkCelex == (missingStateCelex ? null : "02016R0679-20240101") &&
                    expression.ArticleIdentities.Count > 0 && states.Any(state =>
                        state.StateIdentitySha256 == expression.StateIdentitySha256)));
                Assert.HasCount(0, stateReader.ReadStateExpressions("unknown-seed"));
                Assert.HasCount(0, stateReader.ResolveExact(EuFirstMountAcquisitionTests.ConsolidatedWork));
                Assert.HasCount(0, stateReader.ResolveExact("02016R0679-20240101"));
                Assert.HasCount(0, stateReader.ResolveWorkExpressions(EuFirstMountAcquisitionTests.ConsolidatedWork));
                foreach (var expression in consolidatedExpressions)
                {
                    Assert.HasCount(0, stateReader.ResolveExact(expression.PublisherExpressionId));
                    Assert.HasCount(0, stateReader.ResolveExact(expression.ArticleIdentities[0]));
                    var source = stateReader.ReadArticleSourceEvidence(expression.ArticleIdentities[0]);
                    Assert.IsNotNull(source, "State-aware access keeps the held source evidence.");
                    Assert.HasCount(0, stateReader.ResolveExact(
                        Lex.V3.Ingest.Europe.EuropeIndexReader.QualifiedProvisionIdentifierOf(
                            expression.PublisherExpressionId, source.PublisherIdentifier)));
                    Assert.HasCount(0, stateReader.SearchExpressionArticles(expression.Language,
                        new[] { "data" }, expression.PublisherExpressionId)!);
                }
                Assert.HasCount(2, stateReader.ResolveExact(seed));
                var originalWork = states.Single(state => state.DateStatus ==
                    Lex.V3.Ingest.Europe.EuropeIndexStateDateStatus.OriginalWording).PublisherWorkIri;
                Assert.HasCount(2, stateReader.ResolveWorkExpressions(originalWork));
                var originalEnglish = expressions.Single(expression => expression.PublisherWorkIri == originalWork &&
                    expression.Language == "eng");
                Assert.IsTrue(stateReader.SearchExpressionArticles("eng", new[] { "data" },
                    originalEnglish.PublisherExpressionId)!.Count > 0, "Original English text remains searchable.");
                var heldHits = stateReader.Search("eng", new DateOnly(2016, 4, 27), new DateOnly(2016, 4, 27), "data");
                Assert.IsTrue(heldHits.ArticleIdentities.Any(identity =>
                    consolidatedExpressions.Any(expression => expression.ArticleIdentities.Contains(identity))),
                    "The low-level capability search still covers all held articles for state-aware callers.");
                using var connection = Lex.V3.Ingest.Europe.EuropeIndexBuilder.Open(
                    Path.Combine(second, "europe-index.sqlite3"), Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM states";
                Assert.AreEqual(2L, command.ExecuteScalar());
                command.CommandText = "SELECT count(DISTINCT publisher_expression_id) FROM articles";
                Assert.AreEqual(4L, command.ExecuteScalar(), "Both original and consolidated EN/FR texts must reach the complete offline mount.");
                command.CommandText = "SELECT publisher_work_celex FROM states WHERE publisher_consolidation_date='2024-01-01'";
                Assert.AreEqual(missingStateCelex ? DBNull.Value : (object)"02016R0679-20240101", command.ExecuteScalar(),
                    "The publisher's own CELEX remains present or absent without substitution.");
                command.CommandText = "SELECT count(DISTINCT publisher_expression_id) FROM articles WHERE publisher_work_celex IS NULL";
                Assert.AreEqual(missingStateCelex ? 2L : 0L, command.ExecuteScalar(),
                    "Both consolidated languages remain indexed without inventing CELEX.");
            }

            string[] expectedFiles = ["build-report.json", "lex-corpus-6.json", "luxembourg-index.sqlite3",
                "luxembourg-capability-manifest.json", "europe-index.sqlite3", "europe-capability-manifest.json"];
            if (chained)
                expectedFiles = [.. expectedFiles, "generations/retention.json",
                    .. expectedFiles.Select(file => $"generations/{predecessor!.IndexSha256}/{file}")];
            CollectionAssert.AreEqual(expectedFiles.Order(StringComparer.Ordinal).ToArray(),
                Directory.EnumerateFiles(second, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(second, file).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray(),
                "The complete mount contains exactly its six files and, when chained, its retained predecessor and retention decision.");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => V3OfflineMount.DeriveAsync(store, checkpoint, second, CancellationToken.None));

            var originalBytes = await store.ReadByDigestAsync(checkpoint.Sha256, CancellationToken.None);
            var document = JsonNode.Parse(Encoding.UTF8.GetString(originalBytes.Span))!;
            document["predecessor"] = new JsonArray(new JsonObject { ["path"] = "../escape", ["sha256"] = checkpoint.Sha256 });
            var held = await store.CreateAsync(Encoding.UTF8.GetBytes(document.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.DeriveAsync(store,
                new SourceArtifactRef(checkpoint.ResourceId, held.Reference.ContentSha256), Path.Combine(root, "unsafe"), CancellationToken.None));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "unsafe")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task OfflineCommandMissingCatalogAndCancellationDoNotWriteOutput()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var output = Path.Combine(Path.GetTempPath(), "lex-v3-missing-" + Guid.NewGuid().ToString("N"));
        var missing = new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000089", new string('a', 64));
        try { _ = await V3OfflineMount.DeriveAsync(store, missing, output, CancellationToken.None); Assert.Fail("Missing catalog must refuse."); }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => V3OfflineMount.DeriveAsync(store, missing, output, cancellation.Token));
        Assert.IsFalse(Directory.Exists(output));
    }

    private static string[] MountDigests(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') + " " + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))
        .Order(StringComparer.Ordinal).ToArray();

    private static async Task RunOfflineCommandAsync(string custody, string checkpoint, string output, bool compressed, string proxy)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var tool = Path.Combine(CheckoutRoot(), "src", "Lex.V3.Tool", "bin", configuration, "net10.0", "Lex.V3.Tool.dll");
        Assert.IsTrue(File.Exists(tool), tool);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { tool, "derive", "--custody", custody, "--checkpoint", checkpoint, "--out", output,
            "--custody-encoding", compressed ? "brotli" : "raw" }) start.ArgumentList.Add(argument);
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }) start.Environment[name] = proxy;
        start.Environment["NO_PROXY"] = ""; start.Environment["no_proxy"] = "";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.AreEqual(0, process.ExitCode, await stderr + "\n" + await stdout);
        StringAssert.Contains(await stdout, "publisher_requests=0");
    }
}
