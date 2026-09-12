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
/// THE CLASS-SCOPED PREMISE THIS FILE WAS WRITTEN ON IS GONE. An earlier version drove one run whose
/// question was "every InitialDraft", on the stated ground that the family "carries no selection a
/// caller could narrow". #417 has since accepted the batched shape, and
/// <see cref="LuxembourgDraftGraphRunRequest"/>'s constructor is private precisely so no caller can
/// assemble a batch the inventory never issued - so that version no longer compiles, which is the
/// membership repair refusing it rather than a port being awkward. Acceptance is now the whole
/// sequence: enumerate the class, let the inventory issue the batches, sweep every one of them, and
/// reconcile the terminal cover over the result.
/// </para>
/// <para>
/// SKIPPED BY DEFAULT under <see cref="EnableVariable"/>, and it is the most expensive gate in this
/// repository, which is worth stating rather than discovering. The projected volume is recorded at
/// <see cref="ProjectedRequestNote"/> and re-derived from the measured population at run time, so a
/// reader sees the arithmetic rather than a remembered number. Against the measured 7,753 drafts it
/// is on the order of 900 sequential requests under the executor's own robots handling and shared
/// origin pacing - not the 120 the class-scoped version projected, and not the 800 this paragraph
/// carried while <see cref="ProjectedRequestNote"/> said otherwise. There is no smaller honest
/// version of this family's question.
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

    /// <summary>
    /// The whole-run wire ceiling for this acceptance sweep: 2,000 requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE CEILING FOR THE WHOLE RUN. The inventory and every batch it issues share one
    /// <see cref="WireRequestBudget"/>, and every robots fetch, product attempt and retry is charged
    /// to it. An earlier head of this harness built a fresh budget per batch, each the offline test
    /// helper's 100,000, so a 156-batch run had 157 independent ceilings and no bound at all.
    /// </para>
    /// <para>
    /// WHAT 2,000 IS BUILT FROM. 157 robots fetches, one per producer run and never retried; the
    /// inventory's 24 measured product requests; and 156 batches at up to 8 product requests each -
    /// 2 counts plus 3 and 3 pages, which is the <c>MaximumPagesFor</c> page budget at the measured
    /// row band rather than the 6 the canary actually spent. The reviewer's projections on that one
    /// basis: 1,065 at measured density, 1,973 with one retry on every projected product request,
    /// 1,429 with every batch at 8, 1,741 with every batch at 10.
    /// </para>
    /// <para>
    /// IT IS A BINDING STOP, NOT A COMPLETION GUARANTEE AND NOT A STRUCTURAL BOUND. It survives
    /// either a density surprise or a retry on every request, and not both. A run that reaches it
    /// stops, retains what it has, and is a dated partial finding - never a completed sweep.
    /// </para>
    /// <para>
    /// THE EVIDENCE UNDER IT IS THIN, AND THAT IS WHY THE HEADROOM IS SPENT ON DENSITY RATHER THAN
    /// RETRIES. The retained draft sample is 650 of 7,753 drafts - 8.38%, and 19.60% of the two
    /// families it touches. The inventory's own census is <c>pr</c> 4,091, <c>pl</c> 3,306,
    /// <c>ppl</c> 295, <c>ppc</c> 51, <c>pc</c> 10; the sample holds only <c>pl</c> and <c>pc</c>,
    /// so <c>pr</c>, <c>ppl</c> and <c>ppc</c> - 4,437 drafts, 57.23% - have no observations at all,
    /// and the sample's median year is 2003 against the population's 2015. Every rows-per-draft
    /// figure anyone has quoted measures the oldest <c>pl</c>-dominated head.
    /// </para>
    /// <para>
    /// Retries, by contrast, were observed zero times in 100 live product requests. Reserving for a
    /// never-observed multiplier would make the ceiling non-binding on the one dimension that has
    /// actually varied.
    /// </para>
    /// </remarks>
    private static readonly int? SharedWireCeiling = 2_000;
    private const string LegiluxEndpoint = "https://data.legilux.public.lu/sparqlendpoint";

    /// <summary>
    /// What one full acceptance run costs the publisher, derived rather than remembered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both families page under <c>ShortPageTerminal</c>, so a pass ends on the first short page and
    /// the request count is a function of the delivered row count, not of a ceiling. The inventory
    /// enumerates N subjects: one count plus <c>ceil(N/907)</c> pages on pass one, one count plus
    /// <c>ceil(N/613)</c> on pass two. The sweep is <c>ceil(N/50)</c> batches, each one count plus
    /// <c>ceil(R/953)</c> pages on pass one and one count plus <c>ceil(R/571)</c> on pass two, where
    /// R is that batch's delivered rows.
    /// </para>
    /// <para>
    /// R is the part no arithmetic settles, and this note no longer guesses it. It used to project R
    /// from the retained ten-draft delivery's 178 rows, which put a 50-draft batch at about 890 rows
    /// and so at five requests. A real run has since happened and it was more than five.
    /// </para>
    /// <para>
    /// MEASURED, from the truncated acceptance run retained at
    /// <c>artifacts/e8-draft-live-7a07f85b5d924787948c6c1150df070b</c>, request starts
    /// 2026-09-11T07:30:33.1535703Z through 2026-09-11T07:33:21.9788460Z. It issued
    /// <b>94 product requests across fourteen producer runs</b>: 24 for the
    /// inventory - two counts and twenty-two pages, exactly what <c>ceil(7753/907)</c> and
    /// <c>ceil(7753/613)</c> predict - and 70 across thirteen batch runs. Twelve of those batches
    /// completed, consuming 68 requests: <b>eight needed six and four needed five</b>, a mean of
    /// 5.67. The thirteenth refused after two on the long-title row.
    /// </para>
    /// <para>
    /// So N=7,753 is 156 batches and <b>about 910 sequential requests</b> at the sampled rate, not
    /// 804. The retained run averaged 1.8s start to start, which puts a full sweep near
    /// <b>twenty-seven minutes</b> of wall clock rather than twenty.
    /// </para>
    /// <para>
    /// THE SAMPLE BOUNDS NOTHING, and an earlier version of this note said "at most 960" as though
    /// it did. Six is the largest per-batch count OBSERVED, over twelve batches out of 156. It is
    /// not a ceiling: 143 batches were never swept at all, the thirteenth stopped after two requests
    /// before delivering, and what that batch costs once the cursor is repaired is unmeasured. Six
    /// per batch throughout would be 960 requests - a scenario worth holding in mind when scheduling,
    /// never a bound to plan against. R is a function of what the publisher holds, and this family
    /// asks broad-predicate, so a batch of unusually verbose drafts pages more.
    /// </para>
    /// <para>
    /// The run still RECORDS its actual count and nothing here asserts it: twelve batches are a
    /// better sample than ten drafts, and still a sample.
    /// </para>
    /// </remarks>
    private const string ProjectedRequestNote =
        "inventory: 2 counts + ceil(N/907) + ceil(N/613) pages; sweep: ceil(N/50) batches x "
        + "(2 counts + ceil(R/953) + ceil(R/571) pages), R measured per batch. Sampled over the "
        + "twelve complete batches of the retained 2026-09-11T07:30:33.1535703Z run: 24 inventory "
        + "requests and 5.67 per batch (eight at six, four at five). N=7753 projects ~910 sequential "
        + "requests AT THAT SAMPLED RATE. Six throughout would be 960, a scenario and not a bound: "
        + "143 batches were never swept and the refused one never delivered.";

    [TestMethod]
    public async Task TheAcceptedDraftProvisionsAreAnsweredByThePublisher()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 for E8's live draft-graph acceptance proof. It sweeps the "
                + "whole InitialDraft class twice, so it is skipped by default.");
        }

        // FAIL CLOSED ON A MISSING CEILING, before a store, a witness or a plan is built. A sweep
        // that reached the publisher and then discovered it had no agreed bound would already have
        // spent the requests the bound exists to limit.
        if (SharedWireCeiling is not { } ceiling)
        {
            Assert.Inconclusive(
                "The whole-run wire ceiling for the full draft sweep has not been dispositioned. "
                + "This harness will not choose one: set SharedWireCeiling from the reviewed "
                + "full-draft plan before running it.");
            return;
        }

        // ONE INSTANCE, SHARED BY THE INVENTORY AND EVERY BATCH IT ISSUES.
        var budget = WireRequestBudget.OfWireRequests(ceiling);

        // WHEN, not only how many. The owner ruling of 2026-09-11 07:48 admits the measured
        // population as this run's observation rather than as a constant, and requires the terminal
        // receipt to cite the observation AND its time. A receipt carrying a bare count invites the
        // next reader to treat it as the population, which is exactly what 8,164 became.
        var startedAt = TimeProvider.System.GetUtcNow();

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "e8-draft-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new FileSystemCustodyStore(root);
        var witness = LuxembourgDraftGraphProducerTests.LuxembourgSourceWitness();
        var rendererSource = RendererSource(checkout);

        // 1. THE CLASS, ENUMERATED. Everything after this is derived from what the publisher said
        //    the class is; nothing below names a draft this run did not first prove exists.
        // SESSIONS ARE COUNTED BY OBSERVATION, NOT BY ARITHMETIC. A producer call does not always
        // open a session: the doors reserve robots BEFORE StartSessionAsync, so an exhausted budget
        // returns with no session, no request and no spend. Counting attempted calls instead put one
        // phantom robots fetch into the expected total and made `reconciles` false on exactly the
        // pre-session safety stop the index exists to describe.
        var sessionsOpened = 0;
        var spentBeforeRun = budget.Spent;

        var inventory = await new LuxembourgInitialDraftInventoryProducer(store, TimeProvider.System)
            .RunAsync(
                new LuxembourgInitialDraftInventoryRunRequest(
                    LuxembourgInitialDraftInventoryDiscoveryPlan.Create(), NewUrn(), rendererSource,
                    budget),
                witness,
                CancellationToken.None);

        if (budget.Spent > spentBeforeRun)
        {
            sessionsOpened++;
        }

        // RETAINED BEFORE IT IS JUDGED. A refusal here is a safety stop, and a stop is exactly the
        // outcome a bounded run most needs evidence for: what it spent, how far it got and why. The
        // first version asserted delivery first, so on every stopped path the terminal index was
        // never written at all.
        if (inventory.Refusal != LuxembourgInitialDraftInventoryRefusal.None)
        {
            await RetainTerminalIndexAsync(
                root, store, "InventoryRefused", inventory.Refusal.ToString(), inventory.Detail,
                stoppedOrdinal: null, batchesDelivered: 0, batchesIssued: null,
                productRequests: inventory.ProductRequestCount, sessionsOpened: sessionsOpened,
                terminal: inventory.WireBudget);
            Assert.Fail(
                $"the inventory must be proven before any batch is swept: {inventory.Refusal} "
                + $"{inventory.Detail}. Terminal evidence retained under {root}.");
        }

        var population = inventory.AddressableInOrder();

        // 2. THE BATCHES, ISSUED BY THAT INVENTORY. Not assembled here: the factory's only input is
        //    the delivered inventory, which is what makes membership structural.
        var assignments = LuxembourgDraftGraphBatchFactory.AssignBatches(inventory);
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
        var producer = new LuxembourgDraftGraphProducer(store, TimeProvider.System);

        // 3. EVERY BATCH, SWEPT. A refusal stops the run and names the batch rather than leaving a
        //    partial sweep to be reconciled as though it were whole.
        var coverages = new List<LuxembourgDraftPropertyCoverage>(assignments.Count);
        var productRequests = inventory.ProductRequestCount;
        var terminal = inventory.WireBudget;
        var rows = 0L;
        for (var ordinal = 0; ordinal < assignments.Count; ordinal++)
        {
            var spentBeforeBatch = budget.Spent;
            var batch = await producer.RunAsync(
                LuxembourgDraftGraphRunRequest.ForBatch(
                    plan, inventory, ordinal, NewUrn(), rendererSource, budget),
                witness,
                CancellationToken.None);

            if (budget.Spent > spentBeforeBatch)
            {
                sessionsOpened++;
            }

            // TOTALS FIRST, DECISION SECOND. This batch's cost is part of the run's cost whether it
            // delivered or refused, so it is folded in before anything is judged. Assigning after
            // the assertion lost the stopped batch's own attempts and its terminal snapshot, which
            // are the two numbers a stopped run exists to report.
            productRequests += batch.ProductRequestCount;
            terminal = batch.WireBudget;

            if (batch.Refusal != LuxembourgDraftGraphProductionRefusal.None)
            {
                await RetainTerminalIndexAsync(
                    root, store, "BatchRefused", batch.Refusal.ToString(), batch.Detail,
                    stoppedOrdinal: ordinal, batchesDelivered: coverages.Count,
                    batchesIssued: assignments.Count, productRequests: productRequests,
                    sessionsOpened: sessionsOpened, terminal: terminal);
                Assert.Fail(
                    $"batch {ordinal} of {assignments.Count} refused: {batch.Refusal} "
                    + $"{batch.Detail}. Terminal evidence retained under {root}.");
            }

            coverages.Add(batch.Coverage!);
            rows += batch.Coverage!.PublisherRowCount;
        }

        // THE WHOLE RUN'S ACCOUNTING, RECONCILED BEFORE ANY COVER IS READ. One session opened per
        // run - the inventory's and one per batch - and every product attempt reserved, so the
        // reservations the shared budget granted must equal the attempts the producers recorded plus
        // one robots fetch each. A mismatch is a finding about the accounting itself, and a cover
        // read over accounting that does not add up is a conclusion drawn from disputed figures.
        var expectedSpend = productRequests + sessionsOpened;
        await RetainTerminalIndexAsync(
            root, store, "SweepCompleted", refusal: null, detail: null,
            stoppedOrdinal: null, batchesDelivered: coverages.Count,
            batchesIssued: assignments.Count, productRequests: productRequests,
            sessionsOpened: sessionsOpened, terminal: terminal);
        Assert.AreEqual(
            expectedSpend,
            terminal.Spent,
            $"the shared budget granted {terminal.Spent} reservations against {productRequests} "
                + $"recorded product attempts and {sessionsOpened} robots fetches.");
        Assert.IsLessThanOrEqualTo(
            terminal.Limit, terminal.Spent, "the whole-run ceiling was exceeded.");

        // 4. THE TERMINAL COVER. Each batch proved its own matrix; none of them can say the batches
        //    together are the class. This is where a partial sweep stops being readable as a whole
        //    one, and it is the reason this run is an acceptance rather than a sample.
        Assert.AreEqual(
            assignments.Count,
            coverages.Count,
            "the cover is attempted only after every inventory-issued batch delivered; a cover over "
                + "a truncated set would agree with itself about a class it never finished reading.");

        var cover = LuxembourgDraftGraphBatchCover.TryCreate(
            inventory, coverages, out var coverRefusal, out var coverDetail);
        Assert.IsNotNull(
            cover,
            $"the sweep must reconcile against the inventory it covers: {coverRefusal} {coverDetail}");

        // The negative, which holds whether the run completed or refused: a request this family was
        // never authorized to send is a finding even on a failed run.
        var offending = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            foreach (var target in HttpTargetsIn(text))
            {
                if (!IsTheAuthorizedEndpoint(target))
                {
                    offending.Add(Path.GetFileName(file)[..12] + " -> " + target);
                }
            }
        }

        Assert.IsEmpty(
            offending,
            "this run may contact the Legilux SPARQL endpoint and nothing else — CHD draft URLs and "
            + "opinion PDFs stay link-only: " + string.Join("; ", offending.Take(10)));

        var cited = await store.ReadByDigestAsync(
            inventory.CompletionEvidenceRef!.Sha256, CancellationToken.None);
        StringAssert.StartsWith(
            Encoding.UTF8.GetString(cited.Span), "lex-http-acquisition-run/1",
            "the records must cite the acquisition RUN, not one request's HTTP evidence.");

        // THE SHAPE, asserted through the cover rather than recounted here. CoveredPairCount is
        // recomputed from the inventory's own subject count inside TryCreate, so asserting it again
        // against the same inventory is the one thing that would prove nothing. What is worth
        // asserting is that the sweep accounted for every subject the enumeration proved.
        Assert.AreEqual(
            population.Count, cover.SubjectCount,
            "the cover must be of the population this run enumerated.");
        Assert.AreEqual(
            assignments.Count, cover.Batches.Count,
            "every batch the inventory issued is in the cover.");

        // THE NUMBERS, recorded. See the class remarks: 31-v3-spec's figures are a hypothesis that
        // 38-verified-claims declines to treat as fact, and this run is the first honest measurement.
        var summary = new StringBuilder()
            .AppendLine("e8-draft-graph-live-acceptance/2")
            .AppendLine("endpoint=" + LegiluxEndpoint)
            .AppendLine("projection=" + ProjectedRequestNote)
            .AppendLine("product_requests=" + productRequests)
            .AppendLine("sessions_opened=" + sessionsOpened)
            .AppendLine("actual_http_requests=" + terminal.Spent)
            .AppendLine("wire_ceiling=" + terminal.Limit)
            .AppendLine("initial_drafts=" + population.Count)
            .AppendLine("batches=" + assignments.Count)
            .AppendLine("publisher_rows=" + rows)
            .AppendLine("covered_pairs=" + cover.CoveredPairCount)
            .AppendLine("present_pairs=" + cover.PresentPairCount)
            .AppendLine("derived_absences=" + cover.DerivedAbsenceCount)
            .AppendLine("unresolved_gaps=" + cover.UnresolvedGapCount)
            .AppendLine("unconfirmed_drafts=" + cover.UnconfirmedDraftCount)
            .AppendLine("hypothesis_initial_drafts=8164")
            .AppendLine("observed_from=" + startedAt.UtcDateTime.ToString(
                "O", System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine("observed_to=" + TimeProvider.System.GetUtcNow().UtcDateTime.ToString(
                "O", System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine("inventory_completion_evidence=" + inventory.CompletionEvidenceRef!.Sha256)
            .AppendLine("inventory_selection_digest=" + inventory.Citation!.SelectionDigest)
            .ToString();
        await File.WriteAllTextAsync(Path.Combine(root, "acceptance-summary.txt"), summary);
        TestContext?.WriteLine(summary);

        Assert.IsGreaterThan(
            0, population.Count,
            "an empty InitialDraft class would be a complete answer and a product finding; it is not "
            + "what the accepted spec describes, so it fails here rather than passing quietly.");
        Assert.IsGreaterThan(
            0, cover.PresentPairCount,
            "a sweep of the whole class that found no property at all is a finding, not an "
            + "acceptance.");
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

            // An inventory-issued batch, exactly as the live path takes: the run request has no
            // other door, which is the membership repair this harness is downstream of.
            var inventory = LuxembourgDraftGraphProducerTests.InventoryOf(
                LuxembourgDraftGraphProducerTests.DraftForScan);

            var result = await producer.RunAsync(
                LuxembourgDraftGraphRunRequest.ForBatch(
                    plan,
                    inventory,
                    0,
                    NewUrn(),
                    LuxembourgAcquisitionTestFixture.BuildRendererSource(9101),
            LuxembourgAcquisitionTestFixture.TestWireBudget()),
                LuxembourgDraftGraphProducerTests.LuxembourgSourceWitness(),
                CancellationToken.None);

            Assert.AreEqual(
                LuxembourgDraftGraphProductionRefusal.None, result.Refusal,
                $"the scripted run must complete: {result.Refusal} {result.Detail}");

            // AND THE ACCEPTANCE SEQUENCE ITSELF, RECONCILED OFFLINE. The gated body above never
            // runs in an ordinary suite, so its spine would otherwise be unexercised until someone
            // spent some nine hundred live requests discovering it had rotted. This drives the same
            // sequence the live run does - an inventory-issued batch, swept, then reconciled as a
            // terminal cover over that same inventory - and requires the cover to mint rather than
            // merely not throw.
            var cover = LuxembourgDraftGraphBatchCover.TryCreate(
                inventory, [result.Coverage!], out var coverRefusal, out var coverDetail);

            Assert.IsNotNull(
                cover, $"the swept batch must reconcile against its inventory: {coverRefusal} {coverDetail}");
            Assert.AreEqual(
                inventory.AddressableInOrder().Count, cover.SubjectCount,
                "the cover is of the population the inventory proved.");
            Assert.AreEqual(
                LuxembourgDraftGraphBatchFactory.AssignBatches(inventory).Count, cover.Batches.Count,
                "and it carries every batch that inventory issues.");

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
                Assert.IsTrue(
                    IsTheAuthorizedEndpoint(target),
                    $"this family asks {LegiluxEndpoint} and nothing else, and not a child of it: {target}");
            }

            // AND THE GUARD CAN FAIL, proven on a planted target rather than assumed. A child path
            // on the authorized host is the shape that slipped through prefix matching: an opinion
            // body served from the endpoint's own host is still a body this run may not fetch.
            foreach (var planted in new[]
                     {
                         LegiluxEndpoint + "/opinion-body",
                         LegiluxEndpoint + "-other",
                         "https://data.legilux.public.lu/other",
                         "https://www.chd.lu/sparqlendpoint",
                     })
            {
                Assert.IsFalse(
                    IsTheAuthorizedEndpoint(planted),
                    $"{planted} is not the authorized endpoint and must be reported as offending.");
            }

            // The honest shapes still pass, or the negative would fail every real run.
            Assert.IsTrue(IsTheAuthorizedEndpoint(LegiluxEndpoint));
            Assert.IsTrue(
                IsTheAuthorizedEndpoint(LegiluxEndpoint + "?query=SELECT%20*"),
                "a SPARQL question carried in the query string is still the same endpoint.");
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
    /// <summary>
    /// Whether one retained request target is the endpoint this run may contact, and not a child of
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PREFIX MATCHING WAS NOT THE RULE. Both scans asked whether the target STARTED WITH the
    /// endpoint, which accepts <c>/sparqlendpoint/opinion-body</c> - an opinion body fetched from
    /// the same host, which is exactly what S2-A07 and the standing authorization forbid and exactly
    /// what this negative exists to catch. A reviewer planted that target and both checks passed it.
    /// </para>
    /// <para>
    /// The comparison is on the absolute path, ordinal and whole, so a child segment cannot slip
    /// through. The query string is deliberately not part of it: a SPARQL request may legitimately
    /// carry its query there, and requiring the whole URI to be equal would fail an honest run for
    /// asking a question. What is asserted is WHERE the request went, which is the thing the
    /// authorization bounds.
    /// </para>
    /// </remarks>
    private static bool IsTheAuthorizedEndpoint(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var actual) ||
            !Uri.TryCreate(LegiluxEndpoint, UriKind.Absolute, out var allowed))
        {
            return false;
        }

        return string.Equals(actual.Scheme, allowed.Scheme, StringComparison.Ordinal)
            && string.Equals(actual.Host, allowed.Host, StringComparison.Ordinal)
            && actual.Port == allowed.Port
            && string.Equals(actual.AbsolutePath, allowed.AbsolutePath, StringComparison.Ordinal);
    }

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

    /// <summary>
    /// One terminal index, written on every outcome before any of it is judged.
    /// </summary>
    /// <remarks>
    /// Mirrors the integrated OpinionRequest sweep. A run that stopped is the run whose cost most
    /// needs stating, so the index is written for refusals and exhaustion exactly as for success,
    /// and it names the ordinal the sweep stopped at rather than leaving a reader to infer it.
    /// </remarks>
    internal static async Task RetainTerminalIndexAsync(
        string root,
        FileSystemCustodyStore store,
        string verdict,
        string? refusal,
        string? detail,
        int? stoppedOrdinal,
        int batchesDelivered,
        int? batchesIssued,
        int productRequests,
        int sessionsOpened,
        WireBudgetSnapshot terminal)
    {
        var expected = productRequests + sessionsOpened;
        var index = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                purpose = "E8 full draft-graph acceptance: terminal accounting, retained on every "
                    + "outcome including safety stops.",
                observedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                verdict,
                refusal,
                detail,
                stoppedOrdinal,
                batchesDelivered,
                batchesIssued,
                productRequests,
                sessionsOpened,
                actualHttpRequests = terminal.Spent,
                wireCeiling = terminal.Limit,
                terminal.Exhausted,
                expectedIfEverySessionCompleted = expected,
                reconciles = terminal.Spent == expected,
                root,
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(root, "terminal-index.json"), index);
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
