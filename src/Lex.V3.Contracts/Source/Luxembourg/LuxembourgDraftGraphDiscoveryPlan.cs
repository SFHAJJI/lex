using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public sealed record LuxembourgDraftGraphBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Asks Legilux which properties each <c>InitialDraft</c> carries, one row per declared value.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE PRODUCTION PATH <see cref="LuxembourgDraftRelationPredicate"/> HAS NEVER HAD. That
/// vocabulary was accepted on its own and reached by nothing: no query asked for
/// <c>draftTransposes</c>, so a predicate this repository declares was one the publisher was never
/// asked about. A closed vocabulary reached by nothing covers nothing, which is what S2-A01 requires
/// of publisher assertions, and it leaves "no draft transposes this directive" indistinguishable
/// from "nobody asked", which is the false absence S2-A03 forbids.
/// </para>
/// <para>
/// ONE FAMILY, NOT TWO, AND THE AUTHORITY SAYS SO. <c>draftTransposes</c> is one of
/// <c>InitialDraft</c>'s own properties — <c>baseline/pack/24-research-users.md:62</c> lists it
/// among titleDraft, statusDraft, parliamentDraftUrl, referralDate, hasResultingLegalResource and
/// the rest — so the question-catalogue's row 56 and row 60 are the same node asked two ways.
/// Building <c>draftTransposes</c> alone would have produced a family whose node type is asked for
/// by nothing, which is the defect this plan exists to end rather than repeat.
/// </para>
/// <para>
/// THE SHAPE IS THE OBJECT-FACTS SHAPE, deliberately, rather than the sibling opinion plan's. A
/// draft carries several heterogeneous properties, and one UNION pair per property multiplies the
/// query without bound. <see cref="EuObjectFactsDiscoveryPlan"/>'s
/// <c>(node, predicate, value, value_kind, datatype_iri, language_tag)</c> row already carries every
/// term's authority, which is the rule two review rounds settled on the procedure-event plan: a term
/// delivered in object position carries its kind, its datatype AND its language, because two
/// literals can share a lexical form and still be different facts.
/// </para>
/// <para>
/// THE VALUE-DERIVED COLUMNS ARE TOTALISED AND <c>draft_kind</c> IS NOT, which is the optional
/// shape deciding it rather than a preference. <c>OPTIONAL</c> leaves <c>?value</c> unbound for a
/// pair the publisher holds nothing for, and this engine evaluates <c>IF</c>'s arguments EAGERLY, so
/// every BIND that dereferences it raises on exactly the rows the absence branch exists to deliver -
/// and an erroring BIND leaves its variable out of the binding entirely. Without COALESCE the
/// unbound rows would arrive missing the very marker that says they are unbound. <c>?draft</c> is
/// bound by the batch and the class triple on every row, so its marker needs no such treatment.
/// </para>
/// <para>
/// EVERY DERIVED CURSOR KEY IS TOTALISED WITH COALESCE, for two separately measured reasons, and
/// neither is stylistic.
/// </para>
/// <para>
/// The first is the value key. <see cref="EuObjectFactsDiscoveryPlan"/> records the probe: this
/// engine selects IF's branch correctly and evaluates the arguments EAGERLY, so <c>STR</c> on the
/// unbound term raised, the erroring BIND left the key unbound, and SPARQL's JSON omitted it from 8
/// of 41 rows.
/// </para>
/// <para>
/// The second is the qualifier keys, and it is a DIFFERENT cause with the same symptom.
/// <c>EuPageDecodeClassificationTests</c> retains the page: for a language-tagged literal this
/// engine does not answer <c>DATATYPE()</c> with <c>rdf:langString</c> as SPARQL 1.1 specifies, so
/// that BIND errors and leaves <c>datatype_iri</c> unbound — and any key derived from it unbound
/// with it. 32 of 373 rows on that page were the shape. The COLUMN is therefore left as the
/// publisher answers it, absent and all, exactly as the EU family leaves it; only the KEY is made
/// total, so the keyset is never short a component.
/// </para>
/// <para>
/// Nothing is lost by <c>datatype_iri</c> reading empty for a language-tagged literal, because
/// <c>language_tag</c> is non-empty for precisely those and empty for a plain one. The pair is
/// unambiguous where either alone would not be.
/// </para>
/// <para>
/// WHAT IS NOT MODELLED HERE, and each omission is a decision rather than an oversight.
/// <c>statusDraft</c>'s four observed values are not a closed enum: the accepted E8 line's own
/// instruction to the sibling family is to tolerate mixed vocabularies, and pinning a value set from
/// a research note is how source drift becomes a silent refusal (S2-A05). No count is pinned either
/// — <c>baseline/pack/38-verified-claims.md:12</c> says those figures must be treated as a
/// hypothesis, the endpoint having answered 502 when they were re-checked.
/// </para>
/// <para>
/// <c>parliamentDraftUrl</c> VALUES ARE RETAINED AS TEXT AND NEVER FETCHED. They name pages on
/// <c>www.chd.lu</c>, a host this pipeline does not contact. Retaining a URL is not requesting one,
/// and no part of this family dereferences it; a slice that did would be reaching a new host class.
/// </para>
/// </remarks>
public sealed class LuxembourgDraftGraphDiscoveryPlan
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";

    /// <summary>The JOLux class of a legislative draft.</summary>
    /// <remarks>
    /// Already admitted as a resource class by <c>LuxembourgScopeResolver</c> and
    /// <c>LuxembourgSourceProfile</c>; what has never existed is a query that asks for it.
    /// </remarks>
    public const string InitialDraftClassIri = Jolux + "InitialDraft";

    /// <summary>The draft's own status token.</summary>
    public const string StatusDraftPredicateIri = Jolux + "statusDraft";

    /// <summary>The Chambre des Deputes dossier page for the draft. Retained as a link, never fetched.</summary>
    public const string ParliamentDraftUrlPredicateIri = Jolux + "parliamentDraftUrl";

    /// <summary>The date the draft was referred.</summary>
    public const string ReferralDatePredicateIri = Jolux + "referralDate";

    /// <summary>The enacted act a draft became, where it became one.</summary>
    /// <remarks>
    /// Distinct from <c>hasResultingOpinionDocument</c>, which is a different predicate on a
    /// different class and whose count is adjacent enough to invite the confusion. This one links a
    /// draft to an act; that one links an opinion to its PDF.
    /// </remarks>
    public const string ResultingLegalResourcePredicateIri = Jolux + "hasResultingLegalResource";

    /// <summary>The draft-to-EU-act transposition intention.</summary>
    /// <remarks>
    /// The predicate <see cref="LuxembourgDraftRelationPredicate.DraftTransposes"/> declares. A
    /// draft that proposes to transpose a directive has transposed nothing; that separation is the
    /// whole reason the draft-graph vocabulary is disjoint from the final-law one.
    /// </remarks>
    public const string DraftTransposesPredicateIri = Jolux + "draftTransposes";

    /// <summary>The marker a row carries when the publisher holds no value for that property.</summary>
    public const string UnboundKind = "unbound";

    /// <summary>
    /// How many drafts one request may name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT A TUNING CONSTANT. Every batch member travels as its own <c>publisher_literal</c>
    /// <see cref="MachineQueryParameter"/> and <c>MachineQueryValidation.MaximumParameterCount</c>
    /// is 64. This family always spends one on <c>pass_id</c>, one on <c>has_cursor</c> and up to
    /// seven on cursor continuation, so 55 remain; 50 keeps the same margin the EU object-facts
    /// batch keeps, and reading the arithmetic here rather than restating it means a batch that
    /// could not be bound cannot be minted.
    /// </para>
    /// <para>
    /// The class sweep is not narrowed by this. The batch is a PARTITION of the class, and which
    /// drafts are in it comes from a proven inventory of the whole class rather than from a caller
    /// choosing a subset.
    /// </para>
    /// </remarks>
    public const int BatchCapacity = 50;

    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 953;
    internal const uint Pass2PageLimit = 571;
    internal const string PartitionMemberKey = "legilux-initial-draft-graph";

    private const string ResourceId = "urn:uuid:7c9e2f4b-18a6-4d05-b3e7-52f0a91c6d84";
    private const string MemberPrefix = "lu-initial-draft-graph";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// The properties this family asks each draft for, in the exact order the query binds them.
    /// </summary>
    /// <remarks>
    /// Closed and ordered, because the order is part of the canonical identity and therefore part of
    /// what a reviewer can compare. A property added here changes the plan's digest, which is the
    /// intended cost of widening what this family asks about.
    /// </remarks>
    private static readonly string[] AskedPredicates =
    [
        StatusDraftPredicateIri,
        ParliamentDraftUrlPredicateIri,
        ReferralDatePredicateIri,
        ResultingLegalResourcePredicateIri,
        DraftTransposesPredicateIri,
    ];

    /// <summary>Every predicate this family asks about, for a caller that needs to name them.</summary>
    public static IReadOnlyList<string> AskedAbout { get; } = Array.AsReadOnly(AskedPredicates);

    private static readonly string[] Projection =
    [
        "draft", "draft_kind", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
        "multiplicity",
        "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
    ];

    /// <summary>
    /// The keyset, injective over the grouped row rather than merely plausible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A draft is not unique on its own and neither is a (draft, predicate) pair: a draft carrying
    /// two transposition targets delivers a row for each, and a cursor naming only the draft could
    /// not advance past the second. So the value participates — and with it the value's OWN
    /// AUTHORITY, because Source/Core requires canonical keys unique and cursors strictly
    /// increasing, and two literals sharing a lexical form while differing in datatype or language
    /// are different facts that would otherwise share every key.
    /// </para>
    /// <para>
    /// <c>key_3</c>, the predicate, needs no kind key: it is bound from a VALUES block of IRIs, so it
    /// is an IRI by construction rather than by hope. The draft and the value are whatever the
    /// publisher delivered, which is why each carries its own.
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
            names[index] = "batch_draft_" + index.ToString("D3", CultureInfo.InvariantCulture);
        }

        return names;
    }

    /// <summary>
    /// One batch as the query will carry it: sorted, deduplicated, and padded to capacity.
    /// </summary>
    /// <remarks>
    /// Sorted and deduplicated HERE rather than trusted, because the partition key digests the
    /// batch's own members and two runs naming the same drafts in a different order must mint the
    /// same key. Padding repeats the last member: the <c>VALUES</c> block sits inside a
    /// <c>SELECT DISTINCT</c>, so a repeat asks nothing extra, and a fixed parameter count keeps the
    /// input role identical for every batch including a short final one.
    /// </remarks>
    /// <summary>
    /// The batch as the partition names it: sorted, deduplicated, and NOT padded.
    /// </summary>
    /// <remarks>
    /// The executor verifies every delivered row's own key against this set, so it must contain
    /// exactly the drafts asked about. The padding that fills the parameter block is a rendering
    /// detail and would make a repeated member look like a member asked about twice.
    /// </remarks>
    public static IReadOnlyList<string> RequestedPartitionMembers(IReadOnlyList<string> batchDrafts)
    {
        ArgumentNullException.ThrowIfNull(batchDrafts);
        return batchDrafts
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string[] CanonicalizeAndPad(IReadOnlyList<string> batchDrafts)
    {
        ArgumentNullException.ThrowIfNull(batchDrafts);
        var ordered = batchDrafts
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length is 0 || ordered.Length > BatchCapacity)
        {
            throw new ArgumentException(
                $"A batch names one to {BatchCapacity} drafts.", nameof(batchDrafts));
        }

        var padded = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            padded[index] = index < ordered.Length ? ordered[index] : ordered[^1];
        }

        return padded;
    }

    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgDraftGraphDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "lu-initial-draft-graph-plan/1",
            "endpoint=" + LuxembourgQueryPlan.PublisherEndpoint,
            "method=POST",
            "request_media_type=application/x-www-form-urlencoded",
            "response_media_type=" + ResponseMediaType,
            "draft_class=" + InitialDraftClassIri,
            "asked_predicates=" + string.Join(',', AskedPredicates),
            "unbound_kind=" + UnboundKind,
            "batch_capacity=" + BatchCapacity.ToString(CultureInfo.InvariantCulture),
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

    public static LuxembourgDraftGraphDiscoveryPlan Create() => new();

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

    public LuxembourgDraftGraphBoundQuery BindCount(
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchDrafts,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, batchDrafts, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public LuxembourgDraftGraphBoundQuery BindPage(
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchDrafts,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, pass, batchDrafts, cursor,
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private LuxembourgDraftGraphBoundQuery Bind(
        bool isPage,
        LuxembourgQueryPass pass,
        IReadOnlyList<string> batchDrafts,
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
        var padded = CanonicalizeAndPad(batchDrafts);
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
            inputResourceId, family, PartitionMemberKey, response, parameters);
        var renderer = new LuxembourgDraftGraphSparqlRenderer(this, isPage, rendererSource);
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
        var grouped = "?draft ?draft_kind ?predicate ?value ?value_kind ?datatype_iri ?language_tag";

        // THE PUBLISHER IS ASKED FOR EVERY PREDICATE IT HOLDS ABOUT THESE SUBJECTS, and admission
        // to the E8 vocabulary happens locally afterwards. That is not a widening of scope; it is
        // the only shape whose completeness can be checked.
        //
        // MEASURED, AND IT IS WHY THIS CHANGED. With `VALUES ?predicate { <five IRIs> }` in front of
        // the triple, Legilux returned ZERO parliamentDraftUrl rows for fifty drafts. The same ten
        // of those drafts, asked with a free predicate variable, return TEN of them - the IRI in the
        // VALUES block byte-identical to the one that comes back, the subjects the same, the class
        // triple satisfied for them since their statusDraft arrives either way. The predicate VALUES
        // was silently dropping rows the publisher holds, and every one of those pairs was being
        // minted as a derived absence: a typed record asserting the publisher holds no parliament
        // URL for a draft that has one.
        //
        // A complete enumeration proof does not make an enumeration complete. It proves the pages
        // arrived whole; it cannot prove the question asked for everything. Asking without a
        // predicate filter removes the only step that could drop a triple before we ever see it,
        // and the accepted-predicate projection of the broad delivery reproduces the retained
        // predicate census over the same subjects exactly.
        //
        // THIS QUERY ASKS THE PUBLISHER FOR PRESENT FACTS ONLY, and the gap is derived afterwards
        // rather than requested. Both halves of that were forced, and by different things.
        //
        // Asking the publisher to MATERIALISE the absent case does not work. An OPTIONAL emitting a
        // row per (draft, predicate) pair was sent three times and never served: ~49s and a read
        // timeout, ordered and unordered alike, and again as five constant-predicate UNION branches
        // whose predicates the engine could index. The same fifty drafts asked for present facts
        // answer in 6.7 seconds. This engine will enumerate what it holds; it will not enumerate
        // what it does not.
        //
        // And asking it to was the wrong question anyway. A row the publisher returns is something
        // the publisher SAID. There is no triple behind an absent pair, so a row claiming to be one
        // is our inference wearing the publisher's clothes - which is what S2-A01 forbids. The gap
        // is real and must stay first-class (S2-A03), but it is OURS to state: derived from a
        // complete enumeration, citing that enumeration, and typed so no reader can mistake it for
        // an assertion. So the absent case leaves this query entirely.
        //
        // A MULTI-VALUED PROPERTY STILL DELIVERS EVERY VALUE. The mandatory triple yields one row
        // per value, so a draft transposing nine directives is nine rows - measured, not supposed:
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
                SELECT DISTINCT ?draft WHERE {
                  VALUES ?draft {
            {{batchValues}}
                  }
                }
              }
              ?draft a <{{InitialDraftClassIri}}> .
              ?draft ?predicate ?value .
              BIND(IF(isIRI(?draft), "iri", "unsupported_blank_node") AS ?draft_kind)
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
              BIND(STR(?draft) AS ?key_1)
              BIND(?draft_kind AS ?key_2)
              BIND(STR(?predicate) AS ?key_3)
              BIND(COALESCE(STR(?value), "") AS ?key_4)
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
internal sealed class LuxembourgDraftGraphSparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgDraftGraphDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgDraftGraphSparqlRenderer(
        LuxembourgDraftGraphDiscoveryPlan plan,
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
        var limit = LuxembourgDraftGraphDiscoveryPlan.PageLimit(pass);
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));

        // Every batch slot is filled from the ordered parameter set, and Replace requires each to
        // occur exactly once - so a template that dropped or duplicated a member fails here rather
        // than asking the publisher about a different set of drafts than the input names.
        foreach (var name in LuxembourgDraftGraphDiscoveryPlan.BatchParameterNames())
        {
            query = Replace(query, "{" + name + ":iri}", SparqlIriTerm(Literal(parameters, name)));
        }

        var batchCount = LuxembourgDraftGraphDiscoveryPlan.BatchCapacity;
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
            (hasCursor == 1 ? LuxembourgDraftGraphDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= LuxembourgDraftGraphDiscoveryPlan.CursorKeyCount; ordinal++)
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
