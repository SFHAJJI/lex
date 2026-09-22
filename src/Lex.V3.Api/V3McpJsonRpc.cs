using System.Buffers;
using System.Text.Json;
using Lex.V3.Contracts.Platform;

namespace Lex.V3.Api;

/// <summary>
/// S4-A01 slice 1: one MCP tool, <c>resolve</c>, over JSON-RPC 2.0. No stdio, no process, no
/// corpus — a pure function from a request payload to a response payload, so a framing or shape
/// defect is caught here and never needs a live client to reproduce.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reuses the REST path's own plumbing, not a parallel one.</b> A <c>tools/call</c> for
/// <c>resolve</c> is rewritten as the same request document the REST route validates
/// (<c>{"operation_id":"resolve","parameters":&lt;arguments&gt;}</c>) and handed to
/// <see cref="V3PlatformHost.CreateMcpOutcomeAsync"/>, which does the same schema validation,
/// registry lookup and envelope construction <see cref="V3PlatformHost.WriteRestOutcomeAsync(Microsoft.AspNetCore.Http.HttpResponse,ReadOnlyMemory{byte},string,Func{V3PlatformOperationRequest,V3PlatformOperationOutcome},CancellationToken)"/>
/// does for REST. A defect in request validation or envelope shape shows up on both transports at
/// once; nothing about the answer is computed twice.
/// </para>
/// <para>
/// <b>A reviewed refusal is not a tool error.</b> The REST route answers a refusal with HTTP 200:
/// the operation ran and correctly determined it must refuse, which is not a transport failure.
/// The MCP analogue is <c>isError: false</c> with the refusal envelope as the result — a tool
/// failure (<c>isError: true</c>) is reserved for something this slice does not have a case for
/// yet, since every outcome resolve produces today is a reviewed success or a reviewed refusal.
/// </para>
/// <para>
/// <b>Protocol version is not negotiated in this slice.</b> <see cref="ProtocolVersion"/> is
/// always echoed back verbatim regardless of what the client requested. Real negotiation (refuse
/// or downgrade to a version the client did not offer) is not needed to prove the transport shape
/// and is left to whichever slice exposes this outside a test.
/// </para>
/// </remarks>
internal static class V3McpJsonRpc
{
    public const string ProtocolVersion = "2025-06-18";
    public const string ServerName = "lex-v3";

    private const string ToolName = "resolve";

    // The tool-call argument shape, transcribed from schemas/v3-platform/resolve-request.schema.json's
    // "parameters" sub-schema. V3McpJsonRpcTests.TheResolveToolInputSchemaMatchesTheReviewedRequestSchema
    // reads that file directly and fails if this literal drifts from it.
    private const string ResolveInputSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "identifier": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S"
            }
          },
          "required": ["identifier"]
        }
        """;

    /// <summary>
    /// One JSON-RPC request in, at most one JSON-RPC response out. Never throws for a malformed or
    /// unsupported request; every failure this function can identify is a JSON-RPC error object in
    /// the response, per the JSON-RPC 2.0 and MCP specifications.
    /// </summary>
    /// <returns>
    /// The response bytes, or <see langword="null"/> when nothing must be written. JSON-RPC 2.0 §4.1
    /// defines a Notification as a Request <b>object</b> with no <c>id</c> member and says the server
    /// <c>MUST NOT</c> reply to one, including one whose method is unknown or whose params are
    /// invalid — the client has no outstanding request to match a reply to and, by the same section,
    /// is not informed of errors on a notification by design. MCP's own lifecycle relies on this: the
    /// client's <c>notifications/initialized</c> after a successful <c>initialize</c> carries no
    /// <c>id</c>. <c>id: null</c> is used for every other failure this function can identify before it
    /// has read an id: a document that is not JSON at all (§5's own example), and a document that is
    /// valid JSON but not an object — a JSON array, string, number or literal cannot be a Notification,
    /// only a malformed Request, and a malformed Request still gets a reply.
    /// </returns>
    public static async Task<byte[]?> HandleAsync(
        byte[] requestUtf8,
        V3PlatformHost host,
        V3CorpusMount? corpusMount,
        Func<DateTimeOffset> utcNow,
        string requestReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUtf8);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(utcNow);
        ArgumentNullException.ThrowIfNull(requestReference);

        JsonElement id;
        string? method;
        JsonElement parameters;
        try
        {
            using var document = JsonDocument.Parse(requestUtf8);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                // Valid JSON, but not a Request object at all -- §4.1's Notification is defined only
                // for a Request object with no id member, so a non-object cannot be one. It is simply
                // an Invalid Request, and TryGetProperty below is not legal to call on it, so this
                // must be checked first, not folded into the object-shape checks that follow.
                return Error(NullId, -32600, "Invalid Request");
            }

            if (!root.TryGetProperty("id", out var idElement))
            {
                // An object with no id member at all: a Notification, whatever else is wrong with it.
                // MUST NOT reply, per §4.1 — not even to report that its method or params are invalid.
                return null;
            }

            id = Clone(idElement);
            if (!root.TryGetProperty("jsonrpc", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                version.GetString() != "2.0" ||
                !root.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String)
            {
                return Error(id, -32600, "Invalid Request");
            }

            method = methodElement.GetString();
            parameters = root.TryGetProperty("params", out var paramsElement)
                ? Clone(paramsElement)
                : Empty();
        }
        catch (JsonException)
        {
            // The document itself did not parse, so an id (if any) could not be read either. This is
            // the one case JSON-RPC 2.0 §5 reserves id: null for, and it always gets a reply.
            return Error(NullId, -32700, "Parse error");
        }

        try
        {
            return method switch
            {
                "initialize" => Result(id, Initialize()),
                "tools/list" => Result(id, ToolsList()),
                "tools/call" => await ToolsCallAsync(
                        id, parameters, host, corpusMount, utcNow, requestReference, cancellationToken)
                    .ConfigureAwait(false),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private static async Task<byte[]> ToolsCallAsync(
        JsonElement id,
        JsonElement parameters,
        V3PlatformHost host,
        V3CorpusMount? corpusMount,
        Func<DateTimeOffset> utcNow,
        string requestReference,
        CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return Error(id, -32602, "Invalid params: 'name' is required");
        }

        var name = nameElement.GetString();
        if (!string.Equals(name, ToolName, StringComparison.Ordinal))
        {
            return Error(id, -32602, $"Unknown tool: {name}");
        }

        var arguments = parameters.TryGetProperty("arguments", out var argumentsElement) &&
                         argumentsElement.ValueKind == JsonValueKind.Object
            ? argumentsElement
            : Empty();
        var operationRequestUtf8 = BuildOperationRequest(arguments);

        try
        {
            // Same refusal REST gives when no corpus is mounted (V3ApiHandler.HandleRefusalAsync),
            // reached through the same host method resolve's success path uses below, so both
            // branches of this tool call go through V3PlatformHost and never hand-build an envelope.
            var toolResult = corpusMount is null
                ? await host.CreateMcpRefusalAsync(
                        operationRequestUtf8,
                        requestReference,
                        V3ApiHandler.UnmountedLuxembourgContext(utcNow),
                        V3ApiHandler.NoCorpusMounted,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await host.CreateMcpOutcomeAsync(
                        operationRequestUtf8,
                        requestReference,
                        request => corpusMount.Resolve(request, utcNow()),
                        cancellationToken)
                    .ConfigureAwait(false);
            using var envelope = JsonDocument.Parse(toolResult.JsonUtf8);
            return Result(id, ToolResult(isError: false, Clone(envelope.RootElement)));
        }
        catch (V3TransportFailureException exception)
        {
            // A malformed request document (schema-invalid arguments) never reaches the reviewed
            // envelope; that is a transport-layer refusal below the envelope on REST too, so it is
            // a JSON-RPC protocol error here rather than a false "the tool ran and this is its
            // answer".
            return Error(id, -32602, $"Invalid params: {exception.Kind}");
        }
    }

    private static byte[] BuildOperationRequest(JsonElement arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("operation_id", ToolName);
            writer.WritePropertyName("parameters");
            arguments.WriteTo(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static JsonElement Initialize()
    {
        using var document = JsonDocument.Parse(
            $$"""
            {
              "protocolVersion": "{{ProtocolVersion}}",
              "capabilities": { "tools": { "listChanged": false } },
              "serverInfo": { "name": "{{ServerName}}", "version": "1" }
            }
            """);
        return Clone(document.RootElement);
    }

    private static JsonElement ToolsList()
    {
        using var inputSchema = JsonDocument.Parse(ResolveInputSchemaJson);
        var writer = new ArrayBufferWriter<byte>();
        using (var jsonWriter = new Utf8JsonWriter(writer))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("tools");
            jsonWriter.WriteStartArray();
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("name", ToolName);
            jsonWriter.WriteString(
                "description",
                "Resolve a Luxembourg legal-act identifier or a hash-pinned permalink to the work and expression it names.");
            jsonWriter.WritePropertyName("inputSchema");
            inputSchema.RootElement.WriteTo(jsonWriter);
            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndArray();
            jsonWriter.WriteEndObject();
        }

        using var document = JsonDocument.Parse(writer.WrittenMemory);
        return Clone(document.RootElement);
    }

    /// <summary>
    /// The MCP tool-result wrapping (spec 2025-06-18): unstructured text (required, for backward
    /// compatibility) and structured content, both carrying the same envelope. <paramref name="isError"/>
    /// marks a tool execution failure, never a reviewed refusal — see the class remarks.
    /// </summary>
    private static JsonElement ToolResult(bool isError, JsonElement envelope)
    {
        var writer = new ArrayBufferWriter<byte>();
        using (var jsonWriter = new Utf8JsonWriter(writer))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("content");
            jsonWriter.WriteStartArray();
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("type", "text");
            jsonWriter.WriteString("text", JsonSerializer.Serialize(envelope));
            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndArray();
            jsonWriter.WritePropertyName("structuredContent");
            envelope.WriteTo(jsonWriter);
            jsonWriter.WriteBoolean("isError", isError);
            jsonWriter.WriteEndObject();
        }

        using var document = JsonDocument.Parse(writer.WrittenMemory);
        return Clone(document.RootElement);
    }

    private static byte[] Result(JsonElement? id, JsonElement result)
    {
        var writer = new ArrayBufferWriter<byte>();
        using (var jsonWriter = new Utf8JsonWriter(writer))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("jsonrpc", "2.0");
            jsonWriter.WritePropertyName("id");
            WriteId(jsonWriter, id);
            jsonWriter.WritePropertyName("result");
            result.WriteTo(jsonWriter);
            jsonWriter.WriteEndObject();
        }

        return writer.WrittenSpan.ToArray();
    }

    private static byte[] Error(JsonElement? id, int code, string message)
    {
        var writer = new ArrayBufferWriter<byte>();
        using (var jsonWriter = new Utf8JsonWriter(writer))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("jsonrpc", "2.0");
            jsonWriter.WritePropertyName("id");
            WriteId(jsonWriter, id);
            jsonWriter.WritePropertyName("error");
            jsonWriter.WriteStartObject();
            jsonWriter.WriteNumber("code", code);
            jsonWriter.WriteString("message", message);
            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndObject();
        }

        return writer.WrittenSpan.ToArray();
    }

    private static void WriteId(Utf8JsonWriter writer, JsonElement? id)
    {
        if (id is { } value)
        {
            value.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static readonly JsonElement? NullId = null;

    private static JsonElement Clone(JsonElement value) => value.Clone();

    private static JsonElement Empty()
    {
        using var document = JsonDocument.Parse("{}");
        return Clone(document.RootElement);
    }
}
