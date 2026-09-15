using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgQueryExecutionAdapterTests
{
    internal static async Task<LuxembourgQueryExecutionResult> RunEmptyDeliveredForEnvelopeAsync()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store,
            NewExecutor(store, NoSendHandler()),
            profile);

        return await adapter.RunAsync(
            [],
            null,
            null,
            null,
            new PermissiveEvidenceResolver(enumerationRef),
            DocumentFetchRendererSource(),
            LuxembourgAcquisitionTestFixture.TestWireBudget(),
            CancellationToken.None);
    }

    /// <summary>The two subjects the population harness below derives, in the census's delivery order.</summary>
    internal static readonly string[] PopulationHarnessSubjects =
    [
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0",
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
    ];

    /// <summary>
    /// A delivered run with a real, non-empty population: the same two genuinely delivered and
    /// independently re-verified census rows
    /// <see cref="AProvenResourceFamilysRowsDeriveObservationsAndLetScopeResolutionProceed"/> drives,
    /// carried all the way through to this run's own written and reopened corpus/6 record set.
    /// </summary>
    /// <remarks>
    /// The empty harness above cannot stand in for this one: a population of zero satisfies every
    /// membership rule vacuously, so a closer tested only against it would pass with its matching
    /// deleted.
    /// </remarks>
    internal static async Task<LuxembourgQueryExecutionResult> RunTwoSubjectDeliveredForPopulationAsync()
    {
        var subjectA0 = PopulationHarnessSubjects[0];
        var subjectA1 = PopulationHarnessSubjects[1];
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(subjectA0, subjectA1)),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            7 => TextResponse(req, "User-agent: *\nAllow: /\n"),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, AssertionRowsJson((subjectA0, CitesPredicate, subjectA1, "iri", "", ""))),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(req, AssertionRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        return await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null,
            ResourceFamilyKey,
            AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef),
            DocumentFetchRendererSource(),
            LuxembourgAcquisitionTestFixture.TestWireBudget(),
            CancellationToken.None);
    }
}
