using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Api;

/// <summary>
/// The EU time view: the dated wordings of an EU act, read from the EU index's states table (schema 5), which lists every
/// work the publisher's census discovered for a reviewed seed act: the original act and each consolidated version, with the
/// publisher's consolidation date or a typed reason it has none. A wording's date is the original act's Formex date or the
/// publisher's consolidation date: never an entry-into-force, application or publication date, and never merged with a
/// Luxembourg date. The selection rule is the one every dated answer uses: the latest dated wording at or before the
/// requested date. Two different texts the corpus holds for one date are refused as ambiguous_version, never chosen
/// between; a version whose text is not held is disclosed beside the answer.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string EuropeStateDateSemantics =
        "an EU wording date is the date of a wording of the act: for the original wording, the date the publisher's Formex package gives " +
        "the act; for a consolidated version, the publisher's consolidation date (cdm:act_consolidated_date) of that version; it is not an " +
        "entry-into-force, application or publication date, and an EU date is never merged with a Luxembourg applicability date";

    internal const string EuropeSelectionRule =
        "per language, the wording a date answers is the latest dated wording at or before it, and it holds until the next dated wording " +
        "(next_date), or with no next date until a version the publisher has not yet consolidated, which is not held; when several " +
        "discovered works share that date, the answer is served only if every text the corpus holds for that date in that language is the " +
        "same (the publisher's work whose CELEX names that version represents them when its text is held), and two different held texts are " +
        "refused ambiguous_version; a version with no usable publisher date is disclosed, and refused as ambiguous only when the corpus holds " +
        "a text of it that differs from the answer";

    internal const string EuropeTimelineScope =
        "every wording of one EU act the publisher's census discovered, per language: the original wording and each consolidated version, " +
        "each with its date, whether its text is held, its hash-pinned permalink when it is, and the works that share its date; the versions " +
        "with no usable publisher date are listed apart with the typed reason";

    internal static readonly string[][] EuropeTimeNotHeld =
    [
        ["unconsolidated_amendments", "an amendment the publisher has not yet consolidated is not held: the latest consolidated wording answers every later date, as dated"],
        ["force_dates", "no entry-into-force, application or end-of-validity date is held; a wording date is none of them"],
        ["observation_time", "when the publisher served the retained packages is not held, so no observation time is stated"],
        ["publisher_signature", "no signature or attestation of the publisher is held; the digests are this index's own reading of the retained packages"],
    ];

    /// <summary>One held expression of a discovered work, in one language: what a wording of that work's date quotes.</summary>
    private sealed record EuropeWordingCandidate(EuropeIndexState State, string ExpressionId, string Language);

    /// <summary>
    /// The works the census dated to one day, in one language: the held candidates, and the answer when every held text agrees.
    /// <see cref="Basis"/> says how the answer was reached, or why there is none.
    /// </summary>
    private sealed record EuropeDatedWording(
        string Date,
        IReadOnlyList<EuropeIndexState> Works,
        IReadOnlyList<EuropeWordingCandidate> Held,
        EuropeWordingCandidate? Answer,
        string Basis);

    private sealed record EuropeLanguageTimeline(
        string Language,
        IReadOnlyList<EuropeDatedWording> Dated,
        IReadOnlyList<(EuropeIndexState State, EuropeWordingCandidate? Held)> Unplaced);

    private sealed record EuropeSeedTimeline(
        string SeedCelex,
        string? RootWorkIri,
        IReadOnlyList<string> Languages,
        IReadOnlyDictionary<string, EuropeLanguageTimeline> ByLanguage);

    private readonly object _europeTimeGate = new();
    private IReadOnlyList<EuropeIndexState>? _europeStates;
    private Dictionary<string, SortedSet<string>>? _europeSeedsByIdentifier;
    private readonly Dictionary<string, EuropeSeedTimeline> _europeTimelines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<EuropeIndexArticleText>> _europeArticleCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _europeTextKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// The seed acts whose census holds <paramref name="identifier"/>: a seed CELEX, a discovered work's IRI or observed CELEX,
    /// or one of the expressions a discovered work holds. Empty when the EU index has no states table or does not hold it.
    /// </summary>
    private IReadOnlyList<string> EuropeSeedsOf(string identifier)
    {
        if (_europeReader is not { HasStates: true })
        {
            return Array.Empty<string>();
        }

        lock (_europeTimeGate)
        {
            if (_europeSeedsByIdentifier is null)
            {
                var map = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                void Add(string key, string seed)
                {
                    if (!map.TryGetValue(key, out var seeds))
                    {
                        map[key] = seeds = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    seeds.Add(seed);
                }

                var states = EuropeStatesLocked();
                foreach (var state in states)
                {
                    Add(state.SeedCelex, state.SeedCelex);
                    Add(state.RootWorkIri, state.SeedCelex);
                    Add(state.PublisherWorkIri, state.SeedCelex);
                    if (state.PublisherWorkCelex is not null)
                    {
                        Add(state.PublisherWorkCelex, state.SeedCelex);
                    }
                }

                foreach (var seed in states.Select(static state => state.SeedCelex).Distinct(StringComparer.Ordinal))
                {
                    foreach (var expression in _europeReader.ReadStateExpressions(seed))
                    {
                        Add(expression.PublisherExpressionId, seed);
                    }
                }

                _europeSeedsByIdentifier = map;
            }

            return _europeSeedsByIdentifier.TryGetValue(identifier, out var found)
                ? found.ToArray()
                : Array.Empty<string>();
        }
    }

    private IReadOnlyList<EuropeIndexState> EuropeStatesLocked() => _europeStates ??= _europeReader!.ReadAllStates();

    private IReadOnlyList<EuropeIndexArticleText> EuropeArticlesOf(string expressionId)
    {
        lock (_europeTimeGate)
        {
            if (!_europeArticleCache.TryGetValue(expressionId, out var articles))
            {
                _europeArticleCache[expressionId] = articles = _europeReader!.ResolveExpressionArticles(expressionId);
            }

            return articles;
        }
    }

    /// <summary>The digest of an expression's held text, article by article in publisher order: what "the same text" compares.</summary>
    private string EuropeTextKeyOf(string expressionId)
    {
        lock (_europeTimeGate)
        {
            if (_europeTextKeys.TryGetValue(expressionId, out var key))
            {
                return key;
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var article in EuropeArticlesOf(expressionId))
        {
            foreach (var field in new[] { article.PublisherIdentifier, article.Text })
            {
                var bytes = Encoding.UTF8.GetBytes(field);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
        }

        var computed = Convert.ToHexStringLower(hash.GetHashAndReset());
        lock (_europeTimeGate)
        {
            _europeTextKeys[expressionId] = computed;
        }

        return computed;
    }

    /// <summary>The CELEX the publisher gives a consolidated version of <paramref name="seed"/> dated <paramref name="date"/>.</summary>
    private static string EuropeConsolidatedCelex(string seed, string date) =>
        "0" + seed[1..] + "-" + date.Replace("-", string.Empty, StringComparison.Ordinal);

    /// <summary>The wording timeline of one seed act, built once per mount (the index does not change while it is mounted).</summary>
    private EuropeSeedTimeline EuropeTimelineOf(string seed)
    {
        lock (_europeTimeGate)
        {
            if (_europeTimelines.TryGetValue(seed, out var cached))
            {
                return cached;
            }
        }

        IReadOnlyList<EuropeIndexState> all;
        lock (_europeTimeGate)
        {
            all = EuropeStatesLocked();
        }

        var states = all.Where(state => string.Equals(state.SeedCelex, seed, StringComparison.Ordinal)).ToArray();
        var expressions = _europeReader!.ReadStateExpressions(seed);
        var root = states.FirstOrDefault(static state => state.DateStatus == EuropeIndexStateDateStatus.OriginalWording);

        // The original act's date is the Formex date of its held wording, the same in every language; held in no language, or
        // with more than one date, the original wording is not placed.
        string? originalDate = null;
        if (root is not null)
        {
            var rootDates = expressions
                .Where(expression => string.Equals(expression.StateIdentitySha256, root.StateIdentitySha256, StringComparison.Ordinal))
                .SelectMany(expression => EuropeArticlesOf(expression.PublisherExpressionId).Select(static article => article.WordingDate))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            originalDate = rootDates.Length == 1 ? rootDates[0] : null;
        }

        var languages = expressions.Select(static expression => expression.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var byLanguage = new Dictionary<string, EuropeLanguageTimeline>(StringComparer.Ordinal);
        foreach (var language in languages)
        {
            EuropeWordingCandidate[] HeldOf(EuropeIndexState state) => expressions
                .Where(expression => string.Equals(expression.StateIdentitySha256, state.StateIdentitySha256, StringComparison.Ordinal) &&
                    string.Equals(expression.Language, language, StringComparison.Ordinal))
                .Select(static expression => expression.PublisherExpressionId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(expression => new EuropeWordingCandidate(state, expression, language))
                .ToArray();

            var dated = new SortedDictionary<string, List<EuropeIndexState>>(StringComparer.Ordinal);
            var unplaced = new List<(EuropeIndexState State, EuropeWordingCandidate? Held)>();
            foreach (var state in states)
            {
                var date = state.DateStatus == EuropeIndexStateDateStatus.OriginalWording ? originalDate : state.PublisherConsolidationDate;
                if (date is null)
                {
                    // Each held expression of an unplaced work is its own entry: none of them can be placed in time.
                    var held = HeldOf(state);
                    if (held.Length == 0)
                    {
                        unplaced.Add((state, null));
                    }
                    else
                    {
                        unplaced.AddRange(held.Select(candidate => (state, (EuropeWordingCandidate?)candidate)));
                    }

                    continue;
                }

                if (!dated.TryGetValue(date, out var works))
                {
                    dated[date] = works = new List<EuropeIndexState>();
                }

                works.Add(state);
            }

            var entries = dated.Select(entry =>
            {
                var works = entry.Value.OrderBy(static state => state.PublisherWorkIri, StringComparer.Ordinal).ToArray();
                var held = works.SelectMany(HeldOf).ToArray();
                if (held.Length == 0)
                {
                    return new EuropeDatedWording(entry.Key, works, held, Answer: null, Basis: "text_not_held");
                }

                if (held.Select(candidate => EuropeTextKeyOf(candidate.ExpressionId)).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    return new EuropeDatedWording(entry.Key, works, held, Answer: null, Basis: "different_texts");
                }

                // Every held text of this date agrees. The publisher's own designation represents them when its text is held:
                // the original work for the original's date, or the work whose CELEX names the consolidated version.
                var designated = held.FirstOrDefault(candidate =>
                    candidate.State.DateStatus == EuropeIndexStateDateStatus.OriginalWording ||
                    string.Equals(candidate.State.PublisherWorkCelex, EuropeConsolidatedCelex(seed, entry.Key), StringComparison.Ordinal));
                var answer = designated ?? held[0];
                var worksWithText = held.Select(static candidate => candidate.State.PublisherWorkIri).Distinct(StringComparer.Ordinal).Count();
                var basis = works.Length == 1 && held.Length == 1
                    ? "single_work"
                    : worksWithText == works.Length ? "identical_text" : "identical_held_text";
                return new EuropeDatedWording(entry.Key, works, held, answer, basis);
            }).ToArray();
            byLanguage[language] = new EuropeLanguageTimeline(language, entries, unplaced);
        }

        var timeline = new EuropeSeedTimeline(seed, root?.PublisherWorkIri, languages, byLanguage);
        lock (_europeTimeGate)
        {
            _europeTimelines[seed] = timeline;
        }

        return timeline;
    }

    /// <summary>The pin of a held wording on its date: its digest under <see cref="EuropeWordingDigestRule"/> and its permalink.</summary>
    private (string Sha256, string Permalink, IReadOnlyList<string> Provisions) EuropeStatePin(string seed, EuropeWordingCandidate candidate, string date)
    {
        var articles = EuropeArticlesOf(candidate.ExpressionId);
        var sha256 = EuropeWordingSha256(seed, candidate.State.PublisherWorkIri, candidate.ExpressionId, candidate.Language, date,
            articles.Select(static article => article.ArticleIdentitySha256).ToArray());
        return (sha256, $"/{EuropePermalinkPublisher}/{seed}/{candidate.Language}/{date}--{sha256}",
            articles.Select(static article => article.PublisherIdentifier).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string EuropeDateStatusToken(EuropeIndexStateDateStatus status) => status switch
    {
        EuropeIndexStateDateStatus.OriginalWording => "original_wording",
        EuropeIndexStateDateStatus.ObservedConsolidationDate => "consolidation_date",
        EuropeIndexStateDateStatus.AmbiguousVersion => "several_works_or_dates",
        EuropeIndexStateDateStatus.ObservationMissing => "date_not_observed",
        EuropeIndexStateDateStatus.PublisherDateAbsent => "publisher_date_absent",
        EuropeIndexStateDateStatus.PublisherDateUnusable => "publisher_date_unusable",
        _ => throw new InvalidDataException("An EU state has an unknown date status."),
    };

    private static string EuropeKindOf(EuropeIndexState state) =>
        state.DateStatus == EuropeIndexStateDateStatus.OriginalWording ? "original_wording" : "consolidated_version";

    /// <summary>One dated wording as the time view serves it, with the works that share its date.</summary>
    private object EuropeDatedRow(string seed, EuropeDatedWording entry, string? nextDate)
    {
        var answerText = entry.Answer is null ? null : EuropeTextKeyOf(entry.Answer.ExpressionId);
        var pin = entry.Answer is null ? ((string Sha256, string Permalink, IReadOnlyList<string> Provisions)?)null : EuropeStatePin(seed, entry.Answer, entry.Date);
        return new
        {
            wording_date = entry.Date,
            kind = entry.Answer is null
                ? (entry.Works.Any(static state => state.DateStatus == EuropeIndexStateDateStatus.OriginalWording) ? "original_wording" : "consolidated_version")
                : EuropeKindOf(entry.Answer.State),
            basis = entry.Basis,
            text_held = entry.Answer is not null,
            publisher_work_id = entry.Answer?.State.PublisherWorkIri,
            celex = entry.Answer?.State.PublisherWorkCelex,
            publisher_expression_id = entry.Answer?.ExpressionId,
            article_count = entry.Answer is null ? (int?)null : EuropeArticlesOf(entry.Answer.ExpressionId).Count,
            wording_sha256 = pin?.Sha256,
            stable_coordinate = pin is null ? null : EuropeStableCoordinate(pin.Value.Permalink),
            permalink = pin?.Permalink,
            next_date = nextDate,
            same_date_works = entry.Works
                .Where(state => entry.Answer is null || !string.Equals(state.PublisherWorkIri, entry.Answer.State.PublisherWorkIri, StringComparison.Ordinal))
                .Select(state =>
                {
                    var held = entry.Held.Where(candidate => string.Equals(candidate.State.PublisherWorkIri, state.PublisherWorkIri, StringComparison.Ordinal)).ToArray();
                    return new
                    {
                        publisher_work_id = state.PublisherWorkIri,
                        celex = state.PublisherWorkCelex,
                        kind = EuropeKindOf(state),
                        text_held = held.Length > 0,
                        publisher_expression_ids = held.Select(static candidate => candidate.ExpressionId).ToArray(),
                        same_text = held.Length == 0 || answerText is null
                            ? (bool?)null
                            : held.All(candidate => string.Equals(EuropeTextKeyOf(candidate.ExpressionId), answerText, StringComparison.Ordinal)),
                    };
                }).ToArray(),
        };
    }

    private object EuropeUnplacedRow((EuropeIndexState State, EuropeWordingCandidate? Held) unplaced, string? answerText) => new
    {
        publisher_work_id = unplaced.State.PublisherWorkIri,
        celex = unplaced.State.PublisherWorkCelex,
        kind = EuropeKindOf(unplaced.State),
        date_status = EuropeDateStatusToken(unplaced.State.DateStatus),
        text_held = unplaced.Held is not null,
        publisher_expression_id = unplaced.Held?.ExpressionId,
        same_text = unplaced.Held is null || answerText is null
            ? (bool?)null
            : string.Equals(EuropeTextKeyOf(unplaced.Held.ExpressionId), answerText, StringComparison.Ordinal),
    };

    /// <summary>
    /// The EU path of a dated or inventory operation: the seed act the identifier names, when the EU index has a states table
    /// and its census holds the identifier. Returns a refusal (an identifier two seeds hold), or null with <paramref name="seed"/>
    /// set, or null with <paramref name="seed"/> null when the time view does not answer (the caller's path answers).
    /// </summary>
    private V3PlatformOperationOutcome? LocateEuropeSeed(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt,
        out string? seed)
    {
        seed = null;
        var seeds = EuropeSeedsOf(identifier);
        if (seeds.Count == 0)
        {
            return null;
        }

        if (seeds.Count > 1)
        {
            using var ambiguous = JsonSerializer.SerializeToDocument(new { requested_identifier = identifier, candidates = seeds });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
        }

        // An identifier the Luxembourg index also holds is ambiguous, as resolve answers it.
        if (_reader is not null &&
            (_reader.ResolveWorkStates(TryParseStableWorkCoordinate(identifier, out var workKey) ? workKey : identifier).Count > 0 ||
             _reader.ResolveExact(identifier).Count > 0))
        {
            using var both = JsonSerializer.SerializeToDocument(new { requested_identifier = identifier, candidates = new[] { identifier } });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherForIdentifierInBoth(identifier)),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", both.RootElement));
        }

        seed = seeds[0];
        return null;
    }

    private static PublisherId PublisherForIdentifierInBoth(string identifier) =>
        IsEuropeanUnionShaped(identifier) ? PublisherId.EuEurLex : PublisherId.LuLegilux;

    private V3PlatformOperationOutcome? RefuseEuropeLanguage(
        V3PlatformOperationRequest request,
        EuropeSeedTimeline timeline,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        if (requestedLanguage is null || timeline.Languages.Contains(requestedLanguage, StringComparer.Ordinal))
        {
            return null;
        }

        using var unavailable = JsonSerializer.SerializeToDocument(new
        {
            requested_language = requestedLanguage,
            available_languages = timeline.Languages,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationRefusal(request, "language_not_available", unavailable.RootElement));
    }

    /// <summary>EU <c>timeline</c>: every wording the census discovered for the act, per language (<see cref="EuropeTimelineScope"/>).</summary>
    private V3PlatformOperationOutcome TimelineEurope(
        V3PlatformOperationRequest request,
        string identifier,
        string seed,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        var timeline = EuropeTimelineOf(seed);
        if (RefuseEuropeLanguage(request, timeline, requestedLanguage, observedAt) is { } refused)
        {
            return refused;
        }

        var served = requestedLanguage is null ? timeline.Languages : new[] { requestedLanguage };
        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = EuropeTimelineScope,
            requested_identifier = identifier,
            requested_language = requestedLanguage,
            publisher = EuropePermalinkPublisher,
            seed_celex = seed,
            original_work_id = timeline.RootWorkIri,
            available_languages = timeline.Languages,
            languages = served.Select(language =>
            {
                var ofLanguage = timeline.ByLanguage[language];
                return new
                {
                    language,
                    history_begins = ofLanguage.Dated.Count == 0 ? null : ofLanguage.Dated[0].Date,
                    wording_count = ofLanguage.Dated.Count,
                    wordings = ofLanguage.Dated
                        .Select((entry, position) => EuropeDatedRow(seed, entry, position + 1 < ofLanguage.Dated.Count ? ofLanguage.Dated[position + 1].Date : null))
                        .ToArray(),
                    unplaced_versions = ofLanguage.Unplaced.Select(unplaced => EuropeUnplacedRow(unplaced, null)).ToArray(),
                };
            }).ToArray(),
            date_semantics = EuropeStateDateSemantics,
            selection_rule = EuropeSelectionRule,
            digest_rule = EuropeWordingDigestRule,
            not_held = EuropeTimeNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "timeline", result.RootElement));
    }

    /// <summary>The selection of one language at one date, or why there is none.</summary>
    private sealed record EuropeSelection(
        string Language,
        EuropeDatedWording? Entry,
        string? NextDate,
        IReadOnlyList<(EuropeIndexState State, EuropeWordingCandidate? Held)> ConflictingUnplaced);

    private EuropeSelection EuropeSelectAt(EuropeLanguageTimeline ofLanguage, string requestedDate)
    {
        var index = -1;
        for (var position = 0; position < ofLanguage.Dated.Count; position++)
        {
            if (string.CompareOrdinal(ofLanguage.Dated[position].Date, requestedDate) <= 0)
            {
                index = position;
            }
        }

        if (index < 0)
        {
            return new EuropeSelection(ofLanguage.Language, null, null, Array.Empty<(EuropeIndexState State, EuropeWordingCandidate? Held)>());
        }

        var entry = ofLanguage.Dated[index];
        var next = index + 1 < ofLanguage.Dated.Count ? ofLanguage.Dated[index + 1].Date : null;
        var answerText = entry.Answer is null ? null : EuropeTextKeyOf(entry.Answer.ExpressionId);
        var conflicting = answerText is null
            ? Array.Empty<(EuropeIndexState State, EuropeWordingCandidate? Held)>()
            : ofLanguage.Unplaced.Where(unplaced => unplaced.Held is not null &&
                !string.Equals(EuropeTextKeyOf(unplaced.Held.ExpressionId), answerText, StringComparison.Ordinal)).ToArray();
        return new EuropeSelection(ofLanguage.Language, entry, next, conflicting);
    }

    /// <summary>
    /// The shared EU selection of <c>as_of</c> and <c>evidence_bundle</c>: per served language, the wording the requested date
    /// answers, or the refusal every served language reaches. Returns null with the answered selections, or a refusal.
    /// </summary>
    private V3PlatformOperationOutcome? SelectEuropeAt(
        V3PlatformOperationRequest request,
        EuropeSeedTimeline timeline,
        string requestedDate,
        string? requestedLanguage,
        DateTimeOffset observedAt,
        out IReadOnlyList<EuropeSelection> answered)
    {
        answered = Array.Empty<EuropeSelection>();
        if (RefuseEuropeLanguage(request, timeline, requestedLanguage, observedAt) is { } refused)
        {
            return refused;
        }

        var served = requestedLanguage is null ? timeline.Languages : new[] { requestedLanguage };
        var selections = served.Select(language => EuropeSelectAt(timeline.ByLanguage[language], requestedDate)).ToArray();

        // Two different held texts for the date, in a served language, are never chosen between.
        var ambiguous = selections
            .Where(static selection => selection.Entry is { Answer: null, Basis: "different_texts" } || selection.ConflictingUnplaced.Count > 0)
            .ToArray();
        if (ambiguous.Length > 0)
        {
            var candidates = ambiguous.SelectMany(selection =>
                    (selection.Entry is { Answer: null } entry ? entry.Held.Select(static candidate => candidate.ExpressionId) : Array.Empty<string>())
                    .Concat(selection.Entry?.Answer is { } answer ? new[] { answer.ExpressionId } : Array.Empty<string>())
                    .Concat(selection.ConflictingUnplaced.Select(static unplaced => unplaced.Held!.ExpressionId)))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var document = JsonSerializer.SerializeToDocument(new
            {
                requested_date = requestedDate,
                candidates,
                reason = ambiguous.Any(static selection => selection.ConflictingUnplaced.Count > 0)
                    ? "a held text of a version with no usable publisher date differs from the wording of the date, so the date cannot be answered"
                    : "the corpus holds different texts for the wording of this date, so it cannot be answered",
                selection_rule = EuropeSelectionRule,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_version", document.RootElement));
        }

        answered = selections.Where(static selection => selection.Entry?.Answer is not null).ToArray();
        if (answered.Count > 0)
        {
            return null;
        }

        // No served language answers. A date before every wording is no_version_for_date; a date whose wording is held in no
        // served language is text_not_available, naming the publisher's works of that date.
        var withoutText = selections.Where(static selection => selection.Entry is { Basis: "text_not_held" }).ToArray();
        if (withoutText.Length > 0)
        {
            var works = withoutText.SelectMany(static selection => selection.Entry!.Works)
                .Select(static state => state.PublisherWorkIri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var unavailable = JsonSerializer.SerializeToDocument(new
            {
                official_identity = works[0],
                official_source = works[0],
                retained_transport_evidence = "none",
                requested_date = requestedDate,
                wording_date = withoutText[0].Entry!.Date,
                publisher_works = works,
                what_would_answer = new[] { "new_official_observation" },
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "text_not_available", unavailable.RootElement));
        }

        var dates = served.SelectMany(language => timeline.ByLanguage[language].Dated.Select(static entry => entry.Date))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        using var noVersion = JsonSerializer.SerializeToDocument(new
        {
            requested_date = requestedDate,
            history_begins = dates.Length == 0 ? null : dates[0],
            nearest_earlier = (string?)null,
            nearest_later = dates.FirstOrDefault(date => string.CompareOrdinal(date, requestedDate) > 0),
            what_would_answer = NoVersionForDateRoutes,
            asserts_absence_of_law = false,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationRefusal(request, "no_version_for_date", noVersion.RootElement));
    }

    /// <summary>EU <c>as_of</c>: the wording each served language holds at the requested date (<see cref="EuropeSelectionRule"/>).</summary>
    private V3PlatformOperationOutcome AsOfEurope(
        V3PlatformOperationRequest request,
        string identifier,
        string seed,
        string requestedDate,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        var timeline = EuropeTimelineOf(seed);
        if (SelectEuropeAt(request, timeline, requestedDate, requestedLanguage, observedAt, out var answered) is { } refused)
        {
            return refused;
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            publisher = EuropePermalinkPublisher,
            seed_celex = seed,
            original_work_id = timeline.RootWorkIri,
            states = answered.Select(selection => new
            {
                language = selection.Language,
                wording = EuropeDatedRow(seed, selection.Entry!, selection.NextDate),
                unplaced_versions = timeline.ByLanguage[selection.Language].Unplaced
                    .Select(unplaced => EuropeUnplacedRow(unplaced, EuropeTextKeyOf(selection.Entry!.Answer!.ExpressionId)))
                    .ToArray(),
            }).ToArray(),
            languages_not_answering = (requestedLanguage is null ? timeline.Languages : new[] { requestedLanguage })
                .Except(answered.Select(static selection => selection.Language), StringComparer.Ordinal).ToArray(),
            available_languages = timeline.Languages,
            date_semantics = EuropeStateDateSemantics,
            selection_rule = EuropeSelectionRule,
            digest_rule = EuropeWordingDigestRule,
            not_held = EuropeTimeNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "version_state", result.RootElement));
    }

    /// <summary>
    /// EU <c>evidence_bundle</c> over the time view: the wording each served language holds at the requested date, quoted under
    /// the rights rule with the acknowledgement and authenticity statement Decision 95 requires.
    /// </summary>
    private V3PlatformOperationOutcome EvidenceBundleEuropeAtDate(
        V3PlatformOperationRequest request,
        string identifier,
        string seed,
        string requestedDate,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        var timeline = EuropeTimelineOf(seed);
        if (SelectEuropeAt(request, timeline, requestedDate, requestedLanguage, observedAt, out var answered) is { } refused)
        {
            return refused;
        }

        // ---- Rights at compose time, per selected wording, before any text is served. ----
        foreach (var selection in answered)
        {
            var entry = selection.Entry!;
            var candidate = entry.Answer!;
            var articles = EuropeArticlesOf(candidate.ExpressionId);
            var members = articles.Select(static article => article.ObjectRefSha256).Distinct(StringComparer.Ordinal)
                .Select(objectRef => (ObjectRef: objectRef, Member: EuropeMemberOf(objectRef)))
                .ToArray();
            var blocking = members.FirstOrDefault(static entry => entry.Member is null || entry.Member.Outcome != LexCorpus6OutcomeKind.Acquired);
            if (articles.Count == 0 || blocking.ObjectRef is not null)
            {
                var pin = EuropeStatePin(seed, candidate, entry.Date);
                using var withheld = JsonSerializer.SerializeToDocument(new
                {
                    official_identity = candidate.ExpressionId,
                    official_link = articles.Select(static article => article.OfficialSourceUri).FirstOrDefault(static uri => uri is not null) ?? candidate.State.PublisherWorkIri,
                    content_sha256 = blocking.Member?.BodySha256 ?? pin.Sha256,
                    stable_coordinate = EuropeStableCoordinate(pin.Permalink),
                    permalink = pin.Permalink,
                    language = candidate.Language,
                    source_outcome = blocking.Member is null ? null : ContractWire.NameOf(blocking.Member.Outcome),
                    rule = EuropeEvidenceBundleRightsRule,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.EuEurLex),
                    new V3PlatformOperationRefusal(request, "text_withheld", withheld.RootElement));
            }
        }

        var bundles = answered.Select(selection =>
        {
            var entry = selection.Entry!;
            var candidate = entry.Answer!;
            var pin = EuropeStatePin(seed, candidate, entry.Date);
            var articles = EuropeArticlesOf(candidate.ExpressionId);
            var quoted = articles.Where(static article => article.Text.Length > 0).ToArray();
            return new
            {
                publisher_work_id = candidate.State.PublisherWorkIri,
                celex = candidate.State.PublisherWorkCelex,
                publisher_expression_id = candidate.ExpressionId,
                kind = EuropeKindOf(candidate.State),
                language = candidate.Language,
                wording_date = entry.Date,
                next_date = selection.NextDate,
                basis = entry.Basis,
                wording_sha256 = pin.Sha256,
                stable_coordinate = EuropeStableCoordinate(pin.Permalink),
                permalink = pin.Permalink,
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
                        official_source = article.OfficialSourceUri ?? candidate.State.PublisherWorkIri,
                        article_permalink = EuropeProvisionPermalink(pin.Permalink, article.PublisherIdentifier),
                        provision_coordinate = candidate.ExpressionId + "#lex-provision=" + Uri.EscapeDataString(article.PublisherIdentifier),
                    };
                }).ToArray(),
                articles_without_text = articles.Where(static article => article.Text.Length == 0).Select(static article => new
                {
                    article_identity_sha256 = article.ArticleIdentitySha256,
                    publisher_id = article.PublisherIdentifier,
                }).ToArray(),
            };
        }).ToArray();

        using var result = JsonSerializer.SerializeToDocument(new
        {
            scope = EuropeEvidenceBundleScope,
            requested_identifier = identifier,
            requested_date = requestedDate,
            requested_language = requestedLanguage,
            publisher = EuropePermalinkPublisher,
            publisher_work_id = timeline.RootWorkIri,
            celex = seed,
            available_languages = timeline.Languages,
            served_languages = answered.Select(static selection => selection.Language).ToArray(),
            wordings = bundles,
            acknowledgement = EuropeTextAcknowledgement,
            authenticity = EuropeTextAuthenticity,
            rights_rule = EuropeEvidenceBundleRightsRule,
            date_rule = EuropeSelectionRule,
            date_semantics = EuropeStateDateSemantics,
            digest_rule = EuropeWordingDigestRule,
            consolidations_held = EuropeConsolidationsHeld(timeline),
            not_held = EuropeTimeNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "evidence_bundle", result.RootElement));
    }

    /// <summary>Whether the corpus holds the text of any consolidated version of the act, in any language.</summary>
    private static bool EuropeConsolidationsHeld(EuropeSeedTimeline timeline) =>
        timeline.ByLanguage.Values.Any(static ofLanguage =>
            ofLanguage.Dated.Any(static entry => entry.Held.Any(static candidate => candidate.State.DateStatus != EuropeIndexStateDateStatus.OriginalWording)) ||
            ofLanguage.Unplaced.Any(static unplaced => unplaced.Held is not null));

    /// <summary>
    /// <c>verify</c> for an EU permalink over the time view: the held wordings of the seed act on the permalink's date, in its
    /// language, are the candidates; the one whose digest is the permalink's verifies. Null when the time view does not hold the
    /// permalink's CELEX as a seed act (the original-wording path answers, unchanged).
    /// </summary>
    private V3PlatformOperationOutcome? VerifyEuropeState(
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
        if (EuropeSeedsOf(celex) is not [var seed] || !string.Equals(seed, celex, StringComparison.Ordinal))
        {
            return null;
        }

        var timeline = EuropeTimelineOf(seed);
        if (!timeline.Languages.Contains(language, StringComparer.Ordinal) ||
            (requestedLanguage is not null && !string.Equals(requestedLanguage, language, StringComparison.Ordinal)))
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage ?? language,
                available_languages = timeline.Languages.Contains(language, StringComparer.Ordinal) ? new[] { language } : timeline.Languages.ToArray(),
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement));
        }

        var entry = timeline.ByLanguage[language].Dated.FirstOrDefault(dated => string.Equals(dated.Date, wordingDate, StringComparison.Ordinal));
        if (entry is null || entry.Held.Count == 0)
        {
            return Unknown(request, identifier, observedAt, PublisherId.EuEurLex,
                "a hash-pinned EU permalink of a wording this EU index holds, at its wording date");
        }

        var pins = entry.Held.Select(candidate => (Candidate: candidate, Pin: EuropeStatePin(seed, candidate, wordingDate))).ToArray();
        var match = pins.FirstOrDefault(pinned => string.Equals(pinned.Pin.Sha256, requestedDigest, StringComparison.Ordinal));
        if (match.Candidate is null)
        {
            // The current wording of that date, when the date has one; held texts that differ have no single current pin.
            var current = entry.Answer is null ? pins[0] : pins.First(pinned => ReferenceEquals(pinned.Candidate, entry.Answer));
            using var mismatch = JsonSerializer.SerializeToDocument(new
            {
                requested_digest = requestedDigest,
                current_digest = current.Pin.Sha256,
                stable_coordinate = EuropeStableCoordinate(current.Pin.Permalink),
                current_hash_pinned_url = current.Pin.Permalink,
                candidates = entry.Answer is null ? pins.Select(static pinned => pinned.Pin.Permalink).ToArray() : null,
                digest_rule = EuropeWordingDigestRule,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
        }

        if (provision is not null && !match.Pin.Provisions.Contains(provision, StringComparer.Ordinal))
        {
            using var notInVersion = JsonSerializer.SerializeToDocument(new
            {
                requested_anchor = provision,
                nearest_anchors = NearestAnchors(match.Pin.Provisions, provision),
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
            article_permalink = provision is null ? null : EuropeProvisionPermalink(match.Pin.Permalink, provision),
            verdict = "digest_matches",
            publisher = EuropePermalinkPublisher,
            celex = seed,
            publisher_work_id = match.Candidate.State.PublisherWorkIri,
            work_celex = match.Candidate.State.PublisherWorkCelex,
            publisher_expression_id = match.Candidate.ExpressionId,
            kind = EuropeKindOf(match.Candidate.State),
            language,
            wording_date = wordingDate,
            wording_date_semantics = EuropeStateDateSemantics,
            wording_sha256 = match.Pin.Sha256,
            digest_rule = EuropeWordingDigestRule,
            stable_coordinate = EuropeStableCoordinate(match.Pin.Permalink),
            permalink = match.Pin.Permalink,
            provisions = match.Pin.Provisions.Count,
            provision_coordinate = provision is null ? null : match.Candidate.ExpressionId + "#lex-provision=" + Uri.EscapeDataString(provision),
            available_languages = timeline.Languages,
            verified_by = new
            {
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _europeReader!.IndexRef.Sha256,
                registry_sha256 = V3OperationRegistry.Reviewed.Sha256,
            },
            not_held = EuropeTimeNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "verification", verification.RootElement));
    }

    /// <summary>The time view's summary for EU dossier: each language's dated wordings and unplaced versions, as timeline lists them.</summary>
    private object? EuropeDossierTimeline(string? seed, string? requestedLanguage)
    {
        if (seed is null)
        {
            return null;
        }

        var timeline = EuropeTimelineOf(seed);
        var served = requestedLanguage is null
            ? timeline.Languages
            : timeline.Languages.Where(language => string.Equals(language, requestedLanguage, StringComparison.Ordinal)).ToArray();
        return new
        {
            seed_celex = seed,
            consolidations_held = EuropeConsolidationsHeld(timeline),
            languages = served.Select(language =>
            {
                var ofLanguage = timeline.ByLanguage[language];
                return new
                {
                    language,
                    wordings = ofLanguage.Dated.Select(static entry => new
                    {
                        wording_date = entry.Date,
                        basis = entry.Basis,
                        text_held = entry.Answer is not null,
                        works = entry.Works.Count,
                    }).ToArray(),
                    unplaced_versions = ofLanguage.Unplaced.Count,
                };
            }).ToArray(),
        };
    }
}
