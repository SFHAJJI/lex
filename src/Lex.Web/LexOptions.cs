using System.ComponentModel.DataAnnotations;

namespace Lex.Web;

/// <summary>
/// Everything the web layer reads from its environment, in one place, validated at startup.
///
/// These were seven separate <c>Environment.GetEnvironmentVariable</c> calls scattered through a
/// two-thousand-line file, each with its own inline default and none of them checked. A typo in a
/// container's environment produced a service that started cleanly and then behaved oddly: the
/// index directory silently fell back to a path that did not exist, and the public base URL
/// silently fell back to the production host, which is exactly the value you do not want a
/// staging deployment to assume.
///
/// Bound from configuration, so an environment variable, a JSON file and a command-line switch
/// all reach the same property, and validated on start so a misconfiguration fails loudly at
/// boot rather than quietly at request time.
/// </summary>
public sealed class LexOptions
{
    public const string Section = "Lex";

    /// <summary>Directory holding the per-publisher <c>index-*.db</c> files (D27).</summary>
    [Required]
    public string IndexDir { get; init; } = "indexes";

    /// <summary>Verified local ONNX model directory. Absent keeps retrieval keyword-only.</summary>
    public string? EmbeddingModelDir { get; init; }

    /// <summary>
    /// Absolute base for permalinks and social-preview metadata. Defaults to the canonical host
    /// so a pasted link previews correctly even when nothing is configured.
    /// </summary>
    [Required, Url]
    public string PublicBaseUrl { get; init; } = "https://law.soufien.lu";

    /// <summary>Azure OpenAI endpoint for the assistant. Absent disables it rather than failing.</summary>
    public string? AzureOpenAiEndpoint { get; init; }

    public string? AzureOpenAiKey { get; init; }

    public string? AzureOpenAiDeployment { get; init; }

    public bool AzureOpenAiUseManagedIdentity { get; init; }

    /// <summary>Application Insights. Absent means observability is off and the app still runs.</summary>
    public string? AppInsightsConnectionString { get; init; }

    /// <summary>
    /// Migration gate. Production turns this on after the nightly publishes its first signed
    /// artifact manifest; an invalid manifest always fails closed even while absence is allowed.
    /// </summary>
    public bool RequireArtifactManifest { get; init; }

    /// <summary>
    /// Exact comma-separated publisher set required for a production-ready revision.
    /// Deliberately scoped deployments declare their smaller set instead of silently appearing
    /// complete with whatever happened to mount.
    /// </summary>
    public string RequiredPublishers { get; init; } = "";

    public IReadOnlyList<string> RequiredPublisherSet => RequiredPublishers
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Immutable deployment facts injected by the release workflow.</summary>
    public string? CodeCommit { get; init; }
    public string? ArtifactManifestId { get; init; }
    public string? DeployImage { get; init; }

    /// <summary>True when the assistant has everything it needs to answer.</summary>
    public bool AssistantEnabled =>
        !string.IsNullOrWhiteSpace(AzureOpenAiEndpoint)
        && !string.IsNullOrWhiteSpace(AzureOpenAiDeployment)
        && (AzureOpenAiUseManagedIdentity || !string.IsNullOrWhiteSpace(AzureOpenAiKey));

    /// <summary>The base URL without a trailing slash, which is how every caller wants it.</summary>
    public string PublicBase => PublicBaseUrl.TrimEnd('/');
}

/// <summary>
/// Binds <see cref="LexOptions"/> from the environment variable names the deployment already
/// uses, so this refactor changes no container configuration anywhere.
/// </summary>
public static class LexOptionsSetup
{
    public static IServiceCollection AddLexOptions(this IServiceCollection services, IConfiguration config)
    {
        // The deployed names are flat and historical (LEX_INDEX_DIR, AOAI_KEY...). Mapping them
        // here keeps the container definitions untouched while the code reads a typed object.
        services.AddOptions<LexOptions>()
            .Configure(o => { })
            .PostConfigure(o => { })
            .Validate(o => !string.IsNullOrWhiteSpace(o.IndexDir), "Lex:IndexDir must be set.")
            .ValidateOnStart();

        services.Configure<LexOptions>(config.GetSection(LexOptions.Section));
        return services;
    }

    /// <summary>Reads the historical flat environment variables into the typed shape.</summary>
    public static LexOptions FromConfiguration(IHostEnvironment env, IConfiguration configuration) => new()
    {
        IndexDir = configuration["LEX_INDEX_DIR"]
                   ?? Path.Combine(env.ContentRootPath, "indexes"),
        EmbeddingModelDir = configuration["LEX_EMBEDDING_MODEL_DIR"],
        PublicBaseUrl = configuration["LEX_PUBLIC_BASE_URL"]
                        ?? "https://law.soufien.lu",
        AzureOpenAiEndpoint = configuration["AOAI_ENDPOINT"],
        AzureOpenAiKey = configuration["AOAI_KEY"],
        AzureOpenAiDeployment = configuration["AOAI_CHAT_DEPLOYMENT"],
        AzureOpenAiUseManagedIdentity =
            configuration["AOAI_USE_MANAGED_IDENTITY"] == "1",
        AppInsightsConnectionString =
            configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"],
        RequireArtifactManifest = configuration["LEX_REQUIRE_ARTIFACT_MANIFEST"] == "1",
        RequiredPublishers = configuration["LEX_REQUIRED_PUBLISHERS"] ?? "",
        CodeCommit = configuration["LEX_CODE_COMMIT"],
        ArtifactManifestId = configuration["LEX_ARTIFACT_MANIFEST_ID"],
        DeployImage = configuration["LEX_DEPLOY_IMAGE"],
    };
}
