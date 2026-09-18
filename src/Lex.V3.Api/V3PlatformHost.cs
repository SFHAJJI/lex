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
        Operation = operation;
    }

    public string OperationId { get; }

    public string Schema { get; }

    public string SchemaSha256 { get; }

    public JsonElement Parameters { get; }

    internal V3OperationDefinition Operation { get; }
}

internal sealed class V3PlatformOperationResult
{
    public V3PlatformOperationResult(
        V3PlatformOperationRequest request,
        string objectType,
        JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("An operation result must be an object.", nameof(value));
        }

        OperationId = request.OperationId;
        Schema = request.Operation.ResultSchema;
        SchemaSha256 = request.Operation.ResultSchemaSha256;
        ObjectType = objectType;
        Value = value.Clone();
    }

    public string OperationId { get; }

    public string Schema { get; }

    public string SchemaSha256 { get; }

    public string ObjectType { get; }

    public JsonElement Value { get; }
}

internal sealed class V3PlatformOperationRefusal
{
    public V3PlatformOperationRefusal(
        V3PlatformOperationRequest request,
        string code,
        JsonElement helpfulPayload)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (helpfulPayload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A refusal helpful payload must be an object.", nameof(helpfulPayload));
        }

        OperationId = request.OperationId;
        Schema = request.Operation.RefusalSchema;
        Code = code;
        HelpfulPayload = helpfulPayload.Clone();
    }

    public string OperationId { get; }

    public string Schema { get; }

    public string Code { get; }

    public JsonElement HelpfulPayload { get; }
}

internal sealed class V3PlatformOperationOutcome
{
    private V3PlatformOperationOutcome(
        V3EnvelopeContext context,
        V3PlatformOperationResult? result,
        V3PlatformOperationRefusal? refusal)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        if ((result is null) == (refusal is null))
        {
            throw new ArgumentException("An operation outcome must contain exactly one result or refusal.");
        }

        Result = result;
        Refusal = refusal;
    }

    public V3EnvelopeContext Context { get; }

    public V3PlatformOperationResult? Result { get; }

    public V3PlatformOperationRefusal? Refusal { get; }

    public static V3PlatformOperationOutcome Success(
        V3EnvelopeContext context,
        V3PlatformOperationResult result) => new(context, result, null);

    public static V3PlatformOperationOutcome Refused(
        V3EnvelopeContext context,
        V3PlatformOperationRefusal refusal) => new(context, null, refusal);
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
    internal const int MaximumRequestBytes = 1024 * 1024;
    private static readonly JsonDocumentOptions RequestOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
        AllowDuplicateProperties = false,
    };

    private readonly V3OperationRegistry _registry = V3OperationRegistry.Reviewed;
    private readonly V3EnvelopeBuilder _builder = new(V3OperationRegistry.Reviewed);
    private readonly IV3PlatformSchemaDocuments _schemas;

    public V3PlatformHost()
        : this(V3PlatformSchemaDocuments.Reviewed)
    {
    }

    internal V3PlatformHost(IV3PlatformSchemaDocuments schemas)
    {
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
    }

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

    public async Task WriteRestOutcomeAsync(
        HttpResponse response,
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var bytes = ExecuteOutcome(
            requestUtf8,
            requestReference,
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

    public async Task WriteRestRefusalAsync(
        HttpResponse response,
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        V3EnvelopeContext context,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var bytes = ExecuteRefusal(
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

    public Task<V3McpToolResult> CreateMcpRefusalAsync(
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        V3EnvelopeContext context,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        CancellationToken cancellationToken)
    {
        var bytes = ExecuteRefusal(
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
        return ProjectSuccess(request, requestReference, context, execute(request), projection, cancellationToken);
    }

    private byte[] ExecuteOutcome(
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute,
        V3EnvelopeProjectionKind projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execute);
        cancellationToken.ThrowIfCancellationRequested();
        var request = ParseRequest(requestUtf8);
        var outcome = execute(request) ?? throw new InvalidOperationException("The operation returned no outcome.");
        return outcome.Result is not null
            ? ProjectSuccess(
                request,
                requestReference,
                outcome.Context,
                outcome.Result,
                projection,
                cancellationToken)
            : ProjectRefusal(
                request,
                requestReference,
                outcome.Context,
                outcome.Refusal!,
                projection,
                cancellationToken);
    }

    private byte[] ProjectSuccess(
        V3PlatformOperationRequest request,
        string requestReference,
        V3EnvelopeContext context,
        V3PlatformOperationResult result,
        V3EnvelopeProjectionKind projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);
        var operation = _registry.Operation(request.OperationId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(result.OperationId, operation.OperationId, StringComparison.Ordinal) ||
            !string.Equals(result.Schema, operation.ResultSchema, StringComparison.Ordinal) ||
            !string.Equals(result.SchemaSha256, operation.ResultSchemaSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The operation result is not bound to its reviewed schema.");
        }

        if (!operation.ResultObjectTypes.Contains(result.ObjectType, StringComparer.Ordinal))
        {
            throw new ArgumentException("The result object type is not bound to this operation.", nameof(execute));
        }

        using var resultDocument = ResultDocument(result);
        try
        {
            _schemas.ValidateResult(operation, resultDocument.RootElement);
        }
        catch (JsonException exception)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.InternalResponseInvalid,
                "The operation result does not satisfy its reviewed schema.",
                exception);
        }

        var envelope = _builder.Success(
            requestReference,
            operation.OperationId,
            context,
            V3Verdicts.Answer,
            result.Schema,
            result.ObjectType,
            result.Value);
        return V3EnvelopeJson.Project(envelope, _registry, projection);
    }

    private byte[] ExecuteRefusal(
        ReadOnlyMemory<byte> requestUtf8,
        string requestReference,
        V3EnvelopeContext context,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        V3EnvelopeProjectionKind projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(execute);
        cancellationToken.ThrowIfCancellationRequested();

        var request = ParseRequest(requestUtf8);
        return ProjectRefusal(request, requestReference, context, execute(request), projection, cancellationToken);
    }

    private byte[] ProjectRefusal(
        V3PlatformOperationRequest request,
        string requestReference,
        V3EnvelopeContext context,
        V3PlatformOperationRefusal refusal,
        V3EnvelopeProjectionKind projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(refusal);
        var operation = _registry.Operation(request.OperationId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(refusal.OperationId, operation.OperationId, StringComparison.Ordinal) ||
            !string.Equals(refusal.Schema, operation.RefusalSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The operation refusal is not bound to its reviewed schema.");
        }

        using var refusalDocument = RefusalDocument(refusal);
        try
        {
            _schemas.ValidateRefusal(refusalDocument.RootElement);
        }
        catch (JsonException exception)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.InternalResponseInvalid,
                "The operation refusal does not satisfy its reviewed schema.",
                exception);
        }
        var envelope = _builder.Refusal(
            requestReference,
            operation.OperationId,
            context,
            refusal.Schema,
            refusal.Code,
            refusal.HelpfulPayload);
        return V3EnvelopeJson.Project(envelope, _registry, projection);
    }

    private V3PlatformOperationRequest ParseRequest(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.MalformedJson,
                "The operation request is empty.");
        }

        if (utf8.Length > MaximumRequestBytes)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestTooLarge,
                "The operation request exceeds its byte ceiling.");
        }

        using var document = ParseTransportJson(utf8);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "An operation request must be an object.");
        }

        var names = root.EnumerateObject()
            .Select(static member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!names.SequenceEqual(["operation_id", "parameters"], StringComparer.Ordinal))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request members do not match the bound schema.");
        }

        var operationElement = root.GetProperty("operation_id");
        if (operationElement.ValueKind != JsonValueKind.String || operationElement.GetString() is not { } operationId)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation identifier must be a string.");
        }

        V3OperationDefinition operation;
        try
        {
            operation = _registry.Operation(operationId);
        }
        catch (ArgumentException exception)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.UnknownOperation,
                "The operation is not declared by the reviewed registry.",
                exception);
        }

        var parameters = root.GetProperty("parameters");
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.ParametersNotObject,
                "Operation parameters must be an object.");
        }

        try
        {
            _schemas.ValidateRequest(operation, root);
        }
        catch (JsonException exception)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request does not satisfy its reviewed schema.",
                exception);
        }

        return new V3PlatformOperationRequest(operation, parameters);
    }

    private static JsonDocument ParseTransportJson(ReadOnlyMemory<byte> utf8)
    {
        try
        {
            ValidateJsonTokens(utf8.Span);
            return JsonDocument.Parse(utf8, RequestOptions);
        }
        catch (V3TransportFailureException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.MalformedJson,
                "The operation request is not valid JSON.",
                exception);
        }
    }

    private static void ValidateJsonTokens(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(
            utf8,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 33,
                AllowMultipleValues = true,
            });
        var objectMembers = new Stack<HashSet<string>>();
        var roots = 0;

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray &&
                reader.CurrentDepth >= 32)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestTooDeep,
                    "The operation request exceeds its depth ceiling.");
            }

            if (reader.CurrentDepth == 0 && IsValueStart(reader.TokenType) && ++roots > 1)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.TrailingJsonContent,
                    "The operation request contains trailing JSON content.");
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objectMembers.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (!objectMembers.Peek().Add(reader.GetString()!))
                {
                    throw new V3TransportFailureException(
                        V3TransportFailureKind.DuplicateJsonMember,
                        "The operation request contains a duplicate JSON member.");
                }
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                objectMembers.Pop();
            }
        }
    }

    private static bool IsValueStart(JsonTokenType tokenType) => tokenType is
        JsonTokenType.StartObject or
        JsonTokenType.StartArray or
        JsonTokenType.String or
        JsonTokenType.Number or
        JsonTokenType.True or
        JsonTokenType.False or
        JsonTokenType.Null;

    private static JsonDocument ResultDocument(V3PlatformOperationResult result) =>
        JsonDocument.Parse(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                operation_id = result.OperationId,
                object_type = result.ObjectType,
                value = result.Value,
            }));

    private static JsonDocument RefusalDocument(V3PlatformOperationRefusal refusal) =>
        JsonDocument.Parse(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = refusal.Schema,
                code = refusal.Code,
                helpful_payload = refusal.HelpfulPayload,
            }));
}
