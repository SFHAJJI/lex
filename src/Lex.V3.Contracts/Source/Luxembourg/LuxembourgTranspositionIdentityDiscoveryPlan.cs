using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public sealed record LuxembourgTranspositionIdentityBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Enumerates the exact Legilux transposition target, its EU ELI identity link and its local
/// directive classification without rewriting the original publisher relation.
/// </summary>
public sealed class LuxembourgTranspositionIdentityDiscoveryPlan
{
    public const string TransposesPredicateIri =
        "http://data.legilux.public.lu/resource/ontology/jolux#transposes";
    public const string SameAsPredicateIri = "http://www.w3.org/2002/07/owl#sameAs";
    public const string EuDirectiveClassIri =
        "http://data.legilux.public.lu/resource/ontology/jolux#EUDirective";
    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 997;
    internal const uint Pass2PageLimit = 613;
    // A continued page carries pass_id, this fixed selection, has_cursor and four cursor terms.
    // The shared machine-input contract permits 64 parameters, leaving 58 exact ELI slots.
    public const int BatchCapacity = 58;
    internal const string PartitionMemberKey = "legilux-transposition-target-identities";

    private const string ResourceId = "urn:uuid:96ce0cf2-43e8-49af-9040-c71f46c38caf";
    private const string MemberPrefix = "lu-transposition-target-identities";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] Projection =
    [
        "measure", "local_eu_work", "eu_eli", "eu_work_kind", "multiplicity",
        "key_1", "key_2", "key_3", "key_4",
    ];
    private static readonly string[] Cursor = ["key_1", "key_2", "key_3", "key_4"];
    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgTranspositionIdentityDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "lu-transposition-target-identity-plan/1",
            "endpoint=" + LuxembourgQueryPlan.PublisherEndpoint,
            "method=POST",
            "request_media_type=application/x-www-form-urlencoded",
            "response_media_type=" + ResponseMediaType,
            "transposes=" + TransposesPredicateIri,
            "identity=" + SameAsPredicateIri,
            "work_kind=" + EuDirectiveClassIri,
            "batch_capacity=" + BatchCapacity.ToString(CultureInfo.InvariantCulture),
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + (int)LuxembourgQueryPass.Pass1 + ":" + Pass1PageLimit,
            "pass_2=" + (int)LuxembourgQueryPass.Pass2 + ":" + Pass2PageLimit,
            "terminal_page_policy=short_page_terminal",
            "projection=" + string.Join(',', Projection),
            "canonical_keys=" + string.Join(',', Cursor),
            "cursor=" + string.Join(',', Cursor),
            "selection_parameters=" + string.Join(',', BatchParameterNames()),
            "count_member=" + MemberPrefix + ".count",
            "page_member=" + MemberPrefix + ".page",
            CountTemplate,
            PageTemplate,
        }));
        ArtifactRef = new SourceArtifactRef(ResourceId, Sha256(_canonicalIdentityBytes));
        CountQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".count");
        PageQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".page");
    }

    public string PublisherEndpoint => LuxembourgQueryPlan.PublisherEndpoint;
    public SourceArtifactRef ArtifactRef { get; }
    public SourceRegistryMemberRef CountQueryFamilyRef { get; }
    public SourceRegistryMemberRef PageQueryFamilyRef { get; }
    public string CountTemplate { get; }
    public string PageTemplate { get; }

    public static LuxembourgTranspositionIdentityDiscoveryPlan Create() => new();

    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    public RepeatedEnumerationInterpretationProfile CreateDeliveryProfile() => new(
        RepeatedEnumerationInterpretationProfile.SchemaId,
        RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso,
        ResponseMediaType,
        EnumerationCursorEnvelope.Identity,
        PublisherDeliveryCeilingRows,
        ThresholdDetectorIdentity,
        CountQueryFamilyRef,
        PageQueryFamilyRef,
        "count",
        Projection,
        Cursor,
        Cursor,
        BatchParameterNames(),
        "pass_id",
        Cursor.Select(static value => "last_" + value).ToArray(),
        "has_cursor",
        RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    public LuxembourgTranspositionIdentityBoundQuery BindCount(
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchEuElis,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, batchEuElis, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public LuxembourgTranspositionIdentityBoundQuery BindPage(
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchEuElis,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, pass, batchEuElis, cursor,
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private LuxembourgTranspositionIdentityBoundQuery Bind(
        bool isPage,
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchEuElis,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);
        var padded = PadBatch(CanonicalizeSelection(batchEuElis));
        var parameters = new List<MachineQueryParameter>();
        var names = BatchParameterNames();
        for (var index = 0; index < BatchCapacity; index++)
        {
            parameters.Add(new MachineQueryParameter(
                names[index], MachineQueryParameterKind.PublisherLiteral,
                null, padded[index], ArtifactRef));
        }
        parameters.Add(new MachineQueryParameter(
            "pass_id", MachineQueryParameterKind.BoundedInteger, (int)pass, null, ArtifactRef));
        if (isPage)
        {
            var values = cursor?.ToArray() ?? [];
            if (values.Length != 0 && values.Length != Cursor.Length)
            {
                throw new ArgumentException("A continuation cursor must have four exact parts.", nameof(cursor));
            }
            parameters.Add(new MachineQueryParameter(
                "has_cursor", MachineQueryParameterKind.BoundedInteger,
                values.Length == 0 ? 0 : 1, null, ArtifactRef));
            for (var index = 0; index < values.Length; index++)
            {
                parameters.Add(new MachineQueryParameter(
                    "last_" + Cursor[index], MachineQueryParameterKind.PublisherCursor,
                    null, EnumerationCursorEnvelope.Encode(values[index]), ArtifactRef));
            }
        }
        else if (cursor is not null)
        {
            throw new ArgumentException("A count query cannot carry a cursor.", nameof(cursor));
        }

        var family = isPage ? PageQueryFamilyRef : CountQueryFamilyRef;
        var input = MachineQueryInputArtifact.Create(
            inputResourceId, family, PartitionMemberKey, response, parameters);
        var renderer = new LuxembourgTranspositionIdentitySparqlRenderer(this, isPage, rendererSource);
        var rendered = renderer.RenderInput(input, response);
        var body = rendered.CopyRequestBody();
        var targetBytes = Encoding.ASCII.GetBytes("/sparqlendpoint");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            family,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Post,
            LuxembourgQueryPlan.PublisherEndpoint,
            targetBytes.LongLength,
            Sha256(targetBytes),
            response,
            new SourceRegistryMemberRef(ArtifactRef, "application/x-www-form-urlencoded"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));
        var machinePlanRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        return new(machinePlan, machinePlanRef, input, MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer));
    }

    private static uint PageLimit(LuxembourgQueryPass pass) => pass switch
    {
        LuxembourgQueryPass.Pass1 => Pass1PageLimit,
        LuxembourgQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    internal static IReadOnlyList<string> BatchParameterNames()
    {
        var names = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            names[index] = "batch_eu_eli_" + index.ToString("D3", CultureInfo.InvariantCulture);
        }
        return Array.AsReadOnly(names);
    }

    public static IReadOnlyList<string> CanonicalizeSelection(IReadOnlyList<string> batchEuElis)
    {
        ArgumentNullException.ThrowIfNull(batchEuElis);
        if (batchEuElis.Count is 0 or > BatchCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchEuElis), $"A batch must name one to {BatchCapacity} EU directive ELIs.");
        }

        var values = batchEuElis.ToArray();
        foreach (var value in values)
        {
            if (OfficialIdentifier.EliMintedBy(value) != PublisherId.EuEurLex ||
                !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !IsAdmittedDirectiveEliPath(uri.AbsolutePath))
            {
                throw new ArgumentException(
                    $"Batch member '{value}' is not an exact EU directive ELI.", nameof(batchEuElis));
            }
        }

        Array.Sort(values, StringComparer.Ordinal);
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException("A batch cannot repeat an EU ELI.", nameof(batchEuElis));
        }
        return Array.AsReadOnly(values);
    }

    private static bool IsAdmittedDirectiveEliPath(string absolutePath) =>
        absolutePath.StartsWith("/eli/dir/", StringComparison.Ordinal) ||
        absolutePath.StartsWith("/eli/dir_del/", StringComparison.Ordinal) ||
        absolutePath.StartsWith("/eli/dir_impl/", StringComparison.Ordinal);

    internal static string[] PadBatch(IReadOnlyList<string> canonicalSortedBatch)
    {
        var padded = new string[BatchCapacity];
        var last = canonicalSortedBatch[^1];
        for (var index = 0; index < BatchCapacity; index++)
        {
            padded[index] = index < canonicalSortedBatch.Count ? canonicalSortedBatch[index] : last;
        }
        return padded;
    }

    private static (string Count, string Page) BuildTemplates()
    {
        var valuesBlock = string.Join('\n', BatchParameterNames()
            .Select(static name => "    {" + name + ":iri}"));
        var graphPattern = $$"""
              VALUES ?lex_pass_id { {pass_id:uint} }
              {
                SELECT DISTINCT ?eu_eli WHERE {
                  VALUES ?eu_eli {
            {{valuesBlock}}
                  }
                }
              }
              ?measure <{{TransposesPredicateIri}}> ?local_eu_work .
              ?local_eu_work <{{SameAsPredicateIri}}> ?eu_eli .
              OPTIONAL {
                ?local_eu_work a <{{EuDirectiveClassIri}}> .
                BIND(<{{EuDirectiveClassIri}}> AS ?eu_work_kind)
              }
            """;
        var rows = $$"""
            SELECT ?measure ?local_eu_work ?eu_eli ?eu_work_kind (COUNT(*) AS ?multiplicity) WHERE {
            {{Indent(graphPattern)}}
            }
            GROUP BY ?measure ?local_eu_work ?eu_eli ?eu_work_kind
            """;
        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;
        var pageRows = $$"""
            SELECT ?measure ?local_eu_work ?eu_eli ?eu_work_kind (COUNT(*) AS ?multiplicity) ?key_1 ?key_2 ?key_3 ?key_4 WHERE {
            {{Indent(graphPattern)}}
              BIND(STR(?measure) AS ?key_1)
              BIND(STR(?local_eu_work) AS ?key_2)
              BIND(COALESCE(STR(?eu_eli), "") AS ?key_3)
              BIND(COALESCE(STR(?eu_work_kind), "") AS ?key_4)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4)
              )
              FILTER(?has_cursor = 0 || !(
                ?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4))
            }
            GROUP BY ?measure ?local_eu_work ?eu_eli ?eu_work_kind ?key_1 ?key_2 ?key_3 ?key_4
            """;
        var page = $$"""
            SELECT ?measure ?local_eu_work ?eu_eli ?eu_work_kind ?multiplicity ?key_1 ?key_2 ?key_3 ?key_4 WHERE {
              {
            {{Indent(Indent(pageRows))}}
              }
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4
            LIMIT {page_limit:uint}
            """;
        return (Normalize(count), Normalize(page));
    }

    private static string Indent(string value) => string.Join('\n',
        Normalize(value).Split('\n').Select(static line => "  " + line));
    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";
    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));
}

internal sealed class LuxembourgTranspositionIdentitySparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgTranspositionIdentityDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgTranspositionIdentitySparqlRenderer(
        LuxembourgTranspositionIdentityDiscoveryPlan plan,
        bool isPage,
        MachineQueryRendererSource rendererSource)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _isPage = isPage;
        _rendererSource = rendererSource ?? throw new ArgumentNullException(nameof(rendererSource));
    }

    public SourceArtifactRef RendererProfileRef => _plan.ArtifactRef;
    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;
    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _plan.CopyCanonicalIdentityBytes();
    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();
    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet, plan.ResponseCardinality);

    internal MachineQueryRenderOutput RenderInput(
        MachineQueryInputArtifact input,
        MachineResponseCardinality response)
    {
        var parameters = input.OrderedParameters.ToDictionary(static value => value.Name, StringComparer.Ordinal);
        var pass = (LuxembourgQueryPass)Integer(parameters, "pass_id");
        var limit = pass switch
        {
            LuxembourgQueryPass.Pass1 => LuxembourgTranspositionIdentityDiscoveryPlan.Pass1PageLimit,
            LuxembourgQueryPass.Pass2 => LuxembourgTranspositionIdentityDiscoveryPlan.Pass2PageLimit,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));
        foreach (var name in LuxembourgTranspositionIdentityDiscoveryPlan.BatchParameterNames())
        {
            query = Replace(query, "{" + name + ":iri}", SparqlIriTerm(Literal(parameters, name)));
        }
        var batchCount = LuxembourgTranspositionIdentityDiscoveryPlan.BatchCapacity;
        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody ||
                parameters.Count != 1 + batchCount)
            {
                throw new ArgumentException("A count input has one exact shape.", nameof(input));
            }
            return Output(query);
        }

        if (response.Kind != MachineResponseCardinalityKind.BoundedRowSetPage || response.RowLimit != limit)
        {
            throw new ArgumentException("The page limit must come from the pass policy.", nameof(response));
        }
        var hasCursor = Integer(parameters, "has_cursor");
        if (hasCursor is not (0 or 1))
        {
            throw new ArgumentException("Cursor presence must be zero or one.", nameof(input));
        }
        if (parameters.Count != 2 + batchCount + (hasCursor == 1 ? 4 : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }
        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= 4; ordinal++)
        {
            var name = "last_key_" + ordinal;
            var value = hasCursor == 0 ? string.Empty : Cursor(parameters, name);
            query = Replace(query, "{" + name + ":sparql_string}", LuxembourgQueryText.SparqlString(value));
        }
        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        LuxembourgQueryPlan.PublisherEndpoint,
        Encoding.UTF8.GetBytes("query=" + Uri.EscapeDataString(query)));
    private static long Integer(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.");
    private static string Literal(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherLiteral && value.TextValue is not null
            ? value.TextValue
            : throw new ArgumentException($"The literal input {name} is missing or invalid.");
    private static string SparqlIriTerm(string canonicalIri)
    {
        if (string.IsNullOrEmpty(canonicalIri) ||
            canonicalIri.AsSpan().IndexOfAny('<', '>') >= 0 ||
            canonicalIri.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A batch member is not a safe SPARQL IRI term.");
        }
        return "<" + canonicalIri + ">";
    }
    private static string Cursor(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherCursor && value.TextValue is not null
            ? EnumerationCursorEnvelope.Decode(value.TextValue)
            : throw new ArgumentException($"The cursor input {name} is missing or invalid.");
    private static string Replace(string source, string slot, string replacement)
    {
        if (source.Split(slot, StringSplitOptions.None).Length != 2)
        {
            throw new ArgumentException("A renderer slot must occur exactly once.", nameof(source));
        }
        return source.Replace(slot, replacement, StringComparison.Ordinal);
    }
}
