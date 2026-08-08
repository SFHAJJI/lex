using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.Derive;

/// <summary>
/// Walks an evidence corpus (works/&lt;slug&gt;/versions/&lt;valid_from&gt;/*.xml) and writes the
/// derived consumption layer (fr.json + fr.md per version) under
/// out/&lt;publisher&gt;/works/&lt;slug&gt;/versions/&lt;valid_from&gt;/. Pure function of
/// (corpus bytes, profile version): deleting the output loses nothing but compute.
/// </summary>
public static class DeriveWriter
{
    public const string SchemaId = "lex-articles/1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record Stats(int Works, int Versions, int Provisions, int Skipped, List<string> Errors);

    public static Stats Derive(string corpusRoot, string outRoot, string publisher)
    {
        var worksDir = Path.Combine(corpusRoot, "works");
        if (!Directory.Exists(worksDir)) throw new DirectoryNotFoundException(worksDir);

        var attribution = publisher == "lu-legilux"
            ? "Legilux — Ministère d'État, Service central de législation, Grand-Duché de Luxembourg (CC-BY-4.0)"
            : "© European Union, 1998-2026; reuse with attribution (Commission Decision 2011/833/EU); consolidated texts have no legal effect";
        var license = publisher == "lu-legilux"
            ? "CC-BY-4.0"
            : "EU reuse-with-attribution (Commission Decision 2011/833/EU)";

        int works = 0, versions = 0, provisionCount = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var workDir in Directory.EnumerateDirectories(worksDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var slug = Path.GetFileName(workDir);
            var workMetaPath = Path.Combine(workDir, "meta.json");
            if (!File.Exists(workMetaPath)) continue;
            var workMeta = JsonNode.Parse(File.ReadAllText(workMetaPath))!;
            var workTitle = workMeta["title"]?.GetValue<string>();
            works++;

            var versionDirs = Directory.Exists(Path.Combine(workDir, "versions"))
                ? Directory.EnumerateDirectories(Path.Combine(workDir, "versions"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList()
                : [];

            // D48: prefer one profile across a work+language when EVERY version that has a
            // primary body also has Formex. If a particular version has no primary body at all,
            // however, its official Formex must remain usable as a recovery source. That can
            // introduce a profile boundary, so comparison endpoints refuse to pair provisions
            // across it rather than omitting the publisher's wording from search and reading.
            var fmxByVersion = versionDirs.Select(Fmx4Mains).ToList();
            // D49: the PDF fallback. Unlike fmx4 this is decided PER VERSION, not per work,
            // because it is a fallback rather than a format upgrade: one law is routinely XML on
            // some dates and PDF-only on others, and the profile id is the confidence marker for
            // exactly that version.
            var pdfByVersion = versionDirs.Select(PdfMains).ToList();
            var gazByVersion = versionDirs.Select(GazetteMains).ToList();
            var bodyLangsByVersion = versionDirs.Select(vd => Directory.EnumerateFiles(vd, "*.*")
                .Where(f => Path.GetExtension(f) is ".xml" or ".html")
                .Select(f => Path.GetFileNameWithoutExtension(f)!)
                .ToHashSet(StringComparer.Ordinal)).ToList();
            var fmx4Langs = fmxByVersion.SelectMany(m => m.Keys).Distinct()
                .Where(l => !bodyLangsByVersion.Where((langs, vi) => langs.Contains(l) && !fmxByVersion[vi].ContainsKey(l)).Any())
                .ToHashSet(StringComparer.Ordinal);

            for (var i = 0; i < versionDirs.Count; i++)
            {
                // A directory name is a version KEY, which may carry a same-day collision
                // suffix (2025-07-28--02, D41). The date is the key's first ten characters —
                // publishing the key as valid_from put a non-date in a date column.
                var versionKey = Path.GetFileName(versionDirs[i]);
                var validFrom = DateKeyOf(versionKey);

                // valid_to = the next version's start, minus a day. Sibling keys for the SAME
                // day must be skipped: taking 2025-07-28--02 as "the next version" of
                // 2025-07-28 produced valid_to = 2025-07-27, an interval ending before it
                // began, which no point-in-time query can ever match.
                string? validTo = null;
                for (var j = i + 1; j < versionDirs.Count; j++)
                {
                    var nextDate = DateKeyOf(Path.GetFileName(versionDirs[j]));
                    if (nextDate == validFrom) continue;
                    if (DateOnly.TryParse(nextDate, out var d))
                        validTo = d.AddDays(-1).ToString("yyyy-MM-dd");
                    break;
                }

                var vMetaPath = Path.Combine(versionDirs[i], "meta.json");
                if (!File.Exists(vMetaPath)) continue;
                var vMeta = JsonNode.Parse(File.ReadAllText(vMetaPath))!;

                var units = new List<(string Lang, string FilePath, string Kind, string ObsFile)>();
                foreach (var f in Directory.EnumerateFiles(versionDirs[i], "*.*")
                             .Where(f => Path.GetExtension(f) is ".xml" or ".html")
                             .OrderBy(f => f, StringComparer.Ordinal))
                {
                    var l = Path.GetFileNameWithoutExtension(f);
                    if (fmx4Langs.Contains(l) && fmxByVersion[i].ContainsKey(l)) continue;   // superseded by fmx4 unit
                    units.Add((l, f, "structured-text", Path.GetFileName(f)));
                }
                foreach (var (l, mainPath) in fmxByVersion[i].OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    if (fmx4Langs.Contains(l) || !units.Any(u => u.Lang == l))
                        units.Add((l, mainPath, "fmx4", $"{l}.fmx4/{Path.GetFileName(mainPath)}"));
                // Only where the publisher served no structural body for that language on that
                // date. Deriving a version twice, once from its XML and once from its PDF, would
                // put two texts of the same article in the corpus and invent a diff between them.
                foreach (var (l, pdfPath) in pdfByVersion[i].OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    if (!units.Any(u => u.Lang == l))
                        units.Add((l, pdfPath, "pdf", $"{l}.pdf/{Path.GetFileName(pdfPath)}"));
                // Last resort, and only when nothing better exists for that language: the act cut
                // out of a gazette issue.
                foreach (var (l, gazPath) in gazByVersion[i].OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    if (!units.Any(u => u.Lang == l))
                        units.Add((l, gazPath, "pdf-memorial", $"{l}.pdf-memorial/{Path.GetFileName(gazPath)}"));

                foreach (var unit in units.OrderBy(u => u.ObsFile, StringComparer.Ordinal))
                {
                    var lang = unit.Lang;
                    try
                    {
                        var expr = (vMeta["expressions"] as JsonArray)?.OfType<JsonObject>()
                            .FirstOrDefault(e => e["language"]?.GetValue<string>() == lang);
                        var obs = (expr?["observations"] as JsonArray)?.OfType<JsonObject>()
                            .FirstOrDefault(o => o["file"]?.GetValue<string>() == unit.ObsFile);
                        var sourceSha = obs?["sha256"]?.GetValue<string>() ?? "";
                        var sourceUri = expr?["source_uri"]?.GetValue<string>()
                                        ?? vMeta["work_identifier"]?.GetValue<string>() ?? "";
                        var lexId = $"{publisher}:{slug}:{validFrom}";

                        Extraction extraction;
                        string profileId;
                        switch (unit.Kind)
                        {
                            case "pdf":
                                extraction = PdfLuProfile.Extract(File.ReadAllBytes(unit.FilePath), lexId);
                                profileId = PdfLuProfile.ProfileId;
                                break;
                            case "pdf-memorial":
                                var memorialBytes = File.ReadAllBytes(unit.FilePath);
                                extraction = PdfMemorialLuProfile.Extract(memorialBytes, lexId);
                                profileId = PdfMemorialLuProfile.ProfileId;
                                if (extraction.Provisions.Count == 0)
                                {
                                    extraction = PdfMemorialLuProfileV2.Extract(
                                        memorialBytes, lexId, workTitle);
                                    profileId = PdfMemorialLuProfileV2.ProfileId;
                                }
                                break;
                            case "fmx4":
                                extraction = Fmx4EuProfile.Extract(File.ReadAllText(unit.FilePath, Encoding.UTF8), lexId);
                                profileId = Fmx4EuProfile.ProfileId;
                                break;
                            default:
                                var result = StructuredTextExtractor.Extract(
                                    File.ReadAllText(unit.FilePath, Encoding.UTF8), lexId);
                                extraction = result.Extraction;
                                profileId = result.ProfileId;
                                break;
                        }
                        var frontmatter = new Dictionary<string, string>
                        {
                            ["lex_id"] = lexId,
                            ["title"] = workTitle ?? slug,
                            ["valid_from"] = validFrom,
                            ["valid_to"] = validTo ?? "open",
                            ["source"] = sourceUri,
                            ["source_sha256"] = sourceSha,
                            ["license"] = license,
                            ["attribution"] = attribution,
                            ["generator"] = $"{profileId} · lex derive",
                        };

                        if (extraction.Provisions.Count == 0)
                        {
                            skipped++;
                            Console.Error.WriteLine($"  [derive] skipped (no provisions): {slug}/{validFrom}/{unit.ObsFile}");
                            continue;
                        }

                        var outDir = Path.Combine(outRoot, publisher, "works", slug, "versions", validFrom);
                        Directory.CreateDirectory(outDir);

                        // ---- fr.md: fenced frontmatter + document. Values are YAML
                        // single-quoted: titles contain ": " which is a YAML mapping error
                        // unquoted (GitHub renders a parse banner instead of the table).
                        var mdHeader = new StringBuilder("---\n");
                        foreach (var (k, v) in frontmatter)
                            mdHeader.Append(k).Append(": '").Append(v.Replace("\n", " ").Replace("'", "''")).Append("'\n");
                        mdHeader.Append("---\n");
                        // spans were computed over extraction.Markdown alone; prepend length in codepoints
                        var headerStr = mdHeader.ToString();
                        var headerCp = headerStr.Count(c => !char.IsLowSurrogate(c));
                        File.WriteAllText(Path.Combine(outDir, $"{lang}.md"), headerStr + extraction.Markdown, new UTF8Encoding(false));

                        // ---- fr.json
                        var provisions = new JsonArray();
                        foreach (var p in extraction.Provisions)
                        {
                            var cites = new JsonArray();
                            foreach (var c in p.Citations.DistinctBy(c => (c.Href, c.Text)))
                                cites.Add(new JsonObject { ["href"] = c.Href, ["text"] = c.Text });
                            provisions.Add(new JsonObject
                            {
                                ["anchor"] = p.Anchor,
                                ["provision_id"] = $"{lexId}#{p.Anchor}",
                                ["eli"] = p.Eli,
                                ["type"] = p.Type,
                                ["num"] = p.Num,
                                ["heading"] = p.Heading,
                                ["path"] = new JsonArray(p.Path.Select(s => (JsonNode)s).ToArray()),
                                ["article_valid_from"] = p.ArticleValidFrom,
                                ["text_md"] = p.TextMd,
                                ["text_sha256"] = p.TextSha256,
                                ["md_span"] = new JsonObject { ["start"] = headerCp + p.MdStart, ["end"] = headerCp + p.MdEnd },
                                ["citations"] = cites,
                            });
                        }
                        var json = new JsonObject
                        {
                            ["schema"] = SchemaId,
                            ["lex_id"] = lexId,
                            ["language"] = lang,
                            ["title"] = workTitle,
                            ["valid_from"] = validFrom,
                            ["valid_to"] = validTo,
                            ["valid_time_source"] = "publisher",
                            ["derived_from"] = new JsonObject
                            {
                                ["corpus_repo"] = $"lex-corpus-{publisher}",
                                ["path"] = $"works/{slug}/versions/{validFrom}/{unit.ObsFile}",
                                ["sha256"] = sourceSha,
                                ["source_uri"] = sourceUri,
                            },
                            // The immutable profile id IS the reproducibility contract (SCHEMA.md);
                            // a code sha here would churn every file on unrelated commits.
                            ["generator"] = new JsonObject
                            {
                                ["profile"] = profileId,
                                ["tool"] = "lex derive",
                            },
                            ["license"] = license,
                            ["attribution"] = attribution,
                            ["provisions"] = provisions,
                            ["notes"] = new JsonArray(extraction.Notes.Select(n => (JsonNode)n).ToArray()),
                        };
                        File.WriteAllText(Path.Combine(outDir, $"{lang}.json"),
                            json.ToJsonString(JsonOpts) + "\n", new UTF8Encoding(false));

                        versions++;
                        provisionCount += extraction.Provisions.Count;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{slug}/{validFrom}/{unit.ObsFile}: {ex.Message}");
                    }
                }
            }
        }
        return new Stats(works, versions, provisionCount, skipped, errors);
    }

    /// <summary>
    /// D48: map language -> Formex main-member path for a version dir. Members live under
    /// {lang}.fmx4/; the main member is the only non-.doc.xml file, or the one the .doc.xml
    /// manifest points at via REF.PHYS TYPE="DOC.XML" (largest file as deterministic fallback).
    /// </summary>
    /// A gazette issue per language, written as {lang}.pdf-memorial/.
    private static Dictionary<string, string> GazetteMains(string versionDir)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in Directory.EnumerateDirectories(versionDir, "*.pdf-memorial").OrderBy(x => x, StringComparer.Ordinal))
        {
            var lang = Path.GetFileName(d);
            lang = lang[..^".pdf-memorial".Length];
            var pdfs = Directory.EnumerateFiles(d, "*.pdf").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (pdfs.Count == 1) result[lang] = pdfs[0];
        }
        return result;
    }

    /// One PDF per language directory, written by the alt-manifestation path as {lang}.pdf/.
    private static Dictionary<string, string> PdfMains(string versionDir)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in Directory.EnumerateDirectories(versionDir, "*.pdf").OrderBy(x => x, StringComparer.Ordinal))
        {
            var lang = Path.GetFileName(d);
            lang = lang[..^".pdf".Length];
            var pdfs = Directory.EnumerateFiles(d, "*.pdf").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (pdfs.Count == 1) result[lang] = pdfs[0];
        }
        return result;
    }

    private static Dictionary<string, string> Fmx4Mains(string versionDir)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in Directory.EnumerateDirectories(versionDir, "*.fmx4").OrderBy(x => x, StringComparer.Ordinal))
        {
            var lang = Path.GetFileName(d);
            lang = lang[..^".fmx4".Length];
            var xmls = Directory.EnumerateFiles(d, "*.xml")
                .Where(f => !f.EndsWith(".doc.xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (xmls.Count == 0) continue;
            string? pick = xmls.Count == 1 ? xmls[0] : null;
            if (pick is null)
            {
                var docXml = Directory.EnumerateFiles(d, "*.doc.xml").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
                if (docXml is not null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        File.ReadAllText(docXml, Encoding.UTF8),
                        "REF\\.PHYS FILE=\"([^\"]+)\" TYPE=\"DOC\\.XML\"");
                    if (m.Success)
                        pick = xmls.FirstOrDefault(f => Path.GetFileName(f) == m.Groups[1].Value);
                }
                pick ??= xmls.OrderByDescending(f => new FileInfo(f).Length).ThenBy(f => f, StringComparer.Ordinal).First();
            }
            result[lang] = pick;
        }
        return result;
    }

    /// <summary>
    /// The date a version key denotes. Keys are normally an ISO date, but a second version
    /// dated the same day carries a collision suffix (2025-07-28--02, D41). That suffix is
    /// storage, not validity: it must never reach a date field or an interval calculation.
    /// </summary>
    public static string DateKeyOf(string versionKey)
        => versionKey.Length >= 10 && DateOnly.TryParse(versionKey[..10], out _) ? versionKey[..10] : versionKey;
}
