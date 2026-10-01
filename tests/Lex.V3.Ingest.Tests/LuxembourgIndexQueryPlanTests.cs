using System.Text.RegularExpressions;
using Lex.V3.Api;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The reader's per-state queries take one state's identity list and read its articles under the
/// reader's gate. Their cost is bounded by that list only while SQLite keeps one join order: the state
/// by digest, its identities, then each article and its member by primary key. Which order SQLite
/// picks depends on the statistics in the index file, and under the ones a fixture build leaves it scanned
/// every article first for one of these and <c>members</c> first for another, so this asks the engine
/// rather than reading the SQL. The one per-work query, the titles of a work, is held differently and says
/// why in <see cref="TitleProblems"/>: it scans the title table once, which is what it is measured to do and
/// no more.
/// </summary>
[TestClass]
public sealed class LuxembourgIndexQueryPlanTests
{
    private static readonly string Digest = new('x', 64);

    private static readonly (string Name, string Sql, (string, object)[] Parameters, bool ReadsMembers)[] Queries =
    [
        ("AnchorArticles", LuxembourgIndexQueries.AnchorArticles,
            [("$states", "[\"" + Digest + "\"]"), ("$anchor", "art_1")], false),
        ("StateArticles", LuxembourgIndexQueries.StateArticles, [("$digest", Digest)], false),
        ("ArticleIds", LuxembourgIndexQueries.ArticleIds, [("$digest", Digest)], false),
        ("StateSources", LuxembourgIndexQueries.StateSources, [("$state", Digest)], true),
        ("StateCitations", LuxembourgIndexQueries.StateCitations, [("$digest", Digest), ("$anchor", "art_1")], false),
    ];

    private static readonly (string Sql, (string, object)[] Parameters) TitleQuery =
        (LuxembourgIndexQueries.WorkTitles, [("$expressions", "[\"" + Digest + "\"]")]);

    private static readonly (string Sql, (string, object)[] Parameters) MemberOutcomeQuery =
        (LuxembourgIndexQueries.MemberOutcomes, [("$member", Digest)]);

    private static readonly (string Sql, (string, object)[] Parameters) CitationQuery =
        (LuxembourgIndexQueries.StateCitations, [("$digest", Digest), ("$anchor", "art_1")]);

    private static readonly (string Sql, (string, object)[] Parameters) CitationsToQuery =
        (LuxembourgIndexQueries.CitationsTo, [("$iris", "[\"http://example.invalid/eli/x\"]")]);

    private static readonly (string Sql, (string, object)[] Parameters) StatesOfExpressionsQuery =
        (LuxembourgIndexQueries.StatesOfExpressions, [("$expressions", "[\"http://example.invalid/expression\"]")]);

    private static readonly (string Sql, (string, object)[] Parameters) HeldWorksQuery =
        (LuxembourgIndexQueries.HeldWorks, [("$iris", "[\"http://example.invalid/eli/x\"]")]);

    private static readonly (string Sql, (string, object)[] Parameters) StateDocumentQuery =
        (LuxembourgIndexQueries.StateDocumentOutcomes, [("$states", "[\"" + Digest + "\"]")]);

    private static readonly (string Sql, (string, object)[] Parameters) SubjectFactsQuery =
        (LuxembourgIndexQueries.SubjectFacts, [("$subjects", "[\"http://example.invalid/eli/x\"]")]);

    private static readonly (string Sql, (string, object)[] Parameters) WorkRecordsQuery =
        (LuxembourgIndexQueries.WorkRecords, [("$after", "a"), ("$language", "fra"), ("$type", "LOI"), ("$take", 10)]);

    private static readonly (string Sql, (string, object)[] Parameters) EventsAfterQuery =
        (LuxembourgIndexQueries.EventsAfter, [("$after", 1L), ("$take", 10)]);

    private static readonly (string Sql, (string, object)[] Parameters) EventsOfNameAfterQuery =
        (LuxembourgIndexQueries.EventsOfNameAfter, [("$after", 1L), ("$event", "first_sighting"), ("$take", 10)]);

    private static readonly (string Sql, (string, object)[] Parameters) WorkEventsUpToQuery =
        (LuxembourgIndexQueries.WorkEventsUpTo, [("$work", "loi-1991-08-10-n3"), ("$last", 10L)]);

    private static readonly (string Sql, (string, object)[] Parameters) EventCountQuery =
        (LuxembourgIndexQueries.EventCount, [("$events", "[\"validity_revised\",\"interval_closed\"]")]);

    /// <summary>
    /// The document behind each state asked for is reached from the list of states: each state by its digest, then its
    /// first article and that article's member, each by primary key. So its cost is the number of states in the list and
    /// not the articles in them, whatever the statistics say: no table is scanned but the list, and the order is the list,
    /// the state, the article, the member.
    /// </summary>
    private static IEnumerable<string> StateDocumentProblems(string label, string[] plan)
    {
        var shown = $"{label}, StateDocumentOutcomes: {string.Join(" | ", plan)}";
        foreach (var table in new[] { "s", "a", "m" })
        {
            if (plan.Any(line => Regex.IsMatch(line, $@"^SCAN {table}\b")))
            {
                yield return $"table {table} is scanned. " + shown;
            }
        }

        var order = new[]
        {
            Array.FindIndex(plan, static line => Regex.IsMatch(line, @"^SCAN t\b")),
            Array.FindIndex(plan, static line => line.StartsWith("SEARCH s USING INDEX states_digest (state_sha256=?)", StringComparison.Ordinal)),
            Array.FindIndex(plan, static line => line.StartsWith("SEARCH a USING INDEX sqlite_autoindex_articles_1 (article_identity_sha256=?)", StringComparison.Ordinal)),
            Array.FindIndex(plan, static line => line.StartsWith("SEARCH m USING INDEX sqlite_autoindex_members_1 (object_ref_sha256=?)", StringComparison.Ordinal)),
        };
        if (order.Any(static index => index < 0) || !order.SequenceEqual(order.Order()))
        {
            yield return "the order is not the list, the state by digest, the article by primary key, the member by primary key. " + shown;
        }
    }

    /// <summary>
    /// A source's outcome list is read by the member's primary key, once per source: one search of <c>members</c>, never a
    /// scan of it, whatever the statistics say.
    /// </summary>
    private static IEnumerable<string> MemberOutcomeProblems(string label, string[] plan)
    {
        var shown = $"{label}, MemberOutcomes: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN m\b")))
        {
            yield return "members are scanned for one source's outcomes. " + shown;
        }

        if (plan.Count(static line => line.StartsWith("SEARCH m USING INDEX sqlite_autoindex_members_1 (object_ref_sha256=?)", StringComparison.Ordinal)) != 1)
        {
            yield return "members are not searched by primary key exactly once. " + shown;
        }
    }

    /// <summary>
    /// The title lookup is not bounded by the work: the title table's key starts with a column that does not
    /// reliably name a state's work, and there is no index on the expression IRI, so it reads the table once and
    /// keeps the rows whose expression is the work's. This holds it to exactly that, and to no more: one scan of
    /// the title table, never a nested one (a probe of the table per expression would be a table pass each), and
    /// no other index chosen for it. If the index ever gains one on the expression IRI the plan becomes a search
    /// and this fails, which is the moment to tighten it into the bound the per-state queries have.
    /// </summary>
    private static IEnumerable<string> CitationProblems(string label, string[] plan)
    {
        var shown = $"{label}, StateCitations: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN r\b")))
        {
            yield return "relations are scanned. " + shown;
        }

        if (!plan.Any(static line => line.StartsWith("SEARCH r USING INDEX sqlite_autoindex_relations_1 (from_ref=?)", StringComparison.Ordinal)))
        {
            yield return "the citing article's edges are not searched by the table's primary key. " + shown;
        }

        // The edges are reached from the article, never the article from the edges.
        var articles = Array.FindIndex(plan, static line => Regex.IsMatch(line, @"^SEARCH a\b"));
        var edges = Array.FindIndex(plan, static line => Regex.IsMatch(line, @"^SEARCH r\b"));
        if (!(articles >= 0 && articles < edges))
        {
            yield return "the join order is not the article and then its edges. " + shown;
        }
    }

    /// <summary>
    /// The facts of a list of subjects are reached from the list: the fact table's primary key starts with the
    /// subject, so each subject is one key search and the table is never scanned, whatever the statistics say.
    /// </summary>
    private static IEnumerable<string> SubjectFactsProblems(string label, string[] plan)
    {
        var shown = $"{label}, SubjectFacts: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN f\b")))
        {
            yield return "the fact table is scanned. " + shown;
        }

        if (!plan.Any(static line => line.StartsWith("SEARCH f USING INDEX sqlite_autoindex_work_facts_1 (subject_iri=?)", StringComparison.Ordinal)))
        {
            yield return "the facts are not searched by the table's primary key on the subject. " + shown;
        }
    }

    /// <summary>
    /// Which of a list of IRIs a state carries is not bounded by a state: no index starts with either IRI column, so it
    /// reads <c>states</c> once for each of the two, compares each row with the list, and merges. This holds it to exactly
    /// that: two passes over <c>states</c> and no more, never a nested one (a probe of the table per name in the list
    /// would be a table pass each), and the list read as a list. If an index on an IRI column is ever added the plan
    /// becomes a search and this fails, which is the moment to tighten it into the bound the per-state queries have.
    /// </summary>
    /// <summary>
    /// The browse listing reads the states in their key order and cuts by LIMIT; its type filter reaches the fact
    /// table by each work's own IRIs, a primary-key search, and never scans the facts, whatever the statistics say.
    /// </summary>
    private static IEnumerable<string> WorkRecordsProblems(string label, string[] plan)
    {
        var shown = $"{label}, WorkRecords: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN f\b")))
        {
            yield return "the fact table is scanned by the browse listing. " + shown;
        }

        // The planner may take the primary key as a covering index and add the predicate column to the search; both
        // are a key search by the subject, which is what is pinned.
        if (!plan.Any(static line => Regex.IsMatch(line, @"^SEARCH f USING (COVERING )?INDEX sqlite_autoindex_work_facts_1 \(subject_iri=\?")))
        {
            yield return "the type filter does not reach the facts by the table's primary key on the subject. " + shown;
        }
    }

    /// <summary>
    /// A page of the event log is a range on its primary key (the rowid) after the cursor, so it costs the rows it
    /// returns and never the rows before it; the event name is a filter on those rows, never a reason to scan.
    /// </summary>
    private static IEnumerable<string> EventsAfterProblems(string label, string[] plan)
    {
        var shown = $"{label}, EventsAfter: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN e\b")))
        {
            yield return "the event log is scanned from its start. " + shown;
        }

        if (!plan.Any(static line => line.StartsWith("SEARCH e USING INTEGER PRIMARY KEY (rowid>?)", StringComparison.Ordinal)))
        {
            yield return "the page is not a range on the log's primary key after the cursor. " + shown;
        }
    }

    /// <summary>
    /// A page of one event name starts after the cursor: a range on the (event, seq) index, or, where the statistics say
    /// every event carries one name (a genesis log holds only first_sighting), the rowid range with the name as a filter.
    /// Never a pass over the log from its start.
    /// </summary>
    private static IEnumerable<string> EventsOfNameAfterProblems(string label, string[] plan)
    {
        var shown = $"{label}, EventsOfNameAfter: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN e\b")))
        {
            yield return "the event log is scanned for one name. " + shown;
        }

        if (!plan.Any(static line =>
                Regex.IsMatch(line, @"^SEARCH e USING (COVERING )?INDEX events_event_seq \(event=\? AND seq>\?\)") ||
                line.StartsWith("SEARCH e USING INTEGER PRIMARY KEY (rowid>?)", StringComparison.Ordinal)))
        {
            yield return "the page of one name is not a range after the cursor. " + shown;
        }
    }

    /// <summary>One work's events up to an observation (as_observed): a range of the (work, seq) index, never a scan of the log.</summary>
    private static IEnumerable<string> WorkEventsUpToProblems(string label, string[] plan)
    {
        var shown = $"{label}, WorkEventsUpTo: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN e\b")))
        {
            yield return "the event log is scanned for one work. " + shown;
        }

        if (!plan.Any(static line => Regex.IsMatch(line, @"^SEARCH e USING INDEX events_work_seq \(<expr>=\? AND seq<\?\)")))
        {
            yield return "one work's events are not a range of the (work, seq) index. " + shown;
        }
    }

    /// <summary>How many events of a list of names: one search of the (event, seq) index per name, never a scan.</summary>
    private static IEnumerable<string> EventCountProblems(string label, string[] plan)
    {
        var shown = $"{label}, EventCount: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN e\b")))
        {
            yield return "the event log is scanned to count names. " + shown;
        }

        if (!plan.Any(static line => Regex.IsMatch(line, @"^SEARCH e USING (COVERING )?INDEX events_event_seq \(event=\?")))
        {
            yield return "the names are not counted by the (event, seq) index. " + shown;
        }
    }

    private static IEnumerable<string> HeldWorksProblems(string label, string[] plan)
    {
        var shown = $"{label}, HeldWorks: {string.Join(" | ", plan)}";
        var scans = plan.Count(static line => Regex.IsMatch(line, @"^SCAN s\b"));
        if (scans != 2)
        {
            yield return $"states is scanned {scans} times, not twice. " + shown;
        }

        if (plan.Any(static line => Regex.IsMatch(line, @"^SEARCH s\b")))
        {
            yield return "states is searched by an index, which is not the plan this test was written for: tighten it. " + shown;
        }

        if (plan.Count(static line => line.StartsWith("LIST SUBQUERY", StringComparison.Ordinal)) < 2)
        {
            yield return "the IRIs are not a list each pass is compared with. " + shown;
        }
    }

    /// <summary>
    /// The edges that name an IRI are reached from the list of IRIs, each by the target index and then the citing article by
    /// primary key, so the cost is the edges that name the work and never a pass over every reference the index holds.
    /// The edges are never scanned, whatever the statistics say.
    /// </summary>
    private static IEnumerable<string> CitationsToProblems(string label, string[] plan)
    {
        var shown = $"{label}, CitationsTo: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN r\b")))
        {
            yield return "relations are scanned. " + shown;
        }

        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN a\b")))
        {
            yield return "articles are scanned. " + shown;
        }

        var list = Array.FindIndex(plan, static line => Regex.IsMatch(line, @"^SCAN t\b"));
        var edges = Array.FindIndex(plan, static line => line.StartsWith("SEARCH r USING INDEX relations_to_ref (to_ref=?", StringComparison.Ordinal));
        var articles = Array.FindIndex(plan, static line => line.StartsWith("SEARCH a USING INDEX sqlite_autoindex_articles_1 (article_identity_sha256=?)", StringComparison.Ordinal));
        if (!(list >= 0 && list < edges && edges < articles))
        {
            yield return "the order is not the list, the edges by the target index, the article by primary key. " + shown;
        }
    }

    /// <summary>
    /// The states of a list of expressions are not bounded by a state: no index starts with the expression IRI, so it reads
    /// <c>states</c> once and compares each row with the list. Exactly one pass, and the list read as a list.
    /// </summary>
    private static IEnumerable<string> StatesOfExpressionsProblems(string label, string[] plan)
    {
        var shown = $"{label}, StatesOfExpressions: {string.Join(" | ", plan)}";
        var scans = plan.Count(static line => Regex.IsMatch(line, @"^SCAN s\b"));
        if (scans != 1)
        {
            yield return $"states is scanned {scans} times, not once. " + shown;
        }

        if (!plan.Any(static line => line.StartsWith("LIST SUBQUERY", StringComparison.Ordinal)))
        {
            yield return "the expressions are not a list the scan is compared with. " + shown;
        }
    }

    private static IEnumerable<string> TitleProblems(string label, string[] plan)
    {
        var shown = $"{label}, WorkTitles: {string.Join(" | ", plan)}";
        var scans = plan.Count(static line => Regex.IsMatch(line, @"^SCAN t\b"));
        if (scans != 1)
        {
            yield return $"work_titles is scanned {scans} times, not once. " + shown;
        }

        if (plan.Any(static line => Regex.IsMatch(line, @"^SEARCH t\b")))
        {
            yield return "work_titles is searched by an index, which is not the plan this test was written for: tighten it. " + shown;
        }

        // The rows kept by the work's expressions are the ones asked for, and they are compared to the list, not joined to it per row.
        if (!plan.Any(static line => line.StartsWith("LIST SUBQUERY", StringComparison.Ordinal)))
        {
            yield return "the expressions are not a list the scan is compared with. " + shown;
        }
    }

    private static string[] Plan(SqliteConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using var reader = command.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
        {
            lines.Add(reader.GetString(3));
        }

        return lines.ToArray();
    }

    private static IEnumerable<string> Problems(string label, string name, string[] plan, bool readsMembers, bool stateIsSearched)
    {
        var shown = $"{label}, {name}: {string.Join(" | ", plan)}";
        if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN a\b")))
        {
            yield return "articles are scanned. " + shown;
        }

        if (plan.Any(static line => Regex.IsMatch(line, @"^SEARCH a\b") &&
                                    !line.Contains("sqlite_autoindex_articles_1 (article_identity_sha256=?)", StringComparison.Ordinal)))
        {
            yield return "articles are searched by something other than their primary key. " + shown;
        }

        if (!plan.Any(static line => line.StartsWith("SEARCH a USING INDEX sqlite_autoindex_articles_1 (article_identity_sha256=?)", StringComparison.Ordinal)))
        {
            yield return "articles are never searched by primary key. " + shown;
        }

        if (readsMembers)
        {
            if (plan.Any(static line => Regex.IsMatch(line, @"^SCAN m\b")))
            {
                yield return "members are scanned. " + shown;
            }

            if (!plan.Any(static line => line.StartsWith("SEARCH m USING INDEX sqlite_autoindex_members_1 (object_ref_sha256=?)", StringComparison.Ordinal)))
            {
                yield return "members are never searched by primary key. " + shown;
            }
        }

        // The order: the state, then its identity list, then the articles; the state's own scan or search is first.
        var state = Array.FindIndex(plan, static line => Regex.IsMatch(line, @"^(SCAN|SEARCH) s\b"));
        var identities = Array.FindIndex(plan, static line => Regex.IsMatch(line, @"^SCAN j\b"));
        var articles = Array.FindIndex(plan, static line => Regex.IsMatch(line, @"^SEARCH a\b"));
        if (!(state >= 0 && state < identities && identities < articles))
        {
            yield return "the join order is not state, identities, articles. " + shown;
        }

        if (stateIsSearched && state >= 0 &&
            !plan[state].StartsWith("SEARCH s USING INDEX states_digest (state_sha256=?)", StringComparison.Ordinal))
        {
            yield return "the state is not looked up by its digest. " + shown;
        }
    }

    [TestMethod]
    public async Task EveryPerStateQueryReadsItsArticlesByPrimaryKeyInTheStatesOwnOrderOnTheIndexAsBuilt()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);

        var problems = Queries.SelectMany(query =>
            Problems("the index as built", query.Name, Plan(connection, query.Sql, query.Parameters), query.ReadsMembers, stateIsSearched: false))
            .Concat(TitleProblems("the index as built", Plan(connection, TitleQuery.Sql, TitleQuery.Parameters)))
            .Concat(MemberOutcomeProblems("the index as built", Plan(connection, MemberOutcomeQuery.Sql, MemberOutcomeQuery.Parameters)))
            .Concat(StateDocumentProblems("the index as built", Plan(connection, StateDocumentQuery.Sql, StateDocumentQuery.Parameters)))
            .Concat(CitationProblems("the index as built", Plan(connection, CitationQuery.Sql, CitationQuery.Parameters)))
            .Concat(HeldWorksProblems("the index as built", Plan(connection, HeldWorksQuery.Sql, HeldWorksQuery.Parameters)))
            .Concat(CitationsToProblems("the index as built", Plan(connection, CitationsToQuery.Sql, CitationsToQuery.Parameters)))
            .Concat(StatesOfExpressionsProblems("the index as built", Plan(connection, StatesOfExpressionsQuery.Sql, StatesOfExpressionsQuery.Parameters)))
            .Concat(SubjectFactsProblems("the index as built", Plan(connection, SubjectFactsQuery.Sql, SubjectFactsQuery.Parameters)))
            .Concat(WorkRecordsProblems("the index as built", Plan(connection, WorkRecordsQuery.Sql, WorkRecordsQuery.Parameters)))
            // The event pages are not asked of the fixture as built: its log holds one event, and the planner rightly
            // scans a one-row table rather than seek into it. They are held below, with no statistics and with
            // statistics shaped like a real log, which is where a pass from the start would cost something.
            .Concat(EventCountProblems("the index as built", Plan(connection, EventCountQuery.Sql, EventCountQuery.Parameters))).ToArray();
        Assert.AreEqual(0, problems.Length, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public async Task TheOrderHoldsWithNoStatisticsAndWithStatisticsShapedLikeARealIndex()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var source = Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName);

        // A private copy each time: statistics are read when a connection opens, so the copy is edited, closed and reopened.
        // Statistics that misstate a table's size by orders of magnitude are not tried: the index is immutable and its
        // digest is verified, so its statistics are those of the data it holds.
        var problems = new List<string>();
        foreach (var (label, statistics, stateIsSearched) in new (string, string[], bool)[]
                 {
                     ("no statistics at all", [], false),
                     // An assumption and not a measurement: statistics shaped like a real index (thousands of works, tens of
                     // thousands of articles, a unique digest per state), so that the planner has every reason to search.
                     ("statistics shaped like a real index", [
                         "INSERT INTO sqlite_stat1 VALUES ('states','states_digest','9000 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('states','sqlite_autoindex_states_1','9000 6 1 1 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('articles','sqlite_autoindex_articles_1','24000 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('articles','articles_object_ref','24000 8')",
                         "INSERT INTO sqlite_stat1 VALUES ('articles','articles_language_date','24000 12000 30')",
                         "INSERT INTO sqlite_stat1 VALUES ('members','sqlite_autoindex_members_1','3000 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('work_titles','sqlite_autoindex_work_titles_1','30000 5 5 4 2 1 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('work_titles','work_titles_normalized','30000 3 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('work_facts','sqlite_autoindex_work_facts_1','60000 20 4 2 1 1 1 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('events','events_event_seq','9000 9000 1')",
                         "INSERT INTO sqlite_stat1 VALUES ('events','sqlite_autoindex_events_1','9000 9000 1 1')",
                     ], true),
                 })
        {
            var copy = Path.Combine(fixture.Directory, $"plan-{Guid.NewGuid():N}.sqlite");
            File.Copy(source, copy);
            using (var edit = LuxembourgIndexBuilder.Open(copy, SqliteOpenMode.ReadWrite))
            {
                using var clear = edit.CreateCommand();
                clear.CommandText = "DELETE FROM sqlite_stat1";
                clear.ExecuteNonQuery();
                foreach (var statement in statistics)
                {
                    using var insert = edit.CreateCommand();
                    insert.CommandText = statement;
                    insert.ExecuteNonQuery();
                }
            }

            SqliteConnection.ClearAllPools();
            using var connection = LuxembourgIndexBuilder.Open(copy, SqliteOpenMode.ReadOnly);
            problems.AddRange(Queries.SelectMany(query =>
                Problems(label, query.Name, Plan(connection, query.Sql, query.Parameters), query.ReadsMembers, stateIsSearched)));
            problems.AddRange(TitleProblems(label, Plan(connection, TitleQuery.Sql, TitleQuery.Parameters)));
            problems.AddRange(MemberOutcomeProblems(label, Plan(connection, MemberOutcomeQuery.Sql, MemberOutcomeQuery.Parameters)));
            problems.AddRange(StateDocumentProblems(label, Plan(connection, StateDocumentQuery.Sql, StateDocumentQuery.Parameters)));
            problems.AddRange(CitationProblems(label, Plan(connection, CitationQuery.Sql, CitationQuery.Parameters)));
            problems.AddRange(HeldWorksProblems(label, Plan(connection, HeldWorksQuery.Sql, HeldWorksQuery.Parameters)));
            problems.AddRange(CitationsToProblems(label, Plan(connection, CitationsToQuery.Sql, CitationsToQuery.Parameters)));
            problems.AddRange(StatesOfExpressionsProblems(label, Plan(connection, StatesOfExpressionsQuery.Sql, StatesOfExpressionsQuery.Parameters)));
            problems.AddRange(SubjectFactsProblems(label, Plan(connection, SubjectFactsQuery.Sql, SubjectFactsQuery.Parameters)));
            problems.AddRange(WorkRecordsProblems(label, Plan(connection, WorkRecordsQuery.Sql, WorkRecordsQuery.Parameters)));
            problems.AddRange(EventsAfterProblems(label, Plan(connection, EventsAfterQuery.Sql, EventsAfterQuery.Parameters)));
            problems.AddRange(EventsOfNameAfterProblems(label, Plan(connection, EventsOfNameAfterQuery.Sql, EventsOfNameAfterQuery.Parameters)));
            problems.AddRange(WorkEventsUpToProblems(label, Plan(connection, WorkEventsUpToQuery.Sql, WorkEventsUpToQuery.Parameters)));
            problems.AddRange(EventCountProblems(label, Plan(connection, EventCountQuery.Sql, EventCountQuery.Parameters)));
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public void TheReaderRunsTheTextThisTestAsks()
    {
        var reader = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Lex.V3.Ingest", "Luxembourg", "LuxembourgIndexBuilder.cs"));
        foreach (var name in Queries.Select(static query => query.Name))
        {
            Assert.AreEqual(
                1,
                Occurrences(reader, $"command.CommandText = LuxembourgIndexQueries.{name};"),
                $"The reader does not run LuxembourgIndexQueries.{name} exactly once, so the plan asked of it is not the plan it gets.");
        }

        Assert.AreEqual(
            1,
            Occurrences(reader, "command.CommandText = LuxembourgIndexQueries.WorkTitles;"),
            "The reader does not run LuxembourgIndexQueries.WorkTitles exactly once, so the plan asked of it is not the plan it gets.");
        Assert.AreEqual(
            1,
            Occurrences(reader, "command.CommandText = LuxembourgIndexQueries.StateDocumentOutcomes;"),
            "The reader does not run LuxembourgIndexQueries.StateDocumentOutcomes exactly once, so the plan asked of it is not the plan it gets.");
        Assert.AreEqual(
            1,
            Occurrences(reader, "command.CommandText = LuxembourgIndexQueries.SubjectFacts;"),
            "The reader does not run LuxembourgIndexQueries.SubjectFacts exactly once, so the plan asked of it is not the plan it gets.");
        Assert.AreEqual(
            1,
            Occurrences(reader, "command.CommandText = LuxembourgIndexQueries.WorkRecords;"),
            "The reader does not run LuxembourgIndexQueries.WorkRecords exactly once, so the plan asked of it is not the plan it gets.");
        foreach (var name in new[] { "EventsAfter", "EventsOfNameAfter", "EventCount" })
        {
            Assert.AreEqual(
                1,
                Occurrences(reader, $"command.CommandText = LuxembourgIndexQueries.{name};"),
                $"The reader does not run LuxembourgIndexQueries.{name} exactly once, so the plan asked of it is not the plan it gets.");
        }
        Assert.AreEqual(
            1,
            Occurrences(reader, "outcomes.CommandText = LuxembourgIndexQueries.MemberOutcomes;"),
            "The reader does not run LuxembourgIndexQueries.MemberOutcomes exactly once, so the plan asked of it is not the plan it gets.");

        // The one per-state join left inline is search's, which scans a whole language on its own connection by design.
        Assert.AreEqual(
            1,
            Occurrences(reader, "json_each(s.article_identities_json)"),
            "A per-state query is written inline in the reader, so no plan is asked of it: put it in LuxembourgIndexQueries and in Queries.");
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
