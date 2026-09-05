using System;
using System.Linq;
using System.Text.Json.Serialization;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU refusal vocabularies' exact WIRE NAMES, pinned member by member. The mirror of
/// <c>LuxembourgEnumerationRefusalWireNameTests</c>, which existed for LU while EU's equivalent
/// vocabulary had no wire-name pin of any kind.
/// </summary>
/// <remarks>
/// <para>
/// THE GAP THIS CLOSES, found at the rebase onto LU-2. Adding a member to
/// <see cref="EuEnumerationRefusal"/> reddened the closed-vocabulary census, which pins member
/// NAMES, and nothing else. The census's own remarks say it does not pin wire tokens and that the
/// per-type pins own those. For EU there were no per-type pins, so a wire name could have been
/// renamed, or a new member could have shipped with any token at all, and every test would still
/// have passed. LU had this covered; EU did not, and the asymmetry was invisible because each lane
/// only ever read its own side.
/// </para>
/// <para>
/// THE NUMBERS ARE DELIBERATELY NOT PINNED, for the same reason LU gives: they are internal
/// ordinals, the wire names are the contract, and pinning both makes an honest dense renumber look
/// like a breaking change. This lane deleted a member and renumbered densely during D1-05f, which
/// is exactly the change that must stay cheap.
/// </para>
/// <para>
/// EuEnumerationRefusal's list below is BYTE IDENTICAL to the list LU pins for
/// <c>LuxembourgEnumerationRefusal</c>. That is intended, not coincidence: the two executors refuse
/// on the same conditions in the same order, and a reader comparing the two files should be able to
/// see divergence immediately. The divergence this pin was written after was real: the same defect
/// was fixed in both lanes on the same day and named <c>PageDecodeFailed</c> here and
/// <c>PageDecodeFailedOnOurSide</c> there, with tokens to match. EU adopted LU's name because LU's
/// had already merged. The assertion that the two lanes AGREE is deliberately NOT here: it is a
/// row in <see cref="Census.MirroredVocabularyPairTests"/>, so that an LU-side change reddens a
/// file about mirroring rather than this one, which is named for EU. This file pins the EU side
/// against a literal and nothing else, which is the half that belongs under an EU name.
/// </para>
/// <para>
/// When this fails, re-derive rather than hand edit: print the attribute names in
/// <see cref="Enum.GetValues{T}()"/> order from a throwaway test and paste the block. Never build
/// the expected side from the enum inside this test; it would then agree with whatever the code
/// says, which is the one thing a pin must not do.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuRefusalWireNameTests
{
    private static string[] WireNames<T>() where T : struct, Enum =>
        Enum.GetValues<T>()
            .Select(static value => typeof(T)
                .GetField(value.ToString())!
                .GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), false)
                .Cast<JsonStringEnumMemberNameAttribute>()
                .Single()
                .Name)
            .ToArray();

    [TestMethod]
    public void TheEnumerationRefusalVocabularyKeepsItsExactWireNames()
    {
        // Joined rather than compared element-wise: CollectionAssert reports a count difference for
        // two equal-length sequences that differ only in a name, which is the failure this pin
        // exists to describe. A string diff names the wire name that moved.
        Assert.AreEqual(
            string.Join("\n", new[]
            {
                "none",
                "robots_bootstrap_refused",
                "observation_not_executed",
                "status_not_admitted",
                "media_type_not_admitted",
                "count_not_one_nonnegative_integer",
                "partition_required",
                "delivered_key_not_representable",
                "delivered_row_outside_partition",
                "cursor_did_not_advance",
                "page_budget_exhausted",
                "custody_member_missing",
                "delivery_proof_refused",
                "page_body_malformed",
                "page_decode_failed_on_our_side",
            }),
            string.Join("\n", WireNames<EuEnumerationRefusal>()),
            "a wire name changing is a contract change; a number changing is not.");

        // custody_floor_not_observed is gone and must stay gone: its only producer was the
        // pre-emptive floor gate, and the genuine failures it once stood for now surface as
        // custody_member_missing.
        CollectionAssert.DoesNotContain(
            WireNames<EuEnumerationRefusal>(), "custody_floor_not_observed");
    }

    [TestMethod]
    public void TheWitnessTraversalRefusalVocabularyKeepsItsExactWireNames()
    {
        Assert.AreEqual(
            string.Join("\n", new[]
            {
                "none",
                "robots_bootstrap_refused",
                "bind_refused",
                "observation_not_executed",
                "status_not_admitted",
                "media_type_not_admitted",
                "page_body_malformed",
                "crossing_refused",
                "step_refused",
                "entry_set_refused",
                "page_budget_exhausted",
                "page_decode_failed_on_our_side",
            }),
            string.Join("\n", WireNames<EuWitnessTraversalRefusal>()),
            "a wire name changing is a contract change; a number changing is not.");
    }
}
