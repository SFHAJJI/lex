using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Lex.Index;

/// <summary>
/// Builds one index file per collection. Time enters as an injected parameter (F9);
/// no ambient clock is read anywhere in this class.
/// </summary>
public static class IndexBuilder
{
    public const string SchemaVersion = "lex-index/4";
    public const string PreviousSchemaVersion = "lex-index/3";
    public const string LegacySchemaVersion = "lex-index/2";

    /// <summary>
    /// One hash over every document identity and every provision hash, in fixed ordinal order.
    /// LexIndexReader.ComputeContentDigest must reproduce this byte for byte from the stored
    /// rows — that equality is what makes tampering detectable.
    /// </summary>
    /// <summary>
    /// Flattens a provision's cross-references into the lookup table. The ELI href
    /// "/eli/etat/leg/loi/2020/06/04/a476/jo" becomes the slug "loi-2020-06-04-a476", which is how
    /// works are keyed everywhere else, so a citation can be resolved to a work without parsing a
    /// URL at read time.
    /// </summary>
    private static void WriteCitations(
        SqliteConnection conn, ProvisionRow p, CitationTargetResolver resolver)
    {
        if (string.IsNullOrEmpty(p.CitationsJson)) return;
        JsonArray? arr;
        try { arr = JsonNode.Parse(p.CitationsJson) as JsonArray; }
        catch { return; }
        if (arr is null) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO citations VALUES ($rid,$a,$slug,$href,$label)";
        foreach (var node in arr.OfType<JsonObject>())
        {
            var href = node["href"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(href)) continue;
            var slug = SlugOfEli(href);
            if (slug is null) continue;
            slug = resolver.CanonicalWork(slug) ?? slug;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$rid", p.Rid);
            cmd.Parameters.AddWithValue("$a", p.Anchor);
            cmd.Parameters.AddWithValue("$slug", slug);
            cmd.Parameters.AddWithValue("$href", href);
            cmd.Parameters.AddWithValue("$label", (object?)node["text"]?.GetValue<string>() ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// "/eli/etat/leg/loi/2020/06/04/a476/jo" -> "loi-2020-06-04-a476". Null when it is not an ELI.
    private static string? SlugOfEli(string href)
    {
        var i = href.IndexOf("/eli/", StringComparison.Ordinal);
        if (i < 0) return null;
        var parts = href[(i + 5)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        var tail = parts.Skip(2).Where(x => x is not ("jo" or "consolide")).ToList();
        return tail.Count == 0 ? null : string.Join("-", tail);
    }

    private static string ContentDigest(
        SqliteConnection connection,
        IEnumerable<DocRow> docs,
        IEnumerable<ProvisionRow> provisions)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var d in docs.OrderBy(d => d.Key, StringComparer.Ordinal).ThenBy(d => d.Language, StringComparer.Ordinal))
            sb.Append(d.Key).Append('|').Append(d.Language).Append('|').Append(d.ValidFrom).Append('|')
              .Append(d.ValidTo ?? "").Append('|').Append(d.RecordSha ?? "").Append('\n');
        foreach (var p in provisions.OrderBy(p => p.Rid, StringComparer.Ordinal).ThenBy(p => p.Seq))
            sb.Append(p.ProvisionId).Append('|')
              .Append(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(p.TextMd)))).Append('\n');
        AppendProvisionGapContentDigest(connection, sb);
        AppendPublisherMetadataDigest(connection, sb);
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// Canonical V4 commitment over the complete SQLite trust surface. Schema entries, logical
    /// rows, FTS shadow rows and stored text bytes are all bound. Stamp rows are excluded because
    /// the signed stamp contains this digest and would otherwise make the construction circular.
    /// </summary>
    internal static string ContentDigestV4(SqliteConnection connection)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var tables = new List<string>();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                SELECT type,name,tbl_name,sql
                FROM sqlite_schema
                WHERE name NOT LIKE 'sqlite_%'
                ORDER BY type,name,tbl_name
                """;
            using var rows = schema.ExecuteReader();
            while (rows.Read())
            {
                AppendV4Marker(digest, "schema");
                for (var i = 0; i < rows.FieldCount; i++)
                    AppendV4Value(digest, rows.GetValue(i));
                if (rows.GetString(0) == "table"
                    && rows.GetString(1) != "stamp"
                    && (rows.IsDBNull(3) || !rows.GetString(3).StartsWith(
                        "CREATE VIRTUAL TABLE", StringComparison.OrdinalIgnoreCase)))
                    tables.Add(rows.GetString(1));
            }
        }

        foreach (var table in tables.Order(StringComparer.Ordinal))
        {
            var columns = new List<(string Name, long PrimaryKeyOrder)>();
            using (var metadata = connection.CreateCommand())
            {
                metadata.CommandText = $"PRAGMA table_xinfo({QuoteIdentifier(table)})";
                using var rows = metadata.ExecuteReader();
                while (rows.Read())
                    columns.Add((rows.GetString(1), rows.GetInt64(5)));
            }

            var rowIdAlias = RowIdAlias(connection, table, columns.Select(column => column.Name));

            AppendV4Marker(digest, "table");
            AppendV4Value(digest, table);
            if (rowIdAlias is not null) AppendV4Value(digest, "$sqlite_rowid");
            foreach (var column in columns) AppendV4Value(digest, column.Name);
            if (columns.Count == 0) continue;

            var quoted = columns.Select(column => QuoteIdentifier(column.Name)).ToArray();
            var primaryKey = columns.Where(column => column.PrimaryKeyOrder > 0)
                .OrderBy(column => column.PrimaryKeyOrder)
                .Select(column => QuoteIdentifier(column.Name))
                .ToArray();
            var rowId = rowIdAlias is null ? null : QuoteIdentifier(rowIdAlias);
            string[] selected = rowId is null ? quoted : [rowId, .. quoted];
            string[] order = rowId is not null
                ? [rowId]
                : primaryKey.Length == 0
                ? quoted.Concat(quoted.Select(column => $"typeof({column})")).ToArray()
                : primaryKey;
            using var data = connection.CreateCommand();
            data.CommandText = $"SELECT {string.Join(',', selected)} "
                + $"FROM {QuoteIdentifier(table)} ORDER BY "
                + string.Join(',', order);
            using var dataRows = data.ExecuteReader();
            while (dataRows.Read())
            {
                AppendV4Marker(digest, "row");
                for (var i = 0; i < dataRows.FieldCount; i++)
                    AppendV4Value(digest, dataRows.GetValue(i));
            }
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static string? RowIdAlias(
        SqliteConnection connection,
        string table,
        IEnumerable<string> declaredColumns)
    {
        using var metadata = connection.CreateCommand();
        metadata.CommandText = """
            SELECT "wr" FROM pragma_table_list
            WHERE "schema"='main' AND "name"=$name
            """;
        metadata.Parameters.AddWithValue("$name", table);
        if (metadata.ExecuteScalar() is not long withoutRowId)
            throw new InvalidDataException($"Table '{table}' has no SQLite layout metadata.");
        if (withoutRowId != 0) return null;

        var declared = declaredColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var alias = new[] { "rowid", "_rowid_", "oid" }
            .FirstOrDefault(candidate => !declared.Contains(candidate));
        return alias ?? throw new InvalidDataException(
            $"Table '{table}' shadows every SQLite rowid alias.");
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static void AppendV4Marker(IncrementalHash digest, string value) =>
        AppendV4Bytes(digest, (byte)'M', Encoding.UTF8.GetBytes(value));

    private static void AppendV4Value(IncrementalHash digest, object value)
    {
        switch (value)
        {
            case DBNull:
                AppendV4Bytes(digest, (byte)'N', []);
                return;
            case string text:
                AppendV4Bytes(digest, (byte)'T', Encoding.UTF8.GetBytes(text));
                return;
            case byte[] bytes:
                AppendV4Bytes(digest, (byte)'B', bytes);
                return;
            case long integer:
            {
                Span<byte> bytes = stackalloc byte[sizeof(long)];
                BinaryPrimitives.WriteInt64BigEndian(bytes, integer);
                AppendV4Bytes(digest, (byte)'I', bytes);
                return;
            }
            case double real:
            {
                Span<byte> bytes = stackalloc byte[sizeof(long)];
                BinaryPrimitives.WriteInt64BigEndian(
                    bytes, BitConverter.DoubleToInt64Bits(real));
                AppendV4Bytes(digest, (byte)'R', bytes);
                return;
            }
            default:
                throw new InvalidDataException(
                    $"Unsupported SQLite value type '{value.GetType().Name}' in V4 digest.");
        }
    }

    private static void AppendV4Bytes(
        IncrementalHash digest, byte type, ReadOnlySpan<byte> value)
    {
        Span<byte> header = stackalloc byte[sizeof(byte) + sizeof(long)];
        header[0] = type;
        BinaryPrimitives.WriteInt64BigEndian(header[1..], value.Length);
        digest.AppendData(header);
        digest.AppendData(value);
    }

    internal static void AppendProvisionGapContentDigest(
        SqliteConnection connection,
        StringBuilder output)
    {
        if (!TableExists(connection, "provision_gaps")) return;
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rid,seq,anchor,provision_id,eli,ptype,num,heading,path,
                   article_valid_from,text_unavailable_reason
            FROM provision_gaps ORDER BY rid,seq
            """;
        using var rows = command.ExecuteReader();
        while (rows.Read())
            AppendDigestRecord(output,
                "gap", rows.GetString(0),
                rows.GetInt64(1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                rows.GetString(2), rows.GetString(3),
                rows.IsDBNull(4) ? null : rows.GetString(4), rows.GetString(5),
                rows.IsDBNull(6) ? null : rows.GetString(6),
                rows.IsDBNull(7) ? null : rows.GetString(7),
                rows.IsDBNull(8) ? null : rows.GetString(8),
                rows.IsDBNull(9) ? null : rows.GetString(9),
                rows.GetString(10));
    }

    internal static string ProvisionGapDigest(SqliteConnection connection)
    {
        if (!TableExists(connection, "provision_gaps"))
            return Convert.ToHexStringLower(SHA256.HashData([]));
        var output = new StringBuilder();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rid,seq,anchor,provision_id,eli,ptype,num,heading,path,
                   article_valid_from,text_unavailable_reason
            FROM provision_gaps ORDER BY rid,seq
            """;
        using var rows = command.ExecuteReader();
        while (rows.Read())
            AppendDigestRecord(output,
                rows.GetString(0),
                rows.GetInt64(1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                rows.GetString(2), rows.GetString(3),
                rows.IsDBNull(4) ? null : rows.GetString(4), rows.GetString(5),
                rows.IsDBNull(6) ? null : rows.GetString(6),
                rows.IsDBNull(7) ? null : rows.GetString(7),
                rows.IsDBNull(8) ? null : rows.GetString(8),
                rows.IsDBNull(9) ? null : rows.GetString(9),
                rows.GetString(10));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())));
    }

    internal static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    internal static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !string.IsNullOrWhiteSpace(uri.Host);

    internal static void AppendPublisherMetadataDigest(SqliteConnection connection, StringBuilder output)
    {
        using (var metadata = connection.CreateCommand())
        {
            metadata.CommandText = """
                SELECT r.group_key,r.language,m.kind,m.identifier,m.value,m.normalized,
                       m.language,m.valid_from,m.valid_to,m.source_uri,m.citation_identity
                FROM work_publisher_metadata m
                JOIN work_records r ON r.work_id=m.work_id
                ORDER BY r.group_key,r.language,m.kind,m.identifier,COALESCE(m.value,''),
                         COALESCE(m.language,''),m.valid_from,COALESCE(m.valid_to,''),m.source_uri
                """;
            using var rows = metadata.ExecuteReader();
            while (rows.Read())
                AppendDigestRecord(output, "publisher",
                    rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3),
                    rows.IsDBNull(4) ? null : rows.GetString(4), rows.GetString(5),
                    rows.IsDBNull(6) ? null : rows.GetString(6), rows.GetString(7),
                    rows.IsDBNull(8) ? null : rows.GetString(8), rows.GetString(9),
                    rows.GetInt64(10).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        using var roles = connection.CreateCommand();
        roles.CommandText = "SELECT rid,role FROM document_roles ORDER BY rid,role";
        using var roleRows = roles.ExecuteReader();
        while (roleRows.Read())
            AppendDigestRecord(output, "role", roleRows.GetString(0), roleRows.GetString(1));
        using var discovery = connection.CreateCommand();
        discovery.CommandText = """
            SELECT r.group_key,r.language,d.kind,d.value,d.normalized,d.model_deployment,
                   d.prompt_sha256,d.schema_sha256,d.generated_at,d.confidence,
                   d.repeat_runs,d.agreement_ratio,d.evidence_json
            FROM work_discovery d
            JOIN work_records r ON r.work_id=d.work_id
            ORDER BY r.group_key,r.language,d.kind,d.normalized,d.model_deployment,
                     d.prompt_sha256,d.schema_sha256,d.generated_at,d.evidence_json
            """;
        using var discoveryRows = discovery.ExecuteReader();
        while (discoveryRows.Read())
            AppendDigestRecord(output, "weak-discovery",
                discoveryRows.GetString(0), discoveryRows.GetString(1), discoveryRows.GetString(2),
                discoveryRows.GetString(3), discoveryRows.GetString(4), discoveryRows.GetString(5),
                discoveryRows.GetString(6), discoveryRows.GetString(7), discoveryRows.GetString(8),
                discoveryRows.GetDouble(9).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                discoveryRows.GetInt64(10).ToString(System.Globalization.CultureInfo.InvariantCulture),
                discoveryRows.GetDouble(11).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                discoveryRows.GetString(12));
    }

    private static void AppendDigestRecord(StringBuilder output, params string?[] fields)
    {
        foreach (var field in fields)
            output.Append(field is null ? -1 : Encoding.UTF8.GetByteCount(field)).Append(':').Append(field);
        output.Append('\n');
    }

    public static void Build(
        string dbPath,
        IReadOnlyDictionary<string, string> stampValues,
        IEnumerable<DocRow> docs,
        IEnumerable<ProvisionRow> provisions,
        IEnumerable<EventRow> events,
        IEnumerable<ObservationRow> observations,
        string? signingKeyPem,
        IEnumerable<ProvisionStateRow>? provisionStates = null,
        IEnumerable<AnchorEventRow>? anchorEvents = null,
        SemanticBuildOptions? semantic = null,
        CapabilityBuildExpectation? capabilityExpectation = null,
        ProvisionGapIndexInput? provisionGaps = null)
    {
        ArgumentNullException.ThrowIfNull(stampValues);
        var stampInput = stampValues.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var docRows = docs.ToList();
        var collection = stampInput.GetValueOrDefault("collection")
            ?? throw new InvalidDataException("Index collection is required.");
        var hasProvisionGapCapability = provisionGaps is not null;
        if (hasProvisionGapCapability)
        {
            docRows = docRows
                .Select(document => document with
                {
                    PublisherMetadata = document.PublisherMetadata?
                        .OrderBy(value => value.Kind, StringComparer.Ordinal)
                        .ThenBy(value => value.Identifier, StringComparer.Ordinal)
                        .ThenBy(value => value.Language, StringComparer.Ordinal)
                        .ThenBy(value => value.Value, StringComparer.Ordinal)
                        .ThenBy(value => value.SourceUri, StringComparer.Ordinal)
                        .ThenBy(value => value.CitationIdentity)
                        .ToArray(),
                    DocumentRoles = document.DocumentRoles?
                        .Order(StringComparer.Ordinal).ToArray(),
                })
                .OrderBy(document => document.Key, StringComparer.Ordinal)
                .ThenBy(document => document.Language, StringComparer.Ordinal)
                .ThenBy(document => document.ValidFrom, StringComparer.Ordinal)
                .ThenBy(document => document.GroupKey, StringComparer.Ordinal)
                .ThenBy(document => document.GroupIdentifier, StringComparer.Ordinal)
                .ToList();
        }
        var foreignCollection = !hasProvisionGapCapability ? null : docRows.FirstOrDefault(document =>
            !string.Equals(document.Collection, collection, StringComparison.Ordinal));
        if (foreignCollection is not null)
            throw new InvalidDataException(
                $"Document '{foreignCollection.Key}' collection does not match the index collection.");
        var invalidSource = hasProvisionGapCapability
            ? docRows.FirstOrDefault(document =>
                document.SourceUri is null || !IsAbsoluteHttpUri(document.SourceUri))
            : null;
        if (invalidSource is not null)
            throw new InvalidDataException(
                $"Document '{invalidSource.Key}' has an invalid source_uri; absolute HTTP(S) is required.");
        var capabilityRows = CapabilityManifest.Build(docRows);
        CapabilityManifest.ValidateExpectation(collection, capabilityRows, capabilityExpectation);
        var capabilityDigest = CapabilityManifest.Digest(capabilityRows);
        var capabilityUnsupported = CapabilityManifest.GovernedFilters
            .Where(filter => !capabilityRows.Any(row => row.Filter == filter
                && row.Language == CapabilityManifest.AllLanguages
                && row.TimeScope == CapabilityManifest.AllVersions && row.Supported))
            .Order(StringComparer.Ordinal).ToArray();
        var capabilityPolicyDigest = capabilityExpectation?.PolicySha256
            ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("unchecked\n")));
        var provisionRows = provisions.ToList();
        var provisionGapRows = provisionGaps?.Rows.ToList() ?? [];
        if (hasProvisionGapCapability)
        {
            provisionRows = provisionRows
                .OrderBy(row => row.Rid, StringComparer.Ordinal)
                .ThenBy(row => row.Seq)
                .ThenBy(row => row.Anchor, StringComparer.Ordinal)
                .ToList();
            provisionGapRows = provisionGapRows
                .OrderBy(row => row.Rid, StringComparer.Ordinal)
                .ThenBy(row => row.Seq)
                .ThenBy(row => row.Anchor, StringComparer.Ordinal)
                .ToList();
        }
        var claimedArticlesCanon = stampInput.GetValueOrDefault("articles_canon");
        if (!hasProvisionGapCapability && claimedArticlesCanon is not null)
            throw new InvalidDataException(
                "articles_canon may be stamped only by the gap-aware index input.");
        if (hasProvisionGapCapability
            && claimedArticlesCanon is not null
            && claimedArticlesCanon != ProvisionGapIndexInput.RequiredArticlesCanon)
            throw new InvalidDataException(
                "Provision-gap capability requires articles canon 'canon/2'.");
        if (hasProvisionGapCapability
            && !string.Equals(stampInput.GetValueOrDefault("generation_sha256"),
                provisionGaps!.GenerationSha256, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Provision-gap generation_sha256 must match the signed stamp input.");
        if (hasProvisionGapCapability
            && !string.Equals(stampInput.GetValueOrDefault("articles_commit"),
                provisionGaps!.ArticlesCommit, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Provision-gap articles_commit must match the signed stamp input.");
        var blankProvision = provisionRows.FirstOrDefault(
            provision => string.IsNullOrWhiteSpace(provision.TextMd));
        if (blankProvision is not null)
            throw new InvalidDataException(
                $"Provision {blankProvision.ProvisionId} has no non-whitespace body text.");
        var provisionCoordinates = new HashSet<(string Rid, string Anchor)>();
        var provisionOrders = new HashSet<(string Rid, int Seq)>();
        var docByRid = docRows.ToDictionary(
            document => $"{document.Key}|{document.Language}|{document.ValidFrom}",
            StringComparer.Ordinal);
        foreach (var provision in provisionRows)
        {
            if (!provisionCoordinates.Add((provision.Rid, provision.Anchor)))
                throw new InvalidDataException(
                    $"Provision coordinate {provision.Rid}#{provision.Anchor} is duplicated.");
            if (provision.Seq < 0 || !provisionOrders.Add((provision.Rid, provision.Seq)))
                throw new InvalidDataException(
                    $"Provision order {provision.Rid}/{provision.Seq} is invalid or duplicated.");
            if (!docByRid.TryGetValue(provision.Rid, out var parentDocument))
                throw new InvalidDataException(
                    $"Provision '{provision.ProvisionId}' has no parent document '{provision.Rid}'.");
            if (hasProvisionGapCapability
                && !string.Equals(provision.ProvisionId,
                    $"{parentDocument.Key}#{provision.Anchor}",
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Provision '{provision.ProvisionId}' does not name its exact parent document and anchor.");
            var textSha = Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes(provision.TextMd)));
            if (!string.Equals(textSha, provision.TextSha,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Provision '{provision.ProvisionId}' text does not match text_sha.");
        }
        foreach (var gap in provisionGapRows)
        {
            ValidateProvisionGapForSigning(gap);
            if (!docByRid.TryGetValue(gap.Rid, out var parentDocument))
                throw new InvalidDataException(
                    $"Provision gap '{gap.ProvisionId}' has no parent document '{gap.Rid}'.");
            if (!string.Equals(gap.ProvisionId,
                    $"{parentDocument.Key}#{gap.Anchor}",
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Provision gap '{gap.ProvisionId}' does not name its exact parent document and anchor.");
            if (!provisionOrders.Add((gap.Rid, gap.Seq)))
                throw new InvalidDataException(
                    $"Provision order {gap.Rid}/{gap.Seq} has both text and a gap or is duplicated.");
            if (!provisionCoordinates.Add((gap.Rid, gap.Anchor)))
                throw new InvalidDataException(
                    $"Provision coordinate {gap.Rid}#{gap.Anchor} has both text and a gap.");
        }
        var provisionStateRows = (provisionStates ?? []).ToList();
        var anchorEventRows = (anchorEvents ?? []).ToList();
        var eventRows = events.ToList();
        var observationRows = observations.ToList();
        if (hasProvisionGapCapability)
        {
            provisionStateRows = provisionStateRows
                .OrderBy(row => row.GroupKey, StringComparer.Ordinal)
                .ThenBy(row => row.Language, StringComparer.Ordinal)
                .ThenBy(row => row.Anchor, StringComparer.Ordinal)
                .ThenBy(row => row.ValidFrom, StringComparer.Ordinal)
                .ThenBy(row => row.ValidTo, StringComparer.Ordinal)
                .ThenBy(row => row.TextSha, StringComparer.Ordinal)
                .ThenBy(row => row.InVersion, StringComparer.Ordinal)
                .ThenBy(row => row.ArticleValidFrom, StringComparer.Ordinal)
                .ThenBy(row => row.IsPrimaryLanguage)
                .ThenBy(row => row.ValidityConflict)
                .ToList();
            anchorEventRows = anchorEventRows
                .OrderBy(row => row.GroupKey, StringComparer.Ordinal)
                .ThenBy(row => row.Language, StringComparer.Ordinal)
                .ThenBy(row => row.AtVersion, StringComparer.Ordinal)
                .ThenBy(row => row.EType, StringComparer.Ordinal)
                .ThenBy(row => row.FromAnchor, StringComparer.Ordinal)
                .ThenBy(row => row.ToAnchor, StringComparer.Ordinal)
                .ThenBy(row => row.Anchor, StringComparer.Ordinal)
                .ThenBy(row => row.TextSha, StringComparer.Ordinal)
                .ThenBy(row => row.IsPrimaryLanguage)
                .ToList();
            eventRows = eventRows
                .OrderBy(row => row.Key, StringComparer.Ordinal)
                .ThenBy(row => row.ObservedFrom, StringComparer.Ordinal)
                .ThenBy(row => row.Scope, StringComparer.Ordinal)
                .ThenBy(row => row.Event, StringComparer.Ordinal)
                .ThenBy(row => row.Detail, StringComparer.Ordinal)
                .ThenBy(row => row.FirstMissedAt, StringComparer.Ordinal)
                .ThenBy(row => row.RunsMissed)
                .ThenBy(row => row.RunIdentity, StringComparer.Ordinal)
                .ToList();
            observationRows = observationRows
                .OrderBy(row => row.Key, StringComparer.Ordinal)
                .ThenBy(row => row.Language, StringComparer.Ordinal)
                .ThenBy(row => row.ExprValidFrom, StringComparer.Ordinal)
                .ThenBy(row => row.ObservedFrom, StringComparer.Ordinal)
                .ThenBy(row => row.ObservedTo, StringComparer.Ordinal)
                .ThenBy(row => row.Sha256, StringComparer.Ordinal)
                .ThenBy(row => row.SourceUri, StringComparer.Ordinal)
                .ToList();
        }
        Dictionary<string, IReadOnlyList<SemanticChunk>>? chunksByTextSha = null;
        List<SemanticChunk>? uniqueSemanticChunks = null;
        long semanticVectorTotal = 0;
        if (semantic is not null)
        {
            if (semantic.BatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(semantic), "Semantic batch size must be positive.");
            if (semantic.MaxBatchTokens <= 0)
                throw new ArgumentOutOfRangeException(nameof(semantic), "Semantic batch token budget must be positive.");
            chunksByTextSha = new Dictionary<string, IReadOnlyList<SemanticChunk>>(StringComparer.OrdinalIgnoreCase);
            uniqueSemanticChunks = [];
            var uniqueChunkHashes = new HashSet<string>(StringComparer.Ordinal);
            var seenTextHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uniqueTextRows = new List<ProvisionRow>();
            foreach (var provision in provisionRows)
                if (seenTextHashes.Add(provision.TextSha)) uniqueTextRows.Add(provision);
            var preparationWatch = System.Diagnostics.Stopwatch.StartNew();
            long preparationCompleted = 0;
            long lastPreparationPercent = -1;
            var lastPreparationReport = TimeSpan.Zero;
            ReportProgress(semantic, SemanticBuildStage.Preparation,
                preparationCompleted, uniqueTextRows.Count, preparationWatch,
                ref lastPreparationPercent, ref lastPreparationReport, force: true);
            using var preparationHeartbeat = new StageHeartbeat(
                semantic, SemanticBuildStage.Preparation, uniqueTextRows.Count, preparationWatch);
            foreach (var provision in uniqueTextRows)
            {
                preparationHeartbeat.SetCurrent(provision.TextSha, provision.TextMd.Length);
                var chunks = SemanticChunker.Split(provision.TextMd, semantic.Encoder);
                chunksByTextSha.Add(provision.TextSha, chunks);
                foreach (var chunk in chunks)
                    if (uniqueChunkHashes.Add(chunk.Sha256)) uniqueSemanticChunks.Add(chunk);
                preparationCompleted++;
                preparationHeartbeat.SetCompleted(preparationCompleted);
                ReportProgress(semantic, SemanticBuildStage.Preparation,
                    preparationCompleted, uniqueTextRows.Count, preparationWatch,
                    ref lastPreparationPercent, ref lastPreparationReport,
                    force: preparationCompleted == uniqueTextRows.Count);
            }
            semanticVectorTotal = uniqueSemanticChunks.Count;
        }

        var semanticProgressWatch = System.Diagnostics.Stopwatch.StartNew();
        long semanticVectorsCompleted = 0;
        long lastReportedPercent = -1;
        var lastProgressReport = TimeSpan.Zero;
        ReportProgress(semantic, SemanticBuildStage.Embeddings,
            semanticVectorsCompleted, semanticVectorTotal, semanticProgressWatch,
            ref lastReportedPercent, ref lastProgressReport, force: true);

        var vectorTempPath = semantic is null ? null : semantic.VectorPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var databaseBuildPath = hasProvisionGapCapability
            ? dbPath + ".tmp-" + Guid.NewGuid().ToString("N")
            : dbPath;
        using var semanticWriter = semantic is null ? null
            : new SemanticVectorWriter(vectorTempPath!, semantic.Encoder.Dimensions);
        using var embeddingCache = semantic?.EmbeddingCachePath is { } cachePath
            ? new SemanticEmbeddingCache(cachePath, semantic) : null;
        var vectorOrdinalByChunk = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
        if (semantic is not null)
        {
            using var embeddingHeartbeat = new StageHeartbeat(
                semantic, SemanticBuildStage.Embeddings, semanticVectorTotal, semanticProgressWatch);
            var bucketedChunks = uniqueSemanticChunks!
                .Select((chunk, originalOrder) => new
                {
                    Chunk = chunk,
                    OriginalOrder = originalOrder,
                    PaddingTokens = EmbeddingTokenBucket(chunk.TokenCount),
                })
                .GroupBy(item => item.PaddingTokens)
                .OrderBy(group => group.Key);
            foreach (var bucket in bucketedChunks)
            {
                var bucketBatchSize = Math.Max(1,
                    Math.Min(semantic.BatchSize, semantic.MaxBatchTokens / bucket.Key));
                foreach (var items in bucket.OrderBy(item => item.OriginalOrder).Chunk(bucketBatchSize))
                {
                    var batch = items.Select(item => item.Chunk).ToArray();
                    embeddingHeartbeat.SetCurrent(
                        $"{batch[0].Sha256}..{batch[^1].Sha256}",
                        batch.Sum(chunk => (long)chunk.Text.Length));
                    var records = new byte[batch.Length][];
                    var missingIndexes = new List<int>();
                    for (var i = 0; i < batch.Length; i++)
                    {
                        if (embeddingCache is null || !embeddingCache.TryRead(batch[i].Sha256, out records[i]!))
                            missingIndexes.Add(i);
                    }
                    if (missingIndexes.Count > 0)
                    {
                        var vectors = semantic.Encoder.EncodeBatch(
                            missingIndexes.Select(i => batch[i].Text).ToArray(),
                            EmbeddingInputKind.Passage,
                            bucket.Key);
                        if (vectors.Count != missingIndexes.Count)
                            throw new InvalidDataException("Embedding encoder returned the wrong batch size.");
                        var additions = new List<(string ChunkSha, byte[] Record)>(missingIndexes.Count);
                        for (var i = 0; i < missingIndexes.Count; i++)
                        {
                            var batchIndex = missingIndexes[i];
                            records[batchIndex] = SemanticVectorWriter.Quantize(vectors[i]);
                            additions.Add((batch[batchIndex].Sha256, records[batchIndex]));
                        }
                        embeddingCache?.Store(additions);
                    }
                    for (var i = 0; i < batch.Length; i++)
                    {
                        var ordinal = semanticWriter!.WriteRecord(records[i]);
                        vectorOrdinalByChunk.Add(batch[i].Sha256, ordinal);
                        semanticVectorsCompleted++;
                    }
                    embeddingHeartbeat.SetCompleted(semanticVectorsCompleted);
                    ReportProgress(semantic, SemanticBuildStage.Embeddings,
                        semanticVectorsCompleted, semanticVectorTotal, semanticProgressWatch,
                        ref lastReportedPercent, ref lastProgressReport,
                        force: semanticVectorsCompleted == semanticVectorTotal);
                }
            }
        }
        if (File.Exists(databaseBuildPath)) File.Delete(databaseBuildPath);
        using var conn = new SqliteConnection(
            $"Data Source={databaseBuildPath};Pooling=False");
        conn.Open();
        if (hasProvisionGapCapability)
        {
            // V4 is finalized on a private sibling file. Retaining SQLite's exclusive lock
            // across the content and stamp commits also prevents a path-aware competing writer
            // from changing the post-commit FTS bytes between digesting and signing.
            using var lockingMode = conn.CreateCommand();
            lockingMode.CommandText = "PRAGMA locking_mode=EXCLUSIVE";
            if (!string.Equals(lockingMode.ExecuteScalar() as string, "exclusive",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "V4 index finalization requires SQLite exclusive locking mode.");
        }

        // lex-index/3 and /4: an occurrence carries legal identity and dates, while wording lives once
        // in a content-addressed blob. Lexical state is also deduplicated across repeat versions.
        ExecuteSchema(conn, """
            CREATE TABLE docs(
              key TEXT NOT NULL, collection TEXT NOT NULL, group_key TEXT NOT NULL,
              group_identifier TEXT NOT NULL, kind TEXT, language TEXT NOT NULL,
              valid_from TEXT NOT NULL, valid_to TEXT, valid_time_source TEXT NOT NULL,
              observed_from TEXT NOT NULL, withdrawn INTEGER NOT NULL,
              text_available INTEGER NOT NULL, text_public INTEGER NOT NULL,
              record_sha TEXT, body_sha TEXT, source_uri TEXT,
              title TEXT, title_short TEXT,
              publication_date TEXT, status_note TEXT, rid TEXT NOT NULL,
              profile TEXT, hierarchy TEXT, domains TEXT, act_form TEXT,
              binding_status TEXT, consolidation_status TEXT,
              PRIMARY KEY(key, language, valid_from));
            CREATE INDEX ix_docs_group ON docs(group_key, valid_from);
            CREATE INDEX ix_docs_stab ON docs(collection, kind, valid_from, valid_to);
            CREATE INDEX ix_docs_rid ON docs(rid);
            CREATE TABLE document_roles(
              rid TEXT NOT NULL, role TEXT NOT NULL, PRIMARY KEY(rid,role));
            CREATE INDEX ix_document_roles_role ON document_roles(role,rid);
            CREATE TABLE provisions(
              rid TEXT NOT NULL, seq INTEGER NOT NULL, anchor TEXT NOT NULL,
              provision_id TEXT NOT NULL, ptype TEXT NOT NULL, num TEXT, heading TEXT,
              path TEXT, article_valid_from TEXT, work_title TEXT,
              text_sha TEXT NOT NULL, state_id INTEGER NOT NULL,
              PRIMARY KEY(rid, seq));
            CREATE INDEX ix_prov_rid ON provisions(rid);
            CREATE INDEX ix_prov_state ON provisions(state_id);
            CREATE TABLE text_blobs(
              text_sha TEXT PRIMARY KEY, encoding TEXT NOT NULL,
              original_size INTEGER NOT NULL, stored_size INTEGER NOT NULL, payload BLOB NOT NULL);
            CREATE TABLE lexical_states(
              state_id INTEGER PRIMARY KEY, group_key TEXT NOT NULL, language TEXT NOT NULL,
              anchor TEXT NOT NULL, text_sha TEXT NOT NULL, provision_id TEXT NOT NULL,
              ptype TEXT NOT NULL, num TEXT, heading TEXT, path TEXT,
              article_valid_from TEXT, work_title TEXT,
              UNIQUE(group_key, language, anchor, text_sha));
            CREATE TABLE semantic_chunks(
              chunk_id INTEGER PRIMARY KEY, state_id INTEGER NOT NULL,
              chunk_index INTEGER NOT NULL, chunk_sha TEXT NOT NULL,
              vector_ordinal INTEGER NOT NULL,
              UNIQUE(state_id, chunk_index));
            CREATE INDEX ix_semantic_state ON semantic_chunks(state_id);
            CREATE INDEX ix_semantic_vector ON semantic_chunks(vector_ordinal);
            -- Cross-references, flattened so the reverse question ("which articles cite this
            -- law?") is an indexed lookup rather than a scan over every provision's JSON.
            CREATE TABLE citations(
              rid TEXT NOT NULL, anchor TEXT NOT NULL,
              cited_slug TEXT NOT NULL, href TEXT NOT NULL, label TEXT);
            CREATE INDEX ix_cit_target ON citations(cited_slug);
            CREATE INDEX ix_prov_anchor ON provisions(anchor);
            CREATE TABLE provision_states(
              group_key TEXT NOT NULL, language TEXT NOT NULL, is_primary_language INTEGER NOT NULL,
              anchor TEXT NOT NULL, valid_from TEXT NOT NULL,
              valid_to TEXT, text_sha TEXT NOT NULL, in_version TEXT,
              article_valid_from TEXT, validity_conflict INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX ix_pstates ON provision_states(group_key, language, anchor, valid_from);
            CREATE TABLE anchor_events(
              group_key TEXT NOT NULL, language TEXT NOT NULL, is_primary_language INTEGER NOT NULL,
              etype TEXT NOT NULL, from_anchor TEXT, to_anchor TEXT,
              anchor TEXT, text_sha TEXT, at_version TEXT);
            CREATE INDEX ix_aevents ON anchor_events(group_key, language);
            CREATE TABLE events(
              key TEXT, scope TEXT, event TEXT, observed_from TEXT, detail TEXT,
              first_missed_at TEXT, runs_missed INTEGER, run_identity TEXT);
            CREATE INDEX ix_events_key ON events(key);
            CREATE TABLE obs_history(key TEXT, language TEXT, expr_valid_from TEXT,
              sha256 TEXT, source_uri TEXT, observed_from TEXT, observed_to TEXT);
            CREATE INDEX ix_obs_key ON obs_history(key, language, expr_valid_from);
            CREATE TABLE capability_manifest(
              filter_name TEXT NOT NULL, language TEXT NOT NULL, time_scope TEXT NOT NULL,
              period_start TEXT NOT NULL, period_end TEXT NOT NULL,
              eligible_rows INTEGER NOT NULL, populated_rows INTEGER NOT NULL,
              PRIMARY KEY(filter_name,language,time_scope,period_start,period_end));
            CREATE TABLE work_records(
              work_id INTEGER PRIMARY KEY,
              group_key TEXT NOT NULL, language TEXT NOT NULL,
              title TEXT, title_short TEXT, group_identifier TEXT,
              hierarchy TEXT, domains TEXT, act_form TEXT,
              UNIQUE(group_key,language));
            CREATE TABLE work_names(
              work_id INTEGER NOT NULL, kind TEXT NOT NULL,
              value TEXT NOT NULL, normalized TEXT NOT NULL, reviewed_by TEXT,
              UNIQUE(work_id,kind,normalized));
            CREATE INDEX ix_work_names_exact ON work_names(normalized,kind,work_id);
            CREATE TABLE work_discovery(
              work_id INTEGER NOT NULL, kind TEXT NOT NULL,
              value TEXT NOT NULL, normalized TEXT NOT NULL,
              model_deployment TEXT NOT NULL, prompt_sha256 TEXT NOT NULL,
              schema_sha256 TEXT NOT NULL, generated_at TEXT NOT NULL,
              confidence REAL NOT NULL, repeat_runs INTEGER NOT NULL,
              agreement_ratio REAL NOT NULL, evidence_json TEXT NOT NULL,
              UNIQUE(work_id,kind,normalized));
            CREATE TABLE work_publisher_metadata(
              work_id INTEGER NOT NULL, kind TEXT NOT NULL, identifier TEXT NOT NULL,
              value TEXT, normalized TEXT NOT NULL, language TEXT, valid_from TEXT NOT NULL, valid_to TEXT,
              source_uri TEXT NOT NULL, citation_identity INTEGER NOT NULL,
              UNIQUE(work_id,kind,identifier,value,language,valid_from));
            CREATE INDEX ix_work_publisher_metadata
              ON work_publisher_metadata(work_id,kind,language,valid_from);
            CREATE INDEX ix_work_publisher_metadata_normalized
              ON work_publisher_metadata(normalized,language,valid_from);
            CREATE TABLE work_vectors(
              work_vector_id INTEGER PRIMARY KEY,
              work_id INTEGER NOT NULL, evidence_kind TEXT NOT NULL, evidence_value TEXT,
              vector_ordinal INTEGER NOT NULL UNIQUE);
            CREATE INDEX ix_work_vectors_work ON work_vectors(work_id);
            CREATE VIRTUAL TABLE work_fts USING fts5(
              group_key UNINDEXED, language UNINDEXED,
              identifiers, aliases, titles, facets, publisher, discovery,
              tokenize='unicode61 remove_diacritics 2');
            CREATE TABLE stamp(k TEXT PRIMARY KEY, v TEXT NOT NULL);
            CREATE VIRTUAL TABLE fts USING fts5(work_title, num, heading, text_md, content='');
            CREATE VIRTUAL TABLE fts_vocab USING fts5vocab(fts, 'row');
            """, canonicalLineEndings: hasProvisionGapCapability);
        if (hasProvisionGapCapability)
            ExecuteSchema(conn, """
                CREATE TABLE provision_gaps(
                  rid TEXT NOT NULL, seq INTEGER NOT NULL, anchor TEXT NOT NULL,
                  provision_id TEXT NOT NULL, eli TEXT, ptype TEXT NOT NULL, num TEXT,
                  heading TEXT, path TEXT, article_valid_from TEXT,
                  text_unavailable_reason TEXT NOT NULL,
                  PRIMARY KEY(rid,seq), UNIQUE(rid,anchor));
                CREATE INDEX ix_provision_gaps_rid ON provision_gaps(rid,seq);
                """, canonicalLineEndings: true);

        System.Diagnostics.Stopwatch? finalizationWatch = null;
        long finalizationCompleted = 0;
        long lastFinalizationPercent = -1;
        var lastFinalizationReport = TimeSpan.Zero;
        Dictionary<string, string>? pendingV4Stamp = null;
        using (var tx = conn.BeginTransaction())
        {
            var databaseWatch = System.Diagnostics.Stopwatch.StartNew();
            var databaseTotal = (long)docRows.Count + provisionRows.Count + provisionGapRows.Count
                + provisionStateRows.Count
                + anchorEventRows.Count + eventRows.Count + observationRows.Count
                + capabilityRows.Count;
            long databaseCompleted = 0;
            long lastDatabasePercent = -1;
            var lastDatabaseReport = TimeSpan.Zero;
            ReportProgress(semantic, SemanticBuildStage.Database,
                databaseCompleted, databaseTotal, databaseWatch,
                ref lastDatabasePercent, ref lastDatabaseReport, force: true);
            void DatabaseItemCompleted()
            {
                databaseCompleted++;
                ReportProgress(semantic, SemanticBuildStage.Database,
                    databaseCompleted, databaseTotal, databaseWatch,
                    ref lastDatabasePercent, ref lastDatabaseReport,
                    force: databaseCompleted == databaseTotal);
            }

            var insDoc = conn.CreateCommand();
            insDoc.CommandText = """
                INSERT OR REPLACE INTO docs VALUES ($key,$col,$gk,$gi,$kind,$lang,$vf,$vt,$vts,$of,$wd,$ta,$tp,$rs,$bs,$su,$t,$ts2,$pd,$sn,$rid,$prof,$hier,$domains,$form,$binding,$consolidation)
                """;
            foreach (var p in new[] { "$key", "$col", "$gk", "$gi", "$kind", "$lang", "$vf", "$vt", "$vts", "$of", "$wd", "$ta", "$tp", "$rs", "$bs", "$su", "$t", "$ts2", "$pd", "$sn", "$rid", "$prof", "$hier", "$domains", "$form", "$binding", "$consolidation" })
                insDoc.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            var insRole = conn.CreateCommand();
            insRole.CommandText = "INSERT OR IGNORE INTO document_roles(rid,role) VALUES ($rid,$role)";
            insRole.Parameters.Add(new SqliteParameter("$rid", SqliteType.Text));
            insRole.Parameters.Add(new SqliteParameter("$role", SqliteType.Text));

            foreach (var d in docRows)
            {
                var rid = $"{d.Key}|{d.Language}|{d.ValidFrom}";
                Set(insDoc, "$key", d.Key); Set(insDoc, "$col", d.Collection); Set(insDoc, "$gk", d.GroupKey);
                Set(insDoc, "$gi", d.GroupIdentifier); Set(insDoc, "$kind", d.Kind); Set(insDoc, "$lang", d.Language);
                Set(insDoc, "$vf", d.ValidFrom); Set(insDoc, "$vt", d.ValidTo); Set(insDoc, "$vts", d.ValidTimeSource);
                Set(insDoc, "$of", d.ObservedFrom); Set(insDoc, "$wd", d.Withdrawn ? "1" : "0");
                Set(insDoc, "$ta", d.TextAvailable ? "1" : "0"); Set(insDoc, "$tp", d.TextPublic ? "1" : "0");
                Set(insDoc, "$rs", d.RecordSha); Set(insDoc, "$bs", d.BodySha); Set(insDoc, "$su", d.SourceUri);
                Set(insDoc, "$t", d.Title); Set(insDoc, "$ts2", d.TitleShort);
                Set(insDoc, "$pd", d.PublicationDate); Set(insDoc, "$sn", d.StatusNote); Set(insDoc, "$rid", rid);
                Set(insDoc, "$prof", d.Profile);
                Set(insDoc, "$hier", d.Hierarchy); Set(insDoc, "$domains", d.Domains);
                Set(insDoc, "$form", d.ActForm); Set(insDoc, "$binding", d.BindingStatus);
                Set(insDoc, "$consolidation", d.ConsolidationStatus);
                insDoc.ExecuteNonQuery();
                foreach (var role in (d.DocumentRoles ?? []).Distinct(StringComparer.Ordinal)
                             .Order(StringComparer.Ordinal))
                {
                    insRole.Parameters["$rid"].Value = rid;
                    insRole.Parameters["$role"].Value = role;
                    insRole.ExecuteNonQuery();
                }
                DatabaseItemCompleted();
            }

            var insCapability = conn.CreateCommand();
            insCapability.CommandText = """
                INSERT INTO capability_manifest(
                  filter_name,language,time_scope,period_start,period_end,
                  eligible_rows,populated_rows)
                VALUES ($filter,$language,$scope,$start,$end,$eligible,$populated)
                """;
            foreach (var parameter in new[] { "$filter", "$language", "$scope", "$start", "$end" })
                insCapability.Parameters.Add(new SqliteParameter(parameter, SqliteType.Text));
            insCapability.Parameters.Add(new SqliteParameter("$eligible", SqliteType.Integer));
            insCapability.Parameters.Add(new SqliteParameter("$populated", SqliteType.Integer));
            foreach (var row in capabilityRows)
            {
                Set(insCapability, "$filter", row.Filter);
                Set(insCapability, "$language", row.Language);
                Set(insCapability, "$scope", row.TimeScope);
                Set(insCapability, "$start", row.PeriodStart ?? "");
                Set(insCapability, "$end", row.PeriodEnd ?? "");
                insCapability.Parameters["$eligible"].Value = row.EligibleRows;
                insCapability.Parameters["$populated"].Value = row.PopulatedRows;
                insCapability.ExecuteNonQuery();
                DatabaseItemCompleted();
            }

            var insBlob = conn.CreateCommand();
            insBlob.CommandText = "INSERT INTO text_blobs VALUES ($sha,$enc,$original,$stored,$payload)";
            insBlob.Parameters.Add(new SqliteParameter("$sha", SqliteType.Text));
            insBlob.Parameters.Add(new SqliteParameter("$enc", SqliteType.Text));
            insBlob.Parameters.Add(new SqliteParameter("$original", SqliteType.Integer));
            insBlob.Parameters.Add(new SqliteParameter("$stored", SqliteType.Integer));
            insBlob.Parameters.Add(new SqliteParameter("$payload", SqliteType.Blob));
            var writtenTextBlobs = new HashSet<string>(StringComparer.Ordinal);

            var insLexical = conn.CreateCommand();
            insLexical.CommandText = """
                INSERT OR IGNORE INTO lexical_states(
                  group_key,language,anchor,text_sha,provision_id,ptype,num,heading,path,article_valid_from,work_title)
                VALUES ($gk,$lang,$a,$sha,$pid,$pt,$n,$h,$path,$avf,$wt)
                """;
            foreach (var name in new[] { "$gk", "$lang", "$a", "$sha", "$pid", "$pt", "$n", "$h", "$path", "$avf", "$wt" })
                insLexical.Parameters.Add(new SqliteParameter(name, SqliteType.Text));
            var findLexical = conn.CreateCommand();
            findLexical.CommandText = "SELECT state_id FROM lexical_states WHERE group_key=$gk AND language=$lang AND anchor=$a AND text_sha=$sha";
            foreach (var name in new[] { "$gk", "$lang", "$a", "$sha" })
                findLexical.Parameters.Add(new SqliteParameter(name, SqliteType.Text));
            var insFts = conn.CreateCommand();
            insFts.CommandText = "INSERT INTO fts(rowid,work_title,num,heading,text_md) VALUES ($id,$wt,$n,$h,$md)";
            insFts.Parameters.Add(new SqliteParameter("$id", SqliteType.Integer));
            foreach (var name in new[] { "$wt", "$n", "$h", "$md" })
                insFts.Parameters.Add(new SqliteParameter(name, SqliteType.Text));
            var insChunk = conn.CreateCommand();
            insChunk.CommandText = "INSERT INTO semantic_chunks(state_id,chunk_index,chunk_sha,vector_ordinal) VALUES ($state,$index,$sha,$ordinal)";
            insChunk.Parameters.Add(new SqliteParameter("$state", SqliteType.Integer));
            insChunk.Parameters.Add(new SqliteParameter("$index", SqliteType.Integer));
            insChunk.Parameters.Add(new SqliteParameter("$sha", SqliteType.Text));
            insChunk.Parameters.Add(new SqliteParameter("$ordinal", SqliteType.Integer));
            var insProv = conn.CreateCommand();
            insProv.CommandText = """
                INSERT INTO provisions VALUES ($rid,$seq,$a,$pid,$pt,$n,$h,$path,$avf,$wt,$sha,$state)
                """;
            insProv.Parameters.Add(new SqliteParameter("$seq", SqliteType.Integer));
            insProv.Parameters.Add(new SqliteParameter("$state", SqliteType.Integer));
            foreach (var p in new[] { "$rid", "$a", "$pid", "$pt", "$n", "$h", "$path", "$avf", "$wt", "$sha" })
                insProv.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            var citationResolver = new CitationTargetResolver(stampInput["collection"],
                docRows);
            foreach (var p in provisionRows)
            {
                if (!docByRid.TryGetValue(p.Rid, out var doc))
                    throw new InvalidDataException($"Provision '{p.ProvisionId}' has no parent document '{p.Rid}'.");
                var utf8 = Encoding.UTF8.GetBytes(p.TextMd);
                var actualSha = Convert.ToHexStringLower(SHA256.HashData(utf8));
                if (!actualSha.Equals(p.TextSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Provision '{p.ProvisionId}' text does not match text_sha.");
                if (writtenTextBlobs.Add(actualSha))
                {
                    var (encoding, payload) = EncodeText(utf8);
                    insBlob.Parameters["$sha"].Value = actualSha;
                    insBlob.Parameters["$enc"].Value = encoding;
                    insBlob.Parameters["$original"].Value = utf8.Length;
                    insBlob.Parameters["$stored"].Value = payload.Length;
                    insBlob.Parameters["$payload"].Value = payload;
                    insBlob.ExecuteNonQuery();
                }

                Set(insLexical, "$gk", doc.GroupKey); Set(insLexical, "$lang", doc.Language);
                Set(insLexical, "$a", p.Anchor); Set(insLexical, "$sha", actualSha);
                Set(insLexical, "$pid", p.ProvisionId); Set(insLexical, "$pt", p.PType);
                Set(insLexical, "$n", p.Num); Set(insLexical, "$h", p.Heading); Set(insLexical, "$path", p.Path);
                Set(insLexical, "$avf", p.ArticleValidFrom); Set(insLexical, "$wt", p.WorkTitle);
                var created = insLexical.ExecuteNonQuery() == 1;
                Set(findLexical, "$gk", doc.GroupKey); Set(findLexical, "$lang", doc.Language);
                Set(findLexical, "$a", p.Anchor); Set(findLexical, "$sha", actualSha);
                var stateId = Convert.ToInt64(findLexical.ExecuteScalar());
                if (created)
                {
                    insFts.Parameters["$id"].Value = stateId;
                    Set(insFts, "$wt", p.WorkTitle); Set(insFts, "$n", p.Num); Set(insFts, "$h", p.Heading);
                    Set(insFts, "$md", p.TextMd);
                    insFts.ExecuteNonQuery();
                    if (semantic is not null)
                    {
                        foreach (var chunk in chunksByTextSha![actualSha])
                        {
                            if (!vectorOrdinalByChunk.TryGetValue(chunk.Sha256, out var ordinal))
                                throw new InvalidDataException($"Semantic chunk '{chunk.Sha256}' was not embedded.");
                            insChunk.Parameters["$state"].Value = stateId;
                            insChunk.Parameters["$index"].Value = chunk.Index;
                            insChunk.Parameters["$sha"].Value = chunk.Sha256;
                            insChunk.Parameters["$ordinal"].Value = ordinal;
                            insChunk.ExecuteNonQuery();
                        }
                    }
                }

                insProv.Parameters["$seq"].Value = p.Seq;
                insProv.Parameters["$state"].Value = stateId;
                Set(insProv, "$rid", p.Rid); Set(insProv, "$a", p.Anchor); Set(insProv, "$pid", p.ProvisionId);
                Set(insProv, "$pt", p.PType); Set(insProv, "$n", p.Num); Set(insProv, "$h", p.Heading);
                Set(insProv, "$path", p.Path); Set(insProv, "$avf", p.ArticleValidFrom);
                Set(insProv, "$wt", p.WorkTitle); Set(insProv, "$sha", actualSha);
                insProv.ExecuteNonQuery();
                WriteCitations(conn, p, citationResolver);
                DatabaseItemCompleted();
            }

            var insGap = conn.CreateCommand();
            insGap.CommandText = """
                INSERT INTO provision_gaps VALUES ($rid,$seq,$a,$pid,$eli,$pt,$n,$h,$path,$avf,$reason)
                """;
            insGap.Parameters.Add(new SqliteParameter("$seq", SqliteType.Integer));
            foreach (var name in new[]
                     { "$rid", "$a", "$pid", "$eli", "$pt", "$n", "$h", "$path", "$avf", "$reason" })
                insGap.Parameters.Add(new SqliteParameter(name, SqliteType.Text));
            foreach (var gap in provisionGapRows)
            {
                if (!docByRid.TryGetValue(gap.Rid, out var parentDocument))
                    throw new InvalidDataException(
                        $"Provision gap '{gap.ProvisionId}' has no parent document '{gap.Rid}'.");
                if (!string.Equals(gap.ProvisionId,
                        $"{parentDocument.Key}#{gap.Anchor}",
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Provision gap '{gap.ProvisionId}' does not name its exact parent document and anchor.");
                insGap.Parameters["$seq"].Value = gap.Seq;
                Set(insGap, "$rid", gap.Rid); Set(insGap, "$a", gap.Anchor);
                Set(insGap, "$pid", gap.ProvisionId); Set(insGap, "$eli", gap.Eli);
                Set(insGap, "$pt", gap.PType);
                Set(insGap, "$n", gap.Num); Set(insGap, "$h", gap.Heading);
                Set(insGap, "$path", gap.Path); Set(insGap, "$avf", gap.ArticleValidFrom);
                Set(insGap, "$reason", gap.TextUnavailableReason);
                insGap.ExecuteNonQuery();
                DatabaseItemCompleted();
            }

            var insState = conn.CreateCommand();
            insState.CommandText = "INSERT INTO provision_states VALUES ($gk,$lang,$primary,$a,$vf,$vt,$sha,$iv,$avf,$vc)";
            foreach (var p in new[] { "$gk", "$lang", "$primary", "$a", "$vf", "$vt", "$sha", "$iv", "$avf", "$vc" })
                insState.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            foreach (var s in provisionStateRows)
            {
                Set(insState, "$gk", s.GroupKey); Set(insState, "$lang", s.Language);
                Set(insState, "$primary", s.IsPrimaryLanguage ? "1" : "0");
                Set(insState, "$a", s.Anchor); Set(insState, "$vf", s.ValidFrom);
                Set(insState, "$vt", s.ValidTo); Set(insState, "$sha", s.TextSha); Set(insState, "$iv", s.InVersion);
                Set(insState, "$avf", s.ArticleValidFrom); Set(insState, "$vc", s.ValidityConflict ? "1" : "0");
                insState.ExecuteNonQuery();
                DatabaseItemCompleted();
            }

            var insAe = conn.CreateCommand();
            insAe.CommandText = "INSERT INTO anchor_events VALUES ($gk,$lang,$primary,$et,$fa,$ta,$a,$sha,$av)";
            foreach (var p in new[] { "$gk", "$lang", "$primary", "$et", "$fa", "$ta", "$a", "$sha", "$av" })
                insAe.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            foreach (var e in anchorEventRows)
            {
                Set(insAe, "$gk", e.GroupKey); Set(insAe, "$lang", e.Language);
                Set(insAe, "$primary", e.IsPrimaryLanguage ? "1" : "0");
                Set(insAe, "$et", e.EType); Set(insAe, "$fa", e.FromAnchor);
                Set(insAe, "$ta", e.ToAnchor); Set(insAe, "$a", e.Anchor); Set(insAe, "$sha", e.TextSha);
                Set(insAe, "$av", e.AtVersion);
                insAe.ExecuteNonQuery();
                DatabaseItemCompleted();
            }

            var insEv = conn.CreateCommand();
            insEv.CommandText = """
                INSERT INTO events(
                  key,scope,event,observed_from,detail,
                  first_missed_at,runs_missed,run_identity)
                VALUES ($key,$scope,$event,$of,$detail,$first,$missed,$run)
                """;
            foreach (var p in new[] { "$key", "$scope", "$event", "$of", "$detail", "$first", "$run" })
                insEv.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            insEv.Parameters.Add(new SqliteParameter("$missed", SqliteType.Integer));
            foreach (var e in eventRows)
            {
                Set(insEv, "$key", e.Key); Set(insEv, "$scope", e.Scope); Set(insEv, "$event", e.Event);
                Set(insEv, "$of", e.ObservedFrom); Set(insEv, "$detail", e.Detail);
                Set(insEv, "$first", e.FirstMissedAt);
                insEv.Parameters["$missed"].Value = (object?)e.RunsMissed ?? DBNull.Value;
                Set(insEv, "$run", e.RunIdentity);
                insEv.ExecuteNonQuery();
                DatabaseItemCompleted();
            }

            var insObs = conn.CreateCommand();
            insObs.CommandText = "INSERT INTO obs_history VALUES ($key,$lang,$evf,$sha,$su,$of,$ot)";
            foreach (var p in new[] { "$key", "$lang", "$evf", "$sha", "$su", "$of", "$ot" })
                insObs.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            foreach (var o in observationRows)
            {
                Set(insObs, "$key", o.Key); Set(insObs, "$lang", o.Language); Set(insObs, "$evf", o.ExprValidFrom);
                Set(insObs, "$sha", o.Sha256); Set(insObs, "$su", o.SourceUri); Set(insObs, "$of", o.ObservedFrom);
                Set(insObs, "$ot", o.ObservedTo);
                insObs.ExecuteNonQuery();
                DatabaseItemCompleted();
            }

            if (databaseTotal == 0)
                ReportProgress(semantic, SemanticBuildStage.Database,
                    0, 0, databaseWatch, ref lastDatabasePercent, ref lastDatabaseReport, force: true);

            WorkSearch.Populate(conn, docRows, semantic, semanticWriter, embeddingCache);

            finalizationWatch = System.Diagnostics.Stopwatch.StartNew();
            ReportProgress(semantic, SemanticBuildStage.Finalization,
                finalizationCompleted, 3, finalizationWatch,
                ref lastFinalizationPercent, ref lastFinalizationReport, force: true);
            using var finalizationHeartbeat = semantic is null ? null : new StageHeartbeat(
                semantic, SemanticBuildStage.Finalization, 3, finalizationWatch);
            finalizationHeartbeat?.SetCurrent("content-digest", provisionRows.Sum(row => (long)row.TextMd.Length));

            // The signature must bind the CONTENT, not just the metadata beside it: otherwise an
            // article's text could be edited inside a released database and the stamp would
            // still verify. Computed HERE, where docs and provisions are written, so no caller
            // can build an index without it and no path can drift from what is stored.
            var stamp = new Dictionary<string, string>(stampInput)
            {
                ["schema"] = hasProvisionGapCapability
                    ? SchemaVersion
                    : PreviousSchemaVersion,
                ["algorithm"] = StampSigner.Algorithm,
                ["work_catalog_version"] = "3",
                ["capability_manifest_schema"] = CapabilityManifest.Schema,
                ["capability_manifest_rows"] = capabilityRows.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["capability_manifest_sha256"] = capabilityDigest,
                ["capability_manifest_unsupported_filters"] = string.Join(',', capabilityUnsupported),
                ["capability_policy_tier"] = capabilityExpectation?.Tier ?? "unchecked",
                ["capability_policy_sha256"] = capabilityPolicyDigest,
            };
            if (!hasProvisionGapCapability)
                stamp["content_digest"] = ContentDigest(conn, docRows, provisionRows);
            if (hasProvisionGapCapability)
            {
                stamp["articles_canon"] = ProvisionGapIndexInput.RequiredArticlesCanon;
                stamp["provision_gap_schema"] = "lex-provision-gap/1";
                stamp["provision_gap_rows"] = provisionGapRows.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                stamp["provision_gap_sha256"] = ProvisionGapDigest(conn);
            }
            using (var workCounts = conn.CreateCommand())
            {
                workCounts.CommandText = """
                    SELECT (SELECT COUNT(*) FROM work_records),
                           (SELECT COUNT(*) FROM work_vectors),
                           (SELECT COUNT(*) FROM work_publisher_metadata),
                           (SELECT COUNT(*) FROM document_roles),
                           (SELECT COUNT(*) FROM work_discovery)
                    """;
                using var countRow = workCounts.ExecuteReader();
                if (!countRow.Read()) throw new InvalidDataException("Work search counts cannot be read.");
                var workVectorRecords = countRow.GetInt64(1);
                stamp["work_search_records"] = countRow.GetInt64(0).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                stamp["work_vector_records"] = workVectorRecords.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                stamp["publisher_metadata_records"] = countRow.GetInt64(2).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                stamp["document_role_records"] = countRow.GetInt64(3).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                stamp["weak_discovery_records"] = countRow.GetInt64(4).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                if (workVectorRecords > 0)
                    stamp["vector_layout"] = "lex-vectors/1-mixed-provision-work";
            }
            finalizationCompleted++;
            finalizationHeartbeat?.SetCompleted(finalizationCompleted);
            ReportProgress(semantic, SemanticBuildStage.Finalization,
                finalizationCompleted, 3, finalizationWatch,
                ref lastFinalizationPercent, ref lastFinalizationReport, force: true);
            finalizationHeartbeat?.SetCurrent("embedded-stamp", stamp.Count);
            if (semantic is not null)
            {
                stamp["embedding_model"] = semantic.Encoder.ModelId;
                stamp["embedding_revision"] = semantic.Encoder.ModelRevision;
                stamp["embedding_model_sha256"] = semantic.ModelSha256;
                stamp["embedding_tokenizer_sha256"] = semantic.TokenizerSha256;
                stamp["vector_format"] = semantic.VectorFormat;
                stamp["vector_file"] = Path.GetFileName(semantic.VectorPath);
                stamp["embedding_execution_provider"] = semantic.ExecutionProvider;
                stamp["embedding_profile"] = semantic.EmbeddingProfile;
                stamp["embedding_max_batch_tokens"] = semantic.MaxBatchTokens.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            if (!hasProvisionGapCapability && signingKeyPem is not null)
            {
                var (sig, pub) = StampSigner.Sign(stamp, signingKeyPem);
                stamp["signature"] = sig;
                stamp["public_key"] = pub;
            }
            if (hasProvisionGapCapability)
            {
                ValidatePersistedProvisionGaps(conn);
                pendingV4Stamp = stamp;
            }
            else
                InsertStamp(conn, tx, stamp);

            finalizationHeartbeat?.SetCurrent("sqlite-commit", 0);
            tx.Commit();
            finalizationCompleted++;
            finalizationHeartbeat?.SetCompleted(finalizationCompleted);
            ReportProgress(semantic, SemanticBuildStage.Finalization,
                finalizationCompleted, 3, finalizationWatch,
                ref lastFinalizationPercent, ref lastFinalizationReport, force: true);
        }
        if (pendingV4Stamp is not null)
        {
            // FTS5 can finalize its shadow rows at the bulk commit, so only the post-commit
            // state is the artifact that may be signed. The private path and retained exclusive
            // lock make this second transaction unreachable to competing writers.
            pendingV4Stamp["content_digest"] = ContentDigestV4(conn);
            if (signingKeyPem is not null)
            {
                var (signature, publicKey) = StampSigner.Sign(pendingV4Stamp, signingKeyPem);
                pendingV4Stamp["signature"] = signature;
                pendingV4Stamp["public_key"] = publicKey;
            }
            using var stampTransaction = conn.BeginTransaction();
            InsertStamp(conn, stampTransaction, pendingV4Stamp);
            stampTransaction.Commit();
        }
        if (semantic is not null)
        {
            semanticWriter!.Dispose();
            File.Move(vectorTempPath!, semantic.VectorPath, overwrite: true);
        }
        if (hasProvisionGapCapability)
        {
            conn.Close();
            using (var durable = new FileStream(
                       databaseBuildPath, FileMode.Open, FileAccess.ReadWrite,
                       FileShare.Read, bufferSize: 1, FileOptions.WriteThrough))
                durable.Flush(flushToDisk: true);
            File.Move(databaseBuildPath, dbPath, overwrite: true);
        }
        // The last step is publication of the durable vector file (or a no-op for lexical-only
        // builds). Keep the same clock so stage elapsed time remains monotonic.
        finalizationCompleted++;
        ReportProgress(semantic, SemanticBuildStage.Finalization,
            finalizationCompleted, 3, finalizationWatch!,
            ref lastFinalizationPercent, ref lastFinalizationReport, force: true);
        }
        catch
        {
            semanticWriter?.Dispose();
            if (vectorTempPath is not null && File.Exists(vectorTempPath))
                File.Delete(vectorTempPath);
            if (hasProvisionGapCapability && File.Exists(databaseBuildPath))
                File.Delete(databaseBuildPath);
            throw;
        }
    }

    private static void InsertStamp(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyDictionary<string, string> stamp)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO stamp VALUES ($key,$value)";
        command.Parameters.Add(new SqliteParameter("$key", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$value", SqliteType.Text));
        foreach (var (key, value) in stamp)
        {
            command.Parameters["$key"].Value = key;
            command.Parameters["$value"].Value = value;
            command.ExecuteNonQuery();
        }
    }

    internal static int EmbeddingTokenBucket(int tokenCount) => tokenCount switch
    {
        <= 0 => throw new InvalidDataException("Semantic chunk token count must be positive."),
        <= 32 => 32,
        <= 64 => 64,
        <= 128 => 128,
        <= 256 => 256,
        <= 512 => 512,
        _ => throw new InvalidDataException(
            $"Semantic chunk has {tokenCount} tokens; the pinned encoder supports at most 512."),
    };

    private static void ReportProgress(
        SemanticBuildOptions? semantic,
        SemanticBuildStage stage,
        long completed,
        long total,
        System.Diagnostics.Stopwatch watch,
        ref long lastReportedPercent,
        ref TimeSpan lastProgressReport,
        bool force)
    {
        if (semantic?.Progress is null) return;
        var elapsed = watch.Elapsed;
        var percent = total == 0 ? 100 : completed * 100 / total;
        if (!force && percent == lastReportedPercent
            && elapsed - lastProgressReport < TimeSpan.FromSeconds(30))
            return;
        TimeSpan? remaining = completed == 0
            ? null
            : TimeSpan.FromTicks((long)(elapsed.Ticks * (total - completed) / (double)completed));
        EmitProgress(semantic, new SemanticBuildProgress(completed, total, elapsed, remaining, stage));
        lastReportedPercent = percent;
        lastProgressReport = elapsed;
    }

    private static void EmitProgress(SemanticBuildOptions semantic, SemanticBuildProgress progress)
    {
        var callback = semantic.Progress;
        if (callback is null) return;
        lock (callback) callback(progress);
    }

    private sealed class StageHeartbeat : IDisposable
    {
        private readonly SemanticBuildOptions _semantic;
        private readonly SemanticBuildStage _stage;
        private readonly long _total;
        private readonly System.Diagnostics.Stopwatch _watch;
        private readonly object _stateGate = new();
        private readonly Timer? _timer;
        private long _completed;
        private string? _currentItem;
        private long? _currentItemCharacters;
        private TimeSpan? _currentItemStartedAt;

        public StageHeartbeat(
            SemanticBuildOptions semantic,
            SemanticBuildStage stage,
            long total,
            System.Diagnostics.Stopwatch watch)
        {
            _semantic = semantic;
            _stage = stage;
            _total = total;
            _watch = watch;
            if (semantic.Progress is null) return;
            var interval = semantic.ProgressHeartbeatInterval ?? TimeSpan.FromSeconds(30);
            if (interval <= TimeSpan.Zero) return;
            _timer = new Timer(_ => Report(), null, interval, interval);
        }

        public void SetCurrent(string item, long characters)
        {
            lock (_stateGate)
            {
                _currentItem = item;
                _currentItemCharacters = characters;
                _currentItemStartedAt = _watch.Elapsed;
            }
        }

        public void SetCompleted(long completed) => Interlocked.Exchange(ref _completed, completed);

        private void Report()
        {
            string? currentItem;
            long? currentItemCharacters;
            TimeSpan? currentItemElapsed;
            lock (_stateGate)
            {
                currentItem = _currentItem;
                currentItemCharacters = _currentItemCharacters;
                currentItemElapsed = _currentItemStartedAt is { } started
                    ? _watch.Elapsed - started : null;
            }
            var completed = Interlocked.Read(ref _completed);
            var elapsed = _watch.Elapsed;
            TimeSpan? remaining = completed == 0
                ? null
                : TimeSpan.FromTicks((long)(elapsed.Ticks * (_total - completed) / (double)completed));
            EmitProgress(_semantic, new SemanticBuildProgress(
                completed, _total, elapsed, remaining, _stage,
                currentItem, currentItemCharacters, currentItemElapsed, IsHeartbeat: true));
        }

        public void Dispose()
        {
            if (_timer is null) return;
            _timer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    internal static void ExecuteSchema(
        SqliteConnection connection,
        string sql,
        bool canonicalLineEndings)
    {
        using var command = connection.CreateCommand();
        command.CommandText = canonicalLineEndings
            ? sql.ReplaceLineEndings("\n")
            : sql;
        command.ExecuteNonQuery();
    }

    internal static void ValidateProvisionGapForSigning(ProvisionGapRow gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        if (gap.Seq < 0
            || string.IsNullOrWhiteSpace(gap.Rid)
            || gap.Rid.Length > 512
            || string.IsNullOrWhiteSpace(gap.Anchor)
            || gap.Anchor.Length > 512
            || string.IsNullOrWhiteSpace(gap.ProvisionId)
            || gap.ProvisionId.Length > 1_000
            || string.IsNullOrWhiteSpace(gap.PType)
            || gap.PType.Length > 128
            || gap.Eli?.Length > 4_096
            || gap.Num?.Length > 512
            || gap.Heading?.Length > 4_096
            || gap.Path?.Length > 4_096
            || gap.ArticleValidFrom?.Length > 64
            || gap.TextUnavailableReason is not ("marker_only" or "marker_suspicious")
            || gap.Eli is not null && !IsAbsoluteHttpUri(gap.Eli)
            || gap.ArticleValidFrom is not null && !DateOnly.TryParseExact(
                gap.ArticleValidFrom,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _))
            throw new InvalidDataException(
                "A provision gap violates the provision-gap metadata contract.");
    }

    internal static void ValidatePersistedProvisionGaps(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rid,seq,anchor,provision_id,eli,ptype,num,heading,path,
                   article_valid_from,text_unavailable_reason
            FROM provision_gaps ORDER BY rid,seq
            """;
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            if (rows.GetValue(1) is not long sequence
                || sequence is < 0 or > int.MaxValue)
                throw new InvalidDataException(
                    "Persisted provision gap violates the provision-gap metadata contract.");
            ValidateProvisionGapForSigning(new ProvisionGapRow(
                RequiredText(rows, 0),
                (int)sequence,
                RequiredText(rows, 2),
                RequiredText(rows, 3),
                OptionalText(rows, 4),
                RequiredText(rows, 5),
                OptionalText(rows, 6),
                OptionalText(rows, 7),
                OptionalText(rows, 8),
                OptionalText(rows, 9),
                RequiredText(rows, 10)));
        }

        static string RequiredText(SqliteDataReader rows, int ordinal) =>
            rows.GetValue(ordinal) as string
            ?? throw new InvalidDataException(
                "Persisted provision gap violates the provision-gap metadata contract.");

        static string? OptionalText(SqliteDataReader rows, int ordinal) =>
            rows.IsDBNull(ordinal)
                ? null
                : rows.GetValue(ordinal) as string
                    ?? throw new InvalidDataException(
                        "Persisted provision gap violates the provision-gap metadata contract.");
    }

    private static (string Encoding, byte[] Payload) EncodeText(byte[] utf8)
    {
        if (utf8.Length == 0) return ("raw", utf8);
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(utf8.Length)];
        if (!BrotliEncoder.TryCompress(utf8, buffer, out var written, quality: 4, window: 22)
            || written >= utf8.Length * 0.92)
            return ("raw", utf8);
        return ("br4", buffer[..written]);
    }

    private static void Set(SqliteCommand cmd, string name, string? value) =>
        cmd.Parameters[name].Value = (object?)value ?? DBNull.Value;
}
