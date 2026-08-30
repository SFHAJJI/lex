using Lex.Law;
using Lex.Sources.Legilux;

namespace Lex.Ingest;

public enum PublicTextAdmission
{
    LicenceUnresolved = 0,
    Admitted,
    LicenceConflict,
    LicenceUnsupported,
}

public sealed record LicenceAdmissionResult(
    PublicTextAdmission Outcome,
    string? BasisUri);

/// <summary>
/// Closed initial publication policy for assessed Luxembourg manifestations. Channel comparison
/// is evidence; this separate policy decision is the only value allowed to authorize public text.
/// </summary>
public static class LicencePublicAdmission
{
    public static LicenceAdmissionResult Assess(ManifestationLicenceEvidence? evidence)
    {
        if (evidence is null)
            return new(PublicTextAdmission.LicenceUnresolved, null);

        var comparison = LegiluxLicenceContract.Compare(
            evidence.Sparql, evidence.File, out var agreedUris);
        if (evidence.Comparison != comparison)
            return new(PublicTextAdmission.LicenceUnresolved, null);

        return comparison switch
        {
            LicenceComparison.Agreed when agreedUris is
                [LegiluxLicenceContract.CreativeCommonsBy40] =>
                new(PublicTextAdmission.Admitted,
                    LegiluxLicenceContract.CreativeCommonsBy40),
            LicenceComparison.Agreed =>
                new(PublicTextAdmission.LicenceUnsupported, null),
            LicenceComparison.LicenceConflict =>
                new(PublicTextAdmission.LicenceConflict, null),
            _ => new(PublicTextAdmission.LicenceUnresolved, null),
        };
    }
}
