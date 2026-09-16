using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgAknLegalContentProfileProducerTests
{
    private const string FixtureDirectory = "LuAknLegalContent";
    private const string Fixture1991 = "loi-1991-08-10-n3--2024-02-01--fr.bin";
    private const string Fixture1984 = "loi-1984-02-24-n1--2020-09-01--fr.bin";

    [TestMethod]
    public async Task ExactRetained1991Article2PreservesWordsAroundReferencesAndModificationEvidence()
    {
        var bytes = await RetainedFixtureAsync(
            Fixture1991, "3a6bb598a9310f8a31240c1f33ae357d6e1f7a46392ca33d223c2718cdded95c");
        var result = await RunAsync(bytes, "loi-1991-08-10-n3");

        var outcome = result.Outcomes.Single(value => value.Coordinate?.PublisherId == "art_2");
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, outcome.Disposition, outcome.Detail);
        Assert.IsNotNull(outcome.Article);
        var tokens = outcome.Article.Tokens;
        var partnership = tokens.Single(token => token.Kind == LuxembourgAknLegalContentTokenKind.Reference
            && token.Text == "loi modifiée du 9 juillet 2004");
        Assert.AreEqual("/eli/etat/leg/loi/2004/07/09/n3/jo", partnership.Target);
        var referenceIndex = tokens.IndexOf(partnership);
        StringAssert.EndsWith(tokens[referenceIndex - 1].Text, "au sens de la ");
        StringAssert.StartsWith(tokens[referenceIndex + 1].Text, " relative aux effets légaux");

        var start = tokens.Single(token => token.Kind == LuxembourgAknLegalContentTokenKind.ModificationStart
            && token.Target == "#pm2");
        var end = tokens.Single(token => token.Kind == LuxembourgAknLegalContentTokenKind.ModificationEnd
            && token.Target == "#pm2");
        var note = tokens.Single(token => token.Kind == LuxembourgAknLegalContentTokenKind.NoteReference
            && token.Target == "#M2");
        Assert.IsTrue(tokens.IndexOf(start) < tokens.IndexOf(end));
        Assert.IsTrue(tokens.IndexOf(end) < tokens.IndexOf(note));
        Assert.IsTrue(tokens.Any(token => token.Kind == LuxembourgAknLegalContentTokenKind.Text
            && token.Text!.Contains("de l’Autorité de concurrence", StringComparison.Ordinal)));
        Assert.AreSame(result.SourceInventoryPopulation, outcome.SourceInventoryPopulation);
        Assert.AreEqual(
            result.SourceInventoryPopulation.SourcePopulation.Inputs.Single().Receipt.Reference.ContentSha256,
            outcome.TransportReceipt.Reference.ContentSha256);
    }

    [TestMethod]
    public async Task ExactRetained1984Article2PreservesAllThreePublisherParagraphsInOrder()
    {
        var bytes = await RetainedFixtureAsync(
            Fixture1984, "5d513304238bbda30578f59f963b227d54ca1fbce9283c55b9c5aa7b4436e48f");
        var result = await RunAsync(bytes, "loi-1984-02-24-n1");

        var outcome = result.Outcomes.Single(value => value.Coordinate?.PublisherId == "art_2");
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, outcome.Disposition, outcome.Detail);
        var texts = outcome.Article!.Tokens
            .Where(token => token.Kind == LuxembourgAknLegalContentTokenKind.Text)
            .Select(token => token.Text!)
            .ToArray();
        var first = Array.FindIndex(texts, text => text.StartsWith(
            "Les actes législatifs et leurs règlements d'exécution", StringComparison.Ordinal));
        var second = Array.FindIndex(texts, text => text.StartsWith(
            "Au cas où des règlements non visés", StringComparison.Ordinal));
        var third = Array.FindIndex(texts, text => text.StartsWith(
            "Le présent article ne déroge pas", StringComparison.Ordinal));
        Assert.IsTrue(first >= 0);
        Assert.IsTrue(second > first);
        Assert.IsTrue(third > second);
    }

    [TestMethod]
    public async Task UnknownLegalContentElementQuarantinesOnlyThatArticle()
    {
        var xml = Akn(
            "<article id=\"art_1\"><num>Art. 1.</num><content><p>kept</p></content></article>" +
            "<article id=\"art_2\"><num>Art. 2.</num><content><invented>guess</invented></content></article>");

        var result = await RunAsync(xml, "hostile-unknown");

        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, result.Outcomes[0].Disposition);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, result.Outcomes[1].Disposition);
        Assert.IsNull(result.Outcomes[1].Article);
        StringAssert.Contains(result.Outcomes[1].Detail, "invented");
    }

    [TestMethod]
    public async Task ModificationSpanCannotCrossAnArticleBoundary()
    {
        var xml = Akn(
            "<article id=\"art_1\"><content><p>before<mod class=\"mod-start\" for=\"#pm1\"/></p></content></article>" +
            "<article id=\"art_2\"><content><p>after<mod class=\"mod-end\" for=\"#pm1\"/></p></content></article>");

        var result = await RunAsync(xml, "hostile-cross-article");

        Assert.IsTrue(result.Outcomes.All(outcome =>
            outcome.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));
        Assert.IsTrue(result.Outcomes.All(outcome => outcome.Article is null));
    }

    [TestMethod]
    public async Task IdenticalPublisherContentHasStableSemanticIdentityAcrossDistinctCustodyRuns()
    {
        var bytes = Akn("<article id=\"art_1\"><num>Art. 1.</num><content><p>same words</p></content></article>");

        var first = await RunAsync(bytes, "stable", '1');
        var second = await RunAsync(bytes, "stable", '2');

        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(
            first.Outcomes.Single().Article!.IdentitySha256,
            second.Outcomes.Single().Article!.IdentitySha256);
        Assert.AreEqual(
            LuxembourgAknLegalContentProfileProducer.RuleProfileSha256,
            first.Outcomes.Single().Article!.RuleProfileSha256);
        var changed = await RunAsync(
            Akn("<article id=\"art_1\"><num>Art. 1.</num><content><p>changed words</p></content></article>"),
            "stable",
            '3');
        Assert.AreNotEqual(first.IdentitySha256, changed.IdentitySha256);
        Assert.AreNotEqual(
            first.Outcomes.Single().Article!.IdentitySha256,
            changed.Outcomes.Single().Article!.IdentitySha256);
    }

    [TestMethod]
    public async Task PopulationHasOneOutcomePerInventoriedArticleInPublisherOrder()
    {
        var result = await RunAsync(Akn(
            "<article id=\"art_3\"><content><p>third</p></content></article>" +
            "<article id=\"art_1\"><content><p>first</p></content></article>" +
            "<article id=\"art_2\"><content><p>second</p></content></article>"), "ordered");

        CollectionAssert.AreEqual(
            new[] { "art_3", "art_1", "art_2" },
            result.Outcomes.Select(outcome => outcome.Coordinate!.PublisherId).ToArray());
        Assert.AreEqual(
            result.SourceInventoryPopulation.Outcomes.Single().Inventory!.Articles.Count,
            result.Outcomes.Count);
    }

    [TestMethod]
    public async Task MarkerEvidenceWithoutPublisherWordingRemainsATypedGap()
    {
        var result = await RunAsync(Akn(
            "<article id=\"art_1\"><content><p>" +
            "<mod class=\"mod-start\" for=\"#pm1\"/>" +
            "<mod class=\"mod-end\" for=\"#pm1\"/>" +
            "<noteRef href=\"#M1\" marker=\"1\"/>" +
            "</p></content></article>"), "marker-only");

        var outcome = result.Outcomes.Single();
        Assert.AreEqual(
            LuxembourgAknLegalContentDisposition.UnsupportedContentShape,
            outcome.Disposition);
        Assert.IsNull(outcome.Article);
        StringAssert.Contains(outcome.Detail, "no publisher legal wording");
    }

    [TestMethod]
    public async Task PublicPublisherPdfPinsItsReachableUpstreamOutcomeIdentity()
    {
        var query = await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync();
        var inventory = await Stage3EvidenceEnvelopeTests.CompleteAknInventoryAsync(query);
        var producer = new LuxembourgAknLegalContentProfileProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore());

        var first = await producer.RunAsync(inventory, CancellationToken.None);
        var second = await producer.RunAsync(inventory, CancellationToken.None);

        var outcome = first.Outcomes.Single();
        Assert.AreEqual(
            LuxembourgAknLegalContentDisposition.UpstreamNotInventoried,
            outcome.Disposition);
        Assert.AreEqual("NotAkn", outcome.Detail);
        Assert.IsNull(outcome.Coordinate);
        Assert.IsNull(outcome.Article);
        Assert.AreSame(inventory, outcome.SourceInventoryPopulation);
        Assert.AreSame(inventory.Outcomes.Single(), outcome.SourceInventoryOutcome);
        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(
            "47f2c680fafa2b0a2c8bd1692ecb397c2cf31a3f829b4cb04be64af1536de6e7",
            first.IdentitySha256);
    }

    [TestMethod]
    public void PublicDoorAcceptsTheReviewedInventoryPopulationAndNoCallerTokenMap()
    {
        var parameters = typeof(LuxembourgAknLegalContentProfileProducer)
            .GetMethod(nameof(LuxembourgAknLegalContentProfileProducer.RunAsync))!
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgAknArticleInventoryPopulation), typeof(CancellationToken) },
            parameters);
        Assert.IsFalse(parameters.Any(type => type.IsGenericType));
    }

    private static async Task<LuxembourgAknLegalContentPopulation> RunAsync(
        byte[] bytes,
        string key,
        char artifactSuffix = '1')
    {
        var fixture = await Fixture.CreateAsync(bytes, key, artifactSuffix);
        var inventory = await new LuxembourgAknArticleInventoryProducer(fixture.Store)
            .RunAsync(fixture.Population, CancellationToken.None);
        Assert.IsTrue(inventory.Outcomes.All(outcome =>
            outcome.Disposition == LuxembourgAknArticleInventoryDisposition.Inventoried));
        return await new LuxembourgAknLegalContentProfileProducer(fixture.Store)
            .RunAsync(inventory, CancellationToken.None);
    }

    private static async Task<byte[]> RetainedFixtureAsync(string name, string expectedSha256)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", FixtureDirectory, name));
        Assert.AreEqual(expectedSha256, Sha(bytes));
        return bytes;
    }

    private static byte[] Akn(string articles) => Encoding.UTF8.GetBytes(
        "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\" " +
        "xmlns:scl=\"http://www.scl.lu\"><act><body>" + articles +
        "</body></act></akomaNtoso>");

    private sealed record Fixture(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        LuxembourgHeldBodyDerivationPopulation Population)
    {
        internal static async Task<Fixture> CreateAsync(byte[] body, string key, char artifactSuffix)
        {
            var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
            var receipt = await store.CreateAsync(
                body, CustodyClass.NightlyFloor90d, CancellationToken.None);
            var enumeration = Artifact(artifactSuffix, "enumeration"u8);
            var kind = new SourceRegistryMemberRef(enumeration, "lu_document_get_root");
            var publisherUri = "http://data.legilux.public.lu/eli/etat/leg/loi/" + key + "/jo/fr";
            var objectRef = new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Jolux,
                kind,
                publisherUri,
                "lu-akn:" + key,
                Sha(Encoding.UTF8.GetBytes("lu-akn:" + key)),
                enumeration,
                null);
            var manifest = Artifact((char)(artifactSuffix + 2), "manifest"u8);
            var run = Artifact((char)(artifactSuffix + 4), "run"u8);
            var record = new CorpusRecord(
                CorpusRecordSchemaIds.Record,
                objectRef,
                0,
                ScopeDisposition.AcceptedSelected,
                ScopeDisposition.AcceptedSelected,
                ScopeDisposition.AcceptedSelected,
                ScopeDisposition.AcceptedSelected,
                CorpusBodyRecord.Held(receipt),
                manifest,
                run);
            var set = new CorpusRecordSet(CorpusRecordSetSchemaIds.Set, manifest, run, [record]);
            using var canonical = new MemoryStream();
            var setDigest = CorpusRecordSetCanonicalWriter.Write(canonical, set);
            var verified = VerifiedCorpusRecordSet.ParseAndVerify(
                new SourceArtifactRef(
                    $"urn:uuid:00000000-0000-0000-0000-0000000000{artifactSuffix}0", setDigest),
                canonical.ToArray());
            var address = LuxembourgDocumentFetchAddress.Create(
                LuxembourgFileUri.RequireValid(
                    "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/" + key + "/jo/fr/xml"),
                LuxembourgUserFormatToken.XmlAkomaNtoso,
                LuxembourgLegalValue.Unstated,
                "/eli/etat/leg/loi/" + key + "/jo/fr");
            var candidate = new LuxembourgWemiCandidate(
                publisherUri,
                publisherUri + "/expression",
                publisherUri + "/expression/manifestation",
                address.StoreFileUri.Value.AbsoluteUri,
                "http://publications.europa.eu/resource/authority/language/FRA",
                "http://data.legilux.public.lu/resource/authority/user-format/xml-akomantoso",
                enumeration,
                LuxembourgWemiCandidateDisposition.StructurallyConsistent,
                []);
            var population = LuxembourgHeldBodyDerivationPopulation.TryCreate(
                verified,
                new Dictionary<int, CorpusAcquisitionOutcome> { [0] = CorpusAcquisitionOutcome.Held(receipt) },
                new Dictionary<SourceObjectRef, LuxembourgSelectedDocumentFetch>
                {
                    [objectRef] = new LuxembourgSelectedDocumentFetch(address, candidate),
                },
                out var refusal,
                out var detail);
            Assert.AreEqual(LuxembourgHeldBodyDerivationPopulationRefusal.None, refusal, detail);
            Assert.IsNotNull(population);
            return new Fixture(store, population);
        }
    }

    private static SourceArtifactRef Artifact(char suffix, ReadOnlySpan<byte> bytes) => new(
        $"urn:uuid:10000000-0000-0000-0000-00000000000{suffix}", Sha(bytes));

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
