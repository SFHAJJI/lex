using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// Stage 2 item E8: the draft-graph relation vocabulary, and the separation from final law that is
/// the reason it exists at all.
/// </summary>
/// <remarks>
/// The owner's #417 ruling put <c>draftTransposes</c> in its own closed vocabulary rather than
/// widening Decision 65's eighteen. These guards are about that boundary rather than about the
/// predicate: a draft that proposes to transpose a directive has not transposed anything, and a
/// vocabulary that admitted both kinds through one door would let that distinction be lost by a
/// caller who never intended to make the claim.
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftGraphTests
{
    [TestMethod]
    public void TheDraftGraphVocabularyIsExactlyThisOnePredicate()
    {
        CollectionAssert.AreEqual(
            new[] { "DraftTransposes" }, Enum.GetNames<LuxembourgDraftRelationPredicate>());
        CollectionAssert.AreEqual(
            new[] { "draftTransposes" }, LuxembourgDraftGraphVocabulary.Tokens.ToArray());
    }

    /// <summary>
    /// Decision 65's eighteen are untouched by this slice. Pinned here as a count read from the
    /// vocabulary itself, so widening it while claiming not to would fail.
    /// </summary>
    [TestMethod]
    public void DecisionSixtyFivesFinalLawVocabularyStillCarriesExactlyEighteen()
    {
        Assert.HasCount(18, LuxembourgRelationVocabulary.Predicates);
    }

    /// <summary>
    /// The two vocabularies share no token in either direction.
    /// </summary>
    /// <remarks>
    /// Asserted both ways round on purpose. "No draft token is a final-law token" alone would still
    /// hold if the final-law vocabulary grew a draft predicate, which is the widening the ruling
    /// forbids; checking the reverse catches that from the side it would actually arrive on.
    /// </remarks>
    [TestMethod]
    public void TheDraftGraphAndFinalLawVocabulariesAreDisjointInBothDirections()
    {
        var finalLaw = LuxembourgRelationVocabulary.Predicates
            .Select(static predicate => ContractJson.Serialize(predicate).Trim('"'))
            .ToArray();

        foreach (var draftToken in LuxembourgDraftGraphVocabulary.Tokens)
        {
            CollectionAssert.DoesNotContain(
                finalLaw, draftToken,
                $"'{draftToken}' is a draft-graph predicate and must not appear in Decision 65's eighteen.");
        }

        foreach (var finalLawToken in finalLaw)
        {
            Assert.IsFalse(
                LuxembourgDraftGraphVocabulary.IsDraftGraphToken(finalLawToken),
                $"'{finalLawToken}' is final-law and must not be admitted as a draft-graph predicate.");
        }
    }

    /// <summary>
    /// The membership door refuses a real final-law predicate, which is the collapse this
    /// vocabulary exists to prevent rather than an arbitrary unknown string.
    /// </summary>
    [TestMethod]
    public void ARealFinalLawPredicateIsNotAdmittedAsADraftGraphPredicate()
    {
        Assert.IsFalse(
            LuxembourgDraftGraphVocabulary.IsDraftGraphToken("transposes"),
            "a published act transposing a directive is not a draft proposing to.");
        Assert.IsFalse(LuxembourgDraftGraphVocabulary.IsDraftGraphToken("modifies"));
        Assert.IsTrue(LuxembourgDraftGraphVocabulary.IsDraftGraphToken("draftTransposes"));
    }
}
