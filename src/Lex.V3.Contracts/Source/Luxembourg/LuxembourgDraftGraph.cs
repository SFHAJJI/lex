using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The closed Luxembourg draft-graph relation vocabulary: relations asserted between DRAFT
/// documents and the law they concern, held separately from the final-law relation vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// SEPARATE BY RULING, NOT BY PREFERENCE. Stage 2 item E8 needs <c>draftTransposes</c>, and the
/// obvious place to put it was <see cref="LuxembourgRelationPredicate"/>. The owner's scope ruling
/// on #417 is that it goes here instead and that Decision 65's eighteen-member final-law vocabulary
/// is not widened. This file therefore adds a predicate WITHOUT touching that enum, its count, or
/// its census pin.
/// </para>
/// <para>
/// WHY THE SEPARATION IS THE POINT. A draft that proposes to transpose a directive has not
/// transposed anything: it is a legislative intention, and the act it would become may never exist,
/// may differ, or may be withdrawn. Decision 65's eighteen describe relations between published
/// legal resources, where the assertion is about law that exists. Collapsing the two would let a
/// reader treat "a bill proposed to transpose this directive" as "this directive is transposed",
/// which is a claim about the law of a member state that nobody has made.
/// </para>
/// <para>
/// THE SEPARATION IS ENFORCED, NOT ASSERTED. <see cref="LuxembourgDraftGraphVocabulary"/>'s static
/// initialiser fails if any token here also appears in the final-law vocabulary, so a future
/// predicate added to either side that collides with the other stops the process at first touch
/// rather than quietly making one vocabulary a superset of the other.
/// </para>
/// <para>
/// Tokens are the exact JOLux local predicate name, appended to
/// <c>http://data.legilux.public.lu/resource/ontology/jolux#</c>, the same convention
/// <see cref="LuxembourgRelationPredicate"/> follows.
/// </para>
/// </remarks>
public enum LuxembourgDraftRelationPredicate
{
    /// <summary>
    /// A draft document proposes to transpose an EU act. The V3 spec's E8 line records 1,735
    /// PROVEN-WF triples for this predicate; that figure is the spec's own and has not been
    /// measured by this contract.
    /// </summary>
    [JsonStringEnumMemberName("draftTransposes")]
    DraftTransposes = 1,
}

/// <summary>
/// The closed Luxembourg draft-graph vocabulary, enumerable rather than hand-counted, and provably
/// disjoint from the final-law relation vocabulary.
/// </summary>
public static class LuxembourgDraftGraphVocabulary
{
    static LuxembourgDraftGraphVocabulary()
    {
        var finalLaw = LuxembourgRelationVocabulary.Predicates
            .Select(static predicate => ContractJson.Serialize(predicate).Trim('"'))
            .ToHashSet(StringComparer.Ordinal);

        var collisions = Tokens.Where(finalLaw.Contains).ToArray();
        if (collisions.Length != 0)
        {
            throw new InvalidOperationException(
                "The draft-graph vocabulary and Decision 65's final-law vocabulary must stay " +
                "disjoint; these tokens appear in both: " + string.Join(", ", collisions) + ".");
        }
    }

    /// <summary>Every draft-graph relation predicate.</summary>
    public static IReadOnlyList<LuxembourgDraftRelationPredicate> Predicates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgDraftRelationPredicate>());

    /// <summary>Each predicate's exact wire token, in declaration order.</summary>
    public static IReadOnlyList<string> Tokens { get; } = Array.AsReadOnly(
        Enum.GetValues<LuxembourgDraftRelationPredicate>()
            .Select(static predicate => ContractJson.Serialize(predicate).Trim('"'))
            .ToArray());

    /// <summary>
    /// Whether <paramref name="token"/> is a draft-graph predicate this vocabulary vouches for.
    /// </summary>
    /// <remarks>
    /// A final-law token answers false here, and a draft token answers false to the final-law
    /// vocabulary's own membership. That is the whole contract: neither side admits the other's,
    /// so a caller cannot reach a draft relation through a final-law door by accident.
    /// </remarks>
    public static bool IsDraftGraphToken(string token) =>
        token is not null && Tokens.Contains(token, StringComparer.Ordinal);
}
