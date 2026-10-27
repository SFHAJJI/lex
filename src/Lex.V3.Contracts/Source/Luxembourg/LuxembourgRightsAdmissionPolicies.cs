using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The closed registry of Luxembourg rights admission policies: each entry is a canonical text, pinned in code, and the
/// SHA-256 of that text. A derivation names the policy it was made under by that digest, and a corpus header carries it.
/// </summary>
/// <remarks>
/// <para>
/// Policy /1 admits exactly <c>agreed_same_run_cc_by</c>, the two channels of one run agreeing on CC BY 4.0. It is the
/// policy every <c>lex-lu-first-mount-acquisition</c> catalog and every corpus built so far implies: a corpus that names no
/// policy reads as /1. Release 1 of the Luxembourg design re-derives the profiles only, so its re-derivation names /1.
/// </para>
/// <para>
/// Policy A (LU-DATA-DESIGN v2 §1.2, a driver decision recorded in §3.3 item 11) adds <c>agreed_cc_by_rederived</c>: the
/// same two-channel agreement, the in-file channel read by reading /2 from the file's own block for the fetched
/// manifestation, with nothing in the file or the catalogue disagreeing (resolution /2). It admits no single-channel
/// disposition, so it rests on no owner's ruling. Release 2's re-derivation names it.
/// </para>
/// <para>
/// The owner's rulings (§5), each a switch over policy A, every one OFF until the owner answers: L2 admits
/// <c>sparql_cc_by_in_file_names_other_identity</c>, L1 <c>sparql_cc_by_in_file_silent</c>, L1b
/// <c>sparql_cc_by_in_file_own_block_empty</c>, each on the publisher's SPARQL declaration of exactly CC BY 4.0 alone,
/// bound to its retained licence page and disclosed with that page's observation date; with option (a) every page of the
/// acquisition counts, with option (b) only the pages it observed live. Every combination is an entry
/// (<see cref="PolicyFor"/>), so a yes changes the release rule set's policy to one of them and records the owner's words
/// on its ruling line; until then no rule set names one. L3 (PDFs) and L4 (conflicts) have no entry: a PDF yields no text
/// at launch, and a conflict is never admissible.
/// </para>
/// <para>
/// The type refuses to compose an admitting set naming anything but <c>agreed_same_run_cc_by</c>,
/// <c>agreed_cc_by_rederived</c> and the four single-channel dispositions; a single-channel disposition needs an entry that
/// names its admission, its evidence window and a ruling. A digest this registry does not hold names no policy, and every
/// reader of one refuses it.
/// </para>
/// </remarks>
public static class LuxembourgRightsAdmissionPolicies
{
    /// <summary>The first line of policy /1's canonical text: its name.</summary>
    public const string Policy1Name = "lex-lu-rights-admission-policy/1";

    /// <summary>The first line of policy A's canonical text: its name.</summary>
    public const string PolicyAName = "lex-lu-rights-admission-policy/A";

    private const string Policy1Text =
        Policy1Name + "\n" +
        "admits=agreed_same_run_cc_by\n" +
        "single-channel-admission=none\n" +
        "single-channel-evidence-window=none\n" +
        "ruling=none;the-two-channel-agreement-every-lex-lu-first-mount-acquisition-catalog-and-every-corpus-before-it-implies\n";

    private const string PolicyAText =
        PolicyAName + "\n" +
        "admits=agreed_cc_by_rederived,agreed_same_run_cc_by\n" +
        "single-channel-admission=none\n" +
        "single-channel-evidence-window=none\n" +
        "ruling=none;driver-decision-2026-10-27;the-same-two-channel-agreement-as-policy-1:the-file's-own-block-for-the-fetched-manifestation-" +
        "(in-file-reading-2)-and-the-publisher's-sparql-declaration-each-say-exactly-cc-by-4.0-and-nothing-in-the-file-or-the-catalogue-" +
        "disagrees-(resolution-2);LU-DATA-DESIGN-2026-10-14-v2-§1.2-and-§3.3-item-11\n";

    private const string SingleChannelAdmission =
        "single-channel-admission=the-publisher's-sparql-declaration-of-exactly-cc-by-4.0-for-the-fetched-file-alone,bound-to-the-" +
        "retained-licence-page-holding-it-and-disclosed-with-that-page's-observation-date\n";

    private const string WindowEveryPage =
        "single-channel-evidence-window=(a)every-licence-page-of-the-acquisition,a-resumed-run's-replayed-pages-included\n";

    private const string WindowLivePages =
        "single-channel-evidence-window=(b)only-licence-pages-the-acquisition-observed-live,on-or-after-its-resumption\n";

    /// <summary>The owner's three rulings over policy A, in the order an entry's name lists them.</summary>
    private static readonly (string Name, LuxembourgRightsChannelDisposition Admits, string Question)[] Rulings =
    [
        ("L1", LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent,
            "serve-xml-files-that-state-no-licence-at-all-(an-empty-template-or-a-stale-block-without-one)-on-the-same-declaration-with-the-disclosure"),
        ("L1b", LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty,
            "serve-xml-files-whose-own-block-names-the-fetched-file-but-states-no-licence-on-the-same-declaration-with-the-disclosure"),
        ("L2", LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity,
            "serve-xml-files-whose-only-licence-statement-sits-in-a-block-naming-another-identity,every-identity-it-names-itself-cc-by-in-the-same-" +
            "catalogue,on-the-publisher's-per-file-sparql-declaration-of-exactly-cc-by-4.0-alone-with-a-visible-single-channel-disclosure"),
    ];

    /// <summary>The dispositions any policy may admit: the two two-channel agreements and the four single-channel ones.</summary>
    private static readonly LuxembourgRightsChannelDisposition[] Admissible =
    [
        LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
        LuxembourgRightsChannelDisposition.AgreedCcByRederived,
        LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity,
        LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent,
        LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty,
        LuxembourgRightsChannelDisposition.SparqlCcByInFileNotApplicable,
    ];

    private static readonly Dictionary<string, Entry> Entries = BuildEntries();

    /// <summary>The SHA-256 of policy /1's canonical text, lowercase hex.</summary>
    public static string Policy1Sha256 { get; } = Digest(Policy1Text);

    /// <summary>The SHA-256 of policy A's canonical text, lowercase hex.</summary>
    public static string PolicyASha256 { get; } = Digest(PolicyAText);

    /// <summary>Policy /1's canonical text, as UTF-8 bytes (a copy).</summary>
    public static byte[] CopyPolicy1Bytes() => Encoding.UTF8.GetBytes(Policy1Text);

    /// <summary>Policy A's canonical text, as UTF-8 bytes (a copy).</summary>
    public static byte[] CopyPolicyABytes() => Encoding.UTF8.GetBytes(PolicyAText);

    /// <summary>The canonical text of a registered policy, as UTF-8 bytes (a copy); a digest the registry does not hold throws.</summary>
    public static byte[] CopyPolicyBytes(string sha256) => Encoding.UTF8.GetBytes(EntryOf(sha256).Text);

    /// <summary>The digests of every registered policy, sorted ordinally.</summary>
    public static IReadOnlyList<string> RegisteredSha256s { get; } =
        Array.AsReadOnly(Entries.Keys.Order(StringComparer.Ordinal).ToArray());

    /// <summary>Whether <paramref name="sha256"/> is the digest of a registered policy.</summary>
    public static bool IsRegistered(string? sha256) => sha256 is not null && Entries.ContainsKey(sha256);

    /// <summary>
    /// The registered policy over policy A with the owner's rulings <paramref name="l1"/>, <paramref name="l1b"/> and
    /// <paramref name="l2"/> switched on, single-channel evidence limited to live-observed pages when
    /// <paramref name="livePagesOnly"/> (option b): policy A itself when none is on.
    /// </summary>
    public static string PolicyFor(bool l1, bool l1b, bool l2, bool livePagesOnly) =>
        !l1 && !l1b && !l2 ? PolicyASha256 : Digest(SwitchedText(l1, l1b, l2, livePagesOnly));

    /// <summary>
    /// The dispositions the registered policy <paramref name="sha256"/> admits, in the enum's order; null names policy /1,
    /// which a corpus without a re-derivation implies. A digest the registry does not hold throws.
    /// </summary>
    public static IReadOnlyList<LuxembourgRightsChannelDisposition> AdmittingDispositions(string? sha256) =>
        Array.AsReadOnly(EntryOf(sha256).Admits.ToArray());

    /// <summary>Whether the registered policy <paramref name="sha256"/> (null: policy /1) names <paramref name="disposition"/> among those it admits.</summary>
    public static bool Admits(string? sha256, LuxembourgRightsChannelDisposition disposition) =>
        EntryOf(sha256).Admits.Contains(disposition);

    /// <summary>
    /// Whether the registered policy <paramref name="sha256"/> admits a body of <paramref name="disposition"/>: a two-channel
    /// agreement it names; a single-channel disposition it names only with a licence page
    /// (<paramref name="licencePageObservedLive"/> not null), and under option (b) only one the acquisition observed live.
    /// </summary>
    public static bool Admits(string? sha256, LuxembourgRightsChannelDisposition disposition, bool? licencePageObservedLive)
    {
        var entry = EntryOf(sha256);
        if (!entry.Admits.Contains(disposition))
        {
            return false;
        }

        return !LuxembourgRightsChannelResolution.IsSingleChannel(disposition) ||
            licencePageObservedLive is { } live && (live || !entry.LivePagesOnly);
    }

    /// <summary>Whether the registered policy <paramref name="sha256"/> admits a resolution: <see cref="Admits(string?, LuxembourgRightsChannelDisposition, bool?)"/> over its disposition and licence page.</summary>
    public static bool Admits(string? sha256, LuxembourgRightsChannelResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return Admits(sha256, resolution.Disposition, resolution.LicencePage?.ObservedLive);
    }

    /// <summary>Whether the registered policy <paramref name="sha256"/> admits any single-channel disposition (one of the owner's rulings is on).</summary>
    public static bool AdmitsSingleChannel(string? sha256) =>
        EntryOf(sha256).Admits.Any(LuxembourgRightsChannelResolution.IsSingleChannel);

    /// <summary>Whether the registered policy <paramref name="sha256"/> limits single-channel evidence to live-observed licence pages (option b).</summary>
    public static bool LivePagesOnly(string? sha256) => EntryOf(sha256).LivePagesOnly;

    /// <summary>Whether some policy could admit <paramref name="disposition"/>; everything else is withheld under every policy.</summary>
    public static bool IsAdmissible(LuxembourgRightsChannelDisposition disposition) => Admissible.Contains(disposition);

    /// <summary>
    /// The type check every entry passes: a policy's text names exactly its admitting set, sorted, on its <c>admits=</c>
    /// line; the set holds only admissible dispositions; and a single-channel disposition is admitted only by a text that
    /// names a single-channel admission, its evidence window and a ruling. Anything else throws, so such a policy cannot be
    /// built.
    /// </summary>
    internal static LuxembourgRightsChannelDisposition[] Compose(string text, params LuxembourgRightsChannelDisposition[] admits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(admits);
        if (admits.Length == 0 || admits.Distinct().Count() != admits.Length)
        {
            throw new ArgumentException("A policy admits a non-empty set of distinct dispositions.", nameof(admits));
        }

        var refused = admits.Where(static disposition => !Admissible.Contains(disposition)).ToArray();
        if (refused.Length != 0)
        {
            throw new ArgumentException(
                $"A policy can never admit {string.Join(", ", refused.Select(static disposition => ContractWire.NameOf(disposition)))}.",
                nameof(admits));
        }

        var lines = text.Split('\n');
        var admitsLine = "admits=" + string.Join(
            ",", admits.Select(static disposition => ContractWire.NameOf(disposition)).Order(StringComparer.Ordinal));
        if (lines.Length != 6 || !lines[0].StartsWith("lex-lu-rights-admission-policy/", StringComparison.Ordinal) ||
            lines[1] != admitsLine || lines[^1].Length != 0)
        {
            throw new ArgumentException("A policy's text names its admitting set, sorted, on its admits= line.", nameof(text));
        }

        if (admits.Any(LuxembourgRightsChannelResolution.IsSingleChannel) &&
            (lines[2] == "single-channel-admission=none" || lines[3] == "single-channel-evidence-window=none" ||
             lines[4].StartsWith("ruling=none", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "A single-channel admission names the owner's ruling and its evidence window.", nameof(text));
        }

        return admits.Order().ToArray();
    }

    /// <summary>
    /// The canonical text of policy A with rulings switched on: its name lists them and the option
    /// (<c>lex-lu-rights-admission-policy/A+L1+L2(a)</c>); its ruling line states each question as asked and that the owner's
    /// words are not yet given, which a yes replaces with the owner's words, dated.
    /// </summary>
    private static string SwitchedText(bool l1, bool l1b, bool l2, bool livePagesOnly)
    {
        var on = Rulings.Where(ruling => ruling.Name switch { "L1" => l1, "L1b" => l1b, _ => l2 }).ToArray();
        var admits = new[] { LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, LuxembourgRightsChannelDisposition.AgreedCcByRederived }
            .Concat(on.Select(static ruling => ruling.Admits))
            .Select(static disposition => ContractWire.NameOf(disposition))
            .Order(StringComparer.Ordinal);
        return PolicyAName + "+" + string.Join("+", on.Select(static ruling => ruling.Name)) + (livePagesOnly ? "(b)" : "(a)") + "\n" +
            "admits=" + string.Join(",", admits) + "\n" +
            SingleChannelAdmission +
            (livePagesOnly ? WindowLivePages : WindowEveryPage) +
            "ruling=" + string.Join("|", on.Select(ruling =>
                $"{ruling.Name}({(livePagesOnly ? "b" : "a")}):{ruling.Question};asked-of-the-owner-2026-10-27-(LU-DATA-DESIGN-2026-10-14-v2-§5),recommended-(a);" +
                "the-owner's-words-not-yet-given,so-no-release-rule-set-names-this-entry")) + "\n";
    }

    private static Dictionary<string, Entry> BuildEntries()
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            [Digest(Policy1Text)] = new(Policy1Text, Compose(Policy1Text, LuxembourgRightsChannelDisposition.AgreedSameRunCcBy), false),
            [Digest(PolicyAText)] = new(PolicyAText, Compose(
                PolicyAText,
                LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
                LuxembourgRightsChannelDisposition.AgreedCcByRederived), false),
        };
        foreach (var mask in Enumerable.Range(1, 7))
        {
            foreach (var live in new[] { false, true })
            {
                bool l1 = (mask & 1) != 0, l1b = (mask & 2) != 0, l2 = (mask & 4) != 0;
                var text = SwitchedText(l1, l1b, l2, live);
                var admits = new[] { LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, LuxembourgRightsChannelDisposition.AgreedCcByRederived }
                    .Concat(Rulings.Where(ruling => ruling.Name switch { "L1" => l1, "L1b" => l1b, _ => l2 }).Select(static ruling => ruling.Admits))
                    .ToArray();
                entries.Add(Digest(text), new(text, Compose(text, admits), live));
            }
        }

        return entries;
    }

    private static Entry EntryOf(string? sha256) =>
        Entries.TryGetValue(sha256 ?? Digest(Policy1Text), out var entry)
            ? entry
            : throw new ArgumentException($"Rights admission policy {sha256} is not registered.", nameof(sha256));

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record Entry(string Text, LuxembourgRightsChannelDisposition[] Admits, bool LivePagesOnly);
}
