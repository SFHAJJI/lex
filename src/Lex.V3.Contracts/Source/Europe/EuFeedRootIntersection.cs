using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The four terminals R3 gives every feed entry that passes through the exact watermark. Closed,
/// and with no member for "not yet decided": the algorithm is total, so an entry that reaches it
/// leaves with one of these.
/// </summary>
/// <remarks>
/// There is deliberately no zero member. A default-valued terminal would be a fifth state that no
/// classification produced, and the sum equation in
/// <see cref="EuFeedTerminalReconciliation"/> is over exactly these four.
/// </remarks>
public enum EuFeedTerminal
{
    /// <summary>
    /// R3: <c>I</c> is nonempty and <c>O</c> is empty. Every in-pack projection must occur in the
    /// cut's corresponding discovered family.
    /// </summary>
    [JsonStringEnumMemberName("eu_feed_positive_in_pack")]
    InPack = 1,

    /// <summary>
    /// R3: <c>I</c> is empty and <c>O</c> is nonempty. Positive locator evidence only. It never
    /// widens the pack.
    /// </summary>
    [JsonStringEnumMemberName("eu_feed_positive_out_of_pack")]
    OutOfPack = 2,

    /// <summary>
    /// R3: both <c>I</c> and <c>O</c> are nonempty. Neither side is discarded and neither is
    /// treated as the other.
    /// </summary>
    [JsonStringEnumMemberName("eu_feed_positive_mixed_scope")]
    MixedScope = 3,

    /// <summary>
    /// R3: identity resolution, watermark membership, family projection, or the complete
    /// <c>I</c> and <c>O</c> partition could not close exactly, including a resolved entry for
    /// which both sets are empty.
    /// </summary>
    [JsonStringEnumMemberName("eu_feed_positive_unresolved_or_ambiguous")]
    UnresolvedOrAmbiguous = 4,
}

/// <summary>
/// Which of R3's four named causes sent an entry to
/// <see cref="EuFeedTerminal.UnresolvedOrAmbiguous"/>. Closed, and the four members are R3's own
/// words rather than a taxonomy invented here.
/// </summary>
public enum EuFeedUnresolvedCause
{
    /// <summary>The entry terminated somewhere else.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The bound resolution did not produce the entry's complete Work-root set. This contract does
    /// not resolve identity itself and therefore cannot second-guess that; it is a declared input.
    /// </summary>
    [JsonStringEnumMemberName("identity_resolution_did_not_close")]
    IdentityResolutionDidNotClose = 1,

    /// <summary>
    /// The entry is not one the watermark traversal delivered, so it did not arrive through the
    /// exact watermark this witness froze.
    /// </summary>
    [JsonStringEnumMemberName("watermark_membership_did_not_close")]
    WatermarkMembershipDidNotClose = 2,

    /// <summary>
    /// A family projection names a source Work root the entry did not resolve to, so the projection
    /// cannot be attributed to either side of the partition.
    /// </summary>
    [JsonStringEnumMemberName("family_projection_did_not_close")]
    FamilyProjectionDidNotClose = 3,

    /// <summary>
    /// The partition did not close. <c>I</c> and <c>O</c> are computed here from <c>W</c> and
    /// <c>P</c>, so they are disjoint and exhaust <c>W</c> by construction; the one instance that
    /// remains reachable is R3's own, a resolved entry for which both sets are empty.
    /// </summary>
    [JsonStringEnumMemberName("partition_did_not_close")]
    PartitionDidNotClose = 4,
}

/// <summary>
/// The typed reason R3 requires an out-of-pack receipt to retain.
/// </summary>
/// <remarks>
/// <para>
/// One member, and the hole is deliberate. R3 asks the receipt to retain a typed reason but does
/// not enumerate one, and the interesting subdivisions - not a seed, not reached by closure,
/// outside the selected category, beyond a per-root or pack guard - are all statements about the
/// pack's closure structure. No bounded observation of that structure exists: the two measurements
/// this witness rests on cover the watermark's order semantics and its lexical profile and say
/// nothing about closure. Emitting one of those names would be a claim about an observation nobody
/// has taken.
/// </para>
/// <para>
/// So the vocabulary carries exactly what the algorithm computed and nothing more: the Work root is
/// not a member of the discovered pack root set. When a reviewed closure observation exists, this
/// enum is where its subdivisions belong.
/// </para>
/// </remarks>
public enum EuFeedOutOfPackReason
{
    /// <summary>The entry has no out-of-pack component.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The Work root is not a member of the discovered pack root set. This restates the set
    /// arithmetic and claims nothing beyond it.
    /// </summary>
    [JsonStringEnumMemberName("not_a_member_of_the_discovered_pack_root_set")]
    NotAMemberOfTheDiscoveredPackRootSet = 1,
}

/// <summary>Why an intersection binding was refused. Closed.</summary>
public enum EuFeedIntersectionRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The discovered pack root set is empty. Every resolved entry would then terminate out of
    /// pack, and the cut would report a total absence of in-pack change while looking well formed.
    /// </summary>
    [JsonStringEnumMemberName("pack_root_set_empty")]
    PackRootSetEmpty = 1,

    /// <summary>A pack root is blank, which is not an identity and would match nothing.</summary>
    [JsonStringEnumMemberName("pack_root_blank")]
    PackRootBlank = 2,

    /// <summary>
    /// A pack root occurs twice. <c>P</c> is a set; a bag would make the pack look larger than the
    /// closure discovered.
    /// </summary>
    [JsonStringEnumMemberName("pack_root_repeated")]
    PackRootRepeated = 3,

    /// <summary>
    /// A discovered family row names a source Work root outside the pack. R7 requires in-pack
    /// projections to reconcile to a named discovered family, and a family index reaching outside
    /// the pack would admit a projection the pack never discovered.
    /// </summary>
    [JsonStringEnumMemberName("discovered_family_row_outside_the_pack")]
    DiscoveredFamilyRowOutsideThePack = 4,

    /// <summary>
    /// A pack root, or a discovered family row's source Work root, could not be reduced to
    /// Appendix A's exact lexical form (see <see cref="EuPackRootCanonicalForm"/>). This is
    /// refused rather than admitted as written and rather than silently excluded from the pack:
    /// the binding fixes <c>P</c> once, and a string this contract cannot even identify must not
    /// be allowed to stand for a root that Appendix A did, or did not, freeze.
    /// </summary>
    [JsonStringEnumMemberName("pack_root_not_canonical")]
    PackRootNotCanonical = 5,
}

/// <summary>
/// Why a Work-root string could not be reduced to Appendix A's exact lexical form: <c>http</c>
/// scheme, no query, no fragment, no doubled slash, and exactly one trailing slash removed if
/// present - otherwise byte for byte as supplied. Closed.
/// </summary>
public enum EuPackRootCanonicalFormRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The root is not a well formed absolute URI.</summary>
    [JsonStringEnumMemberName("root_uri_unparseable")]
    RootUriUnparseable = 1,

    /// <summary>
    /// The root's scheme is neither <c>http</c> nor <c>https</c> - the only two
    /// <see cref="EuWemiIdentityBoundary"/> already admits as naming the same Cellar origin.
    /// </summary>
    [JsonStringEnumMemberName("root_scheme_not_http_or_https")]
    RootSchemeNotHttpOrHttps = 2,

    /// <summary>
    /// The root carries a query string. A Cellar Work root never has one, and without this
    /// refusal the trailing-slash trim below would silently remove a slash that happens to sit at
    /// the end of the query's own value, which <see cref="EuPackRootCanonicalForm"/> otherwise
    /// retains byte for byte.
    /// </summary>
    [JsonStringEnumMemberName("root_uri_has_query")]
    RootUriHasQuery = 3,

    /// <summary>
    /// The root carries a fragment. A Cellar Work root never has one, and without this refusal
    /// the trailing-slash trim below would silently remove a slash that happens to sit at the end
    /// of the fragment itself, which <see cref="EuPackRootCanonicalForm"/> otherwise retains byte
    /// for byte.
    /// </summary>
    [JsonStringEnumMemberName("root_uri_has_fragment")]
    RootUriHasFragment = 4,

    /// <summary>
    /// The root's authority-plus-path carries two or more consecutive slashes. A Cellar Work root
    /// never has one, and without this refusal two distinct lexical forms - differing only in how
    /// many slashes they repeat, at the end or in the middle - would canonicalize to the same
    /// string, which is exactly the injectivity this refusal exists to preserve.
    /// </summary>
    [JsonStringEnumMemberName("root_uri_has_double_slash")]
    RootUriHasDoubleSlash = 5,
}

/// <summary>
/// The one place a Work-root string is reduced to Appendix A's exact lexical form.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrower than a general URI normalizer. It repeats exactly the one equivalence
/// <see cref="EuWemiIdentityBoundary"/> already grants - an <c>http</c> and an <c>https</c>
/// spelling of the same origin name the same Cellar object - and nothing else: no host casing, no
/// percent-decoding, no port folding. Those are axes EuWemiIdentityBoundary's own ordinal
/// comparison does not treat as one identity either (<c>RequirePublisherUriNamesTheKey</c> is
/// exact <see cref="StringComparison.Ordinal"/> against two literal origins), and inventing
/// tolerance for them here would let two genuinely different publisher URIs collapse into one
/// pack root.
/// </para>
/// <para>
/// A trailing slash is stripped rather than refused: Appendix A's frozen form never carries one,
/// and removing exactly one - never more - is unambiguous. A query, a fragment, and a doubled
/// slash anywhere in the authority-plus-path are refused rather than reduced: a Cellar Work root
/// never carries any of the three, and admitting one would either let the trailing-slash trim
/// silently corrupt a query or fragment that happens to end in a slash - which this function
/// otherwise retains byte for byte - or let two distinct lexical forms that differ only in how
/// many slashes they repeat canonicalize to the same string. Everything this cannot reduce
/// deterministically is refused instead of guessed at - an unparseable string, a scheme that is
/// not http or https, a query, a fragment, or a doubled slash - so that the caller of
/// <see cref="TryCanonicalize"/> can tell "not this identity" from "not identified at all" apart.
/// </para>
/// </remarks>
public static class EuPackRootCanonicalForm
{
    private const string HttpScheme = "http://";
    private const string HttpsScheme = "https://";

    /// <summary>The only path that canonicalizes a Work-root string.</summary>
    /// <param name="rootUri">The Work-root string a caller resolved or discovered.</param>
    /// <param name="refusal">Why no canonical form exists, when none does.</param>
    public static string? TryCanonicalize(string rootUri, out EuPackRootCanonicalFormRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(rootUri);

        // A parse gate ahead of the ordinal scheme check, so a string that is not a URI at all -
        // blank, containing whitespace, missing an authority - is refused as unparseable rather
        // than falling through to the scheme check and being refused for the wrong reason.
        if (!Uri.TryCreate(rootUri, UriKind.Absolute, out _))
        {
            refusal = EuPackRootCanonicalFormRefusal.RootUriUnparseable;
            return null;
        }

        string afterScheme;
        if (rootUri.StartsWith(HttpScheme, StringComparison.Ordinal))
        {
            afterScheme = rootUri[HttpScheme.Length..];
        }
        else if (rootUri.StartsWith(HttpsScheme, StringComparison.Ordinal))
        {
            afterScheme = rootUri[HttpsScheme.Length..];
        }
        else
        {
            // Ordinal, not Uri.Scheme: a scheme spelled with different casing round-trips through
            // Uri as "http" too, and admitting it here would accept a lexical form Appendix A does
            // not freeze on the strength of a library normalization nobody reviewed.
            refusal = EuPackRootCanonicalFormRefusal.RootSchemeNotHttpOrHttps;
            return null;
        }

        // A Cellar Work root carries none of these three, so each is refused rather than reduced.
        // Checked on afterScheme (never on the literal scheme prefix, which cannot contain any of
        // them), and in this order so a query or fragment is reported as itself rather than as a
        // doubled slash that happens to occur inside it.
        if (afterScheme.Contains('?'))
        {
            refusal = EuPackRootCanonicalFormRefusal.RootUriHasQuery;
            return null;
        }

        if (afterScheme.Contains('#'))
        {
            refusal = EuPackRootCanonicalFormRefusal.RootUriHasFragment;
            return null;
        }

        if (afterScheme.Contains("//", StringComparison.Ordinal))
        {
            refusal = EuPackRootCanonicalFormRefusal.RootUriHasDoubleSlash;
            return null;
        }

        // Exactly one trailing slash is removed, never more. The double-slash refusal above
        // already guarantees afterScheme cannot end in two or more, so this could equivalently be
        // TrimEnd('/'); it is written as a single-character removal instead so that guarantee is
        // enforced at this line too, rather than only relied upon from the check above it.
        var trimmed = afterScheme.EndsWith('/') ? afterScheme[..^1] : afterScheme;

        // No empty-remainder case reaches here: Uri.TryCreate(rootUri, UriKind.Absolute, out _)
        // above already rejects every http/https string whose authority is empty (an http or
        // https URI requires a host), and afterScheme trims to empty only when the entire
        // authority-plus-path was made of slashes, i.e. the authority was empty. There is
        // therefore no rootUri that both parses as absolute and produces an empty trimmed.
        refusal = EuPackRootCanonicalFormRefusal.None;
        return HttpScheme + trimmed;
    }
}

/// <summary>Why a canonical entry set was refused. Closed.</summary>
public enum EuFeedEntrySetRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The traversal delivered the same tie-safe position twice. The canonical entry count is one
    /// side of R3's terminal equation, so a repeated position would inflate it.
    /// </summary>
    [JsonStringEnumMemberName("canonical_entry_repeated")]
    CanonicalEntryRepeated = 1,

    /// <summary>
    /// The steps do not all come from one frozen plan. R3 gives each EU cut exactly one witness,
    /// and an entry set assembled from two plans is two witnesses reported as one.
    /// </summary>
    [JsonStringEnumMemberName("traversal_steps_do_not_share_one_plan")]
    TraversalStepsDoNotShareOnePlan = 2,
}

/// <summary>Why a feed entry observation was refused. Closed.</summary>
public enum EuFeedObservationRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>A resolved Work root is blank, which is not an identity.</summary>
    [JsonStringEnumMemberName("resolved_work_root_blank")]
    ResolvedWorkRootBlank = 1,

    /// <summary>
    /// A resolved Work root occurs twice. <c>W</c> is a set, and a repeated root would be counted
    /// twice on whichever side of the partition it lands on.
    /// </summary>
    [JsonStringEnumMemberName("resolved_work_root_repeated")]
    ResolvedWorkRootRepeated = 2,

    /// <summary>
    /// An observation that declares identity resolution did not close nevertheless carries roots or
    /// projections. Such a value asserts both that there is no resolution and what it was.
    /// </summary>
    [JsonStringEnumMemberName("unresolved_observation_carries_resolution_output")]
    UnresolvedObservationCarriesResolutionOutput = 3,

    /// <summary>
    /// A projection carries a blank family key or a blank projected key. An out-of-pack projection
    /// is never reconciled against a family index, so nothing downstream would notice.
    /// </summary>
    [JsonStringEnumMemberName("projection_key_blank")]
    ProjectionKeyBlank = 4,

    /// <summary>
    /// A resolved Work root, or a projection's source Work root, could not be reduced to Appendix
    /// A's exact lexical form (see <see cref="EuPackRootCanonicalForm"/>). Refused here rather
    /// than left to reach <see cref="EuFeedRootIntersection.Classify"/>, because Classify is total
    /// and would otherwise have to report "could not tell" as the OutOfPack terminal - a normal,
    /// well-formed negative result that a genuinely unidentifiable root must never be mistaken
    /// for.
    /// </summary>
    [JsonStringEnumMemberName("resolved_work_root_not_canonical")]
    ResolvedWorkRootNotCanonical = 5,
}

/// <summary>
/// Which reconciliation defect a <c>positive_feed_reconciliation_conflict</c> count belongs to.
/// R3's three named defects, plus the two ways its terminal equation can fail.
/// </summary>
/// <remarks>
/// These counts are orthogonal derived projections. R3 says so in as many words, and
/// <see cref="EuFeedTerminalReconciliation"/> never adds one to the terminal equation.
/// </remarks>
public enum EuFeedReconciliationConflict
{
    /// <summary>R3: an unresolved-or-ambiguous terminal.</summary>
    [JsonStringEnumMemberName("unresolved_or_ambiguous_terminal")]
    UnresolvedOrAmbiguousTerminal = 1,

    /// <summary>R3: an in-pack or mixed projection missing from its discovered family.</summary>
    [JsonStringEnumMemberName("projection_missing_from_its_discovered_family")]
    ProjectionMissingFromItsDiscoveredFamily = 2,

    /// <summary>R3: duplicate terminal accounting.</summary>
    [JsonStringEnumMemberName("duplicate_terminal_accounting")]
    DuplicateTerminalAccounting = 3,

    /// <summary>
    /// A canonical entry that received no terminal. R3 requires every feed entry through the exact
    /// watermark to receive exactly one.
    /// </summary>
    [JsonStringEnumMemberName("entry_without_a_terminal")]
    EntryWithoutATerminal = 4,

    /// <summary>
    /// A terminal naming an entry the watermark traversal did not deliver, which would add to the
    /// terminal side of R3's equation without a canonical entry behind it.
    /// </summary>
    [JsonStringEnumMemberName("terminal_outside_the_canonical_entry_set")]
    TerminalOutsideTheCanonicalEntrySet = 5,
}

/// <summary>
/// One family projection, retaining the Work root it came from.
/// </summary>
/// <remarks>
/// <para>
/// R3 requires projections to retain their source Work root "so the in-pack and out-of-pack
/// portions are exact". That retention is the whole content of this type: without it, splitting an
/// entry's roots into <c>I</c> and <c>O</c> would leave the projections unattributable and a mixed
/// entry could not report both sides exactly.
/// </para>
/// <para>
/// The family is an opaque member key rather than a closed vocabulary. R7 names closure families in
/// prose and gives each an executable row, but no observation enumerates the set, and a closed enum
/// spelled from that prose would be a vocabulary this repository invented. The index a cut
/// discovered is the authority; this type only has to name a row in it.
/// </para>
/// </remarks>
/// <param name="SourceWorkRoot">The public Work root this projection belongs to.</param>
/// <param name="FamilyMemberKey">The discovered family's member key.</param>
/// <param name="ProjectedKey">The exact row projected into that family.</param>
public sealed record EuFeedFamilyProjection(
    string SourceWorkRoot,
    string FamilyMemberKey,
    string ProjectedKey);

/// <summary>
/// Identity of a tie-safe position: the watermark and the entry key together, ordinally. The same
/// tuple <see cref="EuWatermarkCursor"/> compares on.
/// </summary>
internal sealed class EuWatermarkCursorIdentity : IEqualityComparer<EuWatermarkCursor>
{
    internal static readonly EuWatermarkCursorIdentity Instance = new();

    public bool Equals(EuWatermarkCursor? x, EuWatermarkCursor? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.CompareTo(y) == 0);

    public int GetHashCode(EuWatermarkCursor obj) =>
        HashCode.Combine(obj.WatermarkLexical, obj.CanonicalEntryKey);
}

/// <summary>
/// The cut's canonical entry set: exactly what the watermark traversal newly delivered, in order.
/// </summary>
/// <remarks>
/// <para>
/// This exists so R3's terminal equation has content. Counting terminals against the number of
/// terminations is a tautology that passes forever; the equation is only a check when the canonical
/// count comes from the traversal and the terminal counts come from the classifier, and the two are
/// reconciled afterwards.
/// </para>
/// <para>
/// It is assembled from <see cref="EuWatermarkTraversalStep.NewlyDelivered"/> rather than from a
/// caller-supplied list, so "through the exact watermark" is a property of how the set was built
/// rather than a claim about it. The inclusive reread means each page re-delivers its own boundary
/// group, and those rereads are not deliveries; the step already computes that complement.
/// </para>
/// </remarks>
public sealed class EuFeedWatermarkEntrySet
{
    private readonly HashSet<EuWatermarkCursor> _index;

    private EuFeedWatermarkEntrySet(
        IReadOnlyList<EuWatermarkCursor> canonicalEntries,
        HashSet<EuWatermarkCursor> index)
    {
        CanonicalEntries = canonicalEntries;
        _index = index;
    }

    /// <summary>The canonical entry set in delivery order.</summary>
    public IReadOnlyList<EuWatermarkCursor> CanonicalEntries { get; }

    /// <summary>The canonical entry count, one side of R3's terminal equation.</summary>
    public int Count => CanonicalEntries.Count;

    /// <summary>The only path that closes an entry set.</summary>
    /// <param name="traversal">The cut's steps, in order. An empty traversal is a valid empty set.</param>
    /// <param name="refusal">Why no entry set exists, when none does.</param>
    public static EuFeedWatermarkEntrySet? TryClose(
        IReadOnlyList<EuWatermarkTraversalStep> traversal,
        out EuFeedEntrySetRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(traversal);

        var steps = traversal.ToArray();
        if (Array.Exists(steps, static step => step is null))
        {
            throw new ArgumentException("A traversal step cannot be null.", nameof(traversal));
        }

        string? planIdentity = null;
        var entries = new List<EuWatermarkCursor>();
        var index = new HashSet<EuWatermarkCursor>(EuWatermarkCursorIdentity.Instance);
        foreach (var step in steps)
        {
            var identity = step.Plan.QueryPlanIdentityDigest;
            if (planIdentity is null)
            {
                planIdentity = identity;
            }
            else if (!string.Equals(planIdentity, identity, StringComparison.Ordinal))
            {
                refusal = EuFeedEntrySetRefusal.TraversalStepsDoNotShareOnePlan;
                return null;
            }

            foreach (var delivered in step.NewlyDelivered)
            {
                if (!index.Add(delivered))
                {
                    refusal = EuFeedEntrySetRefusal.CanonicalEntryRepeated;
                    return null;
                }

                entries.Add(delivered);
            }
        }

        refusal = EuFeedEntrySetRefusal.None;
        return new EuFeedWatermarkEntrySet(Array.AsReadOnly(entries.ToArray()), index);
    }

    /// <summary>True when the traversal delivered this exact tie-safe position.</summary>
    public bool Contains(EuWatermarkCursor entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _index.Contains(entry);
    }
}

/// <summary>
/// One feed entry with the resolution someone else bound for it: its complete set <c>W</c> of
/// public Work roots and its exact family projections, before acceptance selection.
/// </summary>
/// <remarks>
/// <para>
/// This contract does not resolve a feed entry to its Work roots, and the reason is that nobody has
/// observed how. R3 asks the witness to bind "the exact publisher identity predicates" and
/// "canonical projections"; the two bounded observations behind this witness established the
/// watermark's order semantics and its lexical profile, and neither says which Cellar predicates
/// carry an entry to its Work roots or what a canonical projection of one looks like. Writing a
/// resolver here would be inventing that answer.
/// </para>
/// <para>
/// So the resolution is a declared input, its identity is bound by digest in
/// <see cref="EuFeedRootIntersection.IdentityPredicateBindingRef"/> without this contract claiming
/// to know its content, and an entry whose resolution did not close terminates as
/// <see cref="EuFeedTerminal.UnresolvedOrAmbiguous"/> rather than being guessed at.
/// </para>
/// </remarks>
public sealed class EuFeedEntryObservation
{
    private EuFeedEntryObservation(
        EuWatermarkCursor entry,
        bool identityResolutionClosed,
        IReadOnlyList<string> resolvedWorkRoots,
        IReadOnlyList<EuFeedFamilyProjection> projections)
    {
        Entry = entry;
        IdentityResolutionClosed = identityResolutionClosed;
        ResolvedWorkRoots = resolvedWorkRoots;
        Projections = projections;
    }

    /// <summary>The entry's tie-safe position in the watermark traversal.</summary>
    public EuWatermarkCursor Entry { get; }

    /// <summary>Whether the bound resolution produced a complete Work-root set for this entry.</summary>
    public bool IdentityResolutionClosed { get; }

    /// <summary>
    /// <c>W</c>: the entry's complete set of public Work roots, sorted ordinally. Empty when the
    /// resolution did not close.
    /// </summary>
    public IReadOnlyList<string> ResolvedWorkRoots { get; }

    /// <summary>The entry's exact family projections, sorted ordinally.</summary>
    public IReadOnlyList<EuFeedFamilyProjection> Projections { get; }

    /// <summary>The only path that mints an observation.</summary>
    /// <param name="entry">The entry's tie-safe position.</param>
    /// <param name="identityResolutionClosed">Whether the bound resolution closed.</param>
    /// <param name="resolvedWorkRoots"><c>W</c>, empty when the resolution did not close.</param>
    /// <param name="projections">The family projections, empty when the resolution did not close.</param>
    /// <param name="refusal">Why no observation exists, when none does.</param>
    public static EuFeedEntryObservation? TryObserve(
        EuWatermarkCursor entry,
        bool identityResolutionClosed,
        IReadOnlyList<string> resolvedWorkRoots,
        IReadOnlyList<EuFeedFamilyProjection> projections,
        out EuFeedObservationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(resolvedWorkRoots);
        ArgumentNullException.ThrowIfNull(projections);

        var roots = resolvedWorkRoots.ToArray();
        var rows = projections.ToArray();
        if (Array.Exists(rows, static row => row is null))
        {
            throw new ArgumentException("A projection cannot be null.", nameof(projections));
        }

        if (!identityResolutionClosed && roots.Length > 0)
        {
            refusal = EuFeedObservationRefusal.UnresolvedObservationCarriesResolutionOutput;
            return null;
        }

        if (!identityResolutionClosed && rows.Length > 0)
        {
            refusal = EuFeedObservationRefusal.UnresolvedObservationCarriesResolutionOutput;
            return null;
        }

        if (Array.Exists(roots, string.IsNullOrWhiteSpace))
        {
            refusal = EuFeedObservationRefusal.ResolvedWorkRootBlank;
            return null;
        }

        // Appendix A's exact lexical form is the only one that reaches Classify's pack-membership
        // comparison. A caller's identity resolution is free to normalize a root to https - R3
        // never says it may not - so that variant is reduced to the canonical form here, ahead of
        // the repeat check below, rather than compared against the pack byte for byte later. This
        // also means an http and an https spelling of the same root now collide at the repeat
        // check instead of silently doubling W.
        for (var i = 0; i < roots.Length; i++)
        {
            // TryCanonicalize's own refusal reason is discarded here: ResolvedWorkRootNotCanonical
            // is the one member this observation's closed refusal vocabulary has for "could not be
            // reduced to Appendix A's exact lexical form" (it covers this loop and the projection-
            // source loop below alike), and an observation refusal only needs to say that, not
            // which of TryCanonicalize's own reasons produced it.
            var canonicalRoot = EuPackRootCanonicalForm.TryCanonicalize(roots[i], out _);
            if (canonicalRoot is null)
            {
                refusal = EuFeedObservationRefusal.ResolvedWorkRootNotCanonical;
                return null;
            }

            roots[i] = canonicalRoot;
        }

        var distinct = new HashSet<string>(roots, StringComparer.Ordinal);
        if (distinct.Count != roots.Length)
        {
            refusal = EuFeedObservationRefusal.ResolvedWorkRootRepeated;
            return null;
        }

        if (Array.Exists(rows, static row => string.IsNullOrWhiteSpace(row.FamilyMemberKey)))
        {
            refusal = EuFeedObservationRefusal.ProjectionKeyBlank;
            return null;
        }

        if (Array.Exists(rows, static row => string.IsNullOrWhiteSpace(row.ProjectedKey)))
        {
            refusal = EuFeedObservationRefusal.ProjectionKeyBlank;
            return null;
        }

        // A projection's source Work root is compared against the pack too (Classify splits
        // InPackProjections/OutOfPackProjections by it, and the reconciliation looks it up in the
        // discovered family index), so it is canonicalized on the same terms as W itself.
        for (var i = 0; i < rows.Length; i++)
        {
            // Same discard as the resolved-root loop above: ResolvedWorkRootNotCanonical does not
            // distinguish which of TryCanonicalize's reasons fired.
            var canonicalSource = EuPackRootCanonicalForm.TryCanonicalize(
                rows[i].SourceWorkRoot, out _);
            if (canonicalSource is null)
            {
                refusal = EuFeedObservationRefusal.ResolvedWorkRootNotCanonical;
                return null;
            }

            rows[i] = rows[i] with { SourceWorkRoot = canonicalSource };
        }

        refusal = EuFeedObservationRefusal.None;
        return new EuFeedEntryObservation(
            entry,
            identityResolutionClosed,
            Array.AsReadOnly(Sorted(roots)),
            Array.AsReadOnly(Sorted(rows)));
    }

    internal static string[] Sorted(IEnumerable<string> values) =>
        values.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    internal static EuFeedFamilyProjection[] Sorted(IEnumerable<EuFeedFamilyProjection> rows) =>
        rows
            .OrderBy(static row => row.SourceWorkRoot, StringComparer.Ordinal)
            .ThenBy(static row => row.FamilyMemberKey, StringComparer.Ordinal)
            .ThenBy(static row => row.ProjectedKey, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>
/// How one feed entry terminated, and the exact evidence R3 requires the receipt to retain.
/// </summary>
/// <remarks>
/// This type carries no body, no payload and no capability, and nothing on it can widen the pack:
/// the sets it holds are strings and its only door is the classifier. That is the structural half
/// of R3's "no feed terminal supplies a body or capability" and R7's "cannot add a seed, root,
/// closure row, body, or capability".
/// </remarks>
public sealed class EuFeedEntryTermination
{
    private EuFeedEntryTermination(
        EuFeedTerminal terminal,
        EuFeedEntryObservation observation,
        IReadOnlyList<string> inPack,
        IReadOnlyList<string> outOfPack,
        IReadOnlyList<EuFeedFamilyProjection> inPackProjections,
        IReadOnlyList<EuFeedFamilyProjection> outOfPackProjections,
        EuFeedUnresolvedCause unresolvedCause,
        EuFeedOutOfPackReason outOfPackReason)
    {
        Terminal = terminal;
        Observation = observation;
        InPack = inPack;
        OutOfPack = outOfPack;
        InPackProjections = inPackProjections;
        OutOfPackProjections = outOfPackProjections;
        UnresolvedCause = unresolvedCause;
        OutOfPackReason = outOfPackReason;
    }

    /// <summary>The one terminal this entry reached.</summary>
    public EuFeedTerminal Terminal { get; }

    /// <summary>The observation this terminal was computed from, retained whole.</summary>
    public EuFeedEntryObservation Observation { get; }

    /// <summary>The entry's tie-safe position.</summary>
    public EuWatermarkCursor Entry => Observation.Entry;

    /// <summary><c>I = W intersect P</c>, sorted. Empty for an unresolved terminal.</summary>
    public IReadOnlyList<string> InPack { get; }

    /// <summary><c>O = W minus P</c>, sorted. Empty for an unresolved terminal.</summary>
    public IReadOnlyList<string> OutOfPack { get; }

    /// <summary>The projections whose source Work root is in <see cref="InPack"/>.</summary>
    public IReadOnlyList<EuFeedFamilyProjection> InPackProjections { get; }

    /// <summary>The projections whose source Work root is in <see cref="OutOfPack"/>.</summary>
    public IReadOnlyList<EuFeedFamilyProjection> OutOfPackProjections { get; }

    /// <summary>Which named cause sent this entry to the unresolved terminal, when one did.</summary>
    public EuFeedUnresolvedCause UnresolvedCause { get; }

    /// <summary>The typed reason R3 requires beside an out-of-pack component.</summary>
    public EuFeedOutOfPackReason OutOfPackReason { get; }

    internal static EuFeedEntryTermination Unresolved(
        EuFeedEntryObservation observation,
        EuFeedUnresolvedCause cause) =>
        new(
            EuFeedTerminal.UnresolvedOrAmbiguous,
            observation,
            Array.AsReadOnly(Array.Empty<string>()),
            Array.AsReadOnly(Array.Empty<string>()),
            Array.AsReadOnly(Array.Empty<EuFeedFamilyProjection>()),
            Array.AsReadOnly(Array.Empty<EuFeedFamilyProjection>()),
            cause,
            EuFeedOutOfPackReason.None);

    internal static EuFeedEntryTermination Resolved(
        EuFeedTerminal terminal,
        EuFeedEntryObservation observation,
        string[] inPack,
        string[] outOfPack,
        EuFeedFamilyProjection[] inPackProjections,
        EuFeedFamilyProjection[] outOfPackProjections) =>
        new(
            terminal,
            observation,
            Array.AsReadOnly(inPack),
            Array.AsReadOnly(outOfPack),
            Array.AsReadOnly(inPackProjections),
            Array.AsReadOnly(outOfPackProjections),
            EuFeedUnresolvedCause.None,
            outOfPack.Length > 0
                ? EuFeedOutOfPackReason.NotAMemberOfTheDiscoveredPackRootSet
                : EuFeedOutOfPackReason.None);
}

/// <summary>
/// <c>eu_feed_root_intersection/1</c>: the binding a cut freezes, and the total algorithm that
/// gives every feed entry exactly one of R3's four terminals.
/// </summary>
/// <remarks>
/// <para>
/// R3 line 411 and R7 line 739. <c>P</c> is the public Work-root set represented by discovered
/// closure from the exact 82 roots; for a completely resolved entry, <c>I = W intersect P</c> and
/// <c>O = W minus P</c>, and the entry terminates exactly once on which of the two is empty.
/// </para>
/// <para>
/// What this contract does not do, and why. It does not resolve a feed entry to its Work roots: no
/// observation establishes the publisher identity predicates that would, so the resolution is a
/// declared input whose identity is bound by digest. It does not derive <c>P</c>: closure over the
/// 82 seeds is the query plan's work, and no observation of it exists either, so <c>P</c> and the
/// discovered family index are supplied and their artifacts bound. It classifies. That is the half
/// that needs no observation, and it is the half that is here.
/// </para>
/// <para>
/// It also cannot widen the pack, structurally rather than by convention: <c>P</c> is fixed at
/// binding, the only door onto a binding is <see cref="TryBind"/>, and nothing a terminal produces
/// is accepted back as a root, a seed, a closure row or a family. An out-of-pack positive is
/// evidence about a locator and nothing else.
/// </para>
/// </remarks>
public sealed class EuFeedRootIntersection
{
    /// <summary>The schema this binding is an instance of.</summary>
    public const string SchemaId = "eu_feed_root_intersection/1";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly HashSet<string> _packRoots;
    private readonly HashSet<EuFeedFamilyProjection> _discoveredFamilyRows;

    private EuFeedRootIntersection(
        SourceArtifactRef seedMapRef,
        SourceArtifactRef closureMatrixRef,
        SourceArtifactRef identityPredicateBindingRef,
        IReadOnlyList<string> discoveredPackRoots,
        IReadOnlyList<EuFeedFamilyProjection> discoveredFamilyRows,
        HashSet<string> packRoots,
        HashSet<EuFeedFamilyProjection> familyIndex,
        string bindingIdentityDigest)
    {
        SeedMapRef = seedMapRef;
        ClosureMatrixRef = closureMatrixRef;
        IdentityPredicateBindingRef = identityPredicateBindingRef;
        DiscoveredPackRoots = discoveredPackRoots;
        DiscoveredFamilyRows = discoveredFamilyRows;
        _packRoots = packRoots;
        _discoveredFamilyRows = familyIndex;
        BindingIdentityDigest = bindingIdentityDigest;
    }

    /// <summary>The exact 82-seed map this pack was resolved from, bound by digest (R7).</summary>
    public SourceArtifactRef SeedMapRef { get; }

    /// <summary>The closure matrix the discovered pack came from, bound by digest (R3, R7).</summary>
    public SourceArtifactRef ClosureMatrixRef { get; }

    /// <summary>
    /// The publisher identity predicates and canonical projections R3 requires the witness to bind,
    /// held as an artifact reference rather than as content. This contract does not know which
    /// predicates they are and does not pretend to; it requires the binding to exist and to be the
    /// same one the resolution used.
    /// </summary>
    public SourceArtifactRef IdentityPredicateBindingRef { get; }

    /// <summary><c>P</c>, sorted ordinally.</summary>
    public IReadOnlyList<string> DiscoveredPackRoots { get; }

    /// <summary>The cut's discovered family rows, sorted ordinally.</summary>
    public IReadOnlyList<EuFeedFamilyProjection> DiscoveredFamilyRows { get; }

    /// <summary>
    /// SHA-256 over the whole binding: schema, the three bound artifact references, the sorted pack
    /// and the sorted family index. Two cuts that report the same digest classified against the
    /// same pack.
    /// </summary>
    public string BindingIdentityDigest { get; }

    /// <summary>The only path that mints a binding.</summary>
    /// <param name="seedMapRef">The 82-seed map artifact.</param>
    /// <param name="closureMatrixRef">The closure matrix artifact.</param>
    /// <param name="identityPredicateBindingRef">The identity predicate and projection binding.</param>
    /// <param name="discoveredPackRoots"><c>P</c>.</param>
    /// <param name="discoveredFamilyRows">The cut's discovered family index.</param>
    /// <param name="refusal">Why no binding exists, when none does.</param>
    public static EuFeedRootIntersection? TryBind(
        SourceArtifactRef seedMapRef,
        SourceArtifactRef closureMatrixRef,
        SourceArtifactRef identityPredicateBindingRef,
        IReadOnlyList<string> discoveredPackRoots,
        IReadOnlyList<EuFeedFamilyProjection> discoveredFamilyRows,
        out EuFeedIntersectionRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(seedMapRef);
        ArgumentNullException.ThrowIfNull(closureMatrixRef);
        ArgumentNullException.ThrowIfNull(identityPredicateBindingRef);
        ArgumentNullException.ThrowIfNull(discoveredPackRoots);
        ArgumentNullException.ThrowIfNull(discoveredFamilyRows);

        var roots = discoveredPackRoots.ToArray();
        var rows = discoveredFamilyRows.ToArray();
        if (Array.Exists(rows, static row => row is null))
        {
            throw new ArgumentException(
                "A discovered family row cannot be null.", nameof(discoveredFamilyRows));
        }

        if (roots.Length == 0)
        {
            refusal = EuFeedIntersectionRefusal.PackRootSetEmpty;
            return null;
        }

        if (Array.Exists(roots, string.IsNullOrWhiteSpace))
        {
            refusal = EuFeedIntersectionRefusal.PackRootBlank;
            return null;
        }

        // Appendix A's exact lexical form is the only one that reaches the pack-membership
        // comparisons below and in Classify. This also means two spellings of the same seed (one
        // http, one https) now collide at the repeat check below instead of silently doubling P.
        for (var i = 0; i < roots.Length; i++)
        {
            // TryCanonicalize's own refusal reason is discarded here: PackRootNotCanonical is the
            // one member this binding's closed refusal vocabulary has for "could not be reduced to
            // Appendix A's exact lexical form" (it covers this loop and the family-row loop below
            // alike), and a binding refusal only needs to say that, not which of TryCanonicalize's
            // own reasons produced it.
            var canonicalRoot = EuPackRootCanonicalForm.TryCanonicalize(roots[i], out _);
            if (canonicalRoot is null)
            {
                refusal = EuFeedIntersectionRefusal.PackRootNotCanonical;
                return null;
            }

            roots[i] = canonicalRoot;
        }

        for (var i = 0; i < rows.Length; i++)
        {
            // Same discard as the pack-root loop above: PackRootNotCanonical does not distinguish
            // which of TryCanonicalize's reasons fired.
            var canonicalSource = EuPackRootCanonicalForm.TryCanonicalize(
                rows[i].SourceWorkRoot, out _);
            if (canonicalSource is null)
            {
                refusal = EuFeedIntersectionRefusal.PackRootNotCanonical;
                return null;
            }

            rows[i] = rows[i] with { SourceWorkRoot = canonicalSource };
        }

        var packRoots = new HashSet<string>(roots, StringComparer.Ordinal);
        if (packRoots.Count != roots.Length)
        {
            refusal = EuFeedIntersectionRefusal.PackRootRepeated;
            return null;
        }

        if (Array.Exists(rows, row => !packRoots.Contains(row.SourceWorkRoot)))
        {
            refusal = EuFeedIntersectionRefusal.DiscoveredFamilyRowOutsideThePack;
            return null;
        }

        var sortedRoots = EuFeedEntryObservation.Sorted(roots);
        var sortedRows = EuFeedEntryObservation.Sorted(rows);
        refusal = EuFeedIntersectionRefusal.None;
        return new EuFeedRootIntersection(
            seedMapRef,
            closureMatrixRef,
            identityPredicateBindingRef,
            Array.AsReadOnly(sortedRoots),
            Array.AsReadOnly(sortedRows),
            packRoots,
            new HashSet<EuFeedFamilyProjection>(sortedRows),
            BindingDigest(
                seedMapRef,
                closureMatrixRef,
                identityPredicateBindingRef,
                sortedRoots,
                sortedRows));
    }

    /// <summary>True when a Work root is a member of the discovered pack.</summary>
    /// <remarks>
    /// The argument is canonicalized before the comparison (see
    /// <see cref="EuPackRootCanonicalForm"/>), so an http and an https spelling of the same Cellar
    /// origin answer alike here too, not only through <see cref="Classify"/>. A root that cannot
    /// be canonicalized is not a member: nothing this method returns claims to have identified it.
    /// </remarks>
    public bool PackContains(string workRoot)
    {
        ArgumentNullException.ThrowIfNull(workRoot);
        // TryCanonicalize's refusal reason is discarded here, deliberately and unlike the two
        // TryX doors above: this method's contract is a plain membership bool, which has nowhere
        // to carry a refusal, and every reason TryCanonicalize can report answers "not a member"
        // alike (see the remarks above).
        var canonical = EuPackRootCanonicalForm.TryCanonicalize(workRoot, out _);
        return canonical is not null && _packRoots.Contains(canonical);
    }

    /// <summary>True when a projection occurs in the cut's discovered family index.</summary>
    /// <remarks>
    /// The projection's source Work root is canonicalized before the comparison, on the same
    /// terms as <see cref="PackContains"/>, so the family index - itself built from canonicalized
    /// rows in <see cref="TryBind"/> - is not searched for a spelling it never stores.
    /// </remarks>
    public bool DiscoveredFamilyContains(EuFeedFamilyProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        // Same discard as PackContains: a plain membership bool has nowhere to carry a refusal
        // reason, and every reason TryCanonicalize can report answers "not a member" alike.
        var canonicalSource = EuPackRootCanonicalForm.TryCanonicalize(
            projection.SourceWorkRoot, out _);
        return canonicalSource is not null &&
            _discoveredFamilyRows.Contains(projection with { SourceWorkRoot = canonicalSource });
    }

    /// <summary>
    /// The total algorithm. Every entry leaves with exactly one terminal; there is no refusal and
    /// no way to decline, because R3 requires the four terminal counts to sum to the canonical
    /// entry count and an entry that produced nothing would break that equation silently.
    /// </summary>
    /// <remarks>
    /// The order of the four unresolved causes is a precedence, and it runs outward from what is
    /// least trustworthy. A declared failure to resolve identity is checked first because nothing
    /// after it means anything. Watermark membership next, because an entry the traversal did not
    /// deliver did not arrive through this witness whatever it resolves to. Then projections that
    /// name a root outside the entry's own set, which cannot be attributed to either side. Then the
    /// partition, whose only reachable failure is R3's empty-both case.
    /// </remarks>
    /// <param name="observation">The entry and its bound resolution.</param>
    /// <param name="entries">The cut's canonical entry set, for the membership check.</param>
    public EuFeedEntryTermination Classify(
        EuFeedEntryObservation observation,
        EuFeedWatermarkEntrySet entries)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(entries);

        if (!observation.IdentityResolutionClosed)
        {
            return EuFeedEntryTermination.Unresolved(
                observation, EuFeedUnresolvedCause.IdentityResolutionDidNotClose);
        }

        if (!entries.Contains(observation.Entry))
        {
            return EuFeedEntryTermination.Unresolved(
                observation, EuFeedUnresolvedCause.WatermarkMembershipDidNotClose);
        }

        var resolved = new HashSet<string>(observation.ResolvedWorkRoots, StringComparer.Ordinal);
        foreach (var projection in observation.Projections)
        {
            if (!resolved.Contains(projection.SourceWorkRoot))
            {
                return EuFeedEntryTermination.Unresolved(
                    observation, EuFeedUnresolvedCause.FamilyProjectionDidNotClose);
            }
        }

        var inPack = observation.ResolvedWorkRoots.Where(_packRoots.Contains).ToArray();
        var outOfPack = observation.ResolvedWorkRoots.Where(root => !_packRoots.Contains(root))
            .ToArray();

        if (inPack.Length == 0 && outOfPack.Length == 0)
        {
            return EuFeedEntryTermination.Unresolved(
                observation, EuFeedUnresolvedCause.PartitionDidNotClose);
        }

        var terminal = outOfPack.Length == 0
            ? EuFeedTerminal.InPack
            : inPack.Length == 0
                ? EuFeedTerminal.OutOfPack
                : EuFeedTerminal.MixedScope;

        return EuFeedEntryTermination.Resolved(
            terminal,
            observation,
            inPack,
            outOfPack,
            observation.Projections.Where(row => _packRoots.Contains(row.SourceWorkRoot)).ToArray(),
            observation.Projections.Where(row => !_packRoots.Contains(row.SourceWorkRoot))
                .ToArray());
    }

    private static string BindingDigest(
        SourceArtifactRef seedMapRef,
        SourceArtifactRef closureMatrixRef,
        SourceArtifactRef identityPredicateBindingRef,
        IReadOnlyList<string> sortedRoots,
        IReadOnlyList<EuFeedFamilyProjection> sortedRows)
    {
        var lines = new List<string>
        {
            SchemaId,
            "seed_map=" + seedMapRef.ResourceId + "@" + seedMapRef.Sha256,
            "closure_matrix=" + closureMatrixRef.ResourceId + "@" + closureMatrixRef.Sha256,
            "identity_binding=" + identityPredicateBindingRef.ResourceId + "@"
                + identityPredicateBindingRef.Sha256,
        };
        lines.AddRange(sortedRoots.Select(static root => "pack_root=" + root));
        lines.AddRange(sortedRows.Select(static row =>
            "family_row=" + row.SourceWorkRoot + "|" + row.FamilyMemberKey + "|"
            + row.ProjectedKey));
        return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(string.Join('\n', lines))))
            .ToLowerInvariant();
    }
}

/// <summary>
/// R3's terminal equation and its orthogonal conflict counts, over one cut.
/// </summary>
/// <remarks>
/// <para>
/// The equation is that the four terminal counts sum to the canonical entry count. It is checked
/// against a canonical count the watermark traversal produced, not against the number of
/// terminations, because the second comparison is the same number twice and can never fail.
/// </para>
/// <para>
/// The conflict counts are orthogonal derived projections and are never added to the terminal
/// equation, exactly as R3 says. A nonzero conflict count makes the cut incomplete without changing
/// any terminal, so an out-of-pack positive stays a clean terminal while a projection missing from
/// its discovered family stops the cut.
/// </para>
/// <para>
/// What this cannot see: R3 also makes the EU cut incomplete on feed unavailability, partial feed
/// transfer, watermark ambiguity and parser drift. Those are transport and traversal conditions and
/// belong to the observation half of the witness, not to this reconciliation.
/// </para>
/// </remarks>
public sealed class EuFeedTerminalReconciliation
{
    private EuFeedTerminalReconciliation(
        IReadOnlyDictionary<EuFeedTerminal, int> terminalCounts,
        IReadOnlyDictionary<EuFeedReconciliationConflict, int> conflictCounts,
        int canonicalEntryCount)
    {
        TerminalCounts = terminalCounts;
        ConflictCounts = conflictCounts;
        CanonicalEntryCount = canonicalEntryCount;
    }

    /// <summary>Every terminal, including the ones no entry reached.</summary>
    public IReadOnlyDictionary<EuFeedTerminal, int> TerminalCounts { get; }

    /// <summary>Every conflict subtype, including the ones with no occurrence.</summary>
    public IReadOnlyDictionary<EuFeedReconciliationConflict, int> ConflictCounts { get; }

    /// <summary>The canonical entry count, from the watermark traversal.</summary>
    public int CanonicalEntryCount { get; }

    /// <summary>The sum of the four terminal counts.</summary>
    public int TerminalCountSum => TerminalCounts.Values.Sum();

    /// <summary>Whether R3's terminal equation holds for this cut.</summary>
    public bool TerminalEquationHolds => TerminalCountSum == CanonicalEntryCount;

    /// <summary>The total conflict count, never part of the terminal equation.</summary>
    public int ConflictTotal => ConflictCounts.Values.Sum();

    /// <summary>
    /// Whether this reconciliation makes the EU cut incomplete. Any conflict does, and so does a
    /// broken terminal equation.
    /// </summary>
    public bool MakesTheCutIncomplete => ConflictTotal > 0 || !TerminalEquationHolds;

    /// <summary>The only path that mints a reconciliation.</summary>
    /// <param name="binding">The frozen binding, for its discovered family index.</param>
    /// <param name="entries">The cut's canonical entry set.</param>
    /// <param name="terminations">Every termination the classifier produced for this cut.</param>
    public static EuFeedTerminalReconciliation Of(
        EuFeedRootIntersection binding,
        EuFeedWatermarkEntrySet entries,
        IReadOnlyList<EuFeedEntryTermination> terminations)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(terminations);

        var rows = terminations.ToArray();
        if (Array.Exists(rows, static row => row is null))
        {
            throw new ArgumentException(
                "A termination cannot be null.", nameof(terminations));
        }

        var terminalCounts = new Dictionary<EuFeedTerminal, int>();
        foreach (var terminal in Enum.GetValues<EuFeedTerminal>())
        {
            terminalCounts[terminal] = 0;
        }

        var conflictCounts = new Dictionary<EuFeedReconciliationConflict, int>();
        foreach (var conflict in Enum.GetValues<EuFeedReconciliationConflict>())
        {
            conflictCounts[conflict] = 0;
        }

        var terminated = new HashSet<EuWatermarkCursor>(EuWatermarkCursorIdentity.Instance);
        foreach (var row in rows)
        {
            terminalCounts[row.Terminal]++;

            if (!terminated.Add(row.Entry))
            {
                conflictCounts[EuFeedReconciliationConflict.DuplicateTerminalAccounting]++;
            }

            if (!entries.Contains(row.Entry))
            {
                conflictCounts[
                    EuFeedReconciliationConflict.TerminalOutsideTheCanonicalEntrySet]++;
            }

            if (row.Terminal == EuFeedTerminal.UnresolvedOrAmbiguous)
            {
                conflictCounts[
                    EuFeedReconciliationConflict.UnresolvedOrAmbiguousTerminal]++;
            }

            // Only the in-pack side reconciles. R3 requires every in-pack projection of an in-pack
            // or mixed terminal to occur in its discovered family, and requires the out-of-pack
            // side of the same entry to terminate as positive-only evidence instead.
            if (row.Terminal is EuFeedTerminal.InPack or EuFeedTerminal.MixedScope)
            {
                foreach (var projection in row.InPackProjections)
                {
                    if (!binding.DiscoveredFamilyContains(projection))
                    {
                        conflictCounts[
                            EuFeedReconciliationConflict
                                .ProjectionMissingFromItsDiscoveredFamily]++;
                    }
                }
            }
        }

        foreach (var entry in entries.CanonicalEntries)
        {
            if (!terminated.Contains(entry))
            {
                conflictCounts[EuFeedReconciliationConflict.EntryWithoutATerminal]++;
            }
        }

        return new EuFeedTerminalReconciliation(terminalCounts, conflictCounts, entries.Count);
    }
}
