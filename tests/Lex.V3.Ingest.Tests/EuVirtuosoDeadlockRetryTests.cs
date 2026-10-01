using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuVirtuosoDeadlockRetryTests
{
    private const string Deadlock = "Virtuoso 40001 Error SR...: Transaction deadlock, from SQL built-in function.\n\nSPARQL query:\nSELECT * WHERE {}";

    [TestMethod]
    [DataRow(1, 500)]
    [DataRow(2, 500)]
    [DataRow(1, 503)]
    [DataRow(2, 503)]
    public async Task RetainedTransientFailureRetriesTheSameCountOrPageAndProvesBothPasses(int faultAt, int status)
    {
        var body = await RetainedFailureBodyAsync(status);
        var handler = new DeadlockHandler(faultAt, 1, status, body);
        var budget = WireRequestBudget.OfWireRequests(20);
        var (result, store) = await RunAsync(handler, budget);
        Assert.IsNull(result.Refusal, result.Refusal?.Code.ToString());
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(5, result.ProductRequestCount);
        Assert.AreEqual(5, handler.Posts.Count);
        Assert.AreEqual(7, budget.Spent, "Both robots hops and every application attempt are charged.");
        Assert.AreEqual(handler.Posts[faultAt - 1], handler.Posts[faultAt]);
        var evidence = RetainedRoutes(store);
        var failed = evidence.Single(route => route.Hops[^1].Status == status);
        var recovered = evidence.Single(route => route.RequestOrdinal == failed.RequestOrdinal
            && route.AttemptOrdinal == failed.AttemptOrdinal + 1);
        Assert.AreEqual(failed.RunIdentity, recovered.RunIdentity);
        Assert.AreEqual(failed.Hops[^1].LogicalRequestSha256, recovered.Hops[^1].LogicalRequestSha256);
        Assert.AreEqual(200, recovered.Hops[^1].Status);
    }

    [TestMethod]
    [DataRow(500)]
    [DataRow(503)]
    public async Task PersistentTransientFailureExhaustsTheExistingFourAttemptAllowance(int status)
    {
        var handler = new DeadlockHandler(1, 10, status, await RetainedFailureBodyAsync(status));
        var budget = WireRequestBudget.OfWireRequests(20);
        var (result, store) = await RunAsync(handler, budget);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(EuEnumerationRefusal.StatusNotAdmitted, result.Refusal?.Code);
        Assert.AreEqual(status, result.Refusal?.TerminalStatus);
        Assert.AreEqual(4, handler.Posts.Count);
        Assert.AreEqual(4, result.ProductRequestCount);
        Assert.AreEqual(6, budget.Spent);
        CollectionAssert.AreEqual(new ulong[] { 0, 1, 2, 3 }, RetainedRoutes(store)
            .Where(route => route.Hops[^1].Status == status).Select(route => route.AttemptOrdinal)
            .Order().ToArray());
    }

    [TestMethod]
    [DataRow(500)]
    [DataRow(503)]
    public async Task ExhaustedWireBudgetStopsBeforeTheRetryIsSent(int status)
    {
        var handler = new DeadlockHandler(1, 10, status, await RetainedFailureBodyAsync(status));
        var budget = WireRequestBudget.OfWireRequests(3);
        var (result, _) = await RunAsync(handler, budget);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(EuEnumerationRefusal.WireBudgetExhausted, result.Refusal?.Code);
        Assert.AreEqual(1, handler.Posts.Count);
        Assert.AreEqual(3, budget.Spent);
    }

    [TestMethod]
    [DataRow(403, Deadlock)]
    [DataRow(503, Deadlock)]
    [DataRow(503, "<html>Verify you are human</html>")]
    [DataRow(503, "<html><title>Web Site Under Maintenance</title></html>")]
    [DataRow(500, "<html>Verify you are human</html>")]
    [DataRow(500, "Virtuoso 42000 Error SR171: Transaction deadlock")]
    [DataRow(500, "Virtuoso 40001 Error SR...: capacity limit")]
    public async Task OtherRefusalsNeverGainADeadlockRetry(int status, string body)
    {
        var handler = new DeadlockHandler(1, 10, status, body);
        var (result, _) = await RunAsync(handler, WireRequestBudget.OfWireRequests(20));
        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(1, handler.Posts.Count);
    }

    [TestMethod]
    [DataRow(503, true)]
    [DataRow(403, false)]
    [DataRow(500, false)]
    public async Task MaintenanceRequiresBothExactBytesAnd503Status(int status, bool changeBody)
    {
        var body = await RetainedFailureBodyAsync(503);
        if (changeBody) body += "\n";
        var handler = new DeadlockHandler(1, 10, status, body);
        var (result, _) = await RunAsync(handler, WireRequestBudget.OfWireRequests(20));
        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(1, handler.Posts.Count);
    }

    private static async Task<string> RetainedFailureBodyAsync(int status)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "EuDocumentFetch", status == 503 ? "eu-maintenance-503.bin" : "eu-virtuoso-deadlock-500.bin"));
        Assert.AreEqual(status == 503
            ? "e7fab335ce5367cfe359f9f7e0ad6ce1838bec9189a216bc3faf437ce169d404"
            : "70769075fe4617e11288eda6ac3120c1b10b7f5e64d90149ff9e8c28431b3627",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<(EuEnumerationRunResult Result,
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store)> RunAsync(
        DeadlockHandler handler, WireRequestBudget budget)
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var (plan, id) = EuAcquisitionTestFixture.BuildCensusPlan();
        var request = new EuCensusPartitionRunRequest(plan, id, seed.Celex,
            EuAcquisitionTestFixture.BuildRendererSource(1), budget);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(store,
            new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        return (await executor.RunCensusPartitionAsync(request,
            EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None), store);
    }

    private static RoutedHttpEvidence[] RetainedRoutes(EuAcquisitionTestFixture.EuInMemoryCustodyStore store)
    {
        var held = (Dictionary<string, byte[]>)store.GetType()
            .GetField("_byDigest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        var routes = new List<RoutedHttpEvidence>();
        foreach (var bytes in held.Values)
        {
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 100)).IndexOf("lex-license-http-evidence/4"u8) < 0) continue;
            routes.Add(RoutedHttpEvidence.ParseAndVerify(bytes));
        }
        return routes.ToArray();
    }

    private sealed class DeadlockHandler(int faultAt, int failures, int status, string body) : HttpMessageHandler
    {
        private int _remaining = failures;
        private readonly HttpMessageInvoker _inner = new(new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
            {
                ["Census"] = EuAcquisitionTestFixture.ScriptFor("Census", 0, [],
                    EuAcquisitionTestFixture.CensusFamilyProjection),
            }));
        public List<string> Posts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (Posts.Count >= faultAt && _remaining-- > 0)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var content = new ByteArrayContent(bytes);
                    content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
                    content.Headers.ContentLength = bytes.Length;
                    return new HttpResponseMessage((HttpStatusCode)status)
                    {
                        Version = HttpVersion.Version11, RequestMessage = request, Content = content,
                    };
                }
            }
            return await _inner.SendAsync(request, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
