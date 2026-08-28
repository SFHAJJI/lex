using System.Security.Cryptography;
using System.Text.Json;
using Lex.Derive;
using Lex.Index;
using Lex.Temporal;

namespace Lex.Ingest;

public sealed record IndexStampVerification(
    string Collection,
    string? CorpusCommit,
    string? CodeCommit,
    string? ArticlesCommit,
    bool SignatureValid,
    bool ContentDigestPresent,
    bool ContentDigestMatches,
    bool CollectionMatches,
    bool CorpusCommitMatches,
    bool CodeCommitMatches,
    bool ArticlesCommitMatches,
    IReadOnlyList<string> ProvenanceErrors,
    bool Strict)
{
    public bool CapabilityPolicyMatches { get; init; } = true;
    public string? CapabilityManifestSha256 { get; init; }
    public string? CapabilityPolicySha256 { get; init; }

    public bool ProvenanceMatches => ProvenanceErrors.Count == 0;

    public bool IsValid => SignatureValid
                           && (ContentDigestMatches || !Strict && !ContentDigestPresent)
                           && CollectionMatches
                           && CorpusCommitMatches
                           && CodeCommitMatches
                           && ArticlesCommitMatches
                           && CapabilityPolicyMatches
                           && ProvenanceMatches;

    public int ExitCode => !SignatureValid ? 3
        : !ContentDigestMatches && (Strict || ContentDigestPresent) ? 4
        : CollectionMatches && CorpusCommitMatches && CodeCommitMatches && ArticlesCommitMatches
          && CapabilityPolicyMatches && ProvenanceMatches ? 0 : 5;
}

/// <summary>
/// External release evidence for a caller-built index. Exact files are preferred because they
/// let the verifier validate the documents as well as their digests. Air-gapped promotion may
/// instead supply every expected identity explicitly.
/// </summary>
public sealed record IndexStampVerificationInputs(
    string? ExpectedCollection = null,
    string? ExpectedCorpusCommit = null,
    string? ExpectedCodeCommit = null,
    string? ExpectedArticlesCommit = null,
    string? CorpusManifestPath = null,
    string? ArticlesGenerationPath = null,
    string? ExpectedCorpusManifestSha256 = null,
    string? ExpectedIngesterCodeCommit = null,
    string? ExpectedDeriverCodeCommit = null,
    string? ExpectedDeriverTreeId = null,
    string? ExpectedGenerationSha256 = null,
    string? ExpectedProfilesSha256 = null,
    string? CapabilityPolicyPath = null,
    bool RequireDerivedProvenance = false);

public static class IndexStampVerifier
{
    public static IndexStampVerification Verify(
        string dbPath,
        string? expectedCollection = null,
        string? expectedCorpusCommit = null,
        string? expectedCodeCommit = null,
        string? expectedArticlesCommit = null) => Verify(dbPath,
            new IndexStampVerificationInputs(
                expectedCollection, expectedCorpusCommit,
                expectedCodeCommit, expectedArticlesCommit));

    public static IndexStampVerification Verify(
        string dbPath, IndexStampVerificationInputs inputs) =>
        VerifyCore(dbPath, inputs, requireCapabilityPolicy: false);

    /// <summary>
    /// The promotion-capable verifier. It always recomputes the capability manifest from indexed
    /// documents and cannot succeed without the checked-in external production policy.
    /// </summary>
    public static IndexStampVerification VerifyPromotion(
        string dbPath, IndexStampVerificationInputs inputs) =>
        VerifyCore(dbPath, inputs, requireCapabilityPolicy: true);

    private static IndexStampVerification VerifyCore(
        string dbPath,
        IndexStampVerificationInputs inputs,
        bool requireCapabilityPolicy)
    {
        using var reader = LexIndexReader.Open(dbPath);
        var claimedContent = reader.Stamp.GetValueOrDefault("content_digest") ?? "";
        var actualContent = reader.ComputeContentDigest();
        var manifest = ReadManifest(inputs.CorpusManifestPath);
        var expectedCollection = Consistent("collection",
            inputs.ExpectedCollection,
            manifest?.Publisher.GetValueOrDefault("id"));
        var capabilityExpectation = inputs.CapabilityPolicyPath is null
            ? null
            : CapabilityPolicy.Load(inputs.CapabilityPolicyPath,
                expectedCollection ?? reader.Collection);
        DerivationGeneration.Entry? generation = null;
        string? generationFileDigest = null;
        if (inputs.ArticlesGenerationPath is not null)
        {
            if (expectedCollection is null)
                throw new InvalidDataException(
                    "An expected collection or corpus manifest is required with articles generation evidence.");
            var fullGenerationPath = RequireFile(
                inputs.ArticlesGenerationPath, "articles generation");
            if (!string.Equals(Path.GetFileName(fullGenerationPath),
                    DerivationGeneration.FileName, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Articles generation evidence must name '{DerivationGeneration.FileName}'.");
            generation = DerivationGeneration.ReadPublisher(
                Path.GetDirectoryName(fullGenerationPath)!, expectedCollection);
            generationFileDigest = Sha256File(fullGenerationPath);
        }

        var expectedCorpusCommit = Consistent("corpus commit",
            inputs.ExpectedCorpusCommit, generation?.CorpusCommit);
        var externalCorpusManifest = Consistent("corpus manifest digest",
            inputs.ExpectedCorpusManifestSha256,
            inputs.CorpusManifestPath is null ? null
                : Sha256File(RequireFile(inputs.CorpusManifestPath, "corpus manifest")));
        var expectedCorpusManifest = Consistent("corpus manifest digest",
            externalCorpusManifest,
            generation?.CorpusManifestSha256);
        var externalIngester = Consistent("ingester code commit",
            inputs.ExpectedIngesterCodeCommit, manifest?.IngesterCodeCommit);
        var expectedIngester = Consistent("ingester code commit", externalIngester,
            generation?.IngesterCodeCommit);
        var expectedDeriver = Consistent("deriver code commit",
            inputs.ExpectedDeriverCodeCommit, generation?.DeriverCodeCommit);
        var expectedDeriverTree = Consistent("deriver tree id",
            inputs.ExpectedDeriverTreeId, generation?.DeriverTreeId);
        var externalGeneration = Consistent("generation digest",
            inputs.ExpectedGenerationSha256, generationFileDigest);
        var expectedProfiles = Consistent("profiles digest",
            inputs.ExpectedProfilesSha256, generation?.ProfilesSha256);

        var corpusCommit = reader.Stamp.GetValueOrDefault("corpus_commit");
        var codeCommit = reader.Stamp.GetValueOrDefault("code_commit");
        var articlesCommit = reader.Stamp.GetValueOrDefault("articles_commit");
        var strict = expectedCollection is not null || expectedCorpusCommit is not null
                     || inputs.ExpectedCodeCommit is not null
                     || inputs.ExpectedArticlesCommit is not null
                     || inputs.CapabilityPolicyPath is not null
                     || requireCapabilityPolicy
                     || inputs.RequireDerivedProvenance;
        var provenanceErrors = VerifyProvenance(reader.Stamp, inputs.RequireDerivedProvenance,
            inputs.ExpectedCodeCommit, inputs.ExpectedCorpusCommit,
            inputs.ExpectedArticlesCommit, expectedCorpusManifest, expectedIngester,
            expectedDeriver, expectedDeriverTree, externalGeneration,
            expectedProfiles);
        var capabilityPolicyMatches = VerifyCapabilityPolicy(
            reader, capabilityExpectation, provenanceErrors);
        if (requireCapabilityPolicy && inputs.CapabilityPolicyPath is null)
        {
            provenanceErrors.Add("no external capability policy was supplied");
            capabilityPolicyMatches = false;
        }
        if (inputs.RequireDerivedProvenance)
        {
            RequireExternal(inputs.ExpectedCollection ?? manifest?.Publisher.GetValueOrDefault("id"),
                "collection", provenanceErrors);
            RequireExternal(inputs.ExpectedCorpusCommit, "corpus commit", provenanceErrors);
            RequireExternal(inputs.ExpectedArticlesCommit, "articles commit", provenanceErrors);
            RequireExternal(externalCorpusManifest, "corpus manifest digest", provenanceErrors);
            RequireExternal(externalIngester, "ingester code commit", provenanceErrors);
            RequireExternal(externalGeneration, "generation digest", provenanceErrors);
        }
        return new IndexStampVerification(
            reader.Collection,
            corpusCommit,
            codeCommit,
            articlesCommit,
            reader.SignatureValid,
            claimedContent.Length > 0,
            claimedContent.Length > 0
            && string.Equals(claimedContent, actualContent, StringComparison.Ordinal),
            expectedCollection is null
            || string.Equals(expectedCollection, reader.Collection, StringComparison.Ordinal),
            expectedCorpusCommit is null
            || string.Equals(expectedCorpusCommit, corpusCommit, StringComparison.Ordinal),
            inputs.ExpectedCodeCommit is null
            || string.Equals(inputs.ExpectedCodeCommit, codeCommit, StringComparison.Ordinal),
            inputs.ExpectedArticlesCommit is null
            || string.Equals(inputs.ExpectedArticlesCommit, articlesCommit, StringComparison.Ordinal),
            provenanceErrors,
            strict)
        {
            CapabilityPolicyMatches = capabilityPolicyMatches,
            CapabilityManifestSha256 = reader.Stamp.GetValueOrDefault(
                "capability_manifest_sha256"),
            CapabilityPolicySha256 = reader.Stamp.GetValueOrDefault(
                "capability_policy_sha256"),
        };
    }

    private static bool VerifyCapabilityPolicy(
        LexIndexReader reader,
        CapabilityBuildExpectation? expectation,
        ICollection<string> errors)
    {
        var before = errors.Count;
        try
        {
            reader.VerifyCapabilityManifestMatchesDocuments();
        }
        catch (InvalidDataException error)
        {
            errors.Add(error.Message);
        }
        if (expectation is null) return errors.Count == before;
        if (!string.Equals(reader.Stamp.GetValueOrDefault("capability_policy_tier"),
                "production", StringComparison.Ordinal))
            errors.Add("stamp capability_policy_tier is not production");
        if (!string.Equals(reader.Stamp.GetValueOrDefault("capability_policy_sha256"),
                expectation.PolicySha256, StringComparison.Ordinal))
            errors.Add("stamp capability_policy_sha256 does not match external evidence");
        try
        {
            CapabilityManifest.ValidateExpectation(
                reader.Collection, reader.CapabilityManifest, expectation);
        }
        catch (InvalidDataException error)
        {
            errors.Add(error.Message);
        }
        return errors.Count == before;
    }

    private static ManifestDoc? ReadManifest(string? path)
    {
        if (path is null) return null;
        var fullPath = RequireFile(path, "corpus manifest");
        ManifestDoc manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ManifestDoc>(
                           File.ReadAllText(fullPath), CorpusJson.Options)
                       ?? throw new InvalidDataException("Corpus manifest is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Corpus manifest evidence cannot be parsed: {fullPath}", error);
        }
        if (manifest.Schema != ManifestDoc.CurrentSchema)
            throw new InvalidDataException(
                $"Corpus manifest evidence must use '{ManifestDoc.CurrentSchema}'.");
        CodeIdentity.RequireFullCommit(
            manifest.IngesterCodeCommit, "manifest ingester_code_commit");
        CorpusWriter.ValidateSourceConfiguration(
            manifest.SourceConfigurationKind, manifest.SourceConfigurationSha256);
        if (manifest.Publisher is null
            || !manifest.Publisher.TryGetValue("id", out var publisher)
            || string.IsNullOrWhiteSpace(publisher))
            throw new InvalidDataException(
                "Corpus manifest evidence has no publisher identity.");
        return manifest;
    }

    private static List<string> VerifyProvenance(
        IReadOnlyDictionary<string, string> stamp,
        bool required,
        string? expectedBuilder,
        string? expectedCorpusCommit,
        string? expectedArticlesCommit,
        string? expectedCorpusManifest,
        string? expectedIngester,
        string? expectedDeriver,
        string? expectedDeriverTree,
        string? expectedGeneration,
        string? expectedProfiles)
    {
        var errors = new List<string>();
        void Match(string field, string? expected, Func<string, string, string>? validate = null)
        {
            if (expected is null)
            {
                if (required) errors.Add($"no external expectation supplied for {field}");
                return;
            }
            try
            {
                expected = validate?.Invoke(expected, $"expected {field}") ?? expected;
            }
            catch (InvalidDataException error)
            {
                errors.Add(error.Message);
                return;
            }
            if (!stamp.TryGetValue(field, out var actual))
            {
                errors.Add($"stamp {field} is absent");
                return;
            }
            try
            {
                _ = validate?.Invoke(actual, $"stamp {field}");
            }
            catch (InvalidDataException error)
            {
                errors.Add(error.Message);
                return;
            }
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                errors.Add($"stamp {field} does not match external evidence");
        }

        Match("builder_code_commit", required ? expectedBuilder : null,
            CodeIdentity.RequireFullCommit);
        Match("corpus_commit", required ? expectedCorpusCommit : null,
            CodeIdentity.RequireFullCommit);
        Match("articles_commit", required ? expectedArticlesCommit : null,
            CodeIdentity.RequireFullCommit);
        Match("corpus_manifest_sha256", expectedCorpusManifest, CodeIdentity.RequireSha256);
        Match("ingester_code_commit", expectedIngester, CodeIdentity.RequireFullCommit);
        Match("deriver_code_commit", expectedDeriver, CodeIdentity.RequireFullCommit);
        Match("deriver_tree_id", expectedDeriverTree, CodeIdentity.RequireFullGitObjectId);
        Match("generation_sha256", expectedGeneration, CodeIdentity.RequireSha256);
        Match("profiles_sha256", expectedProfiles, CodeIdentity.RequireSha256);
        return errors;
    }

    private static string? Consistent(string label, params string?[] values)
    {
        var present = values.Where(value => value is not null).Cast<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        return present.Length switch
        {
            0 => null,
            1 => present[0],
            _ => throw new InvalidDataException(
                $"Conflicting external {label} evidence was supplied."),
        };
    }

    private static void RequireExternal(
        string? value, string label, ICollection<string> errors)
    {
        if (value is null)
            errors.Add($"no external {label} evidence was supplied");
    }

    private static string RequireFile(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"{label} evidence is missing.", fullPath);
        return fullPath;
    }

    private static string Sha256File(string path) => Convert.ToHexStringLower(
        SHA256.HashData(File.ReadAllBytes(path)));
}
