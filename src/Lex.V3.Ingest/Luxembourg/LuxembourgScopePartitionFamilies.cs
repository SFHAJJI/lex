using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// One declared subject range, named by its S census, A assertions and G relations requests.
/// Several members describe a disjoint bounded scope, not the intervening publisher population.
/// </summary>
public sealed record LuxembourgScopePartitionFamilies(
    string CensusFamilyKey, string AssertionFamilyKey, string RelationFamilyKey)
{
    internal static void Validate(
        IReadOnlyList<(LuxembourgPartitionRunRequest Request, BoundMachineRequest Witness, LuxembourgPartitionChain? Cover)> families,
        IReadOnlyList<LuxembourgScopePartitionFamilies> members)
    {
        if (members.Count == 0 || families.Count != checked(members.Count * 3))
            throw new ArgumentException("A scoped run requires exactly S, A and G for every declared member.", nameof(members));
        var byId = new Dictionary<string, LuxembourgPartitionRunRequest>(StringComparer.Ordinal);
        foreach (var (request, _, _) in families)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!byId.TryAdd(request.Partition.PartitionId, request))
                throw new ArgumentException("Scoped family partition identities must be unique.", nameof(families));
        }
        var proofKeys = new HashSet<string>(byId.Keys, StringComparer.Ordinal);
        foreach (var (request, _, cover) in families)
        {
            if (cover is null) continue;
            if (cover.RootRange != request.Partition)
                throw new ArgumentException("A supplied cover must preserve its requested root identity and bounds.", nameof(families));
            foreach (var leaf in cover.Leaves)
                if (leaf != cover.RootRange && !proofKeys.Add(leaf.PartitionId))
                    throw new ArgumentException("Cover leaves must have distinct identities across the declared scope.", nameof(families));
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var ranges = new List<LuxembourgQueryPartitionRange>();
        SourceArtifactRef? commonPlan = null;
        foreach (var member in members)
        {
            ArgumentNullException.ThrowIfNull(member);
            LuxembourgQueryPartitionRange? range = null;
            foreach (var (key, set) in new[] { (member.CensusFamilyKey, "S"),
                (member.AssertionFamilyKey, "A"), (member.RelationFamilyKey, "G") })
            {
                if (string.IsNullOrWhiteSpace(key) || !used.Add(key) || !byId.TryGetValue(key, out var request))
                    throw new ArgumentException("Every scoped family key must name one distinct requested member.", nameof(members));
                if (request.SetId != set)
                    throw new ArgumentException($"Scoped family {key} must use query set {set}.", nameof(families));
                var plan = LuxembourgQueryPlanIdentity.Create(request.InvariantPlanResourceId, request.InvariantPlan);
                commonPlan ??= plan;
                if (plan != commonPlan)
                    throw new ArgumentException("Scoped members must share one invariant plan and resource identity.", nameof(families));
                range ??= request.Partition;
                if (range.StartInclusive != request.Partition.StartInclusive || range.EndExclusive != request.Partition.EndExclusive)
                    throw new ArgumentException("A member's S, A and G ranges must be aligned.", nameof(families));
                foreach (var cursor in new[] { range.StartInclusive, range.EndExclusive })
                    if (cursor.Key2.Length != 0 || cursor.Key3.Length != 0 || cursor.Key4.Length != 0 ||
                        cursor.Key5.Length != 0 || cursor.Key6.Length != 0)
                        throw new ArgumentException("Scoped member boundaries must include whole subjects.", nameof(families));
            }
            ranges.Add(range!);
        }
        ranges.Sort(static (left, right) => left.StartInclusive.CompareTo(right.StartInclusive));
        for (var index = 1; index < ranges.Count; index++)
            if (ranges[index - 1].EndExclusive.CompareTo(ranges[index].StartInclusive) > 0)
                throw new ArgumentException("Declared scope ranges must be disjoint.", nameof(members));
    }
}
