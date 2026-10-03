using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Resuming an interrupted EU acquisition from its progress journal. The journal only says where the finished units are
/// held: each is admitted again through its own checked reader before any request, only what the journal does not name
/// is acquired live, and a journal that does not describe the acquisition refuses while nothing has been spent. The
/// interrupted run is the consolidated fixture's four Formex packages (the GDPR's original wording and one consolidated
/// work, each in English and French), stopped mid-family at the first request of the second package, as a kill would
/// stop it.
/// </summary>
public sealed partial class EuFirstMountAcquisitionTests
{
    private static readonly string ResumeArguments = AcquisitionJournal.DigestArguments(
        [ConsolidatedSeed], LuxembourgFirstMountAcquisitionTests.ActRange, null, "raw", null);

    [TestMethod]
    public async Task AnInterruptedPopulationResumesReplayingWhatItsJournalNamesAndAcquiringOnlyTheRest()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-eu-resume-" + Guid.NewGuid().ToString("N"));
        try
        {
            var interruptedRoot = Path.Combine(root, "interrupted");
            var interrupted = await InterruptConsolidatedAsync(new FileSystemCustodyStore(interruptedRoot),
                (interruptedHeader, interruptedBudget) => AcquisitionJournal.CreateInCustody(interruptedRoot, interruptedHeader,
                    interruptedBudget, new EuAcquisitionTestFixture.FixedTimeProvider()));
            var journalBytes = await File.ReadAllBytesAsync(interrupted.JournalLocation!);
            var snapshot = V3FirstMountBuildTests.MountDigests(interruptedRoot);

            // The resumed run's root holds the interrupted run's custody objects and never its journal, as the runner
            // links them.
            var resumedRoot = Path.Combine(root, "resumed");
            CopyCustodyObjects(interruptedRoot, resumedRoot);
            var store = new FileSystemCustodyStore(resumedRoot);

            // An uninterrupted run of the same population, for the request counts and the projections without URNs.
            using var uninterruptedHandler = new ResumeRecordingHandler(ConsolidatedHandler(), ConsolidatedExpressions());
            var uninterruptedStore = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
            var uninterrupted = await Acquisition(uninterruptedStore, uninterruptedHandler).RunAsync([ConsolidatedSeed],
                await EuRendererSources.FromCheckoutAsync(uninterruptedStore, CheckoutRoot(), CancellationToken.None),
                EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
            Assert.IsTrue(uninterrupted.Delivered, $"{uninterrupted.Refusal}: {uninterrupted.Detail}");

            var time = new EuAcquisitionTestFixture.FixedTimeProvider();
            var resume = await AcquisitionResume.OpenAsync(store, journalBytes, AcquisitionJournal.CurrentSource(),
                ResumeArguments, time, CancellationToken.None);
            var journaledEnumerations = resume.Count(AcquisitionJournal.EuropeFormexEnumerationPhase);
            var journaledPackages = resume.Count(AcquisitionJournal.EuropeFormexPackagePhase);
            Assert.AreEqual(1, resume.Count(AcquisitionJournal.EuropeAdapterPhase));
            Assert.AreEqual(0, resume.Count(AcquisitionJournal.EuropeCatalogPhase));
            Assert.IsTrue(journaledEnumerations >= 2 && journaledPackages >= 1 && journaledPackages < 4,
                $"{journaledEnumerations} enumeration(s) and {journaledPackages} package(s) journaled: the run must stop mid-population");

            var held = await resume.VerifyRenderersAsync(CheckoutRoot(),
                [.. EuRendererSources.RendererFiles, .. LuxembourgRendererSources.RendererFiles], CancellationToken.None);
            var europeRenderers = await EuRendererSources.FromCustodyAsync(store,
                EuRendererSources.RendererFiles.ToDictionary(file => file, file => held[file], StringComparer.Ordinal), CancellationToken.None);
            var luxembourgRenderers = await LuxembourgRendererSources.FromCustodyAsync(store,
                LuxembourgRendererSources.RendererFiles.ToDictionary(file => file, file => held[file], StringComparer.Ordinal), CancellationToken.None);

            using var handler = new ResumeRecordingHandler(ConsolidatedHandler(), ConsolidatedExpressions());
            var budget = EuAcquisitionTestFixture.TestWireBudget();
            EuFirstMountAcquisitionResult europe;
            string continuedJournal;
            await using (var journal = AcquisitionJournal.CreateInCustody(resumedRoot,
                new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), ResumeArguments, budget.Limit,
                    RendererRoles(europeRenderers, luxembourgRenderers), resume.Continuation), budget, time))
            {
                continuedJournal = journal.Location!;
                var acquisition = new EuFirstMountAcquisition(store, time, handler, journal, resume);
                Assert.IsNull(await acquisition.PrepareResumeAsync([ConsolidatedSeed], europeRenderers, null, CancellationToken.None));
                Assert.IsEmpty(handler.Kinds, "everything the journal names is reopened before any request");
                europe = await acquisition.RunAsync([ConsolidatedSeed], europeRenderers, budget, CancellationToken.None);
            }

            Assert.IsTrue(europe.Delivered, $"{europe.Refusal}: {europe.Detail}");

            // No adapter request: the adapter run was replayed. The rights notice is this build's own (Decision 95), and
            // only the units the journal does not name went to the publisher: every enumeration is two passes of a count
            // and a page, every package a 303 and its ZIP. The unit in flight at the stop is among them.
            Assert.AreEqual(0, handler.Count("adapter"));
            Assert.AreEqual(1, handler.Count("rights"));
            Assert.AreEqual(uninterruptedHandler.Count("enumeration") - (4 * journaledEnumerations), handler.Count("enumeration"));
            Assert.AreEqual(uninterruptedHandler.Count("package") - (2 * journaledPackages), handler.Count("package"));

            // The catalog says it resumed, from what, and what it replayed and acquired live.
            var resumption = europe.Resumption ?? throw new AssertFailedException("A resumed population says so in its catalog.");
            Assert.AreEqual(resume.JournalSha256, resumption.JournalSha256);
            Assert.AreEqual(resume.LastSeq, resumption.LastSeq);
            CollectionAssert.AreEqual(
                new[]
                {
                    AcquisitionJournal.EuropeAdapterPhase + " 1 0",
                    FormattableString.Invariant($"{AcquisitionJournal.EuropeFormexEnumerationPhase} {journaledEnumerations} {4 - journaledEnumerations}"),
                    FormattableString.Invariant($"{AcquisitionJournal.EuropeFormexPackagePhase} {journaledPackages} {4 - journaledPackages}"),
                },
                resumption.Phases.Select(static phase => FormattableString.Invariant($"{phase.Phase} {phase.Replayed} {phase.Live}")).ToArray());

            // Byte equality with an uninterrupted run is impossible (every run mints its own identities); what it says
            // without them is the same.
            CollectionAssert.AreEqual(Projection(uninterrupted), Projection(europe));
            var reopened = await EuFirstMountAcquisition.ReopenAsync(store, europe.CheckpointRef!, [ConsolidatedSeed], CancellationToken.None);
            Assert.IsTrue(reopened.Delivered, reopened.Detail);
            Assert.AreEqual(resumption.Record, reopened.Resumption!.Record);
            CollectionAssert.AreEqual(Projection(europe), Projection(reopened));

            // The resumed run's own journal names the journal it resumed and every unit, replayed or live.
            var continued = new AcquisitionResume(await File.ReadAllBytesAsync(continuedJournal));
            Assert.AreEqual(resume.JournalSha256, continued.Header.ResumedFrom!.JournalSha256);
            Assert.AreEqual(resume.Record, continued.Header.ResumedFrom.Record);
            Assert.AreEqual(1, continued.Count(AcquisitionJournal.EuropeAdapterPhase));
            Assert.AreEqual(4, continued.Count(AcquisitionJournal.EuropeFormexEnumerationPhase));
            Assert.AreEqual(4, continued.Count(AcquisitionJournal.EuropeFormexPackagePhase));
            Assert.AreEqual(1, continued.Count(AcquisitionJournal.EuropeCatalogPhase));
            CollectionAssert.AreEqual(snapshot, V3FirstMountBuildTests.MountDigests(interruptedRoot), "the interrupted run's root is only read");

            // The mount built from the resumed population is the mount two offline derivations rebuild, report included,
            // and its report says the population was not observed in one window.
            using var luxembourgHandler = new LuxembourgFirstMountAcquisitionTests.LuxembourgFamilyHandler(LuxembourgFirstMountAcquisitionTests.PdfBytes());
            var luxembourg = await new LuxembourgFirstMountAcquisition(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), luxembourgHandler)
                .RunAsync(LuxembourgFirstMountAcquisitionTests.ActRange, luxembourgRenderers, LuxembourgAcquisitionTestFixture.TestWireBudget(),
                    CancellationToken.None);
            Assert.IsTrue(luxembourg.Delivered, luxembourg.Detail);
            var buildTime = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1);
            var inputs = await V3OfflineMount.CaptureAsync(store, europe, luxembourg, [ConsolidatedSeed],
                LuxembourgFirstMountAcquisitionTests.ActRange, buildTime, null, CancellationToken.None);
            var inputsPath = Path.Combine(root, "inputs.json");
            await File.WriteAllTextAsync(inputsPath, ContractJson.Serialize(inputs));
            var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(buildTime)).RunAsync(europe, luxembourg, CancellationToken.None);
            Assert.IsTrue(build.Delivered, build.Detail);
            var expected = Path.Combine(root, "mount");
            await V3CorpusMountWriter.WriteAsync(build, expected, null, CancellationToken.None, buildTime);
            var report = await File.ReadAllTextAsync(Path.Combine(expected, "build-report.json"));
            StringAssert.Contains(report, "\"resumedFrom\"");
            StringAssert.Contains(report, resume.JournalSha256);
            StringAssert.Contains(report, "It was not observed in one window.");

            var first = Path.Combine(root, "derive-a");
            var second = Path.Combine(root, "derive-b");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var attemptedNetwork = listener.AcceptTcpClientAsync();
                var proxy = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
                await V3FirstMountBuildTests.RunOfflineCommandAsync(resumedRoot, inputsPath, first, false, proxy);
                await V3FirstMountBuildTests.RunOfflineCommandAsync(resumedRoot, inputsPath, second, false, proxy);
                Assert.IsFalse(attemptedNetwork.IsCompleted, "The offline processes must not attempt publisher traffic.");
            }
            finally
            {
                listener.Stop();
            }

            CollectionAssert.AreEqual(V3FirstMountBuildTests.MountDigests(expected), V3FirstMountBuildTests.MountDigests(first));
            CollectionAssert.AreEqual(V3FirstMountBuildTests.MountDigests(first), V3FirstMountBuildTests.MountDigests(second));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("swapped_digest")]
    [DataRow("foreign_adapter_run")]
    [DataRow("leftover_unit")]
    [DataRow("changed_renderer")]
    public async Task AJournalThatDoesNotDescribeThisAcquisitionRefusesBeforeAnyRequest(string fault)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        using var stream = new MemoryStream();
        var interrupted = await InterruptConsolidatedAsync(store, (interruptedHeader, interruptedBudget) =>
            new AcquisitionJournal(stream, interruptedHeader, interruptedBudget, new EuAcquisitionTestFixture.FixedTimeProvider()));
        var journal = stream.ToArray();
        var copy = await CopyAcquisitionStoreAsync(store);
        switch (fault)
        {
            case "swapped_digest":
                // Two enumerations' checkpoints exchanged: each still reads back, and each checked reader refuses the
                // expression it is offered.
                journal = await RejournalAsync(journal, interrupted.Header, units =>
                {
                    var enumerations = units.Where(static unit => unit.Phase == AcquisitionJournal.EuropeFormexEnumerationPhase).ToArray();
                    var checkpoint = enumerations[0].Payload["enumeration"]!["checkpoint"]!.DeepClone();
                    enumerations[0].Payload["enumeration"]!["checkpoint"] = enumerations[1].Payload["enumeration"]!["checkpoint"]!.DeepClone();
                    enumerations[1].Payload["enumeration"]!["checkpoint"] = checkpoint;
                });
                break;
            case "foreign_adapter_run":
                // A complete adapter run of the same seeds under the same renderer sources, but not the one these Formex
                // units were acquired for.
                var other = await Acquisition(copy, ConsolidatedHandler()).RunAsync([ConsolidatedSeed], interrupted.Europe,
                    EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
                Assert.IsTrue(other.Delivered, $"{other.Refusal}: {other.Detail}");
                var query = other.Run!.AcquisitionCheckpointRef!;
                journal = await RejournalAsync(journal, interrupted.Header, units =>
                {
                    var index = units.FindIndex(static unit => unit.Phase == AcquisitionJournal.EuropeAdapterPhase);
                    units[index] = units[index] with { Payload = JsonNode.Parse(ContractJson.Serialize(query))!, Held = [query.Sha256] };
                });
                break;
            case "leftover_unit":
                // An enumeration of an expression the run does not hold: the walk never reaches it.
                journal = await RejournalAsync(journal, interrupted.Header, units =>
                {
                    var template = units.First(static unit => unit.Phase == AcquisitionJournal.EuropeFormexEnumerationPhase);
                    var stranger = new string('e', 64);
                    var payload = template.Payload.DeepClone();
                    payload["enumeration"]!["expression_sha256"] = stranger;
                    units.Add(template with { Key = stranger, Payload = payload });
                });
                break;
        }

        var resume = await AcquisitionResume.OpenAsync(copy, journal, AcquisitionJournal.CurrentSource(), ResumeArguments,
            TimeProvider.System, CancellationToken.None);
        using var handler = new ResumeRecordingHandler(ConsolidatedHandler(), ConsolidatedExpressions());
        if (fault == "changed_renderer")
        {
            var checkout = Path.Combine(Path.GetTempPath(), "lex-v3-changed-checkout-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var file in EuRendererSources.RendererFiles.Concat(LuxembourgRendererSources.RendererFiles))
                {
                    var target = Path.Combine(checkout, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(CheckoutRoot(), file), target);
                }

                await File.AppendAllTextAsync(Path.Combine(checkout, EuRendererSources.RendererFiles[4]), "\n// Changed renderer.\n");
                var error = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => resume.VerifyRenderersAsync(checkout,
                    [.. EuRendererSources.RendererFiles, .. LuxembourgRendererSources.RendererFiles], CancellationToken.None));
                StringAssert.Contains(error.Message, "differs from the renderer source");
            }
            finally
            {
                Directory.Delete(checkout, recursive: true);
            }
        }
        else
        {
            var acquisition = new EuFirstMountAcquisition(copy, TimeProvider.System, handler, null, resume);
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => acquisition.PrepareResumeAsync(
                [ConsolidatedSeed], interrupted.Europe, null, CancellationToken.None));
        }

        Assert.IsEmpty(handler.Kinds, "a journal that does not describe this acquisition refuses before any request");
    }

    private sealed record InterruptedRun(EuRendererSources Europe, LuxembourgRendererSources Luxembourg,
        AcquisitionJournalHeader Header, string? JournalLocation);

    internal sealed record JournalUnit(string Phase, string Key, JsonNode Payload, string[] Held);

    // The interrupted run every resume test starts from: the consolidated fixture stopped at the first request of its
    // second package, mid-family. Its journal then names the adapter run, the enumerations of the first family and the
    // first package; the package in flight has no line.
    private static async Task<InterruptedRun> InterruptConsolidatedAsync(ICustodyStore store,
        Func<AcquisitionJournalHeader, WireRequestBudget, AcquisitionJournal> openJournal)
    {
        var europe = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var luxembourg = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), ResumeArguments, budget.Limit,
            RendererRoles(europe, luxembourg), null);
        using var stop = new CancellationTokenSource();
        using var handler = new ResumeRecordingHandler(ConsolidatedHandler(), ConsolidatedExpressions(), interruptAtPackageRequest: 3, stop);
        string? location;
        await using (var journal = openJournal(header, budget))
        {
            location = journal.Location;
            await Assert.ThrowsAsync<OperationCanceledException>(() => new EuFirstMountAcquisition(store,
                new EuAcquisitionTestFixture.FixedTimeProvider(), handler, journal).RunAsync([ConsolidatedSeed], europe, budget, stop.Token));
        }

        Assert.AreEqual(3, handler.Count("package"), "the run stopped at its second package's first request");
        return new InterruptedRun(europe, luxembourg, header, location);
    }

    private static AcquisitionJournalRenderer[] RendererRoles(EuRendererSources europe, LuxembourgRendererSources luxembourg) =>
    [
        new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[0], europe.Census.Reference),
        new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[1], europe.ObjectFacts.Reference),
        new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[2], europe.Witness.Reference),
        new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[3], europe.DocumentFetch.Reference),
        new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[4], europe.FormexManifestation.Reference),
        new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[5], europe.LegalNotice.Reference),
        new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[0], luxembourg.Query.Reference),
        new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[1], luxembourg.DocumentFetch.Reference),
    ];

    private static string[] ConsolidatedExpressions()
    {
        var root = EuAxiomWiringHarness.SeedRoot(ConsolidatedSeed);
        return [root + ".0001", root + ".0002", ConsolidatedWork + ".0001", ConsolidatedWork + ".0002"];
    }

    // What a population says without the identities a run mints: each outcome's expression, kind, package digest and
    // annex count, each held body's digest, and the population's counts.
    private static string[] Projection(EuFirstMountAcquisitionResult result) =>
        result.Formex!.Reconciliation!.Outcomes.Select(static outcome => FormattableString.Invariant(
                $"outcome {outcome.ExpressionIdentity.PublisherExpressionId} {outcome.Kind} {outcome.AcquiredInventory?.SourceReceipt.Reference.ContentSha256} {outcome.AcquiredInventory?.Members.Count}"))
            .Concat(result.Run!.CorpusRecordSet!.Set.Records.Select(static record => "body " + record.Body.Receipt?.Reference.ContentSha256))
            .Append(FormattableString.Invariant(
                $"annexes {result.Formex.AnnexClassifications.Count} acquired {result.Formex.AcquiredExpressionCount} eligible {result.Formex.EligibleExpressionCount}"))
            .Order(StringComparer.Ordinal).ToArray();

    // The runner's copy: every custody object of the interrupted root except an unpublished write, and never a file of
    // the root itself (the journal).
    private static void CopyCustodyObjects(string from, string to)
    {
        foreach (var lane in Directory.GetDirectories(from))
        {
            var target = Path.Combine(to, Path.GetFileName(lane));
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(lane).Where(static file => !file.EndsWith(".partial", StringComparison.Ordinal)))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
    }

    // The interrupted journal written again through the production writer after edit, so its chain is whole and only its
    // content says what changed.
    internal static async Task<byte[]> RejournalAsync(byte[] journal, AcquisitionJournalHeader header, Action<List<JournalUnit>> edit)
    {
        var units = Encoding.UTF8.GetString(journal).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(static line => JsonNode.Parse(line)!)
            .Select(static node => new JournalUnit(node["phase"]!.GetValue<string>(), node["key"]!.GetValue<string>(),
                node["payload"]!.DeepClone(), node["held"]!.AsArray().Select(static digest => digest!.GetValue<string>()).ToArray()))
            .ToList();
        edit(units);
        using var stream = new MemoryStream();
        await using (var writer = new AcquisitionJournal(stream, header, WireRequestBudget.OfWireRequests(2), TimeProvider.System))
        {
            foreach (var unit in units)
            {
                using var payload = JsonDocument.Parse(unit.Payload.ToJsonString());
                await writer.AppendAsync(unit.Phase, unit.Key, payload.RootElement.Clone(), unit.Held);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A transport around the composite handler that names every request the way these tests count them (robots, the
    /// rights notice, a Formex enumeration, a Formex package request, or the adapter's own traffic) and can stop the run at
    /// its Nth package request by cancelling it there.
    /// </summary>
    private sealed class ResumeRecordingHandler(HttpMessageHandler inner, IReadOnlyList<string> expressions,
        int interruptAtPackageRequest = 0, CancellationTokenSource? stop = null) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(inner, disposeHandler: false);
        private readonly List<string> _kinds = [];
        private int _packages;

        internal IReadOnlyList<string> Kinds
        {
            get
            {
                lock (_kinds)
                {
                    return _kinds.ToArray();
                }
            }
        }

        internal int Count(string kind)
        {
            lock (_kinds)
            {
                return _kinds.Count(value => value == kind);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var kind = await KindAsync(request, cancellationToken).ConfigureAwait(false);
            lock (_kinds)
            {
                _kinds.Add(kind);
            }

            if (kind == "package" && Interlocked.Increment(ref _packages) == interruptAtPackageRequest && stop is not null)
            {
                await stop.CancelAsync().ConfigureAwait(false);
                throw new OperationCanceledException(stop.Token);
            }

            return await _inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> KindAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/robots.txt") return "robots";
            if (uri.AbsoluteUri == NoticeUri) return "rights";
            if (request.Method == HttpMethod.Get &&
                request.Headers.Accept.ToString().Contains("application/zip;mtype=fmx4", StringComparison.Ordinal))
                return "package";
            if (request.Method == HttpMethod.Post && request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (body.Contains("manifestation_manifests_expression", StringComparison.Ordinal) &&
                    expressions.Any(expression => body.Contains(expression, StringComparison.Ordinal)))
                    return "enumeration";
            }

            return "adapter";
        }
    }
}
