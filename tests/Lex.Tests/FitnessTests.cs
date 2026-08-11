using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using Lex.Mcp;
using Lex.Web;
using System.Diagnostics;

namespace Lex.Tests;

/// <summary>
/// The fitness function is the architecture's enforcement mechanism (spec §15).
/// These tests fail the build, they do not advise.
/// </summary>
public class FitnessTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lex.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<string> Sources(string project) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_published_MCP_surface_and_migration_note_match_version_2()
    {
        using var server = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "server.json")));
        Assert.Equal(McpSdkBridge.ServerVersion,
            server.RootElement.GetProperty("version").GetString());

        var migration = File.ReadAllText(
            Path.Combine(RepoRoot(), "docs", "mcp-2-migration.md"));
        foreach (var required in new[]
                 {
                     "65,536", "2,000", "250,000", "citation rows", "500 states",
                     "1,000 events", "eight publisher", "busy", "rate_limited",
                     "100,000", "unknown fields",
                 })
            Assert.Contains(required, migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Telemetry_processor_removes_query_text_and_ingress_addresses()
    {
        using var activity = new Activity("privacy-test").Start();
        activity.SetTag("url.query", "?q=TRACE_CANARY");
        activity.SetTag("url.full", "https://law.soufien.lu/?q=TRACE_CANARY");
        activity.SetTag("http.url", "https://law.soufien.lu/?q=TRACE_CANARY");
        activity.SetTag("client.address", "203.0.113.42");

        new PrivacyActivityProcessor().OnEnd(activity);
        var exported = string.Join('\n', activity.TagObjects.Select(item => $"{item.Key}={item.Value}"));

        Assert.DoesNotContain("TRACE_CANARY", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.42", exported, StringComparison.Ordinal);
        Assert.Contains("https://law.soufien.lu/", exported, StringComparison.Ordinal);
    }

    private static readonly string[] PublisherWords = ["jolux", "cdm:", "legilux", "eurlex", "cellar", "cssf"];

    [Fact]
    public void Assistant_telemetry_has_a_closed_metadata_only_allowlist()
    {
        var askSource = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lex.Ask", "AskService.cs"));
        var tags = Regex.Matches(askSource, "SetTag\\(\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([
            "gen_ai.request.model", "gen_ai.tool.name", "lex.docs",
            "lex.operation.id", "lex.status",
        ], tags);
        Assert.DoesNotContain("ex.Message", askSource, StringComparison.Ordinal);
        Assert.DoesNotContain("respText[..", askSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTag(\"lex.question", askSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTag(\"client", askSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTag(\"ip", askSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Operation_planner_uses_low_reasoning_for_bounded_routing_latency()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lex.Ask", "AskService.cs"));
        var start = source.IndexOf(
            "private async Task<(OperationPlan Plan, ModelTokenUsage Usage)> PlanOperationsAsync(",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        var requestStart = source.IndexOf("var req = new JsonObject", start, StringComparison.Ordinal);
        var requestEnd = source.IndexOf("using var httpReq", requestStart, StringComparison.Ordinal);
        Assert.True(requestStart > start && requestEnd > requestStart);
        var efforts = Regex.Matches(source[requestStart..requestEnd],
            "\\[\"reasoning_effort\"\\]\\s*=\\s*\"(?<value>[^\"]+)\"");

        var effort = Assert.Single(efforts.Cast<Match>());
        Assert.Equal("low", effort.Groups["value"].Value);
    }

    [Fact]
    public void EurLex_professional_names_live_in_reviewed_data_not_adapter_code()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lex.Sources.EurLex", "EurLexAdapter.cs"));

        Assert.DoesNotContain("CommonNames", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"GDPR\"", source, StringComparison.Ordinal);
    }

    // F1 — foundations know nothing about regulation or any publisher.
    [Theory]
    [InlineData("Lex.Temporal")]
    [InlineData("Lex.Index")]
    public void F1_foundations_reference_no_publisher(string project)
    {
        foreach (var file in Sources(project))
        {
            var text = File.ReadAllText(file).ToLowerInvariant();
            foreach (var w in PublisherWords)
                Assert.False(text.Contains(w), $"{file} contains forbidden publisher token '{w}'");
        }
    }

    // F2 — the neutral model contains no publisher knowledge.
    [Fact]
    public void F2_lex_law_is_publisher_pure()
    {
        foreach (var file in Sources("Lex.Law"))
        {
            var text = File.ReadAllText(file).ToLowerInvariant();
            foreach (var w in PublisherWords)
                Assert.False(text.Contains(w), $"{file} contains forbidden publisher token '{w}'");
        }
    }

    // F3 — dependency direction: L1 references no L2/L3; L2 references no L3; nothing below Apps references adapters.
    [Fact]
    public void F3_dependency_direction()
    {
        static string[] Refs(Type anchor) =>
            anchor.Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();

        var temporalRefs = Refs(typeof(Lex.Temporal.DateInterval));
        Assert.DoesNotContain(temporalRefs, n => n.StartsWith("Lex."));

        var indexRefs = Refs(typeof(Lex.Index.DocRow));
        Assert.DoesNotContain(indexRefs, n => n == "Lex.Law" || n.StartsWith("Lex.Sources"));

        var lawRefs = Refs(typeof(Lex.Law.Tier));
        Assert.DoesNotContain(lawRefs, n => n.StartsWith("Lex.Sources") || n == "Lex.Index");
    }

    // F5 — the query entry points take a non-optional FilterSet (compile-shape check).
    [Fact]
    public void F5_query_methods_require_filterset()
    {
        var reader = typeof(Lex.Index.LexIndexReader);
        foreach (var name in new[] { "AsOf", "InForceOn", "Search" })
        {
            var m = reader.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(m);
            Assert.Contains(m!.GetParameters(), p => p.ParameterType == typeof(Lex.Index.FilterSet) && !p.HasDefaultValue);
        }
    }

    // F8 — adapters never write to disk or invoke git (source-level check).
    [Theory]
    [InlineData("Lex.Sources.Legilux")]
    [InlineData("Lex.Sources.EurLex")]
    public void F8_adapters_do_not_write_disk_or_git(string project)
    {
        foreach (var file in Sources(project))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("File.Write", text);
            Assert.DoesNotContain("File.Create", text);
            Assert.DoesNotContain("Directory.Create", text);
            Assert.DoesNotContain("Process.Start", text);
            Assert.DoesNotContain("\"git\"", text);
        }
    }

    // F9 — no ambient clock inside index build code; time is injected.
    [Fact]
    public void F9_index_build_has_no_ambient_time()
    {
        foreach (var file in Sources("Lex.Index"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("DateTime.Now", text);
            Assert.DoesNotContain("DateTime.UtcNow", text);
            Assert.DoesNotContain("DateTimeOffset.Now", text);
            Assert.DoesNotContain("DateTimeOffset.UtcNow", text);
        }
    }

    // F10 — no model/generation SDK anywhere below the apps.
    [Fact]
    public void F10_no_generation_below_apps()
    {
        foreach (var project in new[] { "Lex.Temporal", "Lex.Law", "Lex.Index", "Lex.Sources.Legilux", "Lex.Sources.EurLex", "Lex.Mcp" })
        foreach (var file in Sources(project))
        {
            var text = File.ReadAllText(file).ToLowerInvariant();
            Assert.DoesNotContain("openai", text);
            Assert.DoesNotContain("anthropic", text);
            Assert.DoesNotContain("chatcompletion", text);
        }
    }

    // F11 — `raw` is written by the corpus writer and read by nothing else.
    [Fact]
    public void F11_raw_is_read_by_nothing()
    {
        foreach (var project in new[] { "Lex.Index", "Lex.Web", "Lex.Mcp" })
        foreach (var file in Sources(project))
            Assert.DoesNotContain(".Raw", File.ReadAllText(file));
    }

    // F14 — the composition root stays a composition root.
    //
    // Program.cs was 2,019 lines: every route, the HTML shell, the stylesheet and seven scattered
    // reads of the environment in one scope. Splitting it is worth nothing if the next feature
    // lands back in the same file, and "keep it small" is not a rule anyone remembers under
    // deadline. So it is a build failure instead.
    [Fact]
    public void F14_the_startup_file_registers_and_delegates_only()
    {
        var program = Path.Combine(RepoRoot(), "src", "Lex.Web", "Program.cs");
        var text = File.ReadAllText(program);
        var lines = File.ReadAllLines(program).Length;

        Assert.True(lines < 150,
            $"Program.cs is {lines} lines. It is the composition root: configuration, services, "
            + "and which module answers what. A route or a page belongs in an endpoint module.");

        // A route registered here is a route nobody will find later.
        Assert.DoesNotContain("app.MapGet(\"", text);
        Assert.DoesNotContain("app.MapPost(\"", text);

        // Markup here means the shell has started leaking back out of PageShell.
        Assert.DoesNotContain("<!DOCTYPE", text);
        Assert.DoesNotContain("<div", text);
    }

    // F15 — every endpoint module is reachable from the composition root.
    //
    // A module nobody calls compiles, tests clean, and serves nothing. The failure mode is a
    // 404 in production on a page that exists in the source.
    [Fact]
    public void F15_every_endpoint_module_is_mapped()
    {
        var webDir = Path.Combine(RepoRoot(), "src", "Lex.Web");
        var program = File.ReadAllText(Path.Combine(webDir, "Program.cs"));

        foreach (var file in Directory.EnumerateFiles(webDir, "*Endpoints.cs"))
        {
            var text = File.ReadAllText(file);
            var i = text.IndexOf("public static IEndpointRouteBuilder Map", StringComparison.Ordinal);
            Assert.True(i >= 0, $"{Path.GetFileName(file)} declares no Map* extension method.");

            var name = text[(i + "public static IEndpointRouteBuilder ".Length)..];
            name = name[..name.IndexOf('(')];
            Assert.Contains($".{name}(", program);
        }
    }

    // F12 — no page states a tool count as a literal.
    //
    // Three places carried one by hand and all three were wrong at once: the /developers lede said
    // "Eight", its own heading said "nine", and the endpoint served ten. A number that must be
    // remembered on every ship is a number that will be wrong, so counts are interpolated from
    // ToolDefs and this test keeps them that way.
    [Fact]
    public void F12_no_page_hard_codes_a_tool_count()
    {
        string[] literals =
        [
            "seven tools", "eight tools", "nine tools", "ten tools", "eleven tools",
            "7 tools", "8 tools", "9 tools", "10 tools", "11 tools",
        ];
        foreach (var file in Sources("Lex.Web"))
        {
            var text = File.ReadAllText(file);
            foreach (var bad in literals)
                Assert.False(text.Contains(bad, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(file)} states a tool count as a literal (\"{bad}\"); interpolate ToolDefs().Count instead");
        }
    }

    // F13 — a copy-paste connect command is never built from req.Scheme.
    //
    // Container Apps terminates TLS at the ingress, so req.Scheme is "http" in production and every
    // command on /ai and /developers handed out an insecure URL that a strict MCP client refuses.
    [Fact]
    public void F13_public_urls_do_not_trust_the_request_scheme()
    {
        foreach (var file in Sources("Lex.Web"))
            Assert.DoesNotContain("{req.Scheme}://", File.ReadAllText(file));
    }
}
