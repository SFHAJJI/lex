using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public sealed record LuxembourgOpinionRequestInventoryBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Asks Legilux which subjects the <c>OpinionRequest</c> class actually holds, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE CLASS THAT DECLARES <c>referralDate</c>, and E8 cannot resolve that predicate
/// without it. The draft-graph family asks every predicate the publisher holds on a draft and
/// <c>referralDate</c> is not among them - measured over 650 drafts, eighteen predicates, none of
/// them it. So a <c>(draft, referralDate)</c> pair is typed an unresolved gap awaiting a traversal
/// that proves the subject role, and this family enumerates the subjects that role belongs to.
/// </para>
/// <para>
/// THE CLASS IS POPULATED, AND THAT IS A DATED OBSERVATION RATHER THAN A CONSTANT. A governed
/// diagnostic counted <b>7,751</b> instances at <c>2026-09-11T11:57:05Z</c>, with two controls -
/// <c>InitialDraft</c> at 7,753 and <c>OpinionConseilEtat</c> at 13,010 - landing on independently
/// known figures, so the count was the publisher's answer rather than an artefact of the question.
/// Nothing here may turn that number into a population: this plan exists to enumerate the members,
/// and what it returns on any given run is what that run observed.
/// </para>
/// <para>
/// STAGED FOR THE SAME REASON THE DRAFT INVENTORY IS, not because this engine has been measured
/// refusing a broad sweep of this class - it has not, and claiming otherwise would be borrowing
/// another family's measurement. What IS established is that the draft-graph class sweep was
/// refused twice with <c>Virtuoso 22026 Error SR319</c> and that the families answering against
/// this engine are bounded by a selection. Enumerating subjects first, then running the graph over
/// deterministic bounded batches issued by this run's own proven inventory, is the shape that
/// survived there. If the count below is refused, the refusal is the finding and no population may
/// be inferred from it.
/// </para>
/// <para>
/// A REACHED-TARGET SELECTION WOULD NOT DO, and that is the architectural point. Subjects could
/// instead be taken from the <c>hasOpinion</c> targets the draft graph already retained, which
/// would bound this family to drafts already proven. It would also make it unable to tell an
/// <b>unlinked class member</b> from an <b>omitted</b> one, and would make this family's
/// completeness argument depend on another family's run. The class sweep has no such hole: what it
/// does not return, it did not return.
/// </para>
/// <para>
/// A NON-ADDRESSABLE SUBJECT IS OBSERVED, NEVER FILTERED - AND IT STOPS THE INVENTORY. The class
/// membership triple is asked without an <c>isIRI</c> guard, so a blank-node subject arrives and is
/// carried with the marker saying so. Filtering it at the query would make a subject the publisher
/// holds vanish from an inventory that calls itself complete, which is the false absence S2-A03
/// forbids. Reporting it and continuing is equally wrong: <c>key_1</c> derives from
/// <c>STR(?request)</c>, and a blank node's label is scoped to the result set that carried it, so
/// two passes could agree on <c>b0</c> while meaning different subjects. No exact membership list
/// exists over an identity that does not survive its own response, so the member is reported and
/// the inventory refuses.
/// </para>
/// <para>
/// THE DERIVED KEYS ARE TOTALISED WITH COALESCE, for the reason
/// <see cref="EuObjectFactsDiscoveryPlan"/> measured: this engine evaluates <c>IF</c>'s arguments
/// EAGERLY, so a dereference of a term the engine will not accept raises, the erroring BIND leaves
/// the key unbound, and SPARQL's JSON omits it from the binding entirely. COALESCE is specified to
/// swallow an erroring argument and take the next.
/// </para>
/// <para>
/// NO VALUE IS KEYED HERE, so this family never meets the publisher's cursor defect. It enumerates
/// subjects; the keyset is the subject IRI and its kind. The request-graph family that follows does
/// key on delivered values and must recompute through
/// <see cref="LuxembourgPublisherCursorCodec"/>.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestInventoryDiscoveryPlan
{
    /// <summary>The class this family enumerates the members of.</summary>
    /// <remarks>
    /// The class that declares <c>referralDate</c>. Its instances are reached from a draft by
    /// <c>jolux:hasOpinion</c> - established by identity rather than by population similarity, on
    /// two independent drafts, and recorded on #417. That relationship is NOT an input here: this
    /// family is request-scoped and the traversal belongs to a composite reconciliation that cites
    /// both proof-bound outputs.
    /// </remarks>
    public const string OpinionRequestClassIri =
        LuxembourgDraftGraphDiscoveryPlan.OpinionRequestClassIri;

    /// <summary>The marker a row carries when its subject is a publisher IRI.</summary>
    /// <remarks>
    /// Aliased, not restated. These markers are a producer convention shared across the Luxembourg
    /// families, and a family that spelled its own would render a template no decoder reads while
    /// every test derived from the same private spelling and agreed.
    /// </remarks>
    public const string IriKind = LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind;

    /// <summary>
    /// The marker a row carries when its subject is a blank node.
    /// </summary>
    /// <remarks>
    /// Such a subject is a real class member this publisher holds, and it is reported rather than
    /// dropped. It is not addressable, because a blank-node label is scoped to the result set that
    /// produced it: naming it in a new request asks about nothing, and two responses reusing the
    /// label are not thereby about the same subject. That is why observing one REFUSES the
    /// inventory instead of merely tagging a row - see the type remarks.
    /// </remarks>
    public const string UnsupportedBlankNodeKind =
        LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind;

    internal const long PublisherDeliveryCeilingRows = 1_000_000;

    /// <summary>
    /// The page limits, deliberately unequal and deliberately not the draft inventory's.
    /// </summary>
    /// <remarks>
    /// Two passes at different limits is what makes agreement evidence rather than repetition: the
    /// same membership arrived at across different page boundaries. The numbers are arbitrary
    /// within the engine's tolerance and are not shared with another family, so a cross-family
    /// copy-paste of a cursor cannot silently line up.
    /// </remarks>
    internal const uint Pass1PageLimit = 911;

    internal const uint Pass2PageLimit = 617;

    internal const string PartitionMemberKey = "legilux-opinion-request-inventory";

    /// <summary>The same partition key, reachable by the linked test fixture.</summary>
    /// <remarks>
    /// The citation door binds a proof to this family, so a fixture proving that family has to name
    /// it. Exposed rather than retyped: two spellings of one partition key is how a fixture starts
    /// proving a family nothing else believes in.
    /// </remarks>
    public const string PartitionMemberKeyForFixtures = PartitionMemberKey;

    private const string ResourceId = "urn:uuid:5f3a9c24-7e61-4b08-9d52-c6f1a48e7b03";
    private const string MemberPrefix = "lu-opinion-request-inventory";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] Projection =
        ["request", "request_kind", "multiplicity", "key_1", "key_2"];

    /// <summary>
    /// The keyset: the subject, and its kind.
    /// </summary>
    /// <remarks>
    /// The subject alone is unique after the grouping, so the kind is not carrying uniqueness - it
    /// is carrying AUTHORITY. Source/Core requires canonical keys unique and cursors strictly
    /// increasing over the delivered order, and a blank node's label and an IRI can in principle
    /// share a lexical form. Two rows that are different facts must not share every key.
    /// </remarks>
    private static readonly string[] Cursor = ["key_1", "key_2"];

    /// <summary>How many cursor keys this family has, read by the renderer rather than written twice.</summary>
    internal static int CursorKeyCount => Cursor.Length;

    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgOpinionRequestInventoryDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "lu-opinion-request-inventory-plan/1",
            "endpoint=" + LuxembourgQueryPlan.PublisherEndpoint,
            "method=POST",
            "request_media_type=application/x-www-form-urlencoded",
            "response_media_type=" + ResponseMediaType,
            "request_class=" + OpinionRequestClassIri,
            "iri_kind=" + IriKind,
            "unsupported_blank_node_kind=" + UnsupportedBlankNodeKind,
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

    public static LuxembourgOpinionRequestInventoryDiscoveryPlan Create() => new();

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
        [],
        "pass_id",
        Cursor.Select(static value => "last_" + value).ToArray(),
        "has_cursor",
        RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    public LuxembourgOpinionRequestInventoryBoundQuery BindCount(
        LuxembourgQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public LuxembourgOpinionRequestInventoryBoundQuery BindPage(
        LuxembourgQueryPass pass,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, pass, cursor,
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private LuxembourgOpinionRequestInventoryBoundQuery Bind(
        bool isPage,
        LuxembourgQueryPass pass,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);

        // No selection, and here that is the point rather than a deferral: this family's question is
        // the class itself, and a caller who could narrow it could narrow what "complete" means.
        // The BATCHING stage is where a selection appears, and it takes its members from this run's
        // proven output. RequireInputRoleShape compares SelectionParameterNames.Append(
        // PassParameterName) by sequence, so that later selection binds BEFORE pass_id.
        var parameters = new List<MachineQueryParameter>
        {
            new("pass_id", MachineQueryParameterKind.BoundedInteger, (int)pass, null, ArtifactRef),
        };

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
        var renderer = new LuxembourgOpinionRequestInventorySparqlRenderer(this, isPage, rendererSource);
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

    private static (string Count, string Page) BuildTemplates()
    {
        // ONE column grouped. The subject is what the publisher answers; the kind and both keys are
        // what this design says about it, and only the first has to survive a GROUP BY.
        var rows = $$"""
            SELECT ?request (COUNT(*) AS ?multiplicity) WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              ?request a <{{OpinionRequestClassIri}}> .
            }
            GROUP BY ?request
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
              BIND(COALESCE(IF(isIRI(?request), "{{IriKind}}", "{{UnsupportedBlankNodeKind}}"), "{{UnsupportedBlankNodeKind}}") AS ?request_kind)
              BIND(COALESCE(STR(?request), "") AS ?key_1)
              BIND(?request_kind AS ?key_2)
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

    private static string KeysetFilter() => string.Join(" ||\n", Enumerable
        .Range(1, Cursor.Length)
        .Select(static length => "(" + string.Join(" && ", Enumerable
            .Range(0, length)
            .Select((_, index) => index == length - 1
                ? $"?{Cursor[index]} > ?last_{Cursor[index]}"
                : $"?{Cursor[index]} = ?last_{Cursor[index]}")) + ")"));

    private static string AllKeysEqual() => string.Join(" && ", Cursor
        .Select(static key => $"?{key} = ?last_{key}"));

    private static string Indent(string value) => string.Join('\n',
        Normalize(value).Split('\n').Select(static line => "  " + line));

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));
}

/// <summary>
/// Renders one bound inventory request. Every slot is filled from the ordered parameter set and each
/// must occur exactly once, so a template edit that drops or duplicates a slot fails loudly rather
/// than asking a silently different question.
/// </summary>
internal sealed class LuxembourgOpinionRequestInventorySparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgOpinionRequestInventoryDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgOpinionRequestInventorySparqlRenderer(
        LuxembourgOpinionRequestInventoryDiscoveryPlan plan,
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
        var limit = LuxembourgOpinionRequestInventoryDiscoveryPlan.PageLimit(pass);
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));

        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody || parameters.Count != 1)
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

        if (parameters.Count != 2 +
            (hasCursor == 1 ? LuxembourgOpinionRequestInventoryDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= LuxembourgOpinionRequestInventoryDiscoveryPlan.CursorKeyCount; ordinal++)
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
