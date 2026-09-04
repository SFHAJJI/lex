using System.Globalization;
using System.Net;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Shared plumbing for driving a real <see cref="EuRepeatedEnumerationExecutor"/> and
/// <see cref="EuQueryExecutionAdapter"/> against a scripted, family-classifying transport, mirroring
/// <see cref="LuxembourgAcquisitionTestFixture"/>'s own role for Luxembourg. Unlike that fixture (one
/// family, one session, hand-rolled pass loop, because it predates the executor), this one drives the
/// full, real <see cref="EuRepeatedEnumerationExecutor"/> end to end: nothing here re-implements a
/// pass loop, a cursor check or a receipt build.
/// </summary>
internal static class EuAcquisitionTestFixture
{
    private const string EuQueryUri = "https://publications.europa.eu/webapi/rdf/sparql";

    // Every EU CDM predicate/authority IRI this fixture's row builders need, transcribed from the
    // exact constants EuObjectFactsDiscoveryPlan.CdmIri and EuConsolidationDiscoveryPlan produce
    // (both internal to Lex.V3.Contracts; this path claim's own tests read the publisher-facing
    // wire shape those internal constants render, not the constants themselves).
    private const string Cdm = "http://publications.europa.eu/ontology/cdm#";
    internal const string ResourceLegalIdCelex = Cdm + "resource_legal_id_celex";
    internal const string ResourceLegalType = Cdm + "resource_legal_type";
    internal const string WorkHasResourceType = Cdm + "work_has_resource-type";
    internal const string WorkDateDocument = Cdm + "work_date_document";
    internal const string ActConsolidatedDate = Cdm + "act_consolidated_date";
    internal const string DateCreationLegacy = Cdm + "date_creation_legacy";
    internal const string ResourceLegalInForce = Cdm + "resource_legal_in-force";
    internal const string WorkIsAboutConceptEurovoc = Cdm + "work_is_about_concept_eurovoc";
    internal const string ResourceLegalIsAboutConceptDirectoryCode =
        Cdm + "resource_legal_is_about_concept_directory-code";
    internal const string AmendsPredicate = Cdm + "resource_legal_amends_resource_legal";
    internal const string CorrectsPredicate = Cdm + "resource_legal_corrects_resource_legal";
    internal const string BasedOnPredicate = Cdm + "resource_legal_based_on_resource_legal";
    internal const string ConsolidatedBasedOnPredicate = Cdm + "act_consolidated_based_on_resource_legal";
    internal const string ExpressionBelongsToWork = Cdm + "expression_belongs_to_work";
    internal const string RegulationResourceType = "http://publications.europa.eu/resource/authority/resource-type/REG";

    /// <summary>The nine object-authority CDM predicates, in the exact order family P asks them.</summary>
    internal static readonly string[] ObjectAuthorityPredicates =
    [
        ResourceLegalIdCelex, ResourceLegalType, WorkHasResourceType, WorkDateDocument, ActConsolidatedDate,
        DateCreationLegacy, ResourceLegalInForce, WorkIsAboutConceptEurovoc, ResourceLegalIsAboutConceptDirectoryCode,
    ];

    /// <summary>The four read relation-family predicates, in the exact order family P asks them.</summary>
    internal static readonly string[] RelationPredicates =
        [AmendsPredicate, CorrectsPredicate, BasedOnPredicate, ConsolidatedBasedOnPredicate];

    internal static readonly string[] ObjectFactsProjection =
        ["object", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6"];

    internal static readonly string[] ExpressionFactsProjection =
        ["parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7"];

    internal static readonly string[] RootWatermarkProjection =
        ["object", "value", "value_kind", "datatype_iri", "language_tag", "key_1", "key_2", "key_3", "key_4", "key_5"];

    internal static readonly string[] CensusFamilyProjection =
        ["base_celex", "base", "state", "family_multiplicity", "state_key"];

    /// <summary>The witness page's own three-column projection: <c>?entry ?entry_key ?watermark</c>.</summary>
    internal static readonly string[] WitnessProjection = ["entry", "entry_key", "watermark"];

    internal static (EuConsolidationDiscoveryPlan Plan, string PlanResourceId) BuildCensusPlan() =>
        (EuConsolidationDiscoveryPlan.Create(), NewUrn());

    internal static (EuObjectFactsDiscoveryPlan Plan, string PlanResourceId) BuildObjectFactsPlan() =>
        (EuObjectFactsDiscoveryPlan.Create(), NewUrn());

    internal static MachineQueryRendererSource BuildRendererSource(int seed) =>
        MachineQueryRendererSource.Open(
            Artifact(NewUrn(), Encoding.UTF8.GetBytes($"eu-fixture-renderer-source-{seed}")),
            Encoding.UTF8.GetBytes($"eu-fixture-renderer-source-{seed}"));

    /// <summary>
    /// The one reusable robots-negotiation witness this fixture ever needs: any bound request that
    /// targets the EU SPARQL endpoint resolves to the same official EU profile
    /// (<c>OfficialMachineQuerySourceProfiles.ResolveFor</c>), and robots negotiation depends only on
    /// that profile, never on which family the caller is about to enumerate.
    /// </summary>
    internal static BoundMachineRequest SourceWitness() => MachineRequestTestFixture.EuropeanUnionRequest();

    /// <summary>
    /// D1-06c-EU defect 4: the document-fetch profile's own robots-negotiation witness. Distinct from
    /// <see cref="SourceWitness"/>, which targets the SPARQL POST profile -- a document-fetch GET
    /// resolves to a genuinely different official profile
    /// (<c>OfficialMachineQuerySourceProfileId.EuropeanUnionDocumentFetch</c>), so it needs its own
    /// bound request to resolve against. The exact ps-id here is a fixture placeholder: profile
    /// resolution depends only on the resource URI's own shape
    /// (<c>EuDocumentFetchAddress.IsAdmittedResourceUri</c>), never on which real object it names.
    /// </summary>
    internal static BoundMachineRequest DocumentFetchSourceWitness()
    {
        var address = EuDocumentFetchAddress.TryCreate(
            "celex", "00000000", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng, out var refusal)
            ?? throw new InvalidOperationException($"Fixture document-fetch witness minting refused: {refusal}.");
        var plan = new EuDocumentFetchPlan(address);
        return plan.Bind(
            "urn:uuid:00000000-0000-4000-8000-0000000000f8",
            "urn:uuid:00000000-0000-4000-8000-0000000000f9",
            BuildRendererSource(9001)).Request;
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static SourceArtifactRef Artifact(string resourceId, ReadOnlySpan<byte> bytes) =>
        new(resourceId, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));

    // ---- JSON term and row builders. EU dialect: "literal", never LU's "typed-literal". ----

    private static string J(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    internal static string EuCountJson(long count) =>
        "{\"head\":{\"link\":[],\"vars\":[\"count\"]}," +
        "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{" +
        "\"count\":{\"type\":\"literal\",\"value\":" + J(count.ToString(CultureInfo.InvariantCulture)) +
        ",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\"}}]}}";

    internal static string EmptyRowsJson(IReadOnlyList<string> projection) =>
        "{\"head\":{\"link\":[],\"vars\":" + System.Text.Json.JsonSerializer.Serialize(projection) + "}," +
        "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[]}}";

    private static string RowsJson(IReadOnlyList<string> projection, IReadOnlyList<string> rows) =>
        "{\"head\":{\"link\":[],\"vars\":" + System.Text.Json.JsonSerializer.Serialize(projection) + "}," +
        "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[" + string.Join(',', rows) + "]}}";

    private static string PlainLiteral(string value) => "{\"type\":\"literal\",\"value\":" + J(value) + "}";

    private static string Iri(string value) => "{\"type\":\"uri\",\"value\":" + J(value) + "}";

    /// <summary>One family-P outcome row: a real IRI value, or the explicit unbound marker.</summary>
    internal static string ObjectFactRow(string objectIri, string predicateIri, string? valueIri)
    {
        var kind = valueIri is null ? "unbound" : "iri";
        var fields = new List<(string Var, string Term)>
        {
            ("object", Iri(objectIri)),
            ("predicate", Iri(predicateIri)),
        };
        if (valueIri is not null)
        {
            fields.Add(("value", Iri(valueIri)));
        }

        fields.Add(("value_kind", PlainLiteral(kind)));
        fields.Add(("datatype_iri", PlainLiteral("")));
        fields.Add(("language_tag", PlainLiteral("")));
        fields.Add(("key_1", PlainLiteral(objectIri)));
        fields.Add(("key_2", PlainLiteral(predicateIri)));
        fields.Add(("key_3", PlainLiteral(kind)));
        fields.Add(("key_4", PlainLiteral(valueIri ?? "")));
        fields.Add(("key_5", PlainLiteral("")));
        fields.Add(("key_6", PlainLiteral("")));
        return Row(fields);
    }

    internal static string ObjectFactsRowsJson(IReadOnlyList<string> rows) => RowsJson(ObjectFactsProjection, rows);

    /// <summary>
    /// Every family-P outcome row for ONE object, already reordered to the exact ascending
    /// <c>key_1..key_6</c> cursor order the real page template's own <c>ORDER BY</c> produces
    /// (<see cref="EuRepeatedEnumerationExecutor"/>'s own strict cursor check refuses
    /// <c>CursorDidNotAdvance</c> otherwise). <c>key_1</c> (the object) is identical for every row
    /// here, so ordinal sorting by predicate IRI alone (<c>key_2</c>) already produces the correct
    /// order: every predicate this fixture asks is distinct, so no row ever needs a <c>key_3</c>/
    /// <c>key_4</c> tiebreak.
    /// </summary>
    internal static IReadOnlyList<string> SortedObjectFactRows(
        string objectIri, IEnumerable<(string PredicateIri, string? ValueIri)> outcomes) =>
        outcomes
            .OrderBy(static outcome => outcome.PredicateIri, StringComparer.Ordinal)
            .Select(outcome => ObjectFactRow(objectIri, outcome.PredicateIri, outcome.ValueIri))
            .ToArray();

    /// <summary>
    /// One family-X row establishing one Expression's own <c>expression_belongs_to_work</c> self
    /// closure: <c>?object &lt;expression_belongs_to_work&gt; ?parent</c>, bound in both the join and
    /// the predicate/value pair the row template independently re-asks.
    /// </summary>
    internal static string ExpressionFactRow(string parentIri, string expressionIri)
    {
        var fields = new List<(string Var, string Term)>
        {
            ("parent", Iri(parentIri)),
            ("object", Iri(expressionIri)),
            ("predicate", Iri(ExpressionBelongsToWork)),
            ("value", Iri(parentIri)),
            ("value_kind", PlainLiteral("iri")),
            ("datatype_iri", PlainLiteral("")),
            ("language_tag", PlainLiteral("")),
            ("key_1", PlainLiteral(expressionIri)),
            ("key_2", PlainLiteral(ExpressionBelongsToWork)),
            ("key_3", PlainLiteral("iri")),
            ("key_4", PlainLiteral(parentIri)),
            ("key_5", PlainLiteral("")),
            ("key_6", PlainLiteral("")),
            ("key_7", PlainLiteral(parentIri)),
        };
        return Row(fields);
    }

    internal static string ExpressionFactsRowsJson(IReadOnlyList<string> rows) => RowsJson(ExpressionFactsProjection, rows);

    /// <summary>One family-W row: one root's own <c>cmr:lastModificationDate</c> lexical value.</summary>
    internal static string RootWatermarkRow(string rootIri, string watermarkLexical)
    {
        var fields = new List<(string Var, string Term)>
        {
            ("object", Iri(rootIri)),
            ("value", PlainLiteral(watermarkLexical)),
            ("value_kind", PlainLiteral("literal")),
            ("datatype_iri", PlainLiteral("")),
            ("language_tag", PlainLiteral("")),
            ("key_1", PlainLiteral(rootIri)),
            ("key_2", PlainLiteral("literal")),
            ("key_3", PlainLiteral(watermarkLexical)),
            ("key_4", PlainLiteral("")),
            ("key_5", PlainLiteral("")),
        };
        return Row(fields);
    }

    internal static string RootWatermarkRowsJson(IReadOnlyList<string> rows) => RowsJson(RootWatermarkProjection, rows);

    /// <summary>
    /// Defect 6's own driving row: a family-W row whose <c>value_kind</c> is <c>"unbound"</c> rather
    /// than <c>"literal"</c>, mirroring exactly what the real page template's own
    /// <c>FILTER NOT EXISTS</c> branch emits for a root that carries no <c>cmr:lastModificationDate</c>
    /// at all -- no <c>value</c> field is bound, exactly as <see cref="ObjectFactRow"/> already omits
    /// it when its own <c>valueIri</c> is null.
    /// </summary>
    internal static string RootWatermarkUnboundRow(string rootIri)
    {
        var fields = new List<(string Var, string Term)>
        {
            ("object", Iri(rootIri)),
            ("value_kind", PlainLiteral("unbound")),
            ("datatype_iri", PlainLiteral("")),
            ("language_tag", PlainLiteral("")),
            ("key_1", PlainLiteral(rootIri)),
            ("key_2", PlainLiteral("unbound")),
            ("key_3", PlainLiteral("")),
            ("key_4", PlainLiteral("")),
            ("key_5", PlainLiteral("")),
        };
        return Row(fields);
    }

    /// <summary>
    /// One witness page row: one delivered <c>?entry</c> at one <c>?watermark</c>, with
    /// <c>?entry_key</c> bound to <c>STR(?entry)</c> exactly as the real page template's own
    /// <c>BIND(STR(?entry) AS ?entry_key)</c> does.
    /// </summary>
    internal static string WitnessRow(string entryIri, string watermarkLexical)
    {
        var fields = new List<(string Var, string Term)>
        {
            ("entry", Iri(entryIri)),
            ("entry_key", PlainLiteral(entryIri)),
            ("watermark", PlainLiteral(watermarkLexical)),
        };
        return Row(fields);
    }

    internal static string WitnessRowsJson(IReadOnlyList<string> rows) => RowsJson(WitnessProjection, rows);

    /// <summary>
    /// The witness's own confirmed-termination script: a page carrying only the boundary's own
    /// retained entry (nothing beyond it), repeated once so
    /// <c>EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync</c>'s own empty-successor
    /// confirmation (see its remarks) observes the identical short page twice and stops.
    /// </summary>
    internal static string[] WitnessEmptyTraversalScript(string boundaryEntryIri, string boundaryWatermarkLexical)
    {
        var page = WitnessRowsJson([WitnessRow(boundaryEntryIri, boundaryWatermarkLexical)]);
        return [page, page];
    }

    /// <summary>
    /// The witness's own script for a traversal that observes exactly one real entry beyond the
    /// census bound: the first page rereads the boundary (its own retained entry) and carries one new
    /// entry beyond it; the second and third pages then confirm the new boundary is terminal (the
    /// executor's own empty-successor confirmation -- see
    /// <c>EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync</c>'s own remarks).
    /// </summary>
    internal static string[] WitnessOneNewEntryTraversalScript(
        string boundaryEntryIri,
        string boundaryWatermarkLexical,
        string newEntryIri,
        string newWatermarkLexical)
    {
        var firstPage = WitnessRowsJson(
        [
            WitnessRow(boundaryEntryIri, boundaryWatermarkLexical),
            WitnessRow(newEntryIri, newWatermarkLexical),
        ]);
        var confirmPage = WitnessRowsJson([WitnessRow(newEntryIri, newWatermarkLexical)]);
        return [firstPage, confirmPage, confirmPage];
    }

    /// <summary>
    /// One family row: one discovered consolidated state of one seed's own base Work.
    /// <c>state_key</c> is <c>STR(?state)</c>, mirroring the real page template's own
    /// <c>BIND(STR(?state) AS ?state_key)</c>, so delivering rows whose <c>state</c> IRIs already
    /// sort ordinally ascending keeps them in the strictly-ascending cursor order
    /// <see cref="EuRepeatedEnumerationExecutor"/>'s own strict cursor check requires.
    /// </summary>
    internal static string CensusFamilyRow(string baseCelex, string baseIri, string stateIri)
    {
        var fields = new List<(string Var, string Term)>
        {
            ("base_celex", PlainLiteral(baseCelex)),
            ("base", Iri(baseIri)),
            ("state", Iri(stateIri)),
            ("family_multiplicity", PlainLiteral("1")),
            ("state_key", PlainLiteral(stateIri)),
        };
        return Row(fields);
    }

    internal static string CensusFamilyRowsJson(IReadOnlyList<string> rows) => RowsJson(CensusFamilyProjection, rows);

    private static string Row(IReadOnlyList<(string Var, string Term)> fields) =>
        "{" + string.Join(',', fields.Select(static field => J(field.Var) + ":" + field.Term)) + "}";

    // ---- The scripted, family-classifying transport. ----

    /// <summary>
    /// One family's full scripted response sequence, in the exact order
    /// <see cref="EuRepeatedEnumerationExecutor"/>'s own two-pass loop sends requests: pass 1's count,
    /// then its pages until an empty terminal, then pass 2's identical shape.
    /// </summary>
    internal sealed record FamilyScript(string FamilyTag, IReadOnlyList<string> ResponseBodies);

    internal static FamilyScript ScriptFor(string familyTag, long selected, IReadOnlyList<string> firstPageRows, string[] projection)
    {
        var pass = selected == 0
            ? new[] { EuCountJson(selected), EmptyRowsJson(projection) }
            : new[] { EuCountJson(selected), RowsJson(projection, firstPageRows), EmptyRowsJson(projection) };
        return new FamilyScript(familyTag, pass.Concat(pass).ToArray());
    }

    /// <summary>
    /// Classifies a rendered SPARQL request body by the one substring unique to each of the five
    /// shapes this run can drive, never by request order. The witness must be checked before family
    /// W: both ask <c>cmr#lastModificationDate</c> (the witness's own predicate is that same watermark
    /// predicate), but only the witness's own template carries <c>isIRI(?entry)</c> -- family W's own
    /// template asks a VALUES-bound batch of <c>?object</c> and never binds an <c>?entry</c> variable
    /// at all. Family X is the only one that ever asks <c>expression_belongs_to_work</c>, family P the
    /// only one that asks <c>resource_legal_type</c> (the census family's own <c>Family</c> set asks
    /// none of the four), so an unclassified body is the census family by elimination.
    /// </summary>
    private static string ClassifyFamily(string body)
    {
        if (body.Contains("isIRI(?entry)", StringComparison.Ordinal))
        {
            return "Witness";
        }

        if (body.Contains("expression_belongs_to_work", StringComparison.Ordinal))
        {
            return "X";
        }

        if (body.Contains("lastModificationDate", StringComparison.Ordinal))
        {
            return "W";
        }

        if (body.Contains("resource_legal_type", StringComparison.Ordinal))
        {
            return "P";
        }

        return "Census";
    }

    /// <summary>
    /// A transport that answers the EU profile's own two-hop robots route (a 301 from
    /// <c>publications.europa.eu</c> to <c>op.europa.eu</c>, then a plain-text Allow-all there) purely
    /// by request URI, and every SPARQL POST to the query endpoint by classifying its body into one of
    /// <paramref name="scripts"/> and replaying that family's own script in order. Robust against
    /// interleaving across the several sessions one adapter run opens: robots needs no state at all,
    /// and each family's own occurrence counter is independent of every other family's.
    /// </summary>
    internal sealed class ClassifyingHandler(
        IReadOnlyDictionary<string, FamilyScript> scripts,
        Func<HttpRequestMessage, HttpResponseMessage>? documentFetchResponse = null) : HttpMessageHandler
    {
        private static readonly Lazy<byte[]> DefaultDocumentFetchBody = new(() => File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-xhtml-200-body.bin")));

        private readonly Dictionary<string, int> _occurrence = new(StringComparer.Ordinal);
        private int _sendCount;

        internal int SendCount => Volatile.Read(ref _sendCount);

        /// <summary>
        /// How many real requests this handler has classified into <paramref name="family"/> and
        /// answered so far -- real dispatch counts, not test-declared expectations. Used to prove the
        /// witness query is actually sent over HTTP (<c>OccurrenceCountFor("Witness")</c> must be
        /// nonzero after a real run) rather than assumed.
        /// </summary>
        internal int OccurrenceCountFor(string family)
        {
            lock (_occurrence)
            {
                return _occurrence.TryGetValue(family, out var value) ? value : 0;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            if (request.RequestUri!.Host == "publications.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                return TextResponse(request, HttpStatusCode.MovedPermanently, "moved", "https://op.europa.eu/robots.txt");
            }

            if (request.RequestUri.Host == "op.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                return TextResponse(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n");
            }

            // D1-06c-EU defect 4: every family this fixture drives real EU seeds through, so every one
            // of them mints a real Minted document-fetch address for its own Cellar objects, and the
            // adapter now actually sends a real GET for each one. A GET carries no request body, so it
            // must be routed before the family-classifying POST branch below, which requires one.
            // Callers that do not care about the document-fetch response get a real, admitted 200
            // (the real GDPR xhtml canary body, Fixtures/EuDocumentFetch/gdpr-xhtml-200-body.bin) by
            // default; callers that do (this file's own defect-4 tests) supply their own
            // documentFetchResponse.
            if (request.Method == HttpMethod.Get)
            {
                return (documentFetchResponse ?? DefaultDocumentFetchResponse)(request);
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var family = ClassifyFamily(body);
            lock (_occurrence)
            {
                var index = _occurrence.TryGetValue(family, out var value) ? value : 0;
                _occurrence[family] = index + 1;
                var script = scripts[family];
                if (index >= script.ResponseBodies.Count)
                {
                    throw new InvalidOperationException(
                        $"Family '{family}' requested more responses ({index + 1}) than its script has ({script.ResponseBodies.Count}).");
                }

                return JsonResponse(request, script.ResponseBodies[index]);
            }
        }

        private static HttpResponseMessage DefaultDocumentFetchResponse(HttpRequestMessage request) =>
            BinaryResponse(
                request, HttpStatusCode.OK, DefaultDocumentFetchBody.Value, "application/xhtml+xml;charset=UTF-8");
    }

    /// <summary>A real-status, real-content-type, real-byte-length response, for a document-fetch GET's own scripted answers.</summary>
    internal static HttpResponseMessage BinaryResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        byte[] body,
        string? contentType = null,
        string? location = null)
    {
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture));
        if (contentType is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11, RequestMessage = request, Content = content,
        };
        if (location is not null)
        {
            response.Headers.TryAddWithoutValidation("Location", location);
        }

        return response;
    }

    internal static HttpResponseMessage JsonResponse(HttpRequestMessage request, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/sparql-results+json");
        content.Headers.TryAddWithoutValidation("Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version11, RequestMessage = request, Content = content,
        };
    }

    private static HttpResponseMessage TextResponse(
        HttpRequestMessage request, HttpStatusCode status, string body, string? location = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
        content.Headers.TryAddWithoutValidation("Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture));
        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11, RequestMessage = request, Content = content,
        };
        if (location is not null)
        {
            response.Headers.Location = new Uri(location);
        }

        return response;
    }

    /// <summary>
    /// A trivial in-memory content-addressed store publishing enforced (<see cref="CustodyProtection.LockedTime"/>)
    /// protection for every write, mirroring <c>LuxembourgQueryExecutionAdapterTests.InMemoryCustodyStore</c>
    /// exactly: a real store this run's own retention floor checks pass against, never a bare
    /// unenforced double.
    /// </summary>
    /// <param name="unenforceDigest">
    /// D1-06c-EU defect 4's own floor-below-run test: when supplied, a write whose own content
    /// digest matches this predicate publishes <see cref="CustodyProtection.NotEnforced"/> instead of
    /// <see cref="CustodyProtection.LockedTime"/> -- everything else this run writes (the scope
    /// manifest, the document-fetch evidence document) stays floored, so only the one write this
    /// predicate targets is unenforced, never the whole store.
    /// </param>
    internal sealed class EuInMemoryCustodyStore(Func<string, bool>? unenforceDigest = null)
        : Lex.V3.Contracts.Custody.ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _byDigest = new(StringComparer.Ordinal);

        public Task<Lex.V3.Contracts.Custody.DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            Lex.V3.Contracts.Custody.CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            var frozen = bytes.ToArray();
            var digest = Lex.V3.Contracts.Custody.CustodyDigest.Of(frozen);
            _byDigest[digest] = frozen;
            var reference = new Lex.V3.Contracts.Custody.DurableBlobRef(
                Lex.V3.Contracts.Custody.CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
            var observedAt = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
            // CustodyPolicyEvidence's own ValidateImmutableProtection admits only two exact pairs
            // under CustodyVerificationProfile.ImmutableObject1: (NightlyFloor90d, LockedTime) and
            // (LegalHoldEvidence, ActiveLegalHold). An unenforced write is instead reported the way a
            // real bare filesystem store does (FileSystemCustodyStore, LuxembourgQueryExecutionAdapterTests
            // own unfloored test): CustodyVerificationProfile.FileSystemUnenforced1, no policy key, no
            // protectedUntil.
            var policy = unenforceDigest?.Invoke(digest) == true
                ? new Lex.V3.Contracts.Custody.CustodyPolicyEvidence(
                    Lex.V3.Contracts.Custody.CustodySchemaIds.CustodyPolicyEvidence,
                    reference,
                    Lex.V3.Contracts.Custody.CustodyVerificationProfile.FileSystemUnenforced1,
                    null,
                    Lex.V3.Contracts.Custody.CustodyProtection.NotEnforced,
                    observedAt,
                    null)
                : new Lex.V3.Contracts.Custody.CustodyPolicyEvidence(
                    Lex.V3.Contracts.Custody.CustodySchemaIds.CustodyPolicyEvidence,
                    reference,
                    Lex.V3.Contracts.Custody.CustodyVerificationProfile.ImmutableObject1,
                    Guid.Parse("00000000-0000-0000-0000-0000000000e2"),
                    Lex.V3.Contracts.Custody.CustodyProtection.LockedTime,
                    observedAt,
                    observedAt.AddDays(91));
            return Task.FromResult(new Lex.V3.Contracts.Custody.DurableBlobWriteReceipt(
                Lex.V3.Contracts.Custody.CustodySchemaIds.DurableBlobWriteReceipt, reference, policy));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            Lex.V3.Contracts.Custody.DurableBlobRef reference, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[reference.ContentSha256]);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[contentSha256]);
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks));

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks);
    }
}
