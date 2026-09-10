using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public sealed record LuxembourgInitialDraftInventoryBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Asks Legilux which subjects the <c>InitialDraft</c> class actually holds, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// THIS FAMILY EXISTS BECAUSE THE PUBLISHER REFUSED THE OTHER ONE. Two live runs against Legilux,
/// retained under #417, were refused at the pass-one count with
/// <c>Virtuoso 22026 Error SR319: Max row length is exceeded when trying to store a string of 5
/// chars into a temp col</c> — once with the draft-graph query grouping seven columns and once with
/// it grouping three. Column count was not the variable. The families that DO answer against this
/// engine (<see cref="LuxembourgTranspositionIdentityDiscoveryPlan"/> groups eight columns and
/// works) are bounded by a <c>VALUES</c> selection, and the draft graph is not.
/// </para>
/// <para>
/// SO THE CLASS SWEEP IS NOT ABANDONED, IT IS STAGED. E8 still asks for every <c>InitialDraft</c>
/// and the same five closed predicates. This is the first stage: enumerate the subjects, then let
/// the draft graph run over deterministic bounded batches derived from THIS RUN'S OWN proven
/// inventory. The cover is the publisher's own answer about its own class rather than an assumption
/// about the shape of its keys — which is what makes "every subject exactly once" checkable instead
/// of asserted.
/// </para>
/// <para>
/// WHY NOT A LEXICAL RANGE COVER. <see cref="LuxembourgQueryPartitionRange"/> requires a finite
/// <c>StartInclusive</c> below a finite <c>EndExclusive</c>. The key domain here is UTF-8 strings,
/// and Σ* has no greatest element: for any <c>s</c>, <c>s + "a"</c> sorts above it. A cover built
/// from finite ranges therefore always leaves <c>[lastEnd, ∞)</c> unqueried and unrefused, which is
/// exactly the silent gap the owner ruling forbids — and choosing a root range that "obviously"
/// contains every draft would redefine the class scope by assumption. An inventory has no such
/// hole: what it does not return, it did not return, and that is a fact rather than a gap.
/// </para>
/// <para>
/// ONE GROUPED COLUMN, WHICH IS THE NARROWEST THIS QUESTION CAN BE ASKED. The subject alone is
/// grouped; its kind marker and both cursor keys are derived afterwards. That is not a guess about
/// SR319's cause — the three-column measurement already refuted the width hypothesis — it is simply
/// the smallest question that answers "which subjects exist", and if this one is refused too then
/// the refusal is the finding and no population may be inferred from it.
/// </para>
/// <para>
/// A NON-ADDRESSABLE SUBJECT IS TYPED, NEVER FILTERED. The class membership triple is asked without
/// an <c>isIRI</c> guard, so a blank-node subject arrives and is carried with the marker saying so.
/// Filtering it at the query would make a subject the publisher holds vanish from an inventory that
/// calls itself complete, which is the false absence S2-A03 forbids; and a later stage that cannot
/// address it must refuse to batch it rather than quietly cover a smaller class.
/// </para>
/// <para>
/// THE DERIVED KEYS ARE TOTALISED WITH COALESCE, for the reason
/// <see cref="EuObjectFactsDiscoveryPlan"/> measured: this engine evaluates <c>IF</c>'s arguments
/// EAGERLY, so a dereference of a term the engine will not accept raises, the erroring BIND leaves
/// the key unbound, and SPARQL's JSON omits it from the binding entirely. COALESCE is specified to
/// swallow an erroring argument and take the next.
/// </para>
/// </remarks>
public sealed class LuxembourgInitialDraftInventoryDiscoveryPlan
{
    /// <summary>The class this family enumerates the members of.</summary>
    public const string InitialDraftClassIri =
        "http://data.legilux.public.lu/resource/ontology/jolux#InitialDraft";

    /// <summary>The marker a row carries when its subject is a publisher IRI.</summary>
    public const string IriKind = "iri";

    /// <summary>
    /// The marker a row carries when its subject is a blank node.
    /// </summary>
    /// <remarks>
    /// Such a subject is a real class member this publisher holds and is retained as one. It is not
    /// addressable in a later <c>VALUES</c> batch, because a blank-node label is scoped to the
    /// result set that produced it and naming it in a new request asks about nothing. That makes it
    /// a typed gap for the batching stage rather than a row to drop here.
    /// </remarks>
    public const string UnsupportedBlankNodeKind = "unsupported_blank_node";

    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 907;
    internal const uint Pass2PageLimit = 613;
    internal const string PartitionMemberKey = "legilux-initial-draft-inventory";

    private const string ResourceId = "urn:uuid:2b8f1d47-9c05-4e63-a7f2-40d6e83b19ca";
    private const string MemberPrefix = "lu-initial-draft-inventory";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] Projection =
        ["draft", "draft_kind", "multiplicity", "key_1", "key_2"];

    /// <summary>
    /// The keyset: the subject, and its kind.
    /// </summary>
    /// <remarks>
    /// The subject alone is unique after the grouping, so the kind is not carrying uniqueness — it
    /// is carrying AUTHORITY. Source/Core requires canonical keys unique and cursors strictly
    /// increasing over the delivered order, and a blank node's label and an IRI can in principle
    /// share a lexical form. Two rows that are different facts must not share every key.
    /// </remarks>
    private static readonly string[] Cursor = ["key_1", "key_2"];

    /// <summary>How many cursor keys this family has, read by the renderer rather than written twice.</summary>
    internal static int CursorKeyCount => Cursor.Length;

    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgInitialDraftInventoryDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "lu-initial-draft-inventory-plan/1",
            "endpoint=" + LuxembourgQueryPlan.PublisherEndpoint,
            "method=POST",
            "request_media_type=application/x-www-form-urlencoded",
            "response_media_type=" + ResponseMediaType,
            "draft_class=" + InitialDraftClassIri,
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

    public static LuxembourgInitialDraftInventoryDiscoveryPlan Create() => new();

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

    public LuxembourgInitialDraftInventoryBoundQuery BindCount(
        LuxembourgQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public LuxembourgInitialDraftInventoryBoundQuery BindPage(
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

    private LuxembourgInitialDraftInventoryBoundQuery Bind(
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
        var renderer = new LuxembourgInitialDraftInventorySparqlRenderer(this, isPage, rendererSource);
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
            SELECT ?draft (COUNT(*) AS ?multiplicity) WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              ?draft a <{{InitialDraftClassIri}}> .
            }
            GROUP BY ?draft
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
              BIND(COALESCE(IF(isIRI(?draft), "{{IriKind}}", "{{UnsupportedBlankNodeKind}}"), "{{UnsupportedBlankNodeKind}}") AS ?draft_kind)
              BIND(COALESCE(STR(?draft), "") AS ?key_1)
              BIND(?draft_kind AS ?key_2)
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
internal sealed class LuxembourgInitialDraftInventorySparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgInitialDraftInventoryDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgInitialDraftInventorySparqlRenderer(
        LuxembourgInitialDraftInventoryDiscoveryPlan plan,
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
        var limit = LuxembourgInitialDraftInventoryDiscoveryPlan.PageLimit(pass);
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
            (hasCursor == 1 ? LuxembourgInitialDraftInventoryDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= LuxembourgInitialDraftInventoryDiscoveryPlan.CursorKeyCount; ordinal++)
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
