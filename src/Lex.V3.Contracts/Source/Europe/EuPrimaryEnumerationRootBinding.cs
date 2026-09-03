using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a primary-enumeration root binding was refused. Closed.
/// </summary>
public enum EuPrimaryEnumerationRefusal
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>A resolved root is blank, which is not an identity.</summary>
    ResolvedRootBlank = 1,

    /// <summary>
    /// A resolved root could not be reduced to Appendix A's exact lexical form
    /// (<see cref="EuPackRootCanonicalForm"/>). Refused rather than admitted as written and rather
    /// than silently dropped: a string this binding cannot even identify must not be allowed to
    /// stand for a root that Appendix A did, or did not, freeze.
    /// </summary>
    ResolvedRootNotCanonical = 2,

    /// <summary>A resolved root occurs twice in one closure result.</summary>
    ResolvedRootRepeated = 3,

    /// <summary>
    /// A resolved root, once canonical, is not a member of <see cref="EuAppendixASeedMap.PackRoots"/>.
    /// This is the ruling's ninth point made structural: the primary enumeration is D1-05's own
    /// closure query <em>over the 82-root pack</em>, never over Cellar at large, so a result naming a
    /// Work outside Appendix A can only be a construction or query-plan defect. Unlike the witness's
    /// <see cref="EuFeedTerminal.OutOfPack"/>, there is no positive-evidence terminal here to fall
    /// back to: the primary enumeration has no independent reason to have found this root at all, and
    /// admitting it would silently widen the pack this ruling fixes at exactly 82.
    /// </summary>
    ResolvedRootOutsideAppendixAPack = 4,
}

/// <summary>
/// D1-05's own primary enumeration: the closure query's discovered root set over the exact 82-root
/// Appendix A pack, structurally separate from <c>eu_positive_change_witness/1</c>
/// (<see cref="EuFeedRootIntersection"/>).
/// </summary>
/// <remarks>
/// <para>
/// The design-synthesis ruling (RULING event
/// <c>lex-event-20260903T192615392Z-b13dee192bd84cea970b71cd8ffd4b89</c>) is explicit that the
/// witness is never the primary enumeration: "R3.2's independence rule structurally forbids it. The
/// primary enumeration is D1-05's own closure query over the 82-root pack ...; the witness
/// reconciles against it as required independent evidence." R3.2 itself requires "a structurally
/// different authoritative root or traversal path and a different producer symbol from the primary
/// enumeration" for any witness to count as independent at all.
/// </para>
/// <para>
/// This type is that primary side. It carries <see cref="ClosureQueryPlanRef"/> as its own producer
/// identity, bound by digest and never equal to the witness's own query-plan or closure-matrix
/// identity (checked at the point the two are actually reconciled, in
/// <see cref="EuPrimaryEnumerationWitnessReconciliation"/>, because this type alone has no witness to
/// compare against). It does not resolve Cellar identities itself: no bounded observation this
/// repository has taken establishes the closure-row predicates and traversal that would, so the
/// resolved root set is a declared input exactly as <see cref="EuFeedRootIntersection"/> declares its
/// own <c>W</c> and <c>P</c>. What this type owns is admission: every root is canonicalized before
/// membership is checked, and R7's "the D1-01 EU root universe is exactly the Appendix A 82-seed map"
/// is enforced by refusing, never silently accepting, a root the closure query names that Appendix A
/// never froze.
/// </para>
/// </remarks>
public sealed class EuPrimaryEnumerationRootBinding
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly HashSet<string> _discoveredRoots;

    private EuPrimaryEnumerationRootBinding(
        SourceArtifactRef closureQueryPlanRef,
        IReadOnlyList<string> discoveredRoots,
        HashSet<string> index,
        string bindingIdentityDigest)
    {
        ClosureQueryPlanRef = closureQueryPlanRef;
        DiscoveredRoots = discoveredRoots;
        _discoveredRoots = index;
        BindingIdentityDigest = bindingIdentityDigest;
    }

    /// <summary>
    /// D1-05's own closure-query-plan identity: which executable row, predicate set, direction,
    /// depth and frontier rule produced <see cref="DiscoveredRoots"/>. This is the "different
    /// producer symbol" R3.2 requires the witness to differ from; it is never the witness's own
    /// <see cref="EuFeedRootIntersection.ClosureMatrixRef"/> or
    /// <see cref="EuWatermarkWitnessPlan.QueryPlanIdentityDigest"/>, and
    /// <see cref="EuPrimaryEnumerationWitnessReconciliation"/> refuses a reconciliation that lets the
    /// two collide.
    /// </summary>
    public SourceArtifactRef ClosureQueryPlanRef { get; }

    /// <summary>
    /// The closure query's discovered roots, each already reduced to Appendix A's exact lexical
    /// form, sorted ordinally. A subset of <see cref="EuAppendixASeedMap.PackRoots"/> by
    /// construction: nothing else can ever survive <see cref="TryBind"/>.
    /// </summary>
    public IReadOnlyList<string> DiscoveredRoots { get; }

    /// <summary>
    /// SHA-256 over the binding: the closure-query-plan reference and the sorted discovered roots.
    /// Two enumerations that report the same digest ran the identical plan against the identical
    /// result.
    /// </summary>
    public string BindingIdentityDigest { get; }

    /// <summary>The only path that mints a primary-enumeration binding.</summary>
    /// <param name="closureQueryPlanRef">D1-05's own closure-query-plan identity.</param>
    /// <param name="resolvedRoots">The roots the closure query discovered, in any order.</param>
    /// <param name="refusal">Why no binding exists, when none does.</param>
    public static EuPrimaryEnumerationRootBinding? TryBind(
        SourceArtifactRef closureQueryPlanRef,
        IReadOnlyList<string> resolvedRoots,
        out EuPrimaryEnumerationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(closureQueryPlanRef);
        ArgumentNullException.ThrowIfNull(resolvedRoots);

        var roots = resolvedRoots.ToArray();
        if (Array.Exists(roots, static root => root is null))
        {
            throw new ArgumentException("A resolved root cannot be null.", nameof(resolvedRoots));
        }

        if (Array.Exists(roots, string.IsNullOrWhiteSpace))
        {
            refusal = EuPrimaryEnumerationRefusal.ResolvedRootBlank;
            return null;
        }

        for (var i = 0; i < roots.Length; i++)
        {
            var canonical = EuPackRootCanonicalForm.TryCanonicalize(roots[i], out _);
            if (canonical is null)
            {
                refusal = EuPrimaryEnumerationRefusal.ResolvedRootNotCanonical;
                return null;
            }

            roots[i] = canonical;
        }

        var distinct = new HashSet<string>(roots, StringComparer.Ordinal);
        if (distinct.Count != roots.Length)
        {
            refusal = EuPrimaryEnumerationRefusal.ResolvedRootRepeated;
            return null;
        }

        // Point 9: never exceed the 82-root map. Checked after canonicalization and after the
        // repeat check, so a root that fails for a more specific reason is refused for that reason
        // first, and a genuinely out-of-pack canonical root is the one condition left standing.
        if (Array.Exists(roots, root => !EuAppendixASeedMap.PackRoots.Contains(root)))
        {
            refusal = EuPrimaryEnumerationRefusal.ResolvedRootOutsideAppendixAPack;
            return null;
        }

        Array.Sort(roots, StringComparer.Ordinal);
        refusal = EuPrimaryEnumerationRefusal.None;
        return new EuPrimaryEnumerationRootBinding(
            closureQueryPlanRef,
            Array.AsReadOnly(roots),
            distinct,
            BindingDigest(closureQueryPlanRef, roots));
    }

    /// <summary>True when a canonical root is a member of this enumeration's own discovered set.</summary>
    public bool Contains(string canonicalRoot)
    {
        ArgumentNullException.ThrowIfNull(canonicalRoot);
        return _discoveredRoots.Contains(canonicalRoot);
    }

    private static string BindingDigest(SourceArtifactRef planRef, IReadOnlyList<string> sortedRoots)
    {
        var lines = new List<string>
        {
            "eu_primary_enumeration_root_binding/1",
            "closure_query_plan=" + planRef.ResourceId + "@" + planRef.Sha256,
        };
        lines.AddRange(sortedRoots.Select(static root => "discovered_root=" + root));
        return Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(string.Join('\n', lines))));
    }
}
