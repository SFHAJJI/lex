using System.Text;
using System.Text.RegularExpressions;
using DiffPlex;
using Lex.Index;
using Markdig;
using static Lex.Web.PageShell;

namespace Lex.Web;

/// <summary>
/// Shared HTML fragments and formatting, lifted out of the route table.
///
/// Everything here is pure: it takes a row or a reader and returns a string. None of it touches
/// the request, the registry or configuration, which is precisely why it has no business sitting
/// between two route registrations.
///
/// Imported into Program.cs with <c>using static</c>, so every existing call site is unchanged
/// and the golden snapshots stay byte-identical across the move.
/// </summary>
public static class Fragments
{
    public static bool TryIsoDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

    // Publisher text is stored as deterministic Markdown. The React reader already parses it;
    // canonical/no-JavaScript pages must use the same presentation contract instead of exposing
    // Markdown punctuation. Raw HTML stays disabled because legal text is evidence, never markup
    // that a publisher (or a malformed source file) may execute in a reader's browser.
    private static readonly MarkdownPipeline LegalMarkdownPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .DisableHtml()
        .Build();

    private static readonly Regex UnparsedStrong = new(
        @"\*\*([^*\s<>\r\n](?:[^*<>\r\n]*?[^*\s<>\r\n])?)\*\*", RegexOptions.Compiled);
    private static readonly Regex UnparsedEmphasis = new(
        @"\*([^*\s<>\r\n](?:[^*<>\r\n]*?[^*\s<>\r\n])?)\*", RegexOptions.Compiled);

    // BaseUrl(HttpRequest) was deleted here. It built the printed connect command from the
    // X-Forwarded-Proto header and the Host header, both of which the requester controls.
    //
    // It was introduced for a real reason: req.Scheme is "http" in production, because Container
    // Apps terminates TLS at the ingress and forwards plain HTTP inside the environment, so the
    // page was handing out an insecure URL that a strict MCP client refuses. Reading the forwarded
    // header fixed the scheme and created two worse problems. Neither header was encoded, so a
    // hostile proxy or a smuggled request could put markup in the page. And a request carrying
    // Host: evil.example made /developers print a connect command pointing at evil.example, which
    // is Lex telling a reader to point their MCP client at somebody else's server.
    //
    // The configured public base answers the original question without asking the requester
    // anything. See ExplainerEndpoints, which is its only caller.

    public static string EnvelopeCard(LexIndexReader r, bool provisional) => $"""
        <div class="card"><table class="kv">
        <tr><td>tier</td><td>{H(r.Stamp.GetValueOrDefault("tier"))}, {(UsesPublisherVersionDates(r) ? "publisher-supplied consolidated wording-state dates" : "publisher-supplied applicability dates")}</td></tr>
        <tr><td>history begins</td><td>{H(r.Stamp.GetValueOrDefault("history_begins"))}</td></tr>
        <tr><td>index built</td><td class="mono">{H(r.Stamp.GetValueOrDefault("built_at"))} · corpus {H(r.Stamp.GetValueOrDefault("corpus_commit"))}</td></tr>
        <tr><td>stamp signature</td><td>{(r.SignatureValid ? "<span class=\"badge ok\">valid (ECDSA-P256)</span>" : "<span class=\"badge warn\">unsigned</span>")}</td></tr>
        {(provisional ? "<tr><td>provisional</td><td><span class=\"badge warn\">future-dated: a prediction from currently enacted text, revisable by any intervening amendment</span></td></tr>" : "")}
        </table></div>
        """;

    public static bool PublisherTextGateOpen(LexIndexReader reader) =>
        string.Equals(reader.Stamp.GetValueOrDefault("text_public"), "true",
            StringComparison.Ordinal);

    public static string ComparisonTextStatus(
        LexIndexReader reader, DocRow first, DocRow second) =>
        reader.ProvisionGapCount(LexIndexReader.RidOf(first)) > 0
        || reader.ProvisionGapCount(LexIndexReader.RidOf(second)) > 0
            ? "typed_text_gap"
        : !first.TextAvailable || !second.TextAvailable
        || PublisherTextGateOpen(reader) && (!first.TextPublic || !second.TextPublic)
            ? "text_not_available"
            : "text_withheld";

    public static string MissingTextBox(DocRow d, bool publisherTextGateOpen)
    {
        if (d.TextAvailable && !publisherTextGateOpen)
            return $"""
                <div class="notice"><b>Text withheld.</b> Lex holds publisher text for this version, but a publication gate
                prevents serving the wording (status <span class="mono">text_withheld</span>). Read the official text at
                <a href="{H(d.SourceUri)}" rel="noopener">{H(d.SourceUri)}</a>.</div>
                """;

        if (d.TextAvailable)
            return $"""
                <div class="notice"><b>Provision text not available.</b> Lex holds the official publisher file, but no
                non-whitespace provision body was safely derived for this version (status
                <span class="mono">text_not_available</span>). A heading alone is not presented as legal wording.
                Read the official text at
                <a href="{H(d.SourceUri)}" rel="noopener">{H(d.SourceUri)}</a>.</div>
                """;

        if (d.Kind is "RECUEIL" or "CODE_RECUEIL")
            return $"""
                <div class="notice"><b>Thematic collection, not one legal instrument.</b> This Legilux record groups
                member acts. Lex does not present its compilation PDF as one law or manufacture provision boundaries
                across those acts. Browse the official collection at
                <a href="{H(d.SourceUri)}" rel="noopener">{H(d.SourceUri)}</a>.</div>
                """;

        // Deliberately not a link. Publishers announce a consolidation before releasing any
        // document for it, so this address is frequently the publisher's own not-found page.
        // Offering it as "read the official record" invited the reader to conclude Lex had
        // built a wrong URL, when the accurate reading is that nothing has been published yet.
        return $"""
            <div class="notice"><b>Provision text not available.</b> Lex holds the publisher record and timeline, but no
            safely derived provision text for this version (status <span class="mono">text_not_available</span>).
            It will not manufacture article boundaries or wording.
            <p class="sub" style="margin:8px 0 0">The address the publisher reserves for this version is
            <span class="mono">{H(d.SourceUri)}</span>. Publishers announce a consolidation before releasing a
            document for it, so that address may return a not-found page until the text exists. Lex records the
            announcement rather than hiding the version, and does not present the address as readable until it is.</p></div>
            """;
    }

    public static bool IsProvisional(LexIndexReader r, DateOnly d)
    {
        var builtAt = r.Stamp.GetValueOrDefault("built_at", "");
        return builtAt.Length >= 10 && TryIsoDate(builtAt[..10], out var b) && d > b;
    }

    public static string DocTitle(DocRow d) => d.TitleShort ?? d.Title ?? d.GroupKey;

    private static readonly IReadOnlyDictionary<string, string> SourceClassLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = "Order", ["AGC"] = "Government-in-Council order",
            ["AGD"] = "Grand-ducal order", ["AMIN"] = "Ministerial order",
            ["ARGD"] = "Royal grand-ducal order", ["CODE"] = "Code",
            ["CODE_RECUEIL"] = "Code collection", ["CONV"] = "Convention",
            ["Constitution"] = "Constitution", ["DIV"] = "Other", ["LOI"] = "Law",
            ["ORD"] = "Ordinance", ["PA"] = "Administrative publication",
            ["PROT"] = "Protocol", ["RBCL"] = "Central Bank of Luxembourg regulation",
            ["RECUEIL"] = "Thematic collection", ["REG"] = "Regulation",
            ["RGC"] = "Government-in-Council regulation", ["RGD"] = "Grand-ducal regulation",
            ["RI"] = "Internal rules", ["RMIN"] = "Ministerial regulation",
            ["ST"] = "Statutes", ["TC"] = "Consolidated text",
            ["REG_DEL"] = "Delegated regulation", ["REG_IMPL"] = "Implementing regulation",
            ["DIR"] = "Directive", ["DIR_DEL"] = "Delegated directive",
            ["DIR_IMPL"] = "Implementing directive", ["DEC"] = "Decision",
            ["DEC_DEL"] = "Delegated decision", ["DEC_IMPL"] = "Implementing decision",
            ["DEC_ENTSCHEID"] = "Decision", ["TREATY"] = "Treaty",
            ["CORRIGENDUM"] = "Corrigendum",
        };

    public static string SourceClassLabel(string? sourceClass) =>
        string.IsNullOrWhiteSpace(sourceClass) ? "Not classified"
        : SourceClassLabels.GetValueOrDefault(sourceClass, "Source class");

    public static bool IsThematicCollection(string? sourceClass) =>
        sourceClass is "RECUEIL" or "CODE_RECUEIL";

    // Legilux prefixes every title with the consolidation it came from: "Version consolidée
    // applicable au DD/MM/YYYY : <real title>", in whichever language the expression is in.
    //
    // On a version row that prefix is noise, because the date is already its own column. In a
    // <title> tag it is worse than noise: 2,856 of the corpus's 2,934 titles carry it, so almost
    // every page in the site opened with the same nine words, and the name of the law, which is
    // the only part anyone types into a search box, sat past the point where a result gets
    // truncated. On a work page it is also simply wrong: that page spans every version, so
    // naming one consolidation date in its title describes something the page is not.
    //
    // Anchored on the four labels the publisher actually uses rather than on "anything before a
    // colon", because plenty of real titles contain a colon of their own. A label we have not
    // seen leaves the title untouched, which is the safe direction to fail in.
    private static readonly string[] ConsolidationLabels =
        ["Version consolidée", "Version rectifiée", "Konsolidierte", "Konsolidéiert"];

    public static string? StripConsolidationLabel(string? t)
    {
        if (string.IsNullOrEmpty(t)) return t;
        var i = t.IndexOf(" : ", StringComparison.Ordinal);
        if (i <= 0) return t;
        foreach (var label in ConsolidationLabels)
            if (t.StartsWith(label, StringComparison.OrdinalIgnoreCase)) return t[(i + 3)..];
        return t;
    }

    public static string? TitleShorten(string? t)
    {
        t = StripConsolidationLabel(t);
        if (string.IsNullOrEmpty(t)) return t;
        return t.Length > 110 ? t[..110].TrimEnd() + "…" : t;
    }

    // valid_from and valid_to are string columns, not DateOnly, and nothing guarantees their
    // shape at this point: CapabilityManifest filters withdrawn rows out BEFORE ParseDate, so a
    // withdrawn row's dates are never format-checked at build, and the ByKey and Timeline read
    // paths behind /provenance and the version rail carry no withdrawn predicate. These helpers
    // return HTML, like the rest of this module, so they encode here rather than relying on
    // eleven call sites to remember. Eight of them did not.
    public static string Interval(DocRow d) => d.ValidTo is null
        ? $"{H(d.ValidFrom)} → open"
        : $"{H(d.ValidFrom)} → {H(d.ValidTo)}";

    /// <summary>
    /// Which legal-time claim a publisher's version axis supports. New publishers declare this
    /// in the signed stamp; the EUR-Lex fallback keeps older verified artifacts honest during a
    /// rolling upgrade. An official consolidation date is not an entry-into-force date.
    /// </summary>
    public static bool UsesPublisherVersionDates(LexIndexReader r)
        => UsesPublisherVersionDates(r.Collection, r.Stamp);

    public static bool UsesPublisherVersionDates(string collection, IReadOnlyDictionary<string, string> stamp)
        => stamp.GetValueOrDefault("timeline_semantics") == "official_consolidation_state"
           || (!stamp.ContainsKey("timeline_semantics") && collection == "eu-eurlex");

    // One encode site, not one per branch. With the encoding duplicated across the two arms, a
    // mutation that stripped it from the publisher-version arm alone stayed green: the fixture
    // reader only ever takes the in-force arm, so half of this was untested. Sharing the encoding
    // makes that untestable branch unable to differ from the tested one.
    public static string IntervalLabel(LexIndexReader r, DocRow d)
    {
        var from = H(d.ValidFrom);
        var to = d.ValidTo is null ? null : H(d.ValidTo);
        return UsesPublisherVersionDates(r)
            ? $"publisher version {from} → {to ?? "latest held"}"
            : $"in force {from} → {to ?? "open"}";
    }

    public static string RenderLegalMarkdown(string text)
    {
        var html = Markdown.ToHtml(text ?? string.Empty, LegalMarkdownPipeline);

        // Frozen v1 publisher profiles can place an italic span directly between text spans,
        // yielding `word:*formula*word`. CommonMark leaves that delimiter literal. Valid
        // emphasis has already become <em>/<strong> here, so remove only balanced delimiters
        // that survived rendering; ordinary arithmetic such as `A * B` has no pair to match.
        html = CleanUnparsedEmphasis(html);

        // On phones, PageShell deliberately makes wide legal tables independently scrollable.
        // A scroll region must be keyboard-focusable too. Markdig owns this generated element,
        // so enrich its stable table opening here instead of post-processing whole pages or
        // making every ordinary layout card an unnecessary tab stop.
        return html.Replace("<table>",
            "<table tabindex=\"0\" aria-label=\"Scrollable legal table\">",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Render the emphasis used in publisher labels while guaranteeing an inline-only result.
    /// Encoding happens first, so a label can produce only text plus the two tags added here.
    /// </summary>
    public static string RenderLegalInline(string? text) =>
        UnparsedEmphasis.Replace(UnparsedStrong.Replace(H(text), "<strong>$1</strong>"), "<em>$1</em>");

    private static string CleanUnparsedEmphasis(string html) =>
        UnparsedEmphasis.Replace(UnparsedStrong.Replace(html, "$1"), "$1");

    /// <summary>
    /// Structural labels arrive beside the Markdown legal text and can inherit its emphasis
    /// delimiters. They are escaped heading text, not Markdown, so remove those presentation
    /// markers instead of showing punctuation such as <c>**Chapitre**</c> to readers.
    /// </summary>
    public static string PlainLegalLabel(string? text) =>
        (text ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal)
                              .Replace("*", string.Empty, StringComparison.Ordinal);

    public static string RenderDiff(string oldText, string newText)
    {
        var oldLines = oldText.Split('\n');
        var newLines = newText.Split('\n');

        // Consolidations share most content: trim the common prefix/suffix, then LCS the middle.
        int prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        int suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
               && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;

        var o = oldLines.AsSpan(prefix, oldLines.Length - prefix - suffix).ToArray();
        var n = newLines.AsSpan(prefix, newLines.Length - prefix - suffix).ToArray();

        var sb = new StringBuilder();
        sb.Append($"<p class=\"sub\">{o.Length:n0} line(s) in the old middle, {n.Length:n0} in the new; {prefix:n0} unchanged leading and {suffix:n0} trailing lines trimmed.</p>");

        if ((long)o.Length * n.Length > 2_250_000) // DP cap ≈ 1500×1500
        {
            var oldSet = o.ToHashSet(StringComparer.Ordinal);
            var newSet = n.ToHashSet(StringComparer.Ordinal);
            var removed = o.Where(l => !newSet.Contains(l)).Take(30).ToList();
            var added = n.Where(l => !oldSet.Contains(l)).Take(30).ToList();
            sb.Append("<div class=\"notice\">Change too large for a useful line-by-line page. Showing a small removed/added sample; use the structured article comparison above or the official source links.</div>");
            sb.Append("<div class=\"card\"><pre style=\"white-space:pre-wrap;font-size:13px;margin:0\">");
            foreach (var l in removed) sb.Append($"<span style=\"color:var(--accent)\">− {H(Trunc(l))}</span>\n");
            foreach (var l in added) sb.Append($"<span style=\"color:var(--ok)\">+ {H(Trunc(l))}</span>\n");
            sb.Append("</pre></div>");
            return sb.ToString();
        }

        // DiffPlex owns the commodity line-diff algorithm. Legal comparability, dated-version
        // selection and provision alignment have already happened before this presentation-only
        // fallback is called. Run it on the original strings so an inserted empty line remains a
        // real piece; serialising the trimmed arrays would make zero lines and one empty line
        // indistinguishable.
        var diff = Differ.Instance.CreateLineDiffs(oldText, newText, ignoreWhitespace: false);
        sb.Append("<div class=\"card\"><pre style=\"white-space:pre-wrap;font-size:13px;margin:0\">");
        var emitted = 0;
        const int maxEmit = 500;
        foreach (var block in diff.DiffBlocks)
        {
            // Preserve the established UI convention: new wording first, then replaced wording.
            for (var i = 0; i < block.InsertCountB && emitted < maxEmit; i++, emitted++)
                sb.Append($"<span style=\"color:var(--ok)\">+ {H(Trunc(diff.PiecesNew[block.InsertStartB + i]))}</span>\n");
            for (var i = 0; i < block.DeleteCountA && emitted < maxEmit; i++, emitted++)
                sb.Append($"<span style=\"color:var(--accent)\">− {H(Trunc(diff.PiecesOld[block.DeleteStartA + i]))}</span>\n");
            if (emitted >= maxEmit) break;
        }
        if (emitted >= maxEmit) sb.Append("<span class=\"sub\">… diff truncated at 500 changed lines …</span>\n");
        if (emitted == 0) sb.Append("<span class=\"sub\">(only whitespace-level differences in the extraction)</span>\n");
        sb.Append("</pre></div>");
        return sb.ToString();

        static string Trunc(string s) => s.Length > 300 ? s[..300] + "…" : s;
    }

    // ------------------------------------------------- routes

    public static string VersionRail(string publisher, string work, List<DocRow> versions, string? activeFrom)
    {
        var vs = versions.GroupBy(v => v.Key, StringComparer.Ordinal).Select(g => g.First())
                         .OrderBy(v => v.ValidFrom, StringComparer.Ordinal)
                         .ThenBy(v => v.Key, StringComparer.Ordinal).ToList();
        if (vs.Count < 2) return "";
        var ds = vs.Select(v => (v, ok: TryIsoDate(v.ValidFrom, out var d), d))
                   .Where(x => x.ok).Select(x => (x.v, x.d)).ToList();
        if (ds.Count < 2) return "";
        double lo = ds[0].d.DayNumber, hi = ds[^1].d.DayNumber, span = Math.Max(1, hi - lo);
        var sb = new StringBuilder("<div class=\"rail\"><div class=\"axis\"></div>");
        foreach (var (v, d) in ds)
        {
            var pct = (d.DayNumber - lo) / span * 98 + 1;
            var coordinate = VersionCoordinate(v);
            var act = coordinate == activeFrom ? " act" : "";
            sb.Append($"<a class=\"tick{act}\" style=\"left:{pct.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%\" "
                    + $"href=\"/{H(publisher)}/{H(work)}/{H(coordinate)}\" title=\"{H(coordinate)}\"></a>");
        }
        foreach (var (v, d) in new[] { ds[0], ds[^1] })
        {
            var pct = (d.DayNumber - lo) / span * 98 + 1;
            var align = v == ds[0].v ? "left:0" : "right:0";
            sb.Append($"<span class=\"yr\" style=\"{align}\">{H(v.ValidFrom)}</span>");
        }
        sb.Append("</div>");
        sb.Append($"<details class=\"railversions\"><summary>Browse {ds.Count} dated versions</summary><div class=\"vchips\">");
        foreach (var (v, _) in ds)
        {
            var coordinate = VersionCoordinate(v);
            var activeClass = coordinate == activeFrom ? " act" : "";
            var ariaCurrent = coordinate == activeFrom ? " aria-current=\"date\"" : "";
            sb.Append($"<a class=\"vchip{activeClass}\"{ariaCurrent} aria-label=\"Read version {H(coordinate)}\" "
                    + $"href=\"/{H(publisher)}/{H(work)}/{H(coordinate)}\">{H(v.ValidFrom)}</a>");
        }
        sb.Append("</div></details>");
        sb.Append($"<p class=\"sub railcap\">{ds.Count} versions · choose a date to read the law as it stood that day"
                + (activeFrom is null ? "" : " · <span class=\"nowmark\">▌</span> the one you are reading") + "</p>");
        return sb.ToString();
    }

    public static string VersionCoordinate(DocRow document)
        => document.Key[(document.Key.LastIndexOf(':') + 1)..];

    // ---- /stories: curated point-in-time narratives. Every figure is computed from the
    // mounted indexes at render time (a story that stops being true stops being shown).
}
