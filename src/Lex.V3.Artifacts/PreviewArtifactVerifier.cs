using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

public sealed class PreviewArtifactVerifier
{
    private readonly string expectedEnvironmentBinding;
    private readonly string expectedArtifactSchemaSha256;
    private readonly PreviewContractSet expectedContractSet;
    private readonly string expectedPayloadSchemaSha256;
    private readonly IPreviewTrustStore trustStore;

    private PreviewArtifactVerifier(
        string expectedEnvironmentBinding,
        IPreviewTrustStore trustStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEnvironmentBinding);
        this.expectedEnvironmentBinding = expectedEnvironmentBinding;
        expectedArtifactSchemaSha256 = PreviewSchemaExporter.ComputeSha256(
            PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewArtifact));
        expectedContractSet = PreviewSchemaExporter.ExportContractSet();
        expectedPayloadSchemaSha256 = PreviewSchemaExporter.ComputeSha256(
            PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewPayload));
        this.trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
    }

    public static PreviewArtifactVerifier CreateStageZero(
        string expectedEnvironmentBinding,
        IPreviewTrustStore trustStore) =>
        new(expectedEnvironmentBinding, trustStore);

    public async ValueTask<PreviewArtifactVerification> VerifyAsync(
        IArtifactCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        await using var manifestStream = await candidate
            .OpenAdmissionManifestAsync(cancellationToken)
            .ConfigureAwait(false);
        var boundedHeader = await BoundedStreamReader
            .ReadAsync(manifestStream, PreviewContractLimits.MaximumManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        if (boundedHeader.ExceededLimit)
        {
            return Rejected(ArtifactAdmissionFailureCode.HeaderTooLarge);
        }

        var parsedHeader = AdmissionHeaderReader.Read(boundedHeader.Bytes);
        if (parsedHeader.Failure is not null)
        {
            return PreviewArtifactVerification.Rejected(parsedHeader.Failure);
        }

        var header = parsedHeader.Header!;
        var markerFailure = ValidateMarkers(header);
        if (markerFailure is not null)
        {
            return Rejected(markerFailure.Value);
        }

        PreviewArtifactManifest manifest;
        try
        {
            manifest = ContractJson.Deserialize<PreviewArtifactManifest>(
                Encoding.UTF8.GetString(boundedHeader.Bytes));
        }
        catch (JsonException)
        {
            return Rejected(ArtifactAdmissionFailureCode.MalformedHeader);
        }

        if (!Equals(manifest.ContractSet, expectedContractSet))
        {
            return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete);
        }

        if (!trustStore.ContainsIssuer(header.IssuerId))
        {
            return Rejected(ArtifactAdmissionFailureCode.IssuerUntrusted);
        }

        if (!trustStore.TryGetSubjectPublicKeyInfo(
                header.IssuerId,
                header.KeyId,
                out var subjectPublicKeyInfo))
        {
            return Rejected(ArtifactAdmissionFailureCode.KeyUntrusted);
        }

        if (!VerifySignature(manifest, subjectPublicKeyInfo.Span))
        {
            return Rejected(ArtifactAdmissionFailureCode.SignatureInvalid);
        }

        await using var payloadStream = await candidate
            .OpenPayloadAsync(cancellationToken)
            .ConfigureAwait(false);
        var boundedPayload = await BoundedStreamReader
            .ReadAsync(payloadStream, PreviewContractLimits.MaximumPayloadBytes, cancellationToken)
            .ConfigureAwait(false);
        if (boundedPayload.ExceededLimit || boundedPayload.Bytes.LongLength != manifest.Payload.Bytes)
        {
            return Rejected(ArtifactAdmissionFailureCode.PayloadSizeMismatch);
        }

        var digest = Convert.ToHexString(SHA256.HashData(boundedPayload.Bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest),
                Encoding.ASCII.GetBytes(manifest.Payload.Sha256)))
        {
            return Rejected(ArtifactAdmissionFailureCode.PayloadDigestMismatch);
        }

        if (!StrictPayloadReader.IsStructurallyValid(boundedPayload.Bytes))
        {
            return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete);
        }

        try
        {
            var payload = ContractJson.Deserialize<PreviewPayload>(Encoding.UTF8.GetString(boundedPayload.Bytes));
            if (!string.Equals(
                    ContractJson.Serialize(payload),
                    ContractJson.Serialize(PreviewPayload.CreateStageZero()),
                    StringComparison.Ordinal))
            {
                return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete);
            }

            return PreviewArtifactVerification.Accepted(payload);
        }
        catch (JsonException)
        {
            return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete);
        }
    }

    private ArtifactAdmissionFailureCode? ValidateMarkers(AdmissionHeader header)
    {
        if (!string.Equals(header.Schema, V3SchemaIds.PreviewArtifact, StringComparison.Ordinal) ||
            !string.Equals(
                header.SchemaResource,
                V3SchemaResourceIds.PreviewArtifact,
                StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(header.SchemaSha256),
                Encoding.ASCII.GetBytes(expectedArtifactSchemaSha256)) ||
            !string.Equals(header.PayloadSchema, V3SchemaIds.PreviewPayload, StringComparison.Ordinal) ||
            !string.Equals(
                header.PayloadSchemaResource,
                V3SchemaResourceIds.PreviewPayload,
                StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(header.PayloadSchemaSha256),
                Encoding.ASCII.GetBytes(expectedPayloadSchemaSha256)))
        {
            return ArtifactAdmissionFailureCode.GraphSchemaUnsupported;
        }

        if (!header.Synthetic)
        {
            return ArtifactAdmissionFailureCode.SyntheticFlagForbidden;
        }

        if (!string.Equals(header.EvidenceClass, "synthetic_preview", StringComparison.Ordinal))
        {
            return ArtifactAdmissionFailureCode.SyntheticEvidenceForbidden;
        }

        if (!string.Equals(header.SourceKind, "synthetic_test", StringComparison.Ordinal))
        {
            return ArtifactAdmissionFailureCode.SyntheticSourceForbidden;
        }

        if (!string.Equals(header.EnvironmentClass, "preview", StringComparison.Ordinal) ||
            !string.Equals(header.EnvironmentBinding, expectedEnvironmentBinding, StringComparison.Ordinal))
        {
            return ArtifactAdmissionFailureCode.EnvironmentForbidden;
        }

        if (!string.Equals(header.IssuerRole, "preview_attestor", StringComparison.Ordinal))
        {
            return ArtifactAdmissionFailureCode.IssuerRoleForbidden;
        }

        if (!string.Equals(header.AttestationPurpose, "preview_mechanics_only", StringComparison.Ordinal) ||
            !string.Equals(header.Algorithm, "ECDSA-P256-SHA256", StringComparison.Ordinal) ||
            !string.Equals(header.SignatureFormat, "ieee-p1363", StringComparison.Ordinal))
        {
            return ArtifactAdmissionFailureCode.AlgorithmUnsupported;
        }

        if (!string.Equals(header.MediaType, "application/json", StringComparison.Ordinal))
        {
            return ArtifactAdmissionFailureCode.GraphSchemaUnsupported;
        }

        return null;
    }

    private static bool VerifySignature(
        PreviewArtifactManifest manifest,
        ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        try
        {
            var signature = Base64Url.Decode(manifest.Attestation.Signature);
            if (signature.Length != 64)
            {
                return false;
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            var curveOid = verifier.ExportParameters(includePrivateParameters: false).Curve.Oid.Value;
            if (bytesRead != subjectPublicKeyInfo.Length ||
                verifier.KeySize != 256 ||
                !string.Equals(
                    curveOid,
                    ECCurve.NamedCurves.nistP256.Oid.Value,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return verifier.VerifyData(
                PreviewArtifactCanonicalizer.GetSigningBytes(manifest),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static PreviewArtifactVerification Rejected(ArtifactAdmissionFailureCode code) =>
        PreviewArtifactVerification.Rejected(
            new ArtifactAdmissionFailure(code, "preview_verification"));
}

public sealed class PreviewArtifactVerification
{
    private PreviewArtifactVerification(
        bool verified,
        PreviewPayload? payload,
        ArtifactAdmissionFailure? failure)
    {
        Verified = verified;
        Payload = payload;
        Failure = failure;
    }

    public bool Verified { get; }

    public PreviewPayload? Payload { get; }

    public ArtifactAdmissionFailure? Failure { get; }

    internal static PreviewArtifactVerification Accepted(PreviewPayload payload) =>
        new(true, payload ?? throw new ArgumentNullException(nameof(payload)), null);

    internal static PreviewArtifactVerification Rejected(ArtifactAdmissionFailure failure) =>
        new(false, null, failure ?? throw new ArgumentNullException(nameof(failure)));
}
