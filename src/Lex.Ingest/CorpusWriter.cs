using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

internal sealed record CorpusPlannedWork(
    WorkRef Work, IReadOnlyList<VersionRecord> Versions);

/// <summary>
/// The single component that writes corpus files (C1 layout, C3 rules, F12 discipline).
/// Adapters never touch disk (F8). Version directories combine valid_from with the full hash of
/// publisher_version_identifier, so coordinates never change as the known set grows. meta.json
/// changes only when observed reality changes; every change
/// appends the corresponding chain entry (F12). Body files are append-only; a declared
/// metadata-only expression writes no body and records the reason instead.
/// </summary>
public sealed class CorpusWriter(
    string corpusRoot, DateTimeOffset now, string codeCommit, TextWriter? progress = null)
{
    private readonly string _now = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    private readonly string _ingesterCodeCommit = CodeIdentity.RequireFullCommit(
        codeCommit, "ingester code commit");
    private readonly TextWriter _progress = progress ?? Console.Error;
    public int Created { get; private set; }
    public int Updated { get; private set; }
    public int Unchanged { get; private set; }
    public bool Committed { get; private set; }
    public bool Accepted { get; private set; }
    public IReadOnlyList<SourceBuildIssue> BuildIssues { get; private set; } = [];

    public Task WriteAsync(
        ISourceAdapter adapter, CancellationToken ct, bool requireComplete = false) =>
        WriteAsync(adapter, ct, requireComplete, validatePlan: null);

    internal async Task WriteAsync(
        ISourceAdapter adapter,
        CancellationToken ct,
        bool requireComplete,
        Action<IReadOnlyList<CorpusPlannedWork>>? validatePlan)
    {
        var existingManifest = RefuseLegacyAppend(corpusRoot);
        var desc = adapter.Describe();
        var pub = desc.Publisher;
        using var candidate = new CorpusCandidate(corpusRoot);

        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var langs = new HashSet<string>(StringComparer.Ordinal);
        string? earliest = null, latest = null;
        int works = 0, versions = 0, expressions = 0, expressionsWithText = 0;
        var seenVersionMetadata = new HashSet<string>(PathComparer);

        // Materialise the metadata-only plan before fetching bodies. Adapters already hold their
        // version catalogue in memory, so this adds no publisher body requests and lets the log
        // report a real denominator. A percentage without a denominator is not actionable; an
        // elapsed time without observed throughput is not an ETA.
        var plan = new List<CorpusPlannedWork>();
        var localBuildIssues = new List<SourceBuildIssue>();
        var enumeratedWorks = 0;
        await foreach (var work in adapter.EnumerateWorks(ct))
        {
            enumeratedWorks++;
            var versionsOfWork = await adapter.FetchVersions(work, ct);
            if (versionsOfWork.Count > 0)
                plan.Add(new CorpusPlannedWork(work, versionsOfWork));
            else
                localBuildIssues.Add(new SourceBuildIssue(
                    "no_versions", work.Slug, "The publisher enumeration returned no version records."));
        }
        var sourceInventory = (adapter as ISourceBuildInventory)?.GetBuildInventory();
        var expectedWorks = Math.Max(enumeratedWorks, sourceInventory?.ExpectedWorks ?? 0);
        var retryMaximumAttempts = sourceInventory?.RetryMaximumAttempts ?? 1;
        var sourceConfigurationKind = sourceInventory?.SourceConfigurationKind ?? "code_only";
        var sourceConfigurationSha256 = sourceInventory?.SourceConfigurationSha256;
        ValidateSourceConfiguration(sourceConfigurationKind, sourceConfigurationSha256);
        if (retryMaximumAttempts is < 1 or > 10)
            throw new InvalidDataException("The source retry maximum must be between 1 and 10 attempts.");
        if (sourceInventory?.EnumerationComplete == false || enumeratedWorks < expectedWorks)
            throw new SourceEnumerationIncompleteException(new SourceBuildIssue(
                "incomplete_enumeration", pub.Id,
                $"Publisher enumeration returned {enumeratedWorks} of {expectedWorks} expected works; the prior corpus remains unchanged."));
        // Engineering scope is an acquisition decision, not publisher legal metadata. Reject the
        // complete metadata plan before any body request or candidate write, and also refuse to
        // carry an old leaked key forward from an existing corpus.
        ValidatePlannedRawMetadata(sourceConfigurationKind, plan);
        if (sourceConfigurationKind == "engineering_scope"
            || existingManifest?.SourceConfigurationKind == "engineering_scope")
            ValidateExistingRawMetadata(corpusRoot, "engineering_scope");
        // Fresh migrations use this seam to prove that the metadata catalogue preserves every
        // held baseline identity before the first expression body is requested. The plan is the
        // same single source enumeration consumed below; the validator must not re-query the
        // publisher.
        validatePlan?.Invoke(plan);
        var totalExpressions = plan.Sum(item => item.Versions.Sum(version => (long)version.Expressions.Count));
        long processedExpressions = 0;
        var lastReportedPercent = -1;
        var progressClock = Stopwatch.StartNew();
        ReportProgress(pub.Id, processedExpressions, totalExpressions, progressClock.Elapsed, null);
        lastReportedPercent = totalExpressions == 0 ? 100 : 0;

        foreach (var (work, versionsOfWork) in plan)
        {
            works++;

            var workDir = Path.Combine(corpusRoot, "works", work.Slug);
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
            candidate.WriteIfChanged(Path.Combine(workDir, "meta.json"), JsonSerializer.Serialize(workMeta, CorpusJson.Options));

            var versionKeys = VersionKeys(workDir, versionsOfWork);
            foreach (var v in versionsOfWork.OrderBy(v => v.ValidFrom).ThenBy(v => v.Id.Value, StringComparer.Ordinal))
            {
                versions++;
                var vfrom = v.ValidFrom.ToString("yyyy-MM-dd");
                var vkey = versionKeys[v.Id.Value];

                if (v.TypeCode is not null) kinds[v.TypeCode] = kinds.GetValueOrDefault(v.TypeCode) + 1;
                foreach (var e in v.Expressions) langs.Add(e.Language);
                earliest = Min(earliest, vfrom);
                latest = Max(latest, vfrom);

                var versionDir = Path.Combine(workDir, "versions", vkey);
                var metaPath = Path.Combine(versionDir, "meta.json");
                seenVersionMetadata.Add(Path.GetFullPath(metaPath));

                var lexId = $"{pub.Id}:{work.Slug}:{vkey}";
                VersionMeta meta;
                var existing = File.Exists(metaPath);
                var changed = false;
                if (existing)
                {
                    meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(metaPath, ct), CorpusJson.Options)!;
                    if (meta.PublisherVersionIdentifier is not null
                        && meta.PublisherVersionIdentifier != v.Id.Value)
                        throw new InvalidDataException(
                            $"Publisher version identity changed for {lexId}; a full re-ingest is required.");
                    if (meta.PublisherVersionIdentifier is null)
                    {
                        meta.PublisherVersionIdentifier = v.Id.Value;
                        meta.Events.Add(new EventEntry
                        {
                            Event = "metadata_revised",
                            ObservedFrom = _now,
                            Scope = "version",
                            Detail = "fields=publisher_version_identifier",
                        });
                        changed = true;
                    }
                    var lifecycle = meta.Events.LastOrDefault(e =>
                        e.Event is "withdrawn_from_source" or "resighted");
                    if (lifecycle?.Event == "withdrawn_from_source")
                    {
                        meta.Events.Add(new EventEntry
                        {
                            Event = "resighted",
                            ObservedFrom = _now,
                            Scope = "version",
                            Detail = "publisher record returned to the current enumeration",
                        });
                        changed = true;
                    }
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
                            Event = "validity_revised",
                            ObservedFrom = _now,
                            Scope = "version",
                            Detail = $"valid_to: {meta.ValidTo ?? "null"} -> {newTo ?? "null"}"
                        });
                        meta.ValidTo = newTo;
                        foreach (var e in meta.Expressions) e.ValidTo = newTo;
                        changed = true;
                    }

                    // Publisher classifications can be refined without changing the legal text.
                    // Keep the latest normalized fields in the hashed record and append one
                    // transaction-time event so a scope or status migration is visible rather
                    // than silently rewriting history. Unknown old raw keys are retained.
                    var revised = new List<string>();
                    if (meta.DocumentType != v.TypeCode)
                    {
                        meta.DocumentType = v.TypeCode;
                        revised.Add("document_type");
                    }
                    if (meta.InForceStatus != v.InForceStatus)
                    {
                        meta.InForceStatus = v.InForceStatus;
                        revised.Add("in_force_status");
                    }
                    foreach (var (key, value) in v.Raw.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        if (meta.Raw.GetValueOrDefault(key) == value) continue;
                        meta.Raw[key] = value;
                        revised.Add($"raw.{key}");
                    }
                    var publisherMetadata = CanonicalPublisherMetadata(v.PublisherMetadata);
                    if (!(meta.PublisherMetadata ?? []).SequenceEqual(publisherMetadata))
                    {
                        meta.PublisherMetadata = publisherMetadata.Count == 0 ? null : publisherMetadata;
                        revised.Add("publisher_metadata");
                    }
                    var documentRoles = (v.DocumentRoles ?? []).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal).ToList();
                    if (!(meta.DocumentRoles ?? []).SequenceEqual(documentRoles, StringComparer.Ordinal))
                    {
                        meta.DocumentRoles = documentRoles.Count == 0 ? null : documentRoles;
                        revised.Add("document_roles");
                    }

                    foreach (var expression in v.Expressions)
                    {
                        var current = meta.Expressions.FirstOrDefault(e => e.Language == expression.Language);
                        if (current is null) continue;
                        if (current.Title != expression.Title)
                        {
                            current.Title = expression.Title;
                            revised.Add($"expressions.{expression.Language}.title");
                        }
                        if (current.TitleShort != expression.TitleShort)
                        {
                            current.TitleShort = expression.TitleShort;
                            revised.Add($"expressions.{expression.Language}.title_short");
                        }
                        if (current.SourceUri != expression.SourceUri)
                        {
                            current.SourceUri = expression.SourceUri;
                            current.Text.Url = expression.SourceUri;
                            revised.Add($"expressions.{expression.Language}.source_uri");
                        }
                    }
                    if (revised.Count > 0)
                    {
                        meta.Events.Add(new EventEntry
                        {
                            Event = "metadata_revised",
                            ObservedFrom = _now,
                            Scope = "version",
                            Detail = "fields=" + string.Join(',', revised),
                        });
                        changed = true;
                    }

                    // A publisher can expose a previously missing language on a later run.
                    // Reconcile by stable language identity rather than positional Zip, which
                    // would silently ignore the new expression and permanently undercount it.
                    foreach (var expression in v.Expressions)
                    {
                        if (meta.Expressions.Any(e => e.Language == expression.Language)) continue;
                        meta.Expressions.Add(CreateExpressionMeta(expression, desc.TextIncluded));
                        meta.Events.Add(new EventEntry
                        {
                            Event = "expression_added",
                            ObservedFrom = _now,
                            Scope = expression.Language,
                            Detail = $"language={expression.Language}",
                        });
                        changed = true;
                    }
                }
                else
                {
                    meta = new VersionMeta
                    {
                        LexId = lexId,
                        PublisherVersionIdentifier = v.Id.Value,
                        WorkIdentifier = v.WorkId.Value,
                        Publisher = pub.Id,
                        DocumentType = v.TypeCode,
                        ValidFrom = vfrom,
                        ValidTo = v.ValidTo?.ToString("yyyy-MM-dd"),
                        ValidTimeSource = v.ValidTimeSource,
                        InForceStatus = v.InForceStatus,
                        PublicationDate = v.PublicationDate?.ToString("yyyy-MM-dd"),
                        Events = [new EventEntry { Event = "first_sighting", ObservedFrom = _now }],
                        Expressions = v.Expressions.Select(e => CreateExpressionMeta(e, desc.TextIncluded)).ToList(),
                        Relations = v.Relations.Select(r => new Dictionary<string, string>
                        { ["type"] = r.Type, ["target"] = r.Target.Value }).ToList(),
                        Raw = new Dictionary<string, string>(v.Raw),
                        PublisherMetadata = CanonicalPublisherMetadata(v.PublisherMetadata) is { Count: > 0 } metadata
                            ? metadata : null,
                        DocumentRoles = (v.DocumentRoles ?? []).Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal).ToList() is { Count: > 0 } roles ? roles : null,
                    };
                }

                // Text-bearing publishers: fetch each expression's body once (verbatim bytes,
                // append-only — F12: an existing body file is never opened for writing).
                // Runs for new AND existing records so a failed night backfills later.
                var bodyAdded = false;
                var bodyFailures = new Dictionary<string, SourceBodyFetch>(StringComparer.Ordinal);
                if (desc.TextIncluded)
                {
                    foreach (var exprRec in v.Expressions)
                    {
                        var exprMeta = meta.Expressions.Single(e => e.Language == exprRec.Language);
                        // Only a PRIMARY observation (Format == null) settles this loop. Alt
                        // manifestation members are observations too, and counting them here let
                        // one successful Formex night disable the promised primary backfill
                        // permanently for an expression whose primary fetch failed that night.
                        if (exprMeta.Observations.Any(o => o.Format is null)) continue;
                        var fetched = await adapter.FetchBody(v, exprRec, ct);
                        ValidateBodyFetch(fetched);
                        retryMaximumAttempts = Math.Max(retryMaximumAttempts, fetched.Attempts);
                        if (fetched.Status == SourceBodyStatus.Retrieved
                            && string.IsNullOrWhiteSpace(fetched.Text))
                        {
                            // A 2xx whose body is empty is a failed fetch, not text. Storing it
                            // would mint a permanent zero-byte observation that counts as covered
                            // and, being an observation, would never be refetched.
                            fetched = new SourceBodyFetch(SourceBodyStatus.EmptyBody,
                                Detail: "publisher returned an empty body", Attempts: fetched.Attempts);
                        }
                        if (fetched.Status != SourceBodyStatus.Retrieved || fetched.Text is null)
                        {
                            bodyFailures[exprRec.Language] = fetched;
                            if (!exprMeta.Text.Available) exprMeta.Text.Reason = fetched.IssueCode;
                            continue;
                        }
                        var bytes = Encoding.UTF8.GetBytes(fetched.Text);
                        var ext = fetched.Text.TrimStart().StartsWith("<?xml", StringComparison.Ordinal) ? "xml" : "html";
                        var file = $"{exprMeta.Language}.{ext}";          // §3.3 rule 3, initial expression
                        var bodyPath = Path.Combine(versionDir, file);
                        if (!candidate.Exists(bodyPath)) await candidate.WriteBytesAsync(bodyPath, bytes, ct);
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

                    // D48: alternative structural manifestation (e.g. Formex 4). Stored as
                    // verbatim members under {lang}.{format}/ — one observation per member.
                    // Append-only like bodies; re-attempted nightly until the publisher serves it.
                    foreach (var exprRec in v.Expressions)
                    {
                        var exprMeta = meta.Expressions.Single(e => e.Language == exprRec.Language);
                        var hasAlt = exprMeta.Observations.Any(o => o.Format is not null);
                        if (!hasAlt)
                        {
                            var altResult = await adapter.FetchAltManifestation(v, exprRec, ct);
                            ValidateManifestationFetch(altResult);
                            retryMaximumAttempts = Math.Max(retryMaximumAttempts, altResult.Attempts);
                            var alt = altResult.Status == SourceBodyStatus.Retrieved
                                ? altResult.Value : null;
                            if (alt is not null)
                            {
                                var altDirName = $"{exprMeta.Language}.{alt.Format}";
                                var altDir = Path.Combine(versionDir, altDirName);
                                foreach (var member in alt.Members)
                                {
                                    var memberPath = Path.Combine(altDir, member.Name);
                                    if (!candidate.Exists(memberPath))
                                        await candidate.WriteBytesAsync(memberPath, member.Bytes, ct);
                                    exprMeta.Observations.Add(new ObservationEntry
                                    {
                                        File = $"{altDirName}/{member.Name}",
                                        Sha256 = Convert.ToHexStringLower(SHA256.HashData(member.Bytes)),
                                        SourceUri = alt.SourceUri,
                                        RetrievedAt = _now,
                                        ObservedFrom = _now,
                                        Format = alt.Format,
                                    });
                                }
                                hasAlt = true;
                                bodyAdded = true;
                            }
                            else if (bodyFailures.ContainsKey(exprRec.Language)
                                     && altResult.Status != SourceBodyStatus.PublisherMetadataOnly)
                            {
                                bodyFailures[exprRec.Language] = new SourceBodyFetch(
                                    altResult.Status, Detail: altResult.Detail, Attempts: altResult.Attempts);
                                exprMeta.Text.Reason = bodyFailures[exprRec.Language].IssueCode;
                            }
                        }
                        // an alt manifestation IS observed text: versions whose primary body was
                        // never fetchable (size cap, 404) become text-available through it
                        if (hasAlt && !exprMeta.Text.Available)
                        {
                            exprMeta.Text = new TextInfo { Available = true, Url = exprMeta.Text.Url ?? exprRec.SourceUri };
                            bodyAdded = true;
                        }
                        if (hasAlt) bodyFailures.Remove(exprRec.Language);
                    }

                    foreach (var (language, failure) in bodyFailures.OrderBy(item => item.Key, StringComparer.Ordinal))
                        localBuildIssues.Add(new SourceBuildIssue(failure.IssueCode, work.Slug,
                            $"version={vkey}; language={language}; attempts={failure.Attempts}; {failure.Detail}"));
                }
                else
                {
                    foreach (var expression in meta.Expressions)
                    {
                        expression.Text.Reason = "publisher_metadata_only";
                        localBuildIssues.Add(new SourceBuildIssue("publisher_metadata_only", work.Slug,
                            $"version={vkey}; language={expression.Language}; the source is declared metadata-only."));
                    }
                }

                expressions += meta.Expressions.Count;
                expressionsWithText += meta.Expressions.Count(expression =>
                    expression.Text.Available && expression.Observations.Count > 0);

                processedExpressions += v.Expressions.Count;
                var percent = totalExpressions == 0
                    ? 100
                    : (int)(processedExpressions * 100 / totalExpressions);
                if (percent > lastReportedPercent || processedExpressions == totalExpressions)
                {
                    ReportProgress(pub.Id, processedExpressions, totalExpressions,
                        progressClock.Elapsed, v.Raw.GetValueOrDefault("celex") ?? work.Slug);
                    lastReportedPercent = percent;
                }

                var canonicalRecordSha = CorpusHashes.RecordSha256(meta);
                var staleRecordSha = !CorpusHashes.Equal(meta.RecordSha256, canonicalRecordSha);
                if (existing && !changed && !bodyAdded && !staleRecordSha) { Unchanged++; continue; }
                if (existing) Updated++; else Created++;

                meta.RecordSha256 = canonicalRecordSha;
                await candidate.WriteTextAsync(metaPath,
                    JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n", ct);
            }
        }

        TombstoneMissingVersions(seenVersionMetadata, candidate);

        sourceInventory = (adapter as ISourceBuildInventory)?.GetBuildInventory();
        retryMaximumAttempts = Math.Max(
            retryMaximumAttempts, sourceInventory?.RetryMaximumAttempts ?? 1);
        if (retryMaximumAttempts is < 1 or > 10)
            throw new InvalidDataException("The observed source retry maximum must be between 1 and 10 attempts.");
        var buildIssues = localBuildIssues.Concat(sourceInventory?.Issues ?? [])
            .Distinct().OrderBy(issue => issue.Work, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Detail, StringComparer.Ordinal).ToList();
        BuildIssues = buildIssues;
        // A publisher offering no XML for an expression is not a failed acquisition. It is the
        // publisher telling us there is nothing to acquire in that format, and the corpus already
        // records it as Text.Reason with Available=false and counts it in expressions_without_text.
        // Every other code here IS a failure, and requireComplete exists so a partial upstream
        // response cannot write history.
        //
        // Counting the two together stopped the nightly outright. Legilux announces future-dated
        // consolidations before their XML exists, so 1,492 expressions were flagged
        // publisher_metadata_only and the whole candidate was discarded: no corpus commit, no
        // derive, no index refresh, for both publishers, every night since 2026-08-10. The count
        // can only grow as more future dates are announced, so this would never have recovered on
        // its own. Coverage the publisher does not offer must not block coverage it does.
        var blocking = buildIssues
            .Where(issue => !string.Equals(issue.Code, "publisher_metadata_only", StringComparison.Ordinal))
            .ToArray();
        if (buildIssues.Count > blocking.Length)
            _progress.WriteLine(
                $"  [corpus] {buildIssues.Count - blocking.Length} expression(s) are metadata-only at "
                + "the publisher; recorded as coverage, not treated as acquisition failures");
        var manifest = new ManifestDoc
        {
            Publisher = new Dictionary<string, string>
            {
                ["id"] = pub.Id,
                ["name"] = pub.Name,
                ["jurisdiction"] = pub.Jurisdiction,
                ["homepage"] = pub.Homepage,
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
            Expressions = expressions,
            ExpressionsWithText = expressionsWithText,
            ExpressionsWithoutText = expressions - expressionsWithText,
            ScopeExpectedWorks = expectedWorks,
            AcquisitionRetryMaximumAttempts = retryMaximumAttempts,
            // The persisted list is the acquisition-failure record, and it is bounded. Coverage the
            // publisher does not offer belongs to the expression that lacks it, not here.
            BuildIssues = blocking.ToList(),
            ValidFromEarliest = earliest,
            ValidToLatest = latest,
            HistoryBegins = desc.HistoryBegins,
            IngesterVersion = "0.1.0",
            // A poll that materializes no different bytes must not relabel the existing corpus
            // as though a newer, unrelated Lex revision had produced it. Write the prior
            // materializer first; if any corpus/NOTICE/manifest fact changes below, replace it
            // with this run's explicitly supplied identity.
            IngesterCodeCommit = existingManifest?.IngesterCodeCommit
                ?? _ingesterCodeCommit,
            SourceConfigurationKind = sourceConfigurationKind,
            SourceConfigurationSha256 = sourceConfigurationSha256,
            MigrationBaselineWorks = existingManifest?.MigrationBaselineWorks,
        };
        candidate.WriteIfChanged(Path.Combine(corpusRoot, "manifest.json"), JsonSerializer.Serialize(manifest, CorpusJson.Options));
        candidate.WriteIfChanged(Path.Combine(corpusRoot, "NOTICE"), Notice(pub, desc.TextIncluded));
        if (candidate.HasChanges && manifest.IngesterCodeCommit != _ingesterCodeCommit)
        {
            manifest.IngesterCodeCommit = _ingesterCodeCommit;
            candidate.WriteIfChanged(Path.Combine(corpusRoot, "manifest.json"),
                JsonSerializer.Serialize(manifest, CorpusJson.Options));
        }
        if (requireComplete && blocking.Length > 0)
        {
            // Only the blocking issues are listed. The metadata-only ones were already summarised
            // above as coverage, and repeating them under "rejected with" would read as though
            // they had contributed to the rejection.
            foreach (var issue in blocking)
                _progress.WriteLine(
                    $"  [corpus-issue] code={issue.Code} work={issue.Work} detail={issue.Detail}");
            _progress.WriteLine(
                $"  [corpus] candidate rejected with {blocking.Length} typed acquisition issue(s); prior corpus retained");
            return;
        }
        Accepted = true;
        if (!candidate.HasChanges)
        {
            _progress.WriteLine(
                "  [corpus] no materialized changes; prior ingester identity retained");
            return;
        }
        candidate.Commit();
        Committed = true;
        _progress.WriteLine($"  [corpus] works={works} versions={versions} expressions={expressions} " +
            $"with_text={expressionsWithText} without_text={manifest.ExpressionsWithoutText} " +
            $"created={Created} updated={Updated} unchanged={Unchanged}");
    }

    private static ManifestDoc? RefuseLegacyAppend(string root)
    {
        var path = Path.Combine(root, "manifest.json");
        if (!File.Exists(path)) return null;
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            File.ReadAllText(path), CorpusJson.Options)
            ?? throw new InvalidDataException("Existing corpus manifest is empty.");
        if (manifest.Schema != ManifestDoc.CurrentSchema)
            throw new InvalidDataException(
                $"Existing corpus schema '{manifest.Schema}' cannot be appended by the v4 writer; "
                + "use the explicit fresh-corpus migration path.");
        CodeIdentity.RequireFullCommit(
            manifest.IngesterCodeCommit, "manifest ingester_code_commit");
        ValidateSourceConfiguration(
            manifest.SourceConfigurationKind, manifest.SourceConfigurationSha256);
        return manifest;
    }

    internal static void ValidateSourceConfiguration(string? kind, string? digest)
    {
        switch (kind)
        {
            case "code_only" when digest is null:
                return;
            case "engineering_scope" when digest is not null:
                CodeIdentity.RequireSha256(digest, "manifest source_configuration_sha256");
                return;
            case "code_only":
                throw new InvalidDataException(
                    "manifest source_configuration_sha256 must be null for code_only sources");
            case "engineering_scope":
                throw new InvalidDataException(
                    "manifest source_configuration_sha256 is required for engineering_scope sources");
            default:
                throw new InvalidDataException(
                    $"manifest source_configuration_kind '{kind}' is unsupported");
        }
    }

    internal static void ValidateVersionRawForSourceConfiguration(
        string? sourceConfigurationKind, IReadOnlyDictionary<string, string> raw, string context)
    {
        if (sourceConfigurationKind != "engineering_scope") return;
        var forbidden = raw.Keys.Where(key =>
                key.Equals("domains", StringComparison.OrdinalIgnoreCase)
                || key.Equals("scope_reasons", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (forbidden.Length > 0)
            throw new InvalidDataException(
                $"source_configuration_kind=engineering_scope forbids acquisition selector "
                + $"key(s) in corpus version raw metadata ({context}): "
                + string.Join(", ", forbidden));
    }

    private static void ValidatePlannedRawMetadata(
        string sourceConfigurationKind, IReadOnlyList<CorpusPlannedWork> plan)
    {
        foreach (var item in plan)
            foreach (var version in item.Versions)
                ValidateVersionRawForSourceConfiguration(
                    sourceConfigurationKind, version.Raw,
                    $"{item.Work.Slug}/{version.Id.Value}");
    }

    private static void ValidateExistingRawMetadata(string root, string sourceConfigurationKind)
    {
        var worksRoot = Path.Combine(root, "works");
        if (!Directory.Exists(worksRoot)) return;
        foreach (var path in Directory.EnumerateFiles(
                     worksRoot, "meta.json", SearchOption.AllDirectories))
        {
            if (!path.Contains(
                    $"{Path.DirectorySeparatorChar}versions{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                continue;
            var version = JsonSerializer.Deserialize<VersionMeta>(
                File.ReadAllText(path), CorpusJson.Options)
                ?? throw new InvalidDataException($"Existing corpus record is empty: {path}");
            ValidateVersionRawForSourceConfiguration(
                sourceConfigurationKind, version.Raw,
                Path.GetRelativePath(root, path).Replace('\\', '/'));
        }
    }

    private static Dictionary<string, string> VersionKeys(
        string workDir, IReadOnlyList<VersionRecord> versions)
    {
        var duplicateId = versions.GroupBy(version => version.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidDataException(
                $"Publisher returned duplicate version identifier {duplicateId.Key} for {Path.GetFileName(workDir)}.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dateGroup in versions.GroupBy(version => version.ValidFrom))
        {
            var date = dateGroup.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sameDate = dateGroup.OrderBy(version => version.Id.Value, StringComparer.Ordinal).ToList();
            var expectedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var version in sameDate)
            {
                var key = VersionIdentity.Create(version.ValidFrom, version.Id.Value);
                if (!expectedKeys.Add(key))
                    throw new InvalidDataException(
                        $"Publisher version identifiers collide after hashing for {Path.GetFileName(workDir)} on {date}.");
                result.Add(version.Id.Value, key);
            }

            var versionsDir = Path.Combine(workDir, "versions");
            if (!Directory.Exists(versionsDir)) continue;
            var incompatible = Directory.EnumerateDirectories(versionsDir)
                .Select(Path.GetFileName)
                .Where(key => key is not null
                    && (key == date || key.StartsWith(date + "--", StringComparison.Ordinal)))
                .FirstOrDefault(key => !expectedKeys.Contains(key!));
            if (incompatible is not null)
                throw new InvalidDataException(
                    $"Legacy or incompatible version key {incompatible} exists for "
                    + $"{Path.GetFileName(workDir)}; a full re-ingest is required.");
        }
        return result;
    }

    private static ExpressionMeta CreateExpressionMeta(ExpressionRecord expression, bool textIncluded) => new()
    {
        Language = expression.Language,
        ValidFrom = expression.ValidFrom?.ToString("yyyy-MM-dd"),
        ValidTo = expression.ValidTo?.ToString("yyyy-MM-dd"),
        ValidTimeSource = expression.ValidTimeSource,
        Title = expression.Title,
        TitleShort = expression.TitleShort,
        SourceUri = expression.SourceUri,
        Text = new TextInfo
        {
            Available = false,
            Reason = textIncluded ? "not-fetched" : "pending-gate",
            Url = expression.SourceUri,
        },
    };

    private static List<PublisherMetadataRecord> CanonicalPublisherMetadata(
        IReadOnlyList<PublisherMetadataRecord>? values) =>
        PublisherMetadataValidation.Canonicalize(values);

    private static void ValidateBodyFetch(SourceBodyFetch result)
    {
        if (result.Attempts is < 1 or > 10)
            throw new InvalidDataException("A source body outcome reported an invalid attempt count.");
        if ((result.Status == SourceBodyStatus.Retrieved) != (result.Text is not null))
            throw new InvalidDataException(
                "A source body outcome must contain text exactly when its status is Retrieved.");
    }

    private static void ValidateManifestationFetch(SourceManifestationFetch result)
    {
        if (result.Attempts is < 1 or > 10)
            throw new InvalidDataException("A source manifestation outcome reported an invalid attempt count.");
        if ((result.Status == SourceBodyStatus.Retrieved) != (result.Value is not null))
            throw new InvalidDataException(
                "A source manifestation outcome must contain a value exactly when its status is Retrieved.");
    }

    private void TombstoneMissingVersions(
        IReadOnlySet<string> seenVersionMetadata, CorpusCandidate candidate)
    {
        var worksRoot = Path.Combine(corpusRoot, "works");
        if (!Directory.Exists(worksRoot)) return;

        foreach (var metaPath in Directory.EnumerateFiles(
                     worksRoot, "meta.json", SearchOption.AllDirectories))
        {
            if (!metaPath.Contains($"{Path.DirectorySeparatorChar}versions{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                continue;
            if (seenVersionMetadata.Contains(Path.GetFullPath(metaPath))) continue;

            var meta = JsonSerializer.Deserialize<VersionMeta>(
                File.ReadAllText(metaPath), CorpusJson.Options)!;
            var lifecycle = meta.Events.LastOrDefault(e =>
                e.Event is "withdrawn_from_source" or "resighted");
            if (lifecycle?.Event == "withdrawn_from_source") continue;

            meta.Events.Add(new EventEntry
            {
                Event = "withdrawn_from_source",
                ObservedFrom = _now,
                Scope = "version",
                Detail = "publisher record absent from the current enumeration",
            });
            meta.RecordSha256 = CorpusHashes.RecordSha256(meta);
            candidate.WriteIfChanged(metaPath, JsonSerializer.Serialize(meta, CorpusJson.Options));
            Updated++;
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string Min(string? a, string b) => a is null || string.CompareOrdinal(b, a) < 0 ? b : a;
    private static string Max(string? a, string b) => a is null || string.CompareOrdinal(b, a) > 0 ? b : a;

    private void ReportProgress(string publisher, long completed, long total,
        TimeSpan elapsed, string? current)
    {
        var percent = total == 0 ? 100d : completed * 100d / total;
        var eta = (total, completed) switch
        {
            (0, _) => "00:00:00",
            (_, 0) => "calculating",
            _ => FormatDuration(TimeSpan.FromSeconds(elapsed.TotalSeconds * (total - completed) / completed)),
        };
        var line = FormattableString.Invariant(
            $"  [progress] {publisher}: ingest expressions={completed}/{total} percent={percent:F1} elapsed={FormatDuration(elapsed)} eta={eta}");
        _progress.WriteLine(line + (current is null ? "" : $" current={current}"));
    }

    private static string FormatDuration(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private static string Notice(Publisher pub, bool textIncluded) => $"""
        NOTICE — three layers (Lex spec §16.2)

        1. UNDERLYING ACTS AND DOCUMENTS
           Official acts of public authority are excluded from copyright protection
           (Luxembourg: loi du 18 avril 2001, art. 10, 8°). Where source metadata is
           reused, it is reused under the publisher's own terms.
           Attribution: {pub.Attribution}
           Source terms: {pub.SourceTermsUrl}
           Modifications: metadata converted from source RDF to JSON; no text altered.
           {(textIncluded
              ? "Bodies are stored verbatim as retrieved from the publisher's dissemination channel."
              : "No text is stored (metadata-only mode, D42).")}
           These obligations survive into forks and derived artefacts.

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
