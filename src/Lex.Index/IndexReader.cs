using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Lex.Index;

/// Per document type: how many versions are held, and how many of them carry text. The second
/// number is the honest one. A source may serve a version only in a format that has no article
/// structure; such a version is held as a complete dated record with no wording (D49).
public sealed record CoverageKind(string? Kind, int Versions, int WithText);

/// How many versions each extraction profile produced text for. Publishes the confidence mix
/// of the corpus as a number rather than as a claim.
public sealed record CoverageProfile(string Profile, int Versions);

/// How many works and versions exist in each language, and how many works hold more than one.
/// The tool that exists to say what is NOT held could not answer "which languages", which is the
/// question that decides whether a language control belongs to the site or to the document.
public sealed record CoverageLanguage(string Language, int Works, int Versions);

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
    int MultilingualWorks);

/// Values that the mounted index can actually accept as public search filters. Keeping this
/// inventory beside the data means adding a reviewed domain or jurisdiction does not require a
/// second hard-coded list in the web client.
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
    private readonly SqliteConnection _conn;
    private readonly string _schema;
    private readonly ITextEncoder? _encoder;
    private readonly SemanticVectorReader? _vectors;
    public IReadOnlyDictionary<string, string> Stamp { get; }
    public string Collection => Stamp.GetValueOrDefault("collection", "?");
    public bool SignatureValid { get; }
    public bool HybridReady => _encoder is not null && _vectors is not null;

    private LexIndexReader(SqliteConnection conn, Dictionary<string, string> stamp, string schema,
                           ITextEncoder? encoder, SemanticVectorReader? vectors)
    {
        _conn = conn;
        Stamp = stamp;
        _schema = schema;
        _encoder = encoder;
        _vectors = vectors;
        SignatureValid = stamp.ContainsKey("signature") && StampSigner.Verify(stamp);
    }

    private bool IsV3 => _schema == IndexBuilder.SchemaVersion;

    public static LexIndexReader Open(string dbPath, ITextEncoder? encoder = null, string? vectorPath = null)
    {
        var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var stamp = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT k, v FROM stamp";
            using var r = cmd.ExecuteReader();
            while (r.Read()) stamp[r.GetString(0)] = r.GetString(1);
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
            var missingV3Docs = new[] { "hierarchy", "domains", "act_form", "binding_status", "consolidation_status" }
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

        SemanticVectorReader? vectors = null;
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
                SELECT
                  (SELECT COUNT(*) FROM semantic_chunks
                     WHERE vector_ordinal < 0 OR vector_ordinal >= $vector_count),
                  (SELECT COUNT(DISTINCT vector_ordinal) FROM semantic_chunks)
                """;
            mapping.Parameters.AddWithValue("$vector_count", vectors.Count);
            using var mappingReader = mapping.ExecuteReader();
            if (!mappingReader.Read())
                throw new InvalidDataException("The semantic vector mapping cannot be read.");
            if (mappingReader.GetInt64(0) != 0)
                throw new InvalidDataException("The semantic vector mapping contains an invalid ordinal.");
            if (mappingReader.GetInt64(1) != vectors.Count)
                throw new InvalidDataException("The semantic vector file contains an unmapped record.");
        }
        return new LexIndexReader(conn, stamp, schema!, encoder, vectors);
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

    /// <summary>
    /// Every distinct version address this index can serve, for the sitemap.
    ///
    /// One row per (collection, work, valid_from), which is exactly the set of canonical version
    /// URLs: a request for any date inside an interval renders that version and canonicalises to
    /// the date the version starts. Grouped rather than selected, because a work published in
    /// several languages has one row per language behind a single URL.
    ///
    /// One query rather than a Timeline call per work, which would be 1,409 round trips to build
    /// one file.
    /// </summary>
    public List<(string Collection, string GroupKey, string ValidFrom, string? LastObserved)> VersionPaths()
    {
        using var cmd = Cmd("""
            SELECT collection, group_key, valid_from, MAX(observed_from)
            FROM docs
            GROUP BY collection, group_key, valid_from
            ORDER BY group_key, valid_from
            """, []);
        var rows = new List<(string, string, string, string?)>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            rows.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2),
                      rd.IsDBNull(3) ? null : rd.GetString(3)));
        return rows;
    }

    /// <summary>In-force set computed from validity intervals at query time (never a stored flag).
    /// Deduplicated by group; deterministic (collection, group_key) ordering for stable cursors.</summary>
    public (List<DocRow> Rows, int TotalGroups) InForceOn(DateOnly date, FilterSet filters, int limit, int offset)
    {
        var (where, ps) = WithFilters(
            "valid_from <= $d AND (valid_to IS NULL OR valid_to >= $d) AND withdrawn = 0",
            filters, excludeAsOf: true);

        int total;
        using (var cnt = Cmd($"SELECT COUNT(DISTINCT group_key) FROM docs WHERE {where}", ps))
        {
            cnt.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
            total = Convert.ToInt32(cnt.ExecuteScalar());
        }

        using var cmd = Cmd($"""
            SELECT {SelectDocCols()} FROM docs d
            WHERE {where} AND valid_from = (
                SELECT MAX(d2.valid_from) FROM docs d2
                WHERE d2.group_key = d.group_key
                  AND d2.valid_from <= $d AND (d2.valid_to IS NULL OR d2.valid_to >= $d) AND d2.withdrawn = 0)
            ORDER BY collection, group_key, language
            LIMIT $lim OFFSET $off
            """, ps);
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$lim", limit);
        cmd.Parameters.AddWithValue("$off", offset);
        var rows = ReadAll(cmd);
        var deduped = rows.GroupBy(x => x.GroupKey).Select(g => g.OrderBy(x => x.Language).First()).ToList();
        return (deduped, total);
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
                   p.article_valid_from, p.work_title, p.text_md, p.text_sha,
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
                TextMd: r.GetString(37), TextSha: r.GetString(38));
            result.Add((ReadDoc(r), prov, r.IsDBNull(39) ? "" : r.GetString(39)));
        }
        return result;
    }

    private List<(DocRow Doc, ProvisionRow Prov, string Snippet)> SearchV3(string query, FilterSet filters, int limit)
    {
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false);
        using var cmd = Cmd($"""
            WITH matched AS (
              SELECT rowid AS state_id, bm25(fts, 10.0, 4.0, 6.0, 1.0) AS score
              FROM fts WHERE fts MATCH $q
            ), eligible AS (
              SELECT m.score, p.rid, p.seq, p.anchor, p.provision_id, p.ptype, p.num,
                     p.heading, p.path, p.article_valid_from, p.work_title, p.text_sha,
                     d.key AS doc_key,
                     ROW_NUMBER() OVER (PARTITION BY m.state_id ORDER BY d.valid_from DESC, p.rid) AS occurrence_rank
              FROM matched m
              JOIN provisions p ON p.state_id = m.state_id
              JOIN docs d ON d.rid = p.rid
              WHERE {where}
            )
            SELECT {SelectDocCols("d")},
                   e.rid, e.seq, e.anchor, e.provision_id, e.ptype, e.num, e.heading, e.path,
                   e.article_valid_from, e.work_title, e.text_sha,
                   b.encoding, b.original_size, b.payload
            FROM eligible e
            JOIN docs d ON d.key = e.doc_key AND d.rid = e.rid
            JOIN text_blobs b ON b.text_sha = e.text_sha
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
            var text = DecodeAndVerify(r.GetString(38), r.GetInt32(39), (byte[])r.GetValue(40), r.GetString(37));
            var prov = new ProvisionRow(
                r.GetString(27), r.GetInt32(28), r.GetString(29), r.GetString(30), r.GetString(31),
                r.IsDBNull(32) ? null : r.GetString(32), r.IsDBNull(33) ? null : r.GetString(33),
                r.IsDBNull(34) ? null : r.GetString(34), r.IsDBNull(35) ? null : r.GetString(35),
                r.IsDBNull(36) ? null : r.GetString(36), text, r.GetString(37));
            result.Add((ReadDoc(r), prov, MakeSnippet(text, query)));
        }
        return result;
    }

    public SearchExecution SearchHybrid(string query, FilterSet filters, int limit, bool fuzzyAuto = true)
    {
        if (_encoder is null || _vectors is null)
            return SearchKeyword(query, filters, limit, fuzzyAuto);

        var keyword = SearchKeyword(query, filters, 100, fuzzyAuto);
        if (keyword.Hits.FirstOrDefault()?.MatchReasons.Contains("exact_identifier") == true)
            return keyword;
        var lexical = keyword.Hits;
        var semantic = SearchSemantic(query, filters, 100);
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
        var hits = fused.Select(kv => kv.Value with { Score = scores[kv.Key] })
            .OrderByDescending(h => h.Score).ThenByDescending(h => h.Doc.ValidFrom, StringComparer.Ordinal)
            .Take(limit).ToList();
        return new SearchExecution("hybrid", hits, keyword.QueryExpansions);
    }

    public SearchExecution SearchKeyword(string query, FilterSet filters, int limit, bool fuzzyAuto)
    {
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

    private static bool IsExactLegalIdentifier(string query) =>
        System.Text.RegularExpressions.Regex.IsMatch(query.Trim(),
            "^(?:CELEX\\s*)?(?:[136][0-9]{4}[A-Z][0-9]{4}|1[0-9]{4}[A-Z]/TXT)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(query.Trim(), "^ECLI:[A-Z0-9:.]+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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

    private List<RetrievalHit> SearchSemantic(string query, FilterSet filters, int limit)
    {
        var queryVector = _encoder!.Encode(query, EmbeddingInputKind.Query);
        var binary = SemanticVectorReader.Binary(queryVector);
        var int8 = SemanticVectorReader.Int8(queryVector);
        var nearest = _vectors!.NearestByHamming(binary, Math.Max(2_000, limit * 20));
        if (nearest.Count == 0) return [];
        var hamming = nearest.ToDictionary(candidate => candidate.Ordinal, candidate => candidate.Distance);
        // Ordinals originate in the verified vector file, not user input. A literal IN list lets
        // SQLite scan semantic_chunks once even for an older v3 artifact that predates the
        // vector-ordinal index; future builds use ix_semantic_vector directly.
        var ordinalSql = string.Join(',', nearest.Select(candidate => candidate.Ordinal));
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false);
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
            var provision = Provision(candidate.Rid, candidate.Anchor);
            if (doc is null || provision is null) continue;
            hits.Add(new RetrievalHit(doc, provision, MakeSnippet(provision.TextMd, query),
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

        var hits = Lookup(terms.Select((t, i) =>
            $"(d.title LIKE $t{i} OR d.group_key LIKE $t{i} OR d.group_identifier LIKE $t{i})"), terms);
        if (hits.Count > 0) return hits;

        // "CELEX 32022R2554", "see 32013r0575" — keep the identifier, drop the chatter.
        var distinctive = terms.OrderByDescending(t => t.Length).First();
        return distinctive.Length >= 5 ? Lookup(["(d.group_key LIKE $t0)"], [distinctive]) : [];

        List<DocRow> Lookup(IEnumerable<string> clauses, IReadOnlyList<string> values)
        {
            var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false);
            using var cmd = Cmd($"""
                SELECT {SelectDocCols("d")}
                FROM docs d
                WHERE {where} AND {string.Join(" AND ", clauses)}
                ORDER BY d.valid_from DESC
                LIMIT $lim
                """, ps);
            for (var i = 0; i < values.Count; i++) cmd.Parameters.AddWithValue($"$t{i}", "%" + values[i] + "%");
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
                                           bool byChurn, int limit, int offset = 0)
    {
        var where = "d.withdrawn=0 AND d.valid_from >= $from AND d.valid_from <= $to"
                    + KindClause(kinds, "d.");
        var order = byChurn ? "versions DESC, last_change DESC" : "last_change DESC, versions DESC";
        using var cmd = Cmd($"""
            SELECT d.group_key,
                   COUNT(DISTINCT d.valid_from) AS versions,
                   MIN(d.valid_from) AS first_change,
                   MAX(d.valid_from) AS last_change,
                   (SELECT COALESCE(t.title_short, t.title) FROM docs t WHERE t.group_key = d.group_key
                     ORDER BY t.valid_from DESC LIMIT 1) AS title,
                   (SELECT COUNT(DISTINCT t2.valid_from) FROM docs t2 WHERE t2.group_key = d.group_key) AS versions_total,
                   -- The state this law was in before the window touched it: the newest version
                   -- strictly older than the window's first change. This is what "what changed"
                   -- has to compare against; comparing first_change with last_change is a
                   -- comparison with itself whenever a work moved exactly once.
                   (SELECT MAX(t3.valid_from) FROM docs t3
                     WHERE t3.group_key = d.group_key AND t3.valid_from < MIN(d.valid_from)) AS baseline,
                   0 AS distinct_texts
            FROM docs d
            WHERE {where}
            GROUP BY d.group_key
            ORDER BY {order}
            LIMIT $lim OFFSET $off
            """, []);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);
        BindKinds(cmd, kinds);
        cmd.Parameters.AddWithValue("$lim", limit);
        cmd.Parameters.AddWithValue("$off", Math.Max(0, offset));
        var list = new List<ChangeRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ChangeRow(r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6)));
        r.Close();
        // Answered per row rather than in the aggregate above, because it is a question about
        // the PROVISIONS and the aggregate is a question about versions. Two small indexed reads
        // per row; the report returns at most a page of them.
        return list.Select(c => c with
        {
            DistinctTexts = DistinctWordings(c.GroupKey, c.Baseline ?? c.FirstChange, c.LastChange),
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
    {
        var a = WordingDigest(work, from);
        var b = WordingDigest(work, to);
        if (a is null && b is null) return 0;
        return a == b ? 1 : 2;
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
    public (int Works, int Versions) ChangeTotals(string from, string to, IReadOnlyList<string>? kinds)
    {
        var where = "valid_from >= $from AND valid_from <= $to" + KindClause(kinds, "");
        using var cmd = Cmd($"SELECT COUNT(DISTINCT group_key), COUNT(DISTINCT group_key || valid_from) FROM docs WHERE {where}", []);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);
        BindKinds(cmd, kinds);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt32(0), r.GetInt32(1)) : (0, 0);
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
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public List<EventRow> Events(string key)
    {
        using var cmd = Cmd("SELECT key, scope, event, observed_from, detail FROM events WHERE key=$k ORDER BY observed_from", []);
        cmd.Parameters.AddWithValue("$k", key);
        var list = new List<EventRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new EventRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    public List<ObservationRow> Observations(string key, string? language)
    {
        using var cmd = Cmd("""
            SELECT key, language, expr_valid_from, sha256, source_uri, observed_from, observed_to
            FROM obs_history WHERE key=$k AND ($l IS NULL OR language=$l)
            ORDER BY language, expr_valid_from, observed_from
            """, []);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$l", (object?)language ?? DBNull.Value);
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

    public List<DocRow> GroupsPage(int limit, int offset, FilterSet filters)
    {
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: true);
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
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false);
        // A GROUP BY carrying more than one MIN/MAX leaves SQLite's bare columns undefined, so the
        // title and type come from a window function pinned to the newest surviving version rather
        // than from whichever row the aggregate happened to walk last.
        var ctes = $"""
            WITH f AS (SELECT * FROM docs WHERE {where}),
                 agg AS (SELECT group_key, COUNT(*) AS versions, MIN(valid_from) AS first_from,
                                MAX(valid_from) AS last_from, MAX(text_public) AS has_text,
                                -- When we last SAW a change for this work. valid_from is when a
                                -- law takes effect, which is legitimately in the future for a
                                -- deferred commencement; observed_from is when the record entered
                                -- the corpus, which is the only one of the two that can serve as
                                -- a last-modified time for the page that renders it.
                                MAX(observed_from) AS last_observed
                         FROM f GROUP BY group_key),
                 newest AS (SELECT group_key, collection, title, title_short, kind,
                                   ROW_NUMBER() OVER (PARTITION BY group_key
                                                      ORDER BY valid_from DESC, key DESC) AS rn
                            FROM f)
            """;
        var having = hasText switch { true => " AND a.has_text = 1", false => " AND a.has_text = 0", _ => "" };
        var orderBy = order switch
        {
            CatalogueOrder.MostVersions => "a.versions DESC, n.group_key ASC",
            CatalogueOrder.MostRecent => "a.last_from DESC, n.group_key ASC",
            CatalogueOrder.Oldest => "a.first_from ASC, n.group_key ASC",
            _ => "n.group_key ASC",
        };

        using var count = Cmd($"{ctes} SELECT COUNT(*) FROM agg a WHERE 1=1{having}", ps);
        var total = Convert.ToInt32(count.ExecuteScalar());

        using var cmd = Cmd($"""
            {ctes}
            SELECT n.collection, n.group_key, n.title, n.title_short, n.kind,
                   a.versions, a.first_from, a.last_from, a.has_text, a.last_observed
            FROM agg a JOIN newest n ON n.group_key = a.group_key AND n.rn = 1
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
                rd.GetInt32(5), rd.GetString(6), rd.GetString(7), rd.GetInt32(8) == 1,
                rd.IsDBNull(9) ? null : rd.GetString(9)));
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
    public List<(string Slug, string Href, string? Label)> CitationsOf(string rid, string anchor)
    {
        using var cmd = Cmd("SELECT cited_slug, href, label FROM citations WHERE rid=$r AND anchor=$a", []);
        cmd.Parameters.AddWithValue("$r", rid);
        cmd.Parameters.AddWithValue("$a", anchor);
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
    public List<(string GroupKey, string ValidFrom, string Anchor, string? Num, string? Title)> CitedBy(
        string slug, int limit)
    {
        using var cmd = Cmd("""
            SELECT DISTINCT d.group_key, d.valid_from, c.anchor, p.num,
                   COALESCE(d.title_short, d.title)
            FROM citations c
            JOIN docs d ON d.rid = c.rid
            LEFT JOIN provisions p ON p.rid = c.rid AND p.anchor = c.anchor
            WHERE c.cited_slug = $s AND d.withdrawn = 0
            ORDER BY d.valid_from DESC, d.group_key, c.anchor
            LIMIT $lim
            """, []);
        cmd.Parameters.AddWithValue("$s", slug);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<(string, string, string, string?, string?)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.GetString(2),
                      r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    public CoverageInfo Coverage()
    {
        var kinds = new List<CoverageKind>();
        using (var cmd = Cmd("""
            SELECT kind, COUNT(*), SUM(CASE WHEN text_public=1 THEN 1 ELSE 0 END)
            FROM docs WHERE withdrawn=0 GROUP BY kind ORDER BY COUNT(*) DESC
            """, []))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                kinds.Add(new CoverageKind(r.IsDBNull(0) ? null : r.GetString(0),
                                           r.GetInt32(1), r.IsDBNull(2) ? 0 : r.GetInt32(2)));

        // lex-index/2: text_public is set only when a derived (provision-bearing) version exists
        using var agg = Cmd("""
            SELECT COUNT(DISTINCT group_key), COUNT(*), MIN(valid_from), MAX(valid_from),
                   SUM(CASE WHEN text_public=1 THEN 1 ELSE 0 END)
            FROM docs WHERE withdrawn=0
            """, []);
        using var ar = agg.ExecuteReader();
        ar.Read();
        var profiles = new List<CoverageProfile>();
        using (var pc = Cmd("""
            SELECT profile, COUNT(*) FROM docs WHERE profile IS NOT NULL AND withdrawn=0
            GROUP BY profile ORDER BY COUNT(*) DESC
            """, []))
        using (var pr = pc.ExecuteReader())
            while (pr.Read()) profiles.Add(new CoverageProfile(pr.GetString(0), pr.GetInt32(1)));

        // Which languages the corpus is actually in. This decides a real design question and was
        // being answered by guesswork: a site-wide language picker only makes sense if the same
        // law exists in several languages, and here it almost never does. Luxembourg publishes in
        // French, the EU acts held here are English, and a reader who picked one would lose the
        // other entirely rather than see a translation.
        var languages = new List<CoverageLanguage>();
        using (var lc = Cmd("""
            SELECT language, COUNT(DISTINCT group_key), COUNT(*) FROM docs WHERE withdrawn=0
            GROUP BY language ORDER BY COUNT(*) DESC
            """, []))
        using (var lr = lc.ExecuteReader())
            while (lr.Read()) languages.Add(new CoverageLanguage(lr.GetString(0), lr.GetInt32(1), lr.GetInt32(2)));

        int multilingual;
        using (var mc = Cmd("""
            SELECT COUNT(*) FROM (
              SELECT group_key FROM docs WHERE withdrawn=0
              GROUP BY group_key HAVING COUNT(DISTINCT language) > 1)
            """, []))
            multilingual = Convert.ToInt32(mc.ExecuteScalar());

        return new CoverageInfo(Collection, ar.GetInt32(0), ar.GetInt32(1),
            ar.IsDBNull(2) ? null : ar.GetString(2), ar.IsDBNull(3) ? null : ar.GetString(3), kinds, Stamp,
            ar.IsDBNull(4) ? 0 : ar.GetInt32(4), profiles, languages, multilingual);
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

    private (string Sql, List<SqliteParameter> Ps) WithFilters(string baseSql, FilterSet f, bool excludeAsOf)
    {
        var ps = new List<SqliteParameter>();
        // Withdrawn publisher records remain addressable by exact provenance tools, but they
        // are not eligible public-search or catalogue candidates after their tombstone.
        var sql = baseSql + " AND withdrawn=0";
        if (f.Collection is not null) { sql += " AND collection=$fcol"; ps.Add(new SqliteParameter("$fcol", f.Collection)); }
        if (f.Kind is not null)
        {
            var kinds = f.Kind.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var negate = kinds.Length > 0 && kinds.All(k => k.StartsWith('!'));
            if (kinds.Length > 0)
            {
                var names = kinds.Select((_, i) => $"$fkind{i}").ToList();
                sql += negate
                    ? $" AND (kind IS NULL OR kind NOT IN ({string.Join(',', names)}))"
                    : $" AND kind IN ({string.Join(',', names)})";
                for (var i = 0; i < kinds.Length; i++)
                    ps.Add(new SqliteParameter($"$fkind{i}", kinds[i].TrimStart('!')));
            }
        }
        if (f.Language is not null) { sql += " AND language=$flang"; ps.Add(new SqliteParameter("$flang", f.Language)); }
        if (f.Hierarchy is not null || f.ActForm is not null || f.BindingStatus is not null || f.Domain is not null)
        {
            if (!IsV3) return (sql + " AND 0=1", ps);
            if (f.Hierarchy is not null) { sql += " AND hierarchy=$fhier"; ps.Add(new SqliteParameter("$fhier", f.Hierarchy)); }
            if (f.ActForm is not null) { sql += " AND act_form=$fform"; ps.Add(new SqliteParameter("$fform", f.ActForm)); }
            if (f.BindingStatus is not null) { sql += " AND binding_status=$fbind"; ps.Add(new SqliteParameter("$fbind", f.BindingStatus)); }
            if (f.Domain is not null) { sql += " AND ('|' || domains || '|') LIKE $fdomain"; ps.Add(new SqliteParameter("$fdomain", "%|" + f.Domain + "|%")); }
        }
        if (f.Works is { Count: > 0 } works)
        {
            // Matches either the slug or the publisher's own identifier, so a caller can scope
            // with whichever of the two it happens to be holding.
            var names = works.Select((_, i) => $"$fw{i}").ToList();
            sql += $" AND (group_key IN ({string.Join(",", names)}) OR group_identifier IN ({string.Join(",", names)}))";
            for (var i = 0; i < works.Count; i++) ps.Add(new SqliteParameter($"$fw{i}", works[i]));
        }
        if (!excludeAsOf && f.AsOf is { } d)
        {
            sql += " AND valid_from <= $fasof AND (valid_to IS NULL OR valid_to >= $fasof)";
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
    public List<ProvisionRow> Provisions(string rid)
    {
        if (IsV3) return ProvisionsV3(rid);
        using var cmd = Cmd("""
            SELECT rid, seq, anchor, provision_id, ptype, num, heading, path, article_valid_from,
                   work_title, text_md, text_sha
            FROM provisions WHERE rid=$rid ORDER BY seq
            """, []);
        cmd.Parameters.AddWithValue("$rid", rid);
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

    private List<ProvisionRow> ProvisionsV3(string rid)
    {
        using var cmd = Cmd("""
            SELECT p.rid, p.seq, p.anchor, p.provision_id, p.ptype, p.num, p.heading, p.path,
                   p.article_valid_from, p.work_title, p.text_sha,
                   b.encoding, b.original_size, b.payload
            FROM provisions p JOIN text_blobs b ON b.text_sha=p.text_sha
            WHERE p.rid=$rid ORDER BY p.seq
            """, []);
        cmd.Parameters.AddWithValue("$rid", rid);
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
        => Provisions(rid).FirstOrDefault(p => p.Anchor == anchor);

    /// <summary>Every distinct text a provision has had, as validity intervals (the time axis).</summary>
    public List<ProvisionStateRow> ProvisionStates(string work, string anchor, string? language = null)
    {
        var normalizedWork = NormalizeWork(work);
        if (IsV3) language ??= PreferredHistoryLanguage(normalizedWork, anchor);
        using var cmd = Cmd(IsV3 ? """
            SELECT group_key, language, is_primary_language, anchor, valid_from, valid_to, text_sha, in_version,
                   article_valid_from, validity_conflict
            FROM provision_states
            WHERE group_key=$w AND anchor=$a AND language=$lang
            ORDER BY valid_from
            """ : """
            SELECT group_key, anchor, valid_from, valid_to, text_sha, in_version,
                   article_valid_from, validity_conflict
            FROM provision_states WHERE group_key=$w AND anchor=$a ORDER BY valid_from
            """, []);
        cmd.Parameters.AddWithValue("$w", normalizedWork);
        cmd.Parameters.AddWithValue("$a", anchor);
        if (IsV3) cmd.Parameters.AddWithValue("$lang", language ?? "und");
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

    /// <summary>Anchor lifecycle events for a work; optionally only those touching one anchor.</summary>
    public List<AnchorEventRow> AnchorEvents(string work, string? anchor = null, string? language = null)
    {
        var normalizedWork = NormalizeWork(work);
        if (IsV3) language ??= PreferredHistoryLanguage(normalizedWork, anchor);
        using var cmd = Cmd(IsV3 ? """
            SELECT group_key, language, is_primary_language, etype, from_anchor, to_anchor, anchor, text_sha, at_version
            FROM anchor_events WHERE group_key=$w AND language=$lang
              AND ($a IS NULL OR from_anchor=$a OR to_anchor=$a OR anchor=$a)
            ORDER BY at_version
            """ : """
            SELECT group_key, etype, from_anchor, to_anchor, anchor, text_sha, at_version
            FROM anchor_events WHERE group_key=$w
              AND ($a IS NULL OR from_anchor=$a OR to_anchor=$a OR anchor=$a)
            ORDER BY at_version
            """, []);
        cmd.Parameters.AddWithValue("$w", normalizedWork);
        cmd.Parameters.AddWithValue("$a", (object?)anchor ?? DBNull.Value);
        if (IsV3) cmd.Parameters.AddWithValue("$lang", language ?? "und");
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
        _vectors?.Dispose();
        _conn.Dispose();
    }
}
