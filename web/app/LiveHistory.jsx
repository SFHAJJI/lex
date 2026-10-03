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
import { ENGLISH_LANG, fillCounted, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { LiveAnswer, Say, StatusSentence, refusalCardCopyFor } from './LiveAnswer.jsx';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().history;
const COMMON = liveChrome().common;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_HISTORY_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

/** Which wording a row has: the first its language holds, or changed or unchanged from the row before. */
function wordingKind(row, firstOfLanguage) {
  if (firstOfLanguage) return 'first';
  return row.wordingChanged ? 'changed' : 'unchanged';
}

/** One lineage view, laid out: the rows that carry the id, the states that do not, and the counts. */
export function HistoryView({ view }) {
  const seen = new Set();
  const runs = Object.entries(view.wordingRuns);
  const carried = view.language === null
    ? fillCounted(COPY.carried, view.rows.length, { from: view.historyBegins })
    : fillCounted(COPY.carriedIn, view.rows.length, { language: view.language, from: view.historyBegins });
  return (
    <>
      <h2>
        <Say template={COPY.anchorHeading} values={{ anchor: view.anchor, work: view.workKey }} />
      </h2>
      <p data-history-summary="">
        {carried}{' '}
        {fillCounted(COPY.absent, view.absent.length, {})}{' '}
        {runs.map(([language, count]) => fillCounted(COPY.runs, count, { language, distinct: view.distinctWordings[language] })).join(COMMON.listSeparator)}.
      </p>
      <table>
        <thead>
          <tr>
            <th scope="col">{COMMON.language}</th>
            <th scope="col">{COMMON.appliesFrom}</th>
            <th scope="col">{COMMON.nextFrom}</th>
            <th scope="col">{COPY.wordingColumn}</th>
            <th scope="col">{COPY.ownDateColumn}</th>
            <th scope="col">{COMMON.permalink}</th>
          </tr>
        </thead>
        <tbody>
          {view.rows.map((row) => {
            const first = !seen.has(row.language);
            seen.add(row.language);
            const kind = wordingKind(row, first);
            return (
              <tr key={row.stateSha256} data-row={row.stateSha256} data-wording={kind}>
                <td>{row.language}</td>
                <td>{row.applicabilityDate}</td>
                <td>{row.nextApplicabilityDate ?? COMMON.noneHeld}</td>
                <td>{COPY.wording[kind]}</td>
                <td>
                  {row.articles
                    .map((article) => (article.validFrom === null
                      ? COPY.ownDateNotStated
                      : article.validityConflict ? fillText(COPY.ownDateDiffers, { date: article.validFrom }) : article.validFrom))
                    .join(COMMON.listSeparator)}
                </td>
                <td><code>{row.permalink}</code></td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {view.absent.length > 0 ? (
        <>
          <h3><Say template={COPY.absentHeading} values={{ anchor: view.anchor }} /></h3>
          <ul data-absent={view.absent.length}>
            {view.absent.map((state) => (
              <li key={state.stateSha256}>
                <Say template={COPY.absentRow} values={{ language: state.language, from: state.applicabilityDate, permalink: <code>{state.permalink}</code> }} />
              </li>
            ))}
          </ul>
        </>
      ) : null}
      <p lang={ENGLISH_LANG}>{asSentence(view.wordingRule)}</p>
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
        <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} copy={refusalCardCopyFor(outcome.context)} />
      </section>
    );
  }

  return (
    <section data-answer-state={outcome.state}>
      <StatusSentence outcome={outcome} />
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
            placeholder={FORM.articleIdPlaceholder}
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
      <LiveAnswer>
        <HistoryAnswerView outcome={outcome} />
      </LiveAnswer>
    </div>
  );
}
