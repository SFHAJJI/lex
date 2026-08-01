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

                // ---- history.json: per anchor, consecutive same-sha runs collapse to intervals
                var history = new JsonObject();
                foreach (var anchor in allAnchors)
                {
                    var states = new JsonArray();
                    string? runSha = null, runFrom = null, runTo = null, runVersion = null;
                    void Flush()
                    {
                        if (runSha is null) return;
                        states.Add(new JsonObject
                        {
                            ["valid_from"] = runFrom,
                            ["valid_to"] = runTo,
                            ["text_sha256"] = runSha,
                            ["in_version"] = runVersion,
                        });
                        runSha = null;
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
                        }
                        runTo = validTo;                              // extends with every version in the run
                    }
                    Flush();
                    stateCount += states.Count;
                    history[anchor] = states;
                }

                File.WriteAllText(Path.Combine(workDir, "work.json"), new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["title"] = title,
                    ["versions"] = versionArr,
                    ["anchors"] = new JsonArray(allAnchors.Select(a => (JsonNode)a).ToArray()),
                }.ToJsonString(JsonOpts) + "\n");

                File.WriteAllText(Path.Combine(workDir, "history.json"), new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["schema"] = "lex-articles/1",
                    ["anchors"] = history,
                }.ToJsonString(JsonOpts) + "\n");

                catalog.Add(new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["title"] = title,
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
