using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Platform.Assistant;

namespace Lex.V3.Api;

/// <summary>
/// The planner over an Azure OpenAI chat-completions deployment (the owner's order of 2026-10-28, for a demonstration): it
/// sends the masked question and the opaque references, asks for a plan under strict structured outputs
/// (<see cref="V3AssistantPlanWire.Schema"/>), and answers the plan and what the call cost. It plans only (R31): the validator
/// judges what it answers, with no repair, and code writes every sentence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Configuration</b>, from the environment only, read once at start (<see cref="FromEnvironment"/>):
/// <c>LEX_ASSISTANT_ENDPOINT</c> (<c>https://&lt;resource&gt;.openai.azure.com</c>), <c>LEX_ASSISTANT_DEPLOYMENT</c>,
/// <c>LEX_ASSISTANT_API_VERSION</c>, and the key <c>LEX_ASSISTANT_KEY</c>, set from a Container Apps secret. With no
/// endpoint there is no planner, and ask answers the switched-off card. The key is held in memory, sent only as the
/// <c>api-key</c> header of the call, and never written, logged or echoed: no exception message of this type carries it.
/// </para>
/// <para>
/// <b>What leaves the server</b> (R33): the question after code masks obvious identifiers (e-mail addresses, phone numbers,
/// IBANs, identity numbers), the answer locale, the opaque references, the search languages and the operation list. Never an
/// address, a user agent, a cookie, an earlier question or anything of the request's headers: the planner is never handed
/// them (<see cref="V3PlannerInput"/>). This is the one outbound call the public process makes, and only when the switch is on
/// (PublicRequestRecordingTests pins it to this file).
/// </para>
/// <para>
/// <b>Time and money</b> (R40): the call has <see cref="Timeout"/>; a timeout, an HTTP error (a 429 included), a refusal by
/// the model or an answer cut short throws, and ask answers the switched-off card. The cost is the response's own
/// <c>usage</c> at the configured prices per million tokens (<c>LEX_ASSISTANT_EUR_PER_MILLION_INPUT</c>,
/// <c>LEX_ASSISTANT_EUR_PER_MILLION_OUTPUT</c>; driver defaults until the deployment's price list is read), counted toward
/// the per-answer ceiling and the monthly cap; a response with no usage is counted at the ceiling.
/// </para>
/// </remarks>
internal sealed class AzureOpenAiAssistantPlanner : IAssistantPlanner, IDisposable
{
    internal const string EndpointVariable = "LEX_ASSISTANT_ENDPOINT";
    internal const string DeploymentVariable = "LEX_ASSISTANT_DEPLOYMENT";
    internal const string ApiVersionVariable = "LEX_ASSISTANT_API_VERSION";
    internal const string KeyVariable = "LEX_ASSISTANT_KEY";
    internal const string InputPriceVariable = "LEX_ASSISTANT_EUR_PER_MILLION_INPUT";
    internal const string OutputPriceVariable = "LEX_ASSISTANT_EUR_PER_MILLION_OUTPUT";

    /// <summary>The call's deadline (about 6 s, inside R40's 10 s to the full answer).</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    /// <summary>The most tokens the plan may take: a plan is short, and a longer answer is cut short and refused.</summary>
    internal const int MaximumCompletionTokens = 1200;

    /// <summary>Driver defaults, in euros per million tokens, until the deployment's own price list is read (TODO for the driver).</summary>
    internal const double DefaultInputPrice = 5.0;

    internal const double DefaultOutputPrice = 20.0;

    internal const string PromptResource = "Lex.V3.Api.AssistantPlannerPrompt.md";

    private readonly HttpClient _client;
    private readonly Uri _completions;
    private readonly string _key;
    private readonly double _inputPrice;
    private readonly double _outputPrice;
    private readonly TimeSpan _timeout;

    internal AzureOpenAiAssistantPlanner(
        Uri endpoint,
        string deployment,
        string apiVersion,
        string key,
        HttpMessageHandler handler,
        double inputPricePerMillion = DefaultInputPrice,
        double outputPricePerMillion = DefaultOutputPrice,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The assistant's endpoint is an https address.", nameof(endpoint));
        }

        _completions = new Uri(endpoint,
            $"openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}");
        _key = key;
        _inputPrice = inputPricePerMillion;
        _outputPrice = outputPricePerMillion;
        _timeout = timeout ?? Timeout;
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = _timeout };
        _client.DefaultRequestHeaders.Add("api-key", _key);
        PlannerId = $"azure-openai/{deployment}/prompt-{PromptSha256[..12]}";
    }

    /// <summary>
    /// The planner the environment names, or null when it names no endpoint, deployment, version or key: then there is no
    /// planner, and the assistant stays off whatever its switch says.
    /// </summary>
    internal static AzureOpenAiAssistantPlanner? FromEnvironment(Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var endpoint = variable(EndpointVariable);
        var deployment = variable(DeploymentVariable);
        var apiVersion = variable(ApiVersionVariable);
        var key = variable(KeyVariable);
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(apiVersion) ||
            string.IsNullOrWhiteSpace(key) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new AzureOpenAiAssistantPlanner(
            uri,
            deployment,
            apiVersion,
            key,
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false },
            Price(variable(InputPriceVariable), DefaultInputPrice),
            Price(variable(OutputPriceVariable), DefaultOutputPrice));
    }

    private static double Price(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) && price > 0 ? price : fallback;

    public string PlannerId { get; }

    /// <summary>The versioned system prompt (<c>assistant-planner-prompt.md</c>), committed and embedded.</summary>
    internal static string Prompt { get; } = ReadPrompt();

    internal static string PromptSha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Prompt)));

    /// <summary>The request body for an input: the system prompt, the input as JSON, and the plan's strict schema.</summary>
    internal static string RequestBody(V3PlannerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var user = new JsonObject
        {
            ["question"] = input.MaskedQuestion,
            ["answer_locale"] = Lex.V3.Contracts.ContractWire.NameOf(input.AnswerLocale),
            ["references"] = new JsonArray(input.References.Select(static reference => (JsonNode)new JsonObject
            {
                ["ref"] = reference.Ref,
                ["kind"] = reference.Kind,
                ["start"] = reference.Start,
                ["end"] = reference.End,
            }).ToArray()),
            ["search_languages"] = new JsonArray(input.SearchLanguages.Select(static language => (JsonNode)language).ToArray()),
            ["operations"] = new JsonArray(input.Operations.Select(static operation => (JsonNode)operation).ToArray()),
        };
        var body = new JsonObject
        {
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = Prompt },
                new JsonObject { ["role"] = "user", ["content"] = user.ToJsonString() }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = V3AssistantPlanWire.SchemaName,
                    ["strict"] = true,
                    ["schema"] = V3AssistantPlanWire.Schema(),
                },
            },
            ["max_completion_tokens"] = MaximumCompletionTokens,
        };
        return body.ToJsonString();
    }

    public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, _completions)
        {
            Content = new StringContent(RequestBody(input), Encoding.UTF8, "application/json"),
        };
        using var response = _client.Send(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            // The status alone: never the response's text, which could echo the request.
            throw new InvalidOperationException($"The planner's provider answered HTTP {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)}.");
        }

        using var stream = response.Content.ReadAsStream(deadline.Token);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        var choice = root.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String && finish.GetString() != "stop")
        {
            throw new InvalidOperationException("The planner's answer was cut short or filtered.");
        }

        var message = choice.GetProperty("message");
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
        {
            throw new InvalidOperationException("The planner's model refused to plan.");
        }

        var content = message.GetProperty("content").GetString() ?? throw new InvalidOperationException("The planner answered no content.");
        return new V3PlannerOutput(V3AssistantPlanWire.ToPlanJson(content), CostOf(root));
    }

    /// <summary>The call's cost in micro-euros, from the response's usage; at the per-answer ceiling when it states none.</summary>
    internal long CostOf(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out var usage) ||
            !usage.TryGetProperty("prompt_tokens", out var prompt) || !prompt.TryGetInt64(out var input) ||
            !usage.TryGetProperty("completion_tokens", out var completion) || !completion.TryGetInt64(out var output))
        {
            return V3AssistantRuntime.PerAnswerCeilingMicroEuros;
        }

        return (long)Math.Ceiling((input * _inputPrice) + (output * _outputPrice));
    }

    public void Dispose() => _client.Dispose();

    private static string ReadPrompt()
    {
        using var stream = typeof(AzureOpenAiAssistantPlanner).Assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException("The assistant planner's prompt is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
