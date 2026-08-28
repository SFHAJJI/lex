using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Law;

namespace Lex.Ingest;

internal sealed class CompletedRunLedgerDoc
{
    public const string CurrentSchema = "lex-completed-runs/1";
    public string Schema { get; set; } = CurrentSchema;
    public List<CompletedRunEntry> Runs { get; set; } = [];
}

internal sealed class CompletedRunEntry
{
    public required string RunIdentity { get; set; }
    public required string CompletedAt { get; set; }
    public required string EnumerationScopeSha256 { get; set; }
    public string? PreviousEntrySha256 { get; set; }
    public required string EntrySha256 { get; set; }
}

internal enum CompletedRunDisposition { New, ExactReplay }

internal static class CompletedRunLedger
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const int MaximumRuns = 100_000;
    private const string FileName = "completed-runs.json";

    public static CompletedRunLedgerDoc Load(string corpusRoot)
    {
        var path = Path.Combine(corpusRoot, FileName);
        if (!File.Exists(path)) return new CompletedRunLedgerDoc();
        path = VerifiedCorpusPath.RequireExisting(corpusRoot, path, "completed-run ledger");
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("The completed-run ledger exceeds its size limit.");
        var options = new JsonSerializerOptions(CorpusJson.Options)
        {
            MaxDepth = 8,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var ledger = JsonSerializer.Deserialize<CompletedRunLedgerDoc>(
            stream, options)
            ?? throw new InvalidDataException("The completed-run ledger is empty.");
        Validate(ledger);
        return ledger;
    }

    public static CompletedRunDisposition Bind(
        CompletedRunLedgerDoc ledger, string runIdentity, string completedAt,
        string enumerationScopeSha256)
    {
        Validate(ledger);
        _ = IngestRunIdentity.Require(runIdentity, "completed-run ledger identity");
        _ = HistoricalWithdrawalAuditValidation.ParseCanonicalUtc(
            completedAt, "completed-run ledger completed_at");
        RequireSha256(enumerationScopeSha256, "enumeration scope digest");
        var existing = ledger.Runs.SingleOrDefault(run =>
            string.Equals(run.RunIdentity, runIdentity, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (string.Equals(existing.CompletedAt, completedAt, StringComparison.Ordinal)
                && CorpusHashes.Equal(existing.EnumerationScopeSha256,
                    enumerationScopeSha256))
                return CompletedRunDisposition.ExactReplay;
            throw new InvalidDataException(
                $"Completed source run identity '{runIdentity}' is already bound to a different completed run tuple.");
        }
        if (ledger.Runs.Count >= MaximumRuns)
            throw new InvalidDataException("The completed-run ledger reached its entry limit.");
        var previous = ledger.Runs.LastOrDefault()?.EntrySha256;
        ledger.Runs.Add(new CompletedRunEntry
        {
            RunIdentity = runIdentity,
            CompletedAt = completedAt,
            EnumerationScopeSha256 = enumerationScopeSha256,
            PreviousEntrySha256 = previous,
            EntrySha256 = EntryHash(runIdentity, completedAt,
                enumerationScopeSha256, previous),
        });
        return CompletedRunDisposition.New;
    }

    public static string Stage(
        CompletedRunLedgerDoc ledger, string corpusRoot, CorpusCandidate candidate)
    {
        var bytes = Serialize(ledger);
        candidate.WriteIfChanged(Path.Combine(corpusRoot, FileName),
            Encoding.UTF8.GetString(bytes).TrimEnd('\n'));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string Write(CompletedRunLedgerDoc ledger, string corpusRoot)
    {
        var bytes = Serialize(ledger);
        File.WriteAllBytes(Path.Combine(corpusRoot, FileName), bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string FileSha256(string corpusRoot)
    {
        var path = VerifiedCorpusPath.RequireExisting(
            corpusRoot, Path.Combine(corpusRoot, FileName),
            "completed-run ledger");
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException(
                "The completed-run ledger has an invalid size.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string EnumerationScopeDigest(
        PublisherDescriptor descriptor,
        string sourceConfigurationKind,
        string? sourceConfigurationSha256,
        int expectedWorks,
        int retryMaximumAttempts,
        IReadOnlyList<SourceBuildIssue> sourceIssues,
        IReadOnlyList<CorpusPlannedWork> enumeration)
    {
        if (descriptor.DocumentTypes
                .Select(value => (value.PublisherId, value.Code))
                .Distinct()
                .Count() != descriptor.DocumentTypes.Count)
            throw new InvalidDataException(
                "The publisher descriptor contains a duplicate document type.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add("lex-source-enumeration/1");
        Add(descriptor.Publisher.Id);
        Add(descriptor.Publisher.Name);
        Add(descriptor.Publisher.Jurisdiction);
        Add(descriptor.Publisher.Homepage);
        Add(descriptor.Publisher.Tier.ToString());
        Add(descriptor.Publisher.Attribution);
        AddNullable(descriptor.Publisher.SourceTermsUrl);
        Add(descriptor.TextIncluded ? "true" : "false");
        Add(descriptor.TextPublic ? "true" : "false");
        Add(descriptor.HistoryBegins);
        Add(descriptor.DocumentTypes.Count.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        foreach (var type in descriptor.DocumentTypes
                     .OrderBy(value => value.PublisherId, StringComparer.Ordinal)
                     .ThenBy(value => value.Code, StringComparer.Ordinal))
        {
            Add(type.PublisherId);
            Add(type.Code);
            Add(type.Labels.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            foreach (var (language, label) in type.Labels
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                Add(language);
                Add(label);
            }
        }
        Add(descriptor.Languages.Count.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        foreach (var language in descriptor.Languages.Order(StringComparer.Ordinal))
            Add(language);
        Add(sourceConfigurationKind);
        AddNullable(sourceConfigurationSha256);
        Add(expectedWorks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(retryMaximumAttempts.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        Add(sourceIssues.Count.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        foreach (var issue in sourceIssues
                     .OrderBy(value => value.Code, StringComparer.Ordinal)
                     .ThenBy(value => value.Work, StringComparer.Ordinal)
                     .ThenBy(value => value.Detail is null)
                     .ThenBy(value => value.Detail, StringComparer.Ordinal))
        {
            Add(issue.Code);
            Add(issue.Work);
            AddNullable(issue.Detail);
        }
        Add(enumeration.Count.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        foreach (var item in enumeration
                     .OrderBy(value => value.Work.Id.Value, StringComparer.Ordinal)
                     .ThenBy(value => value.Work.Slug, StringComparer.Ordinal))
        {
            Add(item.Work.Id.Value);
            Add(item.Work.Slug);
            AddNullable(item.Work.TypeCode);
            AddNullable(item.Work.TitleHint);
            Add(item.Versions.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            foreach (var version in item.Versions
                         .OrderBy(value => value.Id.Value, StringComparer.Ordinal))
            {
                Add(version.Id.Value);
                Add(version.WorkId.Value);
                AddNullable(version.TypeCode);
                Add(version.ValidFrom.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture));
                AddNullable(version.ValidTo?.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture));
                Add(version.ValidTimeSource);
                AddNullable(version.InForceStatus);
                AddNullable(version.PublicationDate?.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture));
                Add(version.Expressions.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                foreach (var expression in version.Expressions
                             .OrderBy(value => value.Language, StringComparer.Ordinal)
                             .ThenBy(value => value.ValidFrom?.DayNumber ?? int.MinValue)
                             .ThenBy(value => value.ValidTo?.DayNumber ?? int.MinValue)
                             .ThenBy(value => value.ValidTimeSource, StringComparer.Ordinal)
                             .ThenBy(value => value.Title is null)
                             .ThenBy(value => value.Title, StringComparer.Ordinal)
                             .ThenBy(value => value.TitleShort is null)
                             .ThenBy(value => value.TitleShort, StringComparer.Ordinal)
                             .ThenBy(value => value.SourceUri is null)
                             .ThenBy(value => value.SourceUri, StringComparer.Ordinal))
                {
                    Add(expression.Language);
                    AddNullable(expression.ValidFrom?.ToString("yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture));
                    AddNullable(expression.ValidTo?.ToString("yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture));
                    Add(expression.ValidTimeSource);
                    AddNullable(expression.Title);
                    AddNullable(expression.TitleShort);
                    AddNullable(expression.SourceUri);
                }
                Add(version.Relations.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                foreach (var relation in version.Relations
                             .OrderBy(value => value.Type, StringComparer.Ordinal)
                             .ThenBy(value => value.Target.Value, StringComparer.Ordinal))
                {
                    Add(relation.Type);
                    Add(relation.Target.Value);
                }
                Add(version.Raw.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                foreach (var (key, value) in version.Raw
                             .OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    Add(key);
                    Add(value);
                }
                var metadata = version.PublisherMetadata ?? [];
                Add(metadata.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                foreach (var record in metadata
                             .OrderBy(value => value.Kind, StringComparer.Ordinal)
                             .ThenBy(value => value.Identifier, StringComparer.Ordinal)
                             .ThenBy(value => value.Language is null)
                             .ThenBy(value => value.Language, StringComparer.Ordinal)
                             .ThenBy(value => value.Label is null)
                             .ThenBy(value => value.Label, StringComparer.Ordinal)
                             .ThenBy(value => value.SourceUri, StringComparer.Ordinal))
                {
                    Add(record.Kind);
                    Add(record.Identifier);
                    AddNullable(record.Language);
                    AddNullable(record.Label);
                    Add(record.SourceUri);
                }
                var roles = version.DocumentRoles ?? [];
                Add(roles.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                foreach (var role in roles.Order(StringComparer.Ordinal))
                    Add(role);
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
            hash.AppendData(bytes);
        }

        void AddNullable(string? value)
        {
            Add(value is null ? "null" : "value");
            if (value is not null) Add(value);
        }
    }

    public static void Validate(CompletedRunLedgerDoc ledger)
    {
        if (ledger.Runs is null
            || ledger.Schema != CompletedRunLedgerDoc.CurrentSchema
            || ledger.Runs.Count > MaximumRuns)
            throw new InvalidDataException("The completed-run ledger schema or count is invalid.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        foreach (var run in ledger.Runs)
        {
            if (run is null)
                throw new InvalidDataException(
                    "The completed-run ledger contains a null entry.");
            _ = IngestRunIdentity.Require(run.RunIdentity,
                "completed-run ledger identity");
            _ = HistoricalWithdrawalAuditValidation.ParseCanonicalUtc(
                run.CompletedAt, "completed-run ledger completed_at");
            RequireSha256(run.EnumerationScopeSha256,
                "completed-run enumeration scope digest");
            RequireSha256(run.EntrySha256, "completed-run entry digest");
            if (!identities.Add(run.RunIdentity)
                || !string.Equals(run.PreviousEntrySha256, previous,
                    StringComparison.Ordinal)
                || !CorpusHashes.Equal(run.EntrySha256, EntryHash(
                    run.RunIdentity, run.CompletedAt,
                    run.EnumerationScopeSha256, previous)))
                throw new InvalidDataException(
                    "The completed-run ledger identity or hash chain is invalid.");
            previous = run.EntrySha256;
        }
    }

    private static string EntryHash(string identity, string completedAt,
        string scopeDigest, string? previous) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            CompletedRunLedgerDoc.CurrentSchema, identity, completedAt,
            scopeDigest, previous ?? ""))));

    private static void RequireSha256(string value, string field)
    {
        if (value is null || value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"The {field} is not canonical SHA-256.");
    }

    private static byte[] Serialize(CompletedRunLedgerDoc ledger)
    {
        Validate(ledger);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
            JsonSerializer.Serialize(ledger, CorpusJson.Options).TrimEnd('\n') + "\n");
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException(
                "The completed-run ledger exceeds its size limit.");
        return bytes;
    }
}
