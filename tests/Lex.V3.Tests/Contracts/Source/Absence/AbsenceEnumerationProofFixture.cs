using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// Builds a real <see cref="EnumerationDeliveryComparison"/> so the absence tests can hold the
/// proof a complete cut now requires.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a stub or a test double. The point of the change under test is that
/// completeness is demonstrated rather than declared, and a fake comparison would make the absence
/// tests pass against a proof that proves nothing, which is the defect wearing a test's clothes.
/// So this assembles the whole retained tuple <c>Source.Core</c> insists on: two counts, two page
/// sets at different page limits, machine query plans bound to their ordered-parameter artifacts,
/// render receipts reproduced offline, logical requests whose headers derive the official
/// publisher source profile, routed HTTP evidence with a single complete 200 hop, and custody
/// write receipts whose digests bind the retained bytes.
/// </para>
/// <para>
/// It exposes only the honest path plus the two conditions the absence proof reads: whether the
/// passes deliver the same rows, and whether the selection reaches the endpoint row cap. Every
/// other way a comparison can be refused is <c>Source.Core</c>'s own subject and is exercised
/// there.
/// </para>
/// </remarks>
internal sealed class AbsenceEnumerationProofFixture : IRepeatedEnumerationEvidenceResolver
{
    private const string CountVariable = "count";
    private const string PassParameter = "pass_id";
    private const string HasCursorParameter = "has_cursor";

    private readonly Dictionary<SourceArtifactRef, RepeatedEnumerationResolvedEvidence> _resolved = [];
    private readonly string _partitionKey;
    private readonly int _runSeed;
    private readonly long _maximumDeliverableRows;
    private readonly SourceRegistryMemberRef _countFamily = new(Artifact(905), "count-query");
    private readonly SourceRegistryMemberRef _pageFamily = new(Artifact(905), "page-query");

    private AbsenceEnumerationProofFixture(string partitionKey, int runSeed, long maximumDeliverableRows)
    {
        _partitionKey = partitionKey;
        _runSeed = runSeed;
        _maximumDeliverableRows = maximumDeliverableRows;
    }

    /// <summary>
    /// A comparison whose two passes delivered the same two rows under different page limits, with
    /// a row cap far above the selection. The admitting case.
    /// </summary>
    public static EnumerationDeliveryComparison Delivery(
        string partitionKey = "lu_root_family",
        int runSeed = 930) =>
        new AbsenceEnumerationProofFixture(partitionKey, runSeed, 100).Build("a,b", "a,b");

    /// <summary>
    /// A comparison whose passes disagreed. Both counted two rows and both delivered two rows, so
    /// only the row identities differ and only the digest comparison can refuse it.
    /// </summary>
    public static EnumerationDeliveryComparison DeliveryWithDisagreeingPasses() =>
        new AbsenceEnumerationProofFixture("lu_root_family", 930, 100).Build("a,b", "a,c");

    /// <summary>
    /// A comparison whose passes agreed exactly, over a selection that reached the endpoint's
    /// maximum deliverable row count. This is the silent truncation both publisher endpoints
    /// perform: it looks identical to a whole enumeration from the rows alone, which is why the
    /// threshold is a separate condition.
    /// </summary>
    public static EnumerationDeliveryComparison DeliveryAtTheRowCap() =>
        new AbsenceEnumerationProofFixture("lu_root_family", 930, 2).Build("a,b", "a,b");

    public RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return _resolved[references.HttpEvidenceRef];
    }

    internal static SourceArtifactRef Artifact(int seed) =>
        new($"urn:uuid:00000000-0000-4000-8000-{seed:D12}", seed.ToString("x64"));

    private EnumerationDeliveryComparison Build(string rowsA, string rowsB)
    {
        var rowCount = rowsA.Split(',').Length;
        var countA = Add(1, CountJson(rowCount), rowCount, Artifact(301), DateTimeOffset.UnixEpoch, true, 1);
        var pageA = Add(
            2, RowsJson(rowsA), rowCount, countA.HttpEvidenceRef,
            DateTimeOffset.UnixEpoch.AddSeconds(1), false, 1);
        var countB = Add(
            3, CountJson(rowCount), rowCount, Artifact(303),
            DateTimeOffset.UnixEpoch.AddSeconds(2), true, 2);
        var pageB = Add(
            4, RowsJson(rowsB), rowCount, countB.HttpEvidenceRef,
            DateTimeOffset.UnixEpoch.AddSeconds(3), false, 2, rowLimit: 7);
        var profile = Profile();
        return EnumerationDeliveryComparison.Create(
            profile,
            RepeatedEnumerationInterpretationProfileIdentity.Create(Artifact(920).ResourceId, profile),
            countA,
            new([new(0, pageA)]),
            countB,
            new([new(0, pageB)]),
            this);
    }

    private RepeatedEnumerationInterpretationProfile Profile() => new(
        RepeatedEnumerationInterpretationProfile.SchemaId,
        RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso,
        "application/sparql-results+json",
        EnumerationCursorEnvelope.Identity,
        _maximumDeliverableRows,
        "enumeration-row-threshold/1",
        _countFamily,
        _pageFamily,
        CountVariable,
        ["id", "cursor", "value"],
        ["id"],
        ["cursor"],
        ["scope"],
        PassParameter,
        ["cursor"],
        HasCursorParameter,
        RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    private RepeatedEnumerationEvidenceRefs Add(
        int seed,
        string text,
        long count,
        SourceArtifactRef countRef,
        DateTimeOffset time,
        bool countQuery,
        long pass,
        long rowLimit = 10)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var cardinality = countQuery
            ? new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null)
            : new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage, rowLimit, count, countRef);
        var family = countQuery ? _countFamily : _pageFamily;
        var parameters = new List<MachineQueryParameter>
        {
            new("scope", MachineQueryParameterKind.PublisherCursor, null, "all", Artifact(906)),
            new(PassParameter, MachineQueryParameterKind.BoundedInteger, pass, null, Artifact(906)),
        };
        if (!countQuery)
        {
            // The single page of each pass is the first page, so it claims no continuation cursor.
            parameters.Add(new(
                HasCursorParameter, MachineQueryParameterKind.BoundedInteger, 0, null, Artifact(906)));
        }

        var input = MachineQueryInputArtifact.Create(
            Artifact(seed + 100).ResourceId, family, _partitionKey, cardinality, parameters);
        var sourceProfile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);
        var requestTarget = sourceProfile.RequestTarget;
        var target = Encoding.ASCII.GetBytes(new Uri(requestTarget).PathAndQuery);
        var requestBody = Encoding.UTF8.GetBytes("ASK{}");
        var plan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            input.QueryFamilyRef,
            Artifact(907),
            Artifact(908),
            HttpRequestMethod.Post,
            requestTarget,
            target.Length,
            Sha(target),
            cardinality,
            new SourceRegistryMemberRef(Artifact(907), sourceProfile.RequestContentType),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            requestBody.LongLength,
            Sha(requestBody));
        var planRef = MachineQueryPlanIdentity.Create(Artifact(seed + 110).ResourceId, plan);
        var renderer = new Renderer(requestTarget, requestBody);
        var receipt = MachineQueryBinder.BindForSend(plan, planRef, input, renderer).RenderReceipt;
        var receiptRef = MachineQueryRenderReceiptIdentity.Create(Artifact(seed + 120).ResourceId, receipt);
        var digest = Sha(bytes);
        var blob = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, digest, bytes.Length, CustodyClass.NightlyFloor90d);
        var write = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            blob,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                blob,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.NewGuid(),
                CustodyProtection.LockedTime,
                time,
                time.AddDays(91)));
        var logicalRequest = HttpLogicalRequest.Create(
            requestTarget,
            HttpRequestMethod.Post,
            [
                new HttpLogicalRequestHeader("user-agent", sourceProfile.CrawlerUserAgent),
                new HttpLogicalRequestHeader("accept", sourceProfile.Accept),
                new HttpLogicalRequestHeader(
                    "content-type", $"{sourceProfile.RequestContentType}; charset=utf-8"),
            ],
            new HttpLogicalRequestBody(checked((ulong)requestBody.LongLength), Sha(requestBody)),
            Artifact(909).Sha256,
            Artifact(910).Sha256);
        var logicalRequestRef = new SourceArtifactRef(
            Artifact(seed + 150).ResourceId, Sha(logicalRequest.CopyCanonicalBytes()));
        var absent = new RoutedHttpAbsentHeader();
        var headers = new RoutedHttpResponseHeaders(
            new RoutedHttpSingleHeader("application/sparql-results+json"),
            new RoutedHttpSingleHeader(
                bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
        var hop = RoutedHttpHop.Create(
            0UL,
            Artifact(seed + 140).ResourceId,
            null,
            logicalRequestRef.Sha256,
            logicalRequest.Uri,
            200,
            headers,
            Timestamp(time),
            Timestamp(time.AddMilliseconds(1)),
            new DeclaredContentLengthHttpCompletion((ulong)bytes.Length),
            (ulong)bytes.Length,
            digest,
            Sha(Encoding.UTF8.GetBytes(ContractJson.Serialize(write))),
            (ulong)bytes.Length,
            digest);
        var httpEvidence = RoutedHttpEvidence.Create(
            Artifact(_runSeed), (ulong)seed, 0, [hop], new CompleteHttpRouteOutcome());
        var httpEvidenceRef = new SourceArtifactRef(
            Artifact(seed + 160).ResourceId, Sha(httpEvidence.CopyCanonicalBytes()));
        _resolved.Add(
            httpEvidenceRef,
            new(plan, input, receipt, renderer, logicalRequest, httpEvidence, write, bytes));
        return new RepeatedEnumerationEvidenceRefs(
            planRef, input.ArtifactRef, receiptRef, logicalRequestRef, httpEvidenceRef);
    }

    private static string CountJson(long count) =>
        "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true,"
        + "\"bindings\":[{\"count\":{\"type\":\"literal\","
        + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\","
        + $"\"value\":\"{count}\"}}}}]}}}}";

    private static string RowsJson(string values) =>
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + string.Join(',', values.Split(',').Select(static value =>
            $"{{\"id\":{{\"type\":\"uri\",\"value\":\"urn:row:{value}\"}},"
            + $"\"cursor\":{{\"type\":\"literal\",\"value\":\"{value}\"}}}}"))
        + "]}}";

    private static string Timestamp(DateTimeOffset value) =>
        value.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string Sha(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed class Renderer(string requestTarget, byte[] requestBody) : IMachineQueryRenderer
    {
        private readonly byte[] _requestBody = requestBody.ToArray();

        public SourceArtifactRef RendererProfileRef { get; } = Artifact(907);

        public SourceArtifactRef RendererSourceRef { get; } = Artifact(908);

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
            new(requestTarget, _requestBody);
    }
}
