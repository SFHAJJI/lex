using System.Text.Json.Nodes;

namespace Lex.Web;

public static class McpRequestBoundaries
{
    private const int MaximumBodyBytes = 65_536;

    public static IApplicationBuilder UseMcpRequestBoundaries(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method)
                || context.Request.Path != "/mcp")
            {
                await next();
                return;
            }

            if (context.Request.ContentLength is > MaximumBodyBytes)
            {
                await Error(context, 413, -32003, "request_too_large", null);
                return;
            }
            var body = await BoundedRequestBody.ReadAsync(
                context.Request.Body, MaximumBodyBytes, context.RequestAborted);
            if (body is null)
            {
                await Error(context, 413, -32003, "request_too_large", null);
                return;
            }

            JsonNode? request = null;
            try { request = JsonNode.Parse(body); }
            catch { /* The MCP transport emits its normal parse error after bounded admission. */ }
            var id = request is JsonObject one ? one["id"]?.DeepClone() : null;
            var client = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                             ?.Split(',')[^1].Trim()
                         ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var admission = await context.RequestServices
                .GetRequiredService<McpAdmissionController>()
                .EnterAsync(client, IsHybrid(request), context.RequestAborted);
            if (!admission.Accepted)
            {
                var limited = admission.Failure == McpAdmissionFailure.RateLimited;
                await Error(context, limited ? 429 : 503, limited ? -32002 : -32001,
                    limited ? "rate_limited" : "busy", id);
                return;
            }

            using var lease = admission.Lease!;
            context.Request.Body = new MemoryStream(body, writable: false);
            context.Request.ContentLength = body.Length;
            await next();
        });

    private static bool IsHybrid(JsonNode? request) => request switch
    {
        JsonObject one => IsHybridCall(one),
        JsonArray batch => batch.OfType<JsonObject>().Any(IsHybridCall),
        _ => false,
    };

    private static bool IsHybridCall(JsonObject request) =>
        String(request, "method") == "tools/call"
        && request["params"] is JsonObject parameters
        && String(parameters, "name") == "search"
        && parameters["arguments"] is JsonObject arguments
        && String(arguments, "retrieval_mode") == "hybrid";

    private static string? String(JsonObject value, string name) =>
        value[name] is JsonValue item && item.TryGetValue<string>(out var text) ? text : null;

    private static async Task Error(
        HttpContext context,
        int status,
        int code,
        string message,
        JsonNode? id)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["data"] = new JsonObject { ["status"] = message },
            },
        };
        await context.Response.WriteAsync(body.ToJsonString(), context.RequestAborted);
    }
}
