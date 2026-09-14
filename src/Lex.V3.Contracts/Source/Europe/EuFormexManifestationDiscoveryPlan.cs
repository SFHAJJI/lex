using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

public enum EuFormexManifestationQueryPass
{
    Pass1 = 1,
    Pass2 = 2,
}

public sealed record EuFormexManifestationBoundQuery
{
    internal EuFormexManifestationBoundQuery(
        MachineQueryPlan machinePlan,
        SourceArtifactRef machinePlanRef,
        MachineQueryInputArtifact inputArtifact,
        BoundMachineRequest request)
    {
        MachinePlan = machinePlan;
        MachinePlanRef = machinePlanRef;
        InputArtifact = inputArtifact;
        Request = request;
    }

    public MachineQueryPlan MachinePlan { get; }
    public SourceArtifactRef MachinePlanRef { get; }
    public MachineQueryInputArtifact InputArtifact { get; }
    public BoundMachineRequest Request { get; }
}

/// <summary>
/// Enumerates every manifestation type asserted for one proven EU expression identity.
/// Rendering is offline; this plan sends no traffic.
/// </summary>
public sealed class EuFormexManifestationDiscoveryPlan
{
    public const string PartitionKeyPrefix = "eu-formex-manifestations-by-expression-v1-";
    public const string WorkSelectionParameterName = "work_iri";
    public const string ExpressionSelectionParameterName = "expression_iri";

    /// <summary>
    /// The Cellar publisher-lexical manifestation type observed as <c>fmx4</c> on named EU works on
    /// 2026-09-04, matching the separately closed listing vocabulary without sharing its wire concern.
    /// </summary>
    public const string FormexTypeToken = "fmx4";

    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 449;
    internal const uint Pass2PageLimit = 283;
    internal const string PublisherEndpoint = "https://publications.europa.eu/webapi/rdf/sparql";

    private const string ResourceId = "urn:uuid:f73b10ec-2d6e-4b3e-91c9-35c12b7a4d86";
    private const string MemberPrefix = "eu-formex-manifestations-by-expression";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] Projection =
    [
        "work", "expression", "manifestation_type", "manifestation_type_kind",
        "datatype_iri", "language_tag", "multiplicity", "key_1", "key_2", "key_3", "key_4",
    ];
    private static readonly string[] Cursor = ["key_1", "key_2", "key_3", "key_4"];
    private readonly byte[] _canonicalIdentityBytes;

    private EuFormexManifestationDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "eu-formex-manifestations-by-expression-plan/1",
            "endpoint=" + PublisherEndpoint,
            "method=POST",
            "target=/webapi/rdf/sparql",
            "request_media_type=application/sparql-query",
            "response_media_type=" + ResponseMediaType,
            "belongs_to_work=" + EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork),
            "manifests_expression=" + EuObjectFactsDiscoveryPlan.ManifestsExpressionPredicateIri,
            "manifestation_type=" + EuObjectFactsDiscoveryPlan.ManifestationTypePredicateIri,
            "formex_type_token=" + FormexTypeToken,
            "partition_key_prefix=" + PartitionKeyPrefix,
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + (int)EuFormexManifestationQueryPass.Pass1 + ":" + Pass1PageLimit,
            "pass_2=" + (int)EuFormexManifestationQueryPass.Pass2 + ":" + Pass2PageLimit,
            "terminal_page_policy=short_page_terminal",
            "selection_parameters=" + WorkSelectionParameterName + "," + ExpressionSelectionParameterName,
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

    public static EuFormexManifestationDiscoveryPlan Create() => new();
    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();
    internal static int CursorKeyCount => Cursor.Length;

    public static string PartitionKeyFor(LanguageScopedExpressionIdentity identity)
    {
        var (work, expression) = CanonicalizeSelection(identity);
        return PartitionKeyPrefix + Sha256(StrictUtf8.GetBytes(work + "\n" + expression));
    }

    public static (string Work, string Expression) CanonicalizeSelection(
        LanguageScopedExpressionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var work = EuPackRootCanonicalForm.TryCanonicalize(identity.PublisherWorkId, out _);
        if (work is null || !string.Equals(work, identity.PublisherWorkId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The work must use Appendix A's exact Cellar spelling.", nameof(identity));
        }

        var expression = identity.PublisherExpressionId;
        if (!expression.StartsWith(work + ".", StringComparison.Ordinal) ||
            expression.Length == work.Length + 1 ||
            expression[(work.Length + 1)..].Any(static value => value is < '0' or > '9') ||
            !Uri.TryCreate(expression, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.AbsoluteUri, expression, StringComparison.Ordinal) ||
            expression.Any(static value => value <= ' ' || value is '<' or '>' or '"' or '{' or '}' or '|' or '^' or '`' or '\\'))
        {
            throw new ArgumentException(
                "The expression must be the exact numeric child Cellar IRI of its work.", nameof(identity));
        }

        return (work, expression);
    }

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
        [WorkSelectionParameterName, ExpressionSelectionParameterName],
        "pass_id",
        Cursor.Select(static value => "last_" + value).ToArray(),
        "has_cursor",
        RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    public EuFormexManifestationBoundQuery BindCount(
        LanguageScopedExpressionIdentity identity,
        EuFormexManifestationQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, identity, pass, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public EuFormexManifestationBoundQuery BindPage(
        LanguageScopedExpressionIdentity identity,
        EuFormexManifestationQueryPass pass,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, identity, pass, cursor,
            new MachineResponseCardinality(MachineResponseCardinalityKind.BoundedRowSetPage,
                PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private EuFormexManifestationBoundQuery Bind(
        bool isPage,
        LanguageScopedExpressionIdentity identity,
        EuFormexManifestationQueryPass pass,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);
        var (work, expression) = CanonicalizeSelection(identity);
        var key = PartitionKeyFor(identity);
        var parameters = new List<MachineQueryParameter>
        {
            new(WorkSelectionParameterName, MachineQueryParameterKind.PublisherLiteral, null, work, ArtifactRef),
            new(ExpressionSelectionParameterName, MachineQueryParameterKind.PublisherLiteral, null, expression, ArtifactRef),
            new("pass_id", MachineQueryParameterKind.BoundedInteger, (int)pass, null, ArtifactRef),
        };
        if (isPage)
        {
            var values = cursor?.ToArray() ?? [];
            if (values.Length != 0 && values.Length != Cursor.Length)
            {
                throw new ArgumentException($"A continuation cursor must have {Cursor.Length} exact parts.", nameof(cursor));
            }
            parameters.Add(new("has_cursor", MachineQueryParameterKind.BoundedInteger,
                values.Length == 0 ? 0 : 1, null, ArtifactRef));
            for (var index = 0; index < values.Length; index++)
            {
                parameters.Add(new("last_" + Cursor[index], MachineQueryParameterKind.PublisherCursor,
                    null, EnumerationCursorEnvelope.Encode(values[index]), ArtifactRef));
            }
        }
        else if (cursor is not null)
        {
            throw new ArgumentException("A count query cannot carry a cursor.", nameof(cursor));
        }

        var family = isPage ? PageQueryFamilyRef : CountQueryFamilyRef;
        var input = MachineQueryInputArtifact.Create(inputResourceId, family, key, response, parameters);
        var renderer = new EuFormexManifestationSparqlRenderer(this, isPage, rendererSource);
        var body = renderer.RenderInput(input, response).CopyRequestBody();
        var target = Encoding.ASCII.GetBytes("/webapi/rdf/sparql");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId, family, ArtifactRef, rendererSource.Reference,
            HttpRequestMethod.Post, PublisherEndpoint, target.LongLength, Sha256(target), response,
            new SourceRegistryMemberRef(ArtifactRef, "application/sparql-query"),
            MachineQueryCharset.Utf8, MachineQueryInputMode.RendererInputs,
            input.ArtifactRef, input.PartitionBinding, body.LongLength, Sha256(body));
        var planRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        return new(machinePlan, planRef, input,
            MachineQueryBinder.BindForSend(machinePlan, planRef, input, renderer));
    }

    internal static uint PageLimit(EuFormexManifestationQueryPass pass) => pass switch
    {
        EuFormexManifestationQueryPass.Pass1 => Pass1PageLimit,
        EuFormexManifestationQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    private static (string Count, string Page) BuildTemplates()
    {
        var pattern = $$"""
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES (?work ?expression) { ({work_iri:iri} {expression_iri:iri}) }
              ?expression <{{EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork)}}> ?work .
              ?manifestation <{{EuObjectFactsDiscoveryPlan.ManifestsExpressionPredicateIri}}> ?expression .
              ?manifestation <{{EuObjectFactsDiscoveryPlan.ManifestationTypePredicateIri}}> ?manifestation_type .
            """;
        var rows = $$"""
            SELECT ?work ?expression ?manifestation_type ?manifestation_type_kind ?datatype_iri ?language_tag (COUNT(*) AS ?multiplicity) WHERE {
            {{Indent(pattern)}}
              BIND(IF(isIRI(?manifestation_type), "iri", IF(isLiteral(?manifestation_type), "literal", "unsupported")) AS ?manifestation_type_kind)
              BIND(COALESCE(STR(DATATYPE(?manifestation_type)), "") AS ?datatype_iri)
              BIND(COALESCE(LANG(?manifestation_type), "") AS ?language_tag)
            }
            GROUP BY ?work ?expression ?manifestation_type ?manifestation_type_kind ?datatype_iri ?language_tag
            """;
        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;
        var page = $$"""
            SELECT ?work ?expression ?manifestation_type ?manifestation_type_kind ?datatype_iri ?language_tag ?multiplicity ?key_1 ?key_2 ?key_3 ?key_4 WHERE {
              {
            {{Indent(Indent(rows))}}
              }
              BIND(?manifestation_type_kind AS ?key_1)
              BIND(COALESCE(STR(?manifestation_type), "") AS ?key_2)
              BIND(?datatype_iri AS ?key_3)
              BIND(?language_tag AS ?key_4)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string})
              }
              FILTER(?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4))
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

internal sealed class EuFormexManifestationSparqlRenderer : IMachineQueryRenderer
{
    private readonly EuFormexManifestationDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal EuFormexManifestationSparqlRenderer(
        EuFormexManifestationDiscoveryPlan plan,
        bool isPage,
        MachineQueryRendererSource rendererSource)
    {
        _plan = plan;
        _isPage = isPage;
        _rendererSource = rendererSource;
    }

    public SourceArtifactRef RendererProfileRef => _plan.ArtifactRef;
    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;
    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _plan.CopyCanonicalIdentityBytes();
    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();
    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact input) =>
        RenderInput(input, plan.ResponseCardinality);

    internal MachineQueryRenderOutput RenderInput(
        MachineQueryInputArtifact input,
        MachineResponseCardinality response)
    {
        var parameters = input.OrderedParameters.ToDictionary(static value => value.Name, StringComparer.Ordinal);
        var work = Literal(parameters, EuFormexManifestationDiscoveryPlan.WorkSelectionParameterName);
        var expression = Literal(parameters, EuFormexManifestationDiscoveryPlan.ExpressionSelectionParameterName);
        var identity = new LanguageScopedExpressionIdentity(work, expression);
        var selection = EuFormexManifestationDiscoveryPlan.CanonicalizeSelection(identity);
        if (!string.Equals(input.PartitionBinding.MemberKey,
                EuFormexManifestationDiscoveryPlan.PartitionKeyFor(identity), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The partition key must name the exact work/expression selection.", nameof(input));
        }

        var pass = (EuFormexManifestationQueryPass)Integer(parameters, "pass_id");
        var limit = EuFormexManifestationDiscoveryPlan.PageLimit(pass);
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{work_iri:iri}", "<" + selection.Work + ">");
        query = Replace(query, "{expression_iri:iri}", "<" + selection.Expression + ">");

        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody || parameters.Count != 3)
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
        if (hasCursor is not (0 or 1) || parameters.Count != 4 +
            (hasCursor == 1 ? EuFormexManifestationDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }
        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= EuFormexManifestationDiscoveryPlan.CursorKeyCount; ordinal++)
        {
            var name = "last_key_" + ordinal;
            var value = hasCursor == 0 ? string.Empty : Cursor(parameters, name);
            query = Replace(query, "{" + name + ":sparql_string}", SparqlQueryText.StringLiteral(value));
        }
        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        EuFormexManifestationDiscoveryPlan.PublisherEndpoint, Encoding.UTF8.GetBytes(query));
    private static long Integer(IReadOnlyDictionary<string, MachineQueryParameter> values, string name) =>
        values.TryGetValue(name, out var value) && value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is not null
            ? value.IntegerValue.Value : throw new ArgumentException($"The integer input {name} is missing or invalid.");
    private static string Literal(IReadOnlyDictionary<string, MachineQueryParameter> values, string name) =>
        values.TryGetValue(name, out var value) && value.Kind == MachineQueryParameterKind.PublisherLiteral && value.TextValue is not null
            ? value.TextValue : throw new ArgumentException($"The literal input {name} is missing or invalid.");
    private static string Cursor(IReadOnlyDictionary<string, MachineQueryParameter> values, string name) =>
        values.TryGetValue(name, out var value) && value.Kind == MachineQueryParameterKind.PublisherCursor && value.TextValue is not null
            ? EnumerationCursorEnvelope.Decode(value.TextValue) : throw new ArgumentException($"The cursor input {name} is missing or invalid.");
    private static string Replace(string source, string slot, string replacement)
    {
        if (source.Split(slot, StringSplitOptions.None).Length != 2)
        {
            throw new ArgumentException("A renderer slot must occur exactly once.", nameof(source));
        }
        return source.Replace(slot, replacement, StringComparison.Ordinal);
    }
}
