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
}
