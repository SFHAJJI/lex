using Lex.V3.Contracts.Derivation;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFormexPackagePopulationProducer
{
    private const string PopulationCheckpointSchema = "lex-eu-formex-population-checkpoint/1";

    /// <summary>Rebuild all Formex outcomes for the same checked run from retained acquisition checkpoints.</summary>
    public static async Task<EuFormexPackagePopulationResult> ReopenAsync(ICustodyStore store, SourceArtifactRef checkpoint,
        EuQueryExecutionResult run, MachineQueryRendererSource manifestationRenderer, MachineQueryRendererSource documentRenderer,
        string? workCelex, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(manifestationRenderer);
        ArgumentNullException.ThrowIfNull(documentRenderer);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<PopulationCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (run.Refusal is not null || run.Completion != EuQueryExecutionCompletion.AllFamiliesProven || run.CorrigendumTripwires is null ||
                document.Schema != PopulationCheckpointSchema || !bytes.Span.SequenceEqual(EncodePopulation(document)) ||
                document.InputSha256 != PopulationInputDigest(run) || document.WorkCelex != workCelex ||
                document.ManifestationRenderer != manifestationRenderer.Reference || document.DocumentRenderer != documentRenderer.Reference ||
                document.Enumerations is null || document.Packages is null ||
                document.Enumerations.Any(static value => value is null || value.Checkpoint is null || value.Run is null || value.Profile is null) ||
                document.Packages.Any(static value => value is null || value.Checkpoint is null) ||
                document.Enumerations.Select(static value => value.ExpressionSha256).Distinct(StringComparer.Ordinal).Count() != document.Enumerations.Length ||
                document.Packages.Select(static value => value.ExpressionSha256).Distinct(StringComparer.Ordinal).Count() != document.Packages.Length)
                throw new CustodyIntegrityException("Formex population checkpoint framing or checked-run binding disagrees.");
            var budget = WireRequestBudget.OfWireRequests(2);
            var result = await new EuFormexPackagePopulationProducer(store, TimeProvider.System).RunCoreAsync(run,
                manifestationRenderer, documentRenderer, workCelex, sourceWitness: null, budget, cancellationToken,
                new PopulationReplay(document)).ConfigureAwait(false);
            if (!result.Delivered || budget.Spent != 0 || result.ProductRequestCount != 0 || PopulationResultDigest(result) != document.ResultSha256)
                throw new CustodyIntegrityException("Formex population replay differs from its original reconciliation: " + result.Detail);
            return result.WithCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Formex population checkpoint failed independent verification.", exception);
        }
    }

    private async Task<SourceArtifactRef> RetainPopulationAsync(EuQueryExecutionResult run, EuFormexPackagePopulationResult result,
        MachineQueryRendererSource manifestationRenderer, MachineQueryRendererSource documentRenderer, string? celex,
        PopulationReplay context, CancellationToken cancellationToken)
    {
        if (context.Packages.Any(static item => item.Checkpoint is null))
            throw new CustodyRequiredException("A package acquisition has no retained checkpoint.");
        var document = new PopulationCheckpoint(PopulationCheckpointSchema, PopulationInputDigest(run),
            manifestationRenderer.Reference, documentRenderer.Reference, celex, context.Enumerations.ToArray(),
            context.Packages.ToArray(), PopulationResultDigest(result));
        var bytes = EncodePopulation(document);
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Formex population checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Formex population checkpoint receipt names different bytes.");
        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }

    private static string PopulationInputDigest(EuQueryExecutionResult run) => HashPopulation(new
    {
        run.ScopeManifestCanonicalSha256, run.CorpusRecordSetRef, Corpus = run.CorpusRecordSet?.Set,
        Families = run.CorrigendumTripwires!.ProductionsByFamilyKey.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new { Family = pair.Key, Expressions = pair.Value.Expressions!.Derivation!.Expressions
                .Select(static expression => expression.CanonicalContentSha256).ToArray() }).ToArray(),
    });
    private static string PopulationResultDigest(EuFormexPackagePopulationResult result) => HashPopulation(new
    {
        result.EligibleExpressionCount, result.NotEnumeratedExpressionCount, result.AcquiredExpressionCount,
        Outcomes = result.Reconciliation!.Outcomes.Select(static outcome => new
        {
            Expression = outcome.Expression.CanonicalContentSha256, outcome.Kind, outcome.NotAcquiredReason,
            outcome.PackageRefusal, outcome.UnavailableReason, outcome.AcquisitionRefusal, outcome.ObservedStatus, outcome.Detail,
            Inventory = outcome.AcquiredInventory?.IdentitySha256, Package = outcome.AcquiredInventory?.SourceReceipt.Reference,
        }).ToArray(),
        Annexes = result.AnnexClassifications.Select(static value => value.IdentitySha256).ToArray(),
    });
    private static string HashPopulation<T>(T value) => CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(value)));
    private static byte[] EncodePopulation(PopulationCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record PopulationCheckpoint(string Schema, string InputSha256, SourceArtifactRef ManifestationRenderer,
        SourceArtifactRef DocumentRenderer, string? WorkCelex, EnumerationCheckpoint[] Enumerations,
        PackageCheckpoint[] Packages, string ResultSha256);
    private sealed record EnumerationCheckpoint(string Family, string ExpressionSha256, SourceArtifactRef Checkpoint,
        SourceArtifactRef Run, SourceArtifactRef Profile);
    private sealed record PackageCheckpoint(string ExpressionSha256, SourceArtifactRef? Checkpoint);

    private sealed class PopulationReplay(PopulationCheckpoint? document)
    {
        private int _enumerationIndex;
        private int _packageIndex;
        internal bool IsReplay => document is not null;
        internal List<EnumerationCheckpoint> Enumerations { get; } = [];
        internal List<PackageCheckpoint> Packages { get; } = [];
        internal void CaptureEnumeration(string family, EuFormexManifestationEnumerationResult enumeration) =>
            Enumerations.Add(new EnumerationCheckpoint(family, enumeration.Expression.CanonicalContentSha256,
                enumeration.CheckpointRef ?? throw new CustodyIntegrityException("A delivered enumeration lost its checkpoint."),
                enumeration.Proof!.AcquisitionRunRef, enumeration.Proof.InterpretationProfileRef));
        internal void CapturePackage(EuFormexPackageAcquisitionResult acquisition) =>
            Packages.Add(new PackageCheckpoint(acquisition.Outcome.Expression.CanonicalContentSha256, acquisition.CheckpointRef));
        internal async Task<EuFormexManifestationEnumerationResult> EnumerateAsync(ICustodyStore store, string family,
            LanguageScopedExpression expression, CancellationToken cancellationToken)
        {
            if (document is null || _enumerationIndex >= document.Enumerations.Length)
                throw new CustodyIntegrityException("Formex population is missing a served-language enumeration.");
            var item = document.Enumerations[_enumerationIndex++];
            if (item.Family != family || item.ExpressionSha256 != expression.CanonicalContentSha256)
                throw new CustodyIntegrityException("Formex enumeration does not belong at this family/expression position.");
            return await EuFormexManifestationEnumerationProducer.ReopenAsync(store, item.Checkpoint, expression,
                item.Run, item.Profile, cancellationToken).ConfigureAwait(false);
        }
        internal async Task<EuFormexPackageAcquisitionResult> AcquireAsync(ICustodyStore store,
            EuFormexManifestationEnumerationResult enumeration, EuQueryExecutionResult run, string celex,
            MachineQueryRendererSource renderer, CancellationToken cancellationToken)
        {
            if (document is null || _packageIndex >= document.Packages.Length)
                throw new CustodyIntegrityException("Formex population is missing a package acquisition.");
            var item = document.Packages[_packageIndex++];
            if (item.ExpressionSha256 != enumeration.Expression.CanonicalContentSha256)
                throw new CustodyIntegrityException("Formex package does not belong to this expression.");
            return await EuFormexPackageAcquisitionProducer.ReopenAsync(store, item.Checkpoint!, enumeration,
                run.CorpusRecordSet, celex, renderer, cancellationToken).ConfigureAwait(false);
        }
        internal void RequireEnd()
        {
            if (document is null || _enumerationIndex != document.Enumerations.Length || _packageIndex != document.Packages.Length)
                throw new CustodyIntegrityException("Formex population contains unconsumed acquisition checkpoints.");
        }
    }
}
