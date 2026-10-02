// The region a live screen's answer is written into, announced to a screen reader when it changes.
//
// A live region must exist before its content changes, or assistive technology has nothing to watch:
// so the server renders it around the idle or loading state, and the answer, the refusal card or the
// sentence a state carries replaces what is inside it. `polite` waits for the reader to finish what
// they are hearing; nothing on these screens is urgent enough to interrupt them. The launch contract
// asks for keyboard and screen-reader paths through the eight screens, and this is the screen-reader
// half of the answer.
//
// `Say` lays out one sentence of the interface copy table (`live-chrome.mjs`): the template's text with
// its values in their places, where a value may be an element (a quotation in its own language, an
// identifier set as code).
//
// English on a page in another language is marked English, so a screen reader reads it as English:
// the platform's own phrases (`inEnglish`, `ENGLISH_LANG`) and the runs of a state's sentence that
// have no reviewed wording in the page's language (`Said`). On an English page none of this adds a
// tag or an attribute, so the English pages are exactly what they were.

import { Fragment } from 'react';

import { ENGLISH_LANG, fillParts, liveChrome } from '../scripts/live-chrome.mjs';

export function Say({ template, values }) {
  return fillParts(template, values).map((part, index) => (typeof part === 'string' ? part : <Fragment key={index}>{part}</Fragment>));
}

/**
 * A phrase the platform sends in English (a reason, a rule, a scope, a statement), as a value of a sentence or a
 * child of an element: the text itself on an English page, and the text marked `lang="en"` on a page in another.
 */
export function inEnglish(text) {
  return ENGLISH_LANG === undefined ? text : <span lang={ENGLISH_LANG}>{text}</span>;
}

/** A statement (`statement` in `live-chrome.mjs`) laid out: its sentence, or its runs with each English run marked. */
export function Said({ statement }) {
  if (statement.runs === undefined) return statement.sentence;
  return statement.runs.map((run, index) => (typeof run === 'string'
    ? <Fragment key={index}>{run}</Fragment>
    : <span key={index} lang={run.lang}>{run.text}</span>));
}

/** The sentence a state carries, as the screen's status. */
export function StatusSentence({ outcome }) {
  return (
    <p role="status">
      <Said statement={outcome} />
    </p>
  );
}

/**
 * The refusal card's words for a refusal's publisher: an EU refusal's declared nulls, offered candidates and their
 * note speak of wordings and their dates, never of states or of what applies (the review of #903; the French review's
 * item 3).
 */
export function refusalCardCopyFor(context) {
  const copy = liveChrome().refusalCard;
  return context?.publisher === 'eu-eurlex'
    ? {
      ...copy,
      notes: { ...copy.notes, ...copy.europeNotes },
      nullSentences: copy.europeNullSentences,
      candidate: copy.europeCandidate,
      candidateWithdrawalNotStated: copy.europeCandidateWithdrawalNotStated,
      published: copy.europePublished,
    }
    : copy;
}

export function LiveAnswer({ children }) {
  return (
    <div className="live-answer" aria-live="polite" data-live-answer="">
      {children}
    </div>
  );
}
