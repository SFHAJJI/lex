using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Law;

namespace Lex.Ingest;

/// <summary>
/// The single component that writes corpus files (C1 layout, C3 rules, F12 discipline).
/// Adapters never touch disk (F8). Version directories are valid_from-only and are never
/// renamed (D41). meta.json changes only when observed reality changes; every change
/// appends the corresponding chain entry (F12). Body files are append-only (none are
/// written at all in metadata-only mode, D42).
/// </summary>
public sealed class CorpusWriter(string corpusRoot, DateTimeOffset now)
{
    private readonly string _now = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    public int Created { get; private set; }
    public int Updated { get; private set; }
    public int Unchanged { get; private set; }

    public async Task WriteAsync(ISourceAdapter adapter, CancellationToken ct)
    {
        var desc = adapter.Describe();
        var pub = desc.Publisher;
        Directory.CreateDirectory(Path.Combine(corpusRoot, "works"));

        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var langs = new HashSet<string>(StringComparer.Ordinal);
        string? earliest = null, latest = null;
        int works = 0, versions = 0;

        await foreach (var work in adapter.EnumerateWorks(ct))
        {
            var versionsOfWork = await adapter.FetchVersions(work, ct);
            if (versionsOfWork.Count == 0) continue;
            works++;

            var workDir = Path.Combine(corpusRoot, "works", work.Slug);
            Directory.CreateDirectory(workDir);

            var workMeta = new WorkMeta
            {
                LexWorkId = $"{pub.Id}:{work.Slug}",
                WorkIdentifier = work.Id.Value,
                Publisher = pub.Id,
                DocumentType = work.TypeCode,
                Slug = work.Slug,
                Title = work.TitleHint,
                SourceUri = versionsOfWork[^1].Expressions.FirstOrDefault()?.SourceUri,
            };
            WriteIfChanged(Path.Combine(workDir, "meta.json"), JsonSerializer.Serialize(workMeta, CorpusJson.Options));

            var usedVersionKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in versionsOfWork.OrderBy(v => v.ValidFrom))
            {
                versions++;
                var vfrom = v.ValidFrom.ToString("yyyy-MM-dd");
                var vkey = vfrom;
                var ordinal = 2;
                while (!usedVersionKeys.Add(vkey)) vkey = $"{vfrom}--{ordinal++:00}"; // D41 collision suffix

                if (v.TypeCode is not null) kinds[v.TypeCode] = kinds.GetValueOrDefault(v.TypeCode) + 1;
                foreach (var e in v.Expressions) langs.Add(e.Language);
                earliest = Min(earliest, vfrom);
                latest = Max(latest, vfrom);

                var versionDir = Path.Combine(workDir, "versions", vkey);
                Directory.CreateDirectory(versionDir);
                var metaPath = Path.Combine(versionDir, "meta.json");

                var lexId = $"{pub.Id}:{work.Slug}:{vkey}";
                VersionMeta meta;
                var existing = File.Exists(metaPath);
                var changed = false;
                if (existing)
                {
                    meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(metaPath, ct), CorpusJson.Options)!;
                    // F12: interval closure — one appended event, valid_to updated, nothing else touched.
                    var newTo = v.ValidTo?.ToString("yyyy-MM-dd");
                    if (meta.ValidTo is null && newTo is not null)
                    {
                        meta.ValidTo = newTo;
                        foreach (var e in meta.Expressions) e.ValidTo ??= newTo;
                        meta.Events.Add(new EventEntry { Event = "interval_closed", ObservedFrom = _now, Detail = $"valid_to={newTo}" });
                        changed = true;
                    }
                    else if (meta.ValidTo != newTo)
                    {
                        meta.Events.Add(new EventEntry
                        {
                            Event = "validity_revised", ObservedFrom = _now, Scope = "version",
                            Detail = $"valid_to: {meta.ValidTo ?? "null"} -> {newTo ?? "null"}"
                        });
                        meta.ValidTo = newTo;
                        foreach (var e in meta.Expressions) e.ValidTo = newTo;
                        changed = true;
                    }
                }
                else
                {
                    meta = new VersionMeta
                    {
                        LexId = lexId,
                        WorkIdentifier = v.WorkId.Value,
                        Publisher = pub.Id,
                        DocumentType = v.TypeCode,
                        ValidFrom = vfrom,
                        ValidTo = v.ValidTo?.ToString("yyyy-MM-dd"),
                        ValidTimeSource = v.ValidTimeSource,
                        InForceStatus = v.InForceStatus,
                        PublicationDate = v.PublicationDate?.ToString("yyyy-MM-dd"),
                        Events = [new EventEntry { Event = "first_sighting", ObservedFrom = _now }],
                        Expressions = v.Expressions.Select(e => new ExpressionMeta
                        {
                            Language = e.Language,
                            ValidFrom = e.ValidFrom?.ToString("yyyy-MM-dd"),
                            ValidTo = e.ValidTo?.ToString("yyyy-MM-dd"),
                            ValidTimeSource = e.ValidTimeSource,
                            Title = e.Title,
                            TitleShort = e.TitleShort,
                            SourceUri = e.SourceUri,
                            Text = new TextInfo
                            {
                                Available = false,
                                Reason = desc.TextIncluded ? "not-fetched" : "pending-gate",
                                Url = e.SourceUri,
                            },
                        }).ToList(),
                        Relations = v.Relations.Select(r => new Dictionary<string, string>
                            { ["type"] = r.Type, ["target"] = r.Target.Value }).ToList(),
                        Raw = new Dictionary<string, string>(v.Raw),
                    };
                }

                // Text-bearing publishers: fetch each expression's body once (verbatim bytes,
                // append-only — F12: an existing body file is never opened for writing).
                // Runs for new AND existing records so a failed night backfills later.
                var bodyAdded = false;
                if (desc.TextIncluded)
                {
                    foreach (var (exprMeta, exprRec) in meta.Expressions.Zip(v.Expressions))
                    {
                        if (exprMeta.Observations.Count > 0) continue;   // already observed
                        var body = await adapter.FetchBody(v, exprRec, ct);
                        if (body is null) continue;
                        var bytes = Encoding.UTF8.GetBytes(body);
                        var file = $"{exprMeta.Language}.html";           // §3.3 rule 3, initial expression
                        var bodyPath = Path.Combine(versionDir, file);
                        if (!File.Exists(bodyPath)) await File.WriteAllBytesAsync(bodyPath, bytes, ct);
                        exprMeta.Observations.Add(new ObservationEntry
                        {
                            File = file,
                            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                            SourceUri = exprRec.SourceUri ?? "",
                            RetrievedAt = _now,
                            ObservedFrom = _now,
                        });
                        exprMeta.Text = new TextInfo { Available = true, Url = exprRec.SourceUri };
                        bodyAdded = true;
                    }
                }

                if (existing && !changed && !bodyAdded) { Unchanged++; continue; }
                if (existing) Updated++; else Created++;

                meta.RecordSha256 = null;
                var canonical = JsonSerializer.Serialize(meta, CorpusJson.Options);
                meta.RecordSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
                await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n", ct);
            }
        }

        var manifest = new ManifestDoc
        {
            Publisher = new Dictionary<string, string>
            {
                ["id"] = pub.Id, ["name"] = pub.Name, ["jurisdiction"] = pub.Jurisdiction, ["homepage"] = pub.Homepage,
            },
            Tier = pub.Tier.ToString(),
            SourceEndpoint = null,
            Attribution = pub.Attribution,
            SourceTermsUrl = pub.SourceTermsUrl,
            TextIncluded = desc.TextIncluded,
            TextPublic = desc.TextPublic,
            Modifications = desc.TextIncluded
                ? "Metadata converted from source RDF to JSON; bodies stored verbatim as retrieved from the publisher's dissemination channel; no text altered."
                : "Metadata converted from source RDF to JSON; no text stored (metadata-only mode, D42).",
            DocumentTypes = kinds.OrderByDescending(k => k.Value)
                .Select(k => new Dictionary<string, object> { ["code"] = k.Key, ["versions"] = k.Value }).ToList(),
            Languages = langs.Order().ToList(),
            Works = works,
            Versions = versions,
            ValidFromEarliest = earliest,
            ValidToLatest = latest,
            HistoryBegins = desc.HistoryBegins,
            IngesterVersion = "0.1.0",
        };
        WriteIfChanged(Path.Combine(corpusRoot, "manifest.json"), JsonSerializer.Serialize(manifest, CorpusJson.Options));
        WriteIfChanged(Path.Combine(corpusRoot, "NOTICE"), Notice(pub));
        Console.Error.WriteLine($"  [corpus] works={works} versions={versions} created={Created} updated={Updated} unchanged={Unchanged}");
    }

    private void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path).TrimEnd('\n') == content.TrimEnd('\n')) return;
        File.WriteAllText(path, content.TrimEnd('\n') + "\n");
    }

    private static string Min(string? a, string b) => a is null || string.CompareOrdinal(b, a) < 0 ? b : a;
    private static string Max(string? a, string b) => a is null || string.CompareOrdinal(b, a) > 0 ? b : a;

    private static string Notice(Publisher pub) => $"""
        NOTICE — three layers (Lex spec §16.2)

        1. UNDERLYING ACTS AND DOCUMENTS
           Official acts of public authority are excluded from copyright protection
           (Luxembourg: loi du 18 avril 2001, art. 10, 8°). Where source metadata is
           reused, it is reused under the publisher's own terms.
           Attribution: {pub.Attribution}
           Source terms: {pub.SourceTermsUrl}
           Modifications: metadata converted from source RDF to JSON; no text altered,
           no text stored. These obligations survive into forks and derived artefacts.

        2. LEX'S COMPILATION
           The selection, arrangement, observation history and database rights in this
           compilation are the work of the Lex project. Published corpus compilations
           carry CC-BY-4.0 (spec D43, stars-maximal). Index release assets carry a
           limited grant: free to download and use; redistribution of any build is
           reserved.

        3. CODE LICENCE DOES NOT APPLY HERE
           The Apache-2.0 licence of the lex code repository does not extend to this
           data repository or to index artefacts.
        """;
}
