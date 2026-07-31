using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Index;

// Lex.Mcp — MCP server over stdio (newline-delimited JSON-RPC).
// Seven tools (spec §9). Retrieves, filters, diffs, reports. Never summarises,
// paraphrases, explains or advises (F10): all natural language is the client model's job.

var indexDir = Environment.GetEnvironmentVariable("LEX_INDEX_DIR") ?? "indexes";
var readers = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);
if (Directory.Exists(indexDir))
    foreach (var db in Directory.EnumerateFiles(indexDir, "index-*.db"))
    {
        var r = LexIndexReader.Open(db);
        readers[r.Collection] = r;
    }
Console.Error.WriteLine($"[lex-mcp] mounted {readers.Count} index(es) from {indexDir}");

var jsonOut = new JsonSerializerOptions { WriteIndented = false };
Console.OutputEncoding = Encoding.UTF8;

string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonNode? msg;
    try { msg = JsonNode.Parse(line); } catch { continue; }
    var method = msg?["method"]?.GetValue<string>();
    var id = msg?["id"];
    if (method is null) continue;

    switch (method)
    {
        case "initialize":
            Reply(id, new JsonObject
            {
                ["protocolVersion"] = msg?["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "lex", ["version"] = "0.1.0" },
                ["instructions"] =
                    "Point-in-time regulatory text. Unknown document -> call search first, take lex_id from the hit, " +
                    "then as_of. The `work` parameter accepts a work-level lex_id (publisher:workkey), a version-level " +
                    "lex_id (version segment ignored), or a verbatim publisher identifier. Never treat a refusal status " +
                    "as an error: outside_observed_window / no_version_for_date / text_withheld are honest answers.",
            });
            break;
        case "notifications/initialized":
            break;
        case "ping":
            Reply(id, new JsonObject());
            break;
        case "tools/list":
            Reply(id, new JsonObject { ["tools"] = ToolDefs() });
            break;
        case "tools/call":
            try
            {
                var name = msg!["params"]!["name"]!.GetValue<string>();
                var a = msg["params"]!["arguments"] as JsonObject ?? new JsonObject();
                var result = CallTool(name, a);
                Reply(id, new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    }),
                });
            }
            catch (Exception ex)
            {
                Reply(id, new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"error: {ex.Message}" }),
                    ["isError"] = true,
                });
            }
            break;
        default:
            if (id is not null)
                Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"unknown method {method}" } });
            break;
    }
}

return;

void Reply(JsonNode? id, JsonNode result) =>
    Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

void Send(JsonNode n)
{
    Console.WriteLine(n.ToJsonString(jsonOut));
    Console.Out.Flush();
}

// ---------------------------------------------------------------- tools

JsonArray ToolDefs()
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
        Tool("as_of", "The state of one document as it stood on one date. Pure lookup, no ranking. Returns metadata, validity interval, hash, provenance link; text only where the publisher's text gate has cleared (else status=text_withheld with the official URL).",
            new JsonObject { ["work"] = S(workDesc), ["date"] = S("ISO date YYYY-MM-DD"), ["language"] = S("optional language code, e.g. fr") }, ["work", "date"]),
        Tool("timeline", "Every state a document has been in: validity intervals and version keys, publisher-asserted.",
            new JsonObject { ["work"] = S(workDesc), ["limit"] = I("max versions (default 100)"), ["offset"] = I("pagination offset") }, ["work"]),
        Tool("in_force_on", "The set of works in force on a date, computed from validity intervals at query time, deduplicated by work. Carries a mandatory population disclosure.",
            new JsonObject { ["date"] = S("ISO date"), ["publisher"] = S("optional publisher id, e.g. lu-legilux"), ["document_type"] = S("optional type code, e.g. CODE"), ["limit"] = I("default 50"), ["offset"] = I("pagination offset") }, ["date"]),
        Tool("diff", "What changed between two dates for one work. In metadata-only mode returns the interval/metadata delta and status=text_withheld (no text diff possible without stored bodies).",
            new JsonObject { ["work"] = S(workDesc), ["from_date"] = S("ISO date"), ["to_date"] = S("ISO date"), ["language"] = S("language code") }, ["work", "from_date", "to_date"]),
        Tool("search", "Filtered-then-ranked search over titles/metadata (FTS; filters always run before ranking). Returns hits WITHOUT body text: lex_id, dates, snippet, hash. Full state via as_of.",
            new JsonObject { ["query"] = S("search terms"), ["publisher"] = S("optional publisher id"), ["document_type"] = S("optional type code"), ["as_of"] = S("optional ISO date: only versions valid on this date"), ["limit"] = I("default 10") }, ["query"]),
        Tool("provenance", "Proof chain for one lex_id: source URI, retrieval time, record hash, event chain, corpus commit, index build, stamp signature.",
            new JsonObject { ["lex_id"] = S("full lex_id"), ["language"] = S("optional") }, ["lex_id"]),
        Tool("coverage", "What we hold and what we lack, tier by tier: counts, date ranges, history_begins, known gaps. This tool exists to say what we do NOT have.",
            new JsonObject { ["publisher"] = S("optional publisher id") }, []),
    ];
}

JsonObject Envelope(LexIndexReader r, string status, bool provisional = false) => new()
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

JsonObject DocJson(DocRow d, bool withText) => new()
{
    ["lex_id"] = d.Key,
    ["work"] = d.GroupKey,
    ["work_identifier"] = d.GroupIdentifier,
    ["document_type"] = d.Kind,
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

bool Provisional(LexIndexReader r, DateOnly d)
{
    var b = r.Stamp.GetValueOrDefault("built_at", "");
    return b.Length >= 10 && DateOnly.TryParse(b[..10], out var bd) && d > bd;
}

(LexIndexReader r, string norm)? Resolve(string work, string? publisher)
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

JsonNode CallTool(string name, JsonObject a)
{
    string? Str(string k) => a[k]?.GetValue<string>();
    int Int(string k, int dflt) => a[k] is { } n && int.TryParse(n.ToString(), out var v) ? v : dflt;

    switch (name)
    {
        case "as_of":
        {
            var work = Str("work") ?? throw new ArgumentException("work required");
            var date = DateOnly.Parse(Str("date") ?? throw new ArgumentException("date required"));
            var res = Resolve(work, Str("publisher"));
            if (res is null) return new JsonObject { ["status"] = "unknown_work", ["work"] = work };
            var (r, w) = res.Value;
            var doc = r.AsOf(w, date, new FilterSet(null, null, null, Str("language")));
            if (doc is null)
                return new JsonObject
                {
                    ["envelope"] = Envelope(r, r.WorkExists(w) ? "no_version_for_date" : "unknown_work", Provisional(r, date)),
                    ["work"] = w,
                    ["date"] = date.ToString("yyyy-MM-dd"),
                };
            var status = doc.TextPublic ? "ok" : "text_withheld";
            var o = new JsonObject
            {
                ["envelope"] = Envelope(r, status, Provisional(r, date)),
                ["document"] = DocJson(doc, withText: true),
            };
            if (status == "text_withheld")
                o["text_withheld_reason"] = "publisher text gate pending (metadata-only mode); read the official text at source_uri";
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
            var date = DateOnly.Parse(Str("date") ?? throw new ArgumentException("date required"));
            var limit = Int("limit", 50); var offset = Int("offset", 0);
            var pub = Str("publisher");
            var outp = new JsonArray();
            foreach (var r in readers.Values.Where(x => pub is null || x.Collection == pub))
            {
                var (rows, total) = r.InForceOn(date, new FilterSet(null, null, Str("document_type"), Str("language")), limit, offset);
                outp.Add(new JsonObject
                {
                    ["envelope"] = Envelope(r, "ok", Provisional(r, date)),
                    ["population"] = new JsonObject
                    {
                        ["basis"] = "versioned works only",
                        ["works_covered"] = r.Coverage().Groups,
                        ["known_exclusions"] = "~24,579 never-consolidated LU acts (not ingested; date coverage unmeasured — see coverage)",
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
            var from = DateOnly.Parse(Str("from_date")!);
            var to = DateOnly.Parse(Str("to_date")!);
            var res = Resolve(work, Str("publisher"));
            if (res is null) return new JsonObject { ["status"] = "unknown_work", ["work"] = work };
            var (r, w) = res.Value;
            var f = new FilterSet(null, null, null, Str("language"));
            var a1 = r.AsOf(w, from, f);
            var b1 = r.AsOf(w, to, f);
            if (a1 is null || b1 is null)
                return new JsonObject { ["envelope"] = Envelope(r, "no_version_for_date"), ["from_resolved"] = a1 is not null, ["to_resolved"] = b1 is not null };
            var changed = a1.Key != b1.Key;
            return new JsonObject
            {
                ["envelope"] = Envelope(r, a1.TextPublic && b1.TextPublic ? "ok" : "text_withheld"),
                ["changed"] = changed,
                ["from"] = DocJson(a1, false),
                ["to"] = DocJson(b1, false),
                ["note"] = changed
                    ? "different versions applied on the two dates; text diff requires the text gate to clear — compare at the official source URIs"
                    : "the same version applied on both dates",
            };
        }
        case "search":
        {
            var q = Str("query") ?? throw new ArgumentException("query required");
            DateOnly? asOf = Str("as_of") is { } s ? DateOnly.Parse(s) : null;
            var pub = Str("publisher");
            var limit = Int("limit", 10);
            var outp = new JsonArray();
            foreach (var r in readers.Values.Where(x => pub is null || x.Collection == pub))
            {
                var hits = r.Search(q, new FilterSet(asOf, null, Str("document_type"), Str("language")), limit * 4)
                    .GroupBy(h => h.Doc.GroupKey).Select(g => g.First()).Take(limit).ToList();
                outp.Add(new JsonObject
                {
                    ["envelope"] = Envelope(r, "ok"),
                    ["hits"] = new JsonArray(hits.Select(h =>
                    {
                        var d = DocJson(h.Doc, false);
                        d["snippet"] = h.Snippet;
                        return (JsonNode)d;
                    }).ToArray()),
                });
            }
            return outp;
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
                    { ["code"] = k.Kind, ["versions"] = k.Versions }).ToArray()),
                    ["known_gaps"] = new JsonArray(
                        "only the publisher's versioned (consolidated) corpus is ingested",
                        "~24,579 never-consolidated LU acts not ingested (date coverage unmeasured)",
                        "text bodies not stored (metadata-only mode); documents link to the official publication",
                        "honest coverage claim: dense and reliable from 2017 onward; sparse before; isolated snapshots back to 1849; forward to 2030"),
                });
            }
            return outp;
        }
        default:
            throw new ArgumentException($"unknown tool {name}");
    }
}
