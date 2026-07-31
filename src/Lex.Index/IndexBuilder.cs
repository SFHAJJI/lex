using Microsoft.Data.Sqlite;

namespace Lex.Index;

/// <summary>
/// Builds one index file per collection. Time enters as an injected parameter (F9);
/// no ambient clock is read anywhere in this class.
/// </summary>
public static class IndexBuilder
{
    public const string SchemaVersion = "lex-index/1";

    public static void Build(
        string dbPath,
        IReadOnlyDictionary<string, string> stampValues,
        IEnumerable<DocRow> docs,
        IEnumerable<EventRow> events,
        IEnumerable<ObservationRow> observations,
        string? signingKeyPem)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        Exec(conn, """
            CREATE TABLE docs(
              key TEXT NOT NULL, collection TEXT NOT NULL, group_key TEXT NOT NULL,
              group_identifier TEXT NOT NULL, kind TEXT, language TEXT NOT NULL,
              valid_from TEXT NOT NULL, valid_to TEXT, valid_time_source TEXT NOT NULL,
              observed_from TEXT NOT NULL, withdrawn INTEGER NOT NULL,
              text_available INTEGER NOT NULL, text_public INTEGER NOT NULL,
              record_sha TEXT, body_sha TEXT, source_uri TEXT,
              title TEXT, title_short TEXT, body TEXT,
              publication_date TEXT, status_note TEXT, rid TEXT NOT NULL,
              PRIMARY KEY(key, language, valid_from));
            CREATE INDEX ix_docs_group ON docs(group_key, valid_from);
            CREATE INDEX ix_docs_stab ON docs(collection, kind, valid_from, valid_to);
            CREATE INDEX ix_docs_rid ON docs(rid);
            CREATE TABLE events(key TEXT, scope TEXT, event TEXT, observed_from TEXT, detail TEXT);
            CREATE INDEX ix_events_key ON events(key);
            CREATE TABLE obs_history(key TEXT, language TEXT, expr_valid_from TEXT,
              sha256 TEXT, source_uri TEXT, observed_from TEXT, observed_to TEXT);
            CREATE INDEX ix_obs_key ON obs_history(key, language, expr_valid_from);
            CREATE TABLE stamp(k TEXT PRIMARY KEY, v TEXT NOT NULL);
            CREATE VIRTUAL TABLE fts USING fts5(rid UNINDEXED, title, title_short, body);
            """);

        using (var tx = conn.BeginTransaction())
        {
            var insDoc = conn.CreateCommand();
            insDoc.CommandText = """
                INSERT OR REPLACE INTO docs VALUES ($key,$col,$gk,$gi,$kind,$lang,$vf,$vt,$vts,$of,$wd,$ta,$tp,$rs,$bs,$su,$t,$ts2,$b,$pd,$sn,$rid)
                """;
            foreach (var p in new[] { "$key", "$col", "$gk", "$gi", "$kind", "$lang", "$vf", "$vt", "$vts", "$of", "$wd", "$ta", "$tp", "$rs", "$bs", "$su", "$t", "$ts2", "$b", "$pd", "$sn", "$rid" })
                insDoc.Parameters.Add(new SqliteParameter(p, SqliteType.Text));

            var insFts = conn.CreateCommand();
            insFts.CommandText = "INSERT INTO fts(rid,title,title_short,body) VALUES ($rid,$t,$ts2,$b)";
            foreach (var p in new[] { "$rid", "$t", "$ts2", "$b" })
                insFts.Parameters.Add(new SqliteParameter(p, SqliteType.Text));

            foreach (var d in docs)
            {
                var rid = $"{d.Key}|{d.Language}|{d.ValidFrom}";
                Set(insDoc, "$key", d.Key); Set(insDoc, "$col", d.Collection); Set(insDoc, "$gk", d.GroupKey);
                Set(insDoc, "$gi", d.GroupIdentifier); Set(insDoc, "$kind", d.Kind); Set(insDoc, "$lang", d.Language);
                Set(insDoc, "$vf", d.ValidFrom); Set(insDoc, "$vt", d.ValidTo); Set(insDoc, "$vts", d.ValidTimeSource);
                Set(insDoc, "$of", d.ObservedFrom); Set(insDoc, "$wd", d.Withdrawn ? "1" : "0");
                Set(insDoc, "$ta", d.TextAvailable ? "1" : "0"); Set(insDoc, "$tp", d.TextPublic ? "1" : "0");
                Set(insDoc, "$rs", d.RecordSha); Set(insDoc, "$bs", d.BodySha); Set(insDoc, "$su", d.SourceUri);
                Set(insDoc, "$t", d.Title); Set(insDoc, "$ts2", d.TitleShort); Set(insDoc, "$b", d.Body);
                Set(insDoc, "$pd", d.PublicationDate); Set(insDoc, "$sn", d.StatusNote); Set(insDoc, "$rid", rid);
                insDoc.ExecuteNonQuery();

                Set(insFts, "$rid", rid); Set(insFts, "$t", d.Title); Set(insFts, "$ts2", d.TitleShort); Set(insFts, "$b", d.Body);
                insFts.ExecuteNonQuery();
            }

            var insEv = conn.CreateCommand();
            insEv.CommandText = "INSERT INTO events VALUES ($key,$scope,$event,$of,$detail)";
            foreach (var p in new[] { "$key", "$scope", "$event", "$of", "$detail" })
                insEv.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            foreach (var e in events)
            {
                Set(insEv, "$key", e.Key); Set(insEv, "$scope", e.Scope); Set(insEv, "$event", e.Event);
                Set(insEv, "$of", e.ObservedFrom); Set(insEv, "$detail", e.Detail);
                insEv.ExecuteNonQuery();
            }

            var insObs = conn.CreateCommand();
            insObs.CommandText = "INSERT INTO obs_history VALUES ($key,$lang,$evf,$sha,$su,$of,$ot)";
            foreach (var p in new[] { "$key", "$lang", "$evf", "$sha", "$su", "$of", "$ot" })
                insObs.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            foreach (var o in observations)
            {
                Set(insObs, "$key", o.Key); Set(insObs, "$lang", o.Language); Set(insObs, "$evf", o.ExprValidFrom);
                Set(insObs, "$sha", o.Sha256); Set(insObs, "$su", o.SourceUri); Set(insObs, "$of", o.ObservedFrom);
                Set(insObs, "$ot", o.ObservedTo);
                insObs.ExecuteNonQuery();
            }

            var stamp = new Dictionary<string, string>(stampValues) { ["schema"] = SchemaVersion, ["algorithm"] = StampSigner.Algorithm };
            if (signingKeyPem is not null)
            {
                var (sig, pub) = StampSigner.Sign(stamp, signingKeyPem);
                stamp["signature"] = sig;
                stamp["public_key"] = pub;
            }
            var insStamp = conn.CreateCommand();
            insStamp.CommandText = "INSERT INTO stamp VALUES ($k,$v)";
            insStamp.Parameters.Add(new SqliteParameter("$k", SqliteType.Text));
            insStamp.Parameters.Add(new SqliteParameter("$v", SqliteType.Text));
            foreach (var (k, v) in stamp)
            {
                insStamp.Parameters["$k"].Value = k; insStamp.Parameters["$v"].Value = v;
                insStamp.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Set(SqliteCommand cmd, string name, string? value) =>
        cmd.Parameters[name].Value = (object?)value ?? DBNull.Value;
}
