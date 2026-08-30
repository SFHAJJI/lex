using Lex.Law;

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
    public const string CreativeCommonsBy40 =
        "http://creativecommons.org/licenses/by/4.0/";

    public static LicenceAdmissionResult Assess(ManifestationLicenceEvidence? evidence)
    {
        if (evidence is null)
            return new(PublicTextAdmission.LicenceUnresolved, null);

        switch (evidence.Comparison)
        {
            case LicenceComparison.Agreed:
                break;
            case LicenceComparison.LicenceConflict:
                return new(PublicTextAdmission.LicenceConflict, null);
            case LicenceComparison.LicenceUnresolved:
            default:
                return new(PublicTextAdmission.LicenceUnresolved, null);
        }

        if (evidence.Sparql.State != LicenceChannelState.Present
            || evidence.File.State != LicenceChannelState.Present)
            return new(PublicTextAdmission.LicenceUnresolved, null);

        var sparqlUris = ExactUris(evidence.Sparql);
        var fileUris = ExactUris(evidence.File);
        if (sparqlUris is null || fileUris is null)
            return new(PublicTextAdmission.LicenceUnresolved, null);
        if (!sparqlUris.SequenceEqual(fileUris, StringComparer.Ordinal))
            return new(PublicTextAdmission.LicenceConflict, null);

        return sparqlUris is [CreativeCommonsBy40]
            ? new(PublicTextAdmission.Admitted, CreativeCommonsBy40)
            : new(PublicTextAdmission.LicenceUnsupported, null);
    }

    private static string[]? ExactUris(LicenceChannelEvidence channel)
    {
        if (channel.Claims.Count == 0
            || channel.Claims.Any(claim =>
                string.IsNullOrWhiteSpace(claim.LicenceUri)))
            return null;
        return channel.Claims.Select(claim => claim.LicenceUri!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
