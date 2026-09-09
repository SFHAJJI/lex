using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Europe;

public enum EuTranspositionBridgeReconciliationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("source_not_delivered")]
    SourceNotDelivered = 1,

    [JsonStringEnumMemberName("nim_work_identity_not_consistent")]
    NimWorkIdentityNotConsistent = 2,

    [JsonStringEnumMemberName("legilux_identity_not_singular")]
    LegiluxIdentityNotSingular = 3,

    [JsonStringEnumMemberName("legilux_identity_not_in_nim_population")]
    LegiluxIdentityNotInNimPopulation = 4,

    [JsonStringEnumMemberName("legilux_identity_contradicts_nim")]
    LegiluxIdentityContradictsNim = 5,

    [JsonStringEnumMemberName("identity_observation_unused")]
    IdentityObservationUnused = 6,

    [JsonStringEnumMemberName("population_refused")]
    PopulationRefused = 7,
}

/// <summary>
/// Lex's disclosed identity bridge from a Legilux-local EU target to a Cellar work. The two
/// publisher spellings and both completion proofs remain present; this is never a publisher claim.
/// </summary>
public sealed record EuTranspositionIdentityReconciliation(
    string CellarWorkUri,
    string EuEli,
    string LegiluxLocalEuWorkUri,
    string LegiluxNationalMeasureUri,
    SourceArtifactRef LegiluxIdentityCompletionEvidenceRef,
    SourceArtifactRef NimCompletionEvidenceRef)
{
    public bool IsDerived() => true;
}

/// <summary>A delivered population plus its disclosed identity readings, or one typed refusal.</summary>
public sealed class EuTranspositionBridgeReconciliationResult
{
    private EuTranspositionBridgeReconciliationResult(
        EuTranspositionBridgePopulationResult? population,
        IReadOnlyList<EuTranspositionIdentityReconciliation>? reconciliations,
        EuTranspositionBridgeReconciliationRefusal refusal,
        string? detail)
    {
        Population = population;
        Reconciliations = reconciliations;
        Refusal = refusal;
        Detail = detail;
    }

    public EuTranspositionBridgePopulationResult? Population { get; }
    public IReadOnlyList<EuTranspositionIdentityReconciliation>? Reconciliations { get; }
    public EuTranspositionBridgeReconciliationRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == EuTranspositionBridgeReconciliationRefusal.None;

    internal static EuTranspositionBridgeReconciliationResult Success(
        EuTranspositionBridgePopulationResult population,
        IReadOnlyList<EuTranspositionIdentityReconciliation> reconciliations) =>
        new(population, Array.AsReadOnly(reconciliations.ToArray()),
            EuTranspositionBridgeReconciliationRefusal.None, null);

    internal static EuTranspositionBridgeReconciliationResult Refused(
        EuTranspositionBridgeReconciliationRefusal refusal,
        string detail,
        EuTranspositionBridgePopulationResult? population = null)
    {
        if (refusal == EuTranspositionBridgeReconciliationRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }
        return new(population, null, refusal, detail);
    }
}

/// <summary>
/// Reconciles the complete Legilux target-identity family with the complete Cellar NIM family by
/// exact EU ELI. No title, CELEX parsing, or local target spelling is used as an identity bridge.
/// </summary>
public sealed class EuTranspositionBridgeReconciliationProducer
{
    private readonly EuTranspositionBridgePopulationProducer _populationProducer;

    public EuTranspositionBridgeReconciliationProducer(ICustodyStore custodyStore)
    {
        _populationProducer = new EuTranspositionBridgePopulationProducer(
            custodyStore ?? throw new ArgumentNullException(nameof(custodyStore)));
    }

    public async Task<EuTranspositionBridgeReconciliationResult> ProduceAsync(
        LuxembourgTranspositionProductionResult legilux,
        LuxembourgTranspositionIdentityProductionResult identities,
        EuNationalImplementingMeasureProductionResult nim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(legilux);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(nim);
        if (!legilux.Delivered || legilux.Relations is null || legilux.CompletionEvidenceRef is null ||
            !identities.Delivered || identities.Relations is null || identities.CompletionEvidenceRef is null ||
            !nim.Delivered || nim.Relations is null || nim.OutOfE5WorkKindExclusions is null ||
            nim.PublisherCoordinateConflicts is null ||
            nim.CompletionEvidenceRef is null)
        {
            return EuTranspositionBridgeReconciliationResult.Refused(
                EuTranspositionBridgeReconciliationRefusal.SourceNotDelivered,
                "Legilux relations, Legilux target identities, and NIM relations must all be delivered.");
        }

        var scope = new List<(string WorkUri, string EuEli, EuWorkKindAssertion Assertion)>();
        foreach (var group in nim.Relations.GroupBy(static relation => relation.EuWorkUri, StringComparer.Ordinal))
        {
            var first = group.First().WorkKindAssertion;
            var eli = first.Work.Value(FactsIdentifierFamily.Eli);
            if (eli is null || first.Work.Publisher != PublisherId.EuEurLex ||
                !EuNationalImplementingMeasureProducer.WorkTypeMatchesKindAndEli(
                    first.Kind, group.First().PublisherWorkTypeIri, eli) ||
                !string.Equals(first.Work.Value(FactsIdentifierFamily.CellarWorkUri), group.Key, StringComparison.Ordinal) ||
                group.Any(relation => relation.WorkKindAssertion.Kind != first.Kind ||
                    !string.Equals(
                        relation.PublisherWorkTypeIri,
                        group.First().PublisherWorkTypeIri,
                        StringComparison.Ordinal) ||
                    !string.Equals(relation.WorkKindAssertion.Work.Value(FactsIdentifierFamily.CellarWorkUri), group.Key, StringComparison.Ordinal) ||
                    !string.Equals(relation.WorkKindAssertion.Work.Value(FactsIdentifierFamily.Eli), eli, StringComparison.Ordinal)))
            {
                return EuTranspositionBridgeReconciliationResult.Refused(
                    EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
                    $"The NIM rows for {group.Key} do not retain one exact Cellar/ELI/work-kind identity.");
            }
            scope.Add((group.Key, eli, first));
        }

        var mixedDisposition = nim.OutOfE5WorkKindExclusions.FirstOrDefault(exclusion =>
            scope.Any(value =>
                string.Equals(value.WorkUri, exclusion.EuWorkUri, StringComparison.Ordinal) ||
                string.Equals(value.EuEli, exclusion.EuWorkEli, StringComparison.Ordinal)));
        if (mixedDisposition is not null)
        {
            return EuTranspositionBridgeReconciliationResult.Refused(
                EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
                $"The NIM population both admits and excludes {mixedDisposition.EuWorkUri} from E5.");
        }

        var conflictedDisposition = nim.PublisherCoordinateConflicts.FirstOrDefault(conflict =>
            scope.Any(value =>
                string.Equals(value.WorkUri, conflict.EuWorkUri, StringComparison.Ordinal) ||
                string.Equals(value.EuEli, conflict.EuWorkEli, StringComparison.Ordinal)));
        if (conflictedDisposition is not null)
        {
            return EuTranspositionBridgeReconciliationResult.Refused(
                EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
                $"The NIM population both admits and records contradictory publisher coordinates for {conflictedDisposition.EuWorkUri}.");
        }

        var ambiguousEli = scope.GroupBy(static value => value.EuEli, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Skip(1).Any());
        if (ambiguousEli is not null)
        {
            return EuTranspositionBridgeReconciliationResult.Refused(
                EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
                $"The EU ELI {ambiguousEli.Key} names more than one Cellar work in the NIM population.");
        }

        var reconciledRelations = new List<LuxembourgTranspositionRelation>(legilux.Relations.Count);
        var reconciliations = new List<EuTranspositionIdentityReconciliation>(legilux.Relations.Count);
        var usedIdentities = new HashSet<LuxembourgTranspositionIdentityRelation>();
        foreach (var relation in legilux.Relations)
        {
            var matches = identities.Relations.Where(identity =>
                string.Equals(identity.NationalMeasureUri, relation.LegiluxMeasureUri, StringComparison.Ordinal) &&
                string.Equals(identity.LocalEuWorkUri, relation.EuWorkUri, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
            {
                return EuTranspositionBridgeReconciliationResult.Refused(
                    EuTranspositionBridgeReconciliationRefusal.LegiluxIdentityNotSingular,
                    $"The Legilux relation {relation.LegiluxMeasureUri} -> {relation.EuWorkUri} has {matches.Length} exact identity observations.");
            }

            var identity = matches[0];
            usedIdentities.Add(identity);
            var identityEli = identity.WorkKindAssertion?.Work.Value(FactsIdentifierFamily.Eli);
            if (identity.WorkKindAssertion is { } assertedKind &&
                (assertedKind.Work.Publisher != PublisherId.EuEurLex ||
                 !string.Equals(identityEli, identity.EuEli, StringComparison.Ordinal)))
            {
                return EuTranspositionBridgeReconciliationResult.Refused(
                    EuTranspositionBridgeReconciliationRefusal.LegiluxIdentityContradictsNim,
                    "The Legilux identity row does not retain its exact EU ELI assertion.");
            }

            var targets = scope.Where(value => string.Equals(value.EuEli, identity.EuEli, StringComparison.Ordinal)).ToArray();
            if (targets.Length != 1)
            {
                return EuTranspositionBridgeReconciliationResult.Refused(
                    EuTranspositionBridgeReconciliationRefusal.LegiluxIdentityNotInNimPopulation,
                    $"The Legilux EU identity {identity.EuEli} does not name exactly one Cellar work in the completed NIM population.");
            }
            if (identity.WorkKindAssertion is { } publisherKind &&
                targets[0].Assertion.Kind != publisherKind.Kind)
            {
                return EuTranspositionBridgeReconciliationResult.Refused(
                    EuTranspositionBridgeReconciliationRefusal.LegiluxIdentityContradictsNim,
                    $"The two publishers disagree about the work kind for {identity.EuEli}.");
            }

            reconciledRelations.Add(new LuxembourgTranspositionRelation(
                targets[0].WorkUri,
                relation.LegiluxMeasureUri,
                relation.Acquisition,
                identities.CompletionEvidenceRef));
            reconciliations.Add(new EuTranspositionIdentityReconciliation(
                targets[0].WorkUri,
                identity.EuEli,
                identity.LocalEuWorkUri,
                identity.NationalMeasureUri,
                identities.CompletionEvidenceRef,
                nim.CompletionEvidenceRef));
        }

        if (usedIdentities.Count != identities.Relations.Count)
        {
            return EuTranspositionBridgeReconciliationResult.Refused(
                EuTranspositionBridgeReconciliationRefusal.IdentityObservationUnused,
                "A completed Legilux target-identity observation did not reconcile to an admitted transposes row.");
        }

        var reconciledLegilux = LuxembourgTranspositionProductionResult.Success(
            reconciledRelations, legilux.CompletionEvidenceRef);
        var population = await _populationProducer.ProduceAsync(
            scope.Select(static value => value.Assertion).ToArray(),
            reconciledLegilux,
            nim,
            cancellationToken).ConfigureAwait(false);
        return population.Delivered
            ? EuTranspositionBridgeReconciliationResult.Success(population, reconciliations)
            : EuTranspositionBridgeReconciliationResult.Refused(
                EuTranspositionBridgeReconciliationRefusal.PopulationRefused,
                $"The reconciled population refused: {population.Refusal}: {population.Detail}",
                population);
    }

}
