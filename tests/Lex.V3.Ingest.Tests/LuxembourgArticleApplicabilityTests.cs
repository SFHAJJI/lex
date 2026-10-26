using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1's refusal (2026-10-19): the Release 1 re-derivation of Luxembourg run 15 stopped after 72 minutes in the index build,
/// "A publisher applicability value is not an exact civil date.", on one article's own <c>dateApplicability</c>. Run 15
/// holds 229,711 such values written <c>yyyy-MM-dd</c> and 3 written <c>yyyyMMdd</c> (<c>art_l_010-1</c> of three states of
/// the Code de la consommation, <c>20220101</c>; two of them in acquired bodies, so in the derive), and none in another form
/// (<see cref="LuxembourgArticleApplicabilityRun15Probe"/>). The index now reads a complete calendar date in either of
/// ISO 8601's forms as that date and gives any other value no date, so one article's value never refuses the build.
/// </summary>
/// <remarks>
/// LEGAL CARE. Every body here is synthetic: the identification block of
/// <see cref="LuxembourgRederivationTests.SyntheticConsolidation"/> and articles in the publisher's shape (the article's own
/// <c>scl:JOLUXWork</c>, <c>scl:JOLUXLegalResource</c> and <c>scl:jolux scl:name="dateApplicability"</c>), with invented
/// wording. Only the date values are run 15's forms.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgArticleApplicabilityTests
{
    /// <summary>
    /// Each article's value as written, what the index holds, and how it was read. The first two are run 15's two forms; the
    /// others are forms run 15 does not hold, each of which gives no date.
    /// </summary>
    private static readonly (string Id, string? Value, string? Date, string Reading)[] Articles =
    [
        ("art_1", "2017-09-05", "2017-09-05", LuxembourgIndexBuilder.ArticleDateExtended),
        ("art_2", "20220101", "2022-01-01", LuxembourgIndexBuilder.ArticleDateBasic),
        ("art_3", null, null, LuxembourgIndexBuilder.ArticleDateNone),
        ("art_4", "2022-01", null, LuxembourgIndexBuilder.ArticleDateNotRead),
        ("art_5", "20220230", null, LuxembourgIndexBuilder.ArticleDateNotRead),
        ("art_6", "2019-01-01+01:00", null, LuxembourgIndexBuilder.ArticleDateNotRead),
    ];

    [TestMethod]
    // Run 15's forms: 229,711 values in the extended form, 3 in the basic form.
    [DataRow("2017-09-05", "2017-09-05", LuxembourgIndexBuilder.ArticleDateExtended)]
    [DataRow("20220101", "2022-01-01", LuxembourgIndexBuilder.ArticleDateBasic)]
    [DataRow("20221203", "2022-12-03", LuxembourgIndexBuilder.ArticleDateBasic)]
    [DataRow(null, null, LuxembourgIndexBuilder.ArticleDateNone)]
    // Forms run 15 does not hold: none is a date the index may state.
    [DataRow("2022-01", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("2022", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("2022-00-00", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("2022-02-30", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("20220230", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("2022-1-01", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("220101", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("202201011", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("2019-01-01+01:00", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("2019-01-01Z", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("2019-01-01T00:00:00", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("01/01/2022", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("1er janvier 2022", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("２０２２０１０１", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    [DataRow("", null, LuxembourgIndexBuilder.ArticleDateNotRead)]
    public void AnArticleValueIsADateOnlyWhenItWritesOneCalendarDayInFull(string? value, string? date, string reading)
    {
        var read = LuxembourgIndexBuilder.ArticleApplicability(value);

        Assert.AreEqual(date, read.Date, value);
        Assert.AreEqual(reading, read.Reading, value);
    }

    /// <summary>
    /// The derive's own path, end to end: a consolidation whose articles carry each form is acquired (R0), re-derived under
    /// Release 1 (R1: inventory /4 and content /3, the rules D1 ran), built into the first mount, written and verified. Before
    /// this change the build refused (<c>DerivationMismatch</c>, D1's sentence) on <c>art_2</c>. The index's rows hold each
    /// article's date as read, and the index reader, whose article dates are what every answer's <c>article_valid_from</c>
    /// says, verifies the index and answers the same.
    /// </summary>
    [TestMethod]
    public async Task ARederivedBuildHoldsEachArticleDateAsReadAndNeverRefusesOnOne()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(
            store, V3FirstMountBuildTests.CheckoutRoot(), DatedConsolidation());
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(verified.Delivered, verified.Detail);

        var rederived = await LuxembourgRederivation.ApplyAsync(
            store, verified, LuxembourgRederivationTests.Release1(), CancellationToken.None);
        CollectionAssert.AreEqual(
            Articles.Select(static article => article.Value).ToArray(),
            rederived.AknInventory!.Outcomes.Single().Inventory!.Articles.Select(static article => article.PublisherApplicability).ToArray(),
            "inventory /4 retains each value as written");
        Assert.IsTrue(
            rederived.AknLegalContent!.Outcomes.All(static outcome => outcome.Disposition == LuxembourgAknLegalContentDisposition.Admitted),
            string.Join(" | ", rederived.AknLegalContent.Outcomes.Select(static outcome => outcome.Detail)));

        var clock = new V3OfflineMount.BuildClock(new DateTimeOffset(2026, 10, 12, 18, 17, 5, TimeSpan.Zero));
        using var build = await new V3FirstMountBuild(store, clock).RunAsync(europe, rederived, CancellationToken.None);
        Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");

        var directory = Path.Combine(Path.GetTempPath(), "lex-v3-article-dates-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            await V3CorpusMountWriter.WriteAsync(build, directory, null, CancellationToken.None, clock.GetUtcNow());
            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
            Assert.IsTrue(verification.Verified, verification.Detail);
            using (var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None))
            {
                Assert.IsNotNull(mount, "the API's own mount opens the index, whose reader refuses an article date that is not exact");
            }

            // The builder's rows: each article's date as read, in the document's order.
            var indexPath = Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName);
            var rows = new List<(string Id, string? Date, string Identity)>();
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly))
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT publisher_id, applicability_date, article_identity_sha256 FROM articles ORDER BY document_position";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
                }
            }
            CollectionAssert.AreEqual(
                Articles.Select(static article => $"{article.Id}={article.Date ?? "null"}").ToArray(),
                rows.Select(static row => $"{row.Id}={row.Date ?? "null"}").ToArray());

            // The reader's answer: the dates every answer's article_valid_from reads. (The shared synthetic acquisition's
            // catalogue names no state of this consolidation, so the index holds its articles in no state, as
            // LuxembourgContent3MountTests says; the reader's dates are per article.)
            using var index = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
                indexPath,
                await File.ReadAllBytesAsync(Path.Combine(directory, V3FirstMountBuildResult.LuxembourgCapabilityManifestFileName)),
                CancellationToken.None);
            Assert.AreEqual(Articles.Length, index.ArticleCount);
            CollectionAssert.AreEqual(
                Articles.Select(static article => article.Date).ToArray(),
                index.ResolveArticleDates(rows.Select(static row => row.Identity).ToArray())
                    .Select(static date => date.ApplicabilityDate).ToArray());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// The served answer: a code state (dated 2022-05-28 by its catalogue) whose articles carry run 15's two forms, none, and
    /// a form it does not hold, through the real handler on a verified mount. <c>as_of</c> says each article's
    /// <c>article_valid_from</c> as the index read it, and the one conflict rule over it: a stated article date that differs
    /// from the state's date. The rule's sentence is the one every index serves.
    /// </summary>
    [TestMethod]
    public async Task AsOfSaysEachArticlesOwnDateAsTheIndexReadIt()
    {
        const string Code = "code/exemple/20220528";
        var harness = new LuxembourgIndexCatalogueStatesTests.Harness()
            .Fact(LuxembourgIndexCatalogueStatesTests.W("code/exemple"), "rdf:type", "http://data.legilux.public.lu/resource/ontology/jolux#ComplexWork")
            .State(LuxembourgIndexCatalogueStatesTests.W(Code), "2022-05-28",
                expressions: ("fr", "http://publications.europa.eu/resource/authority/language/FRA"))
            .Held(LuxembourgIndexCatalogueStatesTests.W(Code), ["art_l_010-1", "art_l_010-2", "art_l_010-3", "art_l_010-4"],
                applicability: ["20220101", "2018-07-01", null, "2022-01"]);
        await using var fixture = await V3CorpusIntervalMountTests.IntervalMount.CreateAsync(harness);

        var envelope = await V3CorpusIntervalMountTests.AskAsync(
            fixture.Mount, "as_of", new { identifier = "/lu-legilux/code-exemple", date = "2022-06-01" });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.HelpfulPayload.GetRawText());
        var state = envelope.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("2022-05-28", state.GetProperty("applicability_date").GetString());
        var byIdentity = state.GetProperty("articles").EnumerateArray().ToDictionary(
            static article => article.GetProperty("article_identity_sha256").GetString()!,
            static article => (
                Date: article.GetProperty("article_valid_from").ValueKind == JsonValueKind.Null
                    ? null
                    : article.GetProperty("article_valid_from").GetString(),
                Conflict: article.GetProperty("validity_conflict").GetBoolean()),
            StringComparer.Ordinal);
        string Served(string id)
        {
            var (date, conflict) = byIdentity[LuxembourgIndexCatalogueStatesTests.Harness.Digest(
                "article:" + LuxembourgIndexCatalogueStatesTests.W(Code) + "/fr#" + id)];
            return $"{id}={date ?? "null"}/{(conflict ? "conflict" : "no_conflict")}";
        }

        CollectionAssert.AreEqual(
            new[]
            {
                "art_l_010-1=2022-01-01/conflict",
                "art_l_010-2=2018-07-01/conflict",
                "art_l_010-3=null/no_conflict",
                "art_l_010-4=null/no_conflict",
            },
            new[] { "art_l_010-1", "art_l_010-2", "art_l_010-3", "art_l_010-4" }.Select(Served).ToArray());
        Assert.AreEqual(2, state.GetProperty("validity_conflict_count").GetInt32());
        Assert.AreEqual(
            "article_valid_from is the publisher's article-level applicability date; validity_conflict is true when it is stated " +
            "and differs from the state's applicability_date",
            state.GetProperty("validity_conflict_rule").GetString());
    }

    /// <summary>
    /// The synthetic consolidation of <see cref="LuxembourgRederivationTests.SyntheticConsolidation"/> with one article per
    /// entry of <see cref="Articles"/>, each carrying its value where the publisher writes an article's own date.
    /// </summary>
    private static byte[] DatedConsolidation()
    {
        var synthetic = Encoding.UTF8.GetString(LuxembourgRederivationTests.SyntheticConsolidation());
        var body = synthetic.IndexOf("<body>", StringComparison.Ordinal);
        var end = synthetic.IndexOf("</body>", StringComparison.Ordinal);
        Assert.IsTrue(body > 0 && end > body);
        var articles = new StringBuilder("<body>\n");
        foreach (var (id, value, _, _) in Articles)
        {
            articles.Append($"<article id=\"{id}\">");
            if (value is not null)
            {
                articles.Append("<scl:JOLUXWork><scl:JOLUXLegalResource>")
                    .Append($"<scl:jolux scl:lg=\"fra\" scl:name=\"title\">Art. {id[4..]}.</scl:jolux>")
                    .Append($"<scl:jolux scl:name=\"dateApplicability\">{value}</scl:jolux>")
                    .Append("</scl:JOLUXLegalResource></scl:JOLUXWork>");
            }
            articles.Append($"<num>Art. {id[4..]}.</num><content><p>Texte synthétique de l’article {id[4..]}.</p></content></article>\n");
        }
        return Encoding.UTF8.GetBytes(synthetic[..body] + articles + synthetic[end..]);
    }
}
