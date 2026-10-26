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
// controls carry no `name`, and every permalink is a link to its page (`Permalink`).

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { HISTORY_LANGUAGES, LIVE_HISTORY_IDLE, createHistorySession } from '../scripts/live-history.mjs';
import { ENGLISH_LANG, fillCounted, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { datedByEntryIntoForce } from '../scripts/date-role.mjs';
import { FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, TableRegion, refusalCardCopyFor } from './LiveAnswer.jsx';

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

/**
 * Where no version whose text this site holds covers the dates (index 9's continuity breaks, S7b): each run in its
 * language, from its first date to the date a version with held text covers again (or with none later), and why, in the
 * page's words; and that an unchanged wording across one says nothing of it. Nothing on an answer that names none.
 */
function Breaks({ breaks = [] }) {
  if (breaks.length === 0) return null;
  return (
    <>
      <h3>{COPY.breaksHeading}</h3>
      <ul data-breaks={breaks.length}>
        {breaks.map((gap) => {
          const reasons = gap.reasons.map((reason) => COPY.breakReasons[reason]).join(COMMON.listSeparator);
          return (
            <li key={`${gap.language}.${gap.from}`} data-break-from={gap.from} data-break-to={gap.to ?? ''}>
              {gap.to === null
                ? fillText(COPY.breakOpen, { language: gap.language, from: gap.from, reasons })
                : fillText(COPY.breakTo, { language: gap.language, from: gap.from, to: gap.to, reasons })}
            </li>
          );
        })}
      </ul>
      <p>{COPY.breaksNote}</p>
    </>
  );
}

/** One lineage view, laid out: the rows that carry the id, the states that do not, and the counts. */
export function HistoryView({ view }) {
  const dossier = liveChrome().dossier;
  const seen = new Set();
  const runs = Object.entries(view.wordingRuns);
  // The lineage begins with its first row, whose date is said with its role: an act dated by its entry into force is never
  // said as from it (R21; the review of #987).
  const byForce = datedByEntryIntoForce(view.rows[0]);
  const carried = view.language === null
    ? fillCounted(byForce ? COPY.carriedEntryIntoForce : COPY.carried, view.rows.length, { from: view.historyBegins })
    : fillCounted(byForce ? COPY.carriedInEntryIntoForce : COPY.carriedIn, view.rows.length, { language: view.language, from: view.historyBegins });
  return (
    <>
      <h2>
        <Say template={COPY.anchorHeading} values={{ anchor: view.anchor, work: view.workKey }} />
      </h2>
      <p data-history-summary="" id="history-summary">
        {carried}{' '}
        {fillCounted(COPY.absent, view.absent.length, {})}{' '}
        {runs.map(([language, count]) => fillCounted(COPY.runs, count, { language, distinct: view.distinctWordings[language] })).join(COMMON.listSeparator)}.
      </p>
      <TableRegion labelledBy="history-summary">
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
              <tr key={row.stateSha256} data-row={row.stateSha256} data-wording={kind} data-after-break={row.afterBreak ? '' : undefined}>
                <td>{row.language}</td>
                {/* A row dated by its entry into force answers the column as the dossier's does (R21, S7b). */}
                <td data-date-role={row.dateRole ?? undefined}>
                  {datedByEntryIntoForce(row) ? fillText(dossier.entryIntoForceDate, { date: row.applicabilityDate }) : row.applicabilityDate}
                </td>
                <td>{row.nextApplicabilityDate ?? COMMON.noneHeld}</td>
                <td>{row.afterBreak ? `${COPY.wording[kind]}${COMMON.listSeparator}${COPY.afterBreak}` : COPY.wording[kind]}</td>
                <td>
                  {row.articles
                    .map((article) => (article.validFrom === null
                      ? COPY.ownDateNotStated
                      : article.validityConflict ? fillText(COPY.ownDateDiffers, { date: article.validFrom }) : article.validFrom))
                    .join(COMMON.listSeparator)}
                </td>
                <td><Permalink value={row.permalink} /></td>
              </tr>
            );
          })}
        </tbody>
      </table>
      </TableRegion>
      {view.rows.some(datedByEntryIntoForce) ? <p data-entry-into-force-note="">{dossier.entryIntoForceNote}</p> : null}
      <Breaks breaks={view.breaks} />
      {view.absent.length > 0 ? (
        <>
          <h3><Say template={COPY.absentHeading} values={{ anchor: view.anchor }} /></h3>
          <ul data-absent={view.absent.length}>
            {view.absent.map((state) => (
              <li key={state.stateSha256}>
                <Say template={datedByEntryIntoForce(state) ? COPY.absentRowEntryIntoForce : COPY.absentRow} values={{ language: state.language, from: state.applicabilityDate, permalink: <Permalink value={state.permalink} /> }} />
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
export function HistoryAnswerView({ outcome, onRetry }) {
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
        <FailureFrame outcome={outcome} onRetry={onRetry}>
          <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} copy={refusalCardCopyFor(outcome.context)} />
        </FailureFrame>
      </section>
    );
  }

  return (
    <section data-answer-state={outcome.state}>
      <FailureFrame outcome={outcome} onRetry={onRetry}>
        <StatusSentence outcome={outcome} />
      </FailureFrame>
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
        <HistoryAnswerView outcome={outcome} onRetry={() => session.current.retry()} />
      </LiveAnswer>
    </div>
  );
}
