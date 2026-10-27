using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;
using Lex.V3.Contracts.Platform;

namespace Lex.V3.Api;

internal interface IV3PlatformSchemaDocuments
{
    void ValidateRequest(V3OperationDefinition operation, JsonElement document);

    void ValidateResult(V3OperationDefinition operation, JsonElement document);

    void ValidateRefusal(JsonElement document);
}

internal sealed class V3PlatformSchemaDocuments : IV3PlatformSchemaDocuments
{
    private const string ResourcePrefix = "Lex.V3.Api.PlatformSchemas.";
    private readonly IReadOnlyDictionary<string, JsonSchema> _schemas;

    private V3PlatformSchemaDocuments(IReadOnlyDictionary<string, JsonSchema> schemas)
    {
        _schemas = schemas;
    }

    public static V3PlatformSchemaDocuments Reviewed { get; } = LoadReviewed();

    public void ValidateRequest(V3OperationDefinition operation, JsonElement document)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Validate(operation.RequestSchema, document, "request");
    }

    public void ValidateResult(V3OperationDefinition operation, JsonElement document)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Validate(operation.ResultSchema, document, "result");
    }

    /// <summary>
    /// The refusal against the refusal schema, and its payload against its code's payload schema (<c>lex-v3-{code}-payload/1</c>,
    /// the plan's item 6.10), each the document the registry's digest names, so a payload that lacks a mandated field or sends
    /// one in another type never leaves the server as an answer.
    /// </summary>
    public void ValidateRefusal(JsonElement document)
    {
        Validate(V3OperationRegistry.RefusalSchema, document, "refusal");
        var code = document.GetProperty("code").GetString()!;
        Validate(V3OperationRegistry.Reviewed.RefusalPayloadSchema(code), document.GetProperty("helpful_payload"), "refusal payload");
    }

    private void Validate(string schemaId, JsonElement document, string role)
    {
        if (!_schemas.TryGetValue(schemaId, out var schema))
        {
            throw new InvalidOperationException($"The reviewed {role} schema document is unavailable.");
        }

        var result = schema.Evaluate(
            document,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
        if (!result.IsValid)
        {
            throw new JsonException($"The operation {role} does not satisfy its reviewed schema document.");
        }
    }

    private static V3PlatformSchemaDocuments LoadReviewed()
    {
        var assembly = typeof(V3PlatformSchemaDocuments).Assembly;
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        foreach (var operation in V3OperationRegistry.Reviewed.Operations)
        {
            var fileStem = operation.OperationId.Replace('_', '-');
            Add(
                schemas,
                assembly,
                operation.RequestSchema,
                $"{fileStem}-request.schema.json",
                operation.RequestSchemaSha256);
            Add(
                schemas,
                assembly,
                operation.ResultSchema,
                $"{fileStem}-result.schema.json",
                operation.ResultSchemaSha256);
        }

        Add(
            schemas,
            assembly,
            V3OperationRegistry.RefusalSchema,
            "refusal.schema.json",
            V3OperationRegistry.Reviewed.RefusalSchemaSha256);
        foreach (var code in V3OperationRegistry.Reviewed.RefusalCodes)
        {
            var schemaId = V3OperationRegistry.Reviewed.RefusalPayloadSchema(code);
            Add(
                schemas,
                assembly,
                schemaId,
                V3PlatformSchemaExporter.FileNameFor(schemaId),
                V3OperationRegistry.Reviewed.RefusalPayloadSchemaSha256(code));
        }

        return new V3PlatformSchemaDocuments(schemas);
    }

    private static void Add(
        IDictionary<string, JsonSchema> schemas,
        Assembly assembly,
        string schemaId,
        string fileName,
        string expectedSha256)
    {
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"The reviewed schema document {fileName} is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The reviewed schema document {fileName} has drifted.");
        }

        var schema = JsonSchema.FromText(
            Encoding.UTF8.GetString(bytes),
            new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = new SchemaRegistry(),
            });
        if (!schemas.TryAdd(schemaId, schema))
        {
            throw new InvalidOperationException($"The reviewed schema identity {schemaId} is repeated.");
        }
    }
}
