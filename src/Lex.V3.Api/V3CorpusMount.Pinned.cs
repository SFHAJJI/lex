using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// A hash-pinned permalink asked of <c>as_of</c> and <c>evidence_bundle</c> (the specification of record's R64, the plan's
/// item 3.2): the identifier is a state's (or an EU wording's) permalink, <c>/{publisher}/{work}/{date}--{digest}/{language}</c>,
/// and the answer is the version it pins, or the refusal that says why not. A permalink is the citation a reader holds, so
/// the page at one asks it as it stands: never the version the date resolves to now in its place.
/// </summary>
/// <remarks>
/// <para>
/// The rules are <c>verify</c>'s, read the same way (<see cref="Verify"/>, <see cref="VerifyEuropeState"/>,
/// <see cref="VerifyEurope"/>): the permalink names its language, so a language asked beside it must be that one; only that
/// language's states (or wordings) at the permalink's date can answer its digest; a digest the coordinate no longer carries is
/// <c>pinned_digest_mismatch</c> naming the current one (its <c>current_hash_pinned_url</c>), and two of one language there
/// with neither the pinned one are ambiguous. The date asked must be the permalink's own: a permalink names its date, and any
/// other date would ask for another version than the one it pins.
/// </para>
/// <para>
/// What is not done here: a state only a retained generation still holds is not quoted from that generation (its corpus is
/// not kept by the mount); it answers <c>pinned_digest_mismatch</c> naming the current state, while <c>verify</c> names the
/// generation that holds it. An article's permalink is not an identifier these operations take (they name a state, as
/// <c>resolve</c> does); the page at one asks its state's.
/// </para>
/// </remarks>
internal sealed partial class V3CorpusMount
{
    internal const string PinnedPermalinkRule =
        "given as the identifier, a state's hash-pinned permalink (/lu-legilux/{work}/{date}--{state sha256}/{language}, R64; with its own date, and " +
        "the language it names if one is asked) answers the state it pins; a digest the coordinate no longer carries in that language is " +
        "pinned_digest_mismatch naming the current state, never another state in its place";

    internal const string EuropePinnedPermalinkRule =
        "given as the identifier, a wording's hash-pinned permalink (/eu-eurlex/{celex}/{date}--{wording sha256}/{language}, R64; with its own date, " +
        "and the language it names if one is asked) answers the wording it pins; a digest no wording of that date carries in that language is " +
        "pinned_digest_mismatch naming the wording the date answers with, never another wording in its place";

    /// <summary>
    /// Whether <paramref name="identifier"/> is a Luxembourg state's hash-pinned permalink with no article segment, and its
    /// parts. An article's permalink is not (it names an article; <c>resolve</c> refuses it the same way).
    /// </summary>
    private static bool TryParsePinnedState(
        string identifier,
        out string workKey,
        out string date,
        out string digest,
        out string language) =>
        TryParsePinnedPermalink(identifier, out workKey, out date, out digest, out language, out var anchor) && anchor is null;

    /// <summary>Whether <paramref name="identifier"/> is an EU wording's hash-pinned permalink with no provision segment, and its parts.</summary>
    private static bool TryParsePinnedWording(
        string identifier,
        out string celex,
        out string date,
        out string digest,
        out string language) =>
        TryParseEuropePermalink(identifier, out celex, out language, out date, out digest, out var provision) && provision is null;

    /// <summary>
    /// <c>evidence_bundle</c> at an EU wording's hash-pinned permalink: the wording it pins, quoted under the rights rule, from the
    /// time view when the EU census holds the permalink's CELEX as a seed act, else from the original wording the EU index holds
    /// of the expression (as <c>verify</c> chooses between the two). The answer is in the permalink's language.
    /// </summary>
    private V3PlatformOperationOutcome EvidenceBundleEuropePinned(
        V3PlatformOperationRequest request,
        string identifier,
        string celex,
        string date,
        string digest,
        string language,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        // With no EU index mounted, an EU identifier keeps the refusal evidence_bundle gives every EU identifier there.
        if (_europeReader is null)
        {
            return ModeUnavailable(request, observedAt, PublisherId.EuEurLex, "r7_evidence_bundle");
        }

        if (EuropeSeedsOf(celex) is [var seed] && string.Equals(seed, celex, StringComparison.Ordinal))
        {
            var timeline = EuropeTimelineOf(seed);
            if (RefuseUnlessPinnedWording(request, identifier, timeline, language, date, digest, requestedLanguage, observedAt, out var pinned) is { } refused)
            {
                return refused;
            }

            return EvidenceBundleEuropeOf(request, identifier, seed, timeline, [pinned], date, language, observedAt);
        }

        if (RefuseUnlessPinnedOriginalWording(request, identifier, celex, language, date, digest, requestedLanguage, observedAt, out var ofWork) is { } refusedOriginal)
        {
            return refusedOriginal;
        }

        return EvidenceBundleEurope(request, identifier, ofWork, date, language, observedAt);
    }

    /// <summary>
    /// <c>as_of</c> at an EU wording's hash-pinned permalink: the wording it pins in the time view. An EU index with no census of
    /// that act keeps <c>as_of</c>'s answer for an EU identifier it cannot place in time (the mode refused, as before).
    /// </summary>
    private V3PlatformOperationOutcome AsOfEuropePinned(
        V3PlatformOperationRequest request,
        string identifier,
        string celex,
        string date,
        string digest,
        string language,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        if (!HasEuropeCensus())
        {
            return ModeUnavailable(request, observedAt, PublisherId.EuEurLex, "r6_as_of");
        }

        if (EuropeSeedsOf(celex) is not [var seed] || !string.Equals(seed, celex, StringComparison.Ordinal))
        {
            return Unknown(request, identifier, observedAt, PublisherId.EuEurLex,
                "a hash-pinned EU permalink of a wording of an EU act the mounted EU census holds, at its wording date");
        }

        var timeline = EuropeTimelineOf(seed);
        if (RefuseUnlessPinnedWording(request, identifier, timeline, language, date, digest, requestedLanguage, observedAt, out var pinned) is { } refused)
        {
            return refused;
        }

        return AsOfEuropeOf(request, identifier, seed, timeline, [pinned], date, language, observedAt);
    }

    /// <summary>
    /// The original wording an EU permalink pins on an EU index with no census of its act, as <c>verify</c> reads it
    /// (<see cref="VerifyEurope"/>): the expressions of the work whose CELEX is exactly the permalink's, or the refusal that
    /// stands in for them: <c>identifier_unknown</c>, <c>language_not_available</c>, <c>ambiguous_identifier</c> for two
    /// expressions in its language, and <c>pinned_digest_mismatch</c> naming the wording the index holds.
    /// </summary>
    private V3PlatformOperationOutcome? RefuseUnlessPinnedOriginalWording(
        V3PlatformOperationRequest request,
        string identifier,
        string celex,
        string language,
        string date,
        string digest,
        string? requestedLanguage,
        DateTimeOffset observedAt,
        out IReadOnlyList<Lex.V3.Ingest.Europe.EuropeIndexResolvedExpression> ofWork)
    {
        // Only the expressions of the work whose CELEX is exactly the permalink's (review of #850).
        ofWork = _europeReader!.ResolveExact(celex)
            .Where(expression => _europeReader.ResolveWorkExpressions(expression.PublisherWorkId)
                .Any(held => string.Equals(held.PublisherWorkCelex, celex, StringComparison.Ordinal)))
            .ToArray();
        if (ofWork.Count == 0)
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
            !string.Equals(wording.WordingDate, date, StringComparison.Ordinal))
        {
            return Unknown(request, identifier, observedAt, PublisherId.EuEurLex,
                "a hash-pinned EU permalink of the one wording this EU index holds of the expression, at its wording date");
        }

        if (!string.Equals(digest, wording.Sha256, StringComparison.Ordinal))
        {
            using var mismatch = JsonSerializer.SerializeToDocument(new
            {
                requested_digest = digest,
                current_digest = wording.Sha256,
                stable_coordinate = EuropeStableCoordinate(wording.Permalink),
                current_hash_pinned_url = wording.Permalink,
                digest_rule = EuropeWordingDigestRule,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
        }

        return null;
    }

    /// <summary>The date asked beside a permalink must be the one it names; any other is a malformed request.</summary>
    private static void RequirePinnedDate(string requestedDate, string pinnedDate)
    {
        if (!string.Equals(requestedDate, pinnedDate, StringComparison.Ordinal))
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "A hash-pinned permalink names its own date: the date asked beside it must be that date.");
        }
    }

    /// <summary>
    /// The Luxembourg state a hash-pinned permalink pins, as <c>verify</c> reads it (<see cref="Verify"/>), with every state of
    /// its work and the languages they are held in; or the refusal that stands in for it: <c>no_corpus_mounted</c>;
    /// <c>language_not_available</c> for a language asked beside it that is not the permalink's, or a permalink whose language
    /// the coordinate does not hold; <c>identifier_unknown</c> for a coordinate holding no state; <c>ambiguous_identifier</c>
    /// for two states of its language there, neither the pinned one; and <c>pinned_digest_mismatch</c> naming the current state
    /// when the coordinate holds another in its language.
    /// </summary>
    private V3PlatformOperationOutcome? RefuseUnlessPinnedState(
        V3PlatformOperationRequest request,
        string identifier,
        string workKey,
        string date,
        string digest,
        string language,
        string? requestedLanguage,
        DateTimeOffset observedAt,
        out LuxembourgIndexResolvedState pinned,
        out IReadOnlyList<LuxembourgIndexResolvedState> workStates,
        out string[] availableLanguages)
    {
        pinned = null!;
        workStates = Array.Empty<LuxembourgIndexResolvedState>();
        availableLanguages = Array.Empty<string>();
        if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement));
        }

        var atCoordinate = _reader.ResolveState(workKey, date);
        var heldLanguages = atCoordinate.Select(static state => state.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (atCoordinate.Count == 0)
        {
            return Unknown(request, identifier, observedAt, PublisherId.LuLegilux,
                "a hash-pinned permalink of a state this index holds at that stable coordinate");
        }

        // The permalink names its text's language (R64): a language asked beside it must be that one, and the languages offered
        // are held ones, the permalink's own when the coordinate holds it, as verify answers.
        if ((requestedLanguage is not null && !string.Equals(requestedLanguage, language, StringComparison.Ordinal)) ||
            !heldLanguages.Contains(language, StringComparer.Ordinal))
        {
            using var otherLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = requestedLanguage ?? language,
                available_languages = heldLanguages.Contains(language, StringComparer.Ordinal) ? new[] { language } : heldLanguages,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "language_not_available", otherLanguage.RootElement));
        }

        var ofLanguage = atCoordinate.Where(state => string.Equals(state.Language, language, StringComparison.Ordinal)).ToArray();
        var matching = ofLanguage.Where(state => string.Equals(state.StateSha256, digest, StringComparison.Ordinal)).ToArray();
        if (matching.Length == 0)
        {
            if (ofLanguage.Length > 1)
            {
                using var ambiguous = JsonSerializer.SerializeToDocument(new
                {
                    requested_identifier = identifier,
                    candidates = ofLanguage.Select(StateUrl).ToArray(),
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
            }

            var current = ofLanguage[0];
            using var mismatch = JsonSerializer.SerializeToDocument(new
            {
                requested_digest = digest,
                current_digest = current.StateSha256,
                stable_coordinate = StableCoordinate(current),
                current_hash_pinned_url = StateUrl(current),
                rule_profile_sha256s = current.RuleProfileSha256s,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
        }

        pinned = matching[0];
        workStates = _reader.ResolveWorkStates(workKey);
        availableLanguages = workStates.Select(static state => state.Language)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return null;
    }

    /// <summary>
    /// The EU wording a hash-pinned permalink pins in the time view, as <c>verify</c> reads it (<see cref="VerifyEuropeState"/>):
    /// the selection that serves it (its date's entry, with the pinned wording as the answer, and the next wording's date), or
    /// the refusal that stands in for it: <c>language_not_available</c>; <c>identifier_unknown</c> for a date with no wording
    /// held in that language; <c>ambiguous_version</c> for held texts of that date that differ, none the pinned one; and
    /// <c>pinned_digest_mismatch</c> naming the wording the date answers with.
    /// </summary>
    private V3PlatformOperationOutcome? RefuseUnlessPinnedWording(
        V3PlatformOperationRequest request,
        string identifier,
        EuropeSeedTimeline timeline,
        string language,
        string date,
        string digest,
        string? requestedLanguage,
        DateTimeOffset observedAt,
        out EuropeSelection pinned)
    {
        pinned = null!;
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

        var dated = timeline.ByLanguage[language].Dated;
        var index = -1;
        for (var position = 0; position < dated.Count; position++)
        {
            if (string.Equals(dated[position].Date, date, StringComparison.Ordinal))
            {
                index = position;
            }
        }

        if (index < 0 || dated[index].Held.Count == 0)
        {
            return Unknown(request, identifier, observedAt, PublisherId.EuEurLex,
                "a hash-pinned EU permalink of a wording this EU index holds, at its wording date");
        }

        var entry = dated[index];
        var pins = entry.Held.Select(candidate => (Candidate: candidate, Pin: EuropeStatePin(timeline.SeedCelex, candidate, date))).ToArray();
        var match = pins.FirstOrDefault(held => string.Equals(held.Pin.Sha256, digest, StringComparison.Ordinal));
        if (match.Candidate is null && entry.Answer is null)
        {
            using var ambiguousDate = JsonSerializer.SerializeToDocument(new
            {
                requested_date = date,
                candidates = pins.Select(static held => held.Pin.Permalink).Order(StringComparer.Ordinal).ToArray(),
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_version", ambiguousDate.RootElement));
        }

        if (match.Candidate is null)
        {
            var current = pins.First(held => ReferenceEquals(held.Candidate, entry.Answer));
            using var mismatch = JsonSerializer.SerializeToDocument(new
            {
                requested_digest = digest,
                current_digest = current.Pin.Sha256,
                stable_coordinate = EuropeStableCoordinate(current.Pin.Permalink),
                current_hash_pinned_url = current.Pin.Permalink,
                digest_rule = EuropeStateDigestRule,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
        }

        // The pinned wording stands as its date's answer: a permalink chooses the one held text it pins, so other texts of the
        // same date, or held versions with no usable date, do not make it ambiguous.
        pinned = new EuropeSelection(
            language,
            entry with { Answer = match.Candidate },
            index + 1 < dated.Count ? dated[index + 1].Date : null,
            Array.Empty<(Lex.V3.Ingest.Europe.EuropeIndexState State, EuropeWordingCandidate? Held)>());
        return null;
    }
}
