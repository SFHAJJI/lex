using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// E8's live acceptance proof for the Luxembourg draft graph: the accepted provisions answered by
/// the publisher rather than by a fixture.
/// </summary>
/// <remarks>
/// <para>
/// Every other draft-graph test decodes pages a test wrote. That proves the decoder and proves
/// nothing about whether Legilux answers this question at all, or answers it in the shape the plan
/// assumes. This asks.
/// </para>
/// <para>
/// SKIPPED BY DEFAULT under <see cref="EnableVariable"/>, and it is the most expensive gate in this
/// repository, which is worth stating rather than discovering. The family is CLASS-SCOPED by design
/// — its question is "every InitialDraft" and it carries no selection a caller could narrow — so a
/// run is the whole class twice. Against the hypothesised 8,164 drafts and five asked properties
/// that is roughly 41,000 rows, about 43 pages at the pass-one limit of 953 and about 72 at the
/// pass-two limit of 571, plus a count each pass: on the order of 120 sequential requests under the
/// executor's own robots handling and shared origin pacing. There is no smaller honest version of
/// this family's question.
/// </para>
/// <para>
/// THE COUNTS ARE MEASURED HERE, NEVER ASSERTED. 31-v3-spec records InitialDraft 8,164 and
/// draftTransposes 1,735, and 38-verified-claims says plainly that those figures must be treated as
/// a hypothesis rather than a fact, because the endpoint answered 502 when they were taken. A test
/// that asserted them would be pinning a guess and would fail for the publisher being right.
/// What is asserted is the shape the accepted provisions require; what is recorded is the number.
/// </para>
/// <para>
/// CHD IS NEVER CONTACTED. <c>parliamentDraftUrl</c> values point at the Chambre des Députés, and
/// the owner's instruction is that those stay link-only. That is not left to the producer having no
/// fetch path: after the run every retained artifact naming an HTTP target must name the Legilux
/// SPARQL endpoint, so a CHD request would be visible in the run's own evidence. The same check
/// covers S2-A07's neighbour rule, that opinion PDFs carrying no licence triple stay link-only too.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftGraphLiveAcceptance
{
    private const string EnableVariable = "LEX_E8_DRAFT_GRAPH_LIVE";
    private const string LegiluxEndpoint = "https://data.legilux.public.lu/sparqlendpoint";

    [TestMethod]
    public async Task TheAcceptedDraftProvisionsAreAnsweredByThePublisher()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 for E8's live draft-graph acceptance proof. It sweeps the "
                + "whole InitialDraft class twice, so it is skipped by default.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "e8-draft-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new FileSystemCustodyStore(root);
        var producer = new LuxembourgDraftGraphProducer(store, TimeProvider.System);

        var result = await producer.RunAsync(
            new LuxembourgDraftGraphRunRequest(
                LuxembourgDraftGraphDiscoveryPlan.Create(),
                NewUrn(),
                RendererSource(checkout)),
            LuxembourgDraftGraphProducerTests.LuxembourgSourceWitness(),
            CancellationToken.None);

        // The negative first, because it holds whether the run completed or refused: a request this
        // family was never authorized to send is a finding even on a failed run.
        var offending = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            foreach (var target in HttpTargetsIn(text))
            {
                if (!target.StartsWith(LegiluxEndpoint, StringComparison.Ordinal))
                {
                    offending.Add(Path.GetFileName(file)[..12] + " -> " + target);
                }
            }
        }

        Assert.IsEmpty(
            offending,
            "this run may contact the Legilux SPARQL endpoint and nothing else — CHD draft URLs and "
            + "opinion PDFs stay link-only: " + string.Join("; ", offending.Take(10)));

        Assert.AreEqual(
            LuxembourgDraftGraphProductionRefusal.None, result.Refusal,
            $"the live run must complete: {result.Refusal} {result.Detail}");
        Assert.IsGreaterThan(0, result.ProductRequestCount, "a live run sends real requests.");

        var cited = await store.ReadByDigestAsync(
            result.CompletionEvidenceRef!.Sha256, CancellationToken.None);
        StringAssert.StartsWith(
            Encoding.UTF8.GetString(cited.Span), "lex-http-acquisition-run/1",
            "the records must cite the acquisition RUN, not one request's HTTP evidence.");

        var records = result.Records!;
        var asked = LuxembourgDraftGraphDiscoveryPlan.AskedAbout;

        // Every record answers one of the five asked properties, and nothing else arrived.
        foreach (var record in records)
        {
            CollectionAssert.Contains(
                asked.ToArray(), record.PredicateIri,
                "a delivered row names a property this family never asked about.");
        }

        // The accepted E8 provisions name InitialDraft, OpinionConseilEtat and draftTransposes
        // together. A draft-graph run that swept the class and came back with no transposition
        // intention at all would satisfy every structural assertion here and answer none of the
        // provision, so it is called out rather than left to the reader of a number.
        var transpositions = result.For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri);
        var boundTranspositions = transpositions
            .Where(static value => value.ValueKind != LuxembourgDraftGraphDiscoveryPlan.UnboundKind)
            .ToArray();

        var drafts = records.Select(static value => value.DraftIri).Distinct(StringComparer.Ordinal).ToArray();

        // THE SHAPE, asserted. Every delivered draft answers for every asked property — the producer
        // enforces it, and asserting it here is what makes the live delivery prove the invariant
        // rather than the invariant hide a short delivery.
        Assert.AreEqual(
            drafts.Length * asked.Count, records.Count,
            "the absence branch means every draft answers every asked property exactly once.");

        // THE NUMBERS, recorded. See the class remarks: these are the figures 38-verified-claims
        // calls a hypothesis, and this run is the first honest measurement of them.
        var summary = new StringBuilder()
            .AppendLine("e8-draft-graph-live-acceptance/1")
            .AppendLine("endpoint=" + LegiluxEndpoint)
            .AppendLine("product_requests=" + result.ProductRequestCount)
            .AppendLine("initial_drafts=" + drafts.Length)
            .AppendLine("rows=" + records.Count)
            .AppendLine("hypothesis_initial_drafts=8164")
            .AppendLine("hypothesis_draft_transposes=1735")
            .AppendLine("draft_transposes_rows=" + transpositions.Count)
            .AppendLine("draft_transposes_bound=" + boundTranspositions.Length)
            .AppendLine("completion_evidence=" + result.CompletionEvidenceRef!.Sha256)
            .ToString();
        await File.WriteAllTextAsync(Path.Combine(root, "acceptance-summary.txt"), summary);
        TestContext?.WriteLine(summary);

        Assert.IsGreaterThan(
            0, drafts.Length,
            "an empty InitialDraft class would be a complete answer and a product finding; it is not "
            + "what the accepted spec describes, so it fails here rather than passing quietly.");
        Assert.IsGreaterThan(
            0, boundTranspositions.Length,
            "the accepted E8 provisions name draftTransposes; a sweep answering none of them is a "
            + "finding, not an acceptance.");
    }

    /// <summary>
    /// The retained-evidence scan finds the targets it is supposed to find, proven offline.
    /// </summary>
    /// <remarks>
    /// Without this the CHD negative above is vacuous: a scan matching nothing satisfies it
    /// perfectly. The key it reads is <c>RequestUri</c> under this contract's
    /// <c>JsonNamingPolicy.SnakeCaseLower</c>, which a rename would empty without failing anything.
    /// This drives the same producer over the scripted transport into a real
    /// <see cref="FileSystemCustodyStore"/> and requires the scan to come back non-empty and pointed
    /// at Legilux. It is NOT gated, so the live gate's machinery is kept honest by something that
    /// runs in every ordinary suite run.
    /// </remarks>
    [TestMethod]
    public async Task TheRetainedEvidenceScanFindsTheTargetsItIsSupposedToFind()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-e8-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
            var page = LuxembourgDraftGraphProducerTests.PageJson(
                plan.CreateDeliveryProfile().ProjectionVariables);
            var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
                LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
                {
                    1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(
                        LuxembourgDraftGraphDiscoveryPlan.AskedAbout.Count),
                    2 or 4 => page,
                    _ => throw new AssertFailedException("No request after both passes complete."),
                }));

            var store = new FileSystemCustodyStore(root);
            var producer = new LuxembourgDraftGraphProducer(
                store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

            var result = await producer.RunAsync(
                new LuxembourgDraftGraphRunRequest(
                    plan, NewUrn(), LuxembourgAcquisitionTestFixture.BuildRendererSource(9101)),
                LuxembourgDraftGraphProducerTests.LuxembourgSourceWitness(),
                CancellationToken.None);

            Assert.AreEqual(
                LuxembourgDraftGraphProductionRefusal.None, result.Refusal,
                $"the scripted run must complete: {result.Refusal} {result.Detail}");

            var targets = new List<string>();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                targets.AddRange(HttpTargetsIn(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file))));
            }

            Assert.IsNotEmpty(
                targets,
                "the scan must actually find request targets, or the live test's CHD negative is vacuous.");
            foreach (var target in targets)
            {
                StringAssert.StartsWith(
                    target, LegiluxEndpoint, "this family asks Legilux and nothing else.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public TestContext? TestContext { get; set; }

    /// <summary>
    /// Every absolute target one retained artifact records as a REQUEST.
    /// </summary>
    /// <remarks>
    /// Only request records are scanned, never payloads: a delivered row naming a CHD URL is the
    /// publisher's data and is exactly what this family is supposed to carry link-only. Confusing
    /// the two would fail the run for doing its job.
    /// </remarks>
    private static IEnumerable<string> HttpTargetsIn(string artifact)
    {
        const string RequestMarker = "\"request_uri\"";
        var index = artifact.IndexOf(RequestMarker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var open = artifact.IndexOf('"', index + RequestMarker.Length);
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
            index = artifact.IndexOf(RequestMarker, close, StringComparison.Ordinal);
        }
    }

    private static MachineQueryRendererSource RendererSource(string checkout)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            checkout, "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgDraftGraphDiscoveryPlan.cs"));
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
