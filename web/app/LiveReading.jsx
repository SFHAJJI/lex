// The reading screen, asked of the live API: the text of a work as it stood on one date.
//
// The server renders the form and the idle state, and the browser asks only when the reader
// submits. What an answer means is decided elsewhere: `scripts/live-reading.mjs` builds the request
// and maps the answer to a view state, `scripts/reading-answer.mjs` reads the evidence bundle, and
// `RefusalCard` lays out a refusal. This file lays the view out and marks the state on the answer
// (`data-answer-state`).
//
// Three rules of the pre-V3 reading screen carry over, because each is a sentence the page would
// otherwise say that is not true. The publisher's text is quoted in its state's language
// (`lang` on the quotation, never the interface's). An article whose own date differs from its
// state's shows both dates, and this page does not decide which controlled. An article held without
// text is named as such, never shown as an empty quotation. The form's controls carry no `name`, and
// permalinks are printed, not linked.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { LiveAnswer } from './LiveAnswer.jsx';
import {
  LIVE_READING_IDLE,
  READING_LANGUAGES,
  createReadingSession,
  quotationLanguageTag,
} from '../scripts/live-reading.mjs';
import { liveChrome } from '../scripts/live-chrome.mjs';

/** The forms' labels and buttons, from the interface copy table. */
const FORM = liveChrome().form;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_READING_IDLE });

function Article({ article, state }) {
  return (
    <li id={`${state.language}-${article.publisherId}`} data-article={article.publisherId}>
      <h3>{article.publisherId}</h3>
      <blockquote lang={quotationLanguageTag(state.language)}>{article.text}</blockquote>
      {article.validityConflict ? (
        <p data-validity-conflict="">
          This article's own date is {article.validFrom}; its state applies from {state.applicabilityDate}.
        </p>
      ) : null}
      {article.notes.length > 0 ? (
        <ol data-notes={article.notes.length}>
          {article.notes.map((note, index) => (
            <li key={`${index}.${note.marker}`}>
              <span>[{note.marker}]</span> <span lang={quotationLanguageTag(state.language)}>{note.text}</span>
            </li>
          ))}
        </ol>
      ) : null}
      <p>
        Text digest <code>{article.textSha256}</code>, <code>{article.permalink}</code>
      </p>
    </li>
  );
}

function StateReading({ state, workKey }) {
  return (
    <section data-state={state.stateSha256}>
      <h2>
        {workKey}, {state.language}, the state applying from {state.applicabilityDate}
        {state.nextApplicabilityDate === null ? '' : ` (the next state held applies from ${state.nextApplicabilityDate})`}
      </h2>
      <p>
        <code>{state.permalink}</code>
      </p>
      <p data-counts="">
        {state.articles.length} {state.articles.length === 1 ? 'article' : 'articles'} quoted,{' '}
        {state.articlesWithoutText.length} held without text, {state.articlesNotAdmitted} not admitted;{' '}
        {state.validityConflictCount} with their own date differing from the state's.
      </p>
      <ol className="articles">
        {state.articles.map((article) => (
          <Article key={article.articleIdentitySha256} article={article} state={state} />
        ))}
      </ol>
      {state.articlesWithoutText.length > 0 ? (
        <p data-without-text="">
          Held without text: {state.articlesWithoutText.map((entry) => entry.publisherId).join(', ')}.
        </p>
      ) : null}
    </section>
  );
}

/** One reading view, laid out: each state's quoted text and what the bundle does not hold. */
export function ReadingView({ view }) {
  return (
    <>
      <p data-rights="">
        Text served under {view.rightsDisposition}. Read on {view.date}
        {view.language === null ? '' : ` in ${view.language}`}.
      </p>
      {view.states.map((state) => (
        <StateReading key={state.stateSha256} state={state} workKey={view.workKey} />
      ))}
      <h3>What this reading does not hold</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <strong>{row.item}</strong>: {row.reason}
          </li>
        ))}
      </ul>
    </>
  );
}

/** One view state, laid out: the reading, the refusal card, or the sentence a state carries. */
export function ReadingAnswerView({ outcome }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <ReadingView view={outcome.view} />
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
 * The reading form: a work identifier, a date and a language, handed to `onAsk` on submit. Shared
 * with the export composer, which asks the same reading.
 */
export function ReadingForm({ onAsk, submitLabel = FORM.submit.reading }) {
  const [identifier, setIdentifier] = useState('');
  const [date, setDate] = useState('');
  const [language, setLanguage] = useState('');
  return (
    <form
      role="search"
      onSubmit={(event) => {
        event.preventDefault();
        onAsk({ identifier, date, language });
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
        {FORM.date}{' '}
        <input
          type="text"
          inputMode="numeric"
          placeholder="yyyy-mm-dd"
          value={date}
          autoComplete="off"
          onChange={(event) => setDate(event.target.value)}
        />
      </label>{' '}
      <label>
        {FORM.language}{' '}
        <select value={language} onChange={(event) => setLanguage(event.target.value)}>
          <option value="">{FORM.anyLanguage}</option>
          {READING_LANGUAGES.map((offered) => (
            <option key={offered.code} value={offered.code}>
              {offered.label}
            </option>
          ))}
        </select>
      </label>{' '}
      <button type="submit">{submitLabel}</button>
    </form>
  );
}

/**
 * The live screen: a form, and the state of the last reading asked. A reading is asked when the
 * reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left.
 */
export function LiveReading({ contract, fetchImpl }) {
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createReadingSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <ReadingForm onAsk={(request) => session.current.ask(request)} />
      <LiveAnswer>
        <ReadingAnswerView outcome={outcome} />
      </LiveAnswer>
    </div>
  );
}
