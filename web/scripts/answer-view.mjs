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

/** The sentences of a quotation that are its text: neither its number ("Article 44") nor its heading (`articleHeadingOf`). */
function textSentencesOf(text) {
  const heading = articleHeadingOf(text);
  return sentencesOf(text).filter((sentence) => sentence !== heading && !/^Art(?:icle|\.)?\s*[\w-]+\.?$/iu.test(sentence));
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

/** What a search answer found: its hits counted, and whether it stopped at its limit (more matched than it lists). */
export function searchCount(envelope) {
  const value = envelope?.result?.value;
  if (!Array.isArray(value?.hits)) return null;
  return Object.freeze({ matched: value.hits.length, more: value.truncated === true });
}

/**
 * Whether a quotation verifies (as lex-eval scores it): the site's `verify` operation, asked for its article's permalink,
 * finds the published text's digest, and it is the digest the answer carried.
 */
export function quoteVerifies(quote, envelope) {
  const value = envelope?.result?.value;
  if (value?.verdict !== 'digest_matches') return false;
  const served = value.article_text_sha256;
  return typeof quote?.article_text_sha256 !== 'string' || typeof served !== 'string' || served === quote.article_text_sha256;
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
