using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>coverage</c> driven through the real handler on a verified mount: what the mounted index holds
/// and recorded as missing, and nothing about what the publisher holds. Every count is held against a
/// ground truth read from the index's rows by code that shares nothing with the reader's queries; the
/// language narrows only the per-language parts; the served list is read from the served bindings; what
/// the mount does not hold is a fixed list; an undeclared parameter is rejected, never ignored.
/// </summary>
[TestClass]
public sealed class V3CorpusCoverageMountTests
{
    private static string CoverageRawTarget => V3RestRouteBinding.Coverage.RawTarget;

    private sealed record Ground(
        (string Outcome, string GapsJson, string ObjectRef)[] Members,
        (string WorkKey, string Language, string Date, string Expression)[] States,
        (string Language, string? Date, bool HasText)[] Articles);

    /// <summary>The index's rows as the test reads them, one row at a time, grouped here in C#.</summary>
    private static Ground ReadGround(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        var members = new List<(string, string, string)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT outcome,gaps_json,object_ref_sha256 FROM members";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                members.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var states = new List<(string, string, string, string)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT work_key,language,applicability_date,expression_iri FROM states";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                states.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        var articles = new List<(string, string?, bool)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT language,applicability_date,searchable_text FROM articles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                articles.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2).Length > 0));
            }
        }

        return new Ground(members.ToArray(), states.ToArray(), articles.ToArray());
    }

    /// <summary>
    /// The gap tokens as the corpus recorded them, each counted once for every member that recorded it,
    /// in ordinal order, and how many members recorded any: read from the rows and grouped here.
    /// </summary>
    private static (string[] Gaps, int MembersWithGaps) ExpectedGaps(Ground ground)
    {
        var perMember = ground.Members
            .Select(static member => (member.ObjectRef, Tokens: JsonSerializer.Deserialize<string[]>(member.GapsJson)!))
            .ToArray();
        var gaps = perMember
            .SelectMany(static member => member.Tokens.Distinct(StringComparer.Ordinal).Select(token => (token, member.ObjectRef)))
            .GroupBy(static pair => pair.token, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => group.Key + "=" + group.Select(static pair => pair.ObjectRef).Distinct(StringComparer.Ordinal).Count())
            .ToArray();
        return (gaps, perMember.Count(static member => member.Tokens.Length > 0));
    }

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(static v => v.GetString()!).ToArray();

    [TestMethod]
    public void TheCoverageRouteIsTheServedBinding()
    {
        // This suite is the one the locator suite names for the route, so it must really post to the
        // binding the router serves and not to a spelling of its own.
        Assert.AreEqual("/api/v3/coverage", CoverageRawTarget);
        Assert.AreEqual("coverage", V3RestRouteBinding.Coverage.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Coverage));
    }

    [TestMethod]
    public async Task TheReportCountsExactlyWhatTheIndexHoldsInEveryLanguage()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // German is a second language of the same work and its articles carry no text; French gets a
        // second, later state; one French article has no publisher date.
        var german = await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "later");
        var own = fixture.ArticlesOfOwnState();
        await fixture.SetArticleDateAsync(fixture.ExpressionIri, own[0].PublisherId, null);
        foreach (var article in own)
        {
            await fixture.RewriteArticleTextAsync(german.ExpressionIri, article.PublisherId, string.Empty);
        }
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var ground = ReadGround(fixture);

        var envelope = await CoverageAsync(mount);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("coverage", envelope.OperationId);
        Assert.AreEqual("coverage_report", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;

        var totals = body.GetProperty("totals");
        Assert.AreEqual(ground.Members.Length, totals.GetProperty("members").GetInt64());
        Assert.AreEqual(ground.States.Select(static s => s.WorkKey).Distinct(StringComparer.Ordinal).Count(), totals.GetProperty("works").GetInt64());
        Assert.AreEqual(ground.States.Length, totals.GetProperty("states").GetInt64());
        Assert.AreEqual(ground.Articles.Length, totals.GetProperty("articles").GetInt64());
        Assert.IsGreaterThan(1, ground.States.Length, "The fixture must hold several states for these counts to mean something.");

        var languages = body.GetProperty("languages").EnumerateArray().ToArray();
        var expectedLanguages = ground.States.Select(static s => s.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expectedLanguages, languages.Select(static l => l.GetProperty("language").GetString()).ToArray());
        CollectionAssert.AreEqual(expectedLanguages, Strings(body.GetProperty("languages_held")));
        Assert.AreEqual(2, expectedLanguages.Length);
        foreach (var row in languages)
        {
            var language = row.GetProperty("language").GetString()!;
            var states = ground.States.Where(s => s.Language == language).ToArray();
            var articles = ground.Articles.Where(a => a.Language == language).ToArray();
            Assert.AreEqual(states.Select(static s => s.WorkKey).Distinct(StringComparer.Ordinal).Count(), row.GetProperty("works").GetInt64(), language);
            Assert.AreEqual(states.Length, row.GetProperty("states").GetInt64(), language);
            Assert.AreEqual(states.Min(static s => s.Date), row.GetProperty("first_state_date").GetString(), language);
            Assert.AreEqual(states.Max(static s => s.Date), row.GetProperty("last_state_date").GetString(), language);
            Assert.AreEqual(articles.Length, row.GetProperty("articles").GetInt64(), language);
            Assert.AreEqual(articles.Count(static a => a.Date is null), row.GetProperty("articles_without_publisher_date").GetInt64(), language);
            // Searchable text is counted where the article carries a publisher date, as the capability cells measure it.
            Assert.AreEqual(articles.Count(static a => a.Date is not null && a.HasText), row.GetProperty("articles_with_searchable_text").GetInt64(), language);
        }

        // The undated article is counted and named, not dropped; French is searchable and German, held
        // as states whose articles carry no text, is not.
        var french = languages.Single(static l => l.GetProperty("language").GetString() == "fra");
        var germanRow = languages.Single(static l => l.GetProperty("language").GetString() == "deu");
        Assert.AreEqual(1, french.GetProperty("articles_without_publisher_date").GetInt64());
        Assert.IsGreaterThan(0, french.GetProperty("articles_with_searchable_text").GetInt64());
        Assert.IsTrue(french.GetProperty("searchable_text_held").GetBoolean());
        Assert.AreEqual(0, germanRow.GetProperty("articles_with_searchable_text").GetInt64());
        Assert.IsFalse(germanRow.GetProperty("searchable_text_held").GetBoolean());
        Assert.AreEqual(2, languages.Length);
        Assert.AreEqual(2, french.GetProperty("states").GetInt64());
        Assert.AreEqual(1, french.GetProperty("works").GetInt64(), "Two states of one work are one work.");

        // The cells carry the population the per-language count is summed from.
        var french_cells = body.GetProperty("capability_cells").EnumerateArray()
            .Where(static cell => cell.GetProperty("language").GetString() == "fra" &&
                                  cell.GetProperty("operation").GetString() == "search")
            .ToArray();
        Assert.AreEqual(
            french.GetProperty("articles_with_searchable_text").GetInt64(),
            french_cells.Sum(static cell => cell.GetProperty("population").GetInt64()));
        Assert.IsGreaterThan(0, french_cells.Length);
    }

    [TestMethod]
    public async Task TheLanguageAskedForNarrowsOnlyThePerLanguagePartsAndAnUnheldOneIsRefused()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var whole = (await CoverageAsync(mount)).Result!.Value;
        var french = (await CoverageAsync(mount, "fra")).Result!.Value;

        Assert.AreEqual("fra", french.GetProperty("requested_language").GetString());
        Assert.AreEqual(JsonValueKind.Null, whole.GetProperty("requested_language").ValueKind);
        Assert.AreEqual(1, french.GetProperty("languages").GetArrayLength());
        Assert.AreEqual("fra", french.GetProperty("languages")[0].GetProperty("language").GetString());
        Assert.IsTrue(french.GetProperty("capability_cells").EnumerateArray().All(static cell => cell.GetProperty("language").GetString() == "fra"));
        Assert.IsGreaterThan(0, french.GetProperty("capability_cells").GetArrayLength());
        // Everything that is not per language is the same, byte for byte, whichever language is asked for.
        foreach (var name in new[] { "scope", "counts_note", "mounted", "totals", "languages_held", "members", "operations", "not_held" })
        {
            Assert.AreEqual(whole.GetProperty(name).GetRawText(), french.GetProperty(name).GetRawText(), name);
        }

        Assert.AreEqual(
            whole.GetProperty("languages").EnumerateArray().Single(static l => l.GetProperty("language").GetString() == "fra").GetRawText(),
            french.GetProperty("languages")[0].GetRawText());

        var english = await CoverageAsync(mount, "eng");
        Assert.AreEqual(V3Verdicts.Refuse, english.Verdict);
        Assert.AreEqual("language_not_available", english.Refusal!.Code);
        Assert.AreEqual("eng", english.Refusal.HelpfulPayload.GetProperty("requested_language").GetString());
        CollectionAssert.AreEqual(new[] { "deu", "fra" }, Strings(english.Refusal.HelpfulPayload.GetProperty("available_languages")));
    }

    [TestMethod]
    public async Task MembersByOutcomeAndTheRecordedGapsAreQuotedVerbatimAndCountedByMember()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using (var untouched = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(untouched);
            // What the fixture's corpus recorded to begin with, whatever it is, is reported as it is.
            var before = (await CoverageAsync(untouched)).Result!.Value.GetProperty("members");
            var (expectedBefore, withGapsBefore) = ExpectedGaps(ReadGround(fixture));
            Assert.AreEqual(withGapsBefore, before.GetProperty("with_gaps").GetInt64());
            CollectionAssert.AreEqual(
                expectedBefore,
                before.GetProperty("gaps").EnumerateArray()
                    .Select(static g => g.GetProperty("gap").GetString() + "=" + g.GetProperty("members").GetInt64()).ToArray());
        }

        // Members that recorded nothing: no gaps and none with gaps, so a count of members with gaps
        // cannot be a count of members.
        await fixture.SetMemberGapsAsync("[]");
        using (var clean = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(clean);
            var none = (await CoverageAsync(clean)).Result!.Value.GetProperty("members");
            Assert.AreEqual(0, none.GetProperty("with_gaps").GetInt64());
            Assert.AreEqual(0, none.GetProperty("gaps").GetArrayLength());
            Assert.IsGreaterThan(0, ReadGround(fixture).Members.Length);
        }

        // The corpus's own tokens, one of them repeated inside one member: counted once for that member.
        await fixture.SetMemberGapsAsync("[\"body_text_not_extracted\",\"body_text_not_extracted\",\"zz_second_gap\"]");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var ground = ReadGround(fixture);

        var members = (await CoverageAsync(mount)).Result!.Value.GetProperty("members");

        var expectedOutcomes = ground.Members.GroupBy(static m => m.Outcome, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => (g.Key, (long)g.Count())).ToArray();
        CollectionAssert.AreEqual(
            expectedOutcomes.Select(static o => o.Key + "=" + o.Item2).ToArray(),
            members.GetProperty("by_outcome").EnumerateArray()
                .Select(static o => o.GetProperty("outcome").GetString() + "=" + o.GetProperty("members").GetInt64()).ToArray());
        Assert.AreEqual(ground.Members.Length, members.GetProperty("by_outcome").EnumerateArray().Sum(static o => o.GetProperty("members").GetInt64()));
        Assert.AreEqual(ground.Members.Length, members.GetProperty("with_gaps").GetInt64(), "Every member of the fixture carries the tokens.");
        var gaps = members.GetProperty("gaps").EnumerateArray()
            .Select(static g => g.GetProperty("gap").GetString() + "=" + g.GetProperty("members").GetInt64()).ToArray();
        // Counted by member: the repeated token is one per member, not two.
        CollectionAssert.AreEqual(
            new[] { "body_text_not_extracted=" + ground.Members.Length, "zz_second_gap=" + ground.Members.Length },
            gaps);
        CollectionAssert.AreEqual(ExpectedGaps(ground).Gaps, gaps);
        Assert.IsGreaterThan(0, ground.Members.Length);
        StringAssert.Contains(members.GetProperty("gaps_note").GetString(), "verbatim");
    }

    [TestMethod]
    public async Task TheReportNamesWhatThisMountServesFromTheBindingsAndRegistersTheRest()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var operations = (await CoverageAsync(mount)).Result!.Value.GetProperty("operations");

        var registered = V3OperationRegistry.Reviewed.Operations.Select(static o => o.OperationId).Order(StringComparer.Ordinal).ToArray();
        var served = V3RestRouteBinding.Served.Select(static b => b.OperationId).Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(27, registered.Length);
        Assert.AreEqual(registered.Length, operations.GetProperty("registered").GetInt32());
        CollectionAssert.AreEqual(served, Strings(operations.GetProperty("served_operations")));
        CollectionAssert.Contains(Strings(operations.GetProperty("served_operations")), "coverage");
        var notServed = Strings(operations.GetProperty("not_served_operations"));
        CollectionAssert.AreEqual(registered.Except(served, StringComparer.Ordinal).ToArray(), notServed);
        Assert.IsFalse(served.Intersect(notServed, StringComparer.Ordinal).Any());
        CollectionAssert.AreEqual(registered, served.Concat(notServed).Order(StringComparer.Ordinal).ToArray());
        // It states a fact about the mount and says nothing of what an unserved request returns.
        var note = operations.GetProperty("note").GetString();
        StringAssert.Contains(note, "says nothing about what a request for an unserved operation returns");
    }

    [TestMethod]
    public async Task TheReportSaysWhoItIsAndWhatItDoesNotHold()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await CoverageAsync(mount);
        var body = envelope.Result!.Value;

        var mounted = body.GetProperty("mounted");
        Assert.AreEqual("lu-legilux", mounted.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.CorpusSha256, mounted.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(fixture.IndexSha256, mounted.GetProperty("index_sha256").GetString());
        Assert.AreEqual(V3OperationRegistry.Reviewed.Sha256, mounted.GetProperty("registry_sha256").GetString());
        Assert.AreEqual(V3OperationRegistry.Reviewed.Sha256, envelope.RegistrySha256);

        // What the mount does not hold is a fixed list, in these words, and not something computed.
        var notHeld = body.GetProperty("not_held").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(
            new[] { "publisher_universe", "never_consolidated_acts", "first_sighting_and_observation_times", "legal_status" },
            notHeld.Select(static row => row.GetProperty("item").GetString()).ToArray());
        // The reasons are what a reader reads, so they are pinned in these words and not by their length.
        CollectionAssert.AreEqual(
            new[]
            {
                "how many acts the publisher holds, or how many of them this mount lacks: the mount records only what was admitted",
                "the count of as-published acts never consolidated is a corpus-level statement this mount does not carry",
                "no observation time or first-sighting event is held, so nothing here says when anything was first seen",
                "no status, repeal or commencement fact is held; nothing here speaks of legal status",
            },
            notHeld.Select(static row => row.GetProperty("reason").GetString()).ToArray());
        StringAssert.Contains(body.GetProperty("scope").GetString(), "nothing about what the publisher holds");
        StringAssert.Contains(body.GetProperty("counts_note").GetString(), "a missing publisher date is counted as missing and never dropped");
        // The searchable count is drawn only from dated articles, so it and the undated count are not addends.
        StringAssert.Contains(body.GetProperty("counts_note").GetString(), "are not addends");
        // No count of the publisher's universe is written anywhere in the answer.
        Assert.IsFalse(body.GetRawText().Contains("24,579", StringComparison.Ordinal) || body.GetRawText().Contains("24579", StringComparison.Ordinal));

        // The capability cells are the manifest's, so a reader can see what can be asked and in what span.
        var cells = body.GetProperty("capability_cells").EnumerateArray().ToArray();
        Assert.IsGreaterThan(0, cells.Length);
        Assert.IsTrue(cells.Any(static cell =>
            cell.GetProperty("operation").GetString() == "search" &&
            cell.GetProperty("column").GetString() == "articles" &&
            cell.GetProperty("field").GetString() == "searchable_text"));
        foreach (var cell in cells)
        {
            Assert.IsLessThanOrEqualTo(0, string.CompareOrdinal(cell.GetProperty("period_from").GetString(), cell.GetProperty("period_to").GetString()));
        }
    }

    /// <summary>
    /// Every property the answer carries, at every depth, as a path: an object member is its dotted path
    /// and a member of an array's objects is written with <c>[]</c>. Array members that are strings add none.
    /// </summary>
    private static void CollectPaths(JsonElement element, string prefix, SortedSet<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                    paths.Add(path);
                    CollectPaths(property.Value, path, paths);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPaths(item, prefix + "[]", paths);
                }

                break;
        }
    }

    [TestMethod]
    public async Task TheAnswerHasExactlyTheseProperties_SoAFieldOfAnyNameFailsUntilItIsDeclared()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // Every array holds a row, so every object the answer can carry is walked: a second language, a
        // recorded gap, the outcome rows, the capability cells and the fixed not-held rows.
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await fixture.SetMemberGapsAsync("[\"a_recorded_gap\"]");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await CoverageAsync(mount)).Result!.Value;

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        CollectPaths(body, string.Empty, paths);
        // This is the guard for the answer's central promise, that it never says how much the publisher
        // holds: a property of any name, numeric or not, at any depth, fails here until it is declared.
        var declared = new[]
        {
            "capability_cells", "capability_cells[].column", "capability_cells[].field", "capability_cells[].language",
            "capability_cells[].operation", "capability_cells[].period_from", "capability_cells[].period_to",
            "capability_cells[].population",
            "counts_note",
            "languages", "languages[].articles", "languages[].articles_with_searchable_text",
            "languages[].articles_without_publisher_date", "languages[].first_state_date", "languages[].language",
            "languages[].last_state_date", "languages[].searchable_text_held", "languages[].states", "languages[].works",
            "languages_held",
            "members", "members.by_outcome", "members.by_outcome[].members", "members.by_outcome[].outcome",
            "members.gaps", "members.gaps[].gap", "members.gaps[].members", "members.gaps_note", "members.with_gaps",
            "mounted", "mounted.corpus_sha256", "mounted.index_sha256", "mounted.publisher", "mounted.registry_sha256",
            "not_held", "not_held[].item", "not_held[].reason",
            "operations", "operations.not_served_operations", "operations.note", "operations.registered",
            "operations.served_operations",
            "requested_language",
            "scope",
            "totals", "totals.articles", "totals.members", "totals.states", "totals.works",
        }.Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(declared, paths.ToArray(), "The answer carries a property that is not declared, or lost one that is.");
        // Every array the paths walked held a row, so a missing row-shape is not hiding behind an empty list.
        foreach (var name in new[] { "capability_cells", "languages", "not_held" })
        {
            Assert.IsGreaterThan(0, body.GetProperty(name).GetArrayLength(), name);
        }

        Assert.IsGreaterThan(0, body.GetProperty("members").GetProperty("gaps").GetArrayLength());
        Assert.IsGreaterThan(0, body.GetProperty("members").GetProperty("by_outcome").GetArrayLength());
    }

    [TestMethod]
    public async Task TheReportDoesNotHoldTheReadersGateSoAskingForItCannotStallTheMount()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var manifest = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        using var reader = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), manifest, CancellationToken.None);
        var gate = typeof(LuxembourgIndexReader).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reader)!;

        Task<IReadOnlyList<LuxembourgIndexResolvedState>> control;
        Task<LuxembourgIndexCoverage> report;
        lock (gate)
        {
            // The control: an operation that needs the gate waits for it, so this test can see a report that holds it.
            control = Task.Run(() => reader.ResolveWorkStates(fixture.WorkKey));
            Assert.IsFalse(control.Wait(TimeSpan.FromMilliseconds(500)), "the control operation was not held by the gate, so this test cannot see the report holding it");

            // The report is not held by it.
            report = Task.Run(() => reader.ResolveCoverage());
            Assert.IsTrue(report.Wait(TimeSpan.FromSeconds(15)), "the coverage report waited for the reader's gate: asking for it could stall every other operation on the mount");
        }

        Assert.IsGreaterThan(0, report.Result.States);
        Assert.IsTrue(control.Wait(TimeSpan.FromSeconds(15)), "the control never completed once the gate was released");
    }

    [TestMethod]
    public async Task IdenticalRequestsGiveByteIdenticalAnswers()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var first = await PostAsync(mount, CoverageRawTarget, "{\"operation_id\":\"coverage\",\"parameters\":{}}");
        var second = await PostAsync(mount, CoverageRawTarget, "{\"operation_id\":\"coverage\",\"parameters\":{}}");

        CollectionAssert.AreEqual(ResponseBytes(first), ResponseBytes(second));
    }

    [TestMethod]
    public async Task AMountWithoutTheLuxembourgIndexRefusesAsNoCorpusMounted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var europeOnly = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(europeOnly);

        var unmounted = await CoverageAsync(europeOnly);

        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
        Assert.AreEqual("lu", unmounted.Refusal.HelpfulPayload.GetProperty("required_corpus").GetString());
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"coverage\"}")]
    [DataRow("{\"operation_id\":\"coverage\",\"parameters\":{\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"coverage\",\"parameters\":{\"language\":\" \"}}")]
    [DataRow("{\"operation_id\":\"coverage\",\"parameters\":{\"language\":7}}")]
    // Every other parameter is undeclared: rejected, never ignored (a work, a date or a limit is another question).
    [DataRow("{\"operation_id\":\"coverage\",\"parameters\":{\"identifier\":\"/lu-legilux/x\"}}")]
    [DataRow("{\"operation_id\":\"coverage\",\"parameters\":{\"date\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"coverage\",\"parameters\":{\"limit\":10}}")]
    [DataRow("{\"operation_id\":\"coverage\",\"parameters\":{\"sort\":\"size\"}}")]
    public async Task UnusableCoverageRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        AssertTransportProblem(await PostAsync(mount, CoverageRawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    private static async Task<V3Envelope> CoverageAsync(V3CorpusMount mount, string? language = null)
    {
        var parameters = new Dictionary<string, object>();
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var context = await PostAsync(
            mount, CoverageRawTarget, JsonSerializer.Serialize(new { operation_id = "coverage", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-coverage";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    private static void AssertTransportProblem(DefaultHttpContext context, string code, int status)
    {
        Assert.AreEqual(status, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(problem.RootElement.TryGetProperty("verdict", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("refusal", out _));
    }
}
