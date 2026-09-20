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
    ];

    private static readonly (string Sql, (string, object)[] Parameters) TitleQuery =
        (LuxembourgIndexQueries.WorkTitles, [("$expressions", "[\"" + Digest + "\"]")]);

    /// <summary>
    /// The title lookup is not bounded by the work: the title table's key starts with a column that does not
    /// reliably name a state's work, and there is no index on the expression IRI, so it reads the table once and
    /// keeps the rows whose expression is the work's. This holds it to exactly that, and to no more: one scan of
    /// the title table, never a nested one (a probe of the table per expression would be a table pass each), and
    /// no other index chosen for it. If the index ever gains one on the expression IRI the plan becomes a search
    /// and this fails, which is the moment to tighten it into the bound the per-state queries have.
    /// </summary>
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
            .Concat(TitleProblems("the index as built", Plan(connection, TitleQuery.Sql, TitleQuery.Parameters))).ToArray();
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
