namespace Lex.V3.Contracts.Source.Europe;

/// <summary>Why a first-cut watermark start position could not be computed. Closed.</summary>
public enum EuFirstCutWatermarkBootstrapRefusal
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>
    /// The census supplied no observations at all. There is no legitimate bound to start a witness
    /// from, and inventing a sentinel would be exactly the "observation time alone is silently
    /// promoted to a publisher watermark" failure R3 forbids.
    /// </summary>
    NoCensusObservations = 1,

    /// <summary>
    /// One census entry could not become a valid tie-safe position (a blank watermark, or a
    /// watermark with no canonical entry key -- <see cref="EuWatermarkCursor.TryOpen"/>'s own two
    /// causes, folded into one member here because this function has nowhere narrower to report
    /// which of the two fired and does not need one: either way, the census itself is malformed).
    /// </summary>
    InvalidCensusEntry = 2,

    /// <summary>The exact same (watermark, entry key) tuple was supplied more than once.</summary>
    DuplicateCensusEntry = 3,
}

/// <summary>
/// Decision 81: the first EU cut's witness starts at the cut's own census observation bound, never
/// before it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EuWatermarkWitnessPlan.TryFreeze"/>'s own doc comment says the gap this type closes in
/// so many words: "what supplies this [start] position for the first cut of all is not settled by R3
/// and is not invented here." Decision 81 settles it: "the first complete EU cut's positive change
/// witness starts at the cut's own census observation bound: the maximum
/// <c>cmr:lastModificationDate</c> observed for in pack roots during the discovery census, reread
/// inclusively with the tie safe boundary rule ... The witness never replays history before the
/// first complete cut; every change before that bound is covered by the census itself." This type is
/// that maximum, computed under the identical tie-safe ordering
/// <see cref="EuWatermarkCursor.CompareTo"/> already applies everywhere else a watermark position is
/// compared, so a caller cannot pick a different, weaker order for the one place it matters most.
/// </para>
/// <para>
/// This function does not perform the census itself and does not check that every supplied entry
/// really is an in-pack root: that is the primary enumeration's job
/// (<see cref="EuPrimaryEnumerationRootBinding"/>), and duplicating a pack-membership check here
/// would be the same drift risk R3.2 warns against for witness independence, applied to one
/// function's own inputs instead of two producers. The census observations passed in are required,
/// by Decision 81's own wording, to already be scoped to in-pack roots before they reach this
/// function.
/// </para>
/// </remarks>
public static class EuFirstCutWatermarkBootstrap
{
    /// <summary>The only path that computes a first-cut start position.</summary>
    /// <param name="censusObservations">
    /// Every in-pack root's observed <c>cmr:lastModificationDate</c> lexical value paired with its
    /// canonical entry key, from the discovery census. Order does not matter; the result is the
    /// tie-safe maximum regardless of input order.
    /// </param>
    /// <param name="refusal">Why no start position exists, when none does.</param>
    public static EuWatermarkCursor? TryComputeStartPosition(
        IReadOnlyList<(string WatermarkLexical, string CanonicalEntryKey)> censusObservations,
        out EuFirstCutWatermarkBootstrapRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(censusObservations);

        if (censusObservations.Count == 0)
        {
            refusal = EuFirstCutWatermarkBootstrapRefusal.NoCensusObservations;
            return null;
        }

        var seen = new HashSet<(string, string)>();
        EuWatermarkCursor? maximum = null;
        foreach (var (lexical, key) in censusObservations)
        {
            if (!seen.Add((lexical, key)))
            {
                refusal = EuFirstCutWatermarkBootstrapRefusal.DuplicateCensusEntry;
                return null;
            }

            var cursor = EuWatermarkCursor.TryOpen(lexical, key, out _);
            if (cursor is null)
            {
                refusal = EuFirstCutWatermarkBootstrapRefusal.InvalidCensusEntry;
                return null;
            }

            if (maximum is null || cursor.CompareTo(maximum) > 0)
            {
                maximum = cursor;
            }
        }

        refusal = EuFirstCutWatermarkBootstrapRefusal.None;
        return maximum;
    }
}
