using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuImageOnlyAnnexProducerTests
{
    internal static async Task<EuImageOnlyAnnexProductionResult> ProduceForEnvelopeAsync(
        EuAnnexBodyDispositionOutcome outcome = EuAnnexBodyDispositionOutcome.TextNotAvailable)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        var profile = Profile(fixture, 1);
        if (outcome != EuAnnexBodyDispositionOutcome.TextNotAvailable)
        {
            return EuImageOnlyAnnexProductionResult.Success(EuAnnexBodyDisposition.Create(
                fixture.SourceObject,
                fixture.Location,
                fixture.Address,
                fixture.Request,
                fixture.Request,
                fixture.Evidence,
                fixture.Receipt,
                profile.Bytes,
                profile.Reference,
                outcome));
        }

        var result = await RunAsync(new EuImageOnlyAnnexProducer(store), fixture, profile);
        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Disposition);
        return result;
    }

    internal static async Task<EuImageOnlyAnnexProductionResult> ProduceForEnvelopeAtAsync(
        string annexSuffix)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        fixture = fixture with
        {
            Location = EuStructuralLocation.Parse(
                "{AN|http://publications.europa.eu/resource/authority/fd_370/AN} " + annexSuffix,
                AnnexAuthority),
        };
        var profile = Profile(fixture, 1);
        var result = await RunAsync(new EuImageOnlyAnnexProducer(store), fixture, profile);
        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Disposition);
        return result;
    }

    internal static async Task<EuImageOnlyAnnexProductionResult> ProduceForEnvelopeFromTwoPagePdfAsync()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(
            store,
            MultiPagePdf((Image: true, Text: false), (Image: true, Text: false)));
        var profile = Profile(fixture, 1, 2);
        var result = await RunAsync(new EuImageOnlyAnnexProducer(store), fixture, profile);
        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Disposition);
        return result;
    }
}
