using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a watermark cursor or a boundary crossing is refused. Closed.
/// </summary>
public enum EuWatermarkRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The cursor carries a watermark value and no canonical entry key, which is a date-only
    /// cursor. R3 forbids it by name.
    /// </summary>
    [JsonStringEnumMemberName("date_only_cursor")]
    DateOnlyCursor = 1,

    /// <summary>The watermark lexical value is empty, so there is nothing to order by.</summary>
    [JsonStringEnumMemberName("watermark_absent")]
    WatermarkAbsent = 2,

    /// <summary>
    /// An entry at the boundary timestamp appears in neither the retained tie set nor the reread,
    /// so the crossing cannot show it was carried rather than skipped.
    /// </summary>
    [JsonStringEnumMemberName("boundary_entry_skipped")]
    BoundaryEntrySkipped = 3,

    /// <summary>
    /// An entry at the boundary timestamp was emitted by both pages. The inclusive reread exists
    /// to see those entries again, not to deliver them twice.
    /// </summary>
    [JsonStringEnumMemberName("boundary_entry_duplicated")]
    BoundaryEntryDuplicated = 4,

    /// <summary>
    /// The next page does not begin at or after the boundary. Its first entry sorts below the
    /// cursor, so the traversal moved backwards.
    /// </summary>
    [JsonStringEnumMemberName("page_not_ordered_after_cursor")]
    PageNotOrderedAfterCursor = 5,
}

/// <summary>
/// A tie-safe position in a Cellar last-modification traversal: a watermark value together with
/// the canonical entry key that shares it.
/// </summary>
/// <remarks>
/// <para>
/// R3 forbids a date-only cursor by name, and the reason is the failure it produces rather than
/// any elegance. Many Cellar entries carry the same <c>cdm:lastModificationDate</c> to the second.
/// A traversal paging on <c>lastModificationDate &gt; cursor</c> steps past every entry sharing the
/// boundary second, and one paging on <c>&gt;=</c> re-delivers them. The first silently drops law
/// that changed; the second inflates counts that a completeness check then reconciles against
/// itself. Neither reports anything, which is what makes this worth a type.
/// </para>
/// <para>
/// So the ordering tuple is the watermark and the entry key together, compared ordinally in that
/// order, and a cursor cannot be constructed from a watermark alone.
/// </para>
/// </remarks>
public sealed class EuWatermarkCursor : IComparable<EuWatermarkCursor>
{
    private EuWatermarkCursor(string watermarkLexical, string canonicalEntryKey)
    {
        WatermarkLexical = watermarkLexical;
        CanonicalEntryKey = canonicalEntryKey;
    }

    /// <summary>
    /// The publisher's value exactly as it was served. Retained lexically rather than parsed,
    /// because the ordering the publisher applied is over its own lexical form and a round trip
    /// through a date type can normalise precision the endpoint did not.
    /// </summary>
    public string WatermarkLexical { get; }

    /// <summary>The canonical entry key that shares this watermark.</summary>
    public string CanonicalEntryKey { get; }

    /// <summary>
    /// The only path that mints a cursor.
    /// </summary>
    public static EuWatermarkCursor? TryOpen(
        string watermarkLexical,
        string canonicalEntryKey,
        out EuWatermarkRefusal refusal)
    {
        if (string.IsNullOrEmpty(watermarkLexical))
        {
            refusal = EuWatermarkRefusal.WatermarkAbsent;
            return null;
        }

        // The refusal a date-only cursor earns, named so the failure is legible in evidence
        // rather than surfacing later as a hole in a corpus.
        if (string.IsNullOrEmpty(canonicalEntryKey))
        {
            refusal = EuWatermarkRefusal.DateOnlyCursor;
            return null;
        }

        refusal = EuWatermarkRefusal.None;
        return new EuWatermarkCursor(watermarkLexical, canonicalEntryKey);
    }

    /// <summary>Ordinal on the tuple, watermark first. Never on the watermark alone.</summary>
    public int CompareTo(EuWatermarkCursor? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byWatermark = string.CompareOrdinal(WatermarkLexical, other.WatermarkLexical);
        return byWatermark != 0
            ? byWatermark
            : string.CompareOrdinal(CanonicalEntryKey, other.CanonicalEntryKey);
    }
}

/// <summary>
/// One page boundary crossed, with the proof that entries sharing the boundary watermark were
/// carried across it exactly once.
/// </summary>
/// <remarks>
/// The inclusive reread is what makes the crossing checkable: the next request starts at the
/// boundary watermark rather than after it, so every entry sharing that value is seen again. This
/// type then requires that each of them is accounted for exactly once across the two pages. Seeing
/// them again is not the point; being able to say afterwards that none was skipped and none was
/// delivered twice is.
/// </remarks>
public sealed class EuBoundaryCrossing
{
    private EuBoundaryCrossing(
        EuWatermarkCursor cursor,
        IReadOnlyList<string> retainedTieSet,
        IReadOnlyList<string> carriedForward)
    {
        Cursor = cursor;
        RetainedTieSet = retainedTieSet;
        CarriedForward = carriedForward;
    }

    /// <summary>The tie-safe position the earlier page ended at.</summary>
    public EuWatermarkCursor Cursor { get; }

    /// <summary>
    /// Every entry key the earlier page emitted at the boundary watermark, including the cursor's
    /// own. What the reread must account for.
    /// </summary>
    public IReadOnlyList<string> RetainedTieSet { get; }

    /// <summary>
    /// The entries at the boundary watermark that the reread delivered because the earlier page
    /// had not. Empty when the earlier page emitted the whole tie group.
    /// </summary>
    public IReadOnlyList<string> CarriedForward { get; }

    /// <summary>
    /// Cross a page boundary, proving the tie group was neither skipped nor duplicated.
    /// </summary>
    /// <param name="cursor">Where the earlier page ended.</param>
    /// <param name="retainedTieSet">Entry keys the earlier page emitted at that watermark.</param>
    /// <param name="rereadAtBoundary">Entry keys the reread returned at that same watermark.</param>
    /// <param name="firstBeyondBoundary">
    /// The first position the next page emits above the boundary watermark, when it emits one.
    /// </param>
    public static EuBoundaryCrossing? TryCross(
        EuWatermarkCursor cursor,
        IReadOnlyList<string> retainedTieSet,
        IReadOnlyList<string> rereadAtBoundary,
        EuWatermarkCursor? firstBeyondBoundary,
        out EuWatermarkRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(retainedTieSet);
        ArgumentNullException.ThrowIfNull(rereadAtBoundary);

        var retained = new HashSet<string>(retainedTieSet, StringComparer.Ordinal);
        if (retained.Count != retainedTieSet.Count)
        {
            refusal = EuWatermarkRefusal.BoundaryEntryDuplicated;
            return null;
        }

        var carried = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in rereadAtBoundary)
        {
            if (!seen.Add(entry))
            {
                // Twice within one reread is a duplicate whether or not the earlier page saw it.
                refusal = EuWatermarkRefusal.BoundaryEntryDuplicated;
                return null;
            }

            if (!retained.Contains(entry))
            {
                carried.Add(entry);
            }
        }

        // Every entry the earlier page emitted at this watermark must appear in the reread. One
        // that does not is the skip the inclusive boundary exists to make visible: it was in the
        // tie group, and the next window no longer shows it.
        foreach (var entry in retained)
        {
            if (!seen.Contains(entry))
            {
                refusal = EuWatermarkRefusal.BoundaryEntrySkipped;
                return null;
            }
        }

        // A next position at or below the cursor means the traversal did not advance, which a
        // date-only comparison would not have noticed either.
        if (firstBeyondBoundary is not null && firstBeyondBoundary.CompareTo(cursor) <= 0)
        {
            refusal = EuWatermarkRefusal.PageNotOrderedAfterCursor;
            return null;
        }

        refusal = EuWatermarkRefusal.None;
        return new EuBoundaryCrossing(cursor, retainedTieSet.ToArray(), carried.ToArray());
    }
}
