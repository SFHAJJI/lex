namespace Lex.Law;

/// <summary>The outcome of observing one publisher licence channel.</summary>
public enum LicenceChannelState
{
    Present,
    Absent,
    Invalid,
    NotObserved,
}

/// <summary>A comparison of two independently observed licence channels.</summary>
public enum LicenceComparison
{
    Agreed,
    LicenceConflict,
    LicenceUnresolved,
}

/// <summary>
/// One exact channel value plus the URI used for set comparison. TermType and Value retain
/// publisher syntax; LicenceUri is null when that syntax cannot be mapped safely.
/// </summary>
public sealed record LicenceClaim(string TermType, string Value, string? LicenceUri);

/// <summary>A set-valued observation from one completed or attempted channel read.</summary>
public sealed record LicenceChannelEvidence
{
    public const int MaximumClaims = 64;
    public const int MaximumTermTypeLength = 32;
    public const int MaximumValueLength = 4_096;
    public const int MaximumLicenceUriLength = 4_096;

    private LicenceChannelEvidence(
        LicenceChannelState state, IReadOnlyList<LicenceClaim> claims)
    {
        State = state;
        Claims = claims;
    }

    public LicenceChannelState State { get; }
    public IReadOnlyList<LicenceClaim> Claims { get; }

    public static LicenceChannelEvidence Absent { get; } =
        new(LicenceChannelState.Absent, []);

    public static LicenceChannelEvidence NotObserved { get; } =
        new(LicenceChannelState.NotObserved, []);

    public static LicenceChannelEvidence Present(IEnumerable<LicenceClaim> claims)
    {
        var ordered = Ordered(claims);
        if (ordered.Length == 0
            || ordered.Any(claim => string.IsNullOrWhiteSpace(claim.LicenceUri)))
            throw new ArgumentException(
                "Present licence evidence requires at least one safely mapped claim.",
                nameof(claims));
        return new(LicenceChannelState.Present, ordered);
    }

    public static LicenceChannelEvidence Invalid(IEnumerable<LicenceClaim> claims) =>
        new(LicenceChannelState.Invalid, Ordered(claims));

    private static LicenceClaim[] Ordered(IEnumerable<LicenceClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var bounded = claims.ToArray();
        if (bounded.Length > MaximumClaims)
            throw new InvalidDataException(
                $"Licence evidence exceeds {MaximumClaims} claims in one channel.");
        foreach (var claim in bounded)
        {
            if (claim is null
                || claim.TermType is null
                || claim.Value is null
                || claim.TermType.Length > MaximumTermTypeLength
                || claim.Value.Length > MaximumValueLength
                || claim.LicenceUri?.Length > MaximumLicenceUriLength)
                throw new InvalidDataException(
                    "Licence evidence contains an unbounded or null claim value.");
        }
        return bounded.Distinct()
            .OrderBy(claim => claim.TermType, StringComparer.Ordinal)
            .ThenBy(claim => claim.Value, StringComparer.Ordinal)
            .ThenBy(claim => claim.LicenceUri, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>
/// Licence evidence bound to one exact publisher manifestation. This is observation and
/// comparison only; it deliberately carries no corpus-admission or publication decision.
/// </summary>
public sealed record ManifestationLicenceEvidence
{
    public const int MaximumIdentifierLength = 4_096;
    private const string CreativeCommonsBy40 =
        "http://creativecommons.org/licenses/by/4.0/";
    private const string LicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    private ManifestationLicenceEvidence(
        string manifestationIdentifier,
        string manifestationFileIdentifier,
        LicenceChannelEvidence sparql,
        LicenceChannelEvidence file,
        LicenceComparison comparison)
    {
        ManifestationIdentifier = manifestationIdentifier;
        ManifestationFileIdentifier = manifestationFileIdentifier;
        Sparql = sparql;
        File = file;
        Comparison = comparison;
    }

    public string ManifestationIdentifier { get; }
    public string ManifestationFileIdentifier { get; }
    public LicenceChannelEvidence Sparql { get; }
    public LicenceChannelEvidence File { get; }
    public LicenceComparison Comparison { get; }

    public static ManifestationLicenceEvidence AwaitingFile(
        string manifestationIdentifier,
        string manifestationFileIdentifier,
        LicenceChannelEvidence sparql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestationIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestationFileIdentifier);
        if (manifestationIdentifier.Length > MaximumIdentifierLength
            || manifestationFileIdentifier.Length > MaximumIdentifierLength)
            throw new InvalidDataException(
                $"Manifestation licence identifiers exceed {MaximumIdentifierLength} characters.");
        ArgumentNullException.ThrowIfNull(sparql);
        return new(manifestationIdentifier, manifestationFileIdentifier, sparql,
            LicenceChannelEvidence.NotObserved, LicenceComparison.LicenceUnresolved);
    }

    public ManifestationLicenceEvidence WithFile(LicenceChannelEvidence file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new(ManifestationIdentifier, ManifestationFileIdentifier,
            Sparql, file, Compare(Sparql, file));
    }

    private static LicenceComparison Compare(
        LicenceChannelEvidence sparql, LicenceChannelEvidence file)
    {
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
        return sparqlUris.SequenceEqual(fileUris, StringComparer.Ordinal)
            ? LicenceComparison.Agreed
            : LicenceComparison.LicenceConflict;
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
