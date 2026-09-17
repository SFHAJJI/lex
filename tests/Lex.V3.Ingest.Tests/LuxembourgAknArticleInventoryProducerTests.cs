using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgAknArticleInventoryProducerTests
{
    [TestMethod]
    public async Task TheRetainedPublisherAknBodyProducesStablePublisherArticleCoordinates()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuDocumentFetch", "lu-xml-200-body.bin"));
        var fixture = await Fixture.CreateAsync(bytes, LuxembourgUserFormatToken.XmlAkomaNtoso);
        var producer = new LuxembourgAknArticleInventoryProducer(fixture.Store);

        var first = await producer.RunAsync(fixture.Population, CancellationToken.None);
        var second = await producer.RunAsync(fixture.Population, CancellationToken.None);

        var outcome = first.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        Assert.IsNotNull(outcome.Inventory);
        Assert.AreNotEqual(
            outcome.Input.CorpusRecord.ObjectRef.PublisherUri,
            outcome.Input.SelectedWemiCandidate.ExpressionIri);
        Assert.AreEqual(
            outcome.Input.SelectedWemiCandidate.ExpressionIri,
            outcome.Inventory.PublisherExpressionIri);
        CollectionAssert.AreEqual(
            new[] { "art_1er", "art_2", "art_3", "art_4", "art_5", "art_6", "art_7", "art_8" },
            outcome.Inventory.Articles.Select(static article => article.PublisherId).ToArray());
        Assert.IsTrue(outcome.Inventory.Articles.All(static article => article.PublisherWId is null));
        Assert.AreSame(fixture.Population, first.SourcePopulation);
        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(outcome.Inventory.IdentitySha256, second.Outcomes.Single().Inventory!.IdentitySha256);
        Assert.AreEqual(
            fixture.Population.Inputs.Single().Receipt.Reference.ContentSha256,
            outcome.TransportReceipt.Reference.ContentSha256);
    }

    [TestMethod]
    public async Task BothPublisherXmlTokensUseOneStableAknRuleProfile()
    {
        var xml = Akn("<article id=\"art_1\" wId=\"/eli/etat/leg/loi/2026/01/01/a1/art_1\">" +
            "<meta><scl:jolux scl:name=\"dateApplicability\">2026-02-03</scl:jolux></meta>" +
            "<num>Art. 1.</num></article>");
        var akn = await Fixture.CreateAsync(xml, LuxembourgUserFormatToken.XmlAkomaNtoso);
        var plain = await Fixture.CreateAsync(xml, LuxembourgUserFormatToken.Xml);

        var first = (await new LuxembourgAknArticleInventoryProducer(akn.Store)
            .RunAsync(akn.Population, CancellationToken.None)).Outcomes.Single().Inventory!;
        var second = (await new LuxembourgAknArticleInventoryProducer(plain.Store)
            .RunAsync(plain.Population, CancellationToken.None)).Outcomes.Single().Inventory!;

        Assert.AreEqual(first.RuleProfileSha256, second.RuleProfileSha256);
        Assert.AreEqual("art_1", first.Articles.Single().PublisherId);
        Assert.AreEqual("/eli/etat/leg/loi/2026/01/01/a1/art_1", first.Articles.Single().PublisherWId);
        Assert.AreEqual("2026-02-03", first.Articles.Single().PublisherApplicability);
    }

    [TestMethod]
    public async Task PdfMembersReceiveOneTypedNonAknOutcomeWithoutParsingTheirBytes()
    {
        var fixture = await Fixture.CreateAsync("not a pdf"u8.ToArray(), LuxembourgUserFormatToken.PdfA);

        var result = await new LuxembourgAknArticleInventoryProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(fixture.Population, CancellationToken.None);

        var outcome = result.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.NotAkn, outcome.Disposition);
        Assert.IsNull(outcome.Inventory);
        Assert.AreEqual("pdfa", outcome.Detail);
    }

    [TestMethod]
    public async Task MissingRetainedBytesRemainAReceiptBoundTypedOutcome()
    {
        var fixture = await Fixture.CreateAsync(Akn("<article id=\"art_1\"/>") , LuxembourgUserFormatToken.Xml);

        var result = await new LuxembourgAknArticleInventoryProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(fixture.Population, CancellationToken.None);

        var outcome = result.Outcomes.Single();
        Assert.AreEqual(
            LuxembourgAknArticleInventoryDisposition.RetainedBytesUnavailable,
            outcome.Disposition);
        Assert.AreSame(fixture.Population.Inputs.Single().Receipt, outcome.TransportReceipt);
        Assert.IsNull(outcome.Inventory);
    }

    [TestMethod]
    public async Task AForeignNamespaceIsRejectedRatherThanReadAsPublisherAkn()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<akomaNtoso xmlns=\"https://example.invalid/akn\"><act><body>" +
            "<article id=\"art_1\"/></body></act></akomaNtoso>");

        var outcome = await RunOne(bytes);

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        StringAssert.Contains(outcome.Detail, "namespace");
    }

    [TestMethod]
    [DataRow("<article/>", "publisher id")]
    [DataRow("<article id=\"art_1\"/><article id=\"art_1\"/>", "duplicate publisher id")]
    [DataRow("<article id=\"art_1\" wId=\"same\"/><article id=\"art_2\" wId=\"same\"/>",
        "duplicate publisher wId")]
    public async Task MissingOrDuplicatePublisherCoordinatesAreRejected(string articles, string expected)
    {
        var outcome = await RunOne(Akn(articles));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        StringAssert.Contains(outcome.Detail, expected);
    }

    [TestMethod]
    public async Task ConflictingArticleApplicabilityValuesAreRejected()
    {
        var xml = Akn("<article id=\"art_1\"><meta>" +
            "<scl:jolux scl:name=\"dateApplicability\">2026-01-01</scl:jolux>" +
            "<scl:jolux scl:name=\"dateApplicability\">2026-02-01</scl:jolux>" +
            "</meta></article>");

        var outcome = await RunOne(xml);

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        StringAssert.Contains(outcome.Detail, "conflicting applicability");
    }

    [TestMethod]
    public async Task DtdsAreRejectedBeforeAnyPublisherCoordinateIsMinted()
    {
        var xml = Encoding.UTF8.GetBytes("<!DOCTYPE akomaNtoso [<!ENTITY x \"art_1\">]>" +
            "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\">" +
            "<act><body><article id=\"&x;\"/></body></act></akomaNtoso>");

        var outcome = await RunOne(xml);

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
    }

    [TestMethod]
    public async Task PublisherXmlBeyondTheDocumentCeilingIsRejected()
    {
        const int oversizedTextLength = (64 * 1024 * 1024) + 1;
        const string prefix =
            "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\">" +
            "<act><body><article id=\"art_1\"><p>";
        const string suffix = "</p></article></body></act></akomaNtoso>";
        var bytes = new byte[prefix.Length + oversizedTextLength + suffix.Length];
        Encoding.UTF8.GetBytes(prefix, bytes);
        bytes.AsSpan(prefix.Length, oversizedTextLength).Fill((byte)'x');
        Encoding.UTF8.GetBytes(suffix, bytes.AsSpan(prefix.Length + oversizedTextLength));

        var outcome = await RunOne(bytes);

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
    }

    private static async Task<LuxembourgAknArticleInventoryOutcome> RunOne(byte[] bytes)
    {
        var fixture = await Fixture.CreateAsync(bytes, LuxembourgUserFormatToken.XmlAkomaNtoso);
        return (await new LuxembourgAknArticleInventoryProducer(fixture.Store)
            .RunAsync(fixture.Population, CancellationToken.None)).Outcomes.Single();
    }

    private static byte[] Akn(string articles) => Encoding.UTF8.GetBytes(
        "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\" " +
        "xmlns:scl=\"http://www.scl.lu\"><act><body>" + articles +
        "</body></act></akomaNtoso>");

    private sealed record Fixture(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        LuxembourgHeldBodyDerivationPopulation Population)
    {
        internal static async Task<Fixture> CreateAsync(
            byte[] body,
            LuxembourgUserFormatToken token)
        {
            var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
            var receipt = await store.CreateAsync(
                body, CustodyClass.NightlyFloor90d, CancellationToken.None);
            var enumeration = Artifact('a', "enumeration"u8);
            var kind = new SourceRegistryMemberRef(enumeration, "lu_document_get_root");
            const string publisherUri =
                "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo/fr";
            var key = "lu-akn:" + publisherUri;
            var objectRef = new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Jolux,
                kind,
                publisherUri,
                key,
                Sha(Encoding.UTF8.GetBytes(key)),
                enumeration,
                null);
            var manifest = Artifact('b', "manifest"u8);
            var run = Artifact('c', "run"u8);
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
                    "urn:uuid:00000000-0000-0000-0000-00000000000d", setDigest),
                canonical.ToArray());
            var address = LuxembourgDocumentFetchAddress.Create(
                LuxembourgFileUri.RequireValid(
                    "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml"),
                token,
                LuxembourgLegalValue.Unstated,
                "/eli/etat/leg/loi/2017/03/14/a439/jo/fr");
            var candidate = new LuxembourgWemiCandidate(
                publisherUri,
                publisherUri + "/expression",
                publisherUri + "/expression/manifestation",
                address.StoreFileUri.Value.AbsoluteUri,
                "http://publications.europa.eu/resource/authority/language/FRA",
                "http://data.legilux.public.lu/resource/authority/user-format/" +
                    FormatName(token),
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

        private static string FormatName(LuxembourgUserFormatToken token) => token switch
        {
            LuxembourgUserFormatToken.XmlAkomaNtoso => "xml-akomantoso",
            LuxembourgUserFormatToken.Xml => "xml",
            LuxembourgUserFormatToken.PdfA => "pdfa",
            LuxembourgUserFormatToken.Pdf => "pdf",
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };
    }

    private static SourceArtifactRef Artifact(char suffix, ReadOnlySpan<byte> bytes) => new(
        $"urn:uuid:00000000-0000-0000-0000-00000000000{suffix}", Sha(bytes));

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
