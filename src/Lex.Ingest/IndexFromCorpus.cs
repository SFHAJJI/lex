using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Derive;
using Lex.Index;
using Lex.Temporal;

namespace Lex.Ingest;

/// <summary>
/// Maps a corpus tree (HEAD only — §7.4/§8.2) into generic index rows and builds the
/// per-publisher index file with a signed stamp (D40). Build time is injected (F9).
/// </summary>
public static class IndexFromCorpus
{
    public static void Build(string corpusRoot, string? articlesRoot, string dbPath, string? signingKeyPem,
                             DateTimeOffset now, SemanticBuildOptions? semantic = null,
                             string? codeCommit = null,
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
        var corpusManifestPath = Path.Combine(corpusRoot, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            File.ReadAllText(corpusManifestPath), CorpusJson.Options)!;
        if (manifest.Schema != ManifestDoc.CurrentSchema)
            throw new InvalidDataException(
                $"Index construction requires a fresh {ManifestDoc.CurrentSchema} input.");
        if (manifest.Canon != ManifestDoc.CurrentCanon)
            throw new InvalidDataException(
                $"Index construction requires canon '{ManifestDoc.CurrentCanon}'.");
        if (manifest.BuildIssues is null || manifest.BuildIssues.Count > 0)
            throw new InvalidDataException(
                "Index construction is blocked while the corpus contains rejected acquisition evidence.");
        var corpusIntegrity = CorpusIntegrity.Verify(corpusRoot);
        if (!corpusIntegrity.IsValid)
            throw new InvalidDataException(
                "Index construction requires an integrity-valid corpus:\n"
                + string.Join("\n", corpusIntegrity.Errors));
        var ingesterCodeCommit = CodeIdentity.RequireFullCommit(
            manifest.IngesterCodeCommit, "manifest ingester_code_commit");
        CorpusWriter.ValidateSourceConfiguration(
            manifest.SourceConfigurationKind, manifest.SourceConfigurationSha256);
        var builderCodeCommit = NormalizeCodeCommit(codeCommit);
        var publisherId = manifest.Publisher["id"];
        DerivationGeneration.Entry? generation = null;
        string? generationSha256 = null;
        if (articlesRoot is not null)
        {
            generation = DerivationGeneration.ReadPublisher(articlesRoot, publisherId);
            generationSha256 = DerivationGeneration.Sha256File(
                Path.Combine(articlesRoot, DerivationGeneration.FileName));
            RequireEqual(generation.CorpusCommit, stampedCorpusCommit,
                "generation corpus_commit", "selected corpus commit");
            RequireEqual(generation.CorpusManifestSha256,
                DerivationGeneration.Sha256File(corpusManifestPath),
                "generation corpus_manifest_sha256", "selected corpus manifest");
            RequireEqual(generation.IngesterCodeCommit, ingesterCodeCommit,
                "generation ingester_code_commit", "corpus manifest ingester identity");
        }

        var docs = new List<DocRow>();
        var provisions = new List<ProvisionRow>();
        var events = new List<EventRow>();
        var observations = new List<ObservationRow>();
        var derivedProfiles = new SortedSet<string>(StringComparer.Ordinal);
        var indexedProvisionStates = new HashSet<(string Work, string Language, string Anchor, string TextSha)>();
        var indexedProvisionAnchors = new HashSet<(string Work, string Language, string Anchor)>();
        var derivedProvisionCount = 0;
        var excludedEmptyProvisions = 0;

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
                VersionIdentity.RequireCanonical(
                    Path.GetFileName(versionDir), meta.ValidFrom,
                    meta.PublisherVersionIdentifier, meta.LexId,
                    $"{meta.Publisher}:{workMeta.Slug}");

                var firstSighting = meta.Events.FirstOrDefault(e => e.Event == "first_sighting")?.ObservedFrom
                                    ?? meta.Events.FirstOrDefault()?.ObservedFrom ?? "";
                var withdrawn = meta.Events.Any(e => e.Event == "withdrawn_from_source")
                                && meta.Events.Last(e => e.Event is "withdrawn_from_source" or "resighted").Event == "withdrawn_from_source";

                foreach (var e in meta.Events)
                    events.Add(new EventRow(
                        meta.LexId, e.Scope ?? "version", e.Event,
                        e.ObservedFrom, e.Detail, e.FirstMissedAt,
                        e.RunsMissed, e.RunIdentity));

                foreach (var expr in meta.Expressions)
                {
                    var freshPrimary = expr.Observations.Where(observation =>
                            observation.Format is null
                            && observation.Http is not null
                            && observation.ObservedFrom == manifest.ObservationRun)
                        .ToArray();
                    if (freshPrimary.Length > 1)
                        throw new InvalidDataException(
                            $"{meta.LexId}/{expr.Language} has ambiguous effective response identity.");
                    var primaryObservation = freshPrimary.SingleOrDefault()
                        ?? expr.Observations.LastOrDefault(observation =>
                            observation.Format is null);
                    var effectiveSourceUri = primaryObservation?.SourceUri
                        ?? expr.SourceUri;
                    var exprValidFrom = expr.ValidFrom ?? meta.ValidFrom;
                    var languageWorkTitle = string.Equals(
                        workMeta.TitleLanguage, expr.Language, StringComparison.Ordinal)
                            ? workMeta.Title
                            : null;

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
                    var indexedInExpression = 0;
                    if (hasDerived)
                    {
                        using var dd = JsonDocument.Parse(File.ReadAllText(derivedJson!));
                        var derivedSourceUri = dd.RootElement.TryGetProperty(
                                "derived_from", out var derivedFrom)
                            && derivedFrom.TryGetProperty(
                                "source_uri", out var source)
                            ? source.GetString() : null;
                        if (!string.Equals(derivedSourceUri, effectiveSourceUri,
                                StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"{derivedJson}: source_uri does not match the effective publisher response URI.");
                        if (dd.RootElement.TryGetProperty("generator", out var gen)
                            && gen.TryGetProperty("profile", out var pf))
                        {
                            profile = pf.GetString();
                            if (!string.IsNullOrWhiteSpace(profile))
                                derivedProfiles.Add(profile);
                        }
                        var seq = 0;
                        foreach (var p in dd.RootElement.GetProperty("provisions").EnumerateArray())
                        {
                            derivedProvisionCount++;
                            var anchor = p.GetProperty("anchor").GetString();
                            if (string.IsNullOrWhiteSpace(anchor))
                                throw new InvalidDataException(
                                    $"{derivedJson}: provision anchor is required.");
                            var provisionId = p.GetProperty("provision_id").GetString();
                            if (string.IsNullOrWhiteSpace(provisionId))
                                throw new InvalidDataException(
                                    $"{derivedJson}: provision provision_id is required.");
                            var textMd = p.GetProperty("text_md").GetString() ?? "";
                            var textSha = p.GetProperty("text_sha256").GetString() ?? "";
                            var actualTextSha = Convert.ToHexStringLower(
                                SHA256.HashData(Encoding.UTF8.GetBytes(textMd)));
                            if (!string.Equals(textSha, actualTextSha, StringComparison.Ordinal))
                                throw new InvalidDataException(
                                    $"{derivedJson}: provision {anchor} text_sha256 does not match text_md.");
                            var candidate = new ProvisionRow(
                                Rid: rid,
                                Seq: seq,
                                Anchor: anchor,
                                ProvisionId: provisionId,
                                PType: p.GetProperty("type").GetString() ?? "article",
                                Num: p.TryGetProperty("num", out var n) ? n.GetString() : null,
                                Heading: p.TryGetProperty("heading", out var h) ? h.GetString() : null,
                                Path: p.TryGetProperty("path", out var pa) && pa.ValueKind == JsonValueKind.Array
                                    ? string.Join(" / ", pa.EnumerateArray().Select(x => x.GetString())) is { Length: > 0 } joined ? joined : null
                                    : null,
                                ArticleValidFrom: p.TryGetProperty("article_valid_from", out var av) ? av.GetString() : null,
                                WorkTitle: languageWorkTitle,
                                TextMd: textMd,
                                TextSha: textSha,
                                CitationsJson: p.TryGetProperty("citations", out var cit)
                                    && cit.ValueKind == JsonValueKind.Array && cit.GetArrayLength() > 0
                                    ? cit.GetRawText() : null);
                            if (string.IsNullOrWhiteSpace(textMd))
                            {
                                excludedEmptyProvisions++;
                                continue;
                            }
                            provisions.Add(candidate);
                            seq++;
                            indexedInExpression++;
                            indexedProvisionStates.Add((workMeta.Slug, expr.Language, anchor, textSha));
                            indexedProvisionAnchors.Add((workMeta.Slug, expr.Language, anchor));
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
                        TextPublic: manifest.TextPublic && expr.Text.Available && indexedInExpression > 0,
                        RecordSha: meta.RecordSha256,
                        BodySha: primaryObservation?.Sha256,
                        SourceUri: effectiveSourceUri,
                        Title: expr.Title ?? languageWorkTitle,
                        TitleShort: expr.TitleShort ?? languageWorkTitle,
                        Body: null,
                        PublicationDate: meta.PublicationDate,
                        StatusNote: meta.InForceStatus,
                        Profile: indexedInExpression > 0 ? profile : null,
                        Hierarchy: meta.Raw.GetValueOrDefault("hierarchy"),
                        // Engineering scope selects what to acquire; it is not publisher legal
                        // metadata and can never become a domain filter or FTS field. Official
                        // subject metadata travels through the typed PublisherMetadata records.
                        Domains: null,
                        ActForm: meta.Raw.GetValueOrDefault("legal_form"),
                        BindingStatus: meta.Raw.GetValueOrDefault("binding_status"),
                        ConsolidationStatus: meta.Raw.GetValueOrDefault("consolidation_status"),
                        PublisherMetadata: (meta.PublisherMetadata ?? [])
                            .Where(value => value.Language is null || value.Language == expr.Language)
                            .Select(value => new PublisherMetadataRow(
                                value.Kind, value.Identifier, value.Language, value.Label,
                                value.SourceUri,
                                CitationIdentity: value.Kind == "legilux_same_as"))
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

        if (generation is not null
            && !generation.Profiles.SequenceEqual(derivedProfiles, StringComparer.Ordinal))
            throw new InvalidDataException(
                "Derived article profiles do not match generation.json.");

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
            // code_commit remains the compatibility name for the index builder identity.
            ["code_commit"] = builderCodeCommit,
            ["builder_code_commit"] = builderCodeCommit,
            ["ingester_code_commit"] = ingesterCodeCommit,
            ["built_at"] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["ingester_version"] = manifest.IngesterVersion,
            ["works"] = docs.Select(d => d.GroupKey).Distinct().Count().ToString(),
            ["versions"] = docs.Select(d => d.Key).Distinct().Count().ToString(),
            ["build_issues_json"] = buildIssuesJson,
            ["build_issues_digest"] = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(buildIssuesJson))),
        };
        if (generation is not null)
        {
            stamp["deriver_code_commit"] = generation.DeriverCodeCommit;
            stamp["deriver_tree_id"] = generation.DeriverTreeId;
            stamp["corpus_manifest_sha256"] = generation.CorpusManifestSha256;
            stamp["generation_sha256"] = generationSha256!;
            stamp["profiles_sha256"] = generation.ProfilesSha256;
        }
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
                        {
                            var textSha = s.GetProperty("text_sha256").GetString() ?? "";
                            if (!indexedProvisionStates.Contains((slug, language, a.Name, textSha)))
                                continue;
                            provisionStates.Add(new ProvisionStateRow(
                                GroupKey: slug,
                                Language: language,
                                IsPrimaryLanguage: language == primaryLanguage,
                                Anchor: a.Name,
                                ValidFrom: s.GetProperty("valid_from").GetString() ?? "",
                                ValidTo: s.TryGetProperty("valid_to", out var vt) ? vt.GetString() : null,
                                TextSha: textSha,
                                InVersion: s.TryGetProperty("in_version", out var iv) ? iv.GetString() : null,
                                ArticleValidFrom: s.TryGetProperty("article_valid_from", out var av) ? av.GetString() : null,
                                ValidityConflict: s.TryGetProperty("validity_conflict", out var vc) && vc.GetBoolean()));
                        }
                }
                void AddEvents(string language, JsonElement eventsForLanguage)
                {
                    foreach (var e in eventsForLanguage.EnumerateArray())
                    {
                        var from = e.TryGetProperty("from", out var f) ? f.GetString() : null;
                        var to = e.TryGetProperty("to", out var t) ? t.GetString() : null;
                        var anchor = e.TryGetProperty("anchor", out var an) ? an.GetString() : null;
                        var textSha = e.TryGetProperty("text_sha256", out var ts) ? ts.GetString() : null;
                        var referencedAnchors = new[] { from, to, anchor }.OfType<string>().ToArray();
                        if (referencedAnchors.Length == 0
                            || referencedAnchors.Any(value =>
                                !indexedProvisionAnchors.Contains((slug, language, value)))
                            || textSha is not null && referencedAnchors.Any(value =>
                                !indexedProvisionStates.Contains((slug, language, value, textSha))))
                            continue;
                        anchorEventRows.Add(new AnchorEventRow(
                            GroupKey: slug,
                            Language: language,
                            IsPrimaryLanguage: language == primaryLanguage,
                            EType: e.GetProperty("type").GetString() ?? "",
                            FromAnchor: from,
                            ToAnchor: to,
                            Anchor: anchor,
                            TextSha: textSha,
                            AtVersion: e.TryGetProperty("at_version", out var atv) ? atv.GetString() : null));
                    }
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

        stamp["derived_provisions"] = derivedProvisionCount.ToString();
        stamp["indexed_provisions"] = provisions.Count.ToString();
        stamp["excluded_empty_provisions"] = excludedEmptyProvisions.ToString();
        IndexBuilder.Build(dbPath, stamp, docs, provisions, events, observations, signingKeyPem,
            provisionStates, anchorEventRows, semantic);
        Console.Error.WriteLine($"  [index] {dbPath}: {docs.Count} rows, {provisions.Count} provisions, {provisionStates.Count} states, {anchorEventRows.Count} anchor events, signed={(signingKeyPem is not null)}");
    }

    private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    private static void RequireEqual(
        string actual, string expected, string actualName, string expectedName)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"{actualName} does not match the {expectedName}.");
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
        return CodeIdentity.RequireFullCommit(value, "index builder code commit");
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
