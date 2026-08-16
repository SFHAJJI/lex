using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;

namespace Lex.Ingest;

public sealed class AzureModelDeploymentResolver
{
    private const string ApiVersion = "2024-10-01";
    private static readonly Regex AccountResourceId = new(
        "^/subscriptions/[0-9a-f-]{36}/resourceGroups/[^/]{1,90}/providers/"
        + "Microsoft\\.CognitiveServices/accounts/[^/]{2,64}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DeploymentName = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ContainerAppResourceId = new(
        "^/subscriptions/[0-9a-f-]{36}/resourceGroups/[^/]{1,90}/providers/"
        + "Microsoft\\.App/containerApps/[^/]{2,32}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RevisionName = new(
        "^[a-z][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Memory = new(
        "^([1-9][0-9]{0,4})(Mi|Gi)$", RegexOptions.CultureInvariant);
    private readonly HttpClient _http;
    private readonly TokenCredential _credential;

    public AzureModelDeploymentResolver(
        HttpClient http,
        TokenCredential? credential = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credential = credential ?? new DefaultAzureCredential();
    }

    public async Task<AssistantModelDeploymentEvidence> ResolveAsync(
        string resourceId,
        string deployment,
        CancellationToken cancellationToken)
    {
        if (!AccountResourceId.IsMatch(resourceId))
            throw new InvalidDataException("Azure model resource id is invalid or unsupported.");
        if (!DeploymentName.IsMatch(deployment))
            throw new InvalidDataException("Azure model deployment name is invalid.");
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            cancellationToken);
        var account = await GetAsync(
            $"https://management.azure.com{resourceId}?api-version={ApiVersion}",
            token.Token, cancellationToken);
        var deployed = await GetAsync(
            $"https://management.azure.com{resourceId}/deployments/"
            + $"{Uri.EscapeDataString(deployment)}?api-version={ApiVersion}",
            token.Token, cancellationToken);
        return Parse(resourceId, deployment, account, deployed);
    }

    public async Task<AssistantCandidateRuntimeEvidence> ResolveContainerAppAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (!ContainerAppResourceId.IsMatch(resourceId))
            throw new InvalidDataException("Azure Container App resource id is invalid.");
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            cancellationToken);
        var app = await GetAsync(
            $"https://management.azure.com{resourceId}?api-version=2025-07-01",
            token.Token, cancellationToken);
        return ParseContainerApp(resourceId, app);
    }

    public async Task<AssistantCandidateRuntimeEvidence> ResolveContainerAppRevisionAsync(
        string resourceId,
        string revisionName,
        CancellationToken cancellationToken)
    {
        if (!ContainerAppResourceId.IsMatch(resourceId))
            throw new InvalidDataException("Azure Container App resource id is invalid.");
        if (!RevisionName.IsMatch(revisionName))
            throw new InvalidDataException("Azure Container App revision name is invalid.");
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            cancellationToken);
        var revision = await GetAsync(
            $"https://management.azure.com{resourceId}/revisions/"
            + $"{Uri.EscapeDataString(revisionName)}?api-version=2025-07-01",
            token.Token, cancellationToken);
        return ParseContainerAppRevision(resourceId, revisionName, revision);
    }

    public async Task<BootstrapRevisionLiveEvidence> ResolveContainerAppBootstrapRevisionAsync(
        string resourceId,
        string revisionName,
        CancellationToken cancellationToken)
    {
        if (!ContainerAppResourceId.IsMatch(resourceId))
            throw new InvalidDataException("Azure Container App resource id is invalid.");
        if (!RevisionName.IsMatch(revisionName))
            throw new InvalidDataException("Azure Container App revision name is invalid.");
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            cancellationToken);
        var revision = await GetAsync(
            $"https://management.azure.com{resourceId}/revisions/"
            + $"{Uri.EscapeDataString(revisionName)}?api-version=2025-07-01",
            token.Token, cancellationToken);
        return ParseContainerAppBootstrapRevision(resourceId, revisionName, revision);
    }

    public async Task<IReadOnlyList<BootstrapRevisionRouteEvidence>>
        ResolveContainerAppBootstrapRoutesAsync(
            string resourceId,
            CancellationToken cancellationToken)
    {
        if (!ContainerAppResourceId.IsMatch(resourceId))
            throw new InvalidDataException("Azure Container App resource id is invalid.");
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            cancellationToken);
        var revisions = await GetAsync(
            $"https://management.azure.com{resourceId}/revisions?api-version=2025-07-01",
            token.Token, cancellationToken);
        return ParseContainerAppBootstrapRoutes(resourceId, revisions);
    }

    internal static AssistantCandidateRuntimeEvidence ParseContainerApp(
        string resourceId,
        JsonObject app)
    {
        if (!string.Equals(app["id"]?.GetValue<string>()?.TrimEnd('/'),
                resourceId.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(app["properties"]?["provisioningState"]?.GetValue<string>(),
                "Succeeded", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Azure Container App evidence is not the requested ready app.");
        var properties = app["properties"] as JsonObject
            ?? throw new InvalidDataException("Azure Container App properties are absent.");
        return ParseCandidateTemplate(resourceId,
            properties["latestRevisionName"]?.GetValue<string>() ?? "",
            properties["latestRevisionFqdn"]?.GetValue<string>() ?? "",
            properties);
    }

    internal static AssistantCandidateRuntimeEvidence ParseContainerAppRevision(
        string resourceId,
        string revisionName,
        JsonObject revision)
    {
        var expectedId = resourceId.TrimEnd('/') + "/revisions/" + revisionName;
        var properties = revision["properties"] as JsonObject;
        if (!string.Equals(revision["id"]?.GetValue<string>()?.TrimEnd('/'),
                expectedId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(revision["name"]?.GetValue<string>(), revisionName,
                StringComparison.Ordinal)
            || properties is null
            || properties["active"]?.GetValue<bool>() != true
            || !IsReadyRunningState(properties["runningState"]?.GetValue<string>()))
            throw new InvalidDataException(
                "Azure Container App revision evidence is not the requested running revision.");
        return ParseCandidateTemplate(resourceId, revisionName,
            properties["fqdn"]?.GetValue<string>() ?? "", properties);
    }

    internal static BootstrapRevisionLiveEvidence ParseContainerAppBootstrapRevision(
        string resourceId,
        string revisionName,
        JsonObject revision)
    {
        var expectedId = resourceId.TrimEnd('/') + "/revisions/" + revisionName;
        var properties = revision["properties"] as JsonObject
            ?? throw new InvalidDataException("Azure Container App revision properties are absent.");
        if (!string.Equals(revision["id"]?.GetValue<string>()?.TrimEnd('/'),
                expectedId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(revision["name"]?.GetValue<string>(), revisionName,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Azure Container App revision evidence is not the requested exact revision.");
        if (properties["active"] is not JsonValue activeValue
            || !activeValue.TryGetValue<bool>(out var active)
            || properties["trafficWeight"] is not JsonValue trafficValue
            || !trafficValue.TryGetValue<int>(out var traffic)
            || traffic is < 0 or > 100)
            throw new InvalidDataException(
                "Azure Container App revision activity evidence is malformed.");
        if (active && !IsReadyRunningState(properties["runningState"]?.GetValue<string>()))
            throw new InvalidDataException(
                "Active Azure Container App bootstrap candidate is not running.");
        var createdTime = properties["createdTime"]?.GetValue<string>() ?? "";
        if (!TryParseExplicitUtc(createdTime, out _))
            throw new InvalidDataException(
                "Azure Container App revision createdTime is not an explicit UTC instant.");
        var template = properties["template"] as JsonObject
            ?? throw new InvalidDataException("Azure Container App revision template is absent.");
        var containers = template["containers"] as JsonArray;
        if (containers is not { Count: 1 } || containers[0] is not JsonObject container)
            throw new InvalidDataException(
                "Bootstrap equivalence requires exactly one Container App container.");
        var image = container["image"]?.GetValue<string>() ?? "";
        var codeCommitValues = (container["env"] as JsonArray)?
            .OfType<JsonObject>()
            .Where(item => item["name"]?.GetValue<string>() == "LEX_CODE_COMMIT")
            .Select(item => item["value"]?.GetValue<string>() ?? "")
            .ToArray() ?? [];
        if (codeCommitValues.Length != 1
            || !Regex.IsMatch(codeCommitValues[0], "^[0-9a-f]{40}$",
                RegexOptions.CultureInvariant))
            throw new InvalidDataException(
                "Bootstrap equivalence requires one exact LEX_CODE_COMMIT value.");
        return new BootstrapRevisionLiveEvidence(
            resourceId.TrimEnd('/'), revisionName, expectedId, createdTime, image,
            CanonicalContainerAppTemplateDigest(template), codeCommitValues[0], active, traffic);
    }

    internal static IReadOnlyList<BootstrapRevisionRouteEvidence>
        ParseContainerAppBootstrapRoutes(string resourceId, JsonObject response)
    {
        if (response["nextLink"] is not null)
            throw new InvalidDataException(
                "Azure Container App revision inventory is paginated and therefore incomplete.");
        if (response["value"] is not JsonArray values)
            throw new InvalidDataException("Complete Azure Container App revision inventory is absent.");
        var routes = new List<BootstrapRevisionRouteEvidence>(values.Count);
        foreach (var node in values)
        {
            if (node is not JsonObject revision
                || revision["name"] is not JsonValue nameValue
                || !nameValue.TryGetValue<string>(out var name)
                || !RevisionName.IsMatch(name)
                || revision["properties"] is not JsonObject properties
                || properties["active"] is not JsonValue activeValue
                || !activeValue.TryGetValue<bool>(out var active)
                || properties["trafficWeight"] is not JsonValue trafficValue
                || !trafficValue.TryGetValue<int>(out var traffic)
                || traffic is < 0 or > 100
                || (!active && traffic != 0))
                throw new InvalidDataException(
                    "Azure Container App revision route inventory is malformed.");
            var expectedId = resourceId.TrimEnd('/') + "/revisions/" + name;
            var id = revision["id"]?.GetValue<string>() ?? "";
            var createdTime = properties["createdTime"]?.GetValue<string>() ?? "";
            if (!string.Equals(id.TrimEnd('/'), expectedId, StringComparison.OrdinalIgnoreCase)
                || !TryParseExplicitUtc(createdTime, out _))
                throw new InvalidDataException(
                    "Azure Container App revision route identity is malformed.");
            routes.Add(new BootstrapRevisionRouteEvidence(
                name, expectedId, createdTime, active, traffic));
        }
        if (routes.Select(item => item.RevisionName).Distinct(StringComparer.Ordinal).Count()
            != routes.Count)
            throw new InvalidDataException(
                "Azure Container App revision route inventory contains duplicates.");
        return routes;
    }

    internal static string CanonicalContainerAppTemplateDigest(JsonNode revisionOrTemplate)
    {
        ArgumentNullException.ThrowIfNull(revisionOrTemplate);
        JsonNode? template = revisionOrTemplate;
        if (revisionOrTemplate is JsonObject root
            && root["properties"] is JsonObject properties)
            template = properties["template"];
        else if (revisionOrTemplate is JsonObject wrapper
            && wrapper["template"] is JsonObject wrappedTemplate)
            template = wrappedTemplate;
        if (template is not JsonObject templateObject
            || templateObject["containers"] is not JsonArray)
            throw new InvalidDataException("Container Apps revision template is required.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            WriteCanonical(writer, templateObject, excludeRevisionSuffix: true);
        }
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(
        Utf8JsonWriter writer,
        JsonNode? node,
        bool excludeRevisionSuffix = false)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var item in value.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (excludeRevisionSuffix && item.Key == "revisionSuffix") continue;
                    writer.WritePropertyName(item.Key);
                    WriteCanonical(writer, item.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var item in value) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static bool IsReadyRunningState(string? state) =>
        string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "RunningAtMaxScale", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseExplicitUtc(string value, out DateTimeOffset parsed)
    {
        parsed = default;
        return (value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal))
            && DateTimeOffset.TryParse(value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out parsed)
            && parsed.Offset == TimeSpan.Zero;
    }

    private static AssistantCandidateRuntimeEvidence ParseCandidateTemplate(
        string resourceId,
        string revisionName,
        string revisionFqdn,
        JsonObject properties)
    {
        var template = properties["template"] as JsonObject
            ?? throw new InvalidDataException("Azure Container App template is absent.");
        var containers = template["containers"] as JsonArray;
        if (containers is not { Count: 1 } || containers[0] is not JsonObject container)
            throw new InvalidDataException("Assistant evaluation requires exactly one candidate container.");
        var scale = template["scale"] as JsonObject
            ?? throw new InvalidDataException("Azure Container App scale configuration is absent.");
        var resources = container["resources"] as JsonObject
            ?? throw new InvalidDataException("Azure Container App resource configuration is absent.");
        var memoryText = resources["memory"]?.GetValue<string>() ?? "";
        var memory = Memory.Match(memoryText);
        if (!memory.Success)
            throw new InvalidDataException("Azure Container App memory configuration is unsupported.");
        var memoryValue = long.Parse(memory.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var memoryBytes = checked(memoryValue * (memory.Groups[2].Value == "Gi"
            ? 1_073_741_824L : 1_048_576L));
        var environment = (container["env"] as JsonArray)?
            .OfType<JsonObject>()
            .Where(item => item["name"] is JsonValue && item["value"] is JsonValue)
            .ToDictionary(item => item["name"]!.GetValue<string>(),
                item => item["value"]!.GetValue<string>(), StringComparer.Ordinal)
            ?? throw new InvalidDataException("Azure Container App environment evidence is absent.");
        string Required(string name) => environment.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value) && value.Length <= 2_000
                ? value : throw new InvalidDataException(
                    $"Azure Container App evidence is missing {name}.");
        var endpoint = new Uri(Required("AOAI_ENDPOINT"));
        var evidence = new AssistantCandidateRuntimeEvidence(
            resourceId,
            revisionName,
            revisionFqdn,
            container["image"]?.GetValue<string>() ?? "",
            resources["cpu"]?.GetValue<decimal>() ?? 0,
            memoryBytes,
            scale["minReplicas"]?.GetValue<int>() ?? -1,
            scale["maxReplicas"]?.GetValue<int>() ?? -1,
            properties["trafficWeight"]?.GetValue<int>() ?? -1,
            Required("LEX_CODE_COMMIT"), Required("LEX_ARTIFACT_MANIFEST_ID"),
            endpoint.IdnHost, Required("AOAI_CHAT_DEPLOYMENT"), "");
        return evidence with { EvidenceSha256 = TargetEvidenceSha256(evidence) };
    }

    public static string TargetEvidenceSha256(AssistantCandidateRuntimeEvidence evidence)
    {
        // A scale-stripping format, because Azure Resource Manager renders the same CPU
        // allocation with different trailing zeros between responses. One evaluation recorded
        // "cpu": 1.000 and the next request for the identical revision returned "cpu": 1.0, which
        // produced two digests for one unchanged revision and made the signed report permanently
        // unpublishable. decimal equality ignores scale, so CpuCores itself compared equal and only
        // this derived string disagreed, which is why the failure surfaced as wrong evidence.
        var canonical = string.Join('\n',
            evidence.ResourceId.TrimEnd('/').ToLowerInvariant(), evidence.RevisionName,
            evidence.RevisionFqdn.ToLowerInvariant(), evidence.Image,
            evidence.CpuCores.ToString(
                "0.############################", System.Globalization.CultureInfo.InvariantCulture),
            evidence.MemoryLimitBytes, evidence.MinimumReplicas, evidence.MaximumReplicas,
            evidence.TrafficWeight,
            evidence.CodeCommit, evidence.ArtifactManifestSet,
            evidence.CandidateModelHost.ToLowerInvariant(), evidence.CandidateDeployment);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static AssistantModelDeploymentEvidence Parse(
        string resourceId,
        string deployment,
        JsonObject account,
        JsonObject deployed)
    {
        if (!string.Equals(account["id"]?.GetValue<string>()?.TrimEnd('/'),
                resourceId.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Azure model account evidence has the wrong resource id.");
        var endpoint = account["properties"]?["endpoint"]?.GetValue<string>()
            ?? throw new InvalidDataException("Azure model account evidence has no endpoint.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Azure model account endpoint is invalid.");
        var expectedDeploymentId = resourceId.TrimEnd('/') + "/deployments/" + deployment;
        if (!string.Equals(deployed["id"]?.GetValue<string>()?.TrimEnd('/'),
                expectedDeploymentId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(deployed["properties"]?["provisioningState"]?.GetValue<string>(),
                "Succeeded", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Azure model deployment is not the requested ready deployment.");
        var model = deployed["properties"]?["model"] as JsonObject
            ?? throw new InvalidDataException("Azure model deployment evidence has no model identity.");
        var sku = deployed["sku"]?["name"]?.GetValue<string>()
            ?? throw new InvalidDataException("Azure model deployment SKU is absent.");
        var format = model["format"]?.GetValue<string>()
            ?? throw new InvalidDataException("Azure model deployment format is absent.");
        var name = model["name"]?.GetValue<string>()
            ?? throw new InvalidDataException("Azure model deployment name is absent.");
        var version = model["version"]?.GetValue<string>()
            ?? throw new InvalidDataException("Azure model deployment version is absent.");
        if (!string.Equals(format, "OpenAI", StringComparison.OrdinalIgnoreCase)
            || sku.Length is 0 or > 100
            || name.Length is 0 or > 200 || version.Length is 0 or > 200)
            throw new InvalidDataException("Azure model deployment identity is unsupported.");
        var evidence = new AssistantModelDeploymentEvidence(
            resourceId, endpointUri.GetLeftPart(UriPartial.Authority), deployment,
            sku, format, name, version, "");
        return evidence with { EvidenceSha256 = EvidenceSha256(evidence) };
    }

    internal static string EvidenceSha256(AssistantModelDeploymentEvidence evidence)
    {
        var endpoint = new Uri(evidence.Endpoint);
        var canonical = string.Join('\n',
            evidence.ResourceId.TrimEnd('/').ToLowerInvariant(),
            endpoint.IdnHost.ToLowerInvariant(),
            evidence.Deployment,
            evidence.Sku,
            evidence.ModelFormat,
            evidence.ModelName,
            evidence.ModelVersion);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task<JsonObject> GetAsync(
        string uri,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Azure model identity lookup returned HTTP {(int)response.StatusCode}.");
        var bytes = await AssistantEvaluationHttpTarget.ReadBoundedAsync(
            response.Content, 512 * 1024, cancellationToken);
        try
        {
            return JsonNode.Parse(bytes) as JsonObject
                ?? throw new InvalidDataException("Azure model identity response is not an object.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidDataException("Azure model identity response is malformed.", exception);
        }
    }
}
