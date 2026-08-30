namespace Lex.V3.Artifacts;

public enum ArtifactAdmissionFailureCode
{
    HeaderTooLarge,
    MalformedHeader,
    DuplicateMember,
    UnknownMember,
    PreviewSchemaForbidden,
    SyntheticFlagForbidden,
    SyntheticEvidenceForbidden,
    SyntheticSourceForbidden,
    EnvironmentForbidden,
    IssuerRoleForbidden,
    ReleaseSchemaUnsupported,
    IssuerUntrusted,
    KeyUntrusted,
    AlgorithmUnsupported,
    SignatureInvalid,
    PayloadSizeMismatch,
    PayloadDigestMismatch,
    GraphSchemaUnsupported,
    GraphIncomplete,
}

public sealed class ArtifactAdmissionFailure
{
    internal ArtifactAdmissionFailure(ArtifactAdmissionFailureCode code, string stage)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        Code = code;
        Stage = stage;
    }

    public ArtifactAdmissionFailureCode Code { get; }

    public string Stage { get; }
}

public sealed class ArtifactAdmissionInspection
{
    internal ArtifactAdmissionInspection(ArtifactAdmissionFailure failure)
    {
        Failure = failure ?? throw new ArgumentNullException(nameof(failure));
    }

    public ArtifactAdmissionFailure Failure { get; }

    public bool Admitted => false;
}
