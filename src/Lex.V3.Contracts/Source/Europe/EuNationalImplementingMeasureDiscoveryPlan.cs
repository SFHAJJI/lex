using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

public enum EuNationalImplementingMeasureQueryPass
{
    Pass1 = 1,
    Pass2 = 2,
}

public sealed record EuNationalImplementingMeasureBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// The bounded Cellar family for Luxembourg sector-7 national implementing measures. The country,
/// work class and CELEX sector are fixed by this plan rather than caller inputs, so this family
/// cannot be widened into another Member State or another Cellar sector at runtime.
/// </summary>
public sealed class EuNationalImplementingMeasureDiscoveryPlan
{
    internal const string PublisherEndpoint = "https://publications.europa.eu/webapi/rdf/sparql";
    internal const string Cdm = EuConsolidationDiscoveryPlan.Cdm;
    public const string LuxembourgCountryIri =
        "http://publications.europa.eu/resource/authority/country/LUX";
    public const string ImplementsResourceLegalPredicateIri =
        Cdm + "measure_national_implementing_implements_resource_legal";
    public const string LegacyImplementsDirectivePredicateIri =
        Cdm + "measure_national_implementing_implements_directive";
    public const string EliPredicateIri = Cdm + "eli";
    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 997;
    internal const uint Pass2PageLimit = 613;
    internal const string PartitionMemberKey = "luxembourg-sector-7-national-implementing-measures";

    private const string ResourceId = "urn:uuid:28559425-718d-4b50-b5b3-c49d958e1f94";
    private const string MemberPrefix = "eu-luxembourg-sector-7-national-implementing-measures";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] Projection =
    [
        "nim", "country", "nim_celex", "implements_predicate", "eu_work",
        "eli", "eli_kind", "multiplicity", "key_1", "key_2", "key_3", "key_4", "key_5",
    ];
    private static readonly string[] Cursor = ["key_1", "key_2", "key_3", "key_4", "key_5"];
    private readonly byte[] _canonicalIdentityBytes;

    private EuNationalImplementingMeasureDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "eu-luxembourg-sector-7-national-implementing-measure-plan/1",
            "endpoint=" + PublisherEndpoint,
            "method=POST",
            "target=/webapi/rdf/sparql",
            "request_media_type=application/sparql-query",
            "response_media_type=" + ResponseMediaType,
            "country=" + LuxembourgCountryIri,
            "celex_sector=7",
            "cursor_envelope=" + EnumerationCursorEnvelope.Identity,
            "threshold_detector=" + ThresholdDetectorIdentity,
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + (int)EuNationalImplementingMeasureQueryPass.Pass1 + ":" + Pass1PageLimit,
            "pass_2=" + (int)EuNationalImplementingMeasureQueryPass.Pass2 + ":" + Pass2PageLimit,
            "terminal_page_policy=short_page_terminal",
            "selection_parameters=",
            "pass_parameter=pass_id",
            "cursor_presence_parameter=has_cursor",
            "projection=" + string.Join(',', Projection),
            "canonical_keys=" + string.Join(',', Cursor),
            "cursor=" + string.Join(',', Cursor),
            "count_member=" + MemberPrefix + ".count",
            "page_member=" + MemberPrefix + ".page",
            CountTemplate,
            PageTemplate,
        }));
        ArtifactRef = new SourceArtifactRef(ResourceId, Sha256(_canonicalIdentityBytes));
        CountQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".count");
        PageQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".page");
    }

    public SourceArtifactRef ArtifactRef { get; }
    public SourceRegistryMemberRef CountQueryFamilyRef { get; }
    public SourceRegistryMemberRef PageQueryFamilyRef { get; }
    public string CountTemplate { get; }
    public string PageTemplate { get; }

    public static EuNationalImplementingMeasureDiscoveryPlan Create() => new();

    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    public RepeatedEnumerationInterpretationProfile CreateDeliveryProfile() => new(
        RepeatedEnumerationInterpretationProfile.SchemaId,
        RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso,
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
        [],
        "pass_id",
        Cursor.Select(static value => "last_" + value).ToArray(),
        "has_cursor",
        RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    public EuNationalImplementingMeasureBoundQuery BindCount(
        EuNationalImplementingMeasureQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, null, new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public EuNationalImplementingMeasureBoundQuery BindPage(
        EuNationalImplementingMeasureQueryPass pass,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, pass, cursor, new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private EuNationalImplementingMeasureBoundQuery Bind(
        bool isPage,
        EuNationalImplementingMeasureQueryPass pass,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);
        var parameters = new List<MachineQueryParameter>
        {
            new("pass_id", MachineQueryParameterKind.BoundedInteger, (int)pass, null, ArtifactRef),
        };
        if (isPage)
        {
            var values = cursor?.ToArray() ?? [];
            if (values.Length != 0 && values.Length != Cursor.Length)
            {
                throw new ArgumentException("A continuation cursor must have five exact parts.", nameof(cursor));
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
        var renderer = new EuNationalImplementingMeasureSparqlRenderer(this, isPage, rendererSource);
        var rendered = renderer.RenderInput(input, response);
        var body = rendered.CopyRequestBody();
        var targetBytes = Encoding.ASCII.GetBytes("/webapi/rdf/sparql");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            family,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Post,
            PublisherEndpoint,
            targetBytes.LongLength,
            Sha256(targetBytes),
            response,
            new SourceRegistryMemberRef(ArtifactRef, "application/sparql-query"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));
        var machinePlanRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        var request = MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer);
        return new(machinePlan, machinePlanRef, input, request);
    }

    private static uint PageLimit(EuNationalImplementingMeasureQueryPass pass) => pass switch
    {
        EuNationalImplementingMeasureQueryPass.Pass1 => Pass1PageLimit,
        EuNationalImplementingMeasureQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    private static (string Count, string Page) BuildTemplates()
    {
        var rows = $$"""
            SELECT ?nim ?country ?nim_celex ?implements_predicate ?eu_work ?eli ?eli_kind (COUNT(*) AS ?multiplicity) WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES ?country { <{{LuxembourgCountryIri}}> }
              VALUES ?implements_predicate {
                <{{ImplementsResourceLegalPredicateIri}}>
                <{{LegacyImplementsDirectivePredicateIri}}>
              }
              ?nim a <{{Cdm}}measure_national_implementing> ;
                   <{{Cdm}}measure_national_implementing_implemented_by_country> ?country ;
                   <{{Cdm}}resource_legal_id_celex> ?nim_celex ;
                   ?implements_predicate ?eu_work .
              FILTER(STRSTARTS(STR(?nim_celex), "7"))
              {
                ?nim <{{EliPredicateIri}}> ?eli .
                BIND(IF(isIRI(?eli), "iri", IF(isLiteral(?eli), "literal", "unsupported_blank_node")) AS ?eli_kind)
              }
              UNION
              {
                FILTER NOT EXISTS { ?nim <{{EliPredicateIri}}> ?missing_eli }
                BIND("unbound" AS ?eli_kind)
              }
            }
            GROUP BY ?nim ?country ?nim_celex ?implements_predicate ?eu_work ?eli ?eli_kind
            """;
        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;
        var page = $$"""
            SELECT ?nim ?country ?nim_celex ?implements_predicate ?eu_work ?eli ?eli_kind ?multiplicity ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 WHERE {
              {
            {{Indent(Indent(rows))}}
              }
              BIND(STR(?nim) AS ?key_1)
              BIND(STR(?nim_celex) AS ?key_2)
              BIND(STR(?implements_predicate) AS ?key_3)
              BIND(STR(?eu_work) AS ?key_4)
              BIND(COALESCE(STR(?eli), "") AS ?key_5)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4 ?last_key_5) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string} {last_key_5:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 > ?last_key_5)
              )
              FILTER(?has_cursor = 0 || !(
                ?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 = ?last_key_5))
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5
            LIMIT {page_limit:uint}
            """;
        return (Normalize(count), Normalize(page));
    }

    private static string Indent(string value) => string.Join('\n',
        Normalize(value).Split('\n').Select(static line => "  " + line));
    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";
    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

internal sealed class EuNationalImplementingMeasureSparqlRenderer : IMachineQueryRenderer
{
    private readonly EuNationalImplementingMeasureDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal EuNationalImplementingMeasureSparqlRenderer(
        EuNationalImplementingMeasureDiscoveryPlan plan,
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
        var pass = (EuNationalImplementingMeasureQueryPass)Integer(parameters, "pass_id");
        var limit = pass switch
        {
            EuNationalImplementingMeasureQueryPass.Pass1 => EuNationalImplementingMeasureDiscoveryPlan.Pass1PageLimit,
            EuNationalImplementingMeasureQueryPass.Pass2 => EuNationalImplementingMeasureDiscoveryPlan.Pass2PageLimit,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));
        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody || parameters.Count != 1)
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
        if (parameters.Count != 2 + (hasCursor == 1 ? 5 : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }
        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= 5; ordinal++)
        {
            var name = "last_key_" + ordinal;
            var value = hasCursor == 0 ? string.Empty : Cursor(parameters, name);
            query = Replace(query, "{" + name + ":sparql_string}", SparqlQueryText.StringLiteral(value));
        }
        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        EuNationalImplementingMeasureDiscoveryPlan.PublisherEndpoint, Encoding.UTF8.GetBytes(query));
    private static long Integer(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.");
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
