using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Lex.Index;
using Microsoft.Data.Sqlite;
using OpenAI.Chat;

namespace Lex.RetrievalExperiment;

public static class EnrichmentSampleGenerator
{
    private const string Instructions = """
        You extract search-discovery metadata from official legal evidence. Return one JSON object
        with an `items` array. Each item has: `kind`, `value`, `confidence`, and `evidence`.
        Allowed kinds are alias, acronym, description, concept, search_synonym, and practice_area.
        Evidence is an array of objects containing exactly `anchor` and `text_sha256`, copied from
        the supplied evidence. Produce concise French search language a lawyer or compliance user
        might use. An alias or acronym is allowed only when it is a genuinely recognised name, not
        an invented abbreviation. Do not state legal effect, dates, status, hierarchy, identifiers,
        or official titles. Do not modify or quote legal text. Prefer 8 to 14 useful items. Every
        item must be supported by at least one supplied evidence anchor. Output JSON only.
        Confidence must be a JSON number from 0 through 1, never a quoted string.
        """;

    public static string PromptHash => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(Instructions)));

    public static async Task<JsonObject> RunAsync(
        string workbenchPath,
        Uri endpoint,
        string deployment,
        IReadOnlyList<(string Work, string Language)> fixtures,
        int runCount,
        CancellationToken cancellationToken)
    {
        if (runCount < 2) throw new ArgumentOutOfRangeException(nameof(runCount), "At least two runs are required.");
        ChatClient chat = new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
            .GetChatClient(deployment);
        var workReports = new JsonArray();
        foreach (var fixture in fixtures)
        {
            WorkEvidence evidence;
            try
            {
                evidence = LoadEvidence(workbenchPath, fixture.Work, fixture.Language);
            }
            catch (InvalidDataException exception)
            {
                workReports.Add(new JsonObject
                {
                    ["work"] = fixture.Work,
                    ["language"] = fixture.Language,
                    ["status"] = "source_evidence_unavailable",
                    ["error"] = exception.Message,
                });
                Console.Error.WriteLine($"[enrichment] {fixture.Work}/{fixture.Language}: source evidence unavailable");
                continue;
            }
            var held = evidence.Provisions.Select(provision => new EvidenceAnchor(
                fixture.Work, evidence.Version, provision.Anchor, provision.TextSha)).ToHashSet();
            var runs = new List<IReadOnlyList<ModelDiscoveryCandidate>>();
            var rawRuns = new JsonArray();
            for (var run = 1; run <= runCount; run++)
            {
                try
                {
                    var completion = (await chat.CompleteChatAsync(
                        [new SystemChatMessage(Instructions), new UserChatMessage(evidence.Prompt)],
                        new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() },
                        cancellationToken)).Value;
                    var raw = completion.Content.Count > 0 ? completion.Content[0].Text : "{}";
                    try
                    {
                        var parsed = ModelDiscoveryParser.Parse(raw, fixture.Work, evidence.Version, held);
                        runs.Add(parsed.Candidates);
                        rawRuns.Add(new JsonObject
                        {
                            ["run"] = run,
                            ["input_tokens"] = completion.Usage?.InputTokenCount,
                            ["output_tokens"] = completion.Usage?.OutputTokenCount,
                            ["raw"] = raw,
                            ["parsed_count"] = parsed.Candidates.Count,
                            ["rejected_items"] = new JsonArray(parsed.RejectedItems.Select(rejection =>
                                (JsonNode)new JsonObject
                                {
                                    ["item"] = rejection.Item,
                                    ["reason"] = rejection.Reason,
                                }).ToArray()),
                        });
                    }
                    catch (Exception exception) when (exception is JsonException or InvalidDataException)
                    {
                        runs.Add([]);
                        rawRuns.Add(new JsonObject
                        {
                            ["run"] = run,
                            ["input_tokens"] = completion.Usage?.InputTokenCount,
                            ["output_tokens"] = completion.Usage?.OutputTokenCount,
                            ["raw"] = raw,
                            ["parsed_count"] = 0,
                            ["parse_error"] = exception.Message,
                        });
                    }
                }
                catch (RequestFailedException exception)
                {
                    runs.Add([]);
                    rawRuns.Add(new JsonObject
                    {
                        ["run"] = run,
                        ["parsed_count"] = 0,
                        ["request_error"] = $"{exception.Status}:{exception.ErrorCode}",
                    });
                }
            }

            var proposals = ProposalConsensus.Merge(
                fixture.Work, fixture.Language, runs, deployment, PromptHash);
            var validation = EnrichmentContract.Validate(proposals, held);
            workReports.Add(new JsonObject
            {
                ["work"] = fixture.Work,
                ["language"] = fixture.Language,
                ["status"] = "completed",
                ["version"] = evidence.Version,
                ["official_title"] = evidence.Title,
                ["evidence_count"] = held.Count,
                ["runs"] = rawRuns,
                ["proposals"] = ProposalJson(proposals),
                ["accepted"] = JsonNode.Parse(EnrichmentContract.Export(validation.Accepted)),
                ["rejected"] = RejectionJson(validation.Rejected),
            });
            Console.Error.WriteLine($"[enrichment] {fixture.Work}/{fixture.Language}: "
                + $"proposals={proposals.Count} accepted={validation.Accepted.Count} rejected={validation.Rejected.Count}");
        }

        return new JsonObject
        {
            ["schema"] = "lex-enrichment-sample/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["endpoint_host"] = endpoint.Host,
            ["model_deployment"] = deployment,
            ["prompt_hash"] = PromptHash,
            ["run_count"] = runCount,
            ["works"] = workReports,
        };
    }

    private static WorkEvidence LoadEvidence(string workbenchPath, string work, string language)
    {
        using var workbench = new SqliteConnection(
            $"Data Source={Path.GetFullPath(workbenchPath)};Mode=ReadOnly;Pooling=False");
        workbench.Open();
        using var command = workbench.CreateCommand();
        command.CommandText = """
            SELECT group_key,title,latest_valid_from,kind,hierarchy,domains,act_form,source_index
            FROM publisher_works WHERE work=$work AND language=$language
            """;
        command.Parameters.AddWithValue("$work", work);
        command.Parameters.AddWithValue("$language", language);
        using var row = command.ExecuteReader();
        if (!row.Read()) throw new InvalidDataException($"Fixture {work}/{language} is absent from the workbench.");
        var groupKey = row.GetString(0);
        var title = row.IsDBNull(1) ? null : row.GetString(1);
        var version = row.GetString(2);
        var kind = row.IsDBNull(3) ? null : row.GetString(3);
        var hierarchy = row.IsDBNull(4) ? null : row.GetString(4);
        var domains = row.IsDBNull(5) ? null : row.GetString(5);
        var actForm = row.IsDBNull(6) ? null : row.GetString(6);
        var sourceIndex = row.GetString(7);
        row.Close();

        using var index = LexIndexReader.Open(sourceIndex);
        var document = index.AsOf(groupKey, DateOnly.Parse(version), new FilterSet(null, null, null, language))
            ?? throw new InvalidDataException($"Fixture {work}/{language}/{version} cannot be read from its source index.");
        var provisions = index.Provisions(LexIndexReader.RidOf(document));
        if (provisions.Count == 0)
            throw new InvalidDataException($"Fixture {work}/{language}/{version} has no provision evidence.");

        var prompt = new StringBuilder();
        prompt.AppendLine("JSON discovery extraction request");
        prompt.AppendLine($"work: {work}");
        prompt.AppendLine($"language: {language}");
        prompt.AppendLine($"official title: {title ?? "[not supplied]"}");
        prompt.AppendLine($"publisher document type: {kind ?? "[not supplied]"}");
        prompt.AppendLine($"normalized hierarchy: {hierarchy ?? "[not supplied]"}");
        prompt.AppendLine($"reviewed scope domains: {domains ?? "[not supplied]"}");
        prompt.AppendLine($"act form: {actForm ?? "[not supplied]"}");
        prompt.AppendLine("official provision evidence (excerpted only for discovery extraction):");
        foreach (var provision in provisions)
        {
            var excerpt = string.Join(' ', provision.TextMd.Split(
                [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            if (excerpt.Length > 500) excerpt = excerpt[..500];
            var line = $"- anchor={provision.Anchor}; text_sha256={provision.TextSha}; text={excerpt}";
            if (prompt.Length + line.Length > 55_000) break;
            prompt.AppendLine(line);
        }
        return new WorkEvidence(title, version, provisions, prompt.ToString());
    }

    private static JsonArray ProposalJson(IReadOnlyList<EnrichmentProposal> proposals) => new(
        proposals.Select(proposal => (JsonNode)new JsonObject
        {
            ["work"] = proposal.Work,
            ["language"] = proposal.Language,
            ["kind"] = proposal.Kind,
            ["value"] = proposal.Value,
            ["confidence"] = proposal.Confidence,
            ["repeat_runs"] = proposal.RepeatRuns,
            ["agreement_ratio"] = proposal.AgreementRatio,
            ["evidence"] = new JsonArray(proposal.Evidence.Select(item => (JsonNode)new JsonObject
            {
                ["work"] = item.Work,
                ["version"] = item.Version,
                ["anchor"] = item.Anchor,
                ["text_sha256"] = item.TextSha256,
            }).ToArray()),
        }).ToArray());

    private static JsonArray RejectionJson(IReadOnlyList<RejectedEnrichment> rejected) => new(
        rejected.Select(item => (JsonNode)new JsonObject
        {
            ["work"] = item.Proposal.Work,
            ["language"] = item.Proposal.Language,
            ["kind"] = item.Proposal.Kind,
            ["value"] = item.Proposal.Value,
            ["reason"] = item.Reason,
            ["repeat_runs"] = item.Proposal.RepeatRuns,
            ["agreement_ratio"] = item.Proposal.AgreementRatio,
        }).ToArray());

    private sealed record WorkEvidence(
        string? Title,
        string Version,
        IReadOnlyList<ProvisionRow> Provisions,
        string Prompt);
}
