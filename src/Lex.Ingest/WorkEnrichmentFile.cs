using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Index;

namespace Lex.Ingest;

/// <summary>Reads the reviewed output of the disposable enrichment workbench.</summary>
public static class WorkEnrichmentFile
{
    private const string Schema = "lex-work-enrichment/1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static WorkSearchBuildOptions Load(string path, string collection)
    {
        var bytes = File.ReadAllBytes(path);
        Contract contract;
        try
        {
            contract = JsonSerializer.Deserialize<Contract>(bytes, JsonOptions)
                ?? throw new JsonException("The document is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Work enrichment file '{path}' could not be parsed.", error);
        }

        if (contract.Schema != Schema)
            throw new InvalidDataException(
                $"Work enrichment schema '{contract.Schema}' is unsupported; expected '{Schema}'.");
        var aliases = (contract.Aliases
                ?? throw new InvalidDataException("Work enrichment aliases are required."))
            .Select(ToAlias).Where(item => item.Collection == collection)
            .Select(item => item.Value).ToArray();
        var discovery = (contract.Discovery
                ?? throw new InvalidDataException("Work enrichment discovery is required."))
            .Select(ToDiscovery).Where(item => item.Collection == collection)
            .Select(item => item.Value).ToArray();
        return new WorkSearchBuildOptions(aliases, discovery,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static (string Collection, ReviewedWorkAliasRow Value) ToAlias(Alias item) =>
        (Required(item.Collection, "alias.collection"), new ReviewedWorkAliasRow(
            Required(item.Work, "alias.work"),
            Required(item.Language, "alias.language"),
            Required(item.Value, "alias.value"),
            Required(item.ReviewedBy, "alias.reviewed_by")));

    private static (string Collection, WorkDiscoveryRow Value) ToDiscovery(Discovery item) =>
        (Required(item.Collection, "discovery.collection"), new WorkDiscoveryRow(
            Required(item.Work, "discovery.work"),
            Required(item.Language, "discovery.language"),
            Required(item.Kind, "discovery.kind"),
            Required(item.Value, "discovery.value"),
            Required(item.ModelDeployment, "discovery.model_deployment"),
            Required(item.PromptSha256, "discovery.prompt_sha256"),
            Required(item.SchemaSha256, "discovery.schema_sha256"),
            Required(item.GeneratedAt, "discovery.generated_at"),
            item.Confidence,
            item.RepeatRuns,
            item.AgreementRatio,
            (item.Evidence ?? throw new InvalidDataException("discovery.evidence is required."))
                .Select(evidence => new WorkEvidenceAnchor(
                    Required(evidence.Version, "evidence.version"),
                    Required(evidence.Anchor, "evidence.anchor"),
                    Required(evidence.TextSha256, "evidence.text_sha256")))
                .ToArray()));

    private static string Required(string? value, string path) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{path} is required.")
            : value;

    private sealed class Contract
    {
        public string? Schema { get; init; }
        public Alias[]? Aliases { get; init; }
        public Discovery[]? Discovery { get; init; }
    }

    private sealed class Alias
    {
        public string? Collection { get; init; }
        public string? Work { get; init; }
        public string? Language { get; init; }
        public string? Value { get; init; }
        public string? ReviewedBy { get; init; }
    }

    private sealed class Discovery
    {
        public string? Collection { get; init; }
        public string? Work { get; init; }
        public string? Language { get; init; }
        public string? Kind { get; init; }
        public string? Value { get; init; }
        public string? ModelDeployment { get; init; }
        public string? PromptSha256 { get; init; }
        public string? SchemaSha256 { get; init; }
        public string? GeneratedAt { get; init; }
        public double Confidence { get; init; }
        public int RepeatRuns { get; init; }
        public double AgreementRatio { get; init; }
        public Evidence[]? Evidence { get; init; }
    }

    private sealed class Evidence
    {
        public string? Version { get; init; }
        public string? Anchor { get; init; }
        public string? TextSha256 { get; init; }
    }
}
