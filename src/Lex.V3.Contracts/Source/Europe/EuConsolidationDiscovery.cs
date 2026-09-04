using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

public enum EuConsolidationQuerySet
{
    Family = 1,
    TemporalFacts = 2,
}

// Widened alongside EuConsolidationDiscoveryPlan (SCOPE_RULING
// lex-event-20260904T040718222Z-7e6f29af07024cf5b2cb716f94f288e3): a caller outside this assembly
// now binds a pass through the plan's own now-public BindCount/BindPage, so the parameter type they
// take can no longer be internal. No logic changed.
public enum EuConsolidationQueryPass
{
    Pass1 = 1,
    Pass2 = 2,
}

internal enum EuConsolidationTemporalPredicate
{
    Date = 1,
    Layer = 2,
    Version = 3,
    Number = 4,
}

internal enum EuConsolidationDateStatus
{
    OneObservedCandidate = 1,
    AmbiguousVersion = 2,
}

internal sealed class EuConsolidationQueryDefinition
{
    internal EuConsolidationQueryDefinition(
        EuConsolidationQuerySet set,
        SourceRegistryMemberRef countQueryFamilyRef,
        SourceRegistryMemberRef pageQueryFamilyRef,
        string countTemplate,
        string pageTemplate,
        IReadOnlyList<string> projectionVariables,
        IReadOnlyList<string> cursorVariables)
    {
        Set = set;
        CountQueryFamilyRef = countQueryFamilyRef;
        PageQueryFamilyRef = pageQueryFamilyRef;
        CountTemplate = countTemplate;
        PageTemplate = pageTemplate;
        ProjectionVariables = Array.AsReadOnly(projectionVariables.ToArray());
        CursorVariables = Array.AsReadOnly(cursorVariables.ToArray());
    }

    internal EuConsolidationQuerySet Set { get; }
    internal SourceRegistryMemberRef CountQueryFamilyRef { get; }
    internal SourceRegistryMemberRef PageQueryFamilyRef { get; }
    internal string CountTemplate { get; }
    internal string PageTemplate { get; }
    internal IReadOnlyList<string> ProjectionVariables { get; }
    internal IReadOnlyList<string> CursorVariables { get; }
}

public sealed record EuConsolidationBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Closed machine-query description for EU consolidation-family discovery and its raw facts.
/// </summary>
/// <remarks>
/// This is an internal, non-authoritative selector-delivery contract. It does not claim publisher
/// completeness, absence, a legal interval, a release use, or permission to serve text.
/// </remarks>
public sealed class EuConsolidationDiscoveryPlan
{
    internal const string PublisherEndpoint =
        "https://publications.europa.eu/webapi/rdf/sparql";
    internal const string Cdm = "http://publications.europa.eu/ontology/cdm#";
    internal const string CelexPredicateIri = Cdm + "resource_legal_id_celex";
    internal const string BasedOnPredicateIri =
        Cdm + "act_consolidated_based_on_resource_legal";
    internal const string ConsolidatedDatePredicateIri = Cdm + "act_consolidated_date";
    internal const string ConsolidatedLayerPredicateIri = Cdm + "act_consolidated_layer";
    internal const string ConsolidatedVersionPredicateIri = Cdm + "act_consolidated_version";
    internal const string ConsolidatedNumberPredicateIri = Cdm + "act_consolidated_number";
    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 997;
    internal const uint Pass2PageLimit = 613;
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";

    private const string ResourceId =
        "urn:uuid:c8d98440-5eb3-4f17-9ce4-6db996530aa9";
    private const string FamilyMemberPrefix = "eu-consolidation-family";
    private const string FactsMemberPrefix = "eu-consolidation-temporal-facts";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IReadOnlyDictionary<EuConsolidationQuerySet, EuConsolidationQueryDefinition>
        _definitions;

    /// <summary>
    /// The exact bytes <see cref="ArtifactRef"/> names. Held rather than recomputed: the digest
    /// is minted from this array in the constructor, so nothing downstream can hand the binder a
    /// second rendering of the same identity that happens to differ.
    /// </summary>
    private readonly byte[] _canonicalIdentityBytes;

    private EuConsolidationDiscoveryPlan()
    {
        var templates = BuildTemplates();
        var familyProjection = new[]
        {
            "base_celex", "base", "state", "family_multiplicity", "state_key",
        };
        var factProjection = new[]
        {
            "base_celex", "base", "state", "predicate", "object", "object_kind",
            "datatype_iri", "language_tag", "multiplicity",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
        };
        var familyCursor = new[] { "state_key" };
        var factsCursor = new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" };
        var identityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "eu-consolidation-discovery-plan/1",
            "endpoint=" + PublisherEndpoint,
            "method=POST",
            "target=/webapi/rdf/sparql",
            "request_media_type=application/sparql-query",
            "response_media_type=" + ResponseMediaType,
            "seed_list_sha256=" + EuSeedResolutionPlan.SeedListSha256,
            "cursor_envelope=" + EnumerationCursorEnvelope.Identity,
            "threshold_detector=" + ThresholdDetectorIdentity,
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(
                CultureInfo.InvariantCulture),
            "pass_1=" + ((int)EuConsolidationQueryPass.Pass1).ToString(
                CultureInfo.InvariantCulture) + ":" + Pass1PageLimit.ToString(
                CultureInfo.InvariantCulture),
            "pass_2=" + ((int)EuConsolidationQueryPass.Pass2).ToString(
                CultureInfo.InvariantCulture) + ":" + Pass2PageLimit.ToString(
                CultureInfo.InvariantCulture),
            "terminal_page_policy=empty_successor_after_short_page",
            "selection_parameters=requested_celex",
            "pass_parameter=pass_id",
            "cursor_presence_parameter=has_cursor",
            "family_projection=" + string.Join(',', familyProjection),
            "family_canonical_keys=" + string.Join(',', familyProjection),
            "family_cursor=" + string.Join(',', familyCursor),
            "facts_projection=" + string.Join(',', factProjection),
            "facts_canonical_keys=" + string.Join(',', factsCursor),
            "facts_cursor=" + string.Join(',', factsCursor),
            "family_count_member=" + FamilyMemberPrefix + ".count",
            "family_page_member=" + FamilyMemberPrefix + ".page",
            "facts_count_member=" + FactsMemberPrefix + ".count",
            "facts_page_member=" + FactsMemberPrefix + ".page",
            templates.FamilyCount,
            templates.FamilyPage,
            templates.FactsCount,
            templates.FactsPage,
        }));
        ArtifactRef = new SourceArtifactRef(ResourceId, Sha256(identityBytes));
        _canonicalIdentityBytes = identityBytes;

        _definitions = new Dictionary<EuConsolidationQuerySet, EuConsolidationQueryDefinition>
        {
            [EuConsolidationQuerySet.Family] = Definition(
                EuConsolidationQuerySet.Family,
                FamilyMemberPrefix,
                templates.FamilyCount,
                templates.FamilyPage,
                familyProjection,
                familyCursor),
            [EuConsolidationQuerySet.TemporalFacts] = Definition(
                EuConsolidationQuerySet.TemporalFacts,
                FactsMemberPrefix,
                templates.FactsCount,
                templates.FactsPage,
                factProjection,
                factsCursor),
        };
    }

    public SourceArtifactRef ArtifactRef { get; }

    public static EuConsolidationDiscoveryPlan Create() => new();

    /// <summary>
    /// A copy of the exact bytes <see cref="ArtifactRef"/> names, so the held array cannot be
    /// mutated through what this hands out.
    /// </summary>
    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    internal EuConsolidationQueryDefinition Definition(EuConsolidationQuerySet set) =>
        _definitions.TryGetValue(set, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(set));

    public RepeatedEnumerationInterpretationProfile CreateDeliveryProfile(
        EuConsolidationQuerySet set)
    {
        var definition = Definition(set);
        return new RepeatedEnumerationInterpretationProfile(
            RepeatedEnumerationInterpretationProfile.SchemaId,
            RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso,
            ResponseMediaType,
            EnumerationCursorEnvelope.Identity,
            PublisherDeliveryCeilingRows,
            ThresholdDetectorIdentity,
            definition.CountQueryFamilyRef,
            definition.PageQueryFamilyRef,
            "count",
            definition.ProjectionVariables,
            set == EuConsolidationQuerySet.TemporalFacts
                ? definition.CursorVariables
                : definition.ProjectionVariables,
            definition.CursorVariables,
            ["requested_celex"],
            "pass_id",
            definition.CursorVariables.Select(static value => "last_" + value).ToArray(),
            "has_cursor",
            RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage);
    }

    public EuConsolidationBoundQuery BindCount(
        EuConsolidationQuerySet set,
        string requestedCelex,
        EuConsolidationQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        var definition = Definition(set);
        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody,
            null,
            null,
            null);
        return Bind(
            definition,
            isPage: false,
            requestedCelex,
            pass,
            cursor: null,
            response,
            machinePlanResourceId,
            inputResourceId,
            rendererSource);
    }

    public EuConsolidationBoundQuery BindPage(
        EuConsolidationQuerySet set,
        string requestedCelex,
        EuConsolidationQueryPass pass,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        var definition = Definition(set);
        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            PageLimit(pass),
            expectedPartitionRowCount,
            expectedPartitionRowCountEvidenceRef);
        return Bind(
            definition,
            isPage: true,
            requestedCelex,
            pass,
            cursor,
            response,
            machinePlanResourceId,
            inputResourceId,
            rendererSource);
    }

    private EuConsolidationBoundQuery Bind(
        EuConsolidationQueryDefinition definition,
        bool isPage,
        string requestedCelex,
        EuConsolidationQueryPass pass,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        RequireCelex(requestedCelex);
        ArgumentNullException.ThrowIfNull(rendererSource);
        var parameters = new List<MachineQueryParameter>
        {
            new(
                "requested_celex",
                MachineQueryParameterKind.PublisherLiteral,
                null,
                requestedCelex,
                ArtifactRef),
            new(
                "pass_id",
                MachineQueryParameterKind.BoundedInteger,
                (int)pass,
                null,
                ArtifactRef),
        };
        if (isPage)
        {
            var values = cursor?.ToArray() ?? [];
            if (values.Length != 0 && values.Length != definition.CursorVariables.Count)
            {
                throw new ArgumentException(
                    "A continuation cursor must have the exact query-set arity.",
                    nameof(cursor));
            }

            parameters.Add(new MachineQueryParameter(
                "has_cursor",
                MachineQueryParameterKind.BoundedInteger,
                values.Length == 0 ? 0 : 1,
                null,
                ArtifactRef));
            for (var index = 0; index < values.Length; index++)
            {
                parameters.Add(new MachineQueryParameter(
                    "last_" + definition.CursorVariables[index],
                    MachineQueryParameterKind.PublisherCursor,
                    null,
                    EnumerationCursorEnvelope.Encode(values[index]),
                    ArtifactRef));
            }
        }
        else if (cursor is not null)
        {
            throw new ArgumentException("A count query cannot carry a cursor.", nameof(cursor));
        }

        var family = isPage ? definition.PageQueryFamilyRef : definition.CountQueryFamilyRef;
        var input = MachineQueryInputArtifact.Create(
            inputResourceId,
            family,
            PartitionKey(requestedCelex),
            response,
            parameters);
        var renderer = new EuConsolidationSparqlRenderer(
            this,
            definition,
            isPage,
            rendererSource);
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
        var machinePlanRef = MachineQueryPlanIdentity.Create(
            machinePlanResourceId,
            machinePlan);
        var request = MachineQueryBinder.BindForSend(
            machinePlan,
            machinePlanRef,
            input,
            renderer);
        return new EuConsolidationBoundQuery(machinePlan, machinePlanRef, input, request);
    }

    private EuConsolidationQueryDefinition Definition(
        EuConsolidationQuerySet set,
        string memberPrefix,
        string count,
        string page,
        IReadOnlyList<string> projection,
        IReadOnlyList<string> cursor) => new(
        set,
        new SourceRegistryMemberRef(ArtifactRef, memberPrefix + ".count"),
        new SourceRegistryMemberRef(ArtifactRef, memberPrefix + ".page"),
        count,
        page,
        projection,
        cursor);

    private static uint PageLimit(EuConsolidationQueryPass pass) => pass switch
    {
        EuConsolidationQueryPass.Pass1 => Pass1PageLimit,
        EuConsolidationQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    private static string PartitionKey(string celex) =>
        "celex-" + Sha256(StrictUtf8.GetBytes(celex))[..24];

    private static void RequireCelex(string value)
    {
        _ = EuConsolidationTerm.RequireAdmittedSeed(value, nameof(value));
    }

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static (string FamilyCount, string FamilyPage, string FactsCount, string FactsPage)
        BuildTemplates()
    {
        var selection = $$"""
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES ?base_celex { {requested_celex:typed_string} }
              ?base <{{CelexPredicateIri}}> ?base_celex .
              ?state <{{BasedOnPredicateIri}}> ?base .
            """;
        var familyRows = $$"""
            SELECT ?base_celex ?base ?state (COUNT(*) AS ?family_multiplicity) WHERE {
            {{Indent(selection)}}
            }
            GROUP BY ?base_celex ?base ?state
            """;
        var familyCount = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(familyRows))}}
              }
            }
            """;
        var familyPage = $$"""
            SELECT ?base_celex ?base ?state ?family_multiplicity ?state_key WHERE {
              {
            {{Indent(Indent(familyRows))}}
              }
              BIND(STR(?state) AS ?state_key)
              VALUES (?has_cursor ?last_state_key) {
                ({has_cursor:uint} {last_state_key:sparql_string})
              }
              FILTER(?has_cursor = 0 || ?state_key > ?last_state_key)
            }
            ORDER BY ?state_key
            LIMIT {page_limit:uint}
            """;
        var factsRows = $$"""
            SELECT ?base_celex ?base ?state ?predicate ?object ?object_kind ?datatype_iri ?language_tag (COUNT(?object) AS ?multiplicity) WHERE {
            {{Indent(selection)}}
              VALUES ?predicate {
                <{{ConsolidatedDatePredicateIri}}>
                <{{ConsolidatedLayerPredicateIri}}>
                <{{ConsolidatedVersionPredicateIri}}>
                <{{ConsolidatedNumberPredicateIri}}>
              }
              {
                ?state ?predicate ?object .
                BIND(IF(isIRI(?object), "iri", IF(isLiteral(?object), "literal", "unsupported_blank_node")) AS ?object_kind)
                BIND(IF(isLiteral(?object), STR(DATATYPE(?object)), "") AS ?datatype_iri)
                BIND(IF(isLiteral(?object), LANG(?object), "") AS ?language_tag)
              }
              UNION
              {
                FILTER NOT EXISTS { ?state ?predicate ?missing_object }
                BIND("unbound" AS ?object_kind)
                BIND("" AS ?datatype_iri)
                BIND("" AS ?language_tag)
              }
            }
            GROUP BY ?base_celex ?base ?state ?predicate ?object ?object_kind ?datatype_iri ?language_tag
            """;
        var factsCount = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(factsRows))}}
              }
            }
            """;
        var factsPage = $$"""
            SELECT ?base_celex ?base ?state ?predicate ?object ?object_kind ?datatype_iri ?language_tag ?multiplicity ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 WHERE {
              {
            {{Indent(Indent(factsRows))}}
              }
              BIND(STR(?state) AS ?key_1)
              BIND(STR(?predicate) AS ?key_2)
              BIND(?object_kind AS ?key_3)
              BIND(IF(BOUND(?object), STR(?object), "") AS ?key_4)
              BIND(?datatype_iri AS ?key_5)
              BIND(?language_tag AS ?key_6)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4 ?last_key_5 ?last_key_6) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string} {last_key_5:sparql_string} {last_key_6:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 > ?last_key_5) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 = ?last_key_5 && ?key_6 > ?last_key_6)
              )
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6
            LIMIT {page_limit:uint}
            """;
        return (Normalize(familyCount), Normalize(familyPage),
            Normalize(factsCount), Normalize(factsPage));
    }

    private static string Indent(string value) => string.Join('\n',
        Normalize(value).Split('\n').Select(static line => "  " + line));

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";
}

internal sealed class EuConsolidationSparqlRenderer : IMachineQueryRenderer
{
    private readonly MachineQueryRendererSource _rendererSource;
    private readonly byte[] _rendererProfileBytes;
    private readonly EuConsolidationQueryDefinition _definition;
    private readonly bool _isPage;

    internal EuConsolidationSparqlRenderer(
        EuConsolidationDiscoveryPlan plan,
        EuConsolidationQueryDefinition definition,
        bool isPage,
        MachineQueryRendererSource rendererSource)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rendererSource);
        _definition = definition;
        _isPage = isPage;
        RendererProfileRef = plan.ArtifactRef;
        _rendererSource = rendererSource;

        // Taken from the same plan instance that minted RendererProfileRef, so the bytes and the
        // digest are two views of one construction rather than two renderings that could drift.
        _rendererProfileBytes = plan.CopyCanonicalIdentityBytes();
    }

    public SourceArtifactRef RendererProfileRef { get; }

    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;

    /// <inheritdoc />
    // Deliberately not written as "_rendererProfileBytes is null ? null : _rendererProfileBytes".
    // That expression compiles against a ReadOnlyMemory<byte>? target and yields a present, empty
    // memory rather than null, which would read as a renderer producing zero bytes. Nothing here
    // is ever null, and the return stays a plain copy for the same reason.
    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _rendererProfileBytes.ToArray();

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(
        MachineQueryPlan plan,
        MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet, plan.ResponseCardinality);

    internal MachineQueryRenderOutput RenderInput(
        MachineQueryInputArtifact input,
        MachineResponseCardinality response)
    {
        var parameters = input.OrderedParameters.ToDictionary(
            static value => value.Name,
            StringComparer.Ordinal);
        var celex = PublisherLiteral(parameters, "requested_celex");
        _ = EuConsolidationTerm.RequireAdmittedSeed(celex, "requested_celex");
        var pass = (EuConsolidationQueryPass)Integer(parameters, "pass_id");
        var limit = pass switch
        {
            EuConsolidationQueryPass.Pass1 => EuConsolidationDiscoveryPlan.Pass1PageLimit,
            EuConsolidationQueryPass.Pass2 => EuConsolidationDiscoveryPlan.Pass2PageLimit,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
        var query = _isPage ? _definition.PageTemplate : _definition.CountTemplate;
        query = Replace(query, "{requested_celex:typed_string}",
            SparqlTypedString(celex));
        query = Replace(query, "{pass_id:uint}",
            ((int)pass).ToString(CultureInfo.InvariantCulture));

        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody || parameters.Count != 2)
            {
                throw new ArgumentException("A count input has one exact shape.", nameof(input));
            }

            return Output(query);
        }

        if (response.Kind != MachineResponseCardinalityKind.BoundedRowSetPage ||
            response.RowLimit != limit)
        {
            throw new ArgumentException("The page limit must come from the pass policy.", nameof(response));
        }

        var hasCursor = Integer(parameters, "has_cursor");
        if (hasCursor is not (0 or 1))
        {
            throw new ArgumentException("Cursor presence must be zero or one.", nameof(input));
        }

        var expectedCount = 3 + (hasCursor == 1 ? _definition.CursorVariables.Count : 0);
        if (parameters.Count != expectedCount)
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        foreach (var variable in _definition.CursorVariables)
        {
            var value = hasCursor == 0
                ? string.Empty
                : Cursor(parameters, "last_" + variable);
            query = Replace(query, "{last_" + variable + ":sparql_string}",
                SparqlQueryText.StringLiteral(value));
        }

        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        EuConsolidationDiscoveryPlan.PublisherEndpoint,
        Encoding.UTF8.GetBytes(query));

    private static long Integer(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters,
        string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger &&
        value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.");

    private static string PublisherLiteral(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters,
        string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherLiteral &&
        value.TextValue is not null
            ? value.TextValue
            : throw new ArgumentException($"The literal input {name} is missing or invalid.");

    private static string Cursor(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters,
        string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherCursor &&
        value.TextValue is not null
            ? EnumerationCursorEnvelope.Decode(value.TextValue)
            : throw new ArgumentException($"The cursor input {name} is missing or invalid.");

    private static string SparqlTypedString(string value) =>
        SparqlQueryText.StringLiteral(value) + "^^<" +
        EuSeedResolutionPlan.XsdStringDatatypeIri + ">";

    private static string Replace(string source, string slot, string replacement)
    {
        if (source.Split(slot, StringSplitOptions.None).Length != 2)
        {
            throw new ArgumentException("A renderer slot must occur exactly once.", nameof(source));
        }

        return source.Replace(slot, replacement, StringComparison.Ordinal);
    }
}

internal sealed record EuConsolidationFamilyRow(
    RepeatedEnumerationRdfTerm BaseCelex,
    RepeatedEnumerationRdfTerm BaseWork,
    RepeatedEnumerationRdfTerm State,
    long Multiplicity)
{
    internal static EuConsolidationFamilyRow Parse(
        RepeatedEnumerationRow row,
        string requestedCelex)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Terms.Count != 5)
        {
            throw new ArgumentException("A consolidation-family row has five exact terms.", nameof(row));
        }

        var celex = EuConsolidationTerm.RequireCelex(
            row.Terms[0], requestedCelex, "base_celex");
        var baseWork = EuConsolidationTerm.RequireCellarWork(row.Terms[1], "base");
        var state = EuConsolidationTerm.RequireCellarWork(row.Terms[2], "state");
        var multiplicity = EuConsolidationTerm.RequirePositiveInteger(
            row.Terms[3], "family_multiplicity");
        EuConsolidationTerm.RequirePlainLiteral(row.Terms[4], state.Value!, "state_key");
        return new EuConsolidationFamilyRow(celex, baseWork, state, multiplicity);
    }
}

internal sealed record EuConsolidationFactRow(
    RepeatedEnumerationRdfTerm BaseCelex,
    RepeatedEnumerationRdfTerm BaseWork,
    RepeatedEnumerationRdfTerm State,
    EuConsolidationTemporalPredicate Predicate,
    RepeatedEnumerationRdfTerm Object,
    string ObjectKind,
    string DatatypeIri,
    string LanguageTag,
    long Multiplicity)
{
    internal static EuConsolidationFactRow Parse(
        RepeatedEnumerationRow row,
        string requestedCelex)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Terms.Count != 15)
        {
            throw new ArgumentException("A consolidation-fact row has fifteen exact terms.", nameof(row));
        }

        var celex = EuConsolidationTerm.RequireCelex(
            row.Terms[0], requestedCelex, "base_celex");
        var baseWork = EuConsolidationTerm.RequireCellarWork(row.Terms[1], "base");
        var state = EuConsolidationTerm.RequireCellarWork(row.Terms[2], "state");
        var predicateTerm = row.Terms[3];
        if (predicateTerm.Kind != RepeatedEnumerationRdfTermKind.Iri)
        {
            throw new ArgumentException("A consolidation predicate must be an IRI.", nameof(row));
        }

        var predicate = predicateTerm.Value switch
        {
            EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri =>
                EuConsolidationTemporalPredicate.Date,
            EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri =>
                EuConsolidationTemporalPredicate.Layer,
            EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri =>
                EuConsolidationTemporalPredicate.Version,
            EuConsolidationDiscoveryPlan.ConsolidatedNumberPredicateIri =>
                EuConsolidationTemporalPredicate.Number,
            _ => throw new ArgumentException("The predicate is outside the temporal fact set.", nameof(row)),
        };
        var value = row.Terms[4];
        if (value.Kind == RepeatedEnumerationRdfTermKind.BlankNode)
        {
            throw new ArgumentException("A selected temporal fact cannot be a blank node.", nameof(row));
        }

        var objectKind = value.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => "iri",
            RepeatedEnumerationRdfTermKind.Literal => "literal",
            _ => "unbound",
        };
        var datatype = value.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri or RepeatedEnumerationRdfTermKind.Unbound =>
                string.Empty,
            _ => value.Datatype ?? (value.Language is null
                ? EuSeedResolutionPlan.XsdStringDatatypeIri
                : EuConsolidationTerm.RdfLangStringDatatypeIri),
        };
        var language = value.Language ?? string.Empty;
        EuConsolidationTerm.RequirePlainLiteral(row.Terms[5], objectKind, "object_kind");
        EuConsolidationTerm.RequirePlainLiteral(row.Terms[6], datatype, "datatype_iri");
        EuConsolidationTerm.RequirePlainLiteral(row.Terms[7], language, "language_tag");
        var multiplicity = EuConsolidationTerm.RequireNonnegativeInteger(
            row.Terms[8], "multiplicity");
        if ((value.Kind == RepeatedEnumerationRdfTermKind.Unbound) != (multiplicity == 0))
        {
            throw new ArgumentException(
                "Only one explicit unbound fact row may carry zero multiplicity.",
                nameof(row));
        }

        var keys = new[]
        {
            state.Value!, predicateTerm.Value!, objectKind, value.Value ?? string.Empty,
            datatype, language,
        };
        for (var index = 0; index < keys.Length; index++)
        {
            EuConsolidationTerm.RequirePlainLiteral(
                row.Terms[9 + index], keys[index], $"key_{index + 1}");
        }

        return new EuConsolidationFactRow(
            celex,
            baseWork,
            state,
            predicate,
            value,
            objectKind,
            datatype,
            language,
            multiplicity);
    }
}

internal sealed record EuConsolidationObservedValue(
    RepeatedEnumerationRdfTerm Term,
    long Multiplicity);

internal sealed class EuConsolidationSelectedState
{
    private EuConsolidationSelectedState(
        EuConsolidationFamilyRow family,
        IReadOnlyList<EuConsolidationObservedValue> date,
        IReadOnlyList<EuConsolidationObservedValue> layer,
        IReadOnlyList<EuConsolidationObservedValue> version,
        IReadOnlyList<EuConsolidationObservedValue> number)
    {
        Family = family;
        Date = date;
        Layer = layer;
        Version = version;
        Number = number;
    }

    internal EuConsolidationFamilyRow Family { get; }
    internal RepeatedEnumerationRdfTerm State => Family.State;
    internal IReadOnlyList<EuConsolidationObservedValue> Date { get; }
    internal IReadOnlyList<EuConsolidationObservedValue> Layer { get; }
    internal IReadOnlyList<EuConsolidationObservedValue> Version { get; }
    internal IReadOnlyList<EuConsolidationObservedValue> Number { get; }

    internal static EuConsolidationSelectedState From(
        EuConsolidationFamilyRow family,
        IReadOnlyList<EuConsolidationFactRow> facts)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(facts);
        var copy = facts.ToArray();
        if (copy.Any(static fact => fact is null) || copy.Any(fact =>
                fact.BaseCelex != family.BaseCelex ||
                fact.BaseWork != family.BaseWork ||
                fact.State != family.State))
        {
            throw new ArgumentException(
                "Every temporal fact must describe the selected family row.",
                nameof(facts));
        }

        return new EuConsolidationSelectedState(
            family,
            Values(copy, EuConsolidationTemporalPredicate.Date),
            Values(copy, EuConsolidationTemporalPredicate.Layer),
            Values(copy, EuConsolidationTemporalPredicate.Version),
            Values(copy, EuConsolidationTemporalPredicate.Number));
    }

    private static IReadOnlyList<EuConsolidationObservedValue> Values(
        IReadOnlyList<EuConsolidationFactRow> facts,
        EuConsolidationTemporalPredicate predicate)
    {
        var selected = facts.Where(fact => fact.Predicate == predicate).ToArray();
        if (selected.Length == 0)
        {
            throw new ArgumentException(
                "Every selected state must carry one explicit row for each temporal predicate.",
                nameof(facts));
        }

        var unbound = selected.Where(static fact =>
            fact.Object.Kind == RepeatedEnumerationRdfTermKind.Unbound).ToArray();
        if (unbound.Length != 0)
        {
            if (selected.Length != 1 || unbound.Length != 1 || unbound[0].Multiplicity != 0)
            {
                throw new ArgumentException(
                    "An unbound temporal predicate must be one explicit zero-multiplicity row.",
                    nameof(facts));
            }

            return Array.AsReadOnly(new[]
            {
                new EuConsolidationObservedValue(unbound[0].Object, 0),
            });
        }

        if (predicate == EuConsolidationTemporalPredicate.Date && selected.Any(static fact =>
                fact.Object.Kind != RepeatedEnumerationRdfTermKind.Literal ||
                !string.Equals(
                    fact.Object.Datatype,
                    EuConsolidationTerm.XsdDateDatatypeIri,
                    StringComparison.Ordinal) ||
                fact.Object.Language is not null))
        {
            throw new ArgumentException(
                "A selected consolidation date is not one exact xsd:date literal.",
                nameof(facts));
        }

        var groups = selected.GroupBy(static fact => fact.Object).ToArray();
        if (groups.Any(static group => group.Count() != 1))
        {
            throw new ArgumentException(
                "An exact fact may occur only once in one grouped selector result.",
                nameof(facts));
        }

        var values = groups
            .Select(static group => new EuConsolidationObservedValue(
                group.Key,
                group.Single().Multiplicity))
            .OrderBy(static value => EuConsolidationTerm.SortKey(value.Term), StringComparer.Ordinal)
            .ToArray();
        return Array.AsReadOnly(values);
    }
}

internal sealed record EuConsolidationSameDateGroup(
    RepeatedEnumerationRdfTerm Date,
    EuConsolidationDateStatus Status,
    IReadOnlyList<EuConsolidationSelectedState> Candidates);

internal sealed class EuConsolidationSameDateAssessment
{
    private EuConsolidationSameDateAssessment(
        IReadOnlyList<EuConsolidationSameDateGroup> boundDates,
        IReadOnlyList<EuConsolidationSelectedState> rowsCarryingUnboundDate)
    {
        BoundDates = boundDates;
        RowsCarryingUnboundDate = rowsCarryingUnboundDate;
    }

    internal IReadOnlyList<EuConsolidationSameDateGroup> BoundDates { get; }
    internal IReadOnlyList<EuConsolidationSelectedState> RowsCarryingUnboundDate { get; }

    internal static EuConsolidationSameDateAssessment From(
        IReadOnlyList<EuConsolidationSelectedState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var copy = states.ToArray();
        if (copy.Any(static state => state is null))
        {
            throw new ArgumentException("Selected states cannot contain null.", nameof(states));
        }


        if (copy.Select(static state => state.Family.BaseCelex).Distinct().Count() > 1 ||
            copy.Select(static state => state.Family.BaseWork).Distinct().Count() > 1)
        {
            throw new ArgumentException(
                "One same-date assessment cannot mix CELEX or base-work coordinates.",
                nameof(states));
        }

        if (copy.Select(static state => state.State).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException(
                "One Cellar state may occur only once in an assessment.",
                nameof(states));
        }

        var withoutDate = copy
            .Where(static state => state.Date.All(value =>
                value.Term.Kind == RepeatedEnumerationRdfTermKind.Unbound))
            .OrderBy(static state => state.State.Value, StringComparer.Ordinal)
            .ToArray();
        var dated = copy
            .SelectMany(state => state.Date
                .Where(static value => value.Term.Kind != RepeatedEnumerationRdfTermKind.Unbound)
                .Select(value => (State: state, Date: value.Term)))
            .GroupBy(static value => value.Date)
            .Select(group =>
            {
                var candidates = group.Select(static value => value.State)
                    .OrderBy(static state => state.State.Value, StringComparer.Ordinal)
                    .ToArray();
                var ambiguous = candidates.Length > 1 || candidates.Any(static candidate =>
                    BoundValueCount(candidate.Date) > 1 ||
                    BoundValueCount(candidate.Layer) > 1 ||
                    BoundValueCount(candidate.Version) > 1 ||
                    BoundValueCount(candidate.Number) > 1);
                return new EuConsolidationSameDateGroup(
                    group.Key,
                    ambiguous
                        ? EuConsolidationDateStatus.AmbiguousVersion
                        : EuConsolidationDateStatus.OneObservedCandidate,
                    Array.AsReadOnly(candidates));
            })
            .OrderBy(static group => EuConsolidationTerm.SortKey(group.Date), StringComparer.Ordinal)
            .ToArray();
        return new EuConsolidationSameDateAssessment(
            Array.AsReadOnly(dated),
            Array.AsReadOnly(withoutDate));
    }

    private static int BoundValueCount(IReadOnlyList<EuConsolidationObservedValue> values) =>
        values.Count(static value => value.Term.Kind != RepeatedEnumerationRdfTermKind.Unbound);
}

internal static class EuConsolidationTerm
{
    internal const string XsdDateDatatypeIri =
        "http://www.w3.org/2001/XMLSchema#date";
    internal const string RdfLangStringDatatypeIri =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";

    internal static RepeatedEnumerationRdfTerm RequireCelex(
        RepeatedEnumerationRdfTerm term,
        string requestedCelex,
        string name)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            !string.Equals(term.Datatype, EuSeedResolutionPlan.XsdStringDatatypeIri,
                StringComparison.Ordinal) ||
            term.Language is not null)
        {
            throw new ArgumentException(
                "A returned CELEX must be the exact xsd:string RDF term.", name);
        }

        _ = RequireAdmittedSeed(requestedCelex, nameof(requestedCelex));
        _ = new OfficialIdentifier(FactsIdentifierFamily.Celex, term.Value!);
        if (!string.Equals(term.Value, requestedCelex, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The returned CELEX differs from the exact requested seed.", name);
        }

        return term;
    }

    internal static string RequireAdmittedSeed(string value, string name)
    {
        _ = new OfficialIdentifier(FactsIdentifierFamily.Celex, value);
        return EuSeedResolutionPlan.Seeds.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new ArgumentException(
                "EU consolidation discovery is limited to the frozen admitted seed set.",
                name);
    }

    internal static RepeatedEnumerationRdfTerm RequireCellarWork(
        RepeatedEnumerationRdfTerm term,
        string name)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri || term.Value is null)
        {
            throw new ArgumentException("A Cellar work must be an IRI.", name);
        }

        _ = new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, term.Value);

        return term;
    }

    internal static long RequirePositiveInteger(RepeatedEnumerationRdfTerm term, string name)
    {
        var value = RequireNonnegativeInteger(term, name);
        return value > 0
            ? value
            : throw new ArgumentException("Multiplicity must be positive.", name);
    }

    internal static long RequireNonnegativeInteger(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            !string.Equals(term.Datatype,
                "http://www.w3.org/2001/XMLSchema#integer",
                StringComparison.Ordinal) ||
            term.Language is not null ||
            !long.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var value) ||
            value < 0)
        {
            throw new ArgumentException("Multiplicity must be one nonnegative xsd:integer.", name);
        }

        return value;
    }

    internal static void RequirePlainLiteral(
        RepeatedEnumerationRdfTerm term,
        string expected,
        string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            term.Datatype is not null ||
            term.Language is not null ||
            !string.Equals(term.Value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException("A derived query key does not match its raw term.", name);
        }
    }

    internal static string SortKey(RepeatedEnumerationRdfTerm term) => string.Join('\u001f',
        ((int)term.Kind).ToString(CultureInfo.InvariantCulture),
        term.Value ?? string.Empty,
        term.Datatype ?? string.Empty,
        term.Language ?? string.Empty);
}
