using Microsoft.Data.Sqlite;

namespace Lex.Index;

/// Per document type: how many versions are held, and how many of them carry text. The second
/// number is the honest one. A source may serve a version only in a format that has no article
/// structure; such a version is held as a complete dated record with no wording (D49).
public sealed record CoverageKind(string? Kind, int Versions, int WithText);

/// How many versions each extraction profile produced text for. Publishes the confidence mix
/// of the corpus as a number rather than as a claim.
public sealed record CoverageProfile(string Profile, int Versions);

public sealed record CoverageInfo(
    string Collection,
    int Groups,
    int Rows,
    string? EarliestValidFrom,
    string? LatestValidFrom,
    IReadOnlyList<CoverageKind> Kinds,
    IReadOnlyDictionary<string, string> Stamp,
    int TextServed,
    IReadOnlyList<CoverageProfile> Profiles);

/// <summary>
/// Read side of one index file. Every query method takes a non-optional FilterSet (F5);
/// filters are applied as SQL predicates before any ranking or ordering.
/// </summary>
public sealed class LexIndexReader : IDisposable
{
    private readonly SqliteConnection _conn;
    public IReadOnlyDictionary<string, string> Stamp { get; }
    public string Collection => Stamp.GetValueOrDefault("collection", "?");
    public bool SignatureValid { get; }

    private LexIndexReader(SqliteConnection conn, Dictionary<string, string> stamp)
    {
        _conn = conn;
        Stamp = stamp;
        SignatureValid = stamp.ContainsKey("signature") && StampSigner.Verify(stamp);
    }

    public static LexIndexReader Open(string dbPath)
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
        if (stamp.GetValueOrDefault("schema") != IndexBuilder.SchemaVersion)
            throw new InvalidOperationException(
                $"Index schema '{stamp.GetValueOrDefault("schema")}' is not '{IndexBuilder.SchemaVersion}'. Refusing to open {dbPath}.");

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

        return new LexIndexReader(conn, stamp);
    }

    private const string DocCols = """
        key, collection, group_key, group_identifier, kind, language, valid_from, valid_to,
        valid_time_source, observed_from, withdrawn, text_available, text_public,
        record_sha, body_sha, source_uri, title, title_short, publication_date, status_note, rid,
        profile
        """;

    /// <summary>True if the work exists at all (distinguishes unknown_work from no_version_for_date).</summary>
    public bool WorkExists(string work)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM docs WHERE group_key=$w OR group_identifier=$w OR key LIKE $p LIMIT 1";
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        cmd.Parameters.AddWithValue("$p", NormalizeWork(work) + ":%");
        return cmd.ExecuteScalar() is not null;
    }

    public DocRow? AsOf(string work, DateOnly date, FilterSet filters)
    {
        var (sql, ps) = WithFilters($"""
            SELECT {DocCols} FROM docs
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
            SELECT {DocCols} FROM docs WHERE group_key=$w OR group_identifier=$w
            ORDER BY valid_from, language
            """, []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        return ReadAll(cmd);
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
            SELECT {DocCols} FROM docs d
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

    private const string DocColsQualified = """
        d.key, d.collection, d.group_key, d.group_identifier, d.kind, d.language, d.valid_from, d.valid_to,
        d.valid_time_source, d.observed_from, d.withdrawn, d.text_available, d.text_public,
        d.record_sha, d.body_sha, d.source_uri, d.title, d.title_short, d.publication_date, d.status_note, d.rid
        """;

    public List<(DocRow Doc, ProvisionRow Prov, string Snippet)> Search(string query, FilterSet filters, int limit)
    {
        // Filters first (F5): SQL predicates restrict the candidate set; only survivors are
        // ranked by bm25 (weights: work title > heading > num > body text). Hits are
        // provision-level: the retrieval unit is the article, not the document.
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false);
        using var cmd = Cmd($"""
            SELECT {DocColsQualified},
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
            // DocCols occupies ordinals 0..20 (incl. rid); provision cols follow at 21..32; snippet last.
            var prov = new ProvisionRow(
                Rid: r.GetString(21), Seq: r.GetInt32(22), Anchor: r.GetString(23), ProvisionId: r.GetString(24),
                PType: r.GetString(25), Num: r.IsDBNull(26) ? null : r.GetString(26),
                Heading: r.IsDBNull(27) ? null : r.GetString(27), Path: r.IsDBNull(28) ? null : r.GetString(28),
                ArticleValidFrom: r.IsDBNull(29) ? null : r.GetString(29),
                WorkTitle: r.IsDBNull(30) ? null : r.GetString(30),
                TextMd: r.GetString(31), TextSha: r.GetString(32));
            result.Add((ReadDoc(r), prov, r.IsDBNull(33) ? "" : r.GetString(33)));
        }
        return result;
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

        var hits = Lookup(terms.Select((t, i) => $"(d.title LIKE $t{i} OR d.group_key LIKE $t{i})"), terms);
        if (hits.Count > 0) return hits;

        // "CELEX 32022R2554", "see 32013r0575" — keep the identifier, drop the chatter.
        var distinctive = terms.OrderByDescending(t => t.Length).First();
        return distinctive.Length >= 5 ? Lookup(["(d.group_key LIKE $t0)"], [distinctive]) : [];

        List<DocRow> Lookup(IEnumerable<string> clauses, IReadOnlyList<string> values)
        {
            var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false);
            using var cmd = Cmd($"""
                SELECT {DocColsQualified}
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
        var where = "d.valid_from >= $from AND d.valid_from <= $to" + KindClause(kinds, "d.");
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
    /// How many distinct wordings a work had between two dates, counted from the ordered
    /// per-provision hashes rather than from the file hash.
    ///
    /// The file hash was the obvious signal and the wrong one: a consolidated document carries a
    /// header naming the date it was produced, so a pure reissue changes the file while every
    /// article stays identical. The report would then promise a change and hand the reader a
    /// comparison reading "0 changed, 0 added, 0 removed", which is how correct software gets
    /// mistaken for broken software. Provisions are what the comparison actually shows, so
    /// provisions are what gets counted.
    /// </summary>
    public int DistinctWordings(string work, string from, string to)
    {
        using var cmd = Cmd($"""
            SELECT d.rid FROM docs d
            WHERE (d.group_key=$w OR d.group_identifier=$w)
              AND d.valid_from >= $from AND d.valid_from <= $to
            GROUP BY d.valid_from
            ORDER BY d.valid_from
            """, []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);
        var rids = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) rids.Add(r.GetString(0));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rid in rids)
        {
            using var q = Cmd("SELECT text_sha FROM provisions WHERE rid=$r ORDER BY seq", []);
            q.Parameters.AddWithValue("$r", rid);
            var sb = new System.Text.StringBuilder();
            using var rr = q.ExecuteReader();
            while (rr.Read()) sb.Append(rr.GetString(0));
            // A version with no provisions holds no wording; it must not count as one more.
            if (sb.Length > 0) seen.Add(sb.ToString());
        }
        return seen.Count;
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
        using var cmd = Cmd($"SELECT {DocCols} FROM docs WHERE key=$k ORDER BY language LIMIT 1", []);
        cmd.Parameters.AddWithValue("$k", key);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadDoc(r) : null;
    }

    public List<DocRow> GroupsPage(int limit, int offset, FilterSet filters)
    {
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: true);
        using var cmd = Cmd($"""
            SELECT {DocCols} FROM docs d
            WHERE {where} AND valid_from = (SELECT MAX(valid_from) FROM docs d2 WHERE d2.group_key = d.group_key)
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
                                MAX(valid_from) AS last_from, MAX(text_public) AS has_text
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
                   a.versions, a.first_from, a.last_from, a.has_text
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
                rd.GetInt32(5), rd.GetString(6), rd.GetString(7), rd.GetInt32(8) == 1));
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
            WHERE {where} GROUP BY kind ORDER BY works DESC
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
            WHERE c.cited_slug = $s
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
            FROM docs GROUP BY kind ORDER BY COUNT(*) DESC
            """, []))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                kinds.Add(new CoverageKind(r.IsDBNull(0) ? null : r.GetString(0),
                                           r.GetInt32(1), r.IsDBNull(2) ? 0 : r.GetInt32(2)));

        // lex-index/2: text_public is set only when a derived (provision-bearing) version exists
        using var agg = Cmd("""
            SELECT COUNT(DISTINCT group_key), COUNT(*), MIN(valid_from), MAX(valid_from),
                   SUM(CASE WHEN text_public=1 THEN 1 ELSE 0 END)
            FROM docs
            """, []);
        using var ar = agg.ExecuteReader();
        ar.Read();
        var profiles = new List<CoverageProfile>();
        using (var pc = Cmd("""
            SELECT profile, COUNT(*) FROM docs WHERE profile IS NOT NULL
            GROUP BY profile ORDER BY COUNT(*) DESC
            """, []))
        using (var pr = pc.ExecuteReader())
            while (pr.Read()) profiles.Add(new CoverageProfile(pr.GetString(0), pr.GetInt32(1)));

        return new CoverageInfo(Collection, ar.GetInt32(0), ar.GetInt32(1),
            ar.IsDBNull(2) ? null : ar.GetString(2), ar.IsDBNull(3) ? null : ar.GetString(3), kinds, Stamp,
            ar.IsDBNull(4) ? 0 : ar.GetInt32(4), profiles);
    }

    private static string NormalizeWork(string work)
    {
        // Accepted forms (§9): work-level lex_id "<collection>:<groupkey>", version-level lex_id
        // "<collection>:<groupkey>:<vkey>" (version segment ignored), or a verbatim identifier/slug.
        if (!work.Contains("://") && work.Contains(':'))
        {
            var parts = work.Split(':');
            if (parts.Length >= 2) return parts[1];
        }
        return work;
    }

    private (string Sql, List<SqliteParameter> Ps) WithFilters(string baseSql, FilterSet f, bool excludeAsOf)
    {
        var ps = new List<SqliteParameter>();
        var sql = baseSql;
        if (f.Collection is not null) { sql += " AND collection=$fcol"; ps.Add(new SqliteParameter("$fcol", f.Collection)); }
        if (f.Kind is not null) { sql += " AND kind=$fkind"; ps.Add(new SqliteParameter("$fkind", f.Kind)); }
        if (f.Language is not null) { sql += " AND language=$flang"; ps.Add(new SqliteParameter("$flang", f.Language)); }
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

    private static string Fts5Escape(string q) =>
        string.Join(" ", q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => "\"" + t.Replace("\"", "") + "\""));

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
        Profile: r.FieldCount > 21 && !r.IsDBNull(21) ? r.GetString(21) : null);

    /// <summary>Rid of a doc row (key|language|valid_from) — the provisions foreign key.</summary>
    public static string RidOf(DocRow d) => $"{d.Key}|{d.Language}|{d.ValidFrom}";

    /// <summary>All provisions of one document version, in document order.</summary>
    public List<ProvisionRow> Provisions(string rid)
    {
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

    /// <summary>One provision of one document version, by anchor.</summary>
    public ProvisionRow? Provision(string rid, string anchor)
        => Provisions(rid).FirstOrDefault(p => p.Anchor == anchor);

    /// <summary>Every distinct text a provision has had, as validity intervals (the time axis).</summary>
    public List<ProvisionStateRow> ProvisionStates(string work, string anchor)
    {
        using var cmd = Cmd("""
            SELECT group_key, anchor, valid_from, valid_to, text_sha, in_version,
                   article_valid_from, validity_conflict
            FROM provision_states WHERE group_key=$w AND anchor=$a ORDER BY valid_from
            """, []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        cmd.Parameters.AddWithValue("$a", anchor);
        var list = new List<ProvisionStateRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new ProvisionStateRow(
            r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
            r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
            r.GetString(7) == "1" || (r.GetValue(7) is long l && l == 1)));
        return list;
    }

    /// <summary>Anchor lifecycle events for a work; optionally only those touching one anchor.</summary>
    public List<AnchorEventRow> AnchorEvents(string work, string? anchor = null)
    {
        using var cmd = Cmd("""
            SELECT group_key, etype, from_anchor, to_anchor, anchor, text_sha, at_version
            FROM anchor_events WHERE group_key=$w
              AND ($a IS NULL OR from_anchor=$a OR to_anchor=$a OR anchor=$a)
            ORDER BY at_version
            """, []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        cmd.Parameters.AddWithValue("$a", (object?)anchor ?? DBNull.Value);
        var list = new List<AnchorEventRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new AnchorEventRow(
            r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    /// <summary>True if the work has any provision-level history rows.</summary>
    public bool HasProvisionHistory(string work)
    {
        using var cmd = Cmd("SELECT 1 FROM provision_states WHERE group_key=$w LIMIT 1", []);
        cmd.Parameters.AddWithValue("$w", NormalizeWork(work));
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>Document text reconstructed from its provisions (lex-index/2 stores text once).</summary>
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

    public void Dispose() => _conn.Dispose();
}
