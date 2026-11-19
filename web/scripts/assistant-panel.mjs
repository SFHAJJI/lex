// The Assistant panel's copy and rules (R47, R52, R55, R45(c)-(e); docs/ANSWER-DOSSIER-1.md).
//
// Built, tested and hidden. The assistant is switched off on Lex (R30), so ASSISTANT_PANEL holds 'hidden', the only value
// this module admits, and no page entry imports the panel: R52 says nothing unserved is shown, and R47 says the assistant's
// entry stays hidden until the assistant is switched on. The tests render the panel from drafted answers and check that no
// built page carries its marker. Switching the panel on is a change to this module, reviewed in the release that switches
// the assistant on, never a value read at run time.
//
// The panel renders an answer_dossier/1 and adds nothing to it. Every sentence of an answer arrives as text the server
// filled from its closed template set, as a verbatim quotation or as a span of the question; this module holds only the
// panel's own words (its heading, its field, its two announcements) in French and English, the R44 notice, which is the
// server's `notice.input` template word for word (a test reads both), the preview line (`notice.preview`, word for word) and
// the processor sentence under the notice. The privacy notice's own section on the assistant (its processor and what that
// processor may keep) is in the legal pages' tables (live-chrome.mjs, live-chrome-fr.mjs: privacy.sections.assistant).

import { readDossier } from './dossier-answer.mjs';
import { actTitle, articleNumberOf, versionsOf, yearOf } from './law-page.mjs';
import { executionOf, maskedCount, numericDate, plannedOperations, searchStepOf } from './answer-view.mjs';

export const ASSISTANT_PANEL = 'hidden';

/** The assistant's page under each interface language, served only while the server reports the assistant on (V3WebRoot). */
export const ASSISTANT_PAGE = 'assistant';

/** The attribute every element of the panel's root carries, which the build checks no page holds. */
export const ASSISTANT_PANEL_MARKER = 'data-lex-assistant-panel';

export const DOSSIER_SCHEMA = 'lex-v3-answer-dossier/1';

/** The six verdicts (R28's labels arrive with the answer), and the two presentation results, a type apart (R45(a)). */
export const VERDICTS = Object.freeze(['answer', 'answer_with_enrichment', 'point', 'clarify', 'refuse', 'split']);
export const PRESENTATION_RESULTS = Object.freeze(['assistant_v3_unavailable', 'localization_unavailable']);

/** The kinds a visible text may have (R31): a template filled by code, a verbatim quotation, a span of the question. */
export const TEXT_KINDS = Object.freeze(['claim', 'quote', 'question_span']);

/** The block kinds a part of an answer holds, in the closed set the contract names. */
export const BLOCK_KINDS = Object.freeze(['claim', 'quote', 'question_span', 'refusal', 'action', 'handoff', 'disclosure']);

/** At most two follow-up context chips travel with a new question (R35, R45(e)); earlier question text never does. */
export const MAXIMUM_CONTEXT_CHIPS = 2;

const PANEL_EN = /* @__PURE__ */ Object.freeze({
    heading: 'Assistant',
    notice:
      'This is an automated assistant that shows published legal texts. It does not give legal advice or apply the law to your situation. Do not type names, health details or other personal details.',
    preview: 'Preview: not yet reviewed by a lawyer or by independent annotators.',
    processor:
      'Your question, with the e-mail addresses, phone numbers, bank account numbers and identity numbers that Lex\'s code recognises removed, is sent to Microsoft\'s Azure OpenAI service (EU data zone), which describes the question or marks lines of law that Lex\'s code found, unless Lex\'s code answers it alone; nothing else about you is sent. Microsoft does not use it to train models; for abuse monitoring, Microsoft may keep it for up to 30 days. Lex keeps nothing.',
    inputLabel: 'Your question',
    submit: 'Ask',
    contextLabel: 'Kept from your last answer',
    removeChip: 'Remove {chip}',
    chipWork: 'Act: {value}',
    chipDate: 'Date: {value}',
    answerHeading: 'Answer',
    quoteLabel: 'Published text',
    detailsSummary: 'Details for checking: hashes and the trace',
    checksSummary: 'Source and checks',
    otherLanguages: 'The same text in the other language',
    announceVerdict: 'Answer: {label}.',
    announceComplete: 'The answer is complete.',
    failed: 'The assistant could not answer this question; search, reading and the other operations do not depend on it.',
    rateLimited: 'Many questions were asked from this connection in a short time; ask again in a minute.',
    // The answer card (the owner's order of 2026-11-13, section C): the verdict in a word, the article and its heading, the
    // act's title, the quotation, the version it is from, and three ways on.
    verdictFound: 'Found',
    verdictNotFound: 'Not found',
    verdictOutOfScope: 'Out of scope',
    articleLine: 'Article {number}',
    articleLineHeading: 'Article {number}: {heading}',
    versionBadge: 'Version applicable from {date}',
    otherDate: 'See at another date',
    whatChanged: 'What changed',
    officialSource: 'Official source',
    howFound: 'How Lex found this',
    // The answer screen (the owner's order of 2026-11-14, evening).
    answerHeadingLabel: 'Answer: {label}',
    asOf: 'as of {date}',
    fullArticle: 'Full article',
    versionsSince: '{count} versions since {year}',
    versionsOne: 'One version, from {year}',
    lawPage: 'The act’s versions',
    originalFrench: 'The quote is the original French text.',
    notFound: 'No article can be quoted for this question, and Lex does not guess.',
    keywordSearch: 'Search these words in the texts',
    showWork: 'Show the work',
    workDocument: 'Document',
    workQuestion: 'Question',
    workRetrieval: 'Retrieval',
    workAnswer: 'Answer',
    workModel: 'the only model call',
    workCode: 'code, no model',
    documentLine: 'The version applicable from {date}, as published',
    documentNone: 'No text was read',
    planDate: 'As of {date}: ',
    planSearch: 'search for “{query}” in the {language} text',
    planArticle: 'read article {number}',
    planDossier: 'open the act’s versions',
    planOther: 'run {operation}',
    planThen: ', then ',
    planNothing: 'no operation to run',
    languageFra: 'French',
    languageEng: 'English',
    languageDeu: 'German',
    retrievalLine: 'Articles matched: {matched}; kept: {kept}',
    retrievalMore: 'Articles matched: more than {matched}; kept: {kept}',
    retrievalDirect: 'Article read by its number; kept: {kept}',
    retrievalPending: 'Counting…',
    retrievalNone: 'No search run',
    retrievalNamed: 'The question names its article: no search, read by its number; kept: {kept}',
    noticeLine: 'Preview, not reviewed by a lawyer. Not legal advice.',
    checkLine: 'Quotes matching the published text’s digest: {verified} of {quotes}',
    checkNone: 'Nothing quoted, nothing to check',
    checkPending: 'Checking…',
    workTime: 'Time: {seconds} s',
    workCost: 'Cost: about {cost} € a question',
    rawPlan: 'Raw plan',
    workPlanner: 'Engine: {planner}',
    workRegistry: 'MLflow registry: {model}, version {version}',
    // No version of the build that answered is in this page's copy of the registry: it is registered when it is put live.
    workRegistryNone: 'MLflow registry: {model} (this build is registered when it is put live)',
    // The docked assistant (the owner's brief of 2026-11-15, sections 5 and 6; the mockup's V1Home, V2Read, V3Compare and
    // V4Changes): its header, its first screen, the answer card, the five stages of the work and the states of section 6.
    dockPreview: 'preview, not legal advice',
    dockHide: 'Hide',
    dockOpen: 'Open the assistant',
    dockIntro: 'Ask in French or English. Lex finds the article, quotes it word for word, and opens the text at that place.',
    // The three examples, as the brief checked them live on 15/11/2026, each with its caption.
    dockExampleOne: "Quel est le délai de prescription d'un contrat d'assurance ?",
    dockExampleOneCaption: 'a question in your own words',
    dockExampleTwo: "Loi du 27 juillet 1997 sur le contrat d'assurance, article 38, 1er juin 2012",
    dockExampleTwoCaption: 'a citation with a date, opened by code with no model call',
    dockExampleThree: 'What does Article 5 of the GDPR say?',
    dockExampleThreeCaption: 'an article of an EU regulation',
    dockDoes: 'What it does.',
    dockDoesText: 'Code reads your question, finds the lines of the text and writes the answer from them. An AI model may describe a question or rank passages; it writes nothing you read.',
    dockDoesNot: 'What it does not do.',
    dockDoesNotText: 'It gives no advice on your situation, and when it does not find the article it says so.',
    dockHowLink: 'How an answer is built, with the measured results',
    dockStays: 'Your conversation stays here while you move between pages.',
    dockAgainAnswer: 'Answered from article {number}. Open the answer again',
    dockAgainOpened: 'Opened at article {number} by code. Open it again',
    dockAgain: 'Open the answer again',
    dockQuestionLabel: 'Your question',
    dockPlaceholder: 'Ask a question',
    dockPlaceholderAnother: 'Ask another question',
    dockPlaceholderText: 'Ask about a text',
    dockAsk: 'Ask',
    dockLoadingSearch: 'Reading your words…',
    dockLoadingAsk: 'Planning the search and reading the text…',
    dockFailure: 'The assistant could not be reached.',
    dockTryAgain: 'Try again',
    dockOpenedLabel: 'Opened by code, no model call',
    dockOpenedLine: 'Article {number} of the {title}, as it read on {date}.',
    dockOpenedLineNoTitle: 'Article {number}, as it read on {date}.',
    dockOpenedText: 'The {title}, as it read on {date}.',
    dockVerdictToday: 'From the official text, as it reads today',
    dockVerdictOn: 'From the official text, as it read on {date}',
    dockNotes: '{count} notes',
    dockNotesOne: '1 note',
    dockNotesHeading: 'Notes on this quote',
    dockCite: 'Article {number} of the {title}.',
    dockCiteNoTitle: 'Article {number}.',
    dockWholeOpen: 'The whole article is open on the left.',
    dockWholeLink: 'Open the whole article',
    dockStripSame: 'Same wording in all {count} versions held',
    dockStripOne: 'One version held',
    dockStripChanged: 'Wording changed on {date}',
    dockBuilt: 'How this answer was built',
    dockBuiltFoot: '{seconds} s, about €{cost}',
    dockBuiltCost: 'about €{cost}',
    dockStageMasking: 'Masking',
    dockStagePlanner: 'Planner',
    dockStageValidator: 'Validator',
    dockStageExecution: 'Execution',
    dockStageComposer: 'Composer',
    dockTagCode: 'code',
    dockTagModel: 'AI model',
    dockMaskNone: 'nothing to mask',
    dockMaskOne: 'one identifier masked before the model',
    dockMaskSome: '{count} identifiers masked before the model',
    dockOpSearch: 'search',
    dockOpRead: 'read the article',
    dockOpText: 'read the text',
    dockOpVersions: 'list the versions',
    dockOpHistory: 'read the article’s history',
    dockOpCompare: 'compare two versions',
    dockOpOther: 'run {operation}',
    dockOpThen: ', then ',
    dockPlanNone: 'no operation planned',
    dockValidatorAccepted: 'plan accepted, nothing repaired',
    dockExecRead: 'art. {number} read as of {date}',
    dockExecNoSingle: 'search run, no single article to read',
    dockExecRefused: '{operation} answered with a refusal',
    dockExecNone: 'no operation run',
    dockExecRan: '{count} operations run',
    dockComposerOne: 'quote identical to the text',
    dockComposerSome: '{count} quotes identical to the text',
    dockComposerNone: 'nothing quoted, every sentence from a template',
    dockPlanNote: 'The model plans; it never writes the answer.',
    dockStageQuestion: 'Question',
    dockStageRoute: 'Route',
    dockStageRetrieval: 'Retrieval',
    dockStageArbiter: 'Ranking',
    dockStageExtraction: 'Extraction',
    dockStageChecks: 'Checks',
    dockQuestionNone: 'no term of the dictionary read',
    dockQuestionOne: '1 term of the dictionary read; looking for {kind}',
    dockQuestionSome: '{count} terms of the dictionary read; looking for {kind}',
    dockKindDefinition: 'a definition',
    dockKindRule: 'a rule',
    dockKindDuration: 'a period',
    dockKindDate: 'a date',
    dockKindAmount: 'an amount',
    dockKindList: 'a list',
    dockKindReference: 'a provision',
    dockKindText: 'passages',
    dockRouteDefinition: 'a term the law defines: its line, no model',
    dockRouteFact: 'one line clearly leads: no model',
    dockRouteRule: 'a rule and its cases',
    dockRouteList: 'a list: the whole text swept',
    dockRouteOpen: 'passages found, then read',
    dockRouteAbsence: 'nothing to quote: the sweep is shown',
    dockRouteNotHeld: 'a text Lex does not hold',
    dockRouteGreeting: 'not a question on the law',
    dockRouteOwnCase: 'a personal case: refused, the law beside it',
    dockRetrievalFound: 'the term is on {lines} lines, in {articles} articles; {kept} kept',
    dockRetrievalNone: 'no line of the text holds the term',
    dockRetrievalLine: 'the defining line, by its place in the text',
    dockRetrievalSkipped: 'no text read',
    dockArbiterLine: 'main: {primary}; supporting: {supporting}; set aside: {others}',
    dockExtractionLine: 'lines pointed at: {count}, each checked in the text',
    dockChecksLine: 'quotes identical to the text: {count}',
    dockBricksNote: 'A model may rank passages and point at lines; it never writes the answer.',
    dockBricksNoModel: 'No AI model was asked for this answer.',
    dockDescribedNote: 'A model describes the question; code chooses what to run and writes the answer.',
    dockNoModelUsed: 'No AI model took part in this answer.',
    dockQuestionDescribed: 'described by an AI model: {asks}',
    dockQuestionByCode: 'read by code alone: your words are searched as written',
    dockQuestionCited: 'read by code alone: the act and the article you cite',
    dockAsksCited: 'an article or an act is cited',
    dockAsksSubject: 'words on a subject',
    dockAsksVersions: 'the versions of an act',
    dockAsksRecord: 'the record of an act',
    dockAsksOnDate: 'an act on a date',
    dockAsksCompared: 'an act compared between two dates',
    dockAsksPeriod: 'what changed in a period',
    dockAsksIdentifier: 'an exact identifier',
    dockAsksNothing: 'nothing to look up',
    dockAsksAnd: '; ',
    dockSaidLabel: 'What Lex found',
    dockSeePlan: 'See the plan',
    dockHidePlan: 'Hide the plan',
    dockPlanHeading: 'The plan, as returned',
    dockStepsHeading: 'The steps run, as returned',
    dockNotFound: 'Lex did not find an article for these words.',
    dockSearched: 'Words searched: “{words}”',
    dockFindByName: 'Find a text by name',
    dockFindInLaw: 'Find in this law',
    dockClarify: 'Your words are in more than one text. Which one do you mean?',
    dockChoice: 'Article {number}, {heading}',
    dockChoiceBare: 'Article {number}',
    dockChoicesPending: 'Reading the titles…',
    dockAdvice: 'Lex shows the text of the law; it does not say what applies to your situation.',
    dockAdviceExample: 'A question it can answer:',
    dockOff: 'The assistant is switched off. The texts, their versions and compare keep working: they use no model.',
    dockCouldNot: 'The assistant could not answer this question: its planner failed, its plan was refused, or this month’s spending cap is reached. The texts, their versions and compare keep working: they use no model.',
});

const PANEL_FR = /* @__PURE__ */ Object.freeze({
    heading: 'Assistant',
    notice:
      'Ceci est un assistant automatisé qui montre des textes juridiques publiés. Il ne donne pas de conseil juridique et n’applique pas le droit à votre situation. N’écrivez ni noms, ni données de santé, ni autres données personnelles.',
    preview: 'Phase d’essai : l’assistant n’a pas encore été examiné par un avocat ni par des annotateurs indépendants.',
    processor:
      'Votre question, dont sont retirés les adresses électroniques, numéros de téléphone, numéros de compte bancaire et numéros d’identification que reconnaît le code de Lex, est envoyée au service Azure OpenAI de Microsoft (zone de données de l’UE), qui indique ce qu’elle demande ou désigne des lignes de loi que le code de Lex a trouvées, sauf si le code de Lex y répond seul ; rien d’autre vous concernant n’est envoyé. Microsoft ne l’utilise pas pour entraîner des modèles ; aux fins de sa surveillance des abus, Microsoft peut la conserver jusqu’à 30 jours. Lex ne conserve rien.',
    inputLabel: 'Votre question',
    submit: 'Demander',
    contextLabel: 'Repris de la dernière réponse',
    removeChip: 'Retirer {chip}',
    chipWork: 'Acte : {value}',
    chipDate: 'Date : {value}',
    answerHeading: 'Réponse',
    quoteLabel: 'Texte publié',
    detailsSummary: 'Éléments de vérification : empreintes et trace',
    checksSummary: 'Source et vérification',
    otherLanguages: 'Le même texte dans l’autre langue',
    announceVerdict: 'Réponse : {label}.',
    announceComplete: 'La réponse est complète.',
    failed: 'L’assistant n’a pas pu répondre à cette question ; la recherche, la lecture et les autres opérations ne dépendent pas de l’assistant.',
    rateLimited: 'Beaucoup de questions ont été posées depuis cette connexion en peu de temps ; reposez la vôtre dans une minute.',
    verdictFound: 'Trouvé',
    verdictNotFound: 'Pas trouvé',
    verdictOutOfScope: 'Hors périmètre',
    articleLine: 'Article {number}',
    articleLineHeading: 'Article {number} : {heading}',
    versionBadge: 'Version applicable depuis le {date}',
    otherDate: 'Voir à une autre date',
    whatChanged: 'Ce qui a changé',
    officialSource: 'Source officielle',
    howFound: 'Comment Lex a trouvé',
    answerHeadingLabel: 'Réponse : {label}',
    asOf: 'au {date}',
    fullArticle: 'Article complet',
    versionsSince: '{count} versions depuis {year}',
    versionsOne: 'Une version, de {year}',
    lawPage: 'Les versions de l’acte',
    originalFrench: 'La citation est le texte original en français.',
    notFound: 'Aucun article ne peut être cité pour cette question, et Lex ne devine pas.',
    keywordSearch: 'Chercher ces mots dans les textes',
    showWork: 'Voir le travail',
    workDocument: 'Document',
    workQuestion: 'Question',
    workRetrieval: 'Recherche',
    workAnswer: 'Réponse',
    workModel: 'le seul appel au modèle',
    workCode: 'code, sans modèle',
    documentLine: 'La version applicable depuis le {date}, telle que publiée',
    documentNone: 'Aucun texte lu',
    planDate: 'Au {date} : ',
    planSearch: 'chercher « {query} » dans le texte en {language}',
    planArticle: 'lire l’article {number}',
    planDossier: 'ouvrir les versions de l’acte',
    planOther: 'exécuter {operation}',
    planThen: ', puis ',
    planNothing: 'aucune opération à exécuter',
    languageFra: 'français',
    languageEng: 'anglais',
    languageDeu: 'allemand',
    retrievalLine: 'Articles trouvés : {matched} ; gardés : {kept}',
    retrievalMore: 'Articles trouvés : plus de {matched} ; gardés : {kept}',
    retrievalDirect: 'Article lu par son numéro ; gardés : {kept}',
    retrievalPending: 'Décompte…',
    retrievalNone: 'Aucune recherche lancée',
    retrievalNamed: 'La question nomme son article : pas de recherche, lu par son numéro ; gardés : {kept}',
    noticeLine: 'Phase d’essai, non examinée par un avocat. Pas un conseil juridique.',
    checkLine: 'Citations conformes à l’empreinte du texte publié : {verified} sur {quotes}',
    checkNone: 'Rien de cité, rien à vérifier',
    checkPending: 'Vérification…',
    workTime: 'Temps : {seconds} s',
    workCost: 'Coût : environ {cost} € par question',
    rawPlan: 'Plan brut',
    workPlanner: 'Moteur : {planner}',
    workRegistry: 'Registre MLflow : {model}, version {version}',
    workRegistryNone: 'Registre MLflow : {model} (ce déploiement est enregistré à sa mise en service)',
    dockPreview: 'aperçu, pas un conseil juridique',
    dockHide: 'Masquer',
    dockOpen: 'Ouvrir l’assistant',
    dockIntro: 'Posez votre question en français ou en anglais. Lex trouve l’article, le cite mot pour mot et ouvre le texte à cet endroit.',
    dockExampleOne: 'Quel est le délai de prescription d’un contrat d’assurance ?',
    dockExampleOneCaption: 'une question avec vos mots',
    dockExampleTwo: 'Loi du 27 juillet 1997 sur le contrat d’assurance, article 38, 1er juin 2012',
    dockExampleTwoCaption: 'une citation avec une date, ouverte par le code sans appel au modèle',
    dockExampleThree: 'Que dit l’article 5 du RGPD ?',
    dockExampleThreeCaption: 'un article d’un règlement de l’UE',
    dockDoes: 'Ce qu’il fait.',
    dockDoesText: 'Le code lit votre question, trouve les lignes du texte et en tire la réponse. Un modèle d’IA peut décrire une question ou classer des passages\u00A0; il n’écrit rien de ce que vous lisez.',
    dockDoesNot: 'Ce qu’il ne fait pas.',
    dockDoesNotText: 'Il ne donne aucun conseil sur votre situation et, quand il ne trouve pas l’article, il le dit.',
    dockHowLink: 'Comment une réponse est construite, avec les résultats mesurés',
    dockStays: 'Votre conversation reste ici pendant que vous passez d’une page à l’autre.',
    dockAgainAnswer: 'Réponse tirée de l’article {number}. Rouvrir la réponse',
    dockAgainOpened: 'Article {number} ouvert par le code. Le rouvrir',
    dockAgain: 'Rouvrir la réponse',
    dockQuestionLabel: 'Votre question',
    dockPlaceholder: 'Posez une question',
    dockPlaceholderAnother: 'Posez une autre question',
    dockPlaceholderText: 'Posez une question sur un texte',
    dockAsk: 'Demander',
    dockLoadingSearch: 'Lecture de vos mots…',
    dockLoadingAsk: 'Planification de la recherche et lecture du texte…',
    dockFailure: 'L’assistant n’a pas pu être joint.',
    dockTryAgain: 'Réessayer',
    dockOpenedLabel: 'Ouvert par le code, sans appel au modèle',
    dockOpenedLine: 'Article {number}, {title}, tel qu’il se lisait le {date}.',
    dockOpenedLineNoTitle: 'Article {number}, tel qu’il se lisait le {date}.',
    dockOpenedText: '{title}, tel qu’il se lisait le {date}.',
    dockVerdictToday: 'D’après le texte officiel, tel qu’il se lit aujourd’hui',
    dockVerdictOn: 'D’après le texte officiel, tel qu’il se lisait le {date}',
    dockNotes: '{count} notes',
    dockNotesOne: '1 note',
    dockNotesHeading: 'Notes sur cette citation',
    dockCite: 'Article {number}, {title}.',
    dockCiteNoTitle: 'Article {number}.',
    dockWholeOpen: 'L’article entier est ouvert à gauche.',
    dockWholeLink: 'Ouvrir l’article entier',
    dockStripSame: 'Même texte dans les {count} versions détenues',
    dockStripOne: 'Une seule version détenue',
    dockStripChanged: 'Texte modifié le {date}',
    dockBuilt: 'Comment cette réponse a été construite',
    dockBuiltFoot: '{seconds} s, environ {cost} €',
    dockBuiltCost: 'environ {cost} €',
    dockStageMasking: 'Masquage',
    dockStagePlanner: 'Planificateur',
    dockStageValidator: 'Validateur',
    dockStageExecution: 'Exécution',
    dockStageComposer: 'Rédaction',
    dockTagCode: 'code',
    dockTagModel: 'modèle d’IA',
    dockMaskNone: 'rien à masquer',
    dockMaskOne: 'un identifiant masqué avant le modèle',
    dockMaskSome: '{count} identifiants masqués avant le modèle',
    dockOpSearch: 'chercher',
    dockOpRead: 'lire l’article',
    dockOpText: 'lire le texte',
    dockOpVersions: 'lister les versions',
    dockOpHistory: 'lire l’historique de l’article',
    dockOpCompare: 'comparer deux versions',
    dockOpOther: 'exécuter {operation}',
    dockOpThen: ', puis ',
    dockPlanNone: 'aucune opération planifiée',
    dockValidatorAccepted: 'plan accepté, rien de réparé',
    dockExecRead: 'art. {number} lu au {date}',
    dockExecNoSingle: 'recherche faite, pas d’article unique à lire',
    dockExecRefused: '{operation} a répondu par un refus',
    dockExecNone: 'aucune opération exécutée',
    dockExecRan: '{count} opérations exécutées',
    dockComposerOne: 'citation identique au texte',
    dockComposerSome: '{count} citations identiques au texte',
    dockComposerNone: 'rien de cité, chaque phrase tirée d’une formule fixe',
    dockPlanNote: 'Le modèle planifie ; il n’écrit jamais la réponse.',
    dockStageQuestion: 'Question',
    dockStageRoute: 'Aiguillage',
    dockStageRetrieval: 'Recherche',
    dockStageArbiter: 'Classement',
    dockStageExtraction: 'Extraction',
    dockStageChecks: 'Contrôles',
    dockQuestionNone: 'aucun terme du dictionnaire reconnu',
    dockQuestionOne: '1 terme du dictionnaire reconnu\u00A0; réponse cherchée\u00A0: {kind}',
    dockQuestionSome: '{count} termes du dictionnaire reconnus\u00A0; réponse cherchée\u00A0: {kind}',
    dockKindDefinition: 'une définition',
    dockKindRule: 'une règle',
    dockKindDuration: 'un délai',
    dockKindDate: 'une date',
    dockKindAmount: 'un montant',
    dockKindList: 'une liste',
    dockKindReference: 'une disposition',
    dockKindText: 'des passages',
    dockRouteDefinition: 'terme défini par la loi\u00A0: sa ligne, sans modèle',
    dockRouteFact: 'une ligne se détache nettement\u00A0: sans modèle',
    dockRouteRule: 'une règle et ses cas',
    dockRouteList: 'une liste\u00A0: tout le texte parcouru',
    dockRouteOpen: 'passages trouvés, puis lus',
    dockRouteAbsence: 'rien à citer\u00A0: le relevé est montré',
    dockRouteNotHeld: 'un texte que Lex ne détient pas',
    dockRouteGreeting: 'pas une question sur le droit',
    dockRouteOwnCase: 'un cas personnel\u00A0: refusé, la loi à côté',
    dockRetrievalFound: 'le terme figure sur {lines} lignes, dans {articles} articles\u00A0; {kept} retenues',
    dockRetrievalNone: 'aucune ligne du texte ne contient le terme',
    dockRetrievalLine: 'la ligne de définition, par sa place dans le texte',
    dockRetrievalSkipped: 'aucun texte lu',
    dockArbiterLine: 'principaux\u00A0: {primary}\u00A0; complémentaires\u00A0: {supporting}\u00A0; écartés\u00A0: {others}',
    dockExtractionLine: 'lignes désignées\u00A0: {count}, chacune vérifiée dans le texte',
    dockChecksLine: 'citations identiques au texte\u00A0: {count}',
    dockBricksNote: 'Un modèle peut classer des passages et désigner des lignes\u00A0; il n’écrit jamais la réponse.',
    dockBricksNoModel: 'Aucun modèle d’IA n’a été interrogé pour cette réponse.',
    dockDescribedNote: 'Un modèle décrit la question\u00A0; le code choisit quoi exécuter et écrit la réponse.',
    dockNoModelUsed: 'Aucun modèle d’IA n’a contribué à cette réponse.',
    dockQuestionDescribed: 'décrite par un modèle d’IA\u00A0: {asks}',
    dockQuestionByCode: 'lue par le code seul\u00A0: vos mots sont cherchés tels quels',
    dockQuestionCited: 'lue par le code seul\u00A0: l’acte et l’article que vous citez',
    dockAsksCited: 'un article ou un acte est cité',
    dockAsksSubject: 'des mots sur un sujet',
    dockAsksVersions: 'les versions d’un acte',
    dockAsksRecord: 'la fiche d’un acte',
    dockAsksOnDate: 'un acte à une date',
    dockAsksCompared: 'un acte comparé entre deux dates',
    dockAsksPeriod: 'ce qui a changé sur une période',
    dockAsksIdentifier: 'un identifiant exact',
    dockAsksNothing: 'rien à chercher',
    dockAsksAnd: '\u00A0; ',
    dockSaidLabel: 'Ce que Lex a trouvé',
    dockSeePlan: 'Voir le plan',
    dockHidePlan: 'Masquer le plan',
    dockPlanHeading: 'Le plan, tel que renvoyé',
    dockStepsHeading: 'Les étapes exécutées, telles que renvoyées',
    dockNotFound: 'Lex n’a pas trouvé d’article pour ces mots.',
    dockSearched: 'Mots cherchés : « {words} »',
    dockFindByName: 'Trouver un texte par son nom',
    dockFindInLaw: 'Chercher dans cette loi',
    dockClarify: 'Vos mots figurent dans plus d’un texte. Lequel visez-vous ?',
    dockChoice: 'Article {number}, {heading}',
    dockChoiceBare: 'Article {number}',
    dockChoicesPending: 'Lecture des titres…',
    dockAdvice: 'Lex montre le texte de la loi ; il ne dit pas ce qui s’applique à votre situation.',
    dockAdviceExample: 'Une question à laquelle il peut répondre :',
    dockOff: 'L’assistant est désactivé. Les textes, leurs versions et la comparaison fonctionnent toujours : ils n’utilisent aucun modèle.',
    dockCouldNot: 'L’assistant n’a pas pu répondre à cette question : son planificateur a échoué, son plan a été refusé ou le plafond de dépenses du mois est atteint. Les textes, leurs versions et la comparaison fonctionnent toujours : ils n’utilisent aucun modèle.',
});

// One line, so the pseudo-locale scan can swap the tables as it swaps the chrome's (`live-chrome-scan.test.mjs`); pure, so a
// bundle that draws neither panel nor dock (the preview build's) carries none of their words.
export const ASSISTANT_PANEL_COPY = /* @__PURE__ */ Object.freeze({ en: PANEL_EN, fr: PANEL_FR });

export { ASK_FRAGMENT_MAX, askFragment, readAskFragment } from './ask-fragment.mjs';

/** The held language a quotation is shown first in, per interface language; the others are one action away. */
const QUOTE_LANGUAGE = Object.freeze({ en: 'eng', fr: 'fra' });

/** The claims of a quotation that are for checking it (its digest, its permalink, its official source): one action away. */
export const QUOTE_CHECK_TEMPLATES = Object.freeze(['law.content_hash', 'law.permalink', 'law.official_source']);

/**
 * A part's blocks cut into what comes before the first quotation's heading (`lead`) and one group per quotation, each from its
 * `law.quote_heading` claim up to the next, with the held language of the quotation it holds (or null). Nothing is dropped or
 * reordered: the groups, in order, hold every block after the lead.
 */
export function quoteGroups(blocks) {
  const lead = [];
  const groups = [];
  for (const block of blocks) {
    if (block.kind === 'claim' && block.template_id === 'law.quote_heading') {
      groups.push({ blocks: [block], language: null });
    } else if (groups.length === 0) {
      lead.push(block);
    } else {
      const group = groups.at(-1);
      group.blocks.push(block);
      if (block.kind === 'quote' && group.language === null) group.language = block.language;
    }
  }
  return { lead, groups };
}

/**
 * The quotation groups shown first in `locale` (those in its held language, or every group when none is), and the others,
 * which the panel puts one action away. The order within each list is the answer's.
 */
export function groupsForLocale(groups, locale) {
  const language = QUOTE_LANGUAGE[locale];
  const first = groups.filter((group) => group.language === language);
  if (first.length === 0) return { shown: groups, other: [] };
  return { shown: first, other: groups.filter((group) => group.language !== language) };
}

// `quoteParagraphs` lives with the article's heading reader in `law-page.mjs`, which the law page shares (2026-11-14).
export { quoteParagraphs } from './law-page.mjs';

/** The copy key of the verdict in a word (the answer card's first line): found, not found, or out of scope. */
export function verdictWordKey(verdict) {
  if (verdict === 'answer' || verdict === 'answer_with_enrichment') return 'verdictFound';
  return verdict === 'refuse' ? 'verdictOutOfScope' : 'verdictNotFound';
}

/** The act a quotation is from, by its article permalink's first two segments: its publisher, its work and its address. */
export function quoteWork(quote) {
  const match = /^\/(lu-legilux|eu-eurlex)\/([^/]+)\//u.exec(typeof quote?.article_permalink === 'string' ? quote.article_permalink : '');
  return match === null ? null : Object.freeze({ publisher: match[1], work: match[2], address: `/${match[1]}/${match[2]}` });
}

/**
 * The act's own page in `locale`, at one article: a Luxembourg act's law page with the article's history ("article") or the
 * comparison at that article ("changes"), as its hash names them (`LawPage.jsx`); an EU act's page as it is.
 */
export function lawPageHref(where, anchor, kind, locale) {
  const base = `/${locale}${where.address}`;
  return where.publisher === 'lu-legilux' && typeof anchor === 'string' ? `${base}#${kind}-${encodeURIComponent(anchor)}` : base;
}

/**
 * The own titles of the Luxembourg acts an answer quotes, by their address: each act's dossier, asked once, read
 * (`readDossier`) and titled as the law page titles it (`actTitle`). An act whose dossier cannot be read keeps no title, and
 * its card names no act: never an address in its place.
 */
export async function actTitlesOf(dossier, fetchImpl) {
  const addresses = [...new Set((dossier?.parts ?? [])
    .flatMap((part) => part.blocks ?? [])
    .filter((block) => block?.kind === 'quote')
    .map(quoteWork)
    .filter((where) => where !== null && where.publisher === 'lu-legilux')
    .map((where) => where.address))];
  const titles = {};
  await Promise.all(addresses.map(async (address) => {
    try {
      const response = await fetchImpl('/api/v3/dossier', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ operation_id: 'dossier', parameters: { identifier: address } }),
      });
      const envelope = await response.json();
      const dossierView = readDossier(envelope?.result?.value);
      const named = actTitle(dossierView);
      // With the act's versions in one language, counted, and the year of the first (the answer's "N versions since YYYY").
      const versions = versionsOf(dossierView);
      if (named !== null) titles[address] = Object.freeze({ ...named, versions: versions.length, since: yearOf(versions[0]?.applicabilityDate) });
    } catch {
      // No title: the card names no act.
    }
  }));
  return titles;
}

/** The panel's copy in a locale it holds, or a thrown error: it never falls back to another language (R43). */
export function panelCopy(locale) {
  if (!Object.hasOwn(ASSISTANT_PANEL_COPY, locale)) throw new Error(`the assistant panel holds no copy in ${locale}`);
  return ASSISTANT_PANEL_COPY[locale];
}

/** `{name}` replaced by its value, once, from the template; a value is never read again as a placeholder. */
export function fill(template, values) {
  return template.replace(/\{([a-zA-Z]+)\}/g, (marker, name) => {
    if (!Object.hasOwn(values, name)) throw new Error(`no value for ${marker}`);
    return String(values[name]);
  });
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function requireText(block, where) {
  if (!isObject(block) || !TEXT_KINDS.includes(block.kind) || typeof block.text !== 'string' || block.text.length === 0) {
    throw new Error(`${where}: a visible text is a claim, a quote or a span of the question, with its text`);
  }
  if (block.kind === 'claim' && (typeof block.template_id !== 'string' || !['fr', 'en'].includes(block.locale))) {
    throw new Error(`${where}: a claim names its template and its locale`);
  }
  if (block.kind === 'quote' && !/^[0-9a-f]{64}$/.test(block.article_text_sha256 ?? '')) {
    throw new Error(`${where}: a quotation carries the hash of the text it is taken from`);
  }
  if (block.kind === 'question_span' && !(Number.isInteger(block.start) && Number.isInteger(block.end) && block.end > block.start)) {
    throw new Error(`${where}: a span of the question names where it is`);
  }
  return block;
}

/**
 * An answer_dossier/1, checked before it is drawn: its schema, a verdict of the six, a plan shown first, every visible text
 * typed. Throws rather than drawing something the contract does not hold. Returns the parts the panel draws, in order.
 */
export function validateDossier(dossier) {
  if (!isObject(dossier) || dossier.schema !== DOSSIER_SCHEMA) throw new Error('not an answer_dossier/1');
  if (!VERDICTS.includes(dossier.verdict)) throw new Error(`not one of the six verdicts: ${dossier.verdict}`);
  if (PRESENTATION_RESULTS.includes(dossier.verdict)) throw new Error('a presentation result is never a verdict');
  if (!['fr', 'en'].includes(dossier.answer_locale)) throw new Error('an answer is in French or English');
  const label = requireText(dossier.label, 'label');
  const notices = (dossier.notices ?? []).map((notice, at) => requireText(notice, `notice ${at}`));
  if (notices.length < 2) throw new Error('an answer carries its two notices (R33, R50)');
  if (!isObject(dossier.plan) || !Array.isArray(dossier.plan.rendered) || dossier.plan.rendered.length === 0) {
    throw new Error('an answer shows its plan first (R45(c))');
  }
  const plan = dossier.plan.rendered.map((line, at) => requireText(line, `plan line ${at}`));
  if (!Array.isArray(dossier.parts) || dossier.parts.length < 1 || dossier.parts.length > 2) throw new Error('an answer has one or two parts');
  const parts = dossier.parts.map((part, at) => {
    if (!isObject(part) || !Array.isArray(part.blocks)) throw new Error(`part ${at} has no blocks`);
    return {
      partId: part.part_id,
      verdict: part.verdict,
      blocks: part.blocks.map((block, index) => {
        const where = `part ${at} block ${index}`;
        if (!isObject(block) || !BLOCK_KINDS.includes(block.kind)) throw new Error(`${where}: not a block kind of the contract`);
        if (TEXT_KINDS.includes(block.kind)) return requireText(block, where);
        if (!isObject(block.claim)) throw new Error(`${where}: a ${block.kind} carries its sentence as a claim`);
        requireText(block.claim, `${where} claim`);
        return block;
      }),
    };
  });
  if (!isObject(dossier.trace)) throw new Error('an answer carries its trace (R32)');
  return { verdict: dossier.verdict, locale: dossier.answer_locale, label, notices, plan, parts, trace: dossier.trace };
}

/**
 * The two polite announcements an answer makes (R45(d)): one when its verdict is known, one when it is complete. Nothing
 * else is announced, and nothing is announced as an alert.
 */
export function announcements(view, locale) {
  const copy = panelCopy(locale);
  return [fill(copy.announceVerdict, { label: view.label.text }), copy.announceComplete];
}

/**
 * The typed context a follow-up question carries from the last answer's trace (R45(e)): the act and the date the trace
 * bound, each a removable chip, at most two. Earlier question text is never among them.
 */
export function contextChips(trace) {
  const chips = [];
  for (const step of trace?.steps ?? []) {
    const parameters = step?.parameters ?? {};
    if (typeof parameters.identifier === 'string' && parameters.identifier.startsWith('/') && !chips.some((chip) => chip.kind === 'work')) {
      chips.push({ kind: 'work', value: parameters.identifier });
    }
    if (typeof parameters.date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(parameters.date) && !chips.some((chip) => chip.kind === 'date')) {
      chips.push({ kind: 'date', value: parameters.date });
    }
  }
  return chips.slice(0, MAXIMUM_CONTEXT_CHIPS);
}

/** What a follow-up sends: the new question and the chips the reader kept, never an earlier question (R45(e)). */
export function followUpRequest(question, chips) {
  if (typeof question !== 'string' || question.trim().length === 0) throw new Error('a question is not blank');
  const kept = (chips ?? []).slice(0, MAXIMUM_CONTEXT_CHIPS).map((chip) => {
    if (!['work', 'date'].includes(chip?.kind) || typeof chip.value !== 'string') throw new Error('a chip is a typed work or date');
    return { kind: chip.kind, value: chip.value };
  });
  return { question, context: kept };
}

/** A chip's label in the panel's words. */
export function chipLabel(chip, locale) {
  const copy = panelCopy(locale);
  return fill(chip.kind === 'work' ? copy.chipWork : copy.chipDate, { value: chip.value });
}

/** What the panel sends: ask/1's request, the question alone. */
export function askRequest(question) {
  return { operation_id: 'ask', parameters: { question } };
}

/**
 * What an `ask` envelope gives the panel to draw: `{ dossier }`, the answer_dossier/1 a result carries, or the one a refusal
 * `advice_boundary` carries in its payload (a refuse travels as that refusal); or `{ dossier: null, sentence }`, the sentence
 * of a presentation result (a type apart, R45(a)), of ask's own limit, or of an answer that could not be read. The panel is
 * shown only while the assistant is on, so the switched-off card there means it could not answer this one question.
 */
export function askOutcome(envelope, locale) {
  const copy = panelCopy(locale);
  const value = envelope?.result?.value;
  // The build that answered, as the envelope names it, so the work can name the registry's version of that very build
  // (`registryVersionFor`; the review of #1060); null when it names none.
  const commit = envelope?.context?.commits?.serving_runtime_source_commit;
  const servingCommit = typeof commit === 'string' ? commit : null;
  if (envelope?.result?.object_type === 'answer_dossier' && value?.schema === DOSSIER_SCHEMA) return { dossier: value, servingCommit };
  const payload = envelope?.refusal?.helpful_payload;
  if (envelope?.refusal?.code === 'advice_boundary' && payload?.answer_dossier?.schema === DOSSIER_SCHEMA) return { dossier: payload.answer_dossier, servingCommit };
  if (envelope?.refusal?.code === 'rate_limited') return { dossier: null, sentence: copy.rateLimited };
  if (value?.presentation_result === 'localization_unavailable' && typeof value?.sentences?.[locale] === 'string') {
    return { dossier: null, sentence: value.sentences[locale] };
  }
  return { dossier: null, sentence: copy.failed };
}

// ---------------------------------------------------------------------------------------------------------------------------
// The docked assistant (the owner's brief of 2026-11-15, sections 5 and 6): what it makes of an ask answer, and the five
// stages of the work it draws from the answer's trace. Pure functions; the dock (`AssistantDock.jsx`) asks and draws.

/**
 * The turn an ask answer makes (`askV3`'s result): `{ kind, dossier, servingCommit, sentence }`. A dossier is checked
 * (`validateDossier`) before it is kept; an answer that quotes nothing is "not found"; a refuse is the advice boundary; the
 * switched-off card (the question not read) is told from the card of a question the assistant could not answer (read: the
 * planner failed, its plan was refused, a gate failed, or the month's cap is reached, which the API does not tell apart).
 */
export function dockTurnOf(asked, locale) {
  if (asked?.state !== 'success' && asked?.state !== 'refusal') return Object.freeze({ kind: 'failure' });
  const envelope = asked.envelope;
  const commit = envelope?.context?.commits?.serving_runtime_source_commit;
  const servingCommit = typeof commit === 'string' ? commit : null;
  const kept = (dossier, kind = null) => {
    try {
      const view = validateDossier(dossier);
      return Object.freeze({ kind: kind ?? dossierKind(view), dossier, servingCommit });
    } catch {
      return Object.freeze({ kind: kind ?? 'failure', dossier: null, servingCommit });
    }
  };
  if (asked.state === 'refusal') {
    const code = envelope?.refusal?.code;
    if (code === 'rate_limited') return Object.freeze({ kind: 'rate' });
    if (code === 'advice_boundary') return kept(envelope.refusal.helpful_payload?.answer_dossier ?? null, 'advice');
    return Object.freeze({ kind: 'couldnot' });
  }
  const value = envelope?.result?.value;
  if (envelope?.result?.object_type === 'answer_dossier') return kept(value);
  if (value?.presentation_result === 'localization_unavailable') {
    const sentence = value?.sentences?.[locale];
    return Object.freeze({ kind: 'locale', sentence: typeof sentence === 'string' ? sentence : null });
  }
  if (value?.presentation_result === 'assistant_v3_unavailable') return Object.freeze({ kind: value.question_read === true ? 'couldnot' : 'off' });
  return Object.freeze({ kind: 'failure' });
}

/** The kind of a checked answer: clarify, the advice boundary (a refuse), an answer when it quotes the law, else not found. */
export function dossierKind(view) {
  if (view.verdict === 'refuse') return 'advice';
  const quotes = view.parts.some((part) => part.blocks.some((block) => block.kind === 'quote'));
  // A bricks answer (plan /2) that quotes nothing still says something in the server's own sentences (a sweep that proves
  // an absence, a text Lex does not hold, a greeting): the dock shows those sentences, kind `said`.
  if (isObject(view.trace?.plan?.bricks) && view.trace.plan.bricks.route !== 'operations') return quotes ? 'answer' : 'said';
  if (view.verdict === 'clarify') return 'clarify';
  return quotes ? 'answer' : 'notfound';
}

/**
 * The bricks record of an answer the tables gave (its trace's plan /2), or null for an answer of operations: one a model
 * described and code mapped to operations (`questionCallOf`), or one from before the bricks, which a model planned.
 */
export function bricksOf(dossier) {
  const bricks = dossier?.trace?.plan?.bricks;
  return isObject(bricks) && bricks.route !== 'operations' ? bricks : null;
}

/**
 * For an answer of operations under plan /2: how the question was read, `{ outcome, asks }`. `outcome` is `described` when
 * a model described the question (then `asks` holds what it said each part asks), else why code read it alone
 * (`not_wired`, `failed`, `refused`). Null for a bricks answer and for an answer from before the bricks.
 */
export function questionCallOf(dossier) {
  const bricks = dossier?.trace?.plan?.bricks;
  if (!isObject(bricks) || bricks.route !== 'operations') return null;
  const asks = (Array.isArray(bricks.described) ? bricks.described : []).map((part) => part?.asks).filter((value) => typeof value === 'string');
  const outcome = ['described', 'not_wired', 'failed', 'refused', 'cited'].includes(bricks.question_call) ? bricks.question_call : 'not_wired';
  return Object.freeze({ outcome, asks: Object.freeze(outcome === 'described' ? asks : []) });
}

/** The sentence under the stages: what a model did for this answer, in one line. */
export function builtNote(dossier, rows, locale) {
  const copy = panelCopy(locale);
  const call = questionCallOf(dossier);
  if (bricksOf(dossier) === null && call === null) return copy.dockPlanNote;
  if (rows.some((row) => row.model && row.key !== 'question')) return copy.dockBricksNote;
  if (rows.some((row) => row.model)) return copy.dockDescribedNote;
  return call === null || call.outcome === 'not_wired' || call.outcome === 'cited' ? copy.dockBricksNoModel : copy.dockNoModelUsed;
}

/** A quotation's lines: its text cut where a line of the law starts (`breaks`), each trimmed; one line when it has none. */
export function quoteLines(quote) {
  const text = typeof quote?.text === 'string' ? quote.text : '';
  const breaks = Array.isArray(quote?.breaks) ? quote.breaks.filter((at) => Number.isInteger(at) && at > 0 && at < text.length) : [];
  const lines = [];
  let from = 0;
  for (const at of [...breaks, text.length]) {
    const line = text.slice(from, at).trim();
    if (line.length > 0) lines.push(line);
    from = at;
  }
  return Object.freeze(lines);
}

/**
 * What a bricks answer says, in its own order, for the dock to draw: `line` (a sentence that introduces or concludes),
 * `quote` (a line of the law, with its article's heading and date as its heading sentence carries them), `value` (the
 * words written on the quoted line), `remark` (a caveat, a note on the reader's words, an official source) and `more`
 * (further articles to read). Every text is the server's own sentence or the law's own words; nothing is composed here.
 */
export function bricksBody(dossier) {
  const body = [];
  let heading = null;
  for (const block of (dossier?.parts ?? []).flatMap((part) => part.blocks ?? [])) {
    if (block?.kind === 'claim') {
      const id = String(block.template_id ?? '');
      if (id === 'law.extract_heading' || id === 'law.quote_heading') {
        heading = block;
      } else if (id === 'answer.value_words') {
        body.push(Object.freeze({ type: 'value', text: block.text }));
      } else if (id.startsWith('caveat.') || id.startsWith('note.') || id === 'law.official_source' && heading === null && !body.some((entry) => entry.type === 'quote')) {
        body.push(Object.freeze({ type: 'remark', text: block.text }));
      } else if (!id.startsWith('law.') && !id.startsWith('boundary.')) {
        body.push(Object.freeze({ type: 'line', text: block.text, template: id }));
      }
    } else if (block?.kind === 'quote') {
      const fact = (name) => heading?.facts?.find((candidate) => candidate?.name === name)?.value ?? null;
      body.push(Object.freeze({ type: 'quote', quote: block, lines: quoteLines(block), heading: fact('heading'), date: fact('date') }));
      heading = null;
    } else if (block?.kind === 'action' && block.operation_id === 'evidence_bundle' && typeof block.parameters?.article === 'string') {
      const article = Object.freeze({ identifier: block.parameters.identifier, article: block.parameters.article, date: block.parameters.date ?? null });
      const last = body.at(-1);
      if (last?.type === 'more') last.articles.push(article);
      else body.push({ type: 'more', articles: [article] });
    } else if (block?.kind === 'handoff' && typeof block.claim?.text === 'string') {
      body.push(Object.freeze({ type: 'remark', text: block.claim.text }));
    }
  }
  return Object.freeze(body);
}

/** The quotation the answer card shows: the first in the interface's held language, else the first. Null when none. */
export function leadQuote(dossier, locale) {
  const quotes = (dossier?.parts ?? []).flatMap((part) => part.blocks ?? []).filter((block) => block?.kind === 'quote');
  return quotes.find((quote) => quote.language === QUOTE_LANGUAGE[locale]) ?? quotes[0] ?? null;
}

/** The notes of a quotation (its disclosures' sentences, as the server wrote them), else every disclosure of the answer. */
export function quoteNotes(dossier, quote) {
  const disclosures = (dossier?.parts ?? []).flatMap((part) => part.blocks ?? []).filter((block) => block?.kind === 'disclosure' && typeof block.claim?.text === 'string');
  const own = disclosures.filter((block) => block.object === quote?.quote_id);
  return (own.length > 0 ? own : disclosures).map((block) => block.claim.text);
}

/** The date an answer read the law at: its quotation heading's `date` fact, else the step that read the article. */
export function answerDate(dossier, quote) {
  const heading = (dossier?.parts ?? []).flatMap((part) => part.blocks ?? [])
    .find((block) => block?.kind === 'claim' && block.template_id === 'law.quote_heading' && block.facts?.some((fact) => fact?.name === 'anchor' && fact.value === quote?.anchor));
  const fact = heading?.facts?.find((candidate) => candidate?.name === 'date' && candidate?.kind === 'calendar_date');
  return typeof fact?.value === 'string' ? fact.value : executionOf(dossier?.trace).read?.date ?? null;
}

/**
 * The candidates of a clarify, as its actions name them (the text, the article, the date), at most five, each once: the
 * dock draws each by the text's title and the article's heading, never by its address.
 */
export function clarifyCandidates(dossier) {
  const seen = new Set();
  const candidates = [];
  for (const block of (dossier?.parts ?? []).flatMap((part) => part.blocks ?? [])) {
    const parameters = block?.kind === 'action' ? block.parameters : null;
    if (typeof parameters?.identifier !== 'string') continue;
    const key = `${parameters.identifier}|${parameters.article ?? ''}`;
    if (seen.has(key)) continue;
    seen.add(key);
    candidates.push(Object.freeze({
      identifier: parameters.identifier,
      article: typeof parameters.article === 'string' ? parameters.article : null,
      date: typeof parameters.date === 'string' ? parameters.date : null,
    }));
  }
  return Object.freeze(candidates.slice(0, 5));
}

/** The words the answer's search looked for (its trace's search step), else the question. */
export function searchedWords(dossier, question) {
  return searchStepOf(dossier?.trace)?.query ?? question ?? '';
}

/** The plan in a few words, from its operations (`plannedOperations`): "search, then read the article". */
function planLine(trace, copy) {
  const said = plannedOperations(trace).map(({ operation, followUp, article }) => {
    if (operation === 'search') return copy.dockOpSearch;
    if (operation === 'evidence_bundle') return followUp || article ? copy.dockOpRead : copy.dockOpText;
    if (operation === 'as_of') return copy.dockOpText;
    if (operation === 'dossier' || operation === 'timeline') return copy.dockOpVersions;
    if (operation === 'article_history') return copy.dockOpHistory;
    if (operation === 'diff') return copy.dockOpCompare;
    return fill(copy.dockOpOther, { operation });
  });
  return said.length === 0 ? copy.dockPlanNone : said.join(copy.dockOpThen);
}

/** What the steps ran, in one line: the article read and its date, else why none was read. */
function executionLine(trace, copy) {
  const run = executionOf(trace);
  if (run.read !== null) return fill(copy.dockExecRead, { number: articleNumberOf(run.read.article), date: numericDate(run.read.date ?? trace?.today ?? '') });
  if (run.refused !== null) return fill(copy.dockExecRefused, { operation: run.refused });
  if (run.fired === 0) return copy.dockExecNone;
  const searched = (trace?.steps ?? []).some((step) => step?.operation_id === 'search' && step?.fired !== false);
  if (run.unfired > 0 && searched) return copy.dockExecNoSingle;
  return fill(copy.dockExecRan, { count: run.fired });
}

/**
 * The five stages of an answer's work (the brief's section 5), each `{ key, name, line, model }`: masking, the planner (the
 * one AI model call), the validator, the execution and the composer, each line read from the trace and the answer's gates.
 */
export function stageRows(dossier, locale) {
  const copy = panelCopy(locale);
  const trace = dossier?.trace ?? {};
  const bricks = bricksOf(dossier);
  if (bricks !== null) return bricksStageRows(dossier, bricks, copy);
  const masked = maskedCount(trace.question ?? '');
  const quotes = Number.isInteger(dossier?.gates?.quotes)
    ? dossier.gates.quotes
    : (dossier?.parts ?? []).flatMap((part) => part.blocks ?? []).filter((block) => block?.kind === 'quote').length;
  const masking = masked === 0 ? copy.dockMaskNone : masked === 1 ? copy.dockMaskOne : fill(copy.dockMaskSome, { count: masked });
  const composer = quotes === 0 ? copy.dockComposerNone : quotes === 1 ? copy.dockComposerOne : fill(copy.dockComposerSome, { count: quotes });
  // Under plan /2 a model describes the question and code chooses the operations; before the bricks a model planned them.
  const call = questionCallOf(dossier);
  const asked = {
    article_or_act_cited: copy.dockAsksCited, words_on_a_subject: copy.dockAsksSubject, versions_of_an_act: copy.dockAsksVersions,
    record_of_an_act: copy.dockAsksRecord, act_on_a_date: copy.dockAsksOnDate, two_dates_compared: copy.dockAsksCompared,
    changes_in_a_period: copy.dockAsksPeriod, exact_identifier: copy.dockAsksIdentifier, nothing_to_look_up: copy.dockAsksNothing,
  };
  const planning = call === null
    ? [Object.freeze({ key: 'planner', name: copy.dockStagePlanner, line: planLine(trace, copy), model: true })]
    : [
      Object.freeze({
        key: 'question',
        name: copy.dockStageQuestion,
        line: call.outcome === 'described' && call.asks.length > 0
          ? fill(copy.dockQuestionDescribed, { asks: call.asks.map((asks) => asked[asks] ?? copy.dockAsksSubject).join(copy.dockAsksAnd) })
          : call.outcome === 'cited' ? copy.dockQuestionCited : copy.dockQuestionByCode,
        model: call.outcome === 'described' && call.asks.length > 0,
      }),
      Object.freeze({ key: 'route', name: copy.dockStageRoute, line: planLine(trace, copy), model: false }),
    ];
  return Object.freeze([
    Object.freeze({ key: 'masking', name: copy.dockStageMasking, line: masking, model: false }),
    ...planning,
    Object.freeze({ key: 'validator', name: copy.dockStageValidator, line: copy.dockValidatorAccepted, model: false }),
    Object.freeze({ key: 'execution', name: copy.dockStageExecution, line: executionLine(trace, copy), model: false }),
    Object.freeze({ key: 'composer', name: copy.dockStageComposer, line: composer, model: false }),
  ]);
}

/**
 * The stages of a bricks answer (V4), each read from the bricks record in the answer's trace: masking, the question's row,
 * the route code chose, retrieval, then the two model steps only when a model was asked, and the checks. A stage is tagged
 * `model` only when a model call is recorded for it.
 */
function bricksStageRows(dossier, bricks, copy) {
  const masked = maskedCount(dossier?.trace?.question ?? '');
  const masking = masked === 0 ? copy.dockMaskNone : masked === 1 ? copy.dockMaskOne : fill(copy.dockMaskSome, { count: masked });
  const kinds = { definition: copy.dockKindDefinition, rule: copy.dockKindRule, duration: copy.dockKindDuration, date: copy.dockKindDate, amount: copy.dockKindAmount, list: copy.dockKindList, reference: copy.dockKindReference, text: copy.dockKindText };
  const terms = (Array.isArray(bricks.concepts) ? bricks.concepts.length : 0) + (Array.isArray(bricks.roles) ? bricks.roles.length : 0);
  const kind = kinds[bricks.kind] ?? copy.dockKindText;
  const question = terms === 0 ? copy.dockQuestionNone : terms === 1 ? fill(copy.dockQuestionOne, { kind }) : fill(copy.dockQuestionSome, { count: terms, kind });
  const routes = { definition: copy.dockRouteDefinition, fact: copy.dockRouteFact, rule: copy.dockRouteRule, list: copy.dockRouteList, open: copy.dockRouteOpen, absence: copy.dockRouteAbsence, not_held: copy.dockRouteNotHeld, greeting: copy.dockRouteGreeting, own_case: copy.dockRouteOwnCase };
  const found = (Array.isArray(bricks.detectors) ? bricks.detectors : []).find((detector) => detector?.id === 'keyword');
  const candidates = Array.isArray(bricks.candidates) ? bricks.candidates : [];
  const retrieval = bricks.route === 'definition' ? copy.dockRetrievalLine
    : typeof bricks.work !== 'string' || !Number.isInteger(bricks.lines) ? copy.dockRetrievalSkipped
    : found === undefined || found.lines === 0 ? copy.dockRetrievalNone
    : fill(copy.dockRetrievalFound, { lines: found.lines, articles: found.articles, kept: candidates.length });
  const calls = (Array.isArray(bricks.model_calls) ? bricks.model_calls : []).map((call) => call?.step);
  const quotes = Number.isInteger(dossier?.gates?.quotes) ? dossier.gates.quotes : 0;
  const rows = [
    { key: 'masking', name: copy.dockStageMasking, line: masking, model: false },
    { key: 'question', name: copy.dockStageQuestion, line: question, model: calls.includes('question') },
    { key: 'route', name: copy.dockStageRoute, line: routes[bricks.route] ?? copy.dockRouteOpen, model: false },
    { key: 'retrieval', name: copy.dockStageRetrieval, line: retrieval, model: false },
  ];
  if (calls.includes('arbiter')) {
    const count = (role) => candidates.filter((candidate) => candidate?.role === role).length;
    rows.push({ key: 'arbiter', name: copy.dockStageArbiter, line: fill(copy.dockArbiterLine, { primary: count('primary'), supporting: count('supporting'), others: count('tangential') + count('discarded') }), model: true });
  }
  if (calls.includes('extraction')) {
    rows.push({ key: 'extraction', name: copy.dockStageExtraction, line: fill(copy.dockExtractionLine, { count: Array.isArray(bricks.answer?.items) ? bricks.answer.items.length : 0 }), model: true });
  }
  rows.push({ key: 'checks', name: copy.dockStageChecks, line: quotes === 0 ? copy.dockComposerNone : fill(copy.dockChecksLine, { count: quotes }), model: false });
  return Object.freeze(rows.map((row) => Object.freeze(row)));
}

/** A number of seconds or euros as the dock writes it in `locale` (1,6 in French, 1.6 in English). */
export function dockDecimal(value, locale, digits) {
  return Number(value).toLocaleString(locale === 'fr' ? 'fr-FR' : 'en-GB', { minimumFractionDigits: digits, maximumFractionDigits: digits });
}

/**
 * The work's foot: the answer's duration as the page measured it (the trace holds none) and its cost, which the trace does
 * not hold either: the latest test run's average cost of an answer (`EVALUATION_RECORD`), said as "about".
 */
export function builtFoot(seconds, costEur, locale) {
  const copy = panelCopy(locale);
  const cost = dockDecimal(costEur, locale, 3);
  return typeof seconds === 'number' && Number.isFinite(seconds)
    ? fill(copy.dockBuiltFoot, { seconds: dockDecimal(seconds, locale, 1), cost })
    : fill(copy.dockBuiltCost, { cost });
}

/** The strip's sentence: the same wording in all N versions held, one version held, or the date it last changed. */
export function stripSentence(strip, locale) {
  const copy = panelCopy(locale);
  if (strip.changedOn !== null) return fill(copy.dockStripChanged, { date: numericDate(strip.changedOn) });
  return strip.ticks.length === 1 ? copy.dockStripOne : fill(copy.dockStripSame, { count: strip.ticks.length });
}

/** The three examples of the dock's first screen, in `locale`'s table: each question, its caption and its language. */
export function dockExamples(locale) {
  const copy = panelCopy(locale);
  const languages = locale === 'fr' ? ['fr', 'fr', 'fr'] : ['fr', 'fr', 'en'];
  return Object.freeze([
    Object.freeze({ question: copy.dockExampleOne, caption: copy.dockExampleOneCaption, lang: languages[0] }),
    Object.freeze({ question: copy.dockExampleTwo, caption: copy.dockExampleTwoCaption, lang: languages[1] }),
    Object.freeze({ question: copy.dockExampleThree, caption: copy.dockExampleThreeCaption, lang: languages[2] }),
  ]);
}
