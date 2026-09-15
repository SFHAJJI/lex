using System.Reflection;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexAnnexClassificationReconciliationTests
{
    [TestMethod]
    public async Task EveryAcquiredInventoryReceivesExactlyOneClassification()
    {
        var acquired = await AcquiredFixtureAsync();
        var formex = Reconciliation(acquired.Run, [acquired.Outcome]);

        var reconciliation = EuFormexAnnexClassificationReconciliation.TryClose(
            formex, [acquired.Classification], out var refusal, out var detail);

        Assert.AreEqual(EuFormexAnnexClassificationReconciliationRefusal.None, refusal, detail);
        Assert.IsNotNull(reconciliation);
        Assert.AreSame(formex, reconciliation.Formex);
        Assert.HasCount(1, reconciliation.Classifications);
        Assert.AreSame(acquired.Classification, reconciliation.Classifications[0]);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<EuBoundAnnexBodyClassification>)reconciliation.Classifications).Add(
                acquired.Classification));
    }

    [TestMethod]
    public async Task CallerOrderCannotReorderTheAcquiredOutcomePopulation()
    {
        var first = await AcquiredFixtureAsync();
        var second = await AcquiredFixtureAsync(formexTwoMembers: true);
        var formex = Reconciliation(first.Run, [first.Outcome, second.Outcome]);

        var reconciliation = EuFormexAnnexClassificationReconciliation.TryClose(
            formex, [second.Classification, first.Classification], out var refusal, out var detail);

        Assert.AreEqual(EuFormexAnnexClassificationReconciliationRefusal.None, refusal, detail);
        Assert.IsNotNull(reconciliation);
        CollectionAssert.AreEqual(
            new[] { first.Classification, second.Classification },
            reconciliation.Classifications.ToArray());
    }

    [TestMethod]
    public async Task MissingAcquiredClassificationRefusesClosure()
    {
        var acquired = await AcquiredFixtureAsync();

        Assert.IsNull(EuFormexAnnexClassificationReconciliation.TryClose(
            Reconciliation(acquired.Run, [acquired.Outcome]), [], out var refusal, out var detail));
        Assert.AreEqual(EuFormexAnnexClassificationReconciliationRefusal.ClassificationMissing, refusal);
        Assert.AreEqual(acquired.Inventory.IdentitySha256, detail);
    }

    [TestMethod]
    public async Task OneAcquiredInventoryCannotBeClassifiedTwice()
    {
        var acquired = await AcquiredFixtureAsync();

        Assert.IsNull(EuFormexAnnexClassificationReconciliation.TryClose(
            Reconciliation(acquired.Run, [acquired.Outcome]),
            [acquired.Classification, acquired.Classification], out var refusal, out var detail));
        Assert.AreEqual(
            EuFormexAnnexClassificationReconciliationRefusal.ClassificationSuppliedTwice,
            refusal);
        Assert.AreEqual(acquired.Inventory.IdentitySha256, detail);
    }

    [TestMethod]
    public async Task ClassificationOutsideTheAcquiredPopulationRefusesClosure()
    {
        var acquired = await AcquiredFixtureAsync();
        var foreign = await AcquiredFixtureAsync(formexTwoMembers: true);

        Assert.IsNull(EuFormexAnnexClassificationReconciliation.TryClose(
            Reconciliation(acquired.Run, [acquired.Outcome]),
            [foreign.Classification], out var refusal, out var detail));
        Assert.AreEqual(
            EuFormexAnnexClassificationReconciliationRefusal.ClassificationOutsideAcquiredPopulation,
            refusal);
        Assert.AreEqual(foreign.Inventory.IdentitySha256, detail);
    }

    [TestMethod]
    public async Task EqualSemanticInventoryFromAnotherTransportCannotReplaceTheAcquiredLineage()
    {
        var acquired = await AcquiredFixtureAsync();
        var package = (byte[])typeof(EuAnnexEvidenceBinderTests).GetMethod(
            "FormexPackage", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [false, false])!;
        var alternateContainer = package.Concat(new byte[] { 0 }).ToArray();
        var otherTransport = await AcquiredFixtureAsync(formexBytes: alternateContainer);

        Assert.AreEqual(acquired.Inventory.IdentitySha256, otherTransport.Inventory.IdentitySha256);
        Assert.AreNotEqual(
            DurableBlobWriteReceiptDigest.Of(acquired.Inventory.SourceReceipt),
            DurableBlobWriteReceiptDigest.Of(otherTransport.Inventory.SourceReceipt));
        Assert.IsNull(EuFormexAnnexClassificationReconciliation.TryClose(
            Reconciliation(acquired.Run, [acquired.Outcome]),
            [otherTransport.Classification], out var refusal, out var detail));
        Assert.AreEqual(
            EuFormexAnnexClassificationReconciliationRefusal.ClassificationInventoryDisagrees,
            refusal);
        Assert.AreEqual(acquired.Inventory.IdentitySha256, detail);
    }

    [TestMethod]
    public async Task NonAcquiredExpressionsCannotReceiveAClassification()
    {
        var fixture = await AcquiredFixtureAsync();
        var refused = EuFormexPackageOutcome.Refused(
            fixture.Outcome.Expression,
            EuDocumentFetchAttemptRefusal.ObservationNotExecuted,
            "offline fixture");

        Assert.IsNull(EuFormexAnnexClassificationReconciliation.TryClose(
            Reconciliation(fixture.Run, [refused]),
            [fixture.Classification], out var refusal, out _));
        Assert.AreEqual(
            EuFormexAnnexClassificationReconciliationRefusal.ClassificationOutsideAcquiredPopulation,
            refusal);
    }

    internal static async Task<Fixture> AcquiredFixtureAsync(
        bool formexTwoMembers = false,
        byte[]? formexBytes = null)
    {
        var source = await EuAnnexEvidenceBinderTests.FixtureAsync(
            EuAnnexEvidenceBinderTests.PageLabelPdf(7, "<< /S /D /St 1 >>"),
            formexTwoMembers: formexTwoMembers,
            xhtmlTwoMembers: formexTwoMembers,
            formexBytes: formexBytes);
        var bound = await source.RunAsync();
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.None, bound.Refusal, bound.Detail);
        var binding = bound.Binding!;
        var address = EuDocumentFetchAddress.TryCreate(
            "cellar", binding.Work.CanonicalKey, EuManifestationMediaType.ApplicationPdf,
            EuDocumentLanguage.Eng, out var addressRefusal)!;
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, addressRefusal);
        var classification = new EuBoundAnnexBodyClassification(
            binding, address, null!, source.Profile.Reference,
            binding.Members.Select(static member =>
                new EuBoundAnnexBodyMemberClassification(
                    member, null, EuBoundAnnexBodyClassificationGap.MappingUnresolved)).ToArray());
        var expression = LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(
                binding.Work.PublisherUri, binding.Expression.PublisherUri),
            binding.Language,
            null,
            binding.Expression,
            LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage,
                    source.Formex.SourceReceipt)]));
        var run = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        return new Fixture(
            run,
            source.Formex,
            EuFormexPackageOutcome.Acquired(expression, source.Formex),
            classification);
    }

    internal static EuFormexRunOutcomeReconciliation Reconciliation(
        EuQueryExecutionResult run,
        IReadOnlyList<EuFormexPackageOutcome> outcomes)
    {
        var constructor = typeof(EuFormexRunOutcomeReconciliation).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(EuQueryExecutionResult),
                typeof(IReadOnlyDictionary<string, EuFormexPackageOutcomePopulation>),
                typeof(IReadOnlyList<EuFormexPackageOutcome>),
            ],
            modifiers: null)!;
        return (EuFormexRunOutcomeReconciliation)constructor.Invoke(
            [run, new Dictionary<string, EuFormexPackageOutcomePopulation>(), outcomes]);
    }

    internal sealed record Fixture(
        EuQueryExecutionResult Run,
        EuFormexAnnexInventory Inventory,
        EuFormexPackageOutcome Outcome,
        EuBoundAnnexBodyClassification Classification);
}
