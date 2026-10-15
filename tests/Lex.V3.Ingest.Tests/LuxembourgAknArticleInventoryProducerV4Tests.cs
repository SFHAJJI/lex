using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Inventory /4: the document's own identification block is evidence and never a rejection, only the
/// main body's top-level articles count, ids follow the publisher-token rule, and six content-
/// consistency checks refuse a file shown to belong to, or copy, another state.
/// </summary>
/// <remarks>
/// <para>
/// REAL BYTES ONLY FROM ADMITTED SOURCES. The excerpts under <c>Fixtures/LuAknInventoryV4</c> are cut
/// from bodies the run admitted under the agreed CC BY reading. Every shape whose real instances
/// are withheld (a consolidation carrying its act's identity, the empty template, copied states,
/// foreign titles) is built here synthetically, with invented wording and invented works such as
/// <c>rgd/2001/02/03/n9</c>; no text in this file is taken from a withheld body.
/// </para>
/// <para>
/// THE CODE CIVIL FILE OF 2019-04-28. Run 15 holds one file for <c>code/civil/20181101</c> and
/// <c>code/civil/20190428</c>. Its <c>FRBRdate</c> and its <c>isExemplifiedBy</c> file name say
/// 2018-11-01; its <c>FRBRthis</c>, its scl block and its in-file date say 2019-04-28. The excerpt
/// keeps that identification block.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgAknArticleInventoryProducerV4Tests
{
    private const string Eli = LuxembourgHeldPopulationBuilder.Eli;
    private const string Profile4Sha256 = "40d79090858058adb470be7fa7bda0f6d54630d4a985e453d6fb4995ce90971d";
    private const string CodePenal = "code-penal-20230101-complex-work";
    private const string ProcedurePenale = "code-procedure-penale-20170401-accented-id";
    private const string A446 = "act-loi-2020-05-20-a446-annex-identification";
    private const string A1020 = "act-loi-2020-12-15-a1020-component-identification";
    private const string A684 = "act-rgd-2018-07-20-a684-embedded-identification";
    private const string A493 = "act-loi-2019-07-12-a493-annex-articles-without-id";
    private const string Civil = "code-civil-20190428-shared-with-20181101";
    private const string Loi1948 = "act-loi-1948-05-15-n1-quoted-article-only";
    private const string A356 = "act-rgd-2021-04-28-a356-annex-ids-with-space";
    private const string Civil20181101 = Eli + "code/civil/20181101";
    private const string Civil20190428 = Eli + "code/civil/20190428";

    private static readonly Dictionary<string, string> FixtureSha256 = new(StringComparer.Ordinal)
    {
        [CodePenal] = "ac6c2253693243467252e35319109890b1193e01fdc5beb2516042cda8a27c00",
        [ProcedurePenale] = "2b36063877c43ddef426087712ef6196bfe59e6addb3fffedaac981bfdc9f4c2",
        [A446] = "81d6dc3958b37a96eee9ae4011f31aaaacb4666072925b240d7a63fe5eb4e375",
        [A1020] = "7ae4118fb79eecc84a5aa39b08a9cc6a69e47b5da32ab3ebcd560e9b7c730311",
        [A684] = "f39096b41a396b91ca5ed98b4f2de495ddebe0c7d2c17075a19ab277f8aa3551",
        [A493] = "cd7e2827e671755b6f1bb3f1da0dcf131113650e4335b69162707ee6c5bbc4a1",
        [Civil] = "62303aa38a47af61ee6efd8ae9bc451397abc31b20b306afeca3dc3f675ea53d",
        [Loi1948] = "e37950bb07fdad0a45d9ec13910c06b31da912df5a2260c1ee43eb09dbbbe7a8",
        [A356] = "b334680854057d8f7df7f459ba6509fc1aad35be07f8e74502d473f1528ad876",
    };

    // ---- The structural reading and the evidence, over admitted excerpts.

    [TestMethod]
    public async Task CodeWhoseComplexWorkIsNotIsMemberOfIsInventoried()
    {
        const string root = Eli + "code/penal/20230101";

        var outcome = await RunOneAsync(root, Fixture(CodePenal));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        var inventory = outcome.Inventory!;
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.Own, inventory.Evidence!.IdentityClass);
        CollectionAssert.AreEqual(new[] { root }, inventory.Evidence.NamedStates.ToArray());
        Assert.AreEqual(root, inventory.Evidence.InFileLegalResourceIri);
        Assert.AreEqual("2023-01-01", inventory.Evidence.InFileApplicabilityDate);
        CollectionAssert.AreEqual(new[] { "art_1er", "art_2" }, Ids(inventory));
        Assert.AreEqual(0, inventory.Evidence.ExcludedArticleCount);
        Assert.IsNull(inventory.PublisherWorkIri);
        Assert.IsNull(inventory.PublisherLegalResourceIri);
        Assert.IsNull(inventory.PublisherApplicabilityDate);
        Assert.IsEmpty(outcome.ConsistencyFindings);
    }

    [TestMethod]
    public async Task ConsolidationCarryingTheActsJoIdentityIsSameWorkStale()
    {
        const string work = Eli + "rgd/2001/02/03/n9";
        var body = Akn(
            Block(work + "/jo", "2022-01-01", null, work + "/jo/fr/xml", work + "/jo/fr/pdf"),
            Articles("Texte synthétique un.", "Texte synthétique deux."));

        var outcome = await RunOneAsync(work + "/consolide/20220101", body);

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        var evidence = outcome.Inventory!.Evidence!;
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.SameWorkAct, evidence.IdentityClass);
        CollectionAssert.AreEqual(new[] { work + "/jo" }, evidence.NamedStates.ToArray());
        Assert.AreEqual(work + "/jo", evidence.InFileLegalResourceIri);
        Assert.AreEqual("2022-01-01", evidence.InFileApplicabilityDate);
    }

    [TestMethod]
    public async Task AnnexIdentificationBlockIsIgnored()
    {
        var outcome = await RunOneAsync(Eli + "loi/2020/05/20/a446/jo", Fixture(A446));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.Own, outcome.Inventory!.Evidence!.IdentityClass);
        CollectionAssert.AreEqual(new[] { "article_unique" }, Ids(outcome.Inventory));
        Assert.AreEqual("2020-05-30", outcome.Inventory.Evidence.InFileApplicabilityDate);
    }

    [TestMethod]
    public async Task ComponentIdentificationBlockIsIgnored()
    {
        var outcome = await RunOneAsync(Eli + "loi/2020/12/15/a1020/jo", Fixture(A1020));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        CollectionAssert.AreEqual(new[] { "article_unique" }, Ids(outcome.Inventory!));
        Assert.AreEqual(2, outcome.Inventory!.Evidence!.ExcludedArticleCount);
    }

    [TestMethod]
    public async Task EmbeddedIdentificationInsideAnArticleIsIgnored()
    {
        var outcome = await RunOneAsync(Eli + "rgd/2018/07/20/a684/jo", Fixture(A684));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.Own, outcome.Inventory!.Evidence!.IdentityClass);
        CollectionAssert.AreEqual(new[] { "art_1er" }, Ids(outcome.Inventory));
    }

    [TestMethod]
    public async Task AccentedIdIsAdmittedVerbatim()
    {
        var outcome = await RunOneAsync(Eli + "code/procedure_penale/20170401", Fixture(ProcedurePenale));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        var ids = Ids(outcome.Inventory!);
        CollectionAssert.AreEqual(new[] { "art_140", "art_141_à_144" }, ids);
        Assert.AreNotEqual(ids[1].Normalize(NormalizationForm.FormD), ids[1], "the id is kept as the publisher wrote it");
    }

    [TestMethod]
    [DataRow("&#x200B;")]
    [DataRow("&#x200C;")]
    [DataRow("&#x200D;")]
    [DataRow("&#x2060;")]
    [DataRow("&#xFEFF;")]
    [DataRow("&#x202A;")]
    [DataRow("&#x202E;")]
    [DataRow("&#x2066;")]
    [DataRow("&#x2069;")]
    [DataRow("&#x61C;")]
    [DataRow("&#x200E;")]
    [DataRow("&#x200F;")]
    [DataRow("&#xA0;")]
    [DataRow("&#x3000;")]
    [DataRow("&#x85;")]
    [DataRow("&#x81;")]
    [DataRow("&#x9;")]
    [DataRow(" ")]
    public async Task ForbiddenCodePointInAnIdRefuses(string inserted)
    {
        var xml = ReplaceOnce(
            Encoding.UTF8.GetString(Fixture(ProcedurePenale)),
            "<article id=\"art_140\"",
            "<article id=\"art_" + inserted + "140\"");

        var outcome = await RunOneAsync(Eli + "code/procedure_penale/20170401", Encoding.UTF8.GetBytes(xml));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        Assert.AreEqual("main-body article has no valid publisher id", outcome.Detail);
        Assert.IsNull(outcome.Inventory);
    }

    [TestMethod]
    public async Task IdLongerThan512Refuses()
    {
        const string root = Eli + "rgd/2001/02/03/n9/jo";
        var admitted = await RunOneAsync(root, Akn(null,
            "<article id=\"" + new string('x', 512) + "\"><p>Texte synthétique.</p></article>"));
        var refused = await RunOneAsync(root, Akn(null,
            "<article id=\"" + new string('x', 513) + "\"><p>Texte synthétique.</p></article>"));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, admitted.Disposition);
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, refused.Disposition);
        Assert.AreEqual("main-body article has no valid publisher id", refused.Detail);
    }

    [TestMethod]
    public async Task AnnexArticlesWithoutIdAreExcludedAndCounted()
    {
        var outcome = await RunOneAsync(Eli + "loi/2019/07/12/a493/jo", Fixture(A493));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        CollectionAssert.AreEqual(new[] { "article_unique" }, Ids(outcome.Inventory!));
        Assert.AreEqual(3, outcome.Inventory!.Evidence!.ExcludedArticleCount);
    }

    [TestMethod]
    public async Task AnnexIdsWithSpacesDoNotRefuse()
    {
        var outcome = await RunOneAsync(Eli + "rgd/2021/04/28/a356/jo", Fixture(A356));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        CollectionAssert.AreEqual(new[] { "art_1er" }, Ids(outcome.Inventory!));
        Assert.AreEqual(2, outcome.Inventory!.Evidence!.ExcludedArticleCount);
    }

    /// <summary>
    /// <c>loi/1948/05/15/n1</c> serves the Constitution's Article 52 as the law's own article under /3,
    /// and loses the amending sentence. The quoted article is another instrument's.
    /// </summary>
    [TestMethod]
    public async Task QuotedArticleOutsideAnyArticleIsNotTheDocuments()
    {
        var outcome = await RunOneAsync(Eli + "loi/1948/05/15/n1/jo", Fixture(Loi1948));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        Assert.AreEqual("publisher XML contains no main-body article", outcome.Detail);
    }

    [TestMethod]
    public async Task EmptyTemplateIsTemplateEmpty()
    {
        const string template =
            "<identification source=\"#casemates\">" +
            "<FRBRWork><FRBRthis value=\"\"/><FRBRuri value=\"\"/><FRBRdate date=\"2999-01-01\" name=\"\"/></FRBRWork>" +
            "<FRBRExpression><FRBRthis value=\"\"/><FRBRuri value=\"\"/><FRBRdate date=\"2999-01-01\" name=\"\"/></FRBRExpression>" +
            "<FRBRManifestation><FRBRthis value=\"\"/><FRBRuri value=\"\"/><FRBRdate date=\"2999-01-01\" name=\"\"/></FRBRManifestation>" +
            "<scl:JOLUXWork><scl:JOLUXComplexWork><scl:jolux scl:name=\"uriThis\"/></scl:JOLUXComplexWork>" +
            "<scl:JOLUXLegalResource><scl:jolux scl:name=\"dateDocument\">2001-02-03</scl:jolux></scl:JOLUXLegalResource>" +
            "</scl:JOLUXWork>" +
            "<scl:JOLUXExpression><scl:jolux scl:name=\"title\">Règlement grand-ducal synthétique du 3 février 2001</scl:jolux></scl:JOLUXExpression>" +
            "<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\"/></scl:JOLUXManifestation>" +
            "</identification>";

        var outcome = await RunOneAsync(
            Eli + "rgd/2001/02/03/n9/jo", Akn(template, Articles("Texte synthétique.")));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        var evidence = outcome.Inventory!.Evidence!;
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.TemplateEmpty, evidence.IdentityClass);
        Assert.IsEmpty(evidence.NamedStates);
        Assert.IsNull(evidence.InFileLegalResourceIri);
        Assert.IsNull(evidence.InFileApplicabilityDate);
    }

    [TestMethod]
    public async Task DuplicateIdStillRejects()
    {
        var outcome = await RunOneAsync(Eli + "rgd/2001/02/03/n9/jo", Akn(null,
            "<article id=\"art_1\"><p>Un.</p></article><article id=\"art_1\"><p>Deux.</p></article>"));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        Assert.AreEqual("duplicate publisher id: art_1", outcome.Detail);
    }

    [TestMethod]
    public async Task DuplicateWIdRejects()
    {
        var outcome = await RunOneAsync(Eli + "rgd/2001/02/03/n9/jo", Akn(null,
            "<article id=\"art_1\" wId=\"same\"><p>Un.</p></article>" +
            "<article id=\"art_2\" wId=\"same\"><p>Deux.</p></article>"));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        Assert.AreEqual("duplicate publisher wId: same", outcome.Detail);
    }

    [TestMethod]
    public async Task NoOwnBlockIsAbsent()
    {
        var outcome = await RunOneAsync(Eli + "rgd/2001/02/03/n9/jo", Akn(null, Articles("Texte synthétique.")));

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        var evidence = outcome.Inventory!.Evidence!;
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.Absent, evidence.IdentityClass);
        Assert.IsEmpty(evidence.NamedStates);
        Assert.IsNull(evidence.InFileLegalResourceIri);
        Assert.IsNull(evidence.InFileApplicabilityDate);
    }

    [TestMethod]
    public async Task TwoOwnBlocksReject()
    {
        const string root = Eli + "rgd/2001/02/03/n9/jo";
        var block = Block(root, null, null, root + "/fr/xml");
        var body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Akn(block, Articles("Texte synthétique.")))
            .Replace("</identification>", "</identification>" + block, StringComparison.Ordinal));

        var outcome = await RunOneAsync(root, body);

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.XmlRejected, outcome.Disposition);
        Assert.AreEqual("publisher AKN document has more than one own identification block", outcome.Detail);
    }

    /// <summary>
    /// The wording comparator of checks (e) and (f) agrees with the reference implementation on every
    /// inventoried excerpt, which covers annex and component containers and an accented id.
    /// </summary>
    [TestMethod]
    [DataRow(CodePenal, "d2d3910e4e01703aa381f4f0ef95077eea8850d3860195cfc4d7c5fe4264fc71")]
    [DataRow(ProcedurePenale, "f9309a62087f9385f11e053d3732d0125790ca960d9dc6228b3f618e108415ca")]
    [DataRow(A446, "1d139cbfde7159b69226cfe44d4a3207d638b90de6bf89da13c82c2b1dd5de2d")]
    [DataRow(A1020, "50cdf6e2df29dbc56f1bd9e7054881da95c94b66da7208a0455be0c58b59e009")]
    [DataRow(A684, "af323f099caa508bf9666652363bd9fdd838836c6666315336f364fbab61f35e")]
    [DataRow(A493, "db3f8903f95f24b1c2576b4092d3b8d02cb75f17010e0e35ba71a6b2f2ca72e3")]
    [DataRow(Civil, "19859e77daefdec4416183a2b1537f73f4574127d8c9957a806a130bdc616257")]
    [DataRow(A356, "e0e491f2e776d0761452483af4473e4a32dc3c1adcab9f6039c2204a6615de4a")]
    public void WordingDigestsMatchTheReference(string fixture, string expected)
    {
        Assert.AreEqual(expected, LuxembourgAknArticleInventoryProducerV4.WordingSha256(Fixture(fixture)));
    }

    // ---- The content-consistency checks.

    [TestMethod]
    public async Task SharedBodyRefusesEveryStateHoldingIt()
    {
        var population = await RunAsync(
            null,
            new LuxembourgHeldPopulationBuilder.Member(Civil20181101, Fixture(Civil)),
            new LuxembourgHeldPopulationBuilder.Member(Civil20190428, Fixture(Civil)));

        var older = population.Outcomes[0];
        var newer = population.Outcomes[1];
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState, older.Disposition);
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState, newer.Disposition);
        Assert.Contains(new LuxembourgAknContentConsistencyFinding(
            LuxembourgAknContentConsistencyCheck.SharedFile, Civil20190428), older.ConsistencyFindings);
        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.SharedFile, Civil20181101) },
            newer.ConsistencyFindings.ToArray());
        Assert.IsNotNull(newer.Inventory?.Evidence, "the refused state keeps its inventory as evidence");
    }

    [TestMethod]
    public async Task BlockNamingALaterStateRefuses()
    {
        var outcome = (await RunAsync(
            null, new LuxembourgHeldPopulationBuilder.Member(Civil20181101, Fixture(Civil)))).Outcomes.Single();

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState, outcome.Disposition);
        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.LaterState, Civil20190428) },
            outcome.ConsistencyFindings.ToArray());
        Assert.AreEqual("content consistency: later_state", outcome.Detail);
    }

    [TestMethod]
    public async Task InFileDateAtOrAfterNextStartRefuses()
    {
        const string work = Eli + "rgd/2002/03/04/n8";
        const string root = work + "/consolide/20200101";
        var catalogue = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [root] = "2020-01-01",
            [work + "/consolide/20210601"] = "2021-06-01",
        };

        var ahead = (await RunAsync(catalogue, new LuxembourgHeldPopulationBuilder.Member(
            root, Akn(Block(root, "2021-06-01", null, root + "/fr/xml"), Articles("Texte synthétique.")))))
            .Outcomes.Single();
        var before = (await RunAsync(catalogue, new LuxembourgHeldPopulationBuilder.Member(
            root, Akn(Block(root, "2021-05-31", null, root + "/fr/xml"), Articles("Texte synthétique.")))))
            .Outcomes.Single();

        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(
                LuxembourgAknContentConsistencyCheck.DateAhead, work + "/consolide/20210601") },
            ahead.ConsistencyFindings.ToArray());
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, before.Disposition);
    }

    /// <summary>
    /// The one file Run 15 holds for both Code civil states fires four checks on the older one, and
    /// every one is recorded, in the design's order, while its inventory stays as evidence.
    /// </summary>
    [TestMethod]
    public async Task EveryFiredCheckIsRecordedInOrder()
    {
        var catalogue = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Civil20181101] = "2018-11-01",
            [Civil20190428] = "2019-04-28",
        };

        var outcome = (await RunAsync(
            catalogue,
            new LuxembourgHeldPopulationBuilder.Member(Civil20181101, Fixture(Civil)),
            new LuxembourgHeldPopulationBuilder.Member(Civil20190428, Fixture(Civil)))).Outcomes[0];

        CollectionAssert.AreEqual(
            new[]
            {
                new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.SharedFile, Civil20190428),
                new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.LaterState, Civil20190428),
                new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.DateAhead, Civil20190428),
                new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.CopiedState, Civil20190428),
            },
            outcome.ConsistencyFindings.ToArray());
        Assert.AreEqual(
            "content consistency: shared_file,later_state,date_ahead,copied_state", outcome.Detail);
        var evidence = outcome.Inventory!.Evidence!;
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.SameWorkOtherState, evidence.IdentityClass);
        Assert.AreEqual(Civil20190428, evidence.InFileLegalResourceIri);
        Assert.AreEqual("2019-04-28", evidence.InFileApplicabilityDate);
    }

    [TestMethod]
    public async Task CopiedStateRefuses()
    {
        var population = await CopiedStatePairAsync("Annexe synthétique.", "Annexe synthétique.");

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, population.Outcomes[0].Disposition);
        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(
                LuxembourgAknContentConsistencyCheck.CopiedState, Eli + "rgd/2003/04/05/n7/consolide/20200101") },
            population.Outcomes[1].ConsistencyFindings.ToArray());
    }

    [TestMethod]
    public async Task StateWhoseAnnexDiffersIsNotACopy()
    {
        var population = await CopiedStatePairAsync("Annexe synthétique.", "Annexe synthétique modifiée.");

        Assert.IsTrue(population.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknArticleInventoryDisposition.Inventoried));
    }

    [TestMethod]
    public async Task CopiedActAfterAnEarlierConsolidationRefuses()
    {
        const string work = Eli + "rgd/2004/05/06/n6";
        var catalogue = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [work + "/consolide/20150101"] = "2015-01-01",
            [work + "/consolide/20200101"] = "2020-01-01",
        };

        var population = await RunAsync(
            catalogue,
            ActMember(work),
            new LuxembourgHeldPopulationBuilder.Member(
                work + "/consolide/20150101",
                Akn(Block(work + "/consolide/20150101", "2015-01-01", null, work + "/consolide/20150101/fr/xml"),
                    Articles("Texte synthétique consolidé."))),
            LaterConsolidationCopyingTheAct(work));

        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.CopiedAct, work + "/jo") },
            population.Outcomes[2].ConsistencyFindings.ToArray());
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, population.Outcomes[0].Disposition);
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, population.Outcomes[1].Disposition);
    }

    [TestMethod]
    public async Task FirstConsolidationEqualToItsActStaysServed()
    {
        const string work = Eli + "rgd/2004/05/06/n6";
        var catalogue = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [work + "/consolide/20200101"] = "2020-01-01",
        };

        var population = await RunAsync(catalogue, ActMember(work), LaterConsolidationCopyingTheAct(work));

        Assert.IsTrue(population.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknArticleInventoryDisposition.Inventoried));
        Assert.AreEqual(
            LuxembourgAknInFileIdentityClass.SameWorkAct,
            population.Outcomes[1].Inventory!.Evidence!.IdentityClass);
    }

    [TestMethod]
    public async Task OtherWorkWithAForeignTitleRefuses()
    {
        var outcome = await CoordinatedTextAsync(
            "Texte coordonné du règlement grand-ducal du 7 juillet 1999 relatif à un objet synthétique");

        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(
                LuxembourgAknContentConsistencyCheck.OtherWork, Eli + "tc/2010/06/10/n1/jo") },
            outcome.ConsistencyFindings.ToArray());
    }

    [TestMethod]
    public async Task OtherWorkWithAMatchingTitlePasses()
    {
        var outcome = await CoordinatedTextAsync(
            "Texte coordonné du règlement grand-ducal du 5 avril 2010 relatif à un objet synthétique");

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.TcOrTrep, outcome.Inventory!.Evidence!.IdentityClass);
    }

    [TestMethod]
    public async Task UnparsedTitleRefuses()
    {
        var outcome = await CoordinatedTextAsync("Texte coordonné synthétique sans désignation");

        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(
                LuxembourgAknContentConsistencyCheck.OtherWork, Eli + "tc/2010/06/10/n1/jo") },
            outcome.ConsistencyFindings.ToArray());
    }

    /// <summary>A code's title designates no dated instrument, so a code naming another work always refuses.</summary>
    [TestMethod]
    public async Task CodeNamingAnotherWorkRefuses()
    {
        const string root = Eli + "code/synthetique/20200101";
        const string other = Eli + "loi/2001/02/03/n9/jo";
        var outcome = await RunOneAsync(root, Akn(
            Block(root, "2020-01-01", "Loi du 3 février 2001 portant un code synthétique", root + "/fr/xml", other + "/fr/xml"),
            Articles("Texte synthétique.")));

        CollectionAssert.AreEqual(
            new[] { new LuxembourgAknContentConsistencyFinding(LuxembourgAknContentConsistencyCheck.OtherWork, other) },
            outcome.ConsistencyFindings.ToArray());
    }

    // ---- The catalogue and the doors.

    [TestMethod]
    public void CatalogueStartsTakeOneExactDateApplicabilityPerConsolidationOrCodeState()
    {
        const string single = Eli + "rgd/2001/02/03/n9/consolide/20200101";
        const string repeated = Eli + "rgd/2001/02/03/n9/consolide/20200301";
        const string twice = Eli + "rgd/2001/02/03/n9/consolide/20210101";
        const string act = Eli + "rgd/2001/02/03/n9/jo";
        const string inexact = Eli + "code/synthetique/20200230";
        const string ended = Eli + "rgd/2001/02/03/n9/consolide/20220101";
        var assertions = new[]
        {
            Applicability(single, "2020-01-01"),
            Applicability(repeated, "2020-03-01"),
            Applicability(repeated, "2020-03-01"),
            Applicability(twice, "2021-01-01"),
            Applicability(twice, "2021-02-01"),
            Applicability(act, "2020-01-01"),
            Applicability(inexact, "2020-02-30"),
            Applicability(ended, "2022-01-01", LuxembourgConsolidationApplicabilityDatePredicate.DateEndApplicability),
        };

        var starts = LuxembourgAknArticleInventoryProducerV4.CatalogueStarts(
            LuxembourgAknArticleInventoryProducerV4.CatalogueDateApplicability(assertions));

        CollectionAssert.AreEquivalent(
            new[] { single + "=2020-01-01", repeated + "=2020-03-01" },
            starts.Select(static pair => pair.Key + "=" + pair.Value).ToArray());
    }

    [TestMethod]
    public async Task TheRunDoorUsesTheRunsOwnPopulation()
    {
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var run = await LuxembourgGazetteAcquisitionTests.CompleteXmlForStage3BodyCompositionAsync(
            Akn(null, Articles("Texte synthétique.")), store);

        var result = await new LuxembourgAknArticleInventoryProducerV4(store).RunAsync(run, CancellationToken.None);

        Assert.AreSame(run.HeldBodyDerivationPopulation, result.SourcePopulation);
        Assert.AreEqual(LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256, result.RuleProfileSha256);
        var outcome = result.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, outcome.Disposition);
        Assert.AreEqual(LuxembourgAknInFileIdentityClass.Absent, outcome.Inventory!.Evidence!.IdentityClass);
    }

    [TestMethod]
    public void PublicDoorTakesTheRunAndNoCallerCatalogue()
    {
        var doors = typeof(LuxembourgAknArticleInventoryProducerV4).GetMethods()
            .Where(static method => method.Name == nameof(LuxembourgAknArticleInventoryProducerV4.RunAsync))
            .ToArray();

        Assert.HasCount(1, doors);
        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgQueryExecutionResult), typeof(CancellationToken) },
            doors[0].GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
    }

    [TestMethod]
    public async Task ARefusedRunIsRefused()
    {
        var delivered = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var refused = LuxembourgQueryExecutionResult.Refused(
            delivered.Topology, [], [],
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained, null, "refused for this test"));
        var producer = new LuxembourgAknArticleInventoryProducerV4(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore());

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => producer.RunAsync(refused, CancellationToken.None));
    }

    // ---- The profile and the identities.

    [TestMethod]
    public void Profile4DigestIsPinned()
    {
        var bytes = LuxembourgAknArticleInventoryProducerV4.CopyRuleProfileBytes();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.AreEqual(Profile4Sha256, LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256);
        Assert.AreEqual(Profile4Sha256, LuxembourgHeldPopulationBuilder.Sha(bytes));
        Assert.HasCount(2019, bytes);
        Assert.AreEqual(21, text.Count(static character => character == '\n'));
        StringAssert.EndsWith(text, "\n");
        StringAssert.StartsWith(text, "lex-v3-luxembourg-akn-article-inventory-profile/4\n");
        Assert.AreNotEqual(Profile4Sha256, LuxembourgAknArticleInventoryProducer.RuleProfileSha256);
    }

    [TestMethod]
    public void ProfileNamesEveryCheckAndClass()
    {
        var profile = Encoding.UTF8.GetString(LuxembourgAknArticleInventoryProducerV4.CopyRuleProfileBytes());

        foreach (var check in Enum.GetValues<LuxembourgAknContentConsistencyCheck>())
        {
            StringAssert.Contains(profile, ContractWire.NameOf(check));
        }
        foreach (var identityClass in Enum.GetValues<LuxembourgAknInFileIdentityClass>())
        {
            StringAssert.Contains(profile, ContractWire.NameOf(identityClass));
        }
        StringAssert.Contains(
            profile, ContractWire.NameOf(LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState));
    }

    /// <summary>
    /// The /4 identity is layout <c>/3</c>, recomputed here from its written order, and every
    /// evidence field moves it. An inventory never carries evidence beside a minted coordinate.
    /// </summary>
    [TestMethod]
    public void Inventory4IdentityCoversTheEvidence()
    {
        const string expression = Eli + "rgd/2001/02/03/n9/consolide/20200101/fr";
        const string state = Eli + "rgd/2001/02/03/n9/consolide/20200101";
        LuxembourgAknArticleCoordinate[] articles = [new("art_1", null, null), new("art_à", "w", "2020-01-02")];
        var evidence = new LuxembourgAknInventoryEvidence(
            LuxembourgAknInFileIdentityClass.Own, [state], state, "2020-01-01", 2);
        var inventory = new LuxembourgAknArticleInventory(
            expression, Profile4Sha256, articles, null, null, null, evidence);

        Assert.AreEqual(Layout3(expression, evidence, articles), inventory.IdentitySha256);
        var variants = new[]
        {
            new LuxembourgAknInventoryEvidence(LuxembourgAknInFileIdentityClass.SameWorkAct, [state], state, "2020-01-01", 2),
            new LuxembourgAknInventoryEvidence(LuxembourgAknInFileIdentityClass.Own, [state, state + "x"], state, "2020-01-01", 2),
            new LuxembourgAknInventoryEvidence(LuxembourgAknInFileIdentityClass.Own, [state], null, "2020-01-01", 2),
            new LuxembourgAknInventoryEvidence(LuxembourgAknInFileIdentityClass.Own, [state], state, null, 2),
            new LuxembourgAknInventoryEvidence(LuxembourgAknInFileIdentityClass.Own, [state], state, "2020-01-01", 3),
        };
        foreach (var variant in variants)
        {
            Assert.AreNotEqual(
                inventory.IdentitySha256,
                new LuxembourgAknArticleInventory(expression, Profile4Sha256, articles, null, null, null, variant).IdentitySha256);
        }
        Assert.AreNotEqual(
            inventory.IdentitySha256,
            new LuxembourgAknArticleInventory(expression, Profile4Sha256, articles, null, null, null).IdentitySha256,
            "the /2 layout of the same articles is a different identity");
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknArticleInventory(
            expression, Profile4Sha256, articles, state, state, "2020-01-01", evidence));
    }

    /// <summary>
    /// The /4 population identity is layout <c>/2</c>, recomputed here: it hashes the rule digest and
    /// every finding, so the same outcomes under another rule, or with another finding, differ.
    /// </summary>
    [TestMethod]
    public async Task Population4IdentityCoversRuleAndFindings()
    {
        var (store, source) = await LuxembourgHeldPopulationBuilder.CreateAsync(
            new LuxembourgHeldPopulationBuilder.Member(Civil20181101, Fixture(Civil)),
            new LuxembourgHeldPopulationBuilder.Member(Civil20190428, Fixture(Civil)),
            new LuxembourgHeldPopulationBuilder.Member(Eli + "rgd/2001/02/03/n9/jo", "%PDF"u8.ToArray(), LuxembourgUserFormatToken.Pdf));
        var population = await new LuxembourgAknArticleInventoryProducerV4(store)
            .RunAsync(source, new Dictionary<string, string>(), CancellationToken.None);

        Assert.AreEqual(Layout2(Profile4Sha256, population.Outcomes), population.IdentitySha256);

        var refused = population.Outcomes[1];
        var renamed = new LuxembourgAknArticleInventoryOutcome(
            refused.Input, refused.Disposition, refused.Inventory, refused.Detail,
            [new(LuxembourgAknContentConsistencyCheck.SharedFile, Civil20181101 + "x")]);
        var changed = new LuxembourgAknArticleInventoryPopulation(
            source, [population.Outcomes[0], renamed, population.Outcomes[2]], Profile4Sha256);
        Assert.AreNotEqual(population.IdentitySha256, changed.IdentitySha256);

        var pdfOnly = new[] { population.Outcomes[2] };
        Assert.AreNotEqual(
            new LuxembourgAknArticleInventoryPopulation(source, pdfOnly, Profile4Sha256).IdentitySha256,
            new LuxembourgAknArticleInventoryPopulation(source, pdfOnly, new string('0', 64)).IdentitySha256);
        Assert.AreNotEqual(
            new LuxembourgAknArticleInventoryPopulation(source, pdfOnly, Profile4Sha256).IdentitySha256,
            new LuxembourgAknArticleInventoryPopulation(source, pdfOnly).IdentitySha256,
            "an unversioned population keeps layout /1");
    }

    /// <summary>A /3 shape and a /4 shape never mix in one outcome or one population.</summary>
    [TestMethod]
    public async Task ThreeAndFourShapesNeverMix()
    {
        var (store, source) = await LuxembourgHeldPopulationBuilder.CreateAsync(
            new LuxembourgHeldPopulationBuilder.Member(Civil20181101, Fixture(Civil)),
            new LuxembourgHeldPopulationBuilder.Member(
                Eli + "rgd/2001/02/03/n9/jo", Akn(null, Articles("Texte synthétique."))));
        var fours = (await new LuxembourgAknArticleInventoryProducerV4(store)
            .RunAsync(source, new Dictionary<string, string>(), CancellationToken.None)).Outcomes;
        var threes = (await new LuxembourgAknArticleInventoryProducer(store)
            .RunAsync(source, CancellationToken.None)).Outcomes;
        var four = fours[0];
        var three = threes[1];
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState, four.Disposition);
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, three.Disposition);

        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknArticleInventoryOutcome(
            four.Input, LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState, four.Inventory, "x"));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknArticleInventoryOutcome(
            four.Input, LuxembourgAknArticleInventoryDisposition.Inventoried, four.Inventory, null,
            four.ConsistencyFindings));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknArticleInventoryOutcome(
            three.Input, LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState, three.Inventory, "x",
            four.ConsistencyFindings));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknArticleInventoryPopulation(source, fours));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknArticleInventoryPopulation(
            source, threes, LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256));
        Assert.AreEqual(
            LuxembourgAknArticleInventoryProducer.RuleProfileSha256,
            new LuxembourgAknArticleInventoryPopulation(source, threes).RuleProfileSha256);
    }

    [TestMethod]
    public async Task IdenticalBytesGiveStableIdentities()
    {
        var first = await RunAsync(null, new LuxembourgHeldPopulationBuilder.Member(Eli + "code/penal/20230101", Fixture(CodePenal)));
        var second = await RunAsync(null, new LuxembourgHeldPopulationBuilder.Member(Eli + "code/penal/20230101", Fixture(CodePenal)));

        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(
            first.Outcomes.Single().Inventory!.IdentitySha256,
            second.Outcomes.Single().Inventory!.IdentitySha256);
    }

    [TestMethod]
    public async Task BothXmlTokensShareProfile4()
    {
        const string root = Eli + "code/penal/20230101";
        var akn = (await RunAsync(null, new LuxembourgHeldPopulationBuilder.Member(
            root, Fixture(CodePenal), LuxembourgUserFormatToken.XmlAkomaNtoso))).Outcomes.Single();
        var xml = (await RunAsync(null, new LuxembourgHeldPopulationBuilder.Member(
            root, Fixture(CodePenal), LuxembourgUserFormatToken.Xml))).Outcomes.Single();

        Assert.AreEqual(Profile4Sha256, akn.Inventory!.RuleProfileSha256);
        Assert.AreEqual(Profile4Sha256, xml.Inventory!.RuleProfileSha256);
        Assert.AreEqual(akn.Inventory.IdentitySha256, xml.Inventory.IdentitySha256);
    }

    [TestMethod]
    public async Task PdfIsNotAkn()
    {
        var outcome = await RunOneAsync(
            Eli + "rgd/2001/02/03/n9/jo", "not a pdf"u8.ToArray(), LuxembourgUserFormatToken.PdfA);

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.NotAkn, outcome.Disposition);
        Assert.AreEqual("pdfa", outcome.Detail);
        Assert.IsNull(outcome.Inventory);
    }

    [TestMethod]
    public async Task MissingCustodyIsRetainedBytesUnavailable()
    {
        var (_, population) = await LuxembourgHeldPopulationBuilder.CreateAsync(
            new LuxembourgHeldPopulationBuilder.Member(Eli + "code/penal/20230101", Fixture(CodePenal)));

        var outcome = (await new LuxembourgAknArticleInventoryProducerV4(
                new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(population, new Dictionary<string, string>(), CancellationToken.None)).Outcomes.Single();

        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.RetainedBytesUnavailable, outcome.Disposition);
        Assert.AreSame(population.Inputs.Single().Receipt, outcome.TransportReceipt);
        Assert.IsNull(outcome.Inventory);
    }

    // ---- Helpers.

    private static async Task<LuxembourgAknArticleInventoryPopulation> RunAsync(
        IReadOnlyDictionary<string, string>? catalogue,
        params LuxembourgHeldPopulationBuilder.Member[] members)
    {
        var (store, population) = await LuxembourgHeldPopulationBuilder.CreateAsync(members);
        return await new LuxembourgAknArticleInventoryProducerV4(store).RunAsync(
            population, catalogue ?? new Dictionary<string, string>(), CancellationToken.None);
    }

    private static async Task<LuxembourgAknArticleInventoryOutcome> RunOneAsync(
        string root,
        byte[] body,
        LuxembourgUserFormatToken token = LuxembourgUserFormatToken.XmlAkomaNtoso) =>
        (await RunAsync(null, new LuxembourgHeldPopulationBuilder.Member(root, body, token))).Outcomes.Single();

    private static Task<LuxembourgAknArticleInventoryPopulation> CopiedStatePairAsync(
        string earlierAnnex,
        string laterAnnex)
    {
        const string work = Eli + "rgd/2003/04/05/n7";
        const string earlier = work + "/consolide/20200101";
        const string later = work + "/consolide/20210101";
        var articles = Articles("Texte synthétique un.", "Texte synthétique deux.");
        return RunAsync(
            null,
            new LuxembourgHeldPopulationBuilder.Member(
                earlier, Akn(Block(earlier, "2020-01-01", null, earlier + "/fr/xml"), articles, Annex(earlierAnnex))),
            new LuxembourgHeldPopulationBuilder.Member(
                later, Akn(Block(earlier, "2021-01-01", null, earlier + "/fr/xml"), articles, Annex(laterAnnex))));
    }

    private static LuxembourgHeldPopulationBuilder.Member ActMember(string work) => new(
        work + "/jo",
        Akn(Block(work + "/jo", "2014-01-01", null, work + "/jo/fr/xml"), Articles("Texte synthétique de l'acte.")));

    private static LuxembourgHeldPopulationBuilder.Member LaterConsolidationCopyingTheAct(string work) => new(
        work + "/consolide/20200101",
        Akn(Block(work + "/jo", "2020-01-01", null, work + "/jo/fr/xml"), Articles("Texte synthétique de l'acte.")));

    private static async Task<LuxembourgAknArticleInventoryOutcome> CoordinatedTextAsync(string title)
    {
        const string root = Eli + "rgd/2010/04/05/n2/consolide/20100601";
        return await RunOneAsync(root, Akn(
            Block(Eli + "tc/2010/06/10/n1/jo", null, title, Eli + "tc/2010/06/10/n1/jo/fr/xml"),
            Articles("Texte synthétique.")));
    }

    private static LuxembourgTypedAssertion Applicability(
        string subject,
        string value,
        LuxembourgConsolidationApplicabilityDatePredicate predicate =
            LuxembourgConsolidationApplicabilityDatePredicate.DateApplicability)
    {
        var evidenceRef = new SourceArtifactRef(
            "urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0", new string('1', 64));
        var underlying = predicate == LuxembourgConsolidationApplicabilityDatePredicate.DateApplicability
            ? LuxembourgAssertionPredicate.DateApplicability
            : LuxembourgAssertionPredicate.DateEndApplicability;
        var local = underlying == LuxembourgAssertionPredicate.DateApplicability
            ? "dateApplicability"
            : "dateEndApplicability";
        return new LuxembourgTypedAssertion(
            new LuxembourgObservedAssertion(
                subject,
                "http://data.legilux.public.lu/resource/ontology/jolux#" + local,
                LuxembourgAssertionObjectKind.Literal,
                value,
                "http://www.w3.org/2001/XMLSchema#date",
                string.Empty,
                evidenceRef),
            new LuxembourgAssertionFactDisposition(underlying, LuxembourgAssertionFactKind.ConsolidationApplicability, evidenceRef),
            null,
            new LuxembourgConsolidationApplicabilityDateFact(
                predicate, value, "http://www.w3.org/2001/XMLSchema#date", evidenceRef));
    }

    private static byte[] Fixture(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknInventoryV4", name + ".bin"));
        Assert.AreEqual(FixtureSha256[name], LuxembourgHeldPopulationBuilder.Sha(bytes), name);
        return bytes;
    }

    private static string[] Ids(LuxembourgAknArticleInventory inventory) =>
        inventory.Articles.Select(static article => article.PublisherId).ToArray();

    /// <summary>A synthetic AKN act: an optional own identification block, a body, and anything after it.</summary>
    private static byte[] Akn(string? identification, string body, string? afterBody = null) =>
        Encoding.UTF8.GetBytes(
            "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\" " +
            "xmlns:scl=\"http://www.scl.lu\"><act>" +
            (identification is null ? "" : "<meta>" + identification + "</meta>") +
            "<body>" + body + "</body>" + (afterBody ?? "") + "</act></akomaNtoso>");

    /// <summary>
    /// A synthetic own identification block: one legal resource (its <c>uriThis</c>, and a
    /// <c>dateApplicability</c> when given), an expression title when given, and one manifestation per
    /// <c>uriThis</c>. FRBR is never read by /4, so it is left out.
    /// </summary>
    private static string Block(string legalResource, string? date, string? title, params string[] manifestations) =>
        "<identification source=\"#synthetic\"><scl:JOLUXWork><scl:JOLUXLegalResource>" +
        "<scl:jolux scl:name=\"uriThis\">" + legalResource + "</scl:jolux>" +
        (date is null ? "" : "<scl:jolux scl:name=\"dateApplicability\">" + date + "</scl:jolux>") +
        "</scl:JOLUXLegalResource></scl:JOLUXWork><scl:JOLUXExpression>" +
        (title is null ? "" : "<scl:jolux scl:name=\"title\">" + title + "</scl:jolux>") +
        "</scl:JOLUXExpression>" +
        string.Concat(manifestations.Select(static manifestation =>
            "<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\">" + manifestation +
            "</scl:jolux></scl:JOLUXManifestation>")) +
        "</identification>";

    private static string Articles(params string[] texts) => string.Concat(texts.Select(static (text, index) =>
        "<article id=\"art_" + (index + 1).ToString(CultureInfo.InvariantCulture) + "\"><num>Art. " +
        (index + 1).ToString(CultureInfo.InvariantCulture) + ".</num><alinea><content><p>" + text +
        "</p></content></alinea></article>"));

    private static string Annex(string text) =>
        "<attachments><attachment><doc name=\"annexe\"><mainBody><p>" + text + "</p></mainBody></doc></attachment></attachments>";

    private static string ReplaceOnce(string value, string oldValue, string newValue)
    {
        var index = value.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, index, oldValue);
        return string.Concat(value.AsSpan(0, index), newValue, value.AsSpan(index + oldValue.Length));
    }

    private static string Layout3(
        string expression,
        LuxembourgAknInventoryEvidence evidence,
        IReadOnlyList<LuxembourgAknArticleCoordinate> articles)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Put(hash, "lex-v3-luxembourg-akn-article-inventory/3");
        Put(hash, expression);
        Put(hash, Profile4Sha256);
        Put(hash, ContractWire.NameOf(evidence.IdentityClass));
        Put(hash, evidence.NamedStates.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var state in evidence.NamedStates)
        {
            Put(hash, state);
        }
        Put(hash, evidence.InFileLegalResourceIri ?? "");
        Put(hash, evidence.InFileApplicabilityDate ?? "");
        Put(hash, evidence.ExcludedArticleCount.ToString(CultureInfo.InvariantCulture));
        foreach (var article in articles)
        {
            Put(hash, article.PublisherId);
            Put(hash, article.PublisherWId ?? "");
            Put(hash, article.PublisherApplicability ?? "");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Layout2(string rule, IReadOnlyList<LuxembourgAknArticleInventoryOutcome> outcomes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Put(hash, "lex-v3-luxembourg-akn-article-inventory-population/2");
        Put(hash, rule);
        foreach (var outcome in outcomes)
        {
            Put(hash, outcome.Input.CorpusRecord.ObjectRef.PublisherUri);
            Put(hash, ((int)outcome.Disposition).ToString(CultureInfo.InvariantCulture));
            Put(hash, outcome.Inventory?.IdentitySha256 ?? "");
            Put(hash, outcome.ConsistencyFindings.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var finding in outcome.ConsistencyFindings)
            {
                Put(hash, ContractWire.NameOf(finding.Check));
                Put(hash, finding.NamedState);
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Put(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
