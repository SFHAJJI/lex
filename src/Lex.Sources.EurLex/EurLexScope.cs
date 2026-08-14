using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lex.Sources.EurLex;

public sealed record EurLexScopeConfig(
    string Schema,
    string ScopeId,
    int ApprovedWave,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> LegalForms,
    IReadOnlyList<string> BindingStatuses,
    EurLexHistoryRules History,
    EurLexClosureRules RelationshipClosure,
    IReadOnlyList<EurLexDomain> Domains,
    IReadOnlyList<EurLexExclusion> Exclusions)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static EurLexScopeConfig Load(string? path = null)
        => LoadWithDigest(path).Scope;

    internal static (EurLexScopeConfig Scope, string Sha256) LoadWithDigest(string? path = null)
    {
        byte[] bytes;
        if (path is null)
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Lex.Sources.EurLex.eu-scope.json")
                ?? throw new InvalidOperationException("Embedded EU scope configuration is missing.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        else
            bytes = File.ReadAllBytes(path);
        var scope = JsonSerializer.Deserialize<EurLexScopeConfig>(bytes, Json)
                    ?? throw new InvalidDataException("EU scope configuration is empty.");
        scope.Validate();
        return (scope, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public IEnumerable<EurLexDomain> ActiveDomains(int? wave = null) =>
        Domains.Where(d => d.Wave <= (wave ?? ApprovedWave));

    private void Validate()
    {
        if (Schema != "lex-eu-scope/1") throw new InvalidDataException($"Unsupported EU scope schema '{Schema}'.");
        if (ApprovedWave is < 1 or > 4) throw new InvalidDataException("approved_wave must be between 1 and 4.");
        if (Languages.Count == 0 || Languages.Any(l => l is not ("en" or "fr")))
            throw new InvalidDataException("EU scope languages must be a non-empty subset of en and fr.");
        if (Domains.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != Domains.Count)
            throw new InvalidDataException("EU domain ids must be unique.");
        if (Domains.Any(d => d.Wave is < 1 or > 4)) throw new InvalidDataException("EU domain waves must be between 1 and 4.");
        if (Domains.SelectMany(d => d.SeedCelex).Any(v => v.Length == 0 ||
                v.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '(' or ')' or '-'))))
            throw new InvalidDataException("Seed CELEX identifiers contain unsafe characters.");
        if (Domains.SelectMany(d => d.DirectoryPrefixes.Concat(d.Eurovoc))
            .Any(v => v.Length == 0 || v.Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new InvalidDataException("Directory and EuroVoc selectors must be alphanumeric.");
        if (RelationshipClosure.MaxDepth is < 0 or > 2) throw new InvalidDataException("Relationship closure depth must be 0, 1 or 2.");
        if (RelationshipClosure.MaxRelatedPerSeed is < 1 or > 5000 || RelationshipClosure.MaxTotalWorks is < 1)
            throw new InvalidDataException("Relationship closure safety limits are invalid.");
        if (RelationshipClosure.Predicates.Concat(RelationshipClosure.CaseLawPredicates)
            .Any(p => p.Length == 0 || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))))
            throw new InvalidDataException("Relationship predicates must be safe CDM local names.");
        if (History.ManufactureConsolidations)
            throw new InvalidDataException("A Lex scope may never enable synthetic consolidation.");
        if (History.MaxVerifiedPortalFallbacks is < 1 or > 512)
            throw new InvalidDataException(
                "max_verified_portal_fallbacks must be between 1 and 512.");
    }
}

public sealed record EurLexHistoryRules(
    bool IncludeOriginal,
    bool IncludeAllOfficialConsolidations,
    bool IncludeUnamended,
    bool ManufactureConsolidations,
    int MaxVerifiedPortalFallbacks = 64);

public sealed record EurLexClosureRules(
    int MaxDepth,
    int MaxRelatedPerSeed,
    int MaxTotalWorks,
    int IncludeCaseLawFromWave,
    IReadOnlyList<string> Predicates,
    IReadOnlyList<string> CaseLawPredicates);

public sealed record EurLexDomain(
    string Id,
    string Label,
    int Wave,
    IReadOnlyList<string> DirectoryPrefixes,
    IReadOnlyList<string> Eurovoc,
    IReadOnlyList<string> SeedCelex);

public sealed record EurLexExclusion(string Kind, string Reason);

public sealed record EurLexScopePreview(
    string Schema,
    string ScopeId,
    int Wave,
    string ReviewStatus,
    string ObservedAt,
    IReadOnlyList<string> AddedWorks,
    IReadOnlyList<string> RemovedWorks,
    int Works,
    int LoadableWorks,
    IReadOnlyList<string> MetadataGaps,
    int OriginalExpressions,
    int ConsolidatedExpressions,
    IReadOnlyDictionary<string, int> Languages,
    IReadOnlyDictionary<string, int> InclusionReasons,
    IReadOnlyList<string> MissingConsolidations,
    long EstimatedDownloadBytes,
    long EstimatedIndexBytes,
    string EstimateBasis);
