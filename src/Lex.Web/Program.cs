using Azure.Monitor.OpenTelemetry.AspNetCore;
using Lex.Ask;
using Lex.Mcp;
using Lex.Web;
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
var options = LexOptionsSetup.FromEnvironment(builder.Environment);

// ---- services --------------------------------------------------------------------------
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IndexRegistry>();
builder.Services.AddSingleton(sp => new McpCore(sp.GetRequiredService<IndexRegistry>().All));
builder.Services.AddSingleton(sp => new AskService(sp.GetRequiredService<McpCore>()));

// Foundry-hybrid observability (D45 posture: keep the loop, adopt the platform's tracing):
// OpenTelemetry via the Azure Monitor distro, exporting to the App Insights resource a Foundry
// project can attach to. Enabled only when configured; the app is fully functional without it.
if (!string.IsNullOrWhiteSpace(options.AppInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
    builder.Services.ConfigureOpenTelemetryTracerProvider(
        (_, b) => b.AddSource(AskService.ActivitySourceName));
}

var app = builder.Build();
app.UseStaticFiles();   // wwwroot: og.png (the social preview card)

var ctx = new WebContext(
    app.Services.GetRequiredService<IndexRegistry>(),
    options,
    app.Services.GetRequiredService<McpCore>(),
    app.Services.GetRequiredService<AskService>(),
    app.Services.GetRequiredService<TimeProvider>());

app.Logger.LogInformation("Assistant {State}; {Count} index(es) mounted from {Dir}",
    ctx.Ask.Enabled ? "enabled" : "disabled (no AOAI endpoint, identity/key or deployment)",
    ctx.Registry.Count, options.IndexDir);

// ---- routes ----------------------------------------------------------------------------
// Grouped by the question each one answers, not by HTTP verb or by URL shape.
app.MapApi(ctx)          // the MCP endpoint, the assistant, robots.txt, healthz
   .MapHome(ctx)         // the front page and the workspace mount
   .MapExplainers(ctx)   // how it works, how it was built, the decisions, how to verify
   .MapCatalogue(ctx)    // browse, search, in force on a date, what changed
   .MapDocuments(ctx);   // one law: its timeline, its text on a date, a comparison

app.Run();

// Top-level statements compile to an internal Program class. WebApplicationFactory<T> needs a
// type from this assembly to boot the app in-process, which is how the golden tests render every
// route without shelling out to a server.
public partial class Program;
