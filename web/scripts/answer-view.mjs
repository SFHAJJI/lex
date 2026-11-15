// The answer screen's reading of an answer (the owner's order of 2026-11-14, evening: one sentence that answers, one line that
// cites it, three ways on, and the work shown on demand). Pure functions over an answer_dossier/1 and the answers of the
// site's own operations; nothing here asks the model, and nothing here changes a word of the law: the sentence shown is a
// span of the quotation's own characters, cut where `quoteParagraphs` cuts and at a sentence's end, and what is marked in it
// is decided by the question's own words.

import { articleHeadingOf, quoteParagraphs } from './law-page.mjs';

// The words a question and a law share that say nothing of what is asked: articles, pronouns, prepositions, auxiliaries.
const STOP = new Set(`
a au aux avec ce ces cet cette d de des du dans elle en est et il ils je la le les leur lui ma mais me mon ne ni nous on ou
par pas pour qu que qui sa se ses si son sont sur ta te tes ton tu un une vos votre vous y l s c n m t j quel quelle quels
quelles quoi dont tout toute tous toutes etre avoir ont a-t-il est-ce peut doit faut cas lors entre sans sous apres avant
the of and or to in on at by for from with as is are be been was were this that these those it its an a any which what when
who whom how does do did shall may must can under say says said about whether there their than then into upon such
`.trim().split(/\s+/u));

/** A word as the two texts are compared: no accents, lower case, its elided article ("l'", "d'", "qu'") taken off. */
export function wordKey(word) {
  return word.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().replace(/[’']/gu, "'").replace(/^(?:l|d|qu|j|n|s|c|m|t)'/u, '');
}

const WORD = /[\p{L}\p{N}][\p{L}\p{N}’'-]*/gu;

/** The words of a text that carry meaning, as keys, the first five letters of each (so "actions" meets "action"). */
export function contentKeys(text) {
  return new Set([...String(text).matchAll(WORD)].map(([word]) => wordKey(word)).filter((key) => key.length > 1 && !STOP.has(key)).map((key) => key.slice(0, 5)));
}

/** A paragraph's or point's number at the start of a span ("1.", "(2)", "a)"), which the sentence shown starts after. */
const LEADING_NUMBER = /^(?:\d{1,3}\.|\(\d{1,3}\)|\([a-z]\)|[a-z]\))\s*/u;

/** The sentences of a quotation, each a span of its own characters: its paragraphs, each cut after a sentence's stop. */
export function sentencesOf(text) {
  return quoteParagraphs(text).flatMap((paragraph) => paragraph.split(/(?<=[.;])\s+(?=\p{Lu})/u)).map((sentence) => sentence.replace(LEADING_NUMBER, '')).filter((sentence) => sentence.length > 0);
}

/**
 * The sentences of a quotation that are its text: neither its number ("Article 44") nor its heading (`articleHeadingOf`),
 * nor a number and heading run together ("Article 5 Principles relating to processing of personal data"): a sentence of the
 * text ends on its stop, a colon, a semicolon or a closing mark; a heading does not.
 */
function textSentencesOf(text) {
  const heading = articleHeadingOf(text);
  return sentencesOf(text).filter((sentence) => sentence !== heading && !/^Art(?:icle|\.)?\s*[\w-]+\.?$/iu.test(sentence) && /[.;:!?)»”"’]$/u.test(sentence));
}

const wordCount = (text) => [...String(text).matchAll(WORD)].length;

/**
 * The sentence of a quotation that answers a question, as `{ parts }`: the sentence's characters in order, each part
 * `{ text, mark }`, `mark` true where the sentence says what the question does not (its own words, outside the question's,
 * in one run or more, when they are few against what it shares with the question). The sentence is the one that shares the
 * most of the question's words (two at least); with none, the first sentence of six words or more, unmarked, and one ending
 * on a colon is shown with the next. Null for a quotation with no such sentence.
 */
export function answerSentence(text, question) {
  const sentences = textSentencesOf(text);
  const asked = contentKeys(question);
  let best = null;
  let bestScore = 1;
  for (const sentence of sentences) {
    if (wordCount(sentence) < 6) continue;
    const shared = [...contentKeys(sentence)].filter((key) => asked.has(key)).length;
    if (shared > bestScore) {
      best = sentence;
      bestScore = shared;
    }
  }
  if (best === null) {
    const at = sentences.findIndex((sentence) => wordCount(sentence) >= 6 || (sentence.endsWith(':') && wordCount(sentence) >= 3));
    if (at < 0) return null;
    const shown = sentences[at].endsWith(':') && at + 1 < sentences.length ? [sentences[at], sentences[at + 1]] : [sentences[at]];
    return Object.freeze({ parts: Object.freeze(shown.map((sentence, index) => Object.freeze({ text: index === 0 ? sentence : ` ${sentence}`, mark: false }))) });
  }
  // The sentence's words: those the question shares, those it does not (new), and those that carry no meaning.
  const tokens = [...best.matchAll(WORD)].map((match) => {
    const key = wordKey(match[0]);
    const kind = key.length <= 1 || STOP.has(key) ? 'stop' : asked.has(key.slice(0, 5)) ? 'shared' : 'new';
    return { start: match.index, end: match.index + match[0].length, kind };
  });
  // Runs of new words, a word that carries no meaning kept inside a run and never at its ends.
  const runs = [];
  let open = null;
  for (const token of tokens) {
    if (token.kind === 'new') {
      if (open === null) open = { start: token.start, end: token.end, words: 1 };
      else {
        open.end = token.end;
        open.words += 1;
      }
    } else if (token.kind === 'shared' && open !== null) {
      runs.push(open);
      open = null;
    }
  }
  if (open !== null) runs.push(open);
  const meaning = tokens.filter((token) => token.kind !== 'stop').length;
  const fresh = runs.reduce((sum, run) => sum + run.words, 0);
  const marked = fresh > 0 && fresh * 5 <= meaning * 2 ? runs : [];
  const parts = [];
  let from = 0;
  for (const run of marked) {
    if (run.start > from) parts.push(Object.freeze({ text: best.slice(from, run.start), mark: false }));
    parts.push(Object.freeze({ text: best.slice(run.start, run.end), mark: true }));
    from = run.end;
  }
  if (from < best.length) parts.push(Object.freeze({ text: best.slice(from), mark: false }));
  return Object.freeze({ parts: Object.freeze(parts) });
}

/** The trace's search step, as it was asked (its operation's parameters), or null when the answer ran no search. */
export function searchStepOf(trace) {
  const step = (trace?.steps ?? []).find((candidate) => candidate?.operation_id === 'search' && candidate?.fired !== false && candidate?.parameters?.query);
  return step === undefined ? null : step.parameters;
}

/**
 * What a search answer found: its hits counted, whether it stopped at its limit (more matched than it lists), and whether
 * the words named an article the search opened by its reference, with no word search (`named`: "article 5 of the GDPR").
 */
export function searchCount(envelope) {
  const value = envelope?.result?.value;
  if (!Array.isArray(value?.hits)) return null;
  const named = value.hits.length === 0 && value.reference?.outcome === 'opens';
  return Object.freeze({ matched: value.hits.length, more: value.truncated === true, named });
}

/**
 * Whether a quotation verifies: the site's `verify` operation, asked for its article's permalink, finds the published text's
 * digest, and that digest is the one the answer carried; both digests must be there (the review of #1055: a digest missing
 * on either side is no match).
 */
export function quoteVerifies(quote, envelope) {
  const value = envelope?.result?.value;
  if (value?.verdict !== 'digest_matches') return false;
  const served = value.article_text_sha256;
  return typeof quote?.article_text_sha256 === 'string' && typeof served === 'string' && served === quote.article_text_sha256;
}

/** The steps an answer ran, in its trace's order, each `{ operation, parameters }`: what the plan made Lex do. */
export function stepsRun(trace) {
  return (trace?.steps ?? []).filter((step) => step?.fired !== false).map((step) => Object.freeze({ operation: step.operation_id, parameters: step.parameters ?? {} }));
}

/** A calendar date as a page in `locale` writes it: day first in French (25/11/2025), as it is in English. */
export function dateFor(date, locale) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(String(date));
  return match !== null && locale === 'fr' ? `${match[3]}/${match[2]}/${match[1]}` : String(date);
}

/** A planner's identifier as a reader reads it: "gpt-4.1-mini · prompt a625e51e" for "azure-openai/gpt-4.1-mini/prompt-a625e51e3b58". */
export function plannerName(plannerId) {
  const match = /^[^/]+\/([^/]+)\/prompt-([0-9a-f]{8})/u.exec(String(plannerId ?? ''));
  return match === null ? String(plannerId ?? '') : `${match[1]} · prompt ${match[2]}`;
}

/** A build's commit as an envelope names it (`context.commits.serving_runtime_source_commit`): forty hex digits. */
const COMMIT = /^[0-9a-f]{40}$/u;

/**
 * The model registry's version that evaluated the very build and planner an answer ran on (the owner's order of 2026-11-15,
 * MLflow visible in the app). A planner alone names none: versions of different builds and gate results share one (the
 * review of #1060). So only the versions tagged with both the answer's planner (its trace's `planner_id`) and the build its
 * envelope names (`servingCommit`); among them the champion, else the latest whose gates passed, else the latest. Null when
 * either is not named or no version evaluated both: a build is registered when it is put live, after its pages are built,
 * so the build serving the answer may not be in this page's copy of the registry, and the work then names no version.
 */
export function registryVersionFor(registry, plannerId, servingCommit) {
  if (typeof plannerId !== 'string' || plannerId.length === 0 || typeof servingCommit !== 'string' || !COMMIT.test(servingCommit)) return null;
  const same = (registry?.versions ?? []).filter((version) => version.tags?.planner_id === plannerId && version.tags?.serving_commit === servingCommit);
  if (same.length === 0) return null;
  return same.find((version) => (version.aliases ?? []).includes('champion'))
    ?? same.filter((version) => version.tags?.gates === 'passed').at(-1)
    ?? same.at(-1);
}

// ---------------------------------------------------------------------------------------------------------------------------
// The docked assistant's reading of an answer (the owner's brief of 2026-11-15, sections 5 and 6). Pure functions again:
// every line the dock draws of an answer's work is read from its trace, its gates or the site's own operations.

/** A calendar date as the V4 pages write it in both languages (the brief's section 9): dd/mm/yyyy, or the value as it is. */
export function numericDate(date) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(String(date ?? ''));
  return match === null ? String(date ?? '') : `${match[3]}/${match[2]}/${match[1]}`;
}

/**
 * The first sentence of a quotation's text (its number and heading left out), as a span of its own characters, and whether
 * the quotation says more after it (the dock shows the sentence and " […]": the whole article is on the text page). Null when
 * the quotation has no sentence.
 */
export function firstSentenceOf(text) {
  const sentences = textSentencesOf(text);
  if (sentences.length === 0) return null;
  return Object.freeze({ sentence: sentences[0], more: sentences.length > 1 });
}

/**
 * The runs of a question that the platform's intake masks before the planner sees it: the same six patterns as
 * `V3AssistantParsers.IdentifierPatterns` (an e-mail, an IBAN, an international phone number, a national number, a run of
 * 11 to 13 digits, a Luxembourg phone number). The trace carries the question as asked, not its mask, so the dock counts
 * them again with the same rules; the count is all it shows.
 */
const IDENTIFIER_PATTERNS = Object.freeze([
  /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/gu,
  /(?<![A-Za-z0-9])[A-Z]{2}\d{2}(?: ?[A-Z0-9]{4}){3,7}(?: ?[A-Z0-9]{1,4})?(?![A-Za-z0-9])/gu,
  /(?<![\p{L}\p{N}_/])(?:\+|00)[1-9][\d ./-]{6,18}\d(?![\p{L}\p{N}_])/gu,
  /(?<!\d)\d{4} ?\d{4} ?\d{5}(?!\d)/gu,
  /(?<![\d\-/.])\d{11,13}(?![\d\-/.])/gu,
  /(?<![\d-])\d{3} \d{3} \d{3}(?![\d-])/gu,
]);

/** How many runs of the question the intake masks (overlapping runs counted once). */
export function maskedCount(question) {
  const runs = [];
  for (const pattern of IDENTIFIER_PATTERNS) {
    for (const match of String(question ?? '').matchAll(pattern)) runs.push([match.index, match.index + match[0].length]);
  }
  runs.sort((a, b) => a[0] - b[0]);
  let count = 0;
  let end = -1;
  for (const [start, stop] of runs) {
    if (start >= end) count += 1;
    end = Math.max(end, stop);
  }
  return count;
}

/** The operations the plan asked for, in its order: its steps, then its follow-ups (`followUp` true), as the trace holds it. */
export function plannedOperations(trace) {
  const plan = trace?.plan ?? {};
  const steps = (Array.isArray(plan.steps) ? plan.steps : []).map((step) => Object.freeze({ operation: String(step?.operation_id ?? ''), followUp: false }));
  const follow = (Array.isArray(plan.follow_ups) ? plan.follow_ups : []).map((step) => Object.freeze({ operation: String(step?.operation_id ?? ''), followUp: true }));
  return Object.freeze([...steps, ...follow].filter((entry) => entry.operation.length > 0));
}

/** What the trace's steps ran: how many fired, the article a fired step read (with its text and date), a refusal. */
export function executionOf(trace) {
  const steps = Array.isArray(trace?.steps) ? trace.steps : [];
  const fired = steps.filter((step) => step?.fired !== false);
  const read = [...fired].reverse().find((step) => step?.operation_id === 'evidence_bundle' && typeof step?.parameters?.article === 'string') ?? null;
  const refused = fired.find((step) => step?.outcome === 'refusal') ?? null;
  return Object.freeze({
    planned: steps.length,
    fired: fired.length,
    unfired: steps.length - fired.length,
    read: read === null ? null : Object.freeze({ identifier: read.parameters.identifier ?? null, article: read.parameters.article, date: read.parameters.date ?? null }),
    refused: refused === null ? null : String(refused.operation_id ?? ''),
  });
}

/**
 * The ten-tick strip of an article's versions (`article_history`): one tick per version held in `language` that holds the
 * article, oldest first, each with its date and whether its wording changed from the one before; and the date of the latest
 * change, or null when the wording is the same in every version held.
 */
export function articleStrip(value, language = null) {
  const states = (Array.isArray(value?.states) ? value.states : []).filter((state) => typeof state?.applicability_date === 'string');
  const inLanguage = language !== null && states.some((state) => state.language === language) ? states.filter((state) => state.language === language) : states;
  const ticks = inLanguage
    .filter((state) => state.text_status === undefined || state.text_status === 'held')
    .filter((state) => !Array.isArray(state.articles) || state.articles.length > 0)
    .slice()
    .sort((a, b) => (a.applicability_date < b.applicability_date ? -1 : a.applicability_date > b.applicability_date ? 1 : 0))
    .map((state, index) => Object.freeze({ date: state.applicability_date, changed: index > 0 && state.wording_changed === true }));
  const changes = ticks.filter((tick) => tick.changed);
  return Object.freeze({ ticks: Object.freeze(ticks), changedOn: changes.length === 0 ? null : changes.at(-1).date, since: ticks[0]?.date ?? null });
}

/**
 * Where an exact reference opens its text, from a `search` answer's value: the text (its address on this site), the date
 * the reference names (else the version's own date), the article, and the act's title as the reference read it. Null when
 * the search did not read the words as an exact reference that opens (phase `exact_reference`, outcome `opens`).
 */
export function referenceOpening(value) {
  const reference = value?.reference;
  if (reference?.phase !== 'exact_reference' || reference?.outcome !== 'opens' || reference?.opens === null || typeof reference?.opens !== 'object') return null;
  const opens = reference.opens;
  if (typeof opens.publisher !== 'string' || typeof opens.work !== 'string') return null;
  const named = reference.recognised?.date?.date;
  return Object.freeze({
    identifier: `/${opens.publisher}/${opens.work}`,
    date: typeof named === 'string' ? named : typeof opens.state_date === 'string' ? opens.state_date : null,
    article: typeof opens.article_id === 'string' ? opens.article_id : null,
    title: typeof reference.recognised?.act?.title === 'string' ? ownTitle(reference.recognised.act.title) : null,
    titleLanguage: typeof reference.recognised?.act?.title_language === 'string' ? reference.recognised.act.title_language : null,
    language: typeof opens.language === 'string' ? opens.language : null,
  });
}

/**
 * The text's own title and the article's heading, from an `evidence_bundle` answer for one article: the title of the very
 * version read (its expression's, in the version's language, "Version consolidée applicable au …:" taken off, never the
 * title of a modifying act the work's dossier may name first), and the article's heading. Each null when not carried.
 */
export function bundleFacts(value, article = null, language = null) {
  const pick = (articles) => {
    const list = Array.isArray(articles) ? articles : [];
    return list.find((candidate) => candidate?.publisher_id === article) ?? (list.length === 1 ? list[0] : null);
  };
  const heading = (found) => (typeof found?.text === 'string' ? articleHeadingOf(found.text) || null : null);
  // An EU act (`wordings`, `titles.entries`): the wording in the language asked, its article, the expression's title.
  if (Array.isArray(value?.wordings)) {
    const wording = value.wordings.find((candidate) => candidate?.language === language) ?? value.wordings[0] ?? null;
    const entries = (Array.isArray(value?.titles?.entries) ? value.titles.entries : []).filter((entry) => entry?.kind === 'title' && typeof entry.text === 'string');
    const lang = wording?.language ?? language;
    const entry = entries.find((candidate) => candidate.language === lang && candidate.source === 'cellar_expression_title')
      ?? entries.find((candidate) => candidate.language === lang) ?? entries[0] ?? null;
    return Object.freeze({
      title: entry === null ? null : ownTitle(entry.text),
      titleLanguage: entry?.language ?? null,
      heading: heading(pick(wording?.articles)),
    });
  }
  // A Luxembourg act (`states`, `citation_facts.titles`): the version read, its article, the title of that version's own
  // expression (never a modifying act's).
  const state = Array.isArray(value?.states) ? value.states[0] ?? null : null;
  const groups = Array.isArray(value?.citation_facts?.titles) ? value.citation_facts.titles : [];
  const group = groups.find((candidate) => candidate?.expression_iri === state?.expression_iri && candidate?.titles?.length > 0)
    ?? groups.find((candidate) => candidate?.language === state?.language && candidate?.titles?.length > 0)
    ?? groups.find((candidate) => candidate?.titles?.length > 0)
    ?? null;
  const raw = group?.titles?.[0]?.title;
  return Object.freeze({
    title: typeof raw === 'string' ? ownTitle(raw) : null,
    titleLanguage: typeof group?.language === 'string' ? group.language : null,
    heading: heading(pick(state?.articles)),
  });
}

/**
 * A version's title as the act's own: its "Version consolidée applicable au dd/mm/yyyy :" prefix, an EU act's "(Text with
 * EEA relevance)" note and its final stop taken off.
 */
export function ownTitle(title) {
  const stripped = String(title)
    .replace(/^\s*Version\s+consolid[ée]e\s+applicable\s+au\s+[0-9/.-]+\s*:\s*/iu, '')
    .replace(/\s*\((?:Text with EEA relevance|Texte présentant de l'intérêt pour l'EEE|Texte présentant de l’intérêt pour l’EEE)\)\s*$/u, '')
    .trim();
  return (stripped.length > 0 ? stripped : String(title).trim()).replace(/\s*\.$/u, '');
}
