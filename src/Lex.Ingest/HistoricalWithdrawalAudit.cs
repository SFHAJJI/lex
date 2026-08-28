using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.Ingest;

/// <summary>
/// Operator-supplied evidence for legacy withdrawals that predate the structured three-run
/// absence lifecycle. The migration consumes this contract; it never manufactures audit rows.
/// </summary>
public sealed class HistoricalWithdrawalAuditDocument
{
    public const string CurrentSchema = "lex-historical-withdrawal-audit/1";
    public required string Schema { get; set; }
    public required string Publisher { get; set; }
    public List<HistoricalWithdrawalAuditEntry> Entries { get; set; } = [];

    public static HistoricalWithdrawalAuditDocument Load(string path)
    {
        const long maximumBytes = 1_048_576;
        using var stream = new FileStream(
            Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException(
                $"Historical withdrawal audit exceeds {maximumBytes} bytes.");

        var options = new JsonSerializerOptions(CorpusJson.Options)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 16,
        };
        return JsonSerializer.Deserialize<HistoricalWithdrawalAuditDocument>(stream, options)
            ?? throw new InvalidDataException("Historical withdrawal audit is empty.");
    }
}

public sealed class HistoricalWithdrawalAuditEntry
{
    public required string WorkIdentifier { get; set; }
    public required string PublisherVersionIdentifier { get; set; }
    public List<HistoricalWithdrawalAuditRun> CompletedRuns { get; set; } = [];
}

public sealed class HistoricalWithdrawalAuditRun
{
    public required string RunIdentity { get; set; }
    public required string CompletedAt { get; set; }
}

internal readonly record struct HistoricalWithdrawalAuditKey(
    string WorkIdentifier,
    string PublisherVersionIdentifier);

internal sealed record HistoricalWithdrawalAuditRunEvidence(
    string RunIdentity,
    string CompletedAt);

internal static class HistoricalWithdrawalAuditValidation
{
    public const string EventDetailPrefix = "historical audit: ";

    public static bool IsAuditCorrection(EventEntry entry) =>
        entry.Detail?.StartsWith(EventDetailPrefix, StringComparison.Ordinal) == true;

    public static string ScopeDigest(
        string publisher,
        IReadOnlyDictionary<HistoricalWithdrawalAuditKey,
            IReadOnlyList<HistoricalWithdrawalAuditRunEvidence>> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(HistoricalWithdrawalAuditDocument.CurrentSchema);
        Add(publisher);
        foreach (var (key, runs) in entries
                     .OrderBy(value => value.Key.WorkIdentifier, StringComparer.Ordinal)
                     .ThenBy(value => value.Key.PublisherVersionIdentifier,
                         StringComparer.Ordinal))
        {
            Add(key.WorkIdentifier);
            Add(key.PublisherVersionIdentifier);
            foreach (var run in runs
                         .OrderBy(value => value.CompletedAt, StringComparer.Ordinal)
                         .ThenBy(value => value.RunIdentity, StringComparer.Ordinal))
            {
                Add(run.RunIdentity);
                Add(run.CompletedAt);
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(
                System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
            hash.AppendData(bytes);
        }
    }

    public static IReadOnlyDictionary<HistoricalWithdrawalAuditKey,
        IReadOnlyList<HistoricalWithdrawalAuditRunEvidence>> Require(
        HistoricalWithdrawalAuditDocument? document,
        string publisher,
        DateTimeOffset migrationStartedAt,
        IReadOnlyList<(string WorkIdentifier, string PublisherVersionIdentifier)> required)
    {
        if (required.Count == 0 && document is null)
            return new Dictionary<HistoricalWithdrawalAuditKey,
                IReadOnlyList<HistoricalWithdrawalAuditRunEvidence>>();
        if (document is null)
            throw new InvalidDataException(
                "A historical withdrawal audit is required for every legacy withdrawal.");

        var entries = ValidateDocument(document, publisher, migrationStartedAt);
        var requiredKeys = required.Select(item => new HistoricalWithdrawalAuditKey(
                item.WorkIdentifier, item.PublisherVersionIdentifier))
            .ToHashSet();
        var missing = requiredKeys.Where(key => !entries.ContainsKey(key)).ToArray();
        var extra = entries.Keys.Where(key => !requiredKeys.Contains(key)).ToArray();
        if (missing.Length != 0 || extra.Length != 0)
            throw new InvalidDataException(
                $"Historical withdrawal audit does not exactly cover the migrated states "
                + $"(missing={missing.Length}, extra={extra.Length}).");
        return entries;
    }

    private static Dictionary<HistoricalWithdrawalAuditKey,
        IReadOnlyList<HistoricalWithdrawalAuditRunEvidence>> ValidateDocument(
        HistoricalWithdrawalAuditDocument document,
        string publisher,
        DateTimeOffset migrationStartedAt)
    {
        if (!string.Equals(document.Schema,
                HistoricalWithdrawalAuditDocument.CurrentSchema, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Historical withdrawal audit schema must be "
                + $"'{HistoricalWithdrawalAuditDocument.CurrentSchema}'.");
        if (!string.Equals(document.Publisher, publisher, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Historical withdrawal audit publisher does not match the migration publisher.");
        if (document.Entries is null || document.Entries.Count > 1_000)
            throw new InvalidDataException(
                "Historical withdrawal audit entries must be a bounded array.");

        var result = new Dictionary<HistoricalWithdrawalAuditKey,
            IReadOnlyList<HistoricalWithdrawalAuditRunEvidence>>();
        foreach (var entry in document.Entries)
        {
            if (entry is null
                || string.IsNullOrWhiteSpace(entry.WorkIdentifier)
                || entry.WorkIdentifier.Length > 2_000
                || string.IsNullOrWhiteSpace(entry.PublisherVersionIdentifier)
                || entry.PublisherVersionIdentifier.Length > 2_000
                || entry.CompletedRuns is null
                || entry.CompletedRuns.Count != 3)
                throw new InvalidDataException(
                    "Each historical withdrawal audit entry must identify one state and "
                    + "exactly three completed runs.");

            var key = new HistoricalWithdrawalAuditKey(
                entry.WorkIdentifier, entry.PublisherVersionIdentifier);
            if (result.ContainsKey(key))
                throw new InvalidDataException(
                    "Historical withdrawal audit contains a duplicate state.");

            var identities = new HashSet<string>(StringComparer.Ordinal);
            DateTimeOffset? previous = null;
            var evidence = new List<HistoricalWithdrawalAuditRunEvidence>(3);
            foreach (var run in entry.CompletedRuns)
            {
                if (run is null)
                    throw new InvalidDataException(
                        "Historical withdrawal audit contains a null completed run.");
                _ = IngestRunIdentity.Require(
                    run.RunIdentity, "historical withdrawal audit run identity");
                if (!identities.Add(run.RunIdentity))
                    throw new InvalidDataException(
                        "Historical withdrawal audit run identities must be distinct.");
                var completed = ParseCanonicalUtc(
                    run.CompletedAt, "historical withdrawal audit completed_at");
                if (previous is not null && completed <= previous.Value)
                    throw new InvalidDataException(
                        "Historical withdrawal audit completed runs must be strictly ordered.");
                if (completed > migrationStartedAt)
                    throw new InvalidDataException(
                        "Historical withdrawal audit cannot contain a future completed run.");
                previous = completed;
                evidence.Add(new HistoricalWithdrawalAuditRunEvidence(
                    run.RunIdentity, run.CompletedAt));
            }
            result.Add(key, evidence.AsReadOnly());
        }
        return result;
    }

    internal static DateTimeOffset ParseCanonicalUtc(string? value, string description)
    {
        if (value is null
            || !DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            throw new InvalidDataException(
                $"{description} must be canonical UTC seconds.");
        return parsed;
    }
}
