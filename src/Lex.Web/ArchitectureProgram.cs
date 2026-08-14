using System.Text.Json;

namespace Lex.Web;

/// <summary>
/// The reviewed implementation-maturity, decision and benchmark registry used by the public
/// evidence pages. It is embedded at build time so a deployed page cannot silently read a newer
/// plan from disk than the code that renders it. Runtime and mounted release identities do not
/// belong here; endpoints read those from process configuration and verified indexes.
/// </summary>
public static class ArchitectureProgram
{
    public static ArchitectureRegistry Registry { get; } = Load();

    private static ArchitectureRegistry Load()
    {
        using var stream = typeof(ArchitectureProgram).Assembly
            .GetManifestResourceStream("Lex.Web.architecture-program.json")
            ?? throw new InvalidOperationException("Embedded architecture-program.json is missing.");
        return JsonSerializer.Deserialize<ArchitectureRegistry>(stream,
                   new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
               ?? throw new InvalidOperationException("Embedded architecture-program.json is invalid.");
    }
}

public sealed record ArchitectureRegistry(
    string ProgramVersion,
    string UpdatedAt,
    string ReviewStatus,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<ArchitectureMilestone> Milestones,
    IReadOnlyList<ArchitectureDecision> Decisions,
    ArchitectureBaseline Baseline);

public sealed record ArchitectureMilestone(string Id, string Title, string Status, string Outcome);

public sealed record ArchitectureDecision(
    string Id,
    string Title,
    string Status,
    string Choice,
    string Alternative,
    string Reason,
    string Cost);

public sealed record ArchitectureBaseline(
    string Kind,
    string MeasuredAt,
    string CodeCommit,
    string LiveEuCorpusCommit,
    string LiveLuCorpusCommit,
    int McpRequests7dSampled,
    double McpInternalP50Ms,
    double McpInternalP95Ms,
    double McpInternalP99Ms,
    int AverageWorkingSetMib,
    JsonElement? RelevanceBenchmark,
    string Note);
