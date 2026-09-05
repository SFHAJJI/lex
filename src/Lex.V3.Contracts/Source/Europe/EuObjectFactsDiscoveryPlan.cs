using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The bounded row-set query-plan families D1-05c-1 adds beyond D1-05a's own closure
/// (<see cref="EuConsolidationDiscoveryPlan"/>'s <c>Family</c> set, reused unchanged for <c>O</c>),
/// plus the manifestation-listing family D1-05d adds to the same machinery.
/// </summary>
/// <remarks>
/// <see cref="ObjectFacts"/> ("P" in the design record) asks the nine object-authority
/// <see cref="EuCdmPredicate"/> members plus the four <see cref="EuScopeVocabulary.ReadRelationFamilies"/>
/// predicates as triples on the same subject, over every object in the closure's own object set
/// <c>O</c> (roots and discovered states alike). <see cref="ExpressionFacts"/> ("X") asks the four
/// Expression-authority <see cref="EuCdmPredicate"/> members over the Expressions of <c>O</c>,
/// discovered by the <c>expression_belongs_to_work</c> join itself rather than trusted from an
/// external enumeration - X proves its own closure. <see cref="RootWatermark"/> ("W") asks
/// <see cref="EuWatermarkWitnessPlan.WatermarkPredicateIri"/> over the 82 Appendix A roots only,
/// feeding <see cref="EuFirstCutWatermarkBootstrap.TryComputeStartPosition"/>.
/// <see cref="ManifestationFacts"/> ("M", D1-05d) asks the one Manifestation-authority predicate
/// <see cref="EuObjectFactsDiscoveryPlan.ManifestationTypePredicateIri"/> over the Manifestations
/// reached from every object in <c>O</c> through <c>expression_belongs_to_work</c> and then
/// <c>manifestation_manifests_expression</c> - the office's own per-work listing of the formats it
/// offers. See <see cref="EuManifestationListingDecode"/> for exactly what that listing does and
/// does not entitle a reader to conclude.
/// </remarks>
public enum EuObjectFactsQuerySet
{
    ObjectFacts = 1,
    ExpressionFacts = 2,
    RootWatermark = 3,
    ManifestationFacts = 4,
}

/// <summary>Same two-pass shape as <see cref="EuConsolidationDiscoveryPlan"/>'s own pass enum.</summary>
public enum EuObjectFactsQueryPass
{
    Pass1 = 1,
    Pass2 = 2,
}

internal sealed class EuObjectFactsQueryDefinition
{
    internal EuObjectFactsQueryDefinition(
        EuObjectFactsQuerySet set,
        SourceRegistryMemberRef countQueryFamilyRef,
        SourceRegistryMemberRef pageQueryFamilyRef,
        string countTemplate,
        string pageTemplate,
        IReadOnlyList<string> projectionVariables,
        IReadOnlyList<string> cursorVariables)
    {
        Set = set;
        CountQueryFamilyRef = countQueryFamilyRef;
        PageQueryFamilyRef = pageQueryFamilyRef;
        CountTemplate = countTemplate;
        PageTemplate = pageTemplate;
        ProjectionVariables = Array.AsReadOnly(projectionVariables.ToArray());
        CursorVariables = Array.AsReadOnly(cursorVariables.ToArray());
    }

    internal EuObjectFactsQuerySet Set { get; }
    internal SourceRegistryMemberRef CountQueryFamilyRef { get; }
    internal SourceRegistryMemberRef PageQueryFamilyRef { get; }
    internal string CountTemplate { get; }
    internal string PageTemplate { get; }
    internal IReadOnlyList<string> ProjectionVariables { get; }
    internal IReadOnlyList<string> CursorVariables { get; }
}

/// <summary>The bound query and every artifact that feeds it, for one count or page request.</summary>
public sealed record EuObjectFactsBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Closed machine-query description for the object-authority, Expression-authority and watermark
/// facts SCOPE_RULING <c>lex-event-20260904T040718222Z-7e6f29af07024cf5b2cb716f94f288e3</c> assigns
/// to this slice.
/// </summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="EuConsolidationDiscoveryPlan"/>: one <see cref="EuObjectFactsQueryDefinition"/>
/// per query set, a fixed <c>count</c> template wrapping a fixed row-shape template, a paged template
/// adding the same generic <c>(object, predicate, value, value_kind, datatype_iri, language_tag)</c>
/// row shape and a cursor derived from that row's own natural key, an unbound <c>FILTER NOT EXISTS</c>
/// branch recording an explicit absence row rather than silently omitting one. This is the identical
/// pattern <see cref="EuConsolidationDiscoveryPlan"/>'s own <c>Family</c>/<c>TemporalFacts</c> sets
/// establish, generalized from one bound <c>?base_celex</c>/<c>?state</c> pair to a VALUES-batched
/// object set.
/// </para>
/// <para>
/// THE VALUE-DERIVED CURSOR COMPONENT IS TOTALISED WITH COALESCE, and the reason is measured rather
/// than assumed. Each family binds exactly one key position from <c>?value</c>, which the absence
/// branch leaves unbound: <c>key_4</c> for <see cref="EuObjectFactsQuerySet.ObjectFacts"/> and
/// <see cref="EuObjectFactsQuerySet.ExpressionFacts"/>, <c>key_3</c> for
/// <see cref="EuObjectFactsQuerySet.RootWatermark"/> and
/// <see cref="EuObjectFactsQuerySet.ManifestationFacts"/>. Every other position binds from a VALUES
/// term or from a literal this template BINDs in both UNION branches, so none of them can be unbound.
/// </para>
/// <para>
/// That one position used to read <c>IF(BOUND(?value), STR(?value), "")</c>, which is correct under
/// SPARQL's own lazy IF and wrong against the publisher's engine. A bounded three-query probe over
/// the exact batch that produced a 41-binding page settled it by naming the behaviour from the
/// response rather than inferring it (PROBE
/// lex-event-20260905T015937388Z-8bc0d2893047464c91a6a1c54982b5e1, RULING
/// lex-event-20260905T020043766Z-cd0db29d887b4d86b5c44da66d82e2f7). On an unbound row the engine
/// answered <c>BOUND(?value)</c> false, took IF's false branch when neither arm called STR, and
/// still errored when the untaken arm did: it selects the branch correctly and evaluates the
/// arguments EAGERLY, so <c>STR</c> on the unbound term raised and the erroring BIND left the key
/// unbound. SPARQL's JSON results format then omits an unbound variable from the binding entirely,
/// so the key vanished from 8 of 41 rows and this route refused a conformant answer. COALESCE is
/// specified to swallow an erroring argument and take the next, and the same probe confirmed it: the
/// key became total while the row count stayed 41.
/// </para>
/// <para>
/// THE OTHER EIGHT BINDS ARE LEFT ALONE, AND THAT IS MEASURED RATHER THAN ASSUMED. Each family also
/// feeds <c>datatype_iri</c> and <c>language_tag</c> through
/// <c>IF(isLiteral(?value), STR(DATATYPE(?value)), "")</c> and
/// <c>IF(isLiteral(?value), LANG(?value), "")</c>, which under the same eager argument evaluation
/// could raise on an IRI-valued row and leave both unbound. The retained post-fix page answers it:
/// of its 41 bindings, 23 ARE IRI-VALUED, and <c>key_5</c>, <c>key_6</c>, <c>datatype_iri</c> and
/// <c>language_tag</c> are present in every one of them. So this engine does not raise inside
/// DATATYPE or LANG on a bound IRI, and those eight sites need no change. The difference from the
/// four that did: those dereference a possibly UNBOUND variable, where eager evaluation raises an
/// unbound-variable error; these dereference a BOUND term of the wrong type, where it does not.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY NOT TOTALISED IS <c>?value</c> ITSELF. It stays absent on exactly those
/// rows, because that absence IS the unbound fact, recorded alongside <c>value_kind</c> of
/// <c>"unbound"</c>. Only the CURSOR becomes total. A change that had totalised both would have
/// destroyed the fact while appearing to succeed, and would have passed any test that merely counted
/// absences.
/// </para>
/// <para>
/// The batch is this design's partition (per the synthesis ruling): a bounded, fixed-capacity set of
/// canonical object IRIs, VALUES-bound in one request. <see cref="BatchCapacity"/> is fixed at 50, not
/// a tuning constant: every batch member is carried as its own <c>publisher_literal</c>
/// <see cref="MachineQueryParameter"/> (an IRI cannot be embedded free-form in query text without a
/// carrier the delivery-verification machinery can bind and reproduce), and
/// <c>MachineQueryValidation.MaximumParameterCount</c> (64, Source/Core, unchanged by this slice) caps
/// the total ordered parameters one request may carry. Nine of those are always spent on
/// <c>pass_id</c>, <c>has_cursor</c> and up to seven cursor-continuation parameters -
/// <see cref="ExpressionFacts"/>'s own seven-part cursor is the widest of the four, wider than
/// <see cref="ObjectFacts"/>'s six and <see cref="RootWatermark"/>'s and
/// <see cref="ManifestationFacts"/>'s five - leaving 55; 50 keeps a
/// five-parameter margin rather than sitting on the ceiling, and matches
/// <c>D1-05C-DESIGN-PROPOSAL-A.md</c>'s own batch constant. A batch smaller than
/// <see cref="BatchCapacity"/> is padded by repeating its own lexicographically-greatest canonical
/// member into the unused slots: every template groups by the generic row shape
/// (<c>GROUP BY ?object ...</c>), so a VALUES entry repeating a value already in the batch produces no
/// row a first occurrence of that same object did not already produce - padding changes no observed
/// fact, only which of the 50 named parameters happens to carry it. The partition/member key is never
/// computed from the padded slots: it is the SHA-256 of the batch's own sorted, deduplicated,
/// LF-joined canonical members, so two callers naming the same real batch in a different order or
/// padded to a different multiplicity bind the identical partition.
/// </para>
/// <para>
/// <see cref="RootWatermark"/>'s row shape carries no <c>predicate</c> column: every row already
/// describes the one fixed predicate <see cref="EuWatermarkWitnessPlan.WatermarkPredicateIri"/>, so a
/// constant key column would carry no distinguishing information.
/// <see cref="ManifestationFacts"/> carries none for the same reason (its one predicate is
/// <see cref="ManifestationTypePredicateIri"/>). Their cursors are therefore five parts,
/// not six - the same "cursor arity matches the row's own natural key, not a fixed count" precedent
/// <see cref="EuConsolidationDiscoveryPlan"/>'s own <c>Family</c> set already sets with its one-part
/// <c>state_key</c> cursor, generalized here to the five parts <see cref="RootWatermark"/> actually
/// needs rather than forced to six.
/// </para>
/// <para>
/// This is an internal, non-authoritative selector-delivery contract exactly as
/// <see cref="EuConsolidationDiscoveryPlan"/>'s own remarks describe: no claim of publisher
/// completeness, absence, a legal interval, a release use, or permission to serve text. Fixtures only;
/// nothing here calls a store or a publisher endpoint.
/// </para>
/// </remarks>
public sealed class EuObjectFactsDiscoveryPlan
{
    /// <summary>
    /// Every batch is exactly this many named object slots (padded when the real batch is smaller).
    /// See the type remarks for why 50, not the review record's own 256.
    /// </summary>
    public const int BatchCapacity = 50;

    internal const string Cdm = EuConsolidationDiscoveryPlan.Cdm;
    internal const string WatermarkPredicateIri = EuWatermarkWitnessPlan.WatermarkPredicateIri;

    /// <summary>
    /// The Manifestation-to-Expression edge family M walks. Declared here as family M's own constant
    /// rather than as a fourteenth <see cref="EuCdmPredicate"/> member, exactly as
    /// <see cref="WatermarkPredicateIri"/> already is for family W: this type's own constructor
    /// asserts that families P and X partition the closed thirteen-member CDM predicate vocabulary
    /// exactly once each, so a fourteenth member would break that partition to describe a predicate
    /// neither P nor X asks.
    /// </summary>
    internal const string ManifestsExpressionPredicateIri = Cdm + "manifestation_manifests_expression";

    /// <summary>The one predicate family M reads. See <see cref="ManifestsExpressionPredicateIri"/>.</summary>
    internal const string ManifestationTypePredicateIri = Cdm + "manifestation_type";

    private const string ResourceId = "urn:uuid:6f3f0a1e-6b8b-4e6a-8f36-6a7f2c9d5b41";
    private const string ObjectFactsMemberPrefix = "eu-object-facts";
    private const string ExpressionFactsMemberPrefix = "eu-expression-facts";
    private const string RootWatermarkMemberPrefix = "eu-root-watermark";
    private const string ManifestationFactsMemberPrefix = "eu-manifestation-facts";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The nine CDM predicates asserted on the object itself. Family P's own predicates.</summary>
    internal static readonly IReadOnlyList<EuCdmPredicate> ObjectAuthorityPredicates = Array.AsReadOnly(
        new[]
        {
            EuCdmPredicate.ResourceLegalIdCelex,
            EuCdmPredicate.ResourceLegalType,
            EuCdmPredicate.WorkHasResourceType,
            EuCdmPredicate.WorkDateDocument,
            EuCdmPredicate.ActConsolidatedDate,
            EuCdmPredicate.DateCreationLegacy,
            EuCdmPredicate.ResourceLegalInForce,
            EuCdmPredicate.WorkIsAboutConceptEurovoc,
            EuCdmPredicate.ResourceLegalIsAboutConceptDirectoryCode,
        });

    /// <summary>The four CDM predicates asserted on an Expression. Family X's own predicates.</summary>
    internal static readonly IReadOnlyList<EuCdmPredicate> ExpressionAuthorityPredicates = Array.AsReadOnly(
        new[]
        {
            EuCdmPredicate.ExpressionBelongsToWork,
            EuCdmPredicate.ExpressionUsesLanguage,
            EuCdmPredicate.ExpressionTitle,
            EuCdmPredicate.ExpressionTitleShort,
        });

    private readonly IReadOnlyDictionary<EuObjectFactsQuerySet, EuObjectFactsQueryDefinition> _definitions;
    private readonly byte[] _canonicalIdentityBytes;

    private EuObjectFactsDiscoveryPlan()
    {
        if (ObjectAuthorityPredicates.Count + ExpressionAuthorityPredicates.Count !=
                EuScopeVocabulary.CdmPredicates.Count ||
            ObjectAuthorityPredicates.Concat(ExpressionAuthorityPredicates).Distinct().Count() !=
                EuScopeVocabulary.CdmPredicates.Count)
        {
            throw new InvalidOperationException(
                "Family P and family X together must partition the closed thirteen-member CDM " +
                "predicate vocabulary exactly once each.");
        }

        var objectFactsProjection = new[]
        {
            "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
        };
        var expressionFactsProjection = new[]
        {
            "parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
        };
        var rootWatermarkProjection = new[]
        {
            "object", "value", "value_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5",
        };

        // Family M's row shape is family W's shape with ?parent in place of ?object: like W it asks
        // exactly one predicate, so a constant predicate column would carry no distinguishing
        // information, and like X its VALUES-bound term is the parent Work rather than the row's own
        // discovered subject. The Manifestation IRI is deliberately NOT projected: M's fact is "this
        // Work's listing offers this manifestation type", and grouping it per Manifestation instead
        // multiplies the row set by every language expression of every act (measured live on
        // 2026-09-04 for CELEX 32008R0593: 5 grouped rows against a 32 KB per-manifestation result)
        // without adding one fact the format ladder reads.
        var manifestationFactsProjection = new[]
        {
            "parent", "value", "value_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5",
        };
        var sixKeyCursor = new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" };

        // Family X's own SELECT groups by ?parent ?object ?predicate ?value ?value_kind
        // ?datatype_iri ?language_tag - seven columns, since one Expression can in principle belong
        // to more than one Work in the same batch (Decision: SCOPE_RULING review, design fix two).
        // The six-part key_1..key_6 below covers everything but ?parent, so two rows sharing one
        // Expression under two different parent Works would carry the identical six-part cursor -
        // a collision EnumerationDeliveryComparison.VerifyPages refuses outright. key_7 closes that
        // gap by carrying ?parent, so X's cursor covers its own grouped row identity exactly, the
        // same "cursor arity matches the row's own natural key" rule the five-part
        // <see cref="RootWatermark"/> cursor already follows for a narrower reason.
        var sevenKeyCursor = new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7" };
        var fiveKeyCursor = new[] { "key_1", "key_2", "key_3", "key_4", "key_5" };

        var templates = BuildTemplates();

        var identityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "eu-object-facts-discovery-plan/1",
            "endpoint=" + EuConsolidationDiscoveryPlan.PublisherEndpoint,
            "method=POST",
            "target=/webapi/rdf/sparql",
            "request_media_type=application/sparql-query",
            "response_media_type=" + ResponseMediaType,
            "cursor_envelope=" + EnumerationCursorEnvelope.Identity,
            "threshold_detector=" + ThresholdDetectorIdentity,
            "publisher_delivery_ceiling_rows=" + EuConsolidationDiscoveryPlan.PublisherDeliveryCeilingRows
                .ToString(CultureInfo.InvariantCulture),
            "batch_capacity=" + BatchCapacity.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + ((int)EuObjectFactsQueryPass.Pass1).ToString(CultureInfo.InvariantCulture) + ":" +
                EuConsolidationDiscoveryPlan.Pass1PageLimit.ToString(CultureInfo.InvariantCulture),
            "pass_2=" + ((int)EuObjectFactsQueryPass.Pass2).ToString(CultureInfo.InvariantCulture) + ":" +
                EuConsolidationDiscoveryPlan.Pass2PageLimit.ToString(CultureInfo.InvariantCulture),
            "terminal_page_policy=empty_successor_after_short_page",
            "selection_parameters=" + string.Join(',', BatchParameterNames()),
            "pass_parameter=pass_id",
            "cursor_presence_parameter=has_cursor",
            "object_facts_projection=" + string.Join(',', objectFactsProjection),
            "object_facts_cursor=" + string.Join(',', sixKeyCursor),
            "expression_facts_projection=" + string.Join(',', expressionFactsProjection),
            "expression_facts_cursor=" + string.Join(',', sevenKeyCursor),
            "root_watermark_projection=" + string.Join(',', rootWatermarkProjection),
            "root_watermark_cursor=" + string.Join(',', fiveKeyCursor),
            "manifestation_facts_projection=" + string.Join(',', manifestationFactsProjection),
            "manifestation_facts_cursor=" + string.Join(',', fiveKeyCursor),
            "object_facts_count_member=" + ObjectFactsMemberPrefix + ".count",
            "object_facts_page_member=" + ObjectFactsMemberPrefix + ".page",
            "expression_facts_count_member=" + ExpressionFactsMemberPrefix + ".count",
            "expression_facts_page_member=" + ExpressionFactsMemberPrefix + ".page",
            "root_watermark_count_member=" + RootWatermarkMemberPrefix + ".count",
            "root_watermark_page_member=" + RootWatermarkMemberPrefix + ".page",
            "manifestation_facts_count_member=" + ManifestationFactsMemberPrefix + ".count",
            "manifestation_facts_page_member=" + ManifestationFactsMemberPrefix + ".page",
            templates.ObjectFactsCount,
            templates.ObjectFactsPage,
            templates.ExpressionFactsCount,
            templates.ExpressionFactsPage,
            templates.RootWatermarkCount,
            templates.RootWatermarkPage,
            templates.ManifestationFactsCount,
            templates.ManifestationFactsPage,
        }));
        ArtifactRef = new SourceArtifactRef(ResourceId, Sha256(identityBytes));
        _canonicalIdentityBytes = identityBytes;

        _definitions = new Dictionary<EuObjectFactsQuerySet, EuObjectFactsQueryDefinition>
        {
            [EuObjectFactsQuerySet.ObjectFacts] = Definition(
                EuObjectFactsQuerySet.ObjectFacts,
                ObjectFactsMemberPrefix,
                templates.ObjectFactsCount,
                templates.ObjectFactsPage,
                objectFactsProjection,
                sixKeyCursor),
            [EuObjectFactsQuerySet.ExpressionFacts] = Definition(
                EuObjectFactsQuerySet.ExpressionFacts,
                ExpressionFactsMemberPrefix,
                templates.ExpressionFactsCount,
                templates.ExpressionFactsPage,
                expressionFactsProjection,
                sevenKeyCursor),
            [EuObjectFactsQuerySet.RootWatermark] = Definition(
                EuObjectFactsQuerySet.RootWatermark,
                RootWatermarkMemberPrefix,
                templates.RootWatermarkCount,
                templates.RootWatermarkPage,
                rootWatermarkProjection,
                fiveKeyCursor),
            [EuObjectFactsQuerySet.ManifestationFacts] = Definition(
                EuObjectFactsQuerySet.ManifestationFacts,
                ManifestationFactsMemberPrefix,
                templates.ManifestationFactsCount,
                templates.ManifestationFactsPage,
                manifestationFactsProjection,
                fiveKeyCursor),
        };
    }

    public SourceArtifactRef ArtifactRef { get; }

    public static EuObjectFactsDiscoveryPlan Create() => new();

    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    internal EuObjectFactsQueryDefinition Definition(EuObjectFactsQuerySet set) =>
        _definitions.TryGetValue(set, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(set));

    public RepeatedEnumerationInterpretationProfile CreateDeliveryProfile(EuObjectFactsQuerySet set)
    {
        var definition = Definition(set);
        return new RepeatedEnumerationInterpretationProfile(
            RepeatedEnumerationInterpretationProfile.SchemaId,
            RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso,
            ResponseMediaType,
            EnumerationCursorEnvelope.Identity,
            EuConsolidationDiscoveryPlan.PublisherDeliveryCeilingRows,
            ThresholdDetectorIdentity,
            definition.CountQueryFamilyRef,
            definition.PageQueryFamilyRef,
            "count",
            definition.ProjectionVariables,
            definition.CursorVariables,
            definition.CursorVariables,
            BatchParameterNames(),
            "pass_id",
            definition.CursorVariables.Select(static value => "last_" + value).ToArray(),
            "has_cursor",
            // A WHOLE SET ON ONE PAGE COMPLETES RATHER THAN REFUSES. RULING
            // lex-event-20260905T021827470Z-61309ecca6e8414db4150b451b181ebb. This declared
            // EmptySuccessorAfterShortPage, which obliges a run to fetch one more page after a short
            // one and requires that page to come back EMPTY. Every family here fits in a single page
            // (the observed counts are 41, 166, 2 and 9 against limits of 997 and 613), so every run
            // spent a request asking for nothing, and the publisher answered those requests with a
            // TAIL SUBSET of rows already delivered rather than with nothing. The executor then
            // correctly refused CursorDidNotAdvance, which is a true observation of a useless
            // request. ORDER BY plus LIMIT already prove a short page has exhausted the result set,
            // so the successor established nothing that the short page had not.
            //
            // THIS DOES NOT RETIRE THE COUNT CROSS CHECK, which remains the closure test for the
            // enumeration. What the terminal policy settles is WHEN TO STOP ASKING; what the count
            // settles is WHETHER WE GOT EVERYTHING. A page can be short because the set is exhausted
            // or because the publisher truncated it, and only the independent count answered by the
            // count query tells those apart. Dropping the successor removed a request that proved
            // nothing; it did not remove the proof.
            RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);
    }

    public EuObjectFactsBoundQuery BindCount(
        EuObjectFactsQuerySet set,
        IReadOnlyList<string> batchObjects,
        EuObjectFactsQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        var definition = Definition(set);
        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody, null, null, null);
        return Bind(
            definition, set, isPage: false, batchObjects, pass, cursor: null, response,
            machinePlanResourceId, inputResourceId, rendererSource);
    }

    public EuObjectFactsBoundQuery BindPage(
        EuObjectFactsQuerySet set,
        IReadOnlyList<string> batchObjects,
        EuObjectFactsQueryPass pass,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        var definition = Definition(set);
        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            PageLimit(pass),
            expectedPartitionRowCount,
            expectedPartitionRowCountEvidenceRef);
        return Bind(
            definition, set, isPage: true, batchObjects, pass, cursor, response,
            machinePlanResourceId, inputResourceId, rendererSource);
    }

    /// <summary>
    /// The batch's own partition/member key: the SHA-256 of its sorted, deduplicated, canonical,
    /// LF-joined members. Never computed from the padded 50-slot parameter set - two batches naming
    /// the same real objects bind the same partition regardless of padding or input order. The
    /// 24-hex-character (96-bit) truncation matches <see cref="EuConsolidationDiscoveryPlan"/>'s own
    /// <c>PartitionKey</c>, the only other partition/member key this source mints; 96 bits of a
    /// cryptographic digest is far past the collision risk this key space (batches of Appendix A's 82
    /// roots and their discovered states) can ever reach, so this reuses that plan's own established
    /// length rather than choosing a second one for the same purpose.
    /// </summary>
    public static string PartitionKeyFor(IReadOnlyList<string> batchObjects)
    {
        var canonical = CanonicalizeBatch(batchObjects);
        return "eu-object-facts-batch-" +
            Sha256(StrictUtf8.GetBytes(string.Join('\n', canonical)))[..24];
    }

    private EuObjectFactsBoundQuery Bind(
        EuObjectFactsQueryDefinition definition,
        EuObjectFactsQuerySet set,
        bool isPage,
        IReadOnlyList<string> batchObjects,
        EuObjectFactsQueryPass pass,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        var canonicalBatch = CanonicalizeBatch(batchObjects);
        if (set == EuObjectFactsQuerySet.RootWatermark &&
            canonicalBatch.Any(iri => !EuAppendixASeedMap.PackRoots.Contains(iri)))
        {
            throw new ArgumentException(
                "Every root-watermark batch member must be one of Appendix A's 82 roots.",
                nameof(batchObjects));
        }

        ArgumentNullException.ThrowIfNull(rendererSource);
        var padded = PadBatch(canonicalBatch);
        var parameters = new List<MachineQueryParameter>();
        var slotNames = BatchParameterNames();
        for (var index = 0; index < slotNames.Count; index++)
        {
            parameters.Add(new MachineQueryParameter(
                slotNames[index],
                MachineQueryParameterKind.PublisherLiteral,
                null,
                padded[index],
                ArtifactRef));
        }

        parameters.Add(new MachineQueryParameter(
            "pass_id", MachineQueryParameterKind.BoundedInteger, (int)pass, null, ArtifactRef));

        if (isPage)
        {
            var values = cursor?.ToArray() ?? [];
            if (values.Length != 0 && values.Length != definition.CursorVariables.Count)
            {
                throw new ArgumentException(
                    "A continuation cursor must have the exact query-set arity.", nameof(cursor));
            }

            parameters.Add(new MachineQueryParameter(
                "has_cursor",
                MachineQueryParameterKind.BoundedInteger,
                values.Length == 0 ? 0 : 1,
                null,
                ArtifactRef));
            for (var index = 0; index < values.Length; index++)
            {
                parameters.Add(new MachineQueryParameter(
                    "last_" + definition.CursorVariables[index],
                    MachineQueryParameterKind.PublisherCursor,
                    null,
                    EnumerationCursorEnvelope.Encode(values[index]),
                    ArtifactRef));
            }
        }
        else if (cursor is not null)
        {
            throw new ArgumentException("A count query cannot carry a cursor.", nameof(cursor));
        }

        var family = isPage ? definition.PageQueryFamilyRef : definition.CountQueryFamilyRef;
        var input = MachineQueryInputArtifact.Create(
            inputResourceId, family, PartitionKeyFor(canonicalBatch), response, parameters);
        var renderer = new EuObjectFactsSparqlRenderer(this, definition, isPage, rendererSource);
        var rendered = renderer.RenderInput(input, response);
        var body = rendered.CopyRequestBody();
        var targetBytes = Encoding.ASCII.GetBytes("/webapi/rdf/sparql");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            family,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Post,
            EuConsolidationDiscoveryPlan.PublisherEndpoint,
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
        return new EuObjectFactsBoundQuery(machinePlan, machinePlanRef, input, request);
    }

    private EuObjectFactsQueryDefinition Definition(
        EuObjectFactsQuerySet set,
        string memberPrefix,
        string count,
        string page,
        IReadOnlyList<string> projection,
        IReadOnlyList<string> cursor) => new(
        set,
        new SourceRegistryMemberRef(ArtifactRef, memberPrefix + ".count"),
        new SourceRegistryMemberRef(ArtifactRef, memberPrefix + ".page"),
        count,
        page,
        projection,
        cursor);

    private static uint PageLimit(EuObjectFactsQueryPass pass) => pass switch
    {
        EuObjectFactsQueryPass.Pass1 => EuConsolidationDiscoveryPlan.Pass1PageLimit,
        EuObjectFactsQueryPass.Pass2 => EuConsolidationDiscoveryPlan.Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    internal static IReadOnlyList<string> BatchParameterNames()
    {
        var names = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            names[index] = "requested_object_" + (index + 1).ToString("D2", CultureInfo.InvariantCulture);
        }

        return Array.AsReadOnly(names);
    }

    /// <summary>
    /// Canonicalizes every batch member to Appendix A's exact lexical form, refuses a null, empty,
    /// over-capacity or duplicate-after-canonicalization batch, and returns it sorted ordinally - the
    /// fixed order every partition-key digest and padding decision is computed from.
    /// </summary>
    private static string[] CanonicalizeBatch(IReadOnlyList<string> batchObjects)
    {
        ArgumentNullException.ThrowIfNull(batchObjects);
        if (batchObjects.Count is 0 or > BatchCapacity)
        {
            throw new ArgumentException(
                $"A batch must name one to {BatchCapacity} objects.", nameof(batchObjects));
        }

        var canonical = new string[batchObjects.Count];
        for (var index = 0; index < batchObjects.Count; index++)
        {
            var value = batchObjects[index] ??
                throw new ArgumentException("A batch member cannot be null.", nameof(batchObjects));
            canonical[index] = EuPackRootCanonicalForm.TryCanonicalize(value, out _) ??
                throw new ArgumentException(
                    $"Batch member '{value}' does not reduce to Appendix A's exact lexical form.",
                    nameof(batchObjects));
        }

        if (canonical.Distinct(StringComparer.Ordinal).Count() != canonical.Length)
        {
            throw new ArgumentException(
                "A batch cannot name the same canonical object twice.", nameof(batchObjects));
        }

        Array.Sort(canonical, StringComparer.Ordinal);
        return canonical;
    }

    /// <summary>
    /// Pads a canonicalized, sorted batch to exactly <see cref="BatchCapacity"/> entries by repeating
    /// its own lexicographically-greatest (last, since it is sorted) member. See the type remarks for
    /// why this changes no observed fact.
    /// </summary>
    private static string[] PadBatch(IReadOnlyList<string> canonicalSortedBatch)
    {
        var padded = new string[BatchCapacity];
        var last = canonicalSortedBatch[^1];
        for (var index = 0; index < BatchCapacity; index++)
        {
            padded[index] = index < canonicalSortedBatch.Count ? canonicalSortedBatch[index] : last;
        }

        return padded;
    }

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static (
        string ObjectFactsCount, string ObjectFactsPage,
        string ExpressionFactsCount, string ExpressionFactsPage,
        string RootWatermarkCount, string RootWatermarkPage,
        string ManifestationFactsCount, string ManifestationFactsPage) BuildTemplates()
    {
        var slots = BatchParameterNames();
        var valuesBlock = string.Join('\n', slots.Select(static name => "    {" + name + ":iri}"));

        var objectPredicates = string.Join('\n', ObjectAuthorityPredicates
            .Select(static predicate => "    <" + CdmIri(predicate) + ">")
            .Concat(EuScopeVocabulary.ReadRelationFamilies.Select(static family =>
                "    <" + RelationIri(family) + ">")));

        var objectFactsRows = $$"""
            SELECT ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES ?object {
            {{valuesBlock}}
              }
              VALUES ?predicate {
            {{objectPredicates}}
              }
              {
                ?object ?predicate ?value .
                BIND(IF(isIRI(?value), "iri", IF(isLiteral(?value), "literal", "unsupported_blank_node")) AS ?value_kind)
                BIND(IF(isLiteral(?value), STR(DATATYPE(?value)), "") AS ?datatype_iri)
                BIND(IF(isLiteral(?value), LANG(?value), "") AS ?language_tag)
              }
              UNION
              {
                FILTER NOT EXISTS { ?object ?predicate ?missing_value }
                BIND("unbound" AS ?value_kind)
                BIND("" AS ?datatype_iri)
                BIND("" AS ?language_tag)
              }
            }
            GROUP BY ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag
            """;
        var objectFactsCount = Wrap(objectFactsRows);
        var objectFactsPage = $$"""
            SELECT ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 WHERE {
              {
            {{Indent(Indent(objectFactsRows))}}
              }
              BIND(STR(?object) AS ?key_1)
              BIND(STR(?predicate) AS ?key_2)
              BIND(?value_kind AS ?key_3)
              BIND(COALESCE(STR(?value), "") AS ?key_4)
              BIND(?datatype_iri AS ?key_5)
              BIND(?language_tag AS ?key_6)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4 ?last_key_5 ?last_key_6) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string} {last_key_5:sparql_string} {last_key_6:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 > ?last_key_5) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 = ?last_key_5 && ?key_6 > ?last_key_6)
              )
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6
            LIMIT {page_limit:uint}
            """;

        var expressionPredicates = string.Join('\n', ExpressionAuthorityPredicates
            .Select(static predicate => "    <" + CdmIri(predicate) + ">"));
        var expressionFactsRows = $$"""
            SELECT ?parent ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES ?parent {
            {{valuesBlock}}
              }
              ?object <{{CdmIri(EuCdmPredicate.ExpressionBelongsToWork)}}> ?parent .
              VALUES ?predicate {
            {{expressionPredicates}}
              }
              {
                ?object ?predicate ?value .
                BIND(IF(isIRI(?value), "iri", IF(isLiteral(?value), "literal", "unsupported_blank_node")) AS ?value_kind)
                BIND(IF(isLiteral(?value), STR(DATATYPE(?value)), "") AS ?datatype_iri)
                BIND(IF(isLiteral(?value), LANG(?value), "") AS ?language_tag)
              }
              UNION
              {
                FILTER NOT EXISTS { ?object ?predicate ?missing_value }
                BIND("unbound" AS ?value_kind)
                BIND("" AS ?datatype_iri)
                BIND("" AS ?language_tag)
              }
            }
            GROUP BY ?parent ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag
            """;
        var expressionFactsCount = Wrap(expressionFactsRows);
        var expressionFactsPage = $$"""
            SELECT ?parent ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 ?key_7 WHERE {
              {
            {{Indent(Indent(expressionFactsRows))}}
              }
              BIND(STR(?object) AS ?key_1)
              BIND(STR(?predicate) AS ?key_2)
              BIND(?value_kind AS ?key_3)
              BIND(COALESCE(STR(?value), "") AS ?key_4)
              BIND(?datatype_iri AS ?key_5)
              BIND(?language_tag AS ?key_6)
              BIND(STR(?parent) AS ?key_7)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4 ?last_key_5 ?last_key_6 ?last_key_7) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string} {last_key_5:sparql_string} {last_key_6:sparql_string} {last_key_7:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 > ?last_key_5) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 = ?last_key_5 && ?key_6 > ?last_key_6) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 = ?last_key_5 && ?key_6 = ?last_key_6 && ?key_7 > ?last_key_7)
              )
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 ?key_7
            LIMIT {page_limit:uint}
            """;

        var rootWatermarkRows = $$"""
            SELECT ?object ?value ?value_kind ?datatype_iri ?language_tag WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES ?object {
            {{valuesBlock}}
              }
              {
                ?object <{{WatermarkPredicateIri}}> ?value .
                BIND(IF(isIRI(?value), "iri", IF(isLiteral(?value), "literal", "unsupported_blank_node")) AS ?value_kind)
                BIND(IF(isLiteral(?value), STR(DATATYPE(?value)), "") AS ?datatype_iri)
                BIND(IF(isLiteral(?value), LANG(?value), "") AS ?language_tag)
              }
              UNION
              {
                FILTER NOT EXISTS { ?object <{{WatermarkPredicateIri}}> ?missing_value }
                BIND("unbound" AS ?value_kind)
                BIND("" AS ?datatype_iri)
                BIND("" AS ?language_tag)
              }
            }
            GROUP BY ?object ?value ?value_kind ?datatype_iri ?language_tag
            """;
        var rootWatermarkCount = Wrap(rootWatermarkRows);
        var rootWatermarkPage = $$"""
            SELECT ?object ?value ?value_kind ?datatype_iri ?language_tag ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 WHERE {
              {
            {{Indent(Indent(rootWatermarkRows))}}
              }
              BIND(STR(?object) AS ?key_1)
              BIND(?value_kind AS ?key_2)
              BIND(COALESCE(STR(?value), "") AS ?key_3)
              BIND(?datatype_iri AS ?key_4)
              BIND(?language_tag AS ?key_5)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4 ?last_key_5) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string} {last_key_5:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 > ?last_key_5)
              )
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5
            LIMIT {page_limit:uint}
            """;

        // Family M. The two-hop path and the FILTER NOT EXISTS absence branch below are the exact
        // query shape probed live against the publisher endpoint on 2026-09-04 under User-Agent
        // Lex/0.1: 200 with five grouped rows (fmx4, pdf, pdfa1a, print, xhtml) for CELEX
        // 32008R0593, and 200 with exactly one value_kind="unbound" row for a well-formed Cellar
        // IRI the store holds no manifestation for.
        var manifestationFactsRows = $$"""
            SELECT ?parent ?value ?value_kind ?datatype_iri ?language_tag WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES ?parent {
            {{valuesBlock}}
              }
              {
                ?listed_expression <{{CdmIri(EuCdmPredicate.ExpressionBelongsToWork)}}> ?parent .
                ?listed_manifestation <{{ManifestsExpressionPredicateIri}}> ?listed_expression .
                ?listed_manifestation <{{ManifestationTypePredicateIri}}> ?value .
                BIND(IF(isIRI(?value), "iri", IF(isLiteral(?value), "literal", "unsupported_blank_node")) AS ?value_kind)
                BIND(IF(isLiteral(?value), STR(DATATYPE(?value)), "") AS ?datatype_iri)
                BIND(IF(isLiteral(?value), LANG(?value), "") AS ?language_tag)
              }
              UNION
              {
                FILTER NOT EXISTS {
                  ?absent_expression <{{CdmIri(EuCdmPredicate.ExpressionBelongsToWork)}}> ?parent .
                  ?absent_manifestation <{{ManifestsExpressionPredicateIri}}> ?absent_expression .
                  ?absent_manifestation <{{ManifestationTypePredicateIri}}> ?absent_value .
                }
                BIND("unbound" AS ?value_kind)
                BIND("" AS ?datatype_iri)
                BIND("" AS ?language_tag)
              }
            }
            GROUP BY ?parent ?value ?value_kind ?datatype_iri ?language_tag
            """;
        var manifestationFactsCount = Wrap(manifestationFactsRows);
        var manifestationFactsPage = $$"""
            SELECT ?parent ?value ?value_kind ?datatype_iri ?language_tag ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 WHERE {
              {
            {{Indent(Indent(manifestationFactsRows))}}
              }
              BIND(STR(?parent) AS ?key_1)
              BIND(?value_kind AS ?key_2)
              BIND(COALESCE(STR(?value), "") AS ?key_3)
              BIND(?datatype_iri AS ?key_4)
              BIND(?language_tag AS ?key_5)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4 ?last_key_5) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string} {last_key_5:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 && ?key_5 > ?last_key_5)
              )
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5
            LIMIT {page_limit:uint}
            """;

        return (
            Normalize(objectFactsCount), Normalize(objectFactsPage),
            Normalize(expressionFactsCount), Normalize(expressionFactsPage),
            Normalize(rootWatermarkCount), Normalize(rootWatermarkPage),
            Normalize(manifestationFactsCount), Normalize(manifestationFactsPage));
    }

    private static string Wrap(string rows) => $$"""
        SELECT (COUNT(*) AS ?count) WHERE {
          {
        {{Indent(Indent(rows))}}
          }
        }
        """;

    private static string Indent(string value) => string.Join('\n',
        Normalize(value).Split('\n').Select(static line => "  " + line));

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";

    /// <summary>
    /// The exact CDM local name for each of the thirteen closed predicates, matching
    /// <see cref="EuCdmPredicate"/>'s own <c>JsonStringEnumMemberName</c> tokens. A switch, not a
    /// reused wire-token lookup: the wire token is a serialization concern
    /// (<c>ExactStringEnumConverter</c>), and this is a SPARQL IRI concern that happens to share the
    /// same local name today; collapsing the two would make a future wire-token rename silently
    /// change the SPARQL this plan sends.
    /// </summary>
    internal static string CdmIri(EuCdmPredicate predicate) => Cdm + predicate switch
    {
        EuCdmPredicate.ResourceLegalIdCelex => "resource_legal_id_celex",
        EuCdmPredicate.ExpressionBelongsToWork => "expression_belongs_to_work",
        EuCdmPredicate.ResourceLegalType => "resource_legal_type",
        EuCdmPredicate.WorkHasResourceType => "work_has_resource-type",
        EuCdmPredicate.WorkDateDocument => "work_date_document",
        EuCdmPredicate.ActConsolidatedDate => "act_consolidated_date",
        EuCdmPredicate.DateCreationLegacy => "date_creation_legacy",
        EuCdmPredicate.ResourceLegalInForce => "resource_legal_in-force",
        EuCdmPredicate.ExpressionUsesLanguage => "expression_uses_language",
        EuCdmPredicate.ExpressionTitle => "expression_title",
        EuCdmPredicate.ExpressionTitleShort => "expression_title_short",
        EuCdmPredicate.WorkIsAboutConceptEurovoc => "work_is_about_concept_eurovoc",
        EuCdmPredicate.ResourceLegalIsAboutConceptDirectoryCode =>
            "resource_legal_is_about_concept_directory-code",
        _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
    };

    /// <summary>The exact CDM local name for each of the four read relation families.</summary>
    internal static string RelationIri(EuRelationFamily family) => family switch
    {
        EuRelationFamily.Amends => Cdm + "resource_legal_amends_resource_legal",
        EuRelationFamily.Corrects => Cdm + "resource_legal_corrects_resource_legal",
        EuRelationFamily.BasedOn => Cdm + "resource_legal_based_on_resource_legal",
        EuRelationFamily.ConsolidatedBasedOn => EuConsolidationDiscoveryPlan.BasedOnPredicateIri,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };
}

internal sealed class EuObjectFactsSparqlRenderer : IMachineQueryRenderer
{
    private readonly MachineQueryRendererSource _rendererSource;
    private readonly byte[] _rendererProfileBytes;
    private readonly EuObjectFactsQueryDefinition _definition;
    private readonly bool _isPage;

    internal EuObjectFactsSparqlRenderer(
        EuObjectFactsDiscoveryPlan plan,
        EuObjectFactsQueryDefinition definition,
        bool isPage,
        MachineQueryRendererSource rendererSource)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rendererSource);
        _definition = definition;
        _isPage = isPage;
        RendererProfileRef = plan.ArtifactRef;
        _rendererSource = rendererSource;
        _rendererProfileBytes = plan.CopyCanonicalIdentityBytes();
    }

    public SourceArtifactRef RendererProfileRef { get; }

    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _rendererProfileBytes.ToArray();

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(
        MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet, plan.ResponseCardinality);

    internal MachineQueryRenderOutput RenderInput(
        MachineQueryInputArtifact input, MachineResponseCardinality response)
    {
        var parameters = input.OrderedParameters.ToDictionary(
            static value => value.Name, StringComparer.Ordinal);
        var pass = (EuObjectFactsQueryPass)Integer(parameters, "pass_id");
        var limit = pass switch
        {
            EuObjectFactsQueryPass.Pass1 => EuConsolidationDiscoveryPlan.Pass1PageLimit,
            EuObjectFactsQueryPass.Pass2 => EuConsolidationDiscoveryPlan.Pass2PageLimit,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
        var query = _isPage ? _definition.PageTemplate : _definition.CountTemplate;
        foreach (var name in EuObjectFactsDiscoveryPlan.BatchParameterNames())
        {
            query = Replace(query, "{" + name + ":iri}", SparqlIriTerm(PublisherLiteral(parameters, name)));
        }

        query = Replace(query, "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));

        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody ||
                parameters.Count != EuObjectFactsDiscoveryPlan.BatchCapacity + 1)
            {
                throw new ArgumentException("A count input has one exact shape.", nameof(input));
            }

            return Output(query);
        }

        if (response.Kind != MachineResponseCardinalityKind.BoundedRowSetPage || response.RowLimit != limit)
        {
            throw new ArgumentException("The page limit must come from the pass policy.", nameof(response));
        }

        var hasCursor = Integer(parameters, "has_cursor");
        if (hasCursor is not (0 or 1))
        {
            throw new ArgumentException("Cursor presence must be zero or one.", nameof(input));
        }

        var expectedCount = EuObjectFactsDiscoveryPlan.BatchCapacity + 2 +
            (hasCursor == 1 ? _definition.CursorVariables.Count : 0);
        if (parameters.Count != expectedCount)
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        foreach (var variable in _definition.CursorVariables)
        {
            var value = hasCursor == 0 ? string.Empty : Cursor(parameters, "last_" + variable);
            query = Replace(query, "{last_" + variable + ":sparql_string}",
                SparqlQueryText.StringLiteral(value));
        }

        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        EuConsolidationDiscoveryPlan.PublisherEndpoint, Encoding.UTF8.GetBytes(query));

    private static string SparqlIriTerm(string canonicalIri)
    {
        if (string.IsNullOrEmpty(canonicalIri) ||
            canonicalIri.AsSpan().IndexOfAny('<', '>') >= 0 ||
            canonicalIri.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A batch member is not a safe SPARQL IRI term.");
        }

        return "<" + canonicalIri + ">";
    }

    private static long Integer(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger &&
        value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.");

    private static string PublisherLiteral(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherLiteral &&
        value.TextValue is not null
            ? value.TextValue
            : throw new ArgumentException($"The literal input {name} is missing or invalid.");

    private static string Cursor(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherCursor &&
        value.TextValue is not null
            ? EnumerationCursorEnvelope.Decode(value.TextValue)
            : throw new ArgumentException($"The cursor input {name} is missing or invalid.");

    private static string Replace(string source, string slot, string replacement)
    {
        if (source.Split(slot, StringSplitOptions.None).Length != 2)
        {
            throw new ArgumentException("A renderer slot must occur exactly once.", nameof(source));
        }

        return source.Replace(slot, replacement, StringComparison.Ordinal);
    }
}
