using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFormexPackageAcquisitionProducer
{
    private const string PackageCheckpointSchema = "lex-eu-formex-package-checkpoint/1";

    /// <summary>Acquire and classify one eligible package, retaining the original replay associations.</summary>
    public async Task<EuFormexPackageAcquisitionResult> RunAsync(EuFormexManifestationEnumerationResult enumeration,
        VerifiedCorpusRecordSet? corpusRecordSet, string workCelex, MachineQueryRendererSource documentFetchRendererSource,
        WireRequestBudget wireBudget, CancellationToken cancellationToken)
    {
        var context = new PackageReplayContext(null);
        var result = await RunCoreAsync(enumeration, corpusRecordSet, workCelex, documentFetchRendererSource,
            wireBudget, context, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = new PackageCheckpoint(PackageCheckpointSchema, InputDigest(enumeration, corpusRecordSet, workCelex),
                documentFetchRendererSource.Reference, context.Fetches.ToArray(), context.Profiles.ToArray(),
                ResultDigest(result), result.ProductRequestCount);
            var bytes = Encode(document);
            var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
            if (receipt is null) throw new CustodyRequiredException("Package checkpoint hold refused: " + failure);
            if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
                throw new CustodyIntegrityException("Package checkpoint receipt names different bytes.");
            return result.WithCheckpoint(new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256));
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return new(EuFormexPackageOutcome.NotAcquired(enumeration.Expression,
                EuFormexPackageNotAcquiredReason.CheckpointNotRetained, exception.Message), result.ProductRequestCount);
        }
    }

    /// <summary>Repeat package and annex derivation from the original capture, with zero publisher traffic.</summary>
    public static async Task<EuFormexPackageAcquisitionResult> ReopenAsync(ICustodyStore store, SourceArtifactRef checkpoint,
        EuFormexManifestationEnumerationResult enumeration, VerifiedCorpusRecordSet? corpusRecordSet, string workCelex,
        MachineQueryRendererSource documentFetchRendererSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(enumeration);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(workCelex);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<PackageCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Schema != PackageCheckpointSchema || !bytes.Span.SequenceEqual(Encode(document)) ||
                document.InputSha256 != InputDigest(enumeration, corpusRecordSet, workCelex) ||
                document.Renderer != documentFetchRendererSource.Reference || document.Fetches is null || document.Profiles is null ||
                document.Fetches.Length > 2 || document.Profiles.Length > 3 || document.ProductRequestCount < 0 ||
                document.Fetches.Any(static item => item is null) || document.Profiles.Any(static item => item is null))
                throw new CustodyIntegrityException("Package checkpoint framing or input binding disagrees.");
            var context = new PackageReplayContext(document);
            var producer = new EuFormexPackageAcquisitionProducer(store, TimeProvider.System);
            var budget = WireRequestBudget.OfWireRequests(2);
            var result = await producer.RunCoreAsync(enumeration, corpusRecordSet, workCelex, documentFetchRendererSource,
                budget, context, cancellationToken).ConfigureAwait(false);
            context.RequireEnd();
            if (budget.Spent != 0 || result.ProductRequestCount != document.ProductRequestCount || ResultDigest(result) != document.ResultSha256)
                throw new CustodyIntegrityException("Package replay differs from its original typed outcome or annex derivation.");
            return new EuFormexPackageAcquisitionResult(result.Outcome, 0, result.AnnexClassification).WithCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Package checkpoint failed independent verification.", exception);
        }
    }

    private static string InputDigest(EuFormexManifestationEnumerationResult enumeration, VerifiedCorpusRecordSet? corpus, string celex) =>
        Digest(new { Expression = enumeration.Expression.CanonicalContentSha256, enumeration.CheckpointRef,
            enumeration.ManifestationTypes, Corpus = corpus?.Set, Celex = celex });
    private static string ResultDigest(EuFormexPackageAcquisitionResult result) => Digest(new
    {
        result.Outcome.Kind, result.Outcome.UnavailableReason, result.Outcome.ObservedStatus,
        result.Outcome.AcquisitionRefusal, result.Outcome.Detail, result.Outcome.NotAcquiredReason, result.Outcome.PackageRefusal,
        Inventory = result.Outcome.AcquiredInventory?.IdentitySha256,
        Package = result.Outcome.AcquiredInventory?.SourceReceipt.Reference,
        Annex = result.AnnexClassification?.IdentitySha256,
    });
    private static string Digest<T>(T value) => CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(value)));
    private static byte[] Encode(PackageCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record PackageCheckpoint(string Schema, string InputSha256, SourceArtifactRef Renderer,
        PackageFetch[] Fetches, SourceArtifactRef[] Profiles, string ResultSha256, int ProductRequestCount);
    private sealed record PackageFetch(string Uri, string Accept, string Language, SourceArtifactRef? Route,
        SourceArtifactRef? Run, string? RequestSha256, EuDocumentFetchAttemptRefusal? Refusal, string? Detail);

    private sealed class PackageReplayContext(PackageCheckpoint? document)
    {
        private int _fetchIndex;
        private int _profileIndex;
        internal bool IsReplay => document is not null;
        internal List<PackageFetch> Fetches { get; } = [];
        internal List<SourceArtifactRef> Profiles { get; } = [];
        internal void Capture(EuDocumentFetchAddress address, EuDocumentFetchAttemptResult attempt)
        {
            var route = attempt.Evidence;
            Fetches.Add(new PackageFetch(address.ResourceUri, address.Accept, address.AcceptLanguage,
                route is null ? null : new SourceArtifactRef(NewUrn(), CustodyDigest.Of(route.CopyCanonicalBytes())),
                route?.RunIdentity, route?.Hops[0].LogicalRequestSha256, attempt.Refusal, attempt.Detail));
        }
        internal async Task<EuDocumentFetchAttemptResult> FetchAsync(ICustodyStore store, EuDocumentFetchAddress address,
            CancellationToken cancellationToken)
        {
            if (document is null || _fetchIndex >= document.Fetches.Length)
                throw new CustodyIntegrityException("Package replay requires an unretained fetch.");
            var fetch = document.Fetches[_fetchIndex++];
            if (fetch.Uri != address.ResourceUri || fetch.Accept != address.Accept || fetch.Language != address.AcceptLanguage)
                throw new CustodyIntegrityException("Package fetch does not match the enumeration-selected representation.");
            if (fetch.Route is null)
            {
                if (fetch.Run is not null || fetch.RequestSha256 is not null || fetch.Refusal is null ||
                    fetch.Refusal == EuDocumentFetchAttemptRefusal.None || !Enum.IsDefined(fetch.Refusal.Value))
                    throw new CustodyIntegrityException("A non-executed package fetch carries inconsistent evidence.");
                return EuDocumentFetchAttemptResult.Refused(fetch.Refusal.Value, fetch.Detail);
            }
            if (fetch.Run is null || fetch.RequestSha256 is null || fetch.Refusal is not null || fetch.Detail is not null)
                throw new CustodyIntegrityException("An executed package fetch carries inconsistent evidence.");
            return await EuDocumentFetchRouteReader.ReopenAsync(store, fetch.Route, fetch.Run, fetch.RequestSha256,
                address, cancellationToken).ConfigureAwait(false);
        }
        internal (byte[] Bytes, SourceArtifactRef Reference) Profile(params string[] lines)
        {
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
            var digest = CustodyDigest.Of(bytes);
            if (document is null)
            {
                var reference = new SourceArtifactRef(NewUrn(), digest);
                Profiles.Add(reference);
                return (bytes, reference);
            }
            if (_profileIndex >= document.Profiles.Length || document.Profiles[_profileIndex].Sha256 != digest)
                throw new CustodyIntegrityException("Package annex profile differs from its original interpretation inputs.");
            return (bytes, document.Profiles[_profileIndex++]);
        }
        internal void RequireEnd()
        {
            if (document is null || _fetchIndex != document.Fetches.Length || _profileIndex != document.Profiles.Length)
                throw new CustodyIntegrityException("Package checkpoint contains unconsumed fetches or profiles.");
        }
    }
}
