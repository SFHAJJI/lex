using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusReferenceMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU in the one box (the plan's item 4.2): a CELEX, consolidated forms included, an EU ELI or an EU act's conventional name
/// opens the EU act, as a search that names no work reads it before any word is searched: the dossier, or Reading at the provision
/// the fixed table opens in the wording the date selects (the EU time view, on a mount whose EU index holds its census), with EU
/// context. The derived mount with a census is built once per test, so the class runs its tests one at a time, as the time view's
/// own tests do.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class V3CorpusEuropeReferenceMountTests
{
    private const string Celex = "32016R0679";

    private static IEnumerable<JsonElement> Descendants(JsonElement element)
    {
        yield return element;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in Descendants(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in Descendants(item))
                {
                    yield return nested;
                }
            }
        }
    }

    [TestMethod]
    public async Task ACelexOrAnEuActsNameOpensItsDossierAndAnArticleOpensReadingInTheWordingHeld()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (query, kind) in new[]
                 {
                     (Celex, "celex"), ("CELEX:32016R0679", "celex"), ("celex 32016r0679", "celex"),
                     ("http://data.europa.eu/eli/reg/2016/679/oj", "celex"), ("https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679", "celex"),
                     ("Regulation (EU) 2016/679", "citation"), ("règlement (UE) 2016/679", "citation"), ("/fr/eu-eurlex/32016R0679", "permalink"),
                 })
        {
            var envelope = await SearchAsync(mount, query, language: "eng", date: "2026-09-18");
            var reference = Opened(envelope, query);
            Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, query);
            Assert.AreEqual("eu-eurlex", envelope.Result!.Value.GetProperty("publisher").GetString(), query);
            Assert.AreEqual(kind, reference.GetProperty("kind").GetString(), query);
            Assert.AreEqual("dossier", reference.GetProperty("opens").GetProperty("page").GetString(), query);
            Assert.AreEqual("/eu-eurlex/" + Celex, reference.GetProperty("opens").GetProperty("address").GetString(), query);
        }

        // An article: Reading at the provision, in the wording the date selects (the fixture's census holds the original alone).
        var pinned = (await SearchAsync(mount, "It shall apply from 25 May 2018.", language: "eng", identifier: Celex))
            .Result!.Value.GetProperty("pinned_wording").GetProperty("permalink").GetString()!;
        var provision = fixture.PublisherProvisionIdentifier;
        var number = ArticleNumbers.FromEuropeId(provision)!.Key;
        foreach (var query in new[] { $"Article {number} of Regulation (EU) 2016/679", $"{Celex}, article {number}", $"article {number} du règlement (UE) 2016/679" })
        {
            var reference = Opened(await SearchAsync(mount, query, language: "eng"), query);
            var opens = reference.GetProperty("opens");
            Assert.AreEqual("reading", opens.GetProperty("page").GetString(), query);
            Assert.AreEqual(pinned + "/" + provision, opens.GetProperty("address").GetString(), query);
            Assert.AreEqual(provision, opens.GetProperty("article_id").GetString(), query);
            Assert.AreEqual(number, reference.GetProperty("recognised").GetProperty("article").GetProperty("number").GetString(), query);
        }

        var dated = Opened(await SearchAsync(mount, "Article 99 of Regulation (EU) 2016/679, 15 March 2021", language: "eng"), "dated");
        Assert.AreEqual(pinned + "/099", dated.GetProperty("opens").GetProperty("address").GetString());
        Assert.AreEqual("2016-04-27", dated.GetProperty("opens").GetProperty("state_date").GetString(), "the wording the date selects");
        Assert.AreEqual("2021-03-15", dated.GetProperty("recognised").GetProperty("date").GetProperty("date").GetString());
        const string early = "Article 99 of Regulation (EU) 2016/679, 15 March 2015";
        var before = Refused(await SearchAsync(mount, early, language: "eng"), "no_version_for_date", early);
        Assert.AreEqual("2015-03-15", before.GetProperty("recognised_date").GetString(), "the date is kept, and no wording this index holds answers it");

        const string missing = "Article 9999 of Regulation (EU) 2016/679";
        var notHeld = Refused(await SearchAsync(mount, missing, language: "eng"), "anchor_not_in_version", missing);
        Assert.AreEqual("9999", notHeld.GetProperty("recognised_article").GetString());

        const string french = "Article 99 of Regulation (EU) 2016/679";
        Refused(await SearchAsync(mount, french, language: "fra"), "language_not_available", french);

        foreach (var query in new[] { "32099R9999", "Regulation (EU) 2099/1", "/eu-eurlex/32099R9999" })
        {
            var envelope = await SearchAsync(mount, query, language: "eng");
            var payload = Refused(envelope, "identifier_unknown", query);
            Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, query);
            StringAssert.Contains(payload.GetProperty("what_would_answer_detail").GetString(), "no word was searched", query);
        }

        // A Luxembourg reference on a mount that holds no Luxembourg index: that corpus is not mounted, said with what was read.
        const string luxembourg = "article 5 de la loi du 10 août 1991";
        var unmounted = Refused(await SearchAsync(mount, luxembourg), "no_corpus_mounted", luxembourg);
        Assert.AreEqual("lu", unmounted.GetProperty("required_corpus").GetString());
        Assert.AreEqual("5", unmounted.GetProperty("recognised_article").GetString());
    }

    [TestMethod]
    public async Task OnACensusTheDateSelectsTheWordingAndAConsolidatedCelexNamesItsOwnDate()
    {
        var (root, directory) = await V3CorpusEuropeTimeViewTests.ConsolidatedMountAsync(missingStateCelex: false);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);

            // The wording each date selects, as as_of answers it.
            async Task<string> WordingAsync(string date)
            {
                var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = Celex, date, language = "eng" });
                Assert.IsNull(asOf.Refusal, asOf.Refusal?.Code);
                return asOf.Result!.Value.GetProperty("states")[0].GetProperty("wording").GetProperty("permalink").GetString()!;
            }

            var consolidated = await WordingAsync("2025-03-15");
            var original = await WordingAsync("2020-03-15");
            StringAssert.StartsWith(consolidated, "/eu-eurlex/32016R0679/2024-01-01--");
            Assert.AreNotEqual(consolidated, original);

            foreach (var (query, address, source) in new[]
                     {
                         ("Regulation (EU) 2016/679, 15 March 2025", consolidated, "citation"),
                         ("règlement (UE) 2016/679 au 15 mars 2020", original, "citation"),
                         ("02016R0679-20240101", consolidated, "identifier"),
                     })
            {
                var reference = Opened(await SearchAsync(mount, query, language: "eng"), query);
                Assert.AreEqual("reading", reference.GetProperty("opens").GetProperty("page").GetString(), query);
                Assert.AreEqual(address, reference.GetProperty("opens").GetProperty("address").GetString(), query);
                Assert.AreEqual(source, reference.GetProperty("recognised").GetProperty("date").GetProperty("source").GetString(), query);
            }

            // An article of the wording the date selects, as the bundle at that date quotes it.
            var bundle = (await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = Celex, date = "2025-03-15", language = "eng" }))
                .Result!.Value;
            var wording = Descendants(bundle).First(element => element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("permalink", out var permalink) && permalink.GetString() == consolidated && element.TryGetProperty("articles", out _));
            var article = wording.GetProperty("articles")[0];
            var provision = article.GetProperty("publisher_id").GetString()!;
            var number = ArticleNumbers.FromEuropeId(provision)!.Key;
            var opened = Opened(await SearchAsync(mount, $"Article {number} of Regulation (EU) 2016/679, 15 March 2025", language: "eng"), "article");
            Assert.AreEqual(article.GetProperty("article_permalink").GetString(), opened.GetProperty("opens").GetProperty("address").GetString());

            Assert.AreEqual("/eu-eurlex/32016R0679",
                Opened(await SearchAsync(mount, Celex, language: "eng"), "dossier").GetProperty("opens").GetProperty("address").GetString());
            const string early = "Regulation (EU) 2016/679, 1 January 1990";
            Refused(await SearchAsync(mount, early, language: "eng"), "no_version_for_date", early);
            const string disagree = "02016R0679-20240101, 15 March 2025";
            var both = (await SearchAsync(mount, disagree, language: "eng")).Result!.Value.GetProperty("reference");
            Assert.AreEqual("not_opened", both.GetProperty("outcome").GetString(), "two dates that disagree open nothing");
            Assert.AreEqual("two_dates_disagree", both.GetProperty("reason").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
