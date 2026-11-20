using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// What a placeholder of an answer template accepts, and how code writes its value into the sentence. Closed, as
/// <see cref="V3FactKind"/> is, for the same reason: a reader tells a date from a hash from the user's own words without
/// parsing the sentence back apart.
/// </summary>
public enum V3AnswerSlotKind
{
    /// <summary>A civil date, held as <c>yyyy-MM-dd</c> and written in words in the answer's locale ("1er février 2024").</summary>
    [JsonStringEnumMemberName("calendar_date")]
    CalendarDate = 1,

    /// <summary>A work's address on this site, written verbatim.</summary>
    [JsonStringEnumMemberName("work_address")]
    WorkAddress = 2,

    /// <summary>The publisher's identifier of an article, written without its <c>art_</c> prefix.</summary>
    [JsonStringEnumMemberName("anchor")]
    Anchor = 3,

    /// <summary>A SHA-256 in lowercase hexadecimal.</summary>
    [JsonStringEnumMemberName("content_hash")]
    ContentHash = 4,

    /// <summary>An absolute http(s) address or a path of this site, written verbatim.</summary>
    [JsonStringEnumMemberName("uri")]
    Uri = 5,

    /// <summary>A verbatim span of the user's question.</summary>
    [JsonStringEnumMemberName("question_span")]
    QuestionSpan = 6,

    /// <summary>An identifier as an operation was asked it or answered it (a span of the question or a bound address), verbatim.</summary>
    [JsonStringEnumMemberName("identifier_text")]
    IdentifierText = 7,

    /// <summary>A held text language (<c>fra</c>, <c>eng</c>, <c>deu</c>, <c>ltz</c>), written as its name in the answer's locale.</summary>
    [JsonStringEnumMemberName("language_code")]
    LanguageCode = 8,

    /// <summary>A code of the closed refusal registry, written verbatim.</summary>
    [JsonStringEnumMemberName("refusal_code")]
    RefusalCode = 9,

    /// <summary>The name of a handoff registry entry, written verbatim.</summary>
    [JsonStringEnumMemberName("entry_name")]
    EntryName = 10,

    /// <summary>A whole number code counted (lines, articles), written in digits.</summary>
    [JsonStringEnumMemberName("count")]
    Count = 11,

    /// <summary>
    /// Words of the law itself, verbatim: an article's heading, a value written on a quoted line. The gates hold them to the
    /// text a step of the answer answered (<see cref="V3AnswerGates.LawWordsBound"/>), character for character.
    /// </summary>
    [JsonStringEnumMemberName("law_words")]
    LawWords = 12,

    /// <summary>A term the build's dictionary or corpus index writes (a concept's label, a keyword, an act's name), verbatim: a closed, reviewed list.</summary>
    [JsonStringEnumMemberName("dictionary_term")]
    DictionaryTerm = 13,
}

/// <summary>Who stands behind a template's wording: a master the specification ships, or a draft, with its review if any.</summary>
/// <param name="SourceMaster">The ruling that ships this exact wording, or null.</param>
/// <param name="DraftedBy">Who drafted a wording no ruling ships, or null for a source master.</param>
/// <param name="ReviewedBy">Who reviewed a draft, or null. An AI reviewer is named as one; it is not R43's qualified person.</param>
/// <param name="ReviewedOn">The review's date, or null.</param>
/// <param name="QualifiedPersonReview">Whether R43's qualified person reviewed it. False for every template today.</param>
public sealed record V3TemplateProvenance(
    string? SourceMaster,
    string? DraftedBy,
    string? ReviewedBy,
    string? ReviewedOn,
    bool QualifiedPersonReview);

/// <summary>
/// One template of the assistant's closed set, in one locale: fixed wording with <c>{placeholder}</c> markers, each of a
/// declared <see cref="V3AnswerSlotKind"/>. The binding rules are <see cref="V3AuthoritativeClaim"/>'s, in both directions.
/// Unlike a claim template, an answer template may name no placeholder: a verdict label or a notice says the same thing on
/// every answer, and is still wording somebody reviews rather than a sentence written at a call site.
/// </summary>
public sealed class V3AnswerTemplate
{
    internal static readonly Regex PlaceholderPattern = new(@"\{([a-z][a-z0-9_]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, V3AnswerSlotKind> _kinds;

    private V3AnswerTemplate(
        string templateId,
        V3AnswerLocale locale,
        string text,
        ReadOnlyCollection<string> placeholders,
        Dictionary<string, V3AnswerSlotKind> kinds,
        V3TemplateProvenance provenance)
    {
        TemplateId = templateId;
        Locale = locale;
        Text = text;
        Placeholders = placeholders;
        _kinds = kinds;
        Provenance = provenance;
    }

    public string TemplateId { get; }

    public V3AnswerLocale Locale { get; }

    public string Text { get; }

    public ReadOnlyCollection<string> Placeholders { get; }

    public V3TemplateProvenance Provenance { get; }

    public V3AnswerSlotKind KindOf(string placeholder) =>
        _kinds.TryGetValue(placeholder, out var kind)
            ? kind
            : throw new ArgumentException($"'{TemplateId}' names no placeholder '{placeholder}'.", nameof(placeholder));

    internal static V3AnswerTemplate Define(
        string templateId,
        V3AnswerLocale locale,
        string text,
        V3TemplateProvenance provenance,
        params (string Name, V3AnswerSlotKind Kind)[] declared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(provenance);
        if ((provenance.SourceMaster is null) == (provenance.DraftedBy is null))
        {
            throw new ArgumentException($"'{templateId}' ({locale}) must be a source master or a draft, never both or neither.", nameof(provenance));
        }

        if ((provenance.ReviewedBy is null) != (provenance.ReviewedOn is null))
        {
            throw new ArgumentException($"'{templateId}' ({locale}) names a reviewer without a date or a date without a reviewer.", nameof(provenance));
        }

        var placeholders = new List<string>();
        foreach (Match match in PlaceholderPattern.Matches(text))
        {
            if (!placeholders.Contains(match.Groups[1].Value, StringComparer.Ordinal))
            {
                placeholders.Add(match.Groups[1].Value);
            }
        }

        if (PlaceholderPattern.Replace(text, string.Empty).AsSpan().IndexOfAny('{', '}') >= 0)
        {
            throw new ArgumentException($"'{templateId}' ({locale}) carries a brace no placeholder rule reads.", nameof(text));
        }

        var kinds = new Dictionary<string, V3AnswerSlotKind>(StringComparer.Ordinal);
        foreach (var (name, kind) in declared)
        {
            if (!Enum.IsDefined(kind) || !kinds.TryAdd(name, kind))
            {
                throw new ArgumentException($"'{templateId}' ({locale}) declares '{name}' twice or outside the closed set.", nameof(declared));
            }
        }

        if (!placeholders.Order(StringComparer.Ordinal).SequenceEqual(kinds.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{templateId}' ({locale}) declares kinds for [{string.Join(", ", kinds.Keys.Order(StringComparer.Ordinal))}] "
                + $"while its wording names [{string.Join(", ", placeholders.Order(StringComparer.Ordinal))}].",
                nameof(declared));
        }

        return new V3AnswerTemplate(templateId, locale, text, placeholders.AsReadOnly(), kinds, provenance);
    }
}

/// <summary>One typed value bound to a placeholder of an answer template.</summary>
public sealed record V3AnswerFact(string Name, V3AnswerSlotKind Kind, string Value);

/// <summary>
/// One visible sentence of an answer: a template, the typed facts bound to it, and the text, which is the template with each
/// placeholder replaced by its fact's value written by <see cref="V3AnswerTemplates.WriteValue"/>, and nothing else.
/// </summary>
public sealed class V3AnswerClaim
{
    private V3AnswerClaim(string templateId, V3AnswerLocale locale, ReadOnlyCollection<V3AnswerFact> facts, string text)
    {
        TemplateId = templateId;
        Locale = locale;
        Facts = facts;
        Text = text;
    }

    public string TemplateId { get; }

    public V3AnswerLocale Locale { get; }

    public ReadOnlyCollection<V3AnswerFact> Facts { get; }

    public string Text { get; }

    /// <summary>Binds facts to a template of the closed set, or throws: an unknown template, a hole, an unused fact, a wrong kind or a value its kind refuses.</summary>
    public static V3AnswerClaim Bind(string templateId, V3AnswerLocale locale, params V3AnswerFact[] facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var template = V3AnswerTemplates.Get(templateId, locale);
        var byName = new Dictionary<string, V3AnswerFact>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            ArgumentNullException.ThrowIfNull(fact);
            if (string.IsNullOrWhiteSpace(fact.Name) || !Enum.IsDefined(fact.Kind) || string.IsNullOrWhiteSpace(fact.Value))
            {
                throw new ArgumentException($"'{templateId}' carries a fact with no name, no value or a kind outside the closed set.", nameof(facts));
            }

            if (!byName.TryAdd(fact.Name, fact))
            {
                throw new ArgumentException($"'{templateId}' binds '{fact.Name}' twice.", nameof(facts));
            }
        }

        var unbound = template.Placeholders.Where(name => !byName.ContainsKey(name)).ToArray();
        var unused = byName.Keys.Where(name => !template.Placeholders.Contains(name, StringComparer.Ordinal)).ToArray();
        if (unbound.Length > 0 || unused.Length > 0)
        {
            throw new ArgumentException(
                $"'{templateId}' leaves [{string.Join(", ", unbound)}] unbound or carries [{string.Join(", ", unused)}] it never states.",
                nameof(facts));
        }

        foreach (var name in template.Placeholders)
        {
            if (byName[name].Kind != template.KindOf(name))
            {
                throw new ArgumentException($"'{templateId}' binds '{name}' to a {byName[name].Kind} where its wording states a {template.KindOf(name)}.", nameof(facts));
            }
        }

        // One pass over the template, so a value is output and never read again as a placeholder.
        var text = V3AnswerTemplate.PlaceholderPattern.Replace(
            template.Text,
            match => V3AnswerTemplates.WriteValue(byName[match.Groups[1].Value].Kind, byName[match.Groups[1].Value].Value, locale));
        return new V3AnswerClaim(
            templateId,
            locale,
            template.Placeholders.Select(name => byName[name]).ToList().AsReadOnly(),
            text);
    }
}

/// <summary>
/// The assistant's closed template set, <c>lex-v3-answer-templates/1</c>, in French and English (R43), with the digest the
/// trace records. Every visible sentence of an answer is one of these filled by code, a verbatim quote, or a verbatim span
/// of the question (R31). docs/ANSWER-DOSSIER-1.md §7 lists them with where each is used.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance.</b> The R14, R19, R21, R28 and R50 sentences are the specification's own masters, in English (R44's notice and
/// R17's derogation clause are drafted from their rulings); R50's French is
/// J6's (<c>live-chrome-fr.mjs</c>). Every other English sentence is drafted by an agent. Every French sentence that is not a
/// master is a translation checked by an independent AI reviewer, whose finals are applied byte for byte. None of them has
/// been reviewed by R43's qualified person, which <see cref="QualifiedReviewComplete"/> reports, and the assistant cannot be
/// switched on while it is false.
/// </para>
/// </remarks>
public static partial class V3AnswerTemplates
{
    public const string SetId = "lex-v3-answer-templates/1";

    private const string AgentDraft = "an agent (Claude), 2026-10-28";

    // ---- Template ids -------------------------------------------------------------------------------------------------

    public const string VerdictAnswer = "verdict.answer";
    public const string VerdictPoint = "verdict.point";
    public const string VerdictClarify = "verdict.clarify";
    public const string VerdictRefuse = "verdict.refuse";
    public const string VerdictSplit = "verdict.split";
    public const string NoticeInput = "notice.input";
    public const string NoticeAssembledByModel = "notice.assembled_by_model";
    public const string NoticeNotLegalAdvice = "notice.not_legal_advice";
    public const string PresentationAssistantUnavailable = "presentation.assistant_unavailable";
    public const string PresentationLocalizationUnavailable = "presentation.localization_unavailable";
    public const string PlanHeading = "plan.heading";
    public const string PlanSplit = "plan.split";
    public const string PlanDateToday = "plan.date_today";
    public const string PlanSearch = "plan.step.search";
    public const string PlanSearchDated = "plan.step.search_dated";
    public const string PlanResolve = "plan.step.resolve";
    public const string PlanDossier = "plan.step.dossier";
    public const string PlanAsOf = "plan.step.as_of";
    public const string PlanTimeline = "plan.step.timeline";
    public const string PlanEvidenceBundle = "plan.step.evidence_bundle";
    public const string PlanEvidenceBundleArticle = "plan.step.evidence_bundle_article";
    public const string PlanArticleHistory = "plan.step.article_history";
    public const string PlanDiff = "plan.step.diff";
    public const string PlanChangesInPeriod = "plan.step.changes_in_period";
    public const string PlanFollowUpSingleWork = "plan.follow_up.single_work";
    public const string PlanFollowUpSingleArticle = "plan.follow_up.single_article";
    public const string LawQuoteHeading = "law.quote_heading";
    public const string LawIntervalLu = "law.interval_lu";
    public const string LawIntervalLuOpen = "law.interval_lu_open";
    public const string LawDatedByEntryIntoForce = "law.dated_by_entry_into_force";
    public const string LawIntervalEu = "law.interval_eu";
    public const string LawIntervalEuOpen = "law.interval_eu_open";
    public const string LawContentHash = "law.content_hash";
    public const string LawOfficialSource = "law.official_source";
    public const string LawPermalink = "law.permalink";
    public const string LawQuoteShortened = "law.quote_shortened";
    public const string BoundaryWhole = "boundary.whole";
    public const string BoundaryWholeNoAdviser = "boundary.whole_no_adviser";
    public const string BoundaryWholeNoText = "boundary.whole_no_text";
    public const string BoundaryLine = "boundary.line";
    public const string BoundaryNoAdviser = "boundary.no_adviser";
    public const string PointNoHit = "point.no_hit";
    public const string PointNotHeld = "point.not_held";
    public const string ClarifyWhichAct = "clarify.which_act";
    public const string ClarifyWhichArticle = "clarify.which_article";
    public const string ClarifyWhichIdentifier = "clarify.which_identifier";
    public const string ClarifyWhichState = "clarify.which_state";
    public const string ActionOpenWork = "action.open_work";
    public const string ActionReadArticle = "action.read_article";
    public const string ActionReadState = "action.read_state";
    public const string ActionCandidate = "action.candidate";
    public const string RefusalNoVersionForDate = "refusal.no_version_for_date";
    public const string RefusalIdentifierUnknown = "refusal.identifier_unknown";
    public const string RefusalTextWithheld = "refusal.text_withheld";
    public const string RefusalTextNotAvailable = "refusal.text_not_available";
    public const string RefusalOutOfCorpusScope = "refusal.out_of_corpus_scope";
    public const string RefusalAnchorNotInVersion = "refusal.anchor_not_in_version";
    public const string RefusalOther = "refusal.other";
    public const string HandoffOfficialSource = "handoff.official_source";
    public const string HandoffInformation = "handoff.information";
    public const string HandoffAdvice = "handoff.advice";
    public const string HandoffComplaint = "handoff.complaint";
    public const string HandoffMediation = "handoff.mediation";
    public const string DisclosureAuthenticityLu = "disclosure.authenticity_lu";
    public const string DisclosureAuthenticityEu = "disclosure.authenticity_eu";
    public const string DisclosureNoLegalEffect = "disclosure.no_legal_effect";
    public const string DisclosureProvisional = "disclosure.provisional";
    public const string DisclosureValidityConflict = "disclosure.validity_conflict";
    public const string DisclosureCorrigendum = "disclosure.corrigendum";
    public const string DisclosurePreApplication = "disclosure.pre_application";
    public const string DisclosureDerogation = "disclosure.derogation";
    public const string DisclosureTitleDistrust = "disclosure.title_distrust";
    public const string ClarifyWhichArticleOfAct = "clarify.which_article_of_act";
    public const string ClarifyWhichWordingEu = "clarify.which_wording_eu";
    public const string LawEuOriginalWording = "law.eu_original_wording";
    public const string DisclosureAuthenticityEuOriginal = "disclosure.authenticity_eu_original";
    public const string PlanTimelineEu = "plan.step.timeline_eu";
    public const string RefusalNoVersionForDateEu = "refusal.no_version_for_date_eu";
    public const string ActionReadStateEu = "action.read_state_eu";
    public const string NoticePreview = "notice.preview";

    // The bricks' sentences (V4, 2026-11-18): what the answer is made of, the notes on the reader's words, the four ways to
    // say "not found", and the plan lines of the route.
    public const string NoticeNoModel = "notice.no_model";
    public const string NoticeModelAssisted = "notice.model_assisted";
    public const string NoticeModelDescribed = "notice.model_described";
    public const string PlanBricksTerm = "plan.bricks.term";
    public const string PlanBricksKindDefinition = "plan.bricks.kind.definition";
    public const string PlanBricksKindRule = "plan.bricks.kind.rule";
    public const string PlanBricksKindDuration = "plan.bricks.kind.duration";
    public const string PlanBricksKindDate = "plan.bricks.kind.date";
    public const string PlanBricksKindAmount = "plan.bricks.kind.amount";
    public const string PlanBricksKindList = "plan.bricks.kind.list";
    public const string PlanBricksKindReference = "plan.bricks.kind.reference";
    public const string PlanBricksKindText = "plan.bricks.kind.text";
    public const string PlanBricksScope = "plan.bricks.scope";
    public const string PlanBricksRuleQuestion = "plan.bricks.rule_question";
    public const string PlanBricksDetectorTitle = "plan.bricks.detector.title";
    public const string PlanBricksDetectorKeyword = "plan.bricks.detector.keyword";
    public const string PlanBricksDetectorCooccurrence = "plan.bricks.detector.cooccurrence";
    public const string PlanBricksDetectorTypePattern = "plan.bricks.detector.type_pattern";
    public const string PlanBricksRouteDefinition = "plan.bricks.route.definition";
    public const string PlanBricksRouteFact = "plan.bricks.route.fact";
    public const string PlanBricksModelNone = "plan.bricks.model.none";
    public const string PlanBricksModelQuestion = "plan.bricks.model.question";
    public const string PlanBricksModelArbiter = "plan.bricks.model.arbiter";
    public const string PlanBricksModelExtraction = "plan.bricks.model.extraction";
    public const string PlanBricksModelOutline = "plan.bricks.model.outline";
    public const string PlanBricksModelUnavailable = "plan.bricks.model.unavailable";
    public const string PlanBricksModelQuestionUnavailable = "plan.bricks.model.question_unavailable";
    public const string PlanBricksPassSweepCases = "plan.bricks.pass.sweep_cases";
    public const string PlanBricksPassWider = "plan.bricks.pass.wider_context";
    public const string PlanBricksPassRetry = "plan.bricks.pass.retry";
    public const string AnswerDefinition = "answer.definition";
    public const string AnswerRule = "answer.rule";
    public const string AnswerRuleCases = "answer.rule_cases";
    public const string AnswerValue = "answer.value";
    public const string AnswerValueWords = "answer.value_words";
    public const string AnswerSeveral = "answer.several";
    public const string AnswerList = "answer.list";
    public const string AnswerPassages = "answer.passages";
    public const string AnswerCandidates = "answer.candidates";
    public const string AnswerMoreArticles = "answer.more_articles";
    public const string AnswerTermUsage = "answer.term_usage";
    public const string LawExtractHeading = "law.extract_heading";
    public const string CaveatConditionsApply = "caveat.conditions_apply";
    public const string CaveatNotYourCase = "caveat.not_your_case";
    public const string CaveatLeftToContract = "caveat.left_to_contract";
    public const string CaveatCasesFromSweep = "caveat.cases_from_sweep";
    public const string CaveatListFromSweep = "caveat.list_from_sweep";
    public const string CaveatGeneralAndSpecial = "caveat.general_and_special";
    public const string CaveatNotDefined = "caveat.not_defined";
    public const string CaveatVocabularyIncomplete = "caveat.vocabulary_incomplete";
    public const string NoteLawTerm = "note.law_term";
    public const string NoteLatestHeld = "note.latest_held";
    public const string NoteWordNotInAct = "note.word_not_in_act";
    public const string AbsenceSweepHeading = "absence.sweep_heading";
    public const string AbsenceTermCount = "absence.term_count";
    public const string AbsenceTermsNowhere = "absence.terms_nowhere";
    public const string AbsenceWordsNotFound = "absence.words_not_found";
    public const string AbsenceTermsNowhereInAct = "absence.terms_nowhere_in_act";
    public const string AbsenceWordsNotFoundInAct = "absence.words_not_found_in_act";
    public const string AbsenceNoAnswerInLines = "absence.no_answer_in_lines";
    public const string AbsenceActNotHeld = "absence.act_not_held";
    public const string AbsenceTextNotHeld = "absence.text_not_held";
    public const string DirectGreeting = "direct.greeting";
    public const string AbsenceArticleNotHeld = "absence.article_not_held";
    public const string ClarifyOneWord = "clarify.one_word";
    public const string ClarifyNoContext = "clarify.no_context";

    private static readonly V3TemplateProvenance EnglishDraft = new(null, AgentDraft, null, null, false);

    private const string BricksAgentDraft = "an agent (Claude), 2026-11-18";

    private static readonly V3TemplateProvenance BricksDraft = new(null, BricksAgentDraft, null, null, false);

    // The sentence of an absence as a reader sees it (2026-11-19): one sentence, the act by its name or "this text", never
    // its address on this site, and no line of counts.
    private const string AbsenceAgentDraft = "an agent (Claude), 2026-11-19";

    private static readonly V3TemplateProvenance AbsenceDraft = new(null, AbsenceAgentDraft, null, null, false);

    private static V3TemplateProvenance Master(string ruling) => new(ruling, null, null, null, false);

    private static readonly (string Id, string En, V3TemplateProvenance EnBasis, (string, V3AnswerSlotKind)[] Slots)[] Definitions =
    [
        // Verdict labels (R28).
        (VerdictAnswer, "From the official text", Master("R28"), []),
        (VerdictPoint, "Not held here: official source", Master("R28"), []),
        (VerdictClarify, "Needs one detail", Master("R28"), []),
        (VerdictRefuse, "Outside what this service does", Master("R28"), []),
        (VerdictSplit, "Two questions", Master("R28"), []),
        // Notices (R44 above the input; R33 on every answer; R50 on every answer).
        (NoticeInput, "This is an automated assistant that shows published legal texts. It does not give legal advice or apply the law to your situation. Do not type names, health details or other personal details.", EnglishDraft, []),
        (NoticeAssembledByModel, "An AI model assembled this answer: it planned which of Lex's operations to run. Lex's code checked that plan, ran the operations and wrote every sentence from fixed wording; the quotations are the published text.", EnglishDraft, []),
        (NoticeNotLegalAdvice, "Lex shows the published text of the law. It does not tell you whether or how it applies to your situation and is not legal advice.", Master("R50"), []),
        // Presentation results (Decision 91; R43).
        (PresentationAssistantUnavailable, "The assistant is switched off on Lex; search, reading and the other operations work.", Master("the driver's decision of 2026-10-28 (STATUS-WEB.md)"), []),
        (PresentationLocalizationUnavailable, "The assistant answers only in French and English.", EnglishDraft, []),
        // The validated plan, shown first (R45(c)).
        (PlanHeading, "What Lex did with your question", EnglishDraft, []),
        (PlanSplit, "Lex read two questions in yours: “{first}” and “{second}”.", EnglishDraft, [("first", V3AnswerSlotKind.QuestionSpan), ("second", V3AnswerSlotKind.QuestionSpan)]),
        (PlanDateToday, "Your question names no date: Lex read the law as it stood today, {date}.", EnglishDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (PlanSearch, "Searched the text Lex holds in {language} for your words: “{query}”.", EnglishDraft, [("language", V3AnswerSlotKind.LanguageCode), ("query", V3AnswerSlotKind.QuestionSpan)]),
        (PlanSearchDated, "Searched the text Lex holds in {language}, as it stood on {date}, for your words: “{query}”.", EnglishDraft, [("language", V3AnswerSlotKind.LanguageCode), ("date", V3AnswerSlotKind.CalendarDate), ("query", V3AnswerSlotKind.QuestionSpan)]),
        (PlanResolve, "Looked up “{identifier}”.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText)]),
        (PlanDossier, "Opened the record of {identifier}.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText)]),
        (PlanAsOf, "Read {identifier} as it stood on {date}.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText), ("date", V3AnswerSlotKind.CalendarDate)]),
        (PlanTimeline, "Listed the versions of {identifier} that Lex holds.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText)]),
        (PlanEvidenceBundle, "Read the text of {identifier} as it stood on {date}.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText), ("date", V3AnswerSlotKind.CalendarDate)]),
        (PlanEvidenceBundleArticle, "Read article {anchor} of {identifier} as it stood on {date}.", EnglishDraft, [("anchor", V3AnswerSlotKind.Anchor), ("identifier", V3AnswerSlotKind.IdentifierText), ("date", V3AnswerSlotKind.CalendarDate)]),
        (PlanArticleHistory, "Listed the wordings of article {anchor} of {identifier} that Lex holds.", EnglishDraft, [("anchor", V3AnswerSlotKind.Anchor), ("identifier", V3AnswerSlotKind.IdentifierText)]),
        (PlanDiff, "Compared {identifier} as it stood on {date_from} and on {date_to}.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText), ("date_from", V3AnswerSlotKind.CalendarDate), ("date_to", V3AnswerSlotKind.CalendarDate)]),
        (PlanChangesInPeriod, "Listed what the publisher changed between {date_from} and {date_to}, as Lex holds it.", EnglishDraft, [("date_from", V3AnswerSlotKind.CalendarDate), ("date_to", V3AnswerSlotKind.CalendarDate)]),
        (PlanFollowUpSingleWork, "Because that step named exactly one act, Lex took the next step:", EnglishDraft, []),
        (PlanFollowUpSingleArticle, "Because that step named exactly one article, Lex took the next step:", EnglishDraft, []),
        // The law, verbatim and dated (R45(b); R21's interval wording; R20 and R19).
        (LawQuoteHeading, "{work}, article {anchor}, as it read on {date}", EnglishDraft, [("work", V3AnswerSlotKind.WorkAddress), ("anchor", V3AnswerSlotKind.Anchor), ("date", V3AnswerSlotKind.CalendarDate)]),
        (LawIntervalLu, "Applicable from {from} to {to} (publisher).", Master("R21"), [("from", V3AnswerSlotKind.CalendarDate), ("to", V3AnswerSlotKind.CalendarDate)]),
        (LawIntervalLuOpen, "Applicable from {from} (publisher).", Master("R21"), [("from", V3AnswerSlotKind.CalendarDate)]),
        (LawDatedByEntryIntoForce, "Dated by its entry into force on {date} (publisher), not by an applicability date.", EnglishDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (LawIntervalEu, "Consolidated wording state from {from} to {to}.", Master("R21"), [("from", V3AnswerSlotKind.CalendarDate), ("to", V3AnswerSlotKind.CalendarDate)]),
        (LawIntervalEuOpen, "Consolidated wording state from {from}.", Master("R21"), [("from", V3AnswerSlotKind.CalendarDate)]),
        (LawContentHash, "Content hash: {hash}", EnglishDraft, [("hash", V3AnswerSlotKind.ContentHash)]),
        (LawOfficialSource, "Official source: {uri}", EnglishDraft, [("uri", V3AnswerSlotKind.Uri)]),
        (LawPermalink, "Permalink: {uri}", EnglishDraft, [("uri", V3AnswerSlotKind.Uri)]),
        (LawQuoteShortened, "The article continues beyond this quotation; read it whole at {uri}.", EnglishDraft, [("uri", V3AnswerSlotKind.Uri)]),
        // The advice boundary (R14, R45(b), S4-A05). boundary.whole is R14's master and is emitted only once the handoff
        // registry holds a reviewed adviser for the question's theme, which it does not: no adviser is invented (the
        // driver's decision of 2026-10-28), so the answer says so instead.
        (BoundaryWhole, "I can show you exactly what the published text says, at any date, and how it changed, with citations. I cannot tell you whether or how it applies to your situation: that depends on facts about you, and this service does not apply the law to individual cases. Here is the governing text as it stood on {date}, and here is who can advise you.", Master("R14"), [("date", V3AnswerSlotKind.CalendarDate)]),
        (BoundaryWholeNoAdviser, "I can show you exactly what the published text says, at any date, and how it changed, with citations. I cannot tell you whether or how it applies to your situation: that depends on facts about you, and this service does not apply the law to individual cases. Here is the governing text as it stood on {date}. Lex does not yet name who can advise you: that list must first be checked by a person.", EnglishDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (BoundaryWholeNoText, "I can show you exactly what the published text says, at any date, and how it changed, with citations. I cannot tell you whether or how it applies to your situation: that depends on facts about you, and this service does not apply the law to individual cases. Lex does not yet name who can advise you: that list must first be checked by a person.", EnglishDraft, []),
        (BoundaryLine, "Whether or how this text applies to your situation depends on facts about you; Lex does not apply the law to individual cases.", EnglishDraft, []),
        (BoundaryNoAdviser, "Lex does not yet name who can advise you: that list must first be checked by a person.", EnglishDraft, []),
        // Outcomes from the operations' typed results (R22, R24, R83).
        (PointNoHit, "Lex found no article with your words “{query}” in the text it holds. This says what Lex holds, not what the law contains.", EnglishDraft, [("query", V3AnswerSlotKind.QuestionSpan)]),
        (PointNotHeld, "Lex holds no text that answers this. The official sources below hold the published law.", EnglishDraft, []),
        (ClarifyWhichAct, "Your words occur in more than one act Lex holds. Which act do you mean?", EnglishDraft, []),
        (ClarifyWhichArticle, "Your words occur in more than one article of {work}. Which article do you mean?", EnglishDraft, [("work", V3AnswerSlotKind.WorkAddress)]),
        (ClarifyWhichIdentifier, "What your question names can be more than one act. Which one do you mean?", EnglishDraft, []),
        (ClarifyWhichState, "More than one version Lex holds covers {date}. Which one do you mean?", EnglishDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (ActionOpenWork, "Open {work}", EnglishDraft, [("work", V3AnswerSlotKind.WorkAddress)]),
        (ActionReadArticle, "Read article {anchor} of {work} as it stood on {date}", EnglishDraft, [("anchor", V3AnswerSlotKind.Anchor), ("work", V3AnswerSlotKind.WorkAddress), ("date", V3AnswerSlotKind.CalendarDate)]),
        (ActionReadState, "Read the version of {work} applicable from {date}", EnglishDraft, [("work", V3AnswerSlotKind.IdentifierText), ("date", V3AnswerSlotKind.CalendarDate)]),
        (ActionCandidate, "{candidate}", EnglishDraft, [("candidate", V3AnswerSlotKind.IdentifierText)]),
        (RefusalNoVersionForDate, "Lex holds no version of {work} that applies on {date}. The absence of a held version is not evidence that the law was unchanged.", EnglishDraft, [("work", V3AnswerSlotKind.IdentifierText), ("date", V3AnswerSlotKind.CalendarDate)]),
        (RefusalIdentifierUnknown, "Lex does not recognise “{identifier}” as an act it holds. This says what Lex holds, not whether the act exists.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText)]),
        (RefusalTextWithheld, "Lex holds this text but does not show it, because its licence is not cleared. The official source below shows it.", EnglishDraft, []),
        (RefusalTextNotAvailable, "Lex holds the record of this act but not its text. The official source below has it.", EnglishDraft, []),
        (RefusalOutOfCorpusScope, "This is outside the law Lex holds. The official source below holds it.", EnglishDraft, []),
        (RefusalAnchorNotInVersion, "Lex holds no article {anchor} in this version. The nearest articles it holds there are listed below.", EnglishDraft, [("anchor", V3AnswerSlotKind.Anchor)]),
        (RefusalOther, "Lex did not answer this step ({code}); the details below say why.", EnglishDraft, [("code", V3AnswerSlotKind.RefusalCode)]),
        // Handoffs by role (R42).
        (HandoffOfficialSource, "Official source: {name}, {uri}", EnglishDraft, [("name", V3AnswerSlotKind.EntryName), ("uri", V3AnswerSlotKind.Uri)]),
        (HandoffInformation, "Information: {name}, {uri}", EnglishDraft, [("name", V3AnswerSlotKind.EntryName), ("uri", V3AnswerSlotKind.Uri)]),
        (HandoffAdvice, "Who can advise you: {name}, {uri}", EnglishDraft, [("name", V3AnswerSlotKind.EntryName), ("uri", V3AnswerSlotKind.Uri)]),
        (HandoffComplaint, "Where to complain: {name}, {uri}", EnglishDraft, [("name", V3AnswerSlotKind.EntryName), ("uri", V3AnswerSlotKind.Uri)]),
        (HandoffMediation, "Mediation: {name}, {uri}", EnglishDraft, [("name", V3AnswerSlotKind.EntryName), ("uri", V3AnswerSlotKind.Uri)]),
        // Disclosures (R38), each shown whenever its flag is on a served object.
        (DisclosureAuthenticityLu, "This is Lex's copy of the text, for information; it is not authentic. Only the texts published in the Journal officiel (Mémorial A) are authentic: the act as published, {uri}.", EnglishDraft, [("uri", V3AnswerSlotKind.Uri)]),
        (DisclosureAuthenticityEu, "All official language versions of the act as published in the Official Journal are equally authentic. This consolidated text, in any language, is documentation without legal effect.", Master("R19"), []),
        (DisclosureNoLegalEffect, "A consolidated text is documentation without legal effect.", EnglishDraft, []),
        (DisclosureProvisional, "This version applies from {date}, after the day Lex's index was built; Lex never uses a later version for today.", EnglishDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (DisclosureValidityConflict, "The publisher dates this article {date}, which differs from the version's own date; both dates are shown.", EnglishDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (DisclosureCorrigendum, "A corrigendum of this act exists; its date and languages are in the act's record.", EnglishDraft, []),
        (DisclosurePreApplication, "On this date the act's date of entry into force had been reached, but not yet its date of application.", EnglishDraft, []),
        (DisclosureDerogation, "Another act temporarily derogated from this provision; Lex does not yet hold the publisher's dates for this derogation.", EnglishDraft, []),
        (DisclosureTitleDistrust, "Titles are shown as the publisher wrote them; Lex does not check them against the act's dates.", EnglishDraft, []),
        // EU forms of the Luxembourg sentences (R21: an EU wording is a consolidated wording state, never a version), the
        // EU original wording and its authenticity, and the question an opened act asks.
        (ClarifyWhichArticleOfAct, "Which article of {work} do you mean?", EnglishDraft, [("work", V3AnswerSlotKind.WorkAddress)]),
        (ClarifyWhichWordingEu, "More than one wording Lex holds covers {date}. Which one do you mean?", EnglishDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (LawEuOriginalWording, "Wording as published in the Official Journal, dated {from}.", EnglishDraft, [("from", V3AnswerSlotKind.CalendarDate)]),
        (DisclosureAuthenticityEuOriginal, "All official language versions of the act as published in the Official Journal are equally authentic. This is Lex's copy of the text as published, for information; it is not authentic.", EnglishDraft, []),
        (PlanTimelineEu, "Listed the wordings of {identifier} that Lex holds.", EnglishDraft, [("identifier", V3AnswerSlotKind.IdentifierText)]),
        (RefusalNoVersionForDateEu, "Lex holds no wording of {work} for {date}. The absence of a held wording is not evidence that the law was unchanged.", EnglishDraft, [("work", V3AnswerSlotKind.IdentifierText), ("date", V3AnswerSlotKind.CalendarDate)]),
        (ActionReadStateEu, "Read the wording of {work} dated {date}", EnglishDraft, [("work", V3AnswerSlotKind.IdentifierText), ("date", V3AnswerSlotKind.CalendarDate)]),
        // The preview line (the owner's order of 2026-10-28): on every answer while the people's reviews are not done.
        (NoticePreview, "Preview: not yet reviewed by a lawyer or by independent annotators.", EnglishDraft, []),
        // The bricks (V4). Drafted by an agent on 2026-11-18; the French was reviewed by an independent AI reviewer the same
        // day, and the English follows what that review found (a count of one, two overstatements).
        (NoticeNoModel, "No AI model took part in this answer: Lex's code read your question, searched the text it holds and wrote every sentence from fixed wording; the quotations are the published text.", BricksDraft, []),
        (NoticeModelAssisted, "An AI model helped find the passages: among the lines Lex's code gave it, it marked those that answer your question. It wrote no sentence of this answer: Lex's code checked each line it marked and wrote every sentence from fixed wording; the quotations are the published text.", BricksDraft, []),
        (NoticeModelDescribed, "An AI model described your question: what it asks for, and which of your words name an act, an article or a subject. Lex's code chose what to run from that description, ran it and wrote every sentence from fixed wording; the quotations are the published text.", BricksDraft, []),
        (PlanBricksTerm, "Lex read your words “{words}” as the term “{term}” of its insurance vocabulary.", BricksDraft, [("words", V3AnswerSlotKind.QuestionSpan), ("term", V3AnswerSlotKind.DictionaryTerm)]),
        (PlanBricksKindDefinition, "Kind of answer looked for: a definition.", BricksDraft, []),
        (PlanBricksKindRule, "Kind of answer looked for: a rule, with its conditions and exceptions.", BricksDraft, []),
        (PlanBricksKindDuration, "Kind of answer looked for: a period of time.", BricksDraft, []),
        (PlanBricksKindDate, "Kind of answer looked for: a date or a moment.", BricksDraft, []),
        (PlanBricksKindAmount, "Kind of answer looked for: an amount or a rate.", BricksDraft, []),
        (PlanBricksKindList, "Kind of answer looked for: a list.", BricksDraft, []),
        (PlanBricksKindReference, "Kind of answer looked for: the provision that says it.", BricksDraft, []),
        (PlanBricksKindText, "Kind of answer looked for: the passages that deal with it.", BricksDraft, []),
        (PlanBricksScope, "Text read: {work}, as it stood on {date} (articles: {articles}; lines: {lines}).", BricksDraft, [("work", V3AnswerSlotKind.WorkAddress), ("date", V3AnswerSlotKind.CalendarDate), ("articles", V3AnswerSlotKind.Count), ("lines", V3AnswerSlotKind.Count)]),
        (PlanBricksRuleQuestion, "Your question asks what the rule is, with no fact of a personal case: Lex quotes the rule and does not say whether it applies to a situation.", BricksDraft, []),
        (PlanBricksDetectorTitle, "Articles whose heading holds the term: {articles}.", BricksDraft, [("articles", V3AnswerSlotKind.Count)]),
        (PlanBricksDetectorKeyword, "Lines that hold a form of the term: {lines}; articles concerned: {articles}.", BricksDraft, [("lines", V3AnswerSlotKind.Count), ("articles", V3AnswerSlotKind.Count)]),
        (PlanBricksDetectorCooccurrence, "Lines where the term stands with the party your question names or with a value: {lines}; articles concerned: {articles}.", BricksDraft, [("lines", V3AnswerSlotKind.Count), ("articles", V3AnswerSlotKind.Count)]),
        (PlanBricksDetectorTypePattern, "Lines that also have the form of the answer looked for: {lines}; articles concerned: {articles}.", BricksDraft, [("lines", V3AnswerSlotKind.Count), ("articles", V3AnswerSlotKind.Count)]),
        (PlanBricksRouteDefinition, "The text defines this term: Lex read the line that defines it.", BricksDraft, []),
        (PlanBricksRouteFact, "One line clearly leads and has the form of the answer: Lex quotes it without asking an AI model.", BricksDraft, []),
        (PlanBricksModelNone, "No AI model was asked.", BricksDraft, []),
        (PlanBricksModelQuestion, "An AI model described your question (the kind of answer, the terms). It chose no operation.", BricksDraft, []),
        (PlanBricksModelArbiter, "An AI model gave each passage found a role (main, supporting, set aside). It could add no passage and write no text.", BricksDraft, []),
        (PlanBricksModelExtraction, "An AI model marked, among the lines given to it, those that answer your question. Lex's code checked each one against the text before showing it.", BricksDraft, []),
        (PlanBricksModelOutline, "An AI model named, in the tables of contents of the texts Lex reads on this subject, the articles to read. It could name only articles of those tables.", BricksDraft, []),
        (PlanBricksModelUnavailable, "The AI model that ranks the passages gave no usable answer: Lex shows the passages its code found, in the order that code set.", BricksDraft, []),
        (PlanBricksModelQuestionUnavailable, "An AI model was asked to describe your question and gave no usable answer: Lex's code searched your own words.", BricksDraft, []),
        (PlanBricksPassSweepCases, "The rule refers to the cases the law provides without naming them: Lex swept the whole text for them.", BricksDraft, []),
        (PlanBricksPassWider, "A first reading gave a partial answer: Lex read the whole article again.", BricksDraft, []),
        (PlanBricksPassRetry, "A first answer of the AI model was refused by Lex's checks: Lex asked again with the lines of the best passage only.", BricksDraft, []),
        (AnswerDefinition, "The text defines this term. The line that defines it:", BricksDraft, []),
        (AnswerRule, "The rule, in the text's own words:", BricksDraft, []),
        (AnswerRuleCases, "Cases the text provides, found in this text:", BricksDraft, []),
        (AnswerValue, "The text states it as follows:", BricksDraft, []),
        (AnswerValueWords, "Written on this line: “{value}”.", BricksDraft, [("value", V3AnswerSlotKind.LawWords)]),
        (AnswerSeveral, "This text gives more than one answer, depending on the case. Each is quoted with its article:", BricksDraft, []),
        (AnswerList, "What Lex found in this text:", BricksDraft, []),
        (AnswerPassages, "The passages of this text that deal with your question:", BricksDraft, []),
        (AnswerCandidates, "Lex found these passages and did not rank them further. The closest:", BricksDraft, []),
        (AnswerMoreArticles, "Other articles of this text where the term appears:", BricksDraft, []),
        (AnswerTermUsage, "Lines of this text where this term appears: {lines}; articles concerned: {articles}.", BricksDraft, [("lines", V3AnswerSlotKind.Count), ("articles", V3AnswerSlotKind.Count)]),
        (LawExtractHeading, "{work}, article {anchor}, “{heading}”, as it read on {date}", BricksDraft, [("work", V3AnswerSlotKind.WorkAddress), ("anchor", V3AnswerSlotKind.Anchor), ("heading", V3AnswerSlotKind.LawWords), ("date", V3AnswerSlotKind.CalendarDate)]),
        (CaveatConditionsApply, "Conditions apply: they are in the lines quoted.", BricksDraft, []),
        (CaveatNotYourCase, "The text does not say how it applies to your situation.", BricksDraft, []),
        (CaveatLeftToContract, "On this point the text refers to the contract: it sets no value of its own.", BricksDraft, []),
        (CaveatCasesFromSweep, "This list comes from a sweep of the text for the term's words; a case worded differently may be missing.", BricksDraft, []),
        (CaveatListFromSweep, "This list comes from a sweep of the text; an element worded differently may be missing.", BricksDraft, []),
        (CaveatGeneralAndSpecial, "A general rule and a special rule bear on this point; both are quoted.", BricksDraft, []),
        (CaveatNotDefined, "Lex found no definition of this term in this text; these are the passages that use it.", BricksDraft, []),
        (CaveatVocabularyIncomplete, "Lex's vocabulary on this subject is not complete: a passage worded differently may exist.", BricksDraft, []),
        (NoteLawTerm, "You wrote “{words}”; the text's word is “{term}”.", BricksDraft, [("words", V3AnswerSlotKind.QuestionSpan), ("term", V3AnswerSlotKind.DictionaryTerm)]),
        (NoteLatestHeld, "Latest version held: {date}. Lex holds no more recent version of this text.", BricksDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (NoteWordNotInAct, "The word “{words}” of your question appears nowhere in this text.", BricksDraft, [("words", V3AnswerSlotKind.QuestionSpan)]),
        // The two below are no longer composed (2026-11-19: they showed a reader the act's address and lines of zero counts);
        // the sweep they wrote is still in the plan record of every absence.
        (AbsenceSweepHeading, "Lex swept the text of {work} (lines: {lines}; articles: {articles}), as it stood on {date}, for these terms:", BricksDraft, [("work", V3AnswerSlotKind.WorkAddress), ("lines", V3AnswerSlotKind.Count), ("articles", V3AnswerSlotKind.Count), ("date", V3AnswerSlotKind.CalendarDate)]),
        (AbsenceTermCount, "Lines that hold “{term}”: {lines}; articles concerned: {articles}.", BricksDraft, [("term", V3AnswerSlotKind.DictionaryTerm), ("lines", V3AnswerSlotKind.Count), ("articles", V3AnswerSlotKind.Count)]),
        (AbsenceTermsNowhere, "None of the terms Lex knows for this subject appears in this text, as it stood on {date}: this says what this text contains, not what a contract provides.", AbsenceDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (AbsenceTermsNowhereInAct, "None of the terms Lex knows for this subject appears in the text “{name}”, as it stood on {date}: this says what this text contains, not what a contract provides.", AbsenceDraft, [("name", V3AnswerSlotKind.DictionaryTerm), ("date", V3AnswerSlotKind.CalendarDate)]),
        (AbsenceWordsNotFound, "Lex found no passage on this subject in this text, as it stood on {date}: this does not show that the text is silent, because Lex looks only for the words it knows for this subject.", AbsenceDraft, [("date", V3AnswerSlotKind.CalendarDate)]),
        (AbsenceWordsNotFoundInAct, "Lex found no passage on this subject in the text “{name}”, as it stood on {date}: this does not show that the text is silent, because Lex looks only for the words it knows for this subject.", AbsenceDraft, [("name", V3AnswerSlotKind.DictionaryTerm), ("date", V3AnswerSlotKind.CalendarDate)]),
        (AbsenceNoAnswerInLines, "Lex found lines on this subject, but none gives an answer of the kind asked. The closest:", BricksDraft, []),
        (AbsenceActNotHeld, "Your question names an act: {name}. Lex does not hold the text of this act yet.", BricksDraft, [("name", V3AnswerSlotKind.DictionaryTerm)]),
        (AbsenceTextNotHeld, "Lex holds the record of {work} but no text of it that it can show for this date.", BricksDraft, [("work", V3AnswerSlotKind.WorkAddress)]),
        (DirectGreeting, "Hello. Ask a question about a Luxembourg or EU legal text that Lex holds; Lex answers by quoting the text.", BricksDraft, []),
        (AbsenceArticleNotHeld, "Article {anchor} exists but Lex does not hold its text.", BricksDraft, [("anchor", V3AnswerSlotKind.Anchor)]),
        (ClarifyNoContext, "Lex cannot tell what your question refers to. Ask it in full, naming the text or the subject: Lex answers by quoting the text.", BricksDraft, []),
        (ClarifyOneWord, "One word alone does not tell Lex what you want to know. Ask a full question: Lex answers by quoting the text.", BricksDraft, []),
    ];

    private static readonly Dictionary<(string, V3AnswerLocale), V3AnswerTemplate> ByIdValue = BuildAll();

    /// <summary>Every template, by id then locale (French before English), in ordinal order of the id.</summary>
    public static ReadOnlyCollection<V3AnswerTemplate> All { get; } = ByIdValue.Values
        .OrderBy(static template => template.TemplateId, StringComparer.Ordinal)
        .ThenBy(static template => template.Locale)
        .ToList()
        .AsReadOnly();

    /// <summary>The ids of the set, in ordinal order.</summary>
    public static ReadOnlyCollection<string> Ids { get; } = Definitions.Select(static definition => definition.Id)
        .Order(StringComparer.Ordinal).ToList().AsReadOnly();

    /// <summary>The SHA-256 of the set's canonical bytes (<see cref="CanonicalUtf8"/>), which every trace records.</summary>
    public static string Sha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(CanonicalUtf8()));

    /// <summary>
    /// Whether R43's qualified person reviewed every template in both locales. False today; the assistant's switch cannot
    /// take effect while it is false (the runtime asks it).
    /// </summary>
    public static bool QualifiedReviewComplete => All.All(static template => template.Provenance.QualifiedPersonReview);

    /// <summary>
    /// Whether an answer is a preview: the people's reviews (R43's of the templates, R42's of the handoff entries) are not done.
    /// Every answer then opens with <c>notice.preview</c>.
    /// </summary>
    public static bool Preview => !QualifiedReviewComplete || !V3HandoffRegistry.ReviewedByPerson;

    public static bool IsDefined(string? templateId) =>
        !string.IsNullOrWhiteSpace(templateId) && ByIdValue.ContainsKey((templateId, V3AnswerLocale.En));

    public static V3AnswerTemplate Get(string templateId, V3AnswerLocale locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        return ByIdValue.TryGetValue((templateId, locale), out var template)
            ? template
            : throw new ArgumentException(
                $"'{templateId}' is not an answer template in {locale}. A sentence no template carries is a change to the set, reviewed, and never a string written at a call site.",
                nameof(templateId));
    }

    /// <summary>
    /// The value of a fact as the answer writes it: a date in words in the locale, a language by its name, an anchor without
    /// its <c>art_</c> prefix, and every other kind verbatim. Throws for a value its kind refuses, so a malformed date or hash
    /// cannot reach a sentence.
    /// </summary>
    public static string WriteValue(V3AnswerSlotKind kind, string value, V3AnswerLocale locale)
    {
        ArgumentNullException.ThrowIfNull(value);
        return kind switch
        {
            V3AnswerSlotKind.CalendarDate => DateInWords(value, locale),
            V3AnswerSlotKind.Anchor => AnchorText(value),
            V3AnswerSlotKind.ContentHash => IsSha256(value) ? value : throw new ArgumentException("A content hash is 64 lowercase hexadecimal digits.", nameof(value)),
            V3AnswerSlotKind.Uri => IsUri(value) ? value : throw new ArgumentException("A uri is an absolute http(s) address or a path of this site.", nameof(value)),
            V3AnswerSlotKind.LanguageCode => LanguageName(value, locale),
            V3AnswerSlotKind.RefusalCode => V3OperationRegistry.Reviewed.DeclaresRefusal(value) ? value : throw new ArgumentException("Not a code of the closed refusal registry.", nameof(value)),
            V3AnswerSlotKind.WorkAddress or V3AnswerSlotKind.QuestionSpan or V3AnswerSlotKind.IdentifierText or V3AnswerSlotKind.EntryName or V3AnswerSlotKind.LawWords =>
                string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A verbatim value is not blank.", nameof(value)) : value,
            V3AnswerSlotKind.Count => value.Length is >= 1 and <= 9 && value.All(char.IsAsciiDigit) && (value.Length == 1 || value[0] != '0')
                ? value
                : throw new ArgumentException("A count is a whole number in digits.", nameof(value)),
            V3AnswerSlotKind.DictionaryTerm => Bricks.V4Dictionary.Insurance.IsTerm(value) || Bricks.V4CorpusIndex.Insurance.IsName(value)
                ? value
                : throw new ArgumentException("Not a term of the build's dictionary or corpus index.", nameof(value)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an answer slot kind."),
        };
    }

    /// <summary>The languages a <see cref="V3AnswerSlotKind.LanguageCode"/> names, with their French and English names.</summary>
    private static readonly Dictionary<string, (string Fr, string En)> LanguageNames = new(StringComparer.Ordinal)
    {
        ["fra"] = ("français", "French"),
        ["eng"] = ("anglais", "English"),
        ["deu"] = ("allemand", "German"),
        ["ltz"] = ("luxembourgeois", "Luxembourgish"),
    };

    /// <summary>The held text languages a search language choice may name.</summary>
    public static ReadOnlyCollection<string> LanguageCodes { get; } = LanguageNames.Keys.Order(StringComparer.Ordinal).ToList().AsReadOnly();

    private static readonly string[] FrenchMonths =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static string DateInWords(string value, V3AnswerLocale locale)
    {
        if (value.Length != 10 || !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new ArgumentException("A calendar date is yyyy-MM-dd.", nameof(value));
        }

        return locale == V3AnswerLocale.Fr
            ? $"{(date.Day == 1 ? "1er" : date.Day.ToString(CultureInfo.InvariantCulture))} {FrenchMonths[date.Month - 1]} {date.Year.ToString(CultureInfo.InvariantCulture)}"
            : $"{date.Day.ToString(CultureInfo.InvariantCulture)} {EnglishMonths[date.Month - 1]} {date.Year.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string AnchorText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An anchor is not blank.", nameof(value));
        }

        var text = value.StartsWith("art_", StringComparison.Ordinal) && value.Length > 4 ? value[4..] : value;
        return text.Replace('_', ' ');
    }

    private static string LanguageName(string value, V3AnswerLocale locale) =>
        LanguageNames.TryGetValue(value, out var names)
            ? (locale == V3AnswerLocale.Fr ? names.Fr : names.En)
            : throw new ArgumentException("Not a held text language this set names.", nameof(value));

    internal static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    internal static bool IsUri(string value) =>
        (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Any(char.IsWhiteSpace))
        || (System.Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == System.Uri.UriSchemeHttps || uri.Scheme == System.Uri.UriSchemeHttp));

    /// <summary>
    /// The set's canonical bytes: <c>{schema, templates: [{id, locale, text, placeholders: [{name, kind}], provenance}]}</c>
    /// in <see cref="All"/>'s order, UTF-8, no indentation. The digest every trace records is over these.
    /// </summary>
    public static byte[] CanonicalUtf8()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SetId);
            writer.WriteStartArray("templates");
            foreach (var template in ByIdValue.Values
                         .OrderBy(static template => template.TemplateId, StringComparer.Ordinal)
                         .ThenBy(static template => template.Locale))
            {
                writer.WriteStartObject();
                writer.WriteString("id", template.TemplateId);
                writer.WriteString("locale", ContractWire.NameOf(template.Locale));
                writer.WriteString("text", template.Text);
                writer.WriteStartArray("placeholders");
                foreach (var name in template.Placeholders)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", name);
                    writer.WriteString("kind", ContractWire.NameOf(template.KindOf(name)));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteStartObject("provenance");
                writer.WriteString("source_master", template.Provenance.SourceMaster);
                writer.WriteString("drafted_by", template.Provenance.DraftedBy);
                writer.WriteString("reviewed_by", template.Provenance.ReviewedBy);
                writer.WriteString("reviewed_on", template.Provenance.ReviewedOn);
                writer.WriteBoolean("qualified_person_review", template.Provenance.QualifiedPersonReview);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static Dictionary<(string, V3AnswerLocale), V3AnswerTemplate> BuildAll()
    {
        var all = new Dictionary<(string, V3AnswerLocale), V3AnswerTemplate>();
        var french = FrenchTexts();
        foreach (var (id, en, enBasis, slots) in Definitions)
        {
            if (!french.TryGetValue(id, out var frenchEntry))
            {
                throw new InvalidOperationException($"The answer template '{id}' has no French.");
            }

            var (fr, frBasis) = frenchEntry;
            if (!all.TryAdd((id, V3AnswerLocale.En), V3AnswerTemplate.Define(id, V3AnswerLocale.En, en, enBasis, slots)) ||
                !all.TryAdd((id, V3AnswerLocale.Fr), V3AnswerTemplate.Define(id, V3AnswerLocale.Fr, fr, frBasis, slots)))
            {
                throw new InvalidOperationException($"The answer template '{id}' is defined twice.");
            }
        }

        if (french.Count != Definitions.Length)
        {
            throw new InvalidOperationException("The French table names a template the set does not define.");
        }

        return all;
    }
}
