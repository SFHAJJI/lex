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
// panel's own words (its heading, its field, its two announcements) in French and English, and the R44 notice, which is the
// server's `notice.input` template word for word (a test reads both).

export const ASSISTANT_PANEL = 'hidden';

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
    inputLabel: 'Your question',
    submit: 'Ask',
    contextLabel: 'Kept from your last answer',
    removeChip: 'Remove {chip}',
    chipWork: 'Act: {value}',
    chipDate: 'Date: {value}',
    answerHeading: 'Answer',
    quoteLabel: 'Published text',
    detailsSummary: 'Details for checking: hashes and the trace',
    announceVerdict: 'Answer: {label}.',
    announceComplete: 'The answer is complete.',
  }),
  fr: Object.freeze({
    heading: 'Assistant',
    notice:
      'Ceci est un assistant automatisé qui montre des textes juridiques publiés. Il ne donne pas de conseil juridique et n’applique pas le droit à votre situation. N’écrivez ni noms, ni données de santé, ni autres données personnelles.',
    inputLabel: 'Votre question',
    submit: 'Demander',
    contextLabel: 'Repris de la dernière réponse',
    removeChip: 'Retirer {chip}',
    chipWork: 'Acte : {value}',
    chipDate: 'Date : {value}',
    answerHeading: 'Réponse',
    quoteLabel: 'Texte publié',
    detailsSummary: 'Éléments de vérification : empreintes et trace',
    announceVerdict: 'Réponse : {label}.',
    announceComplete: 'La réponse est complète.',
  }),
});

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
