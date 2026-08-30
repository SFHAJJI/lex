using Lex.Law;

namespace Lex.Sources.Legilux;

/// <summary>
/// The one boundary that validates and compares raw Legilux licence-channel syntax.
/// Publisher-neutral assemblies only retain the observed values and normalized URIs.
/// </summary>
public static class LegiluxLicenceContract
{
    public const string CreativeCommonsBy40 =
        "http://creativecommons.org/licenses/by/4.0/";
    private const string LicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    public static LicenceComparison Compare(
        LicenceChannelEvidence sparql,
        LicenceChannelEvidence file,
        out string[] agreedUris)
    {
        ArgumentNullException.ThrowIfNull(sparql);
        ArgumentNullException.ThrowIfNull(file);
        agreedUris = [];

        switch ((sparql.State, file.State))
        {
            case (LicenceChannelState.Absent, LicenceChannelState.Absent):
                return LicenceComparison.LicenceUnresolved;
            case (LicenceChannelState.Absent, LicenceChannelState.Present):
                return ExactFileUris(file) is null
                    ? LicenceComparison.LicenceUnresolved
                    : LicenceComparison.LicenceConflict;
            case (LicenceChannelState.Present, LicenceChannelState.Absent):
                return ExactSparqlUris(sparql) is null
                    ? LicenceComparison.LicenceUnresolved
                    : LicenceComparison.LicenceConflict;
            case (LicenceChannelState.Present, LicenceChannelState.Present):
                break;
            default:
                return LicenceComparison.LicenceUnresolved;
        }

        var sparqlUris = ExactSparqlUris(sparql);
        var fileUris = ExactFileUris(file);
        if (sparqlUris is null || fileUris is null)
            return LicenceComparison.LicenceUnresolved;
        if (!sparqlUris.SequenceEqual(fileUris, StringComparer.Ordinal))
            return LicenceComparison.LicenceConflict;

        agreedUris = sparqlUris;
        return LicenceComparison.Agreed;
    }

    internal static string? MapFileToken(string value) => value switch
    {
        "CC-BY-4.0" => CreativeCommonsBy40,
        "licenceSCL" => LicenceScl,
        _ => null,
    };

    internal static string? MapSparqlTerm(string termType, string value) =>
        string.Equals(termType, "uri", StringComparison.Ordinal)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !string.IsNullOrEmpty(uri.Host)
            ? value
            : null;

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
        MapSparqlTerm(claim.TermType, claim.Value) is { } mapped
        && string.Equals(mapped, claim.LicenceUri, StringComparison.Ordinal);

    private static bool IsExactFileClaim(LicenceClaim claim) =>
        string.Equals(claim.TermType, "token", StringComparison.Ordinal)
        && MapFileToken(claim.Value) is { } mapped
        && string.Equals(mapped, claim.LicenceUri, StringComparison.Ordinal);
}
