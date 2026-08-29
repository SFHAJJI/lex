using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Ask;

namespace Lex.Tests;

public sealed class Canon2UiGapTests
{
    [Fact]
    public void Partial_as_of_maps_text_and_typed_gaps_without_manufacturing_wording()
    {
        var effect = UiMapper.From("as_of", Arguments("full"), Result("ok", includeText: true));

        Assert.NotNull(effect.Provision);
        var provision = effect.Provision!;
        Assert.Equal("partial", provision.TextCompleteness);
        Assert.Single(provision.Provisions);
        var gap = Assert.Single(provision.ProvisionGaps!);
        Assert.Equal("art_2", gap.Anchor);
        Assert.Equal(1, gap.DocumentOrder);
        Assert.Equal("marker_only", gap.TextUnavailableReason);
        Assert.Equal("https://publisher.example/work#art_2", gap.Eli);
        Assert.Equal("https://publisher.example/work", gap.SourceUri);
    }

    [Fact]
    public void Gap_only_as_of_maps_a_typed_refusal_with_the_retained_coordinate()
    {
        var result = Result("text_not_available", includeText: false);
        result["total_provision_gaps"] = 2_001;
        result["truncated"] = true;
        var effect = UiMapper.From("as_of", Arguments("select"), result);

        Assert.Null(effect.Provision);
        Assert.NotNull(effect.Gap);
        var refusal = effect.Gap!;
        Assert.Equal("t-pub:work", refusal.Work);
        var gap = Assert.Single(refusal.ProvisionGaps!);
        Assert.Equal("art_2", gap.Anchor);
        Assert.Equal("marker_only", gap.TextUnavailableReason);
        Assert.Equal("https://publisher.example/work#art_2", gap.Eli);
        Assert.Equal("https://publisher.example/work", gap.SourceUri);
        Assert.Equal(2_001, refusal.TotalProvisionGaps);
        Assert.True(refusal.Truncated);
    }

    [Fact]
    public void As_of_preserves_nullable_document_order_for_assistant_text_rows()
    {
        var result = Result("ok", includeText: true);
        result["provisions"]!.AsArray().Add(new JsonObject
        {
            ["document_order"] = 2,
            ["anchor"] = "art_3",
            ["num"] = "Art. 3",
            ["text"] = "More synthetic publisher wording.",
            ["text_sha256"] = new string('b', 64),
        });

        var effect = UiMapper.From("as_of", Arguments("full"), result);

        var provision = Assert.IsType<ProvisionView>(effect.Provision);
        Assert.Collection(provision.Provisions,
            first => Assert.Equal(0, first.DocumentOrder),
            third => Assert.Equal(2, third.DocumentOrder));

        var wire = JsonNode.Parse(JsonSerializer.Serialize(effect, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }))!.AsObject();
        var rows = wire["provision"]!["provisions"]!.AsArray();
        Assert.Equal(0, rows[0]!["document_order"]!.GetValue<int>());
        Assert.Equal(2, rows[1]!["document_order"]!.GetValue<int>());
    }

    [Fact]
    public void Partial_refusal_preserves_bounded_totals_and_omitted_text_signal()
    {
        var result = Result("text_not_available", includeText: false);
        result["text_completeness"] = "partial";
        result["total_provisions"] = 2_001;
        result["total_provision_gaps"] = 2_000;
        result["truncated"] = true;
        result["text_truncated"] = true;

        var effect = UiMapper.From("as_of", Arguments("full"), result);

        var gap = Assert.IsType<GapView>(effect.Gap);
        Assert.Equal("partial", gap.TextCompleteness);
        Assert.Equal(2_001, gap.TotalProvisions);
        Assert.Equal(2_000, gap.TotalProvisionGaps);
        Assert.True(gap.Truncated);
        Assert.True(gap.TextTruncated);
    }

    [Fact]
    public void Typed_gap_keeps_insecure_eli_separate_from_safe_source_uri()
    {
        var result = Result("ok", includeText: true);
        var row = result["provision_gaps"]!.AsArray()[0]!.AsObject();
        row["eli"] = "http://publisher.example/work#art_2";
        row["source_uri"] = "https://publisher.example/work";

        var effect = UiMapper.From("as_of", Arguments("full"), result);

        var gap = Assert.Single(Assert.IsType<ProvisionView>(effect.Provision).ProvisionGaps!);
        Assert.Equal("http://publisher.example/work#art_2", gap.Eli);
        Assert.Equal("https://publisher.example/work", gap.SourceUri);

        var wire = JsonNode.Parse(JsonSerializer.Serialize(effect, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        }))!.AsObject();
        var wireGap = wire["provision"]!["provision_gaps"]![0]!.AsObject();
        Assert.Equal("http://publisher.example/work#art_2", wireGap["eli"]!.GetValue<string>());
        Assert.Equal("https://publisher.example/work", wireGap["source_uri"]!.GetValue<string>());
        Assert.Null(wireGap["official_source"]);
    }

    [Fact]
    public void Partial_as_of_preserves_the_total_when_only_a_bounded_gap_page_is_returned()
    {
        var result = Result("ok", includeText: true);
        result["total_provision_gaps"] = 2_000;
        result["truncated"] = true;

        var effect = UiMapper.From("as_of", Arguments("full"), result);

        var provision = Assert.IsType<ProvisionView>(effect.Provision);
        Assert.Equal(2_000, provision.TotalProvisionGaps);
        Assert.Single(provision.ProvisionGaps!);
        var answer = Answer(effect);
        Assert.Contains("published coordinate(s)", answer, StringComparison.Ordinal);
        Assert.Contains("1 typed gap", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("but 1 published coordinate(s)", answer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_as_of_with_only_gap_rows_inside_the_cap_is_not_erased()
    {
        var result = Result("ok", includeText: false);
        result["text_completeness"] = "partial";
        result["total_provisions"] = 2_001;
        result["total_provision_gaps"] = 2_000;
        result["truncated"] = true;
        result["text_truncated"] = true;

        var effect = UiMapper.From("as_of", Arguments("full"), result);

        var provision = Assert.IsType<ProvisionView>(effect.Provision);
        Assert.Empty(provision.Provisions);
        Assert.Single(provision.ProvisionGaps!);
        Assert.Equal(2_000, provision.TotalProvisionGaps);
        Assert.True(provision.TextTruncated);
        var answer = Answer(effect);
        Assert.Contains("omits some held publisher text", answer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Mixed_outline_describes_coordinates_without_claiming_to_show_text()
    {
        var result = Result("ok", includeText: true);
        result["provisions"]!.AsArray()[0]!.AsObject().Remove("text");

        var effect = UiMapper.From("as_of", Arguments("outline"), result);

        var provision = Assert.IsType<ProvisionView>(effect.Provision);
        Assert.True(provision.OutlineOnly);
        var answer = Answer(effect);
        Assert.Contains("table of contents", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("shows the available certified publisher text", answer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_gap_in_a_mixed_document_scopes_unavailable_text_to_the_coordinate()
    {
        var result = Result("text_not_available", includeText: false);
        result["text_completeness"] = "partial";
        result["total_provisions"] = 2;

        var effect = UiMapper.From("as_of", Arguments("select"), result);

        var gap = Assert.IsType<GapView>(effect.Gap);
        Assert.Contains("other coordinates", gap.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("no safely derived provision text is available",
            gap.Explanation, StringComparison.Ordinal);
    }

    private static JsonObject Arguments(string mode) => new()
    {
        ["work"] = "t-pub:work",
        ["date"] = "2025-01-01",
        ["mode"] = mode,
    };

    private static JsonObject Result(string status, bool includeText)
    {
        var result = new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = status },
            ["document"] = new JsonObject
            {
                ["lex_id"] = "t-pub:work:2025-01-01",
                ["title"] = "Synthetic work",
                ["valid_from"] = "2025-01-01",
                ["source_uri"] = "https://publisher.example/work",
            },
            ["text_completeness"] = includeText ? "partial" : "unavailable",
            ["total_provisions"] = includeText ? 2 : 1,
            ["total_provision_gaps"] = 1,
            ["provisions"] = new JsonArray(),
            ["provision_gaps"] = new JsonArray
            {
                new JsonObject
                {
                    ["document_order"] = 1,
                    ["anchor"] = "art_2",
                    ["num"] = "Art. 2",
                    ["text_available"] = false,
                    ["text_unavailable_reason"] = "marker_only",
                    ["eli"] = "https://publisher.example/work#art_2",
                    ["source_uri"] = "https://publisher.example/work",
                },
            },
        };
        if (includeText)
            result["provisions"]!.AsArray().Add(new JsonObject
            {
                ["document_order"] = 0,
                ["anchor"] = "art_1",
                ["num"] = "Art. 1",
                ["text"] = "Synthetic publisher wording.",
                ["text_sha256"] = new string('a', 64),
            });
        return result;
    }

    private static string Answer(UiEffect effect) => OperationAnswerPolicy.Render(
        "en",
        [new OperationResult("as-of", 0, null, null, LegalOutcome.Succeeded,
            TransportOutcome.Completed, [], null)],
        [effect]);
}
