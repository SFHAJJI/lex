using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "object_type")]
[JsonDerivedType(typeof(PreviewSyntheticCoordinate), "preview_coordinate")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record PreviewObject
{
    protected PreviewObject(string objectId)
    {
        ObjectId = ContractValidation.RequireIdentifier(objectId, nameof(objectId));
    }

    public string ObjectId { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewSyntheticCoordinate : PreviewObject
{
    [JsonConstructor]
    public PreviewSyntheticCoordinate(
        string objectId,
        bool synthetic,
        string workId,
        string versionKey,
        string anchor,
        BodyHoldingState bodyHoldingState,
        string? body,
        string? bodySha256)
        : base(objectId)
    {
        if (!synthetic)
        {
            throw new ArgumentException("A preview coordinate must remain synthetic.", nameof(synthetic));
        }

        WorkId = RequirePreviewIdentifier(workId, nameof(workId));
        VersionKey = RequirePreviewIdentifier(versionKey, nameof(versionKey));
        Anchor = RequirePreviewIdentifier(anchor, nameof(anchor));
        Synthetic = true;
        BodyHoldingState = bodyHoldingState;

        if (bodyHoldingState == BodyHoldingState.HeldPublic)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(body, nameof(body));
            Body = body;
            BodySha256 = ContractValidation.RequireSha256(bodySha256!, nameof(bodySha256));
        }
        else if (body is not null || bodySha256 is not null)
        {
            throw new ArgumentException("A non-public preview holding cannot carry body bytes.", nameof(body));
        }
    }

    public bool Synthetic { get; }

    public string WorkId { get; }

    public string VersionKey { get; }

    public string Anchor { get; }

    public BodyHoldingState BodyHoldingState { get; }

    public string? Body { get; }

    public string? BodySha256 { get; }

    private static string RequirePreviewIdentifier(string value, string parameterName)
    {
        value = ContractValidation.RequireIdentifier(value, parameterName);
        if (!value.StartsWith("preview:", StringComparison.Ordinal))
        {
            throw new ArgumentException("Synthetic coordinates must use the preview namespace.", parameterName);
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewObjectSet
{
    [JsonConstructor]
    public PreviewObjectSet(
        string schema,
        string objectSetId,
        IReadOnlyList<PreviewObject> objects)
    {
        if (!string.Equals(schema, V3SchemaIds.PreviewObjectSet, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview object-set schema.", nameof(schema));
        }

        Schema = schema;
        ObjectSetId = ContractValidation.RequireIdentifier(objectSetId, nameof(objectSetId));
        var copy = (objects ?? throw new ArgumentNullException(nameof(objects))).ToArray();
        if (copy.Length > PreviewContractLimits.MaximumObjects)
        {
            throw new ArgumentException("The preview object limit was exceeded.", nameof(objects));
        }

        if (copy.Select(static item => item.ObjectId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException("Preview object identifiers must be unique.", nameof(objects));
        }

        Objects = Array.AsReadOnly(copy);
    }

    public string Schema { get; }

    public string ObjectSetId { get; }

    public IReadOnlyList<PreviewObject> Objects { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewRefusalDefinition
{
    [JsonConstructor]
    public PreviewRefusalDefinition(RefusalCode code, IReadOnlyList<string> mandatoryFields)
    {
        if (code != RefusalCode.IdentifierUnknown)
        {
            throw new ArgumentException("The Stage 0 preview registry has one refusal branch.", nameof(code));
        }

        Code = code;
        var copy = (mandatoryFields ?? throw new ArgumentNullException(nameof(mandatoryFields))).ToArray();
        var expected = IdentifierUnknownMandatoryFields;
        if (!copy.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new ArgumentException("The identifier_unknown payload field set is incomplete.", nameof(mandatoryFields));
        }

        MandatoryFields = Array.AsReadOnly(copy);
    }

    public RefusalCode Code { get; }

    public IReadOnlyList<string> MandatoryFields { get; }

    public static ReadOnlyCollection<string> IdentifierUnknownMandatoryFields { get; } = Array.AsReadOnly(
        new[]
        {
            "code",
            "checked_identifier_family",
            "requested_coordinate",
            "publisher_contexts_checked",
            "possible_held_records",
            "official_search_actions",
            "what_would_answer",
            "asserts_absence_of_law",
        });
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewRefusalRegistry
{
    [JsonConstructor]
    public PreviewRefusalRegistry(
        string schema,
        string registryId,
        IReadOnlyList<PreviewRefusalDefinition> entries)
    {
        if (!string.Equals(schema, V3SchemaIds.PreviewRefusalRegistry, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview refusal-registry schema.", nameof(schema));
        }

        Schema = schema;
        RegistryId = ContractValidation.RequireIdentifier(registryId, nameof(registryId));
        var copy = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
        if (copy.Select(static item => item.Code).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("A refusal code can appear only once.", nameof(entries));
        }

        Entries = Array.AsReadOnly(copy);
    }

    public string Schema { get; }

    public string RegistryId { get; }

    public IReadOnlyList<PreviewRefusalDefinition> Entries { get; }

    public static PreviewRefusalRegistry StageZero { get; } = new(
        V3SchemaIds.PreviewRefusalRegistry,
        "s0-04-identifier-boundary",
        new[]
        {
            new PreviewRefusalDefinition(
                RefusalCode.IdentifierUnknown,
                PreviewRefusalDefinition.IdentifierUnknownMandatoryFields),
        });
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewPayload
{
    [JsonConstructor]
    public PreviewPayload(
        string schema,
        PreviewOperationCatalog operationCatalog,
        PreviewRefusalRegistry refusalRegistry,
        PreviewObjectSet objectSet,
        IReadOnlyList<PreviewEnvelope> envelopes)
    {
        if (!string.Equals(schema, V3SchemaIds.PreviewPayload, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview payload schema.", nameof(schema));
        }

        Schema = schema;
        OperationCatalog = operationCatalog ?? throw new ArgumentNullException(nameof(operationCatalog));
        RefusalRegistry = refusalRegistry ?? throw new ArgumentNullException(nameof(refusalRegistry));
        ObjectSet = objectSet ?? throw new ArgumentNullException(nameof(objectSet));
        var copy = (envelopes ?? throw new ArgumentNullException(nameof(envelopes))).ToArray();
        if (copy.Length > PreviewContractLimits.MaximumEnvelopes)
        {
            throw new ArgumentException("The preview envelope limit was exceeded.", nameof(envelopes));
        }

        var activeOperations = operationCatalog.Entries
            .Select(static entry => entry.OperationId)
            .ToHashSet(StringComparer.Ordinal);
        if (copy.Any(envelope => !activeOperations.Contains(envelope.Context.Operation.OperationId)))
        {
            throw new ArgumentException("An envelope cannot reference an inactive operation.", nameof(envelopes));
        }

        Envelopes = Array.AsReadOnly(copy);
    }

    public string Schema { get; }

    public PreviewOperationCatalog OperationCatalog { get; }

    public PreviewRefusalRegistry RefusalRegistry { get; }

    public PreviewObjectSet ObjectSet { get; }

    public IReadOnlyList<PreviewEnvelope> Envelopes { get; }

    public static PreviewPayload CreateStageZero() => new(
        V3SchemaIds.PreviewPayload,
        PreviewOperationCatalog.StageZero,
        PreviewRefusalRegistry.StageZero,
        new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "s0-04-empty",
            Array.Empty<PreviewObject>()),
        Array.Empty<PreviewEnvelope>());
}

public static class PreviewContractLimits
{
    public const int MaximumManifestBytes = 32_768;
    public const int MaximumManifestDepth = 8;
    public const int MaximumManifestProperties = 64;
    public const int MaximumManifestPropertyNameBytes = 64;
    public const int MaximumManifestStringBytes = 4_096;
    public const int MaximumPayloadBytes = 8_388_608;
    public const int MaximumPayloadDepth = 32;
    public const int MaximumPayloadTokens = 100_000;
    public const int MaximumObjectMembers = 128;
    public const int MaximumArrayItems = 4_096;
    public const int MaximumPayloadPropertyNameBytes = 128;
    public const int MaximumPayloadStringBytes = 1_048_576;
    public const int MaximumEnvelopes = 16;
    public const int MaximumObjects = 256;
}
