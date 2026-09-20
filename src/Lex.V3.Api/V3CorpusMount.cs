using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

internal sealed class V3CorpusMount : IDisposable
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

    private V3CorpusMount(
        LuxembourgIndexReader? reader,
        EuropeIndexReader? europeReader,
        VerifiedLexCorpus6ManifestSet corpus)
    {
        if (reader is null && europeReader is null)
            throw new ArgumentException("A V3 corpus mount requires at least one publisher index.");
        _reader = reader;
        _europeReader = europeReader;
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
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

        var corpusBytes = await File.ReadAllBytesAsync(corpusPath, cancellationToken)
            .ConfigureAwait(false);
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);
        LuxembourgIndexReader? reader = null;
        EuropeIndexReader? europeReader = null;
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
            }

            if (hasEuropeIndex)
            {
                var capabilityBytes = await ReadCapabilityAsync(
                    europeCapabilityPath, cancellationToken).ConfigureAwait(false);
                europeReader = await EuropeIndexReader.OpenAndVerifyFileAsync(
                        europeIndexPath, capabilityBytes, corpus.ArtifactRef, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new V3CorpusMount(reader, europeReader, corpus);
        }
        catch
        {
            reader?.Dispose();
            europeReader?.Dispose();
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
                retrieval_lane = "r1_work_discovery",
                match_reason = work.MatchReason,
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader!.IndexRef.Sha256,
            });
            return V3PlatformOperationOutcome.Success(
                Context("success", observedAt),
                new V3PlatformOperationResult(request, "work_resolution", workResult.RootElement));
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
            available_languages = availableLanguages,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "version_state", result.RootElement));
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
        string? bound)
    {
        var dates = scope.Select(static state => state.ApplicabilityDate)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var payload = new JsonObject { ["requested_date"] = requestedDate };
        if (bound is not null)
        {
            payload["bound"] = bound;
        }

        payload["history_begins"] = dates[0];
        payload["nearest_earlier"] = null;
        payload["nearest_later"] = dates.FirstOrDefault(date => string.CompareOrdinal(date, requestedDate) > 0);
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
        foreach (var state in scope)
        {
            rows.Add(StateRow(state, NextDateInLanguage(scope, state), datesByIdentity));
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
            available_languages = availableLanguages,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "timeline", result.RootElement));
    }

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
        StateRow(state, nextDate, ArticleDateMap([state]));

    private static object StateRow(
        LuxembourgIndexResolvedState state, string? nextDate, IReadOnlyDictionary<string, string?> datesByIdentity)
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
        IsEuropeanUnionPublisherAddress(identifier);

    /// <summary>The identifier forms this product and Legilux mint for Luxembourg law.</summary>
    private static bool IsLuxembourgShaped(string identifier) =>
        OfficialIdentifier.EliMintedBy(identifier) == PublisherId.LuLegilux ||
        TryParsePinnedPermalink(identifier, out _, out _, out _) ||
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

    private V3PlatformOperationOutcome Unknown(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt,
        PublisherId? publisher = null,
        string whatWouldAnswer = "an exact identifier present in the mounted corpus")
    {
        using var helpful = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            official_search_actions = new[] { "search" },
            what_would_answer = whatWouldAnswer,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, publisher ?? PublisherFor(identifier)),
            new V3PlatformOperationRefusal(request, "identifier_unknown", helpful.RootElement));
    }

    private PublisherId PublisherFor(string identifier) =>
        OfficialIdentifier.EliMintedBy(identifier) ??
        (OfficialIdentifier.ProfileOf(identifier) is not null ||
         IsEuropeanUnionPublisherAddress(identifier)
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
