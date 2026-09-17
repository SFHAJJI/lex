using System.Text.Json;
using Lex.V3.Contracts.Platform;

namespace Lex.V3.Api;

internal sealed class V3PlatformOperationRequest
{
    public V3PlatformOperationRequest(V3OperationDefinition operation, JsonElement parameters)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Operation parameters must be an object.");
        }

        OperationId = operation.OperationId;
        Schema = operation.RequestSchema;
        SchemaSha256 = operation.RequestSchemaSha256;
        Parameters = parameters.Clone();
    }

    public string OperationId { get; }

    public string Schema { get; }

    public string SchemaSha256 { get; }

    public JsonElement Parameters { get; }
}

internal sealed class V3PlatformOperationResult
{
    public V3PlatformOperationResult(string objectType, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("An operation result must be an object.", nameof(value));
        }

        ObjectType = objectType;
        Value = value.Clone();
    }

    public string ObjectType { get; }

    public JsonElement Value { get; }
}

internal sealed class V3McpToolResult
{
    private readonly byte[] _jsonUtf8;

    public V3McpToolResult(byte[] jsonUtf8)
    {
        ArgumentNullException.ThrowIfNull(jsonUtf8);
        if (jsonUtf8.Length == 0)
        {
            throw new ArgumentException("An MCP tool result cannot be empty.", nameof(jsonUtf8));
        }

        _jsonUtf8 = jsonUtf8.ToArray();
    }

    public string ContentType => "application/json;charset=utf-8";

    public byte[] JsonUtf8 => _jsonUtf8.ToArray();
}

internal sealed class V3PlatformHost
{
    private const int MaximumRequestBytes = 1024 * 1024;
    private static readonly JsonDocumentOptions RequestOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
        AllowDuplicateProperties = false,
    };

    private readonly V3OperationRegistry _registry = V3OperationRegistry.Reviewed;
    private readonly V3EnvelopeBuilder _builder = new(V3OperationRegistry.Reviewed);

    public async Task WriteRestSuccessAsync(
        HttpResponse response,
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        V3EnvelopeContext context,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var bytes = ExecuteSuccess(
            requestUtf8,
            requestReference,
            context,
            execute,
            V3EnvelopeProjectionKind.Rest,
            cancellationToken);
        await BufferedHttpResponse.WritePreparedJsonAsync(
            response,
            StatusCodes.Status200OK,
            "application/json;charset=utf-8",
            bytes,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<V3McpToolResult> CreateMcpSuccessAsync(
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        V3EnvelopeContext context,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        CancellationToken cancellationToken)
    {
        var bytes = ExecuteSuccess(
            requestUtf8,
            requestReference,
            context,
            execute,
            V3EnvelopeProjectionKind.Mcp,
            cancellationToken);
        return Task.FromResult(new V3McpToolResult(bytes));
    }

    private byte[] ExecuteSuccess(
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        V3EnvelopeContext context,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        V3EnvelopeProjectionKind projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(execute);
        cancellationToken.ThrowIfCancellationRequested();

        var request = ParseRequest(requestUtf8);
        var operation = _registry.Operation(request.OperationId);
        var result = execute(request) ?? throw new InvalidOperationException("The operation returned no result.");
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = _builder.Success(
            requestReference,
            operation.OperationId,
            context,
            V3Verdicts.Answer,
            operation.ResultSchema,
            result.ObjectType,
            result.Value);
        return V3EnvelopeJson.Project(envelope, _registry, projection);
    }

    private V3PlatformOperationRequest ParseRequest(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.IsEmpty || utf8.Length > MaximumRequestBytes)
        {
            throw new JsonException("The operation request is empty or exceeds its byte ceiling.");
        }

        using var document = JsonDocument.Parse(utf8, RequestOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("An operation request must be an object.");
        }

        var names = root.EnumerateObject().Select(static member => member.Name).Order().ToArray();
        if (!names.SequenceEqual(["operation_id", "parameters"], StringComparer.Ordinal))
        {
            throw new JsonException("The operation request members do not match the bound schema.");
        }

        var operationId = root.GetProperty("operation_id").GetString();
        if (operationId is null)
        {
            throw new JsonException("The operation identifier must be a string.");
        }

        V3OperationDefinition operation;
        try
        {
            operation = _registry.Operation(operationId);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("The operation is not declared by the reviewed registry.", exception);
        }

        return new V3PlatformOperationRequest(operation, root.GetProperty("parameters"));
    }
}
