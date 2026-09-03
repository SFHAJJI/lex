using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>
/// The twelve cut-level count families D1-01 Candidate 5 R3 freezes at lines 355 to 368, in the
/// exact order the text lists them. Each is a disjoint bucket for a globally scoped conflict,
/// drift, or implementation-error variant reported by any bound package for one cut.
/// </summary>
public enum GlobalBlockerFamily
{
    [JsonStringEnumMemberName("manifest_selector_conflict")]
    ManifestSelectorConflict = 1,

    [JsonStringEnumMemberName("manifest_boundary_drift")]
    ManifestBoundaryDrift = 2,

    [JsonStringEnumMemberName("root_definition_conflict")]
    RootDefinitionConflict = 3,

    [JsonStringEnumMemberName("duplicate_closure")]
    DuplicateClosure = 4,

    [JsonStringEnumMemberName("missing_closure")]
    MissingClosure = 5,

    [JsonStringEnumMemberName("closure_reconciliation_conflict")]
    ClosureReconciliationConflict = 6,

    [JsonStringEnumMemberName("witness_reconciliation_conflict")]
    WitnessReconciliationConflict = 7,

    [JsonStringEnumMemberName("paging_partition_or_truncation_conflict")]
    PagingPartitionOrTruncationConflict = 8,

    [JsonStringEnumMemberName("robots_policy_conflict")]
    RobotsPolicyConflict = 9,

    [JsonStringEnumMemberName("positive_feed_reconciliation_conflict")]
    PositiveFeedReconciliationConflict = 10,

    [JsonStringEnumMemberName("implementation_error")]
    ImplementationError = 11,

    /// <summary>
    /// R3 line 370: "An unknown variant increments unclassified_global_blocker". This is not a
    /// family a bound package declares for itself; it is what <see cref="GlobalBlockerRegistry.Classify"/>
    /// returns for a raw family key that matches none of the other eleven canonical keys.
    /// </summary>
    [JsonStringEnumMemberName("unclassified_global_blocker")]
    UnclassifiedGlobalBlocker = 12,
}

/// <summary>
/// One reported occurrence of a globally scoped conflict, drift, or implementation error, as
/// raised by a bound package before this contract classifies it.
/// </summary>
/// <remarks>
/// <see cref="RawFamilyKey"/> is deliberately open beyond a bounded printable-identifier shape: R3
/// names the eleven canonical keys a package should report (lines 357 to 367), but nothing in the
/// text forbids a package from reporting something else, and line 370 exists specifically to
/// define what happens when one does.
/// </remarks>
public sealed record GlobalBlockerOccurrence
{
    public GlobalBlockerOccurrence(string rawFamilyKey, string subtypeKey)
    {
        RawFamilyKey = SourceCoreValidation.RequireMemberKey(rawFamilyKey, nameof(rawFamilyKey));
        SubtypeKey = SourceCoreValidation.RequireMemberKey(subtypeKey, nameof(subtypeKey));
    }

    /// <summary>The family key exactly as the bound package reported it.</summary>
    public string RawFamilyKey { get; }

    /// <summary>
    /// The closed-subtype identity within the family. R3 line 370: "each family retains closed
    /// subtype counts, so aggregation cannot hide which conflict occurred." The source text does
    /// not enumerate a subtype catalog for any of the twelve families, so this key is open
    /// vocabulary: whatever the reporting package names its own conflict variant as. Inventing a
    /// subtype catalog the text does not give was rejected; see the remarks on
    /// <see cref="GlobalBlockerRegistry"/>.
    /// </summary>
    public string SubtypeKey { get; }
}

/// <summary>
/// One registered family's independently derived tally: a total and its closed subtype breakdown.
/// </summary>
public sealed record GlobalBlockerFamilyTally(int Total, IReadOnlyDictionary<string, int> SubtypeCounts);

/// <summary>
/// The independently recomputed cut-level count vector. R3 line 403 requires <c>cut_release_gate/1</c>
/// to recompute the count vector "from the complete cut ledgers" rather than trust a supplied one;
/// this type is that recomputation.
/// </summary>
/// <remarks>
/// It can never be missing a family, hold a duplicate, or carry an unregistered one, because
/// <see cref="Recompute"/> is the only path to an instance and it tallies over the full closed
/// <see cref="GlobalBlockerFamily"/> vocabulary rather than over whatever a caller happened to
/// list. That is what makes it fit to recompute against: a value with the same failure modes as
/// the thing it is meant to check would not be an independent check at all.
/// </remarks>
public sealed class GlobalBlockerCountVector
{
    private readonly IReadOnlyDictionary<GlobalBlockerFamily, GlobalBlockerFamilyTally> _tallies;

    private GlobalBlockerCountVector(
        IReadOnlyDictionary<GlobalBlockerFamily, GlobalBlockerFamilyTally> tallies) =>
        _tallies = tallies;

    /// <summary>
    /// Classifies every occurrence through <see cref="GlobalBlockerRegistry.Classify"/> and tallies
    /// it into its family's subtype counts. What "the complete cut ledgers" (R3 line 403) means for
    /// one cut is the caller's evidence to assemble; this method tallies exactly what it is given.
    /// </summary>
    public static GlobalBlockerCountVector Recompute(IReadOnlyList<GlobalBlockerOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        var subtypeCounts = Enum.GetValues<GlobalBlockerFamily>()
            .ToDictionary(family => family, _ => new Dictionary<string, int>(StringComparer.Ordinal));
        foreach (var occurrence in occurrences)
        {
            ArgumentNullException.ThrowIfNull(occurrence);
            var bucket = subtypeCounts[GlobalBlockerRegistry.Classify(occurrence.RawFamilyKey)];
            bucket[occurrence.SubtypeKey] = bucket.GetValueOrDefault(occurrence.SubtypeKey) + 1;
        }

        var tallies = subtypeCounts.ToDictionary(
            pair => pair.Key,
            pair => new GlobalBlockerFamilyTally(
                pair.Value.Values.Sum(),
                new ReadOnlyDictionary<string, int>(pair.Value)));
        return new GlobalBlockerCountVector(
            new ReadOnlyDictionary<GlobalBlockerFamily, GlobalBlockerFamilyTally>(tallies));
    }

    /// <summary>The total occurrence count for one registered family.</summary>
    public int Total(GlobalBlockerFamily family) => Tally(family).Total;

    /// <summary>The closed subtype breakdown for one registered family.</summary>
    public IReadOnlyDictionary<string, int> SubtypeCounts(GlobalBlockerFamily family) =>
        Tally(family).SubtypeCounts;

    /// <summary>True when every registered family's total is zero.</summary>
    public bool AllZero => _tallies.Values.All(static tally => tally.Total == 0);

    private GlobalBlockerFamilyTally Tally(GlobalBlockerFamily family)
    {
        SourceCoreValidation.RequireDefined(family, nameof(family));
        return _tallies[family];
    }
}

/// <summary>
/// D1-01 Candidate 5 R3 lines 355 to 370: <c>cut_global_blocker_registry/1</c>, the separately
/// frozen total naming exactly the twelve cut-level count families and classifying any raw
/// reported variant into exactly one of them.
/// </summary>
/// <remarks>
/// <para>
/// The registry is a fixed static surface rather than a value a caller assembles, because the text
/// freezes the family list itself: "changing the registry or mapping changes its digest and
/// prevents a stale gate from evaluating the cut" (line 370) describes a registry that is a build
/// artifact of this contract, not configuration supplied per call. A caller that wants to bind a
/// cut to the registry it used passes <see cref="RegistryRef"/> to <see cref="CutReleaseGate"/>,
/// so a mismatch there is a wrong or stale value the gate compares against this one derivation --
/// never a copy of itself.
/// </para>
/// <para>
/// <b>What is deliberately not built.</b> The source text gives no catalog mapping specific
/// conflict, drift, or implementation-error variants to a subtype identity within a family, only
/// the requirement that each family retain "closed subtype counts" (line 370). Inventing such a
/// catalog was rejected as a term the accepted text does not itself define.
/// <see cref="GlobalBlockerOccurrence.SubtypeKey"/> is therefore open vocabulary supplied by the
/// reporting package, and this registry closes only the twelve family names themselves, which the
/// text gives in full at lines 357 to 368.
/// </para>
/// </remarks>
public static class GlobalBlockerRegistry
{
    public const string SchemaId = "cut_global_blocker_registry/1";
    private const string DigestScope = "lex-v3/cut-global-blocker-registry/1";

    /// <summary>Every registered family, in the exact order R3 lines 357 to 368 list them.</summary>
    public static IReadOnlyList<GlobalBlockerFamily> Families { get; } =
        Array.AsReadOnly(Enum.GetValues<GlobalBlockerFamily>());

    /// <summary>
    /// The frozen registry's identity: a content-derived resource id and the SHA-256 of the exact
    /// canonical family-key list it names. Changing <see cref="Families"/> changes this value,
    /// which is the mechanism line 370 describes ("changing the registry ... changes its digest").
    /// </summary>
    public static SourceArtifactRef RegistryRef { get; } = BuildRegistryRef();

    /// <summary>
    /// Classifies a raw family key reported by a bound package. An exact, case-sensitive match
    /// against one of the eleven canonical keys yields that family; anything else -- including the
    /// literal text "unclassified_global_blocker", an empty or malformed key, and a key that simply
    /// names no family this registry knows -- yields
    /// <see cref="GlobalBlockerFamily.UnclassifiedGlobalBlocker"/>. Total: every input has exactly
    /// one classification and this method never throws.
    /// </summary>
    public static GlobalBlockerFamily Classify(string? rawFamilyKey) => rawFamilyKey switch
    {
        "manifest_selector_conflict" => GlobalBlockerFamily.ManifestSelectorConflict,
        "manifest_boundary_drift" => GlobalBlockerFamily.ManifestBoundaryDrift,
        "root_definition_conflict" => GlobalBlockerFamily.RootDefinitionConflict,
        "duplicate_closure" => GlobalBlockerFamily.DuplicateClosure,
        "missing_closure" => GlobalBlockerFamily.MissingClosure,
        "closure_reconciliation_conflict" => GlobalBlockerFamily.ClosureReconciliationConflict,
        "witness_reconciliation_conflict" => GlobalBlockerFamily.WitnessReconciliationConflict,
        "paging_partition_or_truncation_conflict" => GlobalBlockerFamily.PagingPartitionOrTruncationConflict,
        "robots_policy_conflict" => GlobalBlockerFamily.RobotsPolicyConflict,
        "positive_feed_reconciliation_conflict" => GlobalBlockerFamily.PositiveFeedReconciliationConflict,
        "implementation_error" => GlobalBlockerFamily.ImplementationError,
        _ => GlobalBlockerFamily.UnclassifiedGlobalBlocker,
    };

    /// <summary>The exact wire key for a family: the inverse of the non-default branches of <see cref="Classify"/>.</summary>
    public static string WireKey(GlobalBlockerFamily family) => family switch
    {
        GlobalBlockerFamily.ManifestSelectorConflict => "manifest_selector_conflict",
        GlobalBlockerFamily.ManifestBoundaryDrift => "manifest_boundary_drift",
        GlobalBlockerFamily.RootDefinitionConflict => "root_definition_conflict",
        GlobalBlockerFamily.DuplicateClosure => "duplicate_closure",
        GlobalBlockerFamily.MissingClosure => "missing_closure",
        GlobalBlockerFamily.ClosureReconciliationConflict => "closure_reconciliation_conflict",
        GlobalBlockerFamily.WitnessReconciliationConflict => "witness_reconciliation_conflict",
        GlobalBlockerFamily.PagingPartitionOrTruncationConflict => "paging_partition_or_truncation_conflict",
        GlobalBlockerFamily.RobotsPolicyConflict => "robots_policy_conflict",
        GlobalBlockerFamily.PositiveFeedReconciliationConflict => "positive_feed_reconciliation_conflict",
        GlobalBlockerFamily.ImplementationError => "implementation_error",
        GlobalBlockerFamily.UnclassifiedGlobalBlocker => "unclassified_global_blocker",
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static SourceArtifactRef BuildRegistryRef()
    {
        var canonicalBytes = Encoding.UTF8.GetBytes(
            SchemaId + "\n" + string.Join(",", Families.Select(WireKey)));
        var sha256 = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
        return new SourceArtifactRef(
            ContentDerivedIdentity.DeriveUuidUrn(DigestScope, canonicalBytes), sha256);
    }
}
