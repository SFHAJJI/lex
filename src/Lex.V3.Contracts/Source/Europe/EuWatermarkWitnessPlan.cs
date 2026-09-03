using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why the Cellar last-modification witness plan refused to freeze, or refused to render a page
/// for a position. Closed.
/// </summary>
public enum EuWatermarkPlanRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The endpoint is not the one R3 names for this witness. Decision 23 forbids EUR-Lex as an
    /// automated body channel, and R7.1's official-origin allowlist is about content paths rather
    /// than about which store may answer a completeness question, so the witness endpoint is not a
    /// configuration choice.
    /// </summary>
    [JsonStringEnumMemberName("endpoint_not_the_official_cellar_endpoint")]
    EndpointNotTheOfficialCellarEndpoint = 1,

    /// <summary>
    /// The predicate is not <c>cmr:lastModificationDate</c>. A witness over a different predicate
    /// is a different witness and needs its own bounded observation of order semantics.
    /// </summary>
    [JsonStringEnumMemberName("predicate_not_the_watermark_predicate")]
    PredicateNotTheWatermarkPredicate = 2,

    /// <summary>
    /// A page limit below <see cref="EuWatermarkWitnessPlan.MinimumPageLimit"/>. The inclusive
    /// reread spends the head of every page re-delivering the boundary tie group, so a one-row
    /// page can never carry a row beyond the boundary and the traversal can never advance.
    /// </summary>
    [JsonStringEnumMemberName("page_limit_below_minimum")]
    PageLimitBelowMinimum = 3,

    /// <summary>
    /// A page limit above the sorted-result window. R3.2 records sorted
    /// <c>OFFSET + LIMIT &gt; 10000</c> as a permanent platform constraint on both observed
    /// stores; this plan emits no OFFSET, so the constraint binds the limit alone.
    /// </summary>
    [JsonStringEnumMemberName("page_limit_above_sorted_result_window")]
    PageLimitAboveSortedResultWindow = 4,

    /// <summary>
    /// The start position's watermark has no readable fixed-width lexical shape, so no ordering
    /// relation can be frozen from it.
    /// </summary>
    [JsonStringEnumMemberName("start_position_not_lexically_orderable")]
    StartPositionNotLexicallyOrderable = 5,

    /// <summary>
    /// The position offered to the renderer does not share the plan's frozen lexical profile, so
    /// the order the endpoint would apply and the order the cursor compares in are no longer the
    /// same relation.
    /// </summary>
    [JsonStringEnumMemberName("position_not_in_plan_lexical_profile")]
    PositionNotInPlanLexicalProfile = 6,
}

/// <summary>
/// The frozen query plan half of <c>cellar_last_modification_witness/1</c>: the exact endpoint,
/// predicate, ordering tuple, boundary rule and page limit of the bounded SPARQL traversal that
/// R3 permits as an EU positive-change witness when no official feed URI is selected.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EuWatermarkCursor"/> is the cursor half and is used here rather than re-derived: the
/// ordering tuple is that type's comparison, and a page can only be rendered from a cursor, so a
/// date-only continuation cannot be expressed at all.
/// </para>
/// <para>
/// The traversal orders lexically, not chronologically, and that is deliberate. The bounded
/// observation of 2026-09-03 recorded <c>cmr:lastModificationDate</c> as an <c>xsd:dateTime</c>
/// with millisecond precision and an explicit <c>+02:00</c> offset, not UTC-normalised. Ordering
/// the typed value orders instants; the cursor compares the retained lexical bytes ordinally.
/// Those two relations disagree whenever the offset or the fractional precision varies, and a
/// traversal whose publisher order differs from its cursor order skips or re-delivers rows without
/// reporting anything. So the query binds <c>STR()</c> of the watermark and orders that, which
/// makes the publisher's order and the cursor's order one relation by construction. The cost is
/// that the lower boundary is a lexical boundary rather than an instant: this witness must never
/// be described as everything that changed since instant T.
/// </para>
/// <para>
/// Lexical order equals chronological order only within one fixed lexical profile, so the plan
/// freezes the profile it saw and <see cref="SharesLexicalProfile"/> refuses anything else. At a
/// daylight-saving transition the publisher's offset changes and this fails closed, once, with a
/// named cause, rather than silently dropping the entries whose lexical order inverted.
/// </para>
/// <para>
/// What this type deliberately does not do. It does not intersect entries with the 82-seed root
/// pack: R3 runs this witness unscoped and then partitions its entries through
/// <c>eu_feed_root_intersection/1</c>, where an out-of-pack entry terminates as retained
/// positive-only evidence rather than being filtered away, so scoping the query itself would
/// destroy exactly the evidence R7 requires to be kept. It does not carry HTTP observations, byte
/// digests, parser identity or the entry-set digest, which are the receipt half of the witness. It
/// does not bind itself to <c>RepeatedEnumerationInterpretationProfile</c>, because that profile
/// requires a pre-count and a post-count over a partition and a witness has neither; R3 asks a
/// witness for a complete sorted entry set and digest instead.
/// </para>
/// </remarks>
public sealed class EuWatermarkWitnessPlan
{
    /// <summary>The one endpoint R3 names for this witness.</summary>
    public const string OfficialCellarSparqlEndpoint =
        "https://publications.europa.eu/webapi/rdf/sparql";

    /// <summary>The publisher-supplied watermark predicate R3 names.</summary>
    public const string WatermarkPredicateIri =
        "http://publications.europa.eu/ontology/cdm/cmr#lastModificationDate";

    /// <summary>
    /// Below this the traversal can never advance, because the inclusive reread always spends the
    /// first row on the boundary position itself.
    /// </summary>
    public const int MinimumPageLimit = 2;

    /// <summary>
    /// The sorted-result window from R3.2. This plan emits no OFFSET, so the recorded
    /// <c>OFFSET + LIMIT &gt; 10000</c> constraint binds the limit with OFFSET at zero.
    /// </summary>
    public const int SortedResultWindowRows = 10_000;

    /// <summary>The schema this plan is an instance of.</summary>
    public const string SchemaId = "cellar_last_modification_witness/1";

    /// <summary>
    /// The ordering tuple, named for the receipt. It is <see cref="EuWatermarkCursor"/>'s
    /// comparison and nothing else.
    /// </summary>
    public const string OrderingTupleIdentity = "watermark_lexical,canonical_entry_key";

    /// <summary>
    /// The boundary rule, named for the receipt. Each page reads from the boundary watermark
    /// inclusively, so the whole tie group is seen again and <see cref="EuBoundaryCrossing"/> can
    /// say afterwards that none of it was skipped or delivered twice.
    /// </summary>
    public const string BoundaryRuleIdentity = "inclusive_watermark_reread/1";

    private const string LowerBoundarySlot = "{watermark_lower_boundary}";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _template;

    private EuWatermarkWitnessPlan(
        string endpoint,
        string predicateIri,
        int pageLimit,
        EuWatermarkCursor startPosition,
        int watermarkFractionalDigits,
        string watermarkOffsetToken,
        string template,
        string queryPlanIdentityDigest)
    {
        Endpoint = endpoint;
        PredicateIri = predicateIri;
        PageLimit = pageLimit;
        StartPosition = startPosition;
        WatermarkFractionalDigits = watermarkFractionalDigits;
        WatermarkOffsetToken = watermarkOffsetToken;
        _template = template;
        QueryPlanIdentityDigest = queryPlanIdentityDigest;
    }

    /// <summary>The official Cellar SPARQL endpoint this witness runs against.</summary>
    public string Endpoint { get; }

    /// <summary>The watermark predicate, full IRI.</summary>
    public string PredicateIri { get; }

    /// <summary>Rows per page. Keyset paging only; this plan has no offset.</summary>
    public int PageLimit { get; }

    /// <summary>
    /// The tie-safe lower boundary this cut traverses from, which is the position the previous cut
    /// ended at. Required rather than optional: an unbounded traversal ordered by watermark is a
    /// census of every Cellar entry carrying the predicate, not a change witness, and R3.2 makes a
    /// selected-row count at the 1,000,000 ceiling ambiguous rather than complete. What supplies
    /// this position for the first cut of all is not settled by R3 and is not invented here.
    /// </summary>
    public EuWatermarkCursor StartPosition { get; }

    /// <summary>
    /// Fractional second digits in the frozen lexical profile. Three at the bounded observation of
    /// 2026-09-03.
    /// </summary>
    public int WatermarkFractionalDigits { get; }

    /// <summary>
    /// The exact offset token in the frozen lexical profile, such as <c>+02:00</c> or <c>Z</c>.
    /// Retained as the publisher's own bytes and never normalised.
    /// </summary>
    public string WatermarkOffsetToken { get; }

    /// <summary>
    /// SHA-256 over the plan's query identity: endpoint, predicate, ordering tuple, boundary rule,
    /// page limit and the unbound query template.
    /// </summary>
    /// <remarks>
    /// Deliberately not a function of <see cref="StartPosition"/>. The query plan is the thing that
    /// stays fixed while cuts advance; folding this cut's boundary into it would produce a new
    /// query-plan identity every run and make the digest useless for saying that two cuts used the
    /// same plan. The boundary is bound by the witness receipt beside the digest, not inside it.
    /// </remarks>
    public string QueryPlanIdentityDigest { get; }

    /// <summary>
    /// The only path that mints a plan.
    /// </summary>
    /// <param name="endpoint">Must be <see cref="OfficialCellarSparqlEndpoint"/>.</param>
    /// <param name="predicateIri">Must be <see cref="WatermarkPredicateIri"/>.</param>
    /// <param name="pageLimit">Rows per page, inside the sorted-result window.</param>
    /// <param name="startPosition">The tie-safe position the previous cut ended at.</param>
    /// <param name="refusal">Why no plan exists, when none does.</param>
    public static EuWatermarkWitnessPlan? TryFreeze(
        string endpoint,
        string predicateIri,
        int pageLimit,
        EuWatermarkCursor startPosition,
        out EuWatermarkPlanRefusal refusal)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(predicateIri);
        ArgumentNullException.ThrowIfNull(startPosition);

        if (!string.Equals(endpoint, OfficialCellarSparqlEndpoint, StringComparison.Ordinal))
        {
            refusal = EuWatermarkPlanRefusal.EndpointNotTheOfficialCellarEndpoint;
            return null;
        }

        if (!string.Equals(predicateIri, WatermarkPredicateIri, StringComparison.Ordinal))
        {
            refusal = EuWatermarkPlanRefusal.PredicateNotTheWatermarkPredicate;
            return null;
        }

        if (pageLimit < MinimumPageLimit)
        {
            refusal = EuWatermarkPlanRefusal.PageLimitBelowMinimum;
            return null;
        }

        if (pageLimit > SortedResultWindowRows)
        {
            refusal = EuWatermarkPlanRefusal.PageLimitAboveSortedResultWindow;
            return null;
        }

        if (!TryReadLexicalProfile(startPosition.WatermarkLexical, out var digits, out var offset))
        {
            refusal = EuWatermarkPlanRefusal.StartPositionNotLexicallyOrderable;
            return null;
        }

        var template = BuildTemplate(predicateIri, pageLimit);
        refusal = EuWatermarkPlanRefusal.None;
        return new EuWatermarkWitnessPlan(
            endpoint,
            predicateIri,
            pageLimit,
            startPosition,
            digits,
            offset,
            template,
            QueryPlanDigest(endpoint, predicateIri, pageLimit, template));
    }

    /// <summary>
    /// The exact SPARQL for the page that reads from <paramref name="position"/> inclusively.
    /// </summary>
    /// <remarks>
    /// The first page of a cut is this method applied to <see cref="StartPosition"/>. There is no
    /// separate unbounded first page, because an unbounded one would not be a change witness.
    /// </remarks>
    /// <param name="position">Where to read from, inclusive of its whole tie group.</param>
    /// <param name="refusal">Why no query text exists, when none does.</param>
    public string? RenderPage(EuWatermarkCursor position, out EuWatermarkPlanRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (!SharesLexicalProfile(position.WatermarkLexical))
        {
            refusal = EuWatermarkPlanRefusal.PositionNotInPlanLexicalProfile;
            return null;
        }

        refusal = EuWatermarkPlanRefusal.None;
        return _template.Replace(
            LowerBoundarySlot,
            SparqlQueryText.StringLiteral(position.WatermarkLexical),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a watermark shares this plan's frozen lexical profile, which is the condition under
    /// which ordinal comparison of the lexical form agrees with the order of the instants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two concrete disagreements, which is why this is a check rather than a comment. A changed
    /// offset: <c>2026-10-25T02:30:00.000+02:00</c> is the earlier instant than
    /// <c>2026-10-25T02:00:00.000+01:00</c> but the later string. A changed precision, with
    /// <c>Z</c> as the offset: <c>...:17.6Z</c> is the earlier instant than <c>...:17.61Z</c> but
    /// the later string, because <c>Z</c> sorts above the digits.
    /// </para>
    /// <para>
    /// The profile is read from the retained bytes and the bytes are never reformatted, because a
    /// round trip through a date type normalises precision the endpoint did not.
    /// </para>
    /// </remarks>
    public bool SharesLexicalProfile(string watermarkLexical)
    {
        ArgumentNullException.ThrowIfNull(watermarkLexical);
        return TryReadLexicalProfile(watermarkLexical, out var digits, out var offset)
            && digits == WatermarkFractionalDigits
            && string.Equals(offset, WatermarkOffsetToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the ordering-relevant shape of an <c>xsd:dateTime</c> lexical form: how many
    /// fractional digits it carries and which offset token it ends with.
    /// </summary>
    /// <remarks>
    /// The fixed-width date and time fields are required, not merely conventional. A variable width
    /// year or an unpadded month puts the lexical order out of step with the chronological one just
    /// as surely as a changed offset does, and xsd:dateTime permits both.
    /// </remarks>
    private static bool TryReadLexicalProfile(
        string lexical,
        out int fractionalDigits,
        out string offsetToken)
    {
        fractionalDigits = 0;
        offsetToken = string.Empty;

        // yyyy-MM-ddTHH:mm:ss is 19 characters, and an offset token is at least one more.
        if (lexical.Length < 20)
        {
            return false;
        }

        ReadOnlySpan<int> digitPositions = [0, 1, 2, 3, 5, 6, 8, 9, 11, 12, 14, 15, 17, 18];
        foreach (var position in digitPositions)
        {
            if (!char.IsAsciiDigit(lexical[position]))
            {
                return false;
            }
        }

        if (lexical[4] != '-' || lexical[7] != '-' || lexical[10] != 'T' ||
            lexical[13] != ':' || lexical[16] != ':')
        {
            return false;
        }

        var index = 19;
        if (lexical[index] == '.')
        {
            index++;
            var start = index;
            while (index < lexical.Length && char.IsAsciiDigit(lexical[index]))
            {
                index++;
            }

            fractionalDigits = index - start;
            if (fractionalDigits == 0)
            {
                return false;
            }
        }

        var offset = lexical[index..];
        if (string.Equals(offset, "Z", StringComparison.Ordinal))
        {
            offsetToken = offset;
            return true;
        }

        // An absent offset is a valid xsd:dateTime and an unusable watermark: it names no instant,
        // and mixing it with offset-bearing values breaks the lexical order too, because the
        // shorter string is a prefix of the longer one and sorts below every offset token.
        if (offset.Length != 6 || (offset[0] != '+' && offset[0] != '-') || offset[3] != ':' ||
            !char.IsAsciiDigit(offset[1]) || !char.IsAsciiDigit(offset[2]) ||
            !char.IsAsciiDigit(offset[4]) || !char.IsAsciiDigit(offset[5]))
        {
            return false;
        }

        offsetToken = offset;
        return true;
    }

    /// <summary>
    /// The unbound query. Keyset only: there is no OFFSET here, and R3.2 requires keyset paging
    /// rather than a retry when the sorted window is exceeded, so the plan must be unable to
    /// express the failing shape at all.
    /// </summary>
    /// <remarks>
    /// The IRI filter is a narrowing with a stated cost. <c>STR()</c> of a blank node is a
    /// scope-local label rather than a stable identity, so such a subject cannot supply a canonical
    /// entry key and would break the traversal rather than extend it. No observation says whether
    /// any Cellar subject carrying this predicate is a blank node; if one is, this witness does not
    /// cover it and does not claim to.
    /// </remarks>
    private static string BuildTemplate(string predicateIri, int pageLimit) =>
        string.Join('\n',
        [
            "SELECT ?entry ?entry_key ?watermark WHERE {",
            "  ?entry <" + predicateIri + "> ?watermark_value .",
            "  FILTER(isIRI(?entry))",
            "  BIND(STR(?entry) AS ?entry_key)",
            "  BIND(STR(?watermark_value) AS ?watermark)",
            "  FILTER(?watermark >= " + LowerBoundarySlot + ")",
            "}",
            "ORDER BY ?watermark ?entry_key",
            "LIMIT " + pageLimit.ToString(CultureInfo.InvariantCulture),
            string.Empty,
        ]);

    private static string QueryPlanDigest(
        string endpoint,
        string predicateIri,
        int pageLimit,
        string template)
    {
        var identity = StrictUtf8.GetBytes(string.Join('\n',
        [
            SchemaId,
            "endpoint=" + endpoint,
            "predicate=" + predicateIri,
            "ordering_tuple=" + OrderingTupleIdentity,
            "boundary_rule=" + BoundaryRuleIdentity,
            "page_limit=" + pageLimit.ToString(CultureInfo.InvariantCulture),
            "template=" + template,
        ]));
        return Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
    }
}

/// <summary>
/// Why one page of the witness traversal did not produce a step. Closed.
/// </summary>
public enum EuWatermarkStepRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The crossing's own position is missing from the tie set it retained. Such a crossing
    /// describes a boundary its cursor does not belong to, and every later comparison against it
    /// would be against the wrong tie group.
    /// </summary>
    [JsonStringEnumMemberName("crossing_cursor_not_in_retained_tie_set")]
    CrossingCursorNotInRetainedTieSet = 1,

    /// <summary>
    /// The page carries more rows than the plan asked for, so the endpoint did not honour the
    /// LIMIT and no page-shaped reasoning about it holds.
    /// </summary>
    [JsonStringEnumMemberName("page_exceeds_plan_limit")]
    PageExceedsPlanLimit = 2,

    /// <summary>
    /// A watermark on this page, or on the crossing's own cursor, does not share the plan's frozen
    /// lexical profile. The publisher's order and the cursor's order are no longer one relation,
    /// so neither the boundary nor the advance can be reasoned about.
    /// </summary>
    [JsonStringEnumMemberName("watermark_not_in_plan_lexical_profile")]
    WatermarkNotInPlanLexicalProfile = 3,

    /// <summary>
    /// The page is not strictly ascending in the ordering tuple. Either the endpoint did not apply
    /// the plan's ORDER BY, or it delivered one entry twice.
    /// </summary>
    [JsonStringEnumMemberName("page_not_strictly_ascending")]
    PageNotStrictlyAscending = 4,

    /// <summary>
    /// The page begins below the boundary watermark it was requested from, which the inclusive
    /// filter should have excluded.
    /// </summary>
    [JsonStringEnumMemberName("page_below_boundary_watermark")]
    PageBelowBoundaryWatermark = 5,

    /// <summary>
    /// The crossing was not computed from this page: the rows this page carries at the boundary
    /// watermark are not the reread the crossing reconciled. A crossing built from a neighbouring
    /// page agrees about the cursor and about the tie set, which is exactly what makes attaching
    /// the wrong one silent.
    /// </summary>
    [JsonStringEnumMemberName("crossing_does_not_describe_this_page")]
    CrossingDoesNotDescribeThisPage = 6,

    /// <summary>
    /// A full page carrying nothing above the boundary watermark. The tie group at that watermark
    /// is at least as large as the page, so the inclusive reread returns the same rows for ever
    /// and the traversal is stalled rather than finished.
    /// </summary>
    [JsonStringEnumMemberName("traversal_cannot_advance")]
    TraversalCannotAdvance = 7,
}

/// <summary>
/// One delivered page of the witness traversal, checked against the plan and against the boundary
/// crossing that was reconciled from it.
/// </summary>
/// <remarks>
/// <para>
/// The division of labour is deliberate. <see cref="EuBoundaryCrossing"/> proves that the entries
/// sharing the boundary watermark were carried across it exactly once. This type proves the two
/// things a crossing cannot see: that the crossing was computed from this page rather than from a
/// neighbouring one, and that the page actually moved the traversal forward. The second matters
/// because the inclusive reread creates its own stall. The tie groups observed on 2026-09-03 held
/// three to five entries each, so a page limit smaller than a tie group re-delivers the same rows
/// for ever while every individual page still looks well formed.
/// </para>
/// <para>
/// What it does not decide. A page carrying nothing beyond the boundary and fewer rows than the
/// limit is consistent with the end of the traversal, and this type reports that by leaving
/// <see cref="NextPosition"/> null. It is not proof of termination: confirming a short page needs
/// an empty successor request, which is the executor's business and not a property of one page.
/// </para>
/// </remarks>
public sealed class EuWatermarkTraversalStep
{
    private EuWatermarkTraversalStep(
        EuWatermarkWitnessPlan plan,
        EuBoundaryCrossing crossing,
        IReadOnlyList<EuWatermarkCursor> deliveredPage,
        EuWatermarkCursor? nextPosition,
        int rowsBeyondBoundary)
    {
        Plan = plan;
        Crossing = crossing;
        DeliveredPage = deliveredPage;
        NextPosition = nextPosition;
        RowsBeyondBoundary = rowsBeyondBoundary;
    }

    /// <summary>The plan this page was rendered from.</summary>
    public EuWatermarkWitnessPlan Plan { get; }

    /// <summary>The reconciled boundary crossing this page carried.</summary>
    public EuBoundaryCrossing Crossing { get; }

    /// <summary>The page as delivered, in publisher order.</summary>
    public IReadOnlyList<EuWatermarkCursor> DeliveredPage { get; }

    /// <summary>
    /// Where the next page reads from, or null when this page carried nothing beyond the boundary
    /// and is therefore consistent with the end of the traversal.
    /// </summary>
    public EuWatermarkCursor? NextPosition { get; }

    /// <summary>How many rows this page carried above the boundary watermark.</summary>
    public int RowsBeyondBoundary { get; }

    /// <summary>
    /// The only path that mints a step.
    /// </summary>
    /// <param name="plan">The frozen plan, for its page limit and lexical profile.</param>
    /// <param name="crossing">The boundary crossing reconciled from this page.</param>
    /// <param name="deliveredPage">The page as delivered, in publisher order.</param>
    /// <param name="refusal">Why no step exists, when none does.</param>
    public static EuWatermarkTraversalStep? TryAdvance(
        EuWatermarkWitnessPlan plan,
        EuBoundaryCrossing crossing,
        IReadOnlyList<EuWatermarkCursor> deliveredPage,
        out EuWatermarkStepRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(crossing);
        ArgumentNullException.ThrowIfNull(deliveredPage);

        // Materialised once, then checked and kept. IReadOnlyList is a view rather than a
        // guarantee, so a caller could otherwise mutate the list after the checks passed.
        var page = deliveredPage.ToArray();
        if (Array.Exists(page, static row => row is null))
        {
            throw new ArgumentException("A delivered row cannot be null.", nameof(deliveredPage));
        }

        var boundaryWatermark = crossing.Cursor.WatermarkLexical;

        // The crossing must be about a boundary its own cursor belongs to. TryCross reconciles the
        // retained tie set against the reread without requiring the cursor to be a member, so this
        // is the gap that would otherwise let an empty tie set reconcile against an empty reread
        // and then read here as a clean terminal page.
        if (!crossing.RetainedTieSet.Contains(
                crossing.Cursor.CanonicalEntryKey, StringComparer.Ordinal))
        {
            refusal = EuWatermarkStepRefusal.CrossingCursorNotInRetainedTieSet;
            return null;
        }

        if (page.Length > plan.PageLimit)
        {
            refusal = EuWatermarkStepRefusal.PageExceedsPlanLimit;
            return null;
        }

        if (!plan.SharesLexicalProfile(boundaryWatermark) ||
            Array.Exists(page, row => !plan.SharesLexicalProfile(row.WatermarkLexical)))
        {
            refusal = EuWatermarkStepRefusal.WatermarkNotInPlanLexicalProfile;
            return null;
        }

        for (var index = 1; index < page.Length; index++)
        {
            if (page[index].CompareTo(page[index - 1]) <= 0)
            {
                refusal = EuWatermarkStepRefusal.PageNotStrictlyAscending;
                return null;
            }
        }

        // Only the first row needs checking: the page ascends, so nothing after it can be lower.
        // This also proves for this page what EuBoundaryCrossing checks against a caller-supplied
        // first-beyond value, which the crossing does not retain and this type therefore cannot
        // compare against.
        if (page.Length > 0 &&
            string.CompareOrdinal(page[0].WatermarkLexical, boundaryWatermark) < 0)
        {
            refusal = EuWatermarkStepRefusal.PageBelowBoundaryWatermark;
            return null;
        }

        var atBoundary = page
            .Where(row => string.Equals(
                row.WatermarkLexical, boundaryWatermark, StringComparison.Ordinal))
            .Select(static row => row.CanonicalEntryKey)
            .ToHashSet(StringComparer.Ordinal);

        // TryCross already proved the retained set is a subset of the reread, so the reread it
        // reconciled is exactly the retained set together with what it carried forward.
        var reread = new HashSet<string>(crossing.RetainedTieSet, StringComparer.Ordinal);
        reread.UnionWith(crossing.CarriedForward);
        if (!atBoundary.SetEquals(reread))
        {
            refusal = EuWatermarkStepRefusal.CrossingDoesNotDescribeThisPage;
            return null;
        }

        var beyond = page.Length - atBoundary.Count;
        if (beyond == 0 && page.Length == plan.PageLimit)
        {
            refusal = EuWatermarkStepRefusal.TraversalCannotAdvance;
            return null;
        }

        refusal = EuWatermarkStepRefusal.None;
        return new EuWatermarkTraversalStep(
            plan,
            crossing,
            Array.AsReadOnly(page),
            beyond > 0 ? page[^1] : null,
            beyond);
    }
}
