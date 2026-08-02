using System.Diagnostics;
using System.Text.Json;
using Lex.Index;

namespace Lex.Ingest;

/// <summary>
/// Maps a corpus tree (HEAD only — §7.4/§8.2) into generic index rows and builds the
/// per-publisher index file with a signed stamp (D40). Build time is injected (F9).
/// </summary>
public static class IndexFromCorpus
{
    public static void Build(string corpusRoot, string? articlesRoot, string dbPath, string? signingKeyPem, DateTimeOffset now)
    {
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            File.ReadAllText(Path.Combine(corpusRoot, "manifest.json")), CorpusJson.Options)!;
        var publisherId = manifest.Publisher["id"];

        var docs = new List<DocRow>();
        var provisions = new List<ProvisionRow>();
        var events = new List<EventRow>();
        var observations = new List<ObservationRow>();

        foreach (var workDir in Directory.EnumerateDirectories(Path.Combine(corpusRoot, "works")))
        {
            var workMeta = JsonSerializer.Deserialize<WorkMeta>(
                File.ReadAllText(Path.Combine(workDir, "meta.json")), CorpusJson.Options)!;
            var versionsDir = Path.Combine(workDir, "versions");
            if (!Directory.Exists(versionsDir)) continue;

            foreach (var versionDir in Directory.EnumerateDirectories(versionsDir))
            {
                var meta = JsonSerializer.Deserialize<VersionMeta>(
                    File.ReadAllText(Path.Combine(versionDir, "meta.json")), CorpusJson.Options)!;

                var firstSighting = meta.Events.FirstOrDefault(e => e.Event == "first_sighting")?.ObservedFrom
                                    ?? meta.Events.FirstOrDefault()?.ObservedFrom ?? "";
                var withdrawn = meta.Events.Any(e => e.Event == "withdrawn_from_source")
                                && meta.Events.Last(e => e.Event is "withdrawn_from_source" or "resighted").Event == "withdrawn_from_source";

                foreach (var e in meta.Events)
                    events.Add(new EventRow(meta.LexId, e.Scope ?? "version", e.Event, e.ObservedFrom, e.Detail));

                foreach (var expr in meta.Expressions)
                {
                    var exprValidFrom = expr.ValidFrom ?? meta.ValidFrom;

                    // lex-index/2: text comes from the derived consumption layer, per provision;
                    // the hash chain always runs derived text -> verbatim file -> observation.
                    var rid = $"{meta.LexId}|{expr.Language}|{exprValidFrom}";
                    var derivedJson = articlesRoot is null ? null : Path.Combine(
                        articlesRoot, publisherId, "works", workMeta.Slug, "versions",
                        Path.GetFileName(versionDir), $"{expr.Language}.json");
                    var hasDerived = derivedJson is not null && File.Exists(derivedJson);
                    if (hasDerived)
                    {
                        using var dd = JsonDocument.Parse(File.ReadAllText(derivedJson!));
                        var seq = 0;
                        foreach (var p in dd.RootElement.GetProperty("provisions").EnumerateArray())
                        {
                            provisions.Add(new ProvisionRow(
                                Rid: rid,
                                Seq: seq++,
                                Anchor: p.GetProperty("anchor").GetString()!,
                                ProvisionId: p.GetProperty("provision_id").GetString()!,
                                PType: p.GetProperty("type").GetString() ?? "article",
                                Num: p.TryGetProperty("num", out var n) ? n.GetString() : null,
                                Heading: p.TryGetProperty("heading", out var h) ? h.GetString() : null,
                                Path: p.TryGetProperty("path", out var pa) && pa.ValueKind == JsonValueKind.Array
                                    ? string.Join(" / ", pa.EnumerateArray().Select(x => x.GetString())) is { Length: > 0 } joined ? joined : null
                                    : null,
                                ArticleValidFrom: p.TryGetProperty("article_valid_from", out var av) ? av.GetString() : null,
                                WorkTitle: workMeta.Title,
                                TextMd: p.GetProperty("text_md").GetString() ?? "",
                                TextSha: p.GetProperty("text_sha256").GetString() ?? ""));
                        }
                    }

                    docs.Add(new DocRow(
                        Key: meta.LexId,
                        Collection: publisherId,
                        GroupKey: workMeta.Slug,
                        GroupIdentifier: workMeta.WorkIdentifier,
                        Kind: meta.DocumentType,
                        Language: expr.Language,
                        ValidFrom: exprValidFrom,
                        ValidTo: expr.ValidTo,
                        ValidTimeSource: expr.ValidTimeSource,
                        ObservedFrom: firstSighting,
                        Withdrawn: withdrawn,
                        TextAvailable: expr.Text.Available,
                        TextPublic: manifest.TextPublic && expr.Text.Available && hasDerived,
                        RecordSha: meta.RecordSha256,
                        BodySha: expr.Observations.LastOrDefault()?.Sha256,
                        SourceUri: expr.SourceUri,
                        Title: expr.Title ?? workMeta.Title,
                        TitleShort: expr.TitleShort ?? workMeta.Title,
                        Body: null,
                        PublicationDate: meta.PublicationDate,
                        StatusNote: meta.InForceStatus));

                    // Observation chains: obs N's observed_to = obs N+1's observed_from; last closed by tombstone.
                    for (var i = 0; i < expr.Observations.Count; i++)
                    {
                        var o = expr.Observations[i];
                        string? observedTo = i + 1 < expr.Observations.Count
                            ? expr.Observations[i + 1].ObservedFrom
                            : (withdrawn ? meta.Events.Last(e => e.Event == "withdrawn_from_source").ObservedFrom : null);
                        observations.Add(new ObservationRow(meta.LexId, expr.Language, exprValidFrom,
                            o.Sha256, o.SourceUri, o.ObservedFrom, observedTo));
                    }
                }
            }
        }

        var stamp = new Dictionary<string, string>
        {
            ["collection"] = publisherId,
            ["publisher_name"] = manifest.Publisher.GetValueOrDefault("name", publisherId),
            ["jurisdiction"] = manifest.Publisher.GetValueOrDefault("jurisdiction", ""),
            ["tier"] = manifest.Tier,
            ["history_begins"] = manifest.HistoryBegins,
            ["text_included"] = manifest.TextIncluded ? "true" : "false",
            ["text_public"] = manifest.TextPublic ? "true" : "false",
            ["attribution"] = manifest.Attribution,
            ["source_terms_url"] = manifest.SourceTermsUrl ?? "",
            ["modifications"] = manifest.Modifications ?? "",
            ["notice"] = ReadIfExists(Path.Combine(corpusRoot, "NOTICE")),
            ["corpus_commit"] = GitCommit(corpusRoot),
            ["built_at"] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["ingester_version"] = manifest.IngesterVersion,
            ["works"] = docs.Select(d => d.GroupKey).Distinct().Count().ToString(),
            ["versions"] = docs.Select(d => d.Key).Distinct().Count().ToString(),
        };

        stamp["derived_provisions"] = provisions.Count.ToString();
        IndexBuilder.Build(dbPath, stamp, docs, provisions, events, observations, signingKeyPem);
        Console.Error.WriteLine($"  [index] {dbPath}: {docs.Count} rows, {provisions.Count} provisions, {events.Count} events, signed={(signingKeyPem is not null)}");
    }

    private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    private static string GitCommit(string dir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && output.Length >= 7 ? output[..7] : "uncommitted";
        }
        catch { return "uncommitted"; }
    }
}
