using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public sealed record LuxembourgOpinionBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Enumerates Conseil d'État opinion events with the locator of their resulting document and their
/// own date, and asks for nothing else.
/// </summary>
/// <remarks>
/// <para>
/// THIS QUERY CANNOT FETCH AN OPINION'S TEXT, AND THAT IS THE POINT. E8's decision is that
/// unlicensed opinion text stays link-only, and
/// <see cref="LuxembourgOpinionLinkOnlyRecord"/> enforces it in the type by having nowhere to put a
/// body. This plan is the same decision one layer earlier: it projects a locator and a date, so the
/// enumeration that feeds that record never carries text to put anywhere. A licence would change
/// the record's disposition; it would still have to change this query too, which is a second place
/// the decision is written down rather than a restatement of the first.
/// </para>
/// <para>
/// ABSENCE IS ASKED FOR, NEVER INFERRED FROM SILENCE, and here it is the common case rather than the
/// corner. The vocabulary's own observations: 13,009 <c>OpinionConseilEtat</c> events, of which
/// 6,393 carry <c>hasResultingOpinionDocument</c> and 9,144 carry <c>opinionDate</c>. So roughly
/// half the events have no document at all, and a query that dropped them would report a corpus half
/// its real size while looking complete. Both edges are therefore asked through an explicit
/// <c>FILTER NOT EXISTS</c> branch carrying a marker, exactly as the EU case-law family asks for a
/// missing ECLI.
/// </para>
/// <para>
/// This is deliberately NOT the shape of its Luxembourg sibling.
/// <see cref="LuxembourgTranspositionIdentityDiscoveryPlan"/> uses <c>OPTIONAL</c> with
/// <c>COALESCE(..., "")</c>, which cannot distinguish "the publisher holds no value" from "a value
/// was held and the pattern failed to match it": both arrive as the empty string. That is tolerable
/// where the absent column is decoration. Here the absent column decides whether a record can exist
/// at all, so the two have to be different answers.
/// </para>
/// <para>
/// THE KIND MARKERS ARE NOT THE TERMS. <c>?document_kind</c> and <c>?date_kind</c> are <c>BIND</c>
/// values this plan computes ABOUT a row; they are not the publisher's word for what the row is. A
/// decoder reads the terms and may use a marker only to detect disagreement. Trusting a marker over
/// its term is a defect this seat has shipped once, on the E1 axiom decoder, and a marker read as one
/// boolean rather than its whole four-valued space is a defect this seat shipped again on the E6
/// producer. Both were found in review.
/// </para>
/// <para>
/// The family is swept whole rather than asked about a batch: its scope is a class, not a caller's
/// list, so there are no selection parameters and a caller chooses nothing. At 13,009 events and a
/// pass-1 page limit of 971 that is an enumeration this plan can finish.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionDiscoveryPlan
{
    /// <summary>The JOLux class of an opinion event. 13,009 observed.</summary>
    public const string OpinionClassIri = LuxembourgOpinionLinkOnlyVocabulary.OpinionConseilEtatClassIri;

    /// <summary>The opinion-to-document edge. 6,393 of the 13,009 events carry one.</summary>
    public const string ResultingDocumentPredicateIri =
        LuxembourgOpinionLinkOnlyVocabulary.HasResultingOpinionDocumentPredicateIri;

    /// <summary>The opinion's own date. 9,144 of the 13,009 events carry one.</summary>
    public const string OpinionDatePredicateIri = LuxembourgOpinionLinkOnlyVocabulary.OpinionDatePredicateIri;

    /// <summary>The marker a row carries when the publisher holds no such edge for that opinion.</summary>
    public const string UnboundKind = "unbound";

    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 971;
    internal const uint Pass2PageLimit = 587;
    internal const string PartitionMemberKey = "legilux-conseil-etat-opinions";

    private const string ResourceId = "urn:uuid:0b7d41e6-58a2-4c93-9f16-3ad82e5c7401";
    private const string MemberPrefix = "lu-conseil-etat-opinions";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] Projection =
    [
        "opinion", "document", "document_kind", "opinion_date", "date_kind", "multiplicity",
        "key_1", "key_2", "key_3",
    ];

    /// <summary>
    /// The keyset. All three parts are needed because an opinion is not unique on its own: an event
    /// carrying two resulting documents, or two dates, delivers a row per combination, and a cursor
    /// that named only the opinion could not advance past the second of them.
    /// </summary>
    private static readonly string[] Cursor = ["key_1", "key_2", "key_3"];

    /// <summary>
    /// How many cursor keys this family has. The renderer reads this rather than repeating the
    /// number, because a cursor that grew while the renderer still bound the old count would send a
    /// template with an unfilled slot. The EU case-law family had that number written twice.
    /// </summary>
    internal static int CursorKeyCount => Cursor.Length;

    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgOpinionDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "lu-conseil-etat-opinion-plan/1",
            "endpoint=" + LuxembourgQueryPlan.PublisherEndpoint,
            "method=POST",
            "request_media_type=application/x-www-form-urlencoded",
            "response_media_type=" + ResponseMediaType,
            "opinion_class=" + OpinionClassIri,
            "resulting_document=" + ResultingDocumentPredicateIri,
            "opinion_date=" + OpinionDatePredicateIri,
            "unbound_kind=" + UnboundKind,
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

    public static LuxembourgOpinionDiscoveryPlan Create() => new();

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

    public LuxembourgOpinionBoundQuery BindCount(
        LuxembourgQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public LuxembourgOpinionBoundQuery BindPage(
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

    /// <remarks>
    /// There is deliberately no "a count query cannot carry a cursor" refusal here. The sibling
    /// transposition plan has one, and it is unreachable: <see cref="BindCount"/> has no cursor
    /// parameter to pass, so no caller can reach the branch. A refusal nothing can produce is a
    /// sentence that reads like a guarantee and is enforced by the signature instead, so the
    /// signature is what the test asserts.
    /// </remarks>
    private LuxembourgOpinionBoundQuery Bind(
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

        // This family has no selection parameters, so the ordered roles reduce to pass_id and the
        // page's cursor shape. RepeatedEnumerationDeliveryProof.RequireInputRoleShape builds its
        // expectation as SelectionParameterNames.Append(PassParameterName) and compares by sequence:
        // with an empty selection, pass-first and selection-first are the same list. The EU case-law
        // family had a non-empty selection and bound pass_id ahead of it, so an honest delivery
        // reached DeliveryProofRefused and that family could never succeed. Nothing to get wrong
        // here, and it is written down so a later slice that adds a selection knows where it goes.
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
        var renderer = new LuxembourgOpinionSparqlRenderer(this, isPage, rendererSource);
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
        // Two sibling questions, each with its own explicit absence branch. The branches of one
        // question are mutually exclusive by construction - either the triple exists or the
        // FILTER NOT EXISTS holds - so joining the two questions yields exactly one combination per
        // (opinion, document, date) tuple rather than a blow-up. An event carrying two documents
        // delivers two rows, which is the publisher's own multiset and is what ?multiplicity and the
        // three-part keyset are for.
        var rows = $$"""
            SELECT ?opinion ?document ?document_kind ?opinion_date ?date_kind (COUNT(*) AS ?multiplicity) WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              ?opinion a <{{OpinionClassIri}}> .
              {
                ?opinion <{{ResultingDocumentPredicateIri}}> ?document .
                BIND(IF(isIRI(?document), "iri", IF(isLiteral(?document), "literal", "unsupported_blank_node")) AS ?document_kind)
              }
              UNION
              {
                FILTER NOT EXISTS { ?opinion <{{ResultingDocumentPredicateIri}}> ?missing_document }
                BIND("{{UnboundKind}}" AS ?document_kind)
              }
              {
                ?opinion <{{OpinionDatePredicateIri}}> ?opinion_date .
                BIND(IF(isLiteral(?opinion_date), "literal", IF(isIRI(?opinion_date), "iri", "unsupported_blank_node")) AS ?date_kind)
              }
              UNION
              {
                FILTER NOT EXISTS { ?opinion <{{OpinionDatePredicateIri}}> ?missing_date }
                BIND("{{UnboundKind}}" AS ?date_kind)
              }
            }
            GROUP BY ?opinion ?document ?document_kind ?opinion_date ?date_kind
            """;

        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;

        var page = $$"""
            SELECT ?opinion ?document ?document_kind ?opinion_date ?date_kind ?multiplicity ?key_1 ?key_2 ?key_3 WHERE {
              {
            {{Indent(Indent(rows))}}
              }
              BIND(STR(?opinion) AS ?key_1)
              BIND(COALESCE(STR(?document), "") AS ?key_2)
              BIND(COALESCE(STR(?opinion_date), "") AS ?key_3)
              VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3) {
                ({has_cursor:uint} {last_key_1:sparql_string} {last_key_2:sparql_string} {last_key_3:sparql_string})
              }
              FILTER(
                ?has_cursor = 0 || ?key_1 > ?last_key_1 ||
                (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2) ||
                (?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 > ?last_key_3)
              )
              FILTER(?has_cursor = 0 || !(
                ?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3))
            }
            ORDER BY ?key_1 ?key_2 ?key_3
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
/// Renders one bound opinion request. Every slot is filled from the ordered parameter set and each
/// must occur exactly once in the template, so a template edit that drops or duplicates a slot is a
/// loud failure rather than a silently different question.
/// </summary>
internal sealed class LuxembourgOpinionSparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgOpinionDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgOpinionSparqlRenderer(
        LuxembourgOpinionDiscoveryPlan plan,
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
        var limit = LuxembourgOpinionDiscoveryPlan.PageLimit(pass);
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
            (hasCursor == 1 ? LuxembourgOpinionDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= LuxembourgOpinionDiscoveryPlan.CursorKeyCount; ordinal++)
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
