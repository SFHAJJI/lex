using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public sealed record LuxembourgOpinionRequestGraphBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Asks Legilux which properties each <c>OpinionRequest</c> carries, one row per declared value.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS WHERE <c>referralDate</c> LIVES, and the only family that can read it. The draft graph
/// asks every predicate the publisher holds on an <c>InitialDraft</c> and <c>referralDate</c> is
/// not among them - measured over 650 drafts, eighteen predicates, none of them it. So a
/// <c>(draft, referralDate)</c> pair is typed an unresolved gap there, awaiting a traversal that
/// proves the subject role. The subject role was proven by identity on two independent drafts:
/// <c>jolux:hasOpinion</c> reaches <c>/evenement/sace/N</c> resources that carry this class.
/// </para>
/// <para>
/// REQUEST-SCOPED, AND THAT IS AN ARCHITECTURAL DECISION RATHER THAN AN OVERSIGHT. This family
/// never learns which draft reaches which request. The draft edge is not an input, does not appear
/// in the query, and cannot change what is acquired; the traversal belongs to a composite
/// reconciliation citing both proof-bound outputs. Making the edge an input would let a draft-side
/// fact change what this producer asks the publisher.
/// </para>
/// <para>
/// A DELIBERATE MIRROR OF <see cref="LuxembourgDraftGraphDiscoveryPlan"/>, pinned as one. Its
/// batched seven-column shape is the only property sweep in this family measured running to
/// completion against this engine - thirteen batch runs in the retained acceptance packet, and
/// again in the codec validation of 2026-09-11T15:36:49Z, which delivered 115 rows over five
/// subjects. What Legilux refused with <c>Virtuoso SR319</c> was the draft graph's UNBOUNDED sweep,
/// class-wide and seven columns wide, and this is not that: it is bounded by an inventory-issued
/// batch exactly as the proven runs were. The regression beside this file asserts whole-text
/// equality with that plan under only the class, subject and batch-parameter substitutions, because
/// a mirror drifts one fragment at a time while every fragment test still passes.
/// </para>
/// <para>
/// THE PUBLISHER IS ASKED FOR EVERY PREDICATE IT HOLDS, and admission happens locally afterwards.
/// Inherited rationale, not a fresh measurement: the draft graph was measured returning ZERO
/// <c>parliamentDraftUrl</c> rows for fifty drafts that hold them while a <c>VALUES ?predicate</c>
/// block sat in front of the triple, and removing that block is why it can claim completeness at
/// all. This family asks the same way for the same reason, and whether this class would drop rows
/// under a predicate filter is untested because no filter is used.
/// </para>
/// <para>
/// KEY_4 IS THE PUBLISHER'S OWN CURSOR CODEC, NOT SHA-256 OF THE VALUE. Same endpoint, same defect:
/// it hashes the value double UTF-8 encoded, agreeing with the standard on ASCII and diverging on
/// everything else. The producer recomputes through <see cref="LuxembourgPublisherCursorCodec"/> and
/// refuses any row whose delivered key does not describe its own value. Nothing in this family may
/// describe that key as a SHA-256 of the value.
/// </para>
/// <para>
/// WHAT THIS FAMILY MAY CONCLUDE FROM SILENCE. A complete delivery over a subject the publisher
/// itself typed <c>OpinionRequest</c> may derive the absence of an accepted predicate, because such
/// a delivery could have carried it. Nothing weaker earns that, and the delivered <c>rdf:type</c>
/// row is required rather than inferred from the query having filtered on the class: the filter
/// says what was asked, the row says what the publisher answered.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestGraphDiscoveryPlan
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";

    /// <summary>The class this family reads the properties of.</summary>
    /// <remarks>
    /// ALIASED, NEVER RESTATED. A near-copy that spelled its own class IRI is how the inventory
    /// plan was able to name a different, valid class while every one of its guards stayed green.
    /// The accepted coordinate is owned by <see cref="LuxembourgDraftGraphDiscoveryPlan"/>, which
    /// established that <c>referralDate</c> is declared here rather than on the draft.
    /// </remarks>
    public const string OpinionRequestClassIri =
        LuxembourgDraftGraphDiscoveryPlan.OpinionRequestClassIri;

    /// <summary>The date the referral was made, which is what E8 needs from this class.</summary>
    /// <remarks>
    /// Aliased from the family that established where it is declared, for the same reason the class
    /// is. Whether this publisher actually holds it on these resources is UNMEASURED: no reviewed
    /// template could ask until this one, and a delivery returning none is a finding rather than an
    /// acceptance.
    /// </remarks>
    public const string ReferralDatePredicateIri =
        LuxembourgDraftGraphDiscoveryPlan.ReferralDatePredicateIri;

    /// <summary>The type triple, required as delivered evidence of the subject's role.</summary>
    public const string RdfTypePredicateIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    public const string UnboundKind = "unbound";

    /// <summary>How many requests one batch names.</summary>
    /// <remarks>
    /// The proven batch width. Inherited from the draft graph rather than chosen: fifty subjects per
    /// batch is what the thirteen completed batch runs and the codec validation actually sent.
    /// </remarks>
    public const int BatchCapacity = 50;

    internal const long PublisherDeliveryCeilingRows = 1_000_000;

    /// <summary>
    /// The page limits, unequal to each other and to the draft graph's.
    /// </summary>
    /// <remarks>
    /// Two passes at different limits is what makes agreement evidence rather than repetition. Not
    /// shared with the mirrored family, so a cross-family cursor mix-up surfaces as a boundary
    /// disagreement instead of lining up silently. They do not appear in the query text, which binds
    /// the limit as a parameter, so this independence costs the mirror regression nothing.
    /// </remarks>
    internal const uint Pass1PageLimit = 941;

    internal const uint Pass2PageLimit = 563;

    internal const string PartitionMemberKeyPrefix = "legilux-opinion-request-graph-batch-";

    private const string ResourceId = "urn:uuid:3d82f06a-914c-4be7-85f1-2c760ae4d9b3";
    private const string MemberPrefix = "lu-opinion-request-graph";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// The properties this family admits from a row, in the order the identity records them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE ACCEPTED PREDICATE, because E8 needs exactly one thing from this class. The query does
    /// not filter on it - it asks for everything the publisher holds - so a row carrying any other
    /// predicate is delivered, counted and retained as typed evidence rather than dropped. That is
    /// the whole-delivery conservation the reviewer disposition requires: every returned row is
    /// either admitted or retained under a disposition that says why.
    /// </para>
    /// <para>
    /// Adding a predicate here changes the plan's digest, which is the intended cost of widening
    /// what this family claims to know.
    /// </para>
    /// </remarks>
    private static readonly string[] AskedPredicates = [ReferralDatePredicateIri];

    /// <summary>Every predicate this family admits, for a caller that needs to name them.</summary>
    public static IReadOnlyList<string> AskedAbout { get; } = Array.AsReadOnly(AskedPredicates);

    /// <summary>
    /// The predicates admissible from a triple on an <c>OpinionRequest</c>.
    /// </summary>
    /// <remarks>
    /// Every accepted predicate here IS declared on this class, which is the difference from the
    /// draft graph: there, <c>referralDate</c> was accepted but declared elsewhere, so its silence
    /// evidenced nothing and its presence was drift. Here it is at home, so a complete delivery may
    /// derive its absence and a delivered row is a fact. If a predicate accepted by this family is
    /// ever found to be declared on another class, it belongs on a not-declared-here list exactly as
    /// the draft graph has one, and not in this set.
    /// </remarks>
    public static IReadOnlyList<string> DirectlyAdmissiblePredicates { get; } =
        Array.AsReadOnly(AskedPredicates);

    private static readonly string[] Projection =
    [
        "request", "request_kind", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
        "multiplicity",
        "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
    ];

    /// <summary>
    /// The keyset, injective over the grouped row rather than merely plausible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KEY_4 IS THE VALUE'S DIGEST, NOT THE VALUE, and the reason is inherited rather than measured
    /// here. The draft graph's live run stopped at batch 12 of 156 on a real row whose
    /// <c>jolux#titleDraft</c> is 2,648 UTF-8 bytes against a shared key-part ceiling of 2,047:
    /// keyed on the lexical value, that row cannot be keyed at all. Whether this class holds a value
    /// long enough to breach the ceiling is UNMEASURED - no reviewed template has read one - so the
    /// digest is carried because it bounds the key part by construction, not because a long value
    /// has been seen here.
    /// </para>
    /// <para>
    /// The owner's ruling is a digest and not a larger ceiling, a truncation, an exclusion or an
    /// absence. The query asks the endpoint for <c>SHA256(COALESCE(STR(?value), ""))</c>, and what
    /// comes back is NOT the SPARQL 1.1 function of that name: this publisher hashes the value
    /// double UTF-8 encoded, agreeing with the standard on ASCII and diverging on everything else.
    /// The producer therefore recomputes the key through
    /// <see cref="LuxembourgPublisherCursorCodec"/> - a named publisher-profile codec, measured by
    /// rejecting twenty-one competing algorithms - and refuses any row whose delivered key does not
    /// describe its own value. Nothing in this family may call that key a SHA-256 of the value.
    /// The complete raw <c>?value</c> stays projected, retained and decoded whole - the digest keys
    /// the row, it does not replace what the row says. Request, predicate, value-kind, datatype and
    /// language remain independently keyed beside it, so the keyset is still injective over the
    /// grouped row and not merely over the value.
    /// </para>
    /// <para>
    /// A digest is fixed-width, so this bounds the key part by construction rather than by hoping
    /// publisher values stay short. Two distinct full rows colliding onto one canonical key fail
    /// closed through the existing duplicate refusal; nothing deduplicates silently. And if the
    /// endpoint is ever corrected to the standard, local recomputation stops matching and the rows
    /// refuse - drift surfacing as a refusal rather than as a silent change of meaning.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// A request is not unique on its own and neither is a (request, predicate) pair: a multi-valued
    /// property delivers a row per value, and a cursor naming only the request could not advance
    /// past the second. That is not hypothetical for this family - the reviewer disposition requires
    /// every distinct referral date preserved and none chosen, which only means something if two can
    /// arrive. So the value participates — and with it the value's OWN
    /// AUTHORITY, because Source/Core requires canonical keys unique and cursors strictly
    /// increasing, and two literals sharing a lexical form while differing in datatype or language
    /// are different facts that would otherwise share every key.
    /// </para>
    /// <para>
    /// <c>key_3</c>, the predicate, needs no kind key - but no longer for the reason this once gave.
    /// It said the predicate is bound from a VALUES block of IRIs and so is an IRI by construction;
    /// that block is gone, because it was measured dropping rows the publisher holds. The
    /// conclusion survives on RDF itself, where a predicate is always an IRI, and on the producer,
    /// which requires the delivered predicate term to be a readable IRI before it reads anything
    /// else from the row. The request and the value are whatever the publisher delivered, which is
    /// why each still carries its own kind.
    /// </para>
    /// </remarks>
    private static readonly string[] Cursor =
        ["key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7"];

    /// <summary>
    /// How many cursor keys this family has, read by the renderer rather than written twice.
    /// </summary>
    internal static int CursorKeyCount => Cursor.Length;

    /// <summary>The batch member parameter names, in the order they bind.</summary>
    /// <remarks>
    /// They bind BEFORE <c>pass_id</c>, and that is not cosmetic: <c>RequireInputRoleShape</c>
    /// compares <c>SelectionParameterNames.Append(PassParameterName)</c> by sequence, so a selection
    /// appended after the pass would be a different input role from the one the profile declares.
    /// The plan said so in a comment while the selection was empty; this is that comment coming due.
    /// </remarks>
    internal static string[] BatchParameterNames()
    {
        var names = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            names[index] = "batch_request_" + index.ToString("D3", CultureInfo.InvariantCulture);
        }

        return names;
    }

    /// <summary>
    /// One batch as the query will carry it: sorted, deduplicated, and padded to capacity.
    /// </summary>
    /// <remarks>
    /// Sorted and deduplicated HERE rather than trusted, because the partition key digests the
    /// batch's own members and two runs naming the same requests in a different order must mint the
    /// same key. Padding repeats the last member: the <c>VALUES</c> block sits inside a
    /// <c>SELECT DISTINCT</c>, so a repeat asks nothing extra, and a fixed parameter count keeps the
    /// input role identical for every batch including a short final one.
    /// </remarks>
    /// <summary>
    /// The batch as the partition names it: sorted, deduplicated, and NOT padded.
    /// </summary>
    /// <remarks>
    /// The executor verifies every delivered row's own key against this set, so it must contain
    /// exactly the requests asked about. The padding that fills the parameter block is a rendering
    /// detail and would make a repeated member look like a member asked about twice.
    /// </remarks>
    /// <summary>The partition key for one batch, digesting the batch's own members.</summary>
    /// <remarks>
    /// <para>
    /// A CONSTANT KEY HERE WOULD MAKE THE TERMINAL COVER UNIMPLEMENTABLE, and it was one. Every
    /// batch bound the same member key, so every batch minted an enumeration proof with an
    /// identical <c>FamilyKey</c> - which is the delivery's partition key exactly. Batches were
    /// therefore indistinguishable in their own receipts: no omitted batch and no duplicated batch
    /// could be detected from them, and <c>AbsenceCut.Create</c> would refuse any multi-batch cut
    /// outright as a duplicate family.
    /// </para>
    /// <para>
    /// The remarks on <see cref="CanonicalizeAndPad"/> have said the key digests the batch's members
    /// since the batching was written; the code passed a constant. This is that intent, implemented,
    /// and it follows <c>EuObjectFactsDiscoveryPlan.PartitionKeyFor</c>, which does exactly this at
    /// the identical bind site for the same reason.
    /// </para>
    /// <para>
    /// Digested over the sorted, deduplicated, UNPADDED members, so two runs naming the same requests
    /// in a different order mint the same key and a short final batch is not confused with one whose
    /// padding happens to repeat the same tail.
    /// </para>
    /// </remarks>
    public static string PartitionKeyFor(IReadOnlyList<string> batchRequests) =>
        PartitionMemberKeyPrefix + SelectionDigestFor(batchRequests)[..24];

    /// <summary>The full digest of a batch's requested members.</summary>
    /// <remarks>
    /// The whole hash, not the truncated form the partition key carries. An absence cites this: a
    /// key shortened for readability is not the thing to bind evidence to.
    /// </remarks>
    public static string SelectionDigestFor(IReadOnlyList<string> batchRequests) =>
        Sha256(StrictUtf8.GetBytes(string.Join('\n', RequestedPartitionMembers(batchRequests))));

    public static IReadOnlyList<string> RequestedPartitionMembers(IReadOnlyList<string> batchRequests)
    {
        ArgumentNullException.ThrowIfNull(batchRequests);
        return batchRequests
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string[] CanonicalizeAndPad(IReadOnlyList<string> batchRequests)
    {
        ArgumentNullException.ThrowIfNull(batchRequests);
        var ordered = batchRequests
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length is 0 || ordered.Length > BatchCapacity)
        {
            throw new ArgumentException(
                $"A batch names one to {BatchCapacity} requests.", nameof(batchRequests));
        }

        var padded = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            padded[index] = index < ordered.Length ? ordered[index] : ordered[^1];
        }

        return padded;
    }

    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgOpinionRequestGraphDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "lu-opinion-request-graph-plan/1",
            "endpoint=" + LuxembourgQueryPlan.PublisherEndpoint,
            "method=POST",
            "request_media_type=application/x-www-form-urlencoded",
            "response_media_type=" + ResponseMediaType,
            "request_class=" + OpinionRequestClassIri,
            "asked_predicates=" + string.Join(',', AskedPredicates),
            "unbound_kind=" + UnboundKind,
            "batch_capacity=" + BatchCapacity.ToString(CultureInfo.InvariantCulture),
            "partition_member_key_prefix=" + PartitionMemberKeyPrefix,
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + (int)LuxembourgQueryPass.Pass1 + ":" + Pass1PageLimit,
            "pass_2=" + (int)LuxembourgQueryPass.Pass2 + ":" + Pass2PageLimit,
            "terminal_page_policy=short_page_terminal",
            "projection=" + string.Join(',', Projection),
            "canonical_keys=" + string.Join(',', Cursor),
            "cursor=" + string.Join(',', Cursor),
            "count_member=" + MemberPrefix + ".count",
            "page_member=" + MemberPrefix + ".page",
            CountTemplate,
            PageTemplate,
        }));
        ArtifactRef = new SourceArtifactRef(ResourceId, Sha256(_canonicalIdentityBytes));
        CountQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".count");
        PageQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".page");
    }

    public string PublisherEndpoint => LuxembourgQueryPlan.PublisherEndpoint;
    public SourceArtifactRef ArtifactRef { get; }
    public SourceRegistryMemberRef CountQueryFamilyRef { get; }
    public SourceRegistryMemberRef PageQueryFamilyRef { get; }
    public string CountTemplate { get; }
    public string PageTemplate { get; }

    public static LuxembourgOpinionRequestGraphDiscoveryPlan Create() => new();

    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    public RepeatedEnumerationInterpretationProfile CreateDeliveryProfile() => new(
        RepeatedEnumerationInterpretationProfile.SchemaId,
        RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso,
        ResponseMediaType,
        EnumerationCursorEnvelope.Identity,
        PublisherDeliveryCeilingRows,
        ThresholdDetectorIdentity,
        CountQueryFamilyRef,
        PageQueryFamilyRef,
        "count",
        Projection,
        Cursor,
        Cursor,
        BatchParameterNames(),
        "pass_id",
        Cursor.Select(static value => "last_" + value).ToArray(),
        "has_cursor",
        RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    public LuxembourgOpinionRequestGraphBoundQuery BindCount(
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchRequests,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, batchRequests, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public LuxembourgOpinionRequestGraphBoundQuery BindPage(
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchRequests,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, pass, batchRequests, cursor,
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private LuxembourgOpinionRequestGraphBoundQuery Bind(
        bool isPage,
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchRequests,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);

        // THE SELECTION BINDS FIRST, which is the ordering the empty-selection comment on this plan
        // was written to anticipate: RequireInputRoleShape compares
        // SelectionParameterNames.Append(PassParameterName) by sequence.
        //
        // The class is not narrowed by the batch. The members come from a proven inventory of the
        // whole class, so this is a partition of the sweep rather than a caller's subset - which is
        // the distinction the owner ruling turns on.
        var padded = CanonicalizeAndPad(batchRequests);
        var names = BatchParameterNames();
        var parameters = new List<MachineQueryParameter>(BatchCapacity + 2 + Cursor.Length);
        for (var index = 0; index < BatchCapacity; index++)
        {
            parameters.Add(new MachineQueryParameter(
                names[index], MachineQueryParameterKind.PublisherLiteral,
                null, padded[index], ArtifactRef));
        }

        parameters.Add(
            new MachineQueryParameter(
                "pass_id", MachineQueryParameterKind.BoundedInteger, (int)pass, null, ArtifactRef));

        if (isPage)
        {
            var values = cursor?.ToArray() ?? [];
            if (values.Length != 0 && values.Length != Cursor.Length)
            {
                throw new ArgumentException(
                    $"A continuation cursor must have {Cursor.Length} exact parts.", nameof(cursor));
            }

            parameters.Add(new MachineQueryParameter(
                "has_cursor", MachineQueryParameterKind.BoundedInteger,
                values.Length == 0 ? 0 : 1, null, ArtifactRef));
            for (var index = 0; index < values.Length; index++)
            {
                parameters.Add(new MachineQueryParameter(
                    "last_" + Cursor[index], MachineQueryParameterKind.PublisherCursor,
                    null, EnumerationCursorEnvelope.Encode(values[index]), ArtifactRef));
            }
        }

        var family = isPage ? PageQueryFamilyRef : CountQueryFamilyRef;
        var input = MachineQueryInputArtifact.Create(
            inputResourceId, family, PartitionKeyFor(batchRequests), response, parameters);
        var renderer = new LuxembourgOpinionRequestGraphSparqlRenderer(this, isPage, rendererSource);
        var rendered = renderer.RenderInput(input, response);
        var body = rendered.CopyRequestBody();
        var targetBytes = Encoding.ASCII.GetBytes("/sparqlendpoint");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            family,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Post,
            LuxembourgQueryPlan.PublisherEndpoint,
            targetBytes.LongLength,
            Sha256(targetBytes),
            response,
            new SourceRegistryMemberRef(ArtifactRef, "application/x-www-form-urlencoded"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));
        var machinePlanRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        return new(machinePlan, machinePlanRef, input,
            MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer));
    }

    internal static uint PageLimit(LuxembourgQueryPass pass) => pass switch
    {
        LuxembourgQueryPass.Pass1 => Pass1PageLimit,
        LuxembourgQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    /// <summary>
    /// The keyset continuation filter, derived from <see cref="Cursor"/> rather than written out.
    /// </summary>
    /// <remarks>
    /// Seven keys make this comparison seven clauses deep, and a hand-written one would be seven
    /// chances to transpose a key. It is generated from the same array the projection, the ORDER BY
    /// and the bound parameters come from.
    /// </remarks>
    private static string KeysetFilter()
    {
        var clauses = new List<string>();
        for (var index = 0; index < Cursor.Length; index++)
        {
            var parts = new List<string>();
            for (var earlier = 0; earlier < index; earlier++)
            {
                parts.Add($"?{Cursor[earlier]} = ?last_{Cursor[earlier]}");
            }

            parts.Add($"?{Cursor[index]} > ?last_{Cursor[index]}");
            clauses.Add(parts.Count == 1 ? parts[0] : "(" + string.Join(" && ", parts) + ")");
        }

        return string.Join(" ||\n    ", clauses);
    }

    private static string AllKeysEqual() => string.Join(
        " && ", Cursor.Select(static key => $"?{key} = ?last_{key}"));

    private static (string Count, string Page) BuildTemplates()
    {
        var grouped = "?request ?request_kind ?predicate ?value ?value_kind ?datatype_iri ?language_tag";

        // THE PUBLISHER IS ASKED FOR EVERY PREDICATE IT HOLDS ABOUT THESE SUBJECTS, and admission
        // to the E8 vocabulary happens locally afterwards. That is not a widening of scope; it is
        // the only shape whose completeness can be checked.
        //
        // INHERITED, NOT MEASURED HERE, and the distinction matters. The draft graph was measured
        // returning ZERO parliamentDraftUrl rows for fifty drafts that hold them while a
        // `VALUES ?predicate` block sat in front of its triple; the same drafts asked with a free
        // predicate variable returned them, the IRI in the block byte-identical to the one that
        // came back. Every dropped pair was being minted as a derived absence - a typed record
        // asserting the publisher holds nothing where it holds something. Removing that block is
        // why that family can claim completeness at all.
        //
        // This family asks the same way for the same reason. Whether THIS class would drop rows
        // under a predicate filter is untested, because no filter is used and none will be added
        // to find out.
        //
        // A complete enumeration proof does not make an enumeration complete. It proves the pages
        // arrived whole; it cannot prove the question asked for everything. Asking without a
        // predicate filter removes the only step that could drop a triple before we ever see it.
        //
        // THIS QUERY ASKS THE PUBLISHER FOR PRESENT FACTS ONLY, and the gap is derived afterwards
        // rather than requested. Also inherited: asking this engine to MATERIALISE the absent case
        // was sent three times against the draft graph and never served - ~49s and a read timeout,
        // ordered and unordered, and again as constant-predicate UNION branches the engine could
        // index - while the same subjects asked for present facts answered in 6.7 seconds. This
        // engine will enumerate what it holds; it will not enumerate what it does not.
        //
        // And asking it to was the wrong question anyway. A row the publisher returns is something
        // the publisher SAID. There is no triple behind an absent pair, so a row claiming to be one
        // is our inference wearing the publisher's clothes - which is what S2-A01 forbids. The gap
        // is real and must stay first-class (S2-A03), but it is OURS to state: derived from a
        // complete enumeration, citing that enumeration, and typed so no reader can mistake it for
        // an assertion. So the absent case leaves this query entirely.
        //
        // A MULTI-VALUED PROPERTY STILL DELIVERS EVERY VALUE. The mandatory triple yields one row
        // per value, so a request carrying two referral dates is two rows. The reviewer disposition
        // requires every distinct one preserved and none chosen, which is only meaningful because
        // the mirrored family measured exactly this shape:
        // pl/2000/119 does exactly that, and those eight extra rows are the whole of the difference
        // between the 103 rows delivered and the 95 distinct pairs they cover.
        //
        // NO COLUMN HERE IS COALESCEd, AND THAT IS MEASURED ON THIS PUBLISHER RATHER THAN ASSUMED.
        // The eager-IF raise this family guards against elsewhere comes from dereferencing an
        // UNBOUND variable; the mandatory triple removes that cause outright. Applying DATATYPE or
        // LANG to a BOUND term of the wrong type is a different case, and Legilux does not raise on
        // it: the 103-row delivery retained under #417 was produced by exactly these four BINDs,
        // un-COALESCEd, and carried datatype_iri and language_tag in every one of its 103 rows -
        // all of them IRI-valued.
        //
        // COALESCE here would not merely be redundant, it would DESTROY a fact the decoder needs.
        // This engine will not answer DATATYPE() with rdf:langString, so on a language-tagged
        // literal that BIND errors and the column drops out of the binding - the one measured
        // absence ReadQualifier admits, and the thing that lets it tell a language-tagged literal
        // from a plain one. Swallowing the raise into "" would make that branch unreachable and
        // erase the distinction #532 was repaired to preserve. Only the KEYS are totalised.
        var batchValues = string.Join('\n', BatchParameterNames()
            .Select(static name => "      {" + name + ":iri}"));

        var rows = $$"""
            SELECT {{grouped}} (COUNT(*) AS ?multiplicity) WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              {
                SELECT DISTINCT ?request WHERE {
                  VALUES ?request {
            {{batchValues}}
                  }
                }
              }
              ?request a <{{OpinionRequestClassIri}}> .
              ?request ?predicate ?value .
              BIND(IF(isIRI(?request), "iri", "unsupported_blank_node") AS ?request_kind)
              BIND(IF(isIRI(?value), "iri", IF(isLiteral(?value), "literal", "unsupported_blank_node")) AS ?value_kind)
              BIND(IF(isLiteral(?value), STR(DATATYPE(?value)), "") AS ?datatype_iri)
              BIND(IF(isLiteral(?value), LANG(?value), "") AS ?language_tag)
            }
            GROUP BY {{grouped}}
            """;

        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;

        var projected = string.Join(' ', Projection.Select(static name => "?" + name));
        var lastNames = string.Join(' ', Cursor.Select(static key => "?last_" + key));
        var lastSlots = string.Join(' ', Cursor.Select(static key => "{last_" + key + ":sparql_string}"));
        var order = string.Join(' ', Cursor.Select(static key => "?" + key));

        var page = $$"""
            SELECT {{projected}} WHERE {
              {
            {{Indent(Indent(rows))}}
              }
              BIND(STR(?request) AS ?key_1)
              BIND(?request_kind AS ?key_2)
              BIND(STR(?predicate) AS ?key_3)
              BIND(SHA256(COALESCE(STR(?value), "")) AS ?key_4)
              BIND(?value_kind AS ?key_5)
              BIND(COALESCE(?datatype_iri, "") AS ?key_6)
              BIND(COALESCE(?language_tag, "") AS ?key_7)
              VALUES (?has_cursor {{lastNames}}) {
                ({has_cursor:uint} {{lastSlots}})
              }
              FILTER(
                ?has_cursor = 0 ||
            {{Indent(Indent(KeysetFilter()))}}
              )
              FILTER(?has_cursor = 0 || !({{AllKeysEqual()}}))
            }
            ORDER BY {{order}}
            LIMIT {page_limit:uint}
            """;

        return (Normalize(count), Normalize(page));
    }

    private static string Indent(string value) => string.Join('\n',
        Normalize(value).Split('\n').Select(static line => "  " + line));

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));
}

/// <summary>
/// Renders one bound draft-graph request. Every slot is filled from the ordered parameter set and
/// each must occur exactly once, so a template edit that drops or duplicates a slot fails loudly
/// rather than asking a silently different question.
/// </summary>
internal sealed class LuxembourgOpinionRequestGraphSparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgOpinionRequestGraphDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgOpinionRequestGraphSparqlRenderer(
        LuxembourgOpinionRequestGraphDiscoveryPlan plan,
        bool isPage,
        MachineQueryRendererSource rendererSource)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _isPage = isPage;
        _rendererSource = rendererSource ?? throw new ArgumentNullException(nameof(rendererSource));
    }

    public SourceArtifactRef RendererProfileRef => _plan.ArtifactRef;
    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;
    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _plan.CopyCanonicalIdentityBytes();
    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet, plan.ResponseCardinality);

    internal MachineQueryRenderOutput RenderInput(
        MachineQueryInputArtifact input,
        MachineResponseCardinality response)
    {
        var parameters = input.OrderedParameters.ToDictionary(static value => value.Name, StringComparer.Ordinal);
        var pass = (LuxembourgQueryPass)Integer(parameters, "pass_id");
        var limit = LuxembourgOpinionRequestGraphDiscoveryPlan.PageLimit(pass);
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));

        // Every batch slot is filled from the ordered parameter set, and Replace requires each to
        // occur exactly once - so a template that dropped or duplicated a member fails here rather
        // than asking the publisher about a different set of requests than the input names.
        foreach (var name in LuxembourgOpinionRequestGraphDiscoveryPlan.BatchParameterNames())
        {
            query = Replace(query, "{" + name + ":iri}", SparqlIriTerm(Literal(parameters, name)));
        }

        var batchCount = LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity;
        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody ||
                parameters.Count != 1 + batchCount)
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

        if (parameters.Count != 2 + batchCount +
            (hasCursor == 1 ? LuxembourgOpinionRequestGraphDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= LuxembourgOpinionRequestGraphDiscoveryPlan.CursorKeyCount; ordinal++)
        {
            var name = "last_key_" + ordinal;
            var value = hasCursor == 0 ? string.Empty : Cursor(parameters, name);
            query = Replace(query, "{" + name + ":sparql_string}", LuxembourgQueryText.SparqlString(value));
        }

        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        LuxembourgQueryPlan.PublisherEndpoint,
        Encoding.UTF8.GetBytes("query=" + Uri.EscapeDataString(query)));

    /// <summary>One batch member, as the publisher literal the input carries it as.</summary>
    private static string Literal(
        IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherLiteral && value.TextValue is not null
            ? value.TextValue
            : throw new ArgumentException($"The batch input {name} is missing or invalid.");

    /// <summary>
    /// One batch member rendered as a SPARQL IRI term.
    /// </summary>
    /// <remarks>
    /// An IRI cannot be embedded free-form in query text, which is why every member travels as its
    /// own parameter and is rendered here rather than concatenated at the caller.
    /// </remarks>
    private static string SparqlIriTerm(string canonicalIri)
    {
        // Guarded rather than trusted, matching the sibling transposition renderer: a member
        // carrying an angle bracket or whitespace would close this term early and change the query
        // into one nobody wrote. The members come from a proven inventory, which is a reason to
        // expect them well formed and not a reason to skip checking.
        if (string.IsNullOrEmpty(canonicalIri) ||
            canonicalIri.AsSpan().IndexOfAny('<', '>') >= 0 ||
            canonicalIri.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A batch member is not a safe SPARQL IRI term.");
        }

        return "<" + canonicalIri + ">";
    }

    private static long Integer(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.");

    private static string Cursor(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherCursor && value.TextValue is not null
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
