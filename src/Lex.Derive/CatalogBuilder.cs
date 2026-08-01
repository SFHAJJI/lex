using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.Derive;

/// <summary>
/// Post-pass over the derived layer: per-work work.json (version + anchor inventory),
/// per-work history.json (per-anchor distinct text states with validity intervals —
/// "article X has had exactly N texts" as a file read), and a global catalog.json.
/// Pure function of the derived fr.json files; no corpus or network access.
/// </summary>
public static class CatalogBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record Stats(int Works, int Anchors, int HistoryStates);

    public static Stats Build(string articlesRoot)
    {
        int workCount = 0, anchorCount = 0, stateCount = 0;
        var catalog = new JsonArray();

        foreach (var pubDir in Directory.EnumerateDirectories(articlesRoot)
                     .Where(d => Directory.Exists(Path.Combine(d, "works")))
                     .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            var publisher = Path.GetFileName(pubDir);
            foreach (var workDir in Directory.EnumerateDirectories(Path.Combine(pubDir, "works"))
                         .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
            {
                var slug = Path.GetFileName(workDir);
                var versionsDir = Path.Combine(workDir, "versions");
                if (!Directory.Exists(versionsDir)) continue;

                // (validFrom, language) -> parsed derived json
                var versions = new List<(string ValidFrom, string Lang, JsonNode Doc)>();
                foreach (var vDir in Directory.EnumerateDirectories(versionsDir)
                             .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
                    foreach (var jf in Directory.EnumerateFiles(vDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                        versions.Add((Path.GetFileName(vDir), Path.GetFileNameWithoutExtension(jf),
                            JsonNode.Parse(File.ReadAllText(jf))!));
                if (versions.Count == 0) continue;
                workCount++;

                string? title = null;
                var versionArr = new JsonArray();
                var allAnchors = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var (validFrom, lang, doc) in versions)
                {
                    title ??= doc["title"]?.GetValue<string>();
                    var provs = (doc["provisions"] as JsonArray)!.OfType<JsonObject>().ToList();
                    foreach (var p in provs) allAnchors.Add(p["anchor"]!.GetValue<string>());
                    versionArr.Add(new JsonObject
                    {
                        ["valid_from"] = validFrom,
                        ["valid_to"] = doc["valid_to"]?.DeepClone(),
                        ["language"] = lang,
                        ["provisions"] = provs.Count,
                    });
                }
                anchorCount += allAnchors.Count;

                // ---- history.json: per anchor, consecutive same-sha runs collapse to intervals.
                // Publisher-asserted article dates ride along; disagreement between the asserted
                // date and the observed interval start is disclosed, never silently resolved (§3.3).
                var history = new JsonObject();
                foreach (var anchor in allAnchors)
                {
                    var states = new JsonArray();
                    string? runSha = null, runFrom = null, runTo = null, runVersion = null, runArticleDate = null;
                    void Flush()
                    {
                        if (runSha is null) return;
                        var state = new JsonObject
                        {
                            ["valid_from"] = runFrom,
                            ["valid_to"] = runTo,
                            ["text_sha256"] = runSha,
                            ["in_version"] = runVersion,
                        };
                        if (runArticleDate is not null)
                        {
                            state["article_valid_from"] = runArticleDate;
                            if (runArticleDate != runFrom) state["validity_conflict"] = true;
                        }
                        states.Add(state);
                        runSha = null; runArticleDate = null;
                    }
                    foreach (var (validFrom, _, doc) in versions)
                    {
                        var p = (doc["provisions"] as JsonArray)!.OfType<JsonObject>()
                            .FirstOrDefault(x => x["anchor"]!.GetValue<string>() == anchor);
                        var validTo = doc["valid_to"]?.GetValue<string>();
                        if (p is null) { Flush(); continue; }        // anchor absent: close any open run
                        var sha = p["text_sha256"]!.GetValue<string>();
                        if (sha != runSha)
                        {
                            Flush();
                            runSha = sha;
                            runFrom = validFrom;
                            runVersion = doc["lex_id"]?.GetValue<string>();
                            runArticleDate = p["article_valid_from"]?.GetValue<string>();
                        }
                        runTo = validTo;                              // extends with every version in the run
                    }
                    Flush();
                    stateCount += states.Count;
                    history[anchor] = states;
                }

                // ---- anchor_events: mechanical renumbering/insertion/removal detection (§2).
                // A "renumbered" event is emitted only when the text hash match between one
                // removed and one inserted anchor is UNIQUE within the transition — identical
                // boilerplate texts ("Abrogé.") otherwise stay honest removed/inserted pairs.
                var anchorEvents = new JsonArray();
                for (var vi = 1; vi < versions.Count; vi++)
                {
                    Dictionary<string, string> ShaByAnchor(int idx) =>
                        (versions[idx].Doc["provisions"] as JsonArray)!.OfType<JsonObject>()
                        .ToDictionary(p => p["anchor"]!.GetValue<string>(), p => p["text_sha256"]!.GetValue<string>(), StringComparer.Ordinal);
                    var prev = ShaByAnchor(vi - 1);
                    var cur = ShaByAnchor(vi);
                    var atVersion = versions[vi].Doc["lex_id"]?.GetValue<string>();
                    var removed = prev.Keys.Except(cur.Keys, StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToList();
                    var inserted = cur.Keys.Except(prev.Keys, StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToList();
                    var matched = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var r in removed)
                    {
                        var sha = prev[r];
                        var candidates = inserted.Where(a => cur[a] == sha).ToList();
                        var sameShaRemoved = removed.Where(a => prev[a] == sha).ToList();
                        if (candidates.Count == 1 && sameShaRemoved.Count == 1)
                        {
                            matched.Add(r); matched.Add(candidates[0]);
                            anchorEvents.Add(new JsonObject
                            {
                                ["type"] = "renumbered", ["from"] = r, ["to"] = candidates[0],
                                ["text_sha256"] = sha, ["at_version"] = atVersion, ["basis"] = "identical_text",
                            });
                        }
                    }
                    foreach (var r in removed.Where(a => !matched.Contains(a)))
                        anchorEvents.Add(new JsonObject { ["type"] = "removed", ["anchor"] = r, ["at_version"] = atVersion });
                    foreach (var a in inserted.Where(a => !matched.Contains(a)))
                        anchorEvents.Add(new JsonObject { ["type"] = "inserted", ["anchor"] = a, ["at_version"] = atVersion });
                }

                var languages = new SortedSet<string>(versions.Select(v => v.Lang), StringComparer.Ordinal);
                var profiles = new SortedSet<string>(versions
                    .Select(v => v.Doc["generator"]?["profile"]?.GetValue<string>())
                    .OfType<string>(), StringComparer.Ordinal);

                File.WriteAllText(Path.Combine(workDir, "work.json"), new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["title"] = title,
                    ["languages"] = new JsonArray(languages.Select(l => (JsonNode)l).ToArray()),
                    ["versions"] = versionArr,
                    ["anchors"] = new JsonArray(allAnchors.Select(a => (JsonNode)a).ToArray()),
                    // reserved for the transposition/amendment axis (§5): populated by a later
                    // ingest (Cellar NIM links etc.), never a schema migration
                    ["relations"] = new JsonObject
                    {
                        ["amends"] = new JsonArray(), ["amended_by"] = new JsonArray(),
                        ["transposes"] = new JsonArray(), ["implemented_by"] = new JsonArray(),
                        ["repeals"] = new JsonArray(), ["repealed_by"] = new JsonArray(),
                    },
                }.ToJsonString(JsonOpts) + "\n");

                File.WriteAllText(Path.Combine(workDir, "history.json"), new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["schema"] = "lex-articles/1",
                    ["profiles"] = new JsonArray(profiles.Select(p => (JsonNode)p).ToArray()),
                    ["anchors"] = history,
                    ["anchor_events"] = anchorEvents,
                }.ToJsonString(JsonOpts) + "\n");

                catalog.Add(new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["title"] = title,
                    ["languages"] = new JsonArray(languages.Select(l => (JsonNode)l).ToArray()),
                    ["derived_versions"] = versions.Count,
                    ["anchors"] = allAnchors.Count,
                    ["first_valid_from"] = versions[0].ValidFrom,
                    ["last_valid_from"] = versions[^1].ValidFrom,
                });
            }
        }

        File.WriteAllText(Path.Combine(articlesRoot, "catalog.json"), new JsonObject
        {
            ["schema"] = "lex-articles/1",
            ["works"] = catalog,
        }.ToJsonString(JsonOpts) + "\n");
        return new Stats(workCount, anchorCount, stateCount);
    }
}
