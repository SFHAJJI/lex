using System.Net;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Luxembourg doors #579 measured as unbudgeted, each now met with an already-spent ceiling.
/// </summary>
/// <remarks>
/// <para>
/// WHAT EACH TEST ESTABLISHES IS THE POSITION, NOT THE REFUSAL. The sibling
/// <see cref="UnbudgetedEntryPointClosureTests"/> states the doctrine for the EU half and it is the
/// same here: asserting the refusal code alone passes on a door that fetches robots, discovers the
/// exhaustion afterwards and then reports it honestly, which is precisely the exposure #579 records.
/// Only <c>SendCount == 0</c> separates a door that stopped from a door that sent and then noticed.
/// </para>
/// <para>
/// THESE TWO DOORS HAD NOTHING AT ALL. <c>RunPartitionAsync</c> took an optional ceiling and
/// <c>AReusedBudgetStillBoundsTheWireOnTheMirror</c> has covered it since it landed;
/// <c>RunCoverAsync</c> and <c>RunDocumentGetAsync</c> had no parameter to omit, so there was nothing
/// to test and nothing was tested. The slice that made all three required threaded call sites and
/// added no test, which is how a required parameter can still be an unenforced one.
/// </para>
/// <para>
/// The budget floor is two: <c>OfWireRequests</c> admits no run whose ceiling cannot cover its robots
/// fetch plus one product request, so each fixture spends both reservations itself to reach the
/// exhausted state the door must be met with.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgUnbudgetedEntryPointClosureTests
{
    private const string StoreXmlUri =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
        + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml";

    private const string ActEliPagePath = "/eli/etat/leg/loi/2017/03/14/a439/jo";

    /// <summary>
    /// A spent ceiling stops the cover before its session, and says so once per intended leaf.
    /// </summary>
    /// <remarks>
    /// The per-leaf shape is not decoration. <c>RunCoverAsync</c>'s own contract is exactly one
    /// result per chain leaf in leaf order, whether or not it delivered, and a new early return is a
    /// new path that has to keep it. A refusal returning one result for a two-leaf chain would break
    /// <c>results.Count == chain.Leaves.Count</c> for every caller that relies on positional
    /// alignment.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheCoverDoor()
    {
        var chain = LuxembourgPartitionChain.Root(LuxembourgAcquisitionTestFixture.FullRange())
            .SplitLeaf(
                "subjects-fixture",
                new LuxembourgQueryCursor("m", "", "", "", "", ""),
                "leaf-a",
                "leaf-b");
        var (rootRequest, witness) = BuildRequest();
        var handler = NoSendHandler();
        var executor = Executor(handler);

        var results = await executor.RunCoverAsync(
            rootRequest, chain, witness, Spent(), CancellationToken.None);

        Assert.HasCount(
            chain.Leaves.Count,
            results,
            "one result per intended leaf, on the refusal path as on the delivered one.");
        Assert.IsTrue(
            results.All(static result =>
                result.Refusal?.Code == LuxembourgEnumerationRefusal.WireBudgetExhausted),
            "an exhausted ceiling must refuse by name rather than as a bootstrap failure.");
        Assert.IsTrue(results.All(static result => result.ProductRequestCount == 0));
        Assert.AreEqual(
            0,
            handler.SendCount,
            "nothing may reach the publisher once the ceiling is spent - not even robots.");
    }

    /// <summary>A spent ceiling stops the document fetch before its session.</summary>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheDocumentGetDoor()
    {
        var handler = NoSendHandler();
        var executor = Executor(handler);

        var attempt = await executor.RunDocumentGetAsync(
            BoundDocumentRequest(), Spent(), CancellationToken.None);

        Assert.IsNull(attempt.Evidence);
        Assert.AreEqual(LuxembourgDocumentGetAttemptRefusal.WireBudgetExhausted, attempt.Refusal);
        Assert.AreEqual(
            0,
            handler.SendCount,
            "nothing may reach the publisher once the ceiling is spent - not even robots.");
    }

    /// <summary>
    /// The document fetch charges every attempt, so a retry loop cannot outrun the ceiling.
    /// </summary>
    /// <remarks>
    /// This is the door's own difference from every other one in this file, and the reason it needed
    /// its own test. <c>RunDocumentGetAsync</c> re-attempts a COMPLETED response at a retryable
    /// status - the profile allows four attempts per bound request - so a ceiling charged once per
    /// bound request would be wrong by that factor at exactly the limit it exists to hold. The
    /// handler below answers 503 forever; with a ceiling of three the run may send robots and two
    /// attempts, and the third attempt must be refused rather than sent.
    /// </remarks>
    [TestMethod]
    public async Task TheDocumentGetDoorChargesEveryAttemptAndNotOnlyTheFirst()
    {
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler(
            static (_, request) => Unavailable(request));
        var executor = Executor(handler);
        var budget = WireRequestBudget.OfWireRequests(3);

        var attempt = await executor.RunDocumentGetAsync(
            BoundDocumentRequest(), budget, CancellationToken.None);

        Assert.IsNull(attempt.Evidence);
        Assert.AreEqual(
            LuxembourgDocumentGetAttemptRefusal.WireBudgetExhausted,
            attempt.Refusal,
            "the retry loop must stop on the ceiling, not on the profile's attempt allowance.");
        Assert.AreEqual(
            budget.Limit,
            handler.SendCount,
            "robots plus two attempts is the whole ceiling, and the retry after it never went out.");
        Assert.IsTrue(budget.Exhausted);
    }

    // ---- The ceiling is required, not merely documented. ----

    /// <summary>Every Luxembourg executor door refuses a null ceiling.</summary>
    /// <remarks>
    /// An optional ceiling is absent by omission, which is what #579 measured: before this,
    /// <c>RunPartitionAsync</c> declared <c>WireRequestBudget? = null</c> and every caller but one
    /// unit test omitted it. Making the parameter required is what the compiler now enforces; this
    /// is the other half, because <c>null!</c> compiles.
    /// <para>
    /// The adapter that drives these three is covered by
    /// <c>LuxembourgQueryExecutionAdapterTests.TheAdapterRefusesANullCeiling</c>, where the source
    /// profile fixture it needs already lives.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task EveryLuxembourgDoorRefusesANullCeiling()
    {
        var (request, witness) = BuildRequest();
        var executor = Executor(NoSendHandler());

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => executor.RunPartitionAsync(request, witness, null!, CancellationToken.None));

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => executor.RunCoverAsync(
                request,
                LuxembourgPartitionChain.Root(LuxembourgAcquisitionTestFixture.FullRange()),
                witness,
                null!,
                CancellationToken.None));

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => executor.RunDocumentGetAsync(
                BoundDocumentRequest(), null!, CancellationToken.None));
    }

    // ---- Fixtures. ----

    /// <summary>A ceiling already spent down to nothing, at the floor OfWireRequests admits.</summary>
    private static WireRequestBudget Spent()
    {
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt(), "the fixture spends the budget itself.");
        Assert.IsTrue(budget.TryReserveAttempt(), "the fixture spends the budget itself.");
        Assert.IsTrue(budget.Exhausted, "the door must be met with an already-spent budget.");
        return budget;
    }

    /// <summary>A retryable completed response, built here because the fixture's own is private.</summary>
    private static HttpResponseMessage Unavailable(HttpRequestMessage request)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("unavailable");
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        content.Headers.TryAddWithoutValidation(
            "Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
    }

    /// <summary>A handler that fails the test if anything reaches it at all.</summary>
    private static LuxembourgAcquisitionTestFixture.SequencedHandler NoSendHandler() =>
        new(static (_, _) => throw new AssertFailedException(
            "a door stopped by its ceiling must send nothing, including robots."));

    private static LuxembourgRepeatedEnumerationExecutor Executor(HttpMessageHandler handler) =>
        new(new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true },
            new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    private static BoundMachineRequest BoundDocumentRequest() =>
        new LuxembourgDocumentFetchPlan(
                LuxembourgDocumentFetchAddress.Create(
                    LuxembourgFileUri.RequireValid(StoreXmlUri),
                    LuxembourgUserFormatToken.XmlAkomaNtoso,
                    LuxembourgLegalValue.Officiel,
                    ActEliPagePath))
            .Bind(
                $"urn:uuid:{Guid.NewGuid():D}",
                $"urn:uuid:{Guid.NewGuid():D}",
                LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(3001))
            .Request;

    private static (LuxembourgPartitionRunRequest Request, BoundMachineRequest Witness) BuildRequest()
    {
        var partition = LuxembourgAcquisitionTestFixture.FullRange();
        var (invariantPlan, invariantPlanResourceId, _) =
            LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var rendererSource = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var request = new LuxembourgPartitionRunRequest(
            invariantPlan,
            invariantPlanResourceId,
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            partition,
            rendererSource);
        var witness = invariantPlan.BindCount(
            invariantPlanResourceId,
            $"urn:uuid:{Guid.NewGuid():D}",
            $"urn:uuid:{Guid.NewGuid():D}",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            partition,
            rendererSource);
        return (request, witness.Request);
    }
}
