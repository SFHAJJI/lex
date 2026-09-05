using System.Text.Json.Serialization;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Luxembourg enumeration refusal vocabulary's WIRE NAMES, pinned. No such pin existed, so the
/// removal of custody_floor_not_observed and the dense renumber that followed it were unguarded:
/// nothing would have caught a renumber that also changed a wire spelling, and the numbers are
/// exactly what the renumber moved.
/// </summary>
/// <remarks>
/// The numbers are deliberately NOT pinned. They are internal ordinals; the wire names are the
/// contract, and pinning both would make an honest dense renumber look like a breaking change.
/// </remarks>
[TestClass]
public sealed class LuxembourgEnumerationRefusalWireNameTests
{
    [TestMethod]
    public void TheRefusalVocabularyKeepsItsExactWireNames()
    {
        var actual = Enum.GetValues<LuxembourgEnumerationRefusal>()
            .Select(static value => typeof(LuxembourgEnumerationRefusal)
                .GetField(value.ToString())!
                .GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), false)
                .Cast<JsonStringEnumMemberNameAttribute>()
                .Single()
                .Name)
            .ToArray();

        // Joined rather than compared element-wise: CollectionAssert reports a count difference
        // for two equal-length sequences that differ only in a name, which is the failure this
        // pin exists to describe. A string diff names the wire name that moved.
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
            string.Join("\n", actual),
            "a wire name changing is a contract change; a number changing is not.");

        // custody_floor_not_observed is gone and must stay gone: its only producer was the
        // pre-emptive floor gate, and the genuine failures it once stood for now surface as
        // custody_member_missing.
        CollectionAssert.DoesNotContain(actual, "custody_floor_not_observed");
    }
}
