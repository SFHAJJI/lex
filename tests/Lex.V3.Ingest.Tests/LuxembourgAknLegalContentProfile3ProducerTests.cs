using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Content /3 (slice S3b of the Luxembourg data design, §1.4): content /2's article reading with the shapes the design's /3
/// table admits, each held here by a retained admitted excerpt or a synthetic one; every shape it does not admit stays a
/// typed gap, and every shape /2 admits reads to the same stored tokens.
/// </summary>
/// <remarks>
/// <para>
/// REAL BYTES ONLY FROM ADMITTED SOURCES. Two byte-exact excerpts, each cut as S2 and S3a cut theirs (the source bytes with
/// whole element ranges removed, each with the white space before it, so every byte kept is the publisher's, in order),
/// from bodies run 15 acquired under the agreed CC BY reading (<c>agreed_same_run_cc_by</c>):
/// </para>
/// <list type="bullet">
/// <item><c>Fixtures/LuAknLegalContent3/act-loi-2019-12-20-a873-quotations-and-struck-spans.bin</c> (13,530 bytes) of
/// <c>loi/2019/12/20/a873/jo</c> (run-15 body <c>3236529cb312163e45de0d675041a4905c2be8ef2ea4ec5d870345ed62a97081</c>,
/// 37,957,824 bytes, fetched as <c>xml-akomantoso</c>; 4 ranges removed, the act's images among them). It keeps
/// <c>art_1er</c> and <c>art_3</c> (quoted wording, a structure whose marks the publisher writes in the wording),
/// <c>art_2</c> (a struck span, and an underline inside a quoted structure: a typed gap), <c>art_5</c> (plain) and
/// <c>art_13</c> (two struck spans, two quoted structures and an <c>eop</c>).</item>
/// <item><c>Fixtures/LuAknLegalContent3/code-consommation-20220101-deleted-wording.bin</c> (21,533 bytes) of
/// <c>code/consommation/20220101</c> (run-15 body <c>9ade6eb06510462315d7de665e032c0e53bb28b51e71b81044a51bb0b19708e6</c>,
/// 1,749,582 bytes, fetched as <c>xml</c>; 38 ranges removed). It keeps <c>art_l_221-1</c> (a note reference without a
/// marker), <c>art_l_226-23</c> (deleted wording), <c>art_l_311-2</c> (deleted wording holding two references) and
/// <c>art_r302-1</c> (an underlined number).</item>
/// </list>
/// <para>
/// Every other shape is synthetic, with invented wording such as "Texte synthétique". No consolidation is admitted in run
/// 15 (all 2,591 XML consolidations are withheld), so a consolidation's <c>del</c>, and the struck span of
/// <c>loi/2012/03/21/n1/consolide/20220614</c>, are held by synthetic excerpts in the publisher's shape. The three withheld
/// fixtures get no use here.
/// </para>
/// <para>
/// The excerpt articles' identities, tokens and searchable texts were computed independently, from the rule text, by a
/// Python reading of /3 (lxml) that shares no code with this one; the rule digest likewise (<c>hashlib.sha256</c> over the
/// text written out line by line).
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgAknLegalContentProfile3ProducerTests
{
    /// <summary>Profile /3: its own text, which names inventory /4's and content /2's digests. Recomputed in Python.</summary>
    internal const string Content3RuleSha256 = "68ae07566a136b95a728806056bfccd81e220b4fa9d8f52af8343c78c12111c3";

    private const string Eli = LuxembourgHeldPopulationBuilder.Eli;
    private const string ExcerptDirectory = "LuAknLegalContent3";
    private const string ActExcerpt = "act-loi-2019-12-20-a873-quotations-and-struck-spans.bin";
    private const string ActExcerptSha256 = "c574900888053b415167e259325de5a369dc0f81dd1843f0804287db888bb84e";
    private const string ActRoot = Eli + "loi/2019/12/20/a873/jo";
    private const string CodeExcerpt = "code-consommation-20220101-deleted-wording.bin";
    private const string CodeExcerptSha256 = "b5adf8ccdb4e2ab79a7d5fe78655225effb953007e7be468b1de0997d963b3b3";
    private const string CodeRoot = Eli + "code/consommation/20220101";
    private const string Namespace0 = "unsupported legal-content node or namespace";

    // ---- The pins.

    [TestMethod]
    public void Content3RuleDigestIsPinnedAndContent1And2StayFrozen()
    {
        var profile = Encoding.UTF8.GetString(LuxembourgAknLegalContentProfile3Producer.CopyRuleProfileBytes());

        Assert.AreEqual(Content3RuleSha256, LuxembourgAknLegalContentProfile3Producer.RuleProfileSha256);
        Assert.AreEqual(Content3RuleSha256,
            LuxembourgHeldPopulationBuilder.Sha(LuxembourgAknLegalContentProfile3Producer.CopyRuleProfileBytes()));
        Assert.AreEqual(2746, LuxembourgAknLegalContentProfile3Producer.CopyRuleProfileBytes().Length);
        Assert.AreEqual(21, profile.Count(static character => character == '\n'));
        Assert.AreEqual(LuxembourgAknFrozenReplayTests.Content1RuleSha256, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256);
        Assert.AreEqual(LuxembourgAknLegalContentProfile2ProducerTests.Content2RuleSha256, LuxembourgAknLegalContentProfile2Producer.RuleProfileSha256);
        StringAssert.StartsWith(profile, "lex-v3-luxembourg-akn-legal-content-profile/3\n");
        StringAssert.Contains(profile, "\ninventory=lex-v3-luxembourg-akn-article-inventory-profile/4;sha256="
            + LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256 + ";");
        StringAssert.Contains(profile, "\nindex-terms=lex-v3-luxembourg-akn-legal-content-profile/2;sha256="
            + LuxembourgAknLegalContentProfile2ProducerTests.Content2RuleSha256 + ";");

        // /2's lines that /3 does not change are /2's own, word for word.
        var content2 = Encoding.UTF8.GetString(LuxembourgAknLegalContentProfile2Producer.CopyRuleProfileBytes()).Split('\n');
        foreach (var line in content2.Where(static line => line.StartsWith("source=", StringComparison.Ordinal) ||
                     line.StartsWith("inventory=", StringComparison.Ordinal) ||
                     line.StartsWith("document=", StringComparison.Ordinal) ||
                     line.StartsWith("articles=", StringComparison.Ordinal) ||
                     line.StartsWith("skipped-article-children=", StringComparison.Ordinal) ||
                     line.StartsWith("whitespace=", StringComparison.Ordinal) ||
                     line.StartsWith("marker-only=", StringComparison.Ordinal) ||
                     line.StartsWith("unknown=", StringComparison.Ordinal)))
        {
            StringAssert.Contains(profile, "\n" + line + "\n", line);
        }
    }

    [TestMethod]
    public void TheNewTokenKindsHaveTheirWireNames()
    {
        Assert.AreEqual("quotation_start", Lex.V3.Contracts.ContractWire.NameOf(LuxembourgAknLegalContentTokenKind.QuotationStart));
        Assert.AreEqual("quotation_end", Lex.V3.Contracts.ContractWire.NameOf(LuxembourgAknLegalContentTokenKind.QuotationEnd));
        Assert.AreEqual("deleted_text", Lex.V3.Contracts.ContractWire.NameOf(LuxembourgAknLegalContentTokenKind.DeletedText));
        // The numbers are hashed into the article identity, so they are pinned with the names.
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            Enum.GetValues<LuxembourgAknLegalContentTokenKind>().Select(static kind => (int)kind).ToArray());
    }

    [TestMethod]
    public void TheContent3ExcerptFolderHoldsOnlyTheAdmittedExcerpts()
    {
        // The legal-care guard: the folder holds exactly the two excerpts whose provenance the class remarks give.
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", ExcerptDirectory);
        CollectionAssert.AreEquivalent(
            new[] { ActExcerpt, CodeExcerpt },
            Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path))
                .ToArray());
        Assert.AreEqual(ActExcerptSha256, LuxembourgHeldPopulationBuilder.Sha(Excerpt(ActExcerpt, ActExcerptSha256)));
        Assert.AreEqual(CodeExcerptSha256, LuxembourgHeldPopulationBuilder.Sha(Excerpt(CodeExcerpt, CodeExcerptSha256)));
    }

    // ---- The retained excerpts.

    /// <summary>
    /// An amending act's quoted wording and struck spans (<c>loi/2019/12/20/a873/jo</c>): the quoted wording is the act's own
    /// and searchable; the struck numbers are deleted wording, absent from the searchable text; the underline inside a quoted
    /// structure keeps <c>art_2</c> a typed gap.
    /// </summary>
    [TestMethod]
    public async Task ExactRetainedAmendingActReadsItsQuotationsAndStruckSpans()
    {
        var run = await RunAsync(Member(ActRoot, Excerpt(ActExcerpt, ActExcerptSha256)));
        var two = await new LuxembourgAknLegalContentProfile2Producer(run.Store).RunAsync(run.Inventory, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "art_1er", "art_2", "art_3", "art_5", "art_13" },
            run.Content.Outcomes.Select(static outcome => outcome.Coordinate!.PublisherId).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "unsupported legal-content element: embeddedText", "unsupported legal-content element: span",
                "unsupported legal-content element: embeddedText", null, "unsupported legal-content element: eop",
            },
            two.Outcomes.Select(static outcome => outcome.Detail).ToArray(),
            "what /2 makes of the same articles");

        // art_1er: two quoted terms, inline, the marks not written in the wording.
        var first = Admitted(run.Content, "art_1er");
        Assert.AreEqual(Content3RuleSha256, first.RuleProfileSha256);
        var quotes = first.Tokens.Where(static token => token.Kind == LuxembourgAknLegalContentTokenKind.QuotationStart).ToArray();
        Assert.HasCount(2, quotes);
        Assert.IsTrue(quotes.All(static token => token.Quotation == "embeddedText" && !token.MarksInWording && token.Marker is null));
        CollectionAssert.AreEqual(new[] { "le plan", "les plans" }, QuotedTexts(first.Tokens));
        StringAssert.Contains(SearchableText(first.Tokens), "les termes\u00a0le plan sont remplacés par ceux de\u00a0les plans.");
        Assert.AreEqual("cf36e22165827e59639b44137b270123e996fc8a03eab7cae6ac8eef26ba5700", first.IdentitySha256);
        Assert.AreEqual(Article3Identity(first), first.IdentitySha256);

        // art_2: a struck span, and an underline inside a quoted structure, which can mark inserted wording.
        var underlined = Outcome(run.Content, "art_2");
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, underlined.Disposition);
        Assert.AreEqual("underline inside quoted or amending wording: u", underlined.Detail);
        Assert.IsNull(underlined.Article);

        // art_3: inline quotations and a structure whose marks the publisher writes in the wording.
        var third = Admitted(run.Content, "art_3");
        var structure = third.Tokens.Single(static token =>
            token.Kind == LuxembourgAknLegalContentTokenKind.QuotationStart && token.Quotation == "embeddedStructure");
        Assert.IsTrue(structure.MarksInWording, "scl:hide-quote=\"true\": the marks are the publisher's own characters");
        Assert.IsTrue(third.Tokens.Single(static token =>
            token.Kind == LuxembourgAknLegalContentTokenKind.QuotationEnd && token.Quotation == "embeddedStructure").MarksInWording);
        StringAssert.StartsWith(QuotedTexts(third.Tokens)[^1], "«\u00a0Les itinéraires cyclables du réseau national");
        Assert.AreEqual("996679254a9ddaa50fe1e7ce5bffa3a7689f7363949ad265e00edc1d475df21e", third.IdentitySha256);

        // art_5: a shape /2 admits reads to /2's tokens; only the identity layout differs.
        var plain = Admitted(run.Content, "art_5");
        Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(Outcome(two, "art_5").Article!.Tokens), LuxembourgIndexBuilder.TokensJson(plain.Tokens));
        Assert.AreNotEqual(Outcome(two, "art_5").Article!.IdentitySha256, plain.IdentitySha256);
        Assert.AreEqual("0cda7bbd3f51fd05b8be17c1748239c2e16749b8ef8b04f4c359f943e67122ae", plain.IdentitySha256);

        // art_13: the struck old numbers are deleted wording, never searchable; the eop before the number makes no token.
        var thirteenth = Admitted(run.Content, "art_13");
        CollectionAssert.AreEqual(
            new[] { "3.", "4." },
            thirteenth.Tokens.Where(static token => token.Kind == LuxembourgAknLegalContentTokenKind.DeletedText)
                .Select(static token => token.Text).ToArray());
        Assert.AreEqual("Art. 13.", thirteenth.Tokens[0].Text);
        var searchable = SearchableText(thirteenth.Tokens);
        StringAssert.Contains(searchable, "modifications suivantes : 1. L’alinéa 1er est remplacé comme suit :La permission de voirie");
        Assert.IsFalse(searchable.Contains("3. 1.", StringComparison.Ordinal));
        Assert.IsFalse(searchable.Contains("4. 2.", StringComparison.Ordinal));
        Assert.HasCount(2, thirteenth.Tokens.Where(static token =>
            token.Kind == LuxembourgAknLegalContentTokenKind.QuotationStart && token.Quotation == "embeddedStructure" && !token.MarksInWording));
        Assert.AreEqual("cbfcd67166567accff8314ca0cf6cd4e6f3338209607ca45ae9845671845319a", thirteenth.IdentitySha256);
        Assert.AreEqual(Article3Identity(thirteenth), thirteenth.IdentitySha256);
    }

    /// <summary>
    /// A code's deleted wording, an unmarked note and an underline (<c>code/consommation/20220101</c>): the deleted wording
    /// and the references inside it are never searchable and emit no <c>cites</c> row; the note reference the publisher
    /// wrote without a marker binds its one note; the underlined number keeps its mark.
    /// </summary>
    [TestMethod]
    public async Task ExactRetainedCodeArticlesReadDeletedWordingAnUnmarkedNoteAndAnUnderline()
    {
        var run = await RunAsync(new LuxembourgHeldPopulationBuilder.Member(
            CodeRoot, Excerpt(CodeExcerpt, CodeExcerptSha256), LuxembourgUserFormatToken.Xml));
        var two = await new LuxembourgAknLegalContentProfile2Producer(run.Store).RunAsync(run.Inventory, CancellationToken.None);
        CollectionAssert.AreEqual(
            new[]
            {
                "note reference is not empty or lacks bounded publisher evidence", "unsupported legal-content element: del",
                "unsupported legal-content element: del", "unsupported legal-content element: u",
            },
            two.Outcomes.Select(static outcome => outcome.Detail).ToArray(),
            "what /2 makes of the same articles");

        // art_l_221-1: a note reference without a marker, bound to its one note by id.
        var noted = Admitted(run.Content, "art_l_221-1");
        var note = noted.Tokens.Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.NoteReference);
        Assert.IsTrue(note.Unmarked);
        Assert.IsNull(note.Marker);
        Assert.AreEqual("#note_3_3_0", note.Target);
        CollectionAssert.AreEqual(new[] { "Inséré par la ", "loi du 2 avril 2014", "." }, note.NoteBody!.Select(static token => token.Text).ToArray());
        Assert.AreEqual("2018-07-01", noted.Coordinate.PublisherApplicability);
        Assert.AreEqual("7d6d3caade496e2ca3cd58a64c74520ef3745f80095f7af76fb458bae8410285", noted.IdentitySha256);

        // art_l_226-23: deleted wording between two runs of wording in force.
        var deleted = Admitted(run.Content, "art_l_226-23");
        Assert.AreEqual(
            "du ministre ayant la Place financière dans ses attributions",
            deleted.Tokens.Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.DeletedText).Text);
        StringAssert.Contains(SearchableText(deleted.Tokens), "sans être en possession d’un agrément écrit de la CSSF.");
        Assert.IsFalse(SearchableText(deleted.Tokens).Contains("Place financière", StringComparison.Ordinal));
        Assert.IsNotEmpty(deleted.IndexTerms!);
        Assert.AreEqual("0a53e99c23e5129c9afb083eac18d6862f19192d0bdba353c0f16e4e592ab21c", deleted.IdentitySha256);

        // art_l_311-2: two references inside the deleted wording are deleted wording that keeps its targets, never a reference.
        var withReferences = Admitted(run.Content, "art_l_311-2");
        var deletedReferences = withReferences.Tokens
            .Where(static token => token.Kind == LuxembourgAknLegalContentTokenKind.DeletedText && token.Target is not null)
            .Select(static token => (token.Text, token.Target)).ToArray();
        CollectionAssert.AreEqual(
            new[] { ("Règlement 2017/2394", "/eli/reg_ue/2017/2394/jo"), ("Règlement (CE) N° 2006/2004", "/eli/reg_ue/2004/2006/jo") },
            deletedReferences);
        var relations = LuxembourgIndexBuilder.ProjectRelations([Row(withReferences)]);
        Assert.IsFalse(relations.Any(static row => row.Href == "/eli/reg_ue/2004/2006/jo"), "a deleted reference emits no cites row");
        Assert.AreEqual(
            withReferences.Tokens.Count(static token => token.Kind == LuxembourgAknLegalContentTokenKind.Reference),
            relations.Length,
            "every reference in force, and nothing else, is a cites row");
        Assert.IsFalse(SearchableText(withReferences.Tokens).Contains("relatif \u00e0 la coop\u00e9ration en mati\u00e8re de protection", StringComparison.Ordinal));
        Assert.AreEqual("1ef0ed96b2a4ac9fcc1d89c3ad807602b4955e8d1fe33afa03b782841c334f09", withReferences.IdentitySha256);

        // art_r302-1: the underlined number outside any amending structure keeps its mark, and is wording in force.
        var number = Admitted(run.Content, "art_r302-1");
        Assert.IsTrue(number.Tokens[0].Underline);
        Assert.AreEqual("Art. R.302-1", number.Tokens[0].Text);
        Assert.IsTrue(number.Tokens.Skip(1).All(static token => !token.Underline));
        StringAssert.StartsWith(SearchableText(number.Tokens), "Art. R.302-1Le procès-verbal");
        Assert.AreEqual("444778bceab16bd687be46e97739b63048557a9d0f4ce9c1dbb87fc685ddb46d", number.IdentitySha256);
        foreach (var article in new[] { noted, deleted, withReferences, number })
        {
            Assert.AreEqual(Article3Identity(article), article.IdentitySha256, article.Coordinate.PublisherId);
        }
    }

    // ---- Each /3 shape, synthetic.

    /// <summary>A consolidation's deleted wording, in the publisher's shape: deleted, never searchable, in the wording digest.</summary>
    [TestMethod]
    public async Task AConsolidationsDeletedWordingIsInTheWordingDigestAndNeverInTheSearchableText()
    {
        var deleted = await AdmittedAsync(Article("art_1",
            "<p>Le texte <del>supprimé synthétique et <ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">la loi</ref></del>maintenu.</p>"));
        var without = await AdmittedAsync(Article("art_1", "<p>Le texte maintenu.</p>"));
        var other = await AdmittedAsync(Article("art_1", "<p>Le texte <del>autrement supprimé</del>maintenu.</p>"));

        CollectionAssert.AreEqual(
            new[] { "text", "deleted_text", "deleted_text", "text" },
            deleted.Tokens.Skip(1).Select(static token => Lex.V3.Contracts.ContractWire.NameOf(token.Kind)).ToArray());
        Assert.AreEqual("/eli/etat/leg/loi/2001/01/01/n1/jo", deleted.Tokens[3].Target);
        Assert.AreEqual(SearchableText(without.Tokens), SearchableText(deleted.Tokens));
        Assert.AreEqual(SearchableText(without.Tokens), SearchableText(other.Tokens));
        var digests = new[] { deleted, without, other }
            .Select(static article => LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(article.Tokens)))
            .ToArray();
        Assert.AreEqual(3, digests.Distinct().Count(), "deleted wording is in the wording digest");
        Assert.IsEmpty(LuxembourgIndexBuilder.ProjectRelations([Row(deleted)]), "a reference inside deleted wording is never cited");
    }

    /// <summary>The struck span of a consolidation (the design's <c>loi/2012/03/21/n1/consolide/20220614</c>: "et/ou" with a struck "et/"), synthetic.</summary>
    [TestMethod]
    public async Task AStruckSpanIsDeletedWordingAsDelIs()
    {
        var struck = await AdmittedAsync(Article("art_1", "<p>Le juge <span class=\"strike\">et/</span>ou le greffier synthétique.</p>"));
        var del = await AdmittedAsync(Article("art_1", "<p>Le juge <del>et/</del>ou le greffier synthétique.</p>"));

        Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(del.Tokens), LuxembourgIndexBuilder.TokensJson(struck.Tokens));
        Assert.AreEqual("Art. 1.Le juge ou le greffier synthétique.", SearchableText(struck.Tokens));
        Assert.AreEqual("et/", struck.Tokens.Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.DeletedText).Text);
    }

    [TestMethod]
    public async Task QuotedWordingIsBetweenQuotationTokensAndIsTheActsOwnSearchableAndCitedWording()
    {
        var article = await AdmittedAsync(Article("art_1",
            "<p>Le terme <embeddedText>synthétique <ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">loi</ref></embeddedText> est remplacé. " +
            "L’article 9 est remplacé comme suit :<embeddedStructure startQuote=\"«\u00a0\"><article><num>Art. 9.</num><heading>Titre synthétique</heading>" +
            "<paragraph><num>(1)</num><content><p>Texte synthétique cité.</p></content></paragraph></article></embeddedStructure></p>" +
            "<p><quotedStructure><chapter><num>Chapitre 1er</num><section><subsection><alinea><content><p>Structure citée.</p></content></alinea>" +
            "</subsection></section></chapter></quotedStructure><quotedText>texte cité</quotedText></p>"));

        var kinds = article.Tokens.Select(static token => Lex.V3.Contracts.ContractWire.NameOf(token.Kind)).ToArray();
        Assert.AreEqual(4, kinds.Count(static kind => kind == "quotation_start"));
        Assert.AreEqual(4, kinds.Count(static kind => kind == "quotation_end"));
        CollectionAssert.AreEqual(
            new[] { "embeddedText", "embeddedText", "embeddedStructure", "embeddedStructure", "quotedStructure", "quotedStructure", "quotedText", "quotedText" },
            article.Tokens.Where(static token => token.Quotation is not null).Select(static token => token.Quotation).ToArray());
        Assert.AreEqual("«\u00a0", article.Tokens.Single(static token =>
            token.Kind == LuxembourgAknLegalContentTokenKind.QuotationStart && token.Quotation == "embeddedStructure").Marker);
        var searchable = SearchableText(article.Tokens);
        foreach (var quoted in new[] { "synthétique loi", "Art. 9.Titre synthétique(1)Texte synthétique cité.", "Chapitre 1erStructure citée.", "texte cité" })
        {
            StringAssert.Contains(searchable, quoted);
        }
        Assert.HasCount(1, LuxembourgIndexBuilder.ProjectRelations([Row(article)]), "a reference in quoted wording is the act's own and cited");
        Assert.AreEqual(Article3Identity(article), article.IdentitySha256);
    }

    [TestMethod]
    [DataRow("chapter", DisplayName = "chapter")]
    [DataRow("book", DisplayName = "book")]
    [DataRow("section", DisplayName = "section")]
    public async Task NestedHierarchyIsAContainerOnlyInsideAQuotation(string element)
    {
        var outside = await OutcomeAsync(Article("art_1", $"<p>Texte</p></content><{element}><content><p>synthétique</p></content></{element}><content><p>fin</p>"));
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outside.Disposition);
        Assert.AreEqual("unsupported legal-content element: " + element, outside.Detail);

        var inside = await AdmittedAsync(Article("art_1", $"<p>Texte<embeddedStructure><{element}><content><p>synthétique</p></content></{element}></embeddedStructure></p>"));
        Assert.AreEqual("Art. 1.Textesynthétique", SearchableText(inside.Tokens));
    }

    [TestMethod]
    [DataRow("<embeddedStructure><p><u>3°</u> texte synthétique</p></embeddedStructure>", DisplayName = "u in embeddedStructure")]
    [DataRow("<embeddedText>texte <u>synthétique</u></embeddedText>", DisplayName = "u in embeddedText")]
    [DataRow("<quotedStructure><p><b><u>Art. 2.</u></b></p></quotedStructure>", DisplayName = "u in quotedStructure")]
    [DataRow("<quotedText><u>texte</u></quotedText>", DisplayName = "u in quotedText")]
    public async Task UnderlineInsideAQuotedStructureStaysATypedGap(string content)
    {
        var outcome = await OutcomeAsync(Article("art_1", "<p>Texte " + content + "</p>"));
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outcome.Disposition);
        Assert.AreEqual("underline inside quoted or amending wording: u", outcome.Detail);
    }

    [TestMethod]
    public async Task UnderlineOutsideAmendingStructuresMarksItsWording()
    {
        var article = await AdmittedAsync(Article("art_1",
            "<p>Texte <u>souligné <i>synthétique</i> <ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">loi</ref></u> fin<u><del>supprimé</del></u></p>"));
        var plain = await AdmittedAsync(Article("art_1",
            "<p>Texte <b>souligné <i>synthétique</i> <ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">loi</ref></b> fin<b><del>supprimé</del></b></p>"));

        CollectionAssert.AreEqual(
            new[] { false, false, true, true, true, false, true },
            article.Tokens.Select(static token => token.Underline).ToArray());
        Assert.AreEqual(SearchableText(plain.Tokens), SearchableText(article.Tokens));
        Assert.AreEqual(
            LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(plain.Tokens)),
            LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(article.Tokens)),
            "the underline is a mark, not a word");
        Assert.AreNotEqual(plain.IdentitySha256, article.IdentitySha256, "the identity covers the mark");
        StringAssert.Contains(LuxembourgIndexBuilder.TokensJson(article.Tokens), "\"underline\":true");
    }

    [TestMethod]
    [DataRow("s3")]
    [DataRow("s2")]
    [DataRow("li_symbol")]
    [DataRow("h2")]
    public async Task ASpanOfAnyOtherClassStaysATypedGapUntilReviewed(string spanClass)
    {
        var outcome = await OutcomeAsync(Article("art_1", $"<p>Texte <span class=\"{spanClass}\">synthétique</span></p>"));
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outcome.Disposition);
        Assert.AreEqual("span of an unreviewed class: " + spanClass, outcome.Detail);
    }

    [TestMethod]
    public async Task TheInlineContainersAndASpanWithoutAClassReadAsTheirWording()
    {
        var article = await AdmittedAsync(Article("art_1",
            "<p>H<sub>2</sub>O, <def>terme</def>, <abbr>CSSF</abbr>, <em>accent</em>, <term>notion</term>, <inline name=\"x\">ligne</inline>, " +
            "<span style=\"li_symbol\">a)</span> <span scl:leader=\"dot\">....</span> fin.</p>"));
        Assert.AreEqual("Art. 1.H2O, terme, CSSF, accent, notion, ligne, a).... fin.", SearchableText(article.Tokens),
            "white space alone between two elements is discarded, as under /1");
        Assert.IsTrue(article.Tokens.All(static token => token.Kind == LuxembourgAknLegalContentTokenKind.Text && !token.Underline));
    }

    /// <summary>The separator rule: a layout marker makes no token, and one space stands at it only where two words would fuse.</summary>
    [TestMethod]
    [DataRow("<p>Texte<br/>synthétique</p>", "Art. 1.Texte synthétique", 1, DisplayName = "br between two words")]
    [DataRow("<p>Texte <br/>synthétique</p>", "Art. 1.Texte synthétique", 0, DisplayName = "white space before")]
    [DataRow("<p>Texte<eop/> synthétique</p>", "Art. 1.Texte synthétique", 0, DisplayName = "white space after")]
    [DataRow("<p>Texte<eol/><eop/><br/>synthétique</p>", "Art. 1.Texte synthétique", 1, DisplayName = "three markers, one space")]
    [DataRow("<p><eop/>Texte synthétique<br/></p>", "Art. 1. Texte synthétique", 1, DisplayName = "after the number, and at the end")]
    [DataRow("<p>Texte<br/><b>synthétique</b></p>", "Art. 1.Texte synthétique", 1, DisplayName = "before a container")]
    [DataRow("<p>Texte<br/><ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">loi</ref></p>", "Art. 1.Texte loi", 1, DisplayName = "before a reference")]
    [DataRow("<p>Texte<br/><embeddedText>cité</embeddedText></p>", "Art. 1.Texte cité", 1, DisplayName = "before a quotation")]
    public async Task ALayoutMarkerMakesNoTokenAndWordsNeverFuse(string content, string searchable, int separators)
    {
        var article = await AdmittedAsync(Article("art_1", content));
        Assert.AreEqual(searchable, SearchableText(article.Tokens));
        Assert.AreEqual(separators, article.Tokens.Count(static token => token.Text == " "));
    }

    [TestMethod]
    public async Task ALayoutMarkerInsideDeletedWordingSeparatesDeletedWording()
    {
        var article = await AdmittedAsync(Article("art_1", "<p>avant<del>supprimé<br/>aussi</del>après</p>"));
        CollectionAssert.AreEqual(
            new[] { "supprimé", " ", "aussi" },
            article.Tokens.Where(static token => token.Kind == LuxembourgAknLegalContentTokenKind.DeletedText).Select(static token => token.Text).ToArray());
        Assert.AreEqual("Art. 1.avantaprès", SearchableText(article.Tokens), "the wording in force is the publisher's, with no space it did not write");

        var notEmpty = await OutcomeAsync(Article("art_1", "<p>Texte<br>x</br></p>"));
        Assert.AreEqual("layout marker is not empty: br", notEmpty.Detail);
    }

    [TestMethod]
    public async Task ANoteReferenceWithoutAMarkerBindsExactlyOneNoteAndIsFlaggedUnmarked()
    {
        const string notes = "<note id=\"n1\"><p>Note synthétique.</p></note><note id=\"n2\" marker=\"**\"><p>Autre note.</p></note>" +
            "<note id=\"d\"><p>un</p></note><note id=\"d\"><p>deux</p></note>";
        var unmarked = await AdmittedAsync(Article("art_1", "<p>Texte<noteRef href=\"#n1\"/> fin</p>"), notes);
        var token = unmarked.Tokens.Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.NoteReference);
        Assert.IsTrue(token.Unmarked);
        Assert.IsNull(token.Marker);
        StringAssert.Contains(LuxembourgIndexBuilder.TokensJson(unmarked.Tokens), "\"unmarked\":true");

        // A note with its own marker still binds by id when the reference carries none; a marked reference is /1's rule.
        Assert.IsTrue((await AdmittedAsync(Article("art_1", "<p>Texte<noteRef href=\"#n2\"/></p>"), notes)).Tokens
            .Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.NoteReference).Unmarked);
        var marked = await AdmittedAsync(Article("art_1", "<p>Texte<noteRef href=\"#n2\" marker=\"**\"/></p>"), notes);
        Assert.IsFalse(marked.Tokens.Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.NoteReference).Unmarked);

        foreach (var (content, detail) in new[]
                 {
                     ("<p>Texte<noteRef href=\"#absent\"/></p>", "unmarked note reference does not bind exactly one publisher note"),
                     ("<p>Texte<noteRef href=\"#d\"/></p>", "unmarked note reference does not bind exactly one publisher note"),
                     ("<p>Texte<noteRef href=\"#n2\" marker=\"*\"/></p>", "note reference does not bind exactly one publisher note with the same marker"),
                     ("<p>Texte<noteRef href=\"#n1\">x</noteRef></p>", "note reference is not empty or lacks bounded publisher evidence"),
                     ("<p>Texte<noteRef href=\"n1\"/></p>", "note reference is not empty or lacks bounded publisher evidence"),
                     ("<p>Texte<noteRef href=\"#n1\" marker=\"\"/></p>", "note reference is not empty or lacks bounded publisher evidence"),
                 })
        {
            var outcome = await OutcomeAsync(Article("art_1", content), notes);
            Assert.AreEqual(detail, outcome.Detail, content);
        }
    }

    [TestMethod]
    [DataRow("<note id=\"n1\"><p>Texte <del>supprimé</del></p></note>", "publisher note bodies hold no quotation or deleted wording: del")]
    [DataRow("<note id=\"n1\"><p>Texte <span class=\"strike\">x</span></p></note>", "publisher note bodies hold no quotation or deleted wording: span")]
    [DataRow("<note id=\"n1\"><p>Texte <embeddedText>cité</embeddedText></p></note>", "publisher note bodies hold no quotation or deleted wording: embeddedText")]
    [DataRow("<note id=\"n1\"><p>Texte<noteRef href=\"#n1\"/></p></note>", "publisher note bodies cannot recursively reference notes")]
    public async Task ANoteBodyHoldsWordingInForceOnly(string note, string detail)
    {
        var outcome = await OutcomeAsync(Article("art_1", "<p>Texte<noteRef href=\"#n1\"/></p>"), note);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outcome.Disposition);
        Assert.AreEqual(detail, outcome.Detail);
    }

    [TestMethod]
    public async Task ANoteBodyKeepsUnderlineAndLayout()
    {
        var article = await AdmittedAsync(Article("art_1", "<p>Texte<noteRef href=\"#n1\"/></p>"),
            "<note id=\"n1\"><p><u>Loi</u> du<br/>1er janvier</p></note>");
        var body = article.Tokens.Single(static token => token.Kind == LuxembourgAknLegalContentTokenKind.NoteReference).NoteBody!;
        CollectionAssert.AreEqual(new[] { "Loi", " du", " ", "1er janvier" }, body.Select(static token => token.Text).ToArray());
        Assert.IsTrue(body[0].Underline);
        StringAssert.Contains(LuxembourgIndexBuilder.TokensJson(article.Tokens), "\"note_body\":[{\"kind\":\"text\",\"text\":\"Loi\",\"target\":null,\"marker\":null,\"underline\":true}");
    }

    [TestMethod]
    public async Task DeletedWordingHoldsInlineWordingOnly()
    {
        var admitted = await AdmittedAsync(Article("art_1",
            "<p>Texte <del>a <b>b</b> <i>c</i> <sup>d</sup> <sub>e</sub> <span>f</span> <u>g</u> <del>h</del> <span class=\"strike\">i</span>" +
            "<mod class=\"mod-start\" for=\"#m1\"/>j<mod class=\"mod-end\" for=\"#m1\"/><noteRef href=\"#n1\" marker=\"1\"/><eop/>k</del> fin</p>"),
            "<note id=\"n1\" marker=\"1\"><p>Supprimé par la loi.</p></note>");
        Assert.AreEqual("Art. 1.Texte  fin", SearchableText(admitted.Tokens));
        Assert.IsTrue(admitted.Tokens.Any(static token => token.Kind == LuxembourgAknLegalContentTokenKind.ModificationStart));
        Assert.IsTrue(admitted.Tokens.Any(static token => token.Kind == LuxembourgAknLegalContentTokenKind.NoteReference));

        foreach (var (content, detail) in new[]
                 {
                     ("<del><p>paragraphe</p></del>", "unsupported content inside deleted wording: p"),
                     ("<del><embeddedText>cité</embeddedText></del>", "unsupported content inside deleted wording: embeddedText"),
                     ("<del><table/></del>", "unsupported content inside deleted wording: table"),
                     ("<span class=\"strike\"><num>1</num></span>", "unsupported content inside deleted wording: num"),
                     ("<del class=\"x\">texte</del>", "deleted wording of an unreviewed class: x"),
                 })
        {
            var outcome = await OutcomeAsync(Article("art_1", "<p>Texte " + content + "</p>"));
            Assert.AreEqual(detail, outcome.Detail, content);
        }
    }

    [TestMethod]
    public async Task AQuotationsMarksAreThePublishersOrAbsent()
    {
        foreach (var (attributes, inWording) in new[] { ("", false), (" scl:hide-quote=\"false\"", false), (" scl:hide-quote=\"true\"", true) })
        {
            var article = await AdmittedAsync(Article("art_1", $"<p>Texte <embeddedStructure{attributes}><p>cité</p></embeddedStructure></p>"));
            Assert.IsTrue(article.Tokens.Where(static token => token.Quotation is not null).All(token => token.MarksInWording == inWording), attributes);
        }

        foreach (var (content, detail) in new[]
                 {
                     ("<embeddedText scl:hide-quote=\"maybe\">x</embeddedText>", "quotation carries an unreviewed scl:hide-quote value"),
                     ("<embeddedText startQuote=\"\">x</embeddedText>", "quotation mark is not a bounded publisher mark"),
                     ("<embeddedText endQuote=\"" + new string('»', 65) + "\">x</embeddedText>", "quotation mark is not a bounded publisher mark"),
                 })
        {
            Assert.AreEqual(detail, (await OutcomeAsync(Article("art_1", "<p>Texte " + content + "</p>"))).Detail, content);
        }
    }

    /// <summary>The design's still-typed gaps under /3, each with its detail.</summary>
    [TestMethod]
    public async Task TheStillTypedGapsStayTypedGaps()
    {
        var gaps = new (string Name, string Content, string Detail)[]
        {
            ("table", "<table><tr><td><p>Texte synthétique</p></td></tr></table>", "unsupported legal-content element: table"),
            ("quotedtable", "<p><embeddedStructure><table><tr><td><p>x</p></td></tr></table></embeddedStructure></p>", "unsupported legal-content element: table"),
            ("img", "<p>Texte<img src=\"x.png\"/></p>", "unsupported legal-content element: img"),
            ("math", "<p>Texte<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mi>x</mi></math></p>", Namespace0),
            ("pi", "<p>Texte<?pi x?></p>", Namespace0),
            ("modnotempty", "<p><mod for=\"#pm1\"><quotedText>Texte</quotedText></mod></p>", "modification boundary is not empty or lacks a bounded publisher target"),
            ("docnumber", "<p>la loi n° <docNumber>1234</docNumber></p>", "unsupported legal-content element: docNumber"),
            ("embeddeddoc", "<p><embeddedStructure><doc name=\"x\"><mainBody><p>x</p></mainBody></doc></embeddedStructure></p>", "unsupported legal-content element: doc"),
            ("reflabel", "<p><ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\"><u>la loi</u></ref></p>", "reference contains an unsupported publisher label shape"),
            ("unclosed", "<p>Texte<mod class=\"mod-start\" for=\"#pm1\"/></p>", "article ends with an unclosed modification span: #pm1"),
            ("nowording", "<p> <br/> </p>", "article contains no publisher legal wording or marker evidence"),
        };
        var articles = new StringBuilder();
        foreach (var (name, content, _) in gaps)
        {
            articles.Append("<article id=\"art_").Append(name).Append("\"><content>").Append(content).Append("</content></article>");
        }
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(articles.ToString())));
        foreach (var (name, _, detail) in gaps)
        {
            var outcome = Outcome(run.Content, "art_" + name);
            Assert.AreEqual(LuxembourgAknLegalContentDisposition.UnsupportedContentShape, outcome.Disposition, name);
            Assert.AreEqual(detail, outcome.Detail, name);
            Assert.IsNull(outcome.Article, name);
        }
    }

    /// <summary>Every shape /2 admits reads under /3 to the same stored tokens, so what /2 serves /3 serves byte for byte.</summary>
    [TestMethod]
    public async Task EveryShapeContent2AdmitsReadsToTheSameStoredTokens()
    {
        var admitted = new (string Name, string Content)[]
        {
            ("plain", "<p>Texte synthétique.</p>"),
            ("ref", "<p>voir <ref href=\"/eli/etat/leg/loi/2001/01/01/n1/jo\">la <b>loi</b></ref> ensuite</p>"),
            ("mod", "<p>voir <mod class=\"mod-start\" for=\"#pm1\"/>ensuite<mod class=\"mod-end\" for=\"#pm1\"/></p>"),
            ("note", "<p>voir<noteRef href=\"#n2\" marker=\"2\"/></p>"),
            ("markeronly", "<p><mod class=\"mod-start\" for=\"#pm1\"/><mod class=\"mod-end\" for=\"#pm1\"/></p>"),
            ("lists", "<p>a</p><ol><li>un</li><li><b>deux</b> <i>trois</i><sup>e</sup></li></ol><ul><li>x</li></ul>"),
        };
        const string terms = "<scl:indexterms><scl:indexterm><scl:label>Terme synthétique</scl:label></scl:indexterm></scl:indexterms>";
        var articles = new StringBuilder();
        foreach (var (name, content) in admitted)
        {
            articles.Append("<article id=\"art_").Append(name).Append("\">").Append(terms).Append("<num>Art.</num><paragraph><alinea><content>")
                .Append(content).Append("</content></alinea></paragraph></article>");
        }
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            articles.ToString(), "<note id=\"n2\" marker=\"2\"><p>Note synthétique.</p></note>")));
        var two = await new LuxembourgAknLegalContentProfile2Producer(run.Store).RunAsync(run.Inventory, CancellationToken.None);

        foreach (var (name, _) in admitted)
        {
            var three = Outcome(run.Content, "art_" + name);
            var second = Outcome(two, "art_" + name);
            Assert.AreEqual(second.Disposition, three.Disposition, name);
            Assert.AreEqual(LuxembourgIndexBuilder.TokensJson(second.Article!.Tokens), LuxembourgIndexBuilder.TokensJson(three.Article!.Tokens), name);
            Assert.AreEqual(
                LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(second.Article.Tokens)),
                LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(three.Article.Tokens)), name);
            CollectionAssert.AreEqual(new[] { "Terme synthétique" }, three.Article.IndexTerms!.Select(static term => term.Label).ToArray(), name);
            Assert.IsFalse(three.Article.Tokens.Any(static token => token.UsesContent3), name);
        }
    }

    /// <summary>The design (§1.4) names these two /1 tests as true under /2 and /3.</summary>
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

    // ---- The door, the identities and the shared types.

    [TestMethod]
    public async Task Content3ReadsOnlyAnInventory4Population()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><content><p>Texte synthétique.</p></content></article>")));
        var inventory3 = await new LuxembourgAknArticleInventoryProducer(run.Store)
            .RunAsync(run.Inventory.SourcePopulation, CancellationToken.None);
        var otherDigest = new LuxembourgAknArticleInventoryPopulation(run.Inventory.SourcePopulation, [], new string('0', 64));

        foreach (var population in new[] { inventory3, otherDigest })
        {
            var refused = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                new LuxembourgAknLegalContentProfile3Producer(run.Store).RunAsync(population, CancellationToken.None));
            StringAssert.StartsWith(refused.Message, "Content /3 reads only an inventory /4 population.");
        }
    }

    [TestMethod]
    public async Task MissingCustodyIsRetainedBytesUnavailableForEveryArticle()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><content><p>un</p></content></article><article id=\"art_2\"><content><p>deux</p></content></article>")));

        var content = await new LuxembourgAknLegalContentProfile3Producer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(run.Inventory, CancellationToken.None);

        Assert.IsTrue(content.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgAknLegalContentDisposition.RetainedBytesUnavailable && outcome.Article is null));
        Assert.HasCount(2, content.Outcomes);
    }

    [TestMethod]
    public async Task ThePopulationIdentityIsLayoutTwoUnderTheContent3DigestAndStable()
    {
        var body = Akn(
            "<article id=\"art_1\"><content><p>Texte <embeddedText>cité</embeddedText><del>supprimé</del>.</p></content></article>" +
            "<article id=\"art_2\"><content><table/></content></article>");
        var first = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", body));
        var second = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", body));

        Assert.AreEqual(Content3RuleSha256, first.Content.RuleProfileSha256);
        Assert.AreEqual(first.Content.IdentitySha256, second.Content.IdentitySha256);
        Assert.AreEqual(Population3Identity(first.Content), first.Content.IdentitySha256);
        Assert.AreSame(first.Inventory, first.Content.SourceInventoryPopulation);
    }

    [TestMethod]
    public async Task TheSharedTypesKeepContent3sMarksOutOfContent1And2()
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(
            "<article id=\"art_1\"><content><p>Texte <del>supprimé</del></p></content></article>")));
        var plain = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn("<article id=\"art_1\"><content><p>Texte</p></content></article>")));
        var two = await new LuxembourgAknLegalContentProfile2Producer(plain.Store).RunAsync(plain.Inventory, CancellationToken.None);
        Assert.IsNotNull(two.Outcomes.Single().Article);

        // A /3 population holds /3 articles only; a /2 digest over /3 articles, or /3's over /2's, is refused.
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgAknLegalContentPopulation(run.Inventory, run.Content.Outcomes, LuxembourgAknLegalContentProfile2ProducerTests.Content2RuleSha256));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgAknLegalContentPopulation(two.SourceInventoryPopulation, two.Outcomes, Content3RuleSha256));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgAknLegalContentPopulation(run.Inventory, run.Content.Outcomes));
        Assert.AreEqual(run.Content.IdentitySha256,
            new LuxembourgAknLegalContentPopulation(run.Inventory, run.Content.Outcomes, Content3RuleSha256).IdentitySha256);

        // The token marks are /3's: a /1 or /2 article refuses a token that carries one.
        var deleted = run.Content.Outcomes.Single().Article!;
        Assert.IsTrue(deleted.Tokens.Any(static token => token.UsesContent3));
        foreach (var token in new LuxembourgAknLegalContentToken[]
                 {
                     new(LuxembourgAknLegalContentTokenKind.DeletedText, "x", null, null),
                     new(LuxembourgAknLegalContentTokenKind.QuotationStart, null, null, null),
                     new(LuxembourgAknLegalContentTokenKind.Text, "x", null, null) { Underline = true },
                     new(LuxembourgAknLegalContentTokenKind.NoteReference, null, "#n", null,
                         [new(LuxembourgAknLegalContentTokenKind.Text, "x", null, null) { Underline = true }]),
                 })
        {
            Assert.IsTrue(token.UsesContent3);
        }
        Assert.IsFalse(new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Text, "x", null, null).UsesContent3);
    }

    /// <summary>
    /// What the index stores and digests for a /1 or /2 stream stays byte-identical: the stored form of a token without a /3
    /// mark is the old one, and the wording digest of a stream without /3 kinds is the old algorithm's.
    /// </summary>
    [TestMethod]
    public void TheStoredFormAndWordingDigestOfAContent2StreamAreUnchanged()
    {
        var tokens = new LuxembourgAknLegalContentToken[]
        {
            new(LuxembourgAknLegalContentTokenKind.Text, "Art. 1.", null, null),
            new(LuxembourgAknLegalContentTokenKind.Reference, "loi", "/eli/x", null),
            new(LuxembourgAknLegalContentTokenKind.NoteReference, null, "#n1", "1",
                [new(LuxembourgAknLegalContentTokenKind.Text, "Note é", null, null)]),
            new(LuxembourgAknLegalContentTokenKind.ModificationStart, null, "#m", null),
            new(LuxembourgAknLegalContentTokenKind.Text, " fin", null, null),
        };
        const string expected =
            "[{\"kind\":\"text\",\"text\":\"Art. 1.\",\"target\":null,\"marker\":null,\"note_body\":null}," +
            "{\"kind\":\"reference\",\"text\":\"loi\",\"target\":\"/eli/x\",\"marker\":null,\"note_body\":null}," +
            "{\"kind\":\"note_reference\",\"text\":null,\"target\":\"#n1\",\"marker\":\"1\",\"note_body\":[{\"kind\":\"text\",\"text\":\"Note \\u00E9\",\"target\":null,\"marker\":null}]}," +
            "{\"kind\":\"modification_start\",\"text\":null,\"target\":\"#m\",\"marker\":null,\"note_body\":null}," +
            "{\"kind\":\"text\",\"text\":\" fin\",\"target\":null,\"marker\":null,\"note_body\":null}]";
        var stored = LuxembourgIndexBuilder.TokensJson(tokens);
        Assert.AreEqual(expected, stored);

        // The old wording digest: [kind, text, target] of text and references, consecutive text merged across markers and notes.
        var old = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new List<string?[]>
        {
            new[] { "text", "Art. 1.", null },
            new[] { "reference", "loi", "/eli/x" },
            new[] { "text", " fin", null },
        })));
        Assert.AreEqual(old, LuxembourgIndexReader.WordingSha256(stored));
    }

    [TestMethod]
    public void TheWordingDigestTakesInDeletedWordingAndQuotationBoundaries()
    {
        static string Digest(params LuxembourgAknLegalContentToken[] tokens) =>
            LuxembourgIndexReader.WordingSha256(LuxembourgIndexBuilder.TokensJson(tokens));

        var text = new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Text, "a", null, null);
        var start = new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.QuotationStart, null, null, null) { Quotation = "embeddedText" };
        var end = new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.QuotationEnd, null, null, null) { Quotation = "embeddedText" };
        var deleted = new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.DeletedText, "b", null, null);
        var deletedReference = new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.DeletedText, "b", "/eli/x", null);

        Assert.AreNotEqual(Digest(text), Digest(start, text, end), "a quotation boundary is in the digest");
        Assert.AreNotEqual(Digest(text), Digest(text, deleted), "deleted wording is in the digest");
        Assert.AreNotEqual(Digest(text, deleted), Digest(text, deletedReference), "a deleted reference keeps its target");
        Assert.AreEqual(Digest(text, deleted, deleted), Digest(text,
            new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.DeletedText, "bb", null, null)),
            "consecutive deleted text merges as text does");
        Assert.AreNotEqual(Digest(text, text), Digest(text, deleted with { Text = "a" }), "deleted wording is never wording in force");
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
        var content = await new LuxembourgAknLegalContentProfile3Producer(store)
            .RunAsync(inventory, CancellationToken.None);
        return new Run(store, inventory, content);
    }

    private static string Article(string id, string content) =>
        $"<article id=\"{id}\"><num>Art. 1.</num><content>{content}</content></article>";

    private static async Task<LuxembourgAknLegalContentOutcome> OutcomeAsync(string articles, string? afterArticles = null)
    {
        var run = await RunAsync(Member(Eli + "rgd/2001/02/03/n9/jo", Akn(articles, afterArticles)));
        return run.Content.Outcomes.Single();
    }

    private static async Task<LuxembourgAknLegalContentArticle> AdmittedAsync(string articles, string? afterArticles = null)
    {
        var outcome = await OutcomeAsync(articles, afterArticles);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, outcome.Disposition, outcome.Detail);
        return outcome.Article!;
    }

    private static LuxembourgAknLegalContentArticle Admitted(LuxembourgAknLegalContentPopulation population, string id)
    {
        var outcome = Outcome(population, id);
        Assert.AreEqual(LuxembourgAknLegalContentDisposition.Admitted, outcome.Disposition, id + ": " + outcome.Detail);
        return outcome.Article!;
    }

    private static LuxembourgHeldPopulationBuilder.Member Member(string root, byte[] body) =>
        new(root, body, LuxembourgUserFormatToken.XmlAkomaNtoso);

    private static LuxembourgAknLegalContentOutcome Outcome(LuxembourgAknLegalContentPopulation population, string id) =>
        population.Outcomes.Single(outcome => outcome.Coordinate?.PublisherId == id);

    /// <summary><c>articles.searchable_text</c> as the index builder projects it: the text and reference tokens, concatenated.</summary>
    private static string SearchableText(IReadOnlyList<LuxembourgAknLegalContentToken> tokens) =>
        string.Concat(tokens
            .Where(static token => token.Kind is LuxembourgAknLegalContentTokenKind.Text or LuxembourgAknLegalContentTokenKind.Reference)
            .Select(static token => token.Text));

    /// <summary>The wording inside each outermost quotation, in order.</summary>
    private static string[] QuotedTexts(IReadOnlyList<LuxembourgAknLegalContentToken> tokens)
    {
        var quoted = new List<string>();
        var depth = 0;
        var current = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token.Kind == LuxembourgAknLegalContentTokenKind.QuotationStart)
            {
                depth++;
                continue;
            }
            if (token.Kind == LuxembourgAknLegalContentTokenKind.QuotationEnd)
            {
                if (--depth == 0)
                {
                    quoted.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            if (depth > 0 && token.Kind is LuxembourgAknLegalContentTokenKind.Text or LuxembourgAknLegalContentTokenKind.Reference)
            {
                current.Append(token.Text);
            }
        }
        return quoted.ToArray();
    }

    private static LuxembourgIndexBuilder.ArticleRow Row(LuxembourgAknLegalContentArticle article) =>
        new(article.IdentitySha256, new string('1', 64), "https://example.invalid/expression", article.Coordinate.PublisherId,
            null, null, "fra", new string('4', 64), string.Empty, LuxembourgIndexBuilder.TokensJson(article.Tokens));

    private static byte[] Excerpt(string name, string sha256)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", ExcerptDirectory, name));
        Assert.AreEqual(sha256, LuxembourgHeldPopulationBuilder.Sha(bytes), name);
        return bytes;
    }

    /// <summary>A synthetic AKN act with no identification block (inventory /4 reads it as <c>absent</c>).</summary>
    private static byte[] Akn(string articles, string? afterArticles = null) => Encoding.UTF8.GetBytes(
        "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\" " +
        "xmlns:scl=\"http://www.scl.lu\"><act><body>" + articles + (afterArticles ?? "") +
        "</body></act></akomaNtoso>");

    /// <summary>Article identity layout <c>/3</c>, written out by hand from its specification.</summary>
    private static string Article3Identity(LuxembourgAknLegalContentArticle article)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Put(hash, "lex-v3-luxembourg-akn-legal-content-article/3");
        Put(hash, article.PublisherExpressionIri);
        Put(hash, article.Coordinate.PublisherId);
        Put(hash, article.Coordinate.PublisherWId ?? "");
        Put(hash, article.Coordinate.PublisherApplicability ?? "");
        Put(hash, Content3RuleSha256);
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
            Put(hash, token.Underline ? "1" : "0");
            Put(hash, token.Unmarked ? "1" : "0");
            Put(hash, token.Quotation ?? "");
            Put(hash, token.MarksInWording ? "1" : "0");
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

    /// <summary>Population identity layout <c>/2</c> under the /3 digest, written out by hand.</summary>
    private static string Population3Identity(LuxembourgAknLegalContentPopulation population)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Put(hash, "lex-v3-luxembourg-akn-legal-content-population/2");
        Put(hash, Content3RuleSha256);
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
