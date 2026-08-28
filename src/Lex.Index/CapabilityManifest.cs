using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lex.Index;

public sealed record CapabilityManifestEntry(
    string Filter,
    string Language,
    string TimeScope,
    string? PeriodStart,
    string? PeriodEnd,
    long EligibleRows,
    long PopulatedRows)
{
    public bool Supported => EligibleRows > 0 && PopulatedRows == EligibleRows;
}

public enum CapabilityTimeScope
{
    AllVersions,
    AsOf,
}

public sealed class CapabilityBuildExpectation
{
    private CapabilityBuildExpectation(
        string tier,
        string? collection,
        IReadOnlyList<string> unsupportedFilters,
        IReadOnlyList<CapabilityManifestEntry>? exactEntries,
        string policySha256)
    {
        Tier = tier;
        Collection = collection;
        UnsupportedFilters = unsupportedFilters;
        ExactEntries = exactEntries;
        PolicySha256 = policySha256;
    }

    public string Tier { get; }
    public string? Collection { get; }
    public IReadOnlyList<string> UnsupportedFilters { get; }
    public IReadOnlyList<CapabilityManifestEntry>? ExactEntries { get; }
    public string PolicySha256 { get; }

    public static CapabilityBuildExpectation Fixture(
        IReadOnlyList<CapabilityManifestEntry> exactEntries)
    {
        ArgumentNullException.ThrowIfNull(exactEntries);
        var copy = exactEntries.ToArray();
        return new CapabilityBuildExpectation(
            "fixture", null, [], copy, CapabilityManifest.Digest(copy));
    }

    public static CapabilityBuildExpectation Production(
        string collection,
        IReadOnlyList<string> unsupportedFilters,
        string policySha256)
    {
        if (string.IsNullOrWhiteSpace(collection))
            throw new ArgumentException("Collection is required.", nameof(collection));
        CapabilityManifest.RequireDigest(policySha256, nameof(policySha256));
        var filters = unsupportedFilters.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        CapabilityManifest.RequireKnownFilters(filters);
        return new CapabilityBuildExpectation(
            "production", collection, filters, null, policySha256.ToLowerInvariant());
    }
}

public static class CapabilityManifest
{
    public const string Schema = "lex-capability-manifest/1";
    public const string AllLanguages = "*";
    public const string AllVersions = "all_versions";
    public const string AsOf = "as_of";
    internal const int MaximumRows = 250_000;

    private static IReadOnlyList<string> CapabilityStampKeys { get; } =
        Array.AsReadOnly(new[]
        {
            "capability_manifest_schema",
            "capability_manifest_rows",
            "capability_manifest_sha256",
            "capability_manifest_unsupported_filters",
            "capability_policy_tier",
            "capability_policy_sha256",
        });

    public static IReadOnlyList<string> GovernedFilters { get; } = Array.AsReadOnly(new[]
    {
        "act_form",
        "binding_status",
        "domain",
        "hierarchy",
    });

    internal static IReadOnlyList<CapabilityManifestEntry> Build(
        IReadOnlyList<DocRow> documents)
    {
        var sources = documents.Select(document => new CapabilityDocument(
            document.Key, document.Language, document.ValidFrom, document.ValidTo,
            document.Withdrawn, document.Hierarchy, document.Domains,
            document.ActForm, document.BindingStatus)).ToArray();
        return BuildSources(sources);
    }

    internal static IReadOnlyList<CapabilityManifestEntry> Recompute(
        SqliteConnection connection)
    {
        var sources = new List<CapabilityDocument>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key,language,valid_from,valid_to,withdrawn,
                   hierarchy,domains,act_form,binding_status
            FROM docs
            ORDER BY key,language,valid_from
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            sources.Add(new CapabilityDocument(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt64(4) != 0,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        return BuildSources(sources);
    }

    private static IReadOnlyList<CapabilityManifestEntry> BuildSources(
        IReadOnlyList<CapabilityDocument> documents)
    {
        var rows = new List<CapabilityManifestEntry>();
        var activeDocuments = documents.Where(document => !document.Withdrawn)
            .Select(document => new DatedDocument(
                document,
                ParseDate(document.ValidFrom, document.Key, "valid_from"),
                ParseOptionalDate(document.ValidTo, document.Key, "valid_to")))
            .ToArray();
        foreach (var item in activeDocuments)
        {
            ValidateSourceLanguage(item.Document.Language, item.Document.Key);
            if (item.To is { } to && to < item.From)
                throw new InvalidDataException(
                    $"Capability source {item.Document.Key} has valid_to before valid_from.");
        }
        var languages = activeDocuments.Select(document => document.Document.Language)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Prepend(AllLanguages)
            .ToArray();

        foreach (var filter in GovernedFilters)
        foreach (var language in languages)
        {
            var eligible = activeDocuments.Where(document => language == AllLanguages
                || string.Equals(document.Document.Language, language,
                    StringComparison.Ordinal)).ToArray();
            if (eligible.Length == 0) continue;
            rows.Add(new CapabilityManifestEntry(
                filter, language, AllVersions, null, null,
                eligible.LongLength,
                eligible.LongCount(document => Populated(document.Document, filter))));

            var events = new SortedDictionary<DateOnly, PopulationDelta>();
            foreach (var item in eligible)
            {
                var populated = Populated(item.Document, filter) ? 1L : 0L;
                AddDelta(events, item.From, 1, populated);
                if (item.To is { } to && to < DateOnly.MaxValue)
                    AddDelta(events, to.AddDays(1), -1, -populated);
            }
            var ordered = events.ToArray();
            long eligibleCount = 0;
            long populatedCount = 0;
            CapabilityManifestEntry? previous = null;
            for (var index = 0; index < ordered.Length; index++)
            {
                var start = ordered[index].Key;
                eligibleCount += ordered[index].Value.Eligible;
                populatedCount += ordered[index].Value.Populated;
                if (eligibleCount < 0 || populatedCount < 0
                    || populatedCount > eligibleCount)
                    throw new InvalidDataException(
                        "Capability population sweep produced invalid counts.");
                DateOnly? end = index + 1 < ordered.Length
                    ? ordered[index + 1].Key.AddDays(-1) : null;
                if (eligibleCount == 0)
                {
                    previous = null;
                    continue;
                }
                var entry = new CapabilityManifestEntry(
                    filter, language, AsOf, Iso(start), end is { } finite ? Iso(finite) : null,
                    eligibleCount, populatedCount);
                if (previous is not null
                    && previous.EligibleRows == entry.EligibleRows
                    && previous.PopulatedRows == entry.PopulatedRows
                    && previous.PeriodEnd is { } priorEnd
                    && ParseDate(priorEnd, "manifest", "period_end").AddDays(1) == start)
                {
                    rows[^1] = previous = previous with { PeriodEnd = entry.PeriodEnd };
                }
                else
                {
                    rows.Add(entry);
                    previous = entry;
                }
            }
        }

        if (rows.Count > MaximumRows)
            throw new InvalidDataException(
                $"Capability manifest has {rows.Count} rows; maximum is {MaximumRows}.");
        ValidateStructure(rows);
        return rows;
    }

    private static void AddDelta(
        IDictionary<DateOnly, PopulationDelta> events,
        DateOnly date,
        long eligible,
        long populated)
    {
        events.TryGetValue(date, out var current);
        events[date] = new PopulationDelta(
            current.Eligible + eligible, current.Populated + populated);
    }

    internal static void ValidateExpectation(
        string collection,
        IReadOnlyList<CapabilityManifestEntry> rows,
        CapabilityBuildExpectation? expectation)
    {
        if (expectation is null) return;
        if (expectation.ExactEntries is { } exact)
        {
            if (!rows.SequenceEqual(exact))
                throw new InvalidDataException(
                    "Capability manifest does not match the hand-written fixture expectation.");
            return;
        }
        if (!string.Equals(collection, expectation.Collection, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Capability policy is for '{expectation.Collection}', not '{collection}'.");

        var expectedUnsupported = expectation.UnsupportedFilters.ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var filter in GovernedFilters)
        {
            var slices = rows.Where(row => row.Filter == filter).ToArray();
            if (expectedUnsupported.Contains(filter))
            {
                if (slices.Length == 0 || slices.Any(row => row.PopulatedRows != 0))
                    failures.Add($"{filter}: expected zero populated rows in every slice");
            }
            else if (slices.Length == 0 || slices.Any(row => !row.Supported))
            {
                failures.Add($"{filter}: expected complete population in every slice");
            }
        }
        if (failures.Count > 0)
            throw new InvalidDataException(
                "Capability policy mismatch: " + string.Join("; ", failures));
    }

    internal static string Digest(IEnumerable<CapabilityManifestEntry> entries)
    {
        var output = new StringBuilder();
        foreach (var entry in entries)
        {
            Append(output, entry.Filter);
            Append(output, entry.Language);
            Append(output, entry.TimeScope);
            Append(output, entry.PeriodStart);
            Append(output, entry.PeriodEnd);
            Append(output, entry.EligibleRows.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(output, entry.PopulatedRows.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            output.Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())));
    }

    internal static IReadOnlyList<string> UnsupportedFilters(
        IReadOnlyList<CapabilityManifestEntry> rows,
        FilterSet filters,
        CapabilityTimeScope timeScope,
        DateOnly? asOf,
        bool legacy)
    {
        var requested = RequestedFilters(filters);
        if (legacy) return requested;
        var language = filters.Language ?? AllLanguages;
        return requested.Where(filter => !Supported(
                rows, filter, language, timeScope, asOf))
            .Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> UnsupportedFiltersInPeriod(
        IReadOnlyList<CapabilityManifestEntry> rows,
        FilterSet filters,
        DateOnly from,
        DateOnly to,
        bool legacy)
    {
        if (from > to)
            throw new ArgumentException("The capability period start must not follow its end.");
        var requested = RequestedFilters(filters);
        if (legacy) return requested;
        var language = filters.Language ?? AllLanguages;
        return requested.Where(filter => !SupportedInPeriod(
                rows, filter, language, from, to))
            .Order(StringComparer.Ordinal).ToArray();
    }

    internal static (
        IReadOnlyList<CapabilityManifestEntry> Rows,
        bool Legacy) Read(
            SqliteConnection connection,
            IReadOnlyDictionary<string, string> stamp,
            string dbPath)
    {
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='capability_manifest'";
        var hasTable = tableCommand.ExecuteScalar() is not null;
        var capabilityStampKeys = stamp.Keys.Where(key =>
            key.StartsWith("capability_", StringComparison.Ordinal)).ToArray();
        var unknownKeys = capabilityStampKeys.Where(key =>
                !CapabilityStampKeys.Contains(key, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        if (unknownKeys.Length > 0)
            throw new InvalidDataException(
                $"Index {dbPath} has unknown capability stamp claim(s): "
                + string.Join(", ", unknownKeys));
        if (!hasTable && capabilityStampKeys.Length == 0)
            return ([], true);
        if (!hasTable)
            throw new InvalidDataException(
                $"Index {dbPath} claims a capability manifest but has no manifest table.");

        var missingKeys = CapabilityStampKeys.Where(key => !stamp.ContainsKey(key)).ToArray();
        if (missingKeys.Length > 0)
            throw new InvalidDataException(
                $"Index {dbPath} has a capability table without signed claim(s): "
                + string.Join(", ", missingKeys));
        if (stamp["capability_manifest_schema"] != Schema)
            throw new InvalidDataException(
                $"Index {dbPath} has unsupported capability schema "
                + $"'{stamp["capability_manifest_schema"]}'.");
        if (!int.TryParse(stamp["capability_manifest_rows"],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var expectedRows)
            || expectedRows < 0 || expectedRows > MaximumRows)
            throw new InvalidDataException(
                $"Index {dbPath} has an invalid capability row count.");
        try
        {
            RequireDigest(stamp["capability_manifest_sha256"], "capability_manifest_sha256");
            RequireDigest(stamp["capability_policy_sha256"], "capability_policy_sha256");
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException(
                $"Index {dbPath} has an invalid capability digest claim.", error);
        }
        if (stamp["capability_policy_tier"] is not ("fixture" or "production" or "unchecked"))
            throw new InvalidDataException(
                $"Index {dbPath} has an invalid capability policy tier.");

        var rows = new List<CapabilityManifestEntry>(Math.Min(expectedRows, MaximumRows));
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT filter_name,language,time_scope,period_start,period_end,
                       eligible_rows,populated_rows
                FROM capability_manifest
                ORDER BY filter_name,language,
                         CASE time_scope WHEN 'all_versions' THEN 0 ELSE 1 END,
                         period_start,period_end
                LIMIT 250001
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new CapabilityManifestEntry(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    EmptyToNull(reader.GetString(3)), EmptyToNull(reader.GetString(4)),
                    reader.GetInt64(5), reader.GetInt64(6));
                ValidateEntry(row);
                rows.Add(row);
            }
        }
        if (rows.Count > MaximumRows || rows.Count != expectedRows)
            throw new InvalidDataException(
                $"Index {dbPath} capability row count does not match its stamp.");
        ValidateStructure(rows);
        var digest = Digest(rows);
        if (!string.Equals(digest, stamp["capability_manifest_sha256"],
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Index {dbPath} capability manifest does not match its signed digest.");
        var unsupported = GovernedFilters.Where(filter => !rows.Any(row =>
                row.Filter == filter && row.Language == AllLanguages
                && row.TimeScope == AllVersions && row.Supported))
            .Order(StringComparer.Ordinal);
        var stampedUnsupported = stamp["capability_manifest_unsupported_filters"]
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            RequireKnownFilters(stampedUnsupported);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException(
                $"Index {dbPath} has an invalid capability unsupported set.", error);
        }
        if (!unsupported.SequenceEqual(stampedUnsupported, StringComparer.Ordinal))
            throw new InvalidDataException(
                $"Index {dbPath} capability unsupported set does not match its manifest.");
        return (rows, false);
    }

    internal static void ValidateEntry(CapabilityManifestEntry entry)
    {
        RequireKnownFilters([entry.Filter]);
        if (entry.Language != AllLanguages && !IsSourceLanguage(entry.Language))
            throw new InvalidDataException("Capability manifest language is invalid.");
        if (entry.EligibleRows < 1 || entry.PopulatedRows < 0
            || entry.PopulatedRows > entry.EligibleRows || entry.EligibleRows > 10_000_000)
            throw new InvalidDataException("Capability manifest population is invalid.");
        if (entry.TimeScope == AllVersions)
        {
            if (entry.PeriodStart is not null || entry.PeriodEnd is not null)
                throw new InvalidDataException(
                    "An all_versions capability row cannot carry a period.");
            return;
        }
        if (entry.TimeScope != AsOf || entry.PeriodStart is null)
            throw new InvalidDataException("Capability manifest time scope is invalid.");
        var start = ParseDate(entry.PeriodStart, "manifest", "period_start");
        if (entry.PeriodEnd is { } end
            && ParseDate(end, "manifest", "period_end") < start)
            throw new InvalidDataException("Capability manifest period is inverted.");
    }

    internal static void ValidateStructure(
        IReadOnlyList<CapabilityManifestEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return;
        foreach (var row in rows) ValidateEntry(row);
        if (rows.Distinct().Count() != rows.Count)
            throw new InvalidDataException("Capability manifest has duplicate rows.");

        var canonical = rows.OrderBy(row => row.Filter, StringComparer.Ordinal)
            .ThenBy(row => row.Language, StringComparer.Ordinal)
            .ThenBy(row => row.TimeScope == AllVersions ? 0 : 1)
            .ThenBy(row => row.PeriodStart, StringComparer.Ordinal)
            .ThenBy(row => row.PeriodEnd, StringComparer.Ordinal)
            .ToArray();
        if (!rows.SequenceEqual(canonical))
            throw new InvalidDataException("Capability manifest rows are not in canonical order.");

        var actualFilters = rows.Select(row => row.Filter)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!actualFilters.SequenceEqual(GovernedFilters, StringComparer.Ordinal))
            throw new InvalidDataException(
                "Capability manifest does not contain every governed filter.");

        string[]? expectedLanguages = null;
        foreach (var filter in GovernedFilters)
        {
            var languages = rows.Where(row => row.Filter == filter)
                .Select(row => row.Language).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            if (!languages.Contains(AllLanguages, StringComparer.Ordinal))
                throw new InvalidDataException(
                    $"Capability manifest filter {filter} has no aggregate language row.");
            if (expectedLanguages is null)
                expectedLanguages = languages;
            else if (!languages.SequenceEqual(expectedLanguages, StringComparer.Ordinal))
                throw new InvalidDataException(
                    "Capability manifest filters do not share one language set.");

            foreach (var language in languages)
            {
                var group = rows.Where(row => row.Filter == filter
                    && row.Language == language).ToArray();
                if (group.Count(row => row.TimeScope == AllVersions) != 1)
                    throw new InvalidDataException(
                        $"Capability manifest {filter}/{language} must have one all_versions row.");
                var periods = group.Where(row => row.TimeScope == AsOf).ToArray();
                if (periods.Length == 0)
                    throw new InvalidDataException(
                        $"Capability manifest {filter}/{language} has no as_of rows.");
                CapabilityManifestEntry? previous = null;
                foreach (var period in periods)
                {
                    var start = ParseDate(period.PeriodStart!, "manifest", "period_start");
                    if (previous is not null)
                    {
                        if (previous.PeriodEnd is null)
                            throw new InvalidDataException(
                                $"Capability manifest {filter}/{language} has an open period before its final row.");
                        var previousEnd = ParseDate(
                            previous.PeriodEnd, "manifest", "period_end");
                        if (start <= previousEnd)
                            throw new InvalidDataException(
                                $"Capability manifest {filter}/{language} periods overlap.");
                        if (previousEnd < DateOnly.MaxValue
                            && previousEnd.AddDays(1) == start
                            && previous.EligibleRows == period.EligibleRows
                            && previous.PopulatedRows == period.PopulatedRows)
                            throw new InvalidDataException(
                                $"Capability manifest {filter}/{language} has adjacent unmerged periods.");
                    }
                    previous = period;
                }
            }
        }

        foreach (var language in expectedLanguages!)
        {
            var totals = rows.Where(row => row.Language == language
                    && row.TimeScope == AllVersions)
                .Select(row => row.EligibleRows).Distinct().ToArray();
            if (totals.Length != 1)
                throw new InvalidDataException(
                    $"Capability manifest eligible totals disagree for language {language}.");
        }
    }

    internal static void RequireKnownFilters(IEnumerable<string> filters)
    {
        var unknown = filters.Where(filter => !GovernedFilters.Contains(filter, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException(
                $"Unknown governed capability filter(s): {string.Join(", ", unknown)}.");
    }

    internal static void RequireDigest(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Expected a lowercase or uppercase SHA-256 digest.", parameterName);
    }

    private static bool Supported(
        IReadOnlyList<CapabilityManifestEntry> rows,
        string filter,
        string language,
        CapabilityTimeScope timeScope,
        DateOnly? asOf)
    {
        if (timeScope == CapabilityTimeScope.AllVersions)
            return rows.SingleOrDefault(row => row.Filter == filter
                && row.Language == language && row.TimeScope == AllVersions)?.Supported == true;
        if (asOf is null)
            throw new ArgumentException("An as_of capability check requires a date.", nameof(asOf));
        return rows.SingleOrDefault(row => row.Filter == filter
            && row.Language == language && row.TimeScope == AsOf
            && ParseDate(row.PeriodStart!, "manifest", "period_start") <= asOf
            && (row.PeriodEnd is null
                || ParseDate(row.PeriodEnd, "manifest", "period_end") >= asOf))?.Supported == true;
    }

    private static bool SupportedInPeriod(
        IReadOnlyList<CapabilityManifestEntry> rows,
        string filter,
        string language,
        DateOnly from,
        DateOnly to)
    {
        var slices = rows.Where(row => row.Filter == filter
            && row.Language == language && row.TimeScope == AsOf
            && ParseDate(row.PeriodStart!, "manifest", "period_start") <= to
            && (row.PeriodEnd is null
                || ParseDate(row.PeriodEnd, "manifest", "period_end") >= from))
            .ToArray();
        return slices.Length > 0 && slices.All(row => row.Supported);
    }

    private static IReadOnlyList<string> RequestedFilters(FilterSet filters)
    {
        var result = new List<string>(4);
        if (filters.ActForm is not null) result.Add("act_form");
        if (filters.BindingStatus is not null) result.Add("binding_status");
        if (filters.Domain is not null) result.Add("domain");
        if (filters.Hierarchy is not null) result.Add("hierarchy");
        return result;
    }

    private static bool Populated(CapabilityDocument document, string filter) => filter switch
    {
        "act_form" => !string.IsNullOrWhiteSpace(document.ActForm),
        "binding_status" => !string.IsNullOrWhiteSpace(document.BindingStatus),
        "domain" => !string.IsNullOrWhiteSpace(document.Domains),
        "hierarchy" => !string.IsNullOrWhiteSpace(document.Hierarchy),
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private static DateOnly ParseDate(string value, string identity, string field)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            throw new InvalidDataException(
                $"Capability source {identity} has invalid {field} '{value}'.");
        return parsed;
    }

    private static DateOnly? ParseOptionalDate(string? value, string identity, string field) =>
        value is null ? null : ParseDate(value, identity, field);

    private static void ValidateSourceLanguage(string language, string identity)
    {
        if (!IsSourceLanguage(language) || language == AllLanguages)
            throw new InvalidDataException(
                $"Capability source {identity} has invalid language '{language}'.");
    }

    private static bool IsSourceLanguage(string language) =>
        language.Length is >= 1 and <= 16
        && char.IsAsciiLetterOrDigit(language[0])
        && char.IsAsciiLetterOrDigit(language[^1])
        && language.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static string Iso(DateOnly value) => value.ToString(
        "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static void Append(StringBuilder output, string? value) =>
        output.Append(value is null ? -1 : Encoding.UTF8.GetByteCount(value))
            .Append(':').Append(value);

    private sealed record CapabilityDocument(
        string Key,
        string Language,
        string ValidFrom,
        string? ValidTo,
        bool Withdrawn,
        string? Hierarchy,
        string? Domains,
        string? ActForm,
        string? BindingStatus);

    private sealed record DatedDocument(
        CapabilityDocument Document,
        DateOnly From,
        DateOnly? To);

    private readonly record struct PopulationDelta(long Eligible, long Populated);
}
