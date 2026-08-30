using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

public sealed class ProductionArtifactAdmission
{
    private readonly string productionEnvironmentBinding;

    private ProductionArtifactAdmission(string productionEnvironmentBinding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productionEnvironmentBinding);
        this.productionEnvironmentBinding = productionEnvironmentBinding;
    }

    public static ProductionArtifactAdmission CreateStageZero(string productionEnvironmentBinding) =>
        new(productionEnvironmentBinding);

    public async ValueTask<ArtifactAdmissionInspection> InspectAsync(
        IArtifactCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        await using var manifestStream = await candidate
            .OpenAdmissionManifestAsync(cancellationToken)
            .ConfigureAwait(false);
        var bounded = await BoundedStreamReader
            .ReadAsync(manifestStream, PreviewContractLimits.MaximumManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        if (bounded.ExceededLimit)
        {
            return Rejected(ArtifactAdmissionFailureCode.HeaderTooLarge);
        }

        var parsed = AdmissionHeaderReader.Read(bounded.Bytes);
        if (parsed.Failure is not null)
        {
            return new ArtifactAdmissionInspection(parsed.Failure);
        }

        var header = parsed.Header!;
        if (string.Equals(header.Schema, V3SchemaIds.PreviewArtifact, StringComparison.Ordinal))
        {
            return Rejected(ArtifactAdmissionFailureCode.PreviewSchemaForbidden);
        }

        if (header.Synthetic)
        {
            return Rejected(ArtifactAdmissionFailureCode.SyntheticFlagForbidden);
        }

        if (string.Equals(header.EvidenceClass, "synthetic_preview", StringComparison.Ordinal))
        {
            return Rejected(ArtifactAdmissionFailureCode.SyntheticEvidenceForbidden);
        }

        if (string.Equals(header.SourceKind, "synthetic_test", StringComparison.Ordinal))
        {
            return Rejected(ArtifactAdmissionFailureCode.SyntheticSourceForbidden);
        }

        if (!string.Equals(header.EnvironmentClass, "production", StringComparison.Ordinal) ||
            !string.Equals(
                header.EnvironmentBinding,
                productionEnvironmentBinding,
                StringComparison.Ordinal))
        {
            return Rejected(ArtifactAdmissionFailureCode.EnvironmentForbidden);
        }

        if (string.Equals(header.IssuerRole, "preview_attestor", StringComparison.Ordinal) ||
            string.Equals(header.IssuerRole, "migration_inventory", StringComparison.Ordinal))
        {
            return Rejected(ArtifactAdmissionFailureCode.IssuerRoleForbidden);
        }

        // Decision 55 requires Stage 0 to have no production release schema.
        return Rejected(ArtifactAdmissionFailureCode.ReleaseSchemaUnsupported);
    }

    private static ArtifactAdmissionInspection Rejected(ArtifactAdmissionFailureCode code) =>
        new(new ArtifactAdmissionFailure(code, "production_admission"));
}
