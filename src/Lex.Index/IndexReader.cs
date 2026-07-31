using Microsoft.Data.Sqlite;

namespace Lex.Index;

public sealed record CoverageKind(string? Kind, int Versions);

public sealed record CoverageInfo(
    string Collection,
    int Groups,
    int Rows,
    string? EarliestValidFrom,
    string? LatestValidFrom,
    IReadOnlyList<CoverageKind> Kinds,
    IReadOnlyDictionary<string, string> Stamp);

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
        return new LexIndexReader(conn, stamp);
    }

    private const string DocCols = """
        key, collection, group_key, group_identifier, kind, language, valid_from, valid_to,
        valid_time_source, observed_from, withdrawn, text_available, text_public,
        record_sha, body_sha, source_uri, title, title_short, body, publication_date, status_note
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
        sql += " ORDER BY valid_from DESC, language LIMIT 1";
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
            filters, excludeAsOf: true, bare: true);

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
        d.record_sha, d.body_sha, d.source_uri, d.title, d.title_short, d.body, d.publication_date, d.status_note
        """;

    public List<(DocRow Doc, string Snippet)> Search(string query, FilterSet filters, int limit)
    {
        // Filters first (F5): SQL predicates restrict the candidate set; only survivors are ranked by bm25.
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: false, bare: true);
        using var cmd = Cmd($"""
            SELECT {DocColsQualified}, snippet(fts, -1, '«', '»', ' … ', 14) AS snip
            FROM fts JOIN docs d ON d.rid = fts.rid
            WHERE fts MATCH $q AND {where}
            ORDER BY bm25(fts)
            LIMIT $lim
            """, ps);
        cmd.Parameters.AddWithValue("$q", Fts5Escape(query));
        cmd.Parameters.AddWithValue("$lim", limit);
        var result = new List<(DocRow, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add((ReadDoc(r), r.IsDBNull(21) ? "" : r.GetString(21)));
        return result;
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
        var (where, ps) = WithFilters("1=1", filters, excludeAsOf: true, bare: true);
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

    public CoverageInfo Coverage()
    {
        var kinds = new List<CoverageKind>();
        using (var cmd = Cmd("SELECT kind, COUNT(*) FROM docs GROUP BY kind ORDER BY COUNT(*) DESC", []))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) kinds.Add(new CoverageKind(r.IsDBNull(0) ? null : r.GetString(0), r.GetInt32(1)));

        using var agg = Cmd("SELECT COUNT(DISTINCT group_key), COUNT(*), MIN(valid_from), MAX(valid_from) FROM docs", []);
        using var ar = agg.ExecuteReader();
        ar.Read();
        return new CoverageInfo(Collection, ar.GetInt32(0), ar.GetInt32(1),
            ar.IsDBNull(2) ? null : ar.GetString(2), ar.IsDBNull(3) ? null : ar.GetString(3), kinds, Stamp);
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

    private (string Sql, List<SqliteParameter> Ps) WithFilters(string baseSql, FilterSet f, bool excludeAsOf, bool bare = false)
    {
        var ps = new List<SqliteParameter>();
        var sql = baseSql;
        if (f.Collection is not null) { sql += " AND collection=$fcol"; ps.Add(new SqliteParameter("$fcol", f.Collection)); }
        if (f.Kind is not null) { sql += " AND kind=$fkind"; ps.Add(new SqliteParameter("$fkind", f.Kind)); }
        if (f.Language is not null) { sql += " AND language=$flang"; ps.Add(new SqliteParameter("$flang", f.Language)); }
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
        TitleShort: r.IsDBNull(17) ? null : r.GetString(17), Body: r.IsDBNull(18) ? null : r.GetString(18),
        PublicationDate: r.IsDBNull(19) ? null : r.GetString(19), StatusNote: r.IsDBNull(20) ? null : r.GetString(20));

    public void Dispose() => _conn.Dispose();
}
