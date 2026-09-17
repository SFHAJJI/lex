using System.Security.Cryptography;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class Stage3DerivationProfileEnvelopeTests
{
    [TestMethod]
    public async Task ExactProfileChainIsConservedWithoutReinterpretation()
    {
        var chain = await CompleteChainAsync();

        var envelope = Stage3DerivationProfileEnvelope.TryCreate(
            chain.ActScope,
            out var refusal,
            out var detail);

        Assert.IsNotNull(envelope, $"{refusal}: {detail}");
        Assert.AreSame(chain.Composition, envelope.BodyComposition);
        Assert.AreSame(chain.Eligibility, envelope.PdfEligibility);
        Assert.AreSame(chain.Layout, envelope.PdfLayoutEvidence);
        Assert.AreSame(chain.TextLayer, envelope.PublisherPdfTextLayer);
        Assert.AreSame(chain.ActScope, envelope.PublisherPdfActScope);
    }

    [TestMethod]
    public void MissingTerminalProfilePopulationRefusesByName()
    {
        var envelope = Stage3DerivationProfileEnvelope.TryCreate(
            null,
            out var refusal,
            out var detail);

        Assert.IsNull(envelope);
        Assert.AreEqual(Stage3DerivationProfileEnvelopeRefusal.PublisherPdfActScopeMissing, refusal);
        Assert.IsNotNull(detail);
    }

    [TestMethod]
    public void PublicDoorAcceptsOnlyTheProofCompleteProfileChain()
    {
        var parameters = typeof(Stage3DerivationProfileEnvelope)
            .GetMethod(nameof(Stage3DerivationProfileEnvelope.TryCreate))!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(LuxembourgPublisherPdfActScopePopulation),
                typeof(Stage3DerivationProfileEnvelopeRefusal).MakeByRefType(),
                typeof(string).MakeByRefType(),
            },
            parameters);
    }

    private static async Task<ProfileChain> CompleteChainAsync()
    {
        var bytes = Fixture();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes);
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var europe = acquired.Run;
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            europe, [acquired.Outcome]);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(
            formex, [acquired.Classification]);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var evidence = Stage3EvidenceEnvelopeTests.TryCreate(
            europe,
            luxembourg,
            formex,
            classifications,
            fidelity,
            out var evidenceRefusal,
            out var evidenceDetail);
        Assert.IsNotNull(evidence, $"{evidenceRefusal}: {evidenceDetail}");
        var composition = Stage3BodyComposition.TryCreate(
            evidence, out var compositionRefusal, out var compositionDetail);
        Assert.IsNotNull(composition, $"{compositionRefusal}: {compositionDetail}");

        var eligibility = LuxembourgPdfProfileEligibilityProducer.Produce(composition);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        _ = await store.CreateAsync(
            bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var layoutResult = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(eligibility, CancellationToken.None);
        Assert.IsTrue(layoutResult.Produced, $"{layoutResult.Refusal}: {layoutResult.Detail}");
        var textResult = await new LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layoutResult.Population!, CancellationToken.None);
        Assert.IsTrue(textResult.Produced, $"{textResult.Refusal}: {textResult.Detail}");
        var actScope = LuxembourgPublisherPdfActScopeProducer.Produce(textResult.Population!);

        return new ProfileChain(
            composition,
            eligibility,
            layoutResult.Population!,
            textResult.Population!,
            actScope);
    }

    private static byte[] Fixture()
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "LuDocumentFetch",
            "lu-pdf-consolidated-2020-04-08-a265.bin"));
        Assert.AreEqual(84_897, bytes.Length);
        Assert.AreEqual(
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private sealed record ProfileChain(
        Stage3BodyComposition Composition,
        LuxembourgPdfProfileEligibilityPopulation Eligibility,
        LuxembourgPdfLayoutEvidencePopulation Layout,
        LuxembourgPublisherPdfTextLayerPopulation TextLayer,
        LuxembourgPublisherPdfActScopePopulation ActScope);
}
