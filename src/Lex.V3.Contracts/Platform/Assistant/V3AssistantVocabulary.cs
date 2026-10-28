using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// The six verdicts an answer of the assistant can carry (36 §1.6; F30 §2): the legal verdict set, closed. A presentation
/// result is a type apart (<see cref="V3AskPresentationResult"/>, R45(a)) and no conversion exists between the two.
/// </summary>
/// <remarks>
/// The wire tokens are those of <see cref="V3Verdicts"/>, so an envelope's verdict slot reads the same word.
/// <see cref="AnswerWithEnrichment"/> is declared because the verdict set is closed at six, and never emitted: code has no
/// registry of official machine channels to establish one (docs/ANSWER-DOSSIER-1.md §6), so a planner that proposes it is
/// answered under <see cref="Point"/>.
/// </remarks>
public enum V3AssistantVerdict
{
    [JsonStringEnumMemberName("answer")]
    Answer = 1,

    [JsonStringEnumMemberName("answer_with_enrichment")]
    AnswerWithEnrichment = 2,

    [JsonStringEnumMemberName("point")]
    Point = 3,

    [JsonStringEnumMemberName("clarify")]
    Clarify = 4,

    [JsonStringEnumMemberName("refuse")]
    Refuse = 5,

    [JsonStringEnumMemberName("split")]
    Split = 6,
}

/// <summary>
/// The two typed presentation results of <c>ask</c> (Decision 91): neither an answer from held law nor a refusal, and never
/// counted as a legal verdict (R45(a)). The closed refusal registry stays at its twenty codes; neither token is in it.
/// </summary>
public enum V3AskPresentationResult
{
    [JsonStringEnumMemberName("assistant_v3_unavailable")]
    AssistantUnavailable = 1,

    [JsonStringEnumMemberName("localization_unavailable")]
    LocalizationUnavailable = 2,
}

/// <summary>
/// The locales the assistant answers in (R43): French and English, whose templates exist. Every other answering language is
/// answered with <see cref="V3AskPresentationResult.LocalizationUnavailable"/>, naming these two.
/// </summary>
public enum V3AnswerLocale
{
    [JsonStringEnumMemberName("fr")]
    Fr = 1,

    [JsonStringEnumMemberName("en")]
    En = 2,
}

/// <summary>
/// The language a question is written in, as the deterministic parser reads it from closed word lists
/// (<see cref="V3AssistantParsers.LanguageOf"/>). It decides the answering locale while <c>ask/1</c> carries the question
/// alone (docs/ANSWER-DOSSIER-1.md §3).
/// </summary>
public enum V3QuestionLanguage
{
    [JsonStringEnumMemberName("fr")]
    French = 1,

    [JsonStringEnumMemberName("en")]
    English = 2,

    [JsonStringEnumMemberName("de")]
    German = 3,

    [JsonStringEnumMemberName("lb")]
    Luxembourgish = 4,

    [JsonStringEnumMemberName("pt")]
    Portuguese = 5,

    [JsonStringEnumMemberName("undetermined")]
    Undetermined = 6,
}

/// <summary>
/// The four functions R15's function test refuses: output that chooses among legal options, concludes on a person's
/// situation, predicts a decision-maker, or computes deadlines or amounts from the asker's facts. Pronoun-blind: no member
/// is about who is asking.
/// </summary>
public enum V3ApplicationFunction
{
    [JsonStringEnumMemberName("chooses_among_options")]
    ChoosesAmongOptions = 1,

    [JsonStringEnumMemberName("concludes_on_situation")]
    ConcludesOnSituation = 2,

    [JsonStringEnumMemberName("predicts_decision_maker")]
    PredictsDecisionMaker = 3,

    [JsonStringEnumMemberName("computes_from_facts")]
    ComputesFromFacts = 4,
}

/// <summary>The eight typed flags R38's disclosure-completeness gate holds to 100 percent.</summary>
public enum V3DisclosureFlag
{
    [JsonStringEnumMemberName("derogation")]
    Derogation = 1,

    [JsonStringEnumMemberName("corrigendum")]
    Corrigendum = 2,

    [JsonStringEnumMemberName("pre_application")]
    PreApplication = 3,

    [JsonStringEnumMemberName("validity_conflict")]
    ValidityConflict = 4,

    [JsonStringEnumMemberName("title_distrust")]
    TitleDistrust = 5,

    [JsonStringEnumMemberName("provisional")]
    Provisional = 6,

    [JsonStringEnumMemberName("no_legal_effect")]
    NoLegalEffect = 7,

    [JsonStringEnumMemberName("authenticity")]
    Authenticity = 8,
}

/// <summary>
/// The role of a handoff registry entry (R42): advice, information, complaint and mediation, plus
/// <see cref="OfficialSource"/> for the publishers whose official texts Lex reproduces (a driver decision, recorded in
/// STATUS-WEB.md). An adviser is introduced as "who can advise you", a complaint body as "where to complain".
/// </summary>
public enum V3HandoffRole
{
    [JsonStringEnumMemberName("official_source")]
    OfficialSource = 1,

    [JsonStringEnumMemberName("information")]
    Information = 2,

    [JsonStringEnumMemberName("advice")]
    Advice = 3,

    [JsonStringEnumMemberName("complaint")]
    Complaint = 4,

    [JsonStringEnumMemberName("mediation")]
    Mediation = 5,
}

/// <summary>The typed condition on an earlier typed result that fires a follow-up operation (R35).</summary>
public enum V3FollowUpCondition
{
    /// <summary>The earlier step's result names exactly one work.</summary>
    [JsonStringEnumMemberName("on_single_work")]
    OnSingleWork = 1,

    /// <summary>The earlier step's result names exactly one article of exactly one work.</summary>
    [JsonStringEnumMemberName("on_single_article")]
    OnSingleArticle = 2,
}

/// <summary>What code takes from an earlier typed result to bind a follow-up's argument (R35: arguments bound by code).</summary>
public enum V3ResultPick
{
    [JsonStringEnumMemberName("sole_work")]
    SoleWork = 1,

    [JsonStringEnumMemberName("sole_article")]
    SoleArticle = 2,
}

/// <summary>Where a plan argument's value comes from. Closed: there is no free-text source, so a plan cannot carry prose.</summary>
public enum V3ArgumentSourceKind
{
    /// <summary>A span [start, end) of the question: the user's own words (R34).</summary>
    [JsonStringEnumMemberName("span")]
    Span = 1,

    /// <summary>A deterministic parser's output over the question, named by an opaque reference (d1, r1.from, r1.to).</summary>
    [JsonStringEnumMemberName("ref")]
    Reference = 2,

    /// <summary>A value code takes from an earlier step's typed result.</summary>
    [JsonStringEnumMemberName("result")]
    Result = 3,

    /// <summary>One value of the parameter's closed set (the search language).</summary>
    [JsonStringEnumMemberName("choice")]
    Choice = 4,

    /// <summary>Today in Luxembourg at the request (R13), printed in the answer and written into the trace.</summary>
    [JsonStringEnumMemberName("today")]
    Today = 5,
}

/// <summary>Why a planner's output was not admitted as a plan. Every one is answered with the switched-off card.</summary>
public enum V3PlanRefusal
{
    [JsonStringEnumMemberName("too_large")]
    TooLarge = 1,

    [JsonStringEnumMemberName("malformed")]
    Malformed = 2,

    [JsonStringEnumMemberName("unknown_member")]
    UnknownMember = 3,

    [JsonStringEnumMemberName("outside_closed_set")]
    OutsideClosedSet = 4,

    [JsonStringEnumMemberName("operation_not_allowed")]
    OperationNotAllowed = 5,

    [JsonStringEnumMemberName("unbound_argument")]
    UnboundArgument = 6,

    [JsonStringEnumMemberName("source_not_admitted")]
    SourceNotAdmitted = 7,

    [JsonStringEnumMemberName("span_out_of_bounds")]
    SpanOutOfBounds = 8,

    [JsonStringEnumMemberName("span_touches_mask")]
    SpanTouchesMask = 9,

    [JsonStringEnumMemberName("cap_exceeded")]
    CapExceeded = 10,

    [JsonStringEnumMemberName("follow_up_changes_request")]
    FollowUpChangesRequest = 11,
}
