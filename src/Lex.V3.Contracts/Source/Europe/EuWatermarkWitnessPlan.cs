using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The lexical shapes of <c>cmr:lastModificationDate</c> whose order semantics a bounded
/// observation has frozen, plus the one member for everything else. Closed.
/// </summary>
/// <remarks>
/// <para>
/// This vocabulary is an evidence boundary, not an ordering repair. Ordinal comparison of strings
/// is a total order, and it is the same relation the endpoint applies under <c>ORDER BY STR</c>
/// whatever shapes the values take, so the traversal and the cursor stay consistent across every
/// shape with no classification at all. What the classification decides is narrower: whether a
/// bounded observation has established order semantics for the shape in front of us, which is what
/// R3 requires of a witness before it may be used.
/// </para>
/// <para>
/// The measurement of 2026-09-03 found both admitted members at scale in this predicate:
/// 35,918,112 values with fractional seconds and a <c>+02:00</c> offset, 28,357,736 with
/// fractional seconds and <c>+01:00</c>, and 61,169 with no fractional seconds at all, the most
/// recent of those from that same day. Sign and offset minutes do not change the ordering
/// relation, so the admitted members are stated by shape rather than by the two offset tokens that
/// happened to be observed.
/// </para>
/// </remarks>
public enum EuWatermarkLexicalShape
{
    /// <summary>
    /// A shape no bounded observation covers: an absent offset, a <c>Z</c> terminator, a variable
    /// width field, or anything else. Concretely, <c>Z</c> is 0x5A and sorts above both the
    /// fraction's <c>.</c> at 0x2E and the offset's <c>+</c> at 0x2B, so such a value would land in
    /// a position nothing has observed. That is the illustration; the reason to refuse is that
    /// nothing has been observed.
    /// </summary>
    [JsonStringEnumMemberName("outside_the_measured_set")]
    OutsideTheMeasuredSet = 0,

    /// <summary>
    /// <c>yyyy-MM-ddTHH:mm:ss.f{1,n}</c> followed by a signed <c>hh:mm</c> offset. The bulk of the
    /// predicate, under both observed offsets.
    /// </summary>
    [JsonStringEnumMemberName("fractional_seconds_signed_offset")]
    FractionalSecondsSignedOffset = 1,

    /// <summary>
    /// <c>yyyy-MM-ddTHH:mm:ss</c> followed by a signed <c>hh:mm</c> offset, with no fractional
    /// part. Still being emitted, mixed in with the fractional values.
    /// </summary>
    [JsonStringEnumMemberName("whole_seconds_signed_offset")]
    WholeSecondsSignedOffset = 2,
}

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
    /// A page limit below <see cref="EuWatermarkWitnessPlan.MinimumPageLimit"/>. Every page begins
    /// by re-delivering the boundary position itself, so a one row page is spent entirely on a row
    /// already delivered and can never carry a successor.
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
    /// The start position's watermark has a shape no bounded observation covers, so the channel has
    /// supplied no frozen order semantics for it and R3 does not let the witness proceed on it.
    /// </summary>
    [JsonStringEnumMemberName("start_position_shape_without_frozen_order_semantics")]
    StartPositionShapeWithoutFrozenOrderSemantics = 5,

    /// <summary>
    /// The position offered to the renderer has a shape no bounded observation covers. Same cause
    /// as the start position, reached from the other entry point.
    /// </summary>
    [JsonStringEnumMemberName("position_shape_without_frozen_order_semantics")]
    PositionShapeWithoutFrozenOrderSemantics = 6,

    /// <summary>
    /// The plan was frozen over no objects. A witness restricted to an empty pack observes nothing
    /// and would report that as "no change", which is the false zero this whole design exists to
    /// avoid. Refused rather than frozen.
    /// </summary>
    [JsonStringEnumMemberName("batch_names_no_objects")]
    BatchNamesNoObjects = 7,

    /// <summary>
    /// The batch names more objects than <see cref="EuWatermarkWitnessPlan.BatchCapacity"/> admits.
    /// The caller batches; this plan does not silently truncate, because a truncated batch would
    /// observe part of the pack while the record said it observed the pack.
    /// </summary>
    [JsonStringEnumMemberName("batch_above_capacity")]
    BatchAboveCapacity = 8,

    /// <summary>
    /// A batch member does not reduce to Appendix A's exact lexical form, or two members reduce to
    /// the same one. Either way the VALUES block would not name what the caller believes it names.
    /// </summary>
    [JsonStringEnumMemberName("batch_member_not_canonical_or_duplicated")]
    BatchMemberNotCanonicalOrDuplicated = 9,
}

/// <summary>
/// The frozen query plan half of <c>cellar_last_modification_witness/1</c>: the exact endpoint,
/// predicate, ordering tuple, boundary rule, admitted watermark shapes and page limit of the
/// bounded SPARQL traversal that R3 permits as an EU positive-change witness when no official feed
/// URI is selected.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EuWatermarkCursor"/> is the cursor half and is used here rather than re-derived: the
/// ordering tuple is that type's comparison, and a page can only be rendered from a cursor, so a
/// date-only continuation cannot be expressed at all.
/// </para>
/// <para>
/// The traversal orders lexically, not chronologically, and that is deliberate. The cursor compares
/// the publisher's retained bytes ordinally, so the query binds <c>STR()</c> of the watermark and
/// orders that. The endpoint accepts <c>ORDER BY STR</c> and reports the result ordered, measured
/// on 2026-09-03. Ordinal comparison of strings is a total order, so the publisher's order and the
/// cursor's order are one relation whatever lexical shapes the values take, and the traversal
/// cannot skip or re-deliver a row through a disagreement between them.
/// </para>
/// <para>
/// The cost of that choice is real and larger than it first looked. 28,357,736 values in this
/// predicate carry <c>+01:00</c> against 35,918,112 carrying <c>+02:00</c>, so the two offsets are
/// 44 and 56 percent of the corpus rather than one boundary crossing a year. Across a transition
/// the two orders genuinely diverge: <c>2026-03-29T01:52:39.176+01:00</c> and the same local text
/// under <c>+02:00</c> sort in that order lexically while the second names the instant an hour
/// earlier. So the lower boundary of this witness is a position in a string order, never a moment.
/// Nothing here may be worded as what changed since an instant, and nothing on this contract can
/// express one.
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
    /// Below this no page can advance. Every page re-reads the boundary position itself, so a one
    /// row page is spent on a row already delivered. This is a property of the boundary rule and
    /// holds for every corpus; it is not a claim that two rows are enough.
    /// </summary>
    public const int MinimumPageLimit = 2;

    /// <summary>
    /// The sorted-result window from R3.2. This plan emits no OFFSET, so the recorded
    /// <c>OFFSET + LIMIT &gt; 10000</c> constraint binds the limit with OFFSET at zero.
    /// </summary>
    public const int SortedResultWindowRows = 10_000;

    /// <summary>The schema this plan is an instance of.</summary>
    /// <summary>
    /// The schema this plan is an instance of. Bumped to /2 when the plan gained its VALUES
    /// restriction: a /1 digest and a /2 digest describe queries that observe different sets, and a
    /// reader must not be able to mistake one for the other.
    /// </summary>
    public const string SchemaId = "cellar_last_modification_witness/2";

    /// <summary>
    /// How many objects one frozen witness plan restricts to, TAKEN FROM FAMILY P'S OWN SYMBOL
    /// rather than restated here as a number.
    /// </summary>
    /// <remarks>
    /// Deliberately a reference and never a literal. If D1-05g moves
    /// <see cref="EuObjectFactsDiscoveryPlan.BatchCapacity"/>, this plan follows it and the
    /// <c>batch_capacity</c> line in the digest picks up whatever it becomes; a literal here would
    /// turn that rebase into a rework, and would let the two families disagree about how many
    /// objects a batch holds while both believed they agreed.
    /// </remarks>
    public static int BatchCapacity => EuObjectFactsDiscoveryPlan.BatchCapacity;

    /// <summary>
    /// The ordering tuple, named for the receipt. It is <see cref="EuWatermarkCursor"/>'s
    /// comparison and nothing else.
    /// </summary>
    public const string OrderingTupleIdentity = "watermark_lexical,canonical_entry_key";

    /// <summary>
    /// The boundary rule, named for the receipt. Each page reads from the boundary watermark
    /// inclusively, so the whole group sharing that exact lexical value is seen again and
    /// <see cref="EuBoundaryCrossing"/> can say afterwards that none of it was skipped or delivered
    /// twice.
    /// </summary>
    public const string BoundaryRuleIdentity = "inclusive_watermark_reread/1";

    private const string LowerBoundarySlot = "{watermark_lower_boundary}";

    /// <summary>
    /// The one wire media type this witness's own SPARQL JSON page response is ever admitted under,
    /// identical to every other EU family (<see cref="EuObjectFactsDiscoveryPlan"/>'s own
    /// <c>ResponseMediaType</c>, private there because that plan never needed to expose it outside
    /// its own binder; this one does, since <c>TryBindPage</c>'s real send lives in
    /// <c>Lex.V3.Ingest.Europe</c>, outside this path claim's normal reach.
    /// </summary>
    public const string ResponseMediaType = "application/sparql-results+json";

    /// <summary>
    /// Fixed resource-id half of <see cref="ArtifactRef"/>, minted the same way every other Europe
    /// discovery plan's own resource id already is (see <c>EuConsolidationDiscoveryPlan</c>'s and
    /// <c>EuObjectFactsDiscoveryPlan</c>'s own private <c>ResourceId</c> constants): a fixed literal
    /// naming this plan's own structural domain, never a claim about an observation nobody has taken.
    /// </summary>
    private const string WitnessPlanResourceId = "urn:uuid:3b6e1a4c-8f2d-4c7b-9a1e-5d6f7c8b9a0e";

    private const string WitnessPageFamilyMemberKey = "witness.page";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _template;
    private readonly byte[] _canonicalIdentityBytes;
    private readonly SourceRegistryMemberRef _witnessPageFamilyRef;

    private EuWatermarkWitnessPlan(
        string endpoint,
        string predicateIri,
        int pageLimit,
        EuWatermarkCursor startPosition,
        EuWatermarkLexicalShape startPositionShape,
        string template,
        string queryPlanIdentityDigest,
        byte[] canonicalIdentityBytes,
        string[] paddedEntries,
        string batchDigest)
    {
        _paddedEntries = paddedEntries;
        BatchDigest = batchDigest;
        Endpoint = endpoint;
        PredicateIri = predicateIri;
        PageLimit = pageLimit;
        StartPosition = startPosition;
        StartPositionShape = startPositionShape;
        _template = template;
        QueryPlanIdentityDigest = queryPlanIdentityDigest;
        _canonicalIdentityBytes = canonicalIdentityBytes;
        ArtifactRef = new SourceArtifactRef(WitnessPlanResourceId, queryPlanIdentityDigest);
        _witnessPageFamilyRef = new SourceRegistryMemberRef(ArtifactRef, WitnessPageFamilyMemberKey);
    }

    private readonly string[] _paddedEntries;

    /// <summary>
    /// The digest of this plan's own canonical batch. NOT part of
    /// <see cref="QueryPlanIdentityDigest"/>, which covers the query's shape; this covers which
    /// objects the shape was pointed at, and the two answer different questions.
    /// </summary>
    public string BatchDigest { get; }

    /// <summary>The canonical objects this plan observes, padded to <see cref="BatchCapacity"/>.</summary>
    public IReadOnlyList<string> PaddedEntries => Array.AsReadOnly(_paddedEntries);

    /// <summary>
    /// This plan's own content-addressed identity: <see cref="WitnessPlanResourceId"/> paired with
    /// <see cref="QueryPlanIdentityDigest"/>. Added for <see cref="TryBindPage"/> (SCOPE_RULING
    /// lex-event-20260904T092316893Z-6d969a2ba7934aa995907a55914bf3b6): a bound
    /// <see cref="Lex.V3.Contracts.Source.Core.MachineQueryPlan"/> needs a renderer-profile reference
    /// and a query-family registry member the same way every other Europe discovery plan already
    /// supplies one from its own <c>ArtifactRef</c>, and this type had none before that ruling.
    /// </summary>
    public SourceArtifactRef ArtifactRef { get; }

    /// <summary>
    /// The watermark shapes this plan admits, in ascending member order. A shape outside this set
    /// has no frozen order semantics and stops the witness with a named cause.
    /// </summary>
    public static IReadOnlyList<EuWatermarkLexicalShape> AdmittedShapes { get; } =
        Array.AsReadOnly(new[]
        {
            EuWatermarkLexicalShape.FractionalSecondsSignedOffset,
            EuWatermarkLexicalShape.WholeSecondsSignedOffset,
        });

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
    /// Which admitted shape the start position carries. R3 asks the witness to bind the watermark's
    /// precision, and this is that field: a name from a closed vocabulary rather than a digit
    /// count, because the corpus carries more than one precision at once.
    /// </summary>
    public EuWatermarkLexicalShape StartPositionShape { get; }

    /// <summary>
    /// SHA-256 over the plan's query identity: endpoint, predicate, ordering tuple, boundary rule,
    /// admitted shapes, page limit and the unbound query template.
    /// </summary>
    /// <remarks>
    /// Deliberately not a function of <see cref="StartPosition"/>. The query plan is the thing that
    /// stays fixed while cuts advance; folding this cut's boundary into it would produce a new
    /// query-plan identity every run and make the digest useless for saying that two cuts used the
    /// same plan. The admitted shape set is bound, because widening it is a different frozen
    /// order-semantics claim and must be a different plan.
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
        IReadOnlyList<string> batchObjects,
        out EuWatermarkPlanRefusal refusal)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(predicateIri);
        ArgumentNullException.ThrowIfNull(startPosition);
        ArgumentNullException.ThrowIfNull(batchObjects);

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

        var shape = ClassifyShape(startPosition.WatermarkLexical);
        if (shape == EuWatermarkLexicalShape.OutsideTheMeasuredSet)
        {
            refusal = EuWatermarkPlanRefusal.StartPositionShapeWithoutFrozenOrderSemantics;
            return null;
        }

        var padded = TryCanonicalizeAndPad(batchObjects, out refusal);
        if (padded is null)
        {
            return null;
        }

        var template = BuildTemplate(predicateIri, pageLimit);
        var identityBytes = BuildQueryPlanIdentityBytes(endpoint, predicateIri, pageLimit, template);
        var digest = Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant();
        var batchDigest = Sha256(StrictUtf8.GetBytes(string.Join('\n', padded)));
        refusal = EuWatermarkPlanRefusal.None;
        return new EuWatermarkWitnessPlan(
            endpoint,
            predicateIri,
            pageLimit,
            startPosition,
            shape,
            template,
            digest,
            identityBytes,
            padded,
            batchDigest);
    }

    /// <summary>
    /// The exact SPARQL for the page that reads from <paramref name="position"/> inclusively.
    /// </summary>
    /// <remarks>
    /// The first page of a cut is this method applied to <see cref="StartPosition"/>. There is no
    /// separate unbounded first page, because an unbounded one would not be a change witness, and
    /// no separate first-page rule: the previous cut ended at that position and retained the group
    /// sharing its watermark, so the first page of a cut is an ordinary boundary crossing.
    /// </remarks>
    /// <param name="position">Where to read from, inclusive of its whole tie group.</param>
    /// <param name="refusal">Why no query text exists, when none does.</param>
    public string? RenderPage(EuWatermarkCursor position, out EuWatermarkPlanRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(position);
        return RenderForWatermarkLexical(position.WatermarkLexical, out refusal);
    }

    /// <summary>
    /// The substitution core of <see cref="RenderPage"/>, factored out so
    /// <see cref="EuWatermarkWitnessSparqlRenderer"/> can reach the identical query text from a
    /// watermark lexical value it decoded out of a bound <see cref="MachineQueryInputArtifact"/>'s
    /// own parameter, rather than from a caller's in-memory <see cref="EuWatermarkCursor"/> the
    /// renderer cannot have (see <see cref="TryBindPage"/>'s own remarks). <see cref="RenderPage"/>'s
    /// external behavior is unchanged: same checks, same output, for the same input.
    /// </summary>
    internal string? RenderForWatermarkLexical(string watermarkLexical, out EuWatermarkPlanRefusal refusal) =>
        RenderForWatermarkLexical(watermarkLexical, _paddedEntries, out refusal);

    /// <summary>
    /// The substitution core, taking the entry values explicitly so the renderer can supply the ones
    /// it decoded out of the bound input's own parameters rather than trusting the plan instance it
    /// happens to hold. Same checks, same output, for the same input.
    /// </summary>
    internal string? RenderForWatermarkLexical(
        string watermarkLexical, IReadOnlyList<string> entries, out EuWatermarkPlanRefusal refusal)
    {
        if (ClassifyShape(watermarkLexical) == EuWatermarkLexicalShape.OutsideTheMeasuredSet)
        {
            refusal = EuWatermarkPlanRefusal.PositionShapeWithoutFrozenOrderSemantics;
            return null;
        }

        if (entries.Count != BatchCapacity)
        {
            refusal = EuWatermarkPlanRefusal.BatchAboveCapacity;
            return null;
        }

        var slotNames = EntryParameterNames();
        var withEntries = _template;
        for (var index = 0; index < slotNames.Count; index++)
        {
            withEntries = withEntries.Replace(
                "{" + slotNames[index] + "}",
                "<" + entries[index] + ">",
                StringComparison.Ordinal);
        }

        refusal = EuWatermarkPlanRefusal.None;
        return withEntries.Replace(
            LowerBoundarySlot,
            SparqlQueryText.StringLiteral(watermarkLexical),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Binds one witness page request into a real, sendable <see cref="BoundMachineRequest"/> through
    /// this codebase's existing <see cref="MachineQueryBinder"/>/<see cref="IMachineQueryRenderer"/>
    /// send machinery -- the exact same door <c>EuConsolidationDiscoveryPlan.BindPage</c> and
    /// <c>EuObjectFactsDiscoveryPlan.BindPage</c> already use for the census and object-facts
    /// families.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SCOPE_RULING lex-event-20260904T092316893Z-6d969a2ba7934aa995907a55914bf3b6: this is the one
    /// public bind door this file was widened to add, authorized because defect 3's own fix (actually
    /// sending the frozen witness plan rather than assuming an empty result) needs the plan to become
    /// a real HTTP request, and nothing outside <c>Lex.V3.Contracts</c> can construct a
    /// <see cref="Lex.V3.Contracts.Source.Core.MachineQueryPlan"/>/<see cref="BoundMachineRequest"/>
    /// pair by hand -- every other Europe discovery plan already mints its own bound queries from
    /// inside this same assembly, and this witness plan had no such door before this ruling.
    /// </para>
    /// <para>
    /// The response cardinality is deliberately <c>OpaqueBody</c>, never
    /// <c>BoundedRowSetPage</c>: a bounded row-set page requires an independently measured expected
    /// partition row count and its own evidence reference (<see cref="MachineResponseCardinality"/>'s
    /// own constructor), which is exactly the pre-count/post-count shape this plan's own type remarks
    /// say a witness does not have. Claiming one here would be inventing an observation nobody took,
    /// the same discipline <see cref="EuFeedEntryObservation"/>'s own remarks apply to identity
    /// resolution. The page's own row count and boundary crossing are instead checked client-side by
    /// <see cref="EuWatermarkTraversalStep.TryAdvance"/>, exactly as they already were before this
    /// binding existed.
    /// </para>
    /// <para>
    /// Only one parameter is bound: the boundary position's own watermark lexical value, carried as a
    /// <see cref="MachineQueryParameterKind.PublisherCursor"/> (the boundary is a paging position, not
    /// a publisher-literal IRI). <see cref="EuWatermarkCursor.CanonicalEntryKey"/> is not bound at
    /// all, because <see cref="RenderForWatermarkLexical"/> -- and so <see cref="RenderPage"/> before
    /// it -- never reads it; the entry key exists for this plan's own client-side tie-safety
    /// reasoning, not for the query text.
    /// </para>
    /// </remarks>
    /// <param name="position">Where to read from, inclusive of its whole tie group.</param>
    /// <param name="machinePlanResourceId">A fresh resource id for the minted machine-query plan.</param>
    /// <param name="inputResourceId">A fresh resource id for the minted ordered-parameter input.</param>
    /// <param name="rendererSource">
    /// The renderer-source artifact naming this file's own <see cref="EuWatermarkWitnessSparqlRenderer"/>
    /// code, held with its bytes exactly as every other Europe bind already requires.
    /// </param>
    /// <param name="refusal">Why no bound query exists, when none does.</param>
    public EuWatermarkWitnessBoundQuery? TryBindPage(
        EuWatermarkCursor position,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource,
        out EuWatermarkPlanRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentException.ThrowIfNullOrEmpty(machinePlanResourceId);
        ArgumentException.ThrowIfNullOrEmpty(inputResourceId);
        ArgumentNullException.ThrowIfNull(rendererSource);

        if (ClassifyShape(position.WatermarkLexical) == EuWatermarkLexicalShape.OutsideTheMeasuredSet)
        {
            refusal = EuWatermarkPlanRefusal.PositionShapeWithoutFrozenOrderSemantics;
            return null;
        }

        refusal = EuWatermarkPlanRefusal.None;

        var response = new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null);
        var slotNames = EntryParameterNames();
        var parameters = new List<MachineQueryParameter>(slotNames.Count + 1)
        {
            new(
                BoundaryParameterName,
                MachineQueryParameterKind.PublisherCursor,
                null,
                EnumerationCursorEnvelope.Encode(position.WatermarkLexical),
                ArtifactRef),
        };
        for (var index = 0; index < slotNames.Count; index++)
        {
            parameters.Add(new MachineQueryParameter(
                slotNames[index],
                MachineQueryParameterKind.PublisherLiteral,
                null,
                _paddedEntries[index],
                ArtifactRef));
        }
        // The renderer (a separate, top-level class in this same file, mirroring
        // EuObjectFactsSparqlRenderer's own top-level placement) reads this parameter back by name;
        // BoundaryParameterName is internal for exactly that reason.

        var input = MachineQueryInputArtifact.Create(
            inputResourceId, _witnessPageFamilyRef, PartitionKeyFor(position), response, parameters);
        var renderer = new EuWatermarkWitnessSparqlRenderer(this, rendererSource);
        var rendered = renderer.RenderInput(input);
        var body = rendered.CopyRequestBody();
        var targetBytes = Encoding.ASCII.GetBytes("/webapi/rdf/sparql");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            _witnessPageFamilyRef,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Post,
            Endpoint,
            targetBytes.LongLength,
            Sha256(targetBytes),
            response,
            new SourceRegistryMemberRef(ArtifactRef, "application/sparql-query"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));
        var machinePlanRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        var request = MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer);
        return new EuWatermarkWitnessBoundQuery(machinePlan, machinePlanRef, input, request);
    }

    internal const string BoundaryParameterName = "watermark_lower_boundary_param";

    /// <summary>
    /// This plan's own VALUES slot names, one per <see cref="BatchCapacity"/>. The NAMES are the
    /// witness's own even though the CAPACITY is family P's: reusing P's slot names would mean a
    /// rename there silently renamed the parameters this plan binds, which is a wire change nobody
    /// asked for. Only the count is shared, because only the count has to agree.
    /// </summary>
    internal static IReadOnlyList<string> EntryParameterNames()
    {
        var names = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            names[index] = "witness_entry_" + (index + 1).ToString("D2", CultureInfo.InvariantCulture);
        }

        return Array.AsReadOnly(names);
    }

    /// <summary>
    /// Canonicalizes, refuses and sorts a batch exactly as family P's own <c>CanonicalizeBatch</c>
    /// does, then pads to <see cref="BatchCapacity"/> by repeating the greatest member, so the
    /// rendered query has a fixed shape whatever the batch holds.
    /// </summary>
    /// <remarks>
    /// The padding is not decoration. It is what lets the TEMPLATE be fixed and therefore lets the
    /// plan digest cover the query's shape while the batch's CONTENTS travel as parameters. Without
    /// it the digest would move every batch and stop being a plan identity at all.
    /// <para>
    /// THE DUPLICATE-SOLUTION QUESTION, ANSWERED RATHER THAN ASSUMED, because SPARQL defines VALUES
    /// as a multiset and a repeated row would in principle multiply every match. Family P pads the
    /// identical way with no DISTINCT, and its padded query has been observed against this exact
    /// endpoint returning one row set rather than a multiplied one, so this endpoint treats the
    /// block as a set. The witness inherits that proven behaviour by using the same mechanism, and
    /// if it ever changed the traversal's own strictly-ascending page check would refuse loudly
    /// rather than quietly counting a row twice.
    /// </para>
    /// </remarks>
    private static string[]? TryCanonicalizeAndPad(
        IReadOnlyList<string> batchObjects, out EuWatermarkPlanRefusal refusal)
    {
        if (batchObjects.Count == 0)
        {
            refusal = EuWatermarkPlanRefusal.BatchNamesNoObjects;
            return null;
        }

        if (batchObjects.Count > BatchCapacity)
        {
            refusal = EuWatermarkPlanRefusal.BatchAboveCapacity;
            return null;
        }

        var canonical = new string[batchObjects.Count];
        for (var index = 0; index < batchObjects.Count; index++)
        {
            var value = batchObjects[index];
            if (value is null)
            {
                refusal = EuWatermarkPlanRefusal.BatchMemberNotCanonicalOrDuplicated;
                return null;
            }

            var reduced = EuPackRootCanonicalForm.TryCanonicalize(value, out _);
            if (reduced is null)
            {
                refusal = EuWatermarkPlanRefusal.BatchMemberNotCanonicalOrDuplicated;
                return null;
            }

            canonical[index] = reduced;
        }

        if (canonical.Distinct(StringComparer.Ordinal).Count() != canonical.Length)
        {
            refusal = EuWatermarkPlanRefusal.BatchMemberNotCanonicalOrDuplicated;
            return null;
        }

        Array.Sort(canonical, StringComparer.Ordinal);
        var padded = new string[BatchCapacity];
        var last = canonical[^1];
        for (var index = 0; index < BatchCapacity; index++)
        {
            padded[index] = index < canonical.Length ? canonical[index] : last;
        }

        refusal = EuWatermarkPlanRefusal.None;
        return padded;
    }

    /// <summary>The canonical identity bytes <see cref="ArtifactRef"/>'s digest is over, for the renderer's own <c>CopyRendererProfileBytes</c>.</summary>
    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    /// <summary>
    /// This page request's own partition/member key: the SHA-256 of the boundary position's own
    /// tie-safe tuple, truncated to 24 hex characters exactly as
    /// <c>EuObjectFactsDiscoveryPlan.PartitionKeyFor</c> already truncates its own batch digest, for
    /// the identical reason given there (96 bits is far past this key space's own collision risk).
    /// </summary>
    /// <remarks>
    /// THE BATCH DIGEST IS PART OF THE KEY, and leaving it out would have been a real defect rather
    /// than a tidiness point. Two batches traverse from the same start position, so keyed on the
    /// position alone they would collide on one partition, and <see cref="EuBoundaryCrossing"/>
    /// would then prove "no entry skipped and none delivered twice" by comparing one batch's page
    /// against another batch's. A change detector that reconciles the wrong pages reports change it
    /// invented. Found on paper while planning this, not in a run.
    /// </remarks>
    private string PartitionKeyFor(EuWatermarkCursor position) =>
        "eu-watermark-witness-" + Convert.ToHexString(SHA256.HashData(
            StrictUtf8.GetBytes(
                position.WatermarkLexical + "\n" + position.CanonicalEntryKey + "\n" + BatchDigest)))
            .ToLowerInvariant()[..24];

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    /// <summary>
    /// Reads which measured shape a watermark carries, or
    /// <see cref="EuWatermarkLexicalShape.OutsideTheMeasuredSet"/> when it carries none of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixed-width date and time fields are required, not merely conventional: a variable width
    /// year or an unpadded month is a shape nothing has observed, and classifying an unobserved
    /// shape anyway would be exactly the frozen order semantics R3 says the channel has not
    /// supplied.
    /// </para>
    /// <para>
    /// The value is read and never reformatted. A round trip through a date type normalises
    /// precision the endpoint did not, and this predicate demonstrably carries two precisions at
    /// once.
    /// </para>
    /// </remarks>
    public static EuWatermarkLexicalShape ClassifyShape(string watermarkLexical)
    {
        ArgumentNullException.ThrowIfNull(watermarkLexical);

        // yyyy-MM-ddTHH:mm:ss is 19 characters and a signed offset is six more, which is the
        // shortest admitted shape.
        if (watermarkLexical.Length < 25)
        {
            return EuWatermarkLexicalShape.OutsideTheMeasuredSet;
        }

        ReadOnlySpan<int> digitPositions = [0, 1, 2, 3, 5, 6, 8, 9, 11, 12, 14, 15, 17, 18];
        foreach (var position in digitPositions)
        {
            if (!char.IsAsciiDigit(watermarkLexical[position]))
            {
                return EuWatermarkLexicalShape.OutsideTheMeasuredSet;
            }
        }

        if (watermarkLexical[4] != '-' || watermarkLexical[7] != '-' ||
            watermarkLexical[10] != 'T' || watermarkLexical[13] != ':' ||
            watermarkLexical[16] != ':')
        {
            return EuWatermarkLexicalShape.OutsideTheMeasuredSet;
        }

        var index = 19;
        var shape = EuWatermarkLexicalShape.WholeSecondsSignedOffset;
        if (watermarkLexical[index] == '.')
        {
            index++;
            var start = index;
            while (index < watermarkLexical.Length && char.IsAsciiDigit(watermarkLexical[index]))
            {
                index++;
            }

            if (index == start)
            {
                return EuWatermarkLexicalShape.OutsideTheMeasuredSet;
            }

            shape = EuWatermarkLexicalShape.FractionalSecondsSignedOffset;
        }

        // A signed hh:mm offset, which is what both admitted shapes end with. An absent offset and
        // a Z terminator are the two other forms xsd:dateTime allows, and neither was observed.
        var offset = watermarkLexical[index..];
        if (offset.Length != 6 || (offset[0] != '+' && offset[0] != '-') || offset[3] != ':' ||
            !char.IsAsciiDigit(offset[1]) || !char.IsAsciiDigit(offset[2]) ||
            !char.IsAsciiDigit(offset[4]) || !char.IsAsciiDigit(offset[5]))
        {
            return EuWatermarkLexicalShape.OutsideTheMeasuredSet;
        }

        return shape;
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
            "  VALUES ?entry {",
            .. EntryParameterNames().Select(static name => "    {" + name + "}"),
            "  }",
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

    private static byte[] BuildQueryPlanIdentityBytes(
        string endpoint,
        string predicateIri,
        int pageLimit,
        string template) =>
        StrictUtf8.GetBytes(string.Join('\n',
        [
            SchemaId,
            "endpoint=" + endpoint,
            "predicate=" + predicateIri,
            "ordering_tuple=" + OrderingTupleIdentity,
            "boundary_rule=" + BoundaryRuleIdentity,
            "admitted_shapes=" + string.Join(',', AdmittedShapes),
            "page_limit=" + pageLimit.ToString(CultureInfo.InvariantCulture),
            // The CAPACITY is in the identity because two plans differing only in capacity observe
            // differently while hashing identically without it. The batch's CONTENTS are NOT, and
            // must not be: they travel as bound parameters, exactly as family P's do, so one frozen
            // plan identity covers every batch it is pointed at.
            "batch_capacity=" + BatchCapacity.ToString(CultureInfo.InvariantCulture),
            "template=" + template,
        ]));
}

/// <summary>The bound witness page query and every artifact that feeds it, for one page request.</summary>
/// <remarks>
/// Mirrors <c>EuObjectFactsBoundQuery</c> and <c>EuCensusBoundQuery</c> exactly, added by the same
/// SCOPE_RULING as <see cref="EuWatermarkWitnessPlan.TryBindPage"/>
/// (lex-event-20260904T092316893Z-6d969a2ba7934aa995907a55914bf3b6).
/// </remarks>
public sealed record EuWatermarkWitnessBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Renders one witness page's exact SPARQL text from a bound <see cref="MachineQueryInputArtifact"/>,
/// added by the same SCOPE_RULING as <see cref="EuWatermarkWitnessPlan.TryBindPage"/>
/// (lex-event-20260904T092316893Z-6d969a2ba7934aa995907a55914bf3b6). Mirrors
/// <c>EuObjectFactsSparqlRenderer</c>'s own shape: every value the render needs is read back out of
/// <paramref name="orderedParameterSet"/> itself (never from a captured closure value), because
/// <see cref="MachineQueryBinder.OpenForSend"/> and <c>OpenForSendAsync</c> both re-render from a
/// reopened input artifact and must reproduce byte-identical output.
/// </summary>
internal sealed class EuWatermarkWitnessSparqlRenderer : IMachineQueryRenderer
{
    private readonly EuWatermarkWitnessPlan _plan;
    private readonly MachineQueryRendererSource _rendererSource;

    internal EuWatermarkWitnessSparqlRenderer(EuWatermarkWitnessPlan plan, MachineQueryRendererSource rendererSource)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _rendererSource = rendererSource ?? throw new ArgumentNullException(nameof(rendererSource));
        RendererProfileRef = plan.ArtifactRef;
    }

    public SourceArtifactRef RendererProfileRef { get; }

    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _plan.CopyCanonicalIdentityBytes();

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet);

    internal MachineQueryRenderOutput RenderInput(MachineQueryInputArtifact input)
    {
        var slotNames = EuWatermarkWitnessPlan.EntryParameterNames();
        if (input.OrderedParameters.Count != slotNames.Count + 1)
        {
            throw new ArgumentException(
                "A witness page input has one boundary parameter and one parameter per entry slot.",
                nameof(input));
        }

        var parameter = input.OrderedParameters[0];
        if (!string.Equals(parameter.Name, EuWatermarkWitnessPlan.BoundaryParameterName, StringComparison.Ordinal) ||
            parameter.Kind != MachineQueryParameterKind.PublisherCursor ||
            parameter.TextValue is null)
        {
            throw new ArgumentException(
                "The witness page input does not carry the boundary cursor this renderer expects.", nameof(input));
        }

        var entries = new string[slotNames.Count];
        for (var index = 0; index < slotNames.Count; index++)
        {
            var slot = input.OrderedParameters[index + 1];
            if (!string.Equals(slot.Name, slotNames[index], StringComparison.Ordinal) ||
                slot.Kind != MachineQueryParameterKind.PublisherLiteral ||
                slot.TextValue is null)
            {
                throw new ArgumentException(
                    "The witness page input does not carry the entry slots this renderer expects.",
                    nameof(input));
            }

            entries[index] = slot.TextValue;
        }

        var watermarkLexical = EnumerationCursorEnvelope.Decode(parameter.TextValue);
        var query = _plan.RenderForWatermarkLexical(watermarkLexical, entries, out var refusal)
            ?? throw new ArgumentException($"The witness page could not be rendered: {refusal}.", nameof(input));
        return new MachineQueryRenderOutput(_plan.Endpoint, Encoding.UTF8.GetBytes(query));
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
    /// The page carries more rows than the plan asked for, so the endpoint did not honour the LIMIT
    /// and no page-shaped reasoning about it holds.
    /// </summary>
    [JsonStringEnumMemberName("page_exceeds_plan_limit")]
    PageExceedsPlanLimit = 2,

    /// <summary>
    /// A watermark on this page, or on the crossing's own cursor, has a shape no bounded
    /// observation covers. The channel has supplied no frozen order semantics for it, so the
    /// traversal stops here with a named cause and the recovery is the next cut, not a retry.
    /// </summary>
    [JsonStringEnumMemberName("watermark_shape_without_frozen_order_semantics")]
    WatermarkShapeWithoutFrozenOrderSemantics = 3,

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
    /// A full page carrying nothing above the boundary watermark. The group sharing that watermark
    /// is at least as large as the page, so the inclusive reread returns the same rows for ever and
    /// the traversal is stalled rather than finished.
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
/// neighbouring one, and that the page actually moved the traversal forward.
/// </para>
/// <para>
/// The second exists because no page limit can be shown to be large enough. The inclusive reread
/// re-delivers the whole group sharing the boundary watermark before anything new, so a group at
/// least as large as the page stalls the traversal while every individual page still looks well
/// formed. Group size is not a constant to design against: two bounded observations of the same
/// predicate on the same day, over the same top window, recorded groups of three to five and groups
/// of 41 to 49. So the stall is detected when it happens rather than prevented by choosing a
/// number.
/// </para>
/// <para>
/// A group here is the set of entries sharing an exact lexical watermark, not an instant. Two
/// entries naming the same moment under different offsets are two watermarks and are not tied.
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
        EuBoundaryCrossing? crossing,
        IReadOnlyList<EuWatermarkCursor> deliveredPage,
        IReadOnlyList<EuWatermarkCursor> newlyDelivered,
        EuWatermarkCursor? nextPosition,
        int rowsBeyondBoundary)
    {
        Plan = plan;
        Crossing = crossing;
        DeliveredPage = deliveredPage;
        NewlyDelivered = newlyDelivered;
        NextPosition = nextPosition;
        RowsBeyondBoundary = rowsBeyondBoundary;
    }

    /// <summary>The plan this page was rendered from.</summary>
    public EuWatermarkWitnessPlan Plan { get; }

    /// <summary>
    /// The reconciled boundary crossing this page carried, and ABSENT for a batch's opening page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO SITUATIONS WERE BEING SQUEEZED THROUGH ONE TYPE. A page that continues a traversal
    /// crosses a boundary the previous page established, and the crossing is what proves nothing
    /// was skipped across it. A batch's FIRST page crosses nothing: the batch has delivered no
    /// earlier page, so there is no boundary of its own to cross and no tie group it could have
    /// skipped.
    /// </para>
    /// <para>
    /// Forcing the first case's shape onto the second is what produced the defect this replaces.
    /// The witness runs in batches, and only ONE batch holds the run-wide boundary entry; every
    /// other batch was handed an EMPTY retained tie set, which
    /// <see cref="EuWatermarkStepRefusal.CrossingCursorNotInRetainedTieSet"/> refuses BY DESIGN, so
    /// at eighty two seeds every batch but one refused the whole run. Relaxing that guard was the
    /// obvious move and the wrong one: it exists so an empty tie set cannot reconcile against an
    /// empty reread and read as a clean terminal, which is a real defect it really does catch.
    /// Separating the two situations lets both survive intact.
    /// </para>
    /// </remarks>
    public EuBoundaryCrossing? Crossing { get; }

    /// <summary>Whether this step opened a batch rather than continuing one.</summary>
    public bool IsBatchOpening => Crossing is null;

    /// <summary>The page as delivered, in publisher order.</summary>
    public IReadOnlyList<EuWatermarkCursor> DeliveredPage { get; }

    /// <summary>
    /// The rows this page delivered that the incoming crossing had not already retained, in
    /// publisher order. What the cut newly learned from this request.
    /// </summary>
    /// <remarks>
    /// The complement of the inclusive reread. Accumulating this across a traversal is how a cut
    /// reaches each entry exactly once while every page still re-reads its boundary group.
    /// </remarks>
    public IReadOnlyList<EuWatermarkCursor> NewlyDelivered { get; }

    /// <summary>
    /// Where the next page reads from, or null when this page carried nothing beyond the boundary
    /// and is therefore consistent with the end of the traversal.
    /// </summary>
    public EuWatermarkCursor? NextPosition { get; }

    /// <summary>How many rows this page carried above the boundary watermark.</summary>
    public int RowsBeyondBoundary { get; }


    /// <summary>
    /// A batch's FIRST page: establishes where that batch's own traversal starts and what it
    /// carries forward, WITHOUT asserting a crossing it has nothing to cross.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every page validation <see cref="TryAdvance"/> performs is performed here too: the page
    /// limit, the admitted watermark shapes, strict ascent, and the floor at the plan's own start
    /// position. What is NOT performed is the boundary reconciliation, because there is no earlier
    /// page of this batch to reconcile against.
    /// </para>
    /// <para>
    /// AND THE WHOLE PAGE IS NEWLY DELIVERED, which is the substantive difference. A continuing
    /// page discounts the rows its incoming crossing already retained; an opening page has
    /// retained nothing, so discounting anything here would silently drop the batch's own first
    /// rows from the cut.
    /// </para>
    /// </remarks>
    /// <param name="plan">The plan this page was rendered from.</param>
    /// <param name="startPosition">The plan's own start position, which the page must not precede.</param>
    /// <param name="deliveredPage">The page as delivered, in publisher order.</param>
    /// <param name="refusal">Why no step exists, when none does.</param>
    public static EuWatermarkTraversalStep? TryOpenBatch(
        EuWatermarkWitnessPlan plan,
        EuWatermarkCursor startPosition,
        IReadOnlyList<EuWatermarkCursor> deliveredPage,
        out EuWatermarkStepRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(startPosition);
        ArgumentNullException.ThrowIfNull(deliveredPage);

        var page = deliveredPage.ToArray();
        if (Array.Exists(page, static row => row is null))
        {
            throw new ArgumentException("A delivered row cannot be null.", nameof(deliveredPage));
        }

        if (page.Length > plan.PageLimit)
        {
            refusal = EuWatermarkStepRefusal.PageExceedsPlanLimit;
            return null;
        }

        if (Outside(startPosition.WatermarkLexical) ||
            Array.Exists(page, row => Outside(row.WatermarkLexical)))
        {
            refusal = EuWatermarkStepRefusal.WatermarkShapeWithoutFrozenOrderSemantics;
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

        if (page.Length > 0 &&
            string.CompareOrdinal(page[0].WatermarkLexical, startPosition.WatermarkLexical) < 0)
        {
            refusal = EuWatermarkStepRefusal.PageBelowBoundaryWatermark;
            return null;
        }

        // BEYOND IS STILL COUNTED AGAINST THE START POSITION. Only the RECONCILIATION is dropped,
        // not the arithmetic: a page carrying nothing above the start watermark is terminal here
        // exactly as it is in a continuing step, so opening a batch costs no extra request.
        var atStart = page.Count(row => string.Equals(
            row.WatermarkLexical, startPosition.WatermarkLexical, StringComparison.Ordinal));
        var beyond = page.Length - atStart;

        if (beyond == 0 && page.Length == plan.PageLimit)
        {
            refusal = EuWatermarkStepRefusal.TraversalCannotAdvance;
            return null;
        }

        // NEWLY DELIVERED IS EVERYTHING EXCEPT THE START POSITION ITSELF. That one row, when the
        // batch holds it at all, is the previous cut's own boundary being re-read rather than
        // something this cut learned. Every other row is new to this batch, including the rest of
        // the boundary group, because this batch has delivered no earlier page to have retained it.
        var newlyDelivered = page
            .Where(row => row.CompareTo(startPosition) != 0)
            .ToArray();

        refusal = EuWatermarkStepRefusal.None;
        return new EuWatermarkTraversalStep(
            plan,
            null,
            Array.AsReadOnly(page),
            Array.AsReadOnly(newlyDelivered),
            beyond > 0 ? page[^1] : null,
            beyond);
    }

    /// <summary>
    /// The only path that mints a step.
    /// </summary>
    /// <param name="plan">The frozen plan, for its page limit and admitted shapes.</param>
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

        if (Outside(boundaryWatermark) || Array.Exists(page, row => Outside(row.WatermarkLexical)))
        {
            refusal = EuWatermarkStepRefusal.WatermarkShapeWithoutFrozenOrderSemantics;
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
        var retained = new HashSet<string>(crossing.RetainedTieSet, StringComparer.Ordinal);
        var reread = new HashSet<string>(retained, StringComparer.Ordinal);
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

        var newlyDelivered = page
            .Where(row => !string.Equals(
                    row.WatermarkLexical, boundaryWatermark, StringComparison.Ordinal)
                || !retained.Contains(row.CanonicalEntryKey))
            .ToArray();

        refusal = EuWatermarkStepRefusal.None;
        return new EuWatermarkTraversalStep(
            plan,
            crossing,
            Array.AsReadOnly(page),
            Array.AsReadOnly(newlyDelivered),
            beyond > 0 ? page[^1] : null,
            beyond);
    }

    private static bool Outside(string watermarkLexical) =>
        EuWatermarkWitnessPlan.ClassifyShape(watermarkLexical) ==
        EuWatermarkLexicalShape.OutsideTheMeasuredSet;
}
