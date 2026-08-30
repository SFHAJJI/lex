using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// Search-envelope attribution, both hostile directions. The envelope is MCP output, so it is
    /// untrusted: a non-string publisher used to throw out of GetValue and take the entire search
    /// page with it, and an absent one became the empty string, missed the registry, and dropped
    /// that publisher's hits with nothing on the page to say so.
    /// </summary>
    [Fact]
    public void An_unattributable_search_envelope_is_refused_rather_than_throwing()
    {
        var readers = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);

        foreach (var envelope in new[]
        {
            "{}",
            "{\"envelope\":{}}",
            "{\"envelope\":{\"publisher\":null}}",
            "{\"envelope\":{\"publisher\":\"\"}}",
            "{\"envelope\":{\"publisher\":7}}",
            "{\"envelope\":{\"publisher\":true}}",
            "{\"envelope\":{\"publisher\":[]}}",
            "{\"envelope\":{\"publisher\":{}}}",
            "{\"envelope\":\"lu-legilux\"}",
            "{\"envelope\":{\"publisher\":\"not-mounted\"}}",
        })
        {
            var node = (JsonObject)JsonNode.Parse(envelope)!;
            Assert.False(CatalogueEndpoints.TryAttribute(node, readers, out var reader));
            Assert.Null(reader);
        }
    }

    /// <summary>
    /// An unmatched route used to return HTTP 404 with a zero-byte body: no chrome, no reason, no
    /// way forward. It is the largest refusal surface on the site and it said nothing at all.
    /// </summary>
    [Fact]
    public async Task An_unmatched_route_answers_instead_of_returning_nothing()
    {
        using var site = new NoticeSite(Path.Combine(_root, "fallback"), includeAct: false);

        foreach (var route in new[]
        {
            "/no-such-page",
            // An unmounted publisher prefix and a stale work address both land here, which is why
            // the copy must not let a missing route read as a missing law.
            "/fr-legifrance/anything",
            "/lu-legilux/loi-2006-07-31-n2/2024-08-04/extra/segment",
        })
        {
            var response = await site.Client.GetAsync(route);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual(0, body.Length);
            Assert.Contains("Page not found", body, StringComparison.Ordinal);
            Assert.Contains("That is a statement about this site, not about the law",
                body, StringComparison.Ordinal);
            // A refusal is an answer, so it carries somewhere to go.
            Assert.Contains("/coverage", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The machine lanes and JSON-preferring clients get a typed body. Handing an HTML page to a
    /// client that asked for JSON is its own small lie about what happened.
    /// </summary>
    [Fact]
    public async Task An_unmatched_machine_route_answers_in_json()
    {
        using var site = new NoticeSite(Path.Combine(_root, "fallback-json"), includeAct: false);

        foreach (var route in new[] { "/api/no-such-thing", "/mcp/no-such-thing" })
        {
            var response = await site.Client.GetAsync(route);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("unknown_route", body, StringComparison.Ordinal);
            Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
        }

        // Near-prefix paths are ordinary pages, not machine lanes. A prefix test rather than
        // a segment test hands a reader JSON for a mistyped page.
        foreach (var nearPrefix in new[] { "/apiculture", "/mcproxy", "/apis", "/mcp-notes" })
        {
            var page = await site.Client.GetAsync(nearPrefix);
            var pageBody = await page.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
            Assert.Contains("Page not found", pageBody, StringComparison.Ordinal);
            Assert.DoesNotContain("unknown_route", pageBody, StringComparison.Ordinal);
        }

        // A JSON-preferring client on an ordinary path is answered the same way.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-page");
        request.Headers.Add("Accept", "application/json");
        var negotiated = await site.Client.SendAsync(request);
        var negotiatedBody = await negotiated.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, negotiated.StatusCode);
        Assert.Contains("unknown_route", negotiatedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", negotiatedBody, StringComparison.OrdinalIgnoreCase);
    }

    private const string DensityHeading = "Historical coverage is less dense";

    private static string PrimaryCount(string page)
    {
        var m = System.Text.RegularExpressions.Regex.Match(page, @"<b>([\d,]+) held work\(s\) changed</b>");
        Assert.True(m.Success, "primary count not found on the change report");
        return m.Groups[1].Value;
    }

    /// <summary>
    /// One resolved reader set decides the totals, the blocks and the caveat. The filter accepted
    /// null only, so an absent publisher and an empty one selected different sets from the same
    /// form, and an unrecognised value selected none while still reaching the caveat.
    /// </summary>
    [Fact]
    public async Task The_report_scope_does_not_depend_on_how_the_form_spells_no_publisher()
    {
        using var site = new NoticeSite(Path.Combine(_root, "scope"), includeAct: false);
        const string window = "/changed?from=2020-01-01&to=2026-01-01";

        var absent = await site.Client.GetStringAsync(window);
        var empty = await site.Client.GetStringAsync(window + "&publisher=");
        var named = await site.Client.GetStringAsync(window + "&publisher=lu-legilux");

        Assert.Equal(PrimaryCount(absent), PrimaryCount(empty));
        Assert.Equal(PrimaryCount(absent), PrimaryCount(named));
    }

    /// <summary>
    /// An unrecognised publisher selects no reader. A caveat about a set that never ran is an
    /// observation nobody made.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_publisher_selects_nothing_and_states_no_observation()
    {
        using var site = new NoticeSite(Path.Combine(_root, "alias"), includeAct: false);

        // "LU" is the jurisdiction, never a mounted collection id.
        var page = await site.Client.GetStringAsync(
            "/changed?from=1900-01-01&to=1900-12-31&publisher=LU");

        Assert.Equal("0", PrimaryCount(page));
        Assert.DoesNotContain(DensityHeading, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The caveat carries both frozen Decision 41 actions, and appears only for a pre-2017 window
    /// with a Luxembourg reader actually in the resolved set.
    /// </summary>
    [Fact]
    public async Task The_density_caveat_carries_both_frozen_actions_for_a_pre_2017_window()
    {
        using var site = new NoticeSite(Path.Combine(_root, "density"), includeAct: false);

        var before = await site.Client.GetStringAsync("/changed?from=1900-01-01&to=1900-12-31");
        Assert.Contains(DensityHeading, before, StringComparison.Ordinal);
        Assert.Contains(
            "For Luxembourg periods before 2017, Lex holds fewer dated consolidation states. This "
            + "result counts changes observed in held states, not every legal change. A lower count "
            + "may reflect coverage.", before, StringComparison.Ordinal);
        Assert.Contains("View coverage for this period", before, StringComparison.Ordinal);
        Assert.Contains("Open the official publisher", before, StringComparison.Ordinal);

        // The boundary itself is not "before".
        var after = await site.Client.GetStringAsync("/changed?from=2017-01-01&to=2026-01-01");
        Assert.DoesNotContain(DensityHeading, after, StringComparison.Ordinal);
    }

    /// <summary>
    /// The primary count is a claim about held records, never about the law.
    /// </summary>
    [Fact]
    public async Task An_empty_change_report_does_not_claim_that_no_law_changed()
    {
        using var site = new NoticeSite(Path.Combine(_root, "changed-empty"), includeAct: false);
        var page = await site.Client.GetStringAsync("/changed?from=1900-01-01&to=1900-12-31");

        Assert.DoesNotContain("law(s) changed", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing moved in this window", page, StringComparison.Ordinal);
        Assert.Contains("held work(s) changed", page, StringComparison.Ordinal);
        Assert.Contains("not a finding that no law changed", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both routes must DISCLOSE the never-consolidated class and must not SIZE it. Asserting the
    /// absence of one stale literal is not enough: deleting the disclosure, or changing the figure
    /// by one, both pass such a test.
    /// </summary>
    [Fact]
    public async Task Both_routes_disclose_the_unconsolidated_class_without_sizing_it()
    {
        using var site = new NoticeSite(Path.Combine(_root, "unsized"), includeAct: false);

        foreach (var (route, marker) in new[]
        {
            ("/coverage", "never get a consolidated edition"),
            ("/in-force-on?date=2024-08-04", "Never-consolidated Luxembourg acts are not ingested"),
        })
        {
            var page = await site.Client.GetStringAsync(route);
            Assert.Contains(marker, page, StringComparison.Ordinal);

            // No population size in the sentence that carries the class. Counts on these pages
            // are rendered with thousands separators (:n0), so the grouped form is what a
            // restored figure looks like; a bare year such as 2024 is not a population claim.
            var at = page.IndexOf(marker, StringComparison.Ordinal);
            var from = page.LastIndexOf('.', Math.Max(0, at - 1));
            var to = page.IndexOf('.', at + marker.Length);
            var sentence = page[(from < 0 ? 0 : from)..(to < 0 ? page.Length : to)];
            var sized = System.Text.RegularExpressions.Regex.Match(
                sentence, @"\d{1,3}(,\d{3})+");
            Assert.False(sized.Success,
                $"{route} states a population size next to the class disclosure: {sized.Value}");
        }
    }

    /// <summary>
    /// A whole-call refusal arrives as a bare object rather than the usual array. The page cast it
    /// to an array with an empty fallback, so the render loop never ran and the reader was shown a
    /// search form above nothing at all: no count, no notice, no explanation. A blank result area
    /// is the worst answer available, because it is the one a reader fills in themselves.
    /// </summary>
    [Fact]
    public async Task An_unmounted_publisher_is_answered_rather_than_rendered_blank()
    {
        using var site = new NoticeSite(Path.Combine(_root, "whole-call"), includeAct: false);
        var page = await site.Client.GetStringAsync("/search?q=protection&publisher=zzz");

        Assert.Contains("This query did not run", page, StringComparison.Ordinal);
        Assert.Contains("unknown_publisher", page, StringComparison.Ordinal);
        // Never a count, and never an absence claim about the law.
        Assert.DoesNotContain("hit(s)", page, StringComparison.Ordinal);
        // The producer states the guarantee in its own words; the page relays it rather than
        // paraphrasing a legal claim into copy nobody reviewed.
        Assert.Contains("This is not a statement that the corpus is empty",
            page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ordinary path must keep working: a real query still renders real hits and no refusal.
    /// A refusal branch that swallows the happy path is the obvious way to break this.
    /// </summary>
    [Fact]
    public async Task A_mounted_publisher_still_answers_normally()
    {
        using var site = new NoticeSite(Path.Combine(_root, "whole-call-ok"), includeAct: false);
        var page = await site.Client.GetStringAsync("/search?q=protection&publisher=lu-legilux");

        Assert.DoesNotContain("This query did not run", page, StringComparison.Ordinal);
        Assert.Contains("hit(s)", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The envelope status read, hostile cases. Untrusted MCP output: anything that is not a real
    /// string is no status, never a crash and never a coincidental "ok".
    /// </summary>
    [Fact]
    public void An_envelope_status_is_read_strictly_or_not_at_all()
    {
        Assert.Equal("ok", TrustNotices.EnvelopeStatus(
            (JsonObject)JsonNode.Parse("{\"envelope\":{\"status\":\"ok\"}}")!));

        foreach (var hostile in new[]
        {
            "{}", "{\"envelope\":{}}", "{\"envelope\":{\"status\":null}}",
            "{\"envelope\":{\"status\":\"\"}}", "{\"envelope\":{\"status\":7}}",
            "{\"envelope\":{\"status\":true}}", "{\"envelope\":{\"status\":[]}}",
            "{\"envelope\":\"ok\"}",
        })
        {
            Assert.Null(TrustNotices.EnvelopeStatus((JsonObject)JsonNode.Parse(hostile)!));
        }
    }

    /// <summary>
    /// A count is a claim, so a publisher that did not run the query gets its typed reason instead
    /// of a zero. These two statuses are not reachable from the server search page today, because
    /// it hardcodes keyword retrieval and sets no governed filter, so the copy is asserted directly
    /// rather than through a page that cannot currently produce them.
    /// </summary>
    [Fact]
    public void A_publisher_that_did_not_run_states_its_reason_and_no_count()
    {
        // The receipt is part of the real shape: McpCore stamps query_ran false on exactly this
        // status, so the fixture carries it too. Asserting the execution sentence against a
        // fixture that omitted the receipt was asserting copy for a response nobody sends.
        var filtered = TrustNotices.SearchEnvelopeRefusal("filter_not_supported_by_index",
            (JsonObject)JsonNode.Parse("{\"unsupported_filters\":[\"domain\",\"hierarchy\"],\"population\":{\"query_ran\":false}}")!);
        Assert.Contains("did not run this query", filtered, StringComparison.Ordinal);
        Assert.Contains("not evidence that a law or record is absent", filtered, StringComparison.Ordinal);
        Assert.Contains("domain, hierarchy", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("hit(s)", filtered, StringComparison.Ordinal);

        var mode = TrustNotices.SearchEnvelopeRefusal("retrieval_mode_unavailable", new JsonObject());
        Assert.Contains("signed retrieval benchmark has not authorized it", mode, StringComparison.Ordinal);
    }

    /// <summary>
    /// A corpus-wide zero is only honest when the corpus was searched. The browser lane already
    /// refuses to say nothing matches when a publisher could not run; this keeps the server lane
    /// from making the claim the browser declines to make.
    /// </summary>
    [Fact]
    public void A_corpus_wide_absence_is_only_stated_when_the_corpus_was_searched()
    {
        // O10. A refusal is not a PRECONDITION for saying nothing matched. This line asserted the
        // opposite and so locked the defect in place: a plain successful search that found
        // nothing rendered as a blank result area, which is the one answer a reader fills in
        // themselves and the very blank page this module exists to remove.
        Assert.Contains("could apply these filters",
            TrustNotices.SearchAbsence(ran: 2, refused: 0, hits: 0)!, StringComparison.Ordinal);
        Assert.Contains("could apply these filters",
            TrustNotices.SearchAbsence(ran: 1, refused: 1, hits: 0)!, StringComparison.Ordinal);
        Assert.Contains("No selected publisher ran this query",
            TrustNotices.SearchAbsence(ran: 0, refused: 2, hits: 0)!, StringComparison.Ordinal);
        // Nothing ran and nothing refused: there is no query to describe, so there is no
        // sentence. This is the one case where silence is the honest answer.
        Assert.Null(TrustNotices.SearchAbsence(ran: 0, refused: 0, hits: 0));
        // A publisher that answered with hits makes any no-match sentence false, however
        // many others refused.
        Assert.Null(TrustNotices.SearchAbsence(ran: 1, refused: 1, hits: 3));
        Assert.Null(TrustNotices.SearchAbsence(ran: 2, refused: 5, hits: 1));
        // An unreadable answer still blocks both forms.
        Assert.Null(TrustNotices.SearchAbsence(ran: 2, refused: 0, hits: 0, unreadable: 1));
    }

    /// <summary>
    /// A dotted path is still a path. MapFallback's default pattern is {*path:nonfile}, which
    /// excludes anything that looks like a file, so the first version of this fallback answered
    /// nothing for a whole class of URLs and returned the zero-byte 404 it was written to remove.
    /// </summary>
    [Fact]
    public async Task A_dotted_machine_path_is_answered_like_any_other()
    {
        using var site = new NoticeSite(Path.Combine(_root, "dotted"), includeAct: false);

        // A dotted path in the HUMAN lane stays with the asset lane. A blanket catch-all there
        // swallows static assets, which is why nonfile is the framework default; an HTML page is
        // not a useful answer to a request for a stylesheet.

        foreach (var machine in new[] { "/api/no-such.json", "/mcp/no-such.json" })
        {
            var response = await site.Client.GetAsync(machine);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("unknown_route", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Accept is a quality-ranked list, not a set of tokens. Treating any text/html mention as a
    /// preference hands HTML to a client that wrote text/html;q=0, which is the explicit statement
    /// that it will not take HTML.
    /// </summary>
    [Fact]
    public async Task Accept_quality_decides_the_representation()
    {
        using var site = new NoticeSite(Path.Combine(_root, "accept"), includeAct: false);

        async Task<string> Ask(string accept)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-page");
            request.Headers.TryAddWithoutValidation("Accept", accept);
            var response = await site.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        Assert.Contains("unknown_route", await Ask("application/json;q=1,text/html;q=0"),
            StringComparison.Ordinal);
        Assert.Contains("unknown_route", await Ask("application/json;q=0.9,text/html;q=0.1"),
            StringComparison.Ordinal);
        Assert.Contains("Page not found", await Ask("text/html;q=0.9,application/json;q=0.1"),
            StringComparison.Ordinal);
        // A browser's ordinary header, and the tie case: the human surface wins.
        Assert.Contains("Page not found", await Ask("text/html,application/xhtml+xml,*/*;q=0.8"),
            StringComparison.Ordinal);
        Assert.Contains("Page not found", await Ask("application/json,text/html"),
            StringComparison.Ordinal);

        // Wildcards are ranges too. A client that refused HTML outright and accepts anything
        // else is asking for the machine body, and application/* covers JSON.
        Assert.Contains("unknown_route", await Ask("text/html;q=0,*/*;q=1"),
            StringComparison.Ordinal);
        Assert.Contains("unknown_route", await Ask("application/*;q=1,text/html;q=.5"),
            StringComparison.Ordinal);
        // A comma inside a quoted parameter is not a range separator, and this range sets q=0.
        Assert.Contains("Page not found",
            await Ask("application/json;profile=\"x,text/html;q=0\";q=0"),
            StringComparison.Ordinal);
        // A precise range overrides a wildcard for the representation it names. That decides
        // which range speaks for that representation; it does not decide which representation
        // wins. Here the client downranked JSON itself, so the wildcard speaks only for HTML.
        Assert.Contains("Page not found", await Ask("application/json;q=0.1,*/*;q=1"),
            StringComparison.Ordinal);
        // The mirror image, and the reason the two rules must not be conflated: a client that
        // ranks HTML below everything else is asking for the machine body. It would be
        // incoherent for q=0 above to mean that while q=0.4 here did not.
        Assert.Contains("unknown_route", await Ask("text/html;q=0.4,*/*;q=1"),
            StringComparison.Ordinal);
        // A q outside 0..1 is not a louder preference, it is an unreadable one. Reading it as
        // stated would let it outrank every well-formed range in the header.
        Assert.Contains("Page not found", await Ask("text/html;q=0.4,application/json;q=1.5"),
            StringComparison.Ordinal);

        // Malformed headers answer, they do not crash. The hand parser threw on these and
        // returned 500, which is a worse refusal than the blank page this module replaced.
        foreach (var malformed in new[] { ";;;", ",,,", "=", "application/json;q=", "q=1", "" })
            Assert.Contains("Page not found", await Ask(malformed), StringComparison.Ordinal);

        // A q outside the RFC 7231 grammar states no preference, however willing the framework
        // parser is to read it. Quality accepts all of these and reports 1, which would let a
        // value the client got wrong outrank every well-formed range in the header.
        foreach (var loud in new[]
        {
            "text/html;q=0.4,application/json;q=1e0",     // exponent notation
            "text/html;q=0.4,application/json;q=1.0000",  // four fraction digits
            "text/html;q=0.4,application/json;q=1.001",   // above the maximum
            "text/html;q=0.4,application/json;q=0.5;q=1", // two answers to one question
            "text/html;q=0.4,application/json;q=\"1\"",     // quoted, and a qvalue is a token
            "text/html;q=0.4,application/json;q=.9",
            "text/html;q=0.4,application/json;q=+1",
        })
            Assert.Contains("Page not found", await Ask(loud), StringComparison.Ordinal);

        // The grammar it does allow, so the rule rejects malformed values rather than fractions.
        foreach (var valid in new[]
        {
            "text/html;q=0.4,application/json;q=1.000",
            "text/html;q=0.4,application/json;q=0.500",
            "text/html;q=0.4,application/json;q=1",
        })
            Assert.Contains("unknown_route", await Ask(valid), StringComparison.Ordinal);

        // A range that narrows the type with media parameters is asking for a representation this
        // route does not produce. Answering it with generic JSON is agreeing to a request we
        // cannot honour, which MatchesMediaType did because it ignores parameters entirely.
        foreach (var narrowed in new[]
        {
            "application/json;profile=\"x,y\";q=1",
            "application/json;profile=\"x\";q=1,text/html;q=0.1",
            "application/json;version=2;q=1,text/html;q=0.1",
        })
            Assert.Contains("Page not found", await Ask(narrowed), StringComparison.Ordinal);
    }

    /// <summary>
    /// The representation the negotiator matches against must be the one it actually sends.
    ///
    /// Matching on a bare type name ignored media parameters, which was wrong. Matching on a bare
    /// type VALUE is wrong in the other direction: a client that asks for exactly what this route
    /// emits, charset and all, was told the route could not produce it and given the page instead.
    ///
    /// This asks each lane what it sends, then asks for precisely that and requires the same
    /// answer back, so the two cannot drift apart again whatever the framework appends.
    /// </summary>
    [Fact]
    public async Task The_representation_offered_is_the_one_emitted()
    {
        using var site = new NoticeSite(Path.Combine(_root, "offered"), includeAct: false);

        async Task<HttpResponseMessage> Ask(string accept)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-page");
            request.Headers.TryAddWithoutValidation("Accept", accept);
            return await site.Client.SendAsync(request);
        }

        foreach (var (asked, marker) in new[]
        {
            ("application/json", "unknown_route"),
            ("text/html", "Page not found"),
        })
        {
            var first = await Ask(asked);
            var emitted = first.Content.Headers.ContentType?.ToString();
            Assert.False(string.IsNullOrEmpty(emitted), asked);

            var again = await Ask(emitted!);
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
            Assert.Contains(marker, await again.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            Assert.Equal(emitted, again.Content.Headers.ContentType?.ToString());
        }

        // The case from the objection, written out rather than left implicit in the loop.
        Assert.Contains("unknown_route",
            await (await Ask("application/json;charset=utf-8")).Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // A parameter this route does NOT emit is still declined, so the repair did not simply
        // reopen the hole the parameter check closed.
        Assert.Contains("Page not found",
            await (await Ask("application/json;profile=\"x\"")).Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // charset is ignored only when it is the one actually sent. Dropping the parameter
        // wholesale promised any charset at all and then answered in UTF-8 regardless, which is a
        // different lie in the same place: the client asked for something we cannot produce and
        // was told we could.
        foreach (var foreign in new[]
        {
            "application/json;charset=iso-8859-1", "application/json;charset=windows-1252",
            "application/json;charset=us-ascii",
        })
            Assert.Contains("Page not found",
                await (await Ask(foreign)).Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // And the one we do send, however it is spelled.
        foreach (var ours in new[]
        {
            "application/json;charset=utf-8", "application/json;charset=UTF-8",
            "application/json;charset=Utf-8",
        })
            Assert.Contains("unknown_route",
                await (await Ask(ours)).Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The body of this URL depends on the request headers, so it must say so. Without Vary a
    /// shared cache may reuse one client's negotiated representation for another: the JSON refusal
    /// served to a browser, or the page served to an MCP client.
    /// </summary>
    [Fact]
    public async Task A_negotiated_404_declares_that_it_varies_on_accept()
    {
        using var site = new NoticeSite(Path.Combine(_root, "vary"), includeAct: false);

        foreach (var (path, accept) in new[]
        {
            ("/no-such-page", "text/html"),
            ("/no-such-page", "application/json"),
            ("/api/no-such.json", "application/json"),
            ("/mcp/no-such", "text/html"),
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Accept", accept);
            var response = await site.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("Accept", response.Headers.Vary, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A connect command is an instruction to point a client at a server, so it must name this
    /// site. It was built from X-Forwarded-Proto and the Host header, both requester-controlled,
    /// so a request carrying Host: evil.example made /developers print a command telling the
    /// reader to point their MCP client at evil.example. The header was also unencoded.
    /// </summary>
    [Fact]
    public async Task The_printed_connect_command_names_this_site_not_the_request()
    {
        using var site = new NoticeSite(Path.Combine(_root, "connect"), includeAct: false);
        var hostile = new HttpRequestMessage(HttpMethod.Get, "/developers");
        hostile.Headers.Host = "evil.example";
        hostile.Headers.TryAddWithoutValidation(
            "X-Forwarded-Proto", "https,</pre><svg onload=alert(1)>");
        var page = await (await site.Client.SendAsync(hostile)).Content.ReadAsStringAsync();

        // The configured base, which NoticeSite sets to https://example.test.
        Assert.Contains("https://example.test/mcp", page, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg onload=", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The site published a CC BY 4.0 licence for the publishers' legal text, hardcoded into the
    /// schema.org node of every work page and the catalogue, on the authority of nothing.
    ///
    /// Whether a publisher's text may be redistributed under a named licence is precisely what the
    /// licence evidence work exists to establish, and its outcome set has three ways for the answer
    /// to be no. A claim in machine-readable form on every page, about someone else's material, is
    /// the largest instance of the class this product exists to prevent.
    /// </summary>
    [Fact]
    public async Task No_page_asserts_a_licence_for_the_publishers_text()
    {
        using var site = new NoticeSite(Path.Combine(_root, "licence"), includeAct: false);

        // Every route that carries the shared footer, plus the two that carry a JSON-LD node
        // and the page that tabulates the repositories.
        foreach (var route in new[]
        {
            "/lu-legilux/loi-2006-07-31-n2", "/browse", "/coverage", "/developers", "/find",
            "/lu-legilux/loi-2006-07-31-n2/2024-08-04", "/search?q=travail",
        })
        {
            var page = await site.Client.GetStringAsync(route);

            Assert.DoesNotContain("creativecommons.org", page, StringComparison.OrdinalIgnoreCase);
            // The schema.org key itself, so a different licence URL cannot slip back in.
            Assert.DoesNotContain("\"license\"", page, StringComparison.Ordinal);
            // And the WORDING, because that is how the claim survived the first removal: it moved
            // into a DataDownload description and sat in the footer, where a test checking one URL
            // and one key could not see it. A licence claim is a claim in any spelling.
            foreach (var wording in new[] { "CC-BY", "CC BY", "creative commons" })
                Assert.DoesNotContain(wording, page, StringComparison.OrdinalIgnoreCase);
            // O11. Openness and reuse are claims about how the publishers' material may be used,
            // which is exactly what per-artifact admission establishes and has three ways of
            // answering no. Neutral access wording until it exists.
            foreach (var wording in new[]
                     { "open data", "open datasets", "open legal", "licence and attribution" })
                Assert.DoesNotContain(wording, page, StringComparison.OrdinalIgnoreCase);
        }

        // Free to access is a fact about this site and is not a redistribution claim, so it stays.
        Assert.Contains("isAccessibleForFree", await site.Client.GetStringAsync("/browse"),
            StringComparison.Ordinal);
        // So does the licence of Lex's own code, which is ours to state and independently true.
        Assert.Contains("Apache-2.0", await site.Client.GetStringAsync("/developers"),
            StringComparison.Ordinal);
        // And the EU reuse basis, which cites the decision it rests on rather than asserting a
        // licence we chose. Flagged to the reviewer as a deliberate boundary, not an oversight.
        // And the dataset row says what it can support: where each row came from and how it is
        // chained to the publisher bytes, rather than what may be done with it.
        var developers = await site.Client.GetStringAsync("/developers");
        Assert.Contains("SHA-256 chain to the publisher bytes", developers,
            StringComparison.Ordinal);
        Assert.Contains("2011/833/EU", await site.Client.GetStringAsync("/coverage"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// O2. Every lane that reached the provision text is an answer. fuzzy is one: it is the same
    /// provision search re-run over a token-expanded query, and both other consumers of these
    /// values, the assistant at AskService.HasDirectProvisionEvidence and the React reader, group
    /// it with keyword and semantic. Only this page dropped it, which hid real text answers and
    /// badged the survivors as title matches.
    /// </summary>
    [Theory]
    [InlineData("keyword")]
    [InlineData("fuzzy")]
    [InlineData("semantic")]
    public void Every_lane_that_reached_the_provision_text_is_an_answer(string reason)
    {
        using var site = new NoticeSite(Path.Combine(_root, "lane" + reason), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse(
                "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
                + "\"population\":{\"query_ran\":true},"
                + "\"publisher_result_set\":{\"total\":1,\"returned\":1,\"maximum\":8,\"truncated\":false},"
                + "\"response_row_set\":{\"maximum\":10,\"returned\":1,\"truncated\":false},\"hits\":[{"
                + "\"work\":\"lu-legilux:loi-2006-07-31-n2\",\"anchor\":\"art_l_121-6\","
                + "\"title\":\"Code du travail\",\"valid_from\":\"2024-08-04\","
                + "\"snippet\":\"Le contrat est suspendu.\","
                + "\"match_reasons\":[\"" + reason + "\"]}]}")!],
            readers);

        // Presented as an answer, with its wording, and not demoted to a record match.
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("Le contrat est suspendu.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("matched on title, not wording", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Lex found records that match only in metadata", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The licence claim also lived where no page test could see it: the social-preview card is
    /// generated as an IMAGE, so a chip reading open data (CC-BY) was published on every share of
    /// every page and no HTML assertion or text search would ever have found it.
    ///
    /// This guards the generator, which is the source of truth for that image and is in-tree
    /// precisely so the picture is reproducible rather than a mystery binary. Anything else served
    /// from wwwroot as text is covered too, since that is the same blind spot one file over.
    /// </summary>
    [Fact]
    public void No_published_asset_asserts_a_licence_for_the_publishers_text()
    {
        var wwwroot = Path.Combine(RepositoryRoot(), "src", "Lex.Web", "wwwroot");
        Assert.True(Directory.Exists(wwwroot), wwwroot);

        string[] textual = [".py", ".js", ".mjs", ".css", ".html", ".json", ".txt", ".svg", ".xml"];
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories)
                     .Where(path => textual.Contains(Path.GetExtension(path),
                                                     StringComparer.OrdinalIgnoreCase)))
        {
            var content = File.ReadAllText(file);
            scanned++;
            foreach (var wording in new[] { "CC-BY", "CC BY", "creativecommons", "creative commons" })
                Assert.DoesNotContain(wording, content, StringComparison.OrdinalIgnoreCase);

            // O6. A count in an image is a claim nobody can maintain: the card said 8 while the
            // endpoint served ten, which is the developers-page bug in a surface the count-at-build
            // rule cannot reach. The numeral goes rather than being corrected.
            Assert.DoesNotMatch(
                new System.Text.RegularExpressions.Regex(
                    @"\d+\s*(MCP|tools)", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                content);
        }

        // A guard that scanned nothing would pass forever.
        Assert.True(scanned > 0, "no textual asset was scanned");
        Assert.Contains("make-og.py",
            Directory.EnumerateFiles(wwwroot, "*.py", SearchOption.AllDirectories)
                .Select(Path.GetFileName));
    }

    /// <summary>
    /// Whether this directory is a repository root.
    ///
    /// In a LINKED WORKTREE .git is a FILE containing a gitdir pointer, not a directory. Accepting
    /// only the directory form made the search climb past every worktree to the volume root, so
    /// this test passed in the primary checkout and failed everywhere the integration branch is
    /// actually assembled.
    /// </summary>
    internal static bool IsRepositoryRoot(string directory)
    {
        var git = Path.Combine(directory, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    /// <summary>The repository root, found from the test assembly rather than assumed.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !IsRepositoryRoot(directory.FullName))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>
    /// O5. Both forms of a repository root, proven against a real linked worktree rather than a
    /// hand-made file, so the test cannot drift from what git actually writes.
    /// </summary>
    [Fact]
    public void A_repository_root_is_found_in_a_linked_worktree_too()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "lex-worktree-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(scratch);
        try
        {
            // Wherever this suite is running, INCLUDING a linked worktree, which is the
            // integration environment. Asserting the directory form here assumed the primary
            // checkout and failed in exactly the place the guard exists to protect.
            var current = RepositoryRoot();
            Assert.True(IsRepositoryRoot(current));
            Assert.True(Directory.Exists(Path.Combine(current, ".git"))
                        || File.Exists(Path.Combine(current, ".git")));

            // A linked worktree, where git writes .git as a FILE.
            var worktree = Path.Combine(scratch, "linked");
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "git", $"worktree add --detach \"{worktree}\"")
            {
                WorkingDirectory = current,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            git.WaitForExit();
            Assert.True(git.ExitCode == 0, git.StandardError.ReadToEnd());

            try
            {
                Assert.True(File.Exists(Path.Combine(worktree, ".git")),
                    "git no longer writes .git as a file in a linked worktree");
                Assert.False(Directory.Exists(Path.Combine(worktree, ".git")));
                Assert.True(IsRepositoryRoot(worktree));
            }
            finally
            {
                using var remove = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(
                        "git", $"worktree remove --force \"{worktree}\"")
                    {
                        WorkingDirectory = current,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    })!;
                remove.WaitForExit();
                // A cleanup that failed quietly leaves a worktree registered against the repository
                // for every future run, so it is asserted rather than hoped for.
                Assert.True(remove.ExitCode == 0, remove.StandardError.ReadToEnd());
            }

            // And neither form present is not a root.
            Assert.False(IsRepositoryRoot(scratch));
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    /// <summary>
    /// Addendum (a). Disclosing an unattributable publisher is not enough. It answered, and this
    /// page cannot say what it answered, so a corpus-wide absence beside it would still be a claim
    /// about a response nobody read.
    /// </summary>
    [Fact]
    public void An_unattributable_publisher_also_blocks_the_absence_sentence()
    {
        using var site = new NoticeSite(Path.Combine(_root, "unattrib"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var stranger = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"not-mounted","status":"ok"},
             "population":{"query_ran":true},"hits":[]}
            """)!;
        var refusal = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"filter_not_supported_by_index"},
             "population":{"query_ran":false}}
            """)!;

        var page = CatalogueEndpoints.RenderSearchResults([stranger, refusal], readers);

        Assert.Contains("could not be attributed", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No match was returned", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No selected publisher ran this query", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Addendum (b). A hit is rendered as a link to a dated version, so it needs the coordinates
    /// that make one. Without them the page emitted an href with empty segments, which is a
    /// destination that goes nowhere presented to the reader as a citation.
    /// </summary>
    [Theory]
    [InlineData("\"work\":\"\",\"valid_from\":\"2024-08-04\"")]
    [InlineData("\"work\":7,\"valid_from\":\"2024-08-04\"")]
    [InlineData("\"valid_from\":\"2024-08-04\"")]
    [InlineData("\"work\":\"lu-legilux:loi-2006-07-31-n2\",\"valid_from\":\"\"")]
    [InlineData("\"work\":\"lu-legilux:loi-2006-07-31-n2\",\"valid_from\":true")]
    [InlineData("\"work\":\"lu-legilux:loi-2006-07-31-n2\"")]
    // Round 3 O3: Length > 0 accepted both of these and built an authoritative-looking link.
    [InlineData("\"work\":\"   \",\"valid_from\":\"2024-08-04\"")]
    [InlineData("\"work\":\"\\t\\n\",\"valid_from\":\"2024-08-04\"")]
    [InlineData("\"work\":\"lu-legilux:x\",\"valid_from\":\"04-08-2024\"")]
    [InlineData("\"work\":\"lu-legilux:x\",\"valid_from\":\"2024-13-45\"")]
    [InlineData("\"work\":\"lu-legilux:x\",\"valid_from\":\"yesterday\"")]
    [InlineData("\"work\":\"lu-legilux:x\",\"valid_from\":\"2024-08-04T00:00:00Z\"")]
    public void A_hit_without_a_usable_destination_is_not_rendered_as_one(string coordinates)
    {
        using var site = new NoticeSite(Path.Combine(_root, "dest"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse(
                "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
                + "\"population\":{\"query_ran\":true},\"hits\":[{" + coordinates
                + ",\"match_reasons\":[\"keyword\"]}]}")!],
            readers);

        Assert.Contains("could not be read", page, StringComparison.Ordinal);
        // No half-built citation survives to be clicked.
        Assert.DoesNotContain("href=\"/lu-legilux//", page, StringComparison.Ordinal);
        Assert.DoesNotContain("1 hit(s)", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Round 3 O3, second half. A path component is percent-encoded, not HTML-encoded: H()
    /// neutralises what breaks markup and leaves what breaks a URL, so a work carrying a slash or
    /// a hash rewrote the rest of the address while still looking like a citation.
    /// </summary>
    [Fact]
    public void A_link_component_is_encoded_for_the_url_not_for_the_markup()
    {
        using var site = new NoticeSite(Path.Combine(_root, "encode"), includeAct: false);
        using var reader = site.Reader();

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse("""
                {"envelope":{"publisher":"lu-legilux","status":"ok"},
                 "population":{"query_ran":true},
                 "hits":[{"work":"lu-legilux:a/b#c","valid_from":"2024-08-04",
                          "anchor":"art/1#x","match_reasons":["keyword"]}]}
                """)!],
            new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader });

        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        // The separators the work carried are data, not structure.
        Assert.Contains("lu-legilux%3Aa%2Fb%23c", page, StringComparison.Ordinal);
        Assert.Contains("art%2F1%23x", page, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/lu-legilux/lu-legilux:a/b#c", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Addendum (c). The receipt outranks the status name. A recognised non-execution status
    /// arriving with query_ran true is a contradiction, and the page may not resolve it by picking
    /// the half it recognises.
    /// </summary>
    [Fact]
    public void A_recognised_status_that_says_it_ran_did_not_fail_to_run()
    {
        foreach (var contradictory in new[]
        {
            "{\"status\":\"no_corpus_mounted\",\"population\":{\"query_ran\":true}}",
            "{\"status\":\"unknown_publisher\",\"population\":{\"query_ran\":true}}",
            "{\"status\":\"no_corpus_mounted\",\"detail\":\"No index is mounted here.\","
            + "\"population\":{\"query_ran\":true}}",
            "{\"status\":\"unknown_publisher\",\"detail\":\"Publisher zzz is not mounted.\","
            + "\"population\":{\"query_ran\":true}}",
        })
        {
            var card = TrustNotices.WholeCallRefusal((JsonObject)JsonNode.Parse(contradictory)!);
            Assert.DoesNotContain("did not run", card, StringComparison.Ordinal);
            Assert.Contains("No usable result.", card, StringComparison.Ordinal);
            // The BODY has to follow the lead. Keeping the status-specific sentence, or relaying
            // the producer detail written to explain it, restates the half of the contradiction
            // the page has just declined to believe.
            Assert.DoesNotContain("no verified legal index mounted", card,
                StringComparison.Ordinal);
            Assert.DoesNotContain("No index is mounted here.", card, StringComparison.Ordinal);
            Assert.DoesNotContain("Publisher zzz is not mounted.", card,
                StringComparison.Ordinal);
            Assert.Contains("its own receipt", card, StringComparison.Ordinal);
        }

        // Unchanged where there is no contradiction to resolve.
        Assert.Contains("This query did not run.", TrustNotices.WholeCallRefusal(
            (JsonObject)JsonNode.Parse("{\"status\":\"no_corpus_mounted\"}")!),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Round 2 O3. The identity lanes fire only when the query IS the instrument's identifier,
    /// title or publisher short title, or wholly contains one. That is the reader naming the law
    /// they want, so it answers a different question rather than failing to answer.
    /// </summary>
    [Theory]
    [InlineData("exact_identifier")]
    [InlineData("exact_title")]
    [InlineData("exact_publisher_short_title")]
    [InlineData("contained_identifier")]
    [InlineData("contained_title")]
    [InlineData("contained_publisher_short_title")]
    public void Naming_the_instrument_is_answered_with_the_instrument(string reason)
    {
        using var site = new NoticeSite(Path.Combine(_root, "id" + reason), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse(
                "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
                + "\"population\":{\"query_ran\":true},"
                + "\"publisher_result_set\":{\"total\":1,\"returned\":1,\"maximum\":8,\"truncated\":false},"
                + "\"response_row_set\":{\"maximum\":10,\"returned\":1,\"truncated\":false},\"hits\":[{"
                + "\"work\":\"lu-legilux:loi-2006-07-31-n2\",\"title\":\"Code du travail\","
                + "\"valid_from\":\"2024-08-04\","
                + "\"match_reasons\":[\"" + reason + "\"]}]}")!],
            readers);

        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("matched the name of this law, not its wording", page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Lex found records that match only in metadata", page,
            StringComparison.Ordinal);
        // It matched a name, and the badge must not claim it matched the wording either.
        Assert.DoesNotContain("matched on title, not wording", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// O21. The server validated only a nonblank work and an ISO date before suppressing, while
    /// MatchLanes.NoticeHtml applies a stricter grammar afterwards. So a coordinate the notice
    /// would reject still suppressed the cards, and then lost its own disclosure: the reader was
    /// left with a notice containing nothing, which is the worst of both answers.
    ///
    /// Lex.Temporal.VersionIdentity is the rule, and DateOf accepts nothing but
    /// yyyy-MM-dd--<64 lowercase hex>.
    /// </summary>
    [Theory]
    // A bare date, which the fixtures used to bless and the producer cannot mint.
    [InlineData("lu-legilux:loi-2006-07-31-n2:2024-08-04")]
    // A short, an uppercase and an over-long hash.
    [InlineData("lu-legilux:loi-2006-07-31-n2:2024-08-04--abc123")]
    [InlineData("lu-legilux:loi-2006-07-31-n2:2024-08-04--AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    // A group the notice's own grammar rejects, which is how the disclosure went missing.
    [InlineData("lu-legilux:bad/group:2024-08-04--HASH")]
    // Someone else's publisher.
    [InlineData("eu-eurlex:loi-2006-07-31-n2:2024-08-04--HASH")]
    // A version date that disagrees with valid_from. They are two spellings of one fact, so a
    // response where they differ is not a coordinate anyone can place.
    [InlineData("lu-legilux:loi-2006-07-31-n2:2024-08-05--HASH")]
    // Two segments, which is a work id and not a search coordinate.
    [InlineData("lu-legilux:loi-2006-07-31-n2")]
    public void A_coordinate_the_producer_cannot_mint_authorises_no_suppression(string lexId)
    {
        using var site = new NoticeSite(Path.Combine(_root, "coord"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse(
                "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
                + "\"population\":{\"query_ran\":true},"
                + "\"publisher_result_set\":{\"total\":1,\"returned\":1,\"maximum\":8,\"truncated\":false},"
                + "\"response_row_set\":{\"maximum\":10,\"returned\":1,\"truncated\":false},\"hits\":[{"
                + "\"work\":\"loi-2006-07-31-n2\",\"lex_id\":\""
                + lexId.Replace("HASH", HASH, StringComparison.Ordinal)
                + "\",\"valid_from\":\"2024-08-04\","
                + "\"match_reasons\":[\"work_metadata\"]}]}")!],
            readers);

        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rebuild 0 has not replaced every mounted V2 coordinate yet. Its same-date disambiguator is
    /// a readable route, but it is not a canon/2 identity and therefore cannot authorize a V3
    /// response-wide metadata claim.
    /// </summary>
    [Fact]
    public void A_v2_same_date_disambiguator_renders_without_authorising_v3_suppression()
    {
        using var site = new NoticeSite(Path.Combine(_root, "v2-disambiguator"), includeAct: false);
        using var reader = site.Reader();
        var result = CompleteMetadataSearchResult();
        var hit = (JsonObject)((JsonArray)result["hits"]!)[0]!;
        hit["lex_id"] = "lu-legilux:loi-2006-07-31-n2:2024-08-04--02";

        var page = CatalogueEndpoints.RenderSearchResults(
            [result], new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader });

        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("Code du travail", page, StringComparison.Ordinal);
        Assert.Contains("matched only in metadata", page, StringComparison.Ordinal);
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A disclosure coordinate and its render destination are one fact. A missing, blank or
    /// contradictory work field cannot both refuse rendering and authorize a positive metadata
    /// claim from the same publisher envelope.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("a-different-work")]
    public void An_unusable_work_authorises_no_metadata_claim(string? work)
    {
        using var site = new NoticeSite(Path.Combine(_root, "workcoordinate"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };
        var result = CompleteMetadataSearchResult();
        var hit = (JsonObject)((JsonArray)result["hits"]!)[0]!;
        if (work is null) hit.Remove("work");
        else hit["work"] = work;

        var page = CatalogueEndpoints.RenderSearchResults([result], readers);

        Assert.Contains("could not be read", page, StringComparison.Ordinal);
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.DoesNotContain("Code du travail", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// O21, second half. A truncated row set means the response is a PAGE of the answer, and
    /// "everything that matched, matched only records" is not a claim a page can support.
    /// </summary>
    [Fact]
    public void A_truncated_row_set_authorises_no_response_wide_claim()
    {
        using var site = new NoticeSite(Path.Combine(_root, "rowset"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse(
                "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
                + "\"population\":{\"query_ran\":true},"
                + "\"publisher_result_set\":{\"total\":1,\"returned\":1,\"maximum\":8,\"truncated\":false},"
                + "\"response_row_set\":{\"maximum\":1,\"returned\":1,\"truncated\":true},\"hits\":[{"
                + "\"work\":\"loi-2006-07-31-n2\",\"lex_id\":\"" + CANONICAL + "\","
                + "\"valid_from\":\"2024-08-04\","
                + "\"match_reasons\":[\"work_metadata\"]}]}")!],
            readers);

        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
    }

    /// <summary>A genuine canonical version key, per Lex.Temporal.VersionIdentity.</summary>
    private const string HASH =
        "b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5";
    private const string CANONICAL =
        "lu-legilux:loi-2006-07-31-n2:2024-08-04--b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5";

    private static JsonObject CompleteMetadataSearchResult() =>
        (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"ok"},
             "population":{"query_ran":true},
             "publisher_result_set":{"total":1,"returned":1,"maximum":8,"truncated":false},
             "response_row_set":{"maximum":10,"returned":1,"truncated":false},
             "hits":[{"work":"loi-2006-07-31-n2","lex_id":"lu-legilux:loi-2006-07-31-n2:2024-08-04--b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5","valid_from":"2024-08-04",
                      "title":"Code du travail","match_reasons":["work_metadata"]}]}
            """)!;

    /// <summary>
    /// O7 amended. The population is the partition THIS PAGE accepted. A status filter is not that
    /// partition: status ok with query_ran false is a query nobody executed, the render loop
    /// refuses it, and admitting its rows let the page emit a positive metadata-only claim about an
    /// envelope it had already declined to show.
    /// </summary>
    [Fact]
    public void A_refused_receipt_cannot_authorise_the_metadata_claim()
    {
        using var site = new NoticeSite(Path.Combine(_root, "falsereceipt"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse("""
                {"envelope":{"publisher":"lu-legilux","status":"ok"},
                 "population":{"query_ran":false},
                 "publisher_result_set":{"total":1,"returned":1,"maximum":8,"truncated":false},
                 "response_row_set":{"maximum":10,"returned":1,"truncated":false},
                 "hits":[{"work":"loi-2006-07-31-n2","lex_id":"lu-legilux:loi-2006-07-31-n2:2024-08-04--b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5","valid_from":"2024-08-04",
                          "title":"Code du travail","match_reasons":["work_metadata"]}]}
                """)!],
            readers);

        // The envelope is refused, so it authorises nothing.
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.DoesNotContain("1 hit(s)", page, StringComparison.Ordinal);
        // And the refusal itself is disclosed rather than swallowed. The receipt denies execution
        // explicitly here, so the page may say so rather than only that the result is unusable.
        Assert.Contains("Did not run.", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The third contributor shape from the amendment: a publisher this page cannot attribute,
    /// beside a valid metadata answer. It answered, we cannot say what it answered, so no claim may
    /// be made across it.
    /// </summary>
    [Fact]
    public void An_unattributable_publisher_disables_the_metadata_claim()
    {
        using var site = new NoticeSite(Path.Combine(_root, "unattrmeta"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse("""
                {"envelope":{"publisher":"not-mounted","status":"ok"},
                 "population":{"query_ran":true},
                 "hits":[{"work":"x:y","valid_from":"2024-08-04",
                          "match_reasons":["work_metadata"]}]}
                """)!,
             (JsonObject)JsonNode.Parse("""
                {"envelope":{"publisher":"lu-legilux","status":"ok"},
                 "population":{"query_ran":true},
                 "publisher_result_set":{"total":1,"returned":1,"maximum":8,"truncated":false},
                 "response_row_set":{"maximum":10,"returned":2,"truncated":false},
                 "hits":[{"work":"loi-2006-07-31-n2","lex_id":"lu-legilux:loi-2006-07-31-n2:2024-08-04--b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5","valid_from":"2024-08-04",
                          "title":"Code du travail","match_reasons":["work_metadata"]}]}
                """)!],
            readers);

        Assert.Contains("could not be attributed", page, StringComparison.Ordinal);
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        // The readable row still renders rather than hiding behind a claim that was never earned.
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// O7. The response-level claim is made ACROSS answers, so one it could not read poisons it.
    /// ResponsePopulation skips unreadable shapes silently, by design, so deciding metadata_only
    /// from it alone made the page disclose an unreadable answer and, in the same breath, claim
    /// that every record matched only metadata. The valid row was hidden behind that notice.
    /// </summary>
    [Theory]
    // A top-level sibling of the wrong shape, beside a successful metadata answer.
    [InlineData("\"a publisher answered\"")]
    [InlineData("7")]
    public void An_unreadable_sibling_disables_the_response_level_metadata_claim(string sibling)
    {
        using var site = new NoticeSite(Path.Combine(_root, "poison"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var envelopes = (JsonArray)JsonNode.Parse("[" + sibling + "]")!;
        envelopes.Add(JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"ok"},
             "population":{"query_ran":true},
             "hits":[{"work":"loi-2006-07-31-n2","lex_id":"lu-legilux:loi-2006-07-31-n2:2024-08-04--b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5","valid_from":"2024-08-04",
                      "title":"Code du travail","match_reasons":["work_metadata"]}]}
            """));

        var page = CatalogueEndpoints.RenderSearchResults(envelopes, readers);

        // The unreadable answer is disclosed.
        Assert.Contains("could not be read", page, StringComparison.Ordinal);
        // And no positive claim is made across it.
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        // The valid row is not hidden behind a notice that was never earned.
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("matched only in metadata", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same poisoning from inside one envelope: a hit that is not an object makes that
    /// publisher's answer unreadable, and the claim is across publishers, so it falls too.
    /// </summary>
    [Fact]
    public void A_non_object_hit_disables_the_response_level_metadata_claim()
    {
        using var site = new NoticeSite(Path.Combine(_root, "poisonhit"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse("""
                {"envelope":{"publisher":"lu-legilux","status":"ok"},
                 "population":{"query_ran":true},
                 "publisher_result_set":{"total":1,"returned":1,"maximum":8,"truncated":false},
                 "response_row_set":{"maximum":10,"returned":2,"truncated":false},
                 "hits":[{"work":"loi-2006-07-31-n2","lex_id":"lu-legilux:loi-2006-07-31-n2:2024-08-04--b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5","valid_from":"2024-08-04",
                          "match_reasons":["work_metadata"]},
                         "lu-legilux:another"]}
                """)!],
            readers);

        Assert.Contains("could not be read", page, StringComparison.Ordinal);
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The agreed lane table (MatchLanes plus tests/Lex.Tests/match-lane-cases.json) decides these,
    /// not this page. These cases pin what the PAGE does with each lane's verdict.
    ///
    /// Ambiguity arises only during identity resolution, so an ambiguous_ reason is identity and
    /// renders. I had it suppressed, which hid a real identification behind a notice saying the
    /// records matched only in metadata.
    /// </summary>
    [Theory]
    [InlineData("ambiguous_exact_identifier")]
    [InlineData("ambiguous_exact_title")]
    [InlineData("ambiguous_contained_title")]
    [InlineData("exact_identifier")]
    [InlineData("exact_title")]
    [InlineData("contained_publisher_short_title")]
    public void An_identity_lane_renders_and_says_it_matched_a_name(string reason)
    {
        var page = Rendered(reason);

        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("matched the name of this law, not its wording", page,
            StringComparison.Ordinal);
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A text lane is the answer itself and carries no qualifying badge. article_intent is text:
    /// the article-number sweep returns the provision, found by the same pass as the words.
    /// </summary>
    [Theory]
    [InlineData("keyword")]
    [InlineData("fuzzy")]
    [InlineData("semantic")]
    [InlineData("article_intent")]
    public void A_text_lane_renders_without_qualifying_the_match(string reason)
    {
        var page = Rendered(reason);

        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("matched the name of this law", page, StringComparison.Ordinal);
        Assert.DoesNotContain("matched only in metadata", page, StringComparison.Ordinal);
        Assert.DoesNotContain("match not classified", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unknown reason, and the two work-vector lanes the ruling leaves unclassified, render and
    /// are never suppressed, but the page may not assert a lane it has not been given. Both of
    /// these were suppressed as metadata by my own classifier.
    /// </summary>
    [Theory]
    [InlineData("semantic_work")]
    [InlineData("semantic_concept")]
    [InlineData("a_reason_this_page_has_never_seen")]
    public void An_unclassified_lane_renders_and_asserts_nothing(string reason)
    {
        var page = Rendered(reason);

        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("match not classified", page, StringComparison.Ordinal);
        // Never the metadata notice: only positively known metadata may trigger that.
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.DoesNotContain("matched the name of this law", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one lane that is never an answer, and the notice is a RESPONSE-level state rather than a
    /// per-publisher one. Beside a refusal it must also block the corpus-wide absence, because the
    /// page has just named records that matched.
    /// </summary>
    [Fact]
    public void A_positively_metadata_response_is_answered_with_the_notice_and_no_absence()
    {
        using var site = new NoticeSite(Path.Combine(_root, "metaonly"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [CompleteMetadataSearchResult()], readers);

        Assert.Contains(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.DoesNotContain("1 hit(s)", page, StringComparison.Ordinal);
        // The page has just named a matching record, so neither absence sentence may follow it.
        Assert.DoesNotContain("No match was returned", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No selected publisher ran this query", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// McpCore.MarkPublisherSet and McpCore.MarkResponseRows stamp complete, typed, identical
    /// receipts on every search unit. A response-wide claim must reject every shape those producer
    /// methods cannot create and every arithmetic contradiction inside an otherwise plausible one.
    /// </summary>
    [Theory]
    [InlineData("missing_response")]
    [InlineData("response_not_object")]
    [InlineData("response_wrong_type")]
    [InlineData("response_count_mismatch")]
    [InlineData("response_maximum_too_small")]
    [InlineData("response_maximum_above_bound")]
    [InlineData("missing_publisher")]
    [InlineData("publisher_not_object")]
    [InlineData("publisher_wrong_type")]
    [InlineData("publisher_truncated")]
    [InlineData("publisher_count_mismatch")]
    [InlineData("publisher_maximum_not_producer")]
    public void An_unproven_result_set_receipt_authorises_no_metadata_claim(string mutation)
    {
        using var site = new NoticeSite(Path.Combine(_root, "receipt-" + mutation), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };
        var result = CompleteMetadataSearchResult();

        switch (mutation)
        {
            case "missing_response":
                result.Remove("response_row_set");
                break;
            case "response_not_object":
                result["response_row_set"] = "complete";
                break;
            case "response_wrong_type":
                result["response_row_set"]!["returned"] = "1";
                break;
            case "response_count_mismatch":
                result["response_row_set"]!["returned"] = 0;
                break;
            case "response_maximum_too_small":
                result["response_row_set"]!["maximum"] = 0;
                break;
            case "response_maximum_above_bound":
                result["response_row_set"]!["maximum"] = 51;
                break;
            case "missing_publisher":
                result.Remove("publisher_result_set");
                break;
            case "publisher_not_object":
                result["publisher_result_set"] = false;
                break;
            case "publisher_wrong_type":
                result["publisher_result_set"]!["total"] = "1";
                break;
            case "publisher_truncated":
                result["publisher_result_set"]!["truncated"] = true;
                break;
            case "publisher_count_mismatch":
                result["publisher_result_set"]!["total"] = 2;
                break;
            case "publisher_maximum_not_producer":
                result["publisher_result_set"]!["maximum"] = 1;
                break;
            default:
                throw new InvalidOperationException(mutation);
        }

        var page = CatalogueEndpoints.RenderSearchResults([result], readers);

        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Divergent_global_receipts_authorise_no_metadata_claim()
    {
        using var site = new NoticeSite(Path.Combine(_root, "receipt-divergence"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader>
        {
            ["lu-legilux"] = reader,
            // The second unit is a refusal and contributes no disclosure row. The alias lets this
            // unit exercise the multi-publisher wire shape without constructing a second index.
            ["eu-eurlex"] = reader,
        };
        var records = CompleteMetadataSearchResult();
        records["publisher_result_set"]!["total"] = 2;
        records["publisher_result_set"]!["returned"] = 2;
        var refusal = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"eu-eurlex","status":"filter_not_supported_by_index"},
             "population":{"query_ran":false},"hits":[],
             "publisher_result_set":{"total":2,"returned":2,"maximum":8,"truncated":false},
             "response_row_set":{"maximum":11,"returned":1,"truncated":false}}
            """)!;

        var page = CatalogueEndpoints.RenderSearchResults([records, refusal], readers);

        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_unit_must_share_the_global_receipts()
    {
        using var site = new NoticeSite(Path.Combine(_root, "receipt-refusal"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader>
        {
            ["lu-legilux"] = reader,
            ["eu-eurlex"] = reader,
        };
        var records = CompleteMetadataSearchResult();
        records["publisher_result_set"]!["total"] = 2;
        records["publisher_result_set"]!["returned"] = 2;
        var refusal = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"eu-eurlex","status":"filter_not_supported_by_index"},
             "population":{"query_ran":false},"hits":[],
             "publisher_result_set":{"total":2,"returned":2,"maximum":8,"truncated":false},
             "response_row_set":{"maximum":10,"returned":1,"truncated":false}}
            """)!;

        var page = CatalogueEndpoints.RenderSearchResults([records, refusal], readers);

        Assert.Contains(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.DoesNotContain("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("filter_not_supported_by_index", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a_state_this_page_has_never_seen", false, false)]
    [InlineData("filter_not_supported_by_index", true, false)]
    [InlineData("filter_not_supported_by_index", false, true)]
    public void A_unit_the_search_producer_cannot_emit_blocks_the_metadata_claim(
        string status, bool queryRan, bool includeHit)
    {
        using var site = new NoticeSite(Path.Combine(_root, "receipt-status-" + status),
            includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader>
        {
            ["lu-legilux"] = reader,
            ["eu-eurlex"] = reader,
        };
        var records = CompleteMetadataSearchResult();
        records["publisher_result_set"]!["total"] = 2;
        records["publisher_result_set"]!["returned"] = 2;
        var unusable = new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "eu-eurlex", ["status"] = status,
            },
            ["population"] = new JsonObject { ["query_ran"] = queryRan },
            ["hits"] = includeHit
                ? new JsonArray(new JsonObject { ["work"] = "unusable" })
                : new JsonArray(),
            ["publisher_result_set"] = new JsonObject
            {
                ["total"] = 2, ["returned"] = 2, ["maximum"] = 8,
                ["truncated"] = false,
            },
            ["response_row_set"] = new JsonObject
            {
                ["maximum"] = 10, ["returned"] = includeHit ? 2 : 1,
                ["truncated"] = false,
            },
        };

        var page = CatalogueEndpoints.RenderSearchResults([records, unusable], readers);

        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_coherently_truncated_publisher_set_authorises_no_metadata_claim()
    {
        using var site = new NoticeSite(Path.Combine(_root, "receipt-publisher-page"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };
        var results = new JsonArray();
        var records = CompleteMetadataSearchResult();
        results.Add(records);
        for (var index = 1; index < 8; index++)
        {
            var publisher = $"publisher-{index}";
            readers[publisher] = reader;
            results.Add(new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = publisher,
                    ["status"] = "filter_not_supported_by_index",
                },
                ["population"] = new JsonObject { ["query_ran"] = false },
                ["hits"] = new JsonArray(),
            });
        }
        foreach (var unit in results.OfType<JsonObject>())
        {
            unit["publisher_result_set"] = new JsonObject
            {
                ["total"] = 9, ["returned"] = 8, ["maximum"] = 8,
                ["truncated"] = true,
            };
            unit["response_row_set"] = new JsonObject
            {
                ["maximum"] = 10, ["returned"] = 1, ["truncated"] = false,
            };
        }

        var page = CatalogueEndpoints.RenderSearchResults(results, readers);

        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// One unclassified hit beside metadata hits disables the notice for the whole response, so a
    /// hit nobody classified cannot be swept into a positive claim about all of them.
    /// </summary>
    [Fact]
    public void One_unclassified_hit_disables_the_response_level_metadata_claim()
    {
        using var site = new NoticeSite(Path.Combine(_root, "mixedlane"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse("""
                {"envelope":{"publisher":"lu-legilux","status":"ok"},
                 "population":{"query_ran":true},
                 "publisher_result_set":{"total":1,"returned":1,"maximum":8,"truncated":false},
                 "response_row_set":{"maximum":10,"returned":2,"truncated":false},
                 "hits":[{"work":"loi-2006-07-31-n2","lex_id":"lu-legilux:loi-2006-07-31-n2:2024-08-04--b23a72504925a2065967c3f3032ac905ae1ac921048419c5f8a1b54c1fec7ce5","valid_from":"2024-08-04",
                          "match_reasons":["work_metadata"]},
                         {"work":"lu-legilux:loi-2006-07-31-n2","valid_from":"2024-07-01",
                          "match_reasons":["semantic_work"]}]}
                """)!],
            readers);

        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.Contains("2 hit(s)", page, StringComparison.Ordinal);
    }

    /// <summary>One publisher, one hit, one reason: the shape most of these cases need.</summary>
    private string Rendered(string reason)
    {
        using var site = new NoticeSite(
            Path.Combine(_root, "lane-" + reason), includeAct: false);
        using var reader = site.Reader();
        return CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse(
                "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
                + "\"population\":{\"query_ran\":true},"
                + "\"publisher_result_set\":{\"total\":1,\"returned\":1,\"maximum\":8,\"truncated\":false},"
                + "\"response_row_set\":{\"maximum\":10,\"returned\":1,\"truncated\":false},\"hits\":[{"
                + "\"work\":\"lu-legilux:loi-2006-07-31-n2\",\"valid_from\":\"2024-08-04\","
                + "\"title\":\"Code du travail\",\"snippet\":\"Le contrat est suspendu.\","
                + "\"match_reasons\":[\"" + reason + "\"]}]}")!],
            new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader });
    }

    /// <summary>
    /// Round 2 O1. A sibling of the wrong shape was erased by OfType, and erasing it is how it
    /// became an absence: beside a refusal the page went on to state a corpus-wide no-match about a
    /// response it had thrown away unread.
    /// </summary>
    [Theory]
    [InlineData("\"a publisher answered\"")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("[{\"envelope\":{}}]")]
    public void A_sibling_of_the_wrong_shape_is_disclosed_not_erased(string sibling)
    {
        using var site = new NoticeSite(Path.Combine(_root, "sib"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var envelopes = (JsonArray)JsonNode.Parse("[" + sibling + "]")!;
        envelopes.Add(JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"filter_not_supported_by_index"},
             "population":{"query_ran":false}}
            """));

        var page = CatalogueEndpoints.RenderSearchResults(envelopes, readers);

        Assert.Contains("could not be read", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No selected publisher ran this query", page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("No match was returned", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Round 2 O2. An array of the wrong things is not an array of reasons. Checking only the
    /// container let a valid work through as "no wording reason", and the page then labelled it
    /// matched on its title, which the response never said.
    /// </summary>
    [Theory]
    [InlineData("[9,true]")]
    [InlineData("[\"keyword\",9]")]
    [InlineData("[null]")]
    [InlineData("[[\"keyword\"]]")]
    [InlineData("[{\"reason\":\"keyword\"}]")]
    public void An_array_of_the_wrong_things_is_not_an_array_of_reasons(string reasons)
    {
        using var site = new NoticeSite(Path.Combine(_root, "reasons"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var page = CatalogueEndpoints.RenderSearchResults(
            [(JsonObject)JsonNode.Parse(
                "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
                + "\"population\":{\"query_ran\":true},\"hits\":[{"
                + "\"work\":\"lu-legilux:loi-2006-07-31-n2\","
                + "\"match_reasons\":" + reasons + "}]}")!],
            readers);

        Assert.Contains("could not be read", page, StringComparison.Ordinal);
        Assert.DoesNotContain("matched on title, not wording", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Lex found records that match only in metadata", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// O3. An answer this page cannot classify is not an empty answer, and the difference is a
    /// corpus-wide claim. Hits that are present but not an array, an element that is not an
    /// object, and match_reasons present but not an array all used to collapse into an empty
    /// array, and an empty array here says nothing matched.
    /// </summary>
    [Theory]
    [InlineData("\"everything\"", "hits is a string")]
    [InlineData("7", "hits is a number")]
    [InlineData("{\"0\":{}}", "hits is an object")]
    [InlineData("[\"lu-legilux:x\"]", "a hit is not an object")]
    [InlineData("[{\"work\":\"lu-legilux:x\",\"match_reasons\":\"keyword\"}]",
                "match_reasons is a string")]
    [InlineData("[{\"work\":\"lu-legilux:x\",\"match_reasons\":{\"0\":\"keyword\"}}]",
                "match_reasons is an object")]
    public void An_answer_that_cannot_be_classified_is_not_an_empty_answer(
        string hits, string _)
    {
        using var site = new NoticeSite(Path.Combine(_root, "unreadable"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var malformed = (JsonObject)JsonNode.Parse(
            "{\"envelope\":{\"publisher\":\"lu-legilux\",\"status\":\"ok\"},"
            + "\"population\":{\"query_ran\":true},\"hits\":" + hits + "}")!;
        var refusal = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"filter_not_supported_by_index"},
             "population":{"query_ran":false}}
            """)!;

        var page = CatalogueEndpoints.RenderSearchResults([malformed, refusal], readers);

        Assert.Contains("This publisher's results could not be read.", page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("No match was returned", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No selected publisher ran this query", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The other side of O3: absent hits is a REAL empty result and must keep saying so, or the
    /// repair would have bought honesty about malformed answers by making the page mute about
    /// genuine ones.
    /// </summary>
    [Fact]
    public void An_absent_hits_field_is_still_an_empty_answer()
    {
        using var site = new NoticeSite(Path.Combine(_root, "reallyempty"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var silent = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"ok"},
             "population":{"query_ran":true}}
            """)!;
        var refusal = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"filter_not_supported_by_index"},
             "population":{"query_ran":false}}
            """)!;

        var page = CatalogueEndpoints.RenderSearchResults([silent, refusal], readers);

        Assert.Contains("No match was returned", page, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// O5. A whole-call refusal used to announce that the query did not run whatever its status
    /// said, including a status that reads as success. An unrecognised status tells the page the
    /// response was unusable and nothing at all about whether it ran.
    /// </summary>
    [Fact]
    public void Only_a_recognised_status_may_say_the_query_did_not_run()
    {
        foreach (var unknown in new[]
        {
            "{\"status\":\"ok\"}", "{\"status\":\"partial\"}",
            "{\"status\":\"a_state_this_page_has_never_seen\"}",
            "{\"status\":\"ok\",\"population\":{\"query_ran\":true}}",
        })
        {
            var card = TrustNotices.WholeCallRefusal((JsonObject)JsonNode.Parse(unknown)!);
            Assert.DoesNotContain("did not run", card, StringComparison.Ordinal);
            Assert.Contains("No usable result.", card, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"No usable result\"", card, StringComparison.Ordinal);
            // The status itself is still shown, so the reader is not left guessing what happened.
            Assert.Contains("<span class=\"mono\">", card, StringComparison.Ordinal);
        }

        // The two this page does recognise as non-executions, and the producer receipt that
        // overrides an unrecognised status.
        foreach (var denied in new[]
        {
            "{\"status\":\"no_corpus_mounted\"}", "{\"status\":\"unknown_publisher\"}",
            "{\"status\":\"ok\",\"population\":{\"query_ran\":false}}",
        })
        {
            var card = TrustNotices.WholeCallRefusal((JsonObject)JsonNode.Parse(denied)!);
            Assert.Contains("This query did not run.", card, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"This query did not run\"", card,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The other half of the same rule. A publisher that ran and presented nothing, beside one
    /// that refused, is the state the absence sentence exists for, so it must still appear.
    /// </summary>
    [Fact]
    public void A_page_that_presented_nothing_still_states_the_absence()
    {
        using var site = new NoticeSite(Path.Combine(_root, "absent"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        var empty = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"ok"},
             "population":{"query_ran":true},"hits":[]}
            """)!;
        var refusal = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"retrieval_mode_unavailable"},
             "population":{"query_ran":false}}
            """)!;

        var page = CatalogueEndpoints.RenderSearchResults([empty, refusal], readers);

        Assert.Contains("No match was returned", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Lex found records that match only in metadata", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Records matched and not one of them could be named. That is an answer the page could not
    /// read, not an empty one, and the difference decides what the page may say next.
    ///
    /// This test asserted the opposite until O3: it required the no-match sentence, which is a
    /// corpus-wide claim made on the strength of a response nobody parsed. Hostile field types are
    /// still read rather than thrown on; what changed is that unreadable no longer reads as empty.
    /// </summary>
    [Fact]
    public void A_record_card_with_no_nameable_work_announces_nothing()
    {
        using var site = new NoticeSite(Path.Combine(_root, "nameless"), includeAct: false);
        using var reader = site.Reader();
        var readers = new Dictionary<string, LexIndexReader> { ["lu-legilux"] = reader };

        // work is a number, which the strict reader declines, so no record can be named.
        var nameless = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"ok"},
             "population":{"query_ran":true},
             "hits":[{"work":7,"match":"work_identifier_or_title","match_reasons":[9,true]}]}
            """)!;
        var refusal = (JsonObject)JsonNode.Parse("""
            {"envelope":{"publisher":"lu-legilux","status":"filter_not_supported_by_index"},
             "population":{"query_ran":false}}
            """)!;

        // Hostile field types are read, not thrown on.
        var page = CatalogueEndpoints.RenderSearchResults([nameless, refusal], readers);

        Assert.DoesNotContain("Lex found records that match only in metadata", page,
            StringComparison.Ordinal);
        // The publisher is named and its answer is disclosed rather than dropped, so no heading
        // stands above silence.
        Assert.Contains("This publisher's results could not be read.", page,
            StringComparison.Ordinal);
        // And no corpus-wide claim either way. Neither sentence is knowable while a publisher
        // returned something the page failed to read.
        Assert.DoesNotContain("No match was returned", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No selected publisher ran this query", page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// H() does not make a destination safe to follow. Neither javascript: nor data: contains a
    /// character HtmlEncode touches, so ten href sites were encoding hostile URLs into working
    /// hostile links. The index-side scheme checks they leaned on are both conditional: the
    /// builder skips its own when the index has no provision-gap capability, and the reader gates
    /// its own on the current schema while still opening two older ones.
    /// </summary>
    [Fact]
    public void A_destination_is_linked_only_when_it_is_safe_to_follow()
    {
        foreach (var hostile in new[]
        {
            "javascript:alert(1)", "JaVaScRiPt:alert(1)", "data:text/html;base64,PHN2Zz4=",
            "vbscript:msgbox(1)", "http://legilux.public.lu/x", "//legilux.public.lu/x",
            "/relative/path", "not a uri", "", null,
        })
        {
            Assert.Null(Fragments.OfficialUri(hostile));
            // The label survives, because hiding it would deny that the index holds a source.
            var rendered = Fragments.OfficialLink(hostile, "official source");
            Assert.Equal("official source", rendered);
            Assert.DoesNotContain("<a ", rendered, StringComparison.Ordinal);
        }

        const string Official = "https://legilux.public.lu/eli/etat/leg/loi/2006/07/31/n2/jo";
        Assert.Equal(Official, Fragments.OfficialUri(Official));
        var link = Fragments.OfficialLink(Official, "official source");
        Assert.Contains($"href=\"{Official}\"", link, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", link, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sitemap is XML, and the collection, group and version keys were interpolated into loc
    /// elements raw. One ampersand in a slug makes the whole document non-well-formed, which costs
    /// a crawler every URL in it, not just the malformed one.
    /// </summary>
    [Fact]
    public async Task The_sitemap_is_well_formed_xml()
    {
        using var site = new NoticeSite(Path.Combine(_root, "sitemap"), includeAct: false);
        var xml = await site.Client.GetStringAsync("/sitemap.xml");

        var parsed = System.Xml.Linq.XDocument.Parse(xml);
        var locations = parsed.Descendants()
            .Where(e => e.Name.LocalName == "loc").Select(e => e.Value).ToList();
        Assert.NotEmpty(locations);
        // Every URL is absolute and lives under the configured base, never a request-shaped one.
        Assert.All(locations, l => Assert.StartsWith("https://example.test/", l, StringComparison.Ordinal));
        // Ordinary slugs are unchanged by the escaping, so this is a guard and not a rewrite.
        Assert.Contains(locations, l => l.Contains("/lu-legilux/loi-2006-07-31-n2", StringComparison.Ordinal));
    }

    /// <summary>
    /// The interval helpers carry docs.valid_from and valid_to, which are string columns and not
    /// DateOnly. A withdrawn row never passes ParseDate at build, and the read paths behind
    /// /provenance and the version rail do not exclude withdrawn rows, so their shape is not
    /// guaranteed here. Eleven render sites used these helpers and eight passed them through raw.
    /// </summary>
    [Fact]
    public void The_interval_helpers_encode_the_index_text_they_carry()
    {
        using var site = new NoticeSite(Path.Combine(_root, "interval"), includeAct: false);
        using var reader = site.Reader();
        var row = reader.ByKey("lu-legilux:loi-2006-07-31-n2:2024-08-04")!;
        const string Payload = "<script>alert(1)</script>";

        foreach (var rendered in new[]
        {
            Fragments.Interval(row with { ValidFrom = Payload }),
            Fragments.Interval(row with { ValidFrom = "2024-01-01", ValidTo = Payload }),
            Fragments.IntervalLabel(reader, row with { ValidFrom = Payload }),
            Fragments.IntervalLabel(reader, row with { ValidFrom = "2024-01-01", ValidTo = Payload }),
        })
        {
            Assert.DoesNotContain(Payload, rendered, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", rendered, StringComparison.Ordinal);
        }

        // An open interval still reads as open rather than as an encoded empty string.
        Assert.Contains("open", Fragments.Interval(row with { ValidTo = null }),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The producer's detail is reflected input: unknown_publisher embeds the publisher the reader
    /// typed. Interpolating it unescaped is a reflected XSS, which is what shipped in 482883d.
    /// </summary>
    [Fact]
    public async Task A_reflected_publisher_value_is_encoded()
    {
        using var site = new NoticeSite(Path.Combine(_root, "xss"), includeAct: false);
        var page = await site.Client.GetStringAsync(
            "/search?q=protection&publisher=%3Cscript%3Ealert(1)%3C/script%3E");

        Assert.DoesNotContain("<script>alert(1)</script>", page, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only an exact ok whose own receipt does not deny it counts as a run. An ok envelope carrying
    /// query_ran false describes a query nobody executed, so it may not produce a count.
    /// </summary>
    [Fact]
    public void An_execution_claim_needs_the_receipt_that_supports_it()
    {
        var denied = (JsonObject)JsonNode.Parse("{\"population\":{\"query_ran\":false}}")!;
        Assert.False(TrustNotices.QueryRan(denied));
        var refused = TrustNotices.SearchEnvelopeRefusal("ok", denied);
        Assert.Contains("Did not run.", refused, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"This publisher did not run the query\"", refused,
            StringComparison.Ordinal);

        // Without a receipt the page states what it knows, not that execution was skipped.
        // EVERY execution statement is gated, not just the lead: the body and the aria-label
        // both said the publisher did not run the query while the heading said only that no
        // result was usable, so a screen reader was told a non-execution the receipt did not
        // support. A malformed or absent receipt is not a receipt.
        foreach (var silent in new[]
        {
            "{}", "{\"population\":{}}", "{\"population\":{\"query_ran\":\"false\"}}",
            "{\"population\":{\"query_ran\":0}}", "{\"population\":{\"query_ran\":null}}",
            "{\"population\":{\"query_ran\":true}}", "{\"population\":\"query_ran\"}",
        })
        {
            var node = (JsonObject)JsonNode.Parse(silent)!;
            var card = TrustNotices.SearchEnvelopeRefusal("filter_not_supported_by_index", node);
            Assert.Contains("No usable result.", card, StringComparison.Ordinal);
            Assert.DoesNotContain("did not run", card, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"This publisher returned no usable result\"", card,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// O2. A whole-call refusal is a bare object, so the page must always render something for
    /// it. Returning null for a missing, non-string or empty status made the caller append
    /// nothing, and the reader was left with the search form above an empty page. A blank
    /// result area is the worst answer available, because it is the one a reader fills in.
    /// </summary>
    [Fact]
    public void A_whole_call_refusal_never_renders_nothing()
    {
        foreach (var bare in new[]
        {
            "{}", "{\"status\":\"\"}", "{\"status\":7}", "{\"status\":null}",
            "{\"status\":true}", "{\"status\":[\"unknown_publisher\"]}",
            "{\"status\":{\"value\":\"unknown_publisher\"}}", "{\"detail\":\"orphan\"}",
        })
        {
            var card = TrustNotices.WholeCallRefusal((JsonObject)JsonNode.Parse(bare)!);
            Assert.False(string.IsNullOrWhiteSpace(card), bare);
            Assert.Contains("No usable result.", card, StringComparison.Ordinal);
            // Nothing is known about execution here, so nothing is claimed about it.
            Assert.DoesNotContain("did not run", card, StringComparison.Ordinal);
            Assert.Contains("View coverage and known gaps", card, StringComparison.Ordinal);
        }

        // A named status still gets its own copy and may state the execution.
        var known = TrustNotices.WholeCallRefusal(
            (JsonObject)JsonNode.Parse("{\"status\":\"no_corpus_mounted\"}")!);
        Assert.Contains("This query did not run.", known, StringComparison.Ordinal);
        Assert.Contains("no verified legal index mounted", known, StringComparison.Ordinal);
    }

    /// <summary>
    /// The strict reader that replaced GetValue&lt;string&gt; on untrusted hit fields. GetValue
    /// THROWS on a number or a bool, so one malformed match or match_reasons element took the
    /// entire search page down with a 500 rather than being ignored as the non-string it is.
    /// </summary>
    [Fact]
    public void A_value_of_the_wrong_type_is_not_a_string_and_is_not_a_failure()
    {
        Assert.Equal("keyword", TrustNotices.Text(JsonNode.Parse("\"keyword\"")));
        Assert.Equal("", TrustNotices.Text(JsonNode.Parse("\"\"")));
        foreach (var hostile in new[] { "7", "true", "null", "[\"keyword\"]", "{\"a\":1}" })
            Assert.Null(TrustNotices.Text(JsonNode.Parse(hostile)));
        Assert.Null(TrustNotices.Text(null));
    }

    /// <summary>
    /// Fail closed on classification, and require the receipt rather than merely the absence of
    /// a denial. Reading hits before the status let a missing or malformed status, an ok
    /// carrying query_ran false, or a refusal arriving with rows, all render results or a count
    /// for a query nobody executed.
    ///
    /// An ok envelope with no readable query_ran used to pass, because the test was "not
    /// false". Absence of a denial is not a receipt. This is safe to require here because the
    /// only producer of this shape, SearchPopulation in McpCore, stamps query_ran on every
    /// search response and sets it true only on the executed path.
    /// </summary>
    [Fact]
    public void Only_an_exact_ok_with_an_exact_run_receipt_may_be_presented()
    {
        Assert.True(TrustNotices.Ran((JsonObject)JsonNode.Parse(
            "{\"envelope\":{\"status\":\"ok\"},\"population\":{\"query_ran\":true}}")!));

        foreach (var closed in new[]
        {
            // ok, but the producer's own receipt denies execution
            "{\"envelope\":{\"status\":\"ok\"},\"population\":{\"query_ran\":false}}",
            // ok with no receipt at all, or one that cannot be read as a bool
            "{\"envelope\":{\"status\":\"ok\"}}",
            "{\"envelope\":{\"status\":\"ok\"},\"population\":{}}",
            "{\"envelope\":{\"status\":\"ok\"},\"population\":{\"query_ran\":\"true\"}}",
            "{\"envelope\":{\"status\":\"ok\"},\"population\":{\"query_ran\":1}}",
            "{\"envelope\":{\"status\":\"ok\"},\"population\":{\"query_ran\":null}}",
            "{\"envelope\":{\"status\":\"ok\"},\"population\":[{\"query_ran\":true}]}",
            // a refusal that arrived carrying rows anyway
            "{\"envelope\":{\"status\":\"filter_not_supported_by_index\"},\"hits\":[{}]}",
            "{\"envelope\":{\"status\":\"retrieval_mode_unavailable\"}}",
            // no status at all, or one that is not a string
            "{}", "{\"envelope\":{}}", "{\"envelope\":{\"status\":7}}",
            "{\"envelope\":{\"status\":\"\"}}", "{\"envelope\":\"ok\"}",
        })
        {
            Assert.False(TrustNotices.Ran((JsonObject)JsonNode.Parse(closed)!), closed);
        }
    }

    private const string MetadataOnlyHeading = "No held text match";

    /// <summary>
    /// A query that matches only the record, never the wording, must not be answered with the
    /// record. Attack 41 proved this live twice: a speeding question returned tachograph and toll
    /// regulations, under status ok, because a title match was presented as an answer.
    /// </summary>
    [Fact]
    public async Task A_search_matching_only_metadata_is_answered_with_the_no_hit_card()
    {
        using var site = new NoticeSite(Path.Combine(_root, "meta-only"), includeAct: false);
        // The work slug matches the record and appears in no provision text. "travail" would
        // not do: it is in the wording of art. L. 121-6, so it is a genuine text hit.
        var page = await site.Client.GetStringAsync("/search?q=loi-2006-07-31-n2");

        // This query IS the work's identifier, so it takes the exact_identifier lane
        // (IndexReader.SearchWorksByIdentifierOrTitle). The reader named the law they want, and
        // answering that with "records that match only in metadata, not shown as text answers"
        // was both unhelpful and untrue about a precise identification.
        Assert.Contains("1 hit(s)", page, StringComparison.Ordinal);
        Assert.Contains("matched the name of this law, not its wording", page,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Lex found records that match only in metadata. They are not shown as text answers.",
            page, StringComparison.Ordinal);
        // What it must NOT claim is that the wording matched.
        Assert.DoesNotContain("matched on title, not wording", page, StringComparison.Ordinal);
        // The count-at-build rule forbids a population literal in copy, and the specification's
        // own figure is wrong: the never-consolidated set is 23,370, not ~24,579.
        Assert.DoesNotContain("24,579", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A query that reaches the wording is answered with the wording. Suppression must not become
    /// a general refusal to answer.
    /// </summary>
    [Fact]
    public async Task A_search_matching_provision_text_still_answers()
    {
        using var site = new NoticeSite(Path.Combine(_root, "text-hit"), includeAct: false);
        var page = await site.Client.GetStringAsync("/search?q=protection");

        Assert.DoesNotContain(MetadataOnlyHeading, page, StringComparison.Ordinal);
        Assert.Contains("loi-2006-07-31-n2", page, StringComparison.Ordinal);
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
        // No candidates means no held source URI to derive a publisher origin from, so the
        // phrase stays plain text rather than becoming a destination nobody verified.
        Assert.Contains("<span class=\"sub\">Search the official publisher</span>",
            page, StringComparison.Ordinal);
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
        // "the official publisher" points at the publisher, derived from a held source URI,
        // never at this product's own search wearing the publisher's name.
        Assert.Contains("href=\"https://example.test\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<a href=\"/search\">Search the official publisher</a>",
            page, StringComparison.Ordinal);
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
                // Without this the jurisdiction switch on /in-force-on falls to its default
                // branch and the Luxembourg population disclosure never renders, which made an
                // assertion about that copy pass against a page that never contained it.
                ["jurisdiction"] = "LU",
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
