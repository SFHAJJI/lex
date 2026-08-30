using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

public interface ISyntheticSliceCandidate
{
    ValueTask<Stream> OpenAdmissionManifestAsync(CancellationToken cancellationToken);

    ValueTask<Stream> OpenControlAsync(string sha256, CancellationToken cancellationToken);

    ValueTask<Stream> OpenBlobAsync(
        SyntheticSliceBlobKind kind,
        string sha256,
        CancellationToken cancellationToken);
}
