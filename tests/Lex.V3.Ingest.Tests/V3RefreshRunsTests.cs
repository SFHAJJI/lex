using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the publisher refresh runs need from the tool: <c>compose-inputs</c>, which pairs one acquisition's EU catalog with
/// another acquisition's Luxembourg half after reopening each on its own, and <c>build --acquire-only</c>, which stops once
/// the inputs are retained. Each tool step runs as its own process with every proxy pointed at a local trap.
/// </summary>
public sealed partial class V3FirstMountBuildTests
{
    /// <summary>
    /// A later EU acquisition (run B) paired with an earlier acquisition's Luxembourg half (run A): the tool's inputs derive,
    /// offline, the very files an in-process build of B's EU half and A's Luxembourg half writes at the composed build time,
    /// which is the later of A's build time and B's catalogue time; the report dates both halves from their catalogs; the
    /// in-process composition names the same inputs; and a re-derivation over them cannot be stated to run before that time.
    /// </summary>
    [TestMethod]
    public async Task ComposeInputsPairsOneRunsEuCatalogWithAnotherRunsLuxembourgHalfAndDerivesWhatABuildOfThePairWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var custody = Path.Combine(root, "custody");
            var store = new FileSystemCustodyStore(custody);
            var seed = EuAxiomWiringHarness.Seed(null).Celex;
            var (europeA, luxembourgA) = await AcquireAsync(store, CheckoutRoot());
            Assert.IsTrue(europeA.Delivered, europeA.Detail);
            Assert.IsTrue(luxembourgA.Delivered, luxembourgA.Detail);
            var timeA = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
            var baseInputs = await V3OfflineMount.CaptureAsync(store, europeA, luxembourgA, [seed],
                LuxembourgFirstMountAcquisitionTests.ActRange, timeA, null, CancellationToken.None);
            var basePath = Path.Combine(root, "base-inputs.json");
            await File.WriteAllTextAsync(basePath, ContractJson.Serialize(baseInputs));

            // Run B: the same seeds acquired 40 days later, into the same custody.
            var europeB = await AcquireEuropeAsync(store, TimeSpan.FromDays(40));
            Assert.IsTrue(europeB.Delivered, europeB.Detail);
            Assert.IsNotNull(europeB.CatalogedAt);
            Assert.IsTrue(europeB.CatalogedAt > timeA);
            var europePath = Path.Combine(custody, "eu-acquisition-" + europeB.CheckpointRef!.Sha256 + ".json");
            await File.WriteAllTextAsync(europePath, ContractJson.Serialize(europeB.CheckpointRef));
            var composedTime = V3FirstMountBuild.BuildTimeOf(europeB.CatalogedAt!.Value);

            // What the derive of the composed inputs must write: a build of the pair at the composed build time.
            using var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(composedTime))
                .RunAsync(europeB, luxembourgA, CancellationToken.None);
            Assert.IsTrue(build.Delivered, build.Detail);
            var expected = Path.Combine(root, "expected");
            await V3CorpusMountWriter.WriteAsync(build, expected, null, CancellationToken.None, composedTime);

            var derived = Path.Combine(root, "derived");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string composedPointer;
            try
            {
                var attemptedNetwork = listener.AcceptTcpClientAsync();
                var proxy = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
                var compose = await RunToolAsync(proxy, "compose-inputs", "--custody", custody, "--base", basePath, "--europe", europePath);
                Assert.AreEqual(0, compose.ExitCode, compose.Transcript);
                StringAssert.Contains(compose.StandardOutput, OfflineMeasuredNothing, compose.Transcript);
                var line = compose.StandardOutput.Split('\n').Single(text => text.StartsWith("composed inputs: ", StringComparison.Ordinal));
                composedPointer = line["composed inputs: ".Length..line.IndexOf(';', StringComparison.Ordinal)];
                await RunOfflineCommandAsync(custody, composedPointer, derived, compressed: false, proxy);

                // The composed build time bounds a re-derivation over the composed inputs, as it bounds one over an acquisition's.
                var before = await RunToolAsync(proxy, "rederive-inputs", "--custody", custody, "--base", composedPointer, "--rules",
                    LuxembourgRederivation.Release1RuleSet, "--policy", LuxembourgRightsAdmissionPolicies.Policy1Sha256,
                    "--derived-at", composedTime.AddSeconds(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                Assert.AreEqual(3, before.ExitCode, before.Transcript);
                var after = await RunToolAsync(proxy, "rederive-inputs", "--custody", custody, "--base", composedPointer, "--rules",
                    LuxembourgRederivation.Release1RuleSet, "--policy", LuxembourgRightsAdmissionPolicies.Policy1Sha256,
                    "--derived-at", composedTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                Assert.AreEqual(0, after.ExitCode, after.Transcript);

                var usage = await RunToolAsync(proxy, "compose-inputs", "--custody", custody, "--base", basePath);
                Assert.AreEqual(2, usage.ExitCode, usage.Transcript);
                Assert.IsFalse(attemptedNetwork.IsCompleted, "The offline processes must not attempt publisher traffic.");
            }
            finally { listener.Stop(); }

            CollectionAssert.AreEqual(MountDigests(expected), MountDigests(derived),
                "the derive of the composed inputs and an in-process build of the pair\nExpected:\n" + string.Join("\n", MountDigests(expected))
                + "\nActual:\n" + string.Join("\n", MountDigests(derived)));
            using (var report = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(derived, V3CorpusMountWriter.ReportFileName))))
            {
                var completion = report.RootElement.GetProperty("completion");
                Assert.AreEqual(europeB.CatalogedAt, completion.GetProperty("europe").GetProperty("catalogedAt").GetDateTimeOffset(),
                    "the EU half is dated by its own catalog, never by the composition");
                Assert.AreEqual(luxembourgA.CatalogedAt, completion.GetProperty("luxembourg").GetProperty("catalogedAt").GetDateTimeOffset());
                Assert.IsFalse(report.RootElement.TryGetProperty("resumption", out _), "neither half was resumed");
            }

            // The in-process composition names the same inputs: their identity comes from their bytes.
            var composed = ContractJson.Deserialize<SourceArtifactRef>(await File.ReadAllTextAsync(composedPointer));
            Assert.AreEqual(composed, await V3OfflineMount.ComposeAsync(store, baseInputs, europeB.CheckpointRef, CancellationToken.None));
            var inputs = JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(composed.Sha256, CancellationToken.None)).Span))!;
            Assert.AreEqual("lex-v3-offline-mount-inputs/1", inputs["schema"]!.GetValue<string>(), "the base's schema is kept");
            Assert.AreEqual(europeB.CheckpointRef.Sha256, inputs["europe"]!["sha256"]!.GetValue<string>());
            Assert.AreEqual(luxembourgA.CheckpointRef!.Sha256, inputs["luxembourg"]!["sha256"]!.GetValue<string>());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// Each half is reopened against the base before anything is held, so each mismatch refuses with no inputs held: an EU
    /// catalog of other seeds, an EU catalog that states no time (written before catalogs were dated), a base whose act range
    /// is not its Luxembourg catalog's, and a base that is a re-derivation's inputs.
    /// </summary>
    [TestMethod]
    [DataRow("other_seeds")]
    [DataRow("undated_europe")]
    [DataRow("other_act")]
    [DataRow("rederivation_base")]
    public async Task ComposeInputsRefusesAMismatchBeforeHoldingAnything(string mismatch)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var seed = EuAxiomWiringHarness.Seed(null).Celex;
        var (europe, luxembourg) = await AcquireAsync(store, CheckoutRoot());
        var time = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var baseInputs = await V3OfflineMount.CaptureAsync(store, europe, luxembourg, [seed],
            LuxembourgFirstMountAcquisitionTests.ActRange, time, null, CancellationToken.None);
        var europeCatalog = europe.CheckpointRef!;
        switch (mismatch)
        {
            case "other_seeds":
                europeCatalog = (await EuFirstMountAcquisitionTests.AcquireConsolidatedAsync(store, missingStateCelex: false)).CheckpointRef!;
                break;
            case "undated_europe":
            {
                var catalog = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(europeCatalog.Sha256, CancellationToken.None)).Span))!;
                catalog.Remove("cataloged_at");
                catalog["schema"] = "lex-eu-first-mount-acquisition/1";
                europeCatalog = await HoldAsync(store, catalog);
                Assert.IsNull((await EuFirstMountAcquisition.ReopenAsync(store, europeCatalog, [seed], CancellationToken.None)).CatalogedAt,
                    "the undated catalog itself reopens");
                break;
            }
            case "other_act":
            {
                var inputs = JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(baseInputs.Sha256, CancellationToken.None)).Span))!;
                inputs["act"]!["name"] = "another-act";
                baseInputs = await HoldAsync(store, inputs);
                break;
            }
            case "rederivation_base":
                baseInputs = await V3OfflineMount.CaptureRederivationAsync(store, baseInputs, LuxembourgRederivation.Release1RuleSet,
                    LuxembourgRightsAdmissionPolicies.Policy1Sha256, time, CancellationToken.None);
                break;
        }

        var held = store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            V3OfflineMount.ComposeAsync(store, baseInputs, europeCatalog, CancellationToken.None), mismatch);
        CollectionAssert.AreEqual(held, store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray(),
            "a refused composition holds no inputs");
    }

    /// <summary>
    /// <c>--acquire-only</c> is a flag of <c>build</c> without a value: it takes no <c>--out</c> (none is written), is refused
    /// repeated, and is no acquisition argument, so a journal written without it verifies under it. With it, and with
    /// <c>--lu-population legislative-constitution</c>, the arguments parse and the run reaches the EU acquisition, which
    /// refuses an unknown seed before any request.
    /// </summary>
    [TestMethod]
    public async Task AcquireOnlyIsAFlagThatTakesNoOutAndNoPartInTheJournalsArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-acquire-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var custody = Path.Combine(root, "custody");
            var store = new FileSystemCustodyStore(custody);
            var europe = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
            var luxembourg = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
            var renderers = new[] { europe.Census, europe.ObjectFacts, europe.Witness, europe.DocumentFetch, europe.FormexManifestation, europe.LegalNotice }
                .Select((renderer, index) => new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[index], renderer.Reference))
                .Append(new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[0], luxembourg.Query.Reference))
                .Append(new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[1], luxembourg.DocumentFetch.Reference))
                .ToArray();
            var act = LuxembourgFirstMountAcquisitionTests.ActRange;
            var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(),
                AcquisitionJournal.DigestArguments(["NOTASEED"], act, null, "raw", null), 10, renderers, null);
            var journalPath = Path.Combine(root, "acquisition-progress-interrupted.jsonl");
            using (var stream = new MemoryStream())
            {
                await using (var journal = new AcquisitionJournal(stream, header, WireRequestBudget.OfWireRequests(10), TimeProvider.System))
                {
                    _ = journal;
                }

                await File.WriteAllBytesAsync(journalPath, stream.ToArray());
            }

            var attempted = listener.AcceptTcpClientAsync();
            var proxy = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
            string[] common = ["--custody", custody, "--checkout", CheckoutRoot(), "--wire-ceiling", "10"];
            string[] actRange = ["--lu-name", act.Name, "--lu-start", act.StartInclusive, "--lu-end", act.EndExclusive];

            var withOut = await RunToolAsync(proxy, ["build", "--acquire-only", "--celex", "NOTASEED", .. actRange, .. common, "--out", Path.Combine(root, "mount")]);
            Assert.AreEqual(2, withOut.ExitCode, withOut.Transcript);
            StringAssert.Contains(withOut.StandardError, "--acquire-only writes no mount");
            var repeated = await RunToolAsync(proxy, ["build", "--acquire-only", "--celex", "NOTASEED", .. actRange, .. common, "--acquire-only"]);
            Assert.AreEqual(2, repeated.ExitCode, repeated.Transcript);
            StringAssert.Contains(repeated.StandardError, "Repeated option: --acquire-only");
            var missing = await RunToolAsync(proxy, ["build", "--acquire-only", "--celex", "NOTASEED", "--custody", custody, "--checkout", CheckoutRoot()]);
            Assert.AreEqual(2, missing.ExitCode, missing.Transcript);
            StringAssert.Contains(missing.StandardError, "Missing: --lu-name, --lu-start, --lu-end, --wire-ceiling" + Environment.NewLine,
                "no --out is missing under --acquire-only");
            var unknownPopulation = await RunToolAsync(proxy, ["build", "--acquire-only", "--celex", "NOTASEED", "--lu-population", "constitution", .. common]);
            Assert.AreEqual(2, unknownPopulation.ExitCode, unknownPopulation.Transcript);

            // The journal was written without --acquire-only; it verifies with it, and the run goes on to refuse the seed.
            var resumed = await RunToolAsync(proxy, ["build", "--celex", "NOTASEED", .. actRange, .. common, "--resume-from", journalPath, "--acquire-only"]);
            Assert.AreEqual(3, resumed.ExitCode, resumed.Transcript);
            StringAssert.Contains(resumed.StandardOutput, "verified through seq", resumed.Transcript);
            StringAssert.Contains(resumed.StandardError, "(spent 0 of 10)", resumed.Transcript);

            var population = await RunToolAsync(proxy, ["build", "--acquire-only", "--celex", "NOTASEED", "--lu-population", "legislative-constitution", .. common]);
            Assert.AreEqual(3, population.ExitCode, population.Transcript);
            StringAssert.Contains(population.StandardError, "refused: europe:", population.Transcript);
            StringAssert.Contains(population.StandardError, "(spent 0 of 10)", population.Transcript);

            Assert.IsFalse(attempted.IsCompleted, "every case ends before any request");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "mount")));
            Assert.HasCount(0, Directory.EnumerateFiles(custody, "mount-inputs-*.json"));
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The EU half of <see cref="AcquireAsync"/>, its clock <paramref name="offset"/> later.</summary>
    private static async Task<EuFirstMountAcquisitionResult> AcquireEuropeAsync(ICustodyStore store, TimeSpan offset)
    {
        var seedRoot = EuAxiomWiringHarness.SeedRoot(null);
        var expressionIri = seedRoot + ".0001";
        using var handler = new EuFirstMountAcquisitionTests.CompositeHandler(
            EuAxiomWiringHarness.Scripts(
                seedRoot, static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root), expressionIri: expressionIri),
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [expressionIri] = ["fmx4"] });
        var renderers = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        return await new EuFirstMountAcquisition(store, new EuAcquisitionTestFixture.FixedTimeProvider(offset), handler)
            .RunAsync(EuAxiomWiringHarness.Seed(null).Celex, renderers, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
    }

    private static async Task<SourceArtifactRef> HoldAsync(ICustodyStore store, JsonNode node)
    {
        var receipt = await store.CreateAsync(Encoding.UTF8.GetBytes(EuFirstMountAcquisitionTests.CanonicalJson(node)),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
    }
}
