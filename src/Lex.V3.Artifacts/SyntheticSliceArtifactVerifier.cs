using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

public sealed class SyntheticSliceArtifactVerifier
{
    private readonly string expectedEnvironmentBinding;
    private readonly string expectedIssuerId;
    private readonly string expectedKeyId;
    private readonly byte[] expectedPublicKeySha256;
    private readonly SyntheticSliceSchemaTable expectedSchemaTable;
    private readonly IPreviewTrustStore trustStore;

    public SyntheticSliceArtifactVerifier(
        string expectedEnvironmentBinding,
        string expectedIssuerId,
        string expectedKeyId,
        string expectedPublicKeySha256,
        SyntheticSliceSchemaTable expectedSchemaTable,
        IPreviewTrustStore trustStore)
    {
        this.expectedEnvironmentBinding = RequireText(
            expectedEnvironmentBinding,
            nameof(expectedEnvironmentBinding));
        this.expectedIssuerId = RequireText(expectedIssuerId, nameof(expectedIssuerId));
        this.expectedKeyId = RequireText(expectedKeyId, nameof(expectedKeyId));
        this.expectedPublicKeySha256 = Convert.FromHexString(
            RequireSha256(expectedPublicKeySha256, nameof(expectedPublicKeySha256)));
        this.expectedSchemaTable = expectedSchemaTable ?? throw new ArgumentNullException(nameof(expectedSchemaTable));
        this.trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
    }

    public async ValueTask<SyntheticSliceVerification> VerifyAsync(
        ISyntheticSliceCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var candidateBytes = 0;

        await using var manifestStream = await candidate
            .OpenAdmissionManifestAsync(cancellationToken)
            .ConfigureAwait(false);
        var manifestRead = await BoundedStreamReader
            .ReadAsync(manifestStream, SyntheticSliceContractLimits.MaximumManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        if (manifestRead.ExceededLimit)
        {
            return Rejected(ArtifactAdmissionFailureCode.HeaderTooLarge, "synthetic_manifest");
        }

        candidateBytes += manifestRead.Bytes.Length;
        SyntheticSliceArtifactManifest manifest;
        try
        {
            if (!StrictPayloadReader.IsStructurallyValid(manifestRead.Bytes))
            {
                return Rejected(ArtifactAdmissionFailureCode.MalformedHeader, "synthetic_manifest");
            }

            manifest = ContractJson.Deserialize<SyntheticSliceArtifactManifest>(
                StrictUtf8(manifestRead.Bytes));
            if (!Encoding.UTF8.GetBytes(ContractJson.Serialize(manifest)).AsSpan()
                    .SequenceEqual(manifestRead.Bytes))
            {
                return Rejected(ArtifactAdmissionFailureCode.MalformedHeader, "synthetic_manifest");
            }
        }
        catch (JsonException)
        {
            return Rejected(ArtifactAdmissionFailureCode.MalformedHeader, "synthetic_manifest");
        }

        var markerFailure = ValidateManifest(manifest);
        if (markerFailure is not null)
        {
            return markerFailure;
        }

        if (!trustStore.ContainsIssuer(manifest.Issuer.IssuerId))
        {
            return Rejected(ArtifactAdmissionFailureCode.IssuerUntrusted, "synthetic_signature");
        }

        if (!trustStore.TryGetSubjectPublicKeyInfo(
                manifest.Issuer.IssuerId,
                manifest.Issuer.KeyId,
                out var publicKey) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(publicKey.Span),
                expectedPublicKeySha256))
        {
            return Rejected(ArtifactAdmissionFailureCode.KeyUntrusted, "synthetic_signature");
        }

        if (!VerifySignature(manifest, publicKey.Span))
        {
            return Rejected(ArtifactAdmissionFailureCode.SignatureInvalid, "synthetic_signature");
        }

        await using var controlStream = await candidate
            .OpenControlAsync(manifest.Control.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var controlRead = await BoundedStreamReader
            .ReadAsync(controlStream, SyntheticSliceContractLimits.MaximumControlBytes, cancellationToken)
            .ConfigureAwait(false);
        if (controlRead.ExceededLimit)
        {
            return Rejected(ArtifactAdmissionFailureCode.ControlTooLarge, "synthetic_control");
        }

        candidateBytes += controlRead.Bytes.Length;
        if (candidateBytes > SyntheticSliceContractLimits.MaximumCandidateBytes)
        {
            return Rejected(ArtifactAdmissionFailureCode.CandidateReadBudgetExceeded, "synthetic_control");
        }

        if (controlRead.Bytes.LongLength != manifest.Control.Bytes)
        {
            return Rejected(ArtifactAdmissionFailureCode.ControlSizeMismatch, "synthetic_control");
        }

        if (!DigestMatches(controlRead.Bytes, manifest.Control.Sha256))
        {
            return Rejected(ArtifactAdmissionFailureCode.ControlDigestMismatch, "synthetic_control");
        }

        SyntheticSliceControl control;
        try
        {
            if (!StrictPayloadReader.IsStructurallyValid(controlRead.Bytes))
            {
                return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete, "synthetic_control");
            }

            control = ContractJson.Deserialize<SyntheticSliceControl>(StrictUtf8(controlRead.Bytes));
            if (!Encoding.UTF8.GetBytes(ContractJson.Serialize(control)).AsSpan()
                    .SequenceEqual(controlRead.Bytes))
            {
                return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete, "synthetic_control");
            }
        }
        catch (JsonException)
        {
            return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete, "synthetic_control");
        }

        if (!string.Equals(
                control.OperationCatalog.Entries[0].Success.Sha256,
                expectedSchemaTable.Members[2].Sha256,
                StringComparison.Ordinal) ||
            !SameSchemaMember(control.ObjectSetSchema, expectedSchemaTable.Members[5]))
        {
            return Rejected(ArtifactAdmissionFailureCode.GraphIncomplete, "synthetic_control");
        }

        var source = await ReadBlobAsync(
            candidate,
            control.Blobs[0],
            SyntheticSliceContractLimits.MaximumSourceBytes,
            candidateBytes,
            cancellationToken).ConfigureAwait(false);
        if (source.Failure is not null)
        {
            return source.Failure;
        }

        candidateBytes += source.Bytes!.Length;
        var derived = await ReadBlobAsync(
            candidate,
            control.Blobs[1],
            SyntheticSliceContractLimits.MaximumDerivedBytes,
            candidateBytes,
            cancellationToken).ConfigureAwait(false);
        if (derived.Failure is not null)
        {
            return derived.Failure;
        }

        candidateBytes += derived.Bytes!.Length;
        try
        {
            if (!SyntheticTextNormalizer.Normalize(source.Bytes!).AsSpan().SequenceEqual(derived.Bytes))
            {
                return Rejected(ArtifactAdmissionFailureCode.DerivedContentMismatch, "synthetic_derive");
            }
        }
        catch (SyntheticDerivationException)
        {
            return Rejected(ArtifactAdmissionFailureCode.DerivedContentMismatch, "synthetic_derive");
        }

        var sqlite = await ReadBlobAsync(
            candidate,
            control.Blobs[2],
            SyntheticSliceContractLimits.MaximumSqliteBytes,
            candidateBytes,
            cancellationToken).ConfigureAwait(false);
        if (sqlite.Failure is not null)
        {
            return sqlite.Failure;
        }

        candidateBytes += sqlite.Bytes!.Length;
        if (candidateBytes > SyntheticSliceContractLimits.MaximumCandidateBytes ||
            sqlite.Bytes.Length < 16 ||
            !sqlite.Bytes.AsSpan(0, 16).SequenceEqual("SQLite format 3\0"u8))
        {
            return Rejected(
                candidateBytes > SyntheticSliceContractLimits.MaximumCandidateBytes
                    ? ArtifactAdmissionFailureCode.CandidateReadBudgetExceeded
                    : ArtifactAdmissionFailureCode.GraphIncomplete,
                "synthetic_sqlite");
        }

        return SyntheticSliceVerification.Accepted(
            manifest,
            control,
            Sha256Hex(manifestRead.Bytes),
            Sha256Hex(controlRead.Bytes),
            source.Bytes,
            derived.Bytes,
            sqlite.Bytes);
    }

    private SyntheticSliceVerification? ValidateManifest(SyntheticSliceArtifactManifest manifest)
    {
        if (!string.Equals(manifest.Environment.Class, "preview", StringComparison.Ordinal) ||
            !string.Equals(manifest.Environment.Binding, expectedEnvironmentBinding, StringComparison.Ordinal))
        {
            return Rejected(ArtifactAdmissionFailureCode.EnvironmentForbidden, "synthetic_manifest");
        }

        if (!string.Equals(manifest.Issuer.IssuerId, expectedIssuerId, StringComparison.Ordinal) ||
            !string.Equals(manifest.Issuer.KeyId, expectedKeyId, StringComparison.Ordinal))
        {
            return Rejected(ArtifactAdmissionFailureCode.KeyUntrusted, "synthetic_manifest");
        }

        if (!SameSchemaTable(manifest.SchemaTable, expectedSchemaTable))
        {
            return Rejected(ArtifactAdmissionFailureCode.GraphSchemaUnsupported, "synthetic_manifest");
        }

        return null;
    }

    private static async ValueTask<BlobRead> ReadBlobAsync(
        ISyntheticSliceCandidate candidate,
        SyntheticSliceBlobDescriptor descriptor,
        int maximumBytes,
        int bytesAlreadyRead,
        CancellationToken cancellationToken)
    {
        await using var stream = await candidate
            .OpenBlobAsync(descriptor.Kind, descriptor.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var read = await BoundedStreamReader.ReadAsync(stream, maximumBytes, cancellationToken).ConfigureAwait(false);
        if (read.ExceededLimit)
        {
            return BlobRead.Rejected(Rejected(ArtifactAdmissionFailureCode.BlobTooLarge, "synthetic_blob"));
        }

        if (bytesAlreadyRead + read.Bytes.Length > SyntheticSliceContractLimits.MaximumCandidateBytes)
        {
            return BlobRead.Rejected(Rejected(
                ArtifactAdmissionFailureCode.CandidateReadBudgetExceeded,
                "synthetic_blob"));
        }

        if (read.Bytes.LongLength != descriptor.Bytes)
        {
            return BlobRead.Rejected(Rejected(
                ArtifactAdmissionFailureCode.BlobSizeMismatch,
                "synthetic_blob"));
        }

        return DigestMatches(read.Bytes, descriptor.Sha256)
            ? BlobRead.Accepted(read.Bytes)
            : BlobRead.Rejected(Rejected(
                ArtifactAdmissionFailureCode.BlobDigestMismatch,
                "synthetic_blob"));
    }

    private static bool SameSchemaTable(
        SyntheticSliceSchemaTable actual,
        SyntheticSliceSchemaTable expected) =>
        actual.Members.Count == expected.Members.Count &&
        actual.Members.Zip(expected.Members).All(static pair => SameSchemaMember(pair.First, pair.Second));

    private static bool SameSchemaMember(
        SyntheticSliceSchemaMember actual,
        SyntheticSliceSchemaMember expected) =>
        string.Equals(actual.Schema, expected.Schema, StringComparison.Ordinal) &&
        string.Equals(actual.SchemaResource, expected.SchemaResource, StringComparison.Ordinal) &&
        string.Equals(actual.Sha256, expected.Sha256, StringComparison.Ordinal) &&
        actual.Bytes == expected.Bytes;

    private static bool VerifySignature(
        SyntheticSliceArtifactManifest manifest,
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
            return bytesRead == subjectPublicKeyInfo.Length &&
                   verifier.KeySize == 256 &&
                   string.Equals(
                       verifier.ExportParameters(false).Curve.Oid.Value,
                       ECCurve.NamedCurves.nistP256.Oid.Value,
                       StringComparison.Ordinal) &&
                   verifier.VerifyData(
                       SyntheticSliceArtifactCanonicalizer.GetSigningBytes(manifest),
                       signature,
                       HashAlgorithmName.SHA256,
                       DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static bool DigestMatches(ReadOnlySpan<byte> bytes, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(bytes),
            Convert.FromHexString(expected));

    private static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string StrictUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new JsonException("Contract JSON is not strict UTF-8.", exception);
        }
    }

    private static string RequireText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string RequireSha256(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", parameterName);
        }

        return value;
    }

    private static SyntheticSliceVerification Rejected(
        ArtifactAdmissionFailureCode code,
        string stage) =>
        SyntheticSliceVerification.Rejected(new ArtifactAdmissionFailure(code, stage));

    private sealed record BlobRead(byte[]? Bytes, SyntheticSliceVerification? Failure)
    {
        public static BlobRead Accepted(byte[] bytes) => new(bytes, null);

        public static BlobRead Rejected(SyntheticSliceVerification failure) => new(null, failure);
    }
}

public sealed class SyntheticSliceVerification
{
    private SyntheticSliceVerification(
        SyntheticSliceArtifactManifest? manifest,
        SyntheticSliceControl? control,
        string? manifestSha256,
        string? controlSha256,
        byte[]? sourceBytes,
        byte[]? derivedBytes,
        byte[]? sqliteBytes,
        ArtifactAdmissionFailure? failure)
    {
        Manifest = manifest;
        Control = control;
        ManifestSha256 = manifestSha256;
        ControlSha256 = controlSha256;
        SourceBytes = sourceBytes;
        DerivedBytes = derivedBytes;
        SqliteBytes = sqliteBytes;
        Failure = failure;
    }

    public bool Verified => Failure is null;

    public SyntheticSliceArtifactManifest? Manifest { get; }

    public SyntheticSliceControl? Control { get; }

    public string? ManifestSha256 { get; }

    public string? ControlSha256 { get; }

    public ReadOnlyMemory<byte> SourceBytes { get; }

    public ReadOnlyMemory<byte> DerivedBytes { get; }

    public ReadOnlyMemory<byte> SqliteBytes { get; }

    public ArtifactAdmissionFailure? Failure { get; }

    internal static SyntheticSliceVerification Accepted(
        SyntheticSliceArtifactManifest manifest,
        SyntheticSliceControl control,
        string manifestSha256,
        string controlSha256,
        byte[] sourceBytes,
        byte[] derivedBytes,
        byte[] sqliteBytes) =>
        new(
            manifest,
            control,
            manifestSha256,
            controlSha256,
            sourceBytes,
            derivedBytes,
            sqliteBytes,
            null);

    internal static SyntheticSliceVerification Rejected(ArtifactAdmissionFailure failure) =>
        new(null, null, null, null, null, null, null, failure);
}
