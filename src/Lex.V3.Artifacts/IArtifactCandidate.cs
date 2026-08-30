namespace Lex.V3.Artifacts;

public interface IArtifactCandidate
{
    ValueTask<Stream> OpenAdmissionManifestAsync(CancellationToken cancellationToken);

    ValueTask<Stream> OpenPayloadAsync(CancellationToken cancellationToken);
}
