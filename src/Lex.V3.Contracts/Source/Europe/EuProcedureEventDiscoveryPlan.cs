using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

public enum EuProcedureEventQueryPass
{
    Pass1 = 1,
    Pass2 = 2,
}

/// <summary>One bound procedure-event request: the plan, its input artifact and the request.</summary>
public sealed record EuProcedureEventBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Asks which legislative procedure events belong to a batch of dossiers, and what the publisher
/// declared about each: every type it carries, and its date.
/// </summary>
/// <remarks>
/// <para>
/// ONE ROW PER DECLARED TYPE, NEVER A CONCATENATION. <see cref="EuProcedureEventObservation"/>
/// retains every type the publisher stated and refuses the whole observation when any one of them is
/// not an IRI — that refusal only means something if each term arrives as its own term. A
/// <c>GROUP_CONCAT</c> would hand the decoder one string and destroy exactly the per-term authority
/// the contract exists to keep, and the defect it was written against — a row declaring
/// <c>[event_legal, "not-an-iri"]</c> delivered as though <c>event_legal</c> were the only type —
/// would become undetectable. So <c>?event a ?event_type</c> is left to multiply the rows and the
/// producer groups them back by event.
/// </para>
/// <para>
/// THE FAMILY IS ASKED ABOUT DOSSIERS, NOT SWEPT. The authority records 248,683 events corpus-wide,
/// which is not an enumeration anyone finishes, and an event names its dossier rather than the
/// reverse — so the question is asked inversely over a caller-named batch, exactly as the case-law
/// family is asked about a batch of acts. That figure is the authority's own and is not measured
/// here.
/// </para>
/// <para>
/// THE BATCH IS DEDUPLICATED BEFORE IT REACHES THE JOIN. SPARQL solution mappings are a MULTISET:
/// a <c>VALUES</c> block listing one dossier fifty times joins fifty times and
/// <c>COUNT(*)</c> counts every one of them. This seat asserted the opposite once, in a docstring, a
/// commit message and a test comment at the same time, and was shown the measurement. The
/// <c>SELECT DISTINCT ?dossier</c> subquery is what makes the padding a padding rather than a
/// multiplier.
/// </para>
/// <para>
/// ABSENCE IS ASKED FOR, NEVER INFERRED FROM SILENCE. An event with no date is delivered through an
/// explicit <c>FILTER NOT EXISTS</c> branch carrying <see cref="UnboundDateKind"/>. The reason is
/// the contract rather than a measured rate: <see cref="EuProcedureEventObservation"/> refuses an
/// undated event by name, and a refusal that can only be reached from a hand-built row is not a
/// refusal this family has been shown to produce. HOW MANY events carry no date is deliberately
/// not claimed - see <see cref="EventDatePredicateIri"/> for what the orientation probe did and did
/// not establish - and nothing in this plan depends on that number.
/// </para>
/// <para>
/// THE SELECTION IS BOUND BEFORE <c>pass_id</c>.
/// <c>RepeatedEnumerationDeliveryProof.RequireInputRoleShape</c> builds its expectation as
/// <c>SelectionParameterNames.Append(PassParameterName)</c> and compares the ordered roles by
/// sequence. The case-law family bound <c>pass_id</c> first and could therefore refuse correctly
/// while never once succeeding: every honest delivery reached <c>DeliveryProofRefused</c>, and no
/// test noticed because every end-to-end guard that family had drove a refusal.
/// </para>
/// </remarks>
public sealed class EuProcedureEventDiscoveryPlan
{
    internal const string PublisherEndpoint = "https://publications.europa.eu/webapi/rdf/sparql";
    internal const string Cdm = EuConsolidationDiscoveryPlan.Cdm;

    /// <summary>The predicate an event uses to name its dossier.</summary>
    public const string PartOfDossierPredicateIri = EuProcedureEventVocabulary.PartOfDossierPredicateUri;

    /// <summary>
    /// The predicate carrying an event's own legal date. Observed to exist on this class; chosen
    /// among its siblings on what it means, not on how often it occurs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHOSEN ON SEMANTIC GROUNDS, NOT ON A COUNT. It is the event's own legal date;
    /// <c>cmr#lastModificationDate</c> and <c>cmr#creationDate</c> are the repository's
    /// housekeeping stamps for the record rather than a fact about the procedure, and
    /// <c>event_session_date_start</c> / <c>_end</c>, <c>datetime_transmission</c> and the two
    /// dispatch dates are different facts about an event rather than a more precise spelling of the
    /// same one. The siblings are named here so the choice is legible rather than silent.
    /// </para>
    /// <para>
    /// What the probe did and did not establish, stated exactly. One read-only query on 2026-09-09
    /// grouped date-bearing predicates on <c>cdm:event_legal</c> subjects by <c>COUNT(*)</c>. That
    /// establishes the predicate EXISTS on this class and is used at scale. It does NOT establish
    /// distinct event coverage: <c>COUNT(*)</c> counts triples, so an event carrying two dates
    /// counts twice. <c>cmr#lastModificationDate</c> returned the identical figure, which is a
    /// further reason not to read either number as a count of events. No coverage or absence rate
    /// is claimed here, and none is relied on: a <c>COUNT(DISTINCT ?e)</c> over the same current
    /// population, and a direct measurement of the absence branch, are what such a claim would
    /// need.
    /// </para>
    /// <para>
    /// The probe was a direct read-only request rather than one routed through this product's own
    /// governed client, so it carries no retained request or response digest and no robots evidence
    /// of its own. It is therefore cited as orientation for a naming decision, never as acceptance
    /// evidence, and this family's real requests go through the ordinary routed path like every
    /// other.
    /// </para>
    /// </remarks>
    public const string EventDatePredicateIri = Cdm + "event_legal_date";

    /// <summary>The marker a row carries when the publisher holds no date for that event.</summary>
    public const string UnboundDateKind = "unbound";

    /// <summary>
    /// How many dossiers one request may ask about. Fixed at 50, matching the case-law and
    /// object-facts families, so batch size is a property of this plan rather than a caller's choice.
    /// </summary>
    public const int BatchCapacity = 50;

    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 811;
    internal const uint Pass2PageLimit = 509;
    internal const string PartitionMemberKey = "eu-procedure-events-by-dossier";

    private const string ResourceId = "urn:uuid:2f8c5b19-7e34-4a06-9d52-6b1c8ea3f470";
    private const string MemberPrefix = "eu-procedure-events-by-dossier";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] Projection =
    [
        "event", "dossier", "event_type", "event_date", "date_kind", "multiplicity",
        "key_1", "key_2", "key_3", "key_4",
    ];

    /// <summary>
    /// The keyset. <c>key_2</c> carries the declared type because one event contributes one row per
    /// type: without it two types of a single event would share a cursor and the page could not
    /// advance past the second.
    /// </summary>
    private static readonly string[] Cursor = ["key_1", "key_2", "key_3", "key_4"];

    /// <summary>
    /// How many cursor keys this family has. The renderer reads this rather than repeating the
    /// number, because a cursor that grew while the renderer still bound the old count would send a
    /// template with an unfilled slot. The case-law family had that number written twice.
    /// </summary>
    internal static int CursorKeyCount => Cursor.Length;

    private readonly byte[] _canonicalIdentityBytes;

    private EuProcedureEventDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "eu-procedure-events-by-dossier-plan/1",
            "endpoint=" + PublisherEndpoint,
            "method=POST",
            "target=/webapi/rdf/sparql",
            "request_media_type=application/sparql-query",
            "response_media_type=" + ResponseMediaType,
            "batch_capacity=" + BatchCapacity.ToString(CultureInfo.InvariantCulture),
            "part_of_dossier=" + PartOfDossierPredicateIri,
            "unbound_date_kind=" + UnboundDateKind,
            "selection_parameters=" + string.Join(',', BatchParameterNames()),
            "pass_parameter=pass_id",
            "cursor_presence_parameter=has_cursor",
            "cursor_envelope=" + EnumerationCursorEnvelope.Identity,
            "threshold_detector=" + ThresholdDetectorIdentity,
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + (int)EuProcedureEventQueryPass.Pass1 + ":" + Pass1PageLimit,
            "pass_2=" + (int)EuProcedureEventQueryPass.Pass2 + ":" + Pass2PageLimit,
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

    public SourceArtifactRef ArtifactRef { get; }
    public SourceRegistryMemberRef CountQueryFamilyRef { get; }
    public SourceRegistryMemberRef PageQueryFamilyRef { get; }
    public string CountTemplate { get; }
    public string PageTemplate { get; }

    public static EuProcedureEventDiscoveryPlan Create() => new();

    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    public RepeatedEnumerationInterpretationProfile CreateDeliveryProfile() => new(
        RepeatedEnumerationInterpretationProfile.SchemaId,
        RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso,
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

    internal static uint PageLimit(EuProcedureEventQueryPass pass) => pass switch
    {
        EuProcedureEventQueryPass.Pass1 => Pass1PageLimit,
        EuProcedureEventQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    internal static IReadOnlyList<string> BatchParameterNames()
    {
        var names = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            names[index] = "requested_dossier_" + (index + 1).ToString("D2", CultureInfo.InvariantCulture);
        }

        return Array.AsReadOnly(names);
    }

    /// <summary>
    /// The batch members exactly as this plan puts them to the publisher.
    /// </summary>
    /// <remarks>
    /// A membership check downstream compares a delivered key against what was asked, and the two
    /// have to be in one lexical form. The case-law family compared a delivered canonical key
    /// against the caller's raw spelling and would have refused every honest row for a caller who
    /// wrote <c>https://</c>. Exposed here so the executor compares like with like rather than
    /// re-deriving the rule and drifting from it.
    /// </remarks>
    public static IReadOnlyList<string> RequestedPartitionMembers(IReadOnlyList<string> batchDossiers) =>
        Array.AsReadOnly(CanonicalizeBatch(batchDossiers));

    internal static string[] CanonicalizeBatch(IReadOnlyList<string> batchDossiers)
    {
        ArgumentNullException.ThrowIfNull(batchDossiers);
        if (batchDossiers.Count is 0 or > BatchCapacity)
        {
            throw new ArgumentException(
                $"A batch must name one to {BatchCapacity} dossiers.", nameof(batchDossiers));
        }

        var canonical = new string[batchDossiers.Count];
        for (var index = 0; index < batchDossiers.Count; index++)
        {
            var value = batchDossiers[index] ??
                throw new ArgumentException("A batch member cannot be null.", nameof(batchDossiers));
            canonical[index] = EuPackRootCanonicalForm.TryCanonicalize(value, out _) ??
                throw new ArgumentException(
                    $"Batch member '{value}' does not reduce to Appendix A's exact lexical form.",
                    nameof(batchDossiers));
        }

        Array.Sort(canonical, StringComparer.Ordinal);
        if (canonical.Distinct(StringComparer.Ordinal).Count() != canonical.Length)
        {
            throw new ArgumentException("A batch cannot name one dossier twice.", nameof(batchDossiers));
        }

        return canonical;
    }

    internal static string[] PadBatch(IReadOnlyList<string> canonicalSortedBatch)
    {
        var padded = new string[BatchCapacity];
        var last = canonicalSortedBatch[^1];
        for (var index = 0; index < BatchCapacity; index++)
        {
            padded[index] = index < canonicalSortedBatch.Count ? canonicalSortedBatch[index] : last;
        }

        return padded;
    }

    /// <summary>Binds the count request for one batch of dossiers.</summary>
    public EuProcedureEventBoundQuery BindCount(
        EuProcedureEventQueryPass pass,
        IReadOnlyList<string> batchDossiers,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, batchDossiers, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    /// <summary>Binds one page request for one batch of dossiers.</summary>
    public EuProcedureEventBoundQuery BindPage(
        EuProcedureEventQueryPass pass,
        IReadOnlyList<string> batchDossiers,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, pass, batchDossiers, cursor,
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private EuProcedureEventBoundQuery Bind(
        bool isPage,
        EuProcedureEventQueryPass pass,
        IReadOnlyList<string> batchDossiers,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);
        var padded = PadBatch(CanonicalizeBatch(batchDossiers));

        // THE SELECTION COMES FIRST AND pass_id FOLLOWS IT. RequireInputRoleShape builds its
        // expectation as SelectionParameterNames.Append(PassParameterName) and compares the ordered
        // roles by sequence, so binding pass_id ahead of the selection makes every honest delivery
        // reach DeliveryProofRefused - a family that can refuse correctly and never once succeed.
        var parameters = new List<MachineQueryParameter>();
        var names = BatchParameterNames();
        for (var index = 0; index < BatchCapacity; index++)
        {
            parameters.Add(new MachineQueryParameter(
                names[index], MachineQueryParameterKind.PublisherLiteral,
                null, padded[index], ArtifactRef));
        }

        parameters.Add(new MachineQueryParameter(
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
        var renderer = new EuProcedureEventSparqlRenderer(this, isPage, rendererSource);
        var rendered = renderer.RenderInput(input, response);
        var body = rendered.CopyRequestBody();
        var targetBytes = Encoding.ASCII.GetBytes("/webapi/rdf/sparql");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            family,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Post,
            PublisherEndpoint,
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
        return new(machinePlan, machinePlanRef, input,
            MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer));
    }

    private static (string Count, string Page) BuildTemplates()
    {
        var valuesBlock = string.Join('\n', BatchParameterNames()
            .Select(static name => "    {" + name + ":iri}"));

        var rows = $$"""
            SELECT ?event ?dossier ?event_type ?event_date ?date_kind (COUNT(*) AS ?multiplicity) WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              {
                SELECT DISTINCT ?dossier WHERE {
                  VALUES ?dossier {
            {{valuesBlock}}
                  }
                }
              }
              ?event <{{PartOfDossierPredicateIri}}> ?dossier .
              ?event a ?event_type .
              {
                ?event <{{EventDatePredicateIri}}> ?event_date .
                BIND(IF(isLiteral(?event_date), "literal", IF(isIRI(?event_date), "iri", "unsupported_blank_node")) AS ?date_kind)
              }
              UNION
              {
                FILTER NOT EXISTS { ?event <{{EventDatePredicateIri}}> ?missing_date }
                BIND("{{UnboundDateKind}}" AS ?date_kind)
              }
            }
            GROUP BY ?event ?dossier ?event_type ?event_date ?date_kind
            """;

        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;

        var page = $$"""
            SELECT ?event ?dossier ?event_type ?event_date ?date_kind ?multiplicity ?key_1 ?key_2 ?key_3 ?key_4 WHERE {
              {
            {{Indent(Indent(rows))}}
              }
              BIND(STR(?event) AS ?key_1)
              BIND(STR(?event_type) AS ?key_2)
              BIND(STR(?dossier) AS ?key_3)
              BIND(COALESCE(STR(?event_date), "") AS ?key_4)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string} {last_key_4:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 > ?last_key_4)
              )
              FILTER(?has_cursor = 0 || !(
                ?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4))
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4
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
/// Renders one bound procedure-event request. Every slot is filled from the ordered parameter set
/// and each must occur exactly once in the template, so a template edit that drops or duplicates a
/// slot fails loudly rather than sending a silently different question.
/// </summary>
internal sealed class EuProcedureEventSparqlRenderer : IMachineQueryRenderer
{
    private readonly EuProcedureEventDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal EuProcedureEventSparqlRenderer(
        EuProcedureEventDiscoveryPlan plan,
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
        var pass = (EuProcedureEventQueryPass)Integer(parameters, "pass_id");
        var limit = EuProcedureEventDiscoveryPlan.PageLimit(pass);
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));

        var names = EuProcedureEventDiscoveryPlan.BatchParameterNames();
        var batchCount = 0;
        foreach (var name in names)
        {
            if (!parameters.TryGetValue(name, out var member) ||
                member.Kind != MachineQueryParameterKind.PublisherLiteral || member.TextValue is null)
            {
                throw new ArgumentException($"The batch slot {name} is missing or invalid.", nameof(input));
            }

            query = Replace(query, "{" + name + ":iri}", SparqlIriTerm(member.TextValue));
            batchCount++;
        }

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
            (hasCursor == 1 ? EuProcedureEventDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= EuProcedureEventDiscoveryPlan.CursorKeyCount; ordinal++)
        {
            var name = "last_key_" + ordinal;
            var value = hasCursor == 0 ? string.Empty : Cursor(parameters, name);
            query = Replace(query, "{" + name + ":sparql_string}", SparqlQueryText.StringLiteral(value));
        }

        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        EuProcedureEventDiscoveryPlan.PublisherEndpoint, Encoding.UTF8.GetBytes(query));

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

    /// <summary>
    /// Wraps a canonical batch member as a SPARQL IRI term, refusing anything that could close the
    /// angle brackets or split the term. The batch is already canonicalized, so this is the second
    /// line rather than the only one.
    /// </summary>
    private static string SparqlIriTerm(string canonicalIri)
    {
        foreach (var forbidden in new[] { '<', '>', '"', '\\', '{', '}', '|', '^', '`', ' ', '\n', '\r', '\t' })
        {
            if (canonicalIri.Contains(forbidden))
            {
                throw new ArgumentException(
                    "A batch member cannot carry a character that closes or splits an IRI term.",
                    nameof(canonicalIri));
            }
        }

        return "<" + canonicalIri + ">";
    }

    private static string Replace(string source, string slot, string replacement)
    {
        if (source.Split(slot, StringSplitOptions.None).Length != 2)
        {
            throw new ArgumentException("A renderer slot must occur exactly once.", nameof(source));
        }

        return source.Replace(slot, replacement, StringComparison.Ordinal);
    }
}
