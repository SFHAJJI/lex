using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The frozen verifiers stay frozen: inventory /3 and legal content /1 reproduce, over a retained
/// sample of run 15, what the run's verified derivation sealed per body and what an independent
/// replay of the frozen rules computed from the same bytes.
/// </summary>
/// <remarks>
/// <para>
/// WHY A REPLAY AND NOT ONLY A DIGEST. The rule-profile digests below catch an edit to a profile's
/// text; they cannot catch an edit to the logic that reads it. The checkpoint's reopen recomputes
/// /3 and /1 over all of run 15 and compares two population digests, and only a full reopen can
/// reproduce those, because they hash every member. So this replays the frozen code over 19 of the
/// run's own bodies, against two kinds of expectation the expected file keeps apart:
/// </para>
/// <list type="bullet">
/// <item><description>SEALED (<c>sealed</c>): the content disposition multisets, the admitted article
/// identities and the 7 state rows, read from derive-c's index and corpus, which its reopen built
/// after verifying both checkpoint digests.</description></item>
/// <item><description>REPLAYED (<c>replay</c>): /3's disposition, rejection detail, coordinate, article
/// order and inventory identity, and the sample's two population identities. derive-c sealed none
/// of these. An independent Python replay of the frozen rules computed them from the bytes, and the
/// frozen code reproduced them before any production edit.</description></item>
/// </list>
/// <para>
/// THE SAMPLE IS ADMITTED BYTES ONLY. Every body is one the run admitted under the agreed CC BY
/// reading, and <see cref="TheRun15SampleIsAdmittedBodiesOnly"/> holds every file committed in the
/// sample folder to that, so this directory can never become a way to commit a withheld body.
/// </para>
/// <para>
/// WHAT THE SAMPLE HOLDS, AND WHAT IT DOES NOT. Both /3 outcomes with and without a dated
/// coordinate; the four /3 rejection kinds that admitted bodies small enough to commit show (an
/// annex identification block, no top-level article, a non-ASCII id, a code's complex work); the
/// act whose only article is quoted; and twelve content /1 gap kinds. It does not hold /3's
/// <c>XmlException</c> at its 64M-character ceiling, which the admitted act
/// <c>rgd/2021/06/09/a444/jo</c> (175,837,349 bytes) hits, nor /3's per-article wId and
/// applicability readers, because none of the 8,854 articles derive-c sealed carries either. Two
/// synthetic tests hold those instead:
/// <c>LuxembourgAknArticleInventoryProducerV4Tests.ADocumentAboveThreesCeilingIsReadUpToTheCustodyBound</c>
/// and <c>LuxembourgAknArticleInventoryProducerTests.BothPublisherXmlTokensUseOneStableAknRuleProfile</c>.
/// </para>
/// <para>
/// The expected file's own digest is pinned at load, so an edit to the sealed side fails here
/// rather than quietly agreeing with whatever the code says.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgAknFrozenReplayTests
{
    internal const string Inventory3RuleSha256 =
        "70e926111b25a4d364e96b679de0baaa090a869175a82d42e0052572804cf42e";

    internal const string Content1RuleSha256 =
        "dd32d9a59ea33b8c9e9cd51b7b2657a6fa2b716ad00e35db075416ba4cd38a9b";

    private const string SampleDirectory = "LuRun15FrozenReplay";
    private const string ExpectedName = "lu-run15-frozen-replay.expected.json";
    private const string ExpectedSha256 =
        "2a4820ec00a6e216ba4de926a79c3989e891486767e7a5d86614c115df5166c0";

    [TestMethod]
    public void FrozenRuleDigestsArePinned()
    {
        Assert.AreEqual(Inventory3RuleSha256, LuxembourgAknArticleInventoryProducer.RuleProfileSha256);
        Assert.AreEqual(
            Inventory3RuleSha256,
            LuxembourgHeldPopulationBuilder.Sha(LuxembourgAknArticleInventoryProducer.CopyRuleProfileBytes()));
        Assert.AreEqual(Content1RuleSha256, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256);
        Assert.AreEqual(
            Content1RuleSha256,
            LuxembourgHeldPopulationBuilder.Sha(LuxembourgAknLegalContentProfileProducer.CopyRuleProfileBytes()));
    }

    /// <summary>
    /// The admitted act <c>loi/2017/03/14/a439/jo</c>, under the inventory tests' own retained WEMI.
    /// Both values were computed independently from the bytes before any /4 code existed.
    /// </summary>
    [TestMethod]
    public async Task TheRetainedActKeepsItsFrozenInventoryIdentity()
    {
        var fixture = await LuxembourgAknArticleInventoryProducerTests.Fixture.CreateAsync(
            LuxembourgDocumentFetchFixtures.XmlBody(), LuxembourgUserFormatToken.XmlAkomaNtoso);

        var population = await new LuxembourgAknArticleInventoryProducer(fixture.Store)
            .RunAsync(fixture.Population, CancellationToken.None);

        var outcome = population.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        Assert.AreEqual(
            "3a8c769aa0e939da28a1bf485a84c22fd7927aa1737b4ba57e131c2357653435",
            outcome.Inventory!.IdentitySha256);
        Assert.AreEqual(
            "7f714909818756933df89b5583f19d0b47fcf459ae6647a38bd26cf13fd6353b",
            population.IdentitySha256);
    }

    [TestMethod]
    public async Task FrozenInventory3AndContent1ReproduceTheSealedRun15Sample()
    {
        var sample = await RunSampleAsync();
        var statesChecked = 0;
        foreach (var member in sample.Members)
        {
            var name = (string)member["fixture"]!;
            var ordinal = (int)member["source_ordinal"]!;
            var sealedSide = member["sealed"]!;
            var replay = member["replay"]!;
            var outcome = sample.Inventory.Outcomes.Single(candidate => candidate.Input.ObjectOrdinal == ordinal);

            // /3: not inventoried exactly when the run sealed one upstream-not-inventoried outcome.
            var sealedDispositions = sealedSide["content_dispositions"]!.AsObject()
                .Select(static pair => pair.Key + "=" + (int)pair.Value!)
                .ToArray();
            Assert.AreEqual(
                sealedDispositions.SequenceEqual(["akn_upstream_not_inventoried=1"]),
                outcome.Disposition != LuxembourgAknArticleInventoryDisposition.Inventoried,
                name);

            // /1: the outcome count and the disposition multiset. The sealed side is ordered by
            // identity, not by article, so the comparison is of multisets.
            var content = sample.Content.Outcomes
                .Where(candidate => ReferenceEquals(candidate.SourceInventoryOutcome, outcome))
                .ToArray();
            var replayedDispositions = content
                .GroupBy(static candidate => ContractWire.NameOf(LexCorpus6Builder.AknDisposition(candidate.Disposition)))
                .Select(static group => group.Key + "=" + group.Count())
                .ToArray();
            CollectionAssert.AreEquivalent(sealedDispositions, replayedDispositions, name);

            // Every admitted article: its identity, id, wId and applicability equal the sealed row.
            var sealedArticles = sealedSide["admitted_articles"]!.AsArray()
                .Select(static row => string.Join(
                    "|",
                    (string?)row!["publisher_id"],
                    (string?)row["publisher_wid"] ?? "-",
                    (string?)row["applicability_date"] ?? "-",
                    (string?)row["article_identity_sha256"]))
                .ToArray();
            var replayedArticles = content
                .Where(static candidate => candidate.Disposition == LuxembourgAknLegalContentDisposition.Admitted)
                .Select(static candidate => string.Join(
                    "|",
                    candidate.Coordinate!.PublisherId,
                    candidate.Coordinate.PublisherWId ?? "-",
                    candidate.Coordinate.PublisherApplicability ?? "-",
                    candidate.Article!.IdentitySha256))
                .ToArray();
            CollectionAssert.AreEquivalent(sealedArticles, replayedArticles, name);

            // The sealed state rows: /3's coordinate and the two frozen rule digests.
            foreach (var state in sealedSide["states"]!.AsArray())
            {
                Assert.IsNotNull(outcome.Inventory, name);
                Assert.AreEqual((string?)state!["publisher_work_iri"], outcome.Inventory.PublisherWorkIri, name);
                Assert.AreEqual(
                    (string?)state["publisher_legal_resource_iri"],
                    outcome.Inventory.PublisherLegalResourceIri,
                    name);
                Assert.AreEqual((string?)state["applicability_date"], outcome.Inventory.PublisherApplicabilityDate, name);
                CollectionAssert.AreEqual(
                    new[] { outcome.Inventory.RuleProfileSha256, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256 },
                    state["rule_profiles"]!.AsArray().Select(static digest => (string?)digest).ToArray(),
                    name);
                statesChecked++;
            }

            // The replay fields are exact: the disposition, the /3 rejection detail, the coordinate,
            // the article order and the inventory identity.
            Assert.AreEqual((string?)replay["disposition"], ContractWire.NameOf(outcome.Disposition), name);
            if (outcome.Inventory is null)
            {
                Assert.AreEqual((string?)replay["replay_detail"], outcome.Detail, name);
                continue;
            }

            Assert.AreEqual((string?)replay["publisher_work_iri"], outcome.Inventory.PublisherWorkIri, name);
            Assert.AreEqual(
                (string?)replay["publisher_legal_resource_iri"],
                outcome.Inventory.PublisherLegalResourceIri,
                name);
            Assert.AreEqual(
                (string?)replay["publisher_applicability_date"],
                outcome.Inventory.PublisherApplicabilityDate,
                name);
            CollectionAssert.AreEqual(
                replay["articles"]!.AsArray()
                    .Select(static article => string.Join(
                        "|",
                        (string?)article![0],
                        (string?)article[1] ?? "-",
                        (string?)article[2] ?? "-"))
                    .ToArray(),
                outcome.Inventory.Articles
                    .Select(static article => string.Join(
                        "|",
                        article.PublisherId,
                        article.PublisherWId ?? "-",
                        article.PublisherApplicability ?? "-"))
                    .ToArray(),
                name);
            Assert.AreEqual(
                (string?)replay["replay_inventory_identity_sha256"],
                outcome.Inventory.IdentitySha256,
                name);
        }

        Assert.AreEqual(19, sample.Members.Count, "the sample's members");
        Assert.AreEqual(7, statesChecked, "the sealed state rows");
    }

    /// <summary>
    /// The two population identities over the sample. They hash every member, so they are this
    /// sample's own values, never the run's checkpoint digests. derive-c sealed neither: an
    /// independent Python replay computed them, /3's from its replayed inventory identities and
    /// /1's from the sealed admitted-article identities and its replayed article statuses, and the
    /// frozen code reproduced both before any /4 code existed.
    /// </summary>
    [TestMethod]
    public async Task TheRun15SamplePopulationIdentitiesArePinned()
    {
        var sample = await RunSampleAsync();

        Assert.AreEqual(
            "914f9f492eff06aeca6a1e836277753102653a19cc139a55c4f8cd5514bfd28a",
            sample.Inventory.IdentitySha256);
        Assert.AreEqual(
            "897045097732f8e0fde630838cd6c3b3c56c315e3ca5cb783aa43ffc27364367",
            sample.Content.IdentitySha256);
    }

    /// <summary>
    /// The legal-care guard: every member was admitted by the run under the agreed CC BY reading,
    /// every retained body is exactly the bytes the run held, and no file sits in the directory, or
    /// in any folder below it, that the sealed expectations do not name. The test project copies
    /// the committed folder whole (<c>Fixtures/LuRun15FrozenReplay/**</c>), whatever a file's
    /// extension, so the copy this reads holds every file committed there.
    /// </summary>
    [TestMethod]
    public void TheRun15SampleIsAdmittedBodiesOnly()
    {
        var members = LoadExpected();
        var named = new HashSet<string>(StringComparer.Ordinal) { ExpectedName };
        foreach (var member in members)
        {
            var name = (string)member["fixture"]!;
            Assert.AreEqual("agreed_same_run_cc_by", (string?)member["rights_disposition"], name);
            var bytes = File.ReadAllBytes(Path.Combine(SampleRoot(), name));
            Assert.AreEqual((string?)member["body_sha256"], LuxembourgHeldPopulationBuilder.Sha(bytes), name);
            Assert.AreEqual((long)member["byte_length"]!, bytes.LongLength, name);
            Assert.IsTrue(named.Add(name), name);
        }

        CollectionAssert.AreEquivalent(
            named.ToArray(),
            Directory.EnumerateFiles(SampleRoot(), "*", SearchOption.AllDirectories)
                .Select(static path => Path.GetRelativePath(SampleRoot(), path))
                .ToArray(),
            "the directory holds the sealed expectations and the bodies they name, nothing else");
    }

    private sealed record Sample(
        IReadOnlyList<JsonNode> Members,
        LuxembourgAknArticleInventoryPopulation Inventory,
        LuxembourgAknLegalContentPopulation Content);

    /// <summary>
    /// One population of the 19 members at their real ordinals under their real selected WEMI, then
    /// frozen /3 and frozen /1 over it, exactly as the checkpoint's reopen runs them.
    /// </summary>
    private static async Task<Sample> RunSampleAsync()
    {
        var members = LoadExpected();
        var (store, population) = await LuxembourgHeldPopulationBuilder.CreateAsync(members
            .Select(static member => new LuxembourgHeldPopulationBuilder.Member(
                (string)member["root_iri"]!,
                File.ReadAllBytes(Path.Combine(SampleRoot(), (string)member["fixture"]!)),
                LuxembourgHeldPopulationBuilder.Token((string)member["format"]!),
                (int)member["source_ordinal"]!,
                (string)member["expression_iri"]!,
                (string)member["manifestation_iri"]!,
                (string)member["item_iri"]!,
                (string)member["language_iri"]!))
            .ToArray());
        var inventory = await new LuxembourgAknArticleInventoryProducer(store)
            .RunAsync(population, CancellationToken.None);
        var content = await new LuxembourgAknLegalContentProfileProducer(store)
            .RunAsync(inventory, CancellationToken.None);
        return new Sample(members, inventory, content);
    }

    private static IReadOnlyList<JsonNode> LoadExpected()
    {
        var bytes = File.ReadAllBytes(Path.Combine(SampleRoot(), ExpectedName));
        Assert.AreEqual(ExpectedSha256, LuxembourgHeldPopulationBuilder.Sha(bytes), "the sealed expectations");
        var document = JsonNode.Parse(bytes)!;
        Assert.AreEqual("lex-v3-lu-run15-frozen-replay-expected/1", (string?)document["schema"]);
        Assert.AreEqual(Inventory3RuleSha256, (string?)document["inventory_rule_sha256"]);
        Assert.AreEqual(Content1RuleSha256, (string?)document["content_rule_sha256"]);
        return document["members"]!.AsArray().Select(static member => member!).ToArray();
    }

    private static string SampleRoot() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", SampleDirectory);
}
