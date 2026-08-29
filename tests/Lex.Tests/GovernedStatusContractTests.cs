using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lex.Tests;

/// <summary>
/// The producer-bound status contract (Codex ACK of 2026-08-28: stop hand-writing validation
/// predicates, derive governed validation from the authoritative producer).
///
/// Four consecutive review rounds on the browser's governed-response boundary each found a
/// producer status or shape the author had not modelled, and twice a repair for one false
/// claim manufactured another. The cause was method: predicates written from a producer read
/// in fragments. This test removes the fragment. It parses the McpCore case body for every
/// governed tool, extracts the statuses that tool can actually emit, and fails when the
/// contract and the producer disagree in either direction.
///
/// A new producer status without a contract entry is now a build error, not a review round.
/// </summary>
public sealed class GovernedStatusContractTests
{
    private static readonly string[] GovernedTools =
        ["search", "changes_in_period", "in_force_on"];

    [Fact]
    public void The_contract_names_exactly_the_statuses_the_producer_emits()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lex.Mcp", "McpCore.cs"));
        var statusLiterals = StatusLiterals(source);
        var contract = Contract();
        var tools = (JsonObject)contract["tools"]!;

        foreach (var tool in GovernedTools)
        {
            var emitted = EmittedStatuses(source, tool, statusLiterals);
            Assert.True(emitted.Count > 0,
                $"{tool}: the case body scan found no statuses; the parser has drifted");

            var declared = ((JsonObject)tools[tool]!)
                .Select(entry => entry.Key)
                .ToHashSet(StringComparer.Ordinal);

            var undeclared = emitted.Except(declared).Order(StringComparer.Ordinal).ToArray();
            Assert.True(undeclared.Length == 0,
                $"{tool} can emit {string.Join(", ", undeclared)} with no contract entry. "
                + "Add the entry and the client rule together, or the clients will classify a "
                + "real producer response as malformed.");

            var phantom = declared.Except(emitted).Order(StringComparer.Ordinal).ToArray();
            Assert.True(phantom.Length == 0,
                $"{tool} declares {string.Join(", ", phantom)} which the producer cannot emit. "
                + "A phantom entry lets a client admit a status no publisher will ever send.");
        }
    }

    [Fact]
    public void Every_declared_classification_is_one_the_contract_defines()
    {
        var contract = Contract();
        var known = ((JsonObject)contract["classifications"]!)
            .Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ran", known);

        foreach (var tool in ((JsonObject)contract["tools"]!))
            foreach (var entry in (JsonObject)tool.Value!)
                Assert.True(known.Contains(entry.Value!.GetValue<string>()),
                    $"{tool.Key}.{entry.Key} names classification "
                    + $"'{entry.Value!.GetValue<string>()}' which the contract does not define");
    }

    [Fact]
    public void Every_governed_tool_has_a_rows_field_and_row_requirements()
    {
        // A client that validates only the outer array admits hits:[{}] and then throws while
        // reading the row. Each tool states the field its rows live in and what a renderer
        // actually reads from one.
        var rules = (JsonObject)Contract()["shape_rules"]!;
        foreach (var tool in GovernedTools)
        {
            var rule = Assert.IsType<JsonObject>(rules[tool]);
            Assert.False(string.IsNullOrWhiteSpace(rule["rows_field"]?.GetValue<string>()),
                $"{tool} declares no rows field");
            var required = Assert.IsType<JsonArray>(rule["row_required_fields"]);
            Assert.NotEmpty(required);
        }
    }

    /// <summary>Every McpStatus constant and its wire value, read from the status register.</summary>
    private static Dictionary<string, string> StatusLiterals(string _)
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lex.Mcp", "McpStatus.cs"));
        return Regex.Matches(source,
                @"public const string (?<name>\w+)\s*=\s*""(?<value>[a-z_]+)""")
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The statuses one governed tool's case body can emit. The body runs from its own case
    /// label to the next one, which is how the operations are laid out in the dispatch switch.
    /// The capability refusal arrives through the shared UnsupportedFilterResult helper rather
    /// than a literal, so it is added when that helper is called.
    /// </summary>
    private static HashSet<string> EmittedStatuses(
        string source, string tool, Dictionary<string, string> literals)
    {
        var start = source.IndexOf($"case \"{tool}\":", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the {tool} case label moved; retune the scan");
        var next = Regex.Match(source[(start + 1)..], "case \"[a-z_]+\":");
        var body = next.Success
            ? source.Substring(start, next.Index + 1)
            : source[start..];

        var emitted = Regex.Matches(body, @"McpStatus\.(?<name>[A-Za-z]+)")
            .Select(m => m.Groups["name"].Value)
            .Where(literals.ContainsKey)
            .Select(name => literals[name])
            .ToHashSet(StringComparer.Ordinal);

        if (body.Contains("UnsupportedFilterResult", StringComparison.Ordinal))
            emitted.Add(literals["FilterNotSupportedByIndex"]);

        return emitted;
    }

    private static JsonObject Contract() => (JsonObject)JsonNode.Parse(File.ReadAllText(
        Path.Combine(RepoRoot(), "tests", "Lex.Tests", "governed-status-contract.json")))!;

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Lex.slnx")))
            directory = Directory.GetParent(directory)?.FullName
                        ?? throw new InvalidOperationException("Repository root not found.");
        return directory;
    }
}
