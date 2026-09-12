using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What one inventory-issued batch of the OpinionRequest graph produces, driven end to end.
/// </summary>
/// <remarks>
/// <para>
/// THE HONEST PATH IS ASSERTED FIRST. Every refusal case would still pass on a producer that refuses
/// everything, so the delivered matrix is written first and is the only case that fails if the run,
/// the proof, the page reopen, the verified-row re-derivation and the coverage door do not line up.
/// </para>
/// <para>
/// NOTHING HERE CLAIMS COMPLETENESS, and one case asserts that the type cannot. Whether the batches
/// are the class is the cover's decision; a producer that reported "complete" for its own batch is
/// the first step toward a sweep that reconstructs completeness from what came back.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestGraphProducerTests
{
    private const string ReferralDate =
        LuxembourgOpinionRequestGraphDiscoveryPlan.ReferralDatePredicateIri;

    private const string RdfType = LuxembourgOpinionRequestGraphDiscoveryPlan.RdfTypePredicateIri;

    private const string RequestClass =
        LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri;

    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    /// <summary>An honest batch delivery completes into a readable matrix.</summary>
    [TestMethod]
    public async Task AnHonestBatchDeliveryCompletesItsMatrix()
    {
        var batch = new[] { Request(1), Request(2) };

        var result = await RunAsync(
            batch,
            (batch[0], RdfType, RequestClass, true),
            (batch[0], ReferralDate, "2004-03-11", false),
            (batch[1], RdfType, RequestClass, true));

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.IsNotNull(result.Coverage);
        Assert.AreEqual(2, result.Coverage.CoveredPairCount, "two requests, one asked property.");
        Assert.AreEqual(1, result.Coverage.PresentPairCount);
        Assert.AreEqual(
            1, result.Coverage.DerivedAbsences.Count,
            "the typed request that delivered no date earns an absence.");
        Assert.AreEqual(0, result.Coverage.UnresolvedGaps.Count);
        Assert.AreEqual(2, result.Coverage.RetainedRows.Count, "the two type rows.");
        CollectionAssert.AreEqual(
            new[] { "2004-03-11" },
            result.Coverage.ValuesFor(batch[0], ReferralDate).Select(static v => v.Value).ToArray());
        Assert.IsGreaterThan(0, result.ProductRequestCount);
    }

    /// <summary>
    /// A batch whose requests the publisher never typed produces gaps, not absences.
    /// </summary>
    /// <remarks>
    /// The whole point of this family, carried through the real transport rather than asserted about
    /// the door: the query filters on the class, and the plan refuses to read that filter as the
    /// publisher having answered.
    /// </remarks>
    [TestMethod]
    public async Task AnUntypedBatchProducesGapsRatherThanAbsences()
    {
        var batch = new[] { Request(1) };

        var result = await RunAsync(batch, (batch[0], ReferralDate, "2004-03-11", false));

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.IsNotNull(result.Coverage);
        Assert.AreEqual(0, result.Coverage.DerivedAbsences.Count);
        CollectionAssert.AreEqual(
            new[] { batch[0] }, result.Coverage.RequestsOfUnconfirmedRole.ToArray());
    }

    /// <summary>
    /// A delivery naming a request outside the batch produces no coverage at all.
    /// </summary>
    /// <remarks>
    /// Refused by the executor's partition guard before this producer sees a row, which is why the
    /// refusal is an enumeration refusal rather than a matrix one. Asserted here because a producer
    /// that turned it into a completed matrix over the members it did recognise would be admitting
    /// an answer to a different question.
    /// </remarks>
    [TestMethod]
    public async Task ADeliveryNamingARequestOutsideTheBatchProducesNoCoverage()
    {
        var batch = new[] { Request(1), Request(2) };

        var result = await RunAsync(
            batch,
            (batch[0], RdfType, RequestClass, true),
            (Request(97), RdfType, RequestClass, true));

        Assert.IsFalse(result.Delivered);
        Assert.IsNull(result.Coverage, "no matrix is completed over a delivery that was refused.");
        Assert.AreEqual(LuxembourgOpinionRequestGraphRefusal.EnumerationRefused, result.Refusal);
        StringAssert.Contains(
            result.Detail ?? string.Empty,
            EuEnumerationRefusal.DeliveredRowOutsidePartition.ToString(),
            "the executor's own reason travels out rather than being restated.");
    }

    /// <summary>
    /// A row whose key does not describe its own value refuses the matrix, not the enumeration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUND BY MUTATION ON MY OWN SLICE: swapping this refusal's code to the enumeration one left
    /// every case green, because nothing reached the coverage door's refusal at all. That made
    /// <c>MatrixNotCompleted</c> a member no delivery could produce.
    /// </para>
    /// <para>
    /// It is reachable, and this is how. The row is internally consistent with its own retained
    /// bytes, so the proof and the verified-row re-derivation both accept it; what it is not is a
    /// row whose <c>key_4</c> is the publisher's digest of the value beside it. The coverage door
    /// refuses there, and the distinction matters: an enumeration that delivered fine and a matrix
    /// that could not be read are different failures, and folding them would send whoever reads the
    /// receipt back to the publisher for a problem in the rows they already have.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ARowWhoseKeyDoesNotDescribeItsValueRefusesTheMatrix()
    {
        var batch = new[] { Request(1) };

        var result = await RunRawAsync(
            batch,
            RowWithKeyOf(batch[0], ReferralDate, "2004-03-11", keyedAs: "1999-01-01"));

        Assert.IsFalse(result.Delivered);
        Assert.IsNull(result.Coverage);
        Assert.AreEqual(LuxembourgOpinionRequestGraphRefusal.MatrixNotCompleted, result.Refusal);
        StringAssert.Contains(
            result.Detail ?? string.Empty,
            LuxembourgOpinionRequestCoverageRefusal.DeliveredRowNotDescribedByItsOwnKey.ToString(),
            "the coverage door's own reason travels out rather than being restated.");
        StringAssert.Contains(result.Detail ?? string.Empty, "key_4");
    }

    /// <summary>The publisher's own reason travels out with an enumeration refusal.</summary>
    [TestMethod]
    public async Task AnEnumerationRefusalCarriesThePublishersOwnReason()
    {
        var batch = new[] { Request(1) };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, request) =>
            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Version = System.Net.HttpVersion.Version11,
                RequestMessage = request,
                Content = new System.Net.Http.StringContent(string.Empty),
            });

        var result = await ProducerFor(handler).RunAsync(RequestFor(batch), Witness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(LuxembourgOpinionRequestGraphRefusal.EnumerationRefused, result.Refusal);
        Assert.IsNotNull(result.Detail);
        Assert.AreNotEqual(string.Empty, result.Detail);
    }

    /// <summary>
    /// The result cannot express completeness, because completeness is not this stage's to claim.
    /// </summary>
    /// <remarks>
    /// The reviewer disposition is explicit: the producer may consume only the cover and must not
    /// reconstruct completeness from returned deliveries or caller counts. The way to keep that is
    /// to leave the vocabulary out, so this pins the surface rather than trusting a comment - a
    /// property named Complete, Whole or ClassCovered here would be the claim arriving under a name.
    /// </remarks>
    [TestMethod]
    public void TheResultCannotExpressCompleteness()
    {
        var names = typeof(LuxembourgOpinionRequestGraphResult)
            .GetProperties()
            .Select(static value => value.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            // WireBudget is admitted here deliberately: it records what the run COST, which is a
            // fact about this stage's own execution, not a claim about how much of the class it
            // reached. The clause this pin exists for is completeness, and a cost cannot assert one.
            new[]
            {
                "Coverage", "Refusal", "Detail", "ProductRequestCount", "Delivered", "WireBudget",
            },
            names,
            "a property beyond these is a claim this stage is not entitled to make.");
        Assert.IsFalse(
            names.Any(static value =>
                value.Contains("Complete", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Whole", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Covered", StringComparison.OrdinalIgnoreCase)),
            "completeness belongs to the batch cover, which derives its expected batches from the "
                + "inventory rather than from what came back.");
    }

    private static async Task<LuxembourgOpinionRequestGraphResult> RunAsync(
        IReadOnlyList<string> batch,
        params (string Subject, string Predicate, string Value, bool Iri)[] rows)
    {
        // SORTED INTO KEY ORDER HERE, not in each test. A proven delivery is necessarily
        // key-ordered - Source/Core requires cursors to strictly increase - and this family keys on
        // subject, kind, predicate, the value's digest, then kind, datatype and language. Getting
        // that wrong refuses the run as CursorDidNotAdvance and never reaches the rule under test,
        // which is exactly what happened when the tests listed a type row before a referralDate row
        // for one subject: jolux#referralDate sorts before rdf-syntax-ns#type.
        var ordered = rows
            .OrderBy(static row => row.Subject, StringComparer.Ordinal)
            .ThenBy(static row => IriKind, StringComparer.Ordinal)
            .ThenBy(static row => row.Predicate, StringComparer.Ordinal)
            .ThenBy(
                static row => LuxembourgPublisherCursorCodec.ComputeKey(row.Value),
                StringComparer.Ordinal)
            .ToArray();

        var count = CountJson(ordered.Length);
        var page = RowsJson(ordered.Select(row => Row(row.Subject, row.Predicate, row.Value, row.Iri))
            .ToArray());
        var scripted = new[] { count, page, count, page };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
        {
            var index = ordinal - 1;
            Assert.IsLessThan(
                scripted.Length, index, "the run asked for more responses than this script holds.");
            return LuxembourgAcquisitionTestFixture.JsonResponse(request, scripted[index]);
        });

        return await ProducerFor(handler).RunAsync(RequestFor(batch), Witness(), CancellationToken.None);
    }

    /// <summary>Scripts exactly these rendered rows, in the order given.</summary>
    private static async Task<LuxembourgOpinionRequestGraphResult> RunRawAsync(
        IReadOnlyList<string> batch,
        params string[] rows)
    {
        var count = CountJson(rows.Length);
        var page = RowsJson(rows);
        var scripted = new[] { count, page, count, page };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
        {
            var index = ordinal - 1;
            Assert.IsLessThan(
                scripted.Length, index, "the run asked for more responses than this script holds.");
            return LuxembourgAcquisitionTestFixture.JsonResponse(request, scripted[index]);
        });

        return await ProducerFor(handler).RunAsync(RequestFor(batch), Witness(), CancellationToken.None);
    }

    /// <summary>A row carrying the publisher's digest of some OTHER value in key_4.</summary>
    private static string RowWithKeyOf(string subject, string predicate, string value, string keyedAs) =>
        Row(subject, predicate, value, iri: false)
            .Replace(
                LuxembourgPublisherCursorCodec.ComputeKey(value),
                LuxembourgPublisherCursorCodec.ComputeKey(keyedAs),
                StringComparison.Ordinal);

    private static LuxembourgOpinionRequestGraphRunRequest RequestFor(IReadOnlyList<string> batch) =>
        LuxembourgOpinionRequestGraphRunRequest.ForBatch(
            LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
            batch,
            Inventory(batch),
            0,
            NewUrn(),
            Source(),
            WireRequestBudget.OfWireRequests(1000));

    private static LuxembourgOpinionRequestGraphProducer ProducerFor(
        LuxembourgAcquisitionTestFixture.SequencedHandler handler) =>
        new(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    /// <summary>One delivered row, in the plan's own projected order and keyed by its own terms.</summary>
    private static string Row(string subject, string predicate, string value, bool iri)
    {
        var valueKind = iri ? IriKind : "literal";
        var valueJson = iri
            ? "{\"type\":\"uri\",\"value\":\"" + value + "\"}"
            : "{\"type\":\"literal\",\"value\":\"" + value + "\"}";
        return "{\"request\":{\"type\":\"uri\",\"value\":\"" + subject + "\"},"
            + "\"request_kind\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
            + "\"predicate\":{\"type\":\"uri\",\"value\":\"" + predicate + "\"},"
            + "\"value\":" + valueJson + ","
            + "\"value_kind\":{\"type\":\"literal\",\"value\":\"" + valueKind + "\"},"
            + "\"datatype_iri\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"language_tag\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"multiplicity\":{\"type\":\"typed-literal\","
            + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
            + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + subject + "\"},"
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
            + "\"key_3\":{\"type\":\"literal\",\"value\":\"" + predicate + "\"},"
            + "\"key_4\":{\"type\":\"literal\",\"value\":\""
            + LuxembourgPublisherCursorCodec.ComputeKey(value) + "\"},"
            + "\"key_5\":{\"type\":\"literal\",\"value\":\"" + valueKind + "\"},"
            + "\"key_6\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_7\":{\"type\":\"literal\",\"value\":\"\"}}";
    }

    private static string CountJson(long count) =>
        "{\"head\":{\"link\":[],\"vars\":[\"count\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{\"count\":"
        + "{\"type\":\"typed-literal\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\","
        + "\"value\":\"" + count.ToString(CultureInfo.InvariantCulture) + "\"}}]}}";

    private static string RowsJson(IReadOnlyList<string> rows) =>
        "{\"head\":{\"link\":[],\"vars\":"
        + "[\"request\",\"request_kind\",\"predicate\",\"value\",\"value_kind\","
        + "\"datatype_iri\",\"language_tag\",\"multiplicity\","
        + "\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\",\"key_6\",\"key_7\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + string.Join(',', rows) + "]}}";

    private static LuxembourgOpinionRequestInventoryCitation Inventory(IReadOnlyList<string> subjects)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures, ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered);
    }

    private static BoundMachineRequest Witness()
    {
        var (witnessPlan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9801);
        return witnessPlan.BindCount(
            planResourceId,
            NewUrn(),
            NewUrn(),
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9801)).Request;
    }

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-opinion-request-graph-producer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1";
}
