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

    /// <summary>
    /// One tool per served REST operation, named by its operation id, described here, and taking as
    /// <c>inputSchema</c> exactly the <c>parameters</c> sub-schema of the operation's reviewed request
    /// schema (<see cref="V3PlatformSchemaExporter.ExportRequestUtf8"/>), so a tool call is the same
    /// request the REST route reads and the same envelope comes back.
    /// </summary>
    private static string DescriptionOf(string operationId) => operationId switch
    {
        "resolve" => "Resolve a Luxembourg legal-act identifier or a hash-pinned permalink to the work and expression it names.",
        "as_of" => "The state of a Luxembourg act as the publisher applied it on a date, with its articles.",
        "timeline" => "Every state of a Luxembourg act the index holds, in publisher applicability order.",
        "article_history" => "The history of one article of a Luxembourg act across its states.",
        "diff" => "The article-level difference between the states of a Luxembourg act on two dates.",
        "changes_in_period" => "The Luxembourg states that began in a period, for a work or across the index.",
        "in_force_on" => "The Luxembourg works with a state applicable on a date.",
        "search" => "Search the held Luxembourg article text, strict or relaxed, with the quotes that answer; for one EU work named by identifier, its held wording.",
        "coverage" => "What this mount holds and serves, named, and what it does not.",
        "provenance" => "The retained sources, rule profiles and digests behind a Luxembourg state.",
        "dossier" => "The work record of a Luxembourg act as the index holds it.",
        "citation" => "The references the publisher wrote in the text of a Luxembourg state's articles.",
        "cited_by" => "The references, in any held Luxembourg state, whose target is exactly this work.",
        "verify" => "Verify a hash-pinned permalink against the state digest the index holds, or read the current digests to pin.",
        "relations" => "The reference edges of a Luxembourg work in both directions, as one ordered, paged list.",
        "evidence_bundle" => "What a quote of a Luxembourg state needs: every article's text with its digests, the state's permalink, digests, sources and official identities, composed under the rights rule.",
        "classification" => "The publisher's typed facts about a Luxembourg work, verbatim: document type, RDF types, legal value, responsible body, historical identifiers, publication and document dates.",
        "manifestation" => "The publisher's manifestations of a Luxembourg work's expressions with their formats and items, the retained one marked with its body digest.",
        "status_on" => "The publisher's force assertions about a Luxembourg work (in-force status, entry into force, no longer in force), verbatim, beside the state applicable on a date, with one fixed reading of the dates.",
        "browse" => "The Luxembourg works this mount holds, one ordered and paged list with their identifiers, languages, state dates and the publisher's document types; filter by type or language.",
        "ask" => "The contained assistant (Decisions 51 and 91): every question answers the typed presentation result assistant_v3_unavailable, naming the deterministic operations that answer from held law; no model answers.",
        "events" => "The Luxembourg event log, polled by cursor, at least once: this build's genesis log holds one first_sighting per held state, with no observation time.",
        "answer_drift" => "The past dated answers a publisher revision invalidated: none can be enumerated from a genesis log, and the answer says why rather than asserting that nothing drifted.",
        _ => throw new InvalidOperationException($"The served operation {operationId} has no MCP tool description."),
    };

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

            var hasId = root.TryGetProperty("id", out var idElement);
            var idKindIsValid = !hasId || idElement.ValueKind is
                JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null;
            JsonElement? responseId = hasId && idKindIsValid ? Clone(idElement) : NullId;

            if (!root.TryGetProperty("jsonrpc", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                version.GetString() != "2.0" ||
                !root.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String ||
                !idKindIsValid)
            {
                return Error(responseId, -32600, "Invalid Request");
            }

            if (!hasId)
            {
                // Only a valid Request object can be a Notification. Method lookup and
                // method-specific parameter validation happen after this point and remain silent
                // for notifications, because the client has no outstanding request to match.
                return null;
            }

            id = responseId!.Value;
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
        var binding = name is null
            ? null
            : V3RestRouteBinding.Served.FirstOrDefault(served => string.Equals(served.OperationId, name, StringComparison.Ordinal));
        if (binding is null)
        {
            // The REST namespace tells a registered operation with no route apart from a name nothing
            // has (operation_not_served against unknown_route); the tool call says the same in words.
            return Error(id, -32602,
                name is not null && V3OperationRegistry.Reviewed.Operations.Any(operation => string.Equals(operation.OperationId, name, StringComparison.Ordinal))
                    ? $"Tool not served: {name} is a registered operation this mount does not serve"
                    : $"Unknown tool: {name}");
        }

        var hasArguments = parameters.TryGetProperty("arguments", out var argumentsElement);
        if (hasArguments && argumentsElement.ValueKind != JsonValueKind.Object)
        {
            // REST refuses parameters that are not an object below the envelope (parameters_not_object);
            // the same request over MCP is an invalid-params error, never an empty parameter set.
            return Error(id, -32602, "Invalid params: 'arguments' must be an object");
        }

        var arguments = hasArguments ? argumentsElement : Empty();
        var operationRequestUtf8 = BuildOperationRequest(binding.OperationId, arguments);

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
                        V3ApiHandler.ExecuteFor(corpusMount, utcNow, binding.OperationId),
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

    private static byte[] BuildOperationRequest(string operationId, JsonElement arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("operation_id", operationId);
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
        var writer = new ArrayBufferWriter<byte>();
        using (var jsonWriter = new Utf8JsonWriter(writer))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("tools");
            jsonWriter.WriteStartArray();
            foreach (var binding in V3RestRouteBinding.Served)
            {
                using var requestSchema = JsonDocument.Parse(V3PlatformSchemaExporter.ExportRequestUtf8(binding.OperationId));
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString("name", binding.OperationId);
                jsonWriter.WriteString("description", DescriptionOf(binding.OperationId));
                jsonWriter.WritePropertyName("inputSchema");
                requestSchema.RootElement.GetProperty("properties").GetProperty("parameters").WriteTo(jsonWriter);
                jsonWriter.WriteEndObject();
            }
            jsonWriter.WriteEndArray();
            jsonWriter.WriteEndObject();
        }
        using var document = JsonDocument.Parse(writer.WrittenMemory);
        return Clone(document.RootElement);
    }
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
