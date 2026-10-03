using System.Net;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The image-only annex control case (the launch contract's annex line): an EU act whose annex the publisher's PDF holds only
/// as images is acquired through the Formex package producer, its annex classified against that PDF, and built into the
/// corpus and the EU index the API mounts. The annex is served as text not available, linked to the expression's official
/// source, and kept out of search, quotes and exports.
/// </summary>
public sealed partial class EuFormexPackagePopulationProducerTests
{
    /// <summary>The annex's title in the control case: a word no main body holds, so its absence from every answer is checkable.</summary>
    private const string AnnexSentinel = "ANNEXSENTINELZQXV";

    /// <summary>A word of the 2026/1965 annex's structured text (a sanctions-list entry) that its main body does not hold.</summary>
    private const string AnnexOnlyWord = "Hambali";

    private const string ControlCelex = "32016R0679";

    private const string EuAnnexMountVariable = "V3_WRITE_EU_ANNEX_MOUNT";

    /// <summary>
    /// The control case's acquisition: the real 2026/1965 package and work XHTML, whose one annex holds structured text, with
    /// the annex title rewritten to a sentinel in both (the binder pairs the Formex and XHTML annexes by title), and the
    /// publisher's PDF made image-only: seven pages labelled 1 to 7, each an image with no text layer. The run is over the
    /// GDPR's seed, whose CELEX every EU answer and permalink can carry.
    /// </summary>
    private static async Task<(EuFormexPackagePopulationResult Result, LanguageScopedExpression English, EuAcquisitionTestFixture.EuInMemoryCustodyStore Store)>
        AcquireAnnexControlAsync()
    {
        var package = EuFirstMountAcquisitionTests.RewrittenPackage(
            await FixtureAsync("new-fmx4-200-body.bin"), "<P>ANNEX</P>", $"<P>{AnnexSentinel}</P>");
        var xhtml = ReplacedOnce(await FixtureAsync("new-xhtml-200-body.bin"), ">ANNEX<", $">{AnnexSentinel}<");
        var pdf = EuAnnexEvidenceBinderTests.PageLabelPdf(7, "<< /S /D /St 1 >>", image: true);
        var (result, _, english, store) = await AcquireEnglishAsync(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, package, "application/zip")
                    : request.RequestUri!.AbsolutePath.EndsWith("/DOC_1", StringComparison.Ordinal)
                        ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, pdf, "application/pdf;type=pdfa2a;charset=UTF-8")
                        : null,
            heldXhtml: xhtml,
            englishTypes: ["fmx4", "pdfa2a"],
            seedCelex: ControlCelex);
        return (result, english, store);
    }

    /// <summary>The control case's Stage 3 envelope: the acquisition with its annex classification, ready for the corpus and the EU index.</summary>
    private static Task<Stage3DerivationProfileEnvelope> AnnexControlEnvelopeAsync(
        EuFormexPackagePopulationResult result, EuAcquisitionTestFixture.EuInMemoryCustodyStore store) =>
        LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: result.Reconciliation!.Run,
            formexOverride: result.Reconciliation,
            formexStore: store,
            formexClassifications: Stage3EvidenceEnvelopeTests.CompleteClassifications(result.Reconciliation, result.AnnexClassifications));

    /// <summary>
    /// The control case through the corpus, the EU index and the API: the annex is classified text not available, recorded on
    /// the member the articles are read from, and copied into the EU index; no article holds its text; search finds none of
    /// it; dossier and both evidence_bundle paths list it as text_not_available with the expression's official source; and no
    /// answer carries its text or title.
    /// </summary>
    [TestMethod]
    public async Task TheImageOnlyAnnexControlCaseIsTextNotAvailableOfficiallyLinkedAndOutOfSearchQuotesAndExports()
    {
        var (result, english, store) = await AcquireAnnexControlAsync();

        // Acquisition: the package is acquired with its one annex, classified image-only against the PDF.
        var outcome = result.Reconciliation!.Outcomes.Single(value => value.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.Acquired, outcome.Kind, $"{outcome.NotAcquiredReason}: {outcome.Detail}");
        Assert.AreEqual(AnnexSentinel, outcome.AcquiredInventory!.Members.Single().Title);
        var classification = result.AnnexClassifications.Single();
        var annex = classification.Members.Single();
        Assert.AreEqual(EuAnnexBodyDispositionOutcome.TextNotAvailable, annex.Outcome, annex.ReasonCode);
        Assert.AreEqual("image_only", annex.ReasonCode);

        // Corpus: the member the articles are read from carries the admitted main body and the annex, text not available.
        var envelope = await AnnexControlEnvelopeAsync(result, store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(corpus, $"{refusal}: {detail}");
        var annexOutcome = new LexCorpus6Stage3Outcome(
            LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, annex.SemanticIdentitySha256, LexCorpus6Stage3Disposition.AnnexTextNotAvailable);
        var member = corpus.VerifiedSet.Set.Members.Single(candidate => candidate.Stage3Outcomes.Contains(annexOutcome));
        Assert.IsTrue(member.Stage3Outcomes.Any(static value =>
            value.Domain == LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody
            && value.Disposition == LexCorpus6Stage3Disposition.FormexMainBodyAdmitted));

        // The EU index: the annex outcome copied into the member row, and no article holding the annex's text or title.
        var fixture = await EuropeMountedFixture.FromEnvelopeAsync(envelope);
        await using var cleanup = fixture;
        Assert.AreEqual(english.Identity.PublisherExpressionId, fixture.PublisherExpressionId);
        using (var connection = EuropeIndexBuilder.Open(Path.Combine(fixture.Directory, V3CorpusMount.EuropeIndexFileName), SqliteOpenMode.ReadOnly))
        {
            var row = MountedFixture.ReadEuropeMembers(connection).Single(candidate => candidate.ObjectRefSha256 == member.ObjectRefSha256);
            StringAssert.Contains(row.Stage3OutcomesJson, "annex_text_not_available");
            StringAssert.Contains(row.Stage3OutcomesJson, annex.SemanticIdentitySha256);
            var articles = MountedFixture.ReadEuropeArticles(connection);
            Assert.IsNotEmpty(articles);
            Assert.IsTrue(articles.All(article => article.ObjectRefSha256 == member.ObjectRefSha256), "the articles are read from the member the annex is recorded on");
            foreach (var article in articles)
            {
                AssertHoldsNoAnnex(article.SearchableText + article.TokensJson, $"article {article.PublisherIdentifier}");
            }
        }

        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // Search: the main body is found; the annex's text and title are not.
        var found = await EnvelopeAsync(mount, "/api/v3/search", "search", new
        {
            query = "shall enter into force on the day following that of its publication", language = "eng", identifier = ControlCelex,
        });
        Assert.IsNull(found.Refusal, found.Refusal?.Code);
        Assert.IsNotEmpty(found.Result!.Value.GetProperty("hits").EnumerateArray().ToArray(), "the control mount's main body is searchable");
        foreach (var query in new[] { AnnexOnlyWord, AnnexSentinel })
        {
            var searched = await EnvelopeAsync(mount, "/api/v3/search", "search", new { query, language = "eng", identifier = ControlCelex });
            Assert.IsNull(searched.Refusal, searched.Refusal?.Code);
            Assert.AreEqual(0, searched.Result!.Value.GetProperty("hits").GetArrayLength(), $"{query}: an annex is never searched");
        }

        // Dossier: the expression lists the annex, text not available, linked to the expression's official source.
        var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = ControlCelex });
        Assert.IsNull(dossier.Refusal, dossier.Refusal?.Code);
        var expression = dossier.Result!.Value.GetProperty("expressions").EnumerateArray().Single();
        AssertHoldsNoAnnex(dossier.Result.Value.GetRawText(), "dossier");
        var wordingDate = expression.GetProperty("pinned_wording").GetProperty("wording_date").GetString()!;

        // Quotes, by both evidence_bundle paths: the act's CELEX through the EU time view, the article identity through the
        // original wording. Each quotes the main body only and lists the annex beside it.
        var officialSource = (string?)null;
        foreach (var (identifier, scope) in new[]
                 {
                     (ControlCelex, V3CorpusMount.EuropeTimeEvidenceBundleScope),
                     (fixture.ArticleIdentitySha256, V3CorpusMount.EuropeEvidenceBundleScope),
                 })
        {
            var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier, date = wordingDate, language = "eng" });
            Assert.IsNull(bundle.Refusal, $"{identifier}: {bundle.Refusal?.Code}");
            var value = bundle.Result!.Value;
            Assert.AreEqual(scope, value.GetProperty("scope").GetString(), identifier);
            AssertHoldsNoAnnex(value.GetRawText(), $"evidence_bundle for {identifier}");
            var wording = value.GetProperty("wordings").EnumerateArray().Single();
            Assert.AreEqual(member.ObjectRefSha256, wording.GetProperty("sources").EnumerateArray().Single().GetProperty("object_ref_sha256").GetString());
            var quoted = wording.GetProperty("articles").EnumerateArray().ToArray();
            Assert.IsNotEmpty(quoted, identifier);
            officialSource ??= quoted[0].GetProperty("official_source").GetString();
            AssertAnnexRow(wording, english.Identity.PublisherExpressionId, officialSource!, annex.SemanticIdentitySha256, identifier);
            Assert.AreEqual(V3CorpusMount.EuropeAnnexesNotHeldReason, value.GetProperty("not_held").EnumerateArray()
                .Single(static row => row.GetProperty("item").GetString() == "annexes").GetProperty("reason").GetString());
        }

        AssertAnnexRow(expression, english.Identity.PublisherExpressionId, officialSource!, annex.SemanticIdentitySha256, "dossier");
        Assert.AreEqual(V3CorpusMount.EuropeAnnexesNotHeldReason, dossier.Result.Value.GetProperty("not_held").EnumerateArray()
            .Single(static row => row.GetProperty("item").GetString() == "annexes").GetProperty("reason").GetString());
    }

    /// <summary>
    /// Writes the control case's mount where the journeys ask (<c>V3_WRITE_EU_ANNEX_MOUNT</c>), with a <c>journey-mount.json</c>
    /// naming the act, its wording date, the number of annexes not served as text and the texts no page may show, so CI walks
    /// the EU reading and dossier pages over an image-only annex in a real browser.
    /// </summary>
    [TestMethod]
    public async Task TheEuAnnexJourneyMountIsTheControlCaseWrittenWhereTheJourneyAsks()
    {
        var target = Environment.GetEnvironmentVariable(EuAnnexMountVariable);
        if (string.IsNullOrWhiteSpace(target))
        {
            Assert.Inconclusive($"{EuAnnexMountVariable} names no directory, so no EU annex journey mount is written.");
        }

        var (result, _, store) = await AcquireAnnexControlAsync();
        var fixture = await EuropeMountedFixture.FromEnvelopeAsync(await AnnexControlEnvelopeAsync(result, store));
        await using var cleanup = fixture;
        Directory.CreateDirectory(target);
        foreach (var name in new[] { V3CorpusMount.EuropeIndexFileName, V3CorpusMount.EuropeCapabilityManifestFileName, V3CorpusMount.CorpusFileName })
        {
            File.Copy(Path.Combine(fixture.Directory, name), Path.Combine(target, name), overwrite: true);
        }

        string wordingDate;
        using (var mount = await V3CorpusMount.OpenAsync(target, CancellationToken.None))
        {
            Assert.IsNotNull(mount, "the EU annex directory mounts through the API's own verifier.");
            var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = ControlCelex });
            Assert.IsNull(dossier.Refusal, dossier.Refusal?.Code);
            var expression = dossier.Result!.Value.GetProperty("expressions").EnumerateArray().Single();
            Assert.AreEqual(1, expression.GetProperty("annexes_not_served").EnumerateArray().Single().GetProperty("annexes").GetInt32());
            wordingDate = expression.GetProperty("pinned_wording").GetProperty("wording_date").GetString()!;
        }

        var manifest = JsonSerializer.Serialize(new
        {
            schema = "lex-v3-journey-mount/1",
            note = "the image-only annex control case (THE MOUNT IS A FIXTURE): the 2026/1965 package acquired over the GDPR's seed with its publisher PDF made image-only, written for the journeys that read an EU act whose annex is not served as text",
            corpus_sha256 = fixture.CorpusSha256,
            europe_index_sha256 = fixture.IndexSha256,
            eu_annex = new
            {
                celex = ControlCelex,
                wording_date = wordingDate,
                annexes = 1,
                absent_texts = new[] { AnnexOnlyWord, AnnexSentinel },
            },
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(target, "journey-mount.json"), manifest + "\n");
    }

    /// <summary>The one annex row: image-only, served as text not available, linked to the expression's official source.</summary>
    private static void AssertAnnexRow(JsonElement holder, string expressionId, string officialSource, string annexIdentity, string where)
    {
        var row = holder.GetProperty("annexes_not_served").EnumerateArray().Single();
        Assert.AreEqual("annex_text_not_available", row.GetProperty("disposition").GetString(), where);
        Assert.AreEqual(1, row.GetProperty("annexes").GetInt32(), where);
        CollectionAssert.AreEqual(new[] { annexIdentity },
            row.GetProperty("annex_identities_sha256").EnumerateArray().Select(static value => value.GetString()).ToArray(), where);
        Assert.AreEqual("text_not_available", row.GetProperty("served_as").GetString(), where);
        Assert.AreEqual(expressionId, row.GetProperty("official_identity").GetString(), where);
        Assert.AreEqual(officialSource, row.GetProperty("official_source").GetString(), where);
        Assert.AreEqual(V3CorpusMount.EuropeAnnexReason(LexCorpus6Stage3Disposition.AnnexTextNotAvailable), row.GetProperty("reason").GetString(), where);
    }

    /// <summary>Neither the annex's text nor its title is anywhere in <paramref name="served"/>.</summary>
    private static void AssertHoldsNoAnnex(string served, string where)
    {
        Assert.DoesNotContain(AnnexOnlyWord, served, $"{where} holds the annex's text");
        Assert.DoesNotContain(AnnexSentinel, served, $"{where} holds the annex's title");
    }

    /// <summary><paramref name="bytes"/> with its one occurrence of <paramref name="find"/> replaced, byte for byte elsewhere.</summary>
    private static byte[] ReplacedOnce(byte[] bytes, string find, string replace)
    {
        var target = Encoding.UTF8.GetBytes(find);
        var at = bytes.AsSpan().IndexOf(target);
        Assert.IsGreaterThanOrEqualTo(0, at, $"no {find}");
        Assert.AreEqual(-1, bytes.AsSpan(at + target.Length).IndexOf(target), $"more than one {find}");
        return [.. bytes[..at], .. Encoding.UTF8.GetBytes(replace), .. bytes[(at + target.Length)..]];
    }
}
