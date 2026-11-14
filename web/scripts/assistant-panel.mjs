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
import { actTitle } from './law-page.mjs';

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

export const ASSISTANT_PANEL_COPY = Object.freeze({
  en: Object.freeze({
    heading: 'Assistant',
    notice:
      'This is an automated assistant that shows published legal texts. It does not give legal advice or apply the law to your situation. Do not type names, health details or other personal details.',
    preview: 'Preview: not yet reviewed by a lawyer or by independent annotators.',
    processor:
      'Your question, with the e-mail addresses, phone numbers, bank account numbers and identity numbers that Lex\'s code recognises removed, is sent to Microsoft\'s Azure OpenAI service (EU data zone), which plans which of Lex\'s operations to run; nothing else about you is sent. Microsoft does not use it to train models; for abuse monitoring, Microsoft may keep it for up to 30 days. Lex keeps nothing.',
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
  }),
  fr: Object.freeze({
    heading: 'Assistant',
    notice:
      'Ceci est un assistant automatisé qui montre des textes juridiques publiés. Il ne donne pas de conseil juridique et n’applique pas le droit à votre situation. N’écrivez ni noms, ni données de santé, ni autres données personnelles.',
    preview: 'Phase d’essai : l’assistant n’a pas encore été examiné par un avocat ni par des annotateurs indépendants.',
    processor:
      'Votre question, dont sont retirés les adresses électroniques, numéros de téléphone, numéros de compte bancaire et numéros d’identification que reconnaît le code de Lex, est envoyée au service Azure OpenAI de Microsoft (zone de données de l’UE), qui planifie les opérations de Lex à exécuter ; rien d’autre vous concernant n’est envoyé. Microsoft ne l’utilise pas pour entraîner des modèles ; aux fins de sa surveillance des abus, Microsoft peut la conserver jusqu’à 30 jours. Lex ne conserve rien.',
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
  }),
});

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
      const named = actTitle(readDossier(envelope?.result?.value));
      if (named !== null) titles[address] = named;
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
  if (envelope?.result?.object_type === 'answer_dossier' && value?.schema === DOSSIER_SCHEMA) return { dossier: value };
  const payload = envelope?.refusal?.helpful_payload;
  if (envelope?.refusal?.code === 'advice_boundary' && payload?.answer_dossier?.schema === DOSSIER_SCHEMA) return { dossier: payload.answer_dossier };
  if (envelope?.refusal?.code === 'rate_limited') return { dossier: null, sentence: copy.rateLimited };
  if (value?.presentation_result === 'localization_unavailable' && typeof value?.sentences?.[locale] === 'string') {
    return { dossier: null, sentence: value.sentences[locale] };
  }
  return { dossier: null, sentence: copy.failed };
}
