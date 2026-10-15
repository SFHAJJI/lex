using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Content /2: content /1's rules over inventory /4's articles, with one change. An article's
/// publisher index terms are skipped as <c>scl:JOLUXWork</c> is and kept as article metadata, never
/// as wording; every other shape stays the typed gap /1 makes of it.
/// </summary>
/// <remarks>
/// <para>
/// REAL BYTES ONLY FROM AN ADMITTED SOURCE. <c>Fixtures/LuAknLegalContent2/code-procedure-penale-20251219-index-terms.bin</c>
/// is a byte-exact excerpt of <c>code/procedure_penale/20251219</c> (run-15 body
/// <c>bddb7a57f527ad2bec5c4197e1f805780c0120f14a1c89c1218dd47a4fc7cbd2</c>, 2,242,711 bytes, fetched as
/// <c>xml</c>), which run 15 acquired under the agreed CC BY reading (<c>agreed_same_run_cc_by</c>). It
/// is cut as S2 cut its excerpts: the source bytes with 16 whole element ranges removed, each with the
/// white space before it, so every byte kept is the publisher's, in order. It keeps three main-body
/// articles: <c>art_56</c>, whose index terms nest four levels deep; <c>art_102</c>, which carries
/// index terms and a <c>span</c>; and <c>art_283</c>, which carries none. Every other shape here is
/// synthetic, with invented wording such as "Texte synthétique".
/// </para>
/// <para>
/// HOW /1 IS USED HERE. Content /1 is run over the same inventory /4 population only as a measuring
/// device, to show what /1 makes of the same articles. Production never pairs them: the checkpoint
/// runs /1 over /3, and R1 (S4) will run /2 over /4.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgAknLegalContentProfile2ProducerTests
{
    /// <summary>
    /// Profile /2: its own text, which names inventory /4's digest. Recomputed independently from the
    /// text below (Python, <c>hashlib.sha256</c>).
    /// </summary>
    internal const string Content2RuleSha256 = "bf3fea5a5229d1a9f035d8d56816bd054abee30591370dfc6fe62f154dfaef2d";

    private const string Eli = LuxembourgHeldPopulationBuilder.Eli;
    private const string Inventory4RuleSha256 = "8a011d874aed08b161d90c900d54e9ab26dd9f925ed37323e544e44b2056c63f";
    private const string ExcerptDirectory = "LuAknLegalContent2";
    private const string CodeExcerpt = "code-procedure-penale-20251219-index-terms.bin";
    private const string CodeExcerptSha256 = "9448fcc2ca64d6071f29fdbc670c405d34c820afd092dbbaab04b067f3e8f852";
    private const string CodeRoot = Eli + "code/procedure_penale/20251219";
    private const string Namespace0 = "unsupported legal-content node or namespace";
    private const string IndexTermsFailure = "publisher index terms are not the reviewed shape: ";

    // ---- The pins.

    [TestMethod]
    public void Content2RuleDigestIsPinnedAndContent1sStaysFrozen()
    {
        var profile = Encoding.UTF8.GetString(LuxembourgAknLegalContentProfile2Producer.CopyRuleProfileBytes());

        Assert.AreEqual(Content2RuleSha256, LuxembourgAknLegalContentProfile2Producer.RuleProfileSha256);
        Assert.AreEqual(
            Content2RuleSha256,
            LuxembourgHeldPopulationBuilder.Sha(LuxembourgAknLegalContentProfile2Producer.CopyRuleProfileBytes()));
        Assert.AreEqual(LuxembourgAknFrozenReplayTests.Content1RuleSha256, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256);
        Assert.AreNotEqual(Content2RuleSha256, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256);
        StringAssert.StartsWith(profile, "lex-v3-luxembourg-akn-legal-content-profile/2\n");
        StringAssert.Contains(profile, "\ninventory=lex-v3-luxembourg-akn-article-inventory-profile/4;sha256=" + Inventory4RuleSha256 + ";");
        StringAssert.Contains(profile, "\nskipped-article-children=scl:JOLUXWork,scl:indexterms\n");

        // Everything /2 does not change is /1's own line, word for word.
        var content1 = Encoding.UTF8.GetString(LuxembourgAknLegalContentProfileProducer.CopyRuleProfileBytes()).Split('\n');
        foreach (var line in content1.Where(static line => line.StartsWith("source=", StringComparison.Ordinal) ||
                     line.StartsWith("containers=", StringComparison.Ordinal) ||
                     line.StartsWith("tokens=", StringComparison.Ordinal) ||
                     line.StartsWith("whitespace=", StringComparison.Ordinal) ||
                     line.StartsWith("modifications=", StringComparison.Ordinal) ||
                     line.StartsWith("marker-only=", StringComparison.Ordinal) ||
                     line.StartsWith("unknown=", StringComparison.Ordinal)))
        {
            StringAssert.Contains(profile, "\n" + line + "\n", line);
        }
    }

    // ---- The code-article test: a retained, admitted code article carrying index terms.

    [TestMethod]
    public async Task ExactRetainedCodeArticleKeepsItsIndexTermsAsMetadataNeverAsWording()
    {
        var run = await RunAsync(new LuxembourgHeldPopulationBuilder.Member(
            CodeRoot, CodeExcerptBytes(), LuxembourgUserFormatToken.Xml));

        CollectionAssert.AreEqual(
            new[] { "art_56", "art_102", "art_283" },
            run.Inventory.Outcomes.Single().Inventory!.Articles.Select(static article => article.PublisherId).ToArray());
        Assert.AreEqual(3, run.Content.Outcomes.Count);

        // art_56: admitted, and its four-level index terms are kept, in order and verbatim.
        var indexed = Outcome(run.Content, "art_56");
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, indexed.Disposition, indexed.Detail);
        var article = indexed.Article!;
        Assert.AreEqual(Content2RuleSha256, article.RuleProfileSha256);
        CollectionAssert.AreEqual(
            new[]
            {
                "Plainte",
                "Plainte > entre les mains du juge d’instruction",
                "Instruction préparatoire",
                "Instruction préparatoire > formalités d’ouverture",
                "Instruction préparatoire > formalités d’ouverture > plainte avec constitution de partie civile",
                "Action civile",
                "Action civile > exercice",
                "Action civile > exercice > instruction préparatoire",
                "Action civile > exercice > instruction préparatoire > constitution de partie civile et ses effets",
            },
            Paths(article.IndexTerms!));

        // Its wording is the publisher's, around the reference, and holds no index term.
        var reference = article.Tokens.Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.Reference);
        Assert.AreEqual("L. 16 juin 1989", reference.Text);
        Assert.AreEqual("/eli/etat/leg/loi/1989/06/16/n1/jo", reference.Target);
        Assert.AreEqual("Art. 56.", article.Tokens[0].Text);
        StringAssert.StartsWith(
            article.Tokens[^1].Text,
            " Toute personne qui se prétend lésée par un crime ou un délit");
        var searchable = SearchableText(article.Tokens);
        var stored = LuxembourgIndexBuilder.TokensJson(article.Tokens);
        foreach (var label in new[]
                 {
                     "Instruction préparatoire", "formalités d’ouverture",
                     "plainte avec constitution de partie civile", "constitution de partie civile et ses effets",
                     "entre les mains du juge d’instruction", "Action civile",
                 })
        {
            Assert.IsFalse(searchable.Contains(label, StringComparison.Ordinal), label);
            Assert.IsFalse(stored.Contains(label, StringComparison.Ordinal), label);
        }

        // Its identity (layout /2: the coordinate, the rule, the five tokens and the three top terms)
        // equals an independent Python computation from the same bytes; so does art_283's below.
        Assert.AreEqual("2017-09-05", article.Coordinate.PublisherApplicability);
        Assert.AreEqual("b0a09f478da283ef1994de8c9d537817aeed2d624f948a9a613b3fd53dcd902b", article.IdentitySha256);

        // art_102: index terms and a span; the span keeps it the typed gap /1's rules make of it.
        var spanned = Outcome(run.Content, "art_102");
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, spanned.Disposition);
        Assert.AreEqual("unsupported legal-content element: span", spanned.Detail);
        Assert.IsNull(spanned.Article);

        // art_283: no index terms; admitted with an empty list.
        var plain = Outcome(run.Content, "art_283");
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, plain.Disposition, plain.Detail);
        Assert.IsEmpty(plain.Article!.IndexTerms!);
        Assert.AreEqual("9041ab923b0ded22ecafddb3e581048f2ac2f8afe266689e255e4e27c580cdab", plain.Article.IdentitySha256);

        // /1 over the same articles: the index terms are its typed gap, and the article that has none
        // reads token for token as under /2.
        var content1 = await new LuxembourgAknLegalContentProfileProducer(run.Store)
            .RunAsync(run.Inventory, CancellationToken.None);
        Assert.AreEqual(Namespace0, Outcome(content1, "art_56").Detail);
        Assert.AreEqual(Namespace0, Outcome(content1, "art_102").Detail);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, Outcome(content1, "art_283").Disposition);
        Assert.IsNull(Outcome(content1, "art_283").Article!.IndexTerms);
        Assert.AreEqual(
            LuxembourgIndexBuilder.TokensJson(Outcome(content1, "art_283").Article!.Tokens),
            LuxembourgIndexBuilder.TokensJson(plain.Article.Tokens));
    }

    /// <summary>
    /// The same excerpt with its index-term blocks taken out: content /1 then reads <c>art_56</c> to
    /// exactly the tokens content /2 reads with the blocks in, so what the index stores, searches,
    /// digests and quotes is the same; only the article identity, which covers the terms, differs.
    /// </summary>
    [TestMethod]
    public async Task IndexTermsAreAbsentFromWordingSearchableTextAndQuotes()
    {
        var withTerms = await RunAsync(new LuxembourgHeldPopulationBuilder.Member(
            CodeRoot, CodeExcerptBytes(), LuxembourgUserFormatToken.Xml));
        var withoutTerms = await RunAsync(new LuxembourgHeldPopulationBuilder.Member(
            CodeRoot, WithoutArticleIndexTerms(CodeExcerptBytes()), LuxembourgUserFormatToken.Xml));
        var content1WithoutTerms = await new LuxembourgAknLegalContentProfileProducer(withoutTerms.Store)
            .RunAsync(withoutTerms.Inventory, CancellationToken.None);

        var two = Outcome(withTerms.Content, "art_56").Article!;
        var one = Outcome(content1WithoutTerms, "art_56").Article!;
        var twoStripped = Outcome(withoutTerms.Content, "art_56").Article!;
        Assert.IsNotEmpty(two.IndexTerms!);
        Assert.IsEmpty(twoStripped.IndexTerms!);

        Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(one.Tokens), LuxembourgIndexBuilder.TokensJson(two.Tokens));
        Assert.AreEqual(
            LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(one.Tokens)),
            LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(two.Tokens)));
        Assert.AreEqual(SearchableText(one.Tokens), SearchableText(two.Tokens));
        Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(twoStripped.Tokens), LuxembourgIndexBuilder.TokensJson(two.Tokens));
        Assert.AreNotEqual(twoStripped.IdentitySha256, two.IdentitySha256, "the identity covers the index terms");

        // The span article's gap is the same with or without its index terms, and is /1's own.
        Assert.AreEqual(Outcome(content1WithoutTerms, "art_102").Detail, Outcome(withTerms.Content, "art_102").Detail);
    }

    [TestMethod]
    public void TheContent2ExcerptFolderHoldsOnlyTheAdmittedExcerpt()
    {
        // The legal-care guard: the folder is copied whole, and holds exactly the one excerpt whose
        // provenance the class remarks give, with its pinned digest.
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", ExcerptDirectory);
        CollectionAssert.AreEquivalent(
            new[] { CodeExcerpt },
            Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path))
                .ToArray());
        Assert.AreEqual(CodeExcerptSha256, LuxembourgHeldPopulationBuilder.Sha(CodeExcerptBytes()));
    }

    // ---- The index-term shape.

    [TestMethod]
    public async Task NestedTermsAreKeptInDocumentOrderAndVerbatim()
    {
        var article = await AdmittedAsync(
            "<article id=\"art_1\"><scl:JOLUXWork><scl:jolux scl:name=\"title\">Art. 1.</scl:jolux></scl:JOLUXWork>" +
            "<scl:indexterms>\n  <scl:indexterm><scl:label> Terme synthétique un </scl:label>\n" +
            "    <scl:indexterms><scl:indexterm><scl:label>sous-terme &amp; détail</scl:label>" +
            "<scl:indexterms><scl:indexterm><scl:label>niveau trois</scl:label></scl:indexterm></scl:indexterms>" +
            "</scl:indexterm><scl:indexterm><scl:label><![CDATA[second <sous-terme>]]></scl:label></scl:indexterm>" +
            "</scl:indexterms></scl:indexterm>\n  <scl:indexterm><scl:label>Terme synthétique deux</scl:label></scl:indexterm>\n" +
            "</scl:indexterms><num>Art. 1.</num><content><p>Texte synthétique.</p></content></article>");

        CollectionAssert.AreEqual(
            new[]
            {
                " Terme synthétique un ",
                " Terme synthétique un  > sous-terme & détail",
                " Terme synthétique un  > sous-terme & détail > niveau trois",
                " Terme synthétique un  > second <sous-terme>",
                "Terme synthétique deux",
            },
            Paths(article.IndexTerms!));
        Assert.AreEqual(2, article.IndexTerms!.Count);
        Assert.AreEqual(2, article.IndexTerms[0].SubTerms.Count);
        CollectionAssert.AreEqual(
            new[] { "Art. 1.", "Texte synthétique." },
            article.Tokens.Select(static token => token.Text).ToArray());
    }

    [TestMethod]
    [DataRow("<scl:label/>", DisplayName = "empty label")]
    [DataRow("<scl:label>  \n </scl:label>", DisplayName = "white-space label")]
    [DataRow("<scl:label/><scl:indexterms><scl:indexterm><scl:label/></scl:indexterm></scl:indexterms>",
        DisplayName = "empty label over an empty template")]
    public async Task ThePublishersEmptyTemplateKeepsNothing(string term)
    {
        var article = await AdmittedAsync(
            "<article id=\"art_1\"><scl:indexterms><scl:indexterm>" + term + "</scl:indexterm></scl:indexterms>" +
            "<num>Art. 1.</num><content><p>Texte synthétique.</p></content></article>");

        Assert.IsNotNull(article.IndexTerms);
        Assert.IsEmpty(article.IndexTerms);
    }

    [TestMethod]
    [DataRow("<scl:indexterms scl:refid=\"art_1\"><scl:indexterm><scl:label>T</scl:label></scl:indexterm></scl:indexterms>",
        "an attribute on scl:indexterms")]
    [DataRow("<scl:indexterms><scl:indexterm scl:refid=\"art_1\"><scl:label>T</scl:label></scl:indexterm></scl:indexterms>",
        "an attribute on scl:indexterm")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label lang=\"fr\">T</scl:label></scl:indexterm></scl:indexterms>",
        "an attribute on scl:label")]
    [DataRow("<scl:indexterms>Texte<scl:indexterm><scl:label>T</scl:label></scl:indexterm></scl:indexterms>",
        "character data outside a label")]
    [DataRow("<scl:indexterms><scl:indexterm>Texte<scl:label>T</scl:label></scl:indexterm></scl:indexterms>",
        "character data outside a label")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label>T</scl:label><scl:articles><scl:num scl:refid=\"art_1\">1</scl:num></scl:articles></scl:indexterm></scl:indexterms>",
        "scl:indexterm holds scl:articles")]
    [DataRow("<scl:indexterms><p>T</p></scl:indexterms>", "scl:indexterms holds akn:p")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:indexterms/></scl:indexterm></scl:indexterms>",
        "scl:indexterms out of place in scl:indexterm")]
    [DataRow("<scl:indexterms><scl:indexterm></scl:indexterm></scl:indexterms>", "scl:indexterm has no scl:label")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label>T</scl:label><scl:label>U</scl:label></scl:indexterm></scl:indexterms>",
        "scl:label out of place in scl:indexterm")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label>T</scl:label><scl:indexterms/><scl:indexterms/></scl:indexterm></scl:indexterms>",
        "scl:indexterms out of place in scl:indexterm")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label><b>T</b></scl:label></scl:indexterm></scl:indexterms>",
        "scl:label holds akn:b")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label>T<?pi x?></scl:label></scl:indexterm></scl:indexterms>",
        "scl:label holds a processing instruction")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label/><scl:indexterms><scl:indexterm><scl:label>T</scl:label></scl:indexterm></scl:indexterms></scl:indexterm></scl:indexterms>",
        "an empty scl:label over nested terms")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label>T</scl:label><scl:indexterms><x:term xmlns:x=\"urn:x\"/></scl:indexterms></scl:indexterm></scl:indexterms>",
        "scl:indexterms holds term in another namespace")]
    [DataRow("<scl:indexterms><x:indexterm xmlns:x=\"urn:x\"><scl:label>T</scl:label></x:indexterm></scl:indexterms>",
        "scl:indexterms holds indexterm in another namespace")]
    [DataRow("<scl:indexterms><scl:indexterm><x:label xmlns:x=\"urn:x\">T</x:label></scl:indexterm></scl:indexterms>",
        "scl:indexterm holds label in another namespace")]
    [DataRow("<scl:indexterms><scl:indexterm><scl:label>T</scl:label><x:indexterms xmlns:x=\"urn:x\"/></scl:indexterm></scl:indexterms>",
        "scl:indexterm holds indexterms in another namespace")]
    public async Task AnyOtherIndexTermShapeIsATypedGapForThatArticleOnly(string block, string reason)
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\">" + block + "<num>Art. 1.</num><content><p>Texte synthétique.</p></content></article>" +
            "<article id=\"art_2\"><scl:indexterms><scl:indexterm><scl:label>T</scl:label></scl:indexterm></scl:indexterms>" +
            "<num>Art. 2.</num><content><p>Texte synthétique.</p></content></article>")));

        var gap = Outcome(run.Content, "art_1");
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, gap.Disposition);
        Assert.AreEqual(IndexTermsFailure + reason, gap.Detail);
        Assert.IsNull(gap.Article);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, Outcome(run.Content, "art_2").Disposition);
    }

    /// <summary>
    /// Only <c>scl:indexterms</c> itself is skipped. Another <c>scl</c> element, even one wrapping terms
    /// of the reviewed shape, and an AKN element of the same local name stay the typed gap /1 makes of
    /// them, with /1's detail, for that article only.
    /// </summary>
    [TestMethod]
    [DataRow("<scl:index><scl:indexterm><scl:label>T</scl:label></scl:indexterm></scl:index>", Namespace0)]
    [DataRow("<scl:index/>", Namespace0)]
    [DataRow("<indexterms><indexterm><label>T</label></indexterm></indexterms>", "unsupported legal-content element: indexterms")]
    public async Task AnyOtherDirectChildStaysContent1sGapForThatArticleOnly(string child, string detail)
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\">" + child + "<num>Art. 1.</num><content><p>Texte synthétique.</p></content></article>" +
            "<article id=\"art_2\"><scl:indexterms><scl:indexterm><scl:label>T</scl:label></scl:indexterm></scl:indexterms>" +
            "<num>Art. 2.</num><content><p>Texte synthétique.</p></content></article>")));
        var content1 = await new LuxembourgAknLegalContentProfileProducer(run.Store).RunAsync(run.Inventory, CancellationToken.None);

        foreach (var population in new[] { run.Content, content1 })
        {
            var gap = Outcome(population, "art_1");
            Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, gap.Disposition);
            Assert.AreEqual(detail, gap.Detail);
            Assert.IsNull(gap.Article);
        }
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, Outcome(run.Content, "art_2").Disposition);
    }

    [TestMethod]
    public async Task IndexTermsAnywhereButDirectlyUnderTheArticleStayContent1sGap()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><num>Art. 1.</num><content><scl:indexterms><scl:indexterm><scl:label>T</scl:label>" +
            "</scl:indexterm></scl:indexterms><p>Texte synthétique.</p></content></article>")));

        var outcome = run.Content.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outcome.Disposition);
        Assert.AreEqual(Namespace0, outcome.Detail);
    }

    [TestMethod]
    public async Task IndexTermsAreNotWordingSoAnArticleHoldingOnlyThemHasNone()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><scl:JOLUXWork/><scl:indexterms><scl:indexterm><scl:label>Terme synthétique</scl:label>" +
            "</scl:indexterm></scl:indexterms></article>")));

        var outcome = run.Content.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outcome.Disposition);
        Assert.AreEqual("article contains no publisher legal wording or marker evidence", outcome.Detail);
    }

    [TestMethod]
    public async Task MarkerOnlyEvidenceKeepsItsIndexTerms()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><scl:indexterms><scl:indexterm><scl:label>Terme synthétique</scl:label>" +
            "</scl:indexterm></scl:indexterms><content><p><mod class=\"mod-start\" for=\"#pm1\"/>" +
            "<mod class=\"mod-end\" for=\"#pm1\"/></p></content></article>")));

        var outcome = run.Content.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence, outcome.Disposition);
        CollectionAssert.AreEqual(new[] { "Terme synthétique" }, Paths(outcome.Article!.IndexTerms!));
    }

    // ---- Every other shape is /1's.

    /// <summary>
    /// Each shape twice, without and with index terms. Content /1 makes a typed gap of each; content /2
    /// makes the same gap with the same detail, with or without the terms, while /1 meets the terms
    /// first. The admitted shapes read to the same tokens.
    /// </summary>
    [TestMethod]
    public async Task EveryOtherShapeStaysContent1sTypedGapWithItsDetail()
    {
        var gaps = new (string Name, string Content, string Detail)[]
        {
            ("table", "<table><tr><td><p>Texte synthétique</p></td></tr></table>", "unsupported legal-content element: table"),
            ("span", "<p><span>Texte synthétique</span></p>", "unsupported legal-content element: span"),
            ("eop", "<p>Texte<eop/>synthétique</p>", "unsupported legal-content element: eop"),
            ("br", "<p>Texte<br/>synthétique</p>", "unsupported legal-content element: br"),
            ("u", "<p><u>Texte synthétique</u></p>", "unsupported legal-content element: u"),
            ("sub", "<p>Texte<sub>2</sub></p>", "unsupported legal-content element: sub"),
            ("del", "<p><del>Texte synthétique</del></p>", "unsupported legal-content element: del"),
            ("embeddedtext", "<p><embeddedText>Texte synthétique</embeddedText></p>", "unsupported legal-content element: embeddedText"),
            ("quotedstructure", "<p><quotedStructure><p>Texte synthétique</p></quotedStructure></p>", "unsupported legal-content element: quotedStructure"),
            ("noterefnomarker", "<p>Texte<noteRef href=\"#n1\"/></p>", "note reference is not empty or lacks bounded publisher evidence"),
            ("modnotempty", "<p><mod class=\"mod-start\" for=\"#pm1\">Texte</mod></p>", "modification boundary is not empty or lacks a bounded publisher target"),
            ("reflabel", "<p><ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\"><span>la loi</span></ref></p>", "reference contains an unsupported publisher label shape"),
            ("foreign", "<p><x:y xmlns:x=\"urn:x\">Texte</x:y></p>", Namespace0),
            ("pi", "<p>Texte<?pi x?></p>", Namespace0),
            ("unclosed", "<p>Texte<mod class=\"mod-start\" for=\"#pm1\"/></p>", "article ends with an unclosed modification span: #pm1"),
            ("nowording", "<p> </p>", "article contains no publisher legal wording or marker evidence"),
        };
        var admitted = new (string Name, string Content)[]
        {
            ("plain", "<p>Texte synthétique.</p>"),
            ("ref", "<p>voir <ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">la <b>loi</b></ref> ensuite</p>"),
            ("mod", "<p>voir <mod class=\"mod-start\" for=\"#pm1\"/>ensuite<mod class=\"mod-end\" for=\"#pm1\"/></p>"),
            ("note", "<p>voir<noteRef href=\"#n2\" marker=\"2\"/></p>"),
            ("markeronly", "<p><mod class=\"mod-start\" for=\"#pm1\"/><mod class=\"mod-end\" for=\"#pm1\"/></p>"),
        };
        const string terms = "<scl:indexterms><scl:indexterm><scl:label>Terme synthétique</scl:label></scl:indexterm></scl:indexterms>";
        var articles = new StringBuilder();
        foreach (var (name, content, _) in gaps)
        {
            articles.Append("<article id=\"art_").Append(name).Append("_a\"><content>").Append(content).Append("</content></article>");
            articles.Append("<article id=\"art_").Append(name).Append("_b\">").Append(terms).Append("<content>").Append(content).Append("</content></article>");
        }
        foreach (var (name, content) in admitted)
        {
            articles.Append("<article id=\"art_").Append(name).Append("_a\"><content>").Append(content).Append("</content></article>");
            articles.Append("<article id=\"art_").Append(name).Append("_b\">").Append(terms).Append("<content>").Append(content).Append("</content></article>");
        }
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            articles.ToString(),
            "<note id=\"n1\" marker=\"1\"><p>Note synthétique.</p></note><note id=\"n2\" marker=\"2\"><p>Note synthétique.</p></note>")));
        var content1 = await new LuxembourgAknLegalContentProfileProducer(run.Store).RunAsync(run.Inventory, CancellationToken.None);

        foreach (var (name, _, detail) in gaps)
        {
            foreach (var (population, id, expected) in new[]
                     {
                         (content1, "_a", detail), (run.Content, "_a", detail), (run.Content, "_b", detail), (content1, "_b", Namespace0),
                     })
            {
                var outcome = Outcome(population, "art_" + name + id);
                Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outcome.Disposition, name + id);
                Assert.AreEqual(expected, outcome.Detail, name + id);
                Assert.IsNull(outcome.Article, name + id);
            }
        }

        foreach (var (name, _) in admitted)
        {
            var one = Outcome(content1, "art_" + name + "_a");
            var twoPlain = Outcome(run.Content, "art_" + name + "_a");
            var twoIndexed = Outcome(run.Content, "art_" + name + "_b");
            Assert.AreEqual(one.Disposition, twoPlain.Disposition, name);
            Assert.AreEqual(one.Disposition, twoIndexed.Disposition, name);
            Assert.AreEqual(Namespace0, Outcome(content1, "art_" + name + "_b").Detail, name);
            Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(one.Article!.Tokens), LuxembourgIndexBuilder.TokensJson(twoPlain.Article!.Tokens), name);
            Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(one.Article.Tokens), LuxembourgIndexBuilder.TokensJson(twoIndexed.Article!.Tokens), name);
            CollectionAssert.AreEqual(new[] { "Terme synthétique" }, Paths(twoIndexed.Article.IndexTerms!), name);
        }
    }

    /// <summary>The design (§1.4) names these two /1 tests as true under /2 too.</summary>
    [TestMethod]
    public async Task UnknownLegalContentElementQuarantinesOnlyThatArticle()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><num>Art. 1.</num><content><p>kept</p></content></article>" +
            "<article id=\"art_2\"><num>Art. 2.</num><content><invented>guess</invented></content></article>")));

        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, run.Content.Outcomes[0].Disposition);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, run.Content.Outcomes[1].Disposition);
        Assert.IsNull(run.Content.Outcomes[1].Article);
        StringAssert.Contains(run.Content.Outcomes[1].Detail, "invented");
    }

    [TestMethod]
    public async Task ModificationSpanCannotCrossAnArticleBoundary()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><content><p>before<mod class=\"mod-start\" for=\"#pm1\"/></p></content></article>" +
            "<article id=\"art_2\"><content><p>after<mod class=\"mod-end\" for=\"#pm1\"/></p></content></article>")));

        Assert.IsTrue(run.Content.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));
        Assert.IsTrue(run.Content.Outcomes.All(static outcome => outcome.Article is null));
    }

    // ---- Which articles it reads.

    [TestMethod]
    public async Task Content2RefusesAPopulationThatInventory4DidNotMake()
    {
        var (store, population) = await LuxembourgHeldPopulationBuilder.CreateAsync(
            Member(Eli + "rgd/2001/02/03/n9/jo", Akn("<article id=\"art_1\"><content><p>Texte synthétique.</p></content></article>")));
        var inventory3 = await new LuxembourgAknArticleInventoryProducer(store).RunAsync(population, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            new LuxembourgAknLegalContentProfile2Producer(store).RunAsync(inventory3, CancellationToken.None));
    }

    /// <summary>
    /// The door's own refusal, apart from the content population's, which refuses only an inventory /3
    /// source: a population under any third inventory digest is refused by the door alone, and a /3
    /// population is refused by the door, with its detail, before the population could refuse it.
    /// </summary>
    [TestMethod]
    public async Task TheDoorAloneRefusesAPopulationUnderAnyOtherInventoryDigest()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><content><p>Texte synthétique.</p></content></article>")));
        var inventory3 = await new LuxembourgAknArticleInventoryProducer(run.Store)
            .RunAsync(run.Inventory.SourcePopulation, CancellationToken.None);
        var otherDigest = new LuxembourgAknArticleInventoryPopulation(run.Inventory.SourcePopulation, [], new string('0', 64));

        foreach (var population in new[] { inventory3, otherDigest })
        {
            var refused = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                new LuxembourgAknLegalContentProfile2Producer(run.Store).RunAsync(population, CancellationToken.None));
            Assert.AreEqual("inventoryPopulation", refused.ParamName);
            StringAssert.StartsWith(refused.Message, "Content /2 reads only an inventory /4 population.");
        }
    }

    /// <summary>
    /// The one admitted file run 15 fetched for both <c>code/civil/20181101</c> and <c>/20190428</c>:
    /// /4 refuses both states for naming another, and /2 reads neither, although alone the same file
    /// reads to two admitted articles carrying index terms.
    /// </summary>
    [TestMethod]
    public async Task AFileRefusedForNamingAnotherStateIsNeverTokenized()
    {
        var civil = InventoryV4Fixture("code-civil-20190428-shared-with-20181101", "62303aa38a47af61ee6efd8ae9bc451397abc31b20b306afeca3dc3f675ea53d");
        var shared = await RunAsync(
            Member(Eli + "code/civil/20181101", civil, 1),
            Member(Eli + "code/civil/20190428", civil, 2));

        Assert.IsTrue(shared.Inventory.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState && outcome.Inventory is not null));
        Assert.AreEqual(2, shared.Content.Outcomes.Count);
        foreach (var outcome in shared.Content.Outcomes)
        {
            Assert.AreEqual(LuxembourgAknLegalContentDisposition.UpstreamNotInventoried, outcome.Disposition);
            Assert.AreEqual("PublisherFileNamesOtherState", outcome.Detail);
            Assert.IsNull(outcome.Coordinate);
            Assert.IsNull(outcome.Article);
        }

        var alone = await RunAsync(Member(Eli + "code/civil/20190428", civil));
        Assert.IsTrue(alone.Content.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknLegalContentDisposition.Admitted && outcome.Article!.IndexTerms!.Count != 0));
        Assert.AreEqual(2, alone.Content.Outcomes.Count);
    }

    /// <summary>
    /// R10 of the S2 brief: a body with annex articles reads /4's main-body articles. /1's selection
    /// (every top-level article) would see four articles against /4's one and refuse the body.
    /// </summary>
    [TestMethod]
    public async Task ExcludedArticlesAreNeverReadSoTheCoordinatesMatch()
    {
        var a493 = InventoryV4Fixture("act-loi-2019-07-12-a493-annex-articles-without-id", "cd7e2827e671755b6f1bb3f1da0dcf131113650e4335b69162707ee6c5bbc4a1");
        var run = await RunAsync(Member(Eli + "loi/2019/07/12/a493/jo", a493));

        Assert.AreEqual(3, run.Inventory.Outcomes.Single().Inventory!.Evidence!.ExcludedArticleCount);
        var outcome = run.Content.Outcomes.Single();
        Assert.AreEqual("article_unique", outcome.Coordinate!.PublisherId);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, outcome.Disposition, outcome.Detail);

        var content1 = await new LuxembourgAknLegalContentProfileProducer(run.Store).RunAsync(run.Inventory, CancellationToken.None);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch, content1.Outcomes.Single().Disposition);
    }

    [TestMethod]
    public async Task EveryBodyInventory4DidNotInventoryIsUpstreamNotInventoried()
    {
        var run = await RunAsync(
            Member(Eli + "rgd/2001/02/03/n9/jo", "not a pdf"u8.ToArray(), 1, LuxembourgUserFormatToken.PdfA),
            Member(Eli + "rgd/2001/02/03/n8/jo", Akn("<p>no article</p>"), 2));

        CollectionAssert.AreEqual(
            new[] { "NotAkn", "XmlRejected" },
            run.Content.Outcomes.Select(static outcome => outcome.Detail).ToArray());
        Assert.IsTrue(run.Content.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknLegalContentDisposition.UpstreamNotInventoried && outcome.Coordinate is null));
    }

    [TestMethod]
    public async Task MissingCustodyIsRetainedBytesUnavailableForEveryArticle()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><content><p>un</p></content></article><article id=\"art_2\"><content><p>deux</p></content></article>")));

        var content = await new LuxembourgAknLegalContentProfile2Producer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(run.Inventory, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "art_1", "art_2" }, content.Outcomes.Select(static outcome => outcome.Coordinate!.PublisherId).ToArray());
        Assert.IsTrue(content.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknLegalContentDisposition.RetainedBytesUnavailable && outcome.Article is null));
    }

    [TestMethod]
    public void PublicDoorAcceptsTheInventoryPopulationAndNoCallerTokenMap()
    {
        var parameters = typeof(LuxembourgAknLegalContentProfile2Producer)
            .GetMethod(nameof(LuxembourgAknLegalContentProfile2Producer.RunAsync))!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgAknArticleInventoryPopulation), typeof(CancellationToken) },
            parameters);
    }

    // ---- Identities and the shared types.

    [TestMethod]
    public async Task TheArticleIdentityIsLayoutTwoAndCoversTheIndexTerms()
    {
        static Task<LuxembourgAknLegalContentArticle> ArticleAsync(string terms) => AdmittedAsync(
            "<article id=\"art_1\">" + terms + "<num>Art. 1.</num><content><p>voir <ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">la loi</ref>" +
            "<noteRef href=\"#n1\" marker=\"1\"/></p></content></article>",
            "<note id=\"n1\" marker=\"1\"><p>Note synthétique.</p></note>");

        const string nested = "<scl:indexterms><scl:indexterm><scl:label>A</scl:label><scl:indexterms><scl:indexterm><scl:label>B</scl:label>" +
            "</scl:indexterm></scl:indexterms></scl:indexterm></scl:indexterms>";
        const string flat = "<scl:indexterms><scl:indexterm><scl:label>A</scl:label></scl:indexterm><scl:indexterm><scl:label>B</scl:label>" +
            "</scl:indexterm></scl:indexterms>";
        var nestedArticle = await ArticleAsync(nested);
        var flatArticle = await ArticleAsync(flat);
        var noneArticle = await ArticleAsync("");

        foreach (var article in new[] { nestedArticle, flatArticle, noneArticle })
        {
            Assert.AreEqual(Article2Identity(article), article.IdentitySha256);
        }
        Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(nestedArticle.Tokens), LuxembourgIndexBuilder.TokensJson(flatArticle.Tokens));
        Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(nestedArticle.Tokens), LuxembourgIndexBuilder.TokensJson(noneArticle.Tokens));
        Assert.AreEqual(3, new[] { nestedArticle, flatArticle, noneArticle }.Select(static article => article.IdentitySha256).Distinct().Count());
    }

    [TestMethod]
    public async Task ThePopulationIdentityIsLayoutTwoAndStableAcrossCustodyRuns()
    {
        var body = Akn(
            "<article id=\"art_1\"><scl:indexterms><scl:indexterm><scl:label>T</scl:label></scl:indexterm></scl:indexterms>" +
            "<content><p>Texte synthétique.</p></content></article><article id=\"art_2\"><content><table/></content></article>");
        var first = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", body));
        var second = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", body));

        Assert.AreEqual(Content2RuleSha256, first.Content.RuleProfileSha256);
        Assert.AreEqual(Population2Identity(first.Content), first.Content.IdentitySha256);
        Assert.AreEqual(first.Content.IdentitySha256, second.Content.IdentitySha256);
        Assert.AreEqual(first.Content.Outcomes[0].Article!.IdentitySha256, second.Content.Outcomes[0].Article!.IdentitySha256);
        Assert.AreSame(first.Inventory, first.Content.SourceInventoryPopulation);

        var content1 = await new LuxembourgAknLegalContentProfileProducer(first.Store).RunAsync(first.Inventory, CancellationToken.None);
        Assert.AreEqual(LuxembourgAknLegalContentProfileProducer.RuleProfileSha256, content1.RuleProfileSha256);
    }

    [TestMethod]
    public async Task TheSharedTypesKeepContent1AndContent2Apart()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><content><p>Texte synthétique.</p></content></article>")));
        var content1 = await new LuxembourgAknLegalContentProfileProducer(run.Store).RunAsync(run.Inventory, CancellationToken.None);
        var inventory3 = await new LuxembourgAknArticleInventoryProducer(run.Store)
            .RunAsync(run.Inventory.SourcePopulation, CancellationToken.None);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgAknLegalContentPopulation(run.Inventory, run.Content.Outcomes));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgAknLegalContentPopulation(run.Inventory, content1.Outcomes, Content2RuleSha256));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgAknLegalContentPopulation(run.Inventory, run.Content.Outcomes, new string('0', 64)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgAknLegalContentPopulation(inventory3, [], Content2RuleSha256));
        Assert.AreEqual(
            run.Content.IdentitySha256,
            new LuxembourgAknLegalContentPopulation(run.Inventory, run.Content.Outcomes, Content2RuleSha256).IdentitySha256);

        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknIndexTerm(" \n", []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgAknIndexTerm("T", [null!]));
    }

    // ---- Helpers.

    private sealed record Run(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        LuxembourgAknArticleInventoryPopulation Inventory,
        LuxembourgAknLegalContentPopulation Content);

    private static async Task<Run> RunAsync(params LuxembourgHeldPopulationBuilder.Member[] members)
    {
        var (store, population) = await LuxembourgHeldPopulationBuilder.CreateAsync(members);
        var inventory = await new LuxembourgAknArticleInventoryProducerV4(store)
            .RunAsync(population, new Dictionary<string, string>(), CancellationToken.None);
        var content = await new LuxembourgAknLegalContentProfile2Producer(store)
            .RunAsync(inventory, CancellationToken.None);
        return new Run(store, inventory, content);
    }

    private static async Task<LuxembourgAknLegalContentArticle> AdmittedAsync(string articles, string? afterArticles = null)
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(articles, afterArticles)));
        var outcome = run.Content.Outcomes.Single();
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, outcome.Disposition, outcome.Detail);
        return outcome.Article!;
    }

    private static LuxembourgHeldPopulationBuilder.Member Member(
        string root,
        byte[] body,
        int? ordinal = null,
        LuxembourgUserFormatToken token = LuxembourgUserFormatToken.XmlAkomaNtoso) =>
        new(root, body, token, ordinal);

    private static LuxembourgAknLegalContentOutcome Outcome(LuxembourgAknLegalContentPopulation population, string id) =>
        population.Outcomes.Single(outcome => outcome.Coordinate?.PublisherId == id);

    /// <summary>Every term as the path of labels from its top term, depth first.</summary>
    private static string[] Paths(IReadOnlyList<LuxembourgAknIndexTerm> terms)
    {
        var paths = new List<string>();
        void Walk(IReadOnlyList<LuxembourgAknIndexTerm> level, string prefix)
        {
            foreach (var term in level)
            {
                var path = prefix.Length == 0 ? term.Label : prefix + " > " + term.Label;
                paths.Add(path);
                Walk(term.SubTerms, path);
            }
        }
        Walk(terms, "");
        return paths.ToArray();
    }

    /// <summary>
    /// <c>articles.searchable_text</c> as the index builder projects it: the text and reference tokens,
    /// concatenated. Quotes and evidence bundles read that text and <c>tokens_json</c>, nothing else.
    /// </summary>
    private static string SearchableText(IReadOnlyList<LuxembourgAknLegalContentToken> tokens) =>
        string.Concat(tokens
            .Where(static token => token.Kind is LuxembourgAknLegalContentTokenKind.Text or LuxembourgAknLegalContentTokenKind.Reference)
            .Select(static token => token.Text));

    private static byte[] CodeExcerptBytes()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", ExcerptDirectory, CodeExcerpt));
        Assert.AreEqual(CodeExcerptSha256, LuxembourgHeldPopulationBuilder.Sha(bytes), CodeExcerpt);
        return bytes;
    }

    private static byte[] InventoryV4Fixture(string name, string sha256)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LuAknInventoryV4", name + ".bin"));
        Assert.AreEqual(sha256, LuxembourgHeldPopulationBuilder.Sha(bytes), name);
        return bytes;
    }

    /// <summary>The same document with every article's direct-child <c>scl:indexterms</c> taken out.</summary>
    private static byte[] WithoutArticleIndexTerms(byte[] bytes)
    {
        XNamespace akn = "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
        XNamespace scl = "http://www.scl.lu";
        XDocument document;
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        var blocks = document.Descendants(akn + "article").SelectMany(article => article.Elements(scl + "indexterms")).ToArray();
        Assert.AreEqual(2, blocks.Length, "art_56 and art_102 carry one block each");
        foreach (var block in blocks)
        {
            block.Remove();
        }
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
        {
            document.Save(writer);
        }
        return output.ToArray();
    }

    /// <summary>A synthetic AKN act with no identification block (inventory /4 reads it as <c>absent</c>).</summary>
    private static byte[] Akn(string articles, string? afterArticles = null) => Encoding.UTF8.GetBytes(
        "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\" " +
        "xmlns:scl=\"http://www.scl.lu\"><act><body>" + articles + (afterArticles ?? "") +
        "</body></act></akomaNtoso>");

    /// <summary>Article identity layout <c>/2</c>, written out by hand from its specification.</summary>
    private static string Article2Identity(LuxembourgAknLegalContentArticle article)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Put(hash, "lex-v3-luxembourg-akn-legal-content-article/2");
        Put(hash, article.PublisherExpressionIri);
        Put(hash, article.Coordinate.PublisherId);
        Put(hash, article.Coordinate.PublisherWId ?? "");
        Put(hash, article.Coordinate.PublisherApplicability ?? "");
        Put(hash, Content2RuleSha256);
        Put(hash, article.Tokens.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var token in article.Tokens)
        {
            PutToken(hash, token);
        }
        Put(hash, article.IndexTerms!.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var term in article.IndexTerms)
        {
            PutTerm(hash, term);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        static void PutToken(IncrementalHash hash, LuxembourgAknLegalContentToken token)
        {
            Put(hash, ((int)token.Kind).ToString(CultureInfo.InvariantCulture));
            Put(hash, token.Text ?? "");
            Put(hash, token.Target ?? "");
            Put(hash, token.Marker ?? "");
            Put(hash, (token.NoteBody?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            foreach (var nested in token.NoteBody ?? [])
            {
                PutToken(hash, nested);
            }
        }

        static void PutTerm(IncrementalHash hash, LuxembourgAknIndexTerm term)
        {
            Put(hash, term.Label);
            Put(hash, term.SubTerms.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var subTerm in term.SubTerms)
            {
                PutTerm(hash, subTerm);
            }
        }
    }

    /// <summary>Population identity layout <c>/2</c>, written out by hand from its specification.</summary>
    private static string Population2Identity(LuxembourgAknLegalContentPopulation population)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Put(hash, "lex-v3-luxembourg-akn-legal-content-population/2");
        Put(hash, Content2RuleSha256);
        Put(hash, population.SourceInventoryPopulation.IdentitySha256);
        foreach (var outcome in population.Outcomes)
        {
            Put(hash, outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef.PublisherUri);
            Put(hash, outcome.Coordinate?.PublisherId ?? "");
            Put(hash, ((int)outcome.Disposition).ToString(CultureInfo.InvariantCulture));
            Put(hash, outcome.Article?.IdentitySha256 ?? "");
            Put(hash, outcome.Disposition is LuxembourgAknLegalContentDisposition.UpstreamNotInventoried
                or LuxembourgAknLegalContentDisposition.XmlRejected
                or LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch
                or LuxembourgAknLegalContentDisposition.UnsupportedContentShape
                ? outcome.Detail ?? ""
                : "");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Put(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
