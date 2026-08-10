using System.Text.Json.Nodes;
using Lex.Index;

namespace Lex.Mcp;

/// <summary>
/// The MCP tools (spec §9) as a transport-agnostic core: the stdio server and the
/// public HTTP endpoint both dispatch through this class. Retrieves, filters, diffs,
/// reports — never summarises or advises (F10).
/// </summary>
public sealed class McpCore(
    IReadOnlyDictionary<string, LexIndexReader> readers,
    string? artifactManifestIdentity = null)
{
    public JsonArray ToolDefs()
    {
        JsonObject Tool(string name, string desc, JsonObject props, string[] required) => new()
        {
            ["name"] = name,
            ["description"] = desc,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
            },
        };
        JsonObject S(string d) => new() { ["type"] = "string", ["description"] = d };
        JsonObject I(string d, int? minimum = null, int? maximum = null)
        {
            var value = new JsonObject { ["type"] = "integer", ["description"] = d };
            if (minimum is not null) value["minimum"] = minimum.Value;
            if (maximum is not null) value["maximum"] = maximum.Value;
            return value;
        }

        var workDesc = "Work-level lex_id (publisher:workkey), version-level lex_id (version segment ignored), or verbatim publisher identifier. Unknown document -> call search first.";
        return
        [
            Tool("as_of", "The state of one document as it stood on one date. Pure lookup, no ranking. mode=outline lists provisions without text and may be narrowed with anchors; mode=select returns only the named anchors' text; mode=full (default) returns the whole text. Every provision carries its own permalink and hash.",
                new JsonObject
                {
                    ["work"] = S(workDesc), ["date"] = S("ISO date YYYY-MM-DD"),
                    ["language"] = S("optional language code, e.g. fr"),
                    ["mode"] = S("full | outline | select (default full)"),
                    ["anchors"] = S("optional comma-separated provision anchors for mode=outline; required for mode=select, e.g. art_1er,art_33"),
                }, ["work", "date"]),
            Tool("timeline", "Every publisher state a document has been in: timeline intervals, version keys, and explicit timeline_semantics. Legilux intervals describe applicability; EUR-Lex intervals describe official consolidated wording states, not entry into force.",
                new JsonObject { ["work"] = S(workDesc), ["limit"] = I("max versions (default 100)"), ["offset"] = I("pagination offset") }, ["work"]),
            Tool("in_force_on", "Compatibility name for publisher states covering a date, computed from timeline intervals and deduplicated by work. Legilux states describe applicability; EUR-Lex states are official consolidated wording states and must not be called entry into force. Every envelope carries timeline_semantics and the result carries a mandatory population disclosure.",
                new JsonObject
                {
                    ["date"] = S("ISO date"), ["publisher"] = S("optional publisher id, e.g. lu-legilux"),
                    ["jurisdiction"] = S("optional jurisdiction code from index metadata, e.g. LU or EU"),
                    ["document_type"] = S("backward-compatible source document class filter"),
                    ["source_class"] = S("optional source document class"),
                    ["hierarchy"] = S("optional normalized legal hierarchy"),
                    ["act_form"] = S("optional legal act form"),
                    ["binding_status"] = S("optional binding status"),
                    ["domain"] = S("optional reviewed legal domain"),
                    ["language"] = S("optional language code"),
                    ["limit"] = I("default 50"), ["offset"] = I("pagination offset"),
                }, ["date"]),
            Tool("diff", "What changed between two dates for one work: which publisher versions cover the selected dates and, where both texts are held, retrieve them via as_of to compare. An optional held article anchor scopes the typed comparison workspace. Read timeline_semantics before describing legal applicability.",
                new JsonObject
                {
                    ["work"] = S(workDesc), ["from_date"] = S("ISO date"),
                    ["to_date"] = S("ISO date"), ["language"] = S("language code"),
                    ["anchor"] = S("optional held provision anchor returned by search, e.g. art_92"),
                }, ["work", "from_date", "to_date"]),
            Tool("search", "Filtered legal search. keyword is deterministic FTS5/BM25; hybrid adds the pinned local encoder and fixed RRF when verified vectors are mounted. No generative model participates. The response query_plan separates resolved work constraints, article and role intent, and residual provision terms. Returns hits WITHOUT body text; full state via as_of.",
                new JsonObject
                {
                    ["query"] = S("search terms"), ["publisher"] = S("optional publisher id"),
                    ["jurisdiction"] = S("optional jurisdiction code from index metadata, e.g. LU or EU"),
                    ["document_type"] = S("backward-compatible document type filter"),
                    ["source_class"] = S("optional source document class"),
                    ["hierarchy"] = S("optional normalized legal hierarchy"),
                    ["act_form"] = S("optional legal act form"),
                    ["binding_status"] = S("optional binding status"),
                    ["domain"] = S("optional reviewed legal domain id"),
                    ["language"] = S("optional language code"),
                    ["retrieval_mode"] = S("keyword or hybrid; default keyword until activation"),
                    ["time_scope"] = S("all_versions or as_of"),
                    ["as_of"] = S("ISO date required when time_scope=as_of"),
                    ["fuzzy"] = S("auto or off; visible fallback only"),
                    ["works"] = S("optional comma-separated work ids: restrict search to these works"),
                    ["limit"] = I("default 10; minimum 1, maximum 50", 1, 50),
                }, ["query"]),
            Tool("article_history", "Every distinct text ONE provision (article/annex) has had on its publisher timeline, plus lifecycle events (inserted/removed/renumbered, renumbering detected mechanically by identical text hash). Read timeline_semantics before calling an interval legal applicability. Answers \"what did Article X say over its life / when did it change\".",
                new JsonObject
                {
                    ["work"] = S(workDesc),
                    ["anchor"] = S("provision anchor, e.g. art_1er (find it via search or as_of mode=outline)"),
                    ["language"] = S("optional language code; defaults to the work's primary derived language"),
                }, ["work", "anchor"]),
            Tool("provenance", "Proof chain for one lex_id: source URI, retrieval time, record/body hashes, event chain, corpus commit, index build, stamp signature.",
                new JsonObject { ["lex_id"] = S("full lex_id"), ["language"] = S("optional") }, ["lex_id"]),
            Tool("coverage", "What we hold and what we lack, tier by tier: counts, date ranges, history_begins, known gaps. This tool exists to say what we do NOT have.",
                new JsonObject { ["publisher"] = S("optional publisher id") }, []),
            Tool("cited_by", "Which ARTICLES point at this law. The reverse of the cross-references the publisher writes into its own text (\"modifie par la loi du 4 juin 2020\"), captured at derive time. Answers \"what depends on this law\", \"who amended it\", \"is anything still referring to it\" — the question legal research is actually made of, and the one a search box cannot answer.",
                new JsonObject
                {
                    ["work"] = S("the law being cited, e.g. lu-legilux:loi-2020-06-04-a476"),
                    ["limit"] = I("default 50"),
                }, ["work"]),
            Tool("changes_in_period", "ACROSS the corpus: which works gained new versions between two dates, how many each, and when — the aggregate counterpart of diff/timeline (which cover ONE work). Use for \"what changed between 2025 and 2026\", \"which laws changed most during the pandemic\", \"what moved last month\". order=by_churn ranks by number of new versions; by_date (default) lists most recently changed first.",
                new JsonObject
                {
                    ["from_date"] = S("ISO date, start of window (inclusive)"),
                    ["to_date"] = S("ISO date, end of window (inclusive)"),
                    ["publisher"] = S("optional publisher id"),
                    ["jurisdiction"] = S("optional jurisdiction code from index metadata, e.g. LU or EU"),
                    ["document_type"] = S("optional source document class(es), comma-separated; prefix with ! to exclude, e.g. !RECUEIL,!CODE_RECUEIL for instruments only"),
                    ["source_class"] = S("optional source document class; alias of document_type"),
                    ["hierarchy"] = S("optional normalized legal hierarchy"),
                    ["act_form"] = S("optional legal act form"),
                    ["binding_status"] = S("optional binding status"),
                    ["domain"] = S("optional reviewed legal domain"),
                    ["language"] = S("optional language code"),
                    ["order"] = S("by_date (default) or by_churn"),
                    ["limit"] = I("default 20"),
                    ["offset"] = I("skip this many, for paging"),
                }, ["from_date", "to_date"]),
        ];
    }

    private JsonObject Envelope(LexIndexReader r, string status, bool provisional = false)
    {
        var envelope = new JsonObject
        {
            ["publisher"] = r.Collection,
            ["tier"] = r.Stamp.GetValueOrDefault("tier"),
            ["history_begins"] = r.Stamp.GetValueOrDefault("history_begins"),
            ["status"] = status,
            ["provisional"] = provisional,
            ["freshness"] = new JsonObject
            {
                ["corpus_commit"] = r.Stamp.GetValueOrDefault("corpus_commit"),
                ["built_at"] = r.Stamp.GetValueOrDefault("built_at"),
                ["last_confirmed_at"] = r.Stamp.GetValueOrDefault("built_at"),
                ["last_confirmed_source"] = "index-build",
                ["stamp_signature_valid"] = r.SignatureValid,
            },
        };
        var jurisdiction = r.Stamp.GetValueOrDefault("jurisdiction");
        if (!string.IsNullOrWhiteSpace(jurisdiction)) envelope["jurisdiction"] = jurisdiction;
        // Version coordinates do not all make the same legal claim. In particular, EUR-Lex
        // consolidated-expression dates are official wording-state dates, not entry-into-force
        // dates. New signed artifacts carry the field; the collection fallback keeps older
        // verified EU releases honest during rolling deployment.
        envelope["timeline_semantics"] = r.Stamp.GetValueOrDefault("timeline_semantics",
            r.Collection == "eu-eurlex" ? "official_consolidation_state" : "publisher_applicability");
        envelope["artifact"] = new JsonObject
        {
            ["manifest_set_id"] = artifactManifestIdentity,
            ["content_digest"] = r.Stamp.GetValueOrDefault("content_digest"),
            ["code_commit"] = r.Stamp.GetValueOrDefault("code_commit"),
            ["index_format"] = r.Stamp.GetValueOrDefault("index_format"),
        };
        return envelope;
    }

    private static string TextStatus(DocRow d) => !d.TextAvailable ? McpStatus.TextNotAvailable
        : d.TextPublic ? McpStatus.Ok : McpStatus.TextWithheld;

    private static string ComparisonTextStatus(DocRow a, DocRow b)
        => !a.TextAvailable || !b.TextAvailable ? McpStatus.TextNotAvailable
         : a.TextPublic && b.TextPublic ? McpStatus.Ok
         : McpStatus.TextWithheld;

    // Base URL for permalinks is deployment config only (never derived from or stored in
    // signed content); the field is omitted entirely when unconfigured so air-gapped
    // deployments do not emit unreachable URLs.
    private readonly string? _publicBase =
        Environment.GetEnvironmentVariable("LEX_PUBLIC_BASE_URL")?.TrimEnd('/');

    private JsonObject DocJson(DocRow d, bool withText)
    {
        var o = new JsonObject
        {
            ["lex_id"] = d.Key,
            ["work"] = d.GroupKey,
            ["work_identifier"] = d.GroupIdentifier,
            ["document_type"] = d.Kind,
            // How this version's text was obtained. "akn-lu/1" and "fmx4-eu/1" mean the article
            // boundaries came from the publisher's own structural markup; "pdf-lu/1" means they
            // were inferred from a page-description format, which is a weaker claim and has to
            // travel with the text rather than sit in a file nobody reads.
            ["extraction_profile"] = d.Profile,
            ["language"] = d.Language,
            ["valid_from"] = d.ValidFrom,
            ["valid_to"] = d.ValidTo,
            ["valid_time_source"] = d.ValidTimeSource,
            ["publication_date"] = d.PublicationDate,
            ["title"] = d.TitleShort ?? d.Title,
            ["withdrawn"] = d.Withdrawn,
            ["text_available"] = d.TextAvailable,
            ["record_sha256"] = d.RecordSha,
            ["body_sha256"] = d.BodySha,
            ["source_uri"] = d.SourceUri,
            ["observed_from"] = d.ObservedFrom,
            ["text"] = withText && d.TextPublic ? d.Body : null,
        };
        if (d.Hierarchy is not null) o["hierarchy"] = d.Hierarchy;
        if (d.Domains is not null) o["domains"] = new JsonArray(d.Domains.Trim('|').Split('|',
            StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode)x).ToArray());
        if (d.ActForm is not null) o["act_form"] = d.ActForm;
        if (d.BindingStatus is not null) o["binding_status"] = d.BindingStatus;
        if (d.ConsolidationStatus is not null) o["consolidation_status"] = d.ConsolidationStatus;
        if (_publicBase is not null && d.ValidFrom is not null)
            o["permalink"] = $"{_publicBase}/{d.Collection}/{d.GroupKey}/{d.ValidFrom}";
        return o;
    }

    private JsonObject ProvisionJson(DocRow d, ProvisionRow p, bool withText)
    {
        var o = new JsonObject
        {
            ["anchor"] = p.Anchor,
            ["provision_id"] = p.ProvisionId,
            ["type"] = p.PType,
            ["num"] = p.Num,
            ["heading"] = p.Heading,
            ["path"] = p.Path,
            ["article_valid_from"] = p.ArticleValidFrom,
            ["text_sha256"] = p.TextSha,
            ["text"] = withText ? p.TextMd : null,
        };
        // The publisher's own cross-references, captured at derive time. Only with the text, since
        // an outline is a table of contents and these belong to the words.
        if (withText)
        {
            var cits = readers.GetValueOrDefault(d.Collection)?.CitationsOf($"{d.Key}|{d.Language}|{d.ValidFrom}", p.Anchor);
            if (cits is { Count: > 0 })
                o["citations"] = new JsonArray(cits.Select(c => (JsonNode)new JsonObject
                { ["work"] = $"{d.Collection}:{c.Slug}", ["href"] = c.Href, ["text"] = c.Label }).ToArray());
        }
        if (_publicBase is not null)
            o["permalink"] = $"{_publicBase}/{d.Collection}/{d.GroupKey}/{d.ValidFrom}#{p.Anchor}";
        return o;
    }

    private static string KnownExclusions(LexIndexReader r) =>
        r.Stamp.GetValueOrDefault("known_exclusions") ?? r.Collection switch
        {
            "lu-legilux" => "never-consolidated LU acts (~24,579 as-published lois/RGD) are not ingested; ingestion scheduled — see coverage",
            // Named, not gestured at. "Flagship acts" tells a reader nothing about whether the act
            // they care about is here, and the front page used to promise "EU law" over the top
            // of it.
            "eu-eurlex" => $"{r.Coverage().Groups:n0} EU works from the reviewed scope are currently mounted; the wider acquis is not yet ingested, see coverage",
            _ => "see the coverage tool for this publisher's known gaps",
        };

    private static bool ProvisionalFor(LexIndexReader r, DateOnly d)
    {
        var b = r.Stamp.GetValueOrDefault("built_at", "");
        return b.Length >= 10 && DateOnly.TryParse(b[..10], out var bd) && d > bd;
    }

    /// <summary>
    /// Anchors a caller might have meant, for a mode=select miss.
    ///
    /// Two mechanical steps, no fuzzy string distance (which would rank "art_11" above "art_1"
    /// for the query "art_1"). First stay inside the same KIND of anchor: someone asking for an
    /// article wants articles, and an unfiltered digit match answered "art_1er" with
    /// "attachment_1", which is true and useless. Then match on digits rather than on words,
    /// because the mismatch is almost always a numbering convention: "article-5" against "art_5".
    ///
    /// When digits match nothing, return the first few anchors OF THAT KIND, which is the more
    /// valuable answer anyway: it shows the scheme in use. The Code du travail has no Article 1
    /// under any spelling, and seeing "art_l_010-1, art_l_111-1" says why in one line.
    /// </summary>
    private static List<string> NearestAnchors(IEnumerable<string> wanted, IReadOnlyList<ProvisionRow> all)
    {
        static string Digits(string s) => new string(s.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        static string Kind(string s)
        {
            var head = new string(s.TakeWhile(char.IsAsciiLetter).ToArray()).ToLowerInvariant();
            return head.StartsWith("art") ? "art" : head;   // art / article / arts all mean the same
        }

        var want = wanted.ToList();
        var kinds = want.Select(Kind).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        var family = kinds.Count > 0 ? all.Where(p => kinds.Contains(Kind(p.Anchor))).ToList() : [];
        if (family.Count == 0) family = [.. all];

        var keys = want.Select(Digits).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var hit = keys.Count > 0
            ? family.Where(p => keys.Contains(Digits(p.Anchor))).Select(p => p.Anchor).Take(10).ToList()
            : [];
        return hit.Count > 0 ? hit : family.Take(6).Select(p => p.Anchor).ToList();
    }

    private void AddSelectedProvisions(JsonObject output, DocRow document,
        IReadOnlyList<ProvisionRow> all, IReadOnlyList<string> wanted, bool withText)
    {
        var found = new JsonArray();
        var missing = new JsonArray();
        foreach (var anchorName in wanted)
        {
            var provision = all.FirstOrDefault(x => x.Anchor == anchorName);
            if (provision is null) missing.Add((JsonNode)anchorName);
            else found.Add((JsonNode)ProvisionJson(document, provision, withText));
        }

        output["provisions"] = found;
        if (missing.Count == 0) return;

        output["anchors_not_in_version"] = missing;
        if (found.Count == 0) output["envelope"]!["status"] = McpStatus.AnchorNotInVersion;
        var near = NearestAnchors(wanted, all);
        if (near.Count > 0)
            output["nearest_anchors"] = new JsonArray(near.Select(x => (JsonNode)x).ToArray());
        output["anchors_in_version"] = all.Count;
        output["anchor_note"] = near.Count > 0
            ? "This work numbers its provisions in its own scheme. nearest_anchors are anchors it actually has; call as_of mode=select with one of those, or mode=outline without anchors for the full list. Do NOT fall back to full-text search for a provision of a known work."
            : $"This version holds {all.Count} provisions under other anchors. Call as_of mode=outline without anchors to list them, then select from that list. Do NOT fall back to full-text search for a provision of a known work.";
    }

    private (LexIndexReader r, string norm)? Resolve(string work, string? publisher)
    {
        if (publisher is not null && readers.TryGetValue(publisher, out var rp)) return (rp, work);
        if (work.Contains(':') && !work.Contains("://"))
        {
            var pub = work.Split(':')[0];
            if (readers.TryGetValue(pub, out var rr)) return (rr, work);
        }
        foreach (var r in readers.Values)
            if (r.WorkExists(work)) return (r, work);
        return null;
    }

    public JsonNode CallTool(string name, JsonObject a)
    {
        // A build with no corpus must say so, once, in every answer.
        //
        // Source builds and incorrectly assembled releases can start without verified artifacts.
        // Before this guard every tool answered such a caller with `[]`: coverage said [], search
        // said [], and nothing distinguished "this law does not exist" from "no law is mounted".
        // A model reading that concludes the corpus is empty and tells its user so.
        //
        // Emptiness is the one answer this project is not allowed to give silently, and
        // coverage exists specifically "to say what we do NOT have" (F10). Saying it here
        // costs one dictionary lookup per call and turns a broken-looking server into one
        // that explains itself and names where the data actually is.
        if (readers.Count == 0)
            return new JsonObject
            {
                ["status"] = McpStatus.NoCorpusMounted,
                ["detail"] = "This server started with zero verified indexes, so it holds no law "
                           + "and cannot answer legal questions. The engine is available, but its "
                           + "signed corpus artifacts were not mounted.",
                ["hosted_endpoint"] = "https://law.soufien.lu/mcp",
                ["hosted_endpoint_note"] = "Public, no key, no signup. Call the coverage tool "
                           + "there for live mounted counts and known gaps.",
                ["to_build_a_local_index"] = "Download and verify a signed index release, or run "
                           + "the README pipeline (ingest -> derive -> catalog -> index -> sign), "
                           + "then point LEX_INDEX_DIR at the directory holding index-*.db.",
                ["index_dir_searched"] = Environment.GetEnvironmentVariable("LEX_INDEX_DIR") ?? "indexes",
                ["tool_called"] = name,
            };

        string? Str(string k) => a[k]?.GetValue<string>();
        int Int(string k, int dflt) => a[k] is { } n && int.TryParse(n.ToString(), out var v) ? v : dflt;
        int BoundedInt(string k, int dflt, int minimum, int maximum)
        {
            var value = Int(k, dflt);
            if (value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(k,
                    $"{k} must be between {minimum} and {maximum}");
            return value;
        }

        // Every required date goes through here. `diff` used to parse its two dates with the
        // null-forgiving operator, so a caller that omitted one, or spelled it `from` instead of
        // `from_date`, got "Value cannot be null. (Parameter 's')" back: a .NET internal leaked
        // to an API consumer, naming a parameter that does not exist in the tool schema. The
        // caller here is usually a model, and a model cannot act on that. It can act on being
        // told which argument it got wrong and what shape was expected.
        DateOnly Date(string k)
        {
            var raw = Str(k) ?? throw new ArgumentException($"{k} required (ISO date, YYYY-MM-DD)");
            return DateOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d : throw new ArgumentException($"{k} must be an ISO date (YYYY-MM-DD), got '{raw}'");
        }

        switch (name)
        {
            case "as_of":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var date = Date("date");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work };
                var (r, w) = res.Value;
                var doc = r.AsOf(w, date, new FilterSet(null, null, null, Str("language")));
                if (doc is null)
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, r.WorkExists(w) ? McpStatus.NoVersionForDate : McpStatus.UnknownWork, ProvisionalFor(r, date)),
                        ["work"] = w,
                        ["date"] = date.ToString("yyyy-MM-dd"),
                    };
                var status = TextStatus(doc);
                var mode = Str("mode") ?? "full";
                var o = new JsonObject
                {
                    ["envelope"] = Envelope(r, status, ProvisionalFor(r, date)),
                };
                var rid = LexIndexReader.RidOf(doc);
                switch (mode)
                {
                    case "outline":
                    {
                        o["document"] = DocJson(doc, withText: false);
                        var all = r.Provisions(rid);
                        var wanted = (Str("anchors") ?? "").Split(',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (wanted.Length == 0)
                            o["provisions"] = new JsonArray(all
                                .Select(p => (JsonNode)ProvisionJson(doc, p, withText: false)).ToArray());
                        else
                            AddSelectedProvisions(o, doc, all, wanted, withText: false);
                        break;
                    }
                    case "select":
                    {
                        var wanted = (Str("anchors") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (wanted.Length == 0) throw new ArgumentException("mode=select requires anchors");
                        o["document"] = DocJson(doc, withText: false);
                        var all = r.Provisions(rid);
                        // Keep refusal metadata identical in outline and select so a client can
                        // narrow a comparison without losing the honest next step.
                        AddSelectedProvisions(o, doc, all, wanted, withText: doc.TextPublic);
                        break;
                    }
                    default:
                    {
                        // full = every provision, with text. It used to return only the
                        // concatenated body, so `provisions` was the one thing full mode did
                        // NOT carry — while outline and select both did. Every client reading
                        // provisions uniformly (the workspace reader, the answer-to-view
                        // mapper) therefore saw an empty result for a document that has text,
                        // and reported "no text is held" for laws Lex holds in full. full is
                        // also as_of's DEFAULT mode, so this was the common path, not a corner.
                        //
                        // The text is returned exactly once, in the most structured form
                        // available: as provisions when the version is split into them, and as
                        // document.text when it is not.
                        var all = r.Provisions(rid);
                        if (all.Count > 0)
                        {
                            o["document"] = DocJson(doc, withText: false);
                            o["provisions"] = new JsonArray(all
                                .Select(p => (JsonNode)ProvisionJson(doc, p, withText: doc.TextPublic)).ToArray());
                        }
                        else
                        {
                            var body = doc.TextPublic ? r.BuildBody(doc) : null;
                            if (doc.TextPublic && string.IsNullOrWhiteSpace(body))
                            {
                                status = McpStatus.TextNotAvailable;
                                o["envelope"] = Envelope(r, status, ProvisionalFor(r, date));
                            }
                            o["document"] = DocJson(doc with { Body = body }, withText: true);
                        }
                        break;
                    }
                }
                if (status == McpStatus.TextWithheld)
                    o["text_withheld_reason"] = "publisher text gate pending; read the official text at source_uri";
                else if (status == McpStatus.TextNotAvailable)
                    o["text_unavailable_reason"] = "publisher record held; no safely derived provision text is available; read source_uri";
                return o;
            }
            case "timeline":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work };
                var (r, w) = res.Value;
                var rows = r.Timeline(w);
                if (rows.Count == 0) return new JsonObject { ["envelope"] = Envelope(r, McpStatus.UnknownWork), ["work"] = w };
                var limit = Int("limit", 100); var offset = Int("offset", 0);
                var provisional = rows.Any(row => DateOnly.TryParse(row.ValidFrom, out var validFrom)
                    && ProvisionalFor(r, validFrom));
                return new JsonObject
                {
                    ["envelope"] = Envelope(r, McpStatus.Ok, provisional),
                    ["work"] = w,
                    ["total_count"] = rows.Count,
                    ["truncated"] = rows.Count > offset + limit,
                    ["versions"] = new JsonArray(rows.Skip(offset).Take(limit).Select(v => (JsonNode)DocJson(v, false)).ToArray()),
                };
            }
            case "in_force_on":
            {
                var date = Date("date");
                var limit = Int("limit", 50); var offset = Int("offset", 0);
                var pub = Str("publisher");
                var jurisdiction = Str("jurisdiction");
                var outp = new JsonArray();
                foreach (var r in readers.Values.Where(x =>
                             (pub is null || x.Collection == pub)
                             && (jurisdiction is null || string.Equals(
                                 x.Stamp.GetValueOrDefault("jurisdiction"),
                                 jurisdiction,
                                 StringComparison.OrdinalIgnoreCase))))
                {
                    var filter = new FilterSet(null, null, Str("source_class") ?? Str("document_type"),
                        Str("language"), null, Str("hierarchy"), Str("act_form"),
                        Str("binding_status"), Str("domain"));
                    var (rows, total) = r.InForceOn(date, filter, limit, offset);
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, total == 0 ? McpStatus.NoResult : McpStatus.Ok,
                            ProvisionalFor(r, date)),
                        ["population"] = new JsonObject
                        {
                            ["basis"] = "versioned works only",
                            ["works_covered"] = r.Coverage().Groups,
                            ["known_exclusions"] = KnownExclusions(r),
                        },
                        ["total_works_in_force"] = total,
                        ["truncated"] = total > offset + limit,
                        ["works"] = new JsonArray(rows.Select(v => (JsonNode)DocJson(v, false)).ToArray()),
                    });
                }
                return outp;
            }
            case "diff":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var from = Date("from_date");
                var to = Date("to_date");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work };
                var (r, w) = res.Value;
                var f = new FilterSet(null, null, null, Str("language"));
                var a1 = r.AsOf(w, from, f);
                var b1 = r.AsOf(w, to, f);
                if (a1 is null || b1 is null)
                    return new JsonObject { ["envelope"] = Envelope(r, McpStatus.NoVersionForDate), ["from_resolved"] = a1 is not null, ["to_resolved"] = b1 is not null };
                var anchor = Str("anchor")?.Trim().ToLowerInvariant();
                if (anchor is { Length: > 0 } && (anchor.Length > 128
                    || !System.Text.RegularExpressions.Regex.IsMatch(anchor,
                        "^[a-z0-9][a-z0-9_.-]*$",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
                    throw new ArgumentException("anchor must be a provision anchor returned by search, e.g. art_92");
                if (anchor is { Length: > 0 })
                {
                    var anchors = r.Provisions(LexIndexReader.RidOf(a1))
                        .Concat(r.Provisions(LexIndexReader.RidOf(b1)))
                        .Select(provision => provision.Anchor)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    if (!anchors.Contains(anchor, StringComparer.Ordinal))
                        return new JsonObject
                        {
                            ["envelope"] = Envelope(r, McpStatus.UnknownAnchor),
                            ["work"] = w,
                            ["anchor"] = anchor,
                            ["anchors_not_in_version"] = new JsonArray(
                                anchors.Take(100).Select(value => (JsonNode)value).ToArray()),
                        };
                }
                var changed = a1.Key != b1.Key;

                // Two versions of the same work are only comparable provision by provision when
                // the same extraction profile produced both. The Code du travail is the proof:
                // its 2020 version came from pdf-lu/1 with 13 provisions anchored art_541-8, its
                // 2026 version from akn-lu/1 with 1,197 anchored art_l_010-1. Pair those by anchor
                // and you get "1,196 articles added", which is not a fact about Luxembourg law,
                // it is a fact about two parsers. Saying so is the whole point of F10: a caller
                // that is told the comparison is unsound can fall back to the source URIs, while
                // a caller handed a confident diff cannot know to.
                //
                // Only when BOTH profiles are known and they disagree. Profile is null when a
                // version carries no text at all, and two unknowns are not evidence of a
                // mismatch: claiming one would be the same overreach in the other direction,
                // and that case is already told the truth by text_not_available/text_withheld.
                var pa = a1.Profile;
                var pb = b1.Profile;
                var profilesDiffer = pa is not null && pb is not null && pa != pb;
                var comparable = !profilesDiffer && a1.TextPublic && b1.TextPublic;
                var output = new JsonObject
                {
                    ["envelope"] = Envelope(r, profilesDiffer ? McpStatus.ProfilesDiffer
                                               : ComparisonTextStatus(a1, b1),
                        ProvisionalFor(r, from) || ProvisionalFor(r, to)),
                    ["work"] = w,
                    ["changed"] = changed,
                    ["provision_level_comparable"] = comparable,
                    ["from"] = DocJson(a1, false),
                    ["to"] = DocJson(b1, false),
                    ["note"] = profilesDiffer
                        ? $"the two versions were extracted by different profiles ({pa ?? "unknown"} vs "
                          + $"{pb ?? "unknown"}), so their provisions carry different anchor schemes and "
                          + "cannot be paired: any provision-level diff between them would report "
                          + "differences created by the extraction, not by the legislator. Compare the "
                          + "full texts, or the official source URIs on each side, instead."
                        : changed
                            ? (a1.TextPublic && b1.TextPublic
                                ? "different versions applied; retrieve both via as_of (text included) to compare, or use the web diff permalink /{publisher}/{work}/diff/{from}/{to}"
                                : "different versions applied; text diff unavailable here — compare at the official source URIs")
                            : "the same version applied on both dates",
                };
                if (anchor is { Length: > 0 }) output["anchor"] = anchor;
                return output;
            }
            case "search":
            {
                var q = Str("query") ?? throw new ArgumentException("query required");
                if (q.Length > 1_000)
                    throw new ArgumentException("query must not exceed 1000 characters", "query");
                var timeScope = Str("time_scope") ?? (Str("as_of") is null ? "all_versions" : "as_of");
                if (timeScope is not ("all_versions" or "as_of")) throw new ArgumentException("time_scope must be all_versions or as_of");
                DateOnly? asOf = timeScope == "as_of"
                    ? DateOnly.Parse(Str("as_of") ?? throw new ArgumentException("as_of required when time_scope=as_of")) : null;
                var pub = Str("publisher");
                var jurisdiction = Str("jurisdiction");
                var limit = BoundedInt("limit", 10, 1, 50);
                var requestedMode = Str("retrieval_mode") ?? "keyword";
                if (requestedMode is not ("keyword" or "hybrid")) throw new ArgumentException("retrieval_mode must be keyword or hybrid");
                var fuzzy = Str("fuzzy") ?? "auto";
                if (fuzzy is not ("auto" or "off")) throw new ArgumentException("fuzzy must be auto or off");
                // Optional subject scope. Ranking a national corpus by relevance alone is precise
                // only when the question uses rare words: search all of Luxembourg law for "prix"
                // and seed certification outranks the electricity act. A caller that knows which
                // works it cares about can name them.
                var works = Str("works")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(w => w.Contains(':') ? w[(w.IndexOf(':') + 1)..] : w).ToArray();
                var outp = new JsonArray();
                var filter = new FilterSet(asOf, null, Str("source_class") ?? Str("document_type"),
                    Str("language"), works, Str("hierarchy"), Str("act_form"),
                    Str("binding_status"), Str("domain"));
                var executions = readers.Values.Where(x =>
                        (pub is null || x.Collection == pub)
                        && (jurisdiction is null || string.Equals(
                            x.Stamp.GetValueOrDefault("jurisdiction"),
                            jurisdiction,
                            StringComparison.OrdinalIgnoreCase)))
                    .Select(reader => (Reader: reader, Execution: requestedMode == "hybrid"
                        ? reader.SearchHybrid(q, filter, limit * 6, fuzzy == "auto")
                        : reader.SearchKeyword(q, filter, limit * 6, fuzzy == "auto")))
                    .ToList();
                var hasGlobalStrongWorkMatch = executions.Any(item =>
                    item.Execution.QueryPlan?.HasStrongWorkMatch == true);
                var globalResolutions = executions.SelectMany(item =>
                        (item.Execution.QueryPlan?.WorkResolutions ?? []).Select(resolution => new
                        {
                            resolution.Mention,
                            resolution.Status,
                            Candidates = resolution.Candidates
                                .Select(candidate => $"{item.Reader.Collection}:{candidate}"),
                        }))
                    .GroupBy(item => item.Mention, StringComparer.Ordinal)
                    .Select(group =>
                    {
                        var candidates = group.SelectMany(item => item.Candidates)
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                        var status = group.Any(item => item.Status == "ambiguous") || candidates.Length > 1
                            ? "ambiguous" : candidates.Length == 1 ? "resolved" : "unresolved";
                        return new { Mention = group.Key, Status = status, Candidates = candidates };
                    }).OrderBy(item => item.Mention, StringComparer.Ordinal).ToArray();
                var globalResolutionStatus = globalResolutions.Any(item => item.Status == "ambiguous")
                    ? "ambiguous"
                    : globalResolutions.Any(item => item.Status == "unresolved") ? "unresolved"
                    : globalResolutions.Any(item => item.Status == "resolved") ? "resolved"
                    : executions.Any(item => item.Execution.QueryPlan?.WorkCatalogAvailable == false)
                        ? "unavailable" : "not_requested";
                foreach (var (r, execution) in executions)
                {
                    // provision-level hits: the retrieval unit is the article; at most two
                    // provisions per work so one huge code cannot monopolize the result set
                    var suppressUnresolvedPublisher = hasGlobalStrongWorkMatch
                                                      && execution.QueryPlan?.HasStrongWorkMatch != true;
                    var hits = (suppressUnresolvedPublisher ? [] : execution.Hits)
                        .GroupBy(h => (h.Doc.GroupKey, h.Provision.Anchor)).Select(g => g.First())
                        .GroupBy(h => h.Doc.GroupKey).SelectMany(g => g.Take(2))
                        .Take(limit).ToList();
                    var hitsArr = new JsonArray(hits.Select(h =>
                    {
                        var d = DocJson(h.Doc, false);
                        d["anchor"] = h.Provision.Anchor.Length == 0 ? null : h.Provision.Anchor;
                        d["provision_id"] = h.Provision.ProvisionId;
                        d["provision_num"] = h.Provision.Num;
                        d["provision_heading"] = h.Provision.Heading;
                        d["snippet"] = h.Snippet;
                        d["match_reasons"] = new JsonArray(h.MatchReasons.Select(x => (JsonNode)x).ToArray());
                        if (_publicBase is not null)
                            d["permalink"] = $"{_publicBase}/{h.Doc.Collection}/{h.Doc.GroupKey}/{h.Doc.ValidFrom}"
                                + (h.Provision.Anchor.Length == 0 ? "" : $"#{h.Provision.Anchor}");
                        return (JsonNode)d;
                    }).ToArray());

                    // Identifier/title fallback: works holding no per-article text are invisible
                    // to the provision FTS, and no indexed text contains a work's own identifier
                    // (a CELEX number lives in the slug) — both must still be findable.
                    if (!suppressUnresolvedPublisher && hits.Count < limit)
                    {
                        var seen = hits.Select(h => h.Doc.GroupKey).ToHashSet(StringComparer.Ordinal);
                        var fallbackFilter = execution.QueryPlan?.HasStrongWorkMatch == true
                            ? filter with { Works = execution.QueryPlan.WorkConstraints }
                            : filter;
                        // `works` has to reach this branch too. It did not, so a caller that named
                        // its subject and got few article hits was handed unrelated works to fill
                        // the quota — the documented scope silently ignored on exactly the path a
                        // scoped search is most likely to take.
                        foreach (var doc in r.SearchWorksByIdentifierOrTitle(q,
                                     fallbackFilter, limit * 4)
                                 .GroupBy(x => x.GroupKey)
                                 .Select(g => g.OrderByDescending(x => x.ValidFrom, StringComparer.Ordinal).First())
                                 .Where(x => !seen.Contains(x.GroupKey))
                                 .Take(limit - hits.Count))
                        {
                            var d = DocJson(doc, false);
                            d["match"] = "work_identifier_or_title";
                            d["match_note"] = doc.TextPublic
                                ? "THIS WORK IS HELD by Lex, matched on its identifier or title rather than its wording. Call as_of on it for the text."
                                : doc.TextAvailable
                                    ? "THIS WORK IS HELD by Lex; publisher text exists but is not publicly served. Versions, dates and provenance remain available. Never report the work as missing."
                                    : "THIS WORK IS HELD by Lex; versions, publisher dates and provenance are available via timeline / in_force_on / provenance, but no safely derived provision text is available. Never report the work as missing or unknown; report the text gap precisely.";
                            if (_publicBase is not null)
                                d["permalink"] = $"{_publicBase}/{doc.Collection}/{doc.GroupKey}/{doc.ValidFrom}";
                            hitsArr.Add(d);
                        }
                    }

                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.Ok),
                        ["retrieval_mode"] = execution.RetrievalMode,
                        ["time_scope"] = timeScope,
                        ["as_of"] = asOf?.ToString("yyyy-MM-dd"),
                        ["query_plan"] = execution.QueryPlan is not { } plan ? null : new JsonObject
                        {
                            ["provision_query"] = plan.ProvisionQuery,
                            ["work_constraints"] = new JsonArray(
                                plan.WorkConstraints.Select(work => (JsonNode)work).ToArray()),
                            ["article_number"] = plan.ArticleNumber,
                            ["role_intent"] = plan.RoleIntent,
                            ["has_strong_work_match"] = plan.HasStrongWorkMatch,
                            ["work_resolution_status"] = plan.WorkResolutionStatus,
                            ["work_catalog_available"] = plan.WorkCatalogAvailable,
                            ["global_work_resolution_status"] = globalResolutionStatus,
                            ["global_work_resolutions"] = new JsonArray(globalResolutions
                                .Select(resolution => (JsonNode)new JsonObject
                                {
                                    ["mention"] = resolution.Mention,
                                    ["status"] = resolution.Status,
                                    ["candidates"] = new JsonArray(resolution.Candidates
                                        .Select(candidate => (JsonNode)candidate).ToArray()),
                                }).ToArray()),
                            ["work_resolutions"] = new JsonArray(
                                (plan.WorkResolutions ?? []).Select(resolution => (JsonNode)new JsonObject
                                {
                                    ["mention"] = resolution.Mention,
                                    ["status"] = resolution.Status,
                                    ["candidates"] = new JsonArray(resolution.Candidates
                                        .Select(candidate => (JsonNode)candidate).ToArray()),
                                }).ToArray()),
                        },
                        ["query_expansions"] = new JsonArray(execution.QueryExpansions.Select(x => (JsonNode)x).ToArray()),
                        ["artifact_manifest_id"] = Environment.GetEnvironmentVariable("LEX_ARTIFACT_MANIFEST_ID"),
                        ["hits"] = hitsArr,
                    });
                }
                return outp;
            }
            case "article_history":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var anchor = Str("anchor") ?? throw new ArgumentException("anchor required");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work };
                var (r, w) = res.Value;
                if (!r.WorkExists(w)) return new JsonObject { ["envelope"] = Envelope(r, McpStatus.UnknownWork), ["work"] = w };
                var requestedLanguage = Str("language");
                var states = r.ProvisionStates(w, anchor, requestedLanguage);
                var evs = r.AnchorEvents(w, anchor, requestedLanguage);
                if (states.Count == 0 && evs.Count == 0)
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, r.HasProvisionHistory(w) ? McpStatus.UnknownAnchor : McpStatus.NoProvisionHistory),
                        ["work"] = w,
                        ["anchor"] = anchor,
                        ["hint"] = "list anchors via as_of with mode=outline",
                    };
                var statesArr = new JsonArray(states.Select(s =>
                {
                    var document = s.InVersion is null ? null : r.ByKey(s.InVersion);
                    var o = new JsonObject
                    {
                        ["valid_from"] = s.ValidFrom,
                        ["valid_to"] = s.ValidTo,
                        ["language"] = s.Language,
                        ["text_sha256"] = s.TextSha,
                        ["in_version"] = s.InVersion,
                        ["source_uri"] = document?.SourceUri,
                        ["extraction_profile"] = document?.Profile,
                        ["record_sha256"] = document?.RecordSha,
                        ["body_sha256"] = document?.BodySha,
                    };
                    if (s.ArticleValidFrom is not null) o["article_valid_from"] = s.ArticleValidFrom;
                    if (s.ValidityConflict) o["validity_conflict"] = true;
                    if (_publicBase is not null && s.InVersion is not null)
                    {
                        var parts = s.InVersion.Split(':');
                        if (parts.Length >= 3)
                            o["permalink"] = $"{_publicBase}/{parts[0]}/{parts[1]}/{parts[2]}#{anchor}";
                    }
                    return (JsonNode)o;
                }).ToArray());
                return new JsonObject
                {
                    ["envelope"] = Envelope(r, McpStatus.Ok,
                        states.Any(state => DateOnly.TryParse(state.ValidFrom, out var validFrom)
                            && ProvisionalFor(r, validFrom))),
                    ["work"] = w,
                    ["anchor"] = anchor,
                    ["language"] = states.FirstOrDefault()?.Language ?? evs.FirstOrDefault()?.Language ?? requestedLanguage,
                    ["distinct_texts"] = states.Count,
                    ["states"] = statesArr,
                    ["anchor_events"] = new JsonArray(evs.Select(e => (JsonNode)new JsonObject
                    {
                        ["type"] = e.EType,
                        ["language"] = e.Language,
                        ["from"] = e.FromAnchor,
                        ["to"] = e.ToAnchor,
                        ["anchor"] = e.Anchor,
                        ["at_version"] = e.AtVersion,
                    }).ToArray()),
                };
            }
            case "provenance":
            {
                var key = Str("lex_id") ?? throw new ArgumentException("lex_id required");
                foreach (var r in readers.Values)
                {
                    var d = r.ByKey(key);
                    if (d is null) continue;
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.Ok),
                        ["document"] = DocJson(d, false),
                        ["events"] = new JsonArray(r.Events(key).Select(e => (JsonNode)new JsonObject
                        {
                            ["event"] = e.Event, ["scope"] = e.Scope, ["observed_from"] = e.ObservedFrom, ["detail"] = e.Detail,
                        }).ToArray()),
                        ["observations"] = new JsonArray(r.Observations(key, Str("language")).Select(o => (JsonNode)new JsonObject
                        {
                            ["language"] = o.Language, ["expr_valid_from"] = o.ExprValidFrom, ["sha256"] = o.Sha256,
                            ["observed_from"] = o.ObservedFrom, ["observed_to"] = o.ObservedTo,
                        }).ToArray()),
                        ["stamp"] = new JsonObject
                        {
                            ["signature_valid"] = r.SignatureValid,
                            ["algorithm"] = r.Stamp.GetValueOrDefault("algorithm"),
                            ["public_key"] = r.Stamp.GetValueOrDefault("public_key"),
                            ["signature"] = r.Stamp.GetValueOrDefault("signature"),
                        },
                    };
                }
                return new JsonObject { ["status"] = McpStatus.UnknownWork, ["lex_id"] = key };
            }
            case "cited_by":
            {
                var w = Str("work") ?? throw new ArgumentException("work required");
                var slug = w.Contains(':') ? w[(w.IndexOf(':') + 1)..] : w;
                var lim = Int("limit", 50);
                var outp = new JsonArray();
                foreach (var r in readers.Values)
                {
                    var hits = r.CitedBy(slug, lim);
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, hits.Count == 0 ? McpStatus.NoResult : McpStatus.Ok),
                        ["cited_work"] = w,
                        ["citing_articles"] = hits.Count,
                        ["citations"] = new JsonArray(hits.Select(h => (JsonNode)new JsonObject
                        {
                            ["work"] = $"{r.Collection}:{h.GroupKey}",
                            ["title"] = h.Title,
                            ["valid_from"] = h.ValidFrom,
                            ["anchor"] = h.Anchor,
                            ["num"] = h.Num,
                            ["permalink"] = _publicBase is null ? null
                                : $"{_publicBase}/{r.Collection}/{h.GroupKey}/{h.ValidFrom}#{h.Anchor}",
                        }).ToArray()),
                    });
                }
                return outp.Count == 1 ? outp[0]!.DeepClone() : outp;
            }

            case "changes_in_period":
            {
                var from = Str("from_date") ?? throw new ArgumentException("from_date required");
                var to = Str("to_date") ?? throw new ArgumentException("to_date required");
                if (string.CompareOrdinal(from, to) > 0) (from, to) = (to, from);
                var pub = Str("publisher");
                var jurisdiction = Str("jurisdiction");
                // Comma-separated, and a leading "!" inverts: "!RECUEIL,!CODE_RECUEIL" asks for
                // everything that is not a thematic collection, which is what a reader means by
                // "laws" and would otherwise require naming every other type.
                var kinds = (Str("source_class") ?? Str("document_type"))
                    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var filter = new FilterSet(null, null, null, Str("language"), null,
                    Str("hierarchy"), Str("act_form"), Str("binding_status"), Str("domain"));
                var byChurn = string.Equals(Str("order"), "by_churn", StringComparison.OrdinalIgnoreCase);
                var limit = Int("limit", 20);
                var offset = Int("offset", 0);
                var outp = new JsonArray();
                foreach (var r in readers.Values.Where(x =>
                             (pub is null || x.Collection == pub)
                             && (jurisdiction is null || string.Equals(
                                 x.Stamp.GetValueOrDefault("jurisdiction"),
                                 jurisdiction,
                                 StringComparison.OrdinalIgnoreCase))))
                {
                    var (works, versions) = r.ChangeTotals(from, to, kinds, filter);
                    var rows = r.ChangesInPeriod(from, to, kinds, byChurn, limit, offset, filter);
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r,
                            works == 0 ? McpStatus.NoChangesInPeriod : McpStatus.Ok,
                            DateOnly.TryParse(to, out var requestedTo) && ProvisionalFor(r, requestedTo)),
                        ["window"] = new JsonObject { ["from"] = from, ["to"] = to },
                        ["order"] = byChurn ? "by_churn" : "by_date",
                        ["works_changed"] = works,
                        ["new_versions"] = versions,
                        ["population"] = new JsonObject
                        {
                            ["basis"] = "distinct non-withdrawn works in the selected publisher and legal metadata scope",
                            ["works_in_scope"] = r.PopulationTotal(kinds, filter),
                            ["expected_works"] = r.Coverage().ExpectedWorks,
                            ["known_exclusions"] = KnownExclusions(r),
                        },
                        ["shown"] = rows.Count,
                        ["offset"] = offset,
                        ["changes"] = new JsonArray(rows.Select(c =>
                        {
                            var o = new JsonObject
                            {
                                ["work"] = $"{r.Collection}:{c.GroupKey}",
                                ["title"] = c.Title,
                                ["versions_in_period"] = c.VersionsInPeriod,
                                ["versions_total"] = c.VersionsTotal,
                                ["first_change"] = c.FirstChange,
                                ["last_change"] = c.LastChange,
                                // What this law looked like before the window touched it, and so
                                // the correct left-hand side of a diff. Comparing first_change
                                // with last_change is a comparison with itself whenever a work
                                // moved exactly once in the window, which is the usual case.
                                // Null when the window's first change is the work's own first
                                // version: there is no earlier state to show.
                                ["baseline"] = c.Baseline,
                                // 1 means the publisher reissued this act without altering a
                                // word, so "2 new versions" and "nothing changed" are both true.
                                ["distinct_texts"] = c.DistinctTexts,
                                ["wording_changed"] = c.DistinctTexts > 1,
                                ["text_comparable"] = c.TextComparable,
                                ["diff_from"] = c.Baseline ?? c.FirstChange,
                                ["diff_to"] = c.LastChange,
                            };
                            if (c.SourceClass is not null) o["source_class"] = c.SourceClass;
                            if (c.Hierarchy is not null) o["hierarchy"] = c.Hierarchy;
                            if (c.Domains is not null)
                                o["domains"] = new JsonArray(c.Domains.Trim('|').Split('|',
                                    StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode)x).ToArray());
                            if (c.ActForm is not null) o["act_form"] = c.ActForm;
                            if (c.BindingStatus is not null) o["binding_status"] = c.BindingStatus;
                            if (c.Language is not null) o["language"] = c.Language;
                            if (_publicBase is not null)
                            {
                                o["permalink"] = $"{_publicBase}/{r.Collection}/{c.GroupKey}/{c.LastChange}";
                                // Built from the baseline, so a law that moved exactly once still
                                // gets a comparison link. The old condition compared FirstChange
                                // with LastChange and therefore emitted no link at all for the
                                // most common row in the report.
                                var diffFrom = c.Baseline ?? c.FirstChange;
                                if (diffFrom != c.LastChange)
                                    o["diff_permalink"] = $"{_publicBase}/{r.Collection}/{c.GroupKey}/diff/{diffFrom}/{c.LastChange}";
                            }
                            return (JsonNode)o;
                        }).ToArray()),
                        ["note"] = "a row records publisher version dates inside the window; it does not by itself assert a wording change, legal effect, or entry into force; use diff or as_of to inspect text",
                    });
                }
                return outp;
            }
            case "coverage":
            {
                var pub = Str("publisher");
                var outp = new JsonArray();
                foreach (var r in readers.Values.Where(x => pub is null || x.Collection == pub))
                {
                    var c = r.Coverage();
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.Ok),
                        ["publisher_name"] = r.Stamp.GetValueOrDefault("publisher_name"),
                        ["works"] = c.Groups,
                        ["scope_expected_works"] = c.ExpectedWorks,
                        ["build_inventory_status"] = c.ExpectedWorks is null ? "unavailable"
                            : c.Groups > c.ExpectedWorks ? "overfull"
                            : c.Groups == c.ExpectedWorks && (c.BuildIssues?.Count ?? 0) == 0
                                ? "complete" : "incomplete",
                        ["build_complete"] = c.ExpectedWorks is null ? null
                            : c.Groups == c.ExpectedWorks && (c.BuildIssues?.Count ?? 0) == 0,
                        ["build_issues"] = new JsonArray((c.BuildIssues ?? [])
                            .Select(issue => (JsonNode)new JsonObject
                            {
                                ["code"] = issue.Code,
                                ["work"] = issue.Work,
                                ["detail"] = issue.Detail,
                            }).ToArray()),
                        ["versions"] = c.Rows,
                        ["valid_from_earliest"] = c.EarliestValidFrom,
                        ["valid_from_latest"] = c.LatestValidFrom,
                        ["document_types"] = new JsonArray(c.Kinds.Select(k => (JsonNode)new JsonObject
                        { ["code"] = k.Kind, ["versions"] = k.Versions, ["versions_with_text"] = k.WithText }).ToArray()),
                        // Which languages this corpus is in, and how rarely the same law exists
                        // in more than one. A caller planning a language filter needs to know
                        // that picking one here does not narrow a translation, it removes a
                        // publisher: Luxembourg publishes in French, the EU acts held here are
                        // English, and almost no work carries both.
                        ["languages"] = new JsonArray(c.Languages.Select(l => (JsonNode)new JsonObject
                        { ["code"] = l.Language, ["works"] = l.Works, ["versions"] = l.Versions }).ToArray()),
                        ["multilingual_works"] = c.MultilingualWorks,
                        ["text"] = new JsonObject
                        {
                            ["versions_with_text_served"] = c.TextServed,
                            ["versions_without_text"] = c.Rows - c.TextServed,
                            ["note"] = "text availability is per version (text_available/text_public on each document); versions without text carry the official source link",
                        },
                        ["known_gaps"] = new JsonArray(
                            KnownExclusions(r),
                            r.Collection == "lu-legilux"
                                ? "coverage density follows the publisher's own digitised consolidations: dense from 2017 onward; sparse before; isolated snapshots back to 1849; forward-dated to 2030"
                                : "coverage follows the publisher's consolidation practice; future-dated versions are provisional"),
                    });
                }
                return outp;
            }
            default:
                throw new ArgumentException($"unknown tool {name}");
        }
    }
}
