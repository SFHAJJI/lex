using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest;

/// <summary>Why the unsigned terminal corpus manifest set could not be built.</summary>
public enum LexCorpus6BuildRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("evidence_incomplete")]
    EvidenceIncomplete = 1,
}

/// <summary>
/// Builds the deterministic unsigned terminal Stage 3 corpus artifact from proof-complete inputs.
/// </summary>
public static class LexCorpus6Builder
{
    public const string Schema = "lex-corpus/6";

    /// <summary>
    /// The terminal build door. The first increment establishes the door and strict reader beside
    /// it; subsequent RED cases fill the complete population and lineage checks before this method
    /// can return bytes.
    /// </summary>
    public static LexCorpus6BuildResult? TryBuild(
        Stage3DerivationProfileEnvelope profileEnvelope,
        EuRightsMatrix euRightsMatrix,
        out LexCorpus6BuildRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(profileEnvelope);
        ArgumentNullException.ThrowIfNull(euRightsMatrix);
        refusal = LexCorpus6BuildRefusal.EvidenceIncomplete;
        detail = "The complete corpus population projection is not yet implemented.";
        return null;
    }
}

/// <summary>A successful in-memory unsigned build and the exact canonical bytes it produced.</summary>
public sealed record LexCorpus6BuildResult(
    SourceArtifactRef ArtifactRef,
    ReadOnlyMemory<byte> CanonicalBytes,
    VerifiedLexCorpus6ManifestSet VerifiedSet);

/// <summary>The strict checked door for retained <c>lex-corpus/6</c> bytes.</summary>
public sealed class VerifiedLexCorpus6ManifestSet
{
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(LexCorpus6Builder.Schema + "\n");

    private VerifiedLexCorpus6ManifestSet()
    {
    }

    public static VerifiedLexCorpus6ManifestSet ParseAndVerify(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        hash.AppendData(canonicalBytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        if (!string.Equals(Convert.ToHexStringLower(digest), artifactRef.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The corpus manifest-set bytes do not match their artifact reference.",
                nameof(canonicalBytes));
        }

        throw new ArgumentException(
            "No incomplete corpus manifest set is readable.",
            nameof(canonicalBytes));
    }
}
