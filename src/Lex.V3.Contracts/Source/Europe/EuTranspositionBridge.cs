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
/// One publisher's own side of a transposition bridge, carrying who asserted it.
/// </summary>
public sealed record EuTranspositionSide
{
    [JsonConstructor]
    public EuTranspositionSide(
        EuTranspositionAssertedBy assertedBy,
        string nationalMeasureUri,
        SourceArtifactRef evidenceRef)
    {
        AssertedBy = ContractValidation.RequireDefined(assertedBy, nameof(assertedBy));
        NationalMeasureUri = SourceCoreValidation.RequirePublisherUri(
            nationalMeasureUri, nameof(nationalMeasureUri));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuTranspositionAssertedBy AssertedBy { get; }

    /// <summary>The national measure this publisher named, in that publisher's own spelling.</summary>
    public string NationalMeasureUri { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// The normalised ELI used to line the two publishers' spellings up, disclosed as derived.
/// </summary>
/// <remarks>
/// S2-A02: a derived view never becomes a publisher claim. The join is ours — it exists because the
/// two publishers spell the same national measure differently — so it carries no
/// <see cref="EuTranspositionAssertedBy"/> and cannot be constructed as though a publisher had
/// asserted it. <see cref="IsDerived"/> is a computed constant rather than a constructor parameter
/// for exactly the reason <c>EuLegislationSummary.Licence</c> is: a caller who could set it could
/// set it wrong, and there is no correct value other than <c>true</c>.
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
    public bool IsDerived => true;
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
        EuTranspositionSide? legiluxSide,
        EuTranspositionSide? nimSide,
        EuNormalisedEliJoin? normalisedEliJoin)
    {
        EuWorkUri = SourceCoreValidation.RequirePublisherUri(euWorkUri, nameof(euWorkUri));
        WorkKind = ContractValidation.RequireDefined(workKind, nameof(workKind));
        ContractValidation.RequireDefined(transposability, nameof(transposability));

        var read = TransposabilityFor(workKind);
        if (transposability != read)
        {
            throw new ArgumentException(
                $"a {workKind} work is {read} in the accepted reading, not {transposability}; " +
                "transposability is read from the publisher's own work kind rather than chosen here.",
                nameof(transposability));
        }

        Transposability = transposability;

        if (legiluxSide is not null && legiluxSide.AssertedBy != EuTranspositionAssertedBy.Legilux)
        {
            throw new ArgumentException(
                $"the Legilux column carries a side asserted by {legiluxSide.AssertedBy}; the two " +
                "publishers' columns are separate and neither may hold the other's assertion.",
                nameof(legiluxSide));
        }

        if (nimSide is not null && nimSide.AssertedBy != EuTranspositionAssertedBy.Nim)
        {
            throw new ArgumentException(
                $"the NIM column carries a side asserted by {nimSide.AssertedBy}; the two " +
                "publishers' columns are separate and neither may hold the other's assertion.",
                nameof(nimSide));
        }

        if (transposability == EuTransposability.NotTransposable
            && (legiluxSide is not null || nimSide is not null))
        {
            throw new ArgumentException(
                "a not-transposable work carries no transposition side; a publisher assertion that " +
                "it was transposed disagrees with its own work kind and is refused rather than " +
                "stored beside it.",
                nameof(transposability));
        }

        LegiluxSide = legiluxSide;
        NimSide = nimSide;
        NormalisedEliJoin = normalisedEliJoin;
    }

    public string EuWorkUri { get; }

    public EuWorkKind WorkKind { get; }

    public EuTransposability Transposability { get; }

    /// <summary>Legilux's own side, or null when Legilux has asserted none.</summary>
    public EuTranspositionSide? LegiluxSide { get; }

    /// <summary>The NIM side, or null when the Commission's records assert none.</summary>
    public EuTranspositionSide? NimSide { get; }

    /// <summary>The derived join, present only when both publishers named a measure.</summary>
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
