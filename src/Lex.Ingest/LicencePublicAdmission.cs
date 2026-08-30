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
    private const string LicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    public static LicenceAdmissionResult Assess(ManifestationLicenceEvidence? evidence)
    {
        if (evidence is null)
            return new(PublicTextAdmission.LicenceUnresolved, null);

        var (comparison, agreedUris) = CompareChannels(evidence.Sparql, evidence.File);
        if (evidence.Comparison != comparison)
            return new(PublicTextAdmission.LicenceUnresolved, null);

        return comparison switch
        {
            LicenceComparison.Agreed when agreedUris is [CreativeCommonsBy40] =>
                new(PublicTextAdmission.Admitted, CreativeCommonsBy40),
            LicenceComparison.Agreed =>
                new(PublicTextAdmission.LicenceUnsupported, null),
            LicenceComparison.LicenceConflict =>
                new(PublicTextAdmission.LicenceConflict, null),
            _ => new(PublicTextAdmission.LicenceUnresolved, null),
        };
    }

    private static (LicenceComparison Comparison, string[]? AgreedUris) CompareChannels(
        LicenceChannelEvidence sparql,
        LicenceChannelEvidence file)
    {
        switch ((sparql.State, file.State))
        {
            case (LicenceChannelState.Absent, LicenceChannelState.Absent):
                return (LicenceComparison.LicenceUnresolved, null);
            case (LicenceChannelState.Absent, LicenceChannelState.Present):
                return ExactFileUris(file) is null
                    ? (LicenceComparison.LicenceUnresolved, null)
                    : (LicenceComparison.LicenceConflict, null);
            case (LicenceChannelState.Present, LicenceChannelState.Absent):
                return ExactSparqlUris(sparql) is null
                    ? (LicenceComparison.LicenceUnresolved, null)
                    : (LicenceComparison.LicenceConflict, null);
            case (LicenceChannelState.Present, LicenceChannelState.Present):
                break;
            default:
                return (LicenceComparison.LicenceUnresolved, null);
        }

        var sparqlUris = ExactSparqlUris(sparql);
        var fileUris = ExactFileUris(file);
        if (sparqlUris is null || fileUris is null)
            return (LicenceComparison.LicenceUnresolved, null);
        return sparqlUris.SequenceEqual(fileUris, StringComparer.Ordinal)
            ? (LicenceComparison.Agreed, sparqlUris)
            : (LicenceComparison.LicenceConflict, null);
    }

    private static string[]? ExactSparqlUris(LicenceChannelEvidence channel)
    {
        if (channel.Claims.Count == 0
            || channel.Claims.Any(claim => !IsExactSparqlClaim(claim)))
            return null;
        return OrderedUris(channel);
    }

    private static string[]? ExactFileUris(LicenceChannelEvidence channel)
    {
        if (channel.Claims.Count == 0
            || channel.Claims.Any(claim => !IsExactFileClaim(claim)))
            return null;
        return OrderedUris(channel);
    }

    private static string[] OrderedUris(LicenceChannelEvidence channel) =>
        channel.Claims.Select(claim => claim.LicenceUri!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsExactSparqlClaim(LicenceClaim claim) =>
        string.Equals(claim.TermType, "uri", StringComparison.Ordinal)
        && string.Equals(claim.Value, claim.LicenceUri, StringComparison.Ordinal)
        && Uri.TryCreate(claim.LicenceUri, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !string.IsNullOrEmpty(uri.Host);

    private static bool IsExactFileClaim(LicenceClaim claim) =>
        string.Equals(claim.TermType, "token", StringComparison.Ordinal)
        && (claim.Value, claim.LicenceUri) is
            ("CC-BY-4.0", CreativeCommonsBy40)
                or ("licenceSCL", LicenceScl);
}
