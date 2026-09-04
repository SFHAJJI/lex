using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-05c-2 precision one: the EU executor's own pass loop, cursor handling and EU-dialect count
/// parse. These tests drive <see cref="EuRepeatedEnumerationExecutor"/> directly, without the
/// adapter, exactly mirroring the split <c>LuxembourgRepeatedEnumerationExecutorTests</c> keeps from
/// <c>LuxembourgQueryExecutionAdapterTests</c>.
/// </summary>
[TestClass]
public sealed class EuRepeatedEnumerationExecutorTests
{
    [TestMethod]
    public async Task TheEuDialectCountParseAcceptsTheLiteralWireTypeAndRejectsLuTypedLiteral()
    {
        // Print-then-transcribe: EnumerationDeliveryComparison.ParseCount (Core) itself requires
        // "literal" for RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso and "typed-literal"
        // only for LuxembourgVirtuoso. This test proves the EXECUTOR's own pre-check (the strict count
        // read immediately after the count response, before any page is bound) agrees: a real LU-shaped
        // "typed-literal" count response -- which the LU executor's own ParseStrictCount accepts --
        // is refused here as CountNotOneNonNegativeInteger, never silently parsed.
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var (plan, planResourceId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var rendererSource = EuAcquisitionTestFixture.BuildRendererSource(1);
        var request = new EuCensusPartitionRunRequest(plan, planResourceId, seed.Celex, rendererSource);

        const string luShapedCount =
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true," +
            "\"bindings\":[{\"count\":{\"type\":\"typed-literal\"," +
            "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"0\"}}]}}";

        var handler = new SingleFamilyHandler(luShapedCount);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await executor.RunCensusPartitionAsync(
            request, EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuEnumerationRefusal.CountNotOneNonNegativeInteger, result.Refusal!.Code);
    }

    [TestMethod]
    public async Task ASelectionAtTheCeilingRefusesPartitionRequiredRatherThanPaging()
    {
        // Decision 23 / R3.2: the publisher delivery ceiling is 1,000,000 rows. AssessThreshold is
        // exercised for real here (never asserted): a count of exactly the ceiling must refuse before
        // a single page is ever requested, so the fixture's own handler throws if page binding is
        // reached at all.
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var (plan, planResourceId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var rendererSource = EuAcquisitionTestFixture.BuildRendererSource(2);
        var request = new EuCensusPartitionRunRequest(plan, planResourceId, seed.Celex, rendererSource);

        var handler = new SingleFamilyHandler(EuAcquisitionTestFixture.EuCountJson(1_000_000));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await executor.RunCensusPartitionAsync(
            request, EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuEnumerationRefusal.PartitionRequired, result.Refusal!.Code);
        Assert.AreEqual(1_000_000L, result.Refusal.ObservedCount);
    }

    /// <summary>
    /// Defect 5's own driving test. <c>EuEnumerationRefusal.DeliveredKeyNotRepresentable</c> was dead
    /// code before this fix: nothing on the EU side ever tagged an exception with the classifier's own
    /// <c>eu.pageParseFailure</c> key. This delivers a real page whose one delivered key part (the
    /// census family's own <c>state_key</c>) exceeds the representability bound, proving the refusal
    /// is now reachable rather than merely declared.
    /// </summary>
    [TestMethod]
    public async Task ADeliveredKeyPartExceedingTheRepresentabilityBoundIsRefused()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var (plan, planResourceId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var rendererSource = EuAcquisitionTestFixture.BuildRendererSource(3);
        var request = new EuCensusPartitionRunRequest(plan, planResourceId, seed.Celex, rendererSource);

        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        // Well past the 2047 UTF-8 byte bound RequireRepresentableKeyPart enforces.
        var oversizedStateIri = "http://publications.europa.eu/resource/cellar/" + new string('a', 2100);
        var oversizedRow = EuAcquisitionTestFixture.CensusFamilyRow(seed.Celex, rootIri, oversizedStateIri);

        var script = new EuAcquisitionTestFixture.FamilyScript("Census", new[]
        {
            EuAcquisitionTestFixture.EuCountJson(1),
            EuAcquisitionTestFixture.CensusFamilyRowsJson(new[] { oversizedRow }),
        });
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = script,
        };
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await executor.RunCensusPartitionAsync(
            request, EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuEnumerationRefusal.DeliveredKeyNotRepresentable, result.Refusal!.Code);
    }

    /// <summary>
    /// Fold-in from the D1-06c-EU refreeze review (SCOPE_RULING
    /// lex-event-20260904T143553601Z-e6842d729c9b41fc8f5a6e76d5750bc2):
    /// <see cref="EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused"/> existed as a real code path
    /// in <see cref="EuRepeatedEnumerationExecutor.RunDocumentFetchAsync"/> (returned whenever
    /// <c>RoutedHttpAcquisitionSession.StartAsync</c>'s own bootstrap does not reach
    /// <c>ExecutedObservation</c>) but no test ever drove it through the real executor -- only a
    /// robots-policy unit test at the session level exercised the underlying denial itself. This
    /// drives it end to end: a robots.txt that disallows every path for every agent denies the
    /// document-fetch source witness's own requested path at bootstrap, so
    /// <c>RoutedHttpAcquisitionSession.BootstrapRobotsAsync</c> returns
    /// <c>StartResult.PublisherDenied</c>, <c>StartSessionAsync</c> sees a non-<c>ExecutedObservation</c>
    /// kind and returns <see langword="null"/>, and <c>RunDocumentFetchAsync</c> reports this refusal
    /// with no evidence at all -- never reaching the product GET, which this handler would throw for.
    /// </summary>
    [TestMethod]
    public async Task ARobotsDisallowForEveryAgentRefusesTheDocumentFetchAttemptAsRobotsBootstrapRefused()
    {
        var handler = new DocumentFetchRobotsDenyingHandler();
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var witness = EuAcquisitionTestFixture.DocumentFetchSourceWitness();
        var result = await executor.RunDocumentFetchAsync(witness, witness, CancellationToken.None);

        Assert.IsNull(result.Evidence);
        Assert.AreEqual(EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused, result.Refusal);
    }

    /// <summary>
    /// Answers the EU robots route with an unconditional <c>Disallow: /</c> for every agent, so
    /// <see cref="ARobotsDisallowForEveryAgentRefusesTheDocumentFetchAttemptAsRobotsBootstrapRefused"/>
    /// never reaches a product request; reaching one here is a test defect, so it throws.
    /// </summary>
    private sealed class DocumentFetchRobotsDenyingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "publications.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                var body = System.Text.Encoding.UTF8.GetBytes("moved");
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                content.Headers.TryAddWithoutValidation(
                    "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.MovedPermanently)
                {
                    Version = System.Net.HttpVersion.Version11, RequestMessage = request, Content = content,
                };
                response.Headers.Location = new Uri("https://op.europa.eu/robots.txt");
                return Task.FromResult(response);
            }

            if (request.RequestUri.Host == "op.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                var body = System.Text.Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /\n");
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                content.Headers.TryAddWithoutValidation(
                    "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Version = System.Net.HttpVersion.Version11, RequestMessage = request, Content = content,
                });
            }

            throw new InvalidOperationException(
                $"unreachable: a robots-denied bootstrap must never reach a product request ({request.RequestUri}).");
        }
    }

    /// <summary>
    /// Answers the EU robots route (by URI, exactly as <see cref="EuAcquisitionTestFixture.ClassifyingHandler"/>
    /// does), then always returns <paramref name="countBody"/> for every SPARQL POST -- correct for
    /// these two tests because both refuse from the pass's own count response, before any page bind
    /// could ever be reached; a page bind here is a test defect, so it throws.
    /// </summary>
    private sealed class SingleFamilyHandler(string countBody) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "publications.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                var body = System.Text.Encoding.UTF8.GetBytes("moved");
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                content.Headers.TryAddWithoutValidation(
                    "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.MovedPermanently)
                {
                    Version = System.Net.HttpVersion.Version11, RequestMessage = request, Content = content,
                };
                response.Headers.Location = new Uri("https://op.europa.eu/robots.txt");
                return response;
            }

            if (request.RequestUri.Host == "op.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                var body = System.Text.Encoding.UTF8.GetBytes("User-agent: *\nAllow: /\n");
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                content.Headers.TryAddWithoutValidation(
                    "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Version = System.Net.HttpVersion.Version11, RequestMessage = request, Content = content,
                };
            }

            _ = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return EuAcquisitionTestFixture.JsonResponse(request, countBody);
        }
    }
}
