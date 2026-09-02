using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public enum LuxembourgDatasetGraphKind
{
    [JsonStringEnumMemberName("default_graph")]
    DefaultGraph = 1,
}

public enum LuxembourgQueryKeyKind
{
    [JsonStringEnumMemberName("absolute_iri_utf8")]
    AbsoluteIriUtf8 = 1,

    [JsonStringEnumMemberName("composite_literal_utf8")]
    CompositeLiteralUtf8 = 2,
}

public enum LuxembourgQueryPass
{
    Pass1 = 1,
    Pass2 = 2,
}

public enum LuxembourgQuerySetAcquisition
{
    [JsonStringEnumMemberName("publisher_query")]
    PublisherQuery = 1,

    [JsonStringEnumMemberName("local_materialization")]
    LocalMaterialization = 2,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgDatasetGraphIdentity
{
    [JsonConstructor]
    public LuxembourgDatasetGraphIdentity(
        LuxembourgDatasetGraphKind kind,
        string endpoint,
        SourceArtifactRef sourceProfileRef,
        SourceArtifactRef scopeDefinitionRef)
    {
        Kind = RequireDefined(kind, nameof(kind));
        Endpoint = RequireEndpoint(endpoint, nameof(endpoint));
        SourceProfileRef = sourceProfileRef ?? throw new ArgumentNullException(nameof(sourceProfileRef));
        ScopeDefinitionRef = scopeDefinitionRef
            ?? throw new ArgumentNullException(nameof(scopeDefinitionRef));
    }

    public LuxembourgDatasetGraphKind Kind { get; }

    public string Endpoint { get; }

    public SourceArtifactRef SourceProfileRef { get; }

    public SourceArtifactRef ScopeDefinitionRef { get; }

    private static T RequireDefined<T>(T value, string parameterName) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static string RequireEndpoint(string value, string parameterName) =>
        string.Equals(value, LuxembourgQueryPlan.PublisherEndpoint, StringComparison.Ordinal)
            ? value
            : throw new ArgumentException("The LU plan must use the exact publisher endpoint.", parameterName);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgQuerySetDefinition(
    string SetId,
    string? TemplateId,
    LuxembourgQuerySetAcquisition Acquisition,
    LuxembourgQueryKeyKind KeyKind);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgQueryTemplate(
    string TemplateId,
    string Utf8QueryTemplate,
    string Utf8CountTemplate);

public sealed record LuxembourgQueryPartitionRange
{
    public LuxembourgQueryPartitionRange(
        string partitionId,
        LuxembourgQueryCursor startInclusive,
        LuxembourgQueryCursor endExclusive)
    {
        PartitionId = RequireAsciiMember(partitionId, nameof(partitionId));
        StartInclusive = startInclusive ?? throw new ArgumentNullException(nameof(startInclusive));
        EndExclusive = endExclusive ?? throw new ArgumentNullException(nameof(endExclusive));
        if (StartInclusive.CompareTo(EndExclusive) >= 0)
        {
            throw new ArgumentException("A partition range must be finite and increasing.");
        }
    }

    public string PartitionId { get; }
    public LuxembourgQueryCursor StartInclusive { get; }
    public LuxembourgQueryCursor EndExclusive { get; }

    private static string RequireAsciiMember(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            value.Any(static character => character is < '!' or > '~'))
        {
            throw new ArgumentException(
                "A partition identity must be bounded printable ASCII.",
                parameterName);
        }

        return value;
    }
}

public sealed record LuxembourgQueryCursor
{
    public LuxembourgQueryCursor(
        string key1,
        string key2,
        string key3,
        string key4,
        string key5,
        string key6)
    {
        Key1 = LuxembourgQueryText.RequireKeyPart(key1, nameof(key1), allowEmpty: true);
        Key2 = LuxembourgQueryText.RequireKeyPart(key2, nameof(key2), allowEmpty: true);
        Key3 = LuxembourgQueryText.RequireKeyPart(key3, nameof(key3), allowEmpty: true);
        Key4 = LuxembourgQueryText.RequireKeyPart(key4, nameof(key4), allowEmpty: true);
        Key5 = LuxembourgQueryText.RequireKeyPart(key5, nameof(key5), allowEmpty: true);
        Key6 = LuxembourgQueryText.RequireKeyPart(key6, nameof(key6), allowEmpty: true);
    }

    public string Key1 { get; }
    public string Key2 { get; }
    public string Key3 { get; }
    public string Key4 { get; }
    public string Key5 { get; }
    public string Key6 { get; }

    public int CompareTo(LuxembourgQueryCursor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var left = Parts;
        var right = other.Parts;
        for (var index = 0; index < left.Length; index++)
        {
            var comparison = LuxembourgQueryText.CompareUtf8(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    internal string[] Parts => [Key1, Key2, Key3, Key4, Key5, Key6];
}

public static class LuxembourgQueryPlanIdentity
{
    public const string CanonicalizationIdentity = "lex-lu-query-plan-canonical-json/1";

    public static SourceArtifactRef Create(string resourceId, LuxembourgQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SourceArtifactRef(resourceId, Convert.ToHexString(
            SHA256.HashData(GetCanonicalBytes(plan))).ToLowerInvariant());
    }

    public static byte[] GetCanonicalBytes(LuxembourgQueryPlan plan)
    {
        LuxembourgQueryPlan.EnsureClosed(plan);
        return ContractCanonicalizer.Canonicalize(plan, CanonicalizationIdentity, maximumDepth: 64);
    }

    public static void Validate(SourceArtifactRef artifactRef, LuxembourgQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        if (Create(artifactRef.ResourceId, plan) != artifactRef)
        {
            throw new ArgumentException(
                "The LU query-plan reference does not bind its closed plan.",
                nameof(artifactRef));
        }
    }
}

internal static class LuxembourgQueryText
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string RequireKeyPart(string value, string parameterName, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("A query key must be valid UTF-8 text.", parameterName, exception);
        }

        if ((!allowEmpty && bytes.Length == 0) || bytes.Length > 2047)
        {
            throw new ArgumentException("A query key must be bounded control-free text.", parameterName);
        }

        return value;
    }

    public static string EncodeHex(string value) =>
        "h" + Convert.ToHexString(StrictUtf8.GetBytes(value)).ToLowerInvariant();

    public static int CompareUtf8(string left, string right) =>
        StrictUtf8.GetBytes(left).AsSpan().SequenceCompareTo(StrictUtf8.GetBytes(right));

    public static string DecodeHex(string value)
    {
        try
        {
            if (!value.StartsWith('h'))
            {
                throw new FormatException("Missing canonical hex envelope.");
            }

            var decoded = StrictUtf8.GetString(Convert.FromHexString(value[1..]));
            if (!string.Equals(value, EncodeHex(decoded), StringComparison.Ordinal))
            {
                throw new FormatException("The UTF-8 hex envelope is not canonical.");
            }

            return decoded;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw new ArgumentException("A query key input is not canonical UTF-8 hex.", nameof(value), exception);
        }
    }

    public static string DecodeStrict(ReadOnlySpan<byte> value)
    {
        try
        {
            return StrictUtf8.GetString(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("The LU query plan bytes are not strict UTF-8.", nameof(value), exception);
        }
    }

    public static string SparqlString(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var character in value)
        {
            _ = character switch
            {
                '\\' => builder.Append("\\\\"),
                '"' => builder.Append("\\\""),
                '\t' => builder.Append("\\t"),
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                '\b' => builder.Append("\\b"),
                '\f' => builder.Append("\\f"),
                < ' ' or '\u007f' => builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture)),
                _ => builder.Append(character),
            };
        }

        return builder.Append('"').ToString();
    }
}

public sealed class LuxembourgBoundQueryPage
{
    internal LuxembourgBoundQueryPage(
        SourceArtifactRef invariantPlanRef,
        MachineQueryPlan machinePlan,
        SourceArtifactRef machinePlanRef,
        MachineQueryInputArtifact inputArtifact,
        BoundMachineRequest request)
    {
        InvariantPlanRef = invariantPlanRef;
        MachinePlan = machinePlan;
        MachinePlanRef = machinePlanRef;
        InputArtifact = inputArtifact;
        Request = request;
    }

    public SourceArtifactRef InvariantPlanRef { get; }
    public MachineQueryPlan MachinePlan { get; }
    public SourceArtifactRef MachinePlanRef { get; }
    public MachineQueryInputArtifact InputArtifact { get; }
    public BoundMachineRequest Request { get; }
}

public sealed class LuxembourgBoundQueryCount
{
    internal LuxembourgBoundQueryCount(LuxembourgBoundMachineTuple bound)
    {
        InvariantPlanRef = bound.InvariantPlanRef;
        MachinePlan = bound.MachinePlan;
        MachinePlanRef = bound.MachinePlanRef;
        InputArtifact = bound.InputArtifact;
        Request = bound.Request;
    }

    public SourceArtifactRef InvariantPlanRef { get; }
    public MachineQueryPlan MachinePlan { get; }
    public SourceArtifactRef MachinePlanRef { get; }
    public MachineQueryInputArtifact InputArtifact { get; }
    public BoundMachineRequest Request { get; }
}

internal sealed record LuxembourgBoundMachineTuple(
    SourceArtifactRef InvariantPlanRef,
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

internal enum LuxembourgQueryRequestKind
{
    Page = 1,
    Count = 2,
}

internal static class LuxembourgQueryPageBinder
{
    private const string FormContentType = "application/x-www-form-urlencoded";

    public static LuxembourgBoundQueryPage Bind(
        LuxembourgQueryPlan invariantPlan,
        string invariantPlanResourceId,
        string machinePlanResourceId,
        string inputResourceId,
        string setId,
        LuxembourgQueryPass pass,
        LuxembourgQueryPartitionRange partition,
        LuxembourgQueryCursor? lastCursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        SourceArtifactRef rendererSourceRef)
    {
        ArgumentNullException.ThrowIfNull(invariantPlan);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(expectedPartitionRowCountEvidenceRef);
        ArgumentNullException.ThrowIfNull(rendererSourceRef);
        var definition = invariantPlan.SetDefinitions.SingleOrDefault(value => value.SetId == setId)
            ?? throw new ArgumentException("The set identity is not in the LU plan.", nameof(setId));
        if (definition.Acquisition != LuxembourgQuerySetAcquisition.PublisherQuery ||
            definition.TemplateId is null)
        {
            throw new ArgumentException("A local materialization has no publisher query.", nameof(setId));
        }

        var (invariantPlanRef, template) = Resolve(
            invariantPlan, invariantPlanResourceId, definition);
        var pageLimit = LuxembourgQueryPassPolicy.PageLimitFor(pass);
        if (lastCursor is not null &&
            (lastCursor.CompareTo(partition.StartInclusive) < 0 ||
             lastCursor.CompareTo(partition.EndExclusive) >= 0))
        {
            throw new ArgumentException(
                "A page cursor must belong to the exact partition range.",
                nameof(lastCursor));
        }

        var parameters = RangeParameters(partition, pass, invariantPlanRef).ToList();
        parameters.Add(Integer("has_cursor", lastCursor is null ? 0 : 1, invariantPlanRef));
        if (lastCursor is not null)
        {
            parameters.AddRange(lastCursor.Parts.Select((value, index) =>
                Text($"last_key_{index + 1}", value, invariantPlanRef)));
        }

        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            rowLimit: pageLimit,
            expectedPartitionRowCount,
            expectedPartitionRowCountEvidenceRef);
        var bound = BindMachine(
            invariantPlan,
            invariantPlanRef,
            machinePlanResourceId,
            inputResourceId,
            definition,
            template,
            partition,
            LuxembourgQueryRequestKind.Page,
            response,
            parameters,
            rendererSourceRef);
        return new LuxembourgBoundQueryPage(
            bound.InvariantPlanRef,
            bound.MachinePlan,
            bound.MachinePlanRef,
            bound.InputArtifact,
            bound.Request);
    }

    public static LuxembourgBoundQueryCount BindCount(
        LuxembourgQueryPlan invariantPlan,
        string invariantPlanResourceId,
        string machinePlanResourceId,
        string inputResourceId,
        string setId,
        LuxembourgQueryPass pass,
        LuxembourgQueryPartitionRange partition,
        SourceArtifactRef rendererSourceRef)
    {
        ArgumentNullException.ThrowIfNull(invariantPlan);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(rendererSourceRef);
        _ = LuxembourgQueryPassPolicy.PageLimitFor(pass);
        var definition = invariantPlan.SetDefinitions.SingleOrDefault(value => value.SetId == setId)
            ?? throw new ArgumentException("The set identity is not in the LU plan.", nameof(setId));
        if (definition.Acquisition != LuxembourgQuerySetAcquisition.PublisherQuery ||
            definition.TemplateId is null)
        {
            throw new ArgumentException("A local materialization has no publisher query.", nameof(setId));
        }

        var (invariantPlanRef, template) = Resolve(
            invariantPlan, invariantPlanResourceId, definition);
        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody,
            rowLimit: null,
            expectedPartitionRowCount: null,
            expectedPartitionRowCountEvidenceRef: null);
        return new LuxembourgBoundQueryCount(BindMachine(
            invariantPlan,
            invariantPlanRef,
            machinePlanResourceId,
            inputResourceId,
            definition,
            template,
            partition,
            LuxembourgQueryRequestKind.Count,
            response,
            RangeParameters(partition, pass, invariantPlanRef),
            rendererSourceRef));
    }

    private static LuxembourgBoundMachineTuple BindMachine(
        LuxembourgQueryPlan invariantPlan,
        SourceArtifactRef invariantPlanRef,
        string machinePlanResourceId,
        string inputResourceId,
        LuxembourgQuerySetDefinition definition,
        LuxembourgQueryTemplate template,
        LuxembourgQueryPartitionRange partition,
        LuxembourgQueryRequestKind kind,
        MachineResponseCardinality response,
        IEnumerable<MachineQueryParameter> parameters,
        SourceArtifactRef rendererSourceRef)
    {
        var queryFamily = new SourceRegistryMemberRef(
            invariantPlanRef,
            $"{definition.TemplateId}.{kind.ToString().ToLowerInvariant()}");
        var input = MachineQueryInputArtifact.Create(
            inputResourceId,
            queryFamily,
            partition.PartitionId,
            response,
            parameters.OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray());
        var renderer = new LuxembourgSparqlRenderer(
            invariantPlanRef,
            rendererSourceRef,
            template,
            kind);
        var rendered = renderer.RenderInput(input, response);
        var targetBytes = Encoding.ASCII.GetBytes("/sparqlendpoint");
        var body = rendered.CopyRequestBody();
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            queryFamily,
            invariantPlanRef,
            rendererSourceRef,
            HttpRequestMethod.Post,
            invariantPlan.DatasetGraphIdentity.Endpoint,
            targetBytes.LongLength,
            Sha256(targetBytes),
            response,
            new SourceRegistryMemberRef(invariantPlanRef, FormContentType),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));
        var machinePlanRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        var request = MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer);
        return new LuxembourgBoundMachineTuple(
            invariantPlanRef,
            machinePlan,
            machinePlanRef,
            input,
            request);
    }

    private static (SourceArtifactRef InvariantPlanRef, LuxembourgQueryTemplate Template) Resolve(
        LuxembourgQueryPlan invariantPlan,
        string invariantPlanResourceId,
        LuxembourgQuerySetDefinition definition)
    {
        var invariantPlanRef = LuxembourgQueryPlanIdentity.Create(
            invariantPlanResourceId,
            invariantPlan);
        return (invariantPlanRef, invariantPlan.QueryTemplates.Single(value =>
            value.TemplateId == definition.TemplateId));
    }

    private static IEnumerable<MachineQueryParameter> RangeParameters(
        LuxembourgQueryPartitionRange partition,
        LuxembourgQueryPass pass,
        SourceArtifactRef provenance)
    {
        yield return Integer("pass_id", (int)pass, provenance);
        for (var index = 0; index < partition.EndExclusive.Parts.Length; index++)
        {
            yield return Text(
                $"partition_end_{index + 1}",
                partition.EndExclusive.Parts[index],
                provenance);
            yield return Text(
                $"partition_start_{index + 1}",
                partition.StartInclusive.Parts[index],
                provenance);
        }
    }

    private static MachineQueryParameter Integer(
        string name,
        long value,
        SourceArtifactRef provenance) => new(
        name, MachineQueryParameterKind.BoundedInteger, value, null, provenance);

    private static MachineQueryParameter Text(
        string name,
        string value,
        SourceArtifactRef provenance) => new(
        name, MachineQueryParameterKind.PublisherCursor, null,
        LuxembourgQueryText.EncodeHex(value), provenance);

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

internal sealed class LuxembourgSparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgQueryTemplate _template;
    private readonly LuxembourgQueryRequestKind _kind;

    public LuxembourgSparqlRenderer(
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        LuxembourgQueryTemplate template,
        LuxembourgQueryRequestKind kind)
    {
        RendererProfileRef = rendererProfileRef;
        RendererSourceRef = rendererSourceRef;
        _template = template;
        _kind = kind;
    }

    public SourceArtifactRef RendererProfileRef { get; }
    public SourceArtifactRef RendererSourceRef { get; }

    public MachineQueryRenderOutput Render(
        MachineQueryPlan plan,
        MachineQueryInputArtifact orderedParameterSet)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return RenderInput(orderedParameterSet, plan.ResponseCardinality);
    }

    internal MachineQueryRenderOutput RenderInput(
        MachineQueryInputArtifact input,
        MachineResponseCardinality response)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(response);
        var parameters = input.OrderedParameters.ToDictionary(
            static value => value.Name,
            StringComparer.Ordinal);
        var pass = (LuxembourgQueryPass)Integer(parameters, "pass_id");
        var passLimit = LuxembourgQueryPassPolicy.PageLimitFor(pass);
        var query = _kind == LuxembourgQueryRequestKind.Page
            ? _template.Utf8QueryTemplate
            : _template.Utf8CountTemplate;
        query = Replace(
            query,
            "{pass_id:uint}",
            ((int)pass).ToString(CultureInfo.InvariantCulture));
        for (var index = 1; index <= 6; index++)
        {
            query = Replace(query, $"{{partition_start_{index}:sparql_string}}",
                LuxembourgQueryText.SparqlString(Text(parameters, $"partition_start_{index}")));
            query = Replace(query, $"{{partition_end_{index}:sparql_string}}",
                LuxembourgQueryText.SparqlString(Text(parameters, $"partition_end_{index}")));
        }

        if (_kind == LuxembourgQueryRequestKind.Count)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody || parameters.Count != 13)
            {
                throw new ArgumentException("A count input must be one exact opaque request.", nameof(input));
            }

            return Output(query);
        }

        if (response.Kind != MachineResponseCardinalityKind.BoundedRowSetPage ||
            response.RowLimit != passLimit)
        {
            throw new ArgumentException(
                "A page response limit must be the sole exact pass-limit truth.",
                nameof(response));
        }

        var hasCursor = Integer(parameters, "has_cursor");
        if (hasCursor is not (0 or 1))
        {
            throw new ArgumentException("The cursor-presence input must be zero or one.", nameof(input));
        }

        var expectedCount = hasCursor == 0 ? 14 : 20;
        if (parameters.Count != expectedCount ||
            (hasCursor == 0 && parameters.Keys.Any(static key => key.StartsWith("last_key_", StringComparison.Ordinal))))
        {
            throw new ArgumentException("The query input does not match its cursor state.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}",
            passLimit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var index = 1; index <= 6; index++)
        {
            var value = hasCursor == 0 ? string.Empty : Text(parameters, $"last_key_{index}");
            query = Replace(
                query,
                $"{{last_key_{index}:sparql_string}}",
                LuxembourgQueryText.SparqlString(value));
        }

        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        LuxembourgQueryPlan.PublisherEndpoint,
        Encoding.UTF8.GetBytes("query=" + Uri.EscapeDataString(query)));

    private static long Integer(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters,
        string name) => parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.", nameof(parameters));

    private static string Text(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters,
        string name) => parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherCursor && value.TextValue is not null
            ? LuxembourgQueryText.DecodeHex(value.TextValue)
            : throw new ArgumentException($"The text input {name} is missing or invalid.", nameof(parameters));

    private static string Replace(string value, string slot, string replacement)
    {
        if (value.Split(slot, StringSplitOptions.None).Length != 2)
        {
            throw new ArgumentException("A renderer slot must occur exactly once.", nameof(value));
        }

        return value.Replace(slot, replacement, StringComparison.Ordinal);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgKeysetSuccessorRule
{
    public LuxembourgKeysetSuccessorRule(
        string comparison,
        string orderBy,
        int componentCount,
        bool emptySuccessorRequired)
    {
        if (!string.Equals(comparison, "strict_greater_than", StringComparison.Ordinal) ||
            !string.Equals(orderBy, "canonical_utf8_tuple_ascending", StringComparison.Ordinal) ||
            componentCount != 6 ||
            !emptySuccessorRequired)
        {
            throw new ArgumentException("The LU keyset successor rule is closed and strict.");
        }

        Comparison = comparison;
        OrderBy = orderBy;
        ComponentCount = componentCount;
        EmptySuccessorRequired = emptySuccessorRequired;
    }

    public string Comparison { get; }
    public string OrderBy { get; }
    public int ComponentCount { get; }
    public bool EmptySuccessorRequired { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public static class LuxembourgQueryPassPolicy
{
    public const uint MaximumPageLimit = 1000;

    public static uint PageLimitFor(LuxembourgQueryPass pass) => pass switch
    {
        LuxembourgQueryPass.Pass1 => 997,
        LuxembourgQueryPass.Pass2 => 613,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgPageTraversalRule
{
    public LuxembourgPageTraversalRule(
        bool successorAfterFullPageRequired,
        bool emptySuccessorAfterShortPageRequired,
        bool duplicateKeyRejectsObservation,
        bool nonStrictOrderRejectsObservation)
    {
        if (!successorAfterFullPageRequired ||
            !emptySuccessorAfterShortPageRequired ||
            !duplicateKeyRejectsObservation ||
            !nonStrictOrderRejectsObservation)
        {
            throw new ArgumentException("The LU observation checks are fail closed.");
        }

        SuccessorAfterFullPageRequired = successorAfterFullPageRequired;
        EmptySuccessorAfterShortPageRequired = emptySuccessorAfterShortPageRequired;
        DuplicateKeyRejectsObservation = duplicateKeyRejectsObservation;
        NonStrictOrderRejectsObservation = nonStrictOrderRejectsObservation;
    }

    public bool SuccessorAfterFullPageRequired { get; }
    public bool EmptySuccessorAfterShortPageRequired { get; }
    public bool DuplicateKeyRejectsObservation { get; }
    public bool NonStrictOrderRejectsObservation { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgQueryPlan
{
    public const string SchemaId = "lex-lu-query-plan/1";
    public const string PublisherEndpoint = "https://data.legilux.public.lu/sparqlendpoint";
    private static readonly string[] SchemeRootIris =
    [
        "http://data.legilux.public.lu/resource/authority/license/",
        "http://data.legilux.public.lu/resource/authority/resource-type/",
        "http://data.legilux.public.lu/resource/authority/statut-version/",
        "http://data.legilux.public.lu/resource/authority/user-format/",
        "http://publications.europa.eu/resource/authority/language/",
    ];

    private static readonly string[] SelectorPredicateIris = AcceptedPredicates(
        LuxembourgVocabularyKind.AssertionPredicate);

    private static readonly string[] RelationPredicateIris = AcceptedPredicates(
        LuxembourgVocabularyKind.RelationPredicate);

    private readonly IReadOnlyList<string> _schemeRoots;
    private readonly IReadOnlyList<string> _selectorPredicates;
    private readonly IReadOnlyList<string> _relationPredicates;
    private readonly IReadOnlyList<LuxembourgQuerySetDefinition> _setDefinitions;
    private readonly IReadOnlyList<LuxembourgQueryTemplate> _queryTemplates;

    [JsonConstructor]
    private LuxembourgQueryPlan(
        string schema,
        LuxembourgDatasetGraphIdentity datasetGraphIdentity,
        IReadOnlyList<string> schemeRoots,
        IReadOnlyList<string> selectorPredicates,
        IReadOnlyList<string> relationPredicates,
        IReadOnlyList<LuxembourgQuerySetDefinition> setDefinitions,
        IReadOnlyList<LuxembourgQueryTemplate> queryTemplates,
        LuxembourgKeysetSuccessorRule keysetSuccessorRule,
        LuxembourgPageTraversalRule pageTraversalRule)
    {
        if (!string.Equals(schema, SchemaId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"An LU query plan must declare {SchemaId}.", nameof(schema));
        }

        Schema = schema;
        DatasetGraphIdentity = datasetGraphIdentity
            ?? throw new ArgumentNullException(nameof(datasetGraphIdentity));
        _schemeRoots = CopySortedUnique(schemeRoots, nameof(schemeRoots));
        _selectorPredicates = CopySortedUnique(selectorPredicates, nameof(selectorPredicates));
        _relationPredicates = CopySortedUnique(relationPredicates, nameof(relationPredicates));
        _setDefinitions = CopySetDefinitions(setDefinitions);
        _queryTemplates = CopyQueryTemplates(queryTemplates);
        var definitionTemplates = _setDefinitions
            .Where(static value => value.Acquisition == LuxembourgQuerySetAcquisition.PublisherQuery)
            .Select(static value => value.TemplateId!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!definitionTemplates.SequenceEqual(
                _queryTemplates.Select(static value => value.TemplateId),
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Publisher set definitions and query templates must form an exact bijection.",
                nameof(queryTemplates));
        }
        KeysetSuccessorRule = keysetSuccessorRule
            ?? throw new ArgumentNullException(nameof(keysetSuccessorRule));
        PageTraversalRule = pageTraversalRule
            ?? throw new ArgumentNullException(nameof(pageTraversalRule));
    }

    public string Schema { get; }
    public LuxembourgDatasetGraphIdentity DatasetGraphIdentity { get; }
    public IReadOnlyList<string> SchemeRoots => _schemeRoots;
    public IReadOnlyList<string> SelectorPredicates => _selectorPredicates;
    public IReadOnlyList<string> RelationPredicates => _relationPredicates;
    public IReadOnlyList<LuxembourgQuerySetDefinition> SetDefinitions => _setDefinitions;
    public IReadOnlyList<LuxembourgQueryTemplate> QueryTemplates => _queryTemplates;
    public LuxembourgKeysetSuccessorRule KeysetSuccessorRule { get; }
    public LuxembourgPageTraversalRule PageTraversalRule { get; }

    public static LuxembourgQueryPlan CreateDefaultGraph(
        SourceArtifactRef sourceProfileRef,
        SourceArtifactRef scopeDefinitionRef)
    {
        ArgumentNullException.ThrowIfNull(sourceProfileRef);
        ArgumentNullException.ThrowIfNull(scopeDefinitionRef);
        var graph = new LuxembourgDatasetGraphIdentity(
            LuxembourgDatasetGraphKind.DefaultGraph,
            PublisherEndpoint,
            sourceProfileRef,
            scopeDefinitionRef);
        return CreateClosed(graph, SelectorPredicateIris, RelationPredicateIris);
    }

    public static LuxembourgQueryPlan ParseAndVerify(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalJson)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        var json = LuxembourgQueryText.DecodeStrict(canonicalJson);
        LuxembourgQueryPlan parsed;
        try
        {
            parsed = ContractJson.Deserialize<LuxembourgQueryPlan>(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("The retained LU query plan is invalid.", nameof(canonicalJson), exception);
        }

        var rebuilt = CreateDefaultGraph(
            parsed.DatasetGraphIdentity.SourceProfileRef,
            parsed.DatasetGraphIdentity.ScopeDefinitionRef);
        if (!canonicalJson.SequenceEqual(GetWireBytes(rebuilt)))
        {
            throw new ArgumentException(
                "The retained LU query plan is not the exact closed factory representation.",
                nameof(canonicalJson));
        }

        LuxembourgQueryPlanIdentity.Validate(artifactRef, rebuilt);
        return rebuilt;
    }

    public static byte[] GetWireBytes(LuxembourgQueryPlan plan)
    {
        EnsureClosed(plan);
        return Encoding.UTF8.GetBytes(ContractJson.Serialize(plan));
    }

    internal static void EnsureClosed(LuxembourgQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var rebuilt = CreateDefaultGraph(
            plan.DatasetGraphIdentity.SourceProfileRef,
            plan.DatasetGraphIdentity.ScopeDefinitionRef);
        if (!Encoding.UTF8.GetBytes(ContractJson.Serialize(plan))
                .SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(rebuilt))))
        {
            throw new ArgumentException("The LU query plan differs from its closed factory.", nameof(plan));
        }
    }

    public LuxembourgBoundQueryPage BindPage(
        string queryPlanResourceId,
        string machinePlanResourceId,
        string inputResourceId,
        string setId,
        LuxembourgQueryPass pass,
        LuxembourgQueryPartitionRange partition,
        LuxembourgQueryCursor? lastCursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        SourceArtifactRef rendererSourceRef) => LuxembourgQueryPageBinder.Bind(
        this,
        queryPlanResourceId,
        machinePlanResourceId,
        inputResourceId,
        setId,
        pass,
        partition,
        lastCursor,
        expectedPartitionRowCount,
        expectedPartitionRowCountEvidenceRef,
        rendererSourceRef);

    public LuxembourgBoundQueryCount BindCount(
        string queryPlanResourceId,
        string machinePlanResourceId,
        string inputResourceId,
        string setId,
        LuxembourgQueryPass pass,
        LuxembourgQueryPartitionRange partition,
        SourceArtifactRef rendererSourceRef) => LuxembourgQueryPageBinder.BindCount(
        this,
        queryPlanResourceId,
        machinePlanResourceId,
        inputResourceId,
        setId,
        pass,
        partition,
        rendererSourceRef);

    private static LuxembourgQueryPlan CreateClosed(
        LuxembourgDatasetGraphIdentity graph,
        string[] selectors,
        string[] relations)
    {
        var definitions = new[]
        {
            new LuxembourgQuerySetDefinition("A", "assertion-rows", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.CompositeLiteralUtf8),
            new LuxembourgQuerySetDefinition("C", "controlled-concepts", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.AbsoluteIriUtf8),
            new LuxembourgQuerySetDefinition("E", "relation-endpoints", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.AbsoluteIriUtf8),
            new LuxembourgQuerySetDefinition("G", "relation-assertions", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.CompositeLiteralUtf8),
            new LuxembourgQuerySetDefinition("M", null, LuxembourgQuerySetAcquisition.LocalMaterialization, LuxembourgQueryKeyKind.CompositeLiteralUtf8),
            new LuxembourgQuerySetDefinition("O", "iri-objects", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.AbsoluteIriUtf8),
            new LuxembourgQuerySetDefinition("P", "predicates", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.AbsoluteIriUtf8),
            new LuxembourgQuerySetDefinition("R", "typed-resources", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.CompositeLiteralUtf8),
            new LuxembourgQuerySetDefinition("S", "subjects", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.AbsoluteIriUtf8),
            new LuxembourgQuerySetDefinition("T", "types", LuxembourgQuerySetAcquisition.PublisherQuery, LuxembourgQueryKeyKind.AbsoluteIriUtf8),
        };
        return new LuxembourgQueryPlan(
            SchemaId,
            graph,
            SchemeRootIris,
            selectors,
            relations,
            definitions,
            BuildTemplates(graph, SchemeRootIris, selectors, relations),
            new LuxembourgKeysetSuccessorRule(
                "strict_greater_than",
                "canonical_utf8_tuple_ascending",
                6,
                emptySuccessorRequired: true),
            new LuxembourgPageTraversalRule(true, true, true, true));
    }

    private static IReadOnlyList<string> CopySortedUnique(
        IReadOnlyList<string> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Length == 0 || copy.Any(static value => string.IsNullOrEmpty(value)))
        {
            throw new ArgumentException("A query-plan collection must be nonempty.", parameterName);
        }

        var ordered = copy.Order(StringComparer.Ordinal).ToArray();
        if (!copy.SequenceEqual(ordered, StringComparer.Ordinal) ||
            copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException("A query-plan collection must be sorted and duplicate-free.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<LuxembourgQuerySetDefinition> CopySetDefinitions(
        IReadOnlyList<LuxembourgQuerySetDefinition> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Any(static value => value is null) ||
            !copy.Select(static value => value.SetId).SequenceEqual(
                copy.Select(static value => value.SetId).Order(StringComparer.Ordinal),
                StringComparer.Ordinal) ||
            copy.Select(static value => value.SetId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException("Set definitions must have sorted unique identities.", nameof(values));
        }

        if (copy.Any(static value =>
                !Enum.IsDefined(value.Acquisition) || !Enum.IsDefined(value.KeyKind) ||
                (value.Acquisition == LuxembourgQuerySetAcquisition.PublisherQuery) !=
                (value.TemplateId is not null)))
        {
            throw new ArgumentException(
                "Each set must name exactly one publisher query or one local materialization.",
                nameof(values));
        }

        return Array.AsReadOnly(copy);
    }

    private static LuxembourgQueryTemplate[] BuildTemplates(
        LuxembourgDatasetGraphIdentity graph,
        IReadOnlyList<string> schemeRoots,
        IReadOnlyList<string> selectors,
        IReadOnlyList<string> relations)
    {
        var allPredicates = selectors.Concat(relations).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var predicateValues = Values("predicate", allPredicates.Select(static value => $"<{value}>"));
        var relationValues = Values("predicate", relations.Select(static value => $"<{value}>"));
        var rootValues = Values("scheme_root", schemeRoots.Select(LuxembourgQueryText.SparqlString));
        return new[]
        {
            Template("assertion-rows", graph, $"""
                {predicateValues}
                ?subject ?predicate ?object .
                BIND(IF(isIRI(?object), "iri", IF(isLiteral(?object), "literal", "unsupported_blank_node")) AS ?object_kind)
                BIND(IF(isLiteral(?object), STR(DATATYPE(?object)), "") AS ?datatype_iri)
                BIND(IF(isLiteral(?object), LANG(?object), "") AS ?language_tag)
                BIND(IF(isIRI(?subject), STR(?subject), "") AS ?key_1)
                BIND(STR(?predicate) AS ?key_2) BIND(?object_kind AS ?key_3)
                BIND(IF(isIRI(?object) || isLiteral(?object), STR(?object), "") AS ?key_4)
                BIND(?datatype_iri AS ?key_5) BIND(?language_tag AS ?key_6)
                """, "?subject ?predicate ?object ?object_kind ?datatype_iri ?language_tag"),
            Template("controlled-concepts", graph, $"""
                {rootValues}
                ?concept a <http://www.w3.org/2004/02/skos/core#Concept> . FILTER(isIRI(?concept))
                FILTER(STRSTARTS(STR(?concept), ?scheme_root))
                BIND(STR(?concept) AS ?key_1)
                BIND("" AS ?key_2) BIND("" AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """),
            Template("iri-objects", graph, $"""
                {predicateValues}
                ?subject ?predicate ?object . FILTER(isIRI(?object))
                BIND(STR(?object) AS ?key_1)
                BIND("" AS ?key_2) BIND("" AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """),
            Template("predicates", graph, """
                ?subject ?predicate ?object .
                BIND(STR(?predicate) AS ?key_1)
                BIND("" AS ?key_2) BIND("" AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """),
            Template("relation-assertions", graph, $"""
                {relationValues}
                ?subject ?predicate ?object . FILTER(isIRI(?subject) && isIRI(?object))
                BIND(STR(?subject) AS ?key_1) BIND(STR(?predicate) AS ?key_2)
                BIND(STR(?object) AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """, "?subject ?predicate ?object"),
            Template("relation-endpoints", graph, $$"""
                {{relationValues}}
                { ?endpoint ?predicate ?other . } UNION { ?other ?predicate ?endpoint . }
                FILTER(isIRI(?endpoint)) BIND(STR(?endpoint) AS ?key_1)
                BIND("" AS ?key_2) BIND("" AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """),
            Template("subjects", graph, """
                ?subject ?predicate ?object . FILTER(isIRI(?subject))
                BIND(STR(?subject) AS ?key_1)
                BIND("" AS ?key_2) BIND("" AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """),
            Template("typed-resources", graph, """
                ?resource a ?type . FILTER(isIRI(?resource) && isIRI(?type))
                BIND(STR(?type) AS ?key_1) BIND(STR(?resource) AS ?key_2)
                BIND("" AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """),
            Template("types", graph, """
                ?subject a ?type . FILTER(isIRI(?type)) BIND(STR(?type) AS ?key_1)
                BIND("" AS ?key_2) BIND("" AS ?key_3) BIND("" AS ?key_4)
                BIND("" AS ?key_5) BIND("" AS ?key_6)
                """),
        };
    }

    private static LuxembourgQueryTemplate Template(
        string templateId,
        LuxembourgDatasetGraphIdentity graph,
        string traversal,
        string projection = "")
    {
        if (graph.Kind != LuxembourgDatasetGraphKind.DefaultGraph)
        {
            throw new ArgumentException("The measured LU plan is default-graph only.", nameof(graph));
        }

        var rangeSelection = $$"""
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES (?partition_start_1 ?partition_start_2 ?partition_start_3 ?partition_start_4 ?partition_start_5 ?partition_start_6
                      ?partition_end_1 ?partition_end_2 ?partition_end_3 ?partition_end_4 ?partition_end_5 ?partition_end_6) {
                ({partition_start_1:sparql_string} {partition_start_2:sparql_string} {partition_start_3:sparql_string} {partition_start_4:sparql_string} {partition_start_5:sparql_string} {partition_start_6:sparql_string}
                 {partition_end_1:sparql_string} {partition_end_2:sparql_string} {partition_end_3:sparql_string} {partition_end_4:sparql_string} {partition_end_5:sparql_string} {partition_end_6:sparql_string})
              }
            {{Indent(traversal)}}
              FILTER(
                ?key_1 > ?partition_start_1 ||
                (?key_1 = ?partition_start_1 && ?key_2 > ?partition_start_2) ||
                (?key_1 = ?partition_start_1 && ?key_2 = ?partition_start_2 && ?key_3 > ?partition_start_3) ||
                (?key_1 = ?partition_start_1 && ?key_2 = ?partition_start_2 && ?key_3 = ?partition_start_3 && ?key_4 > ?partition_start_4) ||
                (?key_1 = ?partition_start_1 && ?key_2 = ?partition_start_2 && ?key_3 = ?partition_start_3 && ?key_4 = ?partition_start_4 && ?key_5 > ?partition_start_5) ||
                (?key_1 = ?partition_start_1 && ?key_2 = ?partition_start_2 && ?key_3 = ?partition_start_3 && ?key_4 = ?partition_start_4 && ?key_5 = ?partition_start_5 && ?key_6 >= ?partition_start_6)
              )
              FILTER(
                ?key_1 < ?partition_end_1 ||
                (?key_1 = ?partition_end_1 && ?key_2 < ?partition_end_2) ||
                (?key_1 = ?partition_end_1 && ?key_2 = ?partition_end_2 && ?key_3 < ?partition_end_3) ||
                (?key_1 = ?partition_end_1 && ?key_2 = ?partition_end_2 && ?key_3 = ?partition_end_3 && ?key_4 < ?partition_end_4) ||
                (?key_1 = ?partition_end_1 && ?key_2 = ?partition_end_2 && ?key_3 = ?partition_end_3 && ?key_4 = ?partition_end_4 && ?key_5 < ?partition_end_5) ||
                (?key_1 = ?partition_end_1 && ?key_2 = ?partition_end_2 && ?key_3 = ?partition_end_3 && ?key_4 = ?partition_end_4 && ?key_5 = ?partition_end_5 && ?key_6 < ?partition_end_6)
              )
            """;
        var query = $$"""
            SELECT DISTINCT {{projection}} ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 WHERE {
            {{Indent(rangeSelection)}}
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
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
                SELECT DISTINCT {{projection}} ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 WHERE {
                {{Indent(Indent(rangeSelection))}}
                }
              }
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        return new LuxembourgQueryTemplate(templateId, query, count);
    }

    private static string Indent(string value) =>
        string.Join('\n', value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n').Select(static line => "  " + line));

    private static IReadOnlyList<LuxembourgQueryTemplate> CopyQueryTemplates(
        IReadOnlyList<LuxembourgQueryTemplate> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        var identities = copy.Select(static value => value?.TemplateId).ToArray();
        if (copy.Length == 0 || copy.Any(static value => value is null) ||
            identities.Any(static value => string.IsNullOrEmpty(value)) ||
            !identities.SequenceEqual(identities.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            identities.Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException("Query templates must have sorted unique identities.", nameof(values));
        }

        return Array.AsReadOnly(copy);
    }

    private static string Values(string variable, IEnumerable<string> values) =>
        $"VALUES ?{variable} {{ {string.Join(' ', values)} }}";

    private static string[] AcceptedPredicates(LuxembourgVocabularyKind kind) =>
        VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
            .Where(value => value.Kind == kind)
            .Select(static value => value.FullIri)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
