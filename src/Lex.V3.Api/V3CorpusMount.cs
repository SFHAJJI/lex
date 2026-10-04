using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

internal sealed partial class V3CorpusMount : IDisposable
{
    public const string IndexFileName = "luxembourg-index.sqlite3";
    public const string CapabilityManifestFileName = "luxembourg-capability-manifest.json";
    public const string EuropeIndexFileName = "europe-index.sqlite3";
    public const string EuropeCapabilityManifestFileName = "europe-capability-manifest.json";
    public const string CorpusFileName = "lex-corpus-6.json";
    private const int MaximumCapabilityManifestBytes = 4 * 1024 * 1024;
    private const string EuropeanUnionPublisherDomain = "europa.eu";

    private readonly LuxembourgIndexReader? _reader;
    private readonly EuropeIndexReader? _europeReader;
    private readonly VerifiedLexCorpus6ManifestSet _corpus;

    // The retention line's decision the mounted generations carry, held to the mounted log at open; null when the mount
    // keeps no generation.
    private readonly V3RetentionDecision? _generations;

    // One verified reader per retained generation, newest first: a state only an earlier build held is read from the newest
    // generation that holds it (a state digest fixes its text, so any holder answers alike).
    private readonly IReadOnlyList<GenerationReader> _generationReaders;

    private sealed record GenerationReader(string IndexSha256, long Observation, string BuiltAt, LuxembourgIndexReader Reader);

    private V3CorpusMount(
        LuxembourgIndexReader? reader,
        EuropeIndexReader? europeReader,
        VerifiedLexCorpus6ManifestSet corpus,
        V3RetentionDecision? generations = null,
        IReadOnlyList<GenerationReader>? generationReaders = null)
    {
        if (reader is null && europeReader is null)
            throw new ArgumentException("A V3 corpus mount requires at least one publisher index.");
        _reader = reader;
        _europeReader = europeReader;
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
        _generations = generations;
        _generationReaders = generationReaders ?? [];
    }

    public static async Task<V3CorpusMount?> OpenAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var indexPath = Path.Combine(directory, IndexFileName);
        var capabilityPath = Path.Combine(directory, CapabilityManifestFileName);
        var europeIndexPath = Path.Combine(directory, EuropeIndexFileName);
        var europeCapabilityPath = Path.Combine(directory, EuropeCapabilityManifestFileName);
        var corpusPath = Path.Combine(directory, CorpusFileName);
        var hasIndex = File.Exists(indexPath);
        var hasCapability = File.Exists(capabilityPath);
        var hasEuropeIndex = File.Exists(europeIndexPath);
        var hasEuropeCapability = File.Exists(europeCapabilityPath);
        var hasCorpus = File.Exists(corpusPath);
        if (!hasIndex && !hasCapability && !hasEuropeIndex && !hasEuropeCapability && !hasCorpus)
        {
            return null;
        }

        if (!hasCorpus || hasIndex != hasCapability || hasEuropeIndex != hasEuropeCapability ||
            (!hasIndex && !hasEuropeIndex))
        {
            throw new InvalidDataException(
                "A V3 corpus mount requires the exact corpus and a complete index/capability pair.");
        }

        if (!hasIndex && Directory.Exists(Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName)))
        {
            throw new InvalidDataException("A V3 corpus mount keeps generations only of a Luxembourg event log it holds.");
        }

        var corpusBytes = await File.ReadAllBytesAsync(corpusPath, cancellationToken)
            .ConfigureAwait(false);
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);
        LuxembourgIndexReader? reader = null;
        EuropeIndexReader? europeReader = null;
        V3RetentionDecision? generations = null;
        var generationReaders = new List<GenerationReader>();
        try
        {
            if (hasIndex)
            {
                var capabilityBytes = await ReadCapabilityAsync(
                    capabilityPath, cancellationToken).ConfigureAwait(false);
                reader = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
                        indexPath, capabilityBytes, cancellationToken)
                    .ConfigureAwait(false);
                if (reader.CorpusRef != corpus.ArtifactRef)
                    throw new InvalidDataException(
                        "The mounted Luxembourg index does not bind the mounted corpus/6 artifact.");
                reader.VerifyEventLogSources(corpus);

                // The generations beside the mount, held to its log as the mount writer's verification holds them; any that
                // does not hold fails the mount closed, like every other check here.
                var held = await V3CorpusMountWriter.VerifyGenerationsAsync(directory, reader, cancellationToken).ConfigureAwait(false);
                if (held.Failure is { } failure)
                    throw new InvalidDataException($"The mounted generations do not hold to the mounted log: {failure}.");
                generations = held.Decision;

                // Each retained generation opened now, verified as at its check, so a request never opens one (the image's
                // private /tmp holds every index copy from startup on).
                foreach (var kept in (generations?.Retained ?? []).OrderByDescending(static kept => kept.Observation))
                {
                    var generationPath = Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName, kept.IndexSha256);
                    var generationCapability = await ReadCapabilityAsync(
                        Path.Combine(generationPath, CapabilityManifestFileName), cancellationToken).ConfigureAwait(false);
                    generationReaders.Add(new GenerationReader(
                        kept.IndexSha256, kept.Observation, kept.BuiltAt,
                        await LuxembourgIndexReader.OpenAndVerifyFileAsync(
                            Path.Combine(generationPath, IndexFileName), generationCapability, cancellationToken).ConfigureAwait(false)));
                }
            }

            if (hasEuropeIndex)
            {
                var capabilityBytes = await ReadCapabilityAsync(
                    europeCapabilityPath, cancellationToken).ConfigureAwait(false);
                europeReader = await EuropeIndexReader.OpenAndVerifyFileAsync(
                        europeIndexPath, capabilityBytes, corpus.ArtifactRef, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new V3CorpusMount(reader, europeReader, corpus, generations, generationReaders);
        }
        catch
        {
            reader?.Dispose();
            europeReader?.Dispose();
            foreach (var generation in generationReaders) generation.Reader.Dispose();
            throw;
        }
    }

    public V3PlatformOperationOutcome Resolve(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "resolve", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus resolver only accepts resolve/1.");
        }

        var identifier = request.Parameters.TryGetProperty("identifier", out var value) &&
                         value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Unknown(request, identifier, observedAt);
        }

        if (TryParsePinnedPermalink(
                identifier,
                out var workKey,
                out var applicabilityDate,
                out var requestedDigest))
        {
            var states = _reader?.ResolveState(workKey, applicabilityDate) ?? [];
            if (states.Count == 0)
            {
                return Unknown(request, identifier, observedAt, PublisherId.LuLegilux);
            }

            var matching = states.Where(state => string.Equals(
                requestedDigest, state.StateSha256, StringComparison.Ordinal)).ToArray();
            if (matching.Length == 0 && states.Count > 1)
            {
                using var ambiguous = JsonSerializer.SerializeToDocument(new
                {
                    requested_identifier = identifier,
                    candidates = states.Select(StateUrl).ToArray(),
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
            }

            var current = matching.Length == 1 ? matching[0] : states[0];
            var stableCoordinate = StableCoordinate(current);
            var currentUrl = StateUrl(current);
            if (!string.Equals(requestedDigest, current.StateSha256, StringComparison.Ordinal))
            {
                using var mismatch = JsonSerializer.SerializeToDocument(new
                {
                    requested_digest = requestedDigest,
                    current_digest = current.StateSha256,
                    stable_coordinate = stableCoordinate,
                    current_hash_pinned_url = currentUrl,
                    rule_profile_sha256s = current.RuleProfileSha256s,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
            }

            var pinnedDates = ArticleDates(current);
            using var pinnedResult = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                publisher = "lu-legilux",
                work_key = current.WorkKey,
                applicability_date = current.ApplicabilityDate,
                state_sha256 = current.StateSha256,
                expression_iri = current.ExpressionIri,
                publisher_work_iri = current.PublisherWorkIri,
                publisher_legal_resource_iri = current.PublisherLegalResourceIri,
                language = current.Language,
                article_identities = current.ArticleIdentities,
                articles = pinnedDates.Articles,
                articles_not_admitted = NotAdmittedMap([current])[current.StateSha256],
                articles_not_admitted_note = ArticlesNotAdmittedNote,
                validity_conflict_count = pinnedDates.ConflictCount,
                validity_conflict_rule = ValidityConflictRule,
                stable_coordinate = stableCoordinate,
                permalink = currentUrl,
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader!.IndexRef.Sha256,
            });
            return V3PlatformOperationOutcome.Success(
                Context("success", observedAt),
                new V3PlatformOperationResult(request, "work_resolution", pinnedResult.RootElement));
        }

        var candidates = _reader?.ResolveExact(identifier) ?? [];
        var europeCandidates = _europeReader?.ResolveExact(identifier) ?? [];
        var exactCandidateCount = candidates.Count + europeCandidates.Count;
        if (exactCandidateCount == 0)
        {
            if (LooksLikeIdentifier(identifier))
            {
                return Unknown(request, identifier, observedAt);
            }

            var workResolution = _reader?.ResolveWorkTitle(identifier);
            if (workResolution is null)
            {
                using var unavailable = JsonSerializer.SerializeToDocument(new
                {
                    requested_mode = "r1_work_discovery",
                    available_modes = new[] { "r0_exact_coordinate" },
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherFor(identifier)),
                    new V3PlatformOperationRefusal(
                        request, "retrieval_mode_unavailable", unavailable.RootElement));
            }
            if (!workResolution.Available)
            {
                using var unavailable = JsonSerializer.SerializeToDocument(new
                {
                    requested_mode = "r1_work_discovery",
                    available_modes = new[] { "r0_exact_coordinate" },
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(
                        request, "retrieval_mode_unavailable", unavailable.RootElement));
            }

            if (workResolution.Candidates.Count == 0)
            {
                return Unknown(request, identifier, observedAt);
            }

            if (workResolution.Candidates.Count > 1)
            {
                using var ambiguous = JsonSerializer.SerializeToDocument(new
                {
                    requested_identifier = identifier,
                    candidates = workResolution.Candidates
                        .Select(static candidate => candidate.WorkIdentifier).ToArray(),
                    match_reason = workResolution.Candidates[0].MatchReason,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(
                        request, "ambiguous_identifier", ambiguous.RootElement));
            }

            var work = workResolution.Candidates[0];
            using var workResult = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                publisher = "lu-legilux",
                work_identifier = work.WorkIdentifier,
                expressions = work.ExpressionIris,
                languages = work.Languages,
                matched_title = work.MatchedTitle,
                matched_title_language = work.MatchedTitleLanguage,
                retrieval_lane = "r1_work_discovery",
                match_reason = work.MatchReason,
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader!.IndexRef.Sha256,
            });
            return V3PlatformOperationOutcome.Success(
                Context("success", observedAt),
                new V3PlatformOperationResult(request, "work_resolution", workResult.RootElement));
        }

        // One work can hold equally authentic language expressions. Resolve its work
        // coordinate to those choices; the existing exact-expression request selects one.
        // Multiple expressions in the same language still need a version rule.
        if (candidates.Count == 0 && europeCandidates.Count > 1 &&
            europeCandidates.Select(static candidate => candidate.PublisherWorkId)
                .Distinct(StringComparer.Ordinal).Count() == 1 &&
            europeCandidates.Select(static candidate => candidate.Language)
                .Distinct(StringComparer.Ordinal).Count() == europeCandidates.Count &&
            europeCandidates.Select(static candidate => candidate.PublisherExpressionId)
                .Distinct(StringComparer.Ordinal).Count() == europeCandidates.Count)
        {
            var europeWork = europeCandidates[0].PublisherWorkId;
            var isWorkCoordinate = string.Equals(identifier, europeWork, StringComparison.Ordinal) ||
                _europeReader!.ResolveWorkExpressions(europeWork).Any(expression =>
                    string.Equals(identifier, expression.PublisherWorkCelex, StringComparison.Ordinal));
            if (isWorkCoordinate)
            {
                var choices = europeCandidates.OrderBy(static candidate => candidate.Language,
                    StringComparer.Ordinal).ThenBy(static candidate => candidate.PublisherExpressionId,
                    StringComparer.Ordinal).ToArray();
                using var workChoices = JsonSerializer.SerializeToDocument(new
                {
                    requested_identifier = identifier,
                    publisher = "eu-eurlex",
                    publisher_work_id = europeWork,
                    resolution_scope = "work",
                    expression_selection_required = true,
                    language_selection = "Select a held expression by its identifier to resolve that language.",
                    available_languages = choices.Select(static candidate => candidate.Language).ToArray(),
                    expressions = choices.Select(static candidate => new
                    {
                        expression_iri = candidate.PublisherExpressionId,
                        language = candidate.Language,
                        resolve = new { identifier = candidate.PublisherExpressionId },
                    }).ToArray(),
                    corpus_sha256 = _corpus.ArtifactRef.Sha256,
                    index_sha256 = _europeReader!.IndexRef.Sha256,
                });
                return V3PlatformOperationOutcome.Success(
                    Context("success", observedAt, PublisherId.EuEurLex),
                    new V3PlatformOperationResult(request, "work_resolution", workChoices.RootElement));
            }
        }

        if (exactCandidateCount > 1)
        {
            var ambiguityPublisher = candidates.Count == 0
                ? PublisherId.EuEurLex
                : europeCandidates.Count == 0
                    ? PublisherId.LuLegilux
                    : PublisherFor(identifier);
            using var helpful = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                candidates = candidates.Select(static candidate => candidate.ExpressionIri)
                    .Concat(europeCandidates.Select(static candidate => candidate.PublisherExpressionId))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, ambiguityPublisher),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", helpful.RootElement));
        }

        if (europeCandidates.Count == 1)
        {
            var resolvedEurope = europeCandidates[0];
            using var europeResult = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                publisher = "eu-eurlex",
                publisher_work_id = resolvedEurope.PublisherWorkId,
                expression_iri = resolvedEurope.PublisherExpressionId,
                language = resolvedEurope.Language,
                provision_identifiers = resolvedEurope.PublisherProvisionIdentifiers,
                article_identities = resolvedEurope.ArticleIdentities,
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _europeReader!.IndexRef.Sha256,
            });
            return V3PlatformOperationOutcome.Success(
                Context("success", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationResult(request, "work_resolution", europeResult.RootElement));
        }

        var resolved = candidates[0];
        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            publisher = "lu-legilux",
            expression_iri = resolved.ExpressionIri,
            publisher_wid = resolved.PublisherWid,
            language = resolved.Language,
            article_identities = resolved.ArticleIdentities,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "work_resolution", result.RootElement));
    }

    internal const string AsObservedBasis =
        "the event log as it stood at the named snapshot: every state of the work its events had sighted up to that build's last event, " +
        "a later event of a state replacing an earlier one and a state once held staying held (absence is not a withdrawal); the state " +
        "applying on the date is selected among them as as_of selects among the mounted states, and the next date is the next one held at " +
        "that snapshot; the work is named as the mounted index names it, or by its stable work coordinate when only the log still holds it";

    internal const string AsObservedBoundNote =
        "observed_no_later_than is when the snapshot's build ran, rounded up to the second, so every state it held was observed no later " +
        "than that; no observation time is held, so nothing here says when a state was first observed, and no time between two snapshots " +
        "is placed in either";

    internal const string AsObservedTextNote =
        "a state the mounted index still holds is served in full (text_held true); a state only an earlier build held is served in full from " +
        "the newest retained generation that holds it (text_held true, text_from naming that generation); a state no held build holds is " +
        "named by its permalink, digest and source bodies from the log, without text (text_held false)";

    internal const string AsObservedSnapshotWhatWouldAnswer =
        "a snapshot of the mounted log by its index digest: the mounted index (events: log.log_id) or an ancestor it carries forward " +
        "(events: log.ancestors[].log_id)";

    internal const string AsObservedAtWhatWouldAnswer =
        "a snapshot by its index digest instead of a time: no observation time is held and a build's time bounds observation only from " +
        "above, so no time can be placed in a snapshot without guessing; the mounted index (events: log.log_id) or an ancestor it carries " +
        "forward (events: log.ancestors[].log_id)";

    internal const string AsObservedLegacyWhatWouldAnswer =
        "a mount whose Luxembourg index records its builds (lex-v3-luxembourg-index/8); this one is lex-v3-luxembourg-index/6, built " +
        "before builds were recorded, so it names no snapshot and no build time";

    internal const string AsObservedNotHeldWorkWhatWouldAnswer =
        "a work the log held at this snapshot; this one was first sighted by a later build (events: first_sighting)";

    /// <summary>
    /// <c>as_observed</c> for Luxembourg, by build snapshot (the panel's ruling on the owner's behalf): the state of one
    /// work that applied on a date as the event log held it at one build of the mounted chain, named by that build's
    /// index digest. The log is folded up to the snapshot's last event and the state is selected as <c>as_of</c>
    /// selects; the answer names the snapshot and gives its build time as an upper bound (<c>observed_no_later_than</c>),
    /// never an observation time. A state the mounted index still holds is served in full; one only an earlier build
    /// held is named by its identity from the log, without text. A request by time (<c>at</c>) refuses
    /// <c>snapshot_unknown</c>, as does a digest that is no snapshot of this log: upper bounds alone place no instant.
    /// </summary>
    public V3PlatformOperationOutcome AsObserved(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "as_observed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus observed-state operation only accepts as_observed/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedDate = RequiredString(request.Parameters, "date");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (!DateOnly.TryParseExact(
                requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        if (request.Parameters.TryGetProperty("snapshot", out _) == request.Parameters.TryGetProperty("at", out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "as_observed takes exactly one of 'snapshot' (an index digest) and 'at' (a time).");
        }

        if (request.Parameters.TryGetProperty("at", out _))
        {
            var requestedAt = RequiredString(request.Parameters, "at");
            // An EU identifier is refused the mode with EU context, as its snapshot form is: the event log records Luxembourg's
            // builds only, and attribution follows the identifier (review of #913: it was answered snapshot_unknown with
            // Luxembourg context).
            return IsEuropeanUnionShaped(identifier)
                ? ModeUnavailable(request, observedAt, PublisherId.EuEurLex, "r6_as_observed")
                : SnapshotUnknown(request, observedAt, requestedAt, AsObservedAtWhatWouldAnswer);
        }

        var snapshot = RequiredString(request.Parameters, "snapshot");
        if (snapshot.Length != 64 || !snapshot.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'snapshot' is not an index digest.");
        }

        var workKey = "";
        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_as_observed", requestedLanguage: null,
                out var mountedStates, out _) is { } refused)
        {
            // A work the mounted build no longer holds at all is still named by the log, since absence is not a withdrawal:
            // by its stable work coordinate it is answered from the log at the snapshot (review of #889). Any other
            // identifier, or a work the log never held, keeps the refusal.
            var logs = _reader?.RecordsBuilds == true ? _reader.ResolveObservations() : [];
            if (refused.Refusal?.Code != "identifier_unknown" || logs.Count == 0 ||
                !TryParseStableWorkCoordinate(identifier, out var loggedWork) ||
                _reader!.ResolveObservedStates(loggedWork, logs[^1].LastSeq).Count == 0)
            {
                return refused;
            }

            workKey = loggedWork;
        }
        else
        {
            workKey = mountedStates[0].WorkKey;
        }

        if (!_reader!.RecordsBuilds)
        {
            return SnapshotUnknown(request, observedAt, snapshot, AsObservedLegacyWhatWouldAnswer);
        }

        // Snapshot k is the build of observation k: the index its successor names as predecessor, or the mounted one.
        var observations = _reader.ResolveObservations();
        var generation = Enumerable.Range(0, observations.Count).FirstOrDefault(
            k => string.Equals(
                k + 1 < observations.Count ? observations[k + 1].PredecessorIndexSha256 : _reader.IndexRef.Sha256,
                snapshot,
                StringComparison.Ordinal),
            -1);
        if (generation < 0)
        {
            return SnapshotUnknown(request, observedAt, snapshot, AsObservedSnapshotWhatWouldAnswer);
        }

        var at = observations[generation];
        var held = _reader.ResolveObservedStates(workKey, at.LastSeq);
        if (held.Count == 0)
        {
            return Unknown(request, identifier, observedAt, PublisherId.LuLegilux, AsObservedNotHeldWorkWhatWouldAnswer);
        }

        var heldLanguages = held.Select(static state => state.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (requestedLanguage is not null && !heldLanguages.Contains(requestedLanguage, StringComparer.Ordinal))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage,
                available_languages = heldLanguages,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        var scope = requestedLanguage is null
            ? held
            : held.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        var mounted = mountedStates.ToLookup(static state => state.StateSha256, StringComparer.Ordinal);
        var served = new List<JsonNode>();
        var ambiguous = new List<string>();
        foreach (var language in requestedLanguage is null ? heldLanguages : [requestedLanguage])
        {
            var ofLanguage = scope.Where(state => string.Equals(state.Language, language, StringComparison.Ordinal)).ToArray();
            var atOrBefore = ofLanguage.Where(state => string.CompareOrdinal(state.ApplicabilityDate, requestedDate) <= 0).ToArray();
            if (atOrBefore.Length == 0)
            {
                continue;
            }

            var selectedDate = atOrBefore.Max(static state => state.ApplicabilityDate)!;
            var selected = atOrBefore.Where(state => string.Equals(state.ApplicabilityDate, selectedDate, StringComparison.Ordinal)).ToArray();
            if (selected.Length > 1)
            {
                ambiguous.AddRange(selected.Select(ObservedStateUrl));
                continue;
            }

            var nextDate = ofLanguage
                .Select(static state => state.ApplicabilityDate)
                .Where(date => string.CompareOrdinal(date, requestedDate) > 0)
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
            var state = selected[0];
            if (mounted[state.StateSha256].FirstOrDefault(candidate => string.Equals(candidate.Language, state.Language, StringComparison.Ordinal)) is { } full)
            {
                var row = JsonSerializer.SerializeToNode(StateRow(full, nextDate))!.AsObject();
                row["text_held"] = true;
                row["text_from"] = null;
                served.Add(row);
            }
            else if (FromGeneration(state.WorkKey, state.ApplicabilityDate, state.Language, state.StateSha256) is var (holder, kept))
            {
                var dates = holder.Reader.ResolveArticleDates(kept.ArticleIdentities.Distinct(StringComparer.Ordinal).ToArray())
                    .ToDictionary(static date => date.ArticleIdentitySha256, static date => date.ApplicabilityDate, StringComparer.Ordinal);
                var row = JsonSerializer.SerializeToNode(
                    StateRow(kept, nextDate, dates, holder.Reader.ResolveArticlesNotAdmitted([kept.StateSha256])))!.AsObject();
                row["text_held"] = true;
                row["text_from"] = new JsonObject
                {
                    ["snapshot_id"] = holder.IndexSha256,
                    ["observation"] = holder.Observation,
                    ["built_at"] = holder.BuiltAt,
                };
                served.Add(row);
            }
            else
            {
                served.Add(new JsonObject
                {
                    ["language"] = state.Language,
                    ["applicability_date"] = state.ApplicabilityDate,
                    ["next_applicability_date"] = nextDate,
                    ["state_sha256"] = state.StateSha256,
                    ["expression_iri"] = state.ExpressionIri,
                    ["source_body_sha256"] = new JsonArray(state.SourceBodySha256.Select(static body => (JsonNode?)JsonValue.Create(body)).ToArray()),
                    ["stable_coordinate"] = $"/lu-legilux/{state.WorkKey}/{state.ApplicabilityDate}",
                    ["permalink"] = ObservedStateUrl(state),
                    ["text_held"] = false,
                    ["text_from"] = null,
                });
            }
        }

        if (ambiguous.Count != 0)
        {
            return RefuseAmbiguousVersion(request, observedAt, requestedDate, ambiguous.Order(StringComparer.Ordinal).ToArray(), bound: null);
        }

        if (served.Count == 0)
        {
            return RefuseNoVersionForDate(request, observedAt, scope.Select(static state => state.ApplicabilityDate).ToArray(), requestedDate, bound: null);
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            requested_snapshot = snapshot,
            publisher = "lu-legilux",
            work_key = workKey,
            snapshot = new
            {
                snapshot_id = snapshot,
                observation = at.Observation,
                observations_in_log = observations.Count,
                mounted = generation == observations.Count - 1,
                corpus_sha256 = at.CorpusSha256,
                observed_no_later_than = at.BuiltAt,
                observation_time_held = false,
                bound_note = AsObservedBoundNote,
            },
            basis = AsObservedBasis,
            text_note = AsObservedTextNote,
            states = served,
            articles_not_admitted_note = ArticlesNotAdmittedNote,
            available_languages = heldLanguages,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "version_state", result.RootElement));
    }

    private static string ObservedStateUrl(LuxembourgIndexObservedState state) =>
        $"/lu-legilux/{state.WorkKey}/{state.ApplicabilityDate}--{state.StateSha256}";

    /// <summary>The one <c>snapshot_unknown</c> refusal of <c>as_observed</c>: the snapshot or time asked for, and what would answer.</summary>
    private V3PlatformOperationOutcome SnapshotUnknown(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        string snapshotId,
        string whatWouldAnswer)
    {
        using var unknown = JsonSerializer.SerializeToDocument(new
        {
            snapshot_id = snapshotId,
            what_would_answer = whatWouldAnswer,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, "snapshot_unknown", unknown.RootElement));
    }

    /// <summary>
    /// R6 <c>as_of</c> for Luxembourg: the publisher-dated state of one work that applies on the
    /// requested date, per language. Pure selection over the index's <c>states</c> rows: the greatest
    /// <c>applicability_date</c> at or before the date selects the state; the next publisher-dated
    /// state, if any, bounds it. Nothing is derived beyond that: no end date the publisher did not
    /// state, no "in force" claim, and Luxembourg timeline semantics remain publisher applicability.
    /// </summary>
    public V3PlatformOperationOutcome AsOf(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "as_of", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus temporal operation only accepts as_of/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedDate = RequiredString(request.Parameters, "date");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (!DateOnly.TryParseExact(
                requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            // The document admits the spelling; the calendar refuses the value. Same rejection class.
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        // An EU act whose census the EU index holds is answered by the EU time view (V3CorpusMount.EuropeTime.cs); an EU
        // identifier it does not hold is unknown there (review of #913).
        if (LocateEuropeSeedForTime(request, identifier, observedAt, out var europeSeed) is { } refusedEurope)
        {
            return refusedEurope;
        }

        if (europeSeed is not null)
        {
            return AsOfEurope(request, identifier, europeSeed, requestedDate, requestedLanguage, observedAt);
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_as_of", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
        var servedLanguages = requestedLanguage is null ? availableLanguages : new[] { requestedLanguage };

        // Every selection is per language: a date on which another language's text changes is never
        // reported as a change of the text being served.
        var served = new List<object>();
        var ambiguous = new List<string>();
        foreach (var language in servedLanguages)
        {
            var ofLanguage = scope
                .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                .ToArray();
            var (selected, nextDate) = SelectAtDate(ofLanguage, requestedDate);
            if (selected.Length == 0)
            {
                continue;
            }

            if (selected.Length > 1)
            {
                ambiguous.AddRange(selected.Select(StateUrl));
                continue;
            }

            served.Add(StateRow(selected[0], nextDate));
        }

        if (ambiguous.Count != 0)
        {
            return RefuseAmbiguousVersion(request, observedAt, requestedDate, ambiguous.Order(StringComparer.Ordinal).ToArray(), bound: null);
        }

        if (served.Count == 0)
        {
            return RefuseNoVersionForDate(request, observedAt, scope, requestedDate, bound: null);
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            states = served,
            articles_not_admitted_note = ArticlesNotAdmittedNote,
            available_languages = availableLanguages,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "version_state", result.RootElement));
    }

    /// <summary>
    /// What <c>articles_not_admitted</c> says, once, for every answer that carries it. Numbers stay outside it.
    /// It says the mechanism as the reviewed profile's behaviour and gives the range the reviewer measured on one
    /// document (an article the publisher struck out, and an article whose text is complete but which carries a
    /// mark the profile does not accept); it does not say which articles, and it does not say how often either
    /// happens.
    /// </summary>
    internal const string ArticlesNotAdmittedNote =
        "articles_not_admitted counts the articles the corpus recorded for the publisher's document for this state that this state does not hold; " +
        "the number of articles in this state plus articles_not_admitted is the number of articles the corpus recorded for that document, which provenance counts by disposition token; " +
        "which articles they are is not held. " +
        "An article is not admitted whole when the reviewed profile cannot represent every element in it: " +
        "that can be an article the publisher struck out, and it can equally be an article whose text is complete " +
        "but which carries a mark the profile does not accept, such as an empty placeholder where a list item was removed";

    internal const string ProvenanceScope =
        "the chain from the publisher's identifiers to the digests this mount verified; it holds no first-sighting event and no signature, so none is claimed";

    internal const string ProvenanceDerivation =
        "state_sha256 is a SHA-256 over these values, each as UTF-8 preceded by its length as four bytes big-endian: the domain tag " +
        "lex-v3-luxembourg-expression-state/1, the publisher, the work key, the applicability date, the expression, the publisher work IRI, " +
        "the publisher legal-resource IRI, the language, each rule-profile digest in sorted order and each article identity in sorted " +
        "order; the article identities are not carried here (they are article_identities in as_of's answer to the same request, and " +
        "article_identities_sha256 is the SHA-256 of them in sorted order, each preceded by its length in the same way, so a caller can check " +
        "the list it holds); this mount's own reader recomputes the state digest when the index is opened and refuses an index in which it " +
        "does not match its row";

    internal const string ProvenanceSourcesNote =
        "object_ref_sha256 identifies the source object in the corpus; body_sha256 is the digest of the publisher bytes the corpus retained " +
        "for it, body_byte_length their length and body_receipt_sha256 the digest of the corpus receipt for that body, each null where the " +
        "corpus holds none; outcome, rights_disposition and gaps are the corpus manifest's own tokens for the member, given verbatim, and this " +
        "answer does not define them; article_outcomes counts the corpus's legal-content outcomes for this document by disposition token, " +
        "verbatim: the articles this state holds are the document's akn_admitted and akn_marker_only_evidence outcomes, an outcome under " +
        "any other token is not held here, and which article an outcome belongs to is not held";

    internal static readonly string[][] ProvenanceNotHeld =
    [
        ["first_sighting_event", "no observation time is held, so nothing here says when the publisher's bytes were first seen; the event log's first_sighting (events) says only that a state is first present in that log"],
        ["signature_stamp", "no signature or stamp is held or made: this states which digests this mount verified and signs nothing"],
        ["publisher_revision_history", "no record of corrections or withdrawals of the publisher's document is held"],
    ];

    /// <summary>
    /// <c>provenance</c>: how a served state is tied to the digests this mount verified. It takes
    /// <c>as_of</c>'s request and selects states by <c>as_of</c>'s rule and refuses as <c>as_of</c>
    /// refuses (shared builders), so the caller can move from an <c>as_of</c> answer to its provenance
    /// with the same request. For each state it gives the hash-pinned permalink and coordinates, the
    /// rule-profile digests, the number of articles, and the source documents the articles come from
    /// with what the corpus recorded for each; beside them, the digests that verified the mount, the rule
    /// that derives the state digest, and a fixed list of what is not held. Nothing is signed.
    /// </summary>
    public V3PlatformOperationOutcome Provenance(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "provenance", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus provenance operation only accepts provenance/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedDate = RequiredString(request.Parameters, "date");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (!DateOnly.TryParseExact(
                requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_provenance", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
        var servedLanguages = requestedLanguage is null ? availableLanguages : new[] { requestedLanguage };

        var served = new List<object>();
        var ambiguous = new List<string>();
        foreach (var language in servedLanguages)
        {
            var ofLanguage = scope
                .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                .ToArray();
            var (selected, _) = SelectAtDate(ofLanguage, requestedDate);
            if (selected.Length == 0)
            {
                continue;
            }

            if (selected.Length > 1)
            {
                ambiguous.AddRange(selected.Select(StateUrl));
                continue;
            }

            var state = selected[0];
            served.Add(new
            {
                language = state.Language,
                applicability_date = state.ApplicabilityDate,
                state_sha256 = state.StateSha256,
                permalink = StateUrl(state),
                stable_coordinate = StableCoordinate(state),
                expression_iri = state.ExpressionIri,
                publisher_work_iri = state.PublisherWorkIri,
                publisher_legal_resource_iri = state.PublisherLegalResourceIri,
                rule_profile_sha256s = state.RuleProfileSha256s,
                articles = state.ArticleIdentities.Count,
                article_identities_sha256 = ArticleIdentitiesSha256(state.ArticleIdentities),
                sources = _reader!.ResolveStateSources(state.StateSha256).Select(SourceRow).ToArray(),
            });
        }

        if (ambiguous.Count != 0)
        {
            return RefuseAmbiguousVersion(request, observedAt, requestedDate, ambiguous.Order(StringComparer.Ordinal).ToArray(), bound: null);
        }

        if (served.Count == 0)
        {
            return RefuseNoVersionForDate(request, observedAt, scope, requestedDate, bound: null);
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = ProvenanceScope,
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            states = served,
            available_languages = availableLanguages,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader!.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            derivation = ProvenanceDerivation,
            sources_note = ProvenanceSourcesNote,
            not_held = ProvenanceNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "provenance_chain", result.RootElement));
    }

    /// <summary>The one rights disposition under which a member's text enters a bundle: both channels agreed CC BY in the same run.</summary>
    private static readonly string EvidenceBundleAdmittingRightsDisposition =
        ContractWire.NameOf(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy);

    private static readonly string AcquiredOutcomeToken = ContractWire.NameOf(LexCorpus6OutcomeKind.Acquired);

    internal const string EvidenceBundleScope =
        "the evidence a reader needs to quote a Luxembourg state: for the state selected as as_of selects it, the hash-pinned permalink and stable " +
        "coordinate, the state digest and its rule profiles, the source documents with their retained body digests (as provenance names them), and every " +
        "article this state holds with its identity, its wording as the publisher wrote it (text), the digest of that text, the digest of its wording " +
        "structure, its notes, its official source, the digest of the publisher body it was read from and an article permalink (the state permalink " +
        "and the article id after #, which verify accepts); the text is the concatenation of the article's text and reference tokens in publisher order, " +
        "the same bytes the index searches; an article whose tokens carry no text (the index admits a marker-only article as evidence) is named under " +
        "articles_without_text and is not served as a quote";

    internal const string EvidenceBundleRightsRule =
        "rights are enforced when the bundle is composed, before any text is read: every corpus member the state's articles come from must have " +
        "been acquired with its two rights channels agreeing CC BY in the same run (rights_disposition agreed_same_run_cc_by); under any other outcome or " +
        "rights disposition the bundle refuses text_withheld and names the official identity, the official link and the retained body digest, so a " +
        "derived, unofficial or non-redistributable body never enters a bundle as authoritative text; a state none of whose articles holds text refuses " +
        "text_not_available";

    internal static readonly string[][] EvidenceBundleNotHeld =
    [
        ["publisher_signature", "no signature or attestation of the publisher is held; the digests are this index's own reading of the retained bytes under the named rule profiles"],
        ["observation_time", "when the publisher served the retained bytes is not held, so no observation time is stated"],
        ["export_formats", "PDF, JSON and CSV exports are the export composer's, not this answer's; this answer is the evidence they compose from"],
        ["notes_in_wording_digest", "wording_sha256 covers the article's text and reference tokens only; the notes travel beside it and are covered by text_sha256 only through the text they were attached to"],
    ];

    /// <summary>
    /// <c>evidence_bundle</c>: what a quote needs, composed under the rights rule. It takes
    /// <c>as_of</c>'s request, selects states by <c>as_of</c>'s rule and refuses as <c>as_of</c>
    /// refuses; it then enforces rights per selected state before reading any text, and answers the
    /// articles' text with the digests, sources, permalinks and official identities the launch
    /// contract requires of a quote.
    /// </summary>
    public V3PlatformOperationOutcome EvidenceBundle(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "evidence_bundle", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus evidence_bundle operation only accepts evidence_bundle/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedDate = RequiredString(request.Parameters, "date");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (!DateOnly.TryParseExact(
                requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        // An EU work the EU index holds is answered from it (the owner's proxy, 2026-10-01 14:30 UTC: serve EU text now,
        // from the original wording the EU index holds); a Luxembourg work takes the path below, unchanged. With no EU index
        // mounted, an EU identifier keeps the refusal it had (retrieval_mode_unavailable), which the refusal census pins.
        if (_europeReader is not null)
        {
            // An EU act whose census the EU index holds is quoted at the requested date by the EU time view; an index with
            // no states table keeps the original-wording path below.
            if (LocateEuropeSeed(request, identifier, observedAt, out var europeSeed) is { } refusedSeed)
            {
                return refusedSeed;
            }

            if (europeSeed is not null)
            {
                return EvidenceBundleEuropeAtDate(request, identifier, europeSeed, requestedDate, requestedLanguage, observedAt);
            }

            if (LocateEuropeWork(request, identifier, observedAt, out var europe) is { } refusedEurope)
            {
                return refusedEurope;
            }

            if (europe.Count > 0)
            {
                return EvidenceBundleEurope(request, identifier, europe, requestedDate, requestedLanguage, observedAt);
            }
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r7_evidence_bundle", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        var servedLanguages = requestedLanguage is null ? availableLanguages : new[] { requestedLanguage };
        var selected = new List<(LuxembourgIndexResolvedState State, string? NextDate)>();
        var ambiguous = new List<string>();
        foreach (var language in servedLanguages)
        {
            var ofLanguage = scope
                .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                .ToArray();
            var (candidates, nextDate) = SelectAtDate(ofLanguage, requestedDate);
            if (candidates.Length == 0)
            {
                continue;
            }

            if (candidates.Length > 1)
            {
                ambiguous.AddRange(candidates.Select(StateUrl));
                continue;
            }

            selected.Add((candidates[0], nextDate));
        }

        if (ambiguous.Count != 0)
        {
            return RefuseAmbiguousVersion(request, observedAt, requestedDate, ambiguous.Order(StringComparer.Ordinal).ToArray(), bound: null);
        }

        if (selected.Count == 0)
        {
            return RefuseNoVersionForDate(request, observedAt, scope, requestedDate, bound: null);
        }

        // ---- Rights at compose time, per selected state, before any text is read. ----
        var sourcesByState = new Dictionary<string, IReadOnlyList<LuxembourgIndexStateSource>>(StringComparer.Ordinal);
        foreach (var (state, _) in selected)
        {
            var sources = _reader!.ResolveStateSources(state.StateSha256);
            sourcesByState[state.StateSha256] = sources;
            var blocking = sources.FirstOrDefault(source =>
                !string.Equals(source.Outcome, AcquiredOutcomeToken, StringComparison.Ordinal)
                || !string.Equals(source.RightsDisposition, EvidenceBundleAdmittingRightsDisposition, StringComparison.Ordinal));
            if (sources.Count == 0 || blocking is not null)
            {
                var member = blocking is null ? null : MemberOf(blocking.ObjectRefSha256);
                using var withheld = JsonSerializer.SerializeToDocument(new
                {
                    official_identity = state.PublisherLegalResourceIri,
                    official_link = state.PublisherWorkIri,
                    content_sha256 = member?.BodySha256 ?? state.StateSha256,
                    stable_coordinate = StableCoordinate(state),
                    permalink = StateUrl(state),
                    language = state.Language,
                    source_outcome = blocking?.Outcome,
                    rights_disposition = blocking?.RightsDisposition,
                    rule = EvidenceBundleRightsRule,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "text_withheld", withheld.RootElement));
            }
        }

        var datesByIdentity = ArticleDateMap(selected.Select(static entry => entry.State).ToArray());
        var notAdmitted = NotAdmittedMap(selected.Select(static entry => entry.State).ToArray());
        var bundles = new List<object>(selected.Count);
        foreach (var (state, nextDate) in selected)
        {
            // In the order the state lists its articles (the publisher's document order), not the reader's sort.
            var rank = state.ArticleIdentities
                .Select(static (identity, index) => (identity, index))
                .ToDictionary(static pair => pair.identity, static pair => pair.index, StringComparer.Ordinal);
            var articles = _reader!.ResolveStateArticles(state.StateSha256)
                .OrderBy(article => rank.GetValueOrDefault(article.ArticleIdentitySha256, int.MaxValue))
                .ThenBy(static article => article.ArticleIdentitySha256, StringComparer.Ordinal)
                .ToArray();
            var quoted = articles.Where(static article => article.Text.Length > 0).ToArray();
            var withoutText = articles.Where(static article => article.Text.Length == 0).ToArray();
            if (quoted.Length == 0)
            {
                var member = sourcesByState[state.StateSha256].Select(source => MemberOf(source.ObjectRefSha256)).FirstOrDefault(static value => value is not null);
                using var unavailable = JsonSerializer.SerializeToDocument(new
                {
                    official_identity = state.PublisherLegalResourceIri,
                    official_source = state.PublisherWorkIri,
                    retained_transport_evidence = member?.BodyReceiptSha256 ?? "none",
                    stable_coordinate = StableCoordinate(state),
                    permalink = StateUrl(state),
                    language = state.Language,
                    articles_held = articles.Length,
                    // An absence of text in this index is not an absence of law: the state is held and named above.
                    what_would_answer = new[] { "new_official_observation" },
                    asserts_absence_of_law = false,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "text_not_available", unavailable.RootElement));
            }

            var conflicts = 0;
            var rows = new List<object>(quoted.Length);
            foreach (var article in quoted)
            {
                var date = datesByIdentity.GetValueOrDefault(article.ArticleIdentitySha256);
                var conflict = ValidityConflict(date, state.ApplicabilityDate);
                if (conflict)
                {
                    conflicts++;
                }

                var textBytes = Encoding.UTF8.GetBytes(article.Text);
                rows.Add(new
                {
                    article_identity_sha256 = article.ArticleIdentitySha256,
                    publisher_id = article.PublisherId,
                    publisher_wid = article.PublisherWid,
                    language = state.Language,
                    article_valid_from = date,
                    validity_conflict = conflict,
                    wording_sha256 = article.WordingSha256,
                    body_sha256 = MemberOf(article.ObjectRefSha256)?.BodySha256,
                    text = article.Text,
                    text_sha256 = Convert.ToHexStringLower(SHA256.HashData(textBytes)),
                    text_byte_length = textBytes.Length,
                    notes = ArticleNotes(article.TokensJson),
                    official_source = state.PublisherLegalResourceIri,
                    article_permalink = StateUrl(state) + "#" + article.PublisherId,
                });
            }

            bundles.Add(new
            {
                language = state.Language,
                applicability_date = state.ApplicabilityDate,
                next_applicability_date = nextDate,
                state_sha256 = state.StateSha256,
                stable_coordinate = StableCoordinate(state),
                permalink = StateUrl(state),
                expression_iri = state.ExpressionIri,
                publisher_work_iri = state.PublisherWorkIri,
                publisher_legal_resource_iri = state.PublisherLegalResourceIri,
                rule_profile_sha256s = state.RuleProfileSha256s,
                article_identities_sha256 = ArticleIdentitiesSha256(state.ArticleIdentities),
                sources = sourcesByState[state.StateSha256].Select(SourceRow).ToArray(),
                body_sha256s = sourcesByState[state.StateSha256]
                    .Select(source => MemberOf(source.ObjectRefSha256)?.BodySha256)
                    .Where(static value => value is not null)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                articles = rows,
                articles_without_text = withoutText.Select(static article => new
                {
                    article_identity_sha256 = article.ArticleIdentitySha256,
                    publisher_id = article.PublisherId,
                    reason = "no_text_tokens",
                }).ToArray(),
                articles_not_admitted = notAdmitted.GetValueOrDefault(state.StateSha256),
                validity_conflict_count = conflicts,
                validity_conflict_rule = ValidityConflictRule,
            });
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = EvidenceBundleScope,
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            rights_rule = EvidenceBundleRightsRule,
            rights_disposition = EvidenceBundleAdmittingRightsDisposition,
            states = bundles,
            articles_not_admitted_note = ArticlesNotAdmittedNote,
            available_languages = availableLanguages,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader!.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = EvidenceBundleNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "evidence_bundle", result.RootElement));
    }

    private LexCorpus6Member? MemberOf(string objectRefSha256) =>
        _corpus.Set.Members.FirstOrDefault(candidate =>
            candidate.Publisher == PublisherId.LuLegilux &&
            string.Equals(candidate.ObjectRefSha256, objectRefSha256, StringComparison.Ordinal));

    private LexCorpus6Member? EuropeMemberOf(string objectRefSha256) =>
        _corpus.Set.Members.FirstOrDefault(candidate =>
            candidate.Publisher == PublisherId.EuEurLex &&
            string.Equals(candidate.ObjectRefSha256, objectRefSha256, StringComparison.Ordinal));

    /// <summary>The article's notes, each the publisher's marker and the text of its body's text and reference tokens, in order.</summary>
    private static object[] ArticleNotes(string tokensJson)
    {
        using var document = JsonDocument.Parse(tokensJson);
        var notes = new List<object>();
        foreach (var token in document.RootElement.EnumerateArray())
        {
            if (!string.Equals(token.GetProperty("kind").GetString(), "note_reference", StringComparison.Ordinal))
            {
                continue;
            }

            var body = new StringBuilder();
            if (token.TryGetProperty("note_body", out var noteBody) && noteBody.ValueKind == JsonValueKind.Array)
            {
                foreach (var nested in noteBody.EnumerateArray())
                {
                    var kind = nested.GetProperty("kind").GetString();
                    if ((kind is "text" or "reference") && nested.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        body.Append(text.GetString());
                    }
                }
            }

            notes.Add(new
            {
                marker = token.TryGetProperty("marker", out var marker) && marker.ValueKind == JsonValueKind.String ? marker.GetString() : null,
                text = body.ToString(),
            });
        }

        return notes.ToArray();
    }

    internal const string ClassificationScope =
        "the typed facts the publisher asserted about this work's resources, read verbatim from the index's fact table (the Stage 3 assertion list): the document " +
        "type (jolux:typeDocument) and the RDF types, the legal value, the responsible body, the historical identifiers, the publication and document dates, each with " +
        "its subject, its object as an IRI or a literal with the datatype and language tag the publisher gave, and the digest of the observation it was read from; grouped " +
        "by predicate, in the index's order, over the work's own IRIs and its expressions; nothing is inferred from them, no thesaurus or subject term is held, and in-force " +
        "status is not served here";

    internal static readonly string[][] ClassificationNotHeld =
    [
        ["subject_classification", "no thesaurus, EuroVoc or subject term is held; the publisher's typeDocument is a document type, not a subject"],
        ["legal_status", "no in-force status, repeal or commencement fact is served here; status_on serves the publisher's force assertions"],
        ["labels", "the values are the publisher's IRIs and lexical values, not read into labels; a document-type IRI is served as the publisher wrote it"],
    ];

    internal const string ManifestationScope =
        "the manifestations the publisher asserted for this work's expressions (jolux:isEmbodiedBy), each with its formats (jolux:userFormat) and items " +
        "(jolux:isExemplifiedBy) as the publisher wrote them, read verbatim from the index's fact table; the manifestation this corpus retained the body of is marked " +
        "retained and carries the corpus member's body digest; a requested format is the last segment of the format IRI as the publisher wrote it (xml, pdf, pdfa), matched exactly, and a format no manifestation " +
        "of the selected expressions carries is refused format_not_available naming the ones held; nothing is fetched and nothing is said about which manifestation is authentic";

    internal static readonly string[][] ManifestationNotHeld =
    [
        ["manifestation_bytes", "only the retained manifestation's body digest is held; the other manifestations are named from the publisher's assertions, not held"],
        ["authenticity", "which manifestation is the authentic one is not asserted here; the publisher's legalValue facts are served by classification"],
        ["format_semantics", "a format IRI is served as written and its last segment is the token the format parameter matches; nothing about the bytes is inferred from it"],
    ];

    /// <summary>
    /// <c>classification</c>: the publisher's typed facts about a work, grouped by predicate. Takes
    /// <c>dossier</c>'s request (identifier, optional language) and refuses as <c>dossier</c> refuses;
    /// the facts are read for the work's own IRIs and the expressions of the selected states, and served
    /// verbatim.
    /// </summary>
    public V3PlatformOperationOutcome Classification(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "classification", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus classification operation only accepts classification/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_classification", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        var subjects = WorkSubjects(scope);
        var facts = _reader!.ResolveFacts(subjects);
        var served = new[]
        {
            LuxembourgAssertionPredicate.TypeDocument, LuxembourgAssertionPredicate.RdfType, LuxembourgAssertionPredicate.LegalValue,
            LuxembourgAssertionPredicate.ResponsibilityOf, LuxembourgAssertionPredicate.HistoricalLegalId,
            LuxembourgAssertionPredicate.PublicationDate, LuxembourgAssertionPredicate.DateDocument,
        }.Select(ContractWire.NameOf).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = ClassificationScope,
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            publisher_work_iri = states[0].PublisherWorkIri,
            publisher_legal_resource_iri = states[0].PublisherLegalResourceIri,
            subjects,
            available_languages = availableLanguages,
            document_types = FactsOf(facts, LuxembourgAssertionPredicate.TypeDocument),
            resource_types = FactsOf(facts, LuxembourgAssertionPredicate.RdfType),
            legal_values = FactsOf(facts, LuxembourgAssertionPredicate.LegalValue),
            responsible_bodies = FactsOf(facts, LuxembourgAssertionPredicate.ResponsibilityOf),
            historical_identifiers = FactsOf(facts, LuxembourgAssertionPredicate.HistoricalLegalId),
            publication_dates = FactsOf(facts, LuxembourgAssertionPredicate.PublicationDate),
            document_dates = FactsOf(facts, LuxembourgAssertionPredicate.DateDocument),
            fact_count = facts.Count,
            other_predicates_held = facts.Select(static fact => fact.Predicate)
                .Where(predicate => !served.Contains(predicate, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = ClassificationNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "classification", result.RootElement));
    }

    /// <summary>
    /// <c>manifestation</c>: the publisher's manifestations of a work's expressions with their formats and
    /// items, the retained one marked. Takes <c>dossier</c>'s request plus an optional <c>format</c>
    /// (the last segment of the format IRI) and refuses as <c>dossier</c> refuses, plus
    /// <c>format_not_available</c> when the selected expressions have no manifestation in that format.
    /// </summary>
    public V3PlatformOperationOutcome Manifestation(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "manifestation", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus manifestation operation only accepts manifestation/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        var requestedFormat = request.Parameters.TryGetProperty("format", out var formatValue) && formatValue.ValueKind == JsonValueKind.String
            ? formatValue.GetString()
            : null;
        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_manifestation", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        var expressions = scope.Select(static state => state.ExpressionIri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var expressionFacts = _reader!.ResolveFacts(expressions);
        var embodiedBy = ContractWire.NameOf(LuxembourgAssertionPredicate.IsEmbodiedBy);
        var manifestationIris = expressionFacts
            .Where(fact => string.Equals(fact.Predicate, embodiedBy, StringComparison.Ordinal))
            .Select(static fact => fact.ObjectValue)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var manifestationFacts = _reader.ResolveFacts(manifestationIris);

        // What this corpus retained: the member behind each selected state names the one manifestation and
        // format whose body it holds.
        var retained = scope
            .SelectMany(state => _reader.ResolveStateSources(state.StateSha256)
                .Select(source => (State: state, Member: MemberOf(source.ObjectRefSha256))))
            .Where(static entry => entry.Member?.LuxembourgRights is not null)
            .Select(static entry => new
            {
                expression_iri = entry.Member!.LuxembourgRights!.SelectedWemi.ExpressionIri,
                manifestation_iri = entry.Member.LuxembourgRights.SelectedWemi.ManifestationIri,
                format_iri = entry.Member.LuxembourgRights.SelectedWemi.FormatIri,
                item_iri = entry.Member.LuxembourgRights.SelectedWemi.ItemIri,
                body_sha256 = entry.Member.BodySha256,
                body_byte_length = entry.Member.BodyByteLength,
                object_ref_sha256 = entry.Member.ObjectRefSha256,
            })
            .DistinctBy(static entry => entry.object_ref_sha256)
            .OrderBy(static entry => entry.manifestation_iri, StringComparer.Ordinal)
            .ThenBy(static entry => entry.object_ref_sha256, StringComparer.Ordinal)
            .ToArray();
        var retainedByManifestation = retained.ToLookup(static entry => entry.manifestation_iri, StringComparer.Ordinal);

        var rows = expressions.Select(expression => new
        {
            expression_iri = expression,
            languages = scope.Where(state => string.Equals(state.ExpressionIri, expression, StringComparison.Ordinal))
                .Select(static state => state.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            manifestations = expressionFacts
                .Where(fact => string.Equals(fact.SubjectIri, expression, StringComparison.Ordinal) &&
                               string.Equals(fact.Predicate, embodiedBy, StringComparison.Ordinal))
                .Select(fact => fact.ObjectValue)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(manifestation => new
                {
                    manifestation_iri = manifestation,
                    formats = FactsOf(manifestationFacts, LuxembourgAssertionPredicate.UserFormat, manifestation)
                        .Select(static fact => new { format_iri = fact.value, format = FormatToken(fact.value), evidence_sha256 = fact.evidence_sha256 })
                        .ToArray(),
                    items = FactsOf(manifestationFacts, LuxembourgAssertionPredicate.IsExemplifiedBy, manifestation)
                        .Select(static fact => new { item_iri = fact.value, evidence_sha256 = fact.evidence_sha256 })
                        .ToArray(),
                    resource_types = FactsOf(manifestationFacts, LuxembourgAssertionPredicate.RdfType, manifestation)
                        .Select(static fact => fact.value).ToArray(),
                    retained = retainedByManifestation[manifestation].Any(),
                    retained_body_sha256s = retainedByManifestation[manifestation]
                        .Select(static entry => entry.body_sha256).Where(static value => value is not null)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                })
                .ToArray(),
        }).ToArray();

        var availableFormats = rows.SelectMany(static row => row.manifestations)
            .SelectMany(static manifestation => manifestation.formats)
            .Select(static format => format.format)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (requestedFormat is not null && !availableFormats.Contains(requestedFormat, StringComparer.Ordinal))
        {
            using var unavailable = JsonSerializer.SerializeToDocument(new
            {
                requested_format = requestedFormat,
                available_formats = availableFormats,
                requested_identifier = identifier,
                requested_language = requestedLanguage,
                expressions,
                rule = "a format is the last segment of the publisher's userFormat IRI, matched exactly; the formats listed are every one the selected expressions' manifestations carry",
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "format_not_available", unavailable.RootElement));
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = ManifestationScope,
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            requested_format = requestedFormat,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            publisher_work_iri = states[0].PublisherWorkIri,
            available_languages = availableLanguages,
            available_formats = availableFormats,
            expressions = requestedFormat is null
                ? rows
                : rows.Select(row => new
                {
                    row.expression_iri,
                    row.languages,
                    manifestations = row.manifestations
                        .Where(manifestation => manifestation.formats.Any(format => string.Equals(format.format, requestedFormat, StringComparison.Ordinal)))
                        .ToArray(),
                }).ToArray(),
            retained,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = ManifestationNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "manifestation", result.RootElement));
    }

    /// <summary>The IRIs the publisher's facts about a work are asserted on: its work and legal-resource IRIs and the expressions of the given states.</summary>
    private static string[] WorkSubjects(IReadOnlyList<LuxembourgIndexResolvedState> states) =>
        states.SelectMany(static state => new[] { state.PublisherWorkIri, state.PublisherLegalResourceIri, state.ExpressionIri })
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>The last path segment of a publisher format IRI, as written: the token the <c>format</c> parameter matches exactly.</summary>
    private static string FormatToken(string formatIri)
    {
        var trimmed = formatIri.TrimEnd('/');
        return trimmed[(trimmed.LastIndexOf('/') + 1)..];
    }

    /// <summary>One fact as the answers serve it: the property names are the wire names.</summary>
    private sealed record FactView(
        string subject_iri, string object_kind, string value, string? datatype_iri, string? language_tag, string evidence_sha256);

    /// <summary>The facts of one predicate (optionally of one subject), each as the answer serves it, in the index's order.</summary>
    private static FactView[] FactsOf(
        IReadOnlyList<LuxembourgIndexWorkFact> facts, LuxembourgAssertionPredicate predicate, string? subject = null)
    {
        var name = ContractWire.NameOf(predicate);
        return facts
            .Where(fact => string.Equals(fact.Predicate, name, StringComparison.Ordinal) &&
                           (subject is null || string.Equals(fact.SubjectIri, subject, StringComparison.Ordinal)))
            .Select(static fact => new FactView(
                fact.SubjectIri,
                fact.ObjectKind,
                fact.ObjectValue,
                fact.DatatypeIri.Length == 0 ? null : fact.DatatypeIri,
                fact.LanguageTag.Length == 0 ? null : fact.LanguageTag,
                fact.EvidenceSha256))
            .ToArray();
    }

    internal const string StatusOnScope =
        "what the publisher asserted about this work's own force, read verbatim from the index's fact table (jolux:inForceStatus, jolux:dateEntryInForce, " +
        "jolux:dateNoLongerInForce, each with its subject, value, datatype and evidence digest), beside the state as_of selects for the requested date; the answer " +
        "adds one reading of the publisher's dates by a fixed rule and nothing else: it is not a legal opinion, and a state served here is the text the publisher " +
        "dates as applicable, which is a different fact from force";

    internal const string StatusOnReadingRule =
        "asserted_in_force_on_date is true when the publisher's dateEntryInForce is on or before the requested date and no dateNoLongerInForce is on or before it, " +
        "false when a dateNoLongerInForce is on or before it or every dateEntryInForce is after it, and null when the publisher asserted no dated force fact, " +
        "when only a dateNoLongerInForce after the requested date is asserted (an end alone does not say the work was in force), or when a dated fact's " +
        "lexical value is not exactly a civil date yyyy-MM-dd (a dateTime, a timezone-bearing date or any other form is served verbatim and not read); " +
        "inForceStatus is served as the publisher's token and is not read; reading_basis names which case applied";

    internal static readonly string[][] StatusOnNotHeld =
    [
        ["repeal_and_amendment_events", "no repeal, amendment or commencement event is held; the dates above are the publisher's own assertions about the work as a whole"],
        ["article_level_force", "no article-level force fact is held; an article's applicability date is served by as_of and is a different fact"],
        ["status_at_publication", "the facts are those the publisher asserted when observed; when they were asserted is not held, so a status is not dated to an observation"],
    ];

    internal const string BrowseScope =
        "the works this index holds, one row per work key in ordinal order, with the publisher identifiers and languages their states carry, the first and latest " +
        "state dates, the state count and the publisher's document and resource types read verbatim from the index's fact table; a type filters to works whose " +
        "typeDocument is exactly that IRI or ends exactly in /{type} (case-sensitive, no pattern); a language filters to works with a state in it; paged by work key";

    public const int BrowseMaxRows = 200;

    internal static readonly string[][] BrowseNotHeld =
    [
        ["publisher_universe", "how many works the publisher holds, or which of them this index lacks: the rows are what was admitted"],
        ["titles", "titles are served by dossier and resolve; a row here names the work by its identifiers"],
        ["legal_status", "no in-force fact is read here; status_on serves the publisher's force assertions per work"],
    ];

    /// <summary>
    /// <c>status_on</c>: the publisher's force assertions about a work, beside the state <c>as_of</c>
    /// selects for the date. Takes <c>as_of</c>'s request and refuses as <c>as_of</c> refuses; the
    /// facts are work-level, read for the work's own IRIs, and served verbatim with one fixed reading
    /// of the publisher's dates. When no force fact is asserted the absence is typed, never read as
    /// "not in force".
    /// </summary>
    public V3PlatformOperationOutcome StatusOn(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "status_on", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus status_on operation only accepts status_on/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedDate = RequiredString(request.Parameters, "date");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (!DateOnly.TryParseExact(
                requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_status_on", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        var servedLanguages = requestedLanguage is null ? availableLanguages : new[] { requestedLanguage };
        var selected = new List<object>();
        var ambiguous = new List<string>();
        foreach (var language in servedLanguages)
        {
            var ofLanguage = scope
                .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                .ToArray();
            var (candidates, nextDate) = SelectAtDate(ofLanguage, requestedDate);
            if (candidates.Length == 0)
            {
                continue;
            }

            if (candidates.Length > 1)
            {
                ambiguous.AddRange(candidates.Select(StateUrl));
                continue;
            }

            selected.Add(StateReference(candidates[0], nextDate));
        }

        if (ambiguous.Count != 0)
        {
            return RefuseAmbiguousVersion(request, observedAt, requestedDate, ambiguous.Order(StringComparer.Ordinal).ToArray(), bound: null);
        }

        if (selected.Count == 0)
        {
            return RefuseNoVersionForDate(request, observedAt, scope, requestedDate, bound: null);
        }

        // The force facts are the work's, not a state's: read for the work's own IRIs only.
        var workIris = states.SelectMany(static state => new[] { state.PublisherWorkIri, state.PublisherLegalResourceIri })
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var facts = _reader!.ResolveFacts(workIris);
        var statusFacts = FactsOf(facts, LuxembourgAssertionPredicate.InForceStatus);
        var entryFacts = FactsOf(facts, LuxembourgAssertionPredicate.DateEntryInForce);
        var endFacts = FactsOf(facts, LuxembourgAssertionPredicate.DateNoLongerInForce);
        var reading = ReadForce(entryFacts, endFacts, requestedDate);

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = StatusOnScope,
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            publisher_work_iri = states[0].PublisherWorkIri,
            publisher_legal_resource_iri = states[0].PublisherLegalResourceIri,
            subjects = workIris,
            available_languages = availableLanguages,
            states = selected,
            force_facts_held = statusFacts.Length + entryFacts.Length + endFacts.Length > 0,
            in_force_status = statusFacts,
            entry_into_force = entryFacts,
            no_longer_in_force = endFacts,
            asserted_in_force_on_date = reading.Value,
            reading_basis = reading.Basis,
            reading_rule = StatusOnReadingRule,
            what_would_answer = statusFacts.Length + entryFacts.Length + endFacts.Length > 0 ? null : new[] { "new_official_observation" },
            asserts_absence_of_law = false,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = StatusOnNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "version_state", result.RootElement));
    }

    /// <summary>
    /// The one reading <c>status_on</c> makes of the publisher's dated force facts, by
    /// <see cref="StatusOnReadingRule"/>: a civil-date comparison and nothing else. Any lexical value
    /// that is not a civil date, or no dated fact at all, leaves the reading null and says why.
    /// </summary>
    private static (bool? Value, string Basis) ReadForce(FactView[] entryFacts, FactView[] endFacts, string requestedDate)
    {
        if (entryFacts.Length == 0 && endFacts.Length == 0)
        {
            return (null, "no dated force fact asserted");
        }

        // The whole lexical value, exactly yyyy-MM-dd: a dateTime, a timezone-bearing date or any other form is
        // served verbatim and not read (the review of PR #757 found a prefix parse reading "2024-01-22garbage").
        static string? CivilDate(string value) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? value
                : null;

        var entries = entryFacts.Select(static fact => CivilDate(fact.value)).ToArray();
        var ends = endFacts.Select(static fact => CivilDate(fact.value)).ToArray();
        if (entries.Any(static value => value is null) || ends.Any(static value => value is null))
        {
            return (null, "a dated force fact is not a civil date; served verbatim, not read");
        }

        if (ends.Any(end => string.CompareOrdinal(end, requestedDate) <= 0))
        {
            return (false, "a dateNoLongerInForce is on or before the requested date");
        }

        if (entries.Length == 0)
        {
            return (null, "no dateEntryInForce asserted; a later dateNoLongerInForce alone does not say the work was in force");
        }

        return entries.Any(entry => string.CompareOrdinal(entry, requestedDate) <= 0)
            ? (true, "a dateEntryInForce is on or before the requested date and no dateNoLongerInForce is")
            : (false, "every dateEntryInForce is after the requested date");
    }

    /// <summary>
    /// <c>browse</c>: the held works as one ordered, paged list with the publisher's document types.
    /// Takes an optional <c>type</c> (a typeDocument IRI or its last segment), an optional
    /// <c>language</c>, a <c>limit</c> up to <see cref="BrowseMaxRows"/> and an <c>after</c> work key.
    /// </summary>
    public V3PlatformOperationOutcome Browse(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "browse", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus browse operation only accepts browse/1.");
        }

        var requestedLanguage = OptionalLanguage(request.Parameters);
        var requestedType = request.Parameters.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String
            ? typeValue.GetString()
            : null;
        var after = request.Parameters.TryGetProperty("after", out var afterValue) && afterValue.ValueKind == JsonValueKind.String
            ? RequiredString(request.Parameters, "after")
            : null;
        var limit = BrowseMaxRows;
        if (request.Parameters.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
                limit < 1 || limit > BrowseMaxRows)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'limit' is not a whole number of rows within the ceiling.");
            }
        }

        if (requestedType is not null && !IsTypeFilter(requestedType))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'type' is neither an absolute IRI nor a bare type token.");
        }

        if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        var languagesHeld = _reader.ResolveStatePopulation().Languages.ToArray();
        if (requestedLanguage is not null && !languagesHeld.Contains(requestedLanguage, StringComparer.Ordinal))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage,
                available_languages = languagesHeld,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        var page = _reader.ResolveWorkRecords(requestedLanguage, requestedType, after, limit + 1);
        var served = page.Take(limit).ToArray();
        var subjects = served.SelectMany(static record => record.PublisherWorkIris.Concat(record.PublisherLegalResourceIris))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var facts = _reader.ResolveFacts(subjects);
        var rows = served.Select(record =>
        {
            var own = new HashSet<string>(record.PublisherWorkIris.Concat(record.PublisherLegalResourceIris), StringComparer.Ordinal);
            return new
            {
                work_key = record.WorkKey,
                identifier = $"/lu-legilux/{record.WorkKey}",
                publisher_work_iris = record.PublisherWorkIris,
                publisher_legal_resource_iris = record.PublisherLegalResourceIris,
                languages = record.Languages,
                history_begins = record.FirstDate,
                latest_applicability_date = record.LastDate,
                state_count = record.StateCount,
                document_types = FactsOf(facts, LuxembourgAssertionPredicate.TypeDocument).Where(fact => own.Contains(fact.subject_iri)).ToArray(),
                resource_types = FactsOf(facts, LuxembourgAssertionPredicate.RdfType).Where(fact => own.Contains(fact.subject_iri)).ToArray(),
            };
        }).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = BrowseScope,
            requested_type = requestedType,
            requested_language = requestedLanguage,
            requested_after = after,
            limit,
            publisher = "lu-legilux",
            languages_held = languagesHeld,
            works = rows,
            next_after = page.Count > limit ? served[^1].WorkKey : null,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = BrowseNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "work_record", result.RootElement));
    }

    /// <summary>A type filter is an absolute IRI or a bare token of the characters a type segment carries; nothing a LIKE pattern could read.</summary>
    private static bool IsTypeFilter(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var iri) && (iri.Scheme == Uri.UriSchemeHttp || iri.Scheme == Uri.UriSchemeHttps)
        || (value.Length > 0 && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'));

    public const int CitationMaxEdges = 200;

    internal const string CitationScope =
        "the references the publisher wrote in the text of the selected state's articles, and in the footnote bodies its notes carry (in_note), " +
        "read from the index's edge table of forward edges (lane R4); an edge records that a reference was written where it says, " +
        "and this answer assesses neither what relationship the reference states nor whether it has any legal effect";

    internal const string CitationTargetNote =
        "href is the value the publisher wrote, verbatim; target_kind and target_iri come from a fixed reading of it that resolves nothing: " +
        "legilux_eli is a value that begins /eli/ or http://data.legilux.public.lu/eli/ and its target_iri is the absolute form (the one change is putting the host " +
        "in front of a relative value; no trailing slash is trimmed and no scheme or case is changed), other_uri is any other absolute http or https value and its " +
        "target_iri is the value as written, unparsed is anything else and has no target_iri; resolution is held_work only when target_iri is exactly the publisher " +
        "legal-resource IRI (the form the publisher writes in running text, ending /jo) or the publisher work IRI of a work this index holds, and then target_work_key " +
        "names it, not_held when it is not (that says this index holds no work with exactly that IRI, not that no such work exists or that it is not held under another " +
        "spelling), and unparsed when there is no target_iri; no edge is dropped or upgraded, " +
        "and a note's reference is the publisher's own reference to the act named there, not a statement of how that act relates to the article";

    internal const string CitationOrder =
        "by the citing article's publisher id, then its identity, then the order the references occur in it (a footnote body's at its note reference), " +
        "for each language in turn; the index holds no position of an article in its document, so that order is not one";

    internal const string CitationPageIs =
        "the edges are in the stated order and a truncated page is the first limit of them from the cursor, not the most relevant";

    internal static readonly string[][] CitationNotHeld =
    [
        ["relationship_type", "no type of relationship is assessed or held for a reference: relationship_type_assessed is false"],
        ["current_legal_effect", "no legal effect of a reference is assessed or held: current_legal_effect_assessed is false"],
        ["structured_relations", "the publisher's structured relation records (modifies, repeals, based on, transposes) are not held by this index, so these edges are only the references written in the text"],
        ["references_to_this_work", "which texts refer to this work is not answered here: it is the inverse of these edges and its own operation"],
    ];

    /// <summary>
    /// <c>citation</c>: the references the publisher wrote in a state's articles, as the forward edges of lane R4's
    /// edge table. It takes <c>as_of</c>'s request (an identifier and a date, and optionally a language) and selects
    /// states by <c>as_of</c>'s rule and refuses as <c>as_of</c> refuses (shared builders), and it may name one article by its
    /// publisher id. Each edge names the citing article and its place in it, the publisher's label and value verbatim,
    /// and the target read from the value by a fixed grammar that resolves nothing; a target is a held work only by
    /// exact string equality with a publisher work IRI the index holds. Nothing is assessed: no relationship type, no
    /// legal effect.
    /// </summary>
    public V3PlatformOperationOutcome Citation(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "citation", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus citation operation only accepts citation/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedDate = RequiredString(request.Parameters, "date");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        string? Optional(string name) =>
            request.Parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? RequiredString(request.Parameters, name)
                : null;
        var anchor = Optional("anchor");
        var after = Optional("after");
        var limit = CitationMaxEdges;
        if (request.Parameters.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
                limit < 1 || limit > CitationMaxEdges)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'limit' is not a whole number of edges within the ceiling.");
            }
        }

        if (!DateOnly.TryParseExact(
                requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r4_citation", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
        var servedLanguages = requestedLanguage is null ? availableLanguages : new[] { requestedLanguage };

        var selectedStates = new List<LuxembourgIndexResolvedState>();
        var ambiguous = new List<string>();
        foreach (var language in servedLanguages)
        {
            var ofLanguage = scope
                .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                .ToArray();
            var (selected, _) = SelectAtDate(ofLanguage, requestedDate);
            if (selected.Length == 0)
            {
                continue;
            }

            if (selected.Length > 1)
            {
                ambiguous.AddRange(selected.Select(StateUrl));
                continue;
            }

            selectedStates.Add(selected[0]);
        }

        if (ambiguous.Count != 0)
        {
            return RefuseAmbiguousVersion(request, observedAt, requestedDate, ambiguous.Order(StringComparer.Ordinal).ToArray(), bound: null);
        }

        if (selectedStates.Count == 0)
        {
            return RefuseNoVersionForDate(request, observedAt, scope, requestedDate, bound: null);
        }

        var edges = new List<(LuxembourgIndexResolvedState State, LuxembourgIndexCitation Edge)>();
        var absent = new List<object>();
        foreach (var state in selectedStates)
        {
            if (anchor is not null &&
                !_reader!.ResolveArticleIds(state.StateSha256).Contains(anchor, StringComparer.Ordinal))
            {
                absent.Add(new
                {
                    language = state.Language,
                    applicability_date = state.ApplicabilityDate,
                    state_sha256 = state.StateSha256,
                    permalink = StateUrl(state),
                });
                continue;
            }

            edges.AddRange(_reader!.ResolveStateCitations(state.StateSha256, anchor)
                .Select(edge => (state, edge)));
        }

        if (anchor is not null && absent.Count == selectedStates.Count)
        {
            using var notInVersion = JsonSerializer.SerializeToDocument(new
            {
                requested_anchor = anchor,
                nearest_anchors = NearestAnchors(_reader!.ResolveArticleIds(selectedStates[^1].StateSha256), anchor),
                do_not_fall_back_to_full_text_search = true,
                what_would_answer = AnchorNotInVersionRoutes,
                asserts_absence_of_law = false,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "anchor_not_in_version", notInVersion.RootElement));
        }

        static string Cursor((LuxembourgIndexResolvedState State, LuxembourgIndexCitation Edge) entry) =>
            $"{entry.State.StateSha256}.{entry.Edge.ArticleIdentitySha256}.{entry.Edge.Ordinal}";
        var start = 0;
        if (after is not null)
        {
            var index = edges.FindIndex(entry => string.Equals(Cursor(entry), after, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'after' names no edge of this query; a cursor is the continue_after of the same query, scope and index.");
            }

            start = index + 1;
        }

        var page = edges.Skip(start).Take(limit).ToArray();
        var truncated = start + page.Length < edges.Count;
        var held = _reader!.ResolveHeldWorks(page
            .Select(static entry => entry.Edge.ToRef)
            .Where(static target => target is not null)
            .Select(static target => target!)
            .ToArray());
        var rows = page.Select(entry =>
        {
            var target = entry.Edge.ToRef;
            var isHeld = target is not null && held.ContainsKey(target);
            return new
            {
                language = entry.State.Language,
                state_sha256 = entry.State.StateSha256,
                article_identity_sha256 = entry.Edge.ArticleIdentitySha256,
                article_publisher_id = entry.Edge.PublisherId,
                ordinal = entry.Edge.Ordinal,
                in_note = entry.Edge.InNote,
                label = entry.Edge.Label,
                href = entry.Edge.Href,
                target_kind = entry.Edge.ToKind,
                target_iri = target,
                resolution = target is null ? "unparsed" : isHeld ? "held_work" : "not_held",
                target_work_key = isHeld ? held[target!] : null,
            };
        }).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = CitationScope,
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            requested_anchor = anchor,
            requested_after = after,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            relationship_type_assessed = false,
            current_legal_effect_assessed = false,
            states = selectedStates.Select(state => new
            {
                language = state.Language,
                applicability_date = state.ApplicabilityDate,
                state_sha256 = state.StateSha256,
                permalink = StateUrl(state),
                stable_coordinate = StableCoordinate(state),
                edges_in_scope = edges.Count(entry => string.Equals(entry.State.StateSha256, state.StateSha256, StringComparison.Ordinal)),
            }).ToArray(),
            absent_in_states = absent,
            edge_count = edges.Count,
            edge_order = CitationOrder,
            limit,
            truncated,
            continue_after = truncated ? Cursor(page[^1]) : null,
            page_is = CitationPageIs,
            edges = rows,
            target_note = CitationTargetNote,
            available_languages = availableLanguages,
            not_held = CitationNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "relation_edge", result.RootElement));
    }

    public const int RelationsMaxEdges = 200;

    internal const string VerifyScope =
        "whether a hash-pinned permalink of this publisher still names the state this index holds at its stable coordinate, by the state digest the index " +
        "computed from the retained publisher bytes under its rule profiles (a matching digest is verified as digest_matches; a digest the coordinate no longer " +
        "carries is the pinned_digest_mismatch refusal naming the current one, unless a retained generation holds that state, when it is verified there as " +
        "digest_matches with held_in naming the generation and superseded_by the current state of its language, null when the coordinate holds none any " +
        "more); for a work identifier or work coordinate, the current digests of every state " +
        "held, and for a dated stable coordinate those held exactly there, so a caller can pin them; a pinned permalink may carry an article id after # " +
        "(the article permalink evidence_bundle writes), and then the article must be one the pinned state holds or the answer is anchor_not_in_version; " +
        "nothing about the text or its legal effect is assessed";

    internal static readonly string[][] VerifyNotHeld =
    [
        ["publisher_signature", "no signature or attestation of the publisher is held; the digest is this index's own reading of the retained bytes under the named rule profiles"],
        ["observation_time", "when the publisher served the retained bytes is not held in this index, so no observation time is stated"],
        ["text_verification", "the text itself is not compared here: evidence_bundle serves it with its digests, and provenance names its sources"],
    ];

    internal const string RelationsScope =
        "the reference edges this index holds for a work, in both directions, from one edge table (lane R4): outbound, the references the publisher wrote in " +
        "the text of the selected state's articles and their notes (what citation serves); inbound, the references in the text of any state this index holds whose " +
        "target is exactly this work's publisher legal-resource IRI or its publisher work IRI (what cited_by serves); every edge is edge_type cites, " +
        "asserted_by publisher_text, source_predicate akn_ref; an edge records that a reference was written where it says, it is derived here, and this answer " +
        "assesses neither what relationship the reference states nor whether it has any legal effect";

    internal const string RelationsOrder =
        "outbound edges first, in citation's order (by the citing article's publisher id, then its identity, then the order the references occur in it, for " +
        "each language in turn), then inbound edges in cited_by's order (by the citing state's publisher date, then its work key, language and expression, " +
        "then the citing article's publisher id and identity, then the order the references occur in it)";

    internal static readonly string[][] RelationsNotHeld =
    [
        ["relationship_type", "no type of relationship is assessed or held for a reference: relationship_type_assessed is false"],
        ["current_legal_effect", "no legal effect of a reference is assessed or held: current_legal_effect_assessed is false"],
        ["structured_relations", "the publisher's structured relation records (modifies, repeals, based on, transposes, is part of, is realized by) are not in this index's edge table, so no such edge is served; the one edge type held is cites"],
        ["transposition", "transposition edges are their own operation and are not served by this mount"],
    ];

    /// <summary>
    /// <c>verify</c>: a hash-pinned permalink is verified against the state digest this index holds at
    /// its stable coordinate, the same reading <c>resolve</c> makes of a pinned identifier; a work
    /// identifier or stable coordinate answers the current digests so a caller can pin them.
    /// </summary>
    public V3PlatformOperationOutcome Verify(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "verify", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus verify operation only accepts verify/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (TryParseEuropePermalink(identifier, out var celex, out var europeLanguage, out var wordingDate, out var europeDigest, out var provision))
        {
            return VerifyEuropeState(request, identifier, celex, europeLanguage, wordingDate, europeDigest, provision, requestedLanguage, observedAt)
                ?? VerifyEurope(request, identifier, celex, europeLanguage, wordingDate, europeDigest, provision, requestedLanguage, observedAt);
        }

        if (TryParsePinnedPermalink(identifier, out var workKey, out var applicabilityDate, out var requestedDigest, out var anchor))
        {
            if (_reader is null)
            {
                using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.LuLegilux),
                    new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
            }

            var pinnedStates = _reader.ResolveState(workKey, applicabilityDate);

            // A digest the mounted index does not hold at its coordinate, held by a retained generation, is verified there
            // before any refusal over the current states: the coordinate may hold another state, several (one per
            // language), or none any more (review of #889). superseded_by names the current state of that language, if any.
            if (!pinnedStates.Any(state => string.Equals(state.StateSha256, requestedDigest, StringComparison.Ordinal) &&
                                           (requestedLanguage is null || string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))) &&
                FromGeneration(workKey, applicabilityDate, requestedLanguage, requestedDigest) is var (holder, superseded))
            {
                var current = pinnedStates.FirstOrDefault(state => string.Equals(state.Language, superseded.Language, StringComparison.Ordinal));
                return VerifiedPinned(
                    request, observedAt, identifier, requestedDigest, requestedLanguage, anchor, superseded,
                    holder.Reader.ResolveState(workKey, applicabilityDate).Select(static state => state.Language)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    holder.Reader, holder.Reader.CorpusRef.Sha256,
                    new { snapshot_id = holder.IndexSha256, observation = holder.Observation, built_at = holder.BuiltAt },
                    current is null ? null : new { state_sha256 = current.StateSha256, permalink = StateUrl(current) });
            }

            if (pinnedStates.Count == 0)
            {
                return Unknown(request, identifier, observedAt, PublisherId.LuLegilux,
                    "a hash-pinned permalink of a state this index holds at that stable coordinate");
            }

            var pinnedLanguages = pinnedStates.Select(static state => state.Language)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (requestedLanguage is not null && !pinnedLanguages.Contains(requestedLanguage, StringComparer.Ordinal))
            {
                using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
                {
                    requested_language = requestedLanguage,
                    available_languages = pinnedLanguages,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
            }

            var pinnedScope = requestedLanguage is null
                ? pinnedStates
                : pinnedStates.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
            var matching = pinnedScope.Where(state => string.Equals(
                requestedDigest, state.StateSha256, StringComparison.Ordinal)).ToArray();
            if (matching.Length == 0)
            {
                if (pinnedScope.Count > 1)
                {
                    using var ambiguous = JsonSerializer.SerializeToDocument(new
                    {
                        requested_identifier = identifier,
                        candidates = pinnedScope.Select(StateUrl).ToArray(),
                    });
                    return V3PlatformOperationOutcome.Refused(
                        Context("refusal", observedAt),
                        new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
                }

                var current = pinnedScope[0];
                using var mismatch = JsonSerializer.SerializeToDocument(new
                {
                    requested_digest = requestedDigest,
                    current_digest = current.StateSha256,
                    stable_coordinate = StableCoordinate(current),
                    current_hash_pinned_url = StateUrl(current),
                    rule_profile_sha256s = current.RuleProfileSha256s,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
            }

            return VerifiedPinned(
                request, observedAt, identifier, requestedDigest, requestedLanguage, anchor, matching[0], pinnedLanguages,
                _reader, _corpus.ArtifactRef.Sha256, heldIn: null, supersededBy: null);
        }

        return VerifyCoordinate(request, observedAt, identifier, requestedLanguage);
    }

    /// <summary>
    /// The <c>verify</c> answer for a pinned permalink whose digest <paramref name="holder"/> holds at its coordinate: the
    /// mounted index, or a retained generation (<paramref name="heldIn"/> naming it and <paramref name="supersededBy"/> the
    /// mounted state that replaced it). The article anchor, the sources and the verifying digests are the holder's.
    /// </summary>
    private V3PlatformOperationOutcome VerifiedPinned(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        string identifier,
        string requestedDigest,
        string? requestedLanguage,
        string? anchor,
        LuxembourgIndexResolvedState verified,
        string[] pinnedLanguages,
        LuxembourgIndexReader holder,
        string corpusSha256,
        object? heldIn,
        object? supersededBy)
    {
        {
            if (anchor is not null)
            {
                // The article permalink evidence_bundle writes: the digest verified above, and the
                // article id must be one this state holds, else the refusal article_history makes.
                var articleIds = holder.ResolveArticleIds(verified.StateSha256);
                if (!articleIds.Contains(anchor, StringComparer.Ordinal))
                {
                    using var notInVersion = JsonSerializer.SerializeToDocument(new
                    {
                        requested_anchor = anchor,
                        nearest_anchors = NearestAnchors(articleIds, anchor),
                        do_not_fall_back_to_full_text_search = true,
                        what_would_answer = AnchorNotInVersionRoutes,
                        asserts_absence_of_law = false,
                    });
                    return V3PlatformOperationOutcome.Refused(
                        Context("refusal", observedAt),
                        new V3PlatformOperationRefusal(request, "anchor_not_in_version", notInVersion.RootElement));
                }
            }

            using var verification = JsonSerializer.SerializeToDocument(new
            {
                scope = VerifyScope,
                requested_identifier = identifier,
                requested_digest = requestedDigest,
                requested_language = requestedLanguage,
                requested_anchor = anchor,
                article_permalink = anchor is null ? null : StateUrl(verified) + "#" + anchor,
                verdict = "digest_matches",
                publisher = "lu-legilux",
                work_key = verified.WorkKey,
                applicability_date = verified.ApplicabilityDate,
                language = verified.Language,
                state_sha256 = verified.StateSha256,
                stable_coordinate = StableCoordinate(verified),
                permalink = StateUrl(verified),
                expression_iri = verified.ExpressionIri,
                publisher_work_iri = verified.PublisherWorkIri,
                publisher_legal_resource_iri = verified.PublisherLegalResourceIri,
                rule_profile_sha256s = verified.RuleProfileSha256s,
                articles = verified.ArticleIdentities.Count,
                article_identities_sha256 = ArticleIdentitiesSha256(verified.ArticleIdentities),
                sources = holder.ResolveStateSources(verified.StateSha256).Select(SourceRow).ToArray(),
                available_languages = pinnedLanguages,
                held_in = heldIn,
                superseded_by = supersededBy,
                verified_by = new
                {
                    corpus_sha256 = corpusSha256,
                    index_sha256 = holder.IndexRef.Sha256,
                    registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
                },
                not_held = VerifyNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            });
            return V3PlatformOperationOutcome.Success(
                Context("success", observedAt),
                new V3PlatformOperationResult(request, "verification", verification.RootElement));
        }
    }

    /// <summary><c>verify</c> for a work identifier, a work coordinate or a dated stable coordinate: the current digests to pin.</summary>
    private V3PlatformOperationOutcome VerifyCoordinate(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        string identifier,
        string? requestedLanguage)
    {
        IReadOnlyList<LuxembourgIndexResolvedState> states;
        string[] availableLanguages;
        if (TryParseStableStateCoordinate(identifier, out var coordinateWorkKey, out var coordinateDate))
        {
            // The dated stable coordinate this mount writes into every answer and refusal: the
            // current digests of the states held exactly there.
            if (_reader is null)
            {
                using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.LuLegilux),
                    new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
            }

            states = _reader.ResolveState(coordinateWorkKey, coordinateDate);
            if (states.Count == 0)
            {
                return Unknown(request, identifier, observedAt, PublisherId.LuLegilux,
                    "a stable coordinate at which this index holds a state");
            }

            availableLanguages = states.Select(static state => state.Language)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (requestedLanguage is not null && !availableLanguages.Contains(requestedLanguage, StringComparer.Ordinal))
            {
                using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
                {
                    requested_language = requestedLanguage,
                    available_languages = availableLanguages,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
            }
        }
        else if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_verify", requestedLanguage,
                     out states, out availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        using var digests = JsonSerializer.SerializeToDocument(new
        {
            scope = VerifyScope,
            requested_identifier = identifier,
            requested_digest = (string?)null,
            requested_language = requestedLanguage,
            verdict = "current_digests",
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            states = scope.Select(state => new
            {
                language = state.Language,
                applicability_date = state.ApplicabilityDate,
                state_sha256 = state.StateSha256,
                stable_coordinate = StableCoordinate(state),
                permalink = StateUrl(state),
                rule_profile_sha256s = state.RuleProfileSha256s,
                articles = state.ArticleIdentities.Count,
                article_identities_sha256 = ArticleIdentitiesSha256(state.ArticleIdentities),
            }).ToArray(),
            available_languages = availableLanguages,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader!.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = VerifyNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "verification", digests.RootElement));
    }

    /// <summary>
    /// <c>relations</c>: the edges of lane R4's one edge table for a work, outbound (what
    /// <c>citation</c> serves for the state at the date, or the latest state per language when no
    /// date is given) and inbound (what <c>cited_by</c> serves), as one ordered, paged list; every
    /// edge is <c>cites</c>, asserted by the publisher's text, derived from an <c>akn_ref</c>.
    /// </summary>
    public V3PlatformOperationOutcome Relations(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "relations", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus relations operation only accepts relations/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        string? Optional(string name) =>
            request.Parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? RequiredString(request.Parameters, name)
                : null;
        var requestedDate = Optional("date");
        var after = Optional("after");
        var direction = Optional("direction") ?? "both";
        if (direction is not ("both" or "outbound" or "inbound"))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'direction' is not one of both, outbound, inbound.");
        }

        var limit = RelationsMaxEdges;
        if (request.Parameters.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
                limit < 1 || limit > RelationsMaxEdges)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'limit' is not a whole number of edges within the ceiling.");
            }
        }

        if (requestedDate is not null && !DateOnly.TryParseExact(
                requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r4_relations", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var workKey = states[0].WorkKey;
        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        var servedLanguages = requestedLanguage is null ? availableLanguages : new[] { requestedLanguage };

        // Outbound: the state at the date per language, or the latest state per language. Inbound
        // alone selects no state: those edges are references to the work from any held state.
        var selectedStates = new List<LuxembourgIndexResolvedState>();
        var ambiguous = new List<string>();
        string? ambiguousDate = null;
        foreach (var language in direction is "inbound" ? [] : servedLanguages)
        {
            var ofLanguage = scope
                .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                .ToArray();
            LuxembourgIndexResolvedState[] selected;
            if (requestedDate is null)
            {
                var latest = ofLanguage.Select(static state => state.ApplicabilityDate).Max(StringComparer.Ordinal);
                selected = ofLanguage.Where(state => string.Equals(state.ApplicabilityDate, latest, StringComparison.Ordinal)).ToArray();
            }
            else
            {
                (selected, _) = SelectAtDate(ofLanguage, requestedDate);
            }

            if (selected.Length == 0)
            {
                continue;
            }

            if (selected.Length > 1)
            {
                ambiguous.AddRange(selected.Select(StateUrl));
                ambiguousDate ??= selected[0].ApplicabilityDate;
                continue;
            }

            selectedStates.Add(selected[0]);
        }

        if (ambiguous.Count != 0)
        {
            return RefuseAmbiguousVersion(request, observedAt, requestedDate ?? ambiguousDate!, ambiguous.Order(StringComparer.Ordinal).ToArray(), bound: null);
        }

        if (requestedDate is not null && selectedStates.Count == 0 && direction is not "inbound")
        {
            return RefuseNoVersionForDate(request, observedAt, scope, requestedDate, bound: null);
        }

        var outbound = new List<(LuxembourgIndexResolvedState State, LuxembourgIndexCitation Edge)>();
        if (direction is not "inbound")
        {
            foreach (var state in selectedStates)
            {
                outbound.AddRange(_reader!.ResolveStateCitations(state.StateSha256, null).Select(edge => (state, edge)));
            }
        }

        var targets = states
            .SelectMany(static state => new[] { state.PublisherLegalResourceIri, state.PublisherWorkIri })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var inbound = new List<(LuxembourgIndexResolvedState State, LuxembourgIndexInboundCitation Edge)>();
        if (direction is not "outbound")
        {
            var inboundEdges = _reader!.ResolveCitationsTo(targets);
            var citingStates = _reader.ResolveStatesOfExpressions(
                    inboundEdges.Select(static edge => edge.ExpressionIri).Distinct(StringComparer.Ordinal).ToArray())
                .ToDictionary(static state => state.ExpressionIri, StringComparer.Ordinal);
            inbound.AddRange(inboundEdges
                .Select(edge => (State: citingStates.TryGetValue(edge.ExpressionIri, out var state)
                        ? state
                        : throw new InvalidDataException("An article of the index belongs to no state."),
                    Edge: edge))
                .OrderBy(static entry => entry.State.ApplicabilityDate, StringComparer.Ordinal)
                .ThenBy(static entry => entry.State.WorkKey, StringComparer.Ordinal)
                .ThenBy(static entry => entry.State.Language, StringComparer.Ordinal)
                .ThenBy(static entry => entry.State.ExpressionIri, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Edge.PublisherId, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Edge.ArticleIdentitySha256, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Edge.Ordinal));
        }

        var heldTargets = _reader!.ResolveHeldWorks(outbound
            .Select(static entry => entry.Edge.ToRef)
            .Where(static target => target is not null)
            .Select(static target => target!)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        var edges = new List<(string Cursor, object Row)>(outbound.Count + inbound.Count);
        foreach (var (state, edge) in outbound)
        {
            var target = edge.ToRef;
            var isHeld = target is not null && heldTargets.ContainsKey(target);
            edges.Add(($"outbound.{state.StateSha256}.{edge.ArticleIdentitySha256}.{edge.Ordinal}", new
            {
                direction = "outbound",
                edge_type = "cites",
                citing_work_key = state.WorkKey,
                citing_language = state.Language,
                citing_applicability_date = state.ApplicabilityDate,
                citing_state_sha256 = state.StateSha256,
                citing_permalink = StateUrl(state),
                article_identity_sha256 = edge.ArticleIdentitySha256,
                article_publisher_id = edge.PublisherId,
                ordinal = edge.Ordinal,
                in_note = edge.InNote,
                label = edge.Label,
                href = edge.Href,
                target_kind = edge.ToKind,
                target_iri = target,
                resolution = target is null ? "unparsed" : isHeld ? "held_work" : "not_held",
                target_work_key = isHeld ? heldTargets[target!] : null,
                is_self_reference = isHeld && string.Equals(heldTargets[target!], workKey, StringComparison.Ordinal),
            }));
        }

        foreach (var (state, edge) in inbound)
        {
            edges.Add(($"inbound.{state.StateSha256}.{edge.ArticleIdentitySha256}.{edge.Ordinal}", new
            {
                direction = "inbound",
                edge_type = "cites",
                citing_work_key = state.WorkKey,
                citing_language = state.Language,
                citing_applicability_date = state.ApplicabilityDate,
                citing_state_sha256 = state.StateSha256,
                citing_permalink = StateUrl(state),
                article_identity_sha256 = edge.ArticleIdentitySha256,
                article_publisher_id = edge.PublisherId,
                ordinal = edge.Ordinal,
                in_note = edge.InNote,
                label = edge.Label,
                href = edge.Href,
                target_kind = "legilux_eli",
                target_iri = (string?)edge.ToRef,
                resolution = "held_work",
                target_work_key = (string?)workKey,
                is_self_reference = string.Equals(state.WorkKey, workKey, StringComparison.Ordinal),
            }));
        }

        var start = 0;
        if (after is not null)
        {
            var index = edges.FindIndex(entry => string.Equals(entry.Cursor, after, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'after' names no edge of this query; a cursor is the continue_after of the same query, scope and index.");
            }

            start = index + 1;
        }

        var page = edges.Skip(start).Take(limit).ToArray();
        var truncated = start + page.Length < edges.Count;
        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = RelationsScope,
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            requested_direction = direction,
            requested_after = after,
            publisher = "lu-legilux",
            work_key = workKey,
            relationship_type_assessed = false,
            current_legal_effect_assessed = false,
            derived = true,
            edge_type = "cites",
            asserted_by = "publisher_text",
            source_predicate = "akn_ref",
            states = selectedStates.Select(state => new
            {
                language = state.Language,
                applicability_date = state.ApplicabilityDate,
                state_sha256 = state.StateSha256,
                permalink = StateUrl(state),
                stable_coordinate = StableCoordinate(state),
                outbound_edges = outbound.Count(entry => string.Equals(entry.State.StateSha256, state.StateSha256, StringComparison.Ordinal)),
            }).ToArray(),
            inbound_target_iris = targets,
            edge_count = edges.Count,
            edge_counts = new { outbound = outbound.Count, inbound = inbound.Count },
            edge_order = RelationsOrder,
            limit,
            truncated,
            continue_after = truncated ? page[^1].Cursor : null,
            page_is = CitationPageIs,
            edges = page.Select(static entry => entry.Row).ToArray(),
            target_note = CitationTargetNote,
            available_languages = availableLanguages,
            not_held = RelationsNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "relation_edge", result.RootElement));
    }

    public const int CitedByMaxEdges = 200;

    internal const string CitedByScope =
        "the references, in the text of any state this index holds, whose target is exactly this work's publisher legal-resource IRI or its publisher work IRI: " +
        "the forward edges that citation serves, read by their target from the same table (lane R4), so the two operations cannot disagree about a pair of texts; " +
        "an edge records that a reference was written where it says, it is derived here (the publisher asserted the reference in the citing text, not an inbound " +
        "relation on this work), and this answer assesses neither what relationship the reference states nor whether it has any legal effect";

    internal const string CitedByOrder =
        "by the citing state's publisher date, then its work key, language and expression, then the citing article's publisher id and identity, then the order the " +
        "references occur in it";

    internal const string CitedByPageIs =
        "the edges are in the stated order and a truncated page is the first limit of them from the cursor, not the most relevant";

    internal static readonly string[][] CitedByNotHeld =
    [
        ["relationship_type", "no type of relationship is assessed or held for a reference: relationship_type_assessed is false"],
        ["current_legal_effect", "no legal effect of a reference is assessed or held: current_legal_effect_assessed is false"],
        ["citing_texts_not_held", "a text this index does not hold cannot be among the citing texts, so the count here is the references the held texts write and never a count of everything that cites this work"],
        ["citing_works", "citing_works counts the distinct held works whose held texts write a reference to this work, grouped on the citing state's work key, so a work held as several states counts once and a work this index does not hold cannot be counted; a reference from a state of this work to itself is counted in edge_count and flagged by is_self_reference, and it is excluded from citing_works"],
        ["structured_relations", "the publisher's structured relation records (modifies, repeals, based on, transposes) are not held by this index, so these edges are only the references written in the text"],
    ];

    /// <summary>
    /// <c>cited_by</c>: the references written in any state this index holds whose target is exactly a work's publisher
    /// legal-resource IRI or publisher work IRI. It is <c>citation</c>'s forward edges read by their target from the same
    /// table, by an index on the target, so the two answer one fact and cannot disagree; it is derived and says so. Each
    /// edge names the citing state (its work, date, language and permalink), the citing article and the place in it, and the
    /// publisher's label and value verbatim. Nothing is assessed: no relationship type, no legal effect.
    /// </summary>
    public V3PlatformOperationOutcome CitedBy(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "cited_by", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus cited_by operation only accepts cited_by/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var after = request.Parameters.TryGetProperty("after", out var afterValue) && afterValue.ValueKind == JsonValueKind.String
            ? RequiredString(request.Parameters, "after")
            : null;
        var limit = CitedByMaxEdges;
        if (request.Parameters.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
                limit < 1 || limit > CitedByMaxEdges)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'limit' is not a whole number of edges within the ceiling.");
            }
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r4_cited_by", null,
                out var states, out _) is { } refused)
        {
            return refused;
        }

        // The two exact strings a state of this work carries and a reference can name it by.
        var targets = states
            .SelectMany(static state => new[] { state.PublisherLegalResourceIri, state.PublisherWorkIri })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var inbound = _reader!.ResolveCitationsTo(targets);
        var citingStates = _reader.ResolveStatesOfExpressions(
                inbound.Select(static edge => edge.ExpressionIri).Distinct(StringComparer.Ordinal).ToArray())
            .ToDictionary(static state => state.ExpressionIri, StringComparer.Ordinal);
        var workKey = states[0].WorkKey;
        var edges = inbound
            .Select(edge => (State: citingStates.TryGetValue(edge.ExpressionIri, out var state)
                    ? state
                    : throw new InvalidDataException("An article of the index belongs to no state."),
                Edge: edge))
            .OrderBy(static entry => entry.State.ApplicabilityDate, StringComparer.Ordinal)
            .ThenBy(static entry => entry.State.WorkKey, StringComparer.Ordinal)
            .ThenBy(static entry => entry.State.Language, StringComparer.Ordinal)
            .ThenBy(static entry => entry.State.ExpressionIri, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Edge.PublisherId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Edge.ArticleIdentitySha256, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Edge.Ordinal)
            .ToArray();

        static string Cursor((LuxembourgIndexResolvedState State, LuxembourgIndexInboundCitation Edge) entry) =>
            $"{entry.State.StateSha256}.{entry.Edge.ArticleIdentitySha256}.{entry.Edge.Ordinal}";
        var start = 0;
        if (after is not null)
        {
            var index = Array.FindIndex(edges, entry => string.Equals(Cursor(entry), after, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'after' names no edge of this query; a cursor is the continue_after of the same query, scope and index.");
            }

            start = index + 1;
        }

        var page = edges.Skip(start).Take(limit).ToArray();
        var truncated = start + page.Length < edges.Length;
        var rows = page.Select(entry => new
        {
            citing_work_key = entry.State.WorkKey,
            citing_language = entry.State.Language,
            citing_applicability_date = entry.State.ApplicabilityDate,
            citing_state_sha256 = entry.State.StateSha256,
            citing_permalink = StateUrl(entry.State),
            citing_stable_coordinate = StableCoordinate(entry.State),
            article_identity_sha256 = entry.Edge.ArticleIdentitySha256,
            article_publisher_id = entry.Edge.PublisherId,
            ordinal = entry.Edge.Ordinal,
            in_note = entry.Edge.InNote,
            label = entry.Edge.Label,
            href = entry.Edge.Href,
            target_iri = entry.Edge.ToRef,
            is_self_reference = string.Equals(entry.State.WorkKey, workKey, StringComparison.Ordinal),
        }).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = CitedByScope,
            requested_identifier = identifier,
            requested_after = after,
            publisher = "lu-legilux",
            work_key = workKey,
            relationship_type_assessed = false,
            current_legal_effect_assessed = false,
            derived = true,
            target_iris = targets,
            edge_count = edges.Length,
            edge_counts = new
            {
                in_text = edges.Count(static entry => !entry.Edge.InNote),
                in_note = edges.Count(static entry => entry.Edge.InNote),
            },
            citing_works = edges
                .Select(static entry => entry.State.WorkKey)
                .Where(citing => !string.Equals(citing, workKey, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            edge_order = CitedByOrder,
            limit,
            truncated,
            continue_after = truncated ? Cursor(page[^1]) : null,
            page_is = CitedByPageIs,
            edges = rows,
            not_held = CitedByNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "relation_edge", result.RootElement));
    }

    /// <summary>
    /// The SHA-256 of a state's article identities in sorted order, each as UTF-8 preceded by its length as
    /// four bytes big-endian, the encoding the state digest uses for the same identities, so a caller holding
    /// the list (as_of carries it) can check it against this and finish the recomputation the derivation names.
    /// </summary>
    private static string ArticleIdentitiesSha256(IEnumerable<string> identities)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var identity in identities.Order(StringComparer.Ordinal))
        {
            var bytes = Encoding.UTF8.GetBytes(identity);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// One source of a state: what the index recorded for the corpus member (outcome, rights, gaps) and,
    /// from the verified corpus manifest, the digest and length of the publisher bytes it retained for it
    /// and the digest of its receipt. Null where the corpus holds none; nothing is filled in.
    /// </summary>
    private object SourceRow(LuxembourgIndexStateSource source)
    {
        var member = _corpus.Set.Members.FirstOrDefault(candidate =>
            candidate.Publisher == PublisherId.LuLegilux &&
            string.Equals(candidate.ObjectRefSha256, source.ObjectRefSha256, StringComparison.Ordinal));
        return new
        {
            object_ref_sha256 = source.ObjectRefSha256,
            body_sha256 = member?.BodySha256,
            body_byte_length = member?.BodyByteLength,
            body_receipt_sha256 = member?.BodyReceiptSha256,
            outcome = source.Outcome,
            rights_disposition = source.RightsDisposition,
            gaps = source.Gaps,
            article_outcomes = source.ArticleOutcomes
                .Select(static row => new { disposition = row.Key, outcomes = row.Value })
                .ToArray(),
        };
    }

    /// <summary>
    /// The one <c>ambiguous_version</c> refusal for every operation that resolves a date to a state: the
    /// requested date, the bound it belongs to when the operation has more than one (<c>diff</c>), and the
    /// candidate permalinks in ordinal order. <c>as_of</c> passes no bound and its payload has none. The
    /// envelope serialises every object's properties in ordinal order, so the order they are added in here
    /// is not what a reader sees.
    /// </summary>
    private V3PlatformOperationOutcome RefuseAmbiguousVersion(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        string requestedDate,
        IReadOnlyList<string> candidates,
        string? bound)
    {
        var payload = new JsonObject { ["requested_date"] = requestedDate };
        if (bound is not null)
        {
            payload["bound"] = bound;
        }

        payload["candidates"] = new JsonArray(candidates.Select(static candidate => (JsonNode?)JsonValue.Create(candidate)).ToArray());
        using var document = JsonSerializer.SerializeToDocument(payload);
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, "ambiguous_version", document.RootElement));
    }

    /// <summary>
    /// The one <c>no_version_for_date</c> refusal: the requested date, its bound when the operation has more
    /// than one, the date the history of the states it is given begins, and their nearest later publisher
    /// date. <c>as_of</c> gives every served language's states, since it refuses only when none answered;
    /// <c>diff</c> gives the states of the one language that misses the bound, so the history named is that
    /// language's. There is never a nearest earlier date, since a state at or before the date would have
    /// answered.
    /// </summary>
    private V3PlatformOperationOutcome RefuseNoVersionForDate(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        IReadOnlyList<LuxembourgIndexResolvedState> scope,
        string requestedDate,
        string? bound) =>
        RefuseNoVersionForDate(request, observedAt, scope.Select(static state => state.ApplicabilityDate).ToArray(), requestedDate, bound);

    /// <summary>The same refusal over the publisher dates of the states it is given (<c>as_observed</c> gives a snapshot's).</summary>
    private V3PlatformOperationOutcome RefuseNoVersionForDate(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        IReadOnlyList<string> scopeDates,
        string requestedDate,
        string? bound)
    {
        var dates = scopeDates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var payload = new JsonObject { ["requested_date"] = requestedDate };
        if (bound is not null)
        {
            payload["bound"] = bound;
        }

        payload["history_begins"] = dates[0];
        payload["nearest_earlier"] = null;
        payload["nearest_later"] = dates.FirstOrDefault(date => string.CompareOrdinal(date, requestedDate) > 0);
        payload["what_would_answer"] = new JsonArray(NoVersionForDateRoutes.Select(static route => (JsonNode)route).ToArray());
        payload["asserts_absence_of_law"] = false;
        using var document = JsonSerializer.SerializeToDocument(payload);
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, "no_version_for_date", document.RootElement));
    }

    /// <summary>
    /// The selection rule of every dated answer, over the states of one language: the greatest
    /// publisher date at or before the requested date selects; every state on that date is returned
    /// (more than one is an ambiguity the caller refuses); the next publisher date after the requested
    /// date, or <c>null</c>, bounds it. Nothing else is derived.
    /// </summary>
    private static (LuxembourgIndexResolvedState[] Selected, string? NextDate) SelectAtDate(
        IReadOnlyList<LuxembourgIndexResolvedState> ofLanguage, string requestedDate)
    {
        var atOrBefore = ofLanguage
            .Where(state => string.CompareOrdinal(state.ApplicabilityDate, requestedDate) <= 0)
            .ToArray();
        if (atOrBefore.Length == 0)
        {
            return ([], null);
        }

        var selectedDate = atOrBefore.Select(static state => state.ApplicabilityDate).Max(StringComparer.Ordinal)!;
        var selected = atOrBefore
            .Where(state => string.Equals(state.ApplicabilityDate, selectedDate, StringComparison.Ordinal))
            .ToArray();
        var nextDate = ofLanguage
            .Where(state => string.CompareOrdinal(state.ApplicabilityDate, requestedDate) > 0)
            .Select(static state => state.ApplicabilityDate)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        return (selected, nextDate);
    }

    /// <summary>
    /// R6 <c>diff</c> for Luxembourg, at article level: each bound resolves to a state exactly as
    /// <c>as_of</c> resolves it, per language; both states are served in full before anything is
    /// compared; one state covering both bounds answers "the same version applied on both dates"; two
    /// states with the same rule profiles are compared article by article by publisher-minted id and
    /// wording digest (unchanged, changed, added, removed). Two states with different rule profiles
    /// refuse <c>profiles_differ</c>, which nothing overrides. Refusals follow <c>as_of</c>'s rule: an
    /// ambiguity or a profile mismatch in any served language refuses the whole answer, the first found in
    /// language order when both apply, while a language with no state at a bound is listed as not
    /// compared; both bounds are resolved before a language is classified, so a missing bound never masks
    /// an ambiguity on the other. No text is diffed, no legal effect is
    /// asserted: a changed article is a changed digest, and the note says so.
    /// </summary>
    public V3PlatformOperationOutcome Diff(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "diff", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus comparison operation only accepts diff/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var dateFrom = RequiredString(request.Parameters, "date_from");
        var dateTo = RequiredString(request.Parameters, "date_to");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        foreach (var date in new[] { dateFrom, dateTo })
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "A requested date is not a civil calendar date.");
            }
        }

        // An EU act whose census the EU index holds is compared by the EU time view (V3CorpusMount.EuropeDiff.cs), as as_of
        // answers it; an EU identifier it does not hold is unknown there.
        if (LocateEuropeSeedForTime(request, identifier, observedAt, out var europeSeed) is { } refusedEurope)
        {
            return refusedEurope;
        }

        if (europeSeed is not null)
        {
            return DiffEurope(request, identifier, europeSeed, dateFrom, dateTo, requestedLanguage, observedAt);
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_diff", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
        var servedLanguages = requestedLanguage is null ? availableLanguages : new[] { requestedLanguage };

        var comparisons = new List<object>();
        var notCompared = new List<object>();
        (string Bound, string Date, string[] Candidates)? ambiguous = null;
        (string Language, LuxembourgIndexResolvedState From, LuxembourgIndexResolvedState To)? profilesDiffer = null;
        string? firstRefusal = null;
        (string Bound, string Date, IReadOnlyList<LuxembourgIndexResolvedState> OfLanguage)? missing = null;
        foreach (var language in servedLanguages)
        {
            var ofLanguage = scope
                .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                .ToArray();
            var (fromSelected, fromNext) = SelectAtDate(ofLanguage, dateFrom);
            var (toSelected, toNext) = SelectAtDate(ofLanguage, dateTo);

            // Both bounds are resolved before the language is classified: an ambiguity at either bound is
            // recorded first, so a bound with no state cannot mask a twin on the other; only then does a
            // missing bound list the language as not compared.
            if (fromSelected.Length > 1 || toSelected.Length > 1)
            {
                var bound = fromSelected.Length > 1 ? "from" : "to";
                ambiguous ??= (bound, bound == "from" ? dateFrom : dateTo,
                    (bound == "from" ? fromSelected : toSelected).Select(StateUrl).Order(StringComparer.Ordinal).ToArray());
                firstRefusal ??= "ambiguous_version";
                continue;
            }

            var failing = fromSelected.Length == 0 ? "from" : toSelected.Length == 0 ? "to" : null;
            if (failing is not null)
            {
                missing ??= (failing, failing == "from" ? dateFrom : dateTo, ofLanguage);
                notCompared.Add(new { language, bound = failing, reason = "no state at or before the date" });
                continue;
            }

            var from = fromSelected[0];
            var to = toSelected[0];
            if (string.Equals(from.StateSha256, to.StateSha256, StringComparison.Ordinal))
            {
                comparisons.Add(new
                {
                    language,
                    from = StateRow(from, fromNext),
                    to = StateRow(to, toNext),
                    same_state = true,
                    note = "the same version applied on both dates",
                    articles = (object?)null,
                    counts = (object?)null,
                });
                continue;
            }

            if (!from.RuleProfileSha256s.SequenceEqual(to.RuleProfileSha256s, StringComparer.Ordinal))
            {
                profilesDiffer ??= (language, from, to);
                firstRefusal ??= "profiles_differ";
                continue;
            }

            var (articles, counts, _) = CompareArticles(
                _reader!.ResolveStateArticles(from.StateSha256), _reader.ResolveStateArticles(to.StateSha256));
            comparisons.Add(new
            {
                language,
                from = StateRow(from, fromNext),
                to = StateRow(to, toNext),
                same_state = false,
                note = "two publisher-dated states; each article is compared by its publisher-minted id and wording digest, and nothing about legal effect is asserted",
                articles = (object?)articles,
                counts = (object?)counts,
            });
        }

        // One rule for refusals, the one as_of follows: an ambiguity or a profile mismatch in any served
        // language refuses the whole answer; a language with no state at a bound is not a refusal and is
        // listed as not compared. When both apply, the first found in language order is the answer.
        if (profilesDiffer is { } mismatch && firstRefusal == "profiles_differ")
        {
            using var differ = JsonSerializer.SerializeToDocument(new
            {
                left_profile = mismatch.From.RuleProfileSha256s,
                right_profile = mismatch.To.RuleProfileSha256s,
                left = StateUrl(mismatch.From),
                right = StateUrl(mismatch.To),
                language = mismatch.Language,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "profiles_differ", differ.RootElement));
        }

        if (ambiguous is { } ambiguity)
        {
            return RefuseAmbiguousVersion(request, observedAt, ambiguity.Date, ambiguity.Candidates, ambiguity.Bound);
        }

        if (comparisons.Count == 0)
        {
            // The refusal speaks for the language that misses the bound: its own history, not the whole
            // scope's. Another served language can hold a state on or before the very date refused here
            // (it fails at its other bound), and its dates would make this payload contradict itself.
            var (bound, date, ofMissingLanguage) = missing!.Value;
            return RefuseNoVersionForDate(request, observedAt, ofMissingLanguage, date, bound);
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            requested_date_from = dateFrom,
            requested_date_to = dateTo,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            comparisons,
            languages_not_compared = notCompared,
            articles_not_admitted_note = ArticlesNotAdmittedNote,
            available_languages = availableLanguages,
            wording_rule = WordingRule,
            validity_conflict_rule = ValidityConflictRule,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "diff", result.RootElement));
    }

    /// <summary>
    /// Article-level comparison of two states by publisher-minted id: present in both with the same
    /// wording digests is unchanged, with different digests changed; only in the later state added;
    /// only in the earlier removed. An id minted twice in one state compares as its ordered digests.
    /// </summary>
    private static (IReadOnlyList<object> Articles, object Counts, bool Differs) CompareArticles(
        IReadOnlyList<LuxembourgIndexStateArticle> from, IReadOnlyList<LuxembourgIndexStateArticle> to)
    {
        static Dictionary<string, LuxembourgIndexStateArticle[]> ById(IReadOnlyList<LuxembourgIndexStateArticle> articles) =>
            articles.GroupBy(static article => article.PublisherId, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        static object[] Side(LuxembourgIndexStateArticle[]? side) =>
            side is null
                ? []
                : side.Select(static article => (object)new { article_identity_sha256 = article.ArticleIdentitySha256, wording_sha256 = article.WordingSha256 }).ToArray();

        var left = ById(from);
        var right = ById(to);
        var ids = left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var rows = new List<object>();
        int unchanged = 0, changed = 0, added = 0, removed = 0;
        foreach (var id in ids)
        {
            left.TryGetValue(id, out var before);
            right.TryGetValue(id, out var after);
            string status;
            if (before is null)
            {
                status = "added"; added++;
            }
            else if (after is null)
            {
                status = "removed"; removed++;
            }
            else if (before.Select(static a => a.WordingSha256).SequenceEqual(after.Select(static a => a.WordingSha256), StringComparer.Ordinal))
            {
                status = "unchanged"; unchanged++;
            }
            else
            {
                status = "changed"; changed++;
            }

            rows.Add(new { publisher_id = id, status, from = Side(before), to = Side(after) });
        }

        return (rows, new { unchanged, changed, added, removed }, changed + added + removed > 0);
    }

    /// <summary>The ceiling on the rows one <c>in_force_on</c> answer carries.</summary>
    public const int InForceOnMaxRows = 200;

    internal const string InForceOnCaveat =
        "a row is the text the publisher dates as applicable on the requested date; nothing is said about " +
        "legal status, repeal or commencement";

    /// <summary>
    /// R6 <c>in_force_on</c> for Luxembourg: <c>as_of</c> across the mounted works. For every work
    /// and language with a publisher-dated state at or before the requested date, the state on the
    /// greatest such date, selected by <c>SelectAtDate</c>, the one rule <c>as_of</c>, <c>timeline</c>
    /// and <c>diff</c> follow. Rows are in work key and language order and name their state compactly,
    /// with <c>resolve</c> and its hash-pinned permalink for the state in full.
    /// <para>
    /// The name says more than the index can. The mounted index holds publisher applicability dates
    /// and nothing about repeal, <c>dateNoLongerInForce</c> or entry into force, so a repealed work
    /// with no later consolidated state still has an applicable state on every later date. The
    /// operation therefore serves no status, and no served string speaks of legal force except the
    /// operation's own name where the contract fixes it: the operation id, the route, and the mode
    /// tag <c>r6_in_force_on</c> that a <c>retrieval_mode_unavailable</c> refusal echoes, which
    /// mirrors the id as every R6 mode tag does. Every answer carries the fixed caveat and a
    /// <c>derivation</c> block naming the rule, the basis "versioned works only", what bounds a row's
    /// interval (the publisher's next dated state in that language, or nothing; no end is invented)
    /// and what was not consulted.
    /// </para>
    /// <para>
    /// A work and language with several states on the selected date is a row that says
    /// <c>ambiguous_version</c> with every candidate and names none; one ambiguous work must not hide
    /// every other, as in <c>changes_in_period</c>; such a row offers no <c>resolve</c>, since there
    /// is no one state to point at. Without a work named, a date before everything held is an answer
    /// with no rows and a population that says so, because "nothing held" answers a question about the
    /// whole index. With a work named the question is <c>as_of</c>'s, what applied to one work on
    /// one date, and the two must not disagree about whether that has an answer: a date before the
    /// work's history is <c>as_of</c>'s <c>no_version_for_date</c>, and several states on the
    /// selected date in any served language is <c>as_of</c>'s <c>ambiguous_version</c>, both through
    /// the shared builders. The per-row reason is for the question about many works only. Rows are bounded and the bound is said: they are cut only
    /// between works, <c>continue_after</c> is the last work key served and is the next request's
    /// <c>after_work_key</c>, and a work with more rows than the limit is served whole and said.
    /// </para>
    /// </summary>
    public V3PlatformOperationOutcome InForceOn(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "in_force_on", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus selection operation only accepts in_force_on/1.");
        }

        var requestedDate = RequiredString(request.Parameters, "date");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        var identifier = request.Parameters.TryGetProperty("identifier", out var identifierValue) &&
            identifierValue.ValueKind == JsonValueKind.String
                ? RequiredString(request.Parameters, "identifier")
                : null;
        var afterWorkKey = request.Parameters.TryGetProperty("after_work_key", out var afterValue) &&
            afterValue.ValueKind == JsonValueKind.String
                ? RequiredString(request.Parameters, "after_work_key")
                : null;
        var limit = InForceOnMaxRows;
        if (request.Parameters.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
                limit < 1 || limit > InForceOnMaxRows)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'limit' is not a whole number of rows within the ceiling.");
            }
        }

        if (!DateOnly.TryParseExact(requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        var statesByWork = new Dictionary<string, IReadOnlyList<LuxembourgIndexResolvedState>>(StringComparer.Ordinal);
        IReadOnlyList<string> workKeys;
        string[] languagesHeld;
        long worksHeld;
        long worksInScope;
        long worksOnDate;
        string? firstHeld;
        string? lastHeld;
        if (identifier is not null)
        {
            if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_in_force_on", requestedLanguage,
                    out var states, out var availableLanguages) is { } refused)
            {
                return refused;
            }

            var scoped = states
                .Where(state => requestedLanguage is null || string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
            var heldDates = scoped.Select(static state => state.ApplicabilityDate).Order(StringComparer.Ordinal).ToArray();
            statesByWork[states[0].WorkKey] = states;
            languagesHeld = availableLanguages;
            worksHeld = 1;
            firstHeld = heldDates[0];
            lastHeld = heldDates[^1];
            var applies = string.CompareOrdinal(firstHeld, requestedDate) <= 0;
            if (!applies)
            {
                // One work, one date: as_of refuses this with no_version_for_date, and two operations
                // that answer "what applied to this work on this date" must not disagree about whether
                // that has an answer. Without a work named the question is about the whole index, and
                // "nothing held" is its answer.
                return RefuseNoVersionForDate(request, observedAt, scoped, requestedDate, bound: null);
            }

            // The same holds for twins. as_of refuses the whole question when any served language has
            // several states on the selected date, with every candidate of every such language in
            // ordinal order; a named work here is that question, so it is that refusal. The per-row
            // reason below is for the question about many works, where one must not hide the others.
            var twins = scoped
                .GroupBy(static state => state.Language, StringComparer.Ordinal)
                .Select(ofLanguage => SelectAtDate(ofLanguage.ToArray(), requestedDate).Selected)
                .Where(static selected => selected.Length > 1)
                .SelectMany(static selected => selected.Select(StateUrl))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (twins.Length != 0)
            {
                return RefuseAmbiguousVersion(request, observedAt, requestedDate, twins, bound: null);
            }

            worksOnDate = 1;
            workKeys = afterWorkKey is null || string.CompareOrdinal(states[0].WorkKey, afterWorkKey) > 0
                ? [states[0].WorkKey]
                : [];
            worksInScope = 1;
        }
        else
        {
            if (_reader is null)
            {
                // The selection reads the Luxembourg index. Without it there is nothing to select from,
                // whatever else is mounted; the refusal says which corpus is required.
                using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.LuLegilux),
                    new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
            }

            languagesHeld = _reader.ResolveStatePopulation().Languages.ToArray();
            if (requestedLanguage is not null && !languagesHeld.Contains(requestedLanguage, StringComparer.Ordinal))
            {
                using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
                {
                    requested_language = requestedLanguage,
                    available_languages = languagesHeld,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
            }

            var population = _reader.ResolveStatePopulation(requestedLanguage);
            worksHeld = requestedLanguage is null ? population.Works : _reader.ResolveStatePopulation().Works;
            worksInScope = population.Works;
            firstHeld = population.FirstDate;
            lastHeld = population.LastDate;
            worksOnDate = _reader.CountWorksWithStateOnOrBefore(requestedDate, requestedLanguage);
            // Every work contributes at least one row, so one work more than the limit is enough to
            // know whether anything remains after the last work served.
            workKeys = _reader.ResolveWorkKeysWithStateOnOrBefore(requestedDate, requestedLanguage, afterWorkKey, limit + 1);
        }

        var rows = new List<object>();
        string? continueAfter = null;
        string? lastServed = null;
        foreach (var workKey in workKeys)
        {
            if (!statesByWork.TryGetValue(workKey, out var ofWork))
            {
                ofWork = _reader!.ResolveWorkStates(workKey);
                statesByWork[workKey] = ofWork;
            }

            var ofThisWork = new List<object>();
            var languages = ofWork.Select(static state => state.Language)
                .Where(language => requestedLanguage is null || string.Equals(language, requestedLanguage, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (var language in languages)
            {
                var ofLanguage = ofWork
                    .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                    .ToArray();
                var (selected, nextDate) = SelectAtDate(ofLanguage, requestedDate);
                if (selected.Length == 0)
                {
                    continue;
                }

                var single = selected.Length == 1 ? selected[0] : null;
                ofThisWork.Add(new
                {
                    work_key = workKey,
                    publisher_work_iri = selected[0].PublisherWorkIri,
                    language,
                    interval = new { from = selected[0].ApplicabilityDate, until = nextDate },
                    state = single is null ? null : StateReference(single, nextDate),
                    reason = single is null ? "ambiguous_version" : null,
                    candidates = single is null ? selected.Select(StateUrl).Order(StringComparer.Ordinal).ToArray() : null,
                    // The hash-pinned permalink names this state and no other, so resolve serves it in full.
                    resolve = single is null ? null : new { identifier = StateUrl(single) },
                });
            }

            // Rows are cut only between works: a work's languages are served together or not at all.
            if (rows.Count > 0 && rows.Count + ofThisWork.Count > limit)
            {
                continueAfter = lastServed;
                break;
            }

            rows.AddRange(ofThisWork);
            lastServed = workKey;
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_date = requestedDate,
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            requested_after_work_key = afterWorkKey,
            publisher = "lu-legilux",
            caveat = InForceOnCaveat,
            derivation = new
            {
                rule = "per work and language, the state on the greatest publisher applicability date at or before the requested date",
                basis = "versioned works only",
                interval_until = "the publisher's next dated state in that language, or null; no end date is published or invented",
                not_consulted = new[] { "repeal", "end of validity", "commencement" },
            },
            population = new
            {
                // The population is the scope asked for: the one work when an identifier is given, the
                // one language when a language is given, the whole mounted index otherwise.
                scope = new { identifier, language = requestedLanguage },
                // Two kinds of absence are counted apart: a work that holds the language asked for and
                // begins after the date, and a work that holds nothing in that language at all.
                works_held = worksHeld,
                works_holding_the_language = requestedLanguage is null ? (long?)null : worksInScope,
                works_without_the_language = requestedLanguage is null ? (long?)null : worksHeld - worksInScope,
                works_with_a_state_on_date = worksOnDate,
                works_beginning_later = worksInScope - worksOnDate,
                first_date_held = firstHeld,
                last_date_held = lastHeld,
                languages_held = languagesHeld,
                date_is_before_everything_held = firstHeld is null || string.CompareOrdinal(requestedDate, firstHeld) < 0,
            },
            limit,
            truncated = continueAfter is not null,
            continue_after = continueAfter,
            // A work is never cut: when one work alone holds more rows than the limit it is served
            // whole, and this says the limit was exceeded for that reason.
            whole_work_over_limit = rows.Count > limit,
            states = rows,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "version_state", result.RootElement));
    }

    /// <summary>The ceiling on the hits one <c>search</c> answer carries.</summary>
    public const int SearchMaxHits = 200;

    internal const string SearchRanking =
        "none; strict lane before relaxed lane, then work key, publisher date, article identity and state digest. " +
        "This index holds no BM25 ranker, so a page is the first hits in this order and not the best hits";

    /// <summary>The ceiling on the characters of a <c>search</c> query; the request document says the same.</summary>
    public const int SearchMaxQueryCharacters = 512;

    /// <summary>
    /// The ceiling on the distinct terms of a <c>search</c> query. Each term is one <c>instr</c> clause
    /// in an AND chain, and SQLite refuses an expression tree deeper than a thousand, so a query is
    /// bounded here and answered as a request-schema rejection rather than reaching it.
    /// </summary>
    public const int SearchMaxTerms = 32;

    internal const string SearchMatching =
        "byte-exact substring of the article's searchable text, which is its text and reference tokens as the index holds them " +
        "(modification markers and note references are not searched); case and diacritics are significant; " +
        "nothing is folded, stemmed or expanded";

    internal const string SearchLanes =
        "strict: the query as typed is a substring; relaxed: every distinct whitespace-separated term is a substring, in any order, " +
        "so the relaxed set contains the strict set. With no mode the answer is the strict hits and then only the relaxed hits strict did not serve, " +
        "each article once; with a mode it is that lane's whole set. A lane not asked for is null in the population, not zero";

    internal const string SearchPageIs = "the first hits in the stated order, not the best hits";

    /// <summary>
    /// The owner's rights ruling (decided by the sole driver under the owner's delegation of 2026-10-02, on the question PR #842
    /// surfaced): a licence that does not admit a text keeps it out of search matching too, since a hit says which articles
    /// hold a word.
    /// </summary>
    internal const string SearchRightsRule =
        "a state is searched only when its text is one evidence_bundle would quote: every source acquired and admitted by its rights; the " +
        "text of any other state is not matched, so no hit and no count says which of its articles hold a word, and nothing about that " +
        "text is stated here";

    internal const string SearchHitUnit =
        "a hit is one article of one held state, not a provision: a provision whose wording never changed is a hit in every state that holds it";

    private static readonly string[] SearchModes = ["strict", "relaxed"];

    /// <summary>
    /// <c>search</c> for Luxembourg, the two lexical lanes the mounted index can serve, resolver first.
    /// Unless a work is named, the query first runs through the title resolver <c>resolve</c> uses
    /// (lane R1): one work is a card ahead of every hit, several candidates are listed and none is
    /// picked. With a work named the resolver is not run and the answer says so
    /// (<c>not_run_identifier_given</c>). Then the strict lane,
    /// the query as typed as a substring of an article's text, and the relaxed lane, every
    /// whitespace-separated term as a substring in any order. The lanes are sets and the relaxed set
    /// contains the strict set. With no <c>mode</c> the answer is the strict hits and then only the
    /// relaxed hits strict did not serve, so an article matching both is served once, as strict, and
    /// every strict hit precedes every relaxed hit: relaxed never outranks strict. With a <c>mode</c>
    /// the answer is that lane's whole set, and the lane not asked for is neither scanned nor counted:
    /// it is null in the population.
    /// <para>
    /// The index holds no FTS table and no ranker. What is served is an order and not a rank, no score
    /// is served, and every answer says so, says that matching is byte-exact, and says that a page is
    /// the first hits in the stated order and not the best ones. A requested <c>mode</c> other than the
    /// two lanes is refused <c>retrieval_mode_unavailable</c> naming the modes held, never ignored. The
    /// query is bounded (<see cref="SearchMaxQueryCharacters"/> characters, <see cref="SearchMaxTerms"/>
    /// distinct terms) and a longer one is a request-schema rejection. No
    /// snippet is served: a hit names its article and its state with the hash-pinned permalink, and
    /// the text is read there.
    /// </para>
    /// <para>
    /// A hit is one article of one held state. Without a date every held state is searched and each
    /// hit says its date, and the population counts distinct publisher articles (by work key and the
    /// publisher's own article id, since an article identity is minted per expression) beside hits. With a date, only
    /// the state <c>SelectAtDate</c> picks per work contributes. Across works, a work with several
    /// states on the selected date contributes no hits and is listed as ambiguous, since one ambiguous
    /// work must not hide the others. With a work named the question is <c>as_of</c>'s, and so are its
    /// refusals, through the shared builders: <c>no_version_for_date</c> and <c>ambiguous_version</c>.
    /// Zero hits is an answer. Hits are bounded and the bound is said; <c>continue_after</c> is the
    /// last hit served and is the next request's <c>after</c>.
    /// </para>
    /// </summary>
    public V3PlatformOperationOutcome Search(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "search", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus search operation only accepts search/1.");
        }

        var query = RequiredString(request.Parameters, "query");
        var language = RequiredString(request.Parameters, "language");
        string? Optional(string name) =>
            request.Parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? RequiredString(request.Parameters, name)
                : null;
        // The bounds are shape, judged before any data is read. The request document counts characters as
        // code points, so this does too.
        if (query.EnumerateRunes().Count() > SearchMaxQueryCharacters)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'query' is longer than the ceiling.");
        }

        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length > SearchMaxTerms)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'query' has more distinct terms than the ceiling.");
        }

        var requestedDate = Optional("date");
        var identifier = Optional("identifier");
        var mode = Optional("mode");
        var after = Optional("after");
        var limit = SearchMaxHits;
        if (request.Parameters.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
                limit < 1 || limit > SearchMaxHits)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'limit' is not a whole number of hits within the ceiling.");
            }
        }

        if (requestedDate is not null &&
            !DateOnly.TryParseExact(requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The requested date is not a civil calendar date.");
        }

        if (identifier is not null &&
            RouteEuropeSearch(request, identifier, language, query, terms, requestedDate, mode, after, limit, observedAt) is { } europe)
        {
            return europe;
        }

        var statesByWork = new Dictionary<string, IReadOnlyList<LuxembourgIndexResolvedState>>(StringComparer.Ordinal);
        string? workKey = null;
        if (identifier is not null)
        {
            if (RefuseUnlessWorkStates(request, identifier, observedAt, "r2_provision_discovery", language,
                    out var states, out _) is { } refused)
            {
                return refused;
            }

            workKey = states[0].WorkKey;
            statesByWork[workKey] = states;
            if (requestedDate is not null)
            {
                // One work and one date is as_of's question, and these are as_of's refusals through
                // the shared builders: the two must not disagree about whether it has an answer.
                var scoped = states
                    .Where(state => string.Equals(state.Language, language, StringComparison.Ordinal))
                    .ToArray();
                var selected = SelectAtDate(scoped, requestedDate).Selected;
                if (selected.Length == 0)
                {
                    return RefuseNoVersionForDate(request, observedAt, scoped, requestedDate, bound: null);
                }

                if (selected.Length > 1)
                {
                    return RefuseAmbiguousVersion(request, observedAt, requestedDate,
                        selected.Select(StateUrl).Order(StringComparer.Ordinal).ToArray(), bound: null);
                }
            }
        }
        else
        {
            if (_reader is null)
            {
                using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.LuLegilux),
                    new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
            }

            var languagesHeld = _reader.ResolveStatePopulation().Languages.ToArray();
            if (!languagesHeld.Contains(language, StringComparer.Ordinal))
            {
                using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
                {
                    requested_language = language,
                    available_languages = languagesHeld,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
            }
        }

        if (mode is not null && !SearchModes.Contains(mode, StringComparer.Ordinal))
        {
            // A mode this index cannot serve is refused, never ignored: serving the two lanes to a
            // caller who asked for a ranked or semantic mode would be an order they take for another.
            using var unavailableMode = JsonSerializer.SerializeToDocument(new
            {
                requested_mode = mode,
                available_modes = SearchModes,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "retrieval_mode_unavailable", unavailableMode.RootElement));
        }

        // Resolver first (lane R1): the title ladder resolve uses. A plural match lists its candidates
        // and picks none. With a work named the ladder is not run: discovery answers "which work do you
        // mean", the caller has said, and a card for whatever work the query happens to be the title of
        // would sit above hits from the work they named and be read as the work in view.
        // Both paths above have established that a Luxembourg index is mounted; this says so to the
        // compiler, which the unconditional call it replaces used to.
        ArgumentNullException.ThrowIfNull(_reader);
        var titles = identifier is null ? _reader.ResolveWorkTitle(query) : null;
        var titleOutcome = titles is null ? "not_run_identifier_given"
            : !titles.Available ? "no_titles_held"
            : titles.Candidates.Count == 0 ? "no_title_match"
            : titles.Candidates.Count == 1 ? "one_work"
            : "several_candidates";
        static object Card(LuxembourgIndexResolvedWork work) => new
        {
            work_identifier = work.WorkIdentifier,
            expressions = work.ExpressionIris,
            languages = work.Languages,
            matched_title = work.MatchedTitle,
            matched_title_language = work.MatchedTitleLanguage,
            match_reason = work.MatchReason,
        };

        // The lanes are sets and the relaxed set contains the strict set, so a lane is scanned only when
        // it is asked for. With no mode the relaxed lane adds only what strict did not serve; when the
        // query is one word exactly as typed that is nothing, and the second scan is not paid.
        var wantStrict = mode is null || string.Equals(mode, "strict", StringComparison.Ordinal);
        var wantRelaxed = mode is null || string.Equals(mode, "relaxed", StringComparison.Ordinal);
        var relaxedIsStrict = terms.Length == 1 && string.Equals(terms[0], query, StringComparison.Ordinal);
        var strictFound = wantStrict ? _reader.SearchStateArticles(language, [query], workKey) : null;
        var relaxedFound = wantRelaxed && (mode is not null || !relaxedIsStrict)
            ? _reader.SearchStateArticles(language, terms, workKey)
            : null;
        // The index either holds searchable text for the language or it does not; the lane scanned first says.
        var measured = wantStrict ? strictFound is not null : relaxedFound is not null;
        IReadOnlyList<LuxembourgIndexSearchHit> strict = wantStrict ? strictFound ?? [] : [];
        var strictKeys = strict.Select(static hit => (hit.StateSha256, hit.ArticleIdentitySha256)).ToHashSet();
        IReadOnlyList<LuxembourgIndexSearchHit> relaxed = !wantRelaxed
            ? []
            : mode is null
                // Every strict hit is also a relaxed match; it is served once, as strict.
                ? (relaxedFound ?? []).Where(hit => !strictKeys.Contains((hit.StateSha256, hit.ArticleIdentitySha256))).ToArray()
                : relaxedFound ?? [];

        // Rights before any hit is served (SearchRightsRule): a state whose text its rights did not admit is not matched, so
        // no answer says which of its articles hold a word. The rule is evidence_bundle's, applied per state.
        var admittedByState = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Admitted(LuxembourgIndexSearchHit hit)
        {
            if (!admittedByState.TryGetValue(hit.StateSha256, out var admitted))
            {
                var sources = _reader.ResolveStateSources(hit.StateSha256);
                admitted = sources.Count > 0 && sources.All(source =>
                    string.Equals(source.Outcome, AcquiredOutcomeToken, StringComparison.Ordinal) &&
                    string.Equals(source.RightsDisposition, EvidenceBundleAdmittingRightsDisposition, StringComparison.Ordinal));
                admittedByState[hit.StateSha256] = admitted;
            }

            return admitted;
        }

        strict = strict.Where(Admitted).ToArray();
        relaxed = relaxed.Where(Admitted).ToArray();

        var ambiguousWorks = new List<object>();
        if (requestedDate is not null)
        {
            var selectedByWork = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var key in strict.Concat(relaxed).Select(static hit => hit.WorkKey)
                         .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!statesByWork.TryGetValue(key, out var ofWork))
                {
                    ofWork = _reader.ResolveWorkStates(key);
                    statesByWork[key] = ofWork;
                }

                var selected = SelectAtDate(
                    ofWork.Where(state => string.Equals(state.Language, language, StringComparison.Ordinal)).ToArray(),
                    requestedDate).Selected;
                selectedByWork[key] = selected.Length == 1 ? selected[0].StateSha256 : null;
                if (selected.Length > 1)
                {
                    // One ambiguous work must not hide every other work's hits: it contributes none,
                    // names every candidate and picks none.
                    ambiguousWorks.Add(new
                    {
                        work_key = key,
                        reason = "ambiguous_version",
                        candidates = selected.Select(StateUrl).Order(StringComparer.Ordinal).ToArray(),
                    });
                }
            }

            bool Applies(LuxembourgIndexSearchHit hit) =>
                string.Equals(selectedByWork[hit.WorkKey], hit.StateSha256, StringComparison.Ordinal);
            strict = strict.Where(Applies).ToArray();
            relaxed = relaxed.Where(Applies).ToArray();
        }

        // Relaxed never outranks strict: every strict hit, then every relaxed hit.
        var all = strict.Select(static hit => (Lane: "strict", Hit: hit))
            .Concat(relaxed.Select(static hit => (Lane: "relaxed", Hit: hit)))
            .ToArray();
        static string Cursor((string Lane, LuxembourgIndexSearchHit Hit) entry) =>
            $"{entry.Lane}.{entry.Hit.StateSha256}.{entry.Hit.ArticleIdentitySha256}";
        var start = 0;
        if (after is not null)
        {
            var index = Array.FindIndex(all, entry => string.Equals(Cursor(entry), after, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'after' names no hit of this query; a cursor is the continue_after of the same query, scope and index.");
            }

            start = index + 1;
        }

        var page = all.Skip(start).Take(limit).ToArray();
        var truncated = start + page.Length < all.Length;
        var hits = page.Select(entry =>
        {
            if (!statesByWork.TryGetValue(entry.Hit.WorkKey, out var ofWork))
            {
                ofWork = _reader.ResolveWorkStates(entry.Hit.WorkKey);
                statesByWork[entry.Hit.WorkKey] = ofWork;
            }

            var state = ofWork.Single(candidate =>
                string.Equals(candidate.StateSha256, entry.Hit.StateSha256, StringComparison.Ordinal));
            return (object)new
            {
                lane = entry.Lane,
                match_reasons = new[] { entry.Lane == "strict" ? "exact_phrase" : "all_terms" },
                work_key = entry.Hit.WorkKey,
                publisher_work_iri = state.PublisherWorkIri,
                language,
                applicability_date = entry.Hit.ApplicabilityDate,
                article_identity_sha256 = entry.Hit.ArticleIdentitySha256,
                publisher_id = entry.Hit.PublisherId,
                publisher_wid = entry.Hit.PublisherWId,
                state_sha256 = entry.Hit.StateSha256,
                // The hash-pinned permalink names this state and no other; the text is read there.
                resolve = new { identifier = StateUrl(state) },
            };
        }).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_query = query,
            requested_language = language,
            requested_date = requestedDate,
            requested_identifier = identifier,
            requested_mode = mode,
            requested_after = after,
            publisher = "lu-legilux",
            ranking = SearchRanking,
            matching = SearchMatching,
            rights_rule = SearchRightsRule,
            hit_unit = SearchHitUnit,
            lanes = SearchLanes,
            searchable_text_held_for_language = measured,
            // The way out when the language asked for holds no searchable text: the languages that do,
            // as the capability manifest measured them.
            searchable_languages = _reader.SearchableLanguages(),
            modes_held = SearchModes,
            modes_not_held = new[] { "bm25", "semantic" },
            work_resolution = new
            {
                retrieval_lane = "r1_work_discovery",
                outcome = titleOutcome,
                work = titles is not null && titles.Candidates.Count == 1 ? Card(titles.Candidates[0]) : null,
                candidates = titles is not null && titles.Candidates.Count > 1 ? titles.Candidates.Select(Card).ToArray() : null,
            },
            terms,
            population = new
            {
                // The population is the scope asked for, over every hit and not the page.
                scope = new { identifier, language, date = requestedDate, mode },
                // A lane not asked for was not scanned: null says "not counted", never "counted and empty".
                strict_hits = wantStrict ? (int?)strict.Count : null,
                relaxed_hits = wantRelaxed ? (int?)relaxed.Count : null,
                // An article identity is minted per expression, so it cannot say that two hits are one
                // provision in two states. The publisher's own article id within one work can.
                distinct_publisher_articles = all.Select(static entry => (entry.Hit.WorkKey, entry.Hit.PublisherId)).Distinct().Count(),
                works_with_hits = all.Select(static entry => entry.Hit.WorkKey).Distinct(StringComparer.Ordinal).Count(),
            },
            ambiguous_works = ambiguousWorks,
            limit,
            truncated,
            continue_after = truncated ? Cursor(page[^1]) : null,
            // Said beside the bound, where a reader of a truncated page looks: there is no ranker, so
            // ten of five hundred hits are the first ten in the stated order and not the ten best.
            page_is = SearchPageIs,
            hits,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "quote", result.RootElement));
    }

    internal const string EuropeSearchScope =
        "one EU work named by the identifier (its CELEX, work or expression IRI, or one of its provisions), in the one held wording of its " +
        "expression in the language asked; a search across EU works is not served";

    internal const string EuropeSearchRanking =
        "none; strict lane before relaxed lane, then the publisher's article id and article identity. " +
        "This index holds no BM25 ranker, so a page is the first hits in this order and not the best hits";

    internal const string EuropeSearchMatching =
        "byte-exact substring of the article's searchable text as the EU index holds it; case and diacritics are significant; " +
        "nothing is folded, stemmed or expanded";

    internal const string EuropeSearchHitUnit =
        "a hit is one article of the one wording this index holds of the expression; no other wording of the act is held";

    internal const string EuropeWordingDateSemantics =
        "the wording date (wording_date in search, wording_dates in dossier) is the date the publisher's Formex package gives the act, " +
        "the date of the original wording this index holds; " +
        "it is not a publication, entry-into-force, application or consolidation date, and an EU date is never merged with a Luxembourg " +
        "applicability date";

    internal static readonly string[][] EuropeSearchNotHeld =
    [
        ["later_wordings", "no consolidated version is held, so a later wording of the act is not searched and a date is not answered here"],
        ["corrigenda_applied", "corrigenda are recorded by the index and not applied to the wording searched"],
        ["other_languages", "an expression in a language this index holds no wording in is not searched; French expressions are not acquired (Decision 89)"],
        ["article_text", "no snippet or article text is served; each hit names its provision, which resolve answers"],
    ];

    /// <summary>
    /// Where a named work is an EU work, <c>search</c> answers from the EU index (<see cref="SearchEurope"/>)
    /// or refuses with EU context; otherwise the Luxembourg path runs unchanged (null).
    /// </summary>
    private V3PlatformOperationOutcome? RouteEuropeSearch(
        V3PlatformOperationRequest request,
        string identifier,
        string language,
        string query,
        string[] terms,
        string? requestedDate,
        string? mode,
        string? after,
        int limit,
        DateTimeOffset observedAt)
    {
        if (LocateEuropeWork(request, identifier, observedAt, out var europe) is { } refused)
        {
            return refused;
        }

        return europe.Count > 0
            ? SearchEurope(request, identifier, europe, language, query, terms, requestedDate, mode, after, limit, observedAt)
            : null;
    }

    /// <summary>
    /// Decides which publisher's index answers an operation that serves both for a named work: returns a
    /// refusal, or null with <paramref name="europe"/> holding the EU index's expressions for the identifier
    /// (the EU path answers) or empty (the Luxembourg path answers, unchanged). An identifier both indexes
    /// hold is <c>ambiguous_identifier</c>, as <c>resolve</c> answers it: Luxembourg holds it if it names a
    /// work there or if <c>resolve</c>'s exact match finds it. An EU-shaped identifier the EU index does not
    /// hold is <c>no_corpus_mounted</c> for the EU when no EU index is mounted and <c>identifier_unknown</c>
    /// with EU context when one is.
    /// </summary>
    private V3PlatformOperationOutcome? LocateEuropeWork(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt,
        out IReadOnlyList<EuropeIndexResolvedExpression> europe)
    {
        europe = _europeReader?.ResolveExact(identifier) ?? Array.Empty<EuropeIndexResolvedExpression>();
        var luxembourg = _reader is null
            ? Array.Empty<LuxembourgIndexResolvedState>()
            : _reader.ResolveWorkStates(TryParseStableWorkCoordinate(identifier, out var workKey) ? workKey : identifier);
        var luxembourgExact = europe.Count > 0 && _reader is not null
            ? _reader.ResolveExact(identifier)
            : Array.Empty<LuxembourgIndexResolvedExpression>();
        if (europe.Count > 0 && (luxembourg.Count > 0 || luxembourgExact.Count > 0))
        {
            using var helpful = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                candidates = luxembourg.Select(static state => state.ExpressionIri)
                    .Concat(luxembourgExact.Select(static expression => expression.ExpressionIri))
                    .Concat(europe.Select(static expression => expression.PublisherExpressionId))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherFor(identifier)),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", helpful.RootElement));
        }

        if (europe.Count > 0 || luxembourg.Count > 0 || !IsEuropeanUnionShaped(identifier))
        {
            return null;
        }

        if (_europeReader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "eu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        return Unknown(request, identifier, observedAt, PublisherId.EuEurLex,
            "an EU work identifier (CELEX, work or expression IRI) or provision present in the mounted EU index");
    }

    /// <summary>
    /// EU <c>search</c> in one work (lane R2): the two lexical lanes of the Luxembourg search over the one
    /// held wording of the work's expression in the language asked, with its refusals. A language the work
    /// holds no expression in is <c>language_not_available</c>; two expressions in it are
    /// <c>ambiguous_identifier</c>, never one of them; a date is refused <c>retrieval_mode_unavailable</c>:
    /// the search reads the original wording only, and the wording that answers a date is <c>as_of</c>'s
    /// question, which the EU time view answers (V3CorpusMount.EuropeTime.cs); searching that wording at a
    /// date is not built yet; a mode other than the two lanes is refused as the Luxembourg search refuses it. Each hit names its
    /// article, the Formex act date of the wording (<c>wording_date</c>, with its meaning in the answer,
    /// never an applicability date), and the provision coordinate <c>resolve</c> answers; no text is served.
    /// </summary>
    private V3PlatformOperationOutcome SearchEurope(
        V3PlatformOperationRequest request,
        string identifier,
        IReadOnlyList<EuropeIndexResolvedExpression> resolved,
        string language,
        string query,
        string[] terms,
        string? requestedDate,
        string? mode,
        string? after,
        int limit,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(_europeReader);
        var languagesHeld = resolved.Select(static expression => expression.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var expressions = resolved
            .Where(expression => string.Equals(expression.Language, language, StringComparison.Ordinal))
            .Select(static expression => expression.PublisherExpressionId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (expressions.Length == 0)
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = language,
                available_languages = languagesHeld,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        if (expressions.Length > 1)
        {
            using var ambiguous = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                candidates = expressions,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
        }

        if (requestedDate is not null || (mode is not null && !SearchModes.Contains(mode, StringComparer.Ordinal)))
        {
            using var unavailableMode = JsonSerializer.SerializeToDocument(requestedDate is not null
                ? new { requested_mode = "r6_as_of", available_modes = new[] { "r2_provision_discovery" } }
                : new { requested_mode = mode!, available_modes = SearchModes });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "retrieval_mode_unavailable", unavailableMode.RootElement));
        }

        var expression = expressions[0];
        var wording = EuropeWordingOf(expression);
        var wantStrict = mode is null || string.Equals(mode, "strict", StringComparison.Ordinal);
        var wantRelaxed = mode is null || string.Equals(mode, "relaxed", StringComparison.Ordinal);
        var relaxedIsStrict = terms.Length == 1 && string.Equals(terms[0], query, StringComparison.Ordinal);
        var strictFound = wantStrict ? _europeReader.SearchExpressionArticles(language, [query], expression) : null;
        var relaxedFound = wantRelaxed && (mode is not null || !relaxedIsStrict)
            ? _europeReader.SearchExpressionArticles(language, terms, expression)
            : null;
        var measured = wantStrict ? strictFound is not null : relaxedFound is not null;
        IReadOnlyList<EuropeIndexSearchHit> strict = wantStrict ? strictFound ?? [] : [];
        var strictKeys = strict.Select(static hit => hit.ArticleIdentitySha256).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<EuropeIndexSearchHit> relaxed = !wantRelaxed
            ? []
            : mode is null
                ? (relaxedFound ?? []).Where(hit => !strictKeys.Contains(hit.ArticleIdentitySha256)).ToArray()
                : relaxedFound ?? [];
        var all = strict.Select(static hit => (Lane: "strict", Hit: hit))
            .Concat(relaxed.Select(static hit => (Lane: "relaxed", Hit: hit)))
            .ToArray();
        static string Cursor((string Lane, EuropeIndexSearchHit Hit) entry) => $"{entry.Lane}.{entry.Hit.ArticleIdentitySha256}";
        var start = 0;
        if (after is not null)
        {
            var index = Array.FindIndex(all, entry => string.Equals(Cursor(entry), after, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'after' names no hit of this query; a cursor is the continue_after of the same query, scope and index.");
            }

            start = index + 1;
        }

        var page = all.Skip(start).Take(limit).ToArray();
        var truncated = start + page.Length < all.Length;
        var searchSeed = EuropeSeedsOf(resolved[0].PublisherWorkId) is [var heldSeed] ? heldSeed : null;
        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_query = query,
            requested_language = language,
            requested_date = (string?)null,
            requested_identifier = identifier,
            requested_mode = mode,
            requested_after = after,
            publisher = "eu-eurlex",
            scope = EuropeSearchScope,
            ranking = EuropeSearchRanking,
            matching = EuropeSearchMatching,
            hit_unit = EuropeSearchHitUnit,
            lanes = SearchLanes,
            date_semantics = EuropeWordingDateSemantics,
            consolidations_held = searchSeed is not null && EuropeConsolidationsHeld(EuropeTimelineOf(searchSeed)),
            text_served = false,
            searchable_text_held_for_language = measured,
            searchable_languages = _europeReader.SearchableLanguages(),
            modes_held = SearchModes,
            modes_not_held = new[] { "bm25", "semantic" },
            work_resolution = new
            {
                retrieval_lane = "r1_work_discovery",
                outcome = "not_run_identifier_given",
                work = (object?)null,
                candidates = (object?)null,
            },
            terms,
            population = new
            {
                scope = new { identifier, language, date = (string?)null, mode },
                strict_hits = wantStrict ? (int?)strict.Count : null,
                relaxed_hits = wantRelaxed ? (int?)relaxed.Count : null,
                distinct_publisher_articles = all.Select(static entry => entry.Hit.PublisherIdentifier).Distinct(StringComparer.Ordinal).Count(),
                works_with_hits = all.Select(static entry => entry.Hit.PublisherWorkId).Distinct(StringComparer.Ordinal).Count(),
            },
            ambiguous_works = Array.Empty<object>(),
            limit,
            truncated,
            continue_after = truncated ? Cursor(page[^1]) : null,
            page_is = SearchPageIs,
            // The one held wording the hits are in, pinned (the EU permalink grammar), or null when it holds no single
            // wording date to pin; each hit's permalink is it with the provision after #.
            pinned_wording = wording is not { } held ? null : new
            {
                wording_date = held.WordingDate,
                wording_sha256 = held.Sha256,
                permalink = held.Permalink,
                digest_rule = EuropeWordingDigestRule,
            },
            hits = page.Select(entry => new
            {
                lane = entry.Lane,
                match_reasons = new[] { entry.Lane == "strict" ? "exact_phrase" : "all_terms" },
                publisher_work_id = entry.Hit.PublisherWorkId,
                celex = entry.Hit.PublisherWorkCelex,
                publisher_expression_id = entry.Hit.PublisherExpressionId,
                language = entry.Hit.Language,
                wording_date = entry.Hit.WordingDate,
                article_identity_sha256 = entry.Hit.ArticleIdentitySha256,
                publisher_id = entry.Hit.PublisherIdentifier,
                heading = entry.Hit.Heading,
                // The provision coordinate EU resolve answers; the text is not served here.
                resolve = new { identifier = entry.Hit.ProvisionCoordinate },
                // The hash-pinned provision permalink verify checks (null when the wording has no single date).
                permalink = wording is { } pinned ? EuropeProvisionPermalink(pinned.Permalink, entry.Hit.PublisherIdentifier) : null,
            }).ToArray(),
            not_held = EuropeNotHeldRows(EuropeSearchNotHeld, searchSeed,
                "consolidated versions of this act are held and answered by timeline, as_of and evidence_bundle; this search reads the original wording only, and a date is not answered here"),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "quote", result.RootElement));
    }

    internal const string CoverageScope =
        "the mounted Luxembourg corpus and index: what this mount holds and recorded as missing, and nothing about what the publisher holds";

    internal const string CoverageCountsNote =
        "counts are of rows the index holds; totals.articles and languages[].articles count the articles the index holds, " +
        "and the corpus can have recorded more articles than the index holds: members.article_outcomes counts what it recorded; " +
        "a missing publisher date is counted as missing and never dropped; " +
        "articles_with_searchable_text is counted where the article carries a publisher date, which is what the capability cells measure, " +
        "and so it and articles_without_publisher_date are not addends; " +
        "when a language is requested, requested_language echoes it and only languages and capability_cells are narrowed to it, and every other member, totals included, is the whole mount's";

    internal const string ArticleOutcomesNote =
        "the corpus's own record of what its legal-content stage did with the articles of its acquired Luxembourg documents, " +
        "counted by the corpus's disposition token and given verbatim, and this answer does not define the tokens; " +
        "an outcome is one article's, except akn_upstream_not_inventoried, which is one document's because none of its articles was listed; " +
        "the articles the index holds, which totals.articles and languages[].articles count, are exactly the akn_admitted and " +
        "akn_marker_only_evidence outcomes, and an outcome under any other token is not held here; " +
        "which article an outcome belongs to is not held; " +
        "the outcomes of members that are not acquired are not counted";

    internal const string CoverageOperationsNote =
        "served_operations are the routes this mount answers and not_served_operations are registered with no route on it; " +
        "a request for an unserved operation answers the transport failure operation_not_served (HTTP 404, below the envelope), " +
        "which tells it apart from a path nothing names (unknown_route); not_served_data names, for each unserved operation, " +
        "the data that would serve it, none of which the ingest produces";

    /// <summary>
    /// The data that would serve each registered operation this mount has no route for (the driver decision in STATUS:
    /// they keep the transport failure <c>operation_not_served</c>, and the platform says, per operation, which data
    /// would serve it). The data are the specification's own (<c>33-product-spec.md</c>: the publication-time view, the
    /// bitemporal replay, the EuroVoc concepts and the transposition bridge). A served operation never appears here, and
    /// an unserved one without an entry fails the coverage tests.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NotServedDataNeeded =
        V3UnservedOperations.Rows.ToDictionary(static row => row.Operation, static row => row.DataNeeded, StringComparer.Ordinal);

    internal const string CoverageEuropeRefusedNote =
        "refused_for_eu lists the operations the mounted EU index's capability manifest states an EU identifier is refused: a request " +
        "for one with an EU identifier answers the reason given (retrieval_mode_unavailable, naming the requested mode and the modes " +
        "available), and data_needed names the EU data that would serve it; an operation the EU time view serves for an EU act is not " +
        "listed, and an empty list means the mounted manifest states none";

    /// <summary>
    /// The coverage answer's operations block: the routes this mount answers and the registered operations with none, and,
    /// when an EU index is mounted, the operations its capability manifest states an EU identifier is refused, each with its
    /// typed reason and the EU data that would serve it (the launch contract: "each either served or refusing with a typed
    /// reason its capability manifest states"). A mount with no EU index answers the block exactly as before.
    /// </summary>
    private object CoverageOperations(string[] registered, string[] served)
    {
        var notServed = registered.Except(served, StringComparer.Ordinal).ToArray();
        var notServedData = notServed
            .Select(static operation => new
            {
                operation,
                data_needed = NotServedDataNeeded.TryGetValue(operation, out var needed)
                    ? needed
                    : throw new InvalidOperationException($"The unserved operation {operation} states no data that would serve it."),
            })
            .ToArray();
        if (_europeReader is null)
        {
            return new
            {
                registered = registered.Length,
                served_operations = served,
                not_served_operations = notServed,
                not_served_data = notServedData,
                note = CoverageOperationsNote,
            };
        }

        return new
        {
            registered = registered.Length,
            served_operations = served,
            not_served_operations = notServed,
            not_served_data = notServedData,
            note = CoverageOperationsNote,
            refused_for_eu = _europeReader.NotServed
                .Where(static row => string.Equals(
                    row.Reason, Lex.V3.Contracts.Index.V3UnservedOperation.RetrievalModeUnavailable, StringComparison.Ordinal))
                .Select(static row => new { operation = row.Operation, reason = row.Reason, data_needed = row.DataNeeded })
                .ToArray(),
            refused_for_eu_note = CoverageEuropeRefusedNote,
        };
    }

    /// <summary>The build-time row on a mount whose Luxembourg index is schema 6, which records no build time at all.</summary>
    internal static readonly string[] CoverageLegacyBuildTimeRow =
        ["build_time_and_currency", "this report states no build time and no build time of the corpus file is held, so nothing here says how current these counts are; this Luxembourg index is lex-v3-luxembourg-index/6 and its event log records no build time either; the corpus and index digests name exactly which artifacts are mounted"];

    internal const string HistoryNote =
        "the builds the mounted Luxembourg log records (its snapshots), and those whose text this mount holds: the mounted build, and each " +
        "earlier generation the retention line keeps beside it (S7-A09: referenced generations indefinitely, the last build of each UTC day " +
        "for 90 days, each UTC month's earliest indefinitely), held to the log when the mount opened; a snapshot without text is named by the " +
        "log alone, and as_observed answers its states without text; every time here is a build's, an upper bound on observation, never an " +
        "observation time";

    internal const string HistoryNotRecordedNote =
        "the mounted Luxembourg index is lex-v3-luxembourg-index/6 and records no build, so it names no snapshot and keeps no generation";

    /// <summary>
    /// The retained history depth (S7-A09's "reported history depth is truthful"): how many builds the mounted log records and
    /// since when, which of them this mount holds the text of (the mounted build and each retained generation, with why it is
    /// kept), how many it does not, and the retention line that decided. Null without a Luxembourg index.
    /// </summary>
    private object? HistoryBlock()
    {
        if (_reader is null)
        {
            return null;
        }

        if (!_reader.RecordsBuilds)
        {
            return new
            {
                log_records_builds = false,
                snapshots_in_log = 0,
                history_begins = (string?)null,
                retention_policy = (object?)null,
                snapshots_with_text = Array.Empty<object>(),
                snapshots_without_text = 0,
                note = HistoryNotRecordedNote,
            };
        }

        var observations = _reader.ResolveObservations();
        var withText = (_generations?.Retained ?? [])
            .Select(static kept => new { snapshot_id = kept.IndexSha256, observation = kept.Observation, built_at = kept.BuiltAt, retained_as = kept.Reasons.ToArray() })
            .Append(new { snapshot_id = _reader.IndexRef.Sha256, observation = observations[^1].Observation, built_at = observations[^1].BuiltAt, retained_as = new[] { "mounted" } })
            .ToArray();
        return new
        {
            log_records_builds = true,
            snapshots_in_log = observations.Count,
            history_begins = observations[0].BuiltAt,
            retention_policy = _generations is null
                ? null
                : new { id = _generations.PolicyId, nightly_days = V3GenerationRetention.NightlyDays, evaluated_at = _generations.EvaluatedAt },
            snapshots_with_text = withText,
            snapshots_without_text = observations.Count - withText.Length,
            note = HistoryNote,
        };
    }

    internal static readonly string[][] CoverageNotHeld =
    [
        ["publisher_universe", "how many acts the publisher holds, or how many of them this mount lacks: the mount records only what was admitted"],
        ["never_consolidated_acts", "the count of as-published acts never consolidated is a corpus-level statement this mount does not carry"],
        ["first_sighting_and_observation_times", "no observation time is held, so nothing here says when anything was first seen; events serves the event log, whose first_sighting events say only that a state is first present in that log"],
        ["build_time_and_currency", "history gives when each build ran, an upper bound on when its corpus was observed and never an observation time, and nothing here says how current these counts are against the publisher; no build time of the corpus file itself is held; the corpus and index digests name exactly which artifacts are mounted"],
        ["legal_status", "no status, repeal or commencement fact is counted here; status_on serves the publisher's force assertions per work, verbatim"],
    ];

    /// <summary>
    /// <c>coverage</c>: what the mounted Luxembourg corpus and index hold and what they recorded as
    /// missing, and nothing about what the publisher holds. Read from the verified index and the
    /// operation registry only; no publisher is contacted. The report is bounded: totals, per-language
    /// counts, members by outcome, the gap tokens the corpus recorded (verbatim, counted by member),
    /// the capability cells (what can be asked), which registered operations this mount serves, and a
    /// fixed list of what it does not hold. With a language, only the per-language parts narrow; a
    /// language the mount does not hold is <c>language_not_available</c>.
    /// </summary>
    public V3PlatformOperationOutcome Coverage(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "coverage", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus coverage operation only accepts coverage/1.");
        }

        var language = request.Parameters.TryGetProperty("language", out var languageValue) &&
                       languageValue.ValueKind == JsonValueKind.String
            ? RequiredString(request.Parameters, "language")
            : null;
        if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        var coverage = _reader.ResolveCoverage();
        var languagesHeld = coverage.Languages.Select(static held => held.Language).ToArray();
        if (language is not null && !languagesHeld.Contains(language, StringComparer.Ordinal))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = language,
                available_languages = languagesHeld,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        var cells = _reader.CapabilityCells();
        var searchable = _reader.SearchableLanguages();
        var registered = V3OperationRegistry.Reviewed.Operations
            .Select(static operation => operation.OperationId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var served = V3RestRouteBinding.Served
            .Select(static binding => binding.OperationId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var perLanguage = coverage.Languages
            .Where(held => language is null || string.Equals(held.Language, language, StringComparison.Ordinal))
            .Select(held => new
            {
                language = held.Language,
                works = held.Works,
                states = held.States,
                first_state_date = held.FirstStateDate,
                last_state_date = held.LastStateDate,
                articles = held.Articles,
                articles_without_publisher_date = held.ArticlesWithoutPublisherDate,
                articles_with_searchable_text = cells
                    .Where(cell =>
                        string.Equals(cell.Operation, "search", StringComparison.Ordinal) &&
                        string.Equals(cell.Column, "articles", StringComparison.Ordinal) &&
                        string.Equals(cell.Field, "searchable_text", StringComparison.Ordinal) &&
                        string.Equals(cell.Language, held.Language, StringComparison.Ordinal))
                    .Sum(static cell => cell.Population),
                searchable_text_held = searchable.Contains(held.Language, StringComparer.Ordinal),
            })
            .ToArray();
        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = CoverageScope,
            counts_note = CoverageCountsNote,
            requested_language = language,
            mounted = new
            {
                publisher = "lu-legilux",
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            history = HistoryBlock(),
            totals = new
            {
                members = coverage.Members,
                works = coverage.Works,
                states = coverage.States,
                articles = coverage.Articles,
            },
            languages_held = languagesHeld,
            languages = perLanguage,
            members = new
            {
                by_outcome = coverage.MemberOutcomes.Select(static row => new { outcome = row.Key, members = row.Value }).ToArray(),
                with_gaps = coverage.MembersWithGaps,
                gaps = coverage.Gaps.Select(static row => new { gap = row.Key, members = row.Value }).ToArray(),
                gaps_note = "the gap tokens the corpus recorded per member, verbatim, counted by member",
                article_outcomes = coverage.ArticleOutcomes
                    .Select(static row => new { disposition = row.Key, outcomes = row.Value })
                    .ToArray(),
                article_outcomes_note = ArticleOutcomesNote,
            },
            capability_cells = cells
                .Where(cell => language is null || string.Equals(cell.Language, language, StringComparison.Ordinal))
                .Select(static cell => new
                {
                    operation = cell.Operation,
                    column = cell.Column,
                    field = cell.Field,
                    language = cell.Language,
                    period_from = cell.PeriodFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    period_to = cell.PeriodTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    population = cell.Population,
                })
                .ToArray(),
            operations = CoverageOperations(registered, served),
            not_held = CoverageNotHeld
                .Select(row => _reader.RecordsBuilds || row[0] != "build_time_and_currency" ? row : CoverageLegacyBuildTimeRow)
                .Select(static row => new { item = row[0], reason = row[1] })
                .ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "coverage_report", result.RootElement));
    }

    /// <summary>The typed presentation result every <c>ask</c> answers while the assistant is contained (Decision 91).</summary>
    internal const string AssistantUnavailable = "assistant_v3_unavailable";

    internal const string AskPresentationNote =
        "a typed presentation result of ask (Decisions 51 and 91), not a refusal code: the closed refusal registry stays at its twenty codes, " +
        "and this is neither an answer from held law nor a refusal";

    internal const string AskContainmentReason =
        "the assistant is contained: no model answers, retrieves, reranks or authors authoritative text, and the legacy assistant route stays disabled " +
        "until the answer_dossier/1 (S4-A04) and advice-boundary (S4-A05) slices are independently reviewed and integrated; " +
        "the deterministic operations below answer from held law";

    internal const string AskQuestionNote =
        "the question is checked to be a non-blank string and is not read, stored or echoed; nothing in this answer depends on it";

    /// <summary>
    /// The deterministic operations an <c>ask</c> points to, in the order a reader would use them
    /// (resolver first, then search, then the text of a state), each with what it answers.
    /// </summary>
    internal static readonly string[][] AskDeterministicActions =
    [
        ["resolve", "the work an exact identifier names (ELI, CELEX, publisher identifier or permalink), or identifier_unknown"],
        ["search", "the provisions whose publisher wording matches the query, strict lane before relaxed, per language"],
        ["as_of", "the state of a work on a civil date, or ambiguous_version / no_version_for_date rather than a silent choice"],
        ["evidence_bundle", "the text of a state's articles with text_sha256, body_sha256, official source and permalink, rights enforced"],
    ];

    /// <summary>
    /// <c>ask</c> while the assistant is contained (Decisions 51 and 91, S4-A05): every request answers
    /// the typed presentation result <c>assistant_v3_unavailable</c> as a <c>handoff_card</c> under the
    /// <c>point</c> verdict, never a refusal code and never an answer from held law. The question is
    /// checked by the request schema and not read, so the card is the same for every question: the
    /// containment and its end condition, and the deterministic operations that do answer (resolve,
    /// search, as_of, evidence_bundle), each with its route, the parameters its reviewed request schema
    /// requires and what it answers; for search, the languages this mount holds searchable text in. No
    /// model is called and no publisher is contacted.
    /// </summary>
    public V3PlatformOperationOutcome Ask(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "ask", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus ask operation only accepts ask/1.");
        }

        _ = RequiredString(request.Parameters, "question");
        if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            AskContained(request, _reader.SearchableLanguages()));
    }

    /// <summary>
    /// The card <see cref="Ask"/> answers, given the languages the mount holds searchable text in.
    /// It reads nothing of the question, which is why it can be built here without a mount and put to
    /// every scope-line question in the contracts suite.
    /// </summary>
    internal static V3PlatformOperationResult AskContained(
        V3PlatformOperationRequest request,
        IEnumerable<string> searchableLanguages)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(searchableLanguages);
        if (!string.Equals(request.OperationId, "ask", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The contained ask card only answers ask/1.");
        }

        var searchable = searchableLanguages.Order(StringComparer.Ordinal).ToArray();
        var actions = AskDeterministicActions.Select(action =>
        {
            var binding = V3RestRouteBinding.Served.Single(served => string.Equals(served.OperationId, action[0], StringComparison.Ordinal));
            return new
            {
                operation_id = action[0],
                route = binding.RawTarget,
                required_parameters = RequiredRequestParameters(action[0]),
                languages = string.Equals(action[0], "search", StringComparison.Ordinal) ? searchable : null,
                answers = action[1],
            };
        }).ToArray();
        using var result = JsonSerializer.SerializeToDocument(new
        {
            presentation_result = AssistantUnavailable,
            presentation_note = AskPresentationNote,
            containment = new
            {
                decisions = new[] { "51", "91" },
                reason = AskContainmentReason,
                model_gloss = "disabled",
            },
            question_read = false,
            question_note = AskQuestionNote,
            deterministic_actions = actions,
        });
        return new V3PlatformOperationResult(request, "handoff_card", result.RootElement, V3Verdicts.Point);
    }

    /// <summary>The parameters an operation's reviewed request schema requires, read from the schema itself so the card cannot drift from it.</summary>
    private static string[] RequiredRequestParameters(string operationId)
    {
        using var schema = JsonDocument.Parse(V3PlatformSchemaExporter.ExportRequestUtf8(operationId));
        return schema.RootElement.GetProperty("properties").GetProperty("parameters").GetProperty("required")
            .EnumerateArray()
            .Select(static required => required.GetString()!)
            .ToArray();
    }

    /// <summary>The ceiling on the events one <c>events</c> page carries.</summary>
    public const int EventsMaxRows = 200;

    internal const string EventsScope =
        "the event log of the mounted Luxembourg index: a list numbered from 1, polled by cursor, append-only; a genesis log (one observation, " +
        "no predecessor) holds one first_sighting per held state and nothing else; a chained log carries its predecessors' observations unchanged " +
        "and appends one observation per later build (log.basis says which)";

    internal const string EventsDeliveryNote =
        "cursor polling, at least once: while this index is mounted a request with the same cursor answers the same events again, so a reader " +
        "deduplicates by seq within log_id; next_after is always the cursor to poll next; a build chained to this one carries this log forward " +
        "with the same numbers, so a cursor of this log reads on in its successor (log.ancestors lists the logs a log honours); a build not chained " +
        "to this one starts a new log, under which a cursor of this log refuses snapshot_unknown; there is no push delivery and no subscription " +
        "(Decision 93)";

    internal const string EventsGenesisNote =
        "this log comes from one observation with no predecessor, so it holds only first_sighting; first_sighting means first present in this log " +
        "and does not say when the publisher published the text or when it was fetched; observed_from is null because no observation time is held; " +
        "log.built_at is when the build ran, an upper bound on when its corpus was observed and never an observation time; " +
        "a revision (validity_revised, interval_closed, file_replaced, withdrawn_from_source, ...) needs a later build compared against this one";

    /// <summary>The genesis note on a schema-6 index, which records no build time: the same, without the build-time sentence.</summary>
    internal const string EventsGenesisNoteLegacy =
        "this log comes from one observation with no predecessor, so it holds only first_sighting; first_sighting means first present in this log " +
        "and does not say when the publisher published the text or when it was fetched; observed_from is null because no observation time is held, " +
        "and log.built_at is null because this index records no build time (legacy_note); " +
        "a revision (validity_revised, interval_closed, file_replaced, withdrawn_from_source, ...) needs a later build compared against this one";

    /// <summary>The silence note on a schema-6 index, which records no build time.</summary>
    internal const string EventsSilenceNoteLegacy =
        "an empty page says this log holds no further event; it says nothing about whether the publisher changed anything or whether acquisition ran: " +
        "this mount holds no upstream health and its index records no build time, and every envelope's freshness names upstream health stale";

    /// <summary>The upstream-health row on a schema-6 index, which records no build time.</summary>
    internal static readonly string[] EventsUpstreamHealthLegacy =
        ["upstream_health", "no upstream health and no build time is held, so silence here is not a statement that the publisher was quiet"];

    internal const string EventsSilenceNote =
        "an empty page says this log holds no further event; it says nothing about whether the publisher changed anything or whether acquisition ran: " +
        "this mount holds no upstream health, a build's time (log.built_at) says when it ran and not what the publisher did after, and every " +
        "envelope's freshness names upstream health stale";

    internal const string EventNamesNote =
        "the Stage 4 registry names thirteen events; mintable lists the twelve this pipeline may mint, never the coverage event (B42 finding 5.2: a " +
        "gate must not be excused by an event the same pipeline mints); a genesis log holds first_sighting only; a chained log adds expression_added, " +
        "file_replaced, interval_closed and validity_revised; withdrawn_from_source and resighted need three completed runs and a complete " +
        "enumeration, and metadata_revised and the relation and future-state events need data this index does not hold, so none of them is minted; " +
        "in_this_log names those this log holds";

    internal const string EventsLegacyNote =
        "this Luxembourg index is lex-v3-luxembourg-index/6, built before the event log recorded builds: its log is a genesis log of " +
        "first_sighting events naming state digests only, and it records no build time, no corpus per build, no source bodies and no log " +
        "stamp, so log.built_at is null, no build bounds when anything was observed, as_observed names no snapshot of it, and it cannot be " +
        "a predecessor";

    internal static readonly string[] EventsLegacyNotHeld =
        ["build_record", "this index is schema 6: its log records no build, no build time, no source bodies and no log stamp, and nothing stands in for them"];

    internal const string EventsChainedNote =
        "this log carries its predecessors' observations unchanged and appends one observation per later build; each appended event compares that " +
        "build's states with the log before it: first_sighting or expression_added for a state new to the log, file_replaced when a state's source " +
        "bodies differ (its digest changes with its text, not with the bytes alone), and interval_closed or validity_revised when a state's interval " +
        "moved, an end derived from the publisher's start dates and never asserted by the publisher; a state the log holds and a later build lacks " +
        "stays held, because absence is not a withdrawal; observed_from is null because no observation time is held, and each build's time " +
        "(log.built_at, and each ancestor's built_at) is an upper bound on when its corpus was observed";

    internal static readonly string[][] EventsChainedNotHeld =
    [
        ["observation_times", "no observation time is held; observed_from is null on every event"],
        ["withdrawal_events", "a state the log holds and a later build lacks stays held: a withdrawal needs three completed runs and a complete enumeration, which no build here proves"],
        ["work_level_events", "events are scoped to states; no work-level event is minted"],
        ["upstream_health", "no upstream health is held, and a build's time says when it ran and not what the publisher did after, so silence here is not a statement that the publisher was quiet"],
    ];

    internal const string AnswerDriftChainedBasis =
        "enumerated from the log's validity_revised and interval_closed events: each row is the interval of dates, in one work and language, whose " +
        "as_of answer the revision moved from the state named before to the state applying from the row's first date; the dates are derived from the " +
        "publisher's start dates, not asserted by the publisher; a file_replaced event changes a state's text without moving an interval and is not " +
        "enumerated here; this is a statement about the log, not about the law or the publisher";

    internal const string EventsForeignCursorWhatWouldAnswer =
        "a cursor from this log (its log_id is the mounted index digest) or from an ancestor it carries forward (log.ancestors), or no cursor to " +
        "read this log from its first event";

    internal static readonly string[][] EventsNotHeld =
    [
        ["observation_times", "no observation time is held; observed_from is null on every event"],
        ["revision_events", "no predecessor build is compared, so no revision, withdrawal, replacement or relation event is held"],
        ["work_level_events", "events are scoped to states; no work-level event is minted"],
        ["upstream_health", "no upstream health is held, and a build's time says when it ran and not what the publisher did after, so silence here is not a statement that the publisher was quiet"],
    ];

    internal const string AnswerDriftScope =
        "the past dated answers (work and date) a publisher revision invalidated, enumerated from the validity_revised and interval_closed events of the mounted index's log";

    internal const string AnswerDriftBasis =
        "this log is a genesis log and holds no validity_revised or interval_closed event, so no invalidated answer can be enumerated; " +
        "this is a statement about the log, not about the law or the publisher";

    internal const string AnswerDriftWhatWouldAnswer =
        "a later build of this corpus compared against this one, whose log would hold validity_revised or interval_closed events";

    internal const string AnswerDriftRowShapeNote =
        "when a revising event is held, each invalidated answer is one row: the event's seq, the work, the language and the interval of dates " +
        "whose as_of answer changed, with the permalinks before and after";

    /// <summary>
    /// <c>events</c> for Luxembourg: the mounted index's event log, one ordered, cursor-paged list, with no
    /// observation time (null, never invented). A genesis log (one observation, no predecessor) holds one
    /// <c>first_sighting</c> per held state; a chained log carries its predecessors' observations and appends one
    /// per later build, and the log block names its basis, its predecessor, how many builds were compared and the
    /// ancestor logs whose cursors it honours. A cursor is <c>{log_id}:{seq}</c>, where the log id is an index
    /// digest; one of this log or of an ancestor reads on, one from any other log refuses <c>snapshot_unknown</c>,
    /// and a sequence number beyond its log's last is a request-schema failure. Each row carries the log's detail
    /// verbatim. <c>event</c> narrows to one registry name. The answer says what at-least-once polling means, what
    /// its basis cannot say, and that silence is not upstream health.
    /// </summary>
    public V3PlatformOperationOutcome Events(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "events", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus events operation only accepts events/1.");
        }

        var eventName = request.Parameters.TryGetProperty("event", out var eventValue) && eventValue.ValueKind == JsonValueKind.String
            ? RequiredString(request.Parameters, "event")
            : null;
        if (eventName is not null && !V3EventRegistry.Mintable.Contains(eventName, StringComparer.Ordinal))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'event' is not a name this pipeline mints.");
        }

        var limit = EventPageLimit(request.Parameters);
        var cursor = EventCursorOf(request.Parameters);
        if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        var log = _reader.ResolveEventLog();
        var observations = _reader.ResolveObservations();
        var logId = _reader.IndexRef.Sha256;
        if (RefuseUnlessCursorOfThisLog(request, cursor, logId, log, observations, observedAt) is { } foreign)
        {
            return foreign;
        }

        var chained = observations.Count > 1;
        var rows = _reader.ResolveEvents(cursor?.Seq ?? 0, eventName, limit + 1);
        var hasMore = rows.Count > limit;
        var served = rows.Take(limit).ToArray();
        var nextSeq = hasMore ? served[^1].Seq : log.LastSeq;
        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = EventsScope,
            log = EventLogBlock(logId, log, observations),
            requested_event = eventName,
            events = served.Select(value => new
            {
                seq = value.Seq,
                cursor = $"{logId}:{value.Seq}",
                scope = value.Scope,
                @event = value.Event,
                observed_from = value.ObservedFrom,
                work_key = value.WorkKey,
                applicability_date = value.ApplicabilityDate,
                expression_iri = value.ExpressionIri,
                language = value.Language,
                state_sha256 = value.StateSha256,
                stable_coordinate = $"/lu-legilux/{value.WorkKey}/{value.ApplicabilityDate}",
                permalink = $"/lu-legilux/{value.WorkKey}/{value.ApplicabilityDate}--{value.StateSha256}",
                // The log's own detail, verbatim: the source bodies, and what a revision replaced or moved.
                detail = JsonDocument.Parse(value.DetailJson).RootElement.Clone(),
                replaced_permalink = value.Event == V3EventRegistry.FileReplaced
                    ? $"/lu-legilux/{value.WorkKey}/{value.ApplicabilityDate}--{JsonDocument.Parse(value.DetailJson).RootElement.GetProperty("replaced_state_sha256").GetString()}"
                    : null,
            }).ToArray(),
            has_more = hasMore,
            next_after = $"{logId}:{nextSeq}",
            delivery = EventsDeliveryNote,
            genesis_note = _reader.RecordsBuilds ? EventsGenesisNote : EventsGenesisNoteLegacy,
            chained_note = chained ? EventsChainedNote : null,
            legacy_note = _reader.RecordsBuilds ? null : EventsLegacyNote,
            silence_note = _reader.RecordsBuilds ? EventsSilenceNote : EventsSilenceNoteLegacy,
            event_names = new
            {
                mintable = V3EventRegistry.Mintable,
                in_this_log = V3EventRegistry.Mintable.Where(name => _reader.CountEvents([name]) > 0).ToArray(),
                note = EventNamesNote,
            },
            not_held = (chained ? EventsChainedNotHeld : EventsNotHeld)
                .Select(row => _reader.RecordsBuilds || row[0] != "upstream_health" ? row : EventsUpstreamHealthLegacy)
                .Concat(_reader.RecordsBuilds ? [] : [EventsLegacyNotHeld])
                .Select(static row => new { item = row[0], reason = row[1] })
                .ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "event", result.RootElement));
    }

    /// <summary>
    /// <c>answer_drift</c> for Luxembourg: the past dated answers a publisher revision invalidated,
    /// enumerated from the log's <c>validity_revised</c> and <c>interval_closed</c> events, a page at a time:
    /// each row the dates of one work and language whose as_of answer moved from the state before to the state
    /// applying from the first of them, derived from the publisher's start dates. A genesis log holds no
    /// revising event, so its answer is an empty list with its basis and absence flags
    /// (<c>asserts_no_drift_in_law</c> and <c>asserts_publisher_unrevised</c> are false) and what would
    /// answer, never "nothing drifted". An identifier narrows to one work and refuses as <c>dossier</c>
    /// refuses; the cursor is the event log's and is read as <c>events</c> reads it.
    /// </summary>
    public V3PlatformOperationOutcome AnswerDrift(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "answer_drift", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus drift operation only accepts answer_drift/1.");
        }

        var identifier = request.Parameters.TryGetProperty("identifier", out var identifierValue) && identifierValue.ValueKind == JsonValueKind.String
            ? RequiredString(request.Parameters, "identifier")
            : null;
        var limit = EventPageLimit(request.Parameters);
        var cursor = EventCursorOf(request.Parameters);
        string? workKey = null;
        if (identifier is not null)
        {
            if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_answer_drift", null,
                    out var states, out _) is { } refused)
            {
                return refused;
            }

            workKey = states[0].WorkKey;
        }
        else if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        var log = _reader!.ResolveEventLog();
        var observations = _reader.ResolveObservations();
        var logId = _reader.IndexRef.Sha256;
        if (RefuseUnlessCursorOfThisLog(request, cursor, logId, log, observations, observedAt) is { } foreign)
        {
            return foreign;
        }

        var revising = _reader.CountEvents(V3EventRegistry.Revising);
        var rows = _reader.ResolveRevisingEvents(cursor?.Seq ?? 0, workKey, limit + 1);
        var hasMore = rows.Count > limit;
        var served = rows.Take(limit).ToArray();
        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = AnswerDriftScope,
            log = EventLogBlock(logId, log, observations),
            requested_identifier = identifier,
            work_key = workKey,
            revising_event_types = V3EventRegistry.Revising,
            revising_events_considered = revising,
            invalidated_answers = served.Select(InvalidatedAnswer).ToArray(),
            has_more = hasMore,
            next_after = $"{logId}:{(hasMore ? served[^1].Seq : log.LastSeq)}",
            basis = revising == 0 && observations.Count <= 1 ? AnswerDriftBasis : AnswerDriftChainedBasis,
            asserts_no_drift_in_law = false,
            asserts_publisher_unrevised = false,
            what_would_answer = AnswerDriftWhatWouldAnswer,
            row_shape_note = AnswerDriftRowShapeNote,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "answer_drift", result.RootElement));
    }

    private sealed record EventCursor(string LogId, long Seq);

    /// <summary>
    /// One answer a revising event invalidated: the dates of one work and language, from the revision's new end to its
    /// old one (open when the interval was the latest), whose as_of answer moved from the state the event names to the
    /// state applying from the first of those dates (one, or several twins on that date, each by its permalink).
    /// </summary>
    private object InvalidatedAnswer(LuxembourgIndexEvent value)
    {
        using var detail = JsonDocument.Parse(value.DetailJson);
        var previousTo = detail.RootElement.GetProperty("previous_to").GetString();
        var newTo = detail.RootElement.GetProperty("new_to").GetString()!;
        var now = _reader!.ResolveWorkStates(value.WorkKey)
            .Where(state => string.Equals(state.ApplicabilityDate, newTo, StringComparison.Ordinal) &&
                            string.Equals(state.Language, value.Language, StringComparison.Ordinal))
            .Select(state => $"/lu-legilux/{state.WorkKey}/{state.ApplicabilityDate}--{state.StateSha256}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new
        {
            seq = value.Seq,
            @event = value.Event,
            work_key = value.WorkKey,
            language = value.Language,
            dates_from = newTo,
            dates_to_exclusive = previousTo,
            answered_before = $"/lu-legilux/{value.WorkKey}/{value.ApplicabilityDate}--{value.StateSha256}",
            answered_now = now,
            derived = true,
        };
    }

    /// <summary>
    /// The log block: its id (the index digest); its basis (a genesis log, or one chained to predecessors); when its
    /// build ran (an upper bound on when its corpus was observed, never an observation time); the predecessor its last
    /// observation carried forward and how many builds were compared; each ancestor log whose cursors this log still
    /// honours, to its last sequence number, with when that build ran; and the events it holds.
    /// </summary>
    private object EventLogBlock(string logId, LuxembourgIndexEventLog log, IReadOnlyList<LuxembourgIndexObservation> observations) => new
    {
        log_id = logId,
        basis = observations.Count <= 1 ? V3EventRegistry.GenesisBasis : V3EventRegistry.ChainedBasis,
        built_at = observations.Count == 0 ? null : observations[^1].BuiltAt,
        predecessor_index_sha256 = observations.Count == 0 ? null : observations[^1].PredecessorIndexSha256,
        observations_compared = Math.Max(0, observations.Count - 1),
        ancestors = AncestorLogs(observations)
            .Select(ancestor => new
            {
                log_id = ancestor.LogId,
                last_seq = ancestor.LastSeq,
                built_at = ancestor.BuiltAt,
                text_held = RetainedAs(ancestor.LogId).Length != 0,
                retained_as = RetainedAs(ancestor.LogId),
            })
            .ToArray(),
        events_held = log.Events,
        last_seq = log.LastSeq,
    };

    /// <summary>Why the retention line keeps an earlier build of the mounted log beside the mount; none when it is not kept.</summary>
    private string[] RetainedAs(string indexSha256) =>
        _generations?.Retained.FirstOrDefault(kept => string.Equals(kept.IndexSha256, indexSha256, StringComparison.Ordinal))?.Reasons.ToArray() ?? [];

    /// <summary>
    /// The logs this log carries forward, each to the last event it held and with when its build ran: an observation's
    /// predecessor held the events before it and was built at the observation before it.
    /// </summary>
    private static (string LogId, long LastSeq, string BuiltAt)[] AncestorLogs(IReadOnlyList<LuxembourgIndexObservation> observations) =>
        observations
            .Select(static (observation, index) => (observation, index))
            .Where(static pair => pair.observation.PredecessorIndexSha256 is not null && pair.index > 0)
            .Select(pair => (pair.observation.PredecessorIndexSha256!, pair.observation.FirstSeq - 1, observations[pair.index - 1].BuiltAt))
            .ToArray();

    private static int EventPageLimit(JsonElement parameters)
    {
        var limit = EventsMaxRows;
        if (parameters.TryGetProperty("limit", out var limitValue) &&
            (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
             limit < 1 || limit > EventsMaxRows))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'limit' is not a whole number of events within the ceiling.");
        }

        return limit;
    }

    /// <summary>The request's cursor, parsed; the shape is the request schema's, held again here.</summary>
    private static EventCursor? EventCursorOf(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("after", out var afterValue) || afterValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = RequiredString(parameters, "after");
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator != 64 ||
            !value[..64].All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            !long.TryParse(value[65..], NumberStyles.None, CultureInfo.InvariantCulture, out var seq) ||
            !string.Equals(seq.ToString(CultureInfo.InvariantCulture), value[65..], StringComparison.Ordinal))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'after' is not an event cursor ({log_id}:{seq}).");
        }

        return new EventCursor(value[..64], seq);
    }

    /// <summary>
    /// A cursor names its log. One of this log, or of an ancestor this log carries forward unchanged (to the last event
    /// that ancestor held, the same numbers here), reads on in this log. One from any other log (another index digest,
    /// such as a rebuild not chained to this one) is <c>snapshot_unknown</c>: read against this log it would silently
    /// skip or repeat events. A sequence number beyond the log's last was never handed out by it and is a request-schema
    /// failure.
    /// </summary>
    private V3PlatformOperationOutcome? RefuseUnlessCursorOfThisLog(
        V3PlatformOperationRequest request,
        EventCursor? cursor,
        string logId,
        LuxembourgIndexEventLog log,
        IReadOnlyList<LuxembourgIndexObservation> observations,
        DateTimeOffset observedAt)
    {
        if (cursor is null)
        {
            return null;
        }

        var ancestor = AncestorLogs(observations).FirstOrDefault(candidate => string.Equals(candidate.LogId, cursor.LogId, StringComparison.Ordinal));
        if (ancestor.LogId is not null)
        {
            if (cursor.Seq > ancestor.LastSeq)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'after' names a sequence number its log never handed out.");
            }

            return null;
        }

        if (!string.Equals(cursor.LogId, logId, StringComparison.Ordinal))
        {
            using var unknown = JsonSerializer.SerializeToDocument(new
            {
                snapshot_id = cursor.LogId,
                what_would_answer = EventsForeignCursorWhatWouldAnswer,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "snapshot_unknown", unknown.RootElement));
        }

        if (cursor.Seq > log.LastSeq)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'after' names a sequence number this log never handed out.");
        }

        return null;
    }

    /// <summary>The ceiling on the rows one <c>changes_in_period</c> answer carries.</summary>
    public const int ChangesInPeriodMaxRows = 200;

    internal const string ChangesInPeriodCaveat =
        "a version row does not by itself assert a wording change, legal effect, or entry into force";

    /// <summary>
    /// R6 <c>changes_in_period</c> for Luxembourg, the change radar: every publisher-dated state whose
    /// date lies in a closed window, across the mounted works or for one, per language, in publisher
    /// date, work key, language, expression and digest order. The window is the closed interval between
    /// the two dates whichever is given first. Each row names the state compactly (the full state is one
    /// <c>resolve</c> away, and the row carries its parameters: the hash-pinned permalink, which reaches
    /// that state and no other even where its date has twins), the <c>baseline</c> it replaced (the
    /// state of the same work and language on the greatest earlier publisher date), and
    /// <c>wording_changed</c> from the article-level comparison <c>diff</c> makes, with its counts and
    /// the parameters that ask <c>diff</c> for the pair.
    /// <para>
    /// <c>wording_changed</c> is null with a <c>reason</c> wherever comparing would be dishonest. The
    /// row's own date is judged first: a state that is one of several on its date and language is
    /// ambiguous, whether or not anything precedes it, since <c>as_of</c> and <c>diff</c> refuse that
    /// date. Then: the first held state has no baseline; a baseline date holding several states is
    /// ambiguous and none is chosen; states with different rule-profile sets are never compared, as
    /// <c>diff</c> refuses. These are facts about one row and not refusals of the whole
    /// radar: <c>diff</c> answers about one work and refuses whole, but a radar that one ambiguous
    /// work could silence would hide every other work's change from the reader.
    /// </para>
    /// <para>
    /// Rows are bounded and the bound is said: at most <c>limit</c> rows (the ceiling when absent),
    /// never cut within a publisher date, with <c>truncated</c> and <c>continue_from</c>, the first date
    /// not served, which a next request uses as its <c>date_from</c> and neither repeats nor skips a
    /// row. A single date holding more rows than the limit is served whole and
    /// <c>whole_date_over_limit</c> says so. The population block counts the whole window in the scope
    /// asked for (the work, the language), not the rows served. Nothing is derived: no legal
    /// effect, no amending act, no entry into force, and the fixed caveat says so in every answer.
    /// </para>
    /// </summary>
    public V3PlatformOperationOutcome ChangesInPeriod(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "changes_in_period", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus radar operation only accepts changes_in_period/1.");
        }

        var dateFrom = RequiredString(request.Parameters, "date_from");
        var dateTo = RequiredString(request.Parameters, "date_to");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        var identifier = request.Parameters.TryGetProperty("identifier", out var identifierValue) &&
            identifierValue.ValueKind == JsonValueKind.String
                ? RequiredString(request.Parameters, "identifier")
                : null;
        var limit = ChangesInPeriodMaxRows;
        if (request.Parameters.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) ||
                limit < 1 || limit > ChangesInPeriodMaxRows)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'limit' is not a whole number of rows within the ceiling.");
            }
        }

        foreach (var date in new[] { dateFrom, dateTo })
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "A requested date is not a civil calendar date.");
            }
        }

        var windowFrom = string.CompareOrdinal(dateFrom, dateTo) <= 0 ? dateFrom : dateTo;
        var windowTo = string.CompareOrdinal(dateFrom, dateTo) <= 0 ? dateTo : dateFrom;

        IReadOnlyList<LuxembourgIndexResolvedState> inWindow;
        string[] languagesHeld;
        long worksHeld;
        string? firstHeld;
        string? lastHeld;
        var statesByWork = new Dictionary<string, IReadOnlyList<LuxembourgIndexResolvedState>>(StringComparer.Ordinal);
        if (identifier is not null)
        {
            if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_changes_in_period", requestedLanguage,
                    out var states, out var availableLanguages) is { } refused)
            {
                return refused;
            }

            statesByWork[states[0].WorkKey] = states;
            inWindow = states
                .Where(state => string.CompareOrdinal(state.ApplicabilityDate, windowFrom) >= 0 &&
                    string.CompareOrdinal(state.ApplicabilityDate, windowTo) <= 0)
                .ToArray();
            languagesHeld = availableLanguages;
            worksHeld = 1;
            var heldDates = states
                .Where(state => requestedLanguage is null || string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .Select(static state => state.ApplicabilityDate).Order(StringComparer.Ordinal).ToArray();
            firstHeld = heldDates[0];
            lastHeld = heldDates[^1];
        }
        else
        {
            if (_reader is null)
            {
                // The radar reads the Luxembourg index. Without it there is nothing to read a window
                // against, whatever else is mounted; the refusal says which corpus is required.
                using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.LuLegilux),
                    new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
            }

            languagesHeld = _reader.ResolveStatePopulation().Languages.ToArray();
            if (requestedLanguage is not null && !languagesHeld.Contains(requestedLanguage, StringComparer.Ordinal))
            {
                using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
                {
                    requested_language = requestedLanguage,
                    available_languages = languagesHeld,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
            }

            inWindow = _reader.ResolveStatesInPeriod(windowFrom, windowTo);
            var population = _reader.ResolveStatePopulation(requestedLanguage);
            worksHeld = population.Works;
            firstHeld = population.FirstDate;
            lastHeld = population.LastDate;
        }

        var versions = requestedLanguage is null
            ? inWindow
            : inWindow.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal)).ToArray();

        // Rows are never cut within a publisher date: whole dates are served until the limit is reached,
        // and the first date not served is where the reader continues.
        var served = new List<LuxembourgIndexResolvedState>();
        string? continueFrom = null;
        foreach (var byDate in versions.GroupBy(static state => state.ApplicabilityDate, StringComparer.Ordinal))
        {
            if (served.Count > 0 && served.Count + byDate.Count() > limit)
            {
                continueFrom = byDate.Key;
                break;
            }

            served.AddRange(byDate);
        }

        var rows = new List<object>();
        foreach (var state in served)
        {
            if (!statesByWork.TryGetValue(state.WorkKey, out var ofWork))
            {
                ofWork = _reader!.ResolveWorkStates(state.WorkKey);
                statesByWork[state.WorkKey] = ofWork;
            }

            var ofLanguage = ofWork
                .Where(other => string.Equals(other.Language, state.Language, StringComparison.Ordinal))
                .ToArray();
            var sameDate = ofLanguage
                .Where(other => string.Equals(other.ApplicabilityDate, state.ApplicabilityDate, StringComparison.Ordinal))
                .ToArray();
            var earlier = ofLanguage
                .Where(other => string.CompareOrdinal(other.ApplicabilityDate, state.ApplicabilityDate) < 0)
                .ToArray();
            var baselineDate = earlier.Length == 0
                ? null
                : earlier.Select(static other => other.ApplicabilityDate).Order(StringComparer.Ordinal).Last();
            var baselines = earlier
                .Where(other => string.Equals(other.ApplicabilityDate, baselineDate, StringComparison.Ordinal))
                .ToArray();

            bool? wordingChanged = null;
            string? reason = null;
            object? counts = null;
            object? diff = null;
            string[]? candidates = null;
            // The row's own date is judged first. A twin on the earliest date held has no baseline
            // either, but "first held state" would hide the fact that decides what the reader may do
            // with the date: as_of and diff refuse it as ambiguous, and the row must predict that.
            if (sameDate.Length > 1)
            {
                reason = "ambiguous_version";
                candidates = sameDate.Select(StateUrl).Order(StringComparer.Ordinal).ToArray();
            }
            else if (baselines.Length == 0)
            {
                reason = "first_held_state";
            }
            else if (baselines.Length > 1)
            {
                reason = "ambiguous_baseline";
                candidates = baselines.Select(StateUrl).Order(StringComparer.Ordinal).ToArray();
            }
            else if (!baselines[0].RuleProfileSha256s.SequenceEqual(state.RuleProfileSha256s, StringComparer.Ordinal))
            {
                reason = "profiles_differ";
            }
            else
            {
                var (_, compared, differs) = CompareArticles(
                    _reader!.ResolveStateArticles(baselines[0].StateSha256), _reader.ResolveStateArticles(state.StateSha256));
                wordingChanged = differs;
                counts = compared;
                diff = new
                {
                    identifier = $"/lu-legilux/{state.WorkKey}",
                    date_from = baselines[0].ApplicabilityDate,
                    date_to = state.ApplicabilityDate,
                    language = state.Language,
                };
            }

            rows.Add(new
            {
                work_key = state.WorkKey,
                publisher_work_iri = state.PublisherWorkIri,
                state = StateReference(state, NextDateInLanguage(ofWork, state)),
                baseline = baselines.Length == 1 ? StateReference(baselines[0], state.ApplicabilityDate) : null,
                wording_changed = wordingChanged,
                reason,
                candidates,
                counts,
                diff,
                // The hash-pinned permalink names this state and no other, so resolve serves it in full
                // even where its date has twins, which as_of would refuse as ambiguous.
                resolve = new { identifier = StateUrl(state) },
            });
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_date_from = dateFrom,
            requested_date_to = dateTo,
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            window_from = windowFrom,
            window_to = windowTo,
            publisher = "lu-legilux",
            caveat = ChangesInPeriodCaveat,
            population = new
            {
                // The population is the scope asked for: the one work when an identifier is given, the
                // one language when a language is given, the whole mounted index otherwise. The languages
                // listed are every language of that work or index, since they are what could be asked.
                scope = new { identifier, language = requestedLanguage },
                works_held = worksHeld,
                first_date_held = firstHeld,
                last_date_held = lastHeld,
                languages_held = languagesHeld,
                works_in_window = versions.Select(static state => state.WorkKey).Distinct(StringComparer.Ordinal).Count(),
                versions_in_window = versions.Count,
                window_overlaps_what_is_held = firstHeld is not null && lastHeld is not null &&
                    string.CompareOrdinal(windowFrom, lastHeld) <= 0 && string.CompareOrdinal(windowTo, firstHeld) >= 0,
            },
            limit,
            truncated = continueFrom is not null,
            continue_from = continueFrom,
            // A publisher date is never cut: when one date alone holds more rows than the limit it is
            // served whole, and this says the limit was exceeded for that reason.
            whole_date_over_limit = served.Count > limit,
            changes = rows,
            wording_rule = WordingRule,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "change_list", result.RootElement));
    }

    /// <summary>
    /// A state named compactly for a list: what identifies it and where it is read in full. The article
    /// lists stay with <c>resolve</c>, <c>as_of</c> and <c>timeline</c>, which serve the whole state.
    /// </summary>
    private static object StateReference(LuxembourgIndexResolvedState state, string? nextDate) => new
    {
        language = state.Language,
        applicability_date = state.ApplicabilityDate,
        next_applicability_date = nextDate,
        state_sha256 = state.StateSha256,
        expression_iri = state.ExpressionIri,
        article_count = state.ArticleIdentities.Count,
        rule_profile_sha256s = state.RuleProfileSha256s,
        stable_coordinate = StableCoordinate(state),
        permalink = StateUrl(state),
    };

    /// <summary>
    /// R6 <c>timeline</c> for Luxembourg: the inventory of every publisher-dated state of one work,
    /// per language, in the reader's order (date, language, expression, digest). Each row is the same
    /// dated-state vocabulary <c>as_of</c> serves, so the two operations never describe one state in
    /// two ways. Nothing is selected, so nothing is refused for a date: two states on one date and
    /// language are both listed. Nothing is derived: no end date, no "in force", no gap or overlap
    /// label; <c>next_applicability_date</c> is the publisher's next dated state in the same language
    /// or <c>null</c>. The index holds no publication date, so none is served.
    /// </summary>
    public V3PlatformOperationOutcome Timeline(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "timeline", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus inventory operation only accepts timeline/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        // An EU act whose census the EU index holds is answered by the EU time view (V3CorpusMount.EuropeTime.cs); an EU
        // identifier it does not hold is unknown there (review of #913).
        if (LocateEuropeSeedForTime(request, identifier, observedAt, out var europeSeed) is { } refusedEurope)
        {
            return refusedEurope;
        }

        if (europeSeed is not null)
        {
            return TimelineEurope(request, identifier, europeSeed, requestedLanguage, observedAt);
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_timeline", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
        var rows = new List<object>(scope.Count);
        var datesByIdentity = ArticleDateMap(scope);
        var notAdmitted = NotAdmittedMap(scope);
        foreach (var state in scope)
        {
            rows.Add(StateRow(state, NextDateInLanguage(scope, state), datesByIdentity, notAdmitted));
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            history_begins = scope[0].ApplicabilityDate,
            state_count = rows.Count,
            states = rows,
            articles_not_admitted_note = ArticlesNotAdmittedNote,
            available_languages = availableLanguages,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "timeline", result.RootElement));
    }

    /// <summary>
    /// R6 <c>dossier</c> for Luxembourg: the work record the mounted index can honestly give for one work,
    /// and what it does not hold, said. The titles the publisher stated, by expression and language, with
    /// the digest of the evidence each was read from; the publisher-dated states of the work in
    /// <c>timeline</c>'s vocabulary (a state's article count in place of its article list); and a fixed
    /// list of what the index does not hold: a document type, a current-state flag, a publication date,
    /// an entry-into-force or application date, historical identifiers, a responsible ministry, an
    /// observation time, and any statement about gaps between states. Nothing is derived: the states are
    /// the ones <c>timeline</c> lists, refused as it refuses, and a title or a state absent here may exist
    /// at the publisher.
    /// <para>
    /// It does not say which states hold searchable text, and that is not because the count would be
    /// vacuous: every state has at least one article, but an article's searchable text may be empty, and
    /// the index knows the difference. It is held and not served, because counting it per state means
    /// reading the text of every article of every state (not measured), which is the wrong cost for a
    /// work record; <c>coverage</c> serves the corpus-level count. A future change may serve it, and it
    /// belongs beside the states, not on the list of what is not held, because it is held.
    /// </para>
    /// </summary>
    public V3PlatformOperationOutcome Dossier(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "dossier", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus work-record operation only accepts dossier/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var requestedLanguage = OptionalLanguage(request.Parameters);

        // A consolidated version's identifier (its work IRI, CELEX or expression) names its act's dossier, read through the
        // original work the EU census binds it to.
        if (LocateEuropeSeed(request, identifier, observedAt, out var europeSeed) is { } refusedSeed)
        {
            return refusedSeed;
        }

        if (europeSeed is not null && _europeReader!.ResolveExact(identifier).Count == 0 &&
            _europeReader.ResolveExact(europeSeed) is { Count: > 0 } original)
        {
            return DossierEurope(request, identifier, original, requestedLanguage, observedAt);
        }

        if (LocateEuropeWork(request, identifier, observedAt, out var europe) is { } refusedEurope)
        {
            return refusedEurope;
        }

        if (europe.Count > 0)
        {
            return DossierEurope(request, identifier, europe, requestedLanguage, observedAt);
        }

        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_dossier", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
        // A title row names the expression it was read for, which a state names too; the title table's own
        // key does not reliably name the work.
        var expressions = states.Select(static state => state.ExpressionIri)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var titles = _reader!.ResolveWorkTitles(expressions)
            .Where(title => requestedLanguage is null ||
                            string.Equals(title.Language, requestedLanguage, StringComparison.Ordinal))
            .GroupBy(static title => (title.Language, title.ExpressionIri))
            .Select(static group => new
            {
                language = group.Key.Language,
                expression_iri = group.Key.ExpressionIri,
                titles = group.Where(static title => title.TitleKind == "title")
                    .Select(static title => new { title = title.Title, evidence_sha256 = title.EvidenceSha256 })
                    .ToArray(),
                short_titles = group.Where(static title => title.TitleKind == "title_short")
                    .Select(static title => new { title = title.Title, evidence_sha256 = title.EvidenceSha256 })
                    .ToArray(),
            })
            .ToArray();
        var notAdmitted = NotAdmittedMap(scope);
        var rows = scope.Select(state => DossierStateRow(state, NextDateInLanguage(scope, state), notAdmitted)).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = DossierScope,
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            publisher_work_iri = states[0].PublisherWorkIri,
            available_languages = availableLanguages,
            titles,
            state_count = rows.Length,
            history_begins = scope[0].ApplicabilityDate,
            latest_applicability_date = scope[^1].ApplicabilityDate,
            states = rows,
            articles_not_admitted_note = ArticlesNotAdmittedNote,
            not_held = DossierNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "work_record", result.RootElement));
    }

    internal const string EuropeDossierScope =
        "the EU work as the mounted EU index holds it: its expressions, the one wording held of each with the Formex act date, the article count and " +
        "the corpus members the articles were read from; a record of this corpus and not of what the publisher holds, and a missing expression or " +
        "wording here is neither absent from the publisher's record nor absent from law; with a language, only that language's expressions are listed " +
        "and available_languages still lists every language the work has";

    internal static readonly string[][] EuropeDossierNotHeld =
    [
        ["titles", "no title of the EU work is held by the EU index"],
        ["later_wordings", "no consolidated version is held, so the wording listed is the original act's and no later wording or state is listed"],
        ["force_dates", "no entry-into-force, application or end-of-validity date is held; the wording date is none of them"],
        ["document_type", "the publisher's document type is not held for EU works"],
        ["corrigenda", "corrigendum lines are recorded by the index per corrected work root and are not joined to the work here, because the join between a corrected work root and the work's publisher identifier is not established"],
        ["other_languages", "an expression in a language the index holds no wording in is not listed; French expressions are not acquired (Decision 89)"],
        ["annexes", EuropeAnnexesNotHeldReason],
    ];

    /// <summary>
    /// <c>dossier</c> for an EU work: every expression of the work the EU index holds, with the Formex act
    /// date of its one held wording (named with its meaning, never as an applicability date), its article
    /// count and the corpus members its articles were read from, plus fixed words for what is not held. An
    /// identifier whose expressions belong to more than one EU work is <c>ambiguous_identifier</c>; a
    /// language the work holds no expression in is <c>language_not_available</c>; both with EU context.
    /// </summary>
    private V3PlatformOperationOutcome DossierEurope(
        V3PlatformOperationRequest request,
        string identifier,
        IReadOnlyList<EuropeIndexResolvedExpression> resolved,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(_europeReader);
        var works = resolved.Select(static expression => expression.PublisherWorkId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (works.Length > 1)
        {
            using var ambiguous = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                candidates = works,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
        }

        var expressions = _europeReader.ResolveWorkExpressions(works[0]);
        var languages = expressions.Select(static expression => expression.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (requestedLanguage is not null && !languages.Contains(requestedLanguage, StringComparer.Ordinal))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage,
                available_languages = languages,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        var listed = expressions
            .Where(expression => requestedLanguage is null || string.Equals(expression.Language, requestedLanguage, StringComparison.Ordinal))
            .ToArray();
        var dossierSeed = EuropeSeedsOf(works[0]) is [var heldSeed] ? heldSeed : null;
        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = EuropeDossierScope,
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            publisher = "eu-eurlex",
            publisher_work_id = works[0],
            celex = expressions[0].PublisherWorkCelex,
            available_languages = languages,
            expression_count = listed.Length,
            expressions = listed.Select(expression => (
                Expression: expression,
                Wording: EuropeWordingOf(expression.PublisherExpressionId),
                Annexes: EuropeAnnexesNotServed(
                    expression.Members.Select(static member => member.ObjectRefSha256),
                    expression.PublisherExpressionId,
                    () => EuropeOfficialSourceOf(EuropeArticlesOf(expression.PublisherExpressionId), works[0])))).Select(static entry => new
            {
                publisher_expression_id = entry.Expression.PublisherExpressionId,
                language = entry.Expression.Language,
                wording_dates = entry.Expression.WordingDates,
                article_count = entry.Expression.ArticleCount,
                members = entry.Expression.Members.Select(static member => new
                {
                    object_ref_sha256 = member.ObjectRefSha256,
                    outcome = member.Outcome,
                    content_class = member.ContentClass,
                }).ToArray(),
                // The annexes the corpus classified on those members: never served as text (EuropeAnnexesNotHeldReason).
                annexes_not_served = entry.Annexes,
                // The expression is an identifier EU resolve answers.
                resolve = new { identifier = entry.Expression.PublisherExpressionId },
                // The one held wording of the expression, pinned (the EU permalink grammar, as EU search pins it), or null
                // when the expression holds no single wording date to pin.
                pinned_wording = entry.Wording is not { } held ? null : new
                {
                    wording_date = held.WordingDate,
                    wording_sha256 = held.Sha256,
                    permalink = held.Permalink,
                },
            }).ToArray(),
            digest_rule = EuropeWordingDigestRule,
            date_semantics = EuropeWordingDateSemantics,
            wording_timeline = EuropeDossierTimeline(dossierSeed, requestedLanguage),
            consolidations_held = dossierSeed is not null && EuropeConsolidationsHeld(EuropeTimelineOf(dossierSeed)),
            not_held = EuropeNotHeldRows(EuropeDossierNotHeld, dossierSeed,
                "consolidated versions of this act are held: wording_timeline lists them, and timeline, as_of and evidence_bundle answer them; the expressions listed here are the original act's"),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "work_record", result.RootElement));
    }

    internal const string EuropeEvidenceBundleScope =
        "the evidence a reader needs to quote the original wording of an EU work the mounted EU index holds: for each held expression in the " +
        "served languages whose wording date is the requested date, the hash-pinned permalink and stable coordinate of that wording, the corpus " +
        "members its articles belong to with their retained body digests, and every article with its publisher id and heading, its text (the " +
        "text the index searches), the digest of that text (text_sha256), the digest of the corpus member's retained publisher body (body_sha256: " +
        "the manifestation the corpus holds for the expression, such as its XHTML or PDF), the digest of the Formex package the text was read from " +
        "(package_sha256) and of the package entry (source_entry_sha256), both null on an EU index that predates them, its official source and an " +
        "article permalink (the wording permalink and the publisher's provision id after #, which verify accepts); an article whose text is empty is " +
        "named under articles_without_text and is not served as a quote; the annexes the corpus classified on those members are listed under " +
        "annexes_not_served, each served as text not available with the official source to read it, and never quoted";

    internal const string EuropeEvidenceBundleDateRule =
        "the EU index holds one wording of each expression, the original act's, dated by its Formex act date; no consolidated version is held, so " +
        "that wording answers only its own date: a date before it, or after it, is refused no_version_for_date naming the held wording dates, and " +
        "the original wording is never served as the wording of a later date (driver decision on EU parity (b), PR #761)";

    internal const string EuropeEvidenceBundleRightsRule =
        "rights are enforced when the bundle is composed, before any text is read: every corpus member the served expression's articles come from " +
        "must have been acquired, which an EU build reaches only after retaining the Decision 95 rights receipt (Commission Decision 2011/833/EU, " +
        "fetched on the Publications Office route, never from eur-lex.europa.eu); under any other outcome the bundle refuses text_withheld and " +
        "names the official identity, the official link and the retained body digest; every text served carries the acknowledgement and the " +
        "authenticity statement Decision 95 requires";

    internal const string EuropeTextAcknowledgement = "© European Union, https://eur-lex.europa.eu";

    internal const string EuropeTextAuthenticity =
        "Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects (Regulation (EU) " +
        "No 216/2013, Article 1(2)); this text is a reproduction read from the Publications Office's Formex package, not the authentic edition.";

    internal static readonly string[][] EuropeEvidenceBundleNotHeld =
    [
        ["publisher_signature", "no signature or attestation of the publisher is held; the digests are this index's own reading of the retained package"],
        ["later_wordings", "no consolidated version is held, so only the original wording is served, and only for its own wording date"],
        ["force_dates", "no entry-into-force, application or end-of-validity date is held; the wording date is none of them"],
        ["observation_time", "when the publisher served the retained package is not held, so no observation time is stated"],
        ["markup_and_notes", "the Formex markup, notes and tables are not served as structure; the text is the article's searchable text, in publisher order"],
        ["annexes", EuropeAnnexesNotHeldReason],
    ];

    /// <summary>
    /// EU <c>evidence_bundle</c>: the original wording of each held expression of one EU work, quoted under the rights rule, for
    /// the requested date only when it is that wording's Formex act date (<see cref="EuropeEvidenceBundleDateRule"/>). Refuses
    /// as EU dossier does for an ambiguous work or a language the work does not hold.
    /// </summary>
    private V3PlatformOperationOutcome EvidenceBundleEurope(
        V3PlatformOperationRequest request,
        string identifier,
        IReadOnlyList<EuropeIndexResolvedExpression> resolved,
        string requestedDate,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(_europeReader);
        var works = resolved.Select(static expression => expression.PublisherWorkId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (works.Length > 1)
        {
            using var ambiguous = JsonSerializer.SerializeToDocument(new { requested_identifier = identifier, candidates = works });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
        }

        var expressions = _europeReader.ResolveWorkExpressions(works[0]);
        var languages = expressions.Select(static expression => expression.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (requestedLanguage is not null && !languages.Contains(requestedLanguage, StringComparer.Ordinal))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage,
                available_languages = languages,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        // The one pinned wording of each expression in the served languages; an expression with no single wording date has
        // no wording a permalink can pin, so it is not quoted.
        var wordings = expressions
            .Where(expression => requestedLanguage is null || string.Equals(expression.Language, requestedLanguage, StringComparison.Ordinal))
            .Select(expression => EuropeWordingOf(expression.PublisherExpressionId))
            .OfType<(string Celex, string WorkId, string ExpressionId, string Language, string WordingDate, IReadOnlyList<string> Provisions, string Sha256, string Permalink)>()
            .OrderBy(static wording => wording.Language, StringComparer.Ordinal)
            .ThenBy(static wording => wording.ExpressionId, StringComparer.Ordinal)
            .ToArray();
        if (wordings.Length == 0)
        {
            using var unavailable = JsonSerializer.SerializeToDocument(new
            {
                official_identity = works[0],
                official_source = works[0],
                retained_transport_evidence = "none",
                requested_language = requestedLanguage,
                // No held expression has a single wording date a permalink could pin.
                what_would_answer = new[] { "new_official_observation" },
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "text_not_available", unavailable.RootElement));
        }

        // The date rule: only a wording's own date answers.
        var selected = wordings.Where(wording => string.Equals(wording.WordingDate, requestedDate, StringComparison.Ordinal)).ToArray();
        if (selected.Length == 0)
        {
            var dates = wordings.Select(static wording => wording.WordingDate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var noVersion = JsonSerializer.SerializeToDocument(new
            {
                requested_date = requestedDate,
                history_begins = dates[0],
                nearest_earlier = dates.LastOrDefault(date => string.CompareOrdinal(date, requestedDate) < 0),
                nearest_later = dates.FirstOrDefault(date => string.CompareOrdinal(date, requestedDate) > 0),
                what_would_answer = new[] { "new_official_observation" },
                asserts_absence_of_law = false,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "no_version_for_date", noVersion.RootElement));
        }

        // ---- Rights at compose time, per selected wording, before any text is served. ----
        var articlesByExpression = new Dictionary<string, IReadOnlyList<EuropeIndexArticleText>>(StringComparer.Ordinal);
        foreach (var wording in selected)
        {
            var articles = _europeReader.ResolveExpressionArticles(wording.ExpressionId);
            articlesByExpression[wording.ExpressionId] = articles;
            var members = articles.Select(static article => article.ObjectRefSha256).Distinct(StringComparer.Ordinal)
                .Select(objectRef => (ObjectRef: objectRef, Member: EuropeMemberOf(objectRef)))
                .ToArray();
            var blocking = members.FirstOrDefault(static entry => entry.Member is null || entry.Member.Outcome != LexCorpus6OutcomeKind.Acquired);
            if (articles.Count == 0 || blocking.ObjectRef is not null)
            {
                using var withheld = JsonSerializer.SerializeToDocument(new
                {
                    official_identity = wording.ExpressionId,
                    official_link = articles.Select(static article => article.OfficialSourceUri).FirstOrDefault(static uri => uri is not null) ?? wording.WorkId,
                    content_sha256 = blocking.Member?.BodySha256 ?? wording.Sha256,
                    stable_coordinate = EuropeStableCoordinate(wording.Permalink),
                    permalink = wording.Permalink,
                    language = wording.Language,
                    source_outcome = blocking.Member is null ? null : ContractWire.NameOf(blocking.Member.Outcome),
                    rule = EuropeEvidenceBundleRightsRule,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.EuEurLex),
                    new V3PlatformOperationRefusal(request, "text_withheld", withheld.RootElement));
            }
        }

        var bundles = selected.Select(wording =>
        {
            var articles = articlesByExpression[wording.ExpressionId];
            var quoted = articles.Where(static article => article.Text.Length > 0).ToArray();
            return new
            {
                publisher_expression_id = wording.ExpressionId,
                language = wording.Language,
                wording_date = wording.WordingDate,
                wording_sha256 = wording.Sha256,
                stable_coordinate = EuropeStableCoordinate(wording.Permalink),
                permalink = wording.Permalink,
                sources = articles.Select(static article => article.ObjectRefSha256).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    .Select(objectRef => EuropeMemberOf(objectRef)!)
                    .Select(static member => new
                    {
                        object_ref_sha256 = member.ObjectRefSha256,
                        outcome = ContractWire.NameOf(member.Outcome),
                        body_sha256 = member.BodySha256,
                        body_byte_length = member.BodyByteLength,
                        body_receipt_sha256 = member.BodyReceiptSha256,
                    })
                    .ToArray(),
                articles = quoted.Select(article =>
                {
                    var textBytes = Encoding.UTF8.GetBytes(article.Text);
                    var textSha256 = Convert.ToHexStringLower(SHA256.HashData(textBytes));
                    if (article.TextSha256 is not null && !string.Equals(article.TextSha256, textSha256, StringComparison.Ordinal))
                    {
                        // The index records the digest of the very text it searches; a difference is a damaged index, never a quote.
                        throw new InvalidDataException($"The EU index's text digest for article {article.ArticleIdentitySha256} is not its text's.");
                    }

                    return new
                    {
                        article_identity_sha256 = article.ArticleIdentitySha256,
                        publisher_id = article.PublisherIdentifier,
                        heading = article.Heading,
                        language = article.Language,
                        text = article.Text,
                        text_sha256 = textSha256,
                        text_byte_length = textBytes.Length,
                        body_sha256 = EuropeMemberOf(article.ObjectRefSha256)!.BodySha256,
                        package_entry = article.PackageEntry,
                        package_sha256 = article.PackageSha256,
                        source_entry_sha256 = article.SourceEntrySha256,
                        official_source = article.OfficialSourceUri ?? wording.WorkId,
                        article_permalink = EuropeProvisionPermalink(wording.Permalink, article.PublisherIdentifier),
                        provision_coordinate = wording.ExpressionId + "#lex-provision=" + Uri.EscapeDataString(article.PublisherIdentifier),
                    };
                }).ToArray(),
                articles_without_text = articles.Where(static article => article.Text.Length == 0).Select(static article => new
                {
                    article_identity_sha256 = article.ArticleIdentitySha256,
                    publisher_id = article.PublisherIdentifier,
                }).ToArray(),
                annexes_not_served = EuropeAnnexesNotServed(
                    articles.Select(static article => article.ObjectRefSha256), wording.ExpressionId,
                    () => EuropeOfficialSourceOf(articles, wording.WorkId)),
            };
        }).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = EuropeEvidenceBundleScope,
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            publisher = EuropePermalinkPublisher,
            publisher_work_id = works[0],
            celex = selected[0].Celex,
            available_languages = languages,
            served_languages = selected.Select(static wording => wording.Language).Distinct(StringComparer.Ordinal).ToArray(),
            wordings = bundles,
            acknowledgement = EuropeTextAcknowledgement,
            authenticity = EuropeTextAuthenticity,
            rights_rule = EuropeEvidenceBundleRightsRule,
            date_rule = EuropeEvidenceBundleDateRule,
            date_semantics = EuropeWordingDateSemantics,
            digest_rule = EuropeWordingDigestRule,
            consolidations_held = false,
            not_held = EuropeEvidenceBundleNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "evidence_bundle", result.RootElement));
    }

    internal const string DossierScope =
        "The titles and the publisher-dated states the mounted index holds for this work. It is a record of what this corpus holds and not of what the publisher holds: " +
        "a title or a state absent here may exist at the publisher, and absence from this corpus is neither absence from the publisher's record nor absence of law. " +
        "When a language is requested, the titles, the states, the state count and the first and latest applicability dates are that language's alone, and available_languages still lists every language the work has.";

    /// <summary>What the mounted index does not hold about a work, in fixed words; not computed from what is present.</summary>
    internal static readonly string[][] DossierNotHeld =
    [
        ["document_type", "the publisher's document type is not part of this record; classification serves the typeDocument facts the publisher asserted, verbatim"],
        ["current_state_flag", "no current-state flag is part of this record; status_on serves the publisher's force assertions for a date, and a flag about now would not be a statement about any date listed here"],
        ["publication_date", "no publication date is held; the document date the index stores beside a title falls back to an article's applicability date, so it is not served as one"],
        ["entry_into_force", "no entry-into-force date is part of this record; status_on serves the publisher's dateEntryInForce when asserted; a state's applicability date is the date that state applies from, which is a different fact"],
        ["application", "no application date is held; a state's applicability date is the date that state applies from, which is a different fact"],
        ["historical_identifiers", "no historical identifier is held, so no earlier or later identifier of this work is mapped to it"],
        ["responsible_ministry", "no responsible ministry is held"],
        ["first_observed", "no observation time is held, so nothing here says when this work was first seen; the event log's first_sighting (events) says only that a state is first present in that log"],
        ["coverage_gaps", "each state carries the date it applies from and no end date, so no gap between states can be stated, and this answer never says there is none"],
    ];

    /// <summary>
    /// One state as <c>dossier</c> lists it: <c>timeline</c>'s row without the article list, with the count.
    /// The fields both operations carry are the same fields with the same values, and a test holds it.
    /// </summary>
    private static object DossierStateRow(
        LuxembourgIndexResolvedState state, string? nextDate, IReadOnlyDictionary<string, long> notAdmittedByState) => new
    {
        language = state.Language,
        applicability_date = state.ApplicabilityDate,
        next_applicability_date = nextDate,
        state_sha256 = state.StateSha256,
        expression_iri = state.ExpressionIri,
        publisher_work_iri = state.PublisherWorkIri,
        publisher_legal_resource_iri = state.PublisherLegalResourceIri,
        article_count = state.ArticleIdentities.Count,
        articles_not_admitted = notAdmittedByState[state.StateSha256],
        stable_coordinate = StableCoordinate(state),
        permalink = StateUrl(state),
    };

    /// <summary>
    /// R6 <c>article_history</c> for Luxembourg: the lineage of one publisher-minted article id
    /// through the publisher-dated states of one work, per language. One row per state that carries
    /// the anchor, in the reader's order; the states that do not carry it are listed as absent, so a
    /// lineage that begins after the work does is visible and never implied to be the whole history.
    /// "The wording changed" is byte equality of the article's merged text and its references (label
    /// and target) between consecutive rows of one language and nothing looser: a changed apostrophe
    /// or a retargeted reference is a new wording; a note or a modification marker is the publisher's
    /// apparatus and is not; a paragraph or inline-formatting boundary is not a word and is not seen.
    /// The rule travels with the answer. Nothing is derived: no end date, no "in force", no repeal, no diff text.
    /// </summary>
    public V3PlatformOperationOutcome ArticleHistory(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "article_history", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus lineage operation only accepts article_history/1.");
        }

        var identifier = RequiredString(request.Parameters, "identifier");
        var anchor = RequiredString(request.Parameters, "anchor");
        var requestedLanguage = OptionalLanguage(request.Parameters);
        if (RefuseUnlessWorkStates(request, identifier, observedAt, "r6_article_history", requestedLanguage,
                out var states, out var availableLanguages) is { } refused)
        {
            return refused;
        }

        var scope = requestedLanguage is null
            ? states
            : states.Where(state => string.Equals(state.Language, requestedLanguage, StringComparison.Ordinal))
                .ToArray();
        var carried = _reader!.ResolveAnchorArticles(scope.Select(static state => state.StateSha256).ToArray(), anchor)
            .GroupBy(static article => article.StateSha256, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

        var rows = new List<object>();
        var absent = new List<object>();
        string? historyBegins = null;
        var previousWording = new Dictionary<string, string>(StringComparer.Ordinal);
        var runs = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var distinct = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var state in scope)
        {
            if (!carried.TryGetValue(state.StateSha256, out var articles))
            {
                absent.Add(new
                {
                    language = state.Language,
                    applicability_date = state.ApplicabilityDate,
                    state_sha256 = state.StateSha256,
                    permalink = StateUrl(state),
                });
                continue;
            }

            // The wording identity of this row: the token-stream digests of every article carrying the
            // anchor, in identity order (one, unless the publisher minted the id twice in one state).
            var wording = string.Join("\n", articles.Select(static article => article.WordingSha256));
            var changed = previousWording.TryGetValue(state.Language, out var previous) &&
                          !string.Equals(previous, wording, StringComparison.Ordinal);
            if (!previousWording.ContainsKey(state.Language) || changed)
            {
                runs[state.Language] = runs.GetValueOrDefault(state.Language) + 1;
            }
            if (!distinct.TryGetValue(state.Language, out var seen))
            {
                distinct[state.Language] = seen = new HashSet<string>(StringComparer.Ordinal);
            }
            seen.Add(wording);
            previousWording[state.Language] = wording;
            historyBegins ??= state.ApplicabilityDate;

            rows.Add(new
            {
                language = state.Language,
                applicability_date = state.ApplicabilityDate,
                next_applicability_date = NextDateInLanguage(scope, state),
                state_sha256 = state.StateSha256,
                stable_coordinate = StableCoordinate(state),
                permalink = StateUrl(state),
                articles = articles.Select(article => new
                {
                    article_identity_sha256 = article.ArticleIdentitySha256,
                    publisher_id = article.PublisherId,
                    publisher_wid = article.PublisherWid,
                    article_valid_from = article.ApplicabilityDate,
                    validity_conflict = ValidityConflict(article.ApplicabilityDate, state.ApplicabilityDate),
                    wording_sha256 = article.WordingSha256,
                }).ToArray(),
                wording_changed = changed,
            });
        }

        if (rows.Count == 0)
        {
            using var notInVersion = JsonSerializer.SerializeToDocument(new
            {
                requested_anchor = anchor,
                nearest_anchors = NearestAnchors(_reader.ResolveArticleIds(scope[^1].StateSha256), anchor),
                do_not_fall_back_to_full_text_search = true,
                what_would_answer = AnchorNotInVersionRoutes,
                asserts_absence_of_law = false,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "anchor_not_in_version", notInVersion.RootElement));
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            requested_anchor = anchor,
            requested_language = requestedLanguage,
            publisher = "lu-legilux",
            work_key = states[0].WorkKey,
            history_begins = historyBegins,
            wording_rule = WordingRule,
            wording_runs = runs.Select(static pair => new { language = pair.Key, count = pair.Value }).ToArray(),
            distinct_wordings = distinct.Select(static pair => new { language = pair.Key, count = pair.Value.Count }).ToArray(),
            states = rows,
            absent_in_states = absent,
            available_languages = availableLanguages,
            validity_conflict_rule = ValidityConflictRule,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "provision_history", result.RootElement));
    }

    private const string WordingRule =
        "wording_sha256 is the SHA-256 of a canonical JSON array of [kind, text, target] for the Text and Reference tokens of the article's stored token stream, in order, with consecutive text merged into one entry (reference labels and targets included; note references, note bodies and modification markers excluded); wording_changed is true when it differs from the previous state of the same language, and any byte difference in the merged text or in a reference counts; paragraph and inline-formatting boundaries and whitespace-only nodes are not compared; wording_runs counts the first state and every change per language; distinct_wordings counts distinct digests per language";

    /// <summary>
    /// The publisher ids of one state that share the longest non-empty common prefix with the
    /// requested anchor, at most ten, in ordinal order. When none shares a prefix, the first ten ids
    /// of that state in ordinal order, because the reviewed refusal contract always names a
    /// neighbourhood (an empty list is refused by the envelope). A stated rule, not a search: nothing
    /// is matched by content.
    /// </summary>
    private static string[] NearestAnchors(IReadOnlyList<string> anchors, string requested)
    {
        var scored = anchors
            .Select(anchor => (Anchor: anchor, Prefix: CommonPrefixLength(anchor, requested)))
            .Where(static pair => pair.Prefix > 0)
            .ToArray();
        if (scored.Length == 0)
        {
            return anchors.Order(StringComparer.Ordinal).Take(10).ToArray();
        }

        var longest = scored.Max(static pair => pair.Prefix);
        return scored
            .Where(pair => pair.Prefix == longest)
            .Select(static pair => pair.Anchor)
            .Order(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = 0;
        while (length < left.Length && length < right.Length && left[length] == right[length])
        {
            length++;
        }
        return length;
    }

    /// <summary>
    /// The next publisher-dated state in the same language as <paramref name="state"/> within the
    /// served scope, or <c>null</c>: a date on which another language's text changes never bounds
    /// this one.
    /// </summary>
    private static string? NextDateInLanguage(IReadOnlyList<LuxembourgIndexResolvedState> scope, LuxembourgIndexResolvedState state) =>
        scope
            .Where(other => string.Equals(other.Language, state.Language, StringComparison.Ordinal) &&
                            string.CompareOrdinal(other.ApplicabilityDate, state.ApplicabilityDate) > 0)
            .Select(static other => other.ApplicabilityDate)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// Locates the Luxembourg work a temporal operation names, or returns the refusal that stands in
    /// for it: <c>no_corpus_mounted</c> or the mode refusal on a mount without the Luxembourg index,
    /// <c>identifier_unknown</c> or the mode refusal for a work the index does not hold, and
    /// <c>language_not_available</c> for a language the work has no state in. Attribution follows the
    /// identifier, never the mount. Returns <c>null</c> when the states are served.
    /// </summary>
    private V3PlatformOperationOutcome? RefuseUnlessWorkStates(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt,
        string requestedMode,
        string? requestedLanguage,
        out IReadOnlyList<LuxembourgIndexResolvedState> states,
        out string[] availableLanguages)
    {
        states = Array.Empty<LuxembourgIndexResolvedState>();
        availableLanguages = Array.Empty<string>();
        var isStableWorkCoordinate = TryParseStableWorkCoordinate(identifier, out var workKey);
        if (_reader is null)
        {
            // A Luxembourg-shaped identifier on a mount without the Luxembourg index is Luxembourg law
            // whose corpus is not mounted; an EU-shaped identifier is refused the mode with EU context;
            // an identifier with no publisher shape is unknown with Luxembourg context on every mount,
            // as it is on a mounted index below, so the mount never decides the attribution.
            if (isStableWorkCoordinate || IsLuxembourgShaped(identifier))
            {
                using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.LuLegilux),
                    new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
            }

            return IsEuropeanUnionShaped(identifier)
                ? ModeUnavailable(request, observedAt, PublisherId.EuEurLex, requestedMode)
                : Unknown(request, identifier, observedAt, PublisherId.LuLegilux, ShapelessIdentifierWhatWouldAnswer);
        }

        var workIdentifier = isStableWorkCoordinate ? workKey : identifier;
        var located = _reader.ResolveWorkStates(workIdentifier);
        if (located.Count == 0)
        {
            return IsEuropeanUnionShaped(identifier)
                ? ModeUnavailable(request, observedAt, PublisherId.EuEurLex, requestedMode)
                : Unknown(request, identifier, observedAt, PublisherId.LuLegilux,
                    isStableWorkCoordinate || IsLuxembourgShaped(identifier)
                        ? "a Luxembourg work identifier present in the mounted index"
                        : ShapelessIdentifierWhatWouldAnswer);
        }

        var languages = located.Select(static state => state.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (requestedLanguage is not null && !languages.Contains(requestedLanguage, StringComparer.Ordinal))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage,
                available_languages = languages,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(
                    request, "language_not_available", unavailableLanguage.RootElement));
        }

        states = located;
        availableLanguages = languages;
        return null;
    }

    /// <summary>
    /// One publisher-dated state as every temporal operation serves it. The publisher's dates and
    /// digests as stored; the article-level dates and the conflict flag from the same columns; the
    /// stable coordinate and the hash-pinned permalink. <paramref name="nextDate"/> is the next
    /// publisher-dated state in the same language, or <c>null</c>: never an inferred end.
    /// </summary>
    private object StateRow(LuxembourgIndexResolvedState state, string? nextDate) =>
        StateRow(state, nextDate, ArticleDateMap([state]), NotAdmittedMap([state]));

    /// <summary>
    /// How many articles of each state's document the corpus recorded and the state does not hold, read in one
    /// query for the states asked for.
    /// </summary>
    private IReadOnlyDictionary<string, long> NotAdmittedMap(IReadOnlyList<LuxembourgIndexResolvedState> states) =>
        _reader!.ResolveArticlesNotAdmitted(states.Select(static state => state.StateSha256).ToArray());

    private static object StateRow(
        LuxembourgIndexResolvedState state,
        string? nextDate,
        IReadOnlyDictionary<string, string?> datesByIdentity,
        IReadOnlyDictionary<string, long> notAdmittedByState)
    {
        var dates = ArticleDates(state, datesByIdentity);
        return new
        {
            language = state.Language,
            applicability_date = state.ApplicabilityDate,
            next_applicability_date = nextDate,
            state_sha256 = state.StateSha256,
            expression_iri = state.ExpressionIri,
            publisher_work_iri = state.PublisherWorkIri,
            publisher_legal_resource_iri = state.PublisherLegalResourceIri,
            article_identities = state.ArticleIdentities,
            articles = dates.Articles,
            articles_not_admitted = notAdmittedByState[state.StateSha256],
            validity_conflict_count = dates.ConflictCount,
            validity_conflict_rule = ValidityConflictRule,
            stable_coordinate = StableCoordinate(state),
            permalink = StateUrl(state),
        };
    }

    private static string? OptionalLanguage(JsonElement parameters) =>
        parameters.TryGetProperty("language", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public void Dispose()
    {
        _reader?.Dispose();
        _europeReader?.Dispose();
        foreach (var generation in _generationReaders) generation.Reader.Dispose();
    }

    /// <summary>
    /// The newest retained generation holding a state of this work, date and language with this digest, and the state as it
    /// holds it; null when none does.
    /// </summary>
    private (GenerationReader Generation, LuxembourgIndexResolvedState State)? FromGeneration(
        string workKey, string applicabilityDate, string? language, string stateSha256)
    {
        foreach (var generation in _generationReaders)
        {
            var state = generation.Reader.ResolveState(workKey, applicabilityDate).FirstOrDefault(candidate =>
                (language is null || string.Equals(candidate.Language, language, StringComparison.Ordinal)) &&
                string.Equals(candidate.StateSha256, stateSha256, StringComparison.Ordinal));
            if (state is not null)
            {
                return (generation, state);
            }
        }

        return null;
    }

    private static string RequiredString(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                $"The operation request lacks a usable '{name}'.");

    private V3PlatformOperationOutcome ModeUnavailable(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        PublisherId publisher,
        string requestedMode)
    {
        using var unavailable = JsonSerializer.SerializeToDocument(new
        {
            requested_mode = requestedMode,
            available_modes = new[] { "r0_exact_coordinate" },
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, publisher),
            new V3PlatformOperationRefusal(request, "retrieval_mode_unavailable", unavailable.RootElement));
    }

    private const string ShapelessIdentifierWhatWouldAnswer =
        "a Luxembourg work identifier (the stable coordinate /lu-legilux/{work_key} on this origin, or the publisher's work IRI) or an EU coordinate; this identifier has no publisher shape";

    /// <summary>The identifier forms EUR-Lex, the Publications Office and this product mint for EU law.</summary>
    private static bool IsEuropeanUnionShaped(string identifier) =>
        OfficialIdentifier.EliMintedBy(identifier) == PublisherId.EuEurLex ||
        OfficialIdentifier.ProfileOf(identifier) is not null ||
        IsEuropeanUnionPublisherAddress(identifier) ||
        IsEuropeCoordinate(identifier);

    /// <summary>This service's origin, under which its EU coordinates may also be written.</summary>
    private const string EuropeCoordinateOrigin = "https://law.soufien.lu";

    /// <summary>
    /// This service's own EU coordinates: a wording's permalink or stable coordinate (<c>/eu-eurlex/…</c>), as a path or under
    /// its origin. They name EU law, so they are EU-shaped (review of #913: they were answered as having no publisher shape).
    /// </summary>
    private static bool IsEuropeCoordinate(string identifier) =>
        identifier.StartsWith("/" + EuropePermalinkPublisher + "/", StringComparison.Ordinal) ||
        identifier.StartsWith(EuropeCoordinateOrigin + "/" + EuropePermalinkPublisher + "/", StringComparison.Ordinal);

    /// <summary>The identifier forms this product and Legilux mint for Luxembourg law.</summary>
    private static bool IsLuxembourgShaped(string identifier) =>
        OfficialIdentifier.EliMintedBy(identifier) == PublisherId.LuLegilux ||
        TryParsePinnedPermalink(identifier, out _, out _, out _, out _) ||
        (Uri.TryCreate(identifier, UriKind.Absolute, out var uri) && IsLegiluxHost(uri.Host));

    /// <summary>
    /// The publisher's host or one of its subdomains, on a dot boundary: <c>legilux.public.lu</c> and
    /// <c>data.legilux.public.lu</c> are the publisher; <c>notlegilux.public.lu</c> is not (the rule
    /// #678 settled for <c>europa.eu</c>).
    /// </summary>
    private static bool IsLegiluxHost(string host) =>
        string.Equals(host, "legilux.public.lu", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".legilux.public.lu", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The stable work coordinate <c>/lu-legilux/{work_key}</c>, on this origin or as a bare path.
    /// Two segments only; the three-segment hash-pinned permalink is a different grammar.
    /// </summary>
    private static bool TryParseStableWorkCoordinate(string value, out string workKey)
    {
        workKey = string.Empty;
        string path;
        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            path = value;
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
                 string.Equals(uri.Host, "law.soufien.lu", StringComparison.Ordinal) &&
                 uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 &&
                 uri.Fragment.Length == 0)
        {
            path = uri.AbsolutePath;
        }
        else
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 ||
            !string.Equals(segments[0], "lu-legilux", StringComparison.Ordinal) ||
            !IsWorkKey(segments[1]) ||
            !string.Equals(path, "/lu-legilux/" + segments[1], StringComparison.Ordinal))
        {
            return false;
        }

        workKey = segments[1];
        return true;
    }

    /// <summary>
    /// The one conflict rule (B34-L0143), used by every answer that carries an article date: a stated
    /// article date that differs from the state's date; a blank date is never a conflict.
    /// </summary>
    private static bool ValidityConflict(string? articleDate, string stateDate) =>
        articleDate is not null && !string.Equals(articleDate, stateDate, StringComparison.Ordinal);

    /// <summary>
    /// The pack's per-state rule (B34-L0143: the conflict is computed against the version date). It is
    /// not V2's rule, which compared the article date with the state where that wording run began and
    /// which this index cannot reconstruct; the rule text travels with every answer so a consumer can
    /// tell the two apart. Both dates are the publisher's; neither is resolved or preferred.
    /// </summary>
    private const string ValidityConflictRule =
        "article_valid_from is the publisher's article-level applicability date; validity_conflict is true when it is stated and differs from the state's applicability_date";

    private (IReadOnlyList<object> Articles, int ConflictCount) ArticleDates(LuxembourgIndexResolvedState state) =>
        ArticleDates(state, ArticleDateMap([state]));

    /// <summary>
    /// The publisher's article-level dates of every article of the given states, read in one query
    /// (#685 remark 2: one round trip per answer, not per state). An identity the index does not hold
    /// maps to <c>null</c>, as the reader reports it.
    /// </summary>
    private IReadOnlyDictionary<string, string?> ArticleDateMap(IReadOnlyList<LuxembourgIndexResolvedState> states)
    {
        var identities = states.SelectMany(static state => state.ArticleIdentities)
            .Distinct(StringComparer.Ordinal).ToArray();
        return _reader!.ResolveArticleDates(identities)
            .ToDictionary(static date => date.ArticleIdentitySha256, static date => date.ApplicabilityDate, StringComparer.Ordinal);
    }

    private static (IReadOnlyList<object> Articles, int ConflictCount) ArticleDates(
        LuxembourgIndexResolvedState state, IReadOnlyDictionary<string, string?> datesByIdentity)
    {
        var conflicts = 0;
        var articles = new List<object>(state.ArticleIdentities.Count);
        foreach (var identity in state.ArticleIdentities)
        {
            var date = datesByIdentity.GetValueOrDefault(identity);
            var conflict = ValidityConflict(date, state.ApplicabilityDate);
            if (conflict)
            {
                conflicts++;
            }

            articles.Add(new
            {
                article_identity_sha256 = identity,
                article_valid_from = date,
                validity_conflict = conflict,
            });
        }

        return (articles, conflicts);
    }

    private static string StableCoordinate(LuxembourgIndexResolvedState state) =>
        $"/lu-legilux/{state.WorkKey}/{state.ApplicabilityDate}";

    private static string StateUrl(LuxembourgIndexResolvedState state) =>
        StableCoordinate(state) + "--" + state.StateSha256;

    /// <summary>
    /// The dated stable coordinate this mount writes (<c>/lu-legilux/{work}/{date}</c>, also under the
    /// origin), as distinct from the work coordinate (two segments) and the pinned permalink (the
    /// date followed by <c>--</c> and the digest).
    /// </summary>
    private static bool TryParseStableStateCoordinate(string value, out string workKey, out string applicabilityDate)
    {
        workKey = string.Empty;
        applicabilityDate = string.Empty;
        string path;
        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            path = value;
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
                 string.Equals(uri.Host, "law.soufien.lu", StringComparison.Ordinal) &&
                 uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 &&
                 uri.Fragment.Length == 0)
        {
            path = uri.AbsolutePath;
        }
        else
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (path.EndsWith("/", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal) ||
            segments.Length != 3 ||
            !string.Equals(segments[0], "lu-legilux", StringComparison.Ordinal) ||
            !IsWorkKey(segments[1]) || segments[2].Length != 10 ||
            !DateOnly.TryParseExact(
                segments[2], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            return false;
        }

        workKey = segments[1];
        applicabilityDate = segments[2];
        return true;
    }

    /// <summary>
    /// The pinned permalink with an optional article fragment (<c>permalink#publisher_id</c>), the
    /// article permalink <c>evidence_bundle</c> writes. The fragment is the publisher's article id,
    /// verbatim; an empty fragment is not a permalink. Only <c>verify</c> reads this form: the
    /// other operations take the state permalink and no fragment.
    /// </summary>
    /// <summary>The publisher segment of the EU permalink grammar.</summary>
    private const string EuropePermalinkPublisher = "eu-eurlex";

    /// <summary>The domain the EU wording digest is computed under.</summary>
    private const string EuropeWordingDigestDomain = "lex-v3-eu-wording/1";

    internal const string EuropeWordingDigestRule =
        "the SHA-256, under the domain lex-v3-eu-wording/1, of the work's CELEX, the publisher's work and expression IRIs, the language, the wording date " +
        "and every article identity of the expression in the publisher's article order, each field written as a 4-byte big-endian length and its UTF-8 " +
        "bytes; each article identity is itself the SHA-256 of that article's text and tokens, so the digest pins the whole held wording, computed here " +
        "from the EU index, which stores no wording digest";

    internal const string EuropeVerifyScope =
        "whether a hash-pinned EU provision permalink (/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}, with the publisher's provision " +
        "id after #) still names the one wording this EU index holds of that expression, by the wording digest computed from the index (a matching digest " +
        "is digest_matches; a digest the wording no longer has is the pinned_digest_mismatch refusal naming the current one); the provision, when named, " +
        "must be one the wording holds or the answer is anchor_not_in_version; nothing about the text or its legal effect is assessed";

    internal static readonly string[][] EuropeVerifyNotHeld =
    [
        ["publisher_signature", "no signature or attestation of the publisher is held; the digest is this index's own reading of the retained wording"],
        ["later_wordings", "no consolidated version is held, so only the one original wording of the expression can be pinned"],
        ["text_verification", "the text itself is not compared here; each article identity is a digest of its text and tokens"],
    ];

    /// <summary>
    /// The one held wording of an EU expression and its pin: the work, CELEX, language and wording date, the
    /// provisions, the wording digest (<see cref="EuropeWordingDigestRule"/>) and the permalink without a provision.
    /// Null when the index holds no single wording date for the expression, which no permalink can pin.
    /// </summary>
    private (string Celex, string WorkId, string ExpressionId, string Language, string WordingDate, IReadOnlyList<string> Provisions, string Sha256, string Permalink)?
        EuropeWordingOf(string publisherExpressionId)
    {
        ArgumentNullException.ThrowIfNull(_europeReader);
        var resolved = _europeReader.ResolveExact(publisherExpressionId)
            .SingleOrDefault(candidate => string.Equals(candidate.PublisherExpressionId, publisherExpressionId, StringComparison.Ordinal));
        if (resolved is null)
        {
            return null;
        }

        var expression = _europeReader.ResolveWorkExpressions(resolved.PublisherWorkId)
            .SingleOrDefault(candidate => string.Equals(candidate.PublisherExpressionId, publisherExpressionId, StringComparison.Ordinal));
        if (expression is null || expression.WordingDates.Count != 1)
        {
            return null;
        }

        var wordingDate = expression.WordingDates[0];
        var sha256 = EuropeWordingSha256(
            expression.PublisherWorkCelex, resolved.PublisherWorkId, publisherExpressionId, resolved.Language, wordingDate, resolved.ArticleIdentities);
        return (expression.PublisherWorkCelex, resolved.PublisherWorkId, publisherExpressionId, resolved.Language, wordingDate,
            resolved.PublisherProvisionIdentifiers, sha256,
            $"/{EuropePermalinkPublisher}/{expression.PublisherWorkCelex}/{resolved.Language}/{wordingDate}--{sha256}");
    }

    /// <summary>The wording digest, as <see cref="EuropeWordingDigestRule"/> states it.</summary>
    private static string EuropeWordingSha256(
        string celex, string publisherWorkId, string publisherExpressionId, string language, string wordingDate, IReadOnlyList<string> articleIdentities)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        foreach (var field in new[] { EuropeWordingDigestDomain, celex, publisherWorkId, publisherExpressionId, language, wordingDate })
        {
            Append(field);
        }

        foreach (var article in articleIdentities)
        {
            Append(article);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The EU stable coordinate, <c>/eu-eurlex/{celex}/{language}/{wording date}</c>: the wording's permalink without its digest.</summary>
    private static string EuropeStableCoordinate(string wordingPermalink) => wordingPermalink[..^(2 + 64)];

    /// <summary>A provision's permalink: the wording's, with the publisher's provision id (escaped) after #.</summary>
    private static string EuropeProvisionPermalink(string wordingPermalink, string provision) =>
        wordingPermalink + "#" + Uri.EscapeDataString(provision);

    /// <summary>
    /// Parses an EU permalink, <c>/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}</c> with an optional
    /// provision after #, as a path or under the product's own https origin.
    /// </summary>
    private static bool TryParseEuropePermalink(
        string value,
        out string celex,
        out string language,
        out string wordingDate,
        out string requestedDigest,
        out string? provision)
    {
        celex = language = wordingDate = requestedDigest = string.Empty;
        provision = null;
        var hash = value.IndexOf('#', StringComparison.Ordinal);
        var path = hash < 0 ? value : value[..hash];
        if (hash >= 0)
        {
            var fragment = value[(hash + 1)..];
            if (fragment.Length == 0 || fragment.Any(static character => char.IsWhiteSpace(character) || character == '#'))
            {
                return false;
            }

            provision = Uri.UnescapeDataString(fragment);
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
                !string.Equals(uri.Host, "law.soufien.lu", StringComparison.Ordinal) ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0)
            {
                return false;
            }

            path = uri.AbsolutePath;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4 ||
            !string.Equals(segments[0], EuropePermalinkPublisher, StringComparison.Ordinal) ||
            segments[1].Length == 0 || !segments[1].All(static character => char.IsAsciiLetterOrDigit(character) || character is '(' or ')' or '_' or '-' or '.') ||
            segments[2].Length != 3 || !segments[2].All(char.IsAsciiLetterLower) ||
            segments[3].Length != 10 + 2 + 64 ||
            !string.Equals(segments[3].Substring(10, 2), "--", StringComparison.Ordinal) ||
            !DateOnly.TryParseExact(segments[3][..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !segments[3][12..].All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            provision = null;
            return false;
        }

        (celex, language, wordingDate, requestedDigest) = (segments[1], segments[2], segments[3][..10], segments[3][12..]);
        return true;
    }

    /// <summary>
    /// <c>verify</c> for an EU permalink: the one wording the EU index holds of the work's expression in that language,
    /// its digest recomputed, and the provision checked when named.
    /// </summary>
    private V3PlatformOperationOutcome VerifyEurope(
        V3PlatformOperationRequest request,
        string identifier,
        string celex,
        string language,
        string wordingDate,
        string requestedDigest,
        string? provision,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        if (_europeReader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "eu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        // Only the expressions of the work whose CELEX is exactly the permalink's: ResolveExact also matches a work or
        // expression IRI or an article identity, and none of those may stand in the CELEX slot (review of #850).
        var ofWork = _europeReader.ResolveExact(celex)
            .Where(expression => _europeReader.ResolveWorkExpressions(expression.PublisherWorkId)
                .Any(held => string.Equals(held.PublisherWorkCelex, celex, StringComparison.Ordinal)))
            .ToArray();
        if (ofWork.Length == 0)
        {
            return Unknown(request, identifier, observedAt, PublisherId.EuEurLex,
                "a hash-pinned EU permalink of a work this EU index holds");
        }

        var languagesHeld = ofWork.Select(static expression => expression.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!languagesHeld.Contains(language, StringComparer.Ordinal) ||
            (requestedLanguage is not null && !string.Equals(requestedLanguage, language, StringComparison.Ordinal)))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage ?? language,
                available_languages = languagesHeld.Contains(language, StringComparer.Ordinal) ? new[] { language } : languagesHeld,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        var expressions = ofWork.Where(expression => string.Equals(expression.Language, language, StringComparison.Ordinal))
            .Select(static expression => expression.PublisherExpressionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (expressions.Length > 1)
        {
            using var ambiguous = JsonSerializer.SerializeToDocument(new { requested_identifier = identifier, candidates = expressions });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
        }

        if (EuropeWordingOf(expressions[0]) is not { } wording ||
            !string.Equals(wording.Celex, celex, StringComparison.Ordinal) ||
            !string.Equals(wording.WordingDate, wordingDate, StringComparison.Ordinal))
        {
            return Unknown(request, identifier, observedAt, PublisherId.EuEurLex,
                "a hash-pinned EU permalink of the one wording this EU index holds of the expression, at its wording date");
        }

        if (!string.Equals(requestedDigest, wording.Sha256, StringComparison.Ordinal))
        {
            using var mismatch = JsonSerializer.SerializeToDocument(new
            {
                requested_digest = requestedDigest,
                current_digest = wording.Sha256,
                stable_coordinate = EuropeStableCoordinate(wording.Permalink),
                current_hash_pinned_url = wording.Permalink,
                digest_rule = EuropeWordingDigestRule,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
        }

        if (provision is not null && !wording.Provisions.Contains(provision, StringComparer.Ordinal))
        {
            using var notInVersion = JsonSerializer.SerializeToDocument(new
            {
                requested_anchor = provision,
                nearest_anchors = NearestAnchors(wording.Provisions, provision),
                do_not_fall_back_to_full_text_search = true,
                what_would_answer = AnchorNotInVersionRoutes,
                asserts_absence_of_law = false,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "anchor_not_in_version", notInVersion.RootElement));
        }

        using var verification = JsonSerializer.SerializeToDocument(new
        {
            scope = EuropeVerifyScope,
            requested_identifier = identifier,
            requested_digest = requestedDigest,
            requested_language = requestedLanguage,
            requested_anchor = provision,
            article_permalink = provision is null ? null : EuropeProvisionPermalink(wording.Permalink, provision),
            verdict = "digest_matches",
            publisher = EuropePermalinkPublisher,
            celex = wording.Celex,
            publisher_work_id = wording.WorkId,
            publisher_expression_id = wording.ExpressionId,
            language = wording.Language,
            wording_date = wording.WordingDate,
            wording_date_semantics = EuropeWordingDateSemantics,
            wording_sha256 = wording.Sha256,
            digest_rule = EuropeWordingDigestRule,
            stable_coordinate = EuropeStableCoordinate(wording.Permalink),
            permalink = wording.Permalink,
            provisions = wording.Provisions.Count,
            // The provision coordinate EU resolve answers, in the form the EU index writes it.
            provision_coordinate = provision is null ? null : wording.ExpressionId + "#lex-provision=" + Uri.EscapeDataString(provision),
            available_languages = languagesHeld,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _europeReader.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = EuropeVerifyNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "verification", verification.RootElement));
    }

    private static bool TryParsePinnedPermalink(
        string value,
        out string workKey,
        out string applicabilityDate,
        out string requestedDigest,
        out string? anchor)
    {
        anchor = null;
        var hash = value.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
        {
            return TryParsePinnedPermalink(value, out workKey, out applicabilityDate, out requestedDigest);
        }

        var fragment = value[(hash + 1)..];
        if (fragment.Length == 0 || fragment.Any(static character => char.IsWhiteSpace(character) || character == '#') ||
            !TryParsePinnedPermalink(value[..hash], out workKey, out applicabilityDate, out requestedDigest))
        {
            workKey = string.Empty;
            applicabilityDate = string.Empty;
            requestedDigest = string.Empty;
            return false;
        }

        anchor = fragment;
        return true;
    }

    private static bool TryParsePinnedPermalink(
        string value,
        out string workKey,
        out string applicabilityDate,
        out string requestedDigest)
    {
        workKey = string.Empty;
        applicabilityDate = string.Empty;
        requestedDigest = string.Empty;
        string path;
        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            path = value;
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
                 string.Equals(uri.Host, "law.soufien.lu", StringComparison.Ordinal) &&
                 uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 &&
                 uri.Fragment.Length == 0)
        {
            path = uri.AbsolutePath;
        }
        else
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 ||
            !string.Equals(segments[0], "lu-legilux", StringComparison.Ordinal) ||
            !IsWorkKey(segments[1]) || segments[2].Length != 10 + 2 + 64 ||
            !string.Equals(segments[2].Substring(10, 2), "--", StringComparison.Ordinal) ||
            !DateOnly.TryParseExact(
                segments[2][..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _) ||
            !segments[2][12..].All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            return false;
        }

        workKey = segments[1];
        applicabilityDate = segments[2][..10];
        requestedDigest = segments[2][12..];
        return true;
    }

    private static bool IsWorkKey(string value) => value.Length is > 0 and <= 512 &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static bool LooksLikeIdentifier(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out _) ||
        value.StartsWith("eli/", StringComparison.OrdinalIgnoreCase) ||
        OfficialIdentifier.ProfileOf(value) is not null ||
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// The routes out of an absence, in the refusal card's closed vocabulary and its declared order
    /// (<c>web/scripts/refusal-card.mjs</c>, <c>WHAT_WOULD_ANSWER</c>). An absence refusal carries one of these with
    /// <c>asserts_absence_of_law: false</c>, because the absence of a held record is never evidence that the law does
    /// not exist; the prose that says exactly what to ask for instead travels beside it as
    /// <c>what_would_answer_detail</c>.
    /// </summary>
    internal static readonly string[] UnknownIdentifierRoutes = ["corrected_identifier", "expanded_official_scope"];

    internal static readonly string[] NoVersionForDateRoutes = ["new_official_observation"];

    internal static readonly string[] AnchorNotInVersionRoutes = ["corrected_identifier", "new_official_observation"];

    private V3PlatformOperationOutcome Unknown(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt,
        PublisherId? publisher = null,
        string whatWouldAnswer = "an exact identifier present in the mounted corpus")
    {
        var owner = publisher ?? PublisherFor(identifier);
        using var helpful = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            official_search_actions = new[] { "search" },
            what_would_answer = UnknownIdentifierRoutes,
            what_would_answer_detail = whatWouldAnswer,
            asserts_absence_of_law = false,
            population_disclosure = PopulationDisclosure(owner),
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, owner),
            new V3PlatformOperationRefusal(request, "identifier_unknown", helpful.RootElement));
    }

    /// <summary>
    /// What was searched before an identifier was found unknown, from this build's own index: the size of the
    /// population the answer covers, so "not found" is read against it (35-ideal-ux; the card requires it). Counts are
    /// of rows the index holds, never of what the publisher holds.
    /// </summary>
    private string PopulationDisclosure(PublisherId publisher)
    {
        if (publisher == PublisherId.EuEurLex)
        {
            return _europeReader is null
                ? "This build holds no EU index, so no EU identifier was searched."
                : $"This build's EU index holds {_europeReader.MemberCount} members of the EU population it acquired; an identifier outside them was not searched further.";
        }

        if (_reader is null)
        {
            return "This build holds no Luxembourg index, so no Luxembourg identifier was searched.";
        }

        var population = _reader.ResolveStatePopulation();
        var works = population.Works == 1 ? "1 Luxembourg work" : $"{population.Works} Luxembourg works";
        return population.FirstDate is null
            ? $"This build's Luxembourg index holds {works} and no dated state."
            : $"This build's Luxembourg index holds {works}, with states dated from {population.FirstDate} to {population.LastDate}.";
    }

    private PublisherId PublisherFor(string identifier) =>
        OfficialIdentifier.EliMintedBy(identifier) ??
        (OfficialIdentifier.ProfileOf(identifier) is not null ||
         IsEuropeanUnionPublisherAddress(identifier) ||
         IsEuropeCoordinate(identifier)
            ? PublisherId.EuEurLex
            : _reader is null ? PublisherId.EuEurLex : PublisherId.LuLegilux);

    /// <summary>
    /// An absolute web address on an EU publisher host names EU law whatever its path: an EUR-Lex
    /// page link, an Official Journal address, or a Publications Office Cellar or CELEX address.
    /// Attribution is decided by the host alone so that a miss on any of those spellings is
    /// refused as an unknown EU identifier rather than labelled as Luxembourg law. This admits
    /// nothing: exact lookup still requires the spelling the index stores.
    /// </summary>
    private static bool IsEuropeanUnionPublisherAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return string.Equals(uri.Host, EuropeanUnionPublisherDomain, StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith("." + EuropeanUnionPublisherDomain, StringComparison.OrdinalIgnoreCase);
    }

    private V3EnvelopeContext Context(
        string status,
        DateTimeOffset observedAt,
        PublisherId publisher = PublisherId.LuLegilux) => new(
            publisher,
            status,
            publisher == PublisherId.LuLegilux
                ? TimelineSemantics.PublisherApplicability
                : TimelineSemantics.OfficialConsolidationState,
            new V3SnapshotReference(
                "corpus-" + _corpus.ArtifactRef.Sha256[..16],
                _corpus.ArtifactRef.Sha256),
            publisher == PublisherId.LuLegilux ? "lu" : "eu",
            false,
            new V3Freshness(observedAt, "stale"));

    private static async Task<ReadOnlyMemory<byte>> ReadCapabilityAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumCapabilityManifestBytes)
            throw new InvalidDataException("The V3 capability manifest has an invalid byte length.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length != info.Length)
            throw new InvalidDataException("The V3 capability manifest changed while it was read.");
        return bytes;
    }
}
