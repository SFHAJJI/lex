using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>Which page-size policy a case-law enumeration request runs under.</summary>
public enum EuCaseLawQueryPass
{
    Pass1 = 1,
    Pass2 = 2,
}

/// <summary>One bound case-law request: the plan, its input artifact and the request itself.</summary>
public sealed record EuCaseLawBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// The bounded Cellar family that asks which case-law works point at a batch of EU acts, over
/// exactly the five predicates <see cref="EuCaseLawPredicateVocabulary"/> pins.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS FAMILY HAD TO EXIST, in the past tense it has now earned. E6's contract was merged and
/// reachable from nothing: no query in this repository asked the publisher a case-law question. The
/// executor drove five families — consolidation, object facts, NIM, witness traversal, document
/// fetch — and every one bound a closed predicate list containing none of E6's, so a case-law triple
/// was not merely unmatched upstream, it was never requested.
///
/// This plan is what changed that, and <c>EuRelationFamilyReachabilityTests</c> now proves it from
/// the rendered templates rather than restating it: of the thirteen relation families, the two
/// carrying the case-law_ prefix are asked for here and nowhere else. The paragraph above used to
/// quote <c>EuScopeDimensions</c>'s claim that all three case-law families were read by nothing;
/// that sentence has been corrected, and repeating a coverage claim in a second place is how the
/// first one outlived its truth.
/// </para>
/// <para>
/// WHY IT IS ITS OWN PLAN RATHER THAN A WIDER OBJECT-FACTS FAMILY. Three of the five pinned
/// predicates — <c>work_cites_work</c>, <c>case-law_requests_annulment_of_resource_legal</c> and
/// <c>case-law_declares_void_resource_legal</c> — have no member in the closed
/// <see cref="EuRelationFamily"/> vocabulary at all. Reaching them by widening
/// <c>EuScopeVocabulary.ReadRelationFamilies</c> would mint vocabulary the authority has not proven,
/// and the nearest existing member, <see cref="EuRelationFamily.CommunicationCaseRequestsAnnulment"/>,
/// is a <i>different</i> publisher predicate that E6's contract explicitly warns against confusing
/// with annulment-of-resource-legal. Two distinct judicial acts must not be collapsed to reuse a
/// family.
/// </para>
/// <para>
/// THE EDGE RUNS FROM THE CASE TO THE ACT. Every count the research proves is an INBOUND count on
/// the act: <c>work_cites_work</c> 2,257 and <c>case-law_interpretes_resource_legal</c> 74 on the
/// GDPR, <c>case-law_requests_annulment_of_resource_legal</c> 1 on CRD IV. So the subject is the
/// case and the object is the act, and the family is asked about a batch of acts rather than swept
/// corpus-wide — 2,257 citations of one act is enough to show why an unbounded
/// <c>work_cites_work</c> sweep is not a family anyone can enumerate.
/// </para>
/// <para>
/// THE CASE'S OWN ECLI IS PROJECTED BECAUSE THE CONTRACT CANNOT BE BUILT WITHOUT IT.
/// <see cref="EuCaseLawLinkBinding.Create"/> refuses unless one side proves it is a case, and
/// <c>OfficialIdentifier.ProvesCase()</c> accepts only a well-formed ECLI or a sector-6 CELEX. A row
/// carrying only the case's Cellar IRI would be refused by name. <c>case-law_ecli</c> is proven
/// rather than assumed — review/23 records <c>ECLI:EU:C:2020:559</c> on it, and records that it
/// arrives as a LITERAL, the <c>/resource/ecli/</c> resolver having returned 404.
/// </para>
/// <para>
/// ABSENCE IS ASKED FOR, NEVER INFERRED FROM SILENCE. A case with no <c>case-law_ecli</c> is
/// delivered through an explicit <c>FILTER NOT EXISTS</c> branch carrying
/// <see cref="UnboundEcliKind"/>, exactly as the NIM family delivers a missing ELI. A row that
/// simply failed to match would be indistinguishable from a row the publisher never had, which is
/// the false absence S2-A05 exists to prevent.
/// </para>
/// <para>
/// THE KIND MARKER IS NOT THE TERM. <c>?ecli_kind</c> is a <c>BIND</c> this plan computes about a
/// row; it is not the publisher's word for what the row is. A decoder must read the ECLI term
/// itself and may use the marker only to corroborate. Trusting a marker over its term is a defect
/// this seat has already shipped once, on the E1 axiom decoder, and had found against it.
/// </para>
/// </remarks>
public sealed class EuCaseLawDiscoveryPlan
{
    internal const string PublisherEndpoint = "https://publications.europa.eu/webapi/rdf/sparql";
    internal const string Cdm = EuConsolidationDiscoveryPlan.Cdm;

    /// <summary>The predicate carrying a case's ECLI. Proven, and proven to arrive as a literal.</summary>
    public const string CaseLawEcliPredicateIri = Cdm + "case-law_ecli";

    /// <summary>The marker a row carries when the publisher holds no ECLI for that case.</summary>
    public const string UnboundEcliKind = "unbound";

    /// <summary>
    /// The predicate carrying a work's CELEX. Asked for only where it decides whether a case can
    /// be carried at all, which is the branch where the ECLI is absent.
    /// </summary>
    public const string CaseLawCelexPredicateIri = Cdm + "resource_legal_id_celex";

    /// <summary>The marker a row carries when the publisher holds no CELEX for that case either.</summary>
    public const string UnboundCelexKind = "unbound";

    /// <summary>
    /// The marker a row carries when the CELEX was never asked for, because the ECLI answered first.
    /// </summary>
    /// <remarks>
    /// This is deliberately not <see cref="UnboundCelexKind"/>. "We did not ask" and "we asked and
    /// the publisher had none" are different facts, and collapsing them would manufacture exactly
    /// the false absence the <c>FILTER NOT EXISTS</c> branches exist to prevent.
    /// </remarks>
    public const string CelexNotAskedKind = "not_asked";

    /// <summary>
    /// How many acts one request may ask about. Fixed at 50, matching the object-facts family, so
    /// batch size is a property of this plan rather than a caller's choice.
    /// </summary>
    public const int BatchCapacity = 50;

    internal const long PublisherDeliveryCeilingRows = 1_000_000;
    internal const uint Pass1PageLimit = 883;
    internal const uint Pass2PageLimit = 547;
    internal const string PartitionMemberKey = "eu-case-law-links-by-act";

    private const string ResourceId = "urn:uuid:6f2b0c41-9d3a-4e57-9d0e-2b8c5a1f47d3";
    private const string MemberPrefix = "eu-case-law-links-by-act";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] Projection =
    [
        "case_work", "case_predicate", "eu_work", "ecli", "ecli_kind",
        "case_celex", "case_celex_kind",
        "multiplicity", "key_1", "key_2", "key_3", "key_4", "key_5",
    ];

    /// <summary>
    /// The keyset. <c>key_5</c> carries the CELEX and is not decoration: a case whose ECLI is
    /// absent leaves <c>key_4</c> empty, so two CELEX values on one case would otherwise produce
    /// two rows with an identical four-key cursor and the page could not advance past them.
    /// </summary>
    private static readonly string[] Cursor = ["key_1", "key_2", "key_3", "key_4", "key_5"];

    /// <summary>
    /// How many cursor keys this family has. The renderer reads this rather than repeating the
    /// number, because a cursor that grew while the renderer still bound four slots would send a
    /// template with an unfilled slot.
    /// </summary>
    internal static int CursorKeyCount => Cursor.Length;

    private readonly byte[] _canonicalIdentityBytes;

    private EuCaseLawDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "eu-case-law-links-by-act-plan/1",
            "endpoint=" + PublisherEndpoint,
            "method=POST",
            "target=/webapi/rdf/sparql",
            "request_media_type=application/sparql-query",
            "response_media_type=" + ResponseMediaType,
            "batch_capacity=" + BatchCapacity.ToString(CultureInfo.InvariantCulture),
            "ecli_predicate=" + CaseLawEcliPredicateIri,
            "unbound_ecli_kind=" + UnboundEcliKind,
            "celex_predicate=" + CaseLawCelexPredicateIri,
            "unbound_celex_kind=" + UnboundCelexKind,
            "celex_not_asked_kind=" + CelexNotAskedKind,
            "case_predicates=" + string.Join(',', PinnedPredicatesInOrder()),
            "cursor_envelope=" + EnumerationCursorEnvelope.Identity,
            "threshold_detector=" + ThresholdDetectorIdentity,
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + (int)EuCaseLawQueryPass.Pass1 + ":" + Pass1PageLimit,
            "pass_2=" + (int)EuCaseLawQueryPass.Pass2 + ":" + Pass2PageLimit,
            "terminal_page_policy=short_page_terminal",
            "selection_parameters=" + string.Join(',', BatchParameterNames()),
            "pass_parameter=pass_id",
            "cursor_presence_parameter=has_cursor",
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

    public static EuCaseLawDiscoveryPlan Create() => new();

    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    /// <summary>
    /// The five pinned predicates in the fixed order this plan renders and digests them. Read from
    /// <see cref="EuCaseLawPredicateVocabulary"/> rather than restated, so a predicate added or
    /// removed there moves this plan's canonical identity instead of silently disagreeing with it.
    /// </summary>
    internal static IReadOnlyList<string> PinnedPredicatesInOrder()
    {
        var ordered = new[]
        {
            EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            EuCaseLawPredicateVocabulary.CaseLawRequestsAnnulmentOfResourceLegalPredicateUri,
            EuCaseLawPredicateVocabulary.CaseLawDeclaresVoidResourceLegalPredicateUri,
            EuCaseLawPredicateVocabulary.CaseLawDeclaresVoidByPreliminaryRulingResourceLegalPredicateUri,
        };
        Array.Sort(ordered, StringComparer.Ordinal);
        return Array.AsReadOnly(ordered);
    }

    internal static IReadOnlyList<string> BatchParameterNames()
    {
        var names = new string[BatchCapacity];
        for (var index = 0; index < BatchCapacity; index++)
        {
            names[index] = "requested_work_" + (index + 1).ToString("D2", CultureInfo.InvariantCulture);
        }

        return Array.AsReadOnly(names);
    }

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

    internal static uint PageLimit(EuCaseLawQueryPass pass) => pass switch
    {
        EuCaseLawQueryPass.Pass1 => Pass1PageLimit,
        EuCaseLawQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    /// <summary>
    /// Canonicalizes a batch to Appendix A's exact lexical form, refuses a null, empty,
    /// over-capacity or duplicate batch, and returns it sorted ordinally.
    /// </summary>
    internal static string[] CanonicalizeBatch(IReadOnlyList<string> batchWorks)
    {
        ArgumentNullException.ThrowIfNull(batchWorks);
        if (batchWorks.Count is 0 or > BatchCapacity)
        {
            throw new ArgumentException(
                $"A batch must name one to {BatchCapacity} EU works.", nameof(batchWorks));
        }

        var canonical = new string[batchWorks.Count];
        for (var index = 0; index < batchWorks.Count; index++)
        {
            var value = batchWorks[index] ??
                throw new ArgumentException("A batch member cannot be null.", nameof(batchWorks));
            canonical[index] = EuPackRootCanonicalForm.TryCanonicalize(value, out _) ??
                throw new ArgumentException(
                    $"Batch member '{value}' does not reduce to Appendix A's exact lexical form.",
                    nameof(batchWorks));
        }

        if (canonical.Distinct(StringComparer.Ordinal).Count() != canonical.Length)
        {
            throw new ArgumentException(
                "A batch cannot name the same canonical EU work twice.", nameof(batchWorks));
        }

        Array.Sort(canonical, StringComparer.Ordinal);
        return canonical;
    }

    /// <summary>
    /// Pads a canonicalized, sorted batch to exactly <see cref="BatchCapacity"/> by repeating its own
    /// lexicographically greatest member, so every request carries the same shape whatever the batch
    /// size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PADDING IS A TRANSPORT SHAPE AND MUST NOT REACH THE GRAPH JOIN. An earlier version of this
    /// remark claimed the duplicates were harmless because "<c>VALUES</c> set semantics fold" them.
    /// <b>That was false and it produced a real defect.</b> SPARQL solution mappings are a multiset:
    /// duplicate <c>VALUES</c> rows are preserved, and <c>COUNT(*)</c> counts them. A one-act batch
    /// padded to fifty therefore made every matching publisher edge contribute fifty solutions and
    /// reported <c>multiplicity = 50</c>, with the inflation depending on how full the batch happened
    /// to be. That is invented publisher multiplicity, which is exactly what this family exists to
    /// report honestly.
    /// </para>
    /// <para>
    /// The padding is kept, because a constant request shape is worth having, but the slots now enter
    /// through a <c>SELECT DISTINCT ?eu_work</c> subquery so only the distinct acts reach the join.
    /// The transport carries fifty slots; the question asks about the acts the caller named.
    /// </para>
    /// <para>
    /// The pattern was borrowed from the object-facts family, which pads the same way. What did not
    /// carry across is that that family projects rows without aggregating them, while this one
    /// computes a per-row <c>COUNT(*)</c>. Copying a construct is not the same as copying the
    /// conditions that made it safe.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The batch members exactly as this plan puts them to the publisher.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A membership check downstream compares a delivered key against what was asked, and the two
    /// have to be in one lexical form. They were not. <see cref="BindCount"/> and
    /// <see cref="BindPage"/> send <c>PadBatch(CanonicalizeBatch(...))</c>, and
    /// <c>EuPackRootCanonicalForm.TryCanonicalize</c> returns <c>"http://" + trimmed</c> — it
    /// rewrites <c>https://</c> to <c>http://</c> and drops one trailing slash. So a caller's own
    /// spelling is not what the publisher ever sees, and <c>?key_3</c> comes back in this form.
    /// </para>
    /// <para>
    /// Exposed rather than re-derived by the caller, so the rule keeps one owner. The padding is not
    /// included because it only repeats the last real member, so this is the exact set asked about.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> RequestedPartitionMembers(IReadOnlyList<string> batchWorks) =>
        Array.AsReadOnly(CanonicalizeBatch(batchWorks));

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

    /// <summary>Binds the count request for one batch of acts.</summary>
    public EuCaseLawBoundQuery BindCount(
        EuCaseLawQueryPass pass,
        IReadOnlyList<string> batchWorks,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, pass, batchWorks, null, new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    /// <summary>Binds one page request for one batch of acts, optionally continuing from a cursor.</summary>
    public EuCaseLawBoundQuery BindPage(
        EuCaseLawQueryPass pass,
        IReadOnlyList<string> batchWorks,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, pass, batchWorks, cursor, new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private EuCaseLawBoundQuery Bind(
        bool isPage,
        EuCaseLawQueryPass pass,
        IReadOnlyList<string> batchWorks,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);
        var padded = PadBatch(CanonicalizeBatch(batchWorks));

        // THE SELECTION COMES FIRST AND pass_id FOLLOWS IT, because that is the order
        // RepeatedEnumerationDeliveryProof.RequireInputRoleShape requires: it builds its expectation
        // as SelectionParameterNames.Append(PassParameterName) and compares the ordered roles by
        // sequence. This bound pass_id first, so a fully requested, publisher-consistent delivery
        // reached DeliveryProofRefused - "the ordered machine input parameter roles are not exact" -
        // instead of producing a receipt. The family could refuse correctly and could never succeed.
        // Found in review on head 067aa290.
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
        else if (cursor is not null)
        {
            throw new ArgumentException("A count query cannot carry a cursor.", nameof(cursor));
        }

        var family = isPage ? PageQueryFamilyRef : CountQueryFamilyRef;
        var input = MachineQueryInputArtifact.Create(
            inputResourceId, family, PartitionMemberKey, response, parameters);
        var renderer = new EuCaseLawSparqlRenderer(this, isPage, rendererSource);
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
        var request = MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer);
        return new(machinePlan, machinePlanRef, input, request);
    }

    private static (string Count, string Page) BuildTemplates()
    {
        var valuesBlock = string.Join('\n', BatchParameterNames()
            .Select(static name => "    {" + name + ":iri}"));
        var predicateBlock = string.Join('\n', PinnedPredicatesInOrder()
            .Select(static predicate => "    <" + predicate + ">"));

        var rows = $$"""
            SELECT ?case_work ?case_predicate ?eu_work ?ecli ?ecli_kind ?case_celex ?case_celex_kind (COUNT(*) AS ?multiplicity) WHERE {
              VALUES ?lex_pass_id { {pass_id:uint} }
              {
                SELECT DISTINCT ?eu_work WHERE {
                  VALUES ?eu_work {
            {{valuesBlock}}
                  }
                }
              }
              VALUES ?case_predicate {
            {{predicateBlock}}
              }
              ?case_work ?case_predicate ?eu_work .
              {
                ?case_work <{{CaseLawEcliPredicateIri}}> ?ecli .
                BIND(IF(isIRI(?ecli), "iri", IF(isLiteral(?ecli), "literal", "unsupported_blank_node")) AS ?ecli_kind)
                BIND("{{CelexNotAskedKind}}" AS ?case_celex_kind)
              }
              UNION
              {
                FILTER NOT EXISTS { ?case_work <{{CaseLawEcliPredicateIri}}> ?missing_ecli }
                BIND("{{UnboundEcliKind}}" AS ?ecli_kind)
                {
                  ?case_work <{{CaseLawCelexPredicateIri}}> ?case_celex .
                  BIND(IF(isLiteral(?case_celex), "literal", IF(isIRI(?case_celex), "iri", "unsupported_blank_node")) AS ?case_celex_kind)
                }
                UNION
                {
                  FILTER NOT EXISTS { ?case_work <{{CaseLawCelexPredicateIri}}> ?missing_celex }
                  BIND("{{UnboundCelexKind}}" AS ?case_celex_kind)
                }
              }
            }
            GROUP BY ?case_work ?case_predicate ?eu_work ?ecli ?ecli_kind ?case_celex ?case_celex_kind
            """;

        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;

        var page = $$"""
            SELECT ?case_work ?case_predicate ?eu_work ?ecli ?ecli_kind ?case_celex ?case_celex_kind ?multiplicity ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 WHERE {
              {
            {{Indent(Indent(rows))}}
              }
              BIND(STR(?case_work) AS ?key_1)
              BIND(STR(?case_predicate) AS ?key_2)
              BIND(STR(?eu_work) AS ?key_3)
              BIND(COALESCE(STR(?ecli), "") AS ?key_4)
              BIND(COALESCE(STR(?case_celex), "") AS ?key_5)
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
              FILTER(?has_cursor = 0 || !(
                ?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && ?key_4 = ?last_key_4 &&
                ?key_5 = ?last_key_5))
            }
            ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5
            LIMIT {page_limit:uint}
            """;

        return (Normalize(count), Normalize(page));
    }

    private static string Indent(string value) => string.Join('\n',
        Normalize(value).Split('\n').Select(static line => "  " + line));

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

/// <summary>
/// Renders one bound case-law request. Every slot is filled from the ordered parameter set and each
/// must occur exactly once in the template, so a template edit that drops or duplicates a slot is a
/// build-time-shaped failure rather than a silently different question.
/// </summary>
internal sealed class EuCaseLawSparqlRenderer : IMachineQueryRenderer
{
    private readonly EuCaseLawDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal EuCaseLawSparqlRenderer(
        EuCaseLawDiscoveryPlan plan,
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
        var pass = (EuCaseLawQueryPass)Integer(parameters, "pass_id");
        var limit = EuCaseLawDiscoveryPlan.PageLimit(pass);
        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));

        foreach (var name in EuCaseLawDiscoveryPlan.BatchParameterNames())
        {
            query = Replace(query, "{" + name + ":iri}", SparqlIriTerm(Literal(parameters, name)));
        }

        var batchCount = EuCaseLawDiscoveryPlan.BatchCapacity;
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
            (hasCursor == 1 ? EuCaseLawDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= EuCaseLawDiscoveryPlan.CursorKeyCount; ordinal++)
        {
            var name = "last_key_" + ordinal;
            var value = hasCursor == 0 ? string.Empty : CursorValue(parameters, name);
            query = Replace(query, "{" + name + ":sparql_string}", SparqlQueryText.StringLiteral(value));
        }

        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        EuCaseLawDiscoveryPlan.PublisherEndpoint, Encoding.UTF8.GetBytes(query));

    private static long Integer(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.");

    /// <summary>
    /// Wraps a canonical batch member as a SPARQL IRI term, refusing anything that could close the
    /// angle brackets or split the term. The batch is already canonicalized, so this is the second
    /// line rather than the only one.
    /// </summary>
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

    private static string Literal(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherLiteral && value.TextValue is not null
            ? value.TextValue
            : throw new ArgumentException($"The literal input {name} is missing or invalid.");

    private static string CursorValue(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
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
