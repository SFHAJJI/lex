using System.Security.Cryptography;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>
/// A renderer source artifact held as a reference and the exact bytes that reference names,
/// which cannot be constructed apart.
/// </summary>
/// <remarks>
/// <para>
/// Decision 75: a run holds what it depends on. Before this type the binder took a bare
/// <see cref="SourceArtifactRef"/> for the renderer source, so a send could name an artifact the
/// running process had never held. Against a test double that pre-seeded the store the route was
/// green; against a real store it failed at the first send, because nothing in production had put
/// those bytes there. The reference alone is a promise about somebody else's custody.
/// </para>
/// <para>
/// The pair is the fix, and it is a construction-surface fix rather than a check: there is one way
/// in, it verifies the bytes against the digest, and a caller that holds only a reference cannot
/// reach the binder at all. That is what makes it different from asking every caller to remember to
/// retain something first.
/// </para>
/// </remarks>
public sealed class MachineQueryRendererSource
{
    private readonly byte[] _bytes;

    private MachineQueryRendererSource(SourceArtifactRef reference, byte[] bytes)
    {
        Reference = reference;
        _bytes = bytes;
    }

    /// <summary>The frozen reference. Still the authority: the bytes are its witness.</summary>
    public SourceArtifactRef Reference { get; }

    /// <summary>A copy of the exact bytes, so the held array cannot be mutated by a caller.</summary>
    public ReadOnlyMemory<byte> CopyBytes() => _bytes.ToArray();

    /// <summary>
    /// The only path that opens a renderer source. The bytes must hash to the reference's
    /// digest, which is the whole of the reference's content claim: a SourceArtifactRef carries a
    /// resource id and a SHA-256 and nothing else, so there is no length to cross-check.
    /// </summary>
    public static MachineQueryRendererSource Open(
        SourceArtifactRef reference,
        ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(digest, reference.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The renderer source bytes do not carry the digest their reference names.",
                nameof(bytes));
        }

        return new MachineQueryRendererSource(reference, bytes.ToArray());
    }
}
