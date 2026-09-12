using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the OpinionRequest inventory publishes, and what it refuses to publish.
/// </summary>
/// <remarks>
/// <para>
/// THE HONEST PATH IS DRIVEN THROUGH THE REAL PRODUCER, not through its decoder. Every refusal case
/// would still pass on a producer that refuses everything, and a decoder-only suite cannot tell you
/// the run, the proof, the page reopen and the verified-row re-derivation actually line up. That
/// lesson came from #560, where I asserted an entry point could not be tested offline and a reviewer
/// showed me the seam.
/// </para>
/// <para>
/// The cases that cannot be reached end to end say so and reach <c>DecodeRows</c> instead: a
/// delivery repeating a subject, or carrying a key that does not key its own terms, cannot be proven
/// at all, because Source/Core requires canonical keys unique and cursors strictly increasing. Those
/// guards defend the decoder against a delivery the proof would already have refused, and the
/// distinction is stated rather than blurred.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestInventoryProducerTests
{
    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    private const string BlankNodeKind =
        LuxembourgOpinionRequestInventoryDiscoveryPlan.UnsupportedBlankNodeKind;

    /// <summary>An honest whole-class delivery mints the inventory and its citation.</summary>
    [TestMethod]
    public async Task AnHonestClassDeliveryMintsAnInventoryAndItsCitation()
    {
        var subjects = new[] { Request(1), Request(2), Request(3) };

        var result = await RunAsync(subjects.Select(IriRow).ToArray());

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.IsNotNull(result.Citation);
        Assert.AreEqual(subjects.Length, result.Citation.SubjectCount);
        CollectionAssert.AreEqual(
            subjects.Order(StringComparer.Ordinal).ToArray(),
            result.AddressableInOrder().ToArray(),
            "the population handed to batching is the delivered class in ordinal order.");
        Assert.AreEqual(
            LuxembourgOpinionRequestGraphDiscoveryPlan.SelectionDigestFor(result.AddressableInOrder()),
            result.Citation.SelectionDigest,
            "the citation digests the population it hands on, not the delivery order.");
        Assert.AreEqual(0, result.ObservedNonAddressable.Count);
        Assert.IsGreaterThan(0, result.ProductRequestCount);
    }

    /// <summary>
    /// A member no request can name refuses the inventory and is named on the refusal.
    /// </summary>
    /// <remarks>
    /// A blank node's label is scoped to the response that carried it, so no exact membership list
    /// exists over one. The member is reported rather than filtered: an inventory quietly smaller
    /// than the class is the false absence arriving before anything has been asked.
    /// </remarks>
    [TestMethod]
    public async Task ABlankNodeMemberRefusesTheInventoryAndIsNamed()
    {
        // KEY ORDER, because a proven delivery is necessarily key-ordered: Source/Core requires
        // cursors to strictly increase, and the label "b0" sorts before an http IRI. Delivering the
        // IRI first refuses as CursorDidNotAdvance and never reaches the rule under test.
        var result = await RunAsync([BlankNodeRow("b0"), IriRow(Request(1))]);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(
            LuxembourgOpinionRequestInventoryRefusal.NonAddressableSubjectObserved, result.Refusal,
            result.Detail);
        Assert.IsNull(result.Citation, "a refused inventory mints no citation.");
        Assert.IsNull(result.Subjects);
        CollectionAssert.AreEqual(
            new[] { "b0" },
            result.ObservedNonAddressable.Select(static value => value.Value).ToArray(),
            "the member the publisher holds is named by the run that declined to call itself complete.");
        StringAssert.Contains(result.Detail ?? string.Empty, "b0");
    }

    /// <summary>A refused inventory has no population to batch, and says so rather than returning none.</summary>
    [TestMethod]
    public async Task ARefusedInventoryHasNoPopulationToBatch()
    {
        var result = await RunAsync([BlankNodeRow("b0")]);

        Assert.IsFalse(result.Delivered);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => result.AddressableInOrder(),
            "an empty list here would read as a class with no members.");
    }

    /// <summary>
    /// The publisher's own reason travels out with an enumeration refusal.
    /// </summary>
    /// <remarks>
    /// A refusal reading only <c>EnumerationRefused</c> throws away the one thing that makes a live
    /// failure debuggable without asking the publisher again, and the executor already retained it.
    /// </remarks>
    [TestMethod]
    public async Task AnEnumerationRefusalCarriesThePublishersOwnReason()
    {
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, request) =>
            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Version = System.Net.HttpVersion.Version11,
                RequestMessage = request,
                Content = new System.Net.Http.StringContent(string.Empty),
            });

        var result = await ProducerFor(handler).RunAsync(
            new LuxembourgOpinionRequestInventoryRunRequest(
                LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(),
                WireRequestBudget.OfWireRequests(1000)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(
            LuxembourgOpinionRequestInventoryRefusal.EnumerationRefused, result.Refusal);
        Assert.IsNotNull(result.Detail);
        Assert.AreNotEqual(
            string.Empty, result.Detail, "the executor's own refusal code must reach the caller.");
    }

    /// <summary>
    /// The same subject twice is refused rather than deduplicated.
    /// </summary>
    /// <remarks>
    /// REACHED THROUGH THE DECODER, because a delivery repeating a subject repeats a canonical key
    /// and cannot be proven at all. This guard defends the decoder against a delivery the proof
    /// would already have refused; saying that is more useful than a case pretending the publisher
    /// could get one past both.
    /// </remarks>
    [TestMethod]
    public void TheSameSubjectTwiceIsRefusedRatherThanDeduplicated()
    {
        var subject = Request(1);
        var (proof, profile) = ProofAndProfile([subject]);

        var result = LuxembourgOpinionRequestInventoryProducer.DecodeRows(
            [DecodedRow(subject, IriKind), DecodedRow(subject, IriKind)], profile, proof);

        Assert.AreEqual(
            LuxembourgOpinionRequestInventoryRefusal.SubjectDeliveredTwice, result.Refusal);
        StringAssert.Contains(result.Detail ?? string.Empty, subject);
    }

    /// <summary>
    /// The population handed to batching is the one the citation was minted over, not a second
    /// derivation of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUND BY MUTATION ON MY OWN SLICE, and fixed structurally rather than by a test. Minting the
    /// citation over delivery order left every case green, and the reason turned out to be that the
    /// citation door binds rows by an ORDER-SENSITIVE canonical-key digest: rows in any other order
    /// are refused before the population is read, so no delivery that can mint a citation ever
    /// disagrees with ordinal order.
    /// </para>
    /// <para>
    /// The ordering was therefore unreachable as a guard - but it was also being derived twice, once
    /// for the citation and once by <c>AddressableInOrder</c>, which is two chances to disagree about
    /// what the batches must cover. It is now derived once and returned, so a disagreement is
    /// unrepresentable instead of untested. This case pins that they are one list.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ThePopulationHandedToBatchingIsTheOneTheCitationWasMintedOver()
    {
        var subjects = new[] { Request(1), Request(2), Request(3) };

        var result = await RunAsync(subjects.Select(IriRow).ToArray());

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.IsNotNull(result.Citation);
        Assert.AreEqual(
            LuxembourgOpinionRequestGraphDiscoveryPlan.SelectionDigestFor(result.AddressableInOrder()),
            result.Citation.SelectionDigest);
        Assert.AreSame(
            result.AddressableInOrder(),
            result.AddressableInOrder(),
            "one stored list, not a fresh derivation on every call.");
    }

    /// <summary>A marker contradicting the term it describes refuses the row.</summary>
    /// <remarks>
    /// The marker column is projected, grouped and keyed on, so it is authoritative. Recomputing
    /// what it ought to say and never reading it would let the delivered column contradict both the
    /// term and the key.
    /// </remarks>
    [TestMethod]
    public void AMarkerContradictingItsOwnTermRefusesTheRow()
    {
        var subject = Request(1);
        var (proof, profile) = ProofAndProfile([subject]);

        var result = LuxembourgOpinionRequestInventoryProducer.DecodeRows(
            [DecodedRow(subject, BlankNodeKind)], profile, proof);

        Assert.AreEqual(LuxembourgOpinionRequestInventoryRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail ?? string.Empty, "request_kind");
    }

    /// <summary>A key that does not key its own terms refuses the row.</summary>
    [TestMethod]
    public void AKeyThatDoesNotKeyItsOwnTermsRefusesTheRow()
    {
        var subject = Request(1);
        var (proof, profile) = ProofAndProfile([subject]);
        var row = DecodedRow(subject, IriKind);
        var rekeyed = new RepeatedEnumerationRow(
            [
                row.Terms[0], row.Terms[1], row.Terms[2],
                RepeatedEnumerationRdfTerm.Literal(Request(97), null, null),
                row.Terms[4],
            ],
            row.CanonicalKey,
            row.Cursor);

        var result = LuxembourgOpinionRequestInventoryProducer.DecodeRows(
            [rekeyed], profile, proof);

        Assert.AreEqual(LuxembourgOpinionRequestInventoryRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail ?? string.Empty, "key_1");
    }

    /// <summary>A subject RDF cannot put in subject position refuses the row.</summary>
    [TestMethod]
    public void ALiteralSubjectRefusesTheRow()
    {
        var subject = Request(1);
        var (proof, profile) = ProofAndProfile([subject]);
        var row = DecodedRow(subject, IriKind);
        var literalSubject = new RepeatedEnumerationRow(
            [RepeatedEnumerationRdfTerm.Literal(subject, null, null), .. row.Terms.Skip(1)],
            row.CanonicalKey,
            row.Cursor);

        var result = LuxembourgOpinionRequestInventoryProducer.DecodeRows(
            [literalSubject], profile, proof);

        Assert.AreEqual(LuxembourgOpinionRequestInventoryRefusal.RowNotAdmitted, result.Refusal);
    }

    private static async Task<LuxembourgOpinionRequestInventoryResult> RunAsync(string[] rows)
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

        return await ProducerFor(handler).RunAsync(
            new LuxembourgOpinionRequestInventoryRunRequest(
                LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(),
                WireRequestBudget.OfWireRequests(1000)),
            LuxembourgSourceWitness(),
            CancellationToken.None);
    }

    private static LuxembourgOpinionRequestInventoryProducer ProducerFor(
        LuxembourgAcquisitionTestFixture.SequencedHandler handler) =>
        new(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    private static (AbsenceFamilyEnumerationProof Proof,
        RepeatedEnumerationInterpretationProfile Profile) ProofAndProfile(string[] subjects)
    {
        var (proof, _) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures, subjects);
        return (proof, LuxembourgOpinionRequestInventoryDiscoveryPlan.Create().CreateDeliveryProfile());
    }

    /// <summary>One decoded row in the inventory's own projected order.</summary>
    private static RepeatedEnumerationRow DecodedRow(string subject, string markerKind)
    {
        var key = new[]
        {
            RepeatedEnumerationRdfTerm.Literal(subject, null, null),
            RepeatedEnumerationRdfTerm.Literal(markerKind, null, null),
        };
        return new RepeatedEnumerationRow(
            [
                RepeatedEnumerationRdfTerm.Iri(subject),
                RepeatedEnumerationRdfTerm.Literal(markerKind, null, null),
                RepeatedEnumerationRdfTerm.Literal(
                    "1", "http://www.w3.org/2001/XMLSchema#integer", null),
                key[0],
                key[1],
            ],
            key,
            key);
    }

    private static string IriRow(string subject) =>
        "{\"request\":{\"type\":\"uri\",\"value\":\"" + subject + "\"},"
        + "\"request_kind\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
        + "\"multiplicity\":{\"type\":\"typed-literal\","
        + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
        + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + subject + "\"},"
        + "\"key_2\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"}}";

    private static string BlankNodeRow(string label) =>
        "{\"request\":{\"type\":\"bnode\",\"value\":\"" + label + "\"},"
        + "\"request_kind\":{\"type\":\"literal\",\"value\":\"" + BlankNodeKind + "\"},"
        + "\"multiplicity\":{\"type\":\"typed-literal\","
        + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
        + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + label + "\"},"
        + "\"key_2\":{\"type\":\"literal\",\"value\":\"" + BlankNodeKind + "\"}}";

    private static string CountJson(long count) =>
        "{\"head\":{\"link\":[],\"vars\":[\"count\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{\"count\":"
        + "{\"type\":\"typed-literal\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\","
        + "\"value\":\"" + count.ToString(CultureInfo.InvariantCulture) + "\"}}]}}";

    private static string RowsJson(IReadOnlyList<string> rows) =>
        "{\"head\":{\"link\":[],\"vars\":"
        + "[\"request\",\"request_kind\",\"multiplicity\",\"key_1\",\"key_2\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + string.Join(',', rows) + "]}}";

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (witnessPlan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9701);
        return witnessPlan.BindCount(
            planResourceId,
            NewUrn(),
            NewUrn(),
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9701)).Request;
    }

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-opinion-request-inventory-producer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1";
}
