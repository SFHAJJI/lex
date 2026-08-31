using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticResolveRequestContract
{
    public const string Identity = "lex-v3-synthetic-resolve-request/1";
    public const string DigestDomain = "lex-v3-s0-05-resolve-request";
    public const string ProductPath = "/api/v3-preview/resolve";
    public const int MaximumApplicationRawTargetByteCount = 2048;
    public const string HeldRawTarget =
        "/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview";
    public const string CandidateRawTarget =
        "/api/v3-preview/resolve?family=historical_legal_id&coordinate=historical_legal_id%3Asynthetic-preview";
    public const string ReadyRawTarget = "/health/ready";
    private const string Descriptor =
        "{\"contract_id\":\"lex-v3-synthetic-resolve-request/1\",\"method\":\"GET\"," +
        "\"maximum_application_raw_target_bytes\":2048," +
        "\"product_raw_targets\":[\"/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview\"," +
        "\"/api/v3-preview/resolve?family=historical_legal_id&coordinate=historical_legal_id%3Asynthetic-preview\"]," +
        "\"readiness_method\":\"GET\",\"readiness_target\":\"/health/ready\"}";

    [JsonConstructor]
    public SyntheticResolveRequestContract(
        string contractId,
        string method,
        int maximumApplicationRawTargetBytes,
        IReadOnlyList<string> productRawTargets,
        string readinessMethod,
        string readinessTarget,
        string sha256)
    {
        var targets = (productRawTargets ?? throw new ArgumentNullException(nameof(productRawTargets))).ToArray();
        if (!string.Equals(contractId, Identity, StringComparison.Ordinal) ||
            !string.Equals(method, "GET", StringComparison.Ordinal) ||
            maximumApplicationRawTargetBytes != MaximumApplicationRawTargetByteCount ||
            !targets.SequenceEqual(new[] { HeldRawTarget, CandidateRawTarget }, StringComparer.Ordinal) ||
            !string.Equals(readinessMethod, "GET", StringComparison.Ordinal) ||
            !string.Equals(readinessTarget, ReadyRawTarget, StringComparison.Ordinal) ||
            !string.Equals(sha256, ComputeSha256(), StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic resolve request contract must match version 1 exactly.");
        }

        ContractId = contractId;
        Method = method;
        MaximumApplicationRawTargetBytes = maximumApplicationRawTargetBytes;
        ProductRawTargets = Array.AsReadOnly(targets);
        ReadinessMethod = readinessMethod;
        ReadinessTarget = readinessTarget;
        Sha256 = sha256;
    }

    public string ContractId { get; }

    public string Method { get; }

    public int MaximumApplicationRawTargetBytes { get; }

    public IReadOnlyList<string> ProductRawTargets { get; }

    public string ReadinessMethod { get; }

    public string ReadinessTarget { get; }

    public string Sha256 { get; }

    [JsonIgnore]
    public string CanonicalDescriptor => Descriptor;

    public static SyntheticResolveRequestContract V1 { get; } = new(
        Identity,
        "GET",
        MaximumApplicationRawTargetByteCount,
        new[] { HeldRawTarget, CandidateRawTarget },
        "GET",
        ReadyRawTarget,
        ComputeSha256());

    private static string ComputeSha256() => DomainDigest(DigestDomain, Descriptor);

    internal static string DomainDigest(string domain, string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(domain + "\0" + value)))
            .ToLowerInvariant();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceScope
{
    public const string DigestDomain = "lex-v3-s0-05-scope";
    private const string Descriptor =
        "{\"publisher\":\"lu-legilux\",\"complete\":true," +
        "\"upstream_health\":\"not_applicable_synthetic\"," +
        "\"enumerated_members\":[\"eli/synthetic-preview\"]}";

    [JsonConstructor]
    public SyntheticSliceScope(
        PublisherId publisher,
        bool complete,
        PreviewUpstreamHealth upstreamHealth,
        IReadOnlyList<string> enumeratedMembers,
        string sha256)
    {
        var members = (enumeratedMembers ?? throw new ArgumentNullException(nameof(enumeratedMembers))).ToArray();
        if (publisher != PublisherId.LuLegilux ||
            !complete ||
            upstreamHealth != PreviewUpstreamHealth.NotApplicableSynthetic ||
            !members.SequenceEqual(new[] { ContractValidation.SyntheticEliCoordinate }, StringComparer.Ordinal) ||
            !string.Equals(sha256, SyntheticResolveRequestContract.DomainDigest(DigestDomain, Descriptor), StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic scope must be the exact complete one-member LU scope.");
        }

        Publisher = publisher;
        Complete = true;
        UpstreamHealth = upstreamHealth;
        EnumeratedMembers = Array.AsReadOnly(members);
        Sha256 = sha256;
    }

    public PublisherId Publisher { get; }

    public bool Complete { get; }

    public PreviewUpstreamHealth UpstreamHealth { get; }

    public IReadOnlyList<string> EnumeratedMembers { get; }

    public string Sha256 { get; }

    [JsonIgnore]
    public string CanonicalDescriptor => Descriptor;

    public static SyntheticSliceScope CompleteLu { get; } = new(
        PublisherId.LuLegilux,
        complete: true,
        PreviewUpstreamHealth.NotApplicableSynthetic,
        new[] { ContractValidation.SyntheticEliCoordinate },
        SyntheticResolveRequestContract.DomainDigest(DigestDomain, Descriptor));
}

public enum SyntheticSliceBlobKind
{
    [JsonStringEnumMemberName("source_transport")]
    SourceTransport,

    [JsonStringEnumMemberName("derived_text")]
    DerivedText,

    [JsonStringEnumMemberName("sqlite_index")]
    SqliteIndex,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceBlobDescriptor
{
    [JsonConstructor]
    public SyntheticSliceBlobDescriptor(
        SyntheticSliceBlobKind kind,
        string sha256,
        long bytes,
        string mediaType)
    {
        ContractValidation.RequireDefined(kind, nameof(kind));
        var (expectedMediaType, maximumBytes) = kind switch
        {
            SyntheticSliceBlobKind.SourceTransport =>
                ("application/octet-stream", SyntheticSliceContractLimits.MaximumSourceBytes),
            SyntheticSliceBlobKind.DerivedText =>
                ("text/plain;charset=utf-8", SyntheticSliceContractLimits.MaximumDerivedBytes),
            SyntheticSliceBlobKind.SqliteIndex =>
                ("application/vnd.sqlite3", SyntheticSliceContractLimits.MaximumSqliteBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (!string.Equals(mediaType, expectedMediaType, StringComparison.Ordinal))
        {
            throw new ArgumentException("The blob media type does not match its kind.", nameof(mediaType));
        }

        if (bytes is <= 0 || bytes > maximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        Kind = kind;
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
        Bytes = bytes;
        MediaType = mediaType;
    }

    public SyntheticSliceBlobKind Kind { get; }

    public string Sha256 { get; }

    public long Bytes { get; }

    public string MediaType { get; }
}

public static class SyntheticSliceOperationCatalog
{
    public const string CatalogId = "s0-05-resolve-only";

    public static PreviewOperationCatalog Create(string envelopeSchemaSha256) => new(
        V3SchemaIds.PreviewOperationCatalog,
        CatalogId,
        new[]
        {
            new PreviewOperationDescriptor(
                "resolve",
                new ContractReference(
                    SyntheticResolveRequestContract.Identity,
                    SyntheticResolveRequestContract.V1.Sha256),
                new ContractReference(
                    V3SchemaIds.SyntheticResolveEnvelope,
                    ContractValidation.RequireSha256(envelopeSchemaSha256, nameof(envelopeSchemaSha256))),
                new[] { RefusalCode.IdentifierUnknown },
                "exact_identifier",
                "synthetic_preview_only",
                "available",
                "not_built",
                "not_built"),
        });
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceIndexStamp
{
    public const string SchemaIdentity = "lex-v3-synthetic-sqlite/1";

    [JsonConstructor]
    public SyntheticSliceIndexStamp(
        string schema,
        string ddlSha256,
        string sqliteVersion,
        string sqliteSourceId,
        string compileOptionsSha256,
        string logicalRowsSha256,
        string scopeSha256,
        string buildId)
    {
        if (!string.Equals(schema, SchemaIdentity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic index stamp schema is fixed.", nameof(schema));
        }

        Schema = schema;
        DdlSha256 = ContractValidation.RequireSha256(ddlSha256, nameof(ddlSha256));
        SqliteVersion = ContractValidation.RequireIdentifier(sqliteVersion, nameof(sqliteVersion));
        SqliteSourceId = ContractValidation.RequireIdentifier(sqliteSourceId, nameof(sqliteSourceId));
        CompileOptionsSha256 = ContractValidation.RequireSha256(
            compileOptionsSha256,
            nameof(compileOptionsSha256));
        LogicalRowsSha256 = ContractValidation.RequireSha256(logicalRowsSha256, nameof(logicalRowsSha256));
        ScopeSha256 = ContractValidation.RequireSha256(scopeSha256, nameof(scopeSha256));
        BuildId = ContractValidation.RequireSha256(buildId, nameof(buildId));
    }

    public string Schema { get; }

    public string DdlSha256 { get; }

    public string SqliteVersion { get; }

    public string SqliteSourceId { get; }

    public string CompileOptionsSha256 { get; }

    public string LogicalRowsSha256 { get; }

    public string ScopeSha256 { get; }

    public string BuildId { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceControl
{
    [JsonConstructor]
    public SyntheticSliceControl(
        string schema,
        string schemaResource,
        SyntheticResolveRequestContract resolveRequestContract,
        PreviewOperationCatalog operationCatalog,
        PreviewRefusalRegistry refusalRegistry,
        SyntheticSliceSchemaMember objectSetSchema,
        SyntheticNormalizationProfile normalizationProfile,
        SyntheticSliceScope scope,
        PreviewSnapshotReference snapshot,
        ComponentIdentity builder,
        SyntheticSliceIndexStamp indexStamp,
        IReadOnlyList<SyntheticSliceBlobDescriptor> blobs)
    {
        if (!string.Equals(schema, V3SchemaIds.SyntheticSliceControl, StringComparison.Ordinal) ||
            !string.Equals(schemaResource, V3SchemaResourceIds.SyntheticSliceControl, StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic control schema identity is fixed.");
        }

        ArgumentNullException.ThrowIfNull(resolveRequestContract);
        ArgumentNullException.ThrowIfNull(operationCatalog);
        ValidateCatalog(operationCatalog);
        ArgumentNullException.ThrowIfNull(refusalRegistry);
        if (!string.Equals(
                ContractJson.Serialize(refusalRegistry),
                ContractJson.Serialize(PreviewRefusalRegistry.StageZero),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic slice reuses the exact S0-04 refusal registry.", nameof(refusalRegistry));
        }

        ArgumentNullException.ThrowIfNull(objectSetSchema);
        if (!string.Equals(objectSetSchema.Schema, V3SchemaIds.PreviewObjectSet, StringComparison.Ordinal))
        {
            throw new ArgumentException("The control must bind the reused object-set schema.", nameof(objectSetSchema));
        }

        ArgumentNullException.ThrowIfNull(normalizationProfile);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(indexStamp);
        if (!string.Equals(indexStamp.ScopeSha256, scope.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The index stamp and control must bind the same scope.", nameof(indexStamp));
        }

        var blobCopy = (blobs ?? throw new ArgumentNullException(nameof(blobs))).ToArray();
        if (blobCopy.Any(static blob => blob is null) ||
            !blobCopy.Select(static blob => blob.Kind).SequenceEqual(
                new[]
                {
                    SyntheticSliceBlobKind.SourceTransport,
                    SyntheticSliceBlobKind.DerivedText,
                    SyntheticSliceBlobKind.SqliteIndex,
                }))
        {
            throw new ArgumentException("The control must bind exactly source, derived, then SQLite.", nameof(blobs));
        }

        Schema = schema;
        SchemaResource = schemaResource;
        ResolveRequestContract = resolveRequestContract;
        OperationCatalog = operationCatalog;
        RefusalRegistry = refusalRegistry;
        ObjectSetSchema = objectSetSchema;
        NormalizationProfile = normalizationProfile;
        Scope = scope;
        Snapshot = snapshot;
        Builder = builder;
        IndexStamp = indexStamp;
        Blobs = Array.AsReadOnly(blobCopy);
    }

    public string Schema { get; }

    public string SchemaResource { get; }

    public SyntheticResolveRequestContract ResolveRequestContract { get; }

    public PreviewOperationCatalog OperationCatalog { get; }

    public PreviewRefusalRegistry RefusalRegistry { get; }

    public SyntheticSliceSchemaMember ObjectSetSchema { get; }

    public SyntheticNormalizationProfile NormalizationProfile { get; }

    public SyntheticSliceScope Scope { get; }

    public PreviewSnapshotReference Snapshot { get; }

    public ComponentIdentity Builder { get; }

    public SyntheticSliceIndexStamp IndexStamp { get; }

    public IReadOnlyList<SyntheticSliceBlobDescriptor> Blobs { get; }

    private static void ValidateCatalog(PreviewOperationCatalog catalog)
    {
        if (!string.Equals(catalog.CatalogId, SyntheticSliceOperationCatalog.CatalogId, StringComparison.Ordinal) ||
            catalog.Entries.Count != 1)
        {
            throw new ArgumentException("The synthetic catalog must contain resolve exactly once.", nameof(catalog));
        }

        var operation = catalog.Entries[0];
        if (!string.Equals(operation.OperationId, "resolve", StringComparison.Ordinal) ||
            !string.Equals(operation.Request.Schema, SyntheticResolveRequestContract.Identity, StringComparison.Ordinal) ||
            !string.Equals(operation.Request.Sha256, SyntheticResolveRequestContract.V1.Sha256, StringComparison.Ordinal) ||
            !string.Equals(operation.Success.Schema, V3SchemaIds.SyntheticResolveEnvelope, StringComparison.Ordinal) ||
            !operation.AllowedRefusals.SequenceEqual(new[] { RefusalCode.IdentifierUnknown }) ||
            !string.Equals(operation.DeterministicOrder, "exact_identifier", StringComparison.Ordinal) ||
            !string.Equals(operation.CapabilityRequirement, "synthetic_preview_only", StringComparison.Ordinal) ||
            !string.Equals(operation.RestProjection, "available", StringComparison.Ordinal) ||
            !string.Equals(operation.McpProjection, "not_built", StringComparison.Ordinal) ||
            !string.Equals(operation.HtmlProjection, "not_built", StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic resolve operation contract is not exact.", nameof(catalog));
        }
    }
}
