using System.Reflection;
using System.Security.Cryptography;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPublisherPdfActScopeProducerTests
{
    private const string ActItem =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/consolide/20260201/fr/pdf/consolide.pdf";

    [TestMethod]
    public async Task ExactManifestationNestedItemIsOnlyCoordinateScoped()
    {
        var source = await TextPopulationAsync(Fixture(), ActItem);

        var population = LuxembourgPublisherPdfActScopeProducer.Produce(source);

        var outcome = population.Outcomes.Single();
        Assert.AreSame(source.Outcomes.Single(), outcome.SourceTextLayer);
        Assert.AreEqual(
            LuxembourgPublisherPdfActScopeDisposition.ActScopedCoordinate,
            outcome.Disposition);
        Assert.IsNull(outcome.GapReason);
        Assert.AreEqual(source.Outcomes.Single().SourceLayoutEvidence.SourceEligibility.PublisherExpressionIri,
            outcome.PublisherExpressionIri);
        Assert.AreEqual(ActItem, outcome.PublisherItemIri);
    }

    [TestMethod]
    public async Task MemorialItemRemainsIssueScopedAndRequiresLaterSplitting()
    {
        var items = new[]
        {
            "http://data.legilux.public.lu/filestore/eli/etat/leg/memorial/1977/a67/fr/pdf/eli-etat-leg-memorial-1977-a67-fr-pdf.pdf",
            "http://data.legilux.public.lu/filestore/eli/etat/adm/memorial/2015/b12/fr/pdf/eli-etat-adm-memorial-2015-b12-fr-pdf.pdf",
        };

        foreach (var item in items)
        {
            var source = await TextPopulationAsync(Fixture(), item);
            var outcome = LuxembourgPublisherPdfActScopeProducer.Produce(source).Outcomes.Single();
            Assert.AreEqual(
                LuxembourgPublisherPdfActScopeDisposition.GazetteIssueScope,
                outcome.Disposition);
            Assert.IsNull(outcome.GapReason);
            Assert.AreEqual(item, outcome.PublisherItemIri);
        }
    }

    [TestMethod]
    public async Task NearManifestationPrefixCannotBecomeActScoped()
    {
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/consolide/20260201/fr/pdf-extra/consolide.pdf";
        var source = await TextPopulationAsync(Fixture(), item);

        var outcome = LuxembourgPublisherPdfActScopeProducer.Produce(source).Outcomes.Single();

        Assert.AreEqual(LuxembourgPublisherPdfActScopeDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(LuxembourgPublisherPdfActScopeGapReason.ActScopeUnproven, outcome.GapReason);
    }

    [TestMethod]
    public async Task UpstreamTextGapStaysExplicitInsteadOfUsingItsCoordinate()
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuDocumentFetch", "lu-pdf-invisible-1987-12-23-n5.bin"));
        var source = await TextPopulationAsync(bytes, ActItem);
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerDisposition.TypedGap,
            source.Outcomes.Single().Disposition);

        var outcome = LuxembourgPublisherPdfActScopeProducer.Produce(source).Outcomes.Single();

        Assert.AreEqual(LuxembourgPublisherPdfActScopeDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(
            LuxembourgPublisherPdfActScopeGapReason.UpstreamTextLayerGap,
            outcome.GapReason);
    }

    [TestMethod]
    public async Task MixedPopulationPreservesOrderAndDistinctGapCauses()
    {
        var clean = Fixture();
        var invisible = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuDocumentFetch", "lu-pdf-invisible-1987-12-23-n5.bin"));
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteTwoPublisherPdfsForStage3BodyCompositionAsync(clean, invisible);
        var source = await TextPopulationAsync(luxembourg, clean, invisible);

        var population = LuxembourgPublisherPdfActScopeProducer.Produce(source);

        Assert.HasCount(2, population.Outcomes);
        CollectionAssert.AreEqual(
            source.Outcomes.Select(static outcome => outcome.SemanticIdentitySha256).ToArray(),
            population.Outcomes.Select(
                static outcome => outcome.SourceTextLayer.SemanticIdentitySha256).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgPublisherPdfActScopeDisposition.TypedGap,
                LuxembourgPublisherPdfActScopeDisposition.TypedGap,
            },
            population.Outcomes.Select(static outcome => outcome.Disposition).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgPublisherPdfActScopeGapReason.ActScopeUnproven,
                LuxembourgPublisherPdfActScopeGapReason.UpstreamTextLayerGap,
            },
            population.Outcomes.Select(static outcome => outcome.GapReason!.Value).ToArray(),
            "The first fixture's item is not nested under its suffixed manifestation; the second "
            + "is already an upstream invisible-text gap.");
    }

    [TestMethod]
    public async Task EqualEvidenceHasStableIdentityAndCoordinateChangesMoveIt()
    {
        const string memorial =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/memorial/1977/a67/fr/pdf/eli-etat-leg-memorial-1977-a67-fr-pdf.pdf";
        var first = LuxembourgPublisherPdfActScopeProducer.Produce(
            await TextPopulationAsync(Fixture(), ActItem));
        var second = LuxembourgPublisherPdfActScopeProducer.Produce(
            await TextPopulationAsync(Fixture(), ActItem));
        var issue = LuxembourgPublisherPdfActScopeProducer.Produce(
            await TextPopulationAsync(Fixture(), memorial));

        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(
            first.Outcomes.Single().SemanticIdentitySha256,
            second.Outcomes.Single().SemanticIdentitySha256);
        Assert.AreNotEqual(first.IdentitySha256, issue.IdentitySha256);
    }

    [TestMethod]
    public void PublicDoorAcceptsOnlyTheProofCompleteTextLayerPopulation()
    {
        var parameters = typeof(LuxembourgPublisherPdfActScopeProducer)
            .GetMethod(nameof(LuxembourgPublisherPdfActScopeProducer.Produce))!
            .GetParameters().Select(static parameter => parameter.ParameterType).ToArray();
        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgPublisherPdfTextLayerPopulation) },
            parameters);
    }

    private static async Task<LuxembourgPublisherPdfTextLayerPopulation> TextPopulationAsync(
        byte[] bytes,
        string item)
    {
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes, item);
        return await TextPopulationAsync(luxembourg, bytes);
    }

    private static async Task<LuxembourgPublisherPdfTextLayerPopulation> TextPopulationAsync(
        LuxembourgQueryExecutionResult luxembourg,
        params byte[][] bytes)
    {
        var eligibility = await EligibilityAsync(luxembourg);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        foreach (var body in bytes)
        {
            _ = await store.CreateAsync(body, CustodyClass.NightlyFloor90d, CancellationToken.None);
        }
        var layout = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(eligibility, CancellationToken.None);
        Assert.IsTrue(layout.Produced, $"{layout.Refusal}: {layout.Detail}");
        var text = await new LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layout.Population!, CancellationToken.None);
        Assert.IsTrue(text.Produced, $"{text.Refusal}: {text.Detail}");
        return text.Population!;
    }

    private static async Task<LuxembourgPdfProfileEligibilityPopulation> EligibilityAsync(
        LuxembourgQueryExecutionResult luxembourg)
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var europe = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.XhtmlSource.ObjectRef,
                acquired.Classification.Binding.PdfSource.ObjectRef,
            }.Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            europe, [acquired.Outcome]);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(
            formex, [acquired.Classification]);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var envelope = Stage3EvidenceEnvelopeTests.TryCreate(
            europe, luxembourg, formex, classifications, fidelity,
            out var envelopeRefusal, out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");
        var composition = Stage3BodyComposition.TryCreate(envelope, out var refusal, out var detail);
        Assert.IsNotNull(composition, $"{refusal}: {detail}");
        return LuxembourgPdfProfileEligibilityProducer.Produce(composition);
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
}
