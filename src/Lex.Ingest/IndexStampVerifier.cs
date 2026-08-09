using System.Security.Cryptography;
using Lex.Index;

namespace Lex.Ingest;

public sealed record IndexStampVerification(
    string Collection,
    string? CorpusCommit,
    bool SignatureValid,
    bool ContentDigestPresent,
    bool ContentDigestMatches,
    bool CollectionMatches,
    bool CorpusCommitMatches,
    bool EnrichmentDigestMatches,
    bool Strict)
{
    public bool IsValid => SignatureValid
                           && (ContentDigestMatches || !Strict && !ContentDigestPresent)
                           && CollectionMatches
                           && CorpusCommitMatches
                           && EnrichmentDigestMatches;

    public int ExitCode => !SignatureValid ? 3
        : !ContentDigestMatches && (Strict || ContentDigestPresent) ? 4
        : CollectionMatches && CorpusCommitMatches && EnrichmentDigestMatches ? 0 : 5;
}

public static class IndexStampVerifier
{
    public static IndexStampVerification Verify(
        string dbPath,
        string? expectedCollection = null,
        string? expectedCorpusCommit = null,
        string? workEnrichmentPath = null)
    {
        using var reader = LexIndexReader.Open(dbPath);
        var claimedContent = reader.Stamp.GetValueOrDefault("content_digest") ?? "";
        var actualContent = reader.ComputeContentDigest();
        var expectedEnrichment = workEnrichmentPath is null ? null : Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(workEnrichmentPath)));
        var actualEnrichment = reader.Stamp.GetValueOrDefault("enrichment_digest");
        var corpusCommit = reader.Stamp.GetValueOrDefault("corpus_commit");
        var strict = expectedCollection is not null || expectedCorpusCommit is not null
                     || expectedEnrichment is not null;
        return new IndexStampVerification(
            reader.Collection,
            corpusCommit,
            reader.SignatureValid,
            claimedContent.Length > 0,
            claimedContent.Length > 0
            && string.Equals(claimedContent, actualContent, StringComparison.Ordinal),
            expectedCollection is null
            || string.Equals(expectedCollection, reader.Collection, StringComparison.Ordinal),
            expectedCorpusCommit is null
            || string.Equals(expectedCorpusCommit, corpusCommit, StringComparison.Ordinal),
            expectedEnrichment is null
            || string.Equals(expectedEnrichment, actualEnrichment,
                StringComparison.OrdinalIgnoreCase),
            strict);
    }
}
