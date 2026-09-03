namespace Lex.V3.Contracts.Source.Quarantine;

/// <summary>
/// Which of the two required runs a reproduction is. Backlog Candidate 2 section 7.3 step 5 names
/// them concretely: "Reproduce the inventory independently twice, including one reviewer run" --
/// one production run and a second, separate reviewer run, never the same run counted twice.
/// </summary>
public enum QuarantineReproducerRole
{
    Primary = 1,
    IndependentReviewer = 2,
}

public enum QuarantineReproductionRefusal
{
    None = 0,
    RoleUndefined,
    ReproducerIdentityInvalid,
    CoordinatesEmpty,
    CoordinatesTooMany,
    DuplicateCoordinate,
}

/// <summary>
/// One independently produced walk of a retired V2 index's public coordinates: who produced it,
/// under which role, and the coordinates it found.
/// </summary>
/// <remarks>
/// <para>
/// This type never opens a V2 index itself. Backlog Candidate 2 section 7.3 is explicit that the
/// tool which pins to the exact previously promoted signed index pair and reads it is quarantined
/// outside the V3 tree entirely ("the V3 repository cannot contain or execute a V2 index reader"
/// ... "Remove the verifier from active worktrees and credentials after proof. It is absent from
/// the final V3 tree"). What this type receives is that external tool's already-produced output --
/// an ordered coordinate list and a claimed producer identity -- as plain, bounded, coordinate-only
/// data; there is no path, stream, or byte-content parameter anywhere on it for a V2 file to travel
/// through.
/// </para>
/// <para>
/// <see cref="CanonicalSha256"/> is never accepted from a caller. It is derived here, once, from
/// <see cref="Coordinates"/> alone via <see cref="PriorPublicCoordinateSet.CanonicalSha256Hex"/>,
/// so a caller cannot make two reproductions "agree" by supplying matching digest strings that do
/// not actually match their coordinate lists; <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/>
/// then compares two independently derived digests, never a value against a copy of itself.
/// </para>
/// </remarks>
public sealed class QuarantinePriorCoordinateReproduction
{
    /// <summary>
    /// A generous bound, not a claim about V2's real row count: it exists so a malformed or
    /// adversarial input cannot force an unbounded sort and hash over caller-supplied data.
    /// </summary>
    public const int MaximumCoordinates = 2_000_000;

    /// <summary>Internal, not private, only so tests can derive an over-the-bound literal from it
    /// instead of coupling to a hand-copied number.</summary>
    internal const int MaximumReproducerIdentityLength = 256;

    private QuarantinePriorCoordinateReproduction(
        QuarantineReproducerRole role,
        string reproducerIdentity,
        IReadOnlyList<PriorPublicCoordinate> coordinates,
        string canonicalSha256)
    {
        Role = role;
        ReproducerIdentity = reproducerIdentity;
        Coordinates = coordinates;
        CanonicalSha256 = canonicalSha256;
    }

    public QuarantineReproducerRole Role { get; }

    /// <summary>
    /// Who or what produced this run (for example a distinct operator or reviewer identity, never
    /// a shared default). <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/> refuses
    /// two reproductions sharing this value, which is what makes "independent" mean something
    /// stronger than "called twice."
    /// </summary>
    public string ReproducerIdentity { get; }

    public IReadOnlyList<PriorPublicCoordinate> Coordinates { get; }

    public int Count => Coordinates.Count;

    /// <summary>The SHA-256 of <see cref="PriorPublicCoordinateSet.CanonicalBytes"/> over <see cref="Coordinates"/>, computed here and only here.</summary>
    public string CanonicalSha256 { get; }

    public static QuarantinePriorCoordinateReproduction? TryCreate(
        QuarantineReproducerRole role,
        string reproducerIdentity,
        IReadOnlyList<PriorPublicCoordinate> coordinates,
        out QuarantineReproductionRefusal refusal)
    {
        if (!Enum.IsDefined(role))
        {
            refusal = QuarantineReproductionRefusal.RoleUndefined;
            return null;
        }

        if (string.IsNullOrWhiteSpace(reproducerIdentity) ||
            reproducerIdentity.Length > MaximumReproducerIdentityLength ||
            reproducerIdentity.Any(static character => character is < '!' or > '~'))
        {
            refusal = QuarantineReproductionRefusal.ReproducerIdentityInvalid;
            return null;
        }

        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count == 0)
        {
            refusal = QuarantineReproductionRefusal.CoordinatesEmpty;
            return null;
        }

        if (coordinates.Count > MaximumCoordinates)
        {
            refusal = QuarantineReproductionRefusal.CoordinatesTooMany;
            return null;
        }

        var seen = new HashSet<(string WorkKey, string Language, string ValidFrom, string? Anchor)>();
        foreach (var coordinate in coordinates)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            if (!seen.Add((coordinate.WorkKey, coordinate.Language, coordinate.ValidFrom, coordinate.Anchor)))
            {
                refusal = QuarantineReproductionRefusal.DuplicateCoordinate;
                return null;
            }
        }

        var frozen = Array.AsReadOnly(coordinates.ToArray());
        refusal = QuarantineReproductionRefusal.None;
        return new(role, reproducerIdentity, frozen, PriorPublicCoordinateSet.CanonicalSha256Hex(frozen));
    }
}
