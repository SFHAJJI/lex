using System.Text.Json.Nodes;
using Lex.Index;

namespace Lex.Mcp;

/// <summary>
/// The MCP tools (spec §9) as a transport-agnostic core: the stdio server and the
/// public HTTP endpoint both dispatch through this class. Retrieves, filters, diffs,
/// reports — never summarises or advises (F10).
/// </summary>
public sealed class McpCore(IReadOnlyDictionary<string, LexIndexReader> readers)
{
    /// <summary>Handles one JSON-RPC message; returns the response node, or null for notifications.</summary>
    public JsonNode? HandleMessage(JsonNode msg)
    {
        var method = msg["method"]?.GetValue<string>();
        var id = msg["id"];
        if (method is null) return null;

        switch (method)
        {
            case "initialize":
                return Reply(id, new JsonObject
                {
                    ["protocolVersion"] = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "lex", ["version"] = "0.3.0" },
                    ["instructions"] =
                        "Point-in-time regulatory text (Luxembourg + EU). Unknown document -> call search first, " +
                        "take lex_id from the hit, then as_of. The `work` parameter accepts a work-level lex_id " +
                        "(publisher:workkey), a version-level lex_id (version segment ignored), or a verbatim " +
                        "publisher identifier. Refusal statuses (outside_observed_window / no_version_for_date / " +
                        "text_withheld) are honest answers, not errors.",
                });
            case "notifications/initialized":
                return null;
            case "ping":
                return Reply(id, new JsonObject());
            case "tools/list":
                return Reply(id, new JsonObject { ["tools"] = ToolDefs() });
            case "tools/call":
                try
                {
                    var name = msg["params"]!["name"]!.GetValue<string>();
                    var args = msg["params"]!["arguments"] as JsonObject ?? new JsonObject();
                    var result = CallTool(name, args);
                    return Reply(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                        }),
                    });
                }
                catch (Exception ex)
                {
                    return Reply(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"error: {ex.Message}" }),
                        ["isError"] = true,
                    });
                }
            default:
                return id is null ? null : new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"unknown method {method}" },
                };
        }
    }

    private static JsonNode Reply(JsonNode? id, JsonNode result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };

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
        JsonObject I(string d) => new() { ["type"] = "integer", ["description"] = d };

        var workDesc = "Work-level lex_id (publisher:workkey), version-level lex_id (version segment ignored), or verbatim publisher identifier. Unknown document -> call search first.";
        return
        [
            Tool("as_of", "The state of one document as it stood on one date. Pure lookup, no ranking. mode=outline lists the provisions (articles/annexes) without text — use it first on long documents; mode=select returns only the named anchors' text; mode=full (default) returns the whole text. Every provision carries its own permalink and hash.",
                new JsonObject
                {
                    ["work"] = S(workDesc), ["date"] = S("ISO date YYYY-MM-DD"),
                    ["language"] = S("optional language code, e.g. fr"),
                    ["mode"] = S("full | outline | select (default full)"),
                    ["anchors"] = S("comma-separated provision anchors for mode=select, e.g. art_1er,art_33"),
                }, ["work", "date"]),
            Tool("timeline", "Every state a document has been in: validity intervals and version keys, publisher-asserted.",
                new JsonObject { ["work"] = S(workDesc), ["limit"] = I("max versions (default 100)"), ["offset"] = I("pagination offset") }, ["work"]),
            Tool("in_force_on", "The set of works in force on a date, computed from validity intervals at query time, deduplicated by work. Carries a mandatory population disclosure.",
                new JsonObject { ["date"] = S("ISO date"), ["publisher"] = S("optional publisher id, e.g. lu-legilux"), ["document_type"] = S("optional type code, e.g. CODE"), ["limit"] = I("default 50"), ["offset"] = I("pagination offset") }, ["date"]),
            Tool("diff", "What changed between two dates for one work: which versions applied, and where both texts are held, retrieve them via as_of to compare.",
                new JsonObject { ["work"] = S(workDesc), ["from_date"] = S("ISO date"), ["to_date"] = S("ISO date"), ["language"] = S("language code") }, ["work", "from_date", "to_date"]),
            Tool("search", "Filtered-then-ranked full-text search (FTS; filters always run before ranking). Returns hits WITHOUT body text: lex_id, dates, snippet, hash. Full state via as_of.",
                new JsonObject { ["query"] = S("search terms"), ["publisher"] = S("optional publisher id"), ["document_type"] = S("optional type code"), ["as_of"] = S("optional ISO date: only versions valid on this date"), ["works"] = S("optional comma-separated work ids: restrict the search to these works, for callers that know their subject"), ["limit"] = I("default 10") }, ["query"]),
            Tool("article_history", "Every distinct text ONE provision (article/annex) has had, as validity intervals — plus its lifecycle events (inserted/removed/renumbered, renumbering detected mechanically by identical text hash). The answer to \"what did Article X say over its life / when did it change\".",
                new JsonObject { ["work"] = S(workDesc), ["anchor"] = S("provision anchor, e.g. art_1er (find it via search or as_of mode=outline)") }, ["work", "anchor"]),
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
                    ["document_type"] = S("optional type code(s), comma-separated; prefix with ! to exclude, e.g. !RECUEIL,!CODE_RECUEIL for instruments only"),
                    ["order"] = S("by_date (default) or by_churn"),
                    ["limit"] = I("default 20"),
                    ["offset"] = I("skip this many, for paging"),
                }, ["from_date", "to_date"]),
        ];
    }

    private JsonObject Envelope(LexIndexReader r, string status, bool provisional = false) => new()
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
            "eu-eurlex" => "ten acts only: GDPR, CRR, MiFID II, PSD2, SFDR, DORA, NIS2, AI Act, RED II, Electricity Market Directive. The wider consolidated acquis is not ingested",
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
        string? Str(string k) => a[k]?.GetValue<string>();
        int Int(string k, int dflt) => a[k] is { } n && int.TryParse(n.ToString(), out var v) ? v : dflt;

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
                if (res is null) return new JsonObject { ["status"] = "unknown_work", ["work"] = work };
                var (r, w) = res.Value;
                var doc = r.AsOf(w, date, new FilterSet(null, null, null, Str("language")));
                if (doc is null)
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, r.WorkExists(w) ? "no_version_for_date" : "unknown_work", ProvisionalFor(r, date)),
                        ["work"] = w,
                        ["date"] = date.ToString("yyyy-MM-dd"),
                    };
                var status = doc.TextPublic ? "ok" : "text_withheld";
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
                        o["provisions"] = new JsonArray(r.Provisions(rid)
                            .Select(p => (JsonNode)ProvisionJson(doc, p, withText: false)).ToArray());
                        break;
                    }
                    case "select":
                    {
                        var wanted = (Str("anchors") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (wanted.Length == 0) throw new ArgumentException("mode=select requires anchors");
                        o["document"] = DocJson(doc, withText: false);
                        var all = r.Provisions(rid);
                        var found = new JsonArray();
                        var missing = new JsonArray();
                        foreach (var anchorName in wanted)
                        {
                            var p = all.FirstOrDefault(x => x.Anchor == anchorName);
                            if (p is null) missing.Add((JsonNode)anchorName);
                            else found.Add((JsonNode)ProvisionJson(doc, p, withText: doc.TextPublic));
                        }
                        o["provisions"] = found;
                        if (missing.Count > 0)
                        {
                            o["anchors_not_in_version"] = missing;   // honest refusal per anchor
                            if (found.Count == 0) o["envelope"]!["status"] = "anchor_not_in_version";
                            // A truthful "no" that leaves the caller nowhere to go is how an
                            // assistant ends up guessing. Asked for art_1er of the Code du travail
                            // it got an empty list, fell back to full-text search, and answered out
                            // of the electricity act. The code has no Article 1 at all: it numbers
                            // its provisions L. 010-1, L. 111-1, and nothing in the reply said so.
                            var near = NearestAnchors(wanted, all);
                            if (near.Count > 0)
                                o["nearest_anchors"] = new JsonArray(near.Select(x => (JsonNode)x).ToArray());
                            o["anchors_in_version"] = all.Count;
                            o["anchor_note"] = near.Count > 0
                                ? "This work numbers its provisions in its own scheme. nearest_anchors are anchors it actually has; call as_of mode=select with one of those, or mode=outline for the full list. Do NOT fall back to full-text search for a provision of a known work."
                                : $"This version holds {all.Count} provisions under other anchors. Call as_of mode=outline to list them, then select from that list. Do NOT fall back to full-text search for a provision of a known work.";
                        }
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
                            o["document"] = DocJson(doc with { Body = doc.TextPublic ? r.BuildBody(doc) : null }, withText: true);
                        }
                        break;
                    }
                }
                if (status == "text_withheld")
                    o["text_withheld_reason"] = "publisher text gate pending; read the official text at source_uri";
                return o;
            }
            case "timeline":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return new JsonObject { ["status"] = "unknown_work", ["work"] = work };
                var (r, w) = res.Value;
                var rows = r.Timeline(w);
                if (rows.Count == 0) return new JsonObject { ["envelope"] = Envelope(r, "unknown_work"), ["work"] = w };
                var limit = Int("limit", 100); var offset = Int("offset", 0);
                return new JsonObject
                {
                    ["envelope"] = Envelope(r, "ok"),
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
                var outp = new JsonArray();
                foreach (var r in readers.Values.Where(x => pub is null || x.Collection == pub))
                {
                    var (rows, total) = r.InForceOn(date, new FilterSet(null, null, Str("document_type"), Str("language")), limit, offset);
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, "ok", ProvisionalFor(r, date)),
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
                if (res is null) return new JsonObject { ["status"] = "unknown_work", ["work"] = work };
                var (r, w) = res.Value;
                var f = new FilterSet(null, null, null, Str("language"));
                var a1 = r.AsOf(w, from, f);
                var b1 = r.AsOf(w, to, f);
                if (a1 is null || b1 is null)
                    return new JsonObject { ["envelope"] = Envelope(r, "no_version_for_date"), ["from_resolved"] = a1 is not null, ["to_resolved"] = b1 is not null };
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
                // and that case is already told the truth by text_withheld.
                var pa = a1.Profile;
                var pb = b1.Profile;
                var profilesDiffer = pa is not null && pb is not null && pa != pb;
                var comparable = !profilesDiffer && a1.TextPublic && b1.TextPublic;
                return new JsonObject
                {
                    ["envelope"] = Envelope(r, profilesDiffer ? "profiles_differ"
                                               : a1.TextPublic && b1.TextPublic ? "ok" : "text_withheld"),
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
            }
            case "search":
            {
                var q = Str("query") ?? throw new ArgumentException("query required");
                DateOnly? asOf = Str("as_of") is { } s ? DateOnly.Parse(s) : null;
                var pub = Str("publisher");
                var limit = Int("limit", 10);
                // Optional subject scope. Ranking a national corpus by relevance alone is precise
                // only when the question uses rare words: search all of Luxembourg law for "prix"
                // and seed certification outranks the electricity act. A caller that knows which
                // works it cares about can name them.
                var works = Str("works")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(w => w.Contains(':') ? w[(w.IndexOf(':') + 1)..] : w).ToArray();
                var outp = new JsonArray();
                foreach (var r in readers.Values.Where(x => pub is null || x.Collection == pub))
                {
                    // provision-level hits: the retrieval unit is the article; at most two
                    // provisions per work so one huge code cannot monopolize the result set
                    var hits = r.Search(q, new FilterSet(asOf, null, Str("document_type"), Str("language"), works), limit * 6)
                        .GroupBy(h => (h.Doc.GroupKey, h.Prov.Anchor)).Select(g => g.First())
                        .GroupBy(h => h.Doc.GroupKey).SelectMany(g => g.Take(2))
                        .Take(limit).ToList();
                    var hitsArr = new JsonArray(hits.Select(h =>
                    {
                        var d = DocJson(h.Doc, false);
                        d["anchor"] = h.Prov.Anchor;
                        d["provision_id"] = h.Prov.ProvisionId;
                        d["provision_num"] = h.Prov.Num;
                        d["provision_heading"] = h.Prov.Heading;
                        d["snippet"] = h.Snippet;
                        if (_publicBase is not null)
                            d["permalink"] = $"{_publicBase}/{h.Doc.Collection}/{h.Doc.GroupKey}/{h.Doc.ValidFrom}#{h.Prov.Anchor}";
                        return (JsonNode)d;
                    }).ToArray());

                    // Identifier/title fallback: works holding no per-article text are invisible
                    // to the provision FTS, and no indexed text contains a work's own identifier
                    // (a CELEX number lives in the slug) — both must still be findable.
                    if (hits.Count < limit)
                    {
                        var seen = hits.Select(h => h.Doc.GroupKey).ToHashSet(StringComparer.Ordinal);
                        // `works` has to reach this branch too. It did not, so a caller that named
                        // its subject and got few article hits was handed unrelated works to fill
                        // the quota — the documented scope silently ignored on exactly the path a
                        // scoped search is most likely to take.
                        foreach (var doc in r.SearchWorksByIdentifierOrTitle(q,
                                     new FilterSet(asOf, null, Str("document_type"), Str("language"), works), limit * 4)
                                 .GroupBy(x => x.GroupKey)
                                 .Select(g => g.OrderByDescending(x => x.ValidFrom, StringComparer.Ordinal).First())
                                 .Where(x => !seen.Contains(x.GroupKey))
                                 .Take(limit - hits.Count))
                        {
                            var d = DocJson(doc, false);
                            d["match"] = "work_identifier_or_title";
                            d["match_note"] = doc.TextPublic
                                ? "THIS WORK IS HELD by Lex, matched on its identifier or title rather than its wording. Call as_of on it for the text."
                                : "THIS WORK IS HELD by Lex — versions, dates and provenance are available via timeline / in_force_on / provenance. Only the per-article TEXT is absent (the publisher offers no machine-readable body). Never report this work as missing or unknown; report that the text specifically is not held.";
                            if (_publicBase is not null)
                                d["permalink"] = $"{_publicBase}/{doc.Collection}/{doc.GroupKey}/{doc.ValidFrom}";
                            hitsArr.Add(d);
                        }
                    }

                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, "ok"),
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
                if (res is null) return new JsonObject { ["status"] = "unknown_work", ["work"] = work };
                var (r, w) = res.Value;
                if (!r.WorkExists(w)) return new JsonObject { ["envelope"] = Envelope(r, "unknown_work"), ["work"] = w };
                var states = r.ProvisionStates(w, anchor);
                var evs = r.AnchorEvents(w, anchor);
                if (states.Count == 0 && evs.Count == 0)
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, r.HasProvisionHistory(w) ? "unknown_anchor" : "no_provision_history"),
                        ["work"] = w,
                        ["anchor"] = anchor,
                        ["hint"] = "list anchors via as_of with mode=outline",
                    };
                var statesArr = new JsonArray(states.Select(s =>
                {
                    var o = new JsonObject
                    {
                        ["valid_from"] = s.ValidFrom,
                        ["valid_to"] = s.ValidTo,
                        ["text_sha256"] = s.TextSha,
                        ["in_version"] = s.InVersion,
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
                    ["envelope"] = Envelope(r, "ok"),
                    ["work"] = w,
                    ["anchor"] = anchor,
                    ["distinct_texts"] = states.Count,
                    ["states"] = statesArr,
                    ["anchor_events"] = new JsonArray(evs.Select(e => (JsonNode)new JsonObject
                    {
                        ["type"] = e.EType,
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
                        ["envelope"] = Envelope(r, "ok"),
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
                return new JsonObject { ["status"] = "unknown_work", ["lex_id"] = key };
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
                        ["envelope"] = Envelope(r, hits.Count == 0 ? "no_result" : "ok"),
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
                // Comma-separated, and a leading "!" inverts: "!RECUEIL,!CODE_RECUEIL" asks for
                // everything that is not a thematic collection, which is what a reader means by
                // "laws" and would otherwise require naming every other type.
                var kinds = Str("document_type")
                    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var byChurn = string.Equals(Str("order"), "by_churn", StringComparison.OrdinalIgnoreCase);
                var limit = Int("limit", 20);
                var offset = Int("offset", 0);
                var outp = new JsonArray();
                foreach (var r in readers.Values.Where(x => pub is null || x.Collection == pub))
                {
                    var (works, versions) = r.ChangeTotals(from, to, kinds);
                    var rows = r.ChangesInPeriod(from, to, kinds, byChurn, limit, offset);
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, works == 0 ? "no_changes_in_period" : "ok"),
                        ["window"] = new JsonObject { ["from"] = from, ["to"] = to },
                        ["order"] = byChurn ? "by_churn" : "by_date",
                        ["works_changed"] = works,
                        ["new_versions"] = versions,
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
                                ["diff_from"] = c.Baseline ?? c.FirstChange,
                                ["diff_to"] = c.LastChange,
                            };
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
                        ["note"] = "a 'change' is a new consolidated version dated inside the window, as asserted by the publisher; use diff or as_of on a work to see the text",
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
                        ["envelope"] = Envelope(r, "ok"),
                        ["publisher_name"] = r.Stamp.GetValueOrDefault("publisher_name"),
                        ["works"] = c.Groups,
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
