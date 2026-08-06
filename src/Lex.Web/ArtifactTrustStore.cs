using Lex.Index;

namespace Lex.Web;

/// <summary>
/// Trust roots are embedded in the application release, never loaded from the directory whose
/// contents they authenticate. Rotation ships old and new roots together before a signer moves.
/// </summary>
public static class ArtifactTrustStore
{
    public static IReadOnlyList<ArtifactTrustRoot> Roots { get; } = Load();

    private static IReadOnlyList<ArtifactTrustRoot> Load()
    {
        using var stream = typeof(ArtifactTrustStore).Assembly
            .GetManifestResourceStream("Lex.Web.trusted-artifact-roots.json")
            ?? throw new InvalidOperationException("Embedded artifact trust roots are missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return ArtifactManifests.ParseTrustRoots(buffer.ToArray());
    }
}
