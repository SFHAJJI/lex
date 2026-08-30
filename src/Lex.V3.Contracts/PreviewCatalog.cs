using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContractReference
{
    [JsonConstructor]
    public ContractReference(string schema, string sha256)
    {
        Schema = ContractValidation.RequireIdentifier(schema, nameof(schema));
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
    }

    public string Schema { get; }

    public string Sha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewOperationDescriptor
{
    [JsonConstructor]
    public PreviewOperationDescriptor(
        string operationId,
        ContractReference request,
        ContractReference success,
        IReadOnlyList<RefusalCode> allowedRefusals,
        string deterministicOrder,
        string capabilityRequirement,
        string restProjection,
        string mcpProjection,
        string htmlProjection)
    {
        if (!V3ContractVocabulary.OperationIds.Contains(operationId, StringComparer.Ordinal))
        {
            throw new ArgumentException("The operation is outside the immutable V3 inventory.", nameof(operationId));
        }

        OperationId = operationId;
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Success = success ?? throw new ArgumentNullException(nameof(success));
        AllowedRefusals = Array.AsReadOnly((allowedRefusals ?? throw new ArgumentNullException(nameof(allowedRefusals))).ToArray());
        DeterministicOrder = ContractValidation.RequireIdentifier(deterministicOrder, nameof(deterministicOrder));
        CapabilityRequirement = ContractValidation.RequireIdentifier(capabilityRequirement, nameof(capabilityRequirement));
        RestProjection = ContractValidation.RequireIdentifier(restProjection, nameof(restProjection));
        McpProjection = ContractValidation.RequireIdentifier(mcpProjection, nameof(mcpProjection));
        HtmlProjection = ContractValidation.RequireIdentifier(htmlProjection, nameof(htmlProjection));
    }

    public string OperationId { get; }

    public ContractReference Request { get; }

    public ContractReference Success { get; }

    public ReadOnlyCollection<RefusalCode> AllowedRefusals { get; }

    public string DeterministicOrder { get; }

    public string CapabilityRequirement { get; }

    public string RestProjection { get; }

    public string McpProjection { get; }

    public string HtmlProjection { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewOperationCatalog
{
    [JsonConstructor]
    public PreviewOperationCatalog(
        string schema,
        string catalogId,
        IReadOnlyList<PreviewOperationDescriptor> entries)
    {
        if (!string.Equals(schema, V3SchemaIds.PreviewOperationCatalog, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview operation catalog schema.", nameof(schema));
        }

        Schema = schema;
        CatalogId = ContractValidation.RequireIdentifier(catalogId, nameof(catalogId));
        var copy = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
        if (copy.Select(static entry => entry.OperationId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException("An operation can appear only once in a catalog.", nameof(entries));
        }

        Entries = Array.AsReadOnly(copy);
    }

    public string Schema { get; }

    public string CatalogId { get; }

    public ReadOnlyCollection<PreviewOperationDescriptor> Entries { get; }

    public static PreviewOperationCatalog StageZero { get; } = new(
        V3SchemaIds.PreviewOperationCatalog,
        "s0-04-empty",
        Array.Empty<PreviewOperationDescriptor>());
}
