using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgIndexBuilderTests
{
    [TestMethod]
    public void FixedLogicalInputPinsTheExactLuxembourgIndexBytes()
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(
            LuxembourgIndexBuilder.BuildFixedInputDeterminismEvidence()));
        Assert.AreEqual("7cfd99881fc878616e96b067342f955df11ae908147f69c04ff64fcfce439184", digest);
    }

    /// <summary>
    /// The publisher's journal documents carry their applicability on the document and none of the Luxembourg population's
    /// 8,854 articles carries a date of its own (the mounted gates of 2026-10-13): measured by the article's own date alone,
    /// the capability manifest stated no searchable text and search answered nothing on the real mount. A search cell counts
    /// an article at its own date where it has one, else at the date of the state of its language that holds it.
    /// </summary>
    [TestMethod]
    public void ASearchCellCountsAnArticleAtItsOwnDateElseAtTheDateOfTheStateThatHoldsIt()
    {
        static LuxembourgIndexBuilder.ArticleRow Article(char identity, string? date, string language, string text) => new(
            new string(identity, 64), new string('1', 64), "https://example.invalid/expression", $"art_{identity}", null, date,
            language, new string('4', 64), text, "[]");
        var articles = new[]
        {
            Article('a', null, "fra", "sans date propre"),
            Article('b', "2023-01-01", "fra", "datée"),
            Article('c', null, "fra", "dans aucun état"),
            Article('d', null, "fra", ""),
            Article('e', null, "eng", "another language"),
        };
        var state = new LuxembourgIndexBuilder.StateRow(
            "loi-2024-01-01-n1", "2024-02-01", new string('5', 64), "https://example.invalid/expression",
            "http://data.legilux.public.lu/eli/etat/leg/loi/2024/01/01/n1", "http://data.legilux.public.lu/eli/etat/leg/loi/2024/01/01/n1/jo",
            "fra", "[]", System.Text.Json.JsonSerializer.Serialize(new[] { 'a', 'b', 'd', 'e' }.Select(static c => new string(c, 64))));
        var digest = new string('9', 64);

        // a at its state's date; b at its own; c in no state, d without text and e of another language than its state: none.
        var cells = LuxembourgIndexBuilder.MeasureCapabilities(digest, articles, [state]).Cells
            .Select(static cell => $"{cell.Operation} {cell.Language} {cell.PeriodFrom:yyyy-MM-dd}..{cell.PeriodTo:yyyy-MM-dd} {cell.Population}")
            .ToArray();
        CollectionAssert.AreEqual(new[] { "search fra 2023-01-01..2023-01-01 1", "search fra 2024-02-01..2024-02-01 1" }, cells);

        // The earlier rule, which an index built before the fix states and the reader still accepts, counted b alone.
        var earlier = LuxembourgIndexBuilder.MeasureCapabilitiesByArticleDates(digest, articles).Cells.Single();
        Assert.AreEqual(new DateOnly(2023, 1, 1), earlier.PeriodFrom);
    }

    /// <summary>
    /// The publisher dates its journal documents on the legal resource (<c>.../jo</c>) and titles them on the expression
    /// (<c>.../jo/fr</c>): none of the population's 2,021 titles had a date before the legal resource was asked. A title's
    /// date is its own subject's, else its article's work identifier's, else its expression's legal resource's, else the
    /// article's own date.
    /// </summary>
    [TestMethod]
    public void ATitleIsDatedByItsSubjectThenItsWorkThenItsLegalResourceThenItsArticle()
    {
        const string Expression = "http://data.legilux.public.lu/eli/etat/leg/loi/2018/04/17/a257/jo/fr";
        const string LegalResource = "http://data.legilux.public.lu/eli/etat/leg/loi/2018/04/17/a257/jo";
        const string Work = "http://data.legilux.public.lu/eli/etat/leg/loi/2018/04/17/a257";
        var article = new LuxembourgIndexBuilder.ArticleRow(
            new string('2', 64), new string('1', 64), Expression, "art_1", null, "2018-04-23", "fra", new string('4', 64), "texte", "[]");
        var legalResources = new Dictionary<string, string>(StringComparer.Ordinal) { [Expression] = LegalResource };
        string? Dated(Dictionary<string, string> dates, LuxembourgIndexBuilder.ArticleRow row) =>
            LuxembourgIndexBuilder.TitleDocumentDate(Expression, row, dates, legalResources);

        Assert.AreEqual("2018-04-17", Dated(new(StringComparer.Ordinal) { [LegalResource] = "2018-04-17" }, article), "the legal resource's, the population's case");
        Assert.AreEqual("2018-04-16", Dated(new(StringComparer.Ordinal) { [Expression] = "2018-04-16", [LegalResource] = "2018-04-17" }, article), "the subject's own first");
        Assert.AreEqual("2018-04-15", Dated(new(StringComparer.Ordinal) { [Work] = "2018-04-15", [LegalResource] = "2018-04-17" }, article with { PublisherWid = Work }), "then the work's");
        Assert.AreEqual("2018-04-23", Dated(new(StringComparer.Ordinal), article), "the article's own date last");
        Assert.IsNull(Dated(new(StringComparer.Ordinal), article with { ApplicabilityDate = null }), "and none when nothing dates it");
        Assert.IsNull(LuxembourgIndexBuilder.TitleDocumentDate(Expression, article with { ApplicabilityDate = null },
            new Dictionary<string, string>(StringComparer.Ordinal) { [LegalResource] = "2018-04-17" }, new Dictionary<string, string>(StringComparer.Ordinal)),
            "an expression whose legal resource is not named takes none of its dates");
    }

    internal const string Retained1991 = "loi-1991-08-10-n3--2024-02-01--fr.bin";

    /// <summary>
    /// The build time every genesis build here records, later than any fixture's EU capture; a chained build records
    /// <see cref="Later"/> and a build chained to that one <see cref="Latest"/>, as builds of one log run in time order.
    /// </summary>
    internal static readonly DateTimeOffset BuiltAt = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    internal const string BuiltAtText = "2026-10-01T08:00:00Z";

    internal static readonly DateTimeOffset Later = BuiltAt.AddHours(1);

    internal const string LaterText = "2026-10-01T09:00:00Z";

    internal static readonly DateTimeOffset Latest = BuiltAt.AddHours(2);

    /// <summary>The Luxembourg capability manifest states the platform's operations not served, as the API's coverage does.</summary>
    [TestMethod]
    public async Task TheCapabilityManifestStatesTheOperationsNotServed()
    {
        var (_, built, _) = await BuildStateEnvelopeAsync();
        CollectionAssert.AreEqual(
            Lex.V3.Contracts.Platform.V3UnservedOperations.Rows.ToArray(),
            built.CapabilityManifest.NotServed.ToArray());
        StringAssert.Contains(Encoding.UTF8.GetString(built.CapabilityManifestBytes.Span), "\"not_served\":[{\"operation\":\"concepts\",\"reason\":\"operation_not_served\"");
    }

    [TestMethod]
    public void BuilderAndStrictReaderShipAsOneTerminalSlice()
    {
        var schema = (string)typeof(LuxembourgIndexBuilder)
            .GetField(nameof(LuxembourgIndexBuilder.Schema))!
            .GetRawConstantValue()!;
        Assert.AreEqual("lex-v3-luxembourg-index/9", schema);
        Assert.IsTrue(typeof(LuxembourgIndexBuilder).GetMethods().Any(static method => method.Name == nameof(LuxembourgIndexBuilder.TryBuild)));
        Assert.IsNotNull(typeof(LuxembourgIndexReader).GetMethod(nameof(LuxembourgIndexReader.OpenAndVerify)));
    }

    [TestMethod]
    [DataRow("https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3")]
    [DataRow("http://example.invalid/eli/etat/leg/loi/1991/08/10/n3")]
    [DataRow("http://data.legilux.public.lu/eli/other/loi/1991/08/10/n3")]
    public void WorkKeyProjectionRejectsWrongSchemeHostOrLegalPath(string workIri)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexBuilder.WorkKeyOf(workIri));
    }

    [TestMethod]
    public async Task CompleteEnvelopeBuildsDeterministicLuOnlyIndexAndMeasuredManifest()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var first = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var firstRefusal, out var firstDetail);
        var second = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var secondRefusal, out var secondDetail);

        Assert.IsNotNull(first, $"{firstRefusal}: {firstDetail}");
        Assert.IsNotNull(second, $"{secondRefusal}: {secondDetail}");
        CollectionAssert.AreEqual(first.IndexBytes.ToArray(), second.IndexBytes.ToArray());
        Assert.AreEqual(first.IndexRef, second.IndexRef);
        Assert.AreEqual(first.CapabilityManifestRef, second.CapabilityManifestRef);
        CollectionAssert.AreEqual(
            first.CapabilityManifestBytes.ToArray(),
            second.CapabilityManifestBytes.ToArray());
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            second.CapabilityManifest.Cells.ToArray());
        Assert.AreEqual(PublisherId.LuLegilux, first.CapabilityManifest.Publisher);
        Assert.AreEqual(first.IndexRef.Sha256, first.CapabilityManifest.IndexSha256);
        var reopenedManifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            first.CapabilityManifestRef,
            first.CapabilityManifestBytes.Span,
            PublisherId.LuLegilux,
            first.IndexRef.Sha256);
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            reopenedManifest.Cells.ToArray());

        using var reader = LuxembourgIndexReader.OpenAndVerify(
            first.IndexRef,
            first.IndexBytes.Span,
            corpus.ArtifactRef,
            first.CapabilityManifest);
        var expectedLuMembers = corpus.VerifiedSet.Set.Members.Count(static member =>
            member.Publisher == PublisherId.LuLegilux);
        Assert.AreEqual(expectedLuMembers, reader.MemberCount);
        Assert.IsTrue(reader.MemberCount < corpus.VerifiedSet.Set.Members.Count,
            "The Luxembourg index must not contain the EU population.");
        Assert.IsTrue(corpus.VerifiedSet.Set.Members.Any(static member =>
            member.Publisher == PublisherId.LuLegilux &&
            member.Outcome == LexCorpus6OutcomeKind.RightsWithheld),
            "The accepted fixture must exercise the retained rights-withheld terminal path.");
        Assert.AreEqual(0, reader.ArticleCount,
            "A rights-withheld member remains population evidence but cannot become legal text.");
        Assert.IsEmpty(first.CapabilityManifest.Cells,
            "No searchable row means no advertised capability.");
        var unsupported = reader.Search(
            "deu", new DateOnly(1900, 1, 1), new DateOnly(2100, 1, 1), "law");
        Assert.AreEqual(
            V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex,
            unsupported.Outcome);
        Assert.IsEmpty(unsupported.ArticleIdentities);
    }

    [TestMethod]
    public async Task RetainedPublisherAknProducesDayExactCapabilitiesWithoutAdvertisingTheGap()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var xml = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        Assert.AreEqual(
            "3a6bb598a9310f8a31240c1f33ae357d6e1f7a46392ca33d223c2718cdded95c",
            Convert.ToHexStringLower(SHA256.HashData(xml)));
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg,
            luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var inventory = envelope.BodyComposition.Envelope.LuxembourgAknArticleInventoryPopulation
            .Outcomes.Single().Inventory;
        Assert.IsNotNull(inventory);
        Assert.HasCount(54, inventory.Articles);
        Assert.IsTrue(inventory.Articles.All(static article => article.PublisherApplicability is not null));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var luxembourgMembers = corpus.VerifiedSet.Set.Members.Where(static value =>
            value.Publisher == PublisherId.LuLegilux).ToArray();
        var member = luxembourgMembers.Single(static value =>
            value.Outcome == LexCorpus6OutcomeKind.Acquired);
        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, member.Outcome);
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(luxembourgMembers.Length, reader.MemberCount);
        Assert.AreEqual(49, reader.ArticleCount);
        var state = reader.ResolveState("loi-1991-08-10-n3", "2024-02-01").Single();
        Assert.AreEqual(
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3",
            state.PublisherWorkIri);
        Assert.AreEqual(
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo",
            state.PublisherLegalResourceIri);
        Assert.AreEqual(manifestation[..manifestation.LastIndexOf('/')], state.ExpressionIri);
        Assert.HasCount(49, state.ArticleIdentities);
        CollectionAssert.Contains(
            state.RuleProfileSha256s.ToArray(),
            LuxembourgAknArticleInventoryProducer.RuleProfileSha256);
        Assert.HasCount(4, built.CapabilityManifest.Cells);
        var cells = built.CapabilityManifest.Cells.OrderBy(static cell => cell.PeriodFrom).ToArray();
        Assert.AreEqual(new DateOnly(2021, 8, 22), cells[0].PeriodFrom);
        Assert.AreEqual(cells[0].PeriodFrom, cells[0].PeriodTo);
        Assert.AreEqual(36, cells[0].Population);
        Assert.AreEqual(new DateOnly(2023, 9, 16), cells[^1].PeriodFrom);
        Assert.AreEqual(cells[^1].PeriodFrom, cells[^1].PeriodTo);
        Assert.AreEqual(1, cells[^1].Population);
        var expectedArticle = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation
            .Outcomes.Single(static value => value.Article?.Coordinate.PublisherId == "art_1er")
            .Article!;
        var supported = reader.Search(
            "fra", new DateOnly(2021, 8, 22), new DateOnly(2021, 8, 22),
            "La profession d’avocat est une profession libérale et indépendante.");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, supported.Outcome);
        CollectionAssert.AreEqual(
            new[] { expectedArticle.IdentitySha256 }, supported.ArticleIdentities.ToArray());
        var gap = reader.Search(
            "fra", new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), "article");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex, gap.Outcome);
        Assert.IsEmpty(gap.ArticleIdentities);
    }

    [TestMethod]
    public async Task AStatesArticlesPlusTheArticlesItsDocumentHadAndItDoesNotHoldAreTheDocumentsArticles()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var real = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        // The real act has 49 admitted articles and five the reviewed profile cannot represent. One admitted article
        // is made, in memory, an article of nothing but a modification start and end, so that the document has all
        // three kinds of article and the state holds one that is marker-only. Bytes are kept as they are (Latin-1
        // reads and writes any byte), and the replacement must happen exactly once.
        var text = Encoding.Latin1.GetString(real);
        var pattern = new System.Text.RegularExpressions.Regex(
            "<article id=\"art_2\">(?<head>.*?</scl:JOLUXWork>).*?</article>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.AreEqual(1, pattern.Matches(text).Count);
        var changed = pattern.Replace(
            text,
            "<article id=\"art_2\">${head}<content><p><mod class=\"mod-start\" for=\"#pm9\"/><mod class=\"mod-end\" for=\"#pm9\"/></p></content></article>");
        Assert.AreNotEqual(text, changed);
        var xml = Encoding.Latin1.GetBytes(changed);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        // The stage's own dispositions, read from its population and not through the index.
        var outcomes = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes;
        Assert.AreEqual(54, outcomes.Count);
        Assert.AreEqual(48, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.Admitted));
        Assert.AreEqual(1, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence));
        Assert.AreEqual(5, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        var state = reader.ResolveState("loi-1991-08-10-n3", "2024-02-01").Single();
        // 48 admitted and the marker-only one are held, and the five the profile could not represent are not.
        Assert.HasCount(49, state.ArticleIdentities);
        var notAdmitted = reader.ResolveArticlesNotAdmitted([state.StateSha256])[state.StateSha256];
        Assert.AreEqual(5, notAdmitted);
        // The sentence the answers carry: the state's articles plus articles_not_admitted are the document's.
        Assert.AreEqual(outcomes.Count, state.ArticleIdentities.Count + notAdmitted);
    }

    [TestMethod]
    public async Task AnArticleNestedInsideAnotherIsNotRecordedApartFromItsParentSoTheSumIsTheOutcomesAndNotTheElements()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var real = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        // A further article is nested inside art_3, in memory and byte-preserving. The profile's vocabulary does not
        // contain `article`, so it is an element outside it and art_3 is not admitted whole; and the inventory and the
        // profile both count top-level articles only, so the document has 55 article elements and 54 outcomes.
        var text = Encoding.Latin1.GetString(real);
        var pattern = new System.Text.RegularExpressions.Regex(
            "(?<parent><article id=\"art_3\">.*?)</article>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.AreEqual(1, pattern.Matches(text).Count);
        var changed = pattern.Replace(
            text,
            "${parent}<article id=\"art_3_nested\"><content><p>A nested article.</p></content></article></article>");
        var elements = new System.Text.RegularExpressions.Regex("<article[ >]").Matches(changed).Count;
        Assert.AreEqual(55, elements);
        var xml = Encoding.Latin1.GetBytes(changed);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var outcomes = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes;
        Assert.AreEqual(54, outcomes.Count);
        Assert.AreEqual(48, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.Admitted));
        Assert.AreEqual(6, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));
        Assert.IsTrue(outcomes.Any(static value =>
            value.Coordinate?.PublisherId == "art_3" &&
            value.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        var state = reader.ResolveState("loi-1991-08-10-n3", "2024-02-01").Single();
        var notAdmitted = reader.ResolveArticlesNotAdmitted([state.StateSha256])[state.StateSha256];
        Assert.HasCount(48, state.ArticleIdentities);
        Assert.AreEqual(6, notAdmitted);
        // 48 + 6 is the 54 articles the corpus recorded (what `provenance` counts by token), and not the 55 article
        // elements a reader who counts the document finds: which is why the sentence says "recorded" and not "in the
        // document".
        Assert.AreEqual(54, state.ArticleIdentities.Count + notAdmitted);
        Assert.AreNotEqual(elements, state.ArticleIdentities.Count + notAdmitted);
    }

    [TestMethod]
    public async Task AdmittedAknTextWithoutAdmittingRightsNeverBecomesSearchable()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{manifestation}}"/></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{manifestation}}</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta><body>
                <article id="art_1"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p>Withheld publisher words.</p></content></article>
              </body></act>
            </akomaNtoso>
            """);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(
                xml, store, manifestation, item, includeEndpointLicence: false);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        Assert.IsTrue(envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes
            .Any(static value => value.Disposition == LuxembourgAknLegalContentDisposition.Admitted));
        Assert.IsTrue(corpus.VerifiedSet.Set.Members.Any(static value =>
            value.Publisher == PublisherId.LuLegilux &&
            value.Outcome == LexCorpus6OutcomeKind.RightsWithheld));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(0, reader.ArticleCount);
        Assert.IsEmpty(built.CapabilityManifest.Cells);

        // The withheld member's own outcome list records the admitted outcome, and the coverage count of the
        // corpus's outcomes leaves it out because the index holds no article for a member that is not acquired.
        // This is the acquired filter on an index the builder made, not on one whose rows a test edited.
        var withheld = corpus.VerifiedSet.Set.Members.Single(static value =>
            value.Publisher == PublisherId.LuLegilux &&
            value.Outcome == LexCorpus6OutcomeKind.RightsWithheld);
        Assert.IsTrue(withheld.Stage3Outcomes.Any(static value =>
            value.Domain == LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent &&
            value.Disposition == LexCorpus6Stage3Disposition.AknAdmitted));
        Assert.IsEmpty(reader.ResolveCoverage().ArticleOutcomes);
    }

    [TestMethod]
    public async Task TheArticlesTheIndexHoldsAreExactlyTheAdmittedAndMarkerOnlyOutcomesOfAnAcquiredMember()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        // Three articles the reviewed profile treats three ways: publisher words (admitted), nothing but
        // modification markers (marker-only evidence, which the index holds) and an element outside its
        // vocabulary (not admitted, so not held).
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{manifestation}}"/></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">{{VerifiedLuxembourgSourceProfile.AdmittingLicence}}</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta><body>
                <article id="art_1"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p>Held publisher words.</p></content></article>
                <article id="art_2"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p><mod class="mod-start" for="#pm1"/><mod class="mod-end" for="#pm1"/></p></content></article>
                <article id="art_3"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p><del/></p></content></article>
              </body></act>
            </akomaNtoso>
            """);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        Assert.IsTrue(
            corpus.VerifiedSet.Set.Members.Any(static value =>
                value.Publisher == PublisherId.LuLegilux && value.Outcome == LexCorpus6OutcomeKind.Acquired),
            "no Luxembourg member is acquired: " + string.Join(
                ", ",
                corpus.VerifiedSet.Set.Members.Where(static value => value.Publisher == PublisherId.LuLegilux)
                    .Select(static value => value.Outcome + "/" + value.LuxembourgRights?.Disposition)));
        // The stage's own dispositions, read from its population and not through the index.
        CollectionAssert.AreEquivalent(
            new[]
            {
                LuxembourgAknLegalContentDisposition.Admitted,
                LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence,
                LuxembourgAknLegalContentDisposition.UnsupportedContentShape,
            },
            envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes
                .Select(static value => value.Disposition).ToArray());

        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        // Two articles: the admitted one and the marker-only one. Dropping either from the index's insert gate
        // makes the corpus's count of outcomes say something the articles it holds do not.
        Assert.AreEqual(2, reader.ArticleCount);
        var coverage = reader.ResolveCoverage();
        Assert.AreEqual(2, coverage.Articles);
        CollectionAssert.AreEqual(
            new[] { "akn_admitted=1", "akn_marker_only_evidence=1", "akn_unsupported_content_shape=1" },
            coverage.ArticleOutcomes.Select(static row => row.Key + "=" + row.Value).ToArray());
    }

    [TestMethod]
    public async Task CorpusIncompletenessRefusesBeforeAnIndexExists()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(includeLegalNotice: false);

        var result = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);

        Assert.IsNull(result);
        Assert.AreEqual(LuxembourgIndexBuildRefusal.CorpusRefused, refusal, detail);
    }

    [TestMethod]
    public async Task StrictReaderRejectsByteCorpusAndCapabilitySubstitution()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");

        var changedBytes = built.IndexBytes.ToArray();
        changedBytes[^1] ^= 1;
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, changedBytes, corpus.ArtifactRef, built.CapabilityManifest));

        var otherCorpus = new SourceArtifactRef(corpus.ArtifactRef.ResourceId, new string('a', 64));
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, otherCorpus, built.CapabilityManifest));

        var foreignCorpusIdentity = new SourceArtifactRef(
            "urn:uuid:ffffffff-ffff-5fff-8fff-ffffffffffff", corpus.ArtifactRef.Sha256);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, foreignCorpusIdentity, built.CapabilityManifest));

        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            new string('b', 64),
            [],
            out var wrongManifest,
            out _));
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, wrongManifest!));

        var extendedBytes = AddUnexpectedTable(built.IndexBytes.Span);
        var extendedDigest = Convert.ToHexStringLower(SHA256.HashData(extendedBytes));
        var extendedRef = new SourceArtifactRef(
            LexCorpus6Builder.ResourceIdOf(extendedDigest), extendedDigest);
        var extendedManifest = RebindManifest(built.CapabilityManifest, extendedDigest);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            extendedRef, extendedBytes, corpus.ArtifactRef, extendedManifest));

        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            built.IndexRef.Sha256,
            [new V3IndexCapabilityCell(
                PublisherId.LuLegilux, built.IndexRef.Sha256, "search", "articles",
                "searchable_text", "fra", new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 1), 1)],
            out var inventedManifest,
            out var inventedRefusal), inventedRefusal.ToString());
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, inventedManifest!));

        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE members SET outcome='invented' WHERE rowid=(SELECT min(rowid) FROM members)");
        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE stamp SET sqlite_version='0.0.0' WHERE stamp_id=1");
        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE stamp SET sqlite_source_id='substituted' WHERE stamp_id=1");
    }

    [TestMethod]
    [DataRow("PRAGMA user_version=2", "schema differs from the exact terminal schema")]
    [DataRow("PRAGMA user_version=6", "schema differs from the exact terminal schema")]
    [DataRow("UPDATE states SET state_sha256='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'", "digest is not derived")]
    [DataRow("UPDATE articles SET expression_iri=expression_iri || '/other'", "does not bind its exact article population")]
    [DataRow("UPDATE states SET work_key='wrong-work-key'", "not canonical")]
    [DataRow("UPDATE states SET publisher_legal_resource_iri=expression_iri", "not canonical")]
    [DataRow("UPDATE states SET expression_iri=publisher_legal_resource_iri || '/de'", "does not bind its exact article population")]
    public async Task StrictReaderRejectsEachStateInvariantIndependently(string sql, string expected)
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertTamperedDatabaseRejected(built, corpusRef, sql, expected);
    }

    /// <summary>
    /// Schema 8 is still read exactly as schema 8 checked it: the same tampers of a schema-8 index (the multi-body fixture's
    /// index 9, every byte synthetic, rewritten into schema 8's tables) are refused by schema 8's own checks, with schema 8's
    /// own reasons, and the untampered rewrite opens as schema 8, its held states only and none of index 9's fields.
    /// </summary>
    [TestMethod]
    [DataRow("PRAGMA user_version=2", "schema differs from the exact terminal schema")]
    [DataRow("PRAGMA user_version=9", "schema differs from the exact terminal schema")]
    [DataRow("UPDATE states SET state_sha256='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'" + FirstCodeCivilState, "digest is not derived")]
    [DataRow("UPDATE articles SET expression_iri=expression_iri || '/other'", "does not bind its exact article population")]
    [DataRow("UPDATE states SET work_key='wrong-work-key'" + FirstCodeCivilState, "not canonical")]
    [DataRow("UPDATE states SET publisher_legal_resource_iri=expression_iri" + FirstCodeCivilState, "omits or crosses")]
    [DataRow("UPDATE states SET expression_iri=publisher_legal_resource_iri || '/de'" + FirstCodeCivilState, "does not bind its exact article population")]
    public void ASchema8IndexIsStillReadExactlyAsSchema8CheckedIt(string sql, string expected)
    {
        var built = LuxembourgIndexCatalogueStatesTests.Fixture().Build();
        var corpusRef = LuxembourgIndexCatalogueStatesTests.Harness.CorpusRef;
        var schema8 = Schema8Of(built.IndexBytes.Span);
        var reference = IndexRefOf(schema8);
        using (var reader = LuxembourgIndexReader.OpenAndVerify(reference, schema8, corpusRef, RebindManifest(built.CapabilityManifest, reference.Sha256)))
        {
            Assert.AreEqual(LuxembourgIndexBuilder.LegacySchema8, reader.SchemaIdentity);
            Assert.IsTrue(reader.RecordsBuilds);
            Assert.IsFalse(reader.RecordsCatalogueStates);
            Assert.IsNull(reader.ResolveObservations().Single().DerivedAt, "schema 8 records no derivation time");
            var code = reader.ResolveCatalogueStates("code-civil");
            Assert.HasCount(2, code, "schema 8 holds the held states only");
            Assert.IsTrue(code.All(static row => row.StateKind is null && row.TextStatus == "held"), "and none of index 9's fields");
            Assert.IsEmpty(reader.ResolveIntervals("code-civil"));
            Assert.AreEqual(12L, reader.ResolveCoverage().States);
        }

        var tampered = MutateDatabase(schema8, sql);
        var tamperedRef = IndexRefOf(tampered);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            tamperedRef, tampered, corpusRef, RebindManifest(built.CapabilityManifest, tamperedRef.Sha256)));
        StringAssert.Contains(exception.Message, expected);
    }

    /// <summary>One row of the multi-body fixture's states, so a tamper of a keyed column meets no other row's key.</summary>
    private const string FirstCodeCivilState =
        " WHERE work_key='code-civil' AND applicability_date=(SELECT min(applicability_date) FROM states WHERE work_key='code-civil')";

    internal static SourceArtifactRef IndexRefOf(byte[] index)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(index));
        return new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
    }

    /// <summary>
    /// The event log is a function of the states, and the reader recomputes it: a row whose digest,
    /// number or key differs, an extra row, or a log emptied of its rows is refused even with the stamp
    /// recomputed over the tampered table, so only the recompute can refuse it. (A non-null observation
    /// time and any name but first_sighting are refused by the table's own checks.)
    /// </summary>
    [TestMethod]
    [DataRow("UPDATE events SET detail_json='{\"state_sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}'", "not the log of its states", DisplayName = "a wrong state digest")]
    [DataRow("UPDATE events SET seq=2", "do not number its events", DisplayName = "a renumbered event")]
    [DataRow("UPDATE events SET key='[\"other-work\",\"2024-01-01\",\"https://example.invalid/e\",\"fra\"]'", "not the log of its states", DisplayName = "a rewritten key")]
    [DataRow("INSERT INTO events VALUES(2,'state','[\"extra\"]','first_sighting',NULL,'{}')", "do not number its events", DisplayName = "an extra event")]
    [DataRow("DELETE FROM events", "do not number its events", DisplayName = "an emptied log")]
    public async Task StrictReaderRejectsAnEventLogThatIsNotTheGenesisLogOfItsStates(string sql, string expected)
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection => Execute(connection, sql), expected);
    }

    [TestMethod]
    public async Task StrictReaderRejectsAnArticleClaimedByTwoStates()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            const string secondDate = "2024-02-02";
            RewriteStates(connection, [state, state with { ApplicabilityDate = secondDate, InFileDate = secondDate }]);
        }, "does not bind its exact article population");
    }

    [TestMethod]
    public async Task StrictReaderRejectsAStateWhoseLanguageContradictsItsArticles()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            ReplaceStatesRedigested(connection, [state with { Language = "deu" }]);
        }, "language contradicts its articles");
    }

    [TestMethod]
    public async Task StrictReaderRejectsAStateThatOmitsOneOfItsExpressionArticles()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            var identities = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)!;
            Assert.IsGreaterThan(1, identities.Length);
            ReplaceStatesRedigested(connection, [state with { ArticleIdentitiesJson = System.Text.Json.JsonSerializer.Serialize(new[] { identities[0] }) }]);
        }, "omits or crosses its expression population");
    }

    [TestMethod]
    public async Task StrictReaderRejectsAStateWhoseIdentityListRepeatsAnArticle()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        // The search join reaches an article's state through json_each over this list and counts one
        // row per element, so a repeated identity would serve an article twice and make its cursor
        // ambiguous. The list is sorted here and its digest recomputed, so nothing but the repeat is wrong.
        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            var identities = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)!;
            Assert.IsGreaterThan(1, identities.Length);
            identities = identities.Append(identities[0]).Order(StringComparer.Ordinal).ToArray();
            ReplaceStatesRedigested(connection, [state with { ArticleIdentitiesJson = System.Text.Json.JsonSerializer.Serialize(identities) }]);
        }, "not canonical");
    }

    [TestMethod]
    public async Task StrictReaderRejectsALegalResourceOutsideItsWork()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            const string workIri = "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/n1";
            ReplaceStatesRedigested(connection, [state with { WorkKey = LuxembourgIndexBuilder.WorkKeyOf(workIri), PublisherWorkIri = workIri }]);
        }, "not canonical");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void StateProjectionRejectsAnObjectWhoseArticlesMixExpressionsOrLanguages(
        bool mixExpression)
    {
        const string objectRef = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string root = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo";
        const string expression = root + "/fr";
        var articles = new[]
        {
            new LuxembourgIndexBuilder.ArticleRow(
                new string('b', 64), objectRef, expression, "art_1", null, "2024-02-01",
                "fra", new string('c', 64), "one", "[]"),
            new LuxembourgIndexBuilder.ArticleRow(
                new string('d', 64), objectRef,
                mixExpression ? expression + "/other" : expression,
                "art_2", null, "2024-02-01", mixExpression ? "fra" : "deu",
                new string('c', 64), "two", "[]"),
        };
        var body = new LuxembourgIndexBuilder.CatalogueBody(
            objectRef, root, expression, "fra", "acquired", "agreed_same_run_cc_by", "inventoried", null, new string('c', 64),
            null, "2024-02-01", [], []);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexBuilder.ProjectCatalogueStates(
            articles, LuxembourgIndexBuilder.CatalogueFacts.From([]), [body], [], "2026-10-01"));
        StringAssert.Contains(exception.Message, "does not match its admitted articles");
    }

    // ---- Index schema /7's event log (STATUS item 4, predecessor chaining, the first slice). ----

    /// <summary>
    /// A genesis log as schema /7 holds it: one observation, of this build's corpus, with no predecessor, numbering
    /// every event; the log's own stamp (its schema, and the digest of exactly its observations and events); and each
    /// event naming the bodies its state's articles were read from, as the corpus holds them, so a later build that
    /// carries the log forward can tell a replaced publisher file by the log alone.
    /// </summary>
    [TestMethod]
    public async Task TheGenesisLogHasOneObservationItsOwnStampAndEachStatesSourceBodies()
    {
        var (built, corpus) = await BuildStateIndexWithCorpusAsync();
        LuxembourgIndexBuilder.EventRow[] events = [];
        LuxembourgIndexBuilder.ObservationRow[] observations = [];
        (string Schema, string Digest) logStamp = default;
        _ = MutateDatabase(built.IndexBytes.Span, connection =>
        {
            events = ReadEvents(connection);
            observations = ReadObservations(connection);
            logStamp = ReadLogStamp(connection);
        });

        Assert.IsNotEmpty(events, "the fixture's state has a first_sighting");
        CollectionAssert.AreEqual(
            new[] { new LuxembourgIndexBuilder.ObservationRow(1, corpus.ArtifactRef.Sha256, null, 1, events.Length, BuiltAtText, null) },
            observations,
            "one observation of this corpus, with no predecessor, numbering every event, at its build time and no observation time");
        Assert.AreEqual((LuxembourgIndexBuilder.EventLogSchema, LuxembourgIndexBuilder.HashEventLog(observations, events)), logStamp);

        var held = corpus.Set.Members
            .Where(static member => member.Publisher == Lex.V3.Contracts.PublisherId.LuLegilux && member.BodySha256 is not null)
            .Select(static member => member.BodySha256!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var value in events)
        {
            var bodies = System.Text.Json.JsonDocument.Parse(value.DetailJson).RootElement.GetProperty("source_body_sha256")
                .EnumerateArray().Select(static body => body.GetString()!).ToArray();
            Assert.IsNotEmpty(bodies, $"event {value.Seq} names its state's source bodies");
            CollectionAssert.AreEqual(bodies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), bodies, "sorted, each once");
            Assert.IsTrue(bodies.All(held.Contains), $"event {value.Seq} names a body the corpus holds");
        }

        using var reader = LuxembourgIndexReader.OpenAndVerify(built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        reader.VerifyEventLogSources(corpus);
    }

    /// <summary>
    /// The observations and the log stamp are the reader's own recomputation, with the logical-rows stamp recomputed
    /// over the tampered tables so only these checks can refuse: an observation numbering an event the log lacks, of
    /// another corpus, a second or no observation; a log stamp of other rows, of another log schema, or none.
    /// </summary>
    [TestMethod]
    [DataRow("UPDATE observations SET last_seq=last_seq+1", "do not number its events", DisplayName = "an observation numbering an event the log lacks")]
    [DataRow("UPDATE observations SET corpus_sha256='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'", "is not of this index's corpus", DisplayName = "an observation of another corpus")]
    [DataRow("INSERT INTO observations VALUES(2,'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb','cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',2,1,'2026-10-01T09:00:00Z',NULL,NULL)", "is not of this index's corpus", DisplayName = "a second observation of another corpus")]
    [DataRow("INSERT INTO observations VALUES(2,'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb','cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',2,1,'2026-10-01T07:59:59Z',NULL,NULL)", "each later than the one before", DisplayName = "a second build earlier than the first")]
    [DataRow("INSERT INTO observations VALUES(2,'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb','cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',2,1,'2026-10-01T08:00:00Z',NULL,NULL)", "each later than the one before", DisplayName = "a second build at the first one's time")]
    [DataRow("UPDATE observations SET built_at='2026-13-01T08:00:00Z'", "are not UTC seconds", DisplayName = "a build time that is no date")]
    [DataRow("DELETE FROM observations", "do not number its events", DisplayName = "no observation")]
    [DataRow("UPDATE log_stamp SET log_rows_sha256='0000000000000000000000000000000000000000000000000000000000000000'", "does not match its log stamp", DisplayName = "a log stamp of other rows")]
    [DataRow("UPDATE log_stamp SET log_schema='lex-v3-event-log/0'", "does not match its log stamp", DisplayName = "a log stamp of another log schema")]
    [DataRow("DELETE FROM log_stamp", "does not match its log stamp", DisplayName = "no log stamp")]
    public async Task StrictReaderRejectsATamperedObservationOrLogStamp(string sql, string expected)
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection => Execute(connection, sql), expected);
    }

    /// <summary>
    /// Source bodies the index alone cannot confirm: an event naming a body its state was not read from passes the
    /// reader (the index holds no body digest) and is refused by the corpus; one naming its body twice is not the
    /// canonical genesis log, and the reader refuses it. Both stamps are recomputed, so only these checks can refuse.
    /// </summary>
    [TestMethod]
    public async Task AnEventsSourceBodiesAreTheCorpusOwnAndCanonical()
    {
        var (built, corpus) = await BuildStateIndexWithCorpusAsync();
        var (foreign, foreignManifest, foreignRef) = Resealed(built, detail => detail["source_body_sha256"] = new System.Text.Json.Nodes.JsonArray(new string('e', 64)));
        using (var reader = LuxembourgIndexReader.OpenAndVerify(foreignRef, foreign, corpus.ArtifactRef, foreignManifest))
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() => reader.VerifyEventLogSources(corpus));
            StringAssert.Contains(exception.Message, "names source bodies the corpus does not hold");
        }

        var (twice, twiceManifest, twiceRef) = Resealed(built, detail =>
        {
            var body = detail["source_body_sha256"]![0]!.GetValue<string>();
            detail["source_body_sha256"] = new System.Text.Json.Nodes.JsonArray(body, body);
        });
        var refused = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(twiceRef, twice, corpus.ArtifactRef, twiceManifest));
        StringAssert.Contains(refused.Message, "not the log of its states");
    }

    /// <summary>
    /// The mount checks the event log's source bodies against the corpus it mounts, so an index whose log names a body
    /// the corpus does not hold for the state is not mounted, though the index alone verifies.
    /// </summary>
    [TestMethod]
    public async Task TheMountRefusesAnEventLogNamingABodyItsCorpusDoesNotHoldForTheState()
    {
        var fixture = await V3CorpusResolveMountTests.MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var indexPath = Path.Combine(fixture.Directory, Lex.V3.Api.V3CorpusMount.IndexFileName);
        var bytes = ResealedBytes(await File.ReadAllBytesAsync(indexPath),
            detail => detail["source_body_sha256"] = new System.Text.Json.Nodes.JsonArray(new string('e', 64)), out var articles, out var states, out var titles);
        await File.WriteAllBytesAsync(indexPath, bytes);
        var manifest = LuxembourgIndexBuilder.MeasureCapabilities(Convert.ToHexStringLower(SHA256.HashData(bytes)), articles, states, titles);
        using (var stream = new MemoryStream())
        {
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, Lex.V3.Api.V3CorpusMount.CapabilityManifestFileName), stream.ToArray());
        }

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Lex.V3.Api.V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));
        StringAssert.Contains(exception.Message, "names source bodies the corpus does not hold");
    }

    // ---- Predecessor chaining, the fifth slice: each observation's build time. ----

    /// <summary>
    /// An observation records its build's time, the upper bound <c>as_observed</c> gives: a UTC whole second (the build
    /// rounds up), later than the predecessor's last build, and no earlier than the corpus's EU capture, the one
    /// observation clock the corpus holds; any other time refuses <c>build_time_invalid</c> and writes no index.
    /// </summary>
    [TestMethod]
    public async Task ABuildTimeIsAUtcSecondInTimeOrderAndNoEarlierThanTheEuCapture()
    {
        var (envelope, first, corpus) = await BuildStateEnvelopeAsync();
        Assert.AreEqual(BuiltAtText, ReadLog(first.IndexBytes.ToArray()).Observations.Single().BuiltAt, "the genesis observation records its build's time");

        var captured = DateTimeOffset.Parse(corpus.Set.EuropeRightsMatrix.CapturedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var beforeCapture = new DateTimeOffset(captured.UtcTicks - (captured.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero).AddSeconds(-1);
        var predecessor = LuxembourgIndexPredecessor.TryRead(first.IndexRef, first.IndexBytes.Span, out _, out _);
        foreach (var (what, chainedTo, builtAt, expected) in new (string, LuxembourgIndexPredecessor?, DateTimeOffset, string)[]
                 {
                     ("an offset that is not UTC", null, new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2)), "is not a UTC whole second"),
                     ("a part of a second", null, BuiltAt.AddMilliseconds(1), "is not a UTC whole second"),
                     ("before the corpus's EU capture", null, beforeCapture, "is earlier than the corpus's EU capture"),
                     ("the predecessor's own build time", predecessor, BuiltAt, "is not later than the predecessor's build"),
                 })
        {
            Assert.IsNull(LuxembourgIndexBuilder.TryBuild(envelope, chainedTo, builtAt, out var refusal, out var detail), what);
            Assert.AreEqual(LuxembourgIndexBuildRefusal.BuildTimeInvalid, refusal, what);
            StringAssert.Contains(detail, expected, what);
        }

        var chained = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, Later, out var chainedRefusal, out var chainedDetail);
        Assert.IsNotNull(chained, $"{chainedRefusal}: {chainedDetail}");
        CollectionAssert.AreEqual(new[] { BuiltAtText, LaterText }, ReadLog(chained.IndexBytes.ToArray()).Observations.Select(static o => o.BuiltAt).ToArray(),
            "each observation keeps its own build's time, the predecessor's carried forward unchanged");
        using var reader = LuxembourgIndexReader.OpenAndVerify(chained.IndexRef, chained.IndexBytes.Span, corpus.ArtifactRef, chained.CapabilityManifest);
        CollectionAssert.AreEqual(new[] { BuiltAtText, LaterText }, reader.ResolveObservations().Select(static o => o.BuiltAt).ToArray());
    }

    /// <summary>
    /// The mount's own check of the build time: an index whose last build is stamped before its corpus's EU capture
    /// passes the reader alone (the index holds no capture time) and is refused once it is held to its corpus.
    /// </summary>
    [TestMethod]
    public async Task AMountedIndexBuiltBeforeItsCorpussEuCaptureIsRefusedWithItsCorpus()
    {
        var (built, corpus) = await BuildStateIndexWithCorpusAsync();
        var (early, reference) = Tampered(built.IndexBytes.ToArray(), connection =>
        {
            // Index 9's rows say provisional by the observation bound, so they follow the earlier time, re-sealed.
            Execute(connection, "UPDATE observations SET built_at='2000-01-01T00:00:00Z'");
            Execute(connection, "UPDATE states SET provisional=(applicability_date>'2000-01-01')");
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LogicalRowsOf(connection)));
        });
        using var reader = LuxembourgIndexReader.OpenAndVerify(reference, early, corpus.ArtifactRef, RebindManifest(built.CapabilityManifest, reference.Sha256));
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => reader.VerifyEventLogSources(corpus));
        StringAssert.Contains(exception.Message, "last build time 2000-01-01T00:00:00Z is earlier than the corpus's EU capture");
    }

    // ---- Predecessor chaining, the second slice: the predecessor as a build input. ----

    /// <summary>
    /// G3a: a build chained to a predecessor carries the predecessor's observations and events forward unchanged, an
    /// exact prefix, and appends one observation of its own naming the predecessor's digest; the same states append no
    /// event (silence). Two builds with the same predecessor are the same bytes, and a third chains onto the second.
    /// </summary>
    [TestMethod]
    public async Task AChainedBuildCarriesThePredecessorsLogAsAnExactPrefix()
    {
        var (envelope, first, corpus) = await BuildStateEnvelopeAsync();
        var predecessor = LuxembourgIndexPredecessor.TryRead(first.IndexRef, first.IndexBytes.Span, out var readRefusal, out var readDetail);
        Assert.IsNotNull(predecessor, $"{readRefusal}: {readDetail}");
        Assert.AreEqual(first.IndexRef.Sha256, predecessor.IndexSha256);

        var second = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, Later, out var refusal, out var detail);
        Assert.IsNotNull(second, $"{refusal}: {detail}");
        var again = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, Later, out refusal, out detail);
        Assert.IsNotNull(again, $"{refusal}: {detail}");
        CollectionAssert.AreEqual(second.IndexBytes.ToArray(), again.IndexBytes.ToArray(), "two builds with one predecessor are one index");

        var (firstObservations, firstEvents) = ReadLog(first.IndexBytes.ToArray());
        var (secondObservations, secondEvents) = ReadLog(second.IndexBytes.ToArray());
        CollectionAssert.AreEqual(firstEvents, secondEvents, "the predecessor's events, carried forward; the same states append none");
        CollectionAssert.AreEqual(firstObservations, secondObservations.Take(firstObservations.Length).ToArray(), "the predecessor's observations, an exact prefix");
        Assert.AreEqual(
            new LuxembourgIndexBuilder.ObservationRow(2, corpus.ArtifactRef.Sha256, first.IndexRef.Sha256, firstEvents.Length + 1, firstEvents.Length, LaterText, null),
            secondObservations[^1],
            "one observation of this build, naming the predecessor, appending no event");
        using (var reader = LuxembourgIndexReader.OpenAndVerify(second.IndexRef, second.IndexBytes.Span, corpus.ArtifactRef, second.CapabilityManifest))
        {
            reader.VerifyEventLogSources(corpus);
        }

        var third = LuxembourgIndexBuilder.TryBuild(
            envelope, LuxembourgIndexPredecessor.TryRead(second.IndexRef, second.IndexBytes.Span, out _, out _), Latest, out refusal, out detail);
        Assert.IsNotNull(third, $"{refusal}: {detail}");
        var (thirdObservations, thirdEvents) = ReadLog(third.IndexBytes.ToArray());
        CollectionAssert.AreEqual(secondObservations, thirdObservations.Take(secondObservations.Length).ToArray(), "the chain grows by one observation a build");
        Assert.AreEqual(second.IndexRef.Sha256, thirdObservations[^1].PredecessorIndexSha256);
        CollectionAssert.AreEqual(firstEvents, thirdEvents);
    }

    /// <summary>
    /// A state the predecessor's log does not hold is appended: a first_sighting, or expression_added when the log holds
    /// the same work and date in another language; one the log holds unchanged appends nothing; one it holds with
    /// other source bodies is a replaced file, and one it holds with another digest from the same bodies refuses.
    /// </summary>
    [TestMethod]
    public void NewStatesAppendFirstSightingsAndANewLanguageOfAHeldWorkAndDateIsExpressionAdded()
    {
        static LuxembourgIndexBuilder.StateRow State(string work, string date, string expression, string language, string digest) =>
            new(work, date, digest, expression, "w", "r", language, "[]", "[]");
        var held = State("loi-a", "2024-01-01", "https://example.invalid/a/fr", "fra", new string('1', 64));
        var fold = new Dictionary<string, LuxembourgIndexBuilder.LoggedState>(StringComparer.Ordinal)
        {
            [LuxembourgIndexBuilder.StateKey(held)] = new("loi-a", "2024-01-01", "https://example.invalid/a/fr", "fra", held.StateSha256, [new string('b', 64)]),
        };
        var german = State("loi-a", "2024-01-01", "https://example.invalid/a/de", "deu", new string('2', 64));
        var other = State("loi-b", "2024-01-01", "https://example.invalid/b/fr", "fra", new string('3', 64));
        var bodies = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [held.StateSha256] = [new string('b', 64)],
            [german.StateSha256] = [new string('c', 64)],
            [other.StateSha256] = [new string('d', 64)],
        };

        var appended = LuxembourgIndexBuilder.ProjectChainedEvents(fold, [other, held, german], bodies, 7, out var changed, Log2);
        Assert.IsNull(changed);
        CollectionAssert.AreEqual(
            new[]
            {
                new LuxembourgIndexBuilder.EventRow(7, "state", LuxembourgIndexBuilder.StateKey(german), "expression_added", null,
                    $"{{\"state_sha256\":\"{german.StateSha256}\",\"source_body_sha256\":[\"{new string('c', 64)}\"]}}"),
                new LuxembourgIndexBuilder.EventRow(8, "state", LuxembourgIndexBuilder.StateKey(other), "first_sighting", null,
                    $"{{\"state_sha256\":\"{other.StateSha256}\",\"source_body_sha256\":[\"{new string('d', 64)}\"]}}"),
            },
            appended,
            "in the states' key order, numbered on from the log, the held state silent");

        var redigested = held with { StateSha256 = new string('9', 64) };
        bodies[redigested.StateSha256] = [new string('b', 64)];
        Assert.IsNull(LuxembourgIndexBuilder.ProjectChainedEvents(fold, [redigested], bodies, 7, out changed, Log2), "a held key with another digest");
        StringAssert.Contains(changed, "/lu-legilux/loi-a/2024-01-01");
        bodies[held.StateSha256] = [new string('e', 64)];
        Assert.AreEqual("file_replaced", LuxembourgIndexBuilder.ProjectChainedEvents(fold, [held], bodies, 7, out changed, Log2)!.Single().Event,
            "a held key with other source bodies is a replaced file");
    }

    /// <summary>
    /// A predecessor is read by its own digest and log: other bytes than its build names, no log stamp (index schema 6
    /// and before), another log schema, a log that does not match its stamp, or a log its observations do not number,
    /// are each refused with their reason.
    /// </summary>
    [TestMethod]
    public async Task APredecessorIsReadByItsOwnDigestAndItsOwnLog()
    {
        var (_, first, _) = await BuildStateEnvelopeAsync();
        var bytes = first.IndexBytes.ToArray();
        Assert.IsNull(LuxembourgIndexPredecessor.TryRead(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(new string('0', 64)), new string('0', 64)), bytes, out var refusal, out _));
        Assert.AreEqual(LuxembourgIndexBuildRefusal.PredecessorMismatch, refusal, "other bytes than the build names");

        foreach (var (tamper, restamp, expected, reason) in new (string Sql, bool Restamp, LuxembourgIndexBuildRefusal Refusal, string Reason)[]
                 {
                     ("DROP TABLE log_stamp", false, LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers, "no event log stamp"),
                     ("UPDATE log_stamp SET log_schema='lex-v3-event-log/0'", false, LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers, "lex-v3-event-log/0"),
                     ("UPDATE log_stamp SET log_rows_sha256='" + new string('0', 64) + "'", false, LuxembourgIndexBuildRefusal.PredecessorMismatch, "does not match its log stamp"),
                     ("UPDATE observations SET last_seq=last_seq+1", true, LuxembourgIndexBuildRefusal.PredecessorMismatch, "do not number its events"),
                     // The review of #866's reproduction: a genesis observation's event renamed, the log stamp recomputed.
                     ("UPDATE events SET event='expression_added' WHERE seq=1", true, LuxembourgIndexBuildRefusal.PredecessorMismatch, "holds events that are not the ones it appends"),
                 })
        {
            var (tampered, reference) = Tampered(bytes, tamper, restamp);
            Assert.IsNull(LuxembourgIndexPredecessor.TryRead(reference, tampered, out refusal, out var detail), tamper);
            Assert.AreEqual(expected, refusal, tamper);
            StringAssert.Contains(detail, reason, tamper);
        }
    }

    /// <summary>
    /// The review of #866: every observation of a carried log is replayed, not only the last, so a chained index whose
    /// predecessor part holds an event no build could have written (a genesis event renamed expression_added) is refused
    /// by the reader, with both stamps recomputed over the change.
    /// </summary>
    [TestMethod]
    public async Task AChainedIndexWhoseCarriedLogHoldsAFalseEventIsRefused()
    {
        var (envelope, first, corpus) = await BuildStateEnvelopeAsync();
        var second = LuxembourgIndexBuilder.TryBuild(
            envelope, LuxembourgIndexPredecessor.TryRead(first.IndexRef, first.IndexBytes.Span, out _, out _), Later, out var refusal, out var detail);
        Assert.IsNotNull(second, $"{refusal}: {detail}");
        var renamed = MutateDatabase(second.IndexBytes.Span, connection =>
        {
            Execute(connection, "UPDATE events SET event='expression_added' WHERE seq=1");
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LogicalRowsOf(connection)));
            Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                ("$digest", LogStampOf(connection)));
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(renamed));
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest), renamed, corpus.ArtifactRef, RebindManifest(second.CapabilityManifest, digest)));
        StringAssert.Contains(exception.Message, "not the log of its states");
        StringAssert.Contains(exception.InnerException!.Message, "observation 1 holds events that are not the ones it appends");
    }

    /// <summary>
    /// A predecessor whose log holds this build's state with another digest is refused (comparison events are not
    /// served yet); one whose log does not hold the state yet gets a first_sighting appended, which the reader then
    /// holds to exactly what the observation must append.
    /// </summary>
    [TestMethod]
    public async Task AChainedBuildAppendsANewStateAndRefusesAChangedOne()
    {
        var (envelope, first, corpus) = await BuildStateEnvelopeAsync();
        var bytes = first.IndexBytes.ToArray();
        var (changedLog, changedRef) = Tampered(bytes, connection =>
        {
            var held = ReadEvents(connection)[0];
            var detail = System.Text.Json.Nodes.JsonNode.Parse(held.DetailJson)!;
            detail["state_sha256"] = new string('9', 64);
            Execute(connection, "UPDATE events SET detail_json=$detail WHERE seq=1", ("$detail", detail.ToJsonString()));
        });
        var changed = LuxembourgIndexPredecessor.TryRead(changedRef, changedLog, out var refusal, out var readDetail);
        Assert.IsNotNull(changed, $"{refusal}: {readDetail}");
        Assert.IsNull(LuxembourgIndexBuilder.TryBuild(envelope, changed, Later, out refusal, out var detail));
        Assert.AreEqual(LuxembourgIndexBuildRefusal.PredecessorDerivationDiffers, refusal);
        StringAssert.Contains(detail, "this build's derivation differs from the predecessor's");

        var (emptyLog, emptyRef) = Tampered(bytes, connection =>
        {
            Execute(connection, "DELETE FROM events");
            Execute(connection, "UPDATE observations SET last_seq=0");
        });
        var empty = LuxembourgIndexPredecessor.TryRead(emptyRef, emptyLog, out refusal, out readDetail);
        Assert.IsNotNull(empty, $"{refusal}: {readDetail}");
        var chained = LuxembourgIndexBuilder.TryBuild(envelope, empty, Later, out refusal, out detail);
        Assert.IsNotNull(chained, $"{refusal}: {detail}");
        var (observations, events) = ReadLog(chained.IndexBytes.ToArray());
        Assert.AreEqual(2, observations.Length);
        Assert.AreEqual(new LuxembourgIndexBuilder.ObservationRow(2, corpus.ArtifactRef.Sha256, emptyRef.Sha256, 1, events.Length, LaterText, null), observations[1]);
        Assert.IsTrue(events.All(static value => value.Event == "first_sighting"), "each state, new to the log, first sighted");

        // The appended events are the reader's own recomputation: one renamed, both stamps recomputed, is refused.
        var renamed = MutateDatabase(chained.IndexBytes.Span, connection =>
        {
            Execute(connection, "UPDATE events SET event='expression_added' WHERE seq=1");
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LogicalRowsOf(connection)));
            Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                ("$digest", LogStampOf(connection)));
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(renamed));
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest), renamed, corpus.ArtifactRef, RebindManifest(chained.CapabilityManifest, digest)));
        StringAssert.Contains(exception.Message, "not the log of its states");
    }

    // ---- Predecessor chaining, the third slice: comparison events. ----

    /// <summary>Log /2, index 8's: the projection these unit tests pin, which reading an index 8 still runs.</summary>
    private const string Log2 = LuxembourgIndexBuilder.LegacyEventLogSchema8;

    private static LuxembourgIndexBuilder.StateRow ChainState(string work, string date, string language, string digest) =>
        new(work, date, digest, $"https://example.invalid/{work}/{date}/{language}", "w", "r", language, "[]", "[]");

    private static Dictionary<string, LuxembourgIndexBuilder.LoggedState> ChainFold(params (LuxembourgIndexBuilder.StateRow Row, string Body)[] held) =>
        held.ToDictionary(
            static pair => LuxembourgIndexBuilder.StateKey(pair.Row),
            static pair => new LuxembourgIndexBuilder.LoggedState(pair.Row.WorkKey, pair.Row.ApplicabilityDate, pair.Row.ExpressionIri, pair.Row.Language, pair.Row.StateSha256, [pair.Body]),
            StringComparer.Ordinal);

    /// <summary>
    /// G1 at the projection: a held state whose source bodies differ is file_replaced, naming the state and bodies it
    /// replaces, whether its digest changed with its text or stayed with its text; the same bodies with another digest
    /// are this derivation's change, not the publisher's, and refuse.
    /// </summary>
    [TestMethod]
    public void AHeldStateWhoseSourceBodiesDifferIsFileReplacedAndAnotherDigestFromTheSameBodiesRefuses()
    {
        var held = ChainState("loi-a", "2020-01-01", "fra", new string('1', 64));
        var fold = ChainFold((held, new string('b', 64)));
        var reworded = held with { StateSha256 = new string('2', 64) };
        var appended = LuxembourgIndexBuilder.ProjectChainedEvents(fold, [reworded],
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [reworded.StateSha256] = [new string('c', 64)] }, 5, out var changed, Log2);
        Assert.IsNull(changed);
        CollectionAssert.AreEqual(
            new[]
            {
                new LuxembourgIndexBuilder.EventRow(5, "state", LuxembourgIndexBuilder.StateKey(held), "file_replaced", null,
                    $"{{\"state_sha256\":\"{reworded.StateSha256}\",\"source_body_sha256\":[\"{new string('c', 64)}\"],"
                    + $"\"replaced_state_sha256\":\"{held.StateSha256}\",\"replaced_source_body_sha256\":[\"{new string('b', 64)}\"]}}"),
            },
            appended,
            "a new text: a new state digest, the state and bodies it replaces named");

        var reissued = LuxembourgIndexBuilder.ProjectChainedEvents(fold, [held],
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [held.StateSha256] = [new string('c', 64)] }, 5, out changed, Log2)!;
        Assert.AreEqual("file_replaced", reissued.Single().Event, "new bytes with the same text: file_replaced, the digest unchanged");
        StringAssert.Contains(reissued.Single().DetailJson, $"\"replaced_state_sha256\":\"{held.StateSha256}\"");

        Assert.IsNull(LuxembourgIndexBuilder.ProjectChainedEvents(fold, [reworded],
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [reworded.StateSha256] = [new string('b', 64)] }, 5, out changed, Log2));
        StringAssert.Contains(changed, "/lu-legilux/loi-a/2020-01-01");
    }

    /// <summary>
    /// The derived intervals: the latest state the log held, followed by a later one, is interval_closed; a state whose
    /// end moved because a state was inserted before its next is validity_revised; a held state this build lacks stays
    /// held, and its interval closes too; each says it is derived and names its old and new end exactly.
    /// </summary>
    [TestMethod]
    public void AHeldStatesIntervalClosesOrIsRevisedByTheStatesThisBuildAdds()
    {
        string Body(char c) => new(c, 64);
        var first = ChainState("loi-a", "2020-01-01", "fra", Body('1'));
        var last = ChainState("loi-a", "2024-01-01", "fra", Body('2'));
        var later = ChainState("loi-a", "2026-01-01", "fra", Body('3'));
        var between = ChainState("loi-a", "2022-01-01", "fra", Body('4'));
        var bodies = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [first.StateSha256] = [Body('a')], [last.StateSha256] = [Body('b')], [later.StateSha256] = [Body('c')], [between.StateSha256] = [Body('d')],
        };
        static string Interval(LuxembourgIndexBuilder.StateRow row, string body, string? previous, string next) =>
            $"{{\"state_sha256\":\"{row.StateSha256}\",\"source_body_sha256\":[\"{body}\"],\"applicable_from\":\"{row.ApplicabilityDate}\","
            + $"\"previous_to\":{(previous is null ? "null" : $"\"{previous}\"")},\"new_to\":\"{next}\",\"derived\":true}}";
        var fold = ChainFold((first, Body('a')), (last, Body('b')));

        var closed = LuxembourgIndexBuilder.ProjectChainedEvents(fold, [first, last, later], bodies, 3, out _, Log2);
        CollectionAssert.AreEqual(
            new[]
            {
                new LuxembourgIndexBuilder.EventRow(3, "state", LuxembourgIndexBuilder.StateKey(last), "interval_closed", null, Interval(last, Body('b'), null, "2026-01-01")),
                new LuxembourgIndexBuilder.EventRow(4, "state", LuxembourgIndexBuilder.StateKey(later), "first_sighting", null,
                    $"{{\"state_sha256\":\"{later.StateSha256}\",\"source_body_sha256\":[\"{Body('c')}\"]}}"),
            },
            closed,
            "the latest held state closes at the later one's date; the first state's end is unchanged");

        var revised = LuxembourgIndexBuilder.ProjectChainedEvents(fold, [first, between, last], bodies, 3, out _, Log2);
        CollectionAssert.AreEqual(
            new[]
            {
                new LuxembourgIndexBuilder.EventRow(3, "state", LuxembourgIndexBuilder.StateKey(first), "validity_revised", null, Interval(first, Body('a'), "2024-01-01", "2022-01-01")),
                new LuxembourgIndexBuilder.EventRow(4, "state", LuxembourgIndexBuilder.StateKey(between), "first_sighting", null,
                    $"{{\"state_sha256\":\"{between.StateSha256}\",\"source_body_sha256\":[\"{Body('d')}\"]}}"),
            },
            revised,
            "a state inserted before the next moves the end back; the latest state stays open");

        var absent = LuxembourgIndexBuilder.ProjectChainedEvents(fold, [later], bodies, 3, out _, Log2)!;
        CollectionAssert.AreEqual(
            new[] { "interval_closed", "first_sighting" },
            absent.Select(static value => value.Event).ToArray(),
            "the held states this build lacks stay held: the latest closes at the new one's date");
        StringAssert.Contains(absent[0].DetailJson, $"\"state_sha256\":\"{last.StateSha256}\"", "named by the digest the log holds");
    }

    /// <summary>
    /// G1 through the real pipeline: the same act built again from the publisher's file with one article reworded is
    /// file_replaced, and the state's digest, and so its permalink, is new; built from the same file with one byte added
    /// and its text unchanged, it is file_replaced with the digest, and so the version, unchanged.
    /// </summary>
    [TestMethod]
    public async Task AReplacedPublisherFileIsFileReplacedAndItsNewTextIsANewVersion()
    {
        var (_, first, _) = await BuildStateEnvelopeAsync();
        var predecessor = LuxembourgIndexPredecessor.TryRead(first.IndexRef, first.IndexBytes.Span, out var readRefusal, out var readDetail);
        Assert.IsNotNull(predecessor, $"{readRefusal}: {readDetail}");
        var (_, firstEvents) = ReadLog(first.IndexBytes.ToArray());
        var firstState = System.Text.Json.JsonDocument.Parse(firstEvents[0].DetailJson).RootElement.GetProperty("state_sha256").GetString();

        foreach (var (what, transform, newVersion) in new (string What, Func<string, string> Transform, bool NewVersion)[]
                 {
                     ("one article reworded", static xml => ReplaceFirst(xml, "assemblée générale", "assemblée plénière"), true),
                     ("one byte added, the text unchanged", static xml => xml + "\n", false),
                 })
        {
            var (envelope, _, _) = await BuildStateEnvelopeAsync(transform);
            var chained = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, Later, out var refusal, out var detail);
            Assert.IsNotNull(chained, $"{what}: {refusal}: {detail}");
            var (observations, events) = ReadLog(chained.IndexBytes.ToArray());
            var replaced = events.Skip((int)observations[^1].FirstSeq - 1).Where(static value => value.Event == "file_replaced").ToArray();
            Assert.HasCount(1, replaced, what);
            var detailJson = System.Text.Json.JsonDocument.Parse(replaced[0].DetailJson).RootElement;
            Assert.AreEqual(firstState, detailJson.GetProperty("replaced_state_sha256").GetString(), what);
            Assert.AreEqual(newVersion, !string.Equals(firstState, detailJson.GetProperty("state_sha256").GetString(), StringComparison.Ordinal), what);
            CollectionAssert.AreNotEqual(
                detailJson.GetProperty("replaced_source_body_sha256").EnumerateArray().Select(static body => body.GetString()).ToArray(),
                detailJson.GetProperty("source_body_sha256").EnumerateArray().Select(static body => body.GetString()).ToArray(),
                $"{what}: the bodies differ");
        }
    }

    internal static string ReplaceFirst(string text, string old, string replacement)
    {
        var at = text.IndexOf(old, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, at, old);
        return text[..at] + replacement + text[(at + old.Length)..];
    }

    /// <summary>The state index's envelope and its first build, with the corpus, for the chained builds.</summary>
    internal static async Task<(Stage3DerivationProfileEnvelope Envelope, LuxembourgIndexBuildResult Built, VerifiedLexCorpus6ManifestSet Corpus)> BuildStateEnvelopeAsync(
        Func<string, string>? transform = null)
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var xml = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        if (transform is not null)
        {
            xml = Encoding.UTF8.GetBytes(transform(Encoding.UTF8.GetString(xml)));
        }

        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        return (envelope, built, corpus.VerifiedSet);
    }

    private static (LuxembourgIndexBuilder.ObservationRow[] Observations, LuxembourgIndexBuilder.EventRow[] Events) ReadLog(byte[] bytes)
    {
        LuxembourgIndexBuilder.ObservationRow[] observations = [];
        LuxembourgIndexBuilder.EventRow[] events = [];
        _ = MutateDatabase(bytes, connection =>
        {
            observations = ReadObservations(connection);
            events = ReadEvents(connection);
        });
        return (observations, events);
    }

    /// <summary>An index changed by <paramref name="tamper"/>, its log stamp recomputed over the change when asked, with its new digest.</summary>
    private static (byte[] Bytes, SourceArtifactRef Reference) Tampered(byte[] source, string tamper, bool restampLog) =>
        Tampered(source, connection => Execute(connection, tamper), restampLog);

    internal static (byte[] Bytes, SourceArtifactRef Reference) Tampered(byte[] source, Action<SqliteConnection> tamper, bool restampLog = true)
    {
        var bytes = MutateDatabase(source, connection =>
        {
            tamper(connection);
            if (restampLog)
            {
                Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                    ("$digest", LogStampOf(connection)));
            }
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest));
    }

    /// <summary>The state index built with its corpus, for the checks that need the corpus's own body digests.</summary>
    internal static async Task<(LuxembourgIndexBuildResult Built, VerifiedLexCorpus6ManifestSet Corpus)> BuildStateIndexWithCorpusAsync()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var xml = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        return (built, corpus.VerifiedSet);
    }

    /// <summary>The index with its first event's detail changed, and both stamps (logical rows, log) recomputed over it.</summary>
    private static (byte[] Bytes, V3IndexCapabilityManifest Manifest, SourceArtifactRef Reference) Resealed(
        LuxembourgIndexBuildResult built,
        Action<System.Text.Json.Nodes.JsonNode> change)
    {
        var bytes = ResealedBytes(built.IndexBytes.ToArray(), change, out _, out _, out _);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, RebindManifest(built.CapabilityManifest, digest), new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest));
    }

    internal static byte[] ResealedBytes(
        byte[] source,
        Action<System.Text.Json.Nodes.JsonNode> change,
        out LuxembourgIndexBuilder.ArticleRow[] articles,
        out LuxembourgIndexBuilder.StateRow[] states,
        out LuxembourgIndexBuilder.WorkTitleRow[] titles)
    {
        LuxembourgIndexBuilder.ArticleRow[] readArticles = [];
        LuxembourgIndexBuilder.StateRow[] readStates = [];
        LuxembourgIndexBuilder.WorkTitleRow[] readTitles = [];
        var bytes = MutateDatabase(source, connection =>
        {
            var first = ReadEvents(connection)[0];
            var detail = System.Text.Json.Nodes.JsonNode.Parse(first.DetailJson)!;
            change(detail);
            Execute(connection, "UPDATE events SET detail_json=$detail WHERE seq=$seq", ("$detail", detail.ToJsonString()), ("$seq", first.Seq));
            readArticles = ReadArticles(connection);
            readStates = ReadStates(connection);
            readTitles = ReadWorkTitles(connection);
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LogicalRowsOf(connection)));
            Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                ("$digest", LogStampOf(connection)));
        });
        articles = readArticles;
        states = readStates;
        titles = readTitles;
        return bytes;
    }

    internal static LuxembourgIndexBuilder.ObservationRow[] ReadObservations(SqliteConnection connection) =>
        LuxembourgIndexBuilder.ReadLogObservations(connection, LogSchemaOf(connection));

    /// <summary>The index's schema version (<c>user_version</c>): 9, or a legacy 8 or 6 a test rewrote it into.</summary>
    internal static int IndexVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>The log schema of the index's version: log /3 for 9, log /2 otherwise.</summary>
    internal static string LogSchemaOf(SqliteConnection connection) =>
        IndexVersion(connection) == 9 ? LuxembourgIndexBuilder.EventLogSchema : LuxembourgIndexBuilder.LegacyEventLogSchema8;

    /// <summary>The digest of the index's log as it now stands, as its log stamp records it for its schema.</summary>
    internal static string LogStampOf(SqliteConnection connection) =>
        LuxembourgIndexBuilder.HashEventLogOf(LogSchemaOf(connection), ReadObservations(connection), ReadEvents(connection));

    /// <summary>The digest of the index's logical rows as they now stand, as its stamp records it for its schema.</summary>
    internal static string LogicalRowsOf(SqliteConnection connection) =>
        IndexVersion(connection) == 9
            ? LuxembourgIndexBuilder.HashLogicalRows(
                ReadMembers(connection), ReadArticles(connection), ReadStates(connection), ReadWorkTitles(connection),
                ReadRelations(connection), ReadWorkFacts(connection), ReadEvents(connection), ReadIntervals(connection))
            : LuxembourgIndexBuilder.HashLegacyLogicalRows8(
                ReadMembers(connection), ReadArticles(connection), ReadStates(connection), ReadWorkTitles(connection),
                ReadRelations(connection), ReadWorkFacts(connection), ReadEvents(connection));

    /// <summary>The index's interval transitions (none before index 9).</summary>
    internal static LuxembourgIndexBuilder.IntervalRow[] ReadIntervals(SqliteConnection connection) =>
        IndexVersion(connection) == 9 ? LuxembourgIndexReader.ReadIntervals(connection) : [];

    /// <summary>
    /// Rewrites the states table as <paramref name="rows"/>, each completed as its schema's builder writes it: under index
    /// 9 a row's own facts the builder would give it where it carries none (kind from its legal resource, its date's role,
    /// an act's in-file date, the French expression a translation takes, <c>provisional</c> by the observation bound, held
    /// text), its digest /2, and the chain fields and interval transitions recomputed over every row; under index 8 or 6,
    /// digest /1. Returns the rows as written. The caller re-seals the log and the stamp.
    /// </summary>
    internal static LuxembourgIndexBuilder.StateRow[] RewriteStates(SqliteConnection connection, IEnumerable<LuxembourgIndexBuilder.StateRow> rows)
    {
        var source = rows.ToArray();
        var version = IndexVersion(connection);
        LuxembourgIndexBuilder.StateRow[] written;
        LuxembourgIndexBuilder.IntervalRow[] intervals = [];
        if (version == 9)
        {
            var builtAtDate = ReadObservations(connection)[^1].BuiltAt[..10];
            var completed = source.Select(row =>
            {
                var kind = row.StateKind ?? LuxembourgIndexBuilder.StateKindOf(row.PublisherLegalResourceIri)!;
                var role = row.DateRole ?? (kind == LuxembourgIndexBuilder.KindAsPublished
                    ? LuxembourgIndexBuilder.RoleApplicabilityInFile
                    : LuxembourgIndexBuilder.RoleApplicability);
                var translation = LuxembourgIndexBuilder.IsTranslationLanguage(row.Language);
                var french = source.Where(other =>
                        string.Equals(other.PublisherLegalResourceIri, row.PublisherLegalResourceIri, StringComparison.Ordinal) &&
                        string.Equals(other.Language, "fra", StringComparison.Ordinal))
                    .Select(static other => other.ExpressionIri).ToArray();
                return row with
                {
                    StateKind = kind,
                    DateRole = role,
                    InFileDate = !translation && role == LuxembourgIndexBuilder.RoleApplicabilityInFile ? row.ApplicabilityDate : row.InFileDate,
                    TextStatus = row.TextStatus ?? LuxembourgIndexBuilder.TextHeld,
                    // A translation keeps the French expression it names while that is still one of its legal resource's
                    // French rows, so a row added later does not move its digest; else the one French row, when there is one.
                    TranslationOf = !translation ? null
                        : row.TranslationOf is { } named && french.Contains(named, StringComparer.Ordinal) ? named
                        : french.Length == 1 ? french[0] : null,
                    Provisional = string.CompareOrdinal(row.ApplicabilityDate, builtAtDate) > 0,
                    Boundary = row.Boundary ?? LuxembourgIndexBuilder.BoundaryDerived,
                };
            }).ToArray();
            completed = completed.Select(static row => row with { StateSha256 = LuxembourgIndexBuilder.StateSha256Two(row) }).ToArray();
            (written, intervals) = LuxembourgIndexBuilder.CompleteIntervals(completed);
        }
        else
        {
            written = source.Select(static row => row with
            {
                StateSha256 = LuxembourgIndexBuilder.StateSha256(
                    row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.PublisherWorkIri, row.PublisherLegalResourceIri, row.Language,
                    System.Text.Json.JsonSerializer.Deserialize<string[]>(row.RuleProfilesJson)!,
                    System.Text.Json.JsonSerializer.Deserialize<string[]>(row.ArticleIdentitiesJson)!),
            }).ToArray();
        }

        Execute(connection, "DELETE FROM states");
        if (version == 9)
        {
            Execute(connection, "DELETE FROM intervals");
        }

        foreach (var row in written)
        {
            InsertStateRow(connection, version, row);
        }

        foreach (var interval in intervals)
        {
            Execute(connection, "INSERT INTO intervals VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6)",
                ("$p0", interval.WorkKey), ("$p1", interval.Ordinal), ("$p2", interval.Kind), ("$p3", interval.FromStateSha256),
                ("$p4", interval.ToStateSha256), ("$p5", (object?)interval.SpanFrom ?? DBNull.Value), ("$p6", (object?)interval.SpanTo ?? DBNull.Value));
        }

        return ReadStates(connection);
    }

    /// <summary>
    /// Rewrites the states table as <paramref name="rows"/> exactly as given but for each digest, recomputed under the
    /// index's schema, so a tamper test leaves one fact wrong and the digest agreeing with it. The intervals are untouched.
    /// </summary>
    internal static void ReplaceStatesRedigested(SqliteConnection connection, IEnumerable<LuxembourgIndexBuilder.StateRow> rows)
    {
        var version = IndexVersion(connection);
        var redigested = rows.Select(row => row with
        {
            StateSha256 = version == 9
                ? LuxembourgIndexBuilder.StateSha256Two(row)
                : LuxembourgIndexBuilder.StateSha256(
                    row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.PublisherWorkIri, row.PublisherLegalResourceIri, row.Language,
                    System.Text.Json.JsonSerializer.Deserialize<string[]>(row.RuleProfilesJson)!,
                    System.Text.Json.JsonSerializer.Deserialize<string[]>(row.ArticleIdentitiesJson)!),
        }).ToArray();
        Execute(connection, "DELETE FROM states");
        foreach (var row in redigested)
        {
            InsertStateRow(connection, version, row);
        }
    }

    internal static void InsertStateRow(SqliteConnection connection, int version, LuxembourgIndexBuilder.StateRow row)
    {
        if (version == 9)
        {
            Execute(connection,
                "INSERT INTO states VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,$p15,$p16,$p17,$p18,$p19,$p20,$p21)",
                ("$p0", row.WorkKey), ("$p1", row.ApplicabilityDate), ("$p2", row.StateSha256), ("$p3", row.ExpressionIri),
                ("$p4", row.PublisherWorkIri), ("$p5", row.PublisherLegalResourceIri), ("$p6", row.Language), ("$p7", row.RuleProfilesJson),
                ("$p8", row.ArticleIdentitiesJson), ("$p9", row.StateKind!), ("$p10", row.DateRole!), ("$p11", (object?)row.ApplicableTo ?? DBNull.Value),
                ("$p12", (object?)row.ApplicableToSource ?? DBNull.Value), ("$p13", row.Boundary!), ("$p14", (object?)row.PublicationDate ?? DBNull.Value),
                ("$p15", row.TextStatus!), ("$p16", (object?)row.TextStatusReason ?? DBNull.Value), ("$p17", (object?)row.InFileIdentity ?? DBNull.Value),
                ("$p18", (object?)row.InFileDate ?? DBNull.Value), ("$p19", (object?)row.TranslationOf ?? DBNull.Value), ("$p20", row.Provisional ? 1 : 0),
                ("$p21", (object?)row.EntryIntoForce ?? DBNull.Value));
        }
        else
        {
            Execute(connection,
                "INSERT INTO states VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8)",
                ("$p0", row.WorkKey), ("$p1", row.ApplicabilityDate), ("$p2", row.StateSha256), ("$p3", row.ExpressionIri),
                ("$p4", row.PublisherWorkIri), ("$p5", row.PublisherLegalResourceIri), ("$p6", row.Language), ("$p7", row.RuleProfilesJson),
                ("$p8", row.ArticleIdentitiesJson));
        }
    }

    /// <summary>
    /// A built index 9 rewritten into schema 8's tables, as schema 8 wrote them: its held rows only, with schema 8's nine
    /// columns and state digest /1; no interval table; log /2, a genesis log of those rows with the source bodies the log
    /// named and an observation with no derivation time; user_version 8 and the stamps re-sealed. Every other table is
    /// copied unchanged. No schema 8 builder remains, so this is how a test holds an index of that schema.
    /// </summary>
    internal static byte[] Schema8Of(ReadOnlySpan<byte> schema9, Action<SqliteConnection>? tamper = null)
    {
        LuxembourgIndexBuilder.MemberRow[] members = [];
        LuxembourgIndexBuilder.ArticleRow[] articles = [];
        LuxembourgIndexBuilder.StateRow[] states = [];
        LuxembourgIndexBuilder.WorkTitleRow[] titles = [];
        LuxembourgIndexBuilder.RelationRow[] relations = [];
        LuxembourgIndexBuilder.WorkFactRow[] facts = [];
        IReadOnlyDictionary<string, LuxembourgIndexBuilder.LoggedState> fold = new Dictionary<string, LuxembourgIndexBuilder.LoggedState>();
        LuxembourgIndexBuilder.ObservationRow observation = null!;
        string corpusSha256 = "";
        (string Version, string SourceId, string Compile) provenance = ("", "", "");
        _ = MutateDatabase(schema9, connection =>
        {
            members = ReadMembers(connection);
            articles = ReadArticles(connection);
            states = ReadStates(connection);
            titles = ReadWorkTitles(connection);
            relations = ReadRelations(connection);
            facts = ReadWorkFacts(connection);
            fold = LuxembourgIndexBuilder.FoldLog(ReadEvents(connection));
            observation = ReadObservations(connection)[^1];
            using var stamp = connection.CreateCommand();
            stamp.CommandText = "SELECT corpus_sha256,sqlite_version,sqlite_source_id,compile_options_sha256 FROM stamp WHERE stamp_id=1";
            using var reader = stamp.ExecuteReader();
            Assert.IsTrue(reader.Read());
            corpusSha256 = reader.GetString(0);
            provenance = (reader.GetString(1), reader.GetString(2), reader.GetString(3));
        });

        var held = states.Where(static row => row.Held).Select(static row => new LuxembourgIndexBuilder.StateRow(
            row.WorkKey, row.ApplicabilityDate,
            LuxembourgIndexBuilder.StateSha256(
                row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.PublisherWorkIri, row.PublisherLegalResourceIri, row.Language,
                System.Text.Json.JsonSerializer.Deserialize<string[]>(row.RuleProfilesJson)!,
                System.Text.Json.JsonSerializer.Deserialize<string[]>(row.ArticleIdentitiesJson)!),
            row.ExpressionIri, row.PublisherWorkIri, row.PublisherLegalResourceIri, row.Language, row.RuleProfilesJson,
            row.ArticleIdentitiesJson)).ToArray();
        var bodies = held.ToDictionary(
            static row => row.StateSha256,
            row => fold[LuxembourgIndexBuilder.StateKey(row)].SourceBodies.ToArray(),
            StringComparer.Ordinal);
        var events = LuxembourgIndexBuilder.ProjectGenesisEvents(held, bodies, LuxembourgIndexBuilder.LegacyEventLogSchema8);
        var observations = LuxembourgIndexBuilder.ProjectGenesisObservations(corpusSha256, events.Length, observation.BuiltAt);
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-schema8-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = path,
                       Mode = SqliteOpenMode.ReadWriteCreate,
                       Pooling = false,
                   }.ToString()))
            {
                connection.Open();
                Execute(connection, "PRAGMA page_size=4096");
                Execute(connection, "PRAGMA encoding='UTF-8'");
                Execute(connection, "PRAGMA auto_vacuum=NONE");
                Execute(connection, "PRAGMA journal_mode=DELETE");
                Execute(connection, "PRAGMA application_id=1280717574");
                Execute(connection, "PRAGMA user_version=8");
                Execute(connection, LuxembourgIndexBuilder.LegacyDdl8);
                foreach (var member in members)
                {
                    Execute(connection, "INSERT INTO members VALUES($p0,$p1,$p2,$p3,$p4,$p5)",
                        ("$p0", member.ObjectRefSha256), ("$p1", member.SourceOrdinal), ("$p2", member.Outcome),
                        ("$p3", (object?)member.RightsDisposition ?? DBNull.Value), ("$p4", member.Stage3OutcomesJson), ("$p5", member.GapsJson));
                }

                foreach (var article in articles)
                {
                    Execute(connection, "INSERT INTO articles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9)",
                        ("$p0", article.ArticleIdentitySha256), ("$p1", article.ObjectRefSha256), ("$p2", article.ExpressionIri),
                        ("$p3", article.PublisherId), ("$p4", (object?)article.PublisherWid ?? DBNull.Value),
                        ("$p5", (object?)article.ApplicabilityDate ?? DBNull.Value), ("$p6", article.Language),
                        ("$p7", article.RuleProfileSha256), ("$p8", article.SearchableText), ("$p9", article.TokensJson));
                }

                foreach (var relation in relations)
                {
                    Execute(connection, "INSERT INTO relations VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9)",
                        ("$p0", relation.FromRef), ("$p1", relation.Ordinal), ("$p2", relation.EdgeType), ("$p3", relation.AssertedBy),
                        ("$p4", relation.SourcePredicate), ("$p5", relation.InNote ? 1 : 0), ("$p6", (object?)relation.Label ?? DBNull.Value),
                        ("$p7", (object?)relation.Href ?? DBNull.Value), ("$p8", relation.ToKind), ("$p9", (object?)relation.ToRef ?? DBNull.Value));
                }

                foreach (var row in held)
                {
                    Execute(connection, "INSERT INTO states VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8)",
                        ("$p0", row.WorkKey), ("$p1", row.ApplicabilityDate), ("$p2", row.StateSha256), ("$p3", row.ExpressionIri),
                        ("$p4", row.PublisherWorkIri), ("$p5", row.PublisherLegalResourceIri), ("$p6", row.Language), ("$p7", row.RuleProfilesJson),
                        ("$p8", row.ArticleIdentitiesJson));
                }

                foreach (var title in titles)
                {
                    Execute(connection, "INSERT INTO work_titles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                        ("$p0", title.WorkIdentifier), ("$p1", title.ExpressionIri), ("$p2", title.Language), ("$p3", title.Title),
                        ("$p4", title.NormalizedTitle), ("$p5", (object?)title.DocumentDate ?? DBNull.Value), ("$p6", title.TitleKind),
                        ("$p7", title.EvidenceSha256));
                }

                foreach (var fact in facts)
                {
                    Execute(connection, "INSERT INTO work_facts VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                        ("$p0", fact.SubjectIri), ("$p1", fact.Predicate), ("$p2", fact.FactKind), ("$p3", fact.ObjectKind),
                        ("$p4", fact.ObjectValue), ("$p5", fact.DatatypeIri), ("$p6", fact.LanguageTag), ("$p7", fact.EvidenceSha256));
                }

                foreach (var value in events)
                {
                    Execute(connection, "INSERT INTO events VALUES($p0,$p1,$p2,$p3,NULL,$p4)",
                        ("$p0", value.Seq), ("$p1", value.Scope), ("$p2", value.Key), ("$p3", value.Event), ("$p4", value.DetailJson));
                }

                foreach (var row in observations)
                {
                    Execute(connection, "INSERT INTO observations VALUES($p0,$p1,NULL,$p2,$p3,$p4,NULL)",
                        ("$p0", row.Observation), ("$p1", row.CorpusSha256), ("$p2", row.FirstSeq), ("$p3", row.LastSeq), ("$p4", row.BuiltAt));
                }

                Execute(connection, "INSERT INTO log_stamp VALUES(1,$p0,$p1)",
                    ("$p0", LuxembourgIndexBuilder.LegacyEventLogSchema8), ("$p1", LuxembourgIndexBuilder.HashLegacyEventLog8(observations, events)));
                Execute(connection, "INSERT INTO stamp VALUES(1,$p0,$p1,$p2,$p3,$p4,$p5)",
                    ("$p0", LuxembourgIndexBuilder.LegacySchema8), ("$p1", corpusSha256),
                    ("$p2", LuxembourgIndexBuilder.HashLegacyLogicalRows8(members, articles, held, titles, relations, facts, events)),
                    ("$p3", provenance.Version), ("$p4", provenance.SourceId), ("$p5", provenance.Compile));
                if (tamper is not null)
                {
                    tamper(connection);
                }
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            LuxembourgIndexBuilder.DeleteDatabase(path);
        }
    }

    private static (string Schema, string Digest) ReadLogStamp(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT log_schema,log_rows_sha256 FROM log_stamp WHERE log_stamp_id=1";
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read(), "the log stamp row");
        return (reader.GetString(0), reader.GetString(1));
    }

    internal static async Task<(LuxembourgIndexBuildResult Built, SourceArtifactRef CorpusRef)>
        BuildStateIndexAsync()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var xml = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, BuiltAt, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        return (built, corpus.ArtifactRef);
    }

    internal static V3IndexCapabilityManifest RebindManifest(
        V3IndexCapabilityManifest source,
        string indexSha256)
    {
        var cells = source.Cells.Select(cell => new V3IndexCapabilityCell(
            cell.Publisher, indexSha256, cell.Operation, cell.Column, cell.Field,
            cell.Language, cell.PeriodFrom, cell.PeriodTo, cell.Population));
        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            source.Publisher, indexSha256, cells, out var rebound, out var refusal),
            refusal.ToString());
        return rebound!;
    }

    private static byte[] AddUnexpectedTable(ReadOnlySpan<byte> source)
        => MutateDatabase(source, "CREATE TABLE invented(value TEXT) STRICT");

    private static void AssertTamperedDatabaseRejected(
        LuxembourgIndexBuildResult built,
        SourceArtifactRef corpusRef,
        string sql,
        string? expectedMessage = null)
    {
        var bytes = MutateDatabase(built.IndexBytes.Span, sql);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var reference = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var manifest = RebindManifest(built.CapabilityManifest, digest);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            reference, bytes, corpusRef, manifest));
        if (expectedMessage is not null) StringAssert.Contains(exception.Message, expectedMessage);
    }

    private static void AssertRecomputedStateTamperRejected(
        LuxembourgIndexBuildResult built,
        SourceArtifactRef corpusRef,
        Action<SqliteConnection> tamper,
        string expectedMessage)
    {
        var bytes = MutateDatabase(built.IndexBytes.Span, connection =>
        {
            tamper(connection);
            var logicalRows = LogicalRowsOf(connection);
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1",
                ("$digest", logicalRows));
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var reference = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var manifest = RebindManifest(built.CapabilityManifest, digest);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            reference, bytes, corpusRef, manifest));
        StringAssert.Contains(exception.Message, expectedMessage);
    }

    private static byte[] MutateDatabase(ReadOnlySpan<byte> source, string sql)
        => MutateDatabase(source, connection => Execute(connection, sql));

    internal static byte[] MutateDatabase(
        ReadOnlySpan<byte> source,
        Action<SqliteConnection> mutate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-hostile-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.WriteAllBytes(path, source.ToArray());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            connection.Open();
            mutate(connection);
            connection.Close();
            return File.ReadAllBytes(path);
        }
        finally
        {
            LuxembourgIndexBuilder.DeleteDatabase(path);
        }
    }

    internal static void Execute(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    internal static LuxembourgIndexBuilder.MemberRow[] ReadMembers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,rights_disposition,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.MemberRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return rows.ToArray();
    }

    /// <summary>Every article row in identity order, with its document position on an index 9.</summary>
    internal static LuxembourgIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
    {
        var nine = IndexVersion(connection) == 9;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,expression_iri,publisher_id,publisher_wid,applicability_date,language,rule_profile_sha256,searchable_text,tokens_json" +
            (nine ? ",document_position" : "") + " FROM articles ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.ArticleRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
            nine ? reader.GetInt32(10) : null));
        return rows.ToArray();
    }

    /// <summary>
    /// The next free place in a document, for an article a test adds to an index 9 by copying another body's: one past
    /// every place the table holds, so no body's articles share one.
    /// </summary>
    internal static long NextDocumentPosition(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(document_position),0)+1 FROM articles";
        return (long)command.ExecuteScalar()!;
    }

    internal static LuxembourgIndexBuilder.StateRow[] ReadStates(SqliteConnection connection) =>
        LuxembourgIndexReader.ReadStates(connection, IndexVersion(connection));

    internal static LuxembourgIndexBuilder.WorkTitleRow[] ReadWorkTitles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_identifier,expression_iri,language,title,normalized_title,document_date,title_kind,evidence_sha256 FROM work_titles ORDER BY work_identifier,expression_iri,language,title,title_kind,evidence_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.WorkTitleRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
            reader.GetString(7)));
        return rows.ToArray();
    }
    internal static LuxembourgIndexBuilder.EventRow[] ReadEvents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT seq,scope,key,event,observed_from,detail_json FROM events ORDER BY seq";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.EventRow>();
        while (reader.Read()) values.Add(new(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
        return values.ToArray();
    }

    /// <summary>
    /// Rewrites the event log as the genesis log of <paramref name="states"/>, for a fixture that added or
    /// changed states legitimately (the log is a function of the states, as the reader checks).
    /// </summary>
    internal static LuxembourgIndexBuilder.EventRow[] RefreshGenesisEvents(
        SqliteConnection connection,
        IReadOnlyList<LuxembourgIndexBuilder.ArticleRow> articles,
        IReadOnlyList<LuxembourgIndexBuilder.StateRow> states,
        IReadOnlyDictionary<string, string> bodyByObjectRef)
    {
        // The genesis log of the edited states, re-sealed whole: its events (each naming its state's source bodies),
        // its one observation (at the build time it held) and its own stamp, as the builder writes them.
        var logSchema = LogSchemaOf(connection);
        var first = ReadObservations(connection)[0];
        var builtAt = first.BuiltAt;

        foreach (var table in new[] { "events", "observations", "log_stamp" })
        {
            using var clear = connection.CreateCommand();
            clear.CommandText = $"DELETE FROM {table}";
            clear.ExecuteNonQuery();
        }

        var events = LuxembourgIndexBuilder.ProjectGenesisEvents(
            states, LuxembourgIndexBuilder.SourceBodiesOfStates(articles, states, bodyByObjectRef), logSchema);
        foreach (var value in events)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO events VALUES($seq,$scope,$key,$event,$observed,$detail)";
            insert.Parameters.AddWithValue("$seq", value.Seq);
            insert.Parameters.AddWithValue("$scope", value.Scope);
            insert.Parameters.AddWithValue("$key", value.Key);
            insert.Parameters.AddWithValue("$event", value.Event);
            insert.Parameters.AddWithValue("$observed", (object?)value.ObservedFrom ?? DBNull.Value);
            insert.Parameters.AddWithValue("$detail", value.DetailJson);
            Assert.AreEqual(1, insert.ExecuteNonQuery());
        }

        string corpusSha256;
        using (var stamp = connection.CreateCommand())
        {
            stamp.CommandText = "SELECT corpus_sha256 FROM stamp WHERE stamp_id=1";
            corpusSha256 = (string)stamp.ExecuteScalar()!;
        }

        var observations = LuxembourgIndexBuilder.ProjectGenesisObservations(corpusSha256, events.Length, builtAt, first.DerivedAt);
        foreach (var observation in observations)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = logSchema == LuxembourgIndexBuilder.EventLogSchema
                ? "INSERT INTO observations VALUES($observation,$corpus,NULL,$first,$last,$builtAt,NULL,$derivedAt)"
                : "INSERT INTO observations VALUES($observation,$corpus,NULL,$first,$last,$builtAt,NULL)";
            insert.Parameters.AddWithValue("$derivedAt", (object?)observation.DerivedAt ?? DBNull.Value);
            insert.Parameters.AddWithValue("$builtAt", observation.BuiltAt);
            insert.Parameters.AddWithValue("$observation", observation.Observation);
            insert.Parameters.AddWithValue("$corpus", observation.CorpusSha256);
            insert.Parameters.AddWithValue("$first", observation.FirstSeq);
            insert.Parameters.AddWithValue("$last", observation.LastSeq);
            Assert.AreEqual(1, insert.ExecuteNonQuery());
        }

        using (var logStamp = connection.CreateCommand())
        {
            logStamp.CommandText = "INSERT INTO log_stamp VALUES(1,$schema,$digest)";
            logStamp.Parameters.AddWithValue("$schema", logSchema);
            logStamp.Parameters.AddWithValue("$digest", LuxembourgIndexBuilder.HashEventLogOf(logSchema, observations, events));
            Assert.AreEqual(1, logStamp.ExecuteNonQuery());
        }

        return events;
    }

    /// <summary>The body digest of every Luxembourg member of a mount's corpus file, by object reference.</summary>
    internal static IReadOnlyDictionary<string, string> CorpusBodies(string directory) =>
        LuxembourgIndexBuilder.BodiesByObjectRef(VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(
            File.ReadAllBytes(Path.Combine(directory, Lex.V3.Api.V3CorpusMount.CorpusFileName))));

    internal static LuxembourgIndexBuilder.WorkFactRow[] ReadWorkFacts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts ORDER BY subject_iri,predicate,object_kind,object_value,datatype_iri,language_tag,evidence_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.WorkFactRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return rows.ToArray();
    }

    internal static LuxembourgIndexBuilder.RelationRow[] ReadRelations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT from_ref,ordinal,edge_type,asserted_by,source_predicate,in_note,label,href,to_kind,to_ref FROM relations ORDER BY from_ref,ordinal";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.RelationRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt32(5) == 1,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return rows.ToArray();
    }
}
