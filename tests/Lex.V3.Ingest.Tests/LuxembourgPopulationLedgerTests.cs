using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// #419 slice 7: the never-consolidated population ledger, folded from a run's own evidence.
/// Offline, through the scripted transport; no publisher traffic and no live population run.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THESE PROVE. That a delivered run carries a ledger built from the acts it actually saw,
/// and that the three ways the fold can fail each refuse the run BY NAME rather than quietly
/// producing a smaller population. The last is the whole point of the slice: a count that drops
/// what it could not classify is worse than no count, because nothing downstream can tell.
/// </para>
/// <para>
/// The transport is scripted by ordinal, as its sibling acquisition file scripts it: a request
/// these tests did not intend to send lands on an ordinal the script does not name and fails.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgPopulationLedgerTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Types = "http://data.legilux.public.lu/resource/authority/resource-type/";
    private const string Formats = "http://data.legilux.public.lu/resource/authority/user-format/";
    private const string Parent = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string Act = Parent + "/jo";
    private const string Expression = Act + "/fr";
    private const string ManifestationPdfA = Expression + "/pdfa";
    private const string ItemPdfA = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/pdfa/eli-etat-leg-loi-2026-01-01-a1-jo-fr-pdfa.pdf";
    private static readonly string CcBy = VerifiedLuxembourgSourceProfile.AdmittingLicence;
    private static readonly byte[] PdfABytes = Encoding.ASCII.GetBytes("%PDF-1.7 gazette pdfa body\n%%EOF\n");

    /// <summary>
    /// A delivered run carries the ledger, and the ledger holds the act the run actually saw, in
    /// scope, with the Gazette body set folded against it rather than counted separately.
    /// </summary>
    [TestMethod]
    public async Task ADeliveredRunCarriesTheLedgerItsOwnEvidenceSupports()
    {
        var result = await RunAsync(Assertions());

        Assert.AreEqual(LuxembourgQueryExecutionRefusal.None, result.Refusal?.Code ?? LuxembourgQueryExecutionRefusal.None,
            result.Refusal?.Detail);
        var ledger = result.PopulationLedger;
        Assert.IsNotNull(ledger, "a delivered run folds its population ledger.");
        Assert.AreEqual(1, ledger.PopulationActCount);
        Assert.IsTrue(ledger.AllPopulationActsDisposed, "every counted act is disposed.");
        var entry = ledger.EntryFor(Act);
        Assert.IsNotNull(entry, "the act the run saw is the act the ledger holds.");
        Assert.AreEqual(Act, entry.PublisherActIri);
    }

    /// <summary>
    /// THE ACT IS NOT DROPPED. Two legal types is the publisher stating two answers; this code does
    /// not pick one, and it does not skip the act either. It refuses the run and names it.
    /// </summary>
    [TestMethod]
    public async Task AnActWithTwoLegalTypesRefusesTheRunAndIsNamed()
    {
        var result = await RunAsync(Assertions(extraType: Types + "RGD"), fetchesGazette: false);

        Assert.AreEqual(LuxembourgQueryExecutionRefusal.PopulationLedgerNotCompleted, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, "no single legal type");
        StringAssert.Contains(result.Refusal.Detail, Act);
        Assert.IsNull(result.PopulationLedger);
    }

    /// <summary>
    /// And the other half of the same rule: an act the publisher gives NO legal type. This is the
    /// case a count keyed on the type would have missed silently, because there would have been
    /// nothing to key on.
    /// </summary>
    [TestMethod]
    public async Task AnActWithNoLegalTypeRefusesTheRunAndIsNamed()
    {
        var result = await RunAsync(Assertions(omitType: true), fetchesGazette: false);

        Assert.AreEqual(LuxembourgQueryExecutionRefusal.PopulationLedgerNotCompleted, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, "no single legal type");
        StringAssert.Contains(result.Refusal.Detail, Act);
        Assert.IsNull(result.PopulationLedger);
    }

    /// <summary>
    /// A legal type the class manifest does not recognise refuses at the coverage, not here, and
    /// the inner refusal travels by its own name. An unknown code is never silently out of scope.
    /// </summary>
    [TestMethod]
    public async Task AnUnrecognisedLegalTypeRefusesTheRunThroughTheCoverage()
    {
        var result = await RunAsync(
            Assertions(replaceType: Types + "ZZZNOTACODE"), fetchesGazette: false);

        Assert.AreEqual(LuxembourgQueryExecutionRefusal.PopulationLedgerNotCompleted, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, "coverage refused");
        Assert.IsNull(result.PopulationLedger);
    }

    /// <summary>
    /// A RECOGNISED OUT-OF-SCOPE ACT IS NOT AN ERROR, AND ITS EVIDENCE IS NOT LOST. The Gazette
    /// loop fetches for every as-published original whatever its legal type, so an AGC act has a
    /// body set; this ledger counts LOI and RGD. Handing that set to the ledger would refuse the
    /// whole run over evidence outside the count, so the fold hands it only the counted acts' sets.
    /// The act is still placed, out of scope, and its set is still on the result.
    /// </summary>
    [TestMethod]
    public async Task ARecognisedOutOfScopeActIsNotCountedAndItsGazetteSetSurvives()
    {
        var result = await RunAsync(Assertions(replaceType: Types + "AGC"));

        Assert.AreEqual(
            LuxembourgQueryExecutionRefusal.None,
            result.Refusal?.Code ?? LuxembourgQueryExecutionRefusal.None,
            result.Refusal?.Detail);
        Assert.AreEqual(0, result.PopulationLedger!.PopulationActCount,
            "an AGC act is recognised and out of scope, so it is placed but not counted.");
        Assert.IsNull(result.PopulationLedger.EntryFor(Act));
        Assert.IsTrue(
            result.GazetteBodySetsByOrdinal!.Values.Any(set => set.PublisherActIri == Act),
            "the act's Gazette body set is still on the result, not dropped with the count.");
    }

    // ---- Fixtures. ----

    private static (string Subject, string Predicate, string Value)[] Assertions(
        string? extraType = null,
        string? replaceType = null,
        bool omitType = false)
    {
        var assertions = new List<(string, string, string)>
        {
            (Act, RdfType, Jolux + "Act"),
            (Act, Jolux + "isMemberOf", Parent),
            (Act, Jolux + "isRealizedBy", Expression),
            (Expression, RdfType, Jolux + "Expression"),
            (Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (Expression, Jolux + "isEmbodiedBy", ManifestationPdfA),
            (ManifestationPdfA, RdfType, Jolux + "Manifestation"),
            (ManifestationPdfA, Jolux + "userFormat", Formats + "pdfa"),
            (ManifestationPdfA, Jolux + "isExemplifiedBy", ItemPdfA),
            (ManifestationPdfA, Jolux + "license", CcBy),
        };
        if (!omitType)
        {
            assertions.Add((Act, Jolux + "typeDocument", replaceType ?? Types + "LOI"));
        }

        if (extraType is not null)
        {
            assertions.Add((Act, Jolux + "typeDocument", extraType));
        }

        return [.. assertions];
    }

    private static async Task<LuxembourgQueryExecutionResult> RunAsync(
        (string Subject, string Predicate, string Value)[] assertions,
        bool fetchesGazette = true)
    {
        var subjects = new[] { Act, Expression, ManifestationPdfA }
            .OrderBy(static subject => subject, StringComparer.Ordinal).ToArray();
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var profileReceipt = await store.CreateAsync(
            "synthetic vocabulary observation for the population ledger"u8.ToArray(),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var profileEvidence = new SourceArtifactRef(NewUrn(), profileReceipt.Reference.ContentSha256);
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            profileEvidence, profileEvidence, VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []));
        var assertionPage = AssertionRows(assertions);
        var censusPage = LuxembourgAcquisitionTestFixture.RowsJson(subjects);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(subjects.Length)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(request, censusPage),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            // Every document GET runs in its own session, and a session bootstraps robots first.
            7 or 14 => Response(request, "User-agent: *\nAllow: /\n"u8.ToArray(), "text/plain"),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(assertions.Length)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(request, assertionPage),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(request, AssertionRows([])),
            15 => Document(request, HttpStatusCode.OK, PdfABytes),
            _ => throw new AssertFailedException(
                $"Unexpected HTTP request {ordinal}: {request.Method} {request.RequestUri}"),
        });
        var executor = new LuxembourgRepeatedEnumerationExecutor(
            store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new LuxembourgQueryExecutionAdapter(store, executor, profile);
        var (censusRequest, censusWitness) = Partition("S", "census");
        var (assertionRequest, assertionWitness) = Partition("A", "assertions");
        return await adapter.RunAsync(
            [(censusRequest, censusWitness, null), (assertionRequest, assertionWitness, null)],
            null, "census", "assertions", LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(420),
            LuxembourgAcquisitionTestFixture.TestWireBudget(),
            CancellationToken.None);

        static HttpResponseMessage Document(HttpRequestMessage request, HttpStatusCode status, byte[] body)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            return Response(request, body, "application/pdf", status);
        }
    }

    private static (LuxembourgPartitionRunRequest, BoundMachineRequest) Partition(string setId, string family)
    {
        var (plan, resourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var renderer = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var range = LuxembourgAcquisitionTestFixture.FullRange(family);
        return (new LuxembourgPartitionRunRequest(plan, resourceId, setId, range, renderer),
            plan.BindCount(resourceId, NewUrn(), NewUrn(), setId, LuxembourgQueryPass.Pass1, range, renderer).Request);
    }

    private static string AssertionRows((string Subject, string Predicate, string Value)[] rows)
    {
        var variables = new[]
        {
            "subject", "predicate", "object", "object_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
        };
        var bindings = rows.OrderBy(row => row.Subject, StringComparer.Ordinal)
            .ThenBy(row => row.Predicate, StringComparer.Ordinal).ThenBy(row => row.Value, StringComparer.Ordinal)
            .Select(row =>
            {
                var values = new[] { row.Subject, row.Predicate, row.Value, "iri", "", "", row.Subject, row.Predicate, "iri", row.Value, "", "" };
                return variables.Select((name, index) => (name, term: new { type = index < 3 ? "uri" : "literal", value = values[index] }))
                    .ToDictionary(field => field.name, field => field.term);
            });
        return JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = variables },
            results = new { distinct = false, ordered = true, bindings },
        });
    }

    private static HttpResponseMessage Response(
        HttpRequestMessage request, byte[] bytes, string mediaType, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        content.Headers.ContentLength = bytes.Length;
        return new HttpResponseMessage(status) { RequestMessage = request, Content = content };
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
