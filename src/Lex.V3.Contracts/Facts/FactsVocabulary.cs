namespace Lex.V3.Contracts.Facts;

/// <summary>
/// The outcome of reading a value against a closed vocabulary.
///
/// Drift is a result, not an exception and not an extra enum member. An exception would
/// abort a run over one unrecognised term and lose every good fact beside it; an extra
/// member would let the unrecognised term be treated as known. A result forces the caller
/// to handle both outcomes, and carries the drift onward to run accounting so an
/// unclassified term is visible rather than merely absent.
///
/// The hierarchy is closed by a <c>private protected</c> constructor, so no third state
/// can appear and no <c>switch</c> over it can silently stop being exhaustive.
/// </summary>
/// <typeparam name="T">The closed vocabulary being resolved.</typeparam>
public abstract record FactsResult<T>
    where T : struct, Enum
{
    private protected FactsResult()
    {
    }

    /// <summary>Applies whichever handler matches, so neither outcome can be forgotten.</summary>
    public abstract TResult Match<TResult>(
        Func<T, TResult> accepted,
        Func<VocabularyDrift, TResult> drifted);

    /// <summary>The term resolved to a member of the closed vocabulary.</summary>
    /// <param name="Value">The resolved member.</param>
    public sealed record Accepted(T Value) : FactsResult<T>
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<T, TResult> accepted,
            Func<VocabularyDrift, TResult> drifted)
        {
            ArgumentNullException.ThrowIfNull(accepted);
            return accepted(Value);
        }
    }

    /// <summary>The term is not classified by the current manifest.</summary>
    /// <param name="Drift">The unclassified term, with the observation that carried it.</param>
    public sealed record Drifted(VocabularyDrift Drift) : FactsResult<T>
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<T, TResult> accepted,
            Func<VocabularyDrift, TResult> drifted)
        {
            ArgumentNullException.ThrowIfNull(drifted);
            return drifted(Drift);
        }
    }
}

/// <summary>
/// Resolves publisher tokens against the closed vocabularies of the fact graph.
///
/// Every method returns a result rather than a value. There is deliberately no overload
/// that takes a fallback: a default parameter is how an unrecognised term becomes a known
/// one, and the resulting record looks exactly like a correct observation.
/// </summary>
public static class FactsVocabulary
{
    /// <summary>Resolves the reason a relation target is not held.</summary>
    /// <param name="term">The token exactly as published or configured.</param>
    /// <param name="encounteredIn">The field position, so drift is attributable to a place.</param>
    /// <param name="occurrence">The observation and position that carried the term.</param>
    public static FactsResult<UnheldTargetReason> ResolveUnheldReason(
        string term,
        string encounteredIn,
        FactOccurrence occurrence) =>
        Resolve<UnheldTargetReason>(term, encounteredIn, occurrence, UnheldReasonTokens);

    /// <summary>Resolves a declared date precision.</summary>
    /// <param name="term">The token exactly as published.</param>
    /// <param name="encounteredIn">The field position, so drift is attributable to a place.</param>
    /// <param name="occurrence">The observation and position that carried the term.</param>
    public static FactsResult<DatePrecision> ResolvePrecision(
        string term,
        string encounteredIn,
        FactOccurrence occurrence) =>
        Resolve<DatePrecision>(term, encounteredIn, occurrence, PrecisionTokens);

    /// <summary>Resolves whether a publisher date is bounded or open ended.</summary>
    /// <param name="term">The token exactly as published.</param>
    /// <param name="encounteredIn">The field position, so drift is attributable to a place.</param>
    /// <param name="occurrence">The observation and position that carried the term.</param>
    public static FactsResult<OpenSentinelState> ResolveOpenSentinel(
        string term,
        string encounteredIn,
        FactOccurrence occurrence) =>
        Resolve<OpenSentinelState>(term, encounteredIn, occurrence, OpenSentinelTokens);

    private static readonly IReadOnlyDictionary<string, UnheldTargetReason> UnheldReasonTokens =
        new Dictionary<string, UnheldTargetReason>(StringComparer.Ordinal)
        {
            ["outside_body_scope"] = UnheldTargetReason.OutsideBodyScope,
            ["not_yet_held"] = UnheldTargetReason.NotYetHeld,
            ["ecli_missing"] = UnheldTargetReason.EcliMissing,
        };

    private static readonly IReadOnlyDictionary<string, DatePrecision> PrecisionTokens =
        new Dictionary<string, DatePrecision>(StringComparer.Ordinal)
        {
            ["year"] = DatePrecision.Year,
            ["month"] = DatePrecision.Month,
            ["day"] = DatePrecision.Day,
            ["instant"] = DatePrecision.Instant,
        };

    private static readonly IReadOnlyDictionary<string, OpenSentinelState> OpenSentinelTokens =
        new Dictionary<string, OpenSentinelState>(StringComparer.Ordinal)
        {
            ["bounded"] = OpenSentinelState.Bounded,
            ["open_ended"] = OpenSentinelState.OpenEnded,
        };

    private static FactsResult<T> Resolve<T>(
        string term,
        string encounteredIn,
        FactOccurrence occurrence,
        IReadOnlyDictionary<string, T> tokens)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(encounteredIn);

        // Ordinal, so a locale or a case-insensitive comparison cannot quietly widen a
        // closed vocabulary to accept a token the publisher did not use.
        return tokens.TryGetValue(term, out var value)
            ? new FactsResult<T>.Accepted(value)
            : new FactsResult<T>.Drifted(new VocabularyDrift(term, encounteredIn, occurrence));
    }
}
