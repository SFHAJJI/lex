using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Law;

namespace Lex.Ingest;

public static class CorpusJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        // record_sha256 is computed from this serialization. Pin the newline so the
        // canonical record is identical on Windows developer machines and Linux CI.
        NewLine = "\r\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class WorkMeta
{
    public required string LexWorkId { get; set; }
    public required string WorkIdentifier { get; set; }
    public required string Publisher { get; set; }
    public string? DocumentType { get; set; }
    public required string Slug { get; set; }
    public string? Title { get; set; }
    public string? SourceUri { get; set; }
}

public sealed class TextInfo
{
    public required bool Available { get; set; }
    public string? Reason { get; set; }
    public string? Url { get; set; }
}

public sealed class EventEntry
{
    public required string Event { get; set; }
    public required string ObservedFrom { get; set; }
    public string? Scope { get; set; }
    public string? Detail { get; set; }
}

public sealed class ObservationEntry
{
    public string? File { get; set; }
    public string? Sha256 { get; set; }
    public required string SourceUri { get; set; }
    public required string RetrievedAt { get; set; }
    public required string ObservedFrom { get; set; }
    /// <summary>D48: manifestation format for alternative-format members (e.g. "fmx4"); null for the primary body.</summary>
    public string? Format { get; set; }
}

public sealed class ExpressionMeta
{
    public required string Language { get; set; }
    public string? ValidFrom { get; set; }
    public string? ValidTo { get; set; }
    public required string ValidTimeSource { get; set; }
    public string? Title { get; set; }
    public string? TitleShort { get; set; }
    public string? SourceUri { get; set; }
    public List<ObservationEntry> Observations { get; set; } = [];
    public required TextInfo Text { get; set; }
}

public sealed class VersionMeta
{
    public required string LexId { get; set; }
    public string? PublisherVersionIdentifier { get; set; }
    public required string WorkIdentifier { get; set; }
    public required string Publisher { get; set; }
    public string? DocumentType { get; set; }
    public required string ValidFrom { get; set; }
    public string? ValidTo { get; set; }
    public required string ValidTimeSource { get; set; }
    public string? InForceStatus { get; set; }
    public string? PublicationDate { get; set; }
    public List<EventEntry> Events { get; set; } = [];
    public List<ExpressionMeta> Expressions { get; set; } = [];
    public List<Dictionary<string, string>> Relations { get; set; } = [];
    public Dictionary<string, string> Raw { get; set; } = [];
    public List<PublisherMetadataRecord>? PublisherMetadata { get; set; }
    public List<string>? DocumentRoles { get; set; }
    public string? RecordSha256 { get; set; }
}

public sealed class ManifestDoc
{
    public const string CurrentSchema = "lex-corpus/4";
    public const string CurrentPublisherDiscoverySchema = "publisher-discovery/1";
    public string Schema { get; set; } = CurrentSchema;
    public required Dictionary<string, string> Publisher { get; set; }
    public required string Tier { get; set; }
    public string? SourceEndpoint { get; set; }
    public required string Attribution { get; set; }
    public string? SourceTermsUrl { get; set; }
    public required bool TextIncluded { get; set; }
    public required bool TextPublic { get; set; }
    public string? Modifications { get; set; }
    public List<Dictionary<string, object>> DocumentTypes { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public int Works { get; set; }
    public int Versions { get; set; }
    public int Expressions { get; set; }
    public int ExpressionsWithText { get; set; }
    public int ExpressionsWithoutText { get; set; }
    public int? ScopeExpectedWorks { get; set; }
    public int AcquisitionRetryMaximumAttempts { get; set; } = 1;
    public List<SourceBuildIssue> BuildIssues { get; set; } = [];
    public string? ValidFromEarliest { get; set; }
    public string? ValidToLatest { get; set; }
    public required string HistoryBegins { get; set; }
    public required string IngesterVersion { get; set; }
    public string? IngesterCodeCommit { get; set; }
    public int? MigrationBaselineWorks { get; set; }
    public string? PublisherDiscoverySchema { get; set; }
}

public sealed class SourceEnumerationIncompleteException(SourceBuildIssue issue)
    : Exception($"{issue.Code}: {issue.Work}: {issue.Detail}")
{
    public SourceBuildIssue Issue { get; } = issue;
}
