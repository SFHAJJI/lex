using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lex.Derive;
using Lex.Index;
using Lex.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lex.Tests;

/// <summary>
/// The Phase 0 trust notices (Decisions 41 and 44): each renders exactly when its typed
/// evidence condition is satisfied, and never states a fact the index does not hold. The sites
/// here use the REAL production trigger identifiers (publisher, work, anchor, derogating act),
/// so the tests exercise the production condition end to end rather than an injected stand-in.
/// </summary>
public sealed class TrustNoticeTests : IDisposable
{
    private const string DerogationHeading = "Temporary derogation recorded";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-trust-notice-{Guid.NewGuid():N}");

    public TrustNoticeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private const string UnknownWorkHeading = "Instrument not found in held records";
    private const string CandidateHeading = "Possible held records";

    /// <summary>
    /// The refusal states the frozen copy, including the sentence that absence of a held record is
    /// not absence of law. The live page said only that the work was not held and pointed at
    /// search, which the verdict names as the sterile refusal that trains readers to treat honesty
    /// as uselessness.
    /// </summary>
    [Fact]
    public async Task The_unknown_work_refusal_states_the_frozen_copy()
    {
        using var site = new NoticeSite(Path.Combine(_root, "unknown-copy"), includeAct: false);
        // A refusal is an answer, and it is served with the status that says so, which
        // GetStringAsync would throw on rather than return.
        var response = await site.Client.GetAsync("/lu-legilux/zzzz-9999-99-99-n1");
        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.Contains(UnknownWorkHeading, page, StringComparison.Ordinal);
        Assert.Contains(
            "Lex does not hold an instrument matching this identifier. This is not evidence "
            + "that the instrument or law does not exist.", page, StringComparison.Ordinal);
        Assert.Contains("Search the official publisher", page, StringComparison.Ordinal);
        // The old sterile refusal must not survive anywhere on the page.
        Assert.DoesNotContain("Try <a href=\"/search\">search</a>", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case the notice exists for, and the one the underlying substring search cannot reach on
    /// its own: a wrong trailing segment. This is the shape of the question catalog's own row 4,
    /// which asked for loi-2004-11-12-n3 when the held work is loi-2004-11-12-n1.
    /// </summary>
    [Fact]
    public async Task A_wrong_trailing_segment_offers_the_held_sibling()
    {
        using var site = new NoticeSite(Path.Combine(_root, "unknown-near"), includeAct: false);
        // A refusal is an answer, and it is served with the status that says so, which
        // GetStringAsync would throw on rather than return.
        var response = await site.Client.GetAsync("/lu-legilux/loi-2006-07-31-n9");
        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.Contains(UnknownWorkHeading, page, StringComparison.Ordinal);
        Assert.Contains(CandidateHeading, page, StringComparison.Ordinal);
        Assert.Contains("/lu-legilux/loi-2006-07-31-n2", page, StringComparison.Ordinal);
        // The slug that is not held is never offered back as a way to reach it.
        Assert.DoesNotContain("/lu-legilux/loi-2006-07-31-n9", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// No candidates, no candidate heading. An empty offer is worse than none: it promises records
    /// the corpus does not hold, which is the exact failure the notice contract forbids.
    /// </summary>
    [Fact]
    public async Task Nothing_near_means_no_candidate_block()
    {
        using var site = new NoticeSite(Path.Combine(_root, "unknown-far"), includeAct: false);
        // A refusal is an answer, and it is served with the status that says so, which
        // GetStringAsync would throw on rather than return.
        var response = await site.Client.GetAsync("/lu-legilux/zzzz-9999-99-99-n1");
        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.DoesNotContain(CandidateHeading, page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Derogation_notice_renders_inside_the_targeted_provision_card_only()
    {
        using var site = new NoticeSite(Path.Combine(_root, "with-act"), includeAct: true);
        var page = await site.Client.GetStringAsync(
            "/lu-legilux/loi-2006-07-31-n2/2024-08-04");

        Assert.Contains(DerogationHeading, page, StringComparison.Ordinal);
        Assert.Contains("dated 19 December 2020", page, StringComparison.Ordinal);
        Assert.Contains("does not yet hold the publisher's act-level", page, StringComparison.Ordinal);
        Assert.Contains("/lu-legilux/loi-2020-12-19-a1039", page, StringComparison.Ordinal);
        Assert.Contains("Open the derogating act", page, StringComparison.Ordinal);
        // The action links to the held act's publisher-asserted source, never a guessed ELI.
        Assert.Contains("https://example.test/derogation-source", page, StringComparison.Ordinal);

        // Decision 44(b): a consolidation-state interval boundary is never spoken as an
        // act-level force fact. The body must carry no force-boundary date at all.
        var notice = ExtractNotice(page);
        Assert.DoesNotContain("2021-07-01", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("2022-06-30", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("21 December 2020", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("30 June 2022", notice, StringComparison.Ordinal);

        // The notice binds to its provision card, not to the page: the sibling article on the
        // same page must not carry it.
        var otherCard = CardOf(page, "art_l_121-7");
        Assert.DoesNotContain(DerogationHeading, otherCard, StringComparison.Ordinal);
        var targetCard = CardOf(page, "art_l_121-6");
        Assert.Contains(DerogationHeading, targetCard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typed_gap_renders_at_its_anchor_with_its_trust_notice_and_blocks_text_diff()
    {
        using var site = new NoticeSite(
            Path.Combine(_root, "gap-anchor"), includeAct: true,
            targetIsGap: true, includeEarlierState: true);
        var page = await site.Client.GetStringAsync(
            "/lu-legilux/loi-2006-07-31-n2/2024-08-04");

        var targetCard = CardOf(page, "art_l_121-6");
        Assert.Contains(DerogationHeading, targetCard, StringComparison.Ordinal);
        Assert.Contains("Text unavailable", targetCard, StringComparison.Ordinal);
        Assert.Contains("marker_only", targetCard, StringComparison.Ordinal);
        Assert.Contains("https://example.test/loi-2006-07-31-n2#art_l_121-6",
            targetCard, StringComparison.Ordinal);
        Assert.DoesNotContain("legal-markdown", targetCard, StringComparison.Ordinal);
        Assert.DoesNotContain("text SHA", targetCard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DerogationHeading,
            CardOf(page, "art_l_121-7"), StringComparison.Ordinal);
        Assert.Contains("partial", page, StringComparison.OrdinalIgnoreCase);

        var comparison = await site.Client.GetStringAsync(
            "/lu-legilux/loi-2006-07-31-n2/diff/2024-07-01/2024-08-04");
        Assert.Contains("text diff is unavailable", comparison, StringComparison.Ordinal);
        Assert.Contains("typed text gap", comparison, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<ins", comparison, StringComparison.Ordinal);
        Assert.DoesNotContain("<del", comparison, StringComparison.Ordinal);

        var sameVersionComparison = await site.Client.GetStringAsync(
            "/lu-legilux/loi-2006-07-31-n2/diff/2024-08-04/2024-08-05");
        Assert.Contains("text diff is unavailable", sameVersionComparison,
            StringComparison.Ordinal);
        Assert.Contains("typed text gap", sameVersionComparison,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>No change.</b>", sameVersionComparison,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Derogation_notice_is_absent_when_the_derogating_act_is_not_held()
    {
        using var site = new NoticeSite(Path.Combine(_root, "without-act"), includeAct: false);
        var page = await site.Client.GetStringAsync(
            "/lu-legilux/loi-2006-07-31-n2/2024-08-04");

        // Same publisher, same work, same anchor; the only difference is that the mounted index
        // does not hold the derogating act. Missing evidence must produce no prose claim.
        Assert.Contains("art_l_121-6", page, StringComparison.Ordinal);
        Assert.DoesNotContain(DerogationHeading, page, StringComparison.Ordinal);
        Assert.DoesNotContain("derogat", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Derogation_notice_never_leaks_to_another_work()
    {
        using var site = new NoticeSite(Path.Combine(_root, "other-work"), includeAct: true);
        var page = await site.Client.GetStringAsync(
            "/lu-legilux/loi-2020-12-19-a1039/2021-07-01");

        // The derogating act's own page shares publisher and holds the act, but it is not the
        // governed work-and-anchor coordinate, so the notice must not appear.
        Assert.DoesNotContain(DerogationHeading, page, StringComparison.Ordinal);
    }

    [Fact]
    public void Pre_application_notice_requires_an_indexed_fact_and_an_earlier_state_date()
    {
        var doc = Doc("eu-eurlex", "gdpr", "2016-05-04");
        var fact = new PreApplicationFact(
            "2018-05-25", "/eu-eurlex/gdpr/dates",
            "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=OJ:L:2016:119:TOC");

        var rendered = TrustNotices.PreApplicationState(doc, fact);
        Assert.NotNull(rendered);
        Assert.Contains("Pre-application state", rendered, StringComparison.Ordinal);
        Assert.Contains("separate publisher dates", rendered, StringComparison.Ordinal);
        Assert.Contains("/eu-eurlex/gdpr/dates", rendered, StringComparison.Ordinal);
        Assert.Contains("https://eur-lex.europa.eu/legal-content", rendered, StringComparison.Ordinal);

        // No indexed fact, no claim; and a state dated on or after application is not
        // pre-application, whatever the fact says.
        Assert.Null(TrustNotices.PreApplicationState(doc, null));
        Assert.Null(TrustNotices.PreApplicationState(
            Doc("eu-eurlex", "gdpr", "2018-05-25"), fact));
        Assert.Null(TrustNotices.PreApplicationState(
            Doc("eu-eurlex", "gdpr", "2019-01-01"), fact));
    }

    [Fact]
    public void Pre_application_notice_fails_closed_on_hostile_or_malformed_evidence()
    {
        var doc = Doc("eu-eurlex", "gdpr", "2016-05-04");
        string? Render(string date, string typedDates, string journal) =>
            TrustNotices.PreApplicationState(doc,
                new PreApplicationFact(date, typedDates, journal));

        // The valid contract renders; every hostile or malformed member suppresses the whole
        // notice, because Decision 41 requires both actions and a partial evidence contract
        // must not become prose (Codex review O1).
        Assert.NotNull(Render("2018-05-25", "/eu-eurlex/gdpr/dates", "https://eur-lex.europa.eu/oj"));
        // javascript: scheme in either action.
        Assert.Null(Render("2018-05-25", "javascript:alert(1)", "https://eur-lex.europa.eu/oj"));
        Assert.Null(Render("2018-05-25", "/eu-eurlex/gdpr/dates", "javascript:alert(1)"));
        // Protocol-relative and scheme-bearing internal routes.
        Assert.Null(Render("2018-05-25", "//evil.example/dates", "https://eur-lex.europa.eu/oj"));
        Assert.Null(Render("2018-05-25", "https://evil.example/dates", "https://eur-lex.europa.eu/oj"));
        // Non-HTTPS official link.
        Assert.Null(Render("2018-05-25", "/eu-eurlex/gdpr/dates", "http://example.test/oj"));
        Assert.Null(Render("2018-05-25", "/eu-eurlex/gdpr/dates", "ftp://example.test/oj"));
        // Malformed or non-canonical dates.
        Assert.Null(Render("25/05/2018", "/eu-eurlex/gdpr/dates", "https://eur-lex.europa.eu/oj"));
        Assert.Null(Render("2018-5-25", "/eu-eurlex/gdpr/dates", "https://eur-lex.europa.eu/oj"));
        Assert.Null(Render("", "/eu-eurlex/gdpr/dates", "https://eur-lex.europa.eu/oj"));
        // Round 2 regressions: an encrypted link is not an official link, and a backslash
        // authority escape is not an internal route ("/\\evil.example/dates" resolves to
        // origin evil.example in browsers).
        Assert.Null(Render("2018-05-25", "/eu-eurlex/gdpr/dates", "https://evil.example/oj"));
        Assert.Null(Render("2018-05-25", "/\\evil.example/dates", "https://eur-lex.europa.eu/oj"));
        Assert.Null(Render("2018-05-25", "/eu-eurlex\\..\\x", "https://eur-lex.europa.eu/oj"));
        // Userinfo and explicit ports are not official publisher shapes.
        Assert.Null(Render("2018-05-25", "/eu-eurlex/gdpr/dates", "https://user@eur-lex.europa.eu/oj"));
        Assert.Null(Render("2018-05-25", "/eu-eurlex/gdpr/dates", "https://eur-lex.europa.eu:8443/oj"));
        // Control characters in the route fail closed.
        Assert.Null(Render("2018-05-25", "/eu-eurlex/gdpr\u0000/dates", "https://eur-lex.europa.eu/oj"));
        // Every official publisher host is accepted; case of the host does not matter.
        Assert.NotNull(Render("2018-05-25", "/x", "https://publications.europa.eu/resource/oj/x"));
        Assert.NotNull(Render("2018-05-25", "/x", "https://legilux.public.lu/eli/etat/leg/x"));
        Assert.NotNull(Render("2018-05-25", "/x", "https://EUR-LEX.europa.eu/oj"));
        // An unparseable state date fails closed too, whatever the fact says.
        Assert.Null(TrustNotices.PreApplicationState(
            Doc("eu-eurlex", "gdpr", "not-a-date"),
            new PreApplicationFact("2018-05-25", "/eu-eurlex/gdpr/dates", "https://eur-lex.europa.eu/oj")));
    }

    [Fact]
    public void Pre_application_evidence_source_answers_null_until_typed_dates_are_indexed()
    {
        // The seam is deliberately inert: the index holds no application-date fact today
        // (verified against the packaged EU index, 2026-08-28), so the source must answer null
        // for every document until EU typed dates land. This test freezes that contract; the
        // E1 implementation replaces it together with a real evidence-present path.
        using var site = new NoticeSite(Path.Combine(_root, "seam"), includeAct: true);
        using var reader = site.Reader();
        var doc = reader.ByKey("lu-legilux:loi-2006-07-31-n2:2024-08-04");
        Assert.NotNull(doc);
        Assert.Null(TrustNotices.FindPreApplicationFact(reader, doc!));
    }

    private static string ExtractNotice(string page)
    {
        var start = page.IndexOf(DerogationHeading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = page.IndexOf("</div>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return page[start..end];
    }

    /// <summary>The provision card markup for one anchor, bounded by the next card.</summary>
    private static string CardOf(string page, string anchor)
    {
        var start = page.IndexOf($"id=\"{anchor}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"provision card {anchor} not found");
        var end = page.IndexOf("<div class=\"card\" id=", start + 1, StringComparison.Ordinal);
        return end < 0 ? page[start..] : page[start..end];
    }

    private static DocRow Doc(string collection, string work, string validFrom) => new(
        $"{collection}:{work}:{validFrom}", collection, work, $"official:{work}", "REG", "en",
        validFrom, null, "official_consolidation_state", "2026-08-14T00:00:00Z", false,
        true, true, Sha(validFrom), null, $"https://example.test/{work}", "Test work",
        "Test work", null, validFrom, null);

    private static string Sha(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// A site whose synthetic Luxembourg index uses the production trigger identifiers: the
    /// Code du travail with articles L. 121-6 and L. 121-7, and optionally the derogating act
    /// loi-2020-12-19-a1039 with a publisher-asserted source URI.
    /// </summary>
    private sealed class NoticeSite : WebApplicationFactory<Program>
    {
        private readonly string _root;
        private readonly string _dbPath;
        public HttpClient Client { get; }

        public NoticeSite(
            string root, bool includeAct,
            bool targetIsGap = false, bool includeEarlierState = false)
        {
            _root = root;
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "app"));
            File.WriteAllText(Path.Combine(root, "wwwroot", "app", "workspace.js"), "/* test */\n");
            _dbPath = Path.Combine(root, "index-lu-legilux.db");
            BuildIndex(_dbPath, includeAct, targetIsGap, includeEarlierState);
            Client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        public LexIndexReader Reader() => LexIndexReader.Open(_dbPath);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _root);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://example.test");
            builder.UseWebRoot(Path.Combine(_root, "wwwroot"));
        }

        protected override void Dispose(bool disposing)
        {
            Client?.Dispose();
            base.Dispose(disposing);
        }

        private static ProvisionRow Provision(
            string rid, string key, int seq, string anchor, string num, string text) =>
            new(rid, seq, anchor, $"{key}#{anchor}", "article", num, null, "Livre I",
                null, "Code du travail", text, Sha(text));

        private static void BuildIndex(
            string path, bool includeAct, bool targetIsGap, bool includeEarlierState)
        {
            var codeKey = "lu-legilux:loi-2006-07-31-n2:2024-08-04";
            var code = new DocRow(
                codeKey, "lu-legilux", "loi-2006-07-31-n2", "official:loi-2006-07-31-n2",
                "CODE", "fr", "2024-08-04", null, "publisher", "2026-08-14T00:00:00Z",
                false, true, true, Sha("code"), null,
                "https://example.test/loi-2006-07-31-n2", "Code du travail",
                "Code du travail", null, "2024-08-04", null);
            var docs = new List<DocRow> { code };
            DocRow? earlier = null;
            if (includeEarlierState)
            {
                earlier = code with
                {
                    Key = "lu-legilux:loi-2006-07-31-n2:2024-07-01",
                    ValidFrom = "2024-07-01",
                    ValidTo = "2024-08-04",
                    RecordSha = Sha("earlier-code"),
                };
                docs.Add(earlier);
            }
            if (includeAct)
                docs.Add(new DocRow(
                    "lu-legilux:loi-2020-12-19-a1039:2021-07-01", "lu-legilux",
                    "loi-2020-12-19-a1039", "official:loi-2020-12-19-a1039", "LOI", "fr",
                    "2021-07-01", "2022-06-30", "publisher", "2026-08-14T00:00:00Z",
                    false, true, true, Sha("derogation"), null,
                    "https://example.test/derogation-source",
                    "Loi du 19 decembre 2020 portant derogation temporaire",
                    "Loi du 19 decembre 2020", null, "2020-12-24", null));
            var rid = $"{codeKey}|fr|2024-08-04";
            var provisions = new List<ProvisionRow>
            {
                Provision(rid, codeKey, 2, "art_l_121-7", "Art. L. 121-7",
                    "Texte voisin sans rapport avec la protection."),
            };
            if (!targetIsGap)
                provisions.Insert(0, Provision(
                    rid, codeKey, 1, "art_l_121-6", "Art. L. 121-6",
                    "Le contrat de travail est suspendu pendant la maladie."));
            if (earlier is not null)
            {
                var earlierRid = LexIndexReader.RidOf(earlier);
                provisions.Add(Provision(
                    earlierRid, earlier.Key, 1, "art_l_121-6", "Art. L. 121-6",
                    "Earlier synthetic wording."));
                provisions.Add(Provision(
                    earlierRid, earlier.Key, 2, "art_l_121-7", "Art. L. 121-7",
                    "Earlier neighbouring wording."));
            }
            var stamp = new Dictionary<string, string>
            {
                ["collection"] = "lu-legilux", ["tier"] = "A",
                ["history_begins"] = "publisher",
                ["built_at"] = "2026-08-14T00:00:00Z", ["corpus_commit"] = "test",
            };
            ProvisionGapIndexInput? gapInput = null;
            if (targetIsGap)
            {
                const string generationSha =
                    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
                const string articlesCommit =
                    "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
                stamp["generation_sha256"] = generationSha;
                stamp["articles_commit"] = articlesCommit;
                stamp["articles_canon"] = ProvisionGapIndexInput.RequiredArticlesCanon;
                gapInput = ProvisionGapIndexInput.FromGenerationEvidence(
                    ProvisionGapIndexInput.RequiredArticlesCanon,
                    generationSha, articlesCommit,
                    [new ProvisionGapRow(
                        rid, 1, "art_l_121-6", $"{codeKey}#art_l_121-6",
                        "https://example.test/loi-2006-07-31-n2#art_l_121-6",
                        "article", "Art. L. 121-6", null, "Livre I", null,
                        ProvisionGapReason.MarkerOnly)]);
            }
            IndexBuilder.Build(path, stamp, docs, provisions, [], [],
                StampSigner.CreateKeyPem(), provisionGaps: gapInput);
        }
    }
}
