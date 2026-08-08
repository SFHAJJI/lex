using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
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
    public const string ServerVersion = "0.3.0";
    public const string ServerInstructions =
        "Point-in-time regulatory text (Luxembourg + EU). Unknown document -> call search first, " +
        "take lex_id from the hit, then as_of. The `work` parameter accepts a work-level lex_id " +
        "(publisher:workkey), a version-level lex_id (version segment ignored), or a verbatim " +
        "publisher identifier. Refusal statuses (outside_observed_window / no_version_for_date / " +
        "text_withheld, text_not_available) are honest answers, not errors.";

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
        .WithCallToolHandler((request, _) =>
        {
            var core = RequiredCore(request.Services);
            var arguments = new JsonObject();
            if (request.Params.Arguments is not null)
                foreach (var (name, value) in request.Params.Arguments)
                    arguments[name] = JsonNode.Parse(value.GetRawText());

            try
            {
                var result = core.CallTool(request.Params.Name, arguments);
                return ValueTask.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock
                    {
                        Text = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    }],
                });
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"error: {ex.Message}" }],
                    IsError = true,
                });
            }
        });

    private static McpCore RequiredCore(IServiceProvider? services) =>
        services?.GetRequiredService<McpCore>()
        ?? throw new InvalidOperationException("The MCP request has no service provider.");
}
