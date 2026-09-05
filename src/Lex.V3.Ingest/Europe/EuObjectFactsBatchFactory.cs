using System;
using System.Collections.Generic;
using System.Linq;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// Everything a run needs to ask families P, X, W and M, EXCEPT which objects to ask about.
/// </summary>
/// <remarks>
/// <para>
/// THE POINT OF THIS TYPE IS WHAT IT DOES NOT CARRY. Before D1-05g the caller handed
/// <see cref="EuQueryExecutionAdapter.RunAsync"/> fully built object-facts requests, each with its
/// own <c>BatchObjects</c> list, and every caller in the repository passed THE SEED ROOTS. The
/// census then discovered consolidated states, the decoder walked root PLUS those states, and
/// family P had never been asked about them. A caller cannot get that right, because the object
/// set is not known until the census this same run proves has been reopened and reverified.
/// </para>
/// <para>
/// So the caller supplies the PLAN and the POLICY and never the object list, and the run derives
/// the objects from its own proven census. That is a sequencing fix rather than a tolerance, and
/// lane A established why a tolerance cannot work: decoding an undescribed state as
/// <c>NotObserved</c> fails, because the content class is derived from those same family P rows,
/// so the moment it was tried the decode refused with
/// <c>ContentClassClosurePositionMismatch</c>. There is no reading of an absent row that produces
/// a content class, and inventing one would be the fabricated observation this repository exists
/// to make impossible.
/// </para>
/// </remarks>
/// <param name="Plan">The object-facts plan every batch of this run binds against.</param>
/// <param name="PlanResourceId">That plan's own resource id, carried into each request.</param>
/// <param name="RendererSource">The renderer source every batch renders through.</param>
/// <param name="SourceWitness">The bound request robots negotiation resolves against.</param>
public sealed record EuObjectFactsBatchPolicy(
    EuObjectFactsDiscoveryPlan Plan,
    string PlanResourceId,
    MachineQueryRendererSource RendererSource,
    BoundMachineRequest SourceWitness);

/// <summary>
/// Turns one run's own observed object set into the batch requests families P, X, W and M ask.
/// </summary>
/// <remarks>
/// <para>
/// THE BATCH IS THE PARTITION, so this decides the partitions of the run. Objects are sorted
/// ordinally and deduplicated before chunking, which is what makes the partition key reproducible:
/// <c>EuObjectFactsDiscoveryPlan.PartitionKeyFor</c> digests the batch's own sorted, deduplicated,
/// LF-joined members, so two runs observing the same objects in a different order mint the same
/// key and a reader diffing them sees no movement where none happened.
/// </para>
/// <para>
/// CHUNK SIZE IS <see cref="EuObjectFactsDiscoveryPlan.BatchCapacity"/> AND IS NOT A CHOICE MADE
/// HERE. That constant is 50 because every batch member travels as its own
/// <c>publisher_literal</c> parameter and <c>MachineQueryValidation.MaximumParameterCount</c> is
/// 64, of which nine are always spent on <c>pass_id</c>, <c>has_cursor</c> and up to seven
/// cursor-continuation parameters; 55 remain and 50 keeps a margin. This factory reads the
/// constant rather than restating it, so a batch that could not be bound cannot be minted here.
/// </para>
/// <para>
/// THREE SETS GET O AND ONE GETS THE ROOTS, and that asymmetry is the plan's rule rather than a
/// choice made here. <c>EuObjectFactsDiscoveryPlan.Bind</c> REFUSES a root-watermark batch
/// carrying any member outside Appendix A's 82 pack roots, and it is right to: family W reads
/// each pack root's own <c>lastModificationDate</c>, and a consolidated state is not a pack root
/// and has no watermark of its own to read. Families P, X and M are asked about O, being the
/// roots together with the states this run's census discovered. Building W over O was tried
/// first and the plan's own guard rejected it immediately, which is the guard doing exactly its
/// job.
/// </para>
/// <para>
/// EACH (SET, BATCH) PAIR IS ITS OWN PARTITION with its own count evidence, never merged, which
/// is what lets one batch refuse with <c>PartitionRequired</c> at the row ceiling while its
/// siblings prove.
/// </para>
/// </remarks>
internal static class EuObjectFactsBatchFactory
{
    /// <summary>The sets asked about O, being the roots plus the discovered states.</summary>
    internal static readonly EuObjectFactsQuerySet[] SetsOverObservedObjects =
    [
        EuObjectFactsQuerySet.ObjectFacts,
        EuObjectFactsQuerySet.ExpressionFacts,
        EuObjectFactsQuerySet.ManifestationFacts,
    ];

    /// <summary>
    /// The set asked about the pack roots alone, because the plan refuses it any other member.
    /// </summary>
    internal static readonly EuObjectFactsQuerySet[] SetsOverPackRootsOnly =
    [
        EuObjectFactsQuerySet.RootWatermark,
    ];

    /// <summary>
    /// One request per (set, batch), over the run's own observed objects.
    /// </summary>
    /// <param name="policy">The plan, its resource id, the renderer source and the witness.</param>
    /// <param name="observedObjects">
    /// This run's observed object set O: every seed's root together with every consolidated state
    /// that run's own census actually delivered. Sorted and deduplicated here rather than trusted.
    /// </param>
    internal static IReadOnlyList<EuObjectFactsPartitionRunRequest> Build(
        EuObjectFactsBatchPolicy policy,
        IReadOnlyCollection<string> observedObjects,
        IReadOnlyCollection<string> packRoots)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(observedObjects);
        ArgumentNullException.ThrowIfNull(packRoots);
        if (observedObjects.Count == 0)
        {
            throw new ArgumentException(
                "a run with no observed objects has nothing for P, X or M to ask about.",
                nameof(observedObjects));
        }

        if (packRoots.Count == 0)
        {
            throw new ArgumentException(
                "a run with no pack roots has nothing for W to ask about.",
                nameof(packRoots));
        }

        var requests = new List<EuObjectFactsPartitionRunRequest>();
        foreach (var set in SetsOverObservedObjects)
        {
            AddBatches(requests, policy, set, observedObjects);
        }

        foreach (var set in SetsOverPackRootsOnly)
        {
            AddBatches(requests, policy, set, packRoots);
        }

        return requests;
    }

    private static void AddBatches(
        List<EuObjectFactsPartitionRunRequest> into,
        EuObjectFactsBatchPolicy policy,
        EuObjectFactsQuerySet set,
        IReadOnlyCollection<string> objects)
    {
        // Sorted and deduplicated HERE rather than trusted from the caller: the partition key is a
        // digest over the batch's own sorted members, so two runs observing the same objects in a
        // different order have to mint the same key or a reader diffing them sees movement where
        // none happened.
        var ordered = objects
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        for (var offset = 0; offset < ordered.Length; offset += EuObjectFactsDiscoveryPlan.BatchCapacity)
        {
            into.Add(new EuObjectFactsPartitionRunRequest(
                policy.Plan,
                policy.PlanResourceId,
                set,
                ordered.Skip(offset).Take(EuObjectFactsDiscoveryPlan.BatchCapacity).ToArray(),
                policy.RendererSource));
        }
    }
}
