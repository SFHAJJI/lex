using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The index's edge table (lane R4's, <c>relations</c>): the forward edges are the references the publisher wrote
/// in each article, one row each, read from the article's stored token stream by a fixed grammar that resolves
/// nothing. The rows are held against the retained publisher XML by a count of <c>ref</c> elements that does not
/// go through the profile or the grammar, and the reader refuses an index whose table is anything else.
/// </summary>
[TestClass]
public sealed class LuxembourgIndexRelationsTests
{
    private const string Root = "http://data.legilux.public.lu";

    [TestMethod]
    [DataRow("/eli/etat/leg/loi/2004/07/09/n3/jo", "legilux_eli", Root + "/eli/etat/leg/loi/2004/07/09/n3/jo")]
    [DataRow(Root + "/eli/etat/leg/loi/2018/09/23/a872/jo", "legilux_eli", Root + "/eli/etat/leg/loi/2018/09/23/a872/jo")]
    [DataRow("/eli/etat/leg/agd/1911/08/14/n1/jo", "legilux_eli", Root + "/eli/etat/leg/agd/1911/08/14/n1/jo")]
    [DataRow("/eli/dir_ue/1995/46/jo", "legilux_eli", Root + "/eli/dir_ue/1995/46/jo")]
    [DataRow("/eli/reg_ue/2016/679/jo", "legilux_eli", Root + "/eli/reg_ue/2016/679/jo")]
    [DataRow("/eli/etat/leg/code/penal", "legilux_eli", Root + "/eli/etat/leg/code/penal")]
    [DataRow("/eli/etat/leg/code/penal/", "legilux_eli", Root + "/eli/etat/leg/code/penal/")]
    [DataRow("/eli/etat/leg/loi/2004/07/09/n3/jo#art_5", "legilux_eli", Root + "/eli/etat/leg/loi/2004/07/09/n3/jo#art_5")]
    [DataRow("http://data.europa.eu/eli/agree_internation/2021/689(1)/oj", "other_uri", "http://data.europa.eu/eli/agree_internation/2021/689(1)/oj")]
    [DataRow("https://data.legilux.public.lu/eli/etat/leg/loi/2004/07/09/n3/jo", "other_uri", "https://data.legilux.public.lu/eli/etat/leg/loi/2004/07/09/n3/jo")]
    [DataRow("HTTP://DATA.LEGILUX.PUBLIC.LU/eli/etat/leg/loi/2004/07/09/n3/jo", "other_uri", "HTTP://DATA.LEGILUX.PUBLIC.LU/eli/etat/leg/loi/2004/07/09/n3/jo")]
    [DataRow(Root + "/eli/", "other_uri", Root + "/eli/")]
    [DataRow("???", "unparsed", null)]
    [DataRow("", "unparsed", null)]
    [DataRow(null, "unparsed", null)]
    [DataRow(" ", "unparsed", null)]
    [DataRow("/eli/", "unparsed", null)]
    [DataRow("eli/etat/leg/loi/2004/07/09/n3/jo", "unparsed", null)]
    [DataRow(" /eli/etat/leg/loi/2004/07/09/n3/jo", "unparsed", null)]
    [DataRow("mailto:someone@example.org", "unparsed", null)]
    [DataRow("http://exa mple.org/x", "unparsed", null)]
    public void EachShapeTheRealActsUseIsReadByTheFixedGrammarAndNothingIsResolved(
        string? href, string expectedKind, string? expectedTarget)
    {
        var (kind, target) = LuxembourgIndexBuilder.ClassifyTarget(href);

        Assert.AreEqual(expectedKind, kind);
        Assert.AreEqual(expectedTarget, target);
    }

    [TestMethod]
    [DataRow(LuxembourgAknLegalContentProfileProducerTests.Fixture1991, "3a6bb598a9310f8a31240c1f33ae357d6e1f7a46392ca33d223c2718cdded95c", "loi-1991-08-10-n3")]
    [DataRow(LuxembourgAknLegalContentProfileProducerTests.Fixture1984, "5d513304238bbda30578f59f963b227d54ca1fbce9283c55b9c5aa7b4436e48f", "loi-1984-02-24-n1")]
    public async Task EveryReferenceOfAnAdmittedArticleIsOneRowInDocumentOrderAgainstACountOfTheXml(
        string fixture, string sha256, string key)
    {
        var bytes = await LuxembourgAknLegalContentProfileProducerTests.RetainedFixtureAsync(fixture, sha256);
        var population = await LuxembourgAknLegalContentProfileProducerTests.RunAsync(bytes, key);
        var document = XDocument.Load(new MemoryStream(bytes));
        var admitted = population.Outcomes.Where(static outcome => outcome.Article is not null).ToArray();
        Assert.IsNotEmpty(admitted);

        var total = 0;
        foreach (var outcome in admitted)
        {
            var article = outcome.Article!;
            var publisherId = article.Coordinate.PublisherId;
            var element = document.Descendants()
                .Single(candidate => candidate.Name.LocalName == "article" &&
                                     (string?)candidate.Attribute("id") == publisherId);
            var written = WrittenReferences(document, element);

            var rows = LuxembourgIndexBuilder.ProjectRelations([Row(article)]);

            var seen = rows.Select(static row => $"{(row.InNote ? "note" : "text")} {row.Href}").ToArray();
            CollectionAssert.AreEqual(
                written, seen,
                $"{publisherId}: the XML has [{string.Join(" | ", written)}] and the rows [{string.Join(" | ", seen)}]");
            CollectionAssert.AreEqual(
                Enumerable.Range(0, rows.Length).ToArray(), rows.Select(static row => row.Ordinal).ToArray(), publisherId);
            Assert.IsTrue(rows.All(row => string.Equals(row.FromRef, article.IdentitySha256, StringComparison.Ordinal)));
            total += rows.Length;
        }

        Assert.IsGreaterThan(0, total, "the act carries references, so the comparison above is not empty");
    }

    [TestMethod]
    public async Task AQuestionMarkPlaceholderTheRealActWritesIsUnparsedAndNeverNamesAWork()
    {
        var bytes = await LuxembourgAknLegalContentProfileProducerTests.RetainedFixtureAsync(
            LuxembourgAknLegalContentProfileProducerTests.Fixture1984,
            "5d513304238bbda30578f59f963b227d54ca1fbce9283c55b9c5aa7b4436e48f");
        var population = await LuxembourgAknLegalContentProfileProducerTests.RunAsync(bytes, "loi-1984-02-24-n1");

        var placeholders = population.Outcomes
            .Where(static outcome => outcome.Article is not null)
            .SelectMany(static outcome => LuxembourgIndexBuilder.ProjectRelations([Row(outcome.Article!)]))
            .Where(static row => string.Equals(row.Href, "???", StringComparison.Ordinal))
            .ToArray();

        Assert.IsNotEmpty(placeholders, "the retained 1984 act writes a ??? reference in an article the profile admits");
        foreach (var row in placeholders)
        {
            Assert.AreEqual("unparsed", row.ToKind);
            Assert.IsNull(row.ToRef);
            Assert.AreEqual("???", row.Href);
        }
    }

    [TestMethod]
    public void AReferenceInAFootnoteBodyIsFlaggedAndKeepsItsPlaceInTheArticlesOrder()
    {
        var article = Row(
        [
            new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Text, "avant", null, null),
            new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Reference, "premier", "/eli/etat/leg/loi/2000/01/01/n1/jo", null),
            new LuxembourgAknLegalContentToken(
                LuxembourgAknLegalContentTokenKind.NoteReference, null, null, "1",
                [
                    new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Text, "voir", null, null),
                    new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Reference, "deuxième", "???", null),
                    new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Reference, "troisième", "http://data.europa.eu/eli/x", null),
                ]),
            new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Reference, "quatrième", "/eli/dir_ue/2003/8/jo", null),
        ]);

        var rows = LuxembourgIndexBuilder.ProjectRelations([article]);

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, rows.Select(static row => row.Ordinal).ToArray());
        CollectionAssert.AreEqual(
            new[] { "premier", "deuxième", "troisième", "quatrième" }, rows.Select(static row => row.Label).ToArray());
        CollectionAssert.AreEqual(new[] { false, true, true, false }, rows.Select(static row => row.InNote).ToArray());
        CollectionAssert.AreEqual(
            new[] { "legilux_eli", "unparsed", "other_uri", "legilux_eli" }, rows.Select(static row => row.ToKind).ToArray());
        Assert.IsTrue(rows.All(static row => row is { EdgeType: "cites", AssertedBy: "publisher_text", SourcePredicate: "akn_ref" }));
    }

    [TestMethod]
    public void AMarkWithNoValueIsAnEdgeWithNoHrefAndNoTargetAndTextAloneIsNoEdge()
    {
        var article = Row(
        [
            new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Text, "https://example.org/not-a-mark", null, null),
            new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Reference, "sans valeur", null, null),
            new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.ModificationStart, null, null, "pm1"),
        ]);

        var row = LuxembourgIndexBuilder.ProjectRelations([article]).Single();

        Assert.AreEqual("sans valeur", row.Label);
        Assert.IsNull(row.Href);
        Assert.AreEqual("unparsed", row.ToKind);
        Assert.IsNull(row.ToRef);
    }

    [TestMethod]
    public async Task TheReaderServesTheRealActsEdgesByStateAndAnchorInTheirStatedOrder()
    {
        var (built, corpusRef) = await LuxembourgIndexBuilderTests.BuildStateIndexAsync();
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpusRef, built.CapabilityManifest);
        var state = reader.ResolveState("loi-1991-08-10-n3", "2024-02-01").Single();
        var articles = reader.ResolveStateArticles(state.StateSha256);

        var edges = reader.ResolveStateCitations(state.StateSha256, null);

        Assert.IsNotEmpty(edges);
        var expected = edges
            .OrderBy(static edge => edge.PublisherId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.ArticleIdentitySha256, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(expected, edges.ToArray(), "publisher id, then identity, then occurrence order");
        Assert.IsTrue(edges.All(edge => articles.Any(article =>
            string.Equals(article.ArticleIdentitySha256, edge.ArticleIdentitySha256, StringComparison.Ordinal))));
        foreach (var group in edges.GroupBy(static edge => edge.ArticleIdentitySha256, StringComparer.Ordinal))
        {
            CollectionAssert.AreEqual(
                Enumerable.Range(0, group.Count()).ToArray(), group.Select(static edge => edge.Ordinal).ToArray());
        }

        var withEdges = edges.First().PublisherId;
        var anchored = reader.ResolveStateCitations(state.StateSha256, withEdges);
        CollectionAssert.AreEqual(
            edges.Where(edge => string.Equals(edge.PublisherId, withEdges, StringComparison.Ordinal)).ToArray(),
            anchored.ToArray());
        var without = articles.Select(static article => article.PublisherId).Distinct(StringComparer.Ordinal)
            .First(id => edges.All(edge => !string.Equals(edge.PublisherId, id, StringComparison.Ordinal)));
        Assert.IsEmpty(reader.ResolveStateCitations(state.StateSha256, without));
        Assert.IsEmpty(reader.ResolveStateCitations(state.StateSha256, "art_that_the_state_does_not_hold"));
        Assert.IsEmpty(reader.ResolveStateCitations(new string('0', 64), null));
    }

    [TestMethod]
    public async Task TheStrictReaderRefusesAnIndexWhoseRelationsAreNotTheReferencesItsArticlesCarry()
    {
        var (built, corpusRef) = await LuxembourgIndexBuilderTests.BuildStateIndexAsync();

        AssertRejected(built, corpusRef, "removes an edge the publisher wrote", connection =>
            LuxembourgIndexBuilderTests.Execute(connection,
                "DELETE FROM relations WHERE rowid=(SELECT min(rowid) FROM relations)"));
        AssertRejected(built, corpusRef, "moves an edge's target to another work", connection =>
            LuxembourgIndexBuilderTests.Execute(connection,
                "UPDATE relations SET to_ref='http://data.legilux.public.lu/eli/etat/leg/loi/1900/01/01/n1/jo' " +
                "WHERE rowid=(SELECT min(rowid) FROM relations WHERE to_kind='legilux_eli')"));
        AssertRejected(built, corpusRef, "turns an unparsed value into a target", connection =>
            LuxembourgIndexBuilderTests.Execute(connection,
                "UPDATE relations SET to_kind='other_uri',to_ref='http://example.org/' " +
                "WHERE rowid=(SELECT min(rowid) FROM relations WHERE to_kind='legilux_eli')"));
        AssertRejected(built, corpusRef, "invents an edge in an article that has none", connection =>
        {
            var articles = LuxembourgIndexBuilderTests.ReadArticles(connection);
            var existing = LuxembourgIndexBuilderTests.ReadRelations(connection)
                .Select(static row => row.FromRef).ToHashSet(StringComparer.Ordinal);
            var bare = articles.First(article => !existing.Contains(article.ArticleIdentitySha256));
            LuxembourgIndexBuilderTests.Execute(connection,
                "INSERT INTO relations VALUES($from,0,'cites','publisher_text','akn_ref',0,'invented','/eli/etat/leg/loi/1900/01/01/n1/jo','legilux_eli','http://data.legilux.public.lu/eli/etat/leg/loi/1900/01/01/n1/jo')",
                ("$from", bare.ArticleIdentitySha256));
        });
        AssertRejected(built, corpusRef, "flips the flag that says an edge is in a footnote", connection =>
            LuxembourgIndexBuilderTests.Execute(connection,
                "UPDATE relations SET in_note=1-in_note WHERE rowid=(SELECT min(rowid) FROM relations)"));
    }

    /// <summary>
    /// What the publisher wrote, counted from the XML alone: each <c>ref</c> inside the article in document order, and
    /// at each <c>noteRef</c> the <c>ref</c>s inside the <c>note</c> it points at (the publisher keeps a note's body
    /// outside the article, and the profile brings it in at the note reference). Nothing here goes through the
    /// profile's tokens or the target grammar.
    /// </summary>
    private static string[] WrittenReferences(XDocument document, XElement article)
    {
        var written = new List<string>();
        foreach (var element in article.Descendants())
        {
            if (element.Name.LocalName == "ref" && element.Attribute("href") is { } href)
            {
                written.Add("text " + href.Value);
            }
            else if (element.Name.LocalName == "noteRef")
            {
                var id = ((string?)element.Attribute("href"))?.TrimStart('#');
                var note = document.Descendants().Single(candidate =>
                    candidate.Name.LocalName == "note" && (string?)candidate.Attribute("id") == id);
                written.AddRange(note.Descendants()
                    .Where(static candidate => candidate.Name.LocalName == "ref" && candidate.Attribute("href") is not null)
                    .Select(static candidate => "note " + (string)candidate.Attribute("href")!));
            }
        }

        return written.ToArray();
    }

    private static void AssertRejected(
        LuxembourgIndexBuildResult built,
        SourceArtifactRef corpusRef,
        string what,
        Action<SqliteConnection> tamper)
    {
        // The stamp is recomputed over the tampered table, so the digest agrees with the rows and only the
        // rule that the rows are the articles' own references can refuse the index.
        var bytes = LuxembourgIndexBuilderTests.MutateDatabase(built.IndexBytes.Span, connection =>
        {
            tamper(connection);
            var logicalRows = LuxembourgIndexBuilder.HashLogicalRows(
                LuxembourgIndexBuilderTests.ReadMembers(connection),
                LuxembourgIndexBuilderTests.ReadArticles(connection),
                LuxembourgIndexBuilderTests.ReadStates(connection),
                LuxembourgIndexBuilderTests.ReadWorkTitles(connection),
                LuxembourgIndexBuilderTests.ReadRelations(connection));
            LuxembourgIndexBuilderTests.Execute(connection,
                "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", logicalRows));
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var reference = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var manifest = LuxembourgIndexBuilderTests.RebindManifest(built.CapabilityManifest, digest);

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => LuxembourgIndexReader.OpenAndVerify(reference, bytes, corpusRef, manifest), what);

        StringAssert.Contains(exception.Message, "relation rows are not the references its articles carry", what);
    }

    private static LuxembourgIndexBuilder.ArticleRow Row(LuxembourgAknLegalContentArticle article) =>
        Row(article.Tokens, article.IdentitySha256, article.Coordinate.PublisherId);

    private static LuxembourgIndexBuilder.ArticleRow Row(
        IReadOnlyList<LuxembourgAknLegalContentToken> tokens,
        string? identity = null,
        string publisherId = "art_1") =>
        new(
            identity ?? new string('a', 64), new string('1', 64), "https://example.invalid/expression", publisherId,
            null, null, "fra", new string('4', 64), string.Empty, LuxembourgIndexBuilder.TokensJson(tokens));
}
