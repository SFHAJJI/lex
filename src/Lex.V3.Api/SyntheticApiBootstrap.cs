using System.Security.Cryptography;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Api;

internal static class SyntheticApiBootstrap
{
    private const int MaximumPublicKeyBytes = 512;

    public static async ValueTask<SyntheticApiState> OpenAsync(
        string graphRoot,
        string publicKeyPath,
        string environmentBinding,
        string issuerId,
        string keyId,
        string expectedPublicKeySha256,
        string runtimeSourceSha256,
        bool immutableCustody,
        IRequestEntropySource entropy,
        CancellationToken cancellationToken)
    {
        var publicKey = ReadPublicKey(publicKeyPath);
        var trustStore = new FixedPreviewTrustStore(issuerId, keyId, publicKey);
        var verifier = new SyntheticSliceArtifactVerifier(
            environmentBinding,
            issuerId,
            keyId,
            expectedPublicKeySha256,
            SyntheticSliceSchemaExporter.ExportSchemaTable(),
            trustStore);
        var candidate = new ContentAddressedSyntheticCandidate(graphRoot);
        var verification = await verifier.VerifyAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (!verification.Verified || verification.Control is null)
        {
            throw new InvalidDataException("The bundled synthetic graph was not admitted.");
        }

        var indexDescriptor = verification.Control.Blobs.Single(static blob =>
            blob.Kind == SyntheticSliceBlobKind.SqliteIndex);
        var sqlitePath = candidate.PathForSqlite(indexDescriptor.Sha256);
        if (immutableCustody)
        {
            SyntheticImmutableCustody.AssertReadOnly(graphRoot, sqlitePath);
        }

        var resolver = SyntheticIndexResolver.Open(
            sqlitePath,
            verification.Control,
            immutableCustody);
        try
        {
            var runtime = new ComponentIdentity("s0-05-runtime", runtimeSourceSha256);
            await PreflightAsync(
                verification,
                resolver,
                runtime,
                cancellationToken).ConfigureAwait(false);
            return SyntheticApiState.Available(
                verification,
                resolver,
                runtime,
                entropy);
        }
        catch
        {
            resolver.Dispose();
            throw;
        }
    }

    internal static async ValueTask PreflightAsync(
        SyntheticSliceVerification verification,
        SyntheticIndexResolver resolver,
        ComponentIdentity runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(runtime);
        var held = await resolver.ResolveAsync(
            "eli",
            "eli/synthetic-preview",
            cancellationToken).ConfigureAwait(false);
        if (SyntheticResponseMapper.Map(
                verification,
                held,
                "eli",
                "eli/synthetic-preview",
                "req_00000000000000000000000000000000",
                runtime) is not SyntheticResolveSuccessEnvelope)
        {
            throw new InvalidDataException("The held preflight did not produce the exact success branch.");
        }

        var historical = await resolver.ResolveAsync(
            "historical_legal_id",
            "historical_legal_id:synthetic-preview",
            cancellationToken).ConfigureAwait(false);
        if (SyntheticResponseMapper.Map(
                verification,
                historical,
                "historical_legal_id",
                "historical_legal_id:synthetic-preview",
                "req_11111111111111111111111111111111",
                runtime) is not SyntheticResolveRefusalEnvelope)
        {
            throw new InvalidDataException("The historical preflight did not produce the exact refusal branch.");
        }
    }

    private static byte[] ReadPublicKey(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The preview public key cannot be a reparse point.");
        }

        var length = new FileInfo(fullPath).Length;
        if (length is <= 0 or > MaximumPublicKeyBytes)
        {
            throw new InvalidDataException("The preview public key has an invalid size.");
        }

        var bytes = File.ReadAllBytes(fullPath);
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var bytesRead);
            if (bytesRead != bytes.Length ||
                key.KeySize != 256 ||
                !string.Equals(
                    key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value,
                    ECCurve.NamedCurves.nistP256.Oid.Value,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The preview public key must be exactly ECDSA P-256 SPKI.");
            }
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("The preview public key is invalid.", exception);
        }

        return bytes;
    }

    private sealed class FixedPreviewTrustStore(
        string issuerId,
        string keyId,
        byte[] publicKey) : IPreviewTrustStore
    {
        public bool ContainsIssuer(string candidateIssuerId) =>
            string.Equals(candidateIssuerId, issuerId, StringComparison.Ordinal);

        public bool TryGetSubjectPublicKeyInfo(
            string candidateIssuerId,
            string candidateKeyId,
            out ReadOnlyMemory<byte> subjectPublicKeyInfo)
        {
            if (ContainsIssuer(candidateIssuerId) &&
                string.Equals(candidateKeyId, keyId, StringComparison.Ordinal))
            {
                subjectPublicKeyInfo = publicKey;
                return true;
            }

            subjectPublicKeyInfo = default;
            return false;
        }
    }
}
