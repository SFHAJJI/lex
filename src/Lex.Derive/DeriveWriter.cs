using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Temporal;

namespace Lex.Derive;

/// <summary>
/// Walks an evidence corpus (works/&lt;slug&gt;/versions/&lt;version_key&gt;/*.xml) and writes the
/// derived consumption layer (fr.json + fr.md per version) under
/// out/&lt;publisher&gt;/works/&lt;slug&gt;/versions/&lt;version_key&gt;/. Pure function of
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

    /// <param name="EmptyProvisions">Provisions the profile emitted with no body text. They are
    /// not a skip: the provision exists, carries a heading, and mints a text_sha256 over the empty
    /// string, so it counts as coverage and any later real text reads as an amendment that never
    /// happened. An existing count remains reportable backlog, while an increase for a work fails
    /// closed and leaves that work's previously accepted derived files untouched.</param>
    /// <param name="MostlyEmpty">Versions where at least <see cref="MostlyEmptyPercent"/> of the
    /// provisions extracted with no text. Distinct from a scattered gap: it means the profile did
    /// not work on that document. Reported rather than rejected, because rejecting would discard
    /// the provisions that did extract and would abort the run over a pre-existing backlog.</param>
    public sealed record Stats(int Works, int Versions, int Provisions, int Skipped, List<string> Errors,
        int EmptyProvisions = 0, IReadOnlyList<string>? MostlyEmpty = null);

    /// <summary>The share of empty provisions at which a version stops being a document with gaps
    /// and becomes a failed extraction. Half is deliberately far above the 1.2 percent corpus rate,
    /// so the flag names documents to fix rather than restating the backlog.</summary>
    public const int MostlyEmptyPercent = 50;

    private static int WithText(Extraction extraction) =>
        extraction.Provisions.Count(provision => !string.IsNullOrWhiteSpace(provision.TextMd));

    /// <summary>ONE predicate for "this version's extraction failed", shared by the mostly-empty
    /// report and the second-profile fallback. They started as two inequalities, a >= on the empty
    /// count and a strict &lt; on the with-text count, and at exactly the threshold a version was
    /// flagged as failed yet never offered to the second profile. Flagged but unfixable, on the
    /// boundary, is precisely the drift a single predicate exists to prevent.</summary>
    internal static bool MostlyEmpty(int emptyCount, int totalCount) =>
        totalCount > 0 && emptyCount * 100 >= totalCount * MostlyEmptyPercent;

    /// <summary>Whether a Memorial extraction is poor enough to be worth a second profile.
    ///
    /// <para>The fallback used to ask whether the first profile found any provisions at all. That
    /// is a question about structure, not about wording: the 2003 consolidation of the financial
    /// sector law produced 145 provisions and text for 40 of them, sailed past a zero check, and
    /// published 105 provisions whose text_sha is the hash of the empty string. The second profile
    /// was never given the document.</para></summary>
    internal static bool RecoveredLittleText(Extraction extraction) =>
        extraction.Provisions.Count == 0
        || MostlyEmpty(extraction.Provisions.Count - WithText(extraction), extraction.Provisions.Count);

    /// <summary>Whether the second profile earned the document. Strictly more wording wins; equal
    /// wording keeps the first, unless the first found no structure either, which is the case the
    /// original fallback existed for.</summary>
    internal static bool RecoversMoreText(Extraction candidate, Extraction current)
    {
        var candidateWithText = WithText(candidate);
        var currentWithText = WithText(current);
        return candidateWithText > currentWithText
            || (candidateWithText == currentWithText && current.Provisions.Count == 0);
    }

    public static Stats Derive(
        string corpusRoot, string outRoot, string publisher,
        string deriverCodeCommit, string deriverTreeId,
        string corpusCommit) =>
        DeriveCore(corpusRoot, outRoot, publisher, deriverCodeCommit, deriverTreeId,
            corpusCommit, stagedFileWritten: null);

    internal static Stats Derive(
        string corpusRoot, string outRoot, string publisher,
        string deriverCodeCommit, string deriverTreeId,
        string corpusCommit,
        Action<string>? stagedFileWritten) =>
        DeriveCore(corpusRoot, outRoot, publisher, deriverCodeCommit, deriverTreeId,
            corpusCommit, stagedFileWritten);

    private static Stats DeriveCore(
        string corpusRoot, string outRoot, string publisher,
        string deriverCodeCommit, string deriverTreeId,
        string corpusCommit,
        Action<string>? stagedFileWritten)
    {
        var buildIdentity = ReadBuildIdentity(
            corpusRoot, deriverCodeCommit, deriverTreeId,
            corpusCommit);
        var worksDir = Path.Combine(corpusRoot, "works");
        if (!Directory.Exists(worksDir)) throw new DirectoryNotFoundException(worksDir);

        var attribution = publisher == "lu-legilux"
            ? "Legilux — Ministère d'État, Service central de législation, Grand-Duché de Luxembourg (CC-BY-4.0)"
            : "© European Union, 1998-2026; reuse with attribution (Commission Decision 2011/833/EU); consolidated texts have no legal effect";
        var license = publisher == "lu-legilux"
            ? "CC-BY-4.0"
            : "EU reuse-with-attribution (Commission Decision 2011/833/EU)";

        int works = 0, versions = 0, provisionCount = 0, skipped = 0, emptyProvisions = 0;
        var errors = new List<string>();
        var mostlyEmpty = new List<string>();
        var acceptedProfiles = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var workDir in Directory.EnumerateDirectories(worksDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var slug = Path.GetFileName(workDir);
            var workMetaPath = Path.Combine(workDir, "meta.json");
            if (!File.Exists(workMetaPath))
            {
                errors.Add($"{slug}: corpus work meta.json is missing; prior derived files retained");
                continue;
            }
            var workMeta = JsonNode.Parse(File.ReadAllText(workMetaPath))!;
            var workTitle = workMeta["title"]?.GetValue<string>();
            var outputWorkDir = Path.Combine(outRoot, publisher, "works", slug);
            int? acceptedEmptyBaseline;
            try
            {
                DerivedWorkCandidate.Recover(outputWorkDir,
                    directory => _ = ExistingEmptyProvisionCount(directory));
                acceptedEmptyBaseline = ExistingEmptyProvisionCount(outputWorkDir);
            }
            catch (Exception ex)
            {
                errors.Add($"{slug}: accepted derived output cannot establish the empty-provision baseline: {ex.Message}");
                continue;
            }
            var errorsBeforeWork = errors.Count;
            using var candidate = new DerivedWorkCandidate(outputWorkDir, stagedFileWritten);
            var versionsForWork = 0;
            var provisionsForWork = 0;
            var skippedForWork = 0;
            var emptyForWork = 0;
            var mostlyEmptyForWork = new List<string>();
            var profilesForWork = new SortedSet<string>(StringComparer.Ordinal);

            var versionsRoot = Path.Combine(workDir, "versions");
            if (!Directory.Exists(versionsRoot))
            {
                errors.Add($"{slug}: corpus versions directory is missing; prior derived files retained");
                continue;
            }
            var versionDirs = Directory.EnumerateDirectories(versionsRoot)
                .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();

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
                // A directory name is a version key: date--full SHA-256 of the publisher version
                // identifier. The date is the key's first ten characters —
                // publishing the key as valid_from put a non-date in a date column.
                var versionKey = Path.GetFileName(versionDirs[i]);
                var validFrom = DateKeyOf(versionKey);

                // valid_to = the next version's start, minus a day. Sibling keys for the SAME
                // day must be skipped: taking a same-date hashed sibling as "the next version"
                // produced valid_to = one day before valid_from, an interval ending before it
                // began, which no point-in-time query can ever match.
                string? validTo = null;
                for (var j = i + 1; j < versionDirs.Count; j++)
                {
                    var nextDate = DateKeyOf(Path.GetFileName(versionDirs[j]));
                    if (nextDate == validFrom) continue;
                    if (DateOnly.TryParseExact(nextDate, "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var d))
                        validTo = d.AddDays(-1).ToString("yyyy-MM-dd");
                    break;
                }

                var vMetaPath = Path.Combine(versionDirs[i], "meta.json");
                if (!File.Exists(vMetaPath))
                {
                    errors.Add($"{slug}/{versionKey}: corpus version meta.json is missing; "
                        + "prior derived files retained");
                    continue;
                }
                var vMeta = JsonNode.Parse(File.ReadAllText(vMetaPath))!;
                VersionIdentity.RequireCanonical(
                    versionKey,
                    vMeta["valid_from"]?.GetValue<string>() ?? "",
                    vMeta["publisher_version_identifier"]?.GetValue<string>(),
                    vMeta["lex_id"]?.GetValue<string>() ?? "",
                    $"{publisher}:{slug}");

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
                        var lexId = $"{publisher}:{slug}:{versionKey}";

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
                                if (RecoveredLittleText(extraction))
                                {
                                    var second = PdfMemorialLuProfileV2.Extract(
                                        memorialBytes, lexId, workTitle);
                                    if (RecoversMoreText(second, extraction))
                                    {
                                        extraction = second;
                                        profileId = PdfMemorialLuProfileV2.ProfileId;
                                    }
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
                            skippedForWork++;
                            Console.Error.WriteLine($"  [derive] skipped (no provisions): {slug}/{validFrom}/{unit.ObsFile}");
                            continue;
                        }

                        var relativeOutDir = Path.Combine("versions", versionKey);

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
                        candidate.WriteText(Path.Combine(relativeOutDir, $"{lang}.md"),
                            headerStr + extraction.Markdown);

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
                                ["path"] = $"works/{slug}/versions/{versionKey}/{unit.ObsFile}",
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
                        candidate.WriteText(Path.Combine(relativeOutDir, $"{lang}.json"),
                            json.ToJsonString(JsonOpts) + "\n");

                        profilesForWork.Add(profileId);
                        versionsForWork++;
                        provisionsForWork += extraction.Provisions.Count;

                        var emptyHere = extraction.Provisions.Count(p => string.IsNullOrWhiteSpace(p.TextMd));
                        emptyForWork += emptyHere;
                        if (emptyHere > 0)
                        {
                            // ObsFile, not lang: a version can derive from html, fmx4, pdf or a
                            // gazette cut, and which artifact produced the empty text is the first
                            // thing worth knowing. It also matches the "skipped" line above.
                            Console.Error.WriteLine("  [derive] empty provisions: "
                                + $"{emptyHere}/{extraction.Provisions.Count} {slug}/{validFrom}/{unit.ObsFile}");

                            // A scattered empty provision is a gap in one article. A version that is
                            // mostly empty is a profile that did not work on this document, which is
                            // a materially different fact: it publishes a version whose text_sha
                            // values are hashes of the empty string, so real wording arriving later
                            // reads as an amendment that never happened. Named separately so it can
                            // be counted and fixed, instead of being averaged into a corpus rate
                            // that looks tolerable. 1.2 percent of provisions are empty overall,
                            // while the 2003 financial-sector law is 105 of 145.
                            //
                            // Flagged, not refused. Refusing would discard the provisions that did
                            // extract, and derive aborts the entire run on a non-empty error list,
                            // so a refusal here would stop the nightly for a pre-existing backlog.
                            //
                            // Collected, not printed. The list is returned and the caller decides
                            // how much of it to show. Printing here as well duplicated every line
                            // and made the caller's own bound meaningless.
                            if (MostlyEmpty(emptyHere, extraction.Provisions.Count))
                                mostlyEmptyForWork.Add($"{slug}/{validFrom}/{unit.ObsFile}: {emptyHere} of "
                                    + $"{extraction.Provisions.Count} provisions extracted empty");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{slug}/{validFrom}/{unit.ObsFile}: {ex.Message}");
                    }
                }
            }
            if (errors.Count != errorsBeforeWork) continue;
            if (acceptedEmptyBaseline is { } baseline && emptyForWork > baseline)
            {
                errors.Add($"{slug}: empty-provision count increased from accepted baseline "
                    + $"{baseline} to {emptyForWork}; prior derived files retained");
                continue;
            }
            candidate.Commit();
            works++;
            versions += versionsForWork;
            provisionCount += provisionsForWork;
            skipped += skippedForWork;
            emptyProvisions += emptyForWork;
            mostlyEmpty.AddRange(mostlyEmptyForWork);
            acceptedProfiles.UnionWith(profilesForWork);
        }
        if (errors.Count == 0)
        {
            DerivationGeneration.UpdatePublisher(
                outRoot, publisher, buildIdentity.CorpusCommit,
                buildIdentity.CorpusManifestSha256,
                buildIdentity.IngesterCodeCommit,
                buildIdentity.DeriverCodeCommit,
                buildIdentity.DeriverTreeId,
                acceptedProfiles);
        }
        return new Stats(works, versions, provisionCount, skipped, errors, emptyProvisions, mostlyEmpty);
    }

    private sealed record BuildIdentity(
        string IngesterCodeCommit,
        string DeriverCodeCommit,
        string DeriverTreeId,
        string CorpusManifestSha256,
        string CorpusCommit);

    private static BuildIdentity ReadBuildIdentity(
        string corpusRoot, string deriverCodeCommit, string deriverTreeId,
        string corpusCommit)
    {
        var manifestPath = Path.Combine(corpusRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("Corpus manifest.json is required before derivation.");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject
            ?? throw new InvalidDataException("Corpus manifest.json is not an object.");
        if (manifest["schema"]?.GetValue<string>() != "lex-corpus/4")
            throw new InvalidDataException(
                "Derivation requires a fresh lex-corpus/4 input; migrate legacy corpus data first.");
        var ingester = CodeIdentity.RequireFullCommit(
            manifest["ingester_code_commit"]?.GetValue<string>(),
            "manifest ingester_code_commit");
        var deriver = CodeIdentity.RequireFullCommit(
            deriverCodeCommit, "deriver code commit");
        var tree = CodeIdentity.RequireFullGitObjectId(
            deriverTreeId, "deriver tree id");
        var corpus = CodeIdentity.RequireFullCommit(corpusCommit, "corpus commit");
        return new(ingester, deriver, tree,
            DerivationGeneration.Sha256File(manifestPath), corpus);
    }

    private static int? ExistingEmptyProvisionCount(string workOutputDirectory)
    {
        if (!Directory.Exists(workOutputDirectory)) return null;
        var files = Directory.EnumerateFiles(workOutputDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            if (!Directory.EnumerateFileSystemEntries(workOutputDirectory).Any()) return 0;
            throw new InvalidDataException("the work output directory contains no derived JSON files");
        }
        var count = 0;
        foreach (var path in files)
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException($"{path} is not a JSON object");
            var provisions = root["provisions"] as JsonArray
                ?? throw new InvalidDataException($"{path} has no provisions array");
            count += provisions.OfType<JsonObject>().Count(provision =>
                string.IsNullOrWhiteSpace(provision["text_md"]?.GetValue<string>()));
        }
        return count;
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
    /// The date a version key denotes. Every fresh key carries a full publisher-identity hash;
    /// that storage suffix must never reach a date field or an interval calculation.
    /// </summary>
    public static string DateKeyOf(string versionKey)
        => versionKey.Length >= 10 && DateOnly.TryParseExact(versionKey[..10], "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _) ? versionKey[..10] : versionKey;
}
