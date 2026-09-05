using System.Text.Json.Serialization;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Luxembourg refusal vocabularies' WIRE NAMES, pinned member by member: the enumeration
/// vocabulary, whose pin existed first, and the query-execution vocabulary, which R4 added.
/// </summary>
/// <remarks>
/// The enumeration pin came first because none existed: the removal of custody_floor_not_observed
/// and the dense renumber that followed were unguarded, and nothing would have caught a renumber
/// that also changed a wire spelling. That class was named for the one vocabulary it pinned and
/// lived in a file named for SPARQL rights channels; R4 moved it here and widened the name, so a
/// second Luxembourg vocabulary had an obvious place to be pinned. This is the mirror of
/// <c>EuRefusalWireNameTests</c>.
/// </remarks>
/// <remarks>
/// The numbers are deliberately NOT pinned. They are internal ordinals; the wire names are the
/// contract, and pinning both would make an honest dense renumber look like a breaking change.
/// </remarks>
[TestClass]
public sealed class LuxembourgRefusalWireNameTests
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
    public void TheRefusalVocabularyKeepsItsExactWireNames()
    {
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
            string.Join("\n", WireNames<LuxembourgEnumerationRefusal>()),
            "a wire name changing is a contract change; a number changing is not.");

        // custody_floor_not_observed is gone and must stay gone: its only producer was the
        // pre-emptive floor gate, and the genuine failures it once stood for now surface as
        // custody_member_missing.
        CollectionAssert.DoesNotContain(
            WireNames<LuxembourgEnumerationRefusal>(), "custody_floor_not_observed");
    }

    /// <summary>
    /// The query-execution refusal vocabulary, every member. R4 declared a token on 10 of these
    /// 12; before that the undeclared ones serialized as their CLR member names and nothing
    /// pinned them.
    /// </summary>
    [TestMethod]
    public void TheQueryExecutionRefusalVocabularyKeepsItsExactWireNames()
    {
        // Joined rather than compared element-wise: CollectionAssert reports a count difference for
        // two equal-length sequences that differ only in a name, which is the failure this pin
        // exists to describe. A string diff names the wire name that moved.
        Assert.AreEqual(
            string.Join("\n", new[]
            {
                "none",
                "scope_resolution_failed",
                "scope_manifest_not_retained",
                "resource_observation_family_not_proven",
                "resource_observation_rows_not_verified",
                "observation_subject_not_in_delivered_census",
                "assertion_row_object_kind_not_recognised",
                "assertion_row_term_unbound",
                "document_fetch_session_not_started",
                "document_body_not_retained",
                "acquisition_outcome_not_representable",
                "record_set_not_retained",
            }),
            string.Join("\n", WireNames<LuxembourgQueryExecutionRefusal>()),
            "a wire name changing is a contract change; a number changing is not.");
    }
}
