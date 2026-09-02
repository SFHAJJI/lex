using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class RepeatedEnumerationDeliveryProofTests
{
    // Exact HTTP 200 bodies from bounded VALUES probes against the Legilux and Cellar SPARQL endpoints, observed 2026-09-02.
    private const string LegiluxSparqlCountObservation20260902 = "\n{ \"head\": { \"link\": [], \"vars\": [\"count\"] },\n  \"results\": { \"distinct\": false, \"ordered\": true, \"bindings\": [\n    { \"count\": { \"type\": \"typed-literal\", \"datatype\": \"http://www.w3.org/2001/XMLSchema#integer\", \"value\": \"1\" }} ] } }";
    private const string CellarSparqlCountObservation20260902 = "\n{ \"head\": { \"link\": [], \"vars\": [\"count\"] },\n  \"results\": { \"distinct\": false, \"ordered\": true, \"bindings\": [\n    { \"count\": { \"type\": \"literal\", \"datatype\": \"http://www.w3.org/2001/XMLSchema#integer\", \"value\": \"1\" }} ] } }";
    private const string LegiluxSparqlRowObservation20260902 = "\n{ \"head\": { \"link\": [], \"vars\": [\"id\", \"cursor\", \"value\"] },\n  \"results\": { \"distinct\": false, \"ordered\": true, \"bindings\": [\n    { \"id\": { \"type\": \"uri\", \"value\": \"urn:lex:v3:probe:lu\" }\t, \"cursor\": { \"type\": \"literal\", \"value\": \"a\" }\t, \"value\": { \"type\": \"literal\", \"value\": \"x\" }} ] } }";
    private const string CellarSparqlRowObservation20260902 = "\n{ \"head\": { \"link\": [], \"vars\": [\"id\", \"cursor\", \"value\"] },\n  \"results\": { \"distinct\": false, \"ordered\": true, \"bindings\": [\n    { \"id\": { \"type\": \"uri\", \"value\": \"urn:lex:v3:probe:eu\" }\t, \"cursor\": { \"type\": \"literal\", \"value\": \"a\" }\t, \"value\": { \"type\": \"literal\", \"value\": \"x\" }} ] } }";

    [TestMethod]
    public void ThresholdUsesTheSourceProfileMaximumAndNotTheRequestLimit()
    {
        var profile = new Fixture(maximumDeliverableRows: 999).ProfileForTest;
        Assert.AreEqual(RepeatedEnumerationThresholdAssessment.BelowMaximum, EnumerationDeliveryComparison.AssessThreshold(998, profile));
        Assert.AreEqual(RepeatedEnumerationThresholdAssessment.PartitionRequired, EnumerationDeliveryComparison.AssessThreshold(999, profile));
        Assert.AreEqual(RepeatedEnumerationThresholdAssessment.PartitionRequired, EnumerationDeliveryComparison.AssessThreshold(1_000, profile));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EnumerationDeliveryComparison.AssessThreshold(-1, profile));
        Assert.AreEqual(RepeatedEnumerationThresholdAssessment.PartitionRequired, new Fixture(maximumDeliverableRows: 2).Create("a,b", "a,b").ThresholdAssessment);
    }

    [TestMethod]
    public void RdfTermsRejectInvalidUnicodeAndAmbiguousLiteralShapes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RepeatedEnumerationRdfTerm.Iri("\ud800"));
        Assert.ThrowsExactly<ArgumentException>(() => RepeatedEnumerationRdfTerm.Literal("value", "urn:type", "fr"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("é")]
    [DataRow("\u0001")]
    [DataRow("😀")]
    public void CursorEnvelopeRoundTripsRawUnicodeThroughLowercaseHex(string raw)
    {
        var encoded = EnumerationCursorEnvelope.Encode(raw);

        StringAssert.Matches(encoded, new System.Text.RegularExpressions.Regex("^h[0-9a-f]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant));
        Assert.AreEqual(raw, EnumerationCursorEnvelope.Decode(encoded));
    }

    [TestMethod]
    public void CursorEnvelopeRejectsNoncanonicalOrInvalidUtf8()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EnumerationCursorEnvelope.Decode("hC3A9"));
        Assert.ThrowsExactly<ArgumentException>(() => EnumerationCursorEnvelope.Decode("h0"));
        Assert.ThrowsExactly<ArgumentException>(() => EnumerationCursorEnvelope.Decode("hgg"));
        Assert.ThrowsExactly<ArgumentException>(() => EnumerationCursorEnvelope.Decode("hff"));
    }

    [TestMethod]
    public void CursorComparisonUsesUtf8BytesRatherThanUtf16CodeUnits()
    {
        const string privateUseBmp = "\uE000";
        const string supplementary = "\U00010000";

        Assert.IsTrue(string.CompareOrdinal(privateUseBmp, supplementary) > 0, "the vector must distinguish UTF-16 ordering");
        Assert.IsTrue(EnumerationCursorEnvelope.CompareRaw(privateUseBmp, supplementary) < 0, "the endpoint-calibrated UTF-8 order must place U+E000 first");
    }

    [TestMethod]
    public void ProofIsFactoryOnlyAndCannotBeDeserializedAsAClaim()
    {
        Assert.AreEqual(0, typeof(EnumerationDeliveryComparison).GetConstructors().Length);
        var privateConstructors = typeof(EnumerationDeliveryComparison).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsFalse(privateConstructors.Any(constructor => constructor.GetCustomAttributes(typeof(JsonConstructorAttribute), false).Any()));
    }

    [TestMethod]
    public void ComparisonExposesExactlyTheContentBoundPublicApi()
    {
        CollectionAssert.AreEquivalent(new[] { "InterpretationProfileRef", "ThresholdAssessment", "CountA", "PagesA", "CountB", "PagesB", "ObservationTimes", "SelectedRowCountA", "SelectedRowCountB", "DeliveredRowCountA", "DeliveredRowCountB", "CanonicalRowDigestA", "CanonicalRowDigestB", "CanonicalKeyDigestA", "CanonicalKeyDigestB", "CursorDigestA", "CursorDigestB", "Outcome" }, typeof(EnumerationDeliveryComparison).GetProperties().Select(static property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Create", "AssessThreshold" }, typeof(EnumerationDeliveryComparison).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly).Where(static method => !method.IsSpecialName).Select(static method => method.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "QueryPlanRef", "QueryInputRef", "RenderReceiptRef", "RequestEvidenceRef", "ObservationRef" }, typeof(RepeatedEnumerationEvidenceRefs).GetProperties().Select(static property => property.Name).ToArray());
    }

    [TestMethod]
    public void FactoryRecomputesRetainedBodiesAndComparesOnlyDeliveredSelections()
    {
        var fixture = new Fixture();
        var proof = fixture.Create("a,b", "a,b");

        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, proof.Outcome);
        Assert.AreEqual(2, proof.DeliveredRowCountA);
        Assert.AreEqual(proof.CountA.ObservationRef, fixture.FirstObservationRef);
        Assert.AreEqual(4, fixture.ResolveCalls);

        var changed = new Fixture().Create("a|same,b|old", "a|same,b|new");
        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections, changed.Outcome);
    }

    [TestMethod]
    public void AcceptedRdfDatatypeAndLanguageArePartOfSelectionIdentity()
    {
        var id = Iri("a");
        var cursor = PlainLiteral("a");
        var datatypeA = ValidRowDocument(id, cursor, "{\"type\":\"literal\",\"value\":\"x\",\"datatype\":\"urn:type:a\"}");
        var datatypeB = ValidRowDocument(id, cursor, "{\"type\":\"literal\",\"value\":\"x\",\"datatype\":\"urn:type:b\"}");
        var languageA = ValidRowDocument(id, cursor, "{\"type\":\"literal\",\"value\":\"x\",\"xml:lang\":\"fr\"}");
        var languageB = ValidRowDocument(id, cursor, "{\"type\":\"literal\",\"value\":\"x\",\"xml:lang\":\"de\"}");

        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections, new Fixture(rawRowsA: datatypeA, rawRowsB: datatypeB, expectedCount: 1).Create("ignored", "ignored").Outcome);
        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections, new Fixture(rawRowsA: languageA, rawRowsB: languageB, expectedCount: 1).Create("ignored", "ignored").Outcome);
    }

    [TestMethod]
    public void FactoryRejectsLyingRequestReferenceAndChangedPayloadBackingBytes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(badRequestRef: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(mutatePayload: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(badProfileRef: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(statusCode: 201).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(statusCode: 500).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(samePass: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(mismatchedPartition: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(samePageLimit: true).Create("a,b", "a,b"));
    }

    [TestMethod]
    [DataRow(RefMutation.Plan)]
    [DataRow(RefMutation.Input)]
    [DataRow(RefMutation.Receipt)]
    [DataRow(RefMutation.Observation)]
    public void FactoryRejectsEveryMismatchedRetainedReference(RefMutation mutation) =>
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(mutation: mutation).Create("a,b", "a,b"));

    [TestMethod]
    public void FactoryRejectsRendererDriftSelectionLiesAndInvalidPageChains()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rendererDrift: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(selectionLie: true).CreateTwoPage());
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(brokenContinuation: true).CreateTwoPage());
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(wrongExpectedPageCount: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(wrongPageCountRef: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(inconsistentWithinPassLimit: true).CreateTwoPage());
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture().Create("a,b,c,d,e,f,g,h,i,j", "a,b,c,d,e,f,g,h,i,j"));
    }

    [TestMethod]
    public void PageContinuationDecodesEnvelopeBeforeComparingRawCursorTerms()
    {
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, new Fixture().CreateTwoPage("\uE000", "\U00010000").Outcome);
    }

    [TestMethod]
    public void FactoryRejectsDuplicateKeysAndNonmonotonicCursors()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: RowsWithKeys(("same", "a"), ("same", "b"))).Create("ignored", "ignored"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture().Create("b,a", "b,a"));
    }

    [TestMethod]
    public void FactoryRejectsMissingAndBlankNodeCanonicalKeys()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument(null, PlainLiteral("a"), PlainLiteral("x"))).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument("{\"type\":\"bnode\",\"value\":\"node-a\"}", PlainLiteral("a"), PlainLiteral("x"))).Create("a", "a"));
    }

    [TestMethod]
    public void VerifiedTokenCannotSatisfyScopeReductionOrConstructCompletion()
    {
        Assert.IsFalse(typeof(IScopeReductionEvidenceResolver).IsAssignableFrom(typeof(EnumerationDeliveryComparison)));
        Assert.IsFalse(typeof(EnumerationDeliveryComparison).GetProperties().Any(property =>
            property.Name.Contains("CompleteEnumeration", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StrictParserRejectsProjectionDriftUnknownDuplicateAndWrongMediaType()
    {
        var valid = ValidRowDocument(Iri("a"), PlainLiteral("a"), PlainLiteral("x"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: valid.Replace("[\"id\",\"cursor\",\"value\"]", "[\"id\",\"value\",\"cursor\"]", StringComparison.Ordinal)).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: valid.Replace("\"value\":{\"type\":\"literal\",\"value\":\"x\"}", "\"value\":{\"type\":\"literal\",\"value\":\"x\"},\"unknown\":{\"type\":\"literal\",\"value\":\"x\"}", StringComparison.Ordinal)).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: valid.Replace("[\"id\",\"cursor\",\"value\"]", "[\"id\",\"cursor\",\"cursor\"]", StringComparison.Ordinal)).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: valid.Replace("\"cursor\":{\"type\":\"literal\",\"value\":\"a\"}", "\"cursor\":{\"type\":\"literal\",\"value\":\"a\"},\"cursor\":{\"type\":\"literal\",\"value\":\"a\"}", StringComparison.Ordinal)).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(mediaType: "application/json").Create("a,b", "a,b"));
    }

    [TestMethod]
    public void StrictParserRejectsUnknownAndAmbiguousRdfBindings()
    {
        var cursor = PlainLiteral("a");
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument("{\"type\":\"triple\",\"value\":\"x\"}", cursor, PlainLiteral("x"))).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"literal\",\"value\":\"x\",\"datatype\":\"urn:type\",\"xml:lang\":\"fr\"}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"literal\",\"value\":\"x\",\"datatype\":null}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"literal\",\"value\":\"x\",\"xml:lang\":\"\"}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"literal\",\"value\":\"x\",\"extra\":\"y\"}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument("{\"type\":\"uri\",\"value\":\"urn:row:a\",\"extra\":\"y\"}", cursor, PlainLiteral("x"))).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":null,\"value\":\"x\"}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"literal\",\"value\":null}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"typed-literal\",\"value\":\"x\",\"datatype\":\"\"}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"typed-literal\",\"value\":\"x\",\"datatype\":null}")).Create("a", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, rawRows: ValidRowDocument(Iri("a"), cursor, "{\"type\":\"typed-literal\",\"value\":\"x\",\"datatype\":\"urn:type\",\"xml:lang\":\"fr\"}")).Create("a", "a"));
    }

    [TestMethod]
    public void ProfilePinsThresholdAndCursorCodecAuthorities()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ProfileWith(cursorEnvelopeIdentity: "raw/1"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProfileWith(maximumDeliverableRows: 0));
        Assert.ThrowsExactly<ArgumentException>(() => ProfileWith(thresholdDetectorIdentity: "caller-threshold/1"));
    }

    [TestMethod]
    public void FrozenLuxembourgAndEuropeanUnionVirtuosoCountDialectsAreExplicit()
    {
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso).Create("a,b", "a,b").Outcome);
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso).Create("a,b", "a,b").Outcome);
    }

    [TestMethod]
    public void VirtuosoEnvelopeMembersAndDialectTypesAreRequired()
    {
        var eu = new Fixture().OfficialCountJson(2);
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawCount: eu.Replace("\"link\":[],", string.Empty, StringComparison.Ordinal)).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawCount: eu.Replace("\"distinct\":false,", string.Empty, StringComparison.Ordinal)).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawCount: eu.Replace("\"ordered\":true,", string.Empty, StringComparison.Ordinal)).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, rawCount: eu).Create("a,b", "a,b"));
    }

    [TestMethod]
    public void CountIsOpaqueAndInputRolesAreClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(badCountCardinality: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(extraCountParameter: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(omitPageState: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(badHasCursor: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(reorderParameters: true).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new RepeatedEnumerationInterpretationProfile(
            RepeatedEnumerationInterpretationProfile.SchemaId, RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso, "application/sparql-results+json",
            EnumerationCursorEnvelope.Identity, 100, "enumeration-row-threshold/1",
            new(Fixture.Artifact(905), "count"), new(Fixture.Artifact(905), "page"), "count", ["id"], ["id"], ["id"], ["cursor"], "pass_id", ["cursor"], "has_cursor"));
    }

    [TestMethod]
    public void CursorProjectionIsOnlyPlainLiteralOrdinalText()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: CursorRowDocument("{\"type\":\"uri\",\"value\":\"a\"}")).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: CursorRowDocument("{\"type\":\"literal\",\"value\":\"a\",\"datatype\":\"urn:type\"}")).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: CursorRowDocument("{\"type\":\"literal\",\"value\":\"a\",\"xml:lang\":\"fr\"}")).Create("a,b", "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(rawRows: CursorRowDocument(null)).Create("a,b", "a,b"));
    }

    [TestMethod]
    public void LiteralObservedLegiluxAndCellarValuesProbeVectorsPass()
    {
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, rawCount: LegiluxSparqlCountObservation20260902, rawRows: LegiluxSparqlRowObservation20260902, expectedCount: 1).Create("ignored", "ignored").Outcome);
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, new Fixture(dialect: RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso, rawCount: CellarSparqlCountObservation20260902, rawRows: CellarSparqlRowObservation20260902, expectedCount: 1).Create("ignored", "ignored").Outcome);
    }

    [TestMethod]
    public void PageOrdinalsAreContiguousAndWallClockOrderIsNotAStabilityClaim()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fixture(badOrdinal: true).Create("a,b", "a,b"));
        var proof = new Fixture(reverseTimes: true).Create("a,b", "a,b");
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, proof.Outcome);
        Assert.IsTrue(string.CompareOrdinal(proof.ObservationTimes.CountA, proof.ObservationTimes.PagesA[0]) > 0);
        Assert.AreEqual(1, new Fixture(mutatePassRefs: true).Create("a,b", "a,b").PagesA.Pages.Count);
    }

    private static string Iri(string value) => $"{{\"type\":\"uri\",\"value\":\"urn:row:{value}\"}}";
    private static string PlainLiteral(string value) => $"{{\"type\":\"literal\",\"value\":\"{value}\"}}";
    private static string ValidRowDocument(string? id, string cursor, string value) => "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{" + (id is null ? string.Empty : $"\"id\":{id},") + $"\"cursor\":{cursor},\"value\":{value}" + "}]}}";
    private static string RowsWithKeys(params (string Id, string Cursor)[] rows) => "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[" + string.Join(',', rows.Select(row => $"{{\"id\":{{\"type\":\"uri\",\"value\":\"urn:row:{row.Id}\"}},\"cursor\":{{\"type\":\"literal\",\"value\":\"{row.Cursor}\"}}}}")) + "]}}";
    private static string CursorRowDocument(string? cursor) => "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{\"id\":{\"type\":\"uri\",\"value\":\"urn:row:a\"}" + (cursor is null ? string.Empty : $",\"cursor\":{cursor}") + "}]}}";
    private static RepeatedEnumerationInterpretationProfile ProfileWith(string cursorEnvelopeIdentity = EnumerationCursorEnvelope.Identity, long maximumDeliverableRows = 100, string thresholdDetectorIdentity = "enumeration-row-threshold/1") => new(
        RepeatedEnumerationInterpretationProfile.SchemaId, RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso, "application/sparql-results+json",
        cursorEnvelopeIdentity, maximumDeliverableRows, thresholdDetectorIdentity,
        new(Fixture.Artifact(905), "count"), new(Fixture.Artifact(905), "page"), "count", ["id", "cursor", "value"], ["id"], ["cursor"], ["scope"], "pass_id", ["cursor"], "has_cursor");

    private sealed class Fixture : IRepeatedEnumerationEvidenceResolver
    {
        private readonly Dictionary<SourceArtifactRef, RepeatedEnumerationResolvedEvidence> _resolved = [];
        private readonly Dictionary<SourceArtifactRef, byte[]> _payloads = [];
        private readonly bool _badRequestRef;
        private readonly bool _mutatePayload;
        private readonly RefMutation _mutation;
        private readonly bool _rendererDrift;
        private readonly bool _selectionLie;
        private readonly bool _brokenContinuation;
        private readonly string? _rawRows;
        private readonly string _mediaType;
        private readonly bool _badOrdinal;
        private readonly bool _reverseTimes;
        private readonly bool _badProfileRef;
        private readonly RepeatedEnumerationSparqlJsonDialect _dialect;
        private readonly string? _rawCount;
        private readonly bool _mutatePassRefs;
        private readonly bool _badCountCardinality;
        private readonly bool _extraCountParameter;
        private readonly bool _omitPageState;
        private readonly bool _badHasCursor;
        private readonly long _maximumDeliverableRows;
        private readonly long _expectedCount;
        private readonly int _statusCode;
        private readonly bool _samePass;
        private readonly bool _mismatchedPartition;
        private readonly bool _samePageLimit;
        private readonly bool _wrongExpectedPageCount;
        private readonly bool _wrongPageCountRef;
        private readonly bool _inconsistentWithinPassLimit;
        private readonly bool _reorderParameters;
        private readonly string? _rawRowsA;
        private readonly string? _rawRowsB;
        private List<RepeatedEnumerationPageRef>? _passToMutate;
        private readonly SourceRegistryMemberRef _countFamily = new(Artifact(905), "count-query");
        private readonly SourceRegistryMemberRef _pageFamily = new(Artifact(905), "page-query");
        public int ResolveCalls { get; private set; }
        public SourceArtifactRef? FirstObservationRef { get; private set; }

        public Fixture(
            bool badRequestRef = false,
            bool mutatePayload = false,
            RefMutation mutation = RefMutation.None,
            bool rendererDrift = false,
            bool selectionLie = false,
            bool brokenContinuation = false,
            string? rawRows = null,
            string mediaType = "application/sparql-results+json",
            bool badOrdinal = false,
            bool reverseTimes = false,
            bool badProfileRef = false,
            RepeatedEnumerationSparqlJsonDialect dialect = RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso,
            string? rawCount = null,
            bool mutatePassRefs = false,
            bool badCountCardinality = false,
            bool extraCountParameter = false,
            bool omitPageState = false,
            bool badHasCursor = false,
            long maximumDeliverableRows = 100,
            long expectedCount = 2,
            int statusCode = 200,
            bool samePass = false,
            bool mismatchedPartition = false,
            bool samePageLimit = false,
            bool wrongExpectedPageCount = false,
            bool wrongPageCountRef = false,
            bool inconsistentWithinPassLimit = false,
            bool reorderParameters = false,
            string? rawRowsA = null,
            string? rawRowsB = null)
        {
            _badRequestRef = badRequestRef;
            _mutatePayload = mutatePayload;
            _mutation = mutation;
            _rendererDrift = rendererDrift;
            _selectionLie = selectionLie;
            _brokenContinuation = brokenContinuation;
            _rawRows = rawRows;
            _mediaType = mediaType;
            _badOrdinal = badOrdinal;
            _reverseTimes = reverseTimes;
            _badProfileRef = badProfileRef;
            _dialect = dialect;
            _rawCount = rawCount;
            _mutatePassRefs = mutatePassRefs;
            _badCountCardinality = badCountCardinality;
            _extraCountParameter = extraCountParameter;
            _omitPageState = omitPageState;
            _badHasCursor = badHasCursor;
            _maximumDeliverableRows = maximumDeliverableRows;
            _expectedCount = expectedCount;
            _statusCode = statusCode;
            _samePass = samePass;
            _mismatchedPartition = mismatchedPartition;
            _samePageLimit = samePageLimit;
            _wrongExpectedPageCount = wrongExpectedPageCount;
            _wrongPageCountRef = wrongPageCountRef;
            _inconsistentWithinPassLimit = inconsistentWithinPassLimit;
            _reorderParameters = reorderParameters;
            _rawRowsA = rawRowsA;
            _rawRowsB = rawRowsB;
        }

        public RepeatedEnumerationInterpretationProfile ProfileForTest => Profile();

        public EnumerationDeliveryComparison Create(string rowsA, string rowsB)
        {
            var countA = Add(1, _rawCount ?? CountJson(_expectedCount), _expectedCount, Artifact(301), _reverseTimes ? DateTimeOffset.UnixEpoch.AddSeconds(5) : DateTimeOffset.UnixEpoch, true);
            var pageA = Add(2, _rawRowsA ?? _rawRows ?? RowsJson(rowsA), _expectedCount, countA.ObservationRef, DateTimeOffset.UnixEpoch.AddSeconds(1), false);
            var countB = Add(3, _rawCount ?? CountJson(_expectedCount), _expectedCount, Artifact(303), DateTimeOffset.UnixEpoch.AddSeconds(2), true);
            var pageB = Add(4, _rawRowsB ?? _rawRows ?? RowsJson(rowsB), _expectedCount, countB.ObservationRef, DateTimeOffset.UnixEpoch.AddSeconds(3), false, rowLimit: _samePageLimit ? 10 : 7);
            countA = Mutate(countA);
            var profile = Profile();
            var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(Artifact(920).ResourceId, profile);
            if (_badProfileRef)
            {
                profileRef = Artifact(999);
            }
            var passAPages = new List<RepeatedEnumerationPageRef> { new(_badOrdinal ? 1 : 0, pageA) };
            _passToMutate = passAPages;
            return EnumerationDeliveryComparison.Create(
                profile, profileRef, countA, new(passAPages),
                countB, new([new(0, pageB)]), this);
        }

        public EnumerationDeliveryComparison CreateTwoPage(string pageOneLastCursor = "j", string pageTwoCursor = "k")
        {
            var sequence = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", pageOneLastCursor, pageTwoCursor };
            var ten = string.Join(',', sequence.Take(10));
            var countA = Add(1, CountJson(11), 11, Artifact(301), DateTimeOffset.UnixEpoch, true);
            var pageA1 = Add(2, RowsJson(ten), 11, countA.ObservationRef, DateTimeOffset.UnixEpoch.AddSeconds(1), false);
            var pageA2 = Add(5, RowsJson(pageTwoCursor), 11, countA.ObservationRef, DateTimeOffset.UnixEpoch.AddSeconds(2), false, pageOneLastCursor);
            var countB = Add(3, CountJson(11), 11, Artifact(303), DateTimeOffset.UnixEpoch.AddSeconds(3), true);
            var pageB1 = Add(4, RowsJson(string.Join(',', sequence.Take(6))), 11, countB.ObservationRef, DateTimeOffset.UnixEpoch.AddSeconds(4), false, rowLimit: 6);
            var pageB2 = Add(6, RowsJson(string.Join(',', sequence.Skip(6))), 11, countB.ObservationRef, DateTimeOffset.UnixEpoch.AddSeconds(5), false, "f", 6);
            var profile = Profile();
            return EnumerationDeliveryComparison.Create(
                profile, RepeatedEnumerationInterpretationProfileIdentity.Create(Artifact(920).ResourceId, profile), countA, new([new(0, pageA1), new(1, pageA2)]),
                countB, new([new(0, pageB1), new(1, pageB2)]), this);
        }

        public RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs references)
        {
            ResolveCalls++;
            if (_mutatePassRefs && ResolveCalls == 1)
            {
                _passToMutate!.Clear();
            }
            var value = _resolved[references.ObservationRef];
            if (_mutatePayload && ResolveCalls == 1)
            {
                _payloads[references.ObservationRef][0] = (byte)'9';
            }
            return value;
        }

        private RepeatedEnumerationEvidenceRefs Add(int seed, string text, long count, SourceArtifactRef countRef, DateTimeOffset time, bool countQuery, string cursor = "start", long rowLimit = 10)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var effectiveRowLimit = _inconsistentWithinPassLimit && seed == 5 ? rowLimit - 1 : rowLimit;
            var expectedPageCount = _wrongExpectedPageCount && seed == 2 ? count + 1 : count;
            var expectedPageCountRef = _wrongPageCountRef && seed == 2 ? Artifact(999) : countRef;
            var cardinality = countQuery && !_badCountCardinality
                ? new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null)
                : new MachineResponseCardinality(MachineResponseCardinalityKind.BoundedRowSetPage, effectiveRowLimit, expectedPageCount, expectedPageCountRef);
            var family = countQuery ? _countFamily : _pageFamily;
            var parameters = new List<MachineQueryParameter> { new("scope", MachineQueryParameterKind.PublisherCursor, null, _selectionLie && seed == 5 ? "other" : "all", Artifact(906)) };
            var pass = _samePass || seed is 1 or 2 or 5 ? 1 : 2;
            parameters.Add(new("pass_id", MachineQueryParameterKind.BoundedInteger, pass, null, Artifact(906)));
            if (countQuery && _extraCountParameter)
            {
                parameters.Add(new("extra", MachineQueryParameterKind.PublisherCursor, null, "x", Artifact(906)));
            }
            if (!countQuery)
            {
                var claimsCursor = _badHasCursor || cursor != "start";
                if (!_omitPageState)
                {
                    parameters.Add(new("has_cursor", MachineQueryParameterKind.BoundedInteger, claimsCursor ? 1 : 0, null, Artifact(906)));
                }
                if (claimsCursor)
                {
                    var cursorValue = cursor == "start" ? "unexpected" : _brokenContinuation && seed == 5 ? "wrong" : cursor;
                    parameters.Add(new("cursor", MachineQueryParameterKind.PublisherCursor, null, EnumerationCursorEnvelope.Encode(cursorValue), Artifact(906)));
                }
            }
            if (_reorderParameters && seed == 2)
            {
                parameters.Reverse();
            }
            var partitionMemberKey = _mismatchedPartition && pass == 2 ? "other-laws" : "laws";
            var input = MachineQueryInputArtifact.Create(Artifact(seed + 100).ResourceId, family, partitionMemberKey, cardinality, parameters);
            var target = Encoding.ASCII.GetBytes("/feed");
            var plan = new MachineQueryPlan(MachineQueryPlan.SchemaId, input.QueryFamilyRef, Artifact(907), Artifact(908), HttpRequestMethod.Get, "https://publisher.example/feed", target.Length, Sha(target), cardinality, null, null, MachineQueryInputMode.RendererInputs, input.ArtifactRef, input.PartitionBinding, null, null);
            var planRef = MachineQueryPlanIdentity.Create(Artifact(seed + 110).ResourceId, plan);
            var renderer = new Renderer(plan.RendererProfileRef, plan.RendererSourceRef);
            var receipt = MachineQueryBinder.BindForSend(plan, planRef, input, renderer).RenderReceipt;
            if (_rendererDrift && seed == 1)
            {
                renderer.Drift = true;
            }
            var receiptRef = MachineQueryRenderReceiptIdentity.Create(Artifact(seed + 120).ResourceId, receipt);
            var execution = Artifact(seed + 130);
            var request = HttpRequestEvidence.CreateAtSend(new HttpRequestTemplate("https://publisher.example/feed", HttpRequestMethod.Get, execution, Artifact(909), Artifact(910), Artifact(911), new(OutboundCrawlerIdentity.Schema, OutboundCrawlerIdentity.Token), new("https", "publisher.example", 443), receipt), time);
            var observationId = Artifact(seed + 140).ResourceId;
            var digest = Sha(bytes);
            var blob = new DurableBlobRef(CustodySchemaIds.DurableBlobRef, digest, bytes.Length, CustodyClass.NightlyFloor90d);
            var policy = new CustodyPolicyEvidence(CustodySchemaIds.CustodyPolicyEvidence, blob, CustodyVerificationProfile.ImmutableObject1, Guid.NewGuid(), CustodyProtection.LockedTime, time, time.AddDays(91));
            var write = new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, blob, policy);
            var absent = new AbsentHttpHeader();
            var metadata = new HttpResponseMetadata(new SingleHttpHeader(_mediaType), absent, new SingleHttpHeader(bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), absent, absent, absent, absent, absent);
            var completion = new DeclaredContentLengthCompleteEvidence(TransferCompletionSchemaIds.TransferCompletionEvidence, request.AdapterIdentity, observationId, digest, bytes.Length);
            var statusDisposition = HttpStatusClassifier.Classify(_statusCode, metadata);
            var observation = new ResponseCompleteBodyObservation(HttpObservationSchemaIds.HttpObservation, observationId, request, "https://publisher.example/feed", _statusCode, statusDisposition, metadata, completion, write);
            var observationRef = HttpObservationIdentity.Create(observation);
            var requestEvidence = MachineRequestEvidence.FromReceipt(planRef, receiptRef, receipt, observationRef);
            var requestRef = _badRequestRef && seed == 1 ? Artifact(999) : MachineRequestEvidenceIdentity.Create(Artifact(seed + 150).ResourceId, requestEvidence);
            var refs = new RepeatedEnumerationEvidenceRefs(planRef, input.ArtifactRef, receiptRef, requestRef, observationRef);
            FirstObservationRef ??= observationRef;
            _resolved.Add(observationRef, new(plan, input, receipt, renderer, requestEvidence, observation, bytes));
            _payloads.Add(observationRef, bytes);
            return refs;
        }

        private RepeatedEnumerationInterpretationProfile Profile() => new(
            RepeatedEnumerationInterpretationProfile.SchemaId, _dialect, "application/sparql-results+json", EnumerationCursorEnvelope.Identity, _maximumDeliverableRows, "enumeration-row-threshold/1", _countFamily, _pageFamily, "count", ["id", "cursor", "value"], ["id"], ["cursor"], ["scope"], "pass_id", ["cursor"], "has_cursor");

        private string CountJson(long count) { var type = _dialect == RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso ? "typed-literal" : "literal"; return $"{{\"head\":{{\"link\":[],\"vars\":[\"count\"]}},\"results\":{{\"distinct\":false,\"ordered\":true,\"bindings\":[{{\"count\":{{\"type\":\"{type}\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"{count}\"}}}}]}}}}"; }
        public string OfficialCountJson(long count) => CountJson(count);
        private static string RowsJson(string values) => "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[" + string.Join(',', values.Split(',').Select(value => { var parts = value.Split('|', 2); var literal = parts.Length == 2 ? $",\"value\":{{\"type\":\"literal\",\"value\":\"{parts[1]}\"}}" : string.Empty; return $"{{\"id\":{{\"type\":\"uri\",\"value\":\"urn:row:{parts[0]}\"}},\"cursor\":{{\"type\":\"literal\",\"value\":\"{parts[0]}\"}}{literal}}}"; })) + "]}}";

        private RepeatedEnumerationEvidenceRefs Mutate(RepeatedEnumerationEvidenceRefs value)
        {
            var bad = Artifact(999);
            return _mutation switch
            {
                RefMutation.Plan => value with { QueryPlanRef = bad },
                RefMutation.Input => value with { QueryInputRef = bad },
                RefMutation.Receipt => value with { RenderReceiptRef = bad },
                RefMutation.Observation => MutateObservation(value, bad),
                _ => value,
            };
        }

        private RepeatedEnumerationEvidenceRefs MutateObservation(RepeatedEnumerationEvidenceRefs value, SourceArtifactRef bad)
        {
            _resolved.Add(bad, _resolved[value.ObservationRef]);
            _payloads.Add(bad, _payloads[value.ObservationRef]);
            return value with { ObservationRef = bad };
        }

        internal static SourceArtifactRef Artifact(int seed) => new($"urn:uuid:00000000-0000-4000-8000-{seed:D12}", seed.ToString("x64"));
        private static string Sha(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
        private sealed class Renderer(SourceArtifactRef rendererProfileRef, SourceArtifactRef rendererSourceRef) : IMachineQueryRenderer
        {
            public SourceArtifactRef RendererProfileRef { get; } = rendererProfileRef;
            public SourceArtifactRef RendererSourceRef { get; } = rendererSourceRef;
            public bool Drift { get; set; }
            public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) => new(Drift ? "https://publisher.example/changed" : "https://publisher.example/feed", []);
        }
    }

    public enum RefMutation { None, Plan, Input, Receipt, Observation }
}
