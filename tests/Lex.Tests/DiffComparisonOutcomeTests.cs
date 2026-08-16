using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Mcp;

namespace Lex.Tests;

/// <summary>
/// The comparison outcome is the answer to "what changed", and <c>diff</c> already verifies it:
/// whether the provision was held on each date, whether the two texts hash the same, and whether
/// the extraction profiles allow the pairing at all. These tests hold the answer layer to that
/// evidence, because both halves of it were silently dropping the outcome: the named line
/// announced that a comparison existed without saying how it came out, and the evidence ledger
/// handed the synthesis two dated documents with no comparison in them at all.
/// </summary>
public sealed class DiffComparisonOutcomeTests
{
    private static readonly Subject Crr =
        new("eu-eurlex:32013r0575", "Regulation (EU) No 575/2013", "2020-01-01", "art_92", "en");

    private static string Line(DiffView diff, string locale = "en")
    {
        var operation = RequestedOperation.CreatePlanned("req:op-1", 0, "diff", new JsonObject
        {
            ["work"] = "eu-eurlex:32013r0575",
            ["anchor"] = "art_92",
            ["from_date"] = "2020-01-01",
            ["to_date"] = "2024-12-31",
        });
        var result = new OperationExecution(operation).Complete(McpStatus.Ok, new JsonObject());
        return OperationAnswerPolicy.Render(locale, [result], [new UiEffect(Diff: diff)]);
    }

    [Fact]
    public void A_verified_comparison_states_how_it_came_out_not_only_that_it_exists()
    {
        var changed = Line(new DiffView(Crr, "2020-01-01", "2024-12-31",
            "https://law.example/from", "https://law.example/to",
            "the requested provision has different wording on the two dates",
            McpStatus.Ok, null, AnchorFromPresent: true, AnchorToPresent: true,
            AnchorTextEqual: false, ProvisionLevelComparable: true));

        Assert.Contains("Article 92", changed, StringComparison.Ordinal);
        Assert.Contains("different wording", changed, StringComparison.Ordinal);
        Assert.Contains("2020-01-01", changed, StringComparison.Ordinal);
        Assert.Contains("2024-12-31", changed, StringComparison.Ordinal);

        // The negative outcome is a result too. An answer that only speaks up when something moved
        // teaches the reader to read silence as "unchanged", which is the one reading Lex must
        // never license.
        var same = Line(new DiffView(Crr, "2020-01-01", "2024-12-31", null, null,
            "the requested provision has the same wording on both dates",
            McpStatus.Ok, null, AnchorFromPresent: true, AnchorToPresent: true,
            AnchorTextEqual: true, ProvisionLevelComparable: true));
        Assert.Contains("same wording", same, StringComparison.Ordinal);
        Assert.DoesNotContain("different wording", same, StringComparison.Ordinal);
    }

    [Fact]
    public void Presence_on_only_one_date_is_reported_as_presence_never_as_wording()
    {
        var added = Line(new DiffView(Crr, "2020-01-01", "2024-12-31", null, null,
            "the requested provision is present only on the later date",
            McpStatus.Ok, null, AnchorFromPresent: false, AnchorToPresent: true,
            AnchorTextEqual: null, ProvisionLevelComparable: true));
        Assert.Contains("only on the later date", added, StringComparison.Ordinal);

        var removed = Line(new DiffView(Crr, "2020-01-01", "2024-12-31", null, null,
            "the requested provision is present only on the earlier date",
            McpStatus.Ok, null, AnchorFromPresent: true, AnchorToPresent: false,
            AnchorTextEqual: null, ProvisionLevelComparable: true));
        Assert.Contains("only on the earlier date", removed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_work_comparison_reports_the_version_boundary_it_actually_compared()
    {
        var subject = new Subject(
            "eu-eurlex:32013r0575", "Regulation (EU) No 575/2013", "2020-01-01", null, "en");

        var moved = Line(new DiffView(subject, "2020-01-01", "2024-12-31", null, null,
            "different versions applied; retrieve both via as_of (text included) to compare",
            McpStatus.Ok, null, ProvisionLevelComparable: true, Changed: true));
        Assert.Contains("different publisher version", moved, StringComparison.Ordinal);

        var held = Line(new DiffView(subject, "2020-01-01", "2020-06-01", null, null,
            "the same version applied on both dates",
            McpStatus.Ok, null, ProvisionLevelComparable: true, Changed: false));
        Assert.Contains("same publisher version", held, StringComparison.Ordinal);

        // An outcome Lex does not hold is not an outcome Lex states. Without `changed` the line
        // falls back to announcing the comparison rather than inventing a direction for it.
        var unknown = Line(new DiffView(subject, "2020-01-01", "2024-12-31", null, null, null,
            McpStatus.Ok, null, ProvisionLevelComparable: true));
        Assert.DoesNotContain("different publisher version", unknown, StringComparison.Ordinal);
        Assert.DoesNotContain("same publisher version", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void An_incomparable_pairing_keeps_saying_so_and_claims_no_outcome()
    {
        var refused = Line(new DiffView(Crr, "2020-01-01", "2024-12-31", null, null,
            "the two versions were extracted by different profiles",
            McpStatus.ProfilesDiffer, null, AnchorFromPresent: true, AnchorToPresent: true,
            AnchorTextEqual: null, ProvisionLevelComparable: false));
        Assert.Contains("cannot produce a reliable comparison", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("different wording", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("same wording", refused, StringComparison.Ordinal);
    }

    [Fact]
    public void The_French_reader_gets_the_outcome_in_French()
    {
        var changed = Line(new DiffView(Crr, "2020-01-01", "2024-12-31", null, null,
            "the requested provision has different wording on the two dates",
            McpStatus.Ok, null, AnchorFromPresent: true, AnchorToPresent: true,
            AnchorTextEqual: false, ProvisionLevelComparable: true), "fr");
        Assert.Contains("libellé différent", changed, StringComparison.Ordinal);
    }

    /// <summary>
    /// The polarity contract in <c>AgentAnswerContract</c> exists to stop a synthesis inverting a
    /// change, and it reads the outcome off the evidence excerpt. Until the ledger carries that
    /// outcome the contract has nothing to check and passes everything, so this asserts the fact
    /// the contract needs is actually present rather than merely permitted.
    /// </summary>
    [Fact]
    public void A_completed_diff_hands_the_synthesis_the_comparison_not_only_two_dated_documents()
    {
        var ledger = new AgentEvidenceLedger();
        var payload = new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
            ["work"] = "eu-eurlex:32013r0575",
            ["changed"] = true,
            ["provision_level_comparable"] = true,
            ["anchor"] = "art_92",
            ["anchor_from_present"] = true,
            ["anchor_to_present"] = true,
            ["anchor_text_equal"] = false,
            ["note"] = "the requested provision has different wording on the two dates",
            ["from"] = new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32013r0575:2019-12-25",
                ["valid_from"] = "2019-12-25",
            },
            ["to"] = new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32013r0575:2024-07-09",
                ["valid_from"] = "2024-07-09",
            },
        };
        ledger.Observe("diff", McpStatus.Ok,
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32013r0575:2019-12-25",
                ["valid_from"] = "2019-12-25",
                ["permalink"] = "https://law.example/from",
            },
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32013r0575:2024-07-09",
                ["valid_from"] = "2024-07-09",
                ["permalink"] = "https://law.example/to",
            },
        ], payload, new JsonObject
        {
            ["work"] = "eu-eurlex:32013r0575",
            ["anchor"] = "art_92",
            ["from_date"] = "2020-01-01",
            ["to_date"] = "2024-12-31",
        });

        var outcome = Assert.Single(ledger.Evidence, item =>
            item.Kind == AgentEvidenceKind.Change
            && item.Excerpt is not null
            && item.Excerpt.Contains("anchor_text_equal", StringComparison.Ordinal));
        Assert.Equal("art_92", outcome.Anchor);

        var parsed = Assert.IsType<JsonObject>(JsonNode.Parse(outcome.Excerpt!));
        Assert.True(parsed["changed"]!.GetValue<bool>());
        Assert.False(parsed["anchor_text_equal"]!.GetValue<bool>());
        Assert.True(parsed["provision_level_comparable"]!.GetValue<bool>());

        // The contract must now be able to reject an inverted synthesis. Before the outcome
        // reached the ledger this call passed, because zero change facts means nothing to
        // contradict.
        var inverted = new AgentAnswerDraft(AgentAnswerStatus.Answer,
            "Article 92 has the same wording on both dates.",
            [new AgentClaim("Article 92 has the same wording on both dates.",
                AgentClaimKind.Change, [outcome.Id])],
            [], null, null);
        Assert.Throws<InvalidDataException>(() =>
            AgentAnswerContract.Validate(inverted, ledger.Evidence));
    }
}
