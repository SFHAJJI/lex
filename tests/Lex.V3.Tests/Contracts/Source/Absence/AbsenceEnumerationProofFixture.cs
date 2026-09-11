using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

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

    /// <summary>
    /// The profile this delivery is read under, or null for this fixture's own generic one.
    /// </summary>
    /// <remarks>
    /// A door may bind a proof to its family's exact interpretation profile - the Luxembourg
    /// inventory citation does - and a proof read under this fixture's generic EU profile then
    /// evidences nothing about that family. So a caller can supply the real profile and the row
    /// shape that profile projects.
    /// </remarks>
    private readonly RepeatedEnumerationInterpretationProfile? _suppliedProfile;

    private readonly OfficialMachineQuerySourceProfileId _sourceProfileId =
        OfficialMachineQuerySourceProfileId.EuropeanUnionSparql;

    private readonly string _passParameter = PassParameter;

    private readonly string _hasCursorParameter = HasCursorParameter;

    private readonly IReadOnlyList<string> _selectionParameters = ["scope"];
    private readonly SourceRegistryMemberRef _countFamily = new(Artifact(905), "count-query");
    private readonly SourceRegistryMemberRef _pageFamily = new(Artifact(905), "page-query");

    private AbsenceEnumerationProofFixture(
        string partitionKey,
        int runSeed,
        long maximumDeliverableRows,
        RepeatedEnumerationInterpretationProfile? suppliedProfile,
        OfficialMachineQuerySourceProfileId sourceProfileId,
        string passParameter,
        string hasCursorParameter,
        IReadOnlyList<string> selectionParameters,
        SourceRegistryMemberRef? countFamily,
        SourceRegistryMemberRef? pageFamily)
        : this(partitionKey, runSeed, maximumDeliverableRows)
    {
        _suppliedProfile = suppliedProfile;
        _sourceProfileId = sourceProfileId;
        _passParameter = passParameter;
        _hasCursorParameter = hasCursorParameter;
        _selectionParameters = selectionParameters;
        if (countFamily is not null)
        {
            _countFamily = countFamily;
        }

        if (pageFamily is not null)
        {
            _pageFamily = pageFamily;
        }
    }

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
    /// The same real comparison over a caller-named row set rather than the default two rows.
    /// </summary>
    /// <remarks>
    /// Added because a citation may no longer be minted beside just any honest proof: the door now
    /// re-derives the delivered rows' canonical-key digest and requires it to equal the proof's, so a
    /// test needs a proof over ITS OWN rows rather than a shared one. The row values become the
    /// canonical keys (<c>urn:row:{value}</c>), which is what a test row must carry to be the
    /// delivery this proof proves. Still the whole real tuple - two counts, two page sets at
    /// different limits, custody-bound bytes - because a stub here would let the binding pass
    /// against a proof that proves nothing, which is the defect wearing a fixture's clothes.
    /// </remarks>
    public static EnumerationDeliveryComparison DeliveryOf(
        string partitionKey,
        int runSeed,
        string rowValues,
        bool rawKeys = false)
    {
        // Both bounds have to clear the row set, and the two page limits stay DIFFERENT so the two
        // passes are still paginated differently - which is the property the comparison exists to
        // test. The default path keeps its original 10 and 7.
        var rowCount = rowValues.Length is 0 ? 0 : rowValues.Split(',').Length;
        return new AbsenceEnumerationProofFixture(partitionKey, runSeed, (rowCount * 2) + 100)
            .Build(rowValues, rowValues, rowCount + 3, rowCount + 1, rawKeys);
    }

    /// <summary>The canonical key one row value carries in <see cref="DeliveryOf"/>.</summary>
    public static string CanonicalKeyFor(string rowValue) => "urn:row:" + rowValue;

    /// <summary>
    /// A real comparison over the Luxembourg initial-draft inventory's own profile and publisher.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inventory citation door binds a proof to that family's exact interpretation profile, so a
    /// proof read under this fixture's generic EU profile evidences nothing there - which is exactly
    /// what a reviewer demonstrated. This proves under the plan's own profile: its dialect, its
    /// five-variable projection, its two-part key of <c>key_1</c> and <c>key_2</c>, its own count and
    /// page query families, and the Luxembourg SPARQL source profile.
    /// </para>
    /// <para>
    /// The rows are the family's own shape, so their canonical key is <c>[key_1, key_2]</c> and
    /// <c>key_1</c> is the subject - which is what the door derives the proven population from.
    /// </para>
    /// </remarks>
    /// <param name="partitionKey">
    /// Which family this delivery claims. Defaults to the inventory's own; a caller naming another
    /// gets a delivery read under THIS profile that belongs to a different family, which is how the
    /// citation door's family check is exercised independently of its profile check.
    /// </param>
    public static EnumerationDeliveryComparison LuxembourgInventoryDelivery(
        IReadOnlyList<string> subjects,
        int runSeed = 930,
        string? partitionKey = null)
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();
        var page = LuxembourgInventoryRowsJson(subjects);
        var fixture = new AbsenceEnumerationProofFixture(
            partitionKey ?? LuxembourgInitialDraftInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            runSeed,
            (subjects.Count * 2) + 100,
            profile,
            OfficialMachineQuerySourceProfileId.LuxembourgSparql,
            "pass_id",
            "has_cursor",
            [],
            plan.CountQueryFamilyRef,
            plan.PageQueryFamilyRef);
        return fixture.BuildPages(page, page, subjects.Count, subjects.Count + 3, subjects.Count + 1);
    }

    /// <summary>
    /// The OpinionRequest inventory's own delivery, read under its own profile.
    /// </summary>
    /// <remarks>
    /// A separate fixture rather than a parameter on the draft one, because the citation door binds
    /// a proof to this family's partition AND its interpretation profile: a delivery built from the
    /// other family's plan evidences nothing here, however similar the rows look.
    /// </remarks>
    /// <param name="nonAddressableAt">
    /// Deliver this member with the blank-node kind marker. The citation door refuses such a
    /// delivery, and that path is unreachable without a fixture that genuinely delivers one:
    /// rewriting a canonical key afterwards changes the key digest, so the proof binding refuses
    /// first and the blank-node rule is never reached.
    /// </param>
    public static EnumerationDeliveryComparison LuxembourgOpinionRequestInventoryDelivery(
        IReadOnlyList<string> subjects,
        int runSeed = 931,
        string? partitionKey = null,
        int? nonAddressableAt = null)
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();
        var page = LuxembourgOpinionRequestInventoryRowsJson(subjects, nonAddressableAt);
        var fixture = new AbsenceEnumerationProofFixture(
            partitionKey ?? LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            runSeed,
            (subjects.Count * 2) + 100,
            profile,
            OfficialMachineQuerySourceProfileId.LuxembourgSparql,
            "pass_id",
            "has_cursor",
            [],
            plan.CountQueryFamilyRef,
            plan.PageQueryFamilyRef);
        return fixture.BuildPages(page, page, subjects.Count, subjects.Count + 3, subjects.Count + 1);
    }

    /// <summary>One page of the request inventory's projection, in key order.</summary>
    private static string LuxembourgOpinionRequestInventoryRowsJson(
        IReadOnlyList<string> subjects,
        int? nonAddressableAt = null)
    {
        var bindings = subjects.Select((subject, index) =>
            "{\"request\":{\"type\":\"uri\",\"value\":\"" + subject + "\"},"
            + "\"request_kind\":{\"type\":\"literal\",\"value\":\""
            + KindAt(index, nonAddressableAt) + "\"},"
            + "\"multiplicity\":{\"type\":\"typed-literal\","
            + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
            + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + subject + "\"},"
            + "\"key_2\":{\"type\":\"literal\",\"value\":\""
            + KindAt(index, nonAddressableAt) + "\"}}");

        return "{\"head\":{\"link\":[],\"vars\":"
            + "[\"request\",\"request_kind\",\"multiplicity\",\"key_1\",\"key_2\"]},"
            + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
            + string.Join(',', bindings) + "]}}";
    }

    /// <summary>The kind marker a delivered member carries.</summary>
    internal static string KindAt(int index, int? nonAddressableAt) =>
        index == nonAddressableAt
            ? LuxembourgOpinionRequestInventoryDiscoveryPlan.UnsupportedBlankNodeKind
            : LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    /// <summary>One page of the inventory's own projection, in key order.</summary>
    private static string LuxembourgInventoryRowsJson(IReadOnlyList<string> subjects)
    {
        const string IriKind = LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind;
        var bindings = subjects.Select(subject =>
            "{\"draft\":{\"type\":\"uri\",\"value\":\"" + subject + "\"},"
            + "\"draft_kind\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
            + "\"multiplicity\":{\"type\":\"typed-literal\","
            + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
            + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + subject + "\"},"
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"}}");

        return "{\"head\":{\"link\":[],\"vars\":"
            + "[\"draft\",\"draft_kind\",\"multiplicity\",\"key_1\",\"key_2\"]},"
            + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
            + string.Join(',', bindings) + "]}}";
    }

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

    private EnumerationDeliveryComparison Build(
        string rowsA, string rowsB, long limitA = 10, long limitB = 7, bool rawKeys = false)
    {
        // An empty delivery is a real shape - a batch may ask about drafts the publisher holds
        // nothing for - and Split would otherwise report one empty row for none.
        // An empty delivery is a real shape - a batch may ask about drafts the publisher holds
        // nothing for - and Split would otherwise report one empty row for none.
        var rowCount = rowsA.Length is 0 ? 0 : rowsA.Split(',').Length;
        return BuildPages(RowsJson(rowsA, rawKeys), RowsJson(rowsB, rawKeys), rowCount, limitA, limitB);
    }

    /// <summary>The same real tuple, over page bodies a caller has already rendered.</summary>
    /// <remarks>
    /// Split out because a family's own page shape is not a list of row values: the Luxembourg
    /// inventory projects five variables and keys on two of them. Passing its rendered page through
    /// the row-value path wrapped one JSON document inside another, which the strict parser reported
    /// as malformed rather than as the fixture mistake it was.
    /// </remarks>
    private EnumerationDeliveryComparison BuildPages(
        string pageJsonA,
        string pageJsonB,
        int rowCount,
        long limitA,
        long limitB)
    {
        var countA = Add(1, CountJson(rowCount), rowCount, Artifact(301), DateTimeOffset.UnixEpoch, true, 1);
        var pageA = Add(
            2, pageJsonA, rowCount, countA.HttpEvidenceRef,
            DateTimeOffset.UnixEpoch.AddSeconds(1), false, 1, rowLimit: limitA);
        var countB = Add(
            3, CountJson(rowCount), rowCount, Artifact(303),
            DateTimeOffset.UnixEpoch.AddSeconds(2), true, 2);
        var pageB = Add(
            4, pageJsonB, rowCount, countB.HttpEvidenceRef,
            DateTimeOffset.UnixEpoch.AddSeconds(3), false, 2, rowLimit: limitB);
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

    private RepeatedEnumerationInterpretationProfile Profile() => _suppliedProfile ?? new(
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
        var parameters = new List<MachineQueryParameter>();
        foreach (var selection in _selectionParameters)
        {
            parameters.Add(new(
                selection, MachineQueryParameterKind.PublisherCursor, null, "all", Artifact(906)));
        }

        parameters.Add(new(
            _passParameter, MachineQueryParameterKind.BoundedInteger, pass, null, Artifact(906)));
        if (!countQuery)
        {
            // The single page of each pass is the first page, so it claims no continuation cursor.
            parameters.Add(new(
                _hasCursorParameter, MachineQueryParameterKind.BoundedInteger, 0, null, Artifact(906)));
        }

        var input = MachineQueryInputArtifact.Create(
            Artifact(seed + 100).ResourceId, family, _partitionKey, cardinality, parameters);
        var sourceProfile = OfficialMachineQuerySourceProfiles.Resolve(_sourceProfileId);
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
            new SourceRegistryMemberRef(Artifact(907), sourceProfile.RequestContentType!),
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
                new HttpLogicalRequestHeader("accept", sourceProfile.Accept!),
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
            Artifact(_runSeed),
            (ulong)seed,
            0,
            [hop],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal)
            {
                [hop.ObservationId] = write,
            });
        var httpEvidenceRef = new SourceArtifactRef(
            Artifact(seed + 160).ResourceId, Sha(httpEvidence.CopyCanonicalBytes()));
        _resolved.Add(
            httpEvidenceRef,
            new(plan, input, receipt, renderer, logicalRequest, httpEvidence, write, bytes));
        return new RepeatedEnumerationEvidenceRefs(
            planRef, input.ArtifactRef, receiptRef, logicalRequestRef, httpEvidenceRef);
    }

    private string CountJson(long count) =>
        "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true,"
        + "\"bindings\":[{\"count\":{\"type\":\"" + TypedLiteralWireType + "\","
        + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\","
        + $"\"value\":\"{count}\"}}}}]}}}}";

    /// <summary>
    /// How this delivery's dialect spells a typed literal on the wire.
    /// </summary>
    /// <remarks>
    /// Source/Core requires the Luxembourg Virtuoso dialect to say <c>typed-literal</c> and every
    /// other to say <c>literal</c>, and refuses the wrong one rather than accepting either. A fixture
    /// that guessed would be describing a response no engine sends.
    /// </remarks>
    private string TypedLiteralWireType =>
        Profile().Dialect == RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso
            ? "typed-literal"
            : "literal";

    /// <param name="rawKeys">
    /// When true the row id is the value verbatim rather than <c>urn:row:{value}</c>, so a caller
    /// can make the proved canonical key BE the subject its family keys on. The Luxembourg inventory
    /// keys on <c>STR(?draft)</c>, so its citation door derives the population from the proved key
    /// rather than from the row terms - which a caller can replace independently.
    /// </param>
    private static string RowsJson(string values, bool rawKeys = false) =>
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + string.Join(',', (values.Length is 0 ? [] : values.Split(',')).Select(value =>
            $"{{\"id\":{{\"type\":\"uri\",\"value\":\"{(rawKeys ? value : "urn:row:" + value)}\"}},"
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
