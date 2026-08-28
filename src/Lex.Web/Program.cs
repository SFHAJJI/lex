using Azure.Monitor.OpenTelemetry.Exporter;
using Lex.Ask;
using Lex.Evaluation;
using Lex.Mcp;
using Lex.Web;
using Microsoft.AspNetCore.ResponseCompression;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Lex.Web: the composition root, and nothing else.
//
// This file was 2,019 lines: twenty-nine route lambdas, the HTML shell, the stylesheet, the
// shared fragments and seven scattered reads of the environment, all in one scope. Every lambda
// closed over whatever happened to be in scope, which is what made it unmovable, because nothing
// declared what it depended on.
//
// It is now the three things a startup file should be: configuration, services, and the map of
// which module answers what. The routes live in ApiEndpoints, HomeEndpoints, ExplainerEndpoints,
// CatalogueEndpoints and DocumentEndpoints; the markup lives in PageShell and Fragments.
//
// The split was verified rather than trusted: 41 golden snapshots of every page and every tool
// are byte-identical across it.

var builder = WebApplication.CreateBuilder(args);

// ---- configuration ---------------------------------------------------------------------
// One bound, validated object, read once. It used to be seven Environment.GetEnvironmentVariable
// calls with inline defaults, so a typo in a container produced a service that started cleanly
// and then behaved oddly: the index directory silently fell back to a path that did not exist,
// and the public base URL silently fell back to the production host.
var options = LexOptionsSetup.FromConfiguration(builder.Environment, builder.Configuration);

// ---- services --------------------------------------------------------------------------
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IndexRegistry>();
builder.Services.AddSingleton(sp =>
{
    var registry = sp.GetRequiredService<IndexRegistry>();
    return new McpCore(registry.All, registry.VerifiedManifestSetId, options.PublicBaseUrl,
        registry.HybridActivations.ToDictionary(
            item => item.Key, item => item.Value.Reason, StringComparer.Ordinal));
});
builder.Services.AddSingleton(sp => new AskService(sp.GetRequiredService<McpCore>()));
builder.Services.AddSingleton(sp => new AskRequestRegistry(
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new AskThreadRegistry(
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new EvaluationAdmissionVerifier(
    EvaluationAdmissionTrustStore.Load(),
    new EvaluationAdmissionIdentity(
        options.Revision ?? "",
        options.DeployImage ?? "",
        options.CodeCommit ?? "",
        options.ArtifactManifestId ?? "",
        options.AssistantEvalCatalogSha256 ?? ""),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new EvaluationAdmissionRegistry(
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new McpAdmissionController(
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddAssistantEvaluationEvidence();
builder.Services.AddMcpServer(McpSdkBridge.Configure)
    .WithHttpTransport(options => options.Stateless = true)
    .WithLexTools();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

// Trace-only, allowlisted observability. Enabled only when configured; the app is fully
// functional without it.
if (!string.IsNullOrWhiteSpace(options.AppInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(b => b
            .SetSampler(LexTraceConfiguration.TraceSampler)
            .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("lex-web"))
            .AddSource(LexRequestTelemetry.ActivitySourceName)
            .AddSource(McpCore.ActivitySourceName)
            .AddSource(AskService.ActivitySourceName)
            .AddProcessor(new PrivacyActivityProcessor())
            .AddAzureMonitorTraceExporter(exporter =>
            {
                exporter.ConnectionString = options.AppInsightsConnectionString;
                exporter.EnableLiveMetrics = false;
                exporter.EnableStandardMetrics = false;
                exporter.EnablePerformanceCounters = false;
                exporter.DisableOfflineStorage = true;
                exporter.TracesPerSecond = null;
                exporter.SamplingRatio = 1.0F;
            }));
}

var app = builder.Build();
// The public service has no user accounts, but its pages still render publisher text and should
// not be frameable or granted browser capabilities. Keep this policy at the composition root so
// HTML, JSON, MCP, health and static responses cannot drift into different security postures.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.StrictTransportSecurity = "max-age=10886400; includeSubDomains; preload";
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers.Append("Permissions-Policy", "camera=(), geolocation=(), microphone=(), payment=(), usb=()");
    await next();
});
app.UseResponseCompression();
app.UseLexRequestTelemetry();
app.UseMcpRequestBoundaries();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path;
        if (path.StartsWithSegments("/app") || path.StartsWithSegments("/fonts"))
            context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    },
});

var ctx = new WebContext(
    app.Services.GetRequiredService<IndexRegistry>(),
    options,
    app.Services.GetRequiredService<McpCore>(),
    app.Services.GetRequiredService<AskService>(),
    app.Services.GetRequiredService<AskRequestRegistry>(),
    app.Services.GetRequiredService<AskThreadRegistry>(),
    app.Services.GetRequiredService<EvaluationAdmissionVerifier>(),
    app.Services.GetRequiredService<EvaluationAdmissionRegistry>(),
    app.Services.GetRequiredService<TimeProvider>());

app.Logger.LogInformation("Assistant {State}; {Count} index(es) mounted from {Dir}",
    ctx.Ask.Enabled ? "enabled" : "disabled (no AOAI endpoint, identity/key or deployment)",
    ctx.Registry.Count, options.IndexDir);

// ---- routes ----------------------------------------------------------------------------
// Grouped by the question each one answers, not by HTTP verb or by URL shape.
app.MapMcp("/mcp");
app.MapApi(ctx)          // the assistant, public metadata APIs, robots.txt and healthz
   .MapHome(ctx)         // the front page and the workspace mount
   .MapExplainers(ctx)   // how it works, how it was built, the decisions, how to verify
   .MapBuilt(ctx)        // the diagram-led architecture dossier and its owned SVGs
   .MapCatalogue(ctx)    // browse, search, in force on a date, what changed
   .MapDocuments(ctx);   // one law: its timeline, its text on a date, a comparison

app.Run();

// Top-level statements compile to an internal Program class. WebApplicationFactory<T> needs a
// type from this assembly to boot the app in-process, which is how the golden tests render every
// route without shelling out to a server.
public partial class Program;
