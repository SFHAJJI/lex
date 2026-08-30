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
        if (sparql.State is LicenceChannelState.Invalid or LicenceChannelState.NotObserved
            || file.State is LicenceChannelState.Invalid or LicenceChannelState.NotObserved)
            return LicenceComparison.LicenceUnresolved;

        if (sparql.State == LicenceChannelState.Absent
            && file.State == LicenceChannelState.Absent)
            return LicenceComparison.LicenceUnresolved;

        if (sparql.State == LicenceChannelState.Absent
            || file.State == LicenceChannelState.Absent)
            return LicenceComparison.LicenceConflict;

        var sparqlUris = sparql.Claims.Select(claim => claim.LicenceUri!)
            .ToHashSet(StringComparer.Ordinal);
        var fileUris = file.Claims.Select(claim => claim.LicenceUri!)
            .ToHashSet(StringComparer.Ordinal);
        return sparqlUris.SetEquals(fileUris)
            ? LicenceComparison.Agreed
            : LicenceComparison.LicenceConflict;
    }
}
