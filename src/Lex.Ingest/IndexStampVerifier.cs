using System.Security.Cryptography;
using Lex.Index;

namespace Lex.Ingest;

public sealed record IndexStampVerification(
    string Collection,
    string? CorpusCommit,
    string? CodeCommit,
    bool SignatureValid,
    bool ContentDigestPresent,
    bool ContentDigestMatches,
    bool CollectionMatches,
    bool CorpusCommitMatches,
    bool CodeCommitMatches,
    bool EnrichmentDigestMatches,
    bool Strict)
{
    public bool IsValid => SignatureValid
                           && (ContentDigestMatches || !Strict && !ContentDigestPresent)
                           && CollectionMatches
                           && CorpusCommitMatches
                           && CodeCommitMatches
                           && EnrichmentDigestMatches;

    public int ExitCode => !SignatureValid ? 3
        : !ContentDigestMatches && (Strict || ContentDigestPresent) ? 4
        : CollectionMatches && CorpusCommitMatches && CodeCommitMatches
          && EnrichmentDigestMatches ? 0 : 5;
}

public static class IndexStampVerifier
{
    public static IndexStampVerification Verify(
        string dbPath,
        string? expectedCollection = null,
        string? expectedCorpusCommit = null,
        string? workEnrichmentPath = null,
        string? expectedCodeCommit = null)
    {
        using var reader = LexIndexReader.Open(dbPath);
        var claimedContent = reader.Stamp.GetValueOrDefault("content_digest") ?? "";
        var actualContent = reader.ComputeContentDigest();
        var expectedEnrichment = workEnrichmentPath is null ? null : Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(workEnrichmentPath)));
        var actualEnrichment = reader.Stamp.GetValueOrDefault("enrichment_digest");
        var corpusCommit = reader.Stamp.GetValueOrDefault("corpus_commit");
        var codeCommit = reader.Stamp.GetValueOrDefault("code_commit");
        var strict = expectedCollection is not null || expectedCorpusCommit is not null
                     || expectedEnrichment is not null || expectedCodeCommit is not null;
        return new IndexStampVerification(
            reader.Collection,
            corpusCommit,
            codeCommit,
            reader.SignatureValid,
            claimedContent.Length > 0,
            claimedContent.Length > 0
            && string.Equals(claimedContent, actualContent, StringComparison.Ordinal),
            expectedCollection is null
            || string.Equals(expectedCollection, reader.Collection, StringComparison.Ordinal),
            expectedCorpusCommit is null
            || string.Equals(expectedCorpusCommit, corpusCommit, StringComparison.Ordinal),
            expectedCodeCommit is null
            || string.Equals(expectedCodeCommit, codeCommit, StringComparison.Ordinal),
            expectedEnrichment is null
            || string.Equals(expectedEnrichment, actualEnrichment,
                StringComparison.OrdinalIgnoreCase),
            strict);
    }
}
