// The change radar screen, asked of the live API: the publisher-dated states in a window of dates.
//
// The server renders the form and the idle state, and the browser asks only when the reader
// submits. What an answer means is decided elsewhere: `scripts/live-radar.mjs` builds the request and
// maps the answer to a view state, `scripts/radar-answer.mjs` reads the radar, and `RefusalCard` lays
// out a refusal. This file lays the view out and marks the state on the answer (`data-answer-state`).
//
// A row says a state of a work appeared in the window, the baseline it replaced, and whether its
// wording changed from it, or why that is not said (the first held state, an ambiguity, different
// rule profiles). The platform's caveat is shown with the rows: a version row does not by itself
// assert a wording change, legal effect or entry into force. The form's controls carry no `name`,
// and permalinks are printed, not linked.

import { Fragment, useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { LIVE_RADAR_IDLE, RADAR_LANGUAGES, createRadarSession } from '../scripts/live-radar.mjs';

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_RADAR_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

const REASON_SENTENCES = Object.freeze({
  first_held_state: 'the first state this index holds for the work and language, so there is nothing to compare it with',
  ambiguous_version: 'several states of the work apply on this date, so none is compared',
  ambiguous_baseline: 'several states apply on the date before it, so no baseline is chosen',
  profiles_differ: 'this state and its baseline were read under different rule profiles, so they are not compared',
});

/** The states a row names besides its own, each by its permalink, so a reader can open either. */
function Named({ label, permalinks }) {
  return (
    <>
      {' '}({label}{' '}
      {permalinks.map((permalink, index) => (
        <Fragment key={permalink}>
          {index === 0 ? '' : ', '}
          <code>{permalink}</code>
        </Fragment>
      ))}
      )
    </>
  );
}

function Verdict({ row }) {
  if (row.reason === null) {
    return (
      <>
        {row.wordingChanged ? 'wording changed' : 'wording unchanged'} from the state of {row.baseline.applicabilityDate}
        <Named label="baseline" permalinks={[row.baseline.permalink]} />:{' '}
        {row.counts.changed} changed, {row.counts.added} added, {row.counts.removed} removed, {row.counts.unchanged} unchanged
      </>
    );
  }
  return (
    <>
      not compared: {REASON_SENTENCES[row.reason]}
      {row.candidates === null ? null : <Named label="candidates" permalinks={row.candidates} />}
      {row.baseline === null ? null : <Named label="baseline" permalinks={[row.baseline.permalink]} />}
    </>
  );
}

/** One radar view, laid out: the window, the population, the rows and the caveat. */
export function RadarView({ view }) {
  const { population } = view;
  return (
    <>
      <p data-radar-summary="">
        {view.window.from} to {view.window.to}: {population.versionsInWindow}{' '}
        {population.versionsInWindow === 1 ? 'state' : 'states'} of {population.worksInWindow}{' '}
        {population.worksInWindow === 1 ? 'work' : 'works'}, of {population.worksHeld} held.
      </p>
      <p data-caveat="">{asSentence(view.caveat)}</p>
      {view.rows.length === 0 ? (
        <p data-no-row="">
          {population.windowOverlapsWhatIsHeld
            ? 'No held state is dated in this window.'
            : `This window does not meet what this index holds${population.firstDateHeld === null ? '' : `, from ${population.firstDateHeld} to ${population.lastDateHeld}`}.`}
        </p>
      ) : (
        <ol data-rows={view.rows.length}>
          {view.rows.map((row) => (
            <li key={row.state.stateSha256} data-reason={row.reason ?? (row.wordingChanged ? 'changed' : 'unchanged')}>
              <strong>{row.workKey}</strong>, {row.state.language}, from {row.state.applicabilityDate}: <Verdict row={row} />.{' '}
              <code>{row.state.permalink}</code>
            </li>
          ))}
        </ol>
      )}
      {view.truncated ? <p data-truncated="">The rows stop before {view.continueFrom}; a next request starts there.</p> : null}
    </>
  );
}

/** One view state, laid out: the radar, the refusal card, or the sentence a state carries. */
export function RadarAnswerView({ outcome }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <RadarView view={outcome.view} />
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
 * The live screen: a form, and the state of the last radar asked. A radar is asked when the reader
 * submits, never while rendering; the session cancels the one in flight when another is asked, and
 * unmounting cancels whatever is left.
 */
export function LiveRadar({ contract, fetchImpl }) {
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [identifier, setIdentifier] = useState('');
  const [language, setLanguage] = useState('');
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createRadarSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ dateFrom, dateTo, identifier, language });
        }}
      >
        <label>
          From{' '}
          <input type="text" inputMode="numeric" placeholder="yyyy-mm-dd" value={dateFrom} autoComplete="off" onChange={(event) => setDateFrom(event.target.value)} />
        </label>{' '}
        <label>
          To{' '}
          <input type="text" inputMode="numeric" placeholder="yyyy-mm-dd" value={dateTo} autoComplete="off" onChange={(event) => setDateTo(event.target.value)} />
        </label>{' '}
        <label>
          Work identifier (optional){' '}
          <input type="text" value={identifier} autoComplete="off" spellCheck={false} onChange={(event) => setIdentifier(event.target.value)} />
        </label>{' '}
        <label>
          Language{' '}
          <select value={language} onChange={(event) => setLanguage(event.target.value)}>
            <option value="">Any held language</option>
            {RADAR_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">List changes</button>
      </form>
      <RadarAnswerView outcome={outcome} />
    </div>
  );
}
