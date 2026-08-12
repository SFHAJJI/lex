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

    /// <summary>
    /// Reads the reviewed catalogue, keeping only entries whose work and language the corpus
    /// actually holds.
    ///
    /// <para><paramref name="report"/> receives one line per reviewed alias that was dropped for
    /// that reason. Filtering is correct, because WorkSearch.Validate refuses an alias whose
    /// work-language record is absent, so an unfiltered catalogue would fail the build outright.
    /// But dropping silently is not: an alias a human reviewed and expected to be live simply
    /// disappears, and nothing anywhere says so. That is how the EU catalogue came to carry
    /// entries for works the index does not hold without anyone noticing.</para>
    /// </summary>
    public static WorkSearchBuildOptions Load(
        string path,
        string collection,
        IReadOnlySet<(string Work, string Language)>? heldWorks = null,
        Action<string>? report = null)
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
        var scoped = (contract.Aliases
                ?? throw new InvalidDataException("Work enrichment aliases are required."))
            .Select(ToAlias).Where(item => item.Collection == collection)
            .Select(item => item.Value).ToArray();
        var aliases = scoped
            .Where(alias => heldWorks is null || heldWorks.Contains((alias.Work, alias.Language)))
            .ToArray();
        if (report is not null && heldWorks is not null)
            foreach (var dropped in scoped.Except(aliases)
                         .GroupBy(alias => alias.Work, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
                report($"  [enrichment] {collection}: reviewed alias "
                    + string.Join(", ", dropped
                        .OrderBy(alias => alias.Language, StringComparer.Ordinal)
                        .Select(alias => $"'{alias.Value}' ({alias.Language})"))
                    + $" dropped, {dropped.Key} is not held");
        var discovery = (contract.Discovery
                ?? throw new InvalidDataException("Work enrichment discovery is required."))
            .Select(ToDiscovery).Where(item => item.Collection == collection)
            .Where(item => heldWorks is null
                           || heldWorks.Contains((item.Value.Work, item.Value.Language)))
            .Select(item => item.Value).ToArray();
        return new WorkSearchBuildOptions(aliases, discovery,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static void BuildReviewedArtifact(string inputPath, string outputPath, string collection)
    {
        collection = Required(collection, "collection");
        var options = Load(inputPath, collection);
        var artifact = new
        {
            schema = Schema,
            aliases = options.ReviewedAliases
                .OrderBy(alias => alias.Work, StringComparer.Ordinal)
                .ThenBy(alias => alias.Language, StringComparer.Ordinal)
                .ThenBy(alias => WorkSearch.Normalize(alias.Value), StringComparer.Ordinal)
                .ThenBy(alias => alias.Value, StringComparer.Ordinal)
                .Select(alias => new
                {
                    collection,
                    work = alias.Work,
                    language = alias.Language,
                    value = alias.Value,
                    reviewed_by = alias.ReviewedBy,
                }).ToArray(),
            discovery = options.Discovery
                .OrderBy(item => item.Work, StringComparer.Ordinal)
                .ThenBy(item => item.Language, StringComparer.Ordinal)
                .ThenBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => WorkSearch.Normalize(item.Value), StringComparer.Ordinal)
                .ThenBy(item => item.Value, StringComparer.Ordinal)
                .Select(item => new
                {
                    collection,
                    work = item.Work,
                    language = item.Language,
                    kind = item.Kind,
                    value = item.Value,
                    model_deployment = item.ModelDeployment,
                    prompt_sha256 = item.PromptSha256,
                    schema_sha256 = item.SchemaSha256,
                    generated_at = item.GeneratedAt,
                    confidence = item.Confidence,
                    repeat_runs = item.RepeatRuns,
                    agreement_ratio = item.AgreementRatio,
                    evidence = item.Evidence
                        .OrderBy(evidence => evidence.Version, StringComparer.Ordinal)
                        .ThenBy(evidence => evidence.Anchor, StringComparer.Ordinal)
                        .ThenBy(evidence => evidence.TextSha256, StringComparer.Ordinal)
                        .Select(evidence => new
                        {
                            version = evidence.Version,
                            anchor = evidence.Anchor,
                            text_sha256 = evidence.TextSha256,
                        }).ToArray(),
                }).ToArray(),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
        output.WriteByte((byte)'\n');
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
