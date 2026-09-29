namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// The text of the reader's per-state queries, in one place so a test can run <c>EXPLAIN QUERY PLAN</c>
/// on exactly what the reader runs. Each takes one state's identity list and reads its articles, so its
/// cost is bounded by that list only while SQLite keeps one join order: the state by digest, its
/// identities, then each article and its member by primary key. SQLite chooses that by the statistics
/// in the index file. Under the ones a fixture build leaves it scanned every article first for
/// <see cref="AnchorArticles"/> and scanned <c>members</c> first for <see cref="StateSources"/>, so those two
/// are written as <c>CROSS JOIN</c>, which it does not reorder; the other two plan as intended under
/// every statistics the plan test tries and are left as SQLite plans them. <see cref="WorkTitles"/> is the
/// one per-work query, and it is not bounded by the work: it scans the title table once (see its remarks).
/// </summary>
internal static class LuxembourgIndexQueries
{
    internal const string AnchorArticles = """
        SELECT s.state_sha256, a.article_identity_sha256, a.publisher_id, a.publisher_wid,
               a.applicability_date, a.tokens_json
        FROM states s CROSS JOIN json_each(s.article_identities_json) j
        CROSS JOIN articles a ON a.article_identity_sha256 = j.value
        WHERE s.state_sha256 IN (SELECT value FROM json_each($states)) AND a.publisher_id = $anchor
        ORDER BY s.state_sha256, a.article_identity_sha256
        """;

    internal const string StateArticles = """
        SELECT a.article_identity_sha256, a.publisher_id, a.tokens_json, a.publisher_wid, a.applicability_date, a.searchable_text, a.object_ref_sha256
        FROM states s, json_each(s.article_identities_json) j
        JOIN articles a ON a.article_identity_sha256 = j.value
        WHERE s.state_sha256 = $digest
        ORDER BY a.publisher_id, a.article_identity_sha256
        """;

    /// <summary>
    /// The forward edges of lane R4's edge table for one state: the references written in the articles it binds, or
    /// in the one carrying a publisher id. The state is looked up by its digest, then its identities, then each
    /// article and its edges by primary key, so the cost is the state's articles and their references and not the
    /// table. Written with <c>CROSS JOIN</c>, which SQLite does not reorder, and the state read
    /// <c>INDEXED BY states_digest</c>, for the reason <see cref="StateDocumentOutcomes"/> gives: the join order is the
    /// bound, and a missing index is a failure to prepare the statement and not a silent scan.
    /// </summary>
    internal const string StateCitations = """
        SELECT a.article_identity_sha256, a.publisher_id, r.ordinal, r.in_note, r.label, r.href, r.to_kind, r.to_ref
        FROM states s INDEXED BY states_digest CROSS JOIN json_each(s.article_identities_json) j
        CROSS JOIN articles a ON a.article_identity_sha256 = j.value
        CROSS JOIN relations r ON r.from_ref = a.article_identity_sha256
        WHERE s.state_sha256 = $digest AND r.edge_type = 'cites' AND ($anchor IS NULL OR a.publisher_id = $anchor)
        ORDER BY a.publisher_id, a.article_identity_sha256, r.ordinal
        """;

    /// <summary>
    /// Which of a list of IRIs some state of the index carries as its publisher legal-resource IRI (the form the
    /// publisher writes in running text, ending <c>/jo</c>) or as its publisher work IRI, each with the product work key
    /// that state stores (derived from the work IRI and checked against it when the index is opened), exactly as stored.
    /// Not bounded by a state: no index starts with either IRI (the states key starts with the work key), so it reads
    /// <c>states</c> once for each of the two columns and keeps the rows whose IRI is on the list. That is the cost
    /// <c>ResolveWorkStates</c> already pays for a work named by its IRI, paid for the whole list and not once per name.
    /// </summary>
    internal const string HeldWorks = """
        SELECT s.publisher_legal_resource_iri, s.work_key
        FROM states s
        WHERE s.publisher_legal_resource_iri IN (SELECT value FROM json_each($iris))
        UNION
        SELECT s.publisher_work_iri, s.work_key
        FROM states s
        WHERE s.publisher_work_iri IN (SELECT value FROM json_each($iris))
        ORDER BY 1, 2
        """;

    /// <summary>
    /// The forward edges of lane R4's edge table whose target is exactly one of a list of IRIs, with the citing article's
    /// publisher id, expression and language: the same rows the per-state query reads, read by their target. The list is
    /// walked and each IRI is looked up in the target index, then the citing article by primary key, so the cost is the
    /// edges that name the work and not the table. Written with <c>CROSS JOIN</c>, which SQLite does not reorder, and the
    /// edges read <c>INDEXED BY relations_to_ref</c>, so a missing index is a failure to prepare the statement and not a
    /// silent scan of every reference the index holds.
    /// </summary>
    internal const string CitationsTo = """
        SELECT r.from_ref, a.publisher_id, a.expression_iri, a.language, r.ordinal, r.in_note, r.label, r.href, r.to_ref
        FROM json_each($iris) t
        CROSS JOIN relations r INDEXED BY relations_to_ref ON r.to_ref = t.value
        CROSS JOIN articles a ON a.article_identity_sha256 = r.from_ref
        WHERE r.edge_type = 'cites'
        ORDER BY a.expression_iri, a.publisher_id, r.from_ref, r.ordinal
        """;

    /// <summary>
    /// The states of a list of expressions, in publisher date, work key, language, expression and digest order. Not bounded
    /// by a state: no index starts with the expression IRI (the states key starts with the work key), so it reads
    /// <c>states</c> once and keeps the rows whose expression is on the list, the cost <see cref="HeldWorks"/> pays for the
    /// same reason.
    /// </summary>
    internal const string StatesOfExpressions = """
        SELECT s.work_key, s.applicability_date, s.state_sha256, s.expression_iri, s.publisher_work_iri,
               s.publisher_legal_resource_iri, s.language, s.rule_profiles_json, s.article_identities_json
        FROM states s
        WHERE s.expression_iri IN (SELECT value FROM json_each($expressions))
        ORDER BY s.applicability_date, s.work_key, s.language, s.expression_iri, s.state_sha256
        """;

    internal const string ArticleIds = """
        SELECT DISTINCT a.publisher_id
        FROM states s, json_each(s.article_identities_json) j
        JOIN articles a ON a.article_identity_sha256 = j.value
        WHERE s.state_sha256 = $digest
        ORDER BY a.publisher_id
        """;

    /// <summary>
    /// The titles of the expressions of one work. Not a per-state query, and <b>not bounded by the work</b>:
    /// it scans <c>work_titles</c> once and keeps the rows whose expression is one of the work's. The
    /// table's key starts with <c>work_identifier</c>, which is the article's publisher <c>wId</c> or the
    /// expression's IRI (<c>LuxembourgIndexBuilder</c> keys a title by whichever the article carries) and so
    /// does not reliably name a state's work, while the expression IRI is on both a state and a title row.
    /// There is no index on <c>expression_iri</c>, and adding one is a change to the index schema that this
    /// query does not make. Measured on a synthetic 48,000-row table in memory (Python's SQLite 3.50.4, not
    /// the shipped engine and not a real index): 3.7 ms, against 0.006 ms for a lookup by key. DISTINCT drops
    /// the copies a title gets for each article it was matched to, which differ only in that key.
    /// </summary>
    internal const string WorkTitles =
        "SELECT DISTINCT t.expression_iri,t.language,t.title_kind,t.title,t.evidence_sha256 " +
        "FROM work_titles t " +
        "WHERE t.expression_iri IN (SELECT value FROM json_each($expressions)) " +
        "ORDER BY t.language,t.expression_iri,t.title_kind,t.title,t.evidence_sha256";

    /// <summary>
    /// The publisher's typed assertions about a list of subjects, verbatim, by the fact table's primary key
    /// (the subject is its first column, so the list drives one key search per subject and no table is
    /// scanned), in the table's own order.
    /// </summary>
    internal const string SubjectFacts =
        "SELECT f.subject_iri,f.predicate,f.fact_kind,f.object_kind,f.object_value,f.datatype_iri,f.language_tag,f.evidence_sha256 " +
        "FROM work_facts f " +
        "WHERE f.subject_iri IN (SELECT value FROM json_each($subjects)) " +
        "ORDER BY f.subject_iri,f.predicate,f.object_kind,f.object_value,f.datatype_iri,f.language_tag,f.evidence_sha256";

    /// <summary>
    /// The held works as one page in work-key order: per work key, the distinct publisher identifiers and
    /// languages its states carry (as JSON arrays, sorted by the caller), the first and latest state dates and
    /// the state count, over every state of the work whatever the filters. The three filters are optional and
    /// bound as parameters; a null parameter disables its clause. The language filter is an EXISTS over the
    /// work's own states (a primary-key search by work key), so a work selected by one language still lists
    /// them all. The type filter is an EXISTS over the fact table by the work's own IRIs (a primary-key search
    /// per work, never a scan of the facts) for a typeDocument that is the value exactly, or whose own tail is
    /// "/" followed by the value exactly (a substring comparison, case-sensitive, so no LIKE wildcard or case fold
    /// can widen it; the review of PR #757 found the LIKE this replaced matching "loi" and "L_I" to LOI). The
    /// states are read in primary-key order and cut by LIMIT, so a deep page costs the rows before it; the listing
    /// is a browse and not a lookup.
    /// </summary>
    internal const string WorkRecords =
        "SELECT s.work_key," +
        "json_group_array(DISTINCT s.publisher_work_iri),json_group_array(DISTINCT s.publisher_legal_resource_iri)," +
        "json_group_array(DISTINCT s.language),MIN(s.applicability_date),MAX(s.applicability_date),COUNT(*) " +
        "FROM states s " +
        "WHERE ($after IS NULL OR s.work_key>$after) " +
        "AND ($language IS NULL OR EXISTS (SELECT 1 FROM states l WHERE l.work_key=s.work_key AND l.language=$language)) " +
        "AND ($type IS NULL OR EXISTS (SELECT 1 FROM work_facts f WHERE f.subject_iri IN (s.publisher_work_iri,s.publisher_legal_resource_iri) " +
        "AND f.predicate='typeDocument' AND (f.object_value=$type OR substr(f.object_value,-(length($type)+1))='/'||$type))) " +
        "GROUP BY s.work_key ORDER BY s.work_key LIMIT $take";

    /// <summary>The event log's size and last sequence number, read from the primary key (the rowid).</summary>
    internal const string EventLog =
        "SELECT COUNT(*),coalesce(MAX(e.seq),0) FROM events e";

    /// <summary>
    /// The events after a sequence number, in sequence order: a range on the primary key (the rowid), so
    /// a page costs the rows it returns and never the rows before it.
    /// </summary>
    internal const string EventsAfter =
        "SELECT e.seq,e.scope,e.key,e.event,e.observed_from,e.detail_json " +
        "FROM events e " +
        "WHERE e.seq > $after " +
        "ORDER BY e.seq LIMIT $take";

    /// <summary>
    /// The events of one name after a sequence number, in sequence order: a range on the (event, seq)
    /// index, so a rare name costs its own rows and never a pass over the log. A separate text from
    /// <see cref="EventsAfter"/>, because an optional filter in one statement hides both ranges from
    /// the planner.
    /// </summary>
    internal const string EventsOfNameAfter =
        "SELECT e.seq,e.scope,e.key,e.event,e.observed_from,e.detail_json " +
        "FROM events e " +
        "WHERE e.event = $event AND e.seq > $after " +
        "ORDER BY e.seq LIMIT $take";

    /// <summary>How many events of a list of names the log holds, by the (event, seq) index.</summary>
    internal const string EventCount =
        "SELECT COUNT(*) FROM events e WHERE e.event IN (SELECT value FROM json_each($events))";

    internal const string StateSources =
        "SELECT DISTINCT m.object_ref_sha256,m.outcome,m.rights_disposition,m.gaps_json " +
        "FROM states s CROSS JOIN json_each(s.article_identities_json) j " +
        "CROSS JOIN articles a ON a.article_identity_sha256=j.value " +
        "CROSS JOIN members m ON m.object_ref_sha256=a.object_ref_sha256 " +
        "WHERE s.state_sha256=$state ORDER BY m.object_ref_sha256";

    /// <summary>
    /// The legal-content outcomes the corpus recorded for one source document, as the JSON list the index
    /// stores, read by the member's primary key. Its own query and not a column of
    /// <see cref="StateSources"/>, because that one is DISTINCT over a row per article of the state and a
    /// list this long would be compared with itself once for each.
    /// </summary>
    internal const string MemberOutcomes =
        "SELECT m.stage3_outcomes_json FROM members m WHERE m.object_ref_sha256=$member";

    /// <summary>
    /// The legal-content outcomes the corpus recorded for the document behind each of the given states, as the
    /// JSON list the index stores. A state is one document's held articles (<c>ProjectStates</c> makes one state per
    /// member), so the document is the member of any one of its articles, and the state's first article by identity
    /// is read for it: the list of states, then each state by its digest, then that one article and its member by
    /// primary key, so the cost is the number of states asked for and not the articles in them. It is written
    /// with <c>CROSS JOIN</c>, which SQLite does not reorder, because the join order is the bound (the same reason
    /// <see cref="StateSources"/> and <see cref="AnchorArticles"/> are), and the state is read
    /// <c>INDEXED BY states_digest</c> because, under the statistics a fixture build leaves, SQLite scanned <c>states</c>
    /// once for every state in the list. A missing index is then a failure to prepare the statement and not a silent scan.
    /// </summary>
    internal const string StateDocumentOutcomes =
        "SELECT s.state_sha256,m.stage3_outcomes_json " +
        "FROM json_each($states) t " +
        "CROSS JOIN states s INDEXED BY states_digest ON s.state_sha256=t.value " +
        "CROSS JOIN articles a ON a.article_identity_sha256=json_extract(s.article_identities_json,'$[0]') " +
        "CROSS JOIN members m ON m.object_ref_sha256=a.object_ref_sha256";
}
