using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Index;

namespace Lex.Ingest;

/// <summary>
/// Maps a corpus tree (HEAD only — §7.4/§8.2) into generic index rows and builds the
/// per-publisher index file with a signed stamp (D40). Build time is injected (F9).
/// </summary>
public static class IndexFromCorpus
{
    public static void Build(string corpusRoot, string? articlesRoot, string dbPath, string? signingKeyPem,
                             DateTimeOffset now, SemanticBuildOptions? semantic = null,
                             string? workEnrichmentPath = null, string? codeCommit = null,
                             string? articlesCommit = null, string? corpusCommit = null)
    {
        if ((articlesRoot is null) != (articlesCommit is null))
            throw new InvalidDataException(
                "The derived articles path and its full Git commit must be supplied together.");
        var stampedCorpusCommit = corpusCommit is null
            ? GitCommit(corpusRoot)
            : RequireCleanGitCheckout(corpusRoot, corpusCommit, "corpus");
        var stampedArticlesCommit = articlesRoot is null ? null
            : RequireCleanGitCheckout(articlesRoot, articlesCommit!, "derived articles");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            File.ReadAllText(Path.Combine(corpusRoot, "manifest.json")), CorpusJson.Options)!;
        var publisherId = manifest.Publisher["id"];
        if (workEnrichmentPath is not null
            && manifest.PublisherDiscoverySchema != ManifestDoc.CurrentPublisherDiscoverySchema)
            throw new InvalidDataException(
                "The corpus predates publisher-discovery migration. Re-ingest it before applying reviewed work aliases.");

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

                    // Text comes from the derived consumption layer, per provision;
                    // the hash chain always runs derived text -> verbatim file -> observation.
                    var rid = $"{meta.LexId}|{expr.Language}|{exprValidFrom}";
                    var derivedJson = articlesRoot is null ? null : Path.Combine(
                        articlesRoot, publisherId, "works", workMeta.Slug, "versions",
                        Path.GetFileName(versionDir), $"{expr.Language}.json");
                    var hasDerived = derivedJson is not null && File.Exists(derivedJson);
                    // Which profile produced this version's text. Recorded per version because one
                    // law is routinely publisher XML on some dates and a read PDF on others, so a
                    // work-level answer would be wrong for half of them.
                    string? profile = null;
                    if (hasDerived)
                    {
                        using var dd = JsonDocument.Parse(File.ReadAllText(derivedJson!));
                        if (dd.RootElement.TryGetProperty("generator", out var gen)
                            && gen.TryGetProperty("profile", out var pf))
                            profile = pf.GetString();
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
                                TextSha: p.GetProperty("text_sha256").GetString() ?? "",
                                CitationsJson: p.TryGetProperty("citations", out var cit)
                                    && cit.ValueKind == JsonValueKind.Array && cit.GetArrayLength() > 0
                                    ? cit.GetRawText() : null));
                        }
                    }

                    docs.Add(new DocRow(
                        Key: meta.LexId,
                        Collection: publisherId,
                        GroupKey: workMeta.Slug,
                        GroupIdentifier: workMeta.WorkIdentifier,
                        // A sparse version record does not erase a stable work classification.
                        // Legilux occasionally omits typeDocument on one consolidation while the
                        // work-level catalogue still has a dominant publisher class.
                        Kind: meta.DocumentType ?? workMeta.DocumentType,
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
                        StatusNote: meta.InForceStatus,
                        Profile: profile,
                        Hierarchy: meta.Raw.GetValueOrDefault("hierarchy"),
                        Domains: NormalizeDomains(meta.Raw.GetValueOrDefault("domains") ?? meta.Raw.GetValueOrDefault("scope_reasons")),
                        ActForm: meta.Raw.GetValueOrDefault("legal_form"),
                        BindingStatus: meta.Raw.GetValueOrDefault("binding_status"),
                        ConsolidationStatus: meta.Raw.GetValueOrDefault("consolidation_status"),
                        PublisherMetadata: (meta.PublisherMetadata ?? [])
                            .Where(value => value.Language is null || value.Language == expr.Language)
                            .Select(value => new PublisherMetadataRow(
                                value.Kind, value.Identifier, value.Language, value.Label, value.SourceUri))
                            .ToArray(),
                        DocumentRoles: meta.DocumentRoles ?? []));

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

        var buildIssues = manifest.BuildIssues.OrderBy(issue => issue.Work, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Detail, StringComparer.Ordinal).ToArray();
        var buildIssuesJson = JsonSerializer.Serialize(buildIssues, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = publisherId,
            ["publisher_name"] = manifest.Publisher.GetValueOrDefault("name", publisherId),
            ["jurisdiction"] = manifest.Publisher.GetValueOrDefault("jurisdiction", ""),
            // Legal time is publisher-specific. EUR-Lex expression dates identify official
            // consolidated wording states; Legilux applicability dates identify when text
            // applied. New publishers must declare the semantic explicitly in their manifest.
            ["timeline_semantics"] = manifest.Publisher.GetValueOrDefault("timeline_semantics",
                publisherId == "eu-eurlex" ? "official_consolidation_state" : "publisher_applicability"),
            ["tier"] = manifest.Tier,
            ["history_begins"] = manifest.HistoryBegins,
            ["text_included"] = manifest.TextIncluded ? "true" : "false",
            ["text_public"] = manifest.TextPublic ? "true" : "false",
            ["attribution"] = manifest.Attribution,
            ["source_terms_url"] = manifest.SourceTermsUrl ?? "",
            ["modifications"] = manifest.Modifications ?? "",
            ["notice"] = ReadIfExists(Path.Combine(corpusRoot, "NOTICE")),
            ["corpus_commit"] = stampedCorpusCommit,
            ["code_commit"] = NormalizeCodeCommit(codeCommit),
            ["built_at"] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["ingester_version"] = manifest.IngesterVersion,
            ["works"] = docs.Select(d => d.GroupKey).Distinct().Count().ToString(),
            ["versions"] = docs.Select(d => d.Key).Distinct().Count().ToString(),
            ["build_issues_json"] = buildIssuesJson,
            ["build_issues_digest"] = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(buildIssuesJson))),
        };
        if (stampedArticlesCommit is not null)
            stamp["articles_commit"] = stampedArticlesCommit;
        if (manifest.ScopeExpectedWorks is { } scopeExpectedWorks)
            stamp["scope_expected_works"] = scopeExpectedWorks.ToString();

        // Per-anchor time axis + lifecycle events, from the derived layer's history.json
        var provisionStates = new List<ProvisionStateRow>();
        var anchorEventRows = new List<AnchorEventRow>();
        var articlesWorks = articlesRoot is null ? null : Path.Combine(articlesRoot, publisherId, "works");
        if (articlesWorks is not null && Directory.Exists(articlesWorks))
        {
            foreach (var wd in Directory.EnumerateDirectories(articlesWorks))
            {
                var histPath = Path.Combine(wd, "history.json");
                if (!File.Exists(histPath)) continue;
                var slug = Path.GetFileName(wd);
                using var hd = JsonDocument.Parse(File.ReadAllText(histPath));
                var primaryLanguage = hd.RootElement.TryGetProperty("primary_language", out var primary)
                    ? primary.GetString() ?? "und" : "und";
                void AddStates(string language, JsonElement anchors)
                {
                    foreach (var a in anchors.EnumerateObject())
                        foreach (var s in a.Value.EnumerateArray())
                            provisionStates.Add(new ProvisionStateRow(
                                GroupKey: slug,
                                Language: language,
                                IsPrimaryLanguage: language == primaryLanguage,
                                Anchor: a.Name,
                                ValidFrom: s.GetProperty("valid_from").GetString() ?? "",
                                ValidTo: s.TryGetProperty("valid_to", out var vt) ? vt.GetString() : null,
                                TextSha: s.GetProperty("text_sha256").GetString() ?? "",
                                InVersion: s.TryGetProperty("in_version", out var iv) ? iv.GetString() : null,
                                ArticleValidFrom: s.TryGetProperty("article_valid_from", out var av) ? av.GetString() : null,
                                ValidityConflict: s.TryGetProperty("validity_conflict", out var vc) && vc.GetBoolean()));
                }
                void AddEvents(string language, JsonElement eventsForLanguage)
                {
                    foreach (var e in eventsForLanguage.EnumerateArray())
                        anchorEventRows.Add(new AnchorEventRow(
                            GroupKey: slug,
                            Language: language,
                            IsPrimaryLanguage: language == primaryLanguage,
                            EType: e.GetProperty("type").GetString() ?? "",
                            FromAnchor: e.TryGetProperty("from", out var f) ? f.GetString() : null,
                            ToAnchor: e.TryGetProperty("to", out var t) ? t.GetString() : null,
                            Anchor: e.TryGetProperty("anchor", out var an) ? an.GetString() : null,
                            TextSha: e.TryGetProperty("text_sha256", out var ts) ? ts.GetString() : null,
                            AtVersion: e.TryGetProperty("at_version", out var atv) ? atv.GetString() : null));
                }

                if (hd.RootElement.TryGetProperty("anchors_by_language", out var languageHistories))
                    foreach (var language in languageHistories.EnumerateObject())
                        AddStates(language.Name, language.Value);
                else if (hd.RootElement.TryGetProperty("anchors", out var legacyAnchors))
                    AddStates(primaryLanguage, legacyAnchors);

                if (hd.RootElement.TryGetProperty("anchor_events_by_language", out var languageEvents))
                    foreach (var language in languageEvents.EnumerateObject())
                        AddEvents(language.Name, language.Value);
                else if (hd.RootElement.TryGetProperty("anchor_events", out var legacyEvents))
                    AddEvents(primaryLanguage, legacyEvents);
            }
        }

        stamp["derived_provisions"] = provisions.Count.ToString();
        var heldWorks = docs.Select(doc => (doc.GroupKey, doc.Language)).ToHashSet();
        var workSearch = workEnrichmentPath is null
            ? null
            : WorkEnrichmentFile.Load(workEnrichmentPath, publisherId, heldWorks);
        IndexBuilder.Build(dbPath, stamp, docs, provisions, events, observations, signingKeyPem,
            provisionStates, anchorEventRows, semantic, workSearch);
        Console.Error.WriteLine($"  [index] {dbPath}: {docs.Count} rows, {provisions.Count} provisions, {provisionStates.Count} states, {anchorEventRows.Count} anchor events, signed={(signingKeyPem is not null)}");
    }

    private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    private static string? NormalizeDomains(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var domains = value.Trim('|').Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return domains.Count == 0 ? null : "|" + string.Join('|', domains) + "|";
    }

    private static string GitCommit(string dir)
    {
        try
        {
            var result = RunGit(dir, "rev-parse", "HEAD");
            var output = result.Output.Trim();
            return result.ExitCode == 0 && output.Length == 40 && output.All(Uri.IsHexDigit)
                ? output.ToLowerInvariant()
                : "uncommitted";
        }
        catch { return "uncommitted"; }
    }

    private static string NormalizeCodeCommit(string? value)
    {
        if (value is null) return "uncommitted";
        var normalized = value.ToLowerInvariant();
        if (normalized.Length != 40 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("The commit must be a full 40-character Git SHA.");
        return normalized;
    }

    private static string RequireCleanGitCheckout(string directory, string expectedCommit, string label)
    {
        var expected = NormalizeCodeCommit(expectedCommit);
        var actual = GitCommit(directory);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The {label} checkout is at {actual}, expected {expected}.");

        var result = RunGit(directory, "status", "--porcelain", "--untracked-files=all");
        if (result.ExitCode != 0)
            throw new InvalidDataException(
                $"Could not verify the {label} checkout: {result.Error.Trim()}");
        if (!string.IsNullOrWhiteSpace(result.Output))
            throw new InvalidDataException($"The {label} checkout has uncommitted changes.");
        return expected;
    }

    private static (int ExitCode, string Output, string Error) RunGit(
        string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Git verification timed out.");
        }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        return (process.ExitCode, output.Result, error.Result);
    }
}
