using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// The production composition seam from separately retained publisher evidence to one annex
/// classification. Binding and classification remain two explicit phases because the immutable
/// classification profile names the binding identity produced by the first phase.
/// </summary>
internal sealed class EuAnnexBodyProduction
{
    private readonly ICustodyStore _custodyStore;

    internal EuAnnexBodyProduction(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    internal Task<EuAnnexEvidenceBindingResult> BindAsync(
        EuWemiIdentityBoundary identityBoundary,
        EuFormexPackage package,
        SourceObjectRef expectedPdfManifestation,
        VerifiedCorpusRecordSet corpusRecordSet,
        EuFormexAnnexInventory formexInventory,
        EuXhtmlAnnexInventory xhtmlInventory,
        DurableBlobWriteReceipt retainedPdfBytes,
        ReadOnlyMemory<byte> reconciliationProfileBytes,
        SourceArtifactRef reconciliationProfileRef,
        CancellationToken cancellationToken) =>
        new EuAnnexEvidenceBinder(_custodyStore).RunAsync(
            identityBoundary, package, expectedPdfManifestation, corpusRecordSet,
            formexInventory, xhtmlInventory, retainedPdfBytes, reconciliationProfileBytes,
            reconciliationProfileRef, cancellationToken);

    internal Task<EuBoundAnnexBodyClassificationResult> ClassifyAsync(
        EuAnnexEvidenceBinding binding,
        EuDocumentFetchAddress officialAddress,
        HttpLogicalRequest officialRequest,
        HttpLogicalRequest terminalRequest,
        RoutedHttpEvidence sourceEvidence,
        ReadOnlyMemory<byte> profileBytes,
        SourceArtifactRef profileRef,
        CancellationToken cancellationToken) =>
        new EuBoundAnnexBodyClassifier(_custodyStore).RunAsync(
            binding, officialAddress, officialRequest, terminalRequest, sourceEvidence,
            profileBytes, profileRef, cancellationToken);
}
