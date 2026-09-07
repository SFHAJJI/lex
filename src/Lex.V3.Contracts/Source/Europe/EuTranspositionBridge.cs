using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Which publisher asserted one side of a transposition bridge. Two sources, never merged.
/// </summary>
/// <remarks>
/// REL-003 requires that Legilux and NIM stay separate columns with separate <c>asserted_by</c>
/// values. The reason is not tidiness: the two publishers answer different questions. Legilux says
/// what Luxembourg enacted; the Commission's NIM records say what a Member State notified as
/// implementing a directive. They agree often and disagree usefully, and a bridge that merged them
/// would publish an agreement neither publisher asserted.
/// </remarks>
public enum EuTranspositionAssertedBy
{
    /// <summary>Asserted by Legilux, the Luxembourg publisher.</summary>
    [JsonStringEnumMemberName("legilux")]
    Legilux = 1,

    /// <summary>Asserted by the Commission's national implementing measures records.</summary>
    [JsonStringEnumMemberName("nim")]
    Nim = 2,
}

/// <summary>
/// What this bridge can say about transposing one EU work, as a typed answer rather than a silence.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NotTransposable"/> exists because a regulation returning nothing and a regulation
/// returning "not transposable" are different claims, and only the second is honest. review/23
/// section 6 gives the reason a regulation is the former case: "GDPR, being a regulation, has no
/// NIM links ... so 'transposition' questions only make sense for directives." A caller reading an
/// empty bridge cannot tell "we did not look" from "this question does not apply", which is the
/// unobserved-versus-observed-absent collapse this vocabulary refuses to make.
/// </para>
/// </remarks>
public enum EuTransposability
{
    /// <summary>
    /// A directive, for which transposition is a question that can be asked. Whether either
    /// publisher has answered it is carried by the sides, not by this member.
    /// </summary>
    [JsonStringEnumMemberName("transposable")]
    Transposable = 1,

    /// <summary>
    /// Not a directive, so transposition does not apply. A typed answer, never an absence.
    /// </summary>
    [JsonStringEnumMemberName("not_transposable")]
    NotTransposable = 2,
}

/// <summary>
/// The Member-State responsibility disclaimer the Commission attaches to its NIM collection.
/// </summary>
/// <remarks>
/// <para>
/// The V3 spec's E5 line requires this disclaimer <b>verbatim</b>, with its archived source. It is
/// not decoration: the NIM records are what a Member State notified, and the Commission states
/// plainly that it does not vouch for them. A bridge that carried a NIM row without the disclaimer
/// would present a Member State's notification with the Union publisher's apparent authority behind
/// it, which is the same class of error as letting a derived view read as a publisher claim.
/// </para>
/// <para>
/// Verbatim means byte-equal. A paraphrase is a different sentence with different legal weight, so
/// substitution is refused as firmly as omission.
/// </para>
/// </remarks>
public static class EuMemberStateDisclaimer
{
    /// <summary>The disclaimer text, exactly as published.</summary>
    public const string Text = "The member states bear sole responsibility for all information";

    /// <summary>The archived source the spec pins for that text.</summary>
    public const string SourceUri =
        "https://web.archive.org/web/20251230180756id_/https://eur-lex.europa.eu/collection/n-law/mne.html";

    /// <summary>Whether a supplied text and source are the pinned pair, byte for byte.</summary>
    public static bool IsExact(string? text, string? sourceUri) =>
        string.Equals(text, Text, StringComparison.Ordinal)
        && string.Equals(sourceUri, SourceUri, StringComparison.Ordinal);
}

/// <summary>
/// One publisher's own side of a transposition bridge, carrying who asserted it.
/// </summary>
/// <remarks>
/// A NIM side carries the Member-State disclaimer and a Legilux side does not. The disclaimer is a
/// statement about who is answerable for the notification, so attaching it to Legilux's own
/// assertion would put the Commission's caveat on Luxembourg's publication.
/// </remarks>
public sealed record EuTranspositionSide
{
    [JsonConstructor]
    public EuTranspositionSide(
        EuTranspositionAssertedBy assertedBy,
        string nationalMeasureUri,
        SourceArtifactRef evidenceRef,
        string? memberStateDisclaimer,
        string? memberStateDisclaimerSourceUri)
    {
        AssertedBy = ContractValidation.RequireDefined(assertedBy, nameof(assertedBy));
        NationalMeasureUri = SourceCoreValidation.RequirePublisherUri(
            nationalMeasureUri, nameof(nationalMeasureUri));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));

        if (assertedBy == EuTranspositionAssertedBy.Nim)
        {
            if (memberStateDisclaimer is null || memberStateDisclaimerSourceUri is null)
            {
                throw new ArgumentException(
                    "a NIM row carries the Member-State responsibility disclaimer and its archived "
                        + "source; without it the row presents a Member State's own notification as "
                        + "though the Union publisher vouched for it.",
                    nameof(memberStateDisclaimer));
            }

            if (!EuMemberStateDisclaimer.IsExact(memberStateDisclaimer, memberStateDisclaimerSourceUri))
            {
                throw new ArgumentException(
                    "the Member-State disclaimer is carried verbatim with its pinned source; a "
                        + "paraphrase is a different sentence with different weight and is refused "
                        + "as firmly as an omission.",
                    nameof(memberStateDisclaimer));
            }
        }
        else if (memberStateDisclaimer is not null || memberStateDisclaimerSourceUri is not null)
        {
            throw new ArgumentException(
                $"a {assertedBy} row carries no Member-State disclaimer; the disclaimer states who "
                    + "is answerable for a NIM notification and does not belong on another "
                    + "publisher's own assertion.",
                nameof(memberStateDisclaimer));
        }

        MemberStateDisclaimer = memberStateDisclaimer;
        MemberStateDisclaimerSourceUri = memberStateDisclaimerSourceUri;
    }

    public EuTranspositionAssertedBy AssertedBy { get; }

    /// <summary>The national measure this publisher named, in that publisher's own spelling.</summary>
    public string NationalMeasureUri { get; }

    public SourceArtifactRef EvidenceRef { get; }

    /// <summary>Present exactly on a NIM row, verbatim.</summary>
    public string? MemberStateDisclaimer { get; }

    /// <summary>The archived source for <see cref="MemberStateDisclaimer"/>.</summary>
    public string? MemberStateDisclaimerSourceUri { get; }
}

/// <summary>
/// One publisher's side together with how far acquiring it actually got.
/// </summary>
/// <remarks>
/// <para>
/// DECISION 64, AND THE REVIEWER WAS RIGHT THAT I HAD MISSED IT. The first version of this contract
/// let a transposable directive carry two null sides and said nothing about why they were null.
/// That shape cannot tell "we never queried Legilux" from "we queried it, the bounded enumeration
/// completed, and it asserts nothing" -- which is precisely the false absence S2-A03 forbids, and
/// precisely the rule I had just verified on the LU relation and assertion vocabularies before
/// failing to apply it here.
/// </para>
/// <para>
/// The state vocabulary is <see cref="EuRelationAcquisitionState"/>, reused rather than redeclared.
/// A third enum saying the same four things would be the divergence I reported against the two LU
/// acquisition-state vocabularies, committed by the person who reported it.
/// </para>
/// <para>
/// <see cref="Side"/> null with <see cref="EuRelationAcquisitionState.Complete"/> is a real negative
/// fact: this publisher asserts no transposition and a completed bounded acquisition says so. Null
/// with any other state is an open question, and the state names which one.
/// </para>
/// </remarks>
public sealed record EuTranspositionSourceAcquisition
{
    [JsonConstructor]
    public EuTranspositionSourceAcquisition(
        EuTranspositionAssertedBy assertedBy,
        EuRelationAcquisitionState acquisition,
        EuTranspositionSide? side,
        SourceArtifactRef? completionEvidenceRef)
    {
        AssertedBy = ContractValidation.RequireDefined(assertedBy, nameof(assertedBy));
        Acquisition = ContractValidation.RequireDefined(acquisition, nameof(acquisition));

        if (acquisition == EuRelationAcquisitionState.Unacquired && side is not null)
        {
            throw new ArgumentException(
                "an unacquired source carries no side; the state says this publisher was never "
                    + "asked, and an observed assertion beside that claim contradicts it. "
                    + "EuCellarRelationFamilyObservation refuses edges in the same state for the "
                    + "same reason.",
                nameof(side));
        }

        if (side is not null && side.AssertedBy != assertedBy)
        {
            throw new ArgumentException(
                $"a {assertedBy} acquisition carries a side asserted by {side.AssertedBy}; a "
                    + "publisher's acquisition holds only that publisher's own assertion.",
                nameof(side));
        }

        if (acquisition == EuRelationAcquisitionState.Complete && completionEvidenceRef is null)
        {
            throw new ArgumentException(
                "a complete acquisition retains its completion evidence; without it the completion "
                    + "is a claim rather than a fact, and only a completed acquisition may support "
                    + "an absence.",
                nameof(completionEvidenceRef));
        }

        if (acquisition != EuRelationAcquisitionState.Complete && completionEvidenceRef is not null)
        {
            throw new ArgumentException(
                $"a {acquisition} acquisition carries completion evidence; completion evidence "
                    + "belongs only to an acquisition that completed.",
                nameof(completionEvidenceRef));
        }

        Side = side;
        CompletionEvidenceRef = completionEvidenceRef;
    }

    public EuTranspositionAssertedBy AssertedBy { get; }

    public EuRelationAcquisitionState Acquisition { get; }

    /// <summary>This publisher's assertion, or null when it asserts none.</summary>
    public EuTranspositionSide? Side { get; }

    /// <summary>Present exactly when <see cref="Acquisition"/> is complete.</summary>
    public SourceArtifactRef? CompletionEvidenceRef { get; }

    /// <summary>
    /// Whether this source's silence is a proven negative rather than an open question.
    /// </summary>
    /// <remarks>
    /// A METHOD, NOT A PROPERTY, and the reviewer had to tell me why. As a computed getter this was
    /// emitted on the wire as <c>proves_absence</c> while the JSON constructor could not bind it, so
    /// a document could carry <c>false</c>, have it silently discarded, and deserialize into an
    /// object computing <c>true</c>. The retained bytes and the contract would then disagree about
    /// the same claim. This repository already answered that question -- see
    /// <c>WireIgnoredMemberTests</c> -- and the answer is that a computed convenience is a method,
    /// absent from the wire entirely.
    /// </remarks>
    public bool ProvesAbsence() =>
        Side is null && Acquisition == EuRelationAcquisitionState.Complete;
}

/// <summary>
/// The normalised ELI used to line the two publishers' spellings up, disclosed as derived.
/// </summary>
/// <remarks>
/// S2-A02: a derived view never becomes a publisher claim. The join is ours — it exists because the
/// two publishers spell the same national measure differently — so it carries no
/// <see cref="EuTranspositionAssertedBy"/> and cannot be constructed as though a publisher had
/// asserted it. <see cref="IsDerived"/> is a computed method rather than a constructor parameter or
/// a property: there is no correct value other than <c>true</c>, and a property would have put the
/// name on the wire where a document could assert otherwise and be silently ignored.
/// </remarks>
public sealed record EuNormalisedEliJoin
{
    [JsonConstructor]
    public EuNormalisedEliJoin(string normalisedEli, SourceArtifactRef evidenceRef)
    {
        NormalisedEli = SourceCoreValidation.RequirePublisherUri(
            normalisedEli, nameof(normalisedEli));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public string NormalisedEli { get; }

    public SourceArtifactRef EvidenceRef { get; }

    /// <summary>Always true. The join is Lex's, never a publisher's.</summary>
    /// <remarks>
    /// A method for the same reason as <see cref="EuTranspositionSourceAcquisition.ProvesAbsence"/>.
    /// My earlier remark that a caller who could set this could set it wrong was exactly backwards
    /// for wire callers: as a property it was emitted and then ignored, so a hostile document could
    /// assert <c>is_derived: false</c> and be silently overruled rather than refused. Absent from
    /// the wire, the claim cannot be made at all.
    /// </remarks>
    public bool IsDerived() => true;
}

/// <summary>
/// A two-source LU-to-EU transposition bridge: the EU work, what can be said about transposing it,
/// and each publisher's own side kept apart.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two sides are separate fields, not a list.</b> A list of sides would let two Legilux
/// assertions sit where a Legilux and a NIM assertion belong, and a reader counting entries would
/// see agreement between two publishers where there is one publisher repeated. Separate fields make
/// that shape unrepresentable rather than merely refused.
/// </para>
/// <para>
/// <b>Transposability is read, never chosen.</b> It is computed from the publisher's own work-kind
/// assertion through <see cref="TransposabilityFor"/>, on the same rule as
/// <c>EuSelectionDisposition</c>'s policy and <c>LuxembourgAssertionFactDisposition</c>'s fact
/// kind: authority comes from the accepted reading, not from whoever is constructing. A caller
/// supplying a value that disagrees is refused by name.
/// </para>
/// <para>
/// <b>A non-transposable work carries no sides.</b> If transposition does not apply, a publisher
/// assertion that it was transposed is evidence disagreeing with itself, and this door refuses it
/// rather than storing both.
/// </para>
/// </remarks>
public sealed record EuTranspositionBridge
{
    [JsonConstructor]
    public EuTranspositionBridge(
        string euWorkUri,
        EuWorkKind workKind,
        EuTransposability transposability,
        EuTranspositionSourceAcquisition legilux,
        EuTranspositionSourceAcquisition nim,
        EuNormalisedEliJoin? normalisedEliJoin)
    {
        EuWorkUri = SourceCoreValidation.RequirePublisherUri(euWorkUri, nameof(euWorkUri));
        WorkKind = ContractValidation.RequireDefined(workKind, nameof(workKind));
        ContractValidation.RequireDefined(transposability, nameof(transposability));
        ArgumentNullException.ThrowIfNull(legilux);
        ArgumentNullException.ThrowIfNull(nim);

        var read = TransposabilityFor(workKind);
        if (transposability != read)
        {
            throw new ArgumentException(
                $"a {workKind} work is {read} in the accepted reading, not {transposability}; " +
                "transposability is read from the publisher's own work kind rather than chosen here.",
                nameof(transposability));
        }

        Transposability = transposability;

        if (legilux.AssertedBy != EuTranspositionAssertedBy.Legilux)
        {
            throw new ArgumentException(
                $"the Legilux column carries an acquisition by {legilux.AssertedBy}; the two " +
                "publishers' columns are separate and neither may hold the other's.",
                nameof(legilux));
        }

        if (nim.AssertedBy != EuTranspositionAssertedBy.Nim)
        {
            throw new ArgumentException(
                $"the NIM column carries an acquisition by {nim.AssertedBy}; the two " +
                "publishers' columns are separate and neither may hold the other's.",
                nameof(nim));
        }

        if (transposability == EuTransposability.NotTransposable
            && (legilux.Side is not null || nim.Side is not null))
        {
            throw new ArgumentException(
                "a not-transposable work carries no transposition side; a publisher assertion that " +
                "it was transposed disagrees with its own work kind and is refused rather than " +
                "stored beside it.",
                nameof(transposability));
        }

        // THE JOIN IS TWO-SOURCE OR IT IS NOT A JOIN. Its own remarks said it is present only when
        // both publishers named a measure, and the first version of this type documented that
        // without enforcing it -- the declared-but-unenforced shape I have reported in other
        // people's code twice this session. A join over one side, or none, or over a regulation
        // whose typed answer is that transposition does not apply, manufactures a two-source
        // derivation from evidence that does not exist.
        if (normalisedEliJoin is not null)
        {
            // A not-transposable work needs no arm of its own here, and must not have one. It is
            // already forbidden a side above, so it reaches this point with both sides null and the
            // check below refuses it for the true reason. I wrote that separate arm first; the
            // mutation that disables it leaves the suite green, because nothing can reach it. An
            // unreachable guard is the shape I have reported twice in other people's code this
            // session, so it is gone rather than kept for symmetry.
            if (legilux.Side is null || nim.Side is null)
            {
                throw new ArgumentException(
                    "the normalised ELI join needs both publishers to have named a measure; a join " +
                    "over " + (legilux.Side is null && nim.Side is null ? "neither side" : "one side") +
                    " is a two-source derivation without two sources.",
                    nameof(normalisedEliJoin));
            }
        }

        Legilux = legilux;
        Nim = nim;
        NormalisedEliJoin = normalisedEliJoin;
    }

    public string EuWorkUri { get; }

    public EuWorkKind WorkKind { get; }

    public EuTransposability Transposability { get; }

    /// <summary>Legilux's own acquisition, carrying its side and how far acquiring it got.</summary>
    public EuTranspositionSourceAcquisition Legilux { get; }

    /// <summary>The NIM acquisition, carrying its side and how far acquiring it got.</summary>
    public EuTranspositionSourceAcquisition Nim { get; }

    /// <summary>The derived join. Enforced, not merely documented: see the constructor.</summary>
    public EuNormalisedEliJoin? NormalisedEliJoin { get; }

    /// <summary>
    /// What the accepted reading says about transposing a work of this kind.
    /// </summary>
    /// <remarks>
    /// A switch with no default arm rather than a lookup with a fallback: a third CDM subclass
    /// arriving here should be a compile-time gap someone must answer, not a silent
    /// <see cref="EuTransposability.NotTransposable"/> that reads as a decision nobody made.
    /// </remarks>
    public static EuTransposability TransposabilityFor(EuWorkKind workKind) =>
        ContractValidation.RequireDefined(workKind, nameof(workKind)) switch
        {
            EuWorkKind.Directive => EuTransposability.Transposable,
            EuWorkKind.Regulation => EuTransposability.NotTransposable,
            _ => throw new ArgumentOutOfRangeException(
                nameof(workKind), workKind, "Unknown EU work kind."),
        };
}
