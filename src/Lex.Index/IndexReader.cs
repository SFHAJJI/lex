using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.Index;

/// Per document type: how many dated versions are held, and how many of them carry text. The
/// second number is the honest one. A source may serve a version only in a format that has no
/// article structure; such a version is held as a complete dated record with no wording (D49).
/// Counts are per version (distinct key), not per docs row: docs holds one row per language
/// expression, and a bilingual version must not count twice under a label that says "versions".
public sealed record CoverageKind(string? Kind, int Versions, int WithText);

/// How many versions each extraction profile produced text for. Publishes the confidence mix
/// of the corpus as a number rather than as a claim.
public sealed record CoverageProfile(string Profile, int Versions);

/// How many works and versions exist in each language, and how many works hold more than one.
/// The tool that exists to say what is NOT held could not answer "which languages", which is the
/// question that decides whether a language control belongs to the site or to the document.
public sealed record CoverageLanguage(string Language, int Works, int Versions);

public sealed record CoverageBuildIssue(string Code, string Work, string? Detail);

public sealed record CoverageInfo(
    string Collection,
    int Groups,
    int Rows,
    string? EarliestValidFrom,
    string? LatestValidFrom,
    IReadOnlyList<CoverageKind> Kinds,
    IReadOnlyDictionary<string, string> Stamp,
    int TextServed,
    IReadOnlyList<CoverageProfile> Profiles,
    IReadOnlyList<CoverageLanguage> Languages,
    int MultilingualWorks,
    int? ExpectedWorks = null,
    IReadOnlyList<CoverageBuildIssue>? BuildIssues = null,
    int TotalKinds = 0,
    int TotalProfiles = 0,
    int TotalLanguages = 0,
    // Rows is one per language expression (the docs table's grain); Versions is the true count
    // of dated versions (distinct version-level key, which carries no language component). The
    // site's public "dated versions" claims must use Versions: labelling the expression count
    // as versions overstated the EU corpus by exactly its language fan-out.
    int Versions = 0,
    int VersionsWithText = 0);

/// Values that the mounted index can actually accept as public search filters. Keeping this
/// inventory beside the data means adding a source-backed hierarchy or jurisdiction does not
/// require a second hard-coded list in the web client. Domains is a legacy column and current v4
/// corpus builds leave it empty; official classifications use typed publisher metadata instead.
public sealed record SearchFacetInfo(
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Hierarchies,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> ActForms,
    IReadOnlyList<string> BindingStatuses);

/// <summary>
/// Read side of one index file. Every query method takes a non-optional FilterSet (F5);
/// filters are applied as SQL predicates before any ranking or ordering.
/// </summary>
public sealed class LexIndexReader : IDisposable
{
    private const int MaximumPublicTitleLength = 8_192;
    private const int MaximumPublicAnchorLength = 512;
    private const int MaximumStampRows = 128;
    private const int MaximumStampKeyLength = 128;
    private const int MaximumStampValueLength = 4_096;
    private const int MaximumBuildIssuesJsonLength = 2_750_000;
    private readonly SqliteConnection _conn;
    private readonly string _schema;
    private readonly ITextEncoder? _encoder;
    private readonly SemanticVectorReader? _vectors;
    private readonly bool _hasWorkSearch;
    private readonly int _workCatalogVersion;
    private readonly long _provisionVectorCount;
    private readonly IReadOnlyList<CoverageBuildIssue> _buildIssues;
    private readonly int? _expectedWorks;
    private readonly bool _ownsVectors;
    public IReadOnlyDictionary<string, string> Stamp { get; }
    public string Collection => Stamp.GetValueOrDefault("collection", "?");
    public bool SignatureValid { get; }
    public bool HybridReady => _encoder is not null && _vectors is not null;

    public void Interrupt() => SQLitePCL.raw.sqlite3_interrupt(_conn.Handle);

    private LexIndexReader(SqliteConnection conn, Dictionary<string, string> stamp, string schema,
                           ITextEncoder? encoder, SemanticVectorReader? vectors, bool hasWorkSearch,
                           int workCatalogVersion, long provisionVectorCount,
                           IReadOnlyList<CoverageBuildIssue> buildIssues, int? expectedWorks,
                           bool ownsVectors)
    {
        _conn = conn;
        Stamp = stamp;
        _schema = schema;
        _encoder = encoder;
        _vectors = vectors;
        _hasWorkSearch = hasWorkSearch;
        _workCatalogVersion = workCatalogVersion;
        _provisionVectorCount = provisionVectorCount;
        _buildIssues = buildIssues;
        _expectedWorks = expectedWorks;
        _ownsVectors = ownsVectors;
        SignatureValid = stamp.ContainsKey("signature") && StampSigner.Verify(stamp);
    }

    private bool IsV3 => _schema == IndexBuilder.SchemaVersion;

    public static LexIndexReader Open(string dbPath, ITextEncoder? encoder = null, string? vectorPath = null)
    {
        var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        ValidateStampBounds(conn, dbPath);
        var stamp = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT k, v FROM stamp";
            using var r = cmd.ExecuteReader();
            while (r.Read()) stamp[r.GetString(0)] = r.GetString(1);
        }
        var buildIssuesJson = stamp.GetValueOrDefault("build_issues_json");
        var buildIssuesDigest = stamp.GetValueOrDefault("build_issues_digest");
        var expectedWorksValue = stamp.GetValueOrDefault("scope_expected_works");
        if (stamp.GetValueOrDefault("known_exclusions") is { Length: > 2000 })
            throw new InvalidDataException(
                $"Index {dbPath} has known-exclusions metadata longer than 2000 characters.");
        if (expectedWorksValue is not null
            && (buildIssuesJson is null || buildIssuesDigest is null))
            throw new InvalidDataException(
                $"Index {dbPath} has an expected-work count without a build-issue inventory.");
        if ((buildIssuesJson is null) != (buildIssuesDigest is null)
            || buildIssuesJson is not null && !string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(buildIssuesJson))),
                buildIssuesDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Index {dbPath} has an invalid build-issue inventory digest.");
        var buildIssues = ValidateBuildIssues(buildIssuesJson, dbPath);
        int? expectedWorks = null;
        if (expectedWorksValue is not null)
        {
            if (!int.TryParse(expectedWorksValue,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedExpected)
                || parsedExpected < 0 || parsedExpected > 1_000_000)
                throw new InvalidDataException(
                    $"Index {dbPath} has an invalid expected-work count.");
            expectedWorks = parsedExpected;
        }
        // §8.4 — refuse unknown schemas explicitly; never guess, never migrate silently.
        var schema = stamp.GetValueOrDefault("schema");
        if (schema is not (IndexBuilder.SchemaVersion or IndexBuilder.PreviousSchemaVersion))
            throw new InvalidOperationException(
                $"Index schema '{schema}' is not a supported schema. Expected '{IndexBuilder.PreviousSchemaVersion}' or '{IndexBuilder.SchemaVersion}'. Refusing to open {dbPath}.");

        // The stamp is a claim, not a check. `profile` was added to docs without the schema string
        // changing, so an index built the day before opened cleanly and then threw a raw
        // "no such column" from inside a request, on whichever page happened to select it first.
        // Reading the columns the reader actually needs turns that into one clear refusal here.
        var present = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM pragma_table_info('docs')";
            using var r = cmd.ExecuteReader();
            while (r.Read()) present.Add(r.GetString(0));
        }
        var missing = DocCols.Split([',', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries)
                             .Where(c => !present.Contains(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Index {dbPath} claims schema '{IndexBuilder.SchemaVersion}' but docs is missing " +
                $"[{string.Join(", ", missing)}]. It predates a column the reader needs; rebuild it.");

        if (schema == IndexBuilder.SchemaVersion)
        {
            var missingV3Docs = new[]
                { "hierarchy", "domains", "act_form", "binding_status", "consolidation_status" }
                .Where(c => !present.Contains(c)).ToList();
            if (missingV3Docs.Count > 0)
                throw new InvalidOperationException(
                    $"Index {dbPath} claims schema '{schema}' but docs is missing [{string.Join(", ", missingV3Docs)}].");
            foreach (var table in new[] { "text_blobs", "lexical_states" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
                cmd.Parameters.AddWithValue("$name", table);
                if (cmd.ExecuteScalar() is null)
                    throw new InvalidOperationException($"Index {dbPath} claims schema '{schema}' but is missing {table}.");
            }
            using var provisionColumns = conn.CreateCommand();
            provisionColumns.CommandText = "SELECT name FROM pragma_table_info('provisions')";
            using var provisionReader = provisionColumns.ExecuteReader();
            var provisionNames = new HashSet<string>(StringComparer.Ordinal);
            while (provisionReader.Read()) provisionNames.Add(provisionReader.GetString(0));
            if (!provisionNames.Contains("state_id"))
                throw new InvalidOperationException($"Index {dbPath} claims schema '{schema}' but provisions is missing state_id.");
            foreach (var table in new[] { "provision_states", "anchor_events" })
            {
                using var requiredColumns = conn.CreateCommand();
                requiredColumns.CommandText =
                    $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name IN ('language','is_primary_language')";
                if (Convert.ToInt32(requiredColumns.ExecuteScalar()) != 2)
                    throw new InvalidOperationException(
                        $"Index {dbPath} claims schema '{schema}' but {table} is missing language identity columns.");
            }
        }

        var baseWorkCatalogTables = new[]
            { "work_records", "work_names", "work_fts", "work_discovery", "work_vectors" };
        var extendedWorkCatalogTables = new[] { "work_publisher_metadata", "document_roles" };
        int PresentCount(IEnumerable<string> tables) => tables.Count(table =>
        {
            using var tableCommand = conn.CreateCommand();
            tableCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE name=$name";
            tableCommand.Parameters.AddWithValue("$name", table);
            return tableCommand.ExecuteScalar() is not null;
        });
        var presentBaseWorkTables = PresentCount(baseWorkCatalogTables);
        var presentExtendedWorkTables = PresentCount(extendedWorkCatalogTables);
        if (presentBaseWorkTables is > 0 && presentBaseWorkTables != baseWorkCatalogTables.Length
            || presentExtendedWorkTables is > 0 && presentExtendedWorkTables != extendedWorkCatalogTables.Length)
            throw new InvalidDataException(
                $"Index {dbPath} contains a partial work catalog. Rebuild it instead of silently disabling work search.");
        var hasExtendedWorkCatalog =
            presentExtendedWorkTables == extendedWorkCatalogTables.Length;
        if (hasExtendedWorkCatalog)
        {
            using var citationIdentityColumn = conn.CreateCommand();
            citationIdentityColumn.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info('work_publisher_metadata') "
                + "WHERE name='citation_identity'";
            if (Convert.ToInt32(citationIdentityColumn.ExecuteScalar()) != 1)
                throw new InvalidDataException(
                    $"Index {dbPath} uses work catalog version 2; rebuild it for source-backed citation identity.");
        }
        var workCatalogVersion = presentBaseWorkTables == 0 ? 0
            : hasExtendedWorkCatalog ? 3 : 1;
        var hasWorkSearch = workCatalogVersion > 0;
        var hasWorkSearchStamp = stamp.ContainsKey("work_search_records")
                                 || stamp.ContainsKey("work_vector_records")
                                 || stamp.ContainsKey("vector_layout")
                                 || stamp.ContainsKey("work_catalog_version")
                                 || stamp.ContainsKey("publisher_metadata_records")
                                 || stamp.ContainsKey("document_role_records")
                                 || stamp.ContainsKey("weak_discovery_records");
        if (hasWorkSearch != hasWorkSearchStamp)
            throw new InvalidDataException(
                $"Index {dbPath} has inconsistent work catalog tables and stamp claims.");
        var stampedCatalogVersion = stamp.GetValueOrDefault("work_catalog_version");
        if ((workCatalogVersion == 1 && stampedCatalogVersion is not null)
            || (workCatalogVersion == 3 && stampedCatalogVersion != "3"))
            throw new InvalidDataException(
                $"Index {dbPath} has inconsistent work catalog version metadata.");
        if (hasWorkSearch)
        {
            if (workCatalogVersion >= 2)
            {
                using var publisherColumn = conn.CreateCommand();
                publisherColumn.CommandText =
                    "SELECT COUNT(*) FROM pragma_table_info('work_fts') WHERE name='publisher'";
                if (Convert.ToInt32(publisherColumn.ExecuteScalar()) != 1)
                    throw new InvalidDataException(
                        $"Index {dbPath} work catalog is missing publisher metadata search.");
            }
            if (!long.TryParse(stamp.GetValueOrDefault("work_search_records"), out var stampedWorks)
                || !long.TryParse(stamp.GetValueOrDefault("work_vector_records"), out var stampedWorkVectors)
                || stampedWorks < 0 || stampedWorkVectors < 0)
                throw new InvalidDataException($"Index {dbPath} has invalid work catalog counts in its stamp.");
            var stampedPublisherMetadata = 0L;
            var stampedDocumentRoles = 0L;
            long? stampedWeakDiscovery = null;
            if (workCatalogVersion >= 2
                && (!long.TryParse(stamp.GetValueOrDefault("publisher_metadata_records"), out stampedPublisherMetadata)
                    || !long.TryParse(stamp.GetValueOrDefault("document_role_records"), out stampedDocumentRoles)
                    || stampedPublisherMetadata < 0 || stampedDocumentRoles < 0))
                throw new InvalidDataException($"Index {dbPath} has invalid extended work catalog counts in its stamp.");
            if (stamp.TryGetValue("weak_discovery_records", out var weakDiscoveryValue))
            {
                if (!long.TryParse(weakDiscoveryValue, out var parsedWeakDiscovery)
                    || parsedWeakDiscovery < 0)
                    throw new InvalidDataException(
                        $"Index {dbPath} has an invalid weak discovery count in its stamp.");
                stampedWeakDiscovery = parsedWeakDiscovery;
            }
            using var workCounts = conn.CreateCommand();
            workCounts.CommandText = workCatalogVersion >= 2
                ? """
                    SELECT (SELECT COUNT(*) FROM work_records),
                           (SELECT COUNT(*) FROM work_vectors),
                           (SELECT COUNT(*) FROM work_publisher_metadata),
                           (SELECT COUNT(*) FROM document_roles),
                           (SELECT COUNT(*) FROM work_discovery)
                    """
                : """
                    SELECT (SELECT COUNT(*) FROM work_records),
                           (SELECT COUNT(*) FROM work_vectors),
                           (SELECT COUNT(*) FROM work_discovery)
                    """;
            using var workCountRow = workCounts.ExecuteReader();
            var actualWeakDiscovery = workCountRow.Read()
                ? workCountRow.GetInt64(workCatalogVersion >= 2 ? 4 : 2)
                : -1;
            if (actualWeakDiscovery < 0
                || workCountRow.GetInt64(0) != stampedWorks
                || workCountRow.GetInt64(1) != stampedWorkVectors
                || workCatalogVersion >= 2 && (workCountRow.GetInt64(2) != stampedPublisherMetadata
                                               || workCountRow.GetInt64(3) != stampedDocumentRoles)
                || stampedWeakDiscovery is not null
                   && actualWeakDiscovery != stampedWeakDiscovery)
                throw new InvalidDataException($"Index {dbPath} work catalog counts do not match its stamp.");
            var vectorLayout = stamp.GetValueOrDefault("vector_layout");
            if ((stampedWorkVectors > 0 && vectorLayout != "lex-vectors/1-mixed-provision-work")
                || (stampedWorkVectors == 0 && vectorLayout is not null))
                throw new InvalidDataException($"Index {dbPath} has an invalid work vector layout claim.");
        }

        ValidateBoundedPublicMetadata(conn, dbPath, schema, hasWorkSearch);

        SemanticVectorReader? vectors = null;
        long provisionVectorCount = 0;
        if (encoder is not null || vectorPath is not null)
        {
            if (encoder is null || vectorPath is null)
                throw new InvalidOperationException("Both an embedding encoder and vector file are required for hybrid retrieval.");
            if (stamp.GetValueOrDefault("embedding_model") != encoder.ModelId
                || stamp.GetValueOrDefault("embedding_revision") != encoder.ModelRevision)
                throw new InvalidDataException("The index embedding identity does not match the runtime encoder.");
            vectors = new SemanticVectorReader(vectorPath);
            if (vectors.Dimensions != encoder.Dimensions)
                throw new InvalidDataException("The semantic vector dimension does not match the runtime encoder.");
            using var mapping = conn.CreateCommand();
            mapping.CommandText = """
                SELECT COUNT(DISTINCT vector_ordinal),
                       COALESCE(MIN(vector_ordinal),-1),COALESCE(MAX(vector_ordinal),-1)
                FROM semantic_chunks
                """;
            using var mappingReader = mapping.ExecuteReader();
            if (!mappingReader.Read())
                throw new InvalidDataException("The semantic vector mapping cannot be read.");
            provisionVectorCount = mappingReader.GetInt64(0);
            var provisionMin = mappingReader.GetInt64(1);
            var provisionMax = mappingReader.GetInt64(2);
            if (provisionVectorCount > 0
                && (provisionMin != 0 || provisionMax != provisionVectorCount - 1))
                throw new InvalidDataException("Provision vector ordinals are not contiguous from zero.");

            long workVectorCount = 0;
            using var workTable = conn.CreateCommand();
            workTable.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='work_vectors'";
            if (workTable.ExecuteScalar() is not null)
            {
                using var invalidWorkMappings = conn.CreateCommand();
                invalidWorkMappings.CommandText = """
                    SELECT COUNT(*)
                    FROM work_vectors v
                    LEFT JOIN work_records r ON r.work_id=v.work_id
                    WHERE r.work_id IS NULL
                       OR (v.evidence_kind='work' AND v.evidence_value IS NOT NULL)
                       OR (v.evidence_kind<>'work' AND (
                           v.evidence_kind NOT IN ('description','concept','search_synonym','practice_area')
                           OR v.evidence_value IS NULL OR trim(v.evidence_value)=''))
                    """;
                if (Convert.ToInt64(invalidWorkMappings.ExecuteScalar()) != 0)
                    throw new InvalidDataException("The work vector mapping has invalid evidence or work identity.");
                using var workMapping = conn.CreateCommand();
                workMapping.CommandText = """
                    SELECT COUNT(DISTINCT vector_ordinal),
                           COALESCE(MIN(vector_ordinal),-1),COALESCE(MAX(vector_ordinal),-1)
                    FROM work_vectors
                    """;
                using var workReader = workMapping.ExecuteReader();
                if (!workReader.Read())
                    throw new InvalidDataException("The work vector mapping cannot be read.");
                workVectorCount = workReader.GetInt64(0);
                var workMin = workReader.GetInt64(1);
                var workMax = workReader.GetInt64(2);
                if (workVectorCount > 0 && (workMin != provisionVectorCount
                    || workMax != provisionVectorCount + workVectorCount - 1))
                    throw new InvalidDataException("Work vector ordinals are not contiguous after provisions.");
                if (workVectorCount > 0)
                {
                    using var baseCoverage = conn.CreateCommand();
                    baseCoverage.CommandText = """
                        SELECT COUNT(*) FROM (
                          SELECT r.work_id
                          FROM work_records r
                          LEFT JOIN work_vectors v ON v.work_id=r.work_id
                          GROUP BY r.work_id
                          HAVING SUM(CASE WHEN v.evidence_kind='work' THEN 1 ELSE 0 END) <> 1)
                        """;
                    if (Convert.ToInt64(baseCoverage.ExecuteScalar()) != 0)
                        throw new InvalidDataException("Every work must have exactly one base vector.");
                }
            }
            if (provisionVectorCount + workVectorCount != vectors.Count)
                throw new InvalidDataException("The semantic vector file contains an unmapped record.");
        }
        return new LexIndexReader(
            conn, stamp, schema!, encoder, vectors, hasWorkSearch, workCatalogVersion,
            provisionVectorCount, buildIssues, expectedWorks, ownsVectors: true);
    }

    /// <summary>
    /// Creates a lightweight read-only session over the already verified immutable artifact.
    /// Cancellable MCP work owns this SQLite connection, so interrupting it cannot affect the
    /// singleton reader used by catalogue and document requests. Encoder and vector bytes remain
    /// shared and are serialized by the caller's per-publisher execution gate.
    /// </summary>
    internal LexIndexReader CreateIsolatedSession()
    {
        var connection = new SqliteConnection(_conn.ConnectionString);
        connection.Open();
        return new LexIndexReader(
            connection,
            new Dictionary<string, string>(Stamp, StringComparer.Ordinal),
            _schema,
            _encoder,
            _vectors,
            _hasWorkSearch,
            _workCatalogVersion,
            _provisionVectorCount,
            _buildIssues,
            _expectedWorks,
            ownsVectors: false);
    }

    private static void ValidateStampBounds(SqliteConnection connection, string dbPath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CASE
              WHEN COUNT(*) > {MaximumStampRows} THEN 1
              WHEN COUNT(*) <> COUNT(DISTINCT k) THEN 1
              WHEN EXISTS (
                SELECT 1 FROM stamp
                WHERE typeof(k) <> 'text' OR typeof(v) <> 'text'
                   OR length(k) = 0 OR length(k) > {MaximumStampKeyLength}
                   OR length(v) > CASE k
                     WHEN 'build_issues_json' THEN {MaximumBuildIssuesJsonLength}
                     WHEN 'known_exclusions' THEN 2000
                     ELSE {MaximumStampValueLength}
                   END
              ) THEN 1
              ELSE 0
            END
            FROM stamp
            """;
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            throw new InvalidDataException(
                $"Index {dbPath} has duplicate, malformed, or oversized stamp metadata.");
    }

    private static IReadOnlyList<CoverageBuildIssue> ValidateBuildIssues(
        string? json, string dbPath)
    {
        if (json is null) return [];
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 4,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() > 1000)
                throw new InvalidDataException("Build issues must be an array with at most 1000 entries.");
            var issues = new List<CoverageBuildIssue>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("code", out var codeElement)
                    || codeElement.ValueKind != JsonValueKind.String
                    || !element.TryGetProperty("work", out var workElement)
                    || workElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Each build issue requires string code and work fields.");
                var code = codeElement.GetString()!;
                var work = workElement.GetString()!;
                string? detail = null;
                if (element.TryGetProperty("detail", out var detailElement))
                {
                    if (detailElement.ValueKind == JsonValueKind.String)
                        detail = detailElement.GetString();
                    else if (detailElement.ValueKind != JsonValueKind.Null)
                        throw new InvalidDataException("Build issue detail must be a string or null.");
                }
                if (string.IsNullOrWhiteSpace(code) || code.Length > 128
                    || string.IsNullOrWhiteSpace(work) || work.Length > 512
                    || detail?.Length > 2000)
                    throw new InvalidDataException("Build issue fields are empty or exceed their bounds.");
                issues.Add(new CoverageBuildIssue(code, work, detail));
            }
            return issues;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException(
                $"Index {dbPath} has a malformed build-issue inventory.", exception);
        }
    }

    private const string DocCols = """
        key, collection, group_key, group_identifier, kind, language, valid_from, valid_to,
        valid_time_source, observed_from, withdrawn, text_available, text_public,
        record_sha, body_sha, source_uri, title, title_short, publication_date, status_note, rid,
        profile
        """;

    private string SelectDocCols(string? alias = null)
    {
        var p = string.IsNullOrEmpty(alias) ? "" : alias + ".";
        var core = string.Join(", ", DocCols.Split(',').Select(c => p + c.Trim()));
        return IsV3
            ? core + $", {p}hierarchy, {p}domains, {p}act_form, {p}binding_status, {p}consolidation_status"
            : core + ", NULL, NULL, NULL, NULL, NULL";
    }

    /// <summary>True if the work exists at all (distinguishes unknown_work from no_version_for_date).</summary>
    public bool WorkExists(string work)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM docs WHERE group_key=$w OR group_identifier=$w OR key LIKE $p LIMIT 1";
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        cmd.Parameters.AddWithValue("$p", NormalizeWork(work) + ":%");
        return cmd.ExecuteScalar() is not null;
    }

    public SearchFacetInfo SearchFacets()
    {
        IReadOnlyList<string> Distinct(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0)))
                    values.Add(reader.GetString(0));
            }
            return values;
        }

        var languages = Distinct(
            "SELECT DISTINCT lower(language) FROM docs WHERE language <> '' ORDER BY lower(language)");
        if (!IsV3)
            return new SearchFacetInfo(languages, [], [], [], []);

        var domains = Distinct(
                "SELECT DISTINCT domains FROM docs WHERE domains IS NOT NULL AND domains <> '' ORDER BY domains")
            .SelectMany(value => value.Trim('|').Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        return new SearchFacetInfo(
            languages,
            Distinct("SELECT DISTINCT hierarchy FROM docs WHERE hierarchy IS NOT NULL AND hierarchy <> '' ORDER BY hierarchy"),
            domains,
            Distinct("SELECT DISTINCT act_form FROM docs WHERE act_form IS NOT NULL AND act_form <> '' ORDER BY act_form"),
            Distinct("SELECT DISTINCT binding_status FROM docs WHERE binding_status IS NOT NULL AND binding_status <> '' ORDER BY binding_status"));
    }

    public DocRow? AsOf(string work, DateOnly date, FilterSet filters)
    {
        var (sql, ps) = WithFilters($"""
            SELECT {SelectDocCols()} FROM docs
            WHERE (group_key=$w OR group_identifier=$w)
              AND valid_from <= $d AND (valid_to IS NULL OR valid_to >= $d)
            """, filters, excludeAsOf: true);
        // When the caller names no language and the version exists in several, prefer the one this
        // work is mostly published in, then fall back to alphabetical for a stable tie-break.
        // Ordering by language alone made "de" win over "fr" on every tie, which is how the
        // Constitution, one of three suggested starting points on the front page, greeted readers
        // in German: it holds 37 French versions, one German and one Luxembourgish, and the German
        // one shares a date with the other two. The rule stays publisher-agnostic (F1): it asks
        // the work what it is written in rather than hard-coding a national language.
        sql += """
             ORDER BY valid_from DESC,
                      (SELECT COUNT(*) FROM docs c
                        WHERE c.group_key = docs.group_key AND c.language = docs.language) DESC,
                      language
             LIMIT 1
            """;
        using var cmd = Cmd(sql, ps);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadDoc(r) : null;
    }

    public List<DocRow> Timeline(string work)
    {
        using var cmd = Cmd($"""
            SELECT {SelectDocCols()} FROM docs WHERE group_key=$w OR group_identifier=$w
            ORDER BY valid_from, language
            """, []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        return ReadAll(cmd);
    }

    private static void ValidateBoundedPublicMetadata(
        SqliteConnection connection,
        string dbPath,
        string schema,
        bool hasWorkSearch)
    {
        void Reject(string table, string predicate, string label)
        {
            using var existence = connection.CreateCommand();
            existence.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$table";
            existence.Parameters.AddWithValue("$table", table);
            if (existence.ExecuteScalar() is null) return;
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT 1 FROM {table} WHERE {predicate} LIMIT 1";
            if (command.ExecuteScalar() is not null)
                throw new InvalidDataException(
                    $"Index {dbPath} contains {label} longer than the public metadata contract permits.");
        }

        Reject("docs", "length(key)>512 OR length(collection)>128 OR length(group_key)>512 "
            + "OR length(group_identifier)>1000 OR length(kind)>128 OR length(language)>16 "
            + "OR length(valid_time_source)>128 OR length(source_uri)>4096 "
            + $"OR length(title)>{MaximumPublicTitleLength} "
            + $"OR length(title_short)>{MaximumPublicTitleLength} OR length(status_note)>4096 "
            + "OR length(profile)>128", "document metadata");
        if (schema == IndexBuilder.SchemaVersion)
            Reject("docs", "length(hierarchy)>512 OR length(domains)>4096 "
                + "OR length(act_form)>512 OR length(binding_status)>512 "
                + "OR length(consolidation_status)>512", "document legal metadata");
        Reject("provisions", $"length(anchor)>{MaximumPublicAnchorLength} OR length(provision_id)>1000 "
            + "OR length(ptype)>128 OR length(num)>512 OR length(heading)>4096 "
            + $"OR length(path)>4096 OR length(work_title)>{MaximumPublicTitleLength}",
            "provision metadata");
        Reject("citations", "length(cited_slug)>512 OR length(href)>4096 OR length(label)>4096",
            "citation metadata");
        Reject("events", "length(event)>128 OR length(scope)>128 OR length(detail)>4096",
            "provenance metadata");
        if (hasWorkSearch)
        {
            Reject("work_records", "length(group_key)>512 OR length(group_identifier)>1000 "
                + $"OR length(title)>{MaximumPublicTitleLength} "
                + $"OR length(title_short)>{MaximumPublicTitleLength}", "work metadata");
            Reject("work_names", $"length(value)>{MaximumPublicTitleLength} "
                + $"OR length(normalized)>{MaximumPublicTitleLength}",
                "work-name metadata");
            Reject("work_discovery", "length(value)>4096 OR length(normalized)>4096",
                "work-discovery metadata");
            using var extended = connection.CreateCommand();
            extended.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='work_publisher_metadata'";
            if (extended.ExecuteScalar() is not null)
            {
                Reject("work_publisher_metadata", "length(kind)>128 OR length(identifier)>2048 "
                    + "OR length(value)>4096 OR length(normalized)>4096 "
                    + "OR length(source_uri)>2048 OR citation_identity NOT IN (0,1)",
                    "publisher work metadata");
                using var publisherRows = connection.CreateCommand();
                publisherRows.CommandText = """
                    SELECT kind,identifier,value,language,source_uri,citation_identity
                    FROM work_publisher_metadata
                    """;
                using var rows = publisherRows.ExecuteReader();
                while (rows.Read())
                {
                    var kind = rows.GetString(0);
                    var identifier = rows.GetString(1);
                    var sourceUri = rows.GetString(4);
                    var citationIdentity = rows.GetInt64(5) == 1;
                    if (string.IsNullOrWhiteSpace(kind)
                        || !HttpUri(identifier) || !HttpUri(sourceUri)
                        || !citationIdentity && (rows.IsDBNull(2) || rows.IsDBNull(3))
                        || citationIdentity && !rows.IsDBNull(3))
                        throw new InvalidDataException(
                            $"Index {dbPath} contains invalid publisher work metadata.");
                }
            }
        }

        static bool HttpUri(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
    }

    /// <summary>A bounded timeline page plus its exact row count.</summary>
    public (List<TimelineVersionRow> Rows, int Total) Timeline(string work, int limit, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var normalized = NormalizeWork(work);
        using var count = Cmd(
            "SELECT COUNT(DISTINCT key) FROM docs WHERE group_key=$w OR group_identifier=$w", []);
        count.Parameters.AddWithValue("$w", normalized);
        var total = Convert.ToInt32(count.ExecuteScalar());
        using var page = Cmd($"""
            WITH version_page AS (
                SELECT key, MIN(valid_from) AS valid_from
                FROM docs WHERE group_key=$w OR group_identifier=$w
                GROUP BY key ORDER BY valid_from, key LIMIT $lim OFFSET $off)
            SELECT {SelectDocCols("d")} FROM docs d
            JOIN version_page v ON v.key=d.key
            ORDER BY v.valid_from, d.key, d.language
            """, []);
        page.Parameters.AddWithValue("$w", normalized);
        page.Parameters.AddWithValue("$lim", limit);
        page.Parameters.AddWithValue("$off", offset);
        var expressions = ReadAll(page);
        var rows = GroupTimelineVersions(expressions, PreferredDocumentLanguage(normalized));
        return (rows, total);
    }

    private static List<TimelineVersionRow> GroupTimelineVersions(
        IEnumerable<DocRow> expressions, string? preferredLanguage) => expressions
        .GroupBy(row => row.Key, StringComparer.Ordinal)
        .OrderBy(group => group.Min(row => row.ValidFrom), StringComparer.Ordinal)
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Select(group =>
        {
            var ordered = group.OrderBy(row => row.Language, StringComparer.Ordinal).ToArray();
            var representative = preferredLanguage is null
                ? ordered[0]
                : ordered.FirstOrDefault(row => row.Language == preferredLanguage) ?? ordered[0];
            return new TimelineVersionRow(representative, ordered);
        }).ToList();

    private string? PreferredDocumentLanguage(string normalizedWork)
    {
        using var command = Cmd("""
            SELECT language FROM docs
            WHERE group_key=$w OR group_identifier=$w
            GROUP BY language
            ORDER BY COUNT(DISTINCT key) DESC, language
            LIMIT 1
            """, []);
        command.Parameters.AddWithValue("$w", normalizedWork);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Every distinct version address this index can serve, for the sitemap.
    ///
    /// One row per exact publisher version key, which is exactly the set of canonical version
    /// URLs. Grouped rather than selected, because a version published in several languages has
    /// one row per language behind a single URL.
    ///
    /// One query rather than a Timeline call per work, which would require one round trip for
    /// every mounted work merely to build one file.
    /// </summary>
    public List<(string Collection, string GroupKey, string VersionKey, string? LastObserved)> VersionPaths()
    {
        using var cmd = Cmd("""
            SELECT collection, group_key, key, MAX(observed_from)
            FROM docs
            GROUP BY collection, group_key, key
            ORDER BY group_key, valid_from, key
            """, []);
        var rows = new List<(string, string, string, string?)>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var key = rd.GetString(2);
            rows.Add((rd.GetString(0), rd.GetString(1),
                key[(key.LastIndexOf(':') + 1)..],
                rd.IsDBNull(3) ? null : rd.GetString(3)));
        }
        return rows;
    }

    /// <summary>In-force set computed from validity intervals at query time (never a stored flag).
    /// Deduplicated by group; deterministic (collection, group_key) ordering for stable cursors.</summary>
    public InForcePage InForceOn(DateOnly date, FilterSet filters, int limit, int offset)
    {
        // Language selects an expression to return; it must never select one publisher state
        // over a same-boundary sibling. Build the state set without that projection, then retain
        // only works for which at least one latest state has the requested expression.
        var requestedLanguage = filters.Language;
        var stateFilters = filters with { Language = null };
        var (countWhere, countParameters) = WithFilters(
            "d.valid_from <= $d AND (d.valid_to IS NULL OR d.valid_to >= $d) AND d.withdrawn = 0",
            stateFilters, excludeAsOf: true, alias: "d");

        int total;
        using (var cnt = Cmd($"""
                   WITH eligible AS (
                       SELECT d.* FROM docs d WHERE {countWhere}),
                   latest AS (
                       SELECT group_key, MAX(valid_from) AS valid_from
                       FROM eligible GROUP BY group_key),
                   scoped AS (
                       SELECT l.group_key, l.valid_from FROM latest l
                       WHERE $language IS NULL OR EXISTS (
                           SELECT 1 FROM eligible e
                           WHERE e.group_key=l.group_key AND e.valid_from=l.valid_from
                             AND e.language=$language))
                   SELECT COUNT(*) FROM scoped
                   """, countParameters))
        {
            cnt.Parameters.AddWithValue("$d",
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cnt.Parameters.AddWithValue("$language",
                (object?)requestedLanguage ?? DBNull.Value);
            total = Convert.ToInt32(cnt.ExecuteScalar());
        }

        var (where, ps) = WithFilters(
            "d.valid_from <= $d AND (d.valid_to IS NULL OR d.valid_to >= $d) AND d.withdrawn = 0",
            stateFilters, excludeAsOf: true, alias: "d");
        using var cmd = Cmd($"""
            WITH eligible AS (
                SELECT d.* FROM docs d WHERE {where}),
            latest AS (
                SELECT group_key, MAX(valid_from) AS valid_from
                FROM eligible GROUP BY group_key),
            group_page AS (
                SELECT l.group_key, l.valid_from FROM latest l
                WHERE $language IS NULL OR EXISTS (
                    SELECT 1 FROM eligible e
                    WHERE e.group_key=l.group_key AND e.valid_from=l.valid_from
                      AND e.language=$language)
                ORDER BY l.group_key LIMIT $lim OFFSET $off),
            candidates AS (
                SELECT e.*, ROW_NUMBER() OVER (
                    PARTITION BY e.key ORDER BY
                        CASE WHEN $language IS NOT NULL AND e.language=$language THEN 0 ELSE 1 END,
                        e.language) AS expression_row
                FROM eligible e JOIN group_page p
                  ON p.group_key=e.group_key AND p.valid_from=e.valid_from)
            SELECT {SelectDocCols("d")} FROM candidates d
            WHERE d.expression_row=1
            ORDER BY d.collection, d.group_key, d.key
            """, ps);
        cmd.Parameters.AddWithValue("$d",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$language",
            (object?)requestedLanguage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lim", limit);
        cmd.Parameters.AddWithValue("$off", offset);
        var candidates = ReadAll(cmd);
        var rows = new List<DocRow>();
        var ambiguities = new List<InForceAmbiguity>();
        foreach (var group in candidates.GroupBy(row => row.GroupKey, StringComparer.Ordinal))
        {
            var choices = group.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
            if (choices.Length == 1) rows.Add(choices[0]);
            else if (choices.Length > 1)
                ambiguities.Add(new InForceAmbiguity(
                    group.Key, choices[0].ValidFrom, choices));
        }
        return new InForcePage(rows, total, ambiguities);
    }

    public List<(DocRow Doc, ProvisionRow Prov, string Snippet)> Search(string query, FilterSet filters, int limit)
    {
        if (IsV3) return SearchV3(query, filters, limit);
        // Filters first (F5): SQL predicates restrict the candidate set; only survivors are
        // ranked by bm25 (weights: work title > heading > num > body text). Hits are
        // provision-level: the retrieval unit is the article, not the document.
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false);
        using var cmd = Cmd($"""
            SELECT {SelectDocCols("d")},
                   p.rid, p.seq, p.anchor, p.provision_id, p.ptype, p.num, p.heading, p.path,
                   p.article_valid_from, p.work_title, p.text_sha,
                   snippet(fts, 3, '«', '»', ' … ', 14) AS snip
            FROM fts
            JOIN provisions p ON p.rowid = fts.rowid
            JOIN docs d ON d.rid = p.rid
            WHERE fts MATCH $q AND {where}
            ORDER BY bm25(fts, 10.0, 4.0, 6.0, 1.0)
            LIMIT $lim
            """, ps);
        cmd.Parameters.AddWithValue("$q", Fts5Escape(query));
        cmd.Parameters.AddWithValue("$lim", limit);
        var result = new List<(DocRow, ProvisionRow, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // Document columns occupy ordinals 0..26; provision columns follow.
            var prov = new ProvisionRow(
                Rid: r.GetString(27), Seq: r.GetInt32(28), Anchor: r.GetString(29), ProvisionId: r.GetString(30),
                PType: r.GetString(31), Num: r.IsDBNull(32) ? null : r.GetString(32),
                Heading: r.IsDBNull(33) ? null : r.GetString(33), Path: r.IsDBNull(34) ? null : r.GetString(34),
                ArticleValidFrom: r.IsDBNull(35) ? null : r.GetString(35),
                WorkTitle: r.IsDBNull(36) ? null : r.GetString(36),
                TextMd: "", TextSha: r.GetString(37), TextLoaded: false);
            result.Add((ReadDoc(r), prov, r.IsDBNull(38) ? "" : r.GetString(38)));
        }
        return result;
    }

    private List<(DocRow Doc, ProvisionRow Prov, string Snippet)> SearchV3(string query, FilterSet filters, int limit)
    {
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false, alias: "d");
        using var cmd = Cmd($"""
            WITH matched AS (
              SELECT rowid AS state_id,
                     bm25(fts, 10.0, 4.0, 6.0, 1.0) AS score,
                     snippet(fts, 3, '«', '»', ' ... ', 14) AS snip
              FROM fts WHERE fts MATCH $q
            ), eligible AS (
              SELECT m.score, p.rid, p.seq, p.anchor, p.provision_id, p.ptype, p.num,
                     p.heading, p.path, p.article_valid_from, p.work_title, p.text_sha,
                     d.key AS doc_key, m.snip,
                     ROW_NUMBER() OVER (PARTITION BY m.state_id ORDER BY d.valid_from DESC, p.rid) AS occurrence_rank
              FROM matched m
              JOIN provisions p ON p.state_id = m.state_id
              JOIN docs d ON d.rid = p.rid
              WHERE {where}
            )
            SELECT {SelectDocCols("d")},
                   e.rid, e.seq, e.anchor, e.provision_id, e.ptype, e.num, e.heading, e.path,
                   e.article_valid_from, e.work_title, e.text_sha, e.snip
            FROM eligible e
            JOIN docs d ON d.key = e.doc_key AND d.rid = e.rid
            WHERE e.occurrence_rank = 1
            ORDER BY e.score, d.valid_from DESC
            LIMIT $lim
            """, ps);
        cmd.Parameters.AddWithValue("$q", Fts5Escape(query));
        cmd.Parameters.AddWithValue("$lim", limit);
        var result = new List<(DocRow, ProvisionRow, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var prov = new ProvisionRow(
                r.GetString(27), r.GetInt32(28), r.GetString(29), r.GetString(30), r.GetString(31),
                r.IsDBNull(32) ? null : r.GetString(32), r.IsDBNull(33) ? null : r.GetString(33),
                r.IsDBNull(34) ? null : r.GetString(34), r.IsDBNull(35) ? null : r.GetString(35),
                r.IsDBNull(36) ? null : r.GetString(36), "", r.GetString(37), TextLoaded: false);
            result.Add((ReadDoc(r), prov, r.IsDBNull(38) ? "" : r.GetString(38)));
        }
        return result;
    }

    public SearchExecution SearchHybrid(string query, FilterSet filters, int limit, bool fuzzyAuto = true)
    {
        if (_encoder is null || _vectors is null)
            return SearchKeyword(query, filters, limit, fuzzyAuto);

        var keyword = SearchKeyword(query, filters, 100, fuzzyAuto);
        var plan = keyword.QueryPlan!;
        if (HasRoleConflict(filters, plan)) return keyword;
        if (plan.ArticleNumber is not null
            || (plan.HasStrongWorkMatch && plan.ProvisionQuery.Length == 0))
            return keyword;
        var semanticQuery = plan.ProvisionQuery.Length == 0 ? query : plan.ProvisionQuery;
        var effectiveFilters = ConstrainToWorks(filters, plan);
        if (_workCatalogVersion >= 2 && plan.RoleIntent is not null
            && effectiveFilters.DocumentRole is null)
            effectiveFilters = effectiveFilters with { DocumentRole = plan.RoleIntent };
        var lexical = keyword.Hits;
        var queryVector = _encoder.Encode(semanticQuery, EmbeddingInputKind.Query);
        var semantic = SearchSemantic(semanticQuery, queryVector, effectiveFilters, 100);
        var semanticWorks = SearchWorkSemanticCatalog(queryVector, effectiveFilters, 100);
        var fused = new Dictionary<string, RetrievalHit>(StringComparer.Ordinal);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        static string Key(DocRow d, ProvisionRow p) => $"{d.GroupKey}|{d.Language}|{p.Anchor}|{p.TextSha}";
        for (var i = 0; i < lexical.Count; i++)
        {
            var h = lexical[i];
            var key = Key(h.Doc, h.Provision);
            scores[key] = scores.GetValueOrDefault(key) + 1d / (60 + i + 1);
            fused[key] = h with { Score = 0 };
        }
        for (var i = 0; i < semantic.Count; i++)
        {
            var h = semantic[i];
            var key = Key(h.Doc, h.Provision);
            scores[key] = scores.GetValueOrDefault(key) + 1d / (60 + i + 1);
            if (fused.TryGetValue(key, out var prior))
                fused[key] = prior with { MatchReasons = ["keyword", "semantic"] };
            else fused[key] = h;
        }
        for (var i = 0; i < semanticWorks.Count; i++)
        {
            var work = semanticWorks[i];
            var key = fused.Where(candidate =>
                    candidate.Value.Doc.Key == work.Doc.Key
                    && candidate.Value.Doc.Language == work.Doc.Language
                    && candidate.Value.Doc.ValidFrom == work.Doc.ValidFrom)
                .OrderByDescending(candidate => scores.GetValueOrDefault(candidate.Key))
                .Select(candidate => candidate.Key)
                .FirstOrDefault();
            if (key is null)
            {
                var standalone = WorkRetrievalHit(work);
                key = Key(standalone.Doc, standalone.Provision);
                fused[key] = standalone;
            }
            var weight = work.Reason == "semantic_concept" ? 0.60 : 0.65;
            scores[key] = scores.GetValueOrDefault(key) + weight / (60 + i + 1);
            if (fused.TryGetValue(key, out var prior))
                fused[key] = prior with
                {
                    MatchReasons = prior.MatchReasons.Append(work.Reason).Distinct().ToArray(),
                };
        }
        var hits = fused.Select(kv => kv.Value with { Score = scores[kv.Key] })
            .OrderByDescending(h => h.Score)
            // RRF intentionally gives the first lexical and first semantic-only candidates the
            // same score. On that exact tie, evidence strength is the relevance signal; document
            // recency is not. Without this tie-break a newer semantic near-neighbour can outrank
            // the provision containing the literal words the reader typed.
            .ThenByDescending(h =>
                (h.MatchReasons.Contains("keyword") ? 2 : 0)
                + (h.MatchReasons.Contains("semantic") ? 1 : 0)
                + (h.MatchReasons.Contains("semantic_work") ? 1 : 0))
            .ThenByDescending(h => h.Doc.ValidFrom, StringComparer.Ordinal)
            .Take(limit).ToList();
        return new SearchExecution("hybrid", hits, keyword.QueryExpansions, plan);
    }

    public SearchExecution SearchKeyword(string query, FilterSet filters, int limit, bool fuzzyAuto)
    {
        if (!_hasWorkSearch)
        {
            var legacyPlan = (IsV3 ? BasicQueryPlan(query) : new SearchQueryPlan(
                query, query, [], null, null, false)) with { WorkCatalogAvailable = false };
            if (HasRoleConflict(filters, legacyPlan))
                return new SearchExecution("keyword", [], [], legacyPlan);
            if (IsV3 && legacyPlan.ArticleNumber is not null)
            {
                var legacyArticleRows = SearchArticleIntent(
                    legacyPlan.ArticleNumber, filters, limit, legacyPlan.ProvisionQuery);
                var legacyReasons = string.IsNullOrWhiteSpace(legacyPlan.ProvisionQuery)
                    ? new[] { "article_intent" }
                    : new[] { "article_intent", "keyword" };
                return new SearchExecution("keyword", legacyArticleRows.Select((hit, rank) =>
                    new RetrievalHit(hit.Doc, hit.Prov, hit.Snippet, 2d / (rank + 1),
                        legacyReasons)).ToList(), [], legacyPlan);
            }
            var legacy = SearchKeywordWithoutWorkCatalog(
                legacyPlan.ProvisionQuery, filters, limit, fuzzyAuto);
            return legacy with { QueryPlan = LegacyIdentifierResolution(query, legacyPlan, legacy.Hits) };
        }

        var catalogMatches = WorkSearch.Find(
            _conn, query, filters.Language, filters.AsOf, Math.Max(limit * 2, 20),
            _workCatalogVersion >= 2);
        var identityFilters = filters with { DocumentRole = null };
        var workMatches = ResolveWorkMatches(catalogMatches, identityFilters);
        var identityMatches = workMatches.Where(IsWorkIdentity).ToList();
        var resolutions = BuildWorkResolutions(query, identityMatches);
        var resolvedMentions = resolutions.Where(item => item.Status == "resolved")
            .Select(item => WorkSearch.Normalize(item.Mention)).ToHashSet(StringComparer.Ordinal);
        var strong = identityMatches.Where(hit => hit.MatchedValue is not null
                                                   && resolvedMentions.Contains(hit.MatchedValue))
            .ToList();
        var plan = BuildQueryPlan(query, identityMatches, resolutions);
        if (HasRoleConflict(filters, plan))
            return new SearchExecution("keyword", [], [], plan);
        var requestedRole = filters.DocumentRole
                            ?? (_workCatalogVersion >= 2 ? plan.RoleIntent : null);
        var roleMatches = requestedRole is null
            ? workMatches
            : ResolveWorkMatches(catalogMatches, identityFilters with { DocumentRole = requestedRole });
        var resultStrong = strong.Where(hit => roleMatches.Any(roleHit =>
            roleHit.Doc.GroupKey == hit.Doc.GroupKey && roleHit.Doc.Language == hit.Doc.Language)).ToList();
        var weak = roleMatches.Where(hit => !hit.Reason.StartsWith("exact_", StringComparison.Ordinal)
                                             && !hit.Reason.StartsWith("contained_", StringComparison.Ordinal))
            .Where(hit => !plan.HasStrongWorkMatch
                          || plan.WorkConstraints.Contains(hit.Doc.GroupKey, StringComparer.Ordinal));
        var effectiveFilters = ConstrainToWorks(filters, plan);
        if (_workCatalogVersion >= 2 && plan.RoleIntent is not null
            && effectiveFilters.DocumentRole is null)
            effectiveFilters = effectiveFilters with { DocumentRole = plan.RoleIntent };
        var articleRows = plan.ArticleNumber is null
            ? []
            : SearchArticleIntent(plan.ArticleNumber, effectiveFilters, limit,
                plan.HasStrongWorkMatch ? null : plan.ProvisionQuery);
        var articleHits = articleRows
                .Select((hit, rank) => new RetrievalHit(
                    hit.Doc, hit.Prov, hit.Snippet, 2d / (rank + 1),
                    !plan.HasStrongWorkMatch && !string.IsNullOrWhiteSpace(plan.ProvisionQuery)
                        ? ["article_intent", "keyword"]
                        : ["article_intent"]))
                .ToList();
        var exactIdentifierFallback = plan.ProvisionQuery.Length == 0 && IsExactLegalIdentifier(query);
        var provision = plan.ArticleNumber is not null
            ? new SearchExecution("keyword", [], [])
            : SearchKeywordWithoutWorkCatalog(
                exactIdentifierFallback ? query : plan.ProvisionQuery,
                exactIdentifierFallback ? filters : effectiveFilters,
                limit,
                fuzzyAuto && strong.Count == 0 && plan.ProvisionQuery.Length > 0);
        var directEvidence = articleHits.Concat(provision.Hits).ToList();
        var combined = (directEvidence.Count > 0
                ? directEvidence.Concat(resultStrong.Select(WorkRetrievalHit))
                : resultStrong.Select(WorkRetrievalHit))
            .Concat(identityMatches.Where(hit => hit.MatchedValue is not null
                                                  && !resolvedMentions.Contains(hit.MatchedValue))
                .Select(hit => WorkRetrievalHit(hit) with
                {
                    MatchReasons = ["ambiguous_" + hit.Reason],
                }))
            .Concat(weak.Select(WorkRetrievalHit))
            .GroupBy(hit => $"{hit.Doc.GroupKey}|{hit.Doc.Language}|{hit.Provision.Anchor}|{hit.Provision.TextSha}",
                StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(limit)
            .ToList();
        return new SearchExecution("keyword", combined, provision.QueryExpansions, plan);
    }

    private SearchExecution SearchKeywordWithoutWorkCatalog(
        string query, FilterSet filters, int limit, bool fuzzyAuto)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchExecution("keyword", [], []);
        if (IsExactLegalIdentifier(query))
        {
            var works = SearchWorksByIdentifierOrTitle(query, filters, limit)
                .GroupBy(d => d.GroupKey).Select(g => g.OrderByDescending(d => d.ValidFrom, StringComparer.Ordinal).First())
                .Select((d, rank) => new RetrievalHit(d,
                    new ProvisionRow(RidOf(d), 0, "", d.Key, "work", null, null, null, null,
                        d.Title, "", d.BodySha ?? ""),
                    d.Title ?? d.GroupKey, 1d / (rank + 1), ["exact_identifier"]))
                .ToList();
            if (works.Count > 0) return new SearchExecution("keyword", works, []);
        }
        var exact = Search(query, filters, limit);
        if (!fuzzyAuto || exact.Count >= 5 || !IsV3)
            return new SearchExecution("keyword", exact.Select((h, rank) =>
                new RetrievalHit(h.Doc, h.Prov, h.Snippet, 1d / (rank + 1), ["keyword"])).ToList(), []);

        var expansions = FuzzyExpansions(query);
        if (expansions.Count == 0)
            return new SearchExecution("keyword", exact.Select((h, rank) =>
                new RetrievalHit(h.Doc, h.Prov, h.Snippet, 1d / (rank + 1), ["keyword"])).ToList(), []);

        var combined = exact.Select((h, rank) => new RetrievalHit(
            h.Doc, h.Prov, h.Snippet, 2d / (rank + 1), ["keyword"])).ToList();
        foreach (var expansion in expansions)
        {
            var alternative = ReplaceToken(query, expansion.Source, expansion.Target);
            combined.AddRange(Search(alternative, filters, limit).Select((h, rank) => new RetrievalHit(
                h.Doc, h.Prov, h.Snippet, 1d / (rank + 1), ["fuzzy"])));
        }
        var hits = combined.GroupBy(h => $"{h.Doc.GroupKey}|{h.Doc.Language}|{h.Provision.Anchor}|{h.Provision.TextSha}", StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(h => h.Score).First())
            .OrderByDescending(h => h.Score).Take(limit).ToList();
        return new SearchExecution("keyword", hits,
            expansions.Select(e => $"{e.Source} -> {e.Target}").ToList());
    }

    private static readonly HashSet<string> ConversationalSearchWords = new(StringComparer.Ordinal)
    {
        "a", "about", "an", "are", "ce", "ces", "comment", "de", "des", "did", "do", "does",
        "du", "est", "for", "la", "le", "les", "me", "please", "que", "quel", "quelle",
        "quels", "quelles", "quoi", "show", "the", "tell", "what",
    };

    private static readonly string[] ConversationalReferencePhrases =
    [
        "on the same date", "at the same date", "on that date", "at that date",
        "the same date", "same date", "that date", "this date",
        "the same law", "same law", "that law", "this law",
        "the same instrument", "same instrument", "that instrument", "this instrument",
        "a la meme date", "a cette date", "la meme date", "cette date",
        "la meme loi", "cette loi", "le meme texte", "ce texte",
    ];

    private static readonly (string Phrase, string Role)[] RolePhrases =
    [
        ("implementing regulation", "implementing"),
        ("implementing act", "implementing"), ("reglement d execution", "implementing"),
        ("acte d execution", "implementing"),
        ("delegated regulation", "delegated"), ("delegated act", "delegated"),
        ("reglement delegue", "delegated"), ("acte delegue", "delegated"),
        ("amending regulation", "amending"), ("amending act", "amending"),
        ("reglement modificatif", "amending"), ("acte modificatif", "amending"),
        ("corrigendum", "corrigendum"), ("rectificatif", "corrigendum"),
        ("consolidated", "consolidated"), ("consolide", "consolidated"),
    ];

    private static readonly (string Phrase, string Role)[] AmbiguousRolePhrases =
    [
        ("implementing", "implementing"),
        ("delegated", "delegated"), ("delegue", "delegated"),
        ("amending", "amending"), ("modificatif", "amending"),
    ];

    private static SearchQueryPlan BasicQueryPlan(string query)
    {
        var normalized = WorkSearch.Normalize(query);
        var article = ArticleNumber(normalized);
        var residual = RemoveArticleIntent(normalized, article);
        var role = RolePhrases.FirstOrDefault(item => ContainsPhrase(normalized, item.Phrase)).Role;
        residual = RemoveRoleIntent(residual, role);
        residual = RemoveConversationalReferences(residual);
        residual = RemoveProfessionalConceptRequest(residual);
        var filtered = string.Join(' ', residual.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !ConversationalSearchWords.Contains(token)));
        if (article is null && role is null && filtered == normalized)
            filtered = query.Trim();
        var identifiers = LegalIdentifierTokens(query);
        return new SearchQueryPlan(
            query, filtered, [], article, role, false,
            identifiers.Count == 0 ? "not_requested" : "unresolved",
            identifiers.Select(identifier => new WorkResolution(identifier, "unresolved", [])).ToArray());
    }

    private static SearchQueryPlan BuildQueryPlan(
        string query,
        IReadOnlyList<WorkSearchHit> identityMatches,
        IReadOnlyList<WorkResolution> resolutions)
    {
        var basic = BasicQueryPlan(query);
        if (identityMatches.Count == 0) return basic;
        var normalized = WorkSearch.Normalize(query);
        var resolved = resolutions.Where(item => item.Status == "resolved").ToArray();
        var role = basic.RoleIntent;
        if (role is null && resolved.Length > 0)
            role = AmbiguousRolePhrases
                .FirstOrDefault(item => ContainsPhrase(normalized, item.Phrase)).Role;
        var residual = " " + normalized + " ";
        foreach (var name in identityMatches.Select(match => match.MatchedValue)
                     .Concat(resolutions.Select(resolution => WorkSearch.Normalize(resolution.Mention)))
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal)
                     .OrderByDescending(value => value!.Length))
        {
            var padded = " " + name + " ";
            if (residual.Contains(padded, StringComparison.Ordinal))
            {
                residual = residual.Replace(padded, " ", StringComparison.Ordinal);
                continue;
            }
            // The name resolved through the citation form ("loi modifiée du 12 novembre 2004"
            // against a title that omits the qualifier). Strip the form that actually matched, so
            // the inserted qualifier does not leak into the provision query as a stray token.
            var citation = " " + WorkSearch.NormalizeCitation(residual.Trim()) + " ";
            if (citation.Contains(padded, StringComparison.Ordinal))
                residual = citation.Replace(padded, " ", StringComparison.Ordinal);
        }
        residual = RemoveArticleIntent(residual.Trim(), basic.ArticleNumber);
        residual = RemoveRoleIntent(residual, role);
        residual = RemoveConversationalReferences(residual);
        residual = RemoveProfessionalConceptRequest(residual);
        residual = string.Join(' ', residual.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !ConversationalSearchWords.Contains(token)));
        var works = resolved.SelectMany(item => item.Candidates)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return basic with
        {
            ProvisionQuery = residual,
            WorkConstraints = works,
            RoleIntent = role,
            HasStrongWorkMatch = works.Length > 0,
            WorkResolutionStatus = ResolutionStatus(resolutions),
            WorkResolutions = resolutions,
        };
    }

    private static string RemoveProfessionalConceptRequest(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 7 || tokens[0] != "find") return query;
        var provision = Array.FindIndex(tokens, 1,
            token => token is "provision" or "provisions");
        if (provision < 0 || provision + 4 >= tokens.Length
            || tokens[provision + 1] != "that"
            || tokens[provision + 2] != "describe")
            return query;
        var relation = provision + 3;
        if (tokens[relation] == "the") relation++;
        if (relation + 1 >= tokens.Length
            || tokens[relation] != "responsibilities"
            || tokens[relation + 1] != "of")
            return query;
        var concept = tokens[(relation + 2)..];
        if (concept.FirstOrDefault() is "a" or "an" or "the") concept = concept[1..];
        return concept.Length == 0 ? query : string.Join(' ', concept);
    }

    /// <summary>
    /// Whether a catalog match names a work rather than merely mentioning words it contains.
    /// Only these matches may resolve a mention, scope the query, or become a work constraint.
    /// </summary>
    /// A title quoted inside a longer question names the work only when the title is a
    /// designation carrying its own number ("Regulation (EU) 2016/679"). A descriptive title
    /// ("Reporting obligations") is reproduced by ordinary prose and stays weak, which is the
    /// same digit requirement WorkSearch.Find already imposes on a contained official identifier.
    /// Dropping contained_title entirely was the bug: the user's own words named the instrument
    /// and the search reported work_resolution_status=not_requested anyway.
    ///
    /// A digit is not the only proof of a designation. National Codes carry no date in their name
    /// (Code du travail, Code civil, the Constitution), so the digit rule alone made the
    /// most-cited instruments of a whole jurisdiction unnameable inside a sentence. A name opening
    /// on an act form is a designation, not prose: a descriptive heading ("Reporting obligations")
    /// and a recueil label ("Cours et Tribunaux") stay weak, and every EU title carries a number
    /// anyway.
    private static bool IsWorkIdentity(WorkSearchHit hit) => hit.Reason switch
    {
        "exact_identifier" or "exact_alias" or "exact_title"
            or "exact_publisher_short_title" => true,
        "contained_identifier" or "contained_alias" or "contained_publisher_short_title" => true,
        "contained_title" => hit.MatchedValue is { } value
            && (value.Any(char.IsDigit) || WorkSearch.IsActFormDesignation(value)),
        _ => false,
    };

    private static IReadOnlyList<WorkResolution> BuildWorkResolutions(
        string query, IReadOnlyList<WorkSearchHit> identityMatches)
    {
        var matches = identityMatches
            .Where(match => !string.IsNullOrWhiteSpace(match.MatchedValue))
            .GroupBy(match => WorkSearch.Normalize(match.MatchedValue!), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (
                    Candidates: group.Select(match => match.Doc.GroupKey)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(20).ToArray(),
                    Kind: MatchKind(group.Select(match => match.Reason)),
                    ShortTitles: group.Where(match =>
                            match.MatchedPublisherMetadata is not null
                            && match.Reason.EndsWith("_publisher_short_title", StringComparison.Ordinal))
                        .Select(match => new PublisherShortTitleMatch(
                            match.Doc.GroupKey,
                            match.MatchedPublisherMetadata!.MatchedSegment!,
                            match.MatchedPublisherMetadata.Identifier,
                            match.MatchedPublisherMetadata.Label ?? "",
                            match.MatchedPublisherMetadata.Language ?? match.Doc.Language,
                            match.MatchedPublisherMetadata.SourceUri))
                        .Distinct().OrderBy(match => match.Work, StringComparer.Ordinal)
                        .ThenBy(match => match.Language, StringComparer.Ordinal)
                        .ThenBy(match => match.Identifier, StringComparer.Ordinal)
                        .Take(20).ToArray()),
                StringComparer.Ordinal);
        matches = DemoteAmendingClauseMentions(query, matches);
        var result = new List<WorkResolution>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in LegalIdentifierTokens(query))
        {
            var normalized = WorkSearch.Normalize(identifier);
            var match = matches.GetValueOrDefault(normalized);
            var candidates = match.Candidates ?? [];
            consumed.Add(normalized);
            result.Add(new WorkResolution(identifier, candidates.Length switch
            {
                0 => "unresolved",
                1 => "resolved",
                _ => "ambiguous",
            }, candidates, match.Kind, match.ShortTitles));
        }
        foreach (var (mention, match) in matches.Where(item => !consumed.Contains(item.Key)))
            result.Add(new WorkResolution(mention,
                match.Candidates.Length == 1 ? "resolved" : "ambiguous",
                match.Candidates, match.Kind, match.ShortTitles));
        return result.OrderBy(item => WorkSearch.Normalize(item.Mention), StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Drops mentions the user's own sentence put inside an amending or repealing clause, when
    /// the sentence also named a work outside one.
    ///
    /// <para>An official EU title routinely ends by naming another instrument: the CRR's ends
    /// "and amending Regulation (EU) No 648/2012". A lawyer quoting it in full therefore names
    /// two works, both match as identity, both resolve to a single candidate, and nothing
    /// downstream looks ambiguous. That is the conflation that served EMIR Article 26 for a CRR
    /// question, and it is created here, so it is corrected here rather than only being survived
    /// later by <c>WorkSubjectRule</c>.</para>
    ///
    /// <para>Strictly a demotion over the already-identified set, never a selector. A question
    /// whose ONLY named work sits in such a clause is asking about that work ("what repealed
    /// Directive 95/46/EC?"), so when nothing survives the filter, nothing is dropped.</para>
    /// </summary>
    private static Dictionary<string, (string[] Candidates, string? Kind,
        PublisherShortTitleMatch[] ShortTitles)>
        DemoteAmendingClauseMentions(
            string query, Dictionary<string, (string[] Candidates, string? Kind,
                PublisherShortTitleMatch[] ShortTitles)> matches)
    {
        if (matches.Count < 2) return matches;
        var normalized = WorkSearch.Normalize(query);
        var kept = matches
            .Where(item => !OnlyEverInAnAmendingClause(normalized, item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return kept.Count == 0 || kept.Count == matches.Count ? matches : kept;
    }

    /// <summary>
    /// Whether EVERY place the sentence names this work sits after an amending participle.
    ///
    /// <para>One occurrence is not enough to judge by. A sentence may cite an instrument in a
    /// trailing clause and then ask about that same instrument directly, and taking only the
    /// first position would demote the work the question is actually about. A single mention
    /// outside a clause is therefore enough to keep it.</para>
    /// </summary>
    private static bool OnlyEverInAnAmendingClause(string normalizedQuery, string mention)
    {
        var padded = " " + normalizedQuery + " ";
        var needle = " " + mention + " ";
        var found = false;
        for (var at = padded.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = padded.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            found = true;
            // The needle opens with a space, so its index in the padded string is already the
            // mention's own start in the unpadded one.
            if (!WorkSearch.FollowsAmendingClause(normalizedQuery, at)) return false;
        }
        return found;
    }

    /// <summary>Which stored name form a mention matched, reduced from the per-hit reasons
    /// (<c>exact_title</c>, <c>contained_identifier</c>, ...) that carried it all along. One
    /// mention normally has one form; when a stored title and a stored alias happen to normalize
    /// to the same string, the title is the stronger claim about identity and wins.</summary>
    private static string? MatchKind(IEnumerable<string> reasons)
    {
        string? kind = null;
        foreach (var reason in reasons)
        {
            if (reason.EndsWith("_publisher_short_title", StringComparison.Ordinal))
                return "publisher_short_title";
            if (reason.EndsWith("_title", StringComparison.Ordinal)) return "title";
            if (reason.EndsWith("_alias", StringComparison.Ordinal)) kind = "alias";
            else if (reason.EndsWith("_identifier", StringComparison.Ordinal))
                kind ??= "identifier";
        }
        return kind;
    }

    private static string ResolutionStatus(IReadOnlyList<WorkResolution> resolutions) =>
        resolutions.Any(item => item.Status == "ambiguous") ? "ambiguous"
        : resolutions.Any(item => item.Status == "unresolved") ? "unresolved"
        : resolutions.Any(item => item.Status == "resolved") ? "resolved"
        : "not_requested";

    private static SearchQueryPlan LegacyIdentifierResolution(
        string query, SearchQueryPlan plan, IReadOnlyList<RetrievalHit> hits)
    {
        var identifier = ExactLegalIdentifierToken(query);
        if (identifier is null) return plan;
        var candidates = hits.Select(hit => hit.Doc.GroupKey)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var status = candidates.Length switch
        {
            0 => "unresolved",
            1 => "resolved",
            _ => "ambiguous",
        };
        return plan with
        {
            WorkConstraints = status == "resolved" ? candidates : [],
            HasStrongWorkMatch = status == "resolved",
            WorkResolutionStatus = status,
            WorkResolutions = [new WorkResolution(identifier, status, candidates)],
        };
    }

    private static FilterSet ConstrainToWorks(FilterSet filters, SearchQueryPlan plan) =>
        !plan.HasStrongWorkMatch ? filters : filters with { Works = plan.WorkConstraints };

    private static bool HasRoleConflict(FilterSet filters, SearchQueryPlan plan) =>
        filters.DocumentRole is not null && plan.RoleIntent is not null
        && !string.Equals(filters.DocumentRole, plan.RoleIntent, StringComparison.Ordinal);

    private static string? ArticleNumber(string normalized)
    {
        var match = System.Text.RegularExpressions.Regex.Match(normalized,
            @"(?:^|\s)(?:article|art)\s+(?<number>(?:[a-z]\s+)?[0-9]+(?:(?:er)|[a-z])?(?:\s+[0-9]+)*(?:\s+(?:bis|ter|quater))?)(?:\s|$)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["number"].Value : null;
    }

    private static string RemoveArticleIntent(string normalized, string? articleNumber) =>
        articleNumber is null ? normalized : System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"(?:^|\s)(?:article|art)\s+" +
            System.Text.RegularExpressions.Regex.Escape(articleNumber) + @"(?:\s|$)",
            " ", System.Text.RegularExpressions.RegexOptions.CultureInvariant).Trim();

    private static string RemoveRoleIntent(string normalized, string? role)
    {
        if (role is null) return normalized;
        var result = " " + normalized + " ";
        foreach (var phrase in RolePhrases.Concat(AmbiguousRolePhrases)
                     .Where(item => item.Role == role)
                     .Select(item => item.Phrase).OrderByDescending(value => value.Length))
            result = result.Replace(" " + phrase + " ", " ", StringComparison.Ordinal);
        return result.Trim();
    }

    private static string RemoveConversationalReferences(string normalized)
    {
        var result = " " + normalized + " ";
        foreach (var phrase in ConversationalReferencePhrases)
            result = result.Replace(" " + phrase + " ", " ", StringComparison.Ordinal);
        return result.Trim();
    }

    private static bool ContainsPhrase(string normalized, string phrase) =>
        (" " + normalized + " ").Contains(" " + phrase + " ", StringComparison.Ordinal);

    private List<(DocRow Doc, ProvisionRow Prov, string Snippet)> SearchArticleIntent(
        string articleNumber, FilterSet filters, int limit, string? residualQuery = null)
    {
        var compact = articleNumber.Replace(" ", "", StringComparison.Ordinal);
        var anchor = "art_" + articleNumber.Replace(' ', '_');
        var (where, parameters) = WithFilters("1=1", filters, excludeAsOf: false, alias: "d");
        var hasResidual = !string.IsNullOrWhiteSpace(residualQuery);
        // FTS5 ranking functions cannot be evaluated in the same SELECT as a window function.
        // Materialize the FTS score first, then rank dated occurrences in the next CTE.
        var matched = hasResidual
            ? "matched AS (SELECT rowid AS state_id, bm25(fts,10.0,4.0,6.0,1.0) AS score FROM fts WHERE fts MATCH $residual),"
            : "";
        var ftsJoin = hasResidual ? "JOIN matched m ON m.state_id=p.state_id" : "";
        var score = hasResidual ? "m.score" : "0.0";
        using var command = Cmd($"""
            WITH {matched} eligible AS (
              SELECT p.rid,p.seq,p.anchor,p.provision_id,p.ptype,p.num,p.heading,p.path,
                     p.article_valid_from,p.work_title,p.text_sha,d.key AS doc_key,{score} AS score,
                     ROW_NUMBER() OVER (
                       PARTITION BY p.state_id ORDER BY d.valid_from DESC,p.rid
                     ) AS occurrence_rank
              FROM provisions p
              {ftsJoin}
              JOIN docs d ON d.rid=p.rid
              WHERE {where} AND p.ptype='article'
                AND (
                  lower(replace(replace(replace(p.num,' ',''),'.',''),'-',''))=$number
                  OR lower(replace(replace(p.anchor,'.',''),'-','_'))=$anchor
                )
            )
            SELECT {SelectDocCols("d")},
                   e.rid,e.seq,e.anchor,e.provision_id,e.ptype,e.num,e.heading,e.path,
                   e.article_valid_from,e.work_title,e.text_sha
            FROM eligible e
            JOIN docs d ON d.key=e.doc_key AND d.rid=e.rid
            WHERE e.occurrence_rank=1
            ORDER BY e.score,d.valid_from DESC,e.seq,e.rid
            LIMIT $limit
            """, parameters);
        command.Parameters.AddWithValue("$number", compact);
        command.Parameters.AddWithValue("$anchor", anchor);
        command.Parameters.AddWithValue("$limit", limit);
        if (hasResidual) command.Parameters.AddWithValue("$residual", Fts5Escape(residualQuery!));
        var result = new List<(DocRow, ProvisionRow, string)>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var provision = new ProvisionRow(
                rows.GetString(27), rows.GetInt32(28), rows.GetString(29), rows.GetString(30),
                rows.GetString(31), rows.IsDBNull(32) ? null : rows.GetString(32),
                rows.IsDBNull(33) ? null : rows.GetString(33),
                rows.IsDBNull(34) ? null : rows.GetString(34),
                rows.IsDBNull(35) ? null : rows.GetString(35),
                rows.IsDBNull(36) ? null : rows.GetString(36), "", rows.GetString(37), TextLoaded: false);
            result.Add((ReadDoc(rows), provision,
                provision.Heading ?? provision.Num ?? provision.Anchor));
        }
        return result;
    }

    private IReadOnlyList<WorkSearchHit> SearchWorkSemanticCatalog(
        float[] queryVector, FilterSet filters, int limit)
    {
        if (!_hasWorkSearch || _encoder is null || limit <= 0) return [];
        return ResolveWorkMatches(
                WorkSearch.FindSemantic(_conn, _vectors!, queryVector, limit), filters)
            .Take(limit).ToArray();
    }

    private IReadOnlyList<WorkSearchHit> ResolveWorkMatches(
        IReadOnlyList<WorkMatch> matches, FilterSet filters)
    {
        var result = new List<WorkSearchHit>();
        foreach (var match in matches)
        {
            using var identity = Cmd(
                "SELECT group_key,language FROM work_records WHERE work_id=$work_id", []);
            identity.Parameters.AddWithValue("$work_id", match.WorkId);
            using var identityRow = identity.ExecuteReader();
            if (!identityRow.Read()) continue;
            var groupKey = identityRow.GetString(0);
            var language = identityRow.GetString(1);
            identityRow.Close();

            var (where, parameters) = WithFilters(
                "docs.group_key=$work_group AND docs.language=$work_language", filters,
                excludeAsOf: false, alias: "docs");
            using var command = Cmd($"""
                SELECT {SelectDocCols()}
                FROM docs
                WHERE {where}
                ORDER BY valid_from DESC
                LIMIT 1
                """, parameters);
            command.Parameters.AddWithValue("$work_group", groupKey);
            command.Parameters.AddWithValue("$work_language", language);
            using var rows = command.ExecuteReader();
            if (rows.Read())
            {
                var doc = ReadDoc(rows);
                result.Add(new WorkSearchHit(doc, match.Reason, match.Score,
                    match.MatchedValue, match.MatchedPublisherMetadata));
            }
        }
        return result;
    }

    private static RetrievalHit WorkRetrievalHit(WorkSearchHit hit) => new(
        hit.Doc,
        new ProvisionRow(RidOf(hit.Doc), 0, "", hit.Doc.Key, "work", null, null, null, null,
            hit.Doc.Title, "", hit.Doc.BodySha ?? ""),
        hit.Doc.Title ?? hit.Doc.GroupKey,
        hit.Score,
        [hit.Reason],
        hit.MatchedPublisherMetadata);

    private List<(string Source, string Target)> FuzzyExpansions(string query)
    {
        var unquoted = System.Text.RegularExpressions.Regex.Replace(query, "\"[^\"]+\"", " ");
        var tokens = unquoted.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('"', '\'', '.', ':', '(', ')')).Distinct(StringComparer.OrdinalIgnoreCase).Take(8);
        var result = new List<(string, string)>();
        foreach (var token in tokens)
        {
            if (token.Length < 4 || IsProtectedSearchToken(token)) continue;
            var maximum = token.Length >= 8 ? 2 : 1;
            using var cmd = Cmd("SELECT term FROM fts_vocab WHERE term >= $prefix AND term < $end LIMIT 500", []);
            var prefix = char.ToLowerInvariant(token[0]).ToString();
            cmd.Parameters.AddWithValue("$prefix", prefix);
            cmd.Parameters.AddWithValue("$end", ((char)(char.ToLowerInvariant(token[0]) + 1)).ToString());
            var candidates = new List<(string Term, int Distance)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var term = reader.GetString(0);
                var distance = EditDistance(token.ToLowerInvariant(), term, maximum);
                if (distance is > 0 && distance <= maximum) candidates.Add((term, distance));
            }
            result.AddRange(candidates.OrderBy(c => c.Distance).ThenBy(c => c.Term, StringComparer.Ordinal)
                .Take(2).Select(c => (token, c.Term)));
        }
        return result.Take(6).ToList();
    }

    private static bool IsProtectedSearchToken(string token) =>
        token.Any(char.IsDigit) || token.Contains('/') || token.Contains(':')
        || System.Text.RegularExpressions.Regex.IsMatch(token, "^(celex|ecli|article|art)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsExactLegalIdentifier(string query) => ExactLegalIdentifierToken(query) is not null;

    private static string? ExactLegalIdentifierToken(string query)
    {
        var trimmed = query.Trim();
        var celex = System.Text.RegularExpressions.Regex.Match(trimmed,
            "^(?:CELEX\\s*)?(?<identifier>[136][0-9]{4}[A-Z][0-9]{4}|1[0-9]{4}[A-Z]/TXT)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (celex.Success) return celex.Groups["identifier"].Value;
        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^ECLI:[A-Z0-9:.]+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? trimmed : null;
    }

    private static IReadOnlyList<string> LegalIdentifierTokens(string query)
    {
        var results = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(query,
                     @"(?<![A-Z0-9])(?:CELEX\s*)?(?<identifier>[136][0-9]{4}[A-Z][0-9]{4}|1[0-9]{4}[A-Z]/TXT)(?![A-Z0-9])|(?<![A-Z0-9])(?<ecli>ECLI:[A-Z0-9:.]+)(?![A-Z0-9])",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase
                     | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            var value = match.Groups["identifier"].Success
                ? match.Groups["identifier"].Value : match.Groups["ecli"].Value;
            if (!results.Contains(value, StringComparer.OrdinalIgnoreCase)) results.Add(value);
        }
        return results;
    }

    private static string ReplaceToken(string query, string source, string target) =>
        System.Text.RegularExpressions.Regex.Replace(query,
            $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(source)}(?![\p{{L}}\p{{N}}])",
            target, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static int EditDistance(string left, string right, int stopAfter)
    {
        if (Math.Abs(left.Length - right.Length) > stopAfter) return stopAfter + 1;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= right.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > stopAfter) return stopAfter + 1;
            previous = current;
        }
        return previous[^1];
    }

    private List<RetrievalHit> SearchSemantic(
        string query, float[] queryVector, FilterSet filters, int limit)
    {
        var binary = SemanticVectorReader.Binary(queryVector);
        var int8 = SemanticVectorReader.Int8(queryVector);
        var nearest = _vectors!.NearestByHamming(
            binary, Math.Max(2_000, limit * 20), 0, _provisionVectorCount);
        if (nearest.Count == 0) return [];
        var hamming = nearest.ToDictionary(candidate => candidate.Ordinal, candidate => candidate.Distance);
        // Ordinals originate in the verified vector file, not user input. A literal IN list lets
        // SQLite scan semantic_chunks once even for an older v3 artifact that predates the
        // vector-ordinal index; future builds use ix_semantic_vector directly.
        var ordinalSql = string.Join(',', nearest.Select(candidate => candidate.Ordinal));
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false, alias: "d");
        using var cmd = Cmd($"""
            WITH eligible AS (
              SELECT sc.state_id, sc.vector_ordinal, p.rid, p.anchor,
                     ROW_NUMBER() OVER (PARTITION BY sc.chunk_id ORDER BY d.valid_from DESC, p.rid) AS occurrence_rank
              FROM semantic_chunks sc
              JOIN provisions p ON p.state_id=sc.state_id
              JOIN docs d ON d.rid=p.rid
              WHERE sc.vector_ordinal IN ({ordinalSql}) AND {where}
            )
            SELECT state_id, vector_ordinal, rid, anchor FROM eligible WHERE occurrence_rank=1
            """, ps);
        var candidates = new List<(long State, long Ordinal, string Rid, string Anchor, int Hamming, int Dot)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var ordinal = r.GetInt64(1);
                candidates.Add((r.GetInt64(0), ordinal, r.GetString(2), r.GetString(3),
                    hamming[ordinal], 0));
            }
        var reranked = candidates.OrderBy(c => c.Hamming).Take(500)
            .Select(c => c with { Dot = _vectors!.Int8Dot(c.Ordinal, int8) })
            .GroupBy(c => c.State).Select(g => g.OrderByDescending(c => c.Dot).First())
            .OrderByDescending(c => c.Dot).Take(limit).ToList();
        var hits = new List<RetrievalHit>();
        foreach (var candidate in reranked)
        {
            var doc = DocByRid(candidate.Rid);
            var provision = ProvisionOutline(candidate.Rid, candidate.Anchor);
            if (doc is null || provision is null) continue;
            hits.Add(new RetrievalHit(doc, provision,
                provision.Heading ?? provision.Num ?? provision.WorkTitle ?? provision.Anchor,
                candidate.Dot / (127d * 127d), ["semantic"]));
        }
        return hits;
    }

    private DocRow? DocByRid(string rid)
    {
        using var cmd = Cmd($"SELECT {SelectDocCols()} FROM docs WHERE rid=$rid LIMIT 1", []);
        cmd.Parameters.AddWithValue("$rid", rid);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadDoc(r) : null;
    }

    /// <summary>
    /// Identifier/title lookup, complementing the provision FTS. Covers two things the FTS
    /// cannot: works whose body is missing upstream (no provisions to match), and lookups by
    /// the publisher's own identifier — a CELEX number like 32022r2554 is the canonical way to
    /// name an EU act, and it lives in the work slug, not in any indexed text.
    /// Terms are AND-ed over slug-or-title; if that yields nothing, the most distinctive term
    /// is matched against the slug alone, so "CELEX 32022R2554" still resolves.
    /// </summary>
    public List<DocRow> SearchWorksByIdentifierOrTitle(string query, FilterSet filters, int limit)
    {
        var terms = query.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2).Take(6).ToList();
        if (terms.Count == 0) return [];
        var exactIdentifier = ExactLegalIdentifierToken(query);

        var hits = Lookup(terms.Select((t, i) =>
            $"(d.title LIKE $t{i} OR d.group_key LIKE $t{i} OR d.group_identifier LIKE $t{i})"), terms);
        if (hits.Count > 0) return hits;

        // "CELEX 32022R2554", "see 32013r0575" — keep the identifier, drop the chatter.
        var distinctive = terms.OrderByDescending(t => t.Length).First();
        return distinctive.Length >= 5 ? Lookup(["(d.group_key LIKE $t0)"], [distinctive]) : [];

        List<DocRow> Lookup(IEnumerable<string> clauses, IReadOnlyList<string> values)
        {
            var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false, alias: "d");
            var exactOrder = exactIdentifier is null ? "" : """
                CASE WHEN lower(d.group_key) = lower(replace($exact_identifier, '/', '-'))
                       OR lower(d.group_identifier) = lower($exact_identifier)
                       OR lower(d.group_identifier) LIKE '%/' || lower($exact_identifier)
                       OR lower(d.group_identifier) LIKE '%:' || lower($exact_identifier)
                     THEN 0 ELSE 1 END,
                """;
            using var cmd = Cmd($"""
                SELECT {SelectDocCols("d")}
                FROM docs d
                WHERE {where} AND {string.Join(" AND ", clauses)}
                ORDER BY {exactOrder} d.valid_from DESC
                LIMIT $lim
                """, ps);
            for (var i = 0; i < values.Count; i++) cmd.Parameters.AddWithValue($"$t{i}", "%" + values[i] + "%");
            if (exactIdentifier is not null) cmd.Parameters.AddWithValue("$exact_identifier", exactIdentifier);
            cmd.Parameters.AddWithValue("$lim", limit);
            var result = new List<DocRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(ReadDoc(r));
            return result;
        }
    }

    /// <summary>
    /// Cross-work aggregation: which works gained versions inside a window, how many, and when.
    /// The one question shape the per-work tools cannot answer — "what moved, across the corpus".
    /// </summary>
    /// <summary>
    /// What moved in a window. `kinds` accepts several document types, because the useful question
    /// is rarely about one code: a reader asks for statutes, or for everything that is an
    /// instrument, or for the thematic collections on their own. `offset` pages through the rest,
    /// since a six-year window moves 860 works and a first page of 25 is not the whole answer.
    /// </summary>
    /// <summary>
    /// Type predicate for a set of document types. A member prefixed with "!" inverts the whole
    /// set, so "!RECUEIL,!CODE_RECUEIL" reads as "anything that is not a thematic collection",
    /// which is the ordinary case and would otherwise mean naming every other type by hand.
    /// </summary>
    private static string KindClause(IReadOnlyList<string>? kinds, string alias)
    {
        if (kinds is not { Count: > 0 }) return "";
        var negate = kinds[0].StartsWith('!');
        var names = kinds.Select((_, i) => $"$k{i}");
        return $" AND ({alias}kind {(negate ? "IS NULL OR " + alias + "kind NOT IN" : "IN")} ({string.Join(",", names)}))";
    }

    private static void BindKinds(SqliteCommand cmd, IReadOnlyList<string>? kinds)
    {
        if (kinds is not { Count: > 0 }) return;
        for (var i = 0; i < kinds.Count; i++)
            cmd.Parameters.AddWithValue($"$k{i}", kinds[i].TrimStart('!'));
    }

    public List<ChangeRow> ChangesInPeriod(string from, string to, IReadOnlyList<string>? kinds,
                                           bool byChurn, int limit, int offset = 0,
                                           FilterSet? filter = null)
    {
        var scoped = (filter ?? FilterSet.All) with
        {
            Kind = kinds is { Count: > 0 } ? string.Join(',', kinds) : filter?.Kind,
        };
        var (where, parameters) = WithFilters(
            "d.valid_from >= $from AND d.valid_from <= $to", scoped, excludeAsOf: true,
            alias: "d");
        var order = byChurn
            ? "versions DESC, last_change DESC, d.group_key"
            : "last_change DESC, versions DESC, d.group_key";
        var metadata = IsV3
            ? "MAX(d.kind), MAX(d.hierarchy), MAX(d.domains), MAX(d.act_form), MAX(d.binding_status), MAX(d.language)"
            : "MAX(d.kind), NULL, NULL, NULL, NULL, MAX(d.language)";
        using var cmd = Cmd($"""
            SELECT d.group_key,
                   COUNT(DISTINCT d.key) AS versions,
                   MIN(d.valid_from) AS first_change,
                   MAX(d.valid_from) AS last_change,
                   (SELECT COALESCE(t.title_short, t.title) FROM docs t WHERE t.group_key = d.group_key
                     ORDER BY t.valid_from DESC LIMIT 1) AS title,
                   (SELECT COUNT(DISTINCT t2.key) FROM docs t2 WHERE t2.group_key = d.group_key) AS versions_total,
                   -- The state this law was in before the window touched it: the newest version
                   -- strictly older than the window's first change. This is what "what changed"
                   -- has to compare against; comparing first_change with last_change is a
                   -- comparison with itself whenever a work moved exactly once.
                   (SELECT MAX(t3.valid_from) FROM docs t3
                     WHERE t3.group_key = d.group_key AND t3.valid_from < MIN(d.valid_from)) AS baseline,
                   0 AS distinct_texts,
                   {metadata}
            FROM docs d
            WHERE {where}
            GROUP BY d.group_key
            ORDER BY {order}
            LIMIT $lim OFFSET $off
            """, parameters);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);
        cmd.Parameters.AddWithValue("$lim", limit);
        cmd.Parameters.AddWithValue("$off", Math.Max(0, offset));
        var list = new List<ChangeRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ChangeRow(r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6), 0, false,
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetString(11),
                r.IsDBNull(12) ? null : r.GetString(12),
                r.IsDBNull(13) ? null : r.GetString(13)));
        r.Close();
        // Answered per row rather than in the aggregate above, because it is a question about
        // the PROVISIONS and the aggregate is a question about versions. Two small indexed reads
        // per row; the report returns at most a page of them.
        return list.Select(c =>
        {
            var comparison = WordingComparison(c.GroupKey, c.Baseline ?? c.FirstChange, c.LastChange);
            return c with { DistinctTexts = comparison.Distinct, TextComparable = comparison.BothAvailable };
        }).ToList();
    }

    /// <summary>
    /// Whether the wording differs between the two versions a comparison would actually show:
    /// 2 when it does, 1 when it does not, 0 when neither side carries text.
    ///
    /// Counted from the ordered per-provision hashes, not from the file hash. The file hash was
    /// the obvious signal and the wrong one: a consolidated document carries a header naming the
    /// date it was produced, so a pure reissue changes the file while every article stays
    /// identical. The report would then promise a change and hand the reader "0 changed, 0 added,
    /// 0 removed", which is how correct software gets mistaken for broken software.
    ///
    /// Exactly two reads, whatever the span. Walking every version in between cost 1.7s on a
    /// six-year window because a work like the Code de l'environnement has 195 of them, and
    /// answered a question nobody asked: what matters is whether THIS comparison shows anything.
    /// </summary>
    public int DistinctWordings(string work, string from, string to)
        => WordingComparison(work, from, to).Distinct;

    private (int Distinct, bool BothAvailable) WordingComparison(string work, string from, string to)
    {
        var a = WordingDigest(work, from);
        var b = WordingDigest(work, to);
        if (a is null && b is null) return (0, false);
        if (a is null || b is null) return (2, false);
        return (a == b ? 1 : 2, true);
    }

    /// <summary>The ordered provision hashes of the version in force on a date, or null.</summary>
    private string? WordingDigest(string work, string date)
    {
        // Exactly the one version in force, chosen the way as_of chooses it. Joining on the
        // interval alone matched every version whose window contains the date, so the digest
        // became the concatenation of several versions and no two dates ever compared equal.
        using var cmd = Cmd("""
            SELECT p.text_sha FROM provisions p
            WHERE p.rid = (
                SELECT d.rid FROM docs d
                WHERE (d.group_key=$w OR d.group_identifier=$w)
                  AND d.valid_from <= $d AND (d.valid_to IS NULL OR d.valid_to >= $d)
                ORDER BY d.valid_from DESC LIMIT 1)
            ORDER BY p.seq
            """, []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        cmd.Parameters.AddWithValue("$d", date);
        var sb = new System.Text.StringBuilder();
        using var r = cmd.ExecuteReader();
        while (r.Read()) sb.Append(r.GetString(0));
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Totals for a window: how many works moved and how many new versions appeared.</summary>
    public (int Works, int Versions) ChangeTotals(string from, string to,
                                                  IReadOnlyList<string>? kinds,
                                                  FilterSet? filter = null)
    {
        var scoped = (filter ?? FilterSet.All) with
        {
            Kind = kinds is { Count: > 0 } ? string.Join(',', kinds) : filter?.Kind,
        };
        var (where, parameters) = WithFilters(
            "valid_from >= $from AND valid_from <= $to", scoped, excludeAsOf: true);
        // key, not group_key || valid_from: two versions of one work can share a valid_from
        // (the corpus disambiguates them with a key suffix), and a concatenation key collapses
        // exactly those pairs while also gluing "2024-1-1" ambiguities into false matches.
        using var cmd = Cmd($"SELECT COUNT(DISTINCT group_key), COUNT(DISTINCT key) FROM docs WHERE {where}", parameters);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt32(0), r.GetInt32(1)) : (0, 0);
    }

    /// <summary>
    /// Distinct non-withdrawn works in the exact legal metadata scope used by an aggregate.
    /// This is the denominator disclosed beside changes_in_period, not a corpus-wide headline.
    /// </summary>
    public int PopulationTotal(IReadOnlyList<string>? kinds, FilterSet? filter = null)
    {
        var scoped = (filter ?? FilterSet.All) with
        {
            Kind = kinds is { Count: > 0 } ? string.Join(',', kinds) : filter?.Kind,
        };
        var (where, parameters) = WithFilters(
            "1=1", scoped, excludeAsOf: true);
        using var cmd = Cmd(
            $"SELECT COUNT(DISTINCT group_key) FROM docs WHERE {where}", parameters);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// Recomputes the digest the stamp commits to, from what this database actually contains.
    /// Comparing it with the signed value is what turns "signature valid" into "the text you
    /// are reading is the text that was signed" — a signature over metadata alone cannot.
    /// Must stay byte-identical to the builder's construction.
    /// </summary>
    public string ComputeContentDigest()
    {
        if (IsV3)
        {
            using var blobs = Cmd("SELECT text_sha, encoding, original_size, payload FROM text_blobs ORDER BY text_sha", []);
            using var br = blobs.ExecuteReader();
            while (br.Read())
                _ = DecodeAndVerify(br.GetString(1), br.GetInt32(2), (byte[])br.GetValue(3), br.GetString(0));
        }
        var sb = new System.Text.StringBuilder();
        using (var cmd = Cmd($"SELECT key, language, valid_from, valid_to, record_sha FROM docs ORDER BY key, language", []))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                sb.Append(r.GetString(0)).Append('|').Append(r.GetString(1)).Append('|')
                  .Append(r.GetString(2)).Append('|').Append(r.IsDBNull(3) ? "" : r.GetString(3)).Append('|')
                  .Append(r.IsDBNull(4) ? "" : r.GetString(4)).Append('\n');
        using (var cmd = Cmd("SELECT provision_id, text_sha FROM provisions ORDER BY rid, seq", []))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                sb.Append(r.GetString(0)).Append('|').Append(r.GetString(1)).Append('\n');
        if (_workCatalogVersion >= 2)
            IndexBuilder.AppendPublisherMetadataDigest(_conn, sb);
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public List<EventRow> Events(string key) => Events(key, int.MaxValue);

    public List<EventRow> Events(string key, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        using var cmd = Cmd("SELECT key, scope, event, observed_from, detail FROM events WHERE key=$k ORDER BY observed_from LIMIT $lim", []);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<EventRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new EventRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    public List<ObservationRow> Observations(string key, string? language)
        => Observations(key, language, int.MaxValue);

    public List<ObservationRow> Observations(string key, string? language, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        using var cmd = Cmd("""
            SELECT key, language, expr_valid_from, sha256, source_uri, observed_from, observed_to
            FROM obs_history WHERE key=$k AND ($l IS NULL OR language=$l)
            ORDER BY language, expr_valid_from, observed_from LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$l", (object?)language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<ObservationRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new ObservationRow(r.GetString(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public DocRow? ByKey(string key)
    {
        using var cmd = Cmd($"SELECT {SelectDocCols()} FROM docs WHERE key=$k ORDER BY language LIMIT 1", []);
        cmd.Parameters.AddWithValue("$k", key);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadDoc(r) : null;
    }

    /// <summary>
    /// Distinct publisher states effective at <paramref name="date"/> on the latest applicable
    /// axis boundary. Same-boundary publisher identities remain distinct instead of being
    /// silently collapsed by <see cref="AsOf"/>.
    /// </summary>
    public List<DocRow> VersionsEffectiveOn(
        string work, DateOnly date, string? language = null, int limit = 21)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var normalized = NormalizeWork(work);
        using var cmd = Cmd($"""
            WITH eligible AS (
                SELECT d.* FROM docs d
                WHERE (d.group_key=$w OR d.group_identifier=$w)
                  AND d.valid_from <= $d AND (d.valid_to IS NULL OR d.valid_to >= $d)),
            latest AS (SELECT MAX(valid_from) AS valid_from FROM eligible),
            matches AS (
                SELECT d.*, ROW_NUMBER() OVER (PARTITION BY d.key ORDER BY
                    CASE WHEN $l IS NOT NULL AND d.language=$l THEN 0 ELSE 1 END,
                    (SELECT COUNT(*) FROM docs c
                     WHERE c.group_key=d.group_key AND c.language=d.language) DESC,
                    d.language) AS expression_row
                FROM eligible d
                WHERE d.valid_from=(SELECT valid_from FROM latest))
            SELECT {SelectDocCols("m")} FROM matches m
            WHERE m.expression_row=1 ORDER BY m.key LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$w", normalized);
        cmd.Parameters.AddWithValue("$d",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$l", (object?)language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lim", limit);
        return ReadAll(cmd);
    }

    /// <summary>One row per publisher version, with its language expressions nested.</summary>
    public List<TimelineVersionRow> TimelineVersions(string work)
    {
        var normalized = NormalizeWork(work);
        return GroupTimelineVersions(Timeline(normalized), PreferredDocumentLanguage(normalized));
    }

    /// <summary>Resolve an opaque key only among publisher states effective on this boundary.</summary>
    public DocRow? VersionByCoordinate(
        string work, DateOnly date, string versionKey, string? language = null)
    {
        var normalized = NormalizeWork(work);
        using var cmd = Cmd($"""
            WITH eligible AS (
                SELECT d.* FROM docs d
                WHERE (d.group_key=$w OR d.group_identifier=$w)
                  AND d.valid_from <= $d AND (d.valid_to IS NULL OR d.valid_to >= $d)),
            latest AS (SELECT MAX(valid_from) AS valid_from FROM eligible)
            SELECT {SelectDocCols("d")} FROM eligible d
            WHERE d.valid_from=(SELECT valid_from FROM latest)
              AND substr(d.key, length(d.key) - length($v)) = ':' || $v
              AND ($l IS NULL OR d.language=$l)
            ORDER BY (SELECT COUNT(*) FROM docs c
                      WHERE c.group_key=d.group_key AND c.language=d.language) DESC,
                     d.language LIMIT 1
            """, []);
        cmd.Parameters.AddWithValue("$w", normalized);
        cmd.Parameters.AddWithValue("$d",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$v", versionKey);
        cmd.Parameters.AddWithValue("$l", (object?)language ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadDoc(reader) : null;
    }

    /// <summary>Resolve one held version key inside a work using its predominant language.</summary>
    public DocRow? VersionByKey(string work, string key)
    {
        var normalized = NormalizeWork(work);
        using var command = Cmd($"""
            SELECT {SelectDocCols("d")} FROM docs d
            WHERE d.key=$k AND (d.group_key=$w OR d.group_identifier=$w)
            ORDER BY (SELECT COUNT(*) FROM docs c
                      WHERE c.group_key=d.group_key AND c.language=d.language) DESC,
                     d.language LIMIT 1
            """, []);
        command.Parameters.AddWithValue("$w", normalized);
        command.Parameters.AddWithValue("$k", key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDoc(reader) : null;
    }

    public List<DocRow> GroupsPage(int limit, int offset, FilterSet filters)
    {
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: true, alias: "d");
        using var cmd = Cmd($"""
            SELECT {SelectDocCols()} FROM docs d
            WHERE {where} AND valid_from = (
                SELECT MAX(valid_from) FROM docs d2
                WHERE d2.group_key = d.group_key AND d2.withdrawn = 0)
            GROUP BY group_key
            ORDER BY group_key LIMIT $lim OFFSET $off
            """, ps);
        cmd.Parameters.AddWithValue("$lim", limit);
        cmd.Parameters.AddWithValue("$off", offset);
        return ReadAll(cmd);
    }

    /// <summary>
    /// One row of the catalogue: a work, summarised across every version of it that survives the
    /// filters. The page that shows this is answering "what is in here", which is a question about
    /// works, while every other query in this file is about versions.
    /// </summary>
    public (List<CatalogueRow> Rows, int Total) Catalogue(
        FilterSet filters, bool? hasText, CatalogueOrder order, int limit, int offset)
    {
        // Source class is a work-level catalogue facet. A publisher can omit classification on
        // one dated state while earlier states identify the work unambiguously; filtering that
        // work must not truncate its timeline or make its newest state disappear from the count.
        var summaryFilters = filters with { Kind = null };
        var (where, ps) = WithFilters("1=1", summaryFilters, excludeAsOf: false);
        if (filters.Kind is not null)
        {
            where += " AND EXISTS (SELECT 1 FROM docs ck WHERE ck.group_key=docs.group_key"
                   + " AND ck.kind=$catalog_kind AND ck.withdrawn=0)";
            ps.Add(new SqliteParameter("$catalog_kind", filters.Kind));
        }

        // Sparse publisher metadata is resolved per field, newest known value first. Taking every
        // field from the newest row made one absent title erase a title held on all prior states.
        var ctes = $"""
            WITH f AS (SELECT * FROM docs WHERE {where}),
                 agg AS (SELECT group_key, COUNT(DISTINCT key) AS versions, MIN(valid_from) AS first_from,
                                MAX(valid_from) AS last_from,
                                COUNT(DISTINCT CASE WHEN text_public=1 THEN key END) AS text_versions,
                                MAX(text_public) AS has_text,
                                -- When we last SAW a change for this work. valid_from is when a
                                -- law takes effect, which is legitimately in the future for a
                                -- deferred commencement; observed_from is when the record entered
                                -- the corpus, which is the only one of the two that can serve as
                                -- a last-modified time for the page that renders it.
                                MAX(observed_from) AS last_observed
                         FROM f GROUP BY group_key),
                 meta AS (SELECT DISTINCT group_key,
                                  FIRST_VALUE(collection) OVER (
                                      PARTITION BY group_key ORDER BY valid_from DESC, key DESC) AS collection,
                                  FIRST_VALUE(title) OVER (
                                      PARTITION BY group_key
                                      ORDER BY (title IS NULL OR title=''), valid_from DESC, key DESC) AS title,
                                  FIRST_VALUE(title_short) OVER (
                                      PARTITION BY group_key
                                      ORDER BY (title_short IS NULL OR title_short=''), valid_from DESC, key DESC) AS title_short,
                                  FIRST_VALUE(kind) OVER (
                                      PARTITION BY group_key
                                      ORDER BY (kind IS NULL OR kind=''), valid_from DESC, key DESC) AS kind
                           FROM f)
            """;
        var having = hasText switch { true => " AND a.has_text = 1", false => " AND a.has_text = 0", _ => "" };
        var orderBy = order switch
        {
            CatalogueOrder.MostVersions => "a.versions DESC, m.group_key ASC",
            CatalogueOrder.MostRecent => "a.last_from DESC, m.group_key ASC",
            CatalogueOrder.Oldest => "a.first_from ASC, m.group_key ASC",
            _ => "m.group_key ASC",
        };

        using var count = Cmd($"{ctes} SELECT COUNT(*) FROM agg a WHERE 1=1{having}", ps);
        var total = Convert.ToInt32(count.ExecuteScalar());

        using var cmd = Cmd($"""
            {ctes}
            SELECT m.collection, m.group_key, m.title, m.title_short, m.kind,
                   a.versions, a.text_versions, a.first_from, a.last_from, a.has_text, a.last_observed
            FROM agg a JOIN meta m ON m.group_key = a.group_key
            WHERE 1=1{having}
            ORDER BY {orderBy}
            LIMIT $lim OFFSET $off
            """, ps);
        cmd.Parameters.AddWithValue("$lim", limit);
        cmd.Parameters.AddWithValue("$off", offset);

        var rows = new List<CatalogueRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            rows.Add(new CatalogueRow(
                rd.GetString(0), rd.GetString(1),
                rd.IsDBNull(2) ? null : rd.GetString(2),
                rd.IsDBNull(3) ? null : rd.GetString(3),
                rd.IsDBNull(4) ? null : rd.GetString(4),
                rd.GetInt32(5), rd.GetInt32(6), rd.GetString(7), rd.GetString(8), rd.GetInt32(9) == 1,
                rd.IsDBNull(10) ? null : rd.GetString(10)));
        return (rows, total);
    }

    /// <summary>The document types present, with how many WORKS each covers, for a filter list.</summary>
    public List<(string Kind, int Works)> CatalogueKinds(string? collection)
    {
        var ps = new List<SqliteParameter>();
        var where = "kind IS NOT NULL";
        if (collection is not null) { where += " AND collection=$c"; ps.Add(new SqliteParameter("$c", collection)); }
        using var cmd = Cmd($"""
            SELECT kind, COUNT(DISTINCT group_key) AS works FROM docs
            WHERE {where} AND withdrawn=0 GROUP BY kind ORDER BY works DESC
            """, ps);
        var outp = new List<(string, int)>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) outp.Add((rd.GetString(0), rd.GetInt32(1)));
        return outp;
    }

    /// <summary>Cross-references a provision makes, in document order.</summary>
    public List<(string Slug, string Href, string? Label)> CitationsOf(
        string rid,
        string anchor,
        int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        using var cmd = Cmd("SELECT cited_slug, href, label FROM citations WHERE rid=$r AND anchor=$a ORDER BY rowid LIMIT $lim", []);
        cmd.Parameters.AddWithValue("$r", rid);
        cmd.Parameters.AddWithValue("$a", anchor);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<(string, string, string?)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>
    /// The reverse: which articles point AT this work. This is the question legal research is
    /// actually made of, and it is only answerable because the publisher's own cross-references
    /// were captured at derive time rather than thrown away with the rest of the markup.
    /// </summary>
    public List<(string GroupKey, string VersionKey, string ValidFrom, string Anchor,
        string? Num, string? Title)> CitedBy(
        string targetWork, int limit, IReadOnlyList<string>? hrefFragments = null)
    {
        var colon = targetWork.IndexOf(':');
        var slug = colon >= 0 ? targetWork[(colon + 1)..] : targetWork;
        var fragments = hrefFragments ?? [];
        var hrefMatch = string.Concat(fragments.Select((_, index) =>
            $" OR instr(c.href, $fragment{index} || '/') > 0 OR substr(c.href, -length($fragment{index})) = $fragment{index}"));
        using var cmd = Cmd($"""
            SELECT DISTINCT d.group_key, d.key, d.valid_from, c.anchor, p.num,
                   COALESCE(d.title_short, d.title)
            FROM citations c
            JOIN docs d ON d.rid = c.rid
            LEFT JOIN provisions p ON p.rid = c.rid AND p.anchor = c.anchor
            WHERE (c.cited_slug = $target OR c.cited_slug = $s{hrefMatch}) AND d.withdrawn = 0
            ORDER BY d.valid_from DESC, d.group_key, c.anchor
            LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$s", slug);
        cmd.Parameters.AddWithValue("$target", targetWork);
        for (var index = 0; index < fragments.Count; index++)
            cmd.Parameters.AddWithValue($"$fragment{index}", fragments[index]);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<(string, string, string, string, string?, string?)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.GetString(2),
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    public CoverageInfo Coverage(int maximumFacetRows = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFacetRows);
        int CountGroups(string column)
        {
            using var command = Cmd(
                $"SELECT COUNT(*) FROM (SELECT {column} FROM docs WHERE withdrawn=0 GROUP BY {column})",
                []);
            return Convert.ToInt32(command.ExecuteScalar());
        }
        var totalKinds = CountGroups("kind");
        int CountProfiles()
        {
            using var command = Cmd(
                "SELECT COUNT(*) FROM (SELECT profile FROM docs WHERE withdrawn=0 AND profile IS NOT NULL GROUP BY profile)",
                []);
            return Convert.ToInt32(command.ExecuteScalar());
        }
        var totalProfiles = CountProfiles();
        var totalLanguages = CountGroups("language");
        var kinds = new List<CoverageKind>();
        using (var cmd = Cmd("""
            SELECT kind, COUNT(DISTINCT key),
                   COUNT(DISTINCT CASE WHEN text_public=1 THEN key END)
            FROM docs WHERE withdrawn=0 GROUP BY kind ORDER BY COUNT(DISTINCT key) DESC LIMIT $lim
            """, []))
        {
            cmd.Parameters.AddWithValue("$lim", maximumFacetRows);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                kinds.Add(new CoverageKind(r.IsDBNull(0) ? null : r.GetString(0),
                    r.GetInt32(1), r.IsDBNull(2) ? 0 : r.GetInt32(2)));
        }

        // lex-index/2: text_public is set only when a derived (provision-bearing) version exists.
        // key is the version-level lex_id in both supported schemas, so COUNT(DISTINCT key) is
        // the true dated-version count; COUNT(*) stays the expression-row count.
        using var agg = Cmd("""
            SELECT COUNT(DISTINCT group_key), COUNT(*), MIN(valid_from), MAX(valid_from),
                   SUM(CASE WHEN text_public=1 THEN 1 ELSE 0 END),
                   COUNT(DISTINCT key),
                   COUNT(DISTINCT CASE WHEN text_public=1 THEN key END)
            FROM docs WHERE withdrawn=0
            """, []);
        using var ar = agg.ExecuteReader();
        ar.Read();
        var profiles = new List<CoverageProfile>();
        using (var pc = Cmd("""
            SELECT profile, COUNT(DISTINCT key) FROM docs WHERE profile IS NOT NULL AND withdrawn=0
            GROUP BY profile ORDER BY COUNT(DISTINCT key) DESC LIMIT $lim
            """, []))
        {
            pc.Parameters.AddWithValue("$lim", maximumFacetRows);
            using var pr = pc.ExecuteReader();
            while (pr.Read()) profiles.Add(new CoverageProfile(pr.GetString(0), pr.GetInt32(1)));
        }

        // Which languages the corpus is actually in. This decides a real design question and was
        // being answered by guesswork: a site-wide language picker only makes sense if the same
        // law exists in several languages, and here it almost never does. Luxembourg publishes in
        // French, the EU acts held here are English, and a reader who picked one would lose the
        // other entirely rather than see a translation.
        var languages = new List<CoverageLanguage>();
        using (var lc = Cmd("""
            SELECT language, COUNT(DISTINCT group_key), COUNT(*) FROM docs WHERE withdrawn=0
            GROUP BY language ORDER BY COUNT(*) DESC LIMIT $lim
            """, []))
        {
            lc.Parameters.AddWithValue("$lim", maximumFacetRows);
            using var lr = lc.ExecuteReader();
            while (lr.Read()) languages.Add(new CoverageLanguage(lr.GetString(0), lr.GetInt32(1), lr.GetInt32(2)));
        }

        int multilingual;
        using (var mc = Cmd("""
            SELECT COUNT(*) FROM (
              SELECT group_key FROM docs WHERE withdrawn=0
              GROUP BY group_key HAVING COUNT(DISTINCT language) > 1)
            """, []))
            multilingual = Convert.ToInt32(mc.ExecuteScalar());

        return new CoverageInfo(Collection, ar.GetInt32(0), ar.GetInt32(1),
            ar.IsDBNull(2) ? null : ar.GetString(2), ar.IsDBNull(3) ? null : ar.GetString(3), kinds, Stamp,
            ar.IsDBNull(4) ? 0 : ar.GetInt32(4), profiles, languages, multilingual,
            _expectedWorks, _buildIssues, totalKinds, totalProfiles, totalLanguages,
            ar.GetInt32(5), ar.GetInt32(6));
    }

    private static string NormalizeWork(string work)
    {
        // Accepted forms (§9): work-level lex_id "<collection>:<groupkey>", version-level lex_id
        // "<collection>:<groupkey>:<vkey>" (version segment ignored), or a verbatim identifier/slug.
        if (!work.Contains("://") && work.Contains(':'))
        {
            var parts = work.Split(':');
            if (parts.Length >= 2) work = parts[1];
        }
        // EUR-Lex presents CELEX identifiers with an uppercase document-form letter while the
        // canonical corpus slugs are normalized to lowercase. Official copied identifiers and
        // canonical permalinks must resolve to the same work.
        return System.Text.RegularExpressions.Regex.IsMatch(work, @"^\d{5}[A-Za-z]\d{4}")
            ? work.ToLowerInvariant() : work;
    }

    private (string Sql, List<SqliteParameter> Ps) WithFilters(
        string baseSql, FilterSet f, bool excludeAsOf, string? alias = null)
    {
        var ps = new List<SqliteParameter>();
        string Column(string name) => alias is null ? name : $"{alias}.{name}";
        // Withdrawn publisher records remain addressable by exact provenance tools, but they
        // are not eligible public-search or catalogue candidates after their tombstone.
        var sql = baseSql + $" AND {Column("withdrawn")}=0";
        if (f.Collection is not null) { sql += $" AND {Column("collection")}=$fcol"; ps.Add(new SqliteParameter("$fcol", f.Collection)); }
        if (f.Kind is not null)
        {
            var kinds = f.Kind.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var negate = kinds.Length > 0 && kinds.All(k => k.StartsWith('!'));
            if (kinds.Length > 0)
            {
                var names = kinds.Select((_, i) => $"$fkind{i}").ToList();
                sql += negate
                    ? $" AND ({Column("kind")} IS NULL OR {Column("kind")} NOT IN ({string.Join(',', names)}))"
                    : $" AND {Column("kind")} IN ({string.Join(',', names)})";
                for (var i = 0; i < kinds.Length; i++)
                    ps.Add(new SqliteParameter($"$fkind{i}", kinds[i].TrimStart('!')));
            }
        }
        if (f.Language is not null) { sql += $" AND {Column("language")}=$flang"; ps.Add(new SqliteParameter("$flang", f.Language)); }
        if (f.Hierarchy is not null || f.ActForm is not null || f.BindingStatus is not null
            || f.Domain is not null || f.DocumentRole is not null)
        {
            if (!IsV3) return (sql + " AND 0=1", ps);
            if (f.Hierarchy is not null) { sql += $" AND {Column("hierarchy")}=$fhier"; ps.Add(new SqliteParameter("$fhier", f.Hierarchy)); }
            if (f.ActForm is not null) { sql += $" AND {Column("act_form")}=$fform"; ps.Add(new SqliteParameter("$fform", f.ActForm)); }
            if (f.BindingStatus is not null) { sql += $" AND {Column("binding_status")}=$fbind"; ps.Add(new SqliteParameter("$fbind", f.BindingStatus)); }
            if (f.Domain is not null) { sql += $" AND ('|' || {Column("domains")} || '|') LIKE $fdomain"; ps.Add(new SqliteParameter("$fdomain", "%|" + f.Domain + "|%")); }
            if (f.DocumentRole is not null)
            {
                if (_workCatalogVersion < 2) return (sql + " AND 0=1", ps);
                var outerRid = alias is null ? "docs.rid" : $"{alias}.rid";
                sql += $" AND EXISTS (SELECT 1 FROM document_roles dr WHERE dr.rid={outerRid} AND dr.role=$frole)";
                ps.Add(new SqliteParameter("$frole", f.DocumentRole));
            }
        }
        if (f.Works is { Count: > 0 } works)
        {
            // Matches either the slug or the publisher's own identifier, so a caller can scope
            // with whichever of the two it happens to be holding.
            var names = works.Select((_, i) => $"$fw{i}").ToList();
            sql += $" AND ({Column("group_key")} IN ({string.Join(",", names)}) OR {Column("group_identifier")} IN ({string.Join(",", names)}))";
            for (var i = 0; i < works.Count; i++) ps.Add(new SqliteParameter($"$fw{i}", works[i]));
        }
        if (f.PublisherMetadataIdentifier is { } metadataIdentifier)
        {
            if (_workCatalogVersion < 2) return (sql + " AND 0=1", ps);
            var outerGroup = Column("group_key");
            var outerLanguage = Column("language");
            var outerFrom = Column("valid_from");
            sql += $"""
                 AND EXISTS (
                   SELECT 1 FROM work_records wr
                   JOIN work_publisher_metadata pm ON pm.work_id=wr.work_id
                   WHERE wr.group_key={outerGroup} AND wr.language={outerLanguage}
                     AND pm.identifier=$fpublishermetadata AND pm.valid_from={outerFrom}
                     AND pm.citation_identity=0)
                """;
            ps.Add(new SqliteParameter("$fpublishermetadata", metadataIdentifier));
        }
        if (!excludeAsOf && f.AsOf is { } d)
        {
            sql += $" AND {Column("valid_from")} <= $fasof AND ({Column("valid_to")} IS NULL OR {Column("valid_to")} >= $fasof)";
            ps.Add(new SqliteParameter("$fasof", d.ToString("yyyy-MM-dd")));
        }
        return (sql, ps);
    }

    private SqliteCommand Cmd(string sql, List<SqliteParameter> ps)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in ps) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
        return cmd;
    }

    private static string Fts5Escape(string q) => string.Join(" ",
        System.Text.RegularExpressions.Regex.Matches(q, "\"[^\"]+\"|\\S+")
            .Select(m => "\"" + m.Value.Trim('"').Replace("\"", "") + "\""));

    private static List<DocRow> ReadAll(SqliteCommand cmd)
    {
        var list = new List<DocRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadDoc(r));
        return list;
    }

    private static DocRow ReadDoc(SqliteDataReader r) => new(
        Key: r.GetString(0), Collection: r.GetString(1), GroupKey: r.GetString(2), GroupIdentifier: r.GetString(3),
        Kind: r.IsDBNull(4) ? null : r.GetString(4), Language: r.GetString(5),
        ValidFrom: r.GetString(6), ValidTo: r.IsDBNull(7) ? null : r.GetString(7),
        ValidTimeSource: r.GetString(8), ObservedFrom: r.GetString(9),
        Withdrawn: r.GetString(10) == "1" || (r.GetValue(10) is long l1 && l1 == 1),
        TextAvailable: r.GetString(11) == "1" || (r.GetValue(11) is long l2 && l2 == 1),
        TextPublic: r.GetString(12) == "1" || (r.GetValue(12) is long l3 && l3 == 1),
        RecordSha: r.IsDBNull(13) ? null : r.GetString(13), BodySha: r.IsDBNull(14) ? null : r.GetString(14),
        SourceUri: r.IsDBNull(15) ? null : r.GetString(15), Title: r.IsDBNull(16) ? null : r.GetString(16),
        TitleShort: r.IsDBNull(17) ? null : r.GetString(17), Body: null,
        PublicationDate: r.IsDBNull(18) ? null : r.GetString(18), StatusNote: r.IsDBNull(19) ? null : r.GetString(19),
        Profile: r.FieldCount > 21 && !r.IsDBNull(21) ? r.GetString(21) : null,
        Hierarchy: r.FieldCount > 22 && !r.IsDBNull(22) ? r.GetString(22) : null,
        Domains: r.FieldCount > 23 && !r.IsDBNull(23) ? r.GetString(23) : null,
        ActForm: r.FieldCount > 24 && !r.IsDBNull(24) ? r.GetString(24) : null,
        BindingStatus: r.FieldCount > 25 && !r.IsDBNull(25) ? r.GetString(25) : null,
        ConsolidationStatus: r.FieldCount > 26 && !r.IsDBNull(26) ? r.GetString(26) : null);

    /// <summary>Rid of a doc row (key|language|valid_from) — the provisions foreign key.</summary>
    public static string RidOf(DocRow d) => $"{d.Key}|{d.Language}|{d.ValidFrom}";

    /// <summary>All provisions of one document version, in document order.</summary>
    public List<ProvisionRow> Provisions(string rid) => ProvisionsCore(rid, null);

    /// <summary>At most <paramref name="limit"/> provisions, in document order.</summary>
    public List<ProvisionRow> Provisions(string rid, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        return ProvisionsCore(rid, limit);
    }

    public int ProvisionCount(string rid)
    {
        using var cmd = Cmd("SELECT COUNT(*) FROM provisions WHERE rid=$rid", []);
        cmd.Parameters.AddWithValue("$rid", rid);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<ProvisionRow> ProvisionOutlines(string rid, int limit)
        => ProvisionMetadata(rid, limit, null);

    public ProvisionRow? ProvisionOutline(string rid, string anchor)
        => ProvisionMetadata(rid, 1, [anchor]).FirstOrDefault();

    public List<ProvisionRow> ProvisionOutlinesByAnchors(
        string rid,
        IReadOnlyCollection<string> anchors)
    {
        if (anchors.Count == 0) return [];
        if (anchors.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(anchors), "At most 50 anchors are allowed.");
        return ProvisionMetadata(rid, anchors.Count, anchors);
    }

    public List<ProvisionRow> ProvisionsForMcp(
        string rid,
        int limit,
        int maximumTextBytes)
        => LoadBoundedProvisionText(ProvisionMetadata(rid, limit, null), maximumTextBytes);

    public List<ProvisionRow> ProvisionsByAnchorsForMcp(
        string rid,
        IReadOnlyCollection<string> anchors,
        int maximumTextBytes)
    {
        if (anchors.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(anchors), "At most 50 anchors are allowed.");
        return LoadBoundedProvisionText(
            ProvisionMetadata(rid, Math.Max(1, anchors.Count), anchors), maximumTextBytes);
    }

    private List<ProvisionRow> ProvisionMetadata(
        string rid,
        int limit,
        IReadOnlyCollection<string>? anchors)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var names = anchors?.Select((_, index) => $"$a{index}").ToArray() ?? [];
        var anchorWhere = names.Length == 0 ? "" : $" AND p.anchor IN ({string.Join(',', names)})";
        using var cmd = Cmd(IsV3 ? $"""
            SELECT p.rid,p.seq,p.anchor,p.provision_id,p.ptype,p.num,p.heading,p.path,
                   p.article_valid_from,p.work_title,p.text_sha,b.original_size,NULL
            FROM provisions p JOIN text_blobs b ON b.text_sha=p.text_sha
            WHERE p.rid=$rid{anchorWhere} ORDER BY p.seq LIMIT $lim
            """ : $"""
            SELECT p.rid,p.seq,p.anchor,p.provision_id,p.ptype,p.num,p.heading,p.path,
                   p.article_valid_from,p.work_title,p.text_sha,
                   length(CAST(p.text_md AS BLOB)),length(p.text_md)
            FROM provisions p WHERE p.rid=$rid{anchorWhere} ORDER BY p.seq LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$rid", rid);
        cmd.Parameters.AddWithValue("$lim", limit);
        if (anchors is not null)
        {
            var index = 0;
            foreach (var anchor in anchors)
                cmd.Parameters.AddWithValue(names[index++], anchor);
        }
        var rows = new List<ProvisionRow>(Math.Min(limit, anchors?.Count ?? limit));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(new ProvisionRow(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), "", reader.GetString(10),
                StoredTextBytes: reader.GetInt32(11),
                StoredTextCharacters: reader.IsDBNull(12) ? null : reader.GetInt32(12),
                TextLoaded: false));
        return rows;
    }

    private List<ProvisionRow> LoadBoundedProvisionText(
        List<ProvisionRow> rows,
        int maximumTextBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTextBytes);
        var remaining = maximumTextBytes;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var size = row.StoredTextBytes ?? int.MaxValue;
            if (size > remaining) continue;
            string text;
            if (IsV3)
            {
                using var command = Cmd(
                    "SELECT encoding,original_size,payload FROM text_blobs WHERE text_sha=$sha LIMIT 1", []);
                command.Parameters.AddWithValue("$sha", row.TextSha);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidDataException($"Missing text blob '{row.TextSha}'.");
                text = DecodeAndVerify(reader.GetString(0), reader.GetInt32(1),
                    (byte[])reader.GetValue(2), row.TextSha);
            }
            else
            {
                using var command = Cmd(
                    "SELECT text_md FROM provisions WHERE rid=$rid AND anchor=$anchor LIMIT 1", []);
                command.Parameters.AddWithValue("$rid", row.Rid);
                command.Parameters.AddWithValue("$anchor", row.Anchor);
                text = command.ExecuteScalar() as string
                    ?? throw new InvalidDataException($"Missing provision text '{row.ProvisionId}'.");
            }
            remaining -= size;
            rows[index] = row with
            {
                TextMd = text,
                StoredTextCharacters = text.Length,
                TextLoaded = true,
            };
        }
        return rows;
    }

    private List<ProvisionRow> ProvisionsCore(string rid, int? limit)
    {
        if (IsV3) return ProvisionsV3(rid, limit);
        using var cmd = Cmd("""
            SELECT rid, seq, anchor, provision_id, ptype, num, heading, path, article_valid_from,
                   work_title, text_md, text_sha
            FROM provisions WHERE rid=$rid ORDER BY seq LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$rid", rid);
        cmd.Parameters.AddWithValue("$lim", limit ?? int.MaxValue);
        var list = new List<ProvisionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new ProvisionRow(
            Rid: r.GetString(0), Seq: r.GetInt32(1), Anchor: r.GetString(2), ProvisionId: r.GetString(3),
            PType: r.GetString(4), Num: r.IsDBNull(5) ? null : r.GetString(5),
            Heading: r.IsDBNull(6) ? null : r.GetString(6), Path: r.IsDBNull(7) ? null : r.GetString(7),
            ArticleValidFrom: r.IsDBNull(8) ? null : r.GetString(8),
            WorkTitle: r.IsDBNull(9) ? null : r.GetString(9),
            TextMd: r.GetString(10), TextSha: r.GetString(11)));
        return list;
    }

    private List<ProvisionRow> ProvisionsV3(string rid, int? limit)
    {
        using var cmd = Cmd("""
            SELECT p.rid, p.seq, p.anchor, p.provision_id, p.ptype, p.num, p.heading, p.path,
                   p.article_valid_from, p.work_title, p.text_sha,
                   b.encoding, b.original_size, b.payload
            FROM provisions p JOIN text_blobs b ON b.text_sha=p.text_sha
            WHERE p.rid=$rid ORDER BY p.seq LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$rid", rid);
        cmd.Parameters.AddWithValue("$lim", limit ?? int.MaxValue);
        var list = new List<ProvisionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var text = DecodeAndVerify(r.GetString(11), r.GetInt32(12), (byte[])r.GetValue(13), r.GetString(10));
            list.Add(new ProvisionRow(
                r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9), text, r.GetString(10)));
        }
        return list;
    }

    /// <summary>
    /// A text window around the first matched term, cut in application code.
    ///
    /// <para>The provision index is <c>fts5(..., content='')</c>. A contentless FTS5 table stores no
    /// text to cut from, so SQLite's <c>snippet()</c> returns NULL on every row: every hit this
    /// server has ever returned carried an empty snippet, for both publishers, whatever their text
    /// coverage. External content is not an option either, because the wording is stored once,
    /// content-addressed and Brotli-encoded in <c>text_blobs</c> (D53), which FTS5 cannot read.</para>
    ///
    /// <para>Terms are located on a diacritics-folded copy that carries a map back to the original
    /// offsets, so the window is cut from the publisher's bytes rather than from the folded form,
    /// and a reader searching "etablissements" still gets a snippet from text spelling it
    /// "établissements". Returns null when the provision holds no text, which is the honest answer
    /// for the versions whose extraction produced nothing.</para>
    ///
    /// <para>Cost, measured on a 9 KB provision: 2.2 ms per call, 88 ms for a full 40-row page,
    /// dominated by the Brotli decode. Callers must therefore ask only for the rows they actually
    /// return, never for the oversampled candidate set, which is six times larger. The default
    /// limit of 10 costs about 22 ms against a 456 ms p95 and a 15 s release gate.</para>
    /// </summary>
    public string? SnippetFor(string textSha, string query, int window = 240)
    {
        ArgumentNullException.ThrowIfNull(textSha);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window);

        string text;
        try
        {
            using var cmd = Cmd(
                "SELECT encoding, original_size, payload FROM text_blobs WHERE text_sha=$sha LIMIT 1", []);
            cmd.Parameters.AddWithValue("$sha", textSha);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            text = DecodeAndVerify(
                reader.GetString(0), reader.GetInt32(1), (byte[])reader.GetValue(2), textSha);
        }
        catch (InvalidDataException) { return null; }
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Fold in step with the original so a match position can be mapped back.
        var folded = new StringBuilder(text.Length);
        var origin = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            foreach (var ch in text[i].ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                folded.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
                origin.Add(i);
            }
        }
        var hay = folded.ToString();

        var terms = WorkSearch.Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var at = -1;
        foreach (var term in terms)
        {
            var from = 0;
            while (from <= hay.Length - term.Length)
            {
                var found = hay.IndexOf(term, from, StringComparison.Ordinal);
                if (found < 0) break;
                var startsWord = found == 0 || hay[found - 1] == ' ';
                if (startsWord && (at < 0 || found < at)) { at = found; break; }
                from = found + 1;
            }
        }

        // No term in the body: the match came from the title, number or heading, which the caller
        // already shows. Open with the provision's own first words rather than inventing relevance.
        var centre = at < 0 ? 0 : origin[at];
        var start = Math.Max(0, centre - window / 3);
        var end = Math.Min(text.Length, start + window);
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;

        var cut = string.Join(' ', text[start..end]
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cut.Length == 0) return null;
        return (start > 0 ? "... " : "") + cut + (end < text.Length ? " ..." : "");
    }

    private static string DecodeAndVerify(string encoding, int originalSize, byte[] payload, string expectedSha)
    {
        byte[] utf8;
        if (encoding == "raw") utf8 = payload;
        else if (encoding == "br4")
        {
            using var input = new MemoryStream(payload);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(originalSize);
            brotli.CopyTo(output);
            utf8 = output.ToArray();
        }
        else throw new InvalidDataException($"Unknown text blob encoding '{encoding}'.");

        if (utf8.Length != originalSize || !Convert.ToHexStringLower(SHA256.HashData(utf8)).Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Text blob '{expectedSha}' failed its size or SHA-256 check.");
        return Encoding.UTF8.GetString(utf8);
    }

    private static string MakeSnippet(string text, string query)
    {
        var term = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var at = term.Length == 0 ? 0 : text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = 0;
        var start = Math.Max(0, at - 60);
        var length = Math.Min(180, text.Length - start);
        return (start > 0 ? "... " : "") + text.Substring(start, length) + (start + length < text.Length ? " ..." : "");
    }

    /// <summary>One provision of one document version, by anchor.</summary>
    public ProvisionRow? Provision(string rid, string anchor)
    {
        var rows = ProvisionsByAnchors(rid, [anchor]);
        return rows.Count == 0 ? null : rows[0];
    }

    public List<ProvisionRow> ProvisionsByAnchors(
        string rid,
        IReadOnlyCollection<string> anchors)
    {
        if (anchors.Count == 0) return [];
        if (anchors.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(anchors), "At most 50 anchors are allowed.");
        var names = anchors.Select((_, index) => $"$a{index}").ToArray();
        using var cmd = Cmd(IsV3 ? $"""
            SELECT p.rid, p.seq, p.anchor, p.provision_id, p.ptype, p.num, p.heading, p.path,
                   p.article_valid_from, p.work_title, p.text_sha,
                   b.encoding, b.original_size, b.payload
            FROM provisions p JOIN text_blobs b ON b.text_sha=p.text_sha
            WHERE p.rid=$rid AND p.anchor IN ({string.Join(',', names)}) ORDER BY p.seq
            """ : $"""
            SELECT rid, seq, anchor, provision_id, ptype, num, heading, path, article_valid_from,
                   work_title, text_md, text_sha
            FROM provisions WHERE rid=$rid AND anchor IN ({string.Join(',', names)}) ORDER BY seq
            """, []);
        cmd.Parameters.AddWithValue("$rid", rid);
        var index = 0;
        foreach (var anchor in anchors)
            cmd.Parameters.AddWithValue(names[index++], anchor);
        var list = new List<ProvisionRow>(anchors.Count);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (IsV3)
            {
                var text = DecodeAndVerify(reader.GetString(11), reader.GetInt32(12),
                    (byte[])reader.GetValue(13), reader.GetString(10));
                list.Add(new ProvisionRow(
                    reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9), text, reader.GetString(10)));
            }
            else
            {
                list.Add(new ProvisionRow(
                    reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10),
                    reader.GetString(11)));
            }
        }
        return list;
    }

    public List<string> ProvisionAnchors(string rid, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        using var cmd = Cmd(
            "SELECT anchor FROM provisions WHERE rid=$rid ORDER BY seq LIMIT $lim", []);
        cmd.Parameters.AddWithValue("$rid", rid);
        cmd.Parameters.AddWithValue("$lim", limit);
        var anchors = new List<string>(limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) anchors.Add(reader.GetString(0));
        return anchors;
    }

    /// <summary>Every distinct text a provision has had, as validity intervals (the time axis).</summary>
    public List<ProvisionStateRow> ProvisionStates(
        string work,
        string anchor,
        string? language = null,
        int limit = int.MaxValue,
        string? windowFrom = null,
        string? windowTo = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var normalizedWork = NormalizeWork(work);
        if (IsV3) language ??= PreferredHistoryLanguage(normalizedWork, anchor);
        using var cmd = Cmd(IsV3 ? """
            SELECT group_key, language, is_primary_language, anchor, valid_from, valid_to, text_sha, in_version,
                   article_valid_from, validity_conflict
            FROM provision_states
            WHERE group_key=$w AND anchor=$a AND language=$lang
              AND ($to IS NULL OR valid_from <= $to)
              AND ($from IS NULL OR valid_to IS NULL OR valid_to >= $from)
            ORDER BY valid_from LIMIT $lim
            """ : """
            SELECT group_key, anchor, valid_from, valid_to, text_sha, in_version,
                   article_valid_from, validity_conflict
            FROM provision_states WHERE group_key=$w AND anchor=$a
              AND ($to IS NULL OR valid_from <= $to)
              AND ($from IS NULL OR valid_to IS NULL OR valid_to >= $from)
            ORDER BY valid_from LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$w", normalizedWork);
        cmd.Parameters.AddWithValue("$a", anchor);
        if (IsV3) cmd.Parameters.AddWithValue("$lang", language ?? "und");
        cmd.Parameters.AddWithValue("$from", (object?)windowFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to", (object?)windowTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<ProvisionStateRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var offset = IsV3 ? 2 : 0;
            list.Add(new ProvisionStateRow(
                r.GetString(0), IsV3 ? r.GetString(1) : "und",
                !IsV3 || r.GetInt64(2) == 1, r.GetString(1 + offset),
                r.GetString(2 + offset), r.IsDBNull(3 + offset) ? null : r.GetString(3 + offset),
                r.GetString(4 + offset), r.IsDBNull(5 + offset) ? null : r.GetString(5 + offset),
                r.IsDBNull(6 + offset) ? null : r.GetString(6 + offset),
                r.GetString(7 + offset) == "1" || (r.GetValue(7 + offset) is long l && l == 1)));
        }
        return list;
    }

    public int ProvisionStateCount(string work, string anchor, string? language = null,
        string? windowFrom = null, string? windowTo = null)
    {
        var normalizedWork = NormalizeWork(work);
        if (IsV3) language ??= PreferredHistoryLanguage(normalizedWork, anchor);
        using var cmd = Cmd(IsV3
            ? "SELECT COUNT(*) FROM provision_states WHERE group_key=$w AND anchor=$a AND language=$lang "
              + "AND ($to IS NULL OR valid_from <= $to) "
              + "AND ($from IS NULL OR valid_to IS NULL OR valid_to >= $from)"
            : "SELECT COUNT(*) FROM provision_states WHERE group_key=$w AND anchor=$a "
              + "AND ($to IS NULL OR valid_from <= $to) "
              + "AND ($from IS NULL OR valid_to IS NULL OR valid_to >= $from)", []);
        cmd.Parameters.AddWithValue("$w", normalizedWork);
        cmd.Parameters.AddWithValue("$a", anchor);
        if (IsV3) cmd.Parameters.AddWithValue("$lang", language ?? "und");
        cmd.Parameters.AddWithValue("$from", (object?)windowFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to", (object?)windowTo ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Anchor lifecycle events for a work; optionally only those touching one anchor.</summary>
    public List<AnchorEventRow> AnchorEvents(
        string work,
        string? anchor = null,
        string? language = null,
        int limit = int.MaxValue,
        string? windowFrom = null,
        string? windowTo = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var normalizedWork = NormalizeWork(work);
        if (IsV3) language ??= PreferredHistoryLanguage(normalizedWork, anchor);
        using var cmd = Cmd(IsV3 ? """
            SELECT e.group_key, e.language, e.is_primary_language, e.etype, e.from_anchor,
                   e.to_anchor, e.anchor, e.text_sha, e.at_version
            FROM anchor_events e JOIN docs d ON d.key=e.at_version AND d.language=e.language
            WHERE e.group_key=$w AND e.language=$lang
              AND ($a IS NULL OR e.from_anchor=$a OR e.to_anchor=$a OR e.anchor=$a)
              AND ($from IS NULL OR d.valid_from >= $from)
              AND ($to IS NULL OR d.valid_from <= $to)
            ORDER BY e.at_version LIMIT $lim
            """ : """
            SELECT e.group_key, e.etype, e.from_anchor, e.to_anchor, e.anchor, e.text_sha, e.at_version
            FROM anchor_events e JOIN docs d ON d.key=e.at_version
            WHERE e.group_key=$w
              AND ($a IS NULL OR e.from_anchor=$a OR e.to_anchor=$a OR e.anchor=$a)
              AND ($from IS NULL OR d.valid_from >= $from)
              AND ($to IS NULL OR d.valid_from <= $to)
            ORDER BY e.at_version LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$w", normalizedWork);
        cmd.Parameters.AddWithValue("$a", (object?)anchor ?? DBNull.Value);
        if (IsV3) cmd.Parameters.AddWithValue("$lang", language ?? "und");
        cmd.Parameters.AddWithValue("$from", (object?)windowFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to", (object?)windowTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<AnchorEventRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var offset = IsV3 ? 2 : 0;
            list.Add(new AnchorEventRow(
                r.GetString(0), IsV3 ? r.GetString(1) : "und",
                !IsV3 || r.GetInt64(2) == 1, r.GetString(1 + offset),
                r.IsDBNull(2 + offset) ? null : r.GetString(2 + offset),
                r.IsDBNull(3 + offset) ? null : r.GetString(3 + offset),
                r.IsDBNull(4 + offset) ? null : r.GetString(4 + offset),
                r.IsDBNull(5 + offset) ? null : r.GetString(5 + offset),
                r.IsDBNull(6 + offset) ? null : r.GetString(6 + offset)));
        }
        return list;
    }

    public int AnchorEventCount(string work, string? anchor = null, string? language = null,
        string? windowFrom = null, string? windowTo = null)
    {
        var normalizedWork = NormalizeWork(work);
        if (IsV3) language ??= PreferredHistoryLanguage(normalizedWork, anchor);
        using var cmd = Cmd(IsV3 ? """
            SELECT COUNT(*) FROM anchor_events e
            JOIN docs d ON d.key=e.at_version AND d.language=e.language
            WHERE e.group_key=$w AND e.language=$lang
              AND ($a IS NULL OR e.from_anchor=$a OR e.to_anchor=$a OR e.anchor=$a)
              AND ($from IS NULL OR d.valid_from >= $from)
              AND ($to IS NULL OR d.valid_from <= $to)
            """ : """
            SELECT COUNT(*) FROM anchor_events e JOIN docs d ON d.key=e.at_version
            WHERE e.group_key=$w
              AND ($a IS NULL OR e.from_anchor=$a OR e.to_anchor=$a OR e.anchor=$a)
              AND ($from IS NULL OR d.valid_from >= $from)
              AND ($to IS NULL OR d.valid_from <= $to)
            """, []);
        cmd.Parameters.AddWithValue("$w", normalizedWork);
        cmd.Parameters.AddWithValue("$a", (object?)anchor ?? DBNull.Value);
        if (IsV3) cmd.Parameters.AddWithValue("$lang", language ?? "und");
        cmd.Parameters.AddWithValue("$from", (object?)windowFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to", (object?)windowTo ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private string? PreferredHistoryLanguage(string work, string? anchor)
    {
        using var cmd = Cmd("""
            SELECT language
            FROM provision_states
            WHERE group_key=$w AND ($a IS NULL OR anchor=$a)
            ORDER BY is_primary_language DESC, language
            LIMIT 1
            """, []);
        cmd.Parameters.AddWithValue("$w", work);
        cmd.Parameters.AddWithValue("$a", (object?)anchor ?? DBNull.Value);
        var language = cmd.ExecuteScalar() as string;
        if (language is not null) return language;

        using var eventCommand = Cmd("""
            SELECT language
            FROM anchor_events
            WHERE group_key=$w
              AND ($a IS NULL OR from_anchor=$a OR to_anchor=$a OR anchor=$a)
            ORDER BY is_primary_language DESC, language
            LIMIT 1
            """, []);
        eventCommand.Parameters.AddWithValue("$w", work);
        eventCommand.Parameters.AddWithValue("$a", (object?)anchor ?? DBNull.Value);
        return eventCommand.ExecuteScalar() as string;
    }

    /// <summary>True if the work has any provision-level history rows.</summary>
    public bool HasProvisionHistory(string work)
    {
        using var cmd = Cmd("SELECT 1 FROM provision_states WHERE group_key=$w LIMIT 1", []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>Document text reconstructed from its authoritative provision occurrences.</summary>
    public string? BuildBody(DocRow d)
    {
        var provs = Provisions(RidOf(d));
        if (provs.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        string? lastPath = null;
        foreach (var p in provs)
        {
            if (p.Path is not null && p.Path != lastPath)
            {
                sb.Append("\n## ").Append(p.Path).Append('\n');
                lastPath = p.Path;
            }
            var title = p.Num is null && p.Heading is null ? p.Anchor
                : string.Join(" — ", new[] { p.Num, p.Heading }.Where(s => !string.IsNullOrEmpty(s)));
            sb.Append("\n### ").Append(title).Append("\n\n").Append(p.TextMd).Append('\n');
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_ownsVectors) _vectors?.Dispose();
        _conn.Dispose();
    }
}
