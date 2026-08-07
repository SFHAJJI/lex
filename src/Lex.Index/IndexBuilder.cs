using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lex.Index;

/// <summary>
/// Builds one index file per collection. Time enters as an injected parameter (F9);
/// no ambient clock is read anywhere in this class.
/// </summary>
public static class IndexBuilder
{
    public const string SchemaVersion = "lex-index/3";
    public const string PreviousSchemaVersion = "lex-index/2";

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
    private static void WriteCitations(SqliteConnection conn, ProvisionRow p)
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

    private static string ContentDigest(IEnumerable<DocRow> docs, IEnumerable<ProvisionRow> provisions)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var d in docs.OrderBy(d => d.Key, StringComparer.Ordinal).ThenBy(d => d.Language, StringComparer.Ordinal))
            sb.Append(d.Key).Append('|').Append(d.Language).Append('|').Append(d.ValidFrom).Append('|')
              .Append(d.ValidTo ?? "").Append('|').Append(d.RecordSha ?? "").Append('\n');
        foreach (var p in provisions.OrderBy(p => p.Rid, StringComparer.Ordinal).ThenBy(p => p.Seq))
            sb.Append(p.ProvisionId).Append('|')
              .Append(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(p.TextMd)))).Append('\n');
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
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
        SemanticBuildOptions? semantic = null)
    {
        var docRows = docs.ToList();
        var provisionRows = provisions.ToList();
        Dictionary<string, IReadOnlyList<SemanticChunk>>? chunksByTextSha = null;
        List<SemanticChunk>? uniqueSemanticChunks = null;
        long semanticVectorTotal = 0;
        if (semantic is not null)
        {
            if (semantic.BatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(semantic), "Semantic batch size must be positive.");
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
            foreach (var provision in uniqueTextRows)
            {
                var chunks = SemanticChunker.Split(provision.TextMd, semantic.Encoder);
                chunksByTextSha.Add(provision.TextSha, chunks);
                foreach (var chunk in chunks)
                    if (uniqueChunkHashes.Add(chunk.Sha256)) uniqueSemanticChunks.Add(chunk);
                preparationCompleted++;
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
        using var semanticWriter = semantic is null ? null
            : new SemanticVectorWriter(vectorTempPath!, semantic.Encoder.Dimensions);
        var vectorOrdinalByChunk = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
        if (semantic is not null)
        {
            foreach (var batch in uniqueSemanticChunks!.Chunk(semantic.BatchSize))
            {
                var vectors = semantic.Encoder.EncodeBatch(
                    batch.Select(chunk => chunk.Text).ToArray(), EmbeddingInputKind.Passage);
                if (vectors.Count != batch.Length)
                    throw new InvalidDataException("Embedding encoder returned the wrong batch size.");
                for (var i = 0; i < batch.Length; i++)
                {
                    var ordinal = semanticWriter!.Write(vectors[i]);
                    vectorOrdinalByChunk.Add(batch[i].Sha256, ordinal);
                    semanticVectorsCompleted++;
                }
                ReportProgress(semantic, SemanticBuildStage.Embeddings,
                    semanticVectorsCompleted, semanticVectorTotal, semanticProgressWatch,
                    ref lastReportedPercent, ref lastProgressReport,
                    force: semanticVectorsCompleted == semanticVectorTotal);
            }
        }
        if (File.Exists(dbPath)) File.Delete(dbPath);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // lex-index/3: an occurrence carries legal identity and dates, while wording lives once
        // in a content-addressed blob. Lexical state is also deduplicated across repeat versions.
        Exec(conn, """
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
            CREATE TABLE events(key TEXT, scope TEXT, event TEXT, observed_from TEXT, detail TEXT);
            CREATE INDEX ix_events_key ON events(key);
            CREATE TABLE obs_history(key TEXT, language TEXT, expr_valid_from TEXT,
              sha256 TEXT, source_uri TEXT, observed_from TEXT, observed_to TEXT);
            CREATE INDEX ix_obs_key ON obs_history(key, language, expr_valid_from);
            CREATE TABLE stamp(k TEXT PRIMARY KEY, v TEXT NOT NULL);
            CREATE VIRTUAL TABLE fts USING fts5(work_title, num, heading, text_md, content='');
            CREATE VIRTUAL TABLE fts_vocab USING fts5vocab(fts, 'row');
            """);

        using (var tx = conn.BeginTransaction())
        {
            var insDoc = conn.CreateCommand();
            insDoc.CommandText = """
                INSERT OR REPLACE INTO docs VALUES ($key,$col,$gk,$gi,$kind,$lang,$vf,$vt,$vts,$of,$wd,$ta,$tp,$rs,$bs,$su,$t,$ts2,$pd,$sn,$rid,$prof,$hier,$domains,$form,$binding,$consolidation)
                """;
            foreach (var p in new[] { "$key", "$col", "$gk", "$gi", "$kind", "$lang", "$vf", "$vt", "$vts", "$of", "$wd", "$ta", "$tp", "$rs", "$bs", "$su", "$t", "$ts2", "$pd", "$sn", "$rid", "$prof", "$hier", "$domains", "$form", "$binding", "$consolidation" })
                insDoc.Parameters.Add(new SqliteParameter(p, SqliteType.Text));

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
            }

            var docByRid = docRows.ToDictionary(d => $"{d.Key}|{d.Language}|{d.ValidFrom}", StringComparer.Ordinal);
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
                WriteCitations(conn, p);
            }

            var insState = conn.CreateCommand();
            insState.CommandText = "INSERT INTO provision_states VALUES ($gk,$lang,$primary,$a,$vf,$vt,$sha,$iv,$avf,$vc)";
            foreach (var p in new[] { "$gk", "$lang", "$primary", "$a", "$vf", "$vt", "$sha", "$iv", "$avf", "$vc" })
                insState.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            foreach (var s in provisionStates ?? [])
            {
                Set(insState, "$gk", s.GroupKey); Set(insState, "$lang", s.Language);
                Set(insState, "$primary", s.IsPrimaryLanguage ? "1" : "0");
                Set(insState, "$a", s.Anchor); Set(insState, "$vf", s.ValidFrom);
                Set(insState, "$vt", s.ValidTo); Set(insState, "$sha", s.TextSha); Set(insState, "$iv", s.InVersion);
                Set(insState, "$avf", s.ArticleValidFrom); Set(insState, "$vc", s.ValidityConflict ? "1" : "0");
                insState.ExecuteNonQuery();
            }

            var insAe = conn.CreateCommand();
            insAe.CommandText = "INSERT INTO anchor_events VALUES ($gk,$lang,$primary,$et,$fa,$ta,$a,$sha,$av)";
            foreach (var p in new[] { "$gk", "$lang", "$primary", "$et", "$fa", "$ta", "$a", "$sha", "$av" })
                insAe.Parameters.Add(new SqliteParameter(p, SqliteType.Text));
            foreach (var e in anchorEvents ?? [])
            {
                Set(insAe, "$gk", e.GroupKey); Set(insAe, "$lang", e.Language);
                Set(insAe, "$primary", e.IsPrimaryLanguage ? "1" : "0");
                Set(insAe, "$et", e.EType); Set(insAe, "$fa", e.FromAnchor);
                Set(insAe, "$ta", e.ToAnchor); Set(insAe, "$a", e.Anchor); Set(insAe, "$sha", e.TextSha);
                Set(insAe, "$av", e.AtVersion);
                insAe.ExecuteNonQuery();
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

            // The signature must bind the CONTENT, not just the metadata beside it: otherwise an
            // article's text could be edited inside a released database and the stamp would
            // still verify. Computed HERE, where docs and provisions are written, so no caller
            // can build an index without it and no path can drift from what is stored.
            var stamp = new Dictionary<string, string>(stampValues)
            {
                ["schema"] = SchemaVersion,
                ["algorithm"] = StampSigner.Algorithm,
                ["content_digest"] = ContentDigest(docRows, provisionRows),
            };
            if (semantic is not null)
            {
                stamp["embedding_model"] = semantic.Encoder.ModelId;
                stamp["embedding_revision"] = semantic.Encoder.ModelRevision;
                stamp["embedding_model_sha256"] = semantic.ModelSha256;
                stamp["embedding_tokenizer_sha256"] = semantic.TokenizerSha256;
                stamp["vector_format"] = semantic.VectorFormat;
                stamp["vector_file"] = Path.GetFileName(semantic.VectorPath);
            }
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
        if (semantic is not null)
        {
            semanticWriter!.Dispose();
            File.Move(vectorTempPath!, semantic.VectorPath, overwrite: true);
        }
        }
        catch
        {
            semanticWriter?.Dispose();
            if (vectorTempPath is not null && File.Exists(vectorTempPath))
                File.Delete(vectorTempPath);
            throw;
        }
    }

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
        semantic.Progress(new SemanticBuildProgress(completed, total, elapsed, remaining, stage));
        lastReportedPercent = percent;
        lastProgressReport = elapsed;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
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
