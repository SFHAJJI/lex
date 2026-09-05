using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// The DECLARED SET of vocabulary pairs that must carry the same wire tokens across publishers,
/// and the assertion that they still do. One entry today.
/// </summary>
/// <remarks>
/// <para>
/// WHY A TABLE AND NOT AN ASSERTION IN EACH PUBLISHER'S TEST FILE. The first cross-publisher check
/// of this kind was written inside the EU-named wire pin, which had two consequences worth
/// avoiding. An LU-side change reddened a file named for EU, so the failure landed where nobody
/// would look for it. And the mirror SET was not written down anywhere: it was whatever assertions
/// people had happened to place, which means a second pair would have arrived as another one-off
/// plus an unwritten assumption. Here the set is a stated thing, a new pair is one row, and the red
/// lands in a file about mirroring.
/// </para>
/// <para>
/// EVERY ROW MUST CARRY ITS REASON, and the reason must be why the two vocabularies are CONSTRAINED
/// to agree, never that they happen to agree. That is not a style rule. Measured across the whole
/// ingest assembly when this was written, FIVE LU/EU vocabulary pairs look like candidates by name
/// and only one is in this table. Two of the others differ for reasons a reader can now see are
/// deliberate: <see cref="LuxembourgFamilyEnumerationOutcomeKind"/> carries CoverProven and
/// CoverRefused for a partition-cover step EU has no equivalent of, and
/// <see cref="LuxembourgDocumentGetAttemptRefusal"/> splits the robots condition into
/// robots_disallowed and robots_bootstrap_not_completed where
/// <see cref="EuDocumentFetchAttemptRefusal"/> has the single robots_bootstrap_refused. A table
/// without reasons is a list that eventually acquires a row because two enums matched on the day
/// somebody looked, and at that point it has stopped being a constraint and become a coincidence
/// check that blocks legitimate divergence.
/// </para>
/// <para>
/// WHAT WOULD FLIP THIS TEST, since the two per-publisher pins already pin each side against a
/// literal and this could look implied by them. It is not implied. Those literals are edited by
/// hand, one lane at a time. Renaming a token in one lane and updating THAT LANE'S pin to match is
/// a green change for both pins and a red change here, which is exactly the drift this exists to
/// catch: the D1-05f rebase found one condition already carrying two names across the two lanes,
/// each side internally consistent.
/// </para>
/// <para>
/// WHAT THE MUTATIONS SHOWED, including where this is NOT the only net, so the file does not claim
/// more than it earned. Three mutations were each watched red. ONE, LU renames a token and updates
/// LU's own pin to match, which is an internally consistent single-lane change: both per-publisher
/// pins stayed green, the closed-vocabulary census stayed green because it pins member NAMES and
/// the member name did not move, and this test was the ONLY failure in 363. That is the drift this
/// table is for and nothing else in the suite sees it. TWO, two EU ordinals swapped so the token
/// SET is equal and the ORDER differs: red here, but also red in EU's own pin and in the census, so
/// for a reorder this is a third net rather than the only one. THREE, a mirrored member loses its
/// wire token attribute: red here through the named missing-token assertion, and EU's own pin threw
/// on the same member. Only the first of those three is uniquely this test's.
/// </para>
/// <para>
/// KNOWN CANDIDATE, DELIBERATELY NOT A ROW. <see cref="LuxembourgQueryExecutionCompletion"/> and
/// <see cref="EuQueryExecutionCompletion"/> carry identical tokens today (all_families_proven,
/// partial_family_refused). Identical tokens is the symptom this table asks about, not the
/// admission criterion, and whether those two are constrained to agree or merely both happen to be
/// a two-member completion state is a question for whoever owns that contract. It is recorded here
/// rather than added, because adding it on the strength of the match would be the exact decay the
/// second paragraph describes.
/// </para>
/// </remarks>
[TestClass]
public sealed class MirroredVocabularyPairTests
{
    /// <param name="Luxembourg">The LU side of the pair.</param>
    /// <param name="Europe">The EU side of the pair.</param>
    /// <param name="Reason">
    /// Why the two are CONSTRAINED to agree. Not a description of the pair, and not an observation
    /// that they currently match.
    /// </param>
    /// <summary>
    /// A declared mirror. <paramref name="Member"/> null means the WHOLE vocabularies mirror each
    /// other; a member name means only that one condition is mirrored, which is the common case
    /// when two vocabularies overlap on a shared dependency without being the same list.
    /// </summary>
    /// <remarks>
    /// The member form was added by R4. Two ruled pairs could not be expressed as whole-vocabulary
    /// rows: EuQueryExecutionRefusal has twenty members and LuxembourgQueryExecutionRefusal twelve,
    /// so asserting their whole token sequences equal would be false, while the two conditions
    /// ruled one-condition-one-name really are constrained to agree. Widening the row rather than
    /// building a second table keeps one declared set of mirrors, which was the point of the table.
    /// </remarks>
    private sealed record MirroredVocabularyPair(
        Type Luxembourg, Type Europe, string Reason, string? Member = null);

    private static readonly MirroredVocabularyPair[] Table =
    [
        new(
            typeof(LuxembourgEnumerationRefusal),
            typeof(EuEnumerationRefusal),
            "Both are the refusal vocabulary of a repeated-enumeration executor, and the "
                + "conditions they name are produced by the SHARED KEYSET PAGINATION PROTOCOL "
                + "rather than by "
                + "either publisher: a status outside the admitted set, a media type outside it, a "
                + "count that is not one nonnegative integer, a delivered key that will not "
                + "round-trip, a row outside the requested partition, a cursor that did not "
                + "advance, an exhausted page budget, a missing custody member, a refused delivery "
                + "proof, a body that is not what the profile promised, and a decode that failed "
                + "on our side. Every one of those is reachable against any endpoint speaking the "
                + "protocol, so a condition present in one executor and absent from the other is a "
                + "gap in that executor rather than a difference between the publishers. The two "
                + "lists were found describing ONE condition under TWO names during the D1-05f "
                + "rebase, which is what this row exists to stop recurring."),
        new(
            typeof(LuxembourgQueryExecutionRefusal),
            typeof(EuQueryExecutionRefusal),
            "AcquisitionOutcomeNotRepresentable refuses on both sides when the CLOSED "
                + "CorpusAcquisitionRefusalReason vocabulary cannot name an acquisition outcome "
                + "faithfully. That is a code dependency on ONE THIRD VOCABULARY, so a reason added "
                + "or renamed there changes what both members mean at once, and the two cannot "
                + "drift apart without one of them becoming wrong. The route each adapter takes "
                + "differs and is carried by the enum type, not by the member name, which is why "
                + "the two were renamed off document_fetch and document_get onto one name.",
            "AcquisitionOutcomeNotRepresentable"),
        new(
            typeof(LuxembourgQueryExecutionRefusal),
            typeof(EuQueryExecutionRefusal),
            "DocumentBodyNotRetained is produced on both sides by THE SAME TWO CUSTODY CALLS: a "
                + "digest-checked reopen raising CustodyIntegrityException, and CustodyHold."
                + "TryHoldAsync returning no receipt. Both adapters depend on those shared helpers "
                + "rather than on anything of their own publisher, so the condition is one "
                + "condition. Stated asymmetry, because it is real and does not break the "
                + "constraint: EU also admits CustodyRequiredException at that catch, so slightly "
                + "more reaches the member on that side. That is a difference in what arrives, not "
                + "in what the member means.",
            "DocumentBodyNotRetained"),
    ];

    private static string TokenOf(Type vocabulary, string member)
    {
        var field = vocabulary.GetField(member)
            ?? throw new InvalidOperationException(vocabulary.Name + " has no member " + member);
        var token = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
        Assert.IsNotNull(
            token,
            vocabulary.Name + "." + member + " is a declared mirror and carries no wire token.");
        return token.Name;
    }

    private static string[] WireTokens(Type vocabulary)
    {
        var names = Enum.GetNames(vocabulary);
        var missing = names
            .Where(name => vocabulary.GetField(name)!
                .GetCustomAttribute<JsonStringEnumMemberNameAttribute>() is null)
            .ToArray();

        // Named rather than left to throw out of a LINQ call: a member added to a mirrored
        // vocabulary without a token is a real failure worth reporting on its own terms, and an
        // InvalidOperationException would send the reader to this helper instead of to the member.
        Assert.AreEqual(
            0,
            missing.Length,
            vocabulary.Name + " is in the mirrored-vocabulary table, so every member must carry an "
                + "explicit wire token. Missing on: " + string.Join(", ", missing));

        return names
            .Select(name => vocabulary.GetField(name)!
                .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!.Name)
            .ToArray();
    }

    [TestMethod]
    public void EveryDeclaredMirroredPairCarriesTheSameWireTokensInTheSameOrder()
    {
        Assert.AreNotEqual(0, Table.Length, "the table must not be empty");

        foreach (var pair in Table)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(pair.Reason),
                pair.Luxembourg.Name + " and " + pair.Europe.Name
                    + " are declared a mirrored pair with no reason. A row without a reason cannot "
                    + "be told apart later from two vocabularies that happened to match.");

            if (pair.Member is { } member)
            {
                // A member row asserts BOTH halves: that each vocabulary really declares a member
                // of that name, and that the two carry the same wire token. Name equality is not
                // implied by the token check, and token equality is not implied by the name.
                foreach (var vocabulary in new[] { pair.Luxembourg, pair.Europe })
                {
                    CollectionAssert.Contains(
                        Enum.GetNames(vocabulary),
                        member,
                        vocabulary.Name + " is declared to mirror " + member
                            + " and does not declare a member of that name. Declared reason: "
                            + pair.Reason);
                }

                Assert.AreEqual(
                    TokenOf(pair.Luxembourg, member),
                    TokenOf(pair.Europe, member),
                    member + " is declared a mirrored condition, so its wire token must be the "
                        + "same on both sides. Declared reason: " + pair.Reason);
                continue;
            }

            // Joined rather than compared element-wise: CollectionAssert reports a count difference
            // for two equal-length sequences that differ only in a token, which is the failure this
            // is for. A string diff names the token that moved.
            Assert.AreEqual(
                string.Join("\n", WireTokens(pair.Luxembourg)),
                string.Join("\n", WireTokens(pair.Europe)),
                pair.Luxembourg.Name + " and " + pair.Europe.Name
                    + " are declared mirrors, so a refusal added, renamed or reordered in one "
                    + "without the other is a divergence to justify here, not to discover later "
                    + "from a census diff. Declared reason: " + pair.Reason);
        }
    }
}
