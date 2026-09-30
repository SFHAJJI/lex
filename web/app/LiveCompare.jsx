// The compare screen, asked of the live API: two states of one work, article by article.
//
// The server renders the form and the idle state, and the browser asks only when the reader
// submits. What an answer means is decided elsewhere: `scripts/live-compare.mjs` builds the request
// and maps the answer to a view state, `scripts/compare-answer.mjs` reads and recomputes the
// comparison, and `RefusalCard` lays out a refusal. This file lays the view out and marks the state
// on the answer (`data-answer-state`).
//
// The comparison is by the publisher's article id and wording digest, and says nothing about legal
// effect; the page says the platform's own note beside it. It is linear, not a colour-coded diff:
// each status is a word, and the changed, added and removed articles are listed before the unchanged
// ones, which are one disclosure away. The form's controls carry no `name`, and permalinks are
// printed, not linked.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { COMPARE_LANGUAGES, LIVE_COMPARE_IDLE, createCompareSession } from '../scripts/live-compare.mjs';
import { liveChrome } from '../scripts/live-chrome.mjs';

/** The forms' labels and buttons, from the interface copy table. */
const FORM = liveChrome().form;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_COMPARE_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

function Side({ label, side }) {
  return (
    <p data-side={label}>
      {/* The next state's date is said as that state's start, never as this one's end: "until" would
          leave open whether the boundary day is this state's (review of #783). */}
      {label}: the state applying from {side.applicabilityDate}
      {side.nextApplicabilityDate === null ? '' : ` (the next state held applies from ${side.nextApplicabilityDate})`},{' '}
      {side.articleCount} articles, {side.validityConflictCount} with their own date differing from the state's,{' '}
      <code>{side.permalink}</code>
    </p>
  );
}

function Row({ row }) {
  // The whole digest, never a prefix: a prefix is not the digest a reader can check (review of #783).
  const digest = (entries) => (entries.length === 0 ? 'none' : <code>{entries.map((entry) => entry.wordingSha256).join(', ')}</code>);
  return (
    <li data-status={row.status}>
      <strong>{row.publisherId}</strong>: {row.status} (wording {digest(row.from)} → {digest(row.to)})
    </li>
  );
}

function Comparison({ comparison }) {
  const moved = comparison.articles?.filter((row) => row.status !== 'unchanged') ?? [];
  const kept = comparison.articles?.filter((row) => row.status === 'unchanged') ?? [];
  return (
    <section data-comparison={comparison.language}>
      <h2>{comparison.language}</h2>
      <Side label="From" side={comparison.from} />
      <Side label="To" side={comparison.to} />
      <p>{asSentence(comparison.note)}</p>
      {comparison.sameState ? null : (
        <>
          <p data-counts="">
            {comparison.counts.changed} changed, {comparison.counts.added} added, {comparison.counts.removed} removed,{' '}
            {comparison.counts.unchanged} unchanged.
          </p>
          {moved.length > 0 ? (
            <ol data-moved={moved.length}>
              {moved.map((row) => <Row key={row.publisherId} row={row} />)}
            </ol>
          ) : null}
          {kept.length > 0 ? (
            <details>
              <summary>{kept.length} unchanged {kept.length === 1 ? 'article' : 'articles'}</summary>
              <ol data-kept={kept.length}>
                {kept.map((row) => <Row key={row.publisherId} row={row} />)}
              </ol>
            </details>
          ) : null}
        </>
      )}
    </section>
  );
}

/** One comparison view, laid out: each compared language, the languages not compared, the rule. */
export function CompareView({ view }) {
  return (
    <>
      <p data-compare-summary="">
        {view.workKey}: {view.dateFrom} against {view.dateTo}
        {view.language === null ? '' : ` in ${view.language}`}.
      </p>
      {view.comparisons.map((comparison) => <Comparison key={comparison.language} comparison={comparison} />)}
      {view.languagesNotCompared.length > 0 ? (
        <ul data-not-compared={view.languagesNotCompared.length}>
          {view.languagesNotCompared.map((entry) => (
            <li key={entry.language}>
              {entry.language} is not compared: {entry.reason} ({entry.bound} date).
            </li>
          ))}
        </ul>
      ) : null}
      <p>{asSentence(view.wordingRule)}</p>
      <p>{asSentence(view.validityConflictRule)}</p>
    </>
  );
}

/** One view state, laid out: the comparison, the refusal card, or the sentence a state carries. */
export function CompareAnswerView({ outcome }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <CompareView view={outcome.view} />
      </section>
    );
  }

  if (outcome.state === 'refusal' && outcome.card) {
    return (
      <section data-answer-state="refusal">
        <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} />
      </section>
    );
  }

  return (
    <section data-answer-state={outcome.state}>
      <p role="status">{outcome.sentence}</p>
    </section>
  );
}

/**
 * The live screen: a form, and the state of the last comparison asked. A comparison is asked when
 * the reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left.
 */
export function LiveCompare({ contract, fetchImpl }) {
  const [identifier, setIdentifier] = useState('');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [language, setLanguage] = useState('');
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createCompareSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ identifier, dateFrom, dateTo, language });
        }}
      >
        <label>
          {FORM.workIdentifier}{' '}
          <input type="text" value={identifier} autoComplete="off" spellCheck={false} onChange={(event) => setIdentifier(event.target.value)} />
        </label>{' '}
        <label>
          {FORM.from}{' '}
          <input type="text" inputMode="numeric" placeholder="yyyy-mm-dd" value={dateFrom} autoComplete="off" onChange={(event) => setDateFrom(event.target.value)} />
        </label>{' '}
        <label>
          {FORM.to}{' '}
          <input type="text" inputMode="numeric" placeholder="yyyy-mm-dd" value={dateTo} autoComplete="off" onChange={(event) => setDateTo(event.target.value)} />
        </label>{' '}
        <label>
          {FORM.language}{' '}
          <select value={language} onChange={(event) => setLanguage(event.target.value)}>
            <option value="">{FORM.anyLanguage}</option>
            {COMPARE_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">{FORM.submit.compare}</button>
      </form>
      <CompareAnswerView outcome={outcome} />
    </div>
  );
}
