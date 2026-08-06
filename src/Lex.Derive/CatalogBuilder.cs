using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.Derive;

/// <summary>
/// Builds per-work inventories and language-specific provision histories from the deterministic
/// derived expression files. No corpus, network, LLM, or clock participates.
/// </summary>
public static class CatalogBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record Stats(int Works, int Anchors, int HistoryStates);

    private sealed record DerivedVersion(
        string ValidFrom,
        string Language,
        JsonNode Document,
        IReadOnlyDictionary<string, JsonObject> Provisions,
        IReadOnlyDictionary<string, string> ShaByAnchor);

    public static Stats Build(string articlesRoot)
    {
        var workCount = 0;
        var anchorCount = 0;
        var stateCount = 0;
        var catalog = new JsonArray();

        foreach (var publisherDirectory in Directory.EnumerateDirectories(articlesRoot)
                     .Where(d => Directory.Exists(Path.Combine(d, "works")))
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var publisher = Path.GetFileName(publisherDirectory);
            foreach (var workDirectory in Directory.EnumerateDirectories(
                             Path.Combine(publisherDirectory, "works"))
                         .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var slug = Path.GetFileName(workDirectory);
                var versionsDirectory = Path.Combine(workDirectory, "versions");
                if (!Directory.Exists(versionsDirectory)) continue;

                var versions = ReadVersions(versionsDirectory);
                if (versions.Count == 0) continue;
                workCount++;

                string? title = null;
                var versionArray = new JsonArray();
                var allAnchors = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var version in versions)
                {
                    title ??= version.Document["title"]?.GetValue<string>();
                    allAnchors.UnionWith(version.Provisions.Keys);
                    versionArray.Add(new JsonObject
                    {
                        ["valid_from"] = version.ValidFrom,
                        ["valid_to"] = version.Document["valid_to"]?.DeepClone(),
                        ["language"] = version.Language,
                        ["provisions"] = version.Provisions.Count,
                    });
                }
                anchorCount += allAnchors.Count;

                // Language is part of legal text identity. Mixing translations in one timeline
                // would manufacture a wording change every time EN and FR alternate in file order.
                var languages = new SortedSet<string>(versions.Select(v => v.Language), StringComparer.Ordinal);
                var historiesByLanguage = new JsonObject();
                var eventsByLanguage = new JsonObject();
                foreach (var language in languages)
                {
                    var result = BuildLanguageHistory(
                        versions.Where(v => v.Language == language).ToList());
                    historiesByLanguage[language] = result.History;
                    eventsByLanguage[language] = result.Events;
                    stateCount += result.StateCount;
                }

                // Preserve the original `anchors` and `anchor_events` fields for existing
                // consumers. They expose one deterministic primary language; new consumers use
                // the explicit by-language maps. Count wins, then ordinal language code on ties.
                var primaryLanguage = versions.GroupBy(v => v.Language)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .First().Key;
                var primaryHistory = historiesByLanguage[primaryLanguage]!.DeepClone();
                var primaryEvents = eventsByLanguage[primaryLanguage]!.DeepClone();
                var profiles = new SortedSet<string>(versions
                    .Select(v => v.Document["generator"]?["profile"]?.GetValue<string>())
                    .OfType<string>(), StringComparer.Ordinal);

                WriteJson(Path.Combine(workDirectory, "work.json"), new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["title"] = title,
                    ["languages"] = new JsonArray(languages.Select(l => (JsonNode)l).ToArray()),
                    ["versions"] = versionArray,
                    ["anchors"] = new JsonArray(allAnchors.Select(a => (JsonNode)a).ToArray()),
                    ["relations"] = new JsonObject
                    {
                        ["amends"] = new JsonArray(),
                        ["amended_by"] = new JsonArray(),
                        ["transposes"] = new JsonArray(),
                        ["implemented_by"] = new JsonArray(),
                        ["repeals"] = new JsonArray(),
                        ["repealed_by"] = new JsonArray(),
                    },
                });

                WriteJson(Path.Combine(workDirectory, "history.json"), new JsonObject
                {
                    ["lex_work_id"] = $"{publisher}:{slug}",
                    ["schema"] = "lex-articles/1",
                    ["profiles"] = new JsonArray(profiles.Select(p => (JsonNode)p).ToArray()),
                    ["primary_language"] = primaryLanguage,
                    ["anchors"] = primaryHistory,
                    ["anchor_events"] = primaryEvents,
                    ["anchors_by_language"] = historiesByLanguage,
                    ["anchor_events_by_language"] = eventsByLanguage,
                });

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

        WriteJson(Path.Combine(articlesRoot, "catalog.json"), new JsonObject
        {
            ["schema"] = "lex-articles/1",
            ["works"] = catalog,
        });
        return new Stats(workCount, anchorCount, stateCount);
    }

    private static List<DerivedVersion> ReadVersions(string versionsDirectory)
    {
        var versions = new List<DerivedVersion>();
        foreach (var versionDirectory in Directory.EnumerateDirectories(versionsDirectory)
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            foreach (var jsonFile in Directory.EnumerateFiles(versionDirectory, "*.json")
                         .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var document = JsonNode.Parse(File.ReadAllText(jsonFile))!;
                var provisions = (document["provisions"] as JsonArray)!.OfType<JsonObject>()
                    .ToDictionary(p => p["anchor"]!.GetValue<string>(), StringComparer.Ordinal);
                versions.Add(new DerivedVersion(
                    document["valid_from"]?.GetValue<string>() ?? Path.GetFileName(versionDirectory),
                    Path.GetFileNameWithoutExtension(jsonFile),
                    document,
                    provisions,
                    provisions.ToDictionary(
                        p => p.Key,
                        p => p.Value["text_sha256"]!.GetValue<string>(),
                        StringComparer.Ordinal)));
            }
        }
        return versions;
    }

    private static (JsonObject History, JsonArray Events, int StateCount) BuildLanguageHistory(
        IReadOnlyList<DerivedVersion> versions)
    {
        var anchors = new SortedSet<string>(
            versions.SelectMany(v => v.Provisions.Keys), StringComparer.Ordinal);
        var history = new JsonObject();
        var stateCount = 0;

        foreach (var anchor in anchors)
        {
            var states = new JsonArray();
            string? runSha = null;
            string? runFrom = null;
            string? runTo = null;
            string? runVersion = null;
            string? runArticleDate = null;

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
                runSha = null;
                runArticleDate = null;
            }

            foreach (var version in versions)
            {
                if (!version.Provisions.TryGetValue(anchor, out var provision))
                {
                    Flush();
                    continue;
                }

                var sha = version.ShaByAnchor[anchor];
                if (sha != runSha)
                {
                    Flush();
                    runSha = sha;
                    runFrom = version.ValidFrom;
                    runVersion = version.Document["lex_id"]?.GetValue<string>();
                    runArticleDate = provision["article_valid_from"]?.GetValue<string>();
                }
                runTo = version.Document["valid_to"]?.GetValue<string>();
            }
            Flush();
            stateCount += states.Count;
            history[anchor] = states;
        }

        return (history, BuildAnchorEvents(versions), stateCount);
    }

    private static JsonArray BuildAnchorEvents(IReadOnlyList<DerivedVersion> versions)
    {
        var events = new JsonArray();
        for (var index = 1; index < versions.Count; index++)
        {
            var previous = versions[index - 1].ShaByAnchor;
            var current = versions[index].ShaByAnchor;
            var atVersion = versions[index].Document["lex_id"]?.GetValue<string>();
            var removed = previous.Keys.Except(current.Keys, StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal).ToList();
            var inserted = current.Keys.Except(previous.Keys, StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal).ToList();
            var matched = new HashSet<string>(StringComparer.Ordinal);

            foreach (var removedAnchor in removed)
            {
                var sha = previous[removedAnchor];
                var candidates = inserted.Where(a => current[a] == sha).ToList();
                var sameShaRemoved = removed.Where(a => previous[a] == sha).ToList();
                if (candidates.Count != 1 || sameShaRemoved.Count != 1) continue;
                matched.Add(removedAnchor);
                matched.Add(candidates[0]);
                events.Add(new JsonObject
                {
                    ["type"] = "renumbered",
                    ["from"] = removedAnchor,
                    ["to"] = candidates[0],
                    ["text_sha256"] = sha,
                    ["at_version"] = atVersion,
                    ["basis"] = "identical_text",
                });
            }
            foreach (var removedAnchor in removed.Where(a => !matched.Contains(a)))
                events.Add(new JsonObject
                    { ["type"] = "removed", ["anchor"] = removedAnchor, ["at_version"] = atVersion });
            foreach (var insertedAnchor in inserted.Where(a => !matched.Contains(a)))
                events.Add(new JsonObject
                    { ["type"] = "inserted", ["anchor"] = insertedAnchor, ["at_version"] = atVersion });
        }
        return events;
    }

    private static void WriteJson(string path, JsonObject value) =>
        File.WriteAllText(path, value.ToJsonString(JsonOpts) + "\n");
}
