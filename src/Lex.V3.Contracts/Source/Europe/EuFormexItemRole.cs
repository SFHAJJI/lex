using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The role a Formex item plays inside one Cellar manifestation. Closed.
/// </summary>
public enum EuFormexItemRole
{
    /// <summary>The consolidated legal text itself.</summary>
    [JsonStringEnumMemberName("main_text")]
    MainText = 1,

    /// <summary>The paired metadata descriptor. Never a body.</summary>
    [JsonStringEnumMemberName("descriptor")]
    Descriptor = 2,
}

/// <summary>
/// Why a Formex item set carries no admitted role. Closed, and every member is a refusal rather
/// than a fallback: an unclassified item may not be fetched as legal text.
/// </summary>
public enum EuFormexRoleRefusal
{
    /// <summary>
    /// No refusal: the value was admitted. Present so the out parameter is a true statement on
    /// the success path rather than whichever member happened to be assigned first.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The stream name matches no admitted grammar.</summary>
    [JsonStringEnumMemberName("unrecognised_stream_name")]
    UnrecognisedStreamName = 1,

    /// <summary>
    /// The stream name is an original act as published, not a consolidation. Refused by scope
    /// rather than by defect: point-in-time law is served from consolidations.
    /// </summary>
    [JsonStringEnumMemberName("original_act_naming")]
    OriginalActNaming = 2,

    /// <summary>Items in one set disagree about the expression language.</summary>
    [JsonStringEnumMemberName("language_disagreement")]
    LanguageDisagreement = 3,

    /// <summary>Items in one set derive different work identities.</summary>
    [JsonStringEnumMemberName("work_disagreement")]
    WorkDisagreement = 4,

    /// <summary>Two items claim the same role.</summary>
    [JsonStringEnumMemberName("duplicate_role")]
    DuplicateRole = 5,

    /// <summary>The set carries no main text, so there is no body to acquire.</summary>
    [JsonStringEnumMemberName("main_text_absent")]
    MainTextAbsent = 6,

    /// <summary>
    /// The stream name disagrees with the work identity the caller opened it against.
    /// </summary>
    [JsonStringEnumMemberName("work_identity_disagreement")]
    WorkIdentityDisagreement = 7,

    /// <summary>
    /// Items in one set carry different package stems, so they are states of different
    /// consolidations rather than one manifestation.
    /// </summary>
    [JsonStringEnumMemberName("stem_disagreement")]
    StemDisagreement = 8,

    /// <summary>
    /// The expected identity is not a base act CELEX. A dated consolidation form such as
    /// <c>02016R0679-20160504</c> or a corrigendum suffix such as <c>32016R0679R(02)</c> names a
    /// state of a work rather than the work, and the Cellar notice puts those beside each other.
    /// </summary>
    [JsonStringEnumMemberName("expected_work_not_a_base_act")]
    ExpectedWorkNotABaseAct = 9,
}

/// <summary>
/// A Formex stream name parsed against the admitted consolidation grammar.
/// </summary>
/// <remarks>
/// <para>
/// Grammar, read off live publisher data on 2026-09-02 rather than off a fixture:
/// <c>CL2016R0679EN0000020.0001.xml</c> is <c>CL</c>, a four digit year, a document type letter, a
/// four digit document number, a two letter language, a seven digit production sequence, a four
/// digit increment, and an extension. The paired <c>.doc.xml</c> is its descriptor.
/// </para>
/// <para>
/// Original acts as published use a different naming, <c>L_2016119EN.01000101.xml</c>, and are
/// refused as <see cref="EuFormexRoleRefusal.OriginalActNaming"/>. That is scope, not a gap.
/// </para>
/// </remarks>
// A class rather than a record on purpose: a record emits a clone method and a copy
// constructor, which are a second construction path on a type whose whole point is that only
// the checked parser can mint one. The structural test below enumerates that surface.
public sealed class EuFormexStreamName
{
    // CL + year + type + number + language + sequence . increment + extension. Anchored and
    // total: an unknown vocabulary fails closed rather than matching a permissive prefix.
    private static readonly System.Text.RegularExpressions.Regex Consolidated = new(
        @"^CL(?<year>\d{4})(?<type>[A-Z])(?<number>\d{4})(?<language>[A-Z]{2})(?<sequence>\d{7})\.(?<increment>\d{4})(?<extension>\.doc\.xml|\.xml)$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
            | System.Text.RegularExpressions.RegexOptions.ExplicitCapture);

    private static readonly System.Text.RegularExpressions.Regex OriginalAct = new(
        @"^L_\d+[A-Z]{2}\.\d+(\.doc)?\.xml$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A base act CELEX in full: sector digit, year, type letter, number, optional parenthetical.
    /// Anchored at both ends on purpose. Matching only the nine characters a stream name shares
    /// would admit a dated consolidation form or a corrigendum suffix as the work, and those are
    /// exactly the neighbouring identities a Cellar notice states side by side.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex CelexCore = new(
        @"^\d(?<year>\d{4})(?<type>[A-Z])(?<number>\d{4})(\(\d{2}\))?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
            | System.Text.RegularExpressions.RegexOptions.ExplicitCapture);

    private EuFormexStreamName(
        string value,
        string stem,
        string workCelex,
        string language,
        EuFormexItemRole role)
    {
        Value = value;
        Stem = stem;
        WorkCelex = workCelex;
        Language = language;
        Role = role;
    }

    /// <summary>
    /// Everything before the extension, for example <c>CL2016R0679EN0000020.0001</c>. Two items
    /// belong to one consolidation package only if their stems agree: the production sequence and
    /// increment are what distinguish one consolidation state from another.
    /// </summary>
    public string Stem { get; }

    /// <summary>The exact publisher stream name, unmodified.</summary>
    public string Value { get; }

    /// <summary>
    /// The work identity this name was opened against and agrees with, verbatim as the caller
    /// supplied it.
    /// </summary>
    /// <remarks>
    /// Verified, never derived. An earlier version of this type built a CELEX by prefixing sector
    /// 3, on the reasoning that consolidated legislation is sector 3. The publisher falsifies
    /// that: <c>CL1998A0403EN0010010.0001.xml</c> is a real consolidated package whose base act is
    /// <c>21998A0403(01)</c>, an international agreement in sector 2. The derivation produced
    /// <c>31998A0403</c>, which names a different sector and silently drops the parenthetical the
    /// grammar cannot carry. A stream name is evidence about an identity the Cellar notice already
    /// states; it is not authority to mint one.
    /// </remarks>
    public string WorkCelex { get; }

    /// <summary>The two letter expression language carried by the name.</summary>
    public string Language { get; }

    /// <summary>
    /// The role, decided by the extension alone.
    /// </summary>
    /// <remarks>
    /// Deliberately not decided by stream order or by the <c>DOC_n</c> segment of the item URL.
    /// Measured on 2026-09-02: in the original act listings the descriptor is served first, and in
    /// the consolidated listing the main text is served first, so the ordering genuinely inverts
    /// between listings. A producer keyed on order would have fetched kilobyte descriptors in
    /// place of legal texts for part of the corpus, and would have done so silently, because a
    /// descriptor is a well formed XML document that parses.
    /// </remarks>
    public EuFormexItemRole Role { get; }

    /// <summary>
    /// The only path that mints a stream name. Returns null with a typed refusal rather than
    /// throwing, because an unclassifiable item is an expected publisher outcome to record, not a
    /// programming error.
    /// </summary>
    public static EuFormexStreamName? TryParse(
        string value,
        string expectedWorkCelex,
        out EuFormexRoleRefusal refusal)
    {
        refusal = EuFormexRoleRefusal.UnrecognisedStreamName;

        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(expectedWorkCelex))
        {
            return null;
        }

        var match = Consolidated.Match(value);
        if (!match.Success)
        {
            refusal = OriginalAct.IsMatch(value)
                ? EuFormexRoleRefusal.OriginalActNaming
                : EuFormexRoleRefusal.UnrecognisedStreamName;
            return null;
        }

        // The name must agree with the identity the caller already holds. Year, type and number
        // are what the two share; the sector and any parenthetical live only in the CELEX, which
        // is why the CELEX is carried through rather than rebuilt from the name.
        var expected = CelexCore.Match(expectedWorkCelex);
        if (!expected.Success)
        {
            refusal = EuFormexRoleRefusal.ExpectedWorkNotABaseAct;
            return null;
        }

        if (expected.Groups["year"].Value != match.Groups["year"].Value
            || expected.Groups["type"].Value != match.Groups["type"].Value
            || expected.Groups["number"].Value != match.Groups["number"].Value)
        {
            refusal = EuFormexRoleRefusal.WorkIdentityDisagreement;
            return null;
        }

        var role = match.Groups["extension"].Value == ".doc.xml"
            ? EuFormexItemRole.Descriptor
            : EuFormexItemRole.MainText;

        refusal = EuFormexRoleRefusal.None;
        return new EuFormexStreamName(
            value,
            value[..^match.Groups["extension"].Value.Length],
            expectedWorkCelex,
            match.Groups["language"].Value,
            role);
    }
}

/// <summary>
/// One classified Formex item: the parsed stream name, and the publisher facts observed beside it.
/// </summary>
/// <remarks>
/// <see cref="StreamOrder"/> is retained because it is what the publisher said, and dropping an
/// observed fact would make the evidence less complete than the reading. It is evidence only. No
/// member of this type derives a role from it, and <see cref="EuFormexItemSet"/> refuses to accept
/// a role that was not minted by <see cref="EuFormexStreamName.TryParse"/>.
/// </remarks>
public sealed class EuFormexItem
{
    public EuFormexItem(
        EuWemiIdentityBoundary boundary,
        EuFormexStreamName streamName,
        SourceObjectRef itemRef,
        long streamOrder)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        StreamName = streamName ?? throw new ArgumentNullException(nameof(streamName));

        // Admitted as an exact Cellar Item registry role, not accepted as a reference that merely
        // looks like one. A bare member key must never authorize a role.
        ItemRef = boundary.Require(itemRef, EuWemiRole.Item, nameof(itemRef));

        if (streamOrder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamOrder), "A publisher stream order is never negative.");
        }

        StreamOrder = streamOrder;
    }

    public EuFormexStreamName StreamName { get; }

    /// <summary>The Cellar item this classification was read from.</summary>
    public SourceObjectRef ItemRef { get; }

    /// <summary>The order the publisher listed this item in. Evidence, never a role signal.</summary>
    public long StreamOrder { get; }

    public EuFormexItemRole Role => StreamName.Role;
}

/// <summary>
/// The complete, classified Formex item set for one manifestation.
/// </summary>
/// <remarks>
/// Every required item is classified independently, and an unknown, missing, duplicate or
/// conflicting role blocks derivation rather than degrading to a best guess. A set that cannot be
/// admitted yields a typed refusal, so a later reader can tell which of the six conditions stopped
/// it without re-deriving anything.
/// </remarks>
public sealed class EuFormexItemSet
{
    private EuFormexItemSet(
        IReadOnlyList<EuFormexItem> items,
        EuFormexItem mainText,
        EuFormexItem? descriptor,
        string workCelex,
        string language)
    {
        Items = items;
        MainText = mainText;
        Descriptor = descriptor;
        WorkCelex = workCelex;
        Language = language;
    }

    public IReadOnlyList<EuFormexItem> Items { get; }

    /// <summary>The item carrying legal text. Present by construction.</summary>
    public EuFormexItem MainText { get; }

    /// <summary>The paired descriptor when the publisher served one.</summary>
    public EuFormexItem? Descriptor { get; }

    public string WorkCelex { get; }

    public string Language { get; }

    /// <summary>
    /// The only path that mints an admitted set.
    /// </summary>
    public static EuFormexItemSet? TryAdmit(
        IReadOnlyList<EuFormexItem> items,
        out EuFormexRoleRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(items);
        refusal = default;

        if (items.Count == 0)
        {
            refusal = EuFormexRoleRefusal.MainTextAbsent;
            return null;
        }

        var languages = items.Select(item => item.StreamName.Language).Distinct(StringComparer.Ordinal).ToArray();
        if (languages.Length != 1)
        {
            refusal = EuFormexRoleRefusal.LanguageDisagreement;
            return null;
        }

        var works = items.Select(item => item.StreamName.WorkCelex).Distinct(StringComparer.Ordinal).ToArray();
        if (works.Length != 1)
        {
            refusal = EuFormexRoleRefusal.WorkDisagreement;
            return null;
        }

        // Same work and same language still admits two different consolidation states. The stem
        // carries the production sequence and increment, which is what actually distinguishes one
        // package from another, so a main text and a descriptor from different states would
        // otherwise pair as one manifestation.
        var stems = items.Select(item => item.StreamName.Stem).Distinct(StringComparer.Ordinal).ToArray();
        if (stems.Length != 1)
        {
            refusal = EuFormexRoleRefusal.StemDisagreement;
            return null;
        }

        var mains = items.Where(item => item.Role == EuFormexItemRole.MainText).ToArray();
        var descriptors = items.Where(item => item.Role == EuFormexItemRole.Descriptor).ToArray();

        if (mains.Length > 1 || descriptors.Length > 1)
        {
            refusal = EuFormexRoleRefusal.DuplicateRole;
            return null;
        }

        if (mains.Length == 0)
        {
            refusal = EuFormexRoleRefusal.MainTextAbsent;
            return null;
        }

        refusal = EuFormexRoleRefusal.None;
        return new EuFormexItemSet(
            items.ToArray(),
            mains[0],
            descriptors.Length == 1 ? descriptors[0] : null,
            works[0],
            languages[0]);
    }
}
