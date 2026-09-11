using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>One membership answer: does this exact resource hold the class it was asked about.</summary>
/// <param name="Count">
/// 1 is the resource holding the class, 0 is it not. A single-resource range can answer nothing
/// else, so any other value is treated as the range not meaning what it should. Null with a
/// <paramref name="Failure"/> is a request that did not complete.
/// </param>
internal sealed record LuxembourgMembershipAnswer(
    string Label, string ResourceIri, long? Count, string? Failure)
{
    /// <summary>Whether this answer is a clean yes, a clean no, or neither.</summary>
    internal bool IsClean => Failure is null && Count is 0 or 1;

    internal bool Holds => Failure is null && Count == 1;
}

/// <summary>
/// What the first membership pair decided, and what — if anything — may be asked next.
/// </summary>
/// <remarks>
/// <para>
/// THE GATE IS THIS LIST, NOT AN <c>if</c>. The owner authorized five wire requests: the robots
/// bootstrap and two membership checks first, the remaining two spent only against a positively
/// identified relationship. That was first written as a branch around the second pair, and a
/// reviewer mutated the branch to <c>if (true)</c> with the whole suite still green - the live test
/// is skipped by default, so nothing offline could see the two conditionally authorized requests
/// being spent unconditionally.
/// </para>
/// <para>
/// So the gate stopped being control flow. <see cref="CrossCheckTargets"/> is EMPTY unless exactly
/// one candidate holds the class, and the canary asks about whatever it contains. There is no
/// boolean to invert: a mutation that forces the loop to run still sends nothing, and
/// <see cref="LuxembourgOpinionRequestRelationshipDecisionTests"/> pins the emptiness directly.
/// </para>
/// </remarks>
internal sealed record LuxembourgRelationshipDecision(
    string Verdict,
    string? IdentifiedPredicate,
    IReadOnlyList<string> CrossCheckTargets);

/// <summary>
/// The four-answer rule this diagnostic turns on, separated from the sending so it can be tested
/// without a publisher.
/// </summary>
internal static class LuxembourgOpinionRequestRelationship
{
    /// <summary>The draft-to-opinion edge, aliased from the vocabulary that owns it.</summary>
    /// <remarks>
    /// RESTATED ONCE, AND THAT WAS A DEFECT. This was its own literal, and the rule below decides
    /// the identified relationship by comparing against it, while every test derived its expectation
    /// from the same constant - so the coordinate was checked against itself. Changing it to
    /// <c>jolux#draftHasOpinionConseilEtat</c>, a different and real JOLux predicate, left 18 of 18
    /// tests passing while the rule reported the wrong relationship. It now aliases
    /// <see cref="LuxembourgOpinionLinkOnlyVocabulary"/>, which owns this coordinate.
    /// </remarks>
    internal const string HasOpinionPredicateIri =
        LuxembourgOpinionLinkOnlyVocabulary.HasOpinionPredicateIri;

    /// <summary>The draft-to-task edge, which no production type currently owns.</summary>
    /// <remarks>
    /// No vocabulary declares it, so it stays a literal here rather than inventing an owner for a
    /// coordinate this family only needs in order to RULE IT OUT. It is pinned instead against the
    /// retained draft-graph delivery, which is evidence outside this file - see
    /// <c>BothPredicatesAreTheOnesTheRetainedDeliveryActuallyCarries</c>.
    /// </remarks>
    internal const string DraftHasTaskPredicateIri =
        "http://data.legilux.public.lu/resource/ontology/jolux#draftHasTask";

    /// <summary>The first pair: does exactly one candidate hold the class?</summary>
    /// <param name="crossCheckSace">The independent draft's sace resource, asked only on identification.</param>
    /// <param name="crossCheckScac">The independent draft's scac resource, asked only on identification.</param>
    internal static LuxembourgRelationshipDecision DecideFirstPair(
        LuxembourgMembershipAnswer sace,
        LuxembourgMembershipAnswer scac,
        string crossCheckSace,
        string crossCheckScac)
    {
        ArgumentNullException.ThrowIfNull(sace);
        ArgumentNullException.ThrowIfNull(scac);

        if (sace.Failure is not null || scac.Failure is not null)
        {
            return Withhold("AMBIGUOUS: a membership check failed; cross-check withheld.");
        }

        if (!sace.IsClean || !scac.IsClean)
        {
            return Withhold(
                $"AMBIGUOUS: a single-resource range answered {sace.Count}/{scac.Count}, which is "
                + "not 0 or 1; the range does not mean what it should. Cross-check withheld.");
        }

        if (sace.Holds == scac.Holds)
        {
            return Withhold(sace.Holds
                ? "AMBIGUOUS: BOTH candidates hold jolux:OpinionRequest; neither predicate is "
                    + "discriminated. Cross-check withheld."
                : "AMBIGUOUS: NEITHER candidate holds jolux:OpinionRequest; the relationship is not "
                    + "among the two candidates retained evidence offered. Cross-check withheld.");
        }

        return new LuxembourgRelationshipDecision(
            $"IDENTIFIED: {(sace.Holds ? "hasOpinion->sace" : "draftHasTask->scac")} holds "
                + "jolux:OpinionRequest and the other does not. Cross-check authorized.",
            sace.Holds ? HasOpinionPredicateIri : DraftHasTaskPredicateIri,
            [crossCheckSace, crossCheckScac]);
    }

    /// <summary>
    /// The second pair, consumed rather than assumed.
    /// </summary>
    /// <remarks>
    /// The first version of this diagnostic discarded both cross-check answers, so the retained
    /// index kept the first pair's identification with only a <c>crossCheckRun = true</c> beside it
    /// - a packet presented as an independently replicated relationship that would have read
    /// identically had draft B reversed the result. A contradicted or ambiguous cross-check must
    /// retract the identification, not sit quietly next to it.
    /// </remarks>
    internal static LuxembourgRelationshipDecision Conclude(
        LuxembourgRelationshipDecision first,
        IReadOnlyList<LuxembourgMembershipAnswer> crossCheck)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(crossCheck);

        if (first.IdentifiedPredicate is null)
        {
            return crossCheck.Count == 0
                ? first
                : Withhold("AMBIGUOUS: cross-check answers exist for an unidentified first pair, "
                    + "which means the gate was bypassed. The result is not reportable.");
        }

        if (crossCheck.Count != first.CrossCheckTargets.Count)
        {
            return Withhold(
                $"AMBIGUOUS: {first.CrossCheckTargets.Count} cross-check answers were authorized and "
                + $"{crossCheck.Count} arrived. {first.Verdict}");
        }

        var sace = crossCheck[0];
        var scac = crossCheck[1];
        if (!sace.IsClean || !scac.IsClean)
        {
            var failure = sace.Failure ?? scac.Failure;
            var how = failure is null
                ? $"answered {sace.Count}/{scac.Count}"
                : "did not complete: " + failure;
            return Withhold(
                $"AMBIGUOUS: the cross-check {how}, so the first pair's identification is NOT "
                + $"confirmed and does not stand. {first.Verdict}");
        }

        var expectedSaceHolds = string.Equals(
            first.IdentifiedPredicate, HasOpinionPredicateIri, StringComparison.Ordinal);
        if (sace.Holds == scac.Holds)
        {
            return Withhold(
                $"AMBIGUOUS: on the independent draft BOTH candidates answered {sace.Count}, so "
                + $"nothing is discriminated there and the identification does not stand. {first.Verdict}");
        }

        if (sace.Holds != expectedSaceHolds)
        {
            return Withhold(
                "CONTRADICTED: the independent draft reverses the first pair. The relationship is "
                + $"NOT established and no predicate is identified. {first.Verdict}");
        }

        return new LuxembourgRelationshipDecision(
            $"CONFIRMED: {first.Verdict} The independent draft agrees.",
            first.IdentifiedPredicate,
            first.CrossCheckTargets);
    }

    /// <summary>An outcome that identifies nothing and authorizes nothing further.</summary>
    private static LuxembourgRelationshipDecision Withhold(string verdict) => new(verdict, null, []);
}
