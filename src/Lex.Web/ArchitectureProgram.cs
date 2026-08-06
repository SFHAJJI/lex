using System.Text.Json;

namespace Lex.Web;

/// <summary>
/// The single status registry behind the public current, next, decisions and benchmarks pages.
/// It is embedded at build time so a deployed page cannot silently read a newer plan from disk
/// than the code that renders it.
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
    CurrentArchitecture Current,
    IReadOnlyList<ArchitectureMilestone> Milestones,
    IReadOnlyList<ArchitectureDecision> Decisions,
    ArchitectureBaseline Baseline);

public sealed record CurrentArchitecture(
    string Retrieval,
    string IndexSchema,
    string Hosting,
    string Region,
    string Resource,
    string Scale,
    string ObservedAt,
    string StructuredContract,
    string ComparisonContract);

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
