using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the draft family may send, and what it says it sent.
/// </summary>
/// <remarks>
/// <para>
/// THE ASYMMETRY THIS CLOSES. The OpinionRequest doors reserved every wire request against an
/// enforced ceiling; their draft siblings opened a session first and ran the pass loop with no budget
/// at all. Clause (c) has to run exactly those two doors, so the acceptance path went through the one
/// family the ceiling did not cover.
/// </para>
/// <para>
/// Every case here is offline. A ceiling is proven by showing requests were NOT sent, which a live
/// run cannot demonstrate and a fake transport can.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftBudgetEvidenceTests
{
    /// <summary>
    /// A spent budget opens no session and sends nothing at all.
    /// </summary>
    /// <remarks>
    /// The reservation sits before <c>StartSessionAsync</c>, so the refusal happens with the
    /// transport untouched. Asserted on the transport's own send count, because a refusal that
    /// arrived after robots went out would be a receipt rather than a ceiling.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheInventoryDoor()
    {
        var handler = new CountingHandler();
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt());
        Assert.IsTrue(budget.TryReserveAttempt());

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.AreEqual(0, handler.SendCount, "not one request, not even robots.");
        Assert.IsFalse(result.Delivered);
        StringAssert.Contains(
            result.Detail ?? string.Empty,
            nameof(EuEnumerationRefusal.WireBudgetExhausted),
            "and it must stop on the CEILING, not on something else that also refuses.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
    }

    /// <summary>The same door, at the ceiling mid-pass rather than before it.</summary>
    /// <remarks>
    /// A budget of two covers the robots fetch and the count; the page that count implies is the
    /// request with nothing left to reserve. That places the stop on a request the run genuinely
    /// wanted to send, which is the only arrangement where <c>Spent == Limit</c> means the ceiling
    /// was reached rather than merely quoted.
    /// </remarks>
    [TestMethod]
    public async Task TheInventoryDoorStopsMidPassAtItsCeiling()
    {
        var handler = new CountingHandler(_ => LuxembourgAcquisitionTestFixture.CountJson(2));
        var budget = WireRequestBudget.OfWireRequests(2);

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(2, handler.SendCount, "robots and the count; the page was never sent.");
        Assert.AreEqual(1, handler.RobotsSends);
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
        Assert.AreEqual(
            handler.SendCount, result.WireBudget.Spent,
            "reservations are taken immediately before send, so at the ceiling they are the sends.");
    }

    /// <summary>A refused run still carries its snapshot, and it reconciles.</summary>
    /// <remarks>
    /// The runs that most need reconciling are the ones that stopped. A refusal that dropped its
    /// cost would leave the wire spend of a failed attempt unaccountable.
    /// </remarks>
    [TestMethod]
    public async Task ARefusedInventoryRunCarriesItsSnapshot()
    {
        var handler = new CountingHandler(_ => null);
        var budget = WireRequestBudget.OfWireRequests(100);

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered, "this publisher answered 503 to every product request.");
        Assert.AreEqual(100, result.WireBudget.Limit);
        Assert.IsGreaterThan(0, result.WireBudget.Spent);
        Assert.AreEqual(
            handler.SendCount, result.WireBudget.Spent,
            "a refusal reconciles to the transport exactly as a delivery would.");
    }

    /// <summary>
    /// Retries are reserved like any other attempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source profile permits four attempts per bound request, so a ceiling counting only bound
    /// requests would be wrong by that factor at exactly its own boundary.
    /// </para>
    /// <para>
    /// DRIVEN BY A PRE-HEADER FAILURE, NOT A 503. A 503 is a completed response and is not retried
    /// here, so a case built on one would have proved nothing about retries while appearing to. The
    /// connection error is the shape that actually reaches the retry path.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task RetryAttemptsAreReservedToo()
    {
        var handler = new CountingHandler(throwPreHeader: true);
        var budget = WireRequestBudget.OfWireRequests(3);

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(
            3, handler.SendCount,
            "robots plus two attempts at the count, and the ceiling stopped the rest of the retries.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
    }

    /// <summary>
    /// One budget spans an inventory run and a graph batch, and the batch inherits what is left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS REPLACES A TEST THAT CLAIMED THIS AND DID NOT DO IT. The first version ran the INVENTORY
    /// producer twice and never touched the graph producer, so it proved only that a spent budget
    /// refuses a second inventory session. It could not have failed when the live harness gave the
    /// inventory and every batch their own budget, which is the defect it was supposed to guard.
    /// A test named for a boundary it does not cross is worse than no test, because the name is what
    /// gets read.
    /// </para>
    /// <para>
    /// The inventory is allowed to succeed and the batch then runs on the remainder of the SAME
    /// instance. Giving the batch a fresh budget makes the transport outrun the terminal snapshot,
    /// which is exactly the shape the live path had.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task OneBudgetSpansTheInventoryAndTheBatchItIssues()
    {
        var handler = new CountingHandler(_ => LuxembourgAcquisitionTestFixture.CountJson(2));
        var budget = WireRequestBudget.OfWireRequests(64);

        // The inventory's own outcome is immaterial here: what is under test is whether a SECOND
        // producer run continues this instance's spend or starts its own. Letting it refuse keeps
        // the case small and still spends real reservations.
        var inventory = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        var afterInventory = inventory.WireBudget.Spent;
        Assert.IsGreaterThan(0, afterInventory, "the inventory reached the publisher.");
        Assert.AreEqual(
            handler.SendCount, afterInventory, "the inventory's own spend reconciles first.");

        var batch = await GraphProducer(handler).RunAsync(
            LuxembourgDraftGraphRunRequest.ForBatch(
                LuxembourgDraftGraphDiscoveryPlan.Create(),
                LuxembourgDraftGraphProducerTests.InventoryOf(
                    "http://data.legilux.public.lu/eli/dl/pl/2000/0001"),
                0,
                "urn:uuid:0c5b83e7-19d4-4a26-9f38-6b2e7d015c4a",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9308),
                budget),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        // THE BATCH CONTINUES THE INVENTORY'S SPEND RATHER THAN RESTARTING IT.
        Assert.IsGreaterThan(
            afterInventory,
            batch.WireBudget.Spent,
            "a batch on a fresh budget would report only its own handful of requests.");
        Assert.AreEqual(
            handler.SendCount,
            batch.WireBudget.Spent,
            "THE WITNESS OUTSIDE THE ACCOUNTING. The transport counted both runs' sends, including "
                + "both robots fetches; a second budget would leave one of them uncharged.");
        Assert.AreEqual(
            inventory.ProductRequestCount + batch.ProductRequestCount + 2,
            batch.WireBudget.Spent,
            "reservations are both runs' product attempts plus one robots fetch per session.");
    }

    /// <summary>
    /// A refused batch's cost is retained before anything concludes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SAFETY-STOP PATH IS THE ONE THAT MOST NEEDS EVIDENCE, and the first version of the live
    /// harness asserted delivery before assigning the stopped batch's attempts or snapshot to the
    /// run totals - so on exactly the outcomes a bounded run exists to report, it retained no
    /// terminal index at all.
    /// </para>
    /// <para>
    /// Driven by a genuinely refused batch rather than a hand-made snapshot: the refusal comes from
    /// a real producer run against a publisher that answers 503, so the numbers written here are the
    /// ones a stopped run would actually carry.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ARefusedBatchHasItsCostRetainedBeforeAnythingConcludes()
    {
        var handler = new CountingHandler(_ => null);
        var budget = WireRequestBudget.OfWireRequests(64);

        var batch = await GraphProducer(handler).RunAsync(
            GraphRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);
        Assert.IsFalse(batch.Delivered, "this publisher answered 503 to every product request.");

        var root = Path.Combine(Path.GetTempPath(), "e8-terminal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await LuxembourgDraftGraphLiveAcceptance.RetainTerminalIndexAsync(
                root,
                new Lex.V3.Artifacts.FileSystemCustodyStore(root),
                "BatchRefused",
                batch.Refusal.ToString(),
                batch.Detail,
                stoppedOrdinal: 0,
                batchesDelivered: 0,
                batchesIssued: 3,
                productRequests: batch.ProductRequestCount,
                sessionsOpened: 1,
                terminal: batch.WireBudget);

            var written = await File.ReadAllTextAsync(Path.Combine(root, "terminal-index.json"));

            StringAssert.Contains(written, "\"verdict\": \"BatchRefused\"");
            StringAssert.Contains(written, "\"stoppedOrdinal\": 0", "the reader must not infer where it stopped.");
            StringAssert.Contains(
                written,
                "\"actualHttpRequests\": " + batch.WireBudget.Spent,
                "the count a stopped run exists to report.");
            StringAssert.Contains(written, "\"wireCeiling\": " + batch.WireBudget.Limit);
            StringAssert.Contains(written, "\"productRequests\": " + batch.ProductRequestCount);
            StringAssert.Contains(written, "\"reconciles\":");
            Assert.AreEqual(
                handler.SendCount,
                batch.WireBudget.Spent,
                "and the retained figure is the transport's, not an invented one.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A batch stopped BEFORE its session opens counts no session, and the index still reconciles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CASE MY PREVIOUS REGRESSION COULD NOT REACH. It drove a 503, which arrives after a
    /// session has opened, so it could not see a count that assumed a session per producer CALL.
    /// The doors reserve robots before <c>StartSessionAsync</c>: an exhausted budget returns with no
    /// session, no request and no spend, and counting the attempt would put one phantom robots fetch
    /// into the expected total - making the accounting disagree with itself on exactly the
    /// pre-session safety stop the index exists to describe.
    /// </para>
    /// <para>
    /// The inventory is allowed to consume the whole ceiling first, so the batch meets a genuinely
    /// spent budget rather than a contrived one.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ABatchStoppedBeforeItsSessionCountsNoSessionAndStillReconciles()
    {
        var handler = new CountingHandler(_ => LuxembourgAcquisitionTestFixture.CountJson(2));
        var budget = WireRequestBudget.OfWireRequests(2);

        // 1. The inventory spends the whole ceiling: robots, then the count it could afford.
        var inventory = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        var sessionsOpened = 1;
        Assert.IsTrue(inventory.WireBudget.Exhausted, "the inventory reached the ceiling.");
        var sendsAfterInventory = handler.SendCount;
        Assert.AreEqual(sendsAfterInventory, inventory.WireBudget.Spent);

        // 2. The batch meets a spent budget and never opens a session.
        var spentBefore = budget.Spent;
        var batch = await GraphProducer(handler).RunAsync(
            GraphRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.AreEqual(
            sendsAfterInventory, handler.SendCount, "not one further request, not even robots.");
        Assert.AreEqual(spentBefore, batch.WireBudget.Spent, "and not one further reservation.");
        Assert.AreEqual(0, batch.ProductRequestCount);
        Assert.IsFalse(batch.Delivered);

        // THE RULE: a session is counted only when the spend advanced.
        if (batch.WireBudget.Spent > spentBefore)
        {
            sessionsOpened++;
        }

        Assert.AreEqual(1, sessionsOpened, "one session opened across both producer calls.");

        var root = Path.Combine(Path.GetTempPath(), "e8-exhausted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await LuxembourgDraftGraphLiveAcceptance.RetainTerminalIndexAsync(
                root,
                new Lex.V3.Artifacts.FileSystemCustodyStore(root),
                "BatchRefused",
                batch.Refusal.ToString(),
                batch.Detail,
                stoppedOrdinal: 0,
                batchesDelivered: 0,
                batchesIssued: 3,
                productRequests: inventory.ProductRequestCount + batch.ProductRequestCount,
                sessionsOpened: sessionsOpened,
                terminal: batch.WireBudget);

            var written = await File.ReadAllTextAsync(Path.Combine(root, "terminal-index.json"));

            StringAssert.Contains(
                written,
                "\"actualHttpRequests\": " + handler.SendCount,
                "the retained count is the transport's, unchanged by an attempt that sent nothing.");
            StringAssert.Contains(written, "\"sessionsOpened\": 1");
            StringAssert.Contains(
                written,
                "\"reconciles\": true",
                "THE DEFECT THIS CLOSES. Counting the attempted call made this false on exactly the "
                    + "pre-session stop, so the one path that most needs trustworthy evidence "
                    + "reported an accounting that disagreed with itself.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The batch canary is pinned to its dispositioned subjects and its named 25-request ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STRUCTURAL, for the reason the sweep guard is: this canary is gated off by default, so a
    /// substituted batch or a borrowed ceiling inside it fails nothing offline. The properties that
    /// matter here — which drafts it asks about, and how many requests it may send — are properties
    /// of the source.
    /// </para>
    /// <para>
    /// The batch was previously selectable through an environment variable. That meant the reviewed
    /// plan could name one set of drafts and the executed run carry another, with identical-looking
    /// evidence either way: a dispositioned canary whose subjects a variable can change is not the
    /// canary that was dispositioned.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheBatchCanaryIsPinnedToItsDispositionedBatchAndCeiling()
    {
        var source = File.ReadAllText(HarnessPath("LuxembourgDraftGraphBatchCanary.cs"));

        // NO ENVIRONMENT-VARIABLE PATH AT ALL, except the one gate that decides whether the run
        // happens. Naming the selectors I knew about was not enough: the first version banned
        // LEX_E8_BATCH_DRAFTS and left LEX_E8_BATCH_DISTINCT, which substituted the batch by
        // TRUNCATION - any value below 17 drops the distinguishing draft while the evidence keeps
        // its shape. The property is "the subjects are not a parameter", so the guard has to be
        // about the mechanism, not about the names of the doors I happened to find.
        Assert.AreEqual(
            1,
            CountOf(source, "GetEnvironmentVariable("),
            "exactly one environment read - the enable gate - and nothing that selects subjects.");
        StringAssert.Contains(
            source,
            "GetEnvironmentVariable(EnableVariable)",
            "and that one read is the gate.");

        // The complete batch reaches the run: a prefix is substitution by another name. Aimed at the
        // batch specifically - the file also truncates a diagnostic list with Take, which is not
        // this property and should not be collateral.
        Assert.AreEqual(
            0, CountOf(source, "BatchIris().Take("), "no prefix of the pinned batch.");
        Assert.AreEqual(
            0, CountOf(source, "batch.Take("), "and none taken from it afterwards.");
        StringAssert.Contains(
            source,
            "var batch = BatchIris();",
            "the run takes the complete pinned batch.");
        Assert.AreEqual(
            0,
            CountOf(source, "TestWireBudget()"),
            "the offline helper's 100,000-request ceiling has no place in a live path.");
        Assert.AreEqual(
            1,
            CountOf(source, "WireRequestBudget.OfWireRequests(WireCeiling)"),
            "one named ceiling, built once for the run.");
        StringAssert.Contains(
            source, "private const int WireCeiling = 25;", "the dispositioned number, named.");

        // The distinguishing draft is the reason this batch was chosen; losing it silently would
        // leave a canary that runs and answers a different question.
        // THE ARRAY ENTRY, NOT THE PROSE. A bare Contains passed while the entry was mutated away,
        // because the doc comment beside it names the same draft - the third time in this slice that
        // a guard of mine was satisfied by a comment rather than by the code it was guarding.
        Assert.AreEqual(
            1,
            CountOf(source, "\"pl/1989/60\""),
            "the non-ASCII titleDraft draft must be IN the batch array, not merely mentioned near "
                + "it: it separates the publisher codec from a conforming UTF-8 SHA-256, and it is "
                + "why this batch is the distinguishing one.");

        // Terminal accounting is written BEFORE the run can conclude, so a refusal reports its cost.
        // The LAST Inconclusive is the refusal exit; the first is the enable gate at the top of the
        // method, which every gated harness has and which proves nothing about ordering.
        var summary = source.IndexOf("actual_http_requests=", StringComparison.Ordinal);
        var refusalExit = source.LastIndexOf("Assert.Inconclusive(", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, summary, "the canary must report its actual request count.");
        Assert.IsGreaterThanOrEqualTo(0, refusalExit, "the canary must have a refusal exit.");
        Assert.IsLessThan(
            refusalExit,
            summary,
            "the terminal accounting is written before a refusal concludes the run.");
    }

    /// <summary>
    /// The gated live harness builds exactly one budget, and never the offline helper's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A STRUCTURAL GUARD, BECAUSE NO BEHAVIOURAL ONE CAN REACH IT. The acceptance harness is gated
    /// off by default, so a budget constructed per batch inside it cannot fail any offline test -
    /// which is exactly how a 100,000-request test helper came to be constructed 157 times in the
    /// live path while every offline suite stayed green. Mutating that harness back to a per-batch
    /// budget survives every other case in this class.
    /// </para>
    /// <para>
    /// So this reads the harness itself. Crude, and the crudeness is the point: the property that
    /// matters - ONE ceiling for the whole run - is a property of the source, and nothing else in
    /// the suite can observe it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheLiveDraftHarnessBuildsExactlyOneWholeRunBudget()
    {
        // SCOPED TO THE LIVE SWEEP, not the whole file. That file also holds an offline scripted
        // case whose transport is a fake, and the ceiling is immaterial there; a whole-file rule
        // would force an unrelated change and say something the property does not mean.
        var sweep = LiveSweepBody();

        Assert.AreEqual(
            1,
            CountOf(sweep, "WireRequestBudget.OfWireRequests("),
            "one budget for the whole run, or the inventory and every batch get their own ceiling.");
        Assert.AreEqual(
            0,
            CountOf(sweep, "TestWireBudget()"),
            "the offline helper's 100,000-request ceiling has no place in a live path.");
        StringAssert.Contains(
            sweep,
            "SharedWireCeiling is not { } ceiling",
            "and the run must refuse to start until a reviewed ceiling exists.");
        Assert.AreEqual(
            1,
            CountOf(sweep, "rendererSource, budget)"),
            "every batch takes the one instance the run created.");

        // ORDERING AND COVERAGE, NOT MERELY PRESENCE. The first version of this guard compared the
        // FIRST retain against the FIRST Assert.Fail, which passes while any earlier branch retains
        // - so deleting the retain from the batch-refusal branch survived it. Mutants caught the
        // guard, which is what they are for.
        Assert.AreEqual(
            3,
            CountOf(sweep, "RetainTerminalIndexAsync("),
            "one terminal index per outcome: inventory refusal, batch refusal, completed sweep. A "
                + "missing one is a path that concludes without reporting what it spent.");

        // The stopped batch's own cost is folded into the totals BEFORE the branch that stops on it,
        // or a stopped run reports the cost of every batch except the one that stopped it.
        var foldTotals = sweep.IndexOf(
            "productRequests += batch.ProductRequestCount", StringComparison.Ordinal);
        var decideStop = sweep.IndexOf(
            "if (batch.Refusal !=", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, foldTotals, "the sweep must total its product attempts.");
        Assert.IsGreaterThanOrEqualTo(0, decideStop, "the sweep must stop on a refused batch.");
        Assert.IsLessThan(
            decideStop,
            foldTotals,
            "totals first, decision second: the batch that stopped the run still cost what it cost.");

        // Sessions are counted by observing the spend, never by arithmetic over attempted calls.
        Assert.AreEqual(
            0,
            CountOf(sweep, "sessionsOpened: 2 + ordinal"),
            "an attempted producer call is not a session: an exhausted budget opens none.");
        Assert.AreEqual(
            2,
            CountOf(sweep, "sessionsOpened++"),
            "one increment for the inventory and one for each batch that opened.");

        // CONDITIONAL, not merely present. Counting increments alone passed when the guard around
        // them was deleted, which is the mutation that produced the defect in the first place: an
        // unconditional increment IS "count the attempted call". The condition is the rule.
        Assert.AreEqual(
            1,
            CountOf(sweep, "budget.Spent > spentBeforeRun"),
            "the inventory's session counts only if its robots reservation succeeded.");
        Assert.AreEqual(
            1,
            CountOf(sweep, "budget.Spent > spentBeforeBatch"),
            "and each batch's session counts only if its own reservation succeeded.");
    }

    /// <summary>The live sweep method's own text, from its signature to the next member.</summary>
    private static string LiveSweepBody()
    {
        var source = File.ReadAllText(HarnessPath("LuxembourgDraftGraphLiveAcceptance.cs"));
        const string Signature = "public async Task TheAcceptedDraftProvisionsAreAnsweredByThePublisher()";
        var start = source.IndexOf(Signature, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(
            0, start, "the live sweep method was renamed; this guard must be re-aimed, not deleted.");

        var end = source.IndexOf("\n    [TestMethod]", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = source.IndexOf("\n    private ", start, StringComparison.Ordinal);
        }

        return end < 0 ? source[start..] : source[start..end];
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string HarnessPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Checkout root not found.");
        return Path.Combine(root, "tests", "Lex.V3.Ingest.Tests", fileName);
    }

    /// <summary>The draft graph's only construction door refuses a null budget.</summary>
    [TestMethod]
    public void TheDraftGraphBatchDoorRefusesANullBudget() =>
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgDraftGraphRunRequest.ForBatch(
                LuxembourgDraftGraphDiscoveryPlan.Create(),
                DeliveredInventory(),
                0,
                "urn:uuid:3f8a1c65-24b7-4e09-9d31-8c5b06e2fa47",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9303),
                null!));

    /// <summary>The draft inventory request refuses a null budget at construction.</summary>
    /// <remarks>
    /// A positional record checks nothing of its own, so the documented requirement had to be made
    /// one. Its OpinionRequest sibling carried exactly this defect until it was repaired.
    /// </remarks>
    [TestMethod]
    public void TheDraftInventoryRequestRefusesANullBudget() =>
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgInitialDraftInventoryRunRequest(
                LuxembourgInitialDraftInventoryDiscoveryPlan.Create(),
                "urn:uuid:1d7e4b90-6c25-4a83-b0f1-27e95a3c8d64",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9304),
                null!));

    /// <summary>
    /// The draft GRAPH door reserves before its session too.
    /// </summary>
    /// <remarks>
    /// Added because the first version of this class tested only the inventory door: deleting the
    /// graph door's reservation survived every case here. Two doors were repaired and only one was
    /// covered, which is the same one-of-a-pair omission this slice keeps producing.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheGraphDoor()
    {
        var handler = new CountingHandler();
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt());
        Assert.IsTrue(budget.TryReserveAttempt());

        var result = await GraphProducer(handler).RunAsync(
            GraphRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.AreEqual(0, handler.SendCount, "not one request, not even robots.");
        Assert.IsFalse(result.Delivered);
        StringAssert.Contains(
            result.Detail ?? string.Empty,
            nameof(EuEnumerationRefusal.WireBudgetExhausted),
            "the graph door must stop on the ceiling, like its sibling.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
    }

    /// <summary>The graph door's pass loop reserves every attempt it makes.</summary>
    /// <remarks>
    /// A budget of two covers robots and the count; the page the count implies has nothing left to
    /// reserve. Without the budget reaching <c>RunPassesAsync</c> the page would go out and the
    /// transport would show three sends against a ceiling of two.
    /// </remarks>
    [TestMethod]
    public async Task TheGraphDoorStopsMidPassAtItsCeiling()
    {
        var handler = new CountingHandler(_ => LuxembourgAcquisitionTestFixture.CountJson(2));
        var budget = WireRequestBudget.OfWireRequests(2);

        var result = await GraphProducer(handler).RunAsync(
            GraphRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(2, handler.SendCount, "robots and the count; the page was never sent.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
        Assert.AreEqual(
            handler.SendCount, result.WireBudget.Spent,
            "reservations are taken immediately before send, so at the ceiling they are the sends.");
    }

    private static LuxembourgDraftGraphProducer GraphProducer(CountingHandler handler) =>
        new(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    private static LuxembourgDraftGraphRunRequest GraphRequest(WireRequestBudget budget) =>
        LuxembourgDraftGraphRunRequest.ForBatch(
            LuxembourgDraftGraphDiscoveryPlan.Create(),
            LuxembourgDraftGraphProducerTests.InventoryOf(
                "http://data.legilux.public.lu/eli/dl/pl/2000/0001"),
            0,
            "urn:uuid:7a2e5c81-3f46-4b09-8d17-52c9e0b4a36d",
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9307),
            budget);

    private static LuxembourgInitialDraftInventoryProducer InventoryProducer(CountingHandler handler) =>
        new(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    private static LuxembourgInitialDraftInventoryRunRequest InventoryRequest(WireRequestBudget budget) =>
        new(LuxembourgInitialDraftInventoryDiscoveryPlan.Create(),
            "urn:uuid:2b6f9e13-58a4-4c72-9e05-1f83d40b7a29",
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9305),
            budget);

    /// <summary>An inventory result standing in for a delivered one, for the null-door case only.</summary>
    private static LuxembourgInitialDraftInventoryResult DeliveredInventory() =>
        LuxembourgInitialDraftInventoryResult.Refused(
            LuxembourgInitialDraftInventoryRefusal.EnumerationRefused,
            "not reached: the null budget is refused first",
            productRequestCount: 0,
            LuxembourgAcquisitionTestFixture.TestBudgetSnapshot());

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9306);
        return plan.BindCount(
            planResourceId,
            "urn:uuid:4e91c7a2-0b36-4d58-8f27-63a519e0cb84",
            "urn:uuid:9c14f6d8-7a23-4b05-91e6-8d02537ace1b",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9306)).Request;
    }

    /// <summary>Counts robots apart from product sends, and answers however a case needs.</summary>
    /// <remarks>
    /// <c>null</c> from <paramref name="respond"/> means 503, which fails pre-header and is the one
    /// shape that exercises the retry path.
    /// </remarks>
    private sealed class CountingHandler(
        Func<int, string?>? respond = null,
        bool throwPreHeader = false)
        : System.Net.Http.HttpMessageHandler
    {
        private int _sendCount;
        private int _robotsSends;

        internal int SendCount => Volatile.Read(ref _sendCount);

        internal int RobotsSends => Volatile.Read(ref _robotsSends);

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Interlocked.Increment(ref _sendCount) - 1;
            if (request.RequestUri!.AbsolutePath.EndsWith("robots.txt", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _robotsSends);
                return Task.FromResult(RobotsAllowAll(request));
            }

            if (throwPreHeader)
            {
                throw new System.Net.Http.HttpRequestException(
                    System.Net.Http.HttpRequestError.ConnectionError, "simulated pre-header failure");
            }

            var body = respond?.Invoke(ordinal);
            return Task.FromResult(body is null
                ? new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    Version = System.Net.HttpVersion.Version11,
                    RequestMessage = request,
                    Content = new System.Net.Http.StringContent(string.Empty),
                }
                : LuxembourgAcquisitionTestFixture.JsonResponse(request, body));
        }

        private static System.Net.Http.HttpResponseMessage RobotsAllowAll(
            System.Net.Http.HttpRequestMessage request)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("User-agent: *\nAllow: /\n");
            var content = new System.Net.Http.ByteArrayContent(bytes);
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
            content.Headers.TryAddWithoutValidation(
                "Content-Length",
                bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Version = System.Net.HttpVersion.Version11,
                RequestMessage = request,
                Content = content,
            };
        }
    }
}
