using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lex.Mcp;

/// <summary>
/// Connects Lex's transport-neutral legal tools to the official MCP protocol implementation.
/// Protocol negotiation, JSON-RPC framing and transport behavior belong to the SDK; legal
/// parameters, refusals and result payloads remain in <see cref="McpCore"/>.
/// </summary>
public static class McpSdkBridge
{
    public const string ServerName = "lex";
    public const string ServerVersion = "2.0.0";
    public const string ServerInstructions =
        "Point-in-time regulatory text (Luxembourg + EU). Unknown document -> call search first, " +
        "take lex_id from the hit, then as_of. The `work` parameter accepts a work-level lex_id " +
        "(publisher:workkey), a version-level lex_id (version segment ignored), or a verbatim " +
        "publisher identifier when `publisher` is supplied. Legal result statuses are closed and documented in the MCP 2.0 " +
        "migration note. Refusals such as no_version_for_date, text_withheld and " +
        "text_not_available, retrieval_mode_unavailable, and filter_not_supported_by_index are honest answers, not transport errors.";

    public static void Configure(McpServerOptions options)
    {
        options.ServerInfo = new Implementation
        {
            Name = ServerName,
            Version = ServerVersion,
        };
        options.ServerInstructions = ServerInstructions;
    }

    public static IMcpServerBuilder WithLexTools(this IMcpServerBuilder builder) => builder
        .WithListToolsHandler((request, _) =>
        {
            var core = RequiredCore(request.Services);
            var tools = core.ToolDefs().OfType<JsonObject>().Select(definition => new Tool
            {
                Name = definition["name"]!.GetValue<string>(),
                Description = definition["description"]!.GetValue<string>(),
                InputSchema = JsonSerializer.SerializeToElement(definition["inputSchema"]),
            }).ToArray();
            return ValueTask.FromResult(new ListToolsResult { Tools = tools });
        })
        .WithCallToolHandler(async (request, cancellationToken) =>
        {
            var core = RequiredCore(request.Services);
            var arguments = new JsonObject();
            if (request.Params.Arguments is not null)
                foreach (var (name, value) in request.Params.Arguments)
                    arguments[name] = JsonNode.Parse(value.GetRawText());

            return await InvokeAsync(
                request.Params.Name,
                arguments,
                core.ToolDefs().OfType<JsonObject>()
                    .Select(definition => definition["name"]!.GetValue<string>())
                    .ToHashSet(StringComparer.Ordinal),
                core.CallToolAsync,
                cancellationToken);
        });

    internal static async ValueTask<CallToolResult> InvokeAsync(
        string name,
        JsonObject arguments,
        IReadOnlySet<string> knownTools,
        Func<string, JsonObject, CancellationToken, ValueTask<JsonNode>> callTool,
        CancellationToken cancellationToken)
    {
        if (!knownTools.Contains(name))
            throw new McpProtocolException(
                "Unknown tool.", McpErrorCode.InvalidParams);
        try
        {
            var result = await callTool(name, arguments, cancellationToken);
            return Result(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            return InvalidArguments();
        }
        catch (Exception)
        {
            throw InternalFailure();
        }
    }

    internal static CallToolResult Invoke(
        string name,
        JsonObject arguments,
        IReadOnlySet<string> knownTools,
        Func<string, JsonObject, JsonNode> callTool)
    {
        if (!knownTools.Contains(name))
            throw new McpProtocolException(
                "Unknown tool.", McpErrorCode.InvalidParams);
        try
        {
            var result = callTool(name, arguments);
            return Result(result);
        }
        catch (ArgumentException)
        {
            return InvalidArguments();
        }
        catch (Exception)
        {
            throw InternalFailure();
        }
    }

    private static CallToolResult Result(JsonNode result) => new()
    {
        Content = [new TextContentBlock
        {
            Text = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
        }],
    };

    private static CallToolResult InvalidArguments() => new()
    {
        Content = [new TextContentBlock
        {
            Text = "Invalid tool arguments. Use the advertised schema and documented bounds.",
        }],
        IsError = true,
    };

    private static McpProtocolException InternalFailure() => new(
        "The MCP tool failed internally.", McpErrorCode.InternalError);

    private static McpCore RequiredCore(IServiceProvider? services) =>
        services?.GetRequiredService<McpCore>()
        ?? throw new InvalidOperationException("The MCP request has no service provider.");
}
