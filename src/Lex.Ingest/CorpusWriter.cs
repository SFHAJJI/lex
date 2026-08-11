using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Law;

namespace Lex.Ingest;

/// <summary>
/// The single component that writes corpus files (C1 layout, C3 rules, F12 discipline).
/// Adapters never touch disk (F8). Version directories are valid_from-only and are never
/// renamed (D41). meta.json changes only when observed reality changes; every change
/// appends the corresponding chain entry (F12). Body files are append-only; a declared
/// metadata-only expression writes no body and records the reason instead.
/// </summary>
public sealed class CorpusWriter(string corpusRoot, DateTimeOffset now, TextWriter? progress = null)
{
    private readonly string _now = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    private readonly TextWriter _progress = progress ?? Console.Error;
    public int Created { get; private set; }
    public int Updated { get; private set; }
    public int Unchanged { get; private set; }
    public bool Committed { get; private set; }
    public IReadOnlyList<SourceBuildIssue> BuildIssues { get; private set; } = [];

    public async Task WriteAsync(
        ISourceAdapter adapter, CancellationToken ct, bool requireComplete = false)
    {
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
        var plan = new List<(WorkRef Work, IReadOnlyList<VersionRecord> Versions)>();
        var localBuildIssues = new List<SourceBuildIssue>();
        var enumeratedWorks = 0;
        await foreach (var work in adapter.EnumerateWorks(ct))
        {
            enumeratedWorks++;
            var versionsOfWork = await adapter.FetchVersions(work, ct);
            if (versionsOfWork.Count > 0)
                plan.Add((work, versionsOfWork));
            else
                localBuildIssues.Add(new SourceBuildIssue(
                    "no_versions", work.Slug, "The publisher enumeration returned no version records."));
        }
        var sourceInventory = (adapter as ISourceBuildInventory)?.GetBuildInventory();
        var expectedWorks = Math.Max(enumeratedWorks, sourceInventory?.ExpectedWorks ?? 0);
        var retryMaximumAttempts = sourceInventory?.RetryMaximumAttempts ?? 1;
        if (retryMaximumAttempts is < 1 or > 10)
            throw new InvalidDataException("The source retry maximum must be between 1 and 10 attempts.");
        if (sourceInventory?.EnumerationComplete == false || enumeratedWorks < expectedWorks)
            throw new SourceEnumerationIncompleteException(new SourceBuildIssue(
                "incomplete_enumeration", pub.Id,
                $"Publisher enumeration returned {enumeratedWorks} of {expectedWorks} expected works; the prior corpus remains unchanged."));
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
                var metaPath = Path.Combine(versionDir, "meta.json");
                seenVersionMetadata.Add(Path.GetFullPath(metaPath));

                var lexId = $"{pub.Id}:{work.Slug}:{vkey}";
                VersionMeta meta;
                var existing = File.Exists(metaPath);
                var changed = false;
                if (existing)
                {
                    meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(metaPath, ct), CorpusJson.Options)!;
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
                        if (exprMeta.Observations.Count > 0) continue;   // already observed
                        var fetched = await adapter.FetchBody(v, exprRec, ct);
                        ValidateBodyFetch(fetched);
                        retryMaximumAttempts = Math.Max(retryMaximumAttempts, fetched.Attempts);
                        if (fetched.Status != SourceBodyStatus.Retrieved || fetched.Text is null)
                        {
                            bodyFailures[exprRec.Language] = fetched;
                            exprMeta.Text.Reason = fetched.IssueCode;
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
            BuildIssues = buildIssues,
            ValidFromEarliest = earliest,
            ValidToLatest = latest,
            HistoryBegins = desc.HistoryBegins,
            IngesterVersion = "0.1.0",
            PublisherDiscoverySchema = ManifestDoc.CurrentPublisherDiscoverySchema,
        };
        candidate.WriteIfChanged(Path.Combine(corpusRoot, "manifest.json"), JsonSerializer.Serialize(manifest, CorpusJson.Options));
        candidate.WriteIfChanged(Path.Combine(corpusRoot, "NOTICE"), Notice(pub, desc.TextIncluded));
        if (requireComplete && buildIssues.Count > 0)
        {
            foreach (var issue in buildIssues)
                Console.Error.WriteLine(
                    $"  [corpus-issue] code={issue.Code} work={issue.Work} detail={issue.Detail}");
            Console.Error.WriteLine(
                $"  [corpus] candidate rejected with {buildIssues.Count} typed acquisition issue(s); prior corpus retained");
            return;
        }
        candidate.Commit();
        Committed = true;
        Console.Error.WriteLine($"  [corpus] works={works} versions={versions} expressions={expressions} " +
            $"with_text={expressionsWithText} without_text={expressions - expressionsWithText} " +
            $"created={Created} updated={Updated} unchanged={Unchanged}");
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
        IReadOnlyList<PublisherMetadataRecord>? values) => (values ?? [])
        .Distinct()
        .OrderBy(value => value.Kind, StringComparer.Ordinal)
        .ThenBy(value => value.Identifier, StringComparer.Ordinal)
        .ThenBy(value => value.Language, StringComparer.Ordinal)
        .ThenBy(value => value.Label, StringComparer.Ordinal)
        .ThenBy(value => value.SourceUri, StringComparer.Ordinal)
        .ToList();

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
