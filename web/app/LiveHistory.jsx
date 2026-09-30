// The provision history screen, asked of the live API: one publisher article id through the held
// states of one work.
//
// The server renders the form and the idle state, and the browser asks only when the reader
// submits. What an answer means is decided elsewhere: `scripts/live-history.mjs` builds the request
// and maps the answer to a view state, `scripts/history-answer.mjs` reads and recomputes the
// lineage, and `RefusalCard` lays out a refusal. This file lays the view out and marks the state on
// the answer (`data-answer-state`).
//
// The lineage says only what the answer states: which held states carry the article id, whether its
// wording changed from the previous state of the same language, and which states do not carry it.
// It derives no end date, no "in force" and no diff text. The states that do not carry the id are
// shown, so a lineage that begins after the work does is never read as the whole history. The form's
// controls carry no `name`, and permalinks are printed, not linked.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { HISTORY_LANGUAGES, LIVE_HISTORY_IDLE, createHistorySession } from '../scripts/live-history.mjs';
import { liveChrome } from '../scripts/live-chrome.mjs';

/** The forms' labels and buttons, from the interface copy table. */
const FORM = liveChrome().form;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_HISTORY_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

function wordingLabel(row, firstOfLanguage) {
  if (firstOfLanguage) return 'first held wording';
  return row.wordingChanged ? 'wording changed' : 'wording unchanged';
}

/** One lineage view, laid out: the rows that carry the id, the states that do not, and the counts. */
export function HistoryView({ view }) {
  const seen = new Set();
  const runs = Object.entries(view.wordingRuns);
  return (
    <>
      <h2>
        {view.anchor} in {view.workKey}
      </h2>
      <p data-history-summary="">
        Carried by {view.rows.length} held {view.rows.length === 1 ? 'state' : 'states'}
        {view.language === null ? '' : ` in ${view.language}`}, from {view.historyBegins};{' '}
        {view.absent.length} held {view.absent.length === 1 ? 'state does' : 'states do'} not carry it.{' '}
        {runs.map(([language, count]) => `${language}: ${count} ${count === 1 ? 'wording run' : 'wording runs'}, ${view.distinctWordings[language]} distinct`).join('; ')}.
      </p>
      <table>
        <thead>
          <tr>
            <th scope="col">Language</th>
            <th scope="col">Applies from</th>
            <th scope="col">Next state from</th>
            <th scope="col">Wording</th>
            <th scope="col">Article's own date</th>
            <th scope="col">Permalink</th>
          </tr>
        </thead>
        <tbody>
          {view.rows.map((row) => {
            const first = !seen.has(row.language);
            seen.add(row.language);
            return (
              <tr key={row.stateSha256} data-row={row.stateSha256} data-wording={wordingLabel(row, first)}>
                <td>{row.language}</td>
                <td>{row.applicabilityDate}</td>
                <td>{row.nextApplicabilityDate ?? 'none held'}</td>
                <td>{wordingLabel(row, first)}</td>
                <td>
                  {row.articles
                    .map((article) => (article.validFrom === null
                      ? 'not stated'
                      : article.validityConflict ? `${article.validFrom} (differs from the state's)` : article.validFrom))
                    .join('; ')}
                </td>
                <td><code>{row.permalink}</code></td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {view.absent.length > 0 ? (
        <>
          <h3>Held states that do not carry {view.anchor}</h3>
          <ul data-absent={view.absent.length}>
            {view.absent.map((state) => (
              <li key={state.stateSha256}>
                {state.language}, from {state.applicabilityDate}: <code>{state.permalink}</code>
              </li>
            ))}
          </ul>
        </>
      ) : null}
      <p>{asSentence(view.wordingRule)}</p>
    </>
  );
}

/** One view state, laid out: the lineage, the refusal card, or the sentence a state carries. */
export function HistoryAnswerView({ outcome }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <HistoryView view={outcome.view} />
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
 * The live screen: a form, and the state of the last lineage asked. A lineage is asked when the
 * reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left.
 */
export function LiveHistory({ contract, fetchImpl }) {
  const [identifier, setIdentifier] = useState('');
  const [anchor, setAnchor] = useState('');
  const [language, setLanguage] = useState('');
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createHistorySession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ identifier, anchor, language });
        }}
      >
        <label>
          {FORM.workIdentifier}{' '}
          <input
            type="text"
            value={identifier}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setIdentifier(event.target.value)}
          />
        </label>{' '}
        <label>
          {FORM.articleId}{' '}
          <input
            type="text"
            placeholder="art_15"
            value={anchor}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setAnchor(event.target.value)}
          />
        </label>{' '}
        <label>
          {FORM.language}{' '}
          <select value={language} onChange={(event) => setLanguage(event.target.value)}>
            <option value="">{FORM.anyLanguage}</option>
            {HISTORY_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">{FORM.submit.history}</button>
      </form>
      <HistoryAnswerView outcome={outcome} />
    </div>
  );
}
